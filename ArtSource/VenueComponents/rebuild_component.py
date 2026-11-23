"""Rebuild selected venue objects in a copy of an existing Blender scene."""
import argparse
import hashlib
import json
import math
from pathlib import Path
import re
import struct
import sys

import bpy

ROOT = Path(__file__).resolve().parent.parent


def source_directory(venue, archive):
    installed = ROOT / venue
    return installed if installed.is_dir() else ROOT / archive / "geometry"


def fingerprint(obj):
    digest = hashlib.sha256()
    digest.update(repr((obj.name, obj.type, tuple(tuple(row) for row in obj.matrix_world),
                        obj.hide_render, obj.hide_viewport,
                        tuple(sorted((collection.name for collection in obj.users_collection))))).encode("utf-8"))
    if obj.type == "MESH":
        for vertex in obj.data.vertices:
            digest.update(struct.pack("<fff", *vertex.co))
        for polygon in obj.data.polygons:
            digest.update(repr((tuple(polygon.vertices), polygon.material_index, polygon.use_smooth)).encode("utf-8"))
        digest.update(repr(tuple(material.name if material else None for material in obj.data.materials)).encode("utf-8"))
    return digest.hexdigest()


def replace_component(patterns, generate):
    matches = lambda name: any(re.fullmatch(pattern, name) for pattern in patterns)
    originals = {obj.name: obj for obj in bpy.context.scene.objects if matches(obj.name)}
    if not originals:
        raise ValueError("Selected component is missing from the input scene")
    untouched = {obj.name: fingerprint(obj) for obj in bpy.context.scene.objects if obj.name not in originals}
    existing = {name: fingerprint(obj) for name, obj in originals.items()}
    collections = {name: tuple(obj.users_collection) for name, obj in originals.items()}
    scratch = bpy.data.collections.new("Venue component staging")
    bpy.context.scene.collection.children.link(scratch)
    for obj in originals.values():
        bpy.data.objects.remove(obj, do_unlink=True)
    generate(scratch)
    generated = {obj.name: obj for obj in scratch.objects}
    if generated.keys() != originals.keys():
        raise ValueError(f"Component contract changed: missing {originals.keys() - generated.keys()}, "
                         f"unexpected {generated.keys() - originals.keys()}")
    for name, obj in generated.items():
        for collection in collections[name]:
            collection.objects.link(obj)
        scratch.objects.unlink(obj)
    bpy.data.collections.remove(scratch)
    bpy.context.view_layer.update()
    after = {obj.name: fingerprint(obj) for obj in bpy.context.scene.objects if obj.name not in generated}
    if untouched != after:
        raise ValueError("An unrelated scene object changed during the component rebuild")
    changed = [name for name, obj in generated.items() if fingerprint(obj) != existing[name]]
    return dict(rebuilt=len(generated), changed=sorted(changed), preserved=len(untouched))


def north_wilkesboro(component):
    sys.path.insert(0, str(source_directory("NorthWilkesboroSpeedway", "NorthWilkesboroRebuild")))
    if component == "catch-fence":
        from build_wilkesboro import catch_fence
        return replace_component((r"Catch fence open posts and rails",), catch_fence)
    import north_wilkesboro_scenery as scenery

    base = [obj for name in ("Banked course and apron", "Ground and concourses", "Pit road and crew work bays")
            for obj in bpy.data.collections["North Wilkesboro | " + name].objects
            if obj.name in ("Pit entrance asphalt", "Pit road asphalt", "Pit exit asphalt")
            or name != "Pit road and crew work bays" or obj.name.startswith(("Pit support apron", "Pit walkway", "Pit work"))]
    site = scenery.site_tree(base)
    if component == "grandstands":
        return replace_component(
            (r"North Wilkesboro grandstand (?:section |bench )(?:009|01[0-8])(?:-\d\d-[02])?",
             r"North Wilkesboro concrete concourse 9"),
            lambda target: scenery.turn_grandstands(target, target, site))
    if component == "lighting":
        return replace_component(
            (r"North Wilkesboro (?:floodlight \d+|street lamp \d+|venue and press roof lights)",),
            lambda target: scenery.venue_lights(target, target, site, scenery.press_building_site(site),
                                                scenery.rv_stations()))
    return replace_component(
        (r"North Wilkesboro frontstretch spectator parking (?:paving and spaces|parked vehicles)",),
        lambda target: scenery.spectator_parking(target, site))


def richmond(component):
    sys.path.insert(0, str(source_directory("RichmondRaceway", "RichmondFanGroundingUpdate")))
    import richmond_scenery as scenery
    import richmond_layout as layout

    if component == "grandstands":
        return replace_component(
            (r"Richmond grandstand (?:section \d{3}|bench \d{3}-\d{2}-[02])",),
            lambda target: scenery.exterior_grandstands(target, target, include_walks=False))
    if component == "lighting":
        gate = layout.race_pose(layout.RECOVERY_STATION)
        yaw = math.atan2(gate.ty, gate.tx)
        lamps = scenery.protected_walkway_lamps(lambda x, y: scenery.local((gate.x, gate.y), yaw, x, y))
        return replace_component(
            (r"Richmond (?:floodlight \d+|street lamp \d+|venue floodlight and street lamp fixtures)",),
            lambda target: scenery.venue_lighting(target, target, lamps))
    return replace_component(
        (r"Richmond frontstretch spectator parking (?:paving and spaces|parked vehicles)",),
        scenery.spectator_parking)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--track", choices=("richmond", "north-wilkesboro"), required=True)
    parser.add_argument("--component", choices=("grandstands", "spectator-parking", "lighting", "catch-fence"), required=True)
    parser.add_argument("--blend", type=Path, required=True, help="Existing editable venue")
    parser.add_argument("--out", type=Path, required=True, help="Separate candidate output directory")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
    if args.track == "richmond" and args.component == "catch-fence":
        parser.error("The catch-fence rebuild is available for North Wilkesboro.")
    output = args.out.resolve()
    source = args.blend.resolve()
    target_blend = output / source.name
    if source == target_blend or target_blend.exists():
        parser.error("Choose a fresh output directory separate from the source scene")
    bpy.ops.wm.open_mainfile(filepath=str(source), load_ui=False, use_scripts=False)
    action = richmond if args.track == "richmond" else north_wilkesboro
    report = action(args.component)
    output.mkdir(parents=True, exist_ok=True)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=str(target_blend))
    kinds = {"MESH"} if args.track == "richmond" else {"MESH", "EMPTY"}
    bpy.ops.export_scene.fbx(filepath=str(target_blend.with_suffix(".fbx")), axis_forward="-Z", axis_up="Y",
                             use_mesh_modifiers=True, object_types=kinds, add_leaf_bones=False, bake_anim=False)
    (output / "component-verification.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(f"Rebuilt {report['rebuilt']} objects ({len(report['changed'])} changed), preserved {report['preserved']}")


if __name__ == "__main__":
    main()

