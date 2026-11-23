using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

internal sealed class RacePrepMail
{
    private const string WeightsPrefix = "Track weights (0\u20131): ";
    private const string WearPrefix = "Component wear: ";
    private static readonly Regex Rating = new(
        @"^(?<name>.+?) (?<number>[0-9]+(?:[.,][0-9]+)?)$",
        RegexOptions.CultureInvariant);
    private static readonly Regex ComponentWear = new(
        @"^(?<name>.+?) (?<wear>(?<number>[0-9]+(?:[.,][0-9]+)?)\s*%)$",
        RegexOptions.CultureInvariant);

    internal string Introduction { get; private set; }
    internal string Closing { get; private set; }
    internal string Recommendation { get; private set; }
    internal TrackWeights Weights { get; private set; }
    internal List<string> TrackNotes { get; } = new();
    internal List<string> ChassisNotes { get; } = new();
    internal List<RaceReportMail.ComponentRow> Components { get; } = new();

    internal static bool TryRead(TeamMailMessage message, out RacePrepMail prep)
    {
        prep = null;
        if (message?.eventId?.StartsWith("crew-preview/", StringComparison.Ordinal) != true ||
            string.IsNullOrWhiteSpace(message.body))
            return false;

        var lines = message.body.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        int chassis = Array.IndexOf(lines, "Chassis recommendation");
        if (chassis < 5 || chassis + 3 >= lines.Length || lines[2] != "Track brief" ||
            string.IsNullOrWhiteSpace(lines[0]) || !string.IsNullOrWhiteSpace(lines[1]) ||
            !string.IsNullOrWhiteSpace(lines[chassis - 1]) || string.IsNullOrWhiteSpace(lines[chassis + 1]))
            return false;

        int end = lines.Length - 1;
        while (string.IsNullOrWhiteSpace(lines[end]))
            end--;
        int closing = Array.FindLastIndex(lines, end, line => string.IsNullOrWhiteSpace(line));
        if (closing < chassis + 2 || closing == end)
            return false;

        var parsed = new RacePrepMail
        {
            Introduction = lines[0],
            Recommendation = lines[chassis + 1],
            Closing = string.Join("\n", lines.Skip(closing + 1).Take(end - closing))
        };
        for (int i = 3; i < chassis - 1; i++)
        {
            string line = lines[i];
            if (parsed.Weights == null && TryReadWeights(line, out var weights, out var additionalRatings))
            {
                parsed.Weights = weights;
                if (additionalRatings.Count > 0)
                    parsed.TrackNotes.Add(string.Join(", ", additionalRatings) + ".");
            }
            else
                parsed.TrackNotes.Add(line);
        }

        for (int i = chassis + 2; i < closing; i++)
        {
            string line = lines[i];
            if (parsed.Components.Count == 0 && TryReadWear(line, out var components))
                parsed.Components.AddRange(components);
            else
                parsed.ChassisNotes.Add(line);
        }

        foreach (var component in parsed.Components)
        {
            // The archived percentage may be rounded to 95% from a lower recorded value.
            component.RequiresRepair = component.Wear > ComponentCondition.RaceBlockingWear ||
                parsed.ChassisNotes.Any(note => RaceReportMail.IsRequiredRepair(note, component.Component));
        }

        prep = parsed;
        return true;
    }

    private static bool TryReadWeights(string line, out TrackWeights weights, out List<string> additionalRatings)
    {
        weights = null;
        additionalRatings = new List<string>();
        if (!line.StartsWith(WeightsPrefix, StringComparison.Ordinal) || !line.EndsWith(".", StringComparison.Ordinal))
            return false;

        var values = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        string metrics = line.Substring(WeightsPrefix.Length, line.Length - WeightsPrefix.Length - 1);
        foreach (string metric in metrics.Split(new[] { ", " }, StringSplitOptions.None))
        {
            var match = Rating.Match(metric);
            if (!match.Success || !RaceReportMail.TryReadNumber(match.Groups["number"].Value, out float value) ||
                !(value >= 0f && value <= 1f) || !values.TryAdd(match.Groups["name"].Value, value))
                return false;

            switch (match.Groups["name"].Value.ToUpperInvariant())
            {
                case "HORSEPOWER":
                case "TRANSMISSION":
                case "SUSPENSION":
                case "BRAKES":
                case "AERO":
                case "TIRE WEAR":
                    break;
                default:
                    additionalRatings.Add(metric);
                    break;
            }
        }

        if (!values.TryGetValue("Horsepower", out float horsepower) ||
            !values.TryGetValue("Transmission", out float transmission) ||
            !values.TryGetValue("Suspension", out float suspension) ||
            !values.TryGetValue("Brakes", out float brakes) ||
            !values.TryGetValue("Aero", out float aero) ||
            !values.TryGetValue("Tire Wear", out float tireWear))
            return false;

        weights = new TrackWeights
        {
            horsepower = horsepower,
            transmission = transmission,
            suspension = suspension,
            brakes = brakes,
            aerodynamics = aero,
            surfaceRoughness = tireWear
        };
        return true;
    }

    private static bool TryReadWear(string line, out List<RaceReportMail.ComponentRow> components)
    {
        components = null;
        if (!line.StartsWith(WearPrefix, StringComparison.Ordinal) || !line.EndsWith(".", StringComparison.Ordinal))
            return false;

        var byComponent = new Dictionary<CarComponent, RaceReportMail.ComponentRow>();
        string metrics = line.Substring(WearPrefix.Length, line.Length - WearPrefix.Length - 1);
        foreach (string metric in metrics.Split(new[] { ", " }, StringSplitOptions.None))
        {
            var match = ComponentWear.Match(metric);
            if (!match.Success || !RaceReportMail.TryReadComponent(match.Groups["name"].Value, out var component) ||
                !RaceReportMail.TryReadNumber(match.Groups["number"].Value, out float percent) ||
                !(percent >= 0f && percent <= 100f) || !byComponent.TryAdd(component, new RaceReportMail.ComponentRow
                {
                    Component = component,
                    Wear = percent / 100f,
                    WearText = match.Groups["wear"].Value
                }))
                return false;
        }

        if (byComponent.Count != CarComponentDisplay.Order.Count)
            return false;

        components = CarComponentDisplay.Order.Select(component => byComponent[component]).ToList();
        return true;
    }
}
