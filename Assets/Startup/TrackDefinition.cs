using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

internal enum TrackValidationSection { Details, Layout, Race, Pit, Ratings }
public enum TrackFuelFillSide { Automatic, Left, Right }
public enum TrackLaneMarkingCoverage
{
    None = 0,
    StraightsOnly = 1,
    FullLap = 2,
    StraightsAndOuterRow = 3,
    BottomRow = 4,
    OuterRowInZone = 5
}

[Serializable]
public sealed class TrackDefinition
{
    public int id;
    public string name;
    public string logo;
    public TrackLocation location = new TrackLocation();
    public const float DefaultAspectRatio = 34f / 19f;
    public const float MetersPerMile = 1609.344f;
    public const float MetersPerFoot = 0.3048f;

    public float qualifyingReferenceMph;
    // Venue-specific VehicleSpeed003 corner calibration, applied before car
    // setup/condition penalties. One preserves existing tracks and A/B models.
    public float cornerSpeedCalibration = 1f;
    public float lengthMiles = 0.5f;
    public float aspectRatio = DefaultAspectRatio;
    public string majorStyle = "Oval";
    public string minorStyle = "Oval";
    public string surface = "Asphalt";
    public TrackLaneMarkingCoverage dashedLaneMarkings = TrackLaneMarkingCoverage.StraightsOnly;
    // Lap fractions measured from layout station zero. The zone can cross station zero.
    public float dashedLaneZoneStart;
    public float dashedLaneZoneEnd;
    public bool restrictorPlate;
    internal ChassisType TrackCategory => majorStyle == "Road" ? ChassisType.RoadCourse
        : restrictorPlate ? ChassisType.SuperSpeedway
        : lengthMiles < 1f ? ChassisType.ShortTrack : ChassisType.IntermediateTrack;
    public TrackBanking banking = new TrackBanking();
    public float widthMeters = 9f;
    public int pitStalls;

    internal int SessionFieldSize(int requested)
    {
        int marked = pitRoad?.stalls?.Length ?? 0;
        // Legacy layouts can specify capacity without providing stall markers.
        int capacity = pitStalls > 0 ? pitStalls : marked;
        if (capacity == 0) return requested;
        if (marked > 0) capacity = Math.Min(capacity, marked);
        return Math.Min(requested, capacity);
    }
    public int NumberOfQualifyingLaps = 1;
    public int raceLaps = 20;
    public int[] raceStageLaps;
    public TrackWeights weights = new TrackWeights();
    public TrackLayoutPoint[] layoutPoints = Array.Empty<TrackLayoutPoint>();
    // Signed offsets in metres along the banked surface, sampled at uniform
    // lap intervals from layout station zero. The last sample wraps to the first.
    public float[] preferredLineLaneMetres = Array.Empty<float>();
    public TrackBankingTransition[] bankingTransitions;
    // Opt in to continuous interpolation between authored bank values in layoutPoints.
    public bool smoothLayoutBanking;
    public TrackRaceMarkers raceMarkers;
    public TrackPitRoad pitRoad;

    public static readonly string[] MinorStyles =
        { "Oval", "Tri-Oval", "Quad Oval", "Triangle", "Rectangle", "D-Oval", "Road Course", "Street Course", "Roval" };
    public static readonly string[] Surfaces = { "Asphalt", "Concrete", "Dirt" };

    public float OvalTurnRadius => lengthMiles * MetersPerMile / (4f * (aspectRatio - 1f) + 2f * Mathf.PI);
    public float OvalStraightLength => 2f * OvalTurnRadius * (aspectRatio - 1f);
    public float StartFinishFraction => raceMarkers?.hasStartFinish == true ? raceMarkers.startFinish
        : (layoutPoints?.Length ?? 0) > 0 ? 0f : OvalStraightLength * 0.5f / (lengthMiles * MetersPerMile);

    internal string CarModelForTrack(string modelResource, int lod = 5)
    {
        var car = CarTypeDefinition.ForModel(modelResource);
        string variant = TrackCategory switch
        {
            ChassisType.SuperSpeedway => car.superspeedwayModel,
            ChassisType.ShortTrack => car.shortTrackModel,
            ChassisType.RoadCourse when !string.IsNullOrWhiteSpace(car.roadCourseLeftModel) ||
                !string.IsNullOrWhiteSpace(car.roadCourseRightModel) =>
                RoadCourseFuelFillSide() == TrackFuelFillSide.Right ? car.roadCourseRightModel : car.roadCourseLeftModel,
            _ => null
        };
        return string.IsNullOrWhiteSpace(variant) ? car.ModelForLod(modelResource, lod) :
            car.ModelForTrackLod(variant, lod);
    }

    internal TrackFuelFillSide RoadCourseFuelFillSide()
    {
        if (pitRoad?.fuelFillSide is TrackFuelFillSide.Left or TrackFuelFillSide.Right)
            return pitRoad.fuelFillSide;
        if (pitRoad?.enabled != true || pitRoad.points?.Length < 2 || pitRoad.points == null)
            return TrackFuelFillSide.Left;

        var route = new TrackPitSpline(pitRoad.points);
        float side = 0f;
        foreach (var marker in pitRoad.stalls ?? Array.Empty<TrackPitPoint>())
        {
            if (marker == null)
                continue;
            Vector3 stall = TrackPitSpline.Position(marker);
            float fraction = route.ClosestFraction(stall);
            side += Vector3.Dot(stall - route.Sample(fraction), RightOfPitLane(fraction));
        }

        if (Mathf.Abs(side) <= 0.1f && layoutPoints?.Length >= 3)
        {
            // Unmarked stalls are generated on the outside edge of pit road, away from the track.
            float lap = lengthMiles * MetersPerMile;
            var layout = new TrackLayout(layoutPoints, lap, widthMeters);
            Vector3 pitCenter = layout.ToTrackSpace(route.Sample(0.5f));
            float attachment = TrackUtilityGeometry.ClosestMainDistance(layout, lap, pitCenter);
            side = Vector3.Dot(pitCenter - layout.Sample(attachment, out _, out _), RightOfPitLane(0.5f));
        }

        return side > 0.1f ? TrackFuelFillSide.Right : TrackFuelFillSide.Left;

        Vector3 RightOfPitLane(float fraction)
        {
            Vector3 forward = route.Sample(fraction + 0.001f) - route.Sample(fraction - 0.001f);
            return Vector3.Cross(Vector3.up, forward).normalized;
        }
    }

    public string Validate() => ValidationErrors().FirstOrDefault().Error;

    internal IEnumerable<(TrackValidationSection Section, string Error)> ValidationErrors()
    {
        if (id <= 0 || string.IsNullOrWhiteSpace(name))
            yield return (TrackValidationSection.Details, "Enter a positive ID and a track name.");
        if (location == null || string.IsNullOrWhiteSpace(location.city) || string.IsNullOrWhiteSpace(location.state))
            yield return (TrackValidationSection.Details, "Enter a city and state.");
        int styleIndex = Array.IndexOf(MinorStyles, minorStyle);
        int firstRoadStyle = Array.IndexOf(MinorStyles, "Road Course");
        if (styleIndex < 0 || Array.IndexOf(Surfaces, surface) < 0 ||
            majorStyle != (styleIndex < firstRoadStyle ? "Oval" : "Road"))
            yield return (TrackValidationSection.Details, "Choose a valid surface and matching major and minor styles.");

        if (!InRange(lengthMiles, .01f, 100f) || !InRange(widthMeters, 3f, 100f))
            yield return (TrackValidationSection.Layout, "Use 0.01–100 miles for length and 9.84–328.08 feet for width.");
        if (banking == null || !new[] { banking.turns12, banking.turns34, banking.frontStraight, banking.backStraight }
            .All(value => InRange(value, 0f, 60f)))
            yield return (TrackValidationSection.Layout, "Enter banking angles from 0 to 60 degrees.");
        string geometry = ValidateLayout();
        if (geometry != null)
            yield return (TrackValidationSection.Layout, geometry);
        string bankProfile = ValidateBankingTransitions();
        if (bankProfile != null)
            yield return (TrackValidationSection.Layout, bankProfile);

        if (raceLaps <= 0)
            yield return (TrackValidationSection.Race, "Enter at least one race lap.");
        if (!InRange(qualifyingReferenceMph, 0f, 300f))
            yield return (TrackValidationSection.Race, "Enter a qualifying reference from 0 to 300 mph.");
        if (!InRange(cornerSpeedCalibration, 0.5f, 1.5f))
            yield return (TrackValidationSection.Race, "Use a corner-speed calibration between 0.5 and 1.5.");
        if (NumberOfQualifyingLaps <= 0)
            yield return (TrackValidationSection.Race, "Enter at least one qualifying lap.");
        if (raceStageLaps?.Length > 0 &&
            (raceStageLaps.Length > ScheduledRace.MaximumStages || raceStageLaps.Any(count => count <= 0) ||
                raceStageLaps.Sum(count => (long)count) != raceLaps))
            yield return (TrackValidationSection.Race, "Use at most four stages with positive laps that add up to the race length.");
        string markers = TrackUtilityGeometry.ValidateMarkers(this);
        if (markers != null)
            yield return (TrackValidationSection.Race, markers);

        if (pitStalls < 0)
            yield return (TrackValidationSection.Pit, "Enter a nonnegative pit stall count.");
        if (pitRoad != null && !Enum.IsDefined(typeof(TrackFuelFillSide), pitRoad.fuelFillSide))
            yield return (TrackValidationSection.Pit, "Choose automatic, left, or right for the pit fuel-fill side.");
        string pits = TrackUtilityGeometry.ValidatePitRoad(this);
        if (pits != null)
            yield return (TrackValidationSection.Pit, pits);

        if (weights == null || !new[] { weights.surfaceRoughness, weights.aerodynamics, weights.horsepower,
            weights.brakes, weights.suspension, weights.transmission }
            .All(value => InRange(value, 0f, 1f)))
            yield return (TrackValidationSection.Ratings, "Enter weights from 0 to 1.");
    }

    private string ValidateLayout()
    {
        int count = layoutPoints?.Length ?? 0;
        if (count == 0)
        {
            if (smoothLayoutBanking)
                return "Continuous layout banking needs a custom track layout.";
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

    private string ValidateBankingTransitions()
    {
        if (bankingTransitions == null || bankingTransitions.Length == 0)
            return null;
        if ((layoutPoints?.Length ?? 0) < 3)
            return "Banking transitions need a custom track layout.";

        TrackBankingTransition previous = null;
        foreach (var transition in bankingTransitions)
        {
            if (transition == null || !InRange(transition.start, 0f, 1f) ||
                !InRange(transition.end, 0f, 1f) || transition.start >= transition.end ||
                !InRange(transition.fromDegrees, -60f, 60f) || !InRange(transition.toDegrees, -60f, 60f))
                return "Banking transitions need increasing lap fractions within 0 to 1 and angles from -60 to 60 degrees.";
            if (previous != null && (previous.end > transition.start || previous.toDegrees != transition.fromDegrees))
                return "Sort banking transitions by lap distance and match the angles between transitions without overlapping.";
            previous = transition;
        }
        return previous.toDegrees == bankingTransitions[0].fromDegrees
            ? null : "Match the banking angles on both sides of the lap seam.";
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
    public float transmission;
    public float surfaceRoughness = 0.5f;
    public float aerodynamics = 0.5f;
    public float horsepower = 0.5f;
    public float brakes = 0.5f;
    public float suspension = 0.5f;
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
public sealed class TrackBankingTransition
{
    // Fractions of the horizontal lap distance measured along the authored spline.
    public float start;
    public float end;
    public float fromDegrees;
    public float toDegrees;
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
    public TrackFuelFillSide fuelFillSide;
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

    internal static int CurrentTrackId(int id) => id switch
    {
        2 => 33,  // Martinsville
        3 => 30,  // Daytona
        8 => 31,  // Texas
        25 => 32, // Kansas
        _ => id
    };

    public Entry Find(int id)
    {
        // Keep saved event IDs intact. Only redirect the definition lookup.
        int currentId = CurrentTrackId(id);
        return Entries.Find(entry => entry.Definition.id == id) ??
            (currentId == id ? null : Entries.Find(entry => entry.Definition.id == currentId));
    }

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
                foreach (var entry in Entries)
                    TrackRatings.Apply(entry.Definition, shortest);
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
        TrackRatings.Apply(definition, lengths.Min());

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
