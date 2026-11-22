using UnityEngine;

internal static class TrackRatings
{
    private const float MaxAeroLength = 2.5f;

    public static float TransmissionWeight(TrackDefinition track) =>
        track.majorStyle == "Road" ? 1f : track.lengthMiles <= 1f ? 0.25f : 0f;

    public static void Apply(TrackDefinition track, float shortest)
    {
        float length = track.lengthMiles;
        var weights = track.weights;
        weights.transmission = TransmissionWeight(track);
        weights.aerodynamics = track.majorStyle == "Road" ? 0.25f :
            length >= MaxAeroLength ? 1f : Mathf.InverseLerp(shortest, MaxAeroLength, length);
        weights.brakes = track.majorStyle == "Road" || Mathf.Approximately(length, shortest) ? 1f : 0f;
    }
}
