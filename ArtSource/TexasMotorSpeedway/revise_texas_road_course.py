"""Repair the Texas road-course ribbon and the editable oval finish marks."""

import json
from pathlib import Path
import sys

import bpy


TARGET = 'Infield road-course pavement'


def mesh_attributes(obj):
    vertices = [tuple(vertex.co) for vertex in obj.data.vertices]
    materials = [slot.material.name for slot in obj.material_slots]
    faces = [(sorted(face.vertices), face.material_index) for face in obj.data.polygons]
    uvs = {layer.name: [sorted((obj.data.loops[i].vertex_index, tuple(layer.data[i].uv))
                             for i in face.loop_indices) for face in obj.data.polygons]
           for layer in obj.data.uv_layers}
    return vertices, materials, faces, uvs


def face_up(obj):
    attributes = mesh_attributes(obj)
    def z_cross(face):
        a, b, c = (obj.matrix_world @ obj.data.vertices[i].co for i in face.vertices[:3])
        return (b - a).cross(c - a).z
    flipped = 0
    for face in obj.data.polygons:
        if z_cross(face) < 0:
            face.flip()
            flipped += 1
    obj.data.update()
    obj.data.calc_loop_triangles()
    assert all(z_cross(face) > 0 for face in obj.data.loop_triangles)
    assert mesh_attributes(obj) == attributes, f'{obj.name}: dimensions, material assignments, or UVs changed'
    return {'mesh': obj.name, 'correctedPolygons': flipped,
            'verifiedUpwardTriangles': len(obj.data.loop_triangles), 'uvLayers': list(attributes[3])}


def main():
    source, output = map(Path, sys.argv[sys.argv.index('--') + 1:])
    bpy.ops.wm.open_mainfile(filepath=str(source), load_ui=False, use_scripts=False)
    bpy.context.preferences.filepaths.save_version = 0
    ribbon = face_up(bpy.data.objects[TARGET])
    assert ribbon['correctedPolygons'] == 119 and ribbon['verifiedUpwardTriangles'] == 238, ribbon
    tiles = [face_up(obj) for obj in bpy.context.scene.objects if obj.name.startswith('Start finish checker')]
    assert len(tiles) == 30 and all(tile['correctedPolygons'] == 1 and tile['verifiedUpwardTriangles'] == 2
                                    for tile in tiles), tiles

    output.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(output / 'TexasMotorSpeedway.blend'), compress=True)
    (output / 'editable-check.json').write_text(json.dumps({
        'roadCourse': ribbon, 'editableOnlyFinishTiles': tiles,
    }, indent=2) + '\n', encoding='utf-8')


if __name__ == '__main__':
    main()
