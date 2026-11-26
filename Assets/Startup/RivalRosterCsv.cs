using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

internal static class RivalRosterCsv
{
    internal const int MaximumRows = 40;
    internal const string HeaderText = "Car Number,Manufacturer id,driver name,team name";
    private static readonly string[] Header = HeaderText.Split(',');

    internal static (Career Draft, string[] ImportedNumbers, string[] Renumbered) Read(RacingSeries series, string path)
    {
        var draft = RivalRosterStore.Open(series);
        // Timing entries are ordered by stable roster slot, regardless of the displayed number.
        var entries = TrackPreview.CreateTimingEntries(draft.cars[0], draft).Skip(1).ToArray();
        using var reader = new StreamReader(path, new UTF8Encoding(false, true));
        var header = reader.ReadLine();
        if (header == null || !ReadFields(header, 1).SequenceEqual(Header, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Use the columns Car Number, Manufacturer id, driver name, team name, in that order.");

        int lineNumber = 1;
        var rows = new List<(string line, int number)>();
        while (reader.ReadLine() is string line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
                continue;
            if (rows.Count == MaximumRows)
                throw Error(lineNumber, "A CSV can contain at most " + MaximumRows + " rival rows.");
            rows.Add((line, lineNumber));
        }
        if (rows.Count == 0)
            throw new InvalidDataException("The CSV has no rival driver rows.");

        if (rows.Count > entries.Length)
            throw new InvalidDataException("This series has " + entries.Length + " rival slots. Remove the extra rows.");

        var choices = series.ManufacturerChoices;
        var updates = new List<(string number, int make, string driver, string team, int line)>();
        foreach (var (line, rowNumber) in rows)
        {
            var fields = ReadFields(line, rowNumber);
            if (fields.Count != Header.Length)
                throw Error(rowNumber, "Expected four columns.");
            string number = fields[0];
            if (!TeamCar.IsValidRivalNumber(number))
                throw Error(rowNumber, "Car number must be one to four letters or digits, such as 00 or 88BX.");
            number = number.ToUpperInvariant();

            int id = ManufacturerCatalog.Chevrolet;
            if ((fields[1].Length > 0 && !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out id)) ||
                !choices.Contains(id))
                throw Error(rowNumber, "Manufacturer ID is invalid for " + series.name + ".");
            if (SettingsFormStyle.TextError(fields[2]) is string driverError)
                throw Error(rowNumber, "Driver name. " + driverError);
            if (SettingsFormStyle.TextError(fields[3]) is string teamError)
                throw Error(rowNumber, "Team name. " + teamError);
            if (fields[2].Any(char.IsControl) || fields[3].Any(char.IsControl))
                throw Error(rowNumber, "Driver and team names cannot contain control characters.");

            updates.Add((number, id, fields[2], fields[3], rowNumber));
        }

        // Reserve explicitly listed numbers before assigning suffixes, even for later rows.
        var untouched = entries.Skip(updates.Count).Select(entry => entry.car.RivalNumber).ToHashSet(StringComparer.Ordinal);
        var reserved = updates.Select(update => update.number).ToHashSet(StringComparer.Ordinal);
        reserved.UnionWith(untouched);
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var imported = new List<string>(updates.Count);
        var renumbered = new List<string>();
        for (int index = 0; index < updates.Count; index++)
        {
            var update = updates[index];
            occurrences.TryGetValue(update.number, out int previous);
            occurrences[update.number] = previous + 1;
            string assigned = update.number;
            if (previous == 0)
            {
                if (untouched.Contains(assigned))
                    throw Error(update.line, "Car #" + assigned + " is already used in a rival slot left unchanged by this import.");
            }
            else
            {
                assigned = AssignSuffix(update.number, previous, reserved);
                if (assigned == null)
                    throw Error(update.line, "No unused suffix is available for car #" + update.number + ".");
                renumbered.Add("#" + update.number + " to #" + assigned);
            }

            var entry = entries[index];
            entry.car.SetRivalNumber(assigned);
            entry.driver.rivalNameOverride = update.driver;
            entry.car.rivalTeamName = update.team;
            entry.car.manufacturer = update.make;
            entry.car.modelResource = series.ModelFor(update.make);
            imported.Add(assigned);
        }

        var duplicates = TrackPreview.CreateTimingEntries(draft.cars[0], draft).Skip(1)
            .GroupBy(entry => entry.teamName, StringComparer.OrdinalIgnoreCase)
            .SelectMany(team => team.GroupBy(entry => entry.driverName, StringComparer.OrdinalIgnoreCase))
            .FirstOrDefault(names => names.Count() > 1);
        if (duplicates != null)
            throw new InvalidDataException("The same driver name appears more than once on a team: " + duplicates.Key + ".");

        return (draft, imported.ToArray(), renumbered.ToArray());
    }

    private static string AssignSuffix(string number, int previous, HashSet<string> reserved)
    {
        // A number that already uses all four characters keeps its first three.
        string stem = number.Substring(0, Math.Min(number.Length, TeamCar.MaximumRivalNumberLength - 1));
        string suffixes = previous switch
        {
            1 => "XZABCDEFGHIJKLMNOPQRSTUVW0123456789",
            2 => "YZABCDEFGHIJKLMNOPQRSTUVW0123456789",
            _ => "ZABCDEFGHIJKLMNOPQRSTUVW0123456789"
        };
        foreach (char suffix in suffixes)
        {
            string candidate = stem + suffix;
            if (reserved.Add(candidate))
                return candidate;
        }
        return null;
    }

    private static List<string> ReadFields(string line, int lineNumber)
    {
        var fields = new List<string>();
        int index = 0;
        do
        {
            while (index < line.Length && char.IsWhiteSpace(line[index]))
                index++;
            var field = new StringBuilder();
            if (index < line.Length && line[index] == '"')
            {
                index++;
                while (true)
                {
                    if (index == line.Length)
                        throw Error(lineNumber, "A quoted field is missing its closing quote.");
                    char character = line[index++];
                    if (character == '"')
                    {
                        if (index == line.Length || line[index] != '"')
                            break;
                        index++;
                    }
                    field.Append(character);
                }
                while (index < line.Length && char.IsWhiteSpace(line[index]))
                    index++;
                if (index < line.Length && line[index] != ',')
                    throw Error(lineNumber, "Expected a comma after the closing quote.");
            }
            else
            {
                while (index < line.Length && line[index] != ',')
                {
                    if (line[index] == '"')
                        throw Error(lineNumber, "Enclose fields containing quotes in double quotes.");
                    field.Append(line[index++]);
                }
            }
            fields.Add(field.ToString().Trim());
        }
        while (index < line.Length && line[index++] == ',');
        return fields;
    }

    private static InvalidDataException Error(int lineNumber, string message) =>
        new InvalidDataException("Line " + lineNumber.ToString(CultureInfo.InvariantCulture) + ". " + message);
}
