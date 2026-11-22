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
    private readonly VisualElement resultsHost = new VisualElement();
    private bool showPracticeReport;
    private readonly Dictionary<RaceSession, PracticeRunChart.Selection> chartSelections =
        new Dictionary<RaceSession, PracticeRunChart.Selection>();
    private readonly VisualElement footer = new VisualElement();
    private readonly Label title = new Label();
    private readonly Label sessionClock = new Label();
    private readonly Label weatherLabel = new Label();
    public void UpdateWeather(SessionWeatherState weather)
    {
        var session = runningSession ?? race?.sessions.FirstOrDefault(s => !s.Completed);
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
    private readonly Button leaveButton;
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
    private Action changeTires;
    private Action repairBody;
    private Func<long> balance;
    private RacingSeries seriesRules;
    private Func<string, Setup, string> saveSetup;
    private Func<Texture> setupCarImage;
    private VisualElement setupPanel;
    private Func<Setup> readSetupDraft;
    private GarageSetupEditor garageSetup;
    private VisualElement garageServices;
    private Func<EventCarCondition> readCarCondition;
    private Func<bool> garageServiceAvailable;
    private readonly VisualElement eventHost = new VisualElement();
    private readonly VisualElement setupHost = new VisualElement();
    private int page;
    private int simulationLapCount = 10;
    internal float StartingFuelGallons { get; private set; } = (float)SessionFuel.TankCapacity;
    internal Action<int> SimulateGarageLaps;
    internal Func<bool> CanSimulateGarageLaps;
    private float lastRunReviewRefresh = -1f;
    private ScrollView garageRunReviews;
    private Action closeGarageRunReviews;

    public VisualElement Screen { get; } = new VisualElement();

    public RaceEventView(Action startSession, Action simulateSession, Action leave, Action repaint,
        Func<(Color32 Primary, Color32 Secondary)> teamColors)
    {
        this.repaint = repaint;
        this.teamColors = teamColors;
        Screen.AddToClassList("screen");
        Screen.AddToClassList("game-neutral");
        Screen.AddToClassList("garage-screen");
        Screen.AddToClassList("event-garage-screen");

        var header = new VisualElement();
        header.AddToClassList("header");
        header.AddToClassList("garage-header");
        title.AddToClassList("garage-heading");
        header.Add(title);
        sessionClock.AddToClassList("garage-detail");
        sessionClock.style.display = DisplayStyle.None;
        sessionClock.style.unityTextAlign = TextAnchor.MiddleRight;
        header.Add(sessionClock);
        Screen.Add(header);
        weatherLabel.AddToClassList("garage-detail");
        weatherLabel.AddToClassList("garage-toolbar");
        weatherLabel.style.flexShrink = 0;
        weatherLabel.style.display = DisplayStyle.None;
        Screen.Add(weatherLabel);
        sessionLaps.AddToClassList("garage-detail");
        sessionLaps.style.display = DisplayStyle.None;
        Screen.Add(sessionLaps);
        body.AddToClassList("garage-body");
        Screen.Add(body);
        eventHost.AddToClassList("garage-event-host");
        eventHost.style.display = DisplayStyle.None;
        Screen.Add(eventHost);
        setupHost.AddToClassList("garage-setup-host");
        setupHost.style.display = DisplayStyle.None;
        Screen.Add(setupHost);
        resultsHost.AddToClassList("garage-results-host");
        resultsHost.style.display = DisplayStyle.None;
        Screen.Add(resultsHost);
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
            tabs[i].tooltip = "Show the " + names[i] + " page.";
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
        next.tooltip = "Open or return to the next on-track session.";
        options.tooltip = "Show the session simulation option.";
        options.AddToClassList("event-session-options");
        sessionMenu.Add(options);
        simulate = Button("Simulate Session", () =>
        {
            CloseSessionOptions();
            simulateSession();
        });
        simulate.tooltip = "Simulate the next session without opening the track view.";
        simulate.AddToClassList("event-simulate-session");
        sessionMenu.Add(simulate);
        footer.Add(sessionMenu);
        leaveButton = Button("Return To Shop", leave);
        leaveButton.tooltip = "Leave this garage and return to the team or startup screen.";
        footer.Add(leaveButton);
        Screen.Add(footer);
        var controls = Resources.Load<StyleSheet>("Settings/SettingsControls");
        if (controls != null) Screen.styleSheets.Add(controls);
        SettingsFormStyle.AddHoverTooltips(Screen);
    }

    public void Open(ScheduledRace eventRace, TrackDefinition definition, TeamCar enteredCar, Texture thumbnail,
        Func<long> teamBalance, Action tires, Action bodyRepair,
        Func<string, Setup, string> saveChassisSetup, Func<Texture> overheadCarImage, Func<EventCarCondition> carCondition,
        Func<bool> canServiceInGarage, bool testPractice, RacingSeries series)
    {
        race = eventRace;
        StartingFuelGallons = (float)SessionFuel.TankCapacity;
        car = enteredCar;
        track = definition;
        foreach (var session in race.sessions) SessionWeatherForecast.Ensure(session, track, race);
        carImage = thumbnail;
        balance = teamBalance;
        seriesRules = series;
        changeTires = tires;
        repairBody = bodyRepair;
        saveSetup = saveChassisSetup;
        setupCarImage = overheadCarImage;
        readCarCondition = carCondition;
        garageServiceAvailable = canServiceInGarage;
        setupPanel = null;
        readSetupDraft = null;
        garageSetup = null;
        SetRunningSession(null);
        shownResult = race.sessions.LastOrDefault(session => session.Completed);
        string eventTitle = testPractice
            ? (race.sessions.Any(session => session.kind == RaceSessionKind.Race) ? "Test Race" : "Test Practice")
            : "Race Event";
        title.text = $"{eventTitle}  /  {race.trackName}";
        leaveButton.text = testPractice ? "Leave Session" : "Return To Shop";
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
            if (garageRunReviews != null)
            {
                var offset = garageRunReviews.scrollOffset;
                UpdateRunReviews(garageRunReviews.contentContainer);
                garageRunReviews.scrollOffset = offset;
            }
            garageServices?.Q<Button>("simulateGarageLaps")?.SetEnabled(CanSimulateGarageLaps?.Invoke() == true);
        }
        if (page == 1 && readCarCondition != null)
        {
            var condition = readCarCondition();
            for (int i = 0; i < 4; i++)
            {
                var wearGauge = garageServices?.Q<ProgressBar>("tireWear" + i);
                if (wearGauge != null) UpdateTireWear(wearGauge, condition.Wear(i));
                var gauge = garageServices?.Q<ProgressBar>("tireTemperature" + i);
                if (gauge != null)
                    UpdateTireTemperature(gauge, condition.Temperature(i));
                var pressureLabel = garageServices?.Q<Label>("tirePressure" + i);
                if (pressureLabel != null) UpdatePressureLabel(pressureLabel, new[] { "Left front", "Right front", "Left rear", "Right rear" }[i], condition, i);
            }
        }
        string duration = runningSession.minutes > 0 ? $"{runningSession.minutes} min" :
            $"{runningSession.laps} {(runningSession.laps == 1 ? "lap" : "laps")}";
        sessionClock.text = $"{runningSession.name}  ·  {duration}  ·  Elapsed {seconds / 60:00}:{seconds % 60:00}";
        sessionClock.style.display = DisplayStyle.Flex;
    }

    public bool EqualConditionTesting { get; set; }

    public Setup CurrentSetup => (race?.SetupLocked == true ? race.EventSetup(car) : null) ?? garageSetup?.ReadDraft() ?? readSetupDraft?.Invoke() ?? car.SetupsFor(track.id).LastOrDefault()?.setup ?? car.setup;

    public void ShowGarage() => Show(1);

    public void ShowPracticeRunResults()
    {
        if (page == 1 && LatestRun().Run != null)
            Show(1);
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
        Show(1);
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
        closeGarageRunReviews?.Invoke();
        chartHost.Clear();
        chartHost.style.display = DisplayStyle.None;
        page = selected;
        body.style.display = DisplayStyle.None;
        eventHost.style.display = page == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        eventHost.Clear();
        setupHost.style.display = page is 1 or 3 ? DisplayStyle.Flex : DisplayStyle.None;
        setupHost.Clear();
        resultsHost.style.display = page == 2 ? DisplayStyle.Flex : DisplayStyle.None;
        resultsHost.Clear();
        footer.SetEnabled(page != 3);
        body.Clear();
        var content = new VisualElement();
        content.AddToClassList("garage-page");
        content.EnableInClassList("event-setup-page", page == 3);
        if (page == 0)
            eventHost.Add(content);
        else if (page == 2)
            resultsHost.Add(content);
        else
            setupHost.Add(content);
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

        switch (page)
        {
            case 0:
                BuildEvent(content);
                break;
            case 1:
                BuildGarage(content);
                break;
            case 2:
                SetVictoryBackground(resultsHost, teamColors());
                BuildResults(content);
                break;
            case 3:
                setupPanel ??= EventSetupPanel.Create(car, track, saveSetup, setupCarImage(), Screen, ShowGarage, out readSetupDraft, () => StartingFuelGallons, () => race.SetupLocked, () => CurrentSetup);
                content.Add(setupPanel);
                break;
        }
        body.scrollOffset = Vector2.zero;
        repaint();
    }

    private void BuildEvent(VisualElement content)
    {
        content.AddToClassList("event-information-page");
        var overview = new VisualElement();
        overview.AddToClassList("event-overview");
        content.Add(overview);
        var trackPanel = Section(overview, race.trackName, garagePanel: true);
        AddText($"{track.location?.city}, {track.location?.state}  ·  {track.lengthMiles:0.###} miles  ·  {track.surface}  ·  {track.minorStyle}", trackPanel);
        AddText($"{race.EffectiveRaceLaps} race laps  ·  Purse {RaceFinances.Dollars(race.purse)}", trackPanel);
        var outline = new VisualElement { pickingMode = PickingMode.Ignore };
        outline.AddToClassList("event-track-outline");
        trackPanel.Add(outline);
        new TrackOutlineView(outline).SetTrack(track);
        var ratings = new VisualElement();
        ratings.AddToClassList("track-ratings");
        trackPanel.Add(ratings);
        StartupMenu.DisplayTrackRatings(ratings, track.weights);

        var sessions = Section(overview, "Sessions", garagePanel: true);
        var nextSession = race.sessions.FirstOrDefault(session => !session.Completed);
        string date = Career.TryReadDate(race.date, out var eventDate) ? eventDate.ToString("MMM d, yyyy") : race.date;
        if (!string.IsNullOrEmpty(date))
            AddText(date, sessions).AddToClassList("event-session-date");
        foreach (var session in race.sessions)
        {
            string length = session.minutes > 0 ? $"{session.minutes} minutes" : $"{session.laps} {(session.laps == 1 ? "lap" : "laps")}";
            string state = session.Completed ? "Complete" : session == runningSession ? "In progress" :
                session == nextSession ? "Next" : "Scheduled";
            var row = AddText($"{session.name}  ·  {length}  ·  {state}\n{session.weather.Summary}", sessions);
            row.AddToClassList("schedule-race");
            row.EnableInClassList("schedule-race-completed", session.Completed);
        }
        var selectedCar = Section(content, $"Chassis {car.chassisNumber} {car.manufacturer} {car.ChassisTypeName}", garagePanel: true);
        selectedCar.AddToClassList("event-selected-car");
        AddComponents(selectedCar, compactMetrics: true);
    }

    private void UpdatePressureLabel(Label label, string name, EventCarCondition condition, int corner)
    {
        bool flat = condition.Wear(corner) >= 1f;
        float cold = flat ? 0f : condition.coldPressures?.Length == 4 && condition.coldPressures[corner] > 0f
            ? condition.coldPressures[corner] : VehicleSpeed003.ColdPressure(CurrentSetup, corner);
        float hot = flat ? 0f : VehicleSpeed003.HotPressure(cold, TireTemperatureModel.Fahrenheit(condition.Temperature(corner)));
        label.text = $"{name}  ·  {hot:0.0} PSI";
        label.tooltip = $"Cold: {cold:0.0} PSI / Current: {hot:0.0} PSI";
    }

    private (RaceSession Session, GarageRunReview Run, int Number) LatestRun(int skip = 0)
    {
        foreach (var session in race.sessions.AsEnumerable().Reverse())
        {
            if (session.garageRuns == null) continue;
            for (int i = session.garageRuns.Count - 1; i >= 0; i--)
                if (session.garageRuns[i]?.complete == true && skip-- == 0)
                    return (session, session.garageRuns[i], i + 1);
        }
        return (runningSession ?? race.sessions.LastOrDefault(s => s.Completed), null, 0);
    }

    private void BuildGarage(VisualElement content)
    {
        content.AddToClassList("garage-redesign");
        if (EqualConditionTesting) StartingFuelGallons = (float)SessionFuel.TankCapacity;
        var layout = GarageSetupEditor.Group(content, "garage-redesign-layout");
        var report = GarageSetupEditor.Group(layout, "garage-redesign-report");
        var last = LatestRun();
        GarageDebrief.Build(report, last.Session, last.Run, LatestRun(skip: 1).Run, last.Number, seriesRules?.id);
        garageSetup ??= new GarageSetupEditor(car, track, Screen, setupCarImage?.Invoke(), saveSetup,
            () => LatestRun().Run, () => StartingFuelGallons,
            gallons => StartingFuelGallons = EqualConditionTesting ? (float)SessionFuel.TankCapacity : gallons,
            () => race.SetupLocked, () => garageServiceAvailable() && !EqualConditionTesting,
            source => OpenGarageRunReviews(source));
        garageSetup.Refresh();
        layout.Add(garageSetup.Root);

    }

    private void BuildGarageServices(VisualElement content)
    {
        content.AddToClassList("event-garage-page");
        bool canService = garageServiceAvailable();
        if (runningSession != null)
            AddText(canService ? "Player car parked in its pit stall." : "Player car waiting to reach its pit stall.", content).AddToClassList("garage-status");
        var overview = new VisualElement();
        overview.AddToClassList("event-overview");
        content.Add(overview);
        var preview = Section(overview, car.ChassisDisplayName, garagePanel: true);
        preview.AddToClassList("garage-chassis-panel");
        var image = new Image { image = carImage, scaleMode = ScaleMode.ScaleToFit };
        image.AddToClassList("car-paint-preview");
        preview.Add(image);
        var chassisActions = new VisualElement();
        chassisActions.AddToClassList("garage-inline-actions");
        preview.Add(chassisActions);
        AddText(car.manufacturer + "  \u00B7  " + car.ChassisTypeName, chassisActions);
        var condition = readCarCondition();
        var tires = Section(overview, "Tires", garagePanel: true);
        tires.AddToClassList("garage-tires-panel");
        var tireGrid = new VisualElement();
        tireGrid.AddToClassList("garage-tire-grid");
        tires.Add(tireGrid);
        string[] names = { "Left front", "Right front", "Left rear", "Right rear" };
        for (int i = 0; i < names.Length; i++)
        {
            var row = new VisualElement();
            row.AddToClassList("event-tire-row");
            tireGrid.Add(row);
            var tireName = AddText(names[i], row);
            tireName.name = "tirePressure" + i;
            UpdatePressureLabel(tireName, names[i], condition, i);
            var meters = new VisualElement();
            meters.AddToClassList("garage-tire-meters");
            row.Add(meters);
            AddTireGauge(meters, "Wear", condition.Wear(i)).name = "tireWear" + i;
            var temperature = new ProgressBar { lowValue = 75f, highValue = 325f };
            temperature.name = "tireTemperature" + i;
            meters.Add(temperature);
            UpdateTireTemperature(temperature, condition.Temperature(i));
        }
        var change = Button("Change Tires", changeTires);
        change.tooltip = "Fit fresh tires while the car is in its stall.";
        change.SetEnabled(canService);
        tires.Add(change);

        var fuelPanel = Section(overview, "Practice tools", garagePanel: true);
        var fuelControls = GarageSetupEditor.Group(fuelPanel, "garage-fuel-controls");
        var simulationControls = new VisualElement { style = { flexDirection = FlexDirection.Row } };
        simulationControls.AddToClassList("garage-simulation-controls");
        var lapChoice = new DropdownField("Laps", new List<string> { "5", "10", "25", "50", "100", "200" },
            new List<int> { 5, 10, 25, 50, 100, 200 }.IndexOf(simulationLapCount));
        lapChoice.tooltip = "Choose the number of laps to simulate.";
        lapChoice.RegisterValueChangedCallback(evt => simulationLapCount = int.Parse(evt.newValue));
        simulationControls.Add(lapChoice);
        var runButton = Button("Simulate laps", () => SimulateGarageLaps?.Invoke(simulationLapCount));
        runButton.name = "simulateGarageLaps";
        runButton.tooltip = "Simulate the selected number of laps.";
        runButton.SetEnabled(CanSimulateGarageLaps?.Invoke() == true);
        simulationControls.Add(runButton);
        fuelControls.Add(simulationControls);

        var components = Section(content, "Components", garagePanel: true);
        var conditionActions = new VisualElement();
        conditionActions.AddToClassList("garage-inline-actions");
        components.Add(conditionActions);
        AddText($"Team balance  ·  {RaceFinances.Dollars(balance())}", conditionActions);
        AddText($"Body damage {condition.damage:0.#}%", conditionActions);
        int cost = ComponentRepair.CostForWear(condition.damage / 100f, seriesRules.maximumComponentRepairCost);
        if (cost > 0)
        {
            var fixBody = Button($"Repair Body  ·  {RaceFinances.Dollars(cost)}", repairBody);
            fixBody.tooltip = "Repair the body damage while the car is in its stall.";
            fixBody.SetEnabled(canService && balance() >= cost);
            conditionActions.Add(fixBody);
            if (balance() < cost)
                AddText("Insufficient funds for body repair.", conditionActions);
        }
        AddComponents(components);
    }

    private void OpenGarageRunReviews(Button source, bool services = false)
    {
        if (Screen.parent == null || garageRunReviews != null) return;
        var overlay = new VisualElement();
        overlay.AddToClassList("settings-confirmation-overlay");
        overlay.AddToClassList("garage-styled");
        foreach (string path in new[] { "Startup/Startup", "Settings/SettingsControls", "Startup/GarageStyles" })
        {
            var sheet = Resources.Load<StyleSheet>(path);
            if (sheet != null) overlay.styleSheets.Add(sheet);
        }
        var dialog = new VisualElement();
        dialog.AddToClassList("settings-confirmation-dialog");
        dialog.AddToClassList("garage-run-dialog");
        dialog.Add(SettingsFormStyle.Label(services ? "Garage services" : "On-track runs", "settings-group-heading"));
        var reviews = new ScrollView(ScrollViewMode.Vertical);
        reviews.verticalScrollerVisibility = ScrollerVisibility.Hidden;
        reviews.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        if (services)
        {
            garageServices = reviews.contentContainer;
            BuildGarageServices(garageServices);
        }
        else
        {
            garageRunReviews = reviews;
            UpdateRunReviews(reviews.contentContainer);
        }
        dialog.Add(reviews);
        var actions = new VisualElement();
        actions.AddToClassList("settings-footer");
        void Close()
        {
            closeGarageRunReviews = null;
            garageRunReviews = null;
            garageServices = null;
            Screen.UnregisterCallback<DetachFromPanelEvent>(OnGarageDetached);
            overlay.RemoveFromHierarchy();
            Screen.SetEnabled(true);
            source.Focus();
        }
        void OnGarageDetached(DetachFromPanelEvent evt) { if (evt.target == Screen) Close(); }
        closeGarageRunReviews = Close;
        Screen.RegisterCallback<DetachFromPanelEvent>(OnGarageDetached);
        var close = SettingsFormStyle.Button("Close", Close);
        close.tooltip = "Close the run history and return to the garage.";
        actions.Add(close);
        dialog.Add(actions);
        overlay.Add(dialog);
        overlay.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Escape) return;
            Close();
            evt.StopPropagation();
        }, TrickleDown.TrickleDown);
        Screen.parent.Add(overlay);
        Screen.SetEnabled(false);
        SettingsFormStyle.AddHoverTooltips(overlay);
        close.Focus();
    }

    private void AddComponents(VisualElement panel, bool compactMetrics = false)
    {
        var components = new VisualElement();
        components.AddToClassList("event-component-strip");
        panel.Add(components);
        ShopComponentPanel.Show(components, car, null, null, null, null, balance(), null,
            null, null, seriesRules, showService: false, compactMetrics: compactMetrics);
        if (components.childCount > 0)
            components[components.childCount - 1].AddToClassList("event-component-last");
    }

    private static VisualElement Section(VisualElement parent, string title, bool garagePanel = false)
    {
        var panel = new VisualElement();
        panel.AddToClassList("event-detail-panel");
        var heading = new Label(title);
        heading.AddToClassList("schedule-heading");
        if (garagePanel)
        {
            panel.AddToClassList("team-home-panel");
            heading.AddToClassList("team-home-heading");
            SettingsFormStyle.StylePanel(panel);
        }
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
            if (run.startingTemperatures?.Length == 4 && run.endingTemperatures?.Length == 4)
                for (int tire = 0; tire < 4; tire++)
                    AddText($"{names[tire]} \u00b7 Start {run.startingTemperatures[tire]:0}\u00b0F \u2192 {(run.complete ? "End" : "Current")} {run.endingTemperatures[tire]:0}\u00b0F", target);
            else
                AddText("Tire temperatures were not recorded for this run.", target);
        }
    }

    private void BuildPracticeReport(VisualElement content, RaceSession session)
    {
        var report = new VisualElement { name = "practiceRunReport" };
        report.AddToClassList("practice-run-report");
        content.Add(report);
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
        resultsHost.style.display = DisplayStyle.None;
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
        content.AddToClassList("garage-results-page");
        var panel = Section(content, "Results", garagePanel: true);
        var completed = race.sessions.Where(session => session.DisplayResults?.Count > 0 ||
            session.kind == RaceSessionKind.Practice && session.garageRuns?.Any(run => run.complete) == true).ToList();
        if (completed.Count == 0)
        {
            AddText("Results appear after a session or a practice run finishes.", panel);
            return;
        }
        if (!completed.Contains(shownResult))
        {
            shownResult = completed[completed.Count - 1];
            showPracticeReport = false;
        }

        var toolbar = new VisualElement();
        toolbar.AddToClassList("garage-results-toolbar");
        panel.Add(toolbar);
        var picker = new DropdownField("Session", completed.Select(session => session.name).ToList(), completed.IndexOf(shownResult));
        picker.tooltip = "Choose which session results to display.";
        SettingsFormStyle.StyleField(picker);
        picker.RegisterValueChangedCallback(_ =>
        {
            shownResult = completed[picker.index];
            showPracticeReport = false;
            Show(2);
        });
        toolbar.Add(picker);

        var session = shownResult;
        bool hasTiming = session.DisplayResults?.Count > 0;
        bool hasRuns = session.kind == RaceSessionKind.Practice && session.garageRuns?.Any(run => run.complete) == true;
        if (hasRuns)
        {
            var chart = Button("Run comparison graph", ShowRunChart);
            chart.tooltip = "Compare lap times, tire temperatures, and handling across runs.";
            toolbar.Add(chart);
        }

        bool showReport = hasRuns && (!hasTiming || showPracticeReport);
        if (hasRuns && hasTiming)
        {
            var toggle = Button(showReport ? "Timing results" : "Practice report", () =>
            {
                showPracticeReport = !showReport;
                Show(2);
            });
            toggle.tooltip = showReport ? "Show the session timing table." : "Show run comparisons and detailed practice reports.";
            toolbar.Add(toggle);
        }

        if (showReport)
        {
            var report = new ScrollView(ScrollViewMode.Vertical)
            {
                verticalScrollerVisibility = ScrollerVisibility.Hidden,
                horizontalScrollerVisibility = ScrollerVisibility.Hidden
            };
            report.AddToClassList("garage-practice-report-scroll");
            var comparison = new Foldout { text = "Compare two runs", value = session.garageRuns.Count(run => run.complete) >= 2 };
            comparison.Add(PracticeRunComparison.Create(session));
            report.Add(comparison);
            BuildPracticeReport(report, session);
            panel.Add(report);
            SettingsFormStyle.CheckSelectorListSpacing(toolbar, report);
        }
        else if (hasTiming)
        {
            var grid = CreateResultsGrid(session, scrollRows: true, resultTrack: track);
            panel.Add(grid);
            SettingsFormStyle.CheckSelectorListSpacing(toolbar, grid);
        }
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

    internal static VisualElement CreateResultsGrid(RaceSession session, Career career = null, TeamCar playerCar = null, bool scrollRows = false, TrackDefinition resultTrack = null)
    {
        var grid = new VisualElement();
        grid.AddToClassList("results-grid");
        var thumbnails = new CarThumbnails(256);
        grid.RegisterCallback<DetachFromPanelEvent>(evt =>
        {
            if (evt.target == grid) thumbnails.Dispose();
        });
        var roster = career?.cars?.Count > 0
            ? TrackPreview.CreateTimingEntries(playerCar ?? career.cars[0], career) : Array.Empty<SessionCarResult>();
        bool isRace = session.kind == RaceSessionKind.Race;
        var headers = new List<string> { "Pos", "", "Car #", "Driver", "Team", "Laps" };
        if (!isRace)
        {
            headers.Add("Fastest lap");
            headers.Add("Best Lap MPH");
        }
        if (isRace)
        {
            headers.Add("Winnings");
            headers.Add("Points");
        }
        grid.Add(ResultRow(true, headers, lapColumns: !isRace));
        VisualElement rows = grid;
        if (scrollRows)
        {
            grid.AddToClassList("results-grid-scrollable");
            var body = new ScrollView(ScrollViewMode.Vertical)
            {
                verticalScrollerVisibility = ScrollerVisibility.Hidden,
                horizontalScrollerVisibility = ScrollerVisibility.Hidden
            };
            body.AddToClassList("results-body-scroll");
            grid.Add(body);
            rows = body;
        }
        foreach (var result in session.DisplayResults)
        {
            var times = result.lapTimes;
            bool hasTime = times?.Count > 0;
            var cells = new List<string>
            {
                result.position.ToString(), "", result.carNumber > 0 ? result.carNumber.ToString() : "—",
                result.driverName ?? result.carName, result.teamName ?? "—", (times?.Count ?? 0).ToString()
            };
            if (!isRace)
            {
                cells.Add(hasTime ? LapTime(result.FastestLap) : "—");
                float bestLap = hasTime ? result.FastestLap : 0f;
                cells.Add(resultTrack != null && resultTrack.lengthMiles > 0 && bestLap > 0 &&
                    !float.IsNaN(bestLap) && !float.IsInfinity(bestLap)
                    ? (resultTrack.lengthMiles * 3600f / bestLap).ToString("F2") : "—");
            }
            if (isRace)
            {
                cells.Add(result.winnings > 0 ? RaceFinances.Dollars(result.winnings) : "—");
                cells.Add(result.pointsEarned.ToString());
            }
            var rosterEntry = roster.FirstOrDefault(entry =>
                entry.teamName == result.teamName &&
                ((!string.IsNullOrEmpty(result.ownerEntryId) && entry.ownerEntryId == result.ownerEntryId) ||
                 entry.driverName == result.driverName)) ??
                roster.FirstOrDefault(entry => entry.carNumber == result.carNumber && entry.teamName == result.teamName);
            var car = result.car ?? rosterEntry?.car ?? new TeamCar { number = result.carNumber };
            if (string.IsNullOrEmpty(car.modelResource) && career != null) car.modelResource = career.SeriesRules.carModel;
            var image = thumbnails.Get(car, Array.Empty<string>(), cacheScope: result.teamName ?? "result",
                transparentBackground: true, displayNumber: result.carNumber);
            var row = ResultRow(false, cells, image, lapColumns: !isRace);
            if (!string.IsNullOrEmpty(result.mechanicalFailure))
                row.tooltip = result.mechanicalFailure;
            bool player = career != null && roster.FirstOrDefault()?.carNumber == result.carNumber;
            row.EnableInClassList("results-player-row", player);
            rows.Add(row);
        }
        return grid;
    }

    private static VisualElement ResultRow(bool header, IReadOnlyList<string> values, Texture carImage = null, bool lapColumns = false)
    {
        var row = new VisualElement();
        row.AddToClassList("results-grid-row");
        row.EnableInClassList("results-grid-header", header);
        for (int i = 0; i < values.Count; i++)
        {
            if (i == 1 && !header)
            {
                var image = new Image { image = carImage, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                image.AddToClassList("results-car-image");
                row.Add(image);
                continue;
            }
            var cell = new Label(values[i]);
            cell.AddToClassList("garage-detail");
            cell.AddToClassList("results-grid-cell");
            if (i == 0) cell.AddToClassList("results-position");
            if (i == 1) cell.AddToClassList("results-car-image");
            if (i == 2) cell.AddToClassList("results-car-number");
            if (i == 3 || i == 4) cell.AddToClassList("results-grid-name");
            if (i == 5) cell.AddToClassList("results-laps");
            if (lapColumns && i == 6) cell.AddToClassList("results-fastest-lap");
            if (lapColumns && i == 7) cell.AddToClassList("results-best-lap-mph");
            if (!lapColumns && i == 6 && values.Count > 7) cell.AddToClassList("results-winnings");
            if (!lapColumns && i == 7) cell.AddToClassList("results-points");
            cell.tooltip = values[i];
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
        button.AddToClassList("settings-button");
        return button;
    }
}
