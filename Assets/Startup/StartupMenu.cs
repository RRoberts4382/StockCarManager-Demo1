using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class StartupMenu : MonoBehaviour
{
    private CarManufacturer[] chassisManufacturers = System.Array.Empty<CarManufacturer>();
    private static readonly ChassisType[] NewChassisTypes =
        (ChassisType[])System.Enum.GetValues(typeof(ChassisType));
    private TeamCar selectedCarsChassis;
    private TeamCar newChassisDraft;
    private bool newManufacturerSelected;
    private bool newChassisTypeSelected;
    private int usedCarSort;
    private static readonly string[] UsedCarSortLabels =
        { "Price: Low to High", "Price: High to Low", "Condition: Best First", "Condition: Worst First" };
    private CarManufacturer? usedManufacturerFilter;
    private ChassisType? usedTypeFilter;
    private long? usedMaxPriceFilter;
    private const string TrackCameraAnglePreference = "StockCarManager.TrackCameraAngle";
    private const double CameraCycleIntervalSeconds = 8d;
    private const long CameraMenuHoldMs = 350;
    private static readonly TrackPreview.CameraAngle[] TrackCameraChoices =
    {
        TrackPreview.CameraAngle.Player, TrackPreview.CameraAngle.Chase, TrackPreview.CameraAngle.RearChase,
        TrackPreview.CameraAngle.Hood, TrackPreview.CameraAngle.InCar, TrackPreview.CameraAngle.TV,
        TrackPreview.CameraAngle.Leader, TrackPreview.CameraAngle.Spotter, TrackPreview.CameraAngle.PitBox, TrackPreview.CameraAngle.Cycle
    };
    private PanelSettings panel;
    private GameSettings settings;
    private Coroutine displayChange;
    private VisualElement settingsReturnScreen;
    private Button settingsReturnButton;
    private Button loadCareerButton;
    private Button continueCareerButton;
    private ScrollView savedCareerList;
    private VisualElement careerPickerDialog;
    private Button newCareerButton;
    private CareerSetup careerSetup;
    private Career activeCareer;
    private readonly TeamMailView teamMail = new TeamMailView();
    private ScheduledRace activeRace;
    private RaceSession activeSession;
    private bool sessionInGarage;
    private bool garageVisitPending;
    private bool garageCarParked;
    private double trackFpsWarmupUntil;
    private bool developmentPractice;
    private Career developmentPracticeCareer;
    private VisualElement developmentTrackPicker;
    private const string DevelopmentTrackPreference = "SCM.DevelopmentPractice.TrackId";
    private const string DevelopmentCarPreference = "SCM.DevelopmentPractice.CarModel";
    private List<TrackCatalog.Entry> developmentTracks;
    private List<(string Model, string Name)> developmentCars;
    private RaceEventView raceEventView;
    private ScheduledRace selectedEvent;
    private Button addTeamCarButton;
    private CarComponent? maintenanceComponent;
    private Employee maintenanceEmployee;
    private readonly Dictionary<VisualElement, bool> maintenanceUnderlayStates = new();
    private bool showingWorkInProgress;
    private readonly CarThumbnails carThumbnails = new CarThumbnails();
    private readonly CarThumbnails largeCarPreviews = CarThumbnails.CreateLargePreview();
    private readonly ShopCarPicker shopCarPicker = new ShopCarPicker();
    private readonly TeamShopPreview teamShopPreview = new TeamShopPreview();
    private readonly ScheduleHaulerBackground scheduleHauler = new ScheduleHaulerBackground();
    private readonly TeamShopExteriorBackground teamShopExterior = new TeamShopExteriorBackground();
    private const int GaragePhotoBlurPixels = 12;
    private RenderTexture financesBackground;
    private RenderTexture carsBackground;
    private Texture2D teamColorDesign;
    private RenderTexture teamColorBackground;
    private string teamColorBackgroundKey;
    private const int ShopBackgroundBlurPixels = 8;
    private System.Action pendingDismissal;
    private readonly EmployeePortraits employeePortraits = new EmployeePortraits();
    private static readonly EmployeeRole[] EmployeeRoles =
        System.Enum.GetValues(typeof(EmployeeRole)).Cast<EmployeeRole>()
            .Where(role => role != EmployeeRole.Driver).ToArray();
    private int previewCarNumber;
    private Button startRaceButton;
    private Button simulateEventButton;
    private TrackOutlineView scheduleTrackOutline;
    private Label raceProgressLabel;
    private Label trackWeatherLabel;
    private Button trackSettingsButton;
    private Button trackGrandstandsButton;
    private Button trackPracticeSkyButton;
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
    private RaceSessionRun pressurePanelRun;
    private readonly FloatField[] pitPressures = new FloatField[4];
    private readonly Toggle[] pitTires = new Toggle[4];
    private readonly Label[] pitTireWear = new Label[4];
    private readonly VisualElement[] pitTireFill = new VisualElement[4];
    private static readonly int[] SessionPlaybackSpeeds = { 1, 2, 4, 8, 16, 200 };
    private int sessionPlaybackSpeed = 1;
    private bool sessionPaused;
    private System.Action closeTrackMsaaPicker;
    private Button playSessionButton;
    private Button playbackSpeedButton;
    private Button pauseSessionButton;
    private Button requestCautionButton;
    private VisualElement throttleDisplay, brakeDisplay, fuelDisplay;
    private VisualElement throttleFill, brakeFill, fuelFill;
    private Label drivingInputMph, drivingInputRpm, drivingInputGear;
    private bool trackCollisionsEnabled;
    private bool trafficInteractionsEnabled;
    private VisualElement practiceFlags;
    private VisualElement primaryPracticeFlag;
    private VisualElement blackPracticeFlag;
    private Label practiceStateLabel;
    private Label luckyDogLabel;
    private VisualElement checkeredFlag;
    private SessionTrackState? displayedTrackState;
    private bool displayedOneToGo;
    private ScrollView trackTimingRows;
    private VisualElement trackStandings;
    private Button toggleTrackStandingsButton;
    private Label scoringSession, scoringSessionRemaining, scoringRaceLap, scoringRaceRemaining;
    private VisualElement scoringRaceLine, scoringRaceProgress, scoringFlag;
    private RaceSessionRun scoringProgressRun;
    private int scoringProgressLaps, scoringProgressTarget;
    private SessionTrackState? scoringFlagState;
    private static readonly Color ScoringGreen = new Color32(66, 199, 100, 255);
    private static readonly Color ScoringYellow = new Color32(255, 215, 53, 255);
    private Button pitSettingsButton;
    private Button cycleTrackCameraButton;
    private Label trackCameraAngle;
    private TrackPreview.CameraAngle trackCameraSelection;
    private double nextTrackCameraCut;
    private IVisualElementScheduledItem cameraMenuHold;
    private const long TrackIconTooltipDelayMs = 350;
    private Label trackIconTooltip;
    private VisualElement hoveredTrackIcon;
    private Vector2 trackIconHoverPosition;
    private IVisualElementScheduledItem pendingTrackIconTooltip;
    private VisualElement trackPitSettings;
    private bool trackStandingsExpanded = true;
    private int displayedTimingRevision = -1;
    private float nextStandingsRefresh;
    private readonly Dictionary<SessionCarResult, string> scoringDriverNames = new Dictionary<SessionCarResult, string>();
    private float scoringStandingsWidth;
    private const float ScoringPositionArrowDuration = 1.5f;
    private readonly Dictionary<SessionCarResult, (int Position, int Direction, float Until)> scoringPositions =
        new Dictionary<SessionCarResult, (int, int, float)>();
    private readonly List<(VisualElement Row, Label Position, Label Car, Label Driver, Label Gap)> timingCells =
        new List<(VisualElement, Label, Label, Label, Label)>();
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
    private Label playerTrackLaps;
    private double fpsSampleStart;
    private int fpsSampleFrames;
    private VisualElement raceViewport;
    private VisualElement trackCockpit, cockpitMirrorImage;
    private CockpitDashboard cockpitDashboard;
    private CockpitSteeringWheel cockpitSteeringWheel;
    private CarTypeDefinition cockpitCarType;
    private TrackSessionMap trackSessionMap;
    private Button returnToGarageButton;
    private Button visitGarageButton;
    private TrackPreview trackPreview;
    private TrackCatalog catalog;
    private TrackCatalog.Entry previewTrack;
    private Button resetCameraButton;
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
        garageLayout.CloneTree(root);
        trackLayout.CloneTree(root);
        titleScreen = root.Q("titleScreen");
        BindTitleIcons(titleScreen);
        developmentTrackPicker = root.Q("developmentTrackPicker");
        SettingsFormStyle.Apply(developmentTrackPicker);
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
        var practiceCar = developmentTrackPicker.Q<DropdownField>("developmentCars");
        var startPractice = developmentTrackPicker.Q<Button>("startDevelopmentPractice");
        var startTestRace = developmentTrackPicker.Q<Button>("startDevelopmentRace");
        practiceTrack.RegisterValueChangedCallback(_ =>
        {
            int index = practiceTrack.index;
            if (index < 0 || index >= developmentTracks.Count)
                return;

            PlayerPrefs.SetInt(DevelopmentTrackPreference, developmentTracks[index].Definition.id);
            PlayerPrefs.Save();
        });
        startPractice.clicked += () => StartSelectedDevelopmentSession(RaceSessionKind.Practice);
        startTestRace.clicked += () => StartSelectedDevelopmentSession(RaceSessionKind.Race);

        void StartSelectedDevelopmentSession(RaceSessionKind kind)
        {
            int trackIndex = practiceTrack.index;
            int carIndex = practiceCar.index;
            if (trackIndex >= 0 && trackIndex < developmentTracks.Count &&
                carIndex >= 0 && carIndex < developmentCars.Count)
                StartDevelopmentSession(developmentTracks[trackIndex], developmentCars[carIndex].Model, kind);
        }
        garageScreen = root.Q("garageScreen");
        BindTeamHeaderIcons(garageScreen);
        AddGarageStyles(garageScreen);
        var dismissal = garageScreen.Q("fireEmployeePrompt");
        dismissal.RemoveFromHierarchy();
        garageScreen.Add(dismissal);
        dismissal.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Escape) return;
            CancelFireEmployee(); evt.StopPropagation();
        });
        garageScreen.Query<VisualElement>(className: "team-home-panel").ForEach(SettingsFormStyle.StylePanel);
        garageScreen.style.display = DisplayStyle.None;

        trackScreen = root.Q("trackScreen");
        trackFpsLabel = trackScreen.Q<Label>("trackFpsLabel");
        playerTrackLaps = trackScreen.Q<Label>("playerTrackLaps");
        trackScreen.style.display = DisplayStyle.None;
        raceViewport = trackScreen.Q("raceViewport");
        trackCockpit = trackScreen.Q("trackCockpit");
        cockpitMirrorImage = trackScreen.Q("cockpitMirrorImage");
        cockpitDashboard = new CockpitDashboard(trackScreen.Q("cockpitDashboard"));
        cockpitSteeringWheel = new CockpitSteeringWheel();
        trackCockpit.Add(cockpitSteeringWheel);
        trackSessionMap = new TrackSessionMap(trackScreen.Q("trackMapWidget"));
        trackTimingRows = trackScreen.Q<ScrollView>("trackTimingRows");
        trackStandings = trackScreen.Q("trackStandings");
        toggleTrackStandingsButton = trackScreen.Q<Button>("toggleTrackStandings");
        toggleTrackStandingsButton.clicked += ToggleTrackStandings;
        scoringSession = trackScreen.Q<Label>("scoringSession");
        scoringSessionRemaining = trackScreen.Q<Label>("scoringSessionRemaining");
        scoringRaceLine = trackScreen.Q("scoringRaceLine");
        scoringRaceLap = trackScreen.Q<Label>("scoringRaceLap");
        scoringRaceRemaining = trackScreen.Q<Label>("scoringRaceRemaining");
        scoringRaceProgress = trackScreen.Q("scoringRaceProgress");
        scoringRaceProgress.generateVisualContent += DrawScoringProgress;
        scoringFlag = trackScreen.Q("scoringFlag");
        pitSettingsButton = trackScreen.Q<Button>("pitSettingsButton");
        trackPitSettings = trackScreen.Q("trackPitSettings");
        pitSettingsButton.clicked += TogglePitSettings;
        DrawButtonIcon(pitSettingsButton, DrawPitSettingsIcon);
        trackCameraAngle = trackScreen.Q<Label>("trackCameraAngle");
        cycleTrackCameraButton = trackScreen.Q<Button>("cycleTrackCameraButton");
        cycleTrackCameraButton.clicked += CycleTrackCameraAngle;
        cycleTrackCameraButton.RegisterCallback<PointerDownEvent>(BeginCameraMenuHold, TrickleDown.TrickleDown);
        cycleTrackCameraButton.RegisterCallback<PointerUpEvent>(EndCameraMenuHold, TrickleDown.TrickleDown);
        cycleTrackCameraButton.RegisterCallback<PointerLeaveEvent>(CancelCameraMenuHold);
        cycleTrackCameraButton.RegisterCallback<PointerCancelEvent>(CancelCameraMenuHold);
        DrawButtonIcon(cycleTrackCameraButton, DrawSnapshotIcon);
        trackIconTooltip = trackScreen.Q<Label>("trackIconTooltip");
        trackScreen.RegisterCallback<PointerMoveEvent>(UpdateTrackIconTooltip, TrickleDown.TrickleDown);
        trackScreen.RegisterCallback<PointerDownEvent>(HideTrackIconTooltip, TrickleDown.TrickleDown);
        trackScreen.RegisterCallback<PointerLeaveEvent>(HideTrackIconTooltip);
        raceProgressLabel = trackScreen.Q<Label>("raceProgressLabel");
        trackWeatherLabel = trackScreen.Q<Label>("trackWeatherLabel");
        trackSettingsButton = trackScreen.Q<Button>("trackSettingsButton");
        trackSettingsButton.clicked += OpenTrackSettings;
        trackScreen.Q<Button>("trackCarDetailButton").clicked += OpenPracticeCarDetail;
        trackGrandstandsButton = trackScreen.Q<Button>("trackGrandstandsButton");
        trackGrandstandsButton.clicked += TogglePracticeGrandstands;
        trackPracticeSkyButton = trackScreen.Q<Button>("trackPracticeSkyButton");
        trackPracticeSkyButton.clicked += TogglePracticeSky;
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
            var pressure = new FloatField { tooltip = "New tire cold pressure (10-60 PSI). Applies only when this tire is changed." };
            pressure.style.width = 48;
            pressure.style.fontSize = 11;
            pressure.RegisterValueChangedCallback(evt => pressure.SetValueWithoutNotify(
                float.IsNaN(evt.newValue) || float.IsInfinity(evt.newValue) ? 30f : Mathf.Clamp(evt.newValue, 10f, 60f)));
            pressure.RegisterValueChangedCallback(_ => UpdatePlayerPitRequest(queue: false));
            pitPressures[i] = pressure;
            pitTires[i].parent.Add(pressure);
            pitTires[i].RegisterValueChangedCallback(_ => UpdatePlayerPitRequest(queue: false));
        }
        refuelAtStop.RegisterValueChangedCallback(_ => UpdatePlayerPitRequest(queue: false));
        pitFuelAmount.RegisterValueChangedCallback(_ => UpdatePlayerPitRequest(queue: false));
        pitRepairAmount.RegisterValueChangedCallback(_ => UpdatePlayerPitRequest(queue: false));
        requestPitStop.clicked += () => UpdatePlayerPitRequest(queue: true);
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
        playSessionButton = trackScreen.Q<Button>("playSessionButton");
        playSessionButton.clicked += PlaySession;
        playbackSpeedButton = trackScreen.Q<Button>("playbackSpeedButton");
        playbackSpeedButton.clicked += OpenPlaybackSpeedMenu;
        pauseSessionButton = trackScreen.Q<Button>("pauseSessionButton");
        pauseSessionButton.clicked += PauseSession;
        requestCautionButton = trackScreen.Q<Button>("requestCautionButton");
        requestCautionButton.clicked += CallCaution;
        requestCautionButton.SetEnabled(false);
        trackCollisionsEnabled = PlayerPrefs.GetInt("StockCarManager.Settings.TrackCollisions", 0) != 0;
        trafficInteractionsEnabled = PlayerPrefs.GetInt("StockCarManager.Settings.TrafficInteractions", 0) != 0;
        startRaceButton = garageScreen.Q<Button>("startRaceButton");
        startRaceButton.clicked += OpenSelectedEvent;
        simulateEventButton = garageScreen.Q<Button>("simulateEventButton");
        simulateEventButton.clicked += SimulateEvent;
        garageScreen.Q<Button>("simulateSeasonButton").clicked += SimulateSeasonTemporary;
        garageScreen.Q<Button>("eventCarPicker").clicked += ShowRaceCarPicker;
        garageScreen.Q<Button>("carOptionsClose").clicked += CloseCarOptions;
        scheduleTrackOutline = new TrackOutlineView(garageScreen.Q("scheduleTrackOutline"));
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
        garageScreen.Q("recruitmentPage").RegisterCallback<KeyDownEvent>(evt =>
        { if (evt.keyCode == KeyCode.Escape) { CloseRecruitment(); evt.StopPropagation(); } });
        garageScreen.Q<Button>("recruitmentPositionSelect").clicked += OpenRecruitmentRoleSelection;
        garageScreen.Q<Button>("recruitmentSortSelect").clicked += OpenRecruitmentSortSelection;
        garageScreen.Q<Button>("cancelFireEmployeeButton").clicked += CancelFireEmployee;
        garageScreen.Q<Button>("confirmFireEmployeeButton").clicked += () => pendingDismissal?.Invoke();
        garageScreen.Q<Button>("carMarketBackButton").clicked += CloseCarMarket;
        garageScreen.Q<Button>("carMaintenanceBackButton").clicked += CloseCarMaintenance;
        garageScreen.Q("carMaintenancePage").RegisterCallback<KeyDownEvent>(evt =>
        { if (evt.keyCode == KeyCode.Escape) { CloseCarMaintenance(); evt.StopPropagation(); } });
        garageScreen.Q<Button>("usedCarSortButton").clicked += OpenUsedCarSort;
        garageScreen.Q<Button>("usedCarFilterButton").clicked += OpenUsedCarFilters;
        garageScreen.Q<Button>("usedCarClearFilterButton").clicked += ClearUsedCarFilters;
        DrawButtonIcon(garageScreen.Q<Button>("usedCarFilterButton"), p =>
        {
            p.MoveTo(new Vector2(2, 3)); p.LineTo(new Vector2(20, 3)); p.LineTo(new Vector2(13, 11));
            p.LineTo(new Vector2(13, 19)); p.LineTo(new Vector2(9, 17)); p.LineTo(new Vector2(9, 11)); p.ClosePath();
        });
        DrawButtonIcon(garageScreen.Q<Button>("usedCarClearFilterButton"), p =>
        {
            p.MoveTo(new Vector2(5, 5)); p.LineTo(new Vector2(17, 17));
            p.MoveTo(new Vector2(17, 5)); p.LineTo(new Vector2(5, 17));
        });
        garageScreen.Q<Button>("carPurchaseNewTab").clicked += () => SelectCarPurchaseTab(false);
        garageScreen.Q<Button>("carPurchaseUsedTab").clicked += () => SelectCarPurchaseTab(true);
        garageScreen.Q("carMarketPage").RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Escape) return;
            CloseCarMarket(); evt.StopPropagation();
        });
        throttleDisplay = trackScreen.Q("footerThrottle");
        brakeDisplay = trackScreen.Q("footerBrake");
        fuelDisplay = trackScreen.Q("footerFuel");
        throttleFill = trackScreen.Q("footerThrottleFill");
        brakeFill = trackScreen.Q("footerBrakeFill");
        fuelFill = trackScreen.Q("footerFuelFill");
        drivingInputMph = trackScreen.Q<Label>("footerMph");
        drivingInputRpm = trackScreen.Q<Label>("footerRpm");
        drivingInputGear = trackScreen.Q<Label>("footerGear");
        raceViewport.RegisterCallback<WheelEvent>(ZoomTrack);
        raceViewport.RegisterCallback<PointerDownEvent>(BeginTrackPointer);
        raceViewport.RegisterCallback<PointerMoveEvent>(MoveTrackPointer);
        raceViewport.RegisterCallback<PointerUpEvent>(EndTrackPointer);
        raceViewport.RegisterCallback<PointerCaptureOutEvent>(LoseTrackPointer);
        resetCameraButton = trackScreen.Q<Button>("resetCameraButton");
        resetCameraButton.clicked += ResetTrackCamera;
        DrawButtonIcon(resetCameraButton, DrawResetCameraIcon);
        snapshotButton = trackScreen.Q<Button>("snapshotButton");
        snapshotStatus = trackScreen.Q<Label>("snapshotStatus");
        snapshotButton.clicked += SaveTrackSnapshot;
        DrawSnapshotButtonIcon(snapshotButton);
        returnToGarageButton = trackScreen.Q<Button>("returnToGarageButton");
        returnToGarageButton.clicked += ReturnToGarage;
        DrawButtonIcon(returnToGarageButton, DrawLeaveSessionIcon);
        visitGarageButton = trackScreen.Q<Button>("visitGarageButton");
        visitGarageButton.style.display = DisplayStyle.None;
        visitGarageButton.clicked += VisitGarageDuringSession;
        DrawButtonIcon(visitGarageButton, DrawGarageIcon);

        loadCareerButton = titleScreen.Q<Button>("loadCareerButton");
        continueCareerButton = titleScreen.Q<Button>("continueCareerButton");
        savedCareerList = titleScreen.Q<ScrollView>("savedCareerList");
        newCareerButton = titleScreen.Q<Button>("newCareerButton");
        careerSetup = new CareerSetup(catalog, ShowTeam, CloseCareerSetup);
        careerSetup.View.styleSheets.Insert(0, Resources.Load<StyleSheet>("Startup/Startup"));
        careerSetup.View.style.display = DisplayStyle.None;
        root.Add(careerSetup.View);
        loadCareerButton.clicked += ToggleSavedCareers;
        continueCareerButton.clicked += ContinueCareer;
        BuildCareerPicker(root);
        RefreshLoadCareerButton();
        newCareerButton.clicked += OpenCareerSetup;
        quitButton = titleScreen.Q<Button>("quitButton");
        backButton = garageScreen.Q<Button>("backButton");
        garageQuitButton = garageScreen.Q<Button>("garageQuitButton");
        string[] garagePageNames = { "home", "mail", "cars", "shop", "employees", "schedule", "results", "finances", "standings" };
        garageTabs = garagePageNames.Select(name => garageScreen.Q<Button>(name + "Tab")).ToArray();
        garagePages = garagePageNames.Select(name => garageScreen.Q(name + "Page")).ToArray();
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
        garageScreen.Q<Button>("nextDayButton").clicked += () => ConfirmAdvanceCareerDate(1);
        var nextWeek = garageScreen.Q<Button>("nextWeekButton");
        var weekToggle = garageScreen.Q<Button>("toggleNextWeekButton");
        nextWeek.clicked += () =>
        {
            SetNextWeekVisible(false);
            weekToggle.Focus();
            ConfirmAdvanceCareerDate(7);
        };
        weekToggle.clicked += () => SetNextWeekVisible(nextWeek.style.display != DisplayStyle.Flex);
        garageScreen.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (nextWeek.style.display == DisplayStyle.Flex && evt.target is VisualElement target &&
                target != nextWeek && !nextWeek.Contains(target) && target != weekToggle && !weekToggle.Contains(target))
                SetNextWeekVisible(false);
        }, TrickleDown.TrickleDown);
        garageScreen.Q<Button>("cancelSkipRaceButton").clicked += CloseSkipRacePrompt;
        garageScreen.Q<Button>("confirmSkipRaceButton").clicked += ConfirmSkipRace;

        settings = new GameSettings(root, ApplyDisplaySettings, CloseSettings, SaveTeamColors, OpenRivalSettings);
        settings.CarLodChanged += level =>
        {
            if (trackPreview != null && trackPreview.SupportsCarDetail && trackPreview.SetCarDetail(level - 1))
            {
                trackFpsWarmupUntil = Time.unscaledTimeAsDouble + 1;
                UpdateTrackDisplayControls();
            }
        };
        settings.TrackMsaaSamplesChanged += samples =>
        {
            if (trackPreview == null)
                return;

            trackPreview.SetTrackMsaaSamples(samples);
            trackFpsWarmupUntil = Time.unscaledTimeAsDouble + 1;
        };
        settings.ScreenView.style.display = DisplayStyle.None;
        root.Add(settings.ScreenView);
        raceEventView = new RaceEventView(() => StartNextSession(false), () => StartNextSession(true), LeaveRaceEvent,
            settings.RestorePalette, () => activeCareer?.TeamColors ?? (default, default));
        raceEventView.Screen.styleSheets.Add(Resources.Load<StyleSheet>("Startup/Startup"));
        AddGarageStyles(raceEventView.Screen);
        raceEventView.Screen.style.display = DisplayStyle.None;
        root.Add(raceEventView.Screen);
        var garageSettings = garageScreen.Q<Button>("garageSettingsButton");
        BindStartupSettings(root);
        garageSettings.clicked += () => OpenSettings(garageScreen, garageSettings);
        settings.RestorePalette();
        ApplyDisplaySettings();
    }

    private static void AddGarageStyles(VisualElement screen)
    {
        screen.AddToClassList("garage-styled");
        var sheet = Resources.Load<StyleSheet>("Startup/GarageStyles");
        if (sheet != null) screen.styleSheets.Add(sheet);
    }

    private static void BindTeamHeaderIcons(VisualElement screen)
    {
        var typography = Resources.Load<StyleSheet>("Fonts/GoogleSans/SettingsTypography");
        if (typography != null) screen.styleSheets.Add(typography);
        var tooltipStyles = Resources.Load<StyleSheet>("Settings/SettingsControls");
        if (tooltipStyles != null)
            screen.styleSheets.Add(tooltipStyles);
        SettingsFormStyle.AddHoverTooltips(screen);
        DrawButtonIcon(screen.Q<Button>("garageSettingsButton"), DrawSettingsSlidersIcon);
        DrawButtonIcon(screen.Q<Button>("backButton"), DrawStartupHomeIcon);
        DrawButtonIcon(screen.Q<Button>("garageQuitButton"), DrawLeaveSessionIcon);
    }

    private static void DrawStartupHomeIcon(Painter2D painter)
    {
        painter.MoveTo(new Vector2(2, 10));
        painter.LineTo(new Vector2(11, 2));
        painter.LineTo(new Vector2(20, 10));
        painter.MoveTo(new Vector2(5, 8));
        painter.LineTo(new Vector2(5, 20));
        painter.LineTo(new Vector2(9, 20));
        painter.LineTo(new Vector2(9, 13));
        painter.LineTo(new Vector2(13, 13));
        painter.LineTo(new Vector2(13, 20));
        painter.LineTo(new Vector2(17, 20));
        painter.LineTo(new Vector2(17, 8));
    }

    private static void BindTitleIcons(VisualElement title)
    {
        var tooltips = Resources.Load<StyleSheet>("Settings/SettingsControls");
        if (tooltips != null)
            title.styleSheets.Add(tooltips);
        SettingsFormStyle.AddHoverTooltips(title);
        DrawButtonIcon(title.Q<Button>("testPracticeButton"), DrawPracticeFlagIcon);
        DrawButtonIcon(title.Q<Button>("titleSettingsButton"), DrawSettingsSlidersIcon);
        DrawButtonIcon(title.Q<Button>("quitButton"), DrawLeaveSessionIcon);
    }

    private static void DrawPracticeFlagIcon(Painter2D painter)
    {
        painter.MoveTo(new Vector2(5, 20));
        painter.LineTo(new Vector2(5, 3));
        painter.LineTo(new Vector2(18, 3));
        painter.LineTo(new Vector2(15, 7));
        painter.LineTo(new Vector2(18, 11));
        painter.LineTo(new Vector2(5, 11));
        painter.MoveTo(new Vector2(9, 3));
        painter.LineTo(new Vector2(9, 11));
        painter.MoveTo(new Vector2(5, 7));
        painter.LineTo(new Vector2(15, 7));
    }

    private static void DrawSettingsSlidersIcon(Painter2D painter)
    {
        for (int row = 0; row < 3; row++)
        {
            float y = 5 + 6 * row;
            float knob = row == 1 ? 14 : 8;
            painter.MoveTo(new Vector2(3, y));
            painter.LineTo(new Vector2(19, y));
            painter.MoveTo(new Vector2(knob, y - 2));
            painter.LineTo(new Vector2(knob, y + 2));
        }
    }

    private void BindStartupSettings(VisualElement root)
    {
        var startupButton = titleScreen.Q<Button>("titleSettingsButton");
        var menu = new VisualElement { name = "startupSettingsMenu" };
        SettingsFormStyle.Apply(menu);
        menu.style.display = DisplayStyle.None;
        var frame = new VisualElement();
        frame.AddToClassList("settings-frame");
        frame.style.maxWidth = 480;
        menu.Add(frame);

        var header = new VisualElement();
        header.AddToClassList("settings-header");
        header.Add(SettingsFormStyle.Label("Settings", "settings-title"));
        frame.Add(header);

        var options = new VisualElement();
        options.AddToClassList("settings-group");
        var body = new ScrollView();
        body.AddToClassList("settings-scroll");
        body.Add(options);
        frame.Add(body);

        var footer = new VisualElement();
        footer.AddToClassList("settings-footer");
        footer.style.justifyContent = Justify.FlexEnd;
        footer.Add(SettingsFormStyle.Button("Back", () =>
        {
            menu.style.display = DisplayStyle.None;
            titleScreen.style.display = DisplayStyle.Flex;
            startupButton.Focus();
        }));
        frame.Add(footer);
        root.Add(menu);

        var seriesButton = SettingsFormStyle.Button("Series Settings", null);
        var carTypeButton = SettingsFormStyle.Button("Car Type Settings", null);
        var gameButton = SettingsFormStyle.Button("Game Settings", null);
        var series = new SeriesSettings(() => ShowMenu(seriesButton));
        var cars = new CarTypeSettings(() => ShowMenu(carTypeButton));

        AddPage(seriesButton, series.Screen, series.Open);
        AddPage(carTypeButton, cars.Screen, cars.Open);
        gameButton.clicked += () => OpenSettings(menu, gameButton);
        options.Add(gameButton);
        startupButton.clicked += () => ShowMenu(seriesButton);

        void ShowMenu(Button focus)
        {
            titleScreen.style.display = DisplayStyle.None;
            menu.style.display = DisplayStyle.Flex;
            focus.Focus();
        }

        void AddPage(Button button, VisualElement screen, System.Action open)
        {
            options.Add(button);
            screen.style.display = DisplayStyle.None;
            root.Add(screen);
            button.clicked += () =>
            {
                menu.style.display = DisplayStyle.None;
                screen.style.display = DisplayStyle.Flex;
                open();
            };
        }
    }

    private void ShowDevelopmentTrackPicker(string message = null)
    {
        catalog.Refresh();
        var dropdown = developmentTrackPicker.Q<DropdownField>("developmentTracks");
        var carDropdown = developmentTrackPicker.Q<DropdownField>("developmentCars");
        var start = developmentTrackPicker.Q<Button>("startDevelopmentPractice");
        developmentTracks = catalog.Entries.ToList();
        int savedId = PlayerPrefs.GetInt(DevelopmentTrackPreference, -1);
        int selected = developmentTracks.IndexOf(catalog.Find(savedId));
        bool tracksAvailable = developmentTracks.Count > 0;
        var choices = developmentTracks.Select(entry => entry.Definition.name).ToList();
        foreach (var group in developmentTracks.Select((entry, index) => (entry, index)).GroupBy(item => item.entry.Definition.name))
        {
            if (group.Count() > 1)
                foreach (var item in group)
                    choices[item.index] = item.entry.ToString();
        }
        dropdown.choices = choices;
        dropdown.SetValueWithoutNotify(tracksAvailable ? choices[Mathf.Max(0, selected)] : string.Empty);
        dropdown.SetEnabled(tracksAvailable);

        developmentCars = CarTypeDefinition.AvailableModels();
        string savedModel = PlayerPrefs.GetString(DevelopmentCarPreference, "Impala");
        int carIndex = developmentCars.FindIndex(car => car.Model == savedModel);
        bool carsAvailable = developmentCars.Count > 0;
        carDropdown.choices = developmentCars.Select(car => car.Name).ToList();
        carDropdown.SetValueWithoutNotify(carsAvailable ? carDropdown.choices[Mathf.Max(0, carIndex)] : string.Empty);
        carDropdown.SetEnabled(carsAvailable);

        bool available = tracksAvailable && carsAvailable;
        start.SetEnabled(available);
        developmentTrackPicker.Q<Button>("startDevelopmentRace").SetEnabled(available);

        var status = developmentTrackPicker.Q<Label>("developmentPracticeStatus");
        status.text = message ?? (!tracksAvailable ? "No tracks were found." : !carsAvailable ? "No cars were found." : string.Empty);
        status.style.display = string.IsNullOrEmpty(status.text) ? DisplayStyle.None : DisplayStyle.Flex;
        titleScreen.style.display = DisplayStyle.None;
        developmentTrackPicker.style.display = DisplayStyle.Flex;
        settings.RestorePalette();
        (available ? start : developmentTrackPicker.Q<Button>("backFromDevelopmentPractice")).Focus();
    }

    private void StartDevelopmentSession(TrackCatalog.Entry track, string modelResource, RaceSessionKind kind)
    {
        var definition = track.Definition;
        bool testRace = kind == RaceSessionKind.Race;
        if (testRace && definition.pitRoad?.enabled != true)
        {
            ShowDevelopmentTrackPicker("A test race requires a pit road with an entry and exit.");
            return;
        }

        int developmentTrackId = definition.id;
        PlayerPrefs.SetInt(DevelopmentTrackPreference, developmentTrackId);
        PlayerPrefs.SetString(DevelopmentCarPreference, modelResource);
        PlayerPrefs.Save();
        developmentTrackPicker.style.display = DisplayStyle.None;
        var existingDriver = activeCareer?.Driver;
        var testDriver = existingDriver != null
            ? JsonUtility.FromJson<Driver>(JsonUtility.ToJson(existingDriver))
            : Driver.Create(new System.Random(developmentTrackId), null);
        var career = new Career
        {
            teamName = activeCareer?.teamName ?? "Player Team",
            primaryTeamColor = activeCareer?.primaryTeamColor,
            secondaryTeamColor = activeCareer?.secondaryTeamColor,
            teamBalance = RaceFinances.StartingBalance,
            drivers = new List<Driver> { testDriver }
        };
        career.InitializeCars(new System.Random(developmentTrackId), randomizeRivals: true);
        var car = career.cars[0];
        foreach (var entry in career.AllCars)
            entry.modelResource = modelResource;
        var session = testRace
            ? new RaceSession { kind = RaceSessionKind.Race, name = "Test Race", laps = RaceSession.TestRaceLaps }
            : new RaceSession { kind = RaceSessionKind.Practice, name = "Practice", minutes = RaceSession.PracticeMinutes };
        var race = new ScheduledRace
        {
            trackId = developmentTrackId,
            trackName = definition.name,
            laps = session.laps,
            carNumber = car.number,
            carChassisNumber = car.chassisNumber,
            sessions = new List<RaceSession> { session }
        };
        if (testRace)
            race.EnsureStages(definition.raceStageLaps);

        var forecast = SessionWeatherForecast.Ensure(session, definition, race);
        developmentPracticeCareer = career;
        developmentPractice = true;
        OpenTrack(track, true, car.number, SessionWeatherForecast.IsNightAt(forecast.startHour), car);
        if (trackPreview.Image == null)
        {
            CloseTrackSession(testPracticeMessage: $"Could not open {session.name.ToLowerInvariant()} at {definition.name}.");
            return;
        }
        if (testRace && !trackPreview.HasSessionPitRoad)
        {
            CloseTrackSession(testPracticeMessage: "A test race requires a pit road with an entry and exit.");
            return;
        }

        titleScreen.style.display = DisplayStyle.None;
        activeRace = race;
        OpenEventGarageView(race, definition, car, System.Array.Empty<string>());
        activeSession = session;
        raceEventView.SetRunningSession(session);
        var entries = TrackPreview.CreateTimingEntries(car, career);
        if (testRace)
        {
            session.qualifyingOrder = entries.Select(entry => entry.carNumber).ToArray();
            var random = new System.Random();
            for (int i = session.qualifyingOrder.Length - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (session.qualifyingOrder[i], session.qualifyingOrder[j]) = (session.qualifyingOrder[j], session.qualifyingOrder[i]);
            }
        }
        var simulation = new CarSimulationContext(definition, race, session, entries)
        {
            EqualConditionTesting = !testRace && settings.EqualConditionPractice
        };
        trackPreview.BeginSession(simulation, new TrackState001(), settings.SpeedModel, settings.BehaviorModel,
            session.qualifyingOrder, settings.UsePaceTruck);
        UpdateTrackDisplayControls();
        raceEventView.EqualConditionTesting = trackPreview.EqualConditionTesting;
        RefreshTrackTiming();
        sessionPlaybackSpeed = 1;
        sessionPaused = false;
        ApplySessionPlayback();
        ConfigureGarageVisitButton();
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

    private string SaveTeamColors(string primary, string secondary, string teamName, CarManufacturer manufacturer, EngineSupplier supplier)
    {
        var career = activeCareer;
        string previousTeamName = career.teamName;
        var previousSupplier = career.engineSupplier;
        var previousManufacturers = career.cars.Select(car => car.manufacturer).ToArray();
        career.teamName = teamName;
        career.engineSupplier = supplier;
        foreach (var car in career.cars) car.manufacturer = manufacturer;
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
            career.teamName = previousTeamName;
            career.engineSupplier = previousSupplier;
            for (int i = 0; i < career.cars.Count; i++) career.cars[i].manufacturer = previousManufacturers[i];
            career.primaryTeamColor = previousPrimary;
            career.secondaryTeamColor = previousSecondary;
            return "Could not save team settings. " + ex.Message;
        }

        ApplyCareerCarModels(career);
        var page = garagePages.FirstOrDefault(view => view.style.display == DisplayStyle.Flex);
        if (page != null)
            UpdateGarageBackground(page.name);
        RefreshTeamHeader();
        RefreshTeamCars();
        RefreshDriverLabel(career);
        DisplayEmployees(career);
        return null;
    }

    private void OpenRivalSettings(Button source)
    {
        RivalRosterSettings.Open(settings.ScreenView, source, PlayerNumbersForSeries, series =>
        {
            if (settingsReturnScreen != garageScreen || activeCareer == null || activeCareer.SeriesRules.id != series.id)
                return;

            RivalRosterStore.ApplyToCareer(activeCareer);
            RefreshHomeDashboard();
            DisplayTeamStandings(activeCareer.schedule);
        });
    }

    private IEnumerable<int> PlayerNumbersForSeries(RacingSeries series)
    {
        var savedNumbers = careerSetup.ReadSavedCareers()
            .Where(career => RacingSeries.CanonicalId(career.seriesId) == series.id)
            .SelectMany(career => career.cars ?? System.Array.Empty<TeamCar>())
            .Where(car => car != null)
            .Select(car => car.number);
        return settingsReturnScreen == garageScreen && activeCareer?.SeriesRules.id == series.id
            ? savedNumbers.Concat(activeCareer.cars.Select(car => car.number))
            : savedNumbers;
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
        largeCarPreviews.Dispose();
        careerSetup?.ClosePreview();
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
        if (teamColorBackground != null) { teamColorBackground.Release(); Destroy(teamColorBackground); }
        if (teamColorDesign != null) Destroy(teamColorDesign);
        if (playSessionButton != null) playSessionButton.clicked -= PlaySession;
        if (playbackSpeedButton != null) playbackSpeedButton.clicked -= OpenPlaybackSpeedMenu;
        if (pauseSessionButton != null) pauseSessionButton.clicked -= PauseSession;
        if (requestCautionButton != null) requestCautionButton.clicked -= CallCaution;
        if (toggleTrackStandingsButton != null)
            toggleTrackStandingsButton.clicked -= ToggleTrackStandings;
        if (scoringRaceProgress != null)
            scoringRaceProgress.generateVisualContent -= DrawScoringProgress;
        if (pitSettingsButton != null)
            pitSettingsButton.clicked -= TogglePitSettings;
        if (cycleTrackCameraButton != null)
        {
            CancelCameraMenuHold();
            cycleTrackCameraButton.clicked -= CycleTrackCameraAngle;
            cycleTrackCameraButton.UnregisterCallback<PointerDownEvent>(BeginCameraMenuHold, TrickleDown.TrickleDown);
            cycleTrackCameraButton.UnregisterCallback<PointerUpEvent>(EndCameraMenuHold, TrickleDown.TrickleDown);
            cycleTrackCameraButton.UnregisterCallback<PointerLeaveEvent>(CancelCameraMenuHold);
            cycleTrackCameraButton.UnregisterCallback<PointerCancelEvent>(CancelCameraMenuHold);
        }
        if (trackScreen != null)
        {
            HideTrackIconTooltip();
            trackScreen.UnregisterCallback<PointerMoveEvent>(UpdateTrackIconTooltip, TrickleDown.TrickleDown);
            trackScreen.UnregisterCallback<PointerDownEvent>(HideTrackIconTooltip, TrickleDown.TrickleDown);
            trackScreen.UnregisterCallback<PointerLeaveEvent>(HideTrackIconTooltip);
        }
        if (trackSettingsButton != null)
            trackSettingsButton.clicked -= OpenTrackSettings;
        if (trackGrandstandsButton != null)
            trackGrandstandsButton.clicked -= TogglePracticeGrandstands;
        if (trackPracticeSkyButton != null)
            trackPracticeSkyButton.clicked -= TogglePracticeSky;
        if (startRaceButton != null)
            startRaceButton.clicked -= OpenSelectedEvent;
        if (simulateEventButton != null)
            simulateEventButton.clicked -= SimulateEvent;
        if (addTeamCarButton != null)
            addTeamCarButton.clicked -= OpenCarMarket;
        if (loadCareerButton != null)
            loadCareerButton.clicked -= ToggleSavedCareers;
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
        if (visitGarageButton != null)
            visitGarageButton.clicked -= VisitGarageDuringSession;
        if (resetCameraButton != null)
            resetCameraButton.clicked -= ResetTrackCamera;
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

    private void DisplayGaragePage(int selected, ScheduledRace resultsEvent = null, TeamMailMessage mailMessage = null)
    {
        if (selected < 0)
            return;
        for (int i = 0; i < garagePages.Length; i++)
        {
            bool active = i == selected;
            garagePages[i].style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
            garageTabs[i].EnableInClassList("nav-button-selected", active);
            garageTabs[i].EnableInClassList("settings-tab-current", active);
        }
        garageScreen.Q("carMarketPage").style.display = DisplayStyle.None;
        garageScreen.Q("carMaintenancePage").style.display = DisplayStyle.None;
        garageScreen.Q("recruitmentPage").style.display = DisplayStyle.None;
        garageScreen.Q("garageFooter").SetEnabled(true);
        var body = garageScreen.Q<ScrollView>("garageBody");
        string pageName = garagePages[selected].name;
        bool isHome = pageName == "homePage";
        bool fitBody = isHome || pageName == "financesPage" || pageName == "shopPage" || pageName == "resultsPage" || pageName == "standingsPage";
        body.contentContainer.style.flexGrow = fitBody ? 1 : 0;
        body.verticalScrollerVisibility = fitBody ? ScrollerVisibility.Hidden : ScrollerVisibility.Auto;
        if (isHome) RefreshHomeDashboard();
        UpdateGarageBackground(pageName);
        if (pageName == "shopPage" && activeCareer != null)
            RefreshShopCarPicker();
        if (pageName == "employeesPage" && activeCareer != null)
            DisplayEmployeeWorkInProgress();
        if (activeCareer != null)
            RefreshTeamHeader();
        if (pageName != "employeesPage")
            CancelFireEmployee();
        body.style.display = pageName == "schedulePage" || pageName == "employeesPage" || pageName == "mailPage" || pageName == "standingsPage"
            ? DisplayStyle.None : DisplayStyle.Flex;
        if (pageName == "mailPage" && activeCareer != null)
            teamMail.Show(garagePages[selected], activeCareer, settings.RestorePalette, DeleteSocialPosts,
                MarkMailRead, DeleteMail, RespondToScenario, SocialDriverPortrait, mailMessage);
        if (pageName == "schedulePage")
            RefreshSchedule(selectNext: true);

        if (pageName == "standingsPage")
            DisplayTeamStandings(activeCareer.schedule);
        if (pageName == "resultsPage")
            BuildTeamResults(resultsEvent);
        if (pageName == "financesPage")
            DisplayFinances();
        if (pageName == "carsPage")
            RefreshTeamCars();
        body.scrollOffset = Vector2.zero;
        settings?.RestorePalette();
    }

    private void RefreshHomeDashboard()
    {
        RefreshMailFooterBadge();
        if (activeCareer?.schedule == null) return;
        var todo = garageScreen.Q<ScrollView>("homeTodoRows");
        todo.Clear();
        foreach (var mail in activeCareer.inbox ?? new List<TeamMailMessage>())
        {
            if (mail == null || !string.IsNullOrWhiteSpace(mail.mediaOutletId) ||
                (mail.isRead && !mail.NeedsResponse)) continue;
            var task = AddTask(mail.subject, "mailPage", DrawTodoEmailIcon, navigateOnClick: false);
            task.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.button != 0 || evt.clickCount != 2) return;
                OpenMessage();
                evt.StopPropagation();
            });
            task.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter) return;
                OpenMessage();
                evt.StopPropagation();
            });
            void OpenMessage() => DisplayGaragePage(
                System.Array.FindIndex(garagePages, page => page.name == "mailPage"), mailMessage: mail);
            task.tooltip = (mail.NeedsResponse ? (mail.BlocksAdvancement ? "Response required: " : "Respond to email: ") : mail.requiresRead && !mail.isRead ? "Reading required: " : "Read email: ") + mail.subject + ". Double-click to open.";
            if (mail.BlocksAdvancement)
            {
                var alert = SettingsFormStyle.Label("!", "team-home-response-alert");
                alert.pickingMode = PickingMode.Ignore;
                task.Add(alert);
            }
        }
        var nextRace = activeCareer.schedule.races?.FirstOrDefault(race => !race.completed);
        var nextTrack = nextRace == null ? null :
            catalog.Find(nextRace.trackId)?.Definition;
        if (nextTrack != null)
        {
            var bestChassis = RecommendedHomeChassis(nextTrack);
            if (bestChassis != null)
            {
                var task = AddTask($"Chassis {bestChassis.chassisNumber} for race at {nextRace.trackName}",
                    "shopPage", DrawPitSettingsIcon, navigateOnClick: false);
                task.tooltip = $"Double-click to prepare chassis {bestChassis.chassisNumber} in Shop for {nextRace.trackName}.";
                task.RegisterCallback<ClickEvent>(evt =>
                {
                    if (evt.button != 0 || evt.clickCount != 2) return;
                    OpenChassis();
                    evt.StopPropagation();
                });
                task.RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter) return;
                    OpenChassis();
                    evt.StopPropagation();
                });
                void OpenChassis()
                {
                    shopCarPicker.SelectCar(bestChassis);
                    DisplayGaragePage(System.Array.FindIndex(garagePages, page => page.name == "shopPage"));
                }
            }
            else
            {
                string type = TeamCar.DisplayChassisType(nextTrack.TrackCategory);
                var task = AddTask($"Acquire a {type} chassis for race at {nextRace.trackName}",
                    "carsPage", DrawPitSettingsIcon);
                task.tooltip = $"No suitable chassis: your team needs a {type} chassis for {nextRace.trackName}. Open Cars to purchase one.";
            }
        }
        RefreshHomeNextRacePanels(nextRace, nextTrack);
        if (!HasDriver(activeCareer)) AddTask("Hire a driver for your team.", "employeesPage");
        if (todo.childCount == 0)
            todo.Add(SettingsFormStyle.Label("You're all caught up.", "team-home-note"));

        var recent = garageScreen.Q("homeRecentRaceRows");
        recent.Clear();
        var seasons = (activeCareer.completedSeasons ?? Enumerable.Empty<SeasonSchedule>())
            .Where(season => season != activeCareer.schedule).Append(activeCareer.schedule);
        var races = seasons.SelectMany(season => season.races ?? Enumerable.Empty<ScheduledRace>())
            .Where(race => race.completed).OrderByDescending(race =>
                Career.TryReadDate(race.date, out var date) ? date : System.DateTime.MinValue).Take(3);
        foreach (var race in races)
        {
            var result = race.sessions?.Find(session => session.kind == RaceSessionKind.Race)?.results?
                .Find(race.IsPlayerResult);
            string finish = race.playerSkipped ? "Skipped" : result?.position > 0 ? $"P{result.position}" : "No result";
            var row = SettingsFormStyle.Button("", null);
            row.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.button != 0 || evt.clickCount != 2) return;
                OpenRaceResults();
                evt.StopPropagation();
            });
            row.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter) return;
                OpenRaceResults();
                evt.StopPropagation();
            });
            void OpenRaceResults() => DisplayGaragePage(
                System.Array.FindIndex(garagePages, page => page.name == "resultsPage"), race);
            row.AddToClassList("team-home-result");
            row.tooltip = "Double-click to view results for " + race.trackName + ".";
            string raceDayMonth = Career.TryReadDate(race.date, out var resultDate)
                ? resultDate.ToString("dd/MM", CultureInfo.InvariantCulture) : "-";
            row.Add(SettingsFormStyle.Label(raceDayMonth, "team-home-result-date"));
            row.Add(SettingsFormStyle.Label(race.trackName, "team-home-track"));
            row.Add(SettingsFormStyle.Label(finish, "team-home-finish"));
            recent.Add(row);
        }
        if (recent.childCount == 0)
            recent.Add(SettingsFormStyle.Label("No completed races yet.", "team-home-note"));

        garageScreen.Q<Label>("homeStandingsHeading").text =
            careerSetup.SeriesName(activeCareer) + " Points Standings";
        var rows = garageScreen.Q<ScrollView>("homeStandingsRows");
        var scroll = rows.scrollOffset;
        rows.Clear();
        var totals = new Dictionary<(string Driver, string Team), long>();
        var numbers = new Dictionary<(string Driver, string Team), string>();
        var numberPaint = new Dictionary<(string Driver, string Team), CarPaintStyle>();
        var roster = TrackPreview.CreateTimingEntries(activeCareer.cars[0], activeCareer);
        foreach (var entry in roster)
        {
            var key = (entry.driverName ?? "Unknown driver", entry.teamName ?? "");
            totals[key] = 0;
            numbers[key] = entry.DisplayCarNumber;
            if (entry.car != null) numberPaint[key] = entry.car.PaintStyle;
        }
        foreach (var race in activeCareer.schedule.races ?? Enumerable.Empty<ScheduledRace>())
        {
            if (!race.completed) continue;
            var results = race.sessions?.Find(session => session.kind == RaceSessionKind.Race)?.results;
            if (results == null) continue;
            foreach (var result in results)
            {
                var key = (result.driverName ?? "Unknown driver", result.teamName ?? "");
                totals[key] = (totals.TryGetValue(key, out var points) ? points : 0) + result.pointsEarned;
                if (!numbers.ContainsKey(key) && result.carNumber > 0)
                    numbers[key] = result.DisplayCarNumber;
                if (result.car != null) numberPaint.TryAdd(key, result.car.PaintStyle);
            }
        }
        int position = 0;
        foreach (var entry in totals.OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key.Driver, System.StringComparer.OrdinalIgnoreCase))
        {
            var row = new VisualElement();
            row.AddToClassList("team-home-row");
            row.EnableInClassList("team-home-player", entry.Key.Team == activeCareer.teamName);
            position++;
            row.Add(SettingsFormStyle.Label(position.ToString(), "team-home-position"));
            var carNumber = SettingsFormStyle.Label(numbers.TryGetValue(entry.Key, out string number)
                ? number : "-", "team-home-car-number");
            if (number != null && numberPaint.TryGetValue(entry.Key, out var paint))
                CarNumberFonts.Apply(carNumber, paint);
            row.Add(carNumber);
            row.Add(SettingsFormStyle.Label(entry.Key.Driver, "team-home-driver"));
            row.Add(SettingsFormStyle.Label(entry.Value == 0 ? "-" : entry.Value.ToString(), "team-home-points"));
            rows.Add(row);
        }
        rows.schedule.Execute(() => rows.scrollOffset = scroll);

        Button AddTask(string text, string pageName, System.Action<Painter2D> drawIcon = null, bool navigateOnClick = true)
        {
            var button = SettingsFormStyle.Button(string.Empty, navigateOnClick ? () =>
                DisplayGaragePage(System.Array.FindIndex(garagePages, page => page.name == pageName)) : (System.Action)null);
            button.AddToClassList("team-home-task");
            if (drawIcon != null)
            {
                var icon = new VisualElement { pickingMode = PickingMode.Ignore };
                icon.AddToClassList("track-icon-art");
                button.Add(icon);
                DrawButtonIcon(button, drawIcon);
            }
            var label = SettingsFormStyle.Label(text, "team-home-task-text");
            label.pickingMode = PickingMode.Ignore;
            button.Add(label);
            button.tooltip = text;
            todo.Add(button);
            return button;
        }
    }

    private TeamCar RecommendedHomeChassis(TrackDefinition track) => track == null ? null :
        activeCareer.cars.Where(car => car.chassisType == track.TrackCategory)
            .OrderByDescending(car => CrewChiefMail.TrackScore(car, track))
            .ThenBy(car => car.chassisNumber, System.StringComparer.Ordinal).FirstOrDefault();

    private void RefreshHomeNextRacePanels(ScheduledRace race, TrackDefinition track)
    {
        var carPanel = garageScreen.Q("homeSuggestedCarPanel");
        var trackPanel = garageScreen.Q("homeNextRacePanel");
        carPanel.Clear();
        trackPanel.Clear();
        var chassisTitle = SettingsFormStyle.Label("Chassis", "team-home-heading");
        carPanel.Add(chassisTitle);
        trackPanel.Add(SettingsFormStyle.Label("Next Race", "team-home-heading"));
        if (race == null)
        {
            carPanel.Add(SettingsFormStyle.Label("No upcoming race.", "team-home-note"));
            trackPanel.Add(SettingsFormStyle.Label("No upcoming race.", "team-home-note"));
            return;
        }
        var car = RecommendedHomeChassis(track) ?? activeCareer.cars
            .OrderByDescending(candidate => CrewChiefMail.TrackScore(candidate, track))
            .ThenBy(candidate => candidate.chassisNumber, System.StringComparer.Ordinal).FirstOrDefault();
        if (car == null)
        {
            carPanel.Add(SettingsFormStyle.Label("No chassis owned.", "team-home-note"));
        }
        else
        {
            var image = new Image
            {
                image = carThumbnails.Get(car, ActiveSponsorNames(), transparentBackground: true),
                scaleMode = ScaleMode.ScaleToFit,
                tooltip = $"Double-click to open chassis {car.chassisNumber} in Shop."
            };
            image.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.button != 0 || evt.clickCount != 2) return;
                shopCarPicker.SelectCar(car);
                DisplayGaragePage(System.Array.FindIndex(garagePages, page => page.name == "shopPage"));
                evt.StopPropagation();
            });
            image.AddToClassList("team-home-car-preview");
            carPanel.Add(image);
            chassisTitle.text = string.IsNullOrEmpty(car.chassisNumber) ? "Chassis" : $"Chassis {car.chassisNumber}";
            if (track != null && car.chassisType != track.TrackCategory)
                carPanel.Add(SettingsFormStyle.Label(
                    $"{car.ChassisTypeName} chassis - {TeamCar.DisplayChassisType(track.TrackCategory)} needed",
                    "team-home-chassis-mismatch"));
            TeamCarCard.AddComponentRatings(carPanel, car, useIcons: true);
        }
        var identity = new VisualElement();
        identity.AddToClassList("team-home-track-identity");
        var logo = track == null ? null : SeriesSettingsStore.LoadArtwork(track.logo);
        if (logo != null)
        {
            var image = new Image { image = logo, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            image.AddToClassList("team-home-track-logo");
            identity.Add(image);
        }
        else
        {
            var placeholder = SettingsFormStyle.Label("TRACK", "team-home-track-logo-placeholder");
            placeholder.tooltip = "Track logo not configured.";
            identity.Add(placeholder);
        }
        var details = new VisualElement();
        details.AddToClassList("team-home-race-text");
        details.Add(SettingsFormStyle.Label(race.trackName, "team-home-next-track-name"));
        identity.Add(details);
        trackPanel.Add(identity);
        Detail(RaceDate(race));
        if (track != null)
        {
            var outline = new VisualElement();
            outline.AddToClassList("team-home-track-outline");
            outline.tooltip = "Double-click to open the next race in Schedule.";
            outline.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.button != 0 || evt.clickCount != 2) return;
                DisplayGaragePage(System.Array.FindIndex(garagePages, page => page.name == "schedulePage"));
                evt.StopPropagation();
            });
            trackPanel.Add(outline);
            new TrackOutlineView(outline).SetTrack(track);
        }

        void Detail(string text) => details.Add(SettingsFormStyle.Label(text, "team-home-race-detail"));
    }

    private static void DrawTodoEmailIcon(Painter2D painter)
    {
        painter.MoveTo(new Vector2(2, 5));
        painter.LineTo(new Vector2(20, 5));
        painter.LineTo(new Vector2(20, 17));
        painter.LineTo(new Vector2(2, 17));
        painter.ClosePath();
        painter.MoveTo(new Vector2(2, 5));
        painter.LineTo(new Vector2(11, 12));
        painter.LineTo(new Vector2(20, 5));
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
                repair => CancelComponentRepair(car, repair), activeCareer.SeriesRules));
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
        toggle.EnableInClassList("settings-tab-current", showingWorkInProgress);
        if (!showingWorkInProgress)
        {
            DisplayEmployees(activeCareer);
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
                var name = new Label(repair.AssigneeDisplay);
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
                var chassis = new Label($"Chassis {car.chassisNumber ?? "-"}");
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
                }) { text = "\u2192", tooltip = $"Open chassis {car.chassisNumber} in Shop" };
                openShop.AddToClassList("settings-button");
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

    private string MarkMailRead(TeamMailMessage message)
    {
        if (message.isRead)
            return null;

        message.isRead = true;
        string error = SaveMailChange(() => message.isRead = false);
        RefreshMailFooterBadge();
        return error;
    }

    private string DeleteMail(TeamMailMessage[] messages)
    {
        messages = messages.Where(message => !message.BlocksAdvancement).ToArray();
        if (messages.Length == 0)
            return null;

        var previousInbox = activeCareer.inbox;
        var previousDeleted = activeCareer.deletedMailIds;
        var removed = new HashSet<TeamMailMessage>(messages);
        activeCareer.inbox = previousInbox.Where(message => !removed.Contains(message)).ToList();
        activeCareer.deletedMailIds = (previousDeleted ?? Enumerable.Empty<string>())
            .Union(messages.Select(message => message.eventId).Where(id => !string.IsNullOrEmpty(id))).ToList();
        return SaveMailChange(() =>
        {
            activeCareer.inbox = previousInbox;
            activeCareer.deletedMailIds = previousDeleted;
        });
    }

    private string RespondToScenario(TeamMailMessage message, string option)
    {
        if (!activeCareer.inbox.Contains(message) || !message.NeedsResponse || !message.scenarioOptions.Contains(option))
            return "This scenario response is no longer available.";

        if (message.seriesChangeYear > 0 && (option == "Change Series" || option == "Yes"))
        {
            garageScreen.style.display = DisplayStyle.None;
            titleScreen.style.display = DisplayStyle.None;
            careerSetup.OpenSeriesChange(activeCareer, message.eventId, message.seriesChangeYear);
            careerSetup.View.style.display = DisplayStyle.Flex;
            return null;
        }

        if (message.seriesChangeYear > 0)
        {
            ConfirmStayInCurrentSeries(message, option);
            return null;
        }

        int choice = System.Array.IndexOf(message.scenarioOptions, option);
        if (message.scenarioSponsorOffers is { Length: > 0 } availableOffers &&
            (choice >= availableOffers.Length || availableOffers[choice] == null))
            return "This sponsor offer is no longer available.";

        long previousBalance = activeCareer.teamBalance;
        System.Action undoPurchase;
        string responseError = message.scenarioOffer switch
        {
            TeamMailOffer.StaffArrest => StaffArrest.Respond(activeCareer, message, choice, out undoPurchase),
            TeamMailOffer.Merchandising => Merchandising.Accept(activeCareer, message, choice, out undoPurchase),
            TeamMailOffer.PublicAppearance or TeamMailOffer.PodcastAppearance =>
                ScenarioMail.ApplyDriverAppearance(activeCareer, message, choice, out undoPurchase),
            _ => ScenarioMail.ApplyPurchase(activeCareer, message, choice, out undoPurchase)
        };
        if (responseError != null) return responseError;

        message.scenarioResponse = option;
        var previousSponsor = activeCareer.PrimarySponsor;
        var season = activeCareer.schedule;
        long previousIncome = season.sponsorIncome;
        long previousSigningIncome = season.sponsorSigningIncome;
        if (message.scenarioSponsorOffers is { Length: > 0 } offers)
        {
            var offer = offers[choice];
            activeCareer.primarySponsor = new SponsorContract
            {
                offer = offer,
                racesRemaining = season.races.Count(race => !race.completed),
                paid = offer.signingPayment
            };
            activeCareer.teamBalance += offer.signingPayment;
            season.sponsorIncome += offer.signingPayment;
            season.sponsorSigningIncome += offer.signingPayment;
        }
        string error = SaveMailChange(() =>
        {
            message.scenarioResponse = null;
            undoPurchase?.Invoke();
            activeCareer.primarySponsor = previousSponsor;
            activeCareer.teamBalance = previousBalance;
            season.sponsorIncome = previousIncome;
            season.sponsorSigningIncome = previousSigningIncome;
        });
        if (error == null)
        {
            RefreshCareerDate();
            RefreshTeamBalance();
            if (message.scenarioOffer == TeamMailOffer.StaffArrest)
            {
                RefreshDriverLabel(activeCareer);
                RefreshSchedule();
            }
        }
        return error;
    }

    private void ConfirmStayInCurrentSeries(TeamMailMessage message, string option)
    {
        var overlay = new VisualElement(); SettingsFormStyle.Apply(overlay);
        overlay.AddToClassList("settings-confirmation-overlay");
        var dialog = new VisualElement(); dialog.AddToClassList("settings-confirmation-dialog");
        dialog.AddToClassList("used-car-filter-dialog");
        dialog.Add(SettingsFormStyle.Label("Continue in " + activeCareer.seriesName, "settings-group-heading"));
        dialog.Add(SettingsFormStyle.Label(activeCareer.teamName + " will continue into the " + message.seriesChangeYear +
            " season in " + activeCareer.seriesName + " with its current cars and employees. Sponsorships will be reset and fresh offers sent for the upcoming season.", "settings-note"));
        var errorSlot = SettingsFormStyle.ErrorSlot(null); dialog.Add(errorSlot);
        var actions = new VisualElement(); actions.AddToClassList("settings-footer");
        actions.style.justifyContent = Justify.FlexEnd;
        var cancel = SettingsFormStyle.Button("Cancel", Close);
        cancel.tooltip = "Return to the email without confirming a response.";
        var confirm = SettingsFormStyle.Button("Confirm Stay", null, primary: true);
        confirm.tooltip = "Continue into the next season with the current cars and employees.";
        confirm.clicked += () =>
        {
            confirm.SetEnabled(false);
            string error = StayInCurrentSeries(message, option);
            if (error == null) { Close(); return; }
            int index = dialog.IndexOf(errorSlot); errorSlot.RemoveFromHierarchy();
            errorSlot = SettingsFormStyle.ErrorSlot(error); dialog.Insert(index, errorSlot);
            confirm.SetEnabled(true);
        };
        actions.Add(cancel); actions.Add(confirm); dialog.Add(actions); overlay.Add(dialog);
        garageScreen.parent.Add(overlay); garageScreen.SetEnabled(false); cancel.Focus();
        overlay.RegisterCallback<KeyDownEvent>(evt =>
        { if (evt.keyCode == KeyCode.Escape) { Close(); evt.StopPropagation(); } });
        void Close()
        {
            overlay.RemoveFromHierarchy(); garageScreen.SetEnabled(true);
            garageScreen.Q<Button>("mailTab")?.Focus();
        }
    }

    private string StayInCurrentSeries(TeamMailMessage message, string option)
    {
        try
        {
            var next = activeCareer.CopyForTransaction();
            next.inbox.Find(mail => mail.eventId == message.eventId).scenarioResponse = option;
            if (next.schedule.season < message.seriesChangeYear)
            {
                var ranking = SeasonSponsorRanking(activeCareer.schedule);
                next.previousSeasonRankingYear = next.schedule.season;
                next.previousSeasonRank = ranking.Rank; next.previousSeasonFieldSize = ranking.FieldSize;
                catalog.Refresh();
                next.schedule = careerSetup.GenerateSeason(next, message.seriesChangeYear);
                next.completedSeasons = new List<SeasonSchedule>();
                if (!Career.TryReadDate(next.currentDate, out var date) || date.Year < message.seriesChangeYear)
                    next.SetSeasonStartDate();
                ScenarioMail.SendSeasonStart(next);
            }
            // Retain inventory, roster, and budget while renewing series sponsorship offers.
            ScenarioMail.ResetSeasonSponsorship(next);
            careerSetup.SaveProgress(next); ShowTeam(next);
            DisplayGaragePage(System.Array.FindIndex(garagePages, page => page.name == "schedulePage"));
            return null;
        }
        catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
        { return "Could not continue into the next season. " + ex.Message; }
    }

    private string SaveMailChange(System.Action rollback)
    {
        try
        {
            careerSetup.SaveProgress(activeCareer);
            return null;
        }
        catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
        {
            rollback();
            return "Could not save inbox changes. " + ex.Message;
        }
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
        bool showingSchedule = pageName == "schedulePage" || pageName == "standingsPage";
        bool showingHome = pageName == "homePage";
        bool showingFinances = pageName == "financesPage";
        bool showingCars = pageName == "carsPage";
        garageScreen.EnableInClassList("garage-shop-background", showingShop);
        garageScreen.EnableInClassList("garage-schedule-background", showingSchedule);
        garageScreen.EnableInClassList("garage-home-background", showingHome);
        garageScreen.EnableInClassList("garage-finances-background", showingFinances);
        garageScreen.EnableInClassList("garage-cars-background", showingCars);
        garageScreen.EnableInClassList("victory-results-background", false);
        var series = activeCareer?.SeriesRules;
        var imagePath = series?.BackgroundFor(pageName);
        bool usesCarsTourDefaults = series?.UsesCarsTourArtworkDefaults == true;
        var carsTourMailArt = usesCarsTourDefaults && pageName == "mailPage" ? SeriesSettingsStore.LoadArtwork(imagePath) : null;
        bool useTeamColorDesign = (pageName == "mailPage" && carsTourMailArt == null) || (showingFinances && !usesCarsTourDefaults);
        garageScreen.EnableInClassList("garage-team-color-background", useTeamColorDesign);
        if (useTeamColorDesign && activeCareer != null)
        {
            teamShopPreview.Dispose();
            GetTeamColorBackground();
            garageScreen.style.backgroundImage = teamColorBackground != null
                ? new StyleBackground(Background.FromRenderTexture(teamColorBackground))
                : new StyleBackground(teamColorDesign);
            return;
        }
        // The generated series-branding tiles are placeholders, not replacements
        // for the original Team page scenes and photographic backgrounds.
        bool originalPageBackground = showingShop || showingSchedule || showingHome || showingFinances || showingCars;
        bool bundledPlaceholder = new[] { "nascar-cup", "craftsman-truck", "oreilly-auto-parts", "tour-modified" }
            .Any(seriesId => imagePath == $"SeriesBranding/{seriesId}/{pageName}");
        var seriesImage = originalPageBackground && bundledPlaceholder ? null :
            carsTourMailArt ?? SeriesSettingsStore.LoadArtwork(imagePath);
        if (seriesImage != null)
        {
            teamShopPreview.Dispose();
            garageScreen.style.backgroundImage = new StyleBackground(seriesImage);
            return;
        }
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
                image = scheduleHauler.Render(activeCareer, ActiveSponsorName());
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

    private void GetTeamColorBackground()
    {
        var colors = activeCareer.TeamColors;
        string key = $"{activeCareer.savePath}|{activeCareer.teamName}|{colors.Primary}|{colors.Secondary}";
        if (key == teamColorBackgroundKey && teamColorDesign != null) return;
        if (teamColorBackground != null)
        {
            teamColorBackground.Release(); Destroy(teamColorBackground); teamColorBackground = null;
        }
        if (teamColorDesign != null) Destroy(teamColorDesign);
        teamColorBackgroundKey = key;
        uint hash = 2166136261;
        foreach (char character in key) hash = unchecked((hash ^ character) * 16777619);
        var random = new System.Random(unchecked((int)hash));
        const int width = 1024, height = 576;
        var shapes = new (float X, float Y, float Width, float Height, float Cos, float Sin, Color Color)[12];
        for (int i = 0; i < shapes.Length; i++)
        {
            float angle = (float)(random.NextDouble() * 1.1 - 0.55);
            shapes[i] = ((float)random.NextDouble(), (float)random.NextDouble(),
                0.12f + (float)random.NextDouble() * 0.65f,
                0.025f + (float)random.NextDouble() * 0.18f,
                Mathf.Cos(angle), Mathf.Sin(angle),
                Color.Lerp(colors.Primary, colors.Secondary, (float)random.NextDouble()));
        }
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float u = x / (float)width, v = y / (float)height;
            Color color = Color.Lerp(colors.Primary, colors.Secondary, Mathf.Clamp01(u * 0.65f + v * 0.35f));
            foreach (var shape in shapes)
            {
                float dx = u - shape.X, dy = v - shape.Y;
                float rx = dx * shape.Cos + dy * shape.Sin;
                float ry = -dx * shape.Sin + dy * shape.Cos;
                if (Mathf.Abs(rx) < shape.Width && Mathf.Abs(ry) < shape.Height)
                    color = Color.Lerp(color, shape.Color, 0.82f);
            }
            // Equivalent to a 50%-opaque black overlay, baked before the linear blur.
            color.r *= 0.5f; color.g *= 0.5f; color.b *= 0.5f; color.a = 1;
            pixels[y * width + x] = color;
        }
        teamColorDesign = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "Team colors Mail and Finances design", wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave
        };
        teamColorDesign.SetPixels32(pixels);
        teamColorDesign.Apply(false, true);
        var shader = Resources.Load<Shader>("Startup/ShopBackgroundBlur");
        if (shader == null || !shader.isSupported) return;
        teamColorBackground = new RenderTexture(width, height, 0)
        {
            name = "Team colors design, 16px blur, 50% darkening", hideFlags = HideFlags.HideAndDontSave
        };
        var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        try { TeamShopPreview.ApplyBackgroundBlur(teamColorDesign, teamColorBackground, material, 16); }
        finally { Destroy(material); }
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
                .Find(race.IsPlayerResult)?.winnings ?? 0);

        ShowAmount(garageScreen.Q<Label>("financeBalance"), activeCareer.teamBalance);
        var upcoming = races.FirstOrDefault(race => !race.completed);
        var upcomingCar = EventCar(upcoming) ?? activeCareer.cars.FirstOrDefault();
        var forecast = RaceExpenses.Generate(activeCareer.SeriesRules, activeCareer.EngineSupplierFor(upcomingCar));
        var chargedExpenses = races.Where(race => race.completed && !race.playerSkipped && race.expenses != null)
            .Select(race => race.expenses).ToList();
        long expenseTotal = chargedExpenses.Sum(expenses => (long)expenses.Total);
        ShowAmount(garageScreen.Q<Label>("financeCostPerRace"), -payroll -
            (upcoming != null ? forecast.Total : 0));
        var merchandise = Merchandising.Totals(activeCareer, season.season);
        long netIncome = merchandise.Sales - merchandise.Inventory + winnings + SeasonPrizes.Income(activeCareer) + season.sponsorIncome + season.carSales - season.payrollPaid - season.carPurchases -
            season.componentUpgrades - season.componentRepairs - expenseTotal +
            season.mysteryPalletSaleProceeds - season.mysteryPalletPurchases;
        var averageProfit = garageScreen.Q<Label>("financeAverageProfitPerRace");
        if (finished == 0)
        {
            ShowAmount(averageProfit, 0);
            averageProfit.text = "-";
        }
        else
            ShowAmount(averageProfit, (long)System.Math.Round((decimal)netIncome / finished,
                System.MidpointRounding.AwayFromZero));

        FinanceReport.Render(garageScreen.Q("financeBreakdown"), activeCareer, races, chargedExpenses, payroll, winnings, forecast);

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
        content.Add(SettingsFormStyle.Label($"{activeCareer.schedule.season} Results", "team-home-heading"));
        var events = (activeCareer.schedule.races ?? new List<ScheduledRace>())
            .Where(race => race.completed || race.sessions?.Any(session => session.Completed && session.DisplayResults?.Count > 0) == true)
            .OrderBy(race => race.date, System.StringComparer.Ordinal).ToList();
        if (events.Count == 0)
        {
            AddResultsMessage(content, "No session results recorded for this season yet.");
            RefreshResultsDetails(null);
            return;
        }

        int initialEvent = events.IndexOf(resultsEvent);
        if (initialEvent < 0)
            initialEvent = events.FindLastIndex(race => race.sessions?.Any(session => session.kind == RaceSessionKind.Race && session.Completed) == true);
        int selectedIndex = Mathf.Max(0, initialEvent);
        string EventLabel(ScheduledRace race) => $"{RaceDate(race)} - {race.trackName}";
        var selectors = new VisualElement();
        selectors.AddToClassList("results-selection-row");
        var pickerField = new VisualElement();
        pickerField.AddToClassList("settings-field");
        pickerField.Add(SettingsFormStyle.Label("Race event", "settings-note"));
        var eventPicker = SettingsFormStyle.Button(EventLabel(events[selectedIndex]), null);
        eventPicker.tooltip = "Choose a current-season event with recorded results.";
        pickerField.Add(eventPicker);
        var sessionContent = new VisualElement();
        sessionContent.AddToClassList("team-results-scroll");
        var sessionField = new VisualElement();
        sessionField.AddToClassList("settings-field");
        sessionField.Add(SettingsFormStyle.Label("Session", "settings-note"));
        var sessionPicker = SettingsFormStyle.Button("Race", null);
        sessionPicker.tooltip = "Choose a session from the selected event to view its results.";
        sessionField.Add(sessionPicker);
        selectors.Add(pickerField);
        selectors.Add(sessionField);
        content.Add(selectors);
        content.Add(sessionContent);
        SettingsFormStyle.CheckSelectorListSpacing(pickerField, sessionContent);
        RaceSession selectedSession = null;
        eventPicker.clicked += OpenPicker;
        sessionPicker.clicked += () =>
        {
            var sessions = events[selectedIndex].sessions ?? new List<RaceSession>();
            OpenSelection("Choose session", sessions.Select(SessionLabel).ToList(), sessions.IndexOf(selectedSession), index =>
            {
                SelectSession(sessions[index]);
            }, sessionPicker);
        };
        ResetSession();
        string SessionLabel(RaceSession session) => string.IsNullOrWhiteSpace(session.name) ? session.kind.ToString() : session.name;
        void ResetSession()
        {
            var race = events[selectedIndex];
            selectedSession = race.sessions?.Find(session => session.kind == RaceSessionKind.Race) ?? race.sessions?.FirstOrDefault();
            sessionPicker.text = selectedSession == null ? "No sessions" : SessionLabel(selectedSession);
            sessionPicker.SetEnabled(race.sessions?.Count > 0);
            ShowRaceResults(sessionContent, race, selectedSession, SelectSession);
        }

        void SelectSession(RaceSession session)
        {
            selectedSession = session;
            sessionPicker.text = SessionLabel(session);
            ShowRaceResults(sessionContent, events[selectedIndex], session, SelectSession);
        }

        void OpenPicker() => OpenSelection("Choose race event", events.Select(EventLabel).ToList(), selectedIndex, index =>
        {
            selectedIndex = index;
            eventPicker.text = EventLabel(events[index]);
            ResetSession();
        }, eventPicker);

        void OpenSelection(string title, List<string> labels, int selected, System.Action<int> choose, Button source)
        {
            var overlay = new VisualElement();
            SettingsFormStyle.Apply(overlay);
            overlay.AddToClassList("settings-confirmation-overlay");
            var dialog = new VisualElement();
            dialog.AddToClassList("settings-confirmation-dialog");
            dialog.style.backgroundColor = (Color)new Color32(16, 21, 30, 255);
            var trim = new StyleColor(new Color32(0, 155, 221, 255));
            dialog.style.borderTopColor = dialog.style.borderBottomColor = dialog.style.borderLeftColor = dialog.style.borderRightColor = trim;
            dialog.Add(SettingsFormStyle.Label(title, "settings-group-heading"));
            var list = new ScrollView(ScrollViewMode.Vertical)
            {
                verticalScrollerVisibility = ScrollerVisibility.Hidden,
                horizontalScrollerVisibility = ScrollerVisibility.Hidden
            };
            list.style.maxHeight = 330;
            for (int index = 0; index < labels.Count; index++)
            {
                int chosen = source == eventPicker ? labels.Count - 1 - index : index;
                var row = SettingsFormStyle.Button(labels[chosen], () => { choose(chosen); Close(); });
                row.tooltip = "View results for " + labels[chosen] + ".";
                row.EnableInClassList("settings-tab-current", chosen == selected);
                list.Add(row);
            }
            dialog.Add(list);
            var actions = new VisualElement();
            actions.AddToClassList("settings-footer");
            actions.Add(SettingsFormStyle.Button("Cancel", Close));
            dialog.Add(actions);
            overlay.Add(dialog);
            garageScreen.parent.Add(overlay);
            garageScreen.SetEnabled(false);
            list.Q<Button>()?.Focus();
            overlay.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape) return;
                Close(); evt.StopPropagation();
            });
            void Close()
            {
                overlay.RemoveFromHierarchy(); garageScreen.SetEnabled(true); source.Focus();
            }
        }
    }

    private void ShowRaceResults(VisualElement content, ScheduledRace race, RaceSession selectedSession = null, System.Action<RaceSession> selectSession = null)
    {
        content.Clear();
        RefreshResultsDetails(race, selectedSession, selectSession);
        var session = selectedSession ?? race.sessions?.Find(item => item.kind == RaceSessionKind.Race && item.Completed);
        if (session?.results?.Count > 0)
            content.Add(RaceEventView.CreateResultsGrid(session, activeCareer, EventGarageCar(race), scrollRows: true,
                resultTrack: catalog.Find(race.trackId)?.Definition));
        else
            AddResultsMessage(content, "No results recorded for this session.");
        settings?.RestorePalette();
    }

    private void RefreshResultsDetails(ScheduledRace race, RaceSession selectedSession = null, System.Action<RaceSession> selectSession = null)
    {
        var panel = garageScreen.Q("teamResultsDetails");
        panel.Clear();
        panel.Add(SettingsFormStyle.Label("Details", "team-home-heading"));
        var scroll = new ScrollView(ScrollViewMode.Vertical)
        {
            verticalScrollerVisibility = ScrollerVisibility.Hidden,
            horizontalScrollerVisibility = ScrollerVisibility.Hidden
        };
        scroll.AddToClassList("results-details-scroll");
        if (race == null)
        {
            panel.Add(SettingsFormStyle.Label("Select an event to view its details.", "team-home-note"));
            return;
        }
        var track = catalog.Find(race.trackId)?.Definition;
        var trackInfo = new VisualElement();
        trackInfo.AddToClassList("results-fixed-track-info");
        panel.Add(trackInfo);
        var identity = new VisualElement();
        identity.AddToClassList("team-home-track-identity");
        var logo = track == null ? null : SeriesSettingsStore.LoadArtwork(track.logo);
        if (logo != null)
        {
            var image = new Image { image = logo, scaleMode = ScaleMode.ScaleToFit };
            image.AddToClassList("team-home-track-logo"); identity.Add(image);
        }
        else identity.Add(SettingsFormStyle.Label("TRACK", "team-home-track-logo-placeholder"));
        var text = new VisualElement();
        text.style.flexGrow = 1; text.style.flexShrink = 1;
        text.Add(SettingsFormStyle.Label(race.trackName, "team-home-next-track-name"));
        text.Add(SettingsFormStyle.Label(RaceDate(race), "team-home-race-detail"));
        text.Add(SettingsFormStyle.Label(track == null ? $"{race.EffectiveRaceLaps} laps" :
            $"{race.EffectiveRaceLaps} laps - {race.EffectiveRaceLaps * track.lengthMiles:0.##} miles", "team-home-race-detail"));
        identity.Add(text); trackInfo.Add(identity);
        if (track != null)
        {
            var outline = new VisualElement(); outline.AddToClassList("results-details-track");
            trackInfo.Add(outline); new TrackOutlineView(outline).SetTrack(track);
        }
        var eventCar = EventGarageCar(race);
        var roster = TrackPreview.CreateTimingEntries(eventCar ?? activeCareer.cars[0], activeCareer);
        string chassisInfo = race.playerSkipped ? "Team did not participate" : eventCar != null
            ? $"Chassis {eventCar.chassisNumber} - {eventCar.manufacturer} - {eventCar.ChassisTypeName}"
            : !string.IsNullOrWhiteSpace(race.carChassisNumber)
                ? $"Chassis {race.carChassisNumber} - Car details unavailable"
                : "Event chassis not recorded";
        trackInfo.Add(SettingsFormStyle.Label(chassisInfo, "results-event-chassis"));
        panel.Add(scroll);
        foreach (var session in race.sessions ?? Enumerable.Empty<RaceSession>())
        {
            var summary = new VisualElement(); summary.AddToClassList("results-session-summary");
            summary.focusable = true;
            summary.tooltip = "Double-click to view " + (session.name ?? session.kind.ToString()) + " results.";
            summary.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.button != 0 || evt.clickCount != 2) return;
                selectSession?.Invoke(session); evt.StopPropagation();
            });
            summary.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter) return;
                selectSession?.Invoke(session); evt.StopPropagation();
            });
            summary.EnableInClassList("results-session-current", ReferenceEquals(session, selectedSession));
            var sessionTitle = SettingsFormStyle.Label(session.name ?? session.kind.ToString(), "results-session-name");
            summary.Add(sessionTitle);
            var body = new VisualElement(); body.AddToClassList("results-session-body");
            var performance = new VisualElement(); performance.AddToClassList("results-session-performance");
            body.Add(performance);
            summary.Add(body);
            var player = session.DisplayResults?.Find(race.IsPlayerResult);
            string length = session.minutes > 0 ? $"{session.minutes} minutes" :
                $"{(session.kind == RaceSessionKind.Race ? race.EffectiveRaceLaps : session.laps)} laps";
            sessionTitle.text = $"{session.name ?? session.kind.ToString()} - {length}";
            var results = session.DisplayResults ?? new List<SessionCarResult>();
            var leader = session.kind == RaceSessionKind.Race
                ? results.Where(result => result.position > 0).OrderBy(result => result.position).FirstOrDefault()
                : results.Where(result => result.lapTimes?.Count > 0 && result.FastestLap > 0 &&
                    !float.IsNaN(result.FastestLap) && !float.IsInfinity(result.FastestLap))
                    .OrderBy(result => result.FastestLap).ThenBy(result => result.position).FirstOrDefault();
            if (leader != null)
            {
                var driverLine = new VisualElement(); driverLine.AddToClassList("results-session-driver-line");
                var leaderNumber = SettingsFormStyle.Label($"#{leader.DisplayCarNumber}", "results-session-car-number");
                if (RaceEventView.ResultCar(leader, roster) is TeamCar leaderCar)
                    CarNumberFonts.Apply(leaderNumber, leaderCar.PaintStyle);
                driverLine.Add(leaderNumber);
                driverLine.Add(SettingsFormStyle.Label(leader.driverName ?? leader.carName, "results-session-driver"));
                if (session.kind != RaceSessionKind.Race)
                {
                    driverLine.Add(SettingsFormStyle.Label(RaceEventView.LapTime(leader.FastestLap), "results-session-lap-time"));
                    if (track != null && track.lengthMiles > 0 && leader.FastestLap > 0)
                        driverLine.Add(SettingsFormStyle.Label($"{track.lengthMiles * 3600f / leader.FastestLap:0.00} MPH", "results-session-lap-mph"));
                }
                performance.Add(driverLine);
            }
            if (player != null)
                performance.Add(SettingsFormStyle.Label($"{activeCareer.teamName} - {player.driverName ?? activeCareer.Driver?.FullName} - P{player.position}", "results-session-player"));
            else
                performance.Add(SettingsFormStyle.Label($"{activeCareer.teamName} - " +
                    (activeCareer.Driver is Driver driver ? $"{driver.FullName} - " : string.Empty) +
                    (race.playerSkipped ? "Did not participate" : "No result"), "team-home-race-detail"));
            var weather = new VisualElement(); weather.AddToClassList("results-weather");
            var recorded = session.weather;
            var info = new VisualElement();
            if (recorded?.IsValid == true)
            {
                int sky = recorded.cloudCover < 0.25f ? 0 : recorded.cloudCover < 0.75f ? 1 : 2;
                bool night = recorded.startHour < 6f || recorded.startHour >= 18f;
                weather.EnableInClassList("results-weather-night", night);
                var start = System.TimeSpan.FromHours(recorded.startHour);
                weather.tooltip = $"Recorded session start conditions: air {recorded.ambientFahrenheit:0.#}\u00B0F, " +
                    $"cloud cover {recorded.cloudCover:P0}, wind {recorded.windMph:0.#} mph, start {start.Hours:00}:{start.Minutes:00}.";
                var icon = new VisualElement(); icon.AddToClassList("results-weather-icon");
                icon.generateVisualContent += context => DrawResultsWeather(context.painter2D, sky);
                weather.Add(icon);
                info.AddToClassList("results-weather-line");
                info.Add(SettingsFormStyle.Label(new[] { "Sunny", "Partly Cloudy", "Cloudy" }[sky], "results-weather-description"));
                info.Add(SettingsFormStyle.Label($"{recorded.ambientFahrenheit:0.#}\u00B0F", "results-weather-temperature"));
            }
            else
            {
                info.Add(SettingsFormStyle.Label("Weather not recorded", "team-home-race-detail"));
                weather.tooltip = "This session has no saved weather conditions.";
            }
            weather.Add(info); body.Add(weather); scroll.Add(summary);
        }
    }

    private static void DrawResultsWeather(Painter2D painter, int sky)
    {
        if (sky < 2)
        {
            painter.strokeColor = new Color32(255, 211, 73, 255); painter.lineWidth = 2;
            painter.BeginPath();
            painter.MoveTo(new Vector2(24, 16));
            painter.Arc(new Vector2(16, 16), 8, Angle.Degrees(0), Angle.Degrees(360));
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI / 4;
                painter.MoveTo(new Vector2(16 + Mathf.Cos(angle) * 11, 16 + Mathf.Sin(angle) * 11));
                painter.LineTo(new Vector2(16 + Mathf.Cos(angle) * 15, 16 + Mathf.Sin(angle) * 15));
            }
            painter.Stroke();
        }
        if (sky > 0)
        {
            painter.fillColor = new Color32(187, 203, 218, 255);
            painter.BeginPath();
            painter.MoveTo(new Vector2(7, 32)); painter.LineTo(new Vector2(32, 32));
            painter.QuadraticCurveTo(new Vector2(42, 31), new Vector2(36, 22));
            painter.QuadraticCurveTo(new Vector2(34, 18), new Vector2(29, 20));
            painter.QuadraticCurveTo(new Vector2(21, 6), new Vector2(13, 21));
            painter.QuadraticCurveTo(new Vector2(1, 19), new Vector2(2, 28));
            painter.QuadraticCurveTo(new Vector2(2, 32), new Vector2(7, 32));
            painter.ClosePath(); painter.Fill();
        }
    }

    private static void AddResultsMessage(VisualElement content, string message)
    {
        var label = new Label(message);
        label.AddToClassList("garage-detail");
        content.Add(label);
    }

    private void BuildCareerPicker(VisualElement root)
    {
        careerPickerDialog = new VisualElement { name = "careerPickerDialog" };
        SettingsFormStyle.Apply(careerPickerDialog);
        careerPickerDialog.AddToClassList("settings-confirmation-overlay");
        careerPickerDialog.style.display = DisplayStyle.None;
        var dialog = new VisualElement();
        dialog.AddToClassList("settings-confirmation-dialog");
        dialog.style.width = 800;
        dialog.style.maxHeight = new Length(90, LengthUnit.Percent);
        dialog.Add(SettingsFormStyle.Label("Load Career", "settings-group-heading"));
        savedCareerList.RemoveFromHierarchy();
        savedCareerList.style.display = DisplayStyle.Flex;
        dialog.Add(savedCareerList);
        var actions = new VisualElement();
        actions.AddToClassList("settings-row");
        actions.AddToClassList("settings-confirmation-actions");
        var cancel = SettingsFormStyle.Button("Cancel", CloseSavedCareers);
        cancel.tooltip = "Close the saved career selection.";
        actions.Add(cancel);
        dialog.Add(actions);
        careerPickerDialog.Add(dialog);
        careerPickerDialog.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Escape) return;
            CloseSavedCareers();
            evt.StopPropagation();
        });
        root.Add(careerPickerDialog);
    }

    private void RefreshLoadCareerButton()
    {
        var details = titleScreen.Q<Label>("continueCareerDetails");
        try
        {
            var latest = careerSetup.ReadSavedCareers().OrderByDescending(entry => entry.LastSaved).FirstOrDefault();
            var visibility = latest == null ? DisplayStyle.None : DisplayStyle.Flex;
            titleScreen.Q("continueCareerControls").style.display = visibility;
            details.style.display = visibility;
            loadCareerButton.style.display = visibility;
            loadCareerButton.SetEnabled(latest != null);
            continueCareerButton.SetEnabled(latest != null);
            int seasonYear = latest?.SeasonYear ?? 0;
            string season = seasonYear > 0 ? seasonYear.ToString() : "Season unknown";
            details.text = latest == null ? string.Empty : $"{latest.teamName} - {season} {latest.seriesName}";
            continueCareerButton.tooltip = latest == null ? "No saved career to continue." :
                $"Continue {latest.teamName}, your most recently saved career.";
        }
        catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
        {
            titleScreen.Q("continueCareerControls").style.display = DisplayStyle.None;
            details.style.display = DisplayStyle.None;
            loadCareerButton.style.display = DisplayStyle.Flex;
            loadCareerButton.SetEnabled(true);
            continueCareerButton.SetEnabled(false);
            Debug.LogWarning("Could not list saved careers. " + ex.Message);
        }
    }

    private void ContinueCareer()
    {
        try
        {
            var latest = careerSetup.ReadSavedCareers().OrderByDescending(entry => entry.LastSaved).FirstOrDefault();
            if (latest == null)
            {
                RefreshLoadCareerButton();
                return;
            }
            LoadSavedCareer(latest);
            if (garageScreen.style.display.value != DisplayStyle.Flex)
            {
                titleScreen.SetEnabled(false);
                careerPickerDialog.style.display = DisplayStyle.Flex;
            }
        }
        catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
        {
            ToggleSavedCareers();
            ShowSavedCareers("Could not continue the career. " + ex.Message);
        }
    }

    private void ToggleSavedCareers()
    {
        ShowSavedCareers();
        titleScreen.SetEnabled(false);
        careerPickerDialog.style.display = DisplayStyle.Flex;
        careerPickerDialog.BringToFront();
        savedCareerList.Query<Button>(className: "title-career-load").First()?.Focus();
    }

    private void CloseSavedCareers()
    {
        careerPickerDialog.style.display = DisplayStyle.None;
        titleScreen.SetEnabled(true);
        RefreshLoadCareerButton();
        if (loadCareerButton.enabledSelf)
            loadCareerButton.Focus();
        else
            newCareerButton.Focus();
    }

    private void ShowSavedCareers(string error = null)
    {
        savedCareerList.Clear();
        if (error != null)
            savedCareerList.Add(SettingsFormStyle.ErrorSlot(error));

        IReadOnlyList<CareerSetup.SavedCareer> entries;
        try
        {
            entries = careerSetup.ReadSavedCareers();
        }
        catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
        {
            savedCareerList.Add(SettingsFormStyle.ErrorSlot("Could not list saved careers. " + ex.Message));
            return;
        }

        if (entries.Count == 0)
        {
            savedCareerList.Add(SettingsFormStyle.Label("No saved careers. Create a new career to get started.", "settings-note"));
            return;
        }

        foreach (var entry in entries)
        {
            var row = new VisualElement();
            row.AddToClassList("title-career-row");
            var team = SettingsFormStyle.Button(string.Empty, () => LoadSavedCareer(entry));
            team.AddToClassList("title-career-load");
            team.tooltip = $"Load {entry.teamName}. Last saved {entry.LastSaved.ToLocalTime():g}.";
            var car = entry.cars?.FirstOrDefault() ?? new TeamCar { number = 27 };
            car.modelResource = RacingSeries.ForId(entry.seriesId).ModelFor(car.manufacturer);
            string sponsor = entry.primarySponsor?.offer == null ? null :
                Sponsorships.CompanyName(entry.primarySponsor.offer.companyId);
            var thumbnail = new Image
            {
                image = carThumbnails.Get(car, sponsor == null ? System.Array.Empty<string>() : new[] { sponsor },
                    cacheScope: entry.Path, transparentBackground: true),
                scaleMode = ScaleMode.ScaleToFit,
                pickingMode = PickingMode.Ignore
            };
            thumbnail.AddToClassList("title-career-car");
            team.Add(thumbnail);

            var details = new VisualElement { pickingMode = PickingMode.Ignore };
            details.AddToClassList("title-career-details");
            string season = entry.SeasonYear > 0 ? entry.SeasonYear.ToString() : "Season unknown";
            AddCareerLabel(details, entry.teamName, "title-career-name");
            AddCareerLabel(details, $"{season} - {entry.seriesName}", "title-career-detail");
            AddCareerLabel(details, $"Saved {entry.LastSaved.ToLocalTime():g}", "title-career-detail");
            team.Add(details);
            var trash = SettingsFormStyle.Button("", () => ConfirmCareerDeletion(entry));
            trash.AddToClassList("title-career-delete");
            trash.tooltip = $"Delete the saved career for {entry.teamName}.";
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("track-icon-art");
            trash.Add(icon);
            DrawButtonIcon(trash, DrawCareerTrashIcon);
            row.Add(team);
            row.Add(trash);
            savedCareerList.Add(row);
        }
        savedCareerList.scrollOffset = Vector2.zero;
    }

    private static void AddCareerLabel(VisualElement details, string text, string style)
    {
        var label = SettingsFormStyle.Label(text, style);
        label.pickingMode = PickingMode.Ignore;
        details.Add(label);
    }

    private void LoadSavedCareer(CareerSetup.SavedCareer entry)
    {
        Career career;
        try
        {
            career = careerSetup.LoadSavedCareer(entry);
        }
        catch (System.Exception ex) when (ex is System.ArgumentException || ex is IOException || ex is System.UnauthorizedAccessException)
        {
            ShowSavedCareers("Could not load the career. " + ex.Message);
            return;
        }
        CloseSavedCareers();
        ShowTeam(career);
    }

    private void ConfirmCareerDeletion(CareerSetup.SavedCareer entry)
    {
        var overlay = new VisualElement { name = "deleteCareerConfirmation" };
        SettingsFormStyle.Apply(overlay);
        overlay.AddToClassList("settings-confirmation-overlay");
        var dialog = new VisualElement();
        dialog.AddToClassList("settings-confirmation-dialog");
        overlay.Add(dialog);

        var heading = SettingsFormStyle.Label("Delete career", "settings-group-heading");
        heading.AddToClassList("google-sans-semibold");
        dialog.Add(heading);
        dialog.Add(SettingsFormStyle.Label(
            $"Permanently delete the saved career for {entry.teamName}? This cannot be undone.",
            "settings-note"));
        var error = SettingsFormStyle.Label("", "settings-validation-summary");
        dialog.Add(error);
        var actions = new VisualElement();
        actions.AddToClassList("settings-row");
        actions.AddToClassList("settings-confirmation-actions");
        dialog.Add(actions);

        var cancel = SettingsFormStyle.Button("Cancel", Close);
        cancel.tooltip = "Close this dialog and keep the saved career.";
        actions.Add(cancel);
        var delete = SettingsFormStyle.Button("Delete Career", () =>
        {
            try
            {
                careerSetup.DeleteSavedCareer(entry);
            }
            catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
            {
                SettingsFormStyle.SetValidationMessage(error, "Could not delete the career. " + ex.Message, true);
                return;
            }

            if (string.Equals(activeCareer?.savePath, entry.Path, System.StringComparison.OrdinalIgnoreCase))
                activeCareer = null;
            ShowSavedCareers();
            Close();
        });
        delete.tooltip = "Permanently remove this career save.";
        actions.Add(delete);
        overlay.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Escape)
                return;
            Close();
            evt.StopPropagation();
        });
        careerPickerDialog.SetEnabled(false);
        titleScreen.parent.Add(overlay);
        cancel.Focus();

        void Close()
        {
            overlay.RemoveFromHierarchy();
            careerPickerDialog.SetEnabled(true);
            savedCareerList.Query<Button>(className: "title-career-delete").ToList()
                .FirstOrDefault(button => button.tooltip == $"Delete the saved career for {entry.teamName}.")
                ?.Focus();
            if (savedCareerList.Query<Button>(className: "title-career-load").First() == null)
                CloseSavedCareers();
        }
    }

    internal static void DrawTrashButtonIcon(Button button)
    {
        var icon = new VisualElement { pickingMode = PickingMode.Ignore };
        icon.AddToClassList("track-icon-art");
        icon.style.width = 22;
        icon.style.height = 22;
        button.Add(icon);
        DrawButtonIcon(button, DrawCareerTrashIcon);
    }

    private static void DrawCareerTrashIcon(Painter2D painter)
    {
        painter.MoveTo(new Vector2(3, 6));
        painter.LineTo(new Vector2(19, 6));
        painter.MoveTo(new Vector2(8, 6));
        painter.LineTo(new Vector2(8, 3));
        painter.LineTo(new Vector2(14, 3));
        painter.LineTo(new Vector2(14, 6));
        painter.MoveTo(new Vector2(5, 6));
        painter.LineTo(new Vector2(6, 19));
        painter.LineTo(new Vector2(16, 19));
        painter.LineTo(new Vector2(17, 6));
        painter.MoveTo(new Vector2(9, 9));
        painter.LineTo(new Vector2(9, 16));
        painter.MoveTo(new Vector2(13, 9));
        painter.LineTo(new Vector2(13, 16));
    }

    private void OpenCareerSetup()
    {
        CloseSavedCareers();
        titleScreen.style.display = DisplayStyle.None;
        careerSetup.View.style.display = DisplayStyle.Flex;
        careerSetup.Open();
    }

    private void CloseCareerSetup()
    {
        if (careerSetup.IsSeriesChangeSetup)
        {
            careerSetup.ClosePreview(); careerSetup.View.style.display = DisplayStyle.None;
            garageScreen.style.display = DisplayStyle.Flex;
            DisplayGaragePage(System.Array.FindIndex(garagePages, page => page.name == "mailPage"));
            return;
        }
        RefreshLoadCareerButton();
        careerSetup.ClosePreview();
        careerSetup.View.style.display = DisplayStyle.None;
        titleScreen.style.display = DisplayStyle.Flex;
        newCareerButton.Focus();
    }

    private static void ApplyCareerCarModels(Career career)
    {
        var rules = career.SeriesRules;
        foreach (var car in career.AllCars)
            car.modelResource = rules.ModelFor(car.manufacturer);
    }

    private void ShowTeam(Career career)
    {
        largeCarPreviews.Dispose();
        var random = new System.Random();
        bool careerChanged = career.InitializeCars(random);
        careerChanged |= career.InitializeEmployees(random);
        careerChanged |= career.AllCars.Any(car => string.IsNullOrEmpty(car.chassisNumber));
        careerSetup.View.style.display = DisplayStyle.None;
        chassisManufacturers = career.SeriesRules.ManufacturerChoices;
        CancelFireEmployee();
        SetEmployeeStatus(null);
        RefreshDriverLabel(career);
        activeCareer = career;
        careerChanged |= ScenarioMail.RefreshSeriesChangeChoices(career);
        careerChanged |= RivalRosterStore.ApplyToCareer(career);
        ApplyCareerCarModels(career);
        careerChanged |= career.MigrateSponsorship();
        DisplayEmployees(career);
        catalog.Refresh();
        selectedEvent = null;
        careerChanged |= InitializeFinances(career);
        if (career.schedule == null || career.schedule.season <= 0 ||
            ((career.schedule.races?.Count ?? 0) == 0 && catalog.Entries.Count > 0))
        {
            career.schedule = careerSetup.GenerateSeason(career, career.schedule?.season > 0
                ? career.schedule.season : System.DateTime.Today.Year);
            careerChanged = true;
        }
        if (career.schedule.races != null)
        {
            bool noStages = !career.SeriesRules.usesStages;
            foreach (var race in career.schedule.races.Where(race => !race.completed && race.noStages != noStages))
            {
                race.noStages = noStages;
                race.EnsureStages(career.SeriesRules.stagePercentages);
                careerChanged = true;
            }
            foreach (var race in career.schedule.races.Where(race => race.purse <= 0))
            {
                var track = catalog.Find(race.trackId);
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

        if (Career.TryReadDate(career.currentDate, out var offerDate) && offerDate.DayOfYear == 1)
            careerChanged |= Merchandising.SendOffer(career, offerDate);

        if (string.IsNullOrEmpty(career.socialStartDate))
        {
            career.socialStartDate = career.currentDate;
            careerChanged = true;
        }
        careerChanged |= TeamCommunications.AddDriverWelcome(career);
        careerChanged |= CrewChiefMail.SendPreview(career, catalog, RaceCarBlockReason);
        careerChanged |= ScenarioMail.SendTestToExistingCareer(career);
        if (career.schedule?.races != null)
        {
            var (rank, fieldSize) = PreviousSeasonSponsorRanking();
            careerChanged |= ScenarioMail.SendSeasonSponsorship(career, rank, fieldSize, test: true);
        }
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
            AddArrestStatus(card, driver.arrest, career.currentDate);
            var traits = AddStaffRatings(card);
            AddDriverRatings(traits, driver, includePublicity: true);
            AddStaffLine(card, $"{RaceFinances.Dollars(driver.annualSalary)} per year");
            AddFireButton(card, "Fire Driver", driver.FullName, career.drivers, driver);
            roster.Add(card);
        }
        foreach (var employee in career.employees)
        {
            var card = new VisualElement();
            card.AddToClassList("employee-card");
            AddEmployeeCardDetails(card, employee, liveryNumber);
            AddArrestStatus(card, employee.arrest, career.currentDate);
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
        button.AddToClassList("settings-button");
        button.tooltip = "Dismiss " + name + " after confirmation.";
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
        foreach (var component in CarComponentDisplay.Order)
        {
            float value = employee.RepairSkill(component);
            string title = component == CarComponent.Aerodynamics ? "Aero" : component.ToString();
            AddStaffRating(skills, title, value, value.ToString("F2"), ShopComponentPanel.ComponentColor(component));
        }
        AddMoraleRating(skills, employee.EffectiveMorale(activeCareer?.currentDate));
        AddStaffDetails(card, employee.annualSalary, employee.age, employee.yearsExperience);
    }

    private static void AddArrestStatus(VisualElement card, StaffArrest arrest, string careerDate)
    {
        if (arrest?.IsActiveOn(careerDate) == true)
            AddStaffLine(card, "Unavailable until " + arrest.releaseDate);
    }

    private static void AddStaffIdentity(VisualElement card, string name, string role)
    {
        card.AddToClassList("team-home-panel");
        SettingsFormStyle.StylePanel(card);
        var nameLabel = SettingsFormStyle.Label(name, "team-home-heading");
        nameLabel.pickingMode = PickingMode.Ignore;
        card.Add(nameLabel);
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

    private static void AddDriverRatings(VisualElement ratings, Driver driver, bool includePublicity = false)
    {
        var color = new Color32(104, 195, 255, 255);
        AddStaffRating(ratings, "Short Track", driver.shortTrackXp / (float)Driver.MaximumTrackXp, $"{driver.shortTrackXp} XP", color, gradient: true);
        AddStaffRating(ratings, "Intermediate", driver.intermediateXp / (float)Driver.MaximumTrackXp, $"{driver.intermediateXp} XP", color, gradient: true);
        AddStaffRating(ratings, "Superspeedway", driver.superspeedwayXp / (float)Driver.MaximumTrackXp, $"{driver.superspeedwayXp} XP", color, gradient: true);
        AddStaffRating(ratings, "Road Course", driver.roadCourseXp / (float)Driver.MaximumTrackXp, $"{driver.roadCourseXp} XP", color, gradient: true);
        var divider = new VisualElement { pickingMode = PickingMode.Ignore };
        divider.AddToClassList("employee-morale-divider"); ratings.Add(divider);
        float aggression = driver.personalityTraits?.aggression ?? 0.5f;
        AddStaffRating(ratings, "Aggression", aggression, $"{aggression:P0}", new Color32(255, 193, 99, 255));
        AddMoraleRating(ratings, driver.morale, addDivider: false);
        if (includePublicity)
        {
            AddStaffRating(ratings, "Social Media XP", driver.SocialMediaXp, $"{driver.SocialMediaXp:P0}", color);
            AddStaffRating(ratings, "Charisma", driver.Charisma, $"{driver.Charisma:P0}", color);
        }
    }

    private static void AddMoraleRating(VisualElement ratings, float morale, bool addDivider = true)
    {
        if (addDivider)
        {
            var divider = new VisualElement { pickingMode = PickingMode.Ignore };
            divider.AddToClassList("employee-morale-divider"); ratings.Add(divider);
        }
        AddStaffRating(ratings, "Morale", morale, $"{morale:P0}", new Color32(106, 211, 210, 255));
    }

    private static void AddStaffRating(VisualElement ratings, string title, float value, string formattedValue, Color color, CarComponent? componentIcon = null, bool gradient = false)
    {
        var row = new VisualElement();
        row.AddToClassList("employee-skill-row");
        row.tooltip = title + ": " + formattedValue;
        if (componentIcon.HasValue)
        {
            var slot = new VisualElement { tooltip = row.tooltip };
            slot.AddToClassList("home-component-icon-slot");
            var icon = ShopComponentPanel.ComponentIcon(componentIcon.Value);
            icon.pickingMode = PickingMode.Ignore; slot.Add(icon); row.Add(slot);
        }
        else
        {
            var name = new Label(title);
            name.AddToClassList("employee-skill-name"); row.Add(name);
        }

        var track = new VisualElement { tooltip = $"{title} {formattedValue}" };
        track.AddToClassList("employee-skill-track");
        var fill = new VisualElement { pickingMode = PickingMode.Ignore };
        fill.AddToClassList("employee-skill-fill");
        fill.style.width = Length.Percent(Mathf.Clamp01(value) * 100f);
        fill.style.backgroundColor = color;
        if (gradient) ComponentWearGradient.Apply(fill, value, reverse: true);
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

    private static Employee CreateRecruit(EmployeeRole role, System.Random random, RacingSeries series)
    {
        var candidate = Employee.Create(role, random, series: series);
        candidate.recruitLiveryNumber = random.Next(27, 27 + TrackPreview.CarCount);
        return candidate;
    }

    private bool recruitingDrivers;
    private int recruitmentRoleFilter;
    private int recruitmentSort;
    private static readonly string[] RecruitmentSortLabels =
        { "Salary: Low to High", "Salary: High to Low", "Skill: High to Low", "Skill: Low to High" };

    private void OpenDriverRecruitment()
    {
        if (HasDriver(activeCareer) || !activeCareer.CanHireDriver)
            return;

        recruitingDrivers = true;
        activeCareer.driverCandidates ??= new List<Driver>();
        var candidates = activeCareer.driverCandidates;
        int previousCount = candidates.Count;
        var random = new System.Random();
        var previousExperience = candidates.Select(driver => (Driver: driver, driver.shortTrackXp, driver.intermediateXp,
            driver.superspeedwayXp, driver.roadCourseXp, driver.trackExperienceInitialized)).ToArray();
        bool experienceChanged = false;
        foreach (var driver in candidates) experienceChanged |= driver.EnsureCandidateTrackExperience(random);
        while (candidates.Count < 10)
            candidates.Add(Driver.Create(random, null, series: activeCareer.SeriesRules));

        string message = null;
        if (candidates.Count != previousCount || experienceChanged)
        {
            try
            {
                careerSetup.SaveProgress(activeCareer);
            }
            catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
            {
                candidates.RemoveRange(previousCount, candidates.Count - previousCount);
                foreach (var previous in previousExperience)
                {
                    previous.Driver.shortTrackXp = previous.shortTrackXp; previous.Driver.intermediateXp = previous.intermediateXp;
                    previous.Driver.superspeedwayXp = previous.superspeedwayXp; previous.Driver.roadCourseXp = previous.roadCourseXp;
                    previous.Driver.trackExperienceInitialized = previous.trackExperienceInitialized;
                }
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
        var roles = career.SeriesRules.StaffRoles;
        recruitmentRoleFilter = 0;
        foreach (var role in roles)
        {
            int available = career.recruitCandidates.Count(candidate => candidate.role == role);
            for (int i = available; i < candidatesPerRole; i++)
                career.recruitCandidates.Add(CreateRecruit(role, random, career.SeriesRules));
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
        garageScreen.Q("recruitmentPage").EnableInClassList("employee-recruitment-page", !recruitingDrivers);
        garageScreen.Q("recruitmentPage").EnableInClassList("driver-recruitment-page", recruitingDrivers);
        garageScreen.Q("recruitmentFilterContainer").style.display = recruitingDrivers ? DisplayStyle.None : DisplayStyle.Flex;
        garageScreen.Q<Label>("recruitmentHeading").text = recruitingDrivers ? "Hire Driver" : "Hire Employee";
        garageScreen.Q("garageFooter").SetEnabled(false);
        DisplayRecruitment(message, resetScroll: true);
        garageScreen.Q<Button>("recruitmentBackButton").Focus();
    }

    private void OpenRecruitmentRoleSelection()
    {
        var roles = activeCareer.SeriesRules.StaffRoles;
        var choices = new[] { "All positions" }.Concat(roles.Select(Employee.NameOf)).ToArray();
        var overlay = new VisualElement(); SettingsFormStyle.Apply(overlay); overlay.AddToClassList("settings-confirmation-overlay");
        var dialog = new VisualElement(); dialog.AddToClassList("settings-confirmation-dialog"); dialog.AddToClassList("used-car-filter-dialog");
        dialog.Add(SettingsFormStyle.Label("Choose Employee Position", "settings-group-heading"));
        var list = new ScrollView { verticalScrollerVisibility = ScrollerVisibility.Hidden, horizontalScrollerVisibility = ScrollerVisibility.Hidden };
        list.style.maxHeight = 360;
        for (int i = 0; i < choices.Length; i++)
        {
            int index = i;
            var row = SettingsFormStyle.Button(choices[i], () => { recruitmentRoleFilter = index; DisplayRecruitment(resetScroll: true); Close(); });
            row.AddToClassList("recruitment-role-choice"); row.EnableInClassList("settings-tab-current", index == recruitmentRoleFilter);
            row.tooltip = index == 0 ? "Show candidates for every role available in this series." : "Show candidates for " + choices[i] + ".";
            list.Add(row);
        }
        dialog.Add(list);
        var footer = new VisualElement(); footer.AddToClassList("settings-footer");
        var cancel = SettingsFormStyle.Button("Cancel", Close); cancel.tooltip = "Keep the current position filter.";
        footer.Add(cancel); dialog.Add(footer); overlay.Add(dialog); garageScreen.parent.Add(overlay);
        garageScreen.SetEnabled(false); list.Q<Button>()?.Focus();
        overlay.RegisterCallback<KeyDownEvent>(evt => { if (evt.keyCode == KeyCode.Escape) { Close(); evt.StopPropagation(); } });
        void Close() { overlay.RemoveFromHierarchy(); garageScreen.SetEnabled(true); garageScreen.Q<Button>("recruitmentPositionSelect").Focus(); }
    }

    private void OpenRecruitmentSortSelection()
    {
        var overlay = new VisualElement(); SettingsFormStyle.Apply(overlay); overlay.AddToClassList("settings-confirmation-overlay");
        var dialog = new VisualElement(); dialog.AddToClassList("settings-confirmation-dialog"); dialog.AddToClassList("used-car-filter-dialog");
        dialog.Add(SettingsFormStyle.Label("Sort Candidates", "settings-group-heading"));
        dialog.Add(SettingsFormStyle.Label(recruitingDrivers ? "Driver skill is the average experience across the four track types." :
            "Overall skill is the average of the five displayed component skills.", "settings-note"));
        for (int i = 0; i < RecruitmentSortLabels.Length; i++)
        {
            int index = i;
            var choice = SettingsFormStyle.Button(RecruitmentSortLabels[i], () => { recruitmentSort = index; DisplayRecruitment(resetScroll: true); Close(); });
            choice.AddToClassList("recruitment-role-choice"); choice.EnableInClassList("settings-tab-current", index == recruitmentSort);
            choice.tooltip = index < 2 ? "Order candidates by annual salary." : "Order candidates by their overall skill.";
            dialog.Add(choice);
        }
        var footer = new VisualElement(); footer.AddToClassList("settings-footer");
        var cancel = SettingsFormStyle.Button("Cancel", Close); cancel.tooltip = "Keep the current candidate order.";
        footer.Add(cancel); dialog.Add(footer); overlay.Add(dialog); garageScreen.parent.Add(overlay); garageScreen.SetEnabled(false);
        dialog.Q<Button>()?.Focus();
        overlay.RegisterCallback<KeyDownEvent>(evt => { if (evt.keyCode == KeyCode.Escape) { Close(); evt.StopPropagation(); } });
        void Close() { overlay.RemoveFromHierarchy(); garageScreen.SetEnabled(true); garageScreen.Q<Button>("recruitmentSortSelect").Focus(); }
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
        var roles = activeCareer.SeriesRules.StaffRoles;
        int filterIndex = recruitmentRoleFilter;
        if (filterIndex < 0 || filterIndex > roles.Length) filterIndex = recruitmentRoleFilter = 0;
        garageScreen.Q<Button>("recruitmentPositionSelect").text = filterIndex == 0 ? "All positions" : Employee.NameOf(roles[filterIndex - 1]);
        var candidates = recruitingDrivers ? System.Array.Empty<Employee>() : activeCareer.recruitCandidates
            .Where(candidate => roles.Contains(candidate.role) &&
                (filterIndex <= 0 || filterIndex > roles.Length || candidate.role == roles[filterIndex - 1]))
            .OrderBy(candidate => candidate.role).ThenBy(candidate => candidate.FullName).ToArray();

        var drivers = recruitingDrivers ? activeCareer.driverCandidates.OrderBy(candidate => candidate.FullName).ToArray() : System.Array.Empty<Driver>();
        candidates = RecruitmentSorting.Order(candidates, recruitmentSort, employee => employee.annualSalary,
            employee => TeamCar.Components.Average(component => employee.RepairSkill(component)), employee => employee.FullName);
        drivers = RecruitmentSorting.Order(drivers, recruitmentSort, driver => driver.annualSalary,
            driver => (driver.shortTrackXp + driver.intermediateXp + driver.superspeedwayXp + driver.roadCourseXp) / 4f, driver => driver.FullName);
        garageScreen.Q<Button>("recruitmentSortSelect").text = RecruitmentSortLabels[recruitmentSort];
        var status = garageScreen.Q<Label>("recruitmentStatus");
        bool canHire = recruitingDrivers ? activeCareer.CanHireDriver : activeCareer.CanHireEmployee;
        status.text = message ?? (!canHire ? "Staff limit reached." : candidates.Length + drivers.Length == 0 ? "No candidates available." : string.Empty);
        status.style.display = DisplayStyle.Flex;
        status.style.visibility = string.IsNullOrEmpty(status.text) ? Visibility.Hidden : Visibility.Visible;

        var heading = MakeRow();
        heading.AddToClassList("results-grid-header");
        heading.Add(new VisualElement());
        heading[0].AddToClassList("recruitment-photo-cell");
        AddCell(heading, "Candidate", "recruitment-candidate");
        if (!recruitingDrivers)
            AddCell(heading, "Position", "recruitment-role");
        AddCell(heading, recruitingDrivers ? "Driver attributes" : "Skills", "recruitment-skills");
        AddCell(heading, "Annual salary", "recruitment-salary");
        var actionSpace = new VisualElement();
        actionSpace.AddToClassList("recruitment-action");
        heading.Add(actionSpace);
        var columnHeadings = garageScreen.Q("recruitmentColumnHeadings");
        columnHeadings.Clear(); columnHeadings.Add(heading);

        foreach (var driver in drivers)
        {
            var portrait = CreateStaffPortrait(driver.FullName, driver.age, driver.gender, driver.hometownCity,
                driver.hometownState, activeCareer.cars[0].number, "recruitment-portrait", driver.ethnicity);
            AddCandidate(portrait, driver.FullName, driver.age, driver.hometownCity, driver.hometownState,
                null, skills => AddDriverRatings(skills, driver), driver.annualSalary, () => HireDriver(driver), driver.yearsExperience);
        }

        foreach (var candidate in candidates)
        {
            AddCandidate(CreateEmployeePortrait(candidate, candidate.recruitLiveryNumber, "recruitment-portrait"),
                candidate.FullName, candidate.age, candidate.hometownCity, candidate.hometownState,
                candidate.RoleName, skills =>
                {
                    foreach (var component in CarComponentDisplay.Order)
                    {
                        float value = candidate.RepairSkill(component);
                        string name = component == CarComponent.Aerodynamics ? "Bodywork" : component.ToString();
                        AddStaffRating(skills, name, value, value.ToString("F2"), ShopComponentPanel.ComponentColor(component), component);
                    }
                }, candidate.annualSalary, () => HireEmployee(candidate));
        }

        roster.schedule.Execute(() => roster.scrollOffset = scroll);
        settings?.RestorePalette();

        void AddCandidate(Image portrait, string name, int age, string city, string state,
            string role, System.Action<VisualElement> drawSkills, long salary, System.Action hireCandidate, int? yearsExperience = null)
        {
            var row = MakeRow();
            var photo = new VisualElement();
            photo.AddToClassList("recruitment-photo-cell");
            if (portrait != null)
                photo.Add(portrait);
            row.Add(photo);
            string experience = yearsExperience.HasValue ? "\nExperience: " + yearsExperience.Value + " years" : "";
            AddCell(row, $"{name}\nAge {age}{experience}\n{city}, {state}", "recruitment-candidate");
            if (role != null)
                AddCell(row, role, "recruitment-role");
            var skills = new VisualElement();
            skills.AddToClassList("results-grid-cell"); skills.AddToClassList("recruitment-skills");
            drawSkills(skills); row.Add(skills);
            AddCell(row, RaceFinances.Dollars(salary), "recruitment-salary");
            var action = new VisualElement();
            action.AddToClassList("recruitment-action");
            var hire = SettingsFormStyle.Button("Hire", hireCandidate);
            hire.tooltip = canHire ? "Hire " + name + " for " + RaceFinances.Dollars(salary) + " per year." : "The series staff limit has been reached.";
            hire.SetEnabled(canHire);
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
        if (HasDriver(career) || !career.CanHireDriver)
            return;

        int index = career.driverCandidates.IndexOf(candidate);
        if (index < 0)
            return;

        career.driverCandidates[index] = Driver.Create(new System.Random(), null, series: career.SeriesRules);
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
        if (index < 0 || !career.CanHireEmployee || !career.SeriesRules.StaffRoles.Contains(candidate.role))
            return;

        var replacement = CreateRecruit(candidate.role, new System.Random(), career.SeriesRules);
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

    private const string RaceDriverRequiredMessage = "Hire or assign a driver before entering or simulating a race.";

    private static bool HasDriver(Career career) => career?.Driver != null;

    private static bool DriverInCustody(Career career) =>
        career?.Driver?.arrest?.IsActiveOn(career.currentDate) == true;

    private static bool CanFieldDriver(Career career) => HasDriver(career) && !DriverInCustody(career);

    private static string DriverEntryMessage(Career career) => DriverInCustody(career)
        ? "Your driver is unavailable until " + career.Driver.arrest.releaseDate + "."
        : RaceDriverRequiredMessage;

    private void RefreshDriverLabel(Career career)
    {
        RefreshEmployeeFooterBadge(career);
        garageScreen.Q<Button>("recruitDriverButton").SetEnabled(!HasDriver(career) && career.CanHireDriver);
        garageScreen.Q<Button>("recruitEmployeeButton").SetEnabled(career.CanHireEmployee);
    }

    private static bool InitializeFinances(Career career)
    {
        if (career.financesInitialized)
            return false;
        career.teamBalance = RaceFinances.StartingBalance;
        career.financesInitialized = true;
        return true;
    }

    private void SetNextWeekVisible(bool show)
    {
        var week = garageScreen.Q<Button>("nextWeekButton");
        var toggle = garageScreen.Q<Button>("toggleNextWeekButton");
        week.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        toggle.text = show ? "\u25BE" : "\u25B4";
        toggle.tooltip = show ? "Hide Next Week." : "Show Next Week.";
        week.parent.parent.EnableInClassList("week-option-open", show);
    }

    private void ConfirmAdvanceCareerDate(int days)
    {
        if (activeCareer == null || !Career.TryReadDate(activeCareer.currentDate, out var date) ||
            date > System.DateTime.MaxValue.Date.AddDays(-days)) return;
        if (PaySeasonPrizesOnNextDay()) return;
        if (ShowRequiredEmailTooltip()) { RefreshHomeDashboard(); return; }
        var target = date.AddDays(days);
        var races = activeCareer.schedule.races.Where(race => !race.completed &&
            Career.TryReadDate(race.date, out var raceDay) && raceDay < target).ToArray();
        bool needsDriver = races.Length > 0 && !HasDriver(activeCareer);
        string action = days == 1 ? "Next Day" : "Next Week";
        string sourceButton = days == 1 ? "nextDayButton" : "toggleNextWeekButton";
        var overlay = new VisualElement();
        SettingsFormStyle.Apply(overlay);
        overlay.AddToClassList("settings-confirmation-overlay");
        var dialog = new VisualElement();
        dialog.AddToClassList("settings-confirmation-dialog");
        dialog.style.backgroundColor = (Color)new Color32(16, 21, 30, 255);
        var trim = new StyleColor(new Color32(0, 155, 221, 255));
        dialog.style.borderTopColor = dialog.style.borderBottomColor = dialog.style.borderLeftColor = dialog.style.borderRightColor = trim;
        dialog.Add(SettingsFormStyle.Label(needsDriver ? "Driver Required" : $"Advance to {action}?", "settings-group-heading"));
        if (needsDriver)
            dialog.Add(SettingsFormStyle.Label(RaceDriverRequiredMessage, "settings-note"));
        else if (races.Length > 0)
        {
            string events = string.Join("\n", races.Select(race => $"{race.trackName} ({RaceDate(race)})"));
            string warning = "Your team will skip these events. Employee payroll will still be deducted.";
            if (DriverInCustody(activeCareer)) warning = DriverEntryMessage(activeCareer) + "\n" + warning;
            var note = SettingsFormStyle.Label(warning + "\n\n" + events, "settings-note");
            note.style.whiteSpace = WhiteSpace.Normal;
            dialog.Add(note);
        }
        var actions = new VisualElement();
        actions.AddToClassList("settings-footer");
        actions.style.justifyContent = Justify.FlexEnd;
        var cancel = SettingsFormStyle.Button("Cancel", Close);
        cancel.tooltip = "Stay on the current career day.";
        actions.Add(cancel);
        var confirm = SettingsFormStyle.Button(needsDriver ? "Employees" : action, null, primary: true);
        confirm.tooltip = needsDriver ? "Open Employees to fill the driver vacancy." : $"Confirm advancing the career by {days} {(days == 1 ? "day" : "days")}.";
        confirm.clicked += () =>
        {
            confirm.SetEnabled(false);
            Close();
            if (needsDriver)
            {
                int employees = System.Array.FindIndex(garagePages, page => page.name == "employeesPage");
                DisplayGaragePage(employees);
                garageTabs[employees].Focus();
                return;
            }
            if (days > 1) AdvanceCareerWeek(date, target, races);
            else if (races.Length == 0) AdvanceCareerDay();
            else
            {
                garageScreen.Q("skipRacePrompt").style.display = DisplayStyle.Flex;
                ConfirmSkipRace();
            }
        };
        actions.Add(confirm);
        dialog.Add(actions); overlay.Add(dialog);
        garageScreen.parent.Add(overlay);
        garageScreen.SetEnabled(false);
        cancel.Focus();
        overlay.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Escape) return;
            Close(); evt.StopPropagation();
        });
        void Close()
        {
            overlay.RemoveFromHierarchy(); garageScreen.SetEnabled(true);
            garageScreen.Q<Button>(sourceButton).Focus();
        }
    }

    private bool SendSeriesChangeOnNextDay(System.DateTime nextDate)
    {
        var draft = activeCareer.CopyForTransaction(); draft.SetDate(nextDate);
        if (!ScenarioMail.SendSeriesChangeIfDue(draft, nextDate)) return false;
        if (Career.TryReadDate(activeCareer.currentDate, out var previousDate))
            ScenarioMail.SendSolicitations(draft, previousDate, nextDate);
        try
        {
            careerSetup.SaveProgress(draft); ShowTeam(draft);
            DisplayGaragePage(System.Array.FindIndex(garagePages, page => page.name == "mailPage"));
        }
        catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
        { Debug.LogWarning("Could not save the series selection email. " + ex.Message); }
        return true;
    }

    private bool PaySeasonPrizesOnNextDay()
    {
        if (!SeasonPrizes.PayPending(activeCareer, out var rollback)) return false;
        try { careerSetup.SaveProgress(activeCareer); }
        catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
        {
            rollback(); Debug.LogWarning("Could not save the season prize payment. " + ex.Message);
        }
        RefreshTeamBalance(); RefreshHomeDashboard();
        if (garageScreen.Q("financesPage").style.display == DisplayStyle.Flex) DisplayFinances();
        if (garageScreen.Q("mailPage").style.display == DisplayStyle.Flex)
            teamMail.Show(garageScreen.Q("mailPage"), activeCareer, settings.RestorePalette, DeleteSocialPosts,
                MarkMailRead, DeleteMail, RespondToScenario, SocialDriverPortrait);
        return true;
    }

    private async void AdvanceCareerWeek(System.DateTime start, System.DateTime target, ScheduledRace[] confirmedRaces)
    {
        var confirmed = confirmedRaces.Select(race => (race.trackId, race.date)).ToHashSet();
        var overlay = new VisualElement();
        SettingsFormStyle.Apply(overlay);
        overlay.AddToClassList("settings-confirmation-overlay");
        var dialog = new VisualElement();
        dialog.AddToClassList("settings-confirmation-dialog");
        dialog.AddToClassList("used-car-filter-dialog");
        dialog.Add(SettingsFormStyle.Label("Advancing Career Week", "settings-group-heading"));
        var wheel = TrackIndicatorIcon(tireWear: false);
        wheel.style.width = wheel.style.height = 40;
        wheel.style.alignSelf = Align.Center;
        dialog.Add(wheel);
        var status = SettingsFormStyle.Label("Preparing the next day...", "settings-note");
        status.style.whiteSpace = WhiteSpace.Normal;
        dialog.Add(status);
        var progress = new ProgressBar { lowValue = 0, highValue = 7 };
        progress.AddToClassList("settings-operation-progress");
        dialog.Add(progress);
        var actions = new VisualElement();
        actions.AddToClassList("settings-footer");
        actions.style.justifyContent = Justify.FlexEnd;
        bool cancelled = false;
        var cancel = SettingsFormStyle.Button("Cancel", () => cancelled = true);
        cancel.tooltip = "Stop the advance. Days already saved are kept.";
        actions.Add(cancel);
        dialog.Add(actions);
        overlay.Add(dialog);
        garageScreen.parent.Add(overlay);
        garageScreen.SetEnabled(false);
        cancel.Focus();
        overlay.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode == KeyCode.Escape) { cancelled = true; evt.StopPropagation(); }
        });

        string notice = null;
        string checkpoint = null;
        float angle = 0f;
        try
        {
            await Awaitable.NextFrameAsync(destroyCancellationToken);
            while (!cancelled && Career.TryReadDate(activeCareer.currentDate, out var date) && date < target)
            {
                if (ScenarioMail.HasPending(activeCareer))
                {
                    notice = RequiredEmailMessage;
                    break;
                }
                if (PaySeasonPrizesOnNextDay())
                {
                    notice = "Season prizes processed. Select Next Week to continue.";
                    break;
                }

                int days = (date - start).Days;
                progress.value = days;
                progress.title = $"Day {days + 1} of 7";
                status.text = date.AddDays(1).ToString("D", CultureInfo.CurrentCulture);
                var race = DueRace(date);
                if (race == null)
                {
                    var season = activeCareer.schedule;
                    if (season.races.Count > 0 && season.races.All(item => item.completed) &&
                        new System.DateTime(season.season + 1, 1, 1) > date.AddDays(1))
                    {
                        notice = "The season has ended. Use Next Day to start the new season.";
                        break;
                    }
                    AdvanceCareerDay(refresh: false);
                    if (Career.TryReadDate(activeCareer.currentDate, out var advanced) && advanced <= date)
                    {
                        notice = "The career date could not advance.";
                        break;
                    }
                }
                else
                {
                    if (!confirmed.Contains((race.trackId, race.date)))
                    {
                        notice = "Another event is due. Select Next Week to review it.";
                        break;
                    }

                    bool success = false;
                    var tomorrow = date.AddDays(1);
                    bool anotherRaceDue = activeCareer.schedule.races.Any(item => item != race && !item.completed &&
                        Career.TryReadDate(item.date, out var raceDate) && raceDate <= date);
                    checkpoint = JsonUtility.ToJson(activeCareer);
                    var steps = SimulateEventSteps(race, anotherRaceDue ? date : tomorrow,
                        (ok, message) => { success = ok; notice = message; }, () => cancelled,
                        message => status.text = race.trackName + " - " + message,
                        reportProgress: fraction => progress.value = days + fraction, calendarLimit: tomorrow);
                    while (steps.MoveNext())
                    {
                        angle = (angle + 12f) % 360f;
                        wheel.style.rotate = new Rotate(new Angle(angle));
                        await Awaitable.NextFrameAsync(destroyCancellationToken);
                    }
                    if (!success) break;
                    checkpoint = null;
                }
                angle = (angle + 12f) % 360f;
                wheel.style.rotate = new Rotate(new Angle(angle));
                await Awaitable.NextFrameAsync(destroyCancellationToken);
            }
        }
        catch (System.OperationCanceledException) { cancelled = true; }
        catch (System.Exception ex)
        {
            notice = "Could not advance the week. " + ex.Message;
            Debug.LogException(ex);
        }
        finally
        {
            if (checkpoint != null)
            {
                JsonUtility.FromJsonOverwrite(checkpoint, activeCareer);
                ApplyCareerCarModels(activeCareer);
                if (selectedEvent != null)
                    selectedEvent = activeCareer.schedule.races.Find(item => item.trackId == selectedEvent.trackId &&
                        item.date == selectedEvent.date);
            }
            overlay.RemoveFromHierarchy();
            garageScreen.SetEnabled(true);
        }
        if (this == null) return;
        RefreshAfterCareerAdvance();
        if (cancelled) notice = "Week advance stopped. Saved days were kept.";
        if (!string.IsNullOrEmpty(notice)) ShowCareerAdvanceNotice(notice);
        garageScreen.Q<Button>("toggleNextWeekButton").Focus();
    }

    private void AdvanceCareerDay(bool refresh = true)
    {
        if (activeCareer == null || !Career.TryReadDate(activeCareer.currentDate, out var date) ||
            date == System.DateTime.MaxValue.Date)
            return;

        if (ShowRequiredEmailTooltip())
        {
            RefreshHomeDashboard();
            return;
        }

        if (SendSeriesChangeOnNextDay(date.AddDays(1))) return;

        if (activeCareer.schedule.races.Count > 0 && activeCareer.schedule.races.All(item => item.completed))
        {
            var previousCareer = activeCareer;
            try
            {
                var ranking = SeasonSponsorRanking(activeCareer.schedule);
                var next = activeCareer.CopyForTransaction();
                next.previousSeasonRankingYear = next.schedule.season;
                next.previousSeasonRank = ranking.Rank; next.previousSeasonFieldSize = ranking.FieldSize;
                next.schedule = careerSetup.GenerateSeason(next, next.schedule.season + 1);
                next.completedSeasons = new List<SeasonSchedule>(); next.SetSeasonStartDate();
                ScenarioMail.SendSeasonStart(next);
                if (Career.TryReadDate(next.currentDate, out var seasonStart))
                    ScenarioMail.SendSolicitations(next, date, seasonStart);
                careerSetup.SaveProgress(next); ShowTeam(next);
                DisplayGaragePage(System.Array.FindIndex(garagePages, page => page.name == "schedulePage"));
            }
            catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
            { activeCareer = previousCareer; Debug.LogWarning("Could not start the next season. " + ex.Message); }
            return;
        }
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
        var undoMerchandising = activeCareer.SetDate(date.AddDays(1));
        var undoRepairs = UpdateCareerRepairs();
        try
        {
            ScenarioMail.SendNewYear(activeCareer, date, date.AddDays(1));
            ScenarioMail.SendSolicitations(activeCareer, date, date.AddDays(1));
            SendSeasonSponsorshipIfDue(date.AddDays(1));
            CrewChiefMail.SendPreview(activeCareer, catalog, RaceCarBlockReason);
            careerSetup.SaveProgress(activeCareer);
        }
        catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
        {
            undoRepairs?.Invoke();
            undoMerchandising?.Invoke();
            activeCareer.currentDate = previous;
            CrewChiefMail.UndoNewMessages(activeCareer, inboxCount);
            Debug.LogWarning("Could not save the next day. " + ex.Message);
        }
        if (refresh) RefreshAfterCareerAdvance();
    }

    private void RefreshAfterCareerAdvance()
    {
        RefreshCareerDate(refreshHome: false);
        RefreshTeamBalance();
        if (garageScreen.Q("financesPage").style.display == DisplayStyle.Flex)
            DisplayFinances();
        if (garageScreen.Q("shopPage").style.display == DisplayStyle.Flex)
            RefreshShopCarPicker();
        if (garageScreen.Q("employeesPage").style.display == DisplayStyle.Flex)
        {
            if (showingWorkInProgress) DisplayEmployeeWorkInProgress();
            else DisplayEmployees(activeCareer);
        }
        if (garageScreen.Q("schedulePage").style.display == DisplayStyle.Flex)
            RefreshSchedule();
        if (garageScreen.Q("carsPage").style.display == DisplayStyle.Flex)
            RefreshTeamCars();
        var mailPage = garageScreen.Q("mailPage");
        if (mailPage.style.display == DisplayStyle.Flex)
        {
            teamMail.Show(mailPage, activeCareer, settings.RestorePalette, DeleteSocialPosts,
                MarkMailRead, DeleteMail, RespondToScenario, SocialDriverPortrait);
            settings.RestorePalette();
        }
        // Rebuild Home after all daily repair, mail, and visible-page updates are finished.
        RefreshHomeDashboard();
    }

    private IVisualElementScheduledItem hideRequiredEmailTooltip;
    private const string RequiredEmailMessage = "Read required emails and respond to pending scenarios before continuing.";

    private bool ShowRequiredEmailTooltip()
    {
        if (!ScenarioMail.HasPending(activeCareer))
            return false;

        ShowCareerAdvanceNotice(RequiredEmailMessage);
        return true;
    }

    private void ShowCareerAdvanceNotice(string message)
    {
        var tooltip = garageScreen.Q<Label>("requiredEmailTooltip");
        tooltip.text = message;
        tooltip.style.display = DisplayStyle.Flex;
        hideRequiredEmailTooltip?.Pause();
        hideRequiredEmailTooltip = tooltip.schedule.Execute(() => tooltip.style.display = DisplayStyle.None)
            .StartingIn(4000);
    }

    private ScheduledRace DueRace(System.DateTime date)
    {
        var next = activeCareer?.schedule?.races?.FirstOrDefault(race => !race.completed);
        return Career.TryReadDate(next?.date, out var raceDate) && raceDate <= date ? next : null;
    }

    private void SendSeasonSponsorshipIfDue(System.DateTime date)
    {
        if (date.Year < activeCareer.schedule.season || ScenarioMail.HasSeasonSponsorship(activeCareer))
            return;
        var (rank, fieldSize) = PreviousSeasonSponsorRanking();
        ScenarioMail.SendSeasonSponsorship(activeCareer, rank, fieldSize);
    }

    private (int Rank, int FieldSize) PreviousSeasonSponsorRanking()
    {
        if (activeCareer.previousSeasonRankingYear == activeCareer.schedule.season - 1)
            return (activeCareer.previousSeasonRank, activeCareer.previousSeasonFieldSize);
        var previous = activeCareer.completedSeasons?.Where(season => season.season < activeCareer.schedule.season)
            .OrderByDescending(season => season.season).FirstOrDefault();
        return SeasonSponsorRanking(previous);
    }

    private (int Rank, int FieldSize) SeasonSponsorRanking(SeasonSchedule previous)
    {
        if (previous == null || activeCareer.cars.Count == 0) return (0, 0);
        var standings = GetStandings(previous);
        var best = standings.FirstOrDefault(entry => entry.Key.IsPlayer);
        int rank = best.Value > 0 ? standings.FindIndex(entry => entry.Value == best.Value) + 1 : standings.Count;
        return (rank, standings.Count);
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

        if (ScenarioMail.HasPending(activeCareer))
        {
            CloseSkipRacePrompt();
            ShowRequiredEmailTooltip();
            return;
        }

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
        if (garageScreen.Q("carsPage").style.display == DisplayStyle.Flex)
            RefreshTeamCars();
        settings?.RestorePalette();
    }

    private void RefreshCareerDate(bool refreshHome = true)
    {
        bool valid = Career.TryReadDate(activeCareer.currentDate, out var date);
        garageScreen.Q<Label>("teamDayOfWeek").text = valid ? date.ToString("dddd", CultureInfo.CurrentCulture) : string.Empty;
        garageScreen.Q<Label>("teamDate").text = valid ? date.ToString("d", CultureInfo.CurrentCulture) : string.Empty;
        var nextDay = garageScreen.Q<Button>("nextDayButton");
        nextDay.SetEnabled(valid && date < System.DateTime.MaxValue.Date);
        garageScreen.Q<Button>("nextWeekButton").SetEnabled(valid && date <= System.DateTime.MaxValue.Date.AddDays(-7));
        garageScreen.Q<Label>("requiredEmailTooltip").style.display = DisplayStyle.None;
        if (refreshHome) RefreshHomeDashboard();
    }

    private void RefreshEmployeeFooterBadge(Career career)
    {
        var button = garageScreen?.Q<Button>("employeesTab"); if (button == null) return;
        var badge = button.Q<Label>("employeeDriverRequiredBadge");
        if (badge == null)
        {
            badge = new Label("!") { name = "employeeDriverRequiredBadge", pickingMode = PickingMode.Ignore };
            badge.AddToClassList("employee-driver-required-badge"); button.hierarchy.Add(badge);
        }
        bool missing = career != null && !HasDriver(career);
        badge.style.display = missing ? DisplayStyle.Flex : DisplayStyle.None;
        button.tooltip = missing ? RaceDriverRequiredMessage : "Manage, hire, and assign employees and drivers.";
    }

    private void RefreshMailFooterBadge()
    {
        var button = garageScreen?.Q<Button>("mailTab");
        if (button == null) return;
        var badge = button.Q<Label>("mailResponseBadge");
        if (badge == null)
        {
            badge = new Label("!") { name = "mailResponseBadge", pickingMode = PickingMode.Ignore };
            badge.AddToClassList("mail-response-badge");
            button.hierarchy.Add(badge);
        }
        bool pending = activeCareer?.inbox?.Any(mail => mail != null && mail.BlocksAdvancement) == true;
        badge.style.display = pending ? DisplayStyle.Flex : DisplayStyle.None;
        button.tooltip = pending ? "Open Mail. An email requires reading or a response." : "Read team mail and respond to required messages.";
    }

    private void RefreshTeamHeader()
    {
        RefreshEmployeeFooterBadge(activeCareer);
        RefreshMailFooterBadge();
        garageScreen.Q<Label>("teamHeaderName").text = activeCareer.teamName;
        garageScreen.Q<Label>("teamHeaderSeriesTitle").text = careerSetup.SeriesName(activeCareer);
        var logo = garageScreen.Q<Image>("teamHeaderSeriesLogo");
        logo.scaleMode = ScaleMode.ScaleToFit;
        logo.image = activeCareer.SeriesRules.Logo;
        logo.style.display = logo.image == null ? DisplayStyle.None : DisplayStyle.Flex;
        garageScreen.Q("teamHeaderSeriesPlaceholder").style.display =
            logo.image == null ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void RefreshTeamBalance()
    {
        garageScreen.Q<Label>("teamHeaderBalance").text = RaceFinances.Dollars(activeCareer.teamBalance);
    }

    private void ShowTitle()
    {
        largeCarPreviews.Dispose();
        RefreshLoadCareerButton();
        garageScreen.style.display = DisplayStyle.None;
        titleScreen.style.display = DisplayStyle.Flex;
        if (continueCareerButton.enabledSelf)
            continueCareerButton.Focus();
        else
            newCareerButton.Focus();
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
        {
            var card = TeamCarCard.Create(car, largeCarPreviews.Get(car, sponsors, transparentBackground: true), activeCareer.cars.Count > 1,
                () => ShowCarPaint(car), () => ConfirmCarSale(car), () =>
                {
                    shopCarPicker.SelectCar(car);
                    DisplayGaragePage(System.Array.FindIndex(garagePages, page => page.name == "shopPage"));
                    garageScreen.Q("shopCarPicker").Q<Button>()?.Focus();
                });
            card.userData = car; card.focusable = true;
            card.EnableInClassList("car-panel-selected", car == selectedCarsChassis);
            card.RegisterCallback<PointerDownEvent>(_ =>
            {
                selectedCarsChassis = car;
                foreach (var sibling in rows.Children())
                    sibling.EnableInClassList("car-panel-selected", sibling == card);
            });
            rows.Add(card);
        }
        rows.schedule.Execute(() => rows.scrollOffset = scroll);
        bool inventoryFull = activeCareer.cars.Count >= TeamCar.MaximumOwned;
        addTeamCarButton.style.display = DisplayStyle.Flex;
        addTeamCarButton.SetEnabled(!inventoryFull);
        addTeamCarButton.tooltip = inventoryFull
            ? $"Your team owns the maximum of {TeamCar.MaximumOwned} chassis. Sell a chassis before purchasing another."
            : "Browse used chassis or purchase a new chassis for your team.";
        settings?.RestorePalette();
    }

    private void ConfirmCarSale(TeamCar car)
    {
        if (activeCareer.cars.Count <= 1 || !activeCareer.cars.Contains(car)) return;
        long price = UsedCarListing.TradeInPrice(car, activeCareer.SeriesRules);
        string amount = RaceFinances.Dollars(price);
        var overlay = new VisualElement();
        SettingsFormStyle.Apply(overlay);
        overlay.AddToClassList("settings-confirmation-overlay");
        var dialog = new VisualElement();
        dialog.AddToClassList("settings-confirmation-dialog");
        dialog.style.backgroundColor = (Color)new Color32(16, 21, 30, 255);
        var trim = new StyleColor(new Color32(0, 155, 221, 255));
        dialog.style.borderTopColor = dialog.style.borderBottomColor = dialog.style.borderLeftColor = dialog.style.borderRightColor = trim;
        dialog.Add(SettingsFormStyle.Label("Sell Chassis", "settings-group-heading"));
        dialog.Add(SettingsFormStyle.Label($"Sell chassis {car.chassisNumber} for {amount}?", "settings-note"));
        if (car.repairs?.Count > 0)
            dialog.Add(SettingsFormStyle.Label("The sale ends any repairs and upgrades in progress on this chassis.", "settings-note"));
        var error = SettingsFormStyle.Label("", "settings-validation-summary");
        error.style.display = DisplayStyle.None;
        dialog.Add(error);
        var actions = new VisualElement();
        actions.AddToClassList("settings-footer"); actions.style.justifyContent = Justify.FlexEnd;
        actions.style.marginTop = 6;
        actions.style.paddingTop = 6;
        var cancel = SettingsFormStyle.Button("Cancel", Close);
        cancel.tooltip = "Keep this chassis and return to Cars.";
        var sell = SettingsFormStyle.Button("Sell for " + amount, null, primary: true);
        sell.tooltip = "Confirm the sale and add the proceeds to your team's balance.";
        sell.clicked += () =>
        {
            sell.SetEnabled(false);
            string failure = SellCar(car, price);
            if (failure == null) Close();
            else
            {
                error.style.display = DisplayStyle.Flex;
                SettingsFormStyle.SetValidationMessage(error, failure, true);
                sell.SetEnabled(true);
            }
        };
        actions.Add(cancel); actions.Add(sell); dialog.Add(actions); overlay.Add(dialog);
        garageScreen.parent.Add(overlay); garageScreen.SetEnabled(false); cancel.Focus();
        overlay.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Escape) return;
            Close(); evt.StopPropagation();
        });
        void Close()
        {
            overlay.RemoveFromHierarchy(); garageScreen.SetEnabled(true); garageTabs[2].Focus();
        }
    }

    private string SellCar(TeamCar car, long price)
    {
        var career = activeCareer;
        int index = career.cars.IndexOf(car);
        if (index < 0 || career.cars.Count <= 1)
            return "This chassis cannot be sold.";

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
            return "Could not save the sale. " + ex.Message;
        }

        RefreshTeamCars();
        RefreshTeamBalance();
        RefreshSchedule();
        var status = garageScreen.Q<Label>("carsStatus");
        status.text = $"Sold chassis {car.chassisNumber} for {RaceFinances.Dollars(price)}.";
        status.style.display = DisplayStyle.Flex;
        return null;
    }


    private ScrollView OpenCarOptions(string title)
    {
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
        CarPaintEditor.Open(garageScreen.parent, car, ActiveSponsorNames(), _ =>
        {
            try { careerSetup.SaveProgress(activeCareer); return null; }
            catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
            {
                return "Could not save paint scheme. " + ex.Message;
            }
        }, RefreshTeamCars);
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

    private void OpenCarMaintenance(TeamCar car, CarComponent component)
    {
        maintenanceComponent = component; maintenanceEmployee = null;
        var overlay = garageScreen.Q("carMaintenancePage");
        maintenanceUnderlayStates.Clear();
        foreach (var child in garageScreen.Children())
        {
            if (child == overlay) continue;
            maintenanceUnderlayStates[child] = child.enabledSelf; child.SetEnabled(false);
        }
        DisplayCarMaintenance(car);
        overlay.style.display = DisplayStyle.Flex; overlay.BringToFront();
        garageScreen.Q<Button>("carMaintenanceBackButton").Focus();
    }

    private void CloseCarMaintenance()
    {
        garageScreen.Q("carMaintenancePage").style.display = DisplayStyle.None;
        foreach (var state in maintenanceUnderlayStates) state.Key.SetEnabled(state.Value);
        maintenanceUnderlayStates.Clear(); maintenanceComponent = null; maintenanceEmployee = null;
        RefreshShopCarPicker();
        garageScreen.Q("shopCarPicker").Q<Button>()?.Focus();
    }

    private void DisplayCarMaintenance(TeamCar car, string message = "")
    {
        if (!maintenanceComponent.HasValue) return;
        var component = maintenanceComponent.Value;
        garageScreen.Q<Label>("carMaintenanceTitle").text = $"{CarComponentDisplay.Name(component)} Repair";
        var content = garageScreen.Q("carMaintenanceComponents"); content.Clear();
        var status = garageScreen.Q<Label>("carMaintenanceStatus"); status.text = string.IsNullOrEmpty(message) ? " " : message;
        SettingsFormStyle.SetValidationMessage(status, status.text, message.StartsWith("Could not", System.StringComparison.Ordinal));
        bool validDate = Career.TryReadDate(activeCareer.currentDate, out var date);
        var repair = car.repairs?.Find(item => item.component == component);
        if (repair != null)
        {
            content.Add(SettingsFormStyle.Label("Assigned to " + repair.AssigneeDisplay, "settings-note"));
            content.Add(SettingsFormStyle.Label(validDate ? "Time remaining: " + ComponentRepair.FormatDuration(repair.RemainingMinutes(date)) :
                "Due: " + repair.completionDate, "settings-note"));
            var cancel = SettingsFormStyle.Button("Cancel Repair", () => CancelComponentRepair(car, repair));
            cancel.tooltip = "Cancel the assigned repair and keep the repair progress already completed.";
            cancel.SetEnabled(validDate); content.Add(cancel);
        }
        else if (car.ComponentWear(component) > 0f)
        {
            var employees = activeCareer.employees.Where(CanAssignRepair).OrderByDescending(employee => employee.RepairSkill(component)).ToList();
            if (maintenanceEmployee != null && !employees.Contains(maintenanceEmployee)) maintenanceEmployee = null;
            int cost = ComponentRepair.CostForWear(car.ComponentWear(component), activeCareer.SeriesRules.maximumComponentRepairCost);
            int minutes = maintenanceEmployee == null ? ComponentRepair.HoursForWear(car.ComponentWear(component)) * 60 :
                ComponentRepair.EstimateMinutes(car.ComponentWear(component), maintenanceEmployee, component, activeCareer.currentDate);
            content.Add(SettingsFormStyle.Label("Assign employee", "settings-note"));
            var picker = SettingsFormStyle.Button(maintenanceEmployee?.FullName ?? (employees.Count == 0 ? "No available employees" : "Select employee"),
                () => OpenRepairEmployeeSelection(car, component, employees));
            picker.name = "repairEmployeeSelect";
            picker.tooltip = "Choose an available mechanic or specialist. Skill and morale determine repair time.";
            picker.SetEnabled(employees.Count > 0); content.Add(picker);
            content.Add(SettingsFormStyle.Label("Repair cost: " + RaceFinances.Dollars(cost), "settings-note"));
            content.Add(SettingsFormStyle.Label("Duration: " + ComponentRepair.FormatDuration(minutes), "settings-note"));
            var start = SettingsFormStyle.Button("Start Repair", () =>
            {
                if (maintenanceEmployee != null && Career.TryReadDate(activeCareer.currentDate, out var current))
                    StartComponentRepair(car, component, maintenanceEmployee, current);
            }, primary: true);
            start.tooltip = "Pay for this repair and assign the selected employee.";
            start.SetEnabled(maintenanceEmployee != null && validDate && activeCareer.teamBalance >= cost &&
                date <= System.DateTime.MaxValue.Date.AddDays(-ComponentRepair.CareerDaysForMinutes(minutes)));
            content.Add(start);
            if (employees.Count == 0) content.Add(SettingsFormStyle.ErrorSlot("No employees are available for this repair."));
            else if (activeCareer.teamBalance < cost) content.Add(SettingsFormStyle.ErrorSlot("The repair exceeds the team budget."));
        }
        else content.Add(SettingsFormStyle.Label("No repair is needed.", "settings-note"));
        settings?.RestorePalette();
    }

    private void OpenRepairEmployeeSelection(TeamCar car, CarComponent component, List<Employee> employees)
    {
        var maintenance = garageScreen.Q("carMaintenancePage");
        var overlay = new VisualElement(); SettingsFormStyle.Apply(overlay); overlay.AddToClassList("settings-confirmation-overlay");
        var dialog = new VisualElement(); dialog.AddToClassList("settings-confirmation-dialog"); dialog.AddToClassList("used-car-filter-dialog");
        dialog.Add(SettingsFormStyle.Label("Assign Repair Employee", "settings-group-heading"));
        var list = new ScrollView { verticalScrollerVisibility = ScrollerVisibility.Hidden, horizontalScrollerVisibility = ScrollerVisibility.Hidden };
        list.style.maxHeight = 300;
        foreach (var employee in employees)
        {
            int minutes = ComponentRepair.EstimateMinutes(car.ComponentWear(component), employee, component, activeCareer.currentDate);
            var choice = SettingsFormStyle.Button(employee.FullName + " - " + employee.RoleName + "\nSkill " + employee.RepairSkill(component).ToString("F2") +
                " - " + ComponentRepair.FormatDuration(minutes), () => { maintenanceEmployee = employee; DisplayCarMaintenance(car); Close(); });
            choice.AddToClassList("recruitment-role-choice"); choice.style.whiteSpace = WhiteSpace.Normal;
            choice.EnableInClassList("settings-tab-current", maintenanceEmployee == employee);
            choice.tooltip = "Assign " + employee.FullName + " to this repair. Estimated duration: " + ComponentRepair.FormatDuration(minutes) + ".";
            list.Add(choice);
        }
        dialog.Add(list); var footer = new VisualElement(); footer.AddToClassList("settings-footer");
        var cancel = SettingsFormStyle.Button("Cancel", Close); cancel.tooltip = "Keep the current assignment selection.";
        footer.Add(cancel); dialog.Add(footer); overlay.Add(dialog); garageScreen.parent.Add(overlay);
        maintenance.SetEnabled(false); list.Q<Button>()?.Focus();
        overlay.RegisterCallback<KeyDownEvent>(evt => { if (evt.keyCode == KeyCode.Escape) { Close(); evt.StopPropagation(); } });
        void Close() { overlay.RemoveFromHierarchy(); maintenance.SetEnabled(true); maintenance.Q<Button>("repairEmployeeSelect")?.Focus(); }
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
        car.ComponentWear(repair.component) = repair.isUpgrade ? previousWear : previousWear * remaining;
        car.repairs.RemoveAt(index);
        SaveMaintenance(car, () =>
        {
            car.ComponentWear(repair.component) = previousWear;
            car.repairs.Insert(index, repair);
        }, $"{repair.component} repair cancelled. Wear is now {car.ComponentWear(repair.component):P0}.");
    }

    private bool CanAssignRepair(Employee employee) =>
        employee.arrest?.IsActiveOn(activeCareer.currentDate) != true &&
        (employee.role == EmployeeRole.GeneralMechanic ||
            employee.RoleName.Contains("Specialist", System.StringComparison.Ordinal)) &&
        !activeCareer.cars.Any(car => car.repairs?.Any(repair => repair.IsAssignedTo(employee)) == true);

    private void StartComponentRepair(TeamCar car, CarComponent component, Employee employee, System.DateTime date)
    {
        car.repairs ??= new List<ComponentRepair>();
        float wear = car.ComponentWear(component);
        int cost = ComponentRepair.CostForWear(wear, activeCareer.SeriesRules.maximumComponentRepairCost);
        if (cost == 0 || activeCareer.teamBalance < cost || car.repairs.Any(item => item.component == component) ||
            !activeCareer.employees.Contains(employee) || !CanAssignRepair(employee))
            return;

        int minutes = ComponentRepair.EstimateMinutes(wear, employee, component, activeCareer.currentDate);
        if (date > System.DateTime.MaxValue.Date.AddDays(-ComponentRepair.CareerDaysForMinutes(minutes)))
            return;
        var completionDay = date.AddDays(ComponentRepair.CareerDaysForMinutes(minutes));
        string previousAssigneeId = employee.repairAssigneeId;
        if (string.IsNullOrEmpty(previousAssigneeId))
            employee.repairAssigneeId = System.Guid.NewGuid().ToString("N");
        var repair = new ComponentRepair
        {
            component = component,
            employeeName = employee.FullName,
            employeeId = employee.repairAssigneeId,
            durationMinutes = minutes,
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
        // SaveMaintenance removes the job again if saving fails; keep the dialog open in that case.
        if (car.repairs.Contains(repair) && garageScreen.Q("carMaintenancePage").style.display == DisplayStyle.Flex)
            CloseCarMaintenance();
    }

    private bool CanUpgradeComponent(TeamCar car, CarComponent component, System.DateTime date) =>
        date < System.DateTime.MaxValue.Date &&
        activeCareer.employees.Any(CanAssignRepair) &&
        car.ComponentLevel(component) < TeamCar.MaximumComponentLevel &&
        activeCareer.teamBalance >= activeCareer.SeriesRules.componentUpgradeCost &&
        car.repairs?.Any(item => item.component == component) != true &&
        (!car.TryGetNextUpgradeDate(component, out var nextUpgrade) || date >= nextUpgrade);

    private Employee UpgradeAssignee(CarComponent component) => activeCareer.employees.Where(CanAssignRepair)
        .OrderByDescending(person => person.RepairSkill(component)).FirstOrDefault();

    private int UpgradeMinutes(Employee employee) =>
        ComponentRepair.ApplyMoraleToMinutes(24, employee.moraleInitialized ? employee.EffectiveMorale(activeCareer.currentDate) : 1f);

    private void ConfirmComponentUpgrade(TeamCar car, CarComponent component)
    {
        if (!Career.TryReadDate(activeCareer.currentDate, out var date) || !CanUpgradeComponent(car, component, date)) return;
        int level = car.ComponentLevel(component);
        var employee = UpgradeAssignee(component);
        int minutes = UpgradeMinutes(employee);
        long cost = activeCareer.SeriesRules.componentUpgradeCost;
        var overlay = new VisualElement(); SettingsFormStyle.Apply(overlay);
        overlay.AddToClassList("settings-confirmation-overlay");
        var dialog = new VisualElement(); dialog.AddToClassList("settings-confirmation-dialog");
        dialog.AddToClassList("component-upgrade-confirmation");
        dialog.Add(SettingsFormStyle.Label("Confirm Upgrade", "settings-group-heading"));
        dialog.Add(SettingsFormStyle.Label($"Upgrade {component} on chassis {car.chassisNumber} to level {level + 1} for {RaceFinances.Dollars(cost)}?", "settings-note"));
        dialog.Add(SettingsFormStyle.Label($"{employee.FullName} will take {ComponentRepair.FormatDuration(minutes)}.", "settings-note"));
        var error = SettingsFormStyle.ErrorSlot(null); dialog.Add(error);
        var actions = new VisualElement(); actions.AddToClassList("settings-footer"); actions.style.justifyContent = Justify.FlexEnd;
        var cancel = SettingsFormStyle.Button("Cancel", Close); cancel.tooltip = "Return to Shop without starting the upgrade.";
        var confirm = SettingsFormStyle.Button("Upgrade for " + RaceFinances.Dollars(cost), null, primary: true);
        confirm.tooltip = "Pay for this upgrade and assign the best available mechanic. The displayed duration includes morale.";
        confirm.clicked += () =>
        {
            if (!activeCareer.cars.Contains(car) || !Career.TryReadDate(activeCareer.currentDate, out var current) ||
                !CanUpgradeComponent(car, component, current) || car.ComponentLevel(component) != level ||
                activeCareer.SeriesRules.componentUpgradeCost != cost || UpgradeAssignee(component) != employee ||
                UpgradeMinutes(employee) != minutes ||
                current > System.DateTime.MaxValue.Date.AddDays(-ComponentRepair.CareerDaysForMinutes(minutes)))
            {
                int index = dialog.IndexOf(error); error.RemoveFromHierarchy();
                error = SettingsFormStyle.ErrorSlot("This upgrade is no longer available. Close and review the component again.");
                dialog.Insert(index, error); confirm.SetEnabled(false); return;
            }
            confirm.SetEnabled(false); Close(); UpgradeComponent(car, component);
        };
        actions.Add(cancel); actions.Add(confirm); dialog.Add(actions); overlay.Add(dialog);
        garageScreen.parent.Add(overlay); garageScreen.SetEnabled(false); cancel.Focus();
        overlay.RegisterCallback<KeyDownEvent>(evt => { if (evt.keyCode == KeyCode.Escape) { Close(); evt.StopPropagation(); } });
        void Close()
        {
            overlay.RemoveFromHierarchy(); garageScreen.SetEnabled(true);
            garageScreen.Q("shopCarPicker").Q<Button>()?.Focus();
        }
    }

    private void UpgradeComponent(TeamCar car, CarComponent component)
    {
        if (!Career.TryReadDate(activeCareer.currentDate, out var date) || !CanUpgradeComponent(car, component, date))
            return;
        var employee = UpgradeAssignee(component);
        int minutes = UpgradeMinutes(employee);
        int days = ComponentRepair.CareerDaysForMinutes(minutes);
        if (date > System.DateTime.MaxValue.Date.AddDays(-days))
            return;
        string previousId = employee.repairAssigneeId;
        if (string.IsNullOrEmpty(previousId)) employee.repairAssigneeId = System.Guid.NewGuid().ToString("N");
        var job = new ComponentRepair
        {
            component = component, isUpgrade = true, targetLevel = car.ComponentLevel(component) + 1,
            employeeName = employee.FullName, employeeId = employee.repairAssigneeId,
            durationMinutes = minutes, completionDate = date.AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };
        var previousUpgradeDays = (int[])car.componentUpgradeDays?.Clone();
        car.RecordComponentUpgrade(component, date);
        car.repairs ??= new List<ComponentRepair>();
        car.repairs.Add(job);
        long cost = activeCareer.SeriesRules.componentUpgradeCost;
        activeCareer.teamBalance -= cost;
        activeCareer.schedule.componentUpgrades += cost;
        SaveMaintenance(car, () =>
        {
            car.repairs.Remove(job);
            car.componentUpgradeDays = previousUpgradeDays;
            employee.repairAssigneeId = previousId;
            activeCareer.teamBalance += cost;
            activeCareer.schedule.componentUpgrades -= cost;
        }, $"{component} upgrade to level {job.targetLevel} started. Completes {job.completionDate}.");
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
        var completed = new List<(TeamCar Car, ComponentRepair Repair, float Wear, int Level, int Index)>();
        foreach (var car in cars)
        {
            if (car.repairs == null)
                continue;
            for (int index = car.repairs.Count - 1; index >= 0; index--)
            {
                var repair = car.repairs[index];
                if (!Career.TryReadDate(repair.completionDate, out var due) || due > date)
                    continue;
                completed.Add((car, repair, car.ComponentWear(repair.component), car.ComponentLevel(repair.component), index));
                if (repair.isUpgrade)
                    car.ComponentLevel(repair.component) = Mathf.Clamp(repair.targetLevel, 1, TeamCar.MaximumComponentLevel);
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
                var (car, repair, wear, level, index) = completed[i];
                car.ComponentLevel(repair.component) = level;
                car.ComponentWear(repair.component) = wear;
                car.repairs.Insert(index, repair);
            }
        };
    }

    private System.Action ScheduleRivalRepairs(System.DateTime date)
    {
        var added = new List<(TeamCar Car, ComponentRepair Repair, Employee Employee, string PreviousId)>();
        var entries = TrackPreview.CreateTimingEntries(activeCareer.cars[0], activeCareer);
        foreach (var team in entries.Skip(1).Where(entry => entry.car != null)
            .GroupBy(entry => TrackPreview.RivalTeamForSlot(entry.car.number)))
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

                int minutes = ComponentRepair.EstimateMinutes(wear, employee, component);
                int days = ComponentRepair.CareerDaysForMinutes(minutes);
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
                    durationMinutes = minutes,
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
        RestoreUsedCarFilters();
        var teamCar = career.cars[0];
        newChassisDraft = new TeamCar
        {
            number = teamCar.number, manufacturer = chassisManufacturers.FirstOrDefault(),
            chassisType = ChassisType.IntermediateTrack, componentWearInitialized = true,
            primaryPaint = "#848482", secondaryPaint = "#848482",
            trimPaint = "#848482", paintScheme = teamCar.paintScheme,
            numberPaint = teamCar.numberPaint, numberFont = teamCar.numberFont,
            numberBold = teamCar.numberBold, numberItalic = teamCar.numberItalic,
            hideRenderedNumber = teamCar.hideRenderedNumber
        };
        RestoreNewChassisConfiguration();
        newChassisDraft.Initialize(new System.Random());
        const int listingCount = 10;
        const int refreshDays = 7;
        var previousListings = career.usedCars ?? new List<UsedCarListing>();
        string previousDate = career.usedCarInventoryDate;
        bool dateKnown = Career.TryReadDate(career.currentDate, out var date);
        bool refresh = dateKnown &&
            (!Career.TryReadDate(previousDate, out var inventoryDate) || (date - inventoryDate).Days >= refreshDays);

        string message = null;
        career.usedCars = previousListings;
        if (refresh || previousListings.Count < listingCount || previousListings.Any(listing => !listing.componentLevelsAssigned || !listing.liveryAssigned))
        {
            var listings = refresh ? new List<UsedCarListing>() : previousListings.Select(listing => listing.componentLevelsAssigned && listing.liveryAssigned ? listing :
                JsonUtility.FromJson<UsedCarListing>(JsonUtility.ToJson(listing))).ToList();
            var random = new System.Random();
            foreach (var listing in listings)
            {
                listing.AssignComponentLevels(random, career.SeriesRules);
                listing.AssignLivery(random);
            }
            while (listings.Count < listingCount)
                listings.Add(UsedCarListing.Create(career.cars[0].number, random, career.SeriesRules));

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

        var market = garageScreen.Q("carMarketPage");
        foreach (var child in garageScreen.Children())
            if (child != market) child.SetEnabled(false);
        market.style.display = DisplayStyle.Flex;
        market.BringToFront();
        SelectCarPurchaseTab(career.purchaseChassisUsedTab, save: false);
        RefreshCarMarket(message);
        garageScreen.Q<Button>("carMarketBackButton").Focus();
    }

    private void SelectCarPurchaseTab(bool used, bool save = true)
    {
        if (save && activeCareer.purchaseChassisUsedTab != used)
        {
            bool previous = activeCareer.purchaseChassisUsedTab;
            activeCareer.purchaseChassisUsedTab = used;
            try { careerSetup.SaveProgress(activeCareer); }
            catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
            {
                activeCareer.purchaseChassisUsedTab = previous;
                RefreshCarMarket("Could not save the Purchase Chassis tab selection. " + ex.Message);
                return;
            }
        }
        garageScreen.Q("carPurchaseNewPanel").style.display = used ? DisplayStyle.None : DisplayStyle.Flex;
        garageScreen.Q("carPurchaseUsedPanel").style.display = used ? DisplayStyle.Flex : DisplayStyle.None;
        garageScreen.Q<Button>("carPurchaseNewTab").EnableInClassList("settings-tab-current", !used);
        garageScreen.Q<Button>("carPurchaseUsedTab").EnableInClassList("settings-tab-current", used);
    }

    private void RestoreNewChassisConfiguration()
    {
        var configuration = activeCareer.newChassisConfiguration;
        newManufacturerSelected = configuration != null && (!configuration.selectionStateRecorded || configuration.manufacturerSelected);
        newChassisTypeSelected = configuration != null && (!configuration.selectionStateRecorded || configuration.chassisTypeSelected);
        if (configuration == null) return;
        newChassisDraft.manufacturer = chassisManufacturers.Contains(configuration.manufacturer)
            ? configuration.manufacturer : chassisManufacturers.FirstOrDefault();
        newChassisDraft.chassisType = System.Enum.IsDefined(typeof(ChassisType), configuration.chassisType)
            ? configuration.chassisType : ChassisType.IntermediateTrack;
        for (int i = 0; i < TeamCar.Components.Length; i++)
            newChassisDraft.ComponentLevel(TeamCar.Components[i]) = configuration.componentLevels != null && i < configuration.componentLevels.Length
                ? Mathf.Clamp(configuration.componentLevels[i], 1, TeamCar.MaximumComponentLevel) : 1;
    }

    private void SaveNewChassisConfiguration()
    {
        var previous = activeCareer.newChassisConfiguration;
        activeCareer.newChassisConfiguration = new NewChassisConfiguration
        {
            manufacturer = newChassisDraft.manufacturer, chassisType = newChassisDraft.chassisType,
            selectionStateRecorded = true, manufacturerSelected = newManufacturerSelected, chassisTypeSelected = newChassisTypeSelected,
            componentLevels = TeamCar.Components.Select(component => newChassisDraft.ComponentLevel(component)).ToArray()
        };
        try { careerSetup.SaveProgress(activeCareer); }
        catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
        {
            activeCareer.newChassisConfiguration = previous;
            if (previous == null)
            {
                newManufacturerSelected = newChassisTypeSelected = false;
                newChassisDraft.manufacturer = chassisManufacturers.FirstOrDefault();
                newChassisDraft.chassisType = ChassisType.IntermediateTrack;
                foreach (var component in TeamCar.Components) newChassisDraft.ComponentLevel(component) = 1;
            }
            else RestoreNewChassisConfiguration();
            RefreshCarMarket("Could not save new chassis selections. " + ex.Message);
        }
    }

    private long NewChassisTotal() => activeCareer.SeriesRules.newChassisCost +
        TeamCar.Components.Sum(component => (long)(newChassisDraft.ComponentLevel(component) - 1) * activeCareer.SeriesRules.componentUpgradeCost);

    private void RefreshNewChassisPanel()
    {
        if (newChassisDraft == null) return;
        var panel = garageScreen.Q("carPurchaseNewPanel"); panel.Clear();
        panel.Add(SettingsFormStyle.Label("New Chassis", "team-home-heading"));
        var body = new VisualElement(); body.AddToClassList("new-chassis-body");
        var preview = new VisualElement(); preview.AddToClassList("new-chassis-preview-column");
        newChassisDraft.modelResource = activeCareer.SeriesRules.ModelFor(newChassisDraft.manufacturer);
        var image = new Image { image = largeCarPreviews.Get(newChassisDraft, System.Array.Empty<string>(),
            cacheScope: "new-chassis-purchase", transparentBackground: true), scaleMode = ScaleMode.ScaleToFit };
        image.AddToClassList("new-chassis-preview"); preview.Add(image);
        var manufacturer = SettingsFormStyle.Button(newManufacturerSelected ? newChassisDraft.manufacturer.ToString() : "Select manufacturer", () =>
            OpenNewChassisChoice("Choose Manufacturer", chassisManufacturers.Select(x => x.ToString()).ToArray(), index =>
            { newChassisDraft.manufacturer = chassisManufacturers[index]; newManufacturerSelected = true; SaveNewChassisConfiguration(); RefreshNewChassisPanel(); }));
        manufacturer.tooltip = "Choose a manufacturer available in your series."; preview.Add(manufacturer);
        var type = SettingsFormStyle.Button(newChassisTypeSelected ? newChassisDraft.ChassisTypeName : "Select Chassis Type", () =>
            OpenNewChassisChoice("Choose Chassis Type", NewChassisTypes.Select(TeamCar.DisplayChassisType).ToArray(), index =>
            { newChassisDraft.chassisType = NewChassisTypes[index]; newChassisTypeSelected = true; SaveNewChassisConfiguration(); RefreshNewChassisPanel(); }));
        type.tooltip = "Choose which track type this chassis is built for."; preview.Add(type);
        body.Add(preview);
        var components = new VisualElement(); components.AddToClassList("new-chassis-components");
        var componentHeading = new VisualElement(); componentHeading.AddToClassList("new-chassis-component-heading");
        componentHeading.Add(SettingsFormStyle.Label("Components", "settings-group-heading"));
        var reset = SettingsFormStyle.Button("Reset", () =>
        {
            foreach (var component in TeamCar.Components) newChassisDraft.ComponentLevel(component) = 1;
            SaveNewChassisConfiguration();
            RefreshNewChassisPanel();
        });
        reset.tooltip = "Reset all component levels to 1 and recalculate the total price.";
        reset.SetEnabled(TeamCar.Components.Any(component => newChassisDraft.ComponentLevel(component) > 1));
        components.Add(componentHeading);
        foreach (var component in CarComponentDisplay.Order)
        {
            string componentName = CarComponentDisplay.Name(component);
            var row = new VisualElement(); row.AddToClassList("new-chassis-component-row");
            var iconSlot = new VisualElement { tooltip = componentName };
            iconSlot.AddToClassList("home-component-icon-slot");
            var icon = ShopComponentPanel.ComponentIcon(component); icon.pickingMode = PickingMode.Ignore;
            iconSlot.Add(icon); row.Add(iconSlot);
            var levelBar = new VisualElement { tooltip = componentName + " level: " + newChassisDraft.ComponentLevel(component) + "/" + TeamCar.MaximumComponentLevel };
            levelBar.AddToClassList("new-chassis-level-bar");
            TeamCarCard.AddComponentLevel(levelBar, newChassisDraft, component); row.Add(levelBar);
            int level = newChassisDraft.ComponentLevel(component);
            var minus = SettingsFormStyle.Button("-", () => { newChassisDraft.ComponentLevel(component)--; SaveNewChassisConfiguration(); RefreshNewChassisPanel(); });
            minus.tooltip = "Decrease " + componentName + " level and purchase price."; minus.SetEnabled(level > 1);
            minus.AddToClassList("new-chassis-level-button"); row.Add(minus);
            row.Add(SettingsFormStyle.Label("Level " + level, "new-chassis-level"));
            var plus = SettingsFormStyle.Button("+", () => { newChassisDraft.ComponentLevel(component)++; SaveNewChassisConfiguration(); RefreshNewChassisPanel(); });
            plus.tooltip = "Increase " + componentName + " level for " + RaceFinances.Dollars(activeCareer.SeriesRules.componentUpgradeCost) + ".";
            plus.SetEnabled(level < TeamCar.MaximumComponentLevel); plus.AddToClassList("new-chassis-level-button"); row.Add(plus);
            long upgrade = (level - 1L) * activeCareer.SeriesRules.componentUpgradeCost;
            var upgradePrice = new TextField
            {
                value = RaceFinances.Dollars(upgrade), isReadOnly = true,
                tooltip = componentName + " upgrade cost above the included level 1 component."
            };
            SettingsFormStyle.StyleField(upgradePrice);
            upgradePrice.AddToClassList("new-chassis-upgrade-price");
            row.Add(upgradePrice);
            components.Add(row);
        }
        var footer = new VisualElement(); footer.AddToClassList("new-chassis-total-row");
        footer.style.alignSelf = Align.Stretch;
        footer.style.alignItems = Align.FlexEnd;
        footer.style.flexGrow = 0;
        footer.style.flexShrink = 0;
        var totalField = new TextField()
        {
            value = RaceFinances.Dollars(NewChassisTotal()), isReadOnly = true,
            tooltip = "Total purchase price, including the chassis and selected component upgrades."
        };
        SettingsFormStyle.StyleField(totalField);
        totalField.AddToClassList("new-chassis-total-field");
        // Keep the total in the same column container as the component price fields.
        var totalPriceRow = new VisualElement();
        totalPriceRow.style.flexDirection = FlexDirection.Row;
        totalPriceRow.style.justifyContent = Justify.FlexEnd;
        totalPriceRow.style.alignSelf = Align.Stretch;
        totalPriceRow.style.flexShrink = 0;
        totalField.style.width = 120;
        totalField.style.minWidth = 120;
        totalField.style.maxWidth = 120;
        totalField.style.marginLeft = totalField.style.marginRight = 0;
        totalField.style.flexGrow = 0;
        totalPriceRow.Add(totalField); footer.Add(totalPriceRow);
        var buy = SettingsFormStyle.Button("Buy", () =>
        {
            var car = JsonUtility.FromJson<TeamCar>(JsonUtility.ToJson(newChassisDraft));
            ConfirmCarPurchase(car, NewChassisTotal());
        }, primary: true);
        buy.AddToClassList("car-purchase-buy-button"); buy.SetEnabled(newManufacturerSelected && newChassisTypeSelected && CanBuyCar(NewChassisTotal()));
        buy.tooltip = "Review and confirm this chassis purchase with the selected component levels.";
        var actions = new VisualElement(); actions.AddToClassList("new-chassis-purchase-actions");
        reset.AddToClassList("car-purchase-buy-button");
        actions.Add(reset); actions.Add(buy); footer.Add(actions);
        long overBudget = NewChassisTotal() - activeCareer.teamBalance;
        var budgetError = SettingsFormStyle.ErrorSlot(overBudget > 0
            ? "The cost exceeds the team budget by " + RaceFinances.Dollars(overBudget) + "." : null);
        budgetError.AddToClassList("new-chassis-budget-error");
        footer.Add(budgetError);
        components.Add(footer); body.Add(components); panel.Add(body);
    }

    private void OpenNewChassisChoice(string title, string[] choices, System.Action<int> selected)
    {
        var market = garageScreen.Q("carMarketPage");
        var overlay = new VisualElement(); SettingsFormStyle.Apply(overlay); overlay.AddToClassList("settings-confirmation-overlay");
        var dialog = new VisualElement(); dialog.AddToClassList("settings-confirmation-dialog"); dialog.AddToClassList("used-car-filter-dialog");
        dialog.Add(SettingsFormStyle.Label(title, "settings-group-heading"));
        for (int i = 0; i < choices.Length; i++)
        {
            int index = i;
            var row = SettingsFormStyle.Button(choices[i], () => { selected(index); Close(); });
            row.tooltip = "Select " + choices[i] + "."; dialog.Add(row);
        }
        var footer = new VisualElement(); footer.AddToClassList("settings-footer");
        var cancel = SettingsFormStyle.Button("Cancel", Close); cancel.tooltip = "Keep the current selection.";
        footer.Add(cancel); dialog.Add(footer); overlay.Add(dialog); garageScreen.parent.Add(overlay);
        market.SetEnabled(false); dialog.Q<Button>()?.Focus();
        overlay.RegisterCallback<KeyDownEvent>(evt => { if (evt.keyCode == KeyCode.Escape) { Close(); evt.StopPropagation(); } });
        void Close() { overlay.RemoveFromHierarchy(); market.SetEnabled(true); }
    }

    private void OpenUsedCarSort()
    {
        var market = garageScreen.Q("carMarketPage");
        var overlay = new VisualElement(); SettingsFormStyle.Apply(overlay);
        overlay.AddToClassList("settings-confirmation-overlay");
        var dialog = new VisualElement(); dialog.AddToClassList("settings-confirmation-dialog");
        dialog.AddToClassList("used-car-filter-dialog");
        dialog.Add(SettingsFormStyle.Label("Sort Used Chassis", "settings-group-heading"));
        for (int i = 0; i < UsedCarSortLabels.Length; i++)
        {
            int selection = i;
            var row = SettingsFormStyle.Button(UsedCarSortLabels[i], () =>
            { usedCarSort = selection; if (SaveUsedCarFilters()) { RefreshCarMarket(); Close(); } });
            row.EnableInClassList("settings-tab-current", i == usedCarSort);
            row.tooltip = i < 2 ? "Order chassis by their purchase price." :
                "Order chassis by average remaining component condition. Higher condition means less wear.";
            dialog.Add(row);
        }
        var footer = new VisualElement(); footer.AddToClassList("settings-footer");
        var cancel = SettingsFormStyle.Button("Cancel", Close); cancel.tooltip = "Keep the current sort order.";
        footer.Add(cancel); dialog.Add(footer); overlay.Add(dialog);
        garageScreen.parent.Add(overlay); market.SetEnabled(false); dialog.Q<Button>()?.Focus();
        overlay.RegisterCallback<KeyDownEvent>(evt =>
        { if (evt.keyCode == KeyCode.Escape) { Close(); evt.StopPropagation(); } });
        void Close()
        {
            overlay.RemoveFromHierarchy(); market.SetEnabled(true);
            garageScreen.Q<Button>("usedCarSortButton").Focus();
        }
    }

    private static double UsedChassisCondition(UsedCarListing listing) =>
        TeamCar.Components.Average(component => 1d - Mathf.Clamp01(listing.car.ComponentWear(component)));

    private void RestoreUsedCarFilters()
    {
        usedCarSort = Mathf.Clamp(activeCareer.usedCarSortOrder, 0, UsedCarSortLabels.Length - 1);
        usedManufacturerFilter = System.Enum.TryParse<CarManufacturer>(activeCareer.usedCarManufacturerFilter, out var manufacturer) &&
            System.Enum.IsDefined(typeof(CarManufacturer), manufacturer) ? manufacturer : (CarManufacturer?)null;
        usedTypeFilter = System.Enum.TryParse<ChassisType>(activeCareer.usedCarTypeFilter, out var type) &&
            System.Enum.IsDefined(typeof(ChassisType), type) ? type : (ChassisType?)null;
        usedMaxPriceFilter = long.TryParse(activeCareer.usedCarMaxPriceFilter, NumberStyles.Integer, CultureInfo.InvariantCulture, out var price) && price >= 0
            ? price : (long?)null;
    }

    private bool SaveUsedCarFilters()
    {
        var career = activeCareer;
        string previousManufacturer = career.usedCarManufacturerFilter;
        string previousType = career.usedCarTypeFilter;
        string previousPrice = career.usedCarMaxPriceFilter;
        int previousSort = career.usedCarSortOrder;
        career.usedCarSortOrder = usedCarSort;
        career.usedCarManufacturerFilter = usedManufacturerFilter?.ToString();
        career.usedCarTypeFilter = usedTypeFilter?.ToString();
        career.usedCarMaxPriceFilter = usedMaxPriceFilter?.ToString(CultureInfo.InvariantCulture);
        try { careerSetup.SaveProgress(career); return true; }
        catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
        {
            career.usedCarManufacturerFilter = previousManufacturer;
            career.usedCarTypeFilter = previousType;
            career.usedCarMaxPriceFilter = previousPrice;
            career.usedCarSortOrder = previousSort;
            RestoreUsedCarFilters();
            RefreshCarMarket("Could not save used chassis preferences. " + ex.Message);
            return false;
        }
    }

    private void ClearUsedCarFilters()
    {
        usedManufacturerFilter = null; usedTypeFilter = null; usedMaxPriceFilter = null;
        if (SaveUsedCarFilters()) RefreshCarMarket();
    }

    private void OpenUsedCarFilters()
    {
        var market = garageScreen.Q("carMarketPage");
        var overlay = new VisualElement(); SettingsFormStyle.Apply(overlay);
        overlay.AddToClassList("settings-confirmation-overlay");
        var dialog = new VisualElement(); dialog.AddToClassList("settings-confirmation-dialog");
        dialog.AddToClassList("used-car-filter-dialog");
        dialog.Add(SettingsFormStyle.Label("Filter Used Chassis", "settings-group-heading"));
        var manufacturer = usedManufacturerFilter;
        var type = usedTypeFilter;
        var manufacturerButton = SettingsFormStyle.Button("", null);
        var typeButton = SettingsFormStyle.Button("", null);
        manufacturerButton.tooltip = "Limit used chassis to one manufacturer, or show all manufacturers.";
        typeButton.tooltip = "Limit used chassis to one chassis type, or show all types.";
        dialog.Add(SettingsFormStyle.Label("Manufacturer", "settings-note")); dialog.Add(manufacturerButton);
        dialog.Add(SettingsFormStyle.Label("Car type", "settings-note")); dialog.Add(typeButton);
        var price = new TextField("Maximum price") { value = usedMaxPriceFilter?.ToString(CultureInfo.InvariantCulture) ?? "" };
        SettingsFormStyle.StyleField(price); price.tooltip = "Maximum purchase price. Leave blank for no price limit.";
        dialog.Add(price); var error = SettingsFormStyle.InlineError(price);
        bool attempted = false;
        bool Valid()
        {
            bool valid = string.IsNullOrWhiteSpace(price.value) ||
                (long.TryParse(price.value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount) && amount >= 0);
            error(attempted && !valid ? "Enter a non-negative whole price, or leave blank." : null);
            return valid;
        }
        void UpdateLabels()
        {
            manufacturerButton.text = manufacturer?.ToString() ?? "All manufacturers";
            typeButton.text = type.HasValue ? TeamCar.DisplayChassisType(type.Value) : "All car types";
        }
        manufacturerButton.clicked += () => Choose("Manufacturer", new[] { "All manufacturers" }.Concat(chassisManufacturers.Select(x => x.ToString())).ToArray(), index =>
        { manufacturer = index == 0 ? (CarManufacturer?)null : chassisManufacturers[index - 1]; UpdateLabels(); });
        typeButton.clicked += () => Choose("Car type", new[] { "All car types" }.Concat(NewChassisTypes.Select(TeamCar.DisplayChassisType)).ToArray(), index =>
        { type = index == 0 ? (ChassisType?)null : NewChassisTypes[index - 1]; UpdateLabels(); });
        var actions = new VisualElement(); actions.AddToClassList("settings-footer");
        var clear = SettingsFormStyle.Button("Clear Filters", () =>
        { manufacturer = null; type = null; price.value = ""; UpdateLabels(); ClearUsedCarFilters(); });
        clear.tooltip = "Remove all active manufacturer, car type, and price filters.";
        var cancel = SettingsFormStyle.Button("Cancel", Close); cancel.tooltip = "Close without applying filter edits.";
        var apply = SettingsFormStyle.Button("Apply", () =>
        {
            attempted = true; if (!Valid()) return;
            usedManufacturerFilter = manufacturer; usedTypeFilter = type;
            usedMaxPriceFilter = string.IsNullOrWhiteSpace(price.value) ? (long?)null : long.Parse(price.value, CultureInfo.InvariantCulture);
            if (SaveUsedCarFilters()) { RefreshCarMarket(); Close(); }
        });
        apply.tooltip = "Show used chassis matching these filters.";
        price.RegisterValueChangedCallback(_ => { if (attempted) Valid(); });
        actions.Add(clear); actions.Add(cancel); actions.Add(apply); dialog.Add(actions);
        overlay.Add(dialog); garageScreen.parent.Add(overlay); market.SetEnabled(false);
        UpdateLabels(); manufacturerButton.Focus();
        overlay.RegisterCallback<KeyDownEvent>(evt => { if (evt.keyCode == KeyCode.Escape) { Close(); evt.StopPropagation(); } });
        void Close() { overlay.RemoveFromHierarchy(); market.SetEnabled(true); garageScreen.Q<Button>("usedCarFilterButton").Focus(); }
        void Choose(string title, string[] choices, System.Action<int> selected)
        {
            var picker = new VisualElement(); SettingsFormStyle.Apply(picker); picker.AddToClassList("settings-confirmation-overlay");
            var panel = new VisualElement(); panel.AddToClassList("settings-confirmation-dialog"); panel.AddToClassList("used-car-filter-dialog");
            panel.Add(SettingsFormStyle.Label(title, "settings-group-heading"));
            for (int i = 0; i < choices.Length; i++)
            {
                int index = i;
                var row = SettingsFormStyle.Button(choices[i], () => { selected(index); Done(); });
                row.tooltip = "Select " + choices[i] + "."; panel.Add(row);
            }
            var back = SettingsFormStyle.Button("Cancel", Done); back.tooltip = "Keep the current filter selection.";
            var footer = new VisualElement(); footer.AddToClassList("settings-footer"); footer.Add(back); panel.Add(footer);
            picker.Add(panel); garageScreen.parent.Add(picker); dialog.SetEnabled(false); panel.Q<Button>()?.Focus();
            picker.RegisterCallback<KeyDownEvent>(evt => { if (evt.keyCode == KeyCode.Escape) { Done(); evt.StopPropagation(); } });
            void Done() { picker.RemoveFromHierarchy(); dialog.SetEnabled(true); manufacturerButton.Focus(); }
        }
    }

    private void CloseCarMarket()
    {
        garageScreen.Q("carMarketPage").style.display = DisplayStyle.None;
        foreach (var child in garageScreen.Children()) child.SetEnabled(true);
        int carsPage = System.Array.FindIndex(garagePages, page => page.name == "carsPage");
        DisplayGaragePage(carsPage);
        garageTabs[carsPage].Focus();
    }

    private void RefreshCarMarket(string message = null)
    {
        var career = activeCareer;
        var status = garageScreen.Q<Label>("carMarketStatus");
        status.text = message ?? (career.cars.Count >= TeamCar.MaximumOwned ? "Your car inventory is full." : string.Empty);
        status.style.display = string.IsNullOrEmpty(status.text) ? DisplayStyle.None : DisplayStyle.Flex;
        RefreshNewChassisPanel();
        var listings = garageScreen.Q<ScrollView>("usedCarListings");
        listings.Clear();
        if (career.usedCars.Count == 0)
            AddResultsMessage(listings, "No used cars available.");
        var filtered = career.usedCars.Where(listing =>
            (!usedManufacturerFilter.HasValue || listing.car.manufacturer == usedManufacturerFilter.Value) &&
            (!usedTypeFilter.HasValue || listing.car.chassisType == usedTypeFilter.Value) &&
            (!usedMaxPriceFilter.HasValue || listing.price <= usedMaxPriceFilter.Value)).ToList();
        switch (usedCarSort)
        {
            case 1: filtered = filtered.OrderByDescending(listing => listing.price).ToList(); break;
            case 2: filtered = filtered.OrderByDescending(UsedChassisCondition).ThenBy(listing => listing.price).ToList(); break;
            case 3: filtered = filtered.OrderBy(UsedChassisCondition).ThenBy(listing => listing.price).ToList(); break;
            default: filtered = filtered.OrderBy(listing => listing.price).ToList(); break;
        }
        garageScreen.Q<Button>("usedCarSortButton").text = UsedCarSortLabels[usedCarSort];
        garageScreen.Q<Label>("carPurchaseBudget").text = "Budget: " + RaceFinances.Dollars(career.teamBalance);
        var descriptions = new List<string>();
        if (usedManufacturerFilter.HasValue) descriptions.Add(usedManufacturerFilter.Value.ToString());
        if (usedTypeFilter.HasValue) descriptions.Add(TeamCar.DisplayChassisType(usedTypeFilter.Value));
        if (usedMaxPriceFilter.HasValue) descriptions.Add("Up to " + RaceFinances.Dollars(usedMaxPriceFilter.Value));
        garageScreen.Q<Label>("usedCarFilterSummary").text = descriptions.Count == 0 ? "All chassis" : string.Join(" | ", descriptions);
        garageScreen.Q<Button>("usedCarClearFilterButton").SetEnabled(descriptions.Count > 0);
        if (filtered.Count == 0 && career.usedCars.Count > 0) AddResultsMessage(listings, "No chassis match these filters.");
        foreach (var listing in filtered)
        {
            var car = listing.car;
            car.modelResource = career.SeriesRules.ModelFor(car.manufacturer);
            var item = new VisualElement(); item.AddToClassList("used-chassis-item");
            var identity = new VisualElement(); identity.AddToClassList("used-chassis-identity");
            identity.Add(SettingsFormStyle.Label($"Chassis {car.chassisNumber ?? "Unassigned"}", "used-chassis-title"));
            var image = new Image
            {
                image = carThumbnails.Get(car, System.Array.Empty<string>(), transparentBackground: true),
                scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore
            };
            image.AddToClassList("used-chassis-image"); identity.Add(image);
            identity.Add(SettingsFormStyle.Label($"{car.manufacturer} - {car.ChassisTypeName}", "used-chassis-specs"));
            var buy = SettingsFormStyle.Button("Buy " + RaceFinances.Dollars(listing.price),
                () => ConfirmCarPurchase(listing.car, listing.price, listing));
            buy.AddToClassList("car-purchase-buy-button");
            buy.tooltip = "Purchase this chassis with the displayed component levels and wear.";
            buy.SetEnabled(CanBuyCar(listing.price)); identity.Add(buy);
            item.Add(identity);
            var ratings = new VisualElement(); ratings.AddToClassList("used-chassis-ratings");
            TeamCarCard.AddComponentRatings(ratings, car, useIcons: true);
            item.Add(ratings);
            bool affordable = listing.price <= career.teamBalance;
            item.EnableInClassList("used-chassis-unaffordable", !affordable);
            item.SetEnabled(affordable);
            listings.Add(item);
        }
        settings?.RestorePalette();
    }

    private bool CanBuyCar(long price) =>
        activeCareer.cars.Count < TeamCar.MaximumOwned && activeCareer.teamBalance >= price;

    private void ConfirmCarPurchase(TeamCar car, long price, UsedCarListing listing = null)
    {
        if (!CanBuyCar(price) || (listing != null && !activeCareer.usedCars.Contains(listing))) return;
        var market = garageScreen.Q("carMarketPage");
        var overlay = new VisualElement();
        SettingsFormStyle.Apply(overlay); overlay.AddToClassList("settings-confirmation-overlay");
        var dialog = new VisualElement(); dialog.AddToClassList("settings-confirmation-dialog");
        dialog.AddToClassList("chassis-purchase-confirmation");
        dialog.style.backgroundColor = (Color)new Color32(16, 21, 30, 255);
        var trim = new StyleColor(new Color32(0, 155, 221, 255));
        dialog.style.borderTopColor = dialog.style.borderBottomColor = dialog.style.borderLeftColor = dialog.style.borderRightColor = trim;
        dialog.Add(SettingsFormStyle.Label("Confirm Chassis Purchase", "settings-group-heading"));
        var actions = new VisualElement(); actions.AddToClassList("settings-footer"); actions.style.justifyContent = Justify.FlexEnd;
        var cancel = SettingsFormStyle.Button("Cancel", Close);
        cancel.tooltip = "Return to available chassis without buying.";
        var confirm = SettingsFormStyle.Button("Buy " + RaceFinances.Dollars(price), null, primary: true);
        confirm.tooltip = "Confirm the purchase at the displayed price.";
        confirm.clicked += () =>
        {
            confirm.SetEnabled(false);
            Close();
            PurchaseCar(car, price, listing);
        };
        actions.Add(cancel); actions.Add(confirm); dialog.Add(actions); overlay.Add(dialog);
        garageScreen.parent.Add(overlay); market.SetEnabled(false); cancel.Focus();
        overlay.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Escape) return;
            Close(); evt.StopPropagation();
        });
        void Close()
        {
            overlay.RemoveFromHierarchy(); market.SetEnabled(true);
            garageScreen.Q<Button>("carMarketBackButton").Focus();
        }
    }

    private void PurchaseCar(TeamCar car, long price, UsedCarListing listing = null)
    {
        var career = activeCareer;
        int listingIndex = listing == null ? -1 : career.usedCars.IndexOf(listing);
        if (!CanBuyCar(price) || (listing != null && listingIndex < 0))
            return;

        var previousConfiguration = career.newChassisConfiguration;
        if (listing == null && previousConfiguration != null)
        {
            career.newChassisConfiguration = new NewChassisConfiguration
            {
                manufacturer = previousConfiguration.manufacturer,
                chassisType = previousConfiguration.chassisType,
                selectionStateRecorded = previousConfiguration.selectionStateRecorded,
                manufacturerSelected = previousConfiguration.manufacturerSelected,
                chassisTypeSelected = previousConfiguration.chassisTypeSelected,
                componentLevels = TeamCar.Components.Select(_ => 1).ToArray()
            };
        }
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
            career.newChassisConfiguration = previousConfiguration;
            career.teamBalance += price;
            career.schedule.carPurchases -= price;
            career.cars.Remove(car);
            car.number = previousNumber;
            if (listingIndex >= 0)
                career.usedCars.Insert(listingIndex, listing);
            RefreshCarMarket("Could not save the purchase. " + ex.Message);
            return;
        }

        if (listing == null && newChassisDraft != null)
            foreach (var component in TeamCar.Components) newChassisDraft.ComponentLevel(component) = 1;
        car.modelResource = career.SeriesRules.ModelFor(car.manufacturer);
        RefreshTeamBalance();
        RefreshTeamCars();
        RefreshSchedule();
        selectedCarsChassis = car;
        CloseCarMarket();
        var rows = garageScreen.Q<ScrollView>("teamCarRows");
        var purchasedCard = rows.Children().FirstOrDefault(card => ReferenceEquals(card.userData, car));
        if (purchasedCard != null)
            rows.schedule.Execute(() => { rows.ScrollTo(purchasedCard); purchasedCard.Focus(); });
    }

    private void RefreshSchedule(bool selectNext = false)
    {
        var schedule = activeCareer.schedule;
        var races = schedule.races;
        var next = races?.FirstOrDefault(race => !race.completed);
        var nextTrack = next == null ? null : catalog.Find(next.trackId)?.Definition;
        string trackType = nextTrack switch
        {
            null => string.Empty,
            { majorStyle: "Road" } => " (Road Course)",
            { restrictorPlate: true } => " (Restrictor Plate)",
            { lengthMiles: < 1f } => " (Short Track)",
            _ => " (Intermediate)"
        };
        string trackWithType = next == null ? string.Empty : next.trackName + trackType;
        garageScreen.Q<Label>("teamHeaderTrack").text = next == null ? "No upcoming race" : next.trackName;
        garageScreen.Q<Label>("teamHeaderRaceDate").text = next == null ? string.Empty : RaceDate(next);
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
                if (selectNext && race == next)
                    rowToReveal = row;
            }
        }
        if (rowToReveal != null)
        {
            RevealScheduleRow(rows, rowToReveal);
        }
        else
        {
            rows.schedule.Execute(() => rows.scrollOffset = scroll);
        }

        DisplayTeamStandings(schedule);
        RefreshHomeDashboard();
        var chosenCar = EventCar(selectedEvent);
        var carPicker = garageScreen.Q<Button>("eventCarPicker");
        carPicker.style.display = selectedEvent != null && !selectedEvent.completed ? DisplayStyle.Flex : DisplayStyle.None;
        carPicker.text = chosenCar == null ? "Choose Race Car" : $"Race car - Chassis # {chosenCar.chassisNumber}";

        var eventTrack = selectedEvent == null ? null : catalog.Find(selectedEvent.trackId);
        bool available = eventTrack != null;
        DisplayTrackRatings(garageScreen.Q("scheduleTrackRatings"), eventTrack?.Definition.weights);
        scheduleTrackOutline.SetTrack(eventTrack?.Definition);
        bool eventCompleted = selectedEvent?.completed == true;
        startRaceButton.text = eventCompleted ? "View Results" : "Go To Event";
        bool canRunEvent = available && !eventCompleted && CanFieldDriver(activeCareer) &&
            chosenCar != null;
        bool canResume = available && CanFieldDriver(activeCareer) &&
            selectedEvent?.sessions?.Any(session => session.Completed) == true && EventGarageCar(selectedEvent) != null;
        startRaceButton.SetEnabled(eventCompleted || canRunEvent || canResume);
        simulateEventButton.SetEnabled(canRunEvent);
        garageScreen.Q<Button>("simulateSeasonButton").SetEnabled(CanFieldDriver(activeCareer) && !simulatingScheduleEvent && races.Any(race => !race.completed));
        simulateEventButton.tooltip = CanFieldDriver(activeCareer) ? "Simulate the selected race event." : DriverEntryMessage(activeCareer);
        startRaceButton.tooltip = eventCompleted ? "View this event's recorded results." : CanFieldDriver(activeCareer) ? "Go to the selected race event." : DriverEntryMessage(activeCareer);
        var details = garageScreen.Q<Label>("scheduleStatus");
        if (selectedEvent == null)
        {
            details.text = "No scheduled races available.";
        }
        else
        {
            string status = selectedEvent.completed ? "Completed" : !available ? "Track definition unavailable"
                : !HasDriver(activeCareer) ? "No driver assigned" : DriverInCustody(activeCareer) ? "Driver unavailable"
                : selectedEvent == next ? "Next race" : "Available";
            details.text = $"{selectedEvent.trackName}\n{RaceDate(selectedEvent)}\n{selectedEvent.EffectiveRaceLaps} laps  /  {status}";
            if (selectedEvent.completed)
                details.text += "\n" + (selectedEvent.playerSkipped ? "Team skipped this race" : chosenCar?.ChassisDisplayName ?? "Car not recorded");
            if (!eventCompleted && !CanFieldDriver(activeCareer))
                details.text += "\n" + DriverEntryMessage(activeCareer);
            if (!eventCompleted && chosenCar == null)
                details.text += "\nChoose a race-ready chassis from the race car list.";
            if (selectedEvent.purse > 0)
                details.text += $"\nPurse  /  {RaceFinances.Dollars(selectedEvent.purse)}";
        }
        settings?.RestorePalette();
    }

    private void RevealScheduleRow(ScrollView rows, VisualElement row)
    {
        var page = garageScreen.Q("schedulePage");
        IVisualElementScheduledItem reveal = null;
        reveal = rows.schedule.Execute(() =>
        {
            if (row.parent != rows || page.style.display == DisplayStyle.None)
            {
                reveal.Pause();
                return;
            }

            // Use the row's position in the unscrolled content. Its panel bounds
            // can still reflect the previous scroll offset while the tab opens.
            float viewportHeight = rows.contentViewport.layout.height;
            float contentHeight = rows.contentContainer.layout.height;
            Rect rowLayout = row.layout;
            if (!(viewportHeight > 0f && rowLayout.height > 0f && contentHeight >= rowLayout.yMax))
                return;

            float offset = rows.scrollOffset.y;
            if (rowLayout.yMin < offset || rowLayout.yMax > offset + viewportHeight)
            {
                float target = Mathf.Max(0f, rowLayout.center.y - viewportHeight * 0.5f);
                float maximum = Mathf.Max(0f, contentHeight - viewportHeight);
                target = Mathf.Min(target, maximum);
                if (!(rows.verticalScroller.highValue >= target - 0.5f))
                    return;

                rows.scrollOffset = new Vector2(rows.scrollOffset.x, target);
                if (Mathf.Abs(rows.scrollOffset.y - target) > 0.5f)
                    return;
            }
            reveal.Pause();
        }).Every(16).ForDuration(2000);
    }

    private void DisplayTeamStandings(SeasonSchedule schedule)
    {
        garageScreen.Q<Label>("teamStandingsHeading").text = "Driver Points";
        garageScreen.Q<Label>("standingsSeasonHeading").text = $"{schedule.season} {activeCareer.seriesName} Standings";
        var roster = TrackPreview.CreateTimingEntries(activeCareer.cars[0], activeCareer);
        var tables = ChampionshipStandings.Calculate(schedule, roster, activeCareer.SeriesRules.ManufacturerChoices);
        Show("teamStandingsRows", "driverStandingsColumns", tables.drivers, "Driver", true);
        Show("manufacturerStandingsRows", "manufacturerStandingsColumns", tables.manufacturers, "Manufacturer", false);
        Show("ownerStandingsRows", "ownerStandingsColumns", tables.owners, "Team", true);
        void Show(string listName, string headerName, List<ChampionshipStandings.Row> entries, string nameHeading, bool number)
        {
            var rows = garageScreen.Q<ScrollView>(listName); var scroll = rows.scrollOffset; rows.Clear();
            var header = garageScreen.Q(headerName); header.Clear();
            bool driverStats = listName == "teamStandingsRows";
            AddStandingsRow(header, "Pos", number ? "Car #" : "", nameHeading, "Points", false, "Wins",
                driverStats ? "Top 5" : null, driverStats ? "Top 10" : null);
            if (!number) header.Q(className: "standings-car-number").style.display = DisplayStyle.None;
            int rank = 1;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (i > 0 && entry.points != entries[i - 1].points) rank = i + 1;
                var row = AddStandingsRow(rows, rank.ToString(), number ? entry.numberText : "", entry.name,
                    entry.points == 0 ? "-" : entry.points.ToString(), entry.player, entry.wins.ToString(),
                    driverStats ? entry.top5.ToString() : null, driverStats ? entry.top10.ToString() : null);
                var carNumber = row.Q<Label>(className: "standings-car-number");
                if (!number) carNumber.style.display = DisplayStyle.None;
                else if (roster.FirstOrDefault(result => result.carNumber == entry.number && result.playerEntry == entry.player)?.car is TeamCar car)
                    CarNumberFonts.Apply(carNumber, car.PaintStyle);
                row.tooltip = entry.name + (number ? " - Car #" + entry.numberText : "") + ": " + entry.points + " points, " + entry.wins + " wins";
            }
            rows.schedule.Execute(() => rows.scrollOffset = scroll);
        }
    }

    private List<KeyValuePair<(int Number, string Team, bool IsPlayer), long>> GetStandings(SeasonSchedule schedule)
    {
        string playerTeam = activeCareer.teamName;
        var roster = TrackPreview.CreateTimingEntries(activeCareer.cars[0], activeCareer);
        var rivalNumbers = roster.Skip(1).ToDictionary(car => car.ownerEntryId, car => car.carNumber);
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
                    bool isPlayer = race.IsPlayerResult(result);
                    int number = result.carNumber;
                    if (!isPlayer)
                    {
                        if (!string.IsNullOrEmpty(result.ownerEntryId) && rivalNumbers.TryGetValue(result.ownerEntryId, out int rivalNumber))
                            number = rivalNumber;
                        else if (roster.Skip(1).FirstOrDefault(entry => entry.teamName == result.teamName &&
                            (entry.driverName == result.driverName || entry.driver?.OriginalName == result.driverName)) is SessionCarResult rival)
                            number = rival.carNumber;
                    }

                    var car = (Number: number, Team: isPlayer ? playerTeam : result.teamName, IsPlayer: isPlayer);
                    if (car.Number > 0 && !string.IsNullOrWhiteSpace(car.Team))
                        totals[car] = (totals.TryGetValue(car, out long points) ? points : 0) + result.pointsEarned;
                }
            }
        }

        return totals.OrderByDescending(car => car.Value)
            .ThenBy(car => car.Key.Number).ThenBy(car => car.Key.Team, System.StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static VisualElement AddStandingsRow(VisualElement rows, string position, string carNumber, string team, string points,
        bool player, string wins = null, string top5 = null, string top10 = null)
    {
        var row = new VisualElement();
        row.AddToClassList("results-grid-row");
        row.AddToClassList("standings-row");
        row.EnableInClassList("standings-player", player);
        AddCell(position, "standings-position");
        AddCell(carNumber, "standings-car-number");
        AddCell(team, "results-grid-name");
        if (wins != null) AddCell(wins, "standings-wins");
        if (top5 != null) AddCell(top5, "standings-top-five");
        if (top10 != null) AddCell(top10, "standings-top-ten");
        AddCell(points, "standings-points");
        rows.Add(row);
        return row;

        void AddCell(string text, string column)
        {
            var cell = new Label(text);
            cell.AddToClassList("garage-detail");
            cell.AddToClassList("results-grid-cell");
            cell.AddToClassList(column);
            row.Add(cell);
        }
    }

    internal static void DisplayTrackRatings(VisualElement chart, TrackWeights weights)
    {
        chart.Clear();
        chart.style.display = weights == null ? DisplayStyle.None : DisplayStyle.Flex;
        if (weights == null)
            return;

        foreach (var (title, value, component) in TrackRatings.DisplayAttributes(weights))
            AddRatingBar(chart, title, value, component);
    }

    private static void AddRatingBar(VisualElement chart, string title, float value, CarComponent? component)
    {
        string help = component.HasValue
            ? $"{title}: {value:F2} out of 1.00. Higher values mean this attribute matters more at this track."
            : $"{title}: {value:F2} out of 1.00. Higher values mean higher tire wear at this track.";
        var column = new VisualElement { tooltip = help };
        column.AddToClassList("rating-column");
        var number = new Label(value.ToString("F2"));
        number.AddToClassList("garage-detail");
        number.AddToClassList("rating-label");
        column.Add(number);
        var well = new VisualElement { tooltip = help };
        well.AddToClassList("rating-well");
        var bar = new VisualElement { tooltip = help };
        bar.AddToClassList("rating-fill");
        bar.style.height = Length.Percent(value * 100f);
        ComponentWearGradient.Apply(bar, value, vertical: true);
        well.Add(bar);
        column.Add(well);
        var icon = component.HasValue ? ShopComponentPanel.ComponentIcon(component.Value) : TrackIndicatorIcon(tireWear: true);
        icon.tooltip = help;
        icon.AddToClassList("track-attribute-icon");
        column.Add(icon);
        chart.Add(column);
    }

    private static VisualElement TrackIndicatorIcon(bool tireWear)
    {
        var icon = new VisualElement();
        Color32 color = tireWear ? new Color32(228, 235, 242, 255) : new Color32(85, 196, 239, 255);
        icon.style.color = new StyleColor(color);
        icon.generateVisualContent += context =>
        {
            var painter = context.painter2D;
            painter.strokeColor = icon.resolvedStyle.color;
            painter.lineWidth = 2;
            painter.BeginPath();
            if (tireWear)
            {
                Circle(20, 20, 17); Circle(20, 20, 12); Circle(20, 20, 4);
                for (int i = 0; i < 5; i++)
                {
                    float angle = i * Mathf.PI * 2 / 5;
                    Line(20 + Mathf.Cos(angle) * 5, 20 + Mathf.Sin(angle) * 5,
                        20 + Mathf.Cos(angle) * 12, 20 + Mathf.Sin(angle) * 12);
                }
            }
            else
            {
                Line(3, 28, 9, 20); Line(9, 20, 16, 26); Line(16, 26, 23, 13);
                Line(23, 13, 31, 22); Line(31, 22, 37, 17);
            }
            painter.Stroke();
            void Circle(float x, float y, float radius)
            {
                painter.MoveTo(new Vector2(x + radius, y));
                painter.Arc(new Vector2(x, y), radius, Angle.Degrees(0), Angle.Degrees(360));
            }
            void Line(float x1, float y1, float x2, float y2)
            {
                painter.MoveTo(new Vector2(x1, y1)); painter.LineTo(new Vector2(x2, y2));
            }
        };
        return icon;
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
        var car = EventGarageCar(race);
        return car != null && CanEnterRace(car, race) ? car : null;
    }

    private TeamCar EventGarageCar(ScheduledRace race)
    {
        if (race == null)
            return null;
        var car = string.IsNullOrEmpty(race.carChassisNumber)
            ? activeCareer.cars.Find(item => item.number == race.carNumber)
            : activeCareer.cars.Find(item => item.chassisNumber == race.carChassisNumber);
        return race.completed ? car : car ?? activeCareer.cars.FirstOrDefault();
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
        button.AddToClassList("settings-button");
        button.tooltip = "Select " + title + ".";
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

        if (ShowRequiredEmailTooltip())
            return;

        var track = catalog.Find(race.trackId);
        var car = EventGarageCar(race);
        bool started = race.sessions?.Any(session => session.Completed) == true;
        if (!CanFieldDriver(activeCareer) || car == null || (!started && !CanEnterRace(car, race)) || track == null)
            return;

        PrepareEventSessions(race, track.Definition);
        activeRace = race;
        garageScreen.style.display = DisplayStyle.None;
        raceEventView.Screen.style.display = DisplayStyle.Flex;
        OpenEventGarageView(race, track.Definition, car, ActiveSponsorNames());
    }

    private Career EventServiceCareer => developmentPractice ? developmentPracticeCareer : activeCareer;

    private void OpenEventGarageView(ScheduledRace race, TrackDefinition track, TeamCar car, string[] sponsors)
    {
        raceEventView.SimulateGarageLaps = SimulateGarageRun;
        raceEventView.CanSimulateGarageLaps = CanSimulateGarageRun;
        raceEventView.Open(race, track, car, largeCarPreviews.Get(car, sponsors, track: track),
            () => EventServiceCareer.teamBalance,
            () => ServiceEventCondition(race, car, changeTires: true),
            () => ServiceEventCondition(race, car, changeTires: false),
            (name, setup) => SaveTrackSetup(car, track.id, name, setup),
            () => largeCarPreviews.Get(car, sponsors, overhead: true, track: track),
            () => ReadEventCondition(race, car), CanServiceInGarage, developmentPractice, EventServiceCareer);
    }

    private string SaveTrackSetup(TeamCar car, int trackId, string name, Setup setup)
    {
        if (activeRace?.SetupLocked == true) return "Setup locked: qualifying has started.";
        if (TrackSetup.IsDefaultName(name))
            return "Default is read-only. Enter a new setup name.";

        trackId = TrackCatalog.CurrentTrackId(trackId);
        var previous = car.trackSetups;
        car.trackSetups = (previous ?? Enumerable.Empty<TrackSetup>())
            .Where(item => item != null && !(TrackCatalog.CurrentTrackId(item.trackId) == trackId && item.Matches(name)))
            .Append(new TrackSetup { trackId = trackId, name = name, setup = setup }).ToList();
        if (developmentPractice)
            return null;

        try
        {
            careerSetup.SaveProgress(activeCareer);
            return null;
        }
        catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
        {
            car.trackSetups = previous;
            return "Could not save the setup. " + ex.Message;
        }
    }

    private void ServiceEventCondition(ScheduledRace race, TeamCar car, bool changeTires)
    {
        if (!CanServiceInGarage())
            return;
        var old = ReadEventCondition(race, car);
        int cost = changeTires ? 0 : ComponentRepair.CostForWear(old.damage / 100f, EventServiceCareer.SeriesRules.maximumComponentRepairCost);
        if (EventServiceCareer.teamBalance < cost && cost > 0)
            return;
        var previous = race.playerCondition;
        race.playerCondition = new EventCarCondition
        {
            chassis = car.chassisNumber,
            damage = changeTires ? old.damage : 0f,
            wear = changeTires ? null : old.wear,
            coldPressures = changeTires ? null : old.coldPressures,
            temperature = changeTires ? null : old.temperature
        };
        bool applyToLiveCar = sessionInGarage && trackPreview.SessionRun.Vehicle(0) == car;
        if (applyToLiveCar)
            race.playerCondition.Apply(car);
        SaveEventService(cost, () =>
        {
            race.playerCondition = previous;
            if (applyToLiveCar)
                old.Apply(car);
        }, changeTires ? "Four new tires fitted." : "Body repaired.");
    }

    private EventCarCondition ReadEventCondition(ScheduledRace race, TeamCar car) =>
        sessionInGarage && trackPreview.SessionRun.Vehicle(0) == car
            ? EventCarCondition.Capture(car, car.Damage) : race.ConditionFor(car);

    private bool CanServiceInGarage() => !sessionInGarage || trackPreview.PlayerParkedForGarage;

    private void SaveEventService(int cost, System.Action rollback, string message)
    {
        EventServiceCareer.teamBalance -= cost;
        if (developmentPractice)
        {
            raceEventView.RefreshGarage(message);
            return;
        }

        activeCareer.schedule.componentRepairs += cost;
        try
        {
            careerSetup.SaveProgress(activeCareer);
        }
        catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
        {
            rollback();
            activeCareer.teamBalance += cost;
            activeCareer.schedule.componentRepairs -= cost;
            message = "Could not save the work. " + ex.Message;
        }
        RefreshTeamBalance();
        raceEventView.RefreshGarage(message);
    }

    private static void PrepareEventSessions(ScheduledRace race, TrackDefinition track)
    {
        if (race.sessions == null || race.sessions.Count == 0)
            race.sessions = RaceSession.Create(1, track.NumberOfQualifyingLaps);

        // Migrate the temporary development duration without overwriting configured sessions.
        foreach (var practice in race.sessions.Where(session => session.kind == RaceSessionKind.Practice && !session.Completed && session.minutes == 999))
            practice.minutes = RaceSession.PracticeMinutes;

        var qualifying = race.sessions.Find(session => session.kind == RaceSessionKind.Qualifying);
        if (qualifying != null && !qualifying.Completed)
            qualifying.laps = track.NumberOfQualifyingLaps;

        var raceSession = race.sessions.Find(session => session.kind == RaceSessionKind.Race);
        if (raceSession != null && !raceSession.Completed)
            raceSession.laps = race.EffectiveRaceLaps;
    }

    private bool simulatingScheduleEvent;

    // TEMPORARY testing shortcut: remove with the Simulate Season button when no longer needed.
    private async void SimulateSeasonTemporary()
    {
        if (simulatingScheduleEvent || !CanFieldDriver(activeCareer)) return;
        simulatingScheduleEvent = true;
        bool cancelled = false, success = false;
        string error = null;
        Career draft = null;
        var overlay = new VisualElement(); SettingsFormStyle.Apply(overlay); overlay.AddToClassList("settings-confirmation-overlay");
        var dialog = new VisualElement(); dialog.AddToClassList("settings-confirmation-dialog"); dialog.AddToClassList("used-car-filter-dialog");
        dialog.Add(SettingsFormStyle.Label("Generating Season Results", "settings-group-heading"));
        var wheel = TrackIndicatorIcon(tireWear: false); wheel.style.width = wheel.style.height = 40; wheel.style.alignSelf = Align.Center;
        dialog.Add(wheel);
        var status = SettingsFormStyle.Label("Preparing random results...", "settings-note"); dialog.Add(status);
        var progress = new ProgressBar { lowValue = 0, highValue = 100, value = 0, title = "0%" };
        progress.AddToClassList("settings-operation-progress"); dialog.Add(progress);
        var footer = new VisualElement(); footer.AddToClassList("settings-footer"); footer.style.justifyContent = Justify.FlexEnd;
        var cancel = SettingsFormStyle.Button("Cancel", () => cancelled = true); cancel.tooltip = "Cancel without changing this career.";
        footer.Add(cancel); dialog.Add(footer); overlay.Add(dialog); garageScreen.parent.Add(overlay); garageScreen.SetEnabled(false); cancel.Focus();
        overlay.RegisterCallback<KeyDownEvent>(evt => { if (evt.keyCode == KeyCode.Escape) { cancelled = true; evt.StopPropagation(); } });
        try
        {
            await Awaitable.NextFrameAsync(destroyCancellationToken);
            draft = activeCareer.CopyForTransaction();
            var remaining = draft.schedule.races.Where(race => !race.completed).ToArray();
            var random = new System.Random(); int done = 0;
            int total = remaining.Sum(race => race.sessions?.Count ?? 3);
            foreach (var race in remaining)
            {
                var track = catalog.Find(race.trackId)?.Definition;
                var car = draft.cars.FirstOrDefault(candidate => candidate.chassisNumber == race.carChassisNumber) ?? draft.cars[0];
                race.carNumber = car.number; race.carChassisNumber = car.chassisNumber; race.playerSkipped = false;
                race.sessions ??= RaceSession.Create(1, track?.NumberOfQualifyingLaps ?? 2);
                foreach (var session in race.sessions)
                {
                    if (cancelled) break;
                    if (!session.Completed)
                    {
                        if (track != null) SessionWeatherForecast.Ensure(session, track, race);
                        TemporarySeasonResults.GenerateSession(session, race, TrackPreview.CreateTimingEntries(car, draft), track?.lengthMiles ?? 1f, random);
                    }
                    done++; status.text = race.trackName + " - " + session.name;
                    progress.value = 100f * done / System.Math.Max(1, total); progress.title = $"{progress.value:0}%";
                    wheel.style.rotate = new Rotate(new Angle(done * 23f));
                    await Awaitable.NextFrameAsync(destroyCancellationToken);
                }
                if (cancelled) break;
                race.completed = true;
            }
            if (!cancelled)
            {
                var dates = draft.schedule.races.Select(race => Career.TryReadDate(race.date, out var date) ? date : System.DateTime.MinValue).ToArray();
                if (dates.Length > 0 && dates.Max() < System.DateTime.MaxValue.Date) draft.SetDate(dates.Max().AddDays(1));
                SeasonPrizes.Queue(draft, draft.schedule, TrackPreview.CreateTimingEntries(draft.cars[0], draft));
                careerSetup.SaveProgress(draft); success = true;
            }
        }
        catch (System.OperationCanceledException) { cancelled = true; }
        catch (System.Exception ex) { error = "Could not generate season results. " + ex.Message; Debug.LogException(ex); }
        finally { overlay.RemoveFromHierarchy(); garageScreen.SetEnabled(true); simulatingScheduleEvent = false; }
        if (this == null) return;
        if (success)
        {
            ShowTeam(draft);
            DisplayGaragePage(System.Array.FindIndex(garagePages, page => page.name == "standingsPage"));
        }
        else garageScreen.Q<Label>("scheduleStatus").text = cancelled ? "Season generation cancelled. No changes were saved." : error;
    }

    private static VisualElement CreateSimulationActivityIcon()
    {
        var wheel = TrackIndicatorIcon(tireWear: true);
        wheel.style.width = wheel.style.height = 40;
        wheel.style.alignSelf = Align.Center;
        wheel.style.marginTop = wheel.style.marginBottom = 14;
        return wheel;
    }

    private async void SimulateEvent()
    {
        var race = selectedEvent;
        if (simulatingScheduleEvent || race == null || race.completed || !CanFieldDriver(activeCareer)) return;
        simulatingScheduleEvent = true;
        bool cancelled = false, success = false;
        string error = null, checkpoint = null;
        var overlay = new VisualElement();
        SettingsFormStyle.Apply(overlay);
        overlay.AddToClassList("settings-confirmation-overlay");
        var dialog = new VisualElement();
        dialog.AddToClassList("settings-confirmation-dialog");
        dialog.style.backgroundColor = (Color)new Color32(16, 21, 30, 255);
        var trim = new StyleColor(new Color32(0, 155, 221, 255));
        dialog.style.borderTopColor = dialog.style.borderBottomColor = dialog.style.borderLeftColor = dialog.style.borderRightColor = trim;
        dialog.Add(SettingsFormStyle.Label("Simulating Event", "settings-group-heading"));
        var wheel = CreateSimulationActivityIcon();
        dialog.Add(wheel);
        dialog.Add(SettingsFormStyle.Label(race.trackName, "settings-note"));
        var progress = SettingsFormStyle.Label("Current session: Preparing...", "settings-operation-session");
        progress.style.whiteSpace = WhiteSpace.Normal;
        dialog.Add(progress);
        var progressBar = new ProgressBar { lowValue = 0, highValue = 100, value = 0, title = "0%" };
        progressBar.style.marginTop = progressBar.style.marginBottom = 10;
        progressBar.style.height = 22;
        progressBar.AddToClassList("settings-operation-progress");
        var progressFill = progressBar.Q(className: "unity-progress-bar__progress");
        if (progressFill != null) progressFill.style.backgroundColor = (Color)new Color32(0, 155, 221, 255);
        dialog.Add(progressBar);
        var actions = new VisualElement();
        actions.AddToClassList("settings-footer");
        actions.style.justifyContent = Justify.FlexEnd;
        var cancel = SettingsFormStyle.Button("Cancel", null);
        cancel.tooltip = "Cancel this simulation without applying unfinished results.";
        cancel.clicked += () => { cancelled = true; cancel.SetEnabled(false); progress.text = "Cancelling..."; };
        actions.Add(cancel); dialog.Add(actions); overlay.Add(dialog);
        garageScreen.parent.Add(overlay);
        garageScreen.SetEnabled(false);
        cancel.Focus();
        overlay.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Escape) return;
            cancelled = true; cancel.SetEnabled(false); progress.text = "Cancelling..."; evt.StopPropagation();
        });
        try
        {
            // Let Unity paint the modal before preparing or simulating any sessions.
            await Awaitable.NextFrameAsync(destroyCancellationToken);
            var steps = SimulateEventSteps(race, null, (ok, message) => { success = ok; error = message; },
                () => cancelled, message => progress.text = message,
                () => checkpoint = JsonUtility.ToJson(activeCareer), fraction =>
                {
                    progressBar.value = fraction * 100;
                    progressBar.title = $"{fraction * 100:0}%";
                });
            float rotation = 0;
            while (!cancelled && steps.MoveNext())
            {
                rotation = (rotation + 12f) % 360f;
                wheel.style.rotate = new Rotate(new Angle(rotation));
                await Awaitable.NextFrameAsync(destroyCancellationToken);
            }
        }
        catch (System.OperationCanceledException) { cancelled = true; }
        catch (System.Exception ex)
        {
            error = "Could not simulate the event. " + ex.Message;
            Debug.LogException(ex);
        }
        finally
        {
            if (!success && checkpoint != null)
            {
                JsonUtility.FromJsonOverwrite(checkpoint, activeCareer);
                ApplyCareerCarModels(activeCareer);
                selectedEvent = activeCareer.schedule.races.Find(item => item.trackId == race.trackId && item.date == race.date);
            }
            overlay.RemoveFromHierarchy();
            garageScreen.SetEnabled(true);
            simulatingScheduleEvent = false;
        }
        if (this == null) return;
        RefreshCareerDate(); RefreshTeamBalance(); RefreshSchedule();
        if (success)
            DisplayGaragePage(System.Array.FindIndex(garagePages, page => page.name == "resultsPage"), race);
        else
        {
            garageScreen.Q<Label>("scheduleStatus").text = cancelled ? "Event simulation cancelled." : error;
            simulateEventButton.Focus();
        }
    }

    private bool TrySimulateEvent(ScheduledRace race, System.DateTime? skipToDate, out string error)
    {
        bool success = false;
        string message = null;
        var steps = SimulateEventSteps(race, skipToDate, (ok, text) => { success = ok; message = text; });
        while (steps.MoveNext()) { }
        error = message;
        return success;
    }

    private System.Collections.IEnumerator SimulateEventSteps(ScheduledRace race, System.DateTime? skipToDate,
        System.Action<bool, string> completed, System.Func<bool> cancelled = null,
        System.Action<string> progress = null, System.Action checkpoint = null, System.Action<float> reportProgress = null, System.DateTime? calendarLimit = null)
    {
        string error;
        if (!HasDriver(activeCareer) || (!skipToDate.HasValue && DriverInCustody(activeCareer)))
        {
            completed(false, DriverEntryMessage(activeCareer)); yield break;
        }
        if (ScenarioMail.HasPending(activeCareer))
        {
            error = RequiredEmailMessage;
            completed(false, error); yield break;
        }

        var track = catalog.Find(race.trackId);
        if (track == null)
        {
            error = "The track definition is unavailable.";
            completed(false, error); yield break;
        }

        PrepareEventSessions(race, track.Definition);
        var random = new System.Random();
        bool skipped = skipToDate.HasValue;
        var car = skipped ? activeCareer.cars[0] : EventCar(race);
        if (car == null)
        {
            error = "Choose a car that is available on race day.";
            completed(false, error); yield break;
        }

        if (!PrepareRivalCarsForEvent(race, out error))
        {
            completed(false, error); yield break;
        }

        checkpoint?.Invoke();
        var results = new List<(RaceSession Session, List<SessionCarResult> Results)>();
        var qualifyingOrder = QualifyingOrder(race);
        var condition = skipped ? null : race.ConditionFor(car);
        var pendingSessions = race.sessions.Where(session => !session.Completed).ToArray();
        float SessionWork(RaceSession session) => session.minutes > 0 ? session.minutes * 60f :
            Mathf.Max(1, session.laps) * track.Definition.lengthMiles * 3600f / Mathf.Max(30f, track.Definition.qualifyingReferenceMph);
        float totalWork = Mathf.Max(1, pendingSessions.Sum(SessionWork));
        float finishedWork = 0;
        foreach (var session in pendingSessions)
        {
            if (cancelled?.Invoke() == true) { completed(false, null); yield break; }
            string sessionName = string.IsNullOrWhiteSpace(session.name) ? session.kind.ToString() : session.name;
            progress?.Invoke($"Current session: {sessionName}");
            yield return null;
            var entries = TrackPreview.CreateTimingEntries(car, activeCareer);
            if (skipped) entries = entries.Skip(1).ToArray();
            else (condition ?? race.ConditionFor(car)).Apply(car);
            var simulation = new CarSimulationContext(track.Definition, race, session, entries, isInstant: true,
                preservePlayerCondition: !skipped);
            if (!skipped && activeRace == race) simulation.PlayerSetup = raceEventView.CurrentSetup;
            List<SessionCarResult> sessionResults = null;
            var sessionSteps = RaceSessionRun.SimulateIncremental(simulation, settings.SpeedModel, random,
                qualifyingOrder, !skipped, value => sessionResults = value, cancelled,
                fraction =>
                {
                    progress?.Invoke($"Current session: {sessionName} - {fraction * 100:0}%");
                    reportProgress?.Invoke(0.95f * (finishedWork + SessionWork(session) * fraction) / totalWork);
                });
            while (sessionSteps.MoveNext()) yield return null;
            if (cancelled?.Invoke() == true || sessionResults == null) { completed(false, null); yield break; }
            if (!skipped && sessionResults.Find(result => result.car == car) is SessionCarResult player)
                player.trackCondition = EventCarCondition.Capture(car, player.damageAtFinish);
            if (!skipped)
                condition = sessionResults.Find(result => result.car == car)?.trackCondition ?? condition;
            results.Add((session, sessionResults));
            finishedWork += SessionWork(session);
            reportProgress?.Invoke(0.95f * finishedWork / totalWork);
            if (session.kind == RaceSessionKind.Qualifying)
                qualifyingOrder = sessionResults.Select(result => result.carNumber).ToArray();
        }
        if (!results.Any(item => item.Session.kind == RaceSessionKind.Race))
        {
            error = "This event has no unfinished race session.";
            completed(false, error); yield break;
        }

        if (cancelled?.Invoke() == true) { completed(false, null); yield break; }
        int previousCar = race.carNumber;
        string previousChassis = race.carChassisNumber;
        bool previouslySkipped = race.playerSkipped;
        race.playerSkipped = skipped;
        race.carNumber = skipped ? 0 : car.number;
        race.carChassisNumber = skipped ? string.Empty : car.chassisNumber;
        progress?.Invoke("Saving results...");
        reportProgress?.Invoke(0.95f);
        yield return null;
        if (cancelled?.Invoke() == true)
        {
            race.playerSkipped = previouslySkipped; race.carNumber = previousCar; race.carChassisNumber = previousChassis;
            completed(false, null); yield break;
        }
        try
        {
            SaveSessionResults(race, results, skipToDate, calendarLimit);
        }
        catch (System.Exception ex) when (ex is IOException || ex is System.UnauthorizedAccessException)
        {
            race.playerSkipped = previouslySkipped;
            race.carNumber = previousCar;
            race.carChassisNumber = previousChassis;
            error = "Could not save the simulated event. " + ex.Message;
            completed(false, error); yield break;
        }

        reportProgress?.Invoke(1f);
        completed(true, null);
    }

    private bool simulatingGarageSession;

    private async void SimulateGarageSession(ScheduledRace race, RaceSession session, TrackCatalog.Entry track, TeamCar car)
    {
        var screen = raceEventView.Screen;
        var overlay = new VisualElement();
        SettingsFormStyle.Apply(overlay);
        overlay.AddToClassList("settings-confirmation-overlay");
        var dialog = new VisualElement();
        dialog.AddToClassList("settings-confirmation-dialog");
        var trim = new StyleColor(new Color32(0, 155, 221, 255));
        dialog.style.borderTopColor = dialog.style.borderBottomColor = dialog.style.borderLeftColor = dialog.style.borderRightColor = trim;
        dialog.style.backgroundColor = (Color)new Color32(16, 21, 30, 255);
        dialog.Add(SettingsFormStyle.Label($"Simulating {session.name}", "settings-group-heading"));
        var wheel = CreateSimulationActivityIcon();
        dialog.Add(wheel);
        var status = SettingsFormStyle.Label("Preparing session...", "settings-note");
        dialog.Add(status);
        var progress = new ProgressBar { lowValue = 0, highValue = 100, title = "0%" };
        progress.AddToClassList("settings-operation-progress");
        dialog.Add(progress);
        var footer = new VisualElement();
        footer.AddToClassList("settings-footer");
        footer.style.justifyContent = Justify.FlexEnd;
        bool cancelled = false;
        var cancel = SettingsFormStyle.Button("Cancel", null);
        cancel.tooltip = "Cancel this simulation without saving a result.";
        void Cancel()
        {
            cancelled = true;
            cancel.SetEnabled(false);
            status.text = "Cancelling...";
        }
        cancel.clicked += Cancel;
        overlay.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Escape) return;
            Cancel();
            evt.StopPropagation();
        });
        footer.Add(cancel);
        dialog.Add(footer);
        overlay.Add(dialog);
        screen.parent.Add(overlay);
        screen.SetEnabled(false);
        cancel.Focus();

        simulatingGarageSession = true;
        bool saved = false;
        string error = null;
        string sessionCheckpoint = null;
        ScheduledRace raceCheckpoint = null;
        (TeamCar Car, string Json)[] carCheckpoints = null;
        float rotation = 0f;
        void Report(float fraction)
        {
            progress.value = 100f * Mathf.Clamp01(fraction);
            progress.title = $"{progress.value:0}%";
        }
        void Animate()
        {
            rotation = (rotation + 12f) % 360f;
            wheel.style.rotate = new Rotate(new Angle(rotation));
        }

        try
        {
            await Awaitable.NextFrameAsync(destroyCancellationToken);
            if (cancelled) return;

            sessionCheckpoint = JsonUtility.ToJson(session);
            raceCheckpoint = JsonUtility.FromJson<ScheduledRace>(JsonUtility.ToJson(race));
            var entries = TrackPreview.CreateTimingEntries(car, activeCareer);
            carCheckpoints = entries.Where(entry => entry.car != null).Select(entry => entry.car).Distinct()
                .Select(vehicle => (vehicle, JsonUtility.ToJson(vehicle))).ToArray();

            race.carNumber = car.number;
            race.carChassisNumber = car.chassisNumber;
            race.ConditionFor(car).Apply(car);
            var simulation = new CarSimulationContext(track.Definition, race, session, entries, isInstant: true,
                preservePlayerCondition: true)
            {
                PlayerSetup = raceEventView.CurrentSetup
            };
            List<SessionCarResult> results = null;
            status.text = "Running session...";
            var steps = RaceSessionRun.SimulateIncremental(simulation, settings.SpeedModel, new System.Random(),
                QualifyingOrder(race), true, value => results = value, () => cancelled,
                fraction => Report(0.95f * fraction));
            while (!cancelled && steps.MoveNext())
            {
                Animate();
                await Awaitable.NextFrameAsync(destroyCancellationToken);
            }
            if (cancelled) return;
            if (results.Find(result => result.car == car) is SessionCarResult player)
                player.trackCondition = EventCarCondition.Capture(car, player.damageAtFinish);

            status.text = "Saving results...";
            Report(0.95f);
            await Awaitable.NextFrameAsync(destroyCancellationToken);
            if (!cancelled)
            {
                SaveSessionResults(race, new[] { (Session: session, Results: results) });
                saved = true;
                ShowCompletedSessionResults(session);
            }
        }
        catch (System.OperationCanceledException) { cancelled = true; }
        catch (System.Exception ex)
        {
            Debug.LogException(ex);
            error = "Could not simulate the session. " + ex.Message;
        }
        finally
        {
            if (!saved && raceCheckpoint != null)
            {
                race.carNumber = raceCheckpoint.carNumber;
                race.carChassisNumber = raceCheckpoint.carChassisNumber;
                race.setupLocked = raceCheckpoint.setupLocked;
                race.eventSetups = raceCheckpoint.eventSetups;
                JsonUtility.FromJsonOverwrite(sessionCheckpoint, session);
                if (carCheckpoints != null)
                    foreach (var checkpoint in carCheckpoints)
                        JsonUtility.FromJsonOverwrite(checkpoint.Json, checkpoint.Car);
            }
            overlay.RemoveFromHierarchy();
            screen.SetEnabled(true);
            simulatingGarageSession = false;
            if (this != null && (cancelled || error != null))
                raceEventView.RefreshGarage(cancelled ? "Session simulation cancelled." : error);
        }
    }

    private static int[] QualifyingOrder(ScheduledRace race) =>
        race.sessions?.Find(session => session.kind == RaceSessionKind.Qualifying)?.qualifyingOrder;

    private bool CanSimulateGarageRun() =>
        (developmentPractice || CanFieldDriver(activeCareer)) && activeRace != null && !activeRace.completed &&
        (activeSession == null
            ? activeRace.sessions.Any(session => !session.Completed)
            : sessionInGarage && trackPreview.PlayerParkedForGarage && !trackPreview.SimulatingGarageLaps &&
                !trackPreview.SessionComplete && !trackPreview.SessionRun.Retired(0));

    private void SimulateGarageRun(int laps)
    {
        if (!CanSimulateGarageRun())
            return;
        if (activeSession == null)
        {
            StartNextSession(false);
            if (activeSession == null || trackPreview == null)
                return;
            VisitGarageDuringSession();
            if (!sessionInGarage)
                return;
        }
        trackPreview.SetPlayerSetup(raceEventView.CurrentSetup);
        trackPreview.SessionRun.SetFuel(0, raceEventView.StartingFuelGallons);
        trackPreview.BeginGarageLaps(laps);
        garageCarParked = false;
        raceEventView.RefreshGarageParking();
    }

    private void StartNextSession(bool simulate)
    {
        if (simulatingGarageSession)
            return;
        if (!developmentPractice && !CanFieldDriver(activeCareer))
        { raceEventView.ShowEvent(DriverEntryMessage(activeCareer)); return; }
        if (sessionInGarage)
        {
            if (!simulate)
                ResumeSession();
            return;
        }

        var session = activeRace?.sessions.FirstOrDefault(item => !item.Completed);
        if (session == null || activeRace.completed)
            return;
        if (ScenarioMail.HasPending(activeCareer))
        {
            raceEventView.ShowEvent(RequiredEmailMessage);
            return;
        }
        var track = catalog.Find(activeRace.trackId);
        if (track == null)
        {
            raceEventView.ShowEvent("The track definition is unavailable.");
            return;
        }

        var car = EventCar(activeRace);
        if (car == null)
        {
            raceEventView.ShowEvent("Choose a car that is available on race day.");
            return;
        }

        if (!PrepareRivalCarsForEvent(activeRace, out string error))
        {
            raceEventView.ShowEvent(error);
            return;
        }

        if (simulate)
        {
            SimulateGarageSession(activeRace, session, track, car);
            return;
        }

        activeRace.carNumber = car.number;
        activeRace.carChassisNumber = car.chassisNumber;
        raceEventView.Screen.style.display = DisplayStyle.None;
        var forecast = SessionWeatherForecast.Ensure(session, track.Definition, activeRace);
        bool night = SessionWeatherForecast.IsNightAt(forecast.startHour);
        OpenTrack(track, true, activeRace.carNumber, night);
        if (trackPreview.Image == null)
        {
            ReturnToGarage();
            raceEventView.ShowEvent("Could not start the session.");
            return;
        }
        if (settings.SimulationModel is TrackState001 && !trackPreview.HasSessionPitRoad)
        {
            ReturnToGarage();
            raceEventView.ShowEvent("TrackState001 requires a pit road with an entry and exit for this session.");
            return;
        }

        activeSession = session;
        raceEventView.SetRunningSession(session);
        activeRace.ConditionFor(car).Apply(car);
        var simulation = new CarSimulationContext(track.Definition, activeRace, session,
            TrackPreview.CreateTimingEntries(car, activeCareer), preservePlayerCondition: true)
        {
            PlayerSetup = raceEventView.CurrentSetup,
            EqualConditionTesting = settings.EqualConditionPractice && session.kind == RaceSessionKind.Practice
        };
        trackPreview.BeginSession(simulation, settings.SimulationModel, settings.SpeedModel, settings.BehaviorModel,
            QualifyingOrder(activeRace), settings.UsePaceTruck);
        if (session.kind != RaceSessionKind.Practice)
            careerSetup.SaveProgress(activeCareer); // Persist the qualifying lock before the session runs.
        raceEventView.EqualConditionTesting = trackPreview.EqualConditionTesting;
        trackPreview.SessionRun.SetFuel(0, raceEventView.StartingFuelGallons);
        RefreshTrackTiming();
        sessionPlaybackSpeed = 1;
        sessionPaused = false;
        ApplySessionPlayback();
        ConfigureGarageVisitButton();
        UpdateSessionLabel();
    }

    private void ConfigureGarageVisitButton()
    {
        garageVisitPending = false;
        visitGarageButton.style.display = DisplayStyle.Flex;
        visitGarageButton.SetEnabled(trackPreview.CanVisitGarage);
        visitGarageButton.tooltip = trackPreview.CanVisitGarage ? CallToGarageTooltip :
            "Garage visits require a pit route for this session.";
    }

    private void Update()
    {
        if (!sessionInGarage && trackPreview != null && trackPreview.gameObject.activeSelf)
        {
            UpdateTrackFps();
            RefreshTrackTiming();
        }
        if (activeSession == null || trackPreview?.SessionRun == null)
            return;

        if (garageVisitPending && trackPreview.PlayerParkedForGarage && !trackPreview.SessionComplete)
            OpenGarageAfterParking();
        else if (garageVisitPending && !sessionInGarage)
        {
            bool stopped = trackPreview.PlayerStoppedOnTrack;
            visitGarageButton.SetEnabled(stopped);
            visitGarageButton.tooltip = stopped ? "Car stopped on track. Click to open the garage." :
                GarageArrivalTooltip;
        }
        else if (trackPreview.UsesNorthWilkesboroHaulerGarage && !sessionInGarage)
        {
            visitGarageButton.SetEnabled(trackPreview.CanVisitGarage);
        }

        UpdateDrivingReadouts();
        if (sessionInGarage)
        {
            raceEventView.UpdateSessionClock(trackPreview.SessionRun.Elapsed);
            if (trackPreview.ConsumeGarageRunReport())
            {
                raceEventView.RefreshGarageParking();
                raceEventView.ShowPracticeRunResults();
            }
            bool parked = trackPreview.PlayerParkedForGarage;
            if (parked != garageCarParked)
            {
                garageCarParked = parked;
                raceEventView.RefreshGarageParking();
                if (parked)
                    raceEventView.ShowPracticeRunResults();
            }
        }
        else
            UpdateSessionLabel();
        if (trackPreview.SessionComplete)
        {
            var session = activeSession;
            if (developmentPractice)
            {
                session.results = trackPreview.SessionRun.Results();
                session.completed = true;
                var car = trackPreview.SessionRun.Vehicle(0);
                activeRace.playerCondition = EventCarCondition.Capture(car, car.Damage);
                CloseTrackSession(showTestPracticeGarage: true);
                if (session.kind == RaceSessionKind.Race)
                    raceEventView.ShowResults(session);
                return;
            }
            var results = trackPreview.SessionRun.Results();
            ReturnToGarage();
            CompleteSession(session, results);
        }
    }

    private void LateUpdate()
    {
        if (trackScreen?.style.display != DisplayStyle.Flex)
            return;

        trackSessionMap.UpdateCars();
        if (trackCameraSelection == TrackPreview.CameraAngle.Cycle && Time.unscaledTimeAsDouble >= nextTrackCameraCut)
        {
            ReleaseTrackPointer();
            trackPreview.SetCameraAngle(NextAvailableCamera(trackPreview.ViewAngle, includeCycle: false));
            nextTrackCameraCut = Time.unscaledTimeAsDouble + CameraCycleIntervalSeconds;
            UpdateTrackCameraLabel();
        }
        if (trackPreview.ViewAngle == TrackPreview.CameraAngle.InCar)
            cockpitSteeringWheel.SetAngle(trackPreview.SteeringWheelAngle);
    }

    private void UpdateDrivingReadouts()
    {
        var run = trackPreview.SessionRun;
        int selected = trackPreview.SelectedCarIndex;
        float throttle = Mathf.Clamp01(run.ThrottleInput(selected));
        float brake = Mathf.Clamp01(run.BrakeInput(selected));
        throttleFill.style.height = Length.Percent(throttle * 100f);
        brakeFill.style.height = Length.Percent(brake * 100f);
        throttleDisplay.tooltip = $"Throttle {throttle:P0}";
        brakeDisplay.tooltip = $"Brake {brake:P0}";
        double fuel = run.Fuel(selected);
        float fuelPercent = Mathf.Clamp01((float)(fuel / SessionFuel.TankCapacity)) * 100f;
        fuelFill.style.height = Length.Percent(fuelPercent);
        fuelDisplay.tooltip = $"Fuel {fuel:0.0} / {SessionFuel.TankCapacity:0.0} gal";
        float mph = run.CurrentSpeed(selected) * 3600f / TrackDefinition.MetersPerMile;
        mph = float.IsNaN(mph) || float.IsInfinity(mph) ? 0f : Mathf.Max(0f, mph);
        double rpm = run.EngineRpm(selected);
        drivingInputMph.text = $"{mph:0.0} MPH";
        drivingInputRpm.text = $"RPM {rpm:0}";
        drivingInputGear.text = $"Gear {run.TransmissionGear(selected)}";
        if (!sessionInGarage && trackPreview.ViewAngle == TrackPreview.CameraAngle.InCar)
        {
            UpdateTrackCockpit(trackPreview.SelectedCarType);
            cockpitDashboard.Update(rpm, fuelPercent / 100f, fuel, throttle, brake);
        }
    }

    private void UpdateTrackFps()
    {
        const double sampleSeconds = 0.5;
        if (Time.unscaledTimeAsDouble < trackFpsWarmupUntil)
        {
            trackFpsLabel.text = "FPS settling";
            fpsSampleStart = Time.unscaledTimeAsDouble;
            fpsSampleFrames = 0;
            return;
        }
        fpsSampleFrames++;
        double now = Time.unscaledTimeAsDouble;
        double elapsed = now - fpsSampleStart;
        if (elapsed < sampleSeconds)
            return;

        trackFpsLabel.text = $"FPS {fpsSampleFrames / elapsed:F0}";
        fpsSampleStart = now;
        fpsSampleFrames = 0;
    }

    private void ToggleTrackStandings()
    {
        trackStandingsExpanded = !trackStandingsExpanded;
        trackStandings.EnableInClassList("standings-collapsed", !trackStandingsExpanded);
        trackStandings.style.minWidth = trackStandingsExpanded && scoringStandingsWidth > 0f
            ? new StyleLength(scoringStandingsWidth) : new StyleLength(StyleKeyword.Null);
        toggleTrackStandingsButton.text = trackStandingsExpanded ? "<" : ">";
        toggleTrackStandingsButton.tooltip = trackStandingsExpanded ? "Collapse Scoring Bug" : "Expand Scoring Bug";
        scoringPositions.Clear();
        if (trackStandingsExpanded)
        {
            displayedTimingRevision = -1;
            nextStandingsRefresh = 0f;
            RefreshTrackTiming();
        }
    }

    private bool PitSettingsOpen => trackPitSettings.ClassListContains("pit-settings-open");

    private void TogglePitSettings()
    {
        bool open = !PitSettingsOpen;
        SetPitSettingsOpen(open);
        ApplySessionPlayback();
    }

    private void SetPitSettingsOpen(bool open)
    {
        trackPitSettings.EnableInClassList("pit-settings-open", open);
        pitSettingsButton.tooltip = open ? "Hide pit stop settings" : "Show pit stop settings";
    }

    private void RefreshTrackTiming()
    {
        var timing = trackPreview?.TimingRun;
        if (timing == null)
            return;

        if (Time.unscaledTime < nextStandingsRefresh && timing.TimingRevision == displayedTimingRevision)
            return;

        nextStandingsRefresh = Time.unscaledTime + 0.15f;
        bool changed = timing.TimingRevision != displayedTimingRevision;
        displayedTimingRevision = timing.TimingRevision;
        UpdateScoringHeader(timing);
        if (!trackStandingsExpanded)
            return;
        if (!timing.IsRace && !changed && timingCells.Count > 0)
        {
            for (int positionIndex = 0; positionIndex < timingCells.Count; positionIndex++)
            {
                var cell = timingCells[positionIndex];
                UpdateScoringPosition(cell.Position, (SessionCarResult)cell.Row.userData, positionIndex);
            }
            return;
        }

        int position = 0;
        if (timing.IsRace)
        {
            foreach (var standing in timing.RunningOrder(compareToLeader: true))
            {
                string gap = standing.LapsBehind > 0 ? $"-{standing.LapsBehind}L" : FormatScoringGap(standing.SecondsBehind);
                SetTimingRow(position++, timing.IndexOfCar(standing.Car), standing.Car, gap);
            }
            return;
        }

        float leadingLap = float.PositiveInfinity;
        foreach (var result in timing.FastestLaps())
        {
            if (position == 0)
                leadingLap = result.FastestLap;
            float gap = position == 0 ? float.NaN : result.FastestLap - leadingLap;
            SetTimingRow(position++, timing.IndexOfCar(result), result, FormatScoringGap(gap));
        }
    }

    private static string FormatScoringGap(float seconds) =>
        float.IsNaN(seconds) || float.IsInfinity(seconds) ? "—" : $"+{seconds:0.00}";

    private void UpdateScoringHeader(RaceSessionRun timing)
    {
        var state = trackPreview.TrackState;
        if (state != scoringFlagState)
        {
            scoringFlagState = state;
            scoringFlag.style.backgroundColor = state switch
            {
                SessionTrackState.TrackOpen => ScoringGreen,
                SessionTrackState.Pacing or SessionTrackState.Caution or SessionTrackState.StageEndingUnderCaution
                    or SessionTrackState.FinishUnderCaution => ScoringYellow,
                SessionTrackState.WhiteFlag or SessionTrackState.Checkered => Color.white,
                SessionTrackState.StageComplete => ScoringGreen,
                SessionTrackState.TrackClosed => (Color)new Color32(65, 143, 255, 255),
                _ => (Color)new Color32(100, 118, 139, 255)
            };
            scoringFlag.tooltip = state switch
            {
                SessionTrackState.TrackOpen => "Green flag",
                SessionTrackState.Pacing => "Pace laps",
                SessionTrackState.Caution or SessionTrackState.StageEndingUnderCaution
                    or SessionTrackState.FinishUnderCaution => "Yellow flag",
                SessionTrackState.WhiteFlag => "White flag",
                SessionTrackState.Checkered => "Checkered flag",
                SessionTrackState.StageComplete => "Stage complete",
                SessionTrackState.TrackClosed => "Track closed",
                _ => string.Empty
            };
        }

        var raceDisplay = timing.IsRace ? DisplayStyle.Flex : DisplayStyle.None;
        scoringRaceLine.style.display = raceDisplay;
        scoringRaceProgress.style.display = raceDisplay;
        if (!timing.IsRace)
        {
            scoringSession.text = activeSession?.name ?? "Practice";
            scoringSession.tooltip = scoringSession.text;
            scoringSessionRemaining.text = activeSession?.kind == RaceSessionKind.Practice && activeSession.minutes > 0
                ? FormatScoringClock(Mathf.CeilToInt(timing.Remaining))
                : string.Empty;
            return;
        }

        int laps = timing.RaceLapCautions.Count;
        int target = timing.TargetLaps;
        bool staged = timing.BonusStageCount > 0;
        scoringSession.text = staged ? $"Stage {timing.CurrentStage}" : activeSession?.name ?? "Race";
        scoringSession.tooltip = scoringSession.text;
        scoringSessionRemaining.text = staged ? $"{Mathf.Max(0, timing.StageEnd - laps)} to go" : string.Empty;
        scoringRaceLap.text = $"Lap {Mathf.Clamp(laps + 1, 1, Mathf.Max(target, laps))} / {target}";
        scoringRaceRemaining.text = timing.FinishSuspended ? "Overtime pending" : $"{Mathf.Max(0, target - laps)} to go";
        if (scoringProgressRun != timing || scoringProgressLaps != laps || scoringProgressTarget != target)
        {
            scoringProgressRun = timing;
            scoringProgressLaps = laps;
            scoringProgressTarget = target;
            scoringRaceProgress.MarkDirtyRepaint();
        }
    }

    private static string FormatScoringClock(int seconds) =>
        $"{seconds / 60}:{seconds % 60:00} left";

    private void DrawScoringProgress(MeshGenerationContext context)
    {
        if (scoringProgressRun == null || scoringProgressTarget <= 0)
            return;

        var bounds = context.visualElement.contentRect;
        float tickWidth = bounds.width / Mathf.Max(scoringProgressTarget, scoringProgressLaps);
        float gap = tickWidth >= 2f ? 0.6f : 0f;
        var painter = context.painter2D;
        var cautions = scoringProgressRun.RaceLapCautions;
        for (int flag = 0; flag < 2; flag++)
        {
            bool caution = flag == 1;
            painter.fillColor = caution ? ScoringYellow : ScoringGreen;
            painter.BeginPath();
            bool drawn = false;
            for (int lap = 0; lap < cautions.Count; lap++)
            {
                if (cautions[lap] != caution)
                    continue;

                float left = bounds.xMin + lap * tickWidth;
                float right = left + tickWidth - gap;
                painter.MoveTo(new Vector2(left, bounds.yMin));
                painter.LineTo(new Vector2(right, bounds.yMin));
                painter.LineTo(new Vector2(right, bounds.yMax));
                painter.LineTo(new Vector2(left, bounds.yMax));
                painter.ClosePath();
                drawn = true;
            }
            if (drawn)
                painter.Fill();
        }
    }

    private void SetTimingRow(int position, int carIndex, SessionCarResult result, string gap)
    {
        if (position >= timingCells.Count)
        {
            var row = new VisualElement();
            row.AddToClassList("timing-row");
            row.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.button == 0 && evt.clickCount == 2 && row.userData is SessionCarResult car)
                    trackPreview?.SelectTimingCar(car);
            });
            var cells = (Row: row, Position: TimingCell(string.Empty, "timing-position"),
                Car: TimingCell(string.Empty, "timing-car"), Driver: TimingCell(string.Empty, "timing-driver-name"),
                Gap: TimingCell(string.Empty, "timing-gap"));
            cells.Position.generateVisualContent += DrawScoringPositionTriangle;
            if (position == 0)
                cells.Driver.RegisterCallback<GeometryChangedEvent>(_ => FitScoringDriverNames(cells.Driver));
            timingCells.Add(cells);
            row.Add(cells.Position);
            row.Add(cells.Car);
            row.Add(cells.Driver);
            row.Add(cells.Gap);
            trackTimingRows.Add(row);
        }

        var cell = timingCells[position];
        if (!ReferenceEquals(cell.Row.userData, result))
        {
            cell.Row.userData = result;
            cell.Row.EnableInClassList("timing-player", carIndex == 0);
            cell.Car.text = result.DisplayCarNumber;
            trackPreview.StyleScoringNumber(cell.Car, carIndex);
            cell.Driver.tooltip = result.driverName?.Trim() ?? string.Empty;
            cell.Driver.text = scoringDriverNames.TryGetValue(result, out string displayName)
                ? displayName : cell.Driver.tooltip;
            cell.Gap.tooltip = trackPreview.TimingRun.IsRace ? "Gap to the race leader" : "Gap to the fastest lap";
        }
        cell.Gap.text = gap;
        UpdateScoringPosition(cell.Position, result, position);
    }

    private void FitScoringDriverNames(Label sample)
    {
        if (!trackStandingsExpanded || scoringStandingsWidth > 0f || sample.contentRect.width <= 0f)
            return;

        float available = sample.contentRect.width;
        float nameWidth = available;
        foreach (var cell in timingCells)
        {
            var car = (SessionCarResult)cell.Row.userData;
            string fullName = cell.Driver.tooltip;
            string shortName = AbbreviateScoringDriverName(car, fullName);
            scoringDriverNames[car] = shortName;
            nameWidth = Mathf.Max(nameWidth, Mathf.Ceil(MeasureScoringName(sample, shortName)) + 2f);
        }

        scoringStandingsWidth = trackStandings.layout.width + nameWidth - available;
        trackStandings.style.minWidth = scoringStandingsWidth;
        foreach (var cell in timingCells)
        {
            var car = (SessionCarResult)cell.Row.userData;
            string fullName = cell.Driver.tooltip;
            if (MeasureScoringName(sample, fullName) <= nameWidth)
                scoringDriverNames[car] = fullName;
            cell.Driver.text = scoringDriverNames[car];
        }
    }

    private static float MeasureScoringName(Label sample, string name) =>
        sample.MeasureTextSize(name, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined).x;

    private static string AbbreviateScoringDriverName(SessionCarResult car, string fullName)
    {
        string[] parts = fullName.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return fullName;

        string surname = car.driver?.lastName?.Trim();
        if (string.IsNullOrEmpty(surname))
            surname = parts[parts.Length - 1];
        return $"{StringInfo.GetNextTextElement(parts[0])}. {surname}";
    }

    private void UpdateScoringPosition(Label label, SessionCarResult car, int position)
    {
        float now = Time.unscaledTime;
        if (!scoringPositions.TryGetValue(car, out var previous))
            scoringPositions[car] = previous = (position, 0, 0f);
        else if (previous.Position != position)
            scoringPositions[car] = previous = (position, previous.Position > position ? 1 : -1,
                now + ScoringPositionArrowDuration);

        int direction = now < previous.Until ? previous.Direction : 0;
        bool directionChanged = label.ClassListContains("scoring-position-up") != (direction > 0) ||
            label.ClassListContains("scoring-position-down") != (direction < 0);
        label.text = direction == 0 ? (position + 1).ToString() : string.Empty;
        label.tooltip = direction == 0 ? string.Empty : $"Position {position + 1}";
        label.EnableInClassList("scoring-position-up", direction > 0);
        label.EnableInClassList("scoring-position-down", direction < 0);
        if (directionChanged)
            label.MarkDirtyRepaint();
    }

    private static void DrawScoringPositionTriangle(MeshGenerationContext context)
    {
        var position = context.visualElement;
        bool up = position.ClassListContains("scoring-position-up");
        if (!up && !position.ClassListContains("scoring-position-down"))
            return;

        Rect bounds = position.contentRect;
        if (bounds.width <= 2f || bounds.height <= 4f)
            return;

        float top = bounds.yMin + 2f;
        float bottom = bounds.yMax - 2f;
        float baseY = up ? bottom : top;
        var painter = context.painter2D;
        painter.fillColor = position.resolvedStyle.color;
        painter.BeginPath();
        painter.MoveTo(new Vector2(bounds.center.x, up ? top : bottom));
        painter.LineTo(new Vector2(bounds.xMin + 1f, baseY));
        painter.LineTo(new Vector2(bounds.xMax - 1f, baseY));
        painter.ClosePath();
        painter.Fill();
    }

    private static Label TimingCell(string text, string className)
    {
        var label = new Label(text);
        label.AddToClassList(className);
        return label;
    }

    private void UpdateTrackDisplayControls()
    {
        var button = trackScreen.Q<Button>("trackCarDetailButton");
        bool showPreviewControls = developmentPractice && trackPreview != null;
        bool showLod = showPreviewControls && trackPreview.SupportsCarDetail;
        button.style.display = showLod ? DisplayStyle.Flex : DisplayStyle.None;
        if (showLod)
            button.text = "Car LOD " + (trackPreview.CarDetailIndex + 1) + " - " + TrackPreview.CarDetailLabels[trackPreview.CarDetailIndex];

        bool showGrandstands = showPreviewControls && trackPreview.SupportsGrandstands;
        trackGrandstandsButton.style.display = showGrandstands ? DisplayStyle.Flex : DisplayStyle.None;
        if (showGrandstands)
            trackGrandstandsButton.text = trackPreview.GrandstandsVisible ? "Grandstands On" : "Grandstands Off";

        trackPracticeSkyButton.style.display = showPreviewControls ? DisplayStyle.Flex : DisplayStyle.None;
        if (showPreviewControls)
        {
            trackPracticeSkyButton.tooltip = trackPreview.SupportsIowaVenueLighting
                ? "Switch between daylight and Iowa's race, street, and pit lights."
                : trackPreview.SupportsRaceNightLighting
                    ? "Switch between daylight and race-night floodlights at Bristol."
                    : "Switch between the day and night skies in this test session.";
            UpdatePracticeSkyButtonLabel();
        }
    }

    private void UpdatePracticeSkyButtonLabel()
    {
        string label = trackPreview.TrackSkyIsNight ? "Night Sky" : "Day Sky";
        if (trackPracticeSkyButton.text != label)
            trackPracticeSkyButton.text = label;
    }

    private void TogglePracticeSky()
    {
        if (!developmentPractice || trackPreview?.SessionRun == null)
            return;

        trackPreview.SetPracticeSkyNight(!trackPreview.TrackSkyIsNight);
        if (trackPreview.SupportsRaceNightLighting || trackPreview.SupportsIowaVenueLighting)
            trackFpsWarmupUntil = Time.unscaledTimeAsDouble + 1;
        HideTrackIconTooltip();
        UpdatePracticeSkyButtonLabel();
    }

    private void TogglePracticeGrandstands()
    {
        if (!developmentPractice || trackPreview == null || !trackPreview.SupportsGrandstands)
            return;

        trackPreview.SetGrandstandsVisible(!trackPreview.GrandstandsVisible);
        trackFpsWarmupUntil = Time.unscaledTimeAsDouble + 1;
        HideTrackIconTooltip();
        UpdateTrackDisplayControls();
    }

    private void OpenPracticeCarDetail() => OpenCarDetailSelection(false);

    private void OpenCarDetailSelection(bool saveSetting)
    {
        if ((!developmentPractice && !saveSetting) || trackPreview == null || !trackPreview.SupportsCarDetail)
            return;
        var overlay = new VisualElement { name = "practiceCarDetailDialog" };
        SettingsFormStyle.Apply(overlay);
        overlay.AddToClassList("settings-confirmation-overlay");
        var dialog = new VisualElement();
        dialog.AddToClassList("settings-confirmation-dialog");
        dialog.style.backgroundColor = new Color(16f / 255f, 21f / 255f, 30f / 255f);
        var trim = new StyleColor(new Color(0, 155f / 255f, 221f / 255f));
        dialog.style.borderTopColor = dialog.style.borderBottomColor =
            dialog.style.borderLeftColor = dialog.style.borderRightColor = trim;
        dialog.Add(SettingsFormStyle.Label("Car Detail", "settings-group-heading"));
        dialog.Add(SettingsFormStyle.Label(saveSetting ? "Choose a saved LOD level for cars in the track view." : "Applies to every car in this practice session. Testing goal 120 FPS, minimum 100 FPS.", "settings-note"));
        var error = SettingsFormStyle.Label("", "settings-validation-summary");
        dialog.Add(error);
        for (int i = 0; i < TrackPreview.CarDetailLabels.Length; i++)
        {
            int index = i;
            string label = (i + 1) + " - " + TrackPreview.CarDetailLabels[i] + (i == trackPreview.CarDetailIndex ? " - Selected" : "");
            var button = SettingsFormStyle.Button(label, () =>
            {
                if (!trackPreview.SetCarDetail(index))
                {
                    SettingsFormStyle.SetValidationMessage(error, "Could not load this detail level.", true);
                    return;
                }
                if (saveSetting) settings.SaveCarLod(index + 1);
                trackFpsWarmupUntil = Time.unscaledTimeAsDouble + 1;
                UpdateTrackDisplayControls();
                Close();
            });
            button.tooltip = "Use level " + (i + 1) + " for the field. Cars with one model use it at every level.";
            dialog.Add(button);
        }
        var cancelFooter = new VisualElement();
        cancelFooter.AddToClassList("settings-footer");
        cancelFooter.Add(SettingsFormStyle.Button("Cancel", Close));
        dialog.Add(cancelFooter);
        overlay.Add(dialog);
        overlay.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Escape) return;
            Close();
            evt.StopPropagation();
        });
        trackScreen.SetEnabled(false);
        trackScreen.parent.Add(overlay);
        dialog.Query<Button>().First()?.Focus();

        void Close()
        {
            overlay.RemoveFromHierarchy();
            trackScreen.SetEnabled(true);
            trackScreen.Q<Button>("trackCarDetailButton").Focus();
        }
    }

    private void OpenTrackSettings()
    {
        var menu = new GenericDropdownMenu();
        var weather = trackPreview?.SessionRun?.Weather;
        if (weather != null && weather.CanLock)
            menu.AddItem("Lock Weather", weather.IsLocked, () => weather.SetLocked(!weather.IsLocked));
        else
            menu.AddDisabledItem("Lock Weather", false);
        menu.AddItem("Traffic Interactions", trafficInteractionsEnabled,
            () => SetTrafficInteractions(!trafficInteractionsEnabled));
        menu.AddItem("Collisions", trackCollisionsEnabled, () => SetTrackCollisions(!trackCollisionsEnabled));
        if (trackPreview != null && trackPreview.SupportsCarDetail)
            menu.AddItem("Car LOD - " + (trackPreview.CarDetailIndex + 1), false, () => OpenCarDetailSelection(true));
        if (trackPreview != null)
            menu.AddItem("Track Anti-Aliasing - " + GameSettings.TrackMsaaLabel(settings.TrackMsaaSamples),
                false, OpenTrackMsaaPicker);
        menu.DropDown(trackSettingsButton.worldBound, trackSettingsButton, DropdownMenuSizeMode.Content);
    }

    private void OpenTrackMsaaPicker()
    {
        if (trackPreview == null || trackPreview.Image == null || trackScreen.parent == null || closeTrackMsaaPicker != null)
            return;

        bool wasPaused = sessionPaused;
        sessionPaused = true;
        ApplySessionPlayback();
        closeTrackMsaaPicker = GameSettings.OpenTrackMsaaPicker(trackScreen.parent, trackScreen, trackSettingsButton,
            settings.TrackMsaaSamples, settings.SaveTrackMsaaSamples, () =>
            {
                closeTrackMsaaPicker = null;
                sessionPaused = wasPaused;
                ApplySessionPlayback();
            });
    }

    private void SetTrafficInteractions(bool enabled)
    {
        if (trafficInteractionsEnabled == enabled)
            return;

        trafficInteractionsEnabled = enabled;
        PlayerPrefs.SetInt("StockCarManager.Settings.TrafficInteractions", enabled ? 1 : 0);
        PlayerPrefs.Save();
        if (trackPreview != null)
            trackPreview.TrafficLaneChangesEnabled = enabled;
    }

    private void SetTrackCollisions(bool enabled)
    {
        if (trackCollisionsEnabled == enabled)
            return;

        trackCollisionsEnabled = enabled;
        PlayerPrefs.SetInt("StockCarManager.Settings.TrackCollisions", enabled ? 1 : 0);
        PlayerPrefs.Save();
        if (trackPreview != null)
            trackPreview.SetCollisionsEnabled(enabled);
    }

    private void ApplySessionPlayback()
    {
        trackPreview?.SetSessionSpeed(sessionPaused || PitSettingsOpen ? 0 : sessionPlaybackSpeed);
        UpdatePlaybackControls();
    }

    private void UpdatePlaybackControls()
    {
        bool available = activeSession != null && !sessionInGarage && !PitSettingsOpen;
        playSessionButton.SetEnabled(available && sessionPaused);
        pauseSessionButton.SetEnabled(available && !sessionPaused);
        playbackSpeedButton.SetEnabled(available);
        requestCautionButton.SetEnabled(CanCallCaution);
        playSessionButton.tooltip = $"Play at {sessionPlaybackSpeed}x";
        playbackSpeedButton.text = $"{sessionPlaybackSpeed}x \u25BE";
        playbackSpeedButton.tooltip = $"Playback speed \u00B7 {sessionPlaybackSpeed}x";
    }

    private bool CanCallCaution => activeSession != null && !sessionInGarage && !PitSettingsOpen &&
        trackPreview?.CanRequestCaution == true;

    private void CallCaution()
    {
        if (!CanCallCaution)
            return;

        trackPreview.RequestCaution();
        UpdateSessionLabel();
    }

    private void PlaySession()
    {
        sessionPaused = false;
        ApplySessionPlayback();
    }

    private void PauseSession()
    {
        sessionPaused = true;
        ApplySessionPlayback();
    }

    private void OpenPlaybackSpeedMenu()
    {
        var menu = new GenericDropdownMenu();
        foreach (int speed in SessionPlaybackSpeeds)
        {
            menu.AddItem($"{speed}x", sessionPlaybackSpeed == speed, () =>
            {
                sessionPlaybackSpeed = speed;
                ApplySessionPlayback();
            });
        }
        menu.DropDown(playbackSpeedButton.worldBound, playbackSpeedButton, DropdownMenuSizeMode.Content);
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
            practiceStateLabel.text = oneToGo ? "One To GO" : "Pace Laps";
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
                activeSession?.kind == RaceSessionKind.Race ? "Race Over" : "Checkered Flag - Return to Pits";
        }
        else if (state == SessionTrackState.Caution || state == SessionTrackState.StageEndingUnderCaution ||
            state == SessionTrackState.FinishUnderCaution)
        {
            primaryPracticeFlag.style.backgroundColor = Color.yellow;
            practiceStateLabel.text = state == SessionTrackState.FinishUnderCaution ? "Race Over Under Caution" : oneToGo ? "One To GO" : "Caution";
        }
        else if (returning)
        {
            primaryPracticeFlag.style.backgroundColor = (Color)new Color32(226, 66, 66, 255);
            practiceStateLabel.text = "Return to Pits";
        }
        practiceFlags.tooltip = state.HasValue ? practiceStateLabel.text : string.Empty;
    }

    private void UpdatePlayerPitRequest(bool queue)
    {
        if (trackPreview == null || !(queue ? trackPreview.CanRequestPlayerPit : trackPreview.CanEditQueuedPlayerPit))
            return;

        int tires = 0;
        for (int i = 0; i < pitTires.Length; i++)
        {
            if (pitTires[i].value)
                tires |= 1 << i;
        }
        float fuel = refuelAtStop.value ? pitFuelAmount.value : 0f;
        var pressures = pitPressures.Select(field => field.value).ToArray();
        if (queue)
            trackPreview.RequestPlayerPit(fuel, tires, pitRepairAmount.value, pressures);
        else
            trackPreview.EditPlayerPitRequest(fuel, tires, pitRepairAmount.value, pressures);
    }

    private void UpdatePlayerPitPanel(RaceSessionRun run)
    {
        var car = run.Vehicle(0);
        if (pressurePanelRun != run)
        {
            pressurePanelRun = run;
            for (int i = 0; i < 4; i++) pitPressures[i].SetValueWithoutNotify(VehicleSpeed003.ColdPressure(run.SetupForCar(0), i, car?.GetTire(i)));
        }
        double fuel = run.Fuel(0);
        float damage = run.DisplayDamage(0);
        playerCarStatus.text = $"Fuel {fuel:F1} / {SessionFuel.TankCapacity:F0} gal";
        pitFuelFill.style.width = Length.Percent((float)(fuel / SessionFuel.TankCapacity * 100));
        pitDamageLabel.text = $"Damage {damage:F0}%";
        pitDamageFill.style.width = Length.Percent(damage);
        pitFuelAmountLabel.text = pitFuelAmount.value >= SessionFuel.TankCapacity
            ? "Fill tank" : $"Add {pitFuelAmount.value:F1} gal";
        pitRepairAmountLabel.text = $"Repair up to {pitRepairAmount.value:F0}%";
        bool canRequest = trackPreview.CanRequestPlayerPit;
        bool canEdit = canRequest || trackPreview.CanEditQueuedPlayerPit;
        requestPitStop.SetEnabled(canRequest);
        refuelAtStop.SetEnabled(canEdit);
        pitFuelAmount.SetEnabled(canEdit && refuelAtStop.value);
        pitRepairAmount.SetEnabled(canEdit);
        playerPitStatus.text = trackPreview.PlayerPitStatus;
        for (int i = 0; i < pitTires.Length; i++)
        {
            float wear = Mathf.Clamp01(car?.GetTire(i)?.wear ?? 0f);
            pitTireFill[i].style.height = Length.Percent((1f - wear) * 100f);
            pitTireWear[i].text = wear.ToString("P0");
            pitTires[i].SetEnabled(canEdit);
            pitPressures[i].SetEnabled(canEdit && pitTires[i].value && !run.SetupLocked);
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
        snapshotStatus.style.display = DisplayStyle.Flex;
    }

    private void UpdateSessionLabel()
    {
        snapshotButton.SetEnabled(trackPreview.CanSaveSnapshot);
        requestCautionButton.SetEnabled(CanCallCaution);
        if (developmentPractice)
            UpdatePracticeSkyButtonLabel();
        var run = trackPreview.SessionRun;
        playerTrackLaps.text = $"Run laps: {trackPreview.PlayerRunLaps} / Session laps: {run.PlayerLaps}";
        trackWeatherLabel.text = $"Air {run.Weather.AmbientFahrenheit:0}\u00B0F \u00B7 Track {run.Weather.TrackFahrenheit:0}\u00B0F";
        trackWeatherLabel.tooltip = run.Weather.Summary;
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
            string stage = run.IsRace && run.BonusStageCount > 0 ? $"  /  Stage {run.CurrentStage} of {run.BonusStageCount + 1}" : string.Empty;
            string pitStatus = trackPreview.PitRoadOpen is bool open ? (open ? "  /  Pits Open" : "  /  Pits Closed") : string.Empty;
            int laps = run.IsRace ? run.LeaderLaps : run.PlayerLaps;
            string progress = run.FinishSuspended ? $"{laps} laps - Overtime pending" : $"{laps} of {run.TargetLaps} laps";
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
            raceEventView.ShowEvent("Could not save the session result. " + ex.Message);
            return;
        }

        ShowCompletedSessionResults(session);
    }

    private void ShowCompletedSessionResults(RaceSession session)
    {
        RefreshTeamBalance();
        raceEventView.ShowResults(session);
    }

    private void SaveSessionResults(ScheduledRace race,
        IReadOnlyList<(RaceSession Session, List<SessionCarResult> Results)> results, System.DateTime? skipToDate = null,
        System.DateTime? calendarLimit = null)
    {
        var season = activeCareer.schedule;
        var previousCondition = race.playerCondition;
        var previousSessions = results.Select(item => (item.Session, item.Session.results,
            item.Session.temporaryResults, item.Session.completed, item.Session.practiceWear, item.Session.qualifyingOrder,
            item.Session.playerContacts, item.Session.playerReport)).ToArray();
        int inboxCount = activeCareer.inbox?.Count ?? 0;
        var wearBefore = new Dictionary<(TeamCar Car, CarComponent Component), float>();
        var track = catalog.Find(race.trackId)?.Definition;
        var wearRandom = new System.Random();
        long previousBalance = activeCareer.teamBalance;
        long previousPayroll = season.payrollPaid;
        long previousSponsorIncome = season.sponsorIncome;
        long previousRaceSponsorIncome = race.sponsorIncome;
        var previousSponsorPayments = race.sponsorPayments;
        var previousRaceExpenses = race.expenses;
        var previousSponsor = activeCareer.PrimarySponsor;
        string previousDate = activeCareer.currentDate;
        bool previouslyCompleted = race.completed;
        var previousArchives = activeCareer.completedSeasons;
        var previousRanking = (activeCareer.previousSeasonRankingYear, activeCareer.previousSeasonRank, activeCareer.previousSeasonFieldSize);
        var previousPrizeAwards = activeCareer.seasonPrizeAwards;
        double previousPointsFundMultiplier = activeCareer.pointsFundMultiplier;
        System.Action undoRepairs = null;
        System.Action undoMorale = null;
        System.Action undoResignations = null;
        System.Action undoMerchandising = null, undoRolloverMerchandising = null;
        System.Action undoDriverProgress = null;
        System.Action undoEmployeeProgress = null;
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
                {
                    session.playerReport = new PlayerSessionReport(player, item.Results.Count, playerWear);
                    race.playerCondition = player.trackCondition ?? EventCarCondition.Capture(player.car, player.damageAtFinish);
                }
                session.playerContacts = item.Results.FirstOrDefault(result => result.contacts?.Count > 0)?.contacts;
                session.results = item.Results;
                session.temporaryResults = null;
                if (session.kind != RaceSessionKind.Race)
                {
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
                undoMorale = RaceMorale.Apply(activeCareer, race);
                undoDriverProgress = DriverRaceProgress.Apply(activeCareer, race, track);
                undoEmployeeProgress = EmployeeRaceProgress.Apply(activeCareer, race, track);
                CrewChiefMail.SendRecap(activeCareer, race);
                BroadcastCoverage.SendRecaps(activeCareer, race);
                ScenarioMail.MaybeSendPostRace(activeCareer, race);
                long annualPayroll = activeCareer.AnnualPayroll;
                int completedRaces = season.races.Count(item => item.completed);
                long installment = RaceFinances.PayrollInstallment(annualPayroll, season.races.Count, completedRaces);
                activeCareer.teamBalance -= installment;
                season.payrollPaid += installment;
                if (!race.playerSkipped)
                {
                    var enteredCar = EventCar(race);
                    race.expenses = RaceExpenses.Generate(activeCareer.SeriesRules, activeCareer.EngineSupplierFor(enteredCar));
                    activeCareer.teamBalance -= race.expenses.Total;
                }
                int finish = race.playerSkipped ? 0 : race.sessions.Find(session => session.kind == RaceSessionKind.Race)
                    .results.Find(result => result.carNumber == race.carNumber)?.position ?? 0;
                activeCareer.primarySponsor = Sponsorships.Settle(previousSponsor,
                    finish, season.season, out long income, out var payments);
                race.sponsorPayments = payments;
                race.sponsorIncome = income;
                season.sponsorIncome += income;
                activeCareer.teamBalance += income;
            }

            var postRaceDate = skipToDate;
            if (!previouslyCompleted && race.completed && !postRaceDate.HasValue &&
                Career.TryReadDate(race.date, out var raceDate) && raceDate < System.DateTime.MaxValue.Date)
                postRaceDate = raceDate.AddDays(1);

            if (Career.TryReadDate(activeCareer.currentDate, out var currentDate) && postRaceDate.HasValue)
            {
                var seasonStart = new System.DateTime(season.season, 1, 1);
                if (postRaceDate.Value < currentDate)
                    postRaceDate = currentDate;
                else if (currentDate < seasonStart && postRaceDate.Value > seasonStart &&
                    !ScenarioMail.HasSeasonSponsorship(activeCareer))
                    postRaceDate = seasonStart;
            }

            if (postRaceDate.HasValue && !ScenarioMail.HasPending(activeCareer))
                undoMerchandising = activeCareer.SetDate(postRaceDate.Value);

            if (!previouslyCompleted && race.completed && season.races.All(item => item.completed))
            {
                SeasonPrizes.Queue(activeCareer, season, TrackPreview.CreateTimingEntries(activeCareer.cars[0], activeCareer));
                catalog.Refresh();
                activeCareer.schedule = careerSetup.GenerateSeason(activeCareer, season.season + 1);
                var nextSeasonStart = new System.DateTime(activeCareer.schedule.season, 1, 1);
                if ((!calendarLimit.HasValue || nextSeasonStart <= calendarLimit.Value) &&
                    !ScenarioMail.HasPending(activeCareer) &&
                    (!Career.TryReadDate(activeCareer.currentDate, out var rolloverDate) || rolloverDate < nextSeasonStart))
                    undoRolloverMerchandising = activeCareer.SetSeasonStartDate();
                ScenarioMail.SendSeasonStart(activeCareer);
                var ranking = SeasonSponsorRanking(season);
                activeCareer.previousSeasonRankingYear = season.season;
                activeCareer.previousSeasonRank = ranking.Rank;
                activeCareer.previousSeasonFieldSize = ranking.FieldSize;
                // Persist only the new season's detailed results. Keep the small sponsorship summary.
                activeCareer.completedSeasons = new List<SeasonSchedule>();
            }

            if (!previouslyCompleted && race.completed)
            {
                undoRepairs = UpdateCareerRepairs();
                undoResignations = RaceMorale.ApplyResignations(activeCareer, race, new System.Random());
            }

            if (Career.TryReadDate(activeCareer.currentDate, out var newDate))
            {
                if (Career.TryReadDate(previousDate, out var oldDate))
                {
                    ScenarioMail.SendNewYear(activeCareer, oldDate, newDate);
                    ScenarioMail.SendSolicitations(activeCareer, oldDate, newDate);
                }
                SendSeasonSponsorshipIfDue(newDate);
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
            undoResignations?.Invoke();
            undoRepairs?.Invoke();
            undoRolloverMerchandising?.Invoke();
            undoMerchandising?.Invoke();
            activeCareer.teamBalance = previousBalance;
            season.payrollPaid = previousPayroll;
            season.sponsorIncome = previousSponsorIncome;
            race.sponsorIncome = previousRaceSponsorIncome;
            race.sponsorPayments = previousSponsorPayments;
            race.expenses = previousRaceExpenses;
            activeCareer.primarySponsor = previousSponsor;
            undoMorale?.Invoke();
            undoDriverProgress?.Invoke();
            undoEmployeeProgress?.Invoke();
            foreach (var entry in wearBefore)
                entry.Key.Car.ComponentWear(entry.Key.Component) = entry.Value;
            activeCareer.currentDate = previousDate;
            CrewChiefMail.UndoNewMessages(activeCareer, inboxCount);
            race.completed = previouslyCompleted;
            race.playerCondition = previousCondition;
            activeCareer.schedule = season;
            activeCareer.completedSeasons = previousArchives;
            activeCareer.seasonPrizeAwards = previousPrizeAwards;
            activeCareer.pointsFundMultiplier = previousPointsFundMultiplier;
            (activeCareer.previousSeasonRankingYear, activeCareer.previousSeasonRank, activeCareer.previousSeasonFieldSize) = previousRanking;
            throw;
        }
        if (undoResignations != null)
            RefreshDriverLabel(activeCareer);
        if (activeCareer.schedule != season && garageScreen.Q("resultsPage").style.display == DisplayStyle.Flex)
            BuildTeamResults(null);
    }

    private void LeaveRaceEvent()
    {
        if (developmentPractice)
        {
            ReturnToGarage();
            return;
        }

        if (sessionInGarage)
            ReturnToGarage();
        activeRace = null;
        raceEventView.Screen.style.display = DisplayStyle.None;
        garageScreen.style.display = DisplayStyle.Flex;
        RefreshCareerDate();
        RefreshSchedule();
        int shop = System.Array.FindIndex(garagePages, page => page.name == "shopPage");
        DisplayGaragePage(shop);
        garageTabs[shop].Focus();
    }

    private string[] ActiveSponsorNames()
    {
        string sponsor = ActiveSponsorName();
        return sponsor == null ? System.Array.Empty<string>() : new[] { sponsor };
    }

    private string ActiveSponsorName() => activeCareer?.PrimarySponsor is SponsorContract contract
        ? Sponsorships.CompanyName(contract.offer.companyId) : null;

    private void OpenTrack(TrackCatalog.Entry track, bool restart, int carNumber, bool night = false, TeamCar playerCar = null)
    {
        if (track == null)
            return;

        if (restart || trackPreview == null || trackPreview.SessionRun != null || previewTrack != track || previewCarNumber != carNumber)
        {
            if (trackPreview != null)
            {
                trackPreview.FinishPracticeSetups();
        trackPreview.FinishGarageRun("Session ended");
                trackPreview.gameObject.SetActive(false);
                Destroy(trackPreview.gameObject);
            }
            raceViewport.style.backgroundImage = StyleKeyword.None;
            var preview = new GameObject(track.Definition.name + " Preview");
            preview.transform.SetParent(transform, false);
            displayedTimingRevision = -1;
            nextStandingsRefresh = 0f;
            timingCells.Clear();
            scoringPositions.Clear();
            scoringDriverNames.Clear();
            scoringStandingsWidth = 0f;
            trackStandings.style.minWidth = StyleKeyword.Null;
            trackTimingRows.Clear();
            trackPreview = preview.AddComponent<TrackPreview>();
            trackPreview.SetCollisionsEnabled(trackCollisionsEnabled);
            trackPreview.TrafficLaneChangesEnabled = trafficInteractionsEnabled;
            trackPreview.Initialize(track.Definition, carNumber, ActiveSponsorNames(), playerCar ?? EventCar(activeRace), night,
                rivalCars: EventServiceCareer?.rivalCars);
            previewTrack = track;
            previewCarNumber = carNumber;
            trackSessionMap.SetPreview(trackPreview.Image != null ? trackPreview : null);
            if (trackPreview.Image != null)
            {
                var savedAngle = (TrackPreview.CameraAngle)PlayerPrefs.GetInt(TrackCameraAnglePreference);
                ApplyTrackCameraSelection(savedAngle);
                raceViewport.style.backgroundImage = Background.FromRenderTexture(trackPreview.Image);
            }
        }

        cycleTrackCameraButton.SetEnabled(trackPreview.Image != null);
        trackScreen.Q<Label>("selectedTrackName").text = track.Definition.name;
        trackPreview.gameObject.SetActive(true);
        visitGarageButton.style.display = DisplayStyle.None;
        garageScreen.style.display = DisplayStyle.None;
        fpsSampleStart = Time.unscaledTimeAsDouble;
        fpsSampleFrames = 0;
        trackFpsLabel.text = "FPS --";
        HideTrackIconTooltip();
        UpdateTrackDisplayControls();
        trackScreen.style.display = DisplayStyle.Flex;
        returnToGarageButton.Focus();
    }

    private Rect RaceImageRect()
    {
        if (trackPreview == null || trackPreview.Image == null)
            return Rect.zero;

        var viewport = raceViewport.contentRect;
        float aspect = (float)trackPreview.Image.width / trackPreview.Image.height;
        // Match the centered cover scaling used by the viewport background.
        float width = Mathf.Max(viewport.width, viewport.height * aspect);
        return new Rect(0f, 0f, width, width / aspect) { center = viewport.center };
    }

    private void ZoomTrack(WheelEvent evt)
    {
        var imageRect = RaceImageRect();
        if (!evt.ctrlKey || !imageRect.Contains(evt.localMousePosition) || Mathf.Approximately(evt.delta.y, 0f) ||
            trackPreview?.CanZoom != true)
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

        if (trackPreview.ViewAngle != TrackPreview.CameraAngle.Player ||
            dragPointerId != -1 || (evt.button != 0 && (evt.button != 1 || !evt.shiftKey)))
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

    private bool IsTrackCameraAvailable(TrackPreview.CameraAngle angle) =>
        System.Enum.IsDefined(typeof(TrackPreview.CameraAngle), angle) &&
        (angle != TrackPreview.CameraAngle.PitBox || trackPreview.CanUsePitBoxCamera);

    private TrackPreview.CameraAngle NextAvailableCamera(TrackPreview.CameraAngle angle, bool includeCycle)
    {
        int index = System.Array.IndexOf(TrackCameraChoices, angle);
        do
        {
            index = (index + 1) % TrackCameraChoices.Length;
            angle = TrackCameraChoices[index];
        }
        while ((!includeCycle && angle == TrackPreview.CameraAngle.Cycle) || !IsTrackCameraAvailable(angle));
        return angle;
    }

    private void CycleTrackCameraAngle() =>
        SelectTrackCamera(NextAvailableCamera(trackCameraSelection, includeCycle: true));

    private void SelectTrackCamera(TrackPreview.CameraAngle angle)
    {
        if (trackPreview?.Image == null || !IsTrackCameraAvailable(angle))
            return;

        ReleaseTrackPointer();
        ApplyTrackCameraSelection(angle);
        PlayerPrefs.SetInt(TrackCameraAnglePreference, (int)trackCameraSelection);
        PlayerPrefs.Save();
    }

    private void ApplyTrackCameraSelection(TrackPreview.CameraAngle angle)
    {
        trackCameraSelection = IsTrackCameraAvailable(angle) ? angle : TrackPreview.CameraAngle.Player;
        if (trackCameraSelection == TrackPreview.CameraAngle.Cycle)
            nextTrackCameraCut = Time.unscaledTimeAsDouble + CameraCycleIntervalSeconds;
        else
            trackPreview.SetCameraAngle(trackCameraSelection);
        UpdateTrackCameraLabel();
    }

    private void UpdateTrackCameraLabel()
    {
        string activeAngle = TrackPreview.CameraAngleName(trackPreview.ViewAngle);
        trackCameraAngle.text = trackCameraSelection == TrackPreview.CameraAngle.Cycle ? $"Cycle / {activeAngle}" : activeAngle;
        var mirror = trackPreview.ViewAngle == TrackPreview.CameraAngle.InCar ? trackPreview.RearViewImage : null;
        trackCockpit.style.display = mirror != null ? DisplayStyle.Flex : DisplayStyle.None;
        trackScreen.EnableInClassList("in-car-view", mirror != null);
        if (mirror != null)
            UpdateTrackCockpit(trackPreview.SelectedCarType);
        cockpitMirrorImage.style.backgroundImage = mirror != null
            ? new StyleBackground(Background.FromRenderTexture(mirror))
            : new StyleBackground(StyleKeyword.None);
    }

    private void UpdateTrackCockpit(CarTypeDefinition carType)
    {
        if (cockpitCarType == carType)
            return;

        if (!string.IsNullOrEmpty(cockpitCarType?.cockpitStyle))
            trackScreen.RemoveFromClassList(cockpitCarType.cockpitStyle);
        cockpitCarType = carType;
        if (!string.IsNullOrEmpty(carType.cockpitStyle))
            trackScreen.AddToClassList(carType.cockpitStyle);

        cockpitDashboard.Configure(carType);
        Vector2 mirrorOffset = carType.rearviewMirrorOffset;
        cockpitMirrorImage.parent.style.translate = new Translate(mirrorOffset.x, mirrorOffset.y, 0f);
    }

    private void BeginCameraMenuHold(PointerDownEvent evt)
    {
        if (evt.button != 1)
            return;

        CancelCameraMenuHold();
        cameraMenuHold = cycleTrackCameraButton.schedule.Execute(OpenTrackCameraMenu).StartingIn(CameraMenuHoldMs);
        evt.StopPropagation();
    }

    private void EndCameraMenuHold(PointerUpEvent evt)
    {
        if (evt.button == 1)
            CancelCameraMenuHold();
    }

    private void CancelCameraMenuHold(EventBase evt = null)
    {
        cameraMenuHold?.Pause();
        cameraMenuHold = null;
    }

    private void OpenTrackCameraMenu()
    {
        cameraMenuHold = null;
        if (trackScreen.style.display != DisplayStyle.Flex || trackPreview?.Image == null)
            return;

        HideTrackIconTooltip();
        var menu = new GenericDropdownMenu();
        foreach (var angle in TrackCameraChoices)
        {
            string label = TrackPreview.CameraAngleName(angle);
            if (IsTrackCameraAvailable(angle))
                menu.AddItem(label, angle == trackCameraSelection, () => SelectTrackCamera(angle));
            else
                menu.AddDisabledItem(label, false);
        }
        menu.DropDown(cycleTrackCameraButton.worldBound, cycleTrackCameraButton, DropdownMenuSizeMode.Content);
    }

    private void ResetTrackCamera()
    {
        ReleaseTrackPointer();
        trackPreview?.ResetView();
    }

    private VisualElement TrackIconAt(Vector2 position)
    {
        var element = trackScreen.panel?.Pick(position);
        while (element != null && element != trackScreen &&
               !element.ClassListContains("track-header-icon") &&
               !element.ClassListContains("track-icon-tooltip-target"))
        {
            element = element.parent;
        }

        return element != trackScreen && !string.IsNullOrEmpty(element?.tooltip) ? element : null;
    }

    private void UpdateTrackIconTooltip(PointerMoveEvent evt)
    {
        trackIconHoverPosition = evt.position;
        var icon = TrackIconAt(trackIconHoverPosition);
        if (icon != hoveredTrackIcon)
        {
            HideTrackIconTooltip();
            if (icon != null)
            {
                hoveredTrackIcon = icon;
                pendingTrackIconTooltip = trackScreen.schedule.Execute(ShowTrackIconTooltip)
                    .StartingIn(TrackIconTooltipDelayMs);
            }
        }
        else if (icon != null && trackIconTooltip.style.display == DisplayStyle.Flex)
        {
            ShowTrackIconTooltip();
        }
    }

    private void ShowTrackIconTooltip()
    {
        pendingTrackIconTooltip = null;
        var icon = TrackIconAt(trackIconHoverPosition);
        if (icon == null || icon != hoveredTrackIcon)
        {
            HideTrackIconTooltip();
            return;
        }

        trackIconTooltip.text = icon.tooltip;
        trackIconTooltip.style.display = DisplayStyle.Flex;
        var bounds = trackScreen.contentRect;
        var size = trackIconTooltip.MeasureTextSize(icon.tooltip, Mathf.Min(280f, bounds.width - 16f),
            VisualElement.MeasureMode.AtMost, 0f, VisualElement.MeasureMode.Undefined);
        float width = Mathf.Ceil(size.x) + 17f;
        float height = Mathf.Ceil(size.y) + 10f;
        Vector2 local = trackScreen.WorldToLocal(trackIconHoverPosition);
        trackIconTooltip.style.width = width;
        trackIconTooltip.style.left = Mathf.Clamp(local.x + 12f, 0f, Mathf.Max(0f, bounds.width - width));
        float top = local.y + 16f;
        trackIconTooltip.style.top = top + height <= bounds.height ? top : Mathf.Max(0f, local.y - height - 8f);
    }

    private void HideTrackIconTooltip(EventBase evt = null)
    {
        pendingTrackIconTooltip?.Pause();
        pendingTrackIconTooltip = null;
        hoveredTrackIcon = null;
        if (trackIconTooltip != null)
            trackIconTooltip.style.display = DisplayStyle.None;
    }

    private static void DrawButtonIcon(Button button, System.Action<Painter2D> draw)
    {
        button.Q(className: "track-icon-art").generateVisualContent += context =>
        {
            var painter = context.painter2D;
            painter.strokeColor = context.visualElement.resolvedStyle.color;
            painter.lineWidth = 1.6f;
            painter.lineJoin = LineJoin.Round;
            painter.BeginPath();
            draw(painter);
            painter.Stroke();
        };
    }

    private static void DrawLeaveSessionIcon(Painter2D painter)
    {
        painter.MoveTo(new Vector2(12, 7));
        painter.LineTo(new Vector2(12, 3));
        painter.LineTo(new Vector2(3, 3));
        painter.LineTo(new Vector2(3, 19));
        painter.LineTo(new Vector2(12, 19));
        painter.LineTo(new Vector2(12, 15));
        painter.MoveTo(new Vector2(8, 11));
        painter.LineTo(new Vector2(20, 11));
        painter.MoveTo(new Vector2(16, 7));
        painter.LineTo(new Vector2(20, 11));
        painter.LineTo(new Vector2(16, 15));
    }

    internal static void DrawSnapshotButtonIcon(Button button) => DrawButtonIcon(button, DrawSnapshotIcon);

    private static void DrawSnapshotIcon(Painter2D painter)
    {
        painter.MoveTo(new Vector2(2, 18));
        painter.LineTo(new Vector2(2, 7));
        painter.LineTo(new Vector2(6, 7));
        painter.LineTo(new Vector2(8, 4));
        painter.LineTo(new Vector2(14, 4));
        painter.LineTo(new Vector2(16, 7));
        painter.LineTo(new Vector2(20, 7));
        painter.LineTo(new Vector2(20, 18));
        painter.ClosePath();
        painter.MoveTo(new Vector2(14, 12));
        painter.Arc(new Vector2(11, 12), 3, Angle.Degrees(0), Angle.Degrees(360));
        painter.ClosePath();
    }

    private static void DrawResetCameraIcon(Painter2D painter)
    {
        painter.MoveTo(new Vector2(16, 6));
        painter.Arc(new Vector2(11, 11), 7, Angle.Degrees(315), Angle.Degrees(585));
        painter.MoveTo(new Vector2(6, 1));
        painter.LineTo(new Vector2(6, 6));
        painter.LineTo(new Vector2(11, 6));
    }

    private static void DrawPitSettingsIcon(Painter2D painter)
    {
        painter.MoveTo(new Vector2(13, 3));
        painter.LineTo(new Vector2(17, 2));
        painter.LineTo(new Vector2(15, 6));
        painter.LineTo(new Vector2(17, 8));
        painter.LineTo(new Vector2(21, 6));
        painter.LineTo(new Vector2(20, 10));
        painter.LineTo(new Vector2(17, 12));
        painter.LineTo(new Vector2(13, 12));
        painter.LineTo(new Vector2(6, 20));
        painter.LineTo(new Vector2(3, 20));
        painter.LineTo(new Vector2(2, 17));
        painter.LineTo(new Vector2(10, 9));
        painter.LineTo(new Vector2(10, 6));
        painter.ClosePath();
    }

    private static void DrawGarageIcon(Painter2D painter)
    {
        painter.MoveTo(new Vector2(2, 20));
        painter.LineTo(new Vector2(2, 8));
        painter.LineTo(new Vector2(11, 2));
        painter.LineTo(new Vector2(20, 8));
        painter.LineTo(new Vector2(20, 20));
        painter.ClosePath();
        painter.MoveTo(new Vector2(6, 20));
        painter.LineTo(new Vector2(6, 10));
        painter.LineTo(new Vector2(16, 10));
        painter.LineTo(new Vector2(16, 20));
        painter.MoveTo(new Vector2(6, 14));
        painter.LineTo(new Vector2(16, 14));
        painter.MoveTo(new Vector2(6, 17));
        painter.LineTo(new Vector2(16, 17));
    }

    private string CallToGarageTooltip => trackPreview.UsesNorthWilkesboroHaulerGarage
        ? "Call your car to the team hauler" : "Call your car to its pit stall";

    private string GarageArrivalTooltip => trackPreview.UsesNorthWilkesboroHaulerGarage
        ? "The garage will open when your car is parked behind the team hauler."
        : "The garage will open when your car is parked in its pit stall.";

    private void VisitGarageDuringSession()
    {
        if (activeRace == null || activeSession == null || !trackPreview.CanVisitGarage || trackPreview.SessionComplete)
            return;

        if (sessionInGarage || (garageVisitPending && !trackPreview.PlayerStoppedOnTrack))
            return;
        bool stoppedOnTrack = trackPreview.PlayerStoppedOnTrack;
        if (PitSettingsOpen)
            TogglePitSettings();
        if (!stoppedOnTrack && !trackPreview.PlayerParkedForGarage && sessionPaused)
            PlaySession();
        trackPreview.SetPlayerGarageVisit(true);
        garageVisitPending = true;
        visitGarageButton.tooltip = GarageArrivalTooltip;
        visitGarageButton.SetEnabled(false);
        if (trackPreview.PlayerParkedForGarage || stoppedOnTrack)
            OpenGarageAfterParking(stoppedOnTrack);
    }

    private void OpenGarageAfterParking(bool allowStoppedOnTrack = false)
    {
        if (!garageVisitPending ||
            !(trackPreview.PlayerParkedForGarage || allowStoppedOnTrack && trackPreview.PlayerStoppedOnTrack) ||
            trackPreview.SessionComplete)
            return;
        closeTrackMsaaPicker?.Invoke();
        garageVisitPending = false;
        ReleaseTrackPointer();
        sessionInGarage = true;
        SetPitSettingsOpen(false);
        trackPreview.SetSessionSpeed(1);
        garageCarParked = trackPreview.PlayerParkedForGarage;
        trackPreview.SetTrackRendering(false);
        CancelCameraMenuHold();
        HideTrackIconTooltip();
        trackScreen.style.display = DisplayStyle.None;
        raceEventView.Screen.style.display = DisplayStyle.Flex;
        raceEventView.ShowGarage();
        raceEventView.UpdateSessionClock(trackPreview.SessionRun.Elapsed);
    }

    private void ResumeSession()
    {
        trackPreview.CancelGarageLapSimulation();
        if (trackPreview.PlayerParkedForGarage)
            trackPreview.SessionRun.SetFuel(0, raceEventView.StartingFuelGallons);
        trackPreview.SetPlayerSetup(raceEventView.CurrentSetup);
        trackPreview.SetPlayerGarageVisit(false);
        sessionInGarage = false;
        ApplySessionPlayback();
        garageVisitPending = false;
        visitGarageButton.SetEnabled(trackPreview.CanVisitGarage);
        visitGarageButton.tooltip = CallToGarageTooltip;
        raceEventView.Screen.style.display = DisplayStyle.None;
        HideTrackIconTooltip();
        trackScreen.style.display = DisplayStyle.Flex;
        trackPreview.SetTrackRendering(true);
        nextTrackCameraCut = Time.unscaledTimeAsDouble + CameraCycleIntervalSeconds;
        fpsSampleStart = Time.unscaledTimeAsDouble;
        fpsSampleFrames = 0;
        trackFpsLabel.text = "FPS --";
        visitGarageButton.Focus();
    }

    private void ReturnToGarage() => CloseTrackSession();

    private void CloseTrackSession(bool showTestPracticeGarage = false, string testPracticeMessage = null)
    {
        closeTrackMsaaPicker?.Invoke();
        ReleaseTrackPointer();
        SetPitSettingsOpen(false);
        trackPreview.FinishPracticeSetups();
        trackPreview.FinishGarageRun("Session ended");
        trackPreview.gameObject.SetActive(false);
        if (activeSession?.kind == RaceSessionKind.Practice && activeCareer != null && !developmentPractice)
            careerSetup.SaveProgress(activeCareer);
        activeSession = null;
        sessionInGarage = false;
        garageVisitPending = false;
        visitGarageButton.SetEnabled(trackPreview.CanVisitGarage);
        visitGarageButton.tooltip = CallToGarageTooltip;
        UpdateTrackFlags(null);
        snapshotStatus.text = string.Empty;
        snapshotStatus.tooltip = null;
        snapshotStatus.style.display = DisplayStyle.None;
        playerPitStatus.text = string.Empty;
        CancelCameraMenuHold();
        HideTrackIconTooltip();
        trackScreen.style.display = DisplayStyle.None;
        if (developmentPractice)
        {
            raceEventView.SetRunningSession(null);
            if (showTestPracticeGarage)
            {
                raceEventView.Screen.style.display = DisplayStyle.Flex;
                raceEventView.ShowGarage();
                return;
            }

            raceEventView.Screen.style.display = DisplayStyle.None;
            activeRace = null;
            developmentPracticeCareer = null;
            developmentPractice = false;
            ShowDevelopmentTrackPicker(testPracticeMessage);
            return;
        }
        if (activeRace != null)
        {
            raceEventView.Screen.style.display = DisplayStyle.Flex;
            raceEventView.SetRunningSession(null);
            raceEventView.ShowEvent();
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
