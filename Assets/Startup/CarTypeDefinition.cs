using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[Serializable]
internal sealed class CarTypeDefinition
{
    internal static readonly string[] GaugeSources = { "Rpm", "Fuel", "Throttle", "Brake" };
    public string displayName;
    // Model resources ordered from lowest to highest detail. An empty list uses this car type's own model.
    public string[] lodModels = Array.Empty<string>();
    public string superspeedwayModel;
    public string shortTrackModel;
    public string roadCourseLeftModel;
    public string roadCourseRightModel;
    public CarTrackModelLods[] trackModelLods = Array.Empty<CarTrackModelLods>();
    public string cockpitStyle;
    public bool darkDashboardDials;
    public float steeringWheelRatio = 14f;
    public int dashboardSwitchRows = 2;
    public CarModelPosition inCarCamera = new CarModelPosition("DriverEye", new Vector3(-0.3f, 0.9f, -0.12f));
    public Vector3 rearviewCamera = new Vector3(0f, 0.95f, -1.58f);
    // Pixel offset from the centered mirror overlay, relative to its default position.
    public Vector2 rearviewMirrorOffset;
    public string dashboardImage;
    public CockpitGaugeDefinition[] gauges =
    {
        new CockpitGaugeDefinition("Rpm", "RPM", true),
        new CockpitGaugeDefinition("Fuel", "FUEL"),
        new CockpitGaugeDefinition("Throttle", "THROTTLE"),
        new CockpitGaugeDefinition("Brake", "BRAKE")
    };
    // Model-specific decal fit, measured in the assembled car's local axes.
    public float roofNumberScale = 1f;
    public float roofNumberPitch;
    public float sideNumberScale = 1f;
    // Maximum visible number dimensions in assembled car space. Zero leaves the size unrestricted.
    public Vector2 roofNumberMaxSize;
    public Vector2 sideNumberMaxSize;
    public float leftNumberYaw;
    public float rightNumberYaw;
    public CarModelPosition roofNumber = new CarModelPosition("RoofNumber", new Vector3(0f, 1.066f, -0.12f));
    public CarModelPosition leftNumber = new CarModelPosition("LeftNumber", new Vector3(-0.776f, 0.49f, 0f));
    public CarModelPosition rightNumber = new CarModelPosition("RightNumber", new Vector3(0.776f, 0.49f, 0f));

    internal bool HasTrackModelLods => trackModelLods?.Length > 0;

    public string ModelForLod(string modelResource, int level) =>
        SelectLod(string.IsNullOrEmpty(modelResource) ? "Impala" : modelResource, lodModels, level);

    internal string ModelForTrackLod(string variant, int level)
    {
        var chain = trackModelLods?.FirstOrDefault(entry => entry != null && entry.model == variant);
        return SelectLod(variant, chain?.lodModels, level);
    }

    private static string SelectLod(string fallback, string[] candidates, int level)
    {
        var models = (candidates ?? Array.Empty<string>()).Where(model => !string.IsNullOrWhiteSpace(model)).ToArray();
        return models.Length == 0 ? fallback : models[Mathf.Clamp(level - 1, 0, models.Length - 1)];
    }

    private static readonly Dictionary<string, CarTypeDefinition> Types = new(StringComparer.Ordinal);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache() => Types.Clear();

    public static List<(string Model, string Name)> AvailableModels() =>
        Resources.LoadAll<TextAsset>("CarTypes")
            .Select(asset =>
            {
                string name = ForModel(asset.name).displayName;
                return (Model: asset.name, Name: string.IsNullOrWhiteSpace(name) ? asset.name : name);
            })
            .OrderBy(car => car.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public static string CanonicalModel(string model) =>
        model == "TourModifiedDetailed" ? "TourModified" : model;

    internal static void Invalidate(string model) => Types.Remove(CanonicalModel(model));

    public static CarTypeDefinition ForModel(string modelResource)
    {
        string key = string.IsNullOrEmpty(modelResource) ? "Impala" : CanonicalModel(modelResource);
        if (Types.TryGetValue(key, out var definition))
            return definition;

        var asset = Resources.Load<TextAsset>("CarTypes/" + key);
        definition = new CarTypeDefinition();
        if (asset != null)
            JsonUtility.FromJsonOverwrite(asset.text, definition);

        if (string.IsNullOrWhiteSpace(definition.displayName))
            definition.displayName = key;
        definition = CarTypeSettingsStore.ApplySaved(key, definition);
        Types.Add(key, definition);
        return definition;
    }
}

[Serializable]
internal sealed class CarTrackModelLods
{
    public string model;
    public string[] lodModels;
}

[Serializable]
internal sealed class CarModelPosition
{
    // Positions use the assembled car's local axes and scale. An anchor, when
    // present in the model, takes precedence over the numeric fallback.
    public string anchor;
    public Vector3 position;

    public CarModelPosition(string anchor, Vector3 position)
    {
        this.anchor = anchor;
        this.position = position;
    }

    public Vector3 Resolve(Transform car, Transform[] modelNodes, float sideClearance = 0f)
    {
        var target = string.IsNullOrEmpty(anchor) ? null :
            Array.Find(modelNodes, node => node.name == anchor);
        if (target == null)
            return position;

        Vector3 point = car.InverseTransformPoint(target.position);
        point.x += Mathf.Sign(point.x) * sideClearance;
        return point;
    }
}

[Serializable]
internal sealed class CockpitGaugeDefinition
{
    public string source;
    public string label;
    public bool large;

    public CockpitGaugeDefinition(string source, string label, bool large = false)
    {
        this.source = source;
        this.label = label;
        this.large = large;
    }
}

