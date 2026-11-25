using System;
using UnityEngine;

internal sealed class TrackState001RaceStart
{
    private const double OpeningDelay = 5.0;
    internal const float PaceSpeed = 32f;
    private const float FormationPositionVariation = .55f;
    private const float VariationFadeDistance = 120f;
    private readonly TrackPitTraffic pit;
    private readonly int[] slots;
    private readonly float rowSpacing;
    private readonly float lightsOffAt;
    private readonly float peelOffAt;
    private readonly float greenAt;
    private readonly float parkingDistance;
    private readonly float firstRowOffset;
    private readonly float positionVariation;
    private float progress;
    private float rolloutSeconds;
    private float fieldLead;
    private float paceLead;
    private float flashTime;
    private float previousProgress;
    private float previousFieldLead;
    private float previousFlashTime;
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
        positionVariation = Mathf.Clamp((rowSpacing - VehicleCollision001.ContactLength - .15f) * .5f,
            0f, FormationPositionVariation);
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
        previousFlashTime = flashTime;
        flashTime += seconds;
        if (State == SessionTrackState.Pacing)
        {
            previousProgress = progress;
            previousFieldLead = fieldLead;
            float speed = progress < pit.RouteLength ? Math.Min(PaceSpeed, pit.SpeedLimit) : PaceSpeed;
            if (progress < pit.RouteLength)
            {
                // Vary the rollout speed together so spacing and pit speed limits hold.
                float variation = .0125f + PaceSpeedVariation(0, flashTime + 3f, .0125f);
                speed *= 1f - PitVariationBlend(progress) * variation;
            }
            progress += speed * seconds;
            rolloutSeconds += seconds;
            paceLead = pit.ExitDistance + progress - pit.RouteLength;
            fieldLead = Math.Min(paceLead - firstRowOffset, greenAt);
            if (returningPaceCar == null && paceLead >= peelOffAt)
            {
                returningPaceCar = new TrackPitTraffic.Car { TrackDistance = peelOffAt, InPit = true };
                seconds = Mathf.Clamp((paceLead - peelOffAt) / speed, 0f, seconds);
            }
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
        // Both cars in a row drift together, keeping the two lanes apart.
        float pitLane = lane > 0f
            ? side + PitVariationBlend(distance) * PaceSpeedVariation(slot / 2, flashTime, Mathf.Min(.08f, lane * .1f))
            : side;
        return distance < pit.RouteLength ? (true, distance, pitLane)
            : (false, fieldLead - offset + FormationVariation(index, distance, fieldLead, flashTime), side);
    }

    private float PitVariationBlend(float distance)
    {
        float room = Mathf.Min(distance - pit.RoadStart, pit.RoadEnd - distance);
        return Mathf.SmoothStep(0f, 1f, room / 12f) * Mathf.SmoothStep(0f, 1f, rolloutSeconds);
    }

    internal float PacedMeters(int index, float fallback)
    {
        float offset = firstRowOffset + slots[index] / 2 * rowSpacing;
        float before = previousProgress - offset;
        if (before < pit.RouteLength)
            return fallback;

        float previousVariation = FormationVariation(index, before, previousFieldLead, previousFlashTime);
        float currentVariation = FormationVariation(index, progress - offset, fieldLead, flashTime);
        return Mathf.Max(0f, fieldLead - previousFieldLead + currentVariation - previousVariation);
    }

    private float FormationVariation(int index, float distance, float lead, float time)
    {
        float leavingPit = Mathf.SmoothStep(0f, 1f, (distance - pit.RouteLength) / VariationFadeDistance);
        float approachingGreen = Mathf.SmoothStep(0f, 1f, (greenAt - lead) / VariationFadeDistance);
        return PaceSpeedVariation(index, time, positionVariation) * leavingPit * approachingGreen;
    }

    internal static float PaceSpeedVariation(int index, float elapsed, float amplitude) =>
        amplitude * Mathf.Sin(elapsed * (.21f + .014f * (index % 5)) + index * 2.399963f);

    public (bool OnPitRoad, float Distance, float ParkingBlend) PacePosition()
    {
        if (returningPaceCar != null)
            return (true, returningPaceCar.PitDistance,
                Mathf.Clamp01(1f - (parkingDistance - returningPaceCar.PitDistance) / 8f));
        return progress < pit.RouteLength ? (true, progress, 0f) : (false, paceLead, 0f);
    }
}
