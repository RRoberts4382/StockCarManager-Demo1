using UnityEngine;

internal sealed class TrackPitTraffic
{
    private const float MphToMetersPerSecond = 0.44704f;
    private const float StallLaneClearance = 2.2f;
    private const float QueueSeparationSpeed = .75f;
    private readonly float lapLength;
    private readonly float entryDistance;
    private readonly float bypassDistance;
    private readonly float speedLimit;
    private readonly float[] boundaries;
    private readonly float laneOffset;
    private readonly bool singleThroughLane;
    private readonly bool recoverStoppedQueueOverlap;
    private float throughLane;
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
        if (!stepActive || RoutePosition == null) return;
        for (int i = 0; i < stepField.Length; i++)
        {
            plannedStepSpeeds[i] = Mathf.Max(0f, speeds[i]);
            if (stepField[i].Maneuver && !stepField[i].ArrivalManeuver)
                plannedStepSpeeds[i] = Mathf.Min(4f, plannedStepSpeeds[i]);
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
                    if (i == j || stepField[j].InGarage) continue;
                    Vector3 delta = stepPositions[j] - stepPositions[i];
                    float gap = Vector3.Dot(delta, forward);
                    if (gap <= 0f || gap > 100f || Mathf.Abs(delta.y) > 1.5f || Mathf.Abs(Vector3.Dot(delta, across)) > 2.1f) continue;
                    float aligned = Mathf.Cos(Mathf.DeltaAngle(stepHeadings[i], stepHeadings[j]) * Mathf.Deg2Rad);
                    if (aligned < .5f) continue;
                    float leaderSpeed = plannedStepSpeeds[j] * aligned;
                    // 3.8 m center spacing leaves room outside the physical guard's
                    // 3.14 m padded footprint, including curved-connector compression.
                    target = Mathf.Min(target, ApproachLimit(leaderSpeed, Mathf.Max(0f, gap - 3.8f), seconds));
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
    internal System.Func<Car, bool, bool> ExitGapClear;
    internal System.Func<bool> ExitCheckAvailable;
    internal const float PitApproachDeceleration = 6f;

    public float RoadStart { get; }
    public float RoadEnd { get; }
    public float RoadLength => RoadEnd - RoadStart;
    public float ParkingDistance => RoadEnd - Mathf.Min(4f, RoadLength * 0.1f);
    public float RouteLength => boundaries[2];
    public float SpeedLimit => speedLimit;

    internal float ThroughLaneAt(float distance)
    {
        float blend = distance < RoadStart
            ? Mathf.SmoothStep(0f, 1f, distance / Mathf.Min(20f, RoadStart))
            : distance > RoadEnd
                ? Mathf.SmoothStep(0f, 1f, (RouteLength - distance) / Mathf.Min(20f, RouteLength - RoadEnd)) : 1f;
        return throughLane * blend;
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
        internal bool ApproachingStall;
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
        bool recoverStoppedQueueOverlap = false)
    {
        this.lapLength = lapLength;
        this.singleThroughLane = singleThroughLane;
        this.recoverStoppedQueueOverlap = recoverStoppedQueueOverlap;
        this.entryDistance = entryDistance;
        bypassDistance = Mathf.Repeat(exitDistance - entryDistance, lapLength);
        speedLimit = road.speedLimitMph * MphToMetersPerSecond;
        laneOffset = road.widthMeters * 0.25f;
        RoadStart = entryLength;
        RoadEnd = entryLength + roadLength;
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

    internal bool CooldownEntranceClear(Car car, float clearance = 5.5f)
    {
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
        hasPassingLane = !singleThroughLane && Mathf.Min(outsideClearance, insideClearance) >= StallLaneClearance;
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
        if (!arrival) return Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, (distance - start) / length));
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
        bool arrival = stopAt == stalls[index];
        bool departure = !arrival && Mathf.Abs(car.PitDistance - stalls[index]) < .001f;
        if (car.Maneuver && (car.ArrivalManeuver != arrival ||
            car.PitDistance > car.ManeuverStart + car.ManeuverLength + .01f)) car.Maneuver = false;
        car.ManeuverRetrySeconds = Mathf.Max(0f, car.ManeuverRetrySeconds - seconds);
        if (!car.Maneuver && (departure || arrival && stopAt - car.PitDistance <= 40f))
        {
            if (car.ManeuverRetrySeconds > 0f) return 0f;
            car.DepartureBlockedByIndex = -1;
            car.DepartureBlockReason = null;
            // Choose the shortest clear rear-axle-compatible route. Long routes
            // can intersect the occupied stall behind.
            foreach (float length in ManeuverLengths)
            {
                float start = arrival ? stopAt - length : car.PitDistance;
                if (start < car.PitDistance - .01f) continue;
                float to = arrival ? stallLanes[index] : throughLane;
                if (!arrival && !DepartureWindowClear(car, length)) continue;
                if (!ClearManeuver(car, start, length, car.PitLane, to, arrival)) continue;
                car.WaitingForStallDeparture = false;
                car.DepartureBlockedByIndex = -1;
                car.DepartureBlockReason = null;
                car.Maneuver = true; car.ArrivalManeuver = arrival;
                car.ManeuverStart = start; car.ManeuverLength = length;
                car.ManeuverFrom = car.PitLane; car.ManeuverTo = to;
                break;
            }
            if (!car.Maneuver) { car.WaitingForStallDeparture = departure; car.ManeuverRetrySeconds = .25f; return 0f; }
        }
        if (car.Maneuver && !car.ArrivalManeuver)
            speed = Mathf.Min(speed, ApproachLimit(4f, Mathf.Max(0f, car.ManeuverStart - car.PitDistance), seconds));
        if (arrival) speed = Mathf.Min(speed, ApproachLimit(0f, Mathf.Max(0f, stopAt - car.PitDistance), seconds));
        float laneVariation = 0f;
        if (!car.Maneuver && !car.WaitingForExitMerge && (!arrival || stopAt - car.PitDistance > 40f))
        {
            // Fade before the connectors and vary below the posted limit.
            float room = Mathf.Min(car.PitDistance - Mathf.Max(RoadStart, boundaries[0]),
                Mathf.Min(RoadEnd, boundaries[1]) - car.PitDistance);
            float blend = Mathf.SmoothStep(0f, 1f, room / 12f);
            laneVariation = blend * TrackState001RaceStart.PaceSpeedVariation(index, stepTime, .08f);
            float speedVariation = TrackState001RaceStart.PaceSpeedVariation(index, stepTime + 3f, .0125f);
            speed *= 1f - blend * (.0125f + speedVariation);
        }
        speed = StallEntryReservationSpeed(car, speed, seconds);
        if (car.WaitingForExitMerge)
            speed = Mathf.Min(speed, ApproachLimit(0f, Mathf.Max(0f, car.ExitHoldDistance - car.PitDistance), seconds));
        float parameterScale = car.FrontEntry && car.PitDistance < frontJoin ? frontEntryLength / frontJoin : 1f;
        if (arrival && stopAt - car.PitDistance < .001f) speed = (stopAt - car.PitDistance) * parameterScale / seconds;
        if (recoverStoppedQueueOverlap && speed > QueueSeparationSpeed)
            foreach (var other in AvoidanceField)
                if (StoppedQueueOverlapBehind(car, other))
                {
                    speed = QueueSeparationSpeed;
                    break;
                }
        float travel = Mathf.Min(speed * seconds / parameterScale, Mathf.Max(0f, stopAt - car.PitDistance));
        float nextLane;
        if (car.Maneuver)
            nextLane = ManeuverLane(car.PitDistance + travel, car.ManeuverStart, car.ManeuverLength,
                car.ManeuverFrom, car.ManeuverTo, car.ArrivalManeuver);
        else
        {
            nextLane = Mathf.MoveTowards(car.PitLane, ThroughLaneAt(car.PitDistance) + laneVariation, 3f * seconds);
        }
        if (travel <= 0f) return 0f;
        // Test the complete step, including corner sweep and traffic approaching
        // from behind. A blocked maneuver waits without sliding sideways in place.
        var previousSweep = (Distance: car.PitDistance, Yaw: car.PitYaw, Seconds: 0f);
        for (int sample = 1; sample <= 8; sample++)
        {
            float t = sample / 8f;
            float lane = Mathf.Lerp(car.PitLane, nextLane, t);
            float yaw = RearAxleHeading.Advance(car.PitYaw,
                new Vector3(lane - car.PitLane, 0f, travel * t), RearAxleHeading.CenterToRearAxle);
            float distance = car.PitDistance + travel * t;
            float sampleSeconds = seconds * t;
            if (!ClearPose(car, distance, lane, yaw, sampleSeconds, previousSweep: previousSweep)) return 0f;
            previousSweep = (distance, yaw, sampleSeconds);
        }
        car.PitLane = nextLane;
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
        if (car.BackExit || !car.InPit && float.IsInfinity(car.NextEntry)) return freeSpeed;
        bool front = car.InPit ? car.FrontEntry : UsesFrontEntry(car);
        float from = car.InPit ? car.PitDistance : 0f;
        float beforeEntry = car.InPit ? 0f : Mathf.Max(0f, car.NextEntry - car.TrackDistance);
        float roadBegin = front ? frontJoin : RoadStart;
        float speed = StallEntryReservationSpeed(car, freeSpeed, seconds);
        if (car.WaitingForExitMerge)
            speed = Mathf.Min(speed, ApproachLimit(0f, Mathf.Max(0f, car.ExitHoldDistance - from), seconds));
        if (from < boundaries[1]) speed = Mathf.Min(speed, ApproachLimit(speedLimit, beforeEntry + PhysicalDistance(from, roadBegin), seconds));
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

    private void UpdateExitClearance(Car car, float stopAt, float seconds)
    {
        car.WaitingForExitMerge = false;
        if (ExitGapClear == null || !car.InPit || car.BackExit || car.ExitMergeGranted) return;
        bool back = CanUseBackExit(car, stopAt);
        float hold = back ? backLeave : RoadEnd;
        if (stopAt <= hold || car.PitDistance > hold + .02f) return;
        car.ExitHoldDistance = hold;
        // Stall departures are handled by maneuver avoidance, not exit admission.
        if (car.Maneuver || Mathf.Abs(car.PitLane - throughLane) > 1f) return;
        float speed = Mathf.Max(ActualSpeed(), speedLimit);
        // Inspect the complete rolling merge before braking would be necessary.
        float lookAhead = Mathf.Max(30f, speed * speed / (2f * PitApproachDeceleration) + speed * 1.5f);
        if (hold - car.PitDistance > lookAhead) return;
        car.WaitingForExitMerge = true;
        // Only the first car in the travel lane may reserve this exit.
        foreach (var other in AvoidanceField)
            if (SameExitQueue(car, other) &&
                !other.Maneuver && Mathf.Abs(other.PitLane - throughLane) <= 1f &&
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

    private static readonly float[] ManeuverLengths = { 6f, 8f, 10f, 12f, 16f, 24f, 40f };

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
            if (Mathf.Abs(leader.PitLane - throughLane) > halfWidth + .9f + .25f) continue;
            // Cars already in their own stalls are not part of the following queue.
            if (Mathf.Abs(car.PitLane - throughLane) > 2.1f) continue;
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

    private bool DepartureWindowClear(Car car, float length)
    {
        // A pull-out is a reservation, not just permission for its first step.
        // Allow time to accelerate from rest, finish the turn and straighten.
        float horizon = length / 4f + 2f;
        foreach (var other in AvoidanceField)
        {
            if (!SamePitRoute(car, other)) continue;
            if (other.Maneuver)
            {
                float otherEnd = other.ManeuverStart + other.ManeuverLength;
                if (otherEnd + 4f >= car.PitDistance &&
                    other.ManeuverStart - 4f <= car.PitDistance + length) return BlockDeparture(car, other, "Reserved maneuver corridor");
            }
            bool parked = other.FieldIndex >= 0 && !other.Maneuver &&
                Mathf.Abs(other.PitDistance - stalls[other.FieldIndex]) < .05f &&
                Mathf.Abs(other.PitLane - stallLanes[other.FieldIndex]) < .1f;
            if (parked) continue; // Static neighbors are checked by the swept path planner.
            float gap = other.PitDistance - car.PitDistance;
            // A distant stopped queue is not approaching traffic. Reserve against
            // actual closing motion, while retaining the immediate maneuver corridor.
            float approachSpeed = Mathf.Max(0f, SpeedAtStepStart(other));
            if (gap < -4f && approachSpeed < .25f) continue;
            if (gap >= -approachSpeed * horizon - 4f && gap <= length + 6f) return BlockDeparture(car, other, "Approaching pit traffic");
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
        int samples = Mathf.CeilToInt((start + length - previousDistance) / .2f);
        float begin = previousDistance;
        for (int sample = 1; sample <= samples; sample++)
        {
            float distance = Mathf.Lerp(begin, start + length, sample / (float)samples);
            float lane = ManeuverLane(distance, start, length, from, to, arrival);
            yaw = RearAxleHeading.Advance(yaw, new Vector3(lane - previousLane, 0f, distance - previousDistance), RearAxleHeading.CenterToRearAxle);
            // Route planning considers stationary neighbors. Moving traffic is
            // checked at each live step, so a passing car does not cancel the route.
            if (!ClearPose(car, distance, lane, yaw, 0f, parkedOnly: true)) return false;
            previousLane = lane; previousDistance = distance;
        }
        return !arrival || Mathf.Abs(Mathf.DeltaAngle(yaw, 0f)) <= 2f;
    }

    private bool ClearPose(Car car, float distance, float lane, float yaw, float aheadSeconds, bool parkedOnly = false,
        (float Distance, float Yaw, float Seconds)? previousSweep = null)
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
                // Every swept sample must move straight and reduce this rear contact.
                if (recoverStoppedQueueOverlap && !parkedOnly && previousSweep.HasValue &&
                    StoppedQueueOverlapBehind(car, other) && lane == car.PitLane && gap < oldGap && after > before + .00001f)
                {
                    var previous = previousSweep.Value;
                    float previousGap = other.PitDistance + other.ActualPitSpeed * (previous.Seconds + predictionOffset) - previous.Distance;
                    float previousClearance = PitFootprint.Clearance(previousGap, oldSide, previous.Yaw, other.PitYaw, .08f);
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
        if (!SamePitRoute(car, other) || car.Maneuver || other.Maneuver ||
            other.PitDistance < RoadStart || car.PitDistance > RoadEnd || other.PitDistance >= car.PitDistance ||
            car.ActualPitSpeed > QueueSeparationSpeed + .05f || other.ActualPitSpeed > .05f ||
            car.PitLane != throughLane || Mathf.Abs(other.PitLane - throughLane) > .05f ||
            Mathf.Abs(Mathf.DeltaAngle(car.PitYaw, 0f)) > 3f || Mathf.Abs(Mathf.DeltaAngle(other.PitYaw, 0f)) > 3f)
            return false;

        return PitFootprint.Overlaps(other.PitDistance - car.PitDistance, other.PitLane - car.PitLane,
            car.PitYaw, other.PitYaw);
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
                if (EntryReady?.Invoke(car, (predictionOffset + entryTime) / duration) == false)
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
                car.DepartureStart = float.NegativeInfinity;
                car.FrontEntry = UsesFrontEntry(car);
                car.BackExit = false;
                car.ConnectorDistance = 0f;
                car.InPit = true;
                continue;
            }

            if (car.BackExit)
            {
                float travel = Mathf.Min(backExitLength - car.ConnectorDistance, seconds * trackSpeed);
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
                if (car.ConnectorDistance < backExitLength)
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
            if (waitingForBackExit || waitingForMerge)
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
