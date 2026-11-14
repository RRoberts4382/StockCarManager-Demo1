using UnityEngine;

internal sealed class LapSpeedProfile
{
    private readonly float[] factors;
    private readonly float length;
    private readonly bool[] brakingZones;
    private readonly float brakingLimit;
    private readonly bool restrictorPlate;

    public LapSpeedProfile(TrackDefinition track)
    {
        restrictorPlate = track.restrictorPlate;
        length = track.lengthMiles * TrackDefinition.MetersPerMile;
        int count = Mathf.Clamp(Mathf.CeilToInt(length / 5f), 128, 4096);
        float spacing = length / count;
        var layout = track.layoutPoints?.Length > 0 ? new TrackLayout(track.layoutPoints, length, track.widthMeters) : null;
        var corners = new float[count];
        var directions = new int[count];
        for (int i = 0; i < count; i++)
        {
            float distance = i * spacing;
            float bank;
            float curvature;
            if (layout != null)
            {
                layout.Sample(Mathf.Repeat(distance - spacing, length), out var before, out _);
                layout.Sample(Mathf.Repeat(distance + spacing, length), out var after, out _);
                layout.Sample(distance, out _, out bank);
                before.y = after.y = 0f;
                curvature = Vector3.SignedAngle(before, after, Vector3.up) / (2f * spacing);
            }
            else
            {
                float turnLength = Mathf.PI * track.OvalTurnRadius;
                bool first = distance >= track.OvalStraightLength && distance < track.OvalStraightLength + turnLength;
                bool second = distance >= 2f * track.OvalStraightLength + turnLength;
                curvature = first || second ? -Mathf.Rad2Deg / track.OvalTurnRadius : 0f;
                bank = first ? track.banking.turns12 : track.banking.turns34;
            }

            float severity = Mathf.InverseLerp(0.035f, 0.75f, Mathf.Abs(curvature));
            if (severity > 0f)
                directions[i] = curvature < 0f ? -1 : 1;
            corners[i] = restrictorPlate
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.015f, 0.075f, Mathf.Abs(curvature)))
                : 1f - Mathf.Lerp(0.09f, 0.52f, severity) * Mathf.Lerp(1f, 0.50f, Mathf.Abs(bank) / 35f);
        }

        factors = new float[count];
        bool roadCourse = track.majorStyle == "Road";
        if (!restrictorPlate && (roadCourse || track.lengthMiles < 1f))
        {
            brakingLimit = MaximumBraking(track);
            brakingZones = new bool[count];
        }
        if (restrictorPlate)
        {
            const float scrubMph = 2.5f;
            float scrubSpeed = scrubMph * TrackDefinition.MetersPerMile / 3600f;
            float totalLoss = 0f;
            for (int i = 0; i < count; i++)
            {
                // Blend the tire scrub in and out as curvature builds and fades.
                float loss = scrubSpeed * corners[i];
                factors[i] = loss;
                totalLoss += loss;
            }

            float averageLoss = totalLoss / count;
            for (int i = 0; i < count; i++)
                factors[i] = averageLoss - factors[i];
            return;
        }

        int boundary = 0;
        while (boundary < count && directions[boundary] == directions[(boundary + count - 1) % count])
            boundary++;

        if (boundary == count)
        {
            for (int i = 0; i < count; i++)
                factors[i] = directions[0] == 0 ? 1f : corners[i];
        }
        else
        {
            for (int start = boundary; start < boundary + count;)
            {
                int direction = directions[start % count];
                int end = start + 1;
                float minimum = corners[start % count];
                while (end < boundary + count && directions[end % count] == direction)
                {
                    minimum = Mathf.Min(minimum, corners[end % count]);
                    end++;
                }

                for (int i = start; i < end; i++)
                {
                    float progress = (i - start) / (float)(end - start);
                    if (brakingZones != null)
                        brakingZones[i % count] = direction == 0 ? progress >= 0.8f : progress < 1f / 3f;

                    if (direction == 0)
                    {
                        factors[i % count] = progress < 0.8f
                            ? Mathf.Lerp(0.92f, 1f, progress / 0.8f)
                            : Mathf.SmoothStep(1f, 0.92f, (progress - 0.8f) / 0.2f);
                    }
                    else
                    {
                        factors[i % count] = progress < 1f / 3f
                            ? Mathf.SmoothStep(0.92f, Mathf.Min(0.91f, minimum), progress * 3f)
                            : Mathf.SmoothStep(Mathf.Min(0.91f, minimum), 0.92f, (progress - 2f / 3f) * 3f);
                    }
                }
                start = end;
            }
        }

        float reciprocalSum = 0f;
        foreach (float factor in factors)
            reciprocalSum += 1f / factor;
        float normalization = reciprocalSum / count;
        for (int i = 0; i < count; i++)
            factors[i] *= normalization;
    }

    internal static float MaximumBraking(TrackDefinition track) =>
        (track.majorStyle == "Road" ? 2.7f : 2.5f) * 9.80665f;

    public float BrakingLimitAt(float distance)
    {
        if (brakingZones == null)
            return 0f;

        int index = (int)(Mathf.Repeat(distance, length) / length * brakingZones.Length);
        return brakingZones[index % brakingZones.Length] ? brakingLimit : 0f;
    }

    public float SpeedAt(float distance, float baseSpeed)
    {
        float sample = Mathf.Repeat(distance, length) / length * factors.Length;
        int index = (int)sample;
        float adjustment = Mathf.Lerp(factors[index % factors.Length], factors[(index + 1) % factors.Length], sample - index);
        return restrictorPlate ? baseSpeed + adjustment : baseSpeed * adjustment;
    }
}
