using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

internal static class EventSetupPanel
{
    private enum PartIcon { Spring, Shock, SwayBar, TrackBar, Garage }

    public static VisualElement Create(TeamCar car, TrackDefinition track, Func<string, Setup, string> save,
        Texture overheadImage, VisualElement dialogHost, Action returnToGarage, out Func<Setup> readDraft, Func<float> fuelGallons = null, Func<bool> isLocked = null, Func<Setup> lockedSetup = null)
    {
        var panel = new VisualElement();
        panel.AddToClassList("setup-editor");

        var board = new VisualElement();
        board.AddToClassList("setup-board");
        board.style.backgroundColor = (Color)CarThumbnails.BackgroundColor;
        panel.Add(board);
        var left = Column("setup-side");
        var center = Column("setup-center");
        var right = Column("setup-side");
        var sway = new SetupSlider(PartRow(center, PartIcon.SwayBar, "Front sway bar (lb/in)"), "Sway bar", 275f, 5000f, 0);
        sway.Control.tooltip = "Front sway bar stiffness (lb/in)";
        var image = new Image { image = overheadImage, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
        image.AddToClassList("setup-overhead-car");
        center.Add(image);

        var weights = new VisualElement();
        weights.tooltip = "Dry-car weight distribution including the driver (%). Fuel is added equally to the rear corners.";
        center.Add(weights);
        var totalWeight = new Label();
        totalWeight.style.fontSize = 12;
        totalWeight.style.whiteSpace = WhiteSpace.Normal;
        weights.Add(totalWeight);
        var frontWeight = new SetupSlider(weights, "Front %", WeightDistributionSetup.MinFront, WeightDistributionSetup.MaxFront, 1);
        frontWeight.Control.tooltip = "Front-axle percentage of dry car weight, including the driver. Rear fuel load is added separately.";
        var crossWeight = new SetupSlider(weights, "Cross %", WeightDistributionSetup.MinCross, WeightDistributionSetup.MaxCross, 1);
        crossWeight.Control.tooltip = "Right-front plus left-rear dry weight as a percentage of total dry weight.";
        var leftWeight = new SetupSlider(weights, "Left side %", WeightDistributionSetup.MinLeft, WeightDistributionSetup.MaxLeft, 1);

        leftWeight.Control.tooltip = "Left-side percentage of dry car weight. Rear fuel is split equally left and right.";
        var trackBar = new SetupSlider(PartRow(center, PartIcon.TrackBar, "Rear track bar (in)"), "Track bar", 5f, 14f, 1);
        trackBar.Control.tooltip = "Right mount height above the ground. The left mount remains fixed to the axle.";
        var cornerWeights = new Label[4];
        string[] cornerNames = { "Left front", "Right front", "Left rear", "Right rear" };
        var fields = Enumerable.Range(0, 4).Select(index =>
        {
            var corner = new VisualElement();
            corner.AddToClassList("setup-corner");
            var weight = new Label { pickingMode = PickingMode.Ignore };
            weight.style.unityFontStyleAndWeight = FontStyle.Bold;
            weight.style.fontSize = 13;
            weight.style.flexShrink = 0;
            weight.style.unityTextAlign = TextAnchor.MiddleCenter;
            cornerWeights[index] = weight;
            corner.Add(weight);
            (index % 2 == 0 ? left : right).Add(corner);
            var (minimum, maximum) = SpringRange(track.TrackCategory, index);
            var spring = new SetupSlider(PartRow(corner, PartIcon.Spring, "Spring rate (lb/in)"), "Spring", minimum, maximum, 0);
            spring.Control.tooltip = $"Spring rate ({minimum}–{maximum} lb/in)";
            var shock = PartRow(corner, PartIcon.Shock, "Shock absorber");
            var compression = new SetupSlider(shock, "Compression", 0f, 1f, 2);
            var rebound = new SetupSlider(shock, "Rebound", 0f, 1f, 2);
            var pressure = new SetupSlider(corner, "Cold PSI", 10f, 60f, 1);
            pressure.Control.tooltip = "Cold tire inflation pressure at 75 F; running pressure rises with temperature.";
            return (Spring: spring, Compression: compression, Rebound: rebound, Pressure: pressure);
        }).ToArray();

        var notes = new TextField("Notes") { multiline = true };
        notes.AddToClassList("setup-notes");
        var footer = new VisualElement();
        footer.AddToClassList("setup-footer");
        footer.Add(notes);
        panel.Add(footer);

        var actions = new VisualElement();
        actions.AddToClassList("actions");
        actions.AddToClassList("setup-actions");
        var status = new Label { style = { whiteSpace = WhiteSpace.Normal } };
        var current = new Label { style = { whiteSpace = WhiteSpace.Normal } };
        current.AddToClassList("setup-current-name");
        var summary = new VisualElement();
        summary.AddToClassList("setup-summary");
        summary.Add(current);
        summary.Add(status);
        string currentName = null;
        Button saveCurrent = null;
        AddButton("Load Setup", () => SetupPresetDialog.Show(dialogHost, track.name,
            Presets(), currentName, Load, null));
        saveCurrent = AddButton("Save Setup", () => SetupPresetDialog.Show(dialogHost, track.name,
            Presets(), currentName, null, Save, overwriteName: currentName));
        saveCurrent.tooltip = "Overwrite the current named setup. Use Save As to save a new setup.";
        AddButton("Save As", () => SetupPresetDialog.Show(dialogHost, track.name,
            Presets(), null, null, Save));
        actions.Add(NavigationButton(PartIcon.Garage, "Return to Garage", returnToGarage));
        footer.Add(actions);
        panel.Add(summary);
        frontWeight.Control.RegisterValueChangedCallback(_ => UpdateCornerWeights());
        crossWeight.Control.RegisterValueChangedCallback(_ => UpdateCornerWeights());
        leftWeight.Control.RegisterValueChangedCallback(_ => UpdateCornerWeights());
        panel.RegisterCallback<AttachToPanelEvent>(_ => UpdateCornerWeights());
        Load(isLocked?.Invoke() == true && lockedSetup?.Invoke() is Setup frozenSetup
            ? new TrackSetup { trackId = track.id, name = "Event locked setup", setup = frozenSetup }
            : car.SetupsFor(track.id).LastOrDefault());
        var openedSetup = ReadDraft();
        void RefreshLock()
        {
            bool frozen = isLocked?.Invoke() == true;
            board.SetEnabled(!frozen);
            notes.SetEnabled(!frozen);
            foreach (var button in actions.Children().OfType<Button>())
                if (button.text == "Load Setup" || button.text == "Save Setup" || button.text == "Save As") button.SetEnabled(!frozen);
            if (frozen) status.text = "Setup locked: qualifying has started.";
        }
        panel.RegisterCallback<AttachToPanelEvent>(_ => RefreshLock());
        RefreshLock();
        readDraft = () => isLocked?.Invoke() == true ? RivalSetup001.Copy(openedSetup) : ReadDraft();
        return panel;

        TrackSetup[] Presets()
        {
            var saved = car.SetupsFor(track.id).ToArray();
            return saved.Any(item => item.IsDefault) ? saved :
                saved.Prepend(new TrackSetup { trackId = track.id, setup = car.setup ?? new Setup() }).ToArray();
        }

        void Load(TrackSetup saved)
        {
            currentName = saved?.IsDefault == false ? saved.DisplayName : null;
            UpdateCurrentName();
            var setup = saved?.setup ?? car.setup ?? new Setup();
            var settings = CornersOf(setup);
            for (int i = 0; i < fields.Length; i++)
            {
                fields[i].Pressure.Value = VehicleSpeed003.ColdPressure(setup, i);
                fields[i].Spring.Value = settings[i].spring.ratePoundsPerInch;
                fields[i].Compression.Value = settings[i].shock.compressionStiffness;
                fields[i].Rebound.Value = settings[i].shock.reboundStiffness;
            }
            sway.Value = setup.frontSwayBar.RatePoundsPerInch;
            trackBar.Value = setup.rearTrackBar.RightMountHeightAboveGroundInches;
            frontWeight.Value = setup.weightDistribution?.FrontPercent ?? 52f;
            crossWeight.Value = setup.weightDistribution?.CrossPercent ?? 50f;
            leftWeight.Value = setup.weightDistribution?.LeftPercent ?? 50f;
            notes.value = setup.notes ?? string.Empty;
            UpdateCornerWeights();
            status.text = saved == null ? string.Empty : "Setup loaded.";
        }

        string Save(string name)
        {
            if (isLocked?.Invoke() == true) return "Setup locked: qualifying has started.";
            string error = save(name, ReadDraft());
            if (error != null)
                return error;
            currentName = name;
            UpdateCurrentName();
            status.text = "Setup saved.";
            return null;
        }

        Setup ReadDraft()
        {
            var setup = new Setup { notes = notes.value };
            var settings = CornersOf(setup);
            for (int i = 0; i < fields.Length; i++)
            {
                setup.coldTirePressures[i] = fields[i].Pressure.Value;
                settings[i].spring.ratePoundsPerInch = fields[i].Spring.Value;
                settings[i].shock.compressionStiffness = fields[i].Compression.Value;
                settings[i].shock.reboundStiffness = fields[i].Rebound.Value;
            }
            setup.frontSwayBar.ratePoundsPerInch = sway.Value;
            setup.rearTrackBar.rightMountHeightAboveGroundInches = trackBar.Value;
            setup.weightDistribution.frontPercent = frontWeight.Value;
            setup.weightDistribution.crossPercent = crossWeight.Value;
            setup.weightDistribution.leftPercent = leftWeight.Value;
            return setup;
        }

        void UpdateCornerWeights()
        {
            float fuel = Mathf.Clamp(fuelGallons?.Invoke() ?? (float)SessionFuel.TankCapacity,
                0f, (float)SessionFuel.TankCapacity);
            float total = VehicleSpeed003.WeightPounds(fuel);
            var draft = ReadDraft();
            totalWeight.text = $"Total {total:N1} lb / {fuel:0.0} gal";
            totalWeight.tooltip = $"With fuel: front {VehicleSpeed003.FrontWeightPercent(draft, fuel):0.00}%, cross {VehicleSpeed003.CrossWeightPercent(draft, fuel):0.00}%, left {VehicleSpeed003.LeftWeightPercent(draft, fuel):0.00}%. Sliders specify dry-car percentages.";
            for (int corner = 0; corner < cornerWeights.Length; corner++)
            {
                float pounds = VehicleSpeed003.CornerWeightPounds(draft, fuel, corner);
                cornerWeights[corner].text = $"{cornerNames[corner]}  ·  {pounds:N1} lb";
                cornerWeights[corner].tooltip = $"Static corner weight, including the {fuel:0.0} gallons selected for the next run.";
            }
        }

        void UpdateCurrentName()
        {
            current.text = currentName == null ? "Current setup · Default (read-only)" : $"Current setup · {currentName}";
            saveCurrent.SetEnabled(currentName != null);
        }

        Button AddButton(string title, Action clicked)
        {
            var button = new Button(clicked) { text = title };
            button.AddToClassList("nav-button");
            actions.Add(button);
            return button;
        }

        VisualElement Column(string className)
        {
            var column = new VisualElement();
            column.AddToClassList(className);
            board.Add(column);
            return column;
        }

    }

    public static Button OpenSetupButton(Action openSetup) =>
        NavigationButton(PartIcon.Spring, "Open Setup", openSetup);

    private static Button NavigationButton(PartIcon part, string tooltip, Action navigate)
    {
        var button = new Button(navigate) { tooltip = tooltip };
        button.AddToClassList("nav-button");
        button.AddToClassList("setup-navigation-icon");
        var icon = CreatePartIcon(part, tooltip);
        icon.pickingMode = PickingMode.Ignore;
        button.Add(icon);
        return button;
    }

    private static VisualElement PartRow(VisualElement parent, PartIcon part, string tooltip)
    {
        var row = new VisualElement();
        row.AddToClassList("setup-part-row");
        row.Add(CreatePartIcon(part, tooltip));
        var controls = new VisualElement();
        controls.AddToClassList("setup-part-controls");
        row.Add(controls);
        parent.Add(row);
        return controls;
    }

    private static VisualElement CreatePartIcon(PartIcon part, string tooltip)
    {
        var icon = new VisualElement { tooltip = tooltip };
        icon.AddToClassList("setup-part-icon");
        icon.generateVisualContent += context => DrawPart(context.painter2D, part);
        return icon;
    }

    private static void DrawPart(Painter2D painter, PartIcon part)
    {
        painter.strokeColor = part switch
        {
            PartIcon.Spring => new Color32(111, 202, 255, 255),
            PartIcon.Shock => new Color32(255, 183, 92, 255),
            PartIcon.SwayBar => new Color32(100, 224, 173, 255),
            PartIcon.Garage => new Color32(233, 240, 247, 255),
            _ => new Color32(216, 162, 255, 255)
        };
        painter.lineWidth = 1.7f;
        painter.lineJoin = LineJoin.Round;
        painter.lineCap = LineCap.Round;
        switch (part)
        {
            case PartIcon.Spring:
                Stroke(new(6, 3), new(18, 3));
                Stroke(new(12, 3), new(6, 6), new(18, 9), new(6, 12), new(18, 15), new(6, 18), new(12, 21));
                Stroke(new(6, 21), new(18, 21));
                break;
            case PartIcon.Shock:
                Stroke(new(9, 3), new(15, 3));
                Stroke(new(12, 3), new(12, 10));
                Stroke(new(9, 10), new(15, 10), new(15, 19), new(9, 19), new(9, 10));
                Stroke(new(12, 19), new(12, 22));
                Stroke(new(10, 22), new(14, 22));
                break;
            case PartIcon.SwayBar:
                Stroke(new(2, 16), new(5, 7), new(19, 7), new(22, 16));
                Stroke(new(8, 5), new(8, 10));
                Stroke(new(16, 5), new(16, 10));
                break;
            case PartIcon.TrackBar:
                Stroke(new(5, 16), new(19, 8));
                Stroke(new(2, 16), new(5, 13), new(8, 16), new(5, 19), new(2, 16));
                Stroke(new(16, 8), new(19, 5), new(22, 8), new(19, 11), new(16, 8));
                break;
            case PartIcon.Garage:
                Stroke(new(3, 21), new(3, 9), new(12, 3), new(21, 9), new(21, 21), new(3, 21));
                Stroke(new(7, 21), new(7, 11), new(17, 11), new(17, 21));
                Stroke(new(7, 14), new(17, 14));
                Stroke(new(7, 17), new(17, 17));
                break;
        }

        void Stroke(params Vector2[] points)
        {
            painter.BeginPath();
            painter.MoveTo(points[0]);
            for (int i = 1; i < points.Length; i++)
                painter.LineTo(points[i]);
            painter.Stroke();
        }
    }

    private static SuspensionCornerSetup[] CornersOf(Setup setup) =>
        new[] { setup.leftFront, setup.rightFront, setup.leftRear, setup.rightRear };

    private static (float Min, float Max) SpringRange(ChassisType type, int corner) => SetupLimits.SpringRange(type, corner);

    private sealed class SetupSlider
    {
        private readonly Label readout;
        private readonly int decimals;
        public Slider Control { get; }

        public float Value
        {
            get => Control.value;
            set
            {
                float rounded = (float)Math.Round(Mathf.Clamp(value, Control.lowValue, Control.highValue), decimals);
                Control.SetValueWithoutNotify(rounded);
                readout.text = rounded.ToString($"F{decimals}");
            }
        }

        public SetupSlider(VisualElement parent, string label, float minimum, float maximum, int decimals)
        {
            this.decimals = decimals;
            var row = new VisualElement();
            row.AddToClassList("setup-field");
            Control = new Slider(label, minimum, maximum);
            Control.tooltip = $"{minimum} to {maximum}";
            readout = new Label { pickingMode = PickingMode.Ignore };
            readout.AddToClassList("setup-slider-value");
            Control.RegisterValueChangedCallback(evt => Value = evt.newValue);
            row.Add(Control);
            row.Add(readout);
            parent.Add(row);
        }
    }
}
