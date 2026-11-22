"""Place Richmond's exported infield fan and walking anchors on their finished ground."""

from mathutils.bvhtree import BVHTree


WALKABLE_SURFACES = (
    'Mown grass site', 'Frontstretch grass median', 'Outer banking embankment',
    'Racing surface', 'Apron asphalt', 'Pit road asphalt', 'Pit entrance asphalt',
    'Pit exit asphalt', 'Turn one safety access asphalt', 'Turn four safety access asphalt',
    'Richmond concrete perimeter walkway', 'Richmond frontstretch concrete concourse',
    'Richmond frontstretch grandstand concourses', 'Richmond protected FanGrounds pedestrian paths',
    'Richmond pit work concrete', 'Richmond pit walkway', 'Richmond pit walkway amenity bays',
    'Pit walkway access west', 'Pit walkway access east',
    'Richmond paved safety access and recovery yard', 'Richmond infield campground roads and camping pads',
)
BUILDING_PAD_PREFIXES = (
    'Richmond 40-stall Cup garage', 'Richmond infield operations garage',
    'Richmond infield media and medical center', 'Richmond Victory Lane pavilion', 'Richmond FanGrounds ',
)
STANDING_PREFIX = 'Richmond standing fan '
WALKING_PREFIX = 'Richmond pedestrian walk '


def walking_ground(objects):
    """Index the upward faces of pavement, terrain, and building pads."""
    points, faces = [], []
    for obj in objects:
        if obj.type != 'MESH':
            continue
        is_building = obj.name.startswith(BUILDING_PAD_PREFIXES)
        if (obj.name not in WALKABLE_SURFACES and not is_building
                and not obj.name.endswith(' paving and spaces')):
            continue
        first = len(points)
        points.extend(obj.matrix_world @ vertex.co for vertex in obj.data.vertices)
        obj.data.calc_loop_triangles()
        for triangle in obj.data.loop_triangles:
            indices = tuple(first + index for index in triangle.vertices)
            a, b, c = (points[index] for index in indices)
            # Paddock buildings include their ground slabs and their roofs in
            # the same mesh. Only accept the low concrete pad as a foot surface.
            if is_building and max(a.z, b.z, c.z) > .13:
                continue
            normal = (b - a).cross(c - a)
            if normal.z > .5 * normal.length:
                faces.append(indices)
    return BVHTree.FromPolygons(points, faces, all_triangles=True)


def ground_height(ground, x, y):
    hit, _, _, _ = ground.ray_cast((x, y, 12.), (0., 0., -1.), 14.)
    return hit.z if hit is not None else None


def ground_fan_anchors(scene):
    ground = walking_ground(scene.objects)
    counts = [0, 0]
    for obj in scene.objects:
        standing = obj.name.startswith(STANDING_PREFIX)
        walking = obj.name.startswith(WALKING_PREFIX) and obj.name.endswith((' start', ' end'))
        if not (standing or walking):
            continue

        matrix = obj.matrix_world.copy()
        point = matrix.translation
        height = ground_height(ground, point.x, point.y)
        if height is None:
            raise ValueError(f'No walkable ground at {obj.name} ({point.x:.3f}, {point.y:.3f})')
        point.z = height
        matrix.translation = point
        obj.matrix_world = matrix
        counts[int(walking)] += 1

    print(f'RICHMOND GROUNDED {counts[0]} standing anchors and {counts[1]} walking endpoints')
