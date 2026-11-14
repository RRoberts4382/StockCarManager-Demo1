using System;
using System.Collections.Generic;

internal interface IVehicleBehaviorModel
{
    string Id { get; }
    string Name { get; }

    Func<float, float> BuildRacingLine(float lapLength, float inside, bool isOval, Func<float, UnityEngine.Vector3> heading);
    void AvoidTraffic(float lapLength, float inside, float seconds, bool allowPassing,
        float[] distances, float[] widths, bool[] onTrack, float[] lanes, float[] targets,
        float[] currentSpeeds, float[] speeds, float brakingLimit, bool[] draftLine = null);
    float LaneOffset(int carIndex, float roadWidth, float distanceToPitMerge, float racingLine);
}

internal static class VehicleBehaviorModels
{
    public static IReadOnlyList<IVehicleBehaviorModel> Available { get; } =
        Array.AsReadOnly(new IVehicleBehaviorModel[] { new VehicleBehavior001() });

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
