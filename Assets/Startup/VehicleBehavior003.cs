using System;
using System.Collections.Generic;
using UnityEngine;

internal class VehicleBehavior003 : IVehicleBehaviorModel
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

    public virtual string Id => "vehicle-behavior-003";
    public virtual string Name => "VehicleBehavior003";

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

    protected virtual float StepTowardPreferredLane(int index, float lane, float target, float seconds) =>
        Mathf.MoveTowards(lane, target, MaximumLateralSpeed * seconds);

    protected virtual float PreferredLaneEdgeClearance(int index, float lane, float inside) => 1.05f;

    internal struct TrafficLaneControl
    {
        internal int PassBlocker;
        internal int PassSide;
        internal bool PassOnPreferredLine;
        internal float StalledPassSeconds;
        internal bool ReturningToLine;
        internal int FollowingCar;
        internal float PassingOpportunitySeconds;
        internal bool Easing;
        internal bool WaitingForRoom;
        internal bool HasLane;
        internal float LateralSpeed;
        internal float LastLane;
    }

    private const float PassReturnBuffer = .35f;
    private const float PassingSpeedAdvantage = .65f;
    private const float PassingOpportunityDuration = .20f;
    private const float StalledPassDuration = 1f;
    private const float PassProgressSpeed = .10f;
    private const float ReturnCompletionTolerance = .05f;
    private const float FollowReleaseMargin = .12f;
    private const float ParallelClearanceMargin = .06f;
    private const float TrafficLookAheadSeconds = .3f;
    private const float TrafficEaseDistance = .35f;
    private const float TrafficLongitudinalEaseDistance = 1f;
    private const float TrafficClearanceReserve = .04f;
    private const float TrafficResumeClearance = .12f;
    private const float TrafficLateralAcceleration = 10f;

    public void AvoidTraffic(float lapLength, float inside, float seconds, bool allowPassing,
        float[] distances, float[] widths, bool[] onTrack, float[] lanes, float[] targets,
        float[] currentSpeeds, float[] speeds, float brakingLimit, bool[] draftLine = null, bool respondToTraffic = true, float[] brakingFactors = null)
    {
        AvoidTraffic(null, null, lapLength, inside, seconds, allowPassing, distances, widths, onTrack, lanes,
            targets, currentSpeeds, speeds, brakingLimit, draftLine, respondToTraffic, brakingFactors);
    }

    internal void AvoidTraffic(TrafficLaneControl[] laneControl, bool[] stabilizeLane,
        float lapLength, float inside, float seconds, bool allowPassing,
        float[] distances, float[] widths, bool[] onTrack, float[] lanes, float[] targets,
        float[] currentSpeeds, float[] speeds, float brakingLimit, bool[] draftLine, bool respondToTraffic, float[] brakingFactors,
        float[] paceSpeeds = null, float[] startLanes = null, float[] startLaneSpeeds = null)
    {
        const float carLength = VehicleCollision001.ContactLength;
        const float carWidth = VehicleCollision001.ContactWidth;
        const float passLookAhead = 2f * carLength; // One car length between bumpers.
        // Aggression changes the driver's caution, never the physical collision footprint.
        // Retain neutral clearances when overtaking is prohibited (yellow flags).
        float Risk(int index) => allowPassing ? Aggression(index) : .5f;
        for (int i = 0; i < lanes.Length; i++)
        {
            bool stabilize = laneControl != null && stabilizeLane[i] && respondToTraffic && allowPassing &&
                draftLine?[i] != true && onTrack[i] && speeds[i] > 0f && seconds > 0f;
            var control = stabilize ? laneControl[i] : default;
            float lane = lanes[i];
            if (laneControl != null && !stabilize)
                laneControl[i] = default;
            if (control.HasLane && Mathf.Abs(lane - control.LastLane) > .001f)
                control = default;
            if (!onTrack[i] || speeds[i] <= 0f)
                continue;

            float aggression = Risk(i);
            float comfortMargin = Mathf.Lerp(.4f, .02f, aggression);
            float carSpacing = carLength + Mathf.Lerp(.9f, .08f, aggression);
            float rearClearance = carLength + Mathf.Lerp(3f * TrackDefinition.MetersPerFoot, .08f, aggression);
            float anticipation = Mathf.Lerp(1f, .2f, aggression);
            float target = targets[i];
            if (!respondToTraffic)
            {
                lanes[i] = StepTowardPreferredLane(i, lanes[i], target, seconds);
                continue;
            }

            float passOffset = carWidth + comfortMargin + Mathf.Lerp(.3f, .02f, aggression);
            if (control.ReturningToLine && Mathf.Abs(target - lane) <= ReturnCompletionTolerance)
                control.ReturningToLine = false;
            bool holdingPass = false;
            if (control.PassSide != 0)
            {
                int previousBlocker = control.PassBlocker;
                float gap = TrackGap(previousBlocker);
                bool finished = !onTrack[previousBlocker] || gap < -rearClearance - PassReturnBuffer ||
                    gap > passLookAhead + carSpacing;
                if (!finished)
                {
                    target = PassLane(previousBlocker, control.PassSide, control.PassOnPreferredLine);
                    float blockerLane = OtherLane(previousBlocker);
                    float clearance = ComfortClearance(previousBlocker, gap, blockerLane - lane);
                    bool advancing = currentSpeeds[i] - currentSpeeds[previousBlocker] >= PassProgressSpeed;
                    if (gap > carLength && !advancing &&
                        ((target - blockerLane) * inside * control.PassSide < clearance ||
                         PaceAdvantage(previousBlocker) < PassingSpeedAdvantage * .5f ||
                         !LaneClear(target, passLookAhead + carSpacing, comfort: true)))
                        control.StalledPassSeconds += seconds;
                    else
                        control.StalledPassSeconds = 0f;
                }

                if (finished || control.StalledPassSeconds >= StalledPassDuration)
                {
                    control.PassSide = 0;
                    control.StalledPassSeconds = 0f;
                    control.ReturningToLine = true;
                    control.Easing = true;
                    target = targets[i];
                }
                else
                    holdingPass = true;
            }

            float nearest = float.PositiveInfinity;
            int leader = -1;
            if (stabilize && !holdingPass && !control.ReturningToLine)
            {
                for (int j = 0; j < lanes.Length; j++)
                {
                    if (i == j || !onTrack[j])
                        continue;
                    float gap = Mathf.Repeat(distances[j] - distances[i], lapLength);
                    float releaseMargin = control.FollowingCar == j + 1 ? FollowReleaseMargin : 0f;
                    float side = OtherLane(j) - lane;
                    if (gap > 0f && gap < nearest && gap <= passLookAhead + releaseMargin &&
                        Mathf.Abs(side) < ComfortClearance(j, gap, side) + releaseMargin)
                    {
                        nearest = gap;
                        leader = j;
                    }
                }
            }

            bool followingLeader = leader >= 0;
            if (followingLeader)
            {
                if (control.FollowingCar != leader + 1)
                    control.PassingOpportunitySeconds = 0f;
                control.FollowingCar = leader + 1;
                float currentAdvantage = currentSpeeds[i] - currentSpeeds[leader];
                if (Mathf.Max(currentAdvantage, PaceAdvantage(leader)) >= PassingSpeedAdvantage)
                    control.PassingOpportunitySeconds += seconds;
                else
                    control.PassingOpportunitySeconds = 0f;

                if (control.PassingOpportunitySeconds >= PassingOpportunityDuration)
                {
                    float leaderLane = OtherLane(leader);
                    float side = leaderLane - lane;
                    float ahead = nearest + carSpacing;
                    float clearance = ComfortClearance(leader, nearest, side);
                    int preferredSide = (target - leaderLane) * inside > 0f ? 1 : -1;
                    if (Mathf.Abs(target - leaderLane) >= clearance && PassCorridorClear(preferredSide, usePreferredLine: true))
                    {
                        CommitPass(preferredSide, usePreferredLine: true);
                    }
                    else if (PassCorridorClear(1))
                        CommitPass(1);
                    else if (PassCorridorClear(-1))
                        CommitPass(-1);

                    bool PassCorridorClear(int passSide, bool usePreferredLine = false)
                    {
                        float destination = PassLane(leader, passSide, usePreferredLine);
                        return (destination - leaderLane) * inside * passSide >= clearance &&
                            LaneClear(destination, ahead, comfort: true);
                    }
                }

                if (followingLeader)
                {
                    target = lane;
                    control.Easing = true;
                }
            }
            else
            {
                control.FollowingCar = 0;
                control.PassingOpportunitySeconds = 0f;
            }

            bool followingPreferred = target == targets[i] && !holdingPass && !followingLeader;
            float next = followingPreferred && !control.ReturningToLine
                ? StepTowardPreferredLane(i, lane, target, seconds)
                : Mathf.MoveTowards(lane, target, MaximumLateralSpeed * seconds);
            bool nearTraffic = false;
            if (stabilize && next != lane)
            {
                float direction = Mathf.Sign(next - lane);
                float room = TrafficRoom(direction);
                nearTraffic = room <= TrafficClearanceReserve + TrafficEaseDistance;
                if (room <= TrafficClearanceReserve)
                    control.WaitingForRoom = true;
                else if (room >= TrafficResumeClearance)
                    control.WaitingForRoom = false;
                if (control.ReturningToLine || nearTraffic || control.Easing)
                {
                    float desiredSpeed = Mathf.Abs(next - lane) / seconds;
                    float safeRoom = control.WaitingForRoom ? 0f : Mathf.Max(0f, room - TrafficClearanceReserve);
                    float roomSpeed = desiredSpeed * Mathf.SmoothStep(0f, 1f, safeRoom / TrafficEaseDistance);
                    float rampSpeed = Mathf.Max(0f, direction * control.LateralSpeed) + TrafficLateralAcceleration * seconds;
                    float movement = Mathf.Min(safeRoom, Mathf.Min(roomSpeed, rampSpeed) * seconds);
                    control.Easing = movement < Mathf.Abs(next - lane) - .00001f;
                    next = lane + direction * movement;
                }
            }

            // The existing swept check remains authoritative for the final movement.
            bool safe = LaneClear(next, carSpacing, followingPreferred);
            if (safe)
                lanes[i] = next;

            if (stabilize)
            {
                control.Easing |= !safe && nearTraffic;
                control.LateralSpeed = (lanes[i] - lane) / seconds;
                control.LastLane = lanes[i];
                control.HasLane = true;
                laneControl[i] = control;
            }

            void CommitPass(int side, bool usePreferredLine = false)
            {
                control.PassBlocker = leader;
                control.PassSide = side;
                control.PassOnPreferredLine = usePreferredLine;
                control.StalledPassSeconds = 0f;
                control.Easing = true;
                control.FollowingCar = 0;
                control.PassingOpportunitySeconds = 0f;
                followingLeader = false;
                holdingPass = true;
                target = PassLane(leader, side, usePreferredLine);
            }

            float PassLane(int other, int side, bool usePreferredLine)
            {
                float direction = inside * side;
                float offset = OtherLane(other) + direction * passOffset;
                float preferred = targets[i];
                if (usePreferredLine && (preferred - offset) * direction > 0f)
                    offset = preferred;
                float edge = Mathf.Max(0f, widths[i] * .5f - 1.05f);
                return Mathf.Clamp(offset, -edge, edge);
            }

            float OtherLane(int other) => startLanes == null ? lanes[other] : startLanes[other];
            float PaceAdvantage(int other) => paceSpeeds == null ? speeds[i] - speeds[other] : paceSpeeds[i] - paceSpeeds[other];
            float TrackGap(int other) => Mathf.Repeat(distances[other] - distances[i] + lapLength * .5f, lapLength) -
                lapLength * .5f;

            float ComfortClearance(int other, float gap, float side) => LateralComfortClearance(
                comfortMargin, gap, side, currentSpeeds[i], currentSpeeds[other],
                startLaneSpeeds?[i] ?? control.LateralSpeed, startLaneSpeeds?[other] ?? 0f);

            float TrafficRoom(float direction)
            {
                float room = float.PositiveInfinity;
                for (int j = 0; j < lanes.Length; j++)
                {
                    if (j == i || !onTrack[j])
                        continue;
                    float separation = OtherLane(j) - lane;
                    float side = separation * direction;
                    if (side <= 0f)
                        continue;
                    float gap = TrackGap(j);
                    float future = gap + (currentSpeeds[j] - currentSpeeds[i]) * TrafficLookAheadSeconds;
                    float longitudinalRoom = Mathf.Max(Mathf.Min(gap, future) - carSpacing,
                        -rearClearance - Mathf.Max(gap, future));
                    if (longitudinalRoom >= TrafficLongitudinalEaseDistance)
                        continue;
                    float lateralRoom = side - ComfortClearance(j, gap, separation);
                    if (longitudinalRoom > 0f)
                        lateralRoom = Mathf.Max(lateralRoom, Mathf.Lerp(0f, TrafficEaseDistance + TrafficClearanceReserve,
                            longitudinalRoom / TrafficLongitudinalEaseDistance));
                    room = Mathf.Min(room, lateralRoom);
                }
                return room;
            }

            bool LaneClear(float candidate, float ahead, bool followingPreferred = false, bool comfort = false,
                float lateralBuffer = 0f)
            {
                float clearance = followingPreferred ? PreferredLaneEdgeClearance(i, candidate, inside) : 1.05f;
                if (Mathf.Abs(candidate) > Mathf.Max(0f, widths[i] * 0.5f - clearance))
                    return false;
                for (int j = 0; j < lanes.Length; j++)
                {
                    if (j == i || !onTrack[j])
                        continue;
                    float gap = TrackGap(j);
                    float horizon = Mathf.Max(seconds, Mathf.Min(anticipation, Mathf.Abs(candidate-lanes[i])/3f * anticipation));
                    float futureGap = gap + (currentSpeeds[j]-currentSpeeds[i])*horizon;
                    // Check the swept longitudinal interval and the whole lane-change corridor.
                    if (Mathf.Min(gap,futureGap) > ahead || Mathf.Max(gap,futureGap) < -rearClearance)
                        continue;
                    float before = OtherLane(j) - lane;
                    float after = (comfort ? OtherLane(j) : lanes[j]) - candidate;
                    bool sameSide = before * after >= 0f;
                    float separation = sameSide ? Mathf.Min(Mathf.Abs(before), Mathf.Abs(after)) : 0f;
                    bool escaping = sameSide && Mathf.Abs(after) > Mathf.Abs(before);
                    float required = comfort ? ComfortClearance(j, gap, before) : carWidth + (stabilize ? 0f : comfortMargin);
                    if (separation < required + lateralBuffer && !escaping)
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
                float comfortMargin = Mathf.Lerp(.4f, .02f, aggression);
                float carSpacing = carLength + Mathf.Lerp(.9f, .08f, aggression);
                for (int j = 0; j < lanes.Length; j++)
                {
                    if (j == i || !onTrack[j])
                        continue;
                    float gap = Mathf.Repeat(distances[j] - distances[i], lapLength);
                    if (gap == 0f && j > i)
                        continue;
                    float side = lanes[j] - lanes[i];
                    float lateralClearance = allowPassing && stabilizeLane?[i] == true
                        ? LateralComfortClearance(comfortMargin, gap, side, currentSpeeds[i], currentSpeeds[j],
                            laneControl[i].LateralSpeed, laneControl[j].LateralSpeed)
                        : carWidth + comfortMargin;
                    if (Mathf.Abs(side) >= lateralClearance)
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

    private static float LateralComfortClearance(float comfortMargin, float gap, float side, float speed,
        float otherSpeed, float lateralSpeed, float otherLateralSpeed)
    {
        float longitudinalClosing = Mathf.Max(0f, (speed - otherSpeed) * Mathf.Sign(gap));
        float lateralClosing = Mathf.Max(0f, (lateralSpeed - otherLateralSpeed) * Mathf.Sign(side));
        float convergence = Mathf.Max(longitudinalClosing / 3f, lateralClosing / 1.25f);
        return VehicleCollision001.ContactWidth + Mathf.Lerp(Mathf.Min(comfortMargin, ParallelClearanceMargin),
            comfortMargin, convergence);
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

    internal static float OffLineSpeedFactor(float distanceFromLine, float turnWeight)
    {
        const float distanceForTenPercentPenalty = 12f * TrackDefinition.MetersPerFoot;
        float penalty = .10f * (Mathf.Abs(distanceFromLine) / distanceForTenPercentPenalty) * Mathf.Clamp01(turnWeight);
        return Mathf.Clamp01(1f - penalty);
    }

    internal static float RestrictorPlateRacingLine(float inside) => inside * .90f;

    public float LaneOffset(int carIndex, float roadWidth, float distanceToPitMerge, float racingLine)
    {
        float usableHalfWidth = Mathf.Max(0f, roadWidth * 0.5f - 1.1f);
        float lane = usableHalfWidth * racingLine;
        return lane * Mathf.SmoothStep(0f, 1f, distanceToPitMerge / 15f);
    }
}
