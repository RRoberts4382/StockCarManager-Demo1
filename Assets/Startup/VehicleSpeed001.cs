using System;

internal sealed class VehicleSpeed001 : IVehicleSpeedModel
{
    private const float ReferenceSpeed = 63f;
    private const float ReferenceEffectiveValue = 1f;
    private const float SpeedPerEffectiveValuePoint = 0.5f;

    public string Id => "vehicle-speed-001";
    public string Name => "VehicleSpeed001";

    public float BaseSpeed(CarSimulationContext context, int carIndex, Random random)
    {
        var weights = context.Track.weights;
        var car = carIndex < context.Cars.Count ? context.Cars[carIndex].car : null;
        if (car == null || weights == null)
            return ReferenceSpeed;

        float weightedPerformance =
            weights.aerodynamics * (car.GetComponent(CarComponent.Aerodynamics).EffectiveValue - ReferenceEffectiveValue) +
            weights.horsepower * (car.GetComponent(CarComponent.Engine).EffectiveValue - ReferenceEffectiveValue) +
            weights.brakes * (car.GetComponent(CarComponent.Brakes).EffectiveValue - ReferenceEffectiveValue) +
            weights.suspension * (car.GetComponent(CarComponent.Suspension).EffectiveValue - ReferenceEffectiveValue);

        return ReferenceSpeed + SpeedPerEffectiveValuePoint * weightedPerformance;
    }

    public float LapSpeedFactor(CarSimulationContext context, int carIndex, Random random) =>
        0.96f + (float)random.NextDouble() * 0.08f;
}
