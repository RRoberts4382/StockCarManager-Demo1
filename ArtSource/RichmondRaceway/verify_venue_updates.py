import math
from pathlib import Path
import sys

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

root = Path(__file__).resolve().parent
sys.path.insert(0, str(root))
import richmond_layout as layout
import richmond_scenery as scene
from richmond_infield import DRIVEWAYS, GUEST_START

builder = root / 'build_richmond.py'
namespace = {'__file__': str(builder)}
exec(compile(builder.read_text(encoding='utf-8').split('bpy.ops.wm.read_factory_settings(use_empty=True)',1)[0],
             str(builder), 'exec'), namespace)
bpy.ops.wm.read_factory_settings(use_empty=True)
namespace['race_geometry']()
scene.build()
bpy.context.view_layer.update()

def bvh(objects):
    points, faces = [], []
    for obj in objects:
        offset = len(points)
        points.extend(obj.matrix_world @ v.co for v in obj.data.vertices)
        faces.extend(tuple(v+offset for v in p.vertices) for p in obj.data.polygons)
    return BVHTree.FromPolygons(points, faces)

def obj(name):
    return bpy.data.objects[name]

def down(tree, p):
    return tree.ray_cast((p[0],p[1],10), (0,0,-1))[0]

amenities = [o for o in bpy.context.scene.objects if o.name.startswith(
    ("Richmond pit walkway concession stand", "Richmond pit walkway portable toilets"))]
walkway = bvh([obj("Richmond pit walkway")])
fixtures = bvh(amenities)
centers = range(math.ceil(layout.PIT_LENGTH*2))
for sample in centers:
    pose = layout.pit_pose(.25+.5*sample)
    for p in range(9):
        offset = layout.lerp(layout.PIT_WALK_INNER+.04, layout.PIT_WORK_INNER-.04, p/8)
        point = pose.offset(side=offset)
        assert down(walkway, point) is not None, (sample, p)
        assert down(fixtures, point) is None, (sample, p)

walks = [o for o in bpy.context.scene.objects if o.name.startswith("Richmond pedestrian walk ")
         and o.name.endswith(" start") and int(o.name.split()[-2]) >= 31]
for start in walks:
    end = obj(start.name.removesuffix("start")+"end").location
    for i in range(31):
        p = start.location.lerp(end,i/30)
        assert down(walkway, p) is not None, (start.name, i, p)
        assert down(fixtures, p) is None, (start.name, i, p)

fan_stations = []
for o in bpy.context.scene.objects:
    if not o.name.startswith("Richmond standing fan ") or not 501 <= int(o.name.split()[-1]) < GUEST_START:
        continue
    station = layout.PIT.nearest(tuple(o.location[:2]))
    pose = layout.pit_pose(station)
    signed = (o.location.x-pose.x)*pose.nx + (o.location.y-pose.y)*pose.ny
    assert layout.PIT_WALL_START+8 < station < layout.PIT_WALL_END-8, o.name
    assert signed < layout.PIT_WALK_INNER-.45, (o.name,signed)
    fan_stations.append(station)
print("PIT WALKWAY", len(centers)*9, "clear pavement points;", len(walks),
      "animatable paths;", len(fan_stations), "protected standing spots;", len(amenities), "amenity groups")

camp_paving = [obj(f"Richmond {name} RV camping paving and spaces") for name in ("turn 3","backstretch")]
camp_rvs = [obj(f"Richmond {name} RV camping parked vehicles") for name in ("turn 3","backstretch")]
perimeter = bvh([obj("Richmond concrete perimeter walkway"), *camp_paving])
vehicles = bvh(camp_rvs)
for anchor, n in ((layout.marker(53,70),12), (layout.marker(177,24),15)):
    for i in range(-n*3,n*3+1):
        station = anchor+i
        for d in (-.15,-.05,0,.05,.15):
            point = scene.exterior_point(station,scene.PERIMETER_WALK_OUTER+d)
            assert down(perimeter,point) is not None, (station,d)
            assert down(vehicles,point) is None, (station,d)
print("RV lots and concrete walkway share both full-length edges; walkway is clear of RV bodies")
for light in bpy.context.scene.objects:
    if not light.name.startswith("Richmond floodlight "):
        continue
    center = tuple(light.location[:2])
    pose = layout.race_pose(layout.nearest_race(center))
    yaw = math.atan2(pose.ty,pose.tx)
    for x in range(-9,10):
        for y in range(-9,10):
            point = scene.local(center,yaw,x*.1,y*.1)
            assert down(vehicles,point) is None, (light.name,point)
print("All exterior floodlight footings clear the RV bodies")

road = obj("Richmond paved safety access and recovery yard")
access = obj("Turn one safety access asphalt")
surfaces = bvh([obj("Apron asphalt"),road,access])
gate = layout.turn_gate_access()
center = Vector((*gate.at(gate.length),0.))
dirn = Vector((*gate.heading(gate.length),0.))
normal = Vector((-dirn.y,dirn.x,0.))
for sample in range(-18,19):
    for dist in (-1.,-.15,-.01,.01,.15,1.):
        point = center+normal*(sample*.25)+dirn*dist
        hit = down(surfaces,point)
        assert hit is not None and .004 < hit.z < .029, (point,hit)
gate_pose = layout.race_pose(layout.RECOVERY_STATION)
for offset in (-23,-7,20):
    for sy in (10.2,10.8,11.4,12.0,12.6,13.2):
        for x in (-2.8,-1.4,0,1.4,2.8):
            point = gate_pose.offset(along=offset+x, side=-sy)
            hit = down(surfaces,point)
            assert hit is not None and -.002 < hit.z < .029, (offset,x,sy,hit)
print("PAVEMENT", 222, "full-width Turn One access seam probes and", 90,"backstretch apron tie-in probes passed")

garage = obj("Richmond infield operations garage")
pad = [v.co for v in garage.data.vertices[:8]]
sample_points = []
for i in range(4):
    for k in range(21):
        sample_points.append(pad[i].lerp(pad[(i+1)%4],k/20))
minimum = min(math.dist(p[:2],layout.EXIT.at(layout.EXIT.nearest(tuple(p[:2]))))
              for p in sample_points)-layout.PIT_WIDTH*.5
assert minimum > 9., minimum
print(f"OPERATIONS GARAGE PAD {minimum:.2f} m minimum clearance from pit exit asphalt")

camp_pavement = bvh([obj("Richmond infield campground roads and camping pads")])
seams = (("Turn Three", DRIVEWAYS[2][0], DRIVEWAYS[2][1], 2.3),
         ("Paddock", DRIVEWAYS[4][-1], DRIVEWAYS[4][-2], 2.3),
         ("East", DRIVEWAYS[5][-1], DRIVEWAYS[5][-2], 1.94))
for label, point, neighbor, half_width in seams:
    tx, ty = layout.unit(layout.sub(point, neighbor))
    for along in (-.8, -.4, 0., .4, .8):
        for offset in (-half_width, -half_width/2, 0., half_width/2, half_width):
            x = point[0] + tx*along - ty*offset
            y = point[1] + ty*along + tx*offset
            hit = down(camp_pavement, gate_pose.offset(along=x, side=-y))
            assert hit is not None and .014 < hit.z < .037, (label, x, y, hit)
print("CAMPGROUND DRIVEWAY SEAMS 75 probes across the Turn Three, Paddock, and East joins passed")
