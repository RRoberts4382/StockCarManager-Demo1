using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

[Serializable]
public sealed class TrackDefinition
{
    public int id;
    public string name;
    public TrackLocation location = new TrackLocation();
    public const float DefaultAspectRatio = 34f / 19f;
    public const float MetersPerMile = 1609.344f;
    public const float MetersPerFoot = 0.3048f;

    public float qualifyingReferenceMph;
    public float lengthMiles = 0.5f;
    public float aspectRatio = DefaultAspectRatio;
    public string majorStyle = "Oval";
    public string minorStyle = "Oval";
    public string surface = "Asphalt";
    public bool restrictorPlate;
    public TrackBanking banking = new TrackBanking();
    public float widthMeters = 9f;
    public int pitStalls;
    public int NumberOfQualifyingLaps = 1;
    public int raceLaps = 20;
    public int[] raceStageLaps;
    public TrackWeights weights = new TrackWeights();
    public TrackLayoutPoint[] layoutPoints = Array.Empty<TrackLayoutPoint>();
    public TrackRaceMarkers raceMarkers;
    public TrackPitRoad pitRoad;

    public static readonly string[] MinorStyles =
        { "Oval", "Tri-Oval", "Quad Oval", "Triangle", "Rectangle", "Road Course", "Street Course", "Roval" };
    public static readonly string[] Surfaces = { "Asphalt", "Concrete", "Dirt" };

    public float OvalTurnRadius => lengthMiles * MetersPerMile / (4f * (aspectRatio - 1f) + 2f * Mathf.PI);
    public float OvalStraightLength => 2f * OvalTurnRadius * (aspectRatio - 1f);
    public float StartFinishFraction => raceMarkers?.hasStartFinish == true ? raceMarkers.startFinish
        : (layoutPoints?.Length ?? 0) > 0 ? 0f : OvalStraightLength * 0.5f / (lengthMiles * MetersPerMile);

    public string Validate()
    {
        if (id <= 0 || string.IsNullOrWhiteSpace(name))
            return "Enter a positive ID and a track name.";
        if (location == null || string.IsNullOrWhiteSpace(location.city) || string.IsNullOrWhiteSpace(location.state))
            return "Enter a city and state.";
        if (!InRange(lengthMiles, 0.01f, 100f) || !InRange(widthMeters, 3f, 100f) || pitStalls < 0 || raceLaps <= 0)
            return "Use 0.01–100 miles for length, 9.84–328.08 feet for width, nonnegative pit stalls, and at least one lap.";
        if (Array.IndexOf(MinorStyles, minorStyle) < 0 || Array.IndexOf(Surfaces, surface) < 0 ||
            majorStyle != (Array.IndexOf(MinorStyles, minorStyle) < 5 ? "Oval" : "Road"))
            return "Choose a valid surface and matching major and minor styles.";
        if (banking == null || !new[] { banking.turns12, banking.turns34, banking.frontStraight, banking.backStraight }
            .All(value => InRange(value, 0f, 60f)))
            return "Enter banking angles from 0 to 60 degrees.";
        if (weights == null || !new[] { weights.surfaceRoughness, weights.aerodynamics, weights.horsepower,
            weights.brakes, weights.suspension, weights.handling, weights.tireWear }.All(value => InRange(value, 0f, 1f)))
            return "Enter weights from 0 to 1.";

        if (!InRange(qualifyingReferenceMph, 0f, 300f))
            return "Enter a qualifying reference from 0 to 300 mph.";
        if (NumberOfQualifyingLaps <= 0)
            return "Enter at least one qualifying lap.";

        if (raceStageLaps?.Length > 0 &&
            (raceStageLaps.Any(count => count <= 0) || raceStageLaps.Sum(count => (long)count) != raceLaps))
            return "Race stage laps must be positive and add up to the race length.";

        string extensionError = TrackUtilityGeometry.Validate(this);
        if (extensionError != null)
            return extensionError;

        int count = layoutPoints?.Length ?? 0;
        if (count == 0)
        {
            if (minorStyle != "Oval")
                return "Add layout points for this track style.";
            if (float.IsNaN(aspectRatio) || float.IsInfinity(aspectRatio) || aspectRatio < 1f)
                return "Enter an oval aspect ratio of at least 1.";
            return OvalTurnRadius > widthMeters * 0.5f + 3f
                ? null : "The turn radius is too small for this track width and aspect ratio.";
        }
        if (count < 3 || count > 256)
            return "Supply between 3 and 256 layout points.";
        for (int i = 0; i < count; i++)
        {
            var point = layoutPoints[i];
            var next = layoutPoints[(i + 1) % count];
            if (point == null || next == null || !InRange(point.x, -100000f, 100000f) ||
                !InRange(point.z, -100000f, 100000f) || !InRange(point.banking, -60f, 60f) || !InRange(point.elevationMeters, -10000f, 10000f) ||
                !(point.widthMeters == 0f || InRange(point.widthMeters, 3f, 100f)) ||
                !InRange(point.apronWidthMeters, 0f, 100f))
                return "Layout points need valid coordinates and elevations, banking from -60 to 60 degrees, road width of 3–100 m (or 0 to inherit), and apron width of 0–100 m.";
            if (point.x == next.x && point.z == next.z)
                return "Adjacent layout points must differ. The game closes the loop automatically.";
        }
        return null;
    }

    private static bool InRange(float value, float min, float max) => value >= min && value <= max;
}

[Serializable]
public sealed class TrackLocation { public string city; public string state; }

[Serializable]
public sealed class TrackBanking
{
    public float turns12 = 20f;
    public float turns34 = 20f;
    public float frontStraight = 5f;
    public float backStraight = 5f;
}

[Serializable]
public sealed class TrackWeights
{
    public float surfaceRoughness = 0.5f;
    public float aerodynamics = 0.5f;
    public float horsepower = 0.5f;
    public float brakes = 0.5f;
    public float suspension = 0.5f;
    public float handling = 0.5f;
    public float tireWear = 0.5f;
    public bool ratingsAssigned;
}

[Serializable]
public sealed class TrackLayoutPoint
{
    public float x;
    public float z;
    public float banking;
    public float bankingSpread;
    public float elevationMeters = 0f;
    public float widthMeters;
    public float apronWidthMeters;
}

[Serializable]
public sealed class TrackRaceMarkers
{
    public bool hasStartFinish;
    public float startFinish;
    public bool hasRestartZone;
    public float restartZoneStart = 0.90f;
    public float restartZoneEnd = 0.98f;
}

[Serializable]
public sealed class TrackPitPoint
{
    public float x;
    public float z;
}

[Serializable]
public sealed class TrackPitRoad
{
    public bool enabled;
    public float widthMeters = 8f;
    public float speedLimitMph = 45f;
    public float speedLimitStart = 0.10f;
    public float speedLimitEnd = 0.90f;
    public TrackPitPoint[] points = Array.Empty<TrackPitPoint>();
    public TrackPitPoint[] entrance = Array.Empty<TrackPitPoint>();
    public TrackPitPoint[] exit = Array.Empty<TrackPitPoint>();
    public TrackPitPoint[] stalls = Array.Empty<TrackPitPoint>();
}

public sealed class TrackCatalog
{
    public sealed class Entry
    {
        public string Path { get; }
        public TrackDefinition Definition { get; }
        public string OriginalJson { get; }
        public Entry(string path, TrackDefinition definition, string originalJson)
        {
            Path = path;
            Definition = definition;
            OriginalJson = originalJson;
        }
        public override string ToString() => $"{Definition.name} (#{Definition.id})";
    }

    public List<Entry> Entries { get; } = new List<Entry>();
    public string Folder { get; } = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Tracks");
    public string Status { get; private set; }

    public void Refresh()
    {
        Entries.Clear();
        var errors = new List<string>();
        try
        {
            bool firstRun = !Directory.Exists(Folder);
            Directory.CreateDirectory(Folder);
            if (firstRun)
            {
                var sample = Resources.Load<TextAsset>("Startup/RiversideOval");
                if (sample != null)
                    File.WriteAllText(Path.Combine(Folder, "1.json"), sample.text);
            }

            var ids = new HashSet<int>();
            foreach (string file in Directory.EnumerateFiles(Folder, "*.json").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var definition = new TrackDefinition();
                    JsonUtility.FromJsonOverwrite(json, definition);
                    string error = definition.Validate();
                    if (error == null && !ids.Add(definition.id))
                        error = $"Duplicate ID {definition.id}.";
                    if (error == null)
                        Entries.Add(new Entry(file, definition, json));
                    else
                        errors.Add($"{Path.GetFileName(file)}: {error}");
                }
                catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException)
                {
                    errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
                }
            }
            if (Entries.Count > 0)
            {
                float shortest = Entries.Min(entry => entry.Definition.lengthMiles);
                float longest = Entries.Max(entry => entry.Definition.lengthMiles);
                foreach (var entry in Entries)
                    TrackRatings.Apply(entry.Definition, shortest, longest);
            }
            Entries.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Definition.name, b.Definition.name));
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            errors.Add(ex.Message);
        }
        Status = string.Join("\n", errors);
    }

    public void Save(TrackDefinition definition, Entry existing)
    {
        int highestId = 0;
        definition.id = existing?.Definition.id ?? 0;
        // Read current files in case a track was added after the form opened.
        foreach (string file in Directory.EnumerateFiles(Folder, "*.json"))
        {
            if (string.Equals(file, existing?.Path, StringComparison.OrdinalIgnoreCase))
                continue;
            TrackDefinition other;
            try { other = JsonUtility.FromJson<TrackDefinition>(File.ReadAllText(file)); }
            catch (ArgumentException) { continue; }
            if (other == null)
                continue;

            highestId = Math.Max(highestId, other.id);
            if (existing != null && other.id == definition.id)
                throw new ArgumentException($"Track ID {definition.id} is already in use.");
        }

        if (existing == null)
        {
            if (highestId == int.MaxValue)
                throw new ArgumentException("No track ID is available above the current highest ID.");
            definition.id = highestId + 1;
        }

        string error = definition.Validate();
        if (error != null)
            throw new ArgumentException(error);

        var otherLengths = Entries.Where(entry => entry != existing).Select(entry => entry.Definition.lengthMiles);
        var lengths = otherLengths.Append(definition.lengthMiles).ToArray();
        TrackRatings.Apply(definition, lengths.Min(), lengths.Max());

        string path = existing?.Path ?? Path.Combine(Folder, $"{definition.id}.json");
        string temporaryPath = Path.Combine(Folder, $"{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, JsonUtility.ToJson(definition, true));
            if (existing == null)
                File.Move(temporaryPath, path);
            else
            {
                VerifyUnchanged(existing);
                File.Replace(temporaryPath, path, null);
            }
        }
        finally
        {
            File.Delete(temporaryPath);
        }
        Refresh();
    }

    public void Delete(Entry entry)
    {
        VerifyUnchanged(entry);
        new TrackReferenceFiles(Folder).Delete(entry.Definition.id);
        File.Delete(entry.Path);
        Refresh();
    }

    private static void VerifyUnchanged(Entry entry)
    {
        if (!File.Exists(entry.Path) || File.ReadAllText(entry.Path) != entry.OriginalJson)
            throw new IOException("The track file changed outside the game. Reopen Tracks to reload it.");
    }
}
