using UnityEngine;

public sealed partial class TrackPreview
{
    // Stations follow the authored lap from start/finish through turns 1–4.
    private static readonly (string Name, float Station)[] RichmondSafetySites =
    {
        ("Start finish", 0f),
        ("Front straight exit", .168848f),
        ("Turn 1", .252505f),
        ("Turn 2", .339826f),
        ("Back straight entrance", .441592f),
        ("Back straight exit", .667027f),
        ("Turn 3", .717433f),
        ("Turn 4", .828466f),
        ("Front straight entrance", .900998f)
    };

    private float LimitRichmondApronOffset(float distance, float offset)
    {
        if (definition.id != RichmondTrackId || !importedTrackGeometry)
            return offset;

        // Follow the narrowed apron outside the full pit divider. The tapers
        // finish before the unchanged pit entry and exit connector attachments.
        float lapFraction = Mathf.Repeat(distance, lapLength) / lapLength;
        float weight = lapFraction >= .807f
            ? Mathf.InverseLerp(.807f, .858f, lapFraction)
            : Mathf.InverseLerp(.243f, .203f, lapFraction);
        return Mathf.SmoothStep(offset, Mathf.Min(offset, 2.15f), weight);
    }

    private void AddRichmondVenue()
    {
        AddRichmondScoringPylon();
        AddVenueHaulers("Richmond");
        foreach (var marker in GetComponentsInChildren<Transform>(true))
            if (marker.name.StartsWith("Richmond pace ", System.StringComparison.Ordinal))
                HideAuthoredVenueMarker(marker);
        AddAuthoredOvalVenue("Richmond", RichmondSafetySites, 19019);
    }

    private void AddRichmondScoringPylon()
    {
        foreach (var marker in GetComponentsInChildren<Transform>(true))
        {
            if (marker.name != "Richmond scoring pylon marker")
                continue;

            HideAuthoredVenueMarker(marker);
            Vector3 position = transform.InverseTransformPoint(marker.position);
            Vector3 finish = TrackPoint(definition.StartFinishFraction * lapLength, 0f, out var heading, out _);
            heading = Vector3.ProjectOnPlane(heading, Vector3.up).normalized;
            position += heading * Vector3.Dot(finish - position, heading);
            Vector3 towardTrack = Vector3.ProjectOnPlane(finish - position, Vector3.up).normalized;
            BuildScoringPylon(position, towardTrack);
            return;
        }

        AddScoringPylonAtPitWalkway("Richmond", definition.pitRoad.widthMeters * .5f + PitWallThickness + 9.5f);
    }
}
