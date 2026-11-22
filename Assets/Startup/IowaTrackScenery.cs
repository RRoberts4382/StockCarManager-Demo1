using UnityEngine;
using UnityEngine.Rendering;

public sealed partial class TrackPreview
{
    internal const int IowaTrackId = 35;
    private const float IowaMetersPerPixel = 1.2584012899481174f;
    private Vector3? iowaSpotterPosition;
    private Texture2D iowaFenceTexture;

    private Vector3 IowaLayoutPoint(float pixelX, float pixelY, float height = 0.02f) =>
        customLayout.ToTrackSpace(new Vector3((pixelX - 374.5f) * IowaMetersPerPixel,
            height, (222f - pixelY) * IowaMetersPerPixel));

    private static Color IowaImportedMaterialColor(Material source) => source.name switch
    {
        "Iowa asphalt" => new Color32(70, 71, 75, 255),
        "Iowa wall concrete" => new Color32(170, 168, 162, 255),
        "Iowa walkway concrete" => new Color32(158, 151, 140, 255),
        "Iowa white paint" => new Color32(237, 237, 226, 255),
        "Iowa black paint" => new Color32(26, 27, 29, 255),
        "Iowa safety yellow" => new Color32(231, 179, 43, 255),
        "Iowa restart red" => new Color32(173, 39, 32, 255),
        _ => source.color
    };

    private void AddIowaDefaultRubber()
    {
        var road = System.Array.Find(GetComponentsInChildren<MeshRenderer>(), item => item.name == "Iowa racing surface");
        if (road == null)
            return;

        const int along = 1536, across = 96;
        var pixels = new Color32[along * across];
        var noise = new System.Random(351215);
        for (int x = 0; x < along; x++)
        {
            float fraction = (x + .5f) / along;
            float station = fraction * lapLength;
            float nominalWidth = RoadWidths(station).x;
            float lane = behaviorModel.LaneOffset(0, nominalWidth, float.PositiveInfinity, racingLine(station));
            float groove = .5f + lane / nominalWidth;
            customLayout.Sample(station - 16f, out var before, out _);
            customLayout.Sample(station + 16f, out var after, out _);
            float bend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.8f, 16f, Vector3.Angle(before, after)));
            float phase = fraction * Mathf.PI * 2f;
            float nx = Mathf.Cos(phase) * 15f, ny = Mathf.Sin(phase) * 15f;
            for (int y = 0; y < across; y++)
            {
                float surface = Mathf.PerlinNoise(nx + y * .13f + 28f, ny + y * .17f + 29f);
                float distance = Mathf.Abs((y + .5f) / across - groove - (surface - .5f) * .023f);
                float trackRubber = 1f - Mathf.SmoothStep(0f, 1f, distance / .145f);
                float shade = 226f + surface * 18f + noise.Next(-6, 7) - (7f + 18f * bend) * trackRubber;
                byte channel = (byte)Mathf.Clamp(Mathf.RoundToInt(shade), 0, 255);
                pixels[y * along + x] = new Color32(channel, channel, channel, 255);
            }
        }
        var texture = new Texture2D(along, across, TextureFormat.RGBA32, true)
        {
            name = "Iowa light rubber in the racing groove", wrapModeU = TextureWrapMode.Repeat,
            wrapModeV = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 8
        };
        texture.SetPixels32(pixels);
        texture.Apply(true, true);
        carPaintTextures.Add(texture);
        Material surfaceMaterial = MakeMaterial(road.sharedMaterial.color);
        surfaceMaterial.SetFloat("_Smoothness", .14f);
        surfaceMaterial.SetTexture("_BaseMap", texture);
        road.sharedMaterial = surfaceMaterial;
    }

    private void AddIowaScenery()
    {
        AddLargeTrackGrandstands(null);
        AddIowaPressBox();
        AddIowaFence();
        AddIowaInfieldGarage();
        AddIowaInfieldBuildings();
        AddIowaTrackVehicles();
    }

    private void AddIowaPressBox()
    {
        if (grandstandScenery == null)
            return;
        var stand = grandstandScenery.transform.Find("Start and Finish Grandstand");
        if (stand == null)
            return;
        float top = 0f;
        foreach (var renderer in stand.GetComponentsInChildren<MeshRenderer>())
            if (renderer.name == "Grandstand detail")
                top = Mathf.Max(top, transform.InverseTransformPoint(renderer.bounds.max).y);

        var edge = SampleOutsideEdge(definition.StartFinishFraction * lapLength);
        var box = CreateObject("Iowa press box", grandstandScenery.transform).transform;
        box.localPosition = edge.Position + edge.Outward * 25.4f;
        box.localPosition = new Vector3(box.localPosition.x, top - 0.35f, box.localPosition.z);
        box.localRotation = Quaternion.LookRotation(edge.Outward, Vector3.up);

        var baseConcrete = MakeMaterial(new Color32(174, 173, 159, 255));
        var blueTrim = MakeMaterial(new Color32(62, 83, 92, 255));
        var silverTrim = MakeMaterial(new Color32(159, 168, 167, 255));
        var windows = MakeMaterial(new Color32(46, 65, 73, 255));
        windows.SetFloat("_Smoothness", 0.58f);
        const float boxWidth = 65f;
        const float depth = 8.7f;
        Box("Press box support deck", box, new Vector3(0f, .1f, 0f), new Vector3(boxWidth + 2f, .8f, depth + .8f), baseConcrete);
        Box("Press box shell", box, new Vector3(0f, 2.4f, .18f), new Vector3(boxWidth, 4.2f, depth + .22f), baseConcrete);
        Box("Press box roof", box, new Vector3(0f, 4.7f, 0f), new Vector3(boxWidth + 1.2f, .5f, depth + .75f), blueTrim);
        Box("Press box front glass", box, new Vector3(0f, 2.55f, -depth * .5f + .065f), new Vector3(boxWidth - 3f, 3.3f, .15f), windows);
        Box("Press box belt trim", box, new Vector3(0f, 2.5f, -depth * .5f - .035f), new Vector3(boxWidth - 2.8f, .15f, .12f), silverTrim);
        for (int i = 0; i <= 20; i++)
            Box("Press box window mullion", box, new Vector3(Mathf.Lerp(-31f, 31f, i / 20f), 2.55f, -depth * .5f - .07f),
                new Vector3(.075f, 3.37f, .13f), silverTrim);
        for (int i = -1; i <= 1; i++)
        {
            float x = i * 21.5f;
            Box("Press box support column", box, new Vector3(x, -1.8f, depth * .36f), new Vector3(.55f, 3.6f, .55f), baseConcrete);
            Box("Roof equipment", box, new Vector3(x - 3f, 5.35f, 2.2f), new Vector3(2.2f, .85f, 1.65f), silverTrim);
            Box("Roof floodlight post", box, new Vector3(x, 8.85f, 3f), new Vector3(.28f, 7.9f, .28f), silverTrim);
            Box("Roof floodlight bar", box, new Vector3(x, 12.9f, 2.9f), new Vector3(3.3f, .3f, .23f), silverTrim);
            for (int lamp = -2; lamp <= 2; lamp++)
                Box("Roof floodlight", box, new Vector3(x + lamp * .61f, 12.82f, 2.69f),
                    new Vector3(.48f, .45f, .32f), baseConcrete);
        }

        iowaSpotterPosition = transform.InverseTransformPoint(box.TransformPoint(new Vector3(0f, 8.7f, 0f)));
    }

    private void AddIowaFence()
    {
        const float panelBottom = 1.06f;
        const float panelHeight = 2.62f;
        const float tile = .52f;
        const int textureSize = 128;

        iowaFenceTexture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, true)
        {
            name = "Iowa chain-link opacity",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 8
        };
        var pixels = new Color32[textureSize * textureSize];
        for (int y = 0; y < textureSize; y++)
        for (int x = 0; x < textureSize; x++)
        {
            float u = x / (float)textureSize;
            float v = y / (float)textureSize;
            float wire = Mathf.Min(Mathf.Abs(Mathf.Repeat(u + v + .5f, 1f) - .5f),
                Mathf.Abs(Mathf.Repeat(u - v + .5f, 1f) - .5f));
            float edge = Mathf.InverseLerp(.021f, .052f, wire);
            byte alpha = (byte)Mathf.RoundToInt(255f * (1f - Mathf.SmoothStep(0f, 1f, edge)));
            pixels[y * textureSize + x] = new Color32(255, 255, 255, alpha);
        }
        iowaFenceTexture.SetPixels32(pixels);
        iowaFenceTexture.Apply(true, true);
        var links = MakeMaterial(new Color32(137, 146, 153, 255));
        links.SetTexture("_BaseMap", iowaFenceTexture);
        links.SetFloat("_AlphaClip", 1f);
        links.SetFloat("_Cutoff", .34f);
        links.SetFloat("_Cull", (float)CullMode.Off);
        links.SetFloat("_AlphaToMask", 1f);
        links.SetFloat("_Smoothness", .22f);
        links.EnableKeyword("_ALPHATEST_ON");
        links.SetOverrideTag("RenderType", "TransparentCutout");
        links.renderQueue = (int)RenderQueue.AlphaTest;

        int sections = Mathf.CeilToInt(lapLength / 2f);
        var vertices = new Vector3[2 * (sections + 1)];
        var uv = new Vector2[vertices.Length];
        var triangles = new int[sections * 6];
        float distance = 0f;
        for (int i = 0; i <= sections; i++)
        {
            var edge = SampleOutsideEdge(lapLength * i / sections);
            Vector3 foot = edge.Position + edge.Outward * .24f + Vector3.up * panelBottom;
            if (i > 0)
                distance += Vector3.Distance(foot, vertices[i * 2 - 2]);
            int v = 2 * i;
            vertices[v] = foot;
            vertices[v + 1] = foot + Vector3.up * panelHeight;
            uv[v] = new Vector2(distance / tile, 0f);
            uv[v + 1] = new Vector2(distance / tile, panelHeight / tile);
            if (i == 0)
                continue;
            int t = (i - 1) * 6;
            triangles[t] = v - 2;
            triangles[t + 1] = v - 1;
            triangles[t + 2] = v;
            triangles[t + 3] = v;
            triangles[t + 4] = v - 1;
            triangles[t + 5] = v + 1;
        }
        AddRoadMesh("Iowa chain-link catch fence", links, vertices, triangles, uv);
        transform.Find("Iowa chain-link catch fence").GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

        var steel = MakeMaterial(new Color32(108, 118, 124, 255));
        var posts = CreateObject("Iowa catch-fence posts", transform).transform;
        int postCount = Mathf.CeilToInt(lapLength / 8f);
        for (int i = 0; i < postCount; i++)
        {
            var edge = SampleOutsideEdge(lapLength * i / postCount);
            Vector3 center = edge.Position + edge.Outward * .26f + Vector3.up * (panelBottom + panelHeight * .5f);
            Box("Fence post", posts, center, new Vector3(.11f, panelHeight + .09f, .11f), steel);
        }
    }

    private void AddIowaInfieldGarage()
    {
        Vector3 center = IowaLayoutPoint(309f, 281f, .017f);
        var garage = CreateObject("Iowa infield garage", transform).transform;
        garage.localPosition = center;
        // The long garage in the annotation runs north-south, with doors on both sides.
        garage.localRotation = Quaternion.Euler(0f, 90f, 0f);
        AddInfieldGarageBuilding(garage, style: 5);

        var concrete = MakeMaterial(new Color32(151, 147, 138, 255));
        concrete.SetTexture("_BaseMap", BristolPavementTexture(true));
        AddThreshold("Iowa garage front threshold", -8.05f);
        AddThreshold("Iowa garage rear threshold", 8.05f);

        void AddThreshold(string name, float z)
        {
            var vertices = new[]
            {
                Point(-55f, z - 1.15f), Point(55f, z - 1.15f),
                Point(-55f, z + 1.15f), Point(55f, z + 1.15f)
            };
            AddRoadMesh(name, concrete, vertices, new[] { 0, 2, 1, 1, 2, 3 });
        }

        Vector3 Point(float x, float z) => center + garage.localRotation * new Vector3(x, .016f, z);
    }

    private void AddIowaInfieldBuildings()
    {
        var sandstone = MakeMaterial(new Color32(186, 177, 158, 255));
        var slate = MakeMaterial(new Color32(79, 91, 97, 255));
        var pale = MakeMaterial(new Color32(177, 188, 179, 255));
        var windows = MakeMaterial(new Color32(51, 77, 87, 255));
        windows.SetFloat("_Smoothness", .46f);
        var doors = MakeMaterial(new Color32(69, 76, 79, 255));
        AddBuilding("Iowa infield operations", IowaLayoutPoint(406f, 221.5f), 64f, 12.5f, 5.1f, 0f, sandstone);
        AddBuilding("Iowa infield utility building", IowaLayoutPoint(507.5f, 248.5f), 31f, 14.5f, 4.45f, -27f, pale);

        void AddBuilding(string name, Vector3 position, float width, float depth, float height, float yaw, Material siding)
        {
            var root = CreateObject(name, transform).transform;
            root.localPosition = position;
            root.localRotation = Quaternion.Euler(0f, yaw, 0f);
            Box("Infield building shell", root, Vector3.up * (height * .5f), new Vector3(width, height, depth), siding);
            Box("Infield building roof", root, Vector3.up * (height + .22f), new Vector3(width + .65f, .45f, depth + .72f), slate);
            Box("Infield building awning", root, new Vector3(0f, 3.4f, -depth * .5f - .55f), new Vector3(width * .67f, .22f, 1.2f), slate);
            for (int i = -2; i <= 2; i++)
                Box("Infield building window", root, new Vector3(i * width / 7f, 2.64f, -depth * .5f - .04f),
                    new Vector3(width / 10f, .91f, .085f), windows);
            Box("Infield utility door", root, new Vector3(width * .39f, 1.35f, -depth * .5f - .035f),
                new Vector3(1.8f, 2.7f, .08f), doors);
            Box("Utility building roof vent", root, new Vector3(2f, height + .75f, 1.8f), new Vector3(2.9f, .82f, 1.65f), doors);
        }
    }
}
