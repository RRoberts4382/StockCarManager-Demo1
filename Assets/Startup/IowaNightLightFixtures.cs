using System;
using UnityEngine;
using Part = IowaPitWorkMesh.Part;

public sealed partial class TrackPreview
{
    private const int IowaRaceFloodlightCount = 38;
    private Material iowaNightFixtureLens;

    // The road centres and shoulder positions are in the archived Iowa layout.
    // Keep the post feet outside the circulation lanes and away from both pit openings.
    private static readonly Vector4[] IowaStreetLightSites =
    {
        new Vector4(512f, 225f, 506f, 223f),
        new Vector4(468.1f, 234.1f, 466.8f, 240.6f),
        new Vector4(434.4f, 247.2f, 436f, 253.7f),
        new Vector4(400.2f, 297.4f, 393f, 298f),
        new Vector4(420f, 323f, 419f, 330f),
        new Vector4(490.2f, 287f, 484f, 286f),
        new Vector4(555.6f, 255.1f, 548.4f, 259.9f),
        new Vector4(510.5f, 345.2f, 507.7f, 338.5f),
        new Vector4(380.3f, 166.4f, 379.9f, 174.9f)
    };

    private void AddIowaNightLightFixtures(Action<Light, bool> configureLight)
    {
        var mounts = new IowaPitWorkMesh();
        var lenses = new IowaPitWorkMesh();
        var root = CreateObject("Iowa night light fixtures", transform).transform;
        float finish = definition.StartFinishFraction * lapLength;
        float pitch = lapLength / IowaRaceFloodlightCount;
        for (int i = 0; i < IowaRaceFloodlightCount; i++)
        {
            float station = Mathf.Repeat(finish + (i + .5f) * pitch, lapLength);
            var edge = SampleOutsideEdge(station);
            float fromFinish = Mathf.Abs(Mathf.Repeat(station - finish + lapLength * .5f, lapLength)
                - lapLength * .5f);
            // The inside of the front grandstand concourse has room for the
            // posts between the fence and the closest seat setback at 6.6 m.
            // The outer RV row approaches the fence through Turns 3 and 4.
            // Hold the posts on the verge before the parked vehicle noses.
            float setback = i switch
            {
                20 or 21 => 3f,
                22 or 23 => 2.2f,
                24 => 4.3f,
                _ => fromFinish < lapLength * .158f ? 4.9f : 5.8f
            };
            Vector3 foot = edge.Position + edge.Outward * setback;
            foot.y = groundHeight;
            Vector3 mast = foot;
            mast.y = edge.Position.y + 21.25f;
            Vector3 head = mast - edge.Outward * .70f;
            Vector3 road = TrackPoint(station, 0f, out _, out _);
            Vector3 aim = Vector3.Lerp(road, TrackPoint(station, ApronLane(station), out _, out _), .20f);
            Quaternion facing = Quaternion.LookRotation(aim - head, Vector3.up);

            mounts.Cylinder(foot, foot + Vector3.up * .26f, .45f, .38f, Part.Secondary, 8);
            mounts.Cylinder(foot + Vector3.up * .19f, mast, .21f, .11f, Part.Metal, 9);
            mounts.Cylinder(mast - Vector3.up * .58f, head, .077f, .067f, Part.Metal, 8);
            mounts.Box(head, new Vector3(1.71f, .78f, .25f), facing, Part.DarkMetal);
            for (int row = -1; row <= 1; row += 2)
            for (int column = -1; column <= 1; column += 2)
            {
                Vector3 socket = head + facing * new Vector3(column * .42f, row * .18f, .125f);
                mounts.Box(socket, new Vector3(.73f, .31f, .082f), facing, Part.Metal);
                lenses.Box(socket + facing * (Vector3.forward * .043f), new Vector3(.62f, .23f, .015f),
                    facing, Part.Light);
            }

            AddSpot($"Iowa racing floodlight {i + 1:00}", head + facing * (Vector3.forward * .145f), facing,
                new Color32(236, 243, 255, 255), 735f, 58f, 108f, 86f, true);
        }

        for (int i = 0; i < IowaStreetLightSites.Length; i++)
        {
            Vector4 site = IowaStreetLightSites[i];
            Vector3 foot = IowaLayoutPoint(site.x, site.y, groundHeight);
            Vector3 aim = IowaLayoutPoint(site.z, site.w, groundHeight);
            Vector3 direction = (aim - foot).normalized;
            Vector3 mast = foot + Vector3.up * 5.55f;
            Vector3 elbow = mast + direction * .28f + Vector3.up * .22f;
            Vector3 head = elbow + direction * .71f;
            Quaternion facing = Quaternion.LookRotation(aim - head, Vector3.up);

            mounts.Cylinder(foot, foot + Vector3.up * .20f, .21f, .17f, Part.Secondary, 8);
            mounts.Cylinder(foot + Vector3.up * .16f, mast, .091f, .052f, Part.Metal, 8);
            mounts.Cylinder(mast, elbow, .055f, .043f, Part.Metal, 8);
            mounts.Cylinder(elbow, head, .043f, .038f, Part.Metal, 8);
            mounts.Box(head, new Vector3(.41f, .21f, .29f), facing, Part.DarkMetal);
            lenses.Box(head + facing * (Vector3.forward * .147f), new Vector3(.34f, .14f, .012f),
                facing, Part.Light);

            AddSpot($"Iowa infield street light {i + 1:00}", head + facing * (Vector3.forward * .16f), facing,
                new Color32(255, 231, 193, 255), 76f, 16f, 76f, 49f, false);
        }

        var palette = new Texture2D(IowaPitWorkMesh.PartCount, 1, TextureFormat.RGBA32, false)
        {
            name = "Iowa night fixture palette",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        var colors = new Color32[IowaPitWorkMesh.PartCount];
        colors[(int)Part.Secondary] = new Color32(128, 123, 113, 255);
        colors[(int)Part.Metal] = new Color32(138, 149, 155, 255);
        colors[(int)Part.DarkMetal] = new Color32(52, 63, 68, 255);
        colors[(int)Part.Light] = new Color32(215, 220, 226, 255);
        palette.SetPixels32(colors);
        palette.Apply(false, true);
        carPaintTextures.Add(palette);

        Material housing = MakeMaterial(Color.white);
        housing.name = "Iowa night light structures";
        housing.SetTexture("_BaseMap", palette);
        housing.SetFloat("_Smoothness", .21f);
        iowaNightFixtureLens = MakeMaterial(Color.white);
        iowaNightFixtureLens.name = "Iowa night light lenses";
        iowaNightFixtureLens.SetTexture("_BaseMap", palette);
        iowaNightFixtureLens.SetFloat("_Smoothness", .31f);
        AddCombined("Iowa night light structures", mounts, housing);
        AddCombined("Iowa night light lenses", lenses, iowaNightFixtureLens);
        SetIowaNightFixtureLenses(false);

        void AddSpot(string name, Vector3 position, Quaternion rotation, Color color,
            float intensity, float range, float outerAngle, float innerAngle, bool race)
        {
            GameObject emitter = CreateObject(name, root);
            emitter.transform.SetLocalPositionAndRotation(position, rotation);
            var light = emitter.AddComponent<Light>();
            light.enabled = false;
            light.type = LightType.Spot;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.spotAngle = outerAngle;
            light.innerSpotAngle = innerAngle;
            light.shadows = LightShadows.None;
            light.cullingMask = 1 << PreviewLayer;
            configureLight(light, race);
        }

        void AddCombined(string name, IowaPitWorkMesh shape, Material paint)
        {
            GameObject item = CreateObject(name, root);
            Mesh mesh = shape.Build(name, 0, 1);
            roadMeshes.Add(mesh);
            item.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = item.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = paint;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    private void SetIowaNightFixtureLenses(bool night)
    {
        if (iowaNightFixtureLens == null)
            return;
        iowaNightFixtureLens.SetColor("_EmissionColor", night ? new Color(.83f, .9f, 1f) * 2.55f : Color.black);
        if (night)
            iowaNightFixtureLens.EnableKeyword("_EMISSION");
        else
            iowaNightFixtureLens.DisableKeyword("_EMISSION");
    }
}
