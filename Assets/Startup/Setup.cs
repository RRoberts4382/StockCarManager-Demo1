using System;
using UnityEngine;

[Serializable]
internal sealed class Setup
{
    public string notes;
    public float[] coldTirePressures = new float[4];
    public WeightDistributionSetup weightDistribution = new WeightDistributionSetup();
    public SuspensionCornerSetup leftFront = new SuspensionCornerSetup(750f);
    public SuspensionCornerSetup rightFront = new SuspensionCornerSetup(750f);
    public SuspensionCornerSetup leftRear = new SuspensionCornerSetup(350f);
    public SuspensionCornerSetup rightRear = new SuspensionCornerSetup(350f);
    public SwayBarSetup frontSwayBar = new SwayBarSetup();
    public TrackBarSetup rearTrackBar = new TrackBarSetup();
}

[Serializable]
internal sealed class TrackSetup
{
    public const string DefaultName = "Default";
    public int trackId;
    public string name;
    public Setup setup;

    public string DisplayName => string.IsNullOrWhiteSpace(name) ? DefaultName : name;
    public bool IsDefault => IsDefaultName(DisplayName);

    public static bool IsDefaultName(string candidate) =>
        string.Equals(DefaultName, candidate?.Trim(), StringComparison.OrdinalIgnoreCase);

    public bool Matches(string candidate) =>
        string.Equals(DisplayName, candidate, StringComparison.OrdinalIgnoreCase);
}

[Serializable]
internal sealed class SuspensionCornerSetup
{
    public SpringSetup spring;
    public ShockSetup shock = new ShockSetup();

    public SuspensionCornerSetup(float springRate)
    {
        spring = new SpringSetup { ratePoundsPerInch = springRate };
    }
}

[Serializable]
internal sealed class SpringSetup
{
    [Min(0f)] public float ratePoundsPerInch;
}

[Serializable]
internal sealed class ShockSetup
{
    [Range(0f, 1f)] public float compressionStiffness = 0.5f;
    [Range(0f, 1f)] public float reboundStiffness = 0.5f;
}

[Serializable]
internal sealed class SwayBarSetup
{
    [Min(0f)] public float torsionalRatePoundFeetPerDegree = 80f;
    public float ratePoundsPerInch;

    public float RatePoundsPerInch => ratePoundsPerInch > 0f ? ratePoundsPerInch : 1200f;
}

[Serializable]
internal sealed class TrackBarSetup
{
    // Mount heights are relative to the rear axle centerline.
    public const float LeftMountHeightInches = 0f;
    public float rightMountHeightInches;
    public float rightMountHeightAboveGroundInches;

    public float RightMountHeightAboveGroundInches => rightMountHeightAboveGroundInches > 0f
        ? rightMountHeightAboveGroundInches : 9.5f + rightMountHeightInches;
}

// Game tuning ranges, not official NASCAR inspection limits. Percentages describe
// dry-car distribution including the driver. Rear fuel load is added at runtime.
[Serializable]
internal sealed class WeightDistributionSetup
{
    public const float MinFront = 48f, MaxFront = 56f;
    public const float MinCross = 45f, MaxCross = 55f;
    public const float MinLeft = 48f, MaxLeft = 58f;
    public float frontPercent = 52f;
    public float crossPercent = 50f;
    public float leftPercent = 50f;
    public float FrontPercent => Valid(frontPercent, 52f, MinFront, MaxFront);
    public float CrossPercent => Valid(crossPercent, 50f, MinCross, MaxCross);
    public float LeftPercent => Valid(leftPercent, 50f, MinLeft, MaxLeft);
    private static float Valid(float value, float fallback, float min, float max) =>
        float.IsNaN(value) || float.IsInfinity(value) || value == 0f ? fallback : Mathf.Clamp(value, min, max);

    internal static float CornerFraction(Setup setup, int corner)
    {
        var weights = setup?.weightDistribution;
        float front = (weights?.FrontPercent ?? 52f) / 100f;
        float cross = (weights?.CrossPercent ?? 50f) / 100f;
        float left = (weights?.LeftPercent ?? 50f) / 100f;
        float lf = (front + left - cross) * 0.5f;
        return corner == 0 ? lf : corner == 1 ? front - lf :
            corner == 2 ? left - lf : 1f - front - left + lf;
    }
}

internal static class SetupLimits
{
    internal static (float Min, float Max) SpringRange(ChassisType type, int corner) => type switch
    {
        ChassisType.ShortTrack => (200f, 600f),
        ChassisType.RoadCourse => corner < 2 ? (500f, 850f) : (300f, 550f),
        _ => corner switch
        {
            0 => (300f, 750f),
            1 => (500f, type == ChassisType.SuperSpeedway ? 950f : 1400f),
            2 => (200f, 450f),
            _ => (500f, type == ChassisType.SuperSpeedway ? 800f : 1100f)
        }
    };

}
