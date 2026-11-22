using System;
using System.Collections.Generic;

[Serializable]
internal sealed class GarageLapTemperature
{
    public float[] tires;
    public HandlingPhaseReview[] handling;
    public GarageRunDiagnostics diagnostics;
}

[Serializable]
internal sealed class GarageRunReview
{
    public List<float> lapTimes = new List<float>();
    public List<GarageLapTemperature> lapTemperatures = new List<GarageLapTemperature>();
    public float[] startingTemperatures;
    public float[] endingTemperatures;
    public HandlingPhaseReview[] handling;
    public bool complete;
    public string ending;
    public GarageRunDiagnostics startDiagnostics;
    public GarageRunDiagnostics endDiagnostics;
}

[Serializable]
internal sealed class RivalPracticeTelemetry
{
    public int carIndex;
    // Runs use the same finish-line interpolation and pit-lap exclusion as the player.
    public List<GarageRunReview> runs = new List<GarageRunReview>();
}

// Time-weighted feedback, not a simulated steering/yaw measurement.
[Serializable]
internal sealed class HandlingPhaseReview
{
    public double seconds, weightedBalance, weightedTires, weightedLoads, tightSeconds, looseSeconds;
    public float Average => seconds > 0 ? (float)(weightedBalance / seconds) : 0f;
    public string Description => seconds < 1d ? "Not enough cornering data" :
        tightSeconds / seconds >= 0.2 && looseSeconds / seconds >= 0.2 ? "Mixed (tight and loose)" :
        Average > 2f ? "Tight" : Average < -2f ? "Loose" : "Balanced";
    internal void Add(float duration, float balance, float tires, float loads)
    {
        if (duration <= 0f || float.IsNaN(balance) || float.IsInfinity(balance)) return;
        seconds += duration;
        weightedBalance += balance * duration;
        weightedTires += tires * duration;
        weightedLoads += loads * duration;
        if (balance > 2f) tightSeconds += duration;
        if (balance < -2f) looseSeconds += duration;
    }
    internal string Summary(string phase) => seconds < 1d ? phase + ": " + Description :
        $"{phase}: {Description} ({Average:+0.0;-0.0;0.0}, {seconds:0.0}s sampled)";
}

internal static class HandlingRunTrend
{
    internal static bool AllowsRunningState(SessionTrackState? state) =>
        !state.HasValue || state == SessionTrackState.TrackOpen || state == SessionTrackState.WhiteFlag;

    private const float SignificantChange = 2f;
    internal static string Describe(GarageRunReview run, int phase)
    {
        int count = run.lapTimes.Count;
        if (count < 10) return "Available after 10 timed laps.";
        int middleStart = (count - 3) / 2;
        if (!Window(run, phase, 0, out float early) ||
            !Window(run, phase, middleStart, out float middle) ||
            !Window(run, phase, count - 3, out float late))
            return "Not enough recorded green-flag handling data for early/middle/late comparison.";
        float change = late - early;
        float firstChange = middle - early, lastChange = late - middle;
        string trend = firstChange > SignificantChange && lastChange < -SignificantChange
            ? "Tightened mid-run, then loosened"
            : firstChange < -SignificantChange && lastChange > SignificantChange
                ? "Loosened mid-run, then tightened"
                : change > SignificantChange ? "Trending tighter"
                : change < -SignificantChange ? "Trending looser" : "Broadly stable";
        return $"{trend}. Laps 1-3: {State(early)} ({early:+0.0;-0.0;0.0}); " +
            $"{middleStart + 1}-{middleStart + 3}: {State(middle)} ({middle:+0.0;-0.0;0.0}); " +
            $"{count - 2}-{count}: {State(late)} ({late:+0.0;-0.0;0.0}). Net {change:+0.0;-0.0;0.0} points.";
    }
    private static string State(float value) => value > 2f ? "tight" : value < -2f ? "loose" : "balanced";
    private static bool Window(GarageRunReview run, int phase, int start, out float value)
    {
        double weighted = 0d, seconds = 0d;
        value = 0f;
        for (int lap = start; lap < start + 3; lap++)
        {
            if (run.lapTemperatures == null || lap >= run.lapTemperatures.Count) return false;
            var phases = run.lapTemperatures[lap]?.handling;
            if (phases?.Length != 3 || phases[phase] == null || phases[phase].seconds < 0.25d) return false;
            weighted += phases[phase].weightedBalance;
            seconds += phases[phase].seconds;
        }
        if (seconds < 1d) return false;
        value = (float)(weighted / seconds);
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

// Observe the actual driven route. Any pit transit invalidates the entire lap,
// including a lap with both crossings on track but a pit visit between them.
internal sealed class GarageRunRecorder
{
    private readonly List<GarageRunReview> reviews;
    private readonly float length;
    private readonly float line;
    private readonly Func<GarageRunDiagnostics> captureDiagnostics;
    private GarageRunReview current;
    private double previousDistance;
    private double previousTime;
    private bool previousInPit = true;
    private double lastCrossing = -1f;
    private bool validLap;
    private double highestCrossing = double.NegativeInfinity;
    private HandlingPhaseReview[] lapHandling;
    private static HandlingPhaseReview[] EmptyHandling() => new[] { new HandlingPhaseReview(), new HandlingPhaseReview(), new HandlingPhaseReview() };
    internal int CompletedLaps => current?.lapTimes.Count ?? 0;
    internal int TotalCompletedLaps { get; private set; }

    internal GarageRunRecorder(List<GarageRunReview> reviews, float length, float line,
        Func<GarageRunDiagnostics> captureDiagnostics = null)
    {
        this.reviews = reviews;
        this.length = length;
        this.line = line;
        this.captureDiagnostics = captureDiagnostics;
    }

    private static float[] Temperatures(TeamCar car)
    {
        var values = new float[4];
        for (int i = 0; i < 4; i++)
            values[i] = TireTemperatureModel.Fahrenheit(car?.GetTire(i)?.temperature ?? 0f);
        return values;
    }

    internal void Observe(double distance, double time, bool inPit, bool parked, bool moving, TeamCar car)
    {
        if (current == null && moving && !parked)
        {
            current = new GarageRunReview
            {
                startDiagnostics = captureDiagnostics?.Invoke(),
                startingTemperatures = Temperatures(car),
                endingTemperatures = Temperatures(car)
            };
            reviews.Add(current);
            lapHandling = EmptyHandling();
            lastCrossing = -1f;
            validLap = false;
            highestCrossing = double.NegativeInfinity;
            previousDistance = distance;
            previousTime = time;
            previousInPit = inPit;
        }
        if (current != null)
        {
            if (inPit || previousInPit)
            {
                validLap = false;
                lapHandling = null;
            }
            if (!inPit && !previousInPit && distance > previousDistance)
            {
                double crossingDistance = line + (Math.Floor((previousDistance - line) / length) + 1d) * length;
                while (crossingDistance <= distance)
                {
                    if (crossingDistance <= highestCrossing)
                    { crossingDistance += length; continue; }
                    highestCrossing = crossingDistance;
                    double fraction = (crossingDistance - previousDistance) / (distance - previousDistance);
                    double crossingTime = previousTime + (time - previousTime) * (float)fraction;
                    if (validLap && lastCrossing >= 0f && crossingTime > lastCrossing)
                    {
                        current.lapTimes.Add((float)(crossingTime - lastCrossing));
                        TotalCompletedLaps++;
                        var temperatures = Temperatures(car);
                        for (int tire = 0; tire < 4; tire++)
                            temperatures[tire] = current.endingTemperatures[tire] +
                                (temperatures[tire] - current.endingTemperatures[tire]) * (float)fraction;
                        current.lapTemperatures.Add(new GarageLapTemperature
                        {
                            tires = temperatures,
                            handling = lapHandling,
                            diagnostics = captureDiagnostics?.Invoke()
                        });
                    }
                    lapHandling = EmptyHandling();
                    lastCrossing = crossingTime;
                    validLap = true;
                    crossingDistance += length;
                }
            }
            for (int i = 0; i < 4; i++)
                current.endingTemperatures[i] = TireTemperatureModel.Fahrenheit(car?.GetTire(i)?.temperature ?? 0f);
            if (parked)
                Finish("Returned to stall");
        }
        previousDistance = distance;
        previousTime = time;
        previousInPit = inPit;
    }

    internal void RecordHandling(int phase, float seconds, float balance, float tires, float loads)
    {
        if (current == null || phase < 0 || phase > 2) return;
        if (current.handling == null)
            current.handling = new[] { new HandlingPhaseReview(), new HandlingPhaseReview(), new HandlingPhaseReview() };
        current.handling[phase].Add(seconds, balance, tires, loads);
        if (validLap && lapHandling != null)
            lapHandling[phase].Add(seconds, balance, tires, loads);
    }

    internal void Finish(string reason)
    {
        if (current == null)
            return;
        current.endDiagnostics = captureDiagnostics?.Invoke();
        current.complete = true;
        current.ending = reason;
        current = null;
        validLap = false;
        lastCrossing = -1f;
    }
}