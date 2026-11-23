"""Shared Blender mesh, seating and fixture components for SCM venues."""
import math
import bpy
from mathutils import Vector

CAR_PAINT = (
    "Parked navy", "Parked red", "Parked sage", "Parked silver",
    "Parked graphite", "Parked bronze", "Parked ice", "Parked cobalt",
)


CAR_PROFILES = {
    "sedan": (4.58, 1.84, .84, 1.48, (-1.55, -.97, .34, 1.08)),
    "hatchback": (4.02, 1.74, .86, 1.57, (-1.82, -1.48, .38, 1.01)),
    "wagon": (4.43, 1.83, .94, 1.69, (-2.02, -1.88, .35, 1.12)),
}


class Builder:
    def __init__(self, target, material_for):
        self.target = target
        self.material_for = material_for
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

    def ribbon(self, rows, name):
        for a, b in zip(rows, rows[1:]):
            points = (a[0], a[1], b[1], b[0])
            normal = (Vector(points[1])-Vector(points[0])).cross(Vector(points[2])-Vector(points[0]))
            self.quad(points if normal.z > 0 else points[::-1], name)

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
            mesh.materials.append(self.material_for(name))
        for polygon, index in zip(mesh.polygons, self.indices):
            polygon.material_index = index
        return obj


def local(center, yaw, x, y):
    return (center[0] + x*math.cos(yaw) - y*math.sin(yaw),
            center[1] + x*math.sin(yaw) + y*math.cos(yaw))


def floodlight(model, origin, yaw, ground, *, base, pole, crossbar, lenses, offsets, setback):
    """Add the common concrete base, pole, crossbar and light heads."""
    for (height, width, depth, thickness), material in (
        (base, "Paddock concrete"), (pole, "Fixture steel"), (crossbar, "Fixture steel")
    ):
        model.box(origin, ground + height, width, depth, thickness, yaw, material)
    height, width, depth, thickness = lenses
    for offset in offsets:
        head = local(origin, yaw, offset, setback)
        model.box(head, ground + height, width, depth, thickness, yaw, "Fixture lens")


def marker(locator_name, target, name, location, yaw=0.0, width=1.0, aim=None):
    # FBX retains a real mesh more reliably than a locator. The runtime hides
    # its renderer for seating and pedestrian markers.
    mesh = bpy.data.meshes.get(locator_name)
    if mesh is None:
        mesh = bpy.data.meshes.new(locator_name)
        mesh.from_pydata([(-.5, -.025, -.025), (.5, -.025, -.025),
                          (0, .025, -.025), (0, 0, .025)], [],
                         [(2, 1, 0), (0, 1, 3), (1, 2, 3), (2, 0, 3)])
    obj = bpy.data.objects.new(name, mesh)
    target.objects.link(obj)
    obj.location = location
    obj.rotation_euler.z = yaw
    if aim is not None:
        obj.rotation_euler = (Vector(aim)-Vector(location)).to_track_quat('-Z', 'Y').to_euler()
    obj.scale = (width, 1, 1)
    return obj


def car_shell(model, center, yaw, profile, paint):
    def loft(sections, material):
        first = len(model.vertices)
        for x, half_width, bottom, top in sections:
            model.vertices.extend((*local(center,yaw,x,y),z) for y,z in
                                  ((-half_width,bottom),(half_width,bottom),
                                   (half_width,top),(-half_width,top)))
        model.add_face((first+3,first+2,first+1,first),material)
        for start in range(first,first+4*(len(sections)-1),4):
            for j in range(4):
                k = (j+1)%4
                model.add_face((start+j,start+k,start+4+k,start+4+j),material)
        end = first+4*(len(sections)-1)
        model.add_face(range(end,end+4),material)

    length, width, belt, roof, (rear, roof_rear, roof_front, front) = CAR_PROFILES[profile]
    half = length*.5
    loft(((-half,width*.43,.32,belt-.09), (-half+.24,width*.5,.23,belt),
          (half-.30,width*.5,.23,belt-.027), (half,width*.43,.32,belt-.16)),paint)
    loft(((rear,width*.43,belt-.035,belt+.055),
          (roof_rear,width*.393,belt-.035,roof-.06),
          (roof_front,width*.393,belt-.035,roof-.06),
          (front,width*.43,belt-.035,belt+.045)),"Smoked glazing")
    roof_center = (roof_rear+roof_front)*.5
    roof_length = roof_front-roof_rear
    model.box(local(center,yaw,roof_center,0.),roof-.063,roof_length,width*.79,.063,yaw,paint)
    for side in (-1,1):
        for x, color in ((half-.043,"Fixture lens"),(-half+.043,"Parked red")):
            model.box(local(center,yaw,x,side*width*.303),.49,.058,width*.208,.105,yaw,color)
        if profile == "wagon":
            model.box(local(center,yaw,roof_center,side*width*.31),
                      roof-.005,roof_length*.82,.064,.085,yaw,"Fixture steel")
    return length,width


def parking_vehicle(model, center, yaw, rv, color, car_profile="sedan"):
    if rv:
        width, depth, body_h, top = 10.1, 2.57, 2.65, 3.29
        model.box(center, .45, width, depth, body_h, yaw, "RV ivory")
        model.box(local(center, yaw, .7, 0), 1.20, width*.78, depth+.02, .22, yaw, color)
        model.box(local(center, yaw, -1.4, 0), top-.19, 2.8, 1.55, .33, yaw, "Light standing seam")
        model.box(local(center, yaw, width*.43, 0), 1.26, .22, depth*.87, 1.32, yaw, "Smoked glazing")
        for x in (-3.3, -.2, 2.1):
            model.box(local(center, yaw, x, 0), 1.8, 1.25, depth+.032, .65, yaw, "Smoked glazing")
    else:
        width, depth = car_shell(model,center,yaw,car_profile,color)
    for x in (-width*.31, width*.32):
        for sign in (-1,1):
            wheel_side = depth*(.48 if rv else .415)
            model.box(local(center, yaw, x, sign*wheel_side), .02, .78 if rv else .61,
                      .29, .78 if rv else .61, yaw, "Tires")


def park_car(model, stall, row_yaw, rng):
    center = local(stall,row_yaw,rng.uniform(-.23,.23),rng.uniform(-.31,.31))
    direction = row_yaw + rng.choice((-math.pi*.5,math.pi*.5)) + rng.uniform(-.04,.04)
    parking_vehicle(model,center,direction,False,rng.choice(CAR_PAINT),rng.choice(tuple(CAR_PROFILES)))
