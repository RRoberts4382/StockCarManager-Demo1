using System;
using UnityEngine;

internal static class TrackRatings
{
    private const float MaxAeroLength = 2.5f;
    private const float HandlingBandStart = 1f;
    private const float HandlingBandEnd = 1.5f;

    public static float TransmissionWeight(TrackDefinition track) =>
        track.majorStyle == "Road" ? 1f : track.lengthMiles <= 1f ? 0.25f : 0f;

    public static void Apply(TrackDefinition track, float shortest, float longest)
    {
        float length = track.lengthMiles;
        var weights = track.weights;
        weights.transmission = TransmissionWeight(track);
        weights.aerodynamics = length >= MaxAeroLength ? 1f : Mathf.InverseLerp(shortest, MaxAeroLength, length);
        weights.handling = length < HandlingBandStart
            ? Mathf.InverseLerp(shortest, HandlingBandStart, length)
            : length > HandlingBandEnd ? 1f - Mathf.InverseLerp(HandlingBandEnd, longest, length) : 1f;
        weights.brakes = Mathf.Approximately(length, shortest) ? 1f : 0f;

        if (track.name.IndexOf("Darlington", StringComparison.OrdinalIgnoreCase) >= 0)
            weights.tireWear = 1f;
        else if (!weights.ratingsAssigned)
            weights.tireWear = TireWearForName(track.name);
        weights.ratingsAssigned = true;
    }

    private static float TireWearForName(string name)
    {
        uint hash = 2166136261;
        foreach (char letter in name.Trim().ToUpperInvariant())
            hash = unchecked((hash ^ letter) * 16777619);
        return 0.4f + (hash % 201) / 1000f;
    }
}
