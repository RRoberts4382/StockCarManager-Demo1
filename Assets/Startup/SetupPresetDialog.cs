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
        var overlay = new VisualElement();
        overlay.AddToClassList("skip-race-overlay");
        overlay.AddToClassList("setup-preset-overlay");
        overlay.style.display = DisplayStyle.Flex;
        var dialog = new VisualElement();
        dialog.AddToClassList("garage-entry");
        dialog.AddToClassList("skip-race-dialog");
        overlay.Add(dialog);
        host.Add(overlay);
        overlay.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Escape)
                return;
            overlay.RemoveFromHierarchy();
            evt.StopPropagation();
        });

        var heading = new Label();
        heading.AddToClassList("schedule-heading");
        heading.style.whiteSpace = WhiteSpace.Normal;
        var status = new Label { style = { whiteSpace = WhiteSpace.Normal } };
        var nameField = new TextField("Setup name") { value = currentName ?? string.Empty, maxLength = 80 };
        nameField.AddToClassList("setup-preset-name");

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
                dialog.Add(new Label("No setups saved for this track."));
            else
            {
                var list = new ScrollView();
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
                        overlay.RemoveFromHierarchy();
                    });
                    entry.SetEnabled(!saving || !preset.IsDefault);
                    list.Add(entry);
                }
                dialog.Add(list);
            }

            status.text = string.Empty;
            dialog.Add(status);
            var cancel = Button("Cancel", () => overlay.RemoveFromHierarchy());
            AddActions(cancel, saving ? Button("Save", RequestSave) : null);
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
                status.text = "Enter a setup name.";
                return;
            }
            if (TrackSetup.IsDefaultName(name))
            {
                status.text = "Default is read-only. Enter a new setup name.";
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
            status.text = "The saved settings and notes will be replaced.";
            dialog.Add(status);
            var cancel = Button(back == null ? "Cancel" : "Back", back ?? (() => overlay.RemoveFromHierarchy()));
            AddActions(cancel, Button("Overwrite", () => Commit(name)));
            cancel.Focus();
        }

        void Commit(string name)
        {
            string error = save(name);
            if (error == null)
                overlay.RemoveFromHierarchy();
            else
                status.text = error;
        }

        void AddActions(Button first, Button second)
        {
            var actions = new VisualElement();
            actions.AddToClassList("actions");
            actions.Add(first);
            if (second != null)
                actions.Add(second);
            dialog.Add(actions);
        }
    }

    private static Button Button(string label, Action action)
    {
        var button = new Button(action) { text = label };
        button.AddToClassList("nav-button");
        return button;
    }
}
