"""Editable venue scenery and Unity anchors for the Richmond Raceway rebuild.

The course diagram controls the driving surfaces. Venue structures and parking
are approximations guided by the official Richmond venue descriptions.
"""
import math
from functools import partial
from pathlib import Path
import sys
import random

import bpy
from mathutils import Vector

import richmond_layout as layout


# Use the shared components from the workspace root or the installed ArtSource.
for ancestor in Path(__file__).resolve().parents:
    library = ancestor / "VenueComponents"
    if (library / "venue_geometry.py").is_file():
        sys.path.insert(0, str(library))
        break
from venue_geometry import CAR_PAINT, CAR_PROFILES, Builder as MeshBuilder, floodlight, local, marker as venue_marker, parking_vehicle, park_car

PERIMETER_WALK_INNER = 4.25
PERIMETER_WALK_OUTER = 9.0
PERIMETER_WALK_Z = .023
GRANDSTAND_FRONTAGE = (-248., 268.)
CONCOURSE_OUTER_EDGE = 61.
HAULER_STALL_COUNT = 40
HAULER_STALL_PITCH = 4.8
HAULER_STALL_CENTERLINE = 110.55
RECOVERY_YARD_Z = .034
SUPPORT_PARKING_COUNT = 26
SUPPORT_PARKING_PITCH = 3.25
SUPPORT_PARKING_FIRST_X = -94.5
SUPPORT_PARKING_Y = 97.05


COLORS = {
    "Paddock concrete": (0.45, 0.44, 0.41),
    "Paddock asphalt": (0.13, 0.132, 0.126),
    "Parking paint": (0.75, 0.73, 0.64),
    "Paddock masonry": (0.53, 0.46, 0.41),
    "Light standing seam": (0.62, 0.63, 0.60),
    "Metal roof": (0.31, 0.36, 0.39),
    "Charcoal panels": (0.13, 0.17, 0.20),
    "Smoked glazing": (0.045, 0.095, 0.12),
    "Blue trim": (0.025, 0.21, 0.43),
    "Stair nosing": (0.88, 0.51, 0.075),
    "Section concrete": (0.39, 0.36, 0.32),
    "Bench aluminium": (0.61, 0.65, 0.65),
    "Seat denim": (0.045, 0.09, 0.15),
    "Seat garnet": (0.29, 0.078, 0.060),
    "Fixture steel": (0.22, 0.25, 0.28),
    "Fixture lens": (0.74, 0.80, 0.80),
    "Tires": (0.022, 0.024, 0.025),
    "RV ivory": (0.65, 0.60, 0.50),
    "Parked navy": (0.030, 0.10, 0.21),
    "Parked red": (0.42, 0.036, 0.024),
    "Parked sage": (0.14, 0.2, 0.17),
    "Parked silver": (0.36, 0.41, 0.44),
    "Parked graphite": (.095, .099, .105),
    "Parked bronze": (.30, .17, .09),
    "Parked ice": (.58, .62, .64),
    "Parked cobalt": (.036, .18, .39),
    "circuit asphalt": (.092, .089, .086),
    "Concession red": (.68, .075, .035),
    "Restroom plastic": (.042, .29, .37),
    "Waste barrel": (.09, .24, .14),
}


# Length, width, belt height, roof height, and cabin profile from tail to nose.


def material(name):
    name = "Richmond " + name
    existing = bpy.data.materials.get(name)
    if existing:
        return existing
    result = bpy.data.materials.new(name)
    color = COLORS[name.removeprefix("Richmond ")]
    result.diffuse_color = (*color, 1)
    result.use_nodes = True
    shader = result.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = (*color, 1)
    shader.inputs["Roughness"].default_value = .27 if "glazing" in name else .77
    return result


Builder = partial(MeshBuilder, material_for=material)
marker = partial(venue_marker, "Richmond locator")


def section(name):
    target = bpy.data.collections.new("Richmond | " + name)
    bpy.context.scene.collection.children.link(target)
    return target


def building(target, title, center, width, depth, height, yaw, roof, stalls=0):
    model = Builder(target)
    model.box(center, .014, width+13, depth+13, .018, yaw, "Paddock concrete")
    model.box(center, .032, width, depth, height, yaw, "Paddock masonry")
    top = height+.032
    model.box(center, top-.08, width+.65, depth+.65, .28, yaw, roof)
    for side in (-1, 1):
        model.box(local(center, yaw, 0, side*depth*.49), top+.20, width+.70, .28, .46, yaw, "Charcoal panels")
        model.box(local(center, yaw, side*width*.49, 0), top+.20, .28, depth+.60, .46, yaw, "Charcoal panels")
        model.box(local(center, yaw, 0, side*(depth/2+.025)), height-.76,
                  width-.65, .10, .30, yaw, "Blue trim")
        count = stalls or max(3, int(width/12))
        span = (width-.85)/count
        for i in range(count):
            across = (i+.5-count/2)*span
            panel = local(center, yaw, across, side*(depth/2+.05))
            if stalls:
                model.box(panel, .045, span*.84, .12, 3.02, yaw, "Charcoal panels")
                model.box(local(center, yaw, across, side*(depth/2+.116)), .21, span*.69, .04, 2.62,
                          yaw, "Light standing seam")
                for stripe in (.58, 1.23, 1.88, 2.53):
                    model.box(local(center, yaw, across, side*(depth/2+.143)), stripe, span*.67, .016, .026,
                              yaw, "Metal roof")
            else:
                window_z = min(2.5, height*.46)
                model.box(panel, window_z, span*.67, .14, height*.20, yaw, "Smoked glazing")
                model.box(local(center, yaw, across, side*(depth/2+.38)), window_z+height*.20,
                          span*.78, .85, .14, yaw, "Metal roof")
    units = max(2, int(width/20))
    for i in range(units):
        offset = (i+.5-units/2)*(width*.84/units)
        model.box(local(center, yaw, offset, depth*.16), top+.18, 2.4, 1.6, .67, yaw, "Light standing seam")
        model.box(local(center, yaw, offset, depth*.16), top+.85, 2.1, 1.45, .08, yaw, "Fixture steel")
    model.bake("Richmond " + title)


def parking_lot(target, title, center, yaw, columns, rows, rv, seed):
    rng = random.Random(seed)
    paint, vehicles = Builder(target), Builder(target)
    bay = 7.0 if rv else 3.25
    pitch = 25.0 if rv else 14.1
    length = 12.0 if rv else 5.4
    paint.box(center, .014, columns*bay+11, rows*pitch+7, .018, yaw, "Paddock asphalt")
    colors = ("Parked navy", "Parked red", "Parked sage", "Parked silver", "RV ivory")
    for row in range(rows):
        y = (row+.5-rows/2)*pitch
        for i in range(columns+1):
            x = (i-columns/2)*bay
            paint.box(local(center,yaw,x,y), .036, .115, length, .005, yaw, "Parking paint")
        for i in range(columns):
            x = (i+.5-columns/2)*bay
            if not rv:
                park_car(vehicles,local(center,yaw,x,y),yaw,rng)
            elif rng.random() <= .77:
                xy = local(center, yaw, x+rng.uniform(-.26,.26), y+rng.uniform(-.52,.52))
                heading = yaw+math.pi*.5+rng.uniform(-.045,.045)+(math.pi if (row+i)%3 else 0)
                parking_vehicle(vehicles, xy, heading, True, rng.choice(colors))
    paint.bake("Richmond " + title + " paving and spaces")
    vehicles.bake("Richmond " + title + " parked vehicles")


def exterior_point(station, offset):
    pose = layout.race_pose(station)
    x, y, _ = layout.racing_surface(station, pose.width*.5)
    return x+pose.nx*offset, y+pose.ny*offset


def exterior_walkway(target):
    perimeter, concourse = Builder(target), Builder(target)
    start, end = GRANDSTAND_FRONTAGE

    def stations(first, last):
        count = math.ceil((last-first)*2)
        return [layout.lerp(first,last,i/count) for i in range(count+1)]

    def flat(station, offset):
        return (*exterior_point(station, offset), PERIMETER_WALK_Z)

    frontage = stations(start,end)
    full_lap = frontage + stations(end,start+layout.LAP)[1:]
    perimeter.ribbon([[flat(s,PERIMETER_WALK_INNER),flat(s,PERIMETER_WALK_OUTER)]
                       for s in full_lap], "Paddock concrete")
    perimeter.bake("Richmond concrete perimeter walkway")

    # The inner edge starts underneath the 0.55 m wall, beyond its track face.
    # Match the original embankment's crest and follow its outward profile to
    # the flat perimeter walk. The exposed shoulder is concrete too.
    def shoulder(station, offset):
        pose = layout.race_pose(station)
        edge_z = layout.racing_surface(station, pose.width*.5)[2]
        t = min(1.,max(0.,(offset-.57)/(4.2-.57)))
        z = layout.lerp(edge_z+.009,PERIMETER_WALK_Z,t)
        return (*exterior_point(station,offset),z)

    for near, far in ((.46,.57),(.57,4.2),(4.2,PERIMETER_WALK_INNER)):
        concourse.ribbon([[shoulder(s,near),shoulder(s,far)] for s in frontage],
                          "Paddock concrete")

    # Follow the full footprint of the stand run, including the gap at the
    # flag stand. The ends taper across the short margins beyond the stands.
    def outer_width(station):
        blend = min(1.,(station-start)/8.,(end-station)/8.)
        return layout.lerp(PERIMETER_WALK_OUTER+1.5,CONCOURSE_OUTER_EDGE,blend)

    concourse.ribbon([[flat(s,PERIMETER_WALK_OUTER),flat(s,outer_width(s))]
                      for s in frontage], "Paddock concrete")
    concourse.bake("Richmond frontstretch concrete concourse")


def trackside_camp(target, title, station, columns, seed):
    rng = random.Random(seed)
    paving, vehicles = Builder(target), Builder(target)
    first = station-(columns*7)/2
    padding = 4.
    length = columns*7+2*padding
    samples = math.ceil(length)
    paving.ribbon([
        [(*exterior_point(first-padding+length*i/samples, PERIMETER_WALK_OUTER+offset), .018)
         for offset in (-.08, 43.)]
        for i in range(samples+1)
    ], "Paddock asphalt")

    colors = ("Parked navy", "Parked red", "Parked sage", "Parked silver", "RV ivory")
    row_depths = (7.9, 30.5)
    for i in range(columns+1):
        distance = first+i*7
        pose = layout.race_pose(distance)
        yaw = math.atan2(pose.ty, pose.tx)
        for depth in row_depths:
            xy = exterior_point(distance, PERIMETER_WALK_OUTER+depth)
            paving.box(xy,.019,.11,11,.005,yaw,"Parking paint")

    for i in range(columns):
        distance = first+(i+.5)*7+rng.uniform(-.22,.22)
        pose = layout.race_pose(distance)
        yaw = math.atan2(pose.ny, pose.nx)
        for row, depth in enumerate(row_depths):
            if rng.random() > .83:
                continue
            xy = exterior_point(distance, PERIMETER_WALK_OUTER+depth+rng.uniform(-.24,.24))
            heading = yaw+rng.uniform(-.035,.035)+(math.pi if (row+i)%3 else 0)
            parking_vehicle(vehicles, xy, heading, True, rng.choice(colors))
    paving.bake("Richmond " + title + " paving and spaces")
    vehicles.bake("Richmond " + title + " parked vehicles")


def grandstand(target, locators, num, position, span, rows, track_edge_height, concourse=False):
    x, y, yaw = position
    center = (x,y)
    bench = Builder(target)
    row_pitch = 1.12
    depth = row_pitch*rows
    bench.box(local(center,yaw,0,2.5), .015, span+4, depth+18, .11, yaw, "Paddock concrete")
    seats = "Seat denim" if num < 13 else "Seat garnet"
    # Place both seating decks relative to the adjoining banked track edge.
    # Keep the pads and lower concrete rakers rooted in the level concourse.
    deck_lift = track_edge_height + 1.0
    lower_start, lower_rise = 2.3 + deck_lift, .74
    upper_start, upper_rise = 12.75 + deck_lift, .98
    upper_setback = 7.5
    if num in (5, 6):
        # This section is behind the flag stand when viewed from the approach.
        lower_start += 1.25
        upper_start += .60

    def seating(start, rise, setback, first_row, upper=False):
        for row in range(rows):
            across = setback+(row+.5)*row_pitch-depth/2
            tread = start+rise*row
            base = tread-.55 if upper else .13
            for column in (-1,1):
                usable = span*.5-1.8
                bench.box(local(center,yaw,column*(span*.25+.32),across),base,
                          span*.5-.64,row_pitch+.02,tread-base,yaw,"Section concrete")
                row_xy = local(center,yaw,column*(span*.25+.39),across+.17)
                bench.box(row_xy,tread,usable,.37,.21,yaw,"Bench aluminium")
                bench.box(local(row_xy,yaw,0,.22),tread+.2,usable,.09,.38,yaw,seats)
                marker(locators, f"Richmond grandstand bench {num:03d}-{first_row+row:02d}-{column+1}",
                       (*row_xy,tread+.26), yaw, usable)
            # The center aisle steps independently of the deeper seating platforms.
            steps = math.ceil(rise/.20)
            for step in range(steps):
                step_depth = row_pitch/steps
                y = across+(step+.5)*step_depth-row_pitch*.5
                step_top = tread-rise*(1-(step+1)/steps)
                bench.box(local(center,yaw,0,y),step_top-.24,1.29,step_depth+.005,.24,yaw,"Section concrete")
                bench.box(local(center,yaw,0,y-step_depth*.5+.025),step_top,
                          1.18,.055,.012,yaw,"Stair nosing")

    seating(lower_start, lower_rise, 0, 0)
    seating(upper_start, upper_rise, upper_setback, rows, upper=True)

    # Upper deck loads run through side frames and towers behind the lower seats.
    # Ground-level circulation passes between the supports on the concourse.
    span_edge = span * .5 + .37
    front_edge = upper_setback - depth*.5
    rear_edge = upper_setback + depth*.5
    rear_height = upper_start+upper_rise*(rows-1)
    for sign in (-1, 1):
        side = sign*span_edge
        front_frame = local(center,yaw,side,front_edge+1.2)
        rear_frame = local(center,yaw,side,rear_edge+1.8)
        bench.box(front_frame,.13,.66,.73,upper_start-.07,yaw,"Fixture steel")
        bench.box(rear_frame,.13,1.45,1.7,rear_height+.93,yaw,"Section concrete")
        bench.brace((*front_frame,upper_start-.65),(*rear_frame,8.2+deck_lift),.39,"Fixture steel")
        bench.brace((*front_frame,7.9+deck_lift),(*rear_frame,rear_height-.48),.39,"Fixture steel")
        bench.brace((*local(center,yaw,side,front_edge+.1),upper_start-.72),
                    (*local(center,yaw,side,rear_edge),rear_height-.72),.50,"Fixture steel")
        bench.brace((*local(center,yaw,side,front_edge+.1),upper_start+1.05),
                    (*local(center,yaw,side,rear_edge),rear_height+1.05),.075,"Bench aluminium")
        for row in range(0, rows, 2):
            across = upper_setback+(row+.5)*row_pitch-depth/2
            bench.box(local(center,yaw,side,across),upper_start+upper_rise*row-.36,.08,.08,1.49,yaw,"Bench aluminium")

    for y_edge, deck in ((front_edge+.04, upper_start), (rear_edge+1.45, rear_height)):
        for rail in (.53, 1.08):
            bench.box(local(center,yaw,0,y_edge),deck+rail,span+.55,.085,.085,yaw,"Bench aluminium")
        for post in range(9):
            x = (post/8-.5)*(span+.44)
            bench.box(local(center,yaw,x,y_edge),deck,.075,.075,1.17,yaw,"Bench aluminium")
    bench.box(local(center,yaw,0,rear_edge+.74),rear_height-.47,span+1.16,1.55,.48,yaw,"Section concrete")
    bench.box(local(center,yaw,0,front_edge+.4),upper_start-.98,span+1.16,.7,.5,yaw,"Fixture steel")

    for sign in (-1,1):
        rear = local(center, yaw, sign*(span*.48), depth/2+.45)
        bench.box(rear, .13, .66, .70, lower_start+lower_rise*(rows-1)+.8, yaw, "Fixture steel")
    if concourse:
        rear = local(center, yaw, 0, rear_edge+6.5)
        base = rear_height
        for sign in (-1,1):
            bench.box(local(rear,yaw,span*.32*sign,0), .13,.72,1.55,base+1.08,yaw,"Section concrete")
        bench.box(rear,base+.90,span*.80,7.8,.65,yaw,"Paddock concrete")
        bench.box(rear,base+1.55,span*.76,6.6,3.03,yaw,"Charcoal panels")
        bench.box(local(rear,yaw,0,-3.31),base+2.57,span*.70,.08,1.48,yaw,"Smoked glazing")
        bench.box(rear,base+4.58,span*.82,7.35,.31,yaw,"Light standing seam")
    bench.bake(f"Richmond grandstand section {num:03d}")


def fixtures(target, markers, race, infield_streets):
    m=Builder(target)
    for i in range(35):
        p = race[(61+int(i*len(race)/35))%len(race)]
        offset = p.width/2+PERIMETER_WALK_OUTER+1.5
        point = (p.x+p.nx*offset,p.y+p.ny*offset)
        yaw=math.atan2(p.ty,p.tx)
        floodlight(m, point, yaw, 0., base=(.025, 1.85, 1.85, .45),
                   pole=(.40, .34, .34, 20.3), crossbar=(20.5, 2.75, .52, .37),
                   lenses=(20.51, .68, .44, .32), offsets=(-.87, 0., .87), setback=-.17)
        marker(markers,f"Richmond floodlight {i+1:02d}",(*point,20.81),yaw,
               aim=(p.x-p.nx*p.width*.28,p.y-p.ny*p.width*.28,p.z))
    for i,(point,yaw) in enumerate(infield_streets):
        m.box(point,.03,.54,.54,.18,yaw,"Paddock concrete")
        m.box(point,.18,.16,.16,5.1,yaw,"Fixture steel")
        m.box(local(point,yaw,0, .56),5.03,.22,1.28,.18,yaw,"Fixture steel")
        head=local(point,yaw,0,1.13)
        m.box(head,4.93,.36,.51,.17,yaw,"Fixture lens")
        marker(markers,f"Richmond street lamp {i+1:02d}",(*head,4.89),yaw,
               aim=(*local(point,yaw,0,3),0))
    m.bake("Richmond venue floodlight and street lamp fixtures")


def venue_lighting(target, anchors, street_lamps):
    race = [layout.race_pose(layout.LAP * i / 600) for i in range(600)]
    fixtures(target, anchors, race, street_lamps)


def spectator_parking(target):
    pose = layout.race_pose(-66)
    parking_lot(target, "frontstretch spectator parking", exterior_point(-66, 111),
                math.atan2(pose.ty, pose.tx), 20, 2, False, 411)


def hauler_parking(target, anchors, world, yaw):
    markings = Builder(target)
    span = HAULER_STALL_COUNT*HAULER_STALL_PITCH
    for bay in range(HAULER_STALL_COUNT):
        x = (bay+.5)*HAULER_STALL_PITCH-span*.5
        position = world(x,HAULER_STALL_CENTERLINE)
        # Blender local +Y points from the recovery yard toward the paddock
        # circulation aisle. Runtime paints and instantiates the hauler model.
        marker(anchors,f"Richmond hauler stall {bay+1:02d}",(*position,RECOVERY_YARD_Z),yaw)

    depth = 20.55
    center = HAULER_STALL_CENTERLINE+.2
    for boundary in range(HAULER_STALL_COUNT+1):
        x = boundary*HAULER_STALL_PITCH-span*.5
        markings.box(world(x,center),.036,.10,depth,.005,yaw,"Parking paint")
    markings.box(world(0,center-depth*.5),.037,span,.12,.005,yaw,"Parking paint")
    markings.bake("Richmond 40-space hauler parking markings")


def recovery_support_parking(target, world, yaw):
    markings, vehicles = Builder(target), Builder(target)
    rng = random.Random(42419)
    first = SUPPORT_PARKING_FIRST_X-SUPPORT_PARKING_PITCH*.5
    length = SUPPORT_PARKING_COUNT*SUPPORT_PARKING_PITCH
    # This portion of the paved strip has separate clearance between the
    # recovery return path and the back of the hauler row.
    for i in range(SUPPORT_PARKING_COUNT):
        x = SUPPORT_PARKING_FIRST_X+i*SUPPORT_PARKING_PITCH
        park_car(vehicles,world(x,SUPPORT_PARKING_Y),yaw,rng)
    for i in range(SUPPORT_PARKING_COUNT+1):
        x = first+i*SUPPORT_PARKING_PITCH
        markings.box(world(x,SUPPORT_PARKING_Y),.036,.10,5.5,.005,yaw,"Parking paint")
    markings.box(world(first+length*.5,SUPPORT_PARKING_Y+2.75),.037,length,.10,.005,yaw,"Parking paint")
    markings.bake("Richmond support parking behind haulers spaces")
    vehicles.bake("Richmond support parking behind haulers parked vehicles")


def roads_and_recovery(target, anchors, origin, yaw):
    paving = Builder(target)
    road_markings = Builder(target)

    def world(x, y):
        return local(origin, yaw, x, y)

    def ribbon(points, half_width, join_apron=False, first_edge=None):
        edges = []
        for i, (x, y) in enumerate(points):
            a, b = points[max(0,i-1)], points[min(len(points)-1,i+1)]
            dx, dy = layout.unit(layout.sub(b,a))
            if i == 0 and first_edge is not None:
                edges.append(first_edge)
                continue
            if join_apron and i == 0:
                row = []
                for side in (1, -1):
                    station = layout.RECOVERY_STATION + x-side*half_width
                    pose = layout.race_pose(station)
                    seam = layout.racing_surface(station, -pose.width*.5-pose.apron*.42)
                    row.append((seam[0], seam[1], seam[2]+.006))
                edges.append(row)
                continue
            edges.append([(*world(x-s*dy*half_width, y+s*dx*half_width), .028)
                          for s in (1,-1)])
        paving.ribbon(edges, "circuit asphalt")

    def route(name, points, apron_at_start):
        route_points = [world(*point) for point in points]
        for i, point in enumerate(route_points, 1):
            marker(anchors, f"Richmond {name} {i:02d}", (*point, .039))
        # Project both corners across the asphalt edge at apron grade. Ending
        # the road at the center alone can leave a grass wedge across one lane.
        ribbon(points if apron_at_start else points[::-1], 4.2, join_apron=True)

    # A shared backstretch wall opening connects the apron and service complex.
    # One row lets a tow approach each bay without passing through another bay.
    paving.box(world(0, 79), .016, 205, 84, RECOVERY_YARD_Z-.016, yaw, "Paddock asphalt")
    paving.box(world(0, 146), .016, 142, 46, .013, yaw, "Paddock asphalt")
    paving.box(world(-101, 124), .016, 116, 11, .011, yaw, "Paddock asphalt")
    paving.box(world(104, 125), .016, 108, 12, .011, yaw, "Paddock asphalt")
    # Fill the center gap to the garage apron at the back of the hauler row.
    paving.ribbon([
        [(*world(x,121.),RECOVERY_YARD_Z) for x in (-43.,50.)],
        [(*world(x,123.),.029) for x in (-43.,50.)],
    ], "Paddock asphalt")
    gate = layout.turn_gate_access()
    gx, gy = gate.at(gate.length)
    tx, ty = gate.heading(gate.length)
    first_edge = [(gx-s*ty*5,gy+s*tx*5,.008) for s in (1,-1)]
    start = local((0,0),-yaw,gx-origin[0],gy-origin[1])
    heading = local((0,0),-yaw,tx,ty)
    lead = [layout.add(start,layout.mul(heading,d)) for d in (3.,6.)]
    ribbon([start, *lead, (-150,122.0), (-136,124.0), (-107,124.0)], 5., first_edge=first_edge)
    ribbon([(-150,123), (-149,118), (-146,111), (-146,107)], 5.0)
    apron_lane = layout.WIDTH*.5 + layout.race_pose(layout.RECOVERY_STATION).apron*.53
    route("response outbound", [(-91, 27), (-70, 27), (-48, 29),
                                (-35, 25), (-25, 18), (-23, apron_lane)], False)
    route("recovery entry", [(-7, apron_lane), (-7, 26), (-20, 37), (-52, 41),
                             (-82, 37), (-96, 38), (-99, 45)], True)
    # Empty trucks turn across the yard before reaching the backs of parked
    # haulers. Drop and approach markers retain their original bay locations.
    route("recovery exit", [(97, 91), (99, 82), (98, 58), (91, 36),
                            (68, 27), (43, 24), (24, 19), (20, apron_lane)], False)
    for i in range(40):
        x = (i-19.5)*3.8
        for name, y in (("approach", 53), ("bay", 64), ("depart", 80)):
            marker(anchors, f"Richmond recovery {name} {i+1:02d}",
                   (*world(x, y), .039), yaw)
        road_markings.box(world(x-1.9, 64), .035, .11, 9.7, .006, yaw, "Parking paint")
    road_markings.box(world(76, 64), .035, .11, 9.7, .006, yaw, "Parking paint")
    road_markings.box(world(0, 69), .035, 152, .12, .006, yaw, "Parking paint")

    paving.box(world(-93, 18), .016, 31, 21, .018, yaw, "Paddock concrete")
    for i in range(3):
        x = -93 + (i-1)*7
        name = "ambulance bay 01" if i == 0 else f"tow bay {i:02d}"
        marker(anchors, f"Richmond {name}", (*world(x, 18), .040), yaw)
        road_markings.box(world(x-3.5, 18), .035, .09, 9.6, .006, yaw, "Parking paint")
    road_markings.box(world(-82.5, 18), .035, .09, 9.6, .006, yaw, "Parking paint")
    paving.bake("Richmond paved safety access and recovery yard")
    road_markings.bake("Richmond emergency staging and 40 recovery bays")
    hauler_parking(target, anchors, world, yaw)
    recovery_support_parking(target, world, yaw)


def exterior_grandstands(target, anchors, *, include_walks=True):
    subdivisions = [
        (-240, -118, 4, 11, "Dogwood"),
        (-118, -13, 3, 13, "Commonwealth"),
        (14, 155, 4, 14, "Commonwealth"),
        (155, 260, 3, 11, "Veranda"),
    ]
    section_index = 0
    for group, (start, end, count, rows, name) in enumerate(subdivisions):
        for i in range(count):
            span = (end-start)/count
            station = start+span*(i+.5)
            pose = layout.race_pose(station)
            x, y, _ = pose.offset(side=pose.width*.5+13+rows*.56)
            yaw = math.atan2(-pose.nx, pose.ny)
            section_index += 1
            track_edge_height = layout.racing_surface(station, pose.width*.5)[2]
            grandstand(target, anchors, section_index, (x,y,yaw), span*.85, rows, track_edge_height,
                       concourse=name in ("Dogwood", "Commonwealth") and i in (1, 2))
        for road in range(2 if include_walks else 0):
            half = (end-start)*.5
            p, q = [exterior_point(start+road*half+s,57) for s in (6,half-6)]
            heading = math.atan2(q[1]-p[1], q[0]-p[0])
            for label, point in (("start",p),("end",q)):
                marker(anchors, f"Richmond pedestrian walk {11+group*2+road:02d} {label}",
                       (*point,PERIMETER_WALK_Z+.005),heading)


def protected_walkway_frames(world):
    for x0, y0, x1, y1 in ((-62,123,62,123), (-57,168,56,168), (-73,133,-73,160),
                           (68,132,68,156), (-141,158,-105,158), (101,153,153,153)):
        a, b = world(x0,y0), world(x1,y1)
        center = ((a[0]+b[0])*.5, (a[1]+b[1])*.5)
        length = math.dist(a,b)
        heading = math.atan2(b[1]-a[1], b[0]-a[0])
        yield a, b, center, length, heading


def protected_walkway_lamps(world):
    return [(local(center, heading, sign * length * .34, -2.0), heading)
            for _, _, center, length, heading in protected_walkway_frames(world)
            for sign in (-1, 1)]


def protected_walkways(target, anchors, world, yaw):
    walk = Builder(target)
    fan = 0
    for i, (a, b, center, length, heading) in enumerate(protected_walkway_frames(world), 1):
        walk.box(center,.024,length+1,3.4,.021,heading,"Paddock concrete")
        marker(anchors,f"Richmond pedestrian walk {i:02d} start",(*a,.046),heading)
        marker(anchors,f"Richmond pedestrian walk {i:02d} end",(*b,.046),heading)
        slots = max(4,int(length/6))
        for j in range(slots):
            across = (j/(slots-1)-.5)*length*.74
            for side in (-1,1):
                fan += 1
                xy = local(center,heading,across,side*1.5)
                marker(anchors,f"Richmond standing fan {fan:03d}",(*xy,.046),heading)
    walk.bake("Richmond protected FanGrounds pedestrian paths")
    return protected_walkway_lamps(world)


def concession(model, xy, yaw, shade):
    def box(x, y, base, width, depth, height, color):
        model.box(local(xy,yaw,x,y),base,width,depth,height,yaw,color)

    box(0,0,.014,3.65,2.58,.10,"Section concrete")
    box(0,0,.11,3.35,2.35,1.02,shade)
    box(0,.86,1.13,3.35,.54,1.48,"RV ivory")
    box(0,-1.13,1.07,3.14,.58,.14,"Light standing seam")
    for x in (-1.5,1.5):
        box(x,-.89,1.13,.13,.13,1.56,"Fixture steel")
    box(0,-.15,2.60,3.75,2.9,.17,shade)
    box(0,-1.59,2.42,3.75,.12,.23,shade)
    for x in (-1.38,-.46,.46,1.38):
        box(x,-1.66,2.43,.44,.035,.15,"RV ivory")
    for x in (-.98,0,.98):
        box(x,.56,1.68,.68,.035,.38,"Charcoal panels")


def portable_restroom(model, xy, yaw):
    def box(x, y, base, width, depth, height, color):
        model.box(local(xy,yaw,x,y),base,width,depth,height,yaw,color)

    box(0,0,.014,1.18,1.28,.09,"Charcoal panels")
    box(0,0,.103,1.14,1.2,2.03,"Restroom plastic")
    box(0,-.616,.127,.82,.04,1.82,"Blue trim")
    box(0,0,2.10,1.19,1.24,.12,"RV ivory")
    box(.29,-.642,1.03,.09,.05,.11,"Fixture steel")
    box(0,-.64,1.76,.36,.063,.07,"Charcoal panels")


def litter_barrel(model, point):
    # The two narrow rings and lid distinguish the barrel from the pit posts.
    for r, z, h, color in ((.29,.014,.78,"Waste barrel"),
                           (.304,.20,.06,"Fixture steel"),
                           (.304,.653,.055,"Fixture steel"),
                           (.32,.794,.075,"Charcoal panels")):
        base = len(model.vertices)
        for height in (z,z+h):
            model.vertices.extend((point[0]+r*math.cos(i*math.tau/12),
                                   point[1]+r*math.sin(i*math.tau/12),height)
                                  for i in range(12))
        for i in range(12):
            j = (i+1)%12
            model.add_face((base+i,base+j,base+12+j,base+12+i),color)
        model.add_face(range(base+12,base+24), color)


def pit_walkway_amenities(target, anchors):
    bays = Builder(target)
    rng = random.Random(15222)
    fan = 501

    for number, (station, booth) in enumerate(((77,False), (98,True), (147,True),
                                               (176,False), (197,True), (238,True),
                                               (262,False), (285,True), (346,False)), 1):
        pose = layout.pit_pose(station)
        yaw = math.atan2(pose.ty,pose.tx)
        x, y, _ = pose.offset(side=layout.PIT_WALK_INNER)
        xy = (x,y)
        bays.ribbon([
            [layout.pit_pose(station-5.5+i).offset(side=layout.PIT_WALK_INNER+side, height=.014)
             for side in (.035,-7.0)]
            for i in range(12)
        ], "Paddock concrete")
        props = Builder(target)
        if booth:
            concession(props, local(xy,yaw,0,5.05), yaw,
                       "Concession red" if number%2 == 0 else "Blue trim")
            for depth, count in ((1.26,3), (2.53,3)):
                for i in range(count):
                    delta = (i-(count-1)*.5)*1.12+rng.uniform(-.14,.14)
                    spot = local(xy,yaw,delta,depth+rng.uniform(-.13,.13))
                    marker(anchors,f"Richmond standing fan {fan:03d}",(*spot,.016),yaw)
                    fan += 1
        else:
            for delta in (-1.83,-.52,.79,2.10):
                portable_restroom(props,local(xy,yaw,delta,4.3),yaw)
            for delta, depth in ((-2.55,1.49),(.96,2.03)):
                spot = local(xy,yaw,delta,depth)
                marker(anchors,f"Richmond standing fan {fan:03d}",(*spot,.016),yaw)
                fan += 1
        for delta, depth in ((-4.20,2.08),(3.95,3.15)):
            litter_barrel(props,local(xy,yaw,delta,depth))
        name = "concession stand" if booth else "portable toilets"
        props.bake(f"Richmond pit walkway {name} and trash barrels {number:02d}")

    bays.bake("Richmond pit walkway amenity bays")

    # Keep trajectories centered in the walking lanes and break them into
    # short runs at crossings, near the wall ends, and through curved sections.
    walks = ((41,54), (58,71), (81,97), (102,117), (125,142), (174,190),
             (199,215), (222,237), (243,256), (263,279), (281,298), (303,318),
             (329,343), (348,359))
    for i, (start, end) in enumerate(walks, 31):
        for label, station in (("start",start),("end",end)):
            pose = layout.pit_pose(station)
            pos = pose.offset(side=(layout.PIT_WALK_INNER+layout.PIT_WORK_INNER)*.5,
                              height=.014)
            marker(anchors,f"Richmond pedestrian walk {i:02d} {label}",pos,
                   math.atan2(pose.ty,pose.tx))


def build():
    architecture = section("FanGrounds buildings")
    seating = section("Frontstretch grandstands")
    parking = section("Paddock and exterior parking")
    service = section("Recovery yard and venue utilities")
    anchors = section("Unity live scenery anchors")

    gate = layout.race_pose(layout.RECOVERY_STATION)
    origin = (gate.x, gate.y)
    yaw = math.atan2(gate.ty, gate.tx)

    def world(x,y):
        return local(origin, yaw, x, y)

    # The official FanGrounds includes Cup garages, viewing, suites and Victory
    # Lane. The reference only shows the courses, so footprints are inferred.
    for name,x,y,width,depth,height,roof,stalls in (
        ("40-stall Cup garage",0,146,112,20,6.5,"Metal roof",20),
        ("infield media and medical center",-101,143,34,20,6.8,"Metal roof",0),
        ("FanGrounds hospitality suites",96,137,36,18,7.5,"Light standing seam",0),
        ("Victory Lane pavilion",-79,170,29,12,5.4,"Light standing seam",0),
        ("FanGrounds food and concessions",117,168,30,12,4.7,"Light standing seam",0),
        ("infield operations garage",-146,100,24,14,5.0,"Metal roof",0),
    ):
        building(architecture, name, world(x,y), width,depth,height,yaw,roof,stalls)

    roads_and_recovery(service, anchors, origin, yaw)
    for i,(name,x,y,columns,rows) in enumerate((
        ("media parking",-137,136,10,1), ("paddock parking",142,127,11,2),
    )):
        parking_lot(parking,name,world(x,y),yaw,columns,rows,False,131+i)

    start = layout.race_pose(0)
    x,y,_ = start.offset(side=-(start.width*.5+start.apron+16))
    marker(anchors,"Richmond scoring pylon marker",(x,y,.045),math.atan2(-start.nx,start.ny))
    exterior_grandstands(seating, anchors)
    exterior_walkway(parking)
    trackside_camp(parking,"turn 3 RV camping",layout.marker(53,70),12,409)
    trackside_camp(parking,"backstretch RV camping",layout.marker(177,24),15,410)
    spectator_parking(parking)
    pit_walkway_amenities(service, anchors)
    streets = protected_walkways(service, anchors, world, yaw)
    venue_lighting(service, anchors, streets)

    from richmond_infield import build_infield
    build_infield(anchors, origin, yaw)


