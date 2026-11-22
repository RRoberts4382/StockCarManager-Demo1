"""Editable North Wilkesboro architecture, camping and Unity scenery anchors."""

import math
from functools import partial
import sys
from pathlib import Path
import random

import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

import wilkesboro_layout as layout


# Use the shared components from the workspace root or the installed ArtSource.
for ancestor in Path(__file__).resolve().parents:
    library = ancestor / "VenueComponents"
    if (library / "venue_geometry.py").is_file():
        sys.path.insert(0, str(library))
        break
from venue_geometry import CAR_PAINT, Builder as MeshBuilder, floodlight, local, marker as venue_marker, parking_vehicle, park_car
from curved_grandstand import curved_grandstand

COLORS = {
    "Venue red": (.55, .026, .030),
    "Venue white": (.79, .78, .72),
    "Stadium glass": (.041, .102, .121),
    "Navy signage": (.029, .061, .136),
    "Scoreboard red": (.63, .014, .025),
    "Scoreboard cream": (.95, .76, .37),
    "Scoreboard white": (.88, .87, .80),
    "Scoreboard black": (.042, .044, .045),
    "circuit asphalt": (.115, .113, .107),
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
    name = "North Wilkesboro " + name
    existing = bpy.data.materials.get(name)
    if existing:
        return existing
    result = bpy.data.materials.new(name)
    color = COLORS[name.removeprefix("North Wilkesboro ")]
    result.diffuse_color = (*color, 1)
    result.use_nodes = True
    shader = result.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = (*color, 1)
    shader.inputs["Roughness"].default_value = .27 if "glazing" in name else .77
    return result


Builder = partial(MeshBuilder, material_for=material)
marker = partial(venue_marker, "North Wilkesboro locator")


def section(name):
    target = bpy.data.collections.new("North Wilkesboro | " + name)
    bpy.context.scene.collection.children.link(target)
    return target


def parking_lot(target, title, center, yaw, columns, rows, rv, seed, site=None):
    rng = random.Random(seed)
    paint, vehicles = Builder(target), Builder(target)
    bay = 7.0 if rv else 3.25
    pitch = 25.0 if rv else 14.1
    length = 12.0 if rv else 5.4
    width,depth = columns*bay+11,rows*pitch+7
    rows_of_pavement = []
    for x in site_stations(-width*.5,width*.5,1.):
        row = []
        for y in (-depth*.5,depth*.5):
            p = local(center,yaw,x,y)
            row.append((*p,ground(site,p)+.021))
        rows_of_pavement.append(row)
    paint.ribbon(rows_of_pavement,"Paddock asphalt")
    colors = ("Parked navy", "Parked red", "Parked sage", "Parked silver", "RV ivory")
    for row in range(rows):
        y = (row+.5-rows/2)*pitch
        for i in range(columns+1):
            x = (i-columns/2)*bay
            p = local(center,yaw,x,y)
            paint.box(p,ground(site,p)+.027,.115,length,.005,yaw,"Parking paint")
        for i in range(columns):
            x = (i+.5-columns/2)*bay
            initial = len(vehicles.vertices)
            stall = local(center,yaw,x,y)
            if not rv:
                park_car(vehicles,stall,yaw,rng)
            elif rng.random() <= .77:
                xy = local(center, yaw, x+rng.uniform(-.26,.26), y+rng.uniform(-.52,.52))
                heading = yaw+math.pi*.5+rng.uniform(-.045,.045)+(math.pi if (row+i)%3 else 0)
                parking_vehicle(vehicles, xy, heading, True, rng.choice(colors))
            z = ground(site,stall)+.002
            for v in range(initial,len(vehicles.vertices)):
                a,b,c = vehicles.vertices[v]
                vehicles.vertices[v]=(a,b,c+z)
    paint.bake("North Wilkesboro " + title + " paving and spaces")
    vehicles.bake("North Wilkesboro " + title + " parked vehicles")


def text(target, point, yaw, label, size, width, color, font="arialbd.ttf"):
    curve = bpy.data.curves.new(label, "FONT")
    path = Path("C:/Windows/Fonts") / font
    if path.is_file():
        curve.font = bpy.data.fonts.load(str(path), check_existing=True)
    curve.body, curve.size = label, size
    curve.align_x, curve.align_y = "CENTER", "CENTER"
    curve.extrude = .003
    item = bpy.data.objects.new(label, curve)
    target.objects.link(item)
    bpy.context.view_layer.update()
    fit = min(1.,width/max(item.dimensions.x,.02))
    depsgraph = bpy.context.evaluated_depsgraph_get()
    mesh = bpy.data.meshes.new_from_object(item.evaluated_get(depsgraph))
    baked = bpy.data.objects.new("North Wilkesboro lettering "+label, mesh)
    target.objects.link(baked)
    baked.location = point
    baked.rotation_euler = (Matrix.Rotation(yaw,4,'Z')@Matrix.Rotation(math.pi*.5,4,'X')).to_euler()
    baked.scale = (fit,fit,fit)
    baked.data.materials.append(material(color))
    bpy.data.objects.remove(item, do_unlink=True)
    bpy.data.curves.remove(curve)


def front_oval(model, center, yaw, x, y, z, w, h, color, points=48):
    model.quad([(*local(center,yaw,x+w*.5*math.cos(i*math.tau/points),y),
                z+h*.5*math.sin(i*math.tau/points)) for i in range(points)],color)


def rails(model, world, x1, y1, x2, y2, floor, color):
    length = math.dist((x1,y1),(x2,y2))
    for rail in (.44,1.10):
        model.brace((*world(x1,y1),floor+rail),(*world(x2,y2),floor+rail),.072,color)
    posts = math.ceil(length/.98)
    for i in range(posts+1):
        t = i/posts
        xy = world(layout.lerp(x1,x2,t),layout.lerp(y1,y2,t))
        model.box(xy,floor,.052,.052,1.13,0.,color)


def press_building(target, anchors, center, yaw, width, depth, floor):
    model = Builder(target)

    def world(x,y):
        return local(center,yaw,x,y)

    def block(x,y,b,w,d,h,color):
        model.box(world(x,y),floor+b,w,d,h,yaw,color)

    front = -depth*.5
    block(0,0,-.14,width+12,depth+10,.14,"Paddock concrete")
    block(0,0,0,width,depth,3.45,"Venue red")
    block(0,.43,3.45,width-.65,depth-.92,6.28,"Venue white")
    block(0,front+.40,3.68,width-.94,1.0,4.45,"Stadium glass")
    block(0,front-.145,3.57,width-.54,.16,.17,"Fixture steel")
    block(0,front-.16,8.05,width-.58,.16,.14,"Venue white")
    for i in range(1,math.ceil(width/2.0)):
        x = (i/math.ceil(width/2.0)-.5)*(width-.98)
        block(x,front-.115,3.65,.082,.105,4.40,"Venue white")
    block(0,front-.244,8.23,width+.1,.33,1.98,"Venue white")
    block(0,-.26,10.07,width+.96,depth+1.54,.27,"Venue red")
    block(0,-.26,10.243,width+.93,depth+1.50,.098,"Section concrete")

    block(0,-.34,14.74,width-.86,depth-.13,.33,"Venue red")
    block(0,-.34,15.061,width-.89,depth-.16,.081,"Section concrete")
    for i in range(11):
        x = (i/10-.5)*(width-2.6)
        for y in (front+.45,-.25,depth*.5-1.15):
            block(x,y,10.30,.32,.44,4.64,"Venue red")
            block(x,y,10.29,.49,.58,.20,"Fixture steel")
    for z,w,dy in ((10.341,width+.86,depth*.5+.73),(15.142,width-.94,depth*.5-.13)):
        hf = w*.5
        for x1,y1,x2,y2 in ((-hf,-dy,hf,-dy),(-hf,dy,hf,dy),
                             (-hf,-dy,-hf,dy),(hf,-dy,hf,dy)):
            rails(model,world,x1,y1,x2,y2,floor+z,"Fixture steel")

    for x in (-width*.5+2.4,width*.5-2.1):
        block(x,depth*.24,0,3.6,depth*.44,10.10,"Venue white")
        block(x,depth*.23,10.20,3.60,depth*.44,4.52,"Venue red")
        block(x,depth*.23,14.34,3.3,depth*.40,.89,"Venue white")
    # The industrial front stair returns at a landing before the open balcony.
    x = -width*.5+3.5
    sy = front-.22
    for base,direction in ((.18,1),(5.17,-1)):
        start = x if direction == 1 else x+6.37
        for step in range(27):
            bx = start + direction*(step+.5)*.235
            z = base+(step+1)*.184
            block(bx,sy,z-.105,.244,1.46,.105,"Fixture steel")
        for side in (-1,1):
            y = sy+side*.70
            for dz in (.35,1.04):
                model.brace((*world(start,y),floor+base+dz),
                            (*world(start+direction*6.345,y),floor+base+4.97+dz),.080,"Venue red")
    for x1,b in ((x+6.81,4.99),(x-.49,10.16)):
        block(x1,sy,b,1.12,1.61,.17,"Section concrete")
    model.bake("North Wilkesboro pressed-steel and glass press building")

    for label,x,z,size,span,paint in (
        ("THE LEGEND LIVES ON",0.,9.22,2.04,width*.61,"Venue red"),
        ("NORTH WILKESBORO",0.,2.44,1.35,width*.45,"Venue white"),
        ("SPEEDWAY",0.,1.23,1.08,width*.36,"Venue white"),
    ):
        y = front-.42 if z>8. else front-.033
        text(target,(*world(x,y),floor+z),yaw,label,size,span,paint)
    ornaments = Builder(target)
    for x in (-width*.31,width*.31):
        ornaments.quad([(*world(x+(.45 if i%2==0 else .20)*math.cos(math.pi*.5+i*math.tau/10),front-.427),
                         floor+9.20+(.45 if i%2==0 else .20)*math.sin(math.pi*.5+i*math.tau/10))
                        for i in range(10)],"Navy signage")
    ornaments.bake("North Wilkesboro press facade stars")

    people = random.Random(7311)
    idx = 2001
    for deck,rows in ((10.341,(front-.24,front+.83)),(15.142,(front+.22,front+1.40,front+2.65))):
        count = int((width-13)/1.24)
        for j,y in enumerate(rows):
            for i in range(count):
                if people.random() < (.13+j*.11):
                    continue
                x = (i+.5-count*.5)*1.24+people.uniform(-.23,.23)
                xy = world(x,y+people.uniform(-.12,.12))
                marker(anchors,f"North Wilkesboro standing fan {idx:04d}",(*xy,floor+deck+.004))
                idx += 1
    marker(anchors,"North Wilkesboro press spotter marker",(*world(0,front+.70),floor+17.45),yaw)


def scoreboard(target, anchors, center, yaw, floor):
    model = Builder(target)

    def world(x,y):
        return local(center,yaw,x,y)

    def block(x,y,h,w,d,t,color):
        model.box(world(x,y),floor+h,w,d,t,yaw,color)

    block(0,.28,-.075,18.18,7.1,.075,"Paddock concrete")
    for x in (-6.4,-2.15,2.15,6.4):
        block(x,.27,0.,.48,.57,5.32,"Fixture steel")
        block(x,.27,0.,1.43,1.48,.24,"Section concrete")
    block(0,.42,2.12,16.34,.33,.28,"Fixture steel")
    block(0,.08,3.75,16.24,.53,6.15,"Scoreboard red")
    block(0,.38,3.75,16.08,.16,6.10,"Fixture steel")
    for z in (4.18,6.34,8.55,9.67):
        block(0,.48,z,16.12,.13,.12,"Fixture steel")
    block(0,-.12,3.29,16.30,.25,1.02,"Scoreboard black")
    for row in range(3):
        for col in range(41):
            if (row+col)%2==0:
                block((col-20)*.395,-.262,3.33+row*.306,.385,.046,.30,"Scoreboard white")

    for y,w,h,paint in ((-.237,5.42,3.24,"Scoreboard white"),
                        (-.245,5.26,3.08,"Scoreboard black"),
                        (-.252,5.10,2.96,"Scoreboard cream"),
                        (-.259,4.75,2.60,"Scoreboard red")):
        front_oval(model,center,yaw,4.13,y,floor+9.20,w,h,paint)
    text(target,(*world(4.13,-.277),floor+9.24),yaw,"Tyson",1.68,4.22,"Scoreboard white","georgiab.ttf")
    text(target,(*world(-3.32,-.201),floor+8.54),yaw,"LEADERBOARD",1.46,8.93,"Scoreboard cream")

    front_plane = -.199
    for label,x,z in (("POS",-6.44,6.92),("CAR",-6.42,5.16),("LAP",5.99,6.94)):
        text(target,(*world(x,front_plane-.016),floor+z),yaw,label,.91,1.74,"Scoreboard white")

    timing_z = 5.11
    for i,x in enumerate((-4.22,-2.47,-.72,1.03,2.78),1):
        front_oval(model,center,yaw,x,front_plane-.002,floor+6.93,1.10,1.10,"Scoreboard white",24)
        text(target,(*world(x,front_plane-.016),floor+6.95),yaw,str(i),1.00,.83,"Scoreboard black")
        block(x,-.196,timing_z-.56,1.18,.073,1.12,"Scoreboard white")
        marker(anchors,f"North Wilkesboro scoreboard position {i:02d}",
               (*world(x,-.2505),floor+timing_z),yaw,width=1.18)
    block(5.96,-.196,timing_z-.56,1.76,.073,1.12,"Scoreboard white")
    marker(anchors,"North Wilkesboro scoreboard lap",(*world(5.96,-.2505),floor+timing_z),yaw,width=1.76)
    marker(anchors,"North Wilkesboro scoreboard foundation marker",(*world(0,.27),floor+.012),yaw)
    model.bake("North Wilkesboro trackside leaderboard housing")


def site_tree(objects):
    vertices, faces = [], []
    for obj in objects:
        if obj.type != "MESH":
            continue
        offset = len(vertices)
        vertices.extend(obj.matrix_world @ v.co for v in obj.data.vertices)
        faces.extend(tuple(offset+i for i in p.vertices) for p in obj.data.polygons)
    return BVHTree.FromPolygons(vertices,faces) if faces else None


def ground(site, xy):
    if site is not None:
        point, normal, _, _ = site.ray_cast((*xy,80.),(0,0,-1),180.)
        if point is not None and normal.z>.35:
            return point.z
    return layout.site_height(*xy)


def outer(station, offset):
    p = layout.race_pose(station)
    x,y,_ = layout.racing_surface(station,p.width*.5)
    return x+p.nx*offset,y+p.ny*offset


def frontage_frame(station, offset):
    p = layout.race_pose(station)
    return outer(station,offset), math.atan2(-p.nx,p.ny)


def site_stations(start,end,step):
    count = max(1,math.ceil((end-start)/step))
    return [layout.lerp(start,end,i/count) for i in range(count+1)]


def grandstand(target, anchors, station, width, rows, year_index, site, bed, upper_tier):
    p = layout.race_pose(station)
    row_pitch = 1.07
    setback = 6.8
    center,yaw = frontage_frame(station,setback)
    first = max(ground(site,center)+1.62,layout.racing_surface(station,p.width*.5)[2]+1.66)
    model = Builder(target)

    def world(x,y):
        return local(center,yaw,x,y)

    model.box(world(0,(rows*row_pitch+6)*.48),ground(site,center)-.13,
              width+1.36,rows*row_pitch+6.2,.19,yaw,"Paddock concrete")
    for deck,count,origin,rise in ((0,rows,(0.,first),.55),
                                    (1,max(1,rows-5),(6.7,first+7.8),.68)):
        if deck and not upper_tier:
            break
        y_start,z_start = origin
        for i in range(count):
            row = i+(rows if deck else 0)
            y = y_start+i*row_pitch
            z = z_start+i*rise
            w = width*.5-.92
            for side in (-1,1):
                x = (width*.25+.34)*side
                model.box(world(x,y),z-.43,w+.07,row_pitch+.016,.44,yaw,"Section concrete")
                model.box(world(x,y+.10),z,w,.35,.24,yaw,"Bench aluminium")
                model.box(world(x,y+.30),z+.19,w,.087,.26,yaw,bed)
                marker(anchors,f"North Wilkesboro grandstand bench {year_index:03d}-{row:02d}-{side+1}",
                       (*world(x,y+.10),z+.245),yaw,width=w)
            for step in range(3):
                model.box(world(0,y+(-1+step)*row_pitch/3),z-rise+rise*(step+1)/3-.20,
                          1.18,row_pitch/3+.002,.20,yaw,"Section concrete")
        rear_y = y_start+(count-.15)*row_pitch
        rear_z = z_start+(count-1)*rise
        rails(model,world,-width*.5,rear_y,width*.5,rear_y,rear_z,"Bench aluminium")
        for i in range(3):
            x = (i-1)*width*.42
            q = world(x,rear_y-.48)
            z0 = ground(site,q)
            model.box(q,z0-.07,.55,.61,rear_z-z0+.07,yaw,"Fixture steel")
            model.brace((*world(x,y_start+1.4),z_start-.38),(*q,rear_z-.50),.43,"Fixture steel")
            model.brace((*world(x,y_start+1.4),ground(site,world(x,y_start+1.4))),
                        (*q,rear_z-.50),.24,"Fixture steel")
    model.bake(f"North Wilkesboro grandstand section {year_index:03d}")


def concrete_frontage(target, start, end, width, site, suffix):
    paving = Builder(target)
    for small,large in ((.46,.62),(.62,5.6),(5.6,width)):
        points = []
        for station in site_stations(start,end,.78):
            p = layout.race_pose(station)
            edge = layout.racing_surface(station,p.width*.5)[2]
            row = []
            foot = ground(site,outer(station,5.6))+.027
            for offset in (small,large):
                xy = outer(station,offset)
                grade = layout.lerp(edge+.012,foot,min(1.,max(0.,(offset-.62)/4.98)))
                row.append((*xy,max(grade,ground(site,xy)+.012)))
            points.append(row)
        paving.ribbon(points,"Paddock concrete")
    paving.bake("North Wilkesboro concrete concourse "+suffix)


def stand_run(target, anchors, start, end, rows, sections, bed, upper_tier, site, first_number):
    concrete_frontage(target,start,end,rows*1.07+(20. if upper_tier else 14.),site,str(first_number))
    step = (end-start)/sections
    for i in range(sections):
        grandstand(target,anchors,start+(i+.5)*step,step*.94,rows,first_number+i,site,bed,upper_tier)
    return first_number+sections


def turn_grandstands(target, anchors, site):
    start, end = layout.marker(475., 302.), layout.marker(477., 155.)
    rows = 24
    concrete_frontage(target, start, end, rows * 1.07 + 20., site, "9")
    curved_grandstand(
        target, anchors, start=start, end=end, rows=rows, sections=10, first_number=9,
        venue="North Wilkesboro", builder=Builder, marker=marker,
        outer=outer, frontage_frame=frontage_frame, ground=lambda xy: ground(site, xy),
        track_edge=lambda s: layout.racing_surface(s, layout.race_pose(s).width * .5)[2],
        nearest_station=layout.nearest_race, site_stations=site_stations, seat_material="Seat garnet",
    )


def booth(target, station, width, rows, site, number):
    center,yaw = frontage_frame(station,rows*1.07+7.25)
    p = layout.race_pose(station)
    base = max(ground(site,outer(station,6.8))+1.62,layout.racing_surface(station,p.width*.5)[2]+1.66)
    z = base+(rows-1)*.55+1.35
    model = Builder(target)
    height = 3.6
    depth = 6.3
    model.box(center,z,width,depth+.68,.40,yaw,"Section concrete")
    model.box(center,z+.37,width-.5,depth,height,yaw,"Venue white")
    model.box(local(center,yaw,0.,-depth*.5),z+1.12,width-.97,.17,1.93,yaw,"Stadium glass")
    model.box(center,z+height+.34,width+.42,depth+.57,.28,yaw,"Metal roof")
    for i in range(1,math.ceil(width/2)):
        x = (i/math.ceil(width/2)-.5)*(width-.94)
        model.box(local(center,yaw,x,-depth*.5-.103),z+1.10,.063,.06,1.96,yaw,"Venue white")
    for side in (-1,1):
        for along in (-.37,.37):
            xy = local(center,yaw,along*width,side*depth*.33)
            floor = ground(site,xy)
            model.box(xy,floor-.08,.53,.53,z-floor+.17,yaw,"Fixture steel")
    model.bake(f"North Wilkesboro elevated frontstretch press booth {number:02d}")


def hauler_yard(target, anchors, center, yaw, site):
    paving, paint = Builder(target),Builder(target)

    def world(x,y):
        return local(center,yaw,x,y)

    def surface(x,y):
        xy = world(x,y)
        return (*xy,ground(site,xy)+.026)

    def stripe(a,b):
        dx, dy = layout.unit(layout.sub(b,a))
        length = math.dist(a,b)
        rows = []
        for along in site_stations(0.,length,.85):
            row = []
            for side in (-.048,.048):
                x,y,z = surface(a[0]+dx*along-dy*side,a[1]+dy*along+dx*side)
                row.append((x,y,z+.008))
            rows.append(row)
        paint.ribbon(rows,"Parking paint")

    cols,pitch = 20,4.8
    hauler_row,car_row,aisle = 23.1,8.8,39.5
    length = cols*pitch
    paving.ribbon([[surface(x,-aisle),surface(x,aisle)]
                   for x in site_stations(-length*.5-12,length*.5+12,.82)],"Paddock asphalt")
    for side in (-1,1):
        for j in range(cols):
            x = (j+.5-cols*.5)*pitch
            number = j+1+(cols if side>0 else 0)
            nose = yaw+(math.pi if side<0 else 0)
            marker(anchors,f"North Wilkesboro hauler stall {number:02d}",surface(x,side*hauler_row),nose)
            marker(anchors,f"North Wilkesboro hauler garage bay {number:02d}",surface(x,side*car_row),nose+math.pi)
            marker(anchors,f"North Wilkesboro hauler garage access {number:02d}",surface(x,0),nose+math.pi)
        for j in range(cols+1):
            x = j*pitch-length*.5
            for cy,depth in ((hauler_row,20.68),(car_row,5.74)):
                stripe((x,side*cy-depth*.5),(x,side*cy+depth*.5))
        for cy,depth in ((hauler_row,20.68),(car_row,5.74)):
            y = side*(cy-depth*.5 if cy==hauler_row else cy+depth*.5)
            stripe((-length*.5,y),(length*.5,y))
    paving.bake("North Wilkesboro hauler and race-car parking asphalt")
    paint.bake("North Wilkesboro 40 hauler and behind-hauler retirement bays")
    return surface


def service_building(target, center, yaw, width, depth, height, site, name, doors, accent):
    model = Builder(target)
    corners = [local(center,yaw,x,y) for x in (-width*.5,width*.5) for y in (-depth*.5,depth*.5)]
    base = max(ground(site,p) for p in corners)+.035

    def block(x,y,z,w,d,h,color):
        model.box(local(center,yaw,x,y),base+z,w,d,h,yaw,color)

    minimum = min(ground(site,p) for p in corners)
    block(0,0,minimum-base-.08,width+5.8,depth+5.8,base-minimum+.08,"Paddock concrete")
    block(0,0,0,width,depth,height,"Venue white")
    block(0,0,height-.11,width+.58,depth+.60,.27,"Metal roof")
    for side in (-1,1):
        block(0,side*(depth*.5+.035),height-.81,width+.15,.10,.43,accent)
        for i in range(doors):
            x = (i+.5-doors*.5)*width/(doors+.35)
            spacing = width/(doors+.35)
            block(x,side*(depth*.5+.025),.08,spacing*.75,.105,2.81,"Charcoal panels")
            block(x,side*(depth*.5+.084),.23,spacing*.65,.055,2.46,"Light standing seam")
            for z in (.60,1.13,1.65,2.16):
                block(x,side*(depth*.5+.119),z,spacing*.64,.014,.041,"Metal roof")
    for x in (-width*.24,width*.25):
        block(x,depth*.18,height+.17,1.88,1.31,.62,"Fixture steel")
        block(x,depth*.18,height+.79,1.76,1.22,.078,"Light standing seam")
    model.bake("North Wilkesboro "+name)


def garage_roads(target, anchors, yard_site, site):
    gates = layout.PIT_GARAGE_GATES
    if len(gates)!=2:
        raise ValueError("North Wilkesboro needs two mapped pit-to-hauler connections")
    entry_gate,exit_gate = (layout.pit_pose(station) for station,_ in gates)
    start = layout.pit_pose(max(.8,gates[0][0]-19.))
    end = layout.pit_pose(min(layout.PIT_LENGTH-.8,gates[1][0]+16.))
    if end.tx<.24:
        raise ValueError("Garage exit must reach the frontstretch pit lane before its return")
    start_pt,end_pt = (start.x,start.y),(end.x,end.y)
    entry_cross = entry_gate.offset(side=layout.PIT_WORK_INNER)[:2]
    exit_cross = exit_gate.offset(side=layout.PIT_WORK_INNER)[:2]
    a,b,z = yard_site(-57.,0.)
    yard_end = a,b
    junction = layout.diagram_xy
    configs = (
        ("ingress", [start_pt,entry_cross,junction(270.,261.),junction(301.,267.),
                     junction(352.,270.),junction(386.,274.),junction(400.,272.),yard_end],
         (start.tx,start.ty),(0.,1.),start.z+.007,z),
        ("egress", [yard_end,junction(405.2,267.),junction(415.,266.2),junction(424.9,266.7),exit_cross,end_pt],
         (0.,-1.),(end.tx,end.ty),z,end.z+.007),
    )
    paths = []
    for name,controls,heading_start,heading_end,z_start,z_end in configs:
        road = Builder(target)
        first = layout.sub(controls[0],layout.mul(heading_start,13.))
        last = layout.add(controls[-1],layout.mul(heading_end,13.))
        points = layout.catmull([first,*controls,last],False,22)[22:-22]
        path = layout.Path(points)
        paths.append(path)
        pavement_stations = site_stations(0.,path.length,.68)

        def surface(distance,side):
            x,y = path.at(distance)
            tx,ty = path.heading(distance)
            from_blend = max(0.,1-distance/33.)
            to_blend = max(0.,1-(path.length-distance)/25.)
            point = (x-ty*side,y+tx*side)
            local_grade = ground(site,point)+.026
            height = layout.lerp(local_grade,z_start,layout.ease(from_blend))
            height = layout.lerp(height,z_end,layout.ease(to_blend))
            if 2.5 < distance < path.length-2.5:
                height = max(height,ground(site,point)+.009)
            return (*point,height)

        for sides in ((-4.2,0.),(0.,4.2)):
            road.ribbon([[surface(distance,d) for d in sides] for distance in pavement_stations],"circuit asphalt")
        road.bake(f"North Wilkesboro paved hauler garage {name}")
        for i,distance in enumerate(pavement_stations,1):
            marker(anchors,f"North Wilkesboro hauler garage {name} {i:03d}",surface(distance,0.))
    return paths


def exterior_rv_camp(target, anchors, start, end, site):
    paving, fixtures = Builder(target),Builder(target)
    count = int((end-start-14)/8.3)
    rng = random.Random(13505)

    def paved(station,offset):
        xy = outer(station,offset)
        return (*xy,ground(site,xy)+.042)

    for near,far,paint in ((4.55,8.6,"Paddock concrete"),(8.6,28.4,"Paddock asphalt")):
        paving.ribbon([[paved(s,near),paved(s,far)] for s in site_stations(start,end,.86)],paint)
    for i in range(count):
        s = start+7.0+(i+.5)*8.3
        p = layout.race_pose(s)
        x,y,z = paved(s,15.15+rng.uniform(-.35,.40))
        yaw=math.atan2(-p.ny,-p.nx)+rng.uniform(-.06,.06)
        # The stock RV mesh has a ground-relative origin. Offset its vertices
        # onto this campground's real motorcoach pad height.
        start_vertex = len(fixtures.vertices)
        parking_vehicle(fixtures,(x,y),yaw,True,rng.choice(CAR_PAINT))
        for v in range(start_vertex,len(fixtures.vertices)):
            a,b,c = fixtures.vertices[v]
            fixtures.vertices[v]=(a,b,c+z-.020)
        parking_side = outer(s,21.4)
        model_side = math.atan2(p.ty,p.tx)
        base = ground(site,parking_side)+.045
        fixtures.box(parking_side,base,2.1,.59,.12,model_side,"Paddock masonry")
        if i%3!=1:
            for offset in (-1.25,1.45):
                xy = outer(s+offset,22.5)
                marker(anchors,f"North Wilkesboro standing fan {3001+2*i+int(offset>0):04d}",
                       (*xy,ground(site,xy)+.046))
    paving.bake("North Wilkesboro exterior RV perimeter walk and camp paving")
    fixtures.bake("North Wilkesboro outside backstretch RV camping")


def pace_service(target, anchors):
    gate_station = 314.6
    gate = layout.race_pose(gate_station)
    tangent = (gate.tx,gate.ty)
    inward = (-gate.nx,-gate.ny)
    outward = (gate.nx,gate.ny)
    way_in,way_out = -4.,4.

    def gate_point(a,d):
        return gate.offset(along=a,side=-17.5-d)[:2]

    def apron(s):
        p = layout.race_pose(s)
        return layout.racing_surface(s,-p.width*.5-p.apron*.51)[:2],(p.tx,p.ty)

    incoming,track_in = apron(295.)
    outgoing,track_out = apron(333.)
    first,last = gate_point(way_in,0.),gate_point(way_out,0.)
    bay = gate_point(3.,22.)
    approach = layout.tangent_blend(incoming,track_in,first,inward,.19)
    inside = layout.tangent_blend(first,inward,bay,tangent,.21)
    corner = gate_point(11.,16.)
    around = layout.tangent_blend(bay,tangent,corner,outward,.22)
    depart = layout.tangent_blend(corner,outward,last,outward,.19)
    join = layout.tangent_blend(last,outward,outgoing,track_out,.22)

    model = Builder(target)
    z = .012
    model.box(bay,z-.055,8.7,5.35,.055,math.atan2(tangent[1],tangent[0]),"Paddock asphalt")
    for d in (19.55,24.45):
        p = gate_point(3.,d)
        model.box(p,z+.006,7.15,.115,.008,math.atan2(tangent[1],tangent[0]),"Stair nosing")
    for name,points in (("return",layout.chain(approach,inside)),
                        ("departure",layout.chain(around,depart,join))):
        path = layout.Path(points)
        rows = []
        for i,s in enumerate(site_stations(0.,path.length,.72),1):
            x,y = path.at(s)
            tx,ty = path.heading(s)
            rows.append([(x-ty*d,y+tx*d,z) for d in (-3.12,3.12)])
            marker(anchors,f"North Wilkesboro pace {name} {i:02d}",(x,y,z))
        model.ribbon(rows,"circuit asphalt")
    marker(anchors,"North Wilkesboro pace bay",(*bay,z),math.atan2(-tangent[0],tangent[1]))
    model.bake("North Wilkesboro pace vehicle bay and Turn Two connector")


def emergency_recovery(target, anchors, site):
    """Enter from the forward pit entrance and return via the downstream Turn Two gate."""
    paving, stripes = Builder(target), Builder(target)
    picture = layout.diagram_xy

    def point3(xy):
        return (*xy,ground(site,xy)+.016)

    def route(name, path, visible_from=0.):
        for i,distance in enumerate(site_stations(0.,path.length,1.8),1):
            marker(anchors,f"North Wilkesboro {name} {i:03d}",point3(path.at(distance)))
        rows = []
        for distance in site_stations(visible_from,path.length,.75):
            x,y = path.at(distance)
            tx,ty = path.heading(distance)
            rows.append([point3((x-ty*d,y+tx*d)) for d in (-3.20,3.20)])
        paving.ribbon(rows,"Paddock asphalt")

    # The divider already separates the main apron from the western portal.
    # Stay on the existing forward pit approach until adjacent to the opening.
    split = layout.ENTRY.nearest(picture(308.,171.))
    approach = [layout.ENTRY.at(d) for d in site_stations(0.,split,1.2)]
    inlet = [approach[-1], *[picture(x,y) for x,y in (
        (301.,172.4),(296.,175.0),(292.0,183.6),(294.0,187.9),
        (308.,188.),(331.,188.),(351.4,188.),(357.7,191.3),(355.0,196.3),(347.0,196.1))]]
    inlet_ghost = layout.sub(approach[-1],layout.mul(layout.ENTRY.heading(split),5.))
    inlet_tail = layout.sub(layout.mul(inlet[-1],2),inlet[-2])
    entry = layout.Path(layout.chain(approach,layout.catmull([inlet_ghost,*inlet,inlet_tail],False,16)[16:-16]))
    route("recovery entry",entry,split-2.)

    east = layout.race_pose(314.6)
    crossing = east.offset(along=4.5,side=-17.5)[:2]
    p = layout.race_pose(333.5)
    lane_end = layout.racing_surface(333.5,-p.width*.5-p.apron*.51)[:2]
    outside = (east.nx,east.ny)
    join = layout.tangent_blend(crossing,outside,lane_end,(p.tx,p.ty),.21)
    yard_exit = [picture(x,y) for x,y in (
        (360.3,221.6),(367.6,218.5),(369.8,209.7),(369.8,191.2),(370.1,177.9),
        (380.,173.45),(411.,173.42),(437.,176.0),(448.,177.4),
        (452.6,181.6),(454.7,186.9),(458.9,189.0),(463.5,187.9))]
    yard_exit.append(crossing)
    outlet_ghost = layout.sub(layout.mul(yard_exit[0],2),yard_exit[1])
    outlet_tail = layout.add(crossing,layout.mul(outside,5.))
    inside = layout.catmull([outlet_ghost,*yard_exit,outlet_tail],False,16)[16:-16]
    exit_route = layout.Path(layout.chain(inside,join))
    route("recovery exit",exit_route)

    # Each tow sets down a wreck in an independent parking slot, then
    # continues into the clear south apron before joining the exit aisle.
    for i in range(40):
        x = 303.0+(i-19.5)*3.0/layout.METERS_PER_PIXEL
        for name,y in (("approach",194.8),("bay",205.3),("depart",218.1)):
            marker(anchors,f"North Wilkesboro recovery {name} {i+1:02d}",point3(picture(x,y)),math.pi)
        for y in (201.3,208.7):
            xy = picture(x,y)
            stripes.box(xy,ground(site,xy)+.018,2.89,.083,.005,0.,"Parking paint")
        xy = picture(x-1.5/layout.METERS_PER_PIXEL,205.)
        stripes.box(xy,ground(site,xy)+.017,.083,10.2,.005,0.,"Parking paint")
    for i,x in enumerate((350.7,355.4,360.1),1):
        xy = picture(x,230.3)
        title = "ambulance bay 01" if i == 1 else f"tow bay {i-1:02d}"
        marker(anchors,f"North Wilkesboro {title}",point3(xy),0.)
        stripes.box(xy,ground(site,xy)+.018,3.45,.11,.005,0.,"Parking paint")
    response = [picture(362.2,226.8),picture(365.1,223.4),picture(367.6,218.5),
                *yard_exit[2:]]
    response_path = layout.Path(layout.chain(layout.catmull(response,False,16),join))
    route("response outbound",response_path)
    paving.bake("North Wilkesboro emergency access and recovery paving")
    stripes.bake("North Wilkesboro safety vehicle parking and 40 recovery bays")


def venue_lights(target, anchors, site, press_center, rv_bounds):
    fixtures = Builder(target)
    for i in range(30):
        s = 15.2+i*layout.LAP/30.
        p = layout.race_pose(s)
        camping = rv_bounds[0]<=s<=rv_bounds[1]
        location = outer(s,9.17 if camping else 11.7)
        height = ground(site,location)
        yaw = math.atan2(p.ty,p.tx)
        base_width = .88 if camping else 1.60
        floodlight(fixtures, location, yaw, height, base=(0., base_width, base_width, .34),
                   pole=(.33, .30, .30, 20.40), crossbar=(20.61, 2.80, .48, .34),
                   lenses=(20.55, .68, .36, .36), offsets=(-.94, 0, .94), setback=-.18)
        p0 = layout.racing_surface(s,-p.width*.2)
        marker(anchors,f"North Wilkesboro floodlight {i+1:02d}",(*location,height+20.95),yaw,aim=p0)
    for i,(px,py) in enumerate(((251,233),(274,226),(322,255),(365,217),(340,228),(437,251),
                                 (436,238),(457,235),(444,235),(272,243)),1):
        xy = layout.diagram_xy(px,py)
        z = ground(site,xy)
        fixtures.box(xy,z,.51,.51,.17,0.,"Paddock concrete")
        fixtures.box(xy,z+.17,.15,.15,5.32,0.,"Fixture steel")
        head = (xy[0]+.83,xy[1])
        fixtures.box((xy[0]+.45,xy[1]),z+5.31,.96,.17,.16,0.,"Fixture steel")
        fixtures.box(head,z+5.22,.43,.33,.16,0.,"Fixture lens")
        marker(anchors,f"North Wilkesboro street lamp {i:02d}",(*head,z+5.18),
               aim=(head[0]+1.7,head[1],z))
    center, yaw, width, depth, floor = press_center
    for i,x in enumerate((-width*.34,width*.34),31):
        xy = local(center,yaw,x,-depth*.5+1.15)
        z = floor+15.14
        fixtures.box(xy,z,.19,.19,2.11,yaw,"Fixture steel")
        fixtures.box(xy,z+2.07,1.47,.29,.32,yaw,"Fixture lens")
        s = layout.nearest_race(xy)
        aim=layout.racing_surface(s,0.)
        marker(anchors,f"North Wilkesboro floodlight {i:02d}",(*xy,z+2.17),yaw,aim=aim)
    fixtures.bake("North Wilkesboro venue and press roof lights")


def press_building_site(site):
    center = layout.diagram_xy(231.5, 305.4)
    pose = layout.race_pose(layout.marker(231.5, 305.4))
    return center, math.atan2(-pose.nx, pose.ny), 49., 15.2, ground(site, center)


def rv_stations():
    return layout.marker(448., 139.), layout.marker(231., 147.)


def spectator_parking(target, site):
    parking_lot(target, "frontstretch spectator parking", layout.diagram_xy(384., 363.),
                0., 23, 2, False, 1151, site)


def pit_visitors(target, anchors, site, walls, garage_paths):
    furnishings = Builder(target)
    rng = random.Random(6919)
    width = getattr(layout,"PIT_WALK_INNER",-15.25)
    gates = [s for s,_ in layout.PIT_GARAGE_GATES]

    def protected(xy):
        if walls is None:
            return False
        station = layout.nearest_race(xy)
        p = layout.racing_surface(station,-layout.WIDTH*.45)
        a = Vector((*xy,ground(site,xy)+.57))
        b = Vector((p[0],p[1],p[2]+.57))
        return walls.ray_cast(a,b-a,(b-a).length)[0] is not None

    fans = 5001
    routes = 1
    for s in site_stations(22.,layout.PIT_LENGTH-23.,18.5):
        if any(abs(s-gate)<24. for gate in gates):
            continue
        pose = layout.pit_pose(s)
        point = pose.offset(side=width-3.45)[:2]
        if not protected(point) or any(math.dist(path.at(path.nearest(point)),point)<9. for path in garage_paths):
            continue
        yaw = math.atan2(pose.ty,pose.tx)
        z = ground(site,point)
        furnishings.box(point,z-.014,5.65,5.70,.042,yaw,"Paddock concrete")
        if routes%4==0:
            for dx in (-1.35,0,1.35):
                p = local(point,yaw,dx,.43)
                furnishings.box(p,z+.029,1.15,1.19,2.02,yaw,"Blue trim")
                furnishings.box(p,z+2.047,1.20,1.22,.11,yaw,"Venue white")
                furnishings.box(local(p,yaw,0.,-.61),z+.119,.76,.054,1.82,yaw,"Charcoal panels")
        elif routes%2==0:
            furnishings.box(point,z+.028,3.42,2.04,1.18,yaw,"Venue red")
            furnishings.box(local(point,yaw,0.,-.91),z+1.10,3.31,.54,.13,yaw,"Light standing seam")
            furnishings.box(local(point,yaw,0.,.71),z+1.21,3.29,.41,1.21,yaw,"Venue white")
            for dx in (-1.56,1.56):
                furnishings.box(local(point,yaw,dx,-.83),z+1.19,.112,.112,1.28,yaw,"Fixture steel")
            furnishings.box(point,z+2.42,3.64,2.27,.18,yaw,"Venue red")
        for x in (-2.2,2.21):
            p = local(point,yaw,x,-1.17)
            furnishings.box(p,z+.029,.52,.50,.78,yaw,"Charcoal panels")
            furnishings.box(p,z+.798,.57,.57,.078,yaw,"Bench aluminium")
        for i in range(3):
            xy = local(point,yaw,(i-1)*1.06+rng.uniform(-.13,.13),-2.2+rng.uniform(-.11,.11))
            marker(anchors,f"North Wilkesboro standing fan {fans:04d}",(*xy,z+.030))
            fans += 1
        offset = (width+getattr(layout,"PIT_WORK_INNER",-12.2))*.5
        for label,ds in (("start",-5.2),("end",5.2)):
            p = layout.pit_pose(s+ds).offset(side=offset)[:2]
            marker(anchors,f"North Wilkesboro pedestrian walk {routes:02d} {label}",(*p,ground(site,p)+.012),yaw)
        routes += 1
    furnishings.bake("North Wilkesboro pit walk concessions restrooms and bins")


def ground_spectators(anchors):
    markers = set(anchors.objects)
    scenery = site_tree(o for o in bpy.context.scene.objects
                        if o.type=="MESH" and o not in markers)
    if scenery is None:
        return
    for obj in anchors.objects:
        if not obj.name.startswith("North Wilkesboro standing fan "):
            continue
        point, normal, _, _ = scenery.ray_cast(obj.location+Vector((0,0,.19)),(0,0,-1),.46)
        if point is not None and normal.z>.32:
            obj.location.z = point.z+.004


def build(surfaces=None):
    if surfaces is None:
        surfaces = (o for o in bpy.context.scene.objects
                    if any(k in o.name.lower() for k in ("surface","asphalt","grass","pavement","walkway","apron","concrete"))
                    and not any(k in o.name.lower() for k in ("wall","patrol","lane mark","stall stripe")))
    floors = set(surfaces) | {o for o in bpy.context.scene.objects if
                              o.name.startswith(("Pit support apron","Pit walkway","Pit work"))}
    site = site_tree(floors)
    walls = site_tree(o for o in bpy.context.scene.objects if "wall" in o.name.lower())
    stands = section("Frontstretch and turn grandstands")
    buildings = section("Press and service buildings")
    parking = section("Paddock and camping")
    service = section("Venue access and lighting")
    anchors = section("Unity live scenery anchors")

    def picture(x,y):
        return layout.diagram_xy(x,y)

    # These sub-runs follow the separate blue annotations in the supplied PDN.
    stop = layout.marker(443.,316.)
    begin = layout.marker(262.,316.)-layout.LAP
    stand_run(stands,anchors,begin,stop,25,8,"Seat denim",False,site,1)
    turn_grandstands(stands,anchors,site)
    for i,(p,w) in enumerate((((283,325),31.),((332,325),24.),((402,326),68.)),1):
        booth(buildings,layout.marker(*p),w,25,site,i)

    station = layout.marker(231.5,305.4)
    press_xy, yaw, width, depth, floor = press_building_site(site)
    press_building(buildings,anchors,press_xy,yaw,width,depth,floor)
    concrete_frontage(stands,station-25.,station+25.,31.,site,"at press building")
    sb_xy = picture(198.5,192.1)
    adjacent = layout.race_pose(layout.marker(198.5,192.1))
    board_yaw = math.atan2(-adjacent.nx,adjacent.ny)
    scoreboard(buildings,anchors,sb_xy,board_yaw,ground(site,sb_xy))

    for name,p,angle,w,d,h,doors,paint in (
        ("technical inspection", (358.6,244.6),0.,26.,31.,5.8,4,"Venue red"),
        ("six-door service shed", (459.,217.),math.pi*.5,18.,12.6,4.9,3,"Venue red"),
        ("small service building", (439.3,189.9),-math.pi*.25,12.8,11.5,4.5,2,"Blue trim"),
    ):
        service_building(buildings,picture(*p),angle,w,d,h,site,name,doors,paint)

    orange = picture(359.,214.5)
    pylon = Builder(service)
    pylon_ground = ground(site,orange)
    pylon.box(orange,pylon_ground-.15,6.2,6.2,.21,0.,"Paddock concrete")
    pylon.bake("North Wilkesboro infield scoring pylon base")
    marker(anchors,"North Wilkesboro scoring pylon marker",(*orange,pylon_ground+.06))

    yard = hauler_yard(parking,anchors,picture(403.2,222.8),math.pi*.5,site)
    garage_paths = garage_roads(service,anchors,yard,site)
    rv_start, rv_end = rv_stations()
    exterior_rv_camp(parking,anchors,rv_start,rv_end,site)
    spectator_parking(parking,site)
    parking_lot(parking,"infield staff parking",picture(305.,244.),0.,15,2,False,1152,site)
    pit_visitors(service,anchors,site,walls,garage_paths)
    pace_service(service,anchors)
    emergency_recovery(service,anchors,site)
    venue_lights(service,anchors,site,(press_xy,yaw,width,depth,floor),(rv_start,rv_end))
    bpy.context.view_layer.update()
    ground_spectators(anchors)


