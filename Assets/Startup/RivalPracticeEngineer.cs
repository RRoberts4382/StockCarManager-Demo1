using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[Serializable]
internal sealed class RivalSetupChange
{
    public int carNumber, sessionLap;
    public float sessionSeconds, entryBalance, middleBalance, exitBalance, beforeSwayBar, afterSwayBar;
    public string reason;
    public string serviceLocation;
    public bool betweenSessions;
    public float setupWorkSeconds, serviceRequestedSeconds, serviceCompletedSeconds;
    public string adjustment, adjustmentId, units;
    public float beforeValue, afterValue;
    public Setup beforeSetup, afterSetup;
    public RivalAdjustmentCandidate[] rankedCandidates;
    public string evaluatedAdjustmentId;
    public string[] exhaustedAdjustmentIds;
    public Setup comparisonSetup;
    public int comparableLaps;
    public float referenceLapSeconds, candidateLapSeconds, lapGainSeconds;
}

// One variable at a time; decisions are evaluated on track and applied only in the stall.
internal sealed class RivalPracticeEngineer
{
    private readonly HandlingPhaseReview[] feedback = { new HandlingPhaseReview(), new HandlingPhaseReview(), new HandlingPhaseReview() };
    private int startLap;
    private bool requested, stopped, evaluating, decisionReady;
    private float previousScore;
    private SetupServicePlan servicePlan;
    private float serviceRequestedSeconds;
    private bool betweenSessions;
    private Setup previousSetup, pendingSetup, pendingBaseline;
    private float[] previousFeedback, pendingFeedback;
    private RivalAdjustmentCandidate trial, pendingTrial;
    private RivalAdjustmentCandidate[] rankedCandidates;
    private string pendingReason, evaluatedAdjustmentId;
    private GarageRunReview referenceRun, pendingReference;
    private readonly HashSet<string> exhausted = new HashSet<string>();
    private int comparableLaps;
    private float referenceLapSeconds, candidateLapSeconds;
    internal const float MinimumLapGain = 0.03f;

    // Interpolate reference times at candidate fuel levels. Tire temperature and wear
    // remain measured outcomes, rather than being normalized away by the comparison.
    internal static bool ComparePace(GarageRunReview reference, GarageRunReview candidate,
        out int count, out float referenceSeconds, out float candidateSeconds)
    {
        count = 0; referenceSeconds = candidateSeconds = 0f;
        if (reference?.startDiagnostics == null || candidate?.startDiagnostics == null) return false;
        var a = reference.startDiagnostics; var b = candidate.startDiagnostics;
        if (a.speedModel != b.speedModel || a.trafficInteractions || b.trafficInteractions ||
            a.collisions || b.collisions || Math.Abs(a.fuelGallons - b.fuelGallons) > 0.25) return false;
        for (int t = 0; t < 4; t++)
            if (a.tireWear[t] > 0.01f || b.tireWear[t] > 0.01f) return false;
        int limit = Math.Min(8, Math.Min(candidate.lapTimes.Count, candidate.lapTemperatures.Count));
        for (int i = 2; i < limit; i++)
        {
            var c = candidate.lapTemperatures[i].diagnostics;
            if (c == null || c.damagePercent > 0.01f || !Running(c.flag)) continue;
            for (int j = 2; j < Math.Min(10, Math.Min(reference.lapTimes.Count, reference.lapTemperatures.Count)); j++)
            {
                var r0 = reference.lapTemperatures[j-1].diagnostics;
                var r1 = reference.lapTemperatures[j].diagnostics;
                if (r0 == null || r1 == null || r0.fuelGallons <= r1.fuelGallons ||
                    c.fuelGallons > r0.fuelGallons || c.fuelGallons < r1.fuelGallons) continue;
                if (!Comparable(r0,c) || !Comparable(r1,c)) break;
                float f = (float)((r0.fuelGallons-c.fuelGallons)/(r0.fuelGallons-r1.fuelGallons));
                referenceSeconds += reference.lapTimes[j-1] + (reference.lapTimes[j]-reference.lapTimes[j-1])*f;
                candidateSeconds += candidate.lapTimes[i]; count++; break;
            }
        }
        if (count > 0) { referenceSeconds /= count; candidateSeconds /= count; }
        return count >= 4;
    }
    private static bool Running(string flag) => flag == "Running" || flag == "TrackOpen" || flag == "WhiteFlag";
    private static bool Comparable(GarageRunDiagnostics a, GarageRunDiagnostics b)
    {
        if (!Running(a.flag) || a.damagePercent > 0.01f ||
            Math.Abs(a.trackFahrenheit-b.trackFahrenheit)>3f || Math.Abs(a.ambientFahrenheit-b.ambientFahrenheit)>3f ||
            Math.Abs(a.cloudCover-b.cloudCover)>.1f || Math.Abs(a.windMph-b.windMph)>2f ||
            Math.Abs(a.lapSpeedFactor-b.lapSpeedFactor)>.0001f) return false;
        if (a.componentEffectiveValues == null || b.componentEffectiveValues == null ||
            a.componentEffectiveValues.Length != b.componentEffectiveValues.Length) return false;
        for (int i=0;i<a.componentEffectiveValues.Length;i++)
            if (Math.Abs(a.componentEffectiveValues[i]-b.componentEffectiveValues[i])>.0001f) return false;
        return true;
    }
    private void Record(int index, RaceSessionRun run, RaceSession session, Setup before, Setup after, string reason, RivalAdjustmentCandidate adjustment, bool reverting = false)
    {
        session.rivalSetupChanges.Add(new RivalSetupChange {
            carNumber = run.Vehicle(index).number, sessionLap = run.CompletedLaps(index), sessionSeconds = run.Elapsed,
            serviceLocation = betweenSessions || servicePlan.Garage ? "Garage" : "Pit stall",
            betweenSessions = betweenSessions,
            setupWorkSeconds = servicePlan.Seconds, serviceRequestedSeconds = serviceRequestedSeconds,
            serviceCompletedSeconds = run.Elapsed,
            entryBalance = feedback[0].Average, middleBalance = feedback[1].Average, exitBalance = feedback[2].Average,
            beforeSwayBar = before.frontSwayBar.RatePoundsPerInch, afterSwayBar = after.frontSwayBar.RatePoundsPerInch, reason = reason,
            adjustment = adjustment?.controlName, adjustmentId = adjustment?.controlId, units = adjustment?.units,
            beforeValue = adjustment == null ? 0 : reverting ? adjustment.afterValue : adjustment.beforeValue,
            afterValue = adjustment == null ? 0 : reverting ? adjustment.beforeValue : adjustment.afterValue,
            beforeSetup = RivalSetup001.Copy(before), afterSetup = RivalSetup001.Copy(after), rankedCandidates = rankedCandidates,
            evaluatedAdjustmentId = evaluatedAdjustmentId,
            exhaustedAdjustmentIds = exhausted.OrderBy(id => id).ToArray(),
            comparisonSetup = previousSetup == null ? null : RivalSetup001.Copy(previousSetup),
            comparableLaps = comparableLaps, referenceLapSeconds = referenceLapSeconds,
            candidateLapSeconds = candidateLapSeconds, lapGainSeconds = referenceLapSeconds-candidateLapSeconds });
        startLap = run.CompletedLaps(index);
        for (int i = 0; i < 3; i++) feedback[i] = new HandlingPhaseReview();
    }

    private float[] Balances() => new[] { feedback[0].Average, feedback[1].Average, feedback[2].Average };
    private static float Severity(float[] values) => (Math.Abs(values[0])+Math.Abs(values[1])+Math.Abs(values[2]))/3f;

    // Select from the retained baseline, never from a rejected trial. A rollback and
    // the next one-setting trial may share a stop, but their reference remains unchanged.
    private bool Decide(int index, RaceSessionRun run, GarageRunReview latest, TrackDefinition track)
    {
        var setup = run.SetupForCar(index);
        pendingSetup = null; pendingTrial = null; pendingBaseline = null;
        servicePlan = default; serviceRequestedSeconds = run.Elapsed; betweenSessions = false;
        rankedCandidates = null; evaluatedAdjustmentId = null;
        comparableLaps = 0; referenceLapSeconds = candidateLapSeconds = 0;
        if (stopped) { pendingReason="Monitoring only; remaining useful adjustments exhausted or comparison unavailable."; return true; }
        if (latest == null || latest.lapTimes.Count < 8) return false;
        var basis = setup;
        var basisRun = latest;
        var basisFeedback = Balances();
        string outcome = "";
        bool rollback = false;
        if (evaluating)
        {
            evaluatedAdjustmentId = trial.controlId;
            bool valid = ComparePace(referenceRun,latest,out comparableLaps,out referenceLapSeconds,out candidateLapSeconds);
            float gain = referenceLapSeconds-candidateLapSeconds;
            if (!valid || gain < 0f || Severity(basisFeedback) > previousScore+.5f)
            {
                basis=previousSetup; basisRun=referenceRun; basisFeedback=previousFeedback;
                rollback=true;
                if (!valid)
                {
                    stopped=true;
                    outcome="Comparable data unavailable; restore proven setup and suspend tuning.";
                }
                else
                {
                    exhausted.Add(trial.controlId);
                    outcome=(gain<0 ? "Slower trial" : "Worse handling")+": revert "+trial.controlName+"; try next ranked subcomponent. ";
                }
            }
            else
            {
                RivalSetup001.SaveBest(run.Vehicle(index),track.id,setup);
                if (gain < MinimumLapGain)
                {
                    exhausted.Add(trial.controlId);
                    outcome="Lap-time plateau: retain small gain from "+trial.controlName+"; try next ranked subcomponent. ";
                }
                else outcome="Comparable pace improved; retained "+trial.controlName+". ";
            }
        }
        RivalSetup001.EnsureBest(run.Vehicle(index),track.id,basis);
        if (!stopped)
        {
            var ranked=RivalSetupTuning.Rank(basis,track,run.Vehicle(index).chassisType,basisFeedback,basisRun.endingTemperatures);
            rankedCandidates=ranked.ToArray();
            pendingTrial=ranked.FirstOrDefault(c=>!exhausted.Contains(c.controlId));
            if (pendingTrial==null)
            {
                stopped=true;
                outcome+="No remaining useful subcomponent adjustments; tuning stopped.";
            }
        }
        pendingReason=outcome;
        if (pendingTrial!=null)
        {
            pendingSetup=pendingTrial.setup;
            pendingBaseline=RivalSetup001.Copy(basis);
            pendingReference=basisRun;
            pendingFeedback=(float[])basisFeedback.Clone();
            pendingReason+=$"Trial {pendingTrial.controlName}: {pendingTrial.beforeValue:0.##} -> {pendingTrial.afterValue:0.##} {pendingTrial.units}. "+pendingTrial.rationale;
        }
        else if (rollback) pendingSetup=RivalSetup001.Copy(basis);
        return true;
    }

    private void ApplyDecision(int index, RaceSessionRun run, RaceSession session, string prefix = "")
    {
        var setup=run.SetupForCar(index);
        var before=RivalSetup001.Copy(setup);
        if (pendingSetup!=null) RivalSetup001.Apply(setup,pendingSetup);
        Record(index,run,session,before,setup,prefix+pendingReason,
            pendingTrial ?? (evaluatedAdjustmentId!=null ? trial : null), pendingSetup!=null && pendingTrial==null);
        if (pendingTrial!=null)
        {
            trial=pendingTrial;
            previousSetup=pendingBaseline;
            referenceRun=pendingReference;
            previousFeedback=pendingFeedback;
            previousScore=Severity(previousFeedback);
            evaluating=true;
        }
        else evaluating=false;
        requested=false; decisionReady=false;
    }

    internal void FinishPractice(int index, RaceSessionRun run, RaceSession session,
        RivalPracticeTelemetry telemetry, TrackDefinition track, ScheduledRace scheduledEvent)
    {
        // Do not bypass unfinished timed work when the practice clock expires.
        if (requested || run.SetupLocked || run.Retired(index) || telemetry.runs.Count==0) return;
        var last=telemetry.runs[telemetry.runs.Count-1];
        if (!decisionReady)
        {
            if (last.handling?.Length==3 && Array.TrueForAll(last.handling,p=>p!=null && p.seconds>=5d))
                for(int p=0;p<3;p++) feedback[p]=last.handling[p];
            else if (!stopped) return;
            if (!Decide(index,run,last,track)) return;
        }
        servicePlan = SetupServicePlan.Between(run.SetupForCar(index), pendingSetup);
        serviceRequestedSeconds = run.Elapsed;
        betweenSessions = true; // Existing final review happens between sessions, not during practice time.
        ApplyDecision(index,run,session,pendingTrial==null ? "Practice ended: " : "Practice ended: final unverified garage trial. ");
        scheduledEvent?.StoreEventSetup(run.Vehicle(index),run.SetupForCar(index));
        stopped=true;
    }

    internal void Observe(int index, RaceSessionRun run, TrackPitTraffic.Car car, float stall,
        SessionPitStops pits, bool green, float seconds, RaceSession session, RivalPracticeTelemetry telemetry, TrackDefinition track)
    {
        if (car==null || run.Retired(index) || run.SetupLocked) return;
        if (requested) return; // The service completion callback applies the change.
        if (!decisionReady)
        {
            if(car.InPit || !green) return;
            if(run.TryHandlingFeedback(index,out int phase,out float balance,out float tires,out float loads))
                feedback[phase].Add(seconds,balance,tires,loads);
            if(run.CompletedLaps(index)-startLap<10) return;
            for(int p=0;p<3;p++) if(feedback[p].seconds<5d) return;
            var latest=telemetry.runs.Count>0 ? telemetry.runs[telemetry.runs.Count-1] : null;
            if(!Decide(index,run,latest,track)) return;
            decisionReady=true;
        }
        if(pendingSetup==null) { ApplyDecision(index,run,session); return; }
        servicePlan = SetupServicePlan.Between(run.SetupForCar(index), pendingSetup);
        // Budget work plus a minute for the pit transit; defer to the between-session review otherwise.
        if(run.Remaining < servicePlan.Seconds + 12f + 60f) return;
        serviceRequestedSeconds = run.Elapsed;
        requested=pits.RequestRivalSetupStop(run,index,servicePlan,
            () => ApplyDecision(index,run,session));
    }
}
