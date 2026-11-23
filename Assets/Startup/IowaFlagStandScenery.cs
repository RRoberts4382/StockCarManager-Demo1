using UnityEngine;
using UnityEngine.Rendering;
using Part = IowaPitWorkMesh.Part;

public sealed partial class TrackPreview
{
    private const float IowaFlagStandSetback = 1.90f;
    private const float IowaFlagStandDeckHeight = 4.12f;

    private Transform AddIowaFlagStandScenery()
    {
        float station = definition.StartFinishFraction * lapLength;
        var edge = SampleOutsideEdge(station);
        string venue = VenueSceneryName;
        var stand = CreateObject(venue + " flag stand", transform).transform;
        stand.SetLocalPositionAndRotation(edge.Position + edge.Outward * IowaFlagStandSetback,
            Quaternion.LookRotation(-edge.Outward, Vector3.up));

        var bridge = new IowaPitWorkMesh();
        const float deck = IowaFlagStandDeckHeight;
        const float railTop = deck + 1.04f;
        const float front = 1.30f;
        const float rear = -1.10f;
        const float halfWidth = 1.67f;
        // Match the concrete walkway at finish. Its outside edge continues 1.2m under the first row.
        float innerWalkway = Mathf.Max(groundHeight + .025f, edge.Position.y + .02f);
        float outerWalkway = groundHeight + .14f;
        for (int sample = 0; sample <= 8; sample++)
        {
            float fraction = definition.StartFinishFraction + Mathf.Lerp(-.031f, .034f, sample / 8f);
            outerWalkway = Mathf.Max(outerWalkway, SampleOutsideEdge(fraction * lapLength).Position.y + .02f);
        }

        bridge.Box(new Vector3(0f, deck - .09f, .10f), new Vector3(3.44f, .18f, 2.54f), Quaternion.identity, Part.DarkMetal);
        bridge.Box(new Vector3(0f, deck - .014f, .105f), new Vector3(3.31f, .029f, 2.36f), Quaternion.identity, Part.Metal);
        for (int side = -1; side <= 1; side += 2)
        {
            float x = side * 1.31f;
            float postZ = .48f;
            float foot = WalkwayHeight(postZ);
            bridge.Box(new Vector3(x, foot + .055f, postZ), new Vector3(.46f, .13f, .46f), Quaternion.identity, Part.Secondary);
            bridge.Box(new Vector3(x, (foot + deck) * .5f, postZ), new Vector3(.19f, deck - foot, .19f), Quaternion.identity, Part.DarkMetal);
            Rail(new Vector3(x, deck - 1.45f, postZ), new Vector3(x, deck - .12f, rear + .11f), .049f, Part.DarkMetal);
            Rail(new Vector3(x, deck - 1.13f, postZ), new Vector3(x, deck - .12f, front - .15f), .046f, Part.DarkMetal);
            Rail(new Vector3(side * halfWidth, deck, front), new Vector3(side * halfWidth, railTop, front), .045f, Part.Metal);
            Rail(new Vector3(side * halfWidth, deck, rear), new Vector3(side * halfWidth, railTop, rear), .045f, Part.Metal);
            Rail(new Vector3(side * halfWidth, railTop, rear), new Vector3(side * halfWidth, railTop, front), .040f, Part.Metal);
            Rail(new Vector3(side * halfWidth, deck + .53f, rear), new Vector3(side * halfWidth, deck + .53f, front), .028f, Part.Metal);
            bridge.Box(new Vector3(side * (halfWidth - .035f), deck + .10f, .10f), new Vector3(.075f, .20f, 2.35f), Quaternion.identity, Part.Primary);
            bridge.Box(new Vector3(side * (halfWidth - .035f), deck + .35f, .98f), new Vector3(.080f, .70f, .63f), Quaternion.identity, Part.Primary);
        }

        Rail(new Vector3(-halfWidth, railTop, front), new Vector3(halfWidth, railTop, front), .047f, Part.Metal);
        Rail(new Vector3(-1.31f, deck - .25f, .48f), new Vector3(1.31f, deck - .25f, .48f), .046f, Part.DarkMetal);
        // The ladder is at the back. A gap in the rear guardrail lines up with its rungs.
        const float ladderCenter = .79f;
        const float ladderBack = -1.38f;
        float ladderBase = WalkwayHeight(ladderBack);
        foreach (float y in new[] { deck + .53f, railTop })
        {
            Rail(new Vector3(-halfWidth, y, rear), new Vector3(ladderCenter - .35f, y, rear), .035f, Part.Metal);
            Rail(new Vector3(ladderCenter + .35f, y, rear), new Vector3(halfWidth, y, rear), .035f, Part.Metal);
        }
        for (int side = -1; side <= 1; side += 2)
        {
            float x = ladderCenter + side * .32f;
            Rail(new Vector3(x, ladderBase + .025f, ladderBack), new Vector3(x, railTop, ladderBack), .035f, Part.Metal);
            Rail(new Vector3(x, railTop, ladderBack), new Vector3(x, railTop, rear + .03f), .035f, Part.Metal);
        }
        for (float y = ladderBase + .25f; y < deck; y += .28f)
            Rail(new Vector3(ladderCenter - .31f, y, ladderBack), new Vector3(ladderCenter + .31f, y, ladderBack), .026f, Part.Metal);

        var bridgePaint = IowaFlagStandPalette("Iowa flag stand metal", .09f,
            new Color32(28, 56, 83, 255), new Color32(150, 150, 144, 255), new Color32(190, 143, 73, 255),
            new Color32(31, 38, 45, 255), new Color32(161, 172, 180, 255), new Color32(60, 73, 80, 255),
            new Color32(245, 245, 233, 255));
        IowaFlagStandMesh("Iowa flag stand structure", stand, bridge, bridgePaint);
        AddIowaFlagStandBranding(stand, deck, front, venue);

        var uniform = IowaFlagStandPalette("Iowa flag official uniforms", .17f,
            new Color32(33, 43, 55, 255), new Color32(239, 239, 229, 255), new Color32(182, 37, 46, 255),
            new Color32(24, 28, 33, 255), new Color32(116, 128, 130, 255), new Color32(22, 33, 44, 255),
            new Color32(230, 229, 212, 255), new Color32(208, 153, 114, 255), new Color32(166, 111, 77, 255),
            new Color32(113, 76, 59, 255), new Color32(67, 110, 132, 255));
        var official = AddIowaFlagOfficial(venue + " flag official", new Vector3(-.18f, deck, .72f), true);
        AddIowaFlagOfficial(venue + " assistant flag official", new Vector3(.80f, deck, -.34f), false);
        return official;

        float WalkwayHeight(float z) => Mathf.Lerp(innerWalkway, outerWalkway,
            Mathf.InverseLerp(.23f, 8.6f, IowaFlagStandSetback - z)) - edge.Position.y;

        void Rail(Vector3 start, Vector3 end, float radius, Part part) =>
            bridge.Cylinder(start, end, radius, radius, part);

        Transform AddIowaFlagOfficial(string name, Vector3 location, bool waves)
        {
            var person = CreateObject(name, stand).transform;
            person.SetLocalPositionAndRotation(location, waves ? Quaternion.identity : Quaternion.Euler(0f, -17f, 0f));
            var mesh = new IowaPitWorkMesh();
            Quaternion rotation = Quaternion.identity;
            mesh.Ellipsoid(new Vector3(0f, 1.01f, -.015f), new Vector3(.174f, .16f, .115f), rotation, Part.Primary);
            mesh.Ellipsoid(new Vector3(0f, 1.285f, 0f), new Vector3(.206f, .240f, .140f), rotation, Part.Secondary, 12, 6);
            mesh.Box(new Vector3(0f, 1.065f, 0f), new Vector3(.295f, .06f, .190f), rotation, Part.Primary);
            mesh.Box(new Vector3(0f, 1.24f, .140f), new Vector3(.017f, .30f, .012f), rotation, Part.Metal);
            mesh.Box(new Vector3(-.093f, 1.37f, .130f), new Vector3(.065f, .038f, .014f), rotation, Part.Accent);
            mesh.Cylinder(new Vector3(0f, 1.49f, 0f), new Vector3(0f, 1.59f, 0f), .068f, .058f, Part.SkinMedium, 12);
            mesh.Ellipsoid(new Vector3(0f, 1.684f, .011f), new Vector3(.121f, .137f, .127f), rotation, Part.DarkMetal, 16, 7);
            mesh.Ellipsoid(new Vector3(0f, 1.687f, .092f), new Vector3(.108f, .053f, .053f), rotation, Part.Visor, 12, 5);
            mesh.Box(new Vector3(0f, 1.722f, .128f), new Vector3(.18f, .013f, .015f), rotation, Part.Metal);
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * .103f;
                float z = side * .047f;
                Vector3 knee = new Vector3(x * 1.15f, .52f, z + .025f);
                Vector3 ankle = new Vector3(x * 1.35f, .09f, z);
                mesh.Cylinder(new Vector3(x, 1.01f, -.015f), knee, .086f, .070f, Part.Primary, 10);
                mesh.Cylinder(knee, ankle, .070f, .055f, Part.Primary, 10);
                mesh.Ellipsoid(new Vector3(ankle.x, .065f, z + .048f), new Vector3(.064f, .062f, .123f), rotation, Part.Rubber);
                Vector3 shoulder = new Vector3(side * .202f, 1.437f, .015f);
                mesh.Ellipsoid(shoulder, new Vector3(.072f, .076f, .071f), rotation, Part.Primary);
                if (waves)
                    continue;

                Vector3 elbow = new Vector3(side * .259f, 1.17f, .046f);
                Vector3 wrist = new Vector3(side * .237f, .978f, .164f);
                mesh.Cylinder(shoulder, elbow, .061f, .051f, Part.Primary);
                mesh.Cylinder(elbow, wrist, .051f, .035f, Part.Primary);
                mesh.Ellipsoid(wrist + Vector3.down * .037f, new Vector3(.038f, .059f, .040f), rotation, Part.Light);
            }

            IowaFlagStandMesh(waves ? "Iowa flag official body" : "Iowa assistant flag official body", person, mesh, uniform);
            return person;
        }
    }

    private void AddIowaFlagStandBranding(Transform stand, float deck, float front, string venue)
    {
        var fascia = new IowaPitWorkMesh();
        var paint = IowaFlagStandPalette("Iowa flag stand lettering and emblem", .19f,
            new Color32(16, 43, 74, 255), new Color32(226, 234, 232, 255),
            definition.id == PhoenixTrackId ? new Color32(251, 163, 47, 255) : new Color32(197, 49, 50, 255),
            new Color32(40, 66, 95, 255), new Color32(154, 169, 180, 255), new Color32(21, 33, 48, 255),
            new Color32(242, 239, 220, 255));
        fascia.Box(new Vector3(0f, deck + .40f, front - .051f), new Vector3(3.34f, .80f, .12f), Quaternion.identity, Part.Primary);
        foreach (float y in new[] { deck + .055f, deck + .751f })
            fascia.Box(new Vector3(0f, y, front + .017f), new Vector3(3.19f, .022f, .018f), Quaternion.identity, Part.Metal);
        IowaFlagStandMesh("Iowa flag stand fascia", stand, fascia, paint);

        var emblem = new IowaPitWorkMesh();
        var badge = CreateObject(venue + " flag stand emblem", stand).transform;
        badge.localPosition = new Vector3(-1.11f, deck + .40f, front + .025f);
        badge.localScale = new Vector3(1.34f, 1f, 1f);
        emblem.Cylinder(Vector3.zero, new Vector3(0f, 0f, .026f), .296f, .296f, Part.Light, 32);
        emblem.Cylinder(new Vector3(0f, 0f, .025f), new Vector3(0f, 0f, .037f), .267f, .267f, Part.Primary, 32);
        for (int lane = -1; lane <= 1; lane++)
            emblem.Box(new Vector3(-.018f, lane * .068f, .045f), new Vector3(.33f, .025f, .019f),
                Quaternion.Euler(0f, 0f, 24f), lane == 1 ? Part.Accent : Part.Secondary);
        for (int row = 0; row < 2; row++)
        for (int column = 0; column < 3; column++)
            if ((row + column) % 2 == 0)
                emblem.Box(new Vector3(.074f + column * .046f, .125f - row * .046f, .052f),
                    new Vector3(.044f, .044f, .014f), Quaternion.identity, Part.Light);
        IowaFlagStandMesh("Iowa speedway emblem mesh", badge, emblem, paint);

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
            return;

        var label = CreateObject(venue + " flag stand venue name", stand);
        label.transform.SetLocalPositionAndRotation(new Vector3(.41f, deck + .401f, front + .024f), Quaternion.Euler(0f, 180f, 0f));
        var text = label.AddComponent<TextMesh>();
        text.font = font;
        text.anchor = TextAnchor.MiddleCenter;
        text.fontStyle = FontStyle.Bold;
        text.fontSize = 90;
        text.characterSize = 1f;
        text.color = new Color32(247, 247, 234, 255);
        text.text = definition.name.ToUpperInvariant();
        var renderer = label.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = GetCarDecalMaterial(font);
        Vector3 dimensions = renderer.localBounds.size;
        label.transform.localScale = Vector3.one * Mathf.Min(2.02f / Mathf.Max(.01f, dimensions.x),
            .40f / Mathf.Max(.01f, dimensions.y));
    }

    private Material IowaFlagStandPalette(string name, float nightVisibility, params Color32[] colors)
    {
        var texture = new Texture2D(IowaPitWorkMesh.PartCount, 1, TextureFormat.RGBA32, false)
        {
            name = name,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        for (int i = 0; i < colors.Length; i++)
            texture.SetPixel(i, 0, colors[i]);
        texture.Apply(false, true);
        carPaintTextures.Add(texture);
        var material = MakeMaterial(Color.white);
        material.name = name;
        material.SetTexture("_BaseMap", texture);
        material.SetTexture("_EmissionMap", texture);
        material.SetColor("_EmissionColor", Color.white * nightVisibility);
        material.EnableKeyword("_EMISSION");
        material.SetFloat("_Smoothness", .27f);
        return material;
    }

    private void IowaFlagStandMesh(string name, Transform parent, IowaPitWorkMesh source, Material material)
    {
        var item = CreateObject(name, parent);
        var mesh = source.Build(name, 0, 1);
        roadMeshes.Add(mesh);
        item.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = item.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
    }
}
