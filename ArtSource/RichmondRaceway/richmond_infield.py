"""Protected infield campgrounds and spectator rows for the Richmond model."""
import math
import random

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

import richmond_layout as layout
from richmond_scenery import Builder, local, marker, parking_vehicle, section


CAMPGROUNDS = (
    ("Backstretch", 155., 55., 84., 48.),
    ("Turn Two", -132.5, 62.5, 54., 39.),
    ("Turn Three", 195., 132.5, 44., 24.),
    ("Operations", -180., 96., 20., 36.),
    ("Paddock", 172., 156., 28., 12.),
    ("East", 212., 58., 16., 20.),
)

# Short entrances join the campground lattice to existing paved access.
DRIVEWAYS = (
    ((155., 68.), (155., 89.), (152., 108.)),
    ((-115., 73.), (-115., 94.), (-115., 123.), (-103., 125.)),
    ((195., 136.4), (195., 106.), (193., 95.)),
    ((-174., 106.), (-170., 114.), (-161., 121.)),
    ((174., 159.), (183., 157.), (189., 149.), (195., 136.4)),
    ((210., 61.), (199., 61.), (190., 64.3)),
)

ROAD_WIDTH = 5.0
ROAD_Z = .029
PAD_Z = .018
GUEST_START = 600
WALL_FAN_START = 1200


def obstacle_tree(objects):
    """Keep additions off the actual road, building, and parking footprints."""
    vertices, triangles = [], []
    for obj in objects:
        if obj.type != "MESH":
            continue
        if not obj.name.startswith((
            "Racing surface", "Apron asphalt", "Pit entrance", "Pit exit", "Pit road asphalt",
            "Turn one safety", "Turn four safety", "Pit continuous stall-side", "Inside apron wall",
            "Richmond paved safety", "Richmond pit work", "Richmond pit walkway",
            "Richmond infield access", "Richmond infield operations", "Richmond infield media",
            "Richmond 40-stall", "Richmond FanGrounds", "Richmond Victory",
            "Richmond media parking", "Richmond paddock parking", "Richmond infield campground",
        )):
            continue
        start = len(vertices)
        for vertex in obj.data.vertices:
            world = obj.matrix_world @ vertex.co
            vertices.append((world.x, world.y, world.z))
        obj.data.calc_loop_triangles()
        for triangle in obj.data.loop_triangles:
            triangles.append(tuple(start + index for index in triangle.vertices))
    return BVHTree.FromPolygons(vertices, triangles, all_triangles=True)


def clear(tree, points):
    return all(tree.ray_cast((x, y, 18.), (0., 0., -1.), 18.2)[0] is None
               for x, y in points)


def road(paving, world, vertices, width=ROAD_WIDTH, height=ROAD_Z):
    edges = []
    for i, point in enumerate(vertices):
        dx, dy = layout.unit(layout.sub(vertices[min(i+1, len(vertices)-1)], vertices[max(0, i-1)]))
        edges.append([(*world(point[0] - dy * side * width / 2, point[1] + dx * side * width / 2), height)
                      for side in (-1., 1.)])
    paving.ribbon(edges, "Paddock asphalt")


def golf_cart(model, center, yaw, color):
    def box(x, y, z, w, d, h, material):
        model.box(local(center, yaw, x, y), z, w, d, h, yaw, material)
    box(0, 0, .23, 2.72, 1.24, .24, color)
    box(.83, 0, .47, .85, 1.14, .31, color)
    box(-.29, 0, .49, .55, 1.06, .20, "Charcoal panels")
    box(-.59, 0, .59, .10, 1.06, .39, "Charcoal panels")
    for x in (-.85, .74):
        for y in (-.53, .53):
            box(x, y, .09, .44, .23, .34, "Tires")
            box(x, y, .43, .055, .055, 1.22, "Fixture steel")
    box(-.08, 0, 1.64, 2.02, 1.35, .10, "Light standing seam")
    box(.76, 0, .78, .045, .98, .48, "Smoked glazing")


def grill_and_table(model, center, yaw):
    def box(x, y, z, w, d, h, material):
        model.box(local(center, yaw, x, y), z, w, d, h, yaw, material)
    for x in (-.29, .29):
        for y in (-.20, .20):
            box(x, y, .02, .062, .062, .62, "Fixture steel")
    box(0, 0, .62, .84, .57, .21, "Charcoal panels")
    box(0, 0, .83, .75, .51, .15, "Charcoal panels")
    box(.11, -.2, .975, .28, .055, .035, "Fixture steel")
    for x in (1.52, 2.54):
        box(x, 0, .04, .09, 1.32, .60, "Fixture steel")
    box(2.03, 0, .68, 1.59, .67, .073, "Paddock masonry")
    for y in (-.56, .56):
        box(2.03, y, .37, 1.59, .22, .07, "Paddock masonry")


def campers(paving, fixtures, anchors, world, yaw, tree, rng):
    guest_id = GUEST_START
    vehicles = grills = carts = 0
    trim = ("Parked red", "Parked navy", "Parked sage", "Parked silver", "Blue trim")
    for index, (_, cx, cy, width, depth) in enumerate(CAMPGROUNDS):
        columns = max(1, int((width - 5.) / 13.0))
        rows = max(1, int((depth - 3.) / 10.8))
        left, right = cx - width * .5 + 2.1, cx + width * .5 - 2.1
        row_y = [cy + (row - (rows - 1) / 2.) * 10.8 for row in range(rows)]
        for y in row_y:
            road(paving, world, [(left, y + 3.9), (right, y + 3.9)], 4.3)
        if rows > 1:
            for x in (left, right):
                road(paving, world, [(x, row_y[0] + 3.9), (x, row_y[-1] + 3.9)], 4.2, ROAD_Z + .005)
        if index == 0:
            road(paving, world, [(cx, row_y[0] + 3.9), (cx, row_y[-1] + 3.9)], 4.6, ROAD_Z + .005)
        for row, y in enumerate(row_y):
            for column in range(columns):
                x = cx + (column - (columns - 1) / 2.) * 13.0
                if index == 0 and abs(x - cx) < 7.:
                    continue  # widens the north-south access aisle through the large campground
                area = [(x + u, y + v) for u in (-5.8, 0., 5.8) for v in (-3.7, -.6, 1.45)]
                if not clear(tree, (world(*point) for point in area)):
                    continue

                # The gravel pad and campsite stay below the access aisle.
                pad = [(*world(x + u, y + v), PAD_Z) for u, v in
                       ((-5.8, -1.8), (5.8, -1.8), (5.8, 1.55), (-5.8, 1.55))]
                paving.quad(pad, "Paddock concrete")
                theme = rng.choice(trim)
                if (row + column + index) % 5 != 1:
                    parking_vehicle(fixtures, world(x, y + rng.uniform(-.25, .25)),
                                    yaw + (math.pi if (row + column) % 2 else 0.) + rng.uniform(-.035, .035),
                                    True, theme)
                    vehicles += 1
                else:
                    golf_cart(fixtures, world(x - 2.6, y - .2), yaw, theme)
                    carts += 1

                grill_and_table(fixtures, world(x - 1.8, y - 3.4), yaw)
                grills += 1
                for dx, dy in ((-3.2, -3.4), (-.3, -2.3), (2.85, -3.0)):
                    px, py = world(x + dx + rng.uniform(-.16, .16), y + dy + rng.uniform(-.15, .15))
                    if clear(tree, ((px, py),)):
                        marker(anchors, f"Richmond standing fan {guest_id:04d}", (px, py, PAD_Z))
                        guest_id += 1
                if (row + column) % 4 == 2:
                    golf_cart(fixtures, world(x + 4.2, y - 3.4), yaw + math.pi, theme)
                    carts += 1

    for line in DRIVEWAYS:
        road(paving, world, line, ROAD_WIDTH, ROAD_Z - .012)
    return vehicles, carts, grills, guest_id - GUEST_START


def wall_spectators(anchors, tree, rng):
    site = layout.RECOVERY_STATION
    gap = layout.RECOVERY_OPENING * .5
    sections = ((layout.TURN_WALL_START, site - gap), (site + gap, layout.TURN_FOUR_WALL_END))
    index = WALL_FAN_START
    for low, high in sections:
        for row, setback in enumerate((1.65, 2.9, 4.15)):
            station = low + 9.0 + row * .83
            while station < high - 9.:
                pose = layout.race_pose(station)
                offset = pose.width / 2. + pose.apron + 1.2 + setback + rng.uniform(-.18, .18)
                x, y, _ = pose.offset(side=-offset)
                spacing = .46
                envelope = ((x, y), (x - spacing, y), (x + spacing, y),
                            (x, y - spacing), (x, y + spacing))
                if rng.random() > (.13 + row * .13) and clear(tree, envelope):
                    marker(anchors, f"Richmond standing fan {index:04d}", (x, y, 0.))
                    index += 1
                station += rng.uniform(1.9, 3.65) + row * .38
    return index - WALL_FAN_START


def build_infield(anchors, origin, yaw):
    target = section("Infield campgrounds")
    paving = Builder(target)
    fixtures = Builder(target)
    rng = random.Random(44215)

    def world(x, y):
        return local(origin, yaw, x, y)

    bpy.context.view_layer.update()
    base = obstacle_tree(bpy.context.scene.objects)
    rvs, carts, grills, guests = campers(paving, fixtures, anchors, world, yaw, base, rng)
    paving.bake("Richmond infield campground roads and camping pads")
    fixtures.bake("Richmond infield campground RVs golf carts and grills")
    bpy.context.view_layer.update()
    rows = wall_spectators(anchors, obstacle_tree(bpy.context.scene.objects), rng)
    print(f"RICHMOND INFIELD {len(CAMPGROUNDS)} groups, {rvs} RVs, {carts} golf carts, {grills} grills, "
          f"{guests} campsite fans, {rows} inside-wall spectators")
