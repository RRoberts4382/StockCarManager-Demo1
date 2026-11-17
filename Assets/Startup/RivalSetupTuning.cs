using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
internal sealed class RivalAdjustmentCandidate
{
    public string controlId, controlName, units, rationale;
    public float beforeValue, afterValue, predictedBenefit, rankScore;
    public float[] predictedPhaseChanges;
    [NonSerialized] internal Setup setup;
}

// Engineering estimates for choosing a trial, NOT extra physics multipliers.
// Signs use positive=tight, negative=loose. Actual comparable laps decide acceptance.
internal static class RivalSetupTuning
{
    private static readonly string[] CornerNames = { "LF", "RF", "LR", "RR" };
    internal static List<RivalAdjustmentCandidate> Rank(Setup current, TrackDefinition track,
        ChassisType chassis, float[] balance, float[] temperatures)
    {
        var candidates = new List<RivalAdjustmentCandidate>();
        float entry = balance[0], middle = balance[1], exit = balance[2];
        bool transitions = Math.Abs(entry-middle) > 1.5f || Math.Abs(exit-middle) > 1.5f;
        void Add(string id, string name, string units, float value, float step, float min, float max,
            float[] effect, float preference, Action<Setup,float> apply, string why)
        {
            for (int direction = -1; direction <= 1; direction += 2)
            {
                float next = Mathf.Clamp(value + direction*step, min, max);
                // Do not silently snap legacy out-of-range setups by more than one step.
                float delta = next-value;
                if (Math.Abs(delta) < 0.00001f || Math.Abs(delta) > step+0.0001f) continue;
                float benefit = 0f;
                var phases = new float[3];
                for (int p = 0; p < 3; p++)
                {
                    phases[p] = effect[p]*delta/step;
                    float after = balance[p]+phases[p];
                    // Prefer reductions in the worst phase; penalize creating an opposite problem.
                    float improvement = balance[p]*balance[p]-after*after;
                    benefit += improvement;
                    if (Math.Abs(after) > Math.Abs(balance[p]) + .25f)
                        benefit += improvement; // Do not cure exit by making entry substantially worse.
                }
                benefit /= 3f;
                if (benefit <= 0.01f) continue;
                var trial = RivalSetup001.Copy(current);
                apply(trial,next);
                candidates.Add(new RivalAdjustmentCandidate {
                    controlId=id, controlName=name, units=units, beforeValue=value, afterValue=next,
                    predictedPhaseChanges=phases, predictedBenefit=benefit,
                    rankScore=benefit*preference, setup=trial, rationale=why });
            }
        }
        Add("track-bar", "Rear track bar", "in", current.rearTrackBar.RightMountHeightAboveGroundInches,
            .5f,5,14,new[]{-.4f,-.85f,-1.2f},1.15f,
            (s,v)=>s.rearTrackBar.rightMountHeightAboveGroundInches=v,
            "High-impact rear balance adjustment, strongest toward corner exit.");
        Add("cross-weight", "Cross weight", "%", current.weightDistribution.CrossPercent,
            .5f,WeightDistributionSetup.MinCross,WeightDistributionSetup.MaxCross,new[]{.85f,1f,.5f},1.12f,
            (s,v)=>s.weightDistribution.crossPercent=v,
            "High-impact diagonal load adjustment for entry and middle balance.");
        Add("front-weight", "Front weight", "%", current.weightDistribution.FrontPercent,
            .25f,WeightDistributionSetup.MinFront,WeightDistributionSetup.MaxFront,new[]{.45f,.25f,.1f},.8f,
            (s,v)=>s.weightDistribution.frontPercent=v,"Front/rear platform balance adjustment.");
        // Side bias is useful on ovals; avoid adding an oval bias to a road-course setup.
        if (track.majorStyle != "Road")
            Add("left-weight", "Left-side weight", "%", current.weightDistribution.LeftPercent,
                .25f,WeightDistributionSetup.MinLeft,WeightDistributionSetup.MaxLeft,new[]{-.1f,-.3f,-.15f},.75f,
                (s,v)=>s.weightDistribution.leftPercent=v,"Oval sustained-corner load distribution.");
        float sway = current.frontSwayBar.RatePoundsPerInch;
        Add("front-sway", "Front sway bar", "lb/in", sway,Math.Max(25,Mathf.Round(sway*.05f/25)*25),275,5000,
            new[]{.3f,.7f,.25f},.75f,(s,v)=>s.frontSwayBar.ratePoundsPerInch=v,
            "Front roll-platform change, strongest in the middle of the turn.");
        for (int corner = 0; corner < 4; corner++)
        {
            int c = corner;
            var part = Corner(current,c);
            bool front = c < 2;
            // Corner temperatures influence which tire gets attention, without claiming
            // a temperature change alone establishes that a pressure setting is faster.
            float heatPriority = temperatures?.Length == 4 ? Mathf.Lerp(.9f,1.2f,Mathf.InverseLerp(185,270,temperatures[c])) : 1;
            Add("pressure-"+c,CornerNames[c]+" cold tire pressure","PSI",VehicleSpeed003.ColdPressure(current,c),
                .5f,10,60,front ? new[]{.45f,.7f,.35f} : new[]{-.25f,-.5f,-.75f},heatPriority,
                (s,v)=>{ if(s.coldTirePressures==null || s.coldTirePressures.Length!=4) s.coldTirePressures=new float[4]; s.coldTirePressures[c]=v; },
                "Small contact-patch adjustment; applied with replacement tires.");
            var range = SetupLimits.SpringRange(chassis,c);
            Add("spring-"+c,CornerNames[c]+" spring","lb/in",part.spring.ratePoundsPerInch,
                25,range.Min,range.Max,front ? new[]{.15f,.4f,.15f} : new[]{-.1f,-.35f,-.3f},.65f,
                (s,v)=>Corner(s,c).spring.ratePoundsPerInch=v,"Mechanical platform change; small steps preserve the baseline.");
            Add("compression-"+c,CornerNames[c]+" shock compression","0-1",part.shock.compressionStiffness,
                .05f,0,1,front ? new[]{.65f,.05f,.05f} : new[]{-.05f,-.05f,-.65f},transitions ? 1.3f : .65f,
                (s,v)=>Corner(s,c).shock.compressionStiffness=v,"Transition-focused damping adjustment.");
            Add("rebound-"+c,CornerNames[c]+" shock rebound","0-1",part.shock.reboundStiffness,
                .05f,0,1,front ? new[]{.05f,.05f,.65f} : new[]{-.65f,-.05f,-.05f},transitions ? 1.3f : .65f,
                (s,v)=>Corner(s,c).shock.reboundStiffness=v,"Transition-focused extension control.");
        }
        candidates.Sort((a,b)=> { int rank=b.rankScore.CompareTo(a.rankScore); return rank!=0 ? rank : string.CompareOrdinal(a.controlId,b.controlId); });
        return candidates;
    }
    private static SuspensionCornerSetup Corner(Setup s,int c) => c==0?s.leftFront:c==1?s.rightFront:c==2?s.leftRear:s.rightRear;
}
