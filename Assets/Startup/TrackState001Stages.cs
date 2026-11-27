using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

internal sealed class TrackState001Stages
{
    internal const float PaceSpeed = 32f;
    internal const float ShortTrackPaceSpeed = 55f * TrackDefinition.MetersPerMile / 3600f;
    private readonly bool shortTrack;
    private float CautionPaceSpeed => shortTrack ? ShortTrackPaceSpeed : PaceSpeed;
    private const float RowSpacing = 5f;
    private const float PacePositionVariation = .35f;
    private const float LeadPaceSpeedVariation = .30f;
    private readonly TrackPitTraffic pit;
    private readonly float lapLength;
    private readonly float startLine;
    private readonly float zoneStart;
    private readonly float zoneEnd;
    private readonly System.Random random;
    private readonly int[] ranks, cautionTailOrder;
    private int nextCautionTailOrder;
    private TrackPitTraffic.Car[] cautionField;
    private readonly float[] lapOffsets, formationPositions;
    private readonly float[] speeds;
    private readonly int[] carAhead;
    private readonly float[] gapAhead;
    private readonly float[] cautionPitEntry;
    private readonly bool[] yielding;
    private readonly float[] yieldLane;
    private bool sortingForRestart;
    private int leader;
    private int freePassCar = -1;
    private bool freePassInProgress;
    private float freePassLapOffset;
    private float freePassLaneBlend;
    private HashSet<int> cautionParticipants;
    private float yellowAt;
    private float doubleFileAt;
    private float peelOffAt;
    private float restartAt;
    private float paceDistance, paceExitStation, paceSpeedThisStep;
    private float cautionTime;
    private TrackPitTraffic.Car pace;
    private bool paceMerged;
    private bool paceReturning;
    private readonly float?[] paceWaveByLapOffsets;
    private readonly float[] paceWaveByLaneBlend;
    internal bool PaceWaveByActive { get; private set; }
    private bool doubleFile;
    private bool caughtUp;
    private readonly System.Collections.Generic.HashSet<TrackPitTraffic.Car> returningCars = new System.Collections.Generic.HashSet<TrackPitTraffic.Car>();
    private int breaksStarted;
    private int? earlyStageEnd;
    private bool overtime;
    private bool overtimeAttempt;
    private int nextTestCaution;
    private readonly bool scriptedCautions;
    private readonly bool widenRestartFormationFirst;
    private static readonly int[] TestCautionLaps = { 2, 23, 40, 58 };
    private float formationBlend;
    private float doubleFileBlend;
    private float doubleFileSpacingBlend;
    private bool restartHeld;
    private float paceVariationTime;
    private float paceVariationBlend;

    public SessionTrackState State { get; private set; } = SessionTrackState.TrackOpen;
    public bool PitOpen { get; private set; } = true;
    public bool IsCaution => State == SessionTrackState.Caution;
    internal string CautionReason { get; private set; }

    internal RivalPitWindow RivalPitWindow(RaceSessionRun run, TrackPitTraffic.Car[] cars)
    {
        double yellowMeters = 0d;
        if (IsCaution)
            yellowMeters = Math.Max(0d, restartAt - cars[leader].TrackDistance);
        else if (State == SessionTrackState.StageComplete)
            yellowMeters = Math.Max(0d, yellowAt - cars[leader].TrackDistance) + 4d * lapLength;
        else if (State is SessionTrackState.StageEndingUnderCaution or SessionTrackState.FinishUnderCaution ||
            StageWhiteOverriddenByCaution)
            yellowMeters = run.CreateRivalPitWindow().Meters;
        return run.CreateRivalPitWindow(yellowMeters, closesBeforeTarget: !ReducedFuelBurn);
    }

    public bool CanEnterPits(int index, TrackPitTraffic.Car car) =>
        PitOpen && (!IsCaution || pit.NextEntryDistance(car) >= cautionPitEntry[index]);
    public bool ReducedFuelBurn => ControlsField || earlyStageEnd.HasValue || State == SessionTrackState.FinishUnderCaution;
    internal bool UsesShortTrackCautionPace => shortTrack && (IsCaution || earlyStageEnd.HasValue);
    public bool ControlsField => State == SessionTrackState.StageComplete || State == SessionTrackState.Caution;
    public bool CanRequestCaution => !earlyStageEnd.HasValue &&
        (State == SessionTrackState.TrackOpen || State == SessionTrackState.WhiteFlag);
    public int FreePassCarIndex => freePassCar;
    public bool HasPaceCar => pace != null;
    internal bool PaceReturning => paceReturning;
    internal float PaceExitStation => paceExitStation;
    internal Func<bool, float, float, float, float> PaceTrafficSpeed { get; set; }
    internal float PacePitEntry => peelOffAt;
    internal int PaceLeaderIndex => leader;
    internal bool DoubleFile => doubleFile;
    internal bool ReleasingRestartLanes { get; private set; }
    public bool OneToGo => State == SessionTrackState.Caution && !restartHeld && caughtUp && paceMerged && paceDistance >= doubleFileAt;
    internal bool StageWhiteOverriddenByCaution { get; private set; }
    public bool LightsVisible => (IsCaution || earlyStageEnd.HasValue) && !paceReturning &&
        (!IsCaution || !paceMerged || paceDistance < doubleFileAt) && cautionTime % 0.5f < 0.25f;
    private bool RestartRowsReady => doubleFileBlend >= 1f && doubleFileSpacingBlend >= 1f;
    private bool CanPeelOff => doubleFile && RestartRowsReady && !restartHeld;

    public TrackState001Stages(TrackPitTraffic pit, float lapLength, float startLine,
        float zoneStart, float zoneEnd, int count, System.Random random, bool scriptedCautions = false,
        bool widenRestartFormationFirst = false)
    {
        this.pit = pit;
        this.lapLength = lapLength;
        shortTrack = lapLength <= .5f * TrackDefinition.MetersPerMile;
        this.startLine = startLine;
        this.zoneStart = zoneStart;
        this.zoneEnd = zoneEnd;
        this.random = random;
        this.scriptedCautions = scriptedCautions;
        this.widenRestartFormationFirst = widenRestartFormationFirst;
        ranks = new int[count];
        paceWaveByLapOffsets = new float?[count];
        paceWaveByLaneBlend = new float[count];
        cautionTailOrder = new int[count];
        pit.CautionTailExitClear = CanRejoinBehindCautionLine;
        lapOffsets = new float[count];
        formationPositions = new float[count];
        speeds = new float[count];
        carAhead = new int[count];
        gapAhead = new float[count];
        cautionPitEntry = new float[count];
        yielding = new bool[count];
        yieldLane = new float[count];
        for (int i = 0; i < count; i++)
            speeds[i] = PaceSpeed;
    }

    public void BeforeStep(RaceSessionRun run, TrackPitTraffic.Car[] cars, float seconds, bool restartReady = true)
    {
        restartHeld = !restartReady;
        paceSpeedThisStep = 0f;
        bool varyingPace = ReducedFuelBurn;
        if (varyingPace)
            paceVariationTime += seconds;
        paceVariationBlend = Mathf.MoveTowards(paceVariationBlend, varyingPace ? 1f : 0f, seconds / 3f);
        if ((ControlsField || earlyStageEnd.HasValue) && cars[leader].Retired)
        {
            for (int i = 0; i < cars.Length; i++)
            {
                if (!cars[i].Retired && (cars[leader].Retired || ranks[i] < ranks[leader]))
                    leader = i;
            }
        }
        formationBlend = Mathf.MoveTowards(formationBlend, ControlsField || earlyStageEnd.HasValue ? 1f : 0f, seconds / 3f);
        doubleFileBlend = Mathf.MoveTowards(doubleFileBlend, doubleFile ? 1f : 0f, seconds / 3f);
        // Keep enough distance between bumpers while drivers move out into their restart lanes.
        doubleFileSpacingBlend = widenRestartFormationFirst
            ? Mathf.MoveTowards(doubleFileSpacingBlend, doubleFileBlend >= 1f ? 1f : 0f, seconds / 4f)
            : doubleFileBlend;
        if (paceReturning)
            pit.Advance(pace, seconds, PitPaceSpeed(seconds), pit.ParkingDistance);

        if (!IsCaution && !earlyStageEnd.HasValue)
            return;

        // Keep the pace car out for another lap when a safety crew is still on the course.
        if (IsCaution && !restartReady && paceMerged && !paceReturning && paceDistance >= doubleFileAt - lapLength * .5f)
        {
            doubleFileAt += lapLength;
            peelOffAt += lapLength;
            restartAt += lapLength;
        }

        if (IsCaution && !sortingForRestart && paceMerged && paceDistance >= doubleFileAt - lapLength * 0.5f)
        {
            sortingForRestart = true;
            float lead = cars[leader].TrackDistance;
            for (int i = 0; i < cars.Length; i++)
                yielding[i] = i != freePassCar && !cars[i].InPit && !cars[i].Retired && lead - cars[i].TrackDistance >= lapLength;
        }

        for (int i = 0; i < cars.Length; i++)
        {
            float gap = cars[leader].TrackDistance - cars[i].TrackDistance - FormationOffset(i);
            if (yielding[i] && (cars[i].InPit || cars[i].Retired || gap >= -1f))
                yielding[i] = false;

            yieldLane[i] = Mathf.MoveTowards(yieldLane[i], yielding[i] ? 1f : 0f, seconds / 2f);
        }

        if (freePassInProgress)
        {
            var recipient = cars[freePassCar];
            float gap = cars[leader].TrackDistance - recipient.TrackDistance - FormationOffset(freePassCar);
            if (recipient.Retired || (!recipient.InPit && gap <= 2f))
                freePassInProgress = false;
        }
        freePassLaneBlend = Mathf.MoveTowards(freePassLaneBlend, freePassInProgress ? 1f : 0f, seconds / 2f);

        cautionTime += seconds;
        if (!paceMerged)
        {
            pit.Advance(pace, seconds, PitPaceSpeed(seconds));
            if (!pace.InPit)
            {
                paceMerged = true;
                float lead = cars[leader].TrackDistance;
                paceExitStation = NextMarker(lead - lapLength * 0.5f, pit.ExitDistance);
                paceDistance = paceExitStation + Mathf.Max(0f, pace.TrackDistance - pace.LastExit);
                MarkPacePassesOnPitRoad(run, cars, paceExitStation, paceDistance);
            }
        }
        else if (!paceReturning)
        {
            if (PaceWaveByActive)
                AdvancePaceWaveBy(cars, seconds);

            // Decide before the visual pit approach starts. A missed peel-off
            // gets another pit entry and a matching restart marker.
            float approachDistance = Mathf.Min(100f, lapLength * .45f);
            if (IsCaution && !CanPeelOff && paceDistance + 55f * seconds >= peelOffAt - approachDistance)
            {
                peelOffAt += lapLength;
                restartAt += lapLength;
            }

            float gap = paceDistance - cars[leader].TrackDistance;
            float correction = (RowSpacing - gap) * .65f;
            const float maximumPaceCarGain = 15f * TrackDefinition.MetersPerMile / 3600f;
            float speed = shortTrack
                ? CautionPaceSpeed + Mathf.Clamp(correction, 0f, maximumPaceCarGain)
                : Mathf.Clamp(PaceSpeed + correction, 8f, 55f);
            speed = PaceTrafficSpeed?.Invoke(false, paceDistance, speed, seconds) ?? speed;
            float before = paceDistance;
            paceDistance += speed * seconds;
            paceSpeedThisStep = speed;
            MarkPacePassesOnPitRoad(run, cars, before, IsCaution ? Math.Min(paceDistance, peelOffAt) : paceDistance);
            if (IsCaution && CanPeelOff && paceDistance >= peelOffAt)
            {
                pace = new TrackPitTraffic.Car { TrackDistance = peelOffAt, InPit = true };
                paceReturning = true;
                float pitSeconds = Mathf.Clamp((paceDistance - peelOffAt) / speed, 0f, seconds);
                pit.Advance(pace, pitSeconds, PitPaceSpeed(pitSeconds), pit.ParkingDistance);
            }
        }
    }

    private void MarkPacePassesOnPitRoad(RaceSessionRun run, TrackPitTraffic.Car[] cars, float before, float after)
    {
        if (after <= before) return;
        for (int i = 0; i < cars.Length; i++)
        {
            var car = cars[i];
            if (!car.InPit || car.InGarage || car.Retired || car.CautionPitTailRequired || run.Finished(i)) continue;
            float station = pit.RaceDistance(car);
            if (Mathf.Repeat(station - before, lapLength) > after - before) continue;

            car.CautionPitTailRequired = true;
            cautionTailOrder[i] = 0;
            if (i == freePassCar)
            {
                freePassCar = -1;
                freePassInProgress = false;
                freePassLaneBlend = 0f;
            }
        }
    }

    private bool CanRejoinBehindCautionLine(TrackPitTraffic.Car car, float exit)
    {
        if (!IsCaution || cautionField == null) return true;
        if (!paceMerged) return false;

        // Measure every car from the pace car, or the leader after the pace car peels off.
        float head = paceReturning ? cautionField[leader].TrackDistance : paceDistance;
        float sinceExit = Mathf.Repeat(head - exit, lapLength);
        if (!paceReturning && lapLength - sinceExit < 2f * RowSpacing) return false;

        foreach (var other in cautionField)
        {
            if (ReferenceEquals(car, other) || other.InPit || other.InGarage || other.Retired) continue;
            float behind = Mathf.Repeat(head - other.TrackDistance, lapLength);
            if (sinceExit < behind + RowSpacing) return false;
        }
        return sinceExit >= RowSpacing;
    }

    private float PitPaceSpeed(float seconds) =>
        PaceTrafficSpeed?.Invoke(true, pace.PitDistance, CautionPaceSpeed, seconds) ?? CautionPaceSpeed;

    public float CarSpeed(int index, TrackPitTraffic.Car[] cars, float raceSpeed, float seconds)
    {
        if (IsCaution && paceWaveByLapOffsets[index].HasValue && !cars[index].InPit)
        {
            const float catchUpGain = 15f * TrackDefinition.MetersPerMile / 3600f;
            speeds[index] = Mathf.Min(raceSpeed, CautionPaceSpeed + catchUpGain);
            return speeds[index];
        }

        if ((IsCaution || earlyStageEnd.HasValue) && index == freePassCar && freePassInProgress && !cars[index].InPit)
        {
            float gap = cars[leader].TrackDistance - FormationOffset(index) - cars[index].TrackDistance;
            speeds[index] = FreePassCatchUpSpeed(CautionPaceSpeed, raceSpeed, gap);
            return speeds[index];
        }

        float target = raceSpeed;
        if (ReducedFuelBurn)
        {
            float paceBlend = !cars[index].InPit && !cars[index].Retired ? paceVariationBlend : 0f;
            float lead = cars[leader].TrackDistance;
            if (index == leader || State == SessionTrackState.FinishUnderCaution)
            {
                target = PaceSpeed + TrackState001RaceStart.PaceSpeedVariation(index, paceVariationTime,
                    LeadPaceSpeedVariation) * paceBlend;
                if (State == SessionTrackState.Caution && paceMerged && !paceReturning)
                    target += Mathf.Clamp((paceDistance - lead - RowSpacing) * 0.4f, -5f, 5f);
            }
            else
            {
                float gap = lead - FormationOffset(index) - cars[index].TrackDistance;
                if (yielding[index])
                    target = Mathf.Clamp(PaceSpeed + gap * 0.65f, 12f, PaceSpeed - 4f);
                else
                {
                    float positionVariation = TrackState001RaceStart.PaceSpeedVariation(index, paceVariationTime,
                        PacePositionVariation) * paceBlend;
                    target = Mathf.Clamp(PaceSpeed + (gap + positionVariation) * 0.65f, 8f, 55f);
                }
            }
        }

        if (UsesShortTrackCautionPace)
        {
            // The early stage-ending caution stays yellow through its white-flag lap.
            if (!IsCaution)
            {
                speeds[index] = CautionPaceSpeed;
                return speeds[index];
            }

            // Close an open formation gap below racing pace, then settle at pace speed.
            const float maximumCatchUpGain = 15f * TrackDefinition.MetersPerMile / 3600f;
            float catchUpSpeed = CautionPaceSpeed + Mathf.Clamp(
                (raceSpeed - CautionPaceSpeed) * .5f, 0f, maximumCatchUpGain);
            bool leaderCanCatchPaceCar = paceMerged && !paceReturning &&
                paceDistance - cars[leader].TrackDistance > RowSpacing;
            float ceiling = index == leader && !leaderCanCatchPaceCar ? CautionPaceSpeed : catchUpSpeed;
            speeds[index] = Mathf.Clamp(target + CautionPaceSpeed - PaceSpeed, 0f, ceiling);
            return speeds[index];
        }

        speeds[index] = Mathf.MoveTowards(speeds[index], target, (ControlsField ? 8f : 12f) * seconds);
        return speeds[index];
    }

    internal static float FreePassCatchUpSpeed(float paceSpeed, float raceSpeed, float gap)
    {
        // Preserve enough distance to coast back to pace speed before rejoining.
        float remaining = Math.Max(0f, gap);
        float coastLimit = Mathf.Sqrt(paceSpeed * paceSpeed + 2f * RaceSessionRun.CoastingDeceleration * remaining);
        return Math.Min(raceSpeed, coastLimit);
    }

    public void PreserveCautionOrder(TrackPitTraffic.Car[] cars, float[] stepSpeeds, float seconds, bool[] racingTraffic)
    {
        if (!ReducedFuelBurn) return;
        if (PaceWaveByActive && seconds > 0f && !cars[leader].InPit && !cars[leader].Retired)
        {
            float room = Mathf.Max(0f, paceDistance - cars[leader].TrackDistance - RowSpacing);
            stepSpeeds[leader] = Mathf.Min(stepSpeeds[leader], paceSpeedThisStep + room / seconds);
        }
        if (State != SessionTrackState.FinishUnderCaution && doubleFile && (!widenRestartFormationFirst || doubleFileSpacingBlend > 0f))
            return;

        // Let the small position variations close a gap while retaining ample
        // bumper clearance and the existing caution order.
        float minimumGap = RowSpacing - 2f * PacePositionVariation * paceVariationBlend;
        PreserveOrder(lapLength, stepSpeeds, seconds, carAhead, gapAhead,
            index => cars[index].TrackDistance, index => racingTraffic[index] && HoldsCautionPosition(index, cars, stepSpeeds), minimumGap,
            openGaps: IsCaution && !doubleFile,
            paceLeaderToHold: paceMerged && !paceReturning && paceDistance - cars[leader].TrackDistance < 2f * RowSpacing
                ? leader : -1);
    }

    internal void HoldPitRejoinsBehindLine(TrackPitTraffic.Car[] cars, float[] stepSpeeds, float seconds, bool[] racingTraffic)
    {
        if (!IsCaution || seconds <= 0f) return;

        for (int i = 0; i < cars.Length; i++)
        {
            var car = cars[i];
            if (cautionTailOrder[i] == 0 || car.InPit || car.Retired) continue;
            if (paceMerged && !paceReturning && !paceWaveByLapOffsets[i].HasValue)
            {
                float paceGap = Mathf.Repeat(paceDistance - car.TrackDistance, lapLength);
                stepSpeeds[i] = Math.Min(stepSpeeds[i],
                    paceSpeedThisStep + Math.Max(0f, paceGap - RowSpacing) / seconds);
            }
            for (int j = 0; j < cars.Length; j++)
            {
                var other = cars[j];
                if (i == j || !racingTraffic[j] || other.InPit || other.Retired || ranks[j] >= ranks[i]) continue;
                float gap = Mathf.Repeat(other.TrackDistance - car.TrackDistance, lapLength);
                stepSpeeds[i] = Math.Min(stepSpeeds[i], stepSpeeds[j] + Math.Max(0f, gap - RowSpacing) / seconds);
            }
        }
    }

    internal static void PreserveOrder(float lapLength, float[] stepSpeeds, float seconds,
        int[] carAhead, float[] gapAhead, Func<int, float> distance, Func<int, bool> participates,
        float minimumGap = RowSpacing, bool openGaps = false, int paceLeaderToHold = -1)
    {
        if (seconds <= 0f) return;
        for (int i = 0; i < stepSpeeds.Length; i++)
        {
            carAhead[i] = -1;
            gapAhead[i] = lapLength;
            if (!participates(i))
                continue;

            for (int other = 0; other < stepSpeeds.Length; other++)
            {
                if (other == i || !participates(other))
                    continue;

                float gap = Mathf.Repeat(distance(other) - distance(i), lapLength);
                if (gap == 0f && other > i)
                    continue;
                if (gap < gapAhead[i])
                {
                    gapAhead[i] = gap;
                    carAhead[i] = other;
                }
            }
        }

        if (openGaps)
        {
            // When a pair is already too close, matching speeds preserves the
            // overlap. Let the forward car help only if it has room of its own.
            const float maximumForwardGain = .6f;
            for (int ahead = 0; ahead < stepSpeeds.Length; ahead++)
            {
                if (ahead == paceLeaderToHold || !participates(ahead) ||
                    (carAhead[ahead] >= 0 && gapAhead[ahead] < 2f * minimumGap))
                    continue;

                float gain = 0f;
                for (int i = 0; i < stepSpeeds.Length; i++)
                    if (carAhead[i] == ahead && gapAhead[i] < minimumGap && stepSpeeds[i] >= stepSpeeds[ahead])
                        gain = Math.Max(gain, Math.Min(maximumForwardGain, (minimumGap - gapAhead[i]) * .4f));

                stepSpeeds[ahead] += gain;
            }
        }

        // Propagate braking through the line before any car moves this step.
        for (int pass = 0; pass < stepSpeeds.Length; pass++)
        {
            bool changed = false;
            for (int i = 0; i < stepSpeeds.Length; i++)
            {
                int ahead = carAhead[i];
                if (ahead < 0)
                    continue;

                float clearance = Mathf.Max(0f, gapAhead[i] - minimumGap);
                float deficit = minimumGap - gapAhead[i];
                float dropBack = openGaps && deficit > 0f ? Math.Min(1.4f, deficit * .65f) : 0f;
                float limit = Math.Max(0f, stepSpeeds[ahead] + clearance / seconds - dropBack);
                if (stepSpeeds[i] > limit)
                {
                    stepSpeeds[i] = limit;
                    changed = true;
                }
            }
            if (!changed)
                break;
        }
    }

    private bool HoldsCautionPosition(int index, TrackPitTraffic.Car[] cars, float[] stepSpeeds) =>
        stepSpeeds[index] > 0f && !cars[index].InPit && !cars[index].Retired &&
        (index != freePassCar || freePassLaneBlend == 0f) && yieldLane[index] == 0f;

    public float FinishedCarSpeed(TrackPitTraffic.Car car, float seconds, float stall)
    {
        if (returningCars.Add(car))
        {
            car.CooldownParking = true;
            pit.CallToStall(car, stall);
        }
        return pit.PlannedSpeed(car, PaceSpeed, FinishStop(car, stall), seconds);
    }

    public void AdvanceFinishedCar(TrackPitTraffic.Car car, float seconds, float speed, float stall)
    {
        pit.Advance(car, seconds, speed, FinishStop(car, stall));
        if (!car.InPit && car.BypassingBlockedStall)
        {
            car.BypassingBlockedStall = false;
            car.ManeuverRetrySeconds = 0f;
            pit.CallToStall(car, stall);
        }
    }

    private static float FinishStop(TrackPitTraffic.Car car, float stall) =>
        car.BypassingBlockedStall || car.InPit && car.PitDistance > stall ? float.PositiveInfinity : stall;

    public void AfterStep(RaceSessionRun run, TrackPitTraffic.Car[] cars, bool restartReady = true,
        bool extendFinalCaution = false)
    {
        restartHeld = !restartReady;
        if (State == SessionTrackState.FinishUnderCaution || State == SessionTrackState.Checkered)
            return;

        if (scriptedCautions && !ControlsField && nextTestCaution < TestCautionLaps.Length &&
            run.LeaderLaps >= TestCautionLaps[nextTestCaution])
        {
            nextTestCaution++;
            RequestCaution(run, cars, reason: "Scheduled Test Race caution");
        }

        if (!ControlsField && State != SessionTrackState.FinishUnderCaution)
        {
            int stageEnd = breaksStarted < run.BonusStageCount
                ? earlyStageEnd ?? run.StageEndAt(breaksStarted) : run.TargetLaps;
            if (run.LeaderLaps >= stageEnd - 2)
                PitOpen = false;
            if (!run.FinishSuspended && run.LeaderLaps >= stageEnd - 1)
                State = SessionTrackState.WhiteFlag;
            if (!run.FinishSuspended && run.LeaderLaps >= stageEnd)
            {
                if (breaksStarted < run.BonusStageCount)
                    BeginStageBreak(run, cars);
                else
                    State = SessionTrackState.Checkered;
            }
            return;
        }

        float lead = cars[leader].TrackDistance;
        if (State == SessionTrackState.StageComplete && lead >= yellowAt)
            BeginYellow(yellowAt, run, cars);
        if (State != SessionTrackState.Caution)
            return;

        if (extendFinalCaution && breaksStarted >= run.BonusStageCount &&
            !run.FinishSuspended && run.TargetLaps - run.LeaderLaps <= 1)
        {
            // A long cleanup can run past the original finish while the field is still pacing.
            // Use the existing overtime restart so classified cars never leave an active formation.
            overtime = true;
            run.SuspendFinish();
        }

        if (!caughtUp)
        {
            caughtUp = FormationBunched(cars, lead);
            PitOpen = caughtUp;
        }
        if (shortTrack && !caughtUp && paceMerged && !paceReturning && restartReady && lead >= doubleFileAt &&
            (PaceWaveByActive || PaceCarHoldsBackCatchUp(cars, lead)))
            QueuePaceWaveBy(cars, lead);

        bool fieldSingleFile = !PaceWaveByActive && !freePassInProgress && freePassLaneBlend == 0f;
        for (int i = 0; i < cars.Length; i++)
        {
            if (!cars[i].InPit && !cars[i].Retired && (yielding[i] || yieldLane[i] > 0f))
            {
                fieldSingleFile = false;
                break;
            }
        }
        if (lead >= doubleFileAt && caughtUp && fieldSingleFile)
            doubleFile = true;

        if (lead >= restartAt && paceReturning && caughtUp && fieldSingleFile && RestartRowsReady && restartReady)
        {
            State = SessionTrackState.TrackOpen;
            CautionReason = null;
            ReleasingRestartLanes = widenRestartFormationFirst;
            pit.CombinedRoute = false;
            PitOpen = true;
            if (overtime)
            {
                run.StartOvertime();
                overtime = false;
                overtimeAttempt = true;
            }
        }
        else if (paceReturning && lead >= restartAt)
        {
            restartAt += lapLength;
        }
    }

    private bool PaceCarHoldsBackCatchUp(TrackPitTraffic.Car[] cars, float lead)
    {
        // A split group can be almost a lap behind in formation but physically just behind the pace car.
        for (int i = 0; i < cars.Length; i++)
            if (IsPaceWaveByCandidate(cars, i, lead, out float gapToPace) && gapToPace <= 2f * RowSpacing)
                return true;
        return false;
    }

    private bool IsPaceWaveByCandidate(TrackPitTraffic.Car[] cars, int index, float lead, out float gapToPace)
    {
        var car = cars[index];
        gapToPace = Mathf.Repeat(paceDistance - car.TrackDistance, lapLength);
        float formationGap = lead - FormationOffset(index) - car.TrackDistance;
        return index != leader && CanWaveBy(car) && formationGap > 2f * RowSpacing &&
            gapToPace < Mathf.Min(paceDistance - lead, lapLength * .5f);
    }

    private static bool CanWaveBy(TrackPitTraffic.Car car) =>
        !car.InPit && !car.InGarage && !car.Retired && car.FlatTireLaneBlend <= 0f &&
        !car.ApronEntryActive && !car.ApronExitActive;

    private void QueuePaceWaveBy(TrackPitTraffic.Car[] cars, float lead)
    {
        for (int i = 0; i < cars.Length; i++)
        {
            if (paceWaveByLapOffsets[i].HasValue || !IsPaceWaveByCandidate(cars, i, lead, out float gapToPace))
                continue;

            // Follow the same physical pass even if the pace car and race car are on different scored laps.
            paceWaveByLapOffsets[i] = paceDistance - cars[i].TrackDistance - gapToPace;
            PaceWaveByActive = true;
        }
    }

    private void AdvancePaceWaveBy(TrackPitTraffic.Car[] cars, float seconds)
    {
        PaceWaveByActive = false;
        for (int i = 0; i < cars.Length; i++)
        {
            var car = cars[i];
            if (paceWaveByLapOffsets[i] is float lapOffset &&
                (!CanWaveBy(car) || car.TrackDistance >= paceDistance - lapOffset + 2f * RowSpacing))
                paceWaveByLapOffsets[i] = null;

            bool passing = paceWaveByLapOffsets[i].HasValue;
            paceWaveByLaneBlend[i] = Mathf.MoveTowards(paceWaveByLaneBlend[i], passing ? 1f : 0f, seconds / 2f);
            PaceWaveByActive |= passing || paceWaveByLaneBlend[i] > 0f;
        }
    }

    internal float PaceWaveByLaneBlend(int index) => Mathf.SmoothStep(0f, 1f, paceWaveByLaneBlend[index]);

    private bool FormationBunched(TrackPitTraffic.Car[] cars, float lead)
    {
        if (freePassInProgress || PaceWaveByActive) return false;

        int count = 0;
        for (int i = 0; i < cars.Length; i++)
        {
            if (cars[i].Retired) continue;
            if (cars[i].InPit) return false;
            formationPositions[count++] = cars[i].TrackDistance + lapOffsets[i];
        }

        // Check adjacent gaps after projecting each car onto its formation lap.
        // Small gaps accumulate along the line and can shift a whole group
        // from its assigned station even though the line has bunched up.
        Array.Sort(formationPositions, 0, count);
        if (count == 0 || formationPositions[count - 1] - lead > RowSpacing) return false;
        for (int i = 1; i < count; i++)
            if (formationPositions[i] - formationPositions[i - 1] > 2f * RowSpacing)
                return false;
        return true;
    }

    public void RequestCaution(RaceSessionRun run, TrackPitTraffic.Car[] cars, string reason = null,
        HashSet<int> incidentParticipants = null)
    {
        if (!CanRequestCaution)
            return;

        cautionParticipants = incidentParticipants;
        CautionReason = string.IsNullOrWhiteSpace(reason) ? "Track incident" : reason;
        int remaining = run.StageEnd - run.LeaderLaps;
        if (breaksStarted < run.BonusStageCount && remaining <= 10)
        {
            // Keep the pit-closure and one-to-go sequence before awarding stage points.
            earlyStageEnd = Math.Min(run.StageEnd, run.LeaderLaps + 2);
            run.EndStageAt(earlyStageEnd.Value);
            PrepareFormation(run, cars);
            BeginYellow(NextMarker(cars[leader].TrackDistance + .01f, startLine), run, cars);
            State = SessionTrackState.StageEndingUnderCaution;
            PitOpen = false;
            StageWhiteOverriddenByCaution = remaining == 1;
            if (StageWhiteOverriddenByCaution)
                State = SessionTrackState.WhiteFlag;
            return;
        }

        if (breaksStarted >= run.BonusStageCount && State == SessionTrackState.WhiteFlag)
        {
            run.FreezeFinish();
            PitOpen = false;
            State = SessionTrackState.FinishUnderCaution;
            return;
        }

        overtime = breaksStarted >= run.BonusStageCount && (overtimeAttempt || remaining <= 5);
        if (overtime)
            run.SuspendFinish();

        PitOpen = false;
        PrepareFormation(run, cars);
        BeginYellow(NextMarker(cars[leader].TrackDistance + 0.01f, startLine), run, cars);
    }

    private void BeginYellow(float lapAnchor, RaceSessionRun run, TrackPitTraffic.Car[] cars)
    {
        pit.CombinedRoute = true;
        cautionField = cars;
        Array.Clear(cautionTailOrder, 0, cautionTailOrder.Length);
        nextCautionTailOrder = 0;
        foreach (var car in cars) car.CautionPitTailRequired = false;
        freePassCar = -1;
        freePassInProgress = false;
        freePassLaneBlend = 0f;
        var order = run.RaceCarIndices();
        float leaderProgress = run.Progress(order[0]);
        for (int i = 0; i < cars.Length; i++)
        {
            bool leadLap = leaderProgress - run.Progress(i) < lapLength;
            cautionPitEntry[i] = pit.NextEntryDistance(cars[i]) + (leadLap ? 0f : lapLength);
        }
        freePassCar = SelectFreePassCar(order,
            index => leaderProgress - run.Progress(index) >= lapLength &&
                !run.Finished(index) && !run.Retired(index), cautionParticipants);
        if (freePassCar >= 0)
        {
            freePassInProgress = true;
            float physicalGap = cars[leader].TrackDistance - cars[freePassCar].TrackDistance;
            freePassLapOffset = (Mathf.Floor(physicalGap / lapLength) - 1f) * lapLength;
        }
        cautionParticipants = null;
        UpdatePitFormation(run, cars);
        State = SessionTrackState.Caution;
        cautionTime = 0f;
        sortingForRestart = false;
        Array.Clear(yielding, 0, yielding.Length);
        Array.Clear(yieldLane, 0, yieldLane.Length);
        pace = pit.CreateStalledCar(pit.ParkingDistance);
        paceMerged = false;
        paceReturning = false;
        PaceWaveByActive = false;
        Array.Clear(paceWaveByLapOffsets, 0, paceWaveByLapOffsets.Length);
        Array.Clear(paceWaveByLaneBlend, 0, paceWaveByLaneBlend.Length);
        ScheduleRestart(lapAnchor);
    }

    private void ScheduleRestart(float lapAnchor)
    {
        doubleFileAt = lapAnchor + 3f * lapLength;
        peelOffAt = NextMarker(doubleFileAt, pit.EntryDistance);
        float restart = zoneStart + (zoneEnd - zoneStart) * (float)random.NextDouble();
        restartAt = NextMarker(peelOffAt + 0.01f, startLine + restart);
    }

    internal static int SelectFreePassCar(int[] order, Func<int, bool> activeLapDown, HashSet<int> incidentParticipants)
    {
        foreach (int index in order)
            if (activeLapDown(index))
                return incidentParticipants?.Contains(index) == true ? -1 : index;
        return -1;
    }

    private void BeginStageBreak(RaceSessionRun run, TrackPitTraffic.Car[] cars)
    {
        breaksStarted++;
        bool continueCaution = earlyStageEnd.HasValue;
        earlyStageEnd = null;
        StageWhiteOverriddenByCaution = false;
        if (continueCaution)
        {
            // The pace car and free pass were started at the original yellow.
            // Stage scoring only starts a fresh restart countdown.
            State = SessionTrackState.Caution;
            PrepareFormation(run, cars);
            ScheduleRestart(NextMarker(Mathf.Max(cars[leader].TrackDistance, paceMerged ? paceDistance : 0f) + .01f, startLine));
            return;
        }

        freePassCar = -1;
        freePassInProgress = false;
        freePassLaneBlend = 0f;
        CautionReason = "Stage break";
        PrepareFormation(run, cars);
        yellowAt = NextMarker(cars[leader].TrackDistance + 0.01f, startLine);
        State = SessionTrackState.StageComplete;
    }

    private void PrepareFormation(RaceSessionRun run, TrackPitTraffic.Car[] cars)
    {
        UpdatePitFormation(run, cars);
        caughtUp = false;
        doubleFile = false;
        if (widenRestartFormationFirst)
        {
            doubleFileBlend = 0f;
            doubleFileSpacingBlend = 0f;
            ReleasingRestartLanes = false;
        }
    }

    internal void FinishRestartLaneRelease()
    {
        if (formationBlend == 0f)
            ReleasingRestartLanes = false;
    }

    public void UpdatePitFormation(RaceSessionRun run, TrackPitTraffic.Car[] cars)
    {
        var order = run.RaceCarIndices();
        if (IsCaution)
        {
            foreach (int index in order)
            {
                var car = cars[index];
                if (car.InPit || car.Retired || !car.CautionPitTailRequired) continue;
                car.CautionPitTailRequired = false;
                cautionTailOrder[index] = ++nextCautionTailOrder;
            }
            order = order.OrderBy(index => cautionTailOrder[index]).ToArray();
        }

        foreach (int index in order)
        {
            if (index != freePassCar && !cars[index].InPit && !cars[index].Retired)
            {
                leader = index;
                break;
            }
        }

        float lead = cars[leader].TrackDistance;
        int rank = 0;
        foreach (int index in order)
        {
            if (index == freePassCar || cars[index].InPit || cars[index].Retired ||
                IsCaution && cautionTailOrder[index] > 0)
                continue;

            SetRank(index, rank++);
        }

        if (freePassCar >= 0)
        {
            // An odd slot puts the recipient at the rear of the outside restart line.
            ranks[freePassCar] = rank + (rank % 2 == 0 ? 1 : 0);
            lapOffsets[freePassCar] = freePassLapOffset;
            rank = ranks[freePassCar] + 1;
        }

        if (IsCaution)
            foreach (int index in order)
                if (cautionTailOrder[index] > 0 && !cars[index].InPit && !cars[index].Retired)
                    SetRank(index, rank++);

        void SetRank(int index, int slot)
        {
            ranks[index] = slot;
            lapOffsets[index] = Mathf.Floor(Math.Max(0f, lead - cars[index].TrackDistance) / lapLength) * lapLength;
        }
    }

    private float NextMarker(float distance, float marker) =>
        marker + Mathf.Ceil((distance - marker) / lapLength) * lapLength;

    private float FormationOffset(int index) =>
        lapOffsets[index] + RowSpacing * Mathf.Lerp(ranks[index], ranks[index] / 2, doubleFileSpacingBlend);

    public float Lane(int index, float raceLane, float width, float inside) =>
        Mathf.Lerp(
            Mathf.Lerp(raceLane, (ranks[index] % 2 == 0 ? -width : width) * doubleFileBlend, formationBlend),
            (index == freePassCar ? -inside : inside) * width * 2f, index == freePassCar ? freePassLaneBlend : yieldLane[index]);

    public (bool OnPitRoad, float Distance, float ParkingBlend) PacePosition()
    {
        if (!paceMerged || paceReturning)
        {
            float blend = Mathf.Clamp01(1f - Math.Abs(pit.ParkingDistance - pace.PitDistance) / 8f);
            return (true, pace.PitDistance, blend);
        }
        return (false, paceDistance, 0f);
    }
}
