using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

internal static class SetupPresetDialog
{
    public static void Show(VisualElement host, string trackName, IReadOnlyList<TrackSetup> presets, string currentName,
        Action<TrackSetup> load, Func<string, string> save, string overwriteName = null)
    {
        bool saving = save != null;
        if (host.Q(className: "setup-preset-overlay") != null) return;
        var previouslyFocused = host.focusController?.focusedElement as VisualElement;
        var enabledChildren = host.Children().Where(child => child.enabledSelf).ToArray();
        var overlay = new VisualElement();
        overlay.AddToClassList("settings-confirmation-overlay");
        overlay.AddToClassList("setup-preset-overlay");
        overlay.AddToClassList("ui-default-font");
        var dialog = new VisualElement();
        dialog.AddToClassList("settings-confirmation-dialog");
        dialog.AddToClassList("setup-preset-dialog");
        overlay.Add(dialog);
        foreach (var child in enabledChildren) child.SetEnabled(false);
        host.Add(overlay);

        void Close() => overlay.RemoveFromHierarchy();
        overlay.RegisterCallback<DetachFromPanelEvent>(evt =>
        {
            if (evt.target != overlay) return;
            foreach (var child in enabledChildren) child.SetEnabled(true);
            previouslyFocused?.Focus();
        });
        overlay.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Escape) return;
            Close();
            evt.StopImmediatePropagation();
        }, TrickleDown.TrickleDown);

        var heading = SettingsFormStyle.Label("", "settings-group-heading");
        heading.style.whiteSpace = WhiteSpace.Normal;
        var status = SettingsFormStyle.Label("", "settings-note");
        var nameField = SettingsFormStyle.StyleField(new TextField("Setup name")
        {
            value = currentName ?? string.Empty, maxLength = 80, tooltip = "Enter a name for this setup preset."
        });

        if (overwriteName == null)
            ShowList();
        else
            ConfirmOverwrite(overwriteName, null);

        void ShowList()
        {
            dialog.Clear();
            heading.text = $"{(saving ? "Save Setup As" : "Load Setup")}  ·  {trackName}";
            dialog.Add(heading);
            if (saving)
                dialog.Add(nameField);

            if (presets.Count == 0)
                dialog.Add(SettingsFormStyle.Label("No setups saved for this track.", "settings-note"));
            else
            {
                var list = new ScrollView
                {
                    verticalScrollerVisibility = ScrollerVisibility.Hidden,
                    horizontalScrollerVisibility = ScrollerVisibility.Hidden
                };
                list.AddToClassList("setup-preset-list");
                foreach (var preset in presets.OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase))
                {
                    var entry = Button(saving && preset.IsDefault ? $"{preset.DisplayName} (read-only)" : preset.DisplayName, () =>
                    {
                        if (saving)
                        {
                            nameField.value = preset.DisplayName;
                            nameField.Focus();
                            return;
                        }
                        load(preset);
                        Close();
                    });
                    entry.tooltip = saving ? $"Use the name {preset.DisplayName}." : $"Load the {preset.DisplayName} setup.";
                    entry.SetEnabled(!saving || !preset.IsDefault);
                    list.Add(entry);
                }
                dialog.Add(list);
            }

            SetStatus("");
            dialog.Add(status);
            var cancel = Button("Cancel", Close);
            AddActions(cancel, saving ? Button("Save", RequestSave, primary: true) : null);
            if (saving)
                nameField.Focus();
            else
                cancel.Focus();
        }

        void RequestSave()
        {
            string name = nameField.value.Trim();
            if (name.Length == 0)
            {
                SetStatus("Enter a setup name.", error: true);
                return;
            }
            if (TrackSetup.IsDefaultName(name))
            {
                SetStatus("Default is read-only. Enter a new setup name.", error: true);
                return;
            }

            var existing = presets.FirstOrDefault(item => item.Matches(name));
            if (existing == null)
            {
                Commit(name);
                return;
            }

            ConfirmOverwrite(existing.DisplayName, ShowList);
        }

        void ConfirmOverwrite(string name, Action back)
        {
            dialog.Clear();
            heading.text = $"Overwrite “{name}”?";
            dialog.Add(heading);
            SetStatus("The saved settings and notes will be replaced.");
            dialog.Add(status);
            var cancel = Button(back == null ? "Cancel" : "Back", back ?? Close);
            AddActions(cancel, Button("Overwrite", () => Commit(name), primary: true));
            cancel.Focus();
        }

        void Commit(string name)
        {
            string error = save(name);
            if (error == null)
                Close();
            else
                SetStatus(error, error: true);
        }

        void SetStatus(string message, bool error = false)
        {
            status.text = message;
            status.style.color = (Color)(error ? new Color32(255, 133, 133, 255) : new Color32(245, 245, 245, 255));
        }

        void AddActions(Button first, Button second)
        {
            var actions = new VisualElement();
            actions.AddToClassList("settings-footer");
            actions.AddToClassList("settings-confirmation-actions");
            actions.Add(first);
            if (second != null)
                actions.Add(second);
            dialog.Add(actions);
        }
    }

    private static Button Button(string label, Action action, bool primary = false)
    {
        var button = SettingsFormStyle.Button(label, action, primary);
        button.tooltip = label switch
        {
            "Save" => "Save the current setup with this name.",
            "Overwrite" => "Replace the existing saved setup.",
            "Back" => "Return to the setup preset list.",
            "Cancel" => "Close without changes.",
            _ => label
        };
        return button;
    }
}
