"""Source geometry for the post-2018 Phoenix Raceway, redrawn from the supplied route image."""
from bisect import bisect_right
from dataclasses import dataclass
import json
import math
from pathlib import Path as FilePath

LAP = 1609.344
PIT_WIDTH = 11.0
WALL_HEIGHT = 1.067
WALKWAY_WIDTH = 3.048
BENCHMARK_SECONDS = 25.480
BENCHMARK_MPH = 3600.0 / BENCHMARK_SECONDS
RESTART_START = .949
RESTART_END = .983


def add(a, b):
    return a[0] + b[0], a[1] + b[1]


def sub(a, b):
    return a[0] - b[0], a[1] - b[1]


def mul(a, scale):
    return a[0] * scale, a[1] * scale


def unit(a):
    return mul(a, 1 / math.hypot(*a))


def lerp(a, b, t):
    return a + (b - a) * t


def ease(t):
    t = max(0., min(1., t))
    return t * t * (3. - 2. * t)


def quintic(t):
    t = max(0., min(1., t))
    return t ** 3 * (10. + t * (-15. + 6. * t))


def line(a, b, count=30):
    return [add(a, mul(sub(b, a), i / count)) for i in range(count + 1)]


def chain(*sections):
    return [point for section in sections for point in section[:-1]] + [sections[-1][-1]]


def bezier(points, count=200):
    degree = len(points) - 1
    result = []
    for i in range(count + 1):
        t = i / count
        weights = [math.comb(degree, j) * (1. - t) ** (degree - j) * t ** j for j in range(degree + 1)]
        result.append(tuple(sum(point[k] * weight for point, weight in zip(points, weights)) for k in (0, 1)))
    return result


def fit_corner(points, incoming, outgoing):
    """Clamped C2 cubic through image anchors, eased to zero curvature at the straight ties."""
    n = len(points)
    s = cumulative(points)
    h = [b - a for a, b in zip(s, s[1:])]
    curvatures = []
    for axis in (0, 1):
        lower = [0.] + h
        upper = h + [0.]
        diag = [2 * h[0]] + [2 * (h[i - 1] + h[i]) for i in range(1, n - 1)] + [2 * h[-1]]
        slopes = [(points[i + 1][axis] - points[i][axis]) / h[i] for i in range(n - 1)]
        values = [6 * (slopes[0] - incoming[axis])] + [6 * (slopes[i] - slopes[i - 1])
                                                         for i in range(1, n - 1)] + [6 * (outgoing[axis] - slopes[-1])]
        for i in range(1, n):
            factor = lower[i] / diag[i - 1]
            diag[i] -= factor * upper[i - 1]
            values[i] -= factor * values[i - 1]
        result = [0.] * n
        for i in range(n - 1, -1, -1):
            result[i] = (values[i] - upper[i] * (result[i + 1] if i + 1 < n else 0.)) / diag[i]
        curvatures.append(result)

    def evaluate(i, t):
        left, right = 1. - t, t
        return tuple(left * points[i][axis] + right * points[i + 1][axis] +
                     h[i] ** 2 / 6 * ((left ** 3 - left) * curvatures[axis][i] +
                                      (right ** 3 - right) * curvatures[axis][i + 1])
                     for axis in (0, 1))

    def tangent(i, t):
        left, right = 1. - t, t
        return tuple((points[i + 1][axis] - points[i][axis]) / h[i] +
                     h[i] / 6 * (-(3 * left ** 2 - 1) * curvatures[axis][i] +
                                 (3 * right ** 2 - 1) * curvatures[axis][i + 1])
                     for axis in (0, 1))

    def tie(i, left, right):
        p0, p1 = points[i:i + 2]
        d0, d1 = mul(tangent(i, 0), h[i] / 5), mul(tangent(i, 1), h[i] / 5)
        a0 = tuple(value[i] * h[i] ** 2 / 20 if left else 0. for value in curvatures)
        a1 = tuple(value[i + 1] * h[i] ** 2 / 20 if right else 0. for value in curvatures)
        return bezier((p0, add(p0, d0), add(add(p0, mul(d0, 2)), a0),
                       add(sub(p1, mul(d1, 2)), a1), sub(p1, d1), p1), 65)

    sections = [tie(0, False, True)]
    sections.extend([evaluate(i, j / 65) for j in range(66)] for i in range(1, n - 2))
    sections.append(tie(n - 2, True, False))
    return chain(*sections)


def cumulative(points):
    result = [0.]
    for a, b in zip(points, points[1:]):
        result.append(result[-1] + math.dist(a, b))
    return result


class Path:
    def __init__(self, points):
        self.points = list(points)
        self.stations = cumulative(self.points)
        self.length = self.stations[-1]

    def at(self, distance):
        distance = min(self.length, max(0., distance))
        i = max(0, min(len(self.points) - 2, bisect_right(self.stations, distance) - 1))
        f = (distance - self.stations[i]) / (self.stations[i + 1] - self.stations[i])
        return tuple(lerp(a, b, f) for a, b in zip(self.points[i], self.points[i + 1]))

    def heading(self, distance, loop=False, window=.4):
        a, b = distance - window, distance + window
        if loop:
            a, b = a % self.length, b % self.length
        return unit(sub(self.at(b), self.at(a)))

    def nearest(self, point):
        best = math.inf
        station = 0.
        for i, (start, end) in enumerate(zip(self.points, self.points[1:])):
            dx, dy = sub(end, start)
            x, y = sub(point, start)
            f = max(0., min(1., (x * dx + y * dy) / (dx * dx + dy * dy)))
            dist = (x - f * dx) ** 2 + (y - f * dy) ** 2
            if dist < best:
                best = dist
                station = lerp(self.stations[i], self.stations[i + 1], f)
        return station


def catmull(points, closed, density):
    """Unity TrackLayout / TrackPitSpline uniform Catmull–Rom implementation."""
    size = len(points)
    expanded = []
    for i in range(size if closed else size - 1):
        a = points[(i - 1) % size if closed else max(0, i - 1)]
        b = points[i]
        c = points[(i + 1) % size]
        d = points[(i + 2) % size if closed else min(size - 1, i + 2)]
        for j in range(density):
            t = j / density
            expanded.append(tuple(
                .5 * (2 * b[k] + (c[k] - a[k]) * t + (2*a[k] - 5*b[k] + 4*c[k] - d[k]) * t*t +
                      (-a[k] + 3*b[k] - 3*c[k] + d[k]) * t*t*t)
                for k in (0, 1)))
    return expanded + [points[0] if closed else points[-1]]


def plan():
    west = [(1100, 331.7), (1055, 356), (1018, 392), (997, 436), (996, 477),
            (1005, 521), (1028, 564), (1067, 602), (1114, 627)]
    dogleg = [(1220, 661), (1260, 672), (1303, 680), (1342, 679), (1384, 670), (1427, 651), (1470, 627)]
    east = [(1629, 535.8), (1662, 512), (1697, 471), (1719, 425), (1728, 374),
            (1723, 328), (1706, 281), (1682, 243), (1644, 211), (1602, 193),
            (1551, 185.7), (1520, 188.2), (1490, 196)]
    back = unit(sub(west[0], east[-1]))
    approach = unit(sub(dogleg[0], west[-1]))
    departure = unit(sub(east[0], dogleg[-1]))
    return chain(fit_corner(west, back, approach), line(west[-1], dogleg[0]),
                 fit_corner(dogleg, approach, departure), line(dogleg[-1], east[0]),
                 fit_corner(east, departure, back), line(east[-1], west[0], 70))


_plan = Path(plan())
_finish = _plan.nearest((1182, 649.2))
METERS_PER_PIXEL = LAP / _plan.length


def diagram_xy(x, y=None):
    if y is None:
        x, y = x
    return (x - 1360.) * METERS_PER_PIXEL, (440. - y) * METERS_PER_PIXEL


def overhead_xy(x, y=None):
    """Approximate aerial placement registered against five diagram landmarks."""
    if y is None:
        x, y = x
    x, y = (x - 1920.) / 1000., (y - 1080.) / 1000.
    return diagram_xy(1338.6296 - 244.4555*x - 65.6673*y,
                      467.8301 + 44.8107*x - 261.1768*y)


_controls = [diagram_xy(_plan.at((_finish + _plan.length * i / 240) % _plan.length)) for i in range(240)]
_race_native = Path(catmull(_controls, True, 24))
_rescale = LAP / _race_native.length
TRACK = Path([mul(point, _rescale) for point in _race_native.points])


def nearest_race(x, y=None):
    if y is None:
        x, y = x
    return TRACK.nearest((x, y))


def marker(x, y):
    return nearest_race(mul(diagram_xy(x, y), _rescale))


_width_keys = [(0., 18.8976), (marker(1400, 662), 18.8976), (marker(1615, 545), 17.5),
               (marker(1718, 422), 16.6), (marker(1490, 196), 15.8496),
               (marker(1122, 324), 15.8496), (marker(998, 483), 16.5), (LAP, 18.8976)]
_apron_keys = [(0., 15.), (marker(1305, 680), 49.), (marker(1420, 654), 39.),
               (marker(1550, 582), 11.), (marker(1655, 516), 7.6), (marker(1718, 422), 6.1),
               (marker(1490, 196), 6.1), (925., 8.8),
               (1030., 8.8), (marker(1082, 343), 7.6),
               (marker(996, 478), 7.6), (marker(1085, 611), 7.6), (LAP, 15.)]
_bank_keys = [(0., 9.), (marker(1230, 665), 9.), (marker(1315, 680), 10.5),
              (marker(1400, 662), 10.5), (marker(1510, 605), 9.),
              (marker(1610, 546), 9.), (marker(1705, 453), 8.5),
              (marker(1722, 325), 8.5), (marker(1490, 196), 3.),
              (marker(1122, 324), 3.), (marker(1017, 395), 10.5),
              (marker(1002, 513), 10.5), (marker(1140, 635), 9.), (LAP, 9.)]


def sample_keys(keys, s, blend=ease):
    for (a, lo), (b, hi) in zip(keys, keys[1:]):
        if a <= s <= b:
            return lerp(lo, hi, blend((s - a) / (b - a)))
    return keys[-1][1]


_control_stations = [s * _rescale for s in _race_native.stations[::24]]
_control_banks = [sample_keys(_bank_keys, s, quintic) for s in _control_stations]
_control_widths = [sample_keys(_width_keys, s) for s in _control_stations]
_control_aprons = [sample_keys(_apron_keys, s) for s in _control_stations]


def _bank_slope(i):
    size = len(_control_banks) - 1
    previous = (i - 1) % size
    incoming_span = (_control_stations[i] - _control_stations[previous]) % LAP
    outgoing_span = _control_stations[i + 1] - _control_stations[i]
    incoming = (_control_banks[i] - _control_banks[previous]) / incoming_span
    outgoing = (_control_banks[i + 1] - _control_banks[i]) / outgoing_span
    if incoming * outgoing <= 0.:
        return 0.
    a = 2. * outgoing_span + incoming_span
    b = outgoing_span + 2. * incoming_span
    return (a + b) / (a / incoming + b / outgoing)


_control_bank_slopes = [_bank_slope(i) for i in range(len(_control_banks) - 1)]
_control_bank_slopes.append(_control_bank_slopes[0])


@dataclass(frozen=True)
class Pose:
    x: float
    y: float
    z: float
    tx: float
    ty: float
    nx: float
    ny: float
    bank: float
    width: float
    apron: float

    def offset(self, along=0., side=0., height=0.):
        return self.x + self.tx * along + self.nx * side, self.y + self.ty * along + self.ny * side, self.z + height


def race_pose(distance):
    s = distance % LAP
    x, y = TRACK.at(s)
    tx, ty = TRACK.heading(s, True)
    i = min(len(_control_stations) - 2, bisect_right(_control_stations, s) - 1)
    span = _control_stations[i + 1] - _control_stations[i]
    t = (s - _control_stations[i]) / span
    rem = 1. - t
    bank_blend = t * t * (3. - 2. * t)
    angle = (lerp(_control_banks[i], _control_banks[i + 1], bank_blend) +
             span * t * rem * (rem * _control_bank_slopes[i] - t * _control_bank_slopes[i + 1]))
    width = lerp(_control_widths[i], _control_widths[i + 1], bank_blend)
    apron = lerp(_control_aprons[i], _control_aprons[i + 1], bank_blend)
    datum = math.sin(math.radians(angle)) * width * .5
    return Pose(x, y, datum, tx, ty, ty, -tx, angle, width, apron)


def racing_surface(distance, lane):
    pose = race_pose(distance)
    angle = math.radians(pose.bank)
    edge = max(-pose.width / 2, min(pose.width / 2, lane))
    horizontal = edge * math.cos(angle) + lane - edge
    return pose.offset(side=horizontal, height=edge * math.sin(angle))


def _pit_path():
    route = [(1190, 319), (1153, 333), (1124, 357), (1105, 387), (1093, 424),
             (1093, 463), (1104, 502), (1125, 535), (1157, 566), (1199, 589),
             (1246, 606.8), (1296, 616), (1341, 620.5), (1380, 620.2)]
    meters = [mul(diagram_xy(p), _rescale) for p in route]
    smooth = Path(fit_corner(meters, unit(sub(meters[1], meters[0])), unit(sub(meters[-1], meters[-2]))))
    controls = [smooth.at(smooth.length * i / 112) for i in range(113)]
    return controls, Path(catmull(controls, False, 16))


_pit_controls, PIT = _pit_path()
PIT_LENGTH = PIT.length
PIT_SPEED_START = .045
PIT_SPEED_END = .955
STALL_COUNT = 40
STALL_START = .07 * PIT_LENGTH
STALL_END = .94 * PIT_LENGTH
STALL_SPACING = (STALL_END - STALL_START) / STALL_COUNT


def pit_pose(distance):
    x, y = PIT.at(distance)
    tx, ty = PIT.heading(distance)
    return Pose(x, y, .02, tx, ty, ty, -tx, 0., PIT_WIDTH, 0.)


def pit_stall_pose(i):
    return pit_pose(STALL_START + (i + .5) * STALL_SPACING)


def _connector(first, first_tangent, last, last_tangent):
    reach = math.dist(first, last)
    d0, d1 = mul(first_tangent, reach * .14), mul(last_tangent, reach * .14)
    source = Path(bezier([first, add(first, d0), add(first, mul(d0, 2)),
                          sub(last, mul(d1, 2)), sub(last, d1), last], 260))
    stations = [source.length * i / 96 for i in range(97)]
    controls = [source.at(s) for s in stations]
    return controls, Path(catmull(controls, False, 16))


ENTRY_STATION = marker(1376, 235.7)
EXIT_STATION = marker(1600, 552.4)


def attach(station):
    pose = race_pose(station)
    position = racing_surface(station, -pose.width * .5 - 6.25)[:2]
    return position, (pose.tx, pose.ty)


entry_position, entry_tangent = attach(ENTRY_STATION)
exit_position, exit_tangent = attach(EXIT_STATION)
_entry_controls, ENTRY = _connector(entry_position, entry_tangent, PIT.points[0], PIT.heading(0.))
_exit_controls, EXIT = _connector(PIT.points[-1], PIT.heading(PIT.length), exit_position, exit_tangent)


def point_json(point):
    return dict(x=round(point[0], 7), z=round(point[1], 7))


def game_definition():
    controls = []
    for i, point in enumerate(_controls):
        controls.append(dict(point_json(mul(point, _rescale)), banking=round(_control_banks[i], 6),
                             elevationMeters=0., widthMeters=round(_control_widths[i], 6),
                             apronWidthMeters=round(_control_aprons[i], 6)))
    points = [point_json(p) for p in _pit_controls]
    # World-station centerlines allow the native assignment code to project to the stall baseline.
    stalls = [point_json(pit_stall_pose(i).offset(side=-PIT_WIDTH * .32)) for i in range(STALL_COUNT)]
    return dict(
        id=9, name="Phoenix Raceway", location=dict(city="Avondale", state="AZ"),
        qualifyingReferenceMph=BENCHMARK_MPH, lengthMiles=1., aspectRatio=1.50,
        majorStyle="Oval", minorStyle="Tri-Oval", surface="Asphalt", restrictorPlate=False,
        widthMeters=18.8976, pitStalls=STALL_COUNT, NumberOfQualifyingLaps=1, raceLaps=312,
        raceStageLaps=[60, 125, 127],
        banking=dict(turns12=8.5, turns34=10.5, frontStraight=9., backStraight=3.),
        smoothLayoutBanking=True, layoutPoints=controls,
        raceMarkers=dict(hasStartFinish=True, startFinish=0., hasRestartZone=True,
                         restartZoneStart=RESTART_START, restartZoneEnd=RESTART_END),
        pitRoad=dict(enabled=True, widthMeters=PIT_WIDTH, speedLimitMph=45,
                     speedLimitStart=PIT_SPEED_START, speedLimitEnd=PIT_SPEED_END, points=points,
                     entrance=[point_json(p) for p in _entry_controls],
                     exit=[point_json(p) for p in _exit_controls], stalls=stalls))


if __name__ == "__main__":
    output = FilePath(__file__).resolve().parent / "PhoenixRaceway.json"
    output.write_text(json.dumps(game_definition(), indent=2) + "\n", encoding="utf-8")
