"""Verify the complete wall-to-grandstand concourse in source or imported FBX."""
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

root = Path(__file__).resolve().parent
sys.path.insert(0, str(root))
import richmond_layout as layout
import richmond_scenery as scenery

bpy.ops.wm.read_factory_settings(use_empty=True)
source = root / "build_richmond.py"
builder = {"__file__": str(source)}
exec(compile(source.read_text(encoding="utf-8").split(
    "bpy.ops.wm.read_factory_settings(use_empty=True)", 1)[0], str(source), "exec"), builder)
check_fbx = "--check-fbx" in sys.argv
if check_fbx:
    bpy.ops.import_scene.fbx(filepath=str(root / "RichmondRaceway.fbx"))
else:
    builder["race_geometry"]()
    scenery.build()
bpy.context.view_layer.update()


def tree(*names):
    vertices, faces = [], []
    for name in names:
        obj = bpy.data.objects[name]
        offset = len(vertices)
        vertices.extend(obj.matrix_world @ v.co for v in obj.data.vertices)
        faces.extend(tuple(i+offset for i in polygon.vertices)
                     for polygon in obj.data.polygons)
    return BVHTree.FromPolygons(vertices,faces)


def below(mesh, x, y):
    return mesh.ray_cast((x,y,15.), (0,0,-1))


paving_names = ("Richmond concrete perimeter walkway","Richmond frontstretch concrete concourse")
paving = tree(*paving_names)
shoulder = tree("Outer banking embankment","Mown grass site")
wall = tree("Outside continuous concrete wall")
driving = tree("Racing surface")

seen = 0
min_berm_gap = math.inf
max_berm_gap = 0.
max_wall_foot_step = 0.
for i in range(2001):
    station = -240.+.25*i
    pose = layout.race_pose(station)
    edge_z = layout.racing_surface(station,pose.width*.5)[2]
    wall_back = scenery.exterior_point(station,.51)
    pavement, *_ = below(paving,*wall_back)
    wall_cap, *_ = below(wall,*wall_back)
    assert pavement is not None and wall_cap is not None, (station,"wall support")
    max_wall_foot_step = max(max_wall_foot_step,abs(wall_cap.z-layout.WALL_HEIGHT-pavement.z))
    asphalt = scenery.exterior_point(station,-.05)
    assert below(driving,*asphalt)[0] is not None, (station,"track edge")
    assert below(paving,*asphalt)[0] is None, (station,"paving on racing surface")
    assert max_wall_foot_step < .018, (station,max_wall_foot_step)

    for offset in (.571,.63,.8,1.0,1.5,2.,3.,4.,4.19,4.24,4.26,5.,6.,8.99,9.01,
                   11.,15.,21.,27.,34.,41.,48.,56.,60.):
        xy = scenery.exterior_point(station,offset)
        hit, normal, *_ = below(paving,*xy)
        soil, *_ = below(shoulder,*xy)
        assert hit is not None and normal.z > .50, (station,offset,"concourseless/invalid winding")
        assert soil is not None and hit.z > soil.z+.003, (station,offset,"visible grass")
        if offset <= 4.19:
            min_berm_gap = min(min_berm_gap,hit.z-soil.z)
            max_berm_gap = max(max_berm_gap,hit.z-soil.z)
        if offset >= 4.26:
            assert abs(hit.z-scenery.PERIMETER_WALK_Z) < .0001, (station,offset,"stepped walkway")
        seen += 1

grandstands = sorted((o for o in bpy.context.scene.objects
                      if o.name.startswith("Richmond grandstand section")),key=lambda o:o.name)
assert len(grandstands) == 14
for stand in grandstands:
    # The first box in each authored stand is its independent concrete base.
    for vertex in list(stand.data.vertices)[:8]:
        start = stand.matrix_world @ vertex.co
        assert below(paving,start.x,start.y)[0] is not None, (stand.name,"uncovered base")

paths = [o for o in bpy.context.scene.objects if o.name.startswith("Richmond pedestrian walk ")
         and o.name.endswith(" start") and 11 <= int(o.name.split()[-2]) <= 18]
assert len(paths) == 8
for start in paths:
    end = bpy.data.objects[start.name.removesuffix("start")+"end"]
    a, b = start.matrix_world.translation,end.matrix_world.translation
    for i in range(65):
        point = a.lerp(b,i/64)
        hit, *_ = below(paving,point.x,point.y)
        assert hit is not None and abs(point.z-hit.z)<.012, (start.name,i)

metrics = {
    "input": "FBX reimport" if check_fbx else "Blender scene generator",
    "frontage_verified_m": 500,
    "concrete_surface_probes": seen,
    "outside_wall_base_probes": 2001,
    "race_edge_clearance_probes": 2001,
    "grandstand_foundations": len(grandstands),
    "pedestrian_routes_in_concourse": len(paths),
    "max_wall_foot_height_step_m": round(max_wall_foot_step,6),
    "concrete_height_over_original_berm_m": [round(min_berm_gap,6),round(max_berm_gap,6)],
}
name = "concourse-fbx-verification.json" if check_fbx else "concourse-source-verification.json"
(root / name).write_text(json.dumps(metrics,indent=2)+"\n",encoding="utf-8")
print("RICHMOND CONCOURSE VERIFIED",json.dumps(metrics))

if "--render" in sys.argv:
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.render_aa = "16"
    shading = scene.display.shading
    shading.light = "STUDIO"
    shading.color_type = "MATERIAL"
    shading.show_backface_culling = True
    shading.show_shadows = True
    shading.show_cavity = True
    shading.cavity_type = "BOTH"
    scene.render.resolution_x = 1220
    scene.render.resolution_y = 800
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.exposure = .55
    bpy.data.collections["Richmond | Unity live scenery anchors"].hide_render = True
    for suffix,location,look,scale in (
        ("start-finish",(-13.,-154.,23.),(-9.,-167.,4.7),90.),
        ("turn-one-bank",(220.,-70.,54.),(200.,-60.,12.),120.),
        ("turn-four-bank",(-216.,-97.,65.),(-199.,-114.,5.),113.),
    ):
        scene.camera = builder["camera"](f"Concourse QA {suffix}", location, look, scale)
        scene.render.filepath = str(root/f"Richmond-concourse-{suffix}.png")
        bpy.ops.render.render(write_still=True)