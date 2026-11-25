using UnityEngine;

internal sealed class LapSpeedProfile
{
    private readonly float[] factors;
    private readonly float length;
    private readonly bool[] brakingZones;
    private readonly float brakingLimit;
    private readonly bool restrictorPlate;
    private readonly bool[] flatOutKinks;
    private readonly float[] cornerExit;
    private readonly float[] turnWeights;
    private readonly float frontstretchStart, frontstretchLength;
    private readonly bool[] powerZones;

    public LapSpeedProfile(TrackDefinition track, bool calibrateBankedShortOvals = false)
    {
        restrictorPlate = track.restrictorPlate;
        bool smoothBankedEntry = calibrateBankedShortOvals && UsesBankedShortOvalEntry(track);
        length = track.lengthMiles * TrackDefinition.MetersPerMile;
        int count = Mathf.Clamp(Mathf.CeilToInt(length / 5f), 128, 4096);
        float spacing = length / count;
        var layout = track.layoutPoints?.Length > 0
            ? new TrackLayout(track.layoutPoints, length, track.widthMeters, track.bankingTransitions, track.smoothLayoutBanking)
            : null;
        if (layout != null && track.majorStyle == "Oval")
        {
            Vector3 Heading(float distance)
            {
                layout.Sample(Mathf.Repeat(distance, length), out var heading, out _);
                return heading;
            }

            var (start, end) = VehicleBehavior004.FrontstretchRange(track, length, Heading);
            frontstretchStart = start * length;
            frontstretchLength = Mathf.Repeat(end - start, 1f) * length;
        }
        var corners = new float[count];
        var directions = new int[count];
        var headingChanges = new float[count];
        var banking = new float[count];
        flatOutKinks = new bool[count];
        cornerExit = new float[count];
        turnWeights = new float[count];
        powerZones = new bool[count];
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

            banking[i] = Mathf.Abs(bank);
            headingChanges[i] = Mathf.Abs(curvature) * spacing;
            float severity = Mathf.InverseLerp(0.035f, 0.75f, Mathf.Abs(curvature));
            if (severity > 0f)
                directions[i] = curvature < 0f ? -1 : 1;
            corners[i] = restrictorPlate
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.015f, 0.075f, Mathf.Abs(curvature)))
                : 1f - Mathf.Lerp(0.09f, 0.52f, severity) * Mathf.Lerp(1f, 0.50f, Mathf.Abs(bank) / 35f);
        }

        // Shallow oval bends are full-throttle kinks, not the main end turns.
        // Classify complete same-direction arcs, including arcs crossing the lap seam.
        if (track.majorStyle != "Road" && !restrictorPlate)
        {
            int edge=0;
            while(edge<count && directions[edge]==directions[(edge+count-1)%count]) edge++;
            for(int start=edge; edge<count && start<edge+count;)
            {
                int end=start+1; float angle=headingChanges[start%count];
                while(end<edge+count && directions[end%count]==directions[start%count])
                { angle+=headingChanges[end%count]; end++; }
                if(directions[start%count]!=0 && angle<90f)
                    for(int i=start;i<end;i++) { flatOutKinks[i%count]=true; directions[i%count]=0; }
                start=end;
            }
        }
        factors = new float[count];
        bool roadCourse = track.majorStyle == "Road";
        if (!restrictorPlate && (roadCourse || track.lengthMiles < 1f))
        {
            // Normal entry uses a gentler planned rate; emergency traffic braking
            // still uses MaximumBraking and retains the original physical limit.
            brakingLimit = smoothBankedEntry ? 0.9f * 9.80665f : MaximumBraking(track);
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
                turnWeights[i] = corners[i];
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
            {
                factors[i] = directions[0] == 0 ? 1f : corners[i];
                powerZones[i] = directions[0] == 0;
                turnWeights[i] = directions[0] == 0 ? 0f : 1f;
            }
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

                // On steep short ovals the low-bank entrance must not set the speed
                // for the sustained, heavily banked middle of the entire corner.
                // Keep other track classes (and the plate branch above) unchanged.
                if (calibrateBankedShortOvals && !roadCourse && track.lengthMiles < 1f && direction != 0)
                {
                    float middleMinimum = 1f, middleBank = 0f;
                    int samples = 0;
                    for (int i = start; i < end; i++)
                    {
                        float progress = (i - start) / (float)(end - start);
                        if (progress < 1f / 3f || progress >= 2f / 3f) continue;
                        middleMinimum = Mathf.Min(middleMinimum, corners[i % count]);
                        middleBank += banking[i % count];
                        samples++;
                    }
                    if (samples > 0)
                    {
                        float support = Mathf.InverseLerp(16f, 24f, middleBank / samples);
                        // Calibrated momentum floor for high-bank short ovals, before
                        // tires/setup/weather and finite acceleration are applied.
                        minimum = Mathf.Lerp(minimum, Mathf.Max(middleMinimum, .76f), support);
                    }
                }

                for (int i = start; i < end; i++)
                {
                    float progress = (i - start) / (float)(end - start);
                    powerZones[i % count] = direction == 0 ? progress < .8f : progress >= 2f/3f;
                    if (brakingZones != null)
                        brakingZones[i % count] = direction == 0 ? progress >= 0.8f : progress < 1f / 3f;

                    if (direction == 0)
                    {
                        factors[i % count] = progress < 0.8f
                            ? 1f // Acceleration is limited by the vehicle, not an artificial straightaway ramp.
                            : Mathf.SmoothStep(1f, 0.92f, (progress - 0.8f) / 0.2f);
                    }
                    else
                    {
                        factors[i % count] = progress < 1f / 3f
                            ? Mathf.SmoothStep(0.92f, Mathf.Min(0.91f, minimum), progress * 3f)
                            : progress < 2f / 3f ? Mathf.Min(0.91f, minimum) : 1f;
                        // The final third requests power; acceleration limits provide the gradual speed rise.
                        cornerExit[i % count] = Mathf.Clamp01((progress - 2f / 3f) * 3f);
                        turnWeights[i % count] = Mathf.SmoothStep(0f,1f,progress*3f) *
                            (1f-Mathf.SmoothStep(0f,1f,(progress-2f/3f)*3f));
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

        // Adjust corner demand only, after lap normalization. Do not raise
        // straightaway targets or cancel setup/tire/weather penalties. The
        // opt-in flag belongs to VehicleSpeed003; 001/002 remain A/B baselines.
        if (calibrateBankedShortOvals && track.cornerSpeedCalibration != 1f)
            for (int i = 0; i < count; i++)
                factors[i] = Mathf.Min(normalization,
                    factors[i] * Mathf.Lerp(1f, track.cornerSpeedCalibration, turnWeights[i]));

        if (smoothBankedEntry && boundary < count)
        {
            float reference = Mathf.Max(1f, track.qualifyingReferenceMph * TrackDefinition.MetersPerMile / 3600f);
            // Work backward from the steady middle-third target. This moves braking
            // upstream by the distance required at the planned rate, including the
            // end of the preceding straight, without slowing the exit third.
            for (int start = boundary; start < boundary + count;)
            {
                int end = start + 1;
                while (end < boundary + count && directions[end % count] == directions[start % count]) end++;
                if (directions[start % count] != 0)
                {
                    int middle = start + Mathf.CeilToInt((end - start) / 3f);
                    int earliest = start;
                    while (earliest > start - count && directions[(earliest - 1 + count) % count] == 0) earliest--;
                    for (int i = middle - 1; i >= earliest; i--)
                    {
                        int index = (i + count) % count, next = (i + 1 + count) % count;
                        float nextSpeed = factors[next] * reference;
                        float entryLimit = Mathf.Sqrt(nextSpeed * nextSpeed + 2f * brakingLimit * spacing) / reference;
                        if (entryLimit >= factors[index]) continue;
                        factors[index] = entryLimit;
                        brakingZones[index] = true;
                        powerZones[index] = false;
                    }
                }
                start = end;
            }
        }
    }

    internal static bool UsesBankedShortOvalEntry(TrackDefinition track) =>
        !track.restrictorPlate && track.majorStyle != "Road" && track.lengthMiles < 1f &&
        (track.banking.turns12 + track.banking.turns34) * .5f > 16f;

    internal static float MaximumBraking(TrackDefinition track) =>
        (track.majorStyle == "Road" ? 2.7f : 2.5f) * 9.80665f;

    internal float TurnWeightAt(float distance)
    {
        float sample=Mathf.Repeat(distance,length)/length*turnWeights.Length;
        int i=(int)sample;
        return Mathf.Lerp(turnWeights[i%turnWeights.Length],turnWeights[(i+1)%turnWeights.Length],sample-i);
    }

    internal float TireTurnWeightAt(float distance)
    {
        float weight = TurnWeightAt(distance);
        if (weight <= 0f || frontstretchLength <= 0f)
            return weight;

        // Curved oval frontstretches carry speed-profile scrub but remain outside the turns.
        const float transitionMeters = 15f;
        float fromEntry = Mathf.Repeat(distance - frontstretchStart, length);
        float room = Mathf.Min(fromEntry - frontstretchLength, length - fromEntry);
        return weight * Mathf.SmoothStep(0f, 1f, room / transitionMeters);
    }

    internal bool FullThrottleAt(float distance) => restrictorPlate || flatOutKinks[(int)(Mathf.Repeat(distance,length)/length*flatOutKinks.Length)%flatOutKinks.Length] || powerZones[(int)(Mathf.Repeat(distance,length)/length*powerZones.Length)%powerZones.Length];

    internal float CornerExitAt(float distance) => cornerExit[(int)(Mathf.Repeat(distance,length)/length*cornerExit.Length)%cornerExit.Length];

    internal bool FlatOutKinkAt(float distance) => flatOutKinks[(int)(Mathf.Repeat(distance,length)/length*flatOutKinks.Length)%flatOutKinks.Length];

    public float BrakingLimitAt(float distance)
    {
        if (brakingZones == null)
            return 0f;

        int index = (int)(Mathf.Repeat(distance, length) / length * brakingZones.Length);
        return brakingZones[index % brakingZones.Length] ? brakingLimit : 0f;
    }

    internal float EstimatePeakSpeed(float referenceSpeed, float forceFactor)
    {
        // Settle into the track's speed profile before measuring the reference lap.
        // The reference car uses clean air and neutral setup and weather.
        float spacing = length / factors.Length;
        float speed = referenceSpeed;
        float peak = 0f;
        for (int lap = 0; lap < 3; lap++)
        {
            for (int i = 0; i < factors.Length; i++)
            {
                float distance = i * spacing;
                float target = SpeedAt(distance, referenceSpeed);
                float brake = BrakingLimitAt(distance) * forceFactor;
                float acceleration = RaceSessionRun.ReferenceAcceleration(speed) * forceFactor;
                float seconds = spacing / Mathf.Max(1f, speed);
                if (brake <= 0f && FullThrottleAt(distance))
                    speed = RaceSessionRun.FullThrottleSpeed(speed, target, acceleration, seconds);
                else if (target < speed)
                    speed = Mathf.Max(target, speed - (brake > 0f ? brake : RaceSessionRun.CoastingDeceleration) * seconds);
                else
                    speed = Mathf.Min(target, speed + acceleration * seconds);

                if (lap == 2)
                    peak = Mathf.Max(peak, speed);
            }
        }
        return peak;
    }

    public float SpeedAt(float distance, float baseSpeed)
    {
        float sample = Mathf.Repeat(distance, length) / length * factors.Length;
        int index = (int)sample;
        float adjustment = Mathf.Lerp(factors[index % factors.Length], factors[(index + 1) % factors.Length], sample - index);
        return restrictorPlate ? baseSpeed + adjustment : baseSpeed * adjustment;
    }
}
