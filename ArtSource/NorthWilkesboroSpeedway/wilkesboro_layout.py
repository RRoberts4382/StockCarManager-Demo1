"""North Wilkesboro metric geometry traced from the owner's Paint.NET layers."""

from bisect import bisect_right
from dataclasses import dataclass
import json
import math
from pathlib import Path as FilePath

LAP = 1005.84
WIDTH = 15.24
BENCHMARK_SECONDS = 18.905
BENCHMARK_MPH = 0.625 * 3600 / BENCHMARK_SECONDS
WALL_HEIGHT = 1.067
RESTART_START = .938
RESTART_END = .976
PIT_WIDTH = 10.80
PIT_WORK_INNER = -8.1
PIT_WALK_INNER = -10.8
PIT_SPEED_START = .020
PIT_SPEED_END = .976

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


# Race outline, painted divider, and black inside wall data are traced from the
# owner's layer masks in the original 725 x 430 Paint.NET image.
_trace = FilePath(__file__).with_name("wilkesboro_trace.json")
with _trace.open(encoding="utf-8") as data:
    TRACE = json.load(data)


def periodic_cubic(points, density=32):
    """Approximate a traced centerline with a periodic uniform cubic B-spline."""
    result = []
    for i, b in enumerate(points):
        a, c, d = (points[(i+j) % len(points)] for j in (-1, 1, 2))
        for step in range(density):
            t = step / density
            u = 1. - t
            weights = (u ** 3, 3*t**3 - 6*t*t + 4, -3*t**3 + 3*t*t + 3*t + 1, t ** 3)
            result.append(tuple(sum(p[k]*w for p, w in zip((a, b, c, d), weights)) / 6 for k in (0, 1)))
    return Path(result + [result[0]])


_raw = periodic_cubic(TRACE["race"])
ORIGIN = _raw.at(_raw.nearest(TRACE["startFinish"]))
CONTROL_COUNT = 224
_origin_distance = _raw.nearest(ORIGIN)
_step = _raw.length / CONTROL_COUNT
_unscaled = [sub(_raw.at((_origin_distance + i * _step) % _raw.length), ORIGIN)
             for i in range(CONTROL_COUNT)]
METERS_PER_PIXEL = LAP / Path(catmull(_unscaled, True, 24)).length


def diagram_xy(x, y=None):
    if y is None:
        x, y = x
    return (x - ORIGIN[0]) * METERS_PER_PIXEL, (ORIGIN[1] - y) * METERS_PER_PIXEL


_controls = [(x * METERS_PER_PIXEL, -y * METERS_PER_PIXEL) for x, y in _unscaled]
TRACK = Path(catmull(_controls, True, 24))
_control_stations = [TRACK.stations[i] for i in range(0, len(TRACK.stations), 24)]


def nearest_race(x, y=None):
    point = x if y is None else (x, y)
    return TRACK.nearest(point)


def marker(x, y):
    return nearest_race(diagram_xy(x, y))


def sample_keys(keys, x):
    index = bisect_right([station for station, value in keys], x) - 1
    index = min(len(keys) - 2, max(0, index))
    a, av = keys[index]
    b, bv = keys[index + 1]
    return lerp(av, bv, quintic((x - a) / (b - a)))


# The speedway documents frontstretch descent and backstretch ascent. Exact
# survey elevations are unavailable, so four meters of relief is modeled.
# This datum's grade plateaus line up with the two pit road transitions.
_west = diagram_xy(244, 0)[0]
_east = diagram_xy(450, 0)[0]


def site_height(x, y):
    return 4. * (1. - ease((x - _west) / (_east - _west)))


def elevation(station):
    x, y = TRACK.at(station % LAP)
    s = station % LAP
    # The modest grade shelf around the westbound handoff keeps two paved
    # lanes flush while they share pavement. The backstretch still climbs
    # through the entire four-meter relief.
    if 338. <= s <= 680.:
        keys = ((338.,0.),(471.,1.90),(532.,1.90),(654.,4.),(680.,4.))
        return sample_keys(keys,s)
    return site_height(x, y)


_BANK_PROFILE = (
    (0., 3.), (.075, 3.), (.207, 13.), (.297, 13.), (.427, 3.),
    (.582, 3.), (.708, 13.), (.801, 13.), (.940, 3.), (1., 3.),
)
_control_banks = [sample_keys(_BANK_PROFILE, s / LAP) for s in _control_stations]
_control_aprons = [sample_keys(((0.,6.1),(206.,6.1),(240.,7.8),(284.,7.8),(301.,6.1),(LAP,6.1)),s)
                   for s in _control_stations]
_control_elevations = [elevation(s) for s in _control_stations]


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
        return self.x + self.tx*along + self.nx*side, self.y + self.ty*along + self.ny*side, self.z + height


def race_pose(distance):
    s = distance % LAP
    x, y = TRACK.at(s)
    tx, ty = TRACK.heading(s, True)
    i = min(len(_control_stations) - 2, bisect_right(_control_stations, s) - 1)
    span = _control_stations[i+1] - _control_stations[i]
    t = (s - _control_stations[i]) / span
    u = 1. - t
    blend = t*t*(3.-2.*t)
    angle = (lerp(_control_banks[i], _control_banks[i+1], blend) +
             span*t*u*(u*_control_bank_slopes[i] - t*_control_bank_slopes[i+1]))
    apron = lerp(_control_aprons[i], _control_aprons[i+1], blend)
    return Pose(x, y, elevation(s) + math.sin(math.radians(angle))*WIDTH*.5,
                tx, ty, ty, -tx, angle, WIDTH, apron)


def racing_surface(distance, lane):
    pose = race_pose(distance)
    angle = math.radians(pose.bank)
    slope = (elevation(distance + .5) - elevation(distance - .5))
    grade = math.atan(slope)
    edge = max(-pose.width / 2, min(pose.width / 2, lane))
    along = -edge * math.sin(angle) * math.sin(grade)
    horizontal = edge * math.cos(angle) + lane - edge
    z = edge*math.sin(angle)*math.cos(grade)
    return pose.offset(along, horizontal, z - WIDTH*.5*math.sin(angle)*(1.-math.cos(grade)))


# Project the painted divider back to station coordinates and keep it clear
# of both the live apron lane and the outer pit through lane.
DIVIDER_START = marker(*TRACE["dividerEndpoints"][0])
DIVIDER_END = marker(*TRACE["dividerEndpoints"][1]) + LAP
DIVIDER_WIDTH = .40
PIT_CENTER_OFFSET = 25.9
_reference_divider = []
for p in TRACE["divider"]:
    xy = diagram_xy(p)
    s = nearest_race(xy)
    if s < DIVIDER_START - 50:
        s += LAP
    offset = math.dist(TRACK.at(s % LAP), xy)
    if DIVIDER_START - 10 < s < DIVIDER_END + 10:
        _reference_divider.append((s, min(19.0, max(16.7, offset))))
_reference_divider.sort()


def divider_side(station):
    return -sample_keys(_reference_divider, station)


def divider_edge(station):
    return racing_surface(station, divider_side(station))


_road_start = marker(236, 272)
_road_end = LAP + marker(469, 267)
ENTRY_STATION = marker(*TRACE["pitEntry"])
EXIT_STATION = marker(497., 208.)
_pit_exit_target = LAP + EXIT_STATION
_pit_entry_offset = (WIDTH + race_pose(ENTRY_STATION).apron) / 2
_pit_exit_offset = (WIDTH + race_pose(EXIT_STATION).apron) / 2


def road_xy(station):
    entry = quintic((station - ENTRY_STATION) / (DIVIDER_START - 9 - ENTRY_STATION))
    exit = quintic((_pit_exit_target - station) / (_pit_exit_target - DIVIDER_END - 9))
    offset = lerp(_pit_entry_offset if station < _road_start else _pit_exit_offset,
                  PIT_CENTER_OFFSET, min(entry, exit))
    return racing_surface(station, -offset)[:2]


def station_part(first, last):
    count = math.ceil((last-first) / 2.)
    controls = [road_xy(lerp(first,last,i/count)) for i in range(count+1)]
    return controls, Path(catmull(controls, False, 16))


_entry_controls, ENTRY = station_part(ENTRY_STATION, _road_start)
_pit_controls, PIT = station_part(_road_start, _road_end)
_exit_controls, EXIT = station_part(_road_end, _pit_exit_target)
PIT_LENGTH = PIT.length
ENTRY_DATUM = elevation(ENTRY_STATION)
EXIT_DATUM = elevation(EXIT_STATION)
PIT_START_DATUM = elevation(nearest_race(PIT.points[0]))
PIT_END_DATUM = elevation(nearest_race(PIT.points[-1]))

def road_height(path, distance):
    a, b = ((ENTRY_DATUM, PIT_START_DATUM) if path is ENTRY else
            (PIT_END_DATUM, EXIT_DATUM) if path is EXIT else (PIT_START_DATUM, PIT_END_DATUM))
    return lerp(a, b, ease(distance / path.length))


def road_pose(path, distance):
    x, y = path.at(distance)
    tx, ty = path.heading(distance)
    return Pose(x, y, road_height(path, distance), tx, ty, ty, -tx, 0., PIT_WIDTH, 0.)


ROAD_SECTIONS = ((ENTRY_STATION, _road_start, ENTRY),
                 (_road_start, _road_end, PIT),
                 (_road_end, _pit_exit_target, EXIT))
EAST_GATE_START = 304.8
EAST_GATE_END = 324.4


def pit_distance_at_station(station, first, last, path):
    count = (len(path.points)-1)//16
    index = (station-first)/(last-first)*count
    low = max(0,min(count-1,int(index)))
    return lerp(path.stations[low*16],path.stations[(low+1)*16],index-low)


def deck_height(station, side):
    """Slope paved surroundings continuously to the three exact lane ribbons."""
    x,y,z = racing_surface(station,side)
    datum = site_height(x,y)
    apron_edge = WIDTH*.5 + race_pose(station).apron
    if side >= -apron_edge-.00001:
        return z
    blend = ease((-side-apron_edge)/3.1)
    s = station % LAP
    if s < _pit_exit_target - LAP:
        s += LAP
    for first,last,path in ROAD_SECTIONS:
        if first <= s <= last:
            d = pit_distance_at_station(s,first,last,path)
            center = path.at(d)
            gap = math.dist(center,(x,y))
            seam_width = 4.
            match = 1. - ease((gap - PIT_WIDTH*.5) / seam_width)
            datum = lerp(datum,road_height(path,d)-.009,match)
            return min(lerp(z,datum,blend),road_height(path,d)-.008) if gap < PIT_WIDTH*.5 else lerp(z,datum,blend)
    return datum

def pit_pose(distance):
    return road_pose(PIT, distance)


def pit_marker(x, y):
    return PIT.nearest(diagram_xy(x, y))


STALL_COUNT = 40
STALL_START = 3.8
STALL_END = PIT_LENGTH - 4.
PIT_GARAGE_GATES = [(pit_marker(x, y), width) for x, y, width in TRACE["pitGarageGates"]]
_west_gate, _east_gate = PIT_GARAGE_GATES
_gate_marking_clearance = 1.25
_stall_blocks = (
    (STALL_START, _west_gate[0]-_west_gate[1]*.5-_gate_marking_clearance, 2),
    (_west_gate[0]+_west_gate[1]*.5+_gate_marking_clearance,
     _east_gate[0]-_east_gate[1]*.5-_gate_marking_clearance, 33),
    (_east_gate[0]+_east_gate[1]*.5+_gate_marking_clearance, STALL_END, 5),
)
PIT_STALL_RANGES = tuple((lerp(a,b,i/n),lerp(a,b,(i+1)/n))
                        for a,b,n in _stall_blocks for i in range(n))


def pit_stall_pose(i):
    a,b = PIT_STALL_RANGES[i]
    return pit_pose((a+b)*.5)

def point_json(point):
    return dict(x=round(point[0], 8), z=round(point[1], 8))


def game_definition():
    points = [dict(point_json(p), banking=round(_control_banks[i], 7),
                   elevationMeters=round(_control_elevations[i], 8),
                   widthMeters=WIDTH, apronWidthMeters=round(_control_aprons[i], 7))
              for i, p in enumerate(_controls)]
    stalls = [point_json(pit_stall_pose(i).offset(side=-PIT_WIDTH * .33)) for i in range(STALL_COUNT)]
    return dict(
        id=13, name="North Wilkesboro Speedway", location=dict(city="North Wilkesboro", state="NC"),
        qualifyingReferenceMph=BENCHMARK_MPH, lengthMiles=.625, aspectRatio=2.04,
        majorStyle="Oval", minorStyle="Oval", surface="Asphalt", restrictorPlate=False,
        widthMeters=WIDTH, pitStalls=STALL_COUNT, NumberOfQualifyingLaps=2, raceLaps=450,
        raceStageLaps=[80,185,185], smoothLayoutBanking=True,
        banking=dict(turns12=13., turns34=13., frontStraight=3., backStraight=3.),
        layoutPoints=points,
        raceMarkers=dict(hasStartFinish=True, startFinish=0., hasRestartZone=True,
                         restartZoneStart=RESTART_START, restartZoneEnd=RESTART_END),
        pitRoad=dict(enabled=True, widthMeters=PIT_WIDTH, speedLimitMph=30,
                     speedLimitStart=PIT_SPEED_START, speedLimitEnd=PIT_SPEED_END,
                     points=[point_json(p) for p in _pit_controls],
                     entrance=[point_json(p) for p in _entry_controls],
                     exit=[point_json(p) for p in _exit_controls], stalls=stalls))


if __name__ == "__main__":
    output = FilePath(__file__).with_name("NorthWilkesboroSpeedway.json")
    output.write_text(json.dumps(game_definition(), indent=2) + "\n", encoding="utf-8")