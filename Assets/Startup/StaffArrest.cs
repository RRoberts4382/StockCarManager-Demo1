using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

[Serializable]
internal sealed class StaffArrest
{
    public string eventId;
    public string arrestDate;
    public string releaseDate;

    public bool IsActiveOn(string careerDate) =>
        Career.TryReadDate(careerDate, out var day) &&
        Career.TryReadDate(arrestDate, out var start) && day >= start &&
        Career.TryReadDate(releaseDate, out var release) && day < release;

    private static readonly string[] Offenses =
    {
        "jaywalking across Main Street", "shoplifting a roll of toilet paper",
        "driving with expired tags", "ignoring a posted no-parking sign",
        "riding an electric scooter on the sidewalk", "setting off fireworks after curfew"
    };

    public static Action SendDue(Career career, DateTime previous, DateTime destination)
    {
        var undo = new List<Action>();
        for (int year = previous.Year; year <= destination.Year; year++)
        {
            var start = new DateTime(year, 1, 1);
            var middle = start.AddMonths(6);
            SendInPeriod(start.AddDays(1), 0, (middle - start).Days - 1);
            SendInPeriod(middle, 1, 184);
        }
        return undo.Count == 0 ? null : () =>
        {
            for (int index = undo.Count - 1; index >= 0; index--) undo[index]();
        };

        void SendInPeriod(DateTime start, int period, int days)
        {
            string id = $"scenario/staff-arrest/{start.Year}/{period}";
            var random = ScenarioMail.ScenarioRandom(career, id);
            var day = start.AddDays(random.Next(days));
            if (day <= previous || day > destination || TeamCommunications.HasReceivedMail(career, id)) return;

            string dayText = DateText(day);
            var staff = new List<(string Name, Func<StaffArrest> Get, Action<StaffArrest> Set, Employee Crew)>();
            foreach (var employee in career.employees ?? Enumerable.Empty<Employee>())
                staff.Add((employee.FullName, () => employee.arrest, arrest => employee.arrest = arrest, employee));
            foreach (var driver in career.drivers ?? Enumerable.Empty<Driver>())
                staff.Add((driver.FullName, () => driver.arrest, arrest => driver.arrest = arrest, null));
            staff.RemoveAll(person => person.Get()?.IsActiveOn(dayText) == true);
            if (staff.Count == 0) return;

            var person = staff[random.Next(staff.Count)];
            var release = AddDays(day, 14);
            var prior = person.Get();
            var arrest = new StaffArrest { eventId = id, arrestDate = dayText, releaseDate = DateText(release) };
            person.Set(arrest);
            var undoJobs = ShiftJobs(career, person.Crew, day, (release - day).Days);
            string offense = Offenses[random.Next(Offenses.Length)];
            int bail = random.Next(4, 21) * 50;
            var mail = new TeamMailMessage
            {
                eventId = id,
                sender = "Shop Office",
                date = dayText,
                subject = $"{person.Name} is unavailable",
                body = $"{person.Name} was arrested for {offense}. They are out for two weeks and due back on {arrest.releaseDate}. " +
                    $"Bail costs {RaceFinances.Dollars(bail)} from the team budget and gets them back to work two days after payment. " +
                    "You can also let the absence run its course or dismiss them.",
                scenarioOffer = TeamMailOffer.StaffArrest,
                scenarioPurchaseCost = bail,
                scenarioOptions = new[] { "Pay Bail", "Decline", "Fire Employee" }
            };
            career.inbox ??= new List<TeamMailMessage>();
            career.inbox.Add(mail);
            undo.Add(() =>
            {
                career.inbox.Remove(mail);
                undoJobs?.Invoke();
                person.Set(prior);
            });
        }
    }

    public static string Respond(Career career, TeamMailMessage mail, int choice, out Action rollback)
    {
        rollback = null;
        if (choice == 1) return null;
        var crew = career.employees?.Find(person => person.arrest?.eventId == mail.eventId);
        var driver = career.drivers?.Find(person => person.arrest?.eventId == mail.eventId);
        var arrest = crew?.arrest ?? driver?.arrest;
        if (arrest == null) return "This employee is no longer available for this decision. Select Decline to clear the email.";

        if (!Career.TryReadDate(career.currentDate, out var today) ||
            !Career.TryReadDate(arrest.releaseDate, out var originalRelease))
            return "The employee's return date is unavailable.";

        if (choice == 2)
        {
            var undoJobs = ShiftJobs(career, crew, today, -Math.Max(0, (originalRelease - today).Days));
            Action restoreRoster = crew != null ? Remove(career.employees, crew) : Remove(career.drivers, driver);
            rollback = () => { restoreRoster(); undoJobs?.Invoke(); };
            return null;
        }

        var early = AddDays(today, 2);
        if (early >= originalRelease) return "Paying bail would no longer bring the employee back sooner.";
        if (career.teamBalance < mail.scenarioPurchaseCost) return "The team budget cannot cover bail.";

        long balance = career.teamBalance;
        career.teamBalance -= mail.scenarioPurchaseCost;
        arrest.releaseDate = DateText(early);
        var restoreJobs = ShiftJobs(career, crew, today, (early - originalRelease).Days);
        rollback = () =>
        {
            restoreJobs?.Invoke();
            arrest.releaseDate = DateText(originalRelease);
            career.teamBalance = balance;
        };
        return null;
    }

    private static Action Remove<T>(List<T> roster, T person)
    {
        int index = roster.IndexOf(person);
        roster.RemoveAt(index);
        return () => roster.Insert(index, person);
    }

    private static Action ShiftJobs(Career career, Employee employee, DateTime from, int days)
    {
        if (employee == null || days == 0) return null;
        var changed = new List<(ComponentRepair Job, string Date)>();
        foreach (var job in career.cars.SelectMany(car => car.repairs ?? Enumerable.Empty<ComponentRepair>()))
        {
            if (!job.IsAssignedTo(employee) || !Career.TryReadDate(job.completionDate, out var due) || due <= from) continue;
            changed.Add((job, job.completionDate));
            job.completionDate = DateText(AddDays(due, days));
        }
        return () =>
        {
            foreach (var entry in changed) entry.Job.completionDate = entry.Date;
        };
    }

    private static DateTime AddDays(DateTime day, int days) =>
        day.AddDays(Math.Min(days, (DateTime.MaxValue.Date - day).Days));

    private static string DateText(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

