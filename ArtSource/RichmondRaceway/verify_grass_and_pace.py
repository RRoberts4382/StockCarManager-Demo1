"""Verify unchanged native driving routes, surfaced vehicle envelopes, and visible grass."""

from bisect import bisect_right
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


def native_route_offsets():
    baseline = json.loads((FOLDER / 'BaselineTrack.json').read_text(encoding='utf-8-sig'))
    updated = json.loads((FOLDER / 'RichmondRaceway.json').read_text(encoding='utf-8'))
    original_widths = []
    painted_widths = []
    for a, b in zip(baseline['layoutPoints'], updated['layoutPoints'], strict=True):
        original_widths.append(a.pop('apronWidthMeters'))
        painted_widths.append(b.pop('apronWidthMeters'))
    assert baseline == updated, 'Only apron widths should change in the catalog'
    original_widths.append(original_widths[0])
    painted_widths.append(painted_widths[0])
    cut_controls = sum(a != b for a, b in zip(original_widths[:-1], painted_widths[:-1]))
    assert cut_controls > 50, cut_controls

    def runtime_limits(s, width):
        fraction = s / layout.LAP
        if fraction >= .807:
            blend = layout.ease((fraction - .807) / (.858 - .807))
        else:
            blend = layout.ease((.243 - fraction) / (.243 - .203))
        return [layout.lerp(offset, min(offset, 2.15), blend)
                for offset in (width*.5, width-1.60, width-2.55)]

    max_difference = 0.
    sampler_error = 0.
    for i in range(6036):
        station = layout.LAP * i / 6036
        n = bisect_right(layout._control_stations, station) - 1
        a, b = layout._control_stations[n:n+2]
        t = layout.ease((station - a) / (b - a))
        original = layout.lerp(*original_widths[n:n+2], t)
        current = layout.lerp(*painted_widths[n:n+2], t)
        diffs = [abs(a-b) for a, b in zip(runtime_limits(station, original), runtime_limits(station, current))]
        max_difference = max(max_difference, *diffs)
        sampler_error = max(sampler_error, abs(current-layout.paved_apron_width(station)))
    assert max_difference < .00001, max_difference
    assert sampler_error < .00001, sampler_error
    return dict(changed_width_controls=cut_controls, unity_offset_checks=6036*3,
                max_existing_runtime_offset_change_m=max_difference, max_width_sampler_difference_m=sampler_error)


def bvh(objects, solids_only=False):
    vertices, triangles = [], []
    for obj in objects:
        offset = len(vertices)
        vertices.extend(obj.matrix_world @ v.co for v in obj.data.vertices)
        obj.data.calc_loop_triangles()
        for t in obj.data.loop_triangles:
            inds = tuple(offset+i for i in t.vertices)
            if solids_only:
                heights = [vertices[i].z for i in inds]
                if max(heights) < .18 or min(heights) > 2.4:
                    continue
            triangles.append(inds)
    return BVHTree.FromPolygons(vertices, triangles, all_triangles=True)


def surface_at(tree, x, y):
    return tree.ray_cast((x, y, 10.), (0., 0., -1.), 11.)[0]


def verify_body(name, route, pavement, width, length, obstacles=None):
    sampled = 0
    count = math.ceil(route.length/.4)
    for i in range(count+1):
        distance = route.length*i/count
        x, y = route.at(distance)
        tx, ty = route.heading(distance)

        def probe(along, side, z=.60):
            return Vector((x + tx*along + ty*side, y + ty*along - tx*side, z))

        for along in (-length/2, 0., length/2):
            for side in (-width/2, 0., width/2):
                px, py, _ = probe(along, side)
                assert surface_at(pavement, px, py) is not None, (name, distance, px, py, 'off pavement')
                sampled += 1
        if obstacles:
            outline = [(probe(a, -width/2), probe(a, width/2)) for a in (-length/2, 0., length/2)]
            outline += [(probe(-length/2, b), probe(length/2, b)) for b in (-width/2, 0., width/2)]
            for a, b in outline:
                hit = obstacles.ray_cast(a, b-a, (b-a).length)[0]
                assert hit is None, (name, distance, tuple(hit), 'solid obstruction')
    return sampled


def verify_marker_path(name, route, scene):
    markers = sorted((obj for obj in scene.objects if obj.name.startswith(f'Richmond pace {name} ')),
                     key=lambda obj: int(obj.name.rsplit(' ', 1)[1]))
    assert len(markers) > 35, (name, len(markers))
    for i, marker in enumerate(markers):
        distance = route.length*i/(len(markers)-1)
        intended = route.at(distance)
        assert math.dist(tuple(marker.location[:2]), intended) < .001, (marker.name, intended, marker.location)
        facing = marker.matrix_world.to_quaternion() @ Vector((1., 0., 0.))
        forward = Vector((*route.heading(distance), 0.))
        assert facing.angle(forward) < .003, marker.name
    return len(markers)


audit = native_route_offsets()
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(FOLDER / 'RichmondRaceway.fbx'))
scene = bpy.context.scene
roads = ('Racing surface', 'Apron asphalt', 'Pit road asphalt', 'Pit entrance asphalt', 'Pit exit asphalt',
         'Turn one safety access asphalt', 'Richmond paved safety access and recovery yard',
         'Pace bay return asphalt', 'Pace bay departure asphalt')
asphalt = bvh([scene.objects[name] for name in roads])
grass = bvh([scene.objects['Frontstretch grass median']])
grass_checked, grass_visible = 0, 0
for i in range(270):
    s = layout.LAP * (.897 + i * .258 / 269)
    p = layout.race_pose(s)
    for fraction in (.1, .3, .5, .7, .86):
        side = -p.width/2-layout.lerp(layout.paved_apron_width(s), p.apron, fraction)
        x, y, _ = layout.racing_surface(s, side)
        grass_checked += 1
        if surface_at(asphalt, x, y) is None:
            hit = surface_at(grass, x, y)
            assert hit is not None and abs(hit.z+.004) < .001, (i, fraction, hit)
            grass_visible += 1
assert grass_visible/grass_checked > .8, (grass_visible, grass_checked)

apron = []
for i in range(850):
    station = layout.LAP*(.855 + .35*i/849)
    p = layout.race_pose(station)
    apron.append(layout.racing_surface(station, -p.width/2-layout.running_apron_offset(station))[:2])
audit['running_apron_tow_pavement_probes'] = verify_body('running apron loaded tow', layout.Path(apron), asphalt, 2.9, 12.)
for key, route in (('entrance', layout.ENTRY), ('pit_road', layout.PIT), ('exit', layout.EXIT)):
    audit[f'{key}_stock_car_pavement_probes'] = verify_body(key, route, asphalt, 2.1, 5.4)

pace_return, pace_departure, _, _ = layout.pace_paths()
solids = bvh([obj for obj in scene.objects if obj.type == 'MESH'], solids_only=True)
audit['pace_paths'] = {}
for key, route in (('return', pace_return), ('departure', pace_departure)):
    audit['pace_paths'][key] = dict(length_m=route.length, markers=verify_marker_path(key, route, scene),
                                    car_pavement_probes=verify_body(key, route, asphalt, 2.1, 5.4, solids))
assert math.dist(pace_return.points[-1], pace_departure.points[0]) < .001
bay_end = pace_return.heading(pace_return.length)
bay_start = pace_departure.heading(0.)
assert math.dist(bay_end, bay_start) < .005, (bay_end, bay_start)
audit.update(visible_grass_probes=grass_visible, grass_region_probes=grass_checked,
             asphalt_shoulder_width_at_start_finish_m=layout.paved_apron_width(0.),
             pace_bay_position_xy_m=layout.PACE_BAY,
             pace_return_station_m=layout.PACE_RETURN_STATION, pace_departure_station_m=layout.PACE_DEPARTURE_STATION)
(FOLDER / 'grass-and-pace-verification.json').write_text(json.dumps(audit, indent=2)+'\n', encoding='utf-8')
print(json.dumps(audit, indent=2))
