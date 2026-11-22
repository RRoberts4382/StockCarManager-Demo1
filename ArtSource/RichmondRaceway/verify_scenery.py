import math
import os
import re
import sys

sys.path.insert(0, os.path.dirname(__file__))

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree
import richmond_layout as layout
import richmond_scenery as scenery

bpy.ops.wm.read_factory_settings(use_empty=True)
scenery.build()
objects = list(bpy.context.scene.objects)
for obj in objects:
    assert obj.type == 'MESH' and obj.data.polygons and all(poly.area > 1e-8 for poly in obj.data.polygons), obj.name
    assert all(math.isfinite(c) for c in obj.location), obj.name
for prefix in ("grandstand section", "grandstand bench", "standing fan", "pedestrian walk",
               "floodlight", "street lamp", "recovery approach", "recovery bay",
               "recovery depart", "tow bay", "ambulance bay", "scoring pylon marker"):
    print(prefix, sum(obj.name.startswith('Richmond '+prefix) for obj in objects))
bpy.context.view_layer.update()

def tree(objects):
    points, faces = [], []
    for obj in objects:
        offset = len(points)
        points.extend(obj.matrix_world @ v.co for v in obj.data.vertices)
        faces.extend(tuple(v+offset for v in p.vertices) for p in obj.data.polygons)
    return BVHTree.FromPolygons(points, faces)

stands = [ob for ob in objects if ob.name.startswith('Richmond grandstand section')]
bvh = tree(stands)
benches = [ob for ob in objects if ob.name.startswith('Richmond grandstand bench')]
ratios = []
for index in range(1,len(stands)+1):
    targets = [ob.location + Vector((0,0,.64)) for ob in benches if
               ob.name.startswith(f'Richmond grandstand bench {index:03d}-')]
    center = sum(targets, Vector()) / len(targets)
    nearest = layout.nearest_race((center.x,center.y))
    observations = []
    for along in (-28,0,28):
        eye = Vector(layout.racing_surface(nearest+along,0.0)) + Vector((0,0,1.6))
        visible = sum(bvh.ray_cast(eye, target-eye, (target-eye).length-.02)[0] is None
                      for target in targets)
        observations.append(visible/len(targets))
    ratios.extend(observations)
    print('Grandstand visible seat rows',index, *(f'{r:.0%}' for r in observations))
assert min(ratios) > 0.5, min(ratios)

solids = [obj for obj in bpy.data.collections["Richmond | FanGrounds buildings"].objects]
# A visibility test against building volumes ignores the paved foundation.
trim_points, trim_faces = [], []
for obj in solids:
    offset = len(trim_points)
    trim_points.extend(v.co for v in obj.data.vertices)
    trim_faces.extend(tuple(v+offset for v in f.vertices) for f in obj.data.polygons if
                      max(obj.data.vertices[v].co.z for v in f.vertices) > .11)
footprints = BVHTree.FromPolygons(trim_points, trim_faces)
for obj in objects:
    if "pedestrian walk" not in obj.name or not obj.name.endswith("start"):
        continue
    end = bpy.data.objects[obj.name.removesuffix("start") + "end"].location
    for i in range(21):
        start = obj.location.lerp(end, i/20)
        assert footprints.ray_cast(start, (0,0,1))[0] is None, (obj.name, tuple(start))

gate = layout.RECOVERY_STATION
for prefix,index in [('response outbound',6), ('recovery entry',1), ('recovery exit',8)]:
    end = bpy.data.objects[f'Richmond {prefix} {index:02d}'].location
    projected = layout.nearest_race(tuple(end[:2]))
    assert abs(projected-gate) < layout.RECOVERY_OPENING*.5-4, (prefix,projected)
    p = layout.race_pose(projected)
    inward = -(end.x-p.x)*p.nx-(end.y-p.y)*p.ny
    assert p.width*.5 < inward < p.width*.5+p.apron, (prefix,inward)
print(f'SCENERY VERIFIED {len(objects)} objects, {min(ratios):.0%} minimum seating visibility')