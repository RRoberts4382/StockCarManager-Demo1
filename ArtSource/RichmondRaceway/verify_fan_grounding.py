"""Compare exported Richmond fan roots and walk paths with their supporting ground."""

import json
import math
from pathlib import Path
import sys

import bpy

FOLDER = Path(__file__).resolve().parent
sys.path.insert(0, str(FOLDER))
from richmond_fan_grounding import STANDING_PREFIX, WALKING_PREFIX, ground_height, walking_ground


def import_scene(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path))
    bpy.context.view_layer.update()
    return bpy.context.scene


def anchors(scene):
    return {obj.name: obj for obj in scene.objects if obj.name.startswith((STANDING_PREFIX, WALKING_PREFIX))}


args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
baseline = {}
if args:
    previous = import_scene(Path(args[0]))
    baseline = {name: obj.matrix_world.copy() for name, obj in anchors(previous).items()}

scene = import_scene(FOLDER / 'RichmondRaceway.fbx')
ground = walking_ground(scene.objects)
found = anchors(scene)
groups, walks, corrections = {}, {}, []
worst_root_gap = 0.
if baseline:
    assert baseline.keys() == found.keys(), 'Standing and walking anchor names must be preserved.'

for name, obj in found.items():
    matrix = obj.matrix_world
    point = matrix.translation
    height = ground_height(ground, point.x, point.y)
    assert height is not None, name
    gap = abs(point.z - height)
    worst_root_gap = max(worst_root_gap, gap)
    if baseline:
        old = baseline[name]
        assert math.dist(tuple(old.translation[:2]), tuple(point[:2])) < .0001, name
        assert max(abs(matrix[row][col] - old[row][col]) for row in range(3) for col in range(3)) < .00001, name
        corrections.append(abs(old.translation.z - point.z))

    if name.startswith(STANDING_PREFIX):
        number = int(name.rsplit(' ', 1)[1])
        if number >= 1200:
            group = 'inside_wall'
        elif number >= 600:
            group = 'campground'
        elif number >= 501:
            group = 'pit_amenities'
        else:
            group = 'protected_fan_paths'
        groups[group] = groups.get(group, 0) + 1
    else:
        route, label = name.rsplit(' ', 1)
        walks.setdefault(route, {})[label] = point.copy()

assert worst_root_gap < .0002, worst_root_gap
assert groups and walks, 'Expected standing and walking anchors in the exported venue.'
walk_stats = {}
for name, ends in sorted(walks.items()):
    start, end = ends['start'], ends['end']
    gaps = []
    sample_count = max(80, math.ceil((end - start).length / .10))
    for sample in range(sample_count + 1):
        point = start.lerp(end, sample / sample_count)
        height = ground_height(ground, point.x, point.y)
        assert height is not None, (name, sample)
        gaps.append(point.z - height)
    walk_stats[name] = dict(probes=len(gaps), minimum_linear_root_gap_m=min(gaps), maximum_linear_root_gap_m=max(gaps))

minimum_walk_gap = min(stats['minimum_linear_root_gap_m'] for stats in walk_stats.values())
maximum_walk_gap = max(stats['maximum_linear_root_gap_m'] for stats in walk_stats.values())
assert abs(minimum_walk_gap) < .025 and maximum_walk_gap < .025, (minimum_walk_gap, maximum_walk_gap)
result = dict(
    grounded_standing_anchors=groups,
    grounded_walking_endpoints=len(walks)*2,
    max_anchor_to_surface_gap_m=worst_root_gap,
    max_corrected_anchor_height_m=max(corrections) if corrections else None,
    original_anchor_xy_and_basis_preserved=bool(baseline),
    walking_interior_probes=sum(stats['probes'] for stats in walk_stats.values()),
    walking_linear_root_gap_m=[minimum_walk_gap, maximum_walk_gap],
    walking_paths=walk_stats,
)
(FOLDER / 'fan-grounding-verification.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
print(json.dumps({key: value for key, value in result.items() if key != 'walking_paths'}, indent=2))
