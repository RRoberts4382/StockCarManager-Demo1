using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;

internal static class SeriesSettingsStore
{
    private const string ArtworkPrefix = "series-art:";
    private const long MaximumImageBytes = 16 * 1024 * 1024;
    private static readonly Dictionary<string, Texture2D> Artwork = new();
    private static string Folder => Path.Combine(Application.persistentDataPath, "SeriesSettings");
    private static string ArtworkFolder => Path.Combine(Folder, "Artwork");

    public static RacingSeries ApplySaved(RacingSeries defaults)
    {
        defaults.name = CareerSetup.LimitName(defaults.name);
        defaults.carModel = CarTypeDefinition.CanonicalModel(defaults.carModel);
        try
        {
            string path = SettingsPath(defaults.id);
            if (!File.Exists(path))
                return defaults;

            var saved = Clone(defaults);
            JsonUtility.FromJsonOverwrite(File.ReadAllText(path), saved);
            saved.carModel = defaults.id == "craftsman-truck" && string.IsNullOrWhiteSpace(saved.carModel)
                ? defaults.carModel : CarTypeDefinition.CanonicalModel(saved.carModel);
            saved.id = defaults.id;
            saved.name = defaults.name;
            saved.SeriesLevel = defaults.SeriesLevel;
            string originalJson = File.ReadAllText(path);
            var original = JsonUtility.FromJson<RacingSeries>(originalJson);
            if (original.name != CareerSetup.LimitName(original.name))
            {
                original.name = CareerSetup.LimitName(original.name);
                AtomicWrite(path, temporary => File.WriteAllText(temporary, JsonUtility.ToJson(original, true)));
            }
            string error = Validate(saved);
            if (error != null)
                throw new ArgumentException(error);
            return saved;
        }
        catch (Exception ex) when (IsSettingsError(ex))
        {
            Debug.LogWarning($"Could not read series settings for {defaults.name}: {ex.Message}");
            return defaults;
        }
    }

    public static RacingSeries Clone(RacingSeries series) => JsonUtility.FromJson<RacingSeries>(JsonUtility.ToJson(series));

    public static void Save(RacingSeries draft, RacingSeries series)
    {
        var saved = Clone(draft);
        saved.carModel = CarTypeDefinition.CanonicalModel(saved.carModel);
        saved.id = series.id;
        saved.name = CareerSetup.LimitName(series.name);
        saved.SeriesLevel = series.SeriesLevel;
        string error = Validate(saved);
        if (error != null)
            throw new ArgumentException(error);

        saved.logo = SaveArtwork(saved.logo);
        foreach (var background in saved.teamBackgrounds ?? Array.Empty<SeriesTeamBackground>())
            background.image = SaveArtwork(background.image);

        string json = JsonUtility.ToJson(saved, true);
        AtomicWrite(SettingsPath(series.id), temporary => File.WriteAllText(temporary, json));
        // Career setup keeps a reference to each catalog entry.
        JsonUtility.FromJsonOverwrite(json, series);
    }

    public static string Validate(RacingSeries series)
    {
        if (string.IsNullOrWhiteSpace(series.name) || series.name.Length > 25)
            return "Series name must contain 1 to 25 characters.";
        if (series.manufacturers?.Length > 0 != true)
            return "Choose at least one manufacturer.";
        if (series.manufacturers.Any(make => make == null || !Enum.IsDefined(typeof(CarManufacturer), make.manufacturer)) ||
            series.manufacturers.Select(make => make.manufacturer).Distinct().Count() != series.manufacturers.Length)
            return "Manufacturer choices must be valid and unique.";

        var shared = series.sharedEngineSuppliers;
        if (series.id == "craftsman-truck" && shared?.Length != 1)
            return "The Truck Series needs one supplier shared by all manufacturers.";
        if (shared?.Length > 0)
        {
            string error = ValidateSuppliers(shared, "Shared suppliers");
            if (error != null) return error;
        }
        else
        {
            foreach (var make in series.manufacturers)
            {
                string error = ValidateSuppliers(make.engineSuppliers, make.manufacturer.ToString());
                if (error != null) return error;
            }
        }

        var roles = series.employeeRoles ?? Array.Empty<SeriesEmployeeRole>();
        if (roles.Any(role => role == null || !Enum.IsDefined(typeof(EmployeeRole), role.role) ||
            role.minimumSalary < 0 || role.maximumSalary < role.minimumSalary || role.maximumSalary == int.MaxValue) ||
            roles.Select(role => role.role).Distinct().Count() != roles.Length ||
            !roles.Any(role => role.role == EmployeeRole.Driver))
            return "Include the Driver role and give each selected role a valid salary range.";
        if (series.maximumEmployees < 1)
            return "The staff limit must allow at least one driver.";

        if (series.newChassisCost < 0 || series.componentUpgradeCost < 0 || series.maximumComponentRepairCost < 0)
            return "Chassis, upgrade, and repair costs must be zero or more.";
        if (series.usesStages && (series.stagePercentages?.Length != ScheduledRace.MaximumStages ||
            series.stagePercentages.Any(percent => percent < 0 || percent > 100) || series.stagePercentages.Sum() != 100))
            return "Stage percentages must each be between 0 and 100 and total 100%.";
        if (!InRange(series.maximumTrackLengthMiles, 0f, 100f))
            return "Set a track limit between 0 and 100 miles.";
        if (!ValidMoneyRange(series.minimumSponsorshipSigning, series.maximumSponsorshipSigning) ||
            !ValidMoneyRange(series.minimumSponsorshipPerRace, series.maximumSponsorshipPerRace))
            return "Enter nonnegative sponsorship ranges in ascending order, up to $1 trillion.";

        var costs = series.eventCosts ?? Array.Empty<SeriesEventCost>();
        if (costs.Any(cost => cost == null || string.IsNullOrWhiteSpace(cost.id) ||
            string.IsNullOrWhiteSpace(cost.name) || cost.amount < 0) ||
            costs.Select(cost => cost.id).Distinct().Count() != costs.Length)
            return "Each event expense needs a name and a nonnegative amount.";
        long eventTotal = costs.Sum(cost => (long)cost.amount);
        var suppliers = shared?.Length > 0 ? shared : series.manufacturers.SelectMany(make => make.engineSuppliers);
        var engineCost = costs.FirstOrDefault(cost => cost.id == "engine");
        if (engineCost != null)
            eventTotal += Math.Max(0, suppliers.Max(supplier => supplier.maintenancePerRace) - (long)engineCost.amount);
        if (eventTotal > int.MaxValue)
            return "Combined event expenses exceed the supported total.";
        if (series.teamBackgrounds?.Any(item => item == null || string.IsNullOrWhiteSpace(item.view)) == true)
            return "A Team background is missing its view.";
        return null;
    }

    public static Texture2D LoadArtwork(string location)
    {
        if (string.IsNullOrWhiteSpace(location))
            return null;
        if (!location.StartsWith(ArtworkPrefix, StringComparison.Ordinal))
            return Resources.Load<Texture2D>(location);

        if (Artwork.TryGetValue(location, out var cached) && cached != null)
            return cached;

        try
        {
            string path = ArtworkPath(location);
            if (!File.Exists(path))
                return null;
            var texture = DecodeImage(ReadImage(path));
            texture.name = Path.GetFileNameWithoutExtension(path);
            Artwork[location] = texture;
            return texture;
        }
        catch (Exception ex) when (IsSettingsError(ex))
        {
            Debug.LogWarning($"Could not read series artwork: {ex.Message}");
            return null;
        }
    }

    public static bool IsSettingsError(Exception ex) =>
        ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException;

    private static string ValidateSuppliers(SeriesEngine[] suppliers, string name)
    {
        if (suppliers?.Length > 0 != true)
            return $"{name} needs at least one engine supplier.";
        if (suppliers.Any(supplier => supplier == null || string.IsNullOrWhiteSpace(supplier.name) ||
            !InRange(supplier.power, 0f, 1f) || !InRange(supplier.reliability, 0f, 1f) || supplier.maintenancePerRace < 0) ||
            suppliers.Select(supplier => supplier.name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != suppliers.Length)
            return $"{name} needs unique supplier names, power and reliability from 0 to 1, and nonnegative maintenance costs.";
        return null;
    }

    private static bool InRange(float value, float min, float max) => value >= min && value <= max;
    private static bool ValidMoneyRange(long minimum, long maximum) =>
        minimum >= 0 && maximum >= minimum && maximum <= 1_000_000_000_000L;

    public static void ValidateArtwork(RacingSeries series)
    {
        SaveArtwork(series.logo, validateOnly: true);
        foreach (var background in series.teamBackgrounds ?? Array.Empty<SeriesTeamBackground>())
            SaveArtwork(background.image, validateOnly: true);
    }

    internal static string SaveArtwork(string location, bool validateOnly = false)
    {
        location = location?.Trim();
        if (string.IsNullOrEmpty(location))
            return string.Empty;
        if (!Path.IsPathRooted(location))
        {
            if (LoadArtwork(location) == null)
                throw new ArgumentException($"Could not find the image '{location}'.");
            return location;
        }

        string extension = Path.GetExtension(location).ToLowerInvariant();
        if (extension != ".png" && extension != ".jpg" && extension != ".jpeg")
            throw new ArgumentException("Choose a PNG or JPEG image.");
        byte[] data = ReadImage(location);
        var sample = DecodeImage(data);
        if (Application.isPlaying)
            UnityEngine.Object.Destroy(sample);
        else
            UnityEngine.Object.DestroyImmediate(sample);
        if (validateOnly)
            return location;

        using var hash = SHA256.Create();
        string key = ArtworkPrefix + BitConverter.ToString(hash.ComputeHash(data)).Replace("-", "").ToLowerInvariant() + extension;
        string path = ArtworkPath(key);
        if (!File.Exists(path))
            AtomicWrite(path, temporary => File.WriteAllBytes(temporary, data));
        return key;
    }

    private static byte[] ReadImage(string path)
    {
        if (new FileInfo(path).Length > MaximumImageBytes)
            throw new ArgumentException("Artwork must be 16 MB or smaller.");
        return File.ReadAllBytes(path);
    }

    private static Texture2D DecodeImage(byte[] data)
    {
        var texture = new Texture2D(2, 2);
        if (ImageConversion.LoadImage(texture, data, markNonReadable: true) &&
            texture.width <= 4096 && texture.height <= 4096)
            return texture;
        UnityEngine.Object.Destroy(texture);
        throw new ArgumentException("Artwork must be a readable PNG or JPEG up to 4096 × 4096 pixels.");
    }

    private static string SettingsPath(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Any(character => !char.IsLetterOrDigit(character) && character != '-' && character != '_'))
            throw new ArgumentException("Invalid racing series ID.");
        return Path.Combine(Folder, id + ".json");
    }

    private static string ArtworkPath(string key)
    {
        string filename = key.Substring(ArtworkPrefix.Length);
        string extension = Path.GetExtension(filename);
        string hash = Path.GetFileNameWithoutExtension(filename);
        if (filename.Length != hash.Length + extension.Length || hash.Length != 64 ||
            hash.Any(character => !Uri.IsHexDigit(character)) ||
            (extension != ".png" && extension != ".jpg" && extension != ".jpeg"))
            throw new ArgumentException("Invalid imported artwork reference.");
        return Path.Combine(ArtworkFolder, filename);
    }

    internal static void AtomicWrite(string path, Action<string> write)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            write(temporary);
            if (File.Exists(path))
                File.Replace(temporary, path, null);
            else
                File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}

