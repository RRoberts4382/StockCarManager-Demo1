using System;

internal struct FourSpeedTransmission
{
    internal const float RedlineRpm = 9000f;
    private const float IdleRpm = 900f;
    private const double TireDiameterInches = 28.0;
    private const double WheelRpmPerMph = 1056.0 / (Math.PI * TireDiameterInches);
    private const double DownshiftRpm = RedlineRpm * 0.85;
    private static readonly double[] GearRatios = { 2.20, 1.64, 1.28, 1.00 };

    private readonly double driveshaftRpmPerMph;
    private int gearIndex;
    private double drivenRpm;

    internal FourSpeedTransmission(double rearGearRatio)
    {
        driveshaftRpmPerMph = WheelRpmPerMph * rearGearRatio;
        gearIndex = 0;
        drivenRpm = 0.0;
    }

    internal static double RearGearRatioForTopSpeed(double topSpeedMph) =>
        RedlineRpm / (WheelRpmPerMph * topSpeedMph * GearRatios[GearRatios.Length - 1]);

    internal int Gear => gearIndex + 1;
    internal double EngineRpm => Math.Max(IdleRpm, drivenRpm);

    internal void Update(double mph)
    {
        if (double.IsNaN(mph) || double.IsInfinity(mph) || mph < 0.0)
            mph = 0.0;

        double driveshaftRpm = mph * driveshaftRpmPerMph;
        while (gearIndex < GearRatios.Length - 1 && driveshaftRpm * GearRatios[gearIndex] >= RedlineRpm)
            gearIndex++;

        // Leave a gap below the upshift threshold so small speed changes do not undo a shift.
        while (gearIndex > 0 && driveshaftRpm * GearRatios[gearIndex - 1] <= DownshiftRpm)
            gearIndex--;

        drivenRpm = driveshaftRpm * GearRatios[gearIndex];
    }
}
