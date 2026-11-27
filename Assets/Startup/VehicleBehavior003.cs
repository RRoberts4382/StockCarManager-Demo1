using System;
using System.Collections.Generic;
using UnityEngine;

internal class VehicleBehavior003 : IVehicleBehaviorModel
{
    // Bound to the active context before each update: registry models are shared.
    internal IReadOnlyList<SessionCarResult> Drivers { get; set; }
    internal float RaceProgressAggression { get; set; }
    internal delegate float PassForecast(int car, int blocker, float lane, float gap);
    internal (Vector2[] PassingLaneRanges, bool[] CornerApproaches, PassForecast ForecastPass) OvalPassingContext { get; set; }
    internal Action<int, int, int> OvalPassCommitted { get; set; }
    internal Action<int, TrafficLaneDecision> SnapshotLaneDecision { get; set; }
    internal bool UsePreferredLineTraffic { get; set; }
    internal float OvalPassingEase { get; set; }
    internal float[] TrackContactHalfWidths { get; set; }
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
    private const float LaneChangeCruiseSpeed = 2f;
    private const float LaneChangeAcceleration = 3.5f;
    private const float LaneChangeReversalBraking = 6f;

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

    internal static float LaneChangeStep(float error, float lateralSpeed, float seconds)
    {
        float remaining = Mathf.Abs(error);
        if (remaining <= .001f) return error;

        // Include this step's travel in the stopping distance so a car can
        // lose lateral speed before it reaches the destination.
        float direction = Mathf.Sign(error);
        float speedStep = LaneChangeAcceleration * seconds;
        float stoppingSpeed = Mathf.Sqrt(speedStep * speedStep + 2f * LaneChangeAcceleration * remaining) - speedStep;
        float targetSpeed = direction * Mathf.Min(LaneChangeCruiseSpeed, stoppingSpeed);
        if (lateralSpeed * direction < 0f)
            lateralSpeed = Mathf.MoveTowards(lateralSpeed, 0f, LaneChangeReversalBraking * seconds);
        else
            lateralSpeed = Mathf.MoveTowards(lateralSpeed, targetSpeed, speedStep);
        float movement = lateralSpeed * seconds;
        return movement * direction > remaining ? error : movement;
    }

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
        internal float LaneChangeSpeed;
        internal float LastLane;
    }

    [Serializable]
    internal struct TrafficLaneDecision
    {
        public bool recorded;
        public float startLaneMeters;
        public float baseTargetLaneMeters;
        public float steeringTargetLaneMeters;
        public float preferredPathAdvanceMeters;
        public float steeringCorrectionMeters;
        public float requestedLaneMeters;
        public float clearanceLimitedLaneMeters;
        public float finalLaneMeters;
        public bool trafficRoomMeasured;
        public float trafficRoomMeters;
        public float openingRoomMeters;
        public int trafficRoomNeighborIndex;
        public bool trafficRoomLimited;
        public bool waitingForRoom;
        public bool sweptClearanceChecked;
        public bool sweptClearanceAccepted;
        public bool sweptRoadEdgeRejected;
        public int sweptBlockerIndex;
        public int leaderIndex;
        public int passBlockerIndex;
        public int passSide;
        public bool returningToLine;
    }

    private const float PassReturnBuffer = .35f;
    private const float PassingSpeedAdvantage = .65f;
    private const float PassingOpportunityDuration = .20f;
    private const float StrictOvalPassingSpeedAdvantage = .65f;
    private const float DefaultOvalPassingSpeedAdvantage = .30f;
    private const float EasierOvalPassingSpeedAdvantage = .15f;
    private const float DefaultOvalPassingOpportunityDuration = .12f;
    private const float EasierOvalPassingOpportunityDuration = .08f;
    // Keep the exact duration used by the previous Strict setting.
    private static readonly float StrictOvalPassingOpportunityDuration = Mathf.Lerp(.5f, .20f, 1f);
    private const float StalledPassDuration = 1f;
    private const float InsidePassLookAheadSeconds = .75f;
    protected const float PassEdgeClearance = 1.05f;
    private const float PassProgressSpeed = .10f;
    private const float ReturnCompletionTolerance = .05f;
    private const float FollowReleaseMargin = .12f;
    private const float ParallelClearanceMargin = .06f;
    private const float TrafficLookAheadSeconds = .3f;
    private const float TrafficEaseDistance = .35f;
    private const float TrafficLongitudinalEaseDistance = 1f;
    private const float TrafficClearanceReserve = .04f;
    private const float TrafficResumeClearance = .12f;
    internal const float TrafficLateralAcceleration = 10f;
    private const float CloseParallelClearanceMargin = .04f;
    private const float CloseTrafficEaseDistance = .12f;
    private const float CloseTrafficClearanceReserve = .02f;
    private const float CloseTrafficResumeClearance = .06f;
    private const float OutwardLateralAcceleration = 15f;
    private const float DraftFollowMaximumSpeedDifference = 3f;
    private const float DraftFollowBumperClearance = .35f;
    private const float PlateDraftBumperClearance = .12f;
    private const float DraftGapResponse = 1.8f;
    private const float DraftPassClosingSpeed = 2.7f;
    internal const float DraftPassCorridorReserve = .04f;
    private const float DraftPassLiftDeceleration = RaceSessionRun.CoastingDeceleration * .5f;
    private const float DraftLateralSpeed = .075f;
    private const float LaneVariationContactDepth = .02f;

    protected static float OvalPassingThreshold(float strict, float midpoint, float easier, float ease)
    {
        // The tested midpoint is ease 1. Each half of the slider interpolates within its own range.
        float threshold = Mathf.Lerp(strict, midpoint, ease);
        return Mathf.Lerp(threshold, easier, ease - 1f);
    }

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
        float[] paceSpeeds = null, float[] startLanes = null, float[] startLaneSpeeds = null,
        int[] draftLeaders = null, float[] draftBumpIntent = null, bool plate = false, TrackDraft laneDraft = null,
        bool[] draftPassLane = null, float[] draftPassGap = null, System.Func<float, Vector2> roadWidthsAt = null)
    {
        const float carLength = VehicleCollision001.ContactLength;
        const float carWidth = VehicleCollision001.ContactWidth;
        const float passLookAhead = 2f * carLength; // One car length between bumpers.
        // Aggression changes the driver's caution, never the physical collision footprint.
        // Keep neutral spacing for 007 so merely looking for a pass does not change traffic following.
        bool legacyPassing = allowPassing && !UsePreferredLineTraffic;
        float Risk(int index) => legacyPassing ? Aggression(index) : .5f;
        float ContactWidthBetween(int first, int second) => VehicleCollision001.TrackContactWidth(TrackContactHalfWidths, first, second);
        bool keepLaneControl = allowPassing || UsePreferredLineTraffic;
        var (passingLaneRanges, cornerApproaches, forecastPass) = OvalPassingContext;
        bool canAttemptPass = allowPassing && (!UsePreferredLineTraffic || forecastPass != null && passingLaneRanges != null);
        float passingAdvantage = UsePreferredLineTraffic
            ? OvalPassingThreshold(StrictOvalPassingSpeedAdvantage, DefaultOvalPassingSpeedAdvantage,
                EasierOvalPassingSpeedAdvantage, OvalPassingEase)
            : PassingSpeedAdvantage;
        float passingDuration = UsePreferredLineTraffic
            ? OvalPassingThreshold(StrictOvalPassingOpportunityDuration, DefaultOvalPassingOpportunityDuration,
                EasierOvalPassingOpportunityDuration, OvalPassingEase)
            : PassingOpportunityDuration;
        float[] preferredAdvances = (this as VehicleBehavior004)?.PreferredLineAdvance;
        float[] fittedAdvances = UsePreferredLineTraffic ? preferredAdvances : null;
        var snapshotLaneDecision = SnapshotLaneDecision;
        bool coordinateArc = respondToTraffic && startLanes != null && fittedAdvances != null;
        Span<int> laneOrder = coordinateArc ? stackalloc int[lanes.Length] : Span<int>.Empty;
        if (coordinateArc)
            OrderFittedMoves(startLanes, fittedAdvances, inside, laneOrder);

        for (int slot = 0; slot < lanes.Length; slot++)
        {
            int i = coordinateArc ? laneOrder[slot] : slot;
            bool directedDraftPass = plate && allowPassing && draftPassLane?[i] == true && stabilizeLane?[i] != false;
            bool activeLane = laneControl != null && stabilizeLane[i] && onTrack[i] && speeds[i] > 0f && seconds > 0f;
            bool smoothChange = activeLane && !plate;
            bool stabilize = activeLane && respondToTraffic && keepLaneControl && (draftLine?[i] != true || directedDraftPass);
            float lane = lanes[i];
            var previousControl = laneControl?[i] ?? default;
            if (!previousControl.HasLane || Mathf.Abs(lane - previousControl.LastLane) > .001f)
                previousControl = default;
            var control = stabilize ? previousControl : default;
            float previousLaneChangeSpeed = previousControl.LaneChangeSpeed;
            if (laneControl != null && !stabilize)
                laneControl[i] = default;
            if (!onTrack[i] || speeds[i] <= 0f)
            {
                if (laneDraft != null)
                    laneDraft.AppliedLaneOffsets[i] = 0f;
                continue;
            }

            float aggression = Risk(i);
            float comfortMargin = Mathf.Lerp(.4f, .02f, aggression);
            float carSpacing = carLength + Mathf.Lerp(.9f, .08f, aggression);
            float rearClearance = carLength + Mathf.Lerp(3f * TrackDefinition.MetersPerFoot, .08f, aggression);
            float laneClearanceAhead = directedDraftPass ? carLength + DraftPassCorridorReserve : carSpacing;
            float laneClearanceBehind = directedDraftPass ? carLength + DraftPassCorridorReserve : rearClearance;
            float anticipation = Mathf.Lerp(1f, .2f, aggression);
            float target = targets[i];
            bool canVaryLane = laneDraft != null && stabilizeLane?[i] == true;
            if (!respondToTraffic)
            {
                float heldOffset = canVaryLane ? laneDraft.AppliedLaneOffsets[i] : 0f;
                float change = smoothChange ? LaneChangeStep(target + heldOffset - lane, previousLaneChangeSpeed, seconds) : 0f;
                float requested = smoothChange ? lane + change + (preferredAdvances?[i] ?? 0f)
                    : StepTowardPreferredLane(i, lane, target + heldOffset, seconds);
                lanes[i] = requested;
                if (canVaryLane)
                    ApplyLaneVariation(i, lapLength, seconds, distances, widths, onTrack, lanes, currentSpeeds, laneDraft, startLanes);
                else if (laneDraft != null)
                    laneDraft.AppliedLaneOffsets[i] = 0f;
                if (smoothChange)
                    SaveLaneMotion(change);
                if (snapshotLaneDecision != null)
                {
                    float advance = preferredAdvances?[i] ?? 0f;
                    snapshotLaneDecision(i, new TrafficLaneDecision
                    {
                        recorded = true,
                        startLaneMeters = lane,
                        baseTargetLaneMeters = targets[i],
                        steeringTargetLaneMeters = target + heldOffset,
                        preferredPathAdvanceMeters = advance,
                        steeringCorrectionMeters = requested - lane - advance,
                        requestedLaneMeters = requested,
                        clearanceLimitedLaneMeters = requested,
                        finalLaneMeters = lanes[i],
                        trafficRoomNeighborIndex = -1,
                        sweptBlockerIndex = -1,
                        leaderIndex = -1,
                        passBlockerIndex = -1,
                    });
                }
                continue;
            }

            float passSideMargin = Mathf.Lerp(.3f, .02f, aggression);
            bool alongsideInCorner = stabilize && cornerApproaches?[i] == true &&
                Mathf.Abs(lane) <= (widths[i] - carWidth) * .5f && HasAdjacentCar();
            if (directedDraftPass || !canAttemptPass)
            {
                control.PassSide = 0;
                control.ReturningToLine = false;
                control.StalledPassSeconds = 0f;
            }
            if (control.ReturningToLine && Mathf.Abs(target - lane) <= ReturnCompletionTolerance)
                control.ReturningToLine = false;
            bool holdingPass = false;
            if (control.PassSide != 0)
            {
                int previousBlocker = control.PassBlocker;
                float gap = TrackGap(previousBlocker);
                bool finished = !onTrack[previousBlocker] || gap < -rearClearance - PassReturnBuffer ||
                    gap > passLookAhead + carSpacing;
                bool noRoomAhead = false;
                if (!finished)
                {
                    target = PassLane(previousBlocker, control.PassSide, control.PassOnPreferredLine);
                    // Give up an unsuitable line while there's still time to tuck in behind the blocker.
                    noRoomAhead = gap > carLength && !alongsideInCorner && !PassFitsAhead(target);
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

                if (finished || noRoomAhead || control.StalledPassSeconds >= StalledPassDuration)
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
            if (canAttemptPass && stabilize && !directedDraftPass && !holdingPass && !control.ReturningToLine && !alongsideInCorner)
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

            bool followingLeader = leader >= 0 && !UsePreferredLineTraffic;
            if (leader >= 0)
            {
                if (control.FollowingCar != leader + 1)
                    control.PassingOpportunitySeconds = 0f;
                control.FollowingCar = leader + 1;
                float paceAdvantage = PaceAdvantage(leader);
                if (!UsePreferredLineTraffic)
                    paceAdvantage = Mathf.Max(paceAdvantage, currentSpeeds[i] - currentSpeeds[leader]);
                if (paceAdvantage >= passingAdvantage)
                    control.PassingOpportunitySeconds += seconds;
                else
                    control.PassingOpportunitySeconds = 0f;

                if (control.PassingOpportunitySeconds >= passingDuration)
                {
                    float leaderLane = OtherLane(leader);
                    float side = leaderLane - lane;
                    float ahead = nearest + carSpacing;
                    float clearance = ComfortClearance(leader, nearest, side);
                    int preferredSide = (target - leaderLane) * inside > 0f ? 1 : -1;
                    if (passingLaneRanges != null)
                    {
                        float insideLane = PassLane(leader, 1, false);
                        float outsideLane = PassLane(leader, -1, false);
                        float insideGain = ScorePassingLane(1, insideLane);
                        float outsideGain = ScorePassingLane(-1, outsideLane);
                        if (insideGain > 0f && (insideGain > outsideGain || insideGain == outsideGain &&
                            Mathf.Abs(insideLane - lane) <= Mathf.Abs(outsideLane - lane)))
                            CommitPass(1);
                        else if (outsideGain > 0f)
                            CommitPass(-1);
                        // The speed advantage must build again before running another forecast.
                        if (forecastPass != null)
                            control.PassingOpportunitySeconds = 0f;
                    }
                    else if (Mathf.Abs(target - leaderLane) >= clearance &&
                        PassCorridorClear(preferredSide, PassLane(leader, preferredSide, true)))
                    {
                        CommitPass(preferredSide, usePreferredLine: true);
                    }
                    else if (PassCorridorClear(1, PassLane(leader, 1, false)))
                        CommitPass(1);
                    else if (PassCorridorClear(-1, PassLane(leader, -1, false)))
                        CommitPass(-1);

                    float ScorePassingLane(int passSide, float destination) => forecastPass != null && PassCorridorClear(passSide, destination)
                        ? forecastPass(i, leader, destination, nearest) : float.NegativeInfinity;

                    bool PassCorridorClear(int passSide, float destination) => PassFitsAhead(destination) &&
                        (destination - leaderLane) * inside * passSide >= clearance && LaneClear(destination, ahead, comfort: true);
                }

                if (followingLeader)
                {
                    target = lane;
                    if (passingLaneRanges != null && !PassFitsAhead(lane))
                    {
                        // Use the gap behind the leader to move toward the line without leaving its following lane.
                        float towardLeader = OtherLane(leader) - lane;
                        float towardLine = targets[i] - lane;
                        if (towardLeader * towardLine > 0f)
                            target += Mathf.Sign(towardLine) * Mathf.Min(Mathf.Abs(towardLine), Mathf.Abs(towardLeader));
                    }
                    control.Easing = true;
                }
            }
            else
            {
                control.FollowingCar = 0;
                control.PassingOpportunitySeconds = 0f;
            }

            bool holdLaneAlongside = alongsideInCorner && !UsePreferredLineTraffic;
            if (holdLaneAlongside)
            {
                // Keep the occupied side clear, while letting a car use open space toward its line.
                float directionToLine = Mathf.Sign(targets[i] - lane);
                target = holdingPass || HasAdjacentCar(directionToLine) ? lane : targets[i];
                control.Easing = true;
            }

            bool followingPreferred = !directedDraftPass && target == targets[i] && !holdingPass && !followingLeader && !holdLaneAlongside;
            float insideLimit = Mathf.Max(0f, widths[i] * .5f - PreferredLaneEdgeClearance(i, lane, inside));
            bool recoveringInsideEdge = stabilize && lane * inside > insideLimit;
            float fittedAdvance = fittedAdvances?[i] ?? 0f;
            bool outwardArc = followingPreferred && fittedAdvance * inside < 0f;
            bool allowVariation = canVaryLane && !directedDraftPass && !holdingPass &&
                !control.ReturningToLine && !recoveringInsideEdge && !holdLaneAlongside;
            float appliedOffset = allowVariation ? laneDraft.AppliedLaneOffsets[i] : 0f;
            // Follow the existing offset on the preferred path. When holding the
            // current lane in traffic, that lane already includes the offset.
            float drivingTarget = followingLeader || draftLine?[i] == true ? target : target + appliedOffset;
            if (recoveringInsideEdge)
                drivingTarget = Mathf.Min(drivingTarget * inside, insideLimit) * inside;
            bool advanceWithLine = followingPreferred && (!control.ReturningToLine || UsePreferredLineTraffic) && !recoveringInsideEdge;
            // Apply the fitted line's normal movement separately. Only an
            // actual correction across that line should accelerate or slow down.
            bool smoothCorrection = smoothChange && !recoveringInsideEdge && !holdLaneAlongside && !followingLeader;
            float correction = smoothCorrection ? LaneChangeStep(drivingTarget - lane, previousLaneChangeSpeed, seconds) : 0f;
            float next = smoothCorrection ? lane + correction + (advanceWithLine ? preferredAdvances?[i] ?? 0f : 0f)
                : advanceWithLine ? StepTowardPreferredLane(i, lane, drivingTarget, seconds)
                : Mathf.MoveTowards(lane, drivingTarget, MaximumLateralSpeed * seconds);
            if (recoveringInsideEdge && outwardArc)
                next += fittedAdvance;
            float requestedLane = next;
            float requestedStep = next - lane;
            bool nearTraffic = false;
            int trafficRoomNeighbor = -1, sweptBlocker = -1;
            float roomForSnapshot = 0f, openingForSnapshot = 0f;
            bool sweptRoadEdgeRejected = false;
            if (stabilize && next != lane)
            {
                float direction = Mathf.Sign(next - lane);
                bool movingWithArc = followingPreferred && direction * fittedAdvance > 0f;
                bool followOpeningGap = coordinateArc && movingWithArc;
                bool closeSpacing = UsePreferredLineTraffic && followingPreferred && (!recoveringInsideEdge || outwardArc);
                float reserve = closeSpacing ? CloseTrafficClearanceReserve : TrafficClearanceReserve;
                float resumeClearance = closeSpacing ? CloseTrafficResumeClearance : TrafficResumeClearance;
                float easeDistance = closeSpacing ? CloseTrafficEaseDistance : TrafficEaseDistance;
                float parallelMargin = closeSpacing ? CloseParallelClearanceMargin : ParallelClearanceMargin;
                float easingRoom = reserve + easeDistance;
                float room = TrafficRoom(direction, easingRoom, parallelMargin, followOpeningGap, out float opening,
                    out trafficRoomNeighbor);
                if (snapshotLaneDecision != null && trafficRoomNeighbor >= 0)
                {
                    roomForSnapshot = room;
                    openingForSnapshot = opening;
                }
                float requestedMovement = Mathf.Abs(next - lane);
                nearTraffic = room <= easingRoom || closeSpacing && room <= reserve + requestedMovement;
                if (room <= reserve)
                    control.WaitingForRoom = true;
                else if (room >= resumeClearance || opening > 0f)
                    control.WaitingForRoom = false;
                if (directedDraftPass || control.ReturningToLine || nearTraffic || control.Easing)
                {
                    float desiredSpeed = requestedMovement / seconds;
                    float safeRoom = control.WaitingForRoom ? 0f : Mathf.Max(0f, room - reserve);
                    float roomSpeed = desiredSpeed * Mathf.SmoothStep(0f, 1f, safeRoom / easeDistance);
                    float lateralAcceleration = closeSpacing && direction * inside < 0f
                        ? OutwardLateralAcceleration : TrafficLateralAcceleration;
                    float rampSpeed = Mathf.Max(0f, direction * control.LateralSpeed) + lateralAcceleration * seconds;
                    if (closeSpacing && movingWithArc && (outwardArc || opening > 0f))
                    {
                        // Follow the neighbor's opening immediately and ramp any extra catch-up.
                        float arcStep = Mathf.Min(direction * fittedAdvance, requestedMovement);
                        if (opening > 0f)
                            arcStep = Mathf.Min(arcStep, opening);
                        if (arcStep <= safeRoom || opening > 0f)
                        {
                            float arcSpeed = Mathf.Min(arcStep, safeRoom) / seconds;
                            roomSpeed = Mathf.Max(roomSpeed, arcSpeed);
                            rampSpeed = Mathf.Max(rampSpeed, arcSpeed);
                        }
                    }
                    float movement = Mathf.Min(safeRoom, Mathf.Min(roomSpeed, rampSpeed) * seconds);
                    if (opening > 0f)
                    {
                        float catchUpRoom = Mathf.Max(0f, safeRoom - opening);
                        // Retain the existing clearance smoothly as the next gap starts to open.
                        float openingWeight = Mathf.SmoothStep(0f, 1f, opening / easeDistance);
                        float catchUp = Mathf.Lerp(1f, Mathf.Clamp01(seconds / TrafficLookAheadSeconds), openingWeight);
                        movement = Mathf.Min(movement, opening + catchUpRoom * catchUp);
                    }
                    control.Easing = movement < requestedMovement - .00001f;
                    next = lane + direction * movement;
                }
            }

            // The existing swept check remains authoritative for the final movement.
            bool safe = LaneClear(next, laneClearanceAhead, followingPreferred, recoveringInsideEdge: recoveringInsideEdge,
                recordSweptRejection: snapshotLaneDecision != null);
            if (safe)
                lanes[i] = next;

            if (allowVariation)
                ApplyLaneVariation(i, lapLength, seconds, distances, widths, onTrack, lanes, currentSpeeds, laneDraft, startLanes);
            else if (laneDraft != null)
                laneDraft.AppliedLaneOffsets[i] = 0f;

            if (stabilize || smoothChange)
            {
                control.Easing |= !safe && nearTraffic;
                float usedMotion = !safe ? 0f : Mathf.Abs(requestedStep) <= .00001f ? 1f
                    : Mathf.Clamp01((next - lane) / requestedStep);
                SaveLaneMotion(correction * usedMotion);
            }

            if (snapshotLaneDecision != null)
            {
                float advance = advanceWithLine ? preferredAdvances?[i] ?? 0f : recoveringInsideEdge && outwardArc ? fittedAdvance : 0f;
                snapshotLaneDecision(i, new TrafficLaneDecision
                {
                    recorded = true,
                    startLaneMeters = lane,
                    baseTargetLaneMeters = targets[i],
                    steeringTargetLaneMeters = drivingTarget,
                    preferredPathAdvanceMeters = advance,
                    steeringCorrectionMeters = requestedStep - advance,
                    requestedLaneMeters = requestedLane,
                    clearanceLimitedLaneMeters = next,
                    finalLaneMeters = lanes[i],
                    trafficRoomMeasured = trafficRoomNeighbor >= 0,
                    trafficRoomMeters = roomForSnapshot,
                    openingRoomMeters = openingForSnapshot,
                    trafficRoomNeighborIndex = trafficRoomNeighbor,
                    trafficRoomLimited = Mathf.Abs(next - requestedLane) > .00001f,
                    waitingForRoom = control.WaitingForRoom,
                    sweptClearanceChecked = true,
                    sweptClearanceAccepted = safe,
                    sweptRoadEdgeRejected = sweptRoadEdgeRejected,
                    sweptBlockerIndex = sweptBlocker,
                    leaderIndex = leader,
                    passBlockerIndex = control.PassSide == 0 ? -1 : control.PassBlocker,
                    passSide = control.PassSide,
                    returningToLine = control.ReturningToLine,
                });
            }

            void SaveLaneMotion(float correctionStep)
            {
                control.LateralSpeed = (lanes[i] - lane) / seconds;
                control.LaneChangeSpeed = correctionStep / seconds;
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
                if (UsePreferredLineTraffic)
                    OvalPassCommitted?.Invoke(i, leader, side);
            }

            float PassLane(int other, int side, bool usePreferredLine)
            {
                float direction = inside * side;
                float contactWidth = UsePreferredLineTraffic ? ContactWidthBetween(i, other) : carWidth;
                float offset = OtherLane(other) + direction * (contactWidth + comfortMargin + passSideMargin);
                float preferred = targets[i];
                if (usePreferredLine && (preferred - offset) * direction > 0f)
                    offset = preferred;
                float edge = Mathf.Max(0f, widths[i] * .5f - PassEdgeClearance);
                float insideEdge = edge;
                if (side > 0 && roadWidthsAt != null)
                {
                    // Leave room to hold the inside lane through an upcoming taper.
                    float ahead = distances[i] + currentSpeeds[i] * InsidePassLookAheadSeconds;
                    float widthAhead = roadWidthsAt(ahead).x;
                    insideEdge = Mathf.Min(edge, Mathf.Max(0f, widthAhead * .5f - PassEdgeClearance));
                }
                return Mathf.Clamp(offset * inside, -edge, insideEdge) * inside;
            }

            float OtherLane(int other) => startLanes == null ? lanes[other] : startLanes[other];
            float PaceAdvantage(int other) => paceSpeeds == null ? speeds[i] - speeds[other] : paceSpeeds[i] - paceSpeeds[other];
            float TrackGap(int other) => Mathf.Repeat(distances[other] - distances[i] + lapLength * .5f, lapLength) -
                lapLength * .5f;

            bool PassFitsAhead(float candidate) => passingLaneRanges == null ||
                candidate >= passingLaneRanges[i].x && candidate <= passingLaneRanges[i].y;

            bool HasAdjacentCar(float direction = 0f)
            {
                for (int j = 0; j < lanes.Length; j++)
                {
                    if (j == i || !onTrack[j] || Mathf.Abs(TrackGap(j)) >= carLength)
                        continue;
                    float separation = OtherLane(j) - lane;
                    float offset = Mathf.Abs(separation);
                    if (offset >= carWidth * .5f && offset <= carWidth * 1.75f &&
                        (direction == 0f || separation * direction > 0f))
                        return true;
                }
                return false;
            }

            float ComfortClearance(int other, float gap, float side, float parallelMargin = ParallelClearanceMargin) => LateralComfortClearance(
                ContactWidthBetween(i, other), comfortMargin, gap, side, currentSpeeds[i], currentSpeeds[other],
                startLaneSpeeds?[i] ?? control.LateralSpeed, startLaneSpeeds?[other] ?? 0f, parallelMargin);

            float TrafficRoom(float direction, float easingRoom, float parallelMargin, bool followOpeningGap,
                out float opening, out int limitingNeighbor)
            {
                float room = float.PositiveInfinity;
                opening = 0f;
                limitingNeighbor = -1;
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
                    float longitudinalRoom = Mathf.Max(Mathf.Min(gap, future) - laneClearanceAhead,
                        -laneClearanceBehind - Mathf.Max(gap, future));
                    // A drafting partner can be close behind without occupying the lane-change corridor.
                    if (longitudinalRoom >= TrafficLongitudinalEaseDistance || directedDraftPass && longitudinalRoom > 0f)
                        continue;
                    float lateralRoom = side - ComfortClearance(j, gap, separation, parallelMargin);
                    // Use only room an adjacent car has already opened in the same direction on this step.
                    float gainedRoom = followOpeningGap ? Mathf.Max(0f, direction * (lanes[j] - startLanes[j])) : 0f;
                    float easedRoom = longitudinalRoom > 0f
                        ? Mathf.Lerp(0f, easingRoom, longitudinalRoom / TrafficLongitudinalEaseDistance)
                        : float.NegativeInfinity;
                    float availableRoom = Mathf.Max(lateralRoom + gainedRoom, easedRoom);
                    if (availableRoom < room)
                    {
                        room = availableRoom;
                        opening = availableRoom - Mathf.Max(lateralRoom, easedRoom);
                        limitingNeighbor = j;
                    }
                }
                return room;
            }

            bool LaneClear(float candidate, float ahead, bool followingPreferred = false, bool comfort = false,
                float lateralBuffer = 0f, bool recoveringInsideEdge = false, bool recordSweptRejection = false)
            {
                float clearance = followingPreferred ? PreferredLaneEdgeClearance(i, candidate, inside) : PassEdgeClearance;
                float edge = Mathf.Max(0f, widths[i] * .5f - clearance);
                // An inside lane can move out of bounds when the track narrows.
                // Allow a partial return and still check the entire move against traffic.
                bool reducesInsideOverlap = recoveringInsideEdge && candidate * inside < lane * inside;
                if (Mathf.Abs(candidate) > edge && !reducesInsideOverlap)
                {
                    if (recordSweptRejection)
                        sweptRoadEdgeRejected = true;
                    return false;
                }
                for (int j = 0; j < lanes.Length; j++)
                {
                    if (j == i || !onTrack[j])
                        continue;
                    float gap = TrackGap(j);
                    float horizon = Mathf.Max(seconds, Mathf.Min(anticipation, Mathf.Abs(candidate-lanes[i])/3f * anticipation));
                    float futureGap = gap + (currentSpeeds[j]-currentSpeeds[i])*horizon;
                    // Check the swept longitudinal interval and the whole lane-change corridor.
                    if (Mathf.Min(gap,futureGap) > ahead || Mathf.Max(gap,futureGap) < -laneClearanceBehind)
                        continue;
                    float before = OtherLane(j) - lane;
                    float after = (comfort ? OtherLane(j) : lanes[j]) - candidate;
                    bool sameSide = before * after >= 0f;
                    float separation = sameSide ? Mathf.Min(Mathf.Abs(before), Mathf.Abs(after)) : 0f;
                    bool escaping = sameSide && Mathf.Abs(after) > Mathf.Abs(before);
                    float required = comfort ? ComfortClearance(j, gap, before)
                        : ContactWidthBetween(i, j) + (stabilize ? 0f : comfortMargin);
                    if (separation < required + lateralBuffer && !escaping)
                    {
                        if (recordSweptRejection)
                            sweptBlocker = j;
                        return false;
                    }
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
                float passBumperClearance = plate ? (draftPassGap?[i] ?? float.NaN) : float.NaN;
                bool settingUpPass = passBumperClearance >= 0f && !float.IsInfinity(passBumperClearance);
                for (int j = 0; j < lanes.Length; j++)
                {
                    if (j == i || !onTrack[j])
                        continue;
                    float gap = Mathf.Repeat(distances[j] - distances[i], lapLength);
                    if (gap == 0f && j > i)
                        continue;
                    float side = lanes[j] - lanes[i];
                    float contactWidth = ContactWidthBetween(i, j);
                    float lateralClearance = keepLaneControl && stabilizeLane?[i] == true
                        ? LateralComfortClearance(contactWidth, comfortMargin, gap, side, currentSpeeds[i], currentSpeeds[j],
                            laneControl[i].LateralSpeed, laneControl[j].LateralSpeed,
                            UsePreferredLineTraffic ? CloseParallelClearanceMargin : ParallelClearanceMargin)
                        : contactWidth + comfortMargin;
                    if (Mathf.Abs(side) >= lateralClearance)
                        continue;
                    float clearance = Mathf.Max(0f, gap - carSpacing);
                    float carBrakingLimit = brakingLimit * (brakingFactors == null ? 1f : brakingFactors[i]);
                    float leaderBrakingLimit = brakingLimit * (brakingFactors == null ? 1f : brakingFactors[j]);
                    bool closeDraft = false;
                    float bumpStrength = 0f;
                    if (legacyPassing && draftLeaders?[i] == j && stabilizeLane?[i] != false &&
                        stabilizeLane?[j] != false && gap > 0f)
                    {
                        float startSide = startLanes == null ? side : startLanes[j] - startLanes[i];
                        float offset = Mathf.Max(Mathf.Abs(startSide), Mathf.Abs(side));
                        float speedDifference = Mathf.Abs(currentSpeeds[j] - currentSpeeds[i]);
                        bool steadyLeader = speeds[j] >= currentSpeeds[j] - (RaceSessionRun.CoastingDeceleration + .01f) * seconds;
                        bool leavingWake = settingUpPass && draftPassLane?[i] == true && startSide * side >= 0f &&
                            Mathf.Abs(side) > Mathf.Abs(startSide);
                        closeDraft = (offset <= TrackDraft.CloseFollowAlignment || leavingWake) &&
                            speedDifference <= DraftFollowMaximumSpeedDifference && steadyLeader;
                        if (closeDraft && plate && !settingUpPass &&
                            offset <= VehicleCollision001.DraftBumpAlignment &&
                            Mathf.Min(currentSpeeds[i], currentSpeeds[j]) >= VehicleCollision001.DraftBumpMinimumSpeed &&
                            speedDifference <= VehicleCollision001.DraftBumpMaximumClosingSpeed + .2f)
                            bumpStrength = draftBumpIntent?[i] ?? 0f;
                    }
                    float limit = closeDraft
                        ? DraftFollowingSpeedLimit(currentSpeeds[i], speeds[j], gap, seconds, carBrakingLimit, bumpStrength, plate,
                            passBumperClearance)
                        : FollowingSpeedLimit(currentSpeeds[i], speeds[j], clearance,
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

    private static void OrderFittedMoves(float[] startLanes, float[] fittedAdvances, float inside, Span<int> order)
    {
        // Resolve each arc from the side it moves toward. Leave other cars in their original order.
        int count = 0;
        for (int group = 0; group < 2; group++)
        {
            float direction = group == 0 ? -inside : inside;
            int firstInGroup = count;
            for (int i = 0; i < order.Length; i++)
            {
                if (!(fittedAdvances[i] * direction > 0f))
                    continue;
                int slot = count;
                while (slot > firstInGroup && startLanes[i] * direction > startLanes[order[slot - 1]] * direction)
                {
                    order[slot] = order[slot - 1];
                    slot--;
                }
                order[slot] = i;
                count++;
            }
        }
        for (int i = 0; i < order.Length; i++)
        {
            float advance = fittedAdvances[i] * inside;
            if (!(advance < 0f || advance > 0f))
                order[count++] = i;
        }
    }

    internal static void ApplyLaneVariation(int index, float lapLength, float seconds, float[] distances,
        float[] widths, bool[] onTrack, float[] lanes, float[] currentSpeeds, TrackDraft draft,
        float[] startLanes = null)
    {
        const float carLength = VehicleCollision001.ContactLength;
        const float carWidth = VehicleCollision001.ContactWidth;
        float appliedOffset = draft.AppliedLaneOffsets[index];
        float desiredOffset = Mathf.MoveTowards(appliedOffset, draft.LaneOffsets[index], DraftLateralSpeed * seconds);
        float movement = desiredOffset - appliedOffset;
        if (movement == 0f)
            return;

        float lane = lanes[index];
        float direction = Mathf.Sign(movement);
        float requestedLane = lane + movement;
        float limit = Mathf.Max(0f, widths[index] * .5f - carWidth * .5f) +
            Mathf.Max(0f, direction * draft.ApronEdgeAllowance[index]);
        float room = limit - direction * lane;
        for (int other = 0; other < lanes.Length && room > 0f; other++)
        {
            if (other == index || !onTrack[other])
                continue;

            float side = direction * (lanes[other] - lane);
            if (side <= 0f)
                continue;

            float gap = Mathf.Repeat(distances[other] - distances[index] + lapLength * .5f, lapLength) - lapLength * .5f;
            float futureGap = gap + (currentSpeeds[other] - currentSpeeds[index]) * seconds;
            if (Mathf.Min(gap, futureGap) >= carLength || Mathf.Max(gap, futureGap) <= -carLength)
                continue;

            // Permit a shallow side rub. The normal swept collision resolver
            // separates the cars and determines the impact severity.
            float startSide = direction * ((startLanes?[other] ?? lanes[other]) - lane);
            if (startSide > 0f)
                side = Mathf.Min(side, startSide);
            room = Mathf.Min(room, side - (carWidth - LaneVariationContactDepth));
        }

        // A car already touching a boundary or another car can still move away.
        float actualMovement = direction * Mathf.Min(Mathf.Abs(movement), Mathf.Max(0f, room));
        float candidate = lane + actualMovement;
        draft.AppliedLaneOffsets[index] = candidate == requestedLane ? desiredOffset : appliedOffset + actualMovement;
        lanes[index] = candidate;
    }

    private static float LateralComfortClearance(float contactWidth, float comfortMargin, float gap, float side, float speed,
        float otherSpeed, float lateralSpeed, float otherLateralSpeed, float parallelMargin = ParallelClearanceMargin)
    {
        float longitudinalClosing = Mathf.Max(0f, (speed - otherSpeed) * Mathf.Sign(gap));
        float lateralClosing = Mathf.Max(0f, (lateralSpeed - otherLateralSpeed) * Mathf.Sign(side));
        float convergence = Mathf.Max(longitudinalClosing / 3f, lateralClosing / 1.25f);
        return contactWidth + Mathf.Lerp(Mathf.Min(comfortMargin, parallelMargin),
            comfortMargin, convergence);
    }

    internal static float DraftFollowingSpeedLimit(float currentSpeed, float leaderNextSpeed, float gap,
        float seconds, float brakingLimit, float bumpStrength, bool plate = false, float passBumperClearance = float.NaN)
    {
        if (seconds <= 0f || brakingLimit <= 0f)
            return currentSpeed;
        bool settingUpPass = plate && passBumperClearance >= 0f && !float.IsInfinity(passBumperClearance);
        float bumperClearance = settingUpPass ? passBumperClearance : plate
            ? (bumpStrength > 0f ? -Mathf.Lerp(.03f, .19f, bumpStrength) : PlateDraftBumperClearance)
            : DraftFollowBumperClearance;
        // Match a steady leader as the gap closes. The small plate offset permits swept bumper contact.
        float gapError = gap - VehicleCollision001.ContactLength - bumperClearance;
        float closingSpeed = Mathf.Clamp(gapError * DraftGapResponse, -1f, settingUpPass ? DraftPassClosingSpeed : 1f);
        if (settingUpPass)
        {
            float relativeSpeed = Mathf.Max(0f, currentSpeed - leaderNextSpeed);
            float travelBeforeResponse = relativeSpeed * seconds;
            if (closingSpeed > 0f)
            {
                float remainingGap = Mathf.Max(0f, gapError - travelBeforeResponse);
                closingSpeed = Mathf.Min(closingSpeed, Mathf.Sqrt(2f * brakingLimit * remainingGap));
            }
            else
            {
                // Lift to create the gap when there is room to do so without risking contact.
                float lift = Mathf.Min(brakingLimit, DraftPassLiftDeceleration);
                float room = gap - VehicleCollision001.ContactLength - travelBeforeResponse;
                if (room >= DraftPassCorridorReserve + relativeSpeed * relativeSpeed / (2f * lift))
                    brakingLimit = lift;
            }
        }
        return Mathf.Max(0f, Mathf.Max(leaderNextSpeed + closingSpeed, currentSpeed - brakingLimit * seconds));
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
