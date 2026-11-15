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
    public int order = 0;
}

[Serializable]
internal sealed class Career
{
    public string playerName;
    public string hometownCity;
    public string hometownState;
    public string teamName;
    public List<TeamMailMessage> inbox = new List<TeamMailMessage>();
    public List<string> deletedSocialPostIds = new List<string>();
    public string socialStartDate;
    public string primaryTeamColor;
    public string secondaryTeamColor;
    public string seriesId;
    public string seriesName;
    public SeasonSchedule schedule;
    public string currentDate;

    public static bool TryReadDate(string value, out DateTime date) =>
        DateTime.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out date);

    public void SetDate(DateTime date) =>
        currentDate = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public void SetSeasonStartDate()
    {
        string firstRace = schedule.races?.FirstOrDefault()?.date;
        SetDate(TryReadDate(firstRace, out var date) ? date.AddMonths(-1) : new DateTime(schedule.season, 1, 1).AddMonths(-1));
    }
    public long teamBalance;
    public List<SponsorContract> sponsors = new List<SponsorContract>();
    public List<SponsorOffer> sponsorOffers = new List<SponsorOffer>();
    public bool financesInitialized;
    public List<SeasonSchedule> completedSeasons = new List<SeasonSchedule>();
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

    public List<UsedCarListing> usedCars = new List<UsedCarListing>();
    public string usedCarInventoryDate;
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
            employees = Employee.CreateStaff(random);
            changed = true;
        }

        if (!playerRosterInitialized && (drivers == null || drivers.Count == 0))
        {
            drivers = MigrateDrivers(employees, random);
            if (drivers.Count == 0)
                drivers.Add(Driver.Create(random, playerName, hometownCity, hometownState));
            changed = true;
        }
        employees ??= new List<Employee>();
        drivers ??= new List<Driver>();
        changed |= !playerRosterInitialized;
        playerRosterInitialized = true;

        if (recruitCandidates != null)
            changed |= recruitCandidates.RemoveAll(candidate => candidate.role == EmployeeRole.Driver) > 0;

        if (rivalEmployees == null || rivalEmployees.Count == 0)
        {
            rivalEmployees = new List<TeamEmployees>();
            var rivals = TrackPreview.CreateTimingEntries(cars[0], this).Skip(1).GroupBy(car => car.teamName);
            foreach (var team in rivals)
            {
                rivalEmployees.Add(new TeamEmployees
                {
                    teamName = team.Key,
                    drivers = team.Select(entry => Driver.Create(random, entry.driverName)).ToList(),
                    employees = Employee.CreateStaff(random)
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
        }
        foreach (var employee in employees.Concat(recruitCandidates ?? Enumerable.Empty<Employee>())
            .Concat(rivalEmployees.SelectMany(team => team.employees ?? Enumerable.Empty<Employee>())))
            changed |= Employee.InitializeYearsExperience(employee.age, random, ref employee.yearsExperience, ref employee.experienceInitialized);

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

    public bool InitializeCars(System.Random random)
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
        return changed;
    }
}

internal enum CarComponent { Engine, Transmission, Brakes, Suspension, Aerodynamics }

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
    public int cost;

    public static int CostForWear(float wear) => wear > 0f
        ? Mathf.Max(1, Mathf.RoundToInt(Mathf.Clamp01(wear) * MaximumCost))
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

    public int RemainingHours(DateTime currentDate)
    {
        if (!Career.TryReadDate(completionDate, out var completionDay) || currentDate.Date >= completionDay)
            return 0;

        // completionDate is rounded up to a whole career day. Recover the original due hour.
        int roundingHours = durationHours > 0 ? (24 - durationHours % 24) % 24 : 0;
        return (completionDay - currentDate.Date).Days * 24 - roundingHours;
    }

    public static string FormatDuration(int hours)
    {
        int days = hours / 24;
        int remainder = hours % 24;
        return $"{days} {(days == 1 ? "day" : "days")}, {remainder} {(remainder == 1 ? "hour" : "hours")}";
    }

    public float RemainingFraction(DateTime date)
    {
        int duration = durationHours > 0 ? durationHours : 72; // Original jobs took three days.
        return Mathf.Clamp01(RemainingHours(date) / (float)duration);
    }
}

internal enum CarManufacturer { Chevrolet, Ford, Toyota }

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
    public CarManufacturer manufacturer = CarManufacturer.Chevrolet;
    public ChassisType chassisType = ChassisType.IntermediateTrack;

    public string ChassisTypeName => DisplayChassisType(chassisType);

    public static string DisplayChassisType(ChassisType type) => type switch
    {
        ChassisType.RoadCourse => "Road Course",
        ChassisType.ShortTrack => "Short Track",
        ChassisType.SuperSpeedway => "SuperSpeedway",
        _ => "Intermediate"
    };

    public string primaryPaint;
    public string secondaryPaint;
    public string trimPaint;
    public CarPaintScheme paintScheme;

    public CarPaintStyle PaintStyle
    {
        get
        {
            var colors = Paint;
            bool hasTrim = ColorUtility.TryParseHtmlString(trimPaint, out var trim);
            var scheme = Enum.IsDefined(typeof(CarPaintScheme), paintScheme) ? paintScheme : CarPaintScheme.Classic;
            return new CarPaintStyle(scheme, colors.HoodAndTrunk, colors.RoofAndSides,
                hasTrim ? (Color32)trim : new Color32(240, 240, 240, 255), hasTrim);
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
            frontLeft == null || frontRight == null || rearLeft == null || rearRight == null;
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
    [Range(0f, 1f)] public float wear = 0f;
    [Range(0f, 1f)] public float temperature = 0f;
}

[Serializable]
internal sealed class UsedCarListing
{
    public const long NewChassisPrice = 100_000;
    public static long TradeInPrice(TeamCar car)
    {
        double condition = TeamCar.Components.Average(component => 1d - Mathf.Clamp01(car.ComponentWear(component)));
        return (long)Math.Round(NewChassisPrice * 0.8d * condition, MidpointRounding.AwayFromZero);
    }
    public TeamCar car;
    public long price;

    public static UsedCarListing Create(int carNumber, System.Random random)
    {
        var manufacturers = (CarManufacturer[])Enum.GetValues(typeof(CarManufacturer));
        var chassisTypes = (ChassisType[])Enum.GetValues(typeof(ChassisType));
        var car = new TeamCar
        {
            number = carNumber,
            manufacturer = manufacturers[random.Next(manufacturers.Length)],
            chassisType = chassisTypes[random.Next(chassisTypes.Length)]
        };
        car.Initialize(random);
        double averageCondition = TeamCar.Components.Average(component => 1d - car.GetComponent(component).Wear);
        long price = (long)Math.Round(averageCondition * NewChassisPrice, MidpointRounding.AwayFromZero) + random.Next(5000, 15001);
        return new UsedCarListing { car = car, price = price };
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
    public string firstName;
    public string lastName;
    public int age;
    public int yearsExperience;
    public bool experienceInitialized;
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

    public static List<Employee> CreateStaff(System.Random random) => Enum.GetValues(typeof(EmployeeRole))
        .Cast<EmployeeRole>().Where(role => role != EmployeeRole.Driver).Select(role => Create(role, random)).ToList();

    public static Employee Create(EmployeeRole role, System.Random random, string name = null,
        string city = null, string state = null)
    {
        var hometown = Hometowns[random.Next(Hometowns.Length)];
        var parts = name?.Trim().Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
        int baseSalary = Roles[(int)role].Salary;
        int salarySpread = baseSalary * 15 / 100;
        var employee = new Employee
        {
            role = role,
            firstName = parts?.Length > 0 ? parts[0] : FirstNames[random.Next(FirstNames.Length)],
            lastName = parts?.Length > 1 ? parts[1] : name == null ? LastNames[random.Next(LastNames.Length)] : string.Empty,
            age = random.Next(20, 66),
            gender = Genders[random.Next(Genders.Length)],
            hometownCity = string.IsNullOrWhiteSpace(city) ? hometown.City : city,
            hometownState = string.IsNullOrWhiteSpace(state) ? hometown.State : state,
            annualSalary = baseSalary + random.Next(-salarySpread, salarySpread + 1),
            engines = (float)random.NextDouble(),
            suspensions = (float)random.NextDouble(),
            transmissions = (float)random.NextDouble(),
            bodywork = (float)random.NextDouble(),
            brakes = (float)random.NextDouble()
        };
        InitializeYearsExperience(employee.age, random, ref employee.yearsExperience, ref employee.experienceInitialized);
        return employee;
    }
}

internal sealed class CareerSetup
{
    private readonly RacingSeries[] series;
    private readonly TextField player = new TextField("Player name") { maxLength = 80 };
    private readonly TextField hometownCity = new TextField("Hometown city") { maxLength = 80 };
    private readonly TextField hometownState = new TextField("Hometown state") { maxLength = 80 };
    private readonly TextField team = new TextField("Team name") { maxLength = 80 };
    private readonly DropdownField seriesField;
    private readonly Label status = new Label();
    private readonly Action<Career> start;
    private readonly TrackCatalog catalog;
    private readonly System.Random scheduleRandom = new System.Random();
    private readonly string folder = Path.Combine(Application.persistentDataPath, "Careers");

    public VisualElement View { get; } = new VisualElement();
    public Career Latest { get; private set; }

    public CareerSetup(TrackCatalog catalog, Action<Career> start, Action back)
    {
        this.start = start;
        this.catalog = catalog;
        series = Resources.LoadAll<TextAsset>("RacingSeries")
            .Select(ReadSeries).Where(item => item != null)
            .GroupBy(item => item.id, StringComparer.Ordinal).Select(group => group.First())
            .OrderBy(item => item.order).ThenBy(item => item.name).ToArray();
        var choices = new List<string> { "Choose a racing series" };
        choices.AddRange(series.Select(item => item.name));
        seriesField = new DropdownField("Racing series", choices, 0);
        Latest = ReadLatest();

        View.AddToClassList("screen");
        View.AddToClassList("game-neutral");
        View.AddToClassList("tracks-editor");
        var header = new VisualElement();
        header.AddToClassList("header");
        header.Add(new Label("Create a New Career") { style = { fontSize = 24 } });
        header.Add(Button("Back to Title", back));
        View.Add(header);

        var form = new ScrollView();
        form.AddToClassList("garage-body");
        form.Add(player);
        form.Add(hometownCity);
        form.Add(hometownState);
        form.Add(team);
        form.Add(seriesField);
        status.style.whiteSpace = WhiteSpace.Normal;
        form.Add(status);
        var create = Button("Create Career", Save);
        create.SetEnabled(series.Length > 0);
        form.Add(create);
        View.Add(form);
    }

    public void Open()
    {
        player.SetValueWithoutNotify(string.Empty);
        hometownCity.SetValueWithoutNotify(string.Empty);
        hometownState.SetValueWithoutNotify(string.Empty);
        team.SetValueWithoutNotify(string.Empty);
        seriesField.index = 0;
        status.text = series.Length > 0 ? string.Empty : "No racing series definitions could be loaded.";
        player.Focus();
    }

    public string SeriesName(Career career) =>
        series.FirstOrDefault(item => item.id == career.seriesId)?.name ?? career.seriesName;

    private void Save()
    {
        string playerName = player.value?.Trim();
        string teamName = team.value?.Trim();
        string city = hometownCity.value?.Trim();
        string state = hometownState.value?.Trim();
        int selected = seriesField.index - 1;
        if (string.IsNullOrWhiteSpace(playerName) || string.IsNullOrWhiteSpace(teamName) ||
            string.IsNullOrWhiteSpace(city) || string.IsNullOrWhiteSpace(state) || selected < 0 || selected >= series.Length)
        {
            status.text = "Enter a player name, hometown city and state, and team name, then choose a racing series.";
            return;
        }

        var career = new Career
        {
            playerName = playerName, hometownCity = city, hometownState = state, teamName = teamName,
            seriesId = series[selected].id, seriesName = series[selected].name,
            teamBalance = RaceFinances.StartingBalance, financesInitialized = true
        };
        try
        {
            catalog.Refresh();
            var random = new System.Random();
            career.InitializeCars(random);
            career.InitializeEmployees(random);
            career.schedule = GenerateSeason(DateTime.Today.Year);
            career.SetSeasonStartDate();
            Directory.CreateDirectory(folder);
            career.savePath = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".json");
            SaveProgress(career);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            status.text = "Could not save the career. " + ex.Message;
            return;
        }

        Latest = career;
        start(career);
    }

    public SeasonSchedule GenerateSeason(int year, DateTime? previousRace = null) =>
        SeasonSchedule.Generate(year, catalog.Entries.Select(entry => entry.Definition), scheduleRandom, previousRace);

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

    private static void WriteCareer(Career career)
    {
        string temporary = career.savePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonUtility.ToJson(career, true));
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

    private Career ReadLatest()
    {
        try
        {
            if (!Directory.Exists(folder))
                return null;

            foreach (string file in Directory.GetFiles(folder, "*.json").OrderByDescending(File.GetLastWriteTimeUtc))
            {
                try
                {
                    var career = JsonUtility.FromJson<Career>(File.ReadAllText(file));
                    if (career != null && !string.IsNullOrWhiteSpace(career.playerName) &&
                        !string.IsNullOrWhiteSpace(career.teamName) && !string.IsNullOrWhiteSpace(career.seriesId) &&
                        !string.IsNullOrWhiteSpace(career.seriesName))
                    {
                        if ((career.cars?.Count ?? 0) == 0)
                            career.cars = new List<TeamCar> { new TeamCar { number = 27 } };
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
                                    if (session == null || session.kind == RaceSessionKind.Race)
                                        continue;

                                    session.completed = session.Completed;
                                    session.results?.Clear();
                                }
                            }
                        }
                        career.savePath = file;
                        return career;
                    }
                }
                catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException)
                {
                    Debug.LogWarning($"Could not load career {Path.GetFileName(file)}. {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            Debug.LogWarning("Could not read saved careers. " + ex.Message);
        }
        return null;
    }

    private static RacingSeries ReadSeries(TextAsset asset)
    {
        try
        {
            var result = JsonUtility.FromJson<RacingSeries>(asset.text);
            return result != null && !string.IsNullOrWhiteSpace(result.id) &&
                !string.IsNullOrWhiteSpace(result.name) ? result : null;
        }
        catch (ArgumentException ex)
        {
            Debug.LogWarning($"Invalid series definition {asset.name}. {ex.Message}");
            return null;
        }
    }

    private static Button Button(string label, Action action)
    {
        var button = new Button(action) { text = label };
        button.AddToClassList("nav-button");
        return button;
    }
}
