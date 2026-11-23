import sys
from pathlib import Path

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

for filepath in map(Path, sys.argv[sys.argv.index("--") + 1:]):
    if filepath.suffix == ".blend":
        bpy.ops.wm.open_mainfile(filepath=str(filepath), load_ui=False, use_scripts=False)
    else:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=str(filepath))
    objects = bpy.context.scene.objects
    wall = objects["Outside track wall"]
    fence = objects["Catch fence open posts and rails"]
    cap = BVHTree.FromPolygons([wall.matrix_world @ v.co for v in wall.data.vertices],
                               [tuple(face.vertices) for face in wall.data.polygons])
    bases = 0
    for vertex in fence.data.vertices:
        point = fence.matrix_world @ vertex.co
        hit, normal, _, _ = cap.ray_cast(point + Vector((0, 0, .05)), (0, 0, -1), 4.)
        if hit is None or normal.z < .8 or point.z < hit.z - .025:
            raise ValueError(f"Fence outside or below wall cap in {filepath}: {point}")
        bases += abs(point.z - hit.z) <= .025
    if bases < 900:
        raise ValueError(f"Expected post bases at cap height, got {bases} in {filepath}")
    print(f"{filepath.suffix}: all {len(fence.data.vertices)} vertices over cap, {bases} at cap height")
