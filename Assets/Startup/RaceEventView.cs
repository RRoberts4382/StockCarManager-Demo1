using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

internal sealed class RaceEventView
{
    private const int VictoryBackgroundBlurPixels = 10;
    private readonly ScrollView body = new ScrollView();
    private readonly VisualElement chartHost = new VisualElement();
    private readonly Dictionary<RaceSession, PracticeRunChart.Selection> chartSelections =
        new Dictionary<RaceSession, PracticeRunChart.Selection>();
    private readonly VisualElement footer = new VisualElement();
    private readonly Label title = new Label();
    private readonly Label sessionClock = new Label();
    private readonly Label weatherLabel = new Label();
    private readonly Toggle garageCollisions = new Toggle("Collisions");
    private readonly Toggle garageTraffic = new Toggle("Traffic interactions");
    internal Action<bool> ChangeCollisions;
    internal Action<bool> ChangeTraffic;
    internal void SetTrafficOptions(bool collisions, bool traffic)
    {
        garageCollisions.SetValueWithoutNotify(collisions);
        garageTraffic.SetValueWithoutNotify(traffic);
    }
    private readonly Toggle lockWeather = new Toggle("Lock weather");
    private SessionWeatherState displayedWeather;
    public void UpdateWeather(SessionWeatherState weather)
    {
        displayedWeather = weather;
        var session = runningSession ?? race?.sessions.FirstOrDefault(s => !s.Completed);
        bool practice = session?.kind == RaceSessionKind.Practice;
        lockWeather.style.display = practice ? DisplayStyle.Flex : DisplayStyle.None;
        lockWeather.SetValueWithoutNotify(weather != null ? weather.IsLocked : session?.lockPracticeWeather == true);
        weatherLabel.text = weather != null ? weather.Summary + (weather.IsLocked ? " (locked)" : "") :
            session?.weather == null ? string.Empty : session.weather.Summary;
        weatherLabel.style.display = string.IsNullOrEmpty(weatherLabel.text) ? DisplayStyle.None : DisplayStyle.Flex;
    }
    private readonly Label sessionLaps = new Label();
    private int shownElapsedSeconds = -1;
    private readonly Button[] tabs;
    private readonly Button next;
    private readonly Button options;
    private readonly Button simulate;
    private readonly Label status = new Label();
    private readonly Action repaint;
    private readonly Func<(Color32 Primary, Color32 Secondary)> teamColors;
    private Texture2D victoryArtwork;
    private RenderTexture blurredVictoryArtwork;
    private (Color32 Primary, Color32 Secondary) victoryPaint;
    private ScheduledRace race;
    private RaceSession shownResult;
    private TeamCar car;
    private TrackDefinition track;
    private RaceSession runningSession;
    private Texture carImage;
    private Action<CarComponent> repair;
    private Action changeTires;
    private Action repairBody;
    private Func<long> balance;
    private Func<string, Setup, string> saveSetup;
    private Func<Texture> setupCarImage;
    private VisualElement setupPanel;
    private Func<Setup> readSetupDraft;
    private Func<EventCarCondition> readCarCondition;
    private Func<bool> garageServiceAvailable;
    private VisualElement setupPage;
    private int page;
    private int simulationLapCount = 10;
    internal float StartingFuelGallons { get; private set; } = (float)SessionFuel.TankCapacity;
    internal Action<int> SimulateGarageLaps;
    internal Func<bool> CanSimulateGarageLaps;
    private float lastRunReviewRefresh = -1f;

    public VisualElement Screen { get; } = new VisualElement();

    public RaceEventView(Action startSession, Action simulateSession, Action leave, Action repaint,
        Func<(Color32 Primary, Color32 Secondary)> teamColors)
    {
        garageCollisions.tooltip = "Enable car collisions and collision damage.";
        garageTraffic.tooltip = "Enable traffic avoidance, slowing for cars ahead, and drafting decisions.";
        garageCollisions.RegisterValueChangedCallback(evt => ChangeCollisions?.Invoke(evt.newValue));
        garageTraffic.RegisterValueChangedCallback(evt => ChangeTraffic?.Invoke(evt.newValue));
        this.repaint = repaint;
        this.teamColors = teamColors;
        Screen.AddToClassList("screen");
        Screen.AddToClassList("game-neutral");
        Screen.AddToClassList("garage-screen");

        var header = new VisualElement();
        header.AddToClassList("header");
        title.AddToClassList("garage-heading");
        header.Add(title);
        sessionClock.AddToClassList("garage-detail");
        sessionClock.style.display = DisplayStyle.None;
        sessionClock.style.unityTextAlign = TextAnchor.MiddleRight;
        header.Add(sessionClock);
        Screen.Add(header);
        weatherLabel.AddToClassList("garage-detail");
        weatherLabel.style.flexShrink = 0;
        weatherLabel.style.display = DisplayStyle.None;
        var weatherRow = new VisualElement();
        weatherRow.style.flexDirection = FlexDirection.Row;
        weatherRow.style.flexWrap = Wrap.Wrap;
        weatherRow.style.flexShrink = 0;
        weatherRow.Add(weatherLabel);
        lockWeather.style.marginLeft = 12;
        lockWeather.style.display = DisplayStyle.None;
        lockWeather.tooltip = "Before practice, lock its starting weather. During practice, freeze current weather across runs. Unlock to resume changes.";
        lockWeather.RegisterValueChangedCallback(evt =>
        {
            var session = runningSession ?? race?.sessions.FirstOrDefault(s => !s.Completed);
            if (session?.kind == RaceSessionKind.Practice)
                session.lockPracticeWeather = evt.newValue;
            displayedWeather?.SetLocked(evt.newValue);
            UpdateWeather(displayedWeather);
        });
        weatherRow.Add(lockWeather);
        Screen.Add(weatherRow);
        sessionLaps.AddToClassList("garage-detail");
        sessionLaps.style.display = DisplayStyle.None;
        Screen.Add(sessionLaps);
        body.AddToClassList("garage-body");
        body.contentViewport.RegisterCallback<GeometryChangedEvent>(_ =>
        {
            FitSetupPage();
            FitPracticeReport();
        });
        Screen.Add(body);
        chartHost.style.flexGrow = 1;
        chartHost.style.minHeight = 0;
        chartHost.style.minWidth = 0;
        chartHost.style.display = DisplayStyle.None;
        Screen.Add(chartHost);
        status.AddToClassList("garage-detail");
        status.style.whiteSpace = WhiteSpace.Normal;
        status.style.display = DisplayStyle.None;
        Screen.Add(status);

        footer.AddToClassList("footer");
        footer.AddToClassList("garage-footer");
        string[] names = { "Event", "Garage", "Results" };
        tabs = new Button[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            int selected = i;
            tabs[i] = Button(names[i], () => Show(selected));
            footer.Add(tabs[i]);
        }
        var sessionMenu = new VisualElement();
        sessionMenu.AddToClassList("event-session-control");
        next = Button("Next Session", () =>
        {
            CloseSessionOptions();
            startSession();
        });
        next.AddToClassList("event-next-session");
        sessionMenu.Add(next);
        options = Button("▴", () =>
        {
            simulate.style.display = simulate.style.display == DisplayStyle.None ? DisplayStyle.Flex : DisplayStyle.None;
        });
        options.tooltip = "Show simulation option";
        options.AddToClassList("event-session-options");
        sessionMenu.Add(options);
        simulate = Button("Simulate Session", () =>
        {
            CloseSessionOptions();
            simulateSession();
        });
        simulate.AddToClassList("event-simulate-session");
        sessionMenu.Add(simulate);
        footer.Add(sessionMenu);
        footer.Add(Button("Return To Shop", leave));
        Screen.Add(footer);
    }

    public void Open(ScheduledRace eventRace, TrackDefinition definition, TeamCar enteredCar, Texture thumbnail,
        Func<long> teamBalance, Action<CarComponent> instantRepair, Action tires, Action bodyRepair,
        Func<string, Setup, string> saveChassisSetup, Func<Texture> overheadCarImage, Func<EventCarCondition> carCondition,
        Func<bool> canServiceInGarage)
    {
        race = eventRace;
        StartingFuelGallons = (float)SessionFuel.TankCapacity;
        car = enteredCar;
        track = definition;
        foreach (var session in race.sessions) SessionWeatherForecast.Ensure(session, track, race);
        carImage = thumbnail;
        balance = teamBalance;
        repair = instantRepair;
        changeTires = tires;
        repairBody = bodyRepair;
        saveSetup = saveChassisSetup;
        setupCarImage = overheadCarImage;
        readCarCondition = carCondition;
        garageServiceAvailable = canServiceInGarage;
        setupPanel = null;
        readSetupDraft = null;
        SetRunningSession(null);
        shownResult = race.sessions.LastOrDefault(session => session.Completed);
        title.text = $"Race Event  /  {race.trackName}";
        Show(0);
    }

    public void ShowResults(RaceSession session)
    {
        Screen.Q(className: "setup-preset-overlay")?.RemoveFromHierarchy();
        SetRunningSession(null);
        shownResult = session;
        Show(2);
    }

    public void SetRunningSession(RaceSession session)
    {
        runningSession = session;
        if (session == null) EqualConditionTesting = false;
        UpdateWeather(null);
        sessionLaps.style.display = DisplayStyle.None;
        shownElapsedSeconds = -1;
        lastRunReviewRefresh = -1f;
        if (session == null)
            sessionClock.style.display = DisplayStyle.None;
        else
            UpdateSessionClock(0f);
    }

    public void UpdateSessionLaps(int total, int departureLaps)
    {
        sessionLaps.text = $"Laps completed  ·  Session total {total}  ·  Since leaving garage {Math.Max(0, total - departureLaps)}";
        sessionLaps.style.display = runningSession != null ? DisplayStyle.Flex : DisplayStyle.None;
    }

    public void UpdateSessionClock(float elapsed)
    {
        if (runningSession == null)
            return;
        int seconds = Mathf.FloorToInt(elapsed);
        if (seconds == shownElapsedSeconds)
            return;
        shownElapsedSeconds = seconds;
        if (page == 1 && elapsed - lastRunReviewRefresh >= 1f)
        {
            lastRunReviewRefresh = elapsed;
            var reviews = body.Q<VisualElement>("garageRunReviews");
            if (reviews != null)
                UpdateRunReviews(reviews);
            body.Q<Button>("simulateGarageLaps")?.SetEnabled(CanSimulateGarageLaps?.Invoke() == true);
        }
        if (page == 1 && readCarCondition != null)
        {
            var condition = readCarCondition();
            for (int i = 0; i < 4; i++)
            {
                var wearGauge = body.Q<ProgressBar>("tireWear" + i);
                if (wearGauge != null) UpdateTireWear(wearGauge, condition.Wear(i));
                var gauge = body.Q<ProgressBar>("tireTemperature" + i);
                if (gauge != null)
                    UpdateTireTemperature(gauge, condition.Temperature(i));
                var pressureLabel = body.Q<Label>("tirePressure" + i);
                if (pressureLabel != null) UpdatePressureLabel(pressureLabel, new[] { "Left front", "Right front", "Left rear", "Right rear" }[i], condition, i);
            }
        }
        string duration = runningSession.minutes > 0 ? $"{runningSession.minutes} min" :
            $"{runningSession.laps} {(runningSession.laps == 1 ? "lap" : "laps")}";
        sessionClock.text = $"{runningSession.name}  ·  {duration}  ·  Elapsed {seconds / 60:00}:{seconds % 60:00}";
        sessionClock.style.display = DisplayStyle.Flex;
    }

    public bool EqualConditionTesting { get; set; }

    public Setup CurrentSetup => (race?.SetupLocked == true ? race.EventSetup(car) : null) ?? readSetupDraft?.Invoke() ?? car.SetupsFor(track.id).LastOrDefault()?.setup ?? car.setup;

    public void ShowGarage() => Show(1);

    public void ShowPracticeRunResults()
    {
        if (runningSession?.kind != RaceSessionKind.Practice ||
            runningSession.garageRuns?.Any(run => run.complete) != true)
            return;
        shownResult = runningSession;
        Show(2);
    }

    public void RefreshGarageParking()
    {
        if (page == 1)
            RefreshGarage(null);
    }

    public void ShowEvent(string message = null)
    {
        Show(0);
        SetStatus(message);
    }

    public void RefreshGarage(string message)
    {
        float scroll = body.scrollOffset.y;
        Show(1);
        body.schedule.Execute(() => body.scrollOffset = new Vector2(0, scroll));
        SetStatus(message);
    }

    private void SetStatus(string message)
    {
        status.text = message ?? string.Empty;
        status.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
    }

    private void CloseSessionOptions() => simulate.style.display = DisplayStyle.None;

    private void Show(int selected)
    {
        chartHost.Clear();
        chartHost.style.display = DisplayStyle.None;
        body.style.display = DisplayStyle.Flex;
        page = selected;
        footer.SetEnabled(page != 3);
        body.EnableInClassList("victory-results-background", false);
        body.style.backgroundImage = StyleKeyword.Null;
        body.Clear();
        var content = new VisualElement();
        content.AddToClassList("garage-page");
        content.EnableInClassList("event-setup-page", page == 3);
        setupPage = page == 3 ? content : null;
        body.Add(content);
        for (int i = 0; i < tabs.Length; i++)
            tabs[i].EnableInClassList("nav-button-selected", i == (page == 3 ? 1 : page));
        CloseSessionOptions();
        SetStatus(null);
        bool canStart = !race.completed && race.sessions.Any(session => !session.Completed) && runningSession == null;
        next.text = runningSession == null ? "Next Session" : "Return To Session";
        next.SetEnabled(canStart || runningSession != null);
        options.style.display = runningSession == null ? DisplayStyle.Flex : DisplayStyle.None;
        options.SetEnabled(canStart);
        simulate.SetEnabled(canStart);

        if (page != 3)
        {
            var heading = new Label(tabs[page].text);
            heading.AddToClassList("garage-page-title");
            content.Add(heading);
        }
        switch (page)
        {
            case 0:
                BuildEvent(content);
                break;
            case 1:
                BuildGarage(content);
                break;
            case 2:
                SetVictoryBackground(body, teamColors());
                BuildResults(content);
                break;
            case 3:
                setupPanel ??= EventSetupPanel.Create(car, track, saveSetup, setupCarImage(), Screen, ShowGarage, out readSetupDraft, () => StartingFuelGallons, () => race.SetupLocked, () => CurrentSetup);
                content.Add(setupPanel);
                FitSetupPage();
                break;
        }
        body.scrollOffset = Vector2.zero;
        repaint();
    }

    private void FitSetupPage()
    {
        float height = body.contentViewport.layout.height;
        if (setupPage != null && height > 1f)
            setupPage.style.minHeight = Mathf.Floor(height) - 1f;
    }

    private void BuildEvent(VisualElement content)
    {
        content.AddToClassList("event-information-page");
        var overview = new VisualElement();
        overview.AddToClassList("event-overview");
        content.Add(overview);
        var trackPanel = Section(overview, race.trackName);
        AddText($"{track.location?.city}, {track.location?.state}  ·  {track.lengthMiles:0.###} miles  ·  {track.surface}  ·  {track.minorStyle}", trackPanel);
        AddText($"{race.EffectiveRaceLaps} race laps  ·  Purse {RaceFinances.Dollars(race.purse)}", trackPanel);
        var outline = new VisualElement { pickingMode = PickingMode.Ignore };
        outline.AddToClassList("event-track-outline");
        trackPanel.Add(outline);
        new TrackOutlineView(outline).SetTrack(track);
        var ratings = new VisualElement();
        ratings.AddToClassList("track-ratings");
        trackPanel.Add(ratings);
        StartupMenu.DisplayTrackRatings(ratings, track.weights, all: true);

        var sessions = Section(overview, "Sessions");
        var nextSession = race.sessions.FirstOrDefault(session => !session.Completed);
        string date = Career.TryReadDate(race.date, out var eventDate) ? eventDate.ToString("MMM d, yyyy") : race.date;
        foreach (var session in race.sessions)
        {
            string length = session.minutes > 0 ? $"{session.minutes} minutes" : $"{session.laps} {(session.laps == 1 ? "lap" : "laps")}";
            string state = session.Completed ? "Complete" : session == runningSession ? "In progress" :
                session == nextSession ? "Next" : "Scheduled";
            var row = AddText($"{session.name}  ·  {length}  ·  {state}\n{date}\n{session.weather.Summary}", sessions);
            row.AddToClassList("schedule-race");
            row.EnableInClassList("schedule-race-completed", session.Completed);
        }
        AddComponents(Section(content, "Selected car"), service: false);
    }

    private void UpdatePressureLabel(Label label, string name, EventCarCondition condition, int corner)
    {
        float cold = condition.coldPressures?.Length == 4 && condition.coldPressures[corner] > 0f
            ? condition.coldPressures[corner] : VehicleSpeed003.ColdPressure(CurrentSetup, corner);
        float hot = VehicleSpeed003.HotPressure(cold, TireTemperatureModel.Fahrenheit(condition.Temperature(corner)));
        label.text = $"{name}\n{hot:0.0} PSI";
        label.tooltip = $"Cold: {cold:0.0} PSI / Current: {hot:0.0} PSI";
    }

    private void BuildGarage(VisualElement content)
    {
        content.AddToClassList("event-garage-page");
        bool canService = garageServiceAvailable();
        if (runningSession != null)
            AddText(canService ? "Player car parked in its pit stall." : "Player car waiting to reach its pit stall.", content);
        var overview = new VisualElement();
        overview.AddToClassList("event-overview");
        content.Add(overview);
        var preview = Section(overview, car.ChassisDisplayName);
        var image = new Image { image = carImage, scaleMode = ScaleMode.ScaleToFit };
        image.AddToClassList("car-paint-preview");
        preview.Add(image);
        var openSetup = EventSetupPanel.OpenSetupButton(() => Show(3));
        openSetup.AddToClassList("garage-open-setup");
        preview.Add(openSetup);
        AddText(car.manufacturer + "  ·  " + car.ChassisTypeName, preview);
        var condition = readCarCondition();
        var tires = Section(overview, "Tires");
        string[] names = { "Left front", "Right front", "Left rear", "Right rear" };
        for (int i = 0; i < names.Length; i++)
        {
            var row = new VisualElement();
            row.AddToClassList("event-tire-row");
            tires.Add(row);
            var tireName = AddText(names[i], row);
            tireName.name = "tirePressure" + i;
            UpdatePressureLabel(tireName, names[i], condition, i);
            AddTireGauge(row, "Wear", condition.Wear(i)).name = "tireWear" + i;
            var temperature = new ProgressBar { lowValue = 75f, highValue = 325f };
            temperature.name = "tireTemperature" + i;
            row.Add(temperature);
            UpdateTireTemperature(temperature, condition.Temperature(i));
        }
        var change = Button("Change Tires", changeTires);
        change.SetEnabled(canService);
        tires.Add(change);

        if (EqualConditionTesting)
        {
            StartingFuelGallons = (float)SessionFuel.TankCapacity;
            AddText("Equal-condition test: field matched at session start. Restart practice for a fresh comparison; tires and fuel evolve during the session.", content);
        }
        var fuelPanel = Section(content, "Starting fuel");
        fuelPanel.AddToClassList("garage-fuel-panel");
        var fuelAmount = new Label($"{StartingFuelGallons:0.0} / {SessionFuel.TankCapacity:0} gallons");
        fuelAmount.AddToClassList("garage-detail");
        var fuelSlider = new Slider("Fuel for next run", 0f, (float)SessionFuel.TankCapacity)
        {
            name = "garageStartingFuel",
            value = StartingFuelGallons,
            showInputField = true
        };
        fuelSlider.style.minHeight = 32f;
        fuelSlider.style.marginTop = 8f;
        fuelSlider.style.marginBottom = 8f;
        fuelSlider.SetEnabled(canService && !EqualConditionTesting);
        fuelSlider.RegisterValueChangedCallback(evt =>
        {
            if (float.IsNaN(evt.newValue) || float.IsInfinity(evt.newValue))
            {
                fuelSlider.SetValueWithoutNotify(StartingFuelGallons);
                return;
            }
            StartingFuelGallons = Mathf.Clamp(Mathf.Round(evt.newValue * 10f) / 10f,
                0f, (float)SessionFuel.TankCapacity);
            fuelSlider.SetValueWithoutNotify(StartingFuelGallons);
            fuelAmount.text = $"{StartingFuelGallons:0.0} / {SessionFuel.TankCapacity:0} gallons";
        });
        fuelPanel.Add(fuelAmount);
        fuelPanel.Add(fuelSlider);

        var simulationControls = new VisualElement { style = { flexDirection = FlexDirection.Row } };
        simulationControls.AddToClassList("garage-simulation-controls");
        var lapChoice = new DropdownField("Laps", new List<string> { "5", "10", "25", "50", "100", "200" },
            new List<int> { 5, 10, 25, 50, 100, 200 }.IndexOf(simulationLapCount));
        lapChoice.RegisterValueChangedCallback(evt => simulationLapCount = int.Parse(evt.newValue));
        simulationControls.Add(lapChoice);
        var runButton = Button("Simulate laps", () => SimulateGarageLaps?.Invoke(simulationLapCount));
        runButton.name = "simulateGarageLaps";
        runButton.SetEnabled(CanSimulateGarageLaps?.Invoke() == true);
        simulationControls.Add(runButton);
        content.Add(simulationControls);
        var trafficOptions = new VisualElement();
        trafficOptions.style.flexDirection = FlexDirection.Row;
        trafficOptions.style.flexWrap = Wrap.Wrap;
        trafficOptions.style.flexShrink = 0;
        trafficOptions.style.marginBottom = 8;
        garageTraffic.style.marginLeft = 18;
        trafficOptions.Add(garageCollisions);
        trafficOptions.Add(garageTraffic);
        content.Add(trafficOptions);
        var reviewSession = runningSession ?? race.sessions.LastOrDefault(item => item.garageRuns?.Count > 0);
        if (reviewSession?.kind != RaceSessionKind.Practice)
        {
            var reviews = Section(content, "On-track runs");
            reviews.name = "garageRunReviews";
            UpdateRunReviews(reviews);
        }

        var components = Section(content, "Components");
        AddText($"Team balance  ·  {RaceFinances.Dollars(balance())}", components);
        AddComponents(components, service: true);
        int cost = ComponentRepair.CostForWear(condition.damage / 100f);
        AddText($"Body damage {condition.damage:0.#}%", content);
        if (cost > 0)
        {
            var fixBody = Button($"Repair Body  ·  {RaceFinances.Dollars(cost)}", repairBody);
            fixBody.SetEnabled(canService && balance() >= cost);
            content.Add(fixBody);
            if (balance() < cost)
                AddText("Insufficient funds for body repair.", content);
        }
    }

    private void AddComponents(VisualElement panel, bool service)
    {
        if (!service)
        {
            AddText($"{car.ChassisDisplayName}  ·  {car.manufacturer}  ·  {car.ChassisTypeName}", panel);
            AddText($"Body damage {readCarCondition().damage:0.#}%", panel);
        }
        var components = new VisualElement();
        panel.Add(components);
        ShopComponentPanel.Show(components, car, null, null, null, null, balance(), null,
            service && garageServiceAvailable() ? repair : null, null,
            showRepairAvailability: service);
    }

    private static VisualElement Section(VisualElement parent, string title)
    {
        var panel = new VisualElement();
        panel.AddToClassList("event-detail-panel");
        var heading = new Label(title);
        heading.AddToClassList("schedule-heading");
        panel.Add(heading);
        parent.Add(panel);
        return panel;
    }

    private void UpdateRunReviews(VisualElement target, RaceSession reportSession = null)
    {
        target.Clear();
        var session = reportSession ?? runningSession ?? race.sessions.LastOrDefault(item => item.garageRuns?.Count > 0);
        if (session?.garageRuns == null || session.garageRuns.Count == 0)
        {
            AddText("Run reviews appear after the car goes on track.", target);
            return;
        }
        AddText(session.name + " · On-track runs", target);
        for (int index = session.garageRuns.Count - 1; index >= 0; index--)
        {
            var run = session.garageRuns[index];
            if (reportSession != null && !run.complete)
                continue;
            int count = run.lapTimes.Count;
            string times = count > 0
                ? $"Best {run.lapTimes.Min():0.000}s · Average {run.lapTimes.Average():0.000}s"
                : "No complete on-track laps";
            AddText($"Run {index + 1} · {count} timed laps · {times} · {(run.complete ? run.ending : "In progress")}", target);
            AddHandlingReport(target, run);
            string[] names = { "LF", "RF", "LR", "RR" };
            for (int tire = 0; tire < 4; tire++)
                AddText($"{names[tire]} · Start {run.startingTemperatures[tire]:0}°F → {(run.complete ? "End" : "Current")} {run.endingTemperatures[tire]:0}°F", target);
        }
    }

    private void FitPracticeReport()
    {
        var report = body.Q<VisualElement>("practiceRunReport");
        if (report != null)
            report.style.minHeight = Mathf.Max(260f, body.contentViewport.layout.height - 115f);
    }

    private void BuildPracticeReport(VisualElement content, RaceSession session)
    {
        var report = new VisualElement { name = "practiceRunReport" };
        report.AddToClassList("practice-run-report");
        content.Add(report);
        report.RegisterCallback<GeometryChangedEvent>(_ => FitPracticeReport());
        if (session.rivalSetupChanges?.Count > 0)
        {
            var changes = new Foldout { text = "Rival practice setup evaluations", value = false };
            report.Add(changes);
            foreach (var change in session.rivalSetupChanges)
            {
                string adjustmentText = !string.IsNullOrEmpty(change.adjustment)
                    ? $"{change.adjustment}: {change.beforeValue:0.##} -> {change.afterValue:0.##} {change.units}."
                    : change.beforeSetup == null ? $"Sway bar {change.beforeSwayBar:0} -> {change.afterSwayBar:0} lb/in." : "Setup unchanged.";
                var label = new Label($"Car {change.carNumber}, lap {change.sessionLap}: {change.reason} {adjustmentText} Entry {change.entryBalance:+0.0;-0.0;0.0}, middle {change.middleBalance:+0.0;-0.0;0.0}, exit {change.exitBalance:+0.0;-0.0;0.0}.");
                if (change.comparableLaps > 0)
                    label.text += $" Matched laps: {change.comparableLaps}; reference {change.referenceLapSeconds:0.000}s, trial {change.candidateLapSeconds:0.000}s, gain {change.lapGainSeconds:+0.000;-0.000;0.000}s.";
                label.style.whiteSpace = WhiteSpace.Normal;
                label.style.marginBottom = 8;
                changes.Add(label);
            }
        }
        string[] names = { "Left front", "Right front", "Left rear", "Right rear" };
        for (int index = session.garageRuns.Count - 1; index >= 0; index--)
        {
            var run = session.garageRuns[index];
            if (!run.complete)
                continue;
            var group = new VisualElement();
            group.AddToClassList("practice-run-group");
            report.Add(group);
            var heading = new Label($"Run {index + 1} · {run.ending}");
            heading.AddToClassList("practice-run-title");
            group.Add(heading);
            int count = run.lapTimes.Count;
            group.Add(new Label(count > 0
                ? $"{count} timed laps     Best {run.lapTimes.Min():0.000}s     Average {run.lapTimes.Average():0.000}s"
                : "No complete on-track laps"));

            var tires = new VisualElement();
            tires.AddToClassList("practice-run-tires");
            group.Add(tires);
            for (int tire = 0; tire < 4; tire++)
            {
                var column = new VisualElement();
                column.AddToClassList("practice-run-tire");
                tires.Add(column);
                column.Add(new Label(names[tire]));
                AddReportTemperature(column, run.startingTemperatures[tire], run.endingTemperatures[tire]);
            }
            AddHandlingReport(group, run);
            var laps = new Foldout { text = $"Lap times ({count})", value = false };
            laps.AddToClassList("practice-run-laps");
            group.Add(laps);
            for (int lap = 0; lap < count; lap++)
                laps.Add(new Label($"Lap {lap + 1}     {run.lapTimes[lap]:0.000} s"));
        }
        FitPracticeReport();
    }

    private static void AddHandlingReport(VisualElement parent, GarageRunReview run)
    {
        var heading = new Label("Estimated handling - entry / middle / exit");
        heading.style.unityFontStyleAndWeight = FontStyle.Bold;
        heading.style.marginTop = 10;
        parent.Add(heading);
        string[] phases = { "Entry", "Middle", "Exit" };
        for (int i = 0; i < 3; i++)
        {
            var phase = run.handling?.Length == 3 ? run.handling[i] : null;
            var label = new Label(phase?.Summary(phases[i]) ?? phases[i] + ": Not recorded");
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginBottom = 4;
            if (phase != null && phase.seconds >= 1d)
                label.tooltip = $"Positive = tight (front grip limits); negative = loose (rear grip limits). Load contribution: {phase.weightedLoads / phase.seconds:+0.0;-0.0;0.0}; tire condition/pressure contribution: {phase.weightedTires / phase.seconds:+0.0;-0.0;0.0}. Tight {phase.tightSeconds / phase.seconds:P0}, loose {phase.looseSeconds / phase.seconds:P0}.";
            parent.Add(label);
        }
        if (run.lapTimes.Count >= 10)
        {
            var trendTitle = new Label("Long-run handling trend");
            trendTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            trendTitle.style.marginTop = 8;
            parent.Add(trendTitle);
            for (int i = 0; i < 3; i++)
            {
                var trend = new Label(phases[i] + ": " + HandlingRunTrend.Describe(run, i));
                trend.style.whiteSpace = WhiteSpace.Normal;
                trend.style.marginBottom = 6;
                parent.Add(trend);
            }
        }
        var note = new Label("Grip-balance estimate from green-flag cornering; no steering or driving-line changes. Scores are relative, not degrees of slip.");
        note.style.whiteSpace = WhiteSpace.Normal;
        note.style.fontSize = 12;
        parent.Add(note);
    }

    private static void AddReportTemperature(VisualElement parent, float starting, float ending)
    {
        var gauge = new ProgressBar { lowValue = 75f, highValue = 325f };
        UpdateTireTemperature(gauge, Mathf.InverseLerp(TireTemperatureModel.AmbientFahrenheit,
            TireTemperatureModel.MaximumFahrenheit, ending));
        gauge.title = $"Start {starting:0}°F · End {ending:0}°F";
        gauge.tooltip = "Fill shows ending temperature. Black line shows starting temperature.";
        var track = gauge.Q<VisualElement>(className: "unity-progress-bar__background");
        if (track != null)
        {
            float fraction = Mathf.InverseLerp(TireTemperatureModel.AmbientFahrenheit,
                TireTemperatureModel.MaximumFahrenheit, starting);
            var marker = new VisualElement { pickingMode = PickingMode.Ignore };
            marker.AddToClassList("practice-temperature-start");
            track.Add(marker);
            marker.RegisterCallback<GeometryChangedEvent>(_ => PositionMarker());
            track.RegisterCallback<GeometryChangedEvent>(_ => PositionMarker());
            void PositionMarker() =>
                marker.style.left = Mathf.Clamp(track.contentRect.width * fraction - 1.5f,
                    0f, Mathf.Max(0f, track.contentRect.width - 3f));
        }
        parent.Add(gauge);
    }

    private static void UpdateTireTemperature(ProgressBar gauge, float stored)
    {
        float value = TireTemperatureModel.Fahrenheit(stored);
        string zone = value < 160f ? "Cold" : value < 185f ? "Warming" :
            value <= 220f ? "Optimal" : value < 225f ? "Warm" :
            value <= 260f ? "Overheating" : value < 270f ? "Very hot" : "Critical";
        gauge.value = value;
        gauge.title = $"{value:0}°F · {zone}";
        var fill = gauge.Q<VisualElement>(className: "unity-progress-bar__progress");
        if (fill != null)
            fill.style.backgroundColor = value < 185f ? new Color(0.3f, 0.65f, 1f) :
                value <= 220f ? new Color(0.3f, 0.8f, 0.4f) :
                value < 270f ? new Color(1f, 0.6f, 0.15f) : new Color(0.95f, 0.2f, 0.2f);
    }

    private static ProgressBar AddTireGauge(VisualElement row, string title, float value)
    {
        var gauge = new ProgressBar { lowValue = 0f, highValue = 1f };
        UpdateTireWear(gauge, value);
        row.Add(gauge);
        return gauge;
    }

    private static void UpdateTireWear(ProgressBar gauge, float wear)
    {
        gauge.value = Mathf.Clamp01(wear);
        gauge.title = $"Wear {gauge.value:P0}";
    }

    private void ShowRunChart()
    {
        var session = shownResult;
        if (session == null)
            return;
        body.style.display = DisplayStyle.None;
        chartHost.Clear();
        chartHost.style.display = DisplayStyle.Flex;
        if (!chartSelections.TryGetValue(session, out var selection))
        {
            selection = new PracticeRunChart.Selection();
            chartSelections.Add(session, selection);
        }
        chartHost.Add(new PracticeRunChart(session, track, selection, () =>
        {
            shownResult = session;
            Show(2);
        }));
    }

    private void BuildResults(VisualElement content)
    {
        var completed = race.sessions.Where(session => session.DisplayResults?.Count > 0 ||
            session.kind == RaceSessionKind.Practice && session.garageRuns?.Any(run => run.complete) == true).ToList();
        if (completed.Count == 0)
        {
            AddText("Results appear after a session or a practice run finishes.", content);
            return;
        }
        if (!completed.Contains(shownResult))
            shownResult = completed[completed.Count - 1];

        var picker = new DropdownField("Session", completed.Select(session => session.name).ToList(), completed.IndexOf(shownResult));
        picker.RegisterValueChangedCallback(_ =>
        {
            shownResult = completed[picker.index];
            Show(2);
        });
        content.Add(picker);
        if (shownResult.kind == RaceSessionKind.Practice &&
            shownResult.garageRuns?.Any(run => run.complete) == true)
        {
            var chartButton = Button("Run comparison graph", ShowRunChart);
            content.Add(chartButton);
            var comparison = new Foldout { text = "Compare two runs", value = shownResult.garageRuns.Count(run => run.complete) >= 2 };
            comparison.Add(PracticeRunComparison.Create(shownResult));
            content.Add(comparison);
            BuildPracticeReport(content, shownResult);
        }
        if (shownResult.DisplayResults?.Count > 0)
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
