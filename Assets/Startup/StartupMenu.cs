using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class StartupMenu : MonoBehaviour
{
    private static readonly CarManufacturer[] NewChassisManufacturers =
        (CarManufacturer[])System.Enum.GetValues(typeof(CarManufacturer));
    private static readonly ChassisType[] NewChassisTypes =
        (ChassisType[])System.Enum.GetValues(typeof(ChassisType));
    private const string NewChassisPaint = "#727A7F";
    private PanelSettings panel;
    private GameSettings settings;
    private Coroutine displayChange;
    private VisualElement settingsReturnScreen;
    private Button settingsReturnButton;
    private Button continueCareerButton;
    private Button newCareerButton;
    private CareerSetup careerSetup;
    private Career activeCareer;
    private readonly TeamMailView teamMail = new TeamMailView();
    private ScheduledRace activeRace;
    private RaceSession activeSession;
    private bool developmentPractice;
    private VisualElement developmentTrackPicker;
    private const string DevelopmentTrackPreference = "SCM.DevelopmentPractice.TrackId";
    private List<TrackCatalog.Entry> developmentTracks;
    private RaceEventView raceEventView;
    private ScheduledRace selectedEvent;
    private Button addTeamCarButton;
    private CarComponent? maintenanceComponent;
    private bool showingWorkInProgress;
    private readonly CarThumbnails carThumbnails = new CarThumbnails();
    private readonly CarThumbnails paintPreviewThumbnails = new CarThumbnails(640);
    private readonly ShopCarPicker shopCarPicker = new ShopCarPicker();
    private readonly TeamShopPreview teamShopPreview = new TeamShopPreview();
    private readonly ScheduleHaulerBackground scheduleHauler = new ScheduleHaulerBackground();
    private readonly TeamShopExteriorBackground teamShopExterior = new TeamShopExteriorBackground();
    private const int GaragePhotoBlurPixels = 12;
    private RenderTexture financesBackground;
    private RenderTexture carsBackground;
    private const int ShopBackgroundBlurPixels = 8;
    private System.Action pendingDismissal;
    private readonly EmployeePortraits employeePortraits = new EmployeePortraits();
    private static readonly EmployeeRole[] EmployeeRoles =
        System.Enum.GetValues(typeof(EmployeeRole)).Cast<EmployeeRole>()
            .Where(role => role != EmployeeRole.Driver).ToArray();
    private int previewCarNumber;
    private Button startRaceButton;
    private Button simulateEventButton;
    private VisualElement scheduleTrackOutline;
    private readonly Vector2[] scheduleOutlinePoints = new Vector2[256];
    private Rect scheduleOutlineBounds;
    private Label raceProgressLabel;
    private Label playerCarStatus;
    private Label playerPitStatus;
    private Button requestPitStop;
    private Toggle refuelAtStop;
    private Slider pitFuelAmount;
    private Slider pitRepairAmount;
    private Label pitFuelAmountLabel;
    private Label pitRepairAmountLabel;
    private Label pitDamageLabel;
    private VisualElement pitFuelFill;
    private VisualElement pitDamageFill;
    private readonly Toggle[] pitTires = new Toggle[4];
    private readonly Label[] pitTireWear = new Label[4];
    private readonly VisualElement[] pitTireFill = new VisualElement[4];
    private DropdownField sessionSpeedSelector;
    private VisualElement practiceFlags;
    private VisualElement primaryPracticeFlag;
    private VisualElement blackPracticeFlag;
    private Label practiceStateLabel;
    private Label luckyDogLabel;
    private VisualElement checkeredFlag;
    private SessionTrackState? displayedTrackState;
    private bool displayedOneToGo;
    private ScrollView trackTimingRows;
    private int displayedTimingRevision = -1;
    private float nextStandingsRefresh;
    private readonly System.Collections.Generic.List<(VisualElement Row, Label Position, Label Car, Label Driver, Label Value, Label Speed)> timingCells =
        new System.Collections.Generic.List<(VisualElement, Label, Label, Label, Label, Label)>();
    private Button quitButton;
    private Button backButton;
    private Button garageQuitButton;
    private Button[] garageTabs;
    private System.Action[] garageTabActions;
    private VisualElement[] garagePages;
    private VisualElement titleScreen;
    private VisualElement garageScreen;
    private VisualElement trackScreen;
    private Label trackFpsLabel;
    private double fpsSampleStart;
    private int fpsSampleFrames;
    private VisualElement raceViewport;
    private Button returnToGarageButton;
    private TrackPreview trackPreview;
    private TrackCatalog catalog;
    private TracksEditor tracksEditor;
    private TrackUtility trackUtility;
    private Button trackUtilityButton;
    private Button tracksButton;
    private TrackCatalog.Entry previewTrack;
    private Button resetCameraButton;
    private Button cautionButton;
    private Button snapshotButton;
    private Label snapshotStatus;
    private int dragPointerId = -1;
    private int trackDragButton;
    private bool rotateTrackDrag;
    private Vector2 dragStart;
    private Vector2 previousPointerPosition;
    private bool isDragging;

    private void Awake()
    {
        var layout = Resources.Load<VisualTreeAsset>("Startup/Startup");
        var garageLayout = Resources.Load<VisualTreeAsset>("Startup/Garage");
        var trackLayout = Resources.Load<VisualTreeAsset>("Startup/Track");
        var theme = Resources.Load<ThemeStyleSheet>("Startup/StartupTheme");
        if (layout == null || garageLayout == null || trackLayout == null || theme == null)
        {
            Debug.LogError("A menu layout or theme is missing.", this);
            return;
        }

        panel = ScriptableObject.CreateInstance<PanelSettings>();
        panel.themeStyleSheet = theme;
        panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        panel.referenceResolution = new Vector2Int(1280, 720);
        panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
        panel.match = 0.5f;

        var view = new GameObject("Menu UI");
        view.SetActive(false);
        view.transform.SetParent(transform, false);
        var document = view.AddComponent<UIDocument>();
        document.panelSettings = panel;
        document.visualTreeAsset = layout;
        view.SetActive(true);

        var root = document.rootVisualElement;
        catalog = new TrackCatalog();
        tracksEditor = new TracksEditor(catalog, CloseTracks);
        tracksEditor.Screen.styleSheets.Add(Resources.Load<StyleSheet>("Startup/Startup"));
        tracksEditor.Screen.style.display = DisplayStyle.None;
        root.Add(tracksEditor.Screen);
        trackUtility = new TrackUtility(catalog, CloseTrackUtility);
        trackUtility.Screen.styleSheets.Add(Resources.Load<StyleSheet>("Startup/Startup"));
        trackUtility.Screen.style.display = DisplayStyle.None;
        root.Add(trackUtility.Screen);
        garageLayout.CloneTree(root);
        trackLayout.CloneTree(root);
        titleScreen = root.Q("titleScreen");
        developmentTrackPicker = root.Q("developmentTrackPicker");
        developmentTrackPicker.style.display = DisplayStyle.None;
        var testPracticeButton = titleScreen.Q<Button>("testPracticeButton");
        testPracticeButton.clicked += () => ShowDevelopmentTrackPicker();
        developmentTrackPicker.Q<Button>("backFromDevelopmentPractice").clicked += () =>
        {
            developmentTrackPicker.style.display = DisplayStyle.None;
            titleScreen.style.display = DisplayStyle.Flex;
            testPracticeButton.Focus();
        };
        developmentTrackPicker.Q<Button>("exitDevelopmentPractice").clicked += Quit;
        var practiceTrack = developmentTrackPicker.Q<DropdownField>("developmentTracks");
        var startPractice = developmentTrackPicker.Q<Button>("startDevelopmentPractice");
        practiceTrack.RegisterValueChangedCallback(_ =>
        {
            int index = practiceTrack.index;
            if (index < 0 || index >= developmentTracks.Count)
                return;

            PlayerPrefs.SetInt(DevelopmentTrackPreference, developmentTracks[index].Definition.id);
            PlayerPrefs.Save();
        });
        startPractice.clicked += () =>
        {
            int index = practiceTrack.index;
            if (index >= 0 && index < developmentTracks.Count)
                StartDevelopmentPractice(developmentTracks[index]);
        };
        garageScreen = root.Q("garageScreen");
        garageScreen.style.display = DisplayStyle.None;

        trackScreen = root.Q("trackScreen");
        trackFpsLabel = trackScreen.Q<Label>("trackFpsLabel");
        trackScreen.style.display = DisplayStyle.None;
        raceViewport = trackScreen.Q("raceViewport");
        trackTimingRows = trackScreen.Q<ScrollView>("trackTimingRows");
        raceProgressLabel = trackScreen.Q<Label>("raceProgressLabel");
        playerCarStatus = trackScreen.Q<Label>("playerCarStatus");
        playerPitStatus = trackScreen.Q<Label>("playerPitStatus");
        requestPitStop = trackScreen.Q<Button>("requestPitStop");
        refuelAtStop = trackScreen.Q<Toggle>("refuelAtStop");
        pitFuelAmount = trackScreen.Q<Slider>("pitFuelAmount");
        pitRepairAmount = trackScreen.Q<Slider>("pitRepairAmount");
        pitFuelAmountLabel = trackScreen.Q<Label>("pitFuelAmountLabel");
        pitRepairAmountLabel = trackScreen.Q<Label>("pitRepairAmountLabel");
        pitDamageLabel = trackScreen.Q<Label>("pitDamageLabel");
        pitFuelFill = trackScreen.Q("pitFuelFill");
        pitDamageFill = trackScreen.Q("pitDamageFill");
        for (int i = 0; i < pitTires.Length; i++)
        {
            pitTires[i] = trackScreen.Q<Toggle>($"pitTire{i}");
            pitTireWear[i] = trackScreen.Q<Label>($"pitTireWear{i}");
            pitTireFill[i] = trackScreen.Q($"pitTireFill{i}");
        }
        requestPitStop.clicked += QueuePlayerPitStop;
        practiceFlags = trackScreen.Q("practiceFlags");
        primaryPracticeFlag = practiceFlags.Q("primaryPracticeFlag");
        blackPracticeFlag = practiceFlags.Q("blackPracticeFlag");
        practiceStateLabel = practiceFlags.Q<Label>();
        luckyDogLabel = trackScreen.Q<Label>("luckyDogLabel");
        checkeredFlag = practiceFlags.Q("checkeredFlag");
        for (int row = 0; row < 2; row++)
        {
            var stripe = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1 } };
            for (int column = 0; column < 4; column++)
            {
                stripe.Add(new VisualElement
                {
                    style = { flexGrow = 1, backgroundColor = (row + column) % 2 == 0 ? Color.white : Color.black }
                });
            }
            checkeredFlag.Add(stripe);
        }
        sessionSpeedSelector = trackScreen.Q<DropdownField>("sessionSpeedSelector");
        sessionSpeedSelector.choices = new System.Collections.Generic.List<string> { "1x", "2x", "4x", "8x", "16x", "Pause" };
        sessionSpeedSelector.style.display = DisplayStyle.None;
        sessionSpeedSelector.RegisterValueChangedCallback(ChangeSessionSpeed);
        startRaceButton = garageScreen.Q<Button>("startRaceButton");
        startRaceButton.clicked += OpenSelectedEvent;
        simulateEventButton = garageScreen.Q<Button>("simulateEventButton");
        simulateEventButton.clicked += SimulateEvent;
        garageScreen.Q<Button>("eventCarPicker").clicked += ShowRaceCarPicker;
        garageScreen.Q<Button>("carOptionsClose").clicked += CloseCarOptions;
        scheduleTrackOutline = garageScreen.Q("scheduleTrackOutline");
        scheduleTrackOutline.generateVisualContent += DrawScheduleTrackOutline;
        addTeamCarButton = garageScreen.Q<Button>("addTeamCarButton");
        addTeamCarButton.clicked += OpenCarMarket;
        garageScreen.Q<Button>("recruitEmployeeButton").clicked += OpenRecruitment;
        garageScreen.Q<Button>("recruitDriverButton").clicked += OpenDriverRecruitment;
        garageScreen.Q<Button>("workInProgressToggle").clicked += () =>
        {
            showingWorkInProgress = !showingWorkInProgress;
            CancelFireEmployee();
            DisplayEmployeeWorkInProgress();
        };
        garageScreen.Q<Button>("recruitmentBackButton").clicked += CloseRecruitment;
        var recruitmentFilter = garageScreen.Q<DropdownField>("recruitmentPositionFilter");
        recruitmentFilter.choices = new[] { "All positions" }.Concat(EmployeeRoles.Select(Employee.NameOf)).ToList();
        recruitmentFilter.SetValueWithoutNotify(recruitmentFilter.choices[0]);
        recruitmentFilter.RegisterValueChangedCallback(_ => DisplayRecruitment(resetScroll: true));
        garageScreen.Q<Button>("cancelFireEmployeeButton").clicked += CancelFireEmployee;
        garageScreen.Q<Button>("confirmFireEmployeeButton").clicked += () => pendingDismissal?.Invoke();
        garageScreen.Q<Button>("carMarketBackButton").clicked += CloseCarMarket;
        garageScreen.Q<Button>("carMaintenanceBackButton").clicked += CloseCarMaintenance;
        var newManufacturer = garageScreen.Q<DropdownField>("newChassisManufacturer");
        newManufacturer.choices = NewChassisManufacturers.Select(value => value.ToString()).ToList();
        newManufacturer.index = System.Array.IndexOf(NewChassisManufacturers, CarManufacturer.Chevrolet);
        var newType = garageScreen.Q<DropdownField>("newChassisType");
        newType.choices = NewChassisTypes.Select(TeamCar.DisplayChassisType).ToList();
        newType.index = System.Array.IndexOf(NewChassisTypes, ChassisType.IntermediateTrack);
        garageScreen.Q<Button>("buyNewChassisButton").clicked += BuyNewChassis;
        raceViewport.RegisterCallback<WheelEvent>(ZoomTrack);
        raceViewport.RegisterCallback<PointerDownEvent>(BeginTrackPointer);
        raceViewport.RegisterCallback<PointerMoveEvent>(MoveTrackPointer);
        raceViewport.RegisterCallback<PointerUpEvent>(EndTrackPointer);
        raceViewport.RegisterCallback<PointerCaptureOutEvent>(LoseTrackPointer);
        resetCameraButton = trackScreen.Q<Button>("resetCameraButton");
        resetCameraButton.clicked += ResetTrackCamera;
        cautionButton = trackScreen.Q<Button>("cautionButton");
        cautionButton.clicked += RequestTrackCaution;
        snapshotButton = trackScreen.Q<Button>("snapshotButton");
        snapshotStatus = trackScreen.Q<Label>("snapshotStatus");
        snapshotButton.clicked += SaveTrackSnapshot;
        returnToGarageButton = trackScreen.Q<Button>("returnToGarageButton");
        returnToGarageButton.clicked += ReturnToGarage;

        tracksButton = titleScreen.Q<Button>("tracksButton");
        tracksButton.clicked += OpenTracks;
        trackUtilityButton = titleScreen.Q<Button>("trackUtilityButton");
        trackUtilityButton.clicked += OpenTrackUtility;
        continueCareerButton = titleScreen.Q<Button>("continueCareerButton");
        newCareerButton = titleScreen.Q<Button>("newCareerButton");
        careerSetup = new CareerSetup(catalog, ShowTeam, CloseCareerSetup);
        careerSetup.View.styleSheets.Add(Resources.Load<StyleSheet>("Startup/Startup"));
        careerSetup.View.style.display = DisplayStyle.None;
        root.Add(careerSetup.View);
        RefreshCareerButtons();
        continueCareerButton.clicked += ContinueCareer;
        newCareerButton.clicked += OpenCareerSetup;
        quitButton = titleScreen.Q<Button>("quitButton");
        backButton = garageScreen.Q<Button>("backButton");
        garageQuitButton = garageScreen.Q<Button>("garageQuitButton");
        string[] garagePageNames = { "home", "mail", "cars", "shop", "employees", "schedule", "results", "finances", "sponsors" };
        garageTabs = garagePageNames.Select(name => garageScreen.Q<Button>(name + "Tab")).ToArray();
        garagePages = garagePageNames.Select(name => garageScreen.Q(name + "Page")).ToArray();
        var shopComponents = garageScreen.Q("shopCarComponents");
        garageScreen.Q("shopCarPicker").RegisterCallback<GeometryChangedEvent>(evt =>
        {
            if (evt.newRect.height > 0f)
                shopComponents.style.marginTop = evt.newRect.height + 12f;
        });
        garageTabActions = new System.Action[garageTabs.Length];
        for (int i = 0; i < garageTabs.Length; i++)
        {
            int page = i;
            garageTabActions[i] = () => DisplayGaragePage(page);
            garageTabs[i].clicked += garageTabActions[i];
        }
        DisplayGaragePage(0);
        backButton.clicked += ShowTitle;
        quitButton.clicked += Quit;
        garageQuitButton.clicked += Quit;
        garageScreen.Q<Button>("nextDayButton").clicked += AdvanceCareerDay;
        garageScreen.Q<Button>("cancelSkipRaceButton").clicked += CloseSkipRacePrompt;
        garageScreen.Q<Button>("confirmSkipRaceButton").clicked += ConfirmSkipRace;

        settings = new GameSettings(root, ApplyDisplaySettings, CloseSettings, SaveTeamColors);
        settings.ScreenView.styleSheets.Add(Resources.Load<StyleSheet>("Startup/Startup"));
        settings.ScreenView.style.display = DisplayStyle.None;
        root.Add(settings.ScreenView);
        raceEventView = new RaceEventView(() => StartNextSession(false), () => StartNextSession(true), LeaveRaceEvent,
            settings.RestorePalette, () => activeCareer?.TeamColors ?? (default, default));
        raceEventView.Screen.styleSheets.Add(Resources.Load<StyleSheet>("Startup/Startup"));
        raceEventView.Screen.style.display = DisplayStyle.None;
        root.Add(raceEventView.Screen);
        var titleSettings = titleScreen.Q<Button>("titleSettingsButton");
        var garageSettings = garageScreen.Q<Button>("garageSettingsButton");
        titleSettings.clicked += () => OpenSettings(titleScreen, titleSettings);
        garageSettings.clicked += () => OpenSettings(garageScreen, garageSettings);
        settings.RestorePalette();
        ApplyDisplaySettings();
    }

    private void ShowDevelopmentTrackPicker(string message = null)
    {
        catalog.Refresh();
        var dropdown = developmentTrackPicker.Q<DropdownField>("developmentTracks");
        var start = developmentTrackPicker.Q<Button>("startDevelopmentPractice");
        developmentTracks = catalog.Entries.Where(entry =>
            (entry.Definition.id >= TrackPreview.BristolTrackId || entry.Definition.id == TrackPreview.CotaTrackId) &&
            entry.Definition.pitRoad is { enabled: true, entrance: { Length: >= 2 }, exit: { Length: >= 2 } }).ToList();
        int savedId = PlayerPrefs.GetInt(DevelopmentTrackPreference, -1);
        int selected = developmentTracks.FindIndex(entry => entry.Definition.id == savedId);
        bool available = developmentTracks.Count > 0;
        var choices = developmentTracks.Select(entry => entry.Definition.name).ToList();
        foreach (var group in developmentTracks.Select((entry, index) => (entry, index)).GroupBy(item => item.entry.Definition.name))
        {
            if (group.Count() > 1)
                foreach (var item in group)
                    choices[item.index] = item.entry.ToString();
        }
        dropdown.choices = choices;
        dropdown.SetValueWithoutNotify(available ? choices[Mathf.Max(0, selected)] : string.Empty);
        dropdown.SetEnabled(available);
        start.SetEnabled(available);

        var status = developmentTrackPicker.Q<Label>("developmentPracticeStatus");
        status.text = message ?? (!available ? "No test tracks with complete pit roads were found." : string.Empty);
        status.style.display = string.IsNullOrEmpty(status.text) ? DisplayStyle.None : DisplayStyle.Flex;
        titleScreen.style.display = DisplayStyle.None;
        developmentTrackPicker.style.display = DisplayStyle.Flex;
        settings.RestorePalette();
        (available ? start : developmentTrackPicker.Q<Button>("backFromDevelopmentPractice")).Focus();
    }

    private void StartDevelopmentPractice(TrackCatalog.Entry track)
    {
        int developmentTrackId = track.Definition.id;
        PlayerPrefs.SetInt(DevelopmentTrackPreference, developmentTrackId);
        PlayerPrefs.Save();
        developmentTrackPicker.style.display = DisplayStyle.None;
        var career = new Career { playerName = "Player", teamName = "Player Team" };
        career.InitializeCars(new System.Random(developmentTrackId));
        var car = career.cars[0];
        var session = new RaceSession { kind = RaceSessionKind.Practice, name = "Practice", minutes = 999 };
        var race = new ScheduledRace { trackId = developmentTrackId, trackName = track.Definition.name, carNumber = car.number };
        developmentPractice = true;
        OpenTrack(track, true, car.number);
        if (trackPreview.Image == null || !trackPreview.HasSessionPitRoad)
        {
            ReturnToGarage();
            ShowDevelopmentTrackPicker("Could not open practice at " + track.Definition.name + ".");
            return;
        }

        titleScreen.style.display = DisplayStyle.None;
        returnToGarageButton.text = "Leave Session";
        activeSession = session;
        var simulation = new CarSimulationContext(track.Definition, race, session,
            TrackPreview.CreateTimingEntries(car, career));
        trackPreview.BeginSession(simulation, new TrackState001(), settings.SpeedModel, settings.BehaviorModel);
        RefreshTrackTiming();
        sessionSpeedSelector.SetValueWithoutNotify(sessionSpeedSelector.choices[0]);
        sessionSpeedSelector.style.display = DisplayStyle.Flex;
        UpdateSessionLabel();
    }

    private void ApplyDisplaySettings()
    {
        if (displayChange != null)
            StopCoroutine(displayChange);
        displayChange = StartCoroutine(settings.ApplyDisplay());
    }

    private void OpenSettings(VisualElement screen, Button source)
    {
        settingsReturnScreen = screen;
        settingsReturnButton = source;
        screen.style.display = DisplayStyle.None;
        settings.ScreenView.style.display = DisplayStyle.Flex;
        settings.Open(screen == garageScreen ? activeCareer : null);
    }

    private string SaveTeamColors(string primary, string secondary)
    {
        var career = activeCareer;
        var current = career.TeamColors;
        if (primary == "#" + ColorUtility.ToHtmlStringRGB(current.Primary) &&
            secondary == "#" + ColorUtility.ToHtmlStringRGB(current.Secondary))
            return null;

        string previousPrimary = career.primaryTeamColor;
        string previousSecondary = career.secondaryTeamColor;
        career.primaryTeamColor = primary;
        career.secondaryTeamColor = secondary;
        try
        {
            careerSetup.SaveProgress(career);
        }
        catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
        {
            career.primaryTeamColor = previousPrimary;
            career.secondaryTeamColor = previousSecondary;
            return "Could not save team colors. " + ex.Message;
        }

        var page = garagePages.FirstOrDefault(view => view.style.display == DisplayStyle.Flex);
        if (page != null)
            UpdateGarageBackground(page.name);
        DisplayEmployees(career);
        return null;
    }

    private void CloseSettings()
    {
        settings.RestorePalette();
        settings.ScreenView.style.display = DisplayStyle.None;
        settingsReturnScreen.style.display = DisplayStyle.Flex;
        settingsReturnButton.Focus();
    }

    private void OnDestroy()
    {
        raceEventView?.Dispose();
        employeePortraits.Dispose();
        carThumbnails.Dispose();
        paintPreviewThumbnails.Dispose();
        teamShopPreview.Dispose();
        scheduleHauler.Dispose();
        teamShopExterior.Dispose();
        if (financesBackground != null)
        {
            financesBackground.Release();
            Destroy(financesBackground);
        }
        if (carsBackground != null)
        {
            carsBackground.Release();
            Destroy(carsBackground);
        }
        trackUtility?.CloseReferenceImage();
        sessionSpeedSelector?.UnregisterValueChangedCallback(ChangeSessionSpeed);
        if (startRaceButton != null)
            startRaceButton.clicked -= OpenSelectedEvent;
        if (simulateEventButton != null)
            simulateEventButton.clicked -= SimulateEvent;
        if (addTeamCarButton != null)
            addTeamCarButton.clicked -= OpenCarMarket;
        if (continueCareerButton != null)
            continueCareerButton.clicked -= ContinueCareer;
        if (newCareerButton != null)
            newCareerButton.clicked -= OpenCareerSetup;
        if (backButton != null)
            backButton.clicked -= ShowTitle;
        if (quitButton != null)
            quitButton.clicked -= Quit;
        if (garageTabs != null)
        {
            for (int i = 0; i < garageTabs.Length; i++)
                garageTabs[i].clicked -= garageTabActions[i];
        }
        if (garageQuitButton != null)
            garageQuitButton.clicked -= Quit;
        if (returnToGarageButton != null)
            returnToGarageButton.clicked -= ReturnToGarage;
        if (tracksButton != null)
            tracksButton.clicked -= OpenTracks;
        if (trackUtilityButton != null)
            trackUtilityButton.clicked -= OpenTrackUtility;
        if (resetCameraButton != null)
            resetCameraButton.clicked -= ResetTrackCamera;
        if (cautionButton != null)
            cautionButton.clicked -= RequestTrackCaution;
        if (snapshotButton != null)
            snapshotButton.clicked -= SaveTrackSnapshot;
        if (raceViewport != null)
        {
            ReleaseTrackPointer();
            raceViewport.UnregisterCallback<WheelEvent>(ZoomTrack);
            raceViewport.UnregisterCallback<PointerDownEvent>(BeginTrackPointer);
            raceViewport.UnregisterCallback<PointerMoveEvent>(MoveTrackPointer);
            raceViewport.UnregisterCallback<PointerUpEvent>(EndTrackPointer);
            raceViewport.UnregisterCallback<PointerCaptureOutEvent>(LoseTrackPointer);
        }
        if (panel != null)
            Destroy(panel);
    }

    private void OpenTrackUtility()
    {
        trackUtility.Open();
        titleScreen.style.display = DisplayStyle.None;
        trackUtility.Screen.style.display = DisplayStyle.Flex;
    }

    private void CloseTrackUtility()
    {
        trackUtility.CloseReferenceImage();
        trackUtility.Screen.style.display = DisplayStyle.None;
        titleScreen.style.display = DisplayStyle.Flex;
        trackUtilityButton.Focus();
    }

    private void OpenTracks()
    {
        tracksEditor.Open();
        titleScreen.style.display = DisplayStyle.None;
        tracksEditor.Screen.style.display = DisplayStyle.Flex;
    }

    private void CloseTracks()
    {
        tracksEditor.Screen.style.display = DisplayStyle.None;
        titleScreen.style.display = DisplayStyle.Flex;
        tracksButton.Focus();
    }

    private void DisplayGaragePage(int selected, ScheduledRace resultsEvent = null)
    {
        if (selected < 0)
            return;
        for (int i = 0; i < garagePages.Length; i++)
        {
            bool active = i == selected;
            garagePages[i].style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
            garageTabs[i].EnableInClassList("nav-button-selected", active);
        }
        garageScreen.Q("carMarketPage").style.display = DisplayStyle.None;
        garageScreen.Q("carMaintenancePage").style.display = DisplayStyle.None;
        garageScreen.Q("recruitmentPage").style.display = DisplayStyle.None;
        garageScreen.Q("garageFooter").SetEnabled(true);
        var body = garageScreen.Q<ScrollView>("garageBody");
        string pageName = garagePages[selected].name;
        UpdateGarageBackground(pageName);
        if (pageName == "shopPage" && activeCareer != null)
            RefreshShopCarPicker();
        if (pageName == "employeesPage" && activeCareer != null)
            DisplayEmployeeWorkInProgress();
        if (activeCareer != null)
            garageScreen.Q<Label>("teamHeader").text = $"{activeCareer.teamName} - {garageTabs[selected].text}";
        if (pageName != "employeesPage")
            CancelFireEmployee();
        body.style.display = pageName == "schedulePage" || pageName == "employeesPage" || pageName == "mailPage"
            ? DisplayStyle.None : DisplayStyle.Flex;
        if (pageName == "mailPage" && activeCareer != null)
            teamMail.Show(garagePages[selected], activeCareer, settings.RestorePalette, DeleteSocialPosts, SocialDriverPortrait);
        if (pageName == "schedulePage")
            RefreshSchedule(selectNext: true);

        if (pageName == "resultsPage")
            BuildTeamResults(resultsEvent);
        if (pageName == "financesPage")
            DisplayFinances();
        if (pageName == "carsPage")
            RefreshTeamCars();
        if (pageName == "sponsorsPage")
            DisplaySponsors();
        body.scrollOffset = Vector2.zero;
        settings?.RestorePalette();
    }

    private void RefreshShopCarPicker()
    {
        var panel = garageScreen.Q("shopCarComponents");
        var status = garageScreen.Q<Label>("shopMaintenanceStatus");
        status.style.display = DisplayStyle.None;
        bool hasDate = Career.TryReadDate(activeCareer.currentDate, out var date);
        var nextRaceDate = NextScheduledRaceDate();
        shopCarPicker.Show(garageScreen.Q("shopCarPicker"), activeCareer.cars, carThumbnails, ActiveSponsorNames(),
            settings.RestorePalette, car => ShopComponentPanel.Show(panel, car, hasDate ? date : (System.DateTime?)null,
                nextRaceDate,
                repair => ShopRepairPortrait(activeCareer.employees?.Find(repair.IsAssignedTo), car.number),
                component => hasDate && CanUpgradeComponent(car, component, date),
                activeCareer.teamBalance,
                component => ConfirmComponentUpgrade(car, component),
                component => OpenCarMaintenance(car, component),
                repair => CancelComponentRepair(car, repair)));
    }

    private System.DateTime? NextScheduledRaceDate()
    {
        var nextRace = activeCareer.schedule?.races?.Find(race => !race.completed);
        return Career.TryReadDate(nextRace?.date, out var raceDate) ? raceDate : null;
    }

    private void DisplayEmployeeWorkInProgress()
    {
        var roster = garageScreen.Q<ScrollView>("employeeRoster");
        var work = garageScreen.Q<ScrollView>("employeeWorkInProgress");
        var toggle = garageScreen.Q<Button>("workInProgressToggle");
        roster.style.display = showingWorkInProgress ? DisplayStyle.None : DisplayStyle.Flex;
        work.style.display = showingWorkInProgress ? DisplayStyle.Flex : DisplayStyle.None;
        toggle.text = showingWorkInProgress ? "Show Employees" : "Work In Progress";
        toggle.EnableInClassList("nav-button-selected", showingWorkInProgress);
        if (!showingWorkInProgress)
        {
            settings.RestorePalette();
            return;
        }

        work.Clear();
        System.DateTime? date = Career.TryReadDate(activeCareer.currentDate, out var currentDate) ? currentDate : null;
        var raceDate = NextScheduledRaceDate();
        var sponsors = ActiveSponsorNames();
        foreach (var car in activeCareer.cars)
        {
            if (car.repairs == null)
                continue;
            foreach (var repair in car.repairs)
            {
                var row = new VisualElement();
                row.AddToClassList("employee-work-row");
                var employee = new VisualElement();
                employee.AddToClassList("employee-work-mechanic");
                var assignee = activeCareer.employees?.Find(repair.IsAssignedTo);
                var portrait = ShopRepairPortrait(assignee, car.number);
                if (portrait != null)
                    employee.Add(portrait);
                var name = new Label(string.IsNullOrWhiteSpace(repair.employeeName) ? "Assigned mechanic" : repair.employeeName);
                name.AddToClassList("employee-work-name");
                var employeeDetails = new VisualElement();
                employeeDetails.AddToClassList("employee-work-details");
                employeeDetails.Add(name);
                if (assignee != null)
                {
                    var role = new Label(assignee.RoleName);
                    role.AddToClassList("employee-work-role");
                    employeeDetails.Add(role);
                }
                employee.Add(employeeDetails);
                row.Add(employee);

                var thumbnail = new Image { image = carThumbnails.Get(car, sponsors), scaleMode = ScaleMode.ScaleToFit };
                thumbnail.AddToClassList("employee-work-car");
                row.Add(thumbnail);
                var chassis = new Label(car.chassisNumber ?? "—");
                chassis.AddToClassList("employee-work-chassis");
                row.Add(chassis);

                row.Add(ShopComponentPanel.ComponentIcon(repair.component));
                var progress = new VisualElement();
                progress.AddToClassList("employee-work-progress");
                bool late = ShopComponentPanel.AddRepairProgress(progress, repair, date, raceDate);
                row.Add(progress);
                row.EnableInClassList("employee-work-late", late);
                var openShop = new Button(() =>
                {
                    shopCarPicker.SelectCar(car);
                    DisplayGaragePage(System.Array.FindIndex(garagePages, page => page.name == "shopPage"));
                    garageScreen.Q("shopCarPicker").Q<Button>()?.Focus();
                }) { text = "→", tooltip = $"Open chassis {car.chassisNumber} in Shop" };
                openShop.AddToClassList("nav-button");
                openShop.AddToClassList("employee-work-open-shop");
                row.Add(openShop);
                work.Add(row);
            }
        }
        if (work.childCount == 0)
            work.Add(new Label("No vehicle repairs in progress."));
        settings.RestorePalette();
    }

    private Image ShopRepairPortrait(Employee employee, int liveryNumber)
    {
        var portrait = employee == null ? null : CreateEmployeePortrait(employee, liveryNumber, "employee-portrait");
        portrait?.AddToClassList("shop-repair-portrait");
        return portrait;
    }

    private string DeleteSocialPosts(IEnumerable<string> postIds)
    {
        var previous = activeCareer.deletedSocialPostIds;
        activeCareer.deletedSocialPostIds = TeamCommunications.DeletePosts(activeCareer, postIds);
        try
        {
            careerSetup.SaveProgress(activeCareer);
            return null;
        }
        catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
        {
            activeCareer.deletedSocialPostIds = previous;
            return "Could not save changes to the social feed. " + ex.Message;
        }
    }

    private void UpdateGarageBackground(string pageName)
    {
        bool showingShop = pageName == "shopPage";
        bool showingSchedule = pageName == "schedulePage";
        bool showingHome = pageName == "homePage";
        bool showingFinances = pageName == "financesPage";
        bool showingCars = pageName == "carsPage";
        garageScreen.EnableInClassList("garage-shop-background", showingShop);
        garageScreen.EnableInClassList("garage-schedule-background", showingSchedule);
        garageScreen.EnableInClassList("garage-home-background", showingHome);
        garageScreen.EnableInClassList("garage-finances-background", showingFinances);
        garageScreen.EnableInClassList("garage-cars-background", showingCars);
        garageScreen.EnableInClassList("victory-results-background", false);
        if (showingShop)
        {
            teamShopPreview.Render(activeCareer, ActiveSponsorNames());
            var image = teamShopPreview.BackgroundAtBlur(ShopBackgroundBlurPixels);
            garageScreen.style.backgroundImage = image != null
                ? new StyleBackground(Background.FromRenderTexture(image))
                : new StyleBackground(StyleKeyword.Null);
        }
        else
        {
            teamShopPreview.Dispose();
            RenderTexture image = null;
            if (showingSchedule)
                image = scheduleHauler.Render(activeCareer, ActiveSponsorNames()[0]);
            else if (showingHome)
                image = teamShopExterior.Render(activeCareer);
            else if (showingFinances)
                image = GetGaragePhotoBackground("FinancesBackground", ref financesBackground);
            else if (showingCars)
                image = GetGaragePhotoBackground("CarsBackground", ref carsBackground);
            garageScreen.style.backgroundImage = image != null
                ? new StyleBackground(Background.FromRenderTexture(image))
                : new StyleBackground(StyleKeyword.Null);
        }
        if (pageName == "resultsPage" && activeCareer != null)
            raceEventView.SetVictoryBackground(garageScreen, activeCareer.TeamColors);
    }

    private RenderTexture GetGaragePhotoBackground(string resourceName, ref RenderTexture cached)
    {
        if (cached != null)
            return cached;

        var source = Resources.Load<Texture2D>($"Startup/{resourceName}");
        var shader = Resources.Load<Shader>("Startup/ShopBackgroundBlur");
        if (source == null || shader == null || !shader.isSupported)
            return null;

        cached = new RenderTexture(source.width, source.height, 0) { name = $"Blurred {resourceName}" };
        var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            TeamShopPreview.ApplyBackgroundBlur(source, cached, material, GaragePhotoBlurPixels);
            return cached;
        }
        finally
        {
            Destroy(material);
        }
    }

    private void DisplaySponsors(string message = null)
    {
        var page = garageScreen.Q("sponsorsPage");
        page.Clear();
        var contracts = activeCareer.sponsors ?? new List<SponsorContract>();
        for (int slot = 0; slot < Sponsorships.Slots; slot++)
        {
            var contract = contracts.Find(item => item.slot == slot);
            string title = Sponsorships.SlotName(slot);
            if (contract == null)
                AddDetail(page, $"{title} — Open");
            else
                AddSponsorRow(contract.offer, $"{title}  •  {contract.racesRemaining} races remaining  •  " +
                    $"{RaceFinances.Dollars(contract.paid)} paid");
        }

        AddDetail(page, message ?? (contracts.Count == Sponsorships.Slots ? "All sponsorship slots are filled."
            : "New offers arrive after each race when a slot is open."));
        foreach (var offer in activeCareer.sponsorOffers ?? Enumerable.Empty<SponsorOffer>())
            AddSponsorRow(offer, "Offer", () => AcceptSponsor(offer));

        void AddSponsorRow(SponsorOffer offer, string status, System.Action accept = null)
        {
            var row = new VisualElement();
            row.AddToClassList("sponsor-row");
            var logo = new Label(Sponsorships.LogoText(offer.companyId));
            logo.AddToClassList("sponsor-logo");
            float hue = Mathf.Repeat(offer.companyId * 0.618034f, 1f);
            logo.style.backgroundColor = Color.HSVToRGB(hue, 0.72f, 0.27f);
            Color ink = Color.HSVToRGB(Mathf.Repeat(hue + 0.43f, 1f), 0.38f, 1f);
            logo.style.color = ink;
            logo.style.borderLeftColor = ink;
            logo.style.borderTopLeftRadius = offer.companyId % 5 * 6;
            logo.style.borderBottomRightRadius = offer.companyId % 5 * 6;
            row.Add(logo);

            var description = new VisualElement();
            description.style.flexGrow = 1;
            description.style.flexShrink = 1;
            row.Add(description);
            AddDetail(description, Sponsorships.CompanyName(offer.companyId) + "  •  " + status);
            AddDetail(description, $"{RaceFinances.Dollars(offer.totalValue)} maximum  •  " +
                $"{offer.duration} {(offer.duration == 1 ? "race" : "races")}  •  " +
                (offer.lumpSum ? "Lump sum on first eligible race" : "Paid per eligible race"));
            AddDetail(description, offer.maxFinishPosition > 0
                ? $"Finish in the top {offer.maxFinishPosition}" : "Any race finish");
            if (accept != null)
            {
                var button = new Button(accept) { text = "Accept Offer" };
                button.AddToClassList("nav-button");
                row.Add(button);
            }
            page.Add(row);
        }

        void AddDetail(VisualElement parent, string text)
        {
            var label = new Label(text);
            label.AddToClassList("garage-detail");
            label.style.whiteSpace = WhiteSpace.Normal;
            parent.Add(label);
        }
    }

    private void AcceptSponsor(SponsorOffer offer)
    {
        var offers = activeCareer.sponsorOffers;
        var contracts = activeCareer.sponsors ?? new List<SponsorContract>();
        if (offers == null || !offers.Contains(offer) || contracts.Count >= Sponsorships.Slots ||
            contracts.Any(contract => contract.offer.companyId == offer.companyId))
            return;

        int slot = Enumerable.Range(0, Sponsorships.Slots)
            .First(index => !contracts.Any(contract => contract.slot == index));
        var contract = new SponsorContract { offer = offer, slot = slot, racesRemaining = offer.duration };
        var previousContracts = activeCareer.sponsors;
        activeCareer.sponsors = contracts.Append(contract).ToList();
        activeCareer.sponsorOffers = activeCareer.sponsors.Count == Sponsorships.Slots
            ? new List<SponsorOffer>() : offers.Where(candidate => candidate != offer).ToList();
        try
        {
            careerSetup.SaveProgress(activeCareer);
        }
        catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
        {
            activeCareer.sponsors = previousContracts;
            activeCareer.sponsorOffers = offers;
            DisplaySponsors("Could not save sponsorship. " + ex.Message);
            settings?.RestorePalette();
            return;
        }
        DisplaySponsors($"{Sponsorships.CompanyName(offer.companyId)} signed as {Sponsorships.SlotName(slot)} sponsor.");
        settings?.RestorePalette();
    }

    private void DisplayFinances()
    {
        var season = activeCareer.schedule;
        var races = season.races ?? new List<ScheduledRace>();
        int finished = races.Count(race => race.completed);
        long annualPayroll = activeCareer.AnnualPayroll;
        long payroll = races.Count > 0 && finished < races.Count
            ? RaceFinances.PayrollInstallment(annualPayroll, races.Count, finished + 1) : 0;
        long winnings = races.Where(race => race.completed && !race.playerSkipped).Sum(race =>
            race.sessions?.Find(session => session.kind == RaceSessionKind.Race)?.results?
                .Find(result => race.carNumber > 0 ? result.carNumber == race.carNumber
                    : result.teamName == activeCareer.teamName && result.driverName == activeCareer.playerName)?.winnings ?? 0);

        ShowAmount(garageScreen.Q<Label>("financeBalance"), activeCareer.teamBalance);
        bool hasUpcomingRace = finished < races.Count;
        var chargedExpenses = races.Where(race => race.completed && !race.playerSkipped && race.expenses != null)
            .Select(race => race.expenses).ToList();
        long expenseTotal = chargedExpenses.Sum(expenses => (long)expenses.Total);
        ShowAmount(garageScreen.Q<Label>("financeCostPerRace"), -payroll -
            (hasUpcomingRace ? RaceExpenses.BaseTotal : 0));
        long netIncome = winnings + season.sponsorIncome + season.carSales - season.payrollPaid - season.carPurchases -
            season.componentUpgrades - season.componentRepairs - expenseTotal;
        var averageProfit = garageScreen.Q<Label>("financeAverageProfitPerRace");
        if (finished == 0)
        {
            ShowAmount(averageProfit, 0);
            averageProfit.text = "—";
        }
        else
            ShowAmount(averageProfit, (long)System.Math.Round((decimal)netIncome / finished,
                System.MidpointRounding.AwayFromZero));

        FinanceReport.Render(garageScreen.Q("financeBreakdown"), activeCareer, races, chargedExpenses, payroll, winnings);

        void ShowAmount(Label label, long amount)
        {
            label.text = amount < 0 ? "-" + RaceFinances.Dollars(-amount) : RaceFinances.Dollars(amount);
            label.EnableInClassList("finance-positive", amount > 0);
            label.EnableInClassList("finance-negative", amount < 0);
        }
    }

    private void BuildTeamResults(ScheduledRace resultsEvent)
    {
        var content = garageScreen.Q("teamResults");
        content.Clear();
        var seasons = (activeCareer.completedSeasons ?? Enumerable.Empty<SeasonSchedule>())
            .Append(activeCareer.schedule);
        var events = seasons.Where(season => season?.races != null)
            .SelectMany(season => season.races).Where(race => race.completed)
            .OrderBy(race => race.date, System.StringComparer.Ordinal).ToList();
        if (events.Count == 0)
        {
            AddResultsMessage(content, "No completed race events yet.");
            return;
        }

        int initialEvent = events.IndexOf(resultsEvent);
        if (initialEvent < 0)
            initialEvent = events.FindLastIndex(race => race.sessions?.Any(session => session.kind == RaceSessionKind.Race && session.Completed) == true);
        var eventPicker = new DropdownField("Race event", events.Select(race => $"{RaceDate(race)} / {race.trackName}").ToList(),
            Mathf.Max(0, initialEvent));
        eventPicker.AddToClassList("team-results-event-picker");
        var sessionContent = new VisualElement();
        content.Add(eventPicker);
        content.Add(sessionContent);
        eventPicker.RegisterValueChangedCallback(_ =>
        {
            if (eventPicker.index >= 0)
                ShowRaceResults(sessionContent, events[eventPicker.index]);
        });
        ShowRaceResults(sessionContent, events[eventPicker.index]);
    }

    private void ShowRaceResults(VisualElement content, ScheduledRace race)
    {
        content.Clear();
        var session = race.sessions?.Find(item => item.kind == RaceSessionKind.Race && item.Completed);
        if (session?.results?.Count > 0)
            content.Add(RaceEventView.CreateResultsGrid(session));
        else
            AddResultsMessage(content, "No race results for this event.");
        settings?.RestorePalette();
    }

    private static void AddResultsMessage(VisualElement content, string message)
    {
        var label = new Label(message);
        label.AddToClassList("garage-detail");
        content.Add(label);
    }

    private void RefreshCareerButtons()
    {
        continueCareerButton.style.display = careerSetup.Latest == null ? DisplayStyle.None : DisplayStyle.Flex;
    }

    private void ContinueCareer()
    {
        if (careerSetup.Latest != null)
            ShowTeam(careerSetup.Latest);
    }

    private void OpenCareerSetup()
    {
        titleScreen.style.display = DisplayStyle.None;
        careerSetup.View.style.display = DisplayStyle.Flex;
        careerSetup.Open();
    }

    private void CloseCareerSetup()
    {
        careerSetup.View.style.display = DisplayStyle.None;
        titleScreen.style.display = DisplayStyle.Flex;
        newCareerButton.Focus();
    }

    private void ShowTeam(Career career)
    {
        var random = new System.Random();
        bool careerChanged = career.InitializeCars(random);
        careerChanged |= career.InitializeEmployees(random);
        careerChanged |= career.AllCars.Any(car => string.IsNullOrEmpty(car.chassisNumber));
        careerSetup.View.style.display = DisplayStyle.None;
        RefreshCareerButtons();
        garageScreen.Q<Label>("careerTeamLabel").text = career.teamName;
        var hometown = string.Join(", ", new[] { career.hometownCity, career.hometownState }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        garageScreen.Q<Label>("careerPlayerLabel").text = string.IsNullOrEmpty(hometown)
            ? career.playerName : $"{career.playerName}  /  {hometown}";
        garageScreen.Q<Label>("careerSeriesLabel").text = careerSetup.SeriesName(career);
        CancelFireEmployee();
        SetEmployeeStatus(null);
        RefreshDriverLabel(career);
        activeCareer = career;
        DisplayEmployees(career);
        catalog.Refresh();
        selectedEvent = null;
        careerChanged |= InitializeFinances(career);
        if (career.schedule == null || career.schedule.season <= 0 ||
            ((career.schedule.races?.Count ?? 0) == 0 && catalog.Entries.Count > 0))
        {
            career.schedule = careerSetup.GenerateSeason(career.schedule?.season > 0
                ? career.schedule.season : System.DateTime.Today.Year);
            careerChanged = true;
        }
        if (career.schedule.races != null)
        {
            foreach (var race in career.schedule.races.Where(race => race.purse <= 0))
            {
                var track = catalog.Entries.Find(entry => entry.Definition.id == race.trackId);
                if (track == null)
                    continue;
                race.purse = RaceFinances.PurseForTrack(track.Definition.lengthMiles);
                careerChanged = true;
            }
        }

        if (!Career.TryReadDate(career.currentDate, out _))
        {
            var lastRace = career.schedule.races?.LastOrDefault(race => race.completed);
            if (Career.TryReadDate(lastRace?.date, out var lastRaceDate))
                career.SetDate(lastRaceDate);
            else
                career.SetSeasonStartDate();
            careerChanged = true;
        }

        if (string.IsNullOrEmpty(career.socialStartDate))
        {
            career.socialStartDate = career.currentDate;
            careerChanged = true;
        }
        careerChanged |= TeamCommunications.AddDriverWelcome(career);
        careerChanged |= CrewChiefMail.SendPreview(career, catalog, RaceCarBlockReason);
        if (careerChanged)
        {
            try
            {
                careerSetup.SaveProgress(career);
            }
            catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
            {
                Debug.LogWarning("Could not save the updated career. " + ex.Message);
            }
        }
        RefreshTeamCars();
        RefreshCareerDate();
        RefreshTeamBalance();
        RefreshSchedule();
        titleScreen.style.display = DisplayStyle.None;
        garageScreen.style.display = DisplayStyle.Flex;
        DisplayGaragePage(0);
        garageTabs[0].Focus();
    }

    private void DisplayEmployees(Career career)
    {
        var roster = garageScreen.Q<ScrollView>("employeeRoster");
        int liveryNumber = career.cars?.FirstOrDefault()?.number ?? 27;
        Vector2 scroll = roster.scrollOffset;
        roster.Clear();
        foreach (var driver in career.drivers)
        {
            var card = new VisualElement();
            card.AddToClassList("employee-card");
            var portrait = CreateStaffPortrait(driver.FullName, driver.age, driver.gender,
                driver.hometownCity, driver.hometownState, liveryNumber, "employee-portrait", driver.ethnicity);
            if (portrait != null)
                card.Add(portrait);
            AddStaffIdentity(card, driver.FullName, "Driver");
            var traits = AddStaffRatings(card);
            float aggression = driver.personalityTraits?.aggression ?? 0.5f;
            AddStaffRating(traits, "Aggression", aggression, $"{aggression:P0}", new Color32(255, 193, 99, 255));
            AddStaffDetails(card, driver.annualSalary, driver.age, driver.yearsExperience);
            AddFireButton(card, "Fire Driver", driver.FullName, career.drivers, driver);
            roster.Add(card);
        }
        foreach (var employee in career.employees)
        {
            var card = new VisualElement();
            card.AddToClassList("employee-card");
            AddEmployeeCardDetails(card, employee, liveryNumber);
            AddFireButton(card, "Fire Employee", $"{employee.FullName}, {employee.RoleName}", career.employees, employee);
            roster.Add(card);
        }
        roster.schedule.Execute(() => roster.scrollOffset = scroll);
        settings?.RestorePalette();
    }

    private void AddFireButton<T>(VisualElement card, string label, string name, List<T> roster, T person)
    {
        var actions = new VisualElement();
        actions.AddToClassList("staff-card-actions");
        var button = new Button(() =>
        {
            SetEmployeeStatus(null);
            pendingDismissal = () => DismissStaff(roster, person, name);
            garageScreen.Q<Label>("fireEmployeeQuestion").text = $"Fire {name}?";
            garageScreen.Q("fireEmployeePrompt").style.display = DisplayStyle.Flex;
            garageScreen.Q<Button>("cancelFireEmployeeButton").Focus();
        }) { text = label };
        button.AddToClassList("nav-button");
        button.AddToClassList("staff-card-fire-button");
        actions.Add(button);
        card.Add(actions);
    }

    private void AddEmployeeCardDetails(VisualElement card, Employee employee, int liveryNumber)
    {
        var portrait = CreateEmployeePortrait(employee, liveryNumber, "employee-portrait");
        if (portrait != null)
            card.Add(portrait);

        AddStaffIdentity(card, employee.FullName, employee.RoleName);
        var skills = AddStaffRatings(card);
        AddSkill(CarComponent.Brakes, "Brakes");
        AddSkill(CarComponent.Engine, "Engine");
        AddSkill(CarComponent.Suspension, "Suspension");
        AddSkill(CarComponent.Transmission, "Transmission");
        AddSkill(CarComponent.Aerodynamics, "Aero");
        AddStaffDetails(card, employee.annualSalary, employee.age, employee.yearsExperience);

        void AddSkill(CarComponent component, string title)
        {
            float value = employee.RepairSkill(component);
            AddStaffRating(skills, title, value, value.ToString("F2"), ShopComponentPanel.ComponentColor(component));
        }
    }

    private static void AddStaffIdentity(VisualElement card, string name, string role)
    {
        var nameLabel = AddStaffLine(card, name);
        nameLabel.AddToClassList("schedule-heading");
        nameLabel.AddToClassList("employee-card-identity");
        AddStaffLine(card, role).AddToClassList("employee-card-identity");
    }

    private static void AddStaffDetails(VisualElement card, long salary, int age, int yearsExperience)
    {
        AddStaffLine(card, $"{RaceFinances.Dollars(salary)} per year");
        AddStaffLine(card, $"Age {age}");
        AddStaffLine(card, $"Years Experience {yearsExperience}");
    }

    private static Label AddStaffLine(VisualElement card, string text)
    {
        var label = new Label(text) { pickingMode = PickingMode.Ignore };
        label.AddToClassList("garage-detail");
        label.AddToClassList("schedule-status");
        card.Add(label);
        return label;
    }

    private static VisualElement AddStaffRatings(VisualElement card)
    {
        var ratings = new VisualElement();
        ratings.AddToClassList("employee-card-skills");
        card.Add(ratings);
        return ratings;
    }

    private static void AddStaffRating(VisualElement ratings, string title, float value, string formattedValue, Color color)
    {
        var row = new VisualElement();
        row.AddToClassList("employee-skill-row");
        var name = new Label(title);
        name.AddToClassList("employee-skill-name");
        row.Add(name);

        var track = new VisualElement { tooltip = $"{title} {formattedValue}" };
        track.AddToClassList("employee-skill-track");
        var fill = new VisualElement { pickingMode = PickingMode.Ignore };
        fill.AddToClassList("employee-skill-fill");
        fill.style.width = Length.Percent(Mathf.Clamp01(value) * 100f);
        fill.style.backgroundColor = color;
        track.Add(fill);
        row.Add(track);

        var amount = new Label(formattedValue);
        amount.AddToClassList("employee-skill-value");
        row.Add(amount);
        ratings.Add(row);
    }

    private Image CreateEmployeePortrait(Employee employee, int liveryNumber, string styleClass) =>
        CreateStaffPortrait(employee.FullName, employee.age, employee.gender, employee.hometownCity,
            employee.hometownState, liveryNumber, styleClass);

    private Image CreateStaffPortrait(string fullName, int age, string gender, string city, string state,
        int liveryNumber, string styleClass, DriverEthnicity ethnicity = DriverEthnicity.Unspecified)
    {
        var livery = TrackPreview.TeamLivery(liveryNumber);
        var colors = styleClass == "employee-portrait" && activeCareer != null
            ? activeCareer.TeamColors : (Primary: livery.HoodAndTrunk, Secondary: livery.RoofAndSides);
        var texture = employeePortraits.Get(fullName, age, gender, city, state, colors.Secondary, colors.Primary, ethnicity);
        if (texture == null)
            return null;

        var portrait = new Image { image = texture, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
        portrait.AddToClassList(styleClass);
        return portrait;
    }

    private Texture2D SocialDriverPortrait(DriverContact contact)
    {
        if (contact == null)
            return null;

        int age = contact.driverAge;
        string gender = contact.driverGender;
        var ethnicity = contact.driverEthnicity;
        if (age <= 0 || string.IsNullOrEmpty(gender) || ethnicity == DriverEthnicity.Unspecified)
        {
            var driver = activeCareer.RivalDriver(contact.teamName, contact.driverName);
            if (driver == null && string.IsNullOrEmpty(contact.teamName))
                driver = activeCareer.rivalEmployees?.SelectMany(team => team.drivers ?? Enumerable.Empty<Driver>())
                    .FirstOrDefault(candidate => candidate.FullName == contact.driverName);
            if (driver == null)
                return null;
            age = driver.age;
            gender = driver.gender;
            ethnicity = driver.ethnicity;
        }

        var paint = TrackPreview.TeamLivery(contact.carNumber);
        return employeePortraits.Get(contact.driverName, age, gender, null, null,
            paint.RoofAndSides, paint.HoodAndTrunk, ethnicity);
    }

    private static Employee CreateRecruit(EmployeeRole role, System.Random random)
    {
        var candidate = Employee.Create(role, random);
        candidate.recruitLiveryNumber = random.Next(27, 27 + TrackPreview.CarCount);
        return candidate;
    }

    private bool recruitingDrivers;

    private void OpenDriverRecruitment()
    {
        if (HasDriver(activeCareer))
            return;

        recruitingDrivers = true;
        activeCareer.driverCandidates ??= new List<Driver>();
        var candidates = activeCareer.driverCandidates;
        int previousCount = candidates.Count;
        var random = new System.Random();
        while (candidates.Count < 10)
            candidates.Add(Driver.Create(random, null));

        string message = null;
        if (candidates.Count != previousCount)
        {
            try
            {
                careerSetup.SaveProgress(activeCareer);
            }
            catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
            {
                candidates.RemoveRange(previousCount, candidates.Count - previousCount);
                message = "Could not save driver candidates. " + ex.Message;
            }
        }
        ShowRecruitment(message);
    }

    private void OpenRecruitment()
    {
        recruitingDrivers = false;
        CancelFireEmployee();
        var career = activeCareer;
        career.recruitCandidates ??= new List<Employee>();
        int previousCount = career.recruitCandidates.Count;
        var random = new System.Random();
        const int candidatesPerRole = 10;
        foreach (var role in EmployeeRoles)
        {
            int available = career.recruitCandidates.Count(candidate => candidate.role == role);
            for (int i = available; i < candidatesPerRole; i++)
                career.recruitCandidates.Add(CreateRecruit(role, random));
        }

        string message = null;
        if (career.recruitCandidates.Count != previousCount)
        {
            try
            {
                careerSetup.SaveProgress(career);
            }
            catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
            {
                career.recruitCandidates.RemoveRange(previousCount, career.recruitCandidates.Count - previousCount);
                message = "Could not save recruit candidates. " + ex.Message;
            }
        }

        ShowRecruitment(message);
    }

    private void ShowRecruitment(string message)
    {
        CancelFireEmployee();
        garageScreen.Q("garageBody").style.display = DisplayStyle.None;
        garageScreen.Q("employeesPage").style.display = DisplayStyle.None;
        garageScreen.Q("recruitmentPage").style.display = DisplayStyle.Flex;
        garageScreen.Q("recruitmentPositionFilter").style.display = recruitingDrivers ? DisplayStyle.None : DisplayStyle.Flex;
        garageScreen.Q("garageFooter").SetEnabled(false);
        DisplayRecruitment(message, resetScroll: true);
        garageScreen.Q<Button>("recruitmentBackButton").Focus();
    }

    private void CloseRecruitment()
    {
        int employeesPage = System.Array.FindIndex(garagePages, page => page.name == "employeesPage");
        DisplayGaragePage(employeesPage);
        garageScreen.Q<Button>(recruitingDrivers && !HasDriver(activeCareer)
            ? "recruitDriverButton" : "recruitEmployeeButton").Focus();
    }

    private void DisplayRecruitment(string message = null, bool resetScroll = false)
    {
        var roster = garageScreen.Q<ScrollView>("recruitmentRoster");
        Vector2 scroll = resetScroll ? Vector2.zero : roster.scrollOffset;
        roster.Clear();
        int filterIndex = garageScreen.Q<DropdownField>("recruitmentPositionFilter").index;
        var candidates = recruitingDrivers ? System.Array.Empty<Employee>() : activeCareer.recruitCandidates
            .Where(candidate => filterIndex <= 0 || candidate.role == EmployeeRoles[filterIndex - 1])
            .OrderBy(candidate => candidate.role).ThenBy(candidate => candidate.FullName).ToArray();

        var drivers = recruitingDrivers ? activeCareer.driverCandidates.OrderBy(candidate => candidate.FullName).ToArray() : System.Array.Empty<Driver>();
        var status = garageScreen.Q<Label>("recruitmentStatus");
        status.text = message ?? (candidates.Length + drivers.Length == 0 ? "No candidates available." : string.Empty);
        status.style.display = string.IsNullOrEmpty(status.text) ? DisplayStyle.None : DisplayStyle.Flex;

        var heading = MakeRow();
        heading.AddToClassList("results-grid-header");
        heading.Add(new VisualElement());
        heading[0].AddToClassList("recruitment-photo-cell");
        AddCell(heading, "Candidate", "recruitment-candidate");
        if (!recruitingDrivers)
            AddCell(heading, "Desired position");
        AddCell(heading, recruitingDrivers ? "Personality" : "Skills", "recruitment-skills");
        AddCell(heading, "Annual salary");
        var actionSpace = new VisualElement();
        actionSpace.AddToClassList("recruitment-action");
        heading.Add(actionSpace);
        roster.Add(heading);

        foreach (var driver in drivers)
        {
            var portrait = CreateStaffPortrait(driver.FullName, driver.age, driver.gender, driver.hometownCity,
                driver.hometownState, activeCareer.cars[0].number, "recruitment-portrait", driver.ethnicity);
            AddCandidate(portrait, driver.FullName, driver.age, driver.gender, driver.hometownCity, driver.hometownState,
                null, $"Aggression {driver.personalityTraits.aggression:P0}", driver.annualSalary, () => HireDriver(driver));
        }

        foreach (var candidate in candidates)
        {
            AddCandidate(CreateEmployeePortrait(candidate, candidate.recruitLiveryNumber, "recruitment-portrait"),
                candidate.FullName, candidate.age, candidate.gender, candidate.hometownCity, candidate.hometownState,
                candidate.RoleName, $"Engines {candidate.engines:F2}  /  Suspensions {candidate.suspensions:F2}\n" +
                $"Transmissions {candidate.transmissions:F2}  /  Bodywork {candidate.bodywork:F2}\nBrakes {candidate.brakes:F2}",
                candidate.annualSalary, () => HireEmployee(candidate));
        }

        roster.schedule.Execute(() => roster.scrollOffset = scroll);
        settings?.RestorePalette();

        void AddCandidate(Image portrait, string name, int age, string gender, string city, string state,
            string role, string details, long salary, System.Action hireCandidate)
        {
            var row = MakeRow();
            var photo = new VisualElement();
            photo.AddToClassList("recruitment-photo-cell");
            if (portrait != null)
                photo.Add(portrait);
            row.Add(photo);
            AddCell(row, $"{name}\n{age} / {gender}\n{city}, {state}", "recruitment-candidate");
            if (role != null)
                AddCell(row, role);
            AddCell(row, details, "recruitment-skills");
            AddCell(row, RaceFinances.Dollars(salary));
            var action = new VisualElement();
            action.AddToClassList("recruitment-action");
            var hire = new Button(hireCandidate) { text = "Hire" };
            hire.AddToClassList("nav-button");
            action.Add(hire);
            row.Add(action);
            roster.Add(row);
        }

        VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.AddToClassList("results-grid-row");
            row.AddToClassList("recruitment-row");
            return row;
        }

        void AddCell(VisualElement row, string text, string styleClass = null)
        {
            var cell = new Label(text);
            cell.AddToClassList("results-grid-cell");
            cell.AddToClassList("garage-detail");
            if (styleClass != null)
                cell.AddToClassList(styleClass);
            row.Add(cell);
        }
    }

    private void HireDriver(Driver candidate)
    {
        var career = activeCareer;
        if (HasDriver(career))
            return;

        int index = career.driverCandidates.IndexOf(candidate);
        if (index < 0)
            return;

        career.driverCandidates[index] = Driver.Create(new System.Random(), null);
        career.drivers.Add(candidate);
        bool welcomeAdded = TeamCommunications.AddDriverWelcome(career);
        try
        {
            careerSetup.SaveProgress(career);
        }
        catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
        {
            career.drivers.Remove(candidate);
            if (welcomeAdded)
                career.inbox.RemoveAt(career.inbox.Count - 1);
            career.driverCandidates[index] = candidate;
            DisplayRecruitment("Could not save the hire. " + ex.Message);
            return;
        }

        DisplayEmployees(career);
        RefreshDriverLabel(career);
        RefreshSchedule();
        CloseRecruitment();
        SetEmployeeStatus($"{candidate.FullName} was hired as a driver.");
    }

    private void HireEmployee(Employee candidate)
    {
        var career = activeCareer;
        int index = career.recruitCandidates.IndexOf(candidate);
        if (index < 0)
            return;

        var replacement = CreateRecruit(candidate.role, new System.Random());
        career.recruitCandidates[index] = replacement;
        career.employees.Add(candidate);
        try
        {
            careerSetup.SaveProgress(career);
        }
        catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
        {
            career.employees.Remove(candidate);
            career.recruitCandidates[index] = candidate;
            DisplayRecruitment("Could not save the hire. " + ex.Message);
            return;
        }

        DisplayEmployees(career);
        RefreshDriverLabel(career);
        RefreshSchedule();
        DisplayRecruitment($"{candidate.FullName} was hired as {candidate.RoleName}.");
    }

    private void CancelFireEmployee()
    {
        pendingDismissal = null;
        garageScreen.Q("fireEmployeePrompt").style.display = DisplayStyle.None;
    }

    private void DismissStaff<T>(List<T> roster, T person, string name)
    {
        CancelFireEmployee();
        int index = roster.IndexOf(person);
        if (index < 0)
            return;

        roster.RemoveAt(index);
        try
        {
            careerSetup.SaveProgress(activeCareer);
        }
        catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
        {
            roster.Insert(index, person);
            SetEmployeeStatus("Could not save the dismissal. " + ex.Message);
            return;
        }

        DisplayEmployees(activeCareer);
        RefreshDriverLabel(activeCareer);
        RefreshSchedule();
        SetEmployeeStatus($"{name} was dismissed.");
    }

    private void SetEmployeeStatus(string message)
    {
        var status = garageScreen.Q<Label>("employeeStatus");
        status.text = message ?? string.Empty;
        status.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
    }

    private static bool HasDriver(Career career) => career.Driver != null;

    private void RefreshDriverLabel(Career career)
    {
        garageScreen.Q<Button>("recruitDriverButton").SetEnabled(!HasDriver(career));
        string driver = career.Driver?.FullName ?? "No driver";
        garageScreen.Q<Label>("homeDriverLabel").text = $"{career.cars[0].DisplayName}  /  {driver}";
    }

    private static bool InitializeFinances(Career career)
    {
        if (career.financesInitialized)
            return false;
        career.teamBalance = RaceFinances.StartingBalance;
        career.financesInitialized = true;
        return true;
    }

    private void AdvanceCareerDay()
    {
        if (activeCareer == null || !Career.TryReadDate(activeCareer.currentDate, out var date) ||
            date == System.DateTime.MaxValue.Date)
            return;

        var race = DueRace(date);
        if (race != null)
        {
            garageScreen.Q<Label>("skipRaceMessage").text =
                $"Advance the date and miss the race at {race.trackName}? Your car will sit out, and employee payroll will still be deducted.";
            garageScreen.Q("skipRacePrompt").style.display = DisplayStyle.Flex;
            garageScreen.Q<Button>("cancelSkipRaceButton").Focus();
            return;
        }

        string previous = activeCareer.currentDate;
        int inboxCount = activeCareer.inbox?.Count ?? 0;
        activeCareer.SetDate(date.AddDays(1));
        var undoRepairs = UpdateCareerRepairs();
        try
        {
            CrewChiefMail.SendPreview(activeCareer, catalog, RaceCarBlockReason);
            careerSetup.SaveProgress(activeCareer);
        }
        catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
        {
            undoRepairs?.Invoke();
            activeCareer.currentDate = previous;
            CrewChiefMail.UndoNewMessages(activeCareer, inboxCount);
            Debug.LogWarning("Could not save the next day. " + ex.Message);
        }
        RefreshCareerDate();
        if (garageScreen.Q("shopPage").style.display == DisplayStyle.Flex)
            RefreshShopCarPicker();
        if (showingWorkInProgress && garageScreen.Q("employeesPage").style.display == DisplayStyle.Flex)
            DisplayEmployeeWorkInProgress();
        if (garageScreen.Q("carsPage").style.display == DisplayStyle.Flex)
            RefreshTeamCars();
        var mailPage = garageScreen.Q("mailPage");
        if (mailPage.style.display == DisplayStyle.Flex)
        {
            teamMail.Show(mailPage, activeCareer, settings.RestorePalette, DeleteSocialPosts, SocialDriverPortrait);
            settings.RestorePalette();
        }
    }

    private ScheduledRace DueRace(System.DateTime date)
    {
        var next = activeCareer?.schedule?.races?.FirstOrDefault(race => !race.completed);
        return Career.TryReadDate(next?.date, out var raceDate) && raceDate <= date ? next : null;
    }

    private void CloseSkipRacePrompt()
    {
        garageScreen.Q("skipRacePrompt").style.display = DisplayStyle.None;
        garageScreen.Q<Button>("nextDayButton").Focus();
    }

    private void ConfirmSkipRace()
    {
        if (garageScreen.Q("skipRacePrompt").style.display != DisplayStyle.Flex ||
            !Career.TryReadDate(activeCareer.currentDate, out var date) || date == System.DateTime.MaxValue.Date)
            return;

        var race = DueRace(date);
        if (race == null)
        {
            CloseSkipRacePrompt();
            return;
        }

        if (!TrySimulateEvent(race, date.AddDays(1), out string error))
        {
            garageScreen.Q<Label>("skipRaceMessage").text = error;
            return;
        }

        CloseSkipRacePrompt();
        RefreshCareerDate();
        RefreshTeamBalance();
        RefreshSchedule();
        if (garageScreen.Q("financesPage").style.display == DisplayStyle.Flex)
            DisplayFinances();
        if (garageScreen.Q("sponsorsPage").style.display == DisplayStyle.Flex)
            DisplaySponsors();
        if (garageScreen.Q("carsPage").style.display == DisplayStyle.Flex)
            RefreshTeamCars();
        settings?.RestorePalette();
    }

    private void RefreshCareerDate()
    {
        bool valid = Career.TryReadDate(activeCareer.currentDate, out var date);
        garageScreen.Q<Label>("teamDayOfWeek").text = valid ? date.ToString("dddd", CultureInfo.CurrentCulture) : string.Empty;
        garageScreen.Q<Label>("teamDate").text = valid ? date.ToString("d", CultureInfo.CurrentCulture) : string.Empty;
        garageScreen.Q<Button>("nextDayButton").SetEnabled(valid && date < System.DateTime.MaxValue.Date);
    }

    private void RefreshTeamBalance()
    {
        garageScreen.Q<Label>("teamBalanceLabel").text = $"Team balance  /  {RaceFinances.Dollars(activeCareer.teamBalance)}";
    }

    private void ShowTitle()
    {
        garageScreen.style.display = DisplayStyle.None;
        titleScreen.style.display = DisplayStyle.Flex;
        continueCareerButton.Focus();
    }

    private void RefreshTeamCars()
    {
        var rows = garageScreen.Q<ScrollView>("teamCarRows");
        var status = garageScreen.Q<Label>("carsStatus");
        status.style.display = DisplayStyle.None;
        Vector2 scroll = rows.scrollOffset;
        rows.Clear();
        var sponsors = ActiveSponsorNames();
        foreach (var car in activeCareer.cars)
            rows.Add(TeamCarCard.Create(car, carThumbnails.Get(car, sponsors), activeCareer.cars.Count > 1,
                () => ShowCarPaint(car), () => ConfirmCarSale(car)));
        rows.schedule.Execute(() => rows.scrollOffset = scroll);
        addTeamCarButton.style.display = activeCareer.cars.Count < TeamCar.MaximumOwned
            ? DisplayStyle.Flex : DisplayStyle.None;
        settings?.RestorePalette();
    }

    private void ConfirmCarSale(TeamCar car)
    {
        if (activeCareer.cars.Count <= 1 || !activeCareer.cars.Contains(car))
            return;

        long price = UsedCarListing.TradeInPrice(car);
        string amount = RaceFinances.Dollars(price);
        var options = OpenCarOptions("Sell Car");
        string repairs = car.repairs?.Count > 0 ? " The sale ends any repairs on this chassis." : string.Empty;
        var question = new Label($"Sell chassis {car.chassisNumber} for {amount}?{repairs}");
        question.AddToClassList("garage-detail");
        options.Add(question);
        var sell = new Button(() => SellCar(car, price)) { text = $"Sell for {amount}" };
        sell.AddToClassList("nav-button");
        options.Add(sell);
        settings.RestorePalette();
    }

    private void SellCar(TeamCar car, long price)
    {
        var career = activeCareer;
        int index = career.cars.IndexOf(car);
        if (index < 0 || career.cars.Count <= 1)
            return;

        var assignedRaces = career.schedule.races
            .Where(race => !race.completed && !string.IsNullOrEmpty(race.carChassisNumber) &&
                race.carChassisNumber == car.chassisNumber)
            .Select(race => (Race: race, Number: race.carNumber)).ToArray();
        career.cars.RemoveAt(index);
        career.teamBalance += price;
        career.schedule.carSales += price;
        foreach (var assignment in assignedRaces)
        {
            assignment.Race.carChassisNumber = string.Empty;
            assignment.Race.carNumber = 0;
        }
        try
        {
            careerSetup.SaveProgress(career);
        }
        catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
        {
            career.cars.Insert(index, car);
            career.teamBalance -= price;
            career.schedule.carSales -= price;
            foreach (var assignment in assignedRaces)
            {
                assignment.Race.carChassisNumber = car.chassisNumber;
                assignment.Race.carNumber = assignment.Number;
            }
            garageScreen.Q<Label>("carOptionsStatus").text = "Could not save the sale. " + ex.Message;
            return;
        }

        CloseCarOptions();
        RefreshTeamCars();
        RefreshTeamBalance();
        RefreshSchedule();
        var status = garageScreen.Q<Label>("carsStatus");
        status.text = $"Sold chassis {car.chassisNumber} for {RaceFinances.Dollars(price)}.";
        status.style.display = DisplayStyle.Flex;
    }


    private ScrollView OpenCarOptions(string title)
    {
        paintPreviewThumbnails.Dispose();
        garageScreen.Q<Label>("carOptionsTitle").text = title;
        garageScreen.Q<Label>("carOptionsStatus").text = "";
        var rows = garageScreen.Q<ScrollView>("carOptionsRows");
        rows.Clear();
        rows.scrollOffset = Vector2.zero;
        garageScreen.Q("carOptionsOverlay").style.display = DisplayStyle.Flex;
        garageScreen.Q<Button>("carOptionsClose").Focus();
        return rows;
    }

    private void CloseCarOptions()
    {
        garageScreen.Q("carOptionsOverlay").style.display = DisplayStyle.None;
        paintPreviewThumbnails.Dispose();
    }

    private void ShowRaceCarPicker()
    {
        if (selectedEvent == null || selectedEvent.completed)
            return;

        var rows = OpenCarOptions("Choose Race Car");
        foreach (var car in activeCareer.cars)
        {
            string reason = RaceCarBlockReason(car, selectedEvent);
            var image = new Image
            {
                image = carThumbnails.Get(car, ActiveSponsorNames()),
                scaleMode = ScaleMode.ScaleToFit,
                pickingMode = PickingMode.Ignore
            };
            var row = AddCarOption(rows, $"Chassis # {car.chassisNumber}" + (reason == null ? "" : $"\n{reason}"),
                image, () =>
                {
                    CloseCarOptions();
                    AssignEventCar(car);
                });
            row.SetEnabled(reason == null);
        }
        settings?.RestorePalette();
    }

    private void ShowCarPaint(TeamCar car)
    {
        var rows = OpenCarOptions($"Paint Scheme · Chassis # {car.chassisNumber}");
        var original = car.PaintStyle;
        var schemes = (CarPaintScheme[])System.Enum.GetValues(typeof(CarPaintScheme));
        var colors = new[] { original.Primary, original.Secondary, original.Trim };
        bool hasTrim = original.HasTrim;
        var sponsors = ActiveSponsorNames();
        var preview = new Image { scaleMode = ScaleMode.ScaleToFit, tooltip = "Right-drag to rotate the car" };
        preview.AddToClassList("car-paint-preview");
        preview.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button != 1)
                return;
            preview.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        });
        preview.RegisterCallback<PointerMoveEvent>(evt =>
        {
            if (!preview.HasPointerCapture(evt.pointerId) || (evt.pressedButtons & 2) == 0)
                return;
            paintPreviewThumbnails.RotatePreview(evt.deltaPosition);
            preview.MarkDirtyRepaint();
            evt.StopPropagation();
        });
        preview.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (evt.button != 1 || !preview.HasPointerCapture(evt.pointerId))
                return;
            preview.ReleasePointer(evt.pointerId);
            evt.StopPropagation();
        });
        rows.Add(preview);
        var scheme = new DropdownField("Paint scheme", schemes.Select(CarPaintStyle.SchemeName).ToList(),
            System.Array.IndexOf(schemes, original.Scheme));
        rows.Add(scheme);
        scheme.RegisterValueChangedCallback(_ => UpdatePreview());
        AddColor("Primary color", 0);
        AddColor("Secondary color", 1);
        AddColor("Trim color", 2);
        var save = new Button(Save) { text = "Save Paint Scheme" };
        save.AddToClassList("nav-button");
        rows.Add(save);
        UpdatePreview();
        settings?.RestorePalette();

        CarPaintStyle Draft() => new CarPaintStyle(schemes[scheme.index], colors[0], colors[1], colors[2], hasTrim);

        void UpdatePreview()
        {
            preview.image = paintPreviewThumbnails.Get(car, sponsors, Draft());
            garageScreen.Q<Label>("carOptionsStatus").text = "";
        }

        void AddColor(string label, int index)
        {
            var swatch = new VisualElement { pickingMode = PickingMode.Ignore };
            swatch.AddToClassList("car-paint-swatch");
            swatch.style.backgroundColor = (Color)colors[index];
            var button = new Button(() =>
            {
                if (!WindowsColorPicker.TryPick(colors[index], out var chosen))
                    return;
                colors[index] = chosen;
                hasTrim |= index == 2;
                swatch.style.backgroundColor = (Color)chosen;
                UpdatePreview();
            });
            button.AddToClassList("nav-button");
            button.AddToClassList("car-paint-color");
            button.Add(swatch);
            button.Add(new Label(label) { pickingMode = PickingMode.Ignore });
            rows.Add(button);
        }

        void Save()
        {
            var previous = (car.primaryPaint, car.secondaryPaint, car.trimPaint, car.paintScheme);
            car.primaryPaint = "#" + ColorUtility.ToHtmlStringRGB(colors[0]);
            car.secondaryPaint = "#" + ColorUtility.ToHtmlStringRGB(colors[1]);
            car.trimPaint = hasTrim ? "#" + ColorUtility.ToHtmlStringRGB(colors[2]) : null;
            car.paintScheme = Draft().Scheme;
            try
            {
                careerSetup.SaveProgress(activeCareer);
            }
            catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
            {
                (car.primaryPaint, car.secondaryPaint, car.trimPaint, car.paintScheme) = previous;
                garageScreen.Q<Label>("carOptionsStatus").text = "Could not save paint scheme. " + ex.Message;
                return;
            }

            CloseCarOptions();
            RefreshTeamCars();
        }
    }

    private static Button AddCarOption(ScrollView rows, string text, VisualElement preview, System.Action action)
    {
        var button = new Button(action);
        button.AddToClassList("car-option-row");
        preview.AddToClassList("car-option-image");
        button.Add(preview);
        var label = new Label(text) { pickingMode = PickingMode.Ignore };
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.flexShrink = 1;
        button.Add(label);
        rows.Add(button);
        return button;
    }

    private void OpenCarMaintenance(TeamCar car, CarComponent? component = null)
    {
        maintenanceComponent = component;

        garageScreen.Q("garageBody").style.display = DisplayStyle.None;
        garageScreen.Q("carMaintenancePage").style.display = DisplayStyle.Flex;
        garageScreen.Q("garageFooter").SetEnabled(false);
        garageScreen.Q<ScrollView>("carMaintenanceComponents").scrollOffset = Vector2.zero;
        DisplayCarMaintenance(car);
        garageScreen.Q<Button>("carMaintenanceBackButton").Focus();
    }

    private void CloseCarMaintenance()
    {
        if (!maintenanceComponent.HasValue)
        {
            CloseCarMarket();
            return;
        }
        maintenanceComponent = null;
        DisplayGaragePage(System.Array.FindIndex(garagePages, page => page.name == "shopPage"));
        garageScreen.Q("shopCarPicker").Q<Button>()?.Focus();
    }

    private void DisplayCarMaintenance(TeamCar car, string message = "")
    {
        garageScreen.Q<Label>("carMaintenanceSummary").text =
            $"Chassis # {car.chassisNumber}  •  Budget {RaceFinances.Dollars(activeCareer.teamBalance)}" +
            $"  •  Active repairs: {car.repairs?.Count ?? 0}";
        garageScreen.Q<Label>("carMaintenanceStatus").text = message;
        var components = garageScreen.Q<ScrollView>("carMaintenanceComponents");
        Vector2 scroll = components.scrollOffset;
        components.Clear();
        var employees = activeCareer.employees.Where(CanAssignRepair).ToList();
        bool validDate = Career.TryReadDate(activeCareer.currentDate, out var date);

        foreach (var component in TeamCar.Components)
        {
            if (maintenanceComponent.HasValue && maintenanceComponent.Value != component)
                continue;
            var condition = car.GetComponent(component);
            var repair = car.repairs?.Find(item => item.component == component);
            var row = new VisualElement();
            row.AddToClassList("garage-entry");
            var description = new Label($"{component}  •  Level {condition.Level}/{TeamCar.MaximumComponentLevel}  •  " +
                $"Wear {condition.Wear:P0}  •  Effective value {condition.EffectiveValue:F2}");
            description.AddToClassList("garage-detail");
            row.Add(description);

            var actions = new VisualElement();
            actions.AddToClassList("employee-role-actions");
            row.Add(actions);
            if (!maintenanceComponent.HasValue && repair == null && condition.Level < TeamCar.MaximumComponentLevel)
            {
                var upgrade = new Button(() => ConfirmComponentUpgrade(car, component))
                    { text = $"Upgrade ({RaceFinances.Dollars(TeamCar.ComponentUpgradeCost)})" };
                upgrade.AddToClassList("nav-button");
                upgrade.SetEnabled(validDate && CanUpgradeComponent(car, component, date));
                actions.Add(upgrade);
                if (car.TryGetNextUpgradeDate(component, out var nextUpgrade) && date < nextUpgrade)
                {
                    var availability = new Label($"Next upgrade {nextUpgrade:d}");
                    availability.AddToClassList("garage-detail");
                    row.Add(availability);
                }
            }

            if (repair != null)
            {
                string remaining = validDate
                    ? $" • {ComponentRepair.FormatDuration(repair.RemainingHours(date))} remaining" : "";
                string paid = repair.cost > 0 ? $" • Paid {RaceFinances.Dollars(repair.cost)}" : "";
                var status = new Label($"Assigned to {repair.employeeName} • Due {repair.completionDate}{remaining}{paid}");
                status.AddToClassList("garage-detail");
                actions.Add(status);
                var cancel = new Button(() => CancelComponentRepair(car, repair)) { text = "Cancel Repair" };
                cancel.AddToClassList("nav-button");
                cancel.SetEnabled(validDate);
                actions.Add(cancel);
            }
            else if (condition.Wear > 0)
            {
                int hours = ComponentRepair.HoursForWear(condition.Wear);
                int cost = ComponentRepair.CostForWear(condition.Wear);
                string price = RaceFinances.Dollars(cost);
                var choices = new List<string> { employees.Count > 0 ? "Select an employee" : "No available employees" };
                for (int index = 0; index < employees.Count; index++)
                {
                    var employee = employees[index];
                    choices.Add($"{index + 1}. {employee.FullName} ({employee.RoleName}) • Skill {employee.RepairSkill(component):F2}");
                }
                var picker = new DropdownField(choices, 0);
                picker.SetEnabled(employees.Count > 0);
                actions.Add(picker);
                var button = new Button(() =>
                {
                    if (picker.index > 0)
                        StartComponentRepair(car, component, employees[picker.index - 1], date);
                }) { text = $"Repair ({price} • {ComponentRepair.FormatDuration(hours)})" };
                button.AddToClassList("nav-button");
                button.SetEnabled(false);
                picker.RegisterValueChangedCallback(_ =>
                {
                    bool assigned = picker.index > 0;
                    int estimate = assigned
                        ? ComponentRepair.EstimateHours(condition.Wear, employees[picker.index - 1].RepairSkill(component))
                        : hours;
                    button.text = $"Repair ({price} • {ComponentRepair.FormatDuration(estimate)})";
                    button.SetEnabled(assigned && validDate && activeCareer.teamBalance >= cost &&
                        date <= System.DateTime.MaxValue.Date.AddHours(-estimate));
                });
                actions.Add(button);
            }
            components.Add(row);
        }
        components.schedule.Execute(() => components.scrollOffset = scroll);
        settings?.RestorePalette();
    }

    private void CancelComponentRepair(TeamCar car, ComponentRepair repair)
    {
        if (!Career.TryReadDate(activeCareer.currentDate, out var date))
            return;

        int index = car.repairs.IndexOf(repair);
        if (index < 0)
            return;

        float previousWear = car.ComponentWear(repair.component);
        float remaining = repair.RemainingFraction(date);
        car.ComponentWear(repair.component) = previousWear * remaining;
        car.repairs.RemoveAt(index);
        SaveMaintenance(car, () =>
        {
            car.ComponentWear(repair.component) = previousWear;
            car.repairs.Insert(index, repair);
        }, $"{repair.component} repair cancelled. Wear is now {car.ComponentWear(repair.component):P0}.");
    }

    private bool CanAssignRepair(Employee employee) =>
        (employee.role == EmployeeRole.GeneralMechanic ||
            employee.RoleName.Contains("Specialist", System.StringComparison.Ordinal)) &&
        !activeCareer.cars.Any(car => car.repairs?.Any(repair => repair.IsAssignedTo(employee)) == true);

    private void StartComponentRepair(TeamCar car, CarComponent component, Employee employee, System.DateTime date)
    {
        car.repairs ??= new List<ComponentRepair>();
        float wear = car.ComponentWear(component);
        int cost = ComponentRepair.CostForWear(wear);
        if (cost == 0 || activeCareer.teamBalance < cost || car.repairs.Any(item => item.component == component) ||
            !activeCareer.employees.Contains(employee) || !CanAssignRepair(employee))
            return;

        int hours = ComponentRepair.EstimateHours(wear, employee.RepairSkill(component));
        if (date > System.DateTime.MaxValue.Date.AddHours(-hours))
            return;
        var completion = date.AddHours(hours);
        // The career clock advances by whole days, so finish on the first day at or after the job is due.
        var completionDay = completion.TimeOfDay == System.TimeSpan.Zero
            ? completion.Date : completion.Date.AddDays(1);
        string previousAssigneeId = employee.repairAssigneeId;
        if (string.IsNullOrEmpty(previousAssigneeId))
            employee.repairAssigneeId = System.Guid.NewGuid().ToString("N");
        var repair = new ComponentRepair
        {
            component = component,
            employeeName = employee.FullName,
            employeeId = employee.repairAssigneeId,
            durationHours = hours,
            cost = cost,
            completionDate = completionDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };
        car.repairs.Add(repair);
        activeCareer.teamBalance -= cost;
        activeCareer.schedule.componentRepairs += cost;
        SaveMaintenance(car, () =>
        {
            car.repairs.Remove(repair);
            employee.repairAssigneeId = previousAssigneeId;
            activeCareer.teamBalance += cost;
            activeCareer.schedule.componentRepairs -= cost;
        },
            $"{component} repair assigned to {employee.FullName} for {RaceFinances.Dollars(cost)}.");
    }

    private bool CanUpgradeComponent(TeamCar car, CarComponent component, System.DateTime date) =>
        car.ComponentLevel(component) < TeamCar.MaximumComponentLevel &&
        activeCareer.teamBalance >= TeamCar.ComponentUpgradeCost &&
        car.repairs?.Any(item => item.component == component) != true &&
        (!car.TryGetNextUpgradeDate(component, out var nextUpgrade) || date >= nextUpgrade);

    private void ConfirmComponentUpgrade(TeamCar car, CarComponent component)
    {
        if (!Career.TryReadDate(activeCareer.currentDate, out var date) || !CanUpgradeComponent(car, component, date))
            return;

        int level = car.ComponentLevel(component);
        string cost = RaceFinances.Dollars(TeamCar.ComponentUpgradeCost);
        var options = OpenCarOptions("Confirm Upgrade");
        var text = new Label($"Upgrade {component} on chassis {car.chassisNumber} to level {level + 1} for {cost}?");
        text.AddToClassList("garage-detail");
        options.Add(text);
        var confirm = new Button(() =>
        {
            CloseCarOptions();
            UpgradeComponent(car, component);
        }) { text = $"Upgrade for {cost}" };
        confirm.AddToClassList("nav-button");
        options.Add(confirm);
        settings.RestorePalette();
    }

    private void UpgradeComponent(TeamCar car, CarComponent component)
    {
        ref int level = ref car.ComponentLevel(component);
        if (!Career.TryReadDate(activeCareer.currentDate, out var date) || !CanUpgradeComponent(car, component, date))
            return;

        var previousUpgradeDays = (int[])car.componentUpgradeDays?.Clone();
        car.RecordComponentUpgrade(component, date);
        float previousWear = car.ComponentWear(component);
        level++;
        car.ComponentWear(component) = 0;
        activeCareer.teamBalance -= TeamCar.ComponentUpgradeCost;
        activeCareer.schedule.componentUpgrades += TeamCar.ComponentUpgradeCost;
        SaveMaintenance(car, () =>
        {
            car.ComponentLevel(component)--;
            car.componentUpgradeDays = previousUpgradeDays;
            car.ComponentWear(component) = previousWear;
            activeCareer.teamBalance += TeamCar.ComponentUpgradeCost;
            activeCareer.schedule.componentUpgrades -= TeamCar.ComponentUpgradeCost;
        }, $"{component} upgraded to level {level}.");
    }

    private void SaveMaintenance(TeamCar car, System.Action undo, string message)
    {
        try
        {
            careerSetup.SaveProgress(activeCareer);
        }
        catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
        {
            undo();
            message = "Could not save component maintenance. " + ex.Message;
        }
        RefreshTeamBalance();
        if (garageScreen.Q("carMaintenancePage").style.display == DisplayStyle.Flex)
            DisplayCarMaintenance(car, message);
        else
        {
            RefreshShopCarPicker();
            var status = garageScreen.Q<Label>("shopMaintenanceStatus");
            status.text = message;
            status.style.display = DisplayStyle.Flex;
            settings.RestorePalette();
        }
    }

    private System.Action UpdateCareerRepairs()
    {
        if (!Career.TryReadDate(activeCareer.currentDate, out var date))
            return null;

        var undoCompleted = CompleteDueRepairs(activeCareer.cars.Concat(activeCareer.rivalCars), date);
        var undoScheduled = ScheduleRivalRepairs(date);
        return () =>
        {
            undoScheduled();
            undoCompleted?.Invoke();
        };
    }

    private static System.Action CompleteDueRepairs(IEnumerable<TeamCar> cars, System.DateTime date)
    {
        var completed = new List<(TeamCar Car, ComponentRepair Repair, float Wear, int Index)>();
        foreach (var car in cars)
        {
            if (car.repairs == null)
                continue;
            for (int index = car.repairs.Count - 1; index >= 0; index--)
            {
                var repair = car.repairs[index];
                if (!Career.TryReadDate(repair.completionDate, out var due) || due > date)
                    continue;
                completed.Add((car, repair, car.ComponentWear(repair.component), index));
                car.ComponentWear(repair.component) = 0;
                car.repairs.RemoveAt(index);
            }
        }

        if (completed.Count == 0)
            return null;

        return () =>
        {
            for (int i = completed.Count - 1; i >= 0; i--)
            {
                var (car, repair, wear, index) = completed[i];
                car.ComponentWear(repair.component) = wear;
                car.repairs.Insert(index, repair);
            }
        };
    }

    private System.Action ScheduleRivalRepairs(System.DateTime date)
    {
        var added = new List<(TeamCar Car, ComponentRepair Repair, Employee Employee, string PreviousId)>();
        var entries = TrackPreview.CreateTimingEntries(activeCareer.cars[0], activeCareer);
        foreach (var team in entries.Skip(1).Where(entry => entry.car != null).GroupBy(entry => entry.teamName))
        {
            var staff = activeCareer.rivalEmployees.Find(roster => roster.teamName == team.Key)?.employees;
            if (staff == null)
                continue;

            var cars = team.Select(entry => entry.car).ToArray();
            var currentRepairs = cars.Where(car => car.repairs != null).SelectMany(car => car.repairs).ToArray();
            var available = staff.Where(employee => !currentRepairs.Any(repair => repair.IsAssignedTo(employee))).ToList();
            var jobs = cars.SelectMany(car => TeamCar.Components.Select(component =>
                    (Car: car, Component: component, Wear: (float)car.ComponentWear(component))))
                .Where(job => job.Wear > 0 && job.Car.repairs?.Any(repair => repair.component == job.Component) != true)
                .OrderByDescending(job => job.Wear);

            foreach (var (car, component, wear) in jobs)
            {
                var specialist = component switch
                {
                    CarComponent.Engine => EmployeeRole.EngineSpecialist,
                    CarComponent.Transmission => EmployeeRole.TransmissionSpecialist,
                    CarComponent.Brakes => EmployeeRole.BrakeSpecialist,
                    CarComponent.Suspension => EmployeeRole.SuspensionSpecialist,
                    _ => EmployeeRole.BodySpecialist
                };
                var employee = available.Where(person => person.role == specialist || person.role == EmployeeRole.GeneralMechanic)
                    .OrderByDescending(person => person.role == specialist)
                    .ThenByDescending(person => person.RepairSkill(component))
                    .FirstOrDefault();
                if (employee == null)
                    continue;

                int hours = ComponentRepair.EstimateHours(wear, employee.RepairSkill(component));
                int days = (hours + 23) / 24;
                if (date > System.DateTime.MaxValue.Date.AddDays(-days))
                    continue;

                string previousId = employee.repairAssigneeId;
                if (string.IsNullOrEmpty(previousId))
                    employee.repairAssigneeId = System.Guid.NewGuid().ToString("N");

                var repair = new ComponentRepair
                {
                    component = component,
                    employeeName = employee.FullName,
                    employeeId = employee.repairAssigneeId,
                    durationHours = hours,
                    completionDate = date.AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                };
                car.repairs ??= new List<ComponentRepair>();
                car.repairs.Add(repair);
                added.Add((car, repair, employee, previousId));
                available.Remove(employee);
            }
        }

        return () =>
        {
            foreach (var (car, repair, employee, previousId) in added)
            {
                car.repairs.Remove(repair);
                employee.repairAssigneeId = previousId;
            }
        };
    }

    private bool PrepareRivalCarsForEvent(ScheduledRace race, out string error)
    {
        error = null;
        if (!Career.TryReadDate(race.date, out var date))
            return true;

        var undo = CompleteDueRepairs(activeCareer.rivalCars, date);
        if (undo == null)
            return true;

        try
        {
            careerSetup.SaveProgress(activeCareer);
            return true;
        }
        catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
        {
            undo();
            error = "Could not save rival repairs. " + ex.Message;
            return false;
        }
    }

    private void OpenCarMarket()
    {
        var career = activeCareer;
        const int listingCount = 10;
        const int refreshDays = 7;
        var previousListings = career.usedCars ?? new List<UsedCarListing>();
        string previousDate = career.usedCarInventoryDate;
        bool dateKnown = Career.TryReadDate(career.currentDate, out var date);
        bool refresh = dateKnown &&
            (!Career.TryReadDate(previousDate, out var inventoryDate) || (date - inventoryDate).Days >= refreshDays);

        string message = null;
        career.usedCars = previousListings;
        if (refresh || previousListings.Count < listingCount)
        {
            var listings = refresh ? new List<UsedCarListing>() : new List<UsedCarListing>(previousListings);
            var random = new System.Random();
            while (listings.Count < listingCount)
                listings.Add(UsedCarListing.Create(career.cars[0].number, random));

            career.usedCars = listings;
            if (refresh)
                career.usedCarInventoryDate = career.currentDate;
            try
            {
                careerSetup.SaveProgress(career);
            }
            catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
            {
                career.usedCars = previousListings;
                career.usedCarInventoryDate = previousDate;
                message = "Could not save used car listings. " + ex.Message;
            }
        }

        garageScreen.Q("garageBody").style.display = DisplayStyle.None;
        garageScreen.Q("carMarketPage").style.display = DisplayStyle.Flex;
        garageScreen.Q("garageFooter").SetEnabled(false);
        RefreshCarMarket(message);
        garageScreen.Q<Button>("carMarketBackButton").Focus();
    }

    private void CloseCarMarket()
    {
        int carsPage = System.Array.FindIndex(garagePages, page => page.name == "carsPage");
        DisplayGaragePage(carsPage);
        garageTabs[carsPage].Focus();
    }

    private void RefreshCarMarket(string message = null)
    {
        var career = activeCareer;
        garageScreen.Q<Label>("carMarketBalance").text = $"Team balance  /  {RaceFinances.Dollars(career.teamBalance)}";
        var status = garageScreen.Q<Label>("carMarketStatus");
        status.text = message ?? (career.cars.Count >= TeamCar.MaximumOwned ? "Your car inventory is full." : string.Empty);
        status.style.display = string.IsNullOrEmpty(status.text) ? DisplayStyle.None : DisplayStyle.Flex;
        garageScreen.Q<Button>("buyNewChassisButton").SetEnabled(CanBuyCar(UsedCarListing.NewChassisPrice));

        var listings = garageScreen.Q<ScrollView>("usedCarListings");
        listings.Clear();
        if (career.usedCars.Count == 0)
        {
            AddResultsMessage(listings, "No used cars available.");
        }
        else
        {
            AddListingRow(new[] { "Manufacturer", "Chassis Type", "Price" });
            foreach (var listing in career.usedCars)
            {
                var car = listing.car;
                AddListingRow(new[] { car.manufacturer.ToString(), car.ChassisTypeName,
                    RaceFinances.Dollars(listing.price) }, listing);
            }
        }

        settings?.RestorePalette();

        void AddListingRow(string[] values, UsedCarListing listing = null)
        {
            var row = new VisualElement();
            row.AddToClassList("results-grid-row");
            row.EnableInClassList("results-grid-header", listing == null);
            foreach (string value in values)
            {
                var label = new Label(value);
                label.AddToClassList("garage-detail");
                label.AddToClassList("results-grid-cell");
                row.Add(label);
            }
            VisualElement action = new VisualElement();
            if (listing != null)
            {
                var buy = new Button(() => PurchaseCar(listing.car, listing.price, listing)) { text = "Buy" };
                buy.AddToClassList("nav-button");
                buy.SetEnabled(CanBuyCar(listing.price));
                action = buy;
            }
            action.AddToClassList("car-market-action");
            row.Add(action);
            listings.Add(row);
        }
    }

    private bool CanBuyCar(long price) =>
        activeCareer.cars.Count < TeamCar.MaximumOwned && activeCareer.teamBalance >= price;

    private void BuyNewChassis()
    {
        if (!CanBuyCar(UsedCarListing.NewChassisPrice))
            return;

        int manufacturer = garageScreen.Q<DropdownField>("newChassisManufacturer").index;
        int type = garageScreen.Q<DropdownField>("newChassisType").index;
        if (manufacturer < 0 || manufacturer >= NewChassisManufacturers.Length || type < 0 || type >= NewChassisTypes.Length)
            return;

        var car = new TeamCar
        {
            number = activeCareer.cars[0].number,
            manufacturer = NewChassisManufacturers[manufacturer],
            chassisType = NewChassisTypes[type],
            primaryPaint = NewChassisPaint,
            secondaryPaint = NewChassisPaint,
            componentWearInitialized = true
        };
        car.Initialize(new System.Random());
        PurchaseCar(car, UsedCarListing.NewChassisPrice);
    }

    private void PurchaseCar(TeamCar car, long price, UsedCarListing listing = null)
    {
        var career = activeCareer;
        int listingIndex = listing == null ? -1 : career.usedCars.IndexOf(listing);
        if (!CanBuyCar(price) || (listing != null && listingIndex < 0))
            return;

        int previousNumber = car.number;
        car.number = career.cars[0].number;
        career.teamBalance -= price;
        career.schedule.carPurchases += price;
        career.cars.Add(car);
        if (listingIndex >= 0)
            career.usedCars.RemoveAt(listingIndex);
        try
        {
            careerSetup.SaveProgress(career);
        }
        catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
        {
            career.teamBalance += price;
            career.schedule.carPurchases -= price;
            career.cars.Remove(car);
            car.number = previousNumber;
            if (listingIndex >= 0)
                career.usedCars.Insert(listingIndex, listing);
            RefreshCarMarket("Could not save the purchase. " + ex.Message);
            return;
        }

        RefreshTeamBalance();
        RefreshTeamCars();
        RefreshSchedule();
        RefreshCarMarket($"Purchased chassis {car.chassisNumber} for {RaceFinances.Dollars(price)}.");
    }

    private void RefreshSchedule(bool selectNext = false)
    {
        var schedule = activeCareer.schedule;
        var races = schedule.races;
        var next = races?.FirstOrDefault(race => !race.completed);
        var headerRace = garageScreen.Q<Label>("teamNextRace");
        var nextTrack = next == null ? null : catalog.Entries.Find(entry => entry.Definition.id == next.trackId)?.Definition;
        string trackType = nextTrack switch
        {
            null => string.Empty,
            { majorStyle: "Road" } => " (Road Course)",
            { restrictorPlate: true } => " (Restrictor Plate)",
            { lengthMiles: < 1f } => " (Short Track)",
            _ => " (Intermediate)"
        };
        string trackWithType = next == null ? string.Empty : next.trackName + trackType;
        garageScreen.Q<Label>("nextRaceLabel").text = next == null
            ? "No upcoming races scheduled."
            : $"Next race  /  {next.EffectiveRaceLaps} laps at {trackWithType}";
        headerRace.text = next == null ? "No upcoming race" : $"Next race  /  {trackWithType}  /  {RaceDate(next)}";
        headerRace.tooltip = headerRace.text;
        if (selectNext || selectedEvent == null || races == null || !races.Contains(selectedEvent))
            selectedEvent = next ?? races?.LastOrDefault();

        garageScreen.Q<Label>("seasonLabel").text = $"{schedule.season} season";
        var rows = garageScreen.Q<ScrollView>("scheduleRows");
        Vector2 scroll = rows.scrollOffset;
        VisualElement rowToReveal = null;
        rows.Clear();
        if (races != null)
        {
            foreach (var race in races)
            {
                var row = ScheduleButton($"{RaceDate(race)}  /  {race.trackName}", race == selectedEvent, () =>
                {
                    selectedEvent = race;
                    RefreshSchedule();
                });
                row.EnableInClassList("schedule-race-completed", race.completed);
                rows.Add(row);
                if (selectNext && race == selectedEvent)
                    rowToReveal = row;
            }
        }
        if (rowToReveal != null)
        {
            rowToReveal.RegisterCallback<GeometryChangedEvent>(RevealSelectedRow);
        }
        else
        {
            rows.schedule.Execute(() => rows.scrollOffset = scroll);
        }

        void RevealSelectedRow(GeometryChangedEvent evt)
        {
            rowToReveal.UnregisterCallback<GeometryChangedEvent>(RevealSelectedRow);
            rows.schedule.Execute(() =>
            {
                if (rowToReveal.parent == rows.contentContainer)
                    rows.ScrollTo(rowToReveal);
            });
        }

        DisplayTeamStandings(schedule);
        var chosenCar = EventCar(selectedEvent);
        var carPicker = garageScreen.Q<Button>("eventCarPicker");
        carPicker.style.display = selectedEvent != null && !selectedEvent.completed ? DisplayStyle.Flex : DisplayStyle.None;
        carPicker.text = chosenCar == null ? "Choose Race Car" : $"Race car • Chassis # {chosenCar.chassisNumber}";

        var eventTrack = selectedEvent == null ? null : catalog.Entries.Find(track => track.Definition.id == selectedEvent.trackId);
        bool available = eventTrack != null;
        DisplayTrackRatings(eventTrack?.Definition.weights);
        UpdateScheduleTrackOutline(eventTrack?.Definition);
        bool eventCompleted = selectedEvent?.completed == true;
        startRaceButton.text = eventCompleted ? "View Results" : "Go To Event";
        bool canRunEvent = available && !eventCompleted && HasDriver(activeCareer) &&
            chosenCar != null;
        startRaceButton.SetEnabled(eventCompleted || canRunEvent);
        simulateEventButton.SetEnabled(canRunEvent);
        var details = garageScreen.Q<Label>("scheduleStatus");
        if (selectedEvent == null)
        {
            details.text = "No scheduled races available.";
        }
        else
        {
            string status = selectedEvent.completed ? "Completed" : !available ? "Track definition unavailable"
                : !HasDriver(activeCareer) ? "No driver assigned" : selectedEvent == next ? "Next race" : "Available";
            details.text = $"{selectedEvent.trackName}\n{RaceDate(selectedEvent)}\n{selectedEvent.EffectiveRaceLaps} laps  /  {status}";
            if (selectedEvent.completed)
                details.text += "\n" + (selectedEvent.playerSkipped ? "Team skipped this race" : chosenCar?.ChassisDisplayName ?? "Car not recorded");
            if (!eventCompleted && chosenCar == null)
                details.text += "\nChoose a race-ready chassis from the race car list.";
            if (selectedEvent.purse > 0)
                details.text += $"\nPurse  /  {RaceFinances.Dollars(selectedEvent.purse)}";
        }
        settings?.RestorePalette();
    }

    private void DisplayTeamStandings(SeasonSchedule schedule)
    {
        garageScreen.Q<Label>("teamStandingsHeading").text = $"{schedule.season} {activeCareer.seriesName} Points Standings";
        var rows = garageScreen.Q<ScrollView>("teamStandingsRows");
        var scroll = rows.scrollOffset;
        rows.Clear();

        var standings = GetStandings(schedule);
        int rank = 1;
        for (int i = 0; i < standings.Count; i++)
        {
            var car = standings[i];
            if (i > 0 && car.Value != standings[i - 1].Value)
                rank = i + 1;
            AddStandingsRow(rows, rank.ToString(), $"#{car.Key.Number}", car.Key.Team, car.Value.ToString(), car.Key.IsPlayer);
        }
        rows.schedule.Execute(() => rows.scrollOffset = scroll);
    }

    private List<KeyValuePair<(int Number, string Team, bool IsPlayer), long>> GetStandings(SeasonSchedule schedule)
    {
        string playerTeam = activeCareer.teamName;
        var roster = TrackPreview.CreateTimingEntries(activeCareer.cars[0], activeCareer);
        var rivalNumbers = roster.Skip(1).ToDictionary(car => (car.teamName, car.driverName), car => car.carNumber);
        var totals = roster.Skip(1).ToDictionary(car => (Number: car.carNumber, Team: car.teamName, IsPlayer: false), _ => 0L);
        foreach (var car in activeCareer.cars)
            totals[(car.number, playerTeam, true)] = 0;

        if (schedule.races != null)
        {
            foreach (var race in schedule.races.Where(race => race.completed))
            {
                var results = race.sessions?.Find(session => session.kind == RaceSessionKind.Race)?.results;
                if (results == null)
                    continue;

                foreach (var result in results)
                {
                    bool isPlayer = !race.playerSkipped && (race.carNumber > 0 ? result.carNumber == race.carNumber
                        : result.teamName == playerTeam && result.driverName == activeCareer.playerName);
                    int number = result.carNumber;
                    if (!isPlayer && rivalNumbers.TryGetValue((result.teamName, result.driverName), out int rivalNumber))
                        number = rivalNumber;

                    var car = (Number: number, Team: isPlayer ? playerTeam : result.teamName, IsPlayer: isPlayer);
                    if (car.Number > 0 && !string.IsNullOrWhiteSpace(car.Team))
                        totals[car] = (totals.TryGetValue(car, out long points) ? points : 0) + result.pointsEarned;
                }
            }
        }

        return totals.OrderByDescending(car => car.Value)
            .ThenBy(car => car.Key.Number).ThenBy(car => car.Key.Team, System.StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void AddStandingsRow(VisualElement rows, string position, string carNumber, string team, string points,
        bool player)
    {
        var row = new VisualElement();
        row.AddToClassList("results-grid-row");
        row.AddToClassList("standings-row");
        row.EnableInClassList("standings-player", player);
        AddCell(position, "standings-position");
        AddCell(carNumber, "standings-car-number");
        AddCell(team, "results-grid-name");
        AddCell(points, "standings-points");
        rows.Add(row);

        void AddCell(string text, string column)
        {
            var cell = new Label(text);
            cell.AddToClassList("garage-detail");
            cell.AddToClassList("results-grid-cell");
            cell.AddToClassList(column);
            row.Add(cell);
        }
    }

    private void UpdateScheduleTrackOutline(TrackDefinition track)
    {
        scheduleTrackOutline.style.display = track == null ? DisplayStyle.None : DisplayStyle.Flex;
        if (track == null)
            return;

        float lapLength = track.lengthMiles * TrackDefinition.MetersPerMile;
        var points = track.layoutPoints?.Length >= 3 ? track.layoutPoints : TrackUtilityGeometry.EditableOval(track);
        var layout = new TrackLayout(points, lapLength, track.widthMeters);
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = -min;
        for (int i = 0; i < scheduleOutlinePoints.Length; i++)
        {
            Vector3 position = layout.Sample(lapLength * i / scheduleOutlinePoints.Length, out _, out _);
            var point = new Vector2(position.x, -position.z);
            scheduleOutlinePoints[i] = point;
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }

        scheduleOutlineBounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        scheduleTrackOutline.MarkDirtyRepaint();
    }

    private void DrawScheduleTrackOutline(MeshGenerationContext context)
    {
        Rect viewport = scheduleTrackOutline.contentRect;
        const float padding = 6f;
        if (viewport.width <= padding * 2f || viewport.height <= padding * 2f ||
            scheduleOutlineBounds.size.sqrMagnitude <= 0f)
            return;

        float scale = Mathf.Min(
            (viewport.width - padding * 2f) / Mathf.Max(1f, scheduleOutlineBounds.width),
            (viewport.height - padding * 2f) / Mathf.Max(1f, scheduleOutlineBounds.height));
        Vector2 offset = viewport.center - scheduleOutlineBounds.center * scale;
        var painter = context.painter2D;
        painter.strokeColor = scheduleTrackOutline.resolvedStyle.color;
        painter.lineWidth = 2.5f;
        painter.lineJoin = LineJoin.Round;
        painter.BeginPath();
        painter.MoveTo(scheduleOutlinePoints[0] * scale + offset);
        for (int i = 1; i < scheduleOutlinePoints.Length; i++)
            painter.LineTo(scheduleOutlinePoints[i] * scale + offset);
        painter.ClosePath();
        painter.Stroke();
    }

    private void DisplayTrackRatings(TrackWeights weights)
    {
        var chart = garageScreen.Q("scheduleTrackRatings");
        chart.Clear();
        chart.style.display = weights == null ? DisplayStyle.None : DisplayStyle.Flex;
        if (weights == null)
            return;

        AddRatingBar(chart, "Aero", weights.aerodynamics);
        AddRatingBar(chart, "Handling", weights.handling);
        AddRatingBar(chart, "Brakes", weights.brakes);
        AddRatingBar(chart, "Tire wear", weights.tireWear);
    }

    private static void AddRatingBar(VisualElement chart, string title, float value)
    {
        var column = new VisualElement { tooltip = $"{title} {value:F2} out of 1.00" };
        column.AddToClassList("rating-column");
        var number = new Label(value.ToString("F2"));
        number.AddToClassList("garage-detail");
        number.AddToClassList("rating-label");
        column.Add(number);
        var well = new VisualElement();
        well.AddToClassList("rating-well");
        var bar = new VisualElement();
        bar.AddToClassList("rating-fill");
        bar.style.height = Length.Percent(value * 100f);
        well.Add(bar);
        column.Add(well);
        var label = new Label(title);
        label.AddToClassList("garage-detail");
        label.AddToClassList("rating-label");
        column.Add(label);
        chart.Add(column);
    }

    private static bool CanEnterRace(TeamCar car, ScheduledRace race)
    {
        if (race == null || race.completed)
            return true;
        return RaceCarBlockReason(car, race) == null;
    }

    private static string RaceCarBlockReason(TeamCar car, ScheduledRace race)
    {
        if (car.HasRaceBlockingWear)
            return "Component wear at 95% or higher";

        bool repairing = car.repairs?.Any(repair =>
            !Career.TryReadDate(race.date, out var raceDate) ||
            !Career.TryReadDate(repair.completionDate, out var due) || due > raceDate) == true;
        return repairing ? "Repair due after race day" : null;
    }

    private TeamCar EventCar(ScheduledRace race)
    {
        if (race == null)
            return null;
        var car = string.IsNullOrEmpty(race.carChassisNumber)
            ? activeCareer.cars.Find(item => item.number == race.carNumber)
            : activeCareer.cars.Find(item => item.chassisNumber == race.carChassisNumber);
        if (race.completed)
            return car;
        car ??= activeCareer.cars[0];
        return CanEnterRace(car, race) ? car : null;
    }

    private void AssignEventCar(TeamCar car)
    {
        if (selectedEvent == null || selectedEvent.completed || !CanEnterRace(car, selectedEvent) ||
            EventCar(selectedEvent) == car)
            return;
        int previous = selectedEvent.carNumber;
        string previousChassis = selectedEvent.carChassisNumber;
        selectedEvent.carNumber = car.number;
        selectedEvent.carChassisNumber = car.chassisNumber;
        try
        {
            careerSetup.SaveProgress(activeCareer);
            RefreshSchedule();
        }
        catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
        {
            selectedEvent.carNumber = previous;
            selectedEvent.carChassisNumber = previousChassis;
            RefreshSchedule();
            garageScreen.Q<Label>("scheduleStatus").text = "Could not save the car selection. " + ex.Message;
        }
    }

    private static string RaceDate(ScheduledRace race) =>
        System.DateTime.TryParseExact(race.date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.ToString("ddd, MMM d, yyyy") : race.date;

    private static Button ScheduleButton(string title, bool selected, System.Action action)
    {
        var button = new Button(action) { text = title };
        button.AddToClassList("nav-button");
        button.AddToClassList("schedule-choice");
        button.EnableInClassList("nav-button-selected", selected);
        return button;
    }

    private void OpenSelectedEvent()
    {
        var race = selectedEvent;
        if (race == null)
            return;
        if (race.completed)
        {
            int resultsPage = System.Array.FindIndex(garagePages, page => page.name == "resultsPage");
            DisplayGaragePage(resultsPage, race);
            return;
        }

        var track = catalog.Entries.Find(entry => entry.Definition.id == race.trackId);
        if (!HasDriver(activeCareer) || EventCar(race) == null ||
            track == null)
            return;

        PrepareEventSessions(race, track.Definition);
        activeRace = race;
        garageScreen.style.display = DisplayStyle.None;
        raceEventView.Screen.style.display = DisplayStyle.Flex;
        raceEventView.Open(race, EventCar(race)?.ChassisDisplayName ?? "Car not recorded");
    }

    private static void PrepareEventSessions(ScheduledRace race, TrackDefinition track)
    {
        if (race.sessions == null || race.sessions.Count == 0)
            race.sessions = RaceSession.Create(1, track.NumberOfQualifyingLaps);

        foreach (var practice in race.sessions.Where(session => session.kind == RaceSessionKind.Practice && !session.Completed))
            practice.minutes = RaceSession.PracticeMinutes;

        var qualifying = race.sessions.Find(session => session.kind == RaceSessionKind.Qualifying);
        if (qualifying != null && !qualifying.Completed)
            qualifying.laps = track.NumberOfQualifyingLaps;

        var raceSession = race.sessions.Find(session => session.kind == RaceSessionKind.Race);
        if (raceSession != null && !raceSession.Completed)
            raceSession.laps = race.EffectiveRaceLaps;
    }

    private void SimulateEvent()
    {
        var race = selectedEvent;
        if (race == null || race.completed || !HasDriver(activeCareer))
            return;

        if (!TrySimulateEvent(race, null, out string error))
        {
            garageScreen.Q<Label>("scheduleStatus").text = error;
            return;
        }

        RefreshCareerDate();
        RefreshTeamBalance();
        RefreshSchedule();
        int resultsPage = System.Array.FindIndex(garagePages, page => page.name == "resultsPage");
        DisplayGaragePage(resultsPage, race);
    }

    private bool TrySimulateEvent(ScheduledRace race, System.DateTime? skipToDate, out string error)
    {
        var track = catalog.Entries.Find(entry => entry.Definition.id == race.trackId);
        if (track == null)
        {
            error = "The track definition is unavailable.";
            return false;
        }

        PrepareEventSessions(race, track.Definition);
        var random = new System.Random();
        bool skipped = skipToDate.HasValue;
        var car = skipped ? activeCareer.cars[0] : EventCar(race);
        if (car == null)
        {
            error = "Choose a car that is available on race day.";
            return false;
        }

        if (!PrepareRivalCarsForEvent(race, out error))
            return false;

        var results = new List<(RaceSession Session, List<SessionCarResult> Results)>();
        var qualifyingOrder = QualifyingOrder(race);
        foreach (var session in race.sessions.Where(session => !session.Completed))
        {
            var sessionResults = SimulateSession(race, session, track, car, random, skipped, qualifyingOrder);
            results.Add((session, sessionResults));
            if (session.kind == RaceSessionKind.Qualifying)
                qualifyingOrder = sessionResults.Select(result => result.carNumber).ToArray();
        }
        if (!results.Any(item => item.Session.kind == RaceSessionKind.Race))
        {
            error = "This event has no unfinished race session.";
            return false;
        }

        int previousCar = race.carNumber;
        string previousChassis = race.carChassisNumber;
        bool previouslySkipped = race.playerSkipped;
        race.playerSkipped = skipped;
        race.carNumber = skipped ? 0 : car.number;
        race.carChassisNumber = skipped ? string.Empty : car.chassisNumber;
        try
        {
            SaveSessionResults(race, results, skipToDate);
        }
        catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
        {
            race.playerSkipped = previouslySkipped;
            race.carNumber = previousCar;
            race.carChassisNumber = previousChassis;
            error = "Could not save the simulated event. " + ex.Message;
            return false;
        }

        error = null;
        return true;
    }

    private List<SessionCarResult> SimulateSession(ScheduledRace race, RaceSession session, TrackCatalog.Entry track, TeamCar car,
        System.Random random, bool rivalsOnly = false, int[] qualifyingOrder = null)
    {
        var entries = TrackPreview.CreateTimingEntries(car, activeCareer);
        if (rivalsOnly)
            entries = entries.Skip(1).ToArray();

        var simulation = new CarSimulationContext(track.Definition, race, session, entries, isInstant: true);
        return RaceSessionRun.Simulate(simulation, settings.SpeedModel, random, qualifyingOrder, includesPlayer: !rivalsOnly);
    }

    private static int[] QualifyingOrder(ScheduledRace race) =>
        race.sessions?.Find(session => session.kind == RaceSessionKind.Qualifying)?.qualifyingOrder;

    private void StartNextSession(bool simulate)
    {
        var session = activeRace?.sessions.FirstOrDefault(item => !item.Completed);
        if (session == null || activeRace.completed)
            return;
        var track = catalog.Entries.Find(entry => entry.Definition.id == activeRace.trackId);
        if (track == null)
        {
            raceEventView.ShowSchedule("The track definition is unavailable.");
            return;
        }

        var car = EventCar(activeRace);
        if (car == null)
        {
            raceEventView.ShowSchedule("Choose a car that is available on race day.");
            return;
        }

        if (!PrepareRivalCarsForEvent(activeRace, out string error))
        {
            raceEventView.ShowSchedule(error);
            return;
        }

        activeRace.carNumber = car.number;
        activeRace.carChassisNumber = car.chassisNumber;
        if (simulate)
        {
            CompleteSession(session, SimulateSession(activeRace, session, track, car, new System.Random(), qualifyingOrder: QualifyingOrder(activeRace)));
            return;
        }

        raceEventView.Screen.style.display = DisplayStyle.None;
        returnToGarageButton.text = "Leave Session";
        bool night = activeRace.trackId == TrackPreview.BristolTrackId &&
            activeCareer.schedule.races.TakeWhile(race => race != activeRace)
                .Count(race => race.trackId == activeRace.trackId) == 1;
        OpenTrack(track, true, activeRace.carNumber, night);
        if (trackPreview.Image == null)
        {
            ReturnToGarage();
            raceEventView.ShowSchedule("Could not start the session.");
            return;
        }
        if (settings.SimulationModel is TrackState001 && !trackPreview.HasSessionPitRoad)
        {
            ReturnToGarage();
            raceEventView.ShowSchedule("TrackState001 requires a pit road with an entry and exit for this session.");
            return;
        }

        activeSession = session;
        var simulation = new CarSimulationContext(track.Definition, activeRace, session,
            TrackPreview.CreateTimingEntries(car, activeCareer));
        trackPreview.BeginSession(simulation, settings.SimulationModel, settings.SpeedModel, settings.BehaviorModel,
            QualifyingOrder(activeRace), settings.UsePaceTruck);
        RefreshTrackTiming();
        sessionSpeedSelector.SetValueWithoutNotify(sessionSpeedSelector.choices[0]);
        sessionSpeedSelector.style.display = DisplayStyle.Flex;
        UpdateSessionLabel();
    }

    private void Update()
    {
        if (trackPreview != null && trackPreview.gameObject.activeSelf)
        {
            UpdateTrackFps();
            RefreshTrackTiming();
        }
        if (activeSession == null || trackPreview?.SessionRun == null)
            return;

        UpdateSessionLabel();
        if (trackPreview.SessionComplete)
        {
            var session = activeSession;
            if (developmentPractice)
            {
                ReturnToGarage();
                return;
            }
            var results = trackPreview.SessionRun.Results();
            ReturnToGarage();
            CompleteSession(session, results);
        }
    }

    private void UpdateTrackFps()
    {
        const double sampleSeconds = 0.5;
        fpsSampleFrames++;
        double now = Time.unscaledTimeAsDouble;
        double elapsed = now - fpsSampleStart;
        if (elapsed < sampleSeconds)
            return;

        trackFpsLabel.text = $"FPS {fpsSampleFrames / elapsed:F0}";
        fpsSampleStart = now;
        fpsSampleFrames = 0;
    }

    private void RefreshTrackTiming()
    {
        var timing = trackPreview?.TimingRun;
        if (timing == null)
            return;

        bool race = timing.IsRace;
        if (Time.unscaledTime < nextStandingsRefresh && timing.TimingRevision == displayedTimingRevision)
            return;
        nextStandingsRefresh = Time.unscaledTime + 0.15f;

        if (!race && timing.TimingRevision == displayedTimingRevision && timingCells.Count > 0)
        {
            foreach (var row in timingCells)
                UpdateTimingSpeed(row.Speed, (SessionCarResult)row.Row.userData);
            return;
        }

        displayedTimingRevision = timing.TimingRevision;
        trackScreen.Q<Label>("timingPositionHeader").style.display = race ? DisplayStyle.Flex : DisplayStyle.None;
        var heading = trackScreen.Q<Label>("timingValueHeader");
        heading.text = race ? "Gap" : "Fastest lap";
        heading.tooltip = race ? "Gap to the car ahead in seconds. Negative numbers are laps behind the leader." : null;

        int index = 0;
        if (race)
        {
            foreach (var standing in timing.RunningOrder())
            {
                string gap = standing.LapsBehind > 0 ? $"-{standing.LapsBehind}"
                    : float.IsNaN(standing.SecondsBehind) ? "—" : $"{standing.SecondsBehind:F2} s";
                SetTimingRow(index, standing.Car, (index + 1).ToString(), gap);
                index++;
            }
        }
        else
        {
            foreach (var result in timing.FastestLaps())
                SetTimingRow(index++, result, null, result.lapTimes.Count == 0 ? "—" : RaceEventView.LapTime(result.FastestLap));
        }
    }

    private void SetTimingRow(int index, SessionCarResult result, string position, string value)
    {
        if (index >= timingCells.Count)
        {
            var row = new VisualElement();
            row.AddToClassList("timing-row");
            row.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.button == 0 && evt.clickCount == 2 && row.userData is SessionCarResult car)
                    trackPreview?.SelectTimingCar(car);
            });
            var cells = (Row: row, Position: TimingCell(string.Empty, "timing-position"), Car: TimingCell(string.Empty, "timing-car"),
                Driver: TimingCell(string.Empty, "timing-driver"), Value: TimingCell(string.Empty, "timing-lap"),
                Speed: TimingCell(string.Empty, "timing-speed"));
            timingCells.Add(cells);
            row.Add(cells.Position);
            row.Add(cells.Car);
            row.Add(cells.Driver);
            row.Add(cells.Value);
            row.Add(cells.Speed);
            trackTimingRows.Add(row);
        }

        var cell = timingCells[index];
        cell.Row.userData = result;
        string driver = result.driverName?.Trim() ?? string.Empty;
        int separator = driver.LastIndexOf(' ');
        cell.Position.style.display = position == null ? DisplayStyle.None : DisplayStyle.Flex;
        cell.Position.text = position;
        cell.Car.text = result.carNumber.ToString();
        cell.Driver.text = separator >= 0 ? driver.Substring(separator + 1) : driver;
        cell.Driver.tooltip = driver;
        cell.Value.text = value;
        UpdateTimingSpeed(cell.Speed, result);
    }

    private static void UpdateTimingSpeed(Label label, SessionCarResult result)
    {
        const double metersPerSecondToMph = 3600.0 / TrackDefinition.MetersPerMile;
        label.text = double.IsNaN(result.InstantSpeed) ? "—" : (result.InstantSpeed * metersPerSecondToMph).ToString("F1");
    }

    private static Label TimingCell(string text, string className)
    {
        var label = new Label(text);
        label.AddToClassList(className);
        return label;
    }

    private void ChangeSessionSpeed(ChangeEvent<string> evt)
    {
        int index = sessionSpeedSelector.index;
        if (activeSession != null && index >= 0 && index < sessionSpeedSelector.choices.Count)
            trackPreview.SetSessionSpeed(evt.newValue == "Pause" ? 0 : 1 << index);
    }

    private void UpdateTrackFlags(SessionTrackState? state, bool oneToGo = false)
    {
        if (state == displayedTrackState && oneToGo == displayedOneToGo)
            return;

        displayedTrackState = state;
        displayedOneToGo = oneToGo;
        practiceFlags.style.display = state.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
        bool returning = state == SessionTrackState.ReturnToPits || state == SessionTrackState.Complete;
        blackPracticeFlag.style.display = returning ? DisplayStyle.Flex : DisplayStyle.None;
        bool stageCheckered = state == SessionTrackState.StageComplete;
        bool checkered = state == SessionTrackState.Checkered || stageCheckered;
        for (int row = 0; row < checkeredFlag.childCount; row++)
        {
            var stripe = checkeredFlag[row];
            for (int column = 0; column < stripe.childCount; column++)
                stripe[column].style.backgroundColor = (row + column) % 2 == 0 ? Color.white :
                    stageCheckered ? (Color)new Color32(66, 199, 100, 255) : Color.black;
        }
        checkeredFlag.style.display = checkered ? DisplayStyle.Flex : DisplayStyle.None;
        primaryPracticeFlag.style.display = checkered || state == SessionTrackState.Pacing ? DisplayStyle.None : DisplayStyle.Flex;
        if (state == SessionTrackState.TrackClosed)
        {
            primaryPracticeFlag.style.backgroundColor = (Color)new Color32(65, 143, 255, 255);
            practiceStateLabel.text = "Track Closed";
        }
        else if (state == SessionTrackState.Pacing)
        {
            practiceStateLabel.text = "Pace Laps";
        }
        else if (state == SessionTrackState.TrackOpen)
        {
            primaryPracticeFlag.style.backgroundColor = (Color)new Color32(66, 199, 100, 255);
            practiceStateLabel.text = "Track Open";
        }
        else if (state == SessionTrackState.WhiteFlag)
        {
            primaryPracticeFlag.style.backgroundColor = Color.white;
            practiceStateLabel.text = "One Lap to Go";
        }
        else if (checkered)
        {
            practiceStateLabel.text = stageCheckered ? "Stage Complete" :
                activeSession?.kind == RaceSessionKind.Race ? "Race Over" : "Checkered Flag · Return to Pits";
        }
        else if (state == SessionTrackState.Caution || state == SessionTrackState.StageEndingUnderCaution ||
            state == SessionTrackState.FinishUnderCaution)
        {
            primaryPracticeFlag.style.backgroundColor = Color.yellow;
            practiceStateLabel.text = state == SessionTrackState.FinishUnderCaution ? "Race Over Under Caution" : oneToGo ? "One To Go" : "Caution";
        }
        else if (returning)
        {
            primaryPracticeFlag.style.backgroundColor = (Color)new Color32(226, 66, 66, 255);
            practiceStateLabel.text = "Return to Pits";
        }
    }

    private void QueuePlayerPitStop()
    {
        int tires = 0;
        for (int i = 0; i < pitTires.Length; i++)
        {
            if (pitTires[i].value)
                tires |= 1 << i;
        }
        trackPreview?.RequestPlayerPit(refuelAtStop.value ? pitFuelAmount.value : 0f, tires, pitRepairAmount.value);
    }

    private void UpdatePlayerPitPanel(RaceSessionRun run)
    {
        var car = run.Vehicle(0);
        double fuel = run.Fuel(0);
        float damage = car?.Damage ?? 0f;
        playerCarStatus.text = $"Fuel {fuel:F1} / {SessionFuel.TankCapacity:F0} gal";
        pitFuelFill.style.width = Length.Percent((float)(fuel / SessionFuel.TankCapacity * 100));
        pitDamageLabel.text = $"Damage {damage:F0}%";
        pitDamageFill.style.width = Length.Percent(damage);
        pitFuelAmountLabel.text = pitFuelAmount.value >= SessionFuel.TankCapacity
            ? "Fill tank" : $"Add {pitFuelAmount.value:F1} gal";
        pitRepairAmountLabel.text = $"Repair up to {pitRepairAmount.value:F0}%";
        bool canRequest = trackPreview.CanRequestPlayerPit;
        requestPitStop.SetEnabled(canRequest);
        refuelAtStop.SetEnabled(canRequest);
        pitFuelAmount.SetEnabled(canRequest && refuelAtStop.value);
        pitRepairAmount.SetEnabled(canRequest);
        playerPitStatus.text = trackPreview.PlayerPitStatus;
        for (int i = 0; i < pitTires.Length; i++)
        {
            float wear = Mathf.Clamp01(car?.GetTire(i)?.wear ?? 0f);
            pitTireFill[i].style.height = Length.Percent((1f - wear) * 100f);
            pitTireWear[i].text = wear.ToString("P0");
            pitTires[i].SetEnabled(canRequest);
        }
    }

    private void SaveTrackSnapshot()
    {
        if (trackPreview?.CanSaveSnapshot != true)
            return;

        try
        {
            string path = trackPreview.SaveSnapshot();
            snapshotStatus.text = "Snapshot saved";
            snapshotStatus.tooltip = path;
            Debug.Log($"Track snapshot saved to {path}");
        }
        catch (System.Exception error) when (error is IOException || error is System.UnauthorizedAccessException)
        {
            snapshotStatus.text = "Snapshot could not be saved";
            snapshotStatus.tooltip = error.Message;
            Debug.LogWarning($"Track snapshot save failed: {error.Message}");
        }
    }

    private void RequestTrackCaution()
    {
        trackPreview.RequestCaution();
        UpdateSessionLabel();
    }

    private void UpdateSessionLabel()
    {
        cautionButton.SetEnabled(trackPreview.CanRequestCaution);
        snapshotButton.SetEnabled(trackPreview.CanSaveSnapshot);
        var run = trackPreview.SessionRun;
        UpdatePlayerPitPanel(run);
        var state = trackPreview.TrackState;
        UpdateTrackFlags(state, trackPreview.OneToGoBeforeGreen);
        string luckyDog = trackPreview.LuckyDogMessage;
        luckyDogLabel.text = luckyDog;
        luckyDogLabel.style.display = luckyDog.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
        if (trackPreview.RaceStartCountdown > 0)
        {
            raceProgressLabel.text = $"{activeSession.name}  /  Start in {trackPreview.RaceStartCountdown}";
            return;
        }
        if (state == SessionTrackState.Pacing)
        {
            raceProgressLabel.text = activeSession.name;
            return;
        }
        if (state == SessionTrackState.Checkered && !run.IsRace)
        {
            raceProgressLabel.text = $"{activeSession.name}  /  Best lap {RaceEventView.LapTime(run.PlayerBestLap)}";
            return;
        }
        if (state == SessionTrackState.TrackClosed || state == SessionTrackState.ReturnToPits ||
            state == SessionTrackState.Complete)
        {
            raceProgressLabel.text = activeSession.name;
            return;
        }
        if (activeSession.minutes > 0)
        {
            var remaining = System.TimeSpan.FromSeconds(Mathf.CeilToInt(run.Remaining));
            raceProgressLabel.text = $"{activeSession.name}  /  {remaining:mm\\:ss} remaining";
        }
        else
        {
            string stage = run.IsRace ? $"  /  Stage {run.CurrentStage} of {RaceSession.TestBonusStages + 1}" : string.Empty;
            string pitStatus = trackPreview.PitRoadOpen is bool open ? (open ? "  /  Pits Open" : "  /  Pits Closed") : string.Empty;
            int laps = run.IsRace ? run.LeaderLaps : run.PlayerLaps;
            string progress = run.FinishSuspended ? $"{laps} laps · Overtime pending" : $"{laps} of {run.TargetLaps} laps";
            string stageProgress = run.IsRace
                ? $"  /  Stage lap {laps - run.StageStartLap} of {run.StageEnd - run.StageStartLap}" : string.Empty;
            raceProgressLabel.text = $"{activeSession.name}{stage}  /  {progress}{stageProgress}{pitStatus}";
        }
    }

    private void CompleteSession(RaceSession session, List<SessionCarResult> results)
    {
        try
        {
            SaveSessionResults(activeRace, new[] { (Session: session, Results: results) });
        }
        catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
        {
            raceEventView.ShowSchedule("Could not save the session result. " + ex.Message);
            return;
        }

        RefreshTeamBalance();
        raceEventView.ShowResults(session);
    }

    private void SaveSessionResults(ScheduledRace race,
        IReadOnlyList<(RaceSession Session, List<SessionCarResult> Results)> results, System.DateTime? skipToDate = null)
    {
        var season = activeCareer.schedule;
        var previousSessions = results.Select(item => (item.Session, item.Session.results,
            item.Session.temporaryResults, item.Session.completed, item.Session.practiceWear, item.Session.qualifyingOrder,
            item.Session.playerContacts, item.Session.playerReport)).ToArray();
        int inboxCount = activeCareer.inbox?.Count ?? 0;
        var wearBefore = new Dictionary<(TeamCar Car, CarComponent Component), float>();
        var track = catalog.Entries.Find(entry => entry.Definition.id == race.trackId)?.Definition;
        var wearRandom = new System.Random();
        long previousBalance = activeCareer.teamBalance;
        long previousPayroll = season.payrollPaid;
        long previousSponsorIncome = season.sponsorIncome;
        long previousRaceSponsorIncome = race.sponsorIncome;
        var previousSponsorPayments = race.sponsorPayments;
        var previousRaceExpenses = race.expenses;
        var previousContracts = activeCareer.sponsors;
        var previousOffers = activeCareer.sponsorOffers;
        string previousDate = activeCareer.currentDate;
        bool previouslyCompleted = race.completed;
        var previousArchives = activeCareer.completedSeasons;
        bool archivedSeason = false;
        System.Action undoRepairs = null;
        try
        {
            foreach (var item in results)
            {
                var session = item.Session;
                var player = race.playerSkipped ? null : item.Results.Find(result => result.carNumber == race.carNumber && result.car != null);
                var playerWear = player == null ? null : TeamCar.Components.Select(component => (float)player.car.ComponentWear(component)).ToArray();
                if (!session.Completed && track != null && session.kind != RaceSessionKind.Qualifying)
                {
                    if (session.kind == RaceSessionKind.Practice)
                        session.practiceWear = new List<PracticeWear>();
                    foreach (var car in item.Results.Select(result => result.car).Where(car => car != null).Distinct())
                    {
                        float practiceAmount = 0f;
                        if (session.kind == RaceSessionKind.Practice)
                        {
                            practiceAmount = (float)wearRandom.NextDouble() * 0.05f;
                            session.practiceWear.Add(new PracticeWear { chassis = car.chassisNumber, amount = practiceAmount });
                        }
                        else
                        {
                            practiceAmount = race.sessions.Where(previous => previous.kind == RaceSessionKind.Practice)
                                .Sum(previous => previous.practiceWear?.Find(wear => wear.chassis == car.chassisNumber)?.amount ?? 0f);
                        }

                        foreach (var component in TeamCar.Components)
                        {
                            if (component == CarComponent.Aerodynamics)
                                continue;
                            bool minimalBrakes = component == CarComponent.Brakes && track.majorStyle != "Road" &&
                                track.lengthMiles >= 1.5f;
                            float amount = session.kind == RaceSessionKind.Practice
                                ? practiceAmount * (minimalBrakes ? 0.2f : 1f)
                                : Mathf.Min(SessionWear.RaceAmount(component, track, wearRandom), 0.55f - practiceAmount);
                            ref float wear = ref car.ComponentWear(component);
                            wearBefore.TryAdd((car, component), wear);
                            wear = Mathf.Clamp01(wear + amount);
                        }
                    }
                }
                session.completed = true;
                if (player != null)
                    session.playerReport = new PlayerSessionReport(player, item.Results.Count, playerWear);
                session.playerContacts = item.Results.FirstOrDefault(result => result.contacts?.Count > 0)?.contacts;
                if (session.kind != RaceSessionKind.Race)
                {
                    session.temporaryResults = item.Results;
                    if (session.kind == RaceSessionKind.Qualifying)
                        session.qualifyingOrder = item.Results.Select(result => result.carNumber).ToArray();

                    continue;
                }

                session.results = item.Results;
                var payouts = RaceFinances.Payouts(race.purse, session.results.Count);
                for (int i = 0; i < session.results.Count; i++)
                    session.results[i].winnings = payouts[i];
                if (!race.playerSkipped)
                    activeCareer.teamBalance += session.results.Find(result => result.carNumber == race.carNumber)?.winnings ?? 0;
                race.completed = true;
            }

            if (!previouslyCompleted && race.completed)
            {
                CrewChiefMail.SendRecap(activeCareer, race);
                BroadcastCoverage.SendRecaps(activeCareer, race);
                long annualPayroll = activeCareer.AnnualPayroll;
                int completedRaces = season.races.Count(item => item.completed);
                long installment = RaceFinances.PayrollInstallment(annualPayroll, season.races.Count, completedRaces);
                activeCareer.teamBalance -= installment;
                season.payrollPaid += installment;
                if (!race.playerSkipped)
                {
                    race.expenses = RaceExpenses.Generate(new System.Random());
                    activeCareer.teamBalance -= race.expenses.Total;
                }
                int finish = race.playerSkipped ? 0 : race.sessions.Find(session => session.kind == RaceSessionKind.Race)
                    .results.Find(result => result.carNumber == race.carNumber)?.position ?? 0;
                activeCareer.sponsors = Sponsorships.Settle(previousContracts ?? Enumerable.Empty<SponsorContract>(),
                    finish, out long income, out var payments);
                race.sponsorPayments = payments;
                race.sponsorIncome = income;
                season.sponsorIncome += income;
                activeCareer.teamBalance += income;
            }

            var postRaceDate = skipToDate;
            if (!previouslyCompleted && race.completed && !postRaceDate.HasValue &&
                Career.TryReadDate(race.date, out var raceDate) && raceDate < System.DateTime.MaxValue.Date)
                postRaceDate = raceDate.AddDays(1);

            if (Career.TryReadDate(activeCareer.currentDate, out var currentDate) &&
                postRaceDate.HasValue && postRaceDate.Value < currentDate)
                postRaceDate = currentDate;

            if (postRaceDate.HasValue)
                activeCareer.SetDate(postRaceDate.Value);

            if (!previouslyCompleted && race.completed && season.races.All(item => item.completed))
            {
                catalog.Refresh();
                var last = season.races[season.races.Count - 1];
                System.DateTime? previous = System.DateTime.TryParseExact(last.date, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : (System.DateTime?)null;
                activeCareer.schedule = careerSetup.GenerateSeason(season.season + 1, previous);
                activeCareer.SetSeasonStartDate();
                if (postRaceDate.HasValue && Career.TryReadDate(activeCareer.currentDate, out var seasonStart) &&
                    seasonStart < postRaceDate.Value)
                    activeCareer.SetDate(postRaceDate.Value);
                activeCareer.completedSeasons ??= new List<SeasonSchedule>();
                activeCareer.completedSeasons.Add(season);
                archivedSeason = true;
            }

            if (!previouslyCompleted && race.completed)
            {
                undoRepairs = UpdateCareerRepairs();
                var standings = GetStandings(season);
                int playerIndex = standings.FindIndex(car => car.Key.IsPlayer);
                int rank = playerIndex >= 0 && standings[playerIndex].Value > 0
                    ? standings.FindIndex(car => car.Value == standings[playerIndex].Value) + 1
                    : standings.Count;
                var upcomingRaces = activeCareer.schedule.races;
                activeCareer.sponsorOffers = Sponsorships.CreateOffers(activeCareer.sponsors, rank, standings.Count,
                    upcomingRaces.Count(item => !item.completed), upcomingRaces.Count, new System.Random());
            }

            CrewChiefMail.SendPreview(activeCareer, catalog, RaceCarBlockReason);
            careerSetup.SaveProgress(activeCareer);
        }
        catch
        {
            foreach (var previous in previousSessions)
            {
                previous.Session.results = previous.results;
                previous.Session.temporaryResults = previous.temporaryResults;
                previous.Session.completed = previous.completed;
                previous.Session.practiceWear = previous.practiceWear;
                previous.Session.qualifyingOrder = previous.qualifyingOrder;
                previous.Session.playerContacts = previous.playerContacts;
                previous.Session.playerReport = previous.playerReport;
            }
            activeCareer.teamBalance = previousBalance;
            season.payrollPaid = previousPayroll;
            season.sponsorIncome = previousSponsorIncome;
            race.sponsorIncome = previousRaceSponsorIncome;
            race.sponsorPayments = previousSponsorPayments;
            race.expenses = previousRaceExpenses;
            activeCareer.sponsors = previousContracts;
            activeCareer.sponsorOffers = previousOffers;
            undoRepairs?.Invoke();
            foreach (var entry in wearBefore)
                entry.Key.Car.ComponentWear(entry.Key.Component) = entry.Value;
            activeCareer.currentDate = previousDate;
            CrewChiefMail.UndoNewMessages(activeCareer, inboxCount);
            race.completed = previouslyCompleted;
            activeCareer.schedule = season;
            if (archivedSeason)
                activeCareer.completedSeasons.Remove(season);
            activeCareer.completedSeasons = previousArchives;
            throw;
        }
    }

    private void LeaveRaceEvent()
    {
        activeRace = null;
        raceEventView.Screen.style.display = DisplayStyle.None;
        garageScreen.style.display = DisplayStyle.Flex;
        RefreshCareerDate();
        RefreshSchedule();
        DisplayGaragePage(0);
        garageTabs[0].Focus();
    }

    private string[] ActiveSponsorNames()
    {
        var names = new string[Sponsorships.Slots];
        foreach (var contract in activeCareer?.sponsors ?? Enumerable.Empty<SponsorContract>())
            names[contract.slot] = Sponsorships.CompanyName(contract.offer.companyId);
        return names;
    }

    private void OpenTrack(TrackCatalog.Entry track, bool restart, int carNumber, bool night = false)
    {
        if (track == null)
            return;

        if (restart || trackPreview == null || trackPreview.SessionRun != null || previewTrack != track || previewCarNumber != carNumber)
        {
            if (trackPreview != null)
            {
                trackPreview.gameObject.SetActive(false);
                Destroy(trackPreview.gameObject);
            }
            raceViewport.style.backgroundImage = StyleKeyword.None;
            var preview = new GameObject(track.Definition.name + " Preview");
            preview.transform.SetParent(transform, false);
            displayedTimingRevision = -1;
            nextStandingsRefresh = 0f;
            timingCells.Clear();
            trackTimingRows.Clear();
            trackPreview = preview.AddComponent<TrackPreview>();
            trackPreview.Initialize(track.Definition, carNumber, ActiveSponsorNames(), EventCar(activeRace), night);
            previewTrack = track;
            previewCarNumber = carNumber;
            if (trackPreview.Image != null)
                raceViewport.style.backgroundImage = Background.FromRenderTexture(trackPreview.Image);
        }

        trackScreen.Q<Label>("selectedTrackName").text = $"03  /  {track.Definition.name.ToUpperInvariant()}";
        trackScreen.Q<Label>("trackPlayerCarLabel").text = $"NO. {carNumber}";
        trackPreview.gameObject.SetActive(true);
        garageScreen.style.display = DisplayStyle.None;
        fpsSampleStart = Time.unscaledTimeAsDouble;
        fpsSampleFrames = 0;
        trackFpsLabel.text = "FPS --";
        trackScreen.style.display = DisplayStyle.Flex;
        returnToGarageButton.Focus();
    }

    private Rect RaceImageRect()
    {
        if (trackPreview == null || trackPreview.Image == null)
            return Rect.zero;

        var viewport = raceViewport.contentRect;
        float aspect = (float)trackPreview.Image.width / trackPreview.Image.height;
        float width = Mathf.Min(viewport.width, viewport.height * aspect);
        return new Rect(0f, 0f, width, width / aspect) { center = viewport.center };
    }

    private void ZoomTrack(WheelEvent evt)
    {
        var imageRect = RaceImageRect();
        if (!evt.ctrlKey || !imageRect.Contains(evt.localMousePosition) || Mathf.Approximately(evt.delta.y, 0f))
            return;

        var viewportPoint = ToRaceViewport(imageRect, evt.localMousePosition);
        trackPreview.Zoom(evt.delta.y, viewportPoint * 2f - Vector2.one);
        evt.StopPropagation();
    }

    private void BeginTrackPointer(PointerDownEvent evt)
    {
        if (!RaceImageRect().Contains(evt.localPosition))
            return;

        if (evt.button == 2)
        {
            ResetTrackCamera();
            evt.StopPropagation();
            return;
        }

        if (dragPointerId != -1 || (evt.button != 0 && (evt.button != 1 || !evt.shiftKey)))
            return;

        dragPointerId = evt.pointerId;
        trackDragButton = evt.button;
        rotateTrackDrag = evt.button == 0 && evt.shiftKey;
        dragStart = evt.localPosition;
        previousPointerPosition = dragStart;
        isDragging = false;
        raceViewport.CapturePointer(dragPointerId);
        evt.StopPropagation();
    }

    private void MoveTrackPointer(PointerMoveEvent evt)
    {
        if (evt.pointerId != dragPointerId)
            return;

        Vector2 position = evt.localPosition;
        if (!isDragging && (position - dragStart).sqrMagnitude < 16f)
            return;

        isDragging = true;
        var imageRect = RaceImageRect();
        if (imageRect.height > 0f)
        {
            Vector2 delta = (position - previousPointerPosition) / imageRect.height;
            if (rotateTrackDrag)
                trackPreview.Rotate(delta.x);
            else if (trackDragButton == 0)
                trackPreview.Pan(delta);
            else if (evt.shiftKey)
                trackPreview.Tilt(delta.y);
        }
        previousPointerPosition = position;
        evt.StopPropagation();
    }

    private void EndTrackPointer(PointerUpEvent evt)
    {
        if (evt.pointerId != dragPointerId || evt.button != trackDragButton)
            return;

        var imageRect = RaceImageRect();
        Vector2 position = evt.localPosition;
        if (trackDragButton == 0 && !rotateTrackDrag && !isDragging && (position - dragStart).sqrMagnitude < 16f && imageRect.Contains(position))
            trackPreview.SelectCar(ToRaceViewport(imageRect, position));

        ReleaseTrackPointer();
        evt.StopPropagation();
    }

    private void LoseTrackPointer(PointerCaptureOutEvent evt)
    {
        if (evt.pointerId == dragPointerId)
            dragPointerId = -1;
    }

    private void ReleaseTrackPointer()
    {
        if (dragPointerId == -1)
            return;

        int pointerId = dragPointerId;
        dragPointerId = -1;
        raceViewport.ReleasePointer(pointerId);
    }

    private static Vector2 ToRaceViewport(Rect imageRect, Vector2 position)
    {
        return new Vector2((position.x - imageRect.xMin) / imageRect.width,
            (imageRect.yMax - position.y) / imageRect.height);
    }

    private void ResetTrackCamera()
    {
        ReleaseTrackPointer();
        trackPreview?.ResetView();
    }

    private void ReturnToGarage()
    {
        ReleaseTrackPointer();
        trackPreview.gameObject.SetActive(false);
        activeSession = null;
        UpdateTrackFlags(null);
        snapshotStatus.text = string.Empty;
        snapshotStatus.tooltip = null;
        trackScreen.style.display = DisplayStyle.None;
        if (developmentPractice)
        {
            developmentPractice = false;
            ShowDevelopmentTrackPicker();
            return;
        }
        if (activeRace != null)
        {
            raceEventView.Screen.style.display = DisplayStyle.Flex;
            raceEventView.ShowSchedule();
        }
        else
        {
            garageScreen.style.display = DisplayStyle.Flex;
        }
    }

    private static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
