using System;
using UnityEngine;

internal sealed class TrackState001Qualifying
{
    private const double OpeningDelay = 3.0;
    private readonly TrackPitTraffic traffic;
    private readonly float stall;

    public TrackPitTraffic.Car Car { get; }
    public SessionTrackState State { get; private set; } = SessionTrackState.TrackClosed;

    public TrackState001Qualifying(TrackPitTraffic traffic, float stall)
    {
        this.traffic = traffic;
        this.stall = stall;
        Car = traffic.CreateStalledCar(stall);
    }

    public void UpdateOpening(double visibleSeconds)
    {
        if (visibleSeconds >= OpeningDelay)
            State = SessionTrackState.TrackOpen;
    }

    public void Advance(RaceSessionRun timing, float seconds, float speed, float brakingLimit)
    {
        speed = timing.ConditionLimitedSpeed(0, speed, seconds, State == SessionTrackState.Checkered ? 0f : brakingLimit);
        if (timing.Retired(0))
        {
            State = SessionTrackState.Complete;
            return;
        }

        if (State == SessionTrackState.Checkered)
        {
            double beforeParking = Car.Odometer;
            traffic.Advance(Car, seconds, speed, stall);
            timing.RecordTravel(0, Car.Odometer - beforeParking,
                (Car.Odometer - beforeParking) / seconds, reducedBurn: true);
            if (Car.InPit && Car.PitDistance == stall)
                State = SessionTrackState.Complete;
            return;
        }

        bool wasInPit = Car.InPit;
        float before = Car.TrackDistance;
        double beforeMovement = Car.Odometer;
        traffic.Advance(Car, seconds, speed);
        timing.RecordTravel(0, Car.Odometer - beforeMovement,
            (Car.Odometer - beforeMovement) / seconds, reducedBurn: wasInPit);
        if (!Car.InPit)
        {
            if (wasInPit)
            {
                before = Car.LastExit;
                timing.BeginPlayerFlyingLaps(before);
                seconds = (Car.TrackDistance - before) / speed;
            }
            timing.Record(0, before, Car.TrackDistance, seconds);
        }
        timing.AdvanceClock(seconds);

        if (timing.Finished(0))
        {
            State = SessionTrackState.Checkered;
            traffic.CallToStall(Car, stall);
        }
        else if (timing.PlayerLaps == 1 && timing.TargetLaps == 2)
        {
            State = SessionTrackState.WhiteFlag;
        }
    }

    public float StallBlend() => 1f - Mathf.Clamp01(Math.Abs(Car.PitDistance - stall) / 6f);
}
