using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

internal static class RaceMorale
{
    private const float ChangePerRace = 0.05f;
    private const float ComplaintThreshold = 0.10f;
    private const double ResignationChance = 0.20;

    public static Action Apply(Career career, ScheduledRace race)
    {
        var results = race.sessions?.Find(session => session.kind == RaceSessionKind.Race)?.results;
        if (results == null || results.Count == 0)
            return null;

        int fieldSize = results.Count;
        var finishes = results.Where(result => result.position > 0 && result.position <= fieldSize).ToArray();
        var player = race.playerSkipped ? null : Array.Find(finishes, result => result.carNumber == race.carNumber);
        var previous = new List<Action>();
        if (player != null)
        {
            float change = FinishChange(player.position, fieldSize);
            UpdateCrew(career.employees, change, sendComplaint: true);
            UpdateDriver(player.driver ?? career.Driver, change, sendComplaint: true);
        }

        var rivalFinishes = finishes.Where(result => result != player).ToLookup(result => result.teamName);
        foreach (var team in career.rivalEmployees ?? Enumerable.Empty<TeamEmployees>())
        {
            var teamFinishes = rivalFinishes[team.teamName].ToArray();
            if (teamFinishes.Length == 0)
                continue;

            UpdateCrew(team.employees, FinishChange(teamFinishes.Average(result => result.position), fieldSize));
            foreach (var finish in teamFinishes)
            {
                var driver = finish.driver ?? team.drivers?.Find(candidate => candidate.FullName == finish.driverName);
                UpdateDriver(driver, FinishChange(finish.position, fieldSize));
            }
        }

        return () =>
        {
            foreach (var undo in previous)
                undo();
        };

        void UpdateCrew(List<Employee> employees, float change, bool sendComplaint = false)
        {
            if (employees == null || change == 0f)
                return;
            foreach (var employee in employees)
            {
                float old = employee.morale;
                float oldEffective = employee.EffectiveMorale(career.currentDate);
                previous.Add(() => employee.morale = old);
                employee.morale = Mathf.Clamp01(old + change);
                if (sendComplaint && CrossedThreshold(oldEffective, employee.EffectiveMorale(career.currentDate)))
                    SendComplaint(career, race, employee.FullName, employee.RoleName, isDriver: false);
            }
        }

        void UpdateDriver(Driver driver, float change, bool sendComplaint = false)
        {
            if (driver == null || change == 0f)
                return;
            float old = driver.morale;
            previous.Add(() => driver.morale = old);
            driver.morale = Mathf.Clamp01(old + change);
            if (sendComplaint && CrossedThreshold(old, driver.morale))
                SendComplaint(career, race, driver.FullName, "Driver", isDriver: true);
        }
    }

    internal static Action ApplyResignations(Career career, ScheduledRace race, System.Random random)
    {
        if (career.inbox == null || !Career.TryReadDate(race.date, out var raceDay))
            return null;

        var undo = new List<Action>();
        int inboxCount = career.inbox.Count;
        CheckRoster(career.drivers, driver => driver.morale, driver => driver.FullName, _ => "Driver");
        CheckRoster(career.employees, employee => employee.EffectiveMorale(career.currentDate), employee => employee.FullName, employee => employee.RoleName);
        if (undo.Count == 0)
            return null;

        return () =>
        {
            for (int i = undo.Count - 1; i >= 0; i--)
                undo[i]();
            CrewChiefMail.UndoNewMessages(career, inboxCount);
        };

        void CheckRoster<T>(List<T> roster, Func<T, float> morale, Func<T, string> name, Func<T, string> role)
        {
            if (roster == null)
                return;

            for (int index = roster.Count - 1; index >= 0; index--)
            {
                var person = roster[index];
                if (morale(person) >= ComplaintThreshold)
                    continue;

                string sender = $"{name(person)} \u00b7 {role(person)}";
                var complaint = career.inbox.LastOrDefault(message => message != null && message.sender == sender &&
                    message.eventId?.StartsWith("morale-complaint/", StringComparison.Ordinal) == true);
                if (complaint == null || !Career.TryReadDate(complaint.date, out var complaintDay) ||
                    complaintDay >= raceDay || random.NextDouble() >= ResignationChance)
                    continue;

                int originalIndex = index;
                roster.RemoveAt(index);
                undo.Add(() => roster.Insert(originalIndex, person));
                career.inbox.Add(new TeamMailMessage
                {
                    eventId = $"morale-resignation/{race.date}/{race.trackId}/{role(person)}/{name(person)}",
                    sender = sender,
                    date = race.date,
                    subject = $"{name(person)} has resigned",
                    body = $"Hi team,\n\nI raised my concerns with the team and still feel it's time to leave {career.teamName}. My resignation is effective immediately.\n\n{name(person)}",
                    requiresRead = true
                });
            }
        }
    }

    private static bool CrossedThreshold(float before, float after) =>
        before >= ComplaintThreshold && after < ComplaintThreshold;

    private static void SendComplaint(Career career, ScheduledRace race, string name, string role, bool isDriver)
    {
        string complaint = isDriver
            ? $"After the race at {race.trackName}, I’m frustrated with where this team is headed. I can take responsibility for my driving, but I need a clear plan for getting the car and the team competitive. I’d like to talk before we go racing again."
            : $"The result at {race.trackName} was hard to take. I’m frustrated with how the team is handling these weekends. We keep coming back to the shop with the same problems, and I need to know what we’re going to change before the next race.";
        career.inbox ??= new List<TeamMailMessage>();
        career.inbox.Add(new TeamMailMessage
        {
            eventId = $"morale-complaint/{race.date}/{race.trackId}/{role}/{name}",
            sender = $"{name} · {role}",
            date = race.date,
            subject = isDriver ? "We need to talk about our results" : "I’m frustrated with how things are going",
            body = $"Hi team,\n\n{complaint}\n\n{name}"
        });
    }

    private static float FinishChange(double position, int fieldSize) =>
        RaceSession.ClassifyFinish(position, fieldSize) switch
        {
            RaceFinishBand.Good => ChangePerRace,
            RaceFinishBand.Bad => -ChangePerRace,
            _ => 0f
        };
}
