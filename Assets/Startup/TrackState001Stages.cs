using System;
using UnityEngine;

internal sealed class TrackState001Stages
{
    internal const float PaceSpeed = 32f;
    private const float RowSpacing = 5f;
    private const float PacePositionVariation = .35f;
    private const float LeadPaceSpeedVariation = .30f;
    private readonly TrackPitTraffic pit;
    private readonly float lapLength;
    private readonly float startLine;
    private readonly float zoneStart;
    private readonly float zoneEnd;
    private readonly System.Random random;
    private readonly int[] ranks;
    private readonly float[] lapOffsets;
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
    private int cautionCause = -1;
    private float yellowAt;
    private float doubleFileAt;
    private float peelOffAt;
    private float restartAt;
    private float paceDistance, paceExitStation;
    private float cautionTime;
    private TrackPitTraffic.Car pace;
    private bool paceMerged;
    private bool paceReturning;
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

    public bool CanEnterPits(int index, TrackPitTraffic.Car car) =>
        PitOpen && (!IsCaution || pit.NextEntryDistance(car) >= cautionPitEntry[index]);
    public bool ReducedFuelBurn => ControlsField || earlyStageEnd.HasValue || State == SessionTrackState.FinishUnderCaution;
    public bool ControlsField => State == SessionTrackState.StageComplete || State == SessionTrackState.Caution;
    public bool CanRequestCaution => !earlyStageEnd.HasValue &&
        (State == SessionTrackState.TrackOpen || State == SessionTrackState.WhiteFlag);
    public int FreePassCarIndex => freePassCar;
    public bool HasPaceCar => pace != null;
    internal bool PaceReturning => paceReturning;
    internal float PaceExitStation => paceExitStation;
    internal Func<bool, float, float, float, float> PaceTrafficSpeed { get; set; }
    internal float PacePitEntry => peelOffAt;
    internal bool ReleasingRestartLanes { get; private set; }
    public bool OneToGo => State == SessionTrackState.Caution && !restartHeld && paceMerged && paceDistance >= doubleFileAt;
    internal bool StageWhiteOverriddenByCaution { get; private set; }
    public bool LightsVisible => State == SessionTrackState.Caution && !paceReturning &&
        (!paceMerged || paceDistance < doubleFileAt) && cautionTime % 0.5f < 0.25f;
    private bool RestartRowsReady => !widenRestartFormationFirst || doubleFileSpacingBlend >= 1f;

    public TrackState001Stages(TrackPitTraffic pit, float lapLength, float startLine,
        float zoneStart, float zoneEnd, int count, System.Random random, bool scriptedCautions = false,
        bool widenRestartFormationFirst = false)
    {
        this.pit = pit;
        this.lapLength = lapLength;
        this.startLine = startLine;
        this.zoneStart = zoneStart;
        this.zoneEnd = zoneEnd;
        this.random = random;
        this.scriptedCautions = scriptedCautions;
        this.widenRestartFormationFirst = widenRestartFormationFirst;
        ranks = new int[count];
        lapOffsets = new float[count];
        speeds = new float[count];
        carAhead = new int[count];
        gapAhead = new float[count];
        cautionPitEntry = new float[count];
        yielding = new bool[count];
        yieldLane = new float[count];
        for (int i = 0; i < count; i++)
            speeds[i] = PaceSpeed;
    }

    public void BeforeStep(TrackPitTraffic.Car[] cars, float seconds, bool restartReady = true)
    {
        restartHeld = !restartReady;
        bool varyingPace = ReducedFuelBurn;
        if (varyingPace)
            paceVariationTime += seconds;
        paceVariationBlend = Mathf.MoveTowards(paceVariationBlend, varyingPace ? 1f : 0f, seconds / 3f);
        if (ControlsField && cars[leader].Retired)
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

        if (State != SessionTrackState.Caution)
            return;

        // Keep the pace car out for another lap when a safety crew is still on the course.
        if (!restartReady && paceMerged && !paceReturning && paceDistance >= doubleFileAt - lapLength * .5f)
        {
            doubleFileAt += lapLength;
            peelOffAt += lapLength;
            restartAt += lapLength;
        }

        if (!sortingForRestart && paceMerged && paceDistance >= doubleFileAt - lapLength * 0.5f)
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
            }
        }
        else if (!paceReturning)
        {
            float gap = paceDistance - cars[leader].TrackDistance;
            float speed = Mathf.Clamp(PaceSpeed + (RowSpacing - gap) * 0.65f, 8f, 55f);
            speed = PaceTrafficSpeed?.Invoke(false, paceDistance, speed, seconds) ?? speed;
            paceDistance += speed * seconds;
            if (paceDistance >= peelOffAt)
            {
                pace = new TrackPitTraffic.Car { TrackDistance = peelOffAt, InPit = true };
                paceReturning = true;
                float pitSeconds = Mathf.Clamp((paceDistance - peelOffAt) / speed, 0f, seconds);
                pit.Advance(pace, pitSeconds, PitPaceSpeed(pitSeconds), pit.ParkingDistance);
            }
        }
    }

    private float PitPaceSpeed(float seconds) =>
        PaceTrafficSpeed?.Invoke(true, pace.PitDistance, PaceSpeed, seconds) ?? PaceSpeed;

    public float CarSpeed(int index, TrackPitTraffic.Car[] cars, float raceSpeed, float seconds)
    {
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
                if (index == freePassCar && freePassInProgress)
                    target = Mathf.Clamp(PaceSpeed + gap * 0.65f, PaceSpeed, 55f);
                else if (yielding[index])
                    target = Mathf.Clamp(PaceSpeed + gap * 0.65f, 12f, PaceSpeed - 4f);
                else
                {
                    float positionVariation = TrackState001RaceStart.PaceSpeedVariation(index, paceVariationTime,
                        PacePositionVariation) * paceBlend;
                    target = Mathf.Clamp(PaceSpeed + (gap + positionVariation) * 0.65f, 8f, 55f);
                }
            }
        }

        speeds[index] = Mathf.MoveTowards(speeds[index], target, (ControlsField ? 8f : 12f) * seconds);
        return speeds[index];
    }

    public void PreserveCautionOrder(TrackPitTraffic.Car[] cars, float[] stepSpeeds, float seconds)
    {
        if (!ReducedFuelBurn ||
            State != SessionTrackState.FinishUnderCaution && doubleFile && (!widenRestartFormationFirst || doubleFileSpacingBlend > 0f))
            return;

        // Let the small position variations close a gap while retaining ample
        // bumper clearance and the existing caution order.
        float minimumGap = RowSpacing - 2f * PacePositionVariation * paceVariationBlend;
        PreserveOrder(lapLength, stepSpeeds, seconds, carAhead, gapAhead,
            index => cars[index].TrackDistance, index => HoldsCautionPosition(index, cars, stepSpeeds), minimumGap);
    }

    internal static void PreserveOrder(float lapLength, float[] stepSpeeds, float seconds,
        int[] carAhead, float[] gapAhead, Func<int, float> distance, Func<int, bool> participates, float minimumGap = RowSpacing)
    {
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
                float limit = stepSpeeds[ahead] + clearance / seconds;
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
            pit.CallToStall(car, stall);
        return pit.PlannedSpeed(car, PaceSpeed, FinishStop(car, stall), seconds);
    }

    public void AdvanceFinishedCar(TrackPitTraffic.Car car, float seconds, float speed, float stall) =>
        pit.Advance(car, seconds, speed, FinishStop(car, stall));

    private static float FinishStop(TrackPitTraffic.Car car, float stall) =>
        car.InPit && car.PitDistance > stall ? float.PositiveInfinity : stall;

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
            caughtUp = true;
            for (int i = 0; i < cars.Length; i++)
            {
                if (cars[i].Retired)
                    continue;
                if (cars[i].InPit || Math.Abs(lead - FormationOffset(i) - cars[i].TrackDistance) > 5f)
                {
                    caughtUp = false;
                    break;
                }
            }
            PitOpen = caughtUp;
        }
        bool fieldSingleFile = !freePassInProgress && freePassLaneBlend == 0f;
        for (int i = 0; i < cars.Length; i++)
        {
            if (!cars[i].InPit && !cars[i].Retired && (yielding[i] || yieldLane[i] > 0f))
            {
                fieldSingleFile = false;
                break;
            }
        }
        if (lead >= doubleFileAt && fieldSingleFile)
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
        else if (lead >= restartAt)
        {
            restartAt += lapLength;
        }
    }

    public void RequestCaution(RaceSessionRun run, TrackPitTraffic.Car[] cars, int causingCarIndex = -1, string reason = null)
    {
        if (!CanRequestCaution)
            return;

        cautionCause = causingCarIndex;
        CautionReason = string.IsNullOrWhiteSpace(reason) ? "Track incident" : reason;
        int remaining = run.StageEnd - run.LeaderLaps;
        if (breaksStarted < run.BonusStageCount && remaining <= 10)
        {
            // Keep the pit-closure and one-to-go sequence before awarding stage points.
            earlyStageEnd = Math.Min(run.StageEnd, run.LeaderLaps + 2);
            run.EndStageAt(earlyStageEnd.Value);
            PrepareFormation(run, cars);
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
        foreach (int index in order)
        {
            float gap = leaderProgress - run.Progress(index);
            if (index != cautionCause && !run.Finished(index) && gap >= lapLength && gap < 2f * lapLength)
            {
                freePassCar = index;
                freePassInProgress = true;
                float physicalGap = cars[leader].TrackDistance - cars[index].TrackDistance;
                freePassLapOffset = (Mathf.Floor(physicalGap / lapLength) - 1f) * lapLength;
                break;
            }
        }
        cautionCause = -1;
        UpdatePitFormation(run, cars);
        State = SessionTrackState.Caution;
        cautionTime = 0f;
        sortingForRestart = false;
        Array.Clear(yielding, 0, yielding.Length);
        Array.Clear(yieldLane, 0, yieldLane.Length);
        pace = pit.CreateStalledCar(pit.ParkingDistance);
        paceMerged = false;
        paceReturning = false;
        doubleFileAt = lapAnchor + 3f * lapLength;
        peelOffAt = NextMarker(doubleFileAt, pit.EntryDistance);
        float restart = zoneStart + (zoneEnd - zoneStart) * (float)random.NextDouble();
        restartAt = NextMarker(peelOffAt + 0.01f, startLine + restart);
    }

    private void BeginStageBreak(RaceSessionRun run, TrackPitTraffic.Car[] cars)
    {
        breaksStarted++;
        freePassCar = -1;
        freePassInProgress = false;
        freePassLaneBlend = 0f;
        if (!earlyStageEnd.HasValue)
            CautionReason = "Stage break";
        earlyStageEnd = null;
        StageWhiteOverriddenByCaution = false;
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
            if (index == freePassCar || cars[index].InPit || cars[index].Retired)
                continue;

            ranks[index] = rank++;
            lapOffsets[index] = Mathf.Floor(Math.Max(0f, lead - cars[index].TrackDistance) / lapLength) * lapLength;
        }

        if (freePassCar >= 0)
        {
            // An odd slot puts the recipient at the rear of the outside restart line.
            ranks[freePassCar] = rank + (rank % 2 == 0 ? 1 : 0);
            lapOffsets[freePassCar] = freePassLapOffset;
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
