using System;
using System.IO;
using System.Linq;
using UnityEngine;

internal static class RivalRosterStore
{
    private const int LegacySlotCount = 23;

    [Serializable]
    private sealed class Roster
    {
        public Profile[] drivers;
    }

    [Serializable]
    private sealed class Profile
    {
        public TeamCar car;
        public string name;
    }

    internal static Career Open(RacingSeries series)
    {
        var career = new Career
        {
            seriesId = series.id,
            seriesName = series.name,
            cars = new() { new TeamCar { number = 27 } }
        };
        // Seed the built-in generator consistently for each series. A career's
        // random state and player choices must never influence the master roster.
        int seed = 17;
        foreach (char character in series.id)
            seed = unchecked(seed * 31 + character);
        var random = new System.Random(seed);
        career.InitializeCars(random, randomizeRivals: true);
        career.AssignRivalManufacturers(random);
        career.InitializeEmployees(random);

        var entries = TrackPreview.CreateTimingEntries(career.cars[0], career);
        var path = PathFor(series.id);
        if (File.Exists(path))
        {
            var saved = JsonUtility.FromJson<Roster>(File.ReadAllText(path, System.Text.Encoding.UTF8));
            int savedCount = saved?.drivers?.Length ?? 0;
            if ((savedCount != LegacySlotCount && savedCount != entries.Length - 1) ||
                saved.drivers.Any(profile => profile?.car == null || profile.car.number <= 0 ||
                    profile.car.number > savedCount || !TeamCar.IsValidRivalNumber(profile.car.RivalNumber) ||
                    (string.IsNullOrEmpty(profile.car.rivalRaceNumberText) && profile.car.rivalRaceNumber == 0) ||
                    string.IsNullOrWhiteSpace(profile.name)) ||
                saved.drivers.Select(profile => profile.car.number).Distinct().Count() != savedCount ||
                saved.drivers.Select(profile => profile.car.RivalNumber).Distinct(StringComparer.OrdinalIgnoreCase).Count() != savedCount)
                throw new IOException("The shared rival roster is invalid.");

            foreach (var profile in saved.drivers)
            {
                var entry = entries[profile.car.number];
                ApplyProfile(profile.car, entry.car, series);
                entry.driver.rivalNameOverride = profile.name;
            }

            // Existing numbers take priority when the added slots are populated.
            var usedNumbers = saved.drivers.Select(profile => profile.car.RivalNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries.Skip(savedCount + 1))
            {
                if (usedNumbers.Add(entry.car.RivalNumber))
                    continue;
                var available = Enumerable.Range(1, 99).Select(number =>
                    number.ToString(System.Globalization.CultureInfo.InvariantCulture)).First(usedNumbers.Add);
                entry.car.SetRivalNumber(available);
            }
        }

        foreach (var entry in entries.Skip(1))
            entry.car.rivalDriverId = DriverId(series.id, entry.car.number);
        // Edit the actual shared car numbers, independent of any player's car.
        career.cars[0].number = 0;
        return career;
    }

    internal static void Save(Career master)
    {
        var profiles = TrackPreview.CreateTimingEntries(master.cars[0], master).Skip(1).Select(entry =>
        {
            var profile = new TeamCar { number = entry.car.number };
            ApplyProfile(entry.car, profile, master.SeriesRules);
            return new Profile { car = profile, name = entry.driverName };
        }).ToArray();
        var json = JsonUtility.ToJson(new Roster { drivers = profiles }, true);
        SeriesSettingsStore.AtomicWrite(PathFor(master.seriesId), temporary =>
            File.WriteAllText(temporary, json, new System.Text.UTF8Encoding(false)));
    }

    internal static bool ApplyToCareer(Career career)
    {
        var series = career.SeriesRules;
        var entries = TrackPreview.CreateTimingEntries(career.cars[0], career);
        foreach (var entry in entries.Skip(1))
            entry.rivalDriverId = DriverId(series.id, entry.car.number);

        // Resolve historical names and numbers against the career's old roster
        // before replacing any profile fields with master values.
        bool changed = career.schedule != null &&
            ChampionshipStandings.MigrateResultIdentities(career.schedule, entries);
        Career master;
        try
        {
            master = Open(series);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
        {
            Debug.LogWarning("Could not apply shared rivals for " + series.name + ". " + ex.Message);
            return changed;
        }

        var profiles = TrackPreview.CreateTimingEntries(master.cars[0], master);
        foreach (var entry in entries.Skip(1))
        {
            var profile = profiles[entry.car.number];
            changed |= ApplyProfile(profile.car, entry.car, series);
            if (entry.driver == null)
                continue;
            var name = profile.driverName == entry.driver.OriginalName ? null : profile.driverName;
            if (name != entry.driver.rivalNameOverride)
            {
                entry.driver.rivalNameOverride = name;
                changed = true;
            }
        }
        return changed;
    }

    private static bool ApplyProfile(TeamCar profile, TeamCar target, RacingSeries series)
    {
        var make = series.ManufacturerChoices.Contains(profile.manufacturer)
            ? profile.manufacturer : target.manufacturer;
        var paint = profile.PaintStyle;
        bool changed = target.rivalDriverId != profile.rivalDriverId || target.rivalTeamName != profile.rivalTeamName ||
            target.RivalNumber != profile.RivalNumber || target.manufacturer != make ||
            target.rivalManufacturerEdited || !target.rivalManufacturerAssigned || !target.rivalLiveryAssigned ||
            !paint.Equals(target.PaintStyle);
        target.rivalDriverId = profile.rivalDriverId;
        target.rivalTeamName = profile.rivalTeamName;
        target.rivalRaceNumber = profile.rivalRaceNumber;
        target.rivalRaceNumberText = profile.rivalRaceNumberText?.ToUpperInvariant();
        target.manufacturer = make;
        target.rivalManufacturerEdited = false;
        target.rivalManufacturerAssigned = true;
        target.rivalLiveryAssigned = true;
        target.modelResource = series.ModelFor(make);
        if (!paint.Equals(target.PaintStyle))
            paint.ApplyTo(target);
        return changed;
    }

    private static string DriverId(string seriesId, int slot) =>
        seriesId + "/rival/" + slot.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string PathFor(string seriesId) => Path.Combine(Application.persistentDataPath, "Rivals",
        Uri.EscapeDataString(RacingSeries.CanonicalId(seriesId)) + ".json");
}
