using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UIElements;

internal sealed class GameSettings
{
    private const string Key = "StockCarManager.Settings.";
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
    private readonly VisualElement[] swatches = new VisualElement[Defaults.Length];
    private readonly Label status = new Label();
    private readonly Action applyDisplay;
    private readonly Func<string, string, string> saveTeamColors;
    private readonly VisualElement teamColors = new VisualElement();
    private readonly TextField primaryTeamColor = new TextField("Primary") { style = { flexGrow = 1 } };
    private readonly TextField secondaryTeamColor = new TextField("Secondary") { style = { flexGrow = 1 } };
    private Button primaryTeamSwatch;
    private Button secondaryTeamSwatch;
    private string[] savedColors;
    private int savedMode;
    private int savedSize;
    private int savedSimulationModel;
    private int savedSpeedModel;
    private int savedBehaviorModel;
    private int savedPaceVehicle;
    public bool UsePaceTruck => savedPaceVehicle == 1;

    public IVehicleBehaviorModel BehaviorModel => VehicleBehaviorModels.Available[savedBehaviorModel];

    public IVehicleSpeedModel SpeedModel => VehicleSpeedModels.Available[savedSpeedModel];

    public ICarSimulationModel SimulationModel => CarSimulationModels.Available[savedSimulationModel];

    public VisualElement ScreenView { get; } = new VisualElement();

    public GameSettings(VisualElement root, Action applyDisplay, Action close, Func<string, string, string> saveTeamColors)
    {
        this.root = root;
        this.applyDisplay = applyDisplay;
        this.saveTeamColors = saveTeamColors;
        savedMode = Mathf.Clamp(PlayerPrefs.GetInt(Key + "Mode"), 0, 1);
        savedSize = Mathf.Clamp(PlayerPrefs.GetInt(Key + "Size"), 0, Sizes.Length - 1);
        savedSimulationModel = CarSimulationModels.IndexForId(PlayerPrefs.GetString(Key + "SimulationModel"));
        savedSpeedModel = VehicleSpeedModels.IndexForId(PlayerPrefs.GetString(Key + "VehicleSpeedModel"));
        savedBehaviorModel = VehicleBehaviorModels.IndexForId(PlayerPrefs.GetString(Key + "VehicleBehaviorModel"));
        savedPaceVehicle = Mathf.Clamp(PlayerPrefs.GetInt(Key + "PaceVehicle"), 0, paceVehicle.choices.Count - 1);
        savedColors = new string[Defaults.Length];
        for (int i = 0; i < Defaults.Length; i++)
        {
            string saved = PlayerPrefs.GetString(Key + "Color." + i, Defaults[i]);
            savedColors[i] = TryColor(saved, out _) ? saved : Defaults[i];
        }

        ScreenView.AddToClassList("screen");
        ScreenView.AddToClassList("game-neutral");
        ScreenView.AddToClassList("tracks-editor");
        var header = new VisualElement();
        header.AddToClassList("header");
        header.Add(new Label("Settings") { style = { fontSize = 24 } });
        header.Add(MakeButton("Back", close));
        ScreenView.Add(header);

        var form = new ScrollView();
        form.AddToClassList("garage-body");
        form.Add(mode);
        form.Add(size);
        form.Add(simulationModel);
        form.Add(vehicleSpeedModel);
        form.Add(vehicleBehaviorModel);
        form.Add(paceVehicle);
        teamColors.Add(new Label("Team colors") { style = { marginTop = 14, marginBottom = 6 } });
        primaryTeamSwatch = AddTeamColorField(primaryTeamColor);
        secondaryTeamSwatch = AddTeamColorField(secondaryTeamColor);
        form.Add(teamColors);
        form.Add(new Label("Color scheme") { style = { marginTop = 14, marginBottom = 6 } });
        for (int i = 0; i < colorFields.Length; i++)
        {
            int index = i;
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            var field = new TextField(ColorLabels[i]) { style = { flexGrow = 1 } };
            var swatch = new Button(() => PickColor(index))
            {
                tooltip = $"Choose {ColorLabels[i].ToLowerInvariant()} color",
                style = { width = 32, height = 24, marginLeft = 8 }
            };
            field.RegisterValueChangedCallback(evt =>
            {
                if (TryColor(evt.newValue, out var color))
                    swatches[index].style.backgroundColor = color;
                if (ReadPalette(out var palette))
                    ApplyPalette(palette);
            });
            colorFields[i] = field;
            swatches[i] = swatch;
            row.Add(field);
            row.Add(swatch);
            form.Add(row);
        }
        form.Add(MakeButton("Reset Colors", () => SetColorFields(Defaults)));
        ScreenView.Add(form);
        status.style.whiteSpace = WhiteSpace.Normal;
        ScreenView.Add(status);
        ScreenView.Add(MakeButton("Save Settings", Save));
    }

    private Button AddTeamColorField(TextField field)
    {
        var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
        var swatch = new Button(() => PickColor(field, Color.white))
        {
            tooltip = $"Choose {field.label.ToLowerInvariant()} team color",
            style = { width = 32, height = 24, marginLeft = 8 }
        };
        field.RegisterValueChangedCallback(evt =>
        {
            if (TryColor(evt.newValue, out var color))
                swatch.style.backgroundColor = color;
        });
        row.Add(field);
        row.Add(swatch);
        teamColors.Add(row);
        return swatch;
    }

    public void Open(Career career)
    {
        mode.SetValueWithoutNotify(mode.choices[savedMode]);
        size.SetValueWithoutNotify(size.choices[savedSize]);
        simulationModel.SetValueWithoutNotify(simulationModel.choices[savedSimulationModel]);
        vehicleSpeedModel.SetValueWithoutNotify(vehicleSpeedModel.choices[savedSpeedModel]);
        vehicleBehaviorModel.SetValueWithoutNotify(vehicleBehaviorModel.choices[savedBehaviorModel]);
        paceVehicle.SetValueWithoutNotify(paceVehicle.choices[savedPaceVehicle]);
        SetColorFields(savedColors);
        teamColors.style.display = career == null ? DisplayStyle.None : DisplayStyle.Flex;
        if (career != null)
        {
            var colors = career.TeamColors;
            primaryTeamColor.SetValueWithoutNotify("#" + ColorUtility.ToHtmlStringRGB(colors.Primary));
            secondaryTeamColor.SetValueWithoutNotify("#" + ColorUtility.ToHtmlStringRGB(colors.Secondary));
            primaryTeamSwatch.style.backgroundColor = (Color)colors.Primary;
            secondaryTeamSwatch.style.backgroundColor = (Color)colors.Secondary;
        }
        status.text = "Enter colors as #RRGGBB.";
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
            if (WindowsColorPicker.TryPick(initial, out var selected))
                field.value = "#" + ColorUtility.ToHtmlStringRGB(selected);
        }
        catch (InvalidOperationException ex)
        {
            status.text = ex.Message;
        }
    }

    private void SetColorFields(string[] colors)
    {
        for (int i = 0; i < colors.Length; i++)
        {
            colorFields[i].SetValueWithoutNotify(colors[i]);
            TryColor(colors[i], out var color);
            swatches[i].style.backgroundColor = color;
        }
        if (ReadPalette(out var palette))
            ApplyPalette(palette);
    }

    private void Save()
    {
        if (!ReadPalette(out var palette))
        {
            status.text = "Use a six-digit hex color for each field, such as #009BDD.";
            return;
        }

        if (teamColors.style.display == DisplayStyle.Flex)
        {
            if (!TryColor(primaryTeamColor.value, out var primary) || !TryColor(secondaryTeamColor.value, out var secondary))
            {
                status.text = "Use a six-digit hex color for each team color.";
                return;
            }
            string error = saveTeamColors("#" + ColorUtility.ToHtmlStringRGB(primary), "#" + ColorUtility.ToHtmlStringRGB(secondary));
            if (error != null)
            {
                status.text = error;
                return;
            }
        }

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
        savedSpeedModel = vehicleSpeedModel.index;
        PlayerPrefs.SetString(Key + "VehicleSpeedModel", SpeedModel.Id);
        savedBehaviorModel = vehicleBehaviorModel.index;
        PlayerPrefs.SetString(Key + "VehicleBehaviorModel", BehaviorModel.Id);
        savedPaceVehicle = paceVehicle.index;
        PlayerPrefs.SetInt(Key + "PaceVehicle", savedPaceVehicle);
        PlayerPrefs.Save();
        ApplyPalette(palette);
        if (displayChanged)
            applyDisplay();
        status.text = "Settings saved.";
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
        ScreenView.style.backgroundColor = colors[0];
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

    private static Button MakeButton(string title, Action action)
    {
        var button = new Button(action) { text = title };
        button.AddToClassList("nav-button");
        return button;
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
