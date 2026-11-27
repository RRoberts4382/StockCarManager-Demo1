using UnityEngine;

public sealed partial class TrackPreview
{
    private static readonly (string Name, float Station)[] LasVegasSafetySites =
    {
        ("Frontstretch exit", .127f), ("Turn 1 midpoint", .215f), ("Turn 2 midpoint", .315f),
        ("Backstretch entrance", .400f), ("Backstretch exit", .600f),
        ("Turn 3 midpoint", .685f), ("Turn 4 midpoint", .785f), ("Frontstretch entrance", .873f)
    };

    private void AddLasVegasVenue()
    {
        const string venue = "Las Vegas";
        const float walkwayCenter = 17.524f;
        foreach (var marker in GetComponentsInChildren<Transform>(true))
        {
            if (marker.name == venue + " Spotter camera")
                iowaSpotterPosition = transform.InverseTransformPoint(marker.position);
        }

        AddScoringPylonAtPitWalkway(venue, walkwayCenter);
        AddVenueHaulers(venue);
        AddAuthoredOvalVenue(venue, LasVegasSafetySites, 26071);
    }
}
