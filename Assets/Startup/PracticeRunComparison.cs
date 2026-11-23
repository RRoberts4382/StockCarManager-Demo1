using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

internal static class PracticeRunComparison
{
    internal static float? Mean(IReadOnlyList<float> laps, int start, int count)
    {
        if (laps == null || start < 0 || count <= 0 || start + count > laps.Count) return null;
        double sum = 0;
        for (int i = start; i < start + count; i++)
        {
            if (float.IsNaN(laps[i]) || float.IsInfinity(laps[i]) || laps[i] <= 0f) return null;
            sum += laps[i];
        }
        return (float)(sum / count);
    }

    internal static VisualElement Create(RaceSession session)
    {
        var runs = session.garageRuns.Select((run, index) => (Run: run, Number: index + 1))
            .Where(item => item.Run.complete).ToList();
        var panel = new VisualElement();
        panel.AddToClassList("practice-run-report");
        var heading = new Label("Run comparison summary");
        heading.AddToClassList("practice-run-title");
        panel.Add(heading);
        if (runs.Count < 2)
        {
            Text(panel, "Complete two runs to compare their pace and recorded conditions.");
            return panel;
        }
        var options = runs.Select(item => $"Run {item.Number} ({item.Run.lapTimes.Count} laps)").ToList();
        var controls = new VisualElement();
        controls.style.flexDirection = FlexDirection.Row;
        controls.style.flexWrap = Wrap.Wrap;
        var first = new DropdownField("Baseline", options, runs.Count - 2);
        var second = new DropdownField("Comparison", options, runs.Count - 1);
        first.style.flexGrow = second.style.flexGrow = 1;
        controls.Add(first); controls.Add(second); panel.Add(controls);
        var details = new VisualElement(); panel.Add(details);
        VisualElement rowsParent = details;
        first.RegisterValueChangedCallback(_ => Refresh());
        second.RegisterValueChangedCallback(_ => Refresh());
        Refresh();
        return panel;

        void Refresh()
        {
            details.Clear();
            rowsParent = details;
            if (first.index == second.index) { Text(details, "Select two different runs."); return; }
            var a = runs[first.index].Run; var b = runs[second.index].Run;
            Row("Metric", $"Run {runs[first.index].Number}", $"Run {runs[second.index].Number}", "Change (B - A)");
            Row("Run ended", a.ending, b.ending);
            Number("Timed laps", a.lapTimes.Count, b.lapTimes.Count, "0", "");
            Number("Best lap", a.lapTimes.Count > 0 ? a.lapTimes.Min() : (float?)null,
                b.lapTimes.Count > 0 ? b.lapTimes.Min() : (float?)null, "0.000", " s");
            Number("Whole-run average", Mean(a.lapTimes, 0, a.lapTimes.Count), Mean(b.lapTimes, 0, b.lapTimes.Count), "0.000", " s");
            int common = Math.Min(a.lapTimes.Count, b.lapTimes.Count);
            if (common > 0)
            {
                Number($"Matched average (laps 1-{common})", Mean(a.lapTimes, 0, common), Mean(b.lapTimes, 0, common), "0.000", " s");
                int tail = Math.Min(5, common);
                Number($"Late pace (laps {common - tail + 1}-{common})", Mean(a.lapTimes, common - tail, tail),
                    Mean(b.lapTimes, common - tail, tail), "0.000", " s");
            }
            Text(details, "Negative lap-time changes mean faster laps. Matched averages and late pace use the same lap numbers in both runs; pit laps are excluded by the recorder.");
            if (a.lapTimes.Count != b.lapTimes.Count)
                Text(details, "Run lengths differ. Whole-run averages and ending conditions cover different distances; use the matched lap window for a closer comparison.");
            for (int phase = 0; phase < 3; phase++)
            {
                string name = new[] { "Entry", "Middle", "Exit" }[phase];
                var ha = a.handling?.Length == 3 ? a.handling[phase] : null;
                var hb = b.handling?.Length == 3 ? b.handling[phase] : null;
                Row(name + " handling", ha?.Description ?? "Not recorded", hb?.Description ?? "Not recorded");
                if (a.lapTimes.Count >= 10 || b.lapTimes.Count >= 10)
                    Row(name + " long-run trend", HandlingRunTrend.Describe(a, phase), HandlingRunTrend.Describe(b, phase));
                Number(name + " balance (+ tight / - loose)", ha?.seconds >= 1d ? ha.Average : (float?)null,
                    hb?.seconds >= 1d ? hb.Average : (float?)null, "0.0", "");
            }
            var sa = a.startDiagnostics; var sb = b.startDiagnostics;
            var ea = a.endDiagnostics; var eb = b.endDiagnostics;
            var conditions = new Foldout { text = "Fuel, weather, tires and car condition", value = false };
            details.Add(conditions);
            rowsParent = conditions;
            Row("Equal-condition testing", sa == null ? null : sa.equalConditionTesting ? "On" : "Off", sb == null ? null : sb.equalConditionTesting ? "On" : "Off");
            Row("Chassis", sa?.chassis, sb?.chassis);
            Row("Speed / track / behavior models", Models(sa), Models(sb));
            Row("Collisions / traffic", Switches(sa), Switches(sb));
            Number("Starting speed multiplier", sa?.lapSpeedFactor, sb?.lapSpeedFactor, "0.0000", "");
            Number("Starting fuel", (float?)sa?.fuelGallons, (float?)sb?.fuelGallons, "0.00", " gal");
            Number("Ending fuel", (float?)ea?.fuelGallons, (float?)eb?.fuelGallons, "0.00", " gal");
            Number("Starting total weight", sa?.weightDynamicsEnabled == true ? sa.vehicleWeightPounds : (float?)null,
                sb?.weightDynamicsEnabled == true ? sb.vehicleWeightPounds : (float?)null, "0.0", " lb");
            Number("Starting damage", sa?.damagePercent, sb?.damagePercent, "0.0", "%");
            Number("Ending damage", ea?.damagePercent, eb?.damagePercent, "0.0", "%");
            Weather("Start", sa, sb); Weather("End", ea, eb);
            for (int tire = 0; tire < 4; tire++)
            {
                string corner = new[] { "LF", "RF", "LR", "RR" }[tire];
                Number(corner + " starting temperature", At(a.startingTemperatures, tire), At(b.startingTemperatures, tire), "0.0", " F");
                Number(corner + " ending temperature", At(a.endingTemperatures, tire), At(b.endingTemperatures, tire), "0.0", " F");
                Number(corner + " fitted cold pressure", At(sa?.coldPressuresPsi, tire), At(sb?.coldPressuresPsi, tire), "0.0", " PSI");
                Number(corner + " starting pressure", At(sa?.runningPressuresPsi, tire), At(sb?.runningPressuresPsi, tire), "0.0", " PSI");
                Number(corner + " ending pressure", At(ea?.runningPressuresPsi, tire), At(eb?.runningPressuresPsi, tire), "0.0", " PSI");
                Number(corner + " starting wear", At(sa?.tireWear, tire) * 100f, At(sb?.tireWear, tire) * 100f, "0.0", "%");
                Number(corner + " ending wear", At(ea?.tireWear, tire) * 100f, At(eb?.tireWear, tire) * 100f, "0.0", "%");
            }
            Text(conditions, "Ending conditions are recorded when the run ends, which can include the trip back to the pit stall.");
            rowsParent = details;
            var changes = new Foldout { text = "Setup and component differences", value = true };
            details.Add(changes);
            if (sa?.setup == null || sb?.setup == null)
                Text(changes, "Setup details were not recorded for one or both runs.");
            else
            {
                var left = SetupValues(sa.setup); var right = SetupValues(sb.setup);
                int count = 0;
                foreach (var entry in left)
                    if (Math.Abs(entry.Value - right[entry.Key]) > 0.0001f)
                    { Text(changes, $"{entry.Key}: {entry.Value:0.##} -> {right[entry.Key]:0.##}"); count++; }
                if (count == 0) Text(changes, "No differences in the recorded setup adjustments.");
            }
            if (sa?.componentNames != null && sb?.componentNames != null)
            {
                int changed = 0;
                foreach (var component in CarComponentDisplay.Order)
                {
                    string recordedName = component.ToString();
                    int leftIndex = Array.IndexOf(sa.componentNames, recordedName);
                    int rightIndex = Array.IndexOf(sb.componentNames, recordedName);
                    float? x = At(sa.componentEffectiveValues, leftIndex), y = At(sb.componentEffectiveValues, rightIndex);
                    if (x.HasValue && y.HasValue && Math.Abs(x.Value - y.Value) > 0.0001f)
                    {
                        Text(changes, $"{CarComponentDisplay.Name(component)} effective value: {x:0.000} -> {y:0.000}");
                        changed++;
                    }
                }
                if (changed == 0) Text(changes, "No changes found in recorded component effective values.");
            }
            else Text(changes, "Component values not recorded for one or both runs.");
            Text(details, "This summary identifies differences, not proof of their cause. Compare runs with the same fuel, tire condition and locked weather to isolate setup effects.");
        }

        void Weather(string when, GarageRunDiagnostics a, GarageRunDiagnostics b)
        {
            bool hasA = a?.weatherForecast != null, hasB = b?.weatherForecast != null;
            Number(when + " air temperature", hasA ? a.ambientFahrenheit : (float?)null, hasB ? b.ambientFahrenheit : (float?)null, "0.0", " F");
            Number(when + " track temperature", hasA ? a.trackFahrenheit : (float?)null, hasB ? b.trackFahrenheit : (float?)null, "0.0", " F");
            Number(when + " cloud cover", hasA ? a.cloudCover * 100f : (float?)null, hasB ? b.cloudCover * 100f : (float?)null, "0.0", "%");
            Number(when + " wind", hasA ? a.windMph : (float?)null, hasB ? b.windMph : (float?)null, "0.0", " mph");
            Row(when + " weather lock", hasA ? (a.weatherLocked ? "Locked" : "Unlocked") : null,
                hasB ? (b.weatherLocked ? "Locked" : "Unlocked") : null);
        }
        void Number(string label, float? a, float? b, string format, string unit)
        {
            string delta = a.HasValue && b.HasValue ?
                ((b.Value - a.Value > 0f ? "+" : "") + (b.Value - a.Value).ToString(format) + unit) : "--";
            Row(label, a.HasValue ? a.Value.ToString(format) + unit : null,
                b.HasValue ? b.Value.ToString(format) + unit : null, delta);
        }
        void Row(string label, string a, string b, string delta = "")
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row;
            row.style.flexShrink = 0; row.style.paddingTop = 4; row.style.paddingBottom = 4;
            string[] values = { label, a ?? "Not recorded", b ?? "Not recorded", delta };
            for (int i = 0; i < values.Length; i++)
            {
                var cell = new Label(values[i]); cell.style.width = Length.Percent(i == 0 ? 34 : 22);
                cell.style.minWidth = 0; cell.style.whiteSpace = WhiteSpace.Normal;
                cell.style.fontSize = 14; cell.style.paddingRight = 6; row.Add(cell);
            }
            rowsParent.Add(row);
        }
    }

    private static float? At(float[] values, int index) => values != null && index >= 0 && index < values.Length ? values[index] : (float?)null;
    private static string Models(GarageRunDiagnostics d) => d == null ? null : $"{d.speedModel} / {d.trackModel} / {d.behaviorModel}";
    private static string Switches(GarageRunDiagnostics d) => d == null ? null : $"{d.collisions} / {d.trafficInteractions}";
    private static void Text(VisualElement parent, string text)
    {
        var label = new Label(text); label.style.whiteSpace = WhiteSpace.Normal;
        label.style.fontSize = 14; label.style.marginTop = 5; label.style.marginBottom = 5;
        label.style.flexShrink = 0; parent.Add(label);
    }
    internal static Dictionary<string, float> SetupValues(Setup setup)
    {
        var values = new Dictionary<string, float>();
        var corners = new[] { setup.leftFront, setup.rightFront, setup.leftRear, setup.rightRear };
        var names = new[] { "LF", "RF", "LR", "RR" };
        for (int i = 0; i < 4; i++)
        {
            values[names[i] + " spring (lb/in)"] = corners[i]?.spring?.ratePoundsPerInch ?? (i < 2 ? 750f : 350f);
            values[names[i] + " cold pressure (PSI)"] = VehicleSpeed003.ColdPressure(setup, i);
            values[names[i] + " compression"] = corners[i]?.shock?.compressionStiffness ?? 0.5f;
            values[names[i] + " rebound"] = corners[i]?.shock?.reboundStiffness ?? 0.5f;
        }
        values["Front sway bar (lb/in)"] = setup.frontSwayBar?.RatePoundsPerInch ?? 1200f;
        values["Rear track bar (in)"] = setup.rearTrackBar?.RightMountHeightAboveGroundInches ?? 9.5f;
        values["Dry front weight (%)"] = setup.weightDistribution?.FrontPercent ?? 52f;
        values["Dry cross weight (%)"] = setup.weightDistribution?.CrossPercent ?? 50f;
        values["Dry left weight (%)"] = setup.weightDistribution?.LeftPercent ?? 50f;
        return values;
    }
}
