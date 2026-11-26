using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

internal sealed class ShopCarPicker
{
    private TeamCar selectedCar;
    private float scrollX;
    private bool revealSelected = true;

    public void SelectCar(TeamCar car) { selectedCar = car; revealSelected = true; }

    public void Show(VisualElement host, List<TeamCar> cars, CarThumbnails thumbnails, string[] sponsors, Action repaint,
        Action<TeamCar> selectionChanged)
    {
        host.Clear();
        if (cars == null || cars.Count == 0)
        {
            host.Add(SettingsFormStyle.Label("No team cars", "shop-car-spec"));
            selectedCar = null;
            selectionChanged(null);
            return;
        }
        if (!cars.Contains(selectedCar)) { selectedCar = cars[0]; revealSelected = true; }
        var left = SettingsFormStyle.Button("◀", null);
        var right = SettingsFormStyle.Button("▶", null);
        left.AddToClassList("shop-strip-arrow");
        right.AddToClassList("shop-strip-arrow");
        left.tooltip = "Scroll chassis panels to the left.";
        right.tooltip = "Scroll chassis panels to the right.";
        var list = new ScrollView(ScrollViewMode.Horizontal)
        {
            verticalScrollerVisibility = ScrollerVisibility.Hidden,
            horizontalScrollerVisibility = ScrollerVisibility.Hidden
        };
        list.AddToClassList("shop-chassis-strip");
        host.Add(left); host.Add(list); host.Add(right);
        var choices = new List<Button>();
        Button selectedPanel = null;
        foreach (var car in cars)
        {
            var panel = SettingsFormStyle.Button("", null);
            panel.AddToClassList("team-home-panel");
            panel.AddToClassList("shop-strip-card");
            SettingsFormStyle.StylePanel(panel);
            panel.tooltip = $"Select chassis {car.chassisNumber} for component work.";
            var title = SettingsFormStyle.Label($"Chassis {car.chassisNumber}", "team-home-heading");
            title.pickingMode = PickingMode.Ignore;
            panel.Add(title);
            var content = new VisualElement { pickingMode = PickingMode.Ignore };
            content.AddToClassList("shop-strip-card-content");
            var image = new Image
            {
                image = thumbnails.Get(car, sponsors, transparentBackground: true),
                scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore
            };
            image.AddToClassList("shop-strip-image");
            content.Add(image);
            var specs = new VisualElement { pickingMode = PickingMode.Ignore };
            specs.AddToClassList("shop-strip-specs");
            specs.Add(SettingsFormStyle.Label(ManufacturerCatalog.Name(car.manufacturer), "shop-car-spec"));
            specs.Add(SettingsFormStyle.Label(car.ChassisTypeName, "shop-car-spec"));
            content.Add(specs);
            panel.Add(content);
            panel.userData = car;
            panel.clicked += () =>
            {
                selectedCar = car;
                selectionChanged(car);
                UpdateSelection();
            };
            choices.Add(panel);
            list.Add(panel);
            if (ReferenceEquals(car, selectedCar)) selectedPanel = panel;
        }
        left.clicked += () => Move(-1);
        right.clicked += () => Move(1);
        list.RegisterCallback<WheelEvent>(evt =>
        {
            float delta = Mathf.Abs(evt.delta.x) > Mathf.Abs(evt.delta.y) ? evt.delta.x : evt.delta.y;
            SetOffset(scrollX + delta * 36f);
            evt.StopImmediatePropagation();
        }, TrickleDown.TrickleDown);
        list.horizontalScroller.valueChanged += value => { scrollX = value; UpdateArrows(); };
        list.RegisterCallback<GeometryChangedEvent>(_ => UpdateArrows());
        list.schedule.Execute(() =>
        {
            if (revealSelected && selectedPanel != null)
            {
                list.ScrollTo(selectedPanel);
                revealSelected = false;
            }
            else SetOffset(scrollX);
            UpdateArrows();
        });
        UpdateSelection();
        selectionChanged(selectedCar);

        void UpdateSelection()
        {
            foreach (var panel in choices)
            {
                panel.EnableInClassList("shop-strip-selected", ReferenceEquals(panel.userData, selectedCar));
                panel.MarkDirtyRepaint();
            }
            repaint();
        }
        void Move(int direction) => SetOffset(scrollX + direction * Mathf.Max(280f, list.contentViewport.layout.width * 0.8f));
        void SetOffset(float offset)
        {
            float maximum = Mathf.Max(0, list.contentContainer.layout.width - list.contentViewport.layout.width);
            list.scrollOffset = new Vector2(Mathf.Clamp(offset, 0, maximum), 0);
            scrollX = list.scrollOffset.x;
            UpdateArrows();
        }
        void UpdateArrows()
        {
            float maximum = Mathf.Max(0, list.contentContainer.layout.width - list.contentViewport.layout.width);
            left.SetEnabled(list.scrollOffset.x > 0.5f);
            right.SetEnabled(list.scrollOffset.x < maximum - 0.5f);
        }
    }
}
