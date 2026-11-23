"""Michigan layout traced from the supplied Irish Hills diagram, in meters."""

from bisect import bisect_right
from dataclasses import dataclass
import json
import math
from pathlib import Path as FilePath


ROOT = FilePath(__file__).resolve().parent
LAP = 3218.688
WIDTH = 22.2504
APRON = 5.4864
PIT_WIDTH = 8.0
STALL_COUNT = 40
BENCHMARK_SECONDS = 34.857
BENCHMARK_MPH = 7200 / BENCHMARK_SECONDS
WALL_HEIGHT = 1.067
RESTART_START = .965
RESTART_END = 1 - 30 / LAP


def mix(a, b, t):
    return a + (b - a) * t


def blend(t):
    t = min(1., max(0., t))
    return t**3 * (10 + t * (-15 + 6 * t))


def unit(x, y):
    length = math.hypot(x, y)
    return x / length, y / length


class Path:
    def __init__(self, points):
        self.points = points
        self.stations = [0.]
        for a, b in zip(points, points[1:]):
            self.stations.append(self.stations[-1] + math.dist(a, b))
        self.length = self.stations[-1]

    def segment(self, station):
        station = min(self.length, max(0., station))
        index = min(len(self.points) - 2, bisect_right(self.stations, station) - 1)
        first, last = self.stations[index:index + 2]
        return index, (station - first) / (last - first)

    def at(self, station):
        i, t = self.segment(station)
        return tuple(mix(a, b, t) for a, b in zip(self.points[i], self.points[i + 1]))

    def heading(self, station, loop=False):
        # Match TrackLayout's interpolation of neighboring sampled tangents.
        i, t = self.segment(station % self.length if loop else station)
        n = len(self.points) - 1
        def tangent(index):
            previous = self.points[(index - 1) % n if loop else max(0, index - 1)]
            following = self.points[(index + 1) % n if loop else min(n, index + 1)]
            return unit(following[0] - previous[0], following[1] - previous[1])
        a, b = tangent(i), tangent(i + 1)
        return unit(mix(a[0], b[0], t), mix(a[1], b[1], t))

    def nearest(self, point):
        best = (math.inf, 0.)
        for i, (a, b) in enumerate(zip(self.points, self.points[1:])):
            dx, dy = b[0] - a[0], b[1] - a[1]
            px, py = point[0] - a[0], point[1] - a[1]
            t = min(1., max(0., (px * dx + py * dy) / (dx * dx + dy * dy)))
            error = (px - t * dx)**2 + (py - t * dy)**2
            if error < best[0]:
                best = error, mix(self.stations[i], self.stations[i + 1], t)
        return best[1]


def catmull(points, closed, subdivisions):
    n = len(points)
    def control(i):
        return points[i % n] if closed else points[min(n - 1, max(0, i))]
    dense = []
    for i in range(n if closed else n - 1):
        a, b, c, d = (control(j) for j in range(i - 1, i + 3))
        for j in range(subdivisions):
            t = j / subdivisions
            dense.append(tuple(
                .5 * (2*b[k] + (c[k] - a[k])*t + (2*a[k] - 5*b[k] + 4*c[k] - d[k])*t*t
                      + (-a[k] + 3*b[k] - 3*c[k] + d[k])*t*t*t)
                for k in range(2)))
    return Path(dense + [points[0] if closed else points[-1]])


# Averaged red-band outline on the right half of the diagram. The pit divider
# is excluded. Constrain the backstretch to a straight and fit one continuous
# curvature profile so raster noise cannot add lateral bends at the turn joins.
TRACE = (
    (350, 134.5, 278), (359, 134.5, 277.5), (369, 134.5, 277),
    (379, 134.5, 276.5), (389, 134.5, 274), (399, 135, 273.5),
    (409, 135, 271.5), (419, 135, 269), (429, 135.5, 265),
    (439, 136.5, 263), (449, 139, 259.5), (459, 142, 255.5),
    (469, 146, 250), (479, 153, 242.5), (489, 164.5, 231),
)


def half_plan(front, angle, entry, exit, subdivisions=450):
    turn = 1.
    k_front = angle / front
    k_turn = (math.pi - angle - .5 * k_front * entry) / (turn - .5 * (entry + exit))
    def curvature(s):
        if s < front:
            return k_front
        if s < front + entry:
            return mix(k_front, k_turn, blend((s - front) / entry))
        if s > front + turn - exit:
            return k_turn * blend((front + turn - s) / exit)
        return k_turn

    ds = (front + turn) / subdivisions
    x = y = theta = 0.
    points = [(x, y)]
    for i in range(subdivisions):
        heading_step = curvature((i + .5) * ds) * ds
        x += ds * math.cos(theta + heading_step * .5)
        y += ds * math.sin(theta + heading_step * .5)
        theta += heading_step
        points.append((x, y))
    return points + [(0., y)]


def _fit():
    points = [(x - 349., 278. - y) for x, north, south in TRACE for y in (north, south)]
    points += [(153., 79.5)]
    def score(parameters):
        front, angle, entry, exit = parameters
        if not (.10 < front < .95 and .08 < angle < .85 and .05 < entry < .45
                and .07 < exit < .48 and entry + exit < .8):
            return math.inf
        half = half_plan(*parameters, subdivisions=240)
        if half[-2][0] <= 0:
            return math.inf
        pixel_scale = 151.5 / max(p[0] for p in half)
        outline = Path([(x * pixel_scale, y * pixel_scale) for x, y in half])
        distance = sum(math.dist(outline.at(outline.nearest(point)), point)**2 for point in points)
        # Weakly prefer the documented stretch lengths because the PDN is schematic.
        lap = (front + 1 + half[-2][0]) * 2
        return distance + 70 * ((2 * front / lap - .341)**2 + (2 * half[-2][0] / lap - .212)**2)

    parameters = [.52, .35, .24, .24]
    steps = [.13, .12, .10, .10]
    loss = score(parameters)
    for _ in range(44):
        improved = False
        for i, step in enumerate(steps):
            for direction in (-1, 1):
                candidate = parameters.copy()
                candidate[i] += step * direction
                value = score(candidate)
                if value < loss:
                    parameters, loss = candidate, value
                    improved = True
        if not improved:
            steps = [value * .62 for value in steps]
    return parameters


_fit_parameters = _fit()
_half = half_plan(*_fit_parameters, subdivisions=2400)
_raw = Path(_half + [(-x, y) for x, y in reversed(_half[:-1])])
_scale = LAP / _raw.length
_depth = _half[-1][1]
METERS_PER_PIXEL = max(x for x, _ in _half) * _scale / 151.5


def diagram_xy(x, y):
    return (x - 349.) * METERS_PER_PIXEL, (278. - y) * METERS_PER_PIXEL - _depth * _scale / 2


def _control(station):
    x, y = _raw.at(station / _scale)
    return x * _scale, (y - _depth / 2) * _scale


CONTROLS = [_control(i * LAP / 256) for i in range(256)]
TRACK = catmull(CONTROLS, True, 24)
TRACK = Path([(x * LAP / TRACK.length, y * LAP / TRACK.length) for x, y in TRACK.points])
CONTROLS = TRACK.points[:-1:24]

BANK_TRANSITIONS = [
    (.120, .187, 12., 18.), (.304, .395, 18., 5.),
    (.605, .696, 5., 18.), (.813, .880, 18., 12.),
]


def bank_at(station):
    fraction = station % LAP / LAP
    for start, end, before, after in BANK_TRANSITIONS:
        if fraction < start:
            return before
        if fraction <= end:
            return mix(before, after, blend((fraction - start) / (end - start)))
    return BANK_TRANSITIONS[-1][3]


@dataclass(frozen=True)
class Pose:
    x: float
    y: float
    tx: float
    ty: float
    bank: float = 0.
    width: float = WIDTH
    apron: float = APRON

    def offset(self, side=0., height=0.):
        return self.x + self.ty * side, self.y - self.tx * side, height


def race_pose(station):
    station %= LAP
    return Pose(*TRACK.at(station), *TRACK.heading(station, True), bank_at(station))


def track_width(station):
    return WIDTH


def racing_surface(station, lane):
    pose = race_pose(station)
    banking = math.radians(pose.bank)
    edge = max(-WIDTH / 2, min(WIDTH / 2, lane))
    return pose.offset(edge * math.cos(banking) + lane - edge, (edge + WIDTH / 2) * math.sin(banking))


def _straight(a, b, count):
    return [tuple(mix(lo, hi, i / count) for lo, hi in zip(a, b)) for i in range(count + 1)]


def _connector(a, start_tangent, b, end_tangent):
    handle = math.dist(a, b) * .18
    control = [
        a, tuple(a[j] + handle * start_tangent[j] for j in range(2)),
        tuple(a[j] + 2 * handle * start_tangent[j] for j in range(2)),
        tuple(b[j] - 2 * handle * end_tangent[j] for j in range(2)),
        tuple(b[j] - handle * end_tangent[j] for j in range(2)), b,
    ]
    def at(t):
        return tuple(sum(math.comb(5, i) * (1-t)**(5-i) * t**i * p[j]
                         for i, p in enumerate(control)) for j in range(2))
    plan = Path([at(i / 400) for i in range(401)])
    controls = [plan.at(plan.length * i / 64) for i in range(65)]
    return controls, catmull(controls, False, 16)


# Preserve the drawn pit baseline for the stalls and pavement markings. Start
# the exit connector earlier so it can curve away before the banked track edge.
PIT_START = diagram_xy(264, 259.8)
PIT_DRAWING_END = diagram_xy(438, 260.6)
PIT_DRAWING_LENGTH = math.dist(PIT_START, PIT_DRAWING_END)
EXIT_ADVANCE = 35.
PIT_END = tuple(mix(a, b, 1. - EXIT_ADVANCE / PIT_DRAWING_LENGTH)
                for a, b in zip(PIT_START, PIT_DRAWING_END))
_pit_controls = _straight(PIT_START, PIT_END, 40)
PIT = catmull(_pit_controls, False, 16)
PIT_LENGTH = PIT.length
ENTRY_STATION = TRACK.nearest(diagram_xy(218, 240))
EXIT_STATION = TRACK.nearest(diagram_xy(481, 239))
PIT_SPEED_START = .055 * PIT_DRAWING_LENGTH / PIT_LENGTH
PIT_SPEED_END = .945 * PIT_DRAWING_LENGTH / PIT_LENGTH
STALL_START = .08 * PIT_DRAWING_LENGTH
STALL_END = .92 * PIT_DRAWING_LENGTH
STALL_SPACING = (STALL_END - STALL_START) / STALL_COUNT
PIT_WALL_START = STALL_START - 4.
PIT_WALL_END = STALL_END + 4.
PIT_DIVIDER_EDGE = PIT_WIDTH / 2 + .45
PIT_WORK_INNER = -PIT_WIDTH / 2 - 13.
PIT_WALK_INNER = PIT_WORK_INNER - 3.048


def running_apron_offset(station):
    return APRON / 2


def pit_pose(station):
    tx, ty = PIT.heading(0.)
    return Pose(PIT_START[0] + tx * station, PIT_START[1] + ty * station, tx, ty,
                width=PIT_WIDTH, apron=0.)


def pit_stall_pose(index):
    return pit_pose(STALL_START + (index + .5) * STALL_SPACING)


def _attachment(station):
    pose = race_pose(station)
    return racing_surface(station, -WIDTH / 2 - APRON / 2)[:2], (pose.tx, pose.ty)


def _pit_exit_connector():
    first = TRACK.nearest(PIT_END)
    start_tangent = PIT.heading(PIT_LENGTH)

    def cross(a, b):
        return a[0] * b[1] - a[1] * b[0]

    def cross_section(station):
        pose = race_pose(station)
        edge = racing_surface(station, -WIDTH / 2)[:2]
        inward = (-pose.ty, pose.tx)
        to_pit_end = (PIT_END[0] - edge[0], PIT_END[1] - edge[1])
        # Intersect the track's local cross section with the straight pit baseline.
        straight_offset = cross(to_pit_end, start_tangent) / cross(inward, start_tangent)
        return edge, inward, straight_offset

    _, _, start_offset = cross_section(first)
    transition = 125.

    def point(station):
        edge, inward, straight_offset = cross_section(station)
        distance = station - first
        apron_offset = mix(start_offset, APRON / 2, blend(distance / (EXIT_STATION - first)))
        offset = mix(straight_offset, apron_offset, blend(distance / transition))
        return edge[0] + inward[0] * offset, edge[1] + inward[1] * offset

    plan = Path([point(mix(first, EXIT_STATION, i / 640)) for i in range(641)])
    controls = [plan.at(plan.length * i / 64) for i in range(65)]
    controls[0] = PIT_END
    controls[-1], end_tangent = _attachment(EXIT_STATION)
    # Align the terminal control chords with the actual seams after resampling.
    for i, neighbor, tangent in ((0, 1, start_tangent), (-1, -2, end_tangent)):
        end = controls[i]
        along = sum((p - a) * d for p, a, d in zip(controls[neighbor], end, tangent))
        controls[neighbor] = tuple(a + along * d for a, d in zip(end, tangent))
    return controls, catmull(controls, False, 16)


_entry, _entry_tangent = _attachment(ENTRY_STATION)
_entry_controls, ENTRY = _connector(_entry, _entry_tangent, PIT_START, PIT.heading(0))
_exit_controls, EXIT = _pit_exit_connector()


def exit_half_width(station):
    # Narrow the paved connector as it joins the 5.49 m flat apron.
    progress = station / EXIT.length
    return mix(PIT_WIDTH / 2, APRON / 2 - .12, blend((progress - .55) / .40))


def point_json(point):
    return dict(x=round(point[0], 7), z=round(point[1], 7))


def game_definition():
    track = json.loads((ROOT / "reference/track-11-before.json").read_bytes())
    track.update(
        location=dict(city="Irish Hills", state="MI"),
        qualifyingReferenceMph=round(BENCHMARK_MPH, 3),
        layoutPoints=[
            dict(point_json(point), banking=round(bank_at(TRACK.stations[i * 24]), 6),
                 elevationMeters=0., widthMeters=WIDTH, apronWidthMeters=APRON)
            for i, point in enumerate(CONTROLS)
        ],
        bankingTransitions=[
            dict(start=a, end=b, fromDegrees=lo, toDegrees=hi)
            for a, b, lo, hi in BANK_TRANSITIONS
        ],
        raceMarkers=dict(hasStartFinish=True, startFinish=0., hasRestartZone=True,
                         restartZoneStart=RESTART_START, restartZoneEnd=RESTART_END),
        pitRoad=dict(
            enabled=True, widthMeters=PIT_WIDTH, speedLimitMph=55,
            speedLimitStart=PIT_SPEED_START, speedLimitEnd=PIT_SPEED_END,
            points=[point_json(p) for p in _pit_controls],
            entrance=[point_json(p) for p in _entry_controls],
            exit=[point_json(p) for p in _exit_controls],
            stalls=[point_json(pit_stall_pose(i).offset(-PIT_WIDTH * .32)) for i in range(STALL_COUNT)],
        ),
    )
    return track


if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser(description="Write the Michigan Unity track definition.")
    parser.add_argument("output", type=FilePath, nargs="?", default=ROOT / "MichiganSpeedway.json")
    args = parser.parse_args()
    print("Fit:", _fit_parameters, "m/px:", round(METERS_PER_PIXEL, 4))
    print("Lap:", TRACK.length, "pit:", PIT_LENGTH, "connectors:", ENTRY.length, EXIT.length)
    print("Handoffs:", ENTRY_STATION, EXIT_STATION, "tight turn extrema:", _half[-2])
    args.output.write_text(json.dumps(game_definition(), indent=2) + "\n", encoding="utf-8")
