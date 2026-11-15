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
        float reference = (context.Track.id == TrackPreview.OrangeCountyTrackId || context.Track.id == TrackPreview.SouthBostonTrackId || context.Track.id == TrackPreview.DaytonaTrackId ||
            context.Track.id == TrackPreview.TexasTrackId || context.Track.id == TrackPreview.KansasTrackId ||
            context.Track.id == TrackPreview.CotaTrackId) &&
            context.Track.qualifyingReferenceMph > 0f
            ? context.Track.qualifyingReferenceMph * TrackDefinition.MetersPerMile / 3600f : ReferenceSpeed;
        var weights = context.Track.weights;
        var car = carIndex < context.Cars.Count ? context.Cars[carIndex].car : null;
        if (car == null || weights == null)
            return reference;

        float weightedPerformance =
            weights.aerodynamics * (car.GetComponent(CarComponent.Aerodynamics).EffectiveValue - ReferenceEffectiveValue) +
            weights.horsepower * (car.GetComponent(CarComponent.Engine).EffectiveValue - ReferenceEffectiveValue) +
            weights.brakes * (car.GetComponent(CarComponent.Brakes).EffectiveValue - ReferenceEffectiveValue) +
            weights.suspension * (car.GetComponent(CarComponent.Suspension).EffectiveValue - ReferenceEffectiveValue);

        return reference + SpeedPerEffectiveValuePoint * weightedPerformance;
    }

    public float LapSpeedFactor(CarSimulationContext context, int carIndex, Random random) =>
        0.96f + (float)random.NextDouble() * (context.Track.id == TrackPreview.OrangeCountyTrackId ? 0.04f : 0.08f);
}
