using Random = System.Random;
using UnityEngine;

internal sealed class VehicleSpeed003 : IVehicleSpeedModel
{
    private const float ReferenceSpeed = 63f;
    private const float ReferenceEffectiveValue = 1f;
    private const float SpeedPerEffectiveValuePoint = 0.5f;

    // Initial game calibration, including the driver but excluding fuel. The
    // existing acceleration/braking calibration is the no-fuel reference mass.
    internal const float DryWeightPounds = 3400f;
    internal const float FuelPoundsPerGallon = 6.2f;

    internal static float WeightPounds(double fuelGallons)
    {
        if (double.IsNaN(fuelGallons) || double.IsInfinity(fuelGallons))
            fuelGallons = SessionFuel.TankCapacity;
        return DryWeightPounds + Mathf.Clamp((float)fuelGallons, 0f,
            (float)SessionFuel.TankCapacity) * FuelPoundsPerGallon;
    }

    // Fixed available drive/braking force: acceleration decreases as mass rises.
    // Neither factor can increase the established maximum acceleration/braking.
    internal static float WeightForceFactor(double fuelGallons) =>
        DryWeightPounds / WeightPounds(fuelGallons);

    // Tires gain load capacity with weight, but less than proportionally. This
    // provisional load sensitivity produces a modest corner-speed penalty,
    // rather than treating extra fuel as if it reduced available grip outright.
    internal static float WeightCornerSpeedFactor(double fuelGallons) =>
        Mathf.Pow(WeightForceFactor(fuelGallons), 0.1f);

    // Initial calibration: tread wear scales linearly with total vehicle weight.
    // Use the same dry-weight reference as the acceleration/braking calculations.
    internal static float WeightTireWearFactor(double fuelGallons) =>
        WeightPounds(fuelGallons) / DryWeightPounds;

    // Setup percentages describe the dry car (including driver). Model the fuel
    // tank at the rear axle, centered laterally: half its load at each rear tire.
    // Do not clamp resulting percentages to setup slider ranges during fuel burn.
    internal static float CornerWeightPounds(Setup setup, double fuelGallons, int corner) =>
        DryWeightPounds * WeightDistributionSetup.CornerFraction(setup, corner) +
        (corner >= 2 ? (WeightPounds(fuelGallons) - DryWeightPounds) * 0.5f : 0f);

    internal static float FrontWeightPercent(Setup setup, double fuelGallons) =>
        (setup?.weightDistribution?.FrontPercent ?? 52f) * DryWeightPounds / WeightPounds(fuelGallons);

    internal static float CrossWeightPercent(Setup setup, double fuelGallons) =>
        ((setup?.weightDistribution?.CrossPercent ?? 50f) * DryWeightPounds +
            (WeightPounds(fuelGallons) - DryWeightPounds) * 50f) / WeightPounds(fuelGallons);

    internal static float LeftWeightPercent(Setup setup, double fuelGallons) =>
        ((setup?.weightDistribution?.LeftPercent ?? 50f) * DryWeightPounds +
            (WeightPounds(fuelGallons) - DryWeightPounds) * 50f) / WeightPounds(fuelGallons);

    // Relative distribution only: wear already includes total mass separately.
    internal static float DistributionTireLoadFactor(Setup setup, int corner, double fuelGallons = 0d) =>
        CornerWeightPounds(setup, fuelGallons, corner) / WeightPounds(fuelGallons) /
        WeightDistributionSetup.CornerFraction(null, corner);

    // Thermal load needs the actual corner load, not a second total-mass multiplier.
    internal static float CornerTireHeatLoadFactor(Setup setup, int corner, double fuelGallons) =>
        CornerWeightPounds(setup, fuelGallons, corner) /
        (DryWeightPounds * WeightDistributionSetup.CornerFraction(null, corner));

    internal static float DistributionAccelerationFactor(Setup setup, double fuelGallons = 0d) =>
        Mathf.Clamp((100f - FrontWeightPercent(setup, fuelGallons)) / 48f, 0.92f, 1.08f);

    internal static float DistributionBrakingFactor(Setup setup, double fuelGallons = 0d) =>
        Mathf.Clamp(FrontWeightPercent(setup, fuelGallons) / 52f, 0.92f, 1.08f);

    internal static float DistributionCornerFactor(TrackDefinition track, Setup setup, double fuelGallons = 0d)
    {
        float front = FrontWeightPercent(setup, fuelGallons);
        float cross = CrossWeightPercent(setup, fuelGallons);
        float left = LeftWeightPercent(setup, fuelGallons);
        bool road = track.majorStyle == "Road";
        float targetFront = road ? 51f : track.lengthMiles <= 1f ? 50.5f : 52f;
        float targetCross = road ? 50f : 51f;
        float targetLeft = road ? 50f : 55.5f;
        // Excess nose weight and wedge share a tight-balance penalty. These are
        // provisional speed targets, not steering or spin instructions.
        float balance = (front - targetFront) + (cross - targetCross) * 0.7f;
        float baseline = (52f - targetFront) + (50f - targetCross) * 0.7f;
        float error = (balance * balance - baseline * baseline) / 64f;
        float lateralError = ((left - targetLeft) * (left - targetLeft) -
            (50f - targetLeft) * (50f - targetLeft)) / 64f;
        return Mathf.Clamp(1f - error * 0.035f - lateralError * 0.025f, 0.94f, 1.025f);
    }

    public string Id => "vehicle-speed-003";
    public string Name => "VehicleSpeed003";
    public bool UsesLapSpeedProfile => true;

    public float BaseSpeed(CarSimulationContext context, int carIndex, Random random)
    {
        float reference = context.Track.qualifyingReferenceMph > 0f
            ? context.Track.qualifyingReferenceMph * TrackDefinition.MetersPerMile / 3600f
            : ReferenceSpeed;
        var weights = context.Track.weights;
        var car = carIndex < context.Cars.Count ? context.Cars[carIndex].car : null;
        if (car == null || weights == null)
            return VehicleSpeedModels.ApplyEngineAndDriverPerformance(context, carIndex, reference);

        float weightedPerformance =
            weights.aerodynamics * (car.GetComponent(CarComponent.Aerodynamics).EffectiveValue - ReferenceEffectiveValue) +
            weights.horsepower * (car.GetComponent(CarComponent.Engine).EffectiveValue - ReferenceEffectiveValue) +
            weights.brakes * (car.GetComponent(CarComponent.Brakes).EffectiveValue - ReferenceEffectiveValue) +
            weights.transmission * (car.GetComponent(CarComponent.Transmission).EffectiveValue - ReferenceEffectiveValue) +
            weights.suspension * (car.GetComponent(CarComponent.Suspension).EffectiveValue - ReferenceEffectiveValue);

        return VehicleSpeedModels.ApplyEngineAndDriverPerformance(context, carIndex,
            reference + SpeedPerEffectiveValuePoint * weightedPerformance);
    }

    // Initial tuning targets, in lb/in. These approximate setup suitability, not
    // a full suspension solver. Keep the coefficients here for A/B calibration.
    internal static float TargetSpring(TrackDefinition track, int corner)
    {
        bool front = corner < 2;
        bool right = corner % 2 == 1;
        float target;
        if (track.majorStyle == "Road")
            target = front ? 650f : 425f;
        else if (track.restrictorPlate)
            target = front ? 650f : 400f;
        else if (track.lengthMiles <= 1f)
            target = front ? (right ? 650f : 450f) : (right ? 600f : 300f);
        else
            target = front ? (right ? 1050f : 650f) : (right ? 900f : 325f);
        float roughness = Mathf.Clamp01(track.weights?.surfaceRoughness ?? 0.5f);
        return target * Mathf.Lerp(1.08f, 0.92f, roughness);
    }

    private static SuspensionCornerSetup CornerSetup(Setup setup, int corner) =>
        corner == 0 ? setup?.leftFront : corner == 1 ? setup?.rightFront :
            corner == 2 ? setup?.leftRear : setup?.rightRear;

    private static float SpringRate(Setup setup, int corner)
    {
        var suspension = CornerSetup(setup, corner);
        float rate = suspension?.spring?.ratePoundsPerInch ?? (corner < 2 ? 750f : 350f);
        return float.IsNaN(rate) || float.IsInfinity(rate) || rate <= 0f
            ? (corner < 2 ? 750f : 350f) : rate;
    }

    private static float SpringError(float rate, float target)
    {
        float difference = Mathf.Clamp(Mathf.Log(rate / target, 2f), -2f, 2f);
        return difference * difference;
    }

    internal static float SpringHeatFactor(TrackDefinition track, Setup setup, int corner)
    {
        float error = SpringError(SpringRate(setup, corner), TargetSpring(track, corner));
        return 1f + Mathf.Min(0.6f, error * 0.3f);
    }

    private const float BaselineSwayBarRate = 1200f;

    private static float SwayBarRate(Setup setup)
    {
        float rate = setup?.frontSwayBar?.RatePoundsPerInch ?? BaselineSwayBarRate;
        return float.IsNaN(rate) || float.IsInfinity(rate)
            ? BaselineSwayBarRate : Mathf.Clamp(rate, 275f, 5000f);
    }

    internal static float TargetSwayBar(TrackDefinition track, Setup setup)
    {
        // Provisional tuning targets in lb/in. Softer front springs need more bar
        // support; rough surfaces favor a softer bar for independent wheel travel.
        float target = track.majorStyle == "Road" ? 1100f :
            track.restrictorPlate ? 2000f : track.lengthMiles <= 1f ? 900f : 1800f;
        float frontSprings = SpringRate(setup, 0) + SpringRate(setup, 1);
        float targetSprings = TargetSpring(track, 0) + TargetSpring(track, 1);
        float support = Mathf.Clamp(targetSprings / frontSprings, 0.8f, 1.2f);
        float roughness = Mathf.Clamp01(track.weights?.surfaceRoughness ?? 0.5f);
        return Mathf.Clamp(target * support * Mathf.Lerp(1.15f, 0.85f, roughness), 275f, 5000f);
    }

    private static float SwayBarErrorDifference(TrackDefinition track, Setup setup)
    {
        float target = TargetSwayBar(track, setup);
        return SpringError(SwayBarRate(setup), target) - SpringError(BaselineSwayBarRate, target);
    }

    internal static float SwayBarHeatFactor(TrackDefinition track, Setup setup, int corner, float lateralG)
    {
        if (corner >= 2)
            return 1f;
        float stiffnessChange = Mathf.Clamp(Mathf.Log(SwayBarRate(setup) / BaselineSwayBarRate, 2f), -2f, 2f);
        float outside = Mathf.Clamp((corner == 0 ? lateralG : -lateralG) / 1.5f, -1f, 1f);
        // Stiffer bars concentrate cornering load on the outside front tire.
        // Both excessive stiffness and inadequate roll support can add scrub heat.
        float scrub = SwayBarErrorDifference(track, setup) * 0.10f;
        float loadTransfer = stiffnessChange * outside * 0.12f;
        return Mathf.Clamp(1f + scrub + loadTransfer, 0.8f, 1.5f);
    }

    private const float BaselineTrackBarHeight = 9.5f;

    private static float TrackBarHeight(Setup setup)
    {
        // Use the existing accessor so older setups with an axle-relative height
        // receive the same conversion as the setup controls.
        float height = setup?.rearTrackBar?.RightMountHeightAboveGroundInches ?? BaselineTrackBarHeight;
        return float.IsNaN(height) || float.IsInfinity(height)
            ? BaselineTrackBarHeight : Mathf.Clamp(height, 5f, 14f);
    }

    private static float TrackBarErrorDifference(TrackDefinition track, Setup setup)
    {
        // Initial game calibration in inches, to be tuned with recorded runs.
        float target = track.majorStyle == "Road" ? 8.5f :
            track.restrictorPlate ? 9.5f : track.lengthMiles <= 1f ? 9f : 10f;
        float roughness = Mathf.Clamp01(track.weights?.surfaceRoughness ?? 0.5f);
        target += Mathf.Lerp(0.75f, -0.75f, roughness);
        float error = (TrackBarHeight(setup) - target) / 4.5f;
        float baselineError = (BaselineTrackBarHeight - target) / 4.5f;
        return error * error - baselineError * baselineError;
    }

    internal static float TrackBarHeatFactor(TrackDefinition track, Setup setup, int corner, float lateralG)
    {
        float heightChange = TrackBarHeight(setup) - BaselineTrackBarHeight;
        bool rear = corner >= 2;
        // Higher rear roll center adds rear scrub; lowering it can add front scrub.
        // This changes thermal load only, without steering or lateral displacement.
        float scrub = (rear ? heightChange : -heightChange) * 0.04f;
        float outside = Mathf.Clamp((corner % 2 == 0 ? lateralG : -lateralG) / 1.5f, -1f, 1f);
        float mismatch = TrackBarErrorDifference(track, setup) * (rear ? 0.12f : 0.08f);
        return Mathf.Clamp(1f + scrub * (1f + outside * 0.25f) + mismatch, 0.8f, 1.4f);
    }

    private static float ShockSetting(float value) =>
        float.IsNaN(value) || float.IsInfinity(value) ? 0.5f : Mathf.Clamp01(value);

    private static float ShockError(float value, float target)
    {
        float deviation = (value - target) * 2f;
        float baseline = (0.5f - target) * 2f;
        return deviation * deviation - baseline * baseline;
    }

    private static float ShockWork(TrackDefinition track, Setup setup, int corner, Vector2 motion)
    {
        var shock = CornerSetup(setup, corner)?.shock;
        float compression = ShockSetting(shock?.compressionStiffness ?? 0.5f);
        float rebound = ShockSetting(shock?.reboundStiffness ?? 0.5f);
        float roughness = Mathf.Clamp01(track.weights?.surfaceRoughness ?? 0.5f);
        float springRatio = Mathf.Clamp(SpringRate(setup, corner) / TargetSpring(track, corner) - 1f, -1f, 1f);
        // Provisional normalized damper targets. Stiffer springs need more damping;
        // rough surfaces favor wheel compliance. Default 0.5 settings are neutral.
        float compressionTarget = Mathf.Clamp(0.48f + springRatio * 0.12f -
            (roughness - 0.5f) * 0.18f, 0.25f, 0.75f);
        float reboundTarget = Mathf.Clamp(0.56f + springRatio * 0.14f -
            (roughness - 0.5f) * 0.12f, 0.25f, 0.8f);

        // Signed motion indicates compression (+) or extension (-) at this corner.
        float lateral = corner % 2 == 0 ? motion.x : -motion.x;
        float pitch = corner < 2 ? motion.y : -motion.y;
        float movement = Mathf.Clamp(lateral + pitch, -1f, 1f);
        float bumps = roughness * 0.15f;
        float compressionWork = Mathf.Clamp01(Mathf.Max(0f, movement) + bumps);
        float reboundWork = Mathf.Clamp01(Mathf.Max(0f, -movement) + bumps);
        return ShockError(compression, compressionTarget) * compressionWork +
            ShockError(rebound, reboundTarget) * reboundWork;
    }

    internal static float ShockHeatFactor(TrackDefinition track, Setup setup, int corner, Vector2 motion) =>
        Mathf.Clamp(1f + ShockWork(track, setup, corner, motion) * 0.3f, 0.8f, 1.5f);

    internal static float ShockSpeedFactor(TrackDefinition track, Setup setup, Vector2 motion)
    {
        float work = 0f;
        for (int corner = 0; corner < 4; corner++)
            work += ShockWork(track, setup, corner, motion);
        return Mathf.Clamp(1f - work * (0.03f / 4f), 0.96f, 1.01f);
    }

    // Provisional grip calibration: fresh tires peak at 185-220 F. Temperature
    // and tread condition combine per tire, then corner speed follows sqrt(grip).
    // This limits cornering performance without altering the driving line.
    // Provisional game calibration. Cold readings are referenced to 75 F;
    // absolute-pressure gas law uses carcass temperature as a gas-temperature proxy.
    internal static float ColdPressure(Setup setup, int corner, CarTire tire = null)
    {
        float value = tire?.coldPressurePsi ?? 0f;
        if (value <= 0f) value = setup?.coldTirePressures?.Length == 4 ? setup.coldTirePressures[corner] : 0f;
        return float.IsNaN(value) || float.IsInfinity(value) || value <= 0f
            ? (corner % 2 == 0 ? 28f : 46f) : Mathf.Clamp(value, 10f, 60f);
    }

    internal static float HotPressure(float cold, float temperatureF) =>
        (Mathf.Clamp(cold, 10f, 60f) + 14.7f) *
        (Mathf.Clamp(temperatureF, 30f, 325f) + 459.67f) / (75f + 459.67f) - 14.7f;

    internal static float PressureRatio(Setup setup, int corner, CarTire tire) =>
        HotPressure(ColdPressure(setup, corner, tire), TireTemperatureModel.Fahrenheit(tire?.temperature ?? 0f)) /
        HotPressure(corner % 2 == 0 ? 28f : 46f, 200f);

    internal static float PressureGrip(Setup setup, int corner, CarTire tire)
    {
        float error = PressureRatio(setup, corner, tire) - 0.96f;
        return Mathf.Clamp(1f - error * error * (error < 0f ? 1.3f : 0.75f), 0.65f, 1f);
    }

    internal static float PressureHeat(Setup setup, int corner, CarTire tire)
    {
        float ratio = ColdPressure(setup, corner, tire) / (corner % 2 == 0 ? 28f : 46f);
        return Mathf.Clamp(1f + Mathf.Max(0f, 1f - ratio) * 0.5f + Mathf.Max(0f, ratio - 1.15f) * 0.2f, 1f, 1.4f);
    }

    internal static float PressureWear(Setup setup, int corner, CarTire tire)
    {
        float error = PressureRatio(setup, corner, tire) - 1f;
        return Mathf.Clamp(1f + error * error * 3f, 1f, 2.5f);
    }

    internal static float PressureStraightSpeed(Setup setup, TeamCar car)
    {
        float ratio = 0f;
        for (int i = 0; i < 4; i++) ratio += ColdPressure(setup, i, car?.GetTire(i)) / (i % 2 == 0 ? 28f : 46f);
        return Mathf.Clamp(1f + (ratio / 4f - 1f) * 0.008f, 0.992f, 1.006f);
    }

    internal static float TireGripFactor(float wear, float temperatureFahrenheit)
    {
        wear = Mathf.Clamp01(wear);
        float wearGrip = 1f - 0.36f * (0.5f * wear + 0.5f * wear * wear);
        float cold = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(75f, 185f, temperatureFahrenheit));
        float hot = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(220f, 325f, temperatureFahrenheit));
        float temperatureGrip = Mathf.Lerp(0.81f, 1f, cold) * Mathf.Lerp(1f, 0.5625f, hot);
        return wearGrip * temperatureGrip;
    }

    // Initial surface-to-carcass heat-transfer calibration. A 90 F surface
    // preserves the previous thermal baseline; each additional 10 F raises the
    // equilibrium tire temperature by 3.5 F before other loads and limits.
    internal static float TrackTemperatureHeatOffset(float trackFahrenheit) =>
        (Mathf.Clamp(trackFahrenheit, 30f, 190f) - 90f) * 0.35f;

    // Hot surfaces speed up the distance-based warm-up without changing its
    // operating-temperature threshold. Initial tuning: +10% rate per 10 F
    // above 90 F, capped at twice the normal rate.
    internal static float TrackTemperatureWarmupFactor(float trackFahrenheit) =>
        1f + Mathf.Clamp((trackFahrenheit - 90f) * 0.01f, 0f, 1f);

    // Initial tuning curve: normal wear through the optimal range, then an
    // increasingly steep penalty up to 4x at the thermal model's 325 F ceiling.
    internal static float TireTemperatureWearFactor(float temperatureFahrenheit)
    {
        float heat = Mathf.InverseLerp(220f, TireTemperatureModel.MaximumFahrenheit,
            temperatureFahrenheit);
        return 1f + heat + 2f * heat * heat;
    }

    internal static float TireCornerSpeedFactor(Setup setup, TeamCar car)
    {
        float totalGrip = 0f;
        float worstGrip = 1f;
        for (int corner = 0; corner < 4; corner++)
        {
            var tire = car?.GetTire(corner);
            float temperature = TireTemperatureModel.Fahrenheit(tire?.temperature ?? 0f);
            float grip = TireGripFactor(tire?.wear ?? 0f, temperature) * PressureGrip(setup, corner, tire);
            totalGrip += grip;
            worstGrip = Mathf.Min(worstGrip, grip);
        }
        return Mathf.Sqrt((totalGrip / 4f + worstGrip) * 0.5f);
    }

    internal static float CornerSetupSpeedFactor(TrackDefinition track, Setup setup, double fuelGallons)
    {
        float springDifference = 0f;
        for (int corner = 0; corner < 4; corner++)
        {
            float target = TargetSpring(track, corner);
            float baseline = corner < 2 ? 750f : 350f;
            springDifference += SpringError(SpringRate(setup, corner), target) -
                SpringError(baseline, target);
        }
        float springFactor = Mathf.Clamp(1f - springDifference * 0.025f, 0.88f, 1.03f);
        float swayBarFactor = Mathf.Clamp(1f - SwayBarErrorDifference(track, setup) * 0.02f, 0.94f, 1.02f);
        float trackBarFactor = Mathf.Clamp(1f - TrackBarErrorDifference(track, setup) * 0.06f, 0.94f, 1.015f);
        return springFactor * swayBarFactor * trackBarFactor * DistributionCornerFactor(track, setup, fuelGallons);
    }

    // Plate cars stay on power. Setup, weight and weather set the sustainable
    // pace; tire condition is blended in where the plate profile calls for it.
    internal static float RestrictorPlatePaceFactor(TrackDefinition track, Setup setup,
        double fuelGallons, float trackGrip) =>
        CornerSetupSpeedFactor(track, setup, fuelGallons) *
        ShockSpeedFactor(track, setup, Vector2.zero) * WeightCornerSpeedFactor(fuelGallons) * Mathf.Sqrt(trackGrip);

    // Diagnostic only: relative front/rear lateral grip per pound of axle load.
    // Uses the same tire grip and load sensitivity as the speed model; it does
    // not feed another penalty into speed, temperature, wear, or steering.
    internal static float HandlingBalance(Setup setup, TeamCar car, DynamicTireLoads loads,
        double fuel, out float tireContribution, out float loadContribution)
    {
        float front = 0f, rear = 0f, frontLoad = 0f, rearLoad = 0f;
        float frontCapacity = 0f, rearCapacity = 0f;
        for (int i = 0; i < 4; i++)
        {
            float weight = Mathf.Max(1f, loads.Load(i, setup, fuel));
            float capacity = Mathf.Pow(weight, 0.85f);
            var tire = car?.GetTire(i);
            float grip = TireGripFactor(tire?.wear ?? 0f,
                TireTemperatureModel.Fahrenheit(tire?.temperature ?? 0f)) * PressureGrip(setup, i, tire);
            if (i < 2) { front += capacity * grip; frontCapacity += capacity; frontLoad += weight; }
            else { rear += capacity * grip; rearCapacity += capacity; rearLoad += weight; }
        }
        float loadOnly = BalanceScore(frontCapacity / frontLoad, rearCapacity / rearLoad);
        float combined = BalanceScore(front / frontLoad, rear / rearLoad);
        loadContribution = loadOnly;
        tireContribution = combined - loadOnly;
        return combined;
    }

    internal static float BalanceScore(float frontGrip, float rearGrip) =>
        100f * (rearGrip - frontGrip) / Mathf.Max(0.001f, Mathf.Max(frontGrip, rearGrip));

    // Keep setup comparisons deterministic across sessions.
    public float LapSpeedFactor(CarSimulationContext context, int carIndex, Random random) => 1f;
}

// Lightweight transient load model: pounds of vertical load, with signed G inputs.
// Positive longitudinal G accelerates; positive lateral G denotes a right turn.
// Provisional geometry: 110-inch wheelbase, 63-inch track, 20-inch CG height.
internal sealed class DynamicTireLoads
{
    private readonly float[] loads = new float[4];
    private readonly float[] targets = new float[4];
    private bool initialized;
    internal float LongitudinalG { get; private set; }
    internal float LateralG { get; private set; }
    internal float Load(int corner, Setup setup, double fuel) => initialized ? loads[corner] :
        VehicleSpeed003.CornerWeightPounds(setup, fuel, corner);
    internal float Ratio(int corner, Setup setup, double fuel) =>
        Load(corner, setup, fuel) / Mathf.Max(1f, VehicleSpeed003.CornerWeightPounds(setup, fuel, corner));

    private static SuspensionCornerSetup Corner(Setup s, int i) =>
        i == 0 ? s?.leftFront : i == 1 ? s?.rightFront : i == 2 ? s?.leftRear : s?.rightRear;
    private static float Spring(Setup s, int i) => Mathf.Clamp(Corner(s, i)?.spring?.ratePoundsPerInch ?? (i < 2 ? 750f : 350f), 100f, 2000f);

    internal void Advance(Setup setup, double fuel, float longitudinalG, float lateralG, float seconds)
    {
        if (seconds <= 0f) return;
        LongitudinalG = Mathf.Clamp(longitudinalG, -2.7f, 0.81f);
        LateralG = Mathf.Clamp(lateralG, -3.5f, 3.5f);
        float total = VehicleSpeed003.WeightPounds(fuel);
        float pitch = total * LongitudinalG * (20f / 110f);
        float roll = total * LateralG * (20f / 63f);
        float frontStiffness = Spring(setup, 0) + Spring(setup, 1) +
            (setup?.frontSwayBar?.RatePoundsPerInch ?? 1200f) * 0.35f;
        float rearStiffness = Spring(setup, 2) + Spring(setup, 3);
        float frontShare = Mathf.Clamp(frontStiffness / (frontStiffness + rearStiffness), 0.25f, 0.8f);
        // Scale the entire transfer if any corner would lift: no negative loads,
        // and no creation of weight when clamping an unloaded inside tire.
        float transferScale = 1f;
        for (int i = 0; i < 4; i++)
        {
            float baseline = VehicleSpeed003.CornerWeightPounds(setup, fuel, i);
            float delta = (i < 2 ? -pitch : pitch) * 0.5f +
                (i % 2 == 0 ? roll : -roll) * (i < 2 ? frontShare : 1f - frontShare);
            targets[i] = delta;
            if (delta < 0f) transferScale = Mathf.Min(transferScale, baseline * 0.98f / -delta);
            if (!initialized) loads[i] = baseline;
        }
        initialized = true;
        float sum = 0f;
        for (int i = 0; i < 4; i++)
        {
            float target = VehicleSpeed003.CornerWeightPounds(setup, fuel, i) + targets[i] * transferScale;
            var shock = Corner(setup, i)?.shock;
            float damping = Mathf.Clamp01(target >= loads[i] ? shock?.compressionStiffness ?? 0.5f : shock?.reboundStiffness ?? 0.5f);
            float tau = (0.16f + damping * 0.55f) * Mathf.Sqrt(600f / Spring(setup, i));
            loads[i] = Mathf.Lerp(loads[i], target, 1f - Mathf.Exp(-seconds / tau));
            sum += loads[i];
        }
        for (int i = 0; i < 4; i++) loads[i] *= total / sum;
    }

    // Concave tire load sensitivity: doubling vertical load gives less than
    // double the grip. The weaker axle limits cornering without changing lanes.
    internal float CornerSpeedFactor(Setup setup, double fuel)
    {
        float factor = 1f;
        for (int axle = 0; axle < 2; axle++)
        {
            int a = axle * 2, b = a + 1;
            float baseline = Mathf.Pow(VehicleSpeed003.CornerWeightPounds(setup, fuel, a), 0.85f) +
                Mathf.Pow(VehicleSpeed003.CornerWeightPounds(setup, fuel, b), 0.85f);
            float grip = Mathf.Pow(Load(a, setup, fuel), 0.85f) + Mathf.Pow(Load(b, setup, fuel), 0.85f);
            factor = Mathf.Min(factor, grip / baseline);
        }
        return Mathf.Sqrt(Mathf.Clamp(factor, 0.65f, 1f));
    }
}
