using System;

public sealed partial class TrackPreview
{
    private enum IowaStandSignal { None, Yellow, Green, White, StageComplete, Finish }

    private const float IowaFinalFlagHoldSeconds = .20f;
    private const double IowaPacingYellowApproachMeters = 80d;
    private readonly double[] iowaFlagStartDistances = new double[CarCount];
    private bool iowaFlagStandEnabled;
    private IowaStandSignal iowaOfficialFlag;
    private IowaStandSignal iowaPendingFlag;
    private IowaStandSignal iowaDisplayedFlag;
    private ulong iowaFlagSeenMask;
    private ulong iowaFlagRemainingMask;
    private float iowaFlagWaveSeconds;
    private float iowaFlagLowerAt = -1f;

    private void ResetIowaFlagStandTiming()
    {
        iowaOfficialFlag = IowaStandSignal.None;
        iowaPendingFlag = IowaStandSignal.None;
        iowaDisplayedFlag = IowaStandSignal.None;
        iowaFlagSeenMask = 0;
        iowaFlagRemainingMask = 0;
        iowaFlagWaveSeconds = 0f;
        iowaFlagLowerAt = -1f;
    }

    private void AdvanceRaceStartStep()
    {
        if (iowaFlagStandEnabled)
            for (int i = 0; i < cars.Length; i++)
                iowaFlagStartDistances[i] = IowaFlagTrackDistance(i);

        AdvanceRaceStartStepCore();
        AdvanceRichmondPaceBay();
        if (iowaFlagStandEnabled)
            AdvanceIowaFlagPresentation();
    }

    private void AdvanceIowaFlagPresentation()
    {
        IowaStandSignal signal = IowaRaceFlag();
        if (signal != iowaOfficialFlag)
        {
            // A newer race-control call supersedes any older flag. One-to-go lowers it immediately.
            ResetIowaFlagStandTiming();
            iowaOfficialFlag = signal;
            iowaPendingFlag = signal;
            if (signal != IowaStandSignal.None)
                for (int i = 0; i < cars.Length; i++)
                    if (IsIowaFlagRecipient(i))
                        iowaFlagRemainingMask |= 1UL << i;
        }

        if (iowaPendingFlag == IowaStandSignal.None && iowaDisplayedFlag == IowaStandSignal.None)
            return;

        if (iowaDisplayedFlag != IowaStandSignal.None)
            iowaFlagWaveSeconds += SessionStep;

        double stripe = definition.StartFinishFraction * lapLength;
        bool pacingYellow = !raceReleased && iowaOfficialFlag == IowaStandSignal.Yellow &&
            raceStart.State == SessionTrackState.Pacing;
        for (int i = 0; i < cars.Length; i++)
        {
            ulong car = 1UL << i;
            if ((iowaFlagRemainingMask & car) == 0)
                continue;

            if (!IsIowaFlagRecipient(i))
                iowaFlagRemainingMask &= ~car;
            else if (ReachedFlag(i))
            {
                iowaFlagSeenMask |= car;
                iowaFlagRemainingMask &= ~car;
                if (iowaPendingFlag != IowaStandSignal.None)
                {
                    iowaDisplayedFlag = iowaPendingFlag;
                    iowaPendingFlag = IowaStandSignal.None;
                }
            }
        }

        if (iowaFlagRemainingMask != 0)
            return;

        iowaPendingFlag = IowaStandSignal.None;
        if (iowaDisplayedFlag == IowaStandSignal.None)
            return;

        if (iowaFlagLowerAt < 0f)
            iowaFlagLowerAt = iowaFlagWaveSeconds + IowaFinalFlagHoldSeconds;
        else if (iowaFlagWaveSeconds >= iowaFlagLowerAt)
            iowaDisplayedFlag = IowaStandSignal.None;

        bool ReachedFlag(int index)
        {
            double distance = IowaFlagTrackDistance(index);
            if (!pacingYellow)
                return CrossesFlagStand(iowaFlagStartDistances[index], distance, stripe, lapLength);

            // The pace car reaches the stripe before the field and starts one-to-go.
            // Show the first yellow to drivers on approach while pacing is still active.
            double nextStripe = stripe + Math.Ceiling((distance - stripe) / lapLength) * lapLength;
            return nextStripe - distance <= IowaPacingYellowApproachMeters;
        }
    }

    private IowaStandSignal IowaRaceFlag()
    {
        if (OneToGoBeforeGreen)
            return IowaStandSignal.None;

        return TrackState switch
        {
            SessionTrackState.Pacing or SessionTrackState.Caution or SessionTrackState.StageEndingUnderCaution => IowaStandSignal.Yellow,
            SessionTrackState.TrackOpen => IowaStandSignal.Green,
            SessionTrackState.WhiteFlag when stages?.StageWhiteOverriddenByCaution == true => IowaStandSignal.Yellow,
            SessionTrackState.WhiteFlag => IowaStandSignal.White,
            SessionTrackState.StageComplete => IowaStandSignal.StageComplete,
            SessionTrackState.Checkered or SessionTrackState.FinishUnderCaution => IowaStandSignal.Finish,
            _ => IowaStandSignal.None
        };
    }

    private bool IsIowaFlagRecipient(int index)
    {
        if (!cars[index].gameObject.activeSelf || SessionRun.Retired(index))
            return false;
        if (!raceReleased)
            return true;

        var car = trafficCars[index];
        return !car.Retired && !car.InGarage &&
            (!car.InPit || !SessionRun.Finished(index) && !(index == 0 && playerGarageVisit && PlayerParkedForGarage));
    }

    private double IowaFlagTrackDistance(int index)
    {
        if (!raceReleased)
        {
            var grid = raceStart.GridPosition(index, 0f);
            return grid.OnPitRoad ? double.NaN : grid.Distance;
        }

        var car = trafficCars[index];
        // Pit-route scoring progress can advance past the stripe without passing the stand.
        return car.InPit ? double.NaN : car.PreciseTrackDistance;
    }

    private static bool CrossesFlagStand(double before, double after, double stripe, float length) =>
        after > before && Math.Floor((after - stripe) / length) > Math.Floor((before - stripe) / length);
}
