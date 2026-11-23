"""Verify occupied Richmond standard-car stalls and recovery clearances in Blender."""
import json
import math
from pathlib import Path
import sys

import bpy

root = Path(__file__).resolve().parent
base = root/"verify_hauler_lot.py"
proof = {"__file__": str(base)}
exec(compile(base.read_text(encoding="utf-8"),str(base),"exec"),proof)
scenery, layout = proof["scenery"],proof["layout"]
gate, world, yaw = proof["gate"],proof["world"],proof["yaw"]
tree, down, pavement = proof["tree"],proof["down"],proof["pavement"]


footprints = {}
for style in scenery.CAR_PROFILES:
    model = scenery.Builder(None)
    scenery.parking_vehicle(model,(0.,0.),0.,False,"Parked silver",style)
    length, width = (max(v[axis] for v in model.vertices)-min(v[axis] for v in model.vertices)
                     for axis in range(2))
    assert length <= 4.58+1e-6 and width <= 1.84+1e-6, (style,length,width)
    footprints[style] = [round(length,3),round(width,3)]

styles_by_lot = {}


def check_lot(title, center, heading, columns, rows, pitch):
    cars = bpy.data.objects[f"Richmond {title} parked vehicles"]
    painted = tree((cars,))
    colors = {m.name for m in cars.data.materials if m.name.removeprefix("Richmond ") in scenery.CAR_PAINT}
    assert len(colors) >= 5, (title,colors)
    count = 0
    shapes = {style:0 for style in scenery.CAR_PROFILES}
    for row in range(rows):
        y = (row+.5-rows*.5)*pitch
        for column in range(columns):
            x = (column+.5-columns*.5)*scenery.SUPPORT_PARKING_PITCH
            p = scenery.local(center,heading,x,y)
            hit = down(painted,p)
            assert hit is not None and hit.z >= .8, (title,column,row,hit)
            style = min(shapes,key=lambda key: abs(hit.z-scenery.CAR_PROFILES[key][3]))
            assert abs(hit.z-scenery.CAR_PROFILES[style][3])<.02, (title,column,row,hit)
            shapes[style] += 1
            count += 1
    for vertex in cars.data.vertices:
        p = cars.matrix_world @ vertex.co
        x,y = scenery.local((0,0),-heading,p.x-center[0],p.y-center[1])
        x_in_stall = (x+columns*scenery.SUPPORT_PARKING_PITCH*.5)%scenery.SUPPORT_PARKING_PITCH
        y_in_row = (y+rows*pitch*.5)%pitch-pitch*.5
        assert .055 < x_in_stall < scenery.SUPPORT_PARKING_PITCH-.055, (title,x,y)
        assert abs(y_in_row)<2.75, (title,x,y)
    assert all(shapes.values()), (title,shapes)
    styles_by_lot[title] = shapes
    return count


pose = layout.race_pose(-66.)
outer = scenery.exterior_point(-66.,111.)
markings = scenery.SUPPORT_PARKING_COUNT
support_center = scenery.SUPPORT_PARKING_FIRST_X+(markings-1)*scenery.SUPPORT_PARKING_PITCH*.5
counts = {
    "media": check_lot("media parking",world((-137.,136.)),yaw,10,1,14.1),
    "paddock": check_lot("paddock parking",world((142.,127.)),yaw,11,2,14.1),
    "frontstretch spectator": check_lot("frontstretch spectator parking",outer,
                                          math.atan2(pose.ty,pose.tx),20,2,14.1),
    "behind haulers": check_lot("support parking behind haulers",world((support_center,scenery.SUPPORT_PARKING_Y)),
                                yaw,markings,1,14.1),
}
assert counts == {"media": 10,"paddock": 22,"frontstretch spectator": 40,"behind haulers": 26}

# A full marked stall area is wider than the parked body. Keep even this
# conservative reserve clear of the haulers and the loaded/empty tow paths.
space = (support_center-markings*scenery.SUPPORT_PARKING_PITCH*.5,
         support_center+markings*scenery.SUPPORT_PARKING_PITCH*.5,
         scenery.SUPPORT_PARKING_Y-2.75,scenery.SUPPORT_PARKING_Y+2.75)
aisle = (-101.,space[1],91.6,space[2])
hauler_gap = min(rect[2]-space[3] for rect in proof["rectangles"])
assert hauler_gap >= .9, hauler_gap
assert aisle[3]-aisle[2] >= 2.65
for area in (space,aisle):
    for i in range(181):
        for j in range(4):
            x = layout.lerp(area[0],area[1],i/180)
            y = layout.lerp(area[2],area[3],j/3)
            hit = down(pavement,world((x,y)))
            assert hit is not None and abs(hit.z-scenery.RECOVERY_YARD_Z)<.0002, (x,y,hit)

clearance = {name:math.inf for name in ("loaded tow","empty tow","one-way support aisle")}
metric_dist = proof["metric_dist"]
curve_samples = proof["curve_samples"]
hermite = proof["hermite"]
entry, leave = proof["entry"],proof["leave"]
for approach, drop, depart in zip(proof["approaches"],proof["bays"],proof["departures"]):
    direction = layout.unit(layout.sub(depart,drop))
    stop = layout.add(drop,layout.mul(direction,6.25))
    in_turn = min(15.,max(3.,math.dist(entry[-1],approach)*.6))
    out_turn = min(15.,max(3.,math.dist(leave[0],depart)*.6))
    loaded = hermite(entry[-1],approach,layout.mul(layout.unit(layout.sub(entry[-1],entry[-2])),in_turn),
                     layout.mul(direction,in_turn))
    returning = hermite(depart,leave[0],layout.mul(direction,out_turn),
                        layout.mul(layout.unit(layout.sub(leave[1],leave[0])),out_turn))
    segments = (("loaded tow",proof["entry_road"][:-1]+loaded+[stop]),
                ("empty tow",[stop]+returning+proof["exit_road"][1:]))
    for name, points in segments:
        for p in curve_samples(points):
            clearance[name] = min(clearance[name],metric_dist(p,space)-3.5)
            clearance["one-way support aisle"] = min(clearance["one-way support aisle"],metric_dist(p,aisle)-3.5)
assert clearance["loaded tow"] > 19. and clearance["empty tow"] > 2.5, clearance
assert clearance["one-way support aisle"] > .30, clearance

access = layout.turn_four_gate_access()
service_gap = min(metric_dist(proof["planar"](p),space)-6.2 for p in curve_samples(access.points))
assert service_gap > 80., service_gap
assert aisle[0] > -102.5
metrics = {
    "geometry": proof["metrics"]["geometry"],
    "filled_marked_stalls": counts,
    "ordinary_cars_in_filled_stalls": sum(counts.values()),
    "body_shape_counts": styles_by_lot,
    "verified_car_dimensions_length_width_m": footprints,
    "haul_clearance_to_marked_spaces_m": round(hauler_gap,4),
    "support_aisle_width_m": round(aisle[3]-aisle[2],4),
    "minimum_tow_clearances_m": {name:round(value,4) for name,value in clearance.items()},
    "turn_four_loaded_tow_clearance_m": round(service_gap,4),
}
name = "parking-fbx-verification.json" if proof["check_fbx"] else "parking-source-verification.json"
(root/name).write_text(json.dumps(metrics,indent=2)+"\n",encoding="utf-8")
print("RICHMOND PARKING VERIFIED",json.dumps(metrics))
