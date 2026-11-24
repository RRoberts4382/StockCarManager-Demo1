using System;
using System.Collections.Generic;
using UnityEngine;

internal static class DriverRaceProgress
{
    private const int XpPerRace = 2;

    public static Action Apply(Career career, ScheduledRace race, TrackDefinition track)
    {
        var results = race.sessions?.Find(session => session.kind == RaceSessionKind.Race)?.results;
        if (track == null || results == null || results.Count == 0)
            return null;

        var category = track.TrackCategory;
        var previous = new Dictionary<Driver, int>();
        foreach (var result in results)
        {
            if (result.position < 1)
                continue;

            var driver = result.driver ?? (!race.playerSkipped && result.carNumber == race.carNumber
                ? career.Driver : career.RivalDriver(result.teamName, result.driverName, result.rivalDriverId));
            if (driver == null || !previous.TryAdd(driver, driver.TrackXp(category)))
                continue;

            ref int xp = ref driver.TrackXp(category);
            xp = Math.Min(Driver.MaximumTrackXp, xp + XpPerRace);
        }

        return () =>
        {
            foreach (var entry in previous)
                entry.Key.TrackXp(category) = entry.Value;
        };
    }

    internal static float FinishBonus(int position, int fieldSize)
    {
        int topHalf = Math.Max(1, fieldSize / 2);
        if (position < 1 || position > topHalf)
            return 0f;
        float fraction = topHalf == 1 ? 1f : (topHalf - position) / (float)(topHalf - 1);
        return Mathf.Lerp(0.005f, 0.010f, fraction);
    }
}
