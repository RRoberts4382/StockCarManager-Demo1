using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

[Serializable]
internal sealed class GarageRunDiagnostics
{
    public float sessionSeconds;
    public float actualTrackPositionMeters, thermalTrackPositionMeters;
    public bool thermalOnTrack;
    public string chassis, manufacturer;
    public int carNumber;
    public string trackModel, speedModel, behaviorModel, flag;
    public bool collisions, trafficInteractions;
    public int playbackSpeed;
    public float lapSpeedFactor, damagePercent;
    public bool hasCarControls, hasSteeringAngle;
    public float steeringAngleDegrees, throttleInput, brakeInput;
    public bool hasPowertrainSample;
    public float engineRpm, speedMph;
    public SessionWeatherForecast weatherForecast;
    public bool weatherLocked;
    public float ambientFahrenheit, trackFahrenheit, cloudCover, windMph, trackGripFactor;
    public double fuelGallons;
    public bool weightDynamicsEnabled;
    public bool equalConditionTesting;
    public float vehicleWeightPounds, fuelWeightPounds;
    public float effectiveFrontWeightPercent, effectiveCrossWeightPercent, effectiveLeftWeightPercent;
    public float[] cornerWeightsPounds;
    public float[] dynamicCornerWeightsPounds;
    public float[] coldPressuresPsi, runningPressuresPsi;
    public float[] tireWear;
    public string[] componentNames;
    public float[] componentEffectiveValues;
    public Setup setup;
}

internal static class GarageGraphSnapshot
{
    [Serializable]
    private sealed class SavedRun
    {
        public int runNumber;
        public int timedLaps;
        public float bestLapSeconds, averageLapSeconds;
        public bool hasRunDiagnostics;
        public GarageRunReview data;
    }

    [Serializable]
    private sealed class Snapshot
    {
        public int formatVersion = 19;
        public string savedUtc;
        public string unityVersion;
        public string session;
        public string units = "Lap time: seconds; temperature: Fahrenheit; fuel: US gallons; tire pressure: PSI; weight: pounds (VehicleSpeed003 only); spring/bar rates: lb/in; track bar: inches; tire wear: 0–1; engine speed: RPM; vehicle speed: MPH.";
        public string sampleTiming = "Tire temperatures are interpolated at the finish line; diagnostic samples are taken at the end of the simulation step containing that crossing. Null diagnostics indicate older runs. Run-time samples are taken approximately every 0.1 seconds of simulation time from run start.";
        public TrackDefinition track;
        public bool[] visibleSeries;
        public string[] series;
        public SavedRun[] runs;
        public List<RivalSetupChange> rivalSetupChanges;
        public List<RivalPracticeTelemetry> rivalPracticeTelemetry;
    }

    internal static string Save(TrackDefinition track, RaceSession session,
        IEnumerable<(GarageRunReview Run, int Number)> selected, bool[] visibleSeries, string[] series)
    {
        var entries = selected.Select(item => new SavedRun
        {
            runNumber = item.Number,
            timedLaps = item.Run.lapTimes.Count,
            bestLapSeconds = item.Run.lapTimes.Count > 0 ? item.Run.lapTimes.Min() : 0f,
            averageLapSeconds = item.Run.lapTimes.Count > 0 ? item.Run.lapTimes.Average() : 0f,
            hasRunDiagnostics = item.Run.startDiagnostics != null,
            data = item.Run
        }).ToArray();
        if (entries.Length == 0)
            throw new InvalidOperationException("Select at least one run to save.");
        DateTime now = DateTime.UtcNow;
        var snapshot = new Snapshot
        {
            savedUtc = now.ToString("o"),
            unityVersion = Application.unityVersion,
            session = session.name,
            track = track,
            visibleSeries = (bool[])visibleSeries.Clone(),
            series = series,
            rivalSetupChanges = session.rivalSetupChanges,
            rivalPracticeTelemetry = session.rivalPracticeTelemetry,
            runs = entries
        };
        string directory = Path.Combine(Application.persistentDataPath, "TrackSnapshots");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"graph-{now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, JsonUtility.ToJson(snapshot, true));
        return path;
    }
}
