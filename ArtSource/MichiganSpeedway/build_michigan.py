"""Build the Michigan Blender source, game FBX and publication previews."""

import argparse
import json
import math
from pathlib import Path
import sys

import bpy
import bmesh
from mathutils import Vector

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT))
import michigan_layout as layout


def collection(name):
    result = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(result)
    return result


def material(name, rgb):
    result = bpy.data.materials.new(name)
    result.diffuse_color = (*rgb, 1.)
    result.roughness = .82
    result.use_nodes = True
    shader = result.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = result.diffuse_color
    shader.inputs["Roughness"].default_value = .82
    return result


def mesh(name, vertices, faces, mats, target, *, pavement=False, indices=None):
    data = bpy.data.meshes.new(name)
    data.from_pydata(vertices, [], faces)
    data.update()
    obj = bpy.data.objects.new(name, data)
    target.objects.link(obj)
    for mat in (mats if isinstance(mats, (list, tuple)) else (mats,)):
        data.materials.append(mat)
    if indices is not None:
        for poly, index in zip(data.polygons, indices):
            poly.material_index = index
    if pavement:
        for poly in data.polygons:
            if poly.area < 1e-8 or poly.normal.z < .25:
                raise ValueError(f"{name} has inverted or degenerate pavement face {poly.index}")
    return obj


BOX_FACES = ((3,2,1,0),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7))


def box_vertices(xyz, size, rotation):
    sx, sy, sz = (value / 2 for value in size)
    cx, cy, cz = xyz
    c, s = math.cos(rotation), math.sin(rotation)
    return [(cx + c*x - s*y, cy + s*x + c*y, cz + z)
            for x, y, z in ((-sx,-sy,-sz),(sx,-sy,-sz),(sx,sy,-sz),(-sx,sy,-sz),
                            (-sx,-sy,sz),(sx,-sy,sz),(sx,sy,sz),(-sx,sy,sz))]


def box(name, xyz, size, mat, target, rotation=0.):
    return mesh(name, box_vertices(xyz, size, rotation), BOX_FACES, mat, target)


def stations(first, last, spacing, closed=False):
    count = max(1, math.ceil((last - first) / spacing))
    return [first + (last - first) * i / count for i in range(count + (not closed))]


def ribbon(name, rows, mat, target, closed=False):
    vertices = [point for row in rows for point in row]
    count = len(rows)
    faces = []
    for i in range(count if closed else count - 1):
        a, b = i * 2, ((i + 1) % count) * 2
        for triangle in ((a, b, a+1), (a+1, b, b+1)):
            q, r, s = (vertices[k] for k in triangle)
            cross = (r[0]-q[0]) * (s[1]-q[1]) - (r[1]-q[1]) * (s[0]-q[0])
            faces.append(triangle if cross > 0 else tuple(reversed(triangle)))
    return mesh(name, vertices, faces, mat, target, pavement=True)


def strip(name, samples, point, side_a, side_b, mat, target, *, height=0., closed=False):
    def at(distance, side):
        x, y, z = point(distance, side)
        return x, y, z + height
    return ribbon(name, [[at(s, side_a), at(s, side_b)] for s in samples], mat, target, closed)


def wall(name, samples, gait, edge, direction, height, mat, target, closed=False):
    vertices = []
    for s in samples:
        p = gait(s)
        x, y, z = edge(s)
        nx, ny = p.ty * direction, -p.tx * direction
        vertices.extend(((x,y,z-.04), (x,y,z+height),
                         (x+.37*nx,y+.37*ny,z+height), (x+.37*nx,y+.37*ny,z-.04)))
    faces = []
    for i in range(len(samples) if closed else len(samples) - 1):
        a, b = i * 4, ((i + 1) % len(samples)) * 4
        faces.extend((a+j, b+j, b+(j+1)%4, a+(j+1)%4) for j in range(4))
    if not closed:
        n = len(vertices)
        faces.extend(((0, 1, 2, 3), (n-1, n-2, n-3, n-4)))
    obj = mesh(name, vertices, faces, mat, target)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(obj.data)
    bm.free()
    return obj


def wire_mesh(name, segments, radius, mat, target):
    vertices, faces = [], []
    for start, end in segments:
        a, b = Vector(start), Vector(end)
        along = (b-a).normalized()
        n = along.cross(Vector((0, 0, 1)))
        if n.length_squared < .1:
            n = along.cross(Vector((0, 1, 0)))
        n = n.normalized() * radius
        up = along.cross(n).normalized() * radius
        i = len(vertices)
        vertices.extend(tuple(pos + side) for pos in (a,b) for side in (n,up,-n,-up))
        faces.extend((i+j,i+(j+1)%4,i+4+(j+1)%4,i+4+j) for j in range(4))
    return mesh(name, vertices, faces, mat, target)


def offset_path(path, station, lane):
    sampled = max(0., min(path.length, station))
    x, y = path.at(sampled)
    tx, ty = path.heading(sampled)
    extension = station - sampled
    return x + tx*extension + ty*lane, y + ty*extension - tx*lane, 0.


def inside_wall_sections(edge):
    annotations = json.loads((ROOT / "reference/MichiganTrackLayout.features.json").read_bytes())
    service_roads = [layout.Path([layout.diagram_xy(*p) for p in route])
                     for route in annotations["road_centerlines_px"].values()]
    corridors = [layout.ENTRY, layout.PIT, layout.EXIT, *service_roads]
    # Allow for the wall's thickness and the sampled endpoints at each opening.
    centers = [(route.at(s), (layout.exit_half_width(s) if route is layout.EXIT else 4.) + 1.2)
               for route in corridors for s in stations(0, route.length, 1.)]
    lap = stations(0, layout.LAP, 1.4, True)
    blocked = []
    for s in lap:
        point = edge(s)[:2]
        blocked.append(any(math.dist(point, center) < clearance for center, clearance in centers))

    # Begin inside an opening so any wall section crossing the lap seam stays whole.
    first_gap = blocked.index(True)
    section = []
    for i in range(first_gap + 1, first_gap + len(lap) + 1):
        index = i % len(lap)
        if blocked[index]:
            if len(section) > 1:
                yield section
            section = []
        else:
            section.append(lap[index])


def build_driving_geometry():
    driving = collection("Driving surfaces")
    painted = collection("Race and pit markings")
    walls = collection("Walls and catch fence")
    ground = collection("Ground")
    asphalt = material("Michigan track asphalt", (.105,.110,.116))
    concrete = material("Michigan pit work concrete", (.39,.40,.41))
    wall_mat = material("Michigan wall concrete", (.80,.82,.84))
    white = material("Michigan white paint", (.92,.93,.91))
    dark = material("Michigan black checker", (.018,.02,.022))
    gold = material("Michigan amber paint", (.90,.65,.09))
    blue = material("Michigan restart paint", (.035,.38,.66))
    wire = material("Michigan catch fence galvanized steel", (.33,.36,.39))
    grass = material("Michigan mown grass", (.155,.254,.097))
    unlit = material("Michigan safety light dark housing", (.037,.044,.052))
    box("Michigan grass site", (0,0,-.11), (2200,1600,.20), grass, ground)

    lap = stations(0, layout.LAP, .75, True)
    half = layout.WIDTH / 2
    race = layout.racing_surface
    strip("Racing surface", lap, race, -half, half, asphalt, driving, closed=True)
    strip("Apron asphalt", lap, race, -half-layout.APRON, -half, asphalt, driving, closed=True)
    strip("Track inner white line", lap, race, -half+.045, -half+.195, white, painted, height=.012, closed=True)
    strip("Track outer white line", lap, race, half-.40, half-.26, white, painted, height=.012, closed=True)

    edge = lambda s: race(s, half+.015)
    outside = stations(0, layout.LAP, 1.4, True)
    wall("Outside continuous concrete wall", outside, layout.race_pose, edge, 1., layout.WALL_HEIGHT,
         wall_mat, walls, True)
    fence = stations(0, layout.LAP, 4.5, True)
    wires = []
    for i, s in enumerate(fence):
        p = Vector(edge(s))
        pose = layout.race_pose(s)
        p += Vector((pose.ty, -pose.tx, 0.)) * .20
        next_s = fence[(i + 1) % len(fence)]
        q = Vector(edge(next_s))
        next_pose = layout.race_pose(next_s)
        q += Vector((next_pose.ty, -next_pose.tx, 0.)) * .20
        def raise_by(v, h):
            return v + Vector((0.,0.,h))
        wires.append((raise_by(p,1.05), raise_by(p,4.10)))
        for h in (1.15,1.67,2.19,2.71,3.23,3.75,4.10):
            wires.append((raise_by(p,h), raise_by(q,h)))
        wires.extend(((raise_by(p,1.12), raise_by(q,4.08)), (raise_by(p,4.08), raise_by(q,1.12))))
    wire_mesh("Outside steel catch fence", wires, .019, wire, walls)

    def inside_edge(s):
        return race(s, -half - layout.APRON - .75)

    for i, section in enumerate(inside_wall_sections(inside_edge), 1):
        wall(f"Inside apron wall {i:02d}", section, layout.race_pose, inside_edge, -1.,
             layout.WALL_HEIGHT, wall_mat, walls)

    for name, route in (("Pit entrance asphalt",layout.ENTRY), ("Pit road asphalt",layout.PIT)):
        # Overlap the through lane into both connectors so triangle rounding
        # or a slightly oblique endpoint cannot expose a narrow grass slit.
        through = route is layout.PIT
        overlap = .75 if through else 0.
        strip(name, stations(-overlap,route.length+overlap,1.2), lambda s,l: offset_path(route,s,l),
              -layout.PIT_WIDTH/2, layout.PIT_WIDTH/2, asphalt, driving, height=.010 if through else .008)

    def exit_pavement(s, lane):
        x, y, _ = offset_path(layout.EXIT, s, lane * layout.exit_half_width(s))
        # Taper under the running apron at the overlap. The apron is the
        # visible surface at the handoff. Match the car path's smooth grade.
        t = s / layout.EXIT.length
        taper = t * t * (3. - 2. * t)
        return x, y, layout.mix(.008, -.001, taper)

    strip("Pit exit asphalt", stations(0, layout.EXIT.length, 1.2), exit_pavement,
          -1., 1., asphalt, driving)

    pit = lambda s,l: offset_path(layout.PIT,s,l)
    first, last = layout.PIT_WALL_START, layout.PIT_WALL_END
    work = stations(first, last, 1.5)
    strip("Michigan continuous concrete pit work", work, pit, layout.PIT_WORK_INNER,
          -layout.PIT_WIDTH/2+.02, concrete, driving, height=.015)
    strip("Michigan ten-foot pit walkway", stations(0,layout.PIT_DRAWING_LENGTH,1.5), pit, layout.PIT_WALK_INNER,
          layout.PIT_WORK_INNER, concrete, driving, height=.013)
    wall("Pit continuous stall-side wall", work, layout.pit_pose, lambda s: pit(s,-layout.PIT_WIDTH/2-.15),
         -1., .96, wall_mat, walls)
    wall("Pit track divider", work, layout.pit_pose, lambda s: pit(s,layout.PIT_DIVIDER_EDGE),
         1., .84, wall_mat, walls)
    for i in range(layout.STALL_COUNT+1):
        s = layout.STALL_START + i*layout.STALL_SPACING
        strip(f"Michigan pit stall divider {i:02}", [s-.05,s+.05], pit, -3.94, -.7,
              white, painted, height=.027)
    for name,fraction in (("entry",layout.PIT_SPEED_START),("exit",layout.PIT_SPEED_END)):
        s = layout.PIT_LENGTH*fraction
        strip(f"Michigan pit speed {name}", [s-.17,s+.17], pit, -3.9, 3.9, gold, painted, height=.028)

    # Full-width checker and exact race-control endpoints.
    vertices, faces, colors = [], [], []
    for row in range(3):
        for cell in range(24):
            a, b = (row-1.5)*.26, (row-.5)*.26
            lo, hi = layout.WIDTH*(cell/24-.5), layout.WIDTH*((cell+1)/24-.5)
            n = len(vertices)
            for s,lane in ((a,lo),(b,lo),(b,hi),(a,hi)):
                x,y,z = race(s,lane)
                vertices.append((x,y,z+.026))
            indices = (n,n+1,n+2,n+3)
            q,r,t = vertices[n:n+3]
            if (r[0]-q[0])*(t[1]-q[1])-(r[1]-q[1])*(t[0]-q[0]) < 0:
                indices = tuple(reversed(indices))
            faces.append(indices)
            colors.append((row+cell)%2)
    mesh("Start finish checker", vertices, faces, (white,dark), painted, pavement=True, indices=colors)
    for label, fraction in (("Start",layout.RESTART_START),("End",layout.RESTART_END)):
        s = layout.LAP*fraction
        strip(f"Restart zone {label}", [s-.22,s+.22], race, half-2.25, half-.4, blue, painted, height=.028)

    x,y,z = edge(0.)
    box("Michigan flag stand steel support", (x,y-1.75,z+2.25), (2.40,1.6,4.5), wire, walls)
    box("Michigan flag stand deck", (x,y-1.75,z+4.55), (3.45,2.4,.26), wall_mat, walls)
    box("Michigan flag stand front plate", (x,y-1.,z+5.03), (3.4,.12,.68), blue, walls)
    for i, side in enumerate((-.72,.76),1):
        box(f"Michigan flag official {i} body", (x+side,y-1.80,z+5.16), (.45,.36,1.08), dark, walls)
        box(f"Michigan flag official {i} head", (x+side,y-1.80,z+5.81), (.23,.22,.24), white, walls)
    official = bpy.data.objects.new("Michigan flag official anchor", None)
    official.location = (x-.72, y-1.80, z+4.67)
    walls.objects.link(official)

    groups = {label: [] for label in ("left amber", "right amber", "green", "red")}
    for i, fraction in enumerate((0,.110,.205,.395,.45,.55,.605,.795,.885)):
        station = layout.LAP*fraction
        pose = layout.race_pose(station)
        x,y,z = edge(station)
        angle = math.atan2(pose.ty,pose.tx)
        box(f"Michigan safety panel {i+1}", (x,y,z+2.64), (1.48,.24,.68), unlit, walls, angle)
        for name, column in (("left amber",-.37),("green",0.),("right amber",.37),("red",.70)):
            for row in (-.14,.14):
                groups[name].append(box_vertices((x+pose.tx*column,y+pose.ty*column,z+2.64+row),
                                                  (.20,.28,.165),angle))
    for name, boxes in groups.items():
        mesh(f"Michigan safety {name} lenses", [v for corners in boxes for v in corners],
             [tuple(i*8+j for j in face) for i in range(len(boxes)) for face in BOX_FACES],
             unlit, walls)

    # A positive x/y source-space anchor lets Unity resolve both FBX axes.
    anchor = bpy.data.objects.new("Michigan import orientation", None)
    anchor.location = (10.,10.,0.)
    walls.objects.link(anchor)


def camera(name, location, target, scale):
    data = bpy.data.cameras.new(name)
    data.type = 'ORTHO'
    data.ortho_scale = scale
    data.clip_end = 6000
    obj = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = location
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat('-Z','Y').to_euler()
    return obj


def build(output, scenery, render):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    build_driving_geometry()
    if scenery:
        from michigan_scenery import build_scenery
        build_scenery(layout)
    scene = bpy.context.scene
    overhead = camera("Michigan overhead", (0,0,1800), (0,0,0), 1510)
    overview = camera("Michigan overview", (1120,-1680,1470), (0,0,0), 1550)
    front = camera("Michigan frontstretch", (285,-635,280), (0,-210,0), 1040)
    scene.camera = overview
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.render.resolution_x = 1600
    scene.render.resolution_y = 1024
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.view_settings.view_transform = 'Standard'
    scene.view_settings.exposure = .8
    shade = scene.display.shading
    shade.light = 'STUDIO'
    shade.color_type = 'MATERIAL'
    shade.show_backface_culling = True
    shade.show_shadows = True
    shade.show_cavity = True
    shade.cavity_type = 'BOTH'
    shade.background_type = 'WORLD'
    scene.world = bpy.data.worlds.new("Michigan preview sky")
    scene.world.color = (.25,.31,.38)
    scene["venue"] = "Michigan International Speedway, Irish Hills, Michigan"
    scene["NASCAR Cup qualifying seconds"] = layout.BENCHMARK_SECONDS
    scene["source layout"] = "MichiganTrackLayout.pdn"
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(output / "MichiganSpeedway.blend"))
    bpy.ops.export_scene.fbx(filepath=str(output / "MichiganSpeedway.fbx"), object_types={'MESH','EMPTY'},
                            axis_forward='-Z', axis_up='Y', apply_unit_scale=True,
                            use_mesh_modifiers=True, bake_anim=False, add_leaf_bones=False)
    if render:
        for view, name in ((overhead,"Michigan-overhead.png"),(overview,"Michigan-overview.png"),
                           (front,"Michigan-frontstretch.png")):
            scene.camera = view
            scene.render.filepath = str(output / name)
            bpy.ops.render.render(write_still=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, default=ROOT)
    parser.add_argument('--scenery', action='store_true')
    parser.add_argument('--render', action='store_true')
    args = parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    args.output.mkdir(parents=True,exist_ok=True)
    build(args.output.resolve(),args.scenery,args.render)
