"""Build the editable Richmond Raceway venue and Unity FBX from the authored source paths."""
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

OUT = Path(__file__).resolve().parent
sys.path.insert(0, str(OUT))

import richmond_layout as layout
import richmond_scenery
from richmond_fan_grounding import ground_fan_anchors

def collection(name):
    result = bpy.data.collections.new("Richmond | " + name)
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
    steel = material("Richmond catch fence steel", (.26, .28, .29))
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
        start = Vector(race_edge(station))
        end = Vector(race_edge(stations[(i+1) % len(stations)]))
        mid = (end + start)*.5
        yaw = math.atan2(end.y-start.y, end.x-start.x)
        post_box(start + Vector((0., 0., 1.63)), (.106, .106, 3.26), yaw)
        for elevation in (.22, 1.45, 3.17):
            post_box(mid + Vector((0.,0.,elevation)), ((end-start).length, .032, .04), yaw)
    mesh("Catch fence open posts and rails", vertices, faces, [steel], target)


def race_geometry():
    paved = collection("Driving surfaces")
    painted = collection("Track and pit markings")
    barriers = collection("Walls and catch fence")
    terrain = collection("Infield and surrounding grounds")
    asphalt = material("Richmond circuit asphalt", (.092, .089, .086))
    concrete = material("Richmond pit concrete", (.43, .416, .392))
    walkway_mat = material("Richmond walkway concrete", (.48, .458, .427))
    white = material("Richmond checker white and lane paint", (.87, .86, .82))
    black = material("Richmond checker black", (.017, .018, .018))
    gold = material("Richmond amber paint", (.91, .64, .053))
    teal = material("Richmond blue restart stripe", (.028, .40, .63))
    wall_mat = material("Richmond white wall concrete", (.60, .604, .587))
    trim = material("Richmond wall navy panels", (.020, .083, .19))
    grass = material("Richmond short grass", (.147, .209, .084))
    box("Mown grass site", (0., 0., -.125), (1700., 1400., .18), grass, terrain)

    race_strip("Racing surface", 0., layout.LAP, lambda p: (-p.width/2, p.width/2), asphalt, paved, closed=True)

    def apron_edges(s):
        half_width = layout.race_pose(s).width / 2
        return (layout.racing_surface(s, -half_width - layout.paved_apron_width(s)),
                layout.racing_surface(s, -half_width))

    ribbon("Apron asphalt", distances(0., layout.LAP, 1., True), apron_edges, asphalt, paved, True)
    # The grass sits just below both roads with no visible step at the divider
    # and extends slightly under the merge tapers to hide its end seams.
    race_strip("Frontstretch grass median", layout.LAP*(layout.GRASS_TAPER_START-.012),
               layout.LAP*(1.+layout.GRASS_TAPER_END+.012),
               lambda p: (-p.width/2-p.apron, -p.width/2), grass, terrain, raised=-.004)

    # The asphalt ribbons overlap the flat apron. A few millimeters of draw
    # separation prevent coincident faces while preserving the driving datum.
    half = layout.PIT_WIDTH/2
    pit_strip("Pit road asphalt", layout.PIT, 0., layout.PIT.length, -half, half, asphalt, paved, .006)
    pit_strip("Pit entrance asphalt", layout.ENTRY, 0., layout.ENTRY.length, -half, half, asphalt, paved, .006)
    pit_strip("Pit exit asphalt", layout.EXIT, 0., layout.EXIT.length, -half, half, asphalt, paved, .006)
    gate_access = layout.turn_gate_access()
    pit_strip("Turn one safety access asphalt", gate_access, 0., gate_access.length,
              -5., 5., asphalt, paved, .008)
    _, _, pace_return_drive, pace_departure_drive = layout.pace_paths()
    pit_strip("Pace bay return asphalt", pace_return_drive, 0., pace_return_drive.length,
              -3.05, 3.05, asphalt, paved, .010)
    pit_strip("Pace bay departure asphalt", pace_departure_drive, 0., pace_departure_drive.length,
              -3.05, 3.05, asphalt, paved, .012)
    x, y = layout.PACE_BAY
    for side in (-1.95, 1.95):
        box("Pace bay side paint", (x, y+side, .022), (7., .12, .006), white, painted)
    box("Pace bay stop line", (x-3.5, y, .022), (.13, 4., .006), white, painted)
    turn_four_access = layout.turn_four_gate_access()
    blend_start = turn_four_access.length - 20.
    pit_strip("Turn four safety access asphalt", turn_four_access, 0., turn_four_access.length,
              -5., 5., asphalt, paved,
              lambda s: .008 + .027 * layout.ease((s - blend_start) / 20.))

    wall_start, wall_end = layout.PIT_WALL_START, layout.PIT_WALL_END
    pit_strip("Richmond pit work concrete", layout.PIT, wall_start, wall_end, layout.PIT_WORK_INNER, -half,
              concrete, paved, .012)
    pit_strip("Richmond pit walkway", layout.PIT, 0., layout.PIT_LENGTH,
              layout.PIT_WALK_INNER, layout.PIT_WORK_INNER, walkway_mat, paved, .012)
    # Access pavement makes the protected walkway reachable via both wall ends.
    for start, end, name in ((0., wall_start, "west"), (wall_end, layout.PIT_LENGTH, "east")):
        pit_strip(f"Pit walkway access {name}", layout.PIT, start, end, layout.PIT_WORK_INNER, -half,
                  concrete, paved, .012)
    for i in range(layout.STALL_COUNT + 1):
        s = layout.STALL_START + i * layout.STALL_SPACING
        pit_strip(f"Painted pit stall divider {i+1:02d}", layout.PIT, s-.065, s+.065, -half+.12, -.6,
                  white, painted, .021)
    pit_strip("Painted pit stall lane line", layout.PIT, layout.STALL_START, layout.STALL_END,
              -.75, -.6, white, painted, .021)
    for fraction, name in ((layout.PIT_SPEED_START, "Pit speed begin"), (layout.PIT_SPEED_END, "Pit speed end")):
        s = fraction * layout.PIT_LENGTH
        pit_strip(name, layout.PIT, s-.17, s+.17, -half+.12, half-.12, gold, painted, .024)
    for start in range(4, int(layout.PIT_LENGTH)-4, 16):
        pit_strip(f"Pit through-lane hash {start:03d}", layout.PIT, start, start+5,
                  2.3, 2.41, white, painted, .02)

    def outer_edge(s):
        return layout.racing_surface(s, layout.race_pose(s).width/2)

    def outward(s):
        p = layout.race_pose(s)
        return p.nx, p.ny

    pit_end = layout.PIT_LENGTH
    exit_end = pit_end + layout.EXIT.length
    entry_start = -layout.ENTRY.length
    pit_wall_head = layout.pit_wall_head()
    pit_wall_head_start = entry_start - pit_wall_head.length
    pit_wall_tail = layout.pit_wall_tail()

    def pit_wall_edge(s):
        if s < entry_start:
            return (*pit_wall_head.at(s - pit_wall_head_start), 0.)
        if s < 0.:
            return (*layout.pit_connector_inner_edge(layout.ENTRY, s - entry_start), 0.)
        if s <= pit_end:
            return layout.pit_pose(s).offset(side=-half)
        if s <= exit_end:
            return (*layout.pit_connector_inner_edge(layout.EXIT, s - pit_end), 0.)
        return (*pit_wall_tail.at(s - exit_end), 0.)

    def pit_frame(s):
        p = layout.pit_pose(s)
        return p.nx, p.ny

    def extended_pit_frame(s):
        if s < entry_start:
            tx, ty = pit_wall_head.heading(s - pit_wall_head_start)
        elif s < 0.:
            tx, ty = layout.ENTRY.heading(s - entry_start)
        elif s <= pit_end:
            return pit_frame(s)
        elif s <= exit_end:
            tx, ty = layout.EXIT.heading(s - pit_end)
        else:
            tx, ty = pit_wall_tail.heading(s - exit_end)
        return ty, -tx

    wall("Outside continuous concrete wall", distances(0., layout.LAP, 1.25, True),
         outer_edge, outward, .55, layout.WALL_HEIGHT, 1, [wall_mat, trim], barriers, True)
    stall_wall_stations = (distances(pit_wall_head_start, entry_start, .8)
                           + distances(entry_start, 0., .8)[1:] + distances(0., wall_start, .8)[1:]
                           + distances(wall_start, wall_end, 1.25)[1:] + distances(wall_end, pit_end, .8)[1:]
                           + distances(pit_end, exit_end, .8)[1:]
                           + distances(exit_end, exit_end + pit_wall_tail.length, .8)[1:])
    wall("Pit continuous stall-side wall", stall_wall_stations, pit_wall_edge,
         extended_pit_frame, .3, .99, -1, [wall_mat], barriers)
    wall("Pit separator wall track side", distances(layout.PIT_DIVIDER_START, layout.PIT_DIVIDER_END, .8),
         lambda s: layout.pit_pose(s).offset(side=layout.PIT_DIVIDER_EDGE),
         pit_frame, layout.PIT_DIVIDER_WIDTH, .99, 1, [wall_mat], barriers)
    # The inner barrier follows the pit stall side and rejoins the apron edge
    # past the paved turn access gates. The backstretch recovery gate stays open.
    opening_half = layout.RECOVERY_OPENING / 2
    for start, end, title in ((layout.TURN_WALL_START, layout.RECOVERY_STATION-opening_half, "east"),
                              (layout.RECOVERY_STATION+opening_half, layout.TURN_FOUR_WALL_END, "west")):
        wall(f"Inside apron wall {title}", distances(start, end, 1.25), layout.inner_apron_wall_edge,
             outward, .44, 1.03, -1, [wall_mat], barriers)

    def fence_base(s):
        p = layout.race_pose(s)
        return add_height(layout.racing_surface(s, p.width/2+.28), layout.WALL_HEIGHT)

    # Carry the bank onto an outside embankment so the elevated racing edge
    # and exterior wall are visibly supported above the surrounding site.
    def shoulder(s):
        p = layout.race_pose(s)
        crest = layout.racing_surface(s, p.width/2+.57)
        toe = layout.racing_surface(s, p.width/2+4.2)
        return crest, (toe[0], toe[1], -.033)
    ribbon("Outer banking embankment", distances(0., layout.LAP, 1.0, True),
           shoulder, grass, terrain, True)
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
    checked_surface("Start finish checker Richmond", checker_verts, checker_faces, [white, black], painted, checker_colors)
    for station, label in ((layout.RESTART_START * layout.LAP, "Start"), (layout.RESTART_END * layout.LAP, "End")):
        race_strip(f"Restart zone {label}", station-.23, station+.23,
                   lambda p: (p.width/2-2.1, p.width/2-.08), teal, painted, .022)


def camera(name, location, target, scale):
    obj = bpy.data.objects.new(name, bpy.data.cameras.new(name))
    bpy.context.scene.collection.objects.link(obj)
    obj.location = location
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat('-Z', 'Y').to_euler()
    if scale is None:
        obj.data.type = 'PERSP'
        obj.data.lens = 42
    else:
        obj.data.type = 'ORTHO'
        obj.data.ortho_scale = scale
    obj.data.clip_end = 3500
    return obj


def inspect_export(path):
    scene = bpy.context.scene
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=str(path))
    driving_names = ("Racing surface", "Apron asphalt", "Pit road asphalt", "Pit entrance asphalt",
                     "Pit exit asphalt")
    checked_names = driving_names + ("Start finish checker Richmond", "Restart zone Start", "Restart zone End",
                     "Track inner white line", "Track inner amber line", "Richmond pit work concrete",
                     "Richmond pit walkway", "Turn one safety access asphalt", "Turn four safety access asphalt",
                     "Frontstretch grass median", "Pace bay return asphalt", "Pace bay departure asphalt")
    metrics = {}
    trace_surfaces = {}
    for obj in scene.objects:
        if obj.type != 'MESH' or obj.name not in checked_names:
            continue
        obj.data.calc_loop_triangles()
        triangles = obj.data.loop_triangles
        for tri in triangles:
            a, b, c = (obj.matrix_world @ obj.data.vertices[i].co for i in tri.vertices)
            if (b-a).cross(c-a).z <= 0.:
                raise ValueError(f"Reimported {obj.name} triangle {tri.index} has invalid winding")
        metrics[obj.name] = len(triangles)
        if obj.name in driving_names:
            points = [obj.matrix_world @ vertex.co for vertex in obj.data.vertices]
            trace_surfaces[obj.name] = BVHTree.FromPolygons(points, [tuple(face.vertices) for face in triangles],
                                                           all_triangles=True)
    if not all(name in metrics for name in checked_names):
        raise ValueError("An expected reimported pavement or marking mesh is missing")
    measurements = {}

    def sample_surface(name, points, visual_offset=0.):
        bvh = trace_surfaces[name]
        max_height_error = 0.
        min_up = 1.
        count = 0
        for x, y, z in points:
            hit, normal, _, _ = bvh.ray_cast(Vector((x, y, z+10.)), Vector((0., 0., -1.)), 15.)
            if hit is None:
                raise ValueError(f"Reimported {name} has no surface at ({x:.4f}, {y:.4f})")
            max_height_error = max(max_height_error, abs(hit.z - z - visual_offset))
            min_up = min(min_up, normal.z)
            count += 1
        if max_height_error > .015 or min_up < .95:
            raise ValueError(f"Reimported {name}: deviation {max_height_error:.5f}m, min up {min_up:.6f}")
        measurements[name] = dict(samples=count, max_height_error_m=max_height_error,
                                  min_normal_up=min_up)

    race_samples = [(distance, layout.race_pose(distance))
                    for distance in distances(0., layout.LAP, 2., True)]
    sample_surface("Racing surface", (layout.racing_surface(s, p.width*f)
                   for s, p in race_samples for f in (-.49,-.25,0.,.25,.49)))
    sample_surface("Apron asphalt", (layout.racing_surface(s, -p.width/2-layout.paved_apron_width(s)*f)
                   for s, p in race_samples for f in (.01,.25,.50,.75,.99)))
    for name, path in (("Pit road asphalt", layout.PIT), ("Pit entrance asphalt", layout.ENTRY),
                       ("Pit exit asphalt", layout.EXIT)):
        def pit_probes():
            for s in distances(.08, path.length-.08, 1.):
                x, y = path.at(s)
                tx, ty = path.heading(s)
                for f in (-.49,-.25,0.,.25,.49):
                    side = layout.PIT_WIDTH*f
                    yield x+ty*side, y-tx*side, 0.
        sample_surface(name, pit_probes(), .006)

    evidence = {"upward_triangle_count": sum(metrics.values()),
                "triangles_by_mesh": metrics,
                "reimported_surface_profile": measurements}
    (OUT / "export-winding.json").write_text(json.dumps(evidence, indent=2)+"\n", encoding="utf-8")
    if "--no-render" not in sys.argv:
        for label, location, target, scale in (
                ("overhead", (0., 28., 815.), (0., 28., 0.), 740.),
                ("oblique", (430., 640., 705.), (0., 26., 0.), 720.)):
            angle = camera(f"FBX verification {label}", location, target, scale)
            if label == "overhead":
                angle.rotation_euler = (0., 0., 0.)
            scene.camera = angle
            scene.render.resolution_percentage = 50
            scene.render.filepath = str(OUT / f"fbx-check-{label}.png")
            bpy.ops.render.render(write_still=True)
    print("RICHMOND FBX WINDING AND HEIGHTS PASSED", sum(metrics.values()),
          "triangles and", sum(entry["samples"] for entry in measurements.values()), "surface probes")


bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene["Venue"] = "Richmond Raceway, Richmond, Virginia"
scene["Reference layout"] = "User-supplied schematic, diagrammed D-oval race direction"
scene["Authoritative lap distance meters"] = layout.LAP
scene["Cup qualifying benchmark seconds"] = layout.BENCHMARK_SECONDS
scene["Cup qualifying benchmark mph"] = layout.BENCHMARK_MPH
scene["Track banking datum"] = "Flat inside paved edge. Center and outside banking height rise above it."
scene["Published measurement"] = "Official distance is measured 13.5 ft in from the exterior wall. Unity uses the midpoint to station official lap length."
scene["Cup benchmark source"] = "https://www.nascar.com/gallery/nascar-all-time-qualifying-records-by-track/"
race_geometry()
richmond_scenery.build()
bpy.context.view_layer.update()
ground_fan_anchors(bpy.context.scene)
pace_anchors = bpy.data.collections['Richmond | Unity live scenery anchors']
pace_return, pace_departure, _, _ = layout.pace_paths()
richmond_scenery.marker(pace_anchors, 'Richmond pace bay', (*layout.PACE_BAY, .012))
for name, path in (('return', pace_return), ('departure', pace_departure)):
    for index, distance in enumerate(distances(0., path.length, 1.2), 1):
        heading = path.heading(distance)
        richmond_scenery.marker(pace_anchors, f'Richmond pace {name} {index:02d}',
                               (*path.at(distance), .012), math.atan2(heading[1], heading[0]))

overview = camera("Oblique overview", (430., 640., 705.), (0., 26., 0.), 720.)
overhead = camera("Overhead layout", (0., 28., 815.), (0., 28., 0.), 740.)
overhead.rotation_euler = (0., 0., 0.)
finish = layout.race_pose(0.)
look = Vector(finish.offset(along=19., side=-3., height=1.5))
approach = Vector(finish.offset(along=-55., side=4., height=10.))
turn_camera = camera("Start-finish and banking check", approach, look, None)
entry = layout.race_pose(layout.ENTRY_STATION + 26.)
entry_focus = Vector(entry.offset(along=25., side=-11., height=2.2))
entry_look = Vector(entry.offset(along=-40., side=-5., height=7.8))
entry_camera = camera("Turn 4 pit approach", entry_look, entry_focus, None)
exit_camera = camera("Pit exit and safety access", (270., -48., 69.), (196., 12., 0.), 131.)
turn_four_camera = camera("Turn Four emergency access", (-279., -79., 88.), (-171., -34., 0.), 169.)
median_camera = camera("Grass between the frontstretch and pit road", (80., -260., 138.),
                       (9., -85., 0.), 296.)
pace_camera = camera("Pace bay and Turn One gate", (260., -9., 62.), (199., 30., 0.), 96.)
scene.camera = overview
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.render_aa = '32'
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
scene.world = bpy.data.worlds.new("Richmond preview sky")
scene.world.color = (.24, .27, .30)
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type == 'VIEW_3D':
            region = area.spaces.active.region_3d
            region.view_distance = 700.
            region.view_rotation = overview.rotation_euler.to_quaternion()
            region.view_location = (0., 0., 0.)
            area.spaces.active.clip_end = 4000.
            area.spaces.active.shading.color_type = 'MATERIAL'
bpy.context.preferences.filepaths.save_version = 0
bpy.context.preferences.filepaths.file_preview_type = 'NONE'
bpy.ops.wm.save_as_mainfile(filepath=str(OUT / "RichmondRaceway.blend"))
bpy.ops.export_scene.fbx(filepath=str(OUT / "RichmondRaceway.fbx"), object_types={'MESH'},
                         axis_forward='-Z', axis_up='Y', apply_unit_scale=True, use_mesh_modifiers=True,
                         bake_anim=False, add_leaf_bones=False)
if "--no-render" not in sys.argv:
    for viewpoint, filename in ((overview, "Richmond-overview.png"), (overhead, "Richmond-overhead.png"),
                                (turn_camera, "Richmond-start-finish.png"), (entry_camera, "Richmond-pit-entry.png"),
                                (exit_camera, "Richmond-pit-exit.png"),
                                (turn_four_camera, "Richmond-turn-four-access.png"),
                                (median_camera, "Richmond-frontstretch-grass.png"),
                                (pace_camera, "Richmond-pace-bay.png")):
        scene.camera = viewpoint
        scene.render.filepath = str(OUT / filename)
        bpy.ops.render.render(write_still=True)
bpy.ops.wm.open_mainfile(filepath=str(OUT / "RichmondRaceway.blend"))
assert bpy.context.scene["Authoritative lap distance meters"] == layout.LAP
assert bpy.data.objects["Start finish checker Richmond"].data.materials[0].name.startswith("Richmond checker white")
inspect_export(OUT / "RichmondRaceway.fbx")
