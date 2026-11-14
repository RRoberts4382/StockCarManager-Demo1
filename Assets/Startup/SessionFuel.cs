internal static class SessionFuel
{
    public const double TankCapacity = 18.0;
    public const double ReducedBurnFactor = 0.5;

    public static double GallonsPerMeter(TrackDefinition track) =>
        1.0 / (MilesPerGallon(track) * TrackDefinition.MetersPerMile);

    private static double MilesPerGallon(TrackDefinition track)
    {
        if (track.majorStyle == "Road")
            return 4.15;
        if (track.lengthMiles > 2.5f || (track.lengthMiles >= 2.5f && track.restrictorPlate))
            return 5.65;
        if (track.lengthMiles < 1f)
            return 4.5;
        return 4.75;
    }
}
