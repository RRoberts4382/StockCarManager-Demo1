using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

[Serializable]
internal sealed class SeriesCalendarRace
{
    public string date;
    public int trackId;
    public int laps;
    public string startTime;
}

[Serializable]
internal sealed class ScheduledRace
{
    public string date;
    public int trackId;
    public string trackName;
    public int laps;
    public bool calendarLapCount;
    public string raceStartTime;
    public bool noStages;
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
    internal bool IsPlayerResult(SessionCarResult result) => !playerSkipped && result != null &&
        (result.ownerEntryId == "player" || result.playerEntry ||
         (string.IsNullOrEmpty(result.ownerEntryId) && carNumber > 0 && result.carNumber == carNumber));

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
    internal const int MaximumStages = 4;

    internal static int[] DefaultStagePercentages() => new[] { 25, 25, 50, 0 };

    public void EnsureStages(int[] defaultPercentages = null)
    {
        if (!noStages && stageCount > 0 && stageCount <= MaximumStages && stageLaps?.Length == stageCount &&
            stageLaps.All(count => count > 0) && stageLaps.Sum(count => (long)count) == laps)
            return;

        stageLaps = noStages ? Array.Empty<int>() : AllocateStageLaps(laps, defaultPercentages);
        stageCount = stageLaps.Length;
    }

    internal static int[] AllocateStageLaps(int totalLaps, int[] weights)
    {
        if (totalLaps <= 0)
            return Array.Empty<int>();

        var stages = (weights?.Any(weight => weight > 0) == true ? weights : DefaultStagePercentages())
            .Where(weight => weight > 0).Take(Math.Min(totalLaps, MaximumStages)).ToArray();
        long totalWeight = stages.Sum(weight => (long)weight);
        long cumulativeWeight = 0;
        int previousEnd = 0;
        // Round cumulative boundaries and leave at least one lap for each remaining stage.
        for (int i = 0; i < stages.Length; i++)
        {
            cumulativeWeight += stages[i];
            int roundedEnd = (int)Math.Round(totalLaps * (double)cumulativeWeight / totalWeight, MidpointRounding.AwayFromZero);
            int end = Math.Clamp(roundedEnd, previousEnd + 1, totalLaps - (stages.Length - i - 1));
            stages[i] = end - previousEnd;
            previousEnd = end;
        }
        return stages;
    }

    public int EffectiveRaceLaps
    {
        get
        {
            var raceSession = sessions?.Find(session => session.kind == RaceSessionKind.Race);
            if (raceSession?.Completed == true) return raceSession.laps;
            return calendarLapCount ? Math.Max(1, laps) : RaceSession.TestRaceLaps;
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
    public long mysteryPalletPurchases;
    public long mysteryPalletSaleProceeds;
    public long componentUpgrades;
    public long componentRepairs;
    public long sponsorIncome;
    public long sponsorSigningIncome;
    public List<ScheduledRace> races = new List<ScheduledRace>();

    internal static bool TryScheduleDate(string value, int year, out DateTime date) =>
        DateTime.TryParseExact($"{year:D4}/{value?.Trim()}", "yyyy/MM/dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static DateTime FirstSunday(DateTime date) =>
        date.AddDays(((int)DayOfWeek.Sunday - (int)date.DayOfWeek + 7) % 7);

    public static SeasonSchedule Generate(int year, IEnumerable<TrackDefinition> tracks, Random random,
        float maximumTrackLengthMiles = 0f, bool usesStages = true, int[] defaultStagePercentages = null,
        string startDate = null, string endDate = null, RacingSeries series = null)
    {
        const int minimumEvents = 36;
        var schedule = new SeasonSchedule { season = year };
        var date = FirstSunday(new DateTime(year, 2, 1)).AddDays(7);
        int? sundayCount = null;
        if (TryScheduleDate(startDate, year, out var start) && TryScheduleDate(endDate, year, out var end))
        {
            date = FirstSunday(start);
            sundayCount = date <= end ? 1 + (end - date).Days / 7 : 0;
        }

        var rotation = tracks.Where(track => maximumTrackLengthMiles <= 0f || track.lengthMiles <= maximumTrackLengthMiles)
            .GroupBy(track => track.id).Select(group => group.First()).ToArray();
        int eventsPerSeason = Math.Max(minimumEvents, rotation.Length);
        if (sundayCount.HasValue)
            eventsPerSeason = Math.Min(eventsPerSeason, sundayCount.Value);
        while (schedule.races.Count < eventsPerSeason && rotation.Length > 0)
        {
            for (int i = rotation.Length - 1; i > 0; i--)
            {
                int other = random.Next(i + 1);
                (rotation[i], rotation[other]) = (rotation[other], rotation[i]);
            }
            foreach (var track in rotation.Take(eventsPerSeason - schedule.races.Count))
            {
                schedule.races.Add(CreateRace(date, track, random, usesStages, defaultStagePercentages, series));
                if (schedule.races.Count < eventsPerSeason)
                    date = date.AddDays(7);
            }
        }
        return schedule;
    }

    internal static SeasonSchedule GenerateCalendar(int year, RacingSeries series, Func<int, TrackDefinition> findTrack, Random random)
    {
        var schedule = new SeasonSchedule { season = year };
        foreach (var entry in series.calendarRaces.OrderBy(race => race.date, StringComparer.Ordinal))
        {
            var track = findTrack(entry.trackId);
            if (track == null || !TryScheduleDate(entry.date, year, out var date))
                continue;
            schedule.races.Add(CreateRace(date, track, random, series.usesStages, series.stagePercentages, series, entry.laps, entry.startTime));
        }
        return schedule;
    }

    private static ScheduledRace CreateRace(DateTime date, TrackDefinition track, Random random,
        bool usesStages, int[] stagePercentages, RacingSeries series, int? calendarLaps = null, string startTime = null)
    {
        var race = new ScheduledRace
        {
            date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            trackId = track.id,
            trackName = track.name,
            laps = calendarLaps ?? track.raceLaps,
            calendarLapCount = calendarLaps.HasValue,
            raceStartTime = startTime,
            noStages = !usesStages,
            stageCount = calendarLaps.HasValue ? 0 : track.raceStageLaps?.Length ?? 0,
            stageLaps = calendarLaps.HasValue ? null : (int[])track.raceStageLaps?.Clone(),
            purse = RaceFinances.PurseForTrack(track.lengthMiles, track.SessionFieldSize(TrackPreview.CarCount), series),
            sessions = RaceSession.Create(random.Next(1, 3), track.NumberOfQualifyingLaps)
        };
        if (calendarLaps.HasValue)
            race.sessions.Find(session => session.kind == RaceSessionKind.Race).laps = race.laps;
        foreach (var session in race.sessions)
            SessionWeatherForecast.Ensure(session, track, race);
        race.EnsureStages(stagePercentages);
        return race;
    }
}

[Serializable]
internal sealed class EventChassisSetup
{
    public string chassis;
    public int number;
    public Setup setup;
}

// Rebuild current-season championships from recorded results, keeping each points pool separate.
internal static class ChampionshipStandings
{
    internal sealed class Row
    {
        public string key, name, numberText;
        public int number;
        public long points;
        public int wins;
        public int top5, top10;
        public bool player;
    }
    internal sealed class Tables { public List<Row> drivers, owners, manufacturers; }
    internal static bool MigrateResultIdentities(SeasonSchedule schedule, SessionCarResult[] roster)
    {
        bool changed = false;
        var player = roster.FirstOrDefault(entry => entry.playerEntry);
        foreach (var race in schedule.races ?? Enumerable.Empty<ScheduledRace>())
        foreach (var session in race.sessions ?? Enumerable.Empty<RaceSession>())
        foreach (var result in session.results ?? Enumerable.Empty<SessionCarResult>())
        {
            bool isPlayer = race.IsPlayerResult(result);
            var current = isPlayer ? player : roster.FirstOrDefault(entry => !entry.playerEntry &&
                entry.ownerEntryId == result.ownerEntryId) ??
                roster.FirstOrDefault(entry => !entry.playerEntry &&
                    entry.teamName == result.teamName && (entry.driverName == result.driverName ||
                        entry.driver?.OriginalName == result.driverName)) ??
                roster.FirstOrDefault(entry => !entry.playerEntry && entry.teamName == result.teamName && entry.carNumber == result.carNumber);
            if (current == null) continue;
            if (string.IsNullOrEmpty(result.ownerEntryId))
            { result.ownerEntryId = current.ownerEntryId; result.playerEntry = isPlayer; changed = true; }
            if (!isPlayer && string.IsNullOrEmpty(result.rivalDriverId) && !string.IsNullOrEmpty(current.rivalDriverId))
            { result.rivalDriverId = current.rivalDriverId; changed = true; }
            if (!result.manufacturerRecorded)
            { result.manufacturer = current.manufacturer; result.manufacturerRecorded = true; changed = true; }
        }
        return changed;
    }

    internal static Tables Calculate(SeasonSchedule schedule, SessionCarResult[] roster, IEnumerable<int> manufacturers)
    {
        var drivers = new Dictionary<string, Row>(StringComparer.Ordinal);
        var owners = new Dictionary<string, Row>(StringComparer.Ordinal);
        var makers = new Dictionary<string, Row>(StringComparer.Ordinal);
        string DriverKey(SessionCarResult r)
        {
            if (!r.playerEntry && !string.IsNullOrEmpty(r.rivalDriverId))
                return r.rivalDriverId;
            if (!r.playerEntry && r.ownerEntryId?.StartsWith("rival:", StringComparison.Ordinal) == true)
                return r.ownerEntryId;
            return r.driverName ?? "Unknown driver";
        }
        string OwnerKey(SessionCarResult r) => !string.IsNullOrEmpty(r.ownerEntryId) ? r.ownerEntryId : "entry:" + r.teamName + ":" + r.carNumber;
        Row Ensure(Dictionary<string, Row> table, string key, string name, int number, bool player, string numberText = null)
        {
            if (!table.TryGetValue(key, out var row)) table[key] = row = new Row { key = key };
            row.name = name; row.number = number; row.numberText = numberText; row.player |= player; return row;
        }
        foreach (var manufacturer in manufacturers) Ensure(makers, manufacturer.ToString(), ManufacturerCatalog.Name(manufacturer), 0, false);
        foreach (var entry in roster)
        {
            Ensure(drivers, DriverKey(entry), entry.driverName, entry.carNumber, entry.playerEntry, entry.DisplayCarNumber);
            Ensure(owners, OwnerKey(entry), entry.teamName, entry.carNumber, entry.playerEntry, entry.DisplayCarNumber);
            Ensure(makers, entry.manufacturer.ToString(), ManufacturerCatalog.Name(entry.manufacturer), 0, entry.playerEntry);
        }
        foreach (var race in schedule.races ?? Enumerable.Empty<ScheduledRace>())
        {
            if (!race.completed) continue;
            var results = race.sessions?.Find(session => session.kind == RaceSessionKind.Race)?.results;
            if (results == null) continue;
            var highest = new Dictionary<int, SessionCarResult>();
            foreach (var result in results)
            {
                // Results from older saves lack identity and manufacturer snapshots.
                bool player = race.IsPlayerResult(result);
                var current = player ? roster.FirstOrDefault(entry => entry.playerEntry) :
                    roster.FirstOrDefault(entry => !entry.playerEntry && entry.ownerEntryId == result.ownerEntryId) ??
                    roster.FirstOrDefault(entry => !entry.playerEntry && entry.teamName == result.teamName && entry.driverName == result.driverName) ??
                    roster.FirstOrDefault(entry => !entry.playerEntry && entry.teamName == result.teamName && entry.carNumber == result.carNumber);
                string owner = !string.IsNullOrEmpty(result.ownerEntryId) ? result.ownerEntryId : player ? "player" : current != null ? OwnerKey(current) : OwnerKey(result);
                var currentDriver = player ? null : roster.FirstOrDefault(entry => !entry.playerEntry &&
                    (!string.IsNullOrEmpty(result.rivalDriverId)
                        ? entry.rivalDriverId == result.rivalDriverId : entry.ownerEntryId == owner));
                var driverEntry = currentDriver ?? result;
                var driverRow = Ensure(drivers, DriverKey(driverEntry), driverEntry.driverName, driverEntry.carNumber, player, driverEntry.DisplayCarNumber);
                var ownerRow = Ensure(owners, owner, current?.teamName ?? result.teamName, current?.carNumber ?? result.carNumber,
                    player, (current ?? result).DisplayCarNumber);
                driverRow.points += result.pointsEarned;
                ownerRow.points += result.pointsEarned;
                if (result.position == 1) { driverRow.wins++; ownerRow.wins++; }
                if (result.position > 0 && result.position <= 5) driverRow.top5++;
                if (result.position > 0 && result.position <= 10) driverRow.top10++;
                if (!result.manufacturerRecorded && current == null) continue;
                var manufacturer = result.manufacturerRecorded ? result.manufacturer : current.manufacturer;
                Ensure(makers, manufacturer.ToString(), ManufacturerCatalog.Name(manufacturer), 0, false);
                if (result.position > 0 && (!highest.TryGetValue(manufacturer, out var best) || result.position < best.position)) highest[manufacturer] = result;
            }
            foreach (var pair in highest)
            {
                var r = pair.Value;
                if (r.position == 1) makers[pair.Key.ToString()].wins++;
                makers[pair.Key.ToString()].points += r.finishingPoints > 0 ? r.finishingPoints : r.position == 1 ? 55 : Math.Max(1, 37 - r.position);
            }
        }
        List<Row> Order(Dictionary<string, Row> rows) => rows.Values.OrderByDescending(row => row.points)
            .ThenBy(row => row.name, StringComparer.OrdinalIgnoreCase).ThenBy(row => row.number).ToList();
        return new Tables { drivers = Order(drivers), owners = Order(owners), manufacturers = Order(makers) };
    }
}

[Serializable]
internal sealed class SeasonPrizeAward
{
    public int season, rank, fieldSize, creditedSeason;
    public long amount;
    public bool paid;
    public string paymentDate;
}

internal static class SeasonPrizes
{
    // 2025 point-fund baseline, dollars, positions 1-36. Later positions use a declining game estimate.
    // https://racingnews.co/2025/10/29/nascar-payout-financials-released-in-court-documents-points-fund/
    private static readonly long[] Baseline = {
        2840000, 2406000, 2310000, 2214000, 2118000, 2021000, 1925000, 1829000,
        1709000, 1612000, 1516000, 1420000, 1324000, 1203000, 1107000, 1011000,
        910000, 804000, 707000, 606000, 505000, 404000, 303000, 193000,
        96000, 72000, 67000, 63000, 58000, 55000, 53000, 52000, 51000, 50000, 49000, 48000
    };
    internal static long Payout(int rank, double multiplier, RacingSeries series)
    {
        if (rank < 1) return 0;
        double baseline = rank <= Baseline.Length ? Baseline[rank - 1] : 48000d * Math.Pow(0.95d, rank - Baseline.Length);
        return (long)Math.Round(baseline * multiplier * series.standardRacePurse / RaceFinances.StandardPurse,
            MidpointRounding.AwayFromZero);
    }
    internal static double ValidMultiplier(double value) => double.IsNaN(value) || double.IsInfinity(value) || value < 1d ? 1d : value;

    internal static void Queue(Career career, SeasonSchedule season, SessionCarResult[] roster)
    {
        if (career.seasonPrizeAwards?.Any(award => award.season == season.season) == true) return;
        var standings = ChampionshipStandings.Calculate(season, roster, career.SeriesRules.ManufacturerChoices).owners;
        int index = standings.FindIndex(row => row.player);
        if (index < 0) return;
        // Match the shared-rank convention used by the displayed standings.
        int rank = standings.FindIndex(row => row.points == standings[index].points) + 1;
        double multiplier = ValidMultiplier(career.pointsFundMultiplier);
        career.seasonPrizeAwards = (career.seasonPrizeAwards ?? new List<SeasonPrizeAward>()).Concat(new[] {
            new SeasonPrizeAward { season = season.season, rank = rank, fieldSize = standings.Count,
                amount = Payout(rank, multiplier, career.SeriesRules) }
        }).ToList();
        // The completed season keeps its existing prize; only the upcoming season receives this increase.
        career.pointsFundMultiplier = multiplier * 1.035d;
    }

    internal static bool PayPending(Career career, out Action rollback)
    {
        rollback = null;
        var previousAwards = career.seasonPrizeAwards;
        var awards = (previousAwards ?? new List<SeasonPrizeAward>()).Select(award => new SeasonPrizeAward {
            season = award.season, rank = award.rank, fieldSize = award.fieldSize, amount = award.amount,
            paid = award.paid, creditedSeason = award.creditedSeason, paymentDate = award.paymentDate
        }).ToList();
        double previousMultiplier = career.pointsFundMultiplier;
        // Older careers may already have rolled over and discarded detailed results.
        bool recover = career.previousSeasonRankingYear > 0 && career.previousSeasonRank > 0 &&
            !awards.Any(award => award.season == career.previousSeasonRankingYear);
        if (recover) awards.Add(new SeasonPrizeAward {
            season = career.previousSeasonRankingYear, rank = career.previousSeasonRank,
            fieldSize = career.previousSeasonFieldSize,
            amount = Payout(career.previousSeasonRank, ValidMultiplier(previousMultiplier), career.SeriesRules)
        });
        var pending = awards.Where(award => !award.paid).ToArray();
        if (pending.Length == 0) return false;
        long previousBalance = career.teamBalance;
        var previousInbox = career.inbox;
        var inbox = (previousInbox ?? new List<TeamMailMessage>()).ToList();
        foreach (var award in pending)
        {
            award.paid = true; award.creditedSeason = career.schedule.season; award.paymentDate = career.currentDate;
            career.teamBalance += award.amount;
            inbox.Add(new TeamMailMessage {
                eventId = "series/season-prize/" + award.season, sender = career.seriesName,
                date = career.currentDate, subject = award.season + " season points fund prize",
                requiresRead = true,
                body = career.teamName + " finished P" + award.rank + " of " + award.fieldSize +
                    " entries in the final " + award.season + " Team Owner Points standings.\n\n" +
                    "Your season prize of " + RaceFinances.Dollars(award.amount) + " has been added to your team budget.\n\n" +
                    "The points fund for the upcoming season has increased by 3.5%."
            });
        }
        career.inbox = inbox; career.seasonPrizeAwards = awards;
        if (recover) career.pointsFundMultiplier = ValidMultiplier(previousMultiplier) * 1.035d;
        rollback = () => { career.teamBalance = previousBalance; career.inbox = previousInbox;
            career.seasonPrizeAwards = previousAwards; career.pointsFundMultiplier = previousMultiplier; };
        return true;
    }
    internal static long Income(Career career) => career.seasonPrizeAwards?.Where(award => award.paid &&
        award.creditedSeason == career.schedule.season).Sum(award => award.amount) ?? 0;
}

// TEMPORARY testing shortcut. Deliberately generates results without constructing RaceSessionRun.
internal static class TemporarySeasonResults
{
    internal static void GenerateSession(RaceSession session, ScheduledRace race, SessionCarResult[] entries, float trackMiles, Random random)
    {
        var results = entries.OrderBy(_ => random.NextDouble()).ToList();
        bool isRace = session.kind == RaceSessionKind.Race;
        float baseLap = Math.Max(0.1f, trackMiles) * 3600f / (trackMiles < 1 ? 100f : 155f);
        for (int i = 0; i < results.Count; i++)
        {
            var result = results[i]; result.position = i + 1;
            int laps = isRace ? Math.Max(1, race.laps) : session.kind == RaceSessionKind.Qualifying ? Math.Max(1, session.laps) : random.Next(5, 16);
            result.lapTimes = Enumerable.Range(0, laps).Select(_ => baseLap * (1f + i * 0.002f + (float)random.NextDouble() * 0.001f)).ToList();
            result.stagePoints = Array.Empty<int>(); result.fastestLapPoints = 0; result.finishingPoints = 0; result.pointsEarned = 0;
            result.winnings = 0;
        }
        if (isRace)
        {
            int bonusStages = race.noStages ? 0 : Math.Max(0, (race.stageLaps?.Length ?? 1) - 1);
            foreach (var result in results)
            {
                result.finishingPoints = result.position == 1 ? 55 : Math.Max(1, 37 - result.position);
                result.stagePoints = new int[bonusStages];
            }
            for (int stage = 0; stage < bonusStages; stage++)
            {
                var order = results.OrderBy(_ => random.NextDouble()).Take(10).ToArray();
                for (int i = 0; i < order.Length; i++) order[i].stagePoints[stage] = 10 - i;
            }
            var fastest = results.OrderBy(result => result.FastestLap).FirstOrDefault();
            if (fastest != null) fastest.fastestLapPoints = 1;
            foreach (var result in results)
                result.pointsEarned = result.finishingPoints + result.stagePoints.Sum() + result.fastestLapPoints;
        }
        session.results = results; session.temporaryResults = null; session.completed = true;
        if (isRace) session.laps = Math.Max(1, race.laps);
    }
}

internal static class SeriesChangeReview
{
    internal static List<Employee> EmployeesToRelease(Career career, RacingSeries nextSeries) =>
        (career.employees ?? new List<Employee>()).Where(employee => !nextSeries.StaffRoles.Contains(employee.role)).ToList();
}
