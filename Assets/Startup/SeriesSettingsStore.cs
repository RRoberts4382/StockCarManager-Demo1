using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
        NormalizeManufacturerModels(defaults);
        try
        {
            string path = SettingsPath(defaults.id);
            if (!File.Exists(path))
                return defaults;

            var saved = Clone(defaults);
            saved.economyVersion = 0;
            JsonUtility.FromJsonOverwrite(File.ReadAllText(path, Encoding.UTF8), saved);
            if (saved.economyVersion < defaults.economyVersion)
                UpdateLegacyEconomyDefaults(saved, defaults);
            // Catalog defaults still apply to manufacturers with no saved override.
            saved.manufacturerDefaultModels = defaults.manufacturerDefaultModels;
            saved.carModel = defaults.id == "craftsman-truck" && string.IsNullOrWhiteSpace(saved.carModel)
                ? defaults.carModel : CarTypeDefinition.CanonicalModel(saved.carModel);
            NormalizeManufacturerModels(saved);
            saved.id = defaults.id;
            saved.name = string.IsNullOrWhiteSpace(saved.name)
                ? defaults.name : CareerSetup.LimitName(saved.name.Trim());
            saved.SeriesLevel = defaults.SeriesLevel;
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

    private static void UpdateLegacyEconomyDefaults(RacingSeries saved, RacingSeries defaults)
    {
        var legacy = new RacingSeries();
        if (saved.maximumEmployees == legacy.maximumEmployees) saved.maximumEmployees = defaults.maximumEmployees;
        if (saved.newChassisCost == legacy.newChassisCost) saved.newChassisCost = defaults.newChassisCost;
        if (saved.componentUpgradeCost == legacy.componentUpgradeCost) saved.componentUpgradeCost = defaults.componentUpgradeCost;
        if (saved.maximumComponentRepairCost == legacy.maximumComponentRepairCost)
            saved.maximumComponentRepairCost = defaults.maximumComponentRepairCost;

        if (saved.minimumSponsorshipSigning == legacy.minimumSponsorshipSigning &&
            saved.maximumSponsorshipSigning == legacy.maximumSponsorshipSigning)
        {
            saved.minimumSponsorshipSigning = defaults.minimumSponsorshipSigning;
            saved.maximumSponsorshipSigning = defaults.maximumSponsorshipSigning;
        }
        if (saved.minimumSponsorshipPerRace == legacy.minimumSponsorshipPerRace &&
            saved.maximumSponsorshipPerRace == legacy.maximumSponsorshipPerRace)
        {
            saved.minimumSponsorshipPerRace = defaults.minimumSponsorshipPerRace;
            saved.maximumSponsorshipPerRace = defaults.maximumSponsorshipPerRace;
        }

        foreach (var role in saved.employeeRoles ?? Array.Empty<SeriesEmployeeRole>())
        {
            if (role == null || !Enum.IsDefined(typeof(EmployeeRole), role.role)) continue;
            var oldRange = Employee.DefaultSalaryRange(role.role);
            var newRange = defaults.SalaryRange(role.role);
            if (newRange == null || role.minimumSalary != oldRange.Minimum || role.maximumSalary != oldRange.Maximum) continue;
            role.minimumSalary = newRange.minimumSalary;
            role.maximumSalary = newRange.maximumSalary;
        }

        foreach (var cost in saved.eventCosts ?? Array.Empty<SeriesEventCost>())
        {
            if (cost == null) continue;
            var oldCost = RaceExpenses.DefaultCosts.FirstOrDefault(item => item.id == cost.id);
            var newCost = defaults.eventCosts?.FirstOrDefault(item => item.id == cost.id);
            if (oldCost != null && newCost != null && cost.amount == oldCost.amount) cost.amount = newCost.amount;
        }

        UpdateLegacySupplierFees(saved.sharedEngineSuppliers, defaults.sharedEngineSuppliers,
            defaults.eventCosts?.FirstOrDefault(item => item.id == "engine")?.amount);
        foreach (var make in saved.manufacturers ?? Array.Empty<SeriesManufacturer>())
        {
            if (make == null) continue;
            var defaultMake = defaults.manufacturers?.FirstOrDefault(item => item.manufacturer == make.manufacturer);
            UpdateLegacySupplierFees(make.engineSuppliers, defaultMake?.engineSuppliers,
                defaults.eventCosts?.FirstOrDefault(item => item.id == "engine")?.amount);
        }
        saved.economyVersion = defaults.economyVersion;
    }

    private static void UpdateLegacySupplierFees(SeriesEngine[] saved, SeriesEngine[] defaults, int? updatedEngineFee)
    {
        if (saved == null || defaults == null || !updatedEngineFee.HasValue) return;
        foreach (var supplier in saved)
        {
            if (supplier == null || Array.IndexOf(LegacySupplierFees, supplier.maintenancePerRace) < 0) continue;
            var updated = defaults.FirstOrDefault(item => item.name == supplier.name);
            if (updated == null) continue;
            int expected = (int)Math.Round(supplier.maintenancePerRace * (updatedEngineFee.Value / 60_000d));
            if (expected == updated.maintenancePerRace) supplier.maintenancePerRace = updated.maintenancePerRace;
        }
    }

    private static readonly int[] LegacySupplierFees = { 44_500, 50_000, 53_500, 60_000 };

    internal static IEnumerable<RacingSeries> LoadCustomSeries()
    {
        if (!Directory.Exists(Folder))
            yield break;
        string[] paths;
        try { paths = Directory.GetFiles(Folder, "custom-*.json"); }
        catch (Exception ex) when (IsSettingsError(ex))
        {
            Debug.LogWarning($"Could not list custom series. {ex.Message}");
            yield break;
        }

        foreach (string path in paths)
        {
            RacingSeries series;
            try
            {
                string id = Path.GetFileNameWithoutExtension(path);
                if (!Guid.TryParseExact(id.Substring("custom-".Length), "N", out _))
                    continue;
                series = new RacingSeries();
                JsonUtility.FromJsonOverwrite(File.ReadAllText(path, Encoding.UTF8), series);
                if (series.id != id || Validate(series) != null)
                    continue;
                series.carModel = CarTypeDefinition.CanonicalModel(series.carModel);
                NormalizeManufacturerModels(series);
            }
            catch (Exception ex) when (IsSettingsError(ex))
            {
                Debug.LogWarning($"Could not read {Path.GetFileName(path)}. {ex.Message}");
                continue;
            }
            yield return series;
        }
    }

    internal static RacingSeries Create(RacingSeries template, string name)
    {
        var series = Clone(template);
        series.id = "custom-" + Guid.NewGuid().ToString("N");
        series.name = name;
        series.useCarsTourArtworkDefaults = template.UsesCarsTourArtworkDefaults;
        Save(series, series);
        RacingSeries.AddCustomSeries(series);
        return series;
    }

    public static RacingSeries Clone(RacingSeries series) => JsonUtility.FromJson<RacingSeries>(JsonUtility.ToJson(series));

    public static void Save(RacingSeries draft, RacingSeries series)
    {
        var saved = Clone(draft);
        saved.carModel = CarTypeDefinition.CanonicalModel(saved.carModel);
        NormalizeManufacturerModels(saved);
        saved.id = series.id;
        saved.name = draft.name?.Trim();
        saved.scheduleStartDate = saved.scheduleStartDate?.Trim();
        saved.scheduleEndDate = saved.scheduleEndDate?.Trim();
        saved.SeriesLevel = series.SeriesLevel;
        string error = Validate(saved);
        if (error != null)
            throw new ArgumentException(error);

        saved.logo = SaveArtwork(saved.logo);
        foreach (var background in saved.teamBackgrounds ?? Array.Empty<SeriesTeamBackground>())
            background.image = SaveArtwork(background.image);

        string json = JsonUtility.ToJson(saved, true);
        AtomicWrite(SettingsPath(series.id), temporary => File.WriteAllText(temporary, json, new UTF8Encoding(false)));
        // Career setup keeps a reference to each catalog entry.
        JsonUtility.FromJsonOverwrite(json, series);
    }

    private static void NormalizeManufacturerModels(RacingSeries series)
    {
        foreach (var make in series.manufacturers ?? Array.Empty<SeriesManufacturer>())
        {
            if (make == null)
                continue;

            make.carModel = string.IsNullOrWhiteSpace(make.carModel)
                ? null : CarTypeDefinition.CanonicalModel(make.carModel.Trim());
        }
    }

    internal static string DisplayNameError(string name) =>
        string.IsNullOrWhiteSpace(name) || name.Trim().Length > 25 || name.Any(char.IsControl)
            ? "Series name must contain 1 to 25 characters." : null;

    internal static (string Start, string End) ScheduleDateErrors(string start, string end)
    {
        if (string.IsNullOrWhiteSpace(start) && string.IsNullOrWhiteSpace(end))
            return (null, null);

        const string formatError = "Enter a month and day as MM/DD, valid every year.";
        bool startValid = SeasonSchedule.TryScheduleDate(start, 2001, out var first);
        bool endValid = SeasonSchedule.TryScheduleDate(end, 2001, out var last);
        string rangeError = startValid && endValid && last < first
            ? "End date cannot be before the start date."
            : startValid && endValid && (last - first).Days < 6
                ? "Allow at least seven days so every season has a Sunday."
                : null;
        return (startValid ? null : formatError, endValid ? rangeError : formatError);
    }

    internal static string CalendarDateError(string value) =>
        SeasonSchedule.TryScheduleDate(value, 2001, out _) ? null : "Enter a race date as MM/DD, valid every year.";

    internal static string CalendarTimeError(string value) =>
        !SessionWeatherForecast.TryStartTime(value, out var time) ||
        time < TimeSpan.FromHours(12) || time > TimeSpan.FromHours(21)
            ? "Enter a start time from 12:00 to 21:00." : null;

    internal static string CalendarError(RacingSeries series)
    {
        var races = series.calendarRaces;
        if (races?.Length > 0 != true)
            return null;
        foreach (var race in races)
        {
            if (race == null || race.trackId <= 0 || race.laps <= 0)
                return "Each calendar race needs a track and a positive lap count.";
            if (CalendarTimeError(race.startTime) is string timeError)
                return timeError;
            if (CalendarDateError(race.date) is string error)
                return error;
        }
        return races.Select(race => race.date.Trim()).Distinct(StringComparer.Ordinal).Count() == races.Length
            ? null : "Choose a different date for each race.";
    }

    public static string Validate(RacingSeries series)
    {
        if (DisplayNameError(series.name) is string nameError)
            return nameError;
        var dates = ScheduleDateErrors(series.scheduleStartDate, series.scheduleEndDate);
        if ((dates.Start ?? dates.End) is string dateError)
            return dateError;
        if (CalendarError(series) is string calendarError)
            return calendarError;
        if (series.calendarRaces?.Length > 0 != true && series.useSelectedTracks && (series.scheduleTrackIds?.Length > 0 != true ||
            series.scheduleTrackIds.Any(id => id <= 0) ||
            series.scheduleTrackIds.Distinct().Count() != series.scheduleTrackIds.Length))
            return "Select at least one track and include each track only once.";
        if (series.manufacturers?.Length > 0 != true)
            return "Choose at least one manufacturer.";
        if (series.manufacturers.Any(make => make == null || !ManufacturerCatalog.Contains(make.manufacturer)) ||
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
                string error = ValidateSuppliers(make.engineSuppliers, ManufacturerCatalog.Name(make.manufacturer));
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

        if (series.maximumConcurrentComponentJobs < 1 || series.startingTeamBalance < 0 ||
            series.standardRacePurse <= 0 || series.standardRacePurse > 1_000_000_000_000L ||
            series.racePayoutsByPosition?.Any(amount => amount <= 0) == true)
            return "Set valid starting funds, race payouts, and repair capacity.";

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
        return SaveArtworkBytes(ReadImage(location), extension, validateOnly) ?? location;
    }

    internal static string PasteArtwork() => SaveArtworkBytes(TrackImageClipboard.ReadPng(), ".png", false);

    private static string SaveArtworkBytes(byte[] data, string extension, bool validateOnly)
    {
        if (data == null || data.Length == 0)
            throw new ArgumentException("The copied image could not be read.");
        if (data.LongLength > MaximumImageBytes)
            throw new ArgumentException("Artwork must be 16 MB or smaller.");
        var sample = DecodeImage(data);
        if (Application.isPlaying)
            UnityEngine.Object.Destroy(sample);
        else
            UnityEngine.Object.DestroyImmediate(sample);
        if (validateOnly)
            return null;

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

