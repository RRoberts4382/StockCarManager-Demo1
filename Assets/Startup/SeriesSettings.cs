using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

internal sealed class SeriesSettings
{
    private static readonly string[] Tabs = { "General", "Manufacturers & engines", "Staff", "Costs & sponsorship", "Artwork", "Car Models" };
    private static readonly (string View, string Label)[] Backgrounds =
    {
        ("setup", "Career setup"), ("homePage", "Team home"), ("mailPage", "Mail"),
        ("carsPage", "Cars"), ("shopPage", "Shop"), ("employeesPage", "Employees"),
        ("schedulePage", "Schedule"), ("resultsPage", "Results"), ("financesPage", "Finances")
    };

    private readonly RacingSeries[] series = RacingSeries.Catalog;
    private readonly Dictionary<string, Draft> drafts = new();
    private readonly List<Action> refreshNumericErrors = new();
    private readonly DropdownField selection;
    private readonly ScrollView body = new();
    private readonly VisualElement frame = new();
    private readonly Button[] tabs;
    private readonly Button save;
    private readonly Button revert;
    private readonly Label status;
    private int tab;
    private Draft Current => selection.index >= 0 && selection.index < series.Length
        ? GetDraft(series[selection.index]) : null;

    public VisualElement Screen { get; } = new VisualElement();

    public SeriesSettings(Action back)
    {
        SettingsFormStyle.Apply(Screen);
        frame.AddToClassList("settings-frame");
        Screen.Add(frame);
        var header = new VisualElement();
        header.AddToClassList("settings-header");
        var title = SettingsFormStyle.Label("Series Settings", "settings-title");
        title.AddToClassList("google-sans-bold");
        header.Add(title);

        var names = series.Select(item => item.name).ToList();
        selection = SettingsFormStyle.Field(new DropdownField("Racing series", names, names.Count > 0 ? 0 : -1),
            names.FirstOrDefault(), _ => Render(resetScroll: true));
        selection.AddToClassList("settings-series-selector");
        selection.SetEnabled(series.Length > 0);
        header.Add(selection);
        frame.Add(header);

        var nav = new VisualElement();
        nav.AddToClassList("settings-tabs");
        tabs = Tabs.Select((name, index) => SettingsFormStyle.Tab(name, () =>
        {
            tab = index;
            Render(resetScroll: true);
        })).ToArray();
        tabs[5].name = "seriesCarModelsTab";
        foreach (var button in tabs)
            nav.Add(button);
        frame.Add(nav);
        body.AddToClassList("settings-scroll");
        frame.Add(body);

        var footer = new VisualElement();
        footer.AddToClassList("settings-footer");
        status = SettingsFormStyle.Label(string.Empty, "settings-status");
        footer.Add(status);
        var warning = new VisualElement();
        warning.AddToClassList("settings-confirmation-overlay");
        warning.style.display = DisplayStyle.None;
        var dialog = new VisualElement();
        dialog.AddToClassList("settings-confirmation-dialog");
        var heading = SettingsFormStyle.Label("Unsaved changes", "settings-group-heading");
        heading.AddToClassList("google-sans-semibold");
        dialog.Add(heading);
        var warningText = SettingsFormStyle.Label(string.Empty, "settings-note");
        dialog.Add(warningText);
        var warningError = SettingsFormStyle.Label(string.Empty, "settings-validation-summary");
        dialog.Add(warningError);
        var actions = Row(dialog);
        actions.AddToClassList("settings-confirmation-actions");
        warning.Add(dialog);
        Screen.Add(warning);

        var backButton = SettingsFormStyle.Button("Back", null);
        footer.Add(backButton);
        var keepEditing = SettingsFormStyle.Button("Keep Editing", CancelBack);
        var saveAndBack = SettingsFormStyle.Button("Save and Go Back", null, primary: true);
        backButton.clicked += RequestBack;
        saveAndBack.clicked += SaveAndBack;
        actions.Add(keepEditing);
        actions.Add(SettingsFormStyle.Button("Discard and Go Back", () =>
        {
            drafts.Clear();
            FinishBack();
        }));
        actions.Add(saveAndBack);
        warning.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Escape)
                return;
            CancelBack();
            evt.StopPropagation();
        });

        void RequestBack()
        {
            var unsaved = DirtySeries();
            if (unsaved.Length == 0)
            {
                FinishBack();
                return;
            }

            warningText.text = $"Save or discard unsaved changes to {string.Join(", ", unsaved.Select(item => item.name))}.";
            RefreshWarning(unsaved);
            frame.SetEnabled(false);
            warning.style.display = DisplayStyle.Flex;
            keepEditing.Focus();
        }

        void RefreshWarning(RacingSeries[] unsaved, string saveError = null)
        {
            bool ready = TryPrepareBatch(unsaved, out _, out string validationError);
            saveAndBack.SetEnabled(ready);
            string message = saveError ?? validationError;
            SettingsFormStyle.SetValidationMessage(warningError, message, message != null);
        }

        void SaveAndBack()
        {
            var unsaved = DirtySeries();
            if (!TryPrepareBatch(unsaved, out var batch, out string validationError))
            {
                saveAndBack.SetEnabled(false);
                SettingsFormStyle.SetValidationMessage(warningError, validationError, true);
                return;
            }

            foreach (var (original, rules) in batch)
            {
                try
                {
                    SeriesSettingsStore.Save(rules, original);
                    drafts.Remove(original.id);
                }
                catch (Exception ex) when (SeriesSettingsStore.IsSettingsError(ex))
                {
                    var remaining = DirtySeries();
                    warningText.text = $"Unsaved changes remain for {string.Join(", ", remaining.Select(item => item.name))}.";
                    RefreshWarning(remaining, $"Could not save {original.name}. {ex.Message}");
                    return;
                }
            }
            FinishBack();
        }

        void CancelBack()
        {
            warning.style.display = DisplayStyle.None;
            frame.SetEnabled(true);
            Render();
            backButton.Focus();
        }

        void FinishBack()
        {
            warning.style.display = DisplayStyle.None;
            frame.SetEnabled(true);
            Screen.style.display = DisplayStyle.None;
            back();
        }
        revert = SettingsFormStyle.Button("Revert", Revert);
        footer.Add(revert);
        save = SettingsFormStyle.Button("Save Series", Save, primary: true);
        footer.Add(save);
        frame.Add(footer);
    }

    public void Open()
    {
        Render(resetScroll: true);
        selection.Focus();
    }

    private RacingSeries[] DirtySeries() =>
        series.Where(item => drafts.TryGetValue(item.id, out var draft) && draft.Dirty).ToArray();

    private bool TryPrepareBatch(RacingSeries[] unsaved,
        out List<(RacingSeries Original, RacingSeries Rules)> batch, out string error)
    {
        batch = new List<(RacingSeries Original, RacingSeries Rules)>(unsaved.Length);
        foreach (var original in unsaved)
        {
            try
            {
                var draft = drafts[original.id];
                if (InvalidTabs(draft).Any())
                    throw new ArgumentException("Fix the highlighted fields.");
                var rules = PrepareRules(draft);
                string validationError = SeriesSettingsStore.Validate(rules);
                if (validationError != null)
                    throw new ArgumentException(validationError);
                SeriesSettingsStore.ValidateArtwork(rules);
                batch.Add((original, rules));
            }
            catch (Exception ex) when (SeriesSettingsStore.IsSettingsError(ex))
            {
                error = $"{original.name}: {ex.Message}";
                return false;
            }
        }
        error = null;
        return true;
    }

    private Draft GetDraft(RacingSeries series)
    {
        if (!drafts.TryGetValue(series.id, out var draft))
        {
            draft = new Draft(series);
            drafts.Add(series.id, draft);
        }
        return draft;
    }

    private void Render(bool resetScroll = false, string message = null)
    {
        var scroll = resetScroll ? Vector2.zero : body.scrollOffset;
        body.Clear();
        refreshNumericErrors.Clear();
        var draft = Current;
        for (int i = 0; i < tabs.Length; i++)
        {
            tabs[i].SetEnabled(draft != null);
            tabs[i].EnableInClassList("settings-tab-current", i == tab);
        }
        if (draft == null)
        {
            SetStatus("No racing series are available.");
            return;
        }
        switch (tab)
        {
            case 0: DrawGeneral(draft); break;
            case 1: DrawManufacturers(draft); break;
            case 2: DrawStaff(draft); break;
            case 3: DrawFinances(draft); break;
            case 4: DrawArtwork(draft); break;
            case 5: DrawCarModels(draft); break;
        }
        body.scrollOffset = scroll;
        SetStatus(message ?? (draft.Dirty ? "Unsaved changes" : string.Empty));
    }

    private void DrawGeneral(Draft draft)
    {
        var rules = draft.Rules;
        var section = Group("Race format");
        var stageRow = Row(section);
        var stageFields = new TextField[ScheduledRace.MaximumStages];
        var stageTotal = SettingsFormStyle.Label(string.Empty, "settings-note");
        stageTotal.name = "stagePercentageTotal";
        stageTotal.AddToClassList("settings-validation-summary");
        for (int i = 0; i < stageFields.Length; i++)
        {
            int stage = i;
            var field = Number(draft.StagePercentages, $"Stage {stage + 1} (%)", draft.StagePercentages[stage], value =>
            {
                draft.StagePercentages[stage] = value;
                if (stage == 0 && value == 100)
                {
                    for (int other = 1; other < stageFields.Length; other++)
                    {
                        stageFields[other].value = "0";
                    }
                }
                RefreshStageTotal();
            }, percent => percent >= 0 && percent <= 100 ? null : "Enter a percentage from 0 to 100.");
            stageFields[stage] = field;
            stageRow.Add(field);
        }
        section.Add(stageTotal);
        RefreshStageTotal();
        section.Add(SettingsFormStyle.Label(
            "Enter 100% in Stage 1 to clear Stages 2–4 and run the race without stages.",
            "settings-note"));

        void RefreshStageTotal()
        {
            long total = draft.StagePercentages.Sum(value => (long)value);
            SettingsFormStyle.SetValidationMessage(stageTotal,
                total == 100 ? "Total: 100%" : $"Total: {total}% · Must equal 100%", total != 100);
        }
        section.Add(Number(rules, "Maximum track length (miles)", rules.maximumTrackLengthMiles,
            x => rules.maximumTrackLengthMiles = x, TrackLengthError));
        section.Add(SettingsFormStyle.Label("Set the track limit to 0 to allow every track.", "settings-note"));

    }

    private void DrawCarModels(Draft draft)
    {
        var rules = draft.Rules;
        var choices = CarTypeDefinition.AvailableModels();
        var defaultChoices = new List<(string Model, string Name)>(choices);
        if (rules.id != "craftsman-truck")
            defaultChoices.Insert(0, ("", "Use Cup Impala"));

        var seriesRow = Row(Group("Series default"));
        string defaultModel = rules.carModel ?? string.Empty;
        AddSelector(seriesRow, "Series car model", "seriesDefaultCarModel", "Choose the series model",
            defaultChoices, defaultModel, "Impala", CarTypeDefinition.ForModel(defaultModel).displayName,
            model => rules.carModel = model);

        var manufacturerChoices = new List<(string Model, string Name)>(choices);
        manufacturerChoices.Insert(0, ("", "Use default model"));
        var modelsRow = Row(Group("Manufacturer models"));
        foreach (var make in rules.manufacturers)
        {
            string name = make.manufacturer.ToString();
            string currentName = CarTypeDefinition.ForModel(rules.ModelFor(make.manufacturer)).displayName;
            if (string.IsNullOrWhiteSpace(make.carModel))
                currentName += " (default)";
            AddSelector(modelsRow, name, "seriesCarModel" + name, $"Choose a model for {name}",
                manufacturerChoices, make.carModel ?? "", rules.DefaultModelFor(make.manufacturer), currentName,
                model => make.carModel = string.IsNullOrEmpty(model) ? null : model);
        }

        void AddSelector(VisualElement row, string title, string id, string dialogTitle,
            List<(string Model, string Name)> models, string current, string inheritedModel, string currentName,
            Action<string> select)
        {
            var field = new VisualElement();
            field.AddToClassList("settings-field");
            field.style.flexGrow = 0;
            field.style.minWidth = 225;
            field.Add(SettingsFormStyle.Label(title, "settings-note"));
            Button button = null;
            button = SettingsFormStyle.Button(currentName, () => OpenCarModelPicker(dialogTitle, models, current,
                inheritedModel, button, model =>
                {
                    if (model == current)
                        return;

                    select(model);
                    Changed();
                    Render();
                    Screen.Q<Button>(id)?.Focus();
                }));
            button.name = id;
            button.tooltip = dialogTitle + ".";
            field.Add(button);
            row.Add(field);
        }
    }

    private void OpenCarModelPicker(string title, List<(string Model, string Name)> models, string current,
        string inheritedModel, Button source, Action<string> select)
    {
        var overlay = new VisualElement { name = "seriesCarModelPicker" };
        overlay.AddToClassList("settings-confirmation-overlay");
        var dialog = new VisualElement();
        dialog.AddToClassList("settings-confirmation-dialog");
        dialog.style.width = 345;
        dialog.style.maxHeight = Length.Percent(90);
        var trim = (Color)new Color32(0, 155, 221, 255);
        dialog.style.backgroundColor = (Color)new Color32(16, 21, 30, 255);
        dialog.style.borderTopColor = dialog.style.borderRightColor =
            dialog.style.borderBottomColor = dialog.style.borderLeftColor = trim;
        dialog.Add(SettingsFormStyle.Label(title, "settings-group-heading"));

        var list = new ScrollView(ScrollViewMode.Vertical)
        {
            name = "seriesCarModelChoices",
            horizontalScrollerVisibility = ScrollerVisibility.Hidden,
            verticalScrollerVisibility = ScrollerVisibility.Hidden
        };
        list.style.maxHeight = 360;
        var thumbnails = new CarThumbnails(192);
        foreach (var option in models)
        {
            var choose = SettingsFormStyle.Button(string.Empty, () =>
            {
                Close();
                select(option.Model);
            });
            choose.AddToClassList("series-model-choice");
            choose.EnableInClassList("settings-tab-current", option.Model == current);
            choose.tooltip = $"Select {option.Name}.";
            choose.style.flexDirection = FlexDirection.Row;
            choose.style.alignItems = Align.Center;
            choose.style.justifyContent = Justify.FlexStart;
            choose.style.minHeight = 54;
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                string model = string.IsNullOrEmpty(option.Model) ? inheritedModel : option.Model;
                var previewCar = new TeamCar { number = 27, modelResource = model, primaryPaint = "#009BDD", secondaryPaint = "#F5F5F5" };
                var image = new Image
                {
                    image = thumbnails.Get(previewCar, Array.Empty<string>(), cacheScope: model,
                        transparentBackground: true, retainOrbitScene: false),
                    scaleMode = ScaleMode.ScaleToFit,
                    pickingMode = PickingMode.Ignore
                };
                image.style.width = 86;
                image.style.height = 46;
                image.style.flexShrink = 0;
                image.style.marginRight = 8;
                choose.Add(image);
            }
            var label = new Label(option.Name) { pickingMode = PickingMode.Ignore };
            label.style.flexShrink = 1;
            label.style.whiteSpace = WhiteSpace.Normal;
            choose.Add(label);
            list.Add(choose);
        }

        dialog.Add(list);
        var footer = new VisualElement();
        footer.AddToClassList("settings-footer");
        var cancel = SettingsFormStyle.Button("Cancel", Close);
        cancel.tooltip = "Close without changing the car model.";
        footer.Add(cancel);
        dialog.Add(footer);
        overlay.Add(dialog);
        Screen.Add(overlay);
        frame.SetEnabled(false);
        (list.Q<Button>(className: "settings-tab-current") ?? list.Q<Button>() ?? cancel).Focus();
        overlay.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Escape)
                return;
            Close();
            evt.StopPropagation();
        });

        void Close()
        {
            overlay.RemoveFromHierarchy();
            thumbnails.Dispose();
            frame.SetEnabled(true);
            source.Focus();
        }
    }

    private void DrawManufacturers(Draft draft)
    {
        var rules = draft.Rules;
        var selected = Group("Manufacturers");
        selected.Add(SettingsFormStyle.ErrorSlot(rules.manufacturers.Length == 0 ? "Choose at least one manufacturer." : null));
        var row = Row(selected);
        foreach (CarManufacturer make in Enum.GetValues(typeof(CarManufacturer)))
        {
            var existing = rules.manufacturers.FirstOrDefault(item => item.manufacturer == make);
            row.Add(Field(new Toggle(make.ToString()) { tooltip = $"Allow teams in this series to use {make} cars." }, existing != null, include =>
            {
                rules.manufacturers = include
                    ? rules.manufacturers.Append(new SeriesManufacturer { manufacturer = make, engineSuppliers = Array.Empty<SeriesEngine>() }).ToArray()
                    : rules.manufacturers.Where(item => item.manufacturer != make).ToArray();
                Render();
            }));
        }

        bool truck = rules.id == "craftsman-truck";
        var availability = Group("Engine supplier availability");
        var mode = Field(new Toggle("Share suppliers across manufacturers"), draft.SharedSuppliers, shared =>
        {
            draft.SharedSuppliers = shared;
            if (shared && rules.sharedEngineSuppliers.Length == 0)
            {
                var initial = rules.manufacturers.FirstOrDefault()?.engineSuppliers ?? Array.Empty<SeriesEngine>();
                rules.sharedEngineSuppliers = initial.Select(engine => new SeriesEngine
                {
                    name = engine.name, power = engine.power, reliability = engine.reliability,
                    maintenancePerRace = engine.maintenancePerRace
                }).ToArray();
            }
            Render();
        });
        mode.SetEnabled(!truck);
        availability.Add(mode);

        if (draft.SharedSuppliers)
            DrawSuppliers(Group("Shared suppliers"), rules.sharedEngineSuppliers, items => rules.sharedEngineSuppliers = items, truck);
        else
            foreach (var make in rules.manufacturers)
                DrawSuppliers(Group(make.manufacturer.ToString()), make.engineSuppliers ?? Array.Empty<SeriesEngine>(),
                    items => make.engineSuppliers = items, false);
    }

    private void DrawSuppliers(VisualElement group, SeriesEngine[] suppliers, Action<SeriesEngine[]> replace, bool single)
    {
        group.Add(SettingsFormStyle.ErrorSlot(suppliers.Length == 0 ? "Add at least one engine supplier." : null));
        foreach (var supplier in suppliers)
        {
            var row = Row(group);
            var name = RequiredText("Supplier", supplier.name, x => supplier.name = x);
            name.AddToClassList("settings-field-name");
            row.Add(name);
            row.Add(Number(supplier, "Power", supplier.power, x => supplier.power = x, EngineRatingError));
            row.Add(Number(supplier, "Reliability", supplier.reliability, x => supplier.reliability = x, EngineRatingError));
            row.Add(Number(supplier, "Maintenance per race ($)", supplier.maintenancePerRace, x => supplier.maintenancePerRace = x));
            if (!single)
                row.Add(Remove("Remove supplier", () => replace(suppliers.Where(item => item != supplier).ToArray())));
        }
        if (!single || suppliers.Length == 0)
        {
            var add = SettingsFormStyle.Button("Add supplier", () =>
            {
                replace(suppliers.Append(new SeriesEngine { name = "", power = 0.85f, reliability = 0.85f }).ToArray());
                Changed();
                Render();
            });
            group.Add(add);
        }
    }

    private void DrawStaff(Draft draft)
    {
        var rules = draft.Rules;
        var limits = Group("Roster");
        limits.Add(Number(rules, "Maximum staff, including driver", rules.maximumEmployees, x => rules.maximumEmployees = x, StaffLimitError));
        var roles = Group("Roles and annual salaries");
        foreach (EmployeeRole role in Enum.GetValues(typeof(EmployeeRole)))
        {
            var row = Row(roles);
            var selected = rules.employeeRoles.FirstOrDefault(item => item.role == role);
            var choice = Field(new Toggle(Employee.NameOf(role)) { tooltip = role == EmployeeRole.Driver
                ? "Every team in this series must have a driver."
                : $"Allow teams in this series to hire for the {Employee.NameOf(role)} role." }, selected != null, include =>
            {
                rules.employeeRoles = include
                    ? rules.employeeRoles.Append(new SeriesEmployeeRole { role = role }).ToArray()
                    : rules.employeeRoles.Where(item => item.role != role).ToArray();
                Render();
            });
            choice.SetEnabled(role != EmployeeRole.Driver);
            row.Add(choice);
            if (selected != null)
            {
                row.Add(Number(selected, "Minimum salary ($)", selected.minimumSalary, x => selected.minimumSalary = x,
                    value => MinimumError(value, selected.maximumSalary)));
                row.Add(Number(selected, "Maximum salary ($)", selected.maximumSalary, x => selected.maximumSalary = x,
                    value => MaximumError(selected.minimumSalary, value)));
            }
        }
    }

    private void DrawFinances(Draft draft)
    {
        var rules = draft.Rules;
        var costs = Row(Group("Chassis and components"));
        costs.Add(Number(rules, "New chassis ($)", rules.newChassisCost, x => rules.newChassisCost = x));
        costs.Add(Number(rules, "Component upgrade ($)", rules.componentUpgradeCost, x => rules.componentUpgradeCost = x));
        costs.Add(Number(rules, "Maximum component repair ($)", rules.maximumComponentRepairCost, x => rules.maximumComponentRepairCost = x));

        var sponsorship = Group("Sponsorship offers");
        var signing = Row(sponsorship);
        signing.Add(Number(rules, "Minimum signing payment ($)", rules.minimumSponsorshipSigning, x => rules.minimumSponsorshipSigning = x,
            value => MinimumError(value, rules.maximumSponsorshipSigning)));
        signing.Add(Number(rules, "Maximum signing payment ($)", rules.maximumSponsorshipSigning, x => rules.maximumSponsorshipSigning = x,
            value => MaximumError(rules.minimumSponsorshipSigning, value)));
        var perRace = Row(sponsorship);
        perRace.Add(Number(rules, "Minimum payment per race ($)", rules.minimumSponsorshipPerRace, x => rules.minimumSponsorshipPerRace = x,
            value => MinimumError(value, rules.maximumSponsorshipPerRace)));
        perRace.Add(Number(rules, "Maximum payment per race ($)", rules.maximumSponsorshipPerRace, x => rules.maximumSponsorshipPerRace = x,
            value => MaximumError(rules.minimumSponsorshipPerRace, value)));

        var expenses = Group("Race event expenses");
        expenses.Add(SettingsFormStyle.Label("The selected supplier sets the engine charge. The amount here is the fallback.", "settings-note"));
        foreach (var item in rules.eventCosts)
        {
            var row = Row(expenses);
            var name = RequiredText("Expense", item.name, x => item.name = x);
            name.AddToClassList("settings-field-name");
            row.Add(name);
            row.Add(Number(item, "Amount per event ($)", item.amount, x => item.amount = x));
            row.Add(Remove("Remove expense", () => rules.eventCosts = rules.eventCosts.Where(cost => cost != item).ToArray()));
        }
        expenses.Add(SettingsFormStyle.Button("Add expense", () =>
        {
            rules.eventCosts = rules.eventCosts.Append(new SeriesEventCost { id = Guid.NewGuid().ToString("N"), name = "" }).ToArray();
            Changed();
            Render();
        }));
    }

    private void DrawArtwork(Draft draft)
    {
        var rules = draft.Rules;
        var branding = Group("Series logo");
        var preview = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
        preview.AddToClassList("settings-logo");
        branding.Add(preview);
        RefreshLogo();
        ArtworkField(branding, "Logo", rules.logo, path =>
        {
            rules.logo = path;
            RefreshLogo();
        }, rules.UsesCarsTourArtworkDefaults ? "zMAX CARS Tour logo" : null);

        var backgrounds = Group("Team backgrounds");
        var columns = new[] { backgrounds, backgrounds };
        if (rules.UsesCarsTourArtworkDefaults)
        {
            var grid = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            backgrounds.Add(grid);
            for (int index = 0; index < columns.Length; index++)
            {
                columns[index] = new VisualElement { style = { flexGrow = 1, flexBasis = 0, minWidth = 0 } };
                grid.Add(columns[index]);
            }
        }

        for (int index = 0; index < Backgrounds.Length; index++)
        {
            var (view, label) = Backgrounds[index];
            var item = rules.teamBackgrounds.FirstOrDefault(background => background.view == view);
            ArtworkField(columns[index % columns.Length], label, item?.image, path =>
            {
                if (item == null)
                {
                    item = new SeriesTeamBackground { view = view };
                    rules.teamBackgrounds = rules.teamBackgrounds.Append(item).ToArray();
                }
                item.image = path;
            }, rules.DefaultBackgroundLabel(view));
        }

        void RefreshLogo()
        {
            if (Path.IsPathRooted(rules.ArtworkLogo ?? string.Empty))
                return;
            preview.image = rules.Logo;
            preview.style.display = preview.image == null ? DisplayStyle.None : DisplayStyle.Flex;
        }
    }

    private void ArtworkField(VisualElement parent, string label, string path, Action<string> set, string defaultName = null)
    {
        var row = Row(parent);
        var field = SettingsFormStyle.Field(new TextField(label) { isReadOnly = true }, DisplayName(path), _ => {});
        field.AddToClassList("settings-field-art");
        field.tooltip = string.IsNullOrWhiteSpace(path) ? defaultName : path;
        var showError = SettingsFormStyle.InlineError(field);
        showError(string.IsNullOrWhiteSpace(path) && defaultName == null ? "Choose an image." : null);
        row.Add(field);

        string DisplayName(string image) => string.IsNullOrWhiteSpace(image) && defaultName != null
            ? "Default: " + defaultName : ArtworkName(image);

        void SetArtwork(string next)
        {
            set(next);
            field.SetValueWithoutNotify(DisplayName(next));
            field.tooltip = string.IsNullOrWhiteSpace(next) ? defaultName : next;
            showError(string.IsNullOrWhiteSpace(next) && defaultName == null ? "Choose an image." : null);
            Changed();
        }
        var browse = SettingsFormStyle.Button("Browse…", () =>
        {
            try
            {
                string selected = TrackImageFilePicker.Pick("Choose " + label.ToLowerInvariant() + " artwork");
                if (!string.IsNullOrEmpty(selected))
                    SetArtwork(selected);
            }
            catch (Exception ex) when (SeriesSettingsStore.IsSettingsError(ex))
            {
                SetStatus(ex.Message, error: true);
            }
        });
        browse.tooltip = "Choose an image for " + label + ".";
        row.Add(browse);
        var clear = SettingsFormStyle.Button(defaultName == null ? "Clear" : "Default", () => SetArtwork(string.Empty));
        clear.tooltip = defaultName == null ? "Remove the selected image." : "Restore the built-in " + label + " artwork.";
        row.Add(clear);
    }

    private static string ArtworkName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;
        return path.StartsWith("series-art:", StringComparison.Ordinal) ? "Imported image" : Path.GetFileName(path);
    }

    private TextField RequiredText(string label, string value, Action<string> changed)
    {
        Action<string> showError = null;
        var field = SettingsFormStyle.Field(new TextField(label), value ?? string.Empty, text =>
        {
            changed(text);
            showError(SettingsFormStyle.TextError(text));
            Changed();
        });
        showError = SettingsFormStyle.InlineError(field);
        showError(SettingsFormStyle.TextError(value));
        return field;
    }

    private delegate bool NumberParser<T>(string text, out T value);

    private TextField Number(object owner, string label, int value, Action<int> changed, Func<int, string> validate = null) =>
        Number(owner, label, value, int.TryParse, changed, validate ?? (amount => NonnegativeError(amount)));

    private TextField Number(object owner, string label, long value, Action<long> changed, Func<long, string> validate = null) =>
        Number(owner, label, value, long.TryParse, changed, validate ?? NonnegativeError);

    private TextField Number(object owner, string label, float value, Action<float> changed, Func<float, string> validate) =>
        Number(owner, label, value, ParseFiniteFloat, changed, validate);

    private static string NonnegativeError(long value) => value < 0 ? "Enter zero or more." : null;

    private static string MinimumError(long minimum, long maximum) =>
        NonnegativeError(minimum) ?? (minimum > maximum ? "Minimum cannot exceed maximum." : null);

    private static string MaximumError(long minimum, long maximum) =>
        NonnegativeError(maximum) ?? (maximum < minimum ? "Maximum cannot be below minimum." : null);

    private static string EngineRatingError(float value) =>
        value < 0f || value > 1f ? "Enter a value from 0 to 1." : null;

    private static string TrackLengthError(float value) =>
        value < 0f || value > 100f ? "Enter a track limit from 0 to 100 miles." : null;

    private static string StaffLimitError(int value) => value < 1 ? "Allow at least one driver." : null;

    private static bool ParseFiniteFloat(string text, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) &&
        !float.IsNaN(value) && !float.IsInfinity(value);

    private TextField Number<T>(object owner, string label, T value, NumberParser<T> parse, Action<T> changed, Func<T, string> validate = null)
        where T : IFormattable
    {
        var draft = Current;
        var key = (owner, label);
        int fieldTab = tab;
        bool hasError = draft.InvalidNumbers.TryGetValue(key, out var invalid);
        var field = SettingsFormStyle.Field(new TextField(label),
            hasError ? invalid.Text : value.ToString(null, CultureInfo.CurrentCulture), text =>
            {
                string error = SettingsFormStyle.TextError(text);
                if (error == null && parse(text, out var number))
                {
                    draft.InvalidNumbers.Remove(key);
                    changed(number);
                }
                else
                {
                    error ??= "Enter a valid number.";
                    draft.InvalidNumbers[key] = (text, error, fieldTab);
                }
                Changed();
            });
        var showError = SettingsFormStyle.InlineError(field);
        refreshNumericErrors.Add(() =>
        {
            if (draft.InvalidNumbers.TryGetValue(key, out var invalidEdit))
                showError(invalidEdit.Error);
            else
                showError(parse(field.value, out var number) ? validate?.Invoke(number) : null);
        });
        refreshNumericErrors[refreshNumericErrors.Count - 1]();
        return field;
    }

    private T Field<T, TValue>(T field, TValue value, Action<TValue> changed) where T : BaseField<TValue> =>
        SettingsFormStyle.Field(field, value, next =>
        {
            Changed();
            changed(next);
        });

    private Button Remove(string label, Action remove)
    {
        var button = SettingsFormStyle.Button("Remove", () =>
        {
            remove();
            Changed();
            Render();
        });
        button.tooltip = label;
        button.AddToClassList("settings-remove");
        return button;
    }

    private VisualElement Group(string name)
    {
        var group = new VisualElement();
        group.AddToClassList("settings-group");
        var heading = SettingsFormStyle.Label(name, "settings-group-heading");
        heading.AddToClassList("google-sans-semibold");
        group.Add(heading);
        body.Add(group);
        return group;
    }

    private static VisualElement Row(VisualElement group)
    {
        var row = new VisualElement();
        row.AddToClassList("settings-row");
        group.Add(row);
        return row;
    }

    private void Changed()
    {
        Current.Dirty = true;
        foreach (var refresh in refreshNumericErrors)
            refresh();
        SetStatus("Unsaved changes");
    }

    private void SetStatus(string message, bool error = false)
    {
        var draft = Current;
        var invalid = draft == null ? new HashSet<int>() : InvalidTabs(draft);
        for (int i = 0; i < tabs.Length; i++)
            SettingsFormStyle.SetTabError(tabs[i], invalid.Contains(i));
        SettingsFormStyle.SetValidationMessage(status,
            !error && invalid.Count > 0 ? "Fix the highlighted fields before saving." : message, error || invalid.Count > 0);
        bool dirty = draft?.Dirty == true;
        save.SetEnabled(dirty && invalid.Count == 0);
        revert.SetEnabled(dirty);
    }

    private static HashSet<int> InvalidTabs(Draft draft)
    {
        var rules = draft.Rules;
        var suppliers = draft.SharedSuppliers ? rules.sharedEngineSuppliers :
            rules.manufacturers.SelectMany(make => make.engineSuppliers ?? Array.Empty<SeriesEngine>()).ToArray();
        var invalid = new HashSet<int>();
        foreach (var entry in draft.InvalidNumbers)
        {
            var owner = entry.Key.Owner;
            if (ReferenceEquals(owner, rules) || ReferenceEquals(owner, draft.StagePercentages) ||
                rules.employeeRoles.Any(role => ReferenceEquals(owner, role)) ||
                rules.eventCosts.Any(cost => ReferenceEquals(owner, cost)) ||
                suppliers.Any(supplier => ReferenceEquals(owner, supplier)))
                invalid.Add(entry.Value.Tab);
        }

        if (TrackLengthError(rules.maximumTrackLengthMiles) != null ||
            draft.StagePercentages.Any(percent => percent < 0 || percent > 100) ||
            draft.StagePercentages.Sum(percent => (long)percent) != 100)
            invalid.Add(0);
        bool missingSupplier = draft.SharedSuppliers ? suppliers.Length == 0 :
            rules.manufacturers.Any(make => (make.engineSuppliers?.Length ?? 0) == 0);
        if (rules.manufacturers.Length == 0 || missingSupplier ||
            suppliers.Any(supplier => SettingsFormStyle.TextError(supplier.name) != null ||
            NonnegativeError(supplier.maintenancePerRace) != null || EngineRatingError(supplier.power) != null ||
            EngineRatingError(supplier.reliability) != null))
            invalid.Add(1);
        if (StaffLimitError(rules.maximumEmployees) != null ||
            rules.employeeRoles.Any(role => MinimumError(role.minimumSalary, role.maximumSalary) != null))
            invalid.Add(2);
        if (NonnegativeError(rules.newChassisCost) != null || NonnegativeError(rules.componentUpgradeCost) != null ||
            NonnegativeError(rules.maximumComponentRepairCost) != null ||
            MinimumError(rules.minimumSponsorshipSigning, rules.maximumSponsorshipSigning) != null ||
            MinimumError(rules.minimumSponsorshipPerRace, rules.maximumSponsorshipPerRace) != null ||
            rules.eventCosts.Any(cost => SettingsFormStyle.TextError(cost.name) != null || NonnegativeError(cost.amount) != null))
            invalid.Add(3);
        if (string.IsNullOrWhiteSpace(rules.ArtworkLogo) || Backgrounds.Any(page =>
            rules.DefaultBackgroundLabel(page.View) == null && string.IsNullOrWhiteSpace(rules.BackgroundFor(page.View))))
            invalid.Add(4);
        return invalid;
    }

    private void Save()
    {
        var draft = Current;
        if (draft?.Dirty != true || InvalidTabs(draft).Any())
            return;
        try
        {
            var rules = PrepareRules(draft);
            var original = series[selection.index];
            SeriesSettingsStore.Save(rules, original);
            drafts.Remove(original.id);
            Render(message: $"Saved {original.name}.");
        }
        catch (Exception ex) when (SeriesSettingsStore.IsSettingsError(ex))
        {
            SetStatus("Could not save settings. " + ex.Message, error: true);
        }
    }

    private static RacingSeries PrepareRules(Draft draft)
    {
        var rules = SeriesSettingsStore.Clone(draft.Rules);
        draft.ApplyStageSettings(rules);
        if (!draft.SharedSuppliers)
            rules.sharedEngineSuppliers = Array.Empty<SeriesEngine>();
        return rules;
    }

    private void Revert()
    {
        drafts.Remove(series[selection.index].id);
        Render(message: "Reverted unsaved changes.");
    }

    private sealed class Draft
    {
        private static readonly int[] NoStages = { 100, 0, 0, 0 };
        public readonly RacingSeries Rules;
        public readonly int[] StagePercentages;
        public readonly Dictionary<(object Owner, string Label), (string Text, string Error, int Tab)> InvalidNumbers = new();
        public bool SharedSuppliers;
        public bool Dirty;

        public Draft(RacingSeries series)
        {
            Rules = SeriesSettingsStore.Clone(series);
            Rules.manufacturers ??= Array.Empty<SeriesManufacturer>();
            Rules.sharedEngineSuppliers ??= Array.Empty<SeriesEngine>();
            Rules.employeeRoles ??= Array.Empty<SeriesEmployeeRole>();
            Rules.eventCosts ??= Array.Empty<SeriesEventCost>();
            if (Rules.stagePercentages?.Length != ScheduledRace.MaximumStages)
                Rules.stagePercentages = ScheduledRace.DefaultStagePercentages();
            Rules.teamBackgrounds ??= Array.Empty<SeriesTeamBackground>();
            StagePercentages = (Rules.usesStages ? Rules.stagePercentages : NoStages).ToArray();
            SharedSuppliers = Rules.sharedEngineSuppliers.Length > 0;
        }

        public void ApplyStageSettings(RacingSeries rules)
        {
            rules.usesStages = !StagePercentages.SequenceEqual(NoStages);
            if (rules.usesStages)
                rules.stagePercentages = StagePercentages.ToArray();
        }
    }
}

