"""Check the Richmond inner barrier, pit divider, and swept driving paths."""

from array import array
from collections import Counter
from hashlib import sha256
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

FOLDER = Path(__file__).resolve().parent
sys.path.insert(0, str(FOLDER))
import richmond_layout as layout


DIVIDER = 'Pit separator wall track side'


def snapshot(path):
    bpy.ops.wm.open_mainfile(filepath=str(path))
    bpy.context.view_layer.update()
    checksums = {}
    for obj in bpy.context.scene.objects:
        if obj.type != 'MESH' or obj.name == DIVIDER:
            continue
        digest = sha256()
        vertices = array('f', [0.]) * (len(obj.data.vertices) * 3)
        obj.data.vertices.foreach_get('co', vertices)
        digest.update(vertices.tobytes())
        digest.update(array('f', (value for row in obj.matrix_world for value in row)).tobytes())
        loops = array('i', [0]) * len(obj.data.loops)
        obj.data.loops.foreach_get('vertex_index', loops)
        digest.update(loops.tobytes())
        digest.update(repr([(poly.loop_total, poly.material_index) for poly in obj.data.polygons]).encode())
        digest.update(repr([slot.name for slot in obj.material_slots]).encode())
        checksums[obj.name] = digest.hexdigest()
    return checksums


def divider_footprint(vertices):
    # Each tessellated wall cross section contains four vertices in order.
    # Sample the complete footprint between stations, including both end caps.
    left = [tuple(v[:2]) for v in vertices[::4]]
    right = [tuple(v[:2]) for v in vertices[1::4]]
    for i in range(len(left) - 1):
        for first, last in ((left[i], left[i+1]), (right[i], right[i+1]),
                            (left[i], right[i]), (left[i+1], right[i+1])):
            yield first
            yield tuple((a+b)/2 for a, b in zip(first, last))
            yield last


def near_distance(path, point):
    return math.dist(path.at(path.nearest(point)), point)


def clearance_checks(vertices):
    race_inner_edge = []
    apron_center = []
    for i in range(1209):
        s = layout.LAP * i / 1208
        pose = layout.race_pose(s)
        race_inner_edge.append(layout.racing_surface(s, -pose.width / 2)[:2])
        apron_lane = -pose.width / 2 - layout.running_apron_offset(s)
        apron_center.append(layout.racing_surface(s, apron_lane)[:2])
    routes = {
        'race_inner_edge': layout.Path(race_inner_edge),
        'running_apron_center': layout.Path(apron_center),
        'pit_straight_pavement_edge': layout.Path([layout.pit_pose(i * layout.PIT_LENGTH / 560)
            .offset(side=layout.PIT_WIDTH/2)[:2] for i in range(561)]),
        'pit_entrance_center': layout.ENTRY,
        'pit_exit_center': layout.EXIT,
    }
    bounds = {name: math.inf for name in routes}
    for point in divider_footprint(vertices):
        for name, path in routes.items():
            bounds[name] = min(bounds[name], near_distance(path, point))
    assert bounds['race_inner_edge'] > 4.20, bounds
    assert bounds['running_apron_center'] > 2.0, bounds
    assert bounds['pit_straight_pavement_edge'] > .60, bounds
    assert bounds['pit_entrance_center'] - layout.PIT_WIDTH/2 > 9.5, bounds
    assert bounds['pit_exit_center'] - layout.PIT_WIDTH/2 > 9.5, bounds
    return bounds, routes['running_apron_center']


def wall_tree(objects):
    points, triangles = [], []
    for obj in objects:
        origin = len(points)
        points.extend(obj.matrix_world @ v.co for v in obj.data.vertices)
        obj.data.calc_loop_triangles()
        triangles.extend(tuple(origin+i for i in t.vertices) for t in obj.data.loop_triangles)
    return BVHTree.FromPolygons(points, triangles, all_triangles=True)


def closed_wall(obj):
    vertices = [obj.matrix_world @ v.co for v in obj.data.vertices]
    obj.data.calc_loop_triangles()
    edges = Counter()
    signed_volume = 0.
    for triangle in obj.data.loop_triangles:
        i, j, k = triangle.vertices
        for a, b in ((i, j), (j, k), (k, i)):
            edges[a, b] += 1
        a, b, c = vertices[i], vertices[j], vertices[k]
        assert (b - a).cross(c - a).length > 1e-6, (obj.name, triangle.index)
        signed_volume += a.dot(b.cross(c)) / 6.
    assert all(count == edges[b, a] == 1 for (a, b), count in list(edges.items())), obj.name
    assert signed_volume > 0., (obj.name, signed_volume)
    return vertices, signed_volume


def check_turn_four_pavement(scene, road):
    paved = wall_tree([scene.objects[name] for name in (
        'Apron asphalt', 'Turn four safety access asphalt', 'Richmond paved safety access and recovery yard')])
    spans = math.ceil(road.length / .5)
    count = 0
    for station in [-1.25, -.15] + [road.length*i/spans for i in range(spans+1)] + [road.length+.15, road.length+1.25]:
        on_road = min(road.length, max(0., station))
        point, heading = road.at(on_road), road.heading(on_road)
        x, y = layout.add(point, layout.mul(heading, station - on_road))
        tx, ty = heading
        for side in (-4.75, -2.5, 0., 2.5, 4.75):
            probe = Vector((x + ty*side, y - tx*side, 3.))
            hit, normal, _, _ = paved.ray_cast(probe, (0., 0., -1.), 4.)
            assert hit is not None and -.001 <= hit.z < .05 and normal.z > .98, (station, side, hit)
            count += 1
    return count


def sweep_vehicle(name, path, bvh, width, length, loop=False, height=.50):
    # Check the perimeter and transverse sections of a conservative rectangle
    # covering the vehicle or loaded recovery combination at heading changes.
    count = max(1, math.ceil(path.length/.5))
    for i in range(count + 1):
        station = path.length*i/count
        x, y = path.at(station)
        tx, ty = path.heading(station % path.length if loop else station, loop=loop)
        def corner(along, side):
            return Vector((x + tx*along + ty*side, y + ty*along - tx*side, height))
        chord_ends = [(corner(a, -width/2), corner(a, width/2)) for a in (-length/2, 0, length/2)]
        chord_ends.extend((corner(-length/2, b), corner(length/2, b)) for b in (-width/2, 0, width/2))
        for start, end in chord_ends:
            hit, _, _, _ = bvh.ray_cast(start, end-start, (end-start).length)
            assert hit is None, (name, station, tuple(hit))
    return count + 1


def check_turn_four_service_clearance(scene, road):
    padding = 9.
    x_min = min(p[0] for p in road.points) - padding
    x_max = max(p[0] for p in road.points) + padding
    y_min = min(p[1] for p in road.points) - padding
    y_max = max(p[1] for p in road.points) + padding
    points, triangles = [], []
    for obj in scene.objects:
        if obj.type != 'MESH':
            continue
        box = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
        if (max(v.x for v in box) < x_min or min(v.x for v in box) > x_max
                or max(v.y for v in box) < y_min or min(v.y for v in box) > y_max
                or max(v.z for v in box) < .18 or min(v.z for v in box) > 3.):
            continue
        origin = len(points)
        points.extend(obj.matrix_world @ vertex.co for vertex in obj.data.vertices)
        obj.data.calc_loop_triangles()
        for triangle in obj.data.loop_triangles:
            indices = tuple(origin+i for i in triangle.vertices)
            heights = [points[index].z for index in indices]
            if max(heights) > .18 and min(heights) < 3.:
                triangles.append(indices)
    bvh = BVHTree.FromPolygons(points, triangles, all_triangles=True)
    return sum(sweep_vehicle('Turn Four service-road scenery clearance', road, bvh,
                             2.9, 12., height=level) for level in (.5, 1.45, 2.4))


def gate_checks(scene, apron_center):
    pit_and_apron_walls = [scene.objects[name] for name in (
        DIVIDER, 'Pit continuous stall-side wall', 'Inside apron wall east', 'Inside apron wall west')]
    divider_trees = wall_tree(pit_and_apron_walls)
    gate = layout.turn_gate_access()
    tail_end = layout.pit_wall_tail().points[-1]
    inner_turn_wall = layout.inner_apron_wall_edge(layout.TURN_WALL_START)[:2]
    gate_width = math.dist(tail_end, inner_turn_wall)
    assert gate_width > 16, gate_width
    west_gate = layout.turn_four_gate_access()
    west_inner_wall = layout.inner_apron_wall_edge(layout.TURN_FOUR_WALL_END)[:2]
    pit_entry_wall = layout.pit_wall_head().points[0]
    west_gap_width = math.dist(west_inner_wall, pit_entry_wall)
    assert west_gap_width - .44 > 16., west_gap_width

    def running_point(station):
        pose = layout.race_pose(station)
        return layout.racing_surface(station, -pose.width / 2 - 2.3)[:2], (pose.tx, pose.ty)

    def gate_turns(path, station):
        upstream, up_tangent = running_point(station - 18.)
        downstream, down_tangent = running_point(station + 19.)
        inside_gate = path.at(min(path.length * .64, 23.5))
        inward = path.heading(0.)
        inbound = layout.Path(layout.tangent_blend(upstream, up_tangent, inside_gate, inward, .23))
        outbound = layout.Path(layout.tangent_blend(inside_gate, layout.mul(inward, -1.),
                                                   downstream, down_tangent, .23))
        return inbound, outbound

    inbound_turn, outbound_turn = gate_turns(gate, layout.TURN_GATE_STATION)
    west_inbound, west_outbound = gate_turns(west_gate, layout.TURN_FOUR_GATE_STATION)
    tests = {
        'on_track_apron_stock_car': sweep_vehicle('apron stock car', apron_center, divider_trees, 2.1, 5.4, True),
        'on_track_apron_loaded_tow': sweep_vehicle('apron loaded tow', apron_center, divider_trees, 2.65, 11., True),
        'pit_through_lane_stock_car': sweep_vehicle('pit through lane', layout.PIT, divider_trees, 2.1, 5.4),
        'pit_entrance_stock_car': sweep_vehicle('pit entrance', layout.ENTRY, divider_trees, 2.1, 5.4),
        'pit_exit_stock_car': sweep_vehicle('pit exit', layout.EXIT, divider_trees, 2.1, 5.4),
        'safety_gate_loaded_tow': sweep_vehicle('safety gate loaded tow', gate, divider_trees, 2.9, 12.),
        'safety_gate_inbound_turn_loaded_tow': sweep_vehicle('turn off apron through safety gate', inbound_turn,
                                                            divider_trees, 2.9, 12.),
        'safety_gate_outbound_turn_loaded_tow': sweep_vehicle('return to apron through safety gate', outbound_turn,
                                                             divider_trees, 2.9, 12.),
        'turn_four_safety_gate_loaded_tow': sweep_vehicle('Turn Four safety access road', west_gate,
                                                         divider_trees, 2.9, 12.),
        'turn_four_inbound_turn_loaded_tow': sweep_vehicle('enter Turn Four safety access', west_inbound,
                                                          divider_trees, 2.9, 12.),
        'turn_four_outbound_turn_loaded_tow': sweep_vehicle('leave Turn Four safety access', west_outbound,
                                                           divider_trees, 2.9, 12.),
    }
    return gate_width - .44, west_gap_width - .44, tests


args = sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
baseline = Path(args[0]) if args else None
evidence = {}
if baseline:
    existing = snapshot(baseline)
    renewed = snapshot(FOLDER / 'RichmondRaceway.blend')
    differences = sorted(name for name in existing.keys() | renewed.keys() if existing.get(name) != renewed.get(name))
    unrelated = ('Racing surface', 'Pit road asphalt', 'Pit entrance asphalt', 'Pit exit asphalt',
                 'Outside continuous concrete wall', 'Inside apron wall east', 'Catch fence', 'Start finish',
                 'Restart zone', 'Painted pit stall', 'Richmond pit work concrete',
                 'Richmond grandstand')
    assert not any(name == 'Richmond pit walkway' or name.startswith(unrelated) for name in differences), differences
    evidence['verified_unchanged_original_meshes'] = sum(existing[n] == renewed.get(n) for n in existing)
    evidence['scoped_mesh_differences'] = differences

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(FOLDER / 'RichmondRaceway.fbx'))
scene = bpy.context.scene
vertices, signed_volume = closed_wall(scene.objects[DIVIDER])
inner_wall_volumes = {name: closed_wall(scene.objects[name])[1] for name in (
    'Pit continuous stall-side wall', 'Inside apron wall east', 'Inside apron wall west')}
minimum_z = min(v.z for v in vertices)
maximum_z = max(v.z for v in vertices)
length = layout.PIT_DIVIDER_END - layout.PIT_DIVIDER_START
assert abs(minimum_z) < .001 and abs(maximum_z - .99) < .001
wall_centers = [(near + far)*.5 for near, far in zip(vertices[::4], vertices[1::4])]
center_length = sum((b - a).length for a, b in zip(wall_centers, wall_centers[1:]))
assert abs(signed_volume - center_length*layout.PIT_DIVIDER_WIDTH*.99) < .02
clearance, apron_route = clearance_checks(vertices)
gate_width, west_gate_width, swept_counts = gate_checks(scene, apron_route)
evidence.update({
    'wall_length_m': length,
    'closed_mesh_signed_volume_m3': signed_volume,
    'closed_inner_barrier_volumes_m3': inner_wall_volumes,
    'divider_runout_beyond_first_and_last_box_m': layout.PIT_DIVIDER_RUNOUT,
    'pit_approach_unobstructed_lengths_m': [layout.PIT_DIVIDER_START,
                                           layout.PIT_LENGTH - layout.PIT_DIVIDER_END],
    'minimum_wall_clearance_m': clearance,
    'turn_one_safety_gate_clear_opening_m': gate_width,
    'turn_four_safety_gate_clear_opening_m': west_gate_width,
    'turn_four_pavement_join_probes': check_turn_four_pavement(scene, layout.turn_four_gate_access()),
    'turn_four_service_clearance_probes': check_turn_four_service_clearance(scene, layout.turn_four_gate_access()),
    'wall_clearance_vehicle_poses': swept_counts,
})
(FOLDER / 'pit-divider-verification.json').write_text(json.dumps(evidence, indent=2) + '\n', encoding='utf-8')
print(json.dumps(evidence, indent=2))
