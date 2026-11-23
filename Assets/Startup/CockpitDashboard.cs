using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

internal sealed class CockpitDashboard
{
    private const float TachometerMaxRpm = 10000f;
    private enum GaugeSource { Rpm, Fuel, Throttle, Brake }
    private readonly List<(GaugeSource Source, Gauge Display)> gauges = new();
    private readonly VisualElement panel;
    private CarTypeDefinition carType;

    public CockpitDashboard(VisualElement panel) => this.panel = panel;

    public void Configure(CarTypeDefinition definition)
    {
        if (carType == definition)
            return;

        carType = definition;
        gauges.Clear();
        panel.Clear();
        var image = SeriesSettingsStore.LoadArtwork(definition.dashboardImage);
        panel.style.backgroundImage = image == null ? new StyleBackground(StyleKeyword.Null) : new StyleBackground(image);
        if (image == null && definition.dashboardSwitchRows > 0)
            AddSwitchPanel();

        foreach (var gauge in definition.gauges ?? Array.Empty<CockpitGaugeDefinition>())
        {
            if (gauge == null || !Enum.TryParse<GaugeSource>(gauge.source, true, out var source) ||
                !Enum.IsDefined(typeof(GaugeSource), source))
                continue;

            float redline = source == GaugeSource.Rpm ? FourSpeedTransmission.RedlineRpm / TachometerMaxRpm : float.PositiveInfinity;
            gauges.Add((source, new Gauge(panel, gauge.label ?? gauge.source, gauge.large, redline, definition.darkDashboardDials)));
        }
    }

    private void AddSwitchPanel()
    {
        var switches = new VisualElement { pickingMode = PickingMode.Ignore };
        switches.AddToClassList("cockpit-switch-panel");
        switches.generateVisualContent += DrawSwitches;
        panel.Add(switches);
    }

    public void Update(double rpm, float fuelFraction, double gallons, float throttleInput, float brakeInput)
    {
        foreach (var gauge in gauges)
        {
            var reading = gauge.Source switch
            {
                GaugeSource.Rpm => ((float)(rpm / TachometerMaxRpm), $"{rpm:0}"),
                GaugeSource.Fuel => (fuelFraction, $"{gallons:0.0}"),
                GaugeSource.Throttle => (throttleInput, $"{throttleInput:P0}"),
                _ => (brakeInput, $"{brakeInput:P0}")
            };
            gauge.Display.Set(reading.Item1, reading.Item2);
        }
    }

    private void DrawSwitches(MeshGenerationContext context)
    {
        Rect rect = context.visualElement.contentRect;
        if (rect.width < 16f || rect.height < 16f)
            return;

        var painter = context.painter2D;
        var steel = new Color32(168, 170, 165, 255);
        float offset = rect.width * 0.2f;
        int rows = Mathf.Clamp(carType.dashboardSwitchRows, 1, 2);
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                var center = new Vector2(offset + col * rect.width * 0.3f, rect.height * (rows == 1 ? 0.55f : 0.34f + row * 0.34f));
                Disc(painter, center, 5.4f, steel);
                Disc(painter, center, 4f, new Color32(48, 49, 48, 255));
                var tip = center + new Vector2(row == 0 ? 1.5f : -2.5f, row == 0 ? -7f : 5f);
                painter.strokeColor = steel;
                painter.lineWidth = 3.2f;
                painter.BeginPath();
                painter.MoveTo(center);
                painter.LineTo(tip);
                painter.Stroke();
                Disc(painter, tip, 2.1f, row == rows - 1 && col == 0 ? new Color32(213, 72, 42, 255) : steel);
            }
        }
        Disc(painter, new Vector2(6f, 6f), 2f, steel);
        Disc(painter, new Vector2(rect.width - 6f, rect.height - 6f), 2f, steel);
    }

    private static void Disc(Painter2D painter, Vector2 center, float radius, Color color)
    {
        painter.fillColor = color;
        painter.BeginPath();
        painter.Arc(center, radius, Angle.Degrees(0f), Angle.Degrees(360f));
        painter.ClosePath();
        painter.Fill();
    }

    private sealed class Gauge
    {
        private readonly VisualElement dial;
        private readonly VisualElement needle;
        private readonly Label valueLabel;
        private readonly float redline;
        private readonly bool darkDial;
        private float fraction;

        public Gauge(VisualElement panel, string name, bool large, float redline, bool darkDial)
        {
            this.redline = redline;
            this.darkDial = darkDial;
            dial = new VisualElement { pickingMode = PickingMode.Ignore };
            dial.AddToClassList("cockpit-gauge");
            dial.EnableInClassList("cockpit-gauge-main", large);
            dial.EnableInClassList("cockpit-gauge-dark", darkDial);
            dial.generateVisualContent += DrawFace;
            needle = new VisualElement { pickingMode = PickingMode.Ignore };
            needle.AddToClassList("cockpit-gauge-needle");
            needle.generateVisualContent += DrawNeedle;
            dial.Add(needle);
            var labels = new VisualElement { pickingMode = PickingMode.Ignore };
            labels.AddToClassList("cockpit-gauge-labels");
            var nameLabel = new Label(name) { pickingMode = PickingMode.Ignore };
            nameLabel.AddToClassList("cockpit-gauge-name");
            labels.Add(nameLabel);
            valueLabel = new Label("0") { pickingMode = PickingMode.Ignore };
            valueLabel.AddToClassList("cockpit-gauge-value");
            labels.Add(valueLabel);
            dial.Add(labels);
            panel.Add(dial);
        }

        public void Set(float normalized, string value)
        {
            normalized = Mathf.Clamp01(normalized);
            if (!Mathf.Approximately(normalized, fraction))
            {
                fraction = normalized;
                needle.MarkDirtyRepaint();
            }
            valueLabel.text = value;
        }

        private void DrawFace(MeshGenerationContext context)
        {
            Rect rect = dial.contentRect;
            float radius = Mathf.Min(rect.width, rect.height) * 0.5f - 1f;
            if (radius <= 6f)
                return;

            var center = rect.center;
            var painter = context.painter2D;
            Disc(painter, center, radius, new Color32(155, 160, 159, 255));
            Disc(painter, center, radius * 0.92f, new Color32(60, 64, 66, 255));
            Disc(painter, center, radius * 0.84f, darkDial ? new Color32(26, 28, 29, 255) : new Color32(221, 219, 199, 255));
            painter.strokeColor = darkDial ? new Color32(231, 223, 200, 255) : new Color32(44, 46, 43, 255);
            painter.lineWidth = 1.2f;
            painter.BeginPath();
            for (int i = 0; i <= 20; i++)
            {
                float tick = i / 20f;
                if (tick >= redline && (i - 1) / 20f < redline)
                {
                    painter.Stroke();
                    painter.strokeColor = new Color32(191, 47, 33, 255);
                    painter.BeginPath();
                }
                Vector2 direction = Direction(tick);
                painter.MoveTo(center + direction * radius * 0.78f);
                painter.LineTo(center + direction * radius * (i % 5 == 0 ? 0.58f : 0.69f));
            }
            painter.Stroke();
        }

        private void DrawNeedle(MeshGenerationContext context)
        {
            Rect rect = needle.contentRect;
            float radius = Mathf.Min(rect.width, rect.height) * 0.5f - 1f;
            if (radius <= 6f)
                return;

            Vector2 direction = Direction(fraction);
            Vector2 side = new Vector2(-direction.y, direction.x);
            var center = rect.center;
            var painter = context.painter2D;
            painter.fillColor = new Color32(191, 47, 33, 255);
            painter.BeginPath();
            painter.MoveTo(center + direction * radius * 0.72f);
            painter.LineTo(center - direction * radius * 0.16f + side * radius * 0.045f);
            painter.LineTo(center - direction * radius * 0.16f - side * radius * 0.045f);
            painter.ClosePath();
            painter.Fill();
            Disc(painter, center, radius * 0.1f, new Color32(53, 54, 52, 255));
        }

        private static Vector2 Direction(float fraction)
        {
            float angle = Mathf.Lerp(135f, 405f, fraction) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }
    }
}

internal sealed class CockpitSteeringWheel : VisualElement
{
    private float angle;

    public CockpitSteeringWheel()
    {
        pickingMode = PickingMode.Ignore;
        AddToClassList("cockpit-steering-wheel");
        generateVisualContent += Draw;
    }

    public void SetAngle(float degrees)
    {
        if (Mathf.Approximately(angle, degrees))
            return;
        angle = degrees;
        style.rotate = new Rotate(Angle.Degrees(degrees));
    }

    private static void Draw(MeshGenerationContext context)
    {
        var rect = context.visualElement.contentRect;
        float radius = Mathf.Min(rect.width, rect.height) * 0.5f - 10f;
        if (radius <= 0f)
            return;

        var painter = context.painter2D;
        Vector2 center = rect.center;
        for (int spoke = 0; spoke < 3; spoke++)
        {
            float direction = (-30f + spoke * 120f) * Mathf.Deg2Rad;
            Vector2 end = center + new Vector2(Mathf.Cos(direction), Mathf.Sin(direction)) * radius;
            Stroke(painter, center, end, 13f, new Color32(56, 61, 62, 255));
            Stroke(painter, center, end, 4f, new Color32(119, 126, 126, 255));
        }

        Circle(painter, center, radius, 17f, new Color32(13, 15, 17, 255));
        Circle(painter, center, radius + 6f, 2f, new Color32(68, 70, 69, 255));
        Circle(painter, center, 17f, 8f, new Color32(86, 90, 90, 255));
        Circle(painter, center, 12f, 15f, new Color32(24, 27, 28, 255));
        Vector2 marker = center + Vector2.down * radius;
        Stroke(painter, marker + Vector2.down * 6f, marker + Vector2.up * 6f, 7f,
            new Color32(235, 170, 67, 255));
    }

    private static void Circle(Painter2D painter, Vector2 center, float radius, float width, Color color)
    {
        painter.strokeColor = color;
        painter.lineWidth = width;
        painter.BeginPath();
        painter.Arc(center, radius, Angle.Degrees(0f), Angle.Degrees(360f));
        painter.ClosePath();
        painter.Stroke();
    }

    private static void Stroke(Painter2D painter, Vector2 start, Vector2 end, float width, Color color)
    {
        painter.strokeColor = color;
        painter.lineWidth = width;
        painter.BeginPath();
        painter.MoveTo(start);
        painter.LineTo(end);
        painter.Stroke();
    }
}

