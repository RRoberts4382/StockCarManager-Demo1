using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

internal sealed class RaceEventView
{
    private readonly ScrollView body = new ScrollView();
    private readonly Label title = new Label();
    private readonly Button exit;
    private readonly Button[] tabs;
    private readonly Action startSession;
    private readonly Action simulateSession;
    private readonly Action repaint;
    private ScheduledRace race;
    private RaceSession shownResult;
    private string carName;
    private int page;

    public VisualElement Screen { get; } = new VisualElement();

    public RaceEventView(Action startSession, Action simulateSession, Action leave, Action repaint)
    {
        this.startSession = startSession;
        this.simulateSession = simulateSession;
        this.repaint = repaint;
        Screen.AddToClassList("screen");
        Screen.AddToClassList("game-neutral");
        Screen.AddToClassList("garage-screen");

        var header = new VisualElement();
        header.AddToClassList("header");
        title.AddToClassList("garage-heading");
        header.Add(title);
        Screen.Add(header);
        body.AddToClassList("garage-body");
        Screen.Add(body);

        var footer = new VisualElement();
        footer.AddToClassList("footer");
        footer.AddToClassList("garage-footer");
        string[] names = { "Garage", "Sessions", "Results", "Maintenance" };
        tabs = new Button[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            int selected = i;
            tabs[i] = Button(names[i], () => Show(selected));
            footer.Add(tabs[i]);
        }
        exit = Button("Exit", leave);
        footer.Add(exit);
        Screen.Add(footer);
    }

    public void Open(ScheduledRace eventRace, string car)
    {
        race = eventRace;
        carName = car;
        shownResult = race.sessions.LastOrDefault(session => session.Completed);
        title.text = $"Race Event  /  {race.trackName}";
        Show(0);
    }

    public void ShowResults(RaceSession session)
    {
        shownResult = session;
        Show(2);
    }

    public void ShowSchedule(string message = null)
    {
        Show(1);
        if (!string.IsNullOrEmpty(message))
            AddText(message);
    }

    private void Show(int selected)
    {
        page = selected;
        body.Clear();
        var content = new VisualElement();
        content.AddToClassList("garage-page");
        body.Add(content);
        for (int i = 0; i < tabs.Length; i++)
            tabs[i].EnableInClassList("nav-button-selected", i == page);
        exit.text = race.completed ? "Home" : "Exit";

        string[] headings = { "Garage", "Sessions", "Results", "Maintenance" };
        var heading = new Label(headings[page]);
        heading.AddToClassList("garage-page-title");
        content.Add(heading);
        switch (page)
        {
            case 0:
                AddText(race.trackName, content);
                AddText($"{carName}  /  {race.EffectiveRaceLaps} race laps", content);
                if (race.purse > 0)
                    AddText($"Purse  /  {RaceFinances.Dollars(race.purse)}", content);
                break;
            case 1:
                BuildSchedule(content);
                break;
            case 2:
                BuildResults(content);
                break;
            case 3:
                AddText(carName, content);
                AddText("No maintenance tasks.", content);
                break;
        }
        body.scrollOffset = Vector2.zero;
        repaint();
    }

    private void BuildSchedule(VisualElement content)
    {
        foreach (var session in race.sessions)
        {
            string length = session.minutes > 0 ? $"{session.minutes} minutes" : $"{session.laps} {(session.laps == 1 ? "lap" : "laps")}";
            var row = AddText($"{session.name}  /  {length}" + (session.Completed ? "  /  Completed" : string.Empty), content);
            row.AddToClassList("schedule-race");
            row.EnableInClassList("schedule-race-completed", session.Completed);
        }
        var start = Button("Start Next Session", startSession);
        start.AddToClassList("garage-track-button");
        bool canStart = !race.completed && race.sessions.Any(session => !session.Completed);
        start.SetEnabled(canStart);
        content.Add(start);

        var simulate = Button("Simulate Next Session", simulateSession);
        simulate.AddToClassList("garage-track-button");
        simulate.SetEnabled(canStart);
        content.Add(simulate);
    }

    private void BuildResults(VisualElement content)
    {
        var completed = race.sessions.Where(session => session.DisplayResults?.Count > 0).ToList();
        if (completed.Count == 0)
        {
            AddText("No completed sessions yet.", content);
            return;
        }
        if (!completed.Contains(shownResult))
            shownResult = completed[completed.Count - 1];

        var picker = new DropdownField("Session", completed.Select(session => session.name).ToList(), completed.IndexOf(shownResult));
        picker.RegisterValueChangedCallback(_ =>
        {
            shownResult = completed[picker.index];
            ShowResults(shownResult);
        });
        content.Add(picker);
        content.Add(CreateResultsGrid(shownResult));
    }

    internal static VisualElement CreateResultsGrid(RaceSession session)
    {
        var grid = new VisualElement();
        grid.AddToClassList("results-grid");
        bool isRace = session.kind == RaceSessionKind.Race;
        var headers = new List<string> { "Position", "Car number", "Driver name", "Team name", "Fastest lap", "Last lap", "Average lap" };
        if (isRace)
        {
            headers.Add("Winnings");
            headers.Add("Points");
        }
        grid.Add(ResultRow(true, headers));
        foreach (var result in session.DisplayResults)
        {
            var times = result.lapTimes;
            bool hasTime = times?.Count > 0;
            var cells = new List<string>
            {
                result.position.ToString(), result.carNumber > 0 ? result.carNumber.ToString() : "—",
                result.driverName ?? result.carName, result.teamName ?? "—",
                hasTime ? LapTime(result.FastestLap) : "—",
                hasTime ? LapTime(times[times.Count - 1]) : "—",
                hasTime ? LapTime(times.Average()) : "—"
            };
            if (isRace)
            {
                cells.Add(result.winnings > 0 ? RaceFinances.Dollars(result.winnings) : "—");
                cells.Add(result.pointsEarned.ToString());
            }
            grid.Add(ResultRow(false, cells));
        }
        return grid;
    }

    private static VisualElement ResultRow(bool header, IReadOnlyList<string> values)
    {
        var row = new VisualElement();
        row.AddToClassList("results-grid-row");
        row.EnableInClassList("results-grid-header", header);
        for (int i = 0; i < values.Count; i++)
        {
            var cell = new Label(values[i]);
            cell.AddToClassList("garage-detail");
            cell.AddToClassList("results-grid-cell");
            if (i == 2 || i == 3)
                cell.AddToClassList("results-grid-name");
            row.Add(cell);
        }
        return row;
    }

    internal static string LapTime(float seconds)
    {
        var time = TimeSpan.FromSeconds(seconds);
        return $"{(int)time.TotalMinutes}:{time.Seconds:00}.{time.Milliseconds:000}";
    }

    private Label AddText(string text, VisualElement parent = null)
    {
        var label = new Label(text);
        label.AddToClassList("garage-detail");
        (parent ?? body).Add(label);
        return label;
    }

    private static Button Button(string text, Action action)
    {
        var button = new Button(action) { text = text };
        button.AddToClassList("nav-button");
        return button;
    }
}
