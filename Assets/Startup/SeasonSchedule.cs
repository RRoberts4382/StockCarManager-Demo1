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
    public EventCarCondition playerCondition;
    public bool setupLocked;
    public List<EventChassisSetup> eventSetups = new List<EventChassisSetup>();
    public bool SetupLocked => setupLocked || sessions?.Any(s => s.kind == RaceSessionKind.Qualifying && s.Completed) == true;
    internal Setup EventSetup(TeamCar car) => eventSetups?.Find(s => s.chassis == car.chassisNumber && s.number == car.number)?.setup;
    internal void StoreEventSetup(TeamCar car, Setup setup)
    {
        if (SetupLocked) return;
        CaptureEventSetup(car, setup);
    }
    internal void CaptureEventSetup(TeamCar car, Setup setup)
    {
        eventSetups ??= new List<EventChassisSetup>();
        var entry = eventSetups.Find(s => s.chassis == car.chassisNumber && s.number == car.number);
        if (entry == null) { entry = new EventChassisSetup { chassis = car.chassisNumber, number = car.number }; eventSetups.Add(entry); }
        entry.setup = RivalSetup001.Copy(setup);
    }

    public EventCarCondition ConditionFor(TeamCar car) =>
        playerCondition != null && playerCondition.chassis == car.chassisNumber
            ? playerCondition : new EventCarCondition { chassis = car.chassisNumber };
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
    public long sponsorSigningIncome;
    public List<ScheduledRace> races = new List<ScheduledRace>();

    public static SeasonSchedule Generate(int year, IEnumerable<TrackDefinition> tracks, Random random)
    {
        const int eventsPerSeason = 36;
        var schedule = new SeasonSchedule { season = year };
        var date = new DateTime(year, 2, 1);
        date = date.AddDays(7 + ((int)DayOfWeek.Sunday - (int)date.DayOfWeek + 7) % 7);

        var rotation = tracks.GroupBy(track => track.id).Select(group => group.First()).ToArray();
        while (schedule.races.Count < eventsPerSeason && rotation.Length > 0)
        {
            for (int i = rotation.Length - 1; i > 0; i--)
            {
                int other = random.Next(i + 1);
                (rotation[i], rotation[other]) = (rotation[other], rotation[i]);
            }
            foreach (var track in rotation.Take(eventsPerSeason - schedule.races.Count))
            {
                var race = new ScheduledRace
                {
                    date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    trackId = track.id,
                    trackName = track.name,
                    laps = track.raceLaps,
                    stageCount = track.raceStageLaps?.Length ?? 0,
                    stageLaps = (int[])track.raceStageLaps?.Clone(),
                    purse = RaceFinances.PurseForTrack(track.lengthMiles),
                    sessions = RaceSession.Create(random.Next(1, 3), track.NumberOfQualifyingLaps)
                };
                foreach (var session in race.sessions)
                    SessionWeatherForecast.Ensure(session, track, race);
                race.EnsureStages();
                schedule.races.Add(race);
                date = date.AddDays(7);
            }
        }
        return schedule;
    }
}

[Serializable]
internal sealed class EventChassisSetup
{
    public string chassis;
    public int number;
    public Setup setup;
}
