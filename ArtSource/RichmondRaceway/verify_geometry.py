"""Measure final sampled route, surface profiles, and pit footprint from source paths."""
import json
import math
from pathlib import Path

import richmond_layout as p


def angle(a, b):
    return math.degrees(math.atan2(a[0] * b[1] - a[1] * b[0], a[0] * b[0] + a[1] * b[1]))


def yaw_sweep(path, width, closed=False):
    distances = (i for i in range(math.ceil(width / 2), math.floor(path.length-width / 2)))
    values = [angle(path.heading(s-width / 2, closed), path.heading(s+width / 2, closed)) for s in distances]
    return [round(min(values), 4), round(max(values), 4)]


def profile(side):
    count = math.ceil(p.LAP * 2.)
    step = p.LAP / count
    def xyzh(s):
        pose = p.race_pose(s)
        lane = (-pose.width/2-pose.apron) if side < -1 else pose.width/2 * side
        return p.racing_surface(s, lane)
    points = [xyzh(i * step) for i in range(count)]
    heights = [v[2] for v in points]
    grades = []
    for i, a in enumerate(points):
        b = points[(i+1)%count]
        grades.append(100 * (b[2] - a[2]) / math.dist(a[:2], b[:2]))
    return {
        "height_range_m": [min(heights), max(heights)],
        "grade_range_percent": [min(grades), max(grades)],
        "max_half_meter_grade_change_percentage_points": max(abs(grade-grades[i-1]) for i, grade in enumerate(grades)),
        "seam_grade_change_percentage_points": grades[0] - grades[-1],
    }


def clearance(route):
    approximate = p.Path([p.TRACK.at(p.LAP * i / 1040) for i in range(1041)])
    outside_wall = 0
    overlap = []
    min_gap = math.inf
    shoulder_gap = 0.
    for i in range(math.ceil(route.length*2)+1):
        s = min(i*.5, route.length)
        center = route.at(s)
        tx, ty = route.heading(s)
        for fraction in (-.5, -.25, 0., .25, .5):
            point = p.add(center, (ty * p.PIT_WIDTH * fraction, -tx * p.PIT_WIDTH * fraction))
            station = approximate.nearest(point) * p.LAP / approximate.length
            pose = p.race_pose(station)
            dx, dy = p.sub(point, (pose.x,pose.y))
            across = dx*pose.nx + dy*pose.ny
            projected_half_width = pose.width/2 * math.cos(math.radians(pose.bank))
            if across > projected_half_width:
                outside_wall += 1
            gap = -projected_half_width - across
            min_gap = min(gap, min_gap)
            if gap < 0.:
                overlap.append(s)
            if fraction == .5:
                shoulder_gap = max(shoulder_gap, gap - p.paved_apron_width(station))
    return {"samples_beyond_outer_wall": outside_wall,
            "min_inside_racing_surface_clearance_m": min_gap,
            "largest_apron_to_pit_grass_gap_m": max(0., shoulder_gap),
            "racing_surface_overlap_m": [round(min(overlap), 2), round(max(overlap), 2)]
            if overlap else None}


def measurement_line_length():
    # Sample the operator's offset measured horizontally from the outside wall.
    samples = []
    for i in range(3500):
        s = p.TRACK.length*i/3500
        pose = p.race_pose(s)
        side = pose.width/2 - 13.5*.3048/math.cos(math.radians(pose.bank))
        samples.append(p.racing_surface(s, side)[:2])
    return sum(math.dist(samples[i], samples[i-1]) for i in range(len(samples)))


def line_deviation(start, end, path):
    a, b = path.at(start), path.at(end)
    tangent = p.unit(p.sub(b, a))
    return max(abs(p.sub(path.at(p.lerp(start, end, i/300)), a)[0]*tangent[1] -
                   p.sub(path.at(p.lerp(start, end, i/300)), a)[1]*tangent[0])
               for i in range(301))


audit = {
    "lap_m": p.TRACK.length,
    "published_offset_measurement_line_m": measurement_line_length(),
    "published_line_note": "Unity normalizes the centerline to nominal 0.75 mi; official distance is measured 13.5 ft in from the wall.",
    "backstretch_straight_line_deviation_m": line_deviation(p.marker(163,28), p.marker(95,46), p.TRACK),
    "pit_straight_line_deviation_m": line_deviation(p.PIT.nearest(p.diagram_xy(118,126)),
                                                     p.PIT.nearest(p.diagram_xy(202,103)), p.PIT),
    "pit_segments_m": [p.ENTRY.length, p.PIT.length, p.EXIT.length],
    "width_range_m": [min(p._control_widths),max(p._control_widths)],
    "apron_width_range_m": [min(p._control_paved_aprons),max(p._control_paved_aprons)],
    "turn_heading_delta_on_1_5_15m_windows_deg": [yaw_sweep(p.TRACK,w,True) for w in (1,5,15)],
    "pit_heading_delta_deg": {key: [yaw_sweep(path,w) for w in (1,5,15)]
                              for key,path in (("entry",p.ENTRY),("road",p.PIT),("exit",p.EXIT))},
    "pit_endpoint_heading_error_deg": {
        "apron_to_entry": angle(p.entry_tangent,p.ENTRY.heading(0)),
        "entry_to_road": angle(p.ENTRY.heading(p.ENTRY.length),p.PIT.heading(0)),
        "road_to_exit": angle(p.PIT.heading(p.PIT.length),p.EXIT.heading(0)),
        "exit_to_apron": angle(p.EXIT.heading(p.EXIT.length),p.exit_tangent),
    },
    "pit_footprint": {key: clearance(path)
                      for key,path in (("entry",p.ENTRY),("road",p.PIT),("exit",p.EXIT))},
    "profile_checks": {str(side): profile(side) for side in (-2,-1,-.5,0,.5,1)},
    "restart_stations_m": [p.LAP*p.RESTART_START,p.LAP*p.RESTART_END],
    "wall_backed_stalls": p.STALL_COUNT,
    "pit_usable_footprint_m": p.PIT_FOOTPRINT,
    "pit_walkway_width_m": p.PIT_WORK_INNER-p.PIT_WALK_INNER,
    "stall_min_longitudinal_margin_behind_wall_m": min(
        p.STALL_START - p.PIT_WALL_START, p.PIT_WALL_END - p.STALL_END),
    "pit_speed_limit_mph": p.game_definition()["pitRoad"]["speedLimitMph"],
    "cup_reference_mph": p.BENCHMARK_MPH,
    "cup_reference_lap_seconds": .75*3600/p.BENCHMARK_MPH,
    "stall_spacing_m": p.STALL_SPACING,
}

Path(__file__).with_name("geometry-verification.json").write_text(json.dumps(audit,indent=2)+"\n", encoding="utf-8")
print(json.dumps(audit, indent=2))

assert abs(audit["lap_m"] - 1207.008) < .001
assert all(entry["samples_beyond_outer_wall"] == 0 for entry in audit["pit_footprint"].values())
assert all(entry["racing_surface_overlap_m"] is None for entry in audit["pit_footprint"].values())
assert max(audit["pit_footprint"][merge]["largest_apron_to_pit_grass_gap_m"] for merge in ("entry", "exit")) < .02
assert audit["pit_footprint"]["road"]["largest_apron_to_pit_grass_gap_m"] > 25.
assert audit["backstretch_straight_line_deviation_m"] < .003, audit["backstretch_straight_line_deviation_m"]
assert audit["pit_straight_line_deviation_m"] < .003, audit["pit_straight_line_deviation_m"]
assert p.RESTART_START < p.RESTART_END < 1.
assert audit["stall_min_longitudinal_margin_behind_wall_m"] >= 2.95
assert max(abs(angle) for angle in audit["pit_endpoint_heading_error_deg"].values()) < 2.0
assert all(abs(profile["seam_grade_change_percentage_points"]) < .1 for profile in audit["profile_checks"].values())
