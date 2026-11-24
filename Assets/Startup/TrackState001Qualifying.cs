using System;
using UnityEngine;

internal sealed class TrackState001Qualifying
{
    private const double OpeningDelay = 3.0;
    private readonly TrackPitTraffic traffic;
    private readonly float stall;
    private bool holdForGarage;
    private float garageVisitTarget;

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

    public void SetGarageVisit(bool visiting, float? alternateTarget = null)
    {
        holdForGarage = visiting;
        garageVisitTarget = alternateTarget ?? stall;
        if (!visiting && State != SessionTrackState.Checkered)
            Car.NextEntry = float.PositiveInfinity;
    }

    public void Advance(RaceSessionRun timing, float seconds, float speed, float brakingLimit,
        float preferredLineSpeedFactor)
    {
        if (Car.InGarage)
        {
            timing.AdvanceClock(seconds);
            return;
        }

        if (holdForGarage && garageVisitTarget != stall && Car.InPit &&
            State != SessionTrackState.Checkered && !timing.HasRandomFlatTire(0))
        {
            float remaining = garageVisitTarget - Car.PitDistance;
            if (remaining > 0f)
                speed = Mathf.Min(speed, remaining < .001f
                    ? remaining / seconds
                    : TrackPitTraffic.ApproachLimit(0f, remaining, seconds));
        }
        bool activeOnTrack = !Car.InPit && State != SessionTrackState.Checkered;
        if (!activeOnTrack || State != SessionTrackState.TrackOpen || Car.ApronExitActive || Car.ApronEntryActive)
            preferredLineSpeedFactor = 1f;
        speed = timing.ConditionLimitedSpeed(0, speed, seconds,
            State == SessionTrackState.Checkered ? 0f : brakingLimit, activeOnTrack,
            preferredLineSpeedFactor: preferredLineSpeedFactor);
        bool failed = timing.HasTerminalFailure(0);
        if (failed)
            Car.NextEntry = float.PositiveInfinity;
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
                (Car.Odometer - beforeParking) / seconds, Car.PreciseTrackDistance, reducedBurn: true, onTrack: !Car.InPit);
            if (Car.InPit && Car.PitDistance == stall)
                State = SessionTrackState.Complete;
            return;
        }

        bool wasInPit = Car.InPit;
        float before = Car.TrackDistance;
        double beforeMovement = Car.Odometer;
        int flats = timing.HasRandomFlatTire(0) ? timing.FlatTireMask(0) : 0;
        float stopAt = flats != 0 ? stall : holdForGarage ? garageVisitTarget : float.PositiveInfinity;
        if (stopAt != stall && Car.InPit && Car.PitDistance > stopAt)
            stopAt = float.PositiveInfinity;
        if (!float.IsInfinity(stopAt) && !Car.InPit && !failed)
            traffic.CallToStall(Car, stopAt);
        speed = timing.FlatTireSpeed(speed, flats);
        traffic.Advance(Car, seconds, speed, stopAt);
        if (flats != 0 && Car.InPit && Car.PitDistance == stall)
            for (int tire = 0; tire < 4; tire++)
                if ((flats & (1 << tire)) != 0)
                {
                    var replacement = timing.Vehicle(0).GetTire(tire);
                    replacement.wear = 0f;
                    replacement.temperature = 0f;
                    replacement.coldPressurePsi = VehicleSpeed003.ColdPressure(timing.SetupForCar(0), tire);
                }
        timing.RecordTravel(0, Car.Odometer - beforeMovement,
            (Car.Odometer - beforeMovement) / seconds, Car.PreciseTrackDistance, reducedBurn: wasInPit, onTrack: !Car.InPit);
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
