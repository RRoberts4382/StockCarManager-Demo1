using System;
using UnityEngine;
using UnityEngine.UIElements;

internal static class ShopComponentPanel
{
    private enum ActionIcon { Upgrade, Repair, Cancel }

    public static void Show(VisualElement panel, TeamCar car, DateTime? date, DateTime? nextRaceDate, Func<ComponentRepair, Image> repairPortrait,
        Func<CarComponent, bool> canUpgrade, long balance, Action<CarComponent> upgrade, Action<CarComponent> openRepair,
        Action<ComponentRepair> cancelRepair, RacingSeries series, bool showService = true,
        bool compactMetrics = false)
    {
        panel.Clear();
        panel.style.display = car == null ? DisplayStyle.None : DisplayStyle.Flex;
        if (car == null)
            return;

        var rows = panel;
        if (panel.name == "shopCarComponents")
        {
            rows = AddElement(panel, "shop-components-group");
            rows.AddToClassList("team-home-panel");
            SettingsFormStyle.StylePanel(rows);
            rows.Add(SettingsFormStyle.Label("Components", "team-home-heading"));
        }
        foreach (var component in TeamCar.Components)
        {
            var condition = car.GetComponent(component);
            var row = AddElement(rows, "shop-component-row");
            row.EnableInClassList("shop-wear-blocked", condition.BlocksRaceEntry);
            row.Add(ComponentIcon(component));

            var metrics = compactMetrics ? AddElement(row, "shop-component-metrics") : row;
            var level = AddElement(metrics, "shop-component-metric");
            string levelText = $"Level {condition.Level}/{TeamCar.MaximumComponentLevel}";
            if (compactMetrics)
                level.tooltip = levelText;
            else
                AddLabel(level, levelText, "shop-component-caption");
            var segments = AddElement(level, "shop-component-level");
            for (int i = 0; i < TeamCar.MaximumComponentLevel; i++)
            {
                var segment = AddElement(segments, "shop-component-segment");
                segment.EnableInClassList("shop-component-segment-filled", i < condition.Level);
            }

            var wear = AddElement(metrics, "shop-component-metric");
            string wearText = $"Wear {condition.Wear:P0}";
            if (compactMetrics)
            {
                wear.AddToClassList("event-component-wear-metric");
                wear.tooltip = wearText;
            }
            else
                AddLabel(wear, wearText, "shop-component-caption");
            AddMeter(wear, condition.Wear, "shop-component-wear");

            if (!showService)
                continue;

            var service = AddElement(row, "shop-component-service");
            var repair = car.repairs?.Find(job => job.component == component);
            var buttons = AddElement(row, "shop-component-actions");
            var upgradeLine = AddElement(buttons, "shop-component-action-line");
            if (upgrade != null && repair == null && condition.Level < TeamCar.MaximumComponentLevel)
            {
                var upgradeButton = AddActionButton(upgradeLine, $"Upgrade {component}", ActionIcon.Upgrade, () => upgrade(component));
                upgradeButton.SetEnabled(canUpgrade(component));
                AddLabel(upgradeLine, RaceFinances.Dollars(series.componentUpgradeCost), "shop-component-action-cost");
            }
            var repairLine = AddElement(buttons, "shop-component-action-line");
            if (repair != null && cancelRepair != null)
            {
                AddActionButton(repairLine, $"Cancel {component} {repair.WorkDescription.ToLowerInvariant()}", ActionIcon.Cancel, () => cancelRepair(repair))
                    .SetEnabled(date.HasValue);
            }
            else if (repair == null && openRepair != null && condition.Wear > 0f)
            {
                int repairCost = ComponentRepair.CostForWear(condition.Wear, series.maximumComponentRepairCost);
                bool affordable = balance >= repairCost;
                AddActionButton(repairLine, $"Repair {component}", ActionIcon.Repair, () => openRepair(component))
                    .SetEnabled(affordable);
                AddLabel(repairLine, RaceFinances.Dollars(repairCost), "shop-component-action-cost");
            }
            if (repair == null)
            {
                AddLabel(service, "No Work In Progress", "shop-component-caption");
                continue;
            }

            var portrait = repairPortrait?.Invoke(repair);
            if (portrait != null)
                service.Add(portrait);
            var details = AddElement(service, "shop-component-repair-details");
            AddLabel(details, repair.AssigneeDisplay, "shop-component-mechanic");
            AddRepairProgress(details, repair, date, nextRaceDate);
        }
    }

    public static VisualElement ComponentIcon(CarComponent component)
    {
        var icon = new VisualElement { tooltip = component.ToString() };
        icon.AddToClassList("shop-component-icon");
        icon.generateVisualContent += context => DrawIcon(context.painter2D, component);
        return icon;
    }

    public static bool AddRepairProgress(VisualElement parent, ComponentRepair repair, DateTime? date, DateTime? nextRaceDate)
    {
        if (!Career.TryReadDate(repair.completionDate, out var completionDate))
        {
            AddLabel(parent, "Repair underway", "shop-component-caption");
            return false;
        }

        bool late = nextRaceDate.HasValue && completionDate > nextRaceDate.Value;
        if (date.HasValue)
        {
            string remaining = ComponentRepair.FormatDuration(repair.RemainingMinutes(date.Value));
            AddLabel(parent, $"{remaining} remaining", "shop-component-caption");
            var meter = AddMeter(parent, 1f - repair.RemainingFraction(date.Value), "shop-component-progress");
            if (late)
            {
                meter.AddToClassList("shop-component-repair-late");
                meter.tooltip = "This repair is due after the next race.";
            }
        }
        else
            AddLabel(parent, "Repair underway", "shop-component-caption");
        return late;
    }

    private static Button AddActionButton(VisualElement row, string tooltip, ActionIcon actionIcon, Action action)
    {
        var button = new Button(action) { tooltip = tooltip };
        button.AddToClassList("settings-button");
        button.AddToClassList("shop-component-action-button");
        row.Add(button);
        var icon = AddElement(button, "shop-component-action-icon");
        icon.pickingMode = PickingMode.Ignore;
        icon.generateVisualContent += context =>
        {
            var painter = context.painter2D;
            painter.strokeColor = icon.resolvedStyle.color;
            painter.lineWidth = 1.7f;
            painter.BeginPath();
            if (actionIcon == ActionIcon.Upgrade)
            {
                painter.MoveTo(new Vector2(3, 8));
                painter.LineTo(new Vector2(8, 3));
                painter.LineTo(new Vector2(13, 8));
                painter.MoveTo(new Vector2(8, 3));
                painter.LineTo(new Vector2(8, 14));
            }
            else if (actionIcon == ActionIcon.Cancel)
            {
                painter.MoveTo(new Vector2(3, 3));
                painter.LineTo(new Vector2(13, 13));
                painter.MoveTo(new Vector2(13, 3));
                painter.LineTo(new Vector2(3, 13));
            }
            else
            {
                painter.MoveTo(new Vector2(12, 2));
                painter.LineTo(new Vector2(9, 3));
                painter.LineTo(new Vector2(8, 6));
                painter.LineTo(new Vector2(2, 12));
                painter.LineTo(new Vector2(4, 14));
                painter.LineTo(new Vector2(10, 8));
                painter.LineTo(new Vector2(13, 7));
                painter.LineTo(new Vector2(14, 4));
                painter.LineTo(new Vector2(12, 6));
                painter.LineTo(new Vector2(10, 4));
                painter.LineTo(new Vector2(12, 2));
            }
            painter.Stroke();
        };
        return button;
    }

    private static VisualElement AddMeter(VisualElement parent, float fraction, string fillStyle)
    {
        var track = AddElement(parent, "shop-component-meter");
        var fill = AddElement(track, fillStyle);
        fill.style.width = Length.Percent(Mathf.Clamp01(fraction) * 100f);
        if (fillStyle == "shop-component-wear") ComponentWearGradient.Apply(fill, fraction);
        return track;
    }

    private static VisualElement AddElement(VisualElement parent, string styleClass)
    {
        var element = new VisualElement();
        element.AddToClassList(styleClass);
        parent.Add(element);
        return element;
    }

    private static void AddLabel(VisualElement parent, string text, string styleClass)
    {
        var label = new Label(text);
        label.AddToClassList(styleClass);
        parent.Add(label);
    }

    internal static Color ComponentColor(CarComponent component) => component switch
    {
        CarComponent.Engine => new Color32(255, 193, 99, 255),
        CarComponent.Transmission => new Color32(104, 195, 255, 255),
        CarComponent.Brakes => new Color32(255, 125, 135, 255),
        CarComponent.Suspension => new Color32(115, 220, 162, 255),
        CarComponent.Aerodynamics => new Color32(198, 164, 255, 255),
        _ => new Color32(190, 222, 239, 255)
    };

    private static void DrawIcon(Painter2D painter, CarComponent component)
    {
        painter.strokeColor = ComponentColor(component);
        painter.lineWidth = 2f;
        painter.BeginPath();
        switch (component)
        {
            case CarComponent.Engine:
                Line(4, 14, 11, 14, 15, 9, 26, 9, 26, 13, 32, 13, 32, 19, 37, 19, 37, 29, 31, 29,
                    27, 33, 14, 33, 10, 28, 4, 28, 4, 14);
                Line(2, 17, 2, 25);
                Line(17, 15, 23, 15, 20, 21, 26, 21, 18, 28);
                break;
            case CarComponent.Transmission:
                Line(3, 20, 9, 20, 14, 14, 26, 14, 32, 19, 32, 22, 38, 22, 38, 28, 32, 28, 25, 35, 14, 35,
                    9, 29, 3, 29, 3, 20);
                Line(16, 20, 16, 29);
                Line(22, 18, 22, 31);
                Line(28, 21, 28, 28);
                Line(20, 14, 20, 10, 24, 7);
                Circle(26, 5, 2);
                break;
            case CarComponent.Brakes:
                Circle(19, 21, 14);
                Circle(19, 21, 5);
                Line(28, 10, 34, 9, 38, 15, 38, 25, 34, 30, 29, 28);
                Line(12, 14, 14, 13);
                Line(12, 28, 14, 29);
                Line(17, 11, 20, 11);
                break;
            case CarComponent.Suspension:
                Line(10, 7, 31, 7);
                Line(10, 34, 31, 34);
                Line(13, 7, 13, 11, 28, 14, 13, 18, 28, 22, 13, 26, 28, 30, 28, 34);
                Line(20, 4, 20, 7);
                Line(20, 34, 20, 38);
                break;
            case CarComponent.Aerodynamics:
                // Low nose, sloping windshield, short rear deck, and raised spoiler.
                Line(2, 24, 3, 20, 12, 18, 18, 12, 25, 12, 31, 17, 35, 18, 35, 15, 38, 15,
                    38, 21, 39, 23, 38, 28, 35, 28, 34, 25, 32, 24, 29, 24, 27, 26, 27, 28,
                    14, 28, 13, 25, 11, 24, 8, 24, 6, 26, 6, 28, 2, 28);
                painter.ClosePath();
                painter.fillColor = painter.strokeColor;
                painter.Fill();
                return;
        }
        painter.Stroke();

        void Line(params int[] coordinates)
        {
            painter.MoveTo(new Vector2(coordinates[0], coordinates[1]));
            for (int i = 2; i < coordinates.Length; i += 2)
                painter.LineTo(new Vector2(coordinates[i], coordinates[i + 1]));
        }

        void Circle(int x, int y, int radius)
        {
            painter.MoveTo(new Vector2(x + radius, y));
            painter.Arc(new Vector2(x, y), radius, Angle.Degrees(0), Angle.Degrees(360));
        }
    }
}

// Shared by Home, Cars, and Shop component wear meters.
internal static class ComponentWearGradient
{
    public static Color ColorAt(float wear) => wear <= 0.5f
        ? Color.Lerp(new Color32(73, 200, 115, 255), new Color32(255, 211, 73, 255), Mathf.Clamp01(wear * 2f))
        : Color.Lerp(new Color32(255, 211, 73, 255), new Color32(239, 79, 79, 255), Mathf.Clamp01((wear - 0.5f) * 2f));

    public static void Apply(VisualElement fill, float wear, bool vertical = false, bool reverse = false)
    {
        float amount = Mathf.Clamp01(wear);
        fill.style.backgroundColor = Color.clear;
        fill.generateVisualContent += context =>
        {
            var rect = fill.contentRect;
            if (rect.width <= 0 || rect.height <= 0 || amount <= 0) return;
            const int segments = 24;
            var mesh = context.Allocate((segments + 1) * 2, segments * 6);
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                Color32 tint = ColorAt(reverse ? 1f - t * amount : t * amount);
                if (vertical)
                {
                    float y = rect.yMax - rect.height * t;
                    mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMin, y, Vertex.nearZ), tint = tint });
                    mesh.SetNextVertex(new Vertex { position = new Vector3(rect.xMax, y, Vertex.nearZ), tint = tint });
                }
                else
                {
                    float x = rect.x + rect.width * t;
                    mesh.SetNextVertex(new Vertex { position = new Vector3(x, rect.yMin, Vertex.nearZ), tint = tint });
                    mesh.SetNextVertex(new Vertex { position = new Vector3(x, rect.yMax, Vertex.nearZ), tint = tint });
                }
            }
            for (int i = 0; i < segments; i++)
            {
                ushort a = (ushort)(i * 2);
                mesh.SetNextIndex(a); mesh.SetNextIndex((ushort)(a + 2)); mesh.SetNextIndex((ushort)(a + 1));
                mesh.SetNextIndex((ushort)(a + 1)); mesh.SetNextIndex((ushort)(a + 2)); mesh.SetNextIndex((ushort)(a + 3));
            }
        };
    }
}
