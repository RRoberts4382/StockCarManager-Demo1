using System;
using UnityEngine;
using UnityEngine.UIElements;

internal static class TeamCarCard
{
    public static VisualElement Create(TeamCar car, RenderTexture thumbnail, bool canSell, Action paint, Action sell)
    {
        var card = new VisualElement();
        card.AddToClassList("garage-entry");
        card.AddToClassList("car-panel");
        var image = new Image { image = thumbnail, scaleMode = ScaleMode.ScaleToFit };
        image.AddToClassList("car-card-image");
        card.Add(image);
        AddLabel(card, $"Chassis # {car.chassisNumber ?? "Unassigned"}", "car-card-title");
        AddLabel(card, $"{car.manufacturer} · {car.ChassisTypeName}", "car-card-specs");

        var header = new VisualElement();
        header.AddToClassList("car-card-rating-row");
        AddLabel(header, "Component", "car-card-component");
        AddLabel(header, "Level", "car-card-rating-heading");
        AddLabel(header, "Wear", "car-card-rating-heading");
        card.Add(header);
        foreach (var component in TeamCar.Components)
        {
            var condition = car.GetComponent(component);
            var row = new VisualElement();
            row.AddToClassList("car-card-rating-row");
            var repair = car.repairs?.Find(item => item.component == component);
            var name = AddLabel(row, component == CarComponent.Aerodynamics ? "Aero" : component.ToString(), "car-card-component");
            if (repair != null)
                name.tooltip = $"Repair in progress · {repair.employeeName}";
            AddMetric(row, condition.Level / (float)TeamCar.MaximumComponentLevel, $"{condition.Level}/{TeamCar.MaximumComponentLevel}",
                ShopComponentPanel.ComponentColor(component));
            AddMetric(row, condition.Wear, $"{condition.Wear:P0}", condition.BlocksRaceEntry
                ? new Color32(239, 91, 87, 255) : new Color32(237, 172, 89, 255));
            card.Add(row);
        }

        var actions = new VisualElement();
        actions.AddToClassList("car-panel-actions");
        var sellButton = ActionButton(canSell ? "Sell chassis" : "Keep at least one chassis", false, sell);
        sellButton.SetEnabled(canSell);
        actions.Add(sellButton);
        actions.Add(ActionButton("Paint chassis", true, paint));
        card.Add(actions);
        return card;
    }

    private static Label AddLabel(VisualElement parent, string text, string style)
    {
        var label = new Label(text);
        label.AddToClassList(style);
        parent.Add(label);
        return label;
    }

    private static void AddMetric(VisualElement row, float fraction, string value, Color color)
    {
        var metric = new VisualElement { tooltip = value };
        metric.AddToClassList("car-card-metric");
        var track = new VisualElement();
        track.AddToClassList("employee-skill-track");
        metric.Add(track);
        var fill = new VisualElement();
        fill.AddToClassList("employee-skill-fill");
        fill.style.width = Length.Percent(Mathf.Clamp01(fraction) * 100f);
        fill.style.backgroundColor = color;
        track.Add(fill);
        AddLabel(metric, value, "car-card-value");
        row.Add(metric);
    }

    private static Button ActionButton(string tooltip, bool paint, Action action)
    {
        var button = new Button(action) { tooltip = tooltip };
        button.AddToClassList("nav-button");
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
            if (paint)
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
