using System;
using System.IO;
using UnityEngine;

internal sealed class TrackSnapshot
{
    private const int SamplesPerSecond = 10;
    private const int Capacity = 60 * SamplesPerSecond + 1;
    private readonly Frame[] frames = new Frame[Capacity];
    private int next;
    private int count;
    private double lastCapture = double.NegativeInfinity;

    [Serializable]
    internal sealed class Frame
    {
        public double simulationSeconds;
        public int renderFrameNumber, renderFramesSinceSample;
        public int connectorBuildCount;
        public double connectorBuildMilliseconds;
        public double averageFrameMilliseconds, maxFrameMilliseconds;
        public int mergeCheckCount, mergeChecksDeferred, mergePredictionSamples;
        public double mergeCheckMilliseconds, maxMergeCheckMilliseconds;
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
        public float thermalTrackPositionMeters;
        public bool thermalOnTrack;
        public bool visible, inPit, retired, spinning, servicing, pitRequested, collisionFlash;
        public float trackMeters, pitMeters, laneMeters, mph, damage;
        public float throttle, brake;
        public float preferredLaneMeters, preferredLineSpeedFactor;
        // Draft allowance in the free-running target before the corner profile and other speed limits.
        public float draftBoostMph;
        public int draftBumpsGiven, draftBumpsReceived;
        public double fuelGallons;
        public float nextPitEntryMeters;
        // All poses use TrackPreview local space, in meters. Rotation includes banking.
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 rearAxlePosition;
        public float headingDegrees;
        public bool pitManeuver, pitArrivalManeuver;
        public bool pitFrontEntry, pitBackExit, waitingForStallDeparture;
        public float pitConnectorMeters;
        public int stallDepartureBlockedByIndex;
        public string stallDepartureBlockReason;
        public bool waitingForPitExit, pitExitCleared;
        public float pitExitHoldMeters;
        public int pitExitBlockedByIndex;
        public float pitExitConflictSeconds;
        // Last rejected movement persists across 10 Hz samples; count/time identify new events.
        public bool pitMovementBlockedThisStep;
        public int pitMovementBlockedByIndex, pitMovementBlockedByCarNumber, pitMovementBlockCount;
        public string pitMovementBlockReason;
        public float pitMovementBlockSessionSeconds, pitMovementBlockClearanceMeters, pitMovementBlockPredictionSeconds;
        public int stallEntryReservationOwner;
        public float stallEntryHoldMeters;
        public bool apronExitActive, apronMergeGranted, apronEntryActive;
        public float apronMergeStartMeters, apronMergeEndMeters;
        public float pitYawDegrees, pitManeuverStartMeters, pitManeuverLengthMeters, pitManeuverTargetLaneMeters;
    }

    [Serializable]
    private sealed class FileData
    {
        public int formatVersion = 20;
        public string poseSpace = "TrackPreview local space (meters)";
        public float centerToRearAxleMeters = RearAxleHeading.CenterToRearAxle;
        public string savedUtc;
        public string trackModel, speedModel, behaviorModel, session;
        public TrackDefinition track;
        public Frame[] frames;
        public System.Collections.Generic.List<RivalSetupChange> rivalSetupChanges;
        public System.Collections.Generic.List<RivalPracticeTelemetry> rivalPracticeTelemetry;
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
            rivalSetupChanges = context.Session.rivalSetupChanges,
            rivalPracticeTelemetry = context.Session.rivalPracticeTelemetry,
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
