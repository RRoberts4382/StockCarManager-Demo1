"""Validate the North Wilkesboro curved tiers and aisle steps after FBX import."""
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
from rebuild_component import source_directory

sys.path.insert(0, str(source_directory("NorthWilkesboroSpeedway", "NorthWilkesboroRebuild")))
import north_wilkesboro_scenery as scenery
import wilkesboro_layout as layout


def main():
    fbx, report = map(Path, sys.argv[sys.argv.index("--") + 1:])
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(fbx))
    bpy.context.view_layer.update()
    objects = bpy.context.scene.objects
    parts = [objects[f"North Wilkesboro grandstand section {i:03d}"] for i in range(9, 19)]
    trees = [scenery.site_tree([part]) for part in parts]
    site = scenery.site_tree(o for o in objects if o.name in
                             ("Racing surface", "Apron asphalt", "Infield asphalt",
                              "Outer banking embankment", "Mown grass site"))
    start, end = layout.marker(475., 302.), layout.marker(477., 155.)
    interval = (end - start) / 10
    pitch = 1.07

    def first(station):
        xy = scenery.outer(station, 6.8)
        edge = layout.racing_surface(station, layout.race_pose(station).width * .5)[2]
        return max(scenery.ground(site, xy) + 1.62, edge + 1.66)

    def surface(tree, station, depth, z, description):
        xy = scenery.outer(station, 6.8 + depth)
        point, normal, _, _ = tree.ray_cast(Vector((*xy, z + .10)), Vector((0, 0, -1)), .20)
        if point is None or normal.z < .8 or abs(point.z - z) > .075:
            raise ValueError(f"{description}: missing or misaligned tread at {station:.3f}, {depth:.3f}, {z:.3f}")
        return point.z

    checks = seams = 0
    max_seam = 0.
    for deck, rows, origin, lift, rise in ((0, 24, 0., 0., .55), (1, 19, 6.7, 7.8, .68)):
        risers = math.ceil(rise / .19)
        for row in range(rows):
            depth = origin + row * pitch
            for index, tree in enumerate(trees):
                station = start + interval * (index + .5)
                z = first(station) + lift + row * rise
                for step in range(risers):
                    underfoot = depth - pitch + (step + .5) * pitch / risers
                    height = z - rise + rise * (step + 1) / risers
                    surface(tree, station, underfoot, height, f"deck {deck + 1} aisle {index + 1}")
                    checks += 1
                if index < len(trees) - 1:
                    boundary = start + interval * (index + 1)
                    samples = [surface(trees[part], s, depth - .91, first(s) + lift + row * rise,
                                       f"deck {deck + 1} section seam {index + 1}")
                               for part, s in ((index, boundary - .015), (index + 1, boundary + .015))]
                    delta = abs(samples[0] - samples[1])
                    max_seam = max(max_seam, delta)
                    if delta > .025:
                        raise ValueError(f"Deck {deck + 1} seam {index + 1} steps {delta:.3f}m")
                    seams += 1
    report.write_text(json.dumps(dict(aisles=10, decks=2, aisleTreadProbes=checks, joinedRowSeams=seams,
                                      maxSeamHeightDifferenceMeters=max_seam), indent=2), encoding="utf-8")
    print(f"Verified {seams} joined row seams and {checks} aisle treads")


if __name__ == "__main__":
    main()
