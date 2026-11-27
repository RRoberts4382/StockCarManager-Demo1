using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Track-specific rival baselines, not an extra speed multiplier. Versioned saved
// presets keep repeated sessions reproducible and allow later feedback tuning.
internal static class RivalSetup001
{
    internal const string BestName = "Rival proven best v1";
    internal static Setup Copy(Setup setup) => JsonUtility.FromJson<Setup>(JsonUtility.ToJson(setup));
    internal static void Apply(Setup destination, Setup source)
    {
        // Preserve the session's Setup reference, but share no mutable members with the best preset.
        var copy = Copy(source);
        destination.notes = copy.notes;
        destination.coldTirePressures = copy.coldTirePressures;
        destination.weightDistribution = copy.weightDistribution;
        destination.leftFront = copy.leftFront; destination.rightFront = copy.rightFront;
        destination.leftRear = copy.leftRear; destination.rightRear = copy.rightRear;
        destination.frontSwayBar = copy.frontSwayBar; destination.rearTrackBar = copy.rearTrackBar;
    }
    internal static void EnsureBest(TeamCar car, int trackId, Setup setup)
    {
        if (car.trackSetups?.Exists(s => s != null && s.trackId == trackId && s.name == BestName && s.setup != null) != true)
            SaveBest(car, trackId, setup);
    }
    internal static void SaveBest(TeamCar car, int trackId, Setup setup)
    {
        car.trackSetups ??= new List<TrackSetup>();
        var saved = car.trackSetups.Find(p => p != null && p.trackId == trackId && p.name == BestName);
        if (saved == null) { saved = new TrackSetup { trackId = trackId, name = BestName }; car.trackSetups.Add(saved); }
        saved.setup = Copy(setup);
    }
    internal const string PresetName = "Rival baseline v1";
    internal static Setup ForTrack(TeamCar car, TrackDefinition track, out bool baseline)
    {
        car.trackSetups ??= new List<TrackSetup>();
        var best = car.trackSetups.Find(p => p != null && p.trackId == track.id && p.name == BestName && p.setup != null);
        baseline = best == null;
        if (best != null) return Copy(best.setup);
        var saved = car.trackSetups.Find(p => p != null && p.trackId == track.id && p.name == PresetName && p.setup != null);
        if (saved != null) return Copy(saved.setup);
        var setup = Create(track, car.chassisNumber ?? car.number.ToString());
        car.trackSetups.Add(new TrackSetup { trackId = track.id, name = PresetName, setup = Copy(setup) });
        return setup;
    }

    internal static void WidenOvalBaselines(List<Setup> setups)
    {
        // Older presets may leave fields missing and rely on the speed model's defaults.
        setups.RemoveAll(setup => setup?.coldTirePressures?.Length != 4 ||
            setup.weightDistribution == null || setup.frontSwayBar == null || setup.rearTrackBar == null ||
            !HasSpringAndShock(setup.leftFront) || !HasSpringAndShock(setup.rightFront) ||
            !HasSpringAndShock(setup.leftRear) || !HasSpringAndShock(setup.rightRear));
        if (setups.Count < 2)
            return;

        const float spread = 6f;
        bool HasSpringAndShock(SuspensionCornerSetup corner) => corner?.spring != null && corner.shock != null;
        void Widen(Func<Setup, float> read, Action<Setup, float> write, float min, float max)
        {
            float[] values = setups.Select(read).Where(float.IsFinite).OrderBy(value => value).ToArray();
            if (values.Length < 2)
                return;
            int middle = values.Length / 2;
            float center = (values[(values.Length - 1) / 2] + values[middle]) * .5f;
            foreach (var setup in setups)
            {
                float value = read(setup);
                if (float.IsFinite(value))
                    write(setup, Mathf.Clamp(center + spread * (value - center), min, max));
            }
        }

        for (int i = 0; i < 4; i++)
        {
            int corner = i;
            Widen(s => s.coldTirePressures[corner], (s, v) => s.coldTirePressures[corner] = v, 10f, 60f);
            Widen(s => VehicleSpeed003.CornerSetup(s, corner).spring.ratePoundsPerInch,
                (s, v) => VehicleSpeed003.CornerSetup(s, corner).spring.ratePoundsPerInch = v, 100f, 2000f);
            Widen(s => VehicleSpeed003.CornerSetup(s, corner).shock.compressionStiffness,
                (s, v) => VehicleSpeed003.CornerSetup(s, corner).shock.compressionStiffness = v, 0f, 1f);
            Widen(s => VehicleSpeed003.CornerSetup(s, corner).shock.reboundStiffness,
                (s, v) => VehicleSpeed003.CornerSetup(s, corner).shock.reboundStiffness = v, 0f, 1f);
        }
        Widen(s => s.frontSwayBar.RatePoundsPerInch, (s, v) => s.frontSwayBar.ratePoundsPerInch = v, 275f, 5000f);
        Widen(s => s.rearTrackBar.RightMountHeightAboveGroundInches,
            (s, v) => s.rearTrackBar.rightMountHeightAboveGroundInches = v, 5f, 14f);
        Widen(s => s.weightDistribution.frontPercent, (s, v) => s.weightDistribution.frontPercent = v,
            WeightDistributionSetup.MinFront, WeightDistributionSetup.MaxFront);
        Widen(s => s.weightDistribution.crossPercent, (s, v) => s.weightDistribution.crossPercent = v,
            WeightDistributionSetup.MinCross, WeightDistributionSetup.MaxCross);
        Widen(s => s.weightDistribution.leftPercent, (s, v) => s.weightDistribution.leftPercent = v,
            WeightDistributionSetup.MinLeft, WeightDistributionSetup.MaxLeft);
    }

    internal static Setup Create(TrackDefinition track, string chassis, bool nominal = false)
    {
        uint state = 2166136261u;
        foreach (char c in track.id + ":" + chassis) state = unchecked((state ^ c) * 16777619u);
        float Variation(float amount)
        {
            if (nominal)
                return 0f;

            state = unchecked(state * 1664525u + 1013904223u);
            return ((state >> 8) / 16777215f * 2f - 1f) * amount;
        }
        float Round(float value, float step) => Mathf.Round(value / step) * step;
        var setup = new Setup { notes = "Automatically prepared track baseline. RivalSetup001 v1." };
        var corners = new[] { setup.leftFront, setup.rightFront, setup.leftRear, setup.rightRear };
        bool road = track.majorStyle == "Road";
        bool shortTrack = track.lengthMiles <= 1f;
        float roughness = Mathf.Clamp01(track.weights?.surfaceRoughness ?? 0.5f);
        for (int i = 0; i < 4; i++)
        {
            float target = VehicleSpeed003.TargetSpring(track, i);
            float rate = Mathf.Clamp(Round(target * (1f + Variation(0.06f)), 25f), 200f, 1400f);
            corners[i].spring.ratePoundsPerInch = rate;
            float springRatio = Mathf.Clamp(rate / target - 1f, -1f, 1f);
            corners[i].shock.compressionStiffness = Mathf.Clamp01(Round(0.48f + springRatio * 0.12f - (roughness - 0.5f) * 0.18f + Variation(0.04f), 0.01f));
            corners[i].shock.reboundStiffness = Mathf.Clamp01(Round(0.56f + springRatio * 0.14f - (roughness - 0.5f) * 0.12f + Variation(0.04f), 0.01f));
            // Start near the speed model's calibrated cold-pressure references.
            setup.coldTirePressures[i] = Mathf.Clamp(Round((i % 2 == 0 ? 28f : 46f) + Variation(1f), 0.1f), 10f, 60f);
        }
        setup.frontSwayBar.ratePoundsPerInch = Mathf.Clamp(Round(VehicleSpeed003.TargetSwayBar(track, setup) * (1f + Variation(0.08f)), 25f), 275f, 5000f);
        float height = road ? 8.5f : track.restrictorPlate ? 9.5f : shortTrack ? 9f : 10f;
        setup.rearTrackBar.rightMountHeightAboveGroundInches = Mathf.Clamp(Round(height + Mathf.Lerp(0.75f, -0.75f, roughness) + Variation(0.35f), 0.1f), 5f, 14f);
        setup.weightDistribution.frontPercent = Mathf.Clamp(Round((road ? 51f : shortTrack ? 50.5f : 52f) + Variation(0.5f), 0.1f), WeightDistributionSetup.MinFront, WeightDistributionSetup.MaxFront);
        setup.weightDistribution.crossPercent = Mathf.Clamp(Round((road ? 50f : 51f) + Variation(0.5f), 0.1f), WeightDistributionSetup.MinCross, WeightDistributionSetup.MaxCross);
        setup.weightDistribution.leftPercent = Mathf.Clamp(Round((road ? 50f : 55.5f) + Variation(0.5f), 0.1f), WeightDistributionSetup.MinLeft, WeightDistributionSetup.MaxLeft);
        return setup;
    }
}
