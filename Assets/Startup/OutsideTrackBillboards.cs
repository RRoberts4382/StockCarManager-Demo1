using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed partial class TrackPreview
{
    private const float BillboardWidth = 15.5f;
    private const float BillboardSetback = 4.5f;

    private static readonly (string Name, Color32 Panel, Color32 Accent)[] BillboardSponsors =
    {
        ("HAWKEYE", new Color32(25, 54, 107, 255), new Color32(255, 193, 43, 255)),
        ("PRAIRIE", new Color32(117, 38, 38, 255), new Color32(255, 201, 55, 255)),
        ("HEARTLAND", new Color32(22, 92, 72, 255), new Color32(237, 199, 83, 255)),
        ("CROSSWIND", new Color32(31, 65, 113, 255), new Color32(66, 216, 246, 255)),
        ("REDLINE", new Color32(132, 35, 43, 255), new Color32(248, 151, 43, 255)),
        ("RIDGEWAY", new Color32(89, 49, 135, 255), new Color32(155, 237, 236, 255)),
        ("BOLTWORKS", new Color32(38, 70, 90, 255), new Color32(255, 148, 48, 255)),
        ("IRON VALLEY", new Color32(30, 86, 77, 255), new Color32(250, 224, 79, 255)),
        ("FIRECREST", new Color32(133, 48, 34, 255), new Color32(249, 229, 169, 255)),
        ("TURNPOINT", new Color32(40, 53, 87, 255), new Color32(234, 89, 98, 255)),
        ("NORTHSTAR", new Color32(40, 84, 143, 255), new Color32(187, 242, 248, 255)),
        ("MILEMARK", new Color32(105, 38, 97, 255), new Color32(251, 176, 207, 255)),
        ("AXLESTONE", new Color32(65, 91, 34, 255), new Color32(228, 251, 100, 255)),
        ("SPEEDWELL", new Color32(28, 74, 121, 255), new Color32(244, 232, 205, 255)),
        ("GRAVEL RUN", new Color32(117, 52, 31, 255), new Color32(129, 222, 206, 255)),
        ("HIGHBANK", new Color32(47, 58, 111, 255), new Color32(255, 201, 93, 255))
    };

    private static readonly string[] BillboardProducts =
    {
        "RACING TIRES", "GARAGE TOOLS", "MOTOR FUEL", "BRAKE SYSTEMS",
        "SYNTHETIC OIL", "WHEELS", "RACING BATTERIES", "INTAKE SYSTEMS",
        "CAR CARE", "SPARK PLUGS", "PERFORMANCE EXHAUST", "DRIVE BELTS",
        "CHASSIS PARTS", "COOLING SYSTEMS", "RACING SEATS", "TRANSMISSION FLUID"
    };

    private static readonly Vector2[] IowaBillboardAnchors =
    {
        new Vector2(156f, 319f), new Vector2(179f, 352f), new Vector2(215f, 375f),
        new Vector2(253f, 387f), new Vector2(299f, 391f)
    };

    private void AddOutsideBillboards()
    {
        if (customLayout == null || definition.majorStyle == "Road" ||
            !Array.Exists(GetComponentsInChildren<MeshRenderer>(), renderer =>
                (renderer.name.Contains("outside", StringComparison.OrdinalIgnoreCase) ||
                    renderer.name.Contains("outer", StringComparison.OrdinalIgnoreCase)) &&
                renderer.name.Contains("wall", StringComparison.OrdinalIgnoreCase))) return;

        var sights = new OutsideBillboardObstacles(transform, cars);
        var sites = new List<IowaParkingSpace>();
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var steel = MakeMaterial(new Color32(74, 85, 92, 255));
        var paper = MakeBillboardPaint(new Color32(245, 247, 236, 255), .40f);
        var backgrounds = new Material[BillboardSponsors.Length];
        var accents = new Material[backgrounds.Length];
        string venue = definition.id == IowaTrackId ? "Iowa" : definition.name;

        if (definition.id == IowaTrackId)
        {
            foreach (Vector2 anchor in IowaBillboardAnchors)
            {
                var foot = IowaLayoutPoint(anchor.x, anchor.y, groundHeight);
                float station = TrackUtilityGeometry.ClosestMainDistance(customLayout, lapLength, foot);
                var edge = SampleOutsideEdge(station);
                var outward = Vector3.ProjectOnPlane(foot - edge.Position, Vector3.up);
                Place(foot, outward.sqrMagnitude > .01f ? outward.normalized : edge.Outward, edge.Position.y);
            }
        }

        float pitch = Mathf.Max(24f, lapLength / 120f);
        float nextStation = 0f;
        for (float station = 0f; station < lapLength; station += 2f)
        {
            if (station < nextStation) continue;
            var edge = SampleOutsideEdge(station);
            Quaternion rotation = Quaternion.LookRotation(edge.Outward, Vector3.up);
            var corridor = new IowaParkingSpace(edge.Position + edge.Outward * 12f, rotation,
                BillboardWidth * .5f + 1f, 10.6f);
            Vector3 foot = edge.Position + edge.Outward * BillboardSetback;
            foot.y = groundHeight;
            var spacing = new IowaParkingSpace(foot, rotation, BillboardWidth * .5f + 3.5f, 4f);
            if (!sights.IsClear(corridor) || sites.Exists(site => spacing.Intersects(site)) ||
                !BillboardClearsTrack(station, spacing)) continue;

            Place(foot, edge.Outward, edge.Position.y);
            nextStation = station + pitch;
        }

        void Place(Vector3 foot, Vector3 outward, float edgeHeight)
        {
            int index = sites.Count;
            int theme = index % BillboardSponsors.Length;
            var sponsor = BillboardSponsors[theme];
            string product = BillboardProducts[(theme + index / BillboardSponsors.Length) % BillboardProducts.Length];
            Material panel = backgrounds[theme] ??= MakeBillboardPaint(sponsor.Panel, .36f);
            Material accent = accents[theme] ??= MakeBillboardPaint(sponsor.Accent, .52f);
            var board = CreateObject($"{venue} track-facing billboard {index + 1:00}", transform).transform;
            board.SetLocalPositionAndRotation(foot, Quaternion.LookRotation(outward, Vector3.up));
            sites.Add(new IowaParkingSpace(foot, board.localRotation, BillboardWidth * .5f, 3.7f));
            float height = edgeHeight - foot.y + 6.85f;
            Box("Billboard panel", board, Vector3.up * height, new Vector3(BillboardWidth, 3.8f, .27f), panel);
            Box("Billboard accent stripe", board, new Vector3(0f, height - 1.70f, -.145f),
                new Vector3(BillboardWidth - .36f, .24f, .06f), accent);
            for (int side = -1; side <= 1; side += 2)
                Box("Billboard support", board, new Vector3(side * 4.65f, (height - 1.4f) * .5f, .22f),
                    new Vector3(.31f, height - 1.4f, .40f), steel);
            AddBillboardLogo(board, height, index, accent, paper, panel);
            AddBillboardText(board, "Billboard sponsor", sponsor.Name, new Vector3(1.55f, height + .43f, -.187f), 1.18f, Color.white, font);
            AddBillboardText(board, "Billboard product", product, new Vector3(1.55f, height - .67f, -.187f), .48f, sponsor.Accent, font);
        }
    }

    private Material MakeBillboardPaint(Color color, float glow)
    {
        var paint = MakeMaterial(color);
        paint.SetColor("_EmissionColor", color * glow);
        paint.EnableKeyword("_EMISSION");
        return paint;
    }

    private bool BillboardClearsTrack(float placementStation, IowaParkingSpace space)
    {
        // A flat panel can cut across the wall near a sharp or concave oval bend.
        for (float offset = -28f; offset <= 28f; offset += 2f)
        {
            var edge = SampleOutsideEdge(placementStation + offset);
            Vector3 point = edge.Position + edge.Outward * 1.2f;
            if (Mathf.Abs(Vector3.Dot(point - space.Center, space.Along)) < .72f &&
                Mathf.Abs(Vector3.Dot(point - space.Center, space.Across)) < space.HalfWidth)
                return false;
        }
        return true;
    }

    private void AddBillboardText(Transform board, string name, string line, Vector3 position,
        float height, Color color, Font font)
    {
        if (font == null) return;
        var label = CreateObject(name, board);
        label.transform.localPosition = position;
        var text = label.AddComponent<TextMesh>();
        text.font = font;
        text.anchor = TextAnchor.MiddleCenter;
        text.fontStyle = FontStyle.Bold;
        text.fontSize = 72;
        text.characterSize = 1f;
        text.color = color;
        text.text = line;
        var renderer = label.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = GetCarDecalMaterial(font);
        float scale = Mathf.Min(10.75f / Mathf.Max(.01f, renderer.localBounds.size.x),
            height / Mathf.Max(.01f, renderer.localBounds.size.y));
        label.transform.localScale = Vector3.one * scale;
    }

    private void AddBillboardLogo(Transform board, float height, int style, Material accent, Material paper, Material panel)
    {
        const float x = -5.65f;
        switch (style % 4)
        {
            case 0: // Wheel and hub.
                Disc(1.40f, -.18f, accent);
                Disc(.94f, -.25f, panel);
                Disc(.37f, -.31f, paper);
                Stripe(new Vector2(0f, 0f), new Vector2(.94f, .12f), .18f, -.32f, paper);
                Stripe(new Vector2(0f, 0f), new Vector2(-.62f, .70f), .18f, -.32f, paper);
                Stripe(new Vector2(0f, 0f), new Vector2(-.40f, -.78f), .18f, -.32f, paper);
                break;
            case 1: // Crossed hand tools.
                Disc(1.42f, -.18f, accent);
                Stripe(new Vector2(-.76f, -.76f), new Vector2(.64f, .76f), .23f, -.26f, panel);
                Stripe(new Vector2(.74f, -.72f), new Vector2(-.60f, .72f), .23f, -.29f, panel);
                Stripe(new Vector2(-.94f, .72f), new Vector2(-.28f, .72f), .29f, -.29f, panel);
                Stripe(new Vector2(.30f, .74f), new Vector2(.98f, .74f), .29f, -.26f, panel);
                break;
            case 2: // Fuel pump and hose.
                Box("Billboard logo", board, new Vector3(x - .22f, height, -.20f), new Vector3(1.48f, 2.45f, .10f), accent);
                Box("Billboard logo", board, new Vector3(x - .23f, height + .51f, -.275f), new Vector3(.99f, .56f, .09f), panel);
                Stripe(new Vector2(.44f, .20f), new Vector2(.99f, .20f), .15f, -.22f, paper);
                Stripe(new Vector2(.97f, .23f), new Vector2(.97f, -.64f), .15f, -.22f, paper);
                break;
            default: // Automotive performance chevrons.
                Stripe(new Vector2(-1.17f, .91f), new Vector2(-.23f, 0f), .37f, -.21f, accent);
                Stripe(new Vector2(-.23f, 0f), new Vector2(-1.17f, -.91f), .37f, -.21f, accent);
                Stripe(new Vector2(-.13f, .91f), new Vector2(.81f, 0f), .37f, -.21f, paper);
                Stripe(new Vector2(.81f, 0f), new Vector2(-.13f, -.91f), .37f, -.21f, paper);
                break;
        }

        void Stripe(Vector2 start, Vector2 end, float thickness, float z, Material paint)
        {
            var center = (start + end) * .5f;
            var shape = Box("Billboard logo", board, new Vector3(x + center.x, height + center.y, z),
                new Vector3((end - start).magnitude, thickness, .075f), paint);
            shape.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(end.y - start.y, end.x - start.x) * Mathf.Rad2Deg);
        }

        void Disc(float radius, float z, Material paint)
        {
            var meshBuilder = new IowaPitWorkMesh();
            var center = new Vector3(x, height, z);
            meshBuilder.Cylinder(center, center - Vector3.forward * .09f, radius, radius, IowaPitWorkMesh.Part.Primary, 24);
            Mesh mesh = meshBuilder.Build("Billboard logo", 0, 1);
            roadMeshes.Add(mesh);
            var shape = CreateObject("Billboard logo", board);
            shape.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = shape.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = paint;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
    }

    private sealed class OutsideBillboardObstacles
    {
        private const float CellSize = 2.5f;
        private readonly HashSet<Vector2Int> covered = new HashSet<Vector2Int>();

        internal OutsideBillboardObstacles(Transform root, Transform[] cars)
        {
            var raceCars = new HashSet<Transform>(cars);
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.gameObject.activeInHierarchy || !renderer.enabled || !BlocksBillboard(renderer)) continue;
                Transform group = renderer.transform;
                while (group.parent != root && group.parent != null) group = group.parent;
                if (raceCars.Contains(group)) continue;

                Bounds bounds = renderer.bounds;
                // Some imported lamp heads are one high mesh for the whole oval. Their individual masts still reserve their ground footprint.
                if (renderer.name.Contains("Floodlight lenses", StringComparison.OrdinalIgnoreCase) &&
                    root.InverseTransformPoint(bounds.min).y > 20f) continue;
                Vector3 center = root.InverseTransformPoint(bounds.center);
                var filter = renderer.GetComponent<MeshFilter>();
                var mesh = filter != null ? filter.sharedMesh : null;
                if (mesh != null && mesh.isReadable && Mathf.Max(bounds.size.x, bounds.size.z) > 13f)
                {
                    Matrix4x4 pose = root.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                    Vector3[] vertices = mesh.vertices;
                    for (int i = 0; i < vertices.Length; i++) vertices[i] = pose.MultiplyPoint3x4(vertices[i]);
                    int[] triangles = mesh.triangles;
                    for (int i = 0; i < triangles.Length; i += 3)
                        FillTriangle(vertices[triangles[i]], vertices[triangles[i + 1]], vertices[triangles[i + 2]]);
                }
                else
                {
                    for (int x = Cell(center.x - bounds.extents.x); x <= Cell(center.x + bounds.extents.x); x++)
                    for (int z = Cell(center.z - bounds.extents.z); z <= Cell(center.z + bounds.extents.z); z++)
                        covered.Add(new Vector2Int(x, z));
                }
            }
        }

        internal bool IsClear(IowaParkingSpace strip)
        {
            Vector3 across = strip.Across * strip.HalfWidth;
            Vector3 along = strip.Along * strip.HalfLength;
            float radiusX = Mathf.Abs(across.x) + Mathf.Abs(along.x);
            float radiusZ = Mathf.Abs(across.z) + Mathf.Abs(along.z);
            for (int x = Cell(strip.Center.x - radiusX); x <= Cell(strip.Center.x + radiusX); x++)
            for (int z = Cell(strip.Center.z - radiusZ); z <= Cell(strip.Center.z + radiusZ); z++)
                if (covered.Contains(new Vector2Int(x, z)) && strip.Intersects(CellArea(x, z))) return false;
            return true;
        }

        private void FillTriangle(Vector3 a, Vector3 b, Vector3 c)
        {
            var first = new Vector2(a.x, a.z);
            var second = new Vector2(b.x, b.z);
            var third = new Vector2(c.x, c.z);
            int minX = Cell(Mathf.Min(a.x, Mathf.Min(b.x, c.x)));
            int maxX = Cell(Mathf.Max(a.x, Mathf.Max(b.x, c.x)));
            int minZ = Cell(Mathf.Min(a.z, Mathf.Min(b.z, c.z)));
            int maxZ = Cell(Mathf.Max(a.z, Mathf.Max(b.z, c.z)));
            for (int x = minX; x <= maxX; x++)
            for (int z = minZ; z <= maxZ; z++)
            {
                var coordinate = new Vector2Int(x, z);
                if (!covered.Contains(coordinate) && CellArea(x, z).Intersects(first, second, third)) covered.Add(coordinate);
            }
        }

        private static IowaParkingSpace CellArea(int x, int z) => new IowaParkingSpace(
            new Vector3((x + .5f) * CellSize, 0f, (z + .5f) * CellSize), Quaternion.identity, CellSize * .5f, CellSize * .5f);

        private static int Cell(float coordinate) => Mathf.FloorToInt(coordinate / CellSize);

        private static bool BlocksBillboard(Renderer renderer)
        {
            string name = renderer.name.ToLowerInvariant();
            if (name.Contains("grandstand") || name.Contains("parking") || name.Contains("access") ||
                name.Contains("pit road") || name.Contains("pit lane") || name.Contains("service road"))
                return true;
            if (name.Contains("fence") || name.Contains("wall") || name.Contains("embankment") ||
                name.Contains("asphalt") || name.Contains("racing surface") || name.Contains("racing edge") || name.Contains("apron") ||
                name.Contains("grass") || name.Contains("restart") || name.Contains("stripe") || name.Contains("checker") ||
                name is "road surface" or "inner edge" or "outer edge" or "start and finish")
                return false;
            return renderer.bounds.size.y > .24f;
        }
    }
}
