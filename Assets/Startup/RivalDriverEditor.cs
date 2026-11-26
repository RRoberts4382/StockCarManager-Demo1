using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

internal static class RivalDriverEditor
{
    public static void Open(VisualElement underlay, Career career, SessionCarResult entry, Action saveCareer, Action refresh)
    {
        var slot = career.rivalCars.IndexOf(entry.car);
        if (slot < 0 || entry.driver == null)
            return;

        var car = entry.car;
        var driver = entry.driver;
        var paint = car.PaintStyle;
        var make = car.manufacturer;
        string savedNumber = entry.DisplayCarNumber;
        var choices = career.SeriesRules.ManufacturerChoices;
        bool showErrors = false;
        bool showingPaint = false;
        var form = new VisualElement();
        form.AddToClassList("settings-form");
        form.AddToClassList("career-inline-paint");
        form.AddToClassList("rival-driver-editor");
        SettingsFormStyle.Apply(form);
        var startupStyles = Resources.Load<StyleSheet>("Startup/Startup");
        if (startupStyles != null)
            form.styleSheets.Insert(0, startupStyles);

        var frame = new VisualElement();
        frame.AddToClassList("settings-frame");
        form.Add(frame);
        var header = new VisualElement();
        header.AddToClassList("settings-header");
        header.Add(SettingsFormStyle.Label("Edit Rival Driver", "settings-title"));
        frame.Add(header);

        Action<bool> switchTab = null;
        Action leave = null;
        Action pickMake = null;
        Action previewChanged = null;
        Func<TeamCar> previewCar = null;
        Func<bool> saveChanges = null;
        var tabs = new VisualElement();
        tabs.AddToClassList("settings-tabs");
        frame.Add(tabs);
        Button infoTab = null, paintTab = null;
        infoTab = SettingsFormStyle.Tab("Driver Info", () => switchTab(false));
        infoTab.tooltip = "Edit the rival's car number, driver name, and manufacturer.";
        paintTab = SettingsFormStyle.Tab("Paint Scheme", () => switchTab(true));
        paintTab.tooltip = "Edit the rival's paint scheme and preview the car.";
        tabs.Add(infoTab);
        tabs.Add(paintTab);

        var body = new VisualElement { style = { flexGrow = 1, minHeight = 0 } };
        frame.Add(body);
        var info = new VisualElement();
        info.AddToClassList("rival-driver-info");
        body.Add(info);
        var number = SettingsFormStyle.StyleField(new TextField { value = savedNumber });
        var name = SettingsFormStyle.StyleField(new TextField { value = driver.FullName });
        var manufacturer = SettingsFormStyle.Button(ManufacturerCatalog.Name(make), () => pickMake());
        manufacturer.tooltip = "Choose the manufacturer for this rival's car.";
        var makeField = new VisualElement();
        makeField.AddToClassList("settings-field");
        makeField.Add(manufacturer);
        var showNumberError = AddField("Car number", number);
        var showNameError = AddField("Driver name", name);
        var showMakeError = AddField("Manufacturer", makeField);

        var paintBody = new VisualElement();
        paintBody.AddToClassList("career-car-setup-body");
        body.Add(paintBody);
        var previewPanel = new VisualElement();
        previewPanel.AddToClassList("career-car-preview-panel");
        paintBody.Add(previewPanel);
        var thumbnails = CarThumbnails.CreateLargePreview(tighterOrbit: true);
        var preview = thumbnails.CreateOrbitPreview();
        previewPanel.Add(preview);
        var controls = new VisualElement();
        controls.AddToClassList("career-car-paint-controls");
        paintBody.Add(controls);
        var paintStatus = SettingsFormStyle.Label("", "settings-validation-summary");
        Action refreshPaintControls = null;
        CarPaintEditor.TemplatePicker templatePicker = new CarPaintEditor.TemplatePicker(paintStatus, template =>
        {
            paint = paint.WithTemplate(template);
            previewChanged();
        });
        templatePicker.SetSelection(paint.Template);
        previewPanel.Add(templatePicker);
        previewPanel.Add(paintStatus);
        (_, refreshPaintControls) = CarPaintEditor.BuildControls(controls, form, frame, () => previewCar(),
            Array.Empty<string>(), paint, updated =>
            {
                paint = updated.WithTemplate(paint.Template);
                previewChanged();
            }, paintStatus, () => paint.Template.IsAssigned, () => paint.HideRenderedNumber, compactLayout: true);

        var footer = new VisualElement();
        footer.AddToClassList("settings-footer");
        frame.Add(footer);
        var back = SettingsFormStyle.Button("Back", () => leave());
        back.tooltip = "Return to Driver Points.";
        var status = SettingsFormStyle.Label("", "settings-status");
        Button save = null;
        save = SettingsFormStyle.Button("Save", () => saveChanges(), primary: true);
        save.tooltip = "Save this rival's driver and paint changes.";
        footer.Add(back);
        footer.Add(status);
        footer.Add(save);
        switchTab = ShowTab;
        leave = RequestLeave;
        pickMake = PickManufacturer;
        previewChanged = UpdatePaint;
        previewCar = PreviewCar;
        saveChanges = Save;
        number.RegisterValueChangedCallback(_ => RefreshValidation());
        name.RegisterValueChangedCallback(_ => RefreshValidation());
        frame.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Escape)
                return;
            RequestLeave();
            evt.StopPropagation();
        });
        underlay.parent.Add(form);
        underlay.SetEnabled(false);
        ShowTab(false);
        infoTab.Focus();

        Action<string> AddField(string label, VisualElement field)
        {
            var row = new VisualElement();
            row.AddToClassList("rival-driver-field");
            row.Add(SettingsFormStyle.Label(label, "rival-driver-field-label"));
            row.Add(field);
            info.Add(row);
            return SettingsFormStyle.InlineError(field);
        }

        string NumberError()
        {
            var value = number.value.Trim();
            if (!TeamCar.IsValidRivalNumber(value))
                return "Enter one to four letters or digits, such as 00 or 88BX.";
            value = value.ToUpperInvariant();
            bool taken = value != savedNumber && (career.cars.Any(other => other.number > 0 &&
                other.number.ToString(System.Globalization.CultureInfo.InvariantCulture) == value) ||
                career.rivalCars.Any(other => other != car && other.RivalNumber == value));
            return taken ? "This car number is already in use." : null;
        }

        string NameError()
        {
            var value = name.value.Trim();
            return SettingsFormStyle.TextError(value) ??
                (TrackPreview.CreateTimingEntries(career.cars[0], career).Any(other => !other.playerEntry &&
                    other.driver != driver && string.Equals(other.teamName, entry.teamName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(other.driverName, value, StringComparison.OrdinalIgnoreCase))
                    ? "This team already has a driver with that name." : null);
        }

        void RefreshValidation()
        {
            string invalidNumber = NumberError();
            string invalidName = NameError();
            string invalidMake = choices.Contains(make) ? null : "Choose a manufacturer.";
            bool invalidInfo = invalidNumber != null || invalidName != null || invalidMake != null;
            showNumberError(showErrors ? invalidNumber : null);
            showNameError(showErrors ? invalidName : null);
            showMakeError(showErrors ? invalidMake : null);
            SettingsFormStyle.SetTabError(infoTab, showErrors && invalidInfo);
            save.SetEnabled(!invalidInfo);
        }

        void ShowTab(bool paintSelected)
        {
            showErrors |= paintSelected || showingPaint;
            showingPaint = paintSelected;
            info.style.display = paintSelected ? DisplayStyle.None : DisplayStyle.Flex;
            paintBody.style.display = paintSelected ? DisplayStyle.Flex : DisplayStyle.None;
            infoTab.EnableInClassList("settings-tab-current", !paintSelected);
            paintTab.EnableInClassList("settings-tab-current", paintSelected);
            RefreshValidation();
            if (paintSelected)
                UpdatePreview();
        }

        TeamCar PreviewCar()
        {
            var draft = new TeamCar
            {
                number = car.number,
                manufacturer = make,
                modelResource = career.SeriesRules.ModelFor(make),
                chassisNumber = "rival-editor-preview"
            };
            var value = number.value.Trim();
            draft.SetRivalNumber(TeamCar.IsValidRivalNumber(value) ? value : savedNumber);
            paint.ApplyTo(draft);
            return draft;
        }

        void UpdatePreview()
        {
            var draft = PreviewCar();
            preview.image = thumbnails.Get(draft, Array.Empty<string>(), draft.PaintStyle);
        }

        void UpdatePaint()
        {
            refreshPaintControls();
            UpdatePreview();
            RefreshValidation();
        }

        void PickManufacturer()
        {
            var dialog = OpenDialog("Choose a manufacturer", out var dismiss);
            foreach (var choice in choices)
            {
                var row = SettingsFormStyle.Button(ManufacturerCatalog.Name(choice), () =>
                {
                    make = choice;
                    manufacturer.text = ManufacturerCatalog.Name(make);
                    dismiss();
                    RefreshValidation();
                });
                row.AddToClassList("rival-manufacturer-choice");
                row.tooltip = "Select " + ManufacturerCatalog.Name(choice) + " for this rival's car.";
                dialog.Add(row);
            }
            var actions = DialogFooter(dialog);
            var cancel = SettingsFormStyle.Button("Cancel", dismiss);
            cancel.tooltip = "Keep the current manufacturer.";
            actions.Add(cancel);
            cancel.Focus();
        }

        bool IsDirty() => !string.Equals(number.value.Trim(), savedNumber, StringComparison.OrdinalIgnoreCase) ||
            name.value.Trim() != driver.FullName || make != car.manufacturer || !paint.Equals(car.PaintStyle);

        bool Save()
        {
            showErrors = true;
            RefreshValidation();
            if (!save.enabledSelf)
                return false;
            if (!IsDirty())
                return true;

            var updated = car.CopyForRivalEdit();
            updated.SetRivalNumber(number.value.Trim());
            if (make != car.manufacturer)
            {
                updated.manufacturer = make;
                updated.rivalManufacturerAssigned = true;
                updated.rivalManufacturerEdited = true;
                updated.modelResource = career.SeriesRules.ModelFor(make);
            }
            if (!paint.Equals(car.PaintStyle))
                paint.ApplyTo(updated);

            string previousName = driver.rivalNameOverride;
            string editedName = name.value.Trim();
            driver.rivalNameOverride = editedName == driver.OriginalName ? null : editedName;
            career.rivalCars[slot] = updated;
            try
            {
                saveCareer();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                career.rivalCars[slot] = car;
                driver.rivalNameOverride = previousName;
                SettingsFormStyle.SetValidationMessage(status, "Could not save rival changes. " + ex.Message, true);
                return false;
            }

            car = updated;
            savedNumber = updated.RivalNumber;
            SettingsFormStyle.SetValidationMessage(status, "Rival changes saved.", false);
            return true;
        }

        void RequestLeave()
        {
            if (!IsDirty())
            {
                Close();
                return;
            }
            showErrors = true;
            RefreshValidation();
            var dialog = OpenDialog("Unsaved changes", out var dismiss);
            dialog.Add(SettingsFormStyle.Label("Save or discard your rival changes before going back.", "settings-note"));
            var warning = SettingsFormStyle.Label("", "settings-validation-summary");
            dialog.Add(warning);
            var actions = DialogFooter(dialog);
            var cancel = SettingsFormStyle.Button("Cancel", dismiss);
            cancel.tooltip = "Return to the editor with your changes.";
            actions.Add(cancel);
            var discard = SettingsFormStyle.Button("Discard and Go Back", Close);
            discard.tooltip = "Return to Driver Points and discard unsaved changes.";
            actions.Add(discard);
            var saveBack = SettingsFormStyle.Button("Save", () =>
            {
                if (Save())
                    Close();
                else
                    SettingsFormStyle.SetValidationMessage(warning, status.text, true);
            }, primary: true);
            saveBack.tooltip = "Save your changes and return to Driver Points.";
            saveBack.SetEnabled(save.enabledSelf);
            actions.Add(saveBack);
            if (!save.enabledSelf)
                SettingsFormStyle.SetValidationMessage(warning, "Fix the highlighted fields before saving.", true);
            cancel.Focus();
        }

        VisualElement OpenDialog(string title, out Action dismiss)
        {
            var overlay = new VisualElement();
            overlay.AddToClassList("settings-confirmation-overlay");
            var dialog = CareerSetup.CreateDialog(overlay, title);
            dialog.AddToClassList("rival-editor-dialog");
            var focused = form.panel?.focusController.focusedElement as VisualElement;
            void Dismiss()
            {
                overlay.RemoveFromHierarchy();
                frame.SetEnabled(true);
                focused?.Focus();
            }
            dismiss = Dismiss;
            overlay.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape)
                    return;
                Dismiss();
                evt.StopPropagation();
            });
            form.Add(overlay);
            frame.SetEnabled(false);
            return dialog;
        }

        void Close()
        {
            preview.image = null;
            thumbnails.Dispose();
            form.RemoveFromHierarchy();
            underlay.SetEnabled(true);
            refresh();
        }
    }

    private static VisualElement DialogFooter(VisualElement dialog)
    {
        var actions = new VisualElement();
        actions.AddToClassList("settings-footer");
        actions.AddToClassList("settings-confirmation-actions");
        dialog.Add(actions);
        return actions;
    }
}
