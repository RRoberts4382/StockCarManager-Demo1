using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

internal sealed class PracticeRunChart : VisualElement
{
    private readonly List<(GarageRunReview Run, int Number)> runs;
    internal sealed class Selection
    {
        internal readonly HashSet<int> Runs = new HashSet<int>();
        internal readonly bool[] Lines = { true, true, true, true, true, false, false, false, false };
        internal bool Initialized;
    }

    private readonly HashSet<int> selected;
    private readonly bool[] visibleSeries;
    private static readonly Color[] SeriesColors =
    {
        new Color(0.35f, 0.75f, 1f),
        new Color(1f, 0.48f, 0.35f),
        new Color(0.4f, 0.95f, 0.55f),
        new Color(0.85f, 0.55f, 1f),
        new Color(1f, 0.85f, 0.3f),
        new Color(0.60f, 0.85f, 1f),
        new Color(1f, 0.70f, 0.58f),
        new Color(0.65f, 1f, 0.75f),
        new Color(0.94f, 0.75f, 1f)
    };
    private readonly VisualElement plot = new VisualElement();
    private readonly VisualElement labels = new VisualElement();
    private readonly VisualElement runChoices = new VisualElement();
    private readonly Label runPageLabel = new Label();
    private int runPage;
    private int runsPerPage = 5;
    private readonly Label notice = new Label();
    private readonly Label snapshotStatus = new Label();
    private readonly Button snapshotButton;
    private readonly Label pointTooltip = new Label { pickingMode = PickingMode.Ignore };
    private VisualElement chartArea;
    private int lapCount = 1;
    private float minimumTime;
    private float maximumTime = 1f;
    private const float Left = 65f;
    private const float Right = 145f;
    private const float Top = 45f;
    private const float Bottom = 55f;
    private static readonly string[] Series = { "LF temp", "RF temp", "LR temp", "RR temp", "Lap time", "LF pressure", "RF pressure", "LR pressure", "RR pressure" };

    internal PracticeRunChart(RaceSession session, TrackDefinition track, Selection selection, Action back)
    {
        selected = selection.Runs;
        visibleSeries = selection.Lines;
        AddToClassList("practice-chart");
        var header = new VisualElement { style = { flexDirection = FlexDirection.Row, flexShrink = 0, alignItems = Align.Center } };
        header.Add(new Button(back) { text = "Return to Results" });
        snapshotButton = new Button(() =>
        {
            try
            {
                string path = GarageGraphSnapshot.Save(track, session,
                    selected.OrderBy(index => index).Select(index => runs[index]), visibleSeries);
                snapshotStatus.text = $"Saved {selected.Count} run(s). Path copied to clipboard.";
                snapshotStatus.tooltip = path;
                GUIUtility.systemCopyBuffer = path;
                Debug.Log("Graph snapshot saved: " + path);
            }
            catch (Exception error) when (error is System.IO.IOException ||
                error is UnauthorizedAccessException || error is InvalidOperationException)
            {
                snapshotStatus.text = "Could not save snapshot: " + error.Message;
            }
            snapshotStatus.style.display = DisplayStyle.Flex;
        }) { text = "Snapshot" };
        snapshotButton.tooltip = "Save all data for the checked runs, including hidden chart lines.";
        header.Add(snapshotButton);
        header.Add(new Label(session.name + " · Run comparison"));
        Add(header);
        snapshotStatus.style.display = DisplayStyle.None;
        snapshotStatus.style.whiteSpace = WhiteSpace.Normal;
        snapshotStatus.style.flexShrink = 0;
        Add(snapshotStatus);
        var row = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1, minHeight = 0 } };
        Add(row);
        var choices = new VisualElement { style = { width = 155, minWidth = 155, marginRight = 8, minHeight = 0 } };
        choices.Add(new Label("Runs"));
        row.Add(choices);
        runs = session.garageRuns.Select((run, i) => (Run: run, Number: i + 1))
            .Where(item => item.Run.complete).ToList();
        selected.RemoveWhere(index => index < 0 || index >= runs.Count);
        if (!selection.Initialized)
        {
            if (runs.Count > 0)
                selected.Add(runs.Count - 1);
            selection.Initialized = true;
        }
        choices.Add(runChoices);
        var pager = new VisualElement { style = { flexDirection = FlexDirection.Row } };
        pager.Add(new Button(() => { runPage = Math.Max(0, runPage - 1); BuildRunChoices(); }) { text = "‹" });
        pager.Add(runPageLabel);
        pager.Add(new Button(() => { runPage = Math.Min(Math.Max(0, (runs.Count - 1) / runsPerPage), runPage + 1); BuildRunChoices(); }) { text = "›" });
        choices.Add(pager);
        runPage = Math.Max(0, (runs.Count - 1) / runsPerPage);
        BuildRunChoices();
        choices.RegisterCallback<GeometryChangedEvent>(_ =>
        {
            int capacity = Math.Max(1, Mathf.FloorToInt((choices.contentRect.height - 355f) / 29f));
            if (capacity == runsPerPage)
                return;
            int first = Math.Max(0, Math.Min(runs.Count - 1, (runPage + 1) * runsPerPage - 1));
            runsPerPage = capacity;
            runPage = Math.Min(Math.Max(0, (runs.Count - 1) / runsPerPage), first / runsPerPage);
            BuildRunChoices();
        });
        choices.Add(new Label("Lines"));
        for (int series = 0; series < Series.Length; series++)
        {
            int selectedSeries = series;
            var check = new Toggle { text = Series[series], value = visibleSeries[series] };
            check.style.color = SeriesColors[series];
            check.RegisterValueChangedCallback(evt =>
            {
                visibleSeries[selectedSeries] = evt.newValue;
                Refresh();
            });
            choices.Add(check);
        }
        var right = new VisualElement { style = { flexGrow = 1, minWidth = 0, minHeight = 0 } };
        row.Add(right);
        chartArea = right;
        notice.style.whiteSpace = WhiteSpace.Normal;
        notice.style.flexShrink = 0;
        right.Add(notice);
        plot.style.flexGrow = 1;
        plot.style.flexBasis = 0;
        plot.style.minHeight = 0;
        plot.style.minWidth = 0;
        plot.style.backgroundColor = new Color(0.045f, 0.065f, 0.09f);
        labels.style.position = Position.Absolute;
        labels.style.left = labels.style.right = labels.style.top = labels.style.bottom = 0;
        labels.pickingMode = PickingMode.Ignore;
        plot.Add(labels);
        right.Add(plot);
        pointTooltip.style.position = Position.Absolute;
        pointTooltip.style.width = 290;
        pointTooltip.style.whiteSpace = WhiteSpace.Normal;
        pointTooltip.style.backgroundColor = new Color(0.02f, 0.03f, 0.045f, 0.98f);
        pointTooltip.style.color = Color.white;
        pointTooltip.style.paddingLeft = pointTooltip.style.paddingRight = 10;
        pointTooltip.style.paddingTop = pointTooltip.style.paddingBottom = 8;
        pointTooltip.style.display = DisplayStyle.None;
        right.Add(pointTooltip);
        plot.RegisterCallback<PointerMoveEvent>(ShowPointTooltip);
        plot.RegisterCallback<PointerLeaveEvent>(_ => HidePointTooltip());

        plot.generateVisualContent += Draw;
        plot.RegisterCallback<GeometryChangedEvent>(_ =>
        {
            HidePointTooltip();
            BuildLabels();
            plot.MarkDirtyRepaint();
        });
        Refresh();
    }

    private void BuildRunChoices()
    {
        runChoices.Clear();
        int end = Math.Min(runs.Count, (runPage + 1) * runsPerPage);
        for (int i = runPage * runsPerPage; i < end; i++)
        {
            int index = i;
            var check = new Toggle { text = $"Run {runs[i].Number} ({runs[i].Run.lapTimes.Count} laps)" };
            check.SetValueWithoutNotify(selected.Contains(i));
            check.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue) selected.Add(index);
                else selected.Remove(index);
                Refresh();
            });
            runChoices.Add(check);
        }
        runPageLabel.text = $"{runPage + 1}/{Math.Max(1, (runs.Count + runsPerPage - 1) / runsPerPage)}";
    }

    private Color SeriesColor(int run, int series)
    {
        // The newest selected run is brightest. Older selected runs share its hue.
        int shade = selected.Count(index => index > run);
        float brightness = Mathf.Max(0.45f, Mathf.Pow(0.86f, shade));
        Color color = SeriesColors[series] * brightness;
        color.a = 1f;
        return color;
    }

    private void Refresh()
    {
        HidePointTooltip();
        snapshotButton.SetEnabled(selected.Count > 0);
        var active = selected.Select(index => runs[index].Run).ToList();
        lapCount = Math.Max(1, active.Select(run => run.lapTimes.Count).DefaultIfEmpty(1).Max());
        var times = active.SelectMany(run => run.lapTimes).Where(time => time > 0f).ToList();
        minimumTime = times.Count > 0 ? Mathf.Floor(times.Min() - 0.5f) : 0f;
        maximumTime = times.Count > 0 ? Mathf.Ceil(times.Max() + 0.5f) : 1f;
        bool missingPressure = visibleSeries.Skip(5).Any(v => v) && active.Any(run =>
            Enumerable.Range(0, run.lapTimes.Count).Any(lap => !TryValue(run, lap, 5, out _)));
        bool missing = active.Any(run => run.lapTemperatures == null ||
            run.lapTemperatures.Count < run.lapTimes.Count);
        notice.text = selected.Count == 0 ? "Select a run to compare." :
            !visibleSeries.Any(visible => visible) ? "Select a line to display." :
            times.Count == 0 ? "The selected runs have no valid timed laps." :
            missingPressure ? "Some runs lack recorded pressures. Pressure lines use the PSI axis and dashed segments; new runs record all four tires." :
            missing ? "Earlier runs have lap times only. Per-lap tire temperatures are recorded on new runs." : "";
        notice.style.display = string.IsNullOrEmpty(notice.text) ? DisplayStyle.None : DisplayStyle.Flex;
        BuildLabels();
        plot.MarkDirtyRepaint();
    }

    private float X(int lap) => Left + (lapCount <= 1 ? 0.5f : lap / (float)(lapCount - 1)) *
        (plot.contentRect.width - Left - Right);
    private float Y(float normalized) => Top + (1f - normalized) * (plot.contentRect.height - Top - Bottom);

    private void BuildLabels()
    {
        labels.Clear();
        if (plot.contentRect.width <= Left + Right)
            return;
        LabelAt("Tire temperature (°F)", 5, 5, 220);
        LabelAt("Time (s)", plot.contentRect.width - Right + 4, 5, 75);
        LabelAt("PSI", plot.contentRect.width - 62, 5, 60);
        for (int tick = 0; tick <= 5; tick++)
        {
            float fraction = tick / 5f;
            LabelAt((fraction * 100f).ToString("0"), plot.contentRect.width - 60, Y(fraction) - 10, 55);
            LabelAt(Mathf.Lerp(75, 325, fraction).ToString("0"), 5, Y(fraction) - 10, 55);
            LabelAt(Mathf.Lerp(minimumTime, maximumTime, fraction).ToString("0.00"),
                plot.contentRect.width - Right + 10, Y(fraction) - 10, 65);
        }
        int labelStep = Math.Max(1, Mathf.CeilToInt(lapCount * 28f / (plot.contentRect.width - Left - Right)));
        for (int lap = 0; lap < lapCount; lap++)
        {
            if (lap % labelStep == 0 || lap == lapCount - 1)
                LabelAt((lap + 1).ToString(), X(lap) - 12, Y(0) + 8, 28);
        }
        LabelAt("Lap number", plot.contentRect.width * 0.5f - 50, plot.contentRect.height - 25, 110);
    }

    private void LabelAt(string text, float x, float y, float width)
    {
        var label = new Label(text);
        label.style.position = Position.Absolute;
        label.style.left = x;
        label.style.top = y;
        label.style.width = width;
        label.style.color = Color.white;
        label.style.fontSize = 14;
        labels.Add(label);
    }

    private void HidePointTooltip() => pointTooltip.style.display = DisplayStyle.None;

    private void ShowPointTooltip(PointerMoveEvent evt)
    {
        Vector2 pointer = plot.WorldToLocal(evt.position);
        float width = plot.contentRect.width - Left - Right;
        if (width <= 0f || pointer.y < Top - 8f || pointer.y > Y(0) + 8f)
        {
            HidePointTooltip();
            return;
        }
        int lap = lapCount <= 1 ? 0 : Mathf.RoundToInt((pointer.x - Left) / width * (lapCount - 1));
        if (lap < 0 || lap >= lapCount)
        {
            HidePointTooltip();
            return;
        }
        float nearest = 64f;
        var hits = new List<string>();
        foreach (int index in selected.OrderBy(index => index))
        {
            var run = runs[index].Run;
            if (lap >= run.lapTimes.Count)
                continue;
            for (int series = 0; series < Series.Length; series++)
            {
                if (!visibleSeries[series])
                    continue;
                if (!TryValue(run, lap, series, out float value)) continue;
                float normalized = Normalize(series, value);
                float distance = (pointer - new Vector2(X(lap), Y(normalized))).sqrMagnitude;
                if (distance > 64f || distance > nearest + 0.01f)
                    continue;
                if (distance < nearest - 0.01f)
                    hits.Clear();
                nearest = distance;
                string units = series == 4 ? "s" : series >= 5 ? "PSI" : "°F";
                hits.Add($"Run {runs[index].Number} · Lap {lap + 1}\n{Series[series]} {value:G9} {units}");
            }
        }
        if (hits.Count == 0)
        {
            HidePointTooltip();
            return;
        }
        pointTooltip.text = string.Join("\n\n", hits);
        pointTooltip.style.display = DisplayStyle.Flex;
        pointTooltip.BringToFront();
        Vector2 local = chartArea.WorldToLocal(evt.position);
        float tooltipWidth = Mathf.Min(290f, chartArea.contentRect.width);
        pointTooltip.style.width = tooltipWidth;
        pointTooltip.style.left = Mathf.Clamp(local.x + 14f, 0f,
            Mathf.Max(0f, chartArea.contentRect.width - tooltipWidth));
        float height = hits.Count * 52f + 16f;
        pointTooltip.style.top = Mathf.Clamp(local.y + 16f, 0f,
            Mathf.Max(0f, chartArea.contentRect.height - height));
    }

    // Pressure samples are captured in each lap's diagnostics. Older runs lack
    // these samples: omit them rather than inventing pressure from today's setup.
    internal static bool TryValue(GarageRunReview run, int lap, int series, out float value)
    {
        value = 0f;
        if (lap < 0 || lap >= run.lapTimes.Count) return false;
        if (series == 4) value = run.lapTimes[lap];
        else
        {
            if (run.lapTemperatures == null || lap >= run.lapTemperatures.Count) return false;
            var sample = run.lapTemperatures[lap];
            var values = series >= 5 ? sample?.diagnostics?.runningPressuresPsi : sample?.tires;
            if (values?.Length != 4) return false;
            value = values[series >= 5 ? series - 5 : series];
        }
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private float Normalize(int series, float value) => series == 4
        ? Mathf.InverseLerp(minimumTime, maximumTime, value)
        : series >= 5 ? Mathf.InverseLerp(0f, 100f, value) : Mathf.InverseLerp(75f, 325f, value);

    private void Draw(MeshGenerationContext context)
    {
        if (plot.contentRect.width <= Left + Right)
            return;
        var painter = context.painter2D;
        painter.lineWidth = 1;
        painter.strokeColor = new Color(0.25f, 0.3f, 0.36f);
        for (int tick = 0; tick <= 5; tick++)
        {
            painter.BeginPath();
            painter.MoveTo(new Vector2(Left, Y(tick / 5f)));
            painter.LineTo(new Vector2(plot.contentRect.width - Right, Y(tick / 5f)));
            painter.Stroke();
        }
        foreach (int index in selected.OrderBy(index => index))
        {
            var run = runs[index].Run;
            for (int series = 0; series < Series.Length; series++)
            {
                if (!visibleSeries[series])
                    continue;
                int count = series == 4 ? run.lapTimes.Count :
                    Math.Min(run.lapTimes.Count, run.lapTemperatures?.Count ?? 0);
                painter.strokeColor = SeriesColor(index, series);
                painter.fillColor = painter.strokeColor;
                painter.lineWidth = series == 4 ? 3f : 1.8f;
                bool started = false;
                painter.BeginPath();
                for (int lap = 0; lap < count; lap++)
                {
                    if (!TryValue(run, lap, series, out float value)) { started = false; continue; }
                    float normalized = Normalize(series, value);
                    var point = new Vector2(X(lap), Y(normalized));
                    if (!started) { painter.MoveTo(point); started = true; }
                    else if (series < 5 || lap % 2 == 1) painter.LineTo(point);
                    else painter.MoveTo(point);
                }
                painter.Stroke();
                // Mark each sample so one-lap runs are visible too.
                for (int lap = 0; lap < count; lap++)
                {
                    if (!TryValue(run, lap, series, out float value)) { started = false; continue; }
                    float normalized = Normalize(series, value);
                    float x = X(lap), y = Y(normalized);
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(x - 2, y - 2));
                    painter.LineTo(new Vector2(x + 2, y - 2));
                    painter.LineTo(new Vector2(x + 2, y + 2));
                    painter.LineTo(new Vector2(x - 2, y + 2));
                    painter.ClosePath();
                    painter.Fill();
                }
            }
        }
    }
}
