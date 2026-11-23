using UnityEngine;
using System.Runtime.CompilerServices;

// A tunable carcass-temperature approximation. Stored temperature remains normalized
// for compatibility with existing event saves: zero is ambient, one is 325 F.
internal sealed class TireTemperatureModel
{
    internal const float AmbientFahrenheit = 75f;
    internal const float MaximumFahrenheit = 325f;
    private const int Samples = 512;
    private readonly float[] curvature = new float[Samples];
    private readonly int[] handlingPhases = new int[Samples];
    private readonly float length;
    private readonly float roughness;
    private readonly float warmupMinimumSpeed;
    private sealed class WarmupState { public bool Complete; }
    private readonly ConditionalWeakTable<CarTire, WarmupState> warmupStates =
        new ConditionalWeakTable<CarTire, WarmupState>();

    // At the baseline warm-up rate, 75 F reaches 185 F after 3.5 racing laps.
    // Distance-based heating gives short tracks and road courses the same lap window.
    internal static float WarmupTemperature(float current, float racingLaps) =>
        195f - (195f - current) * Mathf.Exp(-Mathf.Log(12f) * racingLaps / 3.5f);

    internal TireTemperatureModel(TrackDefinition track)
    {
        length = track.lengthMiles * TrackDefinition.MetersPerMile;
        warmupMinimumSpeed = (track.qualifyingReferenceMph > 0f
            ? track.qualifyingReferenceMph * TrackDefinition.MetersPerMile / 3600f : 63f) * 0.6f;
        roughness = Mathf.Clamp01(track.weights?.surfaceRoughness ?? 0.5f);
        var layout = track.layoutPoints?.Length > 0
            ? new TrackLayout(track.layoutPoints, length, track.widthMeters) : null;
        float spacing = length / Samples;
        for (int i = 0; i < Samples; i++)
        {
            float distance = i * spacing;
            if (layout != null)
            {
                layout.Sample(Mathf.Repeat(distance - spacing, length), out var before, out _);
                layout.Sample(Mathf.Repeat(distance + spacing, length), out var after, out _);
                before.y = after.y = 0f;
                curvature[i] = Vector3.SignedAngle(before, after, Vector3.up) * Mathf.Deg2Rad / (2f * spacing);
            }
            else
            {
                float turnLength = Mathf.PI * track.OvalTurnRadius;
                bool turning = distance >= track.OvalStraightLength &&
                    distance < track.OvalStraightLength + turnLength ||
                    distance >= 2f * track.OvalStraightLength + turnLength;
                curvature[i] = turning ? -1f / track.OvalTurnRadius : 0f;
            }
        }
        // Classify connected turns by direction and arc progress. Do this once,
        // never search the geometry during the simulation's per-car updates.
        for (int i = 0; i < Samples; i++)
        {
            handlingPhases[i] = -1;
            if (Mathf.Abs(curvature[i]) < 0.00015f) continue;
            int sign = curvature[i] < 0f ? -1 : 1;
            bool SameTurn(int j) => Mathf.Abs(curvature[j]) >= 0.00015f && (curvature[j] < 0f ? -1 : 1) == sign;
            int before = 0, after = 0;
            while (before < Samples - 1 && SameTurn((i - before - 1 + Samples) % Samples)) before++;
            while (after < Samples - 1 && SameTurn((i + after + 1) % Samples)) after++;
            handlingPhases[i] = before + after >= Samples - 1 ? 1 :
                Mathf.Min(2, (int)(3f * (before + 0.5f) / (before + after + 1f)));
        }
    }

    internal int HandlingPhaseAt(float distance) =>
        handlingPhases[Mathf.FloorToInt(Mathf.Repeat(distance, length) / length * Samples) % Samples];

    internal static float Fahrenheit(float value) =>
        Mathf.Lerp(AmbientFahrenheit, MaximumFahrenheit, value);

    internal float CurvatureAt(float distance)
    {
        float sample = Mathf.Repeat(distance, length) / length * Samples;
        int index = Mathf.FloorToInt(sample);
        return Mathf.Lerp(curvature[index % Samples], curvature[(index + 1) % Samples], sample - index);
    }

    internal Vector2 ShockMotionAt(float distance, float speed)
    {
        // Sample a fixed time window along the existing curvature lookup. No
        // geometry allocation or change to the car's lane is required.
        float halfWindow = Mathf.Max(2f, speed * 0.3f);
        float scale = speed * speed / 9.80665f;
        float before = Mathf.Clamp(CurvatureAt(distance - halfWindow) * scale, -3.5f, 3.5f);
        float after = Mathf.Clamp(CurvatureAt(distance + halfWindow) * scale, -3.5f, 3.5f);
        float roll = Mathf.Clamp((after - before) / 1.5f, -1f, 1f);
        float pitch = Mathf.Clamp((Mathf.Abs(after) - Mathf.Abs(before)) / 3f, -0.5f, 0.5f);
        return new Vector2(roll, pitch);
    }

    internal void Advance(TeamCar car, float seconds, float distance, float speed, float previousSpeed,
        TrackDefinition springTrack = null, Setup setup = null, bool racing = true, float trackFahrenheit = 90f, double fuelGallons = 0d, DynamicTireLoads dynamicLoads = null, bool onTrack = true)
    {
        if (car == null || seconds <= 0f)
            return;

        float lateralG = onTrack ? Mathf.Clamp(speed * speed * CurvatureAt(distance) / 9.80665f, -3.5f, 3.5f) : 0f;
        float brakingG = Mathf.Clamp((previousSpeed - speed) / seconds / 9.80665f, 0f, 2.7f);
        float rollingHeat = Mathf.Clamp01(speed / 55f) * 75f;
        Vector2 shockMotion = springTrack != null && onTrack ? ShockMotionAt(distance, speed) : Vector2.zero;
        // Measured braking/acceleration supplies pitch for temperature calculations.
        shockMotion.y = Mathf.Clamp((previousSpeed - speed) / seconds / 9.80665f, -1f, 1f);
        for (int i = 0; i < 4; i++)
        {
            var tire = car.GetTire(i);
            if (tire == null)
                continue;
            // Left turns put more load on the right tires, right turns on the left.
            float outsideLoad = (i % 2 == 0 ? lateralG : -lateralG);
            float cornerHeat = Mathf.Abs(lateralG) * 32f + outsideLoad * 10f;
            if (springTrack != null)
            {
                cornerHeat *= VehicleSpeed003.SpringHeatFactor(springTrack, setup, i);
                cornerHeat *= VehicleSpeed003.SwayBarHeatFactor(springTrack, setup, i, lateralG);
                cornerHeat *= VehicleSpeed003.TrackBarHeatFactor(springTrack, setup, i, lateralG);
            }
            float brakeHeat = brakingG * (i < 2 ? 22f : 8f);
            if (springTrack != null)
            {
                float shockHeat = VehicleSpeed003.ShockHeatFactor(springTrack, setup, i, shockMotion);
                float load = VehicleSpeed003.CornerTireHeatLoadFactor(setup, i, fuelGallons) *
                    (dynamicLoads?.Ratio(i, setup, fuelGallons) ?? 1f);
                cornerHeat *= shockHeat * load;
                brakeHeat *= shockHeat * load;
            }
            float pressureHeat = springTrack != null ? VehicleSpeed003.PressureHeat(setup, i, tire) : 1f;
            float target = AmbientFahrenheit + (rollingHeat + cornerHeat + brakeHeat) * pressureHeat *
                Mathf.Lerp(0.85f, 1.15f, roughness);
            if (springTrack != null)
                target += VehicleSpeed003.TrackTemperatureHeatOffset(trackFahrenheit);
            target = Mathf.Clamp(target, AmbientFahrenheit, MaximumFahrenheit);
            float current = Fahrenheit(tire.temperature);
            float timeConstant = target > current ? 100f : speed > 1f ? 150f : 300f;
            float next = Mathf.Lerp(current, target, 1f - Mathf.Exp(-seconds / timeConstant));
            var warmup = warmupStates.GetOrCreateValue(tire);
            // Tire replacement can reset an existing tire object during a pit stop.
            if (current <= AmbientFahrenheit + 0.01f)
                warmup.Complete = false;
            if (current >= 185f)
                warmup.Complete = true;
            if (!warmup.Complete && racing && speed >= warmupMinimumSpeed)
            {
                float warmupFactor = springTrack != null
                    ? VehicleSpeed003.TrackTemperatureWarmupFactor(trackFahrenheit) : 1f;
                next = WarmupTemperature(current, speed * seconds / length * warmupFactor * pressureHeat);
                if (next >= 185f)
                    warmup.Complete = true;
            }
            // Once warm, all subsequent heating/cooling uses the existing model.
            tire.temperature = Mathf.InverseLerp(AmbientFahrenheit, MaximumFahrenheit, next);
        }
    }
}