using System;
using UnityEngine;

internal enum SessionTrackState
{
    TrackClosed,
    Pacing,
    StageComplete,
    Caution,
    StageEndingUnderCaution,
    FinishUnderCaution,
    TrackOpen,
    WhiteFlag,
    Checkered,
    ReturnToPits,
    Complete
}

internal sealed class TrackState001Practice
{
    private const double OpeningDelay = 3.0;
    private const float StallManeuverDistance = 6f;
    private readonly TrackPitTraffic traffic;
    private readonly TrackPitTraffic.Car[] cars;
    private readonly float[] stalls;
    private readonly float[] stallWait;

    private const int PlayerIndex = 0;

    private float cautionRemaining;

    public SessionTrackState State { get; private set; } = SessionTrackState.TrackClosed;

    public TrackState001Practice(TrackPitTraffic traffic, TrackPitTraffic.Car[] cars, float[] stalls, System.Random random)
    {
        this.traffic = traffic;
        this.cars = cars;
        this.stalls = stalls;
        stallWait = new float[cars.Length];
        for (int i = 0; i < cars.Length; i++)
        {
            cars[i] = traffic.CreateStalledCar(stalls[i]);
            if (i != PlayerIndex)
                stallWait[i] = 0.5f + 9.5f * (float)random.NextDouble();
        }
    }

    public void UpdateOpening(double visibleSeconds)
    {
        if (visibleSeconds >= OpeningDelay)
            State = SessionTrackState.TrackOpen;
    }

    public void RequestCaution()
    {
        if (State == SessionTrackState.TrackOpen || State == SessionTrackState.Caution)
        {
            State = SessionTrackState.Caution;
            cautionRemaining = 10f;
        }
    }

    public void AdvanceCaution(float seconds)
    {
        if (State != SessionTrackState.Caution)
            return;

        cautionRemaining -= seconds;
        if (cautionRemaining <= 0f)
            State = SessionTrackState.TrackOpen;
    }

    public void CloseTrack()
    {
        State = SessionTrackState.ReturnToPits;
        for (int i = 0; i < cars.Length; i++)
            traffic.CallToStall(cars[i], stalls[i]);
    }

    public void Advance(int index, float seconds, float speed)
    {
        var car = cars[index];
        if (State == SessionTrackState.ReturnToPits)
        {
            traffic.Advance(car, seconds, speed, stalls[index]);
            return;
        }

        if (stallWait[index] > 0f)
        {
            float wait = Math.Min(seconds, stallWait[index]);
            stallWait[index] -= wait;
            seconds -= wait;
            if (seconds <= 0f)
                return;
        }

        traffic.Advance(car, seconds, speed);
    }

    public void CheckParking()
    {
        for (int i = 0; i < cars.Length; i++)
        {
            if (!cars[i].Retired && (!cars[i].InPit || cars[i].PitDistance != stalls[i]))
                return;
        }
        State = SessionTrackState.Complete;
    }

    public float StallBlend(int index)
    {
        return 1f - Mathf.Clamp01(Math.Abs(cars[index].PitDistance - stalls[index]) / StallManeuverDistance);
    }
}
