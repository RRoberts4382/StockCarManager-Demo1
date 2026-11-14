using UnityEngine;

internal sealed class TrackPitTraffic
{
    private const float MphToMetersPerSecond = 0.44704f;
    private readonly float lapLength;
    private readonly float entryDistance;
    private readonly float bypassDistance;
    private readonly float speedLimit;
    private readonly float[] boundaries;
    private readonly float laneOffset;
    private float outsideLane;
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
        public float TrackDistance;
        public float PitDistance;
        public float PitLane;
        public int FieldIndex = -1;
        public float NextEntry = float.PositiveInfinity;
        public float LastExit = float.NegativeInfinity;
        public bool InPit;
        public bool Retired;
        public float FlatTireLaneBlend;
        public double Odometer;
    }

    public TrackPitTraffic(float lapLength, float entryDistance, float exitDistance,
        float entryLength, float roadLength, float exitLength, TrackPitRoad road)
    {
        this.lapLength = lapLength;
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
        car.NextEntry = entryDistance + Mathf.Ceil((distance - entryDistance) / lapLength) * lapLength;
    }

    public float NextEntryDistance(Car car) =>
        entryDistance + Mathf.Ceil((car.TrackDistance - entryDistance) / lapLength) * lapLength;

    public float DistanceToEntry(Car car) => Mathf.Repeat(entryDistance - car.TrackDistance, lapLength);

    private float[] progressAlongRoute;

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

        float fraction = Mathf.Clamp01(car.PitDistance / RouteLength);
        if (progressAlongRoute == null)
            return car.TrackDistance + fraction * bypassDistance;

        float sample = fraction * (progressAlongRoute.Length - 1);
        int lower = System.Math.Min((int)sample, progressAlongRoute.Length - 2);
        return car.TrackDistance + Mathf.Lerp(progressAlongRoute[lower], progressAlongRoute[lower + 1], sample - lower);
    }

    public void ConfigureField(Car[] cars, float[] stalls, float[] stallLanes, float inside)
    {
        field = cars;
        this.stalls = stalls;
        this.stallLanes = stallLanes;
        outsideLane = -inside * laneOffset;
        for (int i = 0; i < cars.Length; i++)
        {
            cars[i].FieldIndex = i;
            if (cars[i].InPit && cars[i].PitDistance == stalls[i])
                cars[i].PitLane = stallLanes[i];
        }
    }

    private float TrafficSpeed(Car car, float seconds, float speed, float stopAt)
    {
        int index = car.FieldIndex;
        if (index < 0)
            return speed;

        const float clearance = 3.4f;
        bool departing = car.PitDistance == stalls[index] && stopAt != stalls[index];
        float target = stopAt <= RoadEnd ? -outsideLane : outsideLane;
        bool approachingStall = stopAt <= RoadEnd && stopAt - car.PitDistance < 12f;
        if (approachingStall)
            target = stallLanes[index];
        else if (!LaneAvailable(target, clearance + 3f))
        {
            float alternate = -target;
            if (LaneAvailable(alternate, clearance + 3f))
                target = alternate;
            else if (departing)
                return 0f;
            else
                target = car.PitLane;
        }

        float blend = Mathf.Clamp01(Mathf.Min(car.PitDistance, RouteLength - car.PitDistance) / 8f);
        float nextLane = Mathf.MoveTowards(car.PitLane, target * blend, 3f * seconds);
        if (LaneAvailable(nextLane, clearance, merging: true))
            car.PitLane = nextLane;
        else if (departing)
            return 0f;

        foreach (var other in field)
        {
            if (other == car || !other.InPit || Mathf.Abs(other.PitLane - car.PitLane) >= 1.85f)
                continue;
            float gap = other.PitDistance - car.PitDistance;
            if (gap > 0f || (gap == 0f && other.FieldIndex < index))
                speed = Mathf.Min(speed, Mathf.Max(0f, gap - clearance) / seconds);
        }

        return speed;

        bool LaneAvailable(float lane, float ahead, bool merging = false)
        {
            foreach (var other in field)
            {
                if (other == car || !other.InPit)
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
                    car.TrackDistance += trackSpeed * seconds;
                    return;
                }

                seconds -= toEntry / trackSpeed;
                car.Odometer += toEntry;
                car.TrackDistance = car.NextEntry;
                car.NextEntry = float.PositiveInfinity;
                car.PitDistance = 0f;
                car.InPit = true;
                continue;
            }

            int segment = car.PitDistance < boundaries[0] ? 0 : car.PitDistance < boundaries[1] ? 1 : 2;
            float speed = segment == 1 ? Mathf.Min(trackSpeed, speedLimit) : trackSpeed;
            speed = TrafficSpeed(car, seconds, speed, stopAt);
            bool stopInSegment = stopAt >= car.PitDistance && stopAt <= boundaries[segment];
            float target = stopInSegment ? stopAt : boundaries[segment];
            float remaining = target - car.PitDistance;
            if (remaining > speed * seconds)
            {
                car.Odometer += speed * seconds;
                car.PitDistance += speed * seconds;
                return;
            }

            if (speed <= 0f)
                return;
            seconds -= remaining / speed;
            car.Odometer += remaining;
            car.PitDistance = target;
            if (stopInSegment)
                return;

            if (segment == 2)
            {
                car.InPit = false;
                car.TrackDistance += bypassDistance;
                car.LastExit = car.TrackDistance;
            }
        }
    }
}
