using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

internal sealed class RaceReportMail
{
    internal sealed class SessionRow
    {
        internal string Name;
        internal string Position;
        internal string Laps;
        internal string BestLap;
        internal string Status;
    }

    internal sealed class ComponentRow
    {
        internal CarComponent Component;
        internal float Wear;
        internal string WearText;
        internal string AddedWearText;
        internal bool RequiresRepair;
    }

    internal sealed class ChassisSection
    {
        internal string Heading;
        internal string Summary;
        internal readonly List<ComponentRow> Components = new();
        internal readonly List<string> Notes = new();
        internal int CarNumber;
        internal float Damage;
    }

    private const string NoCondition = "No chassis condition was recorded for this event.";
    private const string RequiredRepair = "repair required before entry";
    private static readonly Regex SessionLine = new(
        @"^(?<name>.+?): (?:(?<position>P[0-9]+ of [0-9]+)(?: \u00B7 (?<laps>[0-9]+) laps?(?: \u00B7 best lap (?<lap>[0-9]+:[0-9]+(?:[.,][0-9]+)?))?)?|(?<status>Team did not start|No team result recorded))\.$",
        RegexOptions.CultureInvariant);
    private static readonly Regex ChassisLine = new(
        @"^(?<heading>No\. (?<number>[0-9]+)) \u00B7 (?<summary>[0-9]+(?:[.,][0-9]+)? damage points sustained across the event \u00B7 (?<damage>[0-9]+(?:[.,][0-9]+)?)\s*% damage at the end of its last session\.)$",
        RegexOptions.CultureInvariant);
    private static readonly Regex ComponentLine = new(
        @"^(?<name>[^:]+): \+(?<added>[0-9]+(?:[.,][0-9]+)?) percentage points this event \u00B7 (?<wear>(?<wearNumber>[0-9]+(?:[.,][0-9]+)?)\s*%) wear after the event\.$",
        RegexOptions.CultureInvariant);

    internal string Introduction { get; private set; }
    internal string ConditionNote { get; private set; }
    internal List<SessionRow> Sessions { get; } = new();
    internal List<ChassisSection> Chassis { get; } = new();

    internal static bool TryRead(TeamMailMessage message, Career career, out RaceReportMail report)
    {
        report = null;
        if (message?.eventId?.StartsWith("crew-recap/", StringComparison.Ordinal) != true ||
            string.IsNullOrWhiteSpace(message.body))
            return false;

        var lines = message.body.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        int condition = Array.IndexOf(lines, "Car condition and service");
        if (condition < 5 || condition + 1 >= lines.Length || lines[2] != "Session results" ||
            string.IsNullOrWhiteSpace(lines[0]) || !string.IsNullOrWhiteSpace(lines[1]) ||
            !string.IsNullOrWhiteSpace(lines[condition - 1]))
            return false;

        var parsed = new RaceReportMail { Introduction = lines[0] };
        for (int i = 3; i < condition - 1; i++)
        {
            var match = SessionLine.Match(lines[i]);
            if (!match.Success)
                return false;
            parsed.Sessions.Add(new SessionRow
            {
                Name = match.Groups["name"].Value,
                Position = ReadValue(match, "position"),
                Laps = ReadValue(match, "laps"),
                BestLap = ReadValue(match, "lap"),
                Status = ReadValue(match, "status")
            });
        }

        int index = condition + 1;
        if (lines[index] == NoCondition)
        {
            if (lines.Skip(index + 1).Any(line => !string.IsNullOrWhiteSpace(line)))
                return false;
            parsed.ConditionNote = NoCondition;
        }
        else
        {
            while (index < lines.Length && !string.IsNullOrWhiteSpace(lines[index]))
            {
                if (!TryReadChassis(lines, ref index, out var chassis))
                    return false;
                parsed.Chassis.Add(chassis);
            }
            if (parsed.Chassis.Count == 0 || lines.Skip(index).Any(line => !string.IsNullOrWhiteSpace(line)))
                return false;

            parsed.ReadRecordedWear(message.eventId, career);
        }

        report = parsed;
        return true;
    }

    private static bool TryReadChassis(string[] lines, ref int index, out ChassisSection chassis)
    {
        chassis = null;
        var heading = ChassisLine.Match(lines[index++]);
        if (!heading.Success ||
            !int.TryParse(heading.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int carNumber) ||
            !TryReadNumber(heading.Groups["damage"].Value, out float damage))
            return false;

        var section = new ChassisSection
        {
            Heading = heading.Groups["heading"].Value,
            Summary = heading.Groups["summary"].Value,
            CarNumber = carNumber,
            Damage = damage
        };
        while (index < lines.Length)
        {
            var metric = ComponentLine.Match(lines[index]);
            if (!metric.Success)
                break;
            if (!TryReadComponent(metric.Groups["name"].Value, out var component) ||
                !TryReadNumber(metric.Groups["wearNumber"].Value, out float percent) || percent > 100f ||
                section.Components.Any(item => item.Component == component))
                return false;

            section.Components.Add(new ComponentRow
            {
                Component = component,
                Wear = percent / 100f,
                WearText = metric.Groups["wear"].Value,
                AddedWearText = "+" + metric.Groups["added"].Value + " pts"
            });
            index++;
        }
        if (section.Components.Count != CarComponentDisplay.Order.Count)
            return false;
        section.Components.Sort((left, right) => DisplayIndex(left.Component).CompareTo(DisplayIndex(right.Component)));

        while (index < lines.Length && !lines[index].StartsWith("No. ", StringComparison.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(lines[index]))
                section.Notes.Add(lines[index]);
            index++;
        }

        chassis = section;
        return true;
    }

    private void ReadRecordedWear(string eventId, Career career)
    {
        var savedSections = FindSavedChassis(eventId, career);
        for (int i = 0; i < Chassis.Count; i++)
        {
            var section = Chassis[i];
            var saved = savedSections.Length == Chassis.Count && savedSections[i].carNumber == section.CarNumber
                ? savedSections[i] : null;
            float damage = saved != null && Math.Abs(saved.damageAtFinish - section.Damage) <= .051f
                ? saved.damageAtFinish : section.Damage;

            foreach (var row in section.Components)
            {
                int componentIndex = Array.IndexOf(TeamCar.Components, row.Component);
                var recorded = saved?.finalWear;
                // Historical mail rounds to whole percentages; only trust an event value that agrees with the sent mail.
                bool hasExactWear = recorded != null && componentIndex >= 0 && componentIndex < recorded.Length &&
                    recorded[componentIndex] >= 0f && recorded[componentIndex] <= 1f &&
                    Math.Abs(recorded[componentIndex] - row.Wear) <= .00501f;
                if (hasExactWear)
                    row.Wear = recorded[componentIndex];

                row.RequiresRepair = section.Notes.Any(note => IsRequiredRepair(note, row.Component)) ||
                    (hasExactWear ? row.Wear >= ComponentCondition.RaceBlockingWear : row.Wear > ComponentCondition.RaceBlockingWear) ||
                    (row.Component == CarComponent.Aerodynamics && damage >= 100f);
            }
        }
    }

    private static PlayerSessionReport[] FindSavedChassis(string eventId, Career career)
    {
        if (career == null)
            return Array.Empty<PlayerSessionReport>();

        var identity = eventId.Split('/');
        if (identity.Length != 3 || !int.TryParse(identity[2], NumberStyles.None, CultureInfo.InvariantCulture, out int trackId))
            return Array.Empty<PlayerSessionReport>();

        var race = (career.completedSeasons ?? Enumerable.Empty<SeasonSchedule>()).Prepend(career.schedule)
            .Where(season => season?.races != null).SelectMany(season => season.races)
            .FirstOrDefault(item => item?.completed == true && item.trackId == trackId && item.date == identity[1]);
        return race?.sessions?.Select(session => session?.playerReport).Where(result => result != null)
            .GroupBy(result => (result.chassisNumber, result.carNumber)).Select(group => group.Last()).ToArray()
            ?? Array.Empty<PlayerSessionReport>();
    }

    internal static bool IsRequiredRepair(string line, CarComponent component)
    {
        int separator = line.IndexOf(": ", StringComparison.Ordinal);
        return separator > 0 && TryReadComponent(line.Substring(0, separator), out var recordedComponent) &&
            component == recordedComponent &&
            line.Substring(separator + 2).StartsWith(RequiredRepair, StringComparison.Ordinal);
    }

    private static string ReadValue(Match match, string group) =>
        match.Groups[group].Success ? match.Groups[group].Value : null;

    internal static bool TryReadNumber(string value, out float number) =>
        float.TryParse(value.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number);

    internal static bool TryReadComponent(string label, out CarComponent component)
    {
        switch (label)
        {
            case "Engine": component = CarComponent.Engine; return true;
            case "Transmission": component = CarComponent.Transmission; return true;
            case "Suspension": component = CarComponent.Suspension; return true;
            case "Brakes": component = CarComponent.Brakes; return true;
            case "Aero":
            case "Aerodynamics":
            case "Body": component = CarComponent.Aerodynamics; return true;
            default: component = default; return false;
        }
    }

    private static int DisplayIndex(CarComponent component)
    {
        for (int i = 0; i < CarComponentDisplay.Order.Count; i++)
            if (CarComponentDisplay.Order[i] == component)
                return i;
        return CarComponentDisplay.Order.Count;
    }
}
