using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

internal static class RivalRosterSettings
{
    public static void Open(VisualElement settings, Button source, Func<RacingSeries, System.Collections.Generic.IEnumerable<int>> playerNumbers)
    {
        var thumbnails = new CarThumbnails(256);
        int previewGeneration = 0;
        var screen = new VisualElement();
        SettingsFormStyle.Apply(screen);
        var startupStyles = Resources.Load<StyleSheet>("Startup/Startup");
        if (startupStyles != null)
            screen.styleSheets.Insert(0, startupStyles);
        var frame = new VisualElement();
        frame.AddToClassList("settings-frame");
        screen.Add(frame);
        var header = new VisualElement();
        header.AddToClassList("settings-header");
        header.Add(SettingsFormStyle.Label("Rivals", "settings-title"));
        frame.Add(header);

        var selectors = new VisualElement();
        selectors.AddToClassList("rival-roster-selectors");
        frame.Add(selectors);
        var list = new ScrollView { verticalScrollerVisibility = ScrollerVisibility.Hidden, horizontalScrollerVisibility = ScrollerVisibility.Hidden };
        list.AddToClassList("settings-scroll");
        frame.Add(list);
        SettingsFormStyle.CheckSelectorListSpacing(selectors, list);

        var footer = new VisualElement();
        footer.AddToClassList("settings-footer");
        frame.Add(footer);
        var back = SettingsFormStyle.Button("Back", Close);
        back.tooltip = "Return to Settings.";
        footer.Add(back);
        var importStatus = SettingsFormStyle.Label("", "settings-status");
        footer.Add(importStatus);

        RacingSeries selectedSeries = null;
        var import = SettingsFormStyle.Button("", null);
        import.tooltip = "Import a CSV for the selected series.";
        import.AddToClassList("rival-roster-file-button");
        import.Add(CreateImportIcon());
        var help = SettingsFormStyle.Button("?", null);
        help.tooltip = "Read CSV import instructions and manufacturer IDs.";
        help.AddToClassList("rival-roster-file-button");
        var fileActions = new VisualElement();
        fileActions.AddToClassList("rival-roster-file-actions");
        fileActions.style.display = DisplayStyle.None;
        fileActions.Add(import);
        fileActions.Add(help);
        import.clicked += ImportCsv;
        help.clicked += ShowImportHelp;
        var series = RacingSeries.Catalog;
        var seriesButton = SettingsFormStyle.Button("Choose a series", null);
        seriesButton.tooltip = "Choose the racing series whose rivals you want to edit.";
        AddSelector("Series", seriesButton);
        selectors.Add(fileActions);
        seriesButton.clicked += () => Pick("Choose a series", seriesButton, series, item => item.name, SelectSeries);
        frame.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Escape)
                return;
            Close();
            evt.StopPropagation();
        });
        settings.parent.Add(screen);
        settings.SetEnabled(false);
        seriesButton.Focus();

        VisualElement AddSelector(string label, Button button)
        {
            var field = new VisualElement();
            field.AddToClassList("settings-field");
            field.AddToClassList("rival-roster-selector");
            field.Add(SettingsFormStyle.Label(label, "settings-note"));
            field.Add(button);
            selectors.Add(field);
            return field;
        }

        void SelectSeries(RacingSeries selected)
        {
            seriesButton.text = selected.name;
            selectedSeries = selected;
            fileActions.style.display = DisplayStyle.Flex;
            import.SetEnabled(false);
            SettingsFormStyle.SetValidationMessage(importStatus, null, false);
            previewGeneration++;
            list.Clear();
            thumbnails.Dispose();
            try
            {
                DisplayRoster(RivalRosterStore.Open(selected));
                import.SetEnabled(true);
            }
            catch (Exception ex) when (SeriesSettingsStore.IsSettingsError(ex))
            {
                ShowMessage("Could not open the shared rival roster. " + ex.Message, true);
            }
        }

        void ImportCsv()
        {
            if (selectedSeries == null)
                return;

            Career draft;
            string[] importedNumbers;
            string[] renumbered;
            string[] conflicts;
            try
            {
                var path = TrackImageFilePicker.Pick("Choose a rival roster CSV", "", "CSV files", "csv");
                if (string.IsNullOrEmpty(path))
                    return;
                (draft, importedNumbers, renumbered) = RivalRosterCsv.Read(selectedSeries, path);
                conflicts = importedNumbers.Intersect(playerNumbers(selectedSeries).Select(number =>
                    number.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray();
            }
            catch (Exception ex) when (ex is InvalidDataException || SeriesSettingsStore.IsSettingsError(ex))
            {
                SettingsFormStyle.SetValidationMessage(importStatus, "Import failed. " + ex.Message, true);
                return;
            }

            SettingsFormStyle.SetValidationMessage(importStatus, null, false);
            var overlay = new VisualElement();
            overlay.AddToClassList("settings-confirmation-overlay");
            var dialog = CareerSetup.CreateDialog(overlay, "Import Rival Roster");
            dialog.AddToClassList("rival-roster-dialog");
            int count = importedNumbers.Length;
            var description = count == 1 ? "Replace the first rival in " : "Replace the first " + count + " rivals in ";
            dialog.Add(SettingsFormStyle.Label(description + selectedSeries.name + " for all careers in this series?", "settings-note"));
            if (renumbered.Length > 0)
            {
                string examples = string.Join(", ", renumbered.Take(3));
                string more = renumbered.Length > 3 ? " and " + (renumbered.Length - 3) + " more" : "";
                var changes = SettingsFormStyle.Label("Duplicate car numbers adjusted: " + examples + more + ".", "settings-note");
                changes.AddToClassList("rival-roster-help-copy");
                dialog.Add(changes);
            }
            if (conflicts.Length > 0)
            {
                var warning = SettingsFormStyle.Label("", "settings-note");
                warning.AddToClassList("rival-roster-help-copy");
                string numbers = string.Join(", ", conflicts.Select(number => "#" + number));
                SettingsFormStyle.SetValidationMessage(warning,
                    "Player car number match: " + numbers + ". You can still import. In those careers, the race grid assigns the rival a different number when the player uses it.", true);
                dialog.Add(warning);
            }
            var status = SettingsFormStyle.Label("", "settings-validation-summary");
            dialog.Add(status);
            var actions = new VisualElement();
            actions.AddToClassList("settings-footer");
            actions.AddToClassList("settings-confirmation-actions");
            dialog.Add(actions);
            var cancel = SettingsFormStyle.Button("Cancel", Dismiss);
            cancel.tooltip = "Close the preview without importing the roster.";
            actions.Add(cancel);
            var confirm = SettingsFormStyle.Button("Import", Commit, primary: true);
            confirm.tooltip = "Save the validated CSV changes to the selected series.";
            actions.Add(confirm);
            overlay.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape)
                    return;
                Dismiss();
                evt.StopPropagation();
            });
            frame.SetEnabled(false);
            screen.Add(overlay);
            cancel.Focus();

            void Commit()
            {
                try
                {
                    RivalRosterStore.Save(draft);
                }
                catch (Exception ex) when (SeriesSettingsStore.IsSettingsError(ex))
                {
                    SettingsFormStyle.SetValidationMessage(status, "Could not save the roster. " + ex.Message, true);
                    return;
                }
                Dismiss();

                DisplayRoster(draft);
                SettingsFormStyle.SetValidationMessage(importStatus, count == 1 ? "Imported 1 rival." : "Imported " + count + " rivals.", false);
            }

            void Dismiss()
            {
                overlay.RemoveFromHierarchy();
                frame.SetEnabled(true);
                import.Focus();
            }
        }

        void ShowImportHelp()
        {
            var overlay = new VisualElement();
            overlay.AddToClassList("settings-confirmation-overlay");
            var dialog = CareerSetup.CreateDialog(overlay, "Rival CSV Import Help");
            dialog.AddToClassList("rival-roster-dialog");
            int slots = TrackPreview.CarCount - 1;
            var instructions = string.Join("\n\n", new[]
            {
                "Select a series, click the import icon, and choose a UTF-8 CSV. Confirm the import to save it for all careers in that series.",
                "Use this header and column order:\n" + RivalRosterCsv.HeaderText + "\n7,1,Alex Morgan,Redline Racing",
                "Rows replace rival slots from the first slot onward, in file order. Smaller files leave the remaining slots unchanged. The file limit is " +
                    RivalRosterCsv.MaximumRows + " rows. This series currently has " + slots + " rival slots.",
                "Use one to four letters or digits, such as 00 or 88B. The second use of a number gets X and the third gets Y. Import uses another free suffix if needed. Letters are saved in uppercase. Leading zeroes are kept, so 01 and 1 are different numbers. Put names containing commas in double quotes. A match with a player car number shows a warning and does not prevent import.",
                "Manufacturer IDs: 0 Chevrolet, 1 Ford, 2 Toyota, 3 Dodge. Leave the field blank to use Chevrolet (0). Use only manufacturers configured for the selected series."
            });
            var copy = SettingsFormStyle.Label(instructions, "settings-note");
            copy.AddToClassList("rival-roster-help-copy");
            copy.focusable = true;
            copy.selection.isSelectable = true;
            dialog.Add(copy);

            var actions = new VisualElement();
            actions.AddToClassList("settings-footer");
            actions.AddToClassList("settings-confirmation-actions");
            dialog.Add(actions);
            var close = SettingsFormStyle.Button("Close", Dismiss);
            close.tooltip = "Return to the rival roster.";
            actions.Add(close);
            overlay.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape)
                    return;
                Dismiss();
                evt.StopPropagation();
            });
            frame.SetEnabled(false);
            screen.Add(overlay);
            close.Focus();

            void Dismiss()
            {
                overlay.RemoveFromHierarchy();
                frame.SetEnabled(true);
                help.Focus();
            }
        }

        void DisplayRoster(Career career, string focusId = null)
        {
            var scroll = focusId == null ? Vector2.zero : list.scrollOffset;
            list.Clear();
            Button focus = null;
            var previews = new System.Collections.Generic.List<(Image image, SessionCarResult rival)>();
            var roster = TrackPreview.CreateTimingEntries(career.cars[0], career)
                .Where(entry => !entry.playerEntry && entry.driver != null && entry.car != null)
                .OrderBy(entry => int.TryParse(entry.DisplayCarNumber, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out int numeric) ? numeric : int.MaxValue)
                .ThenBy(entry => entry.DisplayCarNumber, StringComparer.Ordinal);
            foreach (var rival in roster)
            {
                var row = new VisualElement();
                row.AddToClassList("rival-roster-row");
                var number = SettingsFormStyle.Label("#" + rival.DisplayCarNumber, "rival-roster-number");
                CarNumberFonts.Apply(number, rival.car.PaintStyle);
                row.Add(number);
                var preview = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                preview.AddToClassList("rival-roster-car");
                row.Add(preview);
                previews.Add((preview, rival));
                row.Add(CreateManufacturerBadge(rival.manufacturer));
                row.Add(SettingsFormStyle.Label(rival.driverName, "rival-roster-name"));
                row.Add(SettingsFormStyle.Label(rival.teamName, "rival-roster-team"));
                var edit = SettingsFormStyle.Button("", () =>
                {
                    RivalDriverEditor.Open(screen, career, rival, () => RivalRosterStore.Save(career),
                        () => DisplayRoster(career, rival.rivalDriverId));
                });
                edit.AddToClassList("rival-roster-edit");
                edit.tooltip = "Edit " + rival.driverName + "'s driver info and paint scheme.";
                edit.Add(CreateEditIcon());
                row.Add(edit);
                list.Add(row);
                if (focusId == rival.rivalDriverId)
                    focus = edit;
            }
            if (list.contentContainer.childCount == 0)
                ShowMessage("This series has no rival drivers to edit.");
            list.schedule.Execute(() =>
            {
                focus?.Focus();
                list.scrollOffset = scroll;
            });
            _ = PopulatePreviews(previews, ++previewGeneration);
        }

        async Awaitable PopulatePreviews(System.Collections.Generic.List<(Image image, SessionCarResult rival)> previews, int generation)
        {
            try
            {
                foreach (var (image, rival) in previews)
                {
                    await Awaitable.NextFrameAsync();
                    if (generation != previewGeneration || screen.panel == null)
                        return;
                    image.image = thumbnails.Get(rival.car, Array.Empty<string>(), transparentBackground: true,
                        retainOrbitScene: false, cacheScope: rival.rivalDriverId, displayNumber: rival.carNumber, displayNumberText: rival.DisplayCarNumber);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Could not render a rival car preview. " + ex.Message);
            }
        }

        void Pick<T>(string title, Button opener, System.Collections.Generic.IReadOnlyList<T> items,
            Func<T, string> label, Action<T> choose)
        {
            var overlay = new VisualElement();
            overlay.AddToClassList("settings-confirmation-overlay");
            var dialog = CareerSetup.CreateDialog(overlay, title);
            dialog.AddToClassList("rival-roster-dialog");
            var options = new ScrollView { verticalScrollerVisibility = ScrollerVisibility.Hidden, horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            dialog.Add(options);
            foreach (var item in items)
            {
                var text = label(item);
                var option = SettingsFormStyle.Button(text, () =>
                {
                    Dismiss();
                    choose(item);
                });
                option.AddToClassList("rival-manufacturer-choice");
                option.tooltip = "Select " + text + ".";
                options.Add(option);
            }
            var actions = new VisualElement();
            actions.AddToClassList("settings-footer");
            actions.AddToClassList("settings-confirmation-actions");
            dialog.Add(actions);
            var cancel = SettingsFormStyle.Button("Cancel", Dismiss);
            cancel.tooltip = "Keep the current selection.";
            actions.Add(cancel);
            overlay.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape)
                    return;
                Dismiss();
                evt.StopPropagation();
            });
            frame.SetEnabled(false);
            screen.Add(overlay);
            cancel.Focus();

            void Dismiss()
            {
                overlay.RemoveFromHierarchy();
                frame.SetEnabled(true);
                opener.Focus();
            }
        }

        void ShowMessage(string message, bool error = false)
        {
            var label = SettingsFormStyle.Label("", "settings-note");
            SettingsFormStyle.SetValidationMessage(label, message, error);
            list.Add(label);
        }

        void Close()
        {
            previewGeneration++;
            screen.RemoveFromHierarchy();
            thumbnails.Dispose();
            settings.SetEnabled(true);
            source.Focus();
        }
    }

    private static VisualElement CreateManufacturerBadge(int make)
    {
        // Use original placeholder marks until manufacturer artwork is available.
        var (letters, color, shape) = make switch
        {
            ManufacturerCatalog.Chevrolet => ("C", new Color32(218, 166, 65, 255), "amber"),
            ManufacturerCatalog.Ford => ("F", new Color32(66, 126, 214, 255), "blue"),
            ManufacturerCatalog.Toyota => ("T", new Color32(206, 66, 82, 255), "red"),
            ManufacturerCatalog.Dodge => ("D", new Color32(148, 108, 207, 255), "purple"),
            _ => ("?", new Color32(123, 139, 156, 255), "neutral")
        };
        var column = new VisualElement { tooltip = ManufacturerCatalog.Name(make) };
        column.AddToClassList("rival-roster-make");
        var logo = ManufacturerCatalog.Logo(make);
        if (logo != null)
        {
            var image = new Image { image = logo, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            image.style.width = 27;
            image.style.height = 20;
            column.Add(image);
            return column;
        }
        var badge = SettingsFormStyle.Label(letters, "rival-roster-badge");
        badge.AddToClassList("rival-roster-badge-" + shape);
        badge.style.backgroundColor = (Color)color;
        badge.pickingMode = PickingMode.Ignore;
        column.Add(badge);
        return column;
    }

    private static VisualElement CreateImportIcon()
    {
        var icon = new VisualElement { pickingMode = PickingMode.Ignore };
        icon.AddToClassList("rival-roster-edit-icon");
        icon.generateVisualContent += context =>
        {
            var painter = context.painter2D;
            painter.strokeColor = context.visualElement.resolvedStyle.color;
            painter.lineWidth = 1.5f;
            painter.lineJoin = LineJoin.Round;
            painter.BeginPath();
            painter.MoveTo(new Vector2(9, 2));
            painter.LineTo(new Vector2(9, 11));
            painter.MoveTo(new Vector2(5, 7));
            painter.LineTo(new Vector2(9, 11));
            painter.LineTo(new Vector2(13, 7));
            painter.MoveTo(new Vector2(3, 11));
            painter.LineTo(new Vector2(3, 15));
            painter.LineTo(new Vector2(15, 15));
            painter.LineTo(new Vector2(15, 11));
            painter.Stroke();
        };
        return icon;
    }

    private static VisualElement CreateEditIcon()
    {
        var icon = new VisualElement { pickingMode = PickingMode.Ignore };
        icon.AddToClassList("rival-roster-edit-icon");
        icon.generateVisualContent += context =>
        {
            var painter = context.painter2D;
            painter.strokeColor = context.visualElement.resolvedStyle.color;
            painter.lineWidth = 1.5f;
            painter.lineJoin = LineJoin.Round;
            painter.BeginPath();
            painter.MoveTo(new Vector2(3, 15));
            painter.LineTo(new Vector2(4, 10));
            painter.LineTo(new Vector2(12, 2));
            painter.LineTo(new Vector2(16, 6));
            painter.LineTo(new Vector2(8, 14));
            painter.ClosePath();
            painter.MoveTo(new Vector2(10, 4));
            painter.LineTo(new Vector2(14, 8));
            painter.Stroke();
        };
        return icon;
    }
}
