using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;

public sealed partial class TrackPreview
{
    private const int NorthWilkesboroLeaderboardPositions = 5;
    private TextMesh[] northWilkesboroScoreboard;

    private void AddNorthWilkesboroScoreboard()
    {
        var markers = GetComponentsInChildren<Transform>(true);
        var cells = new Transform[NorthWilkesboroLeaderboardPositions + 1];
        foreach (var marker in markers)
        {
            if (marker.name == "North Wilkesboro scoreboard foundation marker")
            {
                HideAuthoredVenueMarker(marker);
                continue;
            }

            int cell;
            if (marker.name == "North Wilkesboro scoreboard lap")
                cell = 0;
            else if (marker.name.StartsWith("North Wilkesboro scoreboard position ", StringComparison.Ordinal) &&
                int.TryParse(marker.name.Substring("North Wilkesboro scoreboard position ".Length),
                    NumberStyles.None, CultureInfo.InvariantCulture, out int rank) &&
                rank >= 1 && rank <= NorthWilkesboroLeaderboardPositions)
                cell = rank;
            else
                continue;

            HideAuthoredVenueMarker(marker);
            cells[cell] = marker;
        }

        if (Array.Exists(cells, marker => marker == null))
        {
            Debug.LogWarning("North Wilkesboro's scoreboard is missing one or more timing cell markers.", this);
            return;
        }

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
            return;
        tracksideNumberFont = font;

        var board = CreateObject("North Wilkesboro live scoreboard numbers", transform).transform;
        northWilkesboroScoreboard = new TextMesh[cells.Length];
        for (int i = 0; i < cells.Length; i++)
        {
            var marker = cells[i];
            Vector3 point = transform.InverseTransformPoint(marker.position);
            float station = TrackUtilityGeometry.ClosestMainDistance(customLayout, lapLength, point);
            Vector3 trackPoint = TrackPoint(station, 0f, out _, out _);
            Vector3 outwards = Vector3.ProjectOnPlane(transform.InverseTransformDirection(marker.forward), Vector3.up).normalized;
            Vector3 toTrack = Vector3.ProjectOnPlane(trackPoint - point, Vector3.up).normalized;
            if (Mathf.Abs(Vector3.Dot(outwards, toTrack)) < .25f)
                outwards = toTrack;
            else if (Vector3.Dot(outwards, toTrack) < 0f)
                outwards = -outwards;
            var field = CreateObject(i == 0 ? "Scoreboard lap" : $"Scoreboard position {i:00}", board);
            field.transform.SetLocalPositionAndRotation(point, Quaternion.LookRotation(-outwards, Vector3.up));
            var text = field.AddComponent<TextMesh>();
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.font = font;
            text.fontStyle = FontStyle.Bold;
            text.fontSize = 96;
            text.characterSize = 1f;
            text.color = new Color32(24, 22, 19, 255);
            text.text = i == 0 ? "888" : "88";
            var renderer = field.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = GetCarDecalMaterial(font);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            Vector3 size = renderer.localBounds.size;
            float cellWidth = i == 0 ? 1.76f : 1.18f;
            field.transform.localScale = new Vector3(
                cellWidth * .84f / Mathf.Max(.01f, size.x),
                1.12f * .90f / Mathf.Max(.01f, size.y), 1f);
            northWilkesboroScoreboard[i] = text;
        }
        if (pylonOrder == null)
        {
            CreateScoringOrder(Mathf.Max(cars.Length, NorthWilkesboroLeaderboardPositions));
        }
        UpdateScoringPylon();
        DisplayNorthWilkesboroScoreboard();
    }

    private void AddNorthWilkesboroScoringPylon()
    {
        foreach (var marker in GetComponentsInChildren<Transform>(true))
        {
            if (marker.name != "North Wilkesboro scoring pylon marker")
                continue;

            HideAuthoredVenueMarker(marker);
            Vector3 point = transform.InverseTransformPoint(marker.position);
            float station = TrackUtilityGeometry.ClosestMainDistance(customLayout, lapLength, point);
            Vector3 trackPoint = TrackPoint(station, 0f, out _, out _);
            BuildScoringPylon(point, Vector3.ProjectOnPlane(trackPoint - point, Vector3.up));
            return;
        }

        Debug.LogWarning("North Wilkesboro's infield scoring pylon marker is missing.", this);
    }

    private void DisplayNorthWilkesboroScoreboard()
    {
        northWilkesboroScoreboard[0].text = Mathf.Max(0, pylonLap).ToString(CultureInfo.InvariantCulture);
        for (int i = 0; i < NorthWilkesboroLeaderboardPositions; i++)
            SetTracksideRaceNumber(northWilkesboroScoreboard[i + 1], pylonOrder[i], TracksideScoringPaint(i),
                tracksideNumberFont, new Vector2(1.18f * .84f, 1.12f * .90f));
    }
}
