using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

internal sealed class GarageSetupEditor
{
    private static readonly string[] Categories = { "Weight", "Springs", "Shocks", "Tires", "Bars", "Fuel" };
    private static readonly Color32[] ButtonColors =
    {
        new Color32(78, 220, 196, 255),  // Weight
        new Color32(255, 138, 100, 255), // Springs
        new Color32(195, 161, 255, 255), // Shocks
        new Color32(90, 205, 255, 255),  // Tires
        new Color32(255, 135, 188, 255), // Bars
        new Color32(226, 224, 99, 255),  // Fuel
        new Color32(0, 155, 221, 255),   // Load
        new Color32(91, 221, 153, 255),  // Save
        new Color32(255, 191, 75, 255),  // Save As
        new Color32(245, 245, 245, 255), // Revert
        new Color32(245, 245, 245, 255), // Help
        new Color32(164, 191, 238, 255)  // Run history
    };
    private static readonly string[] Corners = { "Left front", "Right front", "Left rear", "Right rear" };
    private readonly TeamCar car;
    private readonly TrackDefinition track;
    private readonly VisualElement screen;
    private readonly Func<string, Setup, string> save;
    private readonly Func<GarageRunReview> lastRun;
    private readonly Func<float> fuel;
    private readonly Action<float> setFuel;
    private readonly Func<bool> locked;
    private readonly Func<bool> canFuel;
    private readonly VisualElement[] slots = new VisualElement[4];
    private readonly Label[] loads = new Label[4];
    private readonly Label[] oldLoads = new Label[4];
    private readonly VisualElement[] loadBadges = new VisualElement[4];
    private readonly Button[] tabs = new Button[6];
    private readonly VisualElement board;
    private readonly Label title, selectedLabel, status, totalWeight;
    private readonly TextField notes;
    private readonly Button loadButton, saveButton, saveAsButton, revertButton;
    private Setup draft, saved;
    private string setupName, savedName;
    private float savedFuel;
    private int selected, helpFormat;
    private Action closeDialog;
    internal VisualElement Root { get; } = new VisualElement();

    internal GarageSetupEditor(TeamCar car, TrackDefinition track, VisualElement screen, Texture image,
        Func<string, Setup, string> save, Func<GarageRunReview> lastRun, Func<float> fuel,
        Action<float> setFuel, Func<bool> locked, Func<bool> canFuel, Action<Button> openRunHistory)
    {
        this.car = car;
        this.track = track;
        this.screen = screen;
        this.save = save;
        this.lastRun = lastRun;
        this.fuel = fuel;
        this.setFuel = setFuel;
        this.locked = locked;
        this.canFuel = canFuel;

        Root.AddToClassList("garage-redesign-setup");
        var header = Group(Root, "garage-redesign-row");
        title = Text(header, "", "garage-redesign-title");
        var presets = Group(header, "garage-redesign-buttons");
        loadButton = IconButton(presets, "Load Setup", "Pick a saved setup for this track.", 6, () =>
            SetupPresetDialog.Show(screen, track.name, Presets(), setupName, Load, null));
        saveButton = IconButton(presets, "Save Setup", "Overwrite the current named setup.", 7, () =>
            SetupPresetDialog.Show(screen, track.name, Presets(), setupName, null, Save, overwriteName: setupName));
        saveAsButton = IconButton(presets, "Save As", "Save this setup under a new name.", 8, () =>
            SetupPresetDialog.Show(screen, track.name, Presets(), null, null, Save));
        var toolbar = Group(Root, "garage-redesign-row");
        var categories = Group(toolbar, "garage-redesign-buttons");
        for (int i = 0; i < tabs.Length; i++)
        {
            int index = i;
            tabs[i] = IconButton(categories, Categories[i], $"Show {Categories[i].ToLowerInvariant()} settings.", i, () =>
            {
                selected = index;
                Refresh();
            });
        }
        var actions = Group(toolbar, "garage-redesign-buttons");
        IconButton(actions, "On-track runs", "Review lap times, handling, and tire data from previous runs.", 11,
            () => openRunHistory(Root.Q<Button>("on-track-runs")));
        revertButton = IconButton(actions, "Revert", "Discard changes since the last save.", 9, Revert);
        IconButton(actions, "Help", "Review crew chief suggestions based on the last run's driver comments.", 10, Help);
        var caption = Group(Root, "garage-redesign-row");
        caption.AddToClassList("garage-redesign-caption");
        selectedLabel = Text(caption, "", "garage-redesign-heading");
        status = Text(caption, "", "garage-redesign-status");

        board = Group(Root, "garage-redesign-board");
        var left = Group(board, "garage-redesign-side");
        var center = Group(board, "garage-redesign-center");
        var right = Group(board, "garage-redesign-side");
        var overhead = new Image { image = image, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
        overhead.AddToClassList("garage-redesign-car");
        center.Add(overhead);
        for (int i = 0; i < 4; i++)
        {
            slots[i] = Group(i % 2 == 0 ? left : right, "garage-redesign-slot");
            loadBadges[i] = Group(center, "garage-redesign-load-badge");
            loads[i] = Text(loadBadges[i], "", "garage-redesign-corner-load");
            loads[i].tooltip = $"{Corners[i]} static load including fuel for the next run.";
            oldLoads[i] = Text(loadBadges[i], "", "garage-redesign-previous");
            oldLoads[i].tooltip = $"{Corners[i]} load at the start of the last on-track run.";
        }
        overhead.RegisterCallback<GeometryChangedEvent>(_ => PositionLoads());
        for (int i = 0; i < 4; i++)
            loadBadges[i].RegisterCallback<GeometryChangedEvent>(_ => PositionLoads());
        totalWeight = Text(Root, "", "garage-redesign-status");

        notes = new TextField("Setup Notes") { multiline = true, tooltip = "Notes saved with this setup." };
        notes.AddToClassList("garage-redesign-notes");
        SettingsFormStyle.StyleField(notes);
        notes.RegisterValueChangedCallback(evt => draft.notes = evt.newValue);
        Root.Add(notes);
        Root.RegisterCallback<DetachFromPanelEvent>(evt => { if (evt.target == Root) closeDialog?.Invoke(); });
        ResetToPreset(car.SetupsFor(track.id).LastOrDefault(), true);
    }

    internal Setup ReadDraft() => RivalSetup001.Copy(draft);

    internal void Refresh()
    {
        title.text = setupName ?? "Default";
        title.tooltip = setupName ?? "Default setup. Use Save As to create a named setup.";
        selectedLabel.text = Categories[selected];
        for (int i = 0; i < tabs.Length; i++)
        {
            tabs[i].EnableInClassList("settings-tab-current", i == selected);
            var tint = (Color)ButtonColors[i];
            tint.a = i == selected ? .32f : .08f;
            tabs[i].style.backgroundColor = tint;
        }
        foreach (var slot in slots) slot.Clear();

        var previous = lastRun()?.startDiagnostics;
        var old = previous?.setup;
        bool frozen = locked();
        notes.SetEnabled(!frozen);
        loadButton.SetEnabled(!frozen);
        saveButton.SetEnabled(!frozen && setupName != null);
        saveAsButton.SetEnabled(!frozen);
        revertButton.SetEnabled(!frozen);
        if (frozen) status.text = "Setup locked: qualifying has started.";

        switch (selected)
        {
            case 0:
                var weight = draft.weightDistribution;
                Adjust(0, "Front-rear", weight.FrontPercent, old?.weightDistribution?.FrontPercent,
                    WeightDistributionSetup.MinFront, WeightDistributionSetup.MaxFront, .25f, "0.00", "% front",
                    value => weight.frontPercent = value);
                Adjust(2, "Cross weight", weight.CrossPercent, old?.weightDistribution?.CrossPercent,
                    WeightDistributionSetup.MinCross, WeightDistributionSetup.MaxCross, .25f, "0.00", "%",
                    value => weight.crossPercent = value);
                Adjust(3, "Left-right", weight.LeftPercent, old?.weightDistribution?.LeftPercent,
                    WeightDistributionSetup.MinLeft, WeightDistributionSetup.MaxLeft, .25f, "0.00", "% left",
                    value => weight.leftPercent = value, vertical: false);
                break;
            case 1:
            case 2:
            case 3:
                for (int i = 0; i < 4; i++)
                {
                    int corner = i;
                    var part = Corner(draft, i);
                    var prior = old == null ? null : Corner(old, i);
                    if (selected == 1)
                    {
                        var range = SetupLimits.SpringRange(car.chassisType, i);
                        Adjust(i, "Spring rate", part.spring.ratePoundsPerInch, prior?.spring.ratePoundsPerInch,
                            range.Min, range.Max, 50f, "0", " lb/in", value => part.spring.ratePoundsPerInch = value);
                    }
                    else if (selected == 2)
                    {
                        Adjust(i, "Compression", ShockStep(part.shock.compressionStiffness),
                            prior == null ? null : ShockStep(prior.shock.compressionStiffness),
                            1, 10, 1, "0", "", value => part.shock.compressionStiffness = (value - 1) / 9f, shock: true);
                        Adjust(i, "Rebound", ShockStep(part.shock.reboundStiffness),
                            prior == null ? null : ShockStep(prior.shock.reboundStiffness),
                            1, 10, 1, "0", "", value => part.shock.reboundStiffness = (value - 1) / 9f, shock: true);
                    }
                    else
                        Adjust(i, "Cold tire pressure", VehicleSpeed003.ColdPressure(draft, i),
                            old == null ? null : VehicleSpeed003.ColdPressure(old, i), 10, 60, .5f, "0.0", " psi",
                            value => draft.coldTirePressures[corner] = value);
                }
                break;
            case 4:
                Adjust(0, "Front sway bar", draft.frontSwayBar.RatePoundsPerInch, old?.frontSwayBar?.RatePoundsPerInch,
                    275, 5000, 25, "0", " lb/in", value => draft.frontSwayBar.ratePoundsPerInch = value);
                Adjust(3, "Rear track bar", draft.rearTrackBar.RightMountHeightAboveGroundInches,
                    old?.rearTrackBar?.RightMountHeightAboveGroundInches, 5, 14, .1f, "0.0", " in",
                    value => draft.rearTrackBar.rightMountHeightAboveGroundInches = value);
                break;
            case 5:
                Adjust(2, "Fuel for next run", fuel(), previous == null ? null : (float)previous.fuelGallons,
                    0, (float)SessionFuel.TankCapacity, 1, "0", " gal", setFuel, enabled: canFuel());
                break;
        }
        UpdateLoads();
        PositionLoads();
    }

    private void Adjust(int corner, string name, float raw, float? prior, float min, float max, float step,
        string format, string unit, Action<float> change, bool vertical = true, bool shock = false, bool? enabled = null)
    {
        float Snap(float n) => Mathf.Clamp(Mathf.Round(n / step) * step, min, max);
        string Format(float n) => n.ToString(format) + unit;
        var group = Group(slots[corner], shock || !vertical ? "garage-redesign-stacked" : "garage-redesign-adjust");
        group.name = name.Replace(' ', '-').ToLowerInvariant() + "-" + corner;
        if (shock) Text(group, name, "garage-redesign-adjust-label");
        var values = new VisualElement();
        if (!shock && selected != 1 && selected != 3) Text(values, name, "garage-redesign-adjust-label");
        var value = Text(values, "", "garage-redesign-value");
        var previous = Text(values, "", "garage-redesign-previous");
        string tooltip = $"{Corners[corner]} {name}. Range {Format(min)} to {Format(max)}.";
        var slider = new Slider(min, max, vertical ? SliderDirection.Vertical : SliderDirection.Horizontal) { tooltip = tooltip };
        Color accent = ButtonColors[selected];
        var tracker = slider.Q<VisualElement>("unity-tracker");
        var handle = slider.Q<VisualElement>("unity-dragger");
        if (tracker != null)
            tracker.style.backgroundColor = Color.Lerp(new Color32(32, 42, 54, 255), accent, .42f);
        if (handle != null)
        {
            handle.style.backgroundColor = accent;
            SetBorder(handle, accent);
        }
        slider.AddToClassList(vertical ? "garage-redesign-vertical" : "garage-redesign-horizontal");
        slider.SetValueWithoutNotify(Snap(raw));
        slider.SetEnabled(enabled ?? !locked());
        value.tooltip = tooltip + " Current setting.";
        previous.tooltip = "Value used at the start of the last on-track run.";
        Display(slider.value);
        if (!vertical || !shock && corner % 2 == 0)
        {
            group.Add(values);
            group.Add(slider);
        }
        else
        {
            group.Add(slider);
            group.Add(values);
        }
        slider.RegisterCallback<KeyDownEvent>(evt =>
        {
            int direction = evt.keyCode switch
            {
                KeyCode.UpArrow or KeyCode.RightArrow => 1,
                KeyCode.DownArrow or KeyCode.LeftArrow => -1,
                _ => 0
            };
            if (direction == 0) return;
            slider.value = Snap(slider.value + direction * step);
            evt.StopImmediatePropagation();
        }, TrickleDown.TrickleDown);
        slider.RegisterValueChangedCallback(evt =>
        {
            float next = Snap(evt.newValue);
            slider.SetValueWithoutNotify(next);
            if (Mathf.Approximately(next, Snap(evt.previousValue))) return;
            change(next);
            Display(next);
            UpdateLoads();
        });

        void Display(float current)
        {
            value.text = Format(current);
            previous.text = prior.HasValue && !Mathf.Approximately(Snap(prior.Value), current)
                ? Format(Snap(prior.Value)) : string.Empty;
            previous.style.visibility = previous.text.Length == 0 ? Visibility.Hidden : Visibility.Visible;
        }
    }

    private static float ShockStep(float value) => 1 + Mathf.Round(9 * Mathf.Clamp01(value));
    private static SuspensionCornerSetup Corner(Setup s, int i) =>
        i == 0 ? s.leftFront : i == 1 ? s.rightFront : i == 2 ? s.leftRear : s.rightRear;

    private void UpdateLoads()
    {
        var prior = lastRun()?.startDiagnostics;
        float gallons = fuel();
        totalWeight.style.display = selected == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        totalWeight.text = $"Total {VehicleSpeed003.WeightPounds(gallons):N1} lb  /  {gallons:0.0} gal";
        totalWeight.tooltip = $"With fuel: front {VehicleSpeed003.FrontWeightPercent(draft, gallons):0.00}%, " +
            $"cross {VehicleSpeed003.CrossWeightPercent(draft, gallons):0.00}%, left {VehicleSpeed003.LeftWeightPercent(draft, gallons):0.00}%.";
        for (int i = 0; i < 4; i++)
        {
            float current = VehicleSpeed003.CornerWeightPounds(draft, gallons, i);
            loads[i].text = $"{current:0} lb";
            float? last = prior?.cornerWeightsPounds?.Length == 4 ? prior.cornerWeightsPounds[i] : null;
            oldLoads[i].text = last.HasValue && Mathf.Round(last.Value) != Mathf.Round(current) ? $"{last.Value:0} lb" : "";
            oldLoads[i].style.visibility = string.IsNullOrEmpty(oldLoads[i].text) ? Visibility.Hidden : Visibility.Visible;
            loadBadges[i].style.display = selected == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private void PositionLoads()
    {
        if (selected != 0) return;
        var carImage = board.Q<Image>(className: "garage-redesign-car");
        var area = carImage.layout;
        if (area.width <= 0 || area.height <= 0) return;
        float aspect = carImage.image == null ? .48f : (float)carImage.image.width / carImage.image.height;
        float width = Mathf.Min(area.width, area.height * aspect);
        float height = width / aspect;
        for (int i = 0; i < 4; i++)
        {
            float badgeWidth = loadBadges[i].layout.width;
            if (!float.IsFinite(badgeWidth)) continue;
            float wheelX = area.center.x + (i % 2 == 0 ? -.41f : .41f) * width;
            float wheelY = area.center.y + (i < 2 ? -.28f : .28f) * height;
            loadBadges[i].style.left = Mathf.Clamp(wheelX - badgeWidth * .5f, 0, Mathf.Max(0, area.width - badgeWidth));
            loadBadges[i].style.top = wheelY - 9f;
        }
    }

    private TrackSetup[] Presets()
    {
        var list = car.SetupsFor(track.id).ToArray();
        return list.Any(item => item.IsDefault) ? list :
            list.Prepend(new TrackSetup { trackId = track.id, setup = car.setup ?? new Setup() }).ToArray();
    }

    private void Load(TrackSetup preset)
    {
        ResetToPreset(preset, false);
        status.text = "Setup loaded.";
    }

    private void ResetToPreset(TrackSetup preset, bool resetSaved)
    {
        draft = RivalSetup001.Copy(preset?.setup ?? car.setup ?? new Setup());
        draft.weightDistribution ??= new WeightDistributionSetup();
        draft.frontSwayBar ??= new SwayBarSetup();
        draft.rearTrackBar ??= new TrackBarSetup();
        var defaults = new Setup();
        draft.leftFront ??= defaults.leftFront;
        draft.rightFront ??= defaults.rightFront;
        draft.leftRear ??= defaults.leftRear;
        draft.rightRear ??= defaults.rightRear;
        for (int i = 0; i < 4; i++)
        {
            Corner(draft, i).spring ??= Corner(defaults, i).spring;
            Corner(draft, i).shock ??= Corner(defaults, i).shock;
        }
        if (draft.coldTirePressures?.Length != 4)
        {
            var pressures = Enumerable.Range(0, 4).Select(i => VehicleSpeed003.ColdPressure(draft, i)).ToArray();
            draft.coldTirePressures = pressures;
        }
        setupName = preset?.IsDefault == false ? preset.DisplayName : null;
        if (resetSaved) CaptureSaved();
        notes.SetValueWithoutNotify(draft.notes ?? "");
        Refresh();
    }

    private void CaptureSaved()
    {
        saved = ReadDraft();
        savedName = setupName;
        savedFuel = fuel();
    }

    private string Save(string name)
    {
        if (locked()) return "Setup locked: qualifying has started.";
        string error = save(name, ReadDraft());
        if (error != null) return error;
        setupName = name;
        CaptureSaved();
        status.text = "Setup saved.";
        Refresh();
        return null;
    }

    private void Revert()
    {
        ShowDialog("Revert setup?", new[] { "Restore the last saved setup and discard all subsequent changes?" }, revertButton, () =>
        {
            if (locked()) return;
            draft = RivalSetup001.Copy(saved);
            setupName = savedName;
            setFuel(savedFuel);
            notes.SetValueWithoutNotify(draft.notes ?? "");
            status.text = "Setup restored.";
            Refresh();
        });
    }

    private void Help()
    {
        var run = lastRun();
        if (run == null)
        {
            ShowDialog("Crew chief suggestions", new[] { "Run some laps first so I have feedback to work with." }, Root.Q<Button>("help"));
            return;
        }
        var comments = GarageDebrief.DriverComments(run);
        var lines = Enumerable.Empty<string>();
        if (comments.Length > 0 && run?.handling?.Length == 3 && run.handling.All(p => p != null && p.seconds >= 5))
        {
            var basis = run.startDiagnostics?.setup ?? draft;
            var candidates = RivalSetupTuning.Rank(basis, track, car.chassisType,
                run.handling.Select(phase => phase.Average).ToArray(), run.endingTemperatures);
            lines = candidates.GroupBy(item => item.controlId).Select(group => group.First()).Take(3)
                .Select((item, i) =>
                {
                    int phase = Enumerable.Range(0, 3).Where(p => comments.Any(c => c.Phase == p))
                        .OrderByDescending(p => Math.Abs(run.handling[p].Average) -
                            Math.Abs(run.handling[p].Average + item.predictedPhaseChanges[p])).First();
                    string comment = comments.First(c => c.Phase == phase).Text;
                    string direction = item.afterValue > item.beforeValue ? "raise" : "lower";
                    float next = item.afterValue;
                    string units = " " + item.units;
                    if (item.units == "0-1")
                    {
                        next = GarageStep(ShockStep(item.beforeValue), direction, 1, 1, 10);
                        units = "";
                    }
                    else if (item.controlId.StartsWith("spring-", StringComparison.Ordinal))
                    {
                        int corner = item.controlId[item.controlId.Length - 1] - '0';
                        var range = SetupLimits.SpringRange(car.chassisType, corner);
                        next = GarageStep(Mathf.Round(item.beforeValue / 50) * 50, direction, 50, range.Min, range.Max);
                    }
                    string suggestion = $"{direction} {item.controlName} to {next:0.##}{units}";
                    return ((i + helpFormat) % 3) switch
                    {
                        0 => $"The driver reported \"{comment}\" I'd {suggestion} for the next run.",
                        1 => $"Based on \"{comment}\" my suggestion is to {suggestion}.",
                        _ => $"To address \"{comment}\" I'd try to {suggestion} and compare the next run."
                    };
                }).ToArray();
        }
        helpFormat++;
        ShowDialog("Crew chief suggestions", lines.DefaultIfEmpty("No useful change to suggest from the recorded driver feedback yet."), Root.Q<Button>("help"));
    }

    private static float GarageStep(float value, string direction, float step, float minimum, float maximum) =>
        Mathf.Clamp(value + (direction == "raise" ? step : -step), minimum, maximum);

    private void ShowDialog(string heading, System.Collections.Generic.IEnumerable<string> lines, Button source, Action confirm = null)
    {
        if (screen.parent == null || closeDialog != null) return;
        var overlay = new VisualElement();
        overlay.AddToClassList("settings-confirmation-overlay");
        overlay.AddToClassList("garage-styled");
        foreach (string path in new[] { "Startup/Startup", "Settings/SettingsControls", "Startup/GarageStyles" })
        {
            var sheet = Resources.Load<StyleSheet>(path);
            if (sheet != null) overlay.styleSheets.Add(sheet);
        }
        var dialog = Group(overlay, "settings-confirmation-dialog");
        dialog.AddToClassList("garage-run-dialog");
        Text(dialog, heading, "settings-group-heading");
        foreach (string line in lines)
            Text(dialog, line, "garage-redesign-dialog-text");
        var footer = Group(dialog, "settings-footer");
        void Close()
        {
            closeDialog = null;
            overlay.RemoveFromHierarchy();
            screen.SetEnabled(true);
            source?.Focus();
        }
        var dismiss = SettingsFormStyle.Button(confirm == null ? "Close" : "Cancel", Close);
        dismiss.tooltip = "Dismiss this dialog.";
        footer.Add(dismiss);
        if (confirm != null)
        {
            var accept = SettingsFormStyle.Button("Revert", () => { Close(); confirm(); }, primary: true);
            accept.tooltip = "Restore the last saved setup.";
            footer.Add(accept);
        }
        overlay.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Escape) return;
            Close();
            evt.StopPropagation();
        }, TrickleDown.TrickleDown);
        closeDialog = Close;
        screen.parent.Add(overlay);
        screen.SetEnabled(false);
        SettingsFormStyle.AddHoverTooltips(overlay);
        dismiss.Focus();
    }

    private static Button IconButton(VisualElement parent, string name, string tip, int icon, Action action)
    {
        var button = SettingsFormStyle.Button("", action);
        button.name = name.Replace(' ', '-').ToLowerInvariant();
        button.tooltip = $"{name}. {tip}";
        button.AddToClassList("garage-redesign-icon-button");
        if (icon != 9 && icon != 10) SetBorder(button, ButtonColors[icon]);
        var drawing = new VisualElement { pickingMode = PickingMode.Ignore };
        drawing.AddToClassList("garage-redesign-icon");
        drawing.generateVisualContent += ctx => DrawIcon(ctx.painter2D, icon);
        button.Add(drawing);
        parent.Add(button);
        return button;
    }

    private static void SetBorder(VisualElement element, Color accent)
    {
        element.style.borderTopColor = accent;
        element.style.borderBottomColor = accent;
        element.style.borderLeftColor = accent;
        element.style.borderRightColor = accent;
    }

    private static void DrawIcon(Painter2D p, int icon)
    {
        p.strokeColor = ButtonColors[icon];
        p.lineWidth = 1.5f;
        p.lineJoin = LineJoin.Round;
        p.lineCap = LineCap.Round;
        void Line(params Vector2[] points)
        {
            p.BeginPath();
            p.MoveTo(points[0]);
            foreach (var point in points.Skip(1)) p.LineTo(point);
            p.Stroke();
        }
        void Ring(float x, float y, float radius)
        {
            p.BeginPath();
            p.Arc(new Vector2(x, y), radius, 0, 360);
            p.Stroke();
        }
        switch (icon)
        {
            case 0:
                Line(new(3, 7), new(19, 7)); Line(new(11, 3), new(11, 19)); Line(new(6, 19), new(16, 19));
                Line(new(5, 7), new(2, 14), new(8, 14), new(5, 7)); Line(new(17, 7), new(14, 14), new(20, 14), new(17, 7)); break;
            case 1:
                Line(new(6, 3), new(16, 3)); Line(new(11, 3), new(6, 6), new(16, 9), new(6, 12), new(16, 15), new(6, 18), new(11, 20)); Line(new(6, 20), new(16, 20)); break;
            case 2:
                Line(new(8, 3), new(14, 3)); Line(new(11, 3), new(11, 9));
                Line(new(8, 9), new(14, 9), new(14, 17), new(8, 17), new(8, 9)); Line(new(11, 17), new(11, 21)); break;
            case 3:
                Ring(11, 11, 9); Ring(11, 11, 5); break;
            case 4:
                Line(new(2, 16), new(5, 7), new(17, 7), new(20, 16)); Line(new(7, 5), new(7, 10)); Line(new(15, 5), new(15, 10)); break;
            case 5:
                Line(new(3, 19), new(3, 3), new(13, 3), new(13, 19), new(3, 19)); Line(new(3, 9), new(13, 9)); Line(new(13, 11), new(17, 11), new(17, 17), new(20, 17), new(20, 8), new(17, 4)); break;
            case 6:
                Line(new(2, 18), new(2, 5), new(8, 5), new(10, 8), new(20, 8), new(20, 18), new(2, 18));
                Line(new(11, 10), new(11, 16), new(8, 13)); Line(new(11, 16), new(14, 13)); break;
            case 7:
            case 8:
                Line(new(3, 20), new(3, 3), new(16, 3), new(20, 7), new(20, 20), new(3, 20));
                Line(new(6, 3), new(6, 9), new(15, 9), new(15, 3)); Line(new(6, 20), new(6, 13), new(15, 13), new(15, 20));
                if (icon == 8) { Line(new(13, 17), new(21, 17)); Line(new(17, 13), new(17, 21)); } break;
            case 9:
                p.BeginPath(); p.Arc(new Vector2(11, 11), 7, 220, 535); p.Stroke();
                Line(new(4, 3), new(4, 8), new(9, 8)); break;
            case 10:
                Ring(11, 11, 9); Line(new(8, 8), new(9, 6), new(13, 6), new(14, 8), new(11, 11), new(11, 13)); Ring(11, 16, .5f); break;
            case 11:
                Line(new(3, 4), new(3, 18), new(20, 18));
                Line(new(5, 14), new(9, 10), new(13, 12), new(19, 6)); break;
        }
    }

    internal static VisualElement Group(VisualElement parent, string style)
    {
        var group = new VisualElement();
        group.AddToClassList(style);
        parent.Add(group);
        return group;
    }

    internal static Label Text(VisualElement parent, string text, string style)
    {
        var label = new Label(text);
        if (style != null) label.AddToClassList(style);
        parent.Add(label);
        return label;
    }
}
