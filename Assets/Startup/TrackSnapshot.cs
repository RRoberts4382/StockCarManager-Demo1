using System;
using System.IO;
using UnityEngine;

internal sealed class TrackSnapshot
{
    private const int SamplesPerSecond = 10;
    private const int Capacity = 30 * SamplesPerSecond + 1;
    private readonly Frame[] frames = new Frame[Capacity];
    private int next;
    private int count;
    private double lastCapture = double.NegativeInfinity;

    [Serializable]
    internal sealed class Frame
    {
        public double simulationSeconds;
        public float sessionSeconds;
        public int playbackSpeed;
        public string flag;
        public bool pitsOpen;
        public CarSample[] cars;
    }

    [Serializable]
    internal struct CarSample
    {
        public int number, flatTireMask;
        public bool visible, inPit, retired, spinning, servicing, pitRequested, collisionFlash;
        public float trackMeters, pitMeters, laneMeters, mph, damage;
        public double fuelGallons;
        public float nextPitEntryMeters;
        public Vector3 position;
    }

    [Serializable]
    private sealed class FileData
    {
        public int formatVersion = 1;
        public string savedUtc;
        public string trackModel, speedModel, behaviorModel, session;
        public TrackDefinition track;
        public Frame[] frames;
    }

    public Frame BeginFrame(double simulationSeconds, int carCount, bool force)
    {
        if (force && count > 0 && simulationSeconds == lastCapture)
            return frames[(next + Capacity - 1) % Capacity];
        if (!force && simulationSeconds - lastCapture < 1.0 / SamplesPerSecond - 0.00001)
            return null;

        lastCapture = simulationSeconds;
        var frame = frames[next] ??= new Frame { cars = new CarSample[carCount] };
        next = (next + 1) % Capacity;
        count = Math.Min(count + 1, Capacity);
        frame.simulationSeconds = simulationSeconds;
        return frame;
    }

    public string Save(CarSimulationContext context, string trackModel, string speedModel, string behaviorModel)
    {
        string directory = Path.Combine(Application.persistentDataPath, "TrackSnapshots");
        Directory.CreateDirectory(directory);
        DateTime now = DateTime.UtcNow;
        var data = new FileData
        {
            savedUtc = now.ToString("o"),
            trackModel = trackModel,
            speedModel = speedModel,
            behaviorModel = behaviorModel,
            session = context.Session.name,
            track = context.Track,
            frames = new Frame[count]
        };
        int oldest = (next + Capacity - count) % Capacity;
        for (int i = 0; i < count; i++)
            data.frames[i] = frames[(oldest + i) % Capacity];

        string path = Path.Combine(directory, $"snapshot-{now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonUtility.ToJson(data, true));
        return path;
    }
}
