using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

internal enum CarTypeValidationSection { General, Cameras, Numbers, Dashboard }

internal static class CarTypeSettingsStore
{
    public static CarTypeDefinition ApplySaved(string model, CarTypeDefinition defaults)
    {
        try
        {
            string path = SettingsPath(model);
            if (!File.Exists(path) && model == "TourModified")
            {
                string legacy = Path.Combine(Application.persistentDataPath, "CarTypeSettings", "TourModifiedDetailed.json");
                if (File.Exists(legacy))
                    path = legacy;
            }
            if (!File.Exists(path)) return defaults;
            var definition = JsonUtility.FromJson<CarTypeDefinition>(JsonUtility.ToJson(defaults));
            JsonUtility.FromJsonOverwrite(File.ReadAllText(path, System.Text.Encoding.UTF8), definition);
            // LODs are bundled assets, not editable car type settings.
            definition.lodModels = defaults.lodModels;
            definition.trackModelLods = defaults.trackModelLods;
            definition.fullDetailInteriorModel = defaults.fullDetailInteriorModel;
            if (model == "TourModified" && definition.displayName == "Tour Modified Detailed")
                definition.displayName = "Tour Modified";
            string error = Validate(definition);
            if (error != null) throw new ArgumentException(error);
            return definition;
        }
        catch (Exception ex) when (SeriesSettingsStore.IsSettingsError(ex))
        {
            Debug.LogWarning($"Could not read car type settings for {model}: {ex.Message}");
            return defaults;
        }
    }

    public static void Save(string model, CarTypeDefinition draft)
    {
        string error = Validate(draft);
        if (error != null) throw new ArgumentException(error);
        var saved = JsonUtility.FromJson<CarTypeDefinition>(JsonUtility.ToJson(draft));
        saved.dashboardImage = SeriesSettingsStore.SaveArtwork(saved.dashboardImage);
        string json = JsonUtility.ToJson(saved, true);
        SeriesSettingsStore.AtomicWrite(SettingsPath(model), temporary => File.WriteAllText(temporary, json));
        CarTypeDefinition.Invalidate(model);
    }

    public static string Validate(CarTypeDefinition car) => ValidationErrors(car).FirstOrDefault().Error;

    internal static IEnumerable<(CarTypeValidationSection Section, string Error)> ValidationErrors(CarTypeDefinition car)
    {
        string nameError = SettingsFormStyle.TextError(car.displayName);
        if (nameError != null)
            yield return (CarTypeValidationSection.General, $"Display name: {nameError}");
        if (!(car.steeringWheelRatio >= .01f && car.steeringWheelRatio <= float.MaxValue))
            yield return (CarTypeValidationSection.General, "Set a positive steering-wheel ratio.");

        if (!ValidAnchor(car.inCarCamera) || !ValidPosition(car.rearviewCamera) ||
            !(Mathf.Abs(car.rearviewMirrorOffset.x) <= 10000f && Mathf.Abs(car.rearviewMirrorOffset.y) <= 10000f))
            yield return (CarTypeValidationSection.Cameras, "Enter valid camera and mirror positions and anchors.");
        if (!ValidAnchor(car.roofNumber) || !ValidAnchor(car.leftNumber) || !ValidAnchor(car.rightNumber))
            yield return (CarTypeValidationSection.Numbers, "Enter valid number positions and anchors.");

        if (car.dashboardSwitchRows < 0 || car.dashboardSwitchRows > 2)
            yield return (CarTypeValidationSection.Dashboard, "Use zero, one, or two dashboard switch rows.");
        if (!string.IsNullOrEmpty(car.cockpitStyle) && car.cockpitStyle.Length > 50)
            yield return (CarTypeValidationSection.Dashboard, "Use 50 characters or fewer in the cockpit style class.");
        var gauges = car.gauges ?? Array.Empty<CockpitGaugeDefinition>();
        if (gauges.Any(gauge => gauge == null || SettingsFormStyle.TextError(gauge.label) != null ||
            !CarTypeDefinition.GaugeSources.Contains(gauge.source, StringComparer.OrdinalIgnoreCase)))
            yield return (CarTypeValidationSection.Dashboard, "Choose valid gauges and provide a label for each.");
        else if (gauges.Select(gauge => gauge.source).Distinct(StringComparer.OrdinalIgnoreCase).Count() != gauges.Length)
            yield return (CarTypeValidationSection.Dashboard, "Select each dashboard gauge at most once.");
    }

    private static bool ValidAnchor(CarModelPosition position) =>
        position != null && ValidPosition(position.position) && (position.anchor?.Length ?? 0) <= 50;
    private static bool ValidPosition(Vector3 point) =>
        Mathf.Abs(point.x) <= 100f && Mathf.Abs(point.y) <= 100f && Mathf.Abs(point.z) <= 100f;

    private static string SettingsPath(string model)
    {
        model = CarTypeDefinition.CanonicalModel(model);
        if (string.IsNullOrEmpty(model) || Path.GetFileName(model) != model || model == "." || model == "..")
            throw new ArgumentException("Invalid car type ID.");
        return Path.Combine(Application.persistentDataPath, "CarTypeSettings", model + ".json");
    }
}

