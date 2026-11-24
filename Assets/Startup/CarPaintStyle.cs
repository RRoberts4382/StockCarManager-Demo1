using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

internal enum CarPaintScheme
{
    Classic, SpeedStripe, Sweep, Apex, SideFlash,
    Beltline, Diagonal, ReverseDiagonal, Chevron, Rebound,
    Wave, Ribbon, Taper, Triad, Ladder,
    Velocity, Gridlock, Switchback, FrontClip, RearClip,
    Horizon, SplitRail, TailFin, Overdrive, Afterburn
}

internal static class CarNumberFonts
{
    public static readonly string[] Names = { "Google Sans", "Racing Sans One", "Story Script", "Open Sans" };
    private static readonly string[] Paths =
        { "Fonts/GoogleSans/GoogleSans-Regular", "Fonts/RacingSansOne-Regular", "Fonts/StoryScript-Regular", "Fonts/OpenSans/OpenSans-Regular" };
    public static string Canonical(string name) => System.Array.IndexOf(Names, name) >= 0 ? name : Names[0];
    public static Font Load(string name, bool bold = false, bool italic = false)
    {
        if (Canonical(name) == "Open Sans")
        {
            string face = bold ? (italic ? "BoldItalic" : "Bold") : (italic ? "Italic" : "Regular");
            return Resources.Load<Font>("Fonts/OpenSans/OpenSans-" + face);
        }
        return Resources.Load<Font>(Paths[System.Array.IndexOf(Names, Canonical(name))]);
    }
    public static void Apply(Label label, CarPaintStyle? paint)
    {
        var font = Load(paint?.NumberFont, paint?.NumberBold ?? true, paint?.NumberItalic ?? false);
        if (font != null)
            label.style.unityFontDefinition = FontDefinition.FromFont(font);
        label.style.unityFontStyleAndWeight = Style(paint);
    }

    public static void Apply(TextMesh label, CarPaintStyle? paint)
    {
        label.font = Load(paint?.NumberFont, paint?.NumberBold ?? true, paint?.NumberItalic ?? false) ?? label.font;
        label.fontStyle = Style(paint);
    }

    private static FontStyle Style(CarPaintStyle? paint)
    {
        if (paint?.NumberFont == "Open Sans")
            return FontStyle.Normal;
        bool bold = paint?.NumberBold ?? true;
        bool italic = paint?.NumberItalic ?? false;
        return bold ? (italic ? FontStyle.BoldAndItalic : FontStyle.Bold)
            : (italic ? FontStyle.Italic : FontStyle.Normal);
    }

    public static Color ContrastColor(Color background)
    {
        Color linear = background.linear;
        float luminance = .2126f * linear.r + .7152f * linear.g + .0722f * linear.b;
        // Equal contrast against black and white occurs at this relative luminance.
        return luminance >= .1791288f ? Color.black : Color.white;
    }

}

internal readonly struct CarPaintStyle
{
    internal static IReadOnlyList<CarPaintScheme> Schemes { get; } =
        Array.AsReadOnly((CarPaintScheme[])Enum.GetValues(typeof(CarPaintScheme)));

    public readonly CarPaintScheme Scheme;
    public readonly Color32 Primary;
    public readonly Color32 Secondary;
    public readonly Color32 Trim;
    public readonly bool HasTrim;
    public readonly Color32? NumberColor;
    public readonly string NumberFont;
    public readonly bool NumberBold;
    public readonly bool NumberItalic;
    public readonly bool HideRenderedNumber;
    public readonly CarPaintTemplateAssignment Template;

    public CarPaintStyle(CarPaintScheme scheme, Color32 primary, Color32 secondary, Color32 trim, bool hasTrim, Color32? numberColor = null, string numberFont = null, bool numberBold = true, bool numberItalic = false, bool hideRenderedNumber = false, CarPaintTemplateAssignment template = default)
    {
        Scheme = scheme;
        Primary = primary;
        Secondary = secondary;
        Trim = trim;
        HasTrim = hasTrim;
        NumberColor = numberColor;
        NumberFont = CarNumberFonts.Canonical(numberFont);
        NumberBold = numberBold;
        NumberItalic = numberItalic;
        HideRenderedNumber = hideRenderedNumber;
        Template = template;
    }

    public CarPaintStyle WithRenderedNumberHidden(bool hidden) =>
        new CarPaintStyle(Scheme, Primary, Secondary, Trim, HasTrim, NumberColor, NumberFont, NumberBold, NumberItalic, hidden, Template);

    public CarPaintStyle WithTemplate(CarPaintTemplateAssignment template) =>
        new CarPaintStyle(Scheme, Primary, Secondary, Trim, HasTrim, NumberColor, NumberFont, NumberBold, NumberItalic, HideRenderedNumber, template);

    public void ApplyTo(TeamCar car)
    {
        car.paintScheme = Scheme;
        car.primaryPaint = GameColorPicker.Hex(Primary);
        car.secondaryPaint = GameColorPicker.Hex(Secondary);
        car.trimPaint = HasTrim ? GameColorPicker.Hex(Trim) : null;
        car.numberPaint = NumberColor is Color32 numberColor ? GameColorPicker.Hex(numberColor) : null;
        car.numberFont = NumberFont;
        car.numberBold = NumberBold;
        car.numberItalic = NumberItalic;
        car.hideRenderedNumber = HideRenderedNumber;
        car.paintTemplate = Template;
    }

    public bool HasWrap => Scheme != CarPaintScheme.Classic || HasTrim;

    public static string SchemeName(CarPaintScheme scheme) => scheme switch
    {
        CarPaintScheme.SpeedStripe => "Speed Stripe",
        CarPaintScheme.SideFlash => "Side Flash",
        CarPaintScheme.ReverseDiagonal => "Reverse Diagonal",
        CarPaintScheme.FrontClip => "Front Clip",
        CarPaintScheme.RearClip => "Rear Clip",
        CarPaintScheme.SplitRail => "Split Rail",
        CarPaintScheme.TailFin => "Tail Fin",
        _ => scheme.ToString()
    };

    public Color32 ColorAt(Vector3 carLocal, bool hoodTrunk) => ColorAt(Project(carLocal), hoodTrunk);

    public static Vector2 Project(Vector3 point)
    {
        float length = Mathf.Clamp01((1.45f - point.z) / 2.9f);
        float height = Mathf.Clamp01((point.y - 0.12f) / 0.94f);
        float center = 1f - Mathf.Clamp01(Mathf.Abs(point.x) / 0.82f);
        return new Vector2(length, height * 0.74f + center * 0.26f);
    }

    public Texture2D BuildTexture()
    {
        const int width = 512;
        const int height = 256;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, true)
        {
            name = $"{SchemeName(Scheme)} paint",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Trilinear
        };
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                pixels[y * width + x] = ColorAt(new Vector2((x + 0.5f) / width, (y + 0.5f) / height), false);
        texture.SetPixels32(pixels);
        texture.Apply(true, true);
        return texture;
    }

    private Color32 ColorAt(Vector2 uv, bool hoodTrunk)
    {
        float u = uv.x;
        float v = uv.y;
        switch (Scheme)
        {
            case CarPaintScheme.SpeedStripe:
                // A bright pinstripe separates a wide belt from the body color.
                float belt = v - (0.23f + 0.14f * u);
                if (belt >= -0.05f && belt < 0.06f)
                    return Secondary;
                return belt >= 0.06f && belt < 0.084f ? Trim : Primary;
            case CarPaintScheme.Sweep:
                float sweep = v - (0.22f + 0.63f * u * u);
                return sweep < 0f ? Secondary : sweep < 0.032f ? Trim : Primary;
            case CarPaintScheme.Apex:
                float apex = v - (0.17f + 0.72f * Mathf.Abs(u - 0.58f));
                if (apex >= 0f && apex < 0.029f)
                    return Trim;
                if (u > 0.63f && v > 0.24f && v < 0.58f && Mathf.Repeat(u * 8.5f - v * 1.9f, 1f) < 0.19f)
                    return Trim;
                return apex < 0f ? Secondary : Primary;
            case CarPaintScheme.SideFlash:
                float flash = v - (0.12f + 0.62f * u);
                if (flash > -0.03f && flash < 0.025f)
                    return Trim;
                return flash <= -0.03f || flash is > 0.12f and < 0.2f ? Secondary : Primary;
            case CarPaintScheme.Beltline:
                return Split(v - 0.31f, 0.026f);
            case CarPaintScheme.Diagonal:
                return Split(v - (0.12f + 0.64f * u), 0.047f);
            case CarPaintScheme.ReverseDiagonal:
                return Split(v - (0.72f - 0.59f * u), 0.041f);
            case CarPaintScheme.Chevron:
                return Split(v - (0.66f - 0.85f * Mathf.Abs(u - 0.5f)), 0.032f);
            case CarPaintScheme.Rebound:
                return Split(v - (0.27f + 0.57f * Mathf.Abs(u - 0.31f)), 0.046f);
            case CarPaintScheme.Wave:
                return Split(v - (0.43f + 0.16f * Mathf.Sin(u * 2f * Mathf.PI)), 0.04f);
            case CarPaintScheme.Ribbon:
                return Banded(v - (0.33f + 0.13f * Mathf.Sin(u * Mathf.PI)), 0.085f, 0.025f);
            case CarPaintScheme.Taper:
                return Banded(v - (0.22f + 0.27f * u), 0.025f + 0.13f * u, 0.027f);
            case CarPaintScheme.Triad:
                return Split(u + 0.29f * v - 0.63f, 0.12f);
            case CarPaintScheme.Ladder:
                return v < 0.25f ? Secondary
                    : v < 0.56f && Mathf.Repeat(u * 9f - v * 1.3f, 1f) < 0.18f ? Trim : Primary;
            case CarPaintScheme.Velocity:
                return v < 0.22f ? Secondary
                    : v < 0.59f && u > 0.5f && Mathf.Repeat(u * 12f - v * 4f, 1f) < 0.22f ? Trim : Primary;
            case CarPaintScheme.Gridlock:
                if (u > 0.63f && v > 0.22f && v < 0.56f &&
                    ((Mathf.FloorToInt((u - v * 0.22f) * 19f) + Mathf.FloorToInt(v * 17f)) & 1) == 0)
                    return Trim;
                return Split(v - 0.39f, 0.024f);
            case CarPaintScheme.Switchback:
                return Split(v - (0.22f + 0.45f * Mathf.PingPong(u * 2.2f, 1f)), 0.03f);
            case CarPaintScheme.FrontClip:
                return Split(u - (0.29f + 0.27f * v), 0.044f);
            case CarPaintScheme.RearClip:
                return Split((0.74f - 0.24f * v) - u, 0.05f);
            case CarPaintScheme.Horizon:
                return v < 0.18f ? Secondary : v < 0.215f || v is > 0.51f and < 0.545f ? Trim : Primary;
            case CarPaintScheme.SplitRail:
                return Banded(Mathf.Abs(v - (0.36f + 0.13f * u)) - 0.12f, 0.036f, 0.025f);
            case CarPaintScheme.TailFin:
                return Banded(v - (0.22f + 0.62f * u * u * u), 0.095f, 0.032f);
            case CarPaintScheme.Overdrive:
                return u < 0.14f || u > 0.85f ? Split(v - 0.37f, 0.04f)
                    : Banded(v - (0.29f + 0.16f * u), 0.057f, 0.036f);
            case CarPaintScheme.Afterburn:
                return Split(v - (0.19f + 0.62f * u + 0.065f * Mathf.Sin(u * 8f * Mathf.PI)), 0.034f);
            default:
                if (hoodTrunk)
                    return Primary;
                return HasTrim && v is >= 0.12f and <= 0.17f ? Trim : Secondary;
        }
    }

    private Color32 Split(float offset, float trimWidth) =>
        offset < 0f ? Secondary : offset < trimWidth ? Trim : Primary;

    private Color32 Banded(float offset, float halfWidth, float trimWidth)
    {
        float distance = Mathf.Abs(offset);
        return distance < halfWidth ? Secondary : distance < halfWidth + trimWidth ? Trim : Primary;
    }

    public static bool Apply(Transform car, Material primary, Material secondary, CarPaintStyle paint,
        Action<Texture2D> retainTexture, Texture2D template = null)
    {
        if (template == null && (!paint.HasWrap || retainTexture == null))
            return false;

        bool classic = template == null && paint.Scheme == CarPaintScheme.Classic;
        var paintedMeshes = new List<MeshFilter>();
        foreach (var filter in car.GetComponentsInChildren<MeshFilter>())
        {
            if (!filter.TryGetComponent<MeshRenderer>(out var renderer) ||
                !Array.Exists(renderer.sharedMaterials, material => material == secondary || (!classic && material == primary)))
                continue;
            if (filter.sharedMesh == null || !filter.sharedMesh.isReadable)
                return false;
            paintedMeshes.Add(filter);
        }
        if (paintedMeshes.Count == 0)
            return false;

        foreach (var filter in paintedMeshes)
            ProjectMesh(car, filter);

        var texture = template ?? paint.BuildTexture();
        if (template == null)
            retainTexture(texture);
        SetPaintTexture(secondary);
        if (!classic)
            SetPaintTexture(primary);
        return true;

        void SetPaintTexture(Material material)
        {
            material.color = Color.white;
            material.SetTexture("_BaseMap", texture);
        }
    }

    private static readonly Dictionary<(Mesh Mesh, Matrix4x4 ToCar), Mesh> ProjectedMeshes = new();

    private static void ProjectMesh(Transform car, MeshFilter filter)
    {
        var toCar = Matrix4x4.identity;
        for (var node = filter.transform; node != car; node = node.parent)
            toCar = Matrix4x4.TRS(node.localPosition, node.localRotation, node.localScale) * toCar;
        var source = filter.sharedMesh;
        var key = (source, toCar);
        if (!ProjectedMeshes.TryGetValue(key, out var mesh) || mesh == null)
        {
            var positions = source.vertices;
            var coordinates = new Vector2[positions.Length];
            for (int i = 0; i < positions.Length; i++)
                coordinates[i] = Project(toCar.MultiplyPoint3x4(positions[i]));
            mesh = Object.Instantiate(source);
            mesh.uv = coordinates;
            mesh.UploadMeshData(true);
            ProjectedMeshes[key] = mesh;
        }
        filter.sharedMesh = mesh;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetProjectedMeshes()
    {
        foreach (var mesh in ProjectedMeshes.Values)
            if (mesh != null)
                Object.Destroy(mesh);
        ProjectedMeshes.Clear();
    }
}

internal static class CarPaintEditor
{
    private const string PaintTemplateFolderKey = "CarPaint.LastTemplateFolder";

    public static void Open(VisualElement parent, TeamCar car, string[] sponsors,
        System.Func<TeamCar, string> savePaint, System.Action saved = null, bool careerDraft = false)
    {
        var paintPreviewThumbnails = CarThumbnails.CreateLargePreview();
        var original = car.PaintStyle;
        var overlay = new VisualElement();
        SettingsFormStyle.Apply(overlay);
        var paintStyles = Resources.Load<StyleSheet>("Startup/Startup");
        if (paintStyles != null) overlay.styleSheets.Add(paintStyles);
        overlay.AddToClassList("settings-confirmation-overlay");
        var dialog = MakeDialog(careerDraft ? "Car Paint Scheme" : $"Paint Scheme · Chassis # {car.chassisNumber}");
        overlay.Add(dialog);
        dialog.style.width = 700;
        var content = new VisualElement();
        content.AddToClassList("settings-row");
        var preview = paintPreviewThumbnails.CreateOrbitPreview();
        preview.style.width = 365;
        preview.style.height = 260;
        preview.style.flexShrink = 0;
        preview.style.alignSelf = Align.Center;
        content.Add(preview);
        var controls = new VisualElement();
        controls.style.flexGrow = 1;
        controls.style.minWidth = 220;
        content.Add(controls);
        dialog.Add(content);
        var error = SettingsFormStyle.Label("", "settings-validation-summary");
        var draft = car.PaintStyle;
        Action refreshAvailability = null;
        var (schemeButton, updateAvailability) = BuildControls(controls, overlay, dialog, () => car, sponsors, draft, style =>
        {
            draft = style.WithTemplate(draft.Template);
            UpdatePreview();
        }, error, () => draft.Template.IsAssigned, showRenderNumberSwitch: true);
        refreshAvailability = updateAvailability;
        var templatePicker = new TemplatePicker(error, selection =>
        {
            draft = draft.WithTemplate(selection);
            UpdatePreview();
        });
        templatePicker.SetSelection(draft.Template);
        controls.Add(templatePicker);
        dialog.Add(error);
        var footer = new VisualElement();
        footer.AddToClassList("settings-footer");
        footer.Add(SettingsFormStyle.Button("Cancel", RequestClose));
        footer.Add(SettingsFormStyle.Button(careerDraft ? "Apply Paint Scheme" : "Save Paint Scheme", Save, primary: true));
        dialog.Add(footer);
        parent.Add(overlay);
        overlay.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Escape || !dialog.enabledSelf) return;
            RequestClose();
            evt.StopPropagation();
        });
        schemeButton.Focus();
        UpdatePreview();

        void UpdatePreview()
        {
            refreshAvailability?.Invoke();
            preview.image = paintPreviewThumbnails.Get(car, sponsors, draft);
            SettingsFormStyle.SetValidationMessage(error, null, false);
        }
        void Close()
        {
            overlay.RemoveFromHierarchy();
            paintPreviewThumbnails.Dispose();
        }
        void RequestClose()
        {
            bool changed = !draft.Equals(original);
            if (!changed) { Close(); return; }
            var warning = new VisualElement();
            warning.AddToClassList("settings-confirmation-overlay");
            var box = MakeDialog("Unsaved changes");
            box.Add(SettingsFormStyle.Label("Save or discard changes to this car's paint scheme.", "settings-note"));
            var actions = new VisualElement();
            actions.AddToClassList("settings-footer");
            actions.Add(SettingsFormStyle.Button("Keep Editing", () => { warning.RemoveFromHierarchy(); dialog.SetEnabled(true); }));
            actions.Add(SettingsFormStyle.Button("Discard and Go Back", Close));
            actions.Add(SettingsFormStyle.Button("Save and Go Back", () =>
            {
                warning.RemoveFromHierarchy(); dialog.SetEnabled(true); Save();
            }, primary: true));
            box.Add(actions);
            warning.Add(box);
            dialog.SetEnabled(false);
            overlay.Add(warning);
            warning.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape) return;
                warning.RemoveFromHierarchy(); dialog.SetEnabled(true); evt.StopPropagation();
            });
            actions.Q<Button>()?.Focus();
        }
        void Save()
        {
            var previous = (car.primaryPaint, car.secondaryPaint, car.trimPaint, car.paintScheme, car.numberPaint, car.numberFont, car.numberBold, car.numberItalic, car.hideRenderedNumber, car.paintTemplate);
            draft.ApplyTo(car);
            string saveError = savePaint(car);
            if (saveError != null)
            {
                (car.primaryPaint, car.secondaryPaint, car.trimPaint, car.paintScheme, car.numberPaint, car.numberFont, car.numberBold, car.numberItalic, car.hideRenderedNumber, car.paintTemplate) = previous;
                SettingsFormStyle.SetValidationMessage(error, saveError, true);
                return;
            }
            Close(); saved?.Invoke();
        }
    }

    internal static (Button SchemeButton, Action RefreshAvailability) BuildControls(VisualElement controls, VisualElement popupHost, VisualElement underlay,
        Func<TeamCar> currentCar, string[] sponsors, CarPaintStyle original, Action<CarPaintStyle> changed, Label error,
        Func<bool> templateSelected, Func<bool> numberHidden = null, bool showRenderNumberSwitch = false, bool compactLayout = false)
    {
        var schemes = CarPaintStyle.Schemes;
        int schemeIndex = 0;
        for (int i = 0; i < schemes.Count; i++)
            if (schemes[i] == original.Scheme) { schemeIndex = i; break; }
        var colors = new[] { original.Primary, original.Secondary, original.Trim, original.NumberColor ?? new Color32(255, 255, 255, 255) };
        bool hasNumberColor = original.NumberColor.HasValue;
        string numberFont = original.NumberFont;
        bool numberBold = original.NumberBold;
        bool numberItalic = original.NumberItalic;
        bool hideRenderedNumber = original.HideRenderedNumber;
        bool hasTrim = original.HasTrim;
        var schemeField = new VisualElement();
        schemeField.AddToClassList("settings-field");
        schemeField.Add(SettingsFormStyle.Label("Paint scheme", "settings-note"));
        var schemeButton = SettingsFormStyle.Button(CarPaintStyle.SchemeName(schemes[schemeIndex]), null);
        schemeButton.name = "paint-scheme-selection";
        schemeButton.tooltip = "Preview and choose a paint scheme on this car.";
        schemeField.Add(schemeButton);
        controls.Add(schemeField);
        var primary = AddColor("Primary color", 0);
        var secondary = AddColor("Secondary color", 1);
        var numberControls = new VisualElement();
        numberControls.AddToClassList("settings-row");
        if (compactLayout)
        {
            numberControls.AddToClassList("career-paint-color-actions");
            numberControls.style.flexWrap = Wrap.NoWrap;
        }
        var trim = AddColor("Trim color", 2, compactLayout ? numberControls : controls);
        var numberColor = AddColor("Car number color", 3, showRenderNumberSwitch || compactLayout ? numberControls : controls);
        if (showRenderNumberSwitch)
        {
            var renderNumber = CreateRenderNumberSwitch(!hideRenderedNumber);
            renderNumber.RegisterValueChangedCallback(evt =>
            {
                hideRenderedNumber = !evt.newValue;
                UpdatePreview();
            });
            numberControls.Add(renderNumber);
        }
        if (showRenderNumberSwitch || compactLayout)
            controls.Add(numberControls);
        var fontField = new VisualElement();
        fontField.AddToClassList("settings-field");
        fontField.Add(SettingsFormStyle.Label("Car number font", "settings-note"));
        var fontButton = SettingsFormStyle.Button(numberFont, null);
        fontButton.tooltip = "Choose a bundled, licensed font for the car numbers.";
        if (!compactLayout)
            fontField.Add(fontButton);
        controls.Add(fontField);
        var styles = new VisualElement();
        styles.AddToClassList("settings-row");
        var bold = SettingsFormStyle.Button("Bold", null);
        var italic = SettingsFormStyle.Button("Italic", null);
        bold.tooltip = "Toggle bold lettering for the car numbers.";
        italic.tooltip = "Toggle italic lettering for the car numbers.";
        bold.style.unityFontStyleAndWeight = FontStyle.Bold;
        italic.style.unityFontStyleAndWeight = FontStyle.Italic;
        bold.EnableInClassList("settings-tab-current", numberBold);
        italic.EnableInClassList("settings-tab-current", numberItalic);
        bold.clicked += () =>
        {
            numberBold = !numberBold;
            bold.EnableInClassList("settings-tab-current", numberBold);
            UpdatePreview();
        };
        italic.clicked += () =>
        {
            numberItalic = !numberItalic;
            italic.EnableInClassList("settings-tab-current", numberItalic);
            UpdatePreview();
        };
        if (compactLayout)
        {
            styles.AddToClassList("career-paint-font-actions");
            styles.style.flexWrap = Wrap.NoWrap;
            fontButton.style.flexGrow = 1;
            fontButton.style.flexShrink = 1;
            fontButton.style.minWidth = 0;
            styles.Add(fontButton);
        }
        styles.Add(bold);
        styles.Add(italic);
        if (compactLayout)
            fontField.Add(styles);
        else
            controls.Add(styles);
        fontButton.clicked += ChooseNumberFont;
        schemeButton.clicked += ChooseScheme;
        RefreshAvailability();
        return (schemeButton, RefreshAvailability);

        void RefreshAvailability()
        {
            bool useSchemeColors = !templateSelected();
            schemeButton.SetEnabled(useSchemeColors);
            primary.SetEnabled(useSchemeColors);
            secondary.SetEnabled(useSchemeColors);
            trim.SetEnabled(useSchemeColors);

            bool renderNumber = !RenderedNumberHidden();
            numberColor.SetEnabled(renderNumber);
            fontButton.SetEnabled(renderNumber);
            bold.SetEnabled(renderNumber);
            italic.SetEnabled(renderNumber);
        }

        void UpdatePreview()
        {
            SettingsFormStyle.SetValidationMessage(error, null, false);
            changed(new CarPaintStyle(schemes[schemeIndex], colors[0], colors[1], colors[2], hasTrim,
                hasNumberColor ? colors[3] : (Color32?)null, numberFont, numberBold, numberItalic, RenderedNumberHidden()));
        }

        bool RenderedNumberHidden() => numberHidden?.Invoke() ?? hideRenderedNumber;

        Button AddColor(string label, int index, VisualElement container = null)
        {
            var button = SettingsFormStyle.Button(label, null);
            button.AddToClassList("car-paint-color-button");
            if (index == 2) button.name = "paint-trim-color";
            if (index == 3) button.name = "paint-number-color";
            button.tooltip = "Choose the car's " + label.ToLowerInvariant() + ".";
            var swatch = new VisualElement { pickingMode = PickingMode.Ignore };
            swatch.AddToClassList("car-paint-swatch");
            swatch.style.backgroundColor = (Color)colors[index];
            button.Add(swatch);
            button.clicked += () =>
            {
                try
                {
                    GameColorPicker.Open(popupHost, colors[index], chosen =>
                    {
                        colors[index] = chosen;
                        hasTrim |= index == 2;
                        hasNumberColor |= index == 3;
                        swatch.style.backgroundColor = (Color)chosen;
                        UpdatePreview();
                    });
                }
                catch (System.InvalidOperationException ex)
                {
                    SettingsFormStyle.SetValidationMessage(error, ex.Message, true);
                }
            };
            (container ?? controls).Add(button);
            return button;
        }
        void ChooseNumberFont()
        {
            var picker = new VisualElement();
            picker.AddToClassList("settings-confirmation-overlay");
            var box = MakeDialog("Choose car number font");
            foreach (string name in CarNumberFonts.Names)
            {
                var button = SettingsFormStyle.Button(name, () =>
                {
                    numberFont = name;
                    fontButton.text = name;
                    UpdatePreview();
                    ClosePicker();
                });
                button.style.unityFontDefinition = FontDefinition.FromFont(CarNumberFonts.Load(name, numberBold, numberItalic));
                button.style.unityFontStyleAndWeight = name == "Open Sans" ? FontStyle.Normal : numberBold ?
                    (numberItalic ? FontStyle.BoldAndItalic : FontStyle.Bold) :
                    (numberItalic ? FontStyle.Italic : FontStyle.Normal);
                button.tooltip = "Use " + name + " for car numbers.";
                box.Add(button);
            }
            box.Add(SettingsFormStyle.Button("Cancel", ClosePicker));
            picker.Add(box);
            underlay.SetEnabled(false);
            popupHost.Add(picker);
            box.Q<Button>()?.Focus();
            picker.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape) return;
                ClosePicker(); evt.StopPropagation();
            });
            void ClosePicker()
            {
                picker.RemoveFromHierarchy(); underlay.SetEnabled(true); fontButton.Focus();
            }
        }
        void ChooseScheme()
        {
            var car = currentCar();
            if (car == null) return;
            var thumbnails = new CarThumbnails();
            var picker = new VisualElement();
            picker.AddToClassList("settings-confirmation-overlay");
            var box = MakeDialog("Choose paint scheme");
            box.style.width = 700;
            box.style.maxHeight = new Length(90, LengthUnit.Percent);
            var list = new ScrollView(ScrollViewMode.Vertical);
            list.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            list.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            list.style.height = 350;
            list.style.flexShrink = 1;
            list.contentContainer.style.flexDirection = FlexDirection.Row;
            list.contentContainer.style.flexWrap = Wrap.Wrap;
            box.Add(list);
            var actions = new VisualElement();
            actions.AddToClassList("settings-footer");
            actions.Add(SettingsFormStyle.Button("Cancel", ClosePicker));
            box.Add(actions);
            picker.Add(box);
            underlay.SetEnabled(false);
            popupHost.Add(picker);
            // Static picker previews own their textures; they must not replace the orbit preview.
            picker.RegisterCallback<DetachFromPanelEvent>(_ => thumbnails.Dispose());
            picker.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape) return;
                ClosePicker(); evt.StopPropagation();
            });
            Button selectedRow = null;
            for (int i = 0; i < schemes.Count; i++)
            {
                int selected = i;
                var row = SettingsFormStyle.Button("", () =>
                {
                    schemeIndex = selected;
                    schemeButton.text = CarPaintStyle.SchemeName(schemes[selected]);
                    UpdatePreview(); ClosePicker();
                });
                row.AddToClassList("team-paint-choice");
                if (i == schemeIndex)
                {
                    row.AddToClassList("team-paint-choice-selected");
                    selectedRow = row;
                }
                row.tooltip = "Use " + CarPaintStyle.SchemeName(schemes[i]) + ".";
                var image = new Image
                {
                    image = thumbnails.Get(car, sponsors,
                        new CarPaintStyle(schemes[i], colors[0], colors[1], colors[2], hasTrim, hasNumberColor ? colors[3] : (Color32?)null, numberFont, numberBold, numberItalic, RenderedNumberHidden()),
                        cacheScope: "paint-scheme-" + schemes[i], transparentBackground: true, retainOrbitScene: false),
                    scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore
                };
                image.style.height = 72;
                image.style.alignSelf = Align.Stretch;
                row.Add(image);
                row.Add(SettingsFormStyle.Label(CarPaintStyle.SchemeName(schemes[i]), "settings-note"));
                list.Add(row);
            }
            if (selectedRow != null)
            {
                selectedRow.Focus();
                list.RegisterCallback<GeometryChangedEvent>(RevealSelection);
                void RevealSelection(GeometryChangedEvent evt)
                {
                    list.UnregisterCallback<GeometryChangedEvent>(RevealSelection);
                    list.schedule.Execute(() => list.ScrollTo(selectedRow));
                }
            }
            void ClosePicker()
            {
                picker.RemoveFromHierarchy(); underlay.SetEnabled(true); schemeButton.Focus();
            }
        }
    }

    internal sealed class TemplatePicker : VisualElement
    {
        private const string EmptyLabel = "No paint template selected";
        private readonly Label fileName;
        private readonly Button clear;
        private readonly Label error;
        private readonly Action<CarPaintTemplateAssignment> changed;
        public CarPaintTemplateAssignment Selected { get; private set; }

        public TemplatePicker(Label error, Action<CarPaintTemplateAssignment> changed)
        {
            this.error = error;
            this.changed = changed;
            AddToClassList("paint-template-picker");
            style.alignItems = Align.FlexStart;
            style.marginTop = 4;
            style.flexShrink = 0;
            var actions = new VisualElement();
            actions.AddToClassList("settings-row");
            var browse = SettingsFormStyle.Button("Browse for Paint Template", Browse);
            browse.name = "browse-paint-template";
            browse.tooltip = "Choose a paint template file for this car.";
            actions.Add(browse);

            clear = SettingsFormStyle.Button("", ClearSelection);
            clear.name = "clear-paint-template";
            clear.tooltip = "Remove the custom paint assignment from this car.";
            clear.style.minWidth = 35;
            clear.style.paddingLeft = 5;
            clear.style.paddingRight = 5;
            clear.style.justifyContent = Justify.Center;
            clear.style.alignItems = Align.Center;
            StartupMenu.DrawTrashButtonIcon(clear);
            clear.SetEnabled(false);
            actions.Add(clear);
            Add(actions);

            fileName = SettingsFormStyle.Label(EmptyLabel, "settings-note");
            fileName.name = "paint-template-file-name";
            fileName.style.maxWidth = Length.Percent(100);
            fileName.style.overflow = Overflow.Hidden;
            fileName.style.whiteSpace = WhiteSpace.NoWrap;
            fileName.style.textOverflow = TextOverflow.Ellipsis;
            fileName.style.flexShrink = 0;
            Add(fileName);
        }

        public void SetSelection(CarPaintTemplateAssignment selection)
        {
            Selected = selection;
            fileName.text = selection.IsAssigned ? Path.GetFileName(selection.fileName) : EmptyLabel;
            fileName.tooltip = selection.IsAssigned ? fileName.text : null;
            clear.SetEnabled(selection.IsAssigned);
        }

        private void ClearSelection()
        {
            SetSelection(default);
            SettingsFormStyle.SetValidationMessage(error, null, false);
            changed(Selected);
        }

        private void Browse()
        {
            string path;
            CarPaintTemplateAssignment replacement;
            try
            {
                string folder = PlayerPrefs.GetString(PaintTemplateFolderKey, "");
                path = TrackImageFilePicker.Pick("Choose a paint template",
                    Directory.Exists(folder) ? folder : "",
                    "Paint template images", "tga,png,jpg,jpeg", "All files", "*");
                if (string.IsNullOrEmpty(path))
                    return;
                replacement = CarPaintTemplate.Import(path);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                SettingsFormStyle.SetValidationMessage(error, ex.Message, true);
                return;
            }

            SetSelection(replacement);
            string selectedFolder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(selectedFolder))
            {
                PlayerPrefs.SetString(PaintTemplateFolderKey, selectedFolder);
                PlayerPrefs.Save();
            }
            SettingsFormStyle.SetValidationMessage(error, null, false);
            changed(Selected);
        }
    }

    internal static Toggle CreateRenderNumberSwitch(bool value)
    {
        var toggle = PracticeRunChart.CreatePillSwitch("Render number", value);
        toggle.name = "render-car-number";
        toggle.style.flexShrink = 0;
        toggle.tooltip = "Show the game-rendered number on the car. Turn off if the paint already includes a number.";
        return toggle;
    }

    private static VisualElement MakeDialog(string title)
    {
        var box = new VisualElement();
        box.AddToClassList("settings-confirmation-dialog");
        box.style.backgroundColor = (Color)new Color32(16, 21, 30, 255);
        var trim = new StyleColor(new Color32(0, 155, 221, 255));
        box.style.borderTopColor = box.style.borderBottomColor =
            box.style.borderLeftColor = box.style.borderRightColor = trim;
        box.Add(SettingsFormStyle.Label(title, "settings-group-heading"));
        return box;
    }
}
