using System;
using System.Collections.Generic;
using UnityEngine;

internal sealed class VehicleBehavior003 : IVehicleBehaviorModel
{
    // Bound to the active context before each update: registry models are shared.
    internal IReadOnlyList<SessionCarResult> Drivers { get; set; }
    internal float RaceProgressAggression { get; set; }
    internal static float CombinedAggression(float driver, float race)
    {
        driver = float.IsNaN(driver) || float.IsInfinity(driver) ? .5f : Mathf.Clamp01(driver);
        race = float.IsNaN(race) || float.IsInfinity(race) ? 0f : Mathf.Clamp01(race);
        // Preserve personality differences even at maximum late-race urgency.
        return driver + (1f - driver) * .5f * race;
    }

    internal static float RaceAggression(bool isRace, float completedLaps, int scheduledLaps,
        int stage, int stageStart, int stageEnd, int bonusStages = RaceSession.TestBonusStages)
    {
        if (!isRace || scheduledLaps <= 0) return 0f;
        float maximumAt = scheduledLaps * .9f;
        if (completedLaps >= maximumAt) return 1f;
        if (stage <= bonusStages)
        {
            float progress = Mathf.Clamp01((completedLaps - stageStart) / Mathf.Max(1f, stageEnd - stageStart));
            return .3f * Mathf.Clamp01((stage - 1f + progress) / bonusStages);
        }
        return Mathf.Lerp(.65f, 1f, Mathf.Clamp01((completedLaps - stageStart) / Mathf.Max(.001f, maximumAt - stageStart)));
    }

    private float Aggression(int index)
    {
        float value = Drivers != null && index < Drivers.Count
            ? Drivers[index].driver?.personalityTraits?.aggression ?? .5f : .5f;
        return CombinedAggression(value, RaceProgressAggression);
    }

    public string Id => "vehicle-behavior-003";
    public string Name => "VehicleBehavior003";

    internal const float MaximumLateralSpeed = 3f;

    public Func<float, float> BuildRacingLine(float lapLength, float inside, bool isOval, Func<float, Vector3> heading) =>
        BuildTrackRacingLine(lapLength, inside, isOval, heading, null);

    internal Func<float, float> BuildTrackRacingLine(float lapLength, float inside, bool isOval,
        Func<float, Vector3> heading, TrackDefinition track)
    {
        int count = Mathf.Clamp(Mathf.CeilToInt(lapLength / 4f), 128, 4096);
        float spacing = lapLength / count;
        var line = new float[count];
        var turns = new int[count];
        var headingChanges = new float[count];
        for (int i = 0; i < count; i++)
        {
            float distance = i * spacing;
            var before = heading(distance - spacing);
            var after = heading(distance + spacing);
            before.y = after.y = 0f;
            float angle = Vector3.SignedAngle(before, after, Vector3.up);
            float curvature = angle / (2f * spacing);
            headingChanges[i] = Mathf.Abs(curvature) * spacing;
            if ((isOval ? curvature * inside : Mathf.Abs(curvature)) >= 0.035f)
                turns[i] = curvature > 0f ? 1 : -1;
        }

        if (isOval) FilterMinorOvalTurns(turns, headingChanges, spacing);

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

        if (isOval) JoinFrontstretchArc(line, turns, headingChanges, inside);
        if (track != null && LapSpeedProfile.UsesBankedShortOvalEntry(track))
            AnticipateCornerEntry(line, turns, spacing, track.widthMeters,
                Mathf.Max(1f, track.qualifyingReferenceMph * TrackDefinition.MetersPerMile / 3600f) * 1.1f);

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

    internal static void AnticipateCornerEntry(float[] line, int[] turns, float spacing, float width, float speed)
    {
        int count = turns.Length, boundary = 0;
        if (count == 0) return;
        while (boundary < count && turns[boundary] == turns[(boundary + count - 1) % count]) boundary++;
        if (boundary == count) return;
        float halfWidth = Mathf.Max(.1f, width * .5f - 1.1f);
        float maximumStep = MaximumLateralSpeed * spacing / (Mathf.Max(1f, speed) * halfWidth);
        for (int start = boundary; start < boundary + count;)
        {
            int end = start + 1, turn = turns[start % count];
            while (end < boundary + count && turns[end % count] == turn) end++;
            if (turn != 0)
            {
                int middle = start + Mathf.CeilToInt((end - start) / 3f);
                int earliest = start;
                while (earliest > start - count && turns[(earliest - 1 + count) % count] == 0) earliest--;
                for (int i = middle - 1; i >= earliest; i--)
                {
                    int index = (i + count) % count, next = (i + 1 + count) % count;
                    // Move earlier points inward, never beyond the existing center-third line.
                    line[index] = turn * Mathf.Max(turn * line[index], turn * line[next] - maximumStep);
                }
            }
            start = end;
        }
    }

    // Connect the shallow bends BETWEEN the two main oval ends as one frontstretch.
    // A straight backstretch has no retained bend, so it keeps its outside line.
    internal static void JoinFrontstretchArc(float[] line, int[] turns, float[] headingChanges, float inside)
    {
        int count=turns.Length, boundary=0;
        if(count==0) return;
        while(boundary<count && turns[boundary]==turns[(boundary+count-1)%count]) boundary++;
        if(boundary==count) return;
        var ends=new List<(int Start,int End)>();
        for(int start=boundary;start<boundary+count;)
        {
            int end=start+1; float angle=headingChanges[start%count];
            while(end<boundary+count && turns[end%count]==turns[start%count])
            { angle+=headingChanges[end%count]; end++; }
            if(turns[start%count]!=0 && angle>=90f) ends.Add((start,end));
            start=end;
        }
        if(ends.Count!=2) return;
        for(int k=0;k<ends.Count;k++)
        {
            int start=ends[k].End;
            int end=ends[(k+1)%ends.Count].Start+(k==ends.Count-1?count:0);
            bool hasBend=false;
            for(int i=start;i<end;i++) hasBend |= turns[i%count]!=0;
            if(!hasBend || end-start<3) continue;
            for(int i=start;i<end;i++)
            {
                float progress=(i-start)/(float)(end-start-1);
                // Keep one continuous low line across the lap seam and between the kinks.
                line[i%count]=progress<1f/3f
                    ? Mathf.SmoothStep(-inside*.82f,inside*.98f,progress*3f)
                    : Mathf.SmoothStep(inside*.98f,-inside*.82f,(progress-2f/3f)*3f);
            }
        }
    }

    // Tiny spline curvature lobes near a straight are not independent racing corners.
    // Preserve real tri/quad-oval bends; road courses keep their original turn segmentation.
    internal static void FilterMinorOvalTurns(int[] turns, float[] headingChanges, float spacing)
    {
        int count=turns.Length;
        if(count==0) return;
        var original=(int[])turns.Clone();
        int boundary=0;
        while(boundary<count && original[boundary]==original[(boundary+count-1)%count]) boundary++;
        if(boundary==count) return;
        for(int start=boundary;start<boundary+count;)
        {
            int end=start+1;
            float angle=headingChanges[start%count];
            while(end<boundary+count && original[end%count]==original[start%count])
            { angle+=headingChanges[end%count]; end++; }
            if(original[start%count]!=0 && angle<5f && (end-start)*spacing<=80f)
                for(int i=start;i<end;i++) turns[i%count]=0;
            start=end;
        }
    }

    public void AvoidTraffic(float lapLength, float inside, float seconds, bool allowPassing,
        float[] distances, float[] widths, bool[] onTrack, float[] lanes, float[] targets,
        float[] currentSpeeds, float[] speeds, float brakingLimit, bool[] draftLine = null, bool respondToTraffic = true, float[] brakingFactors = null)
    {
        const float carLength = 2.9f;
        const float carWidth = 1.8f;
        const float passLookAhead = 2f * carLength; // One car length between bumpers.
        // Aggression changes the driver's caution, never the physical collision footprint.
        // Retain neutral clearances when overtaking is prohibited (yellow flags).
        float Risk(int index) => allowPassing ? Aggression(index) : .5f;
        for (int i = 0; i < lanes.Length; i++)
        {
            if (!onTrack[i] || speeds[i] <= 0f)
                continue;

            float aggression = Risk(i);
            float lateralClearance = carWidth + Mathf.Lerp(.4f, .02f, aggression);
            float carSpacing = carLength + Mathf.Lerp(.9f, .08f, aggression);
            float rearClearance = carLength + Mathf.Lerp(3f * TrackDefinition.MetersPerFoot, .08f, aggression);
            float anticipation = Mathf.Lerp(1f, .2f, aggression);
            float target = targets[i];
            if (!respondToTraffic)
            {
                lanes[i] = Mathf.MoveTowards(lanes[i], target, MaximumLateralSpeed * seconds);
                continue;
            }
            float nearest = float.PositiveInfinity;
            int blocker = -1;
            for (int j = 0; j < lanes.Length; j++)
            {
                if (i == j || !onTrack[j])
                    continue;
                float gap = Mathf.Repeat(distances[j] - distances[i], lapLength);
                if (gap < nearest && gap <= passLookAhead && Mathf.Abs(lanes[i] - lanes[j]) < lateralClearance &&
                    speeds[i] > speeds[j])
                {
                    nearest = gap;
                    blocker = j;
                }
            }

            if (allowPassing && blocker >= 0 && draftLine?[i] != true)
            {
                float inner = lanes[blocker] + inside * (lateralClearance + Mathf.Lerp(.3f, .02f, aggression));
                float outer = lanes[blocker] - inside * (lateralClearance + Mathf.Lerp(.3f, .02f, aggression));
                if (Mathf.Abs(target-lanes[blocker]) >= lateralClearance && LaneClear(target,nearest+carSpacing))
                { /* The preferred line itself provides the pass; avoid an unnecessary detour. */ }
                else if (LaneClear(inner, nearest + carSpacing))
                    target = inner;
                else if (LaneClear(outer, nearest + carSpacing))
                    target = outer;
            }

            // Move toward the preferred line as soon as the next swept step is safe.
            // Do not wait for the entire lateral corridor to be empty.

            float next = Mathf.MoveTowards(lanes[i], target, MaximumLateralSpeed * seconds);
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
                    float horizon = Mathf.Max(seconds, Mathf.Min(anticipation, Mathf.Abs(candidate-lanes[i])/3f * anticipation));
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
                float aggression = Risk(i);
                float lateralClearance = carWidth + Mathf.Lerp(.4f, .02f, aggression);
                float carSpacing = carLength + Mathf.Lerp(.9f, .08f, aggression);
                for (int j = 0; j < lanes.Length; j++)
                {
                    if (j == i || !onTrack[j] || Mathf.Abs(lanes[i] - lanes[j]) >= lateralClearance)
                        continue;
                    float gap = Mathf.Repeat(distances[j] - distances[i], lapLength);
                    if (gap == 0f && j > i)
                        continue;
                    float clearance = Mathf.Max(0f, gap - carSpacing);
                    float carBrakingLimit = brakingLimit * (brakingFactors == null ? 1f : brakingFactors[i]);
                    float leaderBrakingLimit = brakingLimit * (brakingFactors == null ? 1f : brakingFactors[j]);
                    float limit = FollowingSpeedLimit(currentSpeeds[i], speeds[j], clearance,
                        seconds, carBrakingLimit, leaderBrakingLimit, aggression);
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

    internal static float FollowingSpeedLimit(float currentSpeed, float leaderNextSpeed, float clearance,
        float seconds, float brakingLimit, float leaderBrakingLimit, float aggression)
    {
        if (seconds <= 0f || brakingLimit <= 0f) return currentSpeed;
        float leaderSpeed = Mathf.Max(0f, leaderNextSpeed);
        float closingSpeed = Mathf.Max(0f, currentSpeed - leaderSpeed);
        // Driver-specific spacing is already removed from clearance. Aggressive drivers
        // also reserve less reaction distance, but neither driver's brake capability changes.
        float distance = Mathf.Max(0f, clearance - closingSpeed * seconds * Mathf.Lerp(1.5f, 0f, aggression));
        float constantSpeedLimit = leaderSpeed + Mathf.Sqrt(2f * brakingLimit * distance);
        float limit = constantSpeedLimit;
        if (leaderBrakingLimit > 0f)
        {
            // Keep enough room if the leader continues (or starts) braking to a stop.
            // Include this integration step, which advances by nextSpeed * seconds.
            // v*dt + v^2/(2*b) <= clearance + leaderNext*dt + leaderNext^2/(2*leaderB).
            // Using the leader's already-limited next speed propagates braking down a queue.
            float available = distance + leaderSpeed * seconds + leaderSpeed * leaderSpeed / (2f * leaderBrakingLimit);
            float brakeStep = brakingLimit * seconds;
            float stoppingSpeedLimit = Mathf.Sqrt(brakeStep * brakeStep + 2f * brakingLimit * available) - brakeStep;
            limit = Mathf.Min(limit, Mathf.Max(0f, stoppingSpeedLimit));
        }
        // Never teleport velocity to a safe value when the gap is already unrecoverable.
        // The caller only reduces the requested speed, preserving its acceleration ceiling.
        return Mathf.Max(limit, Mathf.Max(0f, currentSpeed - brakingLimit * seconds));
    }

    internal static float OffLineSpeedFactor(float distanceFromLine, float turnWeight, bool outside = false)
    {
        float offset = Mathf.Max(0f,Mathf.Abs(distanceFromLine)-.3f);
        float general = .10f * Mathf.SmoothStep(0f,1f,offset/3.3f);
        // Continue increasing the loss into the outer lanes of a wide pack.
        float outer = outside ? .10f * Mathf.SmoothStep(0f,1f,offset/6.9f) : 0f;
        return 1f - (general+outer) * Mathf.Clamp01(turnWeight);
    }

    internal static float RestrictorPlateRacingLine(float inside) => inside * .90f;

    public float LaneOffset(int carIndex, float roadWidth, float distanceToPitMerge, float racingLine)
    {
        float usableHalfWidth = Mathf.Max(0f, roadWidth * 0.5f - 1.1f);
        float lane = usableHalfWidth * racingLine;
        return lane * Mathf.SmoothStep(0f, 1f, distanceToPitMerge / 15f);
    }
}
