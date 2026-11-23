"""Build editable scenery for the features annotated in MichiganTrackLayout.pdn."""

from __future__ import annotations

import json
import math
import random
import sys
from pathlib import Path

import bpy
from mathutils import Vector
from mathutils.geometry import tessellate_polygon

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "VenueComponents"))
from curved_grandstand import curved_grandstand
from venue_geometry import CAR_PAINT, Builder, local, marker, park_car, parking_vehicle


COLORS = {
    "Infield asphalt": (.105, .115, .121),
    "Service asphalt": (.132, .143, .147),
    "Paddock concrete": (.46, .455, .425),
    "Section concrete": (.55, .542, .515),
    "Concrete steps": (.57, .56, .532),
    "Bench aluminium": (.61, .644, .66),
    "Michigan blue seating": (.09, .215, .41),
    "Smoked glazing": (.088, .174, .195),
    "Lake blue glazing": (.15, .34, .44),
    "Garage panel": (.64, .68, .67),
    "Garage door": (.28, .34, .355),
    "Light standing seam": (.57, .60, .614),
    "Dark standing seam": (.225, .27, .278),
    "Fixture steel": (.28, .318, .34),
    "Fixture lens": (.94, .96, .92),
    "RV ivory": (.87, .846, .775),
    "Tires": (.045, .052, .054),
    "Trailer white": (.82, .845, .85),
    "Trailer hardware": (.50, .55, .58),
    "Camp gravel": (.34, .341, .305),
    "Warm skin": (.51, .31, .21),
    "Tan skin": (.68, .47, .32),
    "Dark skin": (.36, .22, .16),
    "Denim trousers": (.12, .145, .21),
    "Walkway steel": (.29, .329, .34),
    "Pavement paint": (.79, .82, .79),
    "Portable toilet": (.133, .30, .45),
    "Rubbish bin": (.11, .28, .19),
    "Awning crimson": (.61, .10, .10),
    "Awning cyan": (.09, .42, .56),
    "Awning sand": (.70, .61, .35),
}
COLORS.update(zip(CAR_PAINT, (
    (.09, .14, .26), (.52, .09, .065), (.34, .46, .36), (.67, .73, .75),
    (.115, .13, .15), (.53, .344, .21), (.82, .87, .90), (.12, .28, .58),
)))
ADS = (
    ("MOTORSTATE", "PRECISION PARTS", (.07, .21, .42)),
    ("APEXLINE", "COMPETITION CLUTCH", (.55, .09, .055)),
    ("FORGE 8", "MOTOR OIL", (.10, .30, .18)),
    ("GREAT LAKES", "PERFORMANCE BRAKES", (.46, .217, .07)),
    ("IRONMILE", "SHOP EQUIPMENT", (.24, .12, .37)),
    ("SHIFTWELL", "GEAR AND AXLE", (.087, .37, .44)),
    ("NORTHLINE", "DIAGNOSTIC TOOLS", (.392, .13, .13)),
    ("REVWORKS", "RACE EXHAUST", (.095, .225, .27)),
)


def collection(name):
    result = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(result)
    return result


def material(name):
    result = bpy.data.materials.get(name)
    if result is None:
        result = bpy.data.materials.new(name)
        color = COLORS[name]
        result.diffuse_color = (*color, 1)
        result.use_nodes = True
        surface = result.node_tree.nodes.get("Principled BSDF")
        surface.inputs["Base Color"].default_value = (*color, 1)
        surface.inputs["Roughness"].default_value = .24 if "glazing" in name else .78
        if "steel" in name or "aluminium" in name:
            surface.inputs["Metallic"].default_value = .48
    return result


def builder(target):
    return Builder(target, material)


def count_between(start, end, spacing):
    return max(1, math.ceil((end - start) / spacing))


def stations(start, end, spacing):
    count = count_between(start, end, spacing)
    return [start + (end - start) * i / count for i in range(count + 1)]


def signed_area(poly):
    return sum(x * y2 - y * x2 for (x, y), (x2, y2) in zip(poly, poly[1:] + poly[:1])) * .5


def simplify_pixel_outline(poly, spacing=.8):
    """Collapse tablet-pixel stair steps while retaining intentional openings."""
    result = [tuple(poly[0])]
    for point in poly[1:]:
        if math.dist(result[-1], point) >= spacing:
            result.append(tuple(point))
    for _ in range(2):
        result = [
            (.15 * result[i - 1][0] + .7 * x + .15 * result[(i + 1) % len(result)][0],
             .15 * result[i - 1][1] + .7 * y + .15 * result[(i + 1) % len(result)][1])
            for i, (x, y) in enumerate(result)
        ]
    return result


def polygon_surface(target, name, points, level, surface):
    poly = [Vector((x, y, level)) for x, y in points if math.isfinite(x) and math.isfinite(y)]
    if signed_area([tuple(point.xy) for point in poly]) < 0:
        poly.reverse()
    model = builder(target)
    for indices in tessellate_polygon([poly]):
        triangle = tuple(poly[index] for index in indices)
        normal_z = (triangle[1] - triangle[0]).cross(triangle[2] - triangle[0]).z
        if abs(normal_z) > 1e-6:
            model.quad(triangle if normal_z > 0 else triangle[::-1], surface)
    return model.bake(name)


def road(model, points, width, level, surface, trim, pad_outline):
    # Shared cross-sections let the roadway and the infield join strips use
    # exactly the same pavement edge through turns and along the straight.
    dense = []
    for a, b in zip(points, points[1:]):
        count = count_between(0, math.dist(a, b), 2.0)
        dense.extend(tuple(x + (y - x) * i / count for x, y in zip(a, b))
                     for i in range(count))
    dense.append(points[-1])
    points = dense
    rows = []
    for i, (x, y) in enumerate(points):
        prev = points[max(0, i - 1)]
        nxt = points[min(len(points) - 1, i + 1)]
        vector = Vector((nxt[0] - prev[0], nxt[1] - prev[1])).normalized()
        dx, dy = -vector.y * width * .5, vector.x * width * .5
        z = level(x, y) if callable(level) else level
        rows.append(((x + dx, y + dy, z), (x - dx, y - dy, z)))

    def segment(shape):
        clipped = trim(shape)
        shape = [point for i, point in enumerate(clipped) if point != clipped[i - 1]]
        if len(shape) >= 3 and abs(signed_area(shape)) > 1e-5:
            if signed_area(shape) < 0:
                shape.reverse()
            model.quad([(*p, level) for p in shape], surface)

    for a, b in zip(rows, rows[1:]):
        segment([a[0][:2], a[1][:2], b[1][:2], b[0][:2]])

    # The painted image has a thin ragged gap between gray and yellow.
    # At an adjacent pad edge, carry the strip from the roadway to a
    # 1 m overlap with the same asphalt surface and common height.
    for side in (0, 1):
        matches = []
        for row in rows:
            x, y = row[side][:2]
            if inside(pad_outline, (x, y)):
                matches.append(((x, y), (x, y)))
                continue
            dx = (x - row[1 - side][0]) / width
            dy = (y - row[1 - side][1]) / width
            across = next((d for d in range(1, 23) if inside(pad_outline, (x + d * dx, y + d * dy))), None)
            matches.append(None if across is None else ((x, y), (x + (across + 1) * dx, y + (across + 1) * dy)))
        for a, b in zip(matches, matches[1:]):
            if a is not None and b is not None:
                segment([a[0], a[1], b[1], b[0]])


def clip_pit_side(layout, polygon):
    """Keep infield finishes behind the walkway that serves pit road."""
    pose = layout.pit_pose(layout.PIT_LENGTH * .5)
    boundary_x, boundary_y, _ = pose.offset(layout.PIT_WALK_INNER + .015)

    def side(point):
        return (point[0] - boundary_x) * pose.ty - (point[1] - boundary_y) * pose.tx

    output = []
    last = polygon[-1]
    for point in polygon:
        a, b = side(last), side(point)
        if (a <= 0) != (b <= 0):
            t = a / (a - b)
            output.append(tuple(x + (y - x) * t for x, y in zip(last, point)))
        if b <= 0:
            output.append(point)
        last = point
    return output


def inside(poly, point):
    x, y = point
    result = False
    for (ax, ay), (bx, by) in zip(poly, poly[1:] + poly[:1]):
        if (ay > y) != (by > y) and x < ax + (bx - ax) * (y - ay) / (by - ay):
            result = not result
    return result


def fits(poly, center, yaw, width, depth):
    return all(inside(poly, local(center, yaw, dx * width * .5, dy * depth * .5))
               for dx in (-1, 1) for dy in (-1, 1))


def bounds(poly):
    return min(x for x, _ in poly), min(y for _, y in poly), max(x for x, _ in poly), max(y for _, y in poly)


def source_box(project, bbox):
    left, top, right, bottom = bbox
    x1, y1 = project(left, top)
    x2, y2 = project(right, bottom)
    return ((x1 + x2) * .5, (y1 + y2) * .5), abs(x2 - x1), abs(y2 - y1)


def lettering(target, name, line, xyz, yaw, size, color_name):
    """Bake flat lettering into the source and the exported venue mesh."""
    curve = bpy.data.curves.new(name, "FONT")
    curve.body = line
    curve.size = size
    curve.align_x = "CENTER"
    curve.align_y = "CENTER"
    obj = bpy.data.objects.new(name, curve)
    obj.location = xyz
    obj.rotation_euler = (math.pi * .5, 0, yaw)
    obj.data.materials.append(material(color_name))
    target.objects.link(obj)
    depsgraph = bpy.context.evaluated_depsgraph_get()
    mesh = bpy.data.meshes.new_from_object(obj.evaluated_get(depsgraph), depsgraph=depsgraph)
    result = bpy.data.objects.new(name + " face", mesh)
    result.location = xyz
    result.rotation_euler = obj.rotation_euler
    target.objects.link(result)
    bpy.data.objects.remove(obj, do_unlink=True)
    bpy.data.curves.remove(curve)
    return result


def build_infield(layout, shapes, project, datum):
    target = collection("Michigan infield roads and paved yards")
    outline = [project(*point) for point in simplify_pixel_outline(shapes["infield_asphalt"][0]["polygons"][0])]
    outline = clip_pit_side(layout, outline)
    # At the access connections, let the apron and pit connectors cover the
    # infield asphalt wherever they meet. This level is still above the grass.
    access_level = datum - .004
    polygon_surface(target, "Michigan annotated infield asphalt", outline, access_level, "Infield asphalt")
    roads = builder(target)
    # The thin gray markings delimit three connections around the large north grass area.
    # Extend their infield ends into the annotated asphalt to close the paved junctions.
    for route in shapes["road_centerlines_px"].values():
        road(roads, [project(*p) for p in route], 8.0, access_level, "Infield asphalt",
             trim=lambda p: clip_pit_side(layout, p), pad_outline=outline)
    roads.bake("Michigan gray annotated service roads")

    structures = collection("Michigan infield buildings and garage pads")
    pads = builder(structures)
    boxes = shapes["grandstand_and_buildings"][1:]
    for i, zone in enumerate(boxes, 1):
        center, width, depth = source_box(project, zone["bbox"])
        pads.box(center, datum, width + 5, depth + 2.6, .017, 0, "Paddock concrete")
        building = builder(structures)
        building.box(center, datum + .017, width, depth, 4.443, 0, "Garage panel")
        # With the diagram's y-down image mapped to Blender's y-up plane,
        # the pit-facing facade is the south side of each infield building.
        glass_y = center[1] - depth * .5 - .03
        building.box((center[0], glass_y), datum + .94, width - 1.25, .12, 3.1, 0, "Lake blue glazing")
        for x in stations(-width * .48, width * .48, 4.4):
            building.box((center[0] + x, glass_y - .08), datum + .88, .09, .095, 3.2, 0, "Fixture steel")
        building.box(center, datum + 4.46, width + 1.2, depth + 1.55, .32, 0, "Light standing seam")
        for x in stations(-width * .43, width * .43, 17.5):
            building.box((center[0] + x, center[1]), datum + 4.78, 2.4, 1.85, .46, 0, "Fixture steel")
        building.bake(f"Michigan glass-front pit-facing building {i}")

    for i, zone in enumerate(shapes["garages"], 1):
        center, width, depth = source_box(project, zone["bbox"])
        pads.box(center, datum, width + 16, depth + 10, .06, 0, "Paddock concrete")
        garages = builder(structures)
        garages.box(center, datum + 5.86, width + 1.1, depth + 1.4, .39, 0, "Dark standing seam")
        doors = stations(-depth * .44, depth * .44, 8.5)
        for end in (-1, 1):
            garages.box((center[0], center[1] + end * (depth * .5 - .20)),
                         datum + .06, width, .40, 5.8, 0, "Garage panel")
        for side in (-1, 1):
            x = center[0] + side * (width * .5 - .21)
            garages.box((x, center[1]), datum + 4.32, .43, depth, 1.54, 0, "Garage panel")
            gaps = [-depth * .5] + [d for y in doors for d in (y - 2.45, y + 2.45)] + [depth * .5]
            for lo, hi in zip(gaps[::2], gaps[1::2]):
                garages.box((x, center[1] + (lo + hi) * .5), datum + .06,
                             .43, hi - lo, 4.26, 0, "Garage panel")
            for y in doors:
                x = center[0] + side * (width * .5 + .05)
                garages.box((x, center[1] + y), datum + 3.81, .10, 4.9, .47, 0, "Garage door")
                for height in (3.91, 4.04, 4.17):
                    garages.box((x + side * .055, center[1] + y), datum + height, .018, 4.82, .035, 0, "Fixture steel")
                garages.box((x, center[1] + y), datum + 4.71, .13, 2.65, .24, 0, "Smoked glazing")
        for y in doors[::2]:
            garages.box((center[0], center[1] + y), datum + 5.54, width * .99, .29, .31, 0, "Fixture steel")
        garages.bake(f"Michigan garage {i:02d}")
    pads.bake("Michigan concrete building and garage foundations")
    return outline


def build_parking(shapes, project, datum, outline, rng):
    target = collection("Michigan annotated parking")
    vehicles = builder(target)
    pads = builder(target)
    # Trucks face away from the garage along the infield's open west side.
    center, width, depth = source_box(project, shapes["hauler_parking"][0]["bbox"])
    per_row, row_count = 4, 10
    pitch = 23.1
    row_pitch = 7.6
    total_width = max(width, (per_row - 1) * pitch + 27)
    total_depth = max(depth, (row_count - 1) * row_pitch + 16)
    pads.box(center, datum, total_width, total_depth, .061, 0, "Infield asphalt")
    for row in range(row_count):
        for col in range(per_row):
            x = center[0] + (col - (per_row - 1) * .5) * pitch
            y = center[1] + (row - (row_count - 1) * .5) * row_pitch
            yaw = math.pi
            paint = CAR_PAINT[(row * per_row + col) % len(CAR_PAINT)]
            vehicles.box((x, y), datum + .43, 13.7, 2.54, 3.45, yaw, "Trailer white")
            vehicles.box(local((x, y), yaw, .45, 0), datum + 1.10, 11.8, 2.557, .79, yaw, paint)
            vehicles.box(local((x, y), yaw, 7.65, 0), datum + .56, 2.2, 2.33, 2.2, yaw, paint)
            vehicles.box(local((x, y), yaw, 8.74, 0), datum + 1.89, .07, 1.91, .64, yaw, "Smoked glazing")
            vehicles.box(local((x, y), yaw, 8.84, 0), datum + .53, .12, 2.25, .25, yaw, "Trailer hardware")
            for dx in (-4.8, -3.85, 6.2):
                for side in (-1, 1):
                    vehicles.box(local((x, y), yaw, dx, side * 1.18), datum + .03, .88, .30, .89, yaw, "Tires")
            marker("Michigan parking anchor mesh", target, f"Michigan hauler stall {row * per_row + col + 1:02d}",
                   (x, y, datum + .065), yaw, aim=(x - 2.0, y, datum + .065))
    haulers = vehicles.bake("Michigan hauler display forty parked team haulers")
    haulers["team_hauler_count"] = row_count * per_row
    parking = builder(target)
    ticks = builder(target)
    for x1, x2, y in ((263, 280, 231), (266, 280, 239), (316, 322, 213), (377, 402, 214),
                      (385, 409, 222), (378, 407, 236), (389, 422, 244)):
        start, end = project(x1, y), project(x2, y)
        for x in stations(start[0] + 1.4, end[0] - 1.4, 2.86):
            spot = (x, start[1])
            if fits(outline, spot, 0, 2.65, 5.5):
                park_car(parking, spot, 0., rng)
                ticks.box((x + 1.36, start[1]), datum + .051, .023, 5.2, .012, 0, "Pavement paint")
    car_object = parking.bake("Michigan infield access parking - occupied stalls")
    if car_object is not None:
        car_object.location.z = datum + .055
    ticks.bake("Michigan infield parking stall lines")

    for index, zone in enumerate(shapes["rv_parking"], 1):
        boundary_px = simplify_pixel_outline(zone["polygons"][0])
        boundary = [project(*point) for point in boundary_px]
        # Both RV areas retain an interior service lane between pairs of rows.
        x1, y1, x2, y2 = bounds(boundary)
        rv_model = builder(target)
        rv_pads = builder(target)
        count = 0
        y = y1 + 13
        row = 0
        while y < y2 - 13:
            x = x1 + 11 + (row % 2) * 2
            while x < x2 - 11:
                xy = (x, y)
                yaw = math.pi * .5 + rng.uniform(-.05, .05)
                if fits(boundary, xy, yaw, 17.5, 10.2):
                    # Build each vehicle on its own level campsite hardstanding.
                    rv_pads.box(xy, datum + .001, 13.0, 5.5, .053, yaw, "Camp gravel")
                    parking_vehicle(rv_model, xy, yaw, True, rng.choice(CAR_PAINT))
                    accessory = local(xy, yaw, -3.0, 2.7)
                    rv_model.box(accessory, .075, 1.85, .56, .49, yaw, "Garage panel")
                    rv_model.box(local(xy, yaw, 2.9, -2.8), .06, 2.2, 1.4, .70, yaw, "Trailer hardware")
                    if count % 3 == 0:
                        awning(rv_model, local(xy, yaw, -.9, -3.10), yaw, 2.6, 2.1, 0,
                               rng.choice(("Awning crimson", "Awning cyan", "Awning sand")))
                    count += 1
                x += 12.0
            y += 27.0 if row % 2 else 19.0
            row += 1
        rv_pads.bake(f"Michigan RV site {index} hardstanding")
        rv_object = rv_model.bake(f"Michigan RV site {index} parked campers")
        if rv_object is not None:
            rv_object.location.z = datum + .055
            rv_object["rv_count"] = count
    pads.bake("Michigan forty-team hauler lot pavement")


def make_track_frames(layout):
    lap = layout.LAP
    # Race direction goes counterclockwise. Determine the signed outside lane
    # in the native layout rather than assuming a sign for lateral offsets.
    centers = [layout.racing_surface(s, 0) for s in stations(0, lap, 5)[:-1]]
    sign = 1 if signed_area([tuple(c[:2]) for c in centers]) > 0 else -1
    sample_stations = stations(0, lap, 2.0)[:-1]
    sample_centers = [layout.racing_surface(s, 0) for s in sample_stations]

    def nearest(vec):
        index = min(range(len(sample_centers)), key=lambda i: math.dist(vec, sample_centers[i][:2]))
        return sample_stations[index]

    def edge(station):
        half = layout.track_width(station) * .5
        p = layout.racing_surface(station, 0)
        q = layout.racing_surface(station, half)
        a = layout.racing_surface(station - .5, 0)
        b = layout.racing_surface(station + .5, 0)
        tangent = Vector((b[0] - a[0], b[1] - a[1])).normalized()
        radial = Vector((tangent.y, -tangent.x)) * sign
        if radial.dot(Vector((q[0] - p[0], q[1] - p[1]))) < 0:
            q = layout.racing_surface(station, -half)
        return q, radial, math.atan2(tangent.y, tangent.x)

    def outer(station, offset):
        pos, outward, _ = edge(station)
        return (pos[0] + offset * outward.x, pos[1] + offset * outward.y)

    def frame(station, offset):
        _, _, yaw = edge(station)
        return outer(station, offset), yaw

    return edge, outer, frame, nearest


def build_frontage(layout, shapes, project, datum):
    target = collection("Michigan continuous grandstand and press box")
    anchors = collection("Michigan venue anchors")
    edge, outer, frame, nearest = make_track_frames(layout)
    lap = layout.LAP
    raw_start = nearest(project(234, 260))
    raw_end = nearest(project(471, 260))
    start = raw_start if (raw_end - raw_start) % lap < lap * .49 else raw_end
    end = start + min((raw_end - raw_start) % lap, (raw_start - raw_end) % lap)

    def anchor(target_collection, name, location, yaw=0, width=1, aim=None):
        return marker("Michigan venue anchor mesh", target_collection, name, location, yaw, width, aim)

    curved_grandstand(
        target, anchors, start=start, end=end, rows=26, sections=30,
        first_number=1, venue="Michigan", builder=builder, marker=anchor,
        outer=outer, frontage_frame=frame, ground=lambda _: datum,
        track_edge=lambda station: edge(station)[0][2], nearest_station=nearest,
        site_stations=lambda lo, hi, spacing: stations(lo, hi, max(spacing, 2.0)),
        seat_material="Michigan blue seating",
    )
    for item in target.objects:
        if item.name.startswith("Michigan grandstand section "):
            item.name = item.name.replace("Michigan grandstand section", "Grandstand seating Michigan section")
    apron = builder(target)
    # Interpolate the embedded edge height into the concourse against the
    # spectator foundation. The outer lane stays outside the race wall face.
    rows = []
    for station in stations(start, end, 1.5):
        outer_height = edge(station)[0][2]
        rows.append(((*outer(station, .29), outer_height + .025),
                     (*outer(station, 6.85), datum + .037)))
    apron.ribbon(rows, "Section concrete")
    apron.bake("Grandstand Michigan full frontage concrete")

    finish_station = getattr(layout, "FINISH", 0.0)
    press_center, press_width, annotated_depth = source_box(project, shapes["press_box"][0]["bbox"])
    finish_edge, outward, yaw = edge(finish_station)
    # Project the purple annotation onto the real finish stripe extension.
    offset = Vector(press_center) - Vector(finish_edge[:2])
    press_xy = outer(finish_station, offset.dot(outward))
    press_depth = min(23.0, annotated_depth)
    pz = max(datum + 23.9, finish_edge[2] + 23.1)
    # Face glazing toward the raceway with side windows and a cantilevered roof.
    press_box = builder(target)
    for dx in stations(-press_width * .46, press_width * .46, 19.0):
        for dy in (-press_depth * .37, press_depth * .37):
            center = local(press_xy, yaw, dx, dy)
            press_box.box(center, datum + .02, .72, .84, pz - datum, yaw, "Fixture steel")
    press_box.box(press_xy, pz, press_width, press_depth, 4.8, yaw, "Garage panel")
    positive_side = local((0, 0), yaw, 0, 1)
    face = -1 if outward.dot(Vector(positive_side)) > 0 else 1
    facade = face * (press_depth * .5 + .03)
    press_box.box(local(press_xy, yaw, 0, facade), pz + 1, press_width - 3, .14, 3.3, yaw, "Lake blue glazing")
    for x in stations(-press_width * .49, press_width * .49, 4):
        press_box.box(local(press_xy, yaw, x, facade + face * .07),
                      pz + .93, .10, .15, 3.39, yaw, "Walkway steel")
    press_box.box(press_xy, pz + 4.80, press_width + 1.7, press_depth + 1.5, .43, yaw, "Dark standing seam")
    press_box.bake("Press box Michigan")
    anchor(anchors, "Michigan Spotter camera", (*press_xy, pz + 7.2), yaw,
           aim=layout.racing_surface(finish_station + 60, 0))

    billboard = collection("Michigan track-facing billboards")
    boards = builder(billboard)
    remaining = lap - (end - start)
    phase = .12
    for count, station in enumerate(stations(end + 25, end + remaining - 25, 27), 1):
        # Placement and inward-facing normal come from the original oval, so
        # billboards remain outboard even in tighter end portions.
        xy, yaw = frame(station, 7.9)
        wall_z = edge(station)[0][2]
        name, sub, color = ADS[(count - 1) % len(ADS)]
        color_name = f"Michigan ad panel {name}"
        COLORS[color_name] = color
        az = math.atan2(
            layout.racing_surface(station + 1.5, 0)[1] - layout.racing_surface(station - 1.5, 0)[1],
            layout.racing_surface(station + 1.5, 0)[0] - layout.racing_surface(station - 1.5, 0)[0],
        )
        normal = Vector((math.sin(az), -math.cos(az)))
        track_xy = Vector(layout.racing_surface(station, 0)[:2])
        if normal.dot(track_xy - Vector(xy)) < 0:
            az += math.pi
            normal.negate()
        height = wall_z + 3.55 + phase * (count % 4)
        panel_width = 10.9 + (count % 4) * .65
        boards.box(xy, height, panel_width, .29, 3.75, yaw, color_name)
        for dx in (-4.4, 4.4):
            foot = local(xy, yaw, dx, 0)
            boards.box(foot, datum, .52, .48, height - datum + .2, yaw, "Fixture steel")
        face_xy = Vector(xy) + normal * .165
        lettering(billboard, f"Michigan ad {count:02d} {name}", name, (*face_xy, height + 2.34), az, 1.13, "Fixture lens")
        lettering(billboard, f"Michigan ad {count:02d} subline", sub, (*face_xy, height + 1.10), az, .53, "Fixture lens")
    boards.bake("Michigan outside-wall billboard structures")
    return edge, outer, frame, anchors


def awning(model, center, yaw, width, depth, level, cloth):
    for side in (-1, 1):
        for end in (-1, 1):
            support = local(center, yaw, side * width * .44, end * depth * .43)
            model.box(support, level, .063, .063, 2.24, yaw, "Fixture steel")
    model.box(center, level + 2.22, width, depth, .105, yaw, cloth)


def person(model, point, yaw, level, rng):
    shirt = rng.choice(CAR_PAINT)
    skin = rng.choice(("Warm skin", "Tan skin", "Dark skin"))
    for side in (-1, 1):
        model.box(local(point, yaw, 0, side * .092), level, .20, .13, .82, yaw, "Denim trousers")
        model.box(local(point, yaw, .035, side * .21), level + .8, .145, .13, .46, yaw, shirt)
    model.box(point, level + .79, .26, .33, .58, yaw, shirt)
    model.box(point, level + 1.37, .195, .20, .25, yaw, skin)


def light_fixture(model, xy, yaw, datum, height, heads):
    model.box(xy, datum, .84, .84, .42, yaw, "Paddock concrete")
    model.box(xy, datum + .42, .22, .22, height - .42, yaw, "Fixture steel")
    model.box(xy, datum + height - .13, max(.8, heads * .46), .20, .20, yaw, "Fixture steel")
    for i in range(heads):
        head = local(xy, yaw, (i - (heads - 1) * .5) * .46, .18)
        model.box(head, datum + height - .24, .36, .31, .22, yaw, "Fixture lens")


def build_fixtures(layout, shapes, datum, rng, edge, outer, frame, anchors):
    target = collection("Michigan pit promenade and venue fixtures")
    pits = builder(target)
    crowd = builder(target)
    pit_yaw = math.atan2(*reversed(layout.PIT.heading(0)))
    for index in range(layout.STALL_COUNT):
        pose = layout.pit_stall_pose(index)
        box = pose.offset(layout.PIT_WORK_INNER + .15, datum)[:2]
        light_fixture(pits, box, pit_yaw, datum + .05, 3.9, 1)
        lamp = (*box, datum + 3.85)
        aim = pose.offset(-layout.PIT_WIDTH * .32, datum)
        marker("Michigan venue anchor mesh", anchors, f"Michigan pit light {index + 1:02d}", lamp,
               pit_yaw, aim=aim)
        if index % 3 == 1:
            # Short fixture rows stand behind the protected walkway.
            rear = pose.offset(layout.PIT_WALK_INNER - 1.0, datum)[:2]
            pits.box(rear, datum, .55, .55, .99, pit_yaw, "Rubbish bin")
    # The pit promenade carries a single timing pylon on the finish-line
    # extension. Keep the footprint behind the 10-foot walkway.
    finish = layout.race_pose(getattr(layout, "FINISH", 0.0))
    pit_start = layout.pit_pose(0.0)
    x, y, _ = pit_start.offset(layout.PIT_WALK_INNER - 3.2)
    station = ((finish.x - x) * finish.tx + (finish.y - y) * finish.ty) / (
        pit_start.tx * finish.tx + pit_start.ty * finish.ty)
    middle = layout.pit_pose(station)
    pylon = middle.offset(layout.PIT_WALK_INNER - 3.2, datum)[:2]
    pits.box(pylon, datum, 4.6, 3.8, .23, pit_yaw, "Paddock concrete")
    marker("Michigan venue anchor mesh", anchors, "Michigan scoring pylon anchor",
           (*pylon, datum + .23), pit_yaw)
    buildings = [source_box(layout.diagram_xy, shape["bbox"]) for shape in shapes["grandstand_and_buildings"][1:]]

    def clear_of_buildings(point, clearance):
        return all(abs(point[0] - center[0]) > width * .5 + clearance or
                   abs(point[1] - center[1]) > depth * .5 + clearance
                   for center, width, depth in buildings)

    for index, portion in enumerate((.04, .105, .205, .475, .525, .82, .90, .96)):
        pose = layout.pit_pose(layout.PIT_DRAWING_LENGTH * portion)
        # Place people beyond the public walkway. The meshes leave the
        # complete walkway and the central garages' gate area clear.
        for j in range(4):
            spot = pose.offset(layout.PIT_WALK_INNER - 1.4 - rng.random() * 1.7, datum)[:2]
            spot = local(spot, pit_yaw, (j - 1.5) * 1.05, 0)
            if clear_of_buildings(spot, .4):
                pits.box(spot, datum, .82, .81, .02, pit_yaw, "Paddock concrete")
                person(crowd, spot, pit_yaw - math.pi * .5 + rng.random() * 1.5, datum + .02, rng)
        if index in (0, 3, 6):
            kiosk = pose.offset(layout.PIT_WALK_INNER - 4.9, datum)[:2]
            if clear_of_buildings(kiosk, 3.3):
                pits.box(kiosk, datum, 4.3, 4.7, .023, pit_yaw, "Paddock concrete")
                pits.box(kiosk, datum + .023, 3.8, 2.1, 2.6, pit_yaw, "Garage panel")
                awning(pits, local(kiosk, pit_yaw, 0, -2.0), pit_yaw,
                       4.1, 2.5, datum, ("Awning cyan", "Awning crimson", "Awning sand")[index // 3])
    for portion in (.07, .93):
        pose = layout.pit_pose(layout.PIT_DRAWING_LENGTH * portion)
        toilet = pose.offset(layout.PIT_WALK_INNER - 4, datum)[:2]
        for i in range(2):
            toilet = local(toilet, pit_yaw, 1.6, 0)
            pits.box(toilet, datum, 1.08, 1.12, 2.33, pit_yaw, "Portable toilet")
            pits.box(toilet, datum + 2.33, 1.13, 1.16, .08, pit_yaw, "Light standing seam")
    pits.bake("Michigan pit promenade fixtures and scoring pylon")
    crowd.bake("Michigan protected pit promenade spectators")

    lamps = collection("Michigan exterior and infield fixture positions")
    exterior = builder(lamps)
    for index, station in enumerate(stations(0, layout.LAP, 115)[:-1], 1):
        pos, yaw = frame(station, 2.25)
        ground = max(datum, edge(station)[0][2] - .1)
        light_fixture(exterior, pos, yaw, ground, 12.8, 4)
        marker("Michigan venue anchor mesh", anchors, f"Michigan track light {index:02d}",
               (*pos, ground + 12.7), yaw, aim=layout.racing_surface(station, 0))
    exterior.bake("Michigan exterior track light assemblies")
    street = builder(lamps)
    for index, (x, y) in enumerate(((268, 165), (263, 180), (270, 192), (301, 191),
                                      (333, 193), (359, 179), (385, 180), (407, 191),
                                      (429, 208), (453, 249)), 1):
        xy = layout.diagram_xy(x, y)
        light_fixture(street, xy, 0, datum + .05, 5.45, 1)
        marker("Michigan venue anchor mesh", anchors, f"Michigan street light {index:02d}",
               (*xy, datum + 5.46), aim=(*local(xy, 0, 2, 3), datum))
    street.bake("Michigan infield streetlights")


def build_scenery(layout):
    """Build all annotated Michigan scenery after the layout preflight passes."""
    shapes = json.loads((HERE / "reference" / "MichiganTrackLayout.features.json").read_text(encoding="utf-8"))
    datum = float(getattr(layout, "GROUND", 0.0))
    rng = random.Random(11022)
    project = layout.diagram_xy
    outline = build_infield(layout, shapes, project, datum)
    build_parking(shapes, project, datum, outline, rng)
    build_fixtures(layout, shapes, datum, rng, *build_frontage(layout, shapes, project, datum))
