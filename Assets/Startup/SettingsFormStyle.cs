using System;
using UnityEngine;
using UnityEngine.UIElements;

internal static class SettingsFormStyle
{
    public static void StylePanel(VisualElement panel)
    {
        panel.generateVisualContent += context =>
        {
            var title = panel.Q<Label>(className: "team-home-heading");
            var rect = panel.contentRect;
            float w = panel.layout.width - 1f;
            float h = panel.layout.height - 1f;
            if (w <= 0 || h <= 0) return;
            float left = title == null ? 12 : Mathf.Clamp(title.layout.x, 10, w - 10);
            float right = title == null ? left : Mathf.Clamp(title.layout.xMax, left, w - 10);
            float top = title == null ? 0 : Mathf.Min(0, title.layout.y);
            var painter = context.painter2D;
            painter.fillColor = new Color(16f / 255f, 21f / 255f, 30f / 255f, 0.94f);
            painter.BeginPath();
            painter.MoveTo(new Vector2(10, 0));
            painter.LineTo(new Vector2(left, 0));
            float titleRadius = Mathf.Min(5f, (right - left) * 0.5f, -top);
            painter.LineTo(new Vector2(left, top + titleRadius));
            painter.QuadraticCurveTo(new Vector2(left, top), new Vector2(left + titleRadius, top));
            painter.LineTo(new Vector2(right - titleRadius, top));
            painter.QuadraticCurveTo(new Vector2(right, top), new Vector2(right, top + titleRadius));
            painter.LineTo(new Vector2(right, 0));
            painter.LineTo(new Vector2(w - 10, 0));
            painter.QuadraticCurveTo(new Vector2(w, 0), new Vector2(w, 10));
            painter.LineTo(new Vector2(w, h - 10));
            painter.QuadraticCurveTo(new Vector2(w, h), new Vector2(w - 10, h));
            painter.LineTo(new Vector2(10, h));
            painter.QuadraticCurveTo(new Vector2(1, h), new Vector2(1, h - 10));
            painter.LineTo(new Vector2(1, 10));
            painter.QuadraticCurveTo(new Vector2(1, 0), new Vector2(10, 0));
            painter.ClosePath();
            painter.Fill();
            bool selected = panel.ClassListContains("shop-strip-selected");
            painter.strokeColor = selected ? new Color32(85, 196, 239, 255) : new Color32(0, 155, 221, 255);
            painter.lineWidth = selected ? 2f : 1f;
            painter.BeginPath();
            painter.MoveTo(new Vector2(right, 0));
            painter.LineTo(new Vector2(w - 10, 0));
            painter.QuadraticCurveTo(new Vector2(w, 0), new Vector2(w, 10));
            painter.LineTo(new Vector2(w, h - 10));
            painter.QuadraticCurveTo(new Vector2(w, h), new Vector2(w - 10, h));
            painter.LineTo(new Vector2(10, h));
            painter.QuadraticCurveTo(new Vector2(1, h), new Vector2(1, h - 10));
            painter.LineTo(new Vector2(1, 10));
            painter.QuadraticCurveTo(new Vector2(1, 0), new Vector2(10, 0));
            painter.LineTo(new Vector2(left, 0));
            painter.Stroke();
        };
        panel.RegisterCallback<GeometryChangedEvent>(_ => panel.MarkDirtyRepaint());
    }

    // Validate rendered geometry rather than assuming shared USS rules remain content-sized.
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void CheckSelectorListSpacing(VisualElement selector, VisualElement list)
    {
        bool reported = false;
        void Check(GeometryChangedEvent evt)
        {
            if (selector.panel == null || list.panel == null || selector.layout.height <= 0 || list.layout.height <= 0)
                return;
            float naturalHeight = selector.resolvedStyle.paddingTop + selector.resolvedStyle.paddingBottom +
                selector.resolvedStyle.borderTopWidth + selector.resolvedStyle.borderBottomWidth;
            foreach (var child in selector.Children())
                if (child.resolvedStyle.display != DisplayStyle.None && child.resolvedStyle.position != Position.Absolute)
                    naturalHeight += child.layout.height + child.resolvedStyle.marginTop + child.resolvedStyle.marginBottom;
            float gap = list.worldBound.yMin - selector.worldBound.yMax;
            bool invalid = selector.layout.height > naturalHeight + 4f || gap > 12f;
            if (invalid && !reported)
                Debug.LogError($"Selector/list layout regression: selector has {selector.layout.height - naturalHeight:0.#}px unused height; " +
                    $"gap before list is {gap:0.#}px. Keep vertical selector containers content-sized (flex-grow: 0) and let the list fill remaining space.");
            reported = invalid;
        }
        selector.RegisterCallback<GeometryChangedEvent>(Check);
        list.RegisterCallback<GeometryChangedEvent>(Check);
    }

    public static void Apply(VisualElement root)
    {
        root.AddToClassList("settings-form");
        root.AddToClassList("google-sans");
        root.AddToClassList("ui-default-font");
        var typography = Resources.Load<StyleSheet>("Fonts/GoogleSans/SettingsTypography");
        var controls = Resources.Load<StyleSheet>("Settings/SettingsControls");
        if (typography != null)
            root.styleSheets.Add(typography);
        if (controls != null)
            root.styleSheets.Add(controls);
        AddHoverTooltips(root);
    }

    public static void AddHoverTooltips(VisualElement root)
    {
        const long delayMs = 350;
        var tooltip = Label(string.Empty, "settings-hover-tooltip");
        tooltip.pickingMode = PickingMode.Ignore;
        tooltip.style.display = DisplayStyle.None;
        root.hierarchy.Add(tooltip);

        VisualElement hovered = null;
        IVisualElementScheduledItem pending = null;
        Vector2 pointer = default;
        root.RegisterCallback<PointerMoveEvent>(evt =>
        {
            if (evt.pointerType != UnityEngine.UIElements.PointerType.mouse)
                return;

            pointer = evt.position;
            var control = FindControl(evt.target as VisualElement, out _);
            if (control == hovered)
                return;

            Hide();
            if (control != null)
            {
                hovered = control;
                pending = root.schedule.Execute(Show).StartingIn(delayMs);
            }
        }, TrickleDown.TrickleDown);
        root.RegisterCallback<PointerLeaveEvent>(evt =>
        {
            if (evt.target == root)
                Hide();
        });
        root.RegisterCallback<PointerDownEvent>(_ => Hide(), TrickleDown.TrickleDown);
        root.RegisterCallback<KeyDownEvent>(_ => Hide(), TrickleDown.TrickleDown);
        root.RegisterCallback<WheelEvent>(_ => Hide(), TrickleDown.TrickleDown);
        root.RegisterCallback<DetachFromPanelEvent>(evt =>
        {
            if (evt.target == root)
                Hide();
        });

        VisualElement FindControl(VisualElement target, out string text)
        {
            for (var element = target; element != null && element != root; element = element.parent)
            {
                text = element switch
                {
                    Button button => ButtonTooltip(button.text),
                    DropdownField dropdown => DropdownTooltip(dropdown.label),
                    Toggle toggle => ToggleTooltip(toggle.label),
                    _ => null
                };
                // Icons, graphs, and other visual elements may also provide explicit help.
                if (!string.IsNullOrWhiteSpace(element.tooltip))
                    text = element.tooltip;
                if (!string.IsNullOrWhiteSpace(text))
                    return element;
            }
            text = null;
            return null;
        }

        void Show()
        {
            pending = null;
            if (hovered == null || hovered.panel != root.panel || !hovered.worldBound.Contains(pointer) ||
                FindControl(hovered, out var text) != hovered)
            {
                Hide();
                return;
            }

            var bounds = root.contentRect;
            if (bounds.width <= 18f || bounds.height <= 0f)
                return;

            tooltip.text = text;
            tooltip.style.display = DisplayStyle.Flex;
            tooltip.BringToFront();
            var size = tooltip.MeasureTextSize(text, Mathf.Min(280f, bounds.width - 18f),
                VisualElement.MeasureMode.AtMost, 0f, VisualElement.MeasureMode.Undefined);
            float width = Mathf.Ceil(size.x) + 18f;
            float height = Mathf.Ceil(size.y) + 12f;
            var local = root.WorldToLocal(pointer);
            tooltip.style.width = width;
            tooltip.style.left = Mathf.Clamp(local.x + 12f, 0f, Mathf.Max(0f, bounds.width - width));
            float top = local.y + 16f;
            tooltip.style.top = top + height <= bounds.height ? top : Mathf.Max(0f, local.y - height - 8f);
        }

        void Hide()
        {
            pending?.Pause();
            pending = null;
            hovered = null;
            tooltip.style.display = DisplayStyle.None;
        }
    }

    private static string ButtonTooltip(string text) => text switch
    {
        "Back" => "Return to the previous screen.",
        "Revert" => "Restore the last saved values.",
        "Save and Go Back" => "Save your changes and return to the previous screen.",
        "Discard and Go Back" => "Discard your changes and return to the previous screen.",
        "Keep Editing" => "Close this prompt and continue editing.",
        "Reset Colors" => "Preview the default color scheme.",
        "Remove" => "Remove this entry.",
        "Browse…" or "Browse..." => "Choose an image file.",
        "Series Settings" => "Open the editor for series rules, manufacturers, staffing, costs, and artwork.",
        "Car Type Settings" => "Open the editor for car models, cockpit cameras, dashboards, and number placement.",
        "Game Settings" => "Open the game's display, simulation, and color preferences.",
        "Display" => "Show window mode and resolution settings.",
        "Simulation" => "Show the simulation models, pace vehicle, and practice testing options.",
        "Team colors" => "Edit the active team's primary and secondary colors.",
        "Color scheme" => "Edit the game's interface palette and preview changes as you type.",
        "General" => "Show the basic settings for this entry.",
        "Manufacturers & engines" => "Choose eligible manufacturers and configure their engine suppliers.",
        "Staff" => "Set the roster limit, available roles, and salary ranges.",
        "Costs & sponsorship" => "Edit chassis and component costs, event expenses, and sponsorship ranges.",
        "Artwork" => "Set the series logo and team-screen backgrounds.",
        "Details" => "Edit the track's name, location, style, and surface.",
        "Layout & banking" => "Edit track dimensions, banking, and layout points.",
        "Race & qualifying" => "Edit race length, stage laps, qualifying settings, and race markers.",
        "Pit road" => "Edit the pit layout, stall count, and speed-limit markers.",
        "Ratings" => "Set how much different parts of the car contribute to performance at this track.",
        "Cameras & mirror" => "Position the cockpit cameras and rearview mirror for this car.",
        "Number placement" => "Position the car numbers on the body and roof.",
        "Dashboard" => "Choose the dashboard artwork and configure its gauges.",
        "Clear" => "Clear the artwork assigned to this field.",
        "Use drawn dashboard" => "Clear the custom dashboard image and use the built-in dashboard drawing.",
        _ => null
    };

    private static string DropdownTooltip(string label) => label switch
    {
        "Display mode" => "Choose whether the game fills the screen or runs in a borderless window.",
        "Screen size" => "Set the game resolution. Desktop uses the monitor's current resolution.",
        "Pace vehicle" => "Choose the car or truck that leads the field during pace laps.",
        "Racing series" => "Choose the series whose rules, suppliers, staffing, and artwork you want to edit.",
        "Track" => "Choose the track whose settings you want to edit.",
        "Car Type" => "Choose the car model whose cockpit, dashboard, and number placement you want to edit.",
        "Car model" => "Set which car model teams in this series use on track.",
        "Track style" => "Classify the layout as an oval or road-course variant. This also sets its broad track category.",
        "Surface" => "Set the track's racing surface material.",
        _ => null
    };

    private static string ToggleTooltip(string label) => label switch
    {
        "Restrictor plate" => "Enable superspeedway rules for this track, including restrictor-plate drafting behavior.",
        "Override start and finish" => "Use the specified track position for the start/finish line.",
        "Override restart zone" => "Use the specified start and end positions for the restart zone.",
        "Enable pit road" => "Allow cars to use the configured pit entrance, lane, stalls, and exit.",
        "Share suppliers across manufacturers" => "Give all manufacturers in this series access to the same engine supplier list.",
        "Dark gauge faces" => "Use dark dial faces for the in-car dashboard gauges.",
        "RPM" => "Show the engine RPM gauge on this car's dashboard.",
        "Fuel" => "Show the remaining fuel gauge on this car's dashboard.",
        "Throttle" => "Show throttle input on this car's dashboard.",
        "Brake" => "Show brake input on this car's dashboard.",
        _ when label?.StartsWith("Large ", StringComparison.Ordinal) == true =>
            $"Use the larger dashboard dial for {label.Substring(6).ToLowerInvariant()}.",
        _ => null
    };

    internal static Toggle PillSwitch(string label, bool value = false, bool labelAbove = false) =>
        new PillToggle(label, value, labelAbove);

    private sealed class PillToggle : Toggle
    {
        private readonly VisualElement track;

        internal PillToggle(string label, bool value, bool labelAbove) : base(labelAbove ? label : null)
        {
            if (!labelAbove)
                text = label;
            style.minHeight = 24;

            var input = this.Q(className: "unity-base-field__input");
            input.Q(className: "unity-toggle__checkmark").style.display = DisplayStyle.None;
            input.style.alignItems = Align.Center;
            input.style.justifyContent = Justify.FlexStart;
            track = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    width = 38,
                    height = 21,
                    marginRight = 7,
                    flexShrink = 0,
                    paddingLeft = 3,
                    paddingRight = 3,
                    justifyContent = Justify.Center,
                    borderTopLeftRadius = 11,
                    borderTopRightRadius = 11,
                    borderBottomLeftRadius = 11,
                    borderBottomRightRadius = 11
                }
            };
            var thumb = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    width = 15,
                    height = 15,
                    backgroundColor = (Color)new Color32(16, 21, 30, 255),
                    borderTopLeftRadius = 8,
                    borderTopRightRadius = 8,
                    borderBottomLeftRadius = 8,
                    borderBottomRightRadius = 8
                }
            };
            track.Add(thumb);
            input.Insert(0, track);
            SetValueWithoutNotify(value);
        }

        public override void SetValueWithoutNotify(bool newValue)
        {
            base.SetValueWithoutNotify(newValue);
            if (track == null) return;
            track.style.backgroundColor = newValue
                ? (Color)new Color32(0, 155, 221, 255)
                : (Color)new Color32(74, 88, 105, 255);
            track.style.alignItems = newValue ? Align.FlexEnd : Align.FlexStart;
        }
    }

    internal static TextField NameAndLogo(VisualElement details, string nameLabelText, string logoLabelText,
        string nameValue, string logoValue, Func<string, string> validateName, Action<string> setName,
        Action<string> setLogo, bool sideBySide = false)
    {
        var content = new VisualElement();
        details.Add(content);
        if (sideBySide)
        {
            content.style.flexDirection = FlexDirection.Row;
            content.style.alignItems = Align.FlexStart;
        }
        var nameColumn = new VisualElement();
        nameColumn.style.flexShrink = 0;
        if (sideBySide)
        {
            nameColumn.style.flexGrow = 1;
            nameColumn.style.flexBasis = 0;
            nameColumn.style.minWidth = 0;
        }
        var nameHeading = new VisualElement();
        nameHeading.AddToClassList("settings-row");
        nameColumn.Add(nameHeading);
        nameHeading.style.alignItems = Align.Center;
        nameHeading.style.flexWrap = Wrap.NoWrap;
        var nameLabel = Label(nameLabelText, "settings-group-heading");
        nameLabel.style.flexShrink = 0;
        nameHeading.Add(nameLabel);
        content.Add(nameColumn);

        Action<string> showNameError = null;
        var name = Field(new TextField(nameLabelText), nameValue ?? "", value =>
        {
            setName(value);
            showNameError(validateName(value));
        });
        name.labelElement.style.display = DisplayStyle.None;
        name.style.flexGrow = 0;
        name.style.flexShrink = 0;
        name.style.marginLeft = name.style.marginRight = 0;
        name.style.marginTop = 0;
        var nameInput = name.Q(className: "unity-base-field__input");
        nameInput.style.height = 42;
        nameInput.style.flexBasis = 42;
        nameInput.style.flexGrow = 0;
        nameInput.style.flexShrink = 0;
        showNameError = InlineError(name);
        var errorSlot = name.Q(className: "settings-error-slot");
        nameColumn.Add(name);
        if (sideBySide)
            nameColumn.Add(errorSlot);
        else
        {
            errorSlot.style.flexGrow = 1;
            errorSlot.style.height = 24;
            errorSlot.style.marginBottom = 0;
            nameHeading.Add(errorSlot);
        }
        showNameError(validateName(nameValue));

        var logoField = new VisualElement();
        logoField.style.flexShrink = 0;
        if (sideBySide)
        {
            logoField.style.marginLeft = 24;
            logoField.style.width = 300;
            logoField.style.maxWidth = Length.Percent(50);
        }
        else
        {
            logoField.style.marginTop = 18;
            logoField.style.width = Length.Percent(100);
        }
        content.Add(logoField);
        LogoField(logoField, logoLabelText, () => logoValue, logo =>
        {
            logoValue = logo;
            setLogo(logo);
        });
        return name;
    }

    internal static Action LogoField(VisualElement logoField, string title, Func<string> getLogo, Action<string> setLogo)
    {
        logoField.Add(Label(title, "settings-group-heading"));
        var preview = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
        preview.style.width = 160;
        preview.style.height = 90;
        preview.style.alignSelf = Align.Center;
        logoField.Add(preview);
        var buttons = new VisualElement();
        buttons.AddToClassList("settings-row");
        logoField.Add(buttons);
        buttons.style.justifyContent = Justify.Center;
        var status = Label("", "settings-validation-summary");
        status.style.minHeight = 26;
        status.style.whiteSpace = WhiteSpace.Normal;
        status.style.unityTextAlign = TextAnchor.MiddleCenter;
        logoField.Add(status);
        AddButton("Browse", "Choose a PNG or JPEG logo.", () =>
        {
            string path = TrackImageFilePicker.Pick("Choose a logo");
            if (!string.IsNullOrEmpty(path))
                SetLogo(SeriesSettingsStore.SaveArtwork(path));
        });
        AddButton("Paste Image", "Paste a logo from the clipboard.", () =>
            SetLogo(SeriesSettingsStore.PasteArtwork()));
        AddButton("Clear", "Remove the logo.", () => SetLogo(""));
        RefreshLogo();
        return RefreshLogo;

        void AddButton(string label, string tooltip, Action action)
        {
            var button = Button(label, () =>
            {
                try { action(); }
                catch (Exception ex) when (SeriesSettingsStore.IsSettingsError(ex))
                {
                    SetValidationMessage(status, ex.Message, true);
                }
            });
            button.tooltip = tooltip;
            buttons.Add(button);
        }

        void SetLogo(string logo)
        {
            setLogo(logo);
            RefreshLogo();
        }

        void RefreshLogo()
        {
            preview.image = SeriesSettingsStore.LoadArtwork(getLogo());
            SetValidationMessage(status, preview.image == null ? "No logo selected." : "", false);
        }
    }

    public static T Field<T, TValue>(T field, TValue value, Action<TValue> changed) where T : BaseField<TValue>
    {
        StyleField(field);
        field.SetValueWithoutNotify(value);
        field.RegisterValueChangedCallback(evt =>
        {
            if (evt.target == field)
                changed(evt.newValue);
        });
        return field;
    }

    public static T StyleField<T>(T field) where T : VisualElement
    {
        field.AddToClassList("settings-field");
        if (field is PillToggle)
        {
            // Keep the pill input the same height as neighboring settings inputs.
            var pillInput = field.Q(className: "unity-base-field__input");
            pillInput.style.height = 42;
            pillInput.style.flexBasis = 42;
            pillInput.style.flexGrow = 0;
            pillInput.style.flexShrink = 0;
            pillInput.style.flexDirection = FlexDirection.Row;
            pillInput.style.alignItems = Align.Center;
        }
        var input = field.Q(className: "unity-base-text-field__input");
        input?.Query<TextElement>().ForEach(text =>
        {
            text.selection.selectAllOnFocus = false;
            text.selection.selectAllOnMouseUp = false;
        });
        field.RegisterCallback<FocusInEvent>(_ => field.AddToClassList("settings-field-focused"));
        field.RegisterCallback<FocusOutEvent>(_ => field.RemoveFromClassList("settings-field-focused"));
        return field;
    }

    public static string TextError(string value) =>
        string.IsNullOrWhiteSpace(value) ? "Enter a value." :
        value.Length > 50 ? "Use 50 characters or fewer." : null;

    public static Action<string> InlineError(VisualElement field)
    {
        field.hierarchy.Insert(0, ErrorSlot(out var message));
        return error =>
        {
            field.EnableInClassList("settings-field-invalid", error != null);
            SetValidationMessage(message, error, error != null);
        };
    }

    public static VisualElement ErrorSlot(string error)
    {
        var slot = ErrorSlot(out var label);
        SetValidationMessage(label, error, error != null);
        return slot;
    }

    private static VisualElement ErrorSlot(out Label label)
    {
        var slot = new VisualElement { pickingMode = PickingMode.Ignore };
        slot.AddToClassList("settings-error-slot");
        label = Label(string.Empty, "settings-inline-error");
        label.pickingMode = PickingMode.Ignore;
        slot.Add(label);
        return slot;
    }

    public static void SetValidationMessage(Label label, string text, bool error)
    {
        bool showError = error && !string.IsNullOrEmpty(text);
        SetErrorIcon(label, showError);
        label.text = text ?? string.Empty;
        label.tooltip = showError ? text : null;
        label.EnableInClassList("settings-status-error", showError);
    }

    public static Button Tab(string text, Action clicked)
    {
        var tab = Button(text, clicked);
        // Give layout a text-sized child alongside the absolute validation badge.
        var sizingLabel = Label(text, "settings-tab-sizing-label");
        sizingLabel.pickingMode = PickingMode.Ignore;
        tab.hierarchy.Add(sizingLabel);
        AddErrorIcon(tab).style.visibility = Visibility.Hidden;
        return tab;
    }

    public static void SetTabError(Button tab, bool error)
    {
        tab.EnableInClassList("settings-tab-invalid", error);
        tab.Q<Label>("settingsErrorIcon").style.visibility = error ? Visibility.Visible : Visibility.Hidden;
    }

    private static void SetErrorIcon(VisualElement element, bool showError)
    {
        var icon = element.Q<Label>("settingsErrorIcon");
        if (showError && icon == null)
            icon = AddErrorIcon(element);
        if (icon != null)
            icon.style.display = showError ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private static Label AddErrorIcon(VisualElement element)
    {
        var icon = Label("!", "settings-error-icon");
        icon.name = "settingsErrorIcon";
        icon.pickingMode = PickingMode.Ignore;
        element.hierarchy.Add(icon);
        return icon;
    }

    public static Button Button(string text, Action clicked, bool primary = false)
    {
        var button = new Button(clicked) { text = text };
        button.AddToClassList("settings-button");
        button.EnableInClassList("settings-button-primary", primary);
        return button;
    }

    public static Label Label(string text, string className)
    {
        var label = new Label(text);
        label.AddToClassList(className);
        return label;
    }
}

