"""Sweep a two-deck grandstand along a venue's continuous annotated run."""
import math
from mathutils import Vector
from venue_geometry import local


def curved_grandstand(target, anchors, *, start, end, rows, sections, first_number, venue,
                      builder, marker, outer, frontage_frame, ground, track_edge, nearest_station,
                      site_stations, seat_material):
    pitch, setback, aisle_width = 1.07, 6.8, 1.25

    def lerp(a, b, fraction):
        return a + (b - a) * fraction

    def first_height(station):
        edge = track_edge(station)
        return max(ground(outer(station, setback)) + 1.62, edge + 1.66)

    def plan(station, depth):
        return outer(station, setback + depth)

    def aisle_limits(center, depth):
        speed = math.dist(plan(center - .25, depth), plan(center + .25, depth)) / .5
        half = aisle_width * .5 / speed
        return center - half, center + half

    def solid_strip(model, lo, hi, front, back, top, thickness, material_name):
        stations = site_stations(lo, hi, .9)
        tread = [((*plan(s, front), top(s)), (*plan(s, back), top(s))) for s in stations]
        model.ribbon(tread, material_name)
        for a, b in zip(tread, tread[1:]):
            for i in (0, 1):
                p, q = a[i], b[i]
                low_p = (*p[:2], p[2] - thickness)
                low_q = (*q[:2], q[2] - thickness)
                face = (p, q, low_q, low_p)
                model.quad(face if i == 0 else face[::-1], material_name)
        for cut in (0, -1):
            p, q = tread[cut]
            face = (p, q, (*q[:2], q[2] - thickness), (*p[:2], p[2] - thickness))
            model.quad(face if cut == -1 else face[::-1], material_name)
        for a, b in zip(tread, tread[1:]):
            points = (a[0], b[0], b[1], a[1])
            low = [(*point[:2], point[2] - thickness) for point in points]
            normal = (Vector(low[1]) - Vector(low[0])).cross(Vector(low[2]) - Vector(low[0]))
            model.quad(low if normal.z < 0 else low[::-1], material_name)

    def aisle_step(model, center, depth, run, top):
        lo, hi = aisle_limits(center, depth)
        solid_strip(model, lo, hi, depth, depth + run, top, .20, "Section concrete")

    def upper_access_dimensions(station):
        rear = 6.7 + (rows - 6) * pitch
        center, yaw = frontage_frame(station, setback + rear + 2.35)
        deck = first_height(station) + 7.8 + (rows - 6) * .68
        floor = ground(local(center, yaw, -4.0, 2.35)) + .06
        count = math.ceil((deck - floor) / (.185 * 4))
        return center, yaw, deck, floor, count

    def upper_access(model, station):
        center, yaw, deck, floor, count = upper_access_dimensions(station)
        rise = (deck - floor) / (count * 4)
        run = .28
        length = count * run
        for flight in range(4):
            sign = 1 if flight % 2 == 0 else -1
            y = 2.35 + (flight % 2) * 1.8
            for index in range(count):
                x = sign * ((index + .5) * run - length * .5)
                top = floor + (flight * count + index + 1) * rise
                model.box(local(center, yaw, x, y), top - .25, run, 1.4, .25, yaw, "Section concrete")
                if index % 4 == 0:
                    for edge in (-.68, .68):
                        model.box(local(center, yaw, x, y + edge), top, .06, .06, .95, yaw, "Bench aluminium")
            lower_x, upper_x = -sign * length * .5, sign * length * .5
            for edge in (-.68, .68):
                model.brace((*local(center, yaw, lower_x, y + edge), floor + flight * count * rise + .93),
                            (*local(center, yaw, upper_x, y + edge), floor + (flight + 1) * count * rise + .93),
                            .055, "Bench aluminium")
            model.box(local(center, yaw, upper_x + sign * .78, 3.25),
                      floor + (flight + 1) * count * rise - .22, 1.56, 3.20, .22, yaw, "Section concrete")
        model.box(local(center, yaw, -length * .5 - .78, 1.85),
                  deck - .23, 1.56, 4.3, .23, yaw, "Section concrete")
        for x in (-length * .5 - 1.46, length * .5 + 1.46):
            for y in (1.72, 4.8):
                xy = local(center, yaw, x, y)
                base = ground(xy)
                model.box(xy, base, .37, .37, deck - base, yaw, "Fixture steel")

    step = (end - start) / sections
    for index in range(sections):
        lo, hi = start + index * step, start + (index + 1) * step
        center = (lo + hi) * .5
        number = first_number + index
        model = builder(target)
        access = lo + 4.2 if index in (0, sections - 1) else None
        solid_strip(model, lo, hi, -.6, rows * pitch + 5.6,
                    lambda s: ground(plan(s, 0.)) + .04, .20, "Paddock concrete")
        for deck, count, origin, lift, rise in ((0, rows, 0., 0., .55), (1, rows - 5, 6.7, 7.8, .68)):
            for row in range(count):
                depth = origin + row * pitch
                top = lambda s: first_height(s) + lift + row * rise
                aisle_lo, aisle_hi = aisle_limits(center, depth)
                for side, left, right in ((0, lo, aisle_lo), (2, aisle_hi, hi)):
                    # Full-width structural treads meet at the section boundaries.
                    solid_strip(model, left, right, depth - pitch, depth, top, .44, "Section concrete")
                    solid_strip(model, left, right, depth - .60, depth - .25,
                                lambda s: top(s) + .24, .24, "Bench aluminium")
                    solid_strip(model, left, right, depth - .29, depth - .20,
                                lambda s: top(s) + .45, .26, seat_material)
                    mid = (left + right) * .5
                    a, b = plan(left, depth - .42), plan(right, depth - .42)
                    yaw = math.atan2(b[1] - a[1], b[0] - a[0])
                    marker(anchors, f"{venue} grandstand bench {number:03d}-{deck * rows + row:02d}-{side}",
                           (*plan(mid, depth - .42), top(mid) + .245), yaw, width=math.dist(a, b))
                steps = math.ceil(rise / .19)
                for i in range(steps):
                    aisle_step(model, center, depth - pitch + i * pitch / steps, pitch / steps,
                               lambda s: top(s) - rise + rise * (i + 1) / steps)
            rear = origin + (count - 1) * pitch
            rear_top = lambda s: first_height(s) + lift + (count - 1) * rise
            if deck:
                solid_strip(model, lo, hi, rear, rear + 2.6, rear_top, .40, "Section concrete")
            rail_depth = rear + (2.55 if deck else .11)
            rails_to_build = [(lo, hi)]
            if deck and access is not None:
                frame, yaw, _, _, stair_count = upper_access_dimensions(access)
                landing = -.14 * stair_count - .78
                a, b = sorted(nearest_station(local(frame, yaw, landing + side * .9, 0.))
                              for side in (-1, 1))
                rails_to_build = [(lo, min(hi, a)), (max(lo, b), hi)]
            for rail_lo, rail_hi in rails_to_build:
                if rail_hi > rail_lo:
                    for rail in (.55, 1.06):
                        solid_strip(model, rail_lo, rail_hi, rail_depth - .03, rail_depth + .03,
                                    lambda s: rear_top(s) + rail, .06, "Bench aluminium")
            for station in (lo + step * .03, center - step * .25, hi - step * .03):
                xy, yaw = frontage_frame(station, setback + rear - .48)
                base = ground(xy)
                model.box(xy, base, .55, .61, max(.25, rear_top(station) - base), yaw, "Fixture steel")
                front = plan(station, origin + 1.4)
                model.brace((*front, first_height(station) + lift - .38),
                            (*xy, rear_top(station) - .50), .43, "Fixture steel")
                model.brace((*front, ground(front)), (*xy, rear_top(station) - .50), .24, "Fixture steel")
                post, yaw = frontage_frame(station, setback + rail_depth)
                model.box(post, rear_top(station), .075, .075, 1.10, yaw, "Bench aluminium")
        # Extend the lower deck stair to the public walkway at the front.
        for i in range(8):
            aisle_step(model, center, -pitch - .34 * (8 - i), .34,
                       lambda s: lerp(ground(plan(s, -3.8)) + .06,
                                             first_height(s) - .55, (i + 1) / 8))
        if access is not None:
            upper_access(model, access)
        model.bake(f"{venue} grandstand section {number:03d}")
