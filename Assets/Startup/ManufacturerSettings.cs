using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

internal sealed class ManufacturerSettings : DefinitionSettings<ManufacturerCatalog.Entry>
{
    private readonly Button delete;
    private int selectedId;

    internal ManufacturerSettings(Action back)
        : base("Manufacturer Settings", "Manufacturer", back, selectorInTabRow: true)
    {
        Button add = null;
        add = ActionButton("Add", "Add a manufacturer and optionally import its logo.", () => Add(add));
        AddFooterAction(add);
        delete = ActionButton("Delete", "Remove the selected manufacturer from the catalog.",
            () => Delete(selectedId, delete));
        delete.style.display = DisplayStyle.None;
        AddFooterAction(delete);
    }

    private static string EntryId(int id) => id.ToString(CultureInfo.InvariantCulture);

    protected override IEnumerable<Entry> LoadEntries()
    {
        foreach (var manufacturer in ManufacturerCatalog.Active)
            yield return new Entry(EntryId(manufacturer.id), manufacturer.name, manufacturer,
                draft => ManufacturerCatalog.SaveEntry(draft.id, draft.name, draft.logo));
    }

    protected override IEnumerable<(int Tab, string Error)> ValidationErrors(ManufacturerCatalog.Entry draft)
    {
        if (ManufacturerCatalog.NameError(draft.id, draft.name) is string error)
            yield return (0, error);
    }

    protected override void Draw(ManufacturerCatalog.Entry draft, int tab)
    {
        var panel = Group(null);
        panel.style.maxWidth = 640;
        SettingsFormStyle.NameAndLogo(panel, "Manufacturer Name", "Manufacturer Logo", draft.name, draft.logo,
            value => ManufacturerCatalog.NameError(draft.id, value),
            value => { draft.name = value; Changed(); },
            value => { draft.logo = value; Changed(); }, sideBySide: true);

        selectedId = draft.id;
        delete.style.display = ManufacturerCatalog.CanDelete(selectedId) ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void Add(Button source)
    {
        var dialog = Dialog("Add Manufacturer", source, out var close);
        dialog.style.width = 640;
        string name = "", logo = "";
        bool attempted = false;
        var nameField = SettingsFormStyle.NameAndLogo(dialog, "Manufacturer Name", "Manufacturer Logo", name, logo,
            value => attempted ? ManufacturerCatalog.NameError(null, value) : null,
            value => name = value, value => logo = value, sideBySide: true);
        var status = SettingsFormStyle.Label("", "settings-validation-summary");
        status.style.minHeight = 27;
        dialog.Add(status);
        var actions = Actions(dialog, close);
        actions.Add(ActionButton("Save", "Save the new manufacturer.", () =>
        {
            attempted = true;
            try
            {
                int id = ManufacturerCatalog.SaveEntry(null, name, logo);
                close();
                OpenEntry(EntryId(id));
            }
            catch (Exception failure) when (SeriesSettingsStore.IsSettingsError(failure) || failure is OverflowException)
            {
                SettingsFormStyle.SetValidationMessage(status, failure.Message, true);
            }
        }));
        nameField.Focus();
    }

    private void Delete(int id, Button source)
    {
        var dialog = Dialog("Delete manufacturer?", source, out var close);
        var message = SettingsFormStyle.Label(
            $"Remove {ManufacturerCatalog.Name(id)} and discard its unsaved edits? Existing series and careers keep their references.",
            "settings-note");
        message.style.whiteSpace = WhiteSpace.Normal;
        dialog.Add(message);
        var error = SettingsFormStyle.Label("", "settings-validation-summary");
        dialog.Add(error);
        var actions = Actions(dialog, close);
        actions.Add(ActionButton("Delete", "Remove this manufacturer from the catalog.", () =>
        {
            try
            {
                ManufacturerCatalog.Delete(id);
                DiscardDraft(EntryId(id));
                close();
                OpenEntry(null);
            }
            catch (Exception failure) when (SeriesSettingsStore.IsSettingsError(failure) || failure is InvalidOperationException)
            {
                SettingsFormStyle.SetValidationMessage(error, failure.Message, true);
            }
        }));
        actions[0].Focus();
    }

    private VisualElement Dialog(string title, Button source, out Action close)
    {
        var overlay = new VisualElement();
        overlay.AddToClassList("settings-confirmation-overlay");
        var dialog = new VisualElement();
        dialog.AddToClassList("settings-confirmation-dialog");
        dialog.AddToClassList("settings-msaa-dialog");
        dialog.style.width = 440;
        dialog.style.maxWidth = Length.Percent(94);
        dialog.Add(SettingsFormStyle.Label(title, "settings-group-heading"));
        overlay.Add(dialog);
        SetFormEnabled(false);
        Screen.Add(overlay);
        void Close()
        {
            overlay.RemoveFromHierarchy();
            SetFormEnabled(true);
            source.Focus();
        }
        close = Close;
        overlay.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Escape) return;
            Close();
            evt.StopPropagation();
        }, TrickleDown.TrickleDown);
        return dialog;
    }

    private static VisualElement Actions(VisualElement dialog, Action close)
    {
        var actions = Row(dialog);
        actions.AddToClassList("settings-confirmation-actions");
        actions.style.borderTopColor = (Color)new Color32(0, 155, 221, 255);
        actions.style.borderTopWidth = 1;
        actions.style.paddingTop = 6;
        actions.Add(ActionButton("Cancel", "Close this dialog without saving changes.", close));
        return actions;
    }

    private static Button ActionButton(string text, string tooltip, Action clicked)
    {
        var button = SettingsFormStyle.Button(text, clicked);
        button.tooltip = tooltip;
        return button;
    }
}
