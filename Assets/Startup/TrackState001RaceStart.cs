using System;
using UnityEngine;

internal sealed class TrackState001RaceStart
{
    private const double OpeningDelay = 5.0;
    private const float PaceSpeed = 32f;
    private readonly TrackPitTraffic pit;
    private readonly int[] slots;
    private readonly float rowSpacing;
    private readonly float lightsOffAt;
    private readonly float peelOffAt;
    private readonly float greenAt;
    private readonly float parkingDistance;
    private readonly float firstRowOffset;
    private float progress;
    private float fieldLead;
    private float paceLead;
    private float flashTime;
    private TrackPitTraffic.Car returningPaceCar;

    public SessionTrackState State { get; private set; } = SessionTrackState.TrackClosed;
    public int SecondsUntilRelease { get; private set; } = 5;
    internal bool OneToGo => State == SessionTrackState.Pacing && paceLead >= lightsOffAt;
    public bool LightsVisible => paceLead < lightsOffAt && flashTime % 0.5f < 0.25f;
    internal bool PaceReturning => returningPaceCar != null;
    internal float PacePitEntry => peelOffAt;
    public float TimingStartLine { get; }
    public float FormationProgress => progress;

    public float RacingSpeed(float targetSpeed, float secondsSinceGreen) =>
        Mathf.Lerp(PaceSpeed, targetSpeed, Mathf.Clamp01(secondsSinceGreen / 3f));

    public TrackState001RaceStart(TrackPitTraffic pit, int[] slots, float startLine,
        float lapLength, float zoneStart, float zoneEnd, System.Random random)
    {
        this.pit = pit;
        this.slots = slots;
        int rows = (slots.Length + 1) / 2;
        rowSpacing = Math.Min(4.5f, pit.RoadLength / (rows + 2));
        firstRowOffset = rowSpacing;
        float lastRowOffset = rowSpacing * rows;
        progress = pit.RoadStart + lastRowOffset + rowSpacing;
        parkingDistance = pit.ParkingDistance;

        lightsOffAt = NextAtOrAfter(pit.ExitDistance, startLine, lapLength);
        peelOffAt = NextAtOrAfter(lightsOffAt, pit.EntryDistance, lapLength);
        float restart = zoneStart + (zoneEnd - zoneStart) * (float)random.NextDouble();
        greenAt = NextAtOrAfter(peelOffAt + 0.01f, startLine + restart, lapLength);
        TimingStartLine = NextAtOrAfter(greenAt, startLine, lapLength);
    }

    private static float NextAtOrAfter(float distance, float marker, float lapLength) =>
        marker + Mathf.Ceil((distance - marker) / lapLength) * lapLength;

    public void UpdateOpening(double visibleSeconds)
    {
        flashTime = (float)visibleSeconds;
        SecondsUntilRelease = Math.Max(0, (int)Math.Ceiling(OpeningDelay - visibleSeconds));
        if (visibleSeconds >= OpeningDelay)
            State = SessionTrackState.Pacing;
    }

    public void Advance(float seconds)
    {
        flashTime += seconds;
        if (State == SessionTrackState.Pacing)
        {
            float speed = progress < pit.RouteLength ? Math.Min(PaceSpeed, pit.SpeedLimit) : PaceSpeed;
            progress += speed * seconds;
            paceLead = pit.ExitDistance + progress - pit.RouteLength;
            fieldLead = Math.Min(paceLead - firstRowOffset, greenAt);
            if (returningPaceCar == null && paceLead >= peelOffAt)
                returningPaceCar = new TrackPitTraffic.Car { TrackDistance = peelOffAt, InPit = true };
            if (fieldLead >= greenAt)
                State = SessionTrackState.TrackOpen;
        }

        if (returningPaceCar != null)
            pit.Advance(returningPaceCar, seconds, Math.Min(PaceSpeed, pit.SpeedLimit), parkingDistance);
    }

    public (bool OnPitRoad, float Distance, float Lane) GridPosition(int index, float lane)
    {
        int slot = slots[index];
        float offset = slot / 2 * rowSpacing;
        float distance = progress - firstRowOffset - offset;
        float side = slot % 2 == 0 ? -lane : lane;
        return distance < pit.RouteLength ? (true, distance, side)
            : (false, fieldLead - offset, side);
    }

    public (bool OnPitRoad, float Distance, float ParkingBlend) PacePosition()
    {
        if (returningPaceCar != null)
            return (true, returningPaceCar.PitDistance,
                Mathf.Clamp01(1f - (parkingDistance - returningPaceCar.PitDistance) / 8f));
        return progress < pit.RouteLength ? (true, progress, 0f) : (false, paceLead, 0f);
    }
}
