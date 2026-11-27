using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

internal sealed class TrackSnapshot
{
    private const int SamplesPerSecond = 10;
    internal const int WindowSeconds = 15;
    private const int Capacity = WindowSeconds * SamplesPerSecond + 1;
    private const int LaneStepsPerSecond = 60;
    private const int LaneCapacity = WindowSeconds * LaneStepsPerSecond + 1;
    private const double CaptureTolerance = 0.00001;
    private readonly Frame[] frames = new Frame[Capacity];
    private readonly LaneFrame[] laneFrames = new LaneFrame[LaneCapacity];
    private readonly Queue<OvalPassAttempt> ovalPassAttempts = new();
    private readonly Queue<PlateMergeAttempt> plateMergeAttempts = new();
    private int next;
    private int count;
    private int nextLaneFrame, laneFrameCount;
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
        public int ovalPassingStrictness;
        public string flag;
        public bool pitsOpen;
        public PaceCarSample paceCar;
        public CarSample[] cars;
    }

    [Serializable]
    internal struct PaceCarSample
    {
        public bool active, onPitRoad, returning, doubleFile;
        public Vector3 position;
        public float routeMeters, laneMeters, targetLaneMeters, mph, requestedMph, allowedMph;
        public int leaderCarNumber, blockedByCarNumber;
        public float leaderGapMeters, clearDistanceMeters, checkedDistanceMeters;
        public string blockReason;
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
        public float packWakeBoostMph;
        public float trailingSpeedBoostMph;
        public bool regrouping;
        public int draftBumpsGiven, draftBumpsReceived;
        public string draftPassPhase;
        public int draftPassTargetNumber;
        public int draftBlockTargetNumber;
        public float draftBlockTargetLaneMeters;
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
    internal sealed class LaneFrame
    {
        // Timestamp at the end of this simulation step, in the same timebase as frames.
        public double simulationSeconds;
        public float sessionSeconds;
        public LaneSample[] cars;
    }

    [Serializable]
    internal struct LaneSample
    {
        public int number;
        public bool onTrackAtStart, onTrackAtEnd, overlapSeparationChecked;
        public float trackStartMeters, trackEndMeters;
        public float laneStartMeters, requestedTargetLaneMeters, laneAfterTrafficMeters, laneEndMeters;
        public float laneBeforeOverlapSeparationMeters, trackBeforeOverlapSeparationMeters;
        public float overlapLaneAdjustmentMeters, overlapTrackAdjustmentMeters;
        public float headingDegrees;
        public VehicleBehavior003.TrafficLaneDecision steering;
    }

    [Serializable]
    private struct OvalPassAttempt
    {
        public double simulationSeconds;
        public int carNumber, blockerCarNumber, passingStrictness;
        public string side;
    }

    [Serializable]
    private struct PlateMergeAttempt
    {
        public double simulationSeconds;
        public int sourceHeadCarNumber, receivingTailCarNumber, receivingHeadCarNumber, queuedCarCount;
        public float sourceLaneMeters, targetLaneMeters, openingSpeedMps;
        public bool chaseLine, frontJoin;
    }

    [Serializable]
    private sealed class FileData
    {
        public int formatVersion = 32;
        public int windowSeconds = WindowSeconds;
        public int laneStepsPerSecond = LaneStepsPerSecond;
        public string poseSpace = "TrackPreview local space (meters)";
        public float centerToRearAxleMeters = RearAxleHeading.CenterToRearAxle;
        public string savedUtc, comment;
        public string trackModel, speedModel, behaviorModel, session;
        public TrackDefinition track;
        public Frame[] frames;
        public LaneFrame[] laneFrames;
        public int ovalPassAttemptCount;
        public OvalPassAttempt[] ovalPassAttempts = Array.Empty<OvalPassAttempt>();
        public int plateMergeAttemptCount;
        public PlateMergeAttempt[] plateMergeAttempts = Array.Empty<PlateMergeAttempt>();
        public List<RivalSetupChange> rivalSetupChanges;
        public List<RivalPracticeTelemetry> rivalPracticeTelemetry;
    }

    internal void RecordOvalPassAttempt(double simulationSeconds, int carNumber, int blockerCarNumber, int passSide, int passingStrictness)
    {
        ovalPassAttempts.Enqueue(new OvalPassAttempt
        {
            simulationSeconds = simulationSeconds,
            carNumber = carNumber,
            blockerCarNumber = blockerCarNumber,
            side = passSide > 0 ? "Inside" : "Outside",
            passingStrictness = passingStrictness
        });
    }

    internal void RecordPlateMergeAttempt(double simulationSeconds, int sourceHead, int receivingCar, int queuedCars,
        float sourceLane, float targetLane, float openingSpeed, bool chaseLine = false, bool frontJoin = false)
    {
        plateMergeAttempts.Enqueue(new PlateMergeAttempt
        {
            simulationSeconds = simulationSeconds,
            sourceHeadCarNumber = sourceHead,
            receivingTailCarNumber = frontJoin ? -1 : receivingCar,
            receivingHeadCarNumber = frontJoin ? receivingCar : -1,
            queuedCarCount = queuedCars,
            sourceLaneMeters = sourceLane,
            targetLaneMeters = targetLane,
            openingSpeedMps = openingSpeed,
            chaseLine = chaseLine,
            frontJoin = frontJoin
        });
    }

    public Frame BeginFrame(double simulationSeconds, int carCount, bool force)
    {
        if (force && count > 0 && simulationSeconds == lastCapture)
            return frames[(next + Capacity - 1) % Capacity];
        if (!force && simulationSeconds - lastCapture < 1.0 / SamplesPerSecond - CaptureTolerance)
            return null;

        lastCapture = simulationSeconds;
        var frame = frames[next] ??= new Frame { cars = new CarSample[carCount] };
        next = (next + 1) % Capacity;
        count = Math.Min(count + 1, Capacity);
        frame.simulationSeconds = simulationSeconds;
        int oldest = (next + Capacity - count) % Capacity;
        double cutoff = simulationSeconds - WindowSeconds - CaptureTolerance;
        while (count > 1 && frames[oldest].simulationSeconds < cutoff)
        {
            count--;
            oldest = (oldest + 1) % Capacity;
        }
        double oldestCapture = frames[oldest].simulationSeconds;
        while (ovalPassAttempts.Count > 0 && ovalPassAttempts.Peek().simulationSeconds < oldestCapture)
            ovalPassAttempts.Dequeue();
        while (plateMergeAttempts.Count > 0 && plateMergeAttempts.Peek().simulationSeconds < oldestCapture)
            plateMergeAttempts.Dequeue();
        return frame;
    }

    internal LaneFrame BeginLaneFrame(double simulationSeconds, int carCount)
    {
        var frame = laneFrames[nextLaneFrame] ??= new LaneFrame { cars = new LaneSample[carCount] };
        nextLaneFrame = (nextLaneFrame + 1) % LaneCapacity;
        laneFrameCount = Math.Min(laneFrameCount + 1, LaneCapacity);
        frame.simulationSeconds = simulationSeconds;
        return frame;
    }

    internal Task<string> SaveAsync(CarSimulationContext context, string trackModel, string speedModel, string behaviorModel,
        string comment, CancellationToken cancellationToken)
    {
        string directory = Path.Combine(Application.persistentDataPath, "TrackSnapshots");
        DateTime now = DateTime.UtcNow;
        var data = new FileData
        {
            savedUtc = now.ToString("o"),
            comment = comment ?? string.Empty,
            trackModel = trackModel,
            speedModel = speedModel,
            behaviorModel = behaviorModel,
            session = context.Session.name,
            track = context.Track,
            rivalSetupChanges = context.Session.rivalSetupChanges,
            rivalPracticeTelemetry = context.Session.rivalPracticeTelemetry,
            frames = new Frame[count],
            laneFrames = Array.Empty<LaneFrame>()
        };
        int oldest = (next + Capacity - count) % Capacity;
        for (int i = 0; i < count; i++)
            data.frames[i] = frames[(oldest + i) % Capacity];
        if (count > 0)
        {
            double latestCapture = data.frames[count - 1].simulationSeconds;
            data.laneFrames = LaneFramesThrough(latestCapture);
            data.ovalPassAttempts = AttemptsThrough(ovalPassAttempts, latestCapture, attempt => attempt.simulationSeconds);
            data.ovalPassAttemptCount = data.ovalPassAttempts.Length;
            data.plateMergeAttempts = AttemptsThrough(plateMergeAttempts, latestCapture, attempt => attempt.simulationSeconds);
            data.plateMergeAttemptCount = data.plateMergeAttempts.Length;
        }

        string path = Path.Combine(directory, $"snapshot-{now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.json");
        // Track View holds the session paused until this task completes, so its ring buffers stay frozen.
        return Task.Run(async () =>
        {
            string json = JsonUtility.ToJson(data);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(directory);
            string pendingPath = path + ".tmp";
            try
            {
                await File.WriteAllTextAsync(pendingPath, json, new UTF8Encoding(false), cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(pendingPath, path);
                return path;
            }
            finally
            {
                if (File.Exists(pendingPath))
                    File.Delete(pendingPath);
            }
        }, cancellationToken);
    }

    private LaneFrame[] LaneFramesThrough(double latestCapture)
    {
        int oldest = (nextLaneFrame + LaneCapacity - laneFrameCount) % LaneCapacity;
        double cutoff = latestCapture - WindowSeconds - CaptureTolerance;
        var included = new List<LaneFrame>(laneFrameCount);
        for (int i = 0; i < laneFrameCount; i++)
        {
            var frame = laneFrames[(oldest + i) % LaneCapacity];
            if (frame.simulationSeconds >= cutoff && frame.simulationSeconds <= latestCapture + CaptureTolerance)
                included.Add(frame);
        }
        return included.ToArray();
    }

    private static T[] AttemptsThrough<T>(Queue<T> attempts, double latestCapture, Func<T, double> simulationSeconds)
    {
        if (attempts.Count == 0)
            return Array.Empty<T>();

        var included = new List<T>(attempts.Count);
        foreach (var attempt in attempts)
        {
            if (simulationSeconds(attempt) > latestCapture)
                break;
            included.Add(attempt);
        }
        return included.ToArray();
    }
}
