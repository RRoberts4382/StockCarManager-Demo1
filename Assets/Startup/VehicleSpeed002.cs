using System;

internal sealed class VehicleSpeed002 : IVehicleSpeedModel
{
    private const float ReferenceSpeed = 63f;
    private const float ReferenceRating = 1f;
    private const float SpeedPerRatingPoint = 0.5f;

    public string Id => "vehicle-speed-002";
    public string Name => "VehicleSpeed002";

    public float BaseSpeed(CarSimulationContext context, int carIndex, Random random)
    {
        float reference = context.Track.qualifyingReferenceMph > 0f
            ? context.Track.qualifyingReferenceMph * TrackDefinition.MetersPerMile / 3600f
            : ReferenceSpeed;
        var weights = context.Track.weights;
        var car = carIndex < context.Cars.Count ? context.Cars[carIndex].car : null;
        if (car == null || weights == null)
            return reference;

        float weightedPerformance =
            weights.aerodynamics * (car.GetComponent(CarComponent.Aerodynamics).EffectiveValue - ReferenceRating) +
            weights.horsepower * (car.GetComponent(CarComponent.Engine).EffectiveValue - ReferenceRating) +
            weights.brakes * (car.GetComponent(CarComponent.Brakes).EffectiveValue - ReferenceRating) +
            weights.suspension * (car.GetComponent(CarComponent.Suspension).EffectiveValue - ReferenceRating);

        return reference + SpeedPerRatingPoint * weightedPerformance;
    }

    public float LapSpeedFactor(CarSimulationContext context, int carIndex, Random random) =>
        0.96f + (float)random.NextDouble() * 0.08f;
}
