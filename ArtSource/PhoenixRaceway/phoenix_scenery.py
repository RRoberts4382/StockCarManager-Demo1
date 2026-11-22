"""Phoenix venue scenery placed from the supplied overhead photo.

The traced race/pit layout owns all driving geometry. This module adds editable
venue structures, exterior parking, and markers read by Unity's live scenery.
"""

import math
import random

import bpy
from mathutils import Matrix, Vector

import phoenix_layout as layout


COLORS = {
    "Paddock concrete": (0.43, 0.40, 0.35),
    "Paddock asphalt": (0.11, 0.105, 0.10),
    "Parking paint": (0.75, 0.73, 0.64),
    "Sand stucco": (0.54, 0.45, 0.34),
    "Light standing seam": (0.62, 0.63, 0.60),
    "Metal roof": (0.31, 0.36, 0.39),
    "Charcoal panels": (0.13, 0.17, 0.20),
    "Smoked glazing": (0.045, 0.095, 0.12),
    "Blue trim": (0.018, 0.39, 0.57),
    "Golden trim": (0.88, 0.51, 0.075),
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
}


def material(name):
    name = "Phoenix " + name
    existing = bpy.data.materials.get(name)
    if existing:
        return existing
    result = bpy.data.materials.new(name)
    color = COLORS[name[8:]]
    result.diffuse_color = (*color, 1)
    result.use_nodes = True
    shader = result.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = (*color, 1)
    shader.inputs["Roughness"].default_value = .27 if "glazing" in name else .77
    return result


def section(name):
    target = bpy.data.collections.new("Phoenix | " + name)
    bpy.context.scene.collection.children.link(target)
    return target


class Builder:
    def __init__(self, target):
        self.target = target
        self.vertices = []
        self.faces = []
        self.materials = []
        self.indices = []

    def quad(self, points, name):
        index = len(self.vertices)
        self.vertices.extend(points)
        self.add_face(range(index, index + len(points)), name)

    def add_face(self, indices, name):
        if name not in self.materials:
            self.materials.append(name)
        self.indices.append(self.materials.index(name))
        self.faces.append(tuple(indices))

    def box(self, xy, base, width, depth, height, yaw, name):
        c, s = math.cos(yaw), math.sin(yaw)
        cx, cy = xy
        index = len(self.vertices)
        for z in (base, base + height):
            for x, y in ((-width / 2, -depth / 2), (width / 2, -depth / 2),
                         (width / 2, depth / 2), (-width / 2, depth / 2)):
                self.vertices.append((cx + c*x - s*y, cy + s*x + c*y, z))
        for face in ((3, 2, 1, 0), (4, 5, 6, 7), (0, 1, 5, 4),
                     (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)):
            self.add_face((index + i for i in face), name)

    def brace(self, start, end, width, name):
        start, end = Vector(start), Vector(end)
        length = (end - start).length
        rotate = (end - start).to_track_quat('Z', 'Y')
        index = len(self.vertices)
        half = width / 2
        for z in (0, length):
            for x, y in ((-half, -half), (half, -half), (half, half), (-half, half)):
                self.vertices.append(start + rotate @ Vector((x, y, z)))
        for face in ((3, 2, 1, 0), (4, 5, 6, 7), (0, 1, 5, 4),
                     (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)):
            self.add_face((index + i for i in face), name)

    def bake(self, name):
        if not self.faces:
            return None
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata(self.vertices, [], self.faces)
        mesh.update()
        obj = bpy.data.objects.new(name, mesh)
        self.target.objects.link(obj)
        for name in self.materials:
            mesh.materials.append(material(name))
        for polygon, index in zip(mesh.polygons, self.indices):
            polygon.material_index = index
        return obj


def local(center, yaw, x, y):
    return (center[0] + x*math.cos(yaw) - y*math.sin(yaw),
            center[1] + x*math.sin(yaw) + y*math.cos(yaw))


def marker(target, name, location, yaw=0.0, width=1.0, aim=None):
    # FBX retains a real mesh more reliably than a locator. The runtime hides
    # its renderer for seating and pedestrian markers.
    mesh = bpy.data.meshes.get("Phoenix locator")
    if mesh is None:
        mesh = bpy.data.meshes.new("Phoenix locator")
        mesh.from_pydata([(-.5, -.025, -.025), (.5, -.025, -.025),
                          (0, .025, -.025), (0, 0, .025)], [],
                         [(2, 1, 0), (0, 1, 3), (1, 2, 3), (2, 0, 3)])
        mesh.materials.append(material("Fixture lens"))
    obj = bpy.data.objects.new(name, mesh)
    target.objects.link(obj)
    obj.location = location
    obj.rotation_euler.z = yaw
    if aim is not None:
        obj.rotation_euler = (Vector(aim)-Vector(location)).to_track_quat('-Z', 'Y').to_euler()
    obj.scale = (width, 1, 1)
    return obj


def photo(px, py):
    """Coordinates measured on a 1152×648 working copy of the supplied image."""
    return tuple(layout.overhead_xy(px / .3, py / .3))[:2]


def photo_box(px, py, w, d, angle):
    center = photo(px, py)
    direction = photo(px + 20*math.cos(angle), py + 20*math.sin(angle))
    normal = photo(px - 20*math.sin(angle), py + 20*math.cos(angle))
    yaw = math.atan2(direction[1]-center[1], direction[0]-center[0])
    return center, w*math.dist(direction, center)/20, d*math.dist(normal, center)/20, yaw


def race_samples():
    return [layout.race_pose(layout.LAP*i/480) for i in range(480)]


def infield_position(center, radius, racing, pitting, origin):
    result = center
    for _ in range(28):
        closest = min(racing, key=lambda p: (p.x-result[0])**2 + (p.y-result[1])**2)
        cross = (result[0]-closest.x)*closest.nx + (result[1]-closest.y)*closest.ny
        pit_gap = min(math.dist(result, (p.x, p.y)) - p.width*.5 for p in pitting)
        if cross < -closest.width*.5 - closest.apron - radius and pit_gap > radius:
            return result
        result = ((result[0]*.91 + origin[0]*.09), (result[1]*.91 + origin[1]*.09))
    return result


def station(x, y, samples):
    return min(range(len(samples)), key=lambda i: math.hypot(samples[i].x-x, samples[i].y-y))*layout.LAP/len(samples)


def building(target, title, center, width, depth, height, yaw, roof, stalls=0):
    model = Builder(target)
    model.box(center, .014, width+13, depth+13, .018, yaw, "Paddock concrete")
    model.box(center, .032, width, depth, height, yaw, "Sand stucco")
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
    model.bake("Phoenix " + title)


def parking_vehicle(model, center, yaw, rv, color):
    if rv:
        width, depth, body_h, top = 10.1, 2.57, 2.65, 3.29
        model.box(center, .45, width, depth, body_h, yaw, "RV ivory")
        model.box(local(center, yaw, .7, 0), 1.20, width*.78, depth+.02, .22, yaw, color)
        model.box(local(center, yaw, -1.4, 0), top-.19, 2.8, 1.55, .33, yaw, "Light standing seam")
        model.box(local(center, yaw, width*.43, 0), 1.26, .22, depth*.87, 1.32, yaw, "Smoked glazing")
        for x in (-3.3, -.2, 2.1):
            model.box(local(center, yaw, x, 0), 1.8, 1.25, depth+.032, .65, yaw, "Smoked glazing")
    else:
        width, depth = 4.58, 1.84
        model.box(center, .22, width, depth, .70, yaw, color)
        model.box(local(center, yaw, -.26, 0), .90, 2.36, 1.58, .50, yaw, "Smoked glazing")
        model.box(local(center, yaw, -.31, 0), 1.36, 1.67, 1.46, .09, yaw, color)
        model.box(local(center, yaw, width*.495, 0), .48, .065, depth*.81, .13, yaw, "Fixture lens")
        model.box(local(center, yaw, -width*.495, 0), .48, .065, depth*.81, .13, yaw, "Parked red")
    for x in (-width*.31, width*.32):
        for sign in (-1,1):
            model.box(local(center, yaw, x, sign*depth*.48), .02, .78 if rv else .61,
                      .29, .78 if rv else .61, yaw, "Tires")


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
            if rng.random() > (.77 if rv else .66):
                continue
            x = (i+.5-columns/2)*bay
            xy = local(center, yaw, x+rng.uniform(-.26,.26), y+rng.uniform(-.52,.52))
            heading = yaw+math.pi*.5+rng.uniform(-.045,.045)+(math.pi if (row+i)%3 else 0)
            parking_vehicle(vehicles, xy, heading, rv, rng.choice(colors))
    paint.bake("Phoenix " + title + " paving and spaces")
    vehicles.bake("Phoenix " + title + " parked vehicles")


def grandstand(target, locators, num, position, span, rows, concourse=False):
    x, y, yaw = position
    center = (x,y)
    bench = Builder(target)
    row_pitch = 1.12
    depth = row_pitch*rows
    bench.box(local(center,yaw,0,2.5), .015, span+4, depth+18, .11, yaw, "Paddock concrete")
    seats = "Seat denim" if num < 13 else "Seat garnet"
    # A raised first row clears the catch fence. The higher deck starts farther
    # back, and its stronger rake exposes faces above the next row toward the track.
    lower_start, lower_rise = 2.3, .74
    upper_start, upper_rise = 12.75, .98
    upper_setback = 7.5
    if num == 14:
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
                marker(locators, f"Phoenix grandstand bench {num:03d}-{first_row+row:02d}-{column+1}",
                       (*row_xy,tread+.26), yaw, usable)
            # The center aisle steps independently of the deeper seating platforms.
            steps = math.ceil(rise/.20)
            for step in range(steps):
                step_depth = row_pitch/steps
                y = across+(step+.5)*step_depth-row_pitch*.5
                step_top = tread-rise*(1-(step+1)/steps)
                bench.box(local(center,yaw,0,y),step_top-.24,1.29,step_depth+.005,.24,yaw,"Section concrete")
                bench.box(local(center,yaw,0,y-step_depth*.5+.025),step_top,
                          1.18,.055,.012,yaw,"Golden trim")

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
        bench.brace((*front_frame,upper_start-.65),(*rear_frame,8.2),.39,"Fixture steel")
        bench.brace((*front_frame,7.9),(*rear_frame,rear_height-.48),.39,"Fixture steel")
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
        rear = local(center, yaw, 0, depth/2+3.7)
        base = lower_start+lower_rise*(rows-1)
        for sign in (-1,1):
            bench.box(local(rear,yaw,span*.32*sign,0), .13,.72,1.55,base+1.08,yaw,"Section concrete")
        bench.box(rear,base+.90,span*.80,7.8,.65,yaw,"Paddock concrete")
        bench.box(rear,base+1.55,span*.76,6.6,3.03,yaw,"Charcoal panels")
        bench.box(local(rear,yaw,0,-3.31),base+2.57,span*.70,.08,1.48,yaw,"Smoked glazing")
        bench.box(rear,base+4.58,span*.82,7.35,.31,yaw,"Light standing seam")
    bench.bake(f"Phoenix grandstand section {num:03d}")


def fixtures(target, markers, race, infield_streets):
    m=Builder(target)
    for i in range(35):
        p = race[(61+int(i*len(race)/35))%len(race)]
        offset = p.width/2+8.5
        point = (p.x+p.nx*offset,p.y+p.ny*offset)
        yaw=math.atan2(p.ty,p.tx)
        m.box(point,.025,1.85,1.85,.45,yaw,"Paddock concrete")
        m.box(point,.40,.34,.34,20.3,yaw,"Fixture steel")
        m.box(point,20.5,2.75,.52,.37,yaw,"Fixture steel")
        for j in (-1,0,1):
            xy=local(point,yaw,j*.87,-.17)
            m.box(xy,20.51,.68,.44,.32,yaw,"Fixture lens")
        marker(markers,f"Phoenix floodlight {i+1:02d}",(*point,20.81),yaw,
               aim=(p.x-p.nx*p.width*.28,p.y-p.ny*p.width*.28,p.z))
    for i,(point,yaw) in enumerate(infield_streets):
        m.box(point,.03,.54,.54,.18,yaw,"Paddock concrete")
        m.box(point,.18,.16,.16,5.1,yaw,"Fixture steel")
        m.box(local(point,yaw,0, .56),5.03,.22,1.28,.18,yaw,"Fixture steel")
        head=local(point,yaw,0,1.13)
        m.box(head,4.93,.36,.51,.17,yaw,"Fixture lens")
        marker(markers,f"Phoenix street lamp {i+1:02d}",(*head,4.89),yaw,
               aim=(*local(point,yaw,0,3),0))
    m.bake("Phoenix venue floodlight and street lamp fixtures")


def protected_wall_crowds(markers, buildings):
    rng = random.Random(87)
    count = 80
    for sampler, stations, side in (
        (layout.race_pose, (layout.EXIT_STATION+145, layout.EXIT_STATION+210,
                            layout.ENTRY_STATION-245, layout.ENTRY_STATION-150),
         lambda p: -p.width/2-p.apron-2.9),
        (layout.pit_pose, (layout.STALL_START+42, layout.PIT_LENGTH*.46, layout.STALL_END-50),
         lambda p: -19.0),
    ):
        for start in stations:
            for row in range(rng.choice((2,3))):
                for index in range(rng.choice((7,8,9,10))):
                    p = sampler(start + index*1.37 + row*.62 + rng.uniform(-.28,.28))
                    xy = p.offset(side=side(p)-row*1.13)[:2]
                    blocked = False
                    for center,width,depth,yaw in buildings:
                        lx,ly = local((0,0),-yaw,xy[0]-center[0],xy[1]-center[1])
                        if abs(lx)<width/2+3 and abs(ly)<depth/2+3:
                            blocked = True
                            break
                    if blocked:
                        continue
                    count += 1
                    marker(markers,f"Phoenix standing fan {count:03d}",(*xy,.045))


def emergency_yard(services, markers, garage, yaw):
    paving=Builder(services)
    def world(x,y):
        return local(garage,yaw,x,y)
    def apron_gate(station):
        p=layout.race_pose(station)
        return p.offset(side=-p.width*.5-p.apron-.7)[:2]
    def road(name,points):
        normals=[]
        for i in range(len(points)):
            a,b=points[max(0,i-1)],points[min(i+1,len(points)-1)]
            sx,sy=b[0]-a[0],b[1]-a[1]
            unit=math.hypot(sx,sy)
            normals.append((-5.8*sy/unit,5.8*sx/unit))
        for i in range(len(points)-1):
            corners=[]
            for j,side in ((i,-1),(i+1,-1),(i+1,1),(i,1)):
                corners.append((points[j][0]+side*normals[j][0],points[j][1]+side*normals[j][1],.039))
            if (Vector(corners[1])-Vector(corners[0])).cross(Vector(corners[2])-Vector(corners[0])).z < 0:
                corners.reverse()
            paving.quad(corners,"Paddock asphalt")
        for i,xy in enumerate(points,1):
            marker(markers,f"Phoenix {name} {i:02d}",(*xy,.045))

    apron_out=apron_gate(904)
    apron_in=apron_gate(923)
    apron_exit=apron_gate(947)
    road("response outbound",[world(*p) for p in [(-38,-36),(-53,-37),(-65,-41),(-74,-51),(-79,-67),(-80,-92)]]+[apron_out])
    road("recovery entry",[apron_in]+[world(*p) for p in [(-62,-97),(-59,-84),(-59,-64),(-57,-51),(-51,-44),(-44,-43)]])
    road("recovery exit",[world(*p) for p in [(-43,-86),(-51,-87),(-54,-92),(-52,-98),(-46,-103)]]+[apron_exit])
    paving.box(world(0,-65),.019,102,54,.025,yaw,"Paddock asphalt")
    for slot in range(24):
        x=(slot-11.5)*3.55
        for kind,y in (("approach",-45),("bay",-65),("depart",-78)):
            marker(markers,f"Phoenix recovery {kind} {slot+1:02d}",(*world(x,y),.046),yaw+math.pi)
        paving.box(world(x-1.78,-65),.045,.11,11,.006,yaw,"Parking paint")
    paving.box(world(42.60,-65),.045,.11,11,.006,yaw,"Parking paint")
    paving.bake("Phoenix emergency access and 24-bay recovery yard")


def build(context=None):
    structures = section("Aerial buildings")
    stands = section("Grandstands")
    parking = section("Paddock and exterior parking")
    infrastructure = section("Service and lighting")
    markers = section("Runtime anchors")
    race = race_samples()
    pits = [layout.pit_pose(layout.PIT_LENGTH*i/125) for i in range(126)]
    origin = (sum(p.x for p in race)/len(race),sum(p.y for p in race)/len(race))

    buildings = [
        ("paddock operations building",472,226,111,63,.10,7.8,"Sand stucco"),
        ("operations south awning",431,260,43,25,.08,4.3,"Metal roof"),
        ("hospitality pavilion",584,207,40,34,.14,6.8,"Metal roof"),
        ("hospitality annex",648,213,34,18,.15,5.4,"Sand stucco"),
        ("north merchandise canopy",573,253,80,24,.16,5.0,"Light standing seam"),
        ("north concessions canopy",664,258,83,24,.15,5.0,"Light standing seam"),
        ("service kiosk",465,334,28,24,-.07,4.8,"Light standing seam"),
        ("east garage canopy",737,317,68,28,1.49,6.2,"Light standing seam"),
        ("east hospitality canopy",736,418,153,27,1.46,5.6,"Light standing seam"),
        ("victory lane pavilion",772,367,34,27,1.72,8.2,"Sand stucco"),
        ("east service annex",780,308,38,14,1.50,4.2,"Sand stucco"),
        ("east fan gathering canopy",780,448,52,15,1.54,4.2,"Metal roof"),
        ("infield electrical building",628,335,55,14,1.56,5.2,"Sand stucco"),
        ("medical station",197,324,36,31,.03,4.8,"Sand stucco"),
        ("south service building",312,435,24,21,.05,4.4,"Light standing seam"),
    ]
    footprints = []
    for title,px,py,w,d,angle,height,roof in buildings:
        xy,width,depth,yaw=photo_box(px,py,w,d,angle)
        xy=infield_position(xy, max(width,depth)*.24+10, race, pits, origin)
        footprints.append((xy,width,depth,yaw))
        building(structures,title,xy,width,depth,height,yaw,roof)

    xy,gar_width,gar_depth,gar_yaw=photo_box(493,415,138,24,-.015)
    gar_width=max(101,min(148,gar_width))
    gar_depth=max(14,min(23,gar_depth))
    garage=infield_position(xy,gar_depth/2+17,race,pits,origin)
    footprints.append((garage,gar_width,gar_depth,gar_yaw))
    building(structures,"40-stall infield garage",garage,gar_width,gar_depth,6.1,gar_yaw,"Metal roof",stalls=20)

    work=Builder(infrastructure)
    helipad=infield_position(photo(198,357),26,race,pits,origin)
    work.box(helipad,.019,31,31,.03,gar_yaw,"Paddock concrete")
    for i in range(40):
        a0=math.tau*i/40
        a1=math.tau*(i+1)/40
        def circle(r,a):
            return (*local(helipad,gar_yaw,r*math.cos(a),r*math.sin(a)),.058)
        work.quad([circle(10.9,a0),circle(11.3,a0),circle(11.3,a1),circle(10.9,a1)],"Parking paint")
    for sg in (-1,1):
        work.box(local(helipad,gar_yaw,2.2*sg,0),.052,.38,5.5,.008,gar_yaw,"Parking paint")
    work.box(helipad,.052,4.7,.38,.008,gar_yaw,"Parking paint")
    responders=local(garage,gar_yaw,-gar_width*.31,-gar_depth/2-14)
    work.box(responders,.018,29,18,.022,gar_yaw,"Paddock concrete")
    for i in range(4):
        xy=local(responders,gar_yaw,(i-1.5)*6,0)
        marker(markers, "Phoenix ambulance bay 01" if i==0 else f"Phoenix tow bay {i:02d}",
               (*xy,.042),gar_yaw+math.pi)
        work.box(local(xy,gar_yaw,-2.4,0),.041,.13,11,.006,gar_yaw,"Parking paint")
    work.box(local(responders,gar_yaw,11.4,0),.041,.13,11,.006,gar_yaw,"Parking paint")
    work.bake("Phoenix helicopter pad and service markings")
    emergency_yard(infrastructure,markers,garage,gar_yaw)

    for i,(name,px,py,columns,rows) in enumerate([
        ("medical and west parking",278,317,18,2), ("infield main paddock parking",533,348,20,2),
        ("east garage lot",669,443,16,2),
    ]):
        xy,_,_,yaw=photo_box(px,py,30,12,0)
        xy=infield_position(xy,24,race,pits,origin)
        parking_lot(parking,name,xy,yaw,columns,rows,False,8294+i)

    # Continuously curve the venues' eastern grandstand blocks outside the
    # reference-aligned wall. Spacing leaves stairways and concrete concourses.
    top=station(*photo(655,71),race)
    bottom=station(*photo(865,572),race)
    forward=(bottom-top)%layout.LAP
    if forward > layout.LAP*.5:
        top,bottom=bottom,top
        forward=layout.LAP-forward
    span=max(420,min(680,forward))
    count=17
    concourse=Builder(stands)
    for i in range(count*12):
        corners=[]
        for j,edge in ((i,26),(i+1,26),(i+1,45),(i,45)):
            p=layout.race_pose(top+span*j/(count*12))
            offset=p.width*.5+edge
            corners.append((p.x+p.nx*offset,p.y+p.ny*offset,.038))
        if (Vector(corners[1])-Vector(corners[0])).cross(Vector(corners[2])-Vector(corners[0])).z < 0:
            corners.reverse()
        concourse.quad(corners,"Paddock concrete")
    concourse.bake("Phoenix continuous exterior grandstand concourse")
    for i in range(count):
        p=layout.race_pose((top+(i+.5)*span/count)%layout.LAP)
        rows=13 if 6<=i<=12 else 11
        offset=p.width*.5+13+rows*.56
        center=(p.x+p.nx*offset,p.y+p.ny*offset)
        # Point local Y outwards so rows rise away from the track.
        yaw=math.atan2(-p.nx,p.ny)
        grandstand(stands,markers,i+1,(*center,yaw),span/count*.84,rows,concourse=i in (4,5,9,10,11))

    # Exterior RV lots use open wall spans beyond the stand structures and
    # stay well clear of spectator lanes and the emergency access approaches.
    for i, dist in enumerate(((top+span+185)%layout.LAP,(top+span+345)%layout.LAP,(top-125)%layout.LAP)):
        p=layout.race_pose(dist)
        center=(p.x+p.nx*(p.width*.5+78),p.y+p.ny*(p.width*.5+78))
        parking_lot(parking,f"exterior {'RV' if i != 2 else 'spectator'} camp lot {i+1}",center,
                    math.atan2(p.ty,p.tx),10 if i !=2 else 22,2,i!=2,9400+i)

    street=[]
    walk=Builder(infrastructure)
    for i,(x0,y0,x1,y1) in enumerate([
        (405,267,405,323), (538,271,599,282), (451,390,549,390),
        (575,442,615,442), (703,339,705,391), (348,255,383,255), (244,408,283,403),
    ]):
        a=infield_position(photo(x0,y0),9,race,pits,origin)
        b=infield_position(photo(x1,y1),9,race,pits,origin)
        length=math.dist(a,b)
        if length<7:
            continue
        center=((a[0]+b[0])*.5,(a[1]+b[1])*.5)
        yaw=math.atan2(b[1]-a[1],b[0]-a[0])
        walk.box(center,.023,length+1,3.6,.021,yaw,"Paddock concrete")
        marker(markers,f"Phoenix pedestrian walk {i+1:02d} start",(*a,.046),yaw)
        marker(markers,f"Phoenix pedestrian walk {i+1:02d} end",(*b,.046),yaw)
        street.extend((local(center,yaw,sg*length*.38,-2.5),yaw) for sg in (-1,1))
        for j in range(3):
            xy=local(center,yaw,(j-1)*min(5,length*.10),2.15)
            marker(markers,f"Phoenix standing fan {i*3+j+1:02d}",(*xy,.043),yaw)
    walk.bake("Phoenix protected infield walkways")
    protected_wall_crowds(markers,footprints)
    fixtures(infrastructure,markers,race,street)
