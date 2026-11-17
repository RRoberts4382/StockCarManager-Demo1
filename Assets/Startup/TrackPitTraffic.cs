using UnityEngine;

internal sealed class TrackPitTraffic
{
    private const float MphToMetersPerSecond = 0.44704f;
    private const float StallLaneClearance = 2.2f;
    private readonly float lapLength;
    private readonly float entryDistance;
    private readonly float bypassDistance;
    private readonly float speedLimit;
    private readonly float[] boundaries;
    private readonly float laneOffset;
    private readonly bool singleThroughLane;
    private float throughLane;
    private bool hasPassingLane;
    private Car[] field;
    private float[] stalls;
    private float[] stallLanes;

    public float RoadStart { get; }
    public float RoadEnd { get; }
    public float RoadLength => RoadEnd - RoadStart;
    public float ParkingDistance => RoadEnd - Mathf.Min(4f, RoadLength * 0.1f);
    public float RouteLength => boundaries[2];
    public float SpeedLimit => speedLimit;
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
        public float LastExit = float.NegativeInfinity;
        public bool InPit;
        public bool InGarage;
        public bool FrontEntry;
        public bool BackExit;
        public float ConnectorDistance;
        public float ConnectorEntryLane;
        public bool Retired;
        public float FlatTireLaneBlend;
        public double Odometer;
    }

    public TrackPitTraffic(float lapLength, float entryDistance, float exitDistance,
        float entryLength, float roadLength, float exitLength, TrackPitRoad road, bool singleThroughLane = false)
    {
        this.lapLength = lapLength;
        this.singleThroughLane = singleThroughLane;
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
        car.NextEntry = entry + Mathf.Ceil((distance - entry) / lapLength) * lapLength;
    }

    public float NextEntryDistance(Car car)
    {
        float entry = EntryFor(car);
        return entry + Mathf.Ceil((car.TrackDistance - entry) / lapLength) * lapLength;
    }

    public float DistanceToEntry(Car car) => Mathf.Repeat(EntryFor(car) - car.TrackDistance, lapLength);

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

    private float TrafficSpeed(Car car, float seconds, float speed, float stopAt)
    {
        int index = car.FieldIndex;
        if (index < 0)
            return speed;

        const float clearance = 3.4f;
        bool departing = car.PitDistance == stalls[index] && stopAt != stalls[index];
        float target = throughLane;
        float lookAhead = Mathf.Max(clearance + 3f, speed * 1.5f);
        bool approachingStall = stopAt <= RoadEnd && stopAt - car.PitDistance < 12f;
        if (approachingStall)
            target = stallLanes[index];
        else if (hasPassingLane && !LaneAvailable(target, lookAhead))
        {
            float alternate = -target;
            if (LaneAvailable(alternate, lookAhead))
                target = alternate;
            else if (departing)
                return 0f;
            else
                target = car.PitLane;
        }

        // Center the car at the track handoff and keep the full lateral
        // clearance once it reaches the stall section.
        float blend = 1f;
        if (car.PitDistance < RoadStart)
            blend = Mathf.Clamp01(car.PitDistance / Mathf.Min(8f, RoadStart));
        else if (car.PitDistance > RoadEnd)
            blend = Mathf.Clamp01((RouteLength - car.PitDistance) / Mathf.Min(8f, RouteLength - RoadEnd));
        float nextLane = Mathf.MoveTowards(car.PitLane, target * blend, 3f * seconds);
        if (LaneAvailable(nextLane, clearance, merging: true))
            car.PitLane = nextLane;
        else if (departing && hasPassingLane)
            return 0f;


        foreach (var other in field)
        {
            if (other == car || other.InGarage || !other.InPit || other.BackExit || (other.FrontEntry != car.FrontEntry && Mathf.Min(other.PitDistance, car.PitDistance) < frontJoin) || Mathf.Abs(other.PitLane - car.PitLane) >= 1.85f)
                continue;
            float gap = other.PitDistance - car.PitDistance;
            if (gap > 0f || (gap == 0f && other.FieldIndex < index))
                speed = Mathf.Min(speed, Mathf.Max(0f, gap - clearance) / seconds);
        }

        return !hasPassingLane && departing ? Mathf.Min(speed, 4f) : speed;

        bool LaneAvailable(float lane, float ahead, bool merging = false)
        {
            foreach (var other in field)
            {
                if (other == car || other.InGarage || !other.InPit || other.BackExit || (other.FrontEntry != car.FrontEntry && Mathf.Min(other.PitDistance, car.PitDistance) < frontJoin))
                    continue;
                float gap = other.PitDistance - car.PitDistance;
                float separation = Mathf.Abs(other.PitLane - lane);
                if (gap >= -6f && gap <= ahead && separation < 1.85f &&
                    (!merging || separation <= Mathf.Abs(other.PitLane - car.PitLane)))
                    return false;
            }
            return true;
        }
    }

    public void Advance(Car car, float seconds, float trackSpeed, float stopAt = float.PositiveInfinity)
    {
        while (seconds > 0f && trackSpeed > 0f)
        {
            if (!car.InPit)
            {
                float toEntry = Mathf.Max(0f, car.NextEntry - car.TrackDistance);
                if (toEntry > trackSpeed * seconds)
                {
                    car.Odometer += trackSpeed * seconds;
                    car.PreciseTrackDistance += (double)trackSpeed * seconds;
                    return;
                }

                seconds -= toEntry / trackSpeed;
                car.Odometer += toEntry;
                car.TrackDistance = car.NextEntry;
                car.NextEntry = float.PositiveInfinity;
                car.PitDistance = 0f;
                car.FrontEntry = UsesFrontEntry(car);
                car.BackExit = false;
                car.ConnectorDistance = 0f;
                car.InPit = true;
                continue;
            }

            if (car.BackExit)
            {
                float travel = Mathf.Min(backExitLength - car.ConnectorDistance, seconds * trackSpeed);
                foreach (var other in field)
                {
                    if (other.InGarage || !other.InPit || !other.BackExit || other == car)
                        continue;
                    float gap = other.ConnectorDistance - car.ConnectorDistance;
                    if (gap > 0f || (gap == 0f && other.FieldIndex < car.FieldIndex))
                        travel = Mathf.Min(travel, Mathf.Max(0f, gap - 3.4f));
                }
                float exitLane = car.ConnectorEntryLane * (1f - Mathf.Clamp01((car.ConnectorDistance + travel) / backExitLength));
                car.PitLane = Mathf.MoveTowards(car.PitLane, exitLane, 3f * seconds);
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
                foreach (var other in field)
                {
                    if (!other.InGarage && other.InPit && other.BackExit)
                        target = Mathf.Min(target, Mathf.Max(car.PitDistance, backLeave + other.ConnectorDistance - 3.4f));
                }
                takeBackExit = target == backLeave;
            }
            bool waitingForBackExit = !takeBackExit && target < (stopInSegment ? stopAt : boundaries[segment]);
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
            if (waitingForBackExit)
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
