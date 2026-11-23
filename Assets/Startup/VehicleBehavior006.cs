using System;
using UnityEngine;

internal sealed class VehicleBehavior006 : VehicleBehavior004
{
    internal new const string ModelId = "vehicle-behavior-006";

    public override string Id => ModelId;
    public override string Name => "VehicleBehavior006";

    internal override Func<float, float> BuildSweepingLine(float lapLength, float inside, TrackDefinition track,
        Func<float, Vector3> heading, Func<float, float, Vector3> point, Func<float, float> width,
        out Func<float, float> trackingWeight, out Func<float, float> kinkSteeringBlend)
    {
        var line = base.BuildSweepingLine(lapLength, inside, track, heading, point, width,
            out trackingWeight, out kinkSteeringBlend);

        // Follow the next step of the same line through the approach and exit
        // shoulders. TrackPreview still gates this advance for traffic and pits.
        if (track.majorStyle == "Oval")
            trackingWeight = _ => 1f;
        return line;
    }
}
