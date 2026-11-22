"""Source geometry for the Richmond Raceway D-shaped oval, redrawn from the supplied route image."""
from bisect import bisect_right
from dataclasses import dataclass
import json
import math
from pathlib import Path as FilePath

LAP = 1207.008
WIDTH = 18.288
PIT_WIDTH = 11.0
PIT_FOOTPRINT = 64 * .3048
PIT_WORK_INNER = PIT_WIDTH/2 - PIT_FOOTPRINT
PIT_WALK_INNER = PIT_WORK_INNER - 3.048
WALL_HEIGHT = 1.067
WALKWAY_WIDTH = 3.048
BENCHMARK_SECONDS = 20.674
BENCHMARK_MPH = 0.75 * 3600.0 / BENCHMARK_SECONDS
RESTART_START = .929
RESTART_END = .977


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


def tangent_blend(first, first_tangent, last, last_tangent, handle_fraction):
    """Quintic with zero curvature and the specified tangent at each endpoint."""
    length = math.dist(first, last) * handle_fraction
    start_handle, end_handle = mul(first_tangent, length), mul(last_tangent, length)
    return bezier([first, add(first, start_handle), add(first, mul(start_handle, 2)),
                   sub(last, mul(end_handle, 2)), sub(last, end_handle), last], 260)


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
    # Centers of the outer red band, paced clockwise in pixel coordinates.
    # Collinear stations preserve the diagrammed backstretch as a straight.
    trace = [(145.5, 140.7), (158, 138.8), (170.5, 135.7), (182.3, 130.7),
             (193.5, 125.5), (204, 119.4), (214.5, 112.2), (224.1, 104.2),
             (232.8, 95.8), (240.1, 86), (244.2, 75.2), (246.2, 63.3),
             (243.8, 51.5), (239, 40.9), (230.8, 32.), (220.5, 25.3),
             (209.5, 21.8), (198., 20.3), (186.1, 22.4)]
    trace.extend((x, 70.2 - .258*x) for x in range(174, 78, -12))
    trace.extend([(78.2, 70.2 - .258*78.2), (67.1, 56.), (57.7, 64.5), (51.1, 74.6),
                  (47.8, 86.5), (47.3, 98.5), (50.5, 110.), (56.8, 120.5),
                  (66.1, 129.2), (77.2, 135.8), (89.5, 139.4), (103.1, 141.5),
                  (117.2, 142.1), (131.5, 142.1)])
    # Uniform cyclic cubic B-spline fits clean edges through bitmap rounding.
    points = []
    for i in range(len(trace)):
        neighbors = [trace[(i+j) % len(trace)] for j in (-1, 0, 1, 2)]
        for k in range(48):
            t = k / 48
            weights = ((1-t)**3, 3*t**3-6*t*t+4, -3*t**3+3*t*t+3*t+1, t**3)
            points.append(tuple(sum(p[d]*w for p, w in zip(neighbors, weights))/6
                                for d in (0, 1)))
    return points + [points[0]]


_plan = Path(plan())
_finish = _plan.nearest((137., 141.7))
METERS_PER_PIXEL = LAP / _plan.length


def diagram_xy(x, y=None):
    if y is None:
        x, y = x
    return (x - 145.) * METERS_PER_PIXEL, (81. - y) * METERS_PER_PIXEL


_controls = [diagram_xy(_plan.at((_finish + _plan.length * i / 240) % _plan.length))
             for i in range(240)]
_race_native = Path(catmull(_controls, True, 24))
_rescale = LAP / _race_native.length
TRACK = Path([mul(point, _rescale) for point in _race_native.points])


def nearest_race(x, y=None):
    if y is None:
        x, y = x
    return TRACK.nearest((x, y))


def marker(x, y):
    return nearest_race(mul(diagram_xy(x, y), _rescale))


# The red band is the reference route, rather than a dimensioned pavement width.
# Blend into the wide paved frontstretch wedge where pit road separates from the oval.
_width_keys = [(0., WIDTH), (LAP, WIDTH)]
ENTRY_STATION = marker(48, 103)
EXIT_STATION = marker(243, 81)
TURN_FOUR_WALL_END = ENTRY_STATION - 42.
PIT_ENTRY_WALL_EXTENSION_STATION = ENTRY_STATION - 21.
TURN_FOUR_GATE_STATION = (TURN_FOUR_WALL_END + PIT_ENTRY_WALL_EXTENSION_STATION) / 2
PIT_WALL_EXTENSION_STATION = EXIT_STATION + 24.
TURN_WALL_START = EXIT_STATION + 45.
TURN_GATE_STATION = (PIT_WALL_EXTENSION_STATION + TURN_WALL_START) / 2
RECOVERY_STATION = marker(145, 33)
RECOVERY_OPENING = 60.

_apron_keys = [
    (0., 44.), (marker(169,135), 42.), (marker(214,111), 26.),
    (marker(235,93), 14.), (EXIT_STATION-5, 12.5), (EXIT_STATION+5, 12.5),
    (marker(246,59), 8.), (marker(233,34), 3.048), (marker(55,67), 3.048),
    (marker(47,91), 9.), (ENTRY_STATION-5, 12.5), (ENTRY_STATION+5, 12.5),
    (marker(63,128), 20.), (marker(87,138), 29.), (LAP, 44.)]
_bank_keys = [(0., 8.), (marker(169,136), 8.), (marker(211,116), 14.),
              (marker(223,29), 14.), (marker(177,24), 2.),
              (marker(87,47), 2.), (marker(53,68), 14.),
              (marker(57,120), 14.), (marker(108,142), 8.), (LAP, 8.)]


def sample_keys(keys, s, blend=ease):
    for (a, lo), (b, hi) in zip(keys, keys[1:]):
        if a <= s <= b:
            return lerp(lo, hi, blend((s - a) / (b - a)))
    return keys[-1][1]


_control_stations = [s * _rescale for s in _race_native.stations[::24]]
_control_banks = [sample_keys(_bank_keys, s, quintic) for s in _control_stations]
_control_widths = [sample_keys(_width_keys, s) for s in _control_stations]
_control_aprons = [sample_keys(_apron_keys, s) for s in _control_stations]

# Stay inside Richmond's fully capped running-apron section, including the
# interpolation between controls. A 5.7 m paved shoulder leaves room for the
# 2.15 m line and keeps the response-route pickup and pass limits identical.
GRASS_TAPER_START = .863
GRASS_TAPER_END = .198
GRASS_APRON_WIDTH = 5.7


def _grass_apron_control(station, old_width):
    fraction = station / LAP
    if fraction >= GRASS_TAPER_START:
        blend = ease((fraction - GRASS_TAPER_START) / (.889 - GRASS_TAPER_START))
    else:
        blend = ease((GRASS_TAPER_END - fraction) / (GRASS_TAPER_END - .163))
    return lerp(old_width, min(old_width, GRASS_APRON_WIDTH), blend)


_control_paved_aprons = [_grass_apron_control(s, w) for s, w in zip(_control_stations, _control_aprons)]


def paved_apron_width(distance):
    """Match the catalog's paved widths using Unity TrackLayout.Widths interpolation."""
    station = distance % LAP
    i = min(len(_control_stations) - 2, bisect_right(_control_stations, station) - 1)
    low, high = _control_stations[i:i+2]
    blend = ease((station - low) / (high - low))
    return lerp(_control_paved_aprons[i], _control_paved_aprons[i+1], blend)


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
    # The shorter U-shaped red route is pit road. Its middle section is a
    # straight line independent of the sweeping race frontstretch.
    entry, left, right, exit = [mul(diagram_xy(p), _rescale)
                                for p in ((69.8, 121.8), (109.5, 128.15),
                                          (213.3, 99.45), (225.2, 93.2))]
    straight = unit(sub(right, left))
    arriving = unit((7.7, -4.4))
    leaving = unit((5.1, 3.7))
    smooth = Path(chain(tangent_blend(entry, arriving, left, straight, .18),
                        line(left, right, 220),
                        tangent_blend(right, straight, exit, leaving, .18)))
    controls = [smooth.at(smooth.length * i / 224) for i in range(225)]
    return controls, Path(catmull(controls, False, 16))


_pit_controls, PIT = _pit_path()
PIT_LENGTH = PIT.length
PIT_SPEED_START = .021
PIT_SPEED_END = .979
STALL_COUNT = 40
STALL_START = .049 * PIT_LENGTH
STALL_END = .951 * PIT_LENGTH
STALL_SPACING = (STALL_END - STALL_START) / STALL_COUNT
PIT_WALL_START = STALL_START - 3.
PIT_WALL_END = STALL_END + 3.
# Cover the full row of pit boxes plus a short runout beyond each end.
PIT_DIVIDER_RUNOUT = 3.5
PIT_DIVIDER_START = STALL_START - PIT_DIVIDER_RUNOUT
PIT_DIVIDER_END = STALL_END + PIT_DIVIDER_RUNOUT
PIT_DIVIDER_EDGE = PIT_WIDTH / 2 + .65
PIT_DIVIDER_WIDTH = .30


def running_apron_offset(station):
    """Preserve the pit handoffs and keep the running apron outside the divider."""
    fraction = station % LAP / LAP
    if fraction >= .807:
        blend = ease((fraction - .807) / (.858 - .807))
    else:
        blend = ease((.243 - fraction) / (.243 - .203))
    midpoint = race_pose(station).apron / 2
    return lerp(midpoint, min(midpoint, 2.15), blend)


def pit_pose(distance):
    x, y = PIT.at(distance)
    tx, ty = PIT.heading(distance)
    return Pose(x, y, 0., tx, ty, ty, -tx, 0., PIT_WIDTH, 0.)


def pit_stall_pose(i):
    return pit_pose(STALL_START + (i + .5) * STALL_SPACING)


def _connector(first, first_tangent, last, last_tangent):
    source = Path(tangent_blend(first, first_tangent, last, last_tangent, .14))
    stations = [source.length * i / 96 for i in range(97)]
    controls = [source.at(s) for s in stations]
    return controls, Path(catmull(controls, False, 16))


def attach(station):
    pose = race_pose(station)
    position = racing_surface(station, -(pose.width + pose.apron) * .5)[:2]
    return position, (pose.tx, pose.ty)


entry_position, entry_tangent = attach(ENTRY_STATION)
exit_position, exit_tangent = attach(EXIT_STATION)
_entry_controls, ENTRY = _connector(entry_position, entry_tangent, PIT.points[0], PIT.heading(0.))
_exit_controls, EXIT = _connector(PIT.points[-1], PIT.heading(PIT.length), exit_position, exit_tangent)


def inner_apron_wall_edge(station):
    p = race_pose(station)
    return racing_surface(station, -p.width / 2 - p.apron - 1.2)


def pit_connector_inner_edge(path, distance):
    x, y = path.at(distance)
    tx, ty = path.heading(distance)
    return x - ty * PIT_WIDTH / 2, y + tx * PIT_WIDTH / 2


def pit_wall_head():
    station = PIT_ENTRY_WALL_EXTENSION_STATION
    start = inner_apron_wall_edge(station)[:2]
    incoming = unit(sub(inner_apron_wall_edge(station + .3)[:2], start))
    target = pit_connector_inner_edge(ENTRY, 0.)
    outgoing = unit(sub(pit_connector_inner_edge(ENTRY, .3), target))
    return Path(tangent_blend(start, incoming, target, outgoing, .22))


def pit_wall_tail():
    tip = pit_connector_inner_edge(EXIT, EXIT.length)
    incoming = unit(sub(tip, pit_connector_inner_edge(EXIT, EXIT.length - .3)))
    target = inner_apron_wall_edge(PIT_WALL_EXTENSION_STATION)[:2]
    outgoing = unit(sub(target, inner_apron_wall_edge(PIT_WALL_EXTENSION_STATION - .3)[:2]))
    return Path(tangent_blend(tip, incoming, target, outgoing, .22))


def turn_gate_access():
    gate = race_pose(TURN_GATE_STATION)
    apron_start = racing_surface(TURN_GATE_STATION, -gate.width / 2 - gate.apron * .55)[:2]
    infield_end = (188., 39.)
    return Path((apron_start, infield_end))


PACE_BAY = (196., 20.8)
PACE_BAY_HEADING = (1., 0.)
PACE_RETURN_STATION = TURN_GATE_STATION - 18.
PACE_DEPARTURE_STATION = TURN_GATE_STATION + 19.


def pace_paths():
    """Join the Turn One safety gate to a forward-facing infield pace bay."""
    gate = turn_gate_access()
    inside = gate.at(gate.length * .64)
    inward = gate.heading(0.)

    def apron_pose(station):
        pose = race_pose(station)
        side = -pose.width / 2 - 2.3
        return racing_surface(station, side)[:2], (pose.tx, pose.ty)

    entry, entering = apron_pose(PACE_RETURN_STATION)
    exit, leaving = apron_pose(PACE_DEPARTURE_STATION)
    gate_return = tangent_blend(entry, entering, inside, inward, .23)
    gate_departure = Path(tangent_blend(inside, mul(inward, -1.), exit, leaving, .23))
    join = gate_departure.at(10.)
    join_heading = gate_departure.heading(10.)
    return_bend, return_heading = (188.5, 29.5), (0., -1.)
    departure_bend, departure_heading = (205.8, 28.1), (0., 1.)
    return_drive = chain(tangent_blend(inside, inward, return_bend, return_heading, .27),
                         tangent_blend(return_bend, return_heading, PACE_BAY, PACE_BAY_HEADING, .29))
    departure_drive = chain(tangent_blend(PACE_BAY, PACE_BAY_HEADING, departure_bend, departure_heading, .29),
                            tangent_blend(departure_bend, departure_heading, join, join_heading, .26))
    remaining = [gate_departure.at(lerp(10., gate_departure.length, i / 200)) for i in range(201)]
    return (Path(chain(gate_return, return_drive)), Path(chain(departure_drive, remaining)),
            Path(return_drive), Path(departure_drive))


def turn_four_gate_access():
    gate = race_pose(TURN_FOUR_GATE_STATION)
    yard = race_pose(RECOVERY_STATION)
    apron_start = racing_surface(TURN_FOUR_GATE_STATION, -gate.width / 2 - gate.apron * .55)[:2]
    old_end = yard.offset(along=90., side=-109.)[:2]
    inward = (-gate.nx, -gate.ny)
    road_lead = add(apron_start, mul(inward, 24.))
    eastbound = (-yard.tx, -yard.ty)
    approach = Path(chain(line(apron_start, road_lead),
                          tangent_blend(road_lead, inward, old_end, eastbound, .24)))
    # Retain the gate and campsite intersections. The last bend enters the
    # yard on the tow-reserved side of the occupied hauler parking row.
    cut = approach.nearest(yard.offset(along=122., side=-109.)[:2])
    first = approach.at(cut)
    cutoff = bisect_right(approach.stations,cut)
    infield_end = yard.offset(along=90., side=-90.)[:2]
    tail = tangent_blend(first,approach.heading(cut),infield_end,eastbound,.22)
    return Path(chain([*approach.points[:cutoff],first],tail))


def point_json(point):
    return dict(x=round(point[0], 7), z=round(point[1], 7))


def game_definition():
    controls = []
    for i, point in enumerate(_controls):
        controls.append(dict(point_json(mul(point, _rescale)), banking=round(_control_banks[i], 6),
                             elevationMeters=0., widthMeters=round(_control_widths[i], 6),
                             apronWidthMeters=round(_control_paved_aprons[i], 6)))
    points = [point_json(p) for p in _pit_controls]
    # World-station centerlines allow the native assignment code to project to the stall baseline.
    stalls = [point_json(pit_stall_pose(i).offset(side=-PIT_WIDTH * .32)) for i in range(STALL_COUNT)]
    return dict(
        id=5, name="Richmond Raceway", location=dict(city="Richmond", state="VA"),
        qualifyingReferenceMph=BENCHMARK_MPH, lengthMiles=.75, aspectRatio=1.58,
        majorStyle="Oval", minorStyle="Oval", surface="Asphalt", restrictorPlate=False,
        widthMeters=WIDTH, pitStalls=STALL_COUNT, NumberOfQualifyingLaps=1, raceLaps=400,
        raceStageLaps=[70, 160, 170],
        banking=dict(turns12=14., turns34=14., frontStraight=8., backStraight=2.),
        smoothLayoutBanking=True, layoutPoints=controls,
        raceMarkers=dict(hasStartFinish=True, startFinish=0., hasRestartZone=True,
                         restartZoneStart=RESTART_START, restartZoneEnd=RESTART_END),
        pitRoad=dict(enabled=True, widthMeters=PIT_WIDTH, speedLimitMph=40,
                     speedLimitStart=PIT_SPEED_START, speedLimitEnd=PIT_SPEED_END, points=points,
                     entrance=[point_json(p) for p in _entry_controls],
                     exit=[point_json(p) for p in _exit_controls], stalls=stalls))


if __name__ == "__main__":
    output = FilePath(__file__).resolve().parent / "RichmondRaceway.json"
    output.write_text(json.dumps(game_definition(), indent=2) + "\n", encoding="utf-8")
