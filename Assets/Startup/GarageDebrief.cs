using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using static GarageSetupEditor;

internal static class GarageDebrief
{
    private static readonly string[] Phases = { "on corner entry", "mid-corner", "on corner exit" };
    private static readonly string[] Corners = { "Left front", "Right front", "Left rear", "Right rear" };

    internal static (int Phase, string Text)[] DriverComments(GarageRunReview run)
    {
        if (run?.handling?.Length != 3) return Array.Empty<(int, string)>();
        return run.handling.Select((sample, phase) => (Sample: sample, Phase: phase))
            .Where(item => item.Sample != null && item.Sample.seconds >= 1)
            .Select(item => (item.Phase, item.Sample.Description switch
            {
                "Tight" => $"The car felt tight {Phases[item.Phase]}.",
                "Loose" => $"The car felt loose {Phases[item.Phase]}.",
                "Balanced" => $"The car felt balanced {Phases[item.Phase]}.",
                _ => $"The balance shifted between tight and loose {Phases[item.Phase]}."
            })).ToArray();
    }

    internal static void Build(VisualElement report, RaceSession session, GarageRunReview run, GarageRunReview previousRun, int number, string series)
    {
        var header = Group(report, "garage-redesign-row");
        string name = session?.name ?? "Run Report";
        if (session?.kind == RaceSessionKind.Practice && run != null) name += $" - Run {number}";
        Text(header, name, "garage-redesign-title");
        var laps = run?.lapTimes?.ToArray() ?? Array.Empty<float>();
        Text(header, $"{laps.Length} {(laps.Length == 1 ? "Lap" : "Laps")}", "garage-redesign-count");

        var timing = Group(report, "garage-redesign-timing");
        var list = Group(timing, "garage-redesign-timing-column");
        Row(list, "Lap", "Time");
        var scroll = new ScrollView(ScrollViewMode.Vertical)
        {
            verticalScrollerVisibility = ScrollerVisibility.Hidden,
            horizontalScrollerVisibility = ScrollerVisibility.Hidden
        };
        scroll.AddToClassList("garage-redesign-laps");
        list.Add(scroll);
        float[] valid = laps.Where(lap => lap > 0 && float.IsFinite(lap)).ToArray();
        float best = valid.Length == 0 ? 0 : valid.Min();
        for (int i = 0; i < laps.Length; i++)
        {
            var row = Row(scroll, (i + 1).ToString(), Time(laps[i]));
            row.EnableInClassList("garage-redesign-best", best > 0 && laps[i] == best);
        }
        if (laps.Length == 0) Text(scroll, "No timed laps yet.", "garage-redesign-dialog-text");
        var metrics = Group(timing, "garage-redesign-timing-column");
        Metric(metrics, "Best lap", Time(best));
        Metric(metrics, "Average lap", Time(valid.Length == 0 ? 0 : valid.Average()));
        var chart = Group(metrics, "garage-redesign-chart");
        var previous = previousRun?.lapTimes?.ToArray() ?? Array.Empty<float>();
        var sharedTimes = valid.Concat(previous.Where(IsTimedLap)).ToArray();
        chart.tooltip = previous.Any(IsTimedLap)
            ? "Lap times. Solid blue shows the most recent run. Dashed dark blue shows the previous run."
            : "Lap times from the most recent run.";
        if (sharedTimes.Length > 0)
        {
            float minimum = sharedTimes.Min(), maximum = sharedTimes.Max();
            int lapCount = Math.Max(laps.Length, previous.Length);
            chart.generateVisualContent += ctx =>
            {
                var painter = ctx.painter2D;
                var bounds = chart.contentRect;
                Plot(painter, bounds, previous, lapCount, minimum, maximum, dashed: true);
                Plot(painter, bounds, laps, lapCount, minimum, maximum, dashed: false);
            };
        }

        for (int row = 0; row < 2; row++)
        {
            var tires = Group(report, "garage-redesign-tire-row");
            for (int side = 0; side < 2; side++)
            {
                int i = row * 2 + side;
                var cell = Group(tires, "garage-redesign-tire");
                var wheel = Gauge("garage-redesign-wheel", Wear(), (p, rect) =>
                    DrawWheel(p, rect, At(run?.endDiagnostics?.tireWear, i), series));
                var heat = Gauge("garage-redesign-gauge", WithUnit(run?.endingTemperatures, "°F", "Temperature"),
                    (p, rect) => DrawGauge(p, rect, At(run?.endingTemperatures, i), true));
                var pressure = Gauge("garage-redesign-gauge", WithUnit(run?.endDiagnostics?.runningPressuresPsi, " psi", "Running pressure"),
                    (p, rect) => DrawGauge(p, rect, At(run?.endDiagnostics?.runningPressuresPsi, i), false));
                cell.Add(side == 0 ? pressure : wheel);
                cell.Add(heat);
                cell.Add(side == 0 ? wheel : pressure);
                string Wear() => At(run?.endDiagnostics?.tireWear, i) is float value
                    ? $"{Corners[i]} wear: {value:P0}." : $"{Corners[i]} wear was not recorded.";
                string WithUnit(float[] values, string unit, string aspect) => At(values, i) is float value
                    ? $"{Corners[i]} {aspect.ToLowerInvariant()}: {value:0.0}{unit}." :
                      $"{Corners[i]} {aspect.ToLowerInvariant()} was not recorded.";
            }
        }

        var comments = Group(report, "garage-redesign-comments");
        Text(comments, "Driver Comments", "garage-redesign-heading");
        var feedback = DriverComments(run);
        foreach (var comment in feedback) Text(comments, comment.Text, "garage-redesign-comment");
        if (feedback.Length == 0) Text(comments, "No driver feedback recorded yet.", "garage-redesign-comment");
    }

    private static string Time(float value) => value > 0 && float.IsFinite(value) ? $"{value:0.000} s" : "—";
    private static float? At(float[] data, int index) =>
        data != null && index < data.Length && float.IsFinite(data[index]) ? data[index] : null;

    private static VisualElement Row(VisualElement parent, string lap, string time)
    {
        var row = Group(parent, "garage-redesign-lap-row");
        Text(row, lap, "garage-redesign-lap-number");
        Text(row, time, null);
        return row;
    }

    private static void Metric(VisualElement parent, string caption, string value)
    {
        var box = Group(parent, "garage-redesign-metric");
        Text(box, caption, null);
        Text(box, value, "garage-redesign-metric-value");
    }

    private static bool IsTimedLap(float lap) => lap > 0 && float.IsFinite(lap);

    private static void Plot(Painter2D painter, Rect bounds, float[] laps, int lapCount,
        float minimum, float maximum, bool dashed)
    {
        if (laps.Length == 0 || bounds.width <= 14 || bounds.height <= 14) return;

        const float dash = 5f, gap = 3f;
        painter.strokeColor = dashed ? new Color32(0, 102, 161, 255) : new Color32(0, 155, 221, 255);
        painter.lineWidth = 1.5f;
        painter.BeginPath();
        Vector2? last = null;
        float phase = 0;
        for (int i = 0; i < laps.Length; i++)
        {
            if (!IsTimedLap(laps[i]))
            {
                last = null;
                phase = 0;
                continue;
            }
            float ratio = Mathf.Approximately(minimum, maximum) ? .5f : Mathf.InverseLerp(minimum, maximum, laps[i]);
            var point = new Vector2(6 + (bounds.width - 12) * i / Math.Max(1, lapCount - 1),
                6 + (bounds.height - 12) * (1 - ratio));

            if (laps.Length == 1)
            {
                painter.MoveTo(point - Vector2.right * 2);
                painter.LineTo(point + Vector2.right * 2);
            }
            else if (!last.HasValue)
                painter.MoveTo(point);
            else if (!dashed)
                painter.LineTo(point);
            else
            {
                float distance = Vector2.Distance(last.Value, point);
                for (float offset = 0; offset < distance - .001f;)
                {
                    bool on = phase < dash;
                    float segment = Mathf.Min(distance - offset, (on ? dash : dash + gap) - phase);
                    if (on)
                    {
                        painter.MoveTo(Vector2.Lerp(last.Value, point, offset / distance));
                        painter.LineTo(Vector2.Lerp(last.Value, point, (offset + segment) / distance));
                    }
                    offset += segment;
                    phase = (phase + segment) % (dash + gap);
                }
            }
            last = point;
        }
        painter.Stroke();
    }

    private static VisualElement Gauge(string style, string tooltip, Action<Painter2D, Rect> draw)
    {
        var element = new VisualElement { tooltip = tooltip };
        element.AddToClassList(style);
        element.generateVisualContent += ctx =>
        {
            if (element.contentRect.width > 5 && element.contentRect.height > 5)
                draw(ctx.painter2D, element.contentRect);
        };
        return element;
    }

    private static void DrawGauge(Painter2D p, Rect rect, float? value, bool temperature)
    {
        float x = rect.width * .5f, h = rect.height - 13;
        if (h < 4) return;
        p.lineWidth = 6;
        if (temperature && value.HasValue)
        {
            for (int i = 0; i < 24; i++)
            {
                float sample = Mathf.Lerp(75, 325, (i + .5f) / 24);
                Color color = sample < 185 ? Color.Lerp(new Color(.12f, .4f, 1), new Color(.2f, .85f, .34f), Mathf.InverseLerp(135, 185, sample)) :
                    sample <= 220 ? new Color(.2f, .85f, .34f) : Color.Lerp(new Color(.2f, .85f, .34f), new Color(1, .2f, .25f), Mathf.InverseLerp(220, 270, sample));
                Line(p, color, x, h * (1f - i / 24f), x, h * (1f - (i + 1) / 24f));
            }
            float marker = h * (1 - Mathf.InverseLerp(75, 325, value.Value));
            p.lineWidth = 1.5f;
            Line(p, Color.white, x - 5, marker, x + 5, marker);
        }
        else
        {
            Line(p, new Color32(55, 67, 82, 255), x, 0, x, h);
            if (value.HasValue)
                Line(p, new Color32(0, 170, 225, 255), x, h, x, h * (1 - Mathf.InverseLerp(0, 65, value.Value)));
        }
        p.lineWidth = 1.2f;
        p.strokeColor = Color.white;
        p.BeginPath();
        if (temperature) p.Arc(new Vector2(x, h + 9), 2.5f, 0, 360);
        else p.Arc(new Vector2(x, h + 10), 4, 180, 360);
        p.Stroke();
        if (temperature) Line(p, Color.white, x, h + 8, x, h + 3);
        else Line(p, Color.white, x, h + 10, x + 2, h + 7);
    }

    private static void DrawWheel(Painter2D p, Rect rect, float? wear, string series)
    {
        var center = rect.center;
        float r = Mathf.Min(rect.width, rect.height) * .46f;
        void Disc(float radius, Color color)
        {
            p.fillColor = color;
            p.BeginPath();
            p.Arc(center, radius, 0, 360);
            p.ClosePath();
            p.Fill();
        }

        Disc(r, new Color32(43, 49, 58, 255));
        bool cup = series == "nascar-cup";
        bool steel = series == "oreilly-auto-parts" || series == "craftsman-truck";
        Disc(r * .70f, cup ? new Color32(168, 180, 192, 255) : new Color32(34, 38, 45, 255));
        Disc(r * .61f, cup ? new Color32(50, 58, 67, 255) : new Color32(64, 71, 80, 255));
        p.lineWidth = Mathf.Max(1, r * .07f);
        for (int i = 0; i < (cup ? 12 : steel ? 8 : 5); i++)
        {
            float a = Mathf.PI * 2 * i / (cup ? 12 : steel ? 8 : 5);
            var v = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            if (!cup)
            {
                var tangent = new Vector2(-v.y, v.x);
                Vector2 inner = center + v * r * .36f;
                Vector2 mid = center + v * r * .57f;
                p.fillColor = steel ? new Color32(8, 14, 22, 255) : new Color32(147, 146, 140, 255);
                p.BeginPath();
                p.MoveTo(inner - tangent * r * .15f);
                p.LineTo(inner + tangent * r * .15f);
                p.QuadraticCurveTo(mid + tangent * r * .22f, mid);
                p.QuadraticCurveTo(mid - tangent * r * .22f, inner - tangent * r * .15f);
                p.ClosePath();
                p.Fill();
            }
            else
            {
                Vector2 first = center + v * r * .18f;
                Vector2 last = center + v * r * .63f;
                Line(p, new Color32(204, 211, 220, 255), first.x, first.y, last.x, last.y);
            }
        }
        Disc(r * .24f, cup ? new Color32(172, 179, 188, 255) : new Color32(30, 34, 40, 255));
        if (!cup && !steel)
        {
            p.strokeColor = new Color32(207, 72, 46, 255);
            p.lineWidth = 1;
            p.BeginPath(); p.Arc(center, r * .67f, 0, 360); p.Stroke();
            for (int i = 0; i < 5; i++)
            {
                float a = Mathf.PI * 2 * i / 5;
                var lug = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r * .29f;
                p.fillColor = new Color32(239, 184, 60, 255);
                p.BeginPath(); p.Arc(lug, Mathf.Max(1, r * .06f), 0, 360); p.ClosePath(); p.Fill();
            }
        }
        if (wear.GetValueOrDefault() <= 0) return;
        p.strokeColor = new Color32(255, 183, 42, 255);
        p.lineWidth = r * .23f;
        p.BeginPath();
        p.Arc(center, r * .88f, -90, -90 + Mathf.Clamp01(wear.Value) * 359.99f);
        p.Stroke();
    }

    private static void Line(Painter2D p, Color color, float x1, float y1, float x2, float y2)
    {
        p.strokeColor = color;
        p.BeginPath();
        p.MoveTo(new Vector2(x1, y1));
        p.LineTo(new Vector2(x2, y2));
        p.Stroke();
    }
}
