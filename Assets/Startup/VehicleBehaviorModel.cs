using System;
using System.Collections.Generic;

internal interface IVehicleBehaviorModel
{
    string Id { get; }
    string Name { get; }

    Func<float, float> BuildRacingLine(float lapLength, float inside, bool isOval, Func<float, UnityEngine.Vector3> heading);
    void AvoidTraffic(float lapLength, float inside, float seconds, bool allowPassing,
        float[] distances, float[] widths, bool[] onTrack, float[] lanes, float[] targets,
        float[] currentSpeeds, float[] speeds, float brakingLimit, bool[] draftLine = null, bool respondToTraffic = true, float[] brakingFactors = null);
    float LaneOffset(int carIndex, float roadWidth, float distanceToPitMerge, float racingLine);
}

internal static class VehicleBehaviorModels
{
    internal const string DefaultModelId = VehicleBehavior007.ModelId;

    public static IReadOnlyList<IVehicleBehaviorModel> Available { get; } =
        Array.AsReadOnly(new IVehicleBehaviorModel[]
        {
            new VehicleBehavior001(), new VehicleBehavior002(), new VehicleBehavior003(),
            new VehicleBehavior004(), new VehicleBehavior006(), new VehicleBehavior007()
        });

    public static int IndexForId(string id)
    {
        int defaultIndex = 0;
        for (int i = 0; i < Available.Count; i++)
        {
            if (Available[i].Id == id)
                return i;
            if (Available[i].Id == DefaultModelId)
                defaultIndex = i;
        }

        return defaultIndex;
    }
}
