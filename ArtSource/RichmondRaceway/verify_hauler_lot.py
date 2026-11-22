"""Check Richmond hauler footprints and native-equivalent tow paths in Blender."""
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

root = Path(__file__).resolve().parent
sys.path.insert(0, str(root))
import richmond_layout as layout
import richmond_scenery as scenery

bpy.ops.wm.read_factory_settings(use_empty=True)
driver = root / "build_richmond.py"
builder = {"__file__": str(driver)}
exec(compile(driver.read_text(encoding="utf-8").split(
    "bpy.ops.wm.read_factory_settings(use_empty=True)",1)[0], str(driver), "exec"), builder)
check_fbx = "--check-fbx" in sys.argv
if check_fbx:
    bpy.ops.import_scene.fbx(filepath=str(root/"RichmondRaceway.fbx"))
else:
    builder["race_geometry"]()
    scenery.build()

hauler_asset = Path(sys.argv[sys.argv.index("--hauler-fbx")+1])
earlier = set(bpy.context.scene.objects)
bpy.ops.import_scene.fbx(filepath=str(hauler_asset))
bpy.context.view_layer.update()
hauler_objects = [o for o in bpy.context.scene.objects if o not in earlier]
hauler_meshes = [o for o in hauler_objects if o.type == "MESH"]
corners = [o.matrix_world @ Vector(v) for o in hauler_meshes for v in o.bound_box]
lateral = (min(v.x for v in corners),max(v.x for v in corners))
lengthwise = (min(v.y for v in corners),max(v.y for v in corners))
assert abs(min(v.z for v in corners))<.01
tractor = next(obj for obj in hauler_meshes if obj.name.startswith("Tractor"))
trailer = next(obj for obj in hauler_meshes if obj.name.startswith("Trailer"))
assert sum((tractor.matrix_world @ Vector(v)).y for v in tractor.bound_box) > sum(
    (trailer.matrix_world @ Vector(v)).y for v in trailer.bound_box)

def tree(objects):
    points, faces = [], []
    for obj in objects:
        offset = len(points)
        points.extend(obj.matrix_world @ v.co for v in obj.data.vertices)
        faces.extend(tuple(offset+i for i in face.vertices) for face in obj.data.polygons)
    return BVHTree.FromPolygons(points, faces)

def down(bvh, point):
    return bvh.ray_cast((point[0],point[1],20.),(0,0,-1))[0]

gate = layout.race_pose(layout.RECOVERY_STATION)
yaw = math.atan2(gate.ty,gate.tx)
origin = (gate.x,gate.y)
inward = Vector((-gate.nx,-gate.ny,0.))

def planar(world):
    return scenery.local((0,0),-yaw,world[0]-origin[0],world[1]-origin[1])

def world(plan):
    return scenery.local(origin,yaw,*plan)

yard = bpy.data.objects["Richmond paved safety access and recovery yard"]
pavement = tree((yard,))
walks = tree(o for o in earlier if o.name.startswith("Richmond protected FanGrounds pedestrian paths"))
slots = sorted((o for o in earlier if o.name.startswith("Richmond hauler stall ")),key=lambda o:o.name)
assert [o.name for o in slots] == [f"Richmond hauler stall {i:02d}" for i in range(1,41)]

rectangles = []
floor_probes = 0
for marker in slots:
    center = marker.matrix_world.translation
    direction = marker.matrix_world.to_quaternion() @ Vector((0.,1.,0.))
    assert direction.dot(inward)>.999, (marker.name,direction)
    x,y = planar(center)
    x0,x1 = x+lateral[0],x+lateral[1]
    y0,y1 = y+lengthwise[0],y+lengthwise[1]
    rectangles.append((x0,x1,y0,y1))
    for i in range(7):
        for j in range(17):
            point = world((layout.lerp(x0,x1,i/6),layout.lerp(y0,y1,j/16)))
            hit = down(pavement,point)
            assert hit is not None and abs(hit.z-center.z)<.0002, (marker.name,point,hit)
            assert down(walks,point) is None, (marker.name,point,"on pedestrian path")
            floor_probes += 1

neighbor_gap = min(b[0]-a[1] for a,b in zip(rectangles,rectangles[1:]))
assert neighbor_gap > 1.6
for x in range(-42,50):
    for y in (121.08,121.5,122.0,122.5,122.92):
        hit = down(pavement,world((x,y)))
        assert hit is not None and .0289 < hit.z <= .034, (x,y,hit)

def route(name):
    return [planar(o.matrix_world.translation) for o in
            sorted((o for o in earlier if o.name.startswith(f"Richmond {name} ")),
                   key=lambda o:o.name)]

def hermite(a,b,tan_a,tan_b):
    # The native tow polyline uses TrackPreview.PitHermite at 24 samples per join.
    samples = []
    for i in range(25):
        t = i/24.
        samples.append(tuple((2*t**3-3*t*t+1)*a[k]+(t**3-2*t*t+t)*tan_a[k] +
                             (-2*t**3+3*t*t)*b[k]+(t**3-t*t)*tan_b[k] for k in range(2)))
    return samples

def catmull(points):
    # TrackPitSpline extrapolates its end controls instead of repeating them.
    expanded = [layout.sub(layout.mul(points[0],2),points[1]), *points,
                layout.sub(layout.mul(points[-1],2),points[-2])]
    curve = layout.catmull(expanded,False,16)
    return curve[16:-16]

def metric_dist(point, rect):
    x0,x1,y0,y1 = rect
    dx = max(x0-point[0],point[0]-x1,0.)
    dy = max(y0-point[1],point[1]-y1,0.)
    return math.hypot(dx,dy)

def curve_samples(points):
    path = layout.Path(points)
    count = math.ceil(path.length/.25)
    for i in range(count+1):
        yield path.at(path.length*i/count)

approaches = route("recovery approach")
bays = route("recovery bay")
departures = route("recovery depart")
entry = route("recovery entry")
leave = route("recovery exit")
amb = route("response outbound")
assert len(approaches)==len(bays)==len(departures)==40
assert all(abs(y-64)<.001 for _,y in bays)

entry_road = catmull(entry)
exit_road = catmull(leave)
minimum = {"loaded":math.inf,"returning":math.inf,"responding":math.inf}
trajectory_samples = 0
radius = 3.5  # Encloses the 6.3 m tow-truck body including mirrors and the following race car.
for approach, drop, depart in zip(approaches,bays,departures):
    direction = layout.unit(layout.sub(depart,drop))
    assert math.dist(drop,approach)>2 and math.dist(depart,drop)>8
    stop = layout.add(drop,layout.mul(direction,6.25))
    in_turn = min(15.,max(3.,math.dist(entry[-1],approach)*.6))
    out_turn = min(15.,max(3.,math.dist(leave[0],depart)*.6))
    loaded = hermite(entry[-1],approach,layout.mul(layout.unit(layout.sub(entry[-1],entry[-2])),in_turn),
                     layout.mul(direction,in_turn))
    returning = hermite(depart,leave[0],layout.mul(direction,out_turn),
                        layout.mul(layout.unit(layout.sub(leave[1],leave[0])),out_turn))
    segments = (("loaded",entry_road[:-1]+loaded+[stop]),
                ("returning",[stop]+returning+exit_road[1:]))
    for name, points in segments:
        for p in curve_samples(points):
            trajectory_samples += 1
            clearance = min(metric_dist(p,rect) for rect in rectangles)-radius
            minimum[name] = min(minimum[name],clearance)
            if p[1] >= 75.:
                assert down(pavement,world(p)) is not None, (name,p,"off pavement")

for p in curve_samples(catmull(amb)):
    minimum["responding"] = min(minimum["responding"],
                                min(metric_dist(p,rect) for rect in rectangles)-radius)
assert min(minimum.values()) >= 4.5, minimum

turn_four = layout.turn_four_gate_access()
turn_four_min = math.inf
turn_four_samples = math.ceil(turn_four.length/.25)
for i in range(turn_four_samples+1):
    distance = turn_four.length*i/turn_four_samples
    center = planar(turn_four.at(distance))
    tx, ty = turn_four.heading(distance)
    along = (tx*gate.tx+ty*gate.ty, -tx*gate.nx-ty*gate.ny)
    across = (-along[1],along[0])
    body = [layout.add(center,layout.add(layout.mul(along,dx),layout.mul(across,dy)))
            for dx in (-6.,6.) for dy in (-1.45,1.45)]
    for x0,x1,y0,y1 in rectangles:
        parked = [(x,y) for x in (x0-.20,x1+.20) for y in (y0-.20,y1+.20)]
        separation = -math.inf
        for ax,ay in ((1.,0.),(0.,1.),along,across):
            a = [x*ax+y*ay for x,y in body]
            b = [x*ax+y*ay for x,y in parked]
            separation = max(separation,min(b)-max(a),min(a)-max(b))
        assert separation > 0, (distance, center, (x0,x1,y0,y1))
        turn_four_min = min(turn_four_min,separation)

for connection in ((152.,108.),(193.,95.)):
    point = world(connection)
    nearest = turn_four.at(turn_four.nearest(point))
    assert math.dist(nearest,point)<2., (connection,nearest)

metrics = {
    "geometry": "reimported FBX" if check_fbx else "Blender source",
    "hauler_stalls": len(slots),
    "hauler_width_m": round(lateral[1]-lateral[0],4),
    "hauler_length_m": round(lengthwise[1]-lengthwise[0],4),
    "minimum_space_between_haulers_m": round(neighbor_gap,4),
    "verified_parking_surface_probes": floor_probes,
    "recovery_drops_retained": len(bays),
    "recovery_trajectory_probes": trajectory_samples,
    "clearance_to_3_5m_tow_envelope_m": {k: round(v,4) for k,v in minimum.items()},
    "turn_four_loaded_tow_probes": turn_four_samples+1,
    "turn_four_minimum_separating_axis_m": round(turn_four_min,4),
}
proof_name = "hauler-fbx-verification.json" if check_fbx else "hauler-source-verification.json"
(root/proof_name).write_text(json.dumps(metrics,indent=2)+"\n",encoding="utf-8")
print("RICHMOND HAULERS VERIFIED",json.dumps(metrics))
