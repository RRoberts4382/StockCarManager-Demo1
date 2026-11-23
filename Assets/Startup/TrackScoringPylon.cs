using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;

public sealed partial class TrackPreview
{
    private const int PylonDisplayWidth = 128;
    private const int PylonRowHeight = 18;
    private static readonly Color32 PylonBackground = new Color32(6, 12, 19, 255);
    private static readonly Color32 PylonPosition = new Color32(137, 188, 211, 255);
    private static readonly Color32 PylonNumber = new Color32(232, 248, 255, 255);
    private static readonly Color32 PylonLap = new Color32(255, 182, 64, 255);
    private static readonly byte[,] PylonGlyphs =
    {
        { 14, 17, 19, 21, 25, 17, 14 }, { 4, 12, 4, 4, 4, 4, 14 },
        { 14, 17, 1, 2, 4, 8, 31 }, { 30, 1, 1, 14, 1, 1, 30 },
        { 2, 6, 10, 18, 31, 2, 2 }, { 31, 16, 16, 30, 1, 1, 30 },
        { 14, 16, 16, 30, 17, 17, 14 }, { 31, 1, 2, 4, 8, 8, 8 },
        { 14, 17, 17, 14, 17, 17, 14 }, { 14, 17, 17, 15, 1, 1, 14 },
        { 16, 16, 16, 16, 16, 16, 31 }, { 14, 17, 17, 31, 17, 17, 17 },
        { 30, 17, 17, 30, 16, 16, 16 }
    };

    private Texture2D pylonDisplay;
    private Color32[] pylonPixels;
    private int[] pylonOrder;
    private int pylonLap = -1;
    private float nextPylonUpdate;

    private void AddIowaScoringPylon()
    {
        Vector3 start = PitPosition(pitRoute[1], 0f, pitHeights[1], pitHeights[2]);
        Vector3 end = PitPosition(pitRoute[1], 1f, pitHeights[1], pitHeights[2]);
        Vector3 along = (end - start).normalized;
        Vector3 infield = Vector3.Cross(Vector3.up, along).normalized * pitWallSide;
        Vector3 finish = TrackPoint(definition.StartFinishFraction * lapLength, 0f, out var raceHeading, out _);

        // Add a concrete bay at the walkway's far edge to preserve its full clear width.
        const float walkwayHalfWidth = 1.524f;
        const float bayDepth = 1.48f;
        Vector3 baseLine = start + infield * (IowaPitWalkwayOffset + walkwayHalfWidth + bayDepth * .5f);
        float projection = Vector3.Dot(along, raceHeading);
        if (Mathf.Abs(projection) < .01f)
            return;
        float station = Vector3.Dot(finish - baseLine, raceHeading) / projection;
        if (station < 1.5f || station > Vector3.Distance(start, end) - 1.5f)
            return;

        Vector3 position = baseLine + along * station;
        position.y = .004f;
        var bay = Box("Iowa scoring pylon walkway bay", transform, position - infield * .04f - Vector3.up * .045f,
            new Vector3(2.8f, .09f, bayDepth + .08f), MakeMaterial(new Color32(152, 149, 138, 255)));
        bay.transform.localRotation = Quaternion.LookRotation(-infield, Vector3.up);
        BuildScoringPylon(position, -infield);
    }

    private void BuildScoringPylon(Vector3 position, Vector3 towardTrack)
    {
        if (pylonDisplay != null)
            return;

        var tower = CreateObject("Track scoring pylon", transform).transform;
        tower.SetLocalPositionAndRotation(position, Quaternion.LookRotation(towardTrack, Vector3.up));
        var steel = MakeMaterial(new Color32(33, 41, 53, 255));
        var concrete = MakeMaterial(new Color32(164, 161, 152, 255));
        var cap = MakeMaterial(new Color32(19, 50, 81, 255));
        var trim = MakeMaterial(new Color32(249, 174, 47, 255));
        const float baseHeight = 4.1f;
        const float bodyWidth = 3.42f;
        float displayHeight = (cars.Length + 1) * .83f;
        float displayCenter = baseHeight + displayHeight * .5f;
        float crown = baseHeight + displayHeight;
        Box("Pylon footing", tower, Vector3.up * .12f, new Vector3(.94f, .24f, .94f), concrete);
        Box("Pylon column", tower, Vector3.up * (baseHeight * .5f), new Vector3(.54f, baseHeight, .54f), steel);
        Box("Pylon display frame", tower, Vector3.up * displayCenter,
            new Vector3(bodyWidth, displayHeight, bodyWidth), steel);
        Box("Pylon crown", tower, Vector3.up * (crown + 1f), new Vector3(4.58f, 2f, 4.58f), cap);
        Box("Pylon crown trim", tower, Vector3.up * (crown + .15f), new Vector3(4.66f, .17f, 4.66f), trim);
        Box("Pylon top cap", tower, Vector3.up * (crown + 2.02f), new Vector3(4.75f, .13f, 4.75f), steel);

        pylonOrder = new int[cars.Length];
        Array.Fill(pylonOrder, -1);
        pylonLap = -1;
        nextPylonUpdate = 0f;
        int height = (cars.Length + 1) * PylonRowHeight;
        pylonPixels = new Color32[PylonDisplayWidth * height];
        pylonDisplay = new Texture2D(PylonDisplayWidth, height, TextureFormat.RGBA32, true)
        {
            name = "Live timing pylon display", filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp
        };
        carPaintTextures.Add(pylonDisplay);
        Material display = MakeMaterial(Color.white);
        display.SetTexture("_BaseMap", pylonDisplay);
        display.SetTexture("_EmissionMap", pylonDisplay);
        display.EnableKeyword("_EMISSION");
        display.SetColor("_EmissionColor", Color.white * .65f);
        var faces = new Vector3[16];
        var uv = new Vector2[16];
        var triangles = new int[24];
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        string venueName = definition.name.ToUpperInvariant();
        int wrap = venueName.LastIndexOf(' ', venueName.Length / 2);
        if (wrap < 0)
            wrap = venueName.IndexOf(' ');
        if (wrap >= 0)
            venueName = venueName.Remove(wrap, 1).Insert(wrap, "\n");
        for (int side = 0; side < 4; side++)
        {
            Quaternion rotation = Quaternion.Euler(0f, side * 90f, 0f);
            Vector3 outwards = rotation * Vector3.forward;
            Vector3 across = rotation * Vector3.right;
            Vector3 center = outwards * (bodyWidth * .5f + .015f) + Vector3.up * displayCenter;
            float width = (bodyWidth - .19f) * .5f;
            float halfHeight = (displayHeight - .09f) * .5f;
            int v = side * 4;
            faces[v] = center - across * width - Vector3.up * halfHeight;
            faces[v + 1] = center + across * width - Vector3.up * halfHeight;
            faces[v + 2] = center + across * width + Vector3.up * halfHeight;
            faces[v + 3] = center - across * width + Vector3.up * halfHeight;
            uv[v] = Vector2.right;
            uv[v + 1] = Vector2.zero;
            uv[v + 2] = Vector2.up;
            uv[v + 3] = Vector2.one;
            int t = side * 6;
            triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
            triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;

            if (font == null)
                continue;
            var nameplate = CreateObject("Pylon track name", tower);
            nameplate.transform.SetLocalPositionAndRotation(outwards * 2.315f + Vector3.up * (crown + 1.08f),
                rotation * Quaternion.Euler(0f, 180f, 0f));
            var sign = nameplate.AddComponent<TextMesh>();
            sign.anchor = TextAnchor.MiddleCenter;
            sign.alignment = TextAlignment.Center;
            sign.font = font;
            sign.fontStyle = FontStyle.Bold;
            sign.fontSize = 110;
            sign.characterSize = 1f;
            sign.color = new Color32(243, 248, 254, 255);
            sign.text = venueName;
            var nameRenderer = nameplate.GetComponent<MeshRenderer>();
            nameRenderer.sharedMaterial = GetCarDecalMaterial(font);
            Vector3 size = nameRenderer.localBounds.size;
            nameplate.transform.localScale = Vector3.one * Mathf.Min(4.05f / Mathf.Max(.01f, size.x),
                1.62f / Mathf.Max(.01f, size.y));
        }

        var mesh = new Mesh { name = "Four-sided scoring display", vertices = faces, uv = uv, triangles = triangles };
        mesh.RecalculateNormals();
        roadMeshes.Add(mesh);
        var panel = CreateObject("Pylon live display", tower);
        panel.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = panel.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = display;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        UpdateScoringPylon();
    }

    private void UpdateScoringPylon()
    {
        if (pylonOrder == null || Time.unscaledTime < nextPylonUpdate)
            return;
        nextPylonUpdate = Time.unscaledTime + .25f;
        int lap = 0;
        var timing = TimingRun;
        if (timing != null && TrackState is not SessionTrackState.TrackClosed and not SessionTrackState.Pacing)
            lap = timing.IsRace ? Mathf.Min(timing.TargetLaps, timing.LeaderLaps + 1) : timing.LeaderLaps + 1;

        bool changed = lap != pylonLap;
        int row = 0;
        if (timing != null)
            foreach (var standing in timing.RunningOrder())
            {
                if (row >= pylonOrder.Length)
                    break;
                changed |= pylonOrder[row] != standing.Car.carNumber;
                pylonOrder[row++] = standing.Car.carNumber;
            }
        for (; row < pylonOrder.Length; row++)
        {
            changed |= pylonOrder[row] != -1;
            pylonOrder[row] = -1;
        }
        if (!changed)
            return;

        pylonLap = lap;
        if (northWilkesboroScoreboard != null)
            DisplayNorthWilkesboroScoreboard();
        if (pylonDisplay == null)
            return;

        Array.Fill(pylonPixels, PylonBackground);
        DrawPylonText("LAP", 14, 2, PylonLap);
        DrawPylonNumber(lap, 112, 2, PylonLap);
        for (int i = 0; i < pylonOrder.Length && pylonOrder[i] >= 0; i++)
        {
            int y = (i + 1) * PylonRowHeight + 2;
            DrawPylonNumber(i + 1, 40, y, PylonPosition);
            DrawPylonNumber(pylonOrder[i], 112, y, PylonNumber);
        }
        pylonDisplay.SetPixels32(pylonPixels);
        pylonDisplay.Apply();
    }

    private void DrawPylonNumber(int number, int right, int y, Color32 color)
    {
        string text = number.ToString(CultureInfo.InvariantCulture);
        DrawPylonText(text, right - text.Length * 12, y, color);
    }

    private void DrawPylonText(string value, int x, int y, Color32 color)
    {
        for (int i = 0; i < value.Length; i++)
        {
            int glyph = value[i] switch { 'L' => 10, 'A' => 11, 'P' => 12, _ => value[i] - '0' };
            for (int row = 0; row < 7; row++)
            for (int column = 0; column < 5; column++)
            {
                if ((PylonGlyphs[glyph, row] & (1 << (4 - column))) == 0)
                    continue;
                int offset = (pylonDisplay.height - 1 - y - row * 2) * PylonDisplayWidth + x + i * 12 + column * 2;
                pylonPixels[offset] = pylonPixels[offset + 1] = color;
                pylonPixels[offset - PylonDisplayWidth] = pylonPixels[offset - PylonDisplayWidth + 1] = color;
            }
        }
    }
}
