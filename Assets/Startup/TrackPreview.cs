using System.Collections.Generic;
using UnityEngine;

public sealed class TrackPreview : MonoBehaviour
{
    private const float StartingViewSize = 25f;
    private const float CloseViewSize = 9f;
    private const int PreviewLayer = 31;
    private const int SamplesPerSection = 48;
    private const float BankTransitionFraction = 0.30f;

    private TrackDefinition definition;
    private TrackLayout customLayout;
    private Vector3 orbitOffset;
    private float wideViewSize;
    private float straightLength;
    private float turnRadius;
    private float turnLength;
    private float lapLength;
    private float roadHalfWidth;

    private readonly List<Material> materials = new List<Material>();
    private readonly List<Mesh> roadMeshes = new List<Mesh>();
    internal const int CarCount = 24;
    private readonly Transform[] cars = new Transform[CarCount];
    private Camera raceCamera;
    private Transform grassGround;
    private float groundHeight;
    private Material surfaceMaterial;
    private float elapsed;
    private TrackSnapshot snapshot;
    private double snapshotTime;
    internal bool CanSaveSnapshot => snapshot != null;

    internal string SaveSnapshot()
    {
        PositionCars();
        CaptureSnapshot(force: true, includePose: true);
        return snapshot.Save(simulation, simulationModel.Id, speedModel.Id, behaviorModel.Id);
    }

    private const float SessionStep = 1f / 60f;
    private float[] sessionDistances;
    private double pendingSessionTime;
    private int sessionSpeed = 1;
    private bool isQualifying;
    private ICarSimulationModel simulationModel;
    private System.Func<float, float> racingLine;
    private IVehicleBehaviorModel behaviorModel = VehicleBehaviorModels.Available[0];
    private LapSpeedProfile lapSpeedProfile;
    private RestrictorPlateDraft draft;
    private readonly float[] draftPitApproach = new float[CarCount];
    private IVehicleSpeedModel speedModel = VehicleSpeedModels.Available[0];
    private CarSimulationContext simulation;
    private readonly float[] baseSpeeds = new float[CarCount];
    private readonly System.Random simulationRandom = new System.Random();
    internal RaceSessionRun SessionRun { get; private set; }
    private RaceSessionRun previewTiming;
    internal RaceSessionRun TimingRun => SessionRun ?? previewTiming;
    private TrackPitTraffic pitTraffic;
    private TrackPitTraffic.Car[] trafficCars;
    private TrackPitSpline[] pitRoute;
    private float[] pitLengths;
    private float[] pitHeights;
    private float[] practiceStallDistances;
    private Vector3[] practiceStallPositions;
    private TrackState001Practice practice;
    private TrackState001Qualifying qualifying;
    private double? trackClosedSince;
    private double? pausedAt;
    private TrackState001RaceStart raceStart;
    private TrackState001Stages stages;
    private SessionPitStops pitStops;
    internal bool CanRequestCaution => stages?.CanRequestCaution == true;

    internal void RequestCaution()
    {
        stages?.RequestCaution(SessionRun, trafficCars);
    }

    internal bool CanRequestPlayerPit => SessionRun?.IsRace == true && pitStops?.CanRequest(SessionRun) == true;
    internal string PlayerPitStatus => SessionRun.Retired(0)
        ? (crashes[0].Active || SessionRun.Vehicle(0)?.Damage >= 100f ? "Retired · damage" : "Out of fuel")
        : pitStops?.PlayerStatus ?? string.Empty;

    internal void RequestPlayerPit(float fuelGallons, int tireMask, float repairPoints)
    {
        pitStops?.RequestPlayer(SessionRun, fuelGallons, tireMask, repairPoints);
    }
    private Renderer pitSignal;
    private Material pitOpenSignal;
    private Material pitClosedSignal;
    internal bool? PitRoadOpen => stages?.PitOpen;
    private Transform paceCar;
    private GameObject paceLights;
    private bool raceReleased;
    internal string LuckyDogMessage
    {
        get
        {
            if (TrackState == SessionTrackState.FinishUnderCaution)
                return "No Lucky Dog awarded";
            if (TrackState != SessionTrackState.Caution)
                return string.Empty;

            int index = stages.FreePassCarIndex;
            return index < 0 ? "No Lucky Dog awarded" : $"Lucky Dog · No. {simulation.Cars[index].carNumber}";
        }
    }

    internal bool OneToGoBeforeGreen => stages?.OneToGo == true;
    internal int RaceStartCountdown => raceStart?.SecondsUntilRelease ?? 0;
    internal SessionTrackState? TrackState => stages?.State ?? raceStart?.State ?? qualifying?.State ?? practice?.State;
    internal bool SessionComplete => stages != null ? RaceFieldParked() :
        raceStart != null || !TrackState.HasValue ? SessionRun.Complete : TrackState == SessionTrackState.Complete;
    internal bool HasSessionPitRoad => pitTraffic != null;
    private Vector3 cameraFocus;
    private float tiltAngle;
    private float yawAngle;
    private Transform followedCar;

    private static readonly (Color32 HoodAndTrunk, Color32 RoofAndSides)[] Liveries =
    {
        (new Color32(255, 145, 50, 255), new Color32(28, 35, 53, 255)),
        (new Color32(60, 164, 255, 255), new Color32(240, 244, 250, 255)),
        (new Color32(242, 221, 97, 255), new Color32(92, 42, 145, 255)),
        (new Color32(240, 83, 87, 255), new Color32(31, 34, 40, 255)),
        (new Color32(105, 221, 171, 255), new Color32(38, 91, 72, 255)),
        (new Color32(222, 226, 238, 255), new Color32(145, 47, 65, 255))
    };

    internal static (Color32 HoodAndTrunk, Color32 RoofAndSides) TeamLivery(int carNumber)
    {
        int livery = Mathf.Clamp(carNumber - 27, 0, CarCount - 1);
        int index = livery % Liveries.Length;
        return (Liveries[index].HoodAndTrunk, Liveries[(index + livery / Liveries.Length) % Liveries.Length].RoofAndSides);
    }

    public RenderTexture Image { get; private set; }

    public void Initialize(TrackDefinition track, int playerCarNumber) => Initialize(track, playerCarNumber, null);

    internal void Initialize(TrackDefinition track, int playerCarNumber, string[] sponsorNames, TeamCar playerCar = null)
    {
        surfaceMaterial = Resources.Load<Material>("Startup/TrackSurface");
        if (surfaceMaterial == null)
        {
            Debug.LogError("The Track surface material is missing.", this);
            enabled = false;
            return;
        }

        definition = track;
        simulationModel = CarSimulationModels.Default;
        simulation = new CarSimulationContext(track, null, null, System.Array.Empty<SessionCarResult>());
        lapLength = track.lengthMiles * TrackDefinition.MetersPerMile;
        float trackScale = lapLength / (60f + 38f * Mathf.PI);
        straightLength = track.OvalStraightLength;
        turnRadius = track.OvalTurnRadius;
        turnLength = Mathf.PI * turnRadius;
        roadHalfWidth = track.widthMeters * 0.5f;
        if (track.layoutPoints?.Length > 0)
            customLayout = new TrackLayout(track.layoutPoints, lapLength, track.widthMeters);
        orbitOffset = new Vector3(0f, 62f, -39f) * Mathf.Max(1f, trackScale);
        wideViewSize = Mathf.Max(StartingViewSize, StartingViewSize * trackScale);

        // Keep the preview outside the startup camera's view.
        transform.position = new Vector3(0f, -1000f, 0f);
        Image = new RenderTexture(960, 540, 24) { name = "Track View" };

        raceCamera = CreateObject("Track Camera", transform).AddComponent<Camera>();
        ApplyCameraPose();
        raceCamera.targetTexture = Image;
        raceCamera.cullingMask = 1 << PreviewLayer;
        raceCamera.clearFlags = CameraClearFlags.SolidColor;
        raceCamera.backgroundColor = new Color32(25, 43, 43, 255);
        raceCamera.orthographic = true;
        raceCamera.orthographicSize = StartingViewSize;
        raceCamera.farClipPlane = Mathf.Max(600f, orbitOffset.magnitude * 2f);

        var sun = CreateObject("Track Light", transform).AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.transform.localRotation = Quaternion.Euler(55f, -25f, 0f);
        sun.cullingMask = 1 << PreviewLayer;
        sun.intensity = 1.1f;

        var grass = MakeMaterial(new Color32(69, 108, 72, 255));
        var roadColor = track.surface == "Dirt" ? new Color32(145, 92, 58, 255)
            : track.surface == "Concrete" ? new Color32(139, 145, 148, 255)
            : new Color32(52, 57, 65, 255);
        var road = MakeMaterial(roadColor);
        var curb = MakeMaterial(new Color32(219, 221, 212, 255));
        var rubber = MakeMaterial(new Color32(24, 25, 30, 255));
        var glass = MakeMaterial(new Color32(43, 66, 87, 255));

        float groundSpan = Mathf.Max(2048f, raceCamera.farClipPlane * 3.5f);
        groundHeight = Mathf.Min(0f, customLayout?.MinimumElevation ?? 0f);
        grassGround = Box("Grass", transform, new Vector3(0f, groundHeight - 0.45f, 0f),
            new Vector3(groundSpan, 0.8f, groundSpan), grass).transform;
        ApplyCameraPose();
        Ring("Outer embankment", roadHalfWidth, roadHalfWidth + 3f, 0f, grass, outerEdgeAtGround: true);
        Ring("Inner embankment", -roadHalfWidth - 3f, -roadHalfWidth, 0f, grass, innerEdgeAtGround: true);
        Ring("Road surface", -roadHalfWidth, roadHalfWidth, 0f, road);
        Ring("Inner edge", -roadHalfWidth, -roadHalfWidth + 0.24f, 0.03f, curb);
        Ring("Outer edge", roadHalfWidth - 0.24f, roadHalfWidth, 0.03f, curb);
        if (customLayout != null && System.Array.Exists(track.layoutPoints, point => point.apronWidthMeters > 0f))
            Ring("Flat apron", 0f, 0f, 0f, road, apron: true);
        float finishDistance = track.StartFinishFraction * lapLength;
        Vector3 stripeCenter = TrackPoint(finishDistance, 0f, out var stripeForward, out var stripeUp);
        var stripe = Box("Start and finish", transform, stripeCenter + stripeUp * 0.025f,
            new Vector3(RoadWidths(finishDistance).x, 0.03f, 0.45f), curb);
        stripe.transform.localRotation = Quaternion.LookRotation(stripeForward, stripeUp);
        if (track.pitRoad?.enabled == true && customLayout != null)
            DrawPitRoad(track.pitRoad, road, curb);



        var hoodPaints = new Material[Liveries.Length];
        var bodyPaints = new Material[Liveries.Length];
        for (int i = 0; i < Liveries.Length; i++)
        {
            hoodPaints[i] = MakeMaterial(Liveries[i].HoodAndTrunk);
            bodyPaints[i] = MakeMaterial(Liveries[i].RoofAndSides);
        }

        int playerLivery = Mathf.Clamp(playerCarNumber - 27, 0, cars.Length - 1);
        for (int i = 0; i < cars.Length; i++)
        {
            cars[i] = CreateObject(i == 0 ? $"No. {playerCarNumber}" : $"Rival {i}", transform).transform;
            int livery = i == 0 ? playerLivery : i == playerLivery ? 0 : i;
            int paintIndex = livery % Liveries.Length;
            int paintGroup = livery / Liveries.Length;
            var hoodAndTrunkPaint = hoodPaints[paintIndex];
            var bodyPaint = bodyPaints[(paintIndex + paintGroup) % Liveries.Length];
            if (i == 0 && playerCar != null)
            {
                var paint = playerCar.Paint;
                hoodAndTrunkPaint = MakeMaterial(paint.HoodAndTrunk);
                bodyPaint = MakeMaterial(paint.RoofAndSides);
            }
            BuildCar(cars[i], i == 0 ? playerCarNumber : i < playerCarNumber ? i : i + 1,
                hoodAndTrunkPaint, bodyPaint, glass, rubber, i == 0 ? sponsorNames : null);
        }

        BuildRacingLine();
        SetBaseSpeeds();
        PositionCars();
        ResetView();
    }

    public void Zoom(float wheelDelta, Vector2 anchor)
    {
        float previousSize = raceCamera.orthographicSize;
        float size = Mathf.Clamp(previousSize * Mathf.Exp(wheelDelta * 0.055f),
            CloseViewSize, wideViewSize);
        float aspect = (float)Image.width / Image.height;
        var cameraTransform = raceCamera.transform;
        Vector3 zoomDirection = cameraTransform.right * (anchor.x * aspect) + cameraTransform.up * anchor.y;
        cameraFocus += zoomDirection * (previousSize - size);

        raceCamera.orthographicSize = size;
        ApplyCameraPose();
    }

    public void Pan(Vector2 imageDelta)
    {
        StopFollowing();
        var cameraTransform = raceCamera.transform;
        float worldHeight = raceCamera.orthographicSize * 2f;
        cameraFocus += (cameraTransform.up * imageDelta.y - cameraTransform.right * imageDelta.x) * worldHeight;
        ApplyCameraPose();
    }

    public void Rotate(float imageDeltaX)
    {
        yawAngle = Mathf.Repeat(yawAngle - imageDeltaX * 120f, 360f);
        ApplyCameraPose();
    }

    public void Tilt(float imageDeltaY)
    {
        tiltAngle = Mathf.Clamp(tiltAngle - imageDeltaY * 90f, -43f, 22f);
        ApplyCameraPose();
    }

    public void SelectCar(Vector2 viewportPoint)
    {
        Ray ray = raceCamera.ViewportPointToRay(viewportPoint);
        var body = new Bounds(new Vector3(0f, 0.53f, 0f), new Vector3(1.8f, 1.07f, 2.9f));
        Transform selection = null;
        float closest = raceCamera.farClipPlane;
        foreach (var car in cars)
        {
            if (!car.gameObject.activeSelf)
                continue;
            var carRay = new Ray(car.InverseTransformPoint(ray.origin),
                car.InverseTransformDirection(ray.direction));
            if (body.IntersectRay(carRay, out float distance) && distance < closest)
            {
                closest = distance;
                selection = car;
            }
        }

        if (selection != null)
        {
            FollowCar(selection);
            return;
        }

        StopFollowing();
        ApplyCameraPose();
    }

    internal void SelectTimingCar(SessionCarResult result)
    {
        int index = TimingRun?.IndexOfCar(result) ?? -1;
        if (index >= 0 && index < cars.Length && cars[index] != null && cars[index].gameObject.activeSelf)
            FollowCar(cars[index]);
    }

    private void FollowCar(Transform car)
    {
        followedCar = car;
        cameraFocus = Vector3.zero;
        ApplyCameraPose();
    }

    public void ResetView()
    {
        if (raceCamera == null)
            return;

        followedCar = cars[0];
        cameraFocus = Vector3.zero;
        tiltAngle = 0f;
        yawAngle = 0f;
        raceCamera.orthographicSize = StartingViewSize;
        ApplyCameraPose();
    }

    private void StopFollowing()
    {
        if (followedCar == null)
            return;

        cameraFocus += CarFocus();
        followedCar = null;
    }

    private Vector3 CarFocus()
    {
        return followedCar.localPosition + Vector3.up * 0.53f;
    }

    private void ApplyCameraPose()
    {
        Vector3 focus = cameraFocus + (followedCar != null ? CarFocus() : Vector3.zero);
        Quaternion orbit = Quaternion.Euler(tiltAngle, yawAngle, 0f);
        Vector3 offset = orbit * orbitOffset;

        var cameraTransform = raceCamera.transform;
        cameraTransform.localPosition = focus + offset;
        cameraTransform.localRotation = Quaternion.LookRotation(-offset);

        // Keep the grass perimeter outside the camera frustum as the focus moves.
        if (grassGround != null)
        {
            Vector3 cameraPosition = cameraTransform.localPosition;
            grassGround.localPosition = new Vector3(cameraPosition.x, grassGround.localPosition.y, cameraPosition.z);
        }
    }

    internal void BeginSession(CarSimulationContext simulation, ICarSimulationModel model, IVehicleSpeedModel speedModel,
        IVehicleBehaviorModel behaviorModel, int[] qualifyingOrder = null, bool usePaceTruck = false)
    {
        this.simulation = simulation;
        simulationModel = model;
        this.speedModel = speedModel;
        lapSpeedProfile = speedModel is VehicleSpeed002 ? new LapSpeedProfile(definition) : null;
        if (this.behaviorModel != behaviorModel)
        {
            this.behaviorModel = behaviorModel;
            BuildRacingLine();
        }
        var session = simulation.Session;
        drivingLanesReady = false;
        draft = definition.restrictorPlate && simulation.Session.kind == RaceSessionKind.Race
            ? new RestrictorPlateDraft(cars.Length) : null;
        snapshot = new TrackSnapshot();
        snapshotTime = 0;
        collisions = new VehicleCollision001(cars.Length);
        pitCollisions = new VehicleCollision001(cars.Length);
        System.Array.Clear(crashes, 0, crashes.Length);
        System.Array.Clear(stoppedCarTime, 0, stoppedCarTime.Length);
        System.Array.Clear(practiceLeftStall, 0, practiceLeftStall.Length);
        PrepareCollisionFlashes();
        SetBaseSpeeds();
        float startLine = Mathf.Repeat(definition.StartFinishFraction, 1f) * lapLength + lapLength;
        float spacing = Mathf.Min(4.5f, lapLength / (2f * cars.Length));
        sessionDistances = new float[cars.Length];
        var slots = RaceSession.GridSlots(simulation.Cars, session.kind == RaceSessionKind.Race ? qualifyingOrder : null);
        isQualifying = session.kind == RaceSessionKind.Qualifying;
        for (int i = 0; i < cars.Length; i++)
        {
            sessionDistances[i] = startLine - spacing * slots[i];
            if (pitTraffic != null && !isQualifying)
                trafficCars[i] = pitTraffic.CreateTrackCar(sessionDistances[i]);
            cars[i].gameObject.SetActive(!isQualifying || i == 0);
        }
        practice = null;
        qualifying = null;
        raceStart = null;
        stages = null;
        pitStops = null;
        raceReleased = false;
        if (paceCar != null)
            Destroy(paceCar.gameObject);
        trackClosedSince = null;
        pausedAt = null;
        if (model is TrackState001 trackStateModel && session.kind == RaceSessionKind.Practice && pitTraffic != null)
        {
            practice = trackStateModel.StartPractice(pitTraffic, trafficCars, practiceStallDistances);
            for (int i = 0; i < cars.Length; i++)
            {
                sessionDistances[i] = pitTraffic.RaceDistance(trafficCars[i]);
                if (pitRoadOutside || definition.restrictorPlate)
                {
                    var entry = simulation.Cars[i];
                    entry.Fuel = SessionFuel.TankCapacity;
                    if (entry.car != null)
                        entry.car.Damage = 0f;
                }
            }
        }

        if (model is TrackState001 qualifyingModel && isQualifying && pitTraffic != null)
            qualifying = qualifyingModel.StartQualifying(pitTraffic, practiceStallDistances[0]);

        if (model is TrackState001 raceModel && session.kind == RaceSessionKind.Race && pitTraffic != null)
        {
            raceStart = raceModel.StartRace(pitTraffic, slots, definition, simulationRandom);
            CreatePaceCar(usePaceTruck);
            CreatePitSignal();
        }

        pendingSessionTime = 0;
        sessionSpeed = 1;
        SessionRun = new RaceSessionRun(session, lapLength, startLine, simulation, speedModel, simulationRandom, initialDistances: sessionDistances);
        if (pitTraffic != null && (session.kind == RaceSessionKind.Race || practice != null))
        {
            ConfigurePitField();
            pitStops = new SessionPitStops(pitTraffic, trafficCars, practiceStallDistances);
        }
        if (isQualifying)
            SessionRun.SimulateQualifyingRivals(CarSpeed);
        PositionCars();
        ResetView();
    }

    internal void BeginPreviewTiming(SessionCarResult[] entries)
    {
        var distances = new float[cars.Length];
        for (int i = 0; i < cars.Length; i++)
            distances[i] = lapLength * i / cars.Length;
        float startLine = Mathf.Repeat(definition.StartFinishFraction, 1f) * lapLength;
        var session = new RaceSession { kind = RaceSessionKind.Practice };
        simulation = new CarSimulationContext(definition, null, session, entries);
        SetBaseSpeeds();
        previewTiming = new RaceSessionRun(session, lapLength, startLine, simulation, speedModel, simulationRandom, distances);
    }

    private static readonly System.Collections.Generic.IReadOnlyList<string> RivalTeams = System.Array.AsReadOnly(new[] { "Redline Racing", "Summit Motorsports", "Pioneer Racing", "Blue Ridge Autosport",
            "Velocity Racing", "Ironwood Motorsports", "Cedar Racing", "Crosswind Racing" });

    internal static SessionCarResult[] CreateTimingEntries(TeamCar playerCar, Career career)
    {
        var entries = new SessionCarResult[CarCount];
        string[] rivalDrivers =
        {
            "Alex Morgan", "Blake Turner", "Cameron Hayes", "Casey Brooks", "Drew Bennett", "Elliot Reed",
            "Emerson Grant", "Frankie Brooks", "Harper Lewis", "Jamie Cole", "Jordan Blake", "Kai Foster",
            "Lane Mitchell", "Logan Shaw", "Morgan Ellis", "Parker Nash", "Quinn Carter", "Reese Walker",
            "Robin Scott", "Rowan Miller", "Sage Parker", "Taylor West", "Toby Rivers"
        };
        for (int i = 0; i < entries.Length; i++)
        {
            entries[i] = new SessionCarResult
            {
                car = i == 0 ? playerCar : career.rivalCars.Find(item => item.number == i),
                carNumber = i == 0 ? playerCar.number : i < playerCar.number ? i : i + 1,
                driverName = i == 0 ? career.Driver?.FullName ?? career.playerName : rivalDrivers[i - 1],
                teamName = i == 0 ? career.teamName : RivalTeams[(i - 1) / 3]
            };
        }
        return entries;
    }

    internal void SetSessionSpeed(int speed)
    {
        if (speed == 0)
        {
            pausedAt ??= Time.unscaledTimeAsDouble;
        }
        else if (pausedAt.HasValue)
        {
            if (trackClosedSince.HasValue)
                trackClosedSince += Time.unscaledTimeAsDouble - pausedAt.Value;
            pausedAt = null;
        }

        sessionSpeed = speed;
    }

    private void Update()
    {
        UpdateCollisionFlashes();
        if (sessionSpeed == 0)
            return;

        if (SessionRun == null)
        {
            AdvanceSimulation(Time.deltaTime);
        }
        else if (TrackState.HasValue)
        {
            UpdateTrackStateSession();
        }
        else
        {
            if (SessionRun.Complete)
                return;

            pendingSessionTime += (double)Time.deltaTime * sessionSpeed;
            // Use the same simulation steps at every speed so lap timing and pit transitions stay consistent.
            while (pendingSessionTime >= SessionStep && !SessionRun.Complete)
            {
                AdvanceSimulation(SessionRun.StepDuration(SessionStep));
                pendingSessionTime -= SessionStep;
                snapshotTime += SessionStep;
                CaptureSnapshot();
            }
        }

        PositionCars();
        if (followedCar != null)
            ApplyCameraPose();
    }

    private void UpdateTrackStateSession()
    {
        if (TrackState == SessionTrackState.TrackClosed)
        {
            double now = Time.unscaledTimeAsDouble;
            trackClosedSince ??= now;
            double visibleSeconds = now - trackClosedSince.Value;
            practice?.UpdateOpening(visibleSeconds);
            qualifying?.UpdateOpening(visibleSeconds);
            raceStart?.UpdateOpening(visibleSeconds);
            return;
        }

        pendingSessionTime += (double)Time.deltaTime * sessionSpeed;
        while (pendingSessionTime >= SessionStep && !SessionComplete)
        {
            if (raceStart != null)
                AdvanceRaceStartStep();
            else if (qualifying != null)
                qualifying.Advance(SessionRun, SessionStep, SpeedAt(0, qualifying.Car.TrackDistance),
                    qualifying.Car.InPit ? 0f : lapSpeedProfile?.BrakingLimitAt(qualifying.Car.TrackDistance) ?? 0f);
            else
                AdvancePracticeStep();
            pendingSessionTime -= SessionStep;
            snapshotTime += SessionStep;
            CaptureSnapshot();
        }
    }

    private void AdvanceRaceStartStep()
    {
        float beforePacing = raceStart.FormationProgress;
        raceStart.Advance(SessionStep);
        float pacedMeters = raceStart.FormationProgress - beforePacing;
        if (pacedMeters > 0f)
        {
            for (int i = 0; i < cars.Length; i++)
                SessionRun.RecordTravel(i, pacedMeters, pacedMeters / SessionStep, reducedBurn: true);
        }
        if (raceStart.State != SessionTrackState.TrackOpen)
            return;

        if (raceReleased)
        {
            stages.BeforeStep(trafficCars, SessionStep);
            AdvanceSimulation(SessionStep);
            stages.AfterStep(SessionRun, trafficCars);
            pitSignal.sharedMaterial = stages.PitOpen ? pitOpenSignal : pitClosedSignal;
            return;
        }

        raceReleased = true;
        stages = ((TrackState001)simulationModel).StartStages(pitTraffic, cars.Length, definition, simulationRandom);
        for (int i = 0; i < cars.Length; i++)
        {
            sessionDistances[i] = raceStart.GridPosition(i, 0f).Distance;
            trafficCars[i] = pitTraffic.CreateTrackCar(sessionDistances[i]);
        }

        ConfigurePitField();
        SessionRun = new RaceSessionRun(simulation.Session, lapLength, raceStart.TimingStartLine,
            simulation, speedModel, simulationRandom, initialDistances: sessionDistances, finishOnCheckered: true);
    }

    private bool RaceFieldParked()
    {
        if (!SessionRun.Complete)
            return false;

        for (int i = 0; i < trafficCars.Length; i++)
        {
            if (!SessionRun.Retired(i) && (!trafficCars[i].InPit || trafficCars[i].PitDistance != practiceStallDistances[i]))
                return false;
        }
        return true;
    }

    private void CreatePitSignal()
    {
        pitOpenSignal = MakeMaterial(new Color32(45, 230, 70, 255));
        pitClosedSignal = MakeMaterial(new Color32(255, 40, 35, 255));
        var pole = CreateObject("Pit entry signal", transform).transform;
        PositionPitCar(pole, 0f, definition.pitRoad.widthMeters * 0.5f + 0.5f);
        var housing = MakeMaterial(new Color32(24, 25, 30, 255));
        Box("Signal post", pole, new Vector3(0f, 0.9f, 0f), new Vector3(0.16f, 1.8f, 0.16f), housing);
        pitSignal = Box("Pit road light", pole, new Vector3(0f, 1.9f, 0f),
            new Vector3(0.65f, 0.50f, 0.40f), pitOpenSignal).GetComponent<Renderer>();
    }

    private void CreatePaceCar(bool usePaceTruck)
    {
        paceCar = CreateObject("Pace Car", transform).transform;
        if (usePaceTruck && TryCreatePaceTruck())
            return;
        var body = MakeMaterial(new Color32(237, 239, 244, 255));
        var accent = MakeMaterial(new Color32(22, 69, 132, 255));
        var rubber = MakeMaterial(new Color32(24, 25, 30, 255));
        var glass = MakeMaterial(new Color32(43, 66, 87, 255));
        if (CreateImpala(paceCar, accent, body, glass, rubber) == null)
            BuildCar(paceCar, 0, accent, body, glass, rubber);
        Box("Light bar", paceCar, new Vector3(0f, 1.10f, -0.12f), new Vector3(0.95f, 0.10f, 0.22f), rubber);
        var amber = MakeMaterial(new Color32(255, 208, 22, 255));
        amber.EnableKeyword("_EMISSION");
        amber.SetColor("_EmissionColor", new Color(2f, 1.4f, 0.04f));
        paceLights = Box("Flashing yellow lights", paceCar, new Vector3(0f, 1.18f, -0.12f),
            new Vector3(0.95f, 0.12f, 0.22f), amber);
    }

    private bool TryCreatePaceTruck()
    {
        var asset = Resources.Load<GameObject>("Startup/FordF350PaceTruck");
        if (asset == null)
            return false;

        var truck = Instantiate(asset, paceCar, false);
        // Use the in-game Impala wheelbase as the scale reference.
        const float raceCarWheelbase = 1.5517f;
        const float truckWheelbase = 172f * 0.0254f;
        truck.transform.localScale *= 1.3f * raceCarWheelbase / truckWheelbase;
        var lights = truck.transform.Find("CautionLights");
        if (lights == null)
        {
            Destroy(truck);
            return false;
        }

        var paints = new Dictionary<string, Material>();
        foreach (var renderer in truck.GetComponentsInChildren<MeshRenderer>())
        {
            renderer.gameObject.layer = PreviewLayer;
            var shared = renderer.sharedMaterials;
            for (int i = 0; i < shared.Length; i++)
            {
                string name = shared[i] == null ? string.Empty : shared[i].name;
                if (!paints.TryGetValue(name, out var material))
                {
                    Color color = name switch
                    {
                        "LowerTrim" => new Color32(105, 94, 80, 255),
                        "Glass" => new Color32(34, 60, 75, 255),
                        "Rubber" or "BedLiner" => new Color32(30, 32, 35, 255),
                        "Chrome" or "Headlight" => new Color32(207, 216, 224, 255),
                        "TailLight" => new Color32(194, 22, 30, 255),
                        "AmberMarker" or "CautionLights" => new Color32(255, 200, 27, 255),
                        "Badge" => new Color32(17, 60, 122, 255),
                        _ => new Color32(211, 218, 225, 255)
                    };
                    material = MakeMaterial(color);
                    if (name == "CautionLights")
                    {
                        material.EnableKeyword("_EMISSION");
                        material.SetColor("_EmissionColor", new Color(2f, 1.4f, 0.04f));
                    }
                    paints.Add(name, material);
                }
                shared[i] = material;
            }
            renderer.sharedMaterials = shared;
        }
        paceLights = lights.gameObject;
        return true;
    }

    private void AdvancePracticeStep()
    {
        bool yellow = practice.State == SessionTrackState.Caution;
        bool trackOpen = practice.State == SessionTrackState.TrackOpen || yellow;
        float seconds = trackOpen ? SessionRun.StepDuration(SessionStep) : SessionStep;
        for (int i = 0; i < cars.Length; i++)
        {
            stepSpeeds[i] = SessionRun.Retired(i) ? 0f : crashes[i].Active ? crashes[i].ForwardSpeed :
                SessionRun.ConditionLimitedSpeed(i, yellow ? TrackState001Stages.PaceSpeed : SpeedAt(i, trafficCars[i].TrackDistance),
                    seconds, yellow ? LapSpeedProfile.MaximumBraking(definition) : trackOpen ? TrackBrakingLimit(i) : 0f);
            stepSpeeds[i] = SessionRun.FlatTireSpeed(stepSpeeds[i], SessionRun.FlatTireMask(i));
        }
        AvoidTrackTraffic(seconds);
        for (int i = 0; i < cars.Length; i++)
        {
            var car = trafficCars[i];
            float before = pitTraffic.RaceDistance(car);
            if (crashes[i].Active)
            {
                AdvanceCrash(i, seconds);
                continue;
            }
            if (SessionRun.Retired(i))
                continue;
            double beforeMovement = car.Odometer;
            bool reducedBurn = car.InPit || yellow || !trackOpen;
            float speed = stepSpeeds[i];
            car.Retired = SessionRun.Retired(i);
            bool serviceBeforeRelease = (pitRoadOutside || definition.restrictorPlate) && !practiceLeftStall[i] && car.InPit &&
                car.PitDistance == practiceStallDistances[i] && SessionRun.FlatTireMask(i) != 0;
            if (trackOpen && !car.Retired && (serviceBeforeRelease || pitStops.NeedsFlatTireService(i) ||
                (!car.InPit && SessionRun.FlatTireMask(i) != 0)))
            {
                pitStops.Advance(i, SessionRun, seconds, speed, pitOpen: true);
            }
            else
            {
                int flatTires = SessionRun.FlatTireMask(i);
                practice.Advance(i, seconds, SessionRun.FlatTireSpeed(speed, flatTires));
                SessionRun.RecordFlatTireDamage(i, flatTires, car.Odometer - beforeMovement);
                if (flatTires == 0)
                    car.FlatTireLaneBlend = Mathf.MoveTowards(car.FlatTireLaneBlend, 0f, seconds);
            }
            practiceLeftStall[i] |= !car.InPit || car.PitDistance != practiceStallDistances[i];
            SessionRun.RecordTravel(i, car.Odometer - beforeMovement, (car.Odometer - beforeMovement) / seconds, reducedBurn);
            if (trackOpen)
                SessionRun.Record(i, before, pitTraffic.RaceDistance(car), seconds);
        }

        ResolveTrackCollisions(seconds);
        practice.AdvanceCaution(seconds);
        HandleStoppedCars(seconds);
        if (trackOpen)
        {
            SessionRun.AdvanceClock(seconds);
            if (SessionRun.Complete)
                practice.CloseTrack();
        }
        else
        {
            practice.CheckParking();
        }
    }

    private readonly float[] drivingDistances = new float[CarCount];
    private readonly float[] drivingWidths = new float[CarCount];
    private readonly bool[] onTrack = new bool[CarCount];
    private readonly float[] drivingLanes = new float[CarCount];
    private readonly float[] targetLanes = new float[CarCount];
    private bool drivingLanesReady;
    private VehicleCollision001 collisions;
    private VehicleCollision001 pitCollisions;
    private readonly bool[] pitParticipants = new bool[CarCount];
    private readonly float[] pitStart = new float[CarCount];
    private readonly float[] pitEnd = new float[CarCount];
    private readonly float[] pitLaneStart = new float[CarCount];
    private readonly float[] pitLaneEnd = new float[CarCount];
    private float[] stallLanes;
    private bool pitRoadOutside;
    private readonly bool[] practiceLeftStall = new bool[CarCount];
    private readonly CrashMotion[] crashes = new CrashMotion[CarCount];

    private struct CrashMotion
    {
        public bool Active;
        public float ForwardSpeed;
        public float SideSpeed;
        public float Lane;
        public float Yaw;
        public float Spin;
    }
    private const float CollisionFlashSeconds = 0.4f;
    private readonly Transform[] collisionFlashes = new Transform[CarCount];
    private readonly float[] collisionFlashRemaining = new float[CarCount];
    private readonly bool[] collisionParticipants = new bool[CarCount];
    private readonly float[] collisionEndDistances = new float[CarCount];
    private readonly float[] collisionStartLanes = new float[CarCount];

    private readonly float[] currentTrafficSpeeds = new float[CarCount];
    private readonly float[] stoppedCarTime = new float[CarCount];
    private readonly int[] practiceCarAhead = new int[CarCount];
    private readonly float[] practiceGapAhead = new float[CarCount];
    private readonly float[] stepSpeeds = new float[CarCount];

    private void AdvanceSimulation(float seconds)
    {
        bool pitMembershipChanged = false;
        float previousElapsed = elapsed;
        elapsed += seconds;
        for (int i = 0; i < cars.Length; i++)
        {
            if (SessionRun?.Finished(i) == true)
            {
                stepSpeeds[i] = 0f;
                continue;
            }

            if (crashes[i].Active)
            {
                stepSpeeds[i] = crashes[i].ForwardSpeed;
                continue;
            }

            float speed = SessionRun == null ? CarSpeed(i) : SpeedAt(i, trafficCars?[i]?.TrackDistance ?? sessionDistances[i]);
            if (DraftingActive)
                speed += draft.SpeedBoost(i);
            if (stages != null)
            {
                speed = raceStart.RacingSpeed(speed, SessionRun.Elapsed + seconds);
                speed = stages.CarSpeed(i, trafficCars, speed, seconds);
            }
            if (SessionRun != null)
                speed = SessionRun.ConditionLimitedSpeed(i, speed, seconds, TrackBrakingLimit(i));
            if (pitStops != null)
                speed = SessionRun.FlatTireSpeed(speed, SessionRun.FlatTireMask(i));

            stepSpeeds[i] = speed;
        }
        stages?.PreserveCautionOrder(trafficCars, stepSpeeds, seconds);
        bool checkTraffic = SessionRun != null && !isQualifying;
        if (checkTraffic)
            AvoidTrackTraffic(seconds);

        for (int i = 0; i < cars.Length; i++)
        {
            if (SessionRun?.Finished(i) == true)
            {
                if (stages != null && !SessionRun.Retired(i))
                {
                    var finishedCar = trafficCars[i];
                    double beforeParking = finishedCar.Odometer;
                    stages.AdvanceFinishedCar(finishedCar, seconds, practiceStallDistances[i]);
                    SessionRun.RecordTravel(i, finishedCar.Odometer - beforeParking,
                        (finishedCar.Odometer - beforeParking) / seconds, reducedBurn: true);
                }
                continue;
            }

            if (crashes[i].Active)
            {
                AdvanceCrash(i, seconds);
                continue;
            }

            float speed = stepSpeeds[i];
            if (pitTraffic != null && !isQualifying)
            {
                var car = trafficCars[i];
                float before = pitTraffic.RaceDistance(car);
                double beforeMovement = car.Odometer;
                bool wasInPit = car.InPit;
                bool reducedBurn = wasInPit || stages?.ReducedFuelBurn == true;
                car.Retired = SessionRun?.Retired(i) == true;
                if (pitStops != null && !car.Retired)
                    pitStops.Advance(i, SessionRun, seconds, speed, stages?.CanEnterPits(i, car) ?? true,
                        allowEmergencyEntry: stages?.IsCaution != true);
                else
                    pitTraffic.Advance(car, seconds, speed);
                pitMembershipChanged |= wasInPit != car.InPit;
                TimingRun?.RecordTravel(i, car.Odometer - beforeMovement, (car.Odometer - beforeMovement) / seconds, reducedBurn);
                TimingRun?.Record(i, before, pitTraffic.RaceDistance(car), seconds);
            }
            else if (SessionRun != null)
            {
                float before = sessionDistances[i];
                sessionDistances[i] += speed * seconds;
                SessionRun.RecordTravel(i, sessionDistances[i] - before, speed);
                SessionRun.Record(i, before, sessionDistances[i], seconds);
            }
        }
        if (previewTiming != null && pitTraffic == null)
        {
            for (int i = 0; i < cars.Length; i++)
            {
                float offset = lapLength * i / cars.Length;
                float speed = CarSpeed(i);
                previewTiming.RecordTravel(i, seconds * speed, speed);
                previewTiming.Record(i, offset + previousElapsed * speed, offset + elapsed * speed, seconds);
            }
        }
        if (checkTraffic)
            ResolveTrackCollisions(seconds);
        TimingRun?.AdvanceClock(seconds);
        if (stages != null)
            HandleStoppedCars(seconds);
        if (pitMembershipChanged && stages?.ReducedFuelBurn == true && stages.ControlsField)
            stages.UpdatePitFormation(SessionRun, trafficCars);
    }

    private void HandleStoppedCars(float seconds)
    {
        int incident = -1;
        for (int i = 0; i < cars.Length; i++)
        {
            var car = trafficCars[i];
            if (!SessionRun.Retired(i) || car.InPit)
                continue;

            car.Retired = true;
            if (stoppedCarTime[i] == 0f && incident < 0)
                incident = i;
            stoppedCarTime[i] += seconds;
            if (stoppedCarTime[i] >= 10f && cars[i].gameObject.activeSelf)
                cars[i].gameObject.SetActive(false);
        }

        if (incident >= 0)
        {
            practice?.RequestCaution();
            stages?.RequestCaution(SessionRun, trafficCars, incident);
        }
    }

    private bool DraftingActive => draft != null && stages?.ReducedFuelBurn != true &&
        TrackState != SessionTrackState.Checkered;

    private void AvoidTrackTraffic(float seconds)
    {
        float inside = customLayout?.InsideSign ?? -1f;
        for (int i = 0; i < cars.Length; i++)
        {
            var traffic = trafficCars?[i];
            onTrack[i] = cars[i].gameObject.activeSelf && traffic?.InPit != true && (traffic?.FlatTireLaneBlend ?? 0f) == 0f;
            float distance = traffic?.TrackDistance ?? sessionDistances[i];
            drivingDistances[i] = distance;
            draftPitApproach[i] = traffic == null ? float.PositiveInfinity : traffic.NextEntry - distance;
            currentTrafficSpeeds[i] = SessionRun.CurrentSpeed(i);
            drivingWidths[i] = RoadWidths(distance).x;
            targetLanes[i] = TargetDrivingLane(i, distance, traffic);
            if (!drivingLanesReady || !onTrack[i])
                drivingLanes[i] = targetLanes[i];
            if (crashes[i].Active)
                drivingLanes[i] = crashes[i].Lane;
            collisionStartLanes[i] = drivingLanes[i];
            collisionParticipants[i] = onTrack[i];
            pitParticipants[i] = cars[i].gameObject.activeSelf && traffic?.InPit == true;
            pitStart[i] = traffic?.PitDistance ?? 0f;
            pitLaneStart[i] = traffic?.PitLane ?? 0f;
        }

        bool practiceYellow = practice?.State == SessionTrackState.Caution;
        if (practiceYellow)
            TrackState001Stages.PreserveOrder(lapLength, stepSpeeds, seconds, practiceCarAhead, practiceGapAhead,
                PracticeCarDistance, HoldsPracticePosition);

        if (DraftingActive)
            draft.Update(lapLength, seconds, SessionRun, onTrack, drivingDistances, drivingWidths, drivingLanes,
                targetLanes, currentTrafficSpeeds, draftPitApproach);

        behaviorModel.AvoidTraffic(lapLength, inside, seconds, !practiceYellow && stages?.ReducedFuelBurn != true,
            drivingDistances, drivingWidths, onTrack, drivingLanes, targetLanes, currentTrafficSpeeds, stepSpeeds,
            LapSpeedProfile.MaximumBraking(definition), DraftingActive ? draft.InLine : null);
        drivingLanesReady = true;
    }

    private float PracticeCarDistance(int index) => drivingDistances[index];

    private bool HoldsPracticePosition(int index) =>
        onTrack[index] && stepSpeeds[index] > 0f && !crashes[index].Active;

    private void PrepareCollisionFlashes()
    {
        if (collisionFlashes[0] == null)
        {
            var light = MakeMaterial(new Color32(255, 218, 95, 255));
            light.EnableKeyword("_EMISSION");
            light.SetColor("_EmissionColor", new Color(3f, 1.8f, 0.2f));
            for (int i = 0; i < cars.Length; i++)
            {
                var flash = CreateObject("Collision flash", cars[i]).transform;
                flash.localPosition = Vector3.up * 1.9f;
                Box("Flash core", flash, Vector3.zero, Vector3.one * 0.25f, light);
                for (int ray = 0; ray < 4; ray++)
                {
                    var streak = Box("Flash ray", flash, Vector3.zero, new Vector3(0.08f, 0.08f, 1.35f), light);
                    streak.transform.localRotation = Quaternion.Euler(0f, 45f * ray, 0f);
                }
                collisionFlashes[i] = flash;
            }
        }
        System.Array.Clear(collisionFlashRemaining, 0, collisionFlashRemaining.Length);
        foreach (var flash in collisionFlashes)
            flash.gameObject.SetActive(false);
    }

    private void UpdateCollisionFlashes()
    {
        if (collisionFlashes[0] == null)
            return;

        for (int i = 0; i < collisionFlashes.Length; i++)
        {
            float remaining = collisionFlashRemaining[i];
            var flash = collisionFlashes[i];
            bool visible = remaining > 0f;
            if (flash.gameObject.activeSelf != visible)
                flash.gameObject.SetActive(visible);
            if (!visible)
                continue;

            flash.localScale = Vector3.one * Mathf.Lerp(0.4f, 1.25f, remaining / CollisionFlashSeconds);
            if (sessionSpeed != 0)
                collisionFlashRemaining[i] = Mathf.Max(0f, remaining - Time.unscaledDeltaTime);
        }
    }

    private void AdvanceCrash(int index, float seconds)
    {
        if (SessionRun.Retired(index))
            return;

        ref var crash = ref crashes[index];
        var traffic = trafficCars?[index];
        float before = traffic?.TrackDistance ?? sessionDistances[index];
        float oldLane = crash.Lane;
        float forward = crash.ForwardSpeed * seconds;
        float after = before + forward;
        float edge = RoadWidths(after).x * 0.5f + 2f;
        crash.Lane = Mathf.Clamp(oldLane + crash.SideSpeed * seconds, -edge, edge);
        crash.Yaw += crash.Spin * seconds;
        crash.ForwardSpeed = Mathf.MoveTowards(crash.ForwardSpeed, 0f, 7f * seconds);
        crash.SideSpeed = Mathf.MoveTowards(crash.SideSpeed, 0f, 6f * seconds);
        crash.Spin = Mathf.MoveTowards(crash.Spin, 0f, 44f * seconds);
        drivingLanes[index] = crash.Lane;
        float traveled = Vector2.Distance(new Vector2(before, oldLane), new Vector2(after, crash.Lane));
        if (traffic != null)
        {
            traffic.TrackDistance = after;
            traffic.Odometer += traveled;
        }
        else
        {
            sessionDistances[index] = after;
        }
        SessionRun.RecordTravel(index, traveled, traveled / seconds, reducedBurn: true);
        SessionRun.Record(index, before, after, seconds);
        if (crash.ForwardSpeed == 0f && crash.SideSpeed == 0f)
        {
            SessionRun.Retire(index);
            if (traffic != null)
                traffic.Retired = true;
        }
    }

    private void ResolveTrackCollisions(float seconds)
    {
        for (int i = 0; i < cars.Length; i++)
        {
            var traffic = trafficCars?[i];
            collisionParticipants[i] &= traffic?.InPit != true && (traffic?.FlatTireLaneBlend ?? 0f) == 0f;
            collisionEndDistances[i] = traffic?.TrackDistance ?? sessionDistances[i];
            pitParticipants[i] &= traffic?.InPit == true;
            pitEnd[i] = traffic?.PitDistance ?? 0f;
            pitLaneEnd[i] = traffic?.PitLane ?? 0f;
        }

        pitCollisions.Resolve(SessionRun, lapLength, seconds, pitParticipants, pitStart, pitEnd, pitLaneStart, pitLaneEnd, wrap: false);
        collisions.Resolve(SessionRun, lapLength, seconds, collisionParticipants, drivingDistances,
            collisionEndDistances, collisionStartLanes, drivingLanes);
        for (int i = 0; i < cars.Length; i++)
        {
            if (collisions.HadImpact(i) || pitCollisions.HadImpact(i))
                collisionFlashRemaining[i] = CollisionFlashSeconds;

            float impulse = collisions.CrashImpulse(i);
            if (impulse != 0f && !SessionRun.Finished(i))
            {
                ref var crash = ref crashes[i];
                if (!crash.Active)
                {
                    crash.Active = true;
                    crash.Lane = drivingLanes[i];
                    crash.ForwardSpeed = SessionRun.CurrentSpeed(i) * 0.8f;
                }
                crash.SideSpeed = Mathf.Clamp(crash.SideSpeed + impulse, -16f, 16f);
                crash.Spin = Mathf.Sign(impulse) * Mathf.Lerp(65f, 165f, Mathf.Abs(impulse) / 14f);
            }
        }
    }

    private void CaptureSnapshot(bool force = false, bool includePose = false)
    {
        var frame = snapshot?.BeginFrame(snapshotTime, cars.Length, force);
        if (frame == null)
            return;

        frame.sessionSeconds = SessionRun.Elapsed;
        frame.playbackSpeed = sessionSpeed;
        frame.flag = TrackState?.ToString() ?? "Unspecified";
        frame.pitsOpen = PitRoadOpen ?? true;
        for (int i = 0; i < cars.Length; i++)
        {
            var traffic = isQualifying ? (i == 0 ? qualifying?.Car : null) : trafficCars?[i];
            bool inPit = traffic?.InPit == true;
            float distance = traffic?.TrackDistance ?? sessionDistances[i];
            float lane = inPit ? traffic.PitLane : drivingLanesReady ? drivingLanes[i] : TargetDrivingLane(i, distance, traffic);
            float nextEntry = traffic?.NextEntry ?? float.PositiveInfinity;
            float speed = SessionRun.CurrentSpeed(i);
            if (raceStart != null && !raceReleased)
            {
                var grid = raceStart.GridPosition(i, Mathf.Min(1.35f, definition.pitRoad.widthMeters * 0.25f));
                inPit = grid.OnPitRoad;
                distance = grid.Distance;
                lane = grid.Lane;
            }
            frame.cars[i] = new TrackSnapshot.CarSample
            {
                number = simulation.Cars[i].carNumber,
                visible = cars[i].gameObject.activeSelf,
                inPit = inPit,
                retired = SessionRun.Retired(i),
                spinning = crashes[i].Active && !SessionRun.Retired(i),
                servicing = pitStops?.IsServicing(i) == true,
                pitRequested = pitStops?.IsRequested(i) == true,
                collisionFlash = collisionFlashRemaining[i] > 0f,
                trackMeters = inPit && raceStart != null && !raceReleased ? 0f : distance,
                pitMeters = inPit ? (raceStart != null && !raceReleased ? distance : traffic.PitDistance) : 0f,
                laneMeters = lane,
                mph = float.IsNaN(speed) ? -1f : speed * 3600f / TrackDefinition.MetersPerMile,
                damage = SessionRun.Vehicle(i)?.Damage ?? 0f,
                flatTireMask = SessionRun.FlatTireMask(i),
                fuelGallons = SessionRun.Fuel(i),
                nextPitEntryMeters = float.IsInfinity(nextEntry) ? -1f : nextEntry,
                position = includePose ? cars[i].localPosition : Vector3.zero
            };
        }
    }

    private void SetBaseSpeeds()
    {
        for (int i = 0; i < baseSpeeds.Length; i++)
            baseSpeeds[i] = speedModel.BaseSpeed(simulation, i, simulationRandom);
    }

    private float TrackBrakingLimit(int index)
    {
        if (lapSpeedProfile == null || stages?.ReducedFuelBurn == true || trafficCars?[index]?.InPit == true)
            return 0f;

        return lapSpeedProfile.BrakingLimitAt(trafficCars?[index]?.TrackDistance ?? sessionDistances[index]);
    }

    private float SpeedAt(int index, float distance)
    {
        float baseSpeed = CarSpeed(index) * SessionRun.SpeedFactor(index);
        return lapSpeedProfile?.SpeedAt(distance, baseSpeed) ?? baseSpeed;
    }

    private float CarSpeed(int index) => baseSpeeds[index];

    private void PositionCars()
    {
        if (raceStart != null)
        {
            var pace = stages?.HasPaceCar == true ? stages.PacePosition() : raceStart.PacePosition();
            if (pace.OnPitRoad)
                PositionPitCar(paceCar, pace.Distance, pace.ParkingBlend * (definition.pitRoad.widthMeters * 0.5f + 1.5f));
            else
                PositionTrackCar(paceCar, pace.Distance, 0f);
            paceLights.SetActive(stages?.HasPaceCar == true ? stages.LightsVisible : raceStart.LightsVisible);
        }
        for (int i = 0; i < cars.Length; i++)
        {
            if (raceStart != null && !raceReleased)
            {
                float halfLane = Mathf.Min(1.35f, definition.pitRoad.widthMeters * 0.25f);
                var grid = raceStart.GridPosition(i, halfLane);
                if (grid.OnPitRoad)
                    PositionPitCar(cars[i], grid.Distance, grid.Lane);
                else
                    PositionTrackCar(cars[i], grid.Distance, grid.Lane);
                continue;
            }

            var traffic = isQualifying ? (i == 0 ? qualifying?.Car : null) : trafficCars?[i];
            if (traffic?.InPit == true)
            {
                PositionPitCar(cars[i], traffic.PitDistance, traffic.PitLane);
                if (practice != null || qualifying != null)
                {
                    float stallBlend = qualifying?.StallBlend() ?? practice.StallBlend(i);
                    cars[i].localPosition = Vector3.Lerp(cars[i].localPosition, practiceStallPositions[i], stallBlend);
                }
                else if ((stages != null && SessionRun.Finished(i)) || pitStops?.IsServicing(i) == true)
                {
                    float blend = 1f - Mathf.Clamp01(Mathf.Abs(traffic.PitDistance - practiceStallDistances[i]) / 6f);
                    cars[i].localPosition = Vector3.Lerp(cars[i].localPosition, practiceStallPositions[i], blend);
                }
                continue;
            }

            float distance = traffic?.TrackDistance ?? (sessionDistances == null ? elapsed * CarSpeed(i) + lapLength * i / cars.Length : sessionDistances[i]);
            if (crashes[i].Active)
            {
                PositionTrackCar(cars[i], distance, crashes[i].Lane);
                cars[i].localRotation *= Quaternion.Euler(0f, crashes[i].Yaw, 0f);
                continue;
            }

            float lane = drivingLanesReady ? drivingLanes[i] : TargetDrivingLane(i, distance, traffic);
            if (traffic != null && traffic.FlatTireLaneBlend > 0f)
            {
                var widths = RoadWidths(distance);
                float inside = customLayout?.InsideSign ?? -1f;
                if (widths.y > 0f)
                {
                    float edge = widths.x * 0.5f * inside;
                    PositionTrackCar(cars[i], distance, Mathf.Lerp(lane, edge, traffic.FlatTireLaneBlend));
                    cars[i].localPosition += Vector3.Cross(Vector3.up, cars[i].localRotation * Vector3.forward).normalized *
                        (inside * widths.y * 0.5f * traffic.FlatTireLaneBlend);
                    continue;
                }
            }
            PositionTrackCar(cars[i], distance, lane);
        }
    }

    private float TargetDrivingLane(int i, float distance, TrackPitTraffic.Car traffic)
    {
        float toMerge = traffic == null ? float.PositiveInfinity
            : Mathf.Min(traffic.NextEntry - distance, distance - traffic.LastExit);
        float lane = behaviorModel.LaneOffset(i, RoadWidths(distance).x, toMerge, racingLine(distance));
        if (raceReleased && SessionRun.Elapsed < 3f)
        {
            float gridLane = raceStart.GridPosition(i, Mathf.Min(1.35f, definition.pitRoad.widthMeters * 0.25f)).Lane;
            lane = Mathf.Lerp(gridLane, lane, SessionRun.Elapsed / 3f);
        }
        if (stages != null)
            lane = stages.Lane(i, lane, Mathf.Min(1.35f, RoadWidths(distance).x * 0.2f), customLayout?.InsideSign ?? -1f);
        if (traffic != null && traffic.FlatTireLaneBlend > 0f)
            lane = Mathf.Lerp(lane, (customLayout?.InsideSign ?? -1f) * Mathf.Max(0f, RoadWidths(distance).x * 0.5f - 1.1f), traffic.FlatTireLaneBlend);
        return lane;
    }

    private void BuildRacingLine()
    {
        racingLine = behaviorModel.BuildRacingLine(lapLength, customLayout?.InsideSign ?? -1f, definition.majorStyle == "Oval", distance =>
        {
            TrackPoint(distance, 0f, out var forward, out _);
            return forward;
        });
    }

    private void PositionTrackCar(Transform car, float distance, float lane, float laneSlope = 0f)
    {
        car.localPosition = TrackPoint(distance, lane, out var forward, out var up);
        car.localRotation = Quaternion.LookRotation(forward + Vector3.Cross(up, forward).normalized * laneSlope, up);
    }

    private void PositionPitCar(Transform car, float distance, float lane = 0f)
    {
        int segment = 0;
        while (segment < pitRoute.Length - 1 && distance >= pitLengths[segment])
            distance -= pitLengths[segment++];
        float fraction = Mathf.Clamp01(distance / pitLengths[segment]);
        var spline = pitRoute[segment];
        Vector3 position = PitPosition(spline, fraction, pitHeights[segment], pitHeights[segment + 1]);
        Vector3 tangent = PitTangent(spline, fraction, pitHeights[segment], pitHeights[segment + 1]);
        car.localPosition = position + Vector3.Cross(Vector3.up, tangent).normalized * lane;
        if (tangent.sqrMagnitude > 0f)
            car.localRotation = Quaternion.LookRotation(tangent);
    }

    private Vector3 TrackPoint(float distance, float lane, out Vector3 forward, out Vector3 up)
    {
        float halfStraight = straightLength * 0.5f;
        distance = Mathf.Repeat(distance, lapLength);
        float bank = definition.banking.frontStraight;
        Vector3 center;

        if (customLayout != null)
        {
            center = customLayout.Sample(distance, out forward, out bank);
        }
        else if (distance < straightLength)
        {
            forward = Vector3.right;
            center = new Vector3(distance - halfStraight, 0f, -turnRadius);
        }
        else if (distance >= straightLength + turnLength && distance < 2f * straightLength + turnLength)
        {
            bank = definition.banking.backStraight;
            forward = Vector3.left;
            center = new Vector3(halfStraight - (distance - straightLength - turnLength), 0f, turnRadius);
        }
        else
        {
            bool rightTurn = distance < straightLength + turnLength;
            float turnStart = rightTurn ? straightLength : 2f * straightLength + turnLength;
            float progress = (distance - turnStart) / turnLength;
            float angle = (progress + (rightTurn ? -0.5f : 0.5f)) * Mathf.PI;
            var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            forward = new Vector3(-radial.z, 0f, radial.x);
            center = new Vector3(rightTurn ? halfStraight : -halfStraight, 0f, 0f) + radial * turnRadius;

            float peak = rightTurn ? definition.banking.turns12 : definition.banking.turns34;
            float entry = rightTurn ? definition.banking.frontStraight : definition.banking.backStraight;
            float exit = rightTurn ? definition.banking.backStraight : definition.banking.frontStraight;
            bank = progress < 0.5f
                ? Mathf.SmoothStep(entry, peak, progress / BankTransitionFraction)
                : Mathf.SmoothStep(exit, peak, (1f - progress) / BankTransitionFraction);
        }

        Vector3 outward = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 crossSlope = Quaternion.AngleAxis(bank, forward) * outward;
        up = Vector3.Cross(forward, crossSlope);
        return center + crossSlope * lane + Vector3.up * (RoadWidths(distance).x * 0.5f * Mathf.Abs(crossSlope.y));
    }

    private Vector2 RoadWidths(float distance) => customLayout?.Widths(Mathf.Repeat(distance, lapLength))
        ?? new Vector2(roadHalfWidth * 2f, 0f);

    private void Ring(string objectName, float inner, float outer, float height, Material material, bool outerEdgeAtGround = false, bool innerEdgeAtGround = false, bool apron = false)
    {
        int samples = customLayout?.Samples ?? SamplesPerSection * 4;
        var vertices = new Vector3[samples * 2];
        var triangles = new int[samples * 6];
        for (int i = 0; i < samples; i++)
        {
            int section = i / SamplesPerSection;
            float fraction = (float)(i % SamplesPerSection) / SamplesPerSection;
            float distance = customLayout?.SampleDistance(i) ?? (straightLength == 0f
                ? lapLength * i / samples
                : (section / 2) * (straightLength + turnLength)
                + (section % 2 == 0 ? fraction * straightLength : straightLength + fraction * turnLength));

            int current = i * 2;
            int next = (current + 2) % vertices.Length;
            Vector2 widths = RoadWidths(distance);
            float halfWidth = widths.x * 0.5f;
            if (apron)
            {
                float side = customLayout.InsideSign;
                Vector3 edge = TrackPoint(distance, halfWidth * side, out var forward, out _);
                edge.y += 0.005f;
                Vector3 apronEdge = edge + Vector3.Cross(Vector3.up, forward).normalized * (side * widths.y);
                vertices[current] = side < 0f ? apronEdge : edge;
                vertices[current + 1] = side < 0f ? edge : apronEdge;
            }
            else
            {
                float widthOffset = halfWidth - roadHalfWidth;
                Vector3 innerPoint = TrackPoint(distance, inner + Mathf.Sign(inner) * widthOffset, out _, out var up) + up * height;
                if (innerEdgeAtGround)
                    innerPoint.y = groundHeight - 0.06f;
                vertices[current] = innerPoint;
                Vector3 outerPoint = TrackPoint(distance, outer + Mathf.Sign(outer) * widthOffset, out _, out _) + up * height;
                if (outerEdgeAtGround)
                    outerPoint.y = groundHeight - 0.06f;
                vertices[current + 1] = outerPoint;
            }

            int triangle = i * 6;
            triangles[triangle] = current;
            triangles[triangle + 1] = next;
            triangles[triangle + 2] = current + 1;
            triangles[triangle + 3] = current + 1;
            triangles[triangle + 4] = next;
            triangles[triangle + 5] = next + 1;
        }

        AddRoadMesh(objectName, material, vertices, triangles);
    }

    private void AddRoadMesh(string objectName, Material material, Vector3[] vertices, int[] triangles)
    {
        var mesh = new Mesh { name = objectName, vertices = vertices, triangles = triangles };
        mesh.RecalculateNormals();
        roadMeshes.Add(mesh);
        var surface = CreateObject(objectName, transform);
        surface.AddComponent<MeshFilter>().sharedMesh = mesh;
        surface.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    private void DrawPitRoad(TrackPitRoad pit, Material pavement, Material markings)
    {
        var spline = new TrackPitSpline(pit.points);
        Vector3 pitCenter = customLayout.ToTrackSpace(spline.Sample(0.5f));
        float nearestTrack = PitAttachment(spline.Sample(0.5f));
        Vector3 trackCenter = TrackPoint(nearestTrack, 0f, out var trackForward, out _);
        pitRoadOutside = Vector3.Dot(pitCenter - trackCenter, Vector3.Cross(Vector3.up, trackForward)) *
            customLayout.InsideSign < 0f;
        float roadStart = customLayout.HasElevation
            ? customLayout.Sample(PitAttachment(spline.Sample(0f)), out _, out _).y : 0f;
        float roadEnd = customLayout.HasElevation
            ? customLayout.Sample(PitAttachment(spline.Sample(1f)), out _, out _).y : 0f;
        DrawPitRibbon("Pit road", spline, pit.widthMeters, pavement, roadStart, roadEnd);
        var entrance = pit.entrance?.Length >= 2 ? new TrackPitSpline(pit.entrance) : null;
        var exit = pit.exit?.Length >= 2 ? new TrackPitSpline(pit.exit) : null;
        float entryDistance = 0f;
        float exitDistance = 0f;
        float entryHeight = 0f;
        float exitHeight = 0f;
        if (entrance != null)
        {
            entryDistance = PitAttachment(entrance.Sample(0f));
            entryHeight = TrackPoint(entryDistance, 0f, out _, out _).y;
            DrawPitRibbon("Pit entrance", entrance, pit.widthMeters, pavement, entryHeight, roadStart, underMainTrack: true);
        }
        if (exit != null)
        {
            exitDistance = PitAttachment(exit.Sample(1f));
            exitHeight = TrackPoint(exitDistance, 0f, out _, out _).y;
            DrawPitRibbon("Pit exit", exit, pit.widthMeters, pavement, roadEnd, exitHeight, underMainTrack: true);
        }
        if (entrance != null && exit != null)
        {
            float sourceScale = Vector3.Distance(customLayout.ToTrackSpace(Vector3.zero), customLayout.ToTrackSpace(Vector3.right));
            pitRoute = new[] { entrance, spline, exit };
            pitLengths = new[] { entrance.Length * sourceScale, spline.Length * sourceScale, exit.Length * sourceScale };
            pitHeights = new[] { entryHeight, roadStart, roadEnd, exitHeight };
            pitTraffic = new TrackPitTraffic(lapLength, entryDistance, exitDistance,
                pitLengths[0], pitLengths[1], pitLengths[2], pit);
            const int pitProgressSamples = 128;
            var trackDistances = new float[pitProgressSamples + 1];
            for (int i = 1; i < pitProgressSamples; i++)
            {
                float distance = pitTraffic.RouteLength * i / pitProgressSamples;
                int segment = 0;
                while (segment < pitRoute.Length - 1 && distance >= pitLengths[segment])
                    distance -= pitLengths[segment++];

                var position = pitRoute[segment].Sample(distance / pitLengths[segment]);
                trackDistances[i] = PitAttachment(position);
            }
            pitTraffic.SetProgressAlongRoute(trackDistances);
            trafficCars = new TrackPitTraffic.Car[cars.Length];
            for (int i = 0; i < cars.Length; i++)
                trafficCars[i] = pitTraffic.CreateTrackCar(lapLength * i / cars.Length);
        }
        DrawPitLine("Pit speed-limit start", spline, pit.speedLimitStart, pit.widthMeters, markings, roadStart, roadEnd);
        DrawPitLine("Pit speed-limit end", spline, pit.speedLimitEnd, pit.widthMeters, markings, roadStart, roadEnd);
        var markedStalls = pit.stalls ?? System.Array.Empty<TrackPitPoint>();
        if (pitTraffic != null)
        {
            practiceStallDistances = new float[cars.Length];
            practiceStallPositions = new Vector3[cars.Length];
        }

        for (int i = 0; i < markedStalls.Length; i++)
        {
            var source = TrackPitSpline.Position(markedStalls[i]);
            Vector3 position = customLayout.ToTrackSpace(source);
            float fraction = spline.ClosestFraction(source);
            if (customLayout.HasElevation)
                position.y = PitHeight(roadStart, roadEnd, fraction);

            if (practiceStallDistances != null && i < cars.Length)
            {
                practiceStallDistances[i] = pitLengths[0] + fraction * pitLengths[1];
                position.y = PitHeight(roadStart, roadEnd, fraction);
                practiceStallPositions[i] = position;
            }
            Box("Pit stall", transform, position + Vector3.up * 0.055f, new Vector3(0.6f, 0.025f, 0.6f), markings);
        }

        if (practiceStallDistances != null)
            FillPracticeStalls(spline, pit, markedStalls.Length);
    }

    private void ConfigurePitField()
    {
        stallLanes ??= new float[cars.Length];
        for (int i = 0; i < cars.Length; i++)
        {
            float fraction = Mathf.Clamp01((practiceStallDistances[i] - pitLengths[0]) / pitLengths[1]);
            var center = PitPosition(pitRoute[1], fraction, pitHeights[1], pitHeights[2]);
            var across = Vector3.Cross(Vector3.up, PitTangent(pitRoute[1], fraction, pitHeights[1], pitHeights[2])).normalized;
            stallLanes[i] = Vector3.Dot(practiceStallPositions[i] - center, across);
        }
        pitTraffic.ConfigureField(trafficCars, practiceStallDistances, stallLanes, customLayout.InsideSign);
    }

    private void FillPracticeStalls(TrackPitSpline spline, TrackPitRoad pit, int assigned)
    {
        for (int i = assigned; i < cars.Length; i++)
        {
            float bestFraction = 0.05f;
            float bestClearance = -1f;
            for (int sample = 5; sample <= 95; sample++)
            {
                float fraction = sample / 100f;
                float distance = pitLengths[0] + fraction * pitLengths[1];
                float clearance = Mathf.Min(fraction, 1f - fraction) * pitLengths[1];
                for (int j = 0; j < i; j++)
                    clearance = Mathf.Min(clearance, Mathf.Abs(distance - practiceStallDistances[j]));
                if (clearance > bestClearance)
                {
                    bestClearance = clearance;
                    bestFraction = fraction;
                }
            }

            float roadStart = pitHeights[1];
            float roadEnd = pitHeights[2];
            var center = PitPosition(spline, bestFraction, roadStart, roadEnd);
            var tangent = PitTangent(spline, bestFraction, roadStart, roadEnd);
            practiceStallDistances[i] = pitLengths[0] + bestFraction * pitLengths[1];
            practiceStallPositions[i] = center + Vector3.Cross(Vector3.up, tangent) * (pit.widthMeters * 0.35f);
        }
    }

    private float PitAttachment(Vector3 source) => Mathf.Repeat(
        TrackUtilityGeometry.ClosestMainDistance(customLayout, lapLength, customLayout.ToTrackSpace(source)), lapLength);

    private static float PitHeight(float startHeight, float endHeight, float fraction) =>
        Mathf.SmoothStep(startHeight, endHeight, fraction) + 0.02f;

    private void DrawPitRibbon(string objectName, TrackPitSpline spline, float width, Material material, float startHeight, float endHeight,
        bool underMainTrack = false)
    {
        const int segments = 128;
        int strips = underMainTrack ? 8 : 1;
        int stride = strips + 1;
        var vertices = new Vector3[(segments + 1) * stride];
        var clearance = new float[vertices.Length];
        var triangles = new List<int>(segments * strips * 6);
        for (int i = 0; i <= segments; i++)
        {
            float fraction = i / (float)segments;
            Vector3 center = PitPosition(spline, fraction, startHeight, endHeight);
            Vector3 across = Vector3.Cross(Vector3.up, PitTangent(spline, fraction, startHeight, endHeight)).normalized;
            for (int j = 0; j <= strips; j++)
            {
                int index = i * stride + j;
                vertices[index] = center + across * (width * (j / (float)strips - 0.5f));
                if (underMainTrack)
                    clearance[index] = MainTrackClearance(vertices[index]);
            }
        }

        for (int i = 0; i < segments; i++)
        {
            for (int j = 0; j < strips; j++)
            {
                int vertex = i * stride + j;
                AddPitTriangle(vertex, vertex + stride, vertex + 1);
                AddPitTriangle(vertex + 1, vertex + stride, vertex + stride + 1);
            }
        }
        AddRoadMesh(objectName, material, vertices, triangles.ToArray());

        void AddPitTriangle(int a, int b, int c)
        {
            if (underMainTrack && (clearance[a] < 0f || clearance[b] < 0f || clearance[c] < 0f))
                return;
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }
    }

    private float MainTrackClearance(Vector3 vertex)
    {
        float distance = TrackUtilityGeometry.ClosestMainDistance(customLayout, lapLength, vertex);
        Vector3 center = TrackPoint(distance, 0f, out var forward, out _);
        Vector3 across = Vector3.Cross(Vector3.up, forward).normalized;
        return Mathf.Abs(Vector3.Dot(vertex - center, across)) - RoadWidths(distance).x * 0.5f;
    }

    private void DrawPitLine(string objectName, TrackPitSpline spline, float fraction, float width, Material markings, float startHeight, float endHeight)
    {
        Vector3 position = PitPosition(spline, fraction, startHeight, endHeight);
        var line = Box(objectName, transform, position + Vector3.up * 0.05f,
            new Vector3(width, 0.02f, 0.35f), markings);
        line.transform.localRotation = Quaternion.LookRotation(PitTangent(spline, fraction, startHeight, endHeight));
    }

    private Vector3 PitPosition(TrackPitSpline spline, float fraction, float startHeight, float endHeight)
    {
        Vector3 position = customLayout.ToTrackSpace(spline.Sample(fraction));
        position.y = PitHeight(startHeight, endHeight, Mathf.Clamp01(fraction));
        return position;
    }

    private Vector3 PitTangent(TrackPitSpline spline, float fraction, float startHeight, float endHeight)
    {
        return (PitPosition(spline, fraction + 0.005f, startHeight, endHeight) -
            PitPosition(spline, fraction - 0.005f, startHeight, endHeight)).normalized;
    }

    private Material MakeMaterial(Color color)
    {
        var material = new Material(surfaceMaterial);
        material.color = color;
        materials.Add(material);
        return material;
    }

    private static GameObject CreateObject(string objectName, Transform parent)
    {
        var item = new GameObject(objectName) { layer = PreviewLayer };
        item.transform.SetParent(parent, false);
        return item;
    }

    internal static void BuildCar(Transform car, int number, Material hoodAndTrunkPaint, Material bodyPaint,
        Material glass, Material rubber, string[] sponsorNames = null)
    {
        var model = CreateImpala(car, hoodAndTrunkPaint, bodyPaint, glass, rubber);
        if (model == null)
        {
            Box("Chassis", car, new Vector3(0f, 0.39f, 0f), new Vector3(1.55f, 0.53f, 2.9f), bodyPaint);
            Box("Hood", car, new Vector3(0f, 0.665f, 0.975f), new Vector3(1.53f, 0.04f, 0.95f), hoodAndTrunkPaint);
            Box("Trunk", car, new Vector3(0f, 0.665f, -1.095f), new Vector3(1.53f, 0.04f, 0.71f), hoodAndTrunkPaint);
            Box("Cabin", car, new Vector3(0f, 0.78f, -0.12f), new Vector3(1.17f, 0.42f, 1.25f), glass);
            Box("Roof", car, new Vector3(0f, 1.02f, -0.12f), new Vector3(1.05f, 0.09f, 0.96f), bodyPaint);
            Box("Front tires", car, new Vector3(0f, 0.22f, 0.87f), new Vector3(1.8f, 0.41f, 0.5f), rubber);
            Box("Rear tires", car, new Vector3(0f, 0.22f, -0.87f), new Vector3(1.8f, 0.41f, 0.5f), rubber);
        }
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
            return;

        var decalMaterial = GetCarDecalMaterial(font);

        AddNumber(Anchor("RoofNumber", new Vector3(0f, 1.066f, -0.12f)), Quaternion.Euler(90f, 0f, 0f));
        AddNumber(Anchor("RightNumber", new Vector3(0.776f, 0.49f, 0f)), Quaternion.Euler(0f, model == null ? -90f : (Anchor("RightNumber", Vector3.right).x > 0f ? -90f : 90f), 0f));
        AddNumber(Anchor("LeftNumber", new Vector3(-0.776f, 0.49f, 0f)), Quaternion.Euler(0f, model == null ? 90f : (Anchor("LeftNumber", Vector3.left).x > 0f ? -90f : 90f), 0f));

        if (sponsorNames != null)
        {
            AddSponsor(0, Anchor("HoodSponsor", new Vector3(0f, 0.686f, 0.975f)), hoodAndTrunkPaint);
            AddSponsor(1, Anchor("TrunkSponsor", new Vector3(0f, 0.686f, -1.095f)), hoodAndTrunkPaint);
            AddSponsor(2, Anchor("RoofSponsor", new Vector3(0f, 1.066f, -0.43f)), bodyPaint);
        }

        Vector3 Anchor(string name, Vector3 fallback)
        {
            if (model == null)
                return fallback;
            var anchor = System.Array.Find(model.GetComponentsInChildren<Transform>(), item => item.name == name);
            if (anchor == null)
                return fallback;
            var point = car.InverseTransformPoint(anchor.position);
            if (name == "RightNumber" || name == "LeftNumber")
                point.x = Mathf.Sign(point.x) * (Mathf.Abs(point.x) + 0.008f);
            return point;
        }

        void AddSponsor(int slot, Vector3 position, Material paint)
        {
            string sponsor = sponsorNames[slot];
            if (string.IsNullOrEmpty(sponsor))
                return;

            var decal = CreateObject("Sponsor decal", car);
            decal.transform.localPosition = position;
            decal.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var text = decal.AddComponent<TextMesh>();
            text.font = font;
            text.text = sponsor;
            text.anchor = TextAnchor.MiddleCenter;
            text.fontStyle = FontStyle.Bold;
            text.fontSize = 48;
            text.characterSize = Mathf.Min(0.035f, (slot == 2 ? 0.11f : 0.15f) / sponsor.Length);
            Color background = paint.color;
            text.color = 0.213f * background.r + 0.715f * background.g + 0.072f * background.b > 0.52f
                ? Color.black : Color.white;
            decal.GetComponent<MeshRenderer>().sharedMaterial = decalMaterial;
        }

        void AddNumber(Vector3 position, Quaternion rotation)
        {
            var label = CreateObject("Race number", car);
            label.transform.localPosition = position;
            label.transform.localRotation = rotation;
            var text = label.AddComponent<TextMesh>();
            text.font = font;
            text.text = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            text.anchor = TextAnchor.MiddleCenter;
            text.fontStyle = FontStyle.Bold;
            text.fontSize = 64;
            text.characterSize = 0.06f;
            Color body = bodyPaint.color;
            text.color = 0.213f * body.r + 0.715f * body.g + 0.072f * body.b > 0.52f ? Color.black : Color.white;
            label.GetComponent<MeshRenderer>().sharedMaterial = decalMaterial;
        }
    }

    private static Material carDecalMaterial;
    private static Font carDecalFont;

    private static Material GetCarDecalMaterial(Font font)
    {
        if (carDecalMaterial == null)
        {
            var shader = Resources.Load<Shader>("Startup/CarDecal");
            if (shader == null)
                return font.material;

            carDecalMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            Font.textureRebuilt -= RefreshCarDecalAtlas;
            Font.textureRebuilt += RefreshCarDecalAtlas;
        }

        carDecalFont = font;
        RefreshCarDecalAtlas(font);
        return carDecalMaterial;
    }

    private static void RefreshCarDecalAtlas(Font font)
    {
        if (font == carDecalFont && carDecalMaterial != null)
            carDecalMaterial.mainTexture = font.material.mainTexture;
    }

    private static GameObject CreateImpala(Transform car, Material accent, Material body, Material glass, Material rubber)
    {
        var asset = Resources.Load<GameObject>("Startup/Impala");
        if (asset == null)
            return null;

        var model = Instantiate(asset, car, false);
        var children = model.GetComponentsInChildren<Transform>();
        var front = System.Array.Find(children, item => item.name == "CarFront");
        var rear = System.Array.Find(children, item => item.name == "CarRear");
        if (front != null && rear != null)
        {
            Vector3 forward = car.InverseTransformVector(front.position - rear.position);
            forward.y = 0;
            if (forward.sqrMagnitude > 0.001f)
                model.transform.localRotation = Quaternion.Inverse(Quaternion.LookRotation(forward));
        }

        foreach (var child in children)
            child.gameObject.layer = PreviewLayer;

        var renderers = model.GetComponentsInChildren<MeshRenderer>();
        if (renderers.Length == 0)
        {
            Destroy(model);
            return null;
        }

        Bounds bounds = default;
        bool first = true;
        foreach (var renderer in renderers)
        {
            Bounds mesh = renderer.localBounds;
            for (int corner = 0; corner < 8; corner++)
            {
                var point = new Vector3((corner & 1) == 0 ? mesh.min.x : mesh.max.x,
                    (corner & 2) == 0 ? mesh.min.y : mesh.max.y, (corner & 4) == 0 ? mesh.min.z : mesh.max.z);
                point = car.InverseTransformPoint(renderer.transform.TransformPoint(point));
                if (first)
                    bounds = new Bounds(point, Vector3.zero);
                else
                    bounds.Encapsulate(point);
                first = false;
            }

            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = materials[i]?.name switch
                {
                    "HoodTrunkPaint" => accent,
                    "windows" => glass,
                    "window net" or "wheel" or "brake" => rubber,
                    _ => body
                };
            }
            renderer.sharedMaterials = materials;
        }

        float scale = 2.9f / Mathf.Max(0.001f, bounds.size.z);
        model.transform.localScale *= scale;
        model.transform.localPosition -= scale * new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        return model;
    }

    private static GameObject Box(string objectName, Transform parent, Vector3 position, Vector3 size, Material material)
    {
        var item = GameObject.CreatePrimitive(PrimitiveType.Cube);
        item.name = objectName;
        item.layer = PreviewLayer;
        item.transform.SetParent(parent, false);
        item.transform.localPosition = position;
        item.transform.localScale = size;
        item.GetComponent<MeshRenderer>().sharedMaterial = material;
        item.GetComponent<BoxCollider>().enabled = false;
        return item;
    }

    private void OnDestroy()
    {
        if (raceCamera != null)
            raceCamera.targetTexture = null;
        if (Image != null)
        {
            Image.Release();
            Destroy(Image);
        }

        foreach (var mesh in roadMeshes)
            Destroy(mesh);
        foreach (var material in materials)
            Destroy(material);
    }
}
