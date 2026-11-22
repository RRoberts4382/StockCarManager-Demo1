using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

internal sealed class CarTypeSettings : DefinitionSettings<CarTypeDefinition>
{
    public CarTypeSettings(Action back)
        : base("Car Type Settings", "Car Type", back, "General", "Cameras & mirror", "Number placement", "Dashboard") {}

    protected override IEnumerable<Entry> LoadEntries() => CarTypeDefinition.AvailableModels()
        .Select(item => new Entry(item.Model, item.Name, CarTypeDefinition.ForModel(item.Model),
            value => CarTypeSettingsStore.Save(item.Model, value)));

    protected override IEnumerable<(int Tab, string Error)> ValidationErrors(CarTypeDefinition draft)
    {
        foreach (var error in CarTypeSettingsStore.ValidationErrors(draft))
            yield return ((int)error.Section, error.Error);
        string image = draft.dashboardImage;
        if (!string.IsNullOrWhiteSpace(image) && (Path.IsPathRooted(image)
            ? !File.Exists(image) : SeriesSettingsStore.LoadArtwork(image) == null))
            yield return ((int)CarTypeValidationSection.Dashboard, "The dashboard image could not be found.");
    }

    protected override void PrepareSave(CarTypeDefinition draft) =>
        SeriesSettingsStore.SaveArtwork(draft.dashboardImage, validateOnly: true);

    protected override void Draw(CarTypeDefinition car, int tab)
    {
        switch ((CarTypeValidationSection)tab)
        {
            case CarTypeValidationSection.General: DrawGeneral(car); break;
            case CarTypeValidationSection.Cameras: DrawCameras(car); break;
            case CarTypeValidationSection.Numbers: DrawNumbers(car); break;
            case CarTypeValidationSection.Dashboard: DrawDashboard(car); break;
        }
    }

    private void DrawGeneral(CarTypeDefinition car)
    {
        var general = Group("Car type");
        Text(general, "Display name", car.displayName, value => car.displayName = value);
        Number(general, "Steering-wheel ratio", car.steeringWheelRatio, .01f, float.MaxValue, value => car.steeringWheelRatio = value);
    }

    private void DrawCameras(CarTypeDefinition car)
    {
        var cockpit = Group("Cameras and mirror");
        DrawAnchor(cockpit, "In-car camera", car.inCarCamera);
        DrawPosition(cockpit, "Rearview camera", car.rearviewCamera, value => car.rearviewCamera = value);
        var mirror = Row(cockpit);
        Number(mirror, "Mirror horizontal offset (px)", car.rearviewMirrorOffset.x, -10000, 10000, value => car.rearviewMirrorOffset.x = value);
        Number(mirror, "Mirror vertical offset (px)", car.rearviewMirrorOffset.y, -10000, 10000, value => car.rearviewMirrorOffset.y = value);
        Note(cockpit, "Positions are in the car’s local coordinates. A named model anchor takes precedence over the entered position.");
    }

    private void DrawNumbers(CarTypeDefinition car)
    {
        var numbers = Group("Number placement");
        DrawAnchor(numbers, "Roof number", car.roofNumber);
        DrawAnchor(numbers, "Left number", car.leftNumber);
        DrawAnchor(numbers, "Right number", car.rightNumber);
    }

    private void DrawDashboard(CarTypeDefinition car)
    {
        var dash = Group("Dashboard");
        DrawArtwork(dash, car);
        Text(dash, "Cockpit style class", car.cockpitStyle, value => car.cockpitStyle = value, optional: true);
        Integer(dash, "Switch rows", car.dashboardSwitchRows, 0, 2, value => car.dashboardSwitchRows = value);
        Toggle(dash, "Dark gauge faces", car.darkDashboardDials, value => car.darkDashboardDials = value);

        var gauges = Group("Dashboard gauges");
        foreach (string source in CarTypeDefinition.GaugeSources)
        {
            var row = Row(gauges);
            string label = source == "Rpm" ? "RPM" : source;
            var gauge = car.gauges.FirstOrDefault(item => string.Equals(item.source, source, StringComparison.OrdinalIgnoreCase));
            Toggle(row, label, gauge != null, enabled =>
            {
                car.gauges = enabled ? car.gauges.Append(new CockpitGaugeDefinition(source, label.ToUpperInvariant(), source == "Rpm")).ToArray() :
                    car.gauges.Where(item => item != gauge).ToArray();
                if (!enabled) Forget(label + " label");
            }, redraw: true);
            if (gauge == null) continue;
            Text(row, label + " label", gauge.label, value => gauge.label = value);
            Toggle(row, "Large " + label + " gauge", gauge.large, value => gauge.large = value);
        }
    }

    private void DrawAnchor(VisualElement parent, string label, CarModelPosition anchor)
    {
        Text(parent, label + " anchor", anchor.anchor, value => anchor.anchor = value, optional: true);
        DrawPosition(parent, label, anchor.position, value => anchor.position = value);
    }

    private void DrawPosition(VisualElement parent, string label, Vector3 position, Action<Vector3> set)
    {
        var row = Row(parent);
        Number(row, label + " X", position.x, -100, 100, value => { position.x = value; set(position); });
        Number(row, label + " Y", position.y, -100, 100, value => { position.y = value; set(position); });
        Number(row, label + " Z", position.z, -100, 100, value => { position.z = value; set(position); });
    }

    private void DrawArtwork(VisualElement group, CarTypeDefinition car)
    {
        var row = Row(group);
        string DisplayName() => string.IsNullOrWhiteSpace(car.dashboardImage) ? "Drawn dashboard" :
            car.dashboardImage.StartsWith("series-art:", StringComparison.Ordinal) ? "Imported dashboard" :
                Path.GetFileName(car.dashboardImage);
        var field = SettingsFormStyle.Field(new TextField("Dashboard image") { isReadOnly = true }, DisplayName(), _ => {});
        field.AddToClassList("settings-field-art");
        row.Add(field);
        var error = SettingsFormStyle.InlineError(field);
        row.Add(SettingsFormStyle.Button("Browse…", () =>
        {
            try
            {
                string selected = TrackImageFilePicker.Pick("Choose a dashboard image");
                if (string.IsNullOrEmpty(selected)) return;
                // Import into the local artwork store when saving. Decode now to catch invalid files beside the picker.
                SeriesSettingsStore.SaveArtwork(selected, validateOnly: true);
                car.dashboardImage = selected;
                field.SetValueWithoutNotify(DisplayName());
                error(null);
                Changed();
            }
            catch (Exception ex) when (SeriesSettingsStore.IsSettingsError(ex)) { error(ex.Message); }
        }));
        row.Add(SettingsFormStyle.Button("Use drawn dashboard", () =>
        {
            car.dashboardImage = "";
            field.SetValueWithoutNotify(DisplayName());
            error(null);
            Changed();
        }));
    }
}

