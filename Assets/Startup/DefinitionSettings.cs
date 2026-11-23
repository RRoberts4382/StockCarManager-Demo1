using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

internal abstract class DefinitionSettings<T> where T : class
{
    protected sealed class Entry
    {
        public readonly string Id;
        public readonly string Name;
        public readonly T Definition;
        public readonly Action<T> Save;

        public Entry(string id, string name, T definition, Action<T> save)
        {
            Id = id;
            Name = name;
            Definition = definition;
            Save = save;
        }
    }

    private sealed class Draft
    {
        public readonly T Value;
        public readonly Dictionary<string, (string Text, string Error, int Tab)> Invalid = new();
        public bool Dirty;

        public Draft(T value) => Value = JsonUtility.FromJson<T>(JsonUtility.ToJson(value));
    }

    private readonly Action back;
    private readonly Dictionary<string, Draft> drafts = new();
    private readonly DropdownField selector;
    private readonly ScrollView body = new();
    private readonly VisualElement frame = new();
    private readonly VisualElement warning = new();
    private readonly Label status = SettingsFormStyle.Label("", "settings-status");
    private readonly Label warningText = SettingsFormStyle.Label("", "settings-note");
    private readonly Label warningError = SettingsFormStyle.Label("", "settings-validation-summary");
    private readonly Button save;
    private readonly Button revert;
    private readonly Button saveBack;
    private readonly Button keepEditing;
    private readonly Button backButton;
    private readonly Button[] tabs;
    private int activeTab;
    private Entry[] entries = Array.Empty<Entry>();
    public VisualElement Screen { get; } = new VisualElement();
    private Entry Selected => selector.index >= 0 && selector.index < entries.Length ? entries[selector.index] : null;

    protected DefinitionSettings(string title, string name, Action back, params string[] tabNames)
    {
        this.back = back;
        SettingsFormStyle.Apply(Screen);
        frame.AddToClassList("settings-frame");
        Screen.Add(frame);
        var header = new VisualElement();
        header.AddToClassList("settings-header");
        var heading = SettingsFormStyle.Label(title, "settings-title");
        heading.AddToClassList("google-sans-bold");
        header.Add(heading);
        selector = SettingsFormStyle.Field(new DropdownField(name), "", _ => Render(true));
        selector.AddToClassList("settings-series-selector");
        header.Add(selector);
        frame.Add(header);
        var navigation = new VisualElement();
        navigation.AddToClassList("settings-tabs");
        tabs = tabNames.Select((label, index) => SettingsFormStyle.Tab(label, () =>
        {
            activeTab = index;
            Render(true);
        })).ToArray();
        foreach (var tab in tabs)
            navigation.Add(tab);
        frame.Add(navigation);
        body.AddToClassList("settings-scroll");
        frame.Add(body);

        var footer = new VisualElement();
        footer.AddToClassList("settings-footer");
        footer.Add(status);
        backButton = SettingsFormStyle.Button("Back", RequestBack);
        revert = SettingsFormStyle.Button("Revert", () =>
        {
            if (Selected == null) return;
            drafts.Remove(Selected.Id);
            Render(false, "Reverted unsaved changes.");
        });
        save = SettingsFormStyle.Button("Save " + name, SaveCurrent, primary: true);
        footer.Add(backButton);
        footer.Add(revert);
        footer.Add(save);
        frame.Add(footer);

        warning.AddToClassList("settings-confirmation-overlay");
        warning.style.display = DisplayStyle.None;
        var dialog = new VisualElement();
        dialog.AddToClassList("settings-confirmation-dialog");
        var warningTitle = SettingsFormStyle.Label("Unsaved changes", "settings-group-heading");
        warningTitle.AddToClassList("google-sans-semibold");
        dialog.Add(warningTitle);
        dialog.Add(warningText);
        dialog.Add(warningError);
        var actions = Row(dialog);
        actions.AddToClassList("settings-confirmation-actions");
        keepEditing = SettingsFormStyle.Button("Keep Editing", CancelBack);
        saveBack = SettingsFormStyle.Button("Save and Go Back", SaveAll, primary: true);
        actions.Add(keepEditing);
        actions.Add(SettingsFormStyle.Button("Discard and Go Back", FinishBack));
        actions.Add(saveBack);
        warning.Add(dialog);
        warning.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Escape) return;
            CancelBack();
            evt.StopPropagation();
        });
        Screen.Add(warning);
    }

    public void Open()
    {
        string previous = Selected?.Id;
        entries = LoadEntries().ToArray();
        selector.choices = entries.Select(SelectorLabel).ToList();
        int selected = Array.FindIndex(entries, entry => entry.Id == previous);
        selector.SetValueWithoutNotify(entries.Length == 0 ? "" : SelectorLabel(entries[Math.Max(0, selected)]));
        selector.SetEnabled(entries.Length > 0);
        Screen.style.display = DisplayStyle.Flex;
        Render(true);
        selector.Focus();
    }

    private string SelectorLabel(Entry entry) =>
        entries.Count(item => item.Name == entry.Name) > 1 ? $"{entry.Name} ({entry.Id})" : entry.Name;

    protected abstract IEnumerable<Entry> LoadEntries();
    protected abstract void Draw(T draft, int tab);
    protected abstract IEnumerable<(int Tab, string Error)> ValidationErrors(T draft);
    protected virtual void PrepareSave(T draft) {}

    private Draft GetDraft(Entry entry)
    {
        if (!drafts.TryGetValue(entry.Id, out var draft))
        {
            draft = new Draft(entry.Definition);
            drafts.Add(entry.Id, draft);
        }
        return draft;
    }

    private void Render(bool resetScroll = false, string message = null)
    {
        var scroll = resetScroll ? Vector2.zero : body.scrollOffset;
        body.Clear();
        var selected = Selected;
        if (selected != null)
            Draw(GetDraft(selected).Value, activeTab);
        body.scrollOffset = scroll;
        UpdateStatus(message);
    }

    private void UpdateStatus(string message = null)
    {
        var selected = Selected;
        var draft = selected == null ? null : GetDraft(selected);
        var modelErrors = draft == null ? Array.Empty<(int Tab, string Error)>() : ReadErrors(draft);
        var invalid = new HashSet<int>(modelErrors.Select(item => item.Tab));
        if (draft != null)
            invalid.UnionWith(draft.Invalid.Values.Select(item => item.Tab));
        for (int i = 0; i < tabs.Length; i++)
        {
            tabs[i].SetEnabled(draft != null);
            tabs[i].EnableInClassList("settings-tab-current", i == activeTab);
            SettingsFormStyle.SetTabError(tabs[i], invalid.Contains(i));
        }

        string error = draft?.Invalid.Count > 0 ? "Fix the highlighted fields before saving." :
            modelErrors.FirstOrDefault().Error;
        SettingsFormStyle.SetValidationMessage(status, error ?? message ??
            (draft == null ? "No entries are available." : draft.Dirty ? "Unsaved changes" : ""), error != null);
        save.SetEnabled(draft?.Dirty == true && error == null);
        revert.SetEnabled(draft?.Dirty == true);
    }

    private string ErrorFor(Draft draft)
    {
        if (draft.Invalid.Count > 0)
            return "Fix the highlighted fields before saving.";
        return ReadErrors(draft).FirstOrDefault().Error;
    }

    private (int Tab, string Error)[] ReadErrors(Draft draft)
    {
        try { return ValidationErrors(draft.Value).ToArray(); }
        catch (Exception ex) when (SeriesSettingsStore.IsSettingsError(ex)) { return new[] { (activeTab, ex.Message) }; }
    }

    private void SaveCurrent()
    {
        var selected = Selected;
        if (selected == null) return;
        var draft = GetDraft(selected);
        if (!draft.Dirty || ErrorFor(draft) != null) return;

        try
        {
            PrepareSave(draft.Value);
            selected.Save(draft.Value);
            ReloadAfterSave(selected.Id);
            Render(false, $"Saved {selected.Name}.");
        }
        catch (Exception ex) when (SeriesSettingsStore.IsSettingsError(ex))
        {
            SettingsFormStyle.SetValidationMessage(status, $"Could not save {selected.Name}. {ex.Message}", true);
        }
    }

    private void ReloadAfterSave(string id)
    {
        drafts.Remove(id);
        string selected = Selected?.Id;
        int index = Array.FindIndex(entries, entry => entry.Id == id);
        var updated = LoadEntries().FirstOrDefault(entry => entry.Id == id);
        if (index >= 0 && updated != null)
            entries[index] = updated;
        selector.choices = entries.Select(SelectorLabel).ToList();
        var current = entries.FirstOrDefault(entry => entry.Id == selected) ?? entries.FirstOrDefault();
        selector.SetValueWithoutNotify(current == null ? "" : SelectorLabel(current));
    }

    private Entry[] Unsaved() => entries.Where(entry => drafts.TryGetValue(entry.Id, out var draft) && draft.Dirty).ToArray();

    private void RequestBack()
    {
        if (Unsaved().Length == 0)
        {
            FinishBack();
            return;
        }
        RefreshWarning();
        warning.style.display = DisplayStyle.Flex;
        frame.SetEnabled(false);
        keepEditing.Focus();
    }

    private void RefreshWarning(string saveError = null)
    {
        var unsaved = Unsaved();
        warningText.text = $"Save or discard unsaved changes to {string.Join(", ", unsaved.Select(entry => entry.Name))}.";
        var error = BatchError(unsaved);
        saveBack.SetEnabled(error == null);
        SettingsFormStyle.SetValidationMessage(warningError, saveError ?? error, (saveError ?? error) != null);
    }

    private string BatchError(IEnumerable<Entry> unsaved)
    {
        foreach (var entry in unsaved)
        {
            var error = ErrorFor(drafts[entry.Id]);
            if (error != null) return $"{entry.Name}: {error}";
            try { PrepareSave(drafts[entry.Id].Value); }
            catch (Exception ex) when (SeriesSettingsStore.IsSettingsError(ex)) { return $"{entry.Name}: {ex.Message}"; }
        }
        return null;
    }

    private void SaveAll()
    {
        var unsaved = Unsaved();
        if (BatchError(unsaved) != null)
        {
            RefreshWarning();
            return;
        }

        foreach (var entry in unsaved)
        {
            try
            {
                entry.Save(drafts[entry.Id].Value);
                ReloadAfterSave(entry.Id);
            }
            catch (Exception ex) when (SeriesSettingsStore.IsSettingsError(ex))
            {
                RefreshWarning($"Could not save {entry.Name}. {ex.Message}");
                return;
            }
        }
        FinishBack();
    }

    private void CancelBack()
    {
        warning.style.display = DisplayStyle.None;
        frame.SetEnabled(true);
        Render();
        backButton.Focus();
    }

    private void FinishBack()
    {
        drafts.Clear();
        warning.style.display = DisplayStyle.None;
        frame.SetEnabled(true);
        Screen.style.display = DisplayStyle.None;
        back();
    }

    protected VisualElement Group(string name)
    {
        var group = new VisualElement();
        group.AddToClassList("settings-group");
        var heading = SettingsFormStyle.Label(name, "settings-group-heading");
        heading.AddToClassList("google-sans-semibold");
        group.Add(heading);
        body.Add(group);
        return group;
    }

    protected static VisualElement Row(VisualElement parent)
    {
        var row = new VisualElement();
        row.AddToClassList("settings-row");
        parent.Add(row);
        return row;
    }

    protected static void Note(VisualElement parent, string message) =>
        parent.Add(SettingsFormStyle.Label(message, "settings-note"));

    protected void Text(VisualElement parent, string label, string value, Action<string> set,
        bool optional = false, Func<string, string> validate = null) =>
        Edit(parent, label, value ?? "", text =>
            validate?.Invoke(text) ?? (optional && string.IsNullOrWhiteSpace(text) ? null : SettingsFormStyle.TextError(text)), set);

    protected void Integer(VisualElement parent, string label, int value, int minimum, int maximum, Action<int> set) =>
        Edit(parent, label, value.ToString(CultureInfo.CurrentCulture), text =>
            SettingsFormStyle.TextError(text) ?? (int.TryParse(text, out int number) && number >= minimum && number <= maximum
                ? null : $"Enter a whole number from {minimum} to {maximum}."), text => set(int.Parse(text)));

    protected void Number(VisualElement parent, string label, float value, float minimum, float maximum, Action<float> set) =>
        Edit(parent, label, value.ToString(CultureInfo.CurrentCulture), text =>
            SettingsFormStyle.TextError(text) ?? (float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out float number) &&
                number >= minimum && number <= maximum ? null : $"Enter a number from {minimum} to {maximum}."),
            text => set(float.Parse(text, NumberStyles.Float, CultureInfo.CurrentCulture)));

    private void Edit(VisualElement parent, string label, string value, Func<string, string> validate, Action<string> set)
    {
        var draft = GetDraft(Selected);
        bool invalid = draft.Invalid.TryGetValue(label, out var edit);
        int fieldTab = activeTab;
        Action<string> show = null;
        var field = SettingsFormStyle.Field(new TextField(label), invalid ? edit.Text : value, text =>
        {
            string error = validate(text);
            if (error == null)
            {
                draft.Invalid.Remove(label);
                set(text);
            }
            else
                draft.Invalid[label] = (text, error, fieldTab);
            show(error);
            Changed();
        });
        show = SettingsFormStyle.InlineError(field);
        string initialError = invalid ? edit.Error : validate(value);
        if (initialError != null)
            draft.Invalid[label] = (invalid ? edit.Text : value, initialError, fieldTab);
        show(initialError);
        parent.Add(field);
    }

    protected void Toggle(VisualElement parent, string label, bool value, Action<bool> set, bool redraw = false)
    {
        parent.Add(SettingsFormStyle.Field(new Toggle(label), value, enabled =>
        {
            set(enabled);
            Changed();
            if (redraw) Render();
        }));
    }

    protected void Choose(VisualElement parent, string label, string value, List<string> options, Action<string> set)
    {
        int index = Math.Max(0, options.IndexOf(value));
        parent.Add(SettingsFormStyle.Field(new DropdownField(label, options, index), options[index], text =>
        {
            set(text);
            Changed();
        }));
    }

    protected void Forget(params string[] labels)
    {
        var draft = GetDraft(Selected);
        foreach (string label in labels)
            draft.Invalid.Remove(label);
    }

    protected void Changed()
    {
        GetDraft(Selected).Dirty = true;
        UpdateStatus();
    }
}

