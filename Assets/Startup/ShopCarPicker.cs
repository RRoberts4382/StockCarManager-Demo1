using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

internal sealed class ShopCarPicker
{
    private TeamCar selectedCar;

    public void SelectCar(TeamCar car) => selectedCar = car;

    public void Show(VisualElement host, List<TeamCar> cars, CarThumbnails thumbnails, string[] sponsors, Action repaint,
        Action<TeamCar> selectionChanged)
    {
        host.Clear();
        if (cars == null || cars.Count == 0)
        {
            var empty = new Button { text = "No team cars" };
            empty.AddToClassList("nav-button");
            empty.SetEnabled(false);
            host.Add(empty);
            selectedCar = null;
            selectionChanged(null);
            return;
        }

        if (!cars.Contains(selectedCar))
            selectedCar = cars[0];

        var picker = new VisualElement();
        host.Add(picker);
        var toggle = new Button { tooltip = "Choose a team car" };
        toggle.AddToClassList("nav-button");
        toggle.AddToClassList("shop-car-toggle");
        picker.Add(toggle);
        var choices = new ScrollView();
        choices.AddToClassList("shop-car-choices");
        choices.style.backgroundColor = (Color)CarThumbnails.BackgroundColor;
        picker.Add(choices);
        foreach (var car in cars)
        {
            var choice = new Button(() =>
            {
                selectedCar = car;
                selectionChanged(car);
                ShowSelection(false);
                toggle.Focus();
            });
            choice.userData = car;
            choice.AddToClassList("nav-button");
            choice.AddToClassList("shop-car-choice");
            var row = BuildRow(car, thumbnails, sponsors);
            bool wearBlocked = car.HasRaceBlockingWear;
            row.EnableInClassList("shop-wear-blocked", wearBlocked);
            if (wearBlocked)
                choice.tooltip = $"A component is at least {ComponentCondition.RaceBlockingWear:P0} worn.";
            choice.Add(row);
            choices.Add(choice);
        }

        bool expanded = false;
        toggle.clicked += () => ShowSelection(!expanded);
        picker.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (expanded && evt.keyCode == KeyCode.Escape)
            {
                ShowSelection(false);
                toggle.Focus();
                evt.StopPropagation();
            }
        }, TrickleDown.TrickleDown);
        ShowSelection(false);
        selectionChanged(selectedCar);

        void ShowSelection(bool open)
        {
            expanded = open;
            choices.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            toggle.Clear();
            toggle.Add(BuildRow(selectedCar, thumbnails, sponsors));
            AddLabel(toggle, open ? "▴" : "▾", "shop-car-caret");
            foreach (var choice in choices.Children())
                choice.EnableInClassList("nav-button-selected", ReferenceEquals(choice.userData, selectedCar));
            repaint();
        }
    }

    private static VisualElement BuildRow(TeamCar car, CarThumbnails thumbnails, string[] sponsors)
    {
        var row = new VisualElement { pickingMode = PickingMode.Ignore };
        row.AddToClassList("shop-car-row");
        var thumbnail = new Image
        {
            image = thumbnails.Get(car, sponsors),
            scaleMode = ScaleMode.ScaleToFit,
            pickingMode = PickingMode.Ignore
        };
        thumbnail.AddToClassList("shop-car-thumbnail");
        row.Add(thumbnail);
        AddLabel(row, car.chassisNumber ?? "—", "shop-car-chassis");
        var specs = new VisualElement { pickingMode = PickingMode.Ignore };
        specs.AddToClassList("shop-car-specs");
        AddLabel(specs, car.manufacturer.ToString(), "shop-car-spec");
        AddLabel(specs, car.ChassisTypeName, "shop-car-spec");
        row.Add(specs);
        return row;
    }

    private static void AddLabel(VisualElement parent, string text, string styleClass)
    {
        var label = new Label(text) { pickingMode = PickingMode.Ignore };
        label.AddToClassList(styleClass);
        parent.Add(label);
    }
}
