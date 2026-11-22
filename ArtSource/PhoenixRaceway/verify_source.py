"""Measure final sampled route, surface profiles, and pit footprint from source paths."""
import json
import math
from pathlib import Path

import phoenix_layout as p


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
    approximate = p.Path([p.TRACK.at(p.LAP * i / 1024) for i in range(1025)])
    outside_wall = 0
    overlap = []
    for i in range(math.ceil(route.length)+1):
        s = min(i, route.length)
        center = route.at(s)
        tx, ty = route.heading(s)
        for lateral in (-p.PIT_WIDTH/2, 0., p.PIT_WIDTH/2):
            point = p.add(center, (ty * lateral, -tx * lateral))
            station = approximate.nearest(point) * p.LAP/approximate.length
            pose = p.race_pose(station)
            dx, dy = p.sub(point, (pose.x,pose.y))
            across = dx*pose.nx + dy*pose.ny
            projected_half_width = pose.width/2 * math.cos(math.radians(pose.bank))
            if across > projected_half_width:
                outside_wall += 1
            if across > -projected_half_width:
                overlap.append(s)
    return {"samples_beyond_outer_wall": outside_wall,
            "racing_surface_overlap_m": [round(min(overlap), 2), round(max(overlap), 2)]
            if overlap else None}


audit = {
    "lap_m": p.TRACK.length,
    "pit_segments_m": [p.ENTRY.length, p.PIT.length, p.EXIT.length],
    "width_range_m": [min(p._control_widths),max(p._control_widths)],
    "apron_width_range_m": [min(p._control_aprons),max(p._control_aprons)],
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
    "stall_count": p.STALL_COUNT,
    "stall_spacing_m": p.STALL_SPACING,
}

assert abs(audit["lap_m"] - 1609.344) < .001
assert all(entry["samples_beyond_outer_wall"] == 0 for entry in audit["pit_footprint"].values())
assert all(entry["racing_surface_overlap_m"] is None for entry in audit["pit_footprint"].values())
assert max(abs(angle) for angle in audit["pit_endpoint_heading_error_deg"].values()) < 2.0
assert all(abs(profile["seam_grade_change_percentage_points"]) < .1 for profile in audit["profile_checks"].values())
Path(__file__).with_name("geometry-verification.json").write_text(json.dumps(audit,indent=2)+"\n",encoding="utf-8")
print(json.dumps(audit, indent=2))
