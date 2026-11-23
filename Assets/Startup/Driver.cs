using System;
using UnityEngine;

internal enum DriverEthnicity { Unspecified, White, Black, EastAsian, SouthAsian }

[Serializable]
internal sealed class PersonalityTraits
{
    [Range(0f, 1f)] public float aggression = 0.5f;
}

[Serializable]
internal sealed class Driver
{
    public string firstName;
    public string lastName;
    public int age;
    public int yearsExperience;
    public bool experienceInitialized;
    [Range(0f, 1f)] public float morale;
    public bool moraleInitialized;
    public StaffArrest arrest;

    // Zero offsets give new drivers and older saves the same neutral rating.
    [SerializeField] private float socialMediaXpOffset;
    [SerializeField] private float charismaOffset;
    public float SocialMediaXp
    {
        get => .5f + socialMediaXpOffset;
        set => socialMediaXpOffset = Mathf.Clamp01(value) - .5f;
    }
    public float Charisma
    {
        get => .5f + charismaOffset;
        set => charismaOffset = Mathf.Clamp01(value) - .5f;
    }
    public const int MaximumTrackXp = 1000;
    public int shortTrackXp, intermediateXp, superspeedwayXp, roadCourseXp;
    public bool trackExperienceInitialized;

    internal bool EnsureCandidateTrackExperience(System.Random random)
    {
        if (trackExperienceInitialized) return false;
        if (shortTrackXp == 0 && intermediateXp == 0 && superspeedwayXp == 0 && roadCourseXp == 0)
        {
            float ageProgress = Mathf.Clamp01((age - 16) / 28f);
            float experienceProgress = Mathf.Clamp01(Math.Min(yearsExperience, Math.Max(0, age - 16)) / 20f);
            float baseline = 0.05f + 0.65f * ageProgress + 0.15f * experienceProgress + (float)(random.NextDouble() - 0.5) * 0.1f;
            foreach (ChassisType type in Enum.GetValues(typeof(ChassisType)))
                TrackXp(type) = Mathf.RoundToInt(Mathf.Clamp01(baseline + (float)(random.NextDouble() - 0.5) * 0.36f) * MaximumTrackXp);
        }
        trackExperienceInitialized = true;
        return true;
    }
    public string gender;
    public DriverEthnicity ethnicity;
    public string hometownCity;
    public string hometownState;
    public long annualSalary;
    public PersonalityTraits personalityTraits = new PersonalityTraits();

    public string FullName => (firstName + " " + lastName).Trim();

    public bool EnsureEthnicity()
    {
        if (ethnicity != DriverEthnicity.Unspecified)
            return false;

        // Assign a fictional profile consistently for older saves that lack this field.
        uint identity = 2166136261;
        foreach (char character in $"{FullName}|{age}|{gender}|{hometownCity}|{hometownState}")
            identity = unchecked((identity ^ character) * 16777619);
        ethnicity = (DriverEthnicity)(1 + identity % 4);
        return true;
    }

    public static Driver Create(System.Random random, string name, string city = null, string state = null, RacingSeries series = null) =>
        FromEmployee(Employee.Create(EmployeeRole.Driver, random, name, city, state, series), random);

    public ref int TrackXp(ChassisType category)
    {
        switch (category)
        {
            case ChassisType.ShortTrack: return ref shortTrackXp;
            case ChassisType.IntermediateTrack: return ref intermediateXp;
            case ChassisType.SuperSpeedway: return ref superspeedwayXp;
            case ChassisType.RoadCourse: return ref roadCourseXp;
            default: throw new ArgumentOutOfRangeException(nameof(category));
        }
    }

    public static Driver FromEmployee(Employee employee, System.Random random)
    {
        var driver = new Driver
        {
            firstName = employee.firstName,
            lastName = employee.lastName,
            age = employee.age,
            yearsExperience = employee.experienceInitialized ? employee.yearsExperience : Employee.GenerateYearsExperience(employee.age, random),
            experienceInitialized = true,
            morale = employee.morale,
            moraleInitialized = employee.moraleInitialized,
            gender = employee.gender,
            ethnicity = (DriverEthnicity)random.Next(1, 5),
            hometownCity = employee.hometownCity,
            hometownState = employee.hometownState,
            annualSalary = employee.annualSalary,
            personalityTraits = new PersonalityTraits { aggression = (float)random.NextDouble() }
        };
        driver.EnsureCandidateTrackExperience(random);
        return driver;
    }
}
