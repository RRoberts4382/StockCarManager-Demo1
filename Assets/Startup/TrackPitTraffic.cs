using UnityEngine;

internal sealed class TrackPitTraffic
{
    private const float MphToMetersPerSecond = 0.44704f;
    private const float StallLaneClearance = 2.2f;
    private const float QueueSeparationSpeed = .75f;
    private const float CautionExitSetback = 6f;
    private readonly float lapLength;
    private readonly float entryDistance;
    private readonly float bypassDistance;
    private readonly float speedLimit;
    private readonly float[] boundaries;
    private readonly float laneOffset;
    private readonly bool singleThroughLane;
    private readonly bool recoverStoppedQueueOverlap;
    private readonly bool allowCompactStallDeparture;
    private readonly float singleFileApproachStart;
    private readonly float singleFileHoldDistance;
    private const float LaneSpacing = 2.6f;
    private const float EdgeClearance = 1.15f;
    private const float MergeApproach = 20f;
    private const float EarlyMergeApproach = 90f;
    private const float SingleFileSpacing = 5f;
    private const float PullOutSpeed = 4f;
    private float throughLane, secondThroughLane;
    private bool hasPassingLane;
    private Car[] field;
    private float[] stalls;
    private float[] stallLanes;

    internal System.Func<Car, float, float, Vector3> RoutePosition;
    internal System.Func<Car, Vector3> CurrentPosition;
    internal System.Func<Car, float> CurrentHeading;
    internal System.Func<Car, float, Vector3> PredictedPosition;
    internal System.Func<Car, float> CurrentSpeed;
    internal System.Func<Car, float> CurrentLane;
    private Car[] stepField;
    private Vector3[] stepPositions;
    private float[] stepHeadings, stepSpeeds, stepLanes, plannedStepSpeeds;
    private bool stepActive;
    private float stepTime, predictionOffset;
    private Car[] AvoidanceField => stepActive ? stepField : field;
    private static bool SameCar(Car a, Car b) => ReferenceEquals(a, b) ||
        a.FieldIndex >= 0 && a.FieldIndex == b.FieldIndex;
    private bool HasStep(Car car) => stepActive && car.FieldIndex >= 0 && car.FieldIndex < stepField.Length;
    internal float SpeedAtStepStart(Car car) => HasStep(car) ? stepSpeeds[car.FieldIndex] :
        CurrentSpeed != null ? CurrentSpeed(car) : car.ActualPitSpeed;
    internal float LaneAtStepStart(Car car) => HasStep(car) ? stepLanes[car.FieldIndex] :
        CurrentLane != null ? CurrentLane(car) : car.PitLane;
    internal Car StateAtStepStart(Car car) => HasStep(car) ? stepField[car.FieldIndex] : car;
    internal Vector3 PositionAtStepStart(Car car) => HasStep(car) ? stepPositions[car.FieldIndex] : CurrentPosition(car);
    private float HeadingAtStepStart(Car car) => HasStep(car) ? stepHeadings[car.FieldIndex] : CurrentHeading(car);
    private Vector3 PredictFromStepStart(Car car, float seconds) =>
        PredictedPosition(HasStep(car) ? stepField[car.FieldIndex] : car, seconds);

    internal void BeginStep(float simulationSeconds)
    {
        stepActive = false;
        if (field == null) return;
        if (stepField == null || stepField.Length != field.Length)
        {
            stepField = new Car[field.Length];
            stepPositions = new Vector3[field.Length];
            stepHeadings = new float[field.Length];
            stepSpeeds = new float[field.Length];
            plannedStepSpeeds = new float[field.Length];
            stepLanes = new float[field.Length];
            for (int i = 0; i < field.Length; i++) stepField[i] = new Car();
        }
        stepTime = simulationSeconds;
        predictionOffset = 0f;
        for (int i = 0; i < field.Length; i++)
        {
            var car = field[i];
            car.CopyTo(stepField[i]);
            stepPositions[i] = CurrentPosition != null ? CurrentPosition(car) : Vector3.zero;
            stepHeadings[i] = CurrentHeading != null ? CurrentHeading(car) : car.PitYaw;
            stepSpeeds[i] = SpeedAtStepStart(car);
            plannedStepSpeeds[i] = stepSpeeds[i];
            stepLanes[i] = LaneAtStepStart(car);
            car.MovementBlockedThisStep = false;
        }
        stepActive = true;
    }

    internal float PredictedSpeed(Car car) => HasStep(car) ? plannedStepSpeeds[car.FieldIndex] : SpeedAtStepStart(car);
    internal bool TakesBackExit(Car car) => CanUseBackExit(car, float.PositiveInfinity);

    internal void PlanTrafficSpeeds(float[] speeds, float seconds)
    {
        if (!stepActive) return;
        for (int i = 0; i < stepField.Length; i++)
        {
            plannedStepSpeeds[i] = Mathf.Max(0f, speeds[i]);
            if (PullingOut(stepField[i]))
                plannedStepSpeeds[i] = Mathf.Min(PullOutSpeed, plannedStepSpeeds[i]);
        }
        // Propagate braking through a queue before any car moves. Repeated passes
        // converge independently of array order; they do not shorten braking limits.
        for (int pass = 0; pass < stepField.Length; pass++)
        {
            bool changed = false;
            for (int i = 0; i < stepField.Length; i++)
            {
                var car = stepField[i];
                if (!car.InPit || car.InGarage || plannedStepSpeeds[i] <= 0f) continue;
                float heading = stepHeadings[i] * Mathf.Deg2Rad;
                var forward = new Vector3(Mathf.Sin(heading), 0f, Mathf.Cos(heading));
                var across = new Vector3(forward.z, 0f, -forward.x);
                float target = plannedStepSpeeds[i];
                for (int j = 0; j < stepField.Length; j++)
                {
                    var leader = stepField[j];
                    if (i == j || leader.InGarage || ParkedInStall(leader)) continue;
                    float gap, aligned;
                    bool sameRoute = SamePitRoute(car, leader) && !car.BackExit;
                    bool earlyMerge = InSingleFileApproach(car) && !CanUseBackExit(leader, float.PositiveInfinity);
                    float approachStart = earlyMerge ? singleFileApproachStart : RoadEnd - MergeApproach;
                    bool converging = sameRoute && hasPassingLane &&
                        car.PitDistance >= approachStart && leader.PitDistance >= approachStart;
                    if (converging || sameRoute && RoutePosition == null)
                    {
                        // A car held in the second lane may need the adjacent
                        // through-lane car to pass before it can merge behind it.
                        if (earlyMerge && converging && !car.SecondTravelLane && leader.SecondTravelLane &&
                            leader.PitDistance >= singleFileHoldDistance - .5f &&
                            stepSpeeds[j] <= .15f && Mathf.Abs(leader.PitLane - car.PitLane) > 2.1f)
                            continue;

                        gap = leader.PitDistance - car.PitDistance;
                        if (gap < 0f || gap == 0f && (!converging || j > i) ||
                            !converging && Mathf.Abs(leader.PitLane - car.PitLane) > 2.1f) continue;
                        aligned = Mathf.Cos(Mathf.DeltaAngle(car.PitYaw, leader.PitYaw) * Mathf.Deg2Rad);
                    }
                    else
                    {
                        if (RoutePosition == null) continue;
                        Vector3 delta = stepPositions[j] - stepPositions[i];
                        gap = Vector3.Dot(delta, forward);
                        if (gap <= 0f || Mathf.Abs(delta.y) > 1.5f || Mathf.Abs(Vector3.Dot(delta, across)) > 2.1f) continue;
                        aligned = Mathf.Cos(Mathf.DeltaAngle(stepHeadings[i], stepHeadings[j]) * Mathf.Deg2Rad);
                    }
                    if (gap > 100f) continue;
                    if (aligned < .5f) continue;
                    float leaderSpeed = plannedStepSpeeds[j] * aligned;
                    // 3.8 m center spacing leaves room outside the physical guard's
                    // 3.14 m padded footprint, including curved-connector compression.
                    float spacing = earlyMerge && converging ? SingleFileSpacing : 3.8f;
                    if (earlyMerge && converging && gap < spacing)
                        leaderSpeed = Mathf.Max(0f, leaderSpeed - 1.5f * (spacing - gap));
                    target = Mathf.Min(target, ApproachLimit(leaderSpeed, Mathf.Max(0f, gap - spacing), seconds));
                }
                float brakeFloor = Mathf.Min(speeds[i], Mathf.Max(0f, stepSpeeds[i] - PitApproachDeceleration * seconds));
                target = Mathf.Max(brakeFloor, target);
                if (target < plannedStepSpeeds[i] - .00001f) { plannedStepSpeeds[i] = target; changed = true; }
            }
            if (!changed) break;
        }
        for (int i = 0; i < stepField.Length; i++) speeds[i] = plannedStepSpeeds[i];
    }

    internal void EndStep() => stepActive = false;

    private bool BlockMovement(Car car, Car other, string reason, float clearance, float seconds)
    {
        car.MovementBlockedThisStep = true;
        car.MovementBlockedByIndex = other.FieldIndex;
        car.MovementBlockReason = reason;
        car.MovementBlockTime = stepTime;
        car.MovementBlockClearance = clearance;
        car.MovementBlockPredictionSeconds = seconds;
        car.MovementBlockCount++;
        return false;
    }

    internal System.Func<Car, float, bool> EntryReady;
    internal System.Func<Car, float, bool> CautionTailExitClear;
    internal System.Func<Car, bool, bool> ExitGapClear;
    internal System.Func<bool> ExitCheckAvailable;
    internal const float PitApproachDeceleration = 6f;

    public float RoadStart { get; }
    public float RoadEnd { get; }
    public float RoadLength => RoadEnd - RoadStart;
    internal float? PaceBayParkingDistance { get; set; }
    public float ParkingDistance => PaceBayParkingDistance ?? (RoadEnd - Mathf.Min(4f, RoadLength * 0.1f));
    public float RouteLength => boundaries[2];
    public float SpeedLimit => speedLimit;

    internal float ThroughLaneAt(float distance) => TravelLaneAt(distance, false);

    private float TravelLaneAt(float distance, bool secondLane)
    {
        float blend = distance < RoadStart
            ? Mathf.SmoothStep(0f, 1f, distance / Mathf.Min(20f, RoadStart))
            : distance > RoadEnd
                ? Mathf.SmoothStep(0f, 1f, (RouteLength - distance) / Mathf.Min(20f, RouteLength - RoadEnd)) : 1f;
        return (secondLane && hasPassingLane ? secondThroughLane : throughLane) * blend;
    }
    private bool InSingleFileApproach(Car car) =>
        hasPassingLane && car.InPit && !car.BackExit && !car.Maneuver &&
        car.PitDistance >= singleFileApproachStart && !CanUseBackExit(car, float.PositiveInfinity);

    private bool CanJoinSingleFile(Car car)
    {
        foreach (var other in AvoidanceField)
            if (SamePitRoute(car, other) && !ParkedInStall(other) &&
                !CanUseBackExit(other, float.PositiveInfinity) &&
                Mathf.Abs(other.PitDistance - car.PitDistance) < SingleFileSpacing - .25f)
                return false;

        return true;
    }

    public float EntryDistance => entryDistance;
    public float ExitDistance => entryDistance + bypassDistance;

    internal sealed class Car
    {
        // Keep cumulative travel in double precision; Unity geometry still uses floats.
        public double PreciseTrackDistance;
        public float TrackDistance
        {
            get => (float)PreciseTrackDistance;
            set => PreciseTrackDistance = value;
        }
        public float PitDistance;
        public float PitLane;
        public int FieldIndex = -1;
        public float NextEntry = float.PositiveInfinity;
        internal float DeferredEntry = float.NegativeInfinity;
        internal bool ApronEntryActive;
        public float LastExit = float.NegativeInfinity;
        public bool InPit;
        public bool InGarage;
        internal bool CautionPitTailRequired;
        internal bool CooldownParking, BypassingBlockedStall;
        public bool FrontEntry;
        public bool BackExit;
        public float ConnectorDistance;
        public float ConnectorEntryLane;
        internal float PitYaw, ActualPitSpeed, ManeuverRetrySeconds;
        internal bool ExitMergeGranted, WaitingForExitMerge;
        internal bool ApronExitActive, ApronMergeGranted, ApronExitInitialized;
        internal float ApronExitId, ApronMergeStart, ApronMergeEnd;
        internal int ExitBlockedBy = -1;
        internal float ExitConflictSeconds;
        internal float StallEntryHoldDistance = float.PositiveInfinity;
        internal int StallEntryReservationOwner = -1;
        internal float ExitMergeRetrySeconds, ExitHoldDistance;
        internal bool Maneuver, ArrivalManeuver;
        internal float ManeuverStart, ManeuverLength, ManeuverFrom, ManeuverTo;
        internal float ApproachStartLane;
        internal bool ApproachingStall, SecondTravelLane;
        internal float DepartureStart = float.NegativeInfinity;
        public bool Retired;
        public float FlatTireLaneBlend;
        public double Odometer;
        // Reuse snapshot objects: no per-step allocations in the avoidance loop.
        internal void CopyTo(Car target)
        {
            target.PreciseTrackDistance = PreciseTrackDistance;
            target.PitDistance = PitDistance;
            target.PitLane = PitLane;
            target.FieldIndex = FieldIndex;
            target.NextEntry = NextEntry;
            target.DeferredEntry = DeferredEntry;
            target.ApronEntryActive = ApronEntryActive;
            target.LastExit = LastExit;
            target.InPit = InPit;
            target.CautionPitTailRequired = CautionPitTailRequired;
            target.CooldownParking = CooldownParking;
            target.BypassingBlockedStall = BypassingBlockedStall;
            target.InGarage = InGarage;
            target.FrontEntry = FrontEntry;
            target.BackExit = BackExit;
            target.ConnectorDistance = ConnectorDistance;
            target.ConnectorEntryLane = ConnectorEntryLane;
            target.PitYaw = PitYaw;
            target.ActualPitSpeed = ActualPitSpeed;
            target.ManeuverRetrySeconds = ManeuverRetrySeconds;
            target.ExitMergeGranted = ExitMergeGranted;
            target.WaitingForExitMerge = WaitingForExitMerge;
            target.ApronExitActive = ApronExitActive;
            target.ApronMergeGranted = ApronMergeGranted;
            target.ApronExitInitialized = ApronExitInitialized;
            target.ApronExitId = ApronExitId;
            target.ApronMergeStart = ApronMergeStart;
            target.ApronMergeEnd = ApronMergeEnd;
            target.ExitBlockedBy = ExitBlockedBy;
            target.ExitConflictSeconds = ExitConflictSeconds;
            target.StallEntryHoldDistance = StallEntryHoldDistance;
            target.StallEntryReservationOwner = StallEntryReservationOwner;
            target.ExitMergeRetrySeconds = ExitMergeRetrySeconds;
            target.ExitHoldDistance = ExitHoldDistance;
            target.Maneuver = Maneuver;
            target.ArrivalManeuver = ArrivalManeuver;
            target.ManeuverStart = ManeuverStart;
            target.ManeuverLength = ManeuverLength;
            target.ManeuverFrom = ManeuverFrom;
            target.ManeuverTo = ManeuverTo;
            target.ApproachStartLane = ApproachStartLane;
            target.ApproachingStall = ApproachingStall;
            target.SecondTravelLane = SecondTravelLane;
            target.DepartureStart = DepartureStart;
            target.Retired = Retired;
            target.FlatTireLaneBlend = FlatTireLaneBlend;
            target.Odometer = Odometer;
        }
        internal bool WaitingForStallDeparture;
        internal int DepartureBlockedByIndex = -1;
        internal string DepartureBlockReason;
        internal bool MovementBlockedThisStep;
        internal int MovementBlockedByIndex = -1, MovementBlockCount;
        internal string MovementBlockReason;
        internal float MovementBlockTime = -1f, MovementBlockClearance, MovementBlockPredictionSeconds;

    }

    public TrackPitTraffic(float lapLength, float entryDistance, float exitDistance,
        float entryLength, float roadLength, float exitLength, TrackPitRoad road, bool singleThroughLane = false,
        bool recoverStoppedQueueOverlap = false, bool allowCompactStallDeparture = false)
    {
        this.lapLength = lapLength;
        this.singleThroughLane = singleThroughLane;
        this.recoverStoppedQueueOverlap = recoverStoppedQueueOverlap;
        this.allowCompactStallDeparture = allowCompactStallDeparture;
        this.entryDistance = entryDistance;
        bypassDistance = Mathf.Repeat(exitDistance - entryDistance, lapLength);
        speedLimit = road.speedLimitMph * MphToMetersPerSecond;
        laneOffset = road.widthMeters * 0.25f;
        RoadStart = entryLength;
        RoadEnd = entryLength + roadLength;
        singleFileApproachStart = RoadEnd - Mathf.Min(EarlyMergeApproach, roadLength * .35f);
        singleFileHoldDistance = RoadEnd - Mathf.Min(12f, (RoadEnd - singleFileApproachStart) * .25f);
        boundaries = new[]
        {
            entryLength + roadLength * road.speedLimitStart,
            entryLength + roadLength * road.speedLimitEnd,
            entryLength + roadLength + exitLength
        };
    }

    public Car CreateTrackCar(float distance) => new Car { TrackDistance = distance };

    public Car CreateStalledCar(float stall) => new Car
    {
        TrackDistance = lapLength + entryDistance,
        PitDistance = stall,
        InPit = true
    };

    public void CallToStall(Car car, float stall)
    {
        float distance = car.TrackDistance;
        if (car.InPit)
        {
            if (car.PitDistance <= stall)
                return;
            distance += bypassDistance;
        }
        float entry = EntryFor(car);
        car.NextEntry = Mathf.Max(car.DeferredEntry, entry + Mathf.Ceil((distance - entry) / lapLength) * lapLength);
    }

    public float NextEntryDistance(Car car)
    {
        float entry = EntryFor(car);
        return entry + Mathf.Ceil((car.TrackDistance - entry) / lapLength) * lapLength;
    }

    public float DistanceToEntry(Car car) => Mathf.Repeat(EntryFor(car) - car.TrackDistance, lapLength);

    internal bool EntranceClear(Car car, float clearance = 5.5f)
    {
        if (field == null || car.FieldIndex < 0) return true;
        bool front = UsesFrontEntry(car);
        // Use the committed field so a car admitted earlier in the step reserves the mouth.
        foreach (var other in field)
        {
            if (SameCar(car, other) || !other.InPit || other.InGarage || other.BackExit ||
                (splitRoad && front != other.FrontEntry && other.PitDistance < frontJoin)) continue;

            float physicalDistance = front
                ? Mathf.Min(other.PitDistance, frontJoin) * frontEntryLength / frontJoin + Mathf.Max(0f, other.PitDistance - frontJoin)
                : other.PitDistance;
            if (physicalDistance < clearance) return false;
        }
        return true;
    }

    internal void DeferEntry(Car car)
    {
        car.DeferredEntry = car.NextEntry + lapLength;
        car.NextEntry = car.DeferredEntry;
        car.ApronEntryActive = false;
    }

    private float[] progressAlongRoute;
    private float frontEntryLength, frontEntry, frontJoin, backLeave, backExit, backExitLength;
    private bool splitRoad;
    public bool CombinedRoute { get; set; }

    public void ConfigureSplitRoad(float entry, float join, float leave, float exit, float exitLength, float entryLength)
    {
        splitRoad = true;
        frontEntry = entry;
        frontJoin = join;
        frontEntryLength = entryLength;
        backLeave = leave;
        backExit = exit;
        backExitLength = exitLength;
    }

    private bool UsesFrontEntry(Car car) =>
        splitRoad && !CombinedRoute && car.FieldIndex >= 0 && stalls[car.FieldIndex] >= frontJoin;

    internal bool TakesFrontEntry(Car car) => UsesFrontEntry(car);

    private float EntryFor(Car car) => UsesFrontEntry(car) ? frontEntry : entryDistance;

    private bool CanUseBackExit(Car car, float stopAt) => splitRoad && !CombinedRoute &&
        car.FieldIndex >= 0 && !car.FrontEntry && car.PitDistance <= backLeave && stopAt != stalls[car.FieldIndex];

    public void SetProgressAlongRoute(float[] trackDistances)
    {
        progressAlongRoute = new float[trackDistances.Length];
        for (int i = 1; i < trackDistances.Length - 1; i++)
        {
            float progress = Mathf.Repeat(trackDistances[i] - entryDistance, lapLength);
            if (progress > bypassDistance)
                progress = progress - bypassDistance < lapLength - progress ? bypassDistance : 0f;
            progressAlongRoute[i] = Mathf.Max(progressAlongRoute[i - 1], progress);
        }
        progressAlongRoute[trackDistances.Length - 1] = bypassDistance;
    }

    public float RaceDistance(Car car)
    {
        if (!car.InPit)
            return car.TrackDistance;

        if (car.BackExit)
        {
            float leave = RouteProgress(backLeave);
            float exit = Mathf.Repeat(backExit - entryDistance, lapLength);
            return car.TrackDistance + Mathf.Lerp(leave, exit, car.ConnectorDistance / backExitLength);
        }
        float progress = RouteProgress(car.PitDistance);
        if (!car.FrontEntry)
            return car.TrackDistance + progress;

        float entry = Mathf.Repeat(frontEntry - entryDistance, lapLength);
        float join = RouteProgress(frontJoin);
        return car.TrackDistance + (car.PitDistance < frontJoin
            ? Mathf.Lerp(0f, Mathf.Max(0f, join - entry), car.PitDistance / frontJoin)
            : Mathf.Max(0f, progress - entry));
    }

    private float RouteProgress(float pitDistance)
    {
        float fraction = Mathf.Clamp01(pitDistance / RouteLength);
        if (progressAlongRoute == null)
            return fraction * bypassDistance;

        float sample = fraction * (progressAlongRoute.Length - 1);
        int lower = System.Math.Min((int)sample, progressAlongRoute.Length - 2);
        return Mathf.Lerp(progressAlongRoute[lower], progressAlongRoute[lower + 1], sample - lower);
    }

    public void ConfigureField(Car[] cars, float[] stalls, float[] stallLanes, float inside)
    {
        field = cars;
        this.stalls = stalls;
        this.stallLanes = stallLanes;
        float outsideLane = -inside * laneOffset;
        float outsideClearance = MinimumStallClearance(outsideLane);
        float insideClearance = MinimumStallClearance(-outsideLane);
        throughLane = outsideClearance >= insideClearance ? outsideLane : -outsideLane;
        hasPassingLane = false;
        float edge = 2f * laneOffset - EdgeClearance;
        if (!singleThroughLane && edge * 2f >= LaneSpacing)
        {
            float bestShift = float.PositiveInfinity;
            float primary = throughLane;
            for (float left = -edge; left + LaneSpacing <= edge; left += .1f)
            {
                float right = left + LaneSpacing;
                if (MinimumStallClearance(left) < StallLaneClearance ||
                    MinimumStallClearance(right) < StallLaneClearance) continue;
                float leftShift = Mathf.Abs(left - throughLane);
                float rightShift = Mathf.Abs(right - throughLane);
                float shift = Mathf.Min(leftShift, rightShift);
                if (shift >= bestShift) continue;
                bestShift = shift;
                primary = leftShift <= rightShift ? left : right;
                secondThroughLane = leftShift <= rightShift ? right : left;
                hasPassingLane = true;
            }
            throughLane = primary;
        }
        for (int i = 0; i < cars.Length; i++)
        {
            cars[i].FieldIndex = i;
            if (cars[i].InPit && cars[i].PitDistance == stalls[i])
                cars[i].PitLane = stallLanes[i];
        }

        float MinimumStallClearance(float lane)
        {
            float clearance = float.PositiveInfinity;
            foreach (float stallLane in stallLanes)
                clearance = Mathf.Min(clearance, Mathf.Abs(stallLane - lane));
            return clearance;
        }
    }

    internal static float StallApproachBlend(float remaining) =>
        Mathf.SmoothStep(0f, 1f, (40f - remaining) / 34f);

    internal static float ManeuverLane(float distance, float start, float length, float from, float to, bool arrival)
    {
        if (!arrival && length >= StandardStallDepartureLength)
            return Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, (distance - start) / length));
        if (distance <= start) return from;
        if (distance >= start + length) return to;
        // Define the rear axle's S-curve, then recover the center from its tangent.
        // Both axles finish parallel without requiring a long straight through
        // the occupied stall behind. Invert center station along this monotone path.
        float a = RearAxleHeading.CenterToRearAxle, low = 0f, high = 1f;
        for (int iteration = 0; iteration < 16; iteration++)
        {
            float t = (low + high) * .5f;
            float yaw = Mathf.Atan((to - from) * 6f * t * (1f - t) / length);
            float station = start - a + length * t + a * Mathf.Cos(yaw);
            if (station < distance) low = t; else high = t;
        }
        float progress = (low + high) * .5f;
        float heading = Mathf.Atan((to - from) * 6f * progress * (1f - progress) / length);
        return Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, progress)) + a * Mathf.Sin(heading);
    }

    private float TrafficSpeed(Car car, float seconds, float speed, float stopAt)
    {
        int index = car.FieldIndex;
        if (index < 0 || seconds <= 0f) return speed;
        bool arrival = stopAt == stalls[index] && !car.BypassingBlockedStall;
        bool departure = !arrival && Mathf.Abs(car.PitDistance - stalls[index]) < .001f;
        if (car.Maneuver && (car.ArrivalManeuver != arrival ||
            car.PitDistance > car.ManeuverStart + car.ManeuverLength + .01f)) car.Maneuver = false;
        car.ManeuverRetrySeconds = Mathf.Max(0f, car.ManeuverRetrySeconds - seconds);
        bool canPlanArrival = arrival && (!car.CooldownParking || car.PitDistance >= RoadStart);
        if (!car.Maneuver && car.ManeuverRetrySeconds <= 0f &&
            (departure || canPlanArrival && stopAt - car.PitDistance <= 40f))
        {
            car.DepartureBlockedByIndex = -1;
            car.DepartureBlockReason = null;
            // Choose the shortest clear rear-axle-compatible route. Long routes
            // can intersect the occupied stall behind.
            float[] lengths = arrival ? StallArrivalLengths : allowCompactStallDeparture ? CompactDepartureLengths : ManeuverLengths;
            foreach (float length in lengths)
            {
                float start = arrival ? stopAt - length : car.PitDistance;
                if (start < car.PitDistance - .01f) continue;
                bool bothLanes = departure && hasPassingLane && start + length < singleFileApproachStart &&
                    (!splitRoad || CanUseBackExit(car, stopAt));
                bool preferSecond = bothLanes && (index & 1) != 0;
                if (TryManeuver(preferSecond) || bothLanes && TryManeuver(!preferSecond)) break;

                bool TryManeuver(bool secondLane)
                {
                    float to = arrival ? stallLanes[index] : secondLane ? secondThroughLane : throughLane;
                    if (!arrival && !DepartureWindowClear(car, length, to)) return false;
                    if (!ClearManeuver(car, start, length, car.PitLane, to, arrival)) return false;
                    car.WaitingForStallDeparture = false;
                    car.DepartureBlockedByIndex = -1;
                    car.DepartureBlockReason = null;
                    car.Maneuver = true; car.ArrivalManeuver = arrival;
                    car.ManeuverStart = start; car.ManeuverLength = length;
                    car.ManeuverFrom = car.PitLane; car.ManeuverTo = to;
                    if (departure) car.SecondTravelLane = secondLane;
                    return true;
                }
            }
            if (!car.Maneuver)
            {
                car.WaitingForStallDeparture = departure;
                car.ManeuverRetrySeconds = .25f;
            }
        }
        if (!car.Maneuver && departure) return 0f;
        if (!car.Maneuver && canPlanArrival && car.ManeuverRetrySeconds > 0f)
        {
            if (!car.CooldownParking) return 0f;

            // Keep the travel lane and creep while a safe turn can still be
            // found. If the last turn-in point is reached, try the stall next lap.
            const float shortestArrival = 4.5f;
            float toLastTurn = stopAt - shortestArrival - car.PitDistance;
            if (toLastTurn <= .02f)
            {
                car.BypassingBlockedStall = true;
                arrival = false;
            }
            else
                speed = Mathf.Min(speed, toLastTurn / seconds, PullOutSpeed);
        }
        if (PullingOut(car))
            speed = Mathf.Min(speed, PullOutSpeed);
        if (arrival)
        {
            speed = Mathf.Min(speed, ApproachLimit(0f, Mathf.Max(0f, stopAt - car.PitDistance), seconds));
            if (car.Maneuver && car.ManeuverLength < 6f)
                speed = Mathf.Min(speed, ApproachLimit(PullOutSpeed,
                    Mathf.Max(0f, car.ManeuverStart - car.PitDistance), seconds));
        }
        float laneVariation = 0f;
        if (!car.Maneuver && !car.WaitingForExitMerge && (!arrival || stopAt - car.PitDistance > 40f))
        {
            // Fade the lateral variation before the connectors.
            float room = Mathf.Min(car.PitDistance - Mathf.Max(RoadStart, boundaries[0]),
                Mathf.Min(RoadEnd, boundaries[1]) - car.PitDistance);
            float blend = Mathf.SmoothStep(0f, 1f, room / 12f);
            laneVariation = blend * TrackState001RaceStart.PaceSpeedVariation(index, stepTime, .08f);
        }
        speed = StallEntryReservationSpeed(car, speed, seconds);
        bool joiningSingleFile = InSingleFileApproach(car) && car.SecondTravelLane;
        bool joinClear = joiningSingleFile && CanJoinSingleFile(car);
        if (joiningSingleFile && !joinClear && car.PitDistance <= singleFileHoldDistance)
        {
            // Hold the second lane on the wide approach until a gap opens.
            float room = singleFileHoldDistance - car.PitDistance;
            speed = Mathf.Min(speed, ApproachLimit(0f, room, seconds), room / seconds);
        }
        if (car.WaitingForExitMerge)
            speed = Mathf.Min(speed, ApproachLimit(0f, Mathf.Max(0f, car.ExitHoldDistance - car.PitDistance), seconds));
        float parameterScale = car.FrontEntry && car.PitDistance < frontJoin ? frontEntryLength / frontJoin : 1f;
        if (arrival && stopAt - car.PitDistance < .001f) speed = (stopAt - car.PitDistance) * parameterScale / seconds;
        bool recoverExitQueue = false;
        if (recoverStoppedQueueOverlap && speed > 0f)
            foreach (var other in AvoidanceField)
                if (StoppedQueueOverlapBehind(car, other))
                {
                    speed = Mathf.Min(speed, QueueSeparationSpeed);
                    recoverExitQueue = car.PitDistance > RoadEnd;
                    break;
                }
        float travel = Mathf.Min(speed * seconds / parameterScale, Mathf.Max(0f, stopAt - car.PitDistance));
        float nextLane;
        if (recoverExitQueue)
            nextLane = car.PitLane;
        else if (car.Maneuver)
            nextLane = ManeuverLane(car.PitDistance + travel, car.ManeuverStart, car.ManeuverLength,
                car.ManeuverFrom, car.ManeuverTo, car.ArrivalManeuver);
        else
        {
            float targetLane = joiningSingleFile
                ? (joinClear ? TravelLaneAt(car.PitDistance, false) : car.PitLane)
                : TravelLaneAt(car.PitDistance, car.SecondTravelLane) + laneVariation;
            nextLane = Mathf.MoveTowards(car.PitLane, targetLane, 3f * seconds);
        }
        if (travel <= 0f) return 0f;
        // Test the complete step, including corner sweep and traffic approaching
        // from behind. A blocked maneuver waits without sliding sideways in place.
        var previousSweep = (Distance: car.PitDistance, Lane: car.PitLane, Yaw: car.PitYaw, Seconds: 0f);
        for (int sample = 1; sample <= 8; sample++)
        {
            float t = sample / 8f;
            float lane = Mathf.Lerp(car.PitLane, nextLane, t);
            float yaw = RearAxleHeading.Advance(car.PitYaw,
                new Vector3(lane - car.PitLane, 0f, travel * t), RearAxleHeading.CenterToRearAxle);
            float distance = car.PitDistance + travel * t;
            float sampleSeconds = seconds * t;
            if (!ClearPose(car, distance, lane, yaw, sampleSeconds, previousSweep: previousSweep)) return 0f;
            previousSweep = (distance, lane, yaw, sampleSeconds);
        }
        car.PitLane = nextLane;
        if (joinClear && Mathf.Abs(nextLane - TravelLaneAt(car.PitDistance, false)) < .02f)
            car.SecondTravelLane = false;
        return speed;
    }

    internal static float ApproachLimit(float targetSpeed, float distance, float seconds)
    {
        float allowance = PitApproachDeceleration * seconds;
        return Mathf.Max(0f, Mathf.Sqrt(targetSpeed * targetSpeed + 2f * PitApproachDeceleration * Mathf.Max(0f, distance) + allowance * allowance) - allowance);
    }

    internal float PlannedSpeed(Car car, float freeSpeed, float stopAt, float seconds)
    {
        UpdateExitClearance(car, stopAt, seconds);
        if (car.InPit && car.CautionPitTailRequired)
        {
            float exit = car.BackExit ? backExit : ExitDistance;
            if (CautionTailExitClear?.Invoke(car, exit) == false)
            {
                float remaining = car.BackExit
                    ? CautionHoldAt(backExitLength) - car.ConnectorDistance
                    : CautionHoldAt(RouteLength) - car.PitDistance;
                freeSpeed = Mathf.Min(freeSpeed, ApproachLimit(0f, remaining, seconds));
            }
        }
        if (car.BackExit || !car.InPit && float.IsInfinity(car.NextEntry)) return freeSpeed;
        bool front = car.InPit ? car.FrontEntry : UsesFrontEntry(car);
        float from = car.InPit ? car.PitDistance : 0f;
        float beforeEntry = car.InPit ? 0f : Mathf.Max(0f, car.NextEntry - car.TrackDistance);
        float roadBegin = front ? frontJoin : RoadStart;
        float speed = StallEntryReservationSpeed(car, freeSpeed, seconds);
        if (InSingleFileApproach(car))
            speed = Mathf.Min(speed, speedLimit);
        if (car.WaitingForExitMerge)
            speed = Mathf.Min(speed, ApproachLimit(0f, Mathf.Max(0f, car.ExitHoldDistance - from), seconds));
        if (from < boundaries[1])
        {
            float limit = car.InPit && from >= boundaries[0] ? speedLimit :
                ApproachLimit(speedLimit, beforeEntry + PhysicalDistance(from, roadBegin), seconds);
            speed = Mathf.Min(speed, limit);
        }
        if (!float.IsInfinity(stopAt))
        {
            float distance = beforeEntry + PhysicalDistance(from, stopAt);
            float stopSpeed = ApproachLimit(0f, distance, seconds);
            // Alternate pit handoffs also need to reach the exact stop station at low speed.
            if (distance > 0f && distance < .001f && seconds > 0f &&
                car.FieldIndex >= 0 && stopAt != stalls[car.FieldIndex])
                stopSpeed = distance / seconds;
            speed = Mathf.Min(speed, stopSpeed);
        }
        return speed;
        float PhysicalDistance(float a, float b)
        {
            if (b <= a) return 0f;
            if (!front) return b - a;
            float scaled = Mathf.Max(0f, Mathf.Min(b, frontJoin) - Mathf.Min(a, frontJoin)) * frontEntryLength / frontJoin;
            return scaled + Mathf.Max(0f, b - Mathf.Max(a, frontJoin));
        }
    }

    private static float CautionHoldAt(float routeEnd) => Mathf.Max(0f, routeEnd - CautionExitSetback);

    private void UpdateExitClearance(Car car, float stopAt, float seconds)
    {
        car.WaitingForExitMerge = false;
        if (ExitGapClear == null || !car.InPit || car.BackExit || car.ExitMergeGranted) return;
        bool back = CanUseBackExit(car, stopAt);
        float hold = back ? backLeave : RoadEnd;
        if (stopAt <= hold || car.PitDistance > hold + .02f) return;
        car.ExitHoldDistance = hold;
        // Stall departures are handled by maneuver avoidance, not exit admission.
        if (car.Maneuver || ParkedInStall(car)) return;
        float speed = Mathf.Max(ActualSpeed(), speedLimit);
        // Inspect the complete rolling merge before braking would be necessary.
        float lookAhead = Mathf.Max(30f, speed * speed / (2f * PitApproachDeceleration) + speed * 1.5f);
        if (hold - car.PitDistance > lookAhead) return;
        car.WaitingForExitMerge = true;
        // Only the first car in the travel lane may reserve this exit.
        foreach (var other in AvoidanceField)
            if (SameExitQueue(car, other) &&
                !other.Maneuver && !ParkedInStall(other) &&
                (other.PitDistance > car.PitDistance ||
                 other.PitDistance == car.PitDistance && other.FieldIndex < car.FieldIndex)) { car.ExitBlockedBy = other.FieldIndex; car.ExitConflictSeconds = 0f; return; }
        car.ExitMergeRetrySeconds = Mathf.Max(0f, car.ExitMergeRetrySeconds - seconds);
        if (car.ExitMergeRetrySeconds > 0f) return;
        if (ExitCheckAvailable != null && !ExitCheckAvailable()) return;
        car.ExitMergeRetrySeconds = .25f;
        car.ExitBlockedBy = -1;
        car.ExitConflictSeconds = 0f;
        if (!ExitGapClear(car, back)) return;
        car.ExitMergeGranted = true;
        car.WaitingForExitMerge = false;

        float ActualSpeed() => CurrentSpeed != null ? CurrentSpeed(car) : car.ActualPitSpeed;
    }

    internal bool FollowsExitQueue(Car leader, Car other) => SameExitQueue(leader, other) &&
        (other.PitDistance < leader.PitDistance ||
         other.PitDistance == leader.PitDistance && other.FieldIndex > leader.FieldIndex);

    private bool SameExitQueue(Car a, Car b)
    {
        if (SameCar(a, b) || !b.InPit || b.InGarage || b.BackExit) return false;
        // Initial parked cars may not have an entry-route flag yet. Queue identity
        // comes from the exit they will actually use, not their entry history.
        bool aBack = CanUseBackExit(a, float.PositiveInfinity);
        bool bBack = CanUseBackExit(b, float.PositiveInfinity);
        if (aBack != bBack) return false;
        float hold = aBack ? backLeave : RoadEnd;
        return b.PitDistance <= hold + .02f;
    }

    internal const float StandardStallDepartureLength = 6f;
    private static readonly float[] ManeuverLengths = { StandardStallDepartureLength, 8f, 10f, 12f, 16f, 24f, 40f };

    // Tight stalls can use a rear-axle curve after the standard routes are rejected.
    private static readonly float[] CompactDepartureLengths = { StandardStallDepartureLength, 8f, 10f, 12f, 16f, 24f, 40f, 4.5f };

    internal float PreferredStallDepartureLane(int index, float station, float length) =>
        hasPassingLane && station + length < singleFileApproachStart && (index & 1) != 0 &&
        (!splitRoad || station <= backLeave)
            ? secondThroughLane : throughLane;
    // Prefer the existing turn when clear. A closer, slower turn can fit
    // between occupied neighboring stalls when the normal turn cannot.
    private static readonly float[] StallArrivalLengths = { 6f, 5f, 4.5f, 8f, 10f, 12f, 16f, 24f, 40f };

    private bool SamePitRoute(Car a, Car b) => !SameCar(a, b) && b.InPit && !b.InGarage && !b.BackExit &&
        !(a.FrontEntry != b.FrontEntry && Mathf.Min(a.PitDistance, b.PitDistance) < frontJoin);

    // Center-to-center clearance includes the leader's rotated rear corner,
    // the follower's nose, and a small maneuver margin (about 15 feet total).
    internal const float StallEntryRearClearance = 4.5f;

    private float StallEntryReservationSpeed(Car car, float speed, float seconds)
    {
        car.StallEntryHoldDistance = float.PositiveInfinity;
        car.StallEntryReservationOwner = -1;
        if (!car.InPit || car.BackExit || car.InGarage || field == null) return speed;
        float reservationSpeed = speed;
        foreach (var leader in AvoidanceField)
        {
            if (!SamePitRoute(car, leader) || !leader.Maneuver || !leader.ArrivalManeuver ||
                leader.PitDistance < car.PitDistance || leader.PitDistance > leader.ManeuverStart + leader.ManeuverLength + .01f) continue;
            // Release the reserved space only when the whole rotated car is clear
            // of the travel lane, not merely when its center has left that lane.
            float yaw = leader.PitYaw * Mathf.Deg2Rad;
            float halfWidth = 1.45f * Mathf.Abs(Mathf.Sin(yaw)) + .9f * Mathf.Abs(Mathf.Cos(yaw));
            float lane = TravelLaneAt(car.PitDistance, car.SecondTravelLane);
            if (Mathf.Abs(leader.PitLane - lane) > halfWidth + .9f + .25f) continue;
            if (ParkedInStall(car) || Mathf.Abs(car.PitLane - lane) > 2.1f) continue;
            // Follow a moving rear-clearance boundary. A turning car that is
            // still moving does not impose an artificial stop before the stall.
            float hold = leader.PitDistance - StallEntryRearClearance;
            if (hold < car.StallEntryHoldDistance)
            {
                car.StallEntryHoldDistance = hold;
                car.StallEntryReservationOwner = leader.FieldIndex;
                float stopDistance = Mathf.Max(0f, leader.ManeuverStart + leader.ManeuverLength - leader.PitDistance);
                reservationSpeed = Mathf.Min(Mathf.Max(0f, leader.ActualPitSpeed), ApproachLimit(0f, stopDistance, seconds));
            }
        }
        if (float.IsInfinity(car.StallEntryHoldDistance)) return speed;
        float from = car.PitDistance, to = Mathf.Max(from, car.StallEntryHoldDistance);
        float distance = to - from;
        if (car.FrontEntry && from < frontJoin)
            distance = (Mathf.Min(to, frontJoin) - from) * frontEntryLength / frontJoin + Mathf.Max(0f, to - frontJoin);
        return Mathf.Min(speed, ApproachLimit(reservationSpeed, distance, seconds));
    }

    private bool ParkedInStall(Car car) =>
        car.InPit && car.FieldIndex >= 0 && !car.Maneuver &&
        Mathf.Abs(car.PitDistance - stalls[car.FieldIndex]) < .05f &&
        Mathf.Abs(car.PitLane - stallLanes[car.FieldIndex]) < .1f;

    private static bool PullingOut(Car car) => car.Maneuver && !car.ArrivalManeuver &&
        (Mathf.Abs(car.PitLane - car.ManeuverTo) > .25f || Mathf.Abs(car.PitYaw) > 8f);

    private bool DepartureWindowClear(Car car, float length, float targetLane)
    {
        // A pull-out is a reservation, not just permission for its first step.
        // Allow time to accelerate from rest, finish the turn and straighten.
        float horizon = length / PullOutSpeed + 1f;
        float low = Mathf.Min(car.PitLane, targetLane) - StallLaneClearance;
        float high = Mathf.Max(car.PitLane, targetLane) + StallLaneClearance;
        // Departures accepted earlier this step already own their swept corridor.
        foreach (var other in field)
        {
            if (!SamePitRoute(car, other) || ParkedInStall(other)) continue;
            if (other.Maneuver)
            {
                float otherEnd = other.ManeuverStart + other.ManeuverLength;
                if (otherEnd + 4f >= car.PitDistance && other.ManeuverStart - 4f <= car.PitDistance + length &&
                    Mathf.Min(other.ManeuverFrom, other.ManeuverTo) <= high &&
                    Mathf.Max(other.ManeuverFrom, other.ManeuverTo) >= low)
                    return BlockDeparture(car, other, "Reserved maneuver corridor");
            }
            if (other.PitLane < low || other.PitLane > high) continue;
            float gap = other.PitDistance - car.PitDistance;
            float approachSpeed = Mathf.Max(speedLimit, SpeedAtStepStart(other));
            if (gap >= -approachSpeed * horizon - 4f && gap <= length + 4f)
                return BlockDeparture(car, other, "Approaching pit traffic");
        }
        return true;
    }

    private static bool BlockDeparture(Car car, Car other, string reason)
    {
        car.DepartureBlockedByIndex = other.FieldIndex;
        car.DepartureBlockReason = reason;
        return false;
    }

    private bool ClearManeuver(Car car, float start, float length, float from, float to, bool arrival)
    {
        float previousLane = from, previousDistance = car.PitDistance, yaw = car.PitYaw;
        bool tightTurn = arrival || length < StandardStallDepartureLength;
        int samples = Mathf.CeilToInt((start + length - previousDistance) / (tightTurn ? .05f : .2f));
        float begin = previousDistance;
        for (int sample = 1; sample <= samples; sample++)
        {
            float distance = Mathf.Lerp(begin, start + length, sample / (float)samples);
            float lane = ManeuverLane(distance, start, length, from, to, arrival);
            yaw = RearAxleHeading.Advance(yaw, new Vector3(lane - previousLane, 0f, distance - previousDistance), RearAxleHeading.CenterToRearAxle);
            // Route planning considers stationary neighbors. Moving traffic is
            // checked at each live step, so a passing car does not cancel the route.
            if (!ClearPose(car, distance, lane, yaw, 0f, parkedOnly: true) ||
                (arrival && !ReservedStallClear(car, distance, lane, yaw)))
                return false;
            previousLane = lane; previousDistance = distance;
        }
        return !arrival || Mathf.Abs(Mathf.DeltaAngle(yaw, 0f)) <= 2f;
    }

    private bool ReservedStallClear(Car car, float distance, float lane, float yaw)
    {
        // Use the live field so an arrival accepted earlier in this step
        // already reserves its final stall footprint.
        foreach (var other in field)
        {
            if (!SamePitRoute(car, other) || !other.Maneuver || !other.ArrivalManeuver) continue;
            float stop = other.ManeuverStart + other.ManeuverLength;
            float target = other.ManeuverTo;
            if (Mathf.Abs(stop - distance) > 5f || Mathf.Abs(target - lane) > 5f) continue;
            if (RoutePosition == null)
            {
                if (PitFootprint.Overlaps(stop - distance, target - lane, yaw, 0f, .08f))
                    return BlockDeparture(car, other, "Reserved stall footprint");
                continue;
            }

            Vector3 delta = RoutePosition(other, stop, target) - RoutePosition(car, distance, lane);
            if (Mathf.Abs(delta.y) >= 1.5f) continue;
            Vector3 tangent = RoutePosition(car, distance + .05f, lane) - RoutePosition(car, distance - .05f, lane);
            Vector3 parkedTangent = RoutePosition(other, stop + .05f, target) - RoutePosition(other, stop - .05f, target);
            float heading = Mathf.Atan2(tangent.x, tangent.z) * Mathf.Rad2Deg + yaw;
            float parkedHeading = Mathf.Atan2(parkedTangent.x, parkedTangent.z) * Mathf.Rad2Deg;
            if (PitFootprint.Overlaps(delta.z, delta.x, heading, parkedHeading, .12f))
                return BlockDeparture(car, other, "Reserved stall footprint");
        }
        return true;
    }

    private bool ClearPose(Car car, float distance, float lane, float yaw, float aheadSeconds, bool parkedOnly = false,
        (float Distance, float Lane, float Yaw, float Seconds)? previousSweep = null)
    {
        if (RoutePosition != null) return ClearPhysicalPose(car, distance, lane, yaw, aheadSeconds, parkedOnly);
        aheadSeconds = parkedOnly ? 0f : aheadSeconds + predictionOffset;
        foreach (var other in AvoidanceField)
        {
            if (SameCar(other, car) || other.InGarage || !other.InPit || other.BackExit ||
                (other.FrontEntry != car.FrontEntry && Mathf.Min(other.PitDistance, car.PitDistance) < frontJoin) ||
                parkedOnly && other.ActualPitSpeed > .05f) continue;
            float gap = other.PitDistance + other.ActualPitSpeed * aheadSeconds - distance;
            if (Mathf.Abs(gap) > 4f || Mathf.Abs(other.PitLane - lane) > 4f) continue;
            if (PitFootprint.Overlaps(gap, other.PitLane - lane, yaw, other.PitYaw, .08f))
            {
                // A clearance margin is not physical contact. Permit a car to
                // leave that margin when each proposed step increases separation.
                // This prevents a stopped, angled neighbor pinning a through car.
                float oldGap = other.PitDistance - car.PitDistance;
                float oldSide = other.PitLane - car.PitLane;
                bool clearBodies = !PitFootprint.Overlaps(oldGap, oldSide, car.PitYaw, other.PitYaw) &&
                    !PitFootprint.Overlaps(gap, other.PitLane - lane, yaw, other.PitYaw);
                float before = PitFootprint.Clearance(oldGap, oldSide, car.PitYaw, other.PitYaw, .08f);
                float after = PitFootprint.Clearance(gap, other.PitLane - lane, yaw, other.PitYaw, .08f);
                // A stopped, already overlapping queue can separate in travel order.
                // Keep the lead car in its travel lane and require each swept sample to reduce rear contact.
                if (recoverStoppedQueueOverlap && !parkedOnly && previousSweep.HasValue &&
                    StoppedQueueOverlapBehind(car, other) && Mathf.Abs(lane - car.PitLane) <= .02f &&
                    gap < oldGap && after > before + .00001f)
                {
                    var previous = previousSweep.Value;
                    float previousGap = other.PitDistance + other.ActualPitSpeed * (previous.Seconds + predictionOffset) - previous.Distance;
                    float previousSide = other.PitLane - previous.Lane;
                    float previousClearance = PitFootprint.Clearance(previousGap, previousSide, previous.Yaw, other.PitYaw, .08f);
                    if (gap < previousGap && after > previousClearance + .00001f) continue;
                }
                float nextSide = other.PitLane - lane;
                bool movingApart = gap * gap + nextSide * nextSide > oldGap * oldGap + oldSide * oldSide + .0000001f;
                // The limiting separating axis can stay constant while a car
                // drives away along another axis. Require growing center distance
                // and a non-worsening margin, not strict growth on every axis.
                if (parkedOnly || !clearBodies || before >= 0f || !movingApart || after < before - .00001f)
                    return parkedOnly ? BlockDeparture(car, other, "Parked neighbor footprint") : BlockMovement(car, other, "Route footprint clearance", after, aheadSeconds);
            }
        }
        return true;
    }

    private bool StoppedQueueOverlapBehind(Car car, Car other)
    {
        float gap = other.PitDistance - car.PitDistance;
        if (!SamePitRoute(car, other) || car.Maneuver || (other.Maneuver && !other.ArrivalManeuver) ||
            other.PitDistance < RoadStart || gap >= 0f || gap < -4f ||
            car.ActualPitSpeed > QueueSeparationSpeed + .05f || other.ActualPitSpeed > .05f)
            return false;

        float side = other.PitLane - car.PitLane;
        if (car.PitDistance > RoadEnd)
        {
            // Freeze the lane briefly through the exit convergence. Moving
            // laterally first can turn a near-touch into a swept contact.
            return other.PitDistance >= RoadEnd && !other.Maneuver &&
                PitFootprint.Overlaps(gap, side, car.PitYaw, other.PitYaw, .10f);
        }

        float travelLane = TravelLaneAt(car.PitDistance, car.SecondTravelLane);
        return Mathf.Abs(car.PitLane - travelLane) <= .075f &&
            Mathf.Abs(other.PitLane - travelLane) <= .075f &&
            Mathf.Abs(Mathf.DeltaAngle(car.PitYaw, 0f)) <= 3f &&
            Mathf.Abs(Mathf.DeltaAngle(other.PitYaw, 0f)) <= 3f &&
            PitFootprint.Overlaps(gap, side, car.PitYaw, other.PitYaw, .08f);
    }

    private bool ClearPhysicalPose(Car car, float distance, float lane, float localYaw, float seconds, bool parkedOnly)
    {
        seconds = parkedOnly ? 0f : seconds + predictionOffset;
        Vector3 before = PositionAtStepStart(car), next = RoutePosition(car, distance, lane);
        float heading = HeadingAtStepStart(car);
        float nextHeading;
        if (parkedOnly)
        {
            Vector3 tangent = RoutePosition(car, distance + .05f, lane) - RoutePosition(car, distance - .05f, lane);
            nextHeading = Mathf.Atan2(tangent.x, tangent.z) * Mathf.Rad2Deg + localYaw;
        }
        else nextHeading = RearAxleHeading.Advance(heading, next - before, RearAxleHeading.CenterToRearAxle);
        foreach (var other in AvoidanceField)
        {
            if (SameCar(other, car) || other.InGarage || parkedOnly && (!other.InPit || SpeedAtStepStart(other) > .05f)) continue;
            Vector3 otherBefore = PositionAtStepStart(other);
            float reach = 4f + SpeedAtStepStart(other) * seconds;
            if ((otherBefore - next).sqrMagnitude > reach * reach) continue;
            Vector3 otherNext = PredictFromStepStart(other, seconds);
            Vector3 delta = otherNext - next;
            if (Mathf.Abs(delta.y) >= 1.5f) continue;
            float otherHeading = HeadingAtStepStart(other);
            float otherNextHeading = RearAxleHeading.Advance(otherHeading, otherNext - otherBefore, RearAxleHeading.CenterToRearAxle);
            if (!PitFootprint.Overlaps(delta.z, delta.x, nextHeading, otherNextHeading, .12f)) continue;
            Vector3 old = otherBefore - before;
            bool clearBodies = !PitFootprint.Overlaps(old.z, old.x, heading, otherHeading) &&
                !PitFootprint.Overlaps(delta.z, delta.x, nextHeading, otherNextHeading);
            float oldClearance = PitFootprint.Clearance(old.z, old.x, heading, otherHeading, .12f);
            float newClearance = PitFootprint.Clearance(delta.z, delta.x, nextHeading, otherNextHeading, .12f);
            if (parkedOnly || !clearBodies || oldClearance >= 0f || delta.sqrMagnitude <= old.sqrMagnitude + .0000001f ||
                newClearance < oldClearance - .00001f)
                return parkedOnly ? BlockDeparture(car, other, "Parked neighbor footprint") : BlockMovement(car, other, "Physical footprint clearance", newClearance, seconds);
        }
        return true;
    }

    public void Advance(Car car, float seconds, float trackSpeed, float stopAt = float.PositiveInfinity)
    {
        float before = car.PitDistance, lane = car.PitLane;
        bool wasInPit = car.InPit;
        UpdateExitClearance(car, stopAt, 0f);
        AdvanceCore(car, seconds, trackSpeed, stopAt);
        predictionOffset = 0f;
        if (wasInPit && car.InPit && !car.BackExit)
        {
            car.PitYaw = RearAxleHeading.Advance(car.PitYaw,
                new Vector3(car.PitLane - lane, 0f, car.PitDistance - before), RearAxleHeading.CenterToRearAxle);
            car.ActualPitSpeed = seconds > 0f ? Mathf.Max(0f, car.PitDistance - before) / seconds : 0f;
        }
        else { car.PitYaw = 0f; car.ActualPitSpeed = 0f; }
    }

    private void AdvanceCore(Car car, float seconds, float trackSpeed, float stopAt)
    {
        float duration = seconds;
        while (seconds > 0f && trackSpeed > 0f)
        {
            // A step may cross a route boundary; predictions still start at t=0.
            predictionOffset = duration - seconds;
            if (!car.InPit)
            {
                float toEntry = Mathf.Max(0f, car.NextEntry - car.TrackDistance);
                if (toEntry > trackSpeed * seconds)
                {
                    car.Odometer += trackSpeed * seconds;
                    car.PreciseTrackDistance += (double)trackSpeed * seconds;
                    return;
                }

                float entryTime = toEntry / trackSpeed;
                // Check the live field before entering the connector. The step
                // snapshot cannot see a car admitted earlier in this same step.
                if (!EntranceClear(car) || EntryReady?.Invoke(car, (predictionOffset + entryTime) / duration) == false)
                {
                    DeferEntry(car);
                    continue;
                }

                seconds -= entryTime;
                car.Odometer += toEntry;
                car.TrackDistance = car.NextEntry;
                car.NextEntry = float.PositiveInfinity;
                car.DeferredEntry = float.NegativeInfinity;
                car.ApronEntryActive = false;
                car.PitDistance = 0f;
                car.PitLane = 0f;
                car.Maneuver = false;
                car.ExitMergeGranted = false;
                car.ExitMergeRetrySeconds = 0f;
                car.WaitingForExitMerge = false;
                car.ApproachingStall = false;
                car.SecondTravelLane = false;
                car.DepartureStart = float.NegativeInfinity;
                car.FrontEntry = UsesFrontEntry(car);
                car.BackExit = false;
                car.ConnectorDistance = 0f;
                car.CautionPitTailRequired = false;
                car.InPit = true;
                continue;
            }

            if (car.BackExit)
            {
                bool waitForTail = car.CautionPitTailRequired && CautionTailExitClear?.Invoke(car, backExit) == false;
                float stop = waitForTail ? CautionHoldAt(backExitLength) : backExitLength;
                float travel = Mathf.Min(Mathf.Max(0f, stop - car.ConnectorDistance), seconds * trackSpeed);
                foreach (var other in AvoidanceField)
                {
                    if (other.InGarage || !other.InPit || !other.BackExit || SameCar(other, car))
                        continue;
                    float gap = other.ConnectorDistance - car.ConnectorDistance;
                    if (gap > 0f || (gap == 0f && other.FieldIndex < car.FieldIndex))
                        travel = Mathf.Min(travel, Mathf.Max(0f, gap - 3.4f));
                }
                float exitLane = car.ConnectorEntryLane * (1f - Mathf.SmoothStep(0f, 1f, (car.ConnectorDistance + travel) / backExitLength));
                float nextLane = Mathf.MoveTowards(car.PitLane, exitLane, 3f * seconds);
                if (RoutePosition != null)
                {
                    for (int sample = 1; sample <= 8; sample++)
                    {
                        float t = sample / 8f;
                        if (!ClearPhysicalPose(car, car.ConnectorDistance + travel * t,
                            Mathf.Lerp(car.PitLane, nextLane, t), car.PitYaw, seconds * t, false)) return;
                    }
                }
                car.PitLane = nextLane;
                car.ConnectorDistance += travel;
                car.Odometer += travel;
                if (car.ConnectorDistance < backExitLength || waitForTail)
                    return;
                seconds -= travel / trackSpeed;
                car.BackExit = false;
                car.InPit = false;
                car.PreciseTrackDistance += Mathf.Repeat(backExit - entryDistance, lapLength);
                car.LastExit = car.TrackDistance;
                continue;
            }

            int segment = car.PitDistance < boundaries[0] ? 0 : car.PitDistance < boundaries[1] ? 1 : 2;
            float speed = segment == 1 ? Mathf.Min(trackSpeed, speedLimit) : trackSpeed;
            speed = TrafficSpeed(car, seconds, speed, stopAt);
            if (car.BypassingBlockedStall) stopAt = float.PositiveInfinity;
            bool stopInSegment = stopAt >= car.PitDistance && stopAt <= boundaries[segment];
            float target = stopInSegment ? stopAt : boundaries[segment];
            bool takeBackExit = CanUseBackExit(car, stopAt) && backLeave <= target;
            if (takeBackExit)
            {
                target = backLeave;
                foreach (var other in AvoidanceField)
                {
                    if (!other.InGarage && other.InPit && other.BackExit)
                        target = Mathf.Min(target, Mathf.Max(car.PitDistance, backLeave + other.ConnectorDistance - 3.4f));
                }
                takeBackExit = target == backLeave;
            }
            bool waitingForBackExit = !takeBackExit && target < (stopInSegment ? stopAt : boundaries[segment]);
            bool waitingForMerge = car.WaitingForExitMerge && car.ExitHoldDistance <= target;
            if (waitingForMerge) target = car.ExitHoldDistance;
            bool waitingForCautionTail = segment == 2 && car.CautionPitTailRequired &&
                target >= CautionHoldAt(RouteLength) && CautionTailExitClear?.Invoke(car, ExitDistance) == false;
            if (waitingForCautionTail) target = Mathf.Max(car.PitDistance, CautionHoldAt(RouteLength));
            float distanceScale = car.FrontEntry && car.PitDistance < frontJoin
                ? frontEntryLength / frontJoin : 1f;
            if (distanceScale != 1f && target > frontJoin)
                target = frontJoin;
            float remaining = (target - car.PitDistance) * distanceScale;
            if (remaining > speed * seconds)
            {
                car.Odometer += speed * seconds;
                car.PitDistance += speed * seconds / distanceScale;
                return;
            }

            if (speed <= 0f)
                return;
            seconds -= remaining / speed;
            car.Odometer += remaining;
            car.PitDistance = target;
            if (waitingForBackExit || waitingForMerge || waitingForCautionTail)
                return;
            if (takeBackExit)
            {
                car.BackExit = true;
                car.ConnectorDistance = 0f;
                car.ConnectorEntryLane = car.PitLane;
                continue;
            }
            if (target < (stopInSegment ? stopAt : boundaries[segment]))
                continue;
            if (stopInSegment)
                return;

            if (segment == 2)
            {
                if (car.CautionPitTailRequired && CautionTailExitClear?.Invoke(car, ExitDistance) == false)
                    return;
                car.InPit = false;
                car.PreciseTrackDistance += car.FrontEntry ? Mathf.Repeat(ExitDistance - frontEntry, lapLength) : bypassDistance;
                car.LastExit = car.TrackDistance;
            }
        }
    }
}


internal static class PitFootprint
{
    // Separating-axis test for the same 2.9 x 1.8 m car used by collision detection.
    internal static bool Overlaps(float gap, float side, float yawA, float yawB, float margin = 0f) =>
        Clearance(gap, side, yawA, yawB, margin) < 0f;

    internal static float Clearance(float gap, float side, float yawA, float yawB, float margin = 0f)
    {
        float a = yawA * Mathf.Deg2Rad, b = yawB * Mathf.Deg2Rad;
        Vector2 fa = new Vector2(Mathf.Sin(a), Mathf.Cos(a)), ra = new Vector2(fa.y, -fa.x);
        Vector2 fb = new Vector2(Mathf.Sin(b), Mathf.Cos(b)), rb = new Vector2(fb.y, -fb.x);
        Vector2 delta = new Vector2(side, gap);
        return Mathf.Max(Mathf.Max(Axis(fa), Axis(ra)), Mathf.Max(Axis(fb), Axis(rb)));
        float Axis(Vector2 axis)
        {
            float extent = (1.45f + margin) * (Mathf.Abs(Vector2.Dot(fa, axis)) + Mathf.Abs(Vector2.Dot(fb, axis))) +
                (.9f + margin) * (Mathf.Abs(Vector2.Dot(ra, axis)) + Mathf.Abs(Vector2.Dot(rb, axis)));
            return Mathf.Abs(Vector2.Dot(delta, axis)) - extent;
        }
    }
}
