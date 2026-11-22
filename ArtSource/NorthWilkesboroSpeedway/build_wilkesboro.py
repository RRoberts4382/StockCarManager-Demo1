"""Bake the editable North Wilkesboro scene, FBX and authored driving mesh."""

from pathlib import Path
import json
import math
import sys

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

OUT = Path(__file__).resolve().parent
sys.path.insert(0, str(OUT))
import wilkesboro_layout as g

def collection(name):
    result = bpy.data.collections.new("North Wilkesboro | " + name)
    bpy.context.scene.collection.children.link(result)
    return result


def material(name, rgb, roughness=.78):
    result = bpy.data.materials.get(name)
    if result:
        return result
    result = bpy.data.materials.new(name)
    result.diffuse_color = (*rgb, 1)
    result.use_nodes = True
    result.use_backface_culling = True
    shader = result.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = (*rgb, 1)
    shader.inputs["Roughness"].default_value = roughness
    return result


def mesh(name, vertices, faces, materials, target, material_indices=None):
    data = bpy.data.meshes.new(name)
    data.from_pydata(vertices, [], faces)
    data.update()
    obj = bpy.data.objects.new(name, data)
    target.objects.link(obj)
    for mat in materials:
        data.materials.append(mat)
    if material_indices is not None:
        for polygon, index in zip(data.polygons, material_indices):
            polygon.material_index = index
    return obj


def box(name, location, size, mat, target, rotation=0.):
    x, y, z = (length / 2. for length in size)
    c, s = math.cos(rotation), math.sin(rotation)
    vertices = [(location[0] + c*a - s*b, location[1] + s*a + c*b, location[2] + d)
                for d in (-z, z) for a, b in ((-x, -y), (x, -y), (x, y), (-x, y))]
    return mesh(name, vertices, ((3, 2, 1, 0), (4, 5, 6, 7), (0, 1, 5, 4),
                                 (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)), [mat], target)


def distances(start, end, spacing, closed=False):
    count = math.ceil((end - start) / spacing)
    return [g.lerp(start, end, i / count) for i in range(count if closed else count + 1)]


def checked_surface(name, verts, triangles, mat, target, mats=None):
    facing = []
    for face in triangles:
        a, b, c = (verts[i] for i in face)
        area_z = (b[0]-a[0]) * (c[1]-a[1]) - (b[1]-a[1]) * (c[0]-a[0])
        if abs(area_z) < 1e-8:
            raise ValueError(f"{name} degenerate projected triangle {face}")
        facing.append(face if area_z > 0 else (face[0], face[2], face[1]))
    obj = mesh(name, verts, facing, mat if isinstance(mat, list) else [mat], target, mats)
    for tri in obj.data.polygons:
        if tri.normal.z <= 0.:
            raise ValueError(f"{name} triangle {tri.index} points down")
    uv_layer = obj.data.uv_layers.new(name="World parallel UV")
    for loop in obj.data.loops:
        x, y, _ = obj.data.vertices[loop.vertex_index].co
        uv_layer.data[loop.index].uv = (x / 8., y / 8.)
    obj["pavement_surface"] = True
    return obj


def ribbon(name, stations, edges, mat, target, closed=False):
    vertices = [point for station in stations for point in edges(station)]
    triangles = []
    for i in range(len(stations) if closed else len(stations)-1):
        a = 2*i
        b = 2*((i+1) % len(stations))
        triangles.extend(((a, b, a+1), (a+1, b, b+1)))
    return checked_surface(name, vertices, triangles, mat, target)


def race_strip(name, start, end, sides, mat, target, raised=0., closed=False):
    def edges(s):
        return [add_height(g.racing_surface(s, lane), raised) for lane in sides(g.race_pose(s))]
    return ribbon(name, distances(start, end, 1.0, closed), edges, mat, target, closed)


def add_height(point, extra):
    return point[0], point[1], point[2] + extra


def pit_strip(name, story, start, end, low, high, material, target, level=.02):
    def edges(s):
        x, y = story.at(s)
        tx, ty = story.heading(s)
        z = level(s) if callable(level) else level
        return [(x + ty * offset, y - tx * offset, z) for offset in (low, high)]
    return ribbon(name, distances(start, end, .8), edges, material, target)


def wall(name, stations, edge, frame, width, height, outward, mats, target, closed=False):
    points = []
    for s in stations:
        x, y, z = edge(s)
        nx, ny = frame(s)
        far = (x + nx * width * outward, y + ny * width * outward)
        points.extend(((x, y, z), (*far, z), (*far, z+height), (x, y, z+height)))
    faces, materials = [], []
    for i in range(len(stations) if closed else len(stations)-1):
        a, b = 4*i, 4*((i+1) % len(stations))
        for j in range(4):
            k = (j+1)%4
            face = (a+j, b+j, b+k, a+k)
            faces.append(face if outward > 0 else face[::-1])
            materials.append(1 if len(mats)>1 and j==3 and (i//12)%12 in (2, 3) else 0)
    if not closed:
        for indices in ((0, 1, 2, 3), (len(points)-1, len(points)-2, len(points)-3, len(points)-4)):
            faces.append(indices if outward > 0 else indices[::-1])
            materials.append(0)
    return mesh(name, points, faces, mats, target, materials)


def fence(stations, wall_cap, target):
    steel = material("North Wilkesboro catch fence steel", (.26, .28, .29))
    vertices, faces = [], []
    def post_box(location, size, yaw):
        x, y, z = (length/2 for length in size)
        c, s = math.cos(yaw), math.sin(yaw)
        i = len(vertices)
        vertices.extend((location[0]+c*a-s*b, location[1]+s*a+c*b, location[2]+d)
                        for d in (-z,z) for a,b in ((-x,-y),(x,-y),(x,y),(-x,y)))
        faces.extend(tuple(i+v for v in face) for face in ((3,2,1,0),(4,5,6,7),(0,1,5,4),
                                                            (1,2,6,5),(2,3,7,6),(3,0,4,7)))
    for i, station in enumerate(stations):
        start = Vector(wall_cap(station))
        end = Vector(wall_cap(stations[(i+1) % len(stations)]))
        mid = (end + start)*.5
        yaw = math.atan2(end.y-start.y, end.x-start.x)
        post_box(start + Vector((0., 0., 1.63)), (.106, .106, 3.26), yaw)
        along = (end - start).normalized()
        across = Vector((-math.sin(yaw), math.cos(yaw), 0.))
        up = along.cross(across)
        for elevation in (.22, 1.45, 3.17):
            center = mid + Vector((0., 0., elevation))
            span = (end - start).length * .5
            index = len(vertices)
            vertices.extend(center + along * a + across * b + up * d
                            for d in (-.02, .02) for a, b in
                            ((-span, -.016), (span, -.016), (span, .016), (-span, .016)))
            faces.extend(tuple(index + v for v in face) for face in
                         ((3,2,1,0), (4,5,6,7), (0,1,5,4), (1,2,6,5), (2,3,7,6), (3,0,4,7)))
    mesh("Catch fence open posts and rails", vertices, faces, [steel], target)




def road_borders(path, s, a, b, raise_by=0.):
    p = g.road_pose(path,s)
    return p.offset(side=a,height=raise_by),p.offset(side=b,height=raise_by)


def track_frame(station):
    p = g.race_pose(station)
    return p.nx,p.ny


def pit_frame(station):
    p = g.pit_pose(station)
    return p.nx,p.ny


def ground_point(station, side):
    x,y,_ = g.racing_surface(station,side)
    return x,y,g.deck_height(station,side)


def infield_deck(pavement, target):
    rim = distances(0.,g.LAP,.90,True)
    center = g.diagram_xy(348.,228.)
    vertices = []
    rim_count = len(rim)
    inner_end = 53.
    start_sides = [-(g.WIDTH*.5+g.race_pose(s).apron) for s in rim]
    # Full asphalt from the inside apron to the garage, paddock and hauler area.
    # A sampled near-pit band joins each sloped ribbon without an exposed edge.
    for lateral in range(97):
        vertices.extend(ground_point(s,g.lerp(start,-inner_end,lateral/96))
                        for s,start in zip(rim,start_sides))
    last_ring = vertices[-rim_count:]
    for weight in (.2,.4,.6,.8):
        for x,y,_ in last_ring:
            qx,qy = g.lerp(x,center[0],weight),g.lerp(y,center[1],weight)
            vertices.append((qx,qy,g.site_height(qx,qy)))
    last_index = len(vertices)
    vertices.append((*center,g.site_height(*center)))
    faces = []
    for rib in range(100):
        cur = rib*rim_count
        far = cur+rim_count
        for i in range(rim_count):
            j = (i+1)%rim_count
            faces.extend(((cur+i,far+i,cur+j),(cur+j,far+i,far+j)))
    for i in range(rim_count):
        a = 100*rim_count + i
        b = 100*rim_count + (i+1)%rim_count
        faces.append((a,last_index,b))
    return checked_surface("Infield asphalt",vertices,faces,pavement,target)


def terrain(grass, outer_ground, target):
    stations = distances(0.,g.LAP,2.,True)
    inner = g.WIDTH*.5
    shoulders = (0.,.55,2.,4.,7.,14.,27.,51.,85.,142.,226.,326.)
    def bounds(station, additional):
        p = g.race_pose(station)
        e = g.racing_surface(station,inner)
        x,y = e[0]+p.nx*additional,e[1]+p.ny*additional
        z = g.lerp(e[2]-.025,g.site_height(x,y)-.025,g.ease(additional/5.5))
        return x,y,z
    surfaces = []
    for a,b in zip(shoulders,shoulders[1:]):
        material_choice = outer_ground if b < 7.1 else grass
        surfaces.append(ribbon(f"Outer terrain {a:05.1f}–{b:05.1f} m", stations,
            lambda s: (bounds(s,a),bounds(s,b)), material_choice,target,True))
    return surfaces


def stall_wall(s):
    return g.pit_pose(s).offset(side=g.PIT_WORK_INNER)


def race_from_pit(s):
    point = g.PIT.at(s)
    r = g.nearest_race(point)
    return r + g.LAP if r < g._road_start - 60. else r


def wall_spans(start,end,gates):
    begin = start
    for center,width in sorted(gates):
        a,b = center-width/2,center+width/2
        if a > begin:
            yield begin,a
        begin = max(begin,b)
    if begin < end:
        yield begin,end


_reference_inner = []
for arc in g.TRACE["insideWallArcs"]:
    for pixel in arc:
        xy = g.diagram_xy(pixel)
        station = g.nearest_race(xy)
        side = math.dist(xy,g.TRACK.at(station))
        _reference_inner.append((station,side))
_reference_inner.sort()


def inner_wall_point(station):
    s = station % g.LAP
    side = -max(15.10,g.sample_keys(_reference_inner,s))
    pit_side = 0.
    start_extension = g.LAP + g._road_start
    if station <= g._pit_exit_target:
        fade = g.quintic((g._pit_exit_target + 12. - station)/12.)
        pit_side = math.dist(g.road_xy(station),g.TRACK.at(s))*fade
    elif station <= g._pit_exit_target + 12.:
        fade = g.quintic((g._pit_exit_target + 12. - station)/12.)
        pit_side = (g.WIDTH + g.race_pose(s).apron)*.5*fade
    elif g.LAP + g.ENTRY_STATION < station <= start_extension:
        pit_side = math.dist(g.road_xy(station-g.LAP),g.TRACK.at(s))
    if pit_side:
        side = min(side,-pit_side-g.PIT_WIDTH*.5-2.)
    x,y,z = ground_point(s,side)
    from_east = g.quintic((g._road_end+18.-station)/18.)
    from_west = g.quintic((station-start_extension+18.)/18.)
    if max(from_east,from_west) > .0001:
        east = from_east > from_west
        p = g.road_pose(g.PIT,g.PIT.length if east else 0.)
        d = station-(g._road_end if east else start_extension)
        extrap = p.offset(along=d,side=g.PIT_WORK_INNER)
        x,y,z = (g.lerp(a,b,max(from_east,from_west)) for a,b in zip((x,y,z),extrap))
    return x,y,z

OUTSIDE_WALL_WIDTH = .54


def outside_wall_edge(station):
    return g.racing_surface(station, g.WIDTH * .5)


def catch_fence(target):
    def wall_cap(station):
        x, y, z = outside_wall_edge(station)
        nx, ny = track_frame(station)
        return x + nx * OUTSIDE_WALL_WIDTH * .5, y + ny * OUTSIDE_WALL_WIDTH * .5, z + g.WALL_HEIGHT

    fence(distances(0., g.LAP, 4.25, True), wall_cap, target)


def walls(track_walls,pit_walls,concrete,accent):
    wall("Outside track wall",distances(0.,g.LAP,.8,True),outside_wall_edge,track_frame,OUTSIDE_WALL_WIDTH,g.WALL_HEIGHT,1,
         [concrete,accent],track_walls,True)
    catch_fence(track_walls)
    wall("Pit track divider",distances(g.DIVIDER_START,g.DIVIDER_END,.8),g.divider_edge,track_frame,
         g.DIVIDER_WIDTH,1.02,1,[concrete,accent],pit_walls)
    for i,(a,b) in enumerate(wall_spans(0.,g.PIT_LENGTH,g.PIT_GARAGE_GATES),1):
        wall(f"Pit stall side wall {i:02d}",distances(a,b,.8),stall_wall,pit_frame,.45,1.04,-1,[concrete],pit_walls)
    # Beyond the stall row the inside wall follows the same painted inner-wall
    # alignment as the plan. These caps are deliberate paved vehicle portals.
    upper_gates = ((g.LAP+(g.EAST_GATE_START+g.EAST_GATE_END)*.5,
                    g.EAST_GATE_END-g.EAST_GATE_START),
                   (g.LAP+g.marker(291.,176.),15.2))
    for i,(a,b) in enumerate(wall_spans(g._road_end,g.LAP+g._road_start,upper_gates),1):
        wall(f"Infield perimeter wall {i:02d}",distances(a,b,.8),inner_wall_point,track_frame,.48,1.04,-1,[concrete],pit_walls)


def race_markings(white,amber,black,target):
    race_strip("Inner white line",0.,g.LAP,lambda p:(-p.width*.5+.07,-p.width*.5+.19),white,target,.014,True)
    race_strip("Apron boundary amber",0.,g.LAP,lambda p:(-p.width*.5-.43,-p.width*.5-.26),amber,target,.014,True)
    race_strip("Outside white stripe",0.,g.LAP,lambda p:(p.width*.5-.33,p.width*.5-.23),white,target,.012,True)
    cells,across = 3,34
    xyz,faces,indices = [],[],[]
    for row in range(cells):
        a,b = (row-cells/2)*.45,(row+1-cells/2)*.45
        for col in range(across):
            inner = -g.WIDTH*.5 + g.WIDTH*col/across
            outer = -g.WIDTH*.5 + g.WIDTH*(col+1)/across
            index = len(xyz)
            xyz.extend(add_height(g.racing_surface(s,t),.024) for s,t in ((a,inner),(b,inner),(b,outer),(a,outer)))
            faces.extend(((index,index+1,index+2),(index,index+2,index+3)))
            indices.extend(((row+col)%2,)*2)
    checked_surface("Start finish checker North Wilkesboro",xyz,faces,[white,black],target,indices)
    for zone,name in ((g.RESTART_START,"Start"),(g.RESTART_END,"End")):
        s = zone*g.LAP
        race_strip(f"Restart zone {name}",s-.21,s+.21,
            lambda p:(p.width*.5-3.3,p.width*.5-.55),amber,target,.023)


def pit_markings(pit_material,concrete,white,amber,target,pit_paving):
    def level(s):
        return g.road_height(g.PIT,s) + .016
    for i,(a,b) in enumerate(g.PIT_STALL_RANGES):
        pit_strip(f"Concrete pit box {i+1:02d}",g.PIT,a+.08,b-.08,-g.PIT_WIDTH*.5+.20,-1.96,
                  concrete,pit_paving,lambda s:g.road_height(g.PIT,s)+.011)
        pit_strip(f"Pit stall stripe {i+1:02d}",g.PIT,a,a+.105,-g.PIT_WIDTH*.5+.23,-1.93,white,target,level)
        pit_strip(f"Pit stall parking baseline {i+1:02d}",g.PIT,a,b,-2.05,-1.96,white,target,level)
    for fraction,name in ((g.PIT_SPEED_START,"start"),(g.PIT_SPEED_END,"end")):
        s = g.PIT.length*fraction
        pit_strip(f"Pit speed line {name}",g.PIT,s-.18,s+.18,-g.PIT_WIDTH*.5,g.PIT_WIDTH*.5,amber,target,level)
    for i,(a,b) in enumerate(wall_spans(0.,g.PIT_LENGTH,g.PIT_GARAGE_GATES),1):
        pit_strip(f"Pit walkway concrete {i:02d}",g.PIT,a,b,g.PIT_WALK_INNER,g.PIT_WORK_INNER-.44,concrete,
                  pit_paving,lambda s:g.road_height(g.PIT,s)+.032)
    pit_strip("Pit support apron",g.PIT,.05,g.PIT_LENGTH-.05,g.PIT_WORK_INNER,-g.PIT_WIDTH*.5-.02,
              pit_material,pit_paving,lambda s:g.road_height(g.PIT,s)+.004)


def race_geometry():
    road = collection("Banked course and apron")
    site = collection("Ground and concourses")
    track_walls = collection("Outside wall and catch fencing")
    pit_walls = collection("Inside walls and access")
    pit_paving = collection("Pit road and crew work bays")
    painted = collection("Race and pit markings")

    asphalt = material("North Wilkesboro circuit asphalt",(.115,.113,.107),.90)
    pit_asphalt = material("North Wilkesboro pit road asphalt",(.129,.127,.119),.88)
    infield_asphalt = material("North Wilkesboro paved infield",(.150,.148,.141))
    concrete = material("North Wilkesboro barrier concrete",(.64,.65,.635))
    pit_concrete = material("North Wilkesboro pit box concrete",(.221,.218,.207))
    grass = material("North Wilkesboro short grass",(.138,.258,.075),.98)
    earth = material("North Wilkesboro outer earth",(.21,.19,.136))
    white = material("North Wilkesboro chalk white",(.83,.82,.76))
    amber = material("North Wilkesboro safety yellow",(.92,.65,.099))
    black = material("North Wilkesboro black marking",(.028,.031,.029))
    navy = material("North Wilkesboro barrier navy",(.07,.15,.26))

    main = race_strip("Racing surface",0.,g.LAP,lambda p:(-p.width*.5,p.width*.5),asphalt,road,closed=True)
    apron = race_strip("Apron asphalt",0.,g.LAP,lambda p:(-p.width*.5-p.apron,-p.width*.5),asphalt,road,closed=True)
    paths = [(main,0.),(apron,0.)]
    for name,path in (("Pit entrance asphalt",g.ENTRY),("Pit road asphalt",g.PIT),("Pit exit asphalt",g.EXIT)):
        obj = pit_strip(name,path,0.,path.length,-g.PIT_WIDTH*.5,g.PIT_WIDTH*.5,pit_asphalt,pit_paving,
                        lambda s:g.road_height(path,s)+.004)
        paths.append((obj,.004))
    infield = infield_deck(infield_asphalt,site)
    land = terrain(grass,earth,site)
    walls(track_walls,pit_walls,concrete,navy)
    race_markings(white,amber,black,painted)
    pit_markings(pit_asphalt,pit_concrete,white,amber,painted,pit_paving)
    return [obj for obj,_ in paths]+[infield]+land,track_walls,pit_walls


def camera(name, position, aim, scale=None):
    o = bpy.data.objects.new(name,bpy.data.cameras.new(name))
    bpy.context.scene.collection.objects.link(o)
    o.location = position
    o.rotation_euler = (Vector(aim)-o.location).to_track_quat("-Z","Y").to_euler()
    o.data.clip_end = 3300.
    if scale is not None:
        o.data.type = "ORTHO"
        o.data.ortho_scale = scale
    else:
        o.data.lens = 39.
    return o


def viewpoints():
    overhead = camera("Reference-matched overhead",(-5,96,700),(-5,96,0),590.)
    overhead.rotation_euler = (0.,0.,0.)
    return [
        ("overhead",overhead),
        ("venue",camera("Grandstands and venue portrait",(325.,-515.,500.),(-10.,100.,3.),590.)),
        ("frontstretch",camera("Start/finish from the racing surface",(-55.,-.05,7.2),(55.,-5.,5.))),
        ("pit-entry",camera("Backstretch pit entrance",(-287.,278.,112.),(-154.,147.,2.),205.)),
        ("pit-exit",camera("Pit exit and first two turns",(257.,-61.,114.),(135.,65.,2.),198.)),
        ("backstretch",camera("Campgrounds and backstretch",(167.,332.,90.),(-35.,190.,5.),314.)),
    ]


def architecture_viewpoints():
    views = []
    for label,pixel,model,along,inset,height,look_height,lens in (
            ("press-building",(231.5,305.4),"North Wilkesboro pressed-steel and glass press building",
             21.5,5.75,6.60,7.0,20.),
            ("scoreboard",(198.5,192.1),"North Wilkesboro trackside leaderboard housing",
             3.,30.,10.05,5.8,59.)):
        asset = bpy.data.objects.get(model)
        if asset is None:
            continue
        cx,cy = g.diagram_xy(pixel)
        floor = min((asset.matrix_world@v.co).z for v in asset.data.vertices)
        station = g.marker(*pixel)
        viewer = g.race_pose(station+along)
        x,y,z = viewer.offset(side=-inset,height=height)
        angle = camera("Press facade from racing line" if label=="press-building" else "Scoreboard face from infield",
                       (x,y,z),(cx,cy,floor+look_height))
        angle.data.lens = lens
        views.append((label,angle))
    return views

def render_views(scene,views):
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.render_aa = "32"
    shade = scene.display.shading
    shade.light = "STUDIO"
    shade.studio_light = "paint.sl"
    shade.color_type = "MATERIAL"
    shade.background_type = "WORLD"
    if scene.world is None:
        scene.world = bpy.data.worlds.new("North Wilkesboro daylight")
    scene.world.color = (.29,.50,.73)
    shade.show_shadows = False
    shade.show_cavity = True
    shade.cavity_type = "BOTH"
    shade.show_object_outline = False
    if hasattr(shade,"show_backface_culling"):
        shade.show_backface_culling = True
    scene.render.resolution_x = 1620
    scene.render.resolution_y = 1070
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    scene.view_settings.exposure = .56
    for label, angle in views:
        scene.camera = angle
        scene.render.filepath = str(OUT/f"north-wilkesboro-{label}.png")
        bpy.ops.render.render(write_still=True)


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene["Venue"] = "North Wilkesboro Speedway, North Wilkesboro, North Carolina"
    scene["Reference layout"] = "Owner's seven-layer North Wilkesboro.pdn"
    scene["Authoritative lap distance meters"] = g.LAP
    scene["Pit posted speed limit mph"] = 30
    scene["Qualifying benchmark seconds"] = g.BENCHMARK_SECONDS
    scene["Modeled longitudinal relief meters"] = 4.
    surfaces, track_walls, pit_walls = race_geometry()
    bpy.context.view_layer.update()
    if "--geometry-only" not in sys.argv:
        import north_wilkesboro_scenery as scenery
        scenery.build(surfaces)
    bpy.context.view_layer.update()
    marker = bpy.data.objects.new("North Wilkesboro import orientation", None)
    scene.collection.objects.link(marker)
    marker.location = (10.,10.,g.site_height(10.,10.))
    marker.empty_display_size = .4
    views = viewpoints() + architecture_viewpoints()
    scene.camera = views[1][1]
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.color_type = "MATERIAL"
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/"NorthWilkesboroSpeedway.blend"))
    bpy.ops.export_scene.fbx(filepath=str(OUT/"NorthWilkesboroSpeedway.fbx"),axis_forward="-Z",axis_up="Y",
                             use_mesh_modifiers=True,object_types={"MESH","EMPTY"},add_leaf_bones=False)
    if "--no-render" not in sys.argv:
        render_views(scene,views)


if __name__ == "__main__":
    if "--architecture-previews" in sys.argv:
        render_views(bpy.context.scene,architecture_viewpoints())
    else:
        main()
