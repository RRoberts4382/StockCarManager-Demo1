using System;
using UnityEngine;
using UnityEngine.UIElements;

internal static class TeamCarCard
{
    public static VisualElement Create(TeamCar car, RenderTexture thumbnail, bool canSell, Action paint, Action sell, Action shop)
    {
        var card = new VisualElement();
        card.AddToClassList("team-home-panel");
        SettingsFormStyle.StylePanel(card);
        card.AddToClassList("car-panel");
        var image = new Image { image = thumbnail, scaleMode = ScaleMode.ScaleToFit };
        image.AddToClassList("car-card-image");
        card.Add(image);
        var title = AddLabel(card, $"Chassis {car.chassisNumber ?? "Unassigned"}", "team-home-heading");
        title.AddToClassList("car-card-title");
        AddLabel(card, $"{car.manufacturer} · {car.ChassisTypeName}", "car-card-specs");

        AddComponentRatings(card, car, useIcons: true);

        var actions = new VisualElement();
        actions.AddToClassList("car-panel-actions");
        actions.style.justifyContent = Justify.FlexStart;
        actions.style.alignSelf = Align.Stretch;
        var sellButton = ActionButton(canSell ? "Sell chassis" : "Keep at least one chassis", false, sell);
        sellButton.SetEnabled(canSell);
        actions.Add(sellButton);
        var paintButton = ActionButton("Paint chassis", true, paint);
        paintButton.style.marginLeft = StyleKeyword.Auto;
        actions.Add(paintButton);
        actions.Add(ActionButton("Open this chassis in Shop", false, shop, wrench: true));
        card.Add(actions);
        return card;
    }

    public static void AddComponentRatings(VisualElement card, TeamCar car, bool useIcons = false)
    {
        var header = new VisualElement();
        header.AddToClassList("car-card-rating-row");
        var componentHeading = AddLabel(header, useIcons ? "" : "Component", "car-card-component");
        if (useIcons) componentHeading.style.width = 32;
        AddLabel(header, "Level", "car-card-rating-heading");
        AddLabel(header, "Wear", "car-card-rating-heading");
        card.Add(header);
        foreach (var component in TeamCar.Components)
        {
            var condition = car.GetComponent(component);
            var row = new VisualElement();
            row.AddToClassList("car-card-rating-row");
            var repair = car.repairs?.Find(item => item.component == component);
            if (useIcons)
            {
                var slot = new VisualElement();
                slot.AddToClassList("home-component-icon-slot");
                slot.tooltip = component + (repair == null ? "" : $" · {repair.AssigneeDisplay}");
                var icon = ShopComponentPanel.ComponentIcon(component);
                icon.pickingMode = PickingMode.Ignore;
                slot.Add(icon);
                row.Add(slot);
            }
            else
            {
                var name = AddLabel(row, component == CarComponent.Aerodynamics ? "Aero" : component.ToString(), "car-card-component");
                if (repair != null)
                    name.tooltip = $"{repair.AssigneeDisplay}";
            }
            AddMetric(row, condition.Level / (float)TeamCar.MaximumComponentLevel, $"{condition.Level}/{TeamCar.MaximumComponentLevel}",
                ShopComponentPanel.ComponentColor(component));
            AddMetric(row, condition.Wear, $"{condition.Wear:P0}", ComponentWearGradient.ColorAt(condition.Wear), wearMeter: true);
            card.Add(row);
        }

    }

    private static Label AddLabel(VisualElement parent, string text, string style)
    {
        var label = new Label(text);
        label.AddToClassList(style);
        parent.Add(label);
        return label;
    }

    internal static void AddComponentLevel(VisualElement row, TeamCar car, CarComponent component)
    {
        int level = car.GetComponent(component).Level;
        AddMetric(row, level / (float)TeamCar.MaximumComponentLevel, $"{level}/{TeamCar.MaximumComponentLevel}",
            ShopComponentPanel.ComponentColor(component));
    }

    private static void AddMetric(VisualElement row, float fraction, string value, Color color, bool wearMeter = false)
    {
        var metric = new VisualElement { tooltip = wearMeter ? "Component wear: " + value : "Component level: " + value };
        metric.AddToClassList("car-card-metric");
        var track = new VisualElement();
        track.AddToClassList("employee-skill-track");
        metric.Add(track);
        var fill = new VisualElement();
        fill.AddToClassList("employee-skill-fill");
        fill.style.width = Length.Percent(Mathf.Clamp01(fraction) * 100f);
        fill.style.backgroundColor = color;
        if (wearMeter) ComponentWearGradient.Apply(fill, fraction);
        track.Add(fill);
        AddLabel(metric, value, "car-card-value");
        row.Add(metric);
    }

    private static Button ActionButton(string tooltip, bool paint, Action action, bool wrench = false)
    {
        var button = new Button(action) { tooltip = tooltip };
        button.AddToClassList("settings-button");
        button.AddToClassList("car-card-action");
        var icon = new VisualElement { pickingMode = PickingMode.Ignore };
        icon.AddToClassList("car-card-action-icon");
        button.Add(icon);
        icon.generateVisualContent += context =>
        {
            var painter = context.painter2D;
            painter.strokeColor = icon.resolvedStyle.color;
            painter.lineWidth = 1.5f;
            painter.BeginPath();
            if (wrench)
            {
                Line(12, 2, 9, 3, 8, 6, 9, 9, 2, 15, 2, 17, 4, 18, 11, 11,
                    14, 12, 17, 10, 18, 7, 14, 9, 11, 6, 12, 2);
            }
            else if (paint)
            {
                Line(2, 3, 14, 3, 14, 8, 2, 8, 2, 3);
                Line(14, 5, 17, 5, 17, 10, 9, 10, 9, 16);
            }
            else
            {
                Line(2, 4, 8, 3, 16, 10, 10, 16, 2, 9, 2, 4);
                painter.MoveTo(new Vector2(7, 6.5f));
                painter.Arc(new Vector2(5.5f, 6.5f), 1.5f, Angle.Degrees(0), Angle.Degrees(360));
            }
            painter.Stroke();

            void Line(params int[] points)
            {
                painter.MoveTo(new Vector2(points[0], points[1]));
                for (int i = 2; i < points.Length; i += 2)
                    painter.LineTo(new Vector2(points[i], points[i + 1]));
            }
        };
        return button;
    }
}
