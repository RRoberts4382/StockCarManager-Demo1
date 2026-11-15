using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

internal sealed class RaceEventView
{
    private const int VictoryBackgroundBlurPixels = 10;
    private readonly ScrollView body = new ScrollView();
    private readonly Label title = new Label();
    private readonly Button exit;
    private readonly Button[] tabs;
    private readonly Action startSession;
    private readonly Action simulateSession;
    private readonly Action repaint;
    private readonly Func<(Color32 Primary, Color32 Secondary)> teamColors;
    private Texture2D victoryArtwork;
    private RenderTexture blurredVictoryArtwork;
    private (Color32 Primary, Color32 Secondary) victoryPaint;
    private ScheduledRace race;
    private RaceSession shownResult;
    private string carName;
    private int page;

    public VisualElement Screen { get; } = new VisualElement();

    public RaceEventView(Action startSession, Action simulateSession, Action leave, Action repaint,
        Func<(Color32 Primary, Color32 Secondary)> teamColors)
    {
        this.startSession = startSession;
        this.simulateSession = simulateSession;
        this.repaint = repaint;
        this.teamColors = teamColors;
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
        body.EnableInClassList("victory-results-background", false);
        body.style.backgroundImage = StyleKeyword.Null;
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
                SetVictoryBackground(body, teamColors());
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

    internal void SetVictoryBackground(VisualElement target, (Color32 Primary, Color32 Secondary) paint)
    {
        if (victoryArtwork == null ||
            !victoryPaint.Primary.Equals(paint.Primary) ||
            !victoryPaint.Secondary.Equals(paint.Secondary))
        {
            Dispose();
            var source = Resources.Load<TextAsset>("Startup/VictoryLane");
            if (source == null)
                return;

            victoryArtwork = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            if (!victoryArtwork.LoadImage(source.bytes))
            {
                Dispose();
                return;
            }

            var pixels = victoryArtwork.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = RecolorArtworkPixel(pixels[i], paint.Primary, paint.Secondary);
            victoryArtwork.SetPixels32(pixels);
            victoryArtwork.Apply(true, true);
            victoryPaint = paint;

            var shader = Resources.Load<Shader>("Startup/ShopBackgroundBlur");
            if (shader != null && shader.isSupported)
            {
                blurredVictoryArtwork = new RenderTexture(victoryArtwork.width, victoryArtwork.height, 0)
                {
                    name = "Blurred Victory Lane background"
                };
                var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    TeamShopPreview.ApplyBackgroundBlur(victoryArtwork, blurredVictoryArtwork, material, VictoryBackgroundBlurPixels);
                }
                finally
                {
                    UnityEngine.Object.Destroy(material);
                }
            }
        }

        target.EnableInClassList("victory-results-background", true);
        target.style.backgroundImage = new StyleBackground(blurredVictoryArtwork != null
            ? Background.FromRenderTexture(blurredVictoryArtwork)
            : Background.FromTexture2D(victoryArtwork));
    }

    internal static Color32 RecolorArtworkPixel(Color32 source, Color primary, Color secondary)
    {
        // The source uses magenta for primary panels and lime for secondary panels.
        float magenta = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(8f, 32f, Mathf.Min(source.r - source.g, source.b - source.g)))
            * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-4f, 20f, source.r - source.b));
        float lime = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(12f, 48f, source.r - source.b))
            * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(5f, 26f, source.g - source.r));
        float mask = Mathf.Max(magenta, lime);
        if (mask <= 0f)
            return source;

        Color paint = magenta >= lime ? primary : secondary;
        float intensity = Mathf.Max(source.r, Mathf.Max(source.g, source.b)) / 255f;
        float highlight = Mathf.Min(source.r, Mathf.Min(source.g, source.b)) / 255f;
        Color shaded = Color.Lerp(paint * Mathf.Min(1f, intensity * 1.12f), Color.white, highlight * highlight);
        shaded.a = 1f;
        return Color.Lerp((Color)source, shaded, mask);
    }

    public void Dispose()
    {
        if (blurredVictoryArtwork != null)
        {
            blurredVictoryArtwork.Release();
            UnityEngine.Object.Destroy(blurredVictoryArtwork);
            blurredVictoryArtwork = null;
        }
        if (victoryArtwork != null)
            UnityEngine.Object.Destroy(victoryArtwork);
        victoryArtwork = null;
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
