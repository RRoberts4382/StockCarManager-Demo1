"""Build the Iowa Speedway track surfaces and shared driving data in Blender."""
import bisect
import json
import math
from math import comb
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector
from mathutils.geometry import delaunay_2d_cdt, tessellate_polygon

OUT = Path(__file__).resolve().parent
LAP = .875 * 1609.344
WIDTH = 60 * .3048
APRON = 20 * .3048
PIT_WIDTH = 10.5
WALL_HEIGHT = 1.067
PIT_STALL_WALLS = (('east', 506., 359., 25), ('west', 327., 239., 15))
PIT_WALKWAY_WIDTH = 3.048
PIT_WALKWAY_OFFSET = 14.30
RESPONSE_ROUTES = (
    ((245.,210.), (228.,190.), (220.,177.)),
    ((522.,211.), (531.,196.), (537.,188.), (547.,183.)),
    ((223.,326.), (240.,340.), (258.,365.)),
    ((490.,337.), (492.,350.), (498.,365.)),
)
METERS_PER_PIXEL = 1.2584012899481174
REFERENCE_SECONDS = 23.004
REFERENCE_MPH = .875 * 3600 / REFERENCE_SECONDS
REFERENCE = "https://www.nascar.com/news-media/2025/08/02/another-one-briscoe-captures-busch-light-pole-with-strong-iowa-showing/"

CONTROL_COUNT = 256
LAYOUT_SEGMENTS = 24
PIT_SEGMENTS = 16
STRAIGHT_BANK = 10.
BACK_BANK = 4.
TURN_BANK = 13.


def from_pixel(pixel):
    x, y = pixel
    return np.array(((x - 374.5) * METERS_PER_PIXEL, (222 - y) * METERS_PER_PIXEL))


def to_pixel(point):
    x, y = point
    return (374.5 + float(x) / METERS_PER_PIXEL, 222 - float(y) / METERS_PER_PIXEL)


def unit(point):
    return point / np.linalg.norm(point)


def cumulative(points):
    return np.r_[0., np.cumsum(np.linalg.norm(np.diff(points, axis=0), axis=1))]


def quintic(t):
    t = max(0., min(t, 1.))
    return t**3 * (10. + t * (-15. + 6. * t))


def smooth_array(t):
    t = np.minimum(t, 1.)
    return t*t*(3.-2.*t)


def mirror(points, center):
    result = points.copy()
    result[:, 0] = 2. * center - result[:, 0]
    return result


def construct_plan():
    """Fit the annotated red outline, with separate straight approaches to the tri-oval bend."""
    center, apex, feature_halfwidth, slope = 377.5, 136.7, 67., .203
    u = np.linspace(1., -1., 4001)
    x = center + feature_halfwidth * u
    y = apex + slope * feature_halfwidth * (.275*u*u + .35*u**4 - .95/6*u**6)
    feature = np.c_[x, y]

    front_left = np.array((249., feature[-1, 1] + (feature[-1, 0]-249.) * slope))
    back_left = np.array((277.5, 374.2))
    front_approach = np.array((feature[-1], front_left))
    back_right = mirror(back_left[None, :], center)[0]

    # Fit left-turn displacement by integrating a nonreversing curvature profile.
    # Cubic end ramps bring curvature to zero at the straight joins.
    t = np.linspace(0., 1., 16385)
    entry_exit_shape = smooth_array(t/.012) * smooth_array((1.-t)/.012)
    initial_heading = math.pi - math.atan(slope)

    def integrate(values):
        return np.r_[0., np.cumsum((values[:-1]+values[1:]) * .5)]

    def turn_with_skew(skew):
        curvature = entry_exit_shape * (1. + skew * (t - .5))
        rotation = integrate(curvature)
        heading = initial_heading * (1. - rotation / rotation[-1])
        offset = np.c_[integrate(np.cos(heading)), integrate(np.sin(heading))]
        return front_left + offset * ((back_left[1]-front_left[1]) / offset[-1, 1])

    low, high = -1., 1.
    for _ in range(36):
        middle = (low+high) * .5
        candidate = turn_with_skew(middle)
        if candidate[-1, 0] < back_left[0]:
            high = middle
        else:
            low = middle
    left = turn_with_skew((low+high)*.5)
    left[-1] = back_left
    right = mirror(left[::-1], center)
    return [feature, front_approach, left, np.array((back_left, back_right)), right,
            mirror(front_approach[::-1], center)]


def catmull(a, b, c, d, t):
    return .5 * ((2.*b)+(c-a)*t+(2.*a-5.*b+4.*c-d)*t*t+(-a+3.*b-3.*c+d)*t*t*t)


def sample_spline(points, segments, closed):
    samples = []
    for index in range(len(points) if closed else len(points)-1):
        b, c = points[index], points[(index+1) % len(points)]
        a = points[index-1] if index or closed else 2.*b-c
        d = points[(index+2) % len(points)] if index+2 < len(points) or closed else 2.*c-b
        samples.extend(catmull(a,b,c,d,j/segments) for j in range(segments))
    samples.append(points[0] if closed else points[-1])
    return np.asarray(samples)


def sample_polyline(points, distances, distance):
    index = min(len(points)-2, max(0, bisect.bisect_right(distances, distance)-1))
    fraction = (distance-distances[index]) / (distances[index+1]-distances[index])
    return points[index] + (points[index+1]-points[index]) * fraction


segments = construct_plan()
section_lengths = [cumulative(points)[-1] * METERS_PER_PIXEL for points in segments]
section_start = np.r_[0., np.cumsum(section_lengths)]
plan = np.concatenate([pts[:-1] for pts in segments] + [segments[-1][-1:]])
plan = np.array([from_pixel(pixel) for pixel in plan])
plan_distance = cumulative(plan)

# Spread curvature gently through the curve joins before Catmull interpolation.
# The symmetric tapered window preserves the central portions of the straights
# and prevents the spline from introducing countersteer at a sparse control knot.
window_radius=36.
window=np.linspace(-window_radius,window_radius,73)
window_weights=(1.+np.cos(np.pi*window/window_radius))**2
window_weights/=window_weights.sum()
controls=np.array([
    sum(sample_polyline(plan,plan_distance,(station+offset)%plan_distance[-1])*weight
        for offset,weight in zip(window,window_weights))
    for station in np.arange(CONTROL_COUNT)*plan_distance[-1]/CONTROL_COUNT
])

# Restore the documented lap length with a subpixel parallel offset. The common
# PDN site transform remains unchanged for scenery and authored pavement.
filtered_length=cumulative(sample_spline(controls,LAYOUT_SEGMENTS,True))[-1]
headings=np.array([unit(v) for v in np.roll(controls,-1,axis=0)-np.roll(controls,1,axis=0)])
outside=np.c_[headings[:,1],-headings[:,0]]
controls+=outside*((LAP-filtered_length)/(2.*math.pi))
controls = np.round(controls, 6)
layout = sample_spline(controls, LAYOUT_SEGMENTS, True)
layout_distance = cumulative(layout)
source_length = layout_distance[-1]
layout_distance *= LAP / source_length


def profile_station(plan_meters):
    sample = plan_meters / plan_distance[-1] * (len(layout)-1)
    return np.interp(sample, np.arange(len(layout_distance)), layout_distance)


# Keep published banking values in each core. Settled transition lengths are
# estimates; splitting the change between heading-independent lofts prevents steps.
left_start = profile_station(section_start[2])
left_end = profile_station(section_start[3])
right_start = profile_station(section_start[4])
right_end = profile_station(section_start[5])
profile = [
    (left_start-22., left_start+38., STRAIGHT_BANK, TURN_BANK, 0., 2.),
    (left_end-60., left_end+14., TURN_BANK, BACK_BANK, 2., 0.),
    (right_start-14., right_start+60., BACK_BANK, TURN_BANK, 0., 2.),
    (right_end-38., right_end+22., TURN_BANK, STRAIGHT_BANK, 2., 0.),
]


def angle_at(station, spread=False):
    station %= LAP
    value = profile[-1][5 if spread else 3]
    for start, end, start_value, finish_value, start_spread, end_spread in profile:
        if station < start:
            break
        a, b = (start_spread, end_spread) if spread else (start_value, finish_value)
        if station < end:
            return a + (b-a) * quintic((station-start)/(end-start))
        value = b
    return value


spread_controls = [angle_at(s, True) for s in layout_distance[::LAYOUT_SEGMENTS]]


def plan_at(station):
    s = station % LAP
    index = bisect.bisect_right(layout_distance, s)-1
    fraction = (s-layout_distance[index]) / (layout_distance[index+1]-layout_distance[index])
    count = len(layout)-1
    before = layout[(index+1) % count]-layout[(index-1) % count]
    after = layout[(index+2) % count]-layout[index]
    point = layout[index] + (layout[index+1]-layout[index])*fraction
    tangent = unit(unit(before)*(1.-fraction)+unit(after)*fraction)
    segment = (index % LAYOUT_SEGMENTS + fraction) / LAYOUT_SEGMENTS
    control = index // LAYOUT_SEGMENTS
    spread = spread_controls[control] + (spread_controls[control+1]-spread_controls[control])*segment
    return point, tangent, spread


def bank_chord(distance, bank, spread):
    half_change = math.radians(spread * distance / WIDTH * .5)
    span = distance if abs(half_change) < .0001 else distance*math.sin(half_change)/half_change
    angle = math.radians(bank - spread*.5) + half_change
    return span * math.cos(angle), span * math.sin(angle)


def surface(station, lane):
    mid, tangent, spread = plan_at(station)
    outside = np.array((tangent[1], -tangent[0]))
    bank = angle_at(station)
    mid_offset, _ = bank_chord(WIDTH*.5, bank, spread)
    inset = min(0., lane+WIDTH*.5)
    offset, height = bank_chord(max(0., lane+WIDTH*.5), bank, spread)
    xy = mid + outside * (offset - mid_offset + inset)
    return Vector((xy[0], xy[1], height))


def apron_at(station):
    return np.array(surface(station, -WIDTH*.5-APRON*.5).xy)


def apron_pose(station):
    delta=1.25
    before,point,after=(apron_at(s) for s in (station-delta,station,station+delta))
    velocity=(after-before)/(2.*delta)
    acceleration=(after+before-2.*point)/delta**2
    curvature=(velocity[0]*acceleration[1]-velocity[1]*acceleration[0])/np.linalg.norm(velocity)**3
    return point,unit(velocity),curvature


def segment_stations(a, b, spacing):
    return np.linspace(a, b, math.ceil((b-a)/spacing)+1)


def station_pixels(center):
    maximum_x = layout[:, 0]
    target = from_pixel(center)[0]
    indices = np.flatnonzero(layout_distance < profile_station(section_start[1]))
    index = indices[np.argmin(np.abs(maximum_x[indices]-target))]
    before = max(0,index-1)
    after = min(len(layout)-1,index+1)
    return np.interp(target, maximum_x[[after,before]], layout_distance[[after,before]])


# Plot trace is right to left along the frontstretch. The ends of the blue
# centerline connect to the native unbanked apron, with no elevated connector.
entry_station = 1230.5
exit_station = 431.
road_a = from_pixel((529., 187.))
road_b = from_pixel((236., 186.))
road_heading = unit(road_b-road_a)
pit_inward = np.array((-road_heading[1], road_heading[0]))


def connector_controls(start_pose, end_pose, start_reach, end_reach, density=.95):
    # Preserve both heading and curvature at the apron and pit-road joins.
    start,start_direction,start_curvature=start_pose
    end,end_direction,end_curvature=end_pose
    chord=np.linalg.norm(end-start)
    first_velocity=start_direction*(chord*start_reach)
    last_velocity=end_direction*(chord*end_reach)
    first_normal=np.array((-start_direction[1],start_direction[0]))
    last_normal=np.array((-end_direction[1],end_direction[0]))
    c1=start+first_velocity/5.
    c4=end-last_velocity/5.
    c2=first_normal*(start_curvature*np.dot(first_velocity,first_velocity)/20.)+2.*c1-start
    c3=last_normal*(end_curvature*np.dot(last_velocity,last_velocity)/20.)+2.*c4-end
    control=np.array((start,c1,c2,c3,c4,end))
    t=np.linspace(0.,1.,1001)
    coefficients=np.array([comb(5,i)*t**i*(1.-t)**(5-i) for i in range(6)])
    curves=coefficients.T @ control
    marks = cumulative(curves)
    points = np.array([sample_polyline(curves, marks, d)
                       for d in segment_stations(0.,marks[-1],density)])
    # Unity's endpoint extrapolation takes its tangent from the neighboring knot.
    points[1] = start + start_direction*np.linalg.norm(points[1]-start)
    points[-2] = end - end_direction*np.linalg.norm(end-points[-2])
    return np.round(points,6)


apron_entry,apron_exit=apron_pose(entry_station),apron_pose(exit_station)
entry_site,exit_site=apron_entry[0],apron_exit[0]
# Fitted against the pixel trace and curvature envelope. Both routes turn
# progressively toward the pit road/apron, and pass through the drawn openings.
entry = connector_controls(apron_entry,(road_a,road_heading,0.),.65,1.35)
exit_ = connector_controls((road_b,road_heading,0.),apron_exit,.75,.75)
through = np.array((road_a, road_b))
speed_line_margin=.5/np.linalg.norm(road_b-road_a)
pit_road_length = np.linalg.norm(road_b-road_a)
stall_end_margin = .22 / pit_road_length
stalls = []
for _, wall_start, wall_end, capacity in PIT_STALL_WALLS:
    first = (529.-wall_start)/(529.-236.) + stall_end_margin
    last = (529.-wall_end)/(529.-236.) - stall_end_margin
    for slot in range(capacity):
        fraction = first + (last-first)*(slot+.5)/capacity
        stalls.append(road_a+(road_b-road_a)*fraction+pit_inward*3.26)


def data_point(point):
    return dict(x=round(float(point[0]),6), z=round(float(point[1]),6))


timing_stripe = station_pixels((379.5,136.7))
# No surveyed restart marking appears in the supplied drawing. Estimate a short zone
# immediately before the checker on the frontstretch, in the actual CCW direction.
restart_start = timing_stripe - 47.
restart_end = timing_stripe - 11.
transitions = [dict(start=a/LAP,end=b/LAP,fromDegrees=c,toDegrees=d) for a,b,c,d,_,_ in profile]
definition = dict(
    id=35, name='Iowa Speedway', location=dict(city='Newton', state='IA'),
    lengthMiles=.875, aspectRatio=(layout[:,0].max()-layout[:,0].min())/(layout[:,1].max()-layout[:,1].min()),
    majorStyle='Oval', minorStyle='Tri-Oval', surface='Asphalt', restrictorPlate=False,
    banking=dict(turns12=TURN_BANK,turns34=TURN_BANK,frontStraight=STRAIGHT_BANK,backStraight=BACK_BANK),
    widthMeters=WIDTH,pitStalls=40,NumberOfQualifyingLaps=2,raceLaps=350,raceStageLaps=[70,140,140],
    qualifyingReferenceMph=REFERENCE_MPH,
    weights=dict(surfaceRoughness=.55,aerodynamics=.38,horsepower=.58,brakes=.75,
                 suspension=.85,transmission=.38,handling=.9,tireWear=.62),
    layoutPoints=[dict(data_point(point),banking=angle_at(s),bankingSpread=angle_at(s,True),
                       widthMeters=WIDTH,apronWidthMeters=APRON)
                  for point,s in zip(controls,layout_distance[::LAYOUT_SEGMENTS])],
    bankingTransitions=transitions,
    raceMarkers=dict(hasStartFinish=True,startFinish=timing_stripe/LAP,hasRestartZone=True,
                     restartZoneStart=(restart_start%LAP)/LAP,restartZoneEnd=(restart_end%LAP)/LAP),
    pitRoad=dict(enabled=True,widthMeters=PIT_WIDTH,speedLimitMph=40,
                 speedLimitStart=speed_line_margin,speedLimitEnd=1.-speed_line_margin,
                 points=list(map(data_point,through)),
                 entrance=list(map(data_point,entry)),exit=list(map(data_point,exit_)),
                 stalls=list(map(data_point,stalls))),
)


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version=0
scene=bpy.context.scene
scene.unit_settings.system='METRIC'
scene['track_id']=35
scene['layout_reference']='Iowa.pdn, Background and RV Parking layers. See archived track reference notes.'
scene['meters_per_pixel']=METERS_PER_PIXEL
scene['qualifying_reference_seconds']=REFERENCE_SECONDS
scene['qualifying_reference_mph']=REFERENCE_MPH
scene['qualifying_reference_source']=REFERENCE
scene['elevation_datum']='Level racing inside edge at apron height; estimated longitudinal datum.'
scene['banking_profile']='10-degree frontstretch, 4-degree backstretch, 12 to 14 progressive turns.'
scene['banking_transitions']=json.dumps(transitions)


def material(name, color):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color=(*color,1.)
    return mat


asphalt=material('Iowa asphalt',(.105,.112,.126))
concrete=material('Iowa wall concrete',(.54,.55,.55))
promenade_concrete=material('Iowa walkway concrete',(.48,.46,.43))
white=material('Iowa white paint',(.88,.88,.83))
black=material('Iowa black paint',(.028,.028,.030))
yellow=material('Iowa safety yellow',(.90,.64,.035))
red=material('Iowa restart red',(.67,.035,.035))


def mesh(name, vertices, faces, mat, pavement=False, smooth=False):
    data=bpy.data.meshes.new(name)
    if pavement:
        aligned=[]
        for face in faces:
            a,b,c=(Vector(vertices[index]) for index in face[:3])
            aligned.append(face if (b-a).cross(c-a).z>0. else tuple(reversed(face)))
        faces=aligned
    data.from_pydata(vertices,[],faces)
    data.update()
    ob=bpy.data.objects.new(name,data)
    scene.collection.objects.link(ob)
    ob.data.materials.append(mat)
    uv=data.uv_layers.new(name='Iowa planar UV')
    for loop in data.loops:
        point=data.vertices[loop.vertex_index].co
        uv.data[loop.index].uv=(point.x/8., point.y/8.)
    if smooth:
        for polygon in ob.data.polygons:
            polygon.use_smooth=True
    if pavement:
        data.calc_loop_triangles()
        for face in data.loop_triangles:
            assert face.area > 0. and face.normal.z > 0., (name,face.polygon_index)
    return ob


def ribbon(name, rows, mat, closed=False, smooth=False):
    width=len(rows[0])
    vertices=[point for row in rows for point in row]
    faces=[]
    for row in range(len(rows) if closed else len(rows)-1):
        current=row*width
        following=(row+1)%len(rows)*width
        for column in range(width-1):
            a,b=current+column,following+column
            faces.append((a,a+1,b+1,b))
    return mesh(name,vertices,faces,mat,pavement=True,smooth=smooth)


def wall(name, rows, height=WALL_HEIGHT, closed=False):
    vertical=Vector((0.,0.,height))
    vertices=[point for a,b in rows for point in (a,b,b+vertical,a+vertical)]
    faces=[]
    for i in range(len(rows) if closed else len(rows)-1):
        a,b=i*4,(i+1)%len(rows)*4
        for j in range(4):
            k=(j+1)%4
            faces.append((a+j,a+k,b+k,b+j))
    if not closed:
        faces.extend(((3,2,1,0),tuple(range(len(vertices)-4,len(vertices)))))
    a,b,c,_=faces[0]
    if (vertices[b]-vertices[a]).cross(vertices[c]-vertices[a]).z>0.:
        faces=[tuple(reversed(face)) for face in faces]
    return mesh(name,vertices,faces,concrete)


def flat(point, height=0.):
    return Vector((point[0],point[1],height))


def flat_polygon(name, pixels, height=-.003):
    points=[flat(from_pixel(pixel),height) for pixel in pixels]
    polygons=tessellate_polygon([points])
    if isinstance(polygons[0][0], int):
        faces=[tuple(polygon) for polygon in polygons]
    else:
        faces=[tuple(points.index(v) for v in polygon) for polygon in polygons]
    return mesh(name,points,faces,asphalt,pavement=True)


def piecewise_path(pixel_controls, density=.8):
    points=np.array([from_pixel(p) for p in pixel_controls])
    curve=sample_spline(points,20,False)
    marks=cumulative(curve)
    return np.array([sample_polyline(curve,marks,s) for s in segment_stations(0.,marks[-1],density)])


def ribbon_widths(name, points, half_width, mat, closed=False):
    rows=[]
    for index,point in enumerate(points):
        before=(index-1)%len(points) if closed else max(0,index-1)
        after=(index+1)%len(points) if closed else min(len(points)-1,index+1)
        direction=unit(points[after]-points[before])
        normal=np.array((-direction[1],direction[0]))
        rows.append([flat(point+normal*half_width),flat(point-normal*half_width)])
    return ribbon(name,rows,mat,closed=closed)


def border_links(polygons):
    """Find directed boundary edges of upward-facing polygons."""
    edges=set()
    for face in polygons:
        for a,b in zip(face,face[1:]+face[:1]):
            if (b,a) in edges:
                edges.remove((b,a))
            else:
                edges.add((a,b))
    return edges


def surface_loops(obj):
    links=dict(border_links([tuple(face.vertices) for face in obj.data.polygons]))
    while links:
        first=min(links)
        trace=[first]
        next_index=links.pop(first)
        while next_index != first:
            trace.append(next_index)
            next_index=links.pop(next_index)
        yield [obj.data.vertices[i].co.xy for i in trace]


def level_asphalt(objects, racing_inner_edge):
    """Form one flat paving footprint, with conforming faces assigned to the named regions."""
    positions=[]
    faces=[]
    owners=[]

    def clockwise(points):
        return sum(a.cross(b) for a,b in zip(points,points[1:]+points[:1])) < 0.

    def add_polygon(points, owner, is_hole=False):
        indices=tuple(range(len(positions),len(positions)+len(points)))
        positions.extend(points)
        faces.append(indices[::-1] if clockwise(points) else indices)
        owners.append((owner,is_hole))

    def triangulate():
        coords,_,triangles,_,_,orig_faces=delaunay_2d_cdt(positions,[],faces,1,.00015)
        regions=[]
        for indices,orig in zip(triangles,orig_faces):
            if 0 not in orig:
                continue
            holes={owners[i][0] for i in orig if owners[i][1]}
            covering=[i for i in orig if i and not owners[i][1] and owners[i][0] not in holes]
            if not covering:
                continue
            owner=owners[min(covering)][0]
            a,b,c=(coords[i] for i in indices)
            signed_area=(b-a).cross(c-a)
            assert signed_area != 0., owner
            regions.append((tuple(indices if signed_area > 0. else reversed(indices)),owner))
        return coords,regions

    # Crop any flat road that touches the racing edge to the exact shared boundary.
    # The first polygon supplies the clipping mask. Every other face supplies asphalt.
    add_polygon([point.xy for point in racing_inner_edge],None)
    for obj in objects:
        # Constrain the actual shape boundaries. Crossing internal tessellation
        # edges can create slivers smaller than Unity's imported vertex precision.
        for loop in surface_loops(obj):
            add_polygon(loop,obj.name,clockwise(loop))

    coords,regions=triangulate()
    borders=border_links([indices for indices,_ in regions])
    following=dict(borders)
    previous={b:a for a,b in borders}

    def along_boundary(index, direction, length):
        point=coords[index]
        while length > 0.:
            index=direction[index]
            neighbor=coords[index]
            reach=(point-neighbor).length
            if reach > length:
                return point.lerp(neighbor,length/reach)
            point=neighbor
            length-=reach
        return point

    # Round the reentrant grass corners at the road and apron junctions.
    # These sites are local to the intended merge footprints in the supplied layout.
    merge_corners=(
        ((407.,173.5),4.0,2.6,'Iowa infield loop access'),
        ((412.,168.),4.0,2.6,'Iowa infield loop access'),
        ((538.,185.),6.5,2.4,'Iowa infield loop access'),
        ((553.,195.),9.0,2.5,'Iowa apron to pit entrance'),
        ((175.,242.),10.0,2.5,'Iowa pit exit to apron'),
    )
    rounded=set()
    for pixel,search,reach,owner in merge_corners:
        target=Vector(from_pixel(pixel))
        candidates=[]
        for index in following.keys() & previous.keys():
            corner=coords[index]
            distance=(corner-target).length
            if distance > search or index in rounded:
                continue
            incoming=(corner-coords[previous[index]]).normalized()
            outgoing=(coords[following[index]]-corner).normalized()
            if incoming.cross(outgoing) < -.25:
                candidates.append((distance,index))
        if not candidates:
            continue
        _,index=min(candidates)
        rounded.add(index)
        center=coords[index]
        start=along_boundary(index,previous,reach)
        finish=along_boundary(index,following,reach)
        arc=[start*(1.-t)**2+center*2.*t*(1.-t)+finish*t*t
             for t in np.linspace(0.,1.,12)]
        add_polygon(arc+[center],owner)

    coords,regions=triangulate()
    by_owner={obj.name:[] for obj in objects}
    for indices,owner in regions:
        by_owner[owner].append(indices)
    for old in objects:
        name=old.name
        triangles=by_owner[name]
        active=sorted({i for face in triangles for i in face})
        remap={index:i for i,index in enumerate(active)}
        pavement_vertices=[flat(coords[i]) for i in active]
        pavement_faces=[tuple(remap[i] for i in tri) for tri in triangles]
        old_mesh=old.data
        region_material=old_mesh.materials[0]
        bpy.data.objects.remove(old,do_unlink=True)
        bpy.data.meshes.remove(old_mesh)
        mesh(name,pavement_vertices,pavement_faces,region_material,pavement=True)


ds=segment_stations(0.,LAP,.54)[:-1]
lanes=np.linspace(-WIDTH*.5,WIDTH*.5,9)
racing_rows=[[surface(s,lane) for lane in lanes] for s in ds]
racing=ribbon('Iowa racing surface',racing_rows,asphalt,closed=True,smooth=True)
racing['layout_fractions']=(ds/LAP).tolist()
# Across-track coordinates follow the same lane ordering as the Unity sampler.
for polygon in racing.data.polygons:
    for loop in polygon.loop_indices:
        row,lane=divmod(racing.data.loops[loop].vertex_index,len(lanes))
        station=1. if row==0 and polygon.index//(len(lanes)-1)==len(ds)-1 else ds[row]/LAP
        racing.data.uv_layers.active.data[loop].uv=(station,lane/(len(lanes)-1))
apron_rows=[[row[0],surface(s,-WIDTH*.5-APRON)] for row,s in zip(racing_rows,ds)]
flat_surfaces=[ribbon('Iowa flat apron',apron_rows,asphalt,closed=True)]
wall_rows=[]
for s,row in zip(ds,racing_rows):
    forward=plan_at(s)[1]
    outward=Vector((forward[1],-forward[0],0.))
    wall_rows.append((row[-1],row[-1]+outward*.34))
wall('Iowa continuous outside concrete wall',wall_rows,closed=True)

# Loft the connector widths into and out of the shared level asphalt.
def pit_rows(points, kind):
    curve=sample_spline(points,PIT_SEGMENTS,False)
    lengths=cumulative(curve)
    # The service lane is exactly straight. End rows define its footprint
    # without collinear constraints that can create subpixel triangles.
    samples=(0.,lengths[-1]) if kind=='road' else segment_stations(0.,lengths[-1],.44)
    rows=[]
    for s in samples:
        point=sample_polyline(curve,lengths,s)
        delta=.1
        a=sample_polyline(curve,lengths,max(0.,s-delta))
        b=sample_polyline(curve,lengths,min(lengths[-1],s+delta))
        direction=unit(b-a)
        normal=np.array((-direction[1],direction[0]))
        t=s/lengths[-1] if kind=='entry' else 1.-s/lengths[-1]
        ramp=quintic(t)
        width=PIT_WIDTH*.5 if kind=='road' else 2.+(PIT_WIDTH*.5-2.)*ramp
        rows.append([flat(point+normal*width),flat(point-normal*width)])
    return rows


entrance_rows=pit_rows(entry,'entry')
road_rows=pit_rows(through,'road')
exit_rows=pit_rows(exit_,'exit')
entrance_rows[-1]=road_rows[0]
exit_rows[0]=road_rows[-1]
flat_surfaces.extend((
    ribbon('Iowa apron to pit entrance',entrance_rows,asphalt),
    ribbon('Iowa pit road',road_rows,asphalt),
    ribbon('Iowa pit exit to apron',exit_rows,asphalt),
))


def wall_straight(name, x0, x1, side):
    # The behind-pit wall preserves both drawn gaps in the west and east of the infield.
    a=(529.-x0)/(529.-236.)
    b=(529.-x1)/(529.-236.)
    rows=[]
    for fraction in (a,b):
        base=road_a+(road_b-road_a)*fraction+pit_inward*side*(PIT_WIDTH*.5+.035)
        rows.append((flat(base),flat(base+pit_inward*side*.31)))
    wall(name,rows,height=.91)


wall_straight('Iowa trackside pit wall',529.,236.,-1.)
for name, wall_start, wall_end, _ in reversed(PIT_STALL_WALLS):
    wall_straight(f'Iowa infield pit wall {name}',wall_start,wall_end,1.)

# Paved garage yards and infield roads are traced from the supplied gray regions.
flat_surfaces.append(flat_polygon('Iowa garage yards and pit service asphalt',
              ((230,189.9),(524,190.95),(526,204),(518,214),(482,216),(374,214),
               (365,229),(365,350),(342,354),(286,348),(250,341),(218,326),
               (220,288),(224,241),(227,212)),height=0.))
for name,points,width in (
    ('Iowa east lot access',((511,193),(522,205),(506,223),(482,233),(442,251),
                            (413,262),(398,277),(393,298),(400,319),(418,330),(455,326),(480,313)),5.1),
    ('Iowa east return drive',((472,319),(483,302),(484,281),(504,267),(534,254),
                              (550,260),(556,278),(544,299),(527,319),(507,339),(482,356)),4.8),
    ('Iowa infield south spur',((461,356),(481,356),(507,356),(525,348),(544,329)),6.0),
):
    flat_surfaces.append(ribbon_widths(name,piecewise_path(points),width,asphalt))

loop_angle=np.linspace(0.,2.*math.pi,80,endpoint=False)
front_loop=np.array([from_pixel((379.5+32.*math.cos(theta),161.+14.5*math.sin(theta)))
                     for theta in loop_angle])
flat_surfaces.append(ribbon_widths('Iowa frontstretch infield loop road',front_loop,3.7,asphalt,closed=True))
flat_surfaces.append(ribbon_widths('Iowa infield loop access',
                     piecewise_path(((403,170.9),(411,170.7),(438,174),(470,176),(511,174),(531,178),(537,186))),
                     3.7,asphalt))

# A continuous pedestrian strip stays level wherever it crosses an access road.
walk_edges=(PIT_WALKWAY_OFFSET-PIT_WALKWAY_WIDTH*.5,PIT_WALKWAY_OFFSET+PIT_WALKWAY_WIDTH*.5)
flat_surfaces.insert(4,ribbon('Iowa pit walkway',
    [[flat(end+pit_inward*edge) for edge in walk_edges] for end in through],promenade_concrete))

for index,pixels in enumerate(RESPONSE_ROUTES,1):
    path=piecewise_path(pixels)
    launch=unit(from_pixel(pixels[1])-from_pixel(pixels[0]))
    # The wide portion holds both vehicles beside a clear exit aisle.
    stations=cumulative(path)
    rows=[]
    for station,point,direction in [(-4.6,path[0]-launch*4.6,launch), *(
            (station,point,unit(path[min(i+1,len(path)-1)]-path[max(i-1,0)]))
            for i,(station,point) in enumerate(zip(stations,path)))]:
        across=np.array((-direction[1],direction[0]))
        f=min(1.,max(0.,(station-3.55)/8.5))
        half_width=7.45-(7.45-3.05)*f*f*(3.-2.*f)
        rows.append([flat(point+across*half_width),flat(point-across*half_width)])
    flat_surfaces.insert(5,ribbon(f'Iowa emergency access {index}',rows,asphalt))
level_asphalt(flat_surfaces,[row[0] for row in racing_rows])

for name,points in (
    ('Iowa curved left infield concrete wall',((227.,196.),(213.,202.),(194.,218.),
          (184.,241.),(183.,268.),(187.,292.),(202.,318.),(218.,333.),(237.,344.))),
    ('Iowa low south infield concrete wall',((243.,336.),(270.,345.),(300.,351.),
          (339.,356.),(381.,357.),(429.,357.),(475.,357.))),
    ('Iowa right infield concrete wall',((513.,349.),(543.,328.),(564.,303.),
          (579.,273.),(579.,245.),(566.,221.),(537.,198.))),
):
    positions=piecewise_path(points)
    wall_rows=[]
    for index,point in enumerate(positions):
        direction=unit(positions[min(len(positions)-1,index+1)]-positions[max(0,index-1)])
        normal=np.array((-direction[1],direction[0]))
        wall_rows.append((flat(point+normal*.13),flat(point-normal*.13)))
    wall(name,wall_rows,height=.87)


def paint(name, rows, mat=white, closed=False):
    return ribbon(name,rows,mat,closed=closed,smooth=closed)


for lane in (-WIDTH*.5+.16,WIDTH*.5-.16):
    paint('Iowa white racing edge',[[surface(s,k)+Vector((0,0,.012)) for k in (lane-.057,lane+.057)] for s in ds],closed=True)

for checker_mat in (white,black):
    vertices=[]
    faces=[]
    for row in range(2):
        for column in range(30):
            if (row+column)%2 != (checker_mat==black):
                continue
            a=-WIDTH*.5+column*WIDTH/30.
            b=a+WIDTH/30.
            first=timing_stripe+(row-1)*.62
            second=first+.62
            rect=[surface(first,a),surface(first,b),surface(second,b),surface(second,a)]
            faces.append(tuple(range(len(vertices),len(vertices)+4)))
            vertices.extend(point+Vector((0.,0.,.011)) for point in rect)
    suffix='white' if checker_mat==white else 'black'
    mesh('Start finish checker '+suffix,vertices,faces,checker_mat,pavement=True)

paint('Iowa restart zone band',
      [[surface(s,lane)+Vector((0,0,.017)) for lane in (WIDTH*.5-.36,WIDTH*.5-1.05)]
       for s in segment_stations(restart_start,restart_end,.55)],mat=red)
for number,s in enumerate((restart_start,restart_end),1):
    paint('Iowa restart endpoint '+str(number),
          [[surface(v,lane)+Vector((0,0,.022)) for lane in (WIDTH*.5-.35,WIDTH*.5-2.25)]
           for v in (s-.19,s+.19)],mat=yellow)

for stall_index,position in enumerate(stalls,1):
    stripe_center=position-road_heading*3.46
    paint(f'Iowa pit stall stripe {stall_index:02}',
          [[flat(stripe_center+road_heading*along+pit_inward*side,.013) for side in (-1.52,1.52)]
           for along in (-.045,.045)],mat=white)
for label,point in (('entry',road_a+road_heading*.5),('exit',road_b-road_heading*.5)):
    paint('Iowa pit speed '+label,
          [[flat(point+road_heading*along+pit_inward*side,.013) for side in (-PIT_WIDTH*.5,PIT_WIDTH*.5)]
           for along in (-.095,.095)],mat=yellow)


def camera(name,position,aim,scale=None):
    data=bpy.data.cameras.new(name)
    ob=bpy.data.objects.new(name,data)
    scene.collection.objects.link(ob)
    ob.location=position
    ob.rotation_euler=(Vector(aim)-ob.location).to_track_quat('-Z','Y').to_euler()
    if scale is not None:
        data.type='ORTHO'
        data.ortho_scale=scale
    else:
        data.lens=39
    data.clip_end=3000.
    return ob


top=camera('Overhead',(4.,-42.,800.),(4.,-42.,0.),632.)
top.rotation_euler=(0.,0.,0.)
oblique=camera('Oblique',(-310.,445.,495.),(4.,-32.,0.),645.)
low=camera('Turn 1 approach',surface(left_start-26.,5.)+Vector((0.,0.,4.5)),
           surface(left_start+60.,0.)+Vector((0.,0.,.2)))
scene.render.engine='BLENDER_WORKBENCH'
scene.display.shading.light='STUDIO'
scene.display.shading.color_type='MATERIAL'
scene.display.shading.show_backface_culling=True
scene.display.shading.show_cavity=True
scene.display.shading.cavity_type='BOTH'
scene.world=bpy.data.worlds.new('Iowa driving surface preview')
scene.display.shading.background_type='WORLD'
scene.world.color=(.07,.10,.075)
scene.render.resolution_x=1800
scene.render.resolution_y=1080
scene.render.resolution_percentage=100
scene.camera=top

(OUT/'35.json').write_text(json.dumps(definition,indent=2)+'\n',encoding='utf-8')
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'IowaSpeedway.blend'))
bpy.ops.object.select_all(action='DESELECT')
for obj in scene.objects:
    obj.select_set(obj.type=='MESH')
bpy.ops.export_scene.fbx(filepath=str(OUT/'IowaSpeedway.fbx'),use_selection=True,
                         object_types={'MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,
                         add_leaf_bones=False,bake_anim=False,mesh_smooth_type='FACE')
bpy.ops.wm.open_mainfile(filepath=str(OUT/'IowaSpeedway.blend'))
scene=bpy.context.scene
for name,path in (('Overhead','Iowa_Overhead.png'),('Oblique','Iowa_Oblique.png'),('Turn 1 approach','Iowa_Turn1.png')):
    scene.camera=bpy.data.objects[name]
    scene.render.filepath=str(OUT/path)
    bpy.ops.render.render(write_still=True)

print('Iowa build',json.dumps(dict(sourceLapMeters=source_length, scale=LAP/source_length,
                 sectionMeters=[profile_station(s) for s in section_start],
                 stripe=timing_stripe, entryStation=entry_station, exitStation=exit_station,
                 entryPixels=to_pixel(entry_site), exitPixels=to_pixel(exit_site),
                 restartMeters=[restart_start%LAP,restart_end%LAP], transitions=transitions)))
