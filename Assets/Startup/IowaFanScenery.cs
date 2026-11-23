using System;
using UnityEngine;
using Part = IowaPitWorkMesh.Part;

public sealed partial class TrackPreview
{
    private const float IowaPitWalkwayOffset = 14.30f;

    private void AddIowaFanScenery()
    {
        AddIowaPitWalkwayVisitors();
        AddIowaWallSpectators();
        AddIowaRoadsideVisitors();
        AddIowaEmergencyAreas();
    }

    private void AddIowaPitWalkwayVisitors()
    {
        var start = PitPosition(pitRoute[1], 0f, pitHeights[1], pitHeights[2]);
        var end = PitPosition(pitRoute[1], 1f, pitHeights[1], pitHeights[2]);
        Vector3 direction = (end - start).normalized;
        Vector3 infield = Vector3.Cross(Vector3.up, direction) * pitWallSide;
        Quaternion fixtureFacing = Quaternion.LookRotation(-infield);
        float length = Vector3.Distance(start, end);
        var random = new System.Random(350694);
        var furnishings = new IowaPitWorkMesh();
        // Keep the garage and emergency driveways free, even where the walk crosses.
        Span(499f, 365f);
        Span(320f, 251f);
        AddIowaFanFurnishings(furnishings);

        void Span(float firstPixel, float lastPixel)
        {
            float first = length * (529f - firstPixel) / 293f;
            float last = length * (529f - lastPixel) / 293f;
            for (int lane = 0; lane < 3; lane++)
            {
                float offset = (lane - 1) * .89f;
                for (float station = first + 1f + lane * 3f; station + 4f < last; station += 10.5f)
                {
                    float origin = station + (float)random.NextDouble() * 1.7f;
                    float destination = Mathf.Min(last - .65f, origin + 5.4f + (float)random.NextDouble() * 1.9f);
                    Vector3 a = Point(origin, offset), b = Point(destination, offset);
                    int seed = random.Next();
                    if (random.Next(2) == 0) QueueIowaWalkingFan(a, b, seed);
                    else QueueIowaWalkingFan(b, a, seed);
                }
            }

            int item = 0;
            for (float station = first + 8f; station < last - 8f; station += 15f + random.Next(10))
            {
                float edge = item % 4 == 0 ? 3.10f : 2.45f;
                Vector3 floor = Point(station, edge);
                switch (item++ % 4)
                {
                    case 0:
                        AddIowaConcessionCart(furnishings, floor, fixtureFacing, station > length / 2f);
                        QueueIowaStandingFan(Point(station - .85f, 1.15f), infield, random.Next());
                        break;
                    case 1:
                        AddIowaPortableToilet(furnishings, floor, fixtureFacing);
                        AddIowaPortableToilet(furnishings, floor + direction * 1.55f, fixtureFacing);
                        break;
                    default:
                        AddIowaTrashBarrel(furnishings, floor);
                        if (item % 2 == 0) AddIowaTrashBarrel(furnishings, floor + direction * .85f);
                        break;
                }
            }
        }

        Vector3 Point(float station, float offset)
        {
            Vector3 point = start + direction * station + infield * (IowaPitWalkwayOffset + offset);
            point.y = .012f;
            return point;
        }
    }

    private void AddIowaWallSpectators()
    {
        var random = new System.Random(352621);
        AddRow(new Vector2(227,196), new Vector2(213,202), new Vector2(194,218), new Vector2(184,241),
            new Vector2(183,268), new Vector2(187,292), new Vector2(202,318), new Vector2(218,333), new Vector2(237,344));
        AddRow(new Vector2(243,336), new Vector2(270,345), new Vector2(300,351), new Vector2(339,356),
            new Vector2(381,357), new Vector2(429,357), new Vector2(475,357));
        AddRow(new Vector2(513,349), new Vector2(543,328), new Vector2(564,303), new Vector2(579,273),
            new Vector2(579,245), new Vector2(566,221), new Vector2(537,198));

        void AddRow(params Vector2[] points)
        {
            var wall = new TrackPitSpline(Array.ConvertAll(points,
                point => TrackPitSpline.Data(IowaLayoutPoint(point.x, point.y, 0f))));
            // Leave both ends open to the response access and nearby vehicles.
            for (float station = 15f; station < wall.Length - 15f; station += .97f + (float)random.NextDouble() * 1.8f)
            {
                Vector3 center = wall.Sample(station / wall.Length);
                Vector3 along = (wall.Sample((station + .4f) / wall.Length) -
                    wall.Sample((station - .4f) / wall.Length)).normalized;
                Vector3 protectedSide = Vector3.Cross(Vector3.up, along);
                float nearest = TrackUtilityGeometry.ClosestMainDistance(customLayout, lapLength, center);
                Vector3 track = TrackPoint(nearest, 0f, out _, out _);
                if (Vector3.Dot(protectedSide, track - center) > 0f) protectedSide = -protectedSide;
                Vector3 position = center + protectedSide * (.86f + (float)random.NextDouble() * .66f);
                position.y = groundHeight - .037f;
                QueueIowaStandingFan(position, -protectedSide, random.Next());
            }
        }
    }

    private void AddIowaRoadsideVisitors()
    {
        var random = new System.Random(356937);
        Walk(363f, 241f, 363f, 309f, true);
        Walk(483f, 227f, 447f, 244f, false);
        Walk(536f, 249f, 551f, 252f, false);
        Walk(363f, 165f, 395f, 165f, false);

        void Walk(float aX, float aY, float bX, float bY, bool paved)
        {
            float floor = paved ? .012f : groundHeight - .037f;
            Vector3 start = IowaLayoutPoint(aX, aY, floor);
            Vector3 end = IowaLayoutPoint(bX, bY, floor);
            float length = Vector3.Distance(start, end);
            int groups = Mathf.Max(1, Mathf.FloorToInt(length / 7f));
            for (int i = 0; i < groups; i++)
            {
                Vector3 a = Vector3.Lerp(start, end, (i + .12f) / groups);
                Vector3 b = Vector3.Lerp(start, end, (i + .87f) / groups);
                if (random.Next(2) == 0) QueueIowaWalkingFan(a, b, random.Next());
                else QueueIowaWalkingFan(b, a, random.Next());
            }
        }
    }

    private void AddIowaEmergencyAreas()
    {
        Park(1, 245, 210, 228, 190);
        Park(2, 522, 211, 531, 196);
        Park(3, 223, 326, 240, 340);
        Park(4, 490, 337, 492, 350);

        void Park(int index, float x, float y, float towardsX, float towardsY)
        {
            Vector3 center = IowaLayoutPoint(x, y, .01f);
            Vector3 facing = (IowaLayoutPoint(towardsX, towardsY, .01f) - center).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, facing);
            AddIowaResponseVehicles(center - side * 4.78f, center + side * 4.78f, facing, index);
        }
    }

    private void AddIowaFanFurnishings(IowaPitWorkMesh furnishings)
    {
        var palette = new Texture2D(IowaPitWorkMesh.PartCount, 1, TextureFormat.RGBA32, false)
        {
            name = "Iowa walkway furnishing colors", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
        };
        palette.SetPixels32(new Color32[]
        {
            new(35, 116, 177, 255), new(37, 72, 98, 255), new(173, 62, 41, 255),
            new(36, 40, 41, 255), new(146, 152, 150, 255), new(60, 68, 65, 255),
            new(224, 220, 196, 255), new(174, 113, 68, 255), new(95, 113, 72, 255),
            new(121, 75, 50, 255), new(73, 108, 116, 255)
        });
        palette.Apply(false, true);
        carPaintTextures.Add(palette);
        Material paint = MakeMaterial(Color.white);
        paint.SetTexture("_BaseMap", palette);
        paint.SetFloat("_Smoothness", .17f);
        var root = CreateObject("Iowa pit walkway amenities", transform);
        Mesh mesh = furnishings.Build(root.name, 0, 1);
        roadMeshes.Add(mesh);
        root.AddComponent<MeshFilter>().sharedMesh = mesh;
        root.AddComponent<MeshRenderer>().sharedMaterial = paint;
    }

    private static void AddIowaConcessionCart(IowaPitWorkMesh mesh, Vector3 floor, Quaternion face, bool blue)
    {
        Part color = blue ? Part.Primary : Part.Accent;
        Panel(0, .65f, 0, 2.48f, 1.10f, 1.28f, color);
        Panel(0, 1.24f, .11f, 2.60f, .105f, 1.42f, Part.Light);
        Panel(0, 1.73f, -.49f, 2.35f, .96f, .15f, Part.Visor);
        Panel(0, 2.29f, 0, 2.73f, .14f, 1.66f, color);
        for (int side = -1; side <= 1; side += 2)
        {
            Panel(side * 1.16f, 1.75f, .57f, .075f, 1.0f, .075f, Part.Metal);
            Panel(side * 1.01f, .16f, .37f, .17f, .32f, .34f, Part.Rubber);
        }
        for (int i = 0; i < 11; i++)
            Panel(-1.23f + .246f * i, 2.17f, .80f, .244f, .24f, .11f, i % 2 == 0 ? Part.Light : color);
        Panel(-.72f, 1.41f, .20f, .56f, .24f, .48f, Part.Secondary);
        Panel(.61f, 1.44f, -.03f, .51f, .30f, .44f, Part.Metal);

        void Panel(float x, float y, float z, float w, float h, float d, Part part) =>
            mesh.Box(floor + face * new Vector3(x, y, z), new Vector3(w, h, d), face, part);
    }

    private static void AddIowaPortableToilet(IowaPitWorkMesh mesh, Vector3 floor, Quaternion face)
    {
        mesh.Box(floor + Vector3.up * 1.08f, new Vector3(1.10f, 2.10f, 1.08f), face, Part.Primary);
        mesh.Box(floor + Vector3.up * 2.15f, new Vector3(1.17f, .14f, 1.14f), face, Part.Light);
        mesh.Box(floor + face * new Vector3(0f, 1.06f, .547f), new Vector3(.81f, 1.88f, .035f), face, Part.Secondary);
        mesh.Box(floor + face * new Vector3(.31f, 1.06f, .575f), new Vector3(.07f, .16f, .037f), face, Part.Metal);
        mesh.Box(floor + face * new Vector3(0f, 1.73f, .575f), new Vector3(.28f, .13f, .012f), face, Part.Light);
        mesh.Box(floor + Vector3.up * .045f, new Vector3(1.15f, .09f, 1.14f), face, Part.DarkMetal);
    }

    private static void AddIowaTrashBarrel(IowaPitWorkMesh mesh, Vector3 floor)
    {
        mesh.Cylinder(floor, floor + Vector3.up * .83f, .269f, .31f, Part.SkinMedium, 12);
        mesh.Cylinder(floor + Vector3.up * .80f, floor + Vector3.up * .89f, .32f, .293f, Part.DarkMetal, 12);
        mesh.Cylinder(floor + Vector3.up * .89f, floor + Vector3.up * .896f, .147f, .147f, Part.Rubber, 12);
    }
}
