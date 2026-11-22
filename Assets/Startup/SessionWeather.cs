using System;
using UnityEngine;

// Fictional session forecasts, persisted with the career. Coefficients are initial
// game calibration, not measured weather or a location-specific meteorology model.
[Serializable]
internal sealed class SessionWeatherForecast
{
    public string id;
    public float ambientFahrenheit;
    public float cloudCover;
    public float windMph;
    public float startHour;
    public float variationPhase;

    internal bool IsValid => !string.IsNullOrWhiteSpace(id) &&
        InRange(ambientFahrenheit, -40f, 140f) && InRange(cloudCover, 0f, 1f) &&
        InRange(windMph, 0f, 100f) && InRange(startHour, 0f, 23.999f) &&
        !float.IsNaN(variationPhase) && !float.IsInfinity(variationPhase);

    private static bool InRange(float value, float minimum, float maximum) =>
        !float.IsNaN(value) && !float.IsInfinity(value) && value >= minimum && value <= maximum;

    internal static bool IsNightAt(float hour) => hour < 6f || hour >= 18f;

    internal static SessionWeatherForecast Ensure(RaceSession session, TrackDefinition track, ScheduledRace race)
    {
        // Unity may materialize an empty inline object for fields absent in an
        // older career. Non-null alone does not mean a forecast was generated.
        if (session.weather != null && session.weather.IsValid) return session.weather;
        int index = Math.Max(0, race?.sessions?.IndexOf(session) ?? 0);
        string key = track.id + ":" + (race?.date ?? "test") + ":" + session.kind + ":" + session.name + ":" + index;
        uint hash = 2166136261;
        foreach (char c in key) hash = unchecked((hash ^ c) * 16777619);
        var random = new System.Random((int)(hash & 0x7fffffff));
        int month = Career.TryReadDate(race?.date, out var date) ? date.Month : 6;
        float seasonal = 70f + 16f * Mathf.Cos((month - 7f) / 12f * Mathf.PI * 2f);
        bool nightRace = session.kind == RaceSessionKind.Race && track.id == TrackPreview.BristolTrackId && month >= 8;
        return session.weather = new SessionWeatherForecast
        {
            id = key,
            ambientFahrenheit = Mathf.Clamp(seasonal + (float)random.NextDouble() * 16f - 8f, 40f, 98f),
            cloudCover = (float)random.NextDouble(),
            windMph = 2f + (float)random.NextDouble() * 16f,
            startHour = nightRace ? 20f : session.kind == RaceSessionKind.Practice ? 10f + index :
                session.kind == RaceSessionKind.Qualifying ? 13f : 15f,
            variationPhase = (float)random.NextDouble() * Mathf.PI * 2f
        };
    }

    internal string Summary => $"Forecast: {ambientFahrenheit:0}\u00B0F air \u00B7 Clouds {cloudCover:P0} \u00B7 Wind {windMph:0} mph \u00B7 Start {startHour:00}:00";
}

internal sealed class SessionWeatherState
{
    private readonly SessionWeatherForecast forecast;
    private readonly string surface;
    private double weatherSeconds;
    private double lastSessionElapsed;
    public bool CanLock { get; }
    public bool IsLocked { get; private set; }
    internal void SetLocked(bool locked) => IsLocked = CanLock && locked;
    public float AmbientFahrenheit { get; private set; }
    public float TrackFahrenheit { get; private set; }
    public float CloudCover { get; private set; }
    public float WindMph { get; private set; }
    public float Sunlight { get; private set; }
    internal bool IsNight { get; private set; }
    public float GripFactor => GripAt(TrackFahrenheit);
    public string Summary => $"Air {AmbientFahrenheit:0}\u00B0F \u00B7 Track {TrackFahrenheit:0}\u00B0F \u00B7 Clouds {CloudCover:P0} \u00B7 Wind {WindMph:0} mph";

    internal SessionWeatherState(SessionWeatherForecast forecast, string surface, bool practice = false)
    {
        this.forecast = forecast;
        CanLock = practice;
        IsLocked = false; // Practice weather can still be locked manually.
        this.surface = surface;
        UpdateConditions(0);
        TrackFahrenheit = TargetTemperature(AmbientFahrenheit, Sunlight, CloudCover, WindMph, surface);
    }

    private void UpdateConditions(double elapsed)
    {
        float hour = (float)((forecast.startHour + elapsed / 3600d) % 24d);
        IsNight = SessionWeatherForecast.IsNightAt(hour);
        // Broad daylight envelope: 6 AM sunrise, 6 PM sunset. No solar heating at night.
        Sunlight = Mathf.Max(0f, Mathf.Sin((hour - 6f) / 12f * Mathf.PI));
        float daily = Mathf.Cos((hour - 15f) / 12f * Mathf.PI);
        float initialDaily = Mathf.Cos((forecast.startHour - 15f) / 12f * Mathf.PI);
        AmbientFahrenheit = forecast.ambientFahrenheit + 6f * (daily - initialDaily);
        float phase = forecast.variationPhase;
        CloudCover = Mathf.Clamp01(forecast.cloudCover + 0.15f *
            ((float)Math.Sin(elapsed / 900d + phase) - Mathf.Sin(phase)));
        WindMph = Mathf.Max(0f, forecast.windMph + 3f *
            ((float)Math.Sin(elapsed / 1200d + phase) - Mathf.Sin(phase)));
    }

    internal static float TargetTemperature(float air, float sun, float cloud, float wind, string surface)
    {
        float absorption = string.Equals(surface, "Concrete", StringComparison.OrdinalIgnoreCase) ? 0.72f :
            string.Equals(surface, "Dirt", StringComparison.OrdinalIgnoreCase) ? 0.8f : 1f;
        float solarRise = 55f * Mathf.Clamp01(sun) * (1f - 0.85f * Mathf.Clamp01(cloud)) * absorption;
        return air + solarRise / (1f + Mathf.Max(0f, wind) * 0.035f);
    }

    internal static float GripAt(float temperature)
    {
        float cold = Mathf.InverseLerp(60f, 30f, temperature);
        float hot = Mathf.InverseLerp(90f, 160f, temperature);
        return 1f - 0.08f * cold - 0.18f * hot;
    }

    internal void Advance(float seconds, double elapsed)
    {
        double delta = Math.Max(0d, elapsed - lastSessionElapsed);
        lastSessionElapsed = elapsed;
        if (IsLocked) return;
        // Pause the weather timeline too, so unlocking never jumps to weather
        // that would have occurred while the user was comparing setups.
        weatherSeconds += delta;
        UpdateConditions(weatherSeconds);
        float target = TargetTemperature(AmbientFahrenheit, Sunlight, CloudCover, WindMph, surface);
        // Thermal inertia avoids sudden grip jumps when clouds or sunlight change.
        float timeConstant = string.Equals(surface, "Concrete", StringComparison.OrdinalIgnoreCase) ? 420f : 300f;
        TrackFahrenheit = Mathf.Lerp(TrackFahrenheit, target, 1f - Mathf.Exp(-seconds / timeConstant));
    }
}
