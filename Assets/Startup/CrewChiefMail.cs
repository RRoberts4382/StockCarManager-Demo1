using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

[Serializable]
internal sealed class PlayerSessionReport
{
    public int position;
    public int fieldSize;
    public int laps;
    public string chassisNumber;
    public int carNumber;
    public float bestLap;
    public float damageSustained;
    public float damageAtFinish;
    public float[] addedWear;
    public float[] finalWear;
    public float[] tires;

    public PlayerSessionReport(SessionCarResult result, int fieldSize, float[] wearBefore)
    {
        position = result.position;
        this.fieldSize = fieldSize;
        laps = result.lapTimes.Count;
        chassisNumber = result.car.chassisNumber;
        carNumber = result.car.number;
        bestLap = laps > 0 ? result.FastestLap : 0f;
        damageSustained = result.damageSustained;
        damageAtFinish = result.damageAtFinish;
        finalWear = TeamCar.Components.Select(component => (float)result.car.ComponentWear(component)).ToArray();
        addedWear = finalWear.Select((wear, index) => Math.Max(0f, wear - wearBefore[index])).ToArray();
        tires = Enumerable.Range(0, 4).Select(index => result.car.GetTire(index)?.wear ?? 0f).ToArray();
    }
}

internal static class CrewChiefMail
{
    private const float RecommendedWearLimit = 0.5f;

    public static bool SendPreview(Career career, TrackCatalog catalog, Func<TeamCar, ScheduledRace, string> entryBlockReason)
    {
        if (!Career.TryReadDate(career.currentDate, out var today) || today == DateTime.MaxValue.Date)
            return false;

        string tomorrow = today.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var race = career.schedule?.races?.Find(item => !item.completed && item.date == tomorrow);
        if (race == null)
            return false;

        string id = EventId("preview", race);
        if (AlreadySent(career, id))
            return false;

        var track = catalog.Entries.Find(entry => entry.Definition.id == race.trackId)?.Definition;
        var candidates = (career.cars ?? Enumerable.Empty<TeamCar>())
            .Select(car => (Car: car, Block: car.Damage >= 100f ? "Body damage must be repaired" : entryBlockReason(car, race)))
            .ToArray();
        var best = candidates.Where(item => item.Block == null)
            .OrderByDescending(item => TrackScore(item.Car, track))
            .ThenBy(item => item.Car.number).FirstOrDefault().Car;

        var lines = new List<string> { $"We race tomorrow at {race.trackName}.", "", "Track brief" };
        if (track != null)
        {
            string location = string.Join(", ", new[] { track.location?.city, track.location?.state }
                .Where(place => !string.IsNullOrWhiteSpace(place)));
            lines.Add($"{track.lengthMiles:0.###} miles · {track.surface} · {track.minorStyle}" +
                (location.Length > 0 ? $" · {location}" : string.Empty));
            if (track.majorStyle != "Road" && track.banking != null)
                lines.Add($"Turn banking: {track.banking.turns12:0.#}° in Turns 1–2, {track.banking.turns34:0.#}° in Turns 3–4.");
            if (track.restrictorPlate)
                lines.Add("Drafting will matter on this restrictor-plate track.");
        }
        else
            lines.Add("Track specifications are unavailable.");
        lines.Add($"{race.EffectiveRaceLaps} scheduled race laps.");
        lines.Add("");
        lines.Add("Chassis recommendation");
        if (best != null)
        {
            lines.Add($"Take {best.ChassisDisplayName} ({best.ChassisTypeName}). Of the cars available, it scores best for this track and its current condition.");
            lines.Add($"Current body damage: {best.Damage:0.#}%.");
            lines.Add("Component wear: " + string.Join(", ", TeamCar.Components.Select(component =>
                $"{component} {best.ComponentWear(component):P0}")) + ".");
            AddServiceNotes(lines, best, best.Damage);
        }
        else
            lines.Add("No chassis is race-ready. Review the repair schedule before committing an entry.");
        foreach (var candidate in candidates.Where(item => item.Car != best))
        {
            float peakWear = TeamCar.Components.Max(component => candidate.Car.ComponentWear(component));
            lines.Add($"{candidate.Car.ChassisDisplayName} · {candidate.Car.ChassisTypeName} · " +
                $"body damage {candidate.Car.Damage:0.#}% · highest component wear {peakWear:P0}" +
                (candidate.Block == null ? "." : $" · {candidate.Block}."));
        }
        lines.Add("");
        lines.Add("Let's hit our marks from the first lap. The crew is ready to make the most of tomorrow.");
        Send(career, id, career.currentDate, $"Race prep at {race.trackName}", lines);
        return true;
    }

    public static void SendRecap(Career career, ScheduledRace race)
    {
        string id = EventId("recap", race);
        if (AlreadySent(career, id))
            return;

        var lines = new List<string> { $"Here’s the weekend report from {race.trackName}.", "", "Session results" };
        foreach (var session in race.sessions)
        {
            var report = session.playerReport;
            if (report == null)
            {
                int gridIndex = session.kind == RaceSessionKind.Qualifying && session.qualifyingOrder != null && race.carNumber > 0
                    ? Array.IndexOf(session.qualifyingOrder, race.carNumber) : -1;
                if (session.kind == RaceSessionKind.Race && race.playerSkipped)
                    lines.Add($"{session.name}: Team did not start.");
                else if (gridIndex >= 0)
                    lines.Add($"{session.name}: P{gridIndex + 1} of {session.qualifyingOrder.Length}.");
                else
                    lines.Add($"{session.name}: No team result recorded.");
                continue;
            }
            string timing = report.bestLap > 0f ? $" · best lap {RaceEventView.LapTime(report.bestLap)}" : string.Empty;
            lines.Add($"{session.name}: P{report.position} of {report.fieldSize} · {report.laps} laps{timing}.");
        }

        var reports = race.sessions.Select(session => session.playerReport).Where(report => report != null)
            .GroupBy(report => (report.chassisNumber, report.carNumber)).ToArray();
        lines.Add("");
        lines.Add("Car condition and service");
        if (reports.Length == 0)
            lines.Add("No chassis condition was recorded for this event.");
        foreach (var chassis in reports)
        {
            var last = chassis.Last();
            var car = career.cars?.Find(item => string.IsNullOrEmpty(last.chassisNumber)
                ? item.number == last.carNumber : item.chassisNumber == last.chassisNumber);
            float damage = last.damageAtFinish;
            lines.Add($"No. {last.carNumber} · {chassis.Sum(report => report.damageSustained):0.#} damage points sustained across the event · {damage:0.#}% damage at the end of its last session.");
            for (int i = 0; i < TeamCar.Components.Length; i++)
            {
                var component = TeamCar.Components[i];
                float added = chassis.Sum(report => report.addedWear[i]);
                lines.Add($"{component}: +{added * 100f:0.#} percentage points this event · {last.finalWear[i]:P0} wear after the event.");
            }
            lines.Add($"Tire wear at finish: LF {last.tires[0]:P0}, RF {last.tires[1]:P0}, LR {last.tires[2]:P0}, RR {last.tires[3]:P0}.");
            if (last.tires.Any(wear => wear >= 0.75f))
                lines.Add("Replace the heavily worn tires before the next run.");
            AddServiceNotes(lines, car, damage, last.finalWear);
        }
        Send(career, id, race.date, $"Race report from {race.trackName}", lines);
    }

    private static void AddServiceNotes(List<string> lines, TeamCar car, float damage, float[] finalWear = null)
    {
        if (damage > 0f)
            lines.Add($"Inspect and repair the {damage:0.#}% reported body damage before the car races again.");
        bool needsComponentWork = false;
        for (int i = 0; i < TeamCar.Components.Length; i++)
        {
            var component = TeamCar.Components[i];
            float wear = finalWear == null ? car.ComponentWear(component) : finalWear[i];
            var repair = car?.repairs?.Find(item => item.component == component);
            if (repair != null)
            {
                needsComponentWork = true;
                lines.Add($"{component}: repair already scheduled, due {repair.completionDate}.");
            }
            else if (wear >= RecommendedWearLimit)
            {
                needsComponentWork = true;
                lines.Add(wear >= ComponentCondition.RaceBlockingWear
                    ? $"{component}: repair required before entry ({wear:P0} worn)."
                    : $"{component}: service recommended before the next race ({wear:P0} worn).");
            }
        }
        if (!needsComponentWork && damage <= 0f)
            lines.Add("No repairs flagged. Complete the usual pre-race inspection.");
    }

    private static float TrackScore(TeamCar car, TrackDefinition track)
    {
        var weights = track?.weights ?? new TrackWeights();
        float performance = weights.aerodynamics * car.GetComponent(CarComponent.Aerodynamics).EffectiveValue +
            weights.horsepower * car.GetComponent(CarComponent.Engine).EffectiveValue +
            weights.brakes * car.GetComponent(CarComponent.Brakes).EffectiveValue +
            weights.suspension * car.GetComponent(CarComponent.Suspension).EffectiveValue;
        var preferred = track?.majorStyle == "Road" ? ChassisType.RoadCourse
            : track?.restrictorPlate == true ? ChassisType.SuperSpeedway
            : track?.lengthMiles < 1f ? ChassisType.ShortTrack : ChassisType.IntermediateTrack;
        return performance + (track != null && car.chassisType == preferred ? 0.5f : 0f) - car.Damage / 20f;
    }

    private static string EventId(string kind, ScheduledRace race) => $"crew-{kind}/{race.date}/{race.trackId}";

    public static void UndoNewMessages(Career career, int inboxCount)
    {
        if (career.inbox?.Count > inboxCount)
            career.inbox.RemoveRange(inboxCount, career.inbox.Count - inboxCount);
    }

    private static bool AlreadySent(Career career, string id) => career.inbox?.Any(message => message.eventId == id) == true;

    private static void Send(Career career, string id, string date, string subject, List<string> lines)
    {
        var chief = career.employees?.Find(employee => employee.role == EmployeeRole.CrewChief);
        career.inbox ??= new List<TeamMailMessage>();
        career.inbox.Add(new TeamMailMessage
        {
            eventId = id,
            sender = chief != null ? $"{chief.FullName} · Crew Chief" : "Race Operations",
            date = date,
            subject = subject,
            body = string.Join("\n", lines)
        });
    }
}
