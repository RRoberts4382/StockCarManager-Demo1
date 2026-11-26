using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

internal static class GameColorPicker
{
    private const string PalettePreference = "StockCarManager.ColorPicker.CustomColors.v1";
    private static readonly string[] BasicColors = {
        "#FF8080", "#FFFF80", "#80FF80", "#00FF80", "#80FFFF", "#0080FF", "#FF80C0", "#FF80FF",
        "#FF0000", "#FFFF00", "#80FF00", "#00FF40", "#00FFFF", "#0080C0", "#8080C0", "#FF00FF",
        "#804040", "#FF8040", "#00FF00", "#008080", "#004080", "#8080FF", "#800040", "#FF0080",
        "#800000", "#FF8000", "#008000", "#008040", "#0000FF", "#0000A0", "#800080", "#8000FF",
        "#400000", "#804000", "#004000", "#004040", "#000080", "#000040", "#400040", "#400080",
        "#000000", "#808000", "#808040", "#808080", "#408080", "#C0C0C0", "#400040", "#FFFFFF"
    };

    public static void Open(VisualElement host, Color32 initial, Action<Color32> chosen) => new Dialog(host, initial, chosen);

    internal static string Hex(Color32 color) => "#" + color.r.ToString("X2") + color.g.ToString("X2") + color.b.ToString("X2");
    internal static bool TryHex(string text, out Color32 color)
    {
        color = new Color32(0, 0, 0, 255);
        text = text?.Trim();
        if (text?.StartsWith("#", StringComparison.Ordinal) == true) text = text.Substring(1);
        if (text?.Length != 6 || !uint.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint rgb)) return false;
        color = new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255); return true;
    }
    internal static string[] DecodePalette(string saved)
    {
        var entries = (saved ?? "").Split(',');
        return Enumerable.Range(0, 16).Select(i => i < entries.Length && TryHex(entries[i], out var color) ? Hex(color) : "#FFFFFF").ToArray();
    }

    // Windows-style HSL editing, independent of the platform's native dialog.
    internal static void ToHsl(Color32 color, out float hue, out float saturation, out float lightness)
    {
        float r = color.r / 255f, g = color.g / 255f, b = color.b / 255f;
        float high = Math.Max(r, Math.Max(g, b)), low = Math.Min(r, Math.Min(g, b));
        float delta = high - low;
        lightness = (high + low) * 0.5f; hue = saturation = 0;
        if (delta < 0.000001f) return;
        saturation = delta / (1f - Math.Abs(2f * lightness - 1f));
        hue = high == r ? ((g - b) / delta + (g < b ? 6f : 0f)) / 6f : high == g ? ((b - r) / delta + 2f) / 6f : ((r - g) / delta + 4f) / 6f;
    }
    internal static Color32 FromHsl(float hue, float saturation, float lightness)
    {
        hue -= (float)Math.Floor(hue); saturation = Mathf.Clamp01(saturation); lightness = Mathf.Clamp01(lightness);
        float chroma = (1f - Math.Abs(2f * lightness - 1f)) * saturation;
        float x = chroma * (1f - Math.Abs(hue * 6f % 2f - 1f)), m = lightness - chroma * 0.5f;
        float r, g, b;
        switch ((int)(hue * 6f))
        {
            case 0: r = chroma; g = x; b = 0; break;
            case 1: r = x; g = chroma; b = 0; break;
            case 2: r = 0; g = chroma; b = x; break;
            case 3: r = 0; g = x; b = chroma; break;
            case 4: r = x; g = 0; b = chroma; break;
            default: r = chroma; g = 0; b = x; break;
        }
        return new Color32((byte)Mathf.RoundToInt((r + m) * 255f), (byte)Mathf.RoundToInt((g + m) * 255f), (byte)Mathf.RoundToInt((b + m) * 255f), 255);
    }

    private sealed class Dialog
    {
        private readonly VisualElement overlay = new VisualElement();
        private readonly VisualElement spectrum = new VisualElement { name = "colorSpectrum", focusable = true };
        private readonly VisualElement luminance = new VisualElement { name = "colorLightness", focusable = true };
        private readonly VisualElement preview = new VisualElement();
        private readonly TextField hex = new TextField("Hex color") { name = "colorHex", maxLength = 7 };
        private readonly TextField[] rgb = { new TextField("Red"), new TextField("Green"), new TextField("Blue") };
        private readonly TextField[] hsl = { new TextField("Hue"), new TextField("Saturation"), new TextField("Lightness") };
        private readonly Dictionary<TextField, Action<string>> errors = new Dictionary<TextField, Action<string>>();
        private readonly Dictionary<TextField, Func<string>> validators = new Dictionary<TextField, Func<string>>();
        private readonly List<(VisualElement Element, bool Enabled)> underlay = new List<(VisualElement, bool)>();
        private readonly List<(Button Button, Color32 Color)> basicButtons = new List<(Button, Color32)>();
        private readonly Button[] customButtons = new Button[16];
        private readonly Label status = SettingsFormStyle.Label("", "settings-validation-summary");
        private readonly Button apply, addCustom;
        private readonly Action<Color32> chosen;
        private readonly VisualElement previousFocus;
        private readonly string[] custom;
        private Color32 selected;
        private float hue, saturation, lightness;
        private int customSlot;
        private bool warnings, closed;

        internal Dialog(VisualElement host, Color32 initial, Action<Color32> chosen)
        {
            this.chosen = chosen;
            custom = DecodePalette(PlayerPrefs.GetString(PalettePreference, ""));
            previousFocus = host.panel?.focusController.focusedElement as VisualElement;
            initial.a = 255;
            SettingsFormStyle.Apply(overlay); overlay.name = "gameColorPicker";
            overlay.AddToClassList("settings-confirmation-overlay");
            var styles = Resources.Load<StyleSheet>("Settings/ColorPicker"); if (styles != null) overlay.styleSheets.Add(styles);
            var dialog = new VisualElement(); dialog.AddToClassList("settings-confirmation-dialog"); dialog.AddToClassList("game-color-picker-dialog");
            dialog.Add(SettingsFormStyle.Label("Choose Color", "settings-group-heading"));
            var body = Element("color-picker-body");
            var visuals = Element("color-picker-visuals");
            visuals.Add(SettingsFormStyle.Label("Color spectrum", "settings-note"));
            var graphs = Element("color-picker-graphs");
            spectrum.AddToClassList("color-picker-spectrum"); luminance.AddToClassList("color-picker-lightness");
            spectrum.tooltip = "Drag to choose hue and saturation. Arrow keys adjust the selection.";
            luminance.tooltip = "Drag up for a lighter color or down for a darker color. Up and Down keys adjust lightness.";
            graphs.Add(spectrum); graphs.Add(luminance); visuals.Add(graphs);
            var previews = Element("color-picker-previews");
            previews.Add(Preview("Current", initial));
            var newPreview = Element("color-picker-preview-column"); newPreview.Add(SettingsFormStyle.Label("New", "settings-note"));
            preview.AddToClassList("color-picker-preview"); newPreview.Add(preview); previews.Add(newPreview); visuals.Add(previews);
            hex.tooltip = "Enter six hexadecimal digits, such as #009BDD.";
            PrepareField(hex, () => TryHex(hex.value, out _) ? null : "Use #RRGGBB."); visuals.Add(hex); body.Add(visuals);
            var palettes = Element("color-picker-palettes");
            palettes.Add(SettingsFormStyle.Label("Basic colors", "settings-note"));
            var basic = Element("color-picker-swatches");
            foreach (string value in BasicColors)
            {
                TryHex(value, out var color);
                var button = Swatch(color, "Choose " + value, () => SetRgb(color));
                basicButtons.Add((button, color)); basic.Add(button);
            }
            palettes.Add(basic); palettes.Add(SettingsFormStyle.Label("Custom colors", "settings-note"));
            var customs = Element("color-picker-swatches");
            for (int i = 0; i < 16; i++)
            {
                int index = i; TryHex(custom[i], out var color);
                customButtons[i] = Swatch(color, "Choose saved color in slot " + (i + 1), () =>
                { customSlot = index; TryHex(custom[index], out var saved); SetRgb(saved); });
                customs.Add(customButtons[i]);
            }
            palettes.Add(customs);
            addCustom = SettingsFormStyle.Button("Add", SaveCustom);
            addCustom.tooltip = "Save the new color in the highlighted custom slot. Saved colors remain available after restarting the game.";
            palettes.Add(addCustom); body.Add(palettes); dialog.Add(body);
            // Align with the editable box, rather than its label or reserved error area.
            // Measure in panel-local units so this stays aligned at different UI scales.
            void AlignCustomAction(GeometryChangedEvent evt)
            {
                var input = hex.Q(className: "unity-base-field__input");
                if (input == null || input.panel == null || addCustom.panel == null) return;
                float inputTop = input.ChangeCoordinatesTo(palettes, Vector2.zero).y;
                float naturalTop = addCustom.layout.y - addCustom.resolvedStyle.marginTop;
                float margin = Mathf.Max(0, inputTop - naturalTop);
                if (!float.IsNaN(margin) && Mathf.Abs(addCustom.resolvedStyle.marginTop - margin) > 0.1f)
                    addCustom.style.marginTop = margin;
            }
            hex.RegisterCallback<GeometryChangedEvent>(AlignCustomAction);
            palettes.RegisterCallback<GeometryChangedEvent>(AlignCustomAction);
            addCustom.RegisterCallback<GeometryChangedEvent>(AlignCustomAction);
            var numbers = Element("color-picker-numbers");
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                rgb[i].name = "colorRgb" + i; rgb[i].maxLength = 3;
                rgb[i].tooltip = "Enter the " + rgb[i].label.ToLowerInvariant() + " channel from 0 to 255.";
                PrepareField(rgb[i], () => InRange(rgb[index], 255) ? null : "Use 0-255."); numbers.Add(rgb[i]);
            }
            for (int i = 0; i < 3; i++)
            {
                int index = i, maximum = i == 0 ? 239 : 240;
                hsl[i].name = "colorHsl" + i; hsl[i].maxLength = 3;
                hsl[i].tooltip = "Enter " + hsl[i].label.ToLowerInvariant() + " from 0 to " + maximum + " (Windows HSL scale).";
                PrepareField(hsl[i], () => InRange(hsl[index], maximum) ? null : "Use 0-" + maximum + "."); numbers.Add(hsl[i]);
            }
            dialog.Add(numbers); dialog.Add(status);
            var actions = Element("settings-footer"); actions.style.justifyContent = Justify.FlexEnd;
            var cancel = SettingsFormStyle.Button("Cancel", () => Finish(false)); cancel.tooltip = "Close without changing the original color.";
            apply = SettingsFormStyle.Button("Use Color", () => { warnings = true; if (Validate()) Finish(true); }, primary: true);
            apply.tooltip = "Apply the selected color and close this dialog.";
            actions.Add(cancel); actions.Add(apply); dialog.Add(actions); overlay.Add(dialog);
            SetRgb(initial);
            foreach (var field in rgb) field.RegisterValueChangedCallback(_ =>
            {
                if (rgb.All(f => InRange(f, 255))) SetRgb(new Color32((byte)Read(rgb[0]), (byte)Read(rgb[1]), (byte)Read(rgb[2]), 255));
                else Validate();
            });
            foreach (var field in hsl) field.RegisterValueChangedCallback(_ =>
            {
                if (InRange(hsl[0], 239) && InRange(hsl[1], 240) && InRange(hsl[2], 240)) SetHsl(Read(hsl[0]) / 240f, Read(hsl[1]) / 240f, Read(hsl[2]) / 240f);
                else Validate();
            });
            hex.RegisterValueChangedCallback(_ => { if (TryHex(hex.value, out var color)) SetRgb(color); else Validate(); });
            spectrum.generateVisualContent += DrawSpectrum; luminance.generateVisualContent += DrawLightness;
            BindDrag(spectrum, point =>
            {
                var r = spectrum.contentRect;
                if (r.width > 0 && r.height > 0) SetHsl(Mathf.Clamp01((point.x - r.x) / r.width) * 239f / 240f,
                    1f - Mathf.Clamp01((point.y - r.y) / r.height), lightness);
            });
            BindDrag(luminance, point =>
            { var r = luminance.contentRect; if (r.height > 0) SetHsl(hue, saturation, 1f - Mathf.Clamp01((point.y - r.y) / r.height)); });
            spectrum.RegisterCallback<KeyDownEvent>(evt =>
            {
                float h = hue, s = saturation;
                if (evt.keyCode == KeyCode.LeftArrow) h -= 1f / 240f;
                else if (evt.keyCode == KeyCode.RightArrow) h += 1f / 240f;
                else if (evt.keyCode == KeyCode.UpArrow) s += 1f / 240f;
                else if (evt.keyCode == KeyCode.DownArrow) s -= 1f / 240f; else return;
                SetHsl(Mathf.Clamp(h, 0, 239f / 240f), s, lightness); evt.StopPropagation();
            });
            luminance.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.UpArrow && evt.keyCode != KeyCode.DownArrow) return;
                SetHsl(hue, saturation, lightness + (evt.keyCode == KeyCode.UpArrow ? 1 : -1) / 240f); evt.StopPropagation();
            });
            overlay.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape) { Finish(false); evt.StopPropagation(); }
                else if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                { warnings = true; if (Validate()) Finish(true); evt.StopPropagation(); }
            });
            var parent = host.parent ?? host;
            foreach (var child in parent.Children()) { underlay.Add((child, child.enabledSelf)); child.SetEnabled(false); }
            parent.Add(overlay);
            overlay.RegisterCallback<DetachFromPanelEvent>(evt => { if (ReferenceEquals(evt.target, overlay)) RestoreUnderlay(); });
            spectrum.Focus();
        }

        private static VisualElement Element(string style) { var e = new VisualElement(); e.AddToClassList(style); return e; }
        private static VisualElement Preview(string title, Color32 color)
        {
            var column = Element("color-picker-preview-column"); column.Add(SettingsFormStyle.Label(title, "settings-note"));
            var well = Element("color-picker-preview"); well.style.backgroundColor = (Color)color; column.Add(well); return column;
        }
        private static Button Swatch(Color32 color, string tooltip, Action click)
        {
            var button = new Button(click) { tooltip = tooltip }; button.AddToClassList("color-picker-swatch"); button.style.backgroundColor = (Color)color; return button;
        }
        private void PrepareField(TextField field, Func<string> validate)
        {
            SettingsFormStyle.StyleField(field); field.AddToClassList("color-picker-field");
            errors[field] = SettingsFormStyle.InlineError(field); validators[field] = validate;
            field.RegisterCallback<FocusOutEvent>(_ => { warnings = true; Validate(); });
        }
        private static bool InRange(TextField field, int max) => int.TryParse(field.value, NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value >= 0 && value <= max;
        private static int Read(TextField field) => int.Parse(field.value, CultureInfo.InvariantCulture);
        private bool Validate()
        {
            bool valid = true;
            foreach (var pair in validators) { string error = pair.Value(); valid &= error == null; errors[pair.Key](warnings ? error : null); }
            apply?.SetEnabled(valid); addCustom?.SetEnabled(valid); return valid;
        }
        private void SetRgb(Color32 color)
        {
            selected = color; selected.a = 255;
            ToHsl(selected, out var h, out saturation, out lightness); if (saturation > 0) hue = h;
            Sync();
        }
        private void SetHsl(float h, float s, float l)
        {
            hue = Mathf.Clamp(h, 0, 239f / 240f); saturation = Mathf.Clamp01(s); lightness = Mathf.Clamp01(l);
            selected = FromHsl(hue, saturation, lightness); Sync();
        }
        private void Sync()
        {
            rgb[0].SetValueWithoutNotify(selected.r.ToString(CultureInfo.InvariantCulture));
            rgb[1].SetValueWithoutNotify(selected.g.ToString(CultureInfo.InvariantCulture));
            rgb[2].SetValueWithoutNotify(selected.b.ToString(CultureInfo.InvariantCulture));
            hsl[0].SetValueWithoutNotify(Mathf.Clamp(Mathf.RoundToInt(hue * 240f), 0, 239).ToString(CultureInfo.InvariantCulture));
            hsl[1].SetValueWithoutNotify(Mathf.RoundToInt(saturation * 240f).ToString(CultureInfo.InvariantCulture));
            hsl[2].SetValueWithoutNotify(Mathf.RoundToInt(lightness * 240f).ToString(CultureInfo.InvariantCulture));
            hex.SetValueWithoutNotify(Hex(selected)); preview.style.backgroundColor = (Color)selected;
            foreach (var pair in basicButtons) pair.Button.EnableInClassList("color-picker-current-swatch", Hex(pair.Color) == Hex(selected));
            for (int i = 0; i < customButtons.Length; i++)
            {
                TryHex(custom[i], out var color); customButtons[i].style.backgroundColor = (Color)color;
                customButtons[i].EnableInClassList("color-picker-custom-target", i == customSlot);
                customButtons[i].tooltip = "Custom slot " + (i + 1) + ": " + custom[i] + ". Click to choose this color or select this slot to replace it.";
            }
            spectrum.MarkDirtyRepaint(); luminance.MarkDirtyRepaint(); Validate();
        }
        private void SaveCustom()
        {
            warnings = true; if (!Validate()) return;
            string previous = custom[customSlot]; custom[customSlot] = Hex(selected);
            try
            {
                PlayerPrefs.SetString(PalettePreference, string.Join(",", custom)); PlayerPrefs.Save();
                SettingsFormStyle.SetValidationMessage(status, "Custom color saved.", false);
                customSlot = (customSlot + 1) % 16; Sync();
            }
            catch (Exception ex)
            {
                custom[customSlot] = previous;
                SettingsFormStyle.SetValidationMessage(status, "Could not save custom colors. " + ex.Message, true);
            }
        }
        private void Finish(bool commit)
        {
            if (closed) return; closed = true;
            overlay.RemoveFromHierarchy(); RestoreUnderlay();
            if (commit) chosen?.Invoke(selected);
        }
        private void RestoreUnderlay()
        {
            foreach (var item in underlay) item.Element.SetEnabled(item.Enabled);
            underlay.Clear(); previousFocus?.Focus();
        }
        private static void BindDrag(VisualElement element, Action<Vector2> select)
        {
            element.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return; element.Focus(); element.CapturePointer(evt.pointerId); select(evt.localPosition); evt.StopPropagation();
            });
            element.RegisterCallback<PointerMoveEvent>(evt => { if (element.HasPointerCapture(evt.pointerId)) { select(evt.localPosition); evt.StopPropagation(); } });
            element.RegisterCallback<PointerUpEvent>(evt => { if (element.HasPointerCapture(evt.pointerId)) { element.ReleasePointer(evt.pointerId); evt.StopPropagation(); } });
            element.RegisterCallback<PointerCancelEvent>(evt => { if (element.HasPointerCapture(evt.pointerId)) element.ReleasePointer(evt.pointerId); });
        }
        private void DrawSpectrum(MeshGenerationContext context)
        {
            var r = spectrum.contentRect; if (r.width <= 0 || r.height <= 0) return;
            const int columns = 48, rows = 24;
            var mesh = context.Allocate((columns + 1) * (rows + 1), columns * rows * 6);
            for (int y = 0; y <= rows; y++) for (int x = 0; x <= columns; x++)
                mesh.SetNextVertex(new Vertex { position = new Vector3(r.x + r.width * x / columns, r.y + r.height * y / rows, Vertex.nearZ), tint = FromHsl(x / (float)columns, 1f - y / (float)rows, 0.5f) });
            for (int y = 0; y < rows; y++) for (int x = 0; x < columns; x++)
            {
                ushort i = (ushort)(y * (columns + 1) + x);
                mesh.SetNextIndex(i); mesh.SetNextIndex((ushort)(i + 1)); mesh.SetNextIndex((ushort)(i + columns + 1));
                mesh.SetNextIndex((ushort)(i + 1)); mesh.SetNextIndex((ushort)(i + columns + 2)); mesh.SetNextIndex((ushort)(i + columns + 1));
            }
            var p = context.painter2D; var center = new Vector2(r.x + r.width * hue, r.y + r.height * (1f - saturation));
            p.strokeColor = Color.black; p.lineWidth = 3; p.BeginPath(); p.Arc(center, 5, Angle.Degrees(0), Angle.Degrees(360)); p.Stroke();
            p.strokeColor = Color.white; p.lineWidth = 1; p.BeginPath(); p.Arc(center, 5, Angle.Degrees(0), Angle.Degrees(360)); p.Stroke();
        }
        private void DrawLightness(MeshGenerationContext context)
        {
            var r = luminance.contentRect; if (r.width <= 0 || r.height <= 0) return;
            const int rows = 48; var mesh = context.Allocate((rows + 1) * 2, rows * 6);
            for (int y = 0; y <= rows; y++)
            {
                float t = y / (float)rows; var tint = FromHsl(hue, saturation, 1f - t);
                mesh.SetNextVertex(new Vertex { position = new Vector3(r.xMin, r.y + r.height * t, Vertex.nearZ), tint = tint });
                mesh.SetNextVertex(new Vertex { position = new Vector3(r.xMax, r.y + r.height * t, Vertex.nearZ), tint = tint });
            }
            for (int i = 0; i < rows; i++)
            {
                ushort n = (ushort)(i * 2); mesh.SetNextIndex(n); mesh.SetNextIndex((ushort)(n + 1)); mesh.SetNextIndex((ushort)(n + 2));
                mesh.SetNextIndex((ushort)(n + 1)); mesh.SetNextIndex((ushort)(n + 3)); mesh.SetNextIndex((ushort)(n + 2));
            }
            float marker = r.y + r.height * (1f - lightness); var p = context.painter2D;
            p.strokeColor = Color.black; p.lineWidth = 4; p.BeginPath(); p.MoveTo(new Vector2(r.xMin, marker)); p.LineTo(new Vector2(r.xMax, marker)); p.Stroke();
            p.strokeColor = Color.white; p.lineWidth = 2; p.BeginPath(); p.MoveTo(new Vector2(r.xMin, marker)); p.LineTo(new Vector2(r.xMax, marker)); p.Stroke();
        }
    }
}
