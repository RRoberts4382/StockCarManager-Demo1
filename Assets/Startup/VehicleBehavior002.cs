using System;
using UnityEngine;

internal sealed class VehicleBehavior002 : IVehicleBehaviorModel
{
    public string Id => "vehicle-behavior-002";
    public string Name => "VehicleBehavior002";

    public Func<float, float> BuildRacingLine(float lapLength, float inside, bool isOval, Func<float, Vector3> heading)
    {
        int count = Mathf.Clamp(Mathf.CeilToInt(lapLength / 4f), 128, 4096);
        float spacing = lapLength / count;
        var line = new float[count];
        var turns = new int[count];
        for (int i = 0; i < count; i++)
        {
            float distance = i * spacing;
            var before = heading(distance - spacing);
            var after = heading(distance + spacing);
            before.y = after.y = 0f;
            float angle = Vector3.SignedAngle(before, after, Vector3.up);
            float curvature = angle / (2f * spacing);
            if ((isOval ? curvature * inside : Mathf.Abs(curvature)) >= 0.035f)
                turns[i] = curvature > 0f ? 1 : -1;
        }

        int boundary = 0;
        while (boundary < count && turns[boundary] == turns[(boundary + count - 1) % count])
            boundary++;

        if (boundary == count)
        {
            for (int i = 0; i < count; i++)
                line[i] = turns[0] == 0 ? -inside * 0.82f : turns[0] * 0.70f;
        }
        else
        {
            int start = boundary;
            while (start < boundary + count)
            {
                int turn = turns[start % count];
                int end = start + 1;
                while (end < boundary + count && turns[end % count] == turn)
                    end++;

                int previousTurn = turns[(start + count - 1) % count];
                int nextTurn = turns[end % count];
                float entry = previousTurn == 0 ? -turn * 0.82f : 0f;
                float exit = nextTurn == 0 ? -turn * 0.82f : 0f;
                for (int i = start; i < end; i++)
                {
                    float progress = (i - start) / (float)(end - start);
                    line[i % count] = turn == 0
                        ? (isOval ? -inside * 0.82f : Mathf.SmoothStep(-previousTurn * 0.82f, -nextTurn * 0.82f, progress))
                        : progress < 1f / 3f
                            ? Mathf.SmoothStep(entry, turn * 0.70f, progress * 3f)
                            : Mathf.SmoothStep(turn * 0.70f, exit, (progress - 2f / 3f) * 3f);
                }

                start = end;
            }
        }

        // Blend across segment boundaries, including the lap seam.
        int radius = Mathf.Max(2, Mathf.CeilToInt(Mathf.Min(28f, lapLength * 0.025f) / spacing));
        var smoothed = new float[count];
        for (int i = 0; i < count; i++)
        {
            float sum = 0f;
            for (int offset = -radius; offset <= radius; offset++)
                sum += line[(i + offset + count) % count] * (radius + 1 - Mathf.Abs(offset));
            smoothed[i] = sum / ((radius + 1f) * (radius + 1f));
        }

        return distance =>
        {
            float sample = Mathf.Repeat(distance, lapLength) / spacing;
            int index = (int)sample;
            return Mathf.Lerp(smoothed[index % count], smoothed[(index + 1) % count], sample - index);
        };
    }

    public void AvoidTraffic(float lapLength, float inside, float seconds, bool allowPassing,
        float[] distances, float[] widths, bool[] onTrack, float[] lanes, float[] targets,
        float[] currentSpeeds, float[] speeds, float brakingLimit, bool[] draftLine = null, bool respondToTraffic = true, float[] brakingFactors = null)
    {
        const float carLength = 2.9f;
        const float carWidth = 1.8f;
        const float passLookAhead = 2f * carLength; // One car length between bumpers.
        const float lateralClearance = 2f;
        const float carSpacing = 3.4f;
        const float rearClearance = carLength + 3f * TrackDefinition.MetersPerFoot; // Bumper clearance, not center separation.
        for (int i = 0; i < lanes.Length; i++)
        {
            if (!onTrack[i] || speeds[i] <= 0f)
                continue;

            float target = targets[i];
            if (!respondToTraffic)
            {
                lanes[i] = Mathf.MoveTowards(lanes[i], target, 3f * seconds);
                continue;
            }
            float nearest = float.PositiveInfinity;
            int blocker = -1;
            for (int j = 0; j < lanes.Length; j++)
            {
                if (i == j || !onTrack[j])
                    continue;
                float gap = Mathf.Repeat(distances[j] - distances[i], lapLength);
                if (gap < nearest && gap <= passLookAhead && Mathf.Abs(lanes[i] - lanes[j]) < carWidth + 0.2f &&
                    speeds[i] > speeds[j])
                {
                    nearest = gap;
                    blocker = j;
                }
            }

            if (allowPassing && blocker >= 0 && draftLine?[i] != true)
            {
                float inner = lanes[blocker] + inside * (lateralClearance + 0.3f);
                float outer = lanes[blocker] - inside * (lateralClearance + 0.3f);
                if (LaneClear(inner, nearest + carSpacing))
                    target = inner;
                else if (LaneClear(outer, nearest + carSpacing))
                    target = outer;
            }

            if (blocker < 0 && !LaneClear(target, 8f))
                target = lanes[i];

            float next = Mathf.MoveTowards(lanes[i], target, 3f * seconds);
            if (LaneClear(next, carSpacing, changingLane: true))
                lanes[i] = next;

            bool LaneClear(float candidate, float ahead, bool changingLane = false)
            {
                if (Mathf.Abs(candidate) > Mathf.Max(0f, widths[i] * 0.5f - 1.05f))
                    return false;
                for (int j = 0; j < lanes.Length; j++)
                {
                    if (j == i || !onTrack[j])
                        continue;
                    float gap = Mathf.Repeat(distances[j] - distances[i] + lapLength * .5f, lapLength) - lapLength * .5f;
                    float horizon = Mathf.Max(seconds, Mathf.Min(1f, Mathf.Abs(candidate-lanes[i])/3f));
                    float futureGap = gap + (currentSpeeds[j]-currentSpeeds[i])*horizon;
                    // Check the swept longitudinal interval and the whole lane-change corridor.
                    if (Mathf.Min(gap,futureGap) > ahead || Mathf.Max(gap,futureGap) < -rearClearance)
                        continue;
                    float low = Mathf.Min(lanes[i],candidate), high = Mathf.Max(lanes[i],candidate);
                    float separation = lanes[j] < low ? low-lanes[j] : lanes[j] > high ? lanes[j]-high : 0f;
                    bool escaping = Mathf.Abs(candidate-lanes[j]) > Mathf.Abs(lanes[i]-lanes[j]) &&
                        (candidate-lanes[j])*(lanes[i]-lanes[j]) >= 0f;
                    if (separation < lateralClearance && !escaping)
                        return false;
                }
                return true;
            }
        }

        if (!respondToTraffic)
            return;

        for (int pass = 0; pass < lanes.Length; pass++)
        {
            bool changed = false;
            for (int i = 0; i < lanes.Length; i++)
            {
                if (!onTrack[i] || speeds[i] <= 0f)
                    continue;
                for (int j = 0; j < lanes.Length; j++)
                {
                    if (j == i || !onTrack[j] || Mathf.Abs(lanes[i] - lanes[j]) >= lateralClearance)
                        continue;
                    float gap = Mathf.Repeat(distances[j] - distances[i], lapLength);
                    if (gap == 0f && j > i)
                        continue;
                    float clearance = Mathf.Max(0f, gap - carSpacing);
                    // Reserve a step of closing distance before applying the stopping-distance bound.
                    float closingSpeed = Mathf.Max(0f, currentSpeeds[i] - speeds[j]);
                    float brakingDistance = Mathf.Max(0f, clearance - closingSpeed * seconds);
                    float carBrakingLimit = brakingLimit * (brakingFactors == null ? 1f : brakingFactors[i]);
                    float limit = speeds[j] + Mathf.Sqrt(2f * carBrakingLimit * brakingDistance);
                    limit = Mathf.Max(limit, currentSpeeds[i] - carBrakingLimit * seconds);
                    if (speeds[i] > limit)
                    {
                        speeds[i] = limit;
                        changed = true;
                    }
                }
            }
            if (!changed)
                break;
        }
    }

    public float LaneOffset(int carIndex, float roadWidth, float distanceToPitMerge, float racingLine)
    {
        float usableHalfWidth = Mathf.Max(0f, roadWidth * 0.5f - 1.1f);
        float variation = (carIndex % 2 == 0 ? -1f : 1f) * 0.12f;
        float lane = usableHalfWidth * (racingLine + variation);
        return lane * Mathf.SmoothStep(0f, 1f, distanceToPitMerge / 15f);
    }
}
