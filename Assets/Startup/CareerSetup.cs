using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

[Serializable]
internal sealed class RacingSeries
{
    public string id = string.Empty;
    public string name = string.Empty;
    public int SeriesLevel = int.MaxValue;
    public string carModel;
    public float maximumTrackLengthMiles;
    public SeriesManufacturer[] manufacturers;
    public SeriesEngine[] sharedEngineSuppliers;
    public long newChassisCost = UsedCarListing.NewChassisPrice;
    public SeriesEmployeeRole[] employeeRoles;
    public int maximumEmployees = 30;
    public long componentUpgradeCost = TeamCar.ComponentUpgradeCost;
    public int maximumComponentRepairCost = ComponentRepair.MaximumCost;
    public long minimumSponsorshipSigning = 300_000;
    public long maximumSponsorshipSigning = 2_000_000;
    public long minimumSponsorshipPerRace = 12_500;
    public long maximumSponsorshipPerRace = 60_000;
    public SeriesEventCost[] eventCosts;
    public bool usesStages = true;
    public int[] stagePercentages = ScheduledRace.DefaultStagePercentages();
    public string logo;
    public SeriesTeamBackground[] teamBackgrounds;

    private static RacingSeries[] catalog;
    private static readonly RacingSeries Defaults = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCatalog() => catalog = null;

    public static RacingSeries[] Catalog => catalog ??= Resources.LoadAll<TextAsset>("RacingSeries")
        .Select(Read).Where(series => series != null)
        .GroupBy(series => series.id, StringComparer.Ordinal).Select(group => group.First())
        .OrderBy(series => series.SeriesLevel).ThenBy(series => series.name).ToArray();

    public static string CanonicalId(string id) =>
        id == "tour-modified-detailed" ? "tour-modified" : id;

    public static RacingSeries ForId(string id) =>
        Catalog.FirstOrDefault(series => series.id == CanonicalId(id)) ??
        Catalog.FirstOrDefault(series => series.id == "nascar-cup") ?? Defaults;

    public CarManufacturer[] ManufacturerChoices => manufacturers?.Length > 0
        ? manufacturers.Select(make => make.manufacturer).Distinct().ToArray()
        : (CarManufacturer[])Enum.GetValues(typeof(CarManufacturer));

    public EmployeeRole[] StaffRoles => employeeRoles?.Length > 0
        ? employeeRoles.Select(role => role.role).Where(role => role != EmployeeRole.Driver).Distinct().ToArray()
        : employeeRoles != null ? Array.Empty<EmployeeRole>() :
            Enum.GetValues(typeof(EmployeeRole)).Cast<EmployeeRole>()
                .Where(role => role != EmployeeRole.Driver).ToArray();

    public SeriesEmployeeRole SalaryRange(EmployeeRole role) => employeeRoles?.FirstOrDefault(item => item.role == role);

    public EngineSupplier[] SuppliersFor(CarManufacturer manufacturer)
    {
        if (manufacturers?.Length > 0 && !manufacturers.Any(make => make.manufacturer == manufacturer))
            return Array.Empty<EngineSupplier>();
        var options = sharedEngineSuppliers?.Length > 0 ? sharedEngineSuppliers :
            manufacturers?.FirstOrDefault(make => make.manufacturer == manufacturer)?.engineSuppliers;
        return options?.Select(option => new EngineSupplier
        {
            name = option.name, manufacturer = manufacturer, power = option.power,
            reliability = option.reliability, maintenancePerRace = option.maintenancePerRace
        }).ToArray() ?? Array.Empty<EngineSupplier>();
    }

    public string BackgroundFor(string view)
    {
        string image = teamBackgrounds?.FirstOrDefault(item => item != null && item.view == view)?.image;
        if (!string.IsNullOrWhiteSpace(image))
            return image;
        var cup = Catalog.FirstOrDefault(series => series.id == "nascar-cup");
        return cup?.teamBackgrounds?.FirstOrDefault(item => item != null && item.view == view)?.image;
    }
    public Texture2D Logo => SeriesSettingsStore.LoadArtwork(logo);

    private static RacingSeries Read(TextAsset asset)
    {
        try
        {
            var series = new RacingSeries();
            JsonUtility.FromJsonOverwrite(asset.text, series);
            return string.IsNullOrWhiteSpace(series.id) || string.IsNullOrWhiteSpace(series.name)
                ? null : SeriesSettingsStore.ApplySaved(series);
        }
        catch (ArgumentException ex)
        {
            Debug.LogWarning($"Invalid series definition {asset.name}. {ex.Message}");
            return null;
        }
    }
}

[Serializable]
internal sealed class SeriesManufacturer
{
    public CarManufacturer manufacturer;
    public SeriesEngine[] engineSuppliers;
}

[Serializable]
internal sealed class SeriesEngine
{
    public string name;
    public float power;
    public float reliability;
    public int maintenancePerRace;
}

[Serializable]
internal sealed class SeriesEmployeeRole
{
    public EmployeeRole role;
    public int minimumSalary;
    public int maximumSalary;
}

[Serializable]
internal sealed class SeriesEventCost
{
    public string id;
    public string name;
    public int amount;
}

[Serializable]
internal sealed class SeriesTeamBackground
{
    public string view;
    public string image;
}

[Serializable]
internal sealed class Career
{
    public string teamName;
    public List<TeamMailMessage> inbox = new List<TeamMailMessage>();
    public List<string> deletedMailIds = new List<string>();
    public List<string> deletedSocialPostIds = new List<string>();
    public string socialStartDate;
    public string primaryTeamColor;
    public string secondaryTeamColor;
    public EngineSupplier engineSupplier;

    public RacingSeries SeriesRules => RacingSeries.ForId(seriesId);
    public EngineSupplier EngineSupplierFor(TeamCar car)
    {
        if (car == null || string.IsNullOrEmpty(seriesId))
            return null;
        var rules = SeriesRules;
        var choices = rules.SuppliersFor(car.manufacturer);
        var selected = choices.FirstOrDefault(choice => choice.name == engineSupplier?.name);
        if (selected != null && engineSupplier.manufacturer == car.manufacturer && rules.sharedEngineSuppliers?.Length != 1)
            return engineSupplier;
        return selected ?? choices.FirstOrDefault();
    }
    public bool CanHireEmployee => (employees?.Count ?? 0) + Math.Max(1, drivers?.Count ?? 0) < SeriesRules.maximumEmployees;
    public bool CanHireDriver => (employees?.Count ?? 0) + (drivers?.Count ?? 0) < SeriesRules.maximumEmployees;
    public string seriesId;
    public string seriesName;
    public SeasonSchedule schedule;
    public string currentDate;

    public static bool TryReadDate(string value, out DateTime date) =>
        DateTime.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out date);

    public List<MerchandiseProgram> merchandisePrograms;
    public List<MerchandiseMonth> merchandiseMonths;

    public Action SetDate(DateTime date)
    {
        bool hadDate = TryReadDate(currentDate, out var previous);
        currentDate = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        if (!hadDate || previous >= date.Date) return null;
        var undo = Merchandising.Advance(this, previous, date);
        var undoArrests = StaffArrest.SendDue(this, previous, date);
        ScenarioMail.SendDriverAppearances(this, previous, date);
        var undoEmbarrassment = ScenarioMail.SendDriverEmbarrassment(this, previous, date);
        var undoArgument = ScenarioMail.SendCrewArgument(this, previous, date);
        return () =>
        {
            undoArgument?.Invoke();
            undoEmbarrassment?.Invoke();
            undoArrests?.Invoke();
            undo?.Invoke();
        };
    }

    public Action SetSeasonStartDate() => SetDate(new DateTime(schedule.season, 1, 1));
    public long teamBalance;
    public SponsorContract primarySponsor;
    [SerializeField] private List<SponsorContract> sponsors;

    public SponsorContract PrimarySponsor =>
        primarySponsor?.racesRemaining > 0 && primarySponsor.offer != null ? primarySponsor : null;

    internal void ResetSponsorships() { primarySponsor = null; sponsors = null; }

    public bool MigrateSponsorship()
    {
        if (sponsors?.Count > 0 != true)
            return false;
        primarySponsor = PrimarySponsor ?? sponsors.Find(contract =>
            contract?.slot == 0 && contract.racesRemaining > 0 && contract.offer != null);
        sponsors = null;
        return true;
    }
    public bool financesInitialized;
    public List<SeasonSchedule> completedSeasons = new List<SeasonSchedule>();
    public int previousSeasonRankingYear;
    public int previousSeasonRank;
    public int previousSeasonFieldSize;
    public double pointsFundMultiplier = 1d;
    public List<SeasonPrizeAward> seasonPrizeAwards = new List<SeasonPrizeAward>();
    public List<TeamCar> cars = new List<TeamCar> { new TeamCar { number = 27 } };
    public List<TeamCar> rivalCars = new List<TeamCar>();
    public (Color32 Primary, Color32 Secondary) TeamColors
    {
        get
        {
            var fallback = cars?.FirstOrDefault()?.Paint ?? TrackPreview.TeamLivery(27);
            return (ColorUtility.TryParseHtmlString(primaryTeamColor, out var primary) ? (Color32)primary : fallback.HoodAndTrunk,
                ColorUtility.TryParseHtmlString(secondaryTeamColor, out var secondary) ? (Color32)secondary : fallback.RoofAndSides);
        }
    }
    [NonSerialized] public string savePath;

    internal Career CopyForTransaction()
    {
        var copy = JsonUtility.FromJson<Career>(JsonUtility.ToJson(this));
        // The save destination is runtime metadata and is omitted from the JSON copy.
        copy.savePath = savePath;
        return copy;
    }


    public List<UsedCarListing> usedCars = new List<UsedCarListing>();
    public string usedCarInventoryDate;
    public string usedCarManufacturerFilter;
    public string usedCarTypeFilter;
    public string usedCarMaxPriceFilter;
    public int usedCarSortOrder;
    public bool purchaseChassisUsedTab;
    public NewChassisConfiguration newChassisConfiguration;
    public List<Employee> employees;
    public List<TeamEmployees> rivalEmployees;
    public List<Employee> recruitCandidates;
    public List<Driver> driverCandidates;
    public bool playerRosterInitialized;
    public List<Driver> drivers;
    public Driver Driver => drivers?.FirstOrDefault();
    public long AnnualPayroll => employees.Sum(employee => employee.annualSalary) +
        (drivers?.Sum(driver => driver.annualSalary) ?? 0L);

    public bool InitializeEmployees(System.Random random)
    {
        bool changed = false;
        if (!playerRosterInitialized && (employees == null || employees.Count == 0))
        {
            employees = Employee.CreateStaff(random, SeriesRules);
            changed = true;
        }

        if (!playerRosterInitialized && (drivers == null || drivers.Count == 0))
        {
            drivers = MigrateDrivers(employees, random);
            if (drivers.Count == 0)
                drivers.Add(Driver.Create(random, null, series: SeriesRules));
            changed = true;
        }
        employees ??= new List<Employee>();
        drivers ??= new List<Driver>();
        changed |= !playerRosterInitialized;
        playerRosterInitialized = true;

        if (recruitCandidates != null)
        {
            var roles = SeriesRules.StaffRoles;
            changed |= recruitCandidates.RemoveAll(candidate => !roles.Contains(candidate.role)) > 0;
        }

        if (rivalEmployees == null || rivalEmployees.Count == 0)
        {
            rivalEmployees = new List<TeamEmployees>();
            var rivals = TrackPreview.CreateTimingEntries(cars[0], this).Skip(1).GroupBy(car => car.teamName);
            foreach (var team in rivals)
            {
                rivalEmployees.Add(new TeamEmployees
                {
                    teamName = team.Key,
                    drivers = team.Select(entry => Driver.Create(random, entry.driverName, series: SeriesRules)).ToList(),
                    employees = Employee.CreateStaff(random, SeriesRules)
                });
            }
            changed = true;
        }
        foreach (var team in rivalEmployees)
        {
            if (team.drivers == null || (team.drivers.Count == 0 &&
                team.employees?.Any(employee => employee.role == EmployeeRole.Driver) == true))
            {
                team.drivers = MigrateDrivers(team.employees, random);
                changed = true;
            }
        }

        foreach (var driver in drivers.Concat(driverCandidates ?? Enumerable.Empty<Driver>())
            .Concat(rivalEmployees.SelectMany(team => team.drivers ?? Enumerable.Empty<Driver>())))
        {
            changed |= driver.EnsureEthnicity();
            changed |= Employee.InitializeYearsExperience(driver.age, random, ref driver.yearsExperience, ref driver.experienceInitialized);
            changed |= Employee.InitializeMorale(random, ref driver.morale, ref driver.moraleInitialized);
        }
        foreach (var employee in employees.Concat(recruitCandidates ?? Enumerable.Empty<Employee>())
            .Concat(rivalEmployees.SelectMany(team => team.employees ?? Enumerable.Empty<Employee>())))
        {
            changed |= Employee.InitializeYearsExperience(employee.age, random, ref employee.yearsExperience, ref employee.experienceInitialized);
            changed |= Employee.InitializeMorale(random, ref employee.morale, ref employee.moraleInitialized);
        }

        return changed;
    }

    private static List<Driver> MigrateDrivers(List<Employee> staff, System.Random random)
    {
        if (staff == null)
            return new List<Driver>();

        var roster = staff.Where(employee => employee.role == EmployeeRole.Driver)
            .Select(employee => Driver.FromEmployee(employee, random)).ToList();
        staff.RemoveAll(employee => employee.role == EmployeeRole.Driver);
        return roster;
    }

    public Driver RivalDriver(string teamName, string driverName) =>
        rivalEmployees?.Find(team => team.teamName == teamName)?.drivers?.Find(driver => driver.FullName == driverName);

    public IEnumerable<TeamCar> AllCars => cars.Concat(rivalCars)
        .Concat(usedCars?.Select(listing => listing.car) ?? Enumerable.Empty<TeamCar>());

    public bool InitializeCars(System.Random random, bool randomizeRivals = false)
    {
        if (cars == null || cars.Count == 0)
            cars = new List<TeamCar> { new TeamCar { number = 27 } };

        bool changed = false;
        foreach (var car in cars)
            changed |= car.Initialize(random);

        rivalCars ??= new List<TeamCar>();
        for (int number = 1; number < TrackPreview.CarCount; number++)
        {
            var car = rivalCars.Find(item => item.number == number);
            if (car == null)
            {
                car = new TeamCar { number = number };
                rivalCars.Add(car);
            }
            changed |= car.Initialize(random);
        }
        return (randomizeRivals ? RandomizeRivalEntries(random) : AssignRivalLiveries(random)) || changed;
    }

    internal bool RandomizeRivalEntries(System.Random random)
    {
        var reserved = cars.Select(car => car.number).ToHashSet();
        var numbers = Enumerable.Range(1, 99).Where(number => !reserved.Contains(number)).ToList();
        if (numbers.Count < TrackPreview.CarCount - 1)
            numbers = Enumerable.Range(1, 99).Where(number => number != cars[0].number).ToList();
        var playerSchemes = cars.Select(car => car.paintScheme).ToHashSet();
        var allSchemes = CarPaintStyle.Schemes.ToArray();
        var rivalSchemes = allSchemes.Where(scheme => !playerSchemes.Contains(scheme)).ToArray();
        if (rivalSchemes.Length == 0) rivalSchemes = allSchemes;
        var remainingSchemes = new List<CarPaintScheme>();
        float hue = (float)random.NextDouble();
        string primary = null, secondary = null, trim = null;
        bool changed = false;
        for (int slot = 1; slot < TrackPreview.CarCount; slot++)
        {
            var car = rivalCars.Find(item => item.number == slot);
            if (car == null) continue;
            if ((slot - 1) % TrackPreview.RivalCarsPerTeam == 0 || primary == null)
            {
                primary = HexPaint(hue, .62f + (float)random.NextDouble() * .24f, .93f);
                secondary = HexPaint(hue + .27f + (float)random.NextDouble() * .22f, .72f, .72f);
                trim = HexPaint(hue + .13f, .34f, 1f);
                hue = Mathf.Repeat(hue + .618034f, 1f);
            }
            if (remainingSchemes.Count == 0) remainingSchemes.AddRange(rivalSchemes);
            car.rivalRaceNumber = TakeRandom(numbers, random);
            car.paintScheme = TakeRandom(remainingSchemes, random);
            car.primaryPaint = primary;
            car.secondaryPaint = secondary;
            car.trimPaint = trim;
            car.rivalLiveryAssigned = true;
            changed = true;
        }
        return changed;
    }

    private static string HexPaint(float hue, float saturation, float value) =>
        "#" + ColorUtility.ToHtmlStringRGB(Color.HSVToRGB(Mathf.Repeat(hue, 1f), saturation, value));

    private static T TakeRandom<T>(List<T> choices, System.Random random)
    {
        int index = random.Next(choices.Count);
        T choice = choices[index];
        choices.RemoveAt(index);
        return choice;
    }

    public bool AssignRivalLiveries(System.Random random)
    {
        bool changed = false;
        float hueStart = (float)random.NextDouble();
        var used = new HashSet<CarPaintScheme>((rivalCars ?? new List<TeamCar>())
            .Where(car => car.rivalLiveryAssigned).Select(car => car.paintScheme));
        if (cars?.Count > 0) used.Add(cars[0].paintScheme);
        var choices = CarPaintStyle.Schemes.Where(scheme => !used.Contains(scheme)).ToList();
        int index = 0;
        foreach (var car in rivalCars ?? new List<TeamCar>())
        {
            if (car.rivalLiveryAssigned) { index++; continue; }
            if (choices.Count == 0) choices = CarPaintStyle.Schemes.ToList();
            int selected = random.Next(choices.Count);
            car.paintScheme = choices[selected]; choices.RemoveAt(selected);
            float hue = Mathf.Repeat(hueStart + index * 0.61803399f, 1f);
            car.primaryPaint = "#" + ColorUtility.ToHtmlStringRGB(Color.HSVToRGB(hue, 0.75f, 0.92f));
            car.secondaryPaint = "#" + ColorUtility.ToHtmlStringRGB(Color.HSVToRGB(Mathf.Repeat(hue + 0.38f, 1f), 0.65f, 0.72f));
            car.trimPaint = "#" + ColorUtility.ToHtmlStringRGB(Color.HSVToRGB(Mathf.Repeat(hue + 0.17f, 1f), 0.35f, 1f));
            car.rivalLiveryAssigned = true;
            changed = true; index++;
        }
        return changed;
    }
}

internal enum CarComponent { Engine, Transmission, Brakes, Suspension, Aerodynamics }

internal static class CarComponentDisplay
{
    public static IReadOnlyList<CarComponent> Order { get; } = Array.AsReadOnly(new[]
    {
        CarComponent.Engine, CarComponent.Transmission, CarComponent.Suspension, CarComponent.Brakes, CarComponent.Aerodynamics
    });

    public static string Name(CarComponent component) =>
        component == CarComponent.Aerodynamics ? "Body" : component.ToString();
}

internal readonly struct ComponentCondition
{
    public const float RaceBlockingWear = 0.95f;
    public readonly float Wear;
    public readonly int Level;

    public ComponentCondition(float wear, int level)
    {
        Wear = wear;
        Level = Mathf.Clamp(level, 1, TeamCar.MaximumComponentLevel);
    }

    public float EffectiveValue => Level - Wear;
    public bool BlocksRaceEntry => Wear >= RaceBlockingWear;
}

[Serializable]
internal sealed class ComponentRepair
{
    public const int MaximumCost = 20_000;
    public CarComponent component;
    public string employeeName;
    public string employeeId;
    public string completionDate;
    public int durationHours;
    public int durationMinutes;
    public int cost;
    public bool isUpgrade;
    public int targetLevel;
    public string WorkDescription => isUpgrade ? $"Upgrade to level {targetLevel}" : "Repair";
    public string AssigneeDisplay => (string.IsNullOrWhiteSpace(employeeName) ? "Assigned mechanic" : employeeName) + " - " + WorkDescription;

    public static int CostForWear(float wear, int maximumCost = MaximumCost) => wear > 0f && maximumCost > 0
        ? Mathf.Max(1, Mathf.RoundToInt(Mathf.Clamp01(wear) * maximumCost))
        : 0;

    public bool IsAssignedTo(Employee employee) => string.IsNullOrEmpty(employeeId)
        ? employeeName == employee.FullName : employeeId == employee.repairAssigneeId;

    public static int HoursForWear(float wear)
    {
        float proportion = Mathf.InverseLerp(0.05f, 0.90f, wear);
        return Mathf.RoundToInt(Mathf.Lerp(24, 168, proportion));
    }

    public static int EstimateHours(float wear, float skill)
    {
        // A skill of 0.5 gives base speed. Maximum skill gives twice minimum speed.
        float speed = (2f + 2f * Mathf.Clamp01(skill)) / 3f;
        return Mathf.RoundToInt(HoursForWear(wear) / speed);
    }

    public static int ApplyMoraleToMinutes(int baseHours, float morale)
    {
        float penalty = morale >= .49f
            ? Mathf.Lerp(.01f, 0f, Mathf.InverseLerp(.49f, .50f, morale))
            : Mathf.Lerp(.25f, .01f, Mathf.InverseLerp(.10f, .49f, morale));
        return Mathf.RoundToInt(baseHours * 60 * (1f + penalty));
    }

    public static int EstimateMinutes(float wear, Employee employee, CarComponent component, string careerDate = null) =>
        ApplyMoraleToMinutes(EstimateHours(wear, employee.RepairSkill(component)),
            employee.moraleInitialized ? employee.EffectiveMorale(careerDate) : 1f);

    public static int CareerDaysForMinutes(int minutes) => (minutes + 1439) / 1440;

    private int ScheduledMinutes => durationMinutes > 0 ? durationMinutes : durationHours * 60;

    public int RemainingMinutes(DateTime currentDate)
    {
        if (!Career.TryReadDate(completionDate, out var completionDay) || currentDate.Date >= completionDay)
            return 0;

        // Recover the due time before the career-day rounding.
        int roundingMinutes = (1440 - ScheduledMinutes % 1440) % 1440;
        return (completionDay - currentDate.Date).Days * 1440 - roundingMinutes;
    }

    public static string FormatDuration(int minutes)
    {
        int days = minutes / 1440;
        int hours = minutes / 60 % 24;
        int remainder = minutes % 60;
        string duration = $"{days} {(days == 1 ? "day" : "days")}, {hours} {(hours == 1 ? "hour" : "hours")}";
        return remainder == 0 ? duration : $"{duration}, {remainder} {(remainder == 1 ? "minute" : "minutes")}";
    }

    public float RemainingFraction(DateTime date)
    {
        int duration = ScheduledMinutes > 0 ? ScheduledMinutes : 72 * 60; // Original jobs took three days.
        return Mathf.Clamp01(RemainingMinutes(date) / (float)duration);
    }
}

internal enum CarManufacturer { Chevrolet, Ford, Toyota, Dodge }

internal enum ChassisType { IntermediateTrack, RoadCourse, ShortTrack, SuperSpeedway }

[Serializable]
internal sealed class TeamCar
{
    public const int MaximumOwned = 7;
    public const int MaximumComponentLevel = 5;
    public const long ComponentUpgradeCost = 25_000;
    public const int ComponentUpgradeCooldownDays = 7;

    // Indexed by CarComponent. Missing entries in older saves have no cooldown.
    public int[] componentUpgradeDays;

    public bool TryGetNextUpgradeDate(CarComponent component, out DateTime date)
    {
        int index = (int)component;
        if (componentUpgradeDays == null || index >= componentUpgradeDays.Length || componentUpgradeDays[index] == 0)
        {
            date = default;
            return false;
        }

        int nextDay = componentUpgradeDays[index] - 1 + ComponentUpgradeCooldownDays;
        date = DateTime.MinValue.AddDays(Math.Min(nextDay, (DateTime.MaxValue.Date - DateTime.MinValue).Days));
        return true;
    }

    public void RecordComponentUpgrade(CarComponent component, DateTime date)
    {
        Array.Resize(ref componentUpgradeDays, Components.Length);
        componentUpgradeDays[(int)component] = (int)(date.Date - DateTime.MinValue).TotalDays + 1;
    }
    public static readonly CarComponent[] Components = (CarComponent[])Enum.GetValues(typeof(CarComponent));

    public int engineLevel = 1;
    public int transmissionLevel = 1;
    public int brakesLevel = 1;
    public int suspensionLevel = 1;
    public int aerodynamicsLevel = 1;
    public List<ComponentRepair> repairs = new List<ComponentRepair>();
    public int number;
    public string chassisNumber;
    [NonSerialized] public string modelResource;
    public CarManufacturer manufacturer = CarManufacturer.Chevrolet;
    public ChassisType chassisType = ChassisType.IntermediateTrack;
    public Setup setup = new Setup();
    public List<TrackSetup> trackSetups = new List<TrackSetup>();

    public IEnumerable<TrackSetup> SetupsFor(int trackId)
    {
        int currentId = TrackCatalog.CurrentTrackId(trackId);
        var saved = (trackSetups ?? Enumerable.Empty<TrackSetup>()).Where(item => item?.setup != null);
        var current = saved.Where(item => item.trackId == currentId);
        var retired = saved.Where(item => item.trackId != currentId &&
            TrackCatalog.CurrentTrackId(item.trackId) == currentId &&
            !current.Any(setup => setup.Matches(item.DisplayName)));
        return retired.Concat(current);
    }

    public string ChassisTypeName => DisplayChassisType(chassisType);

    public static string DisplayChassisType(ChassisType type) => type switch
    {
        ChassisType.RoadCourse => "Road Course",
        ChassisType.ShortTrack => "Short Track",
        ChassisType.SuperSpeedway => "SuperSpeedway",
        _ => "Intermediate"
    };

    // The rival's number field continues to identify its stable roster slot.
    public int rivalRaceNumber;
    public bool rivalLiveryAssigned;
    public string primaryPaint;
    public string secondaryPaint;
    public string trimPaint;
    public string numberPaint;
    public string numberFont;
    public bool numberBold = true;
    public bool numberItalic;
    public CarPaintScheme paintScheme;

    public CarPaintStyle PaintStyle
    {
        get
        {
            var colors = Paint;
            bool hasTrim = ColorUtility.TryParseHtmlString(trimPaint, out var trim);
            var scheme = Enum.IsDefined(typeof(CarPaintScheme), paintScheme) ? paintScheme : CarPaintScheme.Classic;
            return new CarPaintStyle(scheme, colors.HoodAndTrunk, colors.RoofAndSides,
                hasTrim ? (Color32)trim : new Color32(240, 240, 240, 255), hasTrim,
                ColorUtility.TryParseHtmlString(numberPaint, out var numberColor) ? (Color32?)numberColor : null,
                numberFont, numberBold, numberItalic);
        }
    }

    public (Color32 HoodAndTrunk, Color32 RoofAndSides) Paint
    {
        get
        {
            var defaults = TrackPreview.TeamLivery(number);
            return (ColorUtility.TryParseHtmlString(primaryPaint, out var primary) ? (Color32)primary : defaults.HoodAndTrunk,
                ColorUtility.TryParseHtmlString(secondaryPaint, out var secondary) ? (Color32)secondary : defaults.RoofAndSides);
        }
    }
    public bool componentWearInitialized;
    [Range(0f, 1f)] public float engineWear;
    [Range(0f, 1f)] public float transmissionWear;
    [Range(0f, 1f)] public float brakesWear;
    [Range(0f, 1f)] public float suspensionWear;
    [Range(0f, 1f)] public float aerodynamicsWear;
    [SerializeField, Range(0f, 100f)] private float damage;
    public float Damage
    {
        get => Mathf.Clamp(damage, 0f, 100f);
        set => damage = Mathf.Clamp(value, 0f, 100f);
    }

    public CarTire GetTire(int index) => index switch
    {
        0 => frontLeft,
        1 => frontRight,
        2 => rearLeft,
        3 => rearRight,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    public CarTire frontLeft;
    public CarTire frontRight;
    public CarTire rearLeft;
    public CarTire rearRight;

    internal void ResetTrackCondition()
    {
        Damage = 0f;
        frontLeft = new CarTire();
        frontRight = new CarTire();
        rearLeft = new CarTire();
        rearRight = new CarTire();
    }

    public bool Initialize(System.Random random)
    {
        bool changed = !componentWearInitialized ||
            frontLeft == null || frontRight == null || rearLeft == null || rearRight == null || setup == null;
        setup ??= new Setup();
        if (!componentWearInitialized)
        {
            engineWear = (float)random.NextDouble();
            transmissionWear = (float)random.NextDouble();
            brakesWear = (float)random.NextDouble();
            suspensionWear = (float)random.NextDouble();
            aerodynamicsWear = (float)random.NextDouble();
            componentWearInitialized = true;
        }
        foreach (var component in Components)
        {
            ref int level = ref ComponentLevel(component);
            int normalized = Mathf.Clamp(level, 1, MaximumComponentLevel);
            changed |= level != normalized;
            level = normalized;
        }
        frontLeft ??= new CarTire();
        frontRight ??= new CarTire();
        rearLeft ??= new CarTire();
        rearRight ??= new CarTire();
        return changed;
    }

    public ComponentCondition GetComponent(CarComponent component) =>
        new ComponentCondition(ComponentWear(component), ComponentLevel(component));

    public bool HasRaceBlockingWear => Components.Any(component => GetComponent(component).BlocksRaceEntry);

    public ref int ComponentLevel(CarComponent component)
    {
        switch (component)
        {
            case CarComponent.Engine: return ref engineLevel;
            case CarComponent.Transmission: return ref transmissionLevel;
            case CarComponent.Brakes: return ref brakesLevel;
            case CarComponent.Suspension: return ref suspensionLevel;
            case CarComponent.Aerodynamics: return ref aerodynamicsLevel;
            default: throw new ArgumentOutOfRangeException(nameof(component));
        }
    }

    public ref float ComponentWear(CarComponent component)
    {
        switch (component)
        {
            case CarComponent.Engine: return ref engineWear;
            case CarComponent.Transmission: return ref transmissionWear;
            case CarComponent.Brakes: return ref brakesWear;
            case CarComponent.Suspension: return ref suspensionWear;
            case CarComponent.Aerodynamics: return ref aerodynamicsWear;
            default: throw new ArgumentOutOfRangeException(nameof(component));
        }
    }

    public string DisplayName => $"No. {number} Stock Car";
    public string ChassisDisplayName => string.IsNullOrEmpty(chassisNumber) ? DisplayName : $"{DisplayName}  /  {chassisNumber}";
}

[Serializable]
internal sealed class CarTire
{
    public float coldPressurePsi; // Zero uses the chassis setup (legacy saves/new tires).
    [Range(0f, 1f)] public float wear = 0f;
    [Range(0f, 1f)] public float temperature = 0f;
}

[Serializable]
internal sealed class NewChassisConfiguration
{
    public bool selectionStateRecorded;
    public bool manufacturerSelected;
    public bool chassisTypeSelected;
    public CarManufacturer manufacturer;
    public ChassisType chassisType;
    public int[] componentLevels;
}

[Serializable]
internal sealed class UsedCarListing
{
    public const long NewChassisPrice = 100_000;
    public static long TradeInPrice(TeamCar car, RacingSeries series) =>
        (long)Math.Round(0.8d * ConditionAdjustedValue(car, series), MidpointRounding.AwayFromZero);

    public static double ConditionAdjustedValue(TeamCar car, RacingSeries series)
    {
        double condition = TeamCar.Components.Average(component => 1d - Mathf.Clamp01(car.ComponentWear(component)));
        double upgradeValue = TeamCar.Components.Sum(component =>
            (double)(car.GetComponent(component).Level - 1) * series.componentUpgradeCost *
            (1d - Mathf.Clamp01(car.ComponentWear(component))));
        return series.newChassisCost * condition + upgradeValue;
    }
    public TeamCar car;
    public long price;
    public bool componentLevelsAssigned;
    public bool liveryAssigned;

    public void AssignLivery(System.Random random)
    {
        if (liveryAssigned) return;
        var schemes = CarPaintStyle.Schemes;
        car.paintScheme = schemes[random.Next(schemes.Count)];
        float hue = (float)random.NextDouble();
        car.primaryPaint = "#" + ColorUtility.ToHtmlStringRGB(Color.HSVToRGB(hue, 0.75f, 0.92f));
        car.secondaryPaint = "#" + ColorUtility.ToHtmlStringRGB(Color.HSVToRGB(Mathf.Repeat(hue + 0.38f, 1f), 0.65f, 0.72f));
        car.trimPaint = "#" + ColorUtility.ToHtmlStringRGB(Color.HSVToRGB(Mathf.Repeat(hue + 0.17f, 1f), 0.35f, 1f));
        liveryAssigned = true;
    }


    public void AssignComponentLevels(System.Random random, RacingSeries series)
    {
        if (componentLevelsAssigned) return;
        foreach (var component in TeamCar.Components)
            car.ComponentLevel(component) = random.Next(1, TeamCar.MaximumComponentLevel + 1);
        price = (long)Math.Round(ConditionAdjustedValue(car, series), MidpointRounding.AwayFromZero) + random.Next(5000, 15001);
        componentLevelsAssigned = true;
    }

    public static UsedCarListing Create(int carNumber, System.Random random, RacingSeries series)
    {
        var manufacturers = series.ManufacturerChoices;
        var chassisTypes = (ChassisType[])Enum.GetValues(typeof(ChassisType));
        var car = new TeamCar
        {
            number = carNumber,
            manufacturer = manufacturers[random.Next(manufacturers.Length)],
            chassisType = chassisTypes[random.Next(chassisTypes.Length)]
        };
        car.Initialize(random);
        var listing = new UsedCarListing { car = car };
        listing.AssignComponentLevels(random, series);
        listing.AssignLivery(random);
        return listing;
    }
}

internal enum EmployeeRole
{
    Driver, CrewChief, CarChief, RaceEngineer, BodySpecialist, EngineSpecialist, BrakeSpecialist,
    TransmissionSpecialist, SuspensionSpecialist, Fabricator, GeneralMechanic
}

[Serializable]
internal sealed class TeamEmployees
{
    public string teamName;
    public List<Employee> employees;
    public List<Driver> drivers;
}

[Serializable]
internal sealed class Employee
{
    private static readonly (string Name, int Salary)[] Roles =
    {
        ("Driver", 100_000), ("Crew Chief", 250_000), ("Car Chief", 175_000), ("Race Engineer", 165_000),
        ("Body Specialist", 150_000), ("Engine Specialist", 150_000), ("Brake Specialist", 125_000),
        ("Transmission Specialist", 125_000), ("Suspension Specialist", 125_000),
        ("Fabricator", 100_000), ("General Mechanic", 100_000)
    };
    private static readonly string[] FirstNames =
        { "Alex", "Bailey", "Cameron", "Drew", "Elliot", "Harper", "Jamie", "Jordan", "Morgan", "Parker", "Quinn", "Rowan", "Taylor" };
    private static readonly string[] LastNames =
        { "Bennett", "Brooks", "Carter", "Cole", "Davis", "Foster", "Grant", "Hayes", "Kim", "Lewis", "Miller", "Morgan", "Reed", "Shaw", "Turner" };
    private static readonly (string City, string State)[] Hometowns =
    {
        ("Concord", "NC"), ("Greenville", "SC"), ("Knoxville", "TN"), ("Richmond", "VA"), ("Macon", "GA"),
        ("Daytona Beach", "FL"), ("Fort Worth", "TX"), ("Phoenix", "AZ"), ("Indianapolis", "IN"), ("Lansing", "MI"),
        ("Cincinnati", "OH"), ("Reno", "NV"), ("Sacramento", "CA"), ("Reading", "PA"), ("Manchester", "NH")
    };
    private static readonly string[] Genders = { "Woman", "Man", "Nonbinary" };

    public EmployeeRole role;
    public string repairAssigneeId;
    public StaffArrest arrest;
    public string argumentMoraleUntil;

    public float EffectiveMorale(string careerDate) =>
        Career.TryReadDate(argumentMoraleUntil, out var until) &&
        Career.TryReadDate(careerDate, out var today) && today < until
            ? Mathf.Max(0f, morale - .05f) : morale;
    public string firstName;
    public string lastName;
    public int age;
    public int yearsExperience;
    public bool experienceInitialized;
    [Range(0f, 1f)] public float morale;
    public bool moraleInitialized;
    public string gender;
    public string hometownCity;
    public string hometownState;
    public long annualSalary;
    public int recruitLiveryNumber = 0;
    [Range(0f, 1f)] public float engines;
    [Range(0f, 1f)] public float suspensions;
    [Range(0f, 1f)] public float transmissions;
    [Range(0f, 1f)] public float bodywork;
    [Range(0f, 1f)] public float brakes;

    public string FullName => (firstName + " " + lastName).Trim();
    public string RoleName => NameOf(role);

    public static bool InitializeYearsExperience(int age, System.Random random, ref int years, ref bool initialized)
    {
        if (initialized)
            return false;
        years = GenerateYearsExperience(age, random);
        initialized = true;
        return true;
    }

    public static int GenerateYearsExperience(int age, System.Random random) =>
        Math.Max(0, age - 15 - random.Next(6));

    public static bool InitializeMorale(System.Random random, ref float morale, ref bool initialized)
    {
        if (initialized)
            return false;
        morale = 0.3f + (float)random.NextDouble() * 0.5f;
        initialized = true;
        return true;
    }

    public float RepairSkill(CarComponent component) => component switch
    {
        CarComponent.Engine => engines,
        CarComponent.Transmission => transmissions,
        CarComponent.Brakes => brakes,
        CarComponent.Suspension => suspensions,
        CarComponent.Aerodynamics => bodywork,
        _ => throw new ArgumentOutOfRangeException(nameof(component))
    };

    public static string NameOf(EmployeeRole role) => Roles[(int)role].Name;

    public static List<Employee> CreateStaff(System.Random random, RacingSeries series) => series.StaffRoles
        .Take(Math.Max(0, series.maximumEmployees - 1)).Select(role => Create(role, random, series: series)).ToList();

    public static Employee Create(EmployeeRole role, System.Random random, string name = null,
        string city = null, string state = null, RacingSeries series = null)
    {
        var hometown = Hometowns[random.Next(Hometowns.Length)];
        var parts = name?.Trim().Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
        int baseSalary = Roles[(int)role].Salary;
        int salarySpread = baseSalary * 15 / 100;
        var range = series?.SalaryRange(role);
        int minimum = range?.minimumSalary ?? baseSalary - salarySpread;
        int maximum = Math.Max(minimum, range?.maximumSalary ?? baseSalary + salarySpread);
        var employee = new Employee
        {
            role = role,
            firstName = parts?.Length > 0 ? parts[0] : FirstNames[random.Next(FirstNames.Length)],
            lastName = parts?.Length > 1 ? parts[1] : name == null ? LastNames[random.Next(LastNames.Length)] : string.Empty,
            age = random.Next(20, 66),
            gender = Genders[random.Next(Genders.Length)],
            hometownCity = string.IsNullOrWhiteSpace(city) ? hometown.City : city,
            hometownState = string.IsNullOrWhiteSpace(state) ? hometown.State : state,
            annualSalary = random.Next(minimum, maximum + 1),
            engines = (float)random.NextDouble(),
            suspensions = (float)random.NextDouble(),
            transmissions = (float)random.NextDouble(),
            bodywork = (float)random.NextDouble(),
            brakes = (float)random.NextDouble()
        };
        InitializeYearsExperience(employee.age, random, ref employee.yearsExperience, ref employee.experienceInitialized);
        InitializeMorale(random, ref employee.morale, ref employee.moraleInitialized);
        return employee;
    }
}

internal sealed class CareerSetup
{
    private readonly RacingSeries[] series;
    private Career transitionSource;
    private string transitionMailId;
    private int transitionYear;
    public bool IsSeriesChangeSetup => transitionSource != null;
    private readonly TextField team = new TextField("Team name") { maxLength = 25 };
    private int selectedSeries = -1;
    private readonly Button manufacturerSelector;
    private readonly Button supplierSelector;
    private readonly VisualElement frame;
    private string savedDraft;
    private bool validationAttempted;
    private int configuredSeries = -1;
    private readonly Button seriesSelector;
    private readonly Button nextButton;
    private readonly Button saveButton;
    private readonly Dictionary<VisualElement, Action<string>> validationMessages = new();
    private readonly CarThumbnails seriesThumbnails = new CarThumbnails();
    private readonly Label status = new Label();
    private readonly VisualElement playerForm = new VisualElement();
    private readonly VisualElement teamForm = new VisualElement();
    private readonly Button backButton;
    private readonly Label heading = new Label("New Career") { style = { fontSize = 24 } };
    private readonly Label teamStatus = new Label();
    private readonly Label engineDetails = new Label();
    private readonly DropdownField manufacturer;
    private readonly DropdownField supplier = new DropdownField("Engine supplier");
    private readonly IntegerField carNumber = new IntegerField("Car number");
    private readonly Button schemeSelector;
    private CarPaintStyle draftPaint;
    private readonly VisualElement carSetupBody = new VisualElement();
    private readonly VisualElement carPreviewPanel = new VisualElement();
    private readonly VisualElement paintControls = new VisualElement();
    private readonly VisualElement inlinePaintControls = new VisualElement();
    private readonly Label paintStatus = SettingsFormStyle.Label("", "settings-validation-summary");
    private CarManufacturer[] manufacturers = (CarManufacturer[])Enum.GetValues(typeof(CarManufacturer));
    private readonly IReadOnlyList<CarPaintScheme> schemes = CarPaintStyle.Schemes;
    private readonly Image seriesLogo = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
    private EngineSupplier[] shownSuppliers;
    private readonly CarThumbnails previewThumbnails = CarThumbnails.CreateLargePreview();
    private readonly Image carPreview;
    private readonly Action<Career> start;
    private readonly TrackCatalog catalog;
    private readonly System.Random scheduleRandom = new System.Random();
    private readonly string folder = Path.Combine(Application.persistentDataPath, "Careers");

    public VisualElement View { get; } = new VisualElement();

    public CareerSetup(TrackCatalog catalog, Action<Career> start, Action back)
    {
        this.start = start;
        this.catalog = catalog;
        series = RacingSeries.Catalog;
        SettingsFormStyle.Apply(View);
        frame = new VisualElement();
        frame.AddToClassList("settings-frame");
        View.AddToClassList("new-career-form");
        frame.style.maxWidth = 720;
        frame.style.paddingTop = 12;
        frame.style.paddingBottom = 8;
        frame.style.minHeight = 0;
        frame.style.maxHeight = new Length(100, LengthUnit.Percent);
        View.Add(frame);
        var header = new VisualElement();
        header.AddToClassList("settings-header");
        header.Add(heading);
        backButton = Button("Back to Startup", () =>
        {
            if (teamForm.style.display == DisplayStyle.Flex)
                ShowTeamForm(false);
            else
                RequestLeave(back);
        });
        heading.AddToClassList("settings-title");
        frame.Add(header);

        playerForm.AddToClassList("settings-scroll");
        playerForm.AddToClassList("settings-group");
        playerForm.AddToClassList("career-player-form");
        playerForm.style.flexShrink = 1;
        playerForm.style.minHeight = 0;
        var identityRow = new VisualElement();
        identityRow.AddToClassList("settings-row");
        identityRow.Add(team);
        playerForm.Add(identityRow);
        seriesSelector = Button("Choose a racing series", () => OpenSeriesPicker(frame));
        seriesSelector.tooltip = "Choose the racing series and car type for your new career.";
        var seriesContainer = new VisualElement();
        seriesContainer.AddToClassList("settings-field");
        seriesContainer.Add(SettingsFormStyle.Label("Racing series", "settings-note"));
        seriesContainer.Add(seriesSelector);
        playerForm.Add(seriesContainer);
        validationMessages.Add(seriesSelector, SettingsFormStyle.InlineError(seriesContainer));
        status.style.whiteSpace = WhiteSpace.Normal;
        status.style.flexShrink = 0;
        playerForm.Add(status);
        nextButton = Button("Next", () =>
        {
            validationAttempted = true;
            RefreshValidation();
            if (ReadPlayerInfo() != null)
                ShowTeamForm(true);
        });
        nextButton.SetEnabled(false);
        frame.RegisterCallback<PointerDownEvent>(evt =>
        {
            var navigation = teamForm.style.display == DisplayStyle.Flex ? saveButton : nextButton;
            if (navigation == null || !navigation.worldBound.Contains(evt.position))
                return;
            validationAttempted = true;
            RefreshValidation();
        }, TrickleDown.TrickleDown);
        var playerFooter = new VisualElement { name = "careerPlayerActions" };
        playerFooter.AddToClassList("settings-footer");
        playerFooter.Add(nextButton);
        frame.Add(playerForm);

        teamForm.AddToClassList("settings-scroll");
        teamForm.AddToClassList("settings-group");
        teamForm.style.flexShrink = 1;
        teamForm.style.minHeight = 0;
        seriesLogo.style.height = 28;
        carSetupBody.AddToClassList("career-car-setup-body");
        carPreviewPanel.AddToClassList("career-car-preview-panel");
        paintControls.AddToClassList("career-car-paint-controls");
        teamForm.Add(carSetupBody);
        carSetupBody.Add(carPreviewPanel);
        carSetupBody.Add(paintControls);
        carPreviewPanel.Add(seriesLogo);
        carPreview = previewThumbnails.CreateOrbitPreview();
        carPreviewPanel.Add(carPreview);
        manufacturer = new DropdownField("Manufacturer", manufacturers.Select(make => make.ToString()).ToList(), 0);
        manufacturerSelector = AddSelectionControl("Manufacturer", () =>
            OpenChoicePicker("Choose a manufacturer", manufacturer.choices,
                index => { manufacturer.index = index; RefreshSuppliers(); RefreshValidation(); UpdateCarPreview(); }));
        supplierSelector = AddSelectionControl("Engine supplier", () =>
            OpenChoicePicker("Choose an engine supplier", supplier.choices,
                index => { supplier.index = index; RefreshEngineDetails(); RefreshValidation(); },
                index => shownSuppliers != null && index < shownSuppliers.Length
                    ? $"Power {shownSuppliers[index].power:F2} - Reliability {shownSuppliers[index].reliability:F2} - {RaceFinances.Dollars(shownSuppliers[index].maintenancePerRace)} per race"
                    : ""));
        engineDetails.style.whiteSpace = WhiteSpace.Normal;
        var carOptions = new VisualElement();
        carOptions.AddToClassList("settings-row");
        carOptions.Add(carNumber);
        paintControls.Add(carOptions);
        schemeSelector = Button(string.Empty, OpenPaintSchemePicker);
        schemeSelector.tooltip = "Edit the car paint scheme, colors, number color, font, bold, and italic.";
        schemeSelector.style.alignSelf = Align.FlexEnd;
        schemeSelector.style.marginBottom = 3;
        carOptions.Add(schemeSelector);
        paintControls.Add(inlinePaintControls);
        paintControls.Add(paintStatus);
        paintStatus.style.minHeight = 20;
        paintStatus.style.flexShrink = 0;
        teamStatus.style.whiteSpace = WhiteSpace.Normal;
        teamStatus.style.minHeight = 20;
        teamStatus.style.flexShrink = 0;
        teamForm.Add(teamStatus);
        var actions = new VisualElement { name = "careerCarActions" };
        actions.AddToClassList("settings-footer");
        actions.Add(Button("Cancel", () => RequestLeave(back)));
        saveButton = Button("Start Career", () => Save());
        actions.Add(saveButton);
        frame.Add(teamForm);
        var footer = new VisualElement();
        footer.AddToClassList("settings-footer");
        footer.Add(backButton);
        footer.Add(playerFooter);
        footer.Add(actions);
        frame.Add(footer);
        playerFooter.RemoveFromClassList("settings-footer");
        actions.RemoveFromClassList("settings-footer");
        actions.style.flexDirection = FlexDirection.Row;
        playerFooter.style.flexDirection = FlexDirection.Row;
        View.Query<TextField>().ForEach(field => SettingsFormStyle.StyleField(field));
        View.Query<DropdownField>().ForEach(field => SettingsFormStyle.StyleField(field));
        SettingsFormStyle.StyleField(carNumber);
        carNumber.style.flexGrow = 0;
        carNumber.style.alignSelf = Align.FlexStart;
        carNumber.style.width = 150;
        var numberInput = carNumber.Q(className: "unity-base-field__input");
        numberInput.style.width = 64;
        numberInput.style.flexGrow = 0;
        numberInput.style.alignSelf = Align.FlexStart;

        manufacturer.tooltip = "Choose the manufacturer of your team car.";
        supplier.tooltip = "Choose who supplies your engine, including its power, reliability, and maintenance cost.";
        manufacturer.RegisterValueChangedCallback(_ => RefreshSuppliers());
        supplier.RegisterValueChangedCallback(_ => RefreshEngineDetails());
        carNumber.RegisterValueChangedCallback(_ => UpdateCarPreview());
        teamForm.style.display = DisplayStyle.None;
        AddValidation(team, () => SettingsFormStyle.TextError(team.value) ?? (team.value.Length > 25 ? "Use 25 characters or fewer." : null));
        AddValidation(manufacturer, () => manufacturer.index < 0 ? "Choose a manufacturer." : null);
        AddValidation(supplier, () => shownSuppliers == null || supplier.index < 0 ||
            supplier.index >= shownSuppliers.Length ? "Choose an engine supplier." : null);
        AddValidation(carNumber, () => carNumber.value < 1 || carNumber.value > 99 ?
            "Enter a car number from 1 to 99." : null);
        RefreshValidation();
    }

    private Button AddSelectionControl(string label, Action choose)
    {
        var container = new VisualElement();
        container.AddToClassList("settings-field");
        container.Add(SettingsFormStyle.Label(label, "settings-note"));
        var button = SettingsFormStyle.Button("Choose " + label.ToLowerInvariant(), choose);
        button.tooltip = "Open the available " + label.ToLowerInvariant() + " options.";
        container.Add(button);
        playerForm.Add(container);
        return button;
    }

    private void OpenPaintSchemePicker()
    {
        var car = ReadCarDraft();
        if (car == null)
        {
            validationAttempted = true;
            RefreshValidation();
            return;
        }
        CarPaintEditor.Open(View, car, Array.Empty<string>(), updated =>
        {
            draftPaint = updated.PaintStyle;
            RefreshPaintPreview();
            return null;
        }, careerDraft: true);
    }

    private void RefreshPaintPreview()
    {
        schemeSelector.text = $"Edit Paint Scheme   -   {CarPaintStyle.SchemeName(draftPaint.Scheme)}";
        UpdateCarPreview();
    }

    private static Color32 PaintColor(string value, string fallback) =>
        ColorUtility.TryParseHtmlString(value, out var color) ? color :
        ColorUtility.TryParseHtmlString(fallback, out color) ? color : Color.white;

    private void OpenChoicePicker(string title, IList<string> choices, Action<int> select, Func<int, string> describe = null)
    {
        var overlay = new VisualElement();
        overlay.AddToClassList("settings-confirmation-overlay");
        var dialog = CreateDialog(overlay, title);
        var list = new ScrollView { verticalScrollerVisibility = ScrollerVisibility.Hidden, horizontalScrollerVisibility = ScrollerVisibility.Hidden };
        list.AddToClassList("title-career-list");
        list.style.maxHeight = 380;
        dialog.Add(list);
        for (int i = 0; i < choices.Count; i++)
        {
            int index = i;
            var row = new VisualElement();
            row.AddToClassList("title-career-row");
            var choice = SettingsFormStyle.Button("", () => { select(index); Close(); });
            choice.AddToClassList("title-career-load");
            choice.tooltip = "Select " + choices[index] + ".";
            var details = new VisualElement { pickingMode = PickingMode.Ignore };
            details.AddToClassList("title-career-details");
            details.Add(SettingsFormStyle.Label(choices[index], "title-career-name"));
            if (describe != null)
                details.Add(SettingsFormStyle.Label(describe(index), "title-career-detail"));
            choice.Add(details);
            row.Add(choice);
            list.Add(row);
        }
        if (choices.Count == 0)
            list.Add(SettingsFormStyle.Label("No options are configured for this series.", "settings-note"));
        var cancel = SettingsFormStyle.Button("Cancel", Close);
        dialog.Add(cancel);
        overlay.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Escape) return;
            Close();
            evt.StopPropagation();
        });
        frame.SetEnabled(false);
        View.Add(overlay);
        cancel.Focus();

        void Close()
        {
            overlay.RemoveFromHierarchy();
            frame.SetEnabled(true);
        }
    }

    private static VisualElement CreateDialog(VisualElement overlay, string title)
    {
        var dialog = new VisualElement();
        dialog.AddToClassList("settings-confirmation-dialog");
        dialog.style.maxHeight = new Length(90, LengthUnit.Percent);
        dialog.style.backgroundColor = (Color)new Color32(16, 21, 30, 255);
        var trim = new StyleColor(new Color(0, 155f / 255f, 221f / 255f));
        dialog.style.borderTopColor = dialog.style.borderBottomColor =
            dialog.style.borderLeftColor = dialog.style.borderRightColor = trim;
        dialog.Add(SettingsFormStyle.Label(title, "settings-group-heading"));
        overlay.Add(dialog);
        return dialog;
    }

    private string DraftState() => string.Join("|", new[]
    {
        team.value, selectedSeries.ToString(), manufacturer.value, supplier.value,
        carNumber.value.ToString(), draftPaint.Scheme.ToString(), draftPaint.Primary.ToString(),
        draftPaint.Secondary.ToString(), draftPaint.Trim.ToString(), draftPaint.NumberColor.ToString(),
        draftPaint.NumberFont, draftPaint.NumberBold.ToString(), draftPaint.NumberItalic.ToString()
    });

    private void RequestLeave(Action leave)
    {
        if (savedDraft == null || savedDraft == DraftState())
        {
            leave();
            return;
        }

        RefreshValidation();
        var overlay = new VisualElement();
        overlay.AddToClassList("settings-confirmation-overlay");
        var dialog = CreateDialog(overlay, "Unsaved changes");
        dialog.Add(SettingsFormStyle.Label("Save or discard your setup changes before going back.", "settings-note"));
        var message = SettingsFormStyle.Label("", "settings-validation-summary");
        dialog.Add(message);
        var actions = new VisualElement();
        actions.AddToClassList("settings-row");
        actions.AddToClassList("settings-confirmation-actions");
        var keep = SettingsFormStyle.Button("Keep Editing", Close);
        actions.Add(keep);
        actions.Add(SettingsFormStyle.Button("Discard and Go Back", () =>
        {
            Close();
            if (teamForm.style.display == DisplayStyle.Flex && !IsSeriesChangeSetup)
                Open();
            leave();
        }));
        var saveBack = SettingsFormStyle.Button("Save and Go Back", () =>
        {
            if (!saveButton.enabledSelf) return;
            Save(_ =>
            {
                Close();
                leave();
            });
            SettingsFormStyle.SetValidationMessage(message, teamStatus.text, !string.IsNullOrEmpty(teamStatus.text));
        }, primary: true);
        saveBack.SetEnabled(saveButton.enabledSelf);
        SettingsFormStyle.SetValidationMessage(message,
            saveButton.enabledSelf ? null : "Fix the required fields before saving.", !saveButton.enabledSelf);
        actions.Add(saveBack);
        dialog.Add(actions);
        frame.SetEnabled(false);
        View.Add(overlay);
        keep.Focus();
        overlay.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Escape) return;
            Close();
            evt.StopPropagation();
        });

        void Close()
        {
            overlay.RemoveFromHierarchy();
            frame.SetEnabled(true);
            backButton.Focus();
        }
    }

    private readonly Dictionary<VisualElement, Func<string>> validators = new();

    private void AddValidation<TValue>(BaseField<TValue> field, Func<string> validate)
    {
        validators.Add(field, validate);
        validationMessages.Add(field, SettingsFormStyle.InlineError((VisualElement)field == manufacturer ? manufacturerSelector.parent : (VisualElement)field == supplier ? supplierSelector.parent : field));
        field.RegisterValueChangedCallback(_ => RefreshValidation());
    }

    private void RefreshValidation()
    {
        bool identityValid = true;
        bool carValid = true;
        foreach (var field in validators)
        {
            string error = field.Value();
            validationMessages[field.Key](validationAttempted ? error : null);
            if (field.Key == team || field.Key == manufacturer || field.Key == supplier)
                identityValid &= error == null;
            else
                carValid &= error == null;
        }
        string seriesError = selectedSeries < 0 || selectedSeries >= series.Length ? "Choose a racing series." :
            IsSeriesChangeSetup && series[selectedSeries].id == RacingSeries.CanonicalId(transitionSource.seriesId)
                ? "Choose a different series, or return to the email and choose Stay." : null;
        validationMessages[seriesSelector](validationAttempted ? seriesError : null);
        identityValid &= seriesError == null;
        nextButton.SetEnabled(identityValid);
        saveButton.SetEnabled(identityValid && carValid);
        manufacturerSelector.SetEnabled(selectedSeries >= 0 && manufacturers.Length > 0);
        supplierSelector.SetEnabled(selectedSeries >= 0 && shownSuppliers?.Length > 0);
        supplierSelector.parent.style.display = shownSuppliers?.Length == 1 ? DisplayStyle.None : DisplayStyle.Flex;
        manufacturerSelector.text = manufacturer.index >= 0 ? manufacturer.value : "Choose a manufacturer";
        supplierSelector.text = supplier.index >= 0 ? supplier.value : "Choose an engine supplier";
    }

    private void OpenSeriesPicker(VisualElement frame)
    {
        var overlay = new VisualElement { name = "newCareerSeriesPicker" };
        overlay.AddToClassList("settings-confirmation-overlay");
        var dialog = new VisualElement();
        dialog.AddToClassList("settings-confirmation-dialog");
        dialog.style.width = 760;
        dialog.style.maxHeight = new Length(90, LengthUnit.Percent);
        dialog.style.backgroundColor = (Color)new Color32(16, 21, 30, 255);
        dialog.style.borderTopColor = dialog.style.borderBottomColor =
            dialog.style.borderLeftColor = dialog.style.borderRightColor = (Color)new Color32(0, 155, 221, 255);
        overlay.Add(dialog);
        dialog.Add(SettingsFormStyle.Label("Choose a racing series", "settings-group-heading"));
        var list = new ScrollView { verticalScrollerVisibility = ScrollerVisibility.Hidden, horizontalScrollerVisibility = ScrollerVisibility.Hidden };
        list.AddToClassList("title-career-list");
        list.style.maxHeight = 450;
        dialog.Add(list);
        foreach (var rules in series)
        {
            int index = Array.IndexOf(series, rules);
            var row = new VisualElement();
            row.AddToClassList("title-career-row");
            var choose = SettingsFormStyle.Button("", () =>
            {
                selectedSeries = index;
                ConfigureSeries();
                seriesSelector.text = rules.name;
                RefreshValidation();
                Close();
            });
            choose.AddToClassList("title-career-load");
            choose.tooltip = $"Start a career in {rules.name}.";
            var car = new TeamCar
            {
                number = 27,
                modelResource = rules.carModel,
                primaryPaint = "#009BDD",
                secondaryPaint = "#F5F5F5",
                trimPaint = "#202833",
                paintScheme = schemes[0]
            };
            var picture = new Image
            {
                image = seriesThumbnails.Get(car, Array.Empty<string>(), cacheScope: rules.id, transparentBackground: true),
                scaleMode = ScaleMode.ScaleToFit,
                pickingMode = PickingMode.Ignore
            };
            picture.AddToClassList("title-career-car");
            choose.Add(picture);
            var details = new VisualElement { pickingMode = PickingMode.Ignore };
            details.AddToClassList("title-career-details");
            var name = SettingsFormStyle.Label(rules.name, "title-career-name");
            name.pickingMode = PickingMode.Ignore;
            details.Add(name);
            var model = SettingsFormStyle.Label(
                CarTypeDefinition.ForModel(rules.carModel).displayName, "title-career-detail");
            model.pickingMode = PickingMode.Ignore;
            details.Add(model);
            choose.Add(details);
            row.Add(choose);
            list.Add(row);
        }
        var footer = new VisualElement();
        footer.AddToClassList("settings-footer");
        footer.style.justifyContent = Justify.FlexEnd;
        footer.Add(SettingsFormStyle.Button("Cancel", Close));
        dialog.Add(footer);
        overlay.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Escape) return;
            Close();
            evt.StopPropagation();
        });
        frame.SetEnabled(false);
        View.Add(overlay);
        list.Query<Button>().First()?.Focus();

        void Close()
        {
            overlay.RemoveFromHierarchy();
            frame.SetEnabled(true);
            seriesThumbnails.Dispose();
            seriesSelector.Focus();
        }
    }

    private CarPaintScheme CarPaintSchemeForSeries() => schemes[0];

    public void Open()
    {
        transitionSource = null; transitionMailId = null; transitionYear = 0;
        saveButton.text = "Start Career";
        validationAttempted = false;
        ShowTeamForm(false);
        team.SetValueWithoutNotify(string.Empty);
        selectedSeries = -1;
        configuredSeries = -1;
        seriesSelector.text = "Choose a racing series";
        status.text = series.Length > 0 ? string.Empty : "No racing series definitions could be loaded.";
        shownSuppliers = Array.Empty<EngineSupplier>();
        manufacturer.choices = new List<string>();
        manufacturer.SetValueWithoutNotify("");
        supplier.choices = new List<string>();
        supplier.SetValueWithoutNotify("");
        engineDetails.text = "";
        carNumber.SetValueWithoutNotify(27);
        var paint = TrackPreview.TeamLivery(27);
        draftPaint = new CarPaintStyle(CarPaintSchemeForSeries(), paint.HoodAndTrunk, paint.RoofAndSides,
            new Color32(240, 240, 240, 255), true);
        RefreshPaintPreview();
        teamStatus.text = string.Empty;
        RefreshValidation();
        savedDraft = DraftState();
    }

    public void OpenSeriesChange(Career career, string mailId, int year)
    {
        Open(); transitionSource = career; transitionMailId = mailId; transitionYear = year;
        team.SetValueWithoutNotify(career.teamName);
        selectedSeries = Array.FindIndex(series, rules => rules.id == RacingSeries.CanonicalId(career.seriesId));
        configuredSeries = -1; ConfigureSeries();
        if (selectedSeries >= 0) seriesSelector.text = series[selectedSeries].name;
        var car = career.cars[0];
        int make = Array.IndexOf(manufacturers, car.manufacturer);
        manufacturer.index = make >= 0 ? make : 0; RefreshSuppliers();
        int engine = Array.FindIndex(shownSuppliers, item => item.name == career.engineSupplier?.name);
        if (engine >= 0) supplier.index = engine;
        carNumber.SetValueWithoutNotify(car.number);
        var paint = car.PaintStyle;
        draftPaint = new CarPaintStyle(paint.Scheme,
            PaintColor(career.primaryTeamColor ?? car.primaryPaint, "#009BDD"),
            PaintColor(career.secondaryTeamColor ?? car.secondaryPaint, "#FFFFFF"),
            PaintColor(car.trimPaint, "#F0F0F0"), true, paint.NumberColor, paint.NumberFont,
            paint.NumberBold, paint.NumberItalic);
        RefreshPaintPreview();
        saveButton.text = "Review Series Change";
        ShowTeamForm(false); RefreshValidation(); savedDraft = DraftState();
    }

    public string SeriesName(Career career) =>
        series.FirstOrDefault(item => item.id == RacingSeries.CanonicalId(career.seriesId))?.name ?? career.seriesName;

    public string CarModel(Career career) => series.FirstOrDefault(item => item.id == RacingSeries.CanonicalId(career.seriesId))?.carModel;

    private void ShowTeamForm(bool show)
    {
        bool inlinePaint = show && !IsSeriesChangeSetup;
        View.EnableInClassList("career-identity-page", !show);
        View.EnableInClassList("career-inline-paint", inlinePaint);
        frame.style.maxWidth = inlinePaint ? 1280 : 720;
        schemeSelector.style.display = inlinePaint ? DisplayStyle.None : DisplayStyle.Flex;
        paintStatus.style.display = inlinePaint ? DisplayStyle.Flex : DisplayStyle.None;
        inlinePaintControls.Clear();
        SettingsFormStyle.SetValidationMessage(paintStatus, null, false);
        if (inlinePaint)
            CarPaintEditor.BuildControls(inlinePaintControls, View, frame, () => ReadCarDraft(forPreview: true),
                Array.Empty<string>(), draftPaint, updated =>
                {
                    draftPaint = updated;
                    RefreshPaintPreview();
                }, paintStatus);
        heading.text = show ? "Car Setup" : IsSeriesChangeSetup ? "Team and Series Setup" : "New Career";
        backButton.text = show ? (IsSeriesChangeSetup ? "Back to Team Setup" : "Back to New Career") : IsSeriesChangeSetup ? "Back to Team" : "Back to Startup";
        playerForm.style.display = show ? DisplayStyle.None : DisplayStyle.Flex;
        teamForm.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        View.Q("careerPlayerActions").style.display = show ? DisplayStyle.None : DisplayStyle.Flex;
        View.Q("careerCarActions").style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        if (show)
        {
            ConfigureSeries();
            var rules = series[selectedSeries];
            seriesLogo.image = rules.Logo;
            seriesLogo.style.display = seriesLogo.image == null ? DisplayStyle.None : DisplayStyle.Flex;
            UpdateCarPreview();
            carNumber.Focus();
        }
        else if (!show)
        {
            View.style.backgroundImage = StyleKeyword.Null;
            ClosePreview();
            team.Focus();
        }
    }

    public void ClosePreview()
    {
        carPreview.image = null;
        previewThumbnails.Dispose();
    }

    private void UpdateCarPreview()
    {
        if (teamForm.style.display != DisplayStyle.Flex)
            return;
        var car = ReadCarDraft();
        if (car == null)
            return;
        car.chassisNumber = "career-setup-preview";
        carPreview.image = previewThumbnails.Get(car, Array.Empty<string>(), car.PaintStyle);
    }

    private void ConfigureSeries()
    {
        if (selectedSeries < 0 || selectedSeries >= series.Length || configuredSeries == selectedSeries)
            return;

        configuredSeries = selectedSeries;
        manufacturers = series[selectedSeries].ManufacturerChoices;
        manufacturer.choices = manufacturers.Select(make => make.ToString()).ToList();
        manufacturer.SetValueWithoutNotify(manufacturer.choices.FirstOrDefault() ?? "");
        shownSuppliers = Array.Empty<EngineSupplier>();
        supplier.choices = new List<string>();
        supplier.SetValueWithoutNotify("");
        RefreshSuppliers();
        RefreshValidation();
    }

    private void RefreshSuppliers()
    {
        int seriesIndex = selectedSeries;
        if (seriesIndex < 0 || manufacturer.index < 0)
            return;
        shownSuppliers = series[seriesIndex].SuppliersFor(manufacturers[manufacturer.index]);
        supplier.choices = shownSuppliers.Select(engine => engine.name).ToList();
        supplier.index = shownSuppliers.Length > 0 ? 0 : -1;
        supplier.SetEnabled(shownSuppliers.Length > 0);
        engineDetails.text = shownSuppliers.Length == 0 ? "No engine suppliers are configured." : string.Empty;
        RefreshEngineDetails();
        RefreshValidation();
    }

    private void RefreshEngineDetails()
    {
        if (supplier.index < 0 || supplier.index >= shownSuppliers.Length)
            return;
        var engine = shownSuppliers[supplier.index];
        engineDetails.text = $"Power {engine.power:F2}   -   Reliability {engine.reliability:F2}   -   " +
            $"{RaceFinances.Dollars(engine.maintenancePerRace)} maintenance per race";
    }

    private Career ReadPlayerInfo()
    {
        string teamName = team.value?.Trim();
        int selected = selectedSeries;
        if (string.IsNullOrWhiteSpace(teamName) || teamName.Length > 25 ||
            selected < 0 || selected >= series.Length)
        {
            status.text = "Enter a team name, then choose a racing series.";
            return null;
        }

        status.text = string.Empty;
        return new Career
        {
            teamName = teamName,
            seriesId = series[selected].id, seriesName = series[selected].name,
            teamBalance = RaceFinances.StartingBalance, financesInitialized = true
        };
    }

    private void Save(Action<Career> afterSave = null)
    {
        validationAttempted = true;
        RefreshValidation();
        if (!saveButton.enabledSelf) return;
        var career = ReadPlayerInfo();
        if (career == null)
        {
            ShowTeamForm(false);
            return;
        }
        if (carNumber.value < 1 || carNumber.value > 99)
        {
            teamStatus.text = "Enter a car number from 1 to 99.";
            return;
        }
        if (supplier.index < 0 || supplier.index >= shownSuppliers.Length)
        {
            teamStatus.text = "Choose an engine supplier.";
            return;
        }
        var car = ReadCarDraft();
        if (car == null)
        {
            teamStatus.text = "Choose a manufacturer.";
            return;
        }
        if (IsSeriesChangeSetup)
        {
            ReviewSeriesChange(career, car, shownSuppliers[supplier.index], afterSave);
            return;
        }
        career.engineSupplier = shownSuppliers[supplier.index];
        career.primaryTeamColor = car.primaryPaint;
        career.secondaryTeamColor = car.secondaryPaint;
        career.cars[0] = car;
        try
        {
            catalog.Refresh();
            var random = new System.Random();
            career.InitializeCars(random, randomizeRivals: true);
            career.InitializeEmployees(random);
            career.schedule = GenerateSeason(career, DateTime.Today.Year);
            career.SetSeasonStartDate();
            ScenarioMail.SendCareerStart(career);
            Merchandising.SendOffer(career, new DateTime(career.schedule.season, 1, 1));
            ScenarioMail.SendSeasonSponsorship(career, 0, 0);
            Directory.CreateDirectory(folder);
            career.savePath = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".json");
            SaveProgress(career);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            teamStatus.text = "Could not save the career. " + ex.Message;
            return;
        }

        ClosePreview();
        savedDraft = DraftState();
        (afterSave ?? start)(career);
    }

    private void ReviewSeriesChange(Career identity, TeamCar replacement, EngineSupplier engine, Action<Career> afterSave)
    {
        var source = transitionSource;
        var rules = series[selectedSeries];
        var sales = source.cars.Select(car => (Car: car, Value: UsedCarListing.TradeInPrice(car, source.SeriesRules))).ToArray();
        long saleTotal = sales.Sum(item => item.Value);
        long balance = source.teamBalance + saleTotal - rules.newChassisCost;
        var released = SeriesChangeReview.EmployeesToRelease(source, rules);
        bool wasEnabled = frame.enabledSelf;
        var overlay = new VisualElement(); overlay.AddToClassList("settings-confirmation-overlay");
        var dialog = CreateDialog(overlay, "Confirm Series Change"); dialog.style.width = 760;
        dialog.Add(SettingsFormStyle.Label(source.seriesName + " to " + rules.name + " - " + transitionYear, "settings-note"));
        dialog.Add(SettingsFormStyle.Label("Sell current chassis:\n" + string.Join("\n", sales.Select(item =>
            "Chassis " + item.Car.chassisNumber + " - " + RaceFinances.Dollars(item.Value))), "settings-note"));
        dialog.Add(SettingsFormStyle.Label("Purchase: " + replacement.manufacturer + " " + rules.name + " car #" + replacement.number +
            " - " + RaceFinances.Dollars(rules.newChassisCost), "settings-note"));
        dialog.Add(SettingsFormStyle.Label(released.Count == 0 ? "Employees let go: None. All current employees and drivers stay with the team." :
            "Employees let go:\n" + string.Join("\n", released.GroupBy(employee => employee.role).Select(group =>
                group.Key + ": " + string.Join(", ", group.Select(employee => employee.FullName)))), "settings-note"));
        dialog.Add(SettingsFormStyle.Label("Existing sponsorships and offers will be reset. New series offers will arrive by email.", "settings-note"));
        dialog.Add(SettingsFormStyle.Label("Current budget: " + RaceFinances.Dollars(source.teamBalance) +
            "\nChassis sales: +" + RaceFinances.Dollars(saleTotal) + "\nNew car: -" + RaceFinances.Dollars(rules.newChassisCost) +
            "\nBudget after change: " + RaceFinances.Dollars(balance), "settings-note"));
        var error = SettingsFormStyle.ErrorSlot(balance < 0 ? "The new car exceeds the available budget and chassis sale proceeds." : null);
        dialog.Add(error);
        var actions = new VisualElement(); actions.AddToClassList("settings-footer");
        var cancel = SettingsFormStyle.Button("Cancel", Close); cancel.tooltip = "Return to setup without changing the career.";
        var confirm = SettingsFormStyle.Button("Confirm Series Change", null, primary: true);
        confirm.tooltip = "Sell the listed chassis, purchase one replacement, release the listed employees, and reset sponsorships.";
        confirm.SetEnabled(balance >= 0);
        confirm.clicked += () =>
        {
            confirm.SetEnabled(false);
            try
            {
                var updated = source.CopyForTransaction();
                var mail = updated.inbox.Find(item => item.eventId == transitionMailId && item.NeedsResponse);
                if (mail == null) throw new InvalidOperationException("This series change is no longer available.");
                mail.scenarioResponse = "Change Series";
                updated.teamName = identity.teamName;
                updated.seriesId = rules.id; updated.seriesName = rules.name; updated.engineSupplier = engine;
                updated.teamBalance = balance;
                var releaseRoles = released.Select(employee => employee.role).Distinct().ToArray();
                updated.employees.RemoveAll(employee => releaseRoles.Contains(employee.role));
                replacement.componentWearInitialized = true;
                replacement.chassisType = rules.maximumTrackLengthMiles > 0 && rules.maximumTrackLengthMiles <= 1 ? ChassisType.ShortTrack : ChassisType.IntermediateTrack;
                replacement.Initialize(new System.Random()); updated.cars = new List<TeamCar> { replacement };
                updated.primaryTeamColor = replacement.primaryPaint; updated.secondaryTeamColor = replacement.secondaryPaint;
                if (source.schedule.season < transitionYear)
                {
                    var standings = ChampionshipStandings.Calculate(source.schedule, TrackPreview.CreateTimingEntries(source.cars[0], source), source.SeriesRules.ManufacturerChoices).owners;
                    int index = standings.FindIndex(row => row.player);
                    updated.previousSeasonRankingYear = source.schedule.season;
                    updated.previousSeasonRank = index >= 0 ? standings.FindIndex(row => row.points == standings[index].points) + 1 : standings.Count;
                    updated.previousSeasonFieldSize = standings.Count;
                }
                catalog.Refresh(); updated.schedule = GenerateSeason(updated, transitionYear);
                updated.schedule.carSales = saleTotal; updated.schedule.carPurchases = rules.newChassisCost;
                updated.completedSeasons = new List<SeasonSchedule>();
                if (!Career.TryReadDate(updated.currentDate, out var date) || date.Year < transitionYear) updated.SetSeasonStartDate();
                updated.usedCars = new List<UsedCarListing>(); updated.usedCarInventoryDate = null;
                updated.newChassisConfiguration = null; updated.usedCarManufacturerFilter = null;
                updated.usedCarTypeFilter = null; updated.usedCarMaxPriceFilter = null;
                ScenarioMail.ResetSeasonSponsorship(updated);
                SaveProgress(updated);
                transitionSource = updated; savedDraft = DraftState(); Close(); ClosePreview();
                start(updated); afterSave?.Invoke(updated);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
                error.RemoveFromHierarchy(); error = SettingsFormStyle.ErrorSlot("Could not change series. " + ex.Message);
                dialog.Insert(dialog.childCount - 1, error); confirm.SetEnabled(balance >= 0);
            }
        };
        actions.Add(cancel); actions.Add(confirm); dialog.Add(actions);
        frame.SetEnabled(false); View.Add(overlay); cancel.Focus();
        overlay.RegisterCallback<KeyDownEvent>(evt => { if (evt.keyCode == KeyCode.Escape) { Close(); evt.StopPropagation(); } });
        void Close() { overlay.RemoveFromHierarchy(); frame.SetEnabled(wasEnabled); }
    }

    private TeamCar ReadCarDraft(bool forPreview = false)
    {
        if (manufacturer.index < 0 || manufacturer.index >= manufacturers.Length)
            return null;
        if (!forPreview && (carNumber.value < 1 || carNumber.value > 99))
            return null;

        var car = new TeamCar
        {
            number = Mathf.Clamp(carNumber.value, 1, 99),
            manufacturer = manufacturers[manufacturer.index],
            modelResource = selectedSeries >= 0 ? series[selectedSeries].carModel : null
        };
        draftPaint.ApplyTo(car);
        return car;
    }

    public SeasonSchedule GenerateSeason(Career career, int year)
    {
        var definition = career.SeriesRules;
        return SeasonSchedule.Generate(year, catalog.Entries.Select(entry => entry.Definition), scheduleRandom,
            definition.maximumTrackLengthMiles, definition.usesStages, definition.stagePercentages);
    }

    public void SaveProgress(Career career)
    {
        var unassigned = career.AllCars.Where(car => string.IsNullOrEmpty(car.chassisNumber)).ToArray();
        if (unassigned.Length == 0)
        {
            WriteCareer(career);
            return;
        }

        using var allocationLock = new System.Threading.Mutex(false, "StockCarManagerCareerChassis");
        try
        {
            allocationLock.WaitOne();
        }
        catch (System.Threading.AbandonedMutexException)
        {
            // Ownership transfers to this process when the previous owner exits.
        }

        try
        {
            int lastSequence = career.AllCars.Select(ChassisSequence).DefaultIfEmpty().Max();
            foreach (string file in Directory.EnumerateFiles(folder, "*.json"))
            {
                ChassisRoster roster;
                try
                {
                    roster = JsonUtility.FromJson<ChassisRoster>(File.ReadAllText(file));
                }
                catch (ArgumentException ex)
                {
                    Debug.LogWarning($"Could not read chassis numbers in {Path.GetFileName(file)}. {ex.Message}");
                    continue;
                }
                if (roster == null)
                    continue;
                var cars = (roster.cars ?? Array.Empty<TeamCar>()).Concat(roster.rivalCars ?? Array.Empty<TeamCar>());
                var listedCars = (roster.usedCars ?? Array.Empty<UsedCarListing>()).Select(listing => listing.car);
                lastSequence = Math.Max(lastSequence, cars.Concat(listedCars).Select(ChassisSequence).DefaultIfEmpty().Max());
            }

            if (lastSequence + unassigned.Length > 9999)
                throw new IOException("No four-digit chassis numbers are available.");

            foreach (var car in unassigned)
                car.chassisNumber = FormattableString.Invariant($"{car.number}-{++lastSequence:D4}");

            try
            {
                WriteCareer(career);
            }
            catch
            {
                foreach (var car in unassigned)
                    car.chassisNumber = null;
                throw;
            }
        }
        finally
        {
            allocationLock.ReleaseMutex();
        }
    }

    private static int ChassisSequence(TeamCar car)
    {
        string number = car?.chassisNumber;
        int separator = number?.LastIndexOf('-') ?? -1;
        return separator >= 0 && number.Length - separator == 5 &&
            int.TryParse(number.Substring(separator + 1), out int sequence) ? sequence : 0;
    }

    [Serializable]
    private sealed class ChassisRoster
    {
        public TeamCar[] cars = null;
        public TeamCar[] rivalCars = null;
        public UsedCarListing[] usedCars = null;
    }

    public static string LimitName(string value) => value?.Length > 25 ? value.Substring(0, 25) : value;

    [Serializable]
    private sealed class NameValue { public string value; }

    private static string NormalizeCareerNames(string json) =>
        System.Text.RegularExpressions.Regex.Replace(json,
            @"""(?<key>teamName|seriesName)""\s*:\s*(?<value>""(?:\\.|[^""\\])*"")", match =>
            {
                string value = JsonUtility.FromJson<NameValue>("{\"value\":" + match.Groups["value"].Value + "}").value;
                if (value == LimitName(value)) return match.Value;
                string encoded = JsonUtility.ToJson(new NameValue { value = LimitName(value) });
                return "\"" + match.Groups["key"].Value + "\":" + encoded.Substring(9, encoded.Length - 10);
            });

    private static string ReadCareerJson(string file)
    {
        string json = File.ReadAllText(file);
        string normalized = NormalizeCareerNames(json);
        if (normalized != json)
        {
            var timestamp = File.GetLastWriteTimeUtc(file);
            SeriesSettingsStore.AtomicWrite(file, temporary => File.WriteAllText(temporary, normalized));
            File.SetLastWriteTimeUtc(file, timestamp);
        }
        return normalized;
    }

    private static void WriteCareer(Career career)
    {
        if (string.IsNullOrWhiteSpace(career.savePath))
            throw new InvalidOperationException("The career has no save-file destination.");
        career.teamName = LimitName(career.teamName);
        career.seriesName = LimitName(career.seriesName);
        string temporary = career.savePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, NormalizeCareerNames(JsonUtility.ToJson(career, true)));
            if (File.Exists(career.savePath))
                File.Replace(temporary, career.savePath, null);
            else
                File.Move(temporary, career.savePath);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    [Serializable]
    internal sealed class SavedCareer
    {
        public string teamName;
        public string seriesId;
        public string seriesName;
        public SeasonSchedule schedule;
        public string currentDate;
        public TeamCar[] cars;
        public SponsorContract primarySponsor;

        public int SeasonYear => schedule?.season > 0 ? schedule.season :
            Career.TryReadDate(currentDate, out var date) ? date.Year : 0;

        [NonSerialized] public string Path;
        [NonSerialized] public DateTime LastSaved;
    }


    public IReadOnlyList<SavedCareer> ReadSavedCareers()
    {
        var careers = new List<SavedCareer>();
        if (!Directory.Exists(folder))
            return careers;

        foreach (string file in Directory.GetFiles(folder, "*.json"))
        {
            try
            {
                var entry = JsonUtility.FromJson<SavedCareer>(ReadCareerJson(file));
                if (entry == null ||
                    string.IsNullOrWhiteSpace(entry.teamName) || string.IsNullOrWhiteSpace(entry.seriesId) ||
                    string.IsNullOrWhiteSpace(entry.seriesName))
                    continue;
                if (entry.seriesId is "tour-modified-detailed" or "cars-tour")
                {
                    entry.seriesId = RacingSeries.CanonicalId(entry.seriesId);
                    entry.seriesName = RacingSeries.ForId(entry.seriesId).name;
                }
                entry.Path = file;
                entry.LastSaved = File.GetLastWriteTimeUtc(file);
                careers.Add(entry);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException)
            {
                Debug.LogWarning($"Could not read career {Path.GetFileName(file)}. {ex.Message}");
            }
        }
        return careers.OrderByDescending(entry => entry.LastSaved).ThenBy(entry => entry.Path, StringComparer.Ordinal).ToArray();
    }

    [Serializable]
    private sealed class LegacyCareerIdentity
    {
        public string playerName;
    }

    public Career LoadSavedCareer(SavedCareer entry)
    {
        string file = GetSavePath(entry);
        string json = ReadCareerJson(file);
        var career = JsonUtility.FromJson<Career>(json);
        if (career == null ||
            string.IsNullOrWhiteSpace(career.teamName) || string.IsNullOrWhiteSpace(career.seriesId) ||
            string.IsNullOrWhiteSpace(career.seriesName))
            throw new IOException("The file does not contain a valid career.");

        if ((career.cars?.Count ?? 0) == 0)
            career.cars = new List<TeamCar> { new TeamCar { number = 27 } };

        // Read the retired field only while upgrading old result identities and mail.
        string legacyName = JsonUtility.FromJson<LegacyCareerIdentity>(json)?.playerName;
        foreach (var message in career.inbox ?? Enumerable.Empty<TeamMailMessage>())
        {
            if (message?.body?.StartsWith("Hi ", StringComparison.Ordinal) != true ||
                (message.eventId != "driver-welcome" &&
                 message.eventId?.StartsWith("morale-complaint/", StringComparison.Ordinal) != true))
                continue;

            int greetingEnd = message.body.IndexOf(",\n\n", StringComparison.Ordinal);
            if (greetingEnd >= 0)
                message.body = "Hi team" + message.body.Substring(greetingEnd);
        }

        foreach (var season in (career.completedSeasons ?? Enumerable.Empty<SeasonSchedule>()).Append(career.schedule))
        {
            if (season?.races == null)
                continue;
            foreach (var race in season.races)
            {
                race?.EnsureStages();
                if (race?.sessions == null)
                    continue;
                foreach (var session in race.sessions)
                {
                    if (session == null)
                        continue;
                    MigratePlayerResult(career, race, session, legacyName);
                    // Preserve recorded practice and qualifying results across career loads.
                    if (session.kind != RaceSessionKind.Race)
                        session.completed = session.Completed;
                }
            }
        }
        if (career.seriesId is "tour-modified-detailed" or "cars-tour")
        {
            career.seriesId = RacingSeries.CanonicalId(career.seriesId);
            career.seriesName = RacingSeries.ForId(career.seriesId).name;
        }
        career.savePath = file;
        return career;
    }

    private static void MigratePlayerResult(Career career, ScheduledRace race, RaceSession session, string legacyName)
    {
        if (race.playerSkipped || session.results == null)
            return;

        var playerResult = session.results.Find(race.IsPlayerResult);
        if (playerResult == null && race.carNumber <= 0)
        {
            var teamResults = session.results.Where(result => result != null &&
                string.IsNullOrEmpty(result.ownerEntryId) && result.teamName == career.teamName).ToArray();
            var nameMatches = string.IsNullOrWhiteSpace(legacyName) ? Array.Empty<SessionCarResult>() :
                teamResults.Where(result => result.driverName == legacyName).ToArray();
            playerResult = nameMatches.Length == 1 ? nameMatches[0] :
                teamResults.Length == 1 ? teamResults[0] : null;
        }
        if (playerResult == null)
            return;

        playerResult.ownerEntryId = "player";
        playerResult.playerEntry = true;
        if (!string.IsNullOrWhiteSpace(legacyName) && playerResult.driverName == legacyName &&
            career.drivers?.Any(driver => driver?.FullName == legacyName) != true)
            playerResult.driverName = "Unassigned driver";
    }

    public void DeleteSavedCareer(SavedCareer entry) => File.Delete(GetSavePath(entry));

    private string GetSavePath(SavedCareer entry)
    {
        string path = Path.GetFullPath(entry.Path);
        if (!string.Equals(Path.GetDirectoryName(path), Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
            throw new IOException("The selected file is outside the career save folder.");
        return path;
    }

    private static Button Button(string label, Action action)
    {
        var button = SettingsFormStyle.Button(label, action);
        button.tooltip = label switch
        {
            "Next" => "Choose the team car, manufacturer, engine supplier, and paint colors.",
            "Start Career" => "Create a separate saved career and open your team.",
            "Pick color" => "Choose a paint color for your team car.",
            _ => label.StartsWith("Back") ? "Return to the previous screen." : "Select " + label + "."
        };
        return button;
    }
}

internal static class RecruitmentSorting
{
    internal static T[] Order<T>(IEnumerable<T> candidates, int sort, Func<T, long> salary, Func<T, float> skill, Func<T, string> name)
    {
        IOrderedEnumerable<T> ordered = sort switch
        {
            1 => candidates.OrderByDescending(salary),
            2 => candidates.OrderByDescending(skill),
            3 => candidates.OrderBy(skill),
            _ => candidates.OrderBy(salary)
        };
        return ordered.ThenBy(name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
