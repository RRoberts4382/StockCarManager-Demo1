using UnityEngine;

public sealed partial class TrackPreview
{
    private static readonly (string Name, float Station)[] NorthWilkesboroSafetySites =
    {
        ("Start finish", 0f), ("Frontstretch exit", .147f), ("Turn 1", .231f),
        ("Turn 2", .329f), ("Backstretch entrance", .415f), ("Backstretch exit", .663f),
        ("Turn 3", .747f), ("Turn 4", .848f), ("Frontstretch entrance", .916f)
    };

    private void AddNorthWilkesboroVenue()
    {
        AddNorthWilkesboroScoringPylon();
        AddNorthWilkesboroScoreboard();
        AddVenueHaulers(VenueSceneryName);
        foreach (var marker in GetComponentsInChildren<Transform>(true))
            if (marker.name.StartsWith("North Wilkesboro pace ", System.StringComparison.Ordinal))
                HideAuthoredVenueMarker(marker);
        AddAuthoredOvalVenue(VenueSceneryName, NorthWilkesboroSafetySites, 23013);
    }
}
