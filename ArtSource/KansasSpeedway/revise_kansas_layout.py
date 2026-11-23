"""Add Kansas's barriers and pavement markers to the matched editable venue."""

import bisect
import json
import math
from pathlib import Path
import sys

import bmesh
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree


BAY_CLEARANCE = 3.0
STALL_WALL_END = 222.0
WALL_WIDTH = .32
WALL_HEIGHT = 1.067
CAR_HALF_WIDTH = 1.2
CAR_HALF_LENGTH = 2.8
SEAM_OFFSET = .05


def sampled_path(controls, closed, density):
    size = len(controls)
    points = []
    for i in range(size if closed else size - 1):
        b, c = controls[i], controls[(i + 1) % size]
        a = controls[(i - 1) % size] if i or closed else 2 * b - c
        d = controls[(i + 2) % size] if i + 2 < size or closed else 2 * c - b
        for j in range(density):
            t = j / density
            points.append(.5 * (2*b + (c-a)*t + (2*a-5*b+4*c-d)*t*t + (-a+3*b-3*c+d)*t*t*t))
    points.append(controls[0] if closed else controls[-1])
    return points


def cumulative(points):
    distances = [0.]
    for a, b in zip(points, points[1:]):
        distances.append(distances[-1] + (b - a).length)
    return distances


def pose(points, distances, station):
    i = min(len(points) - 2, max(0, bisect.bisect_right(distances, station) - 1))
    fraction = (station - distances[i]) / (distances[i + 1] - distances[i])
    tangent = (points[i+1] - points[max(0, i-1)]).normalized().lerp(
        (points[min(len(points)-1, i+2)] - points[i]).normalized(), fraction).normalized()
    return points[i].lerp(points[i+1], fraction), tangent


def mesh(name, vertices, faces, material):
    data = bpy.data.meshes.new(name)
    data.from_pydata(vertices, [], faces)
    data.update()
    obj = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(obj)
    obj.data.materials.append(material)
    return obj


def fix_winding(obj):
    world = obj.matrix_world
    reversed_faces = 0
    for poly in obj.data.polygons:
        a, b, c = (world @ obj.data.vertices[i].co for i in poly.vertices[:3])
        cross = (b - a).cross(c - a)
        if cross.z < 0.:
            poly.flip()
            reversed_faces += 1
    obj.data.update()
    obj.data.calc_loop_triangles()
    for face in obj.data.loop_triangles:
        a, b, c = (world @ obj.data.vertices[i].co for i in face.vertices)
        if (b - a).cross(c - a).z <= 1e-10:
            raise ValueError(f"{obj.name}: invalid upward winding in triangle {face.index}")
    return reversed_faces


def fix_solid_winding(obj):
    work = bmesh.new()
    work.from_mesh(obj.data)
    try:
        if any(not edge.is_manifold for edge in work.edges):
            raise ValueError(f"{obj.name}: wall is not a closed solid")
        volume = work.calc_volume(signed=True) * obj.matrix_world.to_3x3().determinant()
    finally:
        work.free()
    if abs(volume) < 1e-8:
        raise ValueError(f"{obj.name}: wall has no signed volume")
    if volume < 0.:
        for face in obj.data.polygons:
            face.flip()
        obj.data.update()
    return {'volumeCubicMeters': abs(volume), 'reversed': volume < 0.}


def wall(name, rows, material):
    vertices = [(x, y, z) for x, lo, hi in rows for y, z in
                ((lo, .018), (hi, .018), (hi, .018 + WALL_HEIGHT), (lo, .018 + WALL_HEIGHT))]
    faces = []
    for i in range(len(rows) - 1):
        for k in range(4):
            next_corner = (k + 1) % 4
            faces.append((4*i+k, 4*i+next_corner, 4*(i+1)+next_corner, 4*(i+1)+k))
    faces += [(3, 2, 1, 0), tuple(range(len(vertices) - 4, len(vertices)))]
    return mesh(name, vertices, faces, material)


def join_pavement(before, after, station):
    def boundary(obj):
        result = {}
        for vertex in obj.data.vertices:
            point = obj.matrix_world @ vertex.co
            if abs(point.x - station) < .004:
                result.setdefault((round(point.y, 5), round(point.z, 5)), []).append((vertex, point))
        return result

    lead, follow = boundary(before), boundary(after)
    if lead.keys() != follow.keys() or len(lead) != 17:
        raise ValueError(f"Mismatched cross section at {before.name} / {after.name}")
    differences = [follow[key][0][1].x - lead[key][0][1].x for key in lead]
    # Distribute the shared cross section 5 cm inside the following ribbon so
    # Unity's handoff ray lands on a triangle interior instead of the mesh edge.
    seam_x = station + SEAM_OFFSET
    for key in lead:
        _, old = lead[key][0]
        shared = Vector((seam_x, old.y, old.z))
        for obj, section in ((before, lead), (after, follow)):
            local = obj.matrix_world.inverted() @ shared
            for vertex, _ in section[key]:
                vertex.co = local
    before.data.update()
    after.data.update()
    low = min(key[0] for key in lead)
    high = max(key[0] for key in lead)
    flat_height = next(iter(lead))[1]
    tree = bvh(before)
    for i in range(101):
        lane = low + .01 + (high - low - .02) * i / 100
        point, _, _, _ = tree.ray_cast(Vector((station, lane, 5.)), Vector((0., 0., -1.)), 6.)
        if point is None or abs(point.z - flat_height) > 1e-5:
            raise ValueError(f"Incomplete cross section at {station:.6f} m, lane {lane:.6f} m")
    return {
        'handoffXMeters': station,
        'sharedBoundaryXMeters': seam_x,
        'fullPavedWidthMeters': high - low,
        'checkPointsAcrossHandoff': 101,
        'removedMaximumGapMillimeters': 1000 * max(differences),
        'removedMaximumOverlapMillimeters': -1000 * min(differences),
    }


def bvh(obj):
    return BVHTree.FromPolygons([obj.matrix_world @ v.co for v in obj.data.vertices],
                                [tuple(p.vertices) for p in obj.data.polygons])


def lifted(bvh_tree, p, raise_by):
    target, normal, _, _ = bvh_tree.ray_cast(Vector((*p, 12.)), Vector((0., 0., -1.)), 15.)
    if target is None or normal.z <= 0.:
        raise ValueError(f"No top-facing racing pavement at {tuple(p)}")
    target.z += raise_by
    return target


def authored_lane(points, distances, station, lateral):
    p, tangent = pose(points, distances, station)
    outward = Vector((tangent.y, -tangent.x))
    # At the Kansas race markers the saved bank is 10 degrees across the cross section.
    return p + outward * lateral * math.cos(math.radians(10.))


def paint_at(paint_tree, race_tree, point):
    paint, normal, _, _ = paint_tree.ray_cast(Vector((*point, 12.)), Vector((0., 0., -1.)), 15.)
    if paint is None or normal.z <= 0.:
        return None
    return paint.z - lifted(race_tree, point, 0.).z


def align_finish(tiles, fraction, points, distances, width, race_tree):
    tiles = sorted(tiles, key=lambda obj: obj.name)
    columns = len(tiles) // 2
    assert columns == 18
    style = [obj.data.materials[obj.data.polygons[0].material_index] for obj in tiles]
    first = [min((obj.matrix_world @ v.co).x for v in obj.data.vertices) for obj in tiles[:columns]]
    last = [max((obj.matrix_world @ v.co).x for v in obj.data.vertices) for obj in tiles[columns:]]
    depth = sum(b - a for a, b in zip(first, last)) / columns
    station = fraction * distances[-1]
    vertices = []
    for along in (-depth * .5, 0., depth * .5):
        for index in range(columns + 1):
            lane = (width - .018) * (index / columns - .5)
            point = authored_lane(points, distances, station + along, lane)
            vertices.append(lifted(race_tree, point, .020))
    for tile in tiles:
        bpy.data.objects.remove(tile, do_unlink=True)
    stride = columns + 1
    faces = []
    for row in range(2):
        for column in range(columns):
            a = row * stride + column
            faces.append((a, a + stride, a + stride + 1, a + 1))
    stripe = mesh('Start finish checker', vertices, faces, style[0])
    material_slots = {style[0].name: 0}
    for face, material in zip(stripe.data.polygons, style):
        if material.name not in material_slots:
            material_slots[material.name] = len(stripe.data.materials)
            stripe.data.materials.append(material)
        face.material_index = material_slots[material.name]
    fix_winding(stripe)
    check = bvh(stripe)
    offsets = []
    for sample in range(101):
        point = authored_lane(points, distances, station, width * (.48 * (sample / 50 - 1)))
        offset = paint_at(check, race_tree, point)
        if offset is None or not .01 <= offset <= .03:
            raise ValueError(f"Finish stripe misses the saved line at {tuple(point)}")
        offsets.append(offset)
    center = authored_lane(points, distances, station, 0.)
    return {'rowCount': 2, 'columns': columns, 'materialNames': list(material_slots),
            'originalUpstreamEdgeXMeters': [min(first), max(first)], 'paintDepthMeters': depth,
            'paintCenterSourceXYMeters': list(center), 'paintHeightMeters': [min(offsets), max(offsets)],
            'crossSectionProbes': len(offsets)}


def create_marker(name, fraction, points, distances, width, race_tree, material):
    station = fraction * distances[-1]
    vertices = []
    offsets = [.20, .7, 1.2, 1.7, 2.2, 2.5]
    for along in (-.22, .22):
        p, t = pose(points, distances, station + along)
        outward = Vector((t.y, -t.x))
        for offset in offsets:
            # Ray-project onto the actual progressive-bank surface in the editable mesh.
            vertices.append(lifted(race_tree, p + outward * (width*.5-offset) * math.cos(math.radians(10)), .02))
    n = len(offsets)
    marker = mesh(name, vertices, [(i, i+n, i+n+1, i+1) for i in range(n-1)], material)
    fix_winding(marker)


def identify_marker(obj, name, fraction, layout, stations, width, race_tree):
    point = authored_lane(layout, stations, fraction * stations[-1], width * .5 - 1.35)
    offset = paint_at(bvh(obj), race_tree, point)
    if offset is None or abs(offset) > .1:
        material = obj.data.materials[0]
        bpy.data.objects.remove(obj, do_unlink=True)
        create_marker(name, fraction, layout, stations, width, race_tree, material)
        offset = paint_at(bvh(bpy.data.objects[name]), race_tree, point)
        action = 'refit'
    else:
        obj.name = name
        obj.data.name = name
        action = 'retained'
    if offset is None or abs(offset) > .1:
        raise ValueError(f"{name} misses the exact saved restart station")
    return {'action': action, 'fraction': fraction, 'sourceXYMeters': list(point), 'paintHeightMeters': offset}


def apron_inside_at(x, near_edges):
    for a, b in zip(near_edges, near_edges[1:]):
        if a.x <= x <= b.x:
            return a.y + (b.y-a.y) * (x-a.x)/(b.x-a.x)
    raise ValueError(f"No measured apron edge at x={x}")


def apron_inner_edge(obj, limit):
    # The frontstretch lies on negative source Y and the inboard edge is the
    # northmost boundary at each X. The paired opposing stretch is positive Y.
    incidents = {}
    for polygon in obj.data.polygons:
        for edge in polygon.edge_keys:
            incidents[edge] = incidents.get(edge, 0) + 1
    boundary = {}
    for (a, b), count in incidents.items():
        if count == 1:
            boundary.setdefault(a, []).append(b)
            boundary.setdefault(b, []).append(a)
    coordinates = {i: obj.matrix_world @ obj.data.vertices[i].co for i in boundary}
    seed = max((i for i, p in coordinates.items() if abs(p.x) < 4 and p.y < 0),
               key=lambda i: coordinates[i].y)
    front = set()
    pending = [seed]
    while pending:
        point = pending.pop()
        if point not in front:
            front.add(point)
            pending.extend(boundary[point])
    return sorted((coordinates[i] for i in front if abs(coordinates[i].x) <= limit + 8
                   and coordinates[i].y < 0), key=lambda p: p.x)


def main():
    source, definition, output = map(Path, sys.argv[sys.argv.index('--') + 1:])
    data = json.loads(definition.read_text(encoding='utf-8'))
    road = data['pitRoad']
    assert data['id'] == 32 and len(road['stalls']) == 44
    bpy.ops.wm.open_mainfile(filepath=str(source), load_ui=False, use_scripts=False)
    bpy.context.preferences.filepaths.save_version = 0
    race = bpy.data.objects['Progressively banked asphalt']
    apron = bpy.data.objects['Continuous racing apron']
    entry = bpy.data.objects['Turn 4 pit entry']
    straight = bpy.data.objects['Straight frontstretch pit road']
    exit_ = bpy.data.objects['Turn 1 and 2 apron exit']
    pavement_joins = {
        'entryToPitRoad': join_pavement(entry, straight, road['points'][0]['x']),
        'pitRoadToExit': join_pavement(straight, exit_, road['points'][-1]['x']),
    }
    pavement_and_paint = {race.name, apron.name, 'Turn 4 pit entry', 'Straight frontstretch pit road',
                         'Turn 1 and 2 apron exit', 'White inner line', 'White outer line'}
    pavement_and_paint.update(obj.name for obj in bpy.context.scene.objects if obj.name.startswith((
        'Pit stall ', 'Restart zone pavement tick')))
    corrected = {name: count for name in sorted(pavement_and_paint)
                 if (count := fix_winding(bpy.data.objects[name]))}
    race_tree = bvh(race)
    layout = sampled_path([Vector((p['x'], p['z'])) for p in data['layoutPoints']], True, 24)
    stations = cumulative(layout)
    tiles = [obj for obj in bpy.context.scene.objects if obj.name.startswith('Start finish checker')]
    finish = align_finish(tiles, data['raceMarkers']['startFinish'], layout, stations, data['widthMeters'], race_tree)

    stall_wall = bpy.data.objects['Straight crew-side pit wall']
    wall_material = stall_wall.data.materials[0]
    pit_y = road['points'][0]['z']
    pit_half_width = road['widthMeters'] * .5
    bay_spacing = road['stalls'][1]['x'] - road['stalls'][0]['x']
    stall_end = max(p['x'] for p in road['stalls']) + bay_spacing * .5
    divider_end = stall_end + BAY_CLEARANCE
    assert stall_end < STALL_WALL_END < 222.80
    inside_edge = pit_y + pit_half_width
    for vert in stall_wall.data.vertices:
        vert.co.x = math.copysign(STALL_WALL_END, vert.co.x)
    stall_wall.data.update()
    stall_wall.name = 'Pit continuous stall-side wall'
    stall_wall.data.name = stall_wall.name

    apron_edges = apron_inner_edge(apron, divider_end)
    rows = []
    count = math.ceil(divider_end * 2)
    for sample in range(count + 1):
        x = -divider_end + 2*divider_end*sample/count
        boundary = apron_inside_at(x, apron_edges)
        # Tuck the divider into the unused outer shoulder wherever the full
        # width of the apron approaches the pit pavement at an end.
        outside = max(pit_y - pit_half_width, boundary + .18)
        rows.append((x, outside, outside+WALL_WIDTH))
    divider = wall('Pit track divider Kansas', rows, wall_material)
    walls = ['Outside SAFER wall', 'Infield turn wall 1', 'Infield turn wall 2', stall_wall.name, divider.name]
    solid_winding = {name: fix_solid_winding(bpy.data.objects[name]) for name in walls}

    through_lane = pit_y - road['widthMeters'] * .25
    clearance = min(through_lane-CAR_HALF_WIDTH-hi for _, _, hi in rows)
    stall_clearance = inside_edge-max(p['z'] for p in road['stalls'])-CAR_HALF_WIDTH
    connector_x_clearance = min(abs(p['x']) for side in ('entrance', 'exit') for p in road[side])-divider_end-CAR_HALF_LENGTH
    if min(clearance, stall_clearance, connector_x_clearance) < .5:
        raise ValueError((clearance, stall_clearance, connector_x_clearance))

    marker_actions = {}
    for key, label, source_name in (('restartZoneStart', 'Start', 'Restart zone pavement tick'),
                                    ('restartZoneEnd', 'End', 'Restart zone pavement tick.001')):
        marker_actions[label] = identify_marker(bpy.data.objects[source_name], 'Restart zone ' + label,
                                               data['raceMarkers'][key], layout, stations, data['widthMeters'], race_tree)

    output.mkdir(parents=True, exist_ok=True)
    (output / 'source-clearance.json').write_text(json.dumps({
        'correctedSourceFaces': corrected,
        'pavementJoins': pavement_joins,
        'finishMarker': finish,
        'closedWallWinding': solid_winding,
        'restartMarkers': marker_actions,
        'stallWallXEndpointsMeters': [-STALL_WALL_END, STALL_WALL_END],
        'trackFacingWallXEndpointsMeters': [-divider_end, divider_end],
        'minimumApronShoulderMeters': min(lo - apron_inside_at(x, apron_edges) for x, lo, _ in rows),
        'throughLaneToDividerCarClearanceMeters': clearance,
        'parkedCarToStallWallClearanceMeters': stall_clearance,
        'authoredConnectorLongitudinalCarClearanceMeters': connector_x_clearance,
        'garageMouthMinimumHorizontalGapMeters': 222.8 - STALL_WALL_END,
        'clearanceVehicleHalfDimensionsMeters': [CAR_HALF_WIDTH, CAR_HALF_LENGTH],
    }, indent=2) + '\n', encoding='utf-8')
    bpy.ops.wm.save_as_mainfile(filepath=str(output/'KansasSpeedway.blend'), compress=True)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in bpy.context.scene.objects:
        obj.select_set(obj.type == 'MESH' and obj.name != 'Preview ground')
    bpy.ops.export_scene.fbx(filepath=str(output/'KansasSpeedway.fbx'), object_types={'MESH'}, use_selection=True,
                             axis_forward='-Z', axis_up='Y', apply_unit_scale=True,
                             add_leaf_bones=False, bake_anim=False, mesh_smooth_type='FACE')


if __name__ == '__main__':
    main()
