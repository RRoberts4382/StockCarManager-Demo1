using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UIElements;

internal sealed class GameSettings
{
    private enum SettingsTab { Display, Simulation, TeamColors, ColorScheme }

    private const string Key = "StockCarManager.Settings.";
    // Keep the original preference key so existing choices carry over.
    private const string TrackMsaaKey = Key + "InCarMsaaSamples";
    private static readonly int[] TrackMsaaChoices = { 1, 4, 8 };
    private static readonly string[] ColorLabels =
        { "Background", "Surface", "Button", "Text", "Divider", "Team heading" };
    private static readonly string[] Defaults =
        { "#151515", "#222222", "#454545", "#F5F5F5", "#B6B6B6", "#009BDD" };
    private static readonly string[] Sizes =
        { "Desktop", "1024 × 768", "1280 × 720", "1366 × 768", "1600 × 900", "1920 × 1080", "2560 × 1440", "3840 × 2160" };
    private static readonly Vector2Int[] Resolutions =
    {
        Vector2Int.zero, new Vector2Int(1024, 768), new Vector2Int(1280, 720), new Vector2Int(1366, 768),
        new Vector2Int(1600, 900), new Vector2Int(1920, 1080), new Vector2Int(2560, 1440), new Vector2Int(3840, 2160)
    };

    private readonly VisualElement root;
    private readonly DropdownField mode = new DropdownField("Display mode",
        new List<string> { "Full Screen", "Borderless Window" }, 0);
    private readonly DropdownField size = new DropdownField("Screen size", new List<string>(Sizes), 0);
    private readonly DropdownField simulationModel = new DropdownField("Car simulation model",
        CarSimulationModels.Available.Select(model => model.Name).ToList(), 0);
    private readonly DropdownField vehicleSpeedModel = new DropdownField("Vehicle Speed Model",
        VehicleSpeedModels.Available.Select(model => model.Name).ToList(), 0);
    private readonly DropdownField paceVehicle = new DropdownField("Pace vehicle",
        new List<string> { "Impala", "Ford F-350" }, 0);
    private readonly DropdownField vehicleBehaviorModel = new DropdownField("Vehicle Behavior Model",
        VehicleBehaviorModels.Available.Select(model => model.Name).ToList(), 0);
    private readonly TextField[] colorFields = new TextField[Defaults.Length];
    private readonly Action[] refreshColors = new Action[Defaults.Length];
    private readonly Label status = new Label();
    private readonly Dictionary<SettingsTab, (Button Button, VisualElement Page)> tabs = new();
    private readonly Button revertButton;
    private readonly Button saveButton;
    private SettingsTab activeTab;
    private readonly Action applyDisplay;
    private readonly Func<string, string, string, CarManufacturer, EngineSupplier, string> saveTeamColors;
    private readonly VisualElement teamColors = new VisualElement();
    private Career teamCareer;
    private readonly TextField teamName = new TextField("Team name") { maxLength = 25 };
    private Button teamManufacturer;
    private Button teamSupplier;
    private CarManufacturer draftManufacturer;
    private EngineSupplier draftSupplier;
    private string savedTeamName;
    private CarManufacturer savedManufacturer;
    private EngineSupplier savedSupplier;
    private Action<string> teamNameError;
    private bool showTeamErrors;

    private readonly TextField primaryTeamColor = new TextField("Primary") { style = { flexGrow = 1 } };
    private readonly TextField secondaryTeamColor = new TextField("Secondary") { style = { flexGrow = 1 } };
    private Action refreshPrimaryTeamColor;
    private Action refreshSecondaryTeamColor;
    private string[] savedColors;
    private string savedPrimaryTeamColor;
    private string savedSecondaryTeamColor;
    private int savedMode;
    private int savedSize;
    private int savedSimulationModel;
    private int savedSpeedModel;
    public bool EqualConditionPractice { get; private set; }
    private readonly Toggle equalConditionPractice = new Toggle("Equal-condition practice testing");
    private int savedBehaviorModel;
    private int savedPaceVehicle;
    private int savedCarLod;
    private int draftCarLod;
    private readonly Button carLodButton;
    public int CarLod => savedCarLod;
    public event Action<int> CarLodChanged;
    private readonly Button trackMsaaButton;
    private int savedTrackMsaaSamples;
    private int draftTrackMsaaSamples;
    public int TrackMsaaSamples => savedTrackMsaaSamples;
    public event Action<int> TrackMsaaSamplesChanged;
    public bool UsePaceTruck => savedPaceVehicle == 1;

    public IVehicleBehaviorModel BehaviorModel => VehicleBehaviorModels.Available[savedBehaviorModel];

    public IVehicleSpeedModel SpeedModel => VehicleSpeedModels.Available[savedSpeedModel];

    public ICarSimulationModel SimulationModel => CarSimulationModels.Available[savedSimulationModel];

    public VisualElement ScreenView { get; } = new VisualElement();

    public GameSettings(VisualElement root, Action applyDisplay, Action close,
        Func<string, string, string, CarManufacturer, EngineSupplier, string> saveTeamColors, Action<Button> openRivals)
    {
        this.root = root;
        this.applyDisplay = applyDisplay;
        this.saveTeamColors = saveTeamColors;
        savedMode = Mathf.Clamp(PlayerPrefs.GetInt(Key + "Mode"), 0, 1);
        savedSize = Mathf.Clamp(PlayerPrefs.GetInt(Key + "Size"), 0, Sizes.Length - 1);
        // Enable full flag/stage behavior once; later manual A/B choices remain persistent.
        if (!PlayerPrefs.HasKey(Key + "TrackState001FlagsEnabled"))
        {
            PlayerPrefs.SetString(Key + "SimulationModel", new TrackState001().Id);
            PlayerPrefs.SetInt(Key + "TrackState001FlagsEnabled", 1);
            PlayerPrefs.Save();
        }
        savedSimulationModel = CarSimulationModels.IndexForId(PlayerPrefs.GetString(Key + "SimulationModel"));
        // One-time switch for setup testing. Later user selections remain persistent.
        if (!PlayerPrefs.HasKey(Key + "VehicleSpeed003SetupTesting"))
        {
            PlayerPrefs.SetString(Key + "VehicleSpeedModel", "vehicle-speed-003");
            PlayerPrefs.SetInt(Key + "VehicleSpeed003SetupTesting", 1);
            PlayerPrefs.Save();
        }
        savedSpeedModel = VehicleSpeedModels.IndexForId(PlayerPrefs.GetString(Key + "VehicleSpeedModel"));
        // End the temporary parity test once, while retaining its optional switch.
        if (!PlayerPrefs.HasKey(Key + "RivalSetup001Enabled"))
        {
            PlayerPrefs.SetInt(Key + "EqualConditionPractice", 0);
            PlayerPrefs.SetInt(Key + "RivalSetup001Enabled", 1);
            PlayerPrefs.Save();
        }
        // Restore normal practice after rival setup development tests, once per installation.
        // Keep the optional parity switch available for deliberate future comparisons.
        if (!PlayerPrefs.HasKey(Key + "NormalPracticeDefaults001"))
        {
            PlayerPrefs.SetInt(Key + "EqualConditionPractice", 0);
            PlayerPrefs.SetInt(Key + "NormalPracticeDefaults001", 1);
            PlayerPrefs.Save();
        }
        EqualConditionPractice = PlayerPrefs.GetInt(Key + "EqualConditionPractice", 0) != 0;
        // Select the new development baseline once, then respect later A/B selections.
        if (!PlayerPrefs.HasKey(Key + "VehicleBehavior002Enabled"))
        {
            PlayerPrefs.SetString(Key + "VehicleBehaviorModel", "vehicle-behavior-002");
            PlayerPrefs.SetInt(Key + "VehicleBehavior002Enabled", 1);
            PlayerPrefs.Save();
        }
        // Move to the copied baseline once; preserve subsequent manual A/B selections.
        if (!PlayerPrefs.HasKey(Key + "VehicleBehavior003Enabled"))
        {
            PlayerPrefs.SetString(Key + "VehicleBehaviorModel", "vehicle-behavior-003");
            PlayerPrefs.SetInt(Key + "VehicleBehavior003Enabled", 1);
            PlayerPrefs.Save();
        }
        // Select 004 once and keep subsequent manual model selections.
        if (!PlayerPrefs.HasKey(Key + "VehicleBehavior004Enabled"))
        {
            PlayerPrefs.SetString(Key + "VehicleBehaviorModel", VehicleBehavior004.ModelId);
            PlayerPrefs.SetInt(Key + "VehicleBehavior004Enabled", 1);
            PlayerPrefs.Save();
        }
        // Select 006 once and keep subsequent manual model selections.
        if (!PlayerPrefs.HasKey(Key + "VehicleBehavior006Enabled"))
        {
            PlayerPrefs.SetString(Key + "VehicleBehaviorModel", VehicleBehavior006.ModelId);
            PlayerPrefs.SetInt(Key + "VehicleBehavior006Enabled", 1);
            PlayerPrefs.Save();
        }
        savedBehaviorModel = VehicleBehaviorModels.IndexForId(PlayerPrefs.GetString(Key + "VehicleBehaviorModel"));
        savedPaceVehicle = Mathf.Clamp(PlayerPrefs.GetInt(Key + "PaceVehicle"), 0, paceVehicle.choices.Count - 1);
        savedCarLod = Mathf.Clamp(PlayerPrefs.GetInt(Key + "CarLOD", 3), 1, 5);
        draftCarLod = savedCarLod;
        savedTrackMsaaSamples = draftTrackMsaaSamples = LoadTrackMsaaSamples();
        savedColors = new string[Defaults.Length];
        for (int i = 0; i < Defaults.Length; i++)
        {
            string saved = PlayerPrefs.GetString(Key + "Color." + i, Defaults[i]);
            savedColors[i] = TryColor(saved, out _) ? saved : Defaults[i];
        }

        SettingsFormStyle.Apply(ScreenView);
        var frame = new VisualElement();
        frame.AddToClassList("settings-frame");
        ScreenView.Add(frame);

        var header = new VisualElement();
        header.AddToClassList("settings-header");
        header.Add(SettingsFormStyle.Label("Game Settings", "settings-title"));
        frame.Add(header);

        var navigation = new VisualElement();
        navigation.AddToClassList("settings-tabs");
        frame.Add(navigation);

        var form = new ScrollView();
        form.AddToClassList("settings-scroll");
        frame.Add(form);

        var display = AddTab(SettingsTab.Display, "Display");
        display.Add(SettingsFormStyle.Field(mode, mode.value, _ => RefreshRevert()));
        display.Add(SettingsFormStyle.Field(size, size.value, _ => RefreshRevert()));

        var lodField = new VisualElement();
        lodField.AddToClassList("settings-field");
        lodField.Add(SettingsFormStyle.Label("Car LOD", "settings-note"));
        carLodButton = SettingsFormStyle.Button(CarLodLabel(draftCarLod), () => OpenCarLodPicker(frame));
        carLodButton.tooltip = "Choose car geometry detail from 1 (lowest) to 5 (full). Applies to the whole field. Level 3 is the default.";
        lodField.Add(carLodButton);
        display.Add(lodField);
        var msaaField = new VisualElement();
        msaaField.AddToClassList("settings-field");
        msaaField.Add(SettingsFormStyle.Label("Track Anti-Aliasing", "settings-note"));
        trackMsaaButton = SettingsFormStyle.Button(TrackMsaaLabel(draftTrackMsaaSamples), () =>
            OpenTrackMsaaPicker(ScreenView, frame, trackMsaaButton, draftTrackMsaaSamples, samples =>
            {
                draftTrackMsaaSamples = samples;
                trackMsaaButton.text = TrackMsaaLabel(samples);
                RefreshRevert();
            }));
        trackMsaaButton.name = "trackMsaaButton";
        trackMsaaButton.tooltip = "Choose how smoothly thin lines and edges render in the track view.";
        msaaField.Add(trackMsaaButton);
        display.Add(msaaField);

        simulationModel.tooltip = "Controls how live career sessions run. Basic Simulation uses simpler session flow. " +
            "TrackState001 adds staged race starts, flags and restarts, stage breaks, and pit procedures. Test Practice always uses TrackState001.";
        vehicleSpeedModel.tooltip = "Changes car pace and lap times. VehicleSpeed001 uses a baseline pace with component adjustments. " +
            "002 adds a corner-by-corner speed profile in live sessions. 003 also models setup, tire grip and load, and how fuel weight affects acceleration and braking.";
        vehicleBehaviorModel.tooltip = "Changes how cars choose a racing line, follow traffic, and pass in live sessions. " +
            "VehicleBehavior001 uses basic traffic checks. 002 checks closing speeds and clearance through a lane change. " +
            "003 also adjusts racing lines and passing decisions using track layout, driver aggression, and race progress. " +
            "004 adds sweeping lines on shaped oval frontstretches. 006 anticipates changes in the preferred oval line through corner entry and exit.";

        var simulation = AddTab(SettingsTab.Simulation, "Simulation");
        simulation.Add(SettingsFormStyle.Field(simulationModel, simulationModel.value, _ => RefreshRevert()));
        simulation.Add(SettingsFormStyle.Field(vehicleSpeedModel, vehicleSpeedModel.value, _ => RefreshRevert()));
        simulation.Add(SettingsFormStyle.Field(vehicleBehaviorModel, vehicleBehaviorModel.value, _ => RefreshRevert()));
        simulation.Add(SettingsFormStyle.Field(paceVehicle, paceVehicle.value, _ => RefreshRevert()));
        equalConditionPractice.tooltip = "New practice sessions: all cars use the player setup/component levels and wear, fresh tires, no damage, and full fuel. Restart practice for a fresh comparison.";
        simulation.Add(SettingsFormStyle.Field(equalConditionPractice, equalConditionPractice.value, _ => RefreshRevert()));

        AddTab(SettingsTab.TeamColors, "Team", teamColors);
        var identity = new VisualElement();
        identity.AddToClassList("settings-row");
        teamName.style.flexGrow = 1;
        teamName.style.flexBasis = 0;
        identity.Add(SettingsFormStyle.StyleField(teamName));
        teamNameError = SettingsFormStyle.InlineError(teamName);
        teamName.RegisterValueChangedCallback(_ => RefreshValidation());
        teamName.tooltip = "Change your team name (up to 25 characters).";
        teamColors.Add(identity);
        var choices = new VisualElement();
        choices.AddToClassList("settings-row");
        teamManufacturer = AddTeamSelection(choices, "Manufacturer", () =>
        {
            var values = teamCareer.SeriesRules.ManufacturerChoices;
            OpenTeamChoice("Choose manufacturer", values.Select(value => value.ToString()).ToArray(), index =>
            {
                draftManufacturer = values[index];
                var suppliers = teamCareer.SeriesRules.SuppliersFor(draftManufacturer);
                draftSupplier = suppliers.FirstOrDefault(value => value.name == draftSupplier?.name) ?? suppliers.FirstOrDefault();
                RefreshTeamChoices();
                RefreshValidation();
            });
        });
        teamSupplier = AddTeamSelection(choices, "Engine supplier", () =>
        {
            var values = teamCareer.SeriesRules.SuppliersFor(draftManufacturer);
            OpenTeamChoice("Choose engine supplier", values.Select(value => value.name).ToArray(), index =>
            {
                draftSupplier = values[index];
                RefreshTeamChoices();
                RefreshValidation();
            });
        });
        teamManufacturer.tooltip = "Choose a manufacturer available in this team's series. Updates your team cars after saving.";
        teamSupplier.tooltip = "Choose an engine supplier for this series and manufacturer. Changes engine power, reliability, and running costs after saving.";
        teamColors.Add(choices);
        refreshPrimaryTeamColor = AddColorField(teamColors, primaryTeamColor, () => PickColor(primaryTeamColor, Color.white), RefreshValidation);
        refreshSecondaryTeamColor = AddColorField(teamColors, secondaryTeamColor, () => PickColor(secondaryTeamColor, Color.white), RefreshValidation);

        var appearance = AddTab(SettingsTab.ColorScheme, "Color scheme");
        appearance.Add(SettingsFormStyle.Label("Enter colors as #RRGGBB.", "settings-note"));
        for (int i = 0; i < colorFields.Length; i++)
        {
            int index = i;
            var field = new TextField(ColorLabels[i]);
            colorFields[i] = field;
            refreshColors[i] = AddColorField(appearance, field, () => PickColor(index), () =>
            {
                if (ReadPalette(out var palette))
                    ApplyPalette(palette);
                RefreshValidation();
            });
        }
        appearance.Add(SettingsFormStyle.Button("Reset Colors", () =>
        {
            SetColorFields(Defaults);
            RefreshValidation();
        }));

        var footer = new VisualElement();
        footer.AddToClassList("settings-footer");
        status.AddToClassList("settings-status");
        footer.Add(status);
        var rivals = SettingsFormStyle.Tab("Rivals", null);
        rivals.tooltip = "Edit the shared rival roster for each racing series.";
        rivals.clicked += () => openRivals(rivals);
        navigation.Add(rivals);

        var backButton = SettingsFormStyle.Button("Back", null);
        footer.Add(backButton);
        revertButton = SettingsFormStyle.Button("Revert", () =>
        {
            RestoreSavedValues();
            SettingsFormStyle.SetValidationMessage(status, "Reverted unsaved changes.", false);
        });
        revertButton.SetEnabled(false);
        footer.Add(revertButton);
        saveButton = SettingsFormStyle.Button("Save Settings", () => Save(), primary: true);
        footer.Add(saveButton);
        frame.Add(footer);

        var warning = new VisualElement();
        warning.AddToClassList("settings-confirmation-overlay");
        warning.style.display = DisplayStyle.None;
        var dialog = new VisualElement();
        dialog.AddToClassList("settings-confirmation-dialog");
        var warningTitle = SettingsFormStyle.Label("Unsaved changes", "settings-group-heading");
        warningTitle.AddToClassList("google-sans-semibold");
        dialog.Add(warningTitle);
        dialog.Add(SettingsFormStyle.Label("Save or discard unsaved changes to Game Settings.", "settings-note"));
        var warningError = SettingsFormStyle.Label("", "settings-validation-summary");
        dialog.Add(warningError);
        var actions = new VisualElement();
        actions.AddToClassList("settings-row");
        actions.AddToClassList("settings-confirmation-actions");
        var keepEditing = SettingsFormStyle.Button("Keep Editing", () =>
        {
            DismissWarning();
            backButton.Focus();
        });
        var saveBack = SettingsFormStyle.Button("Save and Go Back", () =>
        {
            if (!saveButton.enabledSelf)
                return;
            if (Save())
                FinishBack();
            else
                SettingsFormStyle.SetValidationMessage(warningError, status.text, true);
        }, primary: true);
        actions.Add(keepEditing);
        actions.Add(SettingsFormStyle.Button("Discard and Go Back", FinishBack));
        actions.Add(saveBack);
        dialog.Add(actions);
        warning.Add(dialog);
        warning.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Escape) return;
            DismissWarning();
            backButton.Focus();
            evt.StopPropagation();
        });
        ScreenView.Add(warning);
        backButton.clicked += RequestBack;

        void RequestBack()
        {
            if (!HasUnsavedChanges())
            {
                close();
                return;
            }

            showTeamErrors = true;
            RefreshValidation();
            bool canSave = saveButton.enabledSelf;
            saveBack.SetEnabled(canSave);
            SettingsFormStyle.SetValidationMessage(warningError,
                canSave ? null : "Fix the highlighted fields before saving.", !canSave);
            frame.SetEnabled(false);
            warning.style.display = DisplayStyle.Flex;
            keepEditing.Focus();
        }

        void DismissWarning()
        {
            warning.style.display = DisplayStyle.None;
            frame.SetEnabled(true);
        }

        void FinishBack()
        {
            DismissWarning();
            close();
        }

        VisualElement AddTab(SettingsTab tab, string title, VisualElement group = null)
        {
            group ??= new VisualElement();
            group.AddToClassList("settings-group");
            group.Add(SettingsFormStyle.Label(title, "settings-group-heading"));
            form.Add(group);

            var button = SettingsFormStyle.Tab(title, () =>
            {
                ShowTab(tab);
                form.scrollOffset = Vector2.zero;
            });
            navigation.Add(button);
            tabs.Add(tab, (button, group));
            return group;
        }
    }

    public void SaveCarLod(int level)
    {
        savedCarLod = draftCarLod = Mathf.Clamp(level, 1, 5);
        carLodButton.text = CarLodLabel(savedCarLod);
        PlayerPrefs.SetInt(Key + "CarLOD", savedCarLod);
        PlayerPrefs.Save();
        CarLodChanged?.Invoke(savedCarLod);
        RefreshRevert();
    }

    internal static int LoadTrackMsaaSamples() => NormalizeTrackMsaaSamples(PlayerPrefs.GetInt(TrackMsaaKey, 8));

    private static int NormalizeTrackMsaaSamples(int samples) => samples is 1 or 4 or 8 ? samples : 8;

    internal static string TrackMsaaLabel(int samples) => samples == 1 ? "Off" : samples + "x";

    internal void SaveTrackMsaaSamples(int samples)
    {
        samples = NormalizeTrackMsaaSamples(samples);
        if (savedTrackMsaaSamples == samples)
            return;

        savedTrackMsaaSamples = draftTrackMsaaSamples = samples;
        trackMsaaButton.text = TrackMsaaLabel(samples);
        PlayerPrefs.SetInt(TrackMsaaKey, samples);
        PlayerPrefs.Save();
        TrackMsaaSamplesChanged?.Invoke(samples);
        RefreshRevert();
    }

    internal static Action OpenTrackMsaaPicker(VisualElement host, VisualElement underlying, Button source,
        int currentSamples, Action<int> select, Action closed = null)
    {
        var overlay = new VisualElement { name = "trackMsaaDialog" };
        if (!host.ClassListContains("settings-form"))
            SettingsFormStyle.Apply(overlay);
        overlay.AddToClassList("settings-confirmation-overlay");
        var dialog = new VisualElement();
        dialog.AddToClassList("settings-confirmation-dialog");
        dialog.AddToClassList("settings-msaa-dialog");
        dialog.Add(SettingsFormStyle.Label("Track Anti-Aliasing", "settings-group-heading"));

        Button current = null;
        foreach (int samples in TrackMsaaChoices)
        {
            bool selected = samples == currentSamples;
            var choice = SettingsFormStyle.Button(TrackMsaaLabel(samples) + (selected ? " (Selected)" : ""), () =>
            {
                select(samples);
                Close();
            });
            choice.name = "trackMsaa" + samples;
            choice.AddToClassList("settings-msaa-choice");
            choice.tooltip = samples == 1
                ? "Render without multisampling for the lowest GPU cost."
                : $"Use up to {samples} samples per pixel to smooth thin lines and edges.";
            dialog.Add(choice);
            if (selected)
                current = choice;
        }

        var footer = new VisualElement();
        footer.AddToClassList("settings-footer");
        var cancel = SettingsFormStyle.Button("Cancel", Close);
        cancel.tooltip = "Close the selector and keep the current anti-aliasing setting.";
        footer.Add(cancel);
        dialog.Add(footer);
        overlay.Add(dialog);

        overlay.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Escape)
                return;
            Close();
            evt.StopPropagation();
        });
        underlying.SetEnabled(false);
        host.Add(overlay);
        (current ?? cancel).Focus();
        return Close;

        void Close()
        {
            if (overlay.parent == null)
                return;

            overlay.RemoveFromHierarchy();
            underlying.SetEnabled(true);
            closed?.Invoke();
            source.Focus();
        }
    }

    private static string CarLodLabel(int level) => "Level " + level + " · " + TrackPreview.CarDetailLabels[level - 1];

    private Button AddTeamSelection(VisualElement row, string label, Action select)
    {
        var field = new VisualElement();
        field.AddToClassList("settings-field");
        field.style.flexGrow = 1;
        field.style.flexBasis = 0;
        field.Add(SettingsFormStyle.Label(label, "settings-note"));
        var button = SettingsFormStyle.Button("Choose", select);
        field.Add(button);
        row.Add(field);
        return button;
    }

    private void RefreshTeamChoices()
    {
        teamManufacturer.text = draftManufacturer.ToString();
        teamSupplier.text = draftSupplier?.name ?? "No supplier available";
        teamSupplier.parent.style.display = teamCareer.SeriesRules.SuppliersFor(draftManufacturer).Length == 1
            ? DisplayStyle.None : DisplayStyle.Flex;
    }

    private void OpenTeamChoice(string title, string[] choices, Action<int> choose)
    {
        var overlay = new VisualElement();
        overlay.AddToClassList("settings-confirmation-overlay");
        var dialog = new VisualElement();
        dialog.AddToClassList("settings-confirmation-dialog");
        dialog.style.backgroundColor = (Color)new Color32(16, 21, 30, 255);
        var trim = new StyleColor(new Color32(0, 155, 221, 255));
        dialog.style.borderTopColor = dialog.style.borderBottomColor =
            dialog.style.borderLeftColor = dialog.style.borderRightColor = trim;
        dialog.Add(SettingsFormStyle.Label(title, "settings-group-heading"));
        for (int i = 0; i < choices.Length; i++)
        {
            int index = i;
            var button = SettingsFormStyle.Button(choices[i], () => { choose(index); Close(); });
            button.tooltip = "Select " + choices[i] + ".";
            dialog.Add(button);
        }
        dialog.Add(SettingsFormStyle.Button("Cancel", Close));
        overlay.Add(dialog);
        foreach (var child in ScreenView.Children()) child.SetEnabled(false);
        ScreenView.Add(overlay);
        dialog.Query<Button>().First()?.Focus();
        overlay.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Escape) return;
            Close();
            evt.StopPropagation();
        });
        void Close()
        {
            overlay.RemoveFromHierarchy();
            foreach (var child in ScreenView.Children()) child.SetEnabled(true);
            teamManufacturer.Focus();
        }
    }

    private void OpenCarLodPicker(VisualElement frame)
    {
        var overlay = new VisualElement();
        overlay.AddToClassList("settings-confirmation-overlay");
        var dialog = new VisualElement();
        dialog.AddToClassList("settings-confirmation-dialog");
        dialog.style.backgroundColor = new Color(16f / 255f, 21f / 255f, 30f / 255f);
        var trim = new StyleColor(new Color(0, 155f / 255f, 221f / 255f));
        dialog.style.borderTopColor = dialog.style.borderBottomColor =
            dialog.style.borderLeftColor = dialog.style.borderRightColor = trim;
        dialog.Add(SettingsFormStyle.Label("Car LOD", "settings-group-heading"));
        dialog.Add(SettingsFormStyle.Label("Higher levels show more geometry and require more rendering work. Applies to cars in the track view. Other car displays use their highest configured LOD.", "settings-note"));
        for (int level = 1; level <= 5; level++)
        {
            int selected = level;
            var row = SettingsFormStyle.Button(CarLodLabel(level) + (level == draftCarLod ? " · Selected" : ""), () =>
            {
                draftCarLod = selected;
                carLodButton.text = CarLodLabel(selected);
                RefreshRevert();
                Close();
            });
            row.tooltip = "Use " + CarLodLabel(level) + " after saving settings.";
            dialog.Add(row);
        }
        dialog.Add(SettingsFormStyle.Button("Cancel", Close));
        overlay.Add(dialog);
        overlay.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Escape) return;
            Close();
            evt.StopPropagation();
        });
        frame.SetEnabled(false);
        ScreenView.Add(overlay);
        dialog.Query<Button>().First()?.Focus();
        void Close()
        {
            overlay.RemoveFromHierarchy();
            frame.SetEnabled(true);
            carLodButton.Focus();
        }
    }

    private void ShowTab(SettingsTab selected)
    {
        activeTab = selected;
        foreach (var (tab, (button, page)) in tabs)
        {
            bool current = tab == selected;
            button.EnableInClassList("settings-tab-current", current);
            page.style.display = current ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private void RefreshValidation()
    {
        bool invalidPalette = colorFields.Any(field => !TryColor(field.value, out _));
        string nameError = SettingsFormStyle.TextError(teamName.value) ??
            (teamName.value.Length > 25 ? "Use 25 characters or fewer." : null);
        teamNameError?.Invoke(showTeamErrors && HasTeamColors ? nameError : null);
        bool invalidTeamColors = HasTeamColors &&
            (nameError != null || draftSupplier == null ||
             !teamCareer.SeriesRules.ManufacturerChoices.Contains(draftManufacturer) ||
             !teamCareer.SeriesRules.SuppliersFor(draftManufacturer).Any(value => value.name == draftSupplier?.name) || !TryColor(primaryTeamColor.value, out _) || !TryColor(secondaryTeamColor.value, out _));
        SettingsFormStyle.SetTabError(tabs[SettingsTab.ColorScheme].Button, invalidPalette);
        SettingsFormStyle.SetTabError(tabs[SettingsTab.TeamColors].Button, invalidTeamColors);
        saveButton.SetEnabled(!invalidPalette && !invalidTeamColors);
        RefreshRevert();
    }

    private void RefreshRevert() => revertButton.SetEnabled(HasUnsavedChanges());

    private bool HasTeamColors => tabs[SettingsTab.TeamColors].Button.style.display != DisplayStyle.None;

    private static Action AddColorField(VisualElement group, TextField field, Action pick, Action preview = null)
    {
        var row = new VisualElement();
        row.AddToClassList("settings-row");
        var swatch = SettingsFormStyle.Button(string.Empty, pick);
        swatch.tooltip = $"Choose {field.label.ToLowerInvariant()} color";
        swatch.style.width = 42;
        swatch.style.height = 42;
        swatch.style.marginBottom = 5;

        row.Add(SettingsFormStyle.StyleField(field));
        var showError = SettingsFormStyle.InlineError(field);
        void UpdateColor()
        {
            bool valid = TryColor(field.value, out var color);
            showError(valid ? null : "Enter a six-digit hex color, such as #009BDD.");
            if (valid)
                swatch.style.backgroundColor = color;
        }
        field.RegisterValueChangedCallback(_ =>
        {
            UpdateColor();
            preview?.Invoke();
        });
        row.Add(swatch);
        group.Add(row);
        return UpdateColor;
    }

    public void Open(Career career)
    {
        teamCareer = career;
        showTeamErrors = false;
        tabs[SettingsTab.TeamColors].Button.style.display = career == null ? DisplayStyle.None : DisplayStyle.Flex;
        if (career != null)
        {
            savedTeamName = career.teamName;
            savedManufacturer = career.cars[0].manufacturer;
            savedSupplier = career.EngineSupplierFor(career.cars[0]);
            var colors = career.TeamColors;
            savedPrimaryTeamColor = "#" + ColorUtility.ToHtmlStringRGB(colors.Primary);
            savedSecondaryTeamColor = "#" + ColorUtility.ToHtmlStringRGB(colors.Secondary);
        }
        RestoreSavedValues();
        ShowTab(!HasTeamColors && activeTab == SettingsTab.TeamColors ? SettingsTab.Display : activeTab);
        SettingsFormStyle.SetValidationMessage(status, null, false);
    }

    private void RestoreSavedValues()
    {
        draftCarLod = savedCarLod;
        carLodButton.text = CarLodLabel(draftCarLod);
        draftTrackMsaaSamples = savedTrackMsaaSamples;
        trackMsaaButton.text = TrackMsaaLabel(draftTrackMsaaSamples);
        mode.SetValueWithoutNotify(mode.choices[savedMode]);
        size.SetValueWithoutNotify(size.choices[savedSize]);
        simulationModel.SetValueWithoutNotify(simulationModel.choices[savedSimulationModel]);
        vehicleSpeedModel.SetValueWithoutNotify(vehicleSpeedModel.choices[savedSpeedModel]);
        equalConditionPractice.SetValueWithoutNotify(EqualConditionPractice);
        vehicleBehaviorModel.SetValueWithoutNotify(vehicleBehaviorModel.choices[savedBehaviorModel]);
        paceVehicle.SetValueWithoutNotify(paceVehicle.choices[savedPaceVehicle]);
        SetColorFields(savedColors);
        if (HasTeamColors)
        {
            teamName.SetValueWithoutNotify(savedTeamName);
            draftManufacturer = savedManufacturer;
            draftSupplier = savedSupplier;
            showTeamErrors = false;
            RefreshTeamChoices();
            primaryTeamColor.SetValueWithoutNotify(savedPrimaryTeamColor);
            secondaryTeamColor.SetValueWithoutNotify(savedSecondaryTeamColor);
            refreshPrimaryTeamColor();
            refreshSecondaryTeamColor();
        }
        RefreshValidation();
    }

    public void RestorePalette()
    {
        var palette = Array.ConvertAll(savedColors, text =>
        {
            TryColor(text, out var color);
            return color;
        });
        ApplyPalette(palette);
    }

    private void PickColor(int index)
    {
        TryColor(savedColors[index], out var fallback);
        PickColor(colorFields[index], fallback);
    }

    private void PickColor(TextField field, Color fallback)
    {
        if (!TryColor(field.value, out var initial))
            initial = fallback;
        try
        {
            GameColorPicker.Open(ScreenView, initial, selected => field.value = GameColorPicker.Hex(selected));
        }
        catch (InvalidOperationException ex)
        {
            SettingsFormStyle.SetValidationMessage(status, ex.Message, true);
        }
    }

    private void SetColorFields(string[] colors)
    {
        for (int i = 0; i < colors.Length; i++)
        {
            colorFields[i].SetValueWithoutNotify(colors[i]);
            refreshColors[i]();
        }
        if (ReadPalette(out var palette))
            ApplyPalette(palette);
    }

    private bool HasUnsavedChanges()
    {
        if (draftCarLod != savedCarLod || draftTrackMsaaSamples != savedTrackMsaaSamples ||
            mode.index != savedMode || size.index != savedSize ||
            simulationModel.index != savedSimulationModel || vehicleSpeedModel.index != savedSpeedModel ||
            vehicleBehaviorModel.index != savedBehaviorModel || paceVehicle.index != savedPaceVehicle ||
            equalConditionPractice.value != EqualConditionPractice)
            return true;

        for (int i = 0; i < colorFields.Length; i++)
            if (ColorChanged(colorFields[i], savedColors[i]))
                return true;

        return HasTeamColors &&
            (teamName.value != savedTeamName ||
             draftManufacturer != savedManufacturer || draftSupplier?.name != savedSupplier?.name ||
             ColorChanged(primaryTeamColor, savedPrimaryTeamColor) || ColorChanged(secondaryTeamColor, savedSecondaryTeamColor));
    }

    private static bool ColorChanged(TextField field, string saved) =>
        !string.Equals(field.value?.Trim(), saved?.Trim(), StringComparison.OrdinalIgnoreCase);

    private bool Save()
    {
        showTeamErrors = true;
        RefreshValidation();
        if (!saveButton.enabledSelf) return false;
        if (!ReadPalette(out var palette))
        {
            SettingsFormStyle.SetValidationMessage(status, "Use a six-digit hex color for each field, such as #009BDD.", true);
            return false;
        }

        if (HasTeamColors)
        {
            if (!TryColor(primaryTeamColor.value, out var primary) || !TryColor(secondaryTeamColor.value, out var secondary))
            {
                SettingsFormStyle.SetValidationMessage(status, "Use a six-digit hex color for each team color.", true);
                return false;
            }
            string primaryHex = "#" + ColorUtility.ToHtmlStringRGB(primary);
            string secondaryHex = "#" + ColorUtility.ToHtmlStringRGB(secondary);
            string error = saveTeamColors(primaryHex, secondaryHex, teamName.value.Trim(), draftManufacturer, draftSupplier);
            if (error != null)
            {
                SettingsFormStyle.SetValidationMessage(status, error, true);
                return false;
            }
            savedTeamName = teamName.value.Trim();
            teamName.SetValueWithoutNotify(savedTeamName);
            savedManufacturer = draftManufacturer;
            savedSupplier = draftSupplier;
            savedPrimaryTeamColor = primaryHex;
            savedSecondaryTeamColor = secondaryHex;
        }

        bool lodChanged = draftCarLod != savedCarLod;
        bool msaaChanged = draftTrackMsaaSamples != savedTrackMsaaSamples;
        bool displayChanged = savedMode != mode.index || savedSize != size.index;
        savedMode = mode.index;
        savedSize = size.index;
        PlayerPrefs.SetInt(Key + "Mode", savedMode);
        PlayerPrefs.SetInt(Key + "Size", savedSize);
        savedSimulationModel = simulationModel.index;
        PlayerPrefs.SetString(Key + "SimulationModel", SimulationModel.Id);
        for (int i = 0; i < colorFields.Length; i++)
        {
            savedColors[i] = "#" + ColorUtility.ToHtmlStringRGB(palette[i]);
            PlayerPrefs.SetString(Key + "Color." + i, savedColors[i]);
        }
        EqualConditionPractice = equalConditionPractice.value;
        PlayerPrefs.SetInt(Key + "EqualConditionPractice", EqualConditionPractice ? 1 : 0);
        savedSpeedModel = vehicleSpeedModel.index;
        PlayerPrefs.SetString(Key + "VehicleSpeedModel", SpeedModel.Id);
        savedBehaviorModel = vehicleBehaviorModel.index;
        PlayerPrefs.SetString(Key + "VehicleBehaviorModel", BehaviorModel.Id);
        savedPaceVehicle = paceVehicle.index;
        PlayerPrefs.SetInt(Key + "PaceVehicle", savedPaceVehicle);
        savedCarLod = draftCarLod;
        PlayerPrefs.SetInt(Key + "CarLOD", savedCarLod);
        savedTrackMsaaSamples = draftTrackMsaaSamples;
        PlayerPrefs.SetInt(TrackMsaaKey, savedTrackMsaaSamples);
        PlayerPrefs.Save();
        ApplyPalette(palette);
        if (lodChanged) CarLodChanged?.Invoke(savedCarLod);
        if (msaaChanged) TrackMsaaSamplesChanged?.Invoke(savedTrackMsaaSamples);
        if (displayChanged)
            applyDisplay();
        RefreshRevert();
        SettingsFormStyle.SetValidationMessage(status, "Settings saved.", false);
        return true;
    }

    private bool ReadPalette(out Color[] palette)
    {
        palette = new Color[colorFields.Length];
        for (int i = 0; i < colorFields.Length; i++)
        {
            if (!TryColor(colorFields[i].value, out palette[i]))
                return false;
        }
        return true;
    }

    private static bool TryColor(string value, out Color color)
    {
        string text = value?.Trim();
        color = default;
        return text?.Length == 7 && text[0] == '#' && ColorUtility.TryParseHtmlString(text, out color);
    }

    private void ApplyPalette(Color[] colors)
    {
        root.Query<VisualElement>(className: "game-neutral").ForEach(screen =>
        {
            screen.style.backgroundColor = colors[0];
            screen.style.color = colors[3];
            screen.Query<VisualElement>(className: "garage-entry").ForEach(element => element.style.backgroundColor = colors[1]);
            screen.Query<Label>(className: "garage-detail").ForEach(element => element.style.color = colors[3]);
            screen.Query<Label>(className: "garage-heading").ForEach(element => element.style.color = colors[5]);
            screen.Query<VisualElement>(className: "footer").ForEach(element => element.style.borderTopColor = colors[4]);
            screen.Query<Button>(className: "nav-button").ForEach(button => ApplyButtonColors(button, colors));
        });
    }

    private static void ApplyButtonColors(Button button, Color[] colors)
    {
        bool selected = button.ClassListContains("nav-button-selected");
        bool thumbnailBackground = button.ClassListContains("shop-car-toggle") || button.ClassListContains("shop-car-choice");
        button.style.color = thumbnailBackground ? (Color)new Color32(238, 245, 248, 255) : colors[3];
        button.style.backgroundColor = thumbnailBackground ? (Color)CarThumbnails.BackgroundColor
            : selected ? Color.Lerp(colors[2], colors[3], 0.15f) : colors[2];
        button.style.borderBottomColor = selected ? colors[4] : new StyleColor(StyleKeyword.Null);
    }

    public IEnumerator ApplyDisplay()
    {
#if !UNITY_EDITOR
        var desktop = Screen.currentResolution;
        Vector2Int requested = Resolutions[savedSize];
        bool borderless = savedMode == 1;
        WindowFrame.Restore();
        int width = requested.x > 0 ? Mathf.Min(requested.x, desktop.width) : desktop.width;
        int height = requested.y > 0 ? Mathf.Min(requested.y, desktop.height) : desktop.height;
        Screen.SetResolution(width, height, borderless ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow);
        if (borderless)
        {
            for (int frame = 0; frame < 60; frame++)
            {
                yield return null;
                if (Screen.fullScreenMode == FullScreenMode.Windowed && WindowFrame.Remove(width, height))
                    yield break;
            }
            Debug.LogWarning("The game window opened, but its border could not be removed.");
        }
#else
        yield break;
#endif
    }

    private static class WindowFrame
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private const int StyleIndex = -16;
        private const int Frame = 0x00CF0000;
        private static IntPtr window;
        private static int originalStyle;

        public static bool Remove(int width, int height)
        {
            using (var process = System.Diagnostics.Process.GetCurrentProcess())
                window = process.MainWindowHandle;
            if (window == IntPtr.Zero)
                return false;
            originalStyle = GetWindowLong(window, StyleIndex);
            SetWindowLong(window, StyleIndex, originalStyle & ~Frame);
            return SetWindowPos(window, IntPtr.Zero, 0, 0, width, height, 0x36);
        }

        public static void Restore()
        {
            if (window == IntPtr.Zero)
                return;
            SetWindowLong(window, StyleIndex, originalStyle);
            Refresh(window);
            window = IntPtr.Zero;
        }

        private static void Refresh(IntPtr target) => SetWindowPos(target, IntPtr.Zero, 0, 0, 0, 0, 0x37);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", ExactSpelling = true)]
        private static extern int GetWindowLong(IntPtr window, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", ExactSpelling = true)]
        private static extern int SetWindowLong(IntPtr window, int index, int style);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
#else
        public static bool Remove(int width, int height) => true;
        public static void Restore() { }
#endif
    }
}
