using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

[Serializable]
internal sealed class ScheduledRace
{
    public string date;
    public int trackId;
    public string trackName;
    public int laps;
    public int stageCount;
    public int[] stageLaps;
    public long purse;
    public long sponsorIncome;
    public List<SponsorRacePayment> sponsorPayments;
    public bool completed = false;
    public bool playerSkipped = false;
    public RaceExpenses expenses;
    public int carNumber = 0;
    public string carChassisNumber = string.Empty;
    public List<RaceSession> sessions;
    public void EnsureStages()
    {
        if (stageCount > 0 && stageLaps?.Length == stageCount &&
            stageLaps.All(count => count > 0) && stageLaps.Sum(count => (long)count) == laps)
            return;

        if (laps <= 0)
        {
            stageCount = 0;
            stageLaps = Array.Empty<int>();
        }
        else if (laps == 400 && trackName?.IndexOf("Charlotte", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            stageLaps = new[] { 100, 100, 100, 100 };
        }
        else if (laps < 3)
        {
            stageLaps = Enumerable.Repeat(1, laps).ToArray();
        }
        else
        {
            int openingStage = Math.Max(1, (int)Math.Round(laps / 4.0, MidpointRounding.AwayFromZero));
            stageLaps = new[] { openingStage, openingStage, laps - openingStage * 2 };
        }

        stageCount = stageLaps.Length;
    }

    public int EffectiveRaceLaps
    {
        get
        {
            var raceSession = sessions?.Find(session => session.kind == RaceSessionKind.Race);
            return raceSession?.Completed == true ? raceSession.laps : RaceSession.TestRaceLaps;
        }
    }
}

[Serializable]
internal sealed class SeasonSchedule
{
    public int season;
    public long payrollPaid = 0;
    public long carPurchases = 0;
    public long carSales;
    public long componentUpgrades;
    public long componentRepairs;
    public long sponsorIncome;
    public List<ScheduledRace> races = new List<ScheduledRace>();

    public static SeasonSchedule Generate(int year, IEnumerable<TrackDefinition> tracks, Random random,
        DateTime? previousRace = null)
    {
        var schedule = new SeasonSchedule { season = year };
        var date = new DateTime(year, 1, 1);
        date = date.AddDays(((int)DayOfWeek.Sunday - (int)date.DayOfWeek + 7) % 7);
        if (previousRace.HasValue && date <= previousRace.Value)
            date = previousRace.Value.AddDays(7);

        foreach (var track in tracks)
        {
            for (int race = 0; race < 2; race++)
            {
                schedule.races.Add(new ScheduledRace
                {
                    trackId = track.id,
                    trackName = track.name,
                    laps = track.raceLaps,
                    stageCount = track.raceStageLaps?.Length ?? 0,
                    stageLaps = (int[])track.raceStageLaps?.Clone(),
                    purse = RaceFinances.PurseForTrack(track.lengthMiles),
                    sessions = RaceSession.Create(random.Next(1, 3), track.NumberOfQualifyingLaps)
                });
            }
        }

        for (int i = schedule.races.Count - 1; i > 0; i--)
        {
            int other = random.Next(i + 1);
            var race = schedule.races[i];
            schedule.races[i] = schedule.races[other];
            schedule.races[other] = race;
        }

        foreach (var race in schedule.races)
        {
            race.EnsureStages();
            race.date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            date = date.AddDays(7);
        }
        return schedule;
    }
}
