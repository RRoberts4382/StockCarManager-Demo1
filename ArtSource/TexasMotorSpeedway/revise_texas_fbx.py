"""Patch only the road-course face order and normals in the deployed Texas FBX.

The matching Blender scene contains non-exported geometry and hidden objects.
Keeping the existing scenery export preserves its exact object set and data.
"""

from dataclasses import dataclass
import io
import json
from pathlib import Path
import struct
import sys
import zlib


HEADER = struct.Struct('<IIIB')
NULL = bytes(HEADER.size)
TARGET = b'Infield road-course pavement.001\x00\x01Geometry'


@dataclass
class Node:
    name: bytes
    count: int
    properties: bytes
    children: list
    terminator: bytes

    def one(self, name):
        matches = [node for node in self.children if node.name == name]
        if len(matches) != 1:
            raise ValueError(f"Expected exactly one {name!r} in {self.name!r}")
        return matches[0]


def read_node(data, start):
    end, count, prop_size, name_size = HEADER.unpack_from(data, start)
    if not start < end <= len(data) or not name_size:
        raise ValueError(f"Unexpected FBX node at {start}")
    props_at = start + HEADER.size + name_size
    child_at = props_at + prop_size
    children = []
    name = data[start + HEADER.size:props_at]
    properties = data[props_at:child_at]
    while child_at < end and data[child_at:child_at + HEADER.size] != NULL:
        child, child_at = read_node(data, child_at)
        children.append(child)
    terminator = data[child_at:end]
    if terminator not in (b'', NULL):
        raise ValueError(f"Unexpected FBX terminator for {name!r}")
    return Node(name, count, properties, children, terminator), end


def load_export(data):
    if not data.startswith(b'Kaydara FBX Binary  \x00\x1a\x00') or struct.unpack_from('<I', data, 23)[0] != 7400:
        raise ValueError('The Texas export is expected to be binary FBX 7400')
    index = 27
    root = Node(b'', 0, b'', [], NULL)
    while data[index:index + HEADER.size] != NULL:
        node, index = read_node(data, index)
        root.children.append(node)
    return root, index + HEADER.size


def write_node(stream, node):
    start = stream.tell()
    stream.write(NULL)
    stream.write(node.name)
    stream.write(node.properties)
    for child in node.children:
        write_node(stream, child)
    stream.write(node.terminator)
    end = stream.tell()
    stream.seek(start)
    stream.write(HEADER.pack(end, node.count, len(node.properties), len(node.name)))
    stream.seek(end)


def rebuild(data, root, old_footer):
    stream = io.BytesIO()
    stream.write(data[:27])
    for node in root.children:
        write_node(stream, node)
    stream.write(root.terminator)
    footer = data[old_footer:]
    fixed = 20  # Original 16-byte footer identifier and four reserved bytes.
    old_pad = 16 - ((old_footer + fixed) % 16)
    assert footer[fixed:fixed + old_pad] == bytes(old_pad)
    stream.write(footer[:fixed])
    stream.write(bytes(16 - (stream.tell() % 16)))
    stream.write(footer[fixed + old_pad:])
    return stream.getvalue()


def array_values(node, kind):
    value = node.properties
    count, encoding, length = struct.unpack_from('<III', value, 1)
    if node.count != 1 or value[:1] != kind or len(value) != 13 + length or encoding not in (0, 1):
        raise ValueError(f"Unexpected FBX array for {node.name!r}")
    data = zlib.decompress(value[13:]) if encoding else value[13:]
    return struct.unpack(f'<{count}{kind.decode()}', data)


def set_array(node, kind, values):
    data = struct.pack(f'<{len(values)}{kind.decode()}', *values)
    compressed = zlib.compress(data)
    payload = min((data, compressed), key=len)
    node.properties = kind + struct.pack('<III', len(values), payload is compressed, len(payload)) + payload


def flip_target(geometry):
    polygons = geometry.one(b'PolygonVertexIndex')
    edges = geometry.one(b'Edges')
    layer = geometry.one(b'LayerElementNormal')
    normals, corners = layer.one(b'Normals'), layer.one(b'NormalsIndex')
    assert not any(node.name.startswith((b'LayerElementUV', b'LayerElementTangent', b'LayerElementColor'))
                   for node in geometry.children)
    xyz = array_values(geometry.one(b'Vertices'), b'd')
    original = array_values(polygons, b'i')
    original_normals = array_values(normals, b'd')
    original_corners = array_values(corners, b'i')
    original_edges = array_values(edges, b'i')
    assert len(original) == len(original_corners) == 238 * 3
    new_triangles, new_corners, reorder_edges = [], [], {}
    for index in range(0, len(original), 3):
        a, b, c = original[index:index + 3]
        assert a >= 0 and b >= 0 and c < 0
        c = ~c
        p, q, r = [xyz[3*v:3*v + 3] for v in (a, b, c)]
        area_z = (q[0]-p[0])*(r[1]-p[1]) - (q[1]-p[1])*(r[0]-p[0])
        assert area_z < 0., f"Polygon {index // 3} does not face down"
        new_triangles.extend((a, c, ~b))
        new_corners.extend((original_corners[index], original_corners[index + 2], original_corners[index + 1]))
        # a->b, b->c, c->a became a->c, c->b, b->a.
        for old, new in enumerate((2, 1, 0)):
            reorder_edges[index + old] = index + new
    assert max(original_normals[2::3]) < 0.
    set_array(polygons, b'i', new_triangles)
    set_array(edges, b'i', [reorder_edges[index] for index in original_edges])
    set_array(normals, b'd', [-value for value in original_normals])
    set_array(corners, b'i', new_corners)


def compare_payloads(a, b):
    assert (a.name, a.count, a.terminator, len(a.children)) == (b.name, b.count, b.terminator, len(b.children))
    changes = [] if a.properties == b.properties else [a.name.decode()]
    for old, new in zip(a.children, b.children):
        changes.extend(compare_payloads(old, new))
    return changes


def main():
    original, output = map(Path, sys.argv[1:])
    source = original.read_bytes()
    root, footer_at = load_export(source)
    prior, _ = load_export(source)
    objects = root.one(b'Objects')
    targets = [node for node in objects.children if node.name == b'Geometry' and TARGET in node.properties]
    assert len(targets) == 1
    flip_target(targets[0])
    revised = rebuild(source, root, footer_at)
    round_trip, _ = load_export(revised)
    changed = compare_payloads(prior, round_trip)
    assert sorted(changed) == ['Edges', 'Normals', 'NormalsIndex', 'PolygonVertexIndex'], changed
    output.write_bytes(revised)
    output.with_name('export-check.json').write_text(json.dumps({
        'modelsPreserved': sum(node.name == b'Model' for node in objects.children),
        'materialsPreserved': sum(node.name == b'Material' for node in objects.children),
        'changedGeometry': TARGET.split(b'\x00')[0].decode(),
        'changedPayloads': sorted(changed),
        'trianglesReversed': 238,
    }, indent=2) + '\n', encoding='utf-8')


if __name__ == '__main__':
    main()
