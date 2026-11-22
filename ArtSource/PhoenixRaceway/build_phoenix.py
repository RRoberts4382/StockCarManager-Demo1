"""Build the editable Phoenix Raceway venue and Unity FBX from the authored source paths."""
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Vector
from mathutils.geometry import tessellate_polygon

OUT = Path(__file__).resolve().parent
sys.path.insert(0, str(OUT))

import phoenix_layout as layout
import phoenix_scenery

def collection(name):
    result = bpy.data.collections.new("Phoenix | " + name)
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
    return [layout.lerp(start, end, i / count) for i in range(count if closed else count + 1)]


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
        return [add_height(layout.racing_surface(s, lane), raised) for lane in sides(layout.race_pose(s))]
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


def fence(stations, race_edge, target):
    from phoenix_scenery import Builder
    posts = Builder(target)
    for i, station in enumerate(stations):
        p = layout.race_pose(station)
        x, y, z = race_edge(station)
        next_station = stations[(i+1) % len(stations)]
        end = Vector(race_edge(next_station))
        start = Vector((x, y, z))
        center = (end + start) * .5
        yaw = math.atan2(end.y - y, end.x - x)
        posts.box((x, y), z, .115, .115, 3.45, yaw, "Fixture steel")
        for elevation in (.25, 1.5, 3.3):
            posts.box((center.x, center.y), center.z+elevation, (end-start).length, .042, .050, yaw, "Fixture steel")
    posts.bake("Outside catch fence posts and rails")


def race_geometry():
    paved = collection("Driving surfaces")
    painted = collection("Track and pit markings")
    barriers = collection("Walls and catch fence")
    terrain = collection("Desert terrain")
    asphalt = material("Phoenix circuit asphalt", (.091, .089, .087))
    apron_mat = material("Phoenix apron asphalt", (.108, .103, .098))
    pit_asphalt = material("Phoenix pit asphalt", (.125, .118, .110))
    infield_mat = material("Phoenix infield asphalt", (.14, .131, .120))
    concrete = material("Phoenix pit concrete", (.43, .409, .378))
    walkway_mat = material("Phoenix walkway concrete", (.53, .49, .41))
    white = material("Phoenix checker white and lane paint", (.87, .86, .80))
    black = material("Phoenix checker black", (.017, .018, .018))
    gold = material("Phoenix amber paint", (.95, .69, .055))
    teal = material("Phoenix teal safety stripe", (.02, .49, .61))
    wall_mat = material("Phoenix wall concrete", (.62, .59, .52))
    trim = material("Phoenix wall turquoise panels", (.018, .32, .42))
    beige = material("Phoenix desert gravel", (.36, .267, .183))
    box("Sonoran gravel site", (0., 0., -.23), (1430., 1060., .22), beige, terrain)

    race_strip("Racing surface", 0., layout.LAP, lambda p: (-p.width/2, p.width/2), asphalt, paved, closed=True)
    race_strip("Apron asphalt", 0., layout.LAP, lambda p: (-p.width/2-p.apron, -p.width/2),
               apron_mat, paved, closed=True)
    ring = [layout.racing_surface(s, -layout.race_pose(s).width/2-layout.race_pose(s).apron+0.10)
            for s in distances(0., layout.LAP, 1.75, True)]
    ring = [(x, y, -.005) for x, y, _ in ring]
    vectors = [Vector(point) for point in ring]
    infield_faces = tessellate_polygon([vectors])
    checked_surface("Asphalt infield", ring, infield_faces, infield_mat, paved)

    half = layout.PIT_WIDTH/2.
    pit_strip("Pit road asphalt", layout.PIT, 0., layout.PIT.length, -half, half, pit_asphalt, paved)
    pit_strip("Pit entrance asphalt", layout.ENTRY, 0., layout.ENTRY.length, -half, half, pit_asphalt, paved,
              lambda s: .02 * layout.ease(s/layout.ENTRY.length))
    pit_strip("Pit exit asphalt", layout.EXIT, 0., layout.EXIT.length, -half, half, pit_asphalt, paved,
              lambda s: .02 * (1. - layout.ease(s/layout.EXIT.length)))

    wall_start, wall_end = layout.STALL_START-3., layout.STALL_END+3.
    pit_strip("Phoenix pit work concrete", layout.PIT, wall_start, wall_end, -14.6, -half,
              concrete, paved, .025)
    pit_strip("Phoenix pit walkway", layout.PIT, wall_start-10., wall_end+10., -17.648, -14.6,
              walkway_mat, paved, .025)
    for i in range(layout.STALL_COUNT + 1):
        s = layout.STALL_START + i * layout.STALL_SPACING
        pit_strip(f"Painted pit stall divider {i+1:02d}", layout.PIT, s-.065, s+.065, -half+.12, -.6,
                  white, painted, .031)
    pit_strip("Painted pit stall lane line", layout.PIT, layout.STALL_START, layout.STALL_END,
              -.75, -.6, white, painted, .031)
    for fraction, name in ((layout.PIT_SPEED_START, "Pit speed begin"), (layout.PIT_SPEED_END, "Pit speed end")):
        s = fraction * layout.PIT_LENGTH
        pit_strip(name, layout.PIT, s-.17, s+.17, -half+.12, half-.12, gold, painted, .035)
    for interval_start in range(4, int(layout.PIT_LENGTH)-4, 16):
        pit_strip(f"Pit through-lane hash {interval_start:03d}", layout.PIT, interval_start, interval_start+5,
                  2.3, 2.41, white, painted, .03)

    def outer_edge(s):
        return layout.racing_surface(s, layout.race_pose(s).width/2)

    def outward(s):
        p = layout.race_pose(s)
        return p.nx, p.ny

    def inner_wall_edge(s):
        p = layout.race_pose(s)
        return layout.racing_surface(s, -p.width/2-p.apron-1.2)

    def pit_wall_edge(s):
        return layout.pit_pose(s).offset(side=-half)

    def pit_frame(s):
        p = layout.pit_pose(s)
        return p.nx, p.ny

    wall("Outside continuous concrete wall", distances(0., layout.LAP, 1.25, True),
         outer_edge, outward, .55, layout.WALL_HEIGHT, 1, [wall_mat, trim], barriers, True)
    wall("Pit continuous stall-side wall", distances(wall_start, wall_end, 1.25), pit_wall_edge,
         pit_frame, .3, .99, -1, [wall_mat], barriers)
    # Opposite the U-shaped pit complex, the inside apron is protected along
    # the turn and the back straight. Leave both connector approaches open.
    wall("Inside apron concrete wall", distances(layout.EXIT_STATION+65., layout.ENTRY_STATION-80., 1.25),
         inner_wall_edge, outward, .44, 1.03, -1, [wall_mat], barriers)

    def fence_base(s):
        p = layout.race_pose(s)
        return add_height(layout.racing_surface(s, p.width/2+.28), layout.WALL_HEIGHT)

    fence(distances(0., layout.LAP, 4.85, True), fence_base, barriers)
    race_strip("Track inner white line", 0., layout.LAP,
               lambda p: (-p.width/2+.16, -p.width/2+.29), white, painted, .015, True)
    race_strip("Track inner amber line", 0., layout.LAP,
               lambda p: (-p.width/2+.43, -p.width/2+.57), gold, painted, .014, True)

    checker_verts, checker_faces, checker_colors = [], [], []
    for row in range(2):
        for col in range(20):
            points = []
            for s, index in ((-.47+row*.47, col), (-.47+(row+1)*.47, col),
                             (-.47+(row+1)*.47, col+1), (-.47+row*.47, col+1)):
                p = layout.race_pose(s)
                points.append(add_height(layout.racing_surface(s, (index/20-.5)*p.width), .023))
            i = len(checker_verts)
            checker_verts.extend(points)
            checker_faces.extend(((i, i+1, i+2), (i, i+2, i+3)))
            checker_colors.extend([(row+col)%2]*2)
    checked_surface("Start finish checker Phoenix", checker_verts, checker_faces, [white, black], painted, checker_colors)
    for station, label in ((layout.RESTART_START * layout.LAP, "Start"), (layout.RESTART_END * layout.LAP, "End")):
        race_strip(f"Restart zone {label}", station-.23, station+.23,
                   lambda p: (p.width/2-2.1, p.width/2-.08), teal, painted, .022)


def camera(name, location, target, scale):
    obj = bpy.data.objects.new(name, bpy.data.cameras.new(name))
    bpy.context.scene.collection.objects.link(obj)
    obj.location = location
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat('-Z', 'Y').to_euler()
    obj.data.type = 'ORTHO'
    obj.data.ortho_scale = scale
    obj.data.clip_end = 3500
    return obj


def inspect_export(path):
    scene = bpy.context.scene
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=str(path))
    checked_names = ("Racing surface", "Apron asphalt", "Pit road asphalt", "Pit entrance asphalt",
                     "Pit exit asphalt", "Asphalt infield", "Start finish checker", "Restart zone",
                     "Track inner", "Phoenix pit work concrete", "Phoenix pit walkway")
    metrics = {}
    for obj in scene.objects:
        if obj.type != 'MESH' or not obj.name.startswith(checked_names):
            continue
        obj.data.calc_loop_triangles()
        triangles = obj.data.loop_triangles
        for tri in triangles:
            a, b, c = (obj.matrix_world @ obj.data.vertices[i].co for i in tri.vertices)
            if (b-a).cross(c-a).z <= 0.:
                raise ValueError(f"Reimported {obj.name} triangle {tri.index} has invalid winding")
        metrics[obj.name] = len(triangles)
    if not all(any(name.startswith(prefix) for name in metrics) for prefix in checked_names):
        raise ValueError("An expected reimported pavement or marking mesh is missing")
    (OUT / "export-winding.json").write_text(json.dumps(metrics, indent=2), encoding="utf-8")
    print("PHOENIX FBX WINDING PASSED", sum(metrics.values()), "triangles")


bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene["Venue"] = "Phoenix Raceway, Avondale, Arizona"
scene["Reference layout"] = "User-supplied schematic and overhead, current post-2018 race direction"
scene["Authoritative lap distance meters"] = layout.LAP
scene["Cup qualifying benchmark seconds"] = layout.BENCHMARK_SECONDS
scene["Cup qualifying benchmark mph"] = layout.BENCHMARK_MPH
scene["Cup benchmark source"] = "https://www.nascar.com/news-media/2019/03/08/ryan-blaney-earns-busch-pole-award-at-ism-raceway/"
race_geometry()
phoenix_scenery.build()

overview = camera("Oblique overview", (540., -780., 630.), (0., 0., 0.), 790.)
overhead = camera("Overhead layout", (0., 0., 875.), (0., 0., 0.), 805.)
overhead.rotation_euler = (0., 0., 0.)
finish = layout.race_pose(0.)
look = Vector(finish.offset(along=28., side=-6., height=1.5))
approach = Vector(finish.offset(along=-62., side=23., height=24.))
turn_camera = camera("Start-finish and banking check", look + (approach - look) * 2.4, look, 124.)
scene.camera = overview
scene.render.engine = 'BLENDER_WORKBENCH'
shade = scene.display.shading
shade.light = 'STUDIO'
shade.color_type = 'MATERIAL'
shade.show_shadows = False
shade.show_cavity = True
shade.cavity_type = 'BOTH'
shade.show_object_outline = False
if hasattr(shade, 'show_backface_culling'):
    shade.show_backface_culling = True
scene.render.resolution_x = 1620
scene.render.resolution_y = 1110
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.view_settings.view_transform = 'Standard'
scene.view_settings.exposure = .8
scene.world = bpy.data.worlds.new("Phoenix preview sky")
scene.world.color = (.24, .27, .30)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT / "PhoenixRaceway.blend"))
bpy.ops.export_scene.fbx(filepath=str(OUT / "PhoenixRaceway.fbx"), object_types={'MESH'},
                         axis_forward='-Z', axis_up='Y', apply_unit_scale=True, use_mesh_modifiers=True,
                         bake_anim=False, add_leaf_bones=False)
if "--no-render" not in sys.argv:
    for viewpoint, filename in ((overview, "Phoenix-overview.png"), (overhead, "Phoenix-overhead.png"),
                                (turn_camera, "Phoenix-start-finish.png")):
        scene.camera = viewpoint
        scene.render.filepath = str(OUT / filename)
        bpy.ops.render.render(write_still=True)
inspect_export(OUT / "PhoenixRaceway.fbx")
