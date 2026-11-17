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
