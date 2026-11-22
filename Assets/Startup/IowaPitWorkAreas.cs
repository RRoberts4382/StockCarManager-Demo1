using UnityEngine;
using PitPart = IowaPitWorkMesh.Part;

public sealed partial class TrackPreview
{
    private const float IowaPitWorkWidth = 7.2f;

    private float[] IowaPitWorkStations(int count)
    {
        Vector3 start = transform.TransformPoint(PitPosition(pitRoute[1], 0f, pitHeights[1], pitHeights[2]));
        Vector3 end = transform.TransformPoint(PitPosition(pitRoute[1], 1f, pitHeights[1], pitHeights[2]));
        float length = Vector3.Distance(start, end);
        if (length <= .01f)
            return null;
        Vector3 along = (end - start) / length;
        Renderer east = null, west = null;
        foreach (var renderer in GetComponentsInChildren<MeshRenderer>())
        {
            if (renderer.name == "Iowa infield pit wall east") east = renderer;
            if (renderer.name == "Iowa infield pit wall west") west = renderer;
        }
        if (east == null || west == null)
            return null;

        var eastSpan = Span(east);
        var westSpan = Span(west);
        int eastCount = Mathf.Min(count, Mathf.FloorToInt((eastSpan.y - eastSpan.x) / IowaPitWorkWidth));
        int westCount = count - eastCount;
        if (westCount * IowaPitWorkWidth > westSpan.y - westSpan.x)
            return null;

        var stations = new float[count];
        Place(eastSpan, 0, eastCount);
        Place(westSpan, eastCount, westCount);
        return stations;

        Vector2 Span(Renderer wall)
        {
            float center = Vector3.Dot(wall.bounds.center - start, along);
            float reach = ProjectPitRendererExtent(wall, along);
            return new Vector2(center - reach + .12f, center + reach - .12f);
        }

        void Place(Vector2 span, int offset, int seats)
        {
            float first = span.x + IowaPitWorkWidth * .5f;
            for (int index = 0; index < seats; index++)
            {
                int stall = offset + index;
                float desired = (practiceStallDistances[stall] - pitLengths[0]) / pitLengths[1] * length;
                float last = span.y - (seats - index - .5f) * IowaPitWorkWidth;
                float station = Mathf.Clamp(desired, first, last);
                stations[stall] = station / length;
                first = station + IowaPitWorkWidth;
            }
        }
    }

    private static float ProjectPitRendererExtent(Renderer renderer, Vector3 axis)
    {
        Vector3 extents = renderer.localBounds.extents;
        var pose = renderer.transform;
        return Mathf.Abs(Vector3.Dot(pose.TransformVector(Vector3.right * extents.x), axis)) +
            Mathf.Abs(Vector3.Dot(pose.TransformVector(Vector3.up * extents.y), axis)) +
            Mathf.Abs(Vector3.Dot(pose.TransformVector(Vector3.forward * extents.z), axis));
    }

    private Material IowaPitWorkMaterial(int count)
    {
        var palette = new Texture2D(IowaPitWorkMesh.PartCount, count, TextureFormat.RGBA32, false)
        {
            name = "Iowa pit team livery palette",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        var colors = new Color32[palette.width * palette.height];
        for (int i = 0; i < count; i++)
        {
            var livery = renderedCarPaint[i];
            Color primary = livery.RoofAndSides;
            Color accent = livery.HoodAndTrunk;
            Assign(PitPart.Primary, primary);
            Assign(PitPart.Accent, accent);
            Assign(PitPart.Secondary, Color.Lerp(primary, new Color32(34, 38, 44, 255), .48f));
            Assign(PitPart.Rubber, new Color32(37, 38, 42, 255));
            Assign(PitPart.Metal, new Color32(165, 175, 182, 255));
            Assign(PitPart.DarkMetal, new Color32(68, 73, 79, 255));
            Assign(PitPart.Light, new Color32(220, 222, 213, 255));
            Assign(PitPart.SkinLight, new Color32(207, 151, 118, 255));
            Assign(PitPart.SkinMedium, new Color32(164, 109, 77, 255));
            Assign(PitPart.SkinDark, new Color32(104, 65, 52, 255));
            Assign(PitPart.Visor, new Color32(41, 53, 59, 255));

            void Assign(PitPart part, Color color) => colors[i * palette.width + (int)part] = color;
        }
        palette.SetPixels32(colors);
        palette.Apply(false, true);
        carPaintTextures.Add(palette);
        Material material = MakeMaterial(Color.white);
        material.name = "Iowa pit crew and equipment";
        material.SetTexture("_BaseMap", palette);
        material.SetFloat("_Smoothness", .16f);
        return material;
    }

    private Material IowaPitWorkConcrete()
    {
        var concrete = MakeMaterial(new Color32(157, 154, 146, 255));
        concrete.name = "Iowa pit work-area concrete";
        concrete.SetTexture("_BaseMap", BristolPavementTexture(true));
        concrete.SetFloat("_Smoothness", .12f);
        return concrete;
    }

    private void AddIowaPitWorkArea(Transform box, Renderer[] boxRenderers, Quaternion facing,
        int carNumber, int team, int count, Material paint, Material concrete)
    {
        var worldForward = transform.TransformDirection(facing * Vector3.forward);
        float rear = 0f;
        foreach (var boxRenderer in boxRenderers)
            rear = Mathf.Min(rear, Vector3.Dot(boxRenderer.bounds.center - box.position, worldForward) -
                ProjectPitRendererExtent(boxRenderer, worldForward));

        string venue = VenueSceneryName;
        var work = CreateObject($"{venue} pit work area No. {carNumber}", teamPitBoxes.transform);
        var point = box.localPosition;
        point.y = .02f;
        work.transform.SetLocalPositionAndRotation(point, facing);
        if (concrete != null)
            AddIowaPitWorkPad(carNumber, point, facing, rear, concrete);
        var mesh = new IowaPitWorkMesh();
        var random = new System.Random(unchecked(35711 + carNumber * 7919 + team * 101));
        var spots = new Vector3[12];
        int available = spots.Length;
        for (int i = 0; i < spots.Length; i++)
            spots[i] = new Vector3((i % 4 - 1.5f) * 1.52f + Offset(.055f), 0f,
                rear - .98f - (i / 4) * 1.53f + Offset(.055f));

        Vector3 tires = Take(random.Next(available));
        bool trolley = random.Next(2) == 0;
        mesh.TireStack(tires, Turn(Offset(38f)), random.Next(2, 5), trolley);
        Vector3 jack = Take(random.Next(available));
        mesh.FloorJack(jack, Turn(Offset(48f)));

        float swap = random.Next(2) == 0 ? 1f : -1f;
        Crew(new Vector3(-2.67f * swap, 0f, -.01f + Offset(.27f)), 0, Offset(33f));
        var helper = new Vector3(2.67f * swap, 0f, -.13f + Offset(.21f));
        Crew(helper, 1, Towards(helper, tires));
        Vector3 changer = Near(tires);
        Crew(changer, 2, Towards(changer, tires));
        Vector3 jackCrew = Near(jack);
        Crew(jackCrew, 3, Towards(jackCrew, jack));
        Vector3 chest = Take(random.Next(available));
        mesh.ToolChest(chest, Turn(Offset(25f)), random.Next(3) != 0);
        Vector3 utility = Near(chest);
        Crew(utility, 4, Towards(utility, chest) + Offset(24f));

        mesh.TireStack(Take(random.Next(available)), Turn(Offset(25f)), random.Next(2, 5), !trolley);
        mesh.ServiceCart(Take(random.Next(available)), Turn(Offset(65f)));
        if (random.Next(3) != 0)
            mesh.NitrogenBottle(Take(random.Next(available)), Turn(Offset(80f)));
        mesh.FuelCan(Take(random.Next(available)), Turn(Offset(90f)));

        Mesh combined = mesh.Build(work.name, team, count);
        roadMeshes.Add(combined);
        work.AddComponent<MeshFilter>().sharedMesh = combined;
        var renderer = work.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = paint;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        float Offset(float radius) => ((float)random.NextDouble() * 2f - 1f) * radius;
        Quaternion Turn(float yaw) => Quaternion.Euler(0f, yaw, 0f);
        float Towards(Vector3 from, Vector3 to) => Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg;

        Vector3 Take(int index)
        {
            Vector3 result = spots[index];
            spots[index] = spots[--available];
            return result;
        }

        Vector3 Near(Vector3 target)
        {
            int selected = 0;
            float shortest = float.PositiveInfinity;
            for (int i = 0; i < available; i++)
            {
                float distance = (spots[i] - target).sqrMagnitude;
                if (distance < shortest)
                {
                    selected = i;
                    shortest = distance;
                }
            }
            return Take(selected);
        }

        void Crew(Vector3 position, int pose, float yaw) =>
            AddIowaPitCrewFigure(mesh, position, yaw, pose, 1f + Offset(.055f), random.Next(3));
    }

    private void AddIowaPitWorkPad(int carNumber, Vector3 center, Quaternion facing, float rear, Material concrete)
    {
        const float halfWidth = 3.51f;
        const float front = .90f;
        const float bevel = .065f;
        float back = rear - 5.22f;
        center.y = 0f;
        var vertices = new[]
        {
            Point(-halfWidth, back, .002f), Point(-halfWidth, front, .002f),
            Point(halfWidth, front, .002f), Point(halfWidth, back, .002f),
            Point(-halfWidth + bevel, back + bevel, .014f), Point(-halfWidth + bevel, front - bevel, .014f),
            Point(halfWidth - bevel, front - bevel, .014f), Point(halfWidth - bevel, back + bevel, .014f)
        };
        var triangles = new[]
        {
            4, 5, 6, 4, 6, 7,
            0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5,
            2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7
        };
        AddRoadMesh($"Iowa concrete pit work pad No. {carNumber}", concrete, vertices, triangles);

        Vector3 Point(float x, float z, float height) => center + facing * new Vector3(x, height, z);
    }
}
