using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

internal static class BroadcastCoverage
{
    [Serializable]
    private sealed class Outlet
    {
        public string id;
        public string name;
        public int logoIndex;
        public string desk;
        public string focus;
    }

    [Serializable]
    private sealed class Catalog
    {
        public Outlet[] outlets;
    }

    private const int LogoColumns = 4;
    private const int LogoRows = 2;
    private static Outlet[] outlets;
    private static Texture2D logoAtlas;

    private static Outlet[] Outlets
    {
        get
        {
            if (outlets == null)
            {
                var asset = Resources.Load<TextAsset>("Startup/BroadcastOutlets");
                outlets = asset != null ? JsonUtility.FromJson<Catalog>(asset.text)?.outlets : null;
                outlets ??= Array.Empty<Outlet>();
            }
            return outlets;
        }
    }

    public static void SendRecaps(Career career, ScheduledRace race)
    {
        string eventPrefix = $"broadcast/{race.date}/{race.trackId}/";
        if (career.inbox?.Any(message => message.eventId?.StartsWith(eventPrefix, StringComparison.Ordinal) == true) == true)
            return;

        var random = new System.Random();
        var selected = Outlets.OrderBy(_ => random.Next()).Take(random.Next(2, 4)).ToArray();
        if (selected.Length == 0)
            return;

        var sessions = race.sessions ?? new List<RaceSession>();
        var raceSession = sessions.Find(session => session.kind == RaceSessionKind.Race);
        var standings = raceSession?.results?.Where(result => result.position > 0)
            .OrderBy(result => result.position).ToArray() ?? Array.Empty<SessionCarResult>();
        var winner = standings.FirstOrDefault(result => result.position == 1);
        var fastest = standings.Where(result => result.FastestLap > 0f && !float.IsInfinity(result.FastestLap))
            .OrderBy(result => result.FastestLap).FirstOrDefault();
        var qualifying = sessions.Find(session => session.kind == RaceSessionKind.Qualifying);
        int polesitter = qualifying?.qualifyingOrder?.FirstOrDefault() ?? 0;
        var poleResult = standings.FirstOrDefault(result => result.carNumber == polesitter);
        var player = race.playerSkipped ? null : standings.FirstOrDefault(result => result.carNumber == race.carNumber);
        string teamSummary = race.playerSkipped ? $"{career.teamName} did not start the race."
            : player == null ? $"A finishing position for {career.teamName} was unavailable."
            : $"{career.teamName} finished P{player.position} of {standings.Length} in the No. {player.carNumber}, driven by {DriverName(player)}.";
        string podium = standings.Length == 0 ? "The official finishing order was unavailable."
            : "Top finishers: " + string.Join(", ", standings.Take(3).Select(result => $"P{result.position} {DriverName(result)}")) + ".";
        string headline = winner == null ? $"Race coverage from {race.trackName}" : $"{DriverName(winner)} wins at {race.trackName}";
        string timing = fastest == null ? null
            : $"The fastest race lap went to {DriverName(fastest)} in {RaceEventView.LapTime(fastest.FastestLap)}.";

        career.inbox ??= new List<TeamMailMessage>();
        foreach (var outlet in selected)
        {
            string focus = outlet.focus switch
            {
                "pace" when timing != null => timing,
                "grid" when polesitter > 0 => $"The No. {polesitter}" +
                    (poleResult == null ? " started on pole." : $" of {DriverName(poleResult)} started on pole and finished P{poleResult.position}."),
                "team" when player != null && qualifying?.playerReport != null =>
                    $"{career.teamName} qualified P{qualifying.playerReport.position} and brought the car home P{player.position}.",
                _ when winner != null =>
                    $"{DriverName(winner)} took the checkered flag in the No. {winner.DisplayCarNumber}" +
                    (string.IsNullOrWhiteSpace(winner.teamName) ? "." : $" for {winner.teamName}."),
                _ => $"Here is the final report from {race.trackName}."
            };
            career.inbox.Add(new TeamMailMessage
            {
                eventId = eventPrefix + outlet.id,
                sender = $"{outlet.name} · {outlet.desk}",
                mediaOutletId = outlet.id,
                subject = $"{outlet.desk} · {headline}",
                date = race.date,
                body = string.Join("\n\n", focus, podium, teamSummary)
            });
        }
    }

    private static string DriverName(SessionCarResult result) =>
        string.IsNullOrWhiteSpace(result.driverName) ? $"the No. {result.DisplayCarNumber} driver" : result.driverName;

    public static Image Logo(string outletId)
    {
        if (string.IsNullOrEmpty(outletId))
            return null;
        var outlet = Array.Find(Outlets, item => item.id == outletId);
        if (outlet == null || outlet.logoIndex < 0 || outlet.logoIndex >= LogoColumns * LogoRows)
            return null;

        logoAtlas ??= Resources.Load<Texture2D>("Startup/BroadcastLogos");
        if (logoAtlas == null)
            return null;

        int column = outlet.logoIndex % LogoColumns;
        int row = outlet.logoIndex / LogoColumns;
        var logo = new Image
        {
            image = logoAtlas,
            uv = new Rect(column / (float)LogoColumns, (LogoRows - 1 - row) / (float)LogoRows,
                1f / LogoColumns, 1f / LogoRows),
            scaleMode = ScaleMode.ScaleToFit,
            pickingMode = PickingMode.Ignore,
            tooltip = $"{outlet.name} logo"
        };
        logo.AddToClassList("mail-broadcast-logo");
        return logo;
    }
}
