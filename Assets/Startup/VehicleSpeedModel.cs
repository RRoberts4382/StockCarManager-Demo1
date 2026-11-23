using System;
using System.Collections.Generic;

internal interface IVehicleSpeedModel
{
    string Id { get; }
    string Name { get; }
    bool UsesLapSpeedProfile { get; }

    float BaseSpeed(CarSimulationContext context, int carIndex, Random random);
    float LapSpeedFactor(CarSimulationContext context, int carIndex, Random random);
}

internal static class VehicleSpeedModels
{
    public static IReadOnlyList<IVehicleSpeedModel> Available { get; } =
        Array.AsReadOnly(new IVehicleSpeedModel[] { new VehicleSpeed001(), new VehicleSpeed002(), new VehicleSpeed003() });

    // A full-scale supplier rating is neutral. Each tenth below it costs 0.5 m/s
    // of base speed independently of the track's component weights.
    internal static float SupplierPowerAdjustment(CarSimulationContext context, int carIndex)
    {
        int index = context.EqualConditionTesting ? 0 : carIndex;
        if (index < 0 || index >= context.Cars.Count ||
            context.Cars[index]?.enginePowerRating is not float power || float.IsNaN(power))
            return 0f;
        return 5f * (UnityEngine.Mathf.Clamp01(power) - 1f);
    }

    internal static float ApplyEngineAndDriverPerformance(CarSimulationContext context, int carIndex, float baseSpeed)
    {
        var driver = carIndex >= 0 && carIndex < context.Cars.Count ? context.Cars[carIndex]?.driver : null;
        float penalty = driver?.moraleInitialized == true
            ? 0.05f * (1f - UnityEngine.Mathf.Clamp01(driver.morale / 0.50f)) : 0f;
        return (baseSpeed + SupplierPowerAdjustment(context, carIndex)) * (1f - penalty);
    }

    public static int IndexForId(string id)
    {
        for (int i = 0; i < Available.Count; i++)
        {
            if (Available[i].Id == id)
                return i;
        }

        return 0;
    }
}
