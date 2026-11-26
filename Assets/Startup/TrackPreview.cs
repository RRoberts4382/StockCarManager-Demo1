using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TextCore.LowLevel;

public sealed partial class TrackPreview : MonoBehaviour
{
    private const float StartingViewSize = 25f;
    private const float CloseViewSize = 0.35f;
    private const float MouseWheelZoomRate = 0.055f;
    private const float ClosestSpotterFieldOfView = 3f;
    private const int PreviewLayer = 31;
    private static readonly int SkyNight = Shader.PropertyToID("_Night");
    private static readonly int SkyCloudSeed = Shader.PropertyToID("_CloudSeed");
    private Material trackSkyMaterial;
    private bool? practiceSkyNightOverride;
    private const uint BristolLightingLayer = 1u << 1;
    private Light bristolSun;
    private readonly List<Light> bristolFloodlights = new List<Light>();
    private Material bristolFloodlightLens;
    private Light previousSun;
    private UniversalRenderPipelineAsset bristolPipeline;
    private float previousShadowDistance;
    internal const int DarlingtonTrackId = 4;
    internal const int RichmondTrackId = 5;
    internal const int NorthWilkesboroTrackId = 13;
    internal const int PhoenixTrackId = 9;
    private const int MichiganTrackId = 11;
    private const int NashvilleFairgroundsTrackId = 17;
    private const int PoconoTrackId = 18;
    internal const int BristolTrackId = 27;
    internal const int OrangeCountyTrackId = 28;
    internal const int SouthBostonTrackId = 29;
    internal const int DaytonaTrackId = 30;
    internal const int TexasTrackId = 31;
    internal const int KansasTrackId = 32;
    internal const int MartinsvilleTrackId = 33;
    internal const int GatewayTrackId = 34;
    internal const int CotaTrackId = 21;
    private const int BristolBaysPerRow = 20;
    private const float BristolBayPitch = 4f;
    private const float BristolBayCenterX = 9f;
    private const float BristolHaulerCenterZ = 15.5f;
    private GameObject parkedHaulers;
    private GameObject teamPitBoxes;
    private float pitWallSide;
    private const float PitWallThickness = 0.30f;
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
    private readonly List<Texture2D> carPaintTextures = new List<Texture2D>();
    private readonly List<Mesh> roadMeshes = new List<Mesh>();
    private Mesh preferredLineMesh;
    private MeshRenderer preferredLineRenderer;
    internal const int CarCount = 40;
    private Transform[] cars;
    private (Color HoodAndTrunk, Color RoofAndSides)[] renderedCarPaint;
    private readonly List<Vector2[]> mapPitRibbons = new List<Vector2[]>();
    internal IReadOnlyList<Vector2[]> MapPitRibbons => mapPitRibbons;
    private RearAxleHeading[] carHeadings;
    internal static readonly string[] CarDetailLabels = { "Low", "Medium", "High", "Very High", "Full" };
    internal bool SupportsCarDetail { get; private set; }
    internal bool SupportsGrandstands => grandstandScenery != null;
    internal bool GrandstandsVisible => SupportsGrandstands && grandstandScenery.activeSelf;
    internal int CarDetailIndex { get; private set; }
    private CarTypeDefinition[] carTypes;

    private RaceWheelAnimation[] raceWheels;
    private RaceWheelAnimation paceWheels;
    internal enum CameraAngle { Player, Chase, Hood, InCar, TV, Spotter, PitBox, Cycle, RearChase, Leader }

    private Camera raceCamera;
    private TrackEngineAudio engineAudio;
    private Camera rearViewCamera;
    internal RenderTexture RearViewImage { get; private set; }
    private Vector3[] tvCameraPositions;
    private int activeTvCamera = -1;
    private Vector3 spotterPosition;
    private Vector3? playerPitBoxCameraPosition;
    private float spotterFieldOfView;
    private float spotterZoomFieldOfView;
    private Quaternion spotterAimOffset = Quaternion.identity;
    private (Vector3 Hood, Vector3 DriverEye)[] onboardCameraOffsets;
    internal CarTypeDefinition CarType => carTypes[0];
    internal CarTypeDefinition SelectedCarType => carTypes[SelectedCarIndex];
    private Material carGlass;
    private Transform cockpitGlassCar;
    private readonly List<MeshRenderer> hiddenCockpitGlass = new List<MeshRenderer>();
    internal CameraAngle ViewAngle { get; private set; }
    internal bool CanZoom => ViewAngle is CameraAngle.Player or CameraAngle.Spotter;
    internal bool TrackSkyIsNight { get; private set; }
    internal bool SupportsRaceNightLighting => bristolFloodlightLens != null;
    internal bool CanUsePitBoxCamera => practiceStallPositions?.Length > 0;

    internal static string CameraAngleName(CameraAngle angle) => angle switch
    {
        CameraAngle.InCar => "In-Car",
        CameraAngle.PitBox => "Pit Box",
        CameraAngle.RearChase => "Rear Chase",
        _ => angle.ToString()
    };
    private Transform grassGround;
    private float groundHeight;
    private Material surfaceMaterial;
    private float elapsed;
    private TrackSnapshot snapshot;
    private double snapshotTime;
    private System.Action<int, int, int> recordOvalPassAttempt;
    private const int StrictOvalPassing = 100;
    private const int DefaultOvalPassing = 50;
    private int ovalPassingStrictness = DefaultOvalPassing;
    internal bool CanSaveSnapshot => snapshot != null;

    internal string SaveSnapshot()
    {
        PositionCars();
        CaptureSnapshot(force: true);
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
    private TrackDraft draft;
    private TrackDraftPass draftPassing;
    private TrackDraftLineMerge draftMerging;
    private TrackDraftLeaderBlock draftLeaderBlock;
    private bool[] directedDraftLaneChanges;
    private bool[] draftEligible;
    private bool[] draftBumpEligible;
    private bool[] draftManeuverEligible;
    private float[] draftPitApproach;
    private float[] trailingSpeedBoosts;
    private int plateDraftRaceLeader = -1;
    private int[] draftBumpsGiven, draftBumpsReceived;
    private IVehicleSpeedModel speedModel = VehicleSpeedModels.Available[0];
    private CarSimulationContext simulation;
    private float[] baseSpeeds;
    private readonly System.Random simulationRandom = new System.Random();
    internal RaceSessionRun SessionRun { get; private set; }
    private RaceSessionRun previewTiming;
    internal RaceSessionRun TimingRun => SessionRun ?? previewTiming;
    internal int TireWearRate => simulation?.TireWearRate ?? 1;

    internal void SetTireWearRate(int rate)
    {
        if (simulation != null && rate is (1 or 2 or 4 or 8))
            simulation.TireWearRate = rate;
    }
    private CarPaintStyle?[] scoringPaint;

    internal void StyleScoringNumber(UnityEngine.UIElements.Label label, int index)
    {
        var paint = scoringPaint[index];
        var colors = renderedCarPaint[index];
        Color body = colors.RoofAndSides;
        label.style.backgroundColor = body;
        label.style.borderLeftColor = colors.HoodAndTrunk;
        label.style.color = paint?.NumberColor is Color32 numberColor ? (Color)numberColor : CarNumberFonts.ContrastColor(body);
        CarNumberFonts.Apply(label, paint);
    }

    internal void StyleMapNumber(UnityEngine.UIElements.Label label, int index)
    {
        if (index != 0 && scoringPaint[index] is CarPaintStyle rivalPaint)
            CarNumberFonts.Apply(label, rivalPaint);
    }
    private TrackPitTraffic pitTraffic;
    private TrackPitTraffic.Car[] trafficCars;
    private TrackPitSpline[] pitRoute;
    private TrackPitSpline bristolFrontEntry, bristolBackExit;
    private const int PitRibbonSegments = 128;
    private const int PitConnectorStrips = 8;
    private readonly Dictionary<TrackPitSpline, Vector3[]> bristolConnectorVertices = new Dictionary<TrackPitSpline, Vector3[]>();
    private float bristolFrontJoin, bristolBackLeave, bristolBackExitLength;
    private float bristolFrontEntryHeight, bristolBackExitHeight;
    private float[] pitLengths;
    private float[] pitHeights;
    private float pitEntryTrackLane;
    private float pitExitTrackLane;
    private float formationExitLane, formationMergeLength;
    private float pacePitEntryLane;
    private Pose pacePitEntryPose, cautionPaceExitPose;
    private float cautionPaceExitLane, cautionPaceMergeLength;
    private float cautionPaceAvoidanceLane;
    private float cautionPaceTrafficSpeed;
    private TrackSnapshot.PaceCarSample cautionPaceSample;
    private Vector3 previousPaceSnapshotPosition;
    private double previousPaceSnapshotTime = double.NaN;
    private const float PaceClearanceMargin = .55f;
    private const float PaceLaneRate = 2.5f;
    private const float FormationSeamLength = 15f;
    private const float FormationHalfWheelTrack = .85f;
    private float FormationHalfLane => Mathf.Min(1.35f, definition.pitRoad.widthMeters * 0.25f);
    private float bristolFrontEntryLane, bristolBackExitLane, bristolBackExitTrackDistance;
    private readonly Dictionary<TrackPitSpline, Vector2> pitMergeAttachments = new Dictionary<TrackPitSpline, Vector2>();
    private bool importedTrackGeometry;
    private GameObject grandstandScenery;
    private readonly List<Renderer> importedGrandstandRenderers = new();
    private float[] practiceStallDistances;
    private Vector3[] practiceStallPositions;
    private TrackState001Practice practice;
    private TrackState001Qualifying qualifying;
    private double? trackClosedSince;
    private double? pausedAt;
    private TrackState001RaceStart raceStart;
    private TrackState001Stages stages;
    private SessionPitStops pitStops;
    private bool playerGarageVisit;
    private RivalPracticeEngineer[] rivalEngineers;
    private GarageRunRecorder[] rivalRunRecorders;
    private int playerRunStartLap;
    private bool playerWasParked = true;
    internal int PlayerRunLaps => System.Math.Max(0, (SessionRun?.PlayerLaps ?? 0) - playerRunStartLap);
    private double lastHandlingSampleTime;
    private GarageRunRecorder garageRunRecorder;
    private int requestedGarageLaps;
    private int garageSimulationStartLaps;
    private readonly float[] garagePreviousWear = new float[4];
    private readonly float[] garageWearPerMeter = new float[4];
    private double garagePreviousOdometer;
    private bool garageSimulationReturning;
    private bool garageRunEndedForFuel;
    private bool garageRunReportPending;
    internal bool ConsumeGarageRunReport()
    {
        bool pending = garageRunReportPending;
        garageRunReportPending = false;
        return pending;
    }
    internal bool SimulatingGarageLaps => requestedGarageLaps > 0;
    internal void CancelGarageLapSimulation() => requestedGarageLaps = 0;

    internal void BeginGarageLaps(int laps)
    {
        requestedGarageLaps = laps;
        garageSimulationStartLaps = garageRunRecorder?.TotalCompletedLaps ?? 0;
        garagePreviousOdometer = trafficCars?[0]?.Odometer ?? 0;
        for (int i=0;i<4;i++)
        {
            garagePreviousWear[i] = SessionRun.Vehicle(0)?.GetTire(i)?.wear ?? 0;
            garageWearPerMeter[i] = 0;
        }
        garageRunEndedForFuel = false;
        garageRunReportPending = false;
        garageSimulationReturning = false;
        SetPlayerGarageVisit(false);
        SetSessionSpeed(200);
    }

    internal void FinishPracticeSetups()
    {
        if (SessionRun == null || simulation?.Session.kind != RaceSessionKind.Practice || simulation.Session.rivalFinalSetupReviewed) return;
        if (rivalRunRecorders != null)
            for (int i=1;i<rivalRunRecorders.Length;i++) rivalRunRecorders[i]?.Finish("Practice ended");
        if (rivalEngineers != null && simulation.Event?.SetupLocked != true)
            for (int i=1;i<rivalEngineers.Length;i++)
                rivalEngineers[i].FinishPractice(i,SessionRun,simulation.Session,
                    simulation.Session.rivalPracticeTelemetry[i-1],definition,simulation.Event);
        simulation.Session.rivalFinalSetupReviewed=true;
    }

    internal void FinishGarageRun(string reason)
    {
        garageRunRecorder?.Finish(reason);
        requestedGarageLaps = 0;
    }

    private GarageRunDiagnostics CaptureGarageRunDiagnostics(int index = 0)
    {
        var car = SessionRun.Vehicle(index);
        var setup = SessionRun.SetupForCar(index) ?? car?.setup;
        var wheels = raceWheels[index];
        var data = new GarageRunDiagnostics
        {
            sessionSeconds = SessionRun.Elapsed,
            actualTrackPositionMeters = RaceSessionRun.TrackPosition((qualifying != null && index == 0 ? qualifying.Car : trafficCars?[index])?.PreciseTrackDistance ?? sessionDistances[index], lapLength),
            thermalTrackPositionMeters = SessionRun.ThermalTrackPosition(index),
            thermalOnTrack = SessionRun.ThermalOnTrack(index),
            chassis = car?.chassisNumber,
            manufacturer = car == null ? null : ManufacturerCatalog.Name(car.manufacturer),
            carNumber = car?.number ?? 0,
            trackModel = simulationModel.Id,
            speedModel = speedModel.Id,
            behaviorModel = behaviorModel.Id,
            flag = TrackState?.ToString() ?? "Running",
            collisions = collisionsEnabled,
            trafficInteractions = TrafficLaneChangesEnabled,
            playbackSpeed = sessionSpeed,
            lapSpeedFactor = SessionRun.SpeedFactor(index),
            damagePercent = car?.Damage ?? 0f,
            hasCarControls = true,
            hasSteeringAngle = wheels != null,
            steeringAngleDegrees = wheels?.FrontSteeringAngle ?? 0f,
            throttleInput = SessionRun.ThrottleInput(index),
            brakeInput = SessionRun.BrakeInput(index),
            hasPowertrainSample = true,
            engineRpm = (float)SessionRun.EngineRpm(index),
            speedMph = SessionRun.CurrentSpeed(index) * 3600f / TrackDefinition.MetersPerMile,
            weatherForecast = simulation.Session.weather,
            weatherLocked = SessionRun.Weather.IsLocked,
            ambientFahrenheit = SessionRun.Weather.AmbientFahrenheit,
            trackFahrenheit = SessionRun.Weather.TrackFahrenheit,
            cloudCover = SessionRun.Weather.CloudCover,
            windMph = SessionRun.Weather.WindMph,
            trackGripFactor = SessionRun.Weather.GripFactor,
            fuelGallons = SessionRun.Fuel(index),
            weightDynamicsEnabled = speedModel is VehicleSpeed003,
            equalConditionTesting = simulation.EqualConditionTesting,
            effectiveFrontWeightPercent = speedModel is VehicleSpeed003 ? VehicleSpeed003.FrontWeightPercent(setup, SessionRun.Fuel(index)) : 0f,
            effectiveCrossWeightPercent = speedModel is VehicleSpeed003 ? VehicleSpeed003.CrossWeightPercent(setup, SessionRun.Fuel(index)) : 0f,
            effectiveLeftWeightPercent = speedModel is VehicleSpeed003 ? VehicleSpeed003.LeftWeightPercent(setup, SessionRun.Fuel(index)) : 0f,
            cornerWeightsPounds = speedModel is VehicleSpeed003 ? new float[4] : null,
            dynamicCornerWeightsPounds = speedModel is VehicleSpeed003 ? new float[4] : null,
            vehicleWeightPounds = speedModel is VehicleSpeed003 ? VehicleSpeed003.WeightPounds(SessionRun.Fuel(index)) : 0f,
            fuelWeightPounds = speedModel is VehicleSpeed003 ? VehicleSpeed003.WeightPounds(SessionRun.Fuel(index)) - VehicleSpeed003.DryWeightPounds : 0f,
            setup = setup == null ? null : JsonUtility.FromJson<Setup>(JsonUtility.ToJson(setup)),
            tireWear = new float[4],
            coldPressuresPsi = new float[4], runningPressuresPsi = new float[4],
            componentNames = new string[TeamCar.Components.Length],
            componentEffectiveValues = new float[TeamCar.Components.Length]
        };
        for (int tire = 0; tire < 4; tire++)
        {
            data.tireWear[tire] = car?.GetTire(tire)?.wear ?? 0f;
            data.coldPressuresPsi[tire] = data.tireWear[tire] >= 1f ? 0f : VehicleSpeed003.ColdPressure(setup, tire, car?.GetTire(tire));
            data.runningPressuresPsi[tire] = data.tireWear[tire] >= 1f ? 0f : VehicleSpeed003.HotPressure(data.coldPressuresPsi[tire], TireTemperatureModel.Fahrenheit(car?.GetTire(tire)?.temperature ?? 0f));
            if (data.dynamicCornerWeightsPounds != null)
                data.dynamicCornerWeightsPounds[tire] = SessionRun.DynamicCornerWeight(index, tire);
            if (data.cornerWeightsPounds != null)
                data.cornerWeightsPounds[tire] = VehicleSpeed003.CornerWeightPounds(setup, SessionRun.Fuel(index), tire);
        }
        for (int component = 0; component < TeamCar.Components.Length; component++)
        {
            data.componentNames[component] = TeamCar.Components[component].ToString();
            data.componentEffectiveValues[component] = car?.GetComponent(TeamCar.Components[component]).EffectiveValue ?? 0f;
        }
        return data;
    }

    private GarageRunTimeSample CapturePlayerRunTimeSample()
    {
        var car = SessionRun.Vehicle(0);
        var setup = SessionRun.SetupForCar(0) ?? car?.setup;
        var wheels = raceWheels[0];
        var sample = new GarageRunTimeSample
        {
            hasSteering = wheels != null,
            steeringAngleDegrees = wheels?.FrontSteeringAngle ?? 0f,
            throttle = SessionRun.ThrottleInput(0),
            brake = SessionRun.BrakeInput(0),
            hasPowertrainSample = true,
            engineRpm = (float)SessionRun.EngineRpm(0),
            speedMph = SessionRun.CurrentSpeed(0) * 3600f / TrackDefinition.MetersPerMile,
            hasDynamicLoads = speedModel is VehicleSpeed003
        };
        for (int corner = 0; corner < 4; corner++)
        {
            var tire = car?.GetTire(corner);
            float temperature = TireTemperatureModel.Fahrenheit(tire?.temperature ?? 0f);
            sample.temperatures[corner] = temperature;
            sample.pressures[corner] = tire?.wear >= 1f ? 0f :
                VehicleSpeed003.HotPressure(VehicleSpeed003.ColdPressure(setup, corner, tire), temperature);
            if (sample.hasDynamicLoads)
                sample.dynamicLoads[corner] = SessionRun.DynamicCornerWeight(0, corner);
        }
        return sample;
    }

    private void ObservePlayerRun()
    {
        if (SessionRun == null || garageRunRecorder == null)
            return;
        float handlingSeconds = (float)System.Math.Max(0d, SessionRun.PreciseElapsed - lastHandlingSampleTime);
        lastHandlingSampleTime = SessionRun.PreciseElapsed;
        bool green = HandlingRunTrend.AllowsRunningState(TrackState);
        if (rivalRunRecorders != null)
            for (int i = 1; i < rivalRunRecorders.Length; i++)
            {
                var rival = trafficCars[i];
                var recorder = rivalRunRecorders[i];
                bool atStall = rival.InPit && System.Math.Abs(rival.PitDistance - practiceStallDistances[i]) < 0.01f;
                if (!SessionRun.Retired(i))
                {
                    recorder.Observe(rival.PreciseTrackDistance, SessionRun.PreciseElapsed, rival.InPit,
                        atStall, SessionRun.CurrentSpeed(i) > 0.1f, SessionRun.Vehicle(i));
                    if (!rival.InPit && green && SessionRun.TryHandlingFeedback(i, out int rp, out float rb, out float rt, out float rl))
                        recorder.RecordHandling(rp, handlingSeconds, rb, rt, rl);
                    if (rivalEngineers != null && pitStops != null)
                        rivalEngineers[i].Observe(i, SessionRun, rival, practiceStallDistances[i], pitStops,
                            green, handlingSeconds, simulation.Session, simulation.Session.rivalPracticeTelemetry[i-1], definition);
                }
                if (SessionRun.Retired(i) || SessionComplete)
                    recorder.Finish(SessionRun.Retired(i) ? "Car retired" : "Session ended");
            }
        // Once fuel ends an automated run, coasting must not create another run.
        if (garageRunEndedForFuel && SessionRun.Fuel(0) <= 0d)
            return;
        var player = qualifying?.Car ?? trafficCars?[0];
        bool inPit = player?.InPit == true;
        bool parked = inPit && player.PitDistance == practiceStallDistances[0] &&
            (SessionRun.CurrentSpeed(0) < 0.1f || pitStops?.IsServicing(0) == true);
        // Garage parking is measured from position, because commanded speed may remain positive.
        if (PlayerParkedForGarage)
            parked = true;
        if (parked) playerWasParked = true;
        else if (playerWasParked && SessionRun.CurrentSpeed(0) > 0.1f)
        {
            playerRunStartLap = SessionRun.PlayerLaps;
            playerWasParked = false;
        }
        garageRunRecorder.Observe(player?.PreciseTrackDistance ?? sessionDistances[0], SessionRun.PreciseElapsed,
            inPit, parked, SessionRun.Vehicle(0) != null && SessionRun.CurrentSpeed(0) > 0.1f,
            SessionRun.Vehicle(0));

        // Basic Simulation has no flag-state controller. Its active on-track
        // running is eligible; null must not discard all handling samples.

        if (!inPit && !parked && green &&
            SessionRun.TryHandlingFeedback(0, out int phase, out float balance, out float tireBalance, out float loadBalance))
            garageRunRecorder.RecordHandling(phase, handlingSeconds, balance, tireBalance, loadBalance);
        if (SimulatingGarageLaps && SessionRun.Fuel(0) <= 0d)
        {
            FinishGarageRun("Out of fuel");
            garageRunEndedForFuel = true;
            garageRunReportPending = true;
            SetSessionSpeed(1);
            pendingSessionTime = 0;
            return;
        }
        if (SimulatingGarageLaps && !garageSimulationReturning &&
            garageRunRecorder.TotalCompletedLaps - garageSimulationStartLaps >= requestedGarageLaps)
        {
            garageSimulationReturning = true;
            SetPlayerGarageVisit(true);
        }
        if (SimulatingGarageLaps && !garageSimulationReturning)
            ServiceGarageSimulation(player);
        if (SimulatingGarageLaps && (PlayerParkedForGarage || SessionComplete || SessionRun.Retired(0)))
        {
            if (!PlayerParkedForGarage)
                FinishGarageRun(SessionRun.Retired(0) ? "Car retired" : "Session ended");
            requestedGarageLaps = 0;
            garageRunReportPending = true;
            SetSessionSpeed(1);
            pendingSessionTime = 0;
        }
    }
    // Reserve enough distance for reaching pit entry and transiting pit road.
    // Wear prediction uses the highest observed rate since the last tire change.
    internal static bool GarageServiceDue(double fuel, double fuelReserve, float wear, float wearRate, double serviceDistance) =>
        fuel <= fuelReserve || wear >= 0.85f || wear + wearRate * serviceDistance >= 0.95f;

    private void ServiceGarageSimulation(TrackPitTraffic.Car player)
    {
        if (player == null || pitStops == null || pitTraffic == null || SessionRun.Retired(0)) return;
        double traveled = System.Math.Max(0, player.Odometer-garagePreviousOdometer);
        garagePreviousOdometer = player.Odometer;
        double reserveDistance = pitTraffic.DistanceToEntry(player) + lapLength + pitTraffic.RouteLength;
        double fuelReserve = SessionRun.FuelForDistance(reserveDistance) + 0.5;
        bool due = false;
        for (int i=0;i<4;i++)
        {
            float wear = SessionRun.Vehicle(0)?.GetTire(i)?.wear ?? 0;
            if (wear < garagePreviousWear[i]) garageWearPerMeter[i]=0;
            else if (traveled>0.001)
                garageWearPerMeter[i]=Mathf.Max(garageWearPerMeter[i],(float)((wear-garagePreviousWear[i])/traveled));
            garagePreviousWear[i]=wear;
            due |= GarageServiceDue(SessionRun.Fuel(0),fuelReserve,wear,garageWearPerMeter[i],reserveDistance);
        }
        if (!player.InPit && due && pitStops.CanRequest(SessionRun))
            pitStops.RequestPlayer(SessionRun,(float)SessionFuel.TankCapacity,15,0f);
    }

    internal bool PlayerStoppedOnTrack
    {
        get
        {
            var player = qualifying?.Car ?? trafficCars?[0];
            float speed = SessionRun?.CurrentSpeed(0) ?? float.NaN;
            return player != null && !player.InPit && !float.IsNaN(speed) && speed <= 0.01f;
        }
    }

    internal bool CanVisitGarage => haulerGaragePhase != HaulerGaragePhase.Leaving &&
        (qualifying != null || pitStops != null);
    internal bool PlayerParkedForGarage
    {
        get
        {
            var player = qualifying?.Car ?? trafficCars?[0];
            return playerGarageVisit && (UsesNorthWilkesboroHaulerGarage
                ? NorthWilkesboroParkedForGarage
                : player?.InPit == true && player.PitDistance == practiceStallDistances[0]);
        }
    }

    internal void SetPlayerGarageVisit(bool visiting)
    {
        if (!visiting && BeginNorthWilkesboroGarageDeparture())
            return;

        playerGarageVisit = visiting;
        qualifying?.SetGarageVisit(visiting, NorthWilkesboroGarageTarget);
        pitStops?.SetGarageVisit(visiting, NorthWilkesboroGarageTarget);
    }
    internal bool CanRequestCaution => (stages?.CanRequestCaution == true ||
        practice?.State == SessionTrackState.TrackOpen) &&
        trafficCars?[0] is { InPit: false, InGarage: false } &&
        !crashes[0].Active && !SessionRun.Retired(0) && !SessionRun.Finished(0) &&
        SessionRun.CurrentSpeed(0) > 2f;
    private string practiceCautionReason;
    internal string CautionReason => TrackState switch
    {
        SessionTrackState.StageComplete => stages?.CautionReason ?? "Stage break",
        SessionTrackState.Caution or SessionTrackState.StageEndingUnderCaution or SessionTrackState.FinishUnderCaution =>
            stages?.CautionReason ?? practiceCautionReason ?? "Track incident",
        _ => null
    };

    internal void RequestCaution()
    {
        if (!CanRequestCaution)
            return;

        const string reason = "Manually called caution (player test spin)";
        float direction = drivingLanes[0] > 0f ? -1f : 1f;
        StartSpin(0, 14f * direction);
        crashes[0].SideSpeed = 2f * direction;
        if (stages != null)
            stages.RequestCaution(SessionRun, trafficCars, reason, incidentParticipants: new HashSet<int> { 0 });
        else
            RequestPracticeCaution(reason);
    }

    private void RequestPracticeCaution(string reason)
    {
        if (practice.State == SessionTrackState.TrackOpen)
            practiceCautionReason = reason;
        practice.RequestCaution();
    }

    internal bool CanRequestPlayerPit => SessionRun != null &&
        (SessionRun.IsRace ||
            (practice?.State is SessionTrackState.TrackOpen or SessionTrackState.Caution && !trafficCars[0].InPit)) &&
        pitStops?.CanRequest(SessionRun) == true;
    internal bool CanEditQueuedPlayerPit => SessionRun != null && pitStops?.CanEditPlayerRequest(SessionRun) == true;
    internal string PlayerPitStatus => SessionRun.HasTerminalFailure(0) ? SessionRun.MechanicalFailure(0) : SessionRun.Retired(0)
        ? (crashes[0].Active || SessionRun.Vehicle(0)?.Damage >= 100f ? "Retired - damage" : "Out of fuel")
        : pitStops?.PlayerStatus ?? string.Empty;

    internal void RequestPlayerPit(float fuelGallons, int tireMask, float repairPoints, float[] pressures = null)
    {
        if (CanRequestPlayerPit)
            pitStops.RequestPlayer(SessionRun, fuelGallons, tireMask, repairPoints, pressures, playerEditable: true);
    }
    internal void EditPlayerPitRequest(float fuelGallons, int tireMask, float repairPoints, float[] pressures)
    {
        if (CanEditQueuedPlayerPit)
            pitStops.EditPlayerRequest(SessionRun, fuelGallons, tireMask, repairPoints, pressures);
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
            if (stages == null)
                return string.Empty;

            if (TrackState == SessionTrackState.FinishUnderCaution)
                return "No Lucky Dog awarded";
            if (TrackState != SessionTrackState.Caution)
                return string.Empty;

            int index = stages.FreePassCarIndex;
            return index < 0 ? "No Lucky Dog awarded" : $"Lucky Dog - No. {simulation.Cars[index].DisplayCarNumber}";
        }
    }

    internal bool OneToGoBeforeGreen => stages?.OneToGo == true ||
        (!raceReleased && raceStart?.OneToGo == true);
    internal int RaceStartCountdown => raceStart?.SecondsUntilRelease ?? 0;
    internal SessionTrackState? TrackState => stages?.State ?? raceStart?.State ?? qualifying?.State ?? practice?.State;
    internal bool SessionComplete => stages != null ? RaceFieldParked() :
        raceStart != null || !TrackState.HasValue ? SessionRun.Complete : TrackState == SessionTrackState.Complete;
    internal bool HasSessionPitRoad => pitTraffic != null;
    private Vector3 cameraFocus;
    private float tiltAngle;
    private float yawAngle;
    private Transform followedCar;
    private Transform CameraSubject => followedCar != null ? followedCar : cars[0];
    internal int SelectedCarIndex => ViewAngle == CameraAngle.Leader
        ? (SessionRun?.IsRace == true ? SessionRun.RaceLeaderIndex() : 0)
        : followedCar == null ? 0 : Mathf.Max(0, System.Array.IndexOf(cars, followedCar));
    internal float SteeringWheelAngle =>
        (raceWheels[SelectedCarIndex]?.FrontSteeringAngle ?? 0f) * SelectedCarType.steeringWheelRatio;

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

    private void AllocateField(int count)
    {
        cars = new Transform[count];
        renderedCarPaint = new (Color HoodAndTrunk, Color RoofAndSides)[count];
        carHeadings = new RearAxleHeading[count];
        carTypes = new CarTypeDefinition[count];
        raceWheels = new RaceWheelAnimation[count];
        onboardCameraOffsets = new (Vector3, Vector3)[count];
        directedDraftLaneChanges = new bool[count];
        draftEligible = new bool[count];
        draftBumpEligible = new bool[count];
        draftManeuverEligible = new bool[count];
        draftPitApproach = new float[count];
        trailingSpeedBoosts = new float[count];
        draftBumpsGiven = new int[count];
        draftBumpsReceived = new int[count];
        baseSpeeds = new float[count];
        scoringPaint = new CarPaintStyle?[count];
        freeRunningSpeeds = new float[count];
        drivingDistances = new float[count];
        drivingWidths = new float[count];
        onTrack = new bool[count];
        drivingLanes = new float[count];
        targetLanes = new float[count];
        trafficLaneControl = new VehicleBehavior003.TrafficLaneControl[count];
        stabilizeTrafficLane = new bool[count];
        trafficPaceSpeeds = new float[count];
        trafficStartLaneSpeeds = new float[count];
        preferredLineAdvance = new float[count];
        passingLaneRanges = new Vector2[count];
        cornerApproaches = new bool[count];
        raceCarHalfWidths = new float[count];
        preferredInsideClearances = new float[count];
        backExitParticipants = new bool[count];
        pitParticipants = new bool[count];
        pitStart = new float[count];
        pitEnd = new float[count];
        pitLaneStart = new float[count];
        pitYawStart = new float[count];
        pitYawEnd = new float[count];
        pitWorldStart = new Vector3[count];
        pitWorldEnd = new Vector3[count];
        pitWorldYawStart = new float[count];
        pitWorldYawEnd = new float[count];
        pitWorldActive = new bool[count];
        pitWorldInvolved = new bool[count];
        pitLaneEnd = new float[count];
        practiceLeftStall = new bool[count];
        crashes = new CrashMotion[count];
        chasePoseBeforeYaw = new Pose?[count];
        collisionFlashes = new Transform[count];
        collisionFlashRemaining = new float[count];
        collisionParticipants = new bool[count];
        collisionEndDistances = new float[count];
        collisionStartLanes = new float[count];
        collisionTireStartLanes = new float[count];
        collisionTireEndLanes = new float[count];
        collisionTireOffsets = new float[count];
        tireYawAngles = new float[count];
        currentTrafficSpeeds = new float[count];
        trafficBrakingFactors = new float[count];
        stoppedCarTime = new float[count];
        pitObstructionTime = new float[count];
        pitObstruction = new bool[count];
        practiceStoppedCars = new PracticeStoppedCar[count];
        practiceCarAhead = new int[count];
        practiceGapAhead = new float[count];
        stepSpeeds = new float[count];
    }

    public void Initialize(TrackDefinition track, int playerCarNumber) => Initialize(track, playerCarNumber, null);

    internal void Initialize(TrackDefinition track, int playerCarNumber, string[] sponsorNames, TeamCar playerCar = null, bool night = false, IReadOnlyList<TeamCar> rivalCars = null)
    {
        surfaceMaterial = Resources.Load<Material>("Startup/TrackSurface");
        if (surfaceMaterial == null)
        {
            Debug.LogError("The Track surface material is missing.", this);
            enabled = false;
            return;
        }

        definition = track;
        AllocateField(track.SessionFieldSize(CarCount));
        SupportsCarDetail = true;
        CarDetailIndex = SupportsCarDetail ? Mathf.Clamp(PlayerPrefs.GetInt("StockCarManager.Settings.CarLOD", 3), 1, 5) - 1 : 0;
        bool bristolNight = night && track.id == BristolTrackId;
        simulationModel = CarSimulationModels.Default;
        simulation = new CarSimulationContext(track, null, null, System.Array.Empty<SessionCarResult>());
        lapLength = track.lengthMiles * TrackDefinition.MetersPerMile;
        float trackScale = lapLength / (60f + 38f * Mathf.PI);
        straightLength = track.OvalStraightLength;
        turnRadius = track.OvalTurnRadius;
        turnLength = Mathf.PI * turnRadius;
        roadHalfWidth = track.widthMeters * 0.5f;
        if (track.layoutPoints?.Length > 0)
            customLayout = new TrackLayout(track.layoutPoints, lapLength, track.widthMeters,
                track.bankingTransitions, track.smoothLayoutBanking);
        orbitOffset = new Vector3(0f, 62f, -39f) * Mathf.Max(1f, trackScale);
        wideViewSize = Mathf.Max(StartingViewSize, StartingViewSize * trackScale);

        // Keep the preview outside the startup camera's view.
        transform.position = new Vector3(0f, -1000f, 0f);
        Image = new RenderTexture(1920, 1080, 24) { name = "Track View" };
        SetTrackMsaaSamples(GameSettings.LoadTrackMsaaSamples());

        raceCamera = CreateObject("Track Camera", transform).AddComponent<Camera>();
        ApplyCameraPose();
        raceCamera.targetTexture = Image;
        raceCamera.allowMSAA = true;
        raceCamera.cullingMask = 1 << PreviewLayer;
        TrackSkyIsNight = night;
        var skyShader = Resources.Load<Shader>("Startup/TrackSky");
        if (skyShader != null && skyShader.isSupported)
        {
            trackSkyMaterial = new Material(skyShader) { name = "Track Sky" };
            trackSkyMaterial.SetFloat(SkyNight, night ? 1f : 0f);
            materials.Add(trackSkyMaterial);
        }
        ConfigureTrackSky(raceCamera);
        raceCamera.orthographic = true;
        raceCamera.orthographicSize = StartingViewSize;
        raceCamera.farClipPlane = Mathf.Max(600f, orbitOffset.magnitude * 2f);

        var sun = CreateObject("Track Light", transform).AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.transform.localRotation = Quaternion.Euler(55f, -25f, 0f);
        sun.cullingMask = 1 << PreviewLayer;
        sun.intensity = 1.1f;
        if (UsesDetailedPavement(track.id) || track.id == MichiganTrackId)
        {
            sun.transform.localRotation = Quaternion.Euler(41f, -35f, 0f);
            sun.shadowStrength = 0.72f;
            sun.GetUniversalAdditionalLightData().renderingLayers = BristolLightingLayer;
            bristolSun = sun;
            EnableBristolLighting();
        }

        var grass = MakeMaterial(new Color32(69, 108, 72, 255));
        var roadColor = track.surface == "Dirt" ? new Color32(145, 92, 58, 255)
            : track.surface == "Concrete" ? new Color32(139, 145, 148, 255)
            : track.id == OrangeCountyTrackId ? new Color32(105, 109, 111, 255)
            : new Color32(52, 57, 65, 255);
        var road = MakeMaterial(track.id == BristolTrackId ? new Color32(119, 117, 112, 255) : roadColor);
        if (UsesDetailedPavement(track.id))
        {
            road.SetFloat("_Smoothness", 0.12f);
            road.SetTexture("_BaseMap", BristolPavementTexture(track.id == BristolTrackId));
            grass.SetFloat("_Smoothness", 0.08f);
        }
        Material racingSurface = road;
        if (track.id == BristolTrackId && track.surface == "Concrete")
        {
            racingSurface = MakeMaterial(new Color32(176, 173, 165, 255));
            racingSurface.SetFloat("_Smoothness", .12f);
            racingSurface.SetTexture("_BaseMap", BristolPavementTexture(true));
        }

        var curb = MakeMaterial(new Color32(219, 221, 212, 255));
        var rubber = MakeMaterial(new Color32(24, 25, 30, 255));
        var glass = MakeMaterial(new Color32(43, 66, 87, 255));

        float groundSpan = Mathf.Max(2048f, raceCamera.farClipPlane * 3.5f);
        groundHeight = Mathf.Min(0f, customLayout?.MinimumElevation ?? 0f);
        grassGround = Box("Grass", transform, new Vector3(0f, groundHeight - 0.45f, 0f),
            new Vector3(groundSpan, 0.8f, groundSpan), grass).transform;
        ApplyCameraPose();
        importedTrackGeometry = customLayout != null && (track.id switch
        {
            MichiganTrackId => AddImportedTrackGeometry("MichiganSpeedway"),
            KansasTrackId => AddImportedTrackGeometry("KansasSpeedway"),
            MartinsvilleTrackId => AddImportedTrackGeometry("MartinsvilleSpeedway"),
            CotaTrackId => AddImportedTrackGeometry("CircuitOfTheAmericas"),
            IowaTrackId => AddImportedTrackGeometry("IowaSpeedway"),
            PhoenixTrackId => AddImportedTrackGeometry("PhoenixRaceway"),
            RichmondTrackId => AddImportedTrackGeometry("RichmondRaceway"),
            NorthWilkesboroTrackId => AddImportedTrackGeometry("NorthWilkesboroSpeedway"),
            _ => false
        });
        if (!importedTrackGeometry || track.id == KansasTrackId)
            Ring("Outer embankment", roadHalfWidth, roadHalfWidth + 3f, 0f, grass, outerEdgeAtGround: true);
        if (!importedTrackGeometry)
            Ring("Inner embankment", -roadHalfWidth - 3f, -roadHalfWidth, 0f, grass, innerEdgeAtGround: true);
        if (track.id == BristolTrackId && customLayout != null)
            AddBristolFloodlights();
        if (bristolSun != null)
            SetDetailedTrackLighting(bristolNight);

        BuildRacingLine();
        float finishDistance = track.StartFinishFraction * lapLength;
        if (!importedTrackGeometry)
        {
            Ring("Road surface", -roadHalfWidth, roadHalfWidth, 0f, racingSurface);
            if (UsesBottomLanePitMerge(track.id) && customLayout != null)
                AddBankedOutsideWall();
            Ring("Inner edge", -roadHalfWidth, -roadHalfWidth + 0.24f, 0.03f, curb);
            Ring("Outer edge", roadHalfWidth - 0.24f, roadHalfWidth, 0.03f, curb);
            if (customLayout != null && System.Array.Exists(track.layoutPoints, point => point.apronWidthMeters > 0f))
                Ring("Flat apron", 0f, 0f, 0f, road, apron: true);
            Vector3 stripeCenter = TrackPoint(finishDistance, 0f, out var stripeForward, out var stripeUp);
            var stripe = Box("Start and finish", transform, stripeCenter + stripeUp * 0.025f,
                new Vector3(RoadWidths(finishDistance).x, 0.03f, 0.45f), curb);
            stripe.transform.localRotation = Quaternion.LookRotation(stripeForward, stripeUp);
            if (track.id == OrangeCountyTrackId && track.raceMarkers?.hasRestartZone == true)
            {
                foreach (var marker in new[] { ("Restart zone Start", track.raceMarkers.restartZoneStart),
                    ("Restart zone End", track.raceMarkers.restartZoneEnd) })
                {
                    float station = marker.Item2 * lapLength;
                    float lane = -(customLayout?.InsideSign ?? -1f) * (RoadWidths(station).x * 0.5f - 1.35f);
                    var point = TrackPoint(station, lane, out var forward, out var up);
                    var paint = Box(marker.Item1, transform, point + up * 0.025f,
                        new Vector3(2.1f, 0.03f, 0.35f), curb);
                    paint.transform.localRotation = Quaternion.LookRotation(forward, up);
                }
            }
        }
        AddDashedLaneMarkings(finishDistance, curb);
        if (track.pitRoad?.enabled == true && customLayout != null)
            DrawPitRoad(track.pitRoad, road, curb);
        AddTrackScenery(track.id);
        AddSpecialtyGarageSite();
        if (track.id == BristolTrackId)
            AddBristolParkingLines();



        var hoodPaints = new Material[Liveries.Length];
        var bodyPaints = new Material[Liveries.Length];
        for (int i = 0; i < Liveries.Length; i++)
        {
            hoodPaints[i] = MakeCarPaintMaterial(Liveries[i].HoodAndTrunk);
            bodyPaints[i] = MakeCarPaintMaterial(Liveries[i].RoofAndSides);
        }

        int playerLivery = Mathf.Clamp(playerCarNumber - 27, 0, cars.Length - 1);
        int[] gridNumbers = GridNumbers(playerCarNumber, rivalCars);
        for (int i = 0; i < cars.Length; i++)
        {
            cars[i] = CreateObject(i == 0 ? $"No. {playerCarNumber}" : $"Rival {i}", transform).transform;
            int livery = i == 0 ? playerLivery : i == playerLivery ? 0 : i;
            int paintIndex = livery % Liveries.Length;
            int paintGroup = livery / Liveries.Length;
            var hoodAndTrunkPaint = hoodPaints[paintIndex];
            var bodyPaint = bodyPaints[(paintIndex + paintGroup) % Liveries.Length];
            CarPaintStyle? playerPaint = null;
            var liveryCar = i == 0 ? playerCar : rivalCars?.FirstOrDefault(car => car.number == i);
            string modelResource = liveryCar?.modelResource ?? playerCar?.modelResource;
            var carType = CarTypeDefinition.ForModel(modelResource);
            carTypes[i] = carType;
            if (liveryCar != null)
            {
                var paint = liveryCar.PaintStyle;
                playerPaint = paint;
                hoodAndTrunkPaint = MakeCarPaintMaterial(paint.Primary);
                bodyPaint = MakeCarPaintMaterial(paint.Secondary);
            }
            renderedCarPaint[i] = (hoodAndTrunkPaint.color, bodyPaint.color);
            scoringPaint[i] = playerPaint;
            string numberText = gridNumbers[i] > 99 ? liveryCar?.rivalRaceNumberText : null;
            InitializeCarVisualLods(i, modelResource, gridNumbers[i], numberText, hoodAndTrunkPaint, bodyPaint,
                glass, rubber, i == 0 ? sponsorNames : null, playerPaint);
        }
        carGlass = glass;
        for (int i = 0; i < cars.Length; i++)
            ConfigureOnboardCamera(i);
        if (track.id == IowaTrackId && importedTrackGeometry && pitRoute != null)
        {
            AddIowaScoringPylon();
            BuildIowaFlagArmRig(AddIowaFlagStandScenery());
            AddIowaSafetyLightFixtures();
            AddIowaFanScenery();
            BuildIowaFans();
            AddIowaNightLightFixtures(RegisterIowaVenueLight);
            ApplyIowaNightLighting(TrackSkyIsNight);
        }

        if (track.id == PhoenixTrackId && importedTrackGeometry && pitRoute != null)
            AddPhoenixVenue();
        if (track.id == RichmondTrackId && importedTrackGeometry && pitRoute != null)
            AddRichmondVenue();
        if (track.id == NorthWilkesboroTrackId && importedTrackGeometry && pitRoute != null)
            AddNorthWilkesboroVenue();
        if (pitRoute != null)
        {
            AddSharedTrackResponse();
            AddSharedPitPromenade();
            AddSharedInfieldWallFans();
        }
        AddOutsideBillboards();
        if (bristolSun != null)
            AssignTrackLightingLayers();
        BuildPreferredLineOverlay();
        SetBaseSpeeds();
        PositionCars();
        ResetView();
    }

    internal void SetGrandstandsVisible(bool visible)
    {
        if (grandstandScenery != null)
            grandstandScenery.SetActive(visible);
        foreach (var renderer in importedGrandstandRenderers)
            renderer.forceRenderingOff = !visible;
        if (bristolCrowd != null)
            bristolCrowd.SetActive(visible);
    }

    internal bool SetCarDetail(int index)
    {
        if (!SupportsCarDetail || index < 0 || index >= CarDetailLabels.Length)
            return false;
        if (index == CarDetailIndex)
            return true;
        for (int i = 0; i < cars.Length; i++)
        {
            var state = carVisualLods[i];
            string resource = state.Resources[index];
            if (resource == state.Current.Resource || loadedCarLodResources.Contains(resource))
                continue;
            if (Resources.Load<GameObject>("Startup/" + resource) == null)
                return false;
            loadedCarLodResources.Add(resource);
        }

        CarDetailIndex = index;
        ResetCarLodBudget();
        int maximum = EffectiveMaximumCarLod;
        for (int i = 0; i < cars.Length; i++)
            if (carVisualLods[i].Level > maximum || UsesMaximumCarLod(i))
                ApplyCarVisualLod(i, maximum);
        ApplyCameraPose();
        return true;
    }

    public void Zoom(float wheelDelta, Vector2 anchor)
    {
        float scale = Mathf.Exp(wheelDelta * MouseWheelZoomRate);
        if (ViewAngle == CameraAngle.Spotter)
        {
            ZoomSpotter(scale, new Vector2(anchor.x * raceCamera.aspect, anchor.y));
            return;
        }

        float previousSize = raceCamera.orthographicSize;
        float size = Mathf.Clamp(previousSize * scale, CloseViewSize, wideViewSize);
        float aspect = (float)Image.width / Image.height;
        var cameraTransform = raceCamera.transform;
        Vector3 zoomDirection = cameraTransform.right * (anchor.x * aspect) + cameraTransform.up * anchor.y;
        cameraFocus += zoomDirection * (previousSize - size);

        raceCamera.orthographicSize = size;
        ApplyCameraPose();
    }

    private void ZoomSpotter(float scale, Vector2 anchor)
    {
        float previousTangent = Mathf.Tan(spotterZoomFieldOfView * Mathf.Deg2Rad * .5f);
        float tangent = Mathf.Clamp(previousTangent * scale,
            Mathf.Tan(ClosestSpotterFieldOfView * Mathf.Deg2Rad * .5f),
            Mathf.Tan(spotterFieldOfView * Mathf.Deg2Rad * .5f));
        if (Mathf.Approximately(tangent, previousTangent))
            return;

        // Keep the world ray under the mouse fixed while the lens zooms.
        var previousRay = new Vector3(anchor.x * previousTangent, anchor.y * previousTangent, 1f);
        var nextRay = new Vector3(anchor.x * tangent, anchor.y * tangent, 1f);
        spotterAimOffset *= Quaternion.FromToRotation(nextRay, previousRay);
        spotterZoomFieldOfView = 2f * Mathf.Atan(tangent) * Mathf.Rad2Deg;
        raceCamera.fieldOfView = spotterZoomFieldOfView;
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

    internal void GetMapSurface(out Vector2[] inside, out Vector2[] outside, out Vector2[] apron)
    {
        const int samples = 320;
        inside = new Vector2[samples];
        outside = new Vector2[samples];
        apron = new Vector2[samples];
        float side = customLayout?.InsideSign ?? -1f;
        for (int i = 0; i < samples; i++)
        {
            float distance = lapLength * i / samples;
            var widths = RoadWidths(distance);
            float insideLane = side * widths.x * 0.5f;
            inside[i] = MapPoint(TrackPoint(distance, insideLane, out _, out _));
            outside[i] = MapPoint(TrackPoint(distance, -insideLane, out _, out _));
            apron[i] = MapPoint(TrackPoint(distance, insideLane + side * widths.y, out _, out _));
        }
    }

    internal int MapCarCount => cars.Length;

    internal bool TryGetMapCar(int index, out Vector2 position, out string number)
    {
        position = default;
        number = default;
        var car = cars[index];
        if (car == null || !car.gameObject.activeSelf || index >= simulation.Cars.Count)
            return false;

        position = MapPoint(car.localPosition);
        number = simulation.Cars[index].DisplayCarNumber;
        return true;
    }

    internal bool TryGetMapPaceCar(out Vector2 position, out bool lightsOn)
    {
        position = default;
        lightsOn = false;
        if (raceStart == null || paceCar == null || !paceCar.gameObject.activeInHierarchy)
            return false;

        if (richmondPaceReturn != null)
        {
            if (RichmondPaceIsParked) return false;
        }
        else
        {
            bool stagePace = stages?.HasPaceCar == true;
            var pace = stagePace ? stages.PacePosition() : raceStart.PacePosition();
            bool returning = stagePace ? stages.PaceReturning : raceStart.PaceReturning;
            if (raceStart.State == SessionTrackState.TrackClosed || returning && pace.OnPitRoad && pace.ParkingBlend >= 1f)
                return false;
        }

        position = MapPoint(paceCar.localPosition);
        lightsOn = paceLights != null && paceLights.activeInHierarchy;
        return true;
    }

    private static Vector2 MapPoint(Vector3 position) => new Vector2(position.x, -position.z);

    internal bool? TimingCarPitStatus(int index)
    {
        if (index < 0 || index >= cars.Length)
            return null;

        var traffic = isQualifying ? (index == 0 ? qualifying?.Car : null) : trafficCars?[index];
        if (traffic?.InPit == true)
            return true;

        return cars[index] != null && cars[index].gameObject.activeSelf ? (bool?)false : null;
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
        KeepOnboardCarLod();
        UpdateCockpitGlass();
        ApplyCameraPose();
    }

    private void ConfigureOnboardCamera(int index)
    {
        var car = cars[index];
        var anchors = carVisualLods[index].Current.Root.GetComponentsInChildren<Transform>(true);
        var hood = System.Array.Find(anchors, child => child.name == "HoodSponsor");
        Vector3 hoodOffset = hood == null ? new Vector3(0f, 0.8f, 0.975f) :
            car.InverseTransformPoint(hood.position) + Vector3.up * 0.12f;
        onboardCameraOffsets[index] = (hoodOffset, carTypes[index].inCarCamera.Resolve(car, anchors));
    }

    private void UpdateCockpitGlass()
    {
        var subject = ViewAngle == CameraAngle.InCar ? CameraSubject : null;
        if (subject == cockpitGlassCar)
            return;

        foreach (var renderer in hiddenCockpitGlass)
            if (renderer != null)
                renderer.forceRenderingOff = false;
        hiddenCockpitGlass.Clear();
        cockpitGlassCar = subject;
        if (subject == null)
            return;

        var visuals = carVisualLods[SelectedCarIndex].Current.Root;
        foreach (var renderer in visuals.GetComponentsInChildren<MeshRenderer>(true))
        {
            var materials = renderer.sharedMaterials;
            if (materials.Length == 0 || !System.Array.TrueForAll(materials, material =>
                    material == carGlass || material != null &&
                    (material.name is "RaceTruck_Windows" or "RaceTruck_RearSideWindows" or "LateModel_Glass" or "FordMustang_Glass" or "ChevroletCamaro_Glass" or "ToyotaCamry_Glass")))
                continue;

            renderer.forceRenderingOff = true;
            hiddenCockpitGlass.Add(renderer);
        }
    }

    internal void SetTrackMsaaSamples(int requestedSamples)
    {
        if (Image == null)
            return;

        var descriptor = Image.descriptor;
        descriptor.msaaSamples = requestedSamples;
        int samples = Mathf.Max(1, SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor));
        if (Image.antiAliasing == samples)
            return;

        // Keep the render texture object bound to the track viewport.
        bool wasCreated = Image.IsCreated();
        if (wasCreated)
            Image.Release();
        Image.antiAliasing = samples;
        if (wasCreated)
            Image.Create();
    }

    internal void SetCameraAngle(CameraAngle angle)
    {
        bool available = System.Enum.IsDefined(typeof(CameraAngle), angle) && angle != CameraAngle.Cycle &&
            (angle != CameraAngle.PitBox || CanUsePitBoxCamera);
        ViewAngle = available ? angle : CameraAngle.Player;
        if (tvCameraPositions == null && ViewAngle is CameraAngle.TV or CameraAngle.Leader or CameraAngle.Spotter)
            ConfigureTracksideCameras();
        activeTvCamera = -1;
        raceCamera.orthographic = ViewAngle == CameraAngle.Player;
        raceCamera.nearClipPlane = ViewAngle is CameraAngle.Hood or CameraAngle.InCar ? 0.035f : 0.3f;
        raceCamera.fieldOfView = ViewAngle switch
        {
            CameraAngle.Hood => 62f,
            CameraAngle.InCar => 72f,
            CameraAngle.TV or CameraAngle.Leader => 33f,
            CameraAngle.Spotter => spotterZoomFieldOfView,
            CameraAngle.PitBox => 42f,
            _ => 55f
        };
        KeepOnboardCarLod();
        UpdateCockpitGlass();
        UpdateRearViewRendering();
        ApplyCameraPose();
    }

    public void ResetView()
    {
        if (raceCamera == null)
            return;

        if (ViewAngle == CameraAngle.Spotter)
        {
            spotterZoomFieldOfView = spotterFieldOfView;
            spotterAimOffset = Quaternion.identity;
            raceCamera.fieldOfView = spotterZoomFieldOfView;
        }
        if (ViewAngle != CameraAngle.Player)
        {
            ApplyCameraPose();
            return;
        }

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
        UpdateCockpitGlass();
    }

    private Vector3 CarFocus()
    {
        return followedCar.localPosition + Vector3.up * 0.53f;
    }

    private void ApplyCameraPose()
    {
        var cameraTransform = raceCamera.transform;
        int leaderIndex = ViewAngle == CameraAngle.Leader ? SelectedCarIndex : 0;
        var subject = ViewAngle == CameraAngle.Leader ? cars[leaderIndex] : CameraSubject;
        if (subject != null && ViewAngle is CameraAngle.Hood or CameraAngle.InCar)
        {
            var offsets = onboardCameraOffsets[SelectedCarIndex];
            Vector3 offset = ViewAngle == CameraAngle.Hood ? offsets.Hood : offsets.DriverEye;
            cameraTransform.localPosition = subject.localPosition + subject.localRotation * offset;
            cameraTransform.localRotation = subject.localRotation;
        }
        else if (subject != null && ViewAngle is CameraAngle.Chase or CameraAngle.RearChase)
        {
            Pose chasePose = chasePoseBeforeYaw[SelectedCarIndex] ?? new Pose(subject.localPosition, subject.localRotation);
            Vector3 viewDirection = chasePose.rotation * Vector3.forward;
            viewDirection.y = 0f;
            viewDirection.Normalize();
            if (ViewAngle == CameraAngle.RearChase)
                viewDirection = -viewDirection;
            Vector3 position = chasePose.position - viewDirection * 5.5f + Vector3.up * 2.65f;
            Vector3 lookAt = chasePose.position + viewDirection * 6f + Vector3.up * 0.65f;
            cameraTransform.localPosition = position;
            cameraTransform.localRotation = Quaternion.LookRotation(lookAt - position);
        }
        else if (subject != null && ViewAngle is CameraAngle.TV or CameraAngle.Leader or CameraAngle.Spotter or CameraAngle.PitBox)
        {
            Vector3 position = ViewAngle switch
            {
                CameraAngle.TV => TvCameraPosition(SelectedCarIndex),
                CameraAngle.Leader => TvCameraPosition(leaderIndex),
                CameraAngle.Spotter => spotterPosition,
                _ => playerPitBoxCameraPosition ?? (practiceStallPositions[0] + Vector3.up * 3.2f)
            };
            Vector3 target = subject.localPosition + Vector3.up * 0.55f;
            cameraTransform.localPosition = position;
            cameraTransform.localRotation = Quaternion.LookRotation(target - position);
            if (ViewAngle is CameraAngle.TV or CameraAngle.Leader)
                raceCamera.fieldOfView = TvFieldOfView(Vector3.Distance(position, target));
            else if (ViewAngle == CameraAngle.Spotter)
                cameraTransform.localRotation *= spotterAimOffset;
        }
        else
        {
            Vector3 focus = cameraFocus + (followedCar != null ? CarFocus() : Vector3.zero);
            Quaternion orbit = Quaternion.Euler(tiltAngle, yawAngle, 0f);
            Vector3 offset = orbit * orbitOffset;
            cameraTransform.localPosition = focus + offset;
            cameraTransform.localRotation = Quaternion.LookRotation(-offset);
        }

        if (rearViewCamera != null && rearViewCamera.enabled && subject != null)
        {
            // Looking from just past the rear bumper keeps the followed car out of the mirror.
            var mirror = rearViewCamera.transform;
            mirror.localPosition = subject.localPosition + subject.localRotation * SelectedCarType.rearviewCamera;
            mirror.localRotation = subject.localRotation * Quaternion.Euler(0f, 180f, 0f);
        }

        // Keep the grass perimeter outside the camera frustum as the focus moves.
        if (grassGround != null)
        {
            Vector3 cameraPosition = cameraTransform.localPosition;
            grassGround.localPosition = new Vector3(cameraPosition.x, grassGround.localPosition.y, cameraPosition.z);
        }
    }

    private void ConfigureTracksideCameras()
    {
        float finish = definition.StartFinishFraction * lapLength;
        float insideSign = customLayout?.InsideSign ?? -1f;
        ConfigureTvCameras(finish, insideSign);

        const int samples = 256;
        var centerline = new Vector3[samples];
        Vector3 finishPoint = TrackPoint(finish, 0f, out var finishForward, out _);
        Vector3 outside = Vector3.Cross(Vector3.up, finishForward).normalized * -insideSign;
        var bounds = new Bounds(finishPoint, Vector3.zero);
        float outsideExtent = 0f;
        for (int i = 0; i < centerline.Length; i++)
        {
            Vector3 point = TrackPoint(lapLength * i / centerline.Length, 0f, out _, out _);
            centerline[i] = point;
            bounds.Encapsulate(point);
            outsideExtent = Mathf.Max(outsideExtent, Vector3.Dot(point - finishPoint, outside));
        }

        if (!TryGetGrandstandCamera(finishPoint, outside, out spotterPosition))
        {
            float setback = outsideExtent + RoadWidths(finish).x * 0.5f + Mathf.Clamp(lapLength * 0.03f, 22f, 90f);
            float height = Mathf.Clamp(lapLength * 0.05f, 24f, 105f);
            spotterPosition = finishPoint + outside * setback + Vector3.up * height;
        }

        Quaternion view = Quaternion.Inverse(Quaternion.LookRotation(bounds.center - spotterPosition));
        float aspect = (float)Image.width / Image.height;
        float maxTangent = 0f;
        foreach (var point in centerline)
        {
            Vector3 local = view * (point - spotterPosition);
            if (local.z > 0f)
                maxTangent = Mathf.Max(maxTangent, Mathf.Max(Mathf.Abs(local.y), Mathf.Abs(local.x) / aspect) / local.z);
        }
        spotterFieldOfView = Mathf.Clamp(2f * Mathf.Atan(maxTangent * 1.15f) * Mathf.Rad2Deg, 35f, 110f);
        spotterZoomFieldOfView = spotterFieldOfView;
    }

    private bool TryGetGrandstandCamera(Vector3 finish, Vector3 outside, out Vector3 position)
    {
        if (iowaSpotterPosition.HasValue)
        {
            position = iowaSpotterPosition.Value;
            return true;
        }
        position = default;
        float bestScore = float.PositiveInfinity;
        Vector3 worldFinish = transform.TransformPoint(finish);
        foreach (var renderer in GetComponentsInChildren<MeshRenderer>())
        {
            string name = renderer.name;
            if (name.IndexOf("grandstand", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                name.IndexOf("bleacher", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                name.IndexOf("seating section", System.StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            Bounds stand = renderer.bounds;
            Vector3 closest = stand.ClosestPoint(worldFinish);
            closest.y = stand.max.y;
            Vector3 top = transform.InverseTransformPoint(closest);
            Vector3 fromFinish = top - finish;
            float height = fromFinish.y;
            fromFinish.y = 0f;
            if (Vector3.Dot(fromFinish, outside) <= 0f || height < 3f)
                continue;

            // Favor a high spot with a short sightline to the start/finish straight.
            float score = fromFinish.magnitude - Mathf.Min(height, 45f) * 1.25f;
            if (score < bestScore)
            {
                bestScore = score;
                position = top + Vector3.up * 2.5f;
            }
        }
        return bestScore < float.PositiveInfinity;
    }

    internal void BeginSession(CarSimulationContext simulation, ICarSimulationModel model, IVehicleSpeedModel speedModel,
        IVehicleBehaviorModel behaviorModel, int[] qualifyingOrder = null, bool usePaceTruck = false, bool fullBristolStands = false)
    {
        ResetIowaFlagStandTiming();
        ResetIowaWreckResponse();
        this.simulation = simulation;
        simulation.ResetLiveTrackConditions();
        if (definition.id == BristolTrackId)
            SetBristolAttendance(fullBristolStands ? RaceSessionKind.Race : simulation.Session.kind);
        if (definition.id == BristolTrackId && simulation.Event != null && parkedHaulers == null)
            ParkBristolHaulers(simulation);
        PaintVenueHaulers(simulation);
        if (simulation.Event != null && teamPitBoxes == null && practiceStallPositions != null)
            AddTeamPitBoxes(simulation);
        simulationModel = model;
        this.speedModel = speedModel;
        lapSpeedProfile = speedModel.UsesLapSpeedProfile ? new LapSpeedProfile(definition, speedModel is VehicleSpeed003) : null;
        if (this.behaviorModel != behaviorModel)
        {
            this.behaviorModel = behaviorModel;
            BuildRacingLine();
            BuildPreferredLineOverlay();
        }
        var session = simulation.Session;
        TrackGrandstandScenery.SetAttendance(grandstandScenery, session.kind, MakeMaterial, roadMeshes, carPaintTextures);
        drivingLanesReady = false;
        System.Array.Clear(trafficLaneControl, 0, trafficLaneControl.Length);
        draft = cars.Length > 1 && session.kind is RaceSessionKind.Race or RaceSessionKind.Practice
            ? new TrackDraft(cars.Length) : null;
        draftPassing = draft != null && definition.restrictorPlate && session.kind == RaceSessionKind.Race
            ? new TrackDraftPass(cars.Length) : null;
        draftMerging = draft != null && definition.restrictorPlate ? new TrackDraftLineMerge(cars.Length)
        {
            Slingshots = draftPassing,
            MergeCommitted = RecordPlateMergeAttempt
        } : null;
        if (draftPassing != null)
            draftPassing.LineMerge = draftMerging;
        draftLeaderBlock = draftPassing != null ? new TrackDraftLeaderBlock() : null;
        System.Array.Clear(trailingSpeedBoosts, 0, trailingSpeedBoosts.Length);
        System.Array.Clear(draftBumpsGiven, 0, draftBumpsGiven.Length);
        System.Array.Clear(draftBumpsReceived, 0, draftBumpsReceived.Length);
        simulation.Session.garageRuns = new System.Collections.Generic.List<GarageRunReview>();
        simulation.Session.rivalSetupChanges = new System.Collections.Generic.List<RivalSetupChange>();
        simulation.Session.rivalFinalSetupReviewed = false;
        simulation.Session.rivalPracticeTelemetry = new System.Collections.Generic.List<RivalPracticeTelemetry>();
        rivalRunRecorders = null;
        if (session.kind == RaceSessionKind.Practice && pitTraffic != null)
        {
            rivalRunRecorders = new GarageRunRecorder[cars.Length];
            for (int i = 1; i < cars.Length; i++)
            {
                int index = i;
                var telemetry = new RivalPracticeTelemetry { carIndex = i };
                session.rivalPracticeTelemetry.Add(telemetry);
                rivalRunRecorders[i] = new GarageRunRecorder(telemetry.runs, lapLength,
                    definition.StartFinishFraction * lapLength, () => CaptureGarageRunDiagnostics(index));
            }
        }
        playerRunStartLap = 0;
        playerWasParked = true;
        rivalEngineers = null;
        if (simulation.Session.kind == RaceSessionKind.Practice && speedModel is VehicleSpeed003 &&
            !simulation.EqualConditionTesting && simulation.Event?.SetupLocked != true && pitTraffic != null)
        {
            rivalEngineers = new RivalPracticeEngineer[cars.Length];
            for (int i = 1; i < cars.Length; i++) rivalEngineers[i] = new RivalPracticeEngineer();
        }
        garageRunRecorder = new GarageRunRecorder(simulation.Session.garageRuns, lapLength,
            definition.StartFinishFraction * lapLength, () => CaptureGarageRunDiagnostics(), CapturePlayerRunTimeSample);
        requestedGarageLaps = 0;
        snapshot = new TrackSnapshot();
        ovalPassingStrictness = DefaultOvalPassing;
        mergeTrackGeometry.Clear();
        mergeExitGeometry.Clear();
        apronConnectors.Clear();
        snapshotTime = 0;
        collisions = new VehicleCollision001(cars.Length, UsesOvalTraffic007 ? raceCarHalfWidths : null);
        pitCollisions = new VehicleCollision001(cars.Length);
        if (bristolBackExit != null)
            backExitCollisions = new VehicleCollision001(cars.Length);
        System.Array.Clear(crashes, 0, crashes.Length);
        System.Array.Clear(stoppedCarTime, 0, stoppedCarTime.Length);
        System.Array.Clear(pitObstructionTime, 0, pitObstructionTime.Length);
        nextPitClearAt = 0d;
        System.Array.Clear(practiceStoppedCars, 0, practiceStoppedCars.Length);
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
        practiceCautionReason = null;
        qualifying = null;
        raceStart = null;
        stages = null;
        pitStops = null;
        playerGarageVisit = false;
        haulerGaragePhase = HaulerGaragePhase.OnPitRoute;
        lastHandlingSampleTime = 0d;
        raceReleased = false;
        if (paceCar != null)
            Destroy(paceCar.gameObject);
        paceWheels = null;
        trackClosedSince = null;
        pausedAt = null;
        if (model is TrackState001 trackStateModel && session.kind == RaceSessionKind.Practice && pitTraffic != null)
        {
            practice = trackStateModel.StartPractice(pitTraffic, trafficCars, practiceStallDistances);
            for (int i = 0; i < cars.Length; i++)
                sessionDistances[i] = pitTraffic.RaceDistance(trafficCars[i]);
        }

        if (model is TrackState001 qualifyingModel && isQualifying && pitTraffic != null)
            qualifying = qualifyingModel.StartQualifying(pitTraffic, practiceStallDistances[0]);

        if (model is TrackState001 raceModel && session.kind == RaceSessionKind.Race && pitTraffic != null)
        {
            raceStart = raceModel.StartRace(pitTraffic, slots, definition, simulationRandom);
            ConfigureFormationPitExit();
            ConfigurePacePitApproach();
            CreatePaceCar(usePaceTruck);
            ConfigureRichmondPaceBay();
            CreatePitSignal();
        }

        System.Array.Clear(carHeadings, 0, carHeadings.Length);
        foreach (var wheels in raceWheels)
            wheels?.Reset();
        pendingSessionTime = 0;
        SetSessionSpeed(1);
        ResetCarLodBudget();
        SessionRun = new RaceSessionRun(session, lapLength, startLine, simulation, speedModel, simulationRandom, initialDistances: sessionDistances);
        if (trackSkyMaterial != null)
        {
            float cloudSeed = Mathf.Repeat(session.weather.variationPhase, Mathf.PI * 2f) * 10000f;
            trackSkyMaterial.SetFloat(SkyCloudSeed, cloudSeed);
        }
        if (practiceSkyNightOverride.HasValue && SupportsRaceNightLighting)
            SetDetailedTrackLighting(SessionRun.Weather.IsNight);
        practiceSkyNightOverride = null;
        UpdateTrackSky();
        if (pitTraffic != null)
        {
            ConfigurePitField();
            pitStops = new SessionPitStops(pitTraffic, trafficCars, practiceStallDistances);
            ConfigureNorthWilkesboroHaulerGarage();
        }
        rubberSession = session;
        ResetSessionSkids();
        BuildRubberOverlay();
        UpdateRubberOverlay();
        if (bristolSun != null)
            AssignTrackLightingLayers();
        if (isQualifying)
            SessionRun.SimulateQualifyingRivals(CarSpeed);
        PositionCars();
        ResetView();
    }

    internal void BeginPreviewTiming(SessionCarResult[] entries)
    {
        rubberSession = null;
        ResetSessionSkids();
        UpdateRubberOverlay();
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
            "Velocity Racing", "Ironwood Motorsports", "Cedar Racing", "Crosswind Racing", "Stonebridge Motorsports",
            "Highline Racing", "Copper Creek Racing", "Oak Valley Motorsports", "Ridgeline Racing" });

    internal const int RivalCarsPerTeam = 3;

    internal static int[] GridNumbers(int playerNumber, IReadOnlyList<TeamCar> rivals)
    {
        var numbers = new int[CarCount];
        numbers[0] = playerNumber;
        var used = new HashSet<int> { playerNumber };
        // Claim the saved numbers first so a fallback cannot displace another rival's number.
        foreach (var rival in rivals ?? System.Array.Empty<TeamCar>())
        {
            if (rival.number <= 0 || rival.number >= numbers.Length || numbers[rival.number] != 0)
                continue;
            // Numbers outside the legacy 1-99 format use a stable timing key per roster slot.
            if (rival.HasCustomRivalTimingKey)
                numbers[rival.number] = 100 + rival.number;
            else if (rival.rivalRaceNumber >= 1 && rival.rivalRaceNumber <= 99 && used.Add(rival.rivalRaceNumber))
                numbers[rival.number] = rival.rivalRaceNumber;
        }
        for (int slot = 1; slot < numbers.Length; slot++)
        {
            if (numbers[slot] != 0) continue;
            int fallback = slot < playerNumber ? slot : slot + 1;
            numbers[slot] = used.Contains(fallback)
                ? Enumerable.Range(1, 99).First(number => !used.Contains(number))
                : fallback;
            used.Add(numbers[slot]);
        }
        return numbers;
    }

    private static readonly string[] RivalDrivers =
    {
        "Alex Morgan", "Blake Turner", "Cameron Hayes", "Casey Brooks", "Drew Bennett", "Elliot Reed",
        "Emerson Grant", "Frankie Brooks", "Harper Lewis", "Jamie Cole", "Jordan Blake", "Kai Foster",
        "Lane Mitchell", "Logan Shaw", "Morgan Ellis", "Parker Nash", "Quinn Carter", "Reese Walker",
        "Robin Scott", "Rowan Miller", "Sage Parker", "Taylor West", "Toby Rivers", "Avery Hale",
        "Bailey Stone", "Carter Wynn", "Dakota Bishop", "Eden Rhodes", "Finley Dawson", "Gray Monroe",
        "Hayden Banks", "Indigo Pierce", "Jules Sutton", "Kendall Moore", "Lennon Price", "Marley Page",
        "Nico Bennett", "Oakley Flynn", "Payton Dean"
    };

    internal static string RivalTeamForSlot(int slot) => RivalTeams[(slot - 1) / RivalCarsPerTeam];
    internal static string RivalNameForSlot(int slot) => RivalDrivers[slot - 1];

    internal static SessionCarResult[] CreateTimingEntries(TeamCar playerCar, Career career)
    {
        var entries = new SessionCarResult[CarCount];
        var numbers = GridNumbers(playerCar.number, career.rivalCars);
        for (int i = 0; i < entries.Length; i++)
        {
            var car = i == 0 ? playerCar : career.rivalCars.Find(item => item.number == i);
            string teamName = i == 0 ? career.teamName : car?.rivalTeamName ?? RivalTeamForSlot(i);
            var driver = i == 0 ? career.Driver : career.RivalDriverForSlot(i);
            var supplier = i == 0 ? career.EngineSupplierFor(car) :
                car != null && !string.IsNullOrEmpty(career.seriesId)
                    ? career.SeriesRules.SuppliersFor(car.manufacturer).FirstOrDefault() : null;
            entries[i] = new SessionCarResult
            {
                car = car,
                enginePowerRating = supplier?.power,
                engineReliabilityRating = supplier?.reliability,
                carNumber = numbers[i],
                carNumberText = numbers[i] > 99 ? car?.rivalRaceNumberText : null,
                driver = driver,
                driverName = driver?.FullName ?? (i == 0 ? "Unassigned driver" : RivalNameForSlot(i)),
                teamName = teamName,
                teamPaint = i == 0 ? (career.TeamColors.Secondary, career.TeamColors.Primary) : null,
                ownerEntryId = i == 0 ? "player" : "rival:" + i,
                rivalDriverId = i == 0 ? null : car?.rivalDriverId,
                playerEntry = i == 0,
                manufacturerRecorded = true,
                manufacturer = car?.manufacturer ?? ManufacturerCatalog.Chevrolet
            };
        }
        return entries;
    }

    internal bool EqualConditionTesting => simulation?.EqualConditionTesting == true;
    internal void SetPlayerSetup(Setup setup) { if (simulation.Event?.SetupLocked != true) simulation.PlayerSetup = setup; }

    internal void SetPracticeSkyNight(bool night)
    {
        practiceSkyNightOverride = night;
        UpdateTrackSky();
        if (SupportsRaceNightLighting)
            SetDetailedTrackLighting(night);
    }

    private Color TrackSkyColor => TrackSkyIsNight
        ? new Color32(12, 19, 36, 255)
        : new Color32(125, 174, 219, 255);

    private void ConfigureTrackSky(Camera camera)
    {
        camera.backgroundColor = TrackSkyColor;
        if (trackSkyMaterial == null)
        {
            camera.clearFlags = CameraClearFlags.SolidColor;
            return;
        }

        camera.gameObject.AddComponent<Skybox>().material = trackSkyMaterial;
        camera.clearFlags = CameraClearFlags.Skybox;
    }

    private void UpdateTrackSky()
    {
        bool? night = practiceSkyNightOverride ?? SessionRun?.Weather?.IsNight;
        if (!night.HasValue || TrackSkyIsNight == night.Value)
            return;

        TrackSkyIsNight = night.Value;
        if (SupportsIowaVenueLighting || SupportsIowaPitStallLighting)
            ApplyIowaNightLighting(TrackSkyIsNight);
        if (trackSkyMaterial != null)
            trackSkyMaterial.SetFloat(SkyNight, TrackSkyIsNight ? 1f : 0f);

        raceCamera.backgroundColor = TrackSkyColor;
        if (rearViewCamera != null)
            rearViewCamera.backgroundColor = TrackSkyColor;
    }

    private void UpdateRearViewRendering()
    {
        bool visible = raceCamera.enabled && ViewAngle == CameraAngle.InCar;
        if (visible && rearViewCamera == null)
        {
            RearViewImage = new RenderTexture(384, 100, 24) { name = "Rear-view Mirror" };
            rearViewCamera = CreateObject("Rear-view Camera", transform).AddComponent<Camera>();
            rearViewCamera.CopyFrom(raceCamera);
            ConfigureTrackSky(rearViewCamera);
            rearViewCamera.targetTexture = RearViewImage;
            rearViewCamera.depth = raceCamera.depth - 1f;
            rearViewCamera.aspect = (float)RearViewImage.width / RearViewImage.height;
            rearViewCamera.fieldOfView = 20f;
            rearViewCamera.nearClipPlane = 0.1f;
            rearViewCamera.allowHDR = false;
            rearViewCamera.allowMSAA = false;
        }
        if (rearViewCamera != null)
            rearViewCamera.enabled = visible;
    }

    internal void SetTrackRendering(bool visible)
    {
        raceCamera.enabled = visible;
        if (!visible)
            engineAudio?.Stop();
        UpdateRearViewRendering();
        if (visible)
            EnableBristolLighting();
        else
            DisableBristolLighting();
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

        ApplyCarLodPlaybackOverride(speed);
        sessionSpeed = speed;
    }

    private void LateUpdate()
    {
        UpdateAdaptiveCarLods();
        UpdateScoringPylon();
        RenderIowaFlagStand();
        UpdateIowaSafetyLights();
        UpdateResponseVehicleWarningLights();
        if (raceCamera == null || !raceCamera.enabled || SessionRun == null || sessionSpeed == 0)
        {
            engineAudio?.Stop();
            return;
        }

        engineAudio ??= new TrackEngineAudio(cars);
        engineAudio.Update(cars, SessionRun, raceCamera.transform);
    }

    private void Update()
    {
        double frameMs = Time.unscaledDeltaTime * 1000.0;
        timingFrames++;
        timingFrameTotal += frameMs;
        timingFrameMax = System.Math.Max(timingFrameMax, frameMs);
        UpdateCollisionFlashes();
        if (sessionSpeed == 0)
            return;
        AnimateIowaFans(Time.deltaTime);

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
                PositionCars();
                SampleSessionSkids();
                pendingSessionTime -= SessionStep;
                snapshotTime += SessionStep;
                ObservePlayerRun();
                CaptureSnapshot();
            }
        }

        UpdateTrackSky();
        UpdateRubberOverlay();
        PresentSessionSkids();
        PositionCars();
        foreach (var wheels in raceWheels)
            wheels?.Present();
        paceWheels?.Present();
        if (ViewAngle != CameraAngle.Player || followedCar != null)
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

        pendingSessionTime = System.Math.Min(pendingSessionTime + (double)Time.deltaTime * sessionSpeed, 5d);
        var stepBudget = System.Diagnostics.Stopwatch.StartNew();
        while (pendingSessionTime >= SessionStep && !SessionComplete && stepBudget.Elapsed.TotalMilliseconds < 12d)
        {
            if (raceStart != null)
                AdvanceRaceStartStep();
            else if (qualifying != null)
                AdvanceQualifyingStep();
            else
                AdvancePracticeStep();
            PositionCars();
            SampleSessionSkids();
            pendingSessionTime -= SessionStep;
            snapshotTime += SessionStep;
            ObservePlayerRun();
            CaptureSnapshot();
        }
    }

    private void AdvanceQualifyingStep()
    {
        var car = qualifying.Car;
        if (crashes[0].Active && !SessionRun.Retired(0))
        {
            AdvanceCrash(0, SessionStep);
            SessionRun.AdvanceClock(SessionStep);
            return;
        }

        float before = car.TrackDistance;
        if (definition.id == PhoenixTrackId || UsesEarlyLowLanePitApproach)
            collisionStartLanes[0] = drivingLanes[0];
        float speed = SpeedAt(0, car.TrackDistance, out float preferredLineSpeedFactor);
        float limitedSpeed = LimitNorthWilkesboroApronSpeed(car, speed);
        qualifying.Advance(SessionRun, SessionStep, limitedSpeed,
            car.InPit ? 0f : lapSpeedProfile?.BrakingLimitAt(car.TrackDistance) ?? 0f,
            limitedSpeed < speed ? 1f : preferredLineSpeedFactor);

        if ((definition.id is NorthWilkesboroTrackId or PhoenixTrackId) || UsesEarlyLowLanePitApproach)
        {
            if (car.InPit)
                drivingLanesReady = false;
            else
            {
                float distance = car.TrackDistance;
                float target = TargetDrivingLane(0, distance, car);
                if (!drivingLanesReady)
                    drivingLanes[0] = target;
                else if (car.ApronExitActive && !car.ApronEntryActive)
                    AdvanceApronExitLane(0, car, distance, drivingLanes[0], target, SessionStep);
                else
                    drivingLanes[0] = Mathf.MoveTowards(drivingLanes[0] +
                        SweepingLaneAdvance(0, before, distance, target, car, targetAtEnd: true), target,
                        VehicleBehavior003.MaximumLateralSpeed * SessionStep);
                drivingLanesReady = true;
            }
        }

        StartTireWearFailure(0, car);
        AdvanceNorthWilkesboroGarageTravel(SessionStep);
    }

    private void AdvanceRaceStartStepCore()
    {
        float beforePacing = raceStart.FormationProgress;
        raceStart.Advance(SessionStep);
        float pacedMeters = raceStart.FormationProgress - beforePacing;
        if (pacedMeters > 0f)
        {
            bool releasingField = raceStart.State == SessionTrackState.TrackOpen;
            for (int i = 0; i < cars.Length; i++)
            {
                var grid = raceStart.GridPosition(i, 0f);
                float travel = raceStart.PacedMeters(i, pacedMeters);
                // The green marker can cut this step's travel short without slowing the cars.
                float speed = releasingField && !grid.OnPitRoad ? TrackState001RaceStart.PaceSpeed : travel / SessionStep;
                SessionRun.RecordTravel(i, travel, speed, grid.Distance,
                    reducedBurn: true, onTrack: !grid.OnPitRoad);
            }
            UpdateInitialPacingLanes(SessionStep);
        }
        if (raceStart.State != SessionTrackState.TrackOpen)
            return;

        if (raceReleased)
        {
            stages.BeforeStep(SessionRun, trafficCars, SessionStep, restartReady: !IowaWreckResponseInProgress);
            AdvanceSimulation(SessionStep);
            AdvanceIowaWreckResponse(SessionStep);
            stages.AfterStep(SessionRun, trafficCars, restartReady: !IowaWreckResponseInProgress,
                extendFinalCaution: definition.id == NorthWilkesboroTrackId);
            pitSignal.sharedMaterial = stages.PitOpen ? pitOpenSignal : pitClosedSignal;
            return;
        }

        raceReleased = true;
        stages = ((TrackState001)simulationModel).StartStages(pitTraffic, cars.Length, definition, simulationRandom);
        cautionPaceAvoidanceLane = 0f;
        cautionPaceSample = default;
        previousPaceSnapshotTime = double.NaN;
        stages.PaceTrafficSpeed = LimitCautionPaceTrafficSpeed;
        bool releaseFormation = UsesOvalTraffic007;
        float halfLane = releaseFormation ? FormationHalfLane : 0f;
        for (int i = 0; i < cars.Length; i++)
        {
            var grid = raceStart.GridPosition(i, halfLane);
            sessionDistances[i] = grid.Distance;
            trafficCars[i] = pitTraffic.CreateTrackCar(grid.Distance);
            if (releaseFormation)
                drivingLanes[i] = FormationTrackLane(grid.Distance, grid.Lane);
        }
        if (releaseFormation)
            drivingLanesReady = true;

        ConfigurePitField();
        SessionRun = new RaceSessionRun(simulation.Session, lapLength, raceStart.TimingStartLine,
            simulation, speedModel, simulationRandom, initialDistances: sessionDistances, finishOnCheckered: true);
    }

    private void UpdateInitialPacingLanes(float seconds)
    {
        if (UsesOvalTraffic007 || draft == null || !TrafficLaneChangesEnabled || behaviorModel is not VehicleBehavior003)
            return;

        float halfLane = FormationHalfLane;
        for (int i = 0; i < cars.Length; i++)
        {
            var grid = raceStart.GridPosition(i, halfLane);
            onTrack[i] = !grid.OnPitRoad && cars[i].gameObject.activeSelf;
            draftEligible[i] = onTrack[i] && grid.Distance - pitTraffic.ExitDistance >= formationMergeLength;
            if (!draftEligible[i])
                draft.AppliedLaneOffsets[i] = 0f;
            draft.ApronEdgeAllowance[i] = 0f;
            drivingDistances[i] = grid.Distance;
            drivingWidths[i] = onTrack[i] ? RoadWidths(grid.Distance).x : 0f;
            currentTrafficSpeeds[i] = SessionRun.CurrentSpeed(i);
            drivingLanes[i] = collisionStartLanes[i] = grid.Lane + draft.AppliedLaneOffsets[i];
        }

        draft.UpdateLaneVariation(lapLength, seconds, drivingDistances, drivingLanes, currentTrafficSpeeds,
            draftEligible, plate: false, pacing: true);
        for (int i = 0; i < cars.Length; i++)
        {
            if (draftEligible[i])
                VehicleBehavior003.ApplyLaneVariation(i, lapLength, seconds, drivingDistances, drivingWidths, onTrack,
                    drivingLanes, currentTrafficSpeeds, draft, collisionStartLanes);
        }
        drivingLanesReady = true;
    }

    private bool RaceFieldParked()
    {
        if (!SessionRun.Complete || IowaWreckResponseInProgress)
            return false;

        for (int i = 0; i < trafficCars.Length; i++)
        {
            if (i == 0 && NorthWilkesboroParkedForGarage)
                continue;
            if (!SessionRun.Retired(i) && (!trafficCars[i].InPit || trafficCars[i].PitDistance != practiceStallDistances[i]))
                return false;
        }
        return true;
    }

    private void CreatePaceCar(bool usePaceTruck)
    {
        paceCar = CreateObject("Pace Car", transform).transform;
        paceWheels = null;
        if (usePaceTruck && TryCreatePaceTruck())
            return;
        var body = MakeMaterial(new Color32(237, 239, 244, 255));
        var accent = MakeMaterial(new Color32(22, 69, 132, 255));
        var rubber = MakeMaterial(new Color32(24, 25, 30, 255));
        var glass = MakeMaterial(new Color32(43, 66, 87, 255));
        if (CreateRaceCarModel(paceCar, accent, body, glass, rubber, null, out _) == null)
            BuildCar(paceCar, 0, accent, body, glass, rubber);
        paceWheels = RaceWheelAnimation.Bind(paceCar);
        Box("Light bar", paceCar, new Vector3(0f, 0.765f, -0.21f), new Vector3(0.55f, 0.10f, 0.22f), rubber);
        var amber = MakeMaterial(new Color32(255, 208, 22, 255));
        amber.EnableKeyword("_EMISSION");
        amber.SetColor("_EmissionColor", new Color(2f, 1.4f, 0.04f));
        paceLights = Box("Flashing yellow lights", paceCar, new Vector3(0f, 0.845f, -0.21f),
            new Vector3(0.55f, 0.12f, 0.22f), amber);
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

    private float PitAdjustedSpeed(int index, float freeSpeed, float seconds, float braking,
        float preferredLineSpeedFactor, float? paceSpeedCeiling = null, bool coastToTarget = false)
    {
        var car = trafficCars?[index];
        bool activeOnTrack = car?.InPit != true && car?.InGarage != true &&
            (practice == null || practice.State is SessionTrackState.TrackOpen or SessionTrackState.Caution);
        if (!activeOnTrack)
            preferredLineSpeedFactor = 1f;
        float cleanAirSpeed = float.NaN;
        if (!coastToTarget && braking <= 0f && activeOnTrack && DraftSpeedActive && !BlendingStartLanes &&
            stages?.ReleasingRestartLanes != true && draftEligible[index] &&
            SessionRun.CurrentSpeed(index) >= 75f * TrackDefinition.MetersPerMile / 3600f &&
            (definition.restrictorPlate || draft.SpeedBoost(index) > .1f))
        {
            float distance = car?.TrackDistance ?? sessionDistances[index];
            cleanAirSpeed = SpeedAt(index, distance, out _, includeDraft: false);
        }
        if (car == null || pitTraffic == null)
            return SessionRun.ConditionLimitedSpeedAndPace(index, freeSpeed, seconds, out trafficPaceSpeeds[index],
                braking, activeOnTrack, paceSpeedCeiling, preferredLineSpeedFactor, cleanAirSpeed, coastToTarget);
        float stopAt = float.PositiveInfinity;
        if (practice?.State == SessionTrackState.ReturnToPits)
            stopAt = practice.ParkingTarget(index);
        else if (pitStops != null)
            stopAt = pitStops.StopTarget(index);
        float approachSpeed = LimitNorthWilkesboroApronSpeed(car, freeSpeed);
        bool seekingPitLane = activeOnTrack && car.ApronEntryActive && pitStops?.IsRequested(index) == true &&
            !SessionRun.Finished(index) &&
            Mathf.Abs(drivingLanes[index] - PitApproachLane(index, car.TrackDistance, car.NextEntry)) > .25f;
        // Brake for pit-road speed once the car has secured the entry lane.
        float target = seekingPitLane ? freeSpeed : pitTraffic.PlannedSpeed(car, approachSpeed, stopAt, seconds);
        if (target < freeSpeed)
        {
            braking = Mathf.Max(braking, TrackPitTraffic.PitApproachDeceleration);
            preferredLineSpeedFactor = 1f;
        }
        float limited = SessionRun.ConditionLimitedSpeedAndPace(index, target, seconds, out trafficPaceSpeeds[index],
            braking, activeOnTrack, paceSpeedCeiling, preferredLineSpeedFactor, cleanAirSpeed, coastToTarget);
        if (SessionRun.HasTerminalFailure(index))
            car.NextEntry = float.PositiveInfinity;
        return limited;
    }

    private void AdvancePracticeStep()
    {
        bool yellow = practice.State == SessionTrackState.Caution;
        bool trackOpen = practice.State == SessionTrackState.TrackOpen || yellow;
        float seconds = trackOpen ? SessionRun.StepDuration(SessionStep) : SessionStep;
        if ((UsesAuthoredApronHandoffs || UsesEarlyApronPitApproach) && trackOpen &&
            !SessionRun.Retired(0) && !crashes[0].Active)
            pitStops.ArmQueuedEntry(0, pitOpen: true, allowEmergencyEntry: true);
        pitTraffic.BeginStep(SessionRun.Elapsed + seconds);
        UpdateDraftSpeed(seconds);
        for (int i = 0; i < cars.Length; i++)
        {
            trafficPaceSpeeds[i] = 0f;
            float preferredLineSpeedFactor = 1f;
            bool shortTrackYellow = yellow && definition.lengthMiles <= .5f;
            float yellowSpeed = shortTrackYellow ? TrackState001Stages.ShortTrackPaceSpeed : TrackState001Stages.PaceSpeed;
            if (yellow && !shortTrackYellow && !trafficCars[i].InPit && !trafficCars[i].ApronEntryActive && !trafficCars[i].ApronExitActive)
                yellowSpeed += TrackState001RaceStart.PaceSpeedVariation(i, SessionRun.Elapsed, amplitude: .35f);
            stepSpeeds[i] = SessionRun.Retired(i) ? 0f : crashes[i].Active ? crashes[i].ForwardSpeed :
                PitAdjustedSpeed(i, yellow ? yellowSpeed : SpeedAt(i, trafficCars[i].TrackDistance, out preferredLineSpeedFactor),
                    seconds, shortTrackYellow && !trafficCars[i].InPit ? 0f : yellow ? LapSpeedProfile.MaximumBraking(definition) : trackOpen ? TrackBrakingLimit(i) : 0f,
                    shortTrackYellow ? 1f : preferredLineSpeedFactor, coastToTarget: shortTrackYellow && !trafficCars[i].InPit);
            if (crashes[i].Cause != CrashCause.FrontTireWear)
                stepSpeeds[i] = SessionRun.FlatTireSpeed(stepSpeeds[i], SessionRun.FlatTireMask(i));
        }
        AvoidTrackTraffic(seconds);
        pitTraffic.PlanTrafficSpeeds(stepSpeeds, seconds);
        for (int i = 0; i < cars.Length; i++)
        {
            if (i == 0 && NorthWilkesboroGarageOffPitRoute)
                continue;
            var car = trafficCars[i];
            double before = car.InPit ? pitTraffic.RaceDistance(car) : car.PreciseTrackDistance;
            if (crashes[i].Active)
            {
                AdvanceCrash(i, seconds);
                continue;
            }
            car.Retired = SessionRun.Retired(i);
            if (car.Retired)
                continue;
            double beforeMovement = car.Odometer;
            bool wasInPit = car.InPit;
            bool reducedBurn = car.InPit || yellow || !trackOpen;
            float speed = stepSpeeds[i];
            bool serviceBeforeRelease = (pitRoadOutside || definition.restrictorPlate) && !practiceLeftStall[i] && car.InPit &&
                car.PitDistance == practiceStallDistances[i] && SessionRun.FlatTireMask(i) != 0;
            if (trackOpen && !car.Retired && !SessionRun.HasTerminalFailure(i) &&
                (i == 0 && playerGarageVisit || serviceBeforeRelease || pitStops.IsRequested(i) ||
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
            if (wasInPit && !car.InPit) drivingLanes[i] = TargetDrivingLane(i, car.TrackDistance, car);
            cars[i].gameObject.SetActive(!car.InGarage);
            practiceLeftStall[i] |= !car.InPit || car.PitDistance != practiceStallDistances[i];
            SessionRun.RecordTravel(i, car.Odometer - beforeMovement, (car.Odometer - beforeMovement) / seconds, car.PreciseTrackDistance, reducedBurn, onTrack: !car.InPit);
            if (trackOpen)
            {
                SessionRun.Record(i, before, car.InPit ? pitTraffic.RaceDistance(car) : car.PreciseTrackDistance, seconds);
                StartTireWearFailure(i, car);
            }
        }

        pitTraffic.EndStep();
        ResolveTrackCollisions(seconds);
        bool hazardRemains = HandleStoppedCars(seconds);
        practice.AdvanceCaution(seconds, hazardRemains);
        AdvanceNorthWilkesboroGarageTravel(seconds);
        if (trackOpen)
        {
            SessionRun.AdvanceClock(seconds);
            if (SessionRun.Complete)
                practice.CloseTrack();
        }
        else
        {
            practice.CheckParking(NorthWilkesboroParkedForGarage);
        }
    }

    private float[] freeRunningSpeeds;
    private float[] drivingDistances;
    private float[] drivingWidths;
    private bool[] onTrack;
    private float[] drivingLanes;
    private float[] targetLanes;
    private VehicleBehavior003.TrafficLaneControl[] trafficLaneControl;
    private bool[] stabilizeTrafficLane;
    private float[] trafficPaceSpeeds;
    private float[] trafficStartLaneSpeeds;
    private float[] preferredLineAdvance;
    private Vector2[] passingLaneRanges;
    private bool[] cornerApproaches;
    private VehicleBehavior003.PassForecast passForecast;
    private System.Func<int, float, float, (float TargetSpeed, float BrakingRate)> passingPaceAt;
    private System.Func<int, float, float> passingReferenceLaneAt;
    private System.Func<int, float, Vector2> passingLaneRangeAt;
    private float[] raceCarHalfWidths;
    private float[] preferredInsideClearances;
    private System.Func<float, float> sweepingLineTracking;
    private System.Func<float, float> kinkSteeringBlend;
    private bool drivingLanesReady;
    internal bool TrafficLaneChangesEnabled { get; set; }
    private bool collisionsEnabled;
    private VehicleCollision001 collisions;
    private VehicleCollision001 pitCollisions;
    private VehicleCollision001 backExitCollisions;
    private bool[] backExitParticipants;
    private bool[] pitParticipants;
    private float[] pitStart;
    private float[] pitEnd;
    private float[] pitLaneStart;
    private float[] pitYawStart, pitYawEnd;
    private Vector3[] pitWorldStart, pitWorldEnd;
    private float[] pitWorldYawStart, pitWorldYawEnd;
    private bool[] pitWorldActive, pitWorldInvolved;
    private float[] pitLaneEnd;
    private float[] stallLanes;
    private bool pitRoadOutside;
    private bool[] practiceLeftStall;
    private CrashMotion[] crashes;
    private Pose?[] chasePoseBeforeYaw;

    private enum CrashCause { Contact, Apron, FrontTireWear, RearTireWear }

    private struct CrashMotion
    {
        public bool Active;
        public CrashCause Cause;
        public HashSet<int> ContactParticipants;
        public float ForwardSpeed;
        public float SideSpeed;
        public float Lane;
        public float Yaw;
        public float Spin;
        public StraightTireMotion Straight;
    }
    private const float CollisionFlashSeconds = 0.4f;
    private Transform[] collisionFlashes;
    private float[] collisionFlashRemaining;
    private bool[] collisionParticipants;
    private float[] collisionEndDistances;
    private float[] collisionStartLanes;
    private float[] collisionTireStartLanes;
    private float[] collisionTireEndLanes;
    private float[] collisionTireOffsets;
    private float[] tireYawAngles;

    private float[] currentTrafficSpeeds;
    private float[] trafficBrakingFactors;
    private float[] stoppedCarTime, pitObstructionTime;
    private bool[] pitObstruction;
    private double nextPitClearAt;
    private const float PitQueueReleaseSeconds = 2f;
    private PracticeStoppedCar[] practiceStoppedCars;
    private int[] practiceCarAhead;
    private float[] practiceGapAhead;
    private float[] stepSpeeds;

    private void AdvanceSimulation(float seconds)
    {
        bool scoringCaution = TrackState is SessionTrackState.Caution or SessionTrackState.StageEndingUnderCaution
            or SessionTrackState.FinishUnderCaution or SessionTrackState.Pacing or SessionTrackState.StageComplete;
        bool pitMembershipChanged = false;
        float previousElapsed = elapsed;
        elapsed += seconds;
        if ((UsesAuthoredApronHandoffs || UsesEarlyApronPitApproach) && pitStops != null && !isQualifying &&
            !SessionRun.Finished(0) && !SessionRun.Retired(0) && !crashes[0].Active)
            pitStops.ArmQueuedEntry(0, stages?.CanEnterPits(0, trafficCars[0]) ?? true,
                allowEmergencyEntry: stages?.IsCaution != true);
        if (stages != null && pitStops != null && SessionRun?.IsRace == true &&
            TrackState is not (SessionTrackState.Checkered or SessionTrackState.FinishUnderCaution))
        {
            var pitWindow = stages.RivalPitWindow(SessionRun, trafficCars);
            for (int i = 1; i < cars.Length; i++)
                if (!crashes[i].Active)
                    pitStops.PlanRivalRaceStop(SessionRun, i, pitWindow, stages.CanEnterPits(i, trafficCars[i]));
        }
        pitTraffic?.BeginStep((SessionRun?.Elapsed ?? previousElapsed) + seconds);
        UpdateDraftSpeed(seconds);
        for (int i = 0; i < cars.Length; i++)
        {
            trafficPaceSpeeds[i] = 0f;
            if (SessionRun?.Finished(i) == true)
            {
                stepSpeeds[i] = stages != null && !SessionRun.Retired(i) && !trafficCars[i].InGarage
                    ? stages.FinishedCarSpeed(trafficCars[i], seconds, practiceStallDistances[i])
                    : 0f;
                continue;
            }

            if (crashes[i].Active)
            {
                stepSpeeds[i] = crashes[i].ForwardSpeed;
                continue;
            }

            float preferredLineSpeedFactor = 1f;
            float speed = SessionRun == null ? CarSpeed(i)
                : SpeedAt(i, trafficCars?[i]?.TrackDistance ?? sessionDistances[i], out preferredLineSpeedFactor);
            float? paceSpeedCeiling = (definition.id is RichmondTrackId or NorthWilkesboroTrackId) &&
                stages?.ControlsField == true && trafficCars[i].InPit == false ? speed : null;
            if (stages != null)
            {
                speed = raceStart.RacingSpeed(speed, SessionRun.Elapsed + seconds);
                speed = stages.CarSpeed(i, trafficCars, speed, seconds);
            }
            if (SessionRun != null)
            {
                bool coastUnderCaution = stages?.UsesShortTrackCautionPace == true && !trafficCars[i].InPit;
                speed = PitAdjustedSpeed(i, speed, seconds, coastUnderCaution ? 0f : TrackBrakingLimit(i),
                    coastUnderCaution ? 1f : preferredLineSpeedFactor, coastUnderCaution ? null : paceSpeedCeiling, coastUnderCaution);
            }
            if (pitStops != null)
                speed = SessionRun.FlatTireSpeed(speed, SessionRun.FlatTireMask(i));

            stepSpeeds[i] = speed;
        }
        stages?.PreserveCautionOrder(trafficCars, stepSpeeds, seconds);
        bool checkTraffic = SessionRun != null && !isQualifying;
        if (checkTraffic)
            AvoidTrackTraffic(seconds);
        pitTraffic?.PlanTrafficSpeeds(stepSpeeds, seconds);
        LimitTrafficBehindCautionPaceCar(seconds);
        stages?.HoldPitRejoinsBehindLine(trafficCars, stepSpeeds, seconds);

        for (int i = 0; i < cars.Length; i++)
        {
            if (i == 0 && NorthWilkesboroGarageOffPitRoute)
                continue;
            if (SessionRun?.Finished(i) == true)
            {
                if (stages != null && !SessionRun.Retired(i) && !trafficCars[i].InGarage)
                {
                    var finishedCar = trafficCars[i];
                    double beforeParking = finishedCar.Odometer;
                    stages.AdvanceFinishedCar(finishedCar, seconds, stepSpeeds[i], practiceStallDistances[i]);
                    SessionRun.RecordTravel(i, finishedCar.Odometer - beforeParking,
                        (finishedCar.Odometer - beforeParking) / seconds, finishedCar.PreciseTrackDistance, reducedBurn: true, onTrack: !finishedCar.InPit);
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
                double before = car.InPit ? pitTraffic.RaceDistance(car) : car.PreciseTrackDistance;
                double beforeMovement = car.Odometer;
                bool wasInPit = car.InPit;
                bool reducedBurn = wasInPit || stages?.ReducedFuelBurn == true;
                car.Retired = SessionRun?.Retired(i) == true;
                if (pitStops != null && !car.Retired && !SessionRun.HasTerminalFailure(i) &&
                    (SessionRun.IsRace || i == 0 && playerGarageVisit || pitStops.IsRequested(i)))
                    pitStops.Advance(i, SessionRun, seconds, speed, stages?.CanEnterPits(i, car) ?? true,
                        allowEmergencyEntry: stages?.IsCaution != true);
                else
                    pitTraffic.Advance(car, seconds, speed);
                if (wasInPit && !car.InPit) drivingLanes[i] = TargetDrivingLane(i, car.TrackDistance, car);
                pitMembershipChanged |= wasInPit != car.InPit;
                TimingRun?.RecordTravel(i, car.Odometer - beforeMovement, (car.Odometer - beforeMovement) / seconds, car.PreciseTrackDistance, reducedBurn, onTrack: !car.InPit);
                TimingRun?.Record(i, before, car.InPit ? pitTraffic.RaceDistance(car) : car.PreciseTrackDistance, seconds);
                if (SessionRun != null)
                    StartTireWearFailure(i, car);
            }
            else if (SessionRun != null)
            {
                float before = sessionDistances[i];
                sessionDistances[i] += speed * seconds;
                SessionRun.RecordTravel(i, sessionDistances[i] - before, speed, sessionDistances[i]);
                SessionRun.Record(i, before, sessionDistances[i], seconds);
                StartTireWearFailure(i, null);
            }
        }
        pitTraffic?.EndStep();
        if (previewTiming != null && pitTraffic == null)
        {
            for (int i = 0; i < cars.Length; i++)
            {
                float offset = lapLength * i / cars.Length;
                float speed = CarSpeed(i);
                previewTiming.RecordTravel(i, seconds * speed, speed, offset + elapsed * speed);
                previewTiming.Record(i, offset + previousElapsed * speed, offset + elapsed * speed, seconds);
            }
        }
        if (checkTraffic)
            ResolveTrackCollisions(seconds);
        AdvanceNorthWilkesboroGarageTravel(seconds);
        TimingRun?.AdvanceClock(seconds, raceLapUnderCaution: scoringCaution);
        if (stages != null)
            pitMembershipChanged |= HandleStoppedCars(seconds);
        if (pitMembershipChanged && stages?.ReducedFuelBurn == true && stages.ControlsField)
            stages.UpdatePitFormation(SessionRun, trafficCars);
    }

    private bool HandleStoppedCars(float seconds)
    {
        if (practice != null)
        {
            bool hazard = false;
            for (int i = 0; i < cars.Length; i++)
            {
                var car = trafficCars[i];
                ref var stopped = ref practiceStoppedCars[i];
                stopped.Update(cars[i].gameObject.activeSelf && !car.InGarage,
                    car.InPit, SessionRun.Retired(i), SessionRun.CurrentSpeed(i), seconds);
                if (stopped.RequestYellow) RequestPracticeCaution(CautionReasonForCar(i));
                if (stopped.Remove)
                {
                    SessionRun.Retire(i);
                    car.Retired = true;
                    car.InGarage = true;
                    crashes[i].Active = false;
                    cars[i].gameObject.SetActive(false);
                }
                else hazard |= stopped.IsHazard;
            }
            return hazard;
        }
        int incident = -1;
        HashSet<int> incidentParticipants = null;
        for (int i = 0; i < cars.Length; i++)
        {
            var car = trafficCars[i];
            if (!SessionRun.Retired(i) || car.InPit)
                continue;

            car.Retired = true;
            if (stoppedCarTime[i] == 0f)
            {
                if (incident < 0)
                    incident = i;
                incidentParticipants ??= new HashSet<int>();
                incidentParticipants.Add(i);
                if (crashes[i].ContactParticipants != null)
                    incidentParticipants.UnionWith(crashes[i].ContactParticipants);
                AddTrackImpactPartners(i, incidentParticipants);
                if (crashes[i].Active)
                    QueueIowaWreckResponse(i);
            }
            stoppedCarTime[i] += seconds;
            if (stoppedCarTime[i] >= 10f && cars[i].gameObject.activeSelf && !IsIowaWreckAwaitingRecovery(i))
            {
                car.InGarage = true;
                cars[i].gameObject.SetActive(false);
            }
        }

        if (incident >= 0)
            stages?.RequestCaution(SessionRun, trafficCars, reason: CautionReasonForCar(incident), incidentParticipants: incidentParticipants);
        return ClearStalledPitTraffic(seconds);
    }

    private bool ClearStalledPitTraffic(float seconds)
    {
        // Measure a caution lap in simulation time so a stationary field cannot stop cleanup.
        float timeout = lapLength / TrackState001Stages.PaceSpeed;
        System.Array.Clear(pitObstruction, 0, pitObstruction.Length);
        for (int i = 0; i < trafficCars.Length; i++)
        {
            var car = trafficCars[i];
            if (car.InGarage) continue;
            if (car.MovementBlockedThisStep && (uint)car.MovementBlockedByIndex < (uint)pitObstruction.Length)
                pitObstruction[car.MovementBlockedByIndex] = true;
        }

        int clear = -1;
        for (int i = 0; i < trafficCars.Length; i++)
        {
            var car = trafficCars[i];
            bool atStall = Mathf.Abs(car.PitDistance - practiceStallDistances[i]) < .05f &&
                Mathf.Abs(car.PitLane - stallLanes[i]) < .25f;
            bool stranded = car.InPit && !car.InGarage && !atStall &&
                SessionRun.CurrentSpeed(i) < .45f &&
                (pitObstruction[i] || SessionRun.Retired(i) || SessionRun.HasTerminalFailure(i) || SessionRun.Fuel(i) <= 0d);
            if (!stranded)
            {
                pitObstructionTime[i] = 0f;
                continue;
            }

            pitObstructionTime[i] += seconds;
            if (pitObstructionTime[i] >= timeout &&
                (clear < 0 || car.PitDistance > trafficCars[clear].PitDistance))
                clear = i;
        }
        if (clear < 0 || SessionRun.PreciseElapsed < nextPitClearAt) return false;

        // Clear one blocker, then let the queue roll before clearing another.
        var removed = trafficCars[clear];
        if (!SessionRun.Finished(clear)) SessionRun.Retire(clear);
        removed.Retired = true;
        removed.InGarage = true;
        removed.NextEntry = float.PositiveInfinity;
        crashes[clear].Active = false;
        pitStops?.ClearObstructedCar(clear);
        cars[clear].gameObject.SetActive(false);
        nextPitClearAt = SessionRun.PreciseElapsed + PitQueueReleaseSeconds;
        return true;
    }

    private string CautionReasonForCar(int index)
    {
        string car = $"No. {simulation.Cars[index].DisplayCarNumber}";
        if (crashes[index].Active)
            return crashes[index].Cause switch
            {
                CrashCause.Apron => $"{car} spun on the apron",
                CrashCause.FrontTireWear => crashes[index].Straight.WallDistanceRemaining == 0f
                    ? $"{car} hit the wall after a front tire failed" : $"{car} ran off after a front tire failed",
                CrashCause.RearTireWear => $"{car} spun after a rear tire failed",
                _ => $"{car} spun after contact"
            };
        string failure = SessionRun.MechanicalFailure(index);
        return string.IsNullOrEmpty(failure) ? $"{car} stopped on track" : $"{car} - {failure}";
    }

    private bool DraftSpeedActive => draft != null && drivingLanesReady && stages?.ReducedFuelBurn != true &&
        TrackState is null or SessionTrackState.TrackOpen or SessionTrackState.WhiteFlag;

    private bool PlateDraftLanesActive => definition.restrictorPlate && TrafficLaneChangesEnabled && draft != null &&
        (simulation.Session.kind == RaceSessionKind.Race || behaviorModel is VehicleBehavior003) &&
        stages?.ReducedFuelBurn != true && TrackState != SessionTrackState.Checkered &&
        practice?.State != SessionTrackState.Caution;

    private bool DraftBumpsActive => DraftSpeedActive && definition.restrictorPlate && SessionRun.IsRace &&
        TrafficLaneChangesEnabled && collisionsEnabled && behaviorModel is VehicleBehavior003 &&
        !BlendingStartLanes && stages?.ReleasingRestartLanes != true;

    private bool BlendingStartLanes => raceReleased && SessionRun.Elapsed < 3f;

    private bool UsesOvalTraffic007 => behaviorModel is VehicleBehavior007 && definition.majorStyle == "Oval" &&
        !definition.restrictorPlate;

    internal bool CanAdjustPassingCriteria => SessionRun != null && !isQualifying && TrafficLaneChangesEnabled && UsesOvalTraffic007;
    private float OvalPassingEase => 2f * (1f - (float)ovalPassingStrictness / StrictOvalPassing);

    internal int OvalPassingStrictness
    {
        get => ovalPassingStrictness;
        set
        {
            int strictness = Mathf.Clamp(value, 0, StrictOvalPassing);
            if (ovalPassingStrictness == strictness)
                return;

            ovalPassingStrictness = strictness;
            // A new threshold starts a fresh observation without interrupting a committed pass.
            for (int i = 0; i < trafficLaneControl.Length; i++)
                trafficLaneControl[i].PassingOpportunitySeconds = 0f;
        }
    }

    private bool IgnorePreferredDrivingLine => definition.restrictorPlate &&
        (simulation?.Session?.kind is RaceSessionKind.Practice or RaceSessionKind.Race);

    private void UpdateDraftSpeed(float seconds)
    {
        plateDraftRaceLeader = -1;
        if (!DraftSpeedActive)
        {
            draft?.ClearSpeed();
            System.Array.Clear(trailingSpeedBoosts, 0, trailingSpeedBoosts.Length);
            return;
        }

        if (draftPassing != null && behaviorModel is VehicleBehavior003)
            plateDraftRaceLeader = SessionRun.RaceLeaderIndex();
        bool boostTrailingCars = plateDraftRaceLeader >= 0 && behaviorModel is VehicleBehavior007 && raceReleased &&
            !BlendingStartLanes && stages?.ReleasingRestartLanes != true &&
            TrackState is (SessionTrackState.TrackOpen or SessionTrackState.WhiteFlag);
        float leaderProgress = boostTrailingCars ? SessionRun.Progress(plateDraftRaceLeader) : 0f;

        for (int i = 0; i < cars.Length; i++)
        {
            var traffic = trafficCars?[i];
            float distance = traffic?.TrackDistance ?? sessionDistances[i];
            drivingDistances[i] = distance;
            currentTrafficSpeeds[i] = SessionRun.CurrentSpeed(i);
            draftPitApproach[i] = traffic == null ? float.PositiveInfinity
                : Mathf.Min(traffic.NextEntry - distance, distance - traffic.LastExit);
            draftEligible[i] = cars[i].gameObject.activeSelf && !iowaRecoveredCars[i].HasValue &&
                !crashes[i].Active && !SessionRun.Retired(i) && SessionRun.FlatTireMask(i) == 0 &&
                traffic?.InPit != true && traffic?.InGarage != true &&
                traffic?.ApronEntryActive != true && traffic?.ApronExitActive != true;
            trailingSpeedBoosts[i] = boostTrailingCars && draftEligible[i] && !SessionRun.Finished(i)
                ? TrackDraft.TrailingSpeedBoost(leaderProgress - SessionRun.Progress(i))
                : 0f;
        }

        draft.UpdateSpeed(lapLength, seconds, SessionRun, draftEligible, drivingDistances, drivingLanes,
            currentTrafficSpeeds, draftPitApproach, definition.restrictorPlate);
    }

    private void AvoidTrackTraffic(float seconds)
    {
        float inside = customLayout?.InsideSign ?? -1f;
        bool ovalTraffic = UsesOvalTraffic007;
        bool holdingCaution = ovalTraffic && stages?.ReducedFuelBurn == true;
        bool practiceYellow = practice?.State == SessionTrackState.Caution;
        // Drivers must clear each other when leaving the restart rows, even if
        // ordinary traffic interactions are disabled.
        bool protectRestartMerge = collisionsEnabled && !TrafficLaneChangesEnabled && stages?.ReleasingRestartLanes == true;
        bool allowPassing = !protectRestartMerge && !practiceYellow && stages?.ReducedFuelBurn != true;
        bool forecastOvalPassing = ovalTraffic && allowPassing;
        bool canStabilizeLanes = practice == null || practice.State is SessionTrackState.TrackOpen or SessionTrackState.Caution;
        bool pacedLanes = BlendingStartLanes || TrackState is SessionTrackState.Pacing or SessionTrackState.StageComplete
            or SessionTrackState.Caution or SessionTrackState.StageEndingUnderCaution or SessionTrackState.FinishUnderCaution;
        bool varyLanes = !ovalTraffic && TrafficLaneChangesEnabled && draft != null && (pacedLanes || DraftSpeedActive) &&
            stages?.ReleasingRestartLanes != true;
        for (int i = 0; i < cars.Length; i++)
        {
            var traffic = trafficCars?[i];
            // A towed wreck remains visible, but its race and pit coordinates still point to the pickup.
            bool hasTrafficPose = !iowaRecoveredCars[i].HasValue && cars[i].gameObject.activeSelf;
            onTrack[i] = hasTrafficPose && traffic?.InPit != true && (traffic?.FlatTireLaneBlend ?? 0f) == 0f;
            float distance = traffic?.TrackDistance ?? sessionDistances[i];
            drivingDistances[i] = distance;
            draftPitApproach[i] = traffic == null ? float.PositiveInfinity : traffic.NextEntry - distance;
            currentTrafficSpeeds[i] = SessionRun.CurrentSpeed(i);
            trafficBrakingFactors[i] = SessionRun.BrakingPerformanceFactor(i);
            Vector2 road = RoadWidths(distance);
            drivingWidths[i] = road.x;
            targetLanes[i] = TargetDrivingLane(i, distance, traffic);
            if (forecastOvalPassing)
            {
                float lookAhead = Mathf.Max(10f, currentTrafficSpeeds[i] * .75f);
                passingLaneRanges[i] = OvalPassingLaneRange(i, distance, road.x, lookAhead);
                cornerApproaches[i] = SessionRun.FlatTireMask(i) == 0 && lapSpeedProfile != null &&
                    (lapSpeedProfile.TireTurnWeightAt(distance) > 0f || lapSpeedProfile.TireTurnWeightAt(distance + lookAhead) > 0f);
            }
            stabilizeTrafficLane[i] = canStabilizeLanes && !holdingCaution && !crashes[i].Active &&
                !SessionRun.Retired(i) && !SessionRun.Finished(i) && traffic?.ApronEntryActive != true && traffic?.ApronExitActive != true;
            if (varyLanes)
            {
                draftEligible[i] = onTrack[i] && stabilizeTrafficLane[i] && SessionRun.FlatTireMask(i) == 0 &&
                    traffic?.InGarage != true;
                if (draftMerging != null)
                    draftManeuverEligible[i] = draftEligible[i] && (traffic == null ||
                        Mathf.Min(traffic.NextEntry - distance, distance - traffic.LastExit) > 25f);
                draft.ApronEdgeAllowance[i] = pacedLanes ? 0f : inside * Mathf.Min(.32f, road.y);
            }
            if (!drivingLanesReady || !onTrack[i])
                drivingLanes[i] = targetLanes[i];
            if (crashes[i].Active)
                drivingLanes[i] = crashes[i].Lane;
            collisionStartLanes[i] = drivingLanes[i];
            var laneControl = trafficLaneControl[i];
            trafficStartLaneSpeeds[i] = stabilizeTrafficLane[i] && laneControl.HasLane &&
                Mathf.Abs(laneControl.LastLane - drivingLanes[i]) <= .001f ? laneControl.LateralSpeed : 0f;
            collisionParticipants[i] = onTrack[i];
            if (collisionsEnabled && collisionParticipants[i])
                collisionTireStartLanes[i] = TireAdjustedLane(i, distance, drivingLanes[i], traffic, out _);
            pitParticipants[i] = hasTrafficPose && traffic?.InPit == true;
            backExitParticipants[i] = pitParticipants[i] && traffic.BackExit;
            pitParticipants[i] &= !backExitParticipants[i];
            pitStart[i] = backExitParticipants[i] ? traffic.ConnectorDistance : traffic?.PitDistance ?? 0f;
            pitLaneStart[i] = traffic?.PitLane ?? 0f;
            pitYawStart[i] = traffic?.PitYaw ?? 0f;
            if (definition.id is BristolTrackId or MartinsvilleTrackId or IowaTrackId or PhoenixTrackId or RichmondTrackId or NorthWilkesboroTrackId)
            {
                pitWorldStart[i] = cars[i].localPosition;
                Vector3 forward = cars[i].localRotation * Vector3.forward;
                pitWorldYawStart[i] = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
                pitWorldActive[i] = hasTrafficPose && traffic?.InGarage != true;
                pitWorldInvolved[i] = traffic?.InPit == true;
            }
        }

        if (practiceYellow)
            TrackState001Stages.PreserveOrder(lapLength, stepSpeeds, seconds, practiceCarAhead, practiceGapAhead,
                PracticeCarDistance, HoldsPracticePosition);

        bool plateDraftLanes = PlateDraftLanesActive;
        if (plateDraftLanes)
            draft.UpdatePlateLanes(lapLength, SessionRun, onTrack, drivingDistances, drivingWidths, drivingLanes,
                targetLanes, currentTrafficSpeeds, draftPitApproach, behaviorModel is VehicleBehavior003);
        bool allowDraftManeuvers = plateDraftLanes && DraftSpeedActive && !pacedLanes &&
            stages?.ReleasingRestartLanes != true && behaviorModel is VehicleBehavior003;
        bool allowRegrouping = allowDraftManeuvers && SessionRun.IsRace;
        bool allowDraftBumps = DraftBumpsActive;
        int raceLeader = allowDraftBumps || allowRegrouping ? plateDraftRaceLeader : -1;
        draftMerging?.Update(lapLength, seconds, allowDraftManeuvers, onTrack, draftManeuverEligible, drivingDistances,
            drivingLanes, drivingWidths, currentTrafficSpeeds, targetLanes, allowRegrouping ? raceLeader : -1);
        if (draftPassing != null)
        {
            draftPassing.Update(draft, lapLength, seconds, allowDraftManeuvers, onTrack, draftManeuverEligible, drivingDistances,
                drivingLanes, drivingWidths, currentTrafficSpeeds, trafficPaceSpeeds, targetLanes, simulation.Cars);
            for (int i = 0; i < cars.Length; i++)
                if (draftPassing.BumperGaps[i] >= 0f)
                    draft.CloseFollowLeaders[i] = draftPassing.LeaderForAttacker(i);
        }
        var mergeLaneChanges = draftMerging?.Active == true ? draftMerging.LaneChanges : null;
        var passLaneChanges = draftPassing?.LaneChanges;
        bool[] directedDraftMoves = null;
        if (mergeLaneChanges != null || passLaneChanges != null)
        {
            for (int i = 0; i < cars.Length; i++)
                directedDraftLaneChanges[i] = mergeLaneChanges?[i] == true || passLaneChanges?[i] == true;
            directedDraftMoves = directedDraftLaneChanges;
        }
        int blockingLeader = -1;
        if (draftLeaderBlock != null)
        {
            int leader = allowDraftManeuvers ? raceLeader : -1;
            if (leader >= 0 && (draftPassing.LeaderForAttacker(leader) >= 0 || draftMerging?.ReservesCar(leader) == true))
                leader = -1;
            float aggression = leader >= 0 ? VehicleBehavior003.CombinedAggression(
                simulation.Cars[leader].driver?.personalityTraits?.aggression ?? .5f, SessionRun.RaceProgressAggression) : .5f;
            if (draftLeaderBlock.Update(draft, lapLength, seconds, leader, aggression, onTrack, draftManeuverEligible,
                    drivingDistances, drivingLanes, drivingWidths, currentTrafficSpeeds, trafficStartLaneSpeeds,
                    directedDraftMoves, targetLanes))
            {
                blockingLeader = leader;
                directedDraftLaneChanges[leader] = true;
            }
        }
        draft?.UpdateBumpIntent(lapLength, seconds, simulation?.Cars, drivingDistances, drivingLanes,
            currentTrafficSpeeds, allowDraftBumps, draftPassing, raceLeader, allowRegrouping ? draftMerging?.Regrouping : null);
        if (mergeLaneChanges != null || blockingLeader >= 0)
        {
            for (int i = 0; i < cars.Length; i++)
            {
                int leader = draft.CloseFollowLeaders[i];
                if (mergeLaneChanges?[i] == true || leader >= 0 && mergeLaneChanges?[leader] == true ||
                    blockingLeader >= 0 && (i == blockingLeader || leader == blockingLeader))
                    draft.FinishBump(i);
            }
        }

        if (!TrafficLaneChangesEnabled)
        {
            for (int i = 0; i < cars.Length; i++)
                targetLanes[i] = TargetDrivingLane(i, drivingDistances[i], trafficCars?[i]);
        }

        System.Array.Copy(stepSpeeds, freeRunningSpeeds, stepSpeeds.Length);
        if (behaviorModel is VehicleBehavior003 aggressiveBehavior)
        {
            aggressiveBehavior.Drivers = simulation?.Cars;
            aggressiveBehavior.RaceProgressAggression = SessionRun?.RaceProgressAggression ?? 0f;
        }
        var sweeping = behaviorModel as VehicleBehavior004;
        if (sweeping != null)
        {
            System.Array.Clear(preferredLineAdvance, 0, preferredLineAdvance.Length);
            if (sweepingLineTracking != null && !IgnorePreferredDrivingLine && !practiceYellow && !holdingCaution &&
                stages?.ControlsField != true)
            {
                for (int i = 0; i < cars.Length; i++)
                {
                    if (!onTrack[i] || crashes[i].Active || stepSpeeds[i] <= 0f ||
                        (plateDraftLanes && draft.InLine[i]))
                        continue;
                    float distance = drivingDistances[i];
                    preferredLineAdvance[i] = SweepingLaneAdvance(i, distance, distance + stepSpeeds[i] * seconds,
                        targetLanes[i], trafficCars?[i]);
                }
            }
            sweeping.PreferredLineAdvance = preferredLineAdvance;
            if (definition.majorStyle == "Oval")
            {
                for (int i = 0; i < cars.Length; i++)
                    preferredInsideClearances[i] = InsideLineClearance(i);
                sweeping.PreferredInsideClearance = preferredInsideClearances;
            }
        }
        float brakingLimit = LapSpeedProfile.MaximumBraking(definition);
        var draftLine = plateDraftLanes ? draft.InLine : null;
        var draftLeaders = DraftSpeedActive && behaviorModel is VehicleBehavior003 ? draft.CloseFollowLeaders : null;
        bool respondToTraffic = TrafficLaneChangesEnabled || protectRestartMerge;
        var laneDraft = varyLanes ? draft : null;
        if (laneDraft != null)
            laneDraft.UpdateLaneVariation(lapLength, seconds, drivingDistances, drivingLanes, currentTrafficSpeeds,
                draftEligible, definition.restrictorPlate, pacing: pacedLanes);
        else
            draft?.ClearLaneVariation();

        if (behaviorModel is VehicleBehavior003 trafficBehavior)
        {
            trafficBehavior.OvalPassingContext = forecastOvalPassing
                ? (passingLaneRanges, cornerApproaches, passForecast ??= ForecastOvalPass) : default;
            trafficBehavior.OvalPassCommitted = forecastOvalPassing ? recordOvalPassAttempt ??= RecordOvalPassAttempt : null;
            trafficBehavior.UsePreferredLineTraffic = forecastOvalPassing;
            trafficBehavior.OvalPassingEase = forecastOvalPassing ? OvalPassingEase : 0f;
            trafficBehavior.TrackContactHalfWidths = ovalTraffic ? raceCarHalfWidths : null;
            trafficBehavior.AvoidTraffic(trafficLaneControl, stabilizeTrafficLane, lapLength, inside, seconds, allowPassing,
                drivingDistances, drivingWidths, onTrack, drivingLanes, targetLanes, currentTrafficSpeeds, stepSpeeds,
                brakingLimit, draftLine, respondToTraffic, trafficBrakingFactors, trafficPaceSpeeds, collisionStartLanes,
                trafficStartLaneSpeeds, draftLeaders, allowDraftBumps ? draft.BumpIntent : null, definition.restrictorPlate,
                laneDraft, directedDraftMoves, draftPassing?.BumperGaps, RoadWidths);
            trafficBehavior.OvalPassingContext = default;
            trafficBehavior.OvalPassCommitted = null;
            trafficBehavior.UsePreferredLineTraffic = false;
            trafficBehavior.OvalPassingEase = 0f;
            trafficBehavior.TrackContactHalfWidths = null;
        }
        else
        {
            System.Array.Clear(trafficLaneControl, 0, trafficLaneControl.Length);
            behaviorModel.AvoidTraffic(lapLength, inside, seconds, allowPassing,
                drivingDistances, drivingWidths, onTrack, drivingLanes, targetLanes, currentTrafficSpeeds, stepSpeeds,
                brakingLimit, draftLine, respondToTraffic, trafficBrakingFactors);
        }
        if (sweeping != null)
        {
            sweeping.PreferredLineAdvance = null;
            sweeping.PreferredInsideClearance = null;
        }
        if (protectRestartMerge)
        {
            bool releaseComplete = true;
            for (int i = 0; i < cars.Length; i++)
            {
                if (onTrack[i] && !crashes[i].Active && !SessionRun.Retired(i) &&
                    Mathf.Abs(drivingLanes[i] - targetLanes[i]) > .1f)
                {
                    releaseComplete = false;
                    break;
                }
            }
            if (releaseComplete)
                stages.FinishRestartLaneRelease();
        }
        bool hasCooldownApproach = false;
        for (int i = 0; i < cars.Length; i++)
        {
            var exitCar = trafficCars?[i];
            if (exitCar?.ApronEntryActive == true && !exitCar.InPit)
            {
                float before = collisionStartLanes[i];
                float pitApproachLane = PitApproachLane(i, drivingDistances[i], exitCar.NextEntry);
                float entryTarget = Mathf.MoveTowards(before, pitApproachLane, VehicleBehavior003.MaximumLateralSpeed * seconds);
                bool returningAfterFinish = stages != null && SessionRun.Finished(i);
                hasCooldownApproach |= returningAfterFinish;
                bool queuedStop = !returningAfterFinish && pitStops?.IsRequested(i) == true;
                bool entryClear = ApronLateralClear(i, before, entryTarget, returningAfterFinish, out int yieldTo);
                drivingLanes[i] = entryClear ? entryTarget : before;
                if (queuedStop && !entryClear && yieldTo >= 0)
                    SeekQueuedPitGap(i, yieldTo, before, pitApproachLane, seconds);
                if (returningAfterFinish && yieldTo >= 0)
                {
                    // Open a longitudinal gap when cooling-down cars reach the apron side by side.
                    float yieldSpeed = Mathf.Max(8f, currentTrafficSpeeds[yieldTo] - 4f);
                    float brakingSpeed = currentTrafficSpeeds[i] - 4.5f * seconds;
                    stepSpeeds[i] = Mathf.Min(stepSpeeds[i], Mathf.Max(yieldSpeed, brakingSpeed));
                }
                float laneRemaining = Mathf.Abs(drivingLanes[i] - pitApproachLane);
                float entryRemaining = exitCar.NextEntry - drivingDistances[i];
                if (returningAfterFinish)
                {
                    float approachSpeed = TrackState001Stages.ShortTrackPaceSpeed;
                    if (laneRemaining > .25f)
                    {
                        float secondsToSettle = laneRemaining / VehicleBehavior003.MaximumLateralSpeed + 1.25f;
                        approachSpeed = Mathf.Min(approachSpeed, Mathf.Max(5f, entryRemaining / secondsToSettle));
                    }
                    float brakingSpeed = currentTrafficSpeeds[i] - 4.5f * seconds;
                    stepSpeeds[i] = Mathf.Min(stepSpeeds[i], Mathf.Max(approachSpeed, brakingSpeed));
                }
                else if (!queuedStop && !(UsesSettledPlayerPitApproach && i == 0) && entryRemaining < 25f && laneRemaining > .25f)
                    pitTraffic.DeferEntry(exitCar);
                continue;
            }
            if (exitCar?.ApronExitActive != true || exitCar.InPit) continue;
            float distance = drivingDistances[i];
            AdvanceApronExitLane(i, exitCar, distance, collisionStartLanes[i],
                ApronExitTarget(i, distance, exitCar), seconds);
        }
        if (hasCooldownApproach)
        {
            // Form a line on the approach so side-by-side finishers have room
            // to move down before the pit-entry lane check.
            TrackState001Stages.PreserveOrder(lapLength, stepSpeeds, seconds, practiceCarAhead, practiceGapAhead,
                index => drivingDistances[index],
                index => onTrack[index] && stepSpeeds[index] > 0f && SessionRun.Finished(index) &&
                    trafficCars[index].ApronEntryActive,
                minimumGap: 6f, openGaps: true);
        }
        for (int i = 0; i < cars.Length; i++)
        {
            bool trafficLimited = stepSpeeds[i] < freeRunningSpeeds[i] - .0001f;
            if (trafficLimited)
                SessionRun.TrafficBrakeInput(i, stepSpeeds[i], seconds, brakingLimit * trafficBrakingFactors[i],
                    drafting: draftLeaders != null && draftLeaders[i] >= 0 && trafficCars?[i]?.ApronEntryActive != true);
            stepSpeeds[i] = SessionRun.ResolveDraftPedal(i, stepSpeeds[i], seconds, trafficLimited);
        }
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
        var traffic = isQualifying ? qualifying.Car : trafficCars?[index];
        if (crash.Cause == CrashCause.FrontTireWear)
        {
            AdvanceStraightTireCrash(index, traffic, seconds);
            return;
        }
        float before = traffic?.TrackDistance ?? sessionDistances[index];
        float oldLane = crash.Lane;
        float forward = crash.ForwardSpeed * seconds;
        float after = before + forward;
        float edge = RoadWidths(after).x * 0.5f + 2f;
        float nextLane = oldLane + crash.SideSpeed * seconds;
        if (definition.id == BristolTrackId && customLayout != null)
        {
            // Clear the wall with the whole car, including when it rotates broadside.
            const float spinningCarRadius = 1.75f;
            float outerSide = -customLayout.InsideSign;
            float wallLimit = RoadWidths(after).x * 0.5f - spinningCarRadius;
            if (nextLane * outerSide >= wallLimit)
            {
                nextLane = wallLimit * outerSide;
                if (crash.SideSpeed * outerSide > 0f)
                    crash.SideSpeed = 0f;
            }
        }
        crash.Lane = Mathf.Clamp(nextLane, -edge, edge);
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
        SessionRun.RecordTravel(index, traveled, traveled / seconds, after, reducedBurn: true, onTrack: traffic?.InPit != true);
        SessionRun.Record(index, before, after, seconds);
        if (crash.ForwardSpeed == 0f && crash.SideSpeed == 0f)
        {
            SessionRun.Retire(index);
            if (traffic != null)
                traffic.Retired = true;
        }
    }

    internal void SetCollisionsEnabled(bool enabled)
    {
        collisionsEnabled = enabled;
        if (enabled)
        {
            collisions?.ClearContacts();
            pitCollisions?.ClearContacts();
            backExitCollisions?.ClearContacts();
        }
    }

    private void ResolveTrackCollisions(float seconds)
    {
        if (!collisionsEnabled)
            return;

        bool allowDraftBumps = DraftBumpsActive;
        for (int i = 0; i < cars.Length; i++)
        {
            var traffic = trafficCars?[i];
            collisionParticipants[i] &= traffic?.InPit != true && (traffic?.FlatTireLaneBlend ?? 0f) == 0f;
            collisionEndDistances[i] = traffic?.TrackDistance ?? sessionDistances[i];
            if (collisionParticipants[i])
            {
                collisionTireEndLanes[i] = TireAdjustedLane(i, collisionEndDistances[i], drivingLanes[i], traffic, out _);
                collisionTireOffsets[i] = collisionTireEndLanes[i] - drivingLanes[i];
            }
            if (allowDraftBumps)
                draftBumpEligible[i] = collisionParticipants[i] && draftEligible[i] &&
                    !SessionRun.Finished(i) && traffic?.ApronEntryActive != true && traffic?.ApronExitActive != true &&
                    (traffic == null || Mathf.Min(traffic.NextEntry - traffic.TrackDistance,
                        traffic.TrackDistance - traffic.LastExit) > 15f);
            pitParticipants[i] &= traffic?.InPit == true && !traffic.InGarage && !traffic.BackExit;
            backExitParticipants[i] &= traffic?.InPit == true && !traffic.InGarage && traffic.BackExit;
            pitEnd[i] = backExitParticipants[i] ? traffic.ConnectorDistance : traffic?.PitDistance ?? 0f;
            pitLaneEnd[i] = traffic?.PitLane ?? 0f;
            pitYawEnd[i] = traffic?.PitYaw ?? 0f;
        }

        if (definition.id is BristolTrackId or MartinsvilleTrackId or IowaTrackId or PhoenixTrackId or RichmondTrackId or NorthWilkesboroTrackId)
        {
            PositionCars(previewCollisionPose: true);
            for (int i = 0; i < cars.Length; i++)
            {
                var traffic = trafficCars?[i];
                pitWorldEnd[i] = cars[i].localPosition;
                Vector3 forward = cars[i].localRotation * Vector3.forward;
                pitWorldYawEnd[i] = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
                pitWorldActive[i] &= cars[i].gameObject.activeSelf && traffic?.InGarage != true;
                pitWorldInvolved[i] |= traffic?.InPit == true;
            }
            pitCollisions.ResolveSpatial(SessionRun, seconds, pitWorldActive, pitWorldInvolved,
                pitWorldStart, pitWorldEnd, pitWorldYawStart, pitWorldYawEnd);
        }
        else
        {
            pitCollisions.Resolve(SessionRun, lapLength, seconds, pitParticipants, pitStart, pitEnd, pitLaneStart, pitLaneEnd, wrap: false, startYaw: pitYawStart, endYaw: pitYawEnd);
            backExitCollisions?.Resolve(SessionRun, lapLength, seconds, backExitParticipants,
                pitStart, pitEnd, pitLaneStart, pitLaneEnd, wrap: false);
        }
        collisions.Resolve(SessionRun, lapLength, seconds, collisionParticipants, drivingDistances,
            collisionEndDistances, collisionTireStartLanes, collisionTireEndLanes,
            draftBumpEligible: allowDraftBumps ? draftBumpEligible : null,
            strongDraftBumpTargets: allowDraftBumps ? draft.StrongDraftBumpTargets : null);
        if ((TrackState is null or SessionTrackState.TrackOpen or SessionTrackState.WhiteFlag) &&
            !BlendingStartLanes && stages?.ReleasingRestartLanes != true && stages?.ReducedFuelBurn != true)
        {
            for (int i = 0; i < cars.Length; i++)
            {
                if (collisions.CrashImpulse(i) == 0f)
                    TryApronTurnWreck(i);
            }
        }
        VehicleCollision001.Separate(lapLength, collisionParticipants, collisionEndDistances, collisionTireEndLanes, drivingWidths,
            UsesOvalTraffic007 ? raceCarHalfWidths : null);
        for (int i=0;i<cars.Length;i++)
        {
            if (!collisionParticipants[i]) continue;
            // Keep the traffic controller's lane separate from the tire-induced drift.
            drivingLanes[i] = collisionTireEndLanes[i] - collisionTireOffsets[i];
            // Preserve double-precision progress and speed/fuel telemetry; this is a contact correction.
            if (trafficCars?[i] != null)
                trafficCars[i].PreciseTrackDistance += collisionEndDistances[i]-trafficCars[i].TrackDistance;
            else sessionDistances[i] = collisionEndDistances[i];
            if (crashes[i].Active) crashes[i].Lane = drivingLanes[i];
        }
        for (int i = 0; i < cars.Length; i++)
        {
            if (allowDraftBumps)
            {
                float speedChange = collisions.DraftBumpSpeedChange(i);
                if (speedChange != 0f)
                    SessionRun.ApplyDraftBumpSpeedChange(i, speedChange);
                if (collisions.GaveDraftBump(i))
                {
                    draftBumpsGiven[i]++;
                    draft.FinishBump(i);
                }
                if (collisions.ReceivedDraftBump(i))
                    draftBumpsReceived[i]++;
            }
            if (collisions.HadImpact(i) || pitCollisions.HadImpact(i) || backExitCollisions?.HadImpact(i) == true)
                collisionFlashRemaining[i] = CollisionFlashSeconds;

            float impulse = collisions.CrashImpulse(i);
            if (impulse != 0f && !SessionRun.Finished(i))
                StartSpin(i, impulse);
            if (crashes[i].Active && collisions.HadImpact(i))
            {
                crashes[i].ContactParticipants ??= new HashSet<int>();
                AddTrackImpactPartners(i, crashes[i].ContactParticipants);
            }
        }
    }

    private void AddTrackImpactPartners(int index, HashSet<int> participants)
    {
        if (!collisionsEnabled || collisions == null) return;
        for (int other = 0; other < cars.Length; other++)
            if (collisions.HadImpactWith(index, other))
                participants.Add(other);
    }

    private void TryApronTurnWreck(int index)
    {
        var traffic = trafficCars?[index];
        if (!collisionParticipants[index] || crashes[index].Active || SessionRun.Finished(index) ||
            SessionRun.Retired(index) || traffic?.ApronEntryActive == true || traffic?.ApronExitActive == true)
            return;

        float distance = collisionEndDistances[index];
        if (lapSpeedProfile == null || lapSpeedProfile.TurnWeightAt(distance) < .5f)
            return;

        Vector2 road = RoadWidths(distance);
        float inside = customLayout?.InsideSign ?? -1f;
        float carHalfWidth = raceCarHalfWidths[index] > 0f ? raceCarHalfWidths[index] : VehicleCollision001.ContactWidth * .5f;
        // Give the inside tires some room to brush the apron before a spin can trigger.
        const float apronSpinTireMargin = .18f;
        float insideTire = drivingLanes[index] * inside + Mathf.Max(.1f, carHalfWidth - .07f);
        if (road.y <= .03f || insideTire <= road.x * .5f + apronSpinTireMargin)
            return;

        float racePace = lapSpeedProfile.SpeedAt(distance, CarSpeed(index) * SessionRun.SpeedFactor(index));
        if (SessionRun.CurrentSpeed(index) >= Mathf.Max(20f, racePace * .80f))
            StartSpin(index, inside * 3.5f, apron: true);
    }

    private void StartSpin(int index, float impulse, bool apron = false)
    {
        ref var crash = ref crashes[index];
        if (!crash.Active)
        {
            SessionRun.RecordWreck(index);
            crash.Active = true;
            crash.Cause = apron ? CrashCause.Apron : CrashCause.Contact;
            crash.ContactParticipants = null;
            crash.Lane = drivingLanes[index];
            crash.ForwardSpeed = SessionRun.CurrentSpeed(index) * .8f;
        }
        crash.SideSpeed = Mathf.Clamp(crash.SideSpeed + impulse, -16f, 16f);
        crash.Spin = -Mathf.Lerp(65f, 165f, Mathf.Abs(impulse) / 14f);
    }

    private void RecordPlateMergeAttempt(int head, int receiver, int queuedCars)
    {
        bool frontJoin = draftMerging?.IsFrontJoin == true;
        float sourceSpeed = currentTrafficSpeeds[head];
        float receiverSpeed = currentTrafficSpeeds[receiver];
        float openingSpeed = frontJoin ? sourceSpeed - receiverSpeed : receiverSpeed - sourceSpeed;
        snapshot?.RecordPlateMergeAttempt(snapshotTime + SessionStep, simulation.Cars[head].carNumber,
            simulation.Cars[receiver].carNumber, queuedCars, drivingLanes[head], drivingLanes[receiver],
            openingSpeed, draftMerging?.IsChaseMerge == true, frontJoin);
    }

    private void RecordOvalPassAttempt(int car, int blocker, int side)
    {
        // CaptureSnapshot uses the clock after advancing this simulation step.
        snapshot?.RecordOvalPassAttempt(snapshotTime + SessionStep,
            simulation.Cars[car].carNumber, simulation.Cars[blocker].carNumber, side, ovalPassingStrictness);
    }

    private void CaptureSnapshot(bool force = false)
    {
        var frame = snapshot?.BeginFrame(snapshotTime, cars.Length, force);
        if (frame == null)
            return;

        // Interval aggregates preserve spikes between the 10 Hz simulation samples.
        // At fast playback a sample can contain no newly completed render frames.
        frame.renderFrameNumber = Time.frameCount;
        frame.renderFramesSinceSample = timingFrames;
        frame.averageFrameMilliseconds = timingFrames > 0 ? timingFrameTotal / timingFrames : 0;
        frame.maxFrameMilliseconds = timingFrameMax;
        frame.connectorBuildCount = connectorBuildCount;
        frame.connectorBuildMilliseconds = connectorBuildMilliseconds;
        connectorBuildCount = 0;
        connectorBuildMilliseconds = 0;
        frame.mergeCheckCount = timingMergeChecks;
        frame.mergeCheckMilliseconds = timingMergeTotal;
        frame.maxMergeCheckMilliseconds = timingMergeMax;
        frame.mergeChecksDeferred = timingMergeDeferred;
        frame.mergePredictionSamples = timingPredictionSamples;
        timingFrames = timingMergeChecks = timingMergeDeferred = timingPredictionSamples = 0;
        timingFrameTotal = timingFrameMax = timingMergeTotal = timingMergeMax = 0;
        frame.sessionSeconds = SessionRun.Elapsed;
        frame.playbackSpeed = sessionSpeed;
        frame.ovalPassingStrictness = CanAdjustPassingCriteria ? ovalPassingStrictness : -1;
        frame.flag = TrackState?.ToString() ?? "Unspecified";
        frame.pitsOpen = PitRoadOpen ?? true;
        frame.paceCar = default;
        if (stages?.HasPaceCar == true && paceCar != null && stages.ReducedFuelBurn)
        {
            var pace = stages.PacePosition();
            int leader = stages.PaceLeaderIndex;
            frame.paceCar = cautionPaceSample;
            frame.paceCar.active = paceCar.gameObject.activeInHierarchy;
            frame.paceCar.position = paceCar.localPosition;
            double elapsed = snapshotTime - previousPaceSnapshotTime;
            frame.paceCar.mph = elapsed > 0d
                ? (float)(Vector3.Distance(previousPaceSnapshotPosition, paceCar.localPosition) / elapsed
                    * 3600d / TrackDefinition.MetersPerMile)
                : frame.paceCar.allowedMph;
            previousPaceSnapshotPosition = paceCar.localPosition;
            previousPaceSnapshotTime = snapshotTime;
            frame.paceCar.routeMeters = pace.Distance;
            frame.paceCar.onPitRoad = pace.OnPitRoad;
            frame.paceCar.returning = stages.PaceReturning;
            frame.paceCar.doubleFile = stages.DoubleFile;
            frame.paceCar.laneMeters = pace.OnPitRoad
                ? pace.ParkingBlend * (definition.pitRoad.widthMeters * .5f + 1.5f)
                : TrackLaneAtPoint(pace.Distance, paceCar.localPosition);
            frame.paceCar.leaderCarNumber = simulation.Cars[leader].carNumber;
            frame.paceCar.leaderGapMeters = pace.OnPitRoad ? 0f : pace.Distance - trafficCars[leader].TrackDistance;
        }
        else
            previousPaceSnapshotTime = double.NaN;
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
                var grid = raceStart.GridPosition(i, FormationHalfLane);
                inPit = grid.OnPitRoad;
                distance = grid.Distance;
                lane = inPit ? grid.Lane : FormationTrackLane(distance, drivingLanesReady ? drivingLanes[i] : grid.Lane);
            }
            else if (!inPit)
                lane = TireAdjustedLane(i, distance, lane, traffic, out _);
            string passPhase = draftPassing?.SnapshotPhase(i);
            bool blocking = draftLeaderBlock?.Leader == i;
            float draftBoost = DraftSpeedBoost(i, distance, out float packWakeBoost);
            frame.cars[i] = new TrackSnapshot.CarSample
            {
                preferredLineSpeedFactor = PreferredLineSpeedFactor(i,distance),
                preferredLaneMeters = PreferredLaneOffset(i, RoadWidths(distance).x, float.PositiveInfinity, racingLine(distance)),
                draftBoostMph = draftBoost * 3600f / TrackDefinition.MetersPerMile,
                packWakeBoostMph = packWakeBoost * 3600f / TrackDefinition.MetersPerMile,
                trailingSpeedBoostMph = trailingSpeedBoosts[i] * 3600f / TrackDefinition.MetersPerMile,
                regrouping = draftMerging?.Regrouping[i] == true,
                draftBumpsGiven = draftBumpsGiven[i],
                draftBumpsReceived = draftBumpsReceived[i],
                draftPassPhase = passPhase,
                draftPassTargetNumber = passPhase != null ? simulation.Cars[draftPassing.SnapshotTarget(i)].carNumber : -1,
                draftBlockTargetNumber = blocking ? simulation.Cars[draftLeaderBlock.Target].carNumber : -1,
                draftBlockTargetLaneMeters = blocking ? draftLeaderBlock.TargetLane : 0f,
                throttle = SessionRun.ThrottleInput(i),
                brake = SessionRun.BrakeInput(i),
                number = simulation.Cars[i].carNumber,
                thermalTrackPositionMeters = SessionRun.ThermalTrackPosition(i),
                thermalOnTrack = SessionRun.ThermalOnTrack(i),
                visible = cars[i].gameObject.activeSelf,
                inPit = inPit,
                retired = SessionRun.Retired(i),
                spinning = crashes[i].Active && crashes[i].Cause != CrashCause.FrontTireWear && !SessionRun.Retired(i),
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
                position = cars[i].localPosition,
                rotation = cars[i].localRotation,
                rearAxlePosition = cars[i].localPosition -
                    cars[i].localRotation * Vector3.forward * RearAxleHeading.CenterToRearAxle,
                headingDegrees = cars[i].localEulerAngles.y,
                pitManeuver = traffic?.Maneuver ?? false,
                pitArrivalManeuver = traffic?.ArrivalManeuver ?? false,
                pitYawDegrees = traffic?.PitYaw ?? 0f,
                pitManeuverStartMeters = traffic?.ManeuverStart ?? 0f,
                pitManeuverLengthMeters = traffic?.ManeuverLength ?? 0f,
                pitManeuverTargetLaneMeters = traffic?.ManeuverTo ?? 0f,
                waitingForPitExit = traffic?.WaitingForExitMerge ?? false,
                pitExitCleared = traffic?.ExitMergeGranted ?? false,
                pitExitHoldMeters = traffic?.ExitHoldDistance ?? 0f,
                pitExitBlockedByIndex = traffic?.WaitingForExitMerge == true ? traffic.ExitBlockedBy : -1,
                pitExitConflictSeconds = traffic?.WaitingForExitMerge == true ? traffic.ExitConflictSeconds : 0f,
                apronExitActive = traffic?.ApronExitActive ?? false,
                apronEntryActive = traffic?.ApronEntryActive ?? false,
                pitFrontEntry = traffic?.FrontEntry ?? false,
                pitBackExit = traffic?.BackExit ?? false,
                pitConnectorMeters = traffic?.ConnectorDistance ?? 0f,
                waitingForStallDeparture = traffic?.WaitingForStallDeparture ?? false,
                stallDepartureBlockedByIndex = traffic?.DepartureBlockedByIndex ?? -1,
                stallDepartureBlockReason = traffic?.DepartureBlockReason,
                pitMovementBlockedThisStep = traffic?.MovementBlockedThisStep ?? false,
                pitMovementBlockedByIndex = traffic?.MovementBlockedByIndex ?? -1,
                pitMovementBlockedByCarNumber = traffic != null && traffic.MovementBlockedByIndex >= 0 &&
                    traffic.MovementBlockedByIndex < cars.Length
                    ? simulation.Cars[traffic.MovementBlockedByIndex].carNumber : -1,
                pitMovementBlockReason = traffic?.MovementBlockReason,
                pitMovementBlockSessionSeconds = traffic?.MovementBlockTime ?? -1f,
                pitMovementBlockClearanceMeters = traffic?.MovementBlockClearance ?? 0f,
                pitMovementBlockPredictionSeconds = traffic?.MovementBlockPredictionSeconds ?? 0f,
                pitMovementBlockCount = traffic?.MovementBlockCount ?? 0,
                stallEntryReservationOwner = traffic?.StallEntryReservationOwner ?? -1,
                stallEntryHoldMeters = traffic == null || float.IsInfinity(traffic.StallEntryHoldDistance) ? -1f : traffic.StallEntryHoldDistance,
                apronMergeGranted = traffic?.ApronMergeGranted ?? false,
                apronMergeStartMeters = traffic?.ApronMergeStart ?? 0f,
                apronMergeEndMeters = traffic?.ApronMergeEnd ?? 0f
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

    private float SpeedAt(int index, float distance, out float preferredLineSpeedFactor, bool includeDraft = true)
    {
        preferredLineSpeedFactor = PreferredLineSpeedFactor(index, distance);
        float speed = SetupAdjustedSpeedAt(index, distance, includeDraft ? DraftSpeedBoost(index, distance) : 0f);
        return speed * preferredLineSpeedFactor + trailingSpeedBoosts[index];
    }

    private float SetupAdjustedSpeedAt(int index, float distance, float draftBoost)
    {
        float baseSpeed = CarSpeed(index) * SessionRun.SpeedFactor(index) + draftBoost;
        float speed = lapSpeedProfile?.SpeedAt(distance, baseSpeed) ?? baseSpeed;
        return SessionRun.SetupAdjustedSpeed(index, distance, speed);
    }

    private float ForecastOvalPass(int car, int blocker, float destinationLane, float gap)
    {
        float blockerDistance = drivingDistances[blocker];
        float blockerLane = collisionStartLanes[blocker];
        var blockerControl = trafficLaneControl[blocker];
        float contactWidth = VehicleCollision001.TrackContactWidth(raceCarHalfWidths, car, blocker);
        bool followingPreferred = blockerControl.PassSide == 0 && !blockerControl.ReturningToLine &&
            Mathf.Abs(blockerLane - PreferredLaneAt(blocker, blockerDistance)) < contactWidth * .5f;
        return VehicleBehavior007.ForecastPass(
            (car, drivingDistances[car], collisionStartLanes[car], currentTrafficSpeeds[car]),
            (blocker, blockerDistance, blockerLane, currentTrafficSpeeds[blocker]),
            destinationLane, gap, passingPaceAt ??= PassingPaceAt,
            followingPreferred ? passingReferenceLaneAt ??= PreferredLaneAt : null, contactWidth,
            passingLaneRangeAt ??= OvalPassingLaneRangeAt, OvalPassingEase);
    }

    private (float TargetSpeed, float BrakingRate) PassingPaceAt(int index, float distance, float lane)
    {
        // A car moving away from its current lane loses the draft it came from.
        float carWidth = VehicleCollision001.TrackContactWidth(raceCarHalfWidths, index, index);
        float retainedDraft = 1f - Mathf.Clamp01(Mathf.Abs(lane - collisionStartLanes[index]) / carWidth);
        float speed = SetupAdjustedSpeedAt(index, distance, DraftSpeedBoost(index, distance) * retainedDraft);
        float damageFactor = 1f - (SessionRun.Vehicle(index)?.Damage ?? 0f) / 100f;
        float lineFactor = OffLineSpeedFactorAt(index, distance, lane);
        float braking = (lapSpeedProfile?.BrakingLimitAt(distance) ?? 0f) * trafficBrakingFactors[index];
        return (speed * lineFactor * damageFactor, braking);
    }

    private float DraftSpeedBoost(int index, float distance) => DraftSpeedBoost(index, distance, out _);

    private float DraftSpeedBoost(int index, float distance, out float packWakeBoost)
    {
        packWakeBoost = 0f;
        if (!DraftSpeedActive || lapSpeedProfile?.BrakingLimitAt(distance) > 0f)
            return 0f;

        packWakeBoost = draftMerging?.PackWakeBoost(index) ?? 0f;
        return draft.SpeedBoost(index) + packWakeBoost;
    }

    private float PreferredLineSpeedFactor(int index, float distance)
    {
        var traffic=trafficCars?[index];
        if (!(behaviorModel is VehicleBehavior003) || definition.restrictorPlate || !drivingLanesReady || lapSpeedProfile == null ||
            traffic?.InPit == true || traffic?.ApronExitActive == true || traffic?.ApronEntryActive == true || crashes[index].Active ||
            (traffic?.FlatTireLaneBlend ?? 0f)>0f || stages?.ReducedFuelBurn == true ||
            practice?.State == SessionTrackState.Caution) return 1f;
        // Exclude the pit entry/exit merge corridor, where the prescribed line is different.
        if (traffic != null && Mathf.Min(traffic.NextEntry-distance,distance-traffic.LastExit)<15f) return 1f;
        return OffLineSpeedFactorAt(index, distance, drivingLanes[index]);
    }

    private float OffLineSpeedFactorAt(int index, float distance, float lane)
    {
        float turnWeight = lapSpeedProfile?.TurnWeightAt(distance) ?? 0f;
        if (turnWeight <= 0f) return 1f;
        return VehicleBehavior003.OffLineSpeedFactor(lane - PreferredLaneAt(index, distance), turnWeight);
    }

    private float PreferredLaneAt(int index, float distance) =>
        PreferredLaneOffset(index, RoadWidths(distance).x, float.PositiveInfinity, racingLine(distance));

    private float CarSpeed(int index) => baseSpeeds[index];

    private float TireAdjustedLane(int index, float distance, float lane, TrackPitTraffic.Car traffic, out float yawDegrees)
    {
        yawDegrees = 0f;
        if (SessionRun == null || !cars[index].gameObject.activeSelf || iowaRecoveredCars[index].HasValue ||
            crashes[index].Active || SessionRun.Retired(index) || SessionRun.Finished(index) ||
            traffic?.ApronEntryActive == true || traffic?.ApronExitActive == true ||
            (traffic?.FlatTireLaneBlend ?? 0f) > 0f || SessionRun.FlatTireMask(index) != 0 ||
            TrackState is not (null or SessionTrackState.TrackOpen or SessionTrackState.WhiteFlag))
            return lane;

        return TireHandlingLaneAt(index, distance, lane, out yawDegrees);
    }

    private float TireHandlingLaneAt(int index, float distance, float lane, out float yawDegrees)
    {
        yawDegrees = 0f;
        float turn = SessionRun.TireHandlingTurnAt(distance);
        float speed = SessionRun.CurrentSpeed(index);
        float turningLoad = turn * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(12f, 32f, speed));
        if (turningLoad == 0f)
            return lane;

        var car = SessionRun.Vehicle(index);
        float rearSlip = AxleTireSlip(car, 2);
        yawDegrees = 18f * turningLoad * rearSlip;

        float frontSlip = AxleTireSlip(car, 0);
        if (frontSlip == 0f)
            return lane;

        float outward = -Mathf.Sign(turningLoad);
        float halfWidth = raceCarHalfWidths[index] > 0f ? raceCarHalfWidths[index] : VehicleCollision001.ContactWidth * .5f;
        float room = Mathf.Max(0f, RoadWidths(distance).x * .5f - halfWidth - lane * outward);
        return lane + outward * Mathf.Min(room, 2.4f * Mathf.Abs(turningLoad) * frontSlip);
    }

    private static float AxleTireSlip(TeamCar car, int firstTire)
    {
        // Cold grip affects pace. This handling loss comes from wear and overheating.
        float grip = 0f;
        for (int corner = firstTire; corner < firstTire + 2; corner++)
        {
            var tire = car?.GetTire(corner);
            float temperature = Mathf.Max(185f, TireTemperatureModel.Fahrenheit(tire?.temperature ?? 0f));
            grip += VehicleSpeed003.TireGripFactor(tire?.wear ?? 0f, temperature);
        }
        // Normalize to the warm-tire wear curve's maximum grip loss.
        return Mathf.Clamp01((1f - grip * .5f) / .36f);
    }

    private void PositionCars(bool previewCollisionPose = false)
    {
        if (!previewCollisionPose && raceStart != null)
        {
            var pace = stages?.HasPaceCar == true ? stages.PacePosition() : raceStart.PacePosition();
            if (!PositionRichmondPaceBay(pace))
            {
                if (pace.OnPitRoad)
                    PositionPitCar(paceCar, pace.Distance, pace.ParkingBlend * (definition.pitRoad.widthMeters * 0.5f + 1.5f),
                        isRaceFormation: !raceReleased && !raceStart.PaceReturning);
                else if (!PositionCautionPaceExit(pace.Distance) && !PositionPacePitApproach(pace.Distance))
                {
                    if (raceReleased)
                        PositionTrackCar(paceCar, pace.Distance,
                            stages?.HasPaceCar == true ? cautionPaceAvoidanceLane : 0f);
                    else
                        PositionFormationTrackCar(paceCar, pace.Distance, 0f);
                }
            }
            paceWheels?.Sample(paceCar, false);
            paceLights.SetActive(!RichmondPaceIsParked &&
                (stages?.HasPaceCar == true ? stages.LightsVisible : raceStart.LightsVisible));
        }
        for (int i = 0; i < cars.Length; i++)
        {
            tireYawAngles[i] = 0f;
            if (!previewCollisionPose)
                chasePoseBeforeYaw[i] = null;
            if (PositionIowaRecoveredCar(i) || i == 0 && PositionNorthWilkesboroGarageCar(previewCollisionPose))
                continue;

            if (raceStart != null && !raceReleased)
            {
                float halfLane = FormationHalfLane;
                var grid = raceStart.GridPosition(i, halfLane);
                if (grid.OnPitRoad)
                    PositionPitCar(cars[i], grid.Distance, grid.Lane, isRaceFormation: true);
                else
                    PositionFormationTrackCar(cars[i], grid.Distance, drivingLanesReady ? drivingLanes[i] : grid.Lane);
                continue;
            }

            var traffic = isQualifying ? (i == 0 ? qualifying?.Car : null) : trafficCars?[i];
            if (traffic?.InPit == true)
            {
                PositionPitCar(cars[i], traffic.PitDistance, traffic.PitLane, traffic);
                continue;
            }

            float distance = traffic?.TrackDistance ?? (sessionDistances == null ? elapsed * CarSpeed(i) + lapLength * i / cars.Length : sessionDistances[i]);
            if (crashes[i].Active)
            {
                if (crashes[i].Cause == CrashCause.FrontTireWear)
                {
                    var straight = crashes[i].Straight;
                    cars[i].SetLocalPositionAndRotation(straight.Position, straight.Rotation);
                    continue;
                }

                PositionTrackCar(cars[i], distance, crashes[i].Lane);
                if (!previewCollisionPose && crashes[i].Yaw != 0f)
                    chasePoseBeforeYaw[i] = new Pose(cars[i].localPosition, cars[i].localRotation);
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
            PositionTrackCar(cars[i], distance, TireAdjustedLane(i, distance, lane, traffic, out tireYawAngles[i]));
        }

        for (int i = 0; i < cars.Length; i++)
        {
            if (cars[i] == null || i == 0 && NorthWilkesboroGarageOffPitRoute) continue;
            bool spinning = crashes[i].Active;
            if (previewCollisionPose)
            {
                // Predict the swept collision heading without consuming the
                // displacement that should be sampled after contact resolution.
                var predictedHeading = carHeadings[i];
                predictedHeading.Apply(cars[i], spinning);
            }
            else
                carHeadings[i].Apply(cars[i], spinning);

            bool tireYaw = tireYawAngles[i] != 0f;
            if (tireYaw)
            {
                if (!previewCollisionPose)
                    chasePoseBeforeYaw[i] = new Pose(cars[i].localPosition, cars[i].localRotation);
                Vector3 frontAxle = raceWheels[i]?.FrontAxleCenter ?? Vector3.forward * RearAxleHeading.CenterToRearAxle;
                RaceWheelAnimation.ApplyFrontAxleYaw(cars[i], frontAxle, tireYawAngles[i]);
            }

            if (!previewCollisionPose)
            {
                float steeringBlend = 0f;
                if (!spinning && kinkSteeringBlend != null)
                {
                    var traffic = isQualifying ? (i == 0 ? qualifying?.Car : null) : trafficCars?[i];
                    if (traffic != null && !traffic.InPit && !traffic.ApronEntryActive && !traffic.ApronExitActive
                        && traffic.FlatTireLaneBlend == 0f)
                        steeringBlend = kinkSteeringBlend(traffic.TrackDistance);
                }
                if (tireYaw)
                    raceWheels[i]?.SampleTireYaw(cars[i]);
                else
                    raceWheels[i]?.Sample(cars[i], spinning, steeringBlend);
            }
        }
    }

    private bool UsesAuthoredApronHandoffs =>
        definition.id is IowaTrackId or PhoenixTrackId or RichmondTrackId or NorthWilkesboroTrackId ||
        definition.id == MichiganTrackId && importedTrackGeometry;
    private bool UsesKansasLowLanePitRoute => definition.id == KansasTrackId && importedTrackGeometry;
    private bool UsesAuthoredLowLanePitRoute => UsesKansasLowLanePitRoute || definition.id == DarlingtonTrackId;
    private bool UsesEarlyLowLanePitApproach => UsesAuthoredLowLanePitRoute || definition.id == GatewayTrackId;
    private bool UsesEarlyApronPitApproach => definition.id is MichiganTrackId or PoconoTrackId;
    private bool UsesSettledPlayerPitApproach => UsesAuthoredApronHandoffs ||
        UsesEarlyLowLanePitApproach || UsesEarlyApronPitApproach;
    private bool UsesSharedPitConnectorSurface => !importedTrackGeometry && definition.majorStyle == "Oval" &&
        definition.id is not (BristolTrackId or MartinsvilleTrackId) && !UsesAuthoredApronHandoffs && !UsesEarlyLowLanePitApproach;

    private bool? continuousApron;
    private bool ApronExitAvailable(float distance)
    {
        if (customLayout == null || pitRoadOutside || UsesEarlyLowLanePitApproach)
            return false;
        if (!continuousApron.HasValue)
        {
            continuousApron = true;
            for (int sample = 0; sample < 128; sample++)
                if (RoadWidths(sample * lapLength / 128f).y < 2.2f) { continuousApron = false; break; }
        }
        return continuousApron.Value && RoadWidths(distance).y >= 2.2f;
    }

    private float ApronLane(float distance)
    {
        Vector2 widths = RoadWidths(distance);
        float offset = LimitRichmondApronOffset(distance, widths.y * .5f);
        return (customLayout?.InsideSign ?? -1f) * (widths.x * .5f + offset);
    }

    private const float PhoenixPitApronTransition = 70f;

    private float PitApronLane(float distance, float handoff)
    {
        float nominal = ApronLane(distance);
        float distanceFromHandoff = Mathf.Abs(distance - handoff);
        if (definition.id != PhoenixTrackId || distanceFromHandoff >= PhoenixPitApronTransition)
            return nominal;

        // The authored connector ends are 6.25 m inside the racing edge.
        // Keep the car on paved apron while easing back to the regular lane.
        Vector2 widths = RoadWidths(distance);
        float authoredOffset = Mathf.Min(6.25f, Mathf.Max(0f, widths.y - 1.1f));
        float authoredLane = customLayout.InsideSign * (widths.x * .5f + authoredOffset);
        return Mathf.SmoothStep(authoredLane, nominal, distanceFromHandoff / PhoenixPitApronTransition);
    }

    private float PitApproachLane(int index, float distance, float handoff)
    {
        if (UsesEarlyLowLanePitApproach) return pitEntryTrackLane;
        float nominal = PitApronLane(distance, handoff);
        float remaining = handoff - distance;
        const float anticipationStartMeters = 160f;
        if (definition.id != PoconoTrackId || index != 0 || isQualifying || remaining <= 0f ||
            remaining >= anticipationStartMeters || !ApronExitAvailable(distance))
            return nominal;

        // Anticipate Pocono's widening before the pit mouth without leaving the current apron.
        const float anticipationMeters = 30f;
        const float carClearance = 1.05f;
        float lookahead = anticipationMeters * Mathf.InverseLerp(anticipationStartMeters, 120f, remaining);
        float anticipatedStation = Mathf.Min(handoff, distance + lookahead);
        float inside = customLayout.InsideSign;
        Vector2 widths = RoadWidths(distance);
        float racingEdge = widths.x * .5f;
        float inwardLane = PitApronLane(anticipatedStation, handoff) * inside;
        return inside * Mathf.Clamp(inwardLane, racingEdge + carClearance, racingEdge + widths.y - carClearance);
    }

    private bool PitEntryReady(TrackPitTraffic.Car car, float stepFraction)
    {
        int index = car.FieldIndex;
        if (index >= 0 && (car.ApronEntryActive || index == 0 && (UsesEarlyLowLanePitApproach ||
            (UsesAuthoredApronHandoffs || UsesEarlyApronPitApproach) && ApronExitAvailable(car.NextEntry))))
        {
            float laneAtEntry = Mathf.Lerp(collisionStartLanes[index], drivingLanes[index], stepFraction);
            if (Mathf.Abs(laneAtEntry - PitApproachLane(index, car.NextEntry, car.NextEntry)) > .25f) return false;
        }

        // The shared admission check already reserves the mouth. Richmond's
        // cooldown route needs extra room at its narrowing connector.
        return definition.id != RichmondTrackId || stages == null || car.FieldIndex < 0 ||
            !SessionRun.Finished(car.FieldIndex) || pitTraffic.EntranceClear(car, clearance: 12f);
    }

    private float LowestLane(float distance) => (customLayout?.InsideSign ?? -1f) *
        Mathf.Max(0f, RoadWidths(distance).x * .5f - 1.05f);

    private bool StraightForApronMerge(float distance)
    {
        TrackPoint(distance - 10f, 0f, out var a, out _);
        TrackPoint(distance + 10f, 0f, out var b, out _);
        return Vector3.Angle(a, b) < 2f && ApronExitAvailable(distance);
    }

    private void PrepareApronExit(TrackPitTraffic.Car car)
    {
        if (car == null || car.InPit || float.IsInfinity(car.LastExit) || !ApronExitAvailable(car.LastExit)) return;
        if (car.ApronExitInitialized && car.ApronExitId == car.LastExit) return;
        car.ApronExitInitialized = true;
        car.ApronExitId = car.LastExit;
        car.ApronExitActive = true;
        car.ApronMergeGranted = false;
        float mergeSearchStart = definition.id == PhoenixTrackId ? car.LastExit + PhoenixPitApronTransition : car.LastExit;
        FindApronStraight(car, mergeSearchStart);
    }

    private float LimitNorthWilkesboroApronSpeed(TrackPitTraffic.Car car, float speed)
    {
        if (definition.id != NorthWilkesboroTrackId)
            return speed;

        // Continue the limit from the pit speed line through the exit connector.
        // The short straights cannot fit the lateral crossing at racing speed.
        if (car.InPit)
        {
            float limitEnd = pitTraffic.RoadStart + pitTraffic.RoadLength * definition.pitRoad.speedLimitEnd;
            return car.PitDistance >= limitEnd && ApronExitAvailable(pitTraffic.ExitDistance)
                ? Mathf.Min(speed, pitTraffic.SpeedLimit) : speed;
        }

        PrepareApronExit(car);
        return car.ApronExitActive ? Mathf.Min(speed, pitTraffic.SpeedLimit) : speed;
    }

    private const float ApronMergeLateralSpeed = 1.5f;

    private void AdvanceApronExitLane(int index, TrackPitTraffic.Car car, float distance,
        float startLane, float targetLane, float seconds)
    {
        bool phoenixConnectorDeparture = definition.id == PhoenixTrackId && !car.ApronMergeGranted &&
            distance - car.LastExit < PhoenixPitApronTransition;
        drivingLanes[index] = phoenixConnectorDeparture ? targetLane :
            Mathf.MoveTowards(startLane, targetLane, ApronMergeLateralSpeed * seconds);
        if (car.ApronMergeGranted && Mathf.Abs(drivingLanes[index] - LowestLane(distance)) < .05f)
            car.ApronExitActive = false;
    }

    private void FindApronStraight(TrackPitTraffic.Car car, float start)
    {
        car.ApronMergeStart = ApronExitPlan.NextStraight(start, lapLength, StraightForApronMerge, out car.ApronMergeEnd);
    }

    private void SeekQueuedPitGap(int index, int blocker, float lane, float pitLane, float seconds)
    {
        float distance = drivingDistances[index];
        if (definition.majorStyle != "Oval" || (lapSpeedProfile?.TurnWeightAt(distance) ?? 0f) < .25f)
            return;

        float higherDirection = Mathf.Sign(lane - pitLane);
        float higherLimit = Mathf.Max(0f, drivingWidths[index] * .5f - VehicleCollision001.TrackContactWidth(raceCarHalfWidths, index, index) * .5f);
        float higherLane = higherDirection * higherLimit;
        float nextLane = Mathf.MoveTowards(lane, higherLane, VehicleBehavior003.MaximumLateralSpeed * seconds);
        if (higherDirection * (nextLane - lane) > 0f)
        {
            if (!ApronLateralClear(index, lane, nextLane, false, out _, allowSeparating: true))
                return;
            drivingLanes[index] = nextLane;
        }
        else if (higherDirection * lane < higherLimit - .25f)
            return;

        // Create a small speed differential in the high lane. Try the
        // pitward move first each step so the car takes the first clear gap.
        float pace = Mathf.Max(stepSpeeds[index], trafficPaceSpeeds[index]);
        float gapSpeed = Mathf.Max(pace - 2f, currentTrafficSpeeds[blocker] - 1.25f);
        float coastSpeed = currentTrafficSpeeds[index] - RaceSessionRun.CoastingDeceleration * seconds;
        stepSpeeds[index] = Mathf.Min(stepSpeeds[index], Mathf.Max(gapSpeed, coastSpeed));
    }

    private bool ApronLateralClear(int index, float from, float to, bool returningAfterFinish, out int yieldTo,
        bool allowSeparating = false)
    {
        yieldTo = -1;
        bool clear = true;
        float direction = Mathf.Sign(to - from);
        float laneTolerance = returningAfterFinish ? .35f : .01f;
        for (int j = 0; j < cars.Length; j++)
        {
            if (j == index || !onTrack[j]) continue;
            float low = Mathf.Min(from, to) - 2.1f, high = Mathf.Max(from, to) + 2.1f;
            if (drivingLanes[j] < low || drivingLanes[j] > high) continue;
            float gap = Mathf.Repeat(drivingDistances[j] - drivingDistances[index] + lapLength * .5f, lapLength) - lapLength * .5f;
            float future = gap + (currentTrafficSpeeds[j] - currentTrafficSpeeds[index]) * .5f;
            if (Mathf.Min(Mathf.Abs(gap), Mathf.Abs(future)) >= 4f && gap * future > 0f) continue;

            if (allowSeparating && Mathf.Abs(collisionStartLanes[j] - to) >
                Mathf.Abs(collisionStartLanes[j] - from) &&
                (collisionStartLanes[j] - from) * (collisionStartLanes[j] - to) >= 0f)
                continue;

            clear = false;
            if (!returningAfterFinish || to == from)
            {
                yieldTo = j;
                return false;
            }
            float relativeLane = direction * (collisionStartLanes[j] - from);
            // A following car opens the cooldown gap. Use the index only to
            // break a tie when the cars are actually abreast.
            bool sameLanePriority = gap > .05f || Mathf.Abs(gap) <= .05f && j < index;
            if (relativeLane > laneTolerance || (Mathf.Abs(relativeLane) <= laneTolerance && sameLanePriority))
            {
                yieldTo = j;
                return false;
            }
        }
        return clear;
    }

    private bool ApronGapClear(int index, float distance, float seconds)
    {
        float lane = LowestLane(distance);
        float speed = SessionRun.CurrentSpeed(index);
        for (int j = 0; j < cars.Length; j++)
        {
            if (j == index || !cars[j].gameObject.activeSelf || trafficCars?[j] == null || trafficCars[j].InGarage || trafficCars[j].InPit) continue;
            // Only cars overlapping the destination lane or moving toward it matter.
            if (Mathf.Abs(drivingLanes[j] - lane) > 2.1f && Mathf.Abs(targetLanes[j] - lane) > 2.1f) continue;
            float gap = Mathf.Repeat(trafficCars[j].TrackDistance - distance + lapLength * .5f, lapLength) - lapLength * .5f;
            if (ApronExitPlan.GapConflict(gap, SessionRun.CurrentSpeed(j) - speed, seconds)) return false;
        }
        return true;
    }

    private float ApronExitTarget(int i, float distance, TrackPitTraffic.Car car)
    {
        float departureLane = PitApronLane(distance, car.LastExit);
        if (!car.ApronMergeGranted)
        {
            float speed = Mathf.Max(1f, SessionRun.CurrentSpeed(i));
            float mergeSeconds = Mathf.Abs(departureLane - LowestLane(distance)) / ApronMergeLateralSpeed + .75f;
            if (distance > car.ApronMergeEnd - speed * mergeSeconds)
                FindApronStraight(car, car.ApronMergeEnd + 5f);
            if (distance >= car.ApronMergeStart && distance + speed * mergeSeconds <= car.ApronMergeEnd &&
                ApronGapClear(i, distance, mergeSeconds)) car.ApronMergeGranted = true;
        }
        return car.ApronMergeGranted ? LowestLane(distance) : departureLane;
    }

    private float TargetDrivingLane(int i, float distance, TrackPitTraffic.Car traffic)
    {
        if (traffic != null)
        {
            float speed = SessionRun?.CurrentSpeed(i) ?? 40f;
            float approach = Mathf.Max(260f, speed * 6f);
            bool settledPitApproachPlayer = UsesSettledPlayerPitApproach && i == 0;
            bool cooldown = stages != null && SessionRun?.Finished(i) == true;
            bool wideApronCooldown = cooldown && (definition.id is PhoenixTrackId or RichmondTrackId or NorthWilkesboroTrackId);
            if (settledPitApproachPlayer && !float.IsInfinity(traffic.NextEntry))
            {
                // Reserve time to cross the actual width and settle in the attachment lane.
                float laneChangeSeconds = Mathf.Abs(drivingLanes[i] - PitApproachLane(i, distance, traffic.NextEntry)) /
                    VehicleBehavior003.MaximumLateralSpeed;
                approach = Mathf.Max(approach, speed * (laneChangeSeconds + 1.25f));
            }
            // The finish can leave a tightly bunched field. Give the cooldown queue
            // time to open gaps across the wide apron before it reaches pit entry.
            if (wideApronCooldown)
                approach = Mathf.Max(approach, speed * 16f);
            if (cooldown && definition.id == SouthBostonTrackId && !float.IsInfinity(traffic.NextEntry))
            {
                // The authored lap reaches Turn 2 at 38% and the backstretch at about 52%.
                const float turn2Fraction = .38f;
                approach = Mathf.Repeat(traffic.NextEntry - lapLength * turn2Fraction, lapLength);
            }
            else if (cooldown)
                approach = Mathf.Max(approach, speed * 12f);
            bool queuedStop = pitStops?.IsRequested(i) == true;
            if (queuedStop)
                approach = Mathf.Max(approach, speed * 12f);

            // Continue the committed approach when braking shortens the lookahead.
            bool continueApproach = (queuedStop || settledPitApproachPlayer || cooldown) && traffic.ApronEntryActive;
            traffic.ApronEntryActive = !traffic.InPit && !float.IsInfinity(traffic.NextEntry) &&
                traffic.NextEntry >= distance && (continueApproach || traffic.NextEntry - distance <= approach) &&
                (UsesEarlyLowLanePitApproach || ApronExitAvailable(traffic.NextEntry));
            if (traffic.ApronEntryActive) return PitApproachLane(i, distance, traffic.NextEntry);
        }
        PrepareApronExit(traffic);
        if (traffic?.ApronExitActive == true) return ApronExitTarget(i, distance, traffic);
        float toMerge = traffic == null ? float.PositiveInfinity
            : Mathf.Min(traffic.NextEntry - distance, distance - traffic.LastExit);
        float mergeLength = definition.id == BristolTrackId ? 60f : 15f;
        bool useBottomLaneMerge = traffic != null && UsesBottomLanePitMerge(definition.id) && toMerge < mergeLength;
        bool ovalTraffic = UsesOvalTraffic007;
        bool blendStart = BlendingStartLanes && (!ovalTraffic || !drivingLanesReady);
        float gridLane = blendStart ? raceStart.GridPosition(i, FormationHalfLane).Lane : 0f;
        float lane;
        if (IgnorePreferredDrivingLine)
        {
            // Lane variation is applied by the traffic controller after choosing a target.
            lane = drivingLanesReady && traffic?.InPit != true
                ? drivingLanes[i] - (draft?.AppliedLaneOffsets[i] ?? 0f)
                : gridLane;
        }
        else
        {
            lane = PreferredLaneOffset(i, RoadWidths(distance).x,
                useBottomLaneMerge ? float.PositiveInfinity : toMerge, racingLine(distance));
            if (blendStart)
                lane = Mathf.Lerp(gridLane, lane, SessionRun.Elapsed / 3f);
        }
        if (stages != null && (!ovalTraffic || stages.ReducedFuelBurn))
            lane = stages.Lane(i, lane, Mathf.Min(1.35f, RoadWidths(distance).x * 0.2f), customLayout?.InsideSign ?? -1f);
        if (traffic != null && traffic.FlatTireLaneBlend > 0f)
            lane = Mathf.Lerp(lane, (customLayout?.InsideSign ?? -1f) * Mathf.Max(0f, RoadWidths(distance).x * 0.5f - 1.1f), traffic.FlatTireLaneBlend);
        if (useBottomLaneMerge)
        {
            bool entering = traffic.NextEntry - distance <= distance - traffic.LastExit;
            float mergeLane = entering ? pitEntryTrackLane : pitExitTrackLane;
            if (definition.id == BristolTrackId)
            {
                mergeLane = entering ? (pitTraffic.TakesFrontEntry(traffic) ? bristolFrontEntryLane : pitEntryTrackLane)
                    : (Mathf.Abs(Mathf.DeltaAngle(traffic.LastExit / lapLength * 360f,
                        bristolBackExitTrackDistance / lapLength * 360f)) < 1f ? bristolBackExitLane : pitExitTrackLane);
            }
            lane = Mathf.Lerp(mergeLane, lane, Mathf.SmoothStep(0f, 1f, Mathf.Max(0f, toMerge - 5f) / (mergeLength - 5f)));
        }
        return lane;
    }

    private float InsideLineClearance(int index)
    {
        float halfWidth = raceCarHalfWidths[index] > 0f ? raceCarHalfWidths[index] : .9f;
        float stripeCenter = !importedTrackGeometry ? .12f : definition.id switch
        {
            RichmondTrackId or PhoenixTrackId => .225f,
            IowaTrackId => .16f,
            NorthWilkesboroTrackId => .13f,
            MartinsvilleTrackId => .10f,
            _ => .12f
        };
        return halfWidth + stripeCenter;
    }

    private bool HasAuthoredPreferredLine => definition.preferredLineLaneMetres?.Length >= 3;

    private bool UsesPreviewPreferredLine => customLayout != null &&
        (HasAuthoredPreferredLine || definition.id is BristolTrackId or NashvilleFairgroundsTrackId or CharlotteTrackId);

    private float PreferredLaneOffset(int index, float width, float distanceToMerge, float line)
    {
        if (UsesPreviewPreferredLine)
            return line * Mathf.SmoothStep(0f, 1f, distanceToMerge / 15f);
        if (behaviorModel is VehicleBehavior004 && definition.majorStyle == "Oval" &&
            line * (customLayout?.InsideSign ?? -1f) > 0f)
        {
            float nominal = Mathf.Max(0f, width * .5f - 1.1f);
            float stripe = Mathf.Max(0f, width * .5f - InsideLineClearance(index));
            float insideLane = line * Mathf.Lerp(nominal, stripe, Mathf.Abs(line));
            return insideLane * Mathf.SmoothStep(0f, 1f, distanceToMerge / 15f);
        }
        return behaviorModel.LaneOffset(index, width, distanceToMerge, line);
    }

    private Vector2 OvalPassingLaneRange(int index, float distance, float width, float lookAhead)
    {
        Vector2 range = VehicleBehavior007.PassingLaneRange(
            PreferredLaneOffset(index, width, float.PositiveInfinity, racingLine(distance)), width);
        // Judge the passing lane across the approach, including the line into the next turn and any narrowing pavement.
        for (int step = 1; step <= 2; step++)
        {
            Vector2 ahead = OvalPassingLaneRangeAt(index, distance + lookAhead * step);
            range.x = Mathf.Max(range.x, ahead.x);
            range.y = Mathf.Min(range.y, ahead.y);
        }
        return range;
    }

    private Vector2 OvalPassingLaneRangeAt(int index, float distance)
    {
        float width = RoadWidths(distance).x;
        return VehicleBehavior007.PassingLaneRange(PreferredLaneOffset(index, width, float.PositiveInfinity, racingLine(distance)), width);
    }

    private void BuildRacingLine()
    {
        sweepingLineTracking = null;
        kinkSteeringBlend = null;
        if (UsesPreviewPreferredLine)
        {
            racingLine = HasAuthoredPreferredLine
                ? BuildPreferredLaneLookup(definition.preferredLineLaneMetres)
                : definition.id switch
                {
                    BristolTrackId => BuildBristolPreferredLine(),
                    CharlotteTrackId => BuildCharlottePreferredLine(),
                    _ => BuildNashvilleFairgroundsPreferredLine()
                };
            if (behaviorModel is VehicleBehavior006)
                sweepingLineTracking = _ => 1f;
            return;
        }

        float inside = customLayout?.InsideSign ?? -1f;
        System.Func<float, Vector3> heading = distance =>
        {
            TrackPoint(distance, 0f, out var forward, out _);
            return forward;
        };
        if (behaviorModel is VehicleBehavior004 sweeping)
        {
            racingLine = sweeping.BuildSweepingLine(lapLength, inside, definition, heading,
                (distance, lane) => TrackPoint(distance, lane, out _, out _), distance => RoadWidths(distance).x,
                out sweepingLineTracking, out kinkSteeringBlend);
            return;
        }
        if (behaviorModel is VehicleBehavior003 && definition.restrictorPlate)
        {
            // Drafting trains hold their lanes; an unaccompanied car takes the low line.
            float lowLine = VehicleBehavior003.RestrictorPlateRacingLine(customLayout?.InsideSign ?? -1f);
            racingLine = distance => lowLine;
            return;
        }
        racingLine = behaviorModel is VehicleBehavior003 current
            ? current.BuildTrackRacingLine(lapLength, inside, definition.majorStyle == "Oval", heading, definition)
            : behaviorModel.BuildRacingLine(lapLength, inside, definition.majorStyle == "Oval", heading);
    }

    private System.Func<float, float> BuildBristolPreferredLine()
    {
        // Reproduce the overhead preview as a closed plan-view path.
        const int count = 1200;
        const float smoothingDistance = 26f;
        // Plan-view offsets from the approved preview, based on a 1.8 m
        // tire footprint. The depth is calibrated after smoothing the arc.
        const float outsideOffset = 5.008388f;
        const float turnDepth = 5.743704f;
        var start = definition.layoutPoints[0];
        float halfStraight = -start.x;
        float radius = -start.z;
        float straight = 2f * halfStraight;
        float turn = Mathf.PI * radius;
        float planLength = 2f * (straight + turn);
        var raw = new Vector2[count];

        for (int i = 0; i < count; i++)
        {
            float station = i * planLength / count;
            float side = station < straight + turn ? 1f : -1f;
            float local = station % (straight + turn);
            if (local < straight)
            {
                raw[i] = side * new Vector2(local - halfStraight, -radius - outsideOffset);
                continue;
            }

            float angle = (local - straight) / radius;
            float sine = Mathf.Sin(angle);
            float lineRadius = radius + outsideOffset - turnDepth * sine * sine;
            raw[i] = side * new Vector2(halfStraight + lineRadius * sine, -lineRadius * Mathf.Cos(angle));
        }

        var weights = PreferredLineSmoothingWeights(smoothingDistance * count / planLength);
        return ProjectPreferredLine(SmoothPreferredLine(raw, weights), weights.Length / 2);
    }

    private System.Func<float, float> BuildNashvilleFairgroundsPreferredLine()
    {
        const int count = 1200;
        const float smoothingDistance = 29f;
        const float straightClearance = 1.05f;
        const float apexClearance = .96f;
        int apex = count / 4;
        var outside = new Vector2[count];
        var turnOffset = new Vector2[count];
        Vector2 apexCenter = default;
        Vector2 apexNormal = default;
        float apexLane = 0f;

        for (int i = 0; i < count; i++)
        {
            float fraction = (float)i / count;
            float station = fraction * lapLength;
            Vector3 point = customLayout.Sample(station, out var forward, out float bank);
            var center = new Vector2(point.x, point.z);
            var normal = new Vector2(forward.z, -forward.x).normalized;
            float halfWidth = RoadWidths(station).x * .5f;
            float horizontal = Mathf.Cos(bank * Mathf.Deg2Rad);

            // Use the local width on each straight and concentrate the inside
            // offset near the two turn midpoints.
            outside[i] = center + normal * ((halfWidth - straightClearance) * horizontal);
            turnOffset[i] = normal * Mathf.Pow(Mathf.Sin(fraction * 2f * Mathf.PI), 4f);
            if (i == apex)
            {
                apexCenter = center;
                apexNormal = normal;
                apexLane = (apexClearance - halfWidth) * horizontal;
            }
        }

        float sourceMetresPerTrackMetre =
            (customLayout.ToSourceSpace(Vector3.right) - customLayout.ToSourceSpace(Vector3.zero)).x;
        float planLength = lapLength * sourceMetresPerTrackMetre;
        var weights = PreferredLineSmoothingWeights(smoothingDistance * count / planLength);
        var path = SmoothPreferredLine(outside, weights);
        var smoothedTurnOffset = SmoothPreferredLine(turnOffset, weights);
        float depth = (Vector2.Dot(path[apex] - apexCenter, apexNormal) - apexLane) /
            Vector2.Dot(smoothedTurnOffset[apex], apexNormal);
        for (int i = 0; i < count; i++)
            path[i] -= smoothedTurnOffset[i] * depth;

        return ProjectPreferredLine(path, weights.Length / 2);
    }

    private System.Func<float, float> BuildCharlottePreferredLine()
    {
        const int count = 1600;
        const float turnSweepMetres = 240f;
        // Calibrated on the approved preview with a 1.8 m tire footprint.
        const float turns12Depth = 7.154903f;
        const float turns34Depth = 7.733173f;
        float turns12Apex = Mathf.Round(customLayout.ControlFraction(8) * count);
        float turns34Apex = Mathf.Round(customLayout.ControlFraction(24) * count);
        var raw = new Vector2[count];
        var turnOffsets = new Vector2[count];

        for (int i = 0; i < count; i++)
        {
            float station = (float)i / count * lapLength;
            Vector3 point = customLayout.Sample(station, out var forward, out float bank);
            var normal = new Vector2(forward.z, -forward.x).normalized;
            float outside = (RoadWidths(station).x * .5f - 1.1f) * Mathf.Cos(bank * Mathf.Deg2Rad);
            raw[i] = new Vector2(point.x, point.z) + normal * outside;
            turnOffsets[i] = normal * (TurnOffset(turns12Apex, turns12Depth) +
                TurnOffset(turns34Apex, turns34Depth));

            float TurnOffset(float apex, float depth)
            {
                float progress = Mathf.Abs(i - apex) * lapLength / (count * turnSweepMetres);
                return progress < 1f ? depth * Mathf.Pow(Mathf.Cos(progress * Mathf.PI * .5f), 4f) : 0f;
            }
        }

        var weights = PreferredLineSmoothingWeights(46f * count / lapLength);
        var path = SmoothPreferredLine(raw, weights);
        var smoothedTurnOffsets = SmoothPreferredLine(turnOffsets, weights);
        for (int i = 0; i < count; i++)
            path[i] -= smoothedTurnOffsets[i];

        // Straighten shallow reverse bends on the base path, then round the
        // transitions into those straight runs before fitting the frontstretch.
        RemoveCharlotteRightBends(path);
        var transitionWeights = PreferredLineSmoothingWeights(1.5f * count / lapLength);
        path = SmoothPreferredLine(path, transitionWeights);

        // The approved circle is expressed in source layout metres. Both
        // kinks are on the exact circle, with blending only beyond the arc.
        const float frontstretchRadius = 817f;
        const float arcHalfSpan = 310f;
        const float blendOuterLimit = 440f;
        var circleCenter = new Vector2(8f, 581.28f);
        for (int i = 0; i < count; i++)
        {
            Vector3 source = customLayout.ToSourceSpace(new Vector3(path[i].x, 0f, path[i].y));
            float blend = Mathf.Clamp01((blendOuterLimit - Mathf.Abs(source.x)) / (blendOuterLimit - arcHalfSpan));
            if (source.z >= 0f || blend <= 0f)
                continue;

            float fromCenter = source.x - circleCenter.x;
            float arcZ = circleCenter.y - Mathf.Sqrt(frontstretchRadius * frontstretchRadius - fromCenter * fromCenter);
            blend = blend * blend * blend * (10f + blend * (6f * blend - 15f));
            source.z = Mathf.Lerp(source.z, arcZ, blend);
            Vector3 point = customLayout.ToTrackSpace(source);
            path[i].y = point.z;
        }

        return ProjectPreferredLine(path, weights.Length / 2);
    }

    private static void RemoveCharlotteRightBends(Vector2[] path)
    {
        // The preferred line travels counterclockwise. Its convex hull
        // replaces each inward dent with a straight-ahead run.
        int count = path.Length;
        var order = new int[count];
        for (int i = 0; i < count; i++)
            order[i] = i;
        System.Array.Sort(order, (a, b) =>
        {
            int horizontal = path[a].x.CompareTo(path[b].x);
            return horizontal != 0 ? horizontal : path[a].y.CompareTo(path[b].y);
        });

        var hull = new int[2 * count];
        int used = 0;
        for (int pass = 0; pass < 2; pass++)
        {
            int start = used;
            for (int j = 0; j < count; j++)
            {
                int i = order[pass == 0 ? j : count - 1 - j];
                while (used >= start + 2 && Turn(hull[used - 2], hull[used - 1], i) <= 0d)
                    used--;
                hull[used++] = i;
            }
            used--; // The next half supplies the shared endpoint.
        }

        System.Array.Sort(hull, 0, used);
        for (int j = 0; j < used; j++)
        {
            int first = hull[j];
            int last = j + 1 < used ? hull[j + 1] : hull[0] + count;
            for (int i = first + 1; i < last; i++)
                path[i % count] = Vector2.Lerp(path[first], path[last % count], (float)(i - first) / (last - first));
        }

        double Turn(int a, int b, int c)
        {
            Vector2 first = path[b] - path[a];
            Vector2 second = path[c] - path[b];
            return (double)first.x * second.y - (double)first.y * second.x;
        }
    }

    private static float[] PreferredLineSmoothingWeights(float smoothingSigma)
    {
        int smoothingRadius = Mathf.CeilToInt(4f * smoothingSigma);
        var weights = new float[2 * smoothingRadius + 1];
        float weightSum = 0f;
        for (int j = -smoothingRadius; j <= smoothingRadius; j++)
        {
            float weight = Mathf.Exp(-0.5f * j * j / (smoothingSigma * smoothingSigma));
            weights[j + smoothingRadius] = weight;
            weightSum += weight;
        }
        for (int i = 0; i < weights.Length; i++)
            weights[i] /= weightSum;
        return weights;
    }

    private static Vector2[] SmoothPreferredLine(Vector2[] raw, float[] weights)
    {
        int count = raw.Length;
        int smoothingRadius = weights.Length / 2;
        var path = new Vector2[count];
        for (int i = 0; i < count; i++)
            for (int j = -smoothingRadius; j <= smoothingRadius; j++)
                path[i] += raw[(i + j + count) % count] * weights[j + smoothingRadius];
        return path;
    }

    private System.Func<float, float> ProjectPreferredLine(Vector2[] path, int smoothingRadius)
    {
        int count = path.Length;
        var lanes = new float[count];
        // Intersect the preview path with each road cross section, then convert
        // the horizontal offset to metres along the banked surface.
        int segment = count - smoothingRadius;
        for (int i = 0; i < count; i++)
        {
            float station = i * lapLength / count;
            Vector3 center = customLayout.Sample(station, out var forward, out float bank);
            var origin = new Vector2(center.x, center.z);
            var along = new Vector2(forward.x, forward.z).normalized;
            var across = new Vector2(along.y, -along.x);
            float Along(int index) => Vector2.Dot(path[index % count] - origin, along);

            while (Along(segment + 1) < 0f)
                segment++;
            float t = Mathf.InverseLerp(Along(segment), Along(segment + 1), 0f);
            Vector2 hit = Vector2.Lerp(path[segment % count], path[(segment + 1) % count], t);
            lanes[i] = Vector2.Dot(hit - origin, across) / Mathf.Cos(bank * Mathf.Deg2Rad);
        }

        return BuildPreferredLaneLookup(lanes);
    }

    private System.Func<float, float> BuildPreferredLaneLookup(float[] lanes)
    {
        int count = lanes.Length;
        return distance =>
        {
            float sample = Mathf.Repeat(distance, lapLength) / lapLength * count;
            int index = Mathf.FloorToInt(sample);
            return Mathf.Lerp(lanes[index % count], lanes[(index + 1) % count], sample - index);
        };
    }

    // Advance the road-relative offset with the fitted path. The traffic model
    // still limits corrections toward it and checks the swept lane for clearance.
    private float SweepingLaneAdvance(int i, float before, float after, float target, TrackPitTraffic.Car traffic,
        bool targetAtEnd = false)
    {
        float weight = sweepingLineTracking?.Invoke((before + after) * .5f) ?? 0f;
        if (weight <= 0f || (traffic != null && (traffic.InPit || traffic.ApronEntryActive || traffic.ApronExitActive ||
            traffic.FlatTireLaneBlend > 0f || Mathf.Min(traffic.NextEntry - after, before - traffic.LastExit) < 15f)))
            return 0f;

        float Preferred(float distance) => PreferredLaneOffset(i, RoadWidths(distance).x,
            float.PositiveInfinity, racingLine(distance));
        float startLane = Preferred(before);
        float endLane = Preferred(after);
        return Mathf.Abs(target - (targetAtEnd ? endLane : startLane)) <= .02f
            ? (endLane - startLane) * weight : 0f;
    }

    private void ConfigureFormationPitExit()
    {
        const float maxMergeLength = 150f;
        if (UsesAuthoredApronHandoffs)
        {
            formationExitLane = pitExitTrackLane;
            formationMergeLength = maxMergeLength;
            return;
        }

        formationExitLane = TrackLaneAtPoint(pitTraffic.ExitDistance, FormationPitExitPoint(1f, 0f));
        formationMergeLength = Mathf.Clamp(Mathf.Abs(formationExitLane) * 12f, FormationSeamLength, maxMergeLength);
    }

    private float TrackLaneAtPoint(float station, Vector3 point)
    {
        // Project the connector endpoint onto the banked track cross section.
        Vector3 center = TrackPoint(station, 0f, out var forward, out _);
        Vector3 across = Vector3.Cross(Vector3.up, forward).normalized;
        float target = Vector3.Dot(point - center, across);
        Vector2 widths = RoadWidths(station);
        float extent = widths.x + widths.y + definition.pitRoad.widthMeters;
        float low = -extent, high = extent;
        for (int i = 0; i < 16; i++)
        {
            float lane = (low + high) * .5f;
            float offset = Vector3.Dot(TrackPoint(station, lane, out _, out _) - center, across);
            if (offset < target) low = lane; else high = lane;
        }
        return (low + high) * .5f;
    }

    private void ConfigurePacePitApproach()
    {
        Vector3 mouth = PitDrivingPosition(pitRoute[0], 0f, 0f, pitHeights[0], pitHeights[1]);
        Vector3 ahead = PitDrivingPosition(pitRoute[0], .001f, 0f, pitHeights[0], pitHeights[1]);
        pacePitEntryPose = new Pose(mouth, Quaternion.LookRotation(ahead - mouth));
        pacePitEntryLane = TrackLaneAtPoint(pitTraffic.EntryDistance, mouth);

        // Use the same single-car connector and tangent as PositionPitSpline.
        Vector3 exit = PitDrivingPosition(pitRoute[2], 1f, 0f, pitHeights[2], pitHeights[3]);
        Vector3 beforeExit = PitDrivingPosition(pitRoute[2], .999f, 0f, pitHeights[2], pitHeights[3]);
        cautionPaceExitPose = new Pose(exit, Quaternion.LookRotation(exit - beforeExit));
        cautionPaceExitLane = TrackLaneAtPoint(pitTraffic.ExitDistance, exit);
        cautionPaceMergeLength = Mathf.Clamp(Mathf.Abs(cautionPaceExitLane) * 12f, 30f, 150f);
    }

    private bool PositionCautionPaceExit(float distance)
    {
        if (!TryCautionPaceExitPose(distance, out var pose, out float lane)) return false;
        paceCar.SetLocalPositionAndRotation(pose.position, pose.rotation);
        if (SupportsIowaVenueLighting)
            ApplyIowaVehicleLighting(paceCar, IowaTrackVehicleLighting(distance, lane));
        return true;
    }

    private bool TryCautionPaceExitPose(float distance, out Pose pose, out float lane)
    {
        // Richmond's authored departure already includes its full track join.
        if (stages?.HasPaceCar == true && !stages.PaceReturning && richmondPaceDeparture == null)
            return TryCautionPaceExitPose(distance, stages.PaceExitStation, out pose, out lane);
        pose = default;
        lane = 0f;
        return false;
    }

    private bool TryCautionPaceExitPose(float distance, float exit, out Pose pose, out float lane)
    {
        pose = default;
        lane = 0f;
        float travel = distance - exit;
        if (travel < 0f || travel >= cautionPaceMergeLength)
            return false;

        bool followsApron = ApronExitAvailable(exit);
        float apronAtExit = followsApron ? PitApronLane(exit, exit) : 0f;
        Vector3 seamOffset = cautionPaceExitPose.position -
            TrackPoint(exit, cautionPaceExitLane, out _, out _);
        float seam = SeamWeight(distance);
        lane = LaneAt(distance);
        Vector3 position = PointAt(distance);
        Vector3 forward = PointAt(Mathf.Min(exit + cautionPaceMergeLength, distance + .4f)) -
            PointAt(Mathf.Max(exit, distance - .4f));
        SampleTrackCarPose(distance, lane, out _, out var up);
        Quaternion rotation = Quaternion.Slerp(Quaternion.LookRotation(forward, up),
            cautionPaceExitPose.rotation, seam);
        pose = new Pose(position, rotation);
        return true;

        float SeamWeight(float station) => 1f - Mathf.SmoothStep(0f, 1f, (station - exit) / FormationSeamLength);

        float LaneAt(float station)
        {
            float lane = followsApron
                ? PitApronLane(station, exit) + (cautionPaceExitLane - apronAtExit) * SeamWeight(station)
                : cautionPaceExitLane;
            return Mathf.SmoothStep(lane, 0f, (station - exit) / cautionPaceMergeLength);
        }

        Vector3 PointAt(float station) =>
            TrackPoint(station, LaneAt(station), out _, out _) + seamOffset * SeamWeight(station);
    }

    private bool PositionPacePitApproach(float distance)
    {
        if (!TryPacePitApproachPose(distance, out var pose, out float lane)) return false;
        paceCar.SetLocalPositionAndRotation(pose.position, pose.rotation);
        if (SupportsIowaVenueLighting)
            ApplyIowaVehicleLighting(paceCar, IowaTrackVehicleLighting(distance, lane));
        return true;
    }

    private bool TryPacePitApproachPose(float distance, out Pose pose, out float lane)
    {
        pose = default;
        lane = 0f;
        if (richmondPaceReturn != null) return false;
        float entry = stages?.HasPaceCar == true ? stages.PacePitEntry : raceStart.PacePitEntry;
        float approachLength = Mathf.Min(100f, lapLength * .45f);
        float start = entry - approachLength;
        if (distance <= start) return false;

        Vector2 entryWidths = RoadWidths(entry);
        float inside = customLayout.InsideSign;
        float apronOffset = pacePitEntryLane * inside - entryWidths.x * .5f;
        Vector3 mouthCorrection = pacePitEntryPose.position - TrackPoint(entry, pacePitEntryLane, out _, out _);
        lane = ApproachLane(distance);
        Vector3 position = ApproachPoint(distance);
        Vector3 forward = ApproachPoint(Mathf.Min(entry, distance + .4f)) -
            ApproachPoint(Mathf.Max(start, distance - .4f));
        Vector3 across = TrackPoint(distance, lane + FormationHalfWheelTrack, out _, out _) -
            TrackPoint(distance, lane - FormationHalfWheelTrack, out _, out _);
        Quaternion rotation = Quaternion.LookRotation(forward, Vector3.Cross(forward, across));
        if (distance - start < 10f)
        {
            SampleTrackCarPose(distance, TrackPaceLane(distance), out var trackForward, out var trackUp);
            rotation = Quaternion.Slerp(Quaternion.LookRotation(trackForward, trackUp), rotation,
                Mathf.SmoothStep(0f, 1f, (distance - start) / 10f));
        }
        rotation = Quaternion.Slerp(rotation, pacePitEntryPose.rotation, SeamBlend(distance));
        pose = new Pose(position, rotation);
        return true;

        float SeamBlend(float station) => Mathf.SmoothStep(1f, 0f, (entry - station) / FormationSeamLength);

        Vector3 ApproachPoint(float station) =>
            TrackPoint(station, ApproachLane(station), out _, out _) + mouthCorrection * SeamBlend(station);

        float TrackPaceLane(float station) => raceReleased ? cautionPaceAvoidanceLane : FormationTrackLane(station, 0f);

        float ApproachLane(float station)
        {
            float target = pacePitEntryLane;
            if (apronOffset > 0f && entryWidths.y > 0f)
            {
                Vector2 widths = RoadWidths(station);
                target = inside * (widths.x * .5f + apronOffset * widths.y / entryWidths.y);
            }
            return Mathf.SmoothStep(TrackPaceLane(station), target, (station - start) / approachLength);
        }
    }

    private float LimitCautionPaceTrafficSpeed(bool onPitRoad, float distance, float speed, float seconds)
    {
        if (seconds <= 0f || paceCar == null) return speed;
        if (richmondPaceReturn != null && (onPitRoad || richmondPacePhase != RichmondPacePhase.Pacing))
            return speed;

        float requested = speed;
        bool fixedPath = onPitRoad || TryCautionPaceExitPose(distance, out _, out _) ||
            TryPacePitApproachPose(distance, out _, out _) ||
            richmondPaceReturn != null && stages.PacePitEntry - distance < 78f;
        if (onPitRoad)
        {
            cautionPaceAvoidanceLane = 0f;
            if (distance < pitTraffic.RoadEnd) speed = Mathf.Min(speed, pitTraffic.SpeedLimit);
        }

        float currentLane = cautionPaceAvoidanceLane;
        float targetLane = currentLane;
        if (!fixedPath)
        {
            float halfWidth = RoadWidths(distance).x * .5f;
            float offset = Mathf.Min(3.4f, halfWidth - 1.5f);
            float lookAhead = Mathf.Max(14f, speed * 1.3f);
            bool centerClear = PacePathClearance(distance, lookAhead, speed,
                (station, time) => SampleCautionPacePose(false, station,
                    Mathf.MoveTowards(currentLane, 0f, PaceLaneRate * time))) >= lookAhead;
            if (centerClear)
                targetLane = 0f;
            else if (offset >= 2.4f)
            {
                // Check the sweep into the adjacent lane, including cars closing from behind.
                float best = PacePathClearance(distance, lookAhead, speed,
                    (station, time) => SampleCautionPacePose(false, station, currentLane));
                for (int side = -1; side <= 1; side += 2)
                {
                    float candidate = side * offset;
                    float clear = PacePathClearance(distance, lookAhead, speed,
                        (station, time) => SampleCautionPacePose(false, station,
                            Mathf.MoveTowards(currentLane, candidate, PaceLaneRate * time)));
                    if (clear <= best + 1f) continue;
                    best = clear;
                    targetLane = candidate;
                }
            }
        }

        float stoppingRoom = Mathf.Max(6f, speed * speed / (2f * TrackPitTraffic.PitApproachDeceleration) + 2f);
        float room = PacePathClearance(distance, stoppingRoom, speed,
            (station, time) => SampleCautionPacePose(onPitRoad, station,
                Mathf.MoveTowards(currentLane, targetLane, PaceLaneRate * time)), out int blocker);
        string blockReason = blocker >= 0 ? "Predicted path clearance" : speed < requested ? "Pit-road speed limit" : "";
        float allowed = room < stoppingRoom ? Mathf.Min(speed, TrackPitTraffic.ApproachLimit(0f, room, seconds)) : speed;
        float nextLane = Mathf.MoveTowards(currentLane, targetLane, PaceLaneRate * seconds);
        Pose next = SampleCautionPacePose(onPitRoad, distance + allowed * seconds, nextLane);
        if (!PacePoseClear(next, seconds, out int laneBlocker))
        {
            nextLane = currentLane;
            if (!PacePoseClear(SampleCautionPacePose(onPitRoad, distance + allowed * seconds, nextLane), seconds,
                out int stopBlocker))
            {
                allowed = 0f;
                blocker = stopBlocker;
                blockReason = "Next pose blocked";
            }
            else if (blocker < 0)
            {
                blocker = laneBlocker;
                blockReason = "Lane change blocked";
            }
        }
        RecordPaceTrafficSample(requested, allowed, targetLane, room, stoppingRoom, blocker, blockReason);
        cautionPaceAvoidanceLane = nextLane;
        cautionPaceTrafficSpeed = allowed;
        return allowed;
    }

    private void LimitTrafficBehindCautionPaceCar(float seconds)
    {
        if (stages?.HasPaceCar != true || paceCar == null || RichmondPaceIsParked) return;
        var pace = stages.PacePosition();
        if (pace.OnPitRoad && stages.PaceReturning && pace.ParkingBlend >= .999f && richmondPaceReturn == null)
            return;
        Pose pose = richmondPaceReturn != null &&
            (richmondPacePhase is RichmondPacePhase.Departing or RichmondPacePhase.Returning)
            ? new Pose(paceCar.localPosition, paceCar.localRotation)
            : SampleCautionPacePose(pace.OnPitRoad, pace.Distance, cautionPaceAvoidanceLane);
        Vector3 paceForward = pose.rotation * Vector3.forward;
        for (int i = 0; i < trafficCars.Length; i++)
        {
            if (stepSpeeds[i] <= 0f || crashes[i].Active || trafficCars[i].InGarage) continue;
            Vector3 delta = pose.position - PaceTrafficCarPosition(i);
            Vector3 forward = cars[i].localRotation * Vector3.forward;
            Vector3 across = Vector3.Cross(Vector3.up, forward).normalized;
            float gap = Vector3.Dot(delta, forward);
            if (gap <= 0f || gap > 40f || Mathf.Abs(delta.y) > 2f ||
                Mathf.Abs(Vector3.Dot(delta, across)) > 2.3f) continue;
            float aheadSpeed = Mathf.Max(0f, Vector3.Dot(paceForward, forward) * cautionPaceTrafficSpeed);
            stepSpeeds[i] = Mathf.Min(stepSpeeds[i],
                TrackPitTraffic.ApproachLimit(aheadSpeed, Mathf.Max(0f, gap - 4.2f), seconds));
        }
    }

    private Pose SampleCautionPacePose(bool onPitRoad, float distance, float lane)
    {
        if (onPitRoad)
        {
            if (distance >= pitTraffic.RouteLength)
            {
                float exit = pitTraffic.ExitDistance;
                float station = exit + distance - pitTraffic.RouteLength;
                return SampleCautionPaceExitPose(station, exit);
            }
            Vector3 point = CautionPacePitPoint(distance);
            Vector3 forward = CautionPacePitPoint(distance + .2f) - CautionPacePitPoint(distance - .2f);
            return new Pose(point, Quaternion.LookRotation(forward));
        }
        if (TryCautionPaceExitPose(distance, out var pose, out _) || TryPacePitApproachPose(distance, out pose, out _))
            return pose;
        if (richmondPaceReturn != null && stages.PacePitEntry - distance < 78f)
            lane = Mathf.SmoothStep(lane, ApronLane(distance),
                Mathf.InverseLerp(stages.PacePitEntry - 78f, stages.PacePitEntry, distance));
        Vector3 position = SampleTrackCarPose(distance, lane, out var tangent, out var up);
        return new Pose(position, Quaternion.LookRotation(tangent, up));
    }

    private Pose SampleCautionPaceExitPose(float station, float exit)
    {
        if (TryCautionPaceExitPose(station, exit, out var pose, out _)) return pose;
        Vector3 position = SampleTrackCarPose(station, 0f, out var tangent, out var up);
        return new Pose(position, Quaternion.LookRotation(tangent, up));
    }

    private Vector3 CautionPacePitPoint(float distance)
    {
        distance = Mathf.Clamp(distance, 0f, pitTraffic.RouteLength);
        float parking = Mathf.Clamp01(1f - Mathf.Abs(pitTraffic.ParkingDistance - distance) / 8f);
        return SamplePacePitPoint(distance, parking * (definition.pitRoad.widthMeters * .5f + 1.5f));
    }

    private Vector3 SamplePacePitPoint(float distance, float lane)
    {
        int segment = 0;
        while (segment < pitRoute.Length - 1 && distance >= pitLengths[segment])
            distance -= pitLengths[segment++];
        return PitDrivingPosition(pitRoute[segment], Mathf.Clamp01(distance / pitLengths[segment]), lane,
            pitHeights[segment], pitHeights[segment + 1]);
    }

    private float PacePathClearance(float start, float distance, float speed,
        System.Func<float, float, Pose> poseAt) =>
        PacePathClearance(start, distance, speed, poseAt, out _);

    private float PacePathClearance(float start, float distance, float speed,
        System.Func<float, float, Pose> poseAt, out int blocker)
    {
        blocker = -1;
        int samples = Mathf.Max(1, Mathf.CeilToInt(distance / 2f));
        for (int sample = 1; sample <= samples; sample++)
        {
            float travel = distance * sample / samples;
            float time = travel / Mathf.Max(4f, speed);
            if (!PacePoseClear(poseAt(start + travel, time), time, out blocker))
                return distance * (sample - 1) / samples;
        }
        return distance;
    }

    private bool PacePoseClear(Pose pose, float time, out int blocker)
    {
        blocker = -1;
        Vector3 forward = pose.rotation * Vector3.forward;
        float heading = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        for (int i = 0; i < trafficCars.Length; i++)
        {
            var car = trafficCars[i];
            if (car.InGarage || i == 0 && NorthWilkesboroGarageOffPitRoute || !cars[i].gameObject.activeSelf) continue;
            float speed = car.Retired ? 0f : SessionRun.CurrentSpeed(i);
            float reach = 7f + speed * time;
            if ((cars[i].localPosition - pose.position).sqrMagnitude > reach * reach) continue;
            Vector3 direction = cars[i].localRotation * Vector3.forward;
            Vector3 predicted;
            if (!car.InPit && !crashes[i].Active && speed > 0f)
                predicted = TrackPoint(car.TrackDistance + speed * time, drivingLanes[i], out direction, out _);
            else
                predicted = PaceTrafficCarPosition(i) + direction * (speed * time);
            Vector3 delta = predicted - pose.position;
            if (Mathf.Abs(delta.x) > 5f || Mathf.Abs(delta.z) > 5f || Mathf.Abs(delta.y) > 2f) continue;
            float otherHeading = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            if (PitFootprint.Overlaps(delta.z, delta.x, heading, otherHeading, PaceClearanceMargin))
            {
                blocker = i;
                return false;
            }
        }
        return true;
    }

    private void RecordPaceTrafficSample(float requested, float allowed, float targetLane,
        float clearDistance, float checkedDistance, int blocker, string reason)
    {
        const float metersPerSecondToMph = 3600f / TrackDefinition.MetersPerMile;
        cautionPaceSample = new TrackSnapshot.PaceCarSample
        {
            allowedMph = allowed * metersPerSecondToMph,
            requestedMph = requested * metersPerSecondToMph,
            targetLaneMeters = targetLane,
            blockedByCarNumber = blocker < 0 ? -1 : simulation.Cars[blocker].carNumber,
            clearDistanceMeters = clearDistance,
            checkedDistanceMeters = checkedDistance,
            blockReason = reason
        };
    }

    private Vector3 PaceTrafficCarPosition(int index)
    {
        var car = trafficCars[index];
        if (car.InPit)
            return SampleTrafficPosition(car, car.BackExit ? car.ConnectorDistance : car.PitDistance, car.PitLane);
        if (crashes[index].Active && crashes[index].Cause == CrashCause.FrontTireWear)
            return cars[index].localPosition;
        float lane = crashes[index].Active ? crashes[index].Lane : drivingLanes[index];
        return TrackPoint(car.TrackDistance, lane, out _, out _);
    }

    private float FormationTrackLane(float distance, float lane) =>
        lane + Mathf.SmoothStep(formationExitLane, 0f,
            (distance - pitTraffic.ExitDistance) / formationMergeLength);

    private void PositionFormationTrackCar(Transform car, float distance, float lane)
    {
        float trackLane = FormationTrackLane(distance, lane);
        PositionTrackCar(car, distance, trackLane);

        // A row can straddle the inside stripe as the apron meets the banking.
        // Derive roll across the wheels so the car banks over its full width.
        Vector2 widths = RoadWidths(distance);
        float stripe = widths.x * .5f * (customLayout?.InsideSign ?? -1f);
        if (widths.y > 0f && Mathf.Abs(trackLane - stripe) < FormationHalfWheelTrack)
        {
            Vector3 across = TrackPoint(distance, trackLane + FormationHalfWheelTrack, out _, out _) -
                TrackPoint(distance, trackLane - FormationHalfWheelTrack, out _, out _);
            Vector3 forward = car.localRotation * Vector3.forward;
            car.localRotation = Quaternion.LookRotation(forward, Vector3.Cross(forward, across));
        }

        float exit = pitTraffic.ExitDistance;
        float seamBlend = 1f - Mathf.SmoothStep(0f, 1f, (distance - exit) / FormationSeamLength);
        if (seamBlend <= 0f) return;

        // Match each lane's position and orientation at the connector seam.
        Pose pitEnd = FormationPitExitPose(1f, lane);
        Vector3 trackStart = TrackPoint(exit, formationExitLane + lane, out _, out _);
        car.localPosition += (pitEnd.position - trackStart) * seamBlend;
        car.localRotation = Quaternion.Slerp(car.localRotation, pitEnd.rotation, seamBlend);
    }

    private void PositionTrackCar(Transform car, float distance, float lane, float laneSlope = 0f)
    {
        car.localPosition = SampleTrackCarPose(distance, lane, out var forward, out var up);
        car.localRotation = Quaternion.LookRotation(forward + Vector3.Cross(up, forward).normalized * laneSlope, up);
        if (SupportsIowaVenueLighting)
            ApplyIowaVehicleLighting(car, IowaTrackVehicleLighting(distance, lane));
    }

    private Vector3 SampleTrackCarPose(float distance, float lane, out Vector3 forward, out Vector3 up)
    {
        Vector3 position = TrackPoint(distance, lane, out forward, out up);
        if (customLayout?.HasContinuousBanking == true)
        {
            // The spline heading is along the datum. Pitch the car along its
            // actual banked lane over the displayed wheelbase.
            const float halfWheelbase = RearAxleHeading.CenterToRearAxle;
            Vector3 across = Vector3.Cross(up, forward);
            Vector3 tangent = TrackPoint(distance + halfWheelbase, lane, out _, out _) -
                TrackPoint(distance - halfWheelbase, lane, out _, out _);
            if (tangent.sqrMagnitude > 0.000001f)
            {
                forward = tangent.normalized;
                up = Vector3.Cross(forward, across).normalized;
            }
        }
        return position;
    }

    private int mergeBudgetFrame = -1, mergeChecksThisFrame;
    private double mergeMillisecondsThisFrame;
    private int timingFrames, timingMergeChecks, timingMergeDeferred, timingPredictionSamples;
    private double timingFrameTotal, timingFrameMax, timingMergeTotal, timingMergeMax;
    private readonly System.Collections.Generic.Dictionary<long, Vector3?> mergePredictionCache =
        new System.Collections.Generic.Dictionary<long, Vector3?>();

    private bool MergeCheckAvailable()
    {
        if (mergeBudgetFrame != Time.frameCount)
        {
            mergeBudgetFrame = Time.frameCount;
            mergeChecksThisFrame = 0;
            mergeMillisecondsThisFrame = 0;
        }
        // Soft budget: one check may exceed it, but cannot trigger repeated expensive checks.
        if (mergeChecksThisFrame < 2 && mergeMillisecondsThisFrame < 2.0) return true;
        timingMergeDeferred++;
        return false;
    }

    private bool PitExitGapClear(TrackPitTraffic.Car car, bool backExit)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        mergeChecksThisFrame++;
        timingMergeChecks++;
        mergePredictionCache.Clear();
        try { return EvaluatePitExitGap(car, backExit); }
        finally
        {
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 /
                System.Diagnostics.Stopwatch.Frequency;
            mergeMillisecondsThisFrame += ms;
            timingMergeTotal += ms;
            timingMergeMax = System.Math.Max(timingMergeMax, ms);
        }
    }

    private readonly System.Collections.Generic.Dictionary<long, Vector3> mergeTrackGeometry =
        new System.Collections.Generic.Dictionary<long, Vector3>();

    private readonly System.Collections.Generic.Dictionary<TrackPitSpline,
        System.Collections.Generic.Dictionary<long, Vector3>> mergeExitGeometry =
        new System.Collections.Generic.Dictionary<TrackPitSpline, System.Collections.Generic.Dictionary<long, Vector3>>();

    private Vector3 MergeExitPoint(TrackPitSpline spline, float fraction, float lane, float start, float end)
    {
        if (!mergeExitGeometry.TryGetValue(spline, out var points))
        {
            points = new System.Collections.Generic.Dictionary<long, Vector3>();
            mergeExitGeometry[spline] = points;
        }
        float sample = Mathf.Clamp01(fraction) * 100f;
        int lower = Mathf.Min(99, Mathf.FloorToInt(sample));
        int laneBin = Mathf.RoundToInt(lane * 2f);
        return Vector3.Lerp(Point(lower), Point(lower + 1), sample - lower);
        Vector3 Point(int index)
        {
            long key = ((long)laneBin << 32) | (uint)index;
            if (!points.TryGetValue(key, out var point))
            {
                point = PitDrivingPosition(spline, index * .01f, laneBin * .5f, start, end);
                points[key] = point;
            }
            return point;
        }
    }

    private Vector3 MergeTrackPoint(float distance, float lane)
    {
        // Geometry-only cache: never caches vehicle state or clearance decisions.
        int count = Mathf.CeilToInt(lapLength / 5f);
        float station = Mathf.Repeat(distance, lapLength) * count / lapLength;
        int lower = Mathf.FloorToInt(station), laneBin = Mathf.RoundToInt(lane * 2f);
        return Vector3.Lerp(Point(lower), Point((lower + 1) % count), station - lower);
        Vector3 Point(int index)
        {
            long key = ((long)laneBin << 32) | (uint)index;
            if (!mergeTrackGeometry.TryGetValue(key, out var point))
            {
                point = TrackPoint(index * lapLength / count, laneBin * .5f, out _, out _);
                mergeTrackGeometry[key] = point;
            }
            return point;
        }
    }

    private bool EvaluatePitExitGap(TrackPitTraffic.Car car, bool backExit)
    {
        TrackPitSpline spline = backExit ? bristolBackExit : pitRoute[2];
        float length = backExit ? bristolBackExitLength : pitLengths[2];
        float exitDistance = backExit ? bristolBackExitTrackDistance : pitTraffic.ExitDistance;
        float mergeLane = backExit ? bristolBackExitLane : pitExitTrackLane;
        float startHeight = backExit ? 0f : pitHeights[2];
        float endHeight = backExit ? bristolBackExitHeight : pitHeights[3];
        float initialSpeed = Mathf.Max(0f, pitTraffic.SpeedAtStepStart(car));
        float approachDistance = Mathf.Max(0f, car.ExitHoldDistance - car.PitDistance);
        float approachTime = PitMergeWindow.ApproachTime(approachDistance, initialSpeed, pitTraffic.SpeedLimit, 4f);
        float exitSpeed = Mathf.Min(pitTraffic.SpeedLimit, initialSpeed + 4f * approachTime);
        float duration = approachTime + PitMergeWindow.TimeToDistance(length + 60f, exitSpeed, 4f);
        return PitMergeWindow.IsClear(duration, fieldCount: trafficCars.Length,
            candidate: (time, acceleration) =>
            {
                if (time < approachTime)
                    return SampleTrafficPosition(car, car.PitDistance + PitMergeWindow.ApproachTravel(time, initialSpeed, pitTraffic.SpeedLimit, 4f), car.PitLane);
                float exitTime = time - approachTime;
                float travel = exitSpeed * exitTime + .5f * acceleration * exitTime * exitTime;
                if (travel <= length)
                {
                    float fraction = travel / length;
                    float lane = car.PitLane * (1f - Mathf.SmoothStep(0f, 1f, fraction));
                    return MergeExitPoint(spline, fraction, lane, startHeight, endHeight);
                }
                return MergeTrackPoint(exitDistance + travel - length, mergeLane);
            },
            obstacle: (index, time) =>
            {
                // Times are on a fixed 0.05-second grid, so uncertainty samples
                // reuse predictions instead of repeatedly evaluating track geometry.
                int tick = Mathf.RoundToInt(time * 20f);
                long key = ((long)index << 32) | (uint)tick;
                if (mergePredictionCache.TryGetValue(key, out var cached)) return cached;
                var other = pitTraffic.StateAtStepStart(trafficCars[index]);
                Vector3? result = null;
                if (other.FieldIndex != car.FieldIndex && !other.InGarage && cars[index].gameObject.activeSelf &&
                    !pitTraffic.FollowsExitQueue(car, other) &&
                    !(other.InPit && !other.ExitMergeGranted && !other.BackExit && other.WaitingForExitMerge))
                {
                    // Stationary vehicles do not require spline/height sampling.
                    result = pitTraffic.SpeedAtStepStart(other) < .01f ? pitTraffic.PositionAtStepStart(other) :
                        PredictMergeObstacle(other, tick * .05f);
                    timingPredictionSamples++;
                }
                mergePredictionCache[key] = result;
                return result;
            },
            active: (time, acceleration) =>
            {
                float exitTime = Mathf.Max(0f, time - approachTime);
                return exitSpeed * exitTime + .5f * acceleration * exitTime * exitTime <= length + 60f;
            },
            blocked: (index, time) =>
            {
                car.ExitBlockedBy = trafficCars[index].FieldIndex;
                car.ExitConflictSeconds = time;
            });
    }

    private Vector3 PredictMergeObstacle(TrackPitTraffic.Car car, float time)
    {
        car = pitTraffic.StateAtStepStart(car);
        float travel = pitTraffic.SpeedAtStepStart(car) * time;
        if (!car.InPit) return MergeTrackPoint(car.TrackDistance + travel, pitTraffic.LaneAtStepStart(car));
        if (car.BackExit && car.ConnectorDistance + travel > bristolBackExitLength)
            return MergeTrackPoint(bristolBackExitTrackDistance + car.ConnectorDistance + travel - bristolBackExitLength, bristolBackExitLane);
        if (!car.BackExit && car.PitDistance + travel > pitTraffic.RouteLength)
            return MergeTrackPoint(pitTraffic.ExitDistance + car.PitDistance + travel - pitTraffic.RouteLength, pitExitTrackLane);
        return pitTraffic.PredictedPosition(car, time);
    }

    private Vector3 SampleTrafficPosition(TrackPitTraffic.Car car, float distance, float lane)
    {
        if (!car.InPit) return TrackPoint(distance, lane, out _, out _);
        if (car.BackExit) return PitDrivingPosition(bristolBackExit, Mathf.Clamp01(distance / bristolBackExitLength), lane, 0f, bristolBackExitHeight);
        if (car.FrontEntry && distance < bristolFrontJoin)
            return PitDrivingPosition(bristolFrontEntry, Mathf.Clamp01(distance / bristolFrontJoin), lane, bristolFrontEntryHeight, 0f);
        int segment = 0;
        while (segment < pitRoute.Length - 1 && distance >= pitLengths[segment]) distance -= pitLengths[segment++];
        return PitDrivingPosition(pitRoute[segment], Mathf.Clamp01(distance / pitLengths[segment]), lane, pitHeights[segment], pitHeights[segment + 1]);
    }

    private void PositionPitCar(Transform car, float distance, float lane = 0f, TrackPitTraffic.Car traffic = null,
        bool isRaceFormation = false)
    {
        if (traffic?.BackExit == true)
        {
            PositionPitSpline(car, bristolBackExit, traffic.ConnectorDistance / bristolBackExitLength, lane, 0f, bristolBackExitHeight);
            return;
        }
        if (traffic?.FrontEntry == true && distance < bristolFrontJoin)
        {
            PositionPitSpline(car, bristolFrontEntry, distance / bristolFrontJoin, lane, bristolFrontEntryHeight, 0f);
            return;
        }
        int segment = 0;
        while (segment < pitRoute.Length - 1 && distance >= pitLengths[segment])
            distance -= pitLengths[segment++];
        float fraction = Mathf.Clamp01(distance / pitLengths[segment]);
        if (isRaceFormation && segment == 2)
        {
            Pose pose = FormationPitExitPose(fraction, lane);
            car.SetLocalPositionAndRotation(pose.position, pose.rotation);
        }
        else
            PositionPitSpline(car, pitRoute[segment], fraction, lane, pitHeights[segment], pitHeights[segment + 1]);
        if (SupportsIowaVenueLighting)
            ApplyIowaVehicleLighting(car, segment == 1 ? IowaPitLightingLayer :
                IowaConnectorVehicleLighting(car.localPosition, segment == 0 ? pitTraffic.EntryDistance : pitTraffic.ExitDistance));
    }

    private void PositionPitSpline(Transform car, TrackPitSpline spline, float fraction, float lane, float startHeight, float endHeight)
    {
        car.localPosition = DrivingPoint(fraction);
        Vector3 tangent = DrivingPoint(Mathf.Min(1f, fraction + 0.001f)) -
            DrivingPoint(Mathf.Max(0f, fraction - 0.001f));
        if (tangent.sqrMagnitude > 0f) car.localRotation = Quaternion.LookRotation(tangent);

        Vector3 DrivingPoint(float sample) => PitDrivingPosition(spline, sample, lane, startHeight, endHeight);
    }

    private Vector3 FormationPitExitPoint(float fraction, float lane)
    {
        // Rolling starts use the width of the exit connector. Individual pit
        // departures still follow the single-car merge to the apron.
        return UsesSharedPitConnectorSurface && ApronExitAvailable(pitTraffic.ExitDistance)
            ? ApronConnectorPoint(pitRoute[2], fraction, lane, pitHeights[2], pitHeights[3], formation: true)
            : RawPitDrivingPosition(pitRoute[2], fraction, lane, pitHeights[2], pitHeights[3]);
    }

    private Pose FormationPitExitPose(float fraction, float lane)
    {
        float halfWheelbase = RearAxleHeading.CenterToRearAxle / pitLengths[2];
        Vector3 forward = FormationPitExitPoint(Mathf.Min(1f, fraction + halfWheelbase), lane) -
            FormationPitExitPoint(Mathf.Max(0f, fraction - halfWheelbase), lane);
        Vector3 across = FormationPitExitPoint(fraction, lane + FormationHalfWheelTrack) -
            FormationPitExitPoint(fraction, lane - FormationHalfWheelTrack);
        return new Pose(FormationPitExitPoint(fraction, lane), Quaternion.LookRotation(forward, Vector3.Cross(forward, across)));
    }

    private Vector3 RawPitDrivingPosition(TrackPitSpline spline, float fraction, float lane, float startHeight, float endHeight)
    {
        if (bristolConnectorVertices.TryGetValue(spline, out var vertices))
            return SampleBristolConnector(vertices, fraction, lane);
        return PitPosition(spline, fraction, startHeight, endHeight) +
            Vector3.Cross(Vector3.up, PitTangent(spline, fraction, startHeight, endHeight)).normalized * lane;
    }

    private int connectorBuildCount;
    private double connectorBuildMilliseconds;

    private sealed class ApronConnectorGeometry
    {
        internal readonly Vector3[] centers = new Vector3[129], across = new Vector3[129],
            edges = new Vector3[129], inward = new Vector3[129];
        internal readonly float[] length = new float[129], bankRise = new float[129], apronWidth = new float[129];
        internal readonly float[,] surfaceHeight = new float[129, 9];
        internal float formationShift;
    }
    private readonly System.Collections.Generic.Dictionary<TrackPitSpline, ApronConnectorGeometry> apronConnectors =
        new System.Collections.Generic.Dictionary<TrackPitSpline, ApronConnectorGeometry>();

    private Vector3 ApronConnectorPoint(TrackPitSpline spline, float fraction, float lane, float startHeight, float endHeight,
        bool formation = false, bool wideningPavement = false)
    {
        bool entry = spline == pitRoute[0] || spline == bristolFrontEntry;
        if (!apronConnectors.TryGetValue(spline, out var geometry))
        {
            long buildStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            geometry = new ApronConnectorGeometry();
            float attachment = PitAttachment(spline.Sample(entry ? 0f : 1f));
            bool sharedExit = !entry && UsesSharedPitConnectorSurface;
            if (sharedExit)
                geometry.formationShift = Mathf.Max(0f, FormationHalfLane + 1.05f - RoadWidths(attachment).y * .5f);
            Vector3 apron = TrackPoint(attachment, ApronLane(attachment), out var trackForward, out _);
            Vector3 start = entry ? apron : RawPitDrivingPosition(spline, 0f, 0f, startHeight, endHeight);
            float roadJoin = entry && spline == bristolFrontEntry
                ? (bristolFrontJoin - pitLengths[0]) / pitLengths[1] : 0f;
            Vector3 end = entry ? PitPosition(pitRoute[1], roadJoin, pitHeights[1], pitHeights[2]) : apron;
            Vector3 startForward = entry ? trackForward : PitTangent(spline, 0f, startHeight, endHeight);
            if (!entry && UsesSharedPitConnectorSurface)
                startForward = PitTangent(pitRoute[1], 1f, pitHeights[1], pitHeights[2]);
            // The final lane offset must use pit road's tangent, not the old connector's.
            Vector3 endForward = entry ? PitTangent(pitRoute[1], roadJoin, pitHeights[1], pitHeights[2]) : trackForward;
            float reach = Vector3.Distance(start, end);
            float side = customLayout.InsideSign;
            for (int i = 0; i < geometry.centers.Length; i++)
            {
                float t = i / 128f;
                Vector3 point = PitHermite(start, end, startForward * reach, endForward * reach, t);
                float station = TrackUtilityGeometry.ClosestMainDistance(customLayout, lapLength, point);
                Vector2 widths = RoadWidths(station);
                float stripe = side * widths.x * .5f;
                Vector3 edge = TrackPoint(station, stripe, out var forward, out _);
                Vector3 inward = Vector3.Cross(Vector3.up, forward).normalized * side;
                // Keep the entire car inside the racing surface edge, including its body width.
                point += inward * Mathf.Max(0f, 1.05f - Vector3.Dot(point - edge, inward));
                geometry.centers[i] = point;
                geometry.edges[i] = edge;
                geometry.inward[i] = inward;
                if (sharedExit)
                {
                    Vector3 bank = TrackPoint(station, stripe - side, out _, out _) - edge;
                    geometry.bankRise[i] = bank.y / -Vector3.Dot(bank, inward);
                    geometry.apronWidth[i] = widths.y;
                }
                Vector3 tangent = PitHermite(start, end, startForward * reach, endForward * reach, Mathf.Min(1f, t + .001f)) -
                    PitHermite(start, end, startForward * reach, endForward * reach, Mathf.Max(0f, t - .001f));
                geometry.across[i] = Vector3.Cross(Vector3.up, i == 128 ? endForward : i == 0 ? startForward : tangent).normalized;
                if (definition.id == BristolTrackId)
                    for (int strip = 0; strip <= 8; strip++)
                    {
                        float offset = definition.pitRoad.widthMeters * (strip / 8f - .5f);
                        Vector3 surface = point + geometry.across[i] * offset *
                            (entry ? Mathf.SmoothStep(0f, 1f, t) : 1f - Mathf.SmoothStep(0f, 1f, t));
                        surface += inward * Mathf.Max(0f, 1.05f - Vector3.Dot(surface - edge, inward));
                        surface.y = entry ? Mathf.Lerp(edge.y, endHeight + .02f, Mathf.SmoothStep(0f, 1f, t)) :
                            Mathf.Lerp(startHeight + .02f, edge.y, Mathf.SmoothStep(0f, 1f, t));
                        geometry.surfaceHeight[i, strip] = BristolConnectorHeight(surface, out _) + .005f;
                    }
                if (i > 0) geometry.length[i] = geometry.length[i - 1] + Vector3.Distance(point, geometry.centers[i - 1]);
            }
            apronConnectors[spline] = geometry;
            connectorBuildCount++;
            connectorBuildMilliseconds += (System.Diagnostics.Stopwatch.GetTimestamp() - buildStarted) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        }
        // Arc-length lookup avoids a speed surge caused by nonuniform cubic parameterization.
        float distance = Mathf.Clamp01(fraction) * geometry.length[128];
        int low = 0, high = 128;
        while (high - low > 1)
        {
            int middle = (low + high) / 2;
            if (geometry.length[middle] < distance) low = middle; else high = middle;
        }
        float blend = Mathf.InverseLerp(geometry.length[low], geometry.length[high], distance);
        Vector3 result = Vector3.Lerp(Point(low), Point(high), blend);
        return result;
        Vector3 Point(int index)
        {
            float t = index / 128f;
            float taper = entry ? Mathf.SmoothStep(0f, 1f, t) : 1f - Mathf.SmoothStep(0f, 1f, t);
            Vector3 inward = geometry.inward[index], edge = geometry.edges[index];
            Vector3 center = geometry.centers[index];
            if (formation || wideningPavement)
            {
                // Move both rows toward the racing surface on a narrow apron.
                float remaining = geometry.length[128] - geometry.length[index];
                center -= inward * Mathf.SmoothStep(geometry.formationShift, 0f, remaining / 30f);
            }
            if (wideningPavement)
            {
                // Keep the exit at pit-road width until the apron can support
                // both rows. The ribbon then tapers into the existing pavement.
                float apronClearance = geometry.apronWidth[index] - Vector3.Dot(center - edge, inward);
                float clearanceNeeded = FormationHalfLane + FormationHalfWheelTrack;
                taper = Mathf.Max(taper, 1f - Mathf.SmoothStep(0f, 1f, (apronClearance - clearanceNeeded) / .2f));
            }
            Vector3 point = center + geometry.across[index] * (formation ? lane : lane * taper);
            if (!formation)
                point += inward * Mathf.Max(0f, 1.05f - Vector3.Dot(point - edge, inward));
            // Start with the connector grade and follow the racing surface
            // where the formation crosses the inside stripe.
            point.y = entry ? Mathf.Lerp(edge.y, endHeight + PitSurfaceLift, Mathf.SmoothStep(0f, 1f, t)) :
                Mathf.Lerp(startHeight + PitSurfaceLift, edge.y, Mathf.SmoothStep(0f, 1f, t));
            float edgeOffset = Vector3.Dot(point - edge, inward);
            if (formation && edgeOffset < 0f)
                point.y = Mathf.Max(point.y, edge.y - edgeOffset * geometry.bankRise[index]);
            if (definition.id == BristolTrackId)
            {
                float strip = Mathf.Clamp01(lane / definition.pitRoad.widthMeters + .5f) * 8f;
                int lower = Mathf.Min(7, Mathf.FloorToInt(strip));
                point.y = Mathf.Lerp(geometry.surfaceHeight[index, lower], geometry.surfaceHeight[index, lower + 1], strip - lower);
            }
            return point;
        }
    }

    private Vector3 PitDrivingPosition(TrackPitSpline spline, float fraction, float lane, float startHeight, float endHeight)
    {
        if (UsesAuthoredApronHandoffs || UsesAuthoredLowLanePitRoute)
        {
            // Preserve the authored paths and reach their attachment lanes at both handoffs.
            float fan = spline == pitRoute[0] ? fraction : spline == pitRoute[2] ? 1f - fraction : 1f;
            return RawPitDrivingPosition(spline, fraction, lane * Mathf.SmoothStep(0f, 1f, fan), startHeight, endHeight);
        }
        if (definition.id == GatewayTrackId && bristolConnectorVertices.ContainsKey(spline))
        {
            // Match the bottom lane at the track handoff and fan out to the pit through lane.
            float pitProgress = spline == pitRoute[0] ? fraction : 1f - fraction;
            lane *= Mathf.SmoothStep(0f, 1f, pitProgress);
        }
        // Martinsville's authored path wraps both ends of the paperclip. Keep
        // those curves and taper the through-lane offset as the exit narrows
        // over its final fifth to meet the center of the backstretch apron.
        if (definition.id == MartinsvilleTrackId)
        {
            if (spline == pitRoute[2])
                lane *= Mathf.SmoothStep(0f, 1f, (1f - fraction) / .2f);
            return PitPosition(spline, fraction, startHeight, endHeight) +
                Vector3.Cross(Vector3.up, PitTangent(spline, fraction, startHeight, endHeight)).normalized * lane;
        }
        if ((spline == pitRoute[0] || spline == bristolFrontEntry || spline == pitRoute[2] || spline == bristolBackExit) &&
            ApronExitAvailable(spline == bristolBackExit ? bristolBackExitTrackDistance : pitTraffic.ExitDistance))
            return ApronConnectorPoint(spline, fraction, lane, startHeight, endHeight);
        Vector3 raw = RawPitDrivingPosition(spline, fraction, lane, startHeight, endHeight);
        // Repair the driving tangent at Bristol's split-road joins without changing
        // the racing line, pavement, or existing connectors at other venues.
        bool entry = spline == pitRoute[0] || spline == bristolFrontEntry;
        bool exit = spline == pitRoute[2] || spline == bristolBackExit;
        if (!entry && !exit) return raw;
        if (definition.id != BristolTrackId && (!exit || !ApronExitAvailable(pitTraffic.ExitDistance))) return raw;
        float scale = Vector3.Distance(customLayout.ToTrackSpace(Vector3.zero), customLayout.ToTrackSpace(Vector3.right));
        float length = Mathf.Max(0.01f, spline.Length * scale);
        float span = Mathf.Min(0.35f, 12f / length);
        if (entry ? fraction >= span : fraction <= 1f - span) return raw;
        if (!pitMergeAttachments.TryGetValue(spline, out var attachment))
        {
            Vector3 source = spline.Sample(entry ? 0f : 1f);
            float distance = PitAttachment(source);
            attachment = new Vector2(distance, PitAttachmentLane(source, distance));
            pitMergeAttachments[spline] = attachment;
        }
        float endpointLane = exit && ApronExitAvailable(attachment.x) ? ApronLane(attachment.x) : attachment.y;
        Vector3 endpoint = TrackPoint(attachment.x, endpointLane, out var trackForward, out _);
        float join = entry ? span : 1f - span;
        Vector3 inner = RawPitDrivingPosition(spline, join, lane, startHeight, endHeight);
        Vector3 innerForward = (RawPitDrivingPosition(spline, join + 0.001f, lane, startHeight, endHeight) -
            RawPitDrivingPosition(spline, join - 0.001f, lane, startHeight, endHeight)).normalized;
        float reach = Vector3.Distance(endpoint, inner);
        Vector3 result = entry
            ? PitHermite(endpoint, inner, trackForward * reach, innerForward * reach, fraction / span)
            : PitHermite(inner, endpoint, innerForward * reach, trackForward * reach, (fraction - join) / span);
        // Stay on the actual banked pavement rather than interpolating above it.
        if (definition.id == BristolTrackId) result.y = BristolConnectorHeight(result, out _) + 0.005f;
        return result;
    }

    internal static Vector3 PitHermite(Vector3 start, Vector3 end, Vector3 startTangent, Vector3 endTangent, float t)
    {
        t = Mathf.Clamp01(t);
        float t2 = t * t, t3 = t2 * t;
        return (2f * t3 - 3f * t2 + 1f) * start + (t3 - 2f * t2 + t) * startTangent +
            (-2f * t3 + 3f * t2) * end + (t3 - t2) * endTangent;
    }

    private Vector3 SampleBristolConnector(Vector3[] vertices, float fraction, float lane)
    {
        int stride = PitConnectorStrips + 1;
        float row = Mathf.Clamp01(fraction) * PitRibbonSegments;
        float column = Mathf.Clamp01(0.5f + lane / definition.pitRoad.widthMeters) * PitConnectorStrips;
        int sample = Mathf.Min((int)row, PitRibbonSegments - 1);
        int strip = Mathf.Min((int)column, PitConnectorStrips - 1);
        int index = sample * stride + strip;
        float u = row - sample;
        float v = column - strip;
        // Match the diagonal used to triangulate the rendered ribbon.
        if (u + v <= 1f)
            return vertices[index] + u * (vertices[index + stride] - vertices[index]) +
                v * (vertices[index + 1] - vertices[index]);

        var opposite = vertices[index + stride + 1];
        return opposite + (1f - u) * (vertices[index + 1] - opposite) +
            (1f - v) * (vertices[index + stride] - opposite);
    }

    private Vector3 TrackPoint(float distance, float lane, out Vector3 forward, out Vector3 up)
    {
        var widths = RoadWidths(distance);
        float side = customLayout?.InsideSign ?? -1f;
        if (widths.y > 0f && lane * side > widths.x * .5f + .0001f)
        {
            Vector3 edge = TrackPoint(distance, side * widths.x * .5f, out forward, out _);
            up = Vector3.up;
            return edge + Vector3.Cross(Vector3.up, forward).normalized * (lane - side * widths.x * .5f);
        }
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
        if ((definition.id is KansasTrackId or IowaTrackId) && customLayout != null)
        {
            float width = RoadWidths(distance).x;
            float spread = customLayout.BankingSpread(distance);
            float insideBank = bank - spread * 0.5f;
            float fromInside = lane + width * 0.5f;
            float slope = insideBank + spread * (fromInside / width);
            up = Vector3.Cross(forward, Quaternion.AngleAxis(slope, forward) * outward);
            Vector3 axis = forward;
            Vector3 inner = BankOffset(width * 0.5f);
            inner.y = 0f;
            return center - inner + BankOffset(fromInside);

            Vector3 BankOffset(float length)
            {
                float angle = spread * (length / width) * 0.5f;
                float radians = angle * Mathf.Deg2Rad;
                float chord = length * (Mathf.Abs(radians) < 0.0001f ? 1f : Mathf.Sin(radians) / radians);
                return Quaternion.AngleAxis(insideBank + angle, axis) * outward * chord;
            }
        }
        Vector3 crossSlope = Quaternion.AngleAxis(bank, forward) * outward;
        up = Vector3.Cross(forward, crossSlope);
        return center + crossSlope * lane + Vector3.up * (RoadWidths(distance).x * 0.5f * Mathf.Abs(crossSlope.y));
    }

    private Vector2 RoadWidths(float distance) => customLayout?.Widths(Mathf.Repeat(distance, lapLength))
        ?? new Vector2(roadHalfWidth * 2f, 0f);

    internal bool HasPreferredLineOverlay => preferredLineRenderer != null;
    internal bool PreferredLineVisible { get; private set; } = true;

    internal void SetPreferredLineVisible(bool visible)
    {
        PreferredLineVisible = visible;
        if (preferredLineRenderer != null)
            preferredLineRenderer.enabled = visible;
    }

    private void BuildPreferredLineOverlay()
    {
        if (definition.majorStyle != "Oval" || racingLine == null || lapLength <= 0f)
            return;

        const float halfStripeWidth = .6f;
        const float surfaceClearance = .08f;
        const string label = "Preferred driving line";
        if (preferredLineMesh == null)
        {
            preferredLineMesh = new Mesh { name = label };
            roadMeshes.Add(preferredLineMesh);
            var stripe = CreateObject(label, transform);
            stripe.AddComponent<MeshFilter>().sharedMesh = preferredLineMesh;
            var renderer = stripe.AddComponent<MeshRenderer>();
            preferredLineRenderer = renderer;
            renderer.enabled = PreferredLineVisible;
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            Material material = unlit != null ? new Material(unlit) : new Material(surfaceMaterial);
            material.SetColor("_BaseColor", Color.magenta);
            materials.Add(material);
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        int samples = Mathf.Clamp(Mathf.CeilToInt(lapLength), 128, 8192);
        var vertices = new Vector3[samples * 2];
        var triangles = new int[samples * 6];
        for (int i = 0; i < samples; i++)
        {
            float distance = i * lapLength / samples;
            float width = RoadWidths(distance).x;
            float lane = PreferredLaneOffset(0, width, float.PositiveInfinity, racingLine(distance));
            float halfWidth = width * .5f;
            float left = Mathf.Clamp(lane - halfStripeWidth, -halfWidth, halfWidth);
            float right = Mathf.Clamp(lane + halfStripeWidth, -halfWidth, halfWidth);
            int edge = i * 2;
            int next = (edge + 2) % vertices.Length;
            vertices[edge] = TrackPoint(distance, left, out _, out var leftUp) + leftUp * surfaceClearance;
            vertices[edge + 1] = TrackPoint(distance, right, out _, out var rightUp) + rightUp * surfaceClearance;
            int triangle = i * 6;
            triangles[triangle] = edge;
            triangles[triangle + 1] = next;
            triangles[triangle + 2] = edge + 1;
            triangles[triangle + 3] = edge + 1;
            triangles[triangle + 4] = next;
            triangles[triangle + 5] = next + 1;
        }

        preferredLineMesh.Clear();
        preferredLineMesh.vertices = vertices;
        preferredLineMesh.triangles = triangles;
        preferredLineMesh.RecalculateNormals();
    }

    private void AddDashedLaneMarkings(float finishDistance, Material paint)
    {
        var coverage = definition.dashedLaneMarkings;
        if (definition.majorStyle != "Oval" || coverage == TrackLaneMarkingCoverage.None)
            return;

        const float targetSpacing = 8f;
        const float dashLength = 2.5f;
        const float halfPaintWidth = 0.10f;
        const float bottomRowInset = 0.9f;
        const int subdivisions = 4;
        // Paint sits above the pavement and below the rubber overlay (0.055 m).
        const float paintLift = 0.035f;
        int outerRow = customLayout?.InsideSign > 0f ? -1 : 1;
        float zoneStart = definition.dashedLaneZoneStart * lapLength;
        float zoneLength = Mathf.Repeat(definition.dashedLaneZoneEnd * lapLength - zoneStart, lapLength);
        int count = Mathf.Clamp(Mathf.RoundToInt(lapLength / targetSpacing), 1, 2048);
        float spacing = lapLength / count;
        float halfDash = Mathf.Min(dashLength, spacing * 0.5f) * 0.5f;
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        for (int dash = 0; dash < count; dash++)
        {
            // Center a gap on the start/finish stripe.
            float center = finishDistance + (dash + 0.5f) * spacing;
            int firstRow = -1;
            int lastRow = 1;
            switch (coverage)
            {
                case TrackLaneMarkingCoverage.StraightsOnly:
                    if (!IsStraightForLaneMarking(center, halfDash))
                        continue;
                    break;
                case TrackLaneMarkingCoverage.StraightsAndOuterRow:
                    if (!IsStraightForLaneMarking(center, halfDash))
                        firstRow = lastRow = outerRow;
                    break;
                case TrackLaneMarkingCoverage.BottomRow:
                    firstRow = lastRow = -outerRow;
                    break;
                case TrackLaneMarkingCoverage.OuterRowInZone:
                    float zoneOffset = Mathf.Repeat(center - halfDash - zoneStart, lapLength);
                    if (zoneOffset + 2f * halfDash > zoneLength)
                        continue;
                    firstRow = lastRow = outerRow;
                    break;
            }
            for (int lane = firstRow; lane <= lastRow; lane++)
            {
                for (int step = 0; step <= subdivisions; step++)
                {
                    float station = center + Mathf.Lerp(-halfDash, halfDash, step / (float)subdivisions);
                    float width = RoadWidths(station).x;
                    float laneCenter = coverage == TrackLaneMarkingCoverage.BottomRow
                        ? lane * (width * 0.5f - bottomRowInset)
                        : lane * width * 0.25f;
                    int row = vertices.Count;
                    Vector3 left = TrackPoint(station, laneCenter - halfPaintWidth, out _, out var leftUp);
                    Vector3 right = TrackPoint(station, laneCenter + halfPaintWidth, out _, out var rightUp);
                    vertices.Add(left + leftUp * paintLift);
                    vertices.Add(right + rightUp * paintLift);
                    if (step == 0)
                        continue;

                    triangles.Add(row - 2);
                    triangles.Add(row);
                    triangles.Add(row - 1);
                    triangles.Add(row - 1);
                    triangles.Add(row);
                    triangles.Add(row + 1);
                }
            }
        }

        if (vertices.Count > 0)
            AddRoadMesh("Dashed lane markings", paint, vertices.ToArray(), triangles.ToArray(), castShadows: false);
    }

    private bool IsStraightForLaneMarking(float center, float halfDash)
    {
        // Measure horizontal curvature so banking and elevation changes do not suppress paint.
        const float straightTolerance = 2f;
        float probe = halfDash + 6f;
        TrackPoint(center - probe, 0f, out var before, out _);
        TrackPoint(center, 0f, out var middle, out _);
        TrackPoint(center + probe, 0f, out var after, out _);
        before.y = middle.y = after.y = 0f;
        return Vector3.Angle(before, middle) <= straightTolerance &&
            Vector3.Angle(middle, after) <= straightTolerance &&
            Vector3.Angle(before, after) <= straightTolerance;
    }

    private void Ring(string objectName, float inner, float outer, float height, Material material, bool outerEdgeAtGround = false, bool innerEdgeAtGround = false, bool apron = false, bool alongTrackUv = false)
    {
        // Keep neighboring Charlotte strips on identical, denser rows so the
        // triangles follow the shared bank profile between layout samples.
        int subdivisions = definition.id == CharlotteTrackId && customLayout != null ? 4 : 1;
        int samples = (customLayout?.Samples ?? SamplesPerSection * 4) * subdivisions;
        var vertices = new Vector3[(samples + (alongTrackUv ? 1 : 0)) * 2];
        var uv = alongTrackUv ? new Vector2[vertices.Length] : null;
        var triangles = new int[samples * 6];
        for (int i = 0; i < samples; i++)
        {
            int section = i / SamplesPerSection;
            float fraction = (float)(i % SamplesPerSection) / SamplesPerSection;
            int layoutSample = i / subdivisions;
            float distance = customLayout?.SampleDistance(layoutSample) ?? (straightLength == 0f
                ? lapLength * i / samples
                : (section / 2) * (straightLength + turnLength)
                + (section % 2 == 0 ? fraction * straightLength : straightLength + fraction * turnLength));
            int substep = i % subdivisions;
            if (substep != 0)
                distance = Mathf.Lerp(distance, customLayout.SampleDistance(layoutSample + 1), (float)substep / subdivisions);

            int current = i * 2;
            int next = (current + 2) % vertices.Length;
            if (uv != null)
            {
                float insideV = apron && customLayout.InsideSign > 0f ? 1f : 0f;
                uv[current] = new Vector2(distance / lapLength, insideV);
                uv[current + 1] = new Vector2(distance / lapLength, apron ? insideV : 1f);
            }
            Vector2 widths = RoadWidths(distance);
            float halfWidth = widths.x * 0.5f;
            if (apron)
            {
                float side = customLayout.InsideSign;
                Vector3 edge = TrackPoint(distance, halfWidth * side, out var forward, out _);
                if (definition.id != CharlotteTrackId)
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

        if (uv != null)
        {
            vertices[samples * 2] = vertices[0];
            vertices[samples * 2 + 1] = vertices[1];
            uv[samples * 2] = new Vector2(1f, uv[0].y);
            uv[samples * 2 + 1] = new Vector2(1f, uv[1].y);
        }
        if (apron && definition.id == CharlotteTrackId)
            charlotteApronVertices = vertices;
        AddRoadMesh(objectName, material, vertices, triangles, uv);
    }

    private void AddBankedOutsideWall()
    {
        float side = -customLayout.InsideSign;
        var concrete = MakeMaterial(new Color32(190, 190, 181, 255));
        concrete.SetFloat("_Smoothness", 0.14f);
        var steel = MakeMaterial(new Color32(95, 104, 111, 255));
        BuildBand("Outside SAFER wall", 0f, 0.45f, -0.05f, 1.12f, concrete);
        for (int i = 0; i < 4; i++)
        {
            float height = 1.55f + i * 0.85f;
            BuildBand("Catch fence cable", 0.28f, 0.04f, height, height + 0.04f, steel);
        }

        int samples = customLayout.Samples;
        for (int i = 0; i < 240; i++)
        {
            var edge = GetEdge(i * samples / 240, out var outside);
            Box("Catch fence post", transform, edge + outside * 0.30f + Vector3.up * 2.61f,
                new Vector3(0.09f, 3.02f, 0.09f), steel);
        }

        Vector3 GetEdge(int sample, out Vector3 outside)
        {
            float distance = customLayout.SampleDistance(sample);
            var edge = TrackPoint(distance, side * RoadWidths(distance).x * 0.5f, out var forward, out _);
            outside = Vector3.Cross(Vector3.up, forward).normalized * side;
            return edge;
        }

        void BuildBand(string name, float offset, float thickness, float bottom, float top, Material material)
        {
            int count = customLayout.Samples;
            var vertices = new Vector3[count * 4];
            var triangles = new int[count * 24];
            int triangle = 0;
            for (int i = 0; i < count; i++)
            {
                var edge = GetEdge(i, out var outside) + outside * offset;
                int row = i * 4;
                int next = (i + 1) % count * 4;
                vertices[row] = edge + Vector3.up * bottom;
                vertices[row + 1] = vertices[row] + outside * thickness;
                vertices[row + 2] = edge + outside * thickness + Vector3.up * top;
                vertices[row + 3] = edge + Vector3.up * top;
                for (int j = 0; j < 4; j++)
                {
                    int k = (j + 1) % 4;
                    int a = row + j;
                    int b = next + j;
                    int c = next + k;
                    int d = row + k;
                    triangles[triangle++] = a;
                    triangles[triangle++] = side > 0f ? d : b;
                    triangles[triangle++] = c;
                    triangles[triangle++] = a;
                    triangles[triangle++] = c;
                    triangles[triangle++] = side > 0f ? b : d;
                }
            }

            AddRoadMesh(name, material, vertices, triangles);
        }
    }

    private void AddRoadMesh(string objectName, Material material, Vector3[] vertices, int[] triangles, Vector2[] uv = null,
        bool castShadows = true)
    {
        var mesh = new Mesh { name = objectName, vertices = vertices, triangles = triangles };
        if (uv != null)
            mesh.uv = uv;
        else if (material.GetTexture("_BaseMap") != null)
            SetPavementCoordinates(mesh);
        mesh.RecalculateNormals();
        roadMeshes.Add(mesh);
        var surface = CreateObject(objectName, transform);
        surface.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = surface.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        if (!castShadows || (definition.id == BristolTrackId && (objectName == "Pit entrance" || objectName == "Pit exit" ||
            objectName == "Bristol Turn 4 entry" || objectName == "Bristol Turn 3 exit")))
            renderer.shadowCastingMode = ShadowCastingMode.Off;
    }

    private GameObject bristolCrowd;

    private void SetBristolAttendance(RaceSessionKind session)
    {
        if (bristolCrowd == null)
        {
            var model = Resources.Load<GameObject>("Startup/BristolCrowd");
            if (model == null)
                return;

            bristolCrowd = Instantiate(model, transform, false);
            bristolCrowd.name = "Bristol spectators";
            foreach (var renderer in bristolCrowd.GetComponentsInChildren<MeshRenderer>(true))
            {
                renderer.gameObject.layer = PreviewLayer;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        var qualifying = bristolCrowd.transform.Find("AttendanceTier1");
        var race = bristolCrowd.transform.Find("AttendanceTier2");
        if (qualifying != null)
            qualifying.gameObject.SetActive(session == RaceSessionKind.Qualifying || session == RaceSessionKind.Race);
        if (race != null)
            race.gameObject.SetActive(session == RaceSessionKind.Race);
    }

    private static bool UsesDetailedPavement(int trackId) =>
        trackId == DarlingtonTrackId || trackId == BristolTrackId || trackId == OrangeCountyTrackId || trackId == SouthBostonTrackId ||
        trackId == DaytonaTrackId || trackId == TexasTrackId || trackId == KansasTrackId || trackId == CotaTrackId ||
        trackId == MartinsvilleTrackId || trackId == GatewayTrackId || trackId == IowaTrackId ||
        trackId == PhoenixTrackId || trackId == RichmondTrackId || trackId == NorthWilkesboroTrackId;

    private static bool UsesBottomLanePitMerge(int trackId) =>
        trackId == BristolTrackId || trackId == DarlingtonTrackId || trackId == DaytonaTrackId || trackId == TexasTrackId ||
        trackId == KansasTrackId || trackId == CotaTrackId || trackId == MartinsvilleTrackId ||
        trackId == GatewayTrackId || trackId == IowaTrackId || trackId == PhoenixTrackId || trackId == RichmondTrackId || trackId == NorthWilkesboroTrackId;

    private bool AddImportedTrackGeometry(string resourceName)
    {
        var model = Resources.Load<GameObject>("Startup/" + resourceName);
        if (model == null)
        {
            Debug.LogWarning($"Track geometry is missing: {resourceName}.", this);
            return false;
        }

        var venue = Instantiate(model, transform, false);
        var finish = customLayout.ToSourceSpace(TrackPoint(definition.StartFinishFraction * lapLength, 0f, out _, out _));
        var stripe = System.Array.Find(venue.GetComponentsInChildren<MeshRenderer>(),
            renderer => renderer.name.StartsWith("Start finish checker", System.StringComparison.Ordinal));
        float scale = (customLayout.ToTrackSpace(Vector3.right) - customLayout.ToTrackSpace(Vector3.zero)).x;
        Vector3 stripeCenter = stripe != null ? transform.InverseTransformPoint(stripe.bounds.center) : finish;
        // An FBX axis conversion can reverse either horizontal axis. Kansas
        // has its timing stripe on the X origin, so that axis is ambiguous there.
        float axisTolerance = definition.id is IowaTrackId or PhoenixTrackId or RichmondTrackId ? 1f : 10f;
        float horizontal = Mathf.Abs(finish.x) > axisTolerance && stripeCenter.x * finish.x < 0f ? -scale : scale;
        float depth = stripeCenter.z * finish.z < 0f ? -scale : scale;
        // These finish stripes sit on the nearly symmetric models. Use the
        // authored restart start to resolve their horizontal orientation.
        if ((definition.id is MartinsvilleTrackId or KansasTrackId) && definition.raceMarkers?.hasRestartZone == true)
        {
            bool kansas = definition.id == KansasTrackId;
            var renderers = venue.GetComponentsInChildren<MeshRenderer>();
            string markerName = kansas ? "Restart zone Start" : "Restart zone endpoint";
            var restart = System.Array.Find(renderers, renderer => renderer.name == markerName);
            if (kansas && restart == null)
                restart = System.Array.Find(renderers, renderer => renderer.name == "Restart zone boundary stripe");
            if (restart != null)
            {
                float station = definition.raceMarkers.restartZoneStart * lapLength;
                float lane = kansas ? -customLayout.InsideSign * (RoadWidths(station).x * .5f - 1.35f) : 0f;
                var expected = customLayout.ToSourceSpace(TrackPoint(station, lane, out _, out _));
                var actual = transform.InverseTransformPoint(restart.bounds.center);
                horizontal = expected.x * actual.x < 0f ? -scale : scale;
            }
        }
        // An off-axis marker resolves both FBX signs even when the timing stripe is centered.
        string orientationMarker = definition.id switch
        {
            MichiganTrackId => "Michigan import orientation",
            NorthWilkesboroTrackId => "North Wilkesboro import orientation",
            _ => null
        };
        if (orientationMarker != null)
        {
            var anchor = System.Array.Find(venue.GetComponentsInChildren<Transform>(true),
                child => child.name == orientationMarker);
            if (anchor != null)
            {
                HideAuthoredVenueMarker(anchor);
                Vector3 axes = venue.transform.InverseTransformPoint(anchor.position);
                horizontal = Mathf.Sign(axes.x) * scale;
                depth = Mathf.Sign(axes.z) * scale;
            }
        }
        venue.transform.localScale = new Vector3(horizontal, 1f, depth);
        venue.transform.localPosition = customLayout.ToTrackSpace(Vector3.zero);
        ApplyMatteTrackScenery(venue);
        foreach (var child in venue.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = PreviewLayer;
        if (definition.id == MichiganTrackId)
            michiganVenue = venue;
        return true;
    }

    private (Vector3 Position, Vector3 Outward) SampleOutsideEdge(float station)
    {
        float side = -customLayout.InsideSign;
        Vector3 edge = TrackPoint(station, side * RoadWidths(station).x * .5f, out var forward, out _);
        Vector3 outward = Vector3.Cross(Vector3.up, forward).normalized * side;
        return (edge, outward);
    }

    private void AddTrackScenery(int trackId)
    {
        if (trackId == MichiganTrackId && importedTrackGeometry)
        {
            AddMichiganVenue();
            return;
        }
        if ((trackId is PhoenixTrackId or RichmondTrackId or NorthWilkesboroTrackId) && importedTrackGeometry) return;
        if (trackId == IowaTrackId && customLayout != null)
        {
            AddIowaScenery();
            return;
        }
        if (trackId == MartinsvilleTrackId && customLayout != null)
        {
            grandstandScenery = TrackGrandstandScenery.AddMartinsville(transform, PreviewLayer, lapLength, groundHeight,
                SampleOutsideEdge, MakeMaterial, roadMeshes);
            return;
        }

        if (trackId == OrangeCountyTrackId || trackId == SouthBostonTrackId || trackId == DaytonaTrackId || trackId == TexasTrackId)
        {
            var sceneryName = trackId switch
            {
                DaytonaTrackId => "DaytonaInternationalSpeedway",
                TexasTrackId => "TexasMotorSpeedway",
                SouthBostonTrackId => "SouthBostonSpeedway",
                _ => "OrangeCountySpeedway"
            };
            string fasciaName = trackId switch
            {
                DaytonaTrackId => "Daytona Rising exterior facade",
                TexasTrackId => "Grandstand facade and suite level",
                SouthBostonTrackId => "Main center grandstand fascia",
                _ => "Red grandstand fascia"
            };
            var scenery = Resources.Load<GameObject>("Startup/" + sceneryName);
            if (scenery == null)
            {
                Debug.LogWarning($"Track scenery is missing: {sceneryName}.", this);
                return;
            }

            var venue = Instantiate(scenery, transform, false);
            // Keep the imported frontstretch on the same side as the timing line.
            var grandstand = System.Array.Find(venue.GetComponentsInChildren<MeshRenderer>(),
                renderer => renderer.name.StartsWith(fasciaName, System.StringComparison.Ordinal));
            var finish = TrackPoint(definition.StartFinishFraction * lapLength, 0f, out _, out _);
            if (grandstand != null && transform.InverseTransformPoint(grandstand.bounds.center).z * finish.z < 0f)
            {
                var scale = venue.transform.localScale;
                scale.z = -scale.z;
                venue.transform.localScale = scale;
            }

            if (trackId == TexasTrackId && customLayout != null)
            {
                // The scenery and layout controls share the Blender source coordinates.
                float scale = (customLayout.ToTrackSpace(Vector3.right) - customLayout.ToTrackSpace(Vector3.zero)).x;
                venue.transform.localScale = Vector3.Scale(venue.transform.localScale, new Vector3(scale, 1f, scale));
                venue.transform.localPosition = customLayout.ToTrackSpace(Vector3.zero);
                if (pitTraffic != null)
                    foreach (var renderer in venue.GetComponentsInChildren<MeshRenderer>())
                        if (renderer.name is "Cup garage" or "Cup garage roof" or "Cup garage service doors")
                            renderer.gameObject.SetActive(false);
            }

            ApplyMatteTrackScenery(venue);
            foreach (var child in venue.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = PreviewLayer;
            AddLargeTrackGrandstands(venue);
            return;
        }

        if (trackId != BristolTrackId)
        {
            AddLargeTrackGrandstands(null);
            return;
        }

        var model = Resources.Load<GameObject>("Startup/BristolGrandstands");
        if (model == null)
        {
            Debug.LogWarning("Bristol grandstands model is missing.", this);
            return;
        }

        var stands = Instantiate(model, transform, false);
        stands.name = "Bristol Grandstands";
        foreach (var child in stands.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = PreviewLayer;

        ApplyMatteTrackScenery(stands);
        AddLargeTrackGrandstands(stands);
    }

    private void AddLargeTrackGrandstands(GameObject importedScenery)
    {
        if (customLayout == null)
            return;

        grandstandScenery = TrackGrandstandScenery.AddLargeTrack(definition.id, transform, PreviewLayer,
            lapLength, definition.StartFinishFraction, groundHeight, SampleOutsideEdge, MakeMaterial, roadMeshes,
            importedScenery, importedGrandstandRenderers);
    }

    private void ApplyMatteTrackScenery(GameObject scenery)
    {
        var matteMaterials = new Dictionary<Material, Material>();
        var scoreboardPaint = definition.id == NorthWilkesboroTrackId
            ? Resources.Load<Material>("Startup/NorthWilkesboroSign") : null;
        foreach (var renderer in scenery.GetComponentsInChildren<Renderer>())
        {
            if (renderer.name.StartsWith("Catch fence", System.StringComparison.Ordinal))
                renderer.shadowCastingMode = ShadowCastingMode.Off;

            var slots = renderer.sharedMaterials;
            for (int i = 0; i < slots.Length; i++)
            {
                var source = slots[i];
                if (source == null)
                    continue;
                if (!matteMaterials.TryGetValue(source, out var matte))
                {
                    bool iowaAsphalt = source.name.StartsWith("Iowa asphalt", System.StringComparison.Ordinal);
                    Color color = definition.id == IowaTrackId ? IowaImportedMaterialColor(source) : source.color;
                    if (definition.id == NorthWilkesboroTrackId && source.HasProperty("_BaseColor"))
                        color = source.GetColor("_BaseColor");
                    if (scoreboardPaint != null &&
                        source.name.StartsWith("North Wilkesboro Scoreboard ", System.StringComparison.Ordinal))
                    {
                        // Keep the painted scoreboard legible from the track when its face is in shadow.
                        matte = new Material(scoreboardPaint);
                        matte.SetColor("_BaseColor", color);
                        materials.Add(matte);
                    }
                    else
                    {
                        matte = MakeMaterial(color);
                        bool glazing = source.name.StartsWith("Suite glazing", System.StringComparison.Ordinal) ||
                            definition.id == NorthWilkesboroTrackId && source.name.Contains("glass", System.StringComparison.OrdinalIgnoreCase);
                        matte.SetFloat("_Smoothness", glazing ? 0.32f : 0.14f);
                    }
                    bool asphalt = iowaAsphalt || source.name.StartsWith("Infield \u2022 asphalt", System.StringComparison.Ordinal);
                    bool concrete = source.name.StartsWith("Infield buildings \u2022 concrete", System.StringComparison.Ordinal) ||
                        source.name.StartsWith("Iowa wall concrete", System.StringComparison.Ordinal) ||
                        source.name == "Iowa walkway concrete";
                    if (definition.id == MartinsvilleTrackId)
                    {
                        asphalt |= source.name.StartsWith("Racing asphalt", System.StringComparison.Ordinal) ||
                            source.name.StartsWith("Apron asphalt", System.StringComparison.Ordinal);
                        concrete |= source.name.StartsWith("Racing concrete", System.StringComparison.Ordinal) ||
                            source.name.StartsWith("Pit concrete", System.StringComparison.Ordinal);
                    }
                    if (definition.id == RichmondTrackId)
                    {
                        asphalt |= source.name is "Richmond circuit asphalt" or "Paddock asphalt";
                        concrete |= source.name is "Richmond pit concrete" or "Richmond walkway concrete" or "Paddock concrete";
                    }
                    if (definition.id == NorthWilkesboroTrackId)
                        asphalt |= source.name is "North Wilkesboro circuit asphalt" or
                            "North Wilkesboro pit road asphalt" or "North Wilkesboro paved infield";
                    if (asphalt || concrete)
                        matte.SetTexture("_BaseMap", BristolPavementTexture(concrete));
                    matteMaterials.Add(source, matte);
                }
                slots[i] = matte;
            }
            renderer.sharedMaterials = slots;
        }
    }

    private void SetDetailedTrackLighting(bool night)
    {
        bristolSun.intensity = night ? 0.10f : 0.78f;
        bristolSun.color = night ? new Color32(167, 186, 225, 255) : (Color32)Color.white;
        bristolSun.shadows = night ? LightShadows.None : LightShadows.Soft;

        if (!SupportsRaceNightLighting)
            return;

        bristolFloodlightLens.SetColor("_EmissionColor", night ? new Color(1f, 0.88f, 0.66f) * 2f : Color.black);
        if (night)
            bristolFloodlightLens.EnableKeyword("_EMISSION");
        else
            bristolFloodlightLens.DisableKeyword("_EMISSION");
        foreach (var light in bristolFloodlights)
            light.enabled = night;
    }

    private void AddBristolFloodlights()
    {
        var structure = MakeMaterial(new Color32(68, 72, 76, 255));
        bristolFloodlightLens = MakeMaterial(new Color32(201, 200, 185, 255));

        for (int i = 0; i < 32; i++)
        {
            float distance = (i + 0.5f) * lapLength / 32f;
            customLayout.Sample(distance, out var forward, out _);
            var outside = Vector3.Cross(Vector3.up, forward).normalized * -customLayout.InsideSign;
            var surface = TrackPoint(distance, 0f, out _, out _);
            var rim = surface + outside * 54f;
            rim.y = 37f;
            AddFixture("Grandstand floodlight", rim, surface - outside * 32f, 6f, 2.8f, 240f, 8280f, 106f);
        }

        foreach (int turnStart in new[] { 13, 45 })
        for (int i = 0; i < 8; i++)
        {
            int control = turnStart + i * 2;
            float distance = customLayout.ControlFraction(control) * lapLength;
            customLayout.Sample(distance, out var forward, out _);
            var inside = Vector3.Cross(Vector3.up, forward).normalized * customLayout.InsideSign;
            var surface = TrackPoint(distance, 0f, out _, out _);
            var basePosition = surface + inside * (turnStart == 13 ? 17f : 21f);
            basePosition.y = 0f;
            AddFixture("Inside turn floodlight", basePosition, surface, 2.4f, 0.65f, 39f, 470f, 92f);
        }

        void AddFixture(string name, Vector3 basePosition, Vector3 aim, float height, float width,
            float range, float intensity, float angle)
        {
            var fixture = CreateObject(name, transform).transform;
            fixture.localPosition = basePosition + Vector3.up * height;
            fixture.localRotation = Quaternion.LookRotation(aim - fixture.localPosition);
            Box("Mount", transform, basePosition + Vector3.up * (height * 0.5f),
                new Vector3(0.16f, height, 0.16f), structure);
            Box("Housing", fixture, Vector3.zero, new Vector3(width, width * 0.34f, 0.24f), structure);
            Box("Lamp face", fixture, Vector3.forward * 0.13f, new Vector3(width * 0.9f, width * 0.27f, 0.03f), bristolFloodlightLens);

            var light = fixture.gameObject.AddComponent<Light>();
            light.enabled = false;
            light.type = LightType.Spot;
            light.color = new Color32(255, 232, 200, 255);
            light.intensity = intensity;
            light.range = range;
            light.spotAngle = angle;
            light.innerSpotAngle = angle * 0.65f;
            light.cullingMask = 1 << PreviewLayer;
            light.GetUniversalAdditionalLightData().renderingLayers = BristolLightingLayer;
            bristolFloodlights.Add(light);
        }
    }

    private void AddBristolParkingLines()
    {
        var vertices = new System.Collections.Generic.List<Vector3>();
        var triangles = new System.Collections.Generic.List<int>();
        float start = BristolBayCenterX - BristolBaysPerRow * BristolBayPitch / 2f;
        float end = start + BristolBaysPerRow * BristolBayPitch;
        const float lineWidth = 0.16f;
        foreach (float side in new[] { -1f, 1f })
        {
            AddLine(start, end, side * 4.5f - lineWidth / 2f, side * 4.5f + lineWidth / 2f);
            AddLine(start, end, side * 27f - lineWidth / 2f, side * 27f + lineWidth / 2f);
            for (int i = 0; i <= BristolBaysPerRow; i++)
            {
                float x = start + i * BristolBayPitch;
                AddLine(x - lineWidth / 2f, x + lineWidth / 2f,
                    Mathf.Min(side * 4.5f, side * 27f), Mathf.Max(side * 4.5f, side * 27f));
            }
        }

        AddRoadMesh("Bristol hauler parking lines", MakeMaterial(new Color32(213, 176, 63, 255)),
            vertices.ToArray(), triangles.ToArray());

        void AddLine(float x0, float x1, float z0, float z1)
        {
            const float height = -0.012f;
            int index = vertices.Count;
            vertices.Add(new Vector3(x0, height, z0));
            vertices.Add(new Vector3(x0, height, z1));
            vertices.Add(new Vector3(x1, height, z1));
            vertices.Add(new Vector3(x1, height, z0));
            triangles.AddRange(new[] { index, index + 1, index + 2, index, index + 2, index + 3 });
        }
    }

    private void ParkBristolHaulers(CarSimulationContext context)
    {
        var model = Resources.Load<GameObject>("Startup/RaceHauler");
        if (model == null)
        {
            Debug.LogWarning("Race hauler model is missing.", this);
            return;
        }

        parkedHaulers = CreateObject("Bristol parked haulers", transform);
        int count = BristolBaysPerRow * 2;
        var paint = new MaterialPropertyBlock();
        var random = new System.Random();
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        for (int i = 0; i < count; i++)
        {
            var entry = i < context.Cars.Count ? context.Cars[i] : null;
            float side = i % 2 == 0 ? -1f : 1f;
            int bay = i / 2;
            float x = BristolBayCenterX + (bay + 0.5f - BristolBaysPerRow / 2f) * BristolBayPitch;
            var hauler = Instantiate(model, parkedHaulers.transform, false);
            hauler.name = entry != null ? $"Hauler No. {entry.DisplayCarNumber}" : $"Guest hauler {i + 1}";
            hauler.transform.localPosition = new Vector3(x, -0.025f, side * BristolHaulerCenterZ);
            var tractor = hauler.transform.Find("Tractor")?.GetComponent<Renderer>();
            var trailer = hauler.transform.Find("Trailer")?.GetComponent<Renderer>();
            bool facesForward = tractor == null || trailer == null || tractor.bounds.center.z > trailer.bounds.center.z;
            if (facesForward == (side > 0f))
                hauler.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            foreach (var child in hauler.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = PreviewLayer;
            var livery = entry == null
                ? (HoodAndTrunk: RandomHaulerPaint(random), RoofAndSides: RandomHaulerPaint(random))
                : entry.car?.Paint ?? TeamLivery(entry.carNumber);
            Color roofColor = Color.Lerp(livery.RoofAndSides, livery.HoodAndTrunk, 0.35f);
            if (entry != null && trailer != null && font != null)
            {
                var bounds = trailer.bounds;
                var roof = new Vector3(bounds.center.x, bounds.max.y + 0.035f, bounds.center.z);
                var label = CreateObject("Hauler roof number", hauler.transform);
                label.transform.position = roof;
                label.transform.localRotation = Quaternion.Euler(90f, facesForward ? 0f : 180f, 0f);
                var number = label.AddComponent<TextMesh>();
                number.font = font;
                number.text = entry.DisplayCarNumber;
                number.anchor = TextAnchor.MiddleCenter;
                if (i > 0 && entry.car?.PaintStyle is CarPaintStyle paintStyle)
                    CarNumberFonts.Apply(number, paintStyle);
                else
                    number.fontStyle = FontStyle.Bold;
                number.fontSize = 64;
                number.characterSize = 0.30f;
                number.color = CarNumberFonts.ContrastColor(roofColor);
                label.GetComponent<MeshRenderer>().sharedMaterial = GetCarDecalMaterial(number.font);
            }
            PaintHauler(hauler.transform, livery, roofColor, paint);
        }
    }

    private static void PaintHauler(Transform hauler, (Color32 HoodAndTrunk, Color32 RoofAndSides) livery,
        Color roofColor, MaterialPropertyBlock paint)
    {
        foreach (var renderer in hauler.GetComponentsInChildren<Renderer>())
        {
            var shared = renderer.sharedMaterials;
            for (int j = 0; j < shared.Length; j++)
            {
                var material = shared[j];
                if (material == null)
                    continue;
                bool primary = material.name.StartsWith("Hauler Primary Paint", System.StringComparison.Ordinal);
                bool roof = material.name.StartsWith("Hauler Roof Paint", System.StringComparison.Ordinal);
                if (!primary && !roof && !material.name.StartsWith("Hauler Accent Paint", System.StringComparison.Ordinal))
                    continue;

                Color color = roof ? roofColor : primary ? livery.RoofAndSides : livery.HoodAndTrunk;
                paint.Clear();
                paint.SetFloat("_Smoothness", roof ? 0.16f : 0.28f);
                paint.SetColor("_BaseColor", color);
                paint.SetColor("_Color", color);
                renderer.SetPropertyBlock(paint, j);
            }
        }
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
        if (definition.id == CharlotteTrackId)
        {
            // Charlotte's banking pivots at the inside edge, on the level apron datum.
            float apronHeight = TrackPoint(nearestTrack, ApronLane(nearestTrack), out _, out _).y;
            roadStart = roadEnd = apronHeight - PitSurfaceLift;
        }
        DrawPitRibbon("Pit road", spline, pit.widthMeters, pavement, roadStart, roadEnd);
        var entrance = pit.entrance?.Length >= 2 ? new TrackPitSpline(pit.entrance) : null;
        var exit = pit.exit?.Length >= 2 ? new TrackPitSpline(pit.exit) : null;
        float entryDistance = entrance == null ? 0f : PitAttachment(entrance.Sample(0f));
        float exitDistance = exit == null ? 0f : PitAttachment(exit.Sample(1f));
        // Match PitDrivingPosition's apron decision at the actual exit station.
        bool sharedConnectorSurface = UsesSharedPitConnectorSurface && entrance != null && exit != null && ApronExitAvailable(exitDistance);
        float entryHeight = 0f;
        float exitHeight = 0f;
        if (entrance != null)
        {
            entryHeight = UsesAuthoredApronHandoffs
                ? TrackPoint(entryDistance, ApronLane(entryDistance), out _, out _).y - .02f
                : UsesDetailedPavement(definition.id) ? BristolMergeHeight(entrance.Sample(0f), entryDistance)
                : TrackPoint(entryDistance, 0f, out _, out _).y;
            if (!sharedConnectorSurface)
                DrawPitRibbon("Pit entrance", entrance, pit.widthMeters, pavement, entryHeight, roadStart, underMainTrack: true);
        }
        if (exit != null)
        {
            exitHeight = UsesAuthoredApronHandoffs
                ? TrackPoint(exitDistance, ApronLane(exitDistance), out _, out _).y - .02f
                : UsesDetailedPavement(definition.id) ? BristolMergeHeight(exit.Sample(1f), exitDistance)
                : TrackPoint(exitDistance, 0f, out _, out _).y;
            if (!sharedConnectorSurface)
                DrawPitRibbon("Pit exit", exit, pit.widthMeters, pavement, roadEnd, exitHeight, underMainTrack: true,
                    startTangent: definition.id == BristolTrackId ? PitTangent(spline, 1f, roadStart, roadEnd) : null);
        }
        if (entrance != null && exit != null)
        {
            float sourceScale = Vector3.Distance(customLayout.ToTrackSpace(Vector3.zero), customLayout.ToTrackSpace(Vector3.right));
            pitRoute = new[] { entrance, spline, exit };
            pitLengths = new[] { entrance.Length * sourceScale, spline.Length * sourceScale, exit.Length * sourceScale };
            pitHeights = new[] { entryHeight, roadStart, roadEnd, exitHeight };
            if (sharedConnectorSurface)
            {
                // Build both paths before creating traffic so its segment distances
                // follow the same arc lengths as the paved connectors.
                ApronConnectorPoint(entrance, 0f, 0f, entryHeight, roadStart);
                ApronConnectorPoint(exit, 0f, 0f, roadEnd, exitHeight);
                pitLengths[0] = apronConnectors[entrance].length[128];
                pitLengths[2] = apronConnectors[exit].length[128];
            }
            if (UsesBottomLanePitMerge(definition.id) || UsesAuthoredApronHandoffs)
            {
                pitEntryTrackLane = PitAttachmentLane(entrance.Sample(0f), entryDistance);
                pitExitTrackLane = PitAttachmentLane(exit.Sample(1f), exitDistance);
            }
            pitTraffic = new TrackPitTraffic(lapLength, entryDistance, exitDistance,
                pitLengths[0], pitLengths[1], pitLengths[2], pit,
                singleThroughLane: definition.id == GatewayTrackId,
                recoverStoppedQueueOverlap: definition.majorStyle == "Oval" &&
                    definition.id is not (BristolTrackId or RichmondTrackId or NorthWilkesboroTrackId));
            if (sharedConnectorSurface)
            {
                DrawPitRibbon("Pit entrance", entrance, pit.widthMeters, pavement, entryHeight, roadStart,
                    underMainTrack: true, useDrivingPath: true);
                DrawPitRibbon("Pit exit", exit, pit.widthMeters, pavement, roadEnd, exitHeight,
                    underMainTrack: true, useDrivingPath: true, widenForFormation: true);
            }
            const int pitProgressSamples = 128;
            var trackDistances = new float[pitProgressSamples + 1];
            for (int i = 1; i < pitProgressSamples; i++)
            {
                float distance = pitTraffic.RouteLength * i / pitProgressSamples;
                int segment = 0;
                while (segment < pitRoute.Length - 1 && distance >= pitLengths[segment])
                    distance -= pitLengths[segment++];

                float progress = distance / pitLengths[segment];
                var position = sharedConnectorSurface && segment != 1
                    ? customLayout.ToSourceSpace(PitDrivingPosition(pitRoute[segment], progress, 0f,
                        pitHeights[segment], pitHeights[segment + 1]))
                    : pitRoute[segment].Sample(progress);
                trackDistances[i] = PitAttachment(position);
            }
            pitTraffic.SetProgressAlongRoute(trackDistances);
            if (definition.id == BristolTrackId)
                ConfigureBristolPitRoutes(pit, spline, pavement, sourceScale);
            trafficCars = new TrackPitTraffic.Car[cars.Length];
            for (int i = 0; i < cars.Length; i++)
                trafficCars[i] = pitTraffic.CreateTrackCar(lapLength * i / cars.Length);
        }
        if (!UsesAuthoredApronHandoffs || !importedTrackGeometry)
        {
            DrawPitLine("Pit speed-limit start", spline, pit.speedLimitStart, pit.widthMeters, markings, roadStart, roadEnd);
            DrawPitLine("Pit speed-limit end", spline, pit.speedLimitEnd, pit.widthMeters, markings, roadStart, roadEnd);
        }
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
            if (!importedTrackGeometry)
                Box("Pit stall", transform, position + Vector3.up * 0.055f, new Vector3(0.6f, 0.025f, 0.6f), markings);
        }

        pitWallSide = Mathf.Sign(Vector3.Dot(pitCenter - trackCenter,
            Vector3.Cross(Vector3.up, PitTangent(spline, 0.5f, roadStart, roadEnd))));
        if (practiceStallDistances != null)
        {
            float stallSide = 0f;
            for (int i = 0; i < Mathf.Min(markedStalls.Length, cars.Length); i++)
            {
                float fraction = Mathf.Clamp01((practiceStallDistances[i] - pitLengths[0]) / pitLengths[1]);
                var center = PitPosition(spline, fraction, roadStart, roadEnd);
                var across = Vector3.Cross(Vector3.up, PitTangent(spline, fraction, roadStart, roadEnd)).normalized;
                stallSide += Vector3.Dot(practiceStallPositions[i] - center, across);
            }
            if (Mathf.Abs(stallSide) > 0.1f)
                pitWallSide = Mathf.Sign(stallSide);
            FillPracticeStalls(spline, pit, markedStalls.Length);
        }

        if (pitTraffic != null && (definition.id is CharlotteTrackId or TexasTrackId))
            AddInfieldGarageSite(spline, pit.widthMeters, roadStart, roadEnd);
        else if (definition.id != BristolTrackId && !importedTrackGeometry &&
            (pitTraffic == null || !TryAddLargeTrackGarage(spline, pit.widthMeters, roadStart, roadEnd)))
            DrawPitWall(spline, pit.widthMeters, roadStart, roadEnd);
    }

    private float BristolMergeHeight(Vector3 source, float distance)
    {
        var center = TrackPoint(distance, 0f, out var forward, out _);
        var across = Vector3.Cross(Vector3.up, forward).normalized;
        float halfWidth = RoadWidths(distance).x * 0.5f;
        var edge = TrackPoint(distance, halfWidth * customLayout.InsideSign, out _, out _);
        float edgeOffset = Vector3.Dot(edge - center, across);
        float offset = Vector3.Dot(customLayout.ToTrackSpace(source) - center, across);
        float blend = Mathf.Clamp01(offset / edgeOffset);
        return Mathf.Lerp(center.y, edge.y, blend) - 0.025f;
    }

    private void ConfigureBristolPitRoutes(TrackPitRoad pit, TrackPitSpline road, Material pavement, float scale)
    {
        // The saved route runs from Turn 2 around the apron to Turn 1.
        // Mirror its turn connectors to connect the opposite ends of the two straights.
        var front = Mirror(pit.entrance);
        var back = Mirror(pit.exit);
        float join = road.ClosestFraction(TrackPitSpline.Position(front[front.Length - 1]));
        float leave = road.ClosestFraction(TrackPitSpline.Position(back[0]));
        Vector3 frontEnd = road.Sample(join);
        Vector3 backStart = road.Sample(leave);
        front[front.Length - 1] = new TrackPitPoint { x = frontEnd.x, z = frontEnd.z };
        back[0] = new TrackPitPoint { x = backStart.x, z = backStart.z };
        bristolFrontEntry = new TrackPitSpline(front);
        bristolBackExit = new TrackPitSpline(back);
        bristolFrontJoin = pitLengths[0] + join * pitLengths[1];
        bristolBackLeave = pitLengths[0] + leave * pitLengths[1];
        bristolBackExitLength = bristolBackExit.Length * scale;
        float frontEntry = PitAttachment(bristolFrontEntry.Sample(0f));
        float backExit = PitAttachment(bristolBackExit.Sample(1f));
        bristolFrontEntryLane = PitAttachmentLane(bristolFrontEntry.Sample(0f), frontEntry);
        bristolBackExitLane = PitAttachmentLane(bristolBackExit.Sample(1f), backExit);
        bristolBackExitTrackDistance = backExit;
        bristolFrontEntryHeight = BristolMergeHeight(bristolFrontEntry.Sample(0f), frontEntry);
        bristolBackExitHeight = BristolMergeHeight(bristolBackExit.Sample(1f), backExit);
        pitTraffic.ConfigureSplitRoad(frontEntry, bristolFrontJoin, bristolBackLeave, backExit,
            bristolBackExitLength, bristolFrontEntry.Length * scale);
        DrawPitRibbon("Bristol Turn 4 entry", bristolFrontEntry, pit.widthMeters, pavement, bristolFrontEntryHeight, 0f, underMainTrack: true);
        DrawPitRibbon("Bristol Turn 3 exit", bristolBackExit, pit.widthMeters, pavement, 0f, bristolBackExitHeight, underMainTrack: true);

        TrackPitPoint[] Mirror(TrackPitPoint[] points) =>
            System.Array.ConvertAll(points, point => new TrackPitPoint { x = -point.x, z = -point.z });
    }

    private void DrawPitWall(TrackPitSpline spline, float width, float roadStart, float roadEnd,
        float startFraction = 0.035f, float endFraction = 0.965f)
    {
        const int segments = 96;
        const float wallHeight = 0.95f;
        var vertices = new Vector3[(segments + 1) * 4];
        var triangles = new int[segments * 24 + 12];
        int index = 0;
        for (int i = 0; i <= segments; i++)
        {
            float fraction = Mathf.Lerp(startFraction, endFraction, i / (float)segments);
            var forward = PitTangent(spline, fraction, roadStart, roadEnd);
            var across = Vector3.Cross(Vector3.up, forward).normalized;
            var edge = PitPosition(spline, fraction, roadStart, roadEnd) + across * (pitWallSide * width * 0.5f);
            var center = edge + across * (pitWallSide * PitWallThickness * 0.5f);
            var half = across * (PitWallThickness * 0.5f);
            int k = i * 4;
            vertices[k] = center - half - Vector3.up * 0.06f;
            vertices[k + 1] = center - half + Vector3.up * wallHeight;
            vertices[k + 2] = center + half + Vector3.up * wallHeight;
            vertices[k + 3] = center + half - Vector3.up * 0.06f;
            if (i == 0)
                continue;
            for (int j = 0; j < 4; j++)
            {
                int next = (j + 1) % 4;
                AddQuad(k - 4 + j, k + j, k + next, k - 4 + next);
            }
        }
        AddQuad(0, 1, 2, 3);
        int last = segments * 4;
        AddQuad(last, last + 3, last + 2, last + 1);
        AddRoadMesh("Concrete pit wall", MakeMaterial(new Color32(177, 173, 163, 255)), vertices, triangles);
        RegisterSharedInfieldWall(startFraction, endFraction);

        void AddQuad(int a, int b, int c, int d)
        {
            triangles[index++] = a;
            triangles[index++] = b;
            triangles[index++] = c;
            triangles[index++] = a;
            triangles[index++] = c;
            triangles[index++] = d;
        }
    }

    private void AddTeamPitBoxes(CarSimulationContext context)
    {
        var model = Resources.Load<GameObject>("Startup/TeamPitBox");
        if (model == null)
            return;

        teamPitBoxes = CreateObject("Team pit boxes", transform);
        var paint = new MaterialPropertyBlock();
        int count = Mathf.Min(context.Cars.Count, practiceStallPositions.Length);
        var workStations = definition.id == IowaTrackId && count > 0 ? IowaPitWorkStations(count) : null;
        bool detailedWork = workStations != null || (definition.id is PhoenixTrackId or RichmondTrackId or NorthWilkesboroTrackId) && count > 0;
        var workPaint = count > 0 ? IowaPitWorkMaterial(count) : null;
        var workConcrete = workStations != null ? IowaPitWorkConcrete() : null;
        for (int i = 0; i < count; i++)
        {
            var entry = context.Cars[i];
            float assignedFraction = Mathf.Clamp01((practiceStallDistances[i] - pitLengths[0]) / pitLengths[1]);
            float fraction = workStations != null ? workStations[i] : assignedFraction;
            var center = PitPosition(pitRoute[1], fraction, pitHeights[1], pitHeights[2]);
            var forward = PitTangent(pitRoute[1], fraction, pitHeights[1], pitHeights[2]);
            var outward = Vector3.Cross(Vector3.up, forward).normalized * pitWallSide;
            var box = Instantiate(model, teamPitBoxes.transform, false);
            box.name = $"Pit box No. {entry.DisplayCarNumber}";
            box.transform.localPosition = center;
            var facing = Quaternion.LookRotation(-outward, Vector3.up);
            // Preserve the imported FBX orientation for the upright pit stands.
            box.transform.localRotation = facing * model.transform.localRotation;
            var renderers = box.GetComponentsInChildren<Renderer>();
            var worldOutward = transform.TransformDirection(outward);
            var stallCenter = transform.TransformPoint(center);
            float inwardReach = 0f;
            foreach (var renderer in renderers)
            {
                // Include the canopy and ladder, even when the imported pivot is off-center.
                float projectedCenter = Vector3.Dot(renderer.transform.TransformPoint(renderer.localBounds.center) - stallCenter, worldOutward);
                inwardReach = Mathf.Max(inwardReach, ProjectPitRendererExtent(renderer, worldOutward) - projectedCenter);
            }

            const float wallClearance = 0.30f;
            float wallBack = definition.pitRoad.widthMeters * 0.5f + PitWallThickness;
            box.transform.localPosition += outward * (wallBack + inwardReach + wallClearance);
            var livery = renderedCarPaint[i];
            var stallCenterline = PitPosition(pitRoute[1], assignedFraction, pitHeights[1], pitHeights[2]);
            var stallHeading = PitTangent(pitRoute[1], assignedFraction, pitHeights[1], pitHeights[2]);
            var infield = Vector3.Cross(Vector3.up, stallHeading).normalized * pitWallSide;
            if ((i == 0 || detailedWork) && renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                foreach (var renderer in renderers)
                    bounds.Encapsulate(renderer.bounds);
                if (i == 0)
                {
                    Vector3 roof = bounds.center;
                    roof.y = bounds.max.y + 0.65f;
                    playerPitBoxCameraPosition = transform.InverseTransformPoint(roof);
                }
                if (detailedWork)
                    AddIowaPitStallLight(teamPitBoxes.transform, entry.carNumber, stallCenterline,
                        stallHeading, infield, transform.InverseTransformPoint(bounds.center));
            }
            if (renderers.Length > 0)
                AddIowaPitWorkArea(box.transform, renderers, facing, entry.carNumber, i, count,
                    workPaint, workConcrete, compact: !detailedWork);
            AddIowaPitStallSign(teamPitBoxes.transform, entry.DisplayCarNumber, practiceStallPositions[i], stallCenterline,
                stallHeading, infield, livery.RoofAndSides, livery.HoodAndTrunk, i == 0 ? null : entry.car?.PaintStyle);
            foreach (var child in box.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = PreviewLayer;

            foreach (var renderer in renderers)
            {
                var shared = renderer.sharedMaterials;
                for (int j = 0; j < shared.Length; j++)
                {
                    string name = shared[j]?.name;
                    if (name != "Pit Box Primary" && name != "Pit Box Accent")
                        continue;
                    Color color = name == "Pit Box Primary" ? livery.RoofAndSides : livery.HoodAndTrunk;
                    paint.SetColor("_BaseColor", color);
                    paint.SetColor("_Color", color);
                    renderer.SetPropertyBlock(paint, j);
                }
            }
        }
        if (SupportsIowaPitStallLighting)
            ApplyIowaNightLighting(TrackSkyIsNight);
    }

    private void ConfigurePitField()
    {
        var markedStalls = definition.pitRoad.stalls ?? System.Array.Empty<TrackPitPoint>();
        stallLanes ??= new float[Mathf.Max(cars.Length, markedStalls.Length)];
        for (int i = 0; i < stallLanes.Length; i++)
        {
            Vector3 source = i < cars.Length ? Vector3.zero : TrackPitSpline.Position(markedStalls[i]);
            float fraction = i < cars.Length
                ? Mathf.Clamp01((practiceStallDistances[i] - pitLengths[0]) / pitLengths[1])
                : pitRoute[1].ClosestFraction(source);
            Vector3 position = i < cars.Length ? practiceStallPositions[i] : customLayout.ToTrackSpace(source);
            var center = PitPosition(pitRoute[1], fraction, pitHeights[1], pitHeights[2]);
            var across = Vector3.Cross(Vector3.up, PitTangent(pitRoute[1], fraction, pitHeights[1], pitHeights[2])).normalized;
            stallLanes[i] = Vector3.Dot(position - center, across);
        }
        pitTraffic.ConfigureField(isQualifying ? new[] { qualifying.Car } : trafficCars,
            practiceStallDistances, stallLanes, customLayout.InsideSign);
        pitTraffic.EntryReady = !isQualifying || definition.id == PhoenixTrackId || UsesEarlyLowLanePitApproach ? PitEntryReady : null;
        if (definition.id is BristolTrackId or MartinsvilleTrackId or IowaTrackId or PhoenixTrackId or RichmondTrackId or NorthWilkesboroTrackId ||
            definition.id == MichiganTrackId && importedTrackGeometry)
        {
            pitTraffic.ExitGapClear = isQualifying || ApronExitAvailable(pitTraffic.ExitDistance) ? null : PitExitGapClear;
            pitTraffic.ExitCheckAvailable = MergeCheckAvailable;
            pitTraffic.RoutePosition = SampleTrafficPosition;
            pitTraffic.CurrentPosition = car => cars[car.FieldIndex].localPosition;
            pitTraffic.CurrentHeading = car =>
            {
                Vector3 forward = cars[car.FieldIndex].localRotation * Vector3.forward;
                return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            };
            pitTraffic.CurrentSpeed = car => SessionRun?.CurrentSpeed(car.FieldIndex) ?? 0f;
            pitTraffic.CurrentLane = car => car.InPit ? car.PitLane : drivingLanes[car.FieldIndex];
            pitTraffic.PredictedPosition = PredictPitTrafficPosition;
            // Build all four connector caches during setup, never when the first
            // car reaches an exit in a live session.
            if (definition.id == BristolTrackId && ApronExitAvailable(pitTraffic.ExitDistance))
            {
                ApronConnectorPoint(pitRoute[0], 0f, 0f, pitHeights[0], pitHeights[1]);
                ApronConnectorPoint(pitRoute[2], 0f, 0f, pitHeights[2], pitHeights[3]);
                ApronConnectorPoint(bristolFrontEntry, 0f, 0f, bristolFrontEntryHeight, 0f);
                ApronConnectorPoint(bristolBackExit, 0f, 0f, 0f, bristolBackExitHeight);
            }
        }
    }

    private Vector3 PredictPitTrafficPosition(TrackPitTraffic.Car car, float seconds)
    {
        float travel = pitTraffic.PredictedSpeed(car) * seconds;
        float lane = pitTraffic.LaneAtStepStart(car);
        if (!car.InPit)
        {
            float station = car.TrackDistance + travel;
            if (definition.id == PhoenixTrackId && car.ApronExitActive && !car.ApronMergeGranted)
                lane = PitApronLane(station, car.LastExit);
            return TrackPoint(station, lane, out _, out _);
        }
        if (car.BackExit) return BackExitPoint(car.ConnectorDistance + travel, car.ConnectorEntryLane);
        float distance = car.PitDistance;
        if (car.FrontEntry && distance < bristolFrontJoin)
        {
            float scale = bristolFrontEntry.Length * Vector3.Distance(customLayout.ToTrackSpace(Vector3.zero), customLayout.ToTrackSpace(Vector3.right)) / bristolFrontJoin;
            float remaining = (bristolFrontJoin - distance) * scale;
            if (travel < remaining) return SampleTrafficPosition(car, distance + travel / scale, lane);
            travel -= remaining;
            distance = bristolFrontJoin;
        }
        if (pitTraffic.TakesBackExit(car) && distance + travel >= bristolBackLeave)
            return BackExitPoint(distance + travel - bristolBackLeave, lane);
        distance += travel;
        if (distance > pitTraffic.RouteLength)
        {
            float station = pitTraffic.ExitDistance + distance - pitTraffic.RouteLength;
            float exitLane = ApronExitAvailable(station) ? PitApronLane(station, pitTraffic.ExitDistance) : pitExitTrackLane;
            return TrackPoint(station, exitLane, out _, out _);
        }
        if (car.Maneuver) lane = TrackPitTraffic.ManeuverLane(distance, car.ManeuverStart, car.ManeuverLength,
            car.ManeuverFrom, car.ManeuverTo, car.ArrivalManeuver);
        return SampleTrafficPosition(car, distance, lane);

        Vector3 BackExitPoint(float distanceOnExit, float entryLane)
        {
            if (distanceOnExit >= bristolBackExitLength)
            {
                float station = bristolBackExitTrackDistance + distanceOnExit - bristolBackExitLength;
                return TrackPoint(station, ApronExitAvailable(station) ? ApronLane(station) : bristolBackExitLane, out _, out _);
            }
            float fraction = distanceOnExit / bristolBackExitLength;
            float targetLane = entryLane * (1f - Mathf.SmoothStep(0f, 1f, fraction));
            float predictedLane = Mathf.MoveTowards(lane, targetLane, 3f * seconds);
            return PitDrivingPosition(bristolBackExit, fraction, predictedLane, 0f, bristolBackExitHeight);
        }
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
            practiceStallPositions[i] = center + Vector3.Cross(Vector3.up, tangent) * (pitWallSide * pit.widthMeters * 0.35f);
        }
    }

    private float PitAttachment(Vector3 source) => Mathf.Repeat(
        TrackUtilityGeometry.ClosestMainDistance(customLayout, lapLength, customLayout.ToTrackSpace(source)), lapLength);

    private float PitAttachmentLane(Vector3 source, float distance)
    {
        Vector3 center = TrackPoint(distance, 0f, out _, out _);
        Vector3 across = TrackPoint(distance, 1f, out _, out _) - center;
        Vector3 offset = customLayout.ToTrackSpace(source) - center;
        across.y = 0f;
        float target = Vector3.Dot(offset, across);
        if (!UsesAuthoredLowLanePitRoute)
            return target / across.sqrMagnitude;

        // Solve on the sampled cross section, including progressive banking,
        // so the track lane agrees with the authored connector endpoint.
        float low = -RoadWidths(distance).x * .5f;
        float high = -low;
        for (int i = 0; i < 16; i++)
        {
            float lane = (low + high) * .5f;
            float lateral = Vector3.Dot(TrackPoint(distance, lane, out _, out _) - center, across);
            if (lateral < target) low = lane;
            else high = lane;
        }
        return (low + high) * .5f;
    }

    private const float PitSurfaceLift = 0.02f;

    private static float PitHeight(float startHeight, float endHeight, float fraction) =>
        Mathf.SmoothStep(startHeight, endHeight, fraction) + PitSurfaceLift;

    private void DrawPitRibbon(string objectName, TrackPitSpline spline, float width, Material material, float startHeight, float endHeight,
        bool underMainTrack = false, bool useDrivingPath = false, bool widenForFormation = false, Vector3? startTangent = null)
    {
        const int segments = PitRibbonSegments;
        int strips = underMainTrack ? PitConnectorStrips : 1;
        bool bristolConnector = underMainTrack && !useDrivingPath && !UsesAuthoredApronHandoffs && UsesDetailedPavement(definition.id);
        int stride = strips + 1;
        var vertices = new Vector3[(segments + 1) * stride];
        var clearance = new float[vertices.Length];
        var triangles = new List<int>(segments * strips * 6);
        for (int i = 0; i <= segments; i++)
        {
            float fraction = i / (float)segments;
            if (useDrivingPath)
            {
                for (int j = 0; j <= strips; j++)
                {
                    float lane = width * (j / (float)strips - .5f);
                    vertices[i * stride + j] = widenForFormation
                        ? ApronConnectorPoint(spline, fraction, lane, startHeight, endHeight, wideningPavement: true)
                        : PitDrivingPosition(spline, fraction, lane, startHeight, endHeight);
                }
                continue;
            }
            Vector3 center = PitPosition(spline, fraction, startHeight, endHeight);
            Vector3 tangent = PitTangent(spline, fraction, startHeight, endHeight);
            if (startTangent.HasValue && fraction < .125f)
                tangent = Vector3.Slerp(startTangent.Value, tangent, Mathf.SmoothStep(0f, 1f, fraction / .125f));
            Vector3 across = Vector3.Cross(Vector3.up, tangent).normalized;
            for (int j = 0; j <= strips; j++)
            {
                int index = i * stride + j;
                vertices[index] = center + across * (width * (j / (float)strips - 0.5f));
                if (bristolConnector)
                    vertices[index].y = BristolConnectorHeight(vertices[index], out clearance[index]);
                else if (underMainTrack)
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
        var outline = new Vector2[(segments + 1) * 2];
        for (int i = 0; i <= segments; i++)
        {
            outline[i] = MapPoint(vertices[i * stride]);
            outline[outline.Length - 1 - i] = MapPoint(vertices[i * stride + strips]);
        }
        mapPitRibbons.Add(outline);

        if (bristolConnector)
            bristolConnectorVertices[spline] = vertices;
        if (!importedTrackGeometry)
        {
            if (definition.id == CharlotteTrackId && useDrivingPath)
                vertices = ClipCharlottePitApron(vertices, triangles);
            AddRoadMesh(objectName, material, vertices, triangles.ToArray());
        }

        void AddPitTriangle(int a, int b, int c)
        {
            if (useDrivingPath)
            {
                // The ribbon fans to a shared point within the paved apron.
                // Omit collapsed triangles and keep each visible face upward.
                var normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                if (Mathf.Abs(normal.y) < .000001f) return;
                if (normal.y < 0f) (b, c) = (c, b);
            }
            else if (underMainTrack && (bristolConnector
                ? clearance[a] < 0f && clearance[b] < 0f && clearance[c] < 0f
                : clearance[a] < 0f || clearance[b] < 0f || clearance[c] < 0f))
                return;
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }
    }

    private float BristolConnectorHeight(Vector3 vertex, out float clearance)
    {
        float distance = TrackUtilityGeometry.ClosestMainDistance(customLayout, lapLength, vertex);
        var center = TrackPoint(distance, 0f, out var forward, out _);
        var across = Vector3.Cross(Vector3.up, forward).normalized;
        float halfWidth = RoadWidths(distance).x * 0.5f;
        float offset = Vector3.Dot(vertex - center, across);
        var edge = TrackPoint(distance, Mathf.Sign(offset) * halfWidth, out _, out _);
        float edgeOffset = Mathf.Abs(Vector3.Dot(edge - center, across));
        clearance = Mathf.Abs(offset) - edgeOffset;
        if (clearance <= 0f)
            return Mathf.Lerp(center.y, edge.y, Mathf.Abs(offset) / edgeOffset) - 0.005f;

        float apronWidth = offset * customLayout.InsideSign > 0f ? RoadWidths(distance).y : 0f;
        // Bristol's pit connectors need more room to blend down from the apron edge.
        float transitionWidth = definition.id == BristolTrackId ? 9f : 3f;
        float blend = Mathf.SmoothStep(0f, 1f, (clearance - apronWidth) / transitionWidth);
        return Mathf.Lerp(edge.y + 0.002f, vertex.y, blend);
    }

    private float MainTrackClearance(Vector3 vertex)
    {
        float distance = TrackUtilityGeometry.ClosestMainDistance(customLayout, lapLength, vertex);
        Vector3 center = TrackPoint(distance, 0f, out var forward, out _);
        Vector3 across = Vector3.Cross(Vector3.up, forward).normalized;
        if (definition.id == BristolTrackId)
        {
            float halfWidth = RoadWidths(distance).x * 0.5f;
            var edge = TrackPoint(distance, halfWidth, out _, out _);
            return Mathf.Abs(Vector3.Dot(vertex - center, across)) - Mathf.Abs(Vector3.Dot(edge - center, across));
        }
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

    private Texture2D bristolConcrete, bristolAsphalt;
    private RaceSession rubberSession;
    private Mesh rubberOverlayMesh;
    private MeshRenderer rubberOverlayRenderer;
    private Material rubberOverlayMaterial;
    private static readonly int RubberBuildupProperty = Shader.PropertyToID("_Buildup");
    private static readonly float[] RubberStripeWeights = { 0f, .72f, 1f, .72f, 0f };

    internal static float RubberBuildup(RaceSessionKind kind, float practiceFraction, int leaderLaps)
    {
        const float practiceRubber = .33f;
        return kind switch
        {
            RaceSessionKind.Practice => practiceRubber * Mathf.Clamp01(practiceFraction),
            RaceSessionKind.Qualifying => practiceRubber,
            RaceSessionKind.Race => Mathf.Lerp(practiceRubber, 1f, Mathf.Clamp01(leaderLaps / 50f)),
            _ => 0f
        };
    }

    private void UpdateRubberOverlay()
    {
        if (rubberOverlayRenderer == null) return;
        float amount = 0f;
        if (rubberSession != null && SessionRun != null)
        {
            float practiceFraction = rubberSession.minutes > 0
                ? SessionRun.Elapsed / (rubberSession.minutes * 60f)
                : SessionRun.LeaderLaps / (float)Mathf.Max(1, rubberSession.laps);
            amount = RubberBuildup(rubberSession.kind, practiceFraction, SessionRun.LeaderLaps);
        }

        rubberOverlayRenderer.enabled = amount > 0f;
        rubberOverlayMaterial.SetFloat(RubberBuildupProperty, amount);
    }

    private void BuildRubberOverlay()
    {
        if (racingLine == null || lapLength <= 0f) return;
        if (rubberOverlayMesh == null)
        {
            var shader = Resources.Load<Shader>("Startup/TrackRubber");
            if (shader == null) return;
            const string label = "Track rubber buildup";
            rubberOverlayMesh = new Mesh { name = label, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            roadMeshes.Add(rubberOverlayMesh);
            var overlay = CreateObject(label, transform);
            overlay.AddComponent<MeshFilter>().sharedMesh = rubberOverlayMesh;
            rubberOverlayRenderer = overlay.AddComponent<MeshRenderer>();
            rubberOverlayRenderer.shadowCastingMode = ShadowCastingMode.Off;
            rubberOverlayRenderer.receiveShadows = false;
            rubberOverlayMaterial = new Material(shader);
            materials.Add(rubberOverlayMaterial);
            rubberOverlayRenderer.sharedMaterial = rubberOverlayMaterial;
        }

        var vertices = new List<Vector3>();
        var colors = new List<Color32>();
        var triangles = new List<int>();
        int samples = Mathf.Clamp(Mathf.CeilToInt(lapLength), 128, 8192);
        for (int i = 0; i <= samples; i++)
        {
            float station = (i % samples) * lapLength / samples;
            TrackPoint(station - 12f, 0f, out var before, out _);
            TrackPoint(station + 12f, 0f, out var after, out _);
            float bend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.5f, 16f, Vector3.Angle(before, after)));
            float halfWidth = RoadWidths(station).x * .5f;
            float preferred = PreferredLaneOffset(0, halfWidth * 2f, float.PositiveInfinity, racingLine(station));
            float stripeHalfWidth = Mathf.Min(1.7f, halfWidth * .4f);
            // Fade to zero at both painted edges, including where the apron starts.
            const float edgeInset = .04f;
            float trackEdge = halfWidth - edgeInset;
            float center = Mathf.Clamp(preferred, -trackEdge, trackEdge);
            float left = Mathf.Max(-trackEdge, center - stripeHalfWidth);
            float right = Mathf.Min(trackEdge, center + stripeHalfWidth);
            int row = vertices.Count;
            for (int side = 0; side < RubberStripeWeights.Length; side++)
            {
                float offset = (side - 2) * .5f;
                float lane = offset < 0f ? Mathf.Lerp(center, left, -offset) : Mathf.Lerp(center, right, offset);
                Vector3 surface = TrackPoint(station, lane, out _, out var up);
                vertices.Add(surface + up * .055f);
                colors.Add(RubberVertex(.72f * bend * RubberStripeWeights[side]));
            }
            if (i > 0) JoinRubberRows(triangles, row, RubberStripeWeights.Length);
        }

        AddPitDepartureRubber(vertices, colors, triangles);
        rubberOverlayMesh.Clear();
        rubberOverlayMesh.SetVertices(vertices);
        rubberOverlayMesh.SetColors(colors);
        rubberOverlayMesh.SetTriangles(triangles, 0);
    }

    private void AddPitDepartureRubber(List<Vector3> vertices, List<Color32> colors, List<int> triangles)
    {
        if (pitTraffic == null || pitRoute == null || stallLanes == null || practiceStallDistances == null) return;
        var markedStalls = definition.pitRoad.stalls ?? System.Array.Empty<TrackPitPoint>();
        var routeCar = new TrackPitTraffic.Car { InPit = true };
        const int samples = 32;
        const int departuresPerStall = 3;
        float length = TrackPitTraffic.StandardStallDepartureLength;
        for (int stall = 0; stall < stallLanes.Length; stall++)
        {
            float start = stall < practiceStallDistances.Length
                ? practiceStallDistances[stall]
                : pitLengths[0] + pitRoute[1].ClosestFraction(TrackPitSpline.Position(markedStalls[stall])) * pitLengths[1];
            if (start + length > pitTraffic.RoadEnd) continue;
            float from = stallLanes[stall];
            float to = pitTraffic.PreferredStallDepartureLane(stall, start, length);
            // Keep the wear pattern stable when the track is rebuilt.
            var random = new System.Random(unchecked(definition.id * 397 ^ stall));
            for (int departure = 0; departure < departuresPerStall; departure++)
            {
                float offset = (departure - 1) * .22f + RandomRange(-.05f, .05f);
                float passStart = Mathf.Clamp(start + RandomRange(-.30f, .30f), 0f, pitTraffic.RoadEnd - length);
                float passLength = length - RandomRange(0f, .65f);
                float passFrom = from + offset;
                float passTo = to + offset * .55f;
                float peakStrength = RandomRange(.36f, .54f);

                float RandomRange(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());

                Vector3 previous = SampleTrafficPosition(routeCar, passStart, passFrom);
                Vector3 tangent = SampleTrafficPosition(routeCar, passStart + .05f, passFrom) - previous;
                float heading = Mathf.Atan2(tangent.x, tangent.z) * Mathf.Rad2Deg;
                for (int i = 0; i <= samples; i++)
                {
                    float fraction = i / (float)samples;
                    float distance = passStart + passLength * fraction;
                    float lane = TrackPitTraffic.ManeuverLane(distance, passStart, passLength, passFrom, passTo, arrival: false);
                    Vector3 position = SampleTrafficPosition(routeCar, distance, lane);
                    heading = RearAxleHeading.Advance(heading, position - previous, RearAxleHeading.CenterToRearAxle);
                    previous = position;
                    float radians = heading * Mathf.Deg2Rad;
                    Vector3 forward = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
                    Vector3 across = new Vector3(forward.z, 0f, -forward.x);
                    Vector3 axle = position - forward * RearAxleHeading.CenterToRearAxle + Vector3.up * .035f;
                    float strength = peakStrength * Mathf.SmoothStep(0f, 1f, fraction * 10f) *
                        Mathf.SmoothStep(0f, 1f, (1f - fraction) * 3f);
                    for (int tire = -1; tire <= 1; tire += 2)
                    {
                        int row = vertices.Count;
                        Vector3 center = axle + across * (tire * FormationHalfWheelTrack);
                        vertices.Add(center - across * .13f);
                        vertices.Add(center);
                        vertices.Add(center + across * .13f);
                        colors.Add(RubberVertex(0f));
                        colors.Add(RubberVertex(strength));
                        colors.Add(RubberVertex(0f));
                        if (i > 0) JoinRubberRows(triangles, row, 3, 6);
                    }
                }
            }
        }
    }

    private Mesh sessionSkidMesh;
    private readonly List<Vector3> skidVertices = new();
    private readonly List<Color32> skidColors = new();
    private readonly List<int> skidTriangles = new();
    private Vector3?[,] previousSkidContacts;
    private bool skidMeshDirty;

    private void ResetSessionSkids()
    {
        skidVertices.Clear();
        skidColors.Clear();
        skidTriangles.Clear();
        sessionSkidMesh?.Clear();
        previousSkidContacts = new Vector3?[cars.Length, 4];
        skidMeshDirty = false;
    }

    private void SampleSessionSkids()
    {
        if (previousSkidContacts == null)
            return;

        for (int i = 0; i < cars.Length; i++)
        {
            var traffic = isQualifying ? (i == 0 ? qualifying?.Car : null) : trafficCars?[i];
            bool onPavement = cars[i].gameObject.activeSelf &&
                traffic?.InPit != true && traffic?.InGarage != true &&
                (!isQualifying || i == 0) && (raceStart == null || raceReleased);
            bool spinning = onPavement && crashes[i].Active;
            bool lockup = onPavement && !SessionRun.Finished(i) && !SessionRun.Retired(i) &&
                SessionRun.EmergencyBrakeLockup(i);
            // Full strength at 20 degrees of handling yaw, proportional below that.
            float yawStrength = onPavement ? .61f * Mathf.Clamp01(Mathf.Abs(tireYawAngles[i]) / 20f) : 0f;
            float station = traffic?.TrackDistance ?? sessionDistances[i];
            for (int tire = 0; tire < 4; tire++)
            {
                bool front = tire < 2;
                float strength = spinning ? .64f : front ? (lockup ? .61f : 0f) : yawStrength;
                Vector3? contact = strength > 0f ? SkidTireContact(i, station, front, tire % 2 == 0 ? -1f : 1f) : null;
                Vector3? previous = previousSkidContacts[i, tire];
                previousSkidContacts[i, tire] = contact;
                if (contact.HasValue && previous.HasValue)
                    AddSkidSegment(previous.Value, contact.Value, strength);
            }
        }
    }

    private Vector3? SkidTireContact(int index, float station, bool front, float side)
    {
        var car = cars[index];
        float axle = RearAxleHeading.CenterToRearAxle * (front ? 1f : -1f);
        Vector3 localContact = raceWheels[index]?.TireContact(front, side) ??
            new Vector3(side * FormationHalfWheelTrack, 0f, axle);
        Vector3 tire = car.localPosition + car.localRotation * localContact;
        Vector3 datum = TrackPoint(station, 0f, out var tangent, out _);
        Vector3 horizontal = Vector3.ProjectOnPlane(tangent, Vector3.up).normalized;
        station += Vector3.Dot(tire - datum, horizontal);
        datum = TrackPoint(station, 0f, out _, out _);
        Vector3 cross = Vector3.ProjectOnPlane(TrackPoint(station, 1f, out _, out _) - datum, Vector3.up);
        float lane = Vector3.Dot(tire - datum, cross) / Mathf.Max(.01f, cross.sqrMagnitude);
        Vector2 widths = RoadWidths(station);
        float inside = customLayout?.InsideSign ?? -1f;
        if (lane * inside > widths.x * .5f + widths.y || lane * inside < -widths.x * .5f)
            return null;

        // Sample pavement height while keeping the endpoint directly below the tire.
        tire.y = TrackPoint(station, lane, out _, out _).y + .075f;
        return tire;
    }

    private void AddSkidSegment(Vector3 previous, Vector3 current, float strength)
    {
        Vector3 travel = current - previous;
        if (travel.sqrMagnitude < .000001f || travel.sqrMagnitude > 100f)
            return;

        Vector3 across = Vector3.Cross(Vector3.up, travel).normalized * .075f;
        int first = skidVertices.Count;
        skidVertices.Add(previous - across);
        skidVertices.Add(previous + across);
        skidVertices.Add(current - across);
        skidVertices.Add(current + across);
        var color = RubberVertex(strength);
        for (int i = 0; i < 4; i++)
            skidColors.Add(color);

        skidTriangles.Add(first);
        skidTriangles.Add(first + 2);
        skidTriangles.Add(first + 1);
        skidTriangles.Add(first + 1);
        skidTriangles.Add(first + 2);
        skidTriangles.Add(first + 3);
        skidMeshDirty = true;
    }

    private void PresentSessionSkids()
    {
        if (!skidMeshDirty)
            return;

        if (sessionSkidMesh == null)
        {
            var shader = Resources.Load<Shader>("Startup/TrackRubber");
            if (shader == null)
                return;

            const string label = "Session skid marks";
            sessionSkidMesh = new Mesh { name = label, indexFormat = IndexFormat.UInt32 };
            sessionSkidMesh.MarkDynamic();
            roadMeshes.Add(sessionSkidMesh);
            var overlay = CreateObject(label, transform);
            overlay.AddComponent<MeshFilter>().sharedMesh = sessionSkidMesh;
            var renderer = overlay.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var material = new Material(shader) { renderQueue = (int)RenderQueue.Transparent - 19 };
            material.SetFloat(RubberBuildupProperty, 1f);
            materials.Add(material);
            renderer.sharedMaterial = material;
        }

        sessionSkidMesh.SetVertices(skidVertices);
        sessionSkidMesh.SetColors(skidColors);
        sessionSkidMesh.SetTriangles(skidTriangles, 0);
        skidMeshDirty = false;
    }

    private static Color32 RubberVertex(float strength) =>
        new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(strength) * 255f));

    private static void JoinRubberRows(List<int> triangles, int row, int width, int stride = 5)
    {
        for (int i = 0; i < width - 1; i++)
        {
            int current = row + i;
            int previous = current - stride;
            triangles.Add(previous);
            triangles.Add(current);
            triangles.Add(previous + 1);
            triangles.Add(previous + 1);
            triangles.Add(current);
            triangles.Add(current + 1);
        }
    }

    private Texture2D BristolPavementTexture(bool concrete)
    {
        ref Texture2D cached = ref (concrete ? ref bristolConcrete : ref bristolAsphalt);
        if (cached != null)
            return cached;
        const int size = 128;
        var pixels = new Color32[size * size];
        var random = new System.Random(concrete ? 27 : 28);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            const float frequency = 0.067f;
            float span = size * frequency;
            float u = x / (float)size;
            float v = y / (float)size;
            float sampleX = x * frequency;
            float sampleY = y * frequency;
            float top = Mathf.Lerp(Mathf.PerlinNoise(sampleX, sampleY), Mathf.PerlinNoise(sampleX - span, sampleY), u);
            float bottom = Mathf.Lerp(Mathf.PerlinNoise(sampleX, sampleY - span), Mathf.PerlinNoise(sampleX - span, sampleY - span), u);
            float weathering = Mathf.Lerp(top, bottom, v);
            int value = (int)(222 + 22 * weathering + random.Next(-8, 9));
            byte shade = (byte)value;
            pixels[y * size + x] = new Color32(shade, shade, shade, 255);
        }
        cached = new Texture2D(size, size, TextureFormat.RGBA32, true);
        cached.SetPixels32(pixels);
        cached.Apply(true, true);
        return cached;
    }

    private static void SetPavementCoordinates(Mesh mesh)
    {
        var vertices = mesh.vertices;
        var uv = new Vector2[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
            uv[i] = new Vector2(vertices[i].x, vertices[i].z) / 16f;
        mesh.uv = uv;
    }

    private Material MakeCarPaintMaterial(Color color)
    {
        var material = MakeMaterial(color);
        material.SetFloat("_Metallic", 0.23f);
        material.SetFloat("_Smoothness", 0.62f);
        return material;
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
        Material glass, Material rubber, string[] sponsorNames = null,
        CarPaintStyle? paintStyle = null, System.Action<Texture2D> retainPaint = null, string modelResource = null,
        string carTypeModelResource = null, int? carLodLevel = null, System.Action<float> measureHalfWidth = null,
        string visualModelResource = null, string numberText = null, string referenceModel = null,
        Texture2D generatedPaintTexture = null)
    {
        string typeResource = carTypeModelResource ?? modelResource;
        var carType = CarTypeDefinition.ForModel(typeResource);
        string visualResource = visualModelResource ?? carType.ModelForLod(typeResource, carLodLevel ?? 5);
        var template = CarPaintTemplate.AcquireAssigned(paintStyle?.Template ?? default);
        CarPaintTemplateRenderAssets templateAssets = null;
        if (template != null)
        {
            templateAssets = CreateObject("Custom paint assets", car).AddComponent<CarPaintTemplateRenderAssets>();
            templateAssets.Own(template);
        }
        bool useFordAtlas = template?.UsesFordAtlas == true &&
            visualResource?.StartsWith("FordCupMustang", System.StringComparison.Ordinal) == true;
        if (useFordAtlas)
        {
            bodyPaint.color = Color.white;
            bodyPaint.SetTexture("_BaseMap", template.FordAtlas);
        }
        var model = CreateRaceCarModel(car, hoodAndTrunkPaint, bodyPaint, glass, rubber, visualResource, out float halfWidth,
            useFordAtlas ? template : null, templateAssets, referenceModel,
            carType.InteriorForVisualModel(typeResource, visualResource), carType.InteriorForVisualModel(typeResource, referenceModel));
        measureHalfWidth?.Invoke(halfWidth);
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
        bool wrapped = !useFordAtlas && paintStyle.HasValue &&
            CarPaintStyle.Apply(car, hoodAndTrunkPaint, bodyPaint, paintStyle.Value, retainPaint,
                template?.Texture, generatedPaintTexture);
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
            return;

        var decalMaterial = GetCarDecalMaterial(font);

        var modelNodes = model == null ? System.Array.Empty<Transform>() : model.GetComponentsInChildren<Transform>();
        if (paintStyle?.HideRenderedNumber != true)
        {
            // Point the bottom of the roof number toward the driver's side (local -X).
            var roofRotation = Quaternion.AngleAxis(carType.roofNumberPitch, Vector3.right) * Quaternion.Euler(90f, 90f, 0f);
            AddNumber(carType.roofNumber.Resolve(car, modelNodes), roofRotation, carType.roofNumberScale,
                carType.roofNumberMaxSize);
            AddSideNumber(carType.rightNumber, carType.rightNumberYaw);
            AddSideNumber(carType.leftNumber, carType.leftNumberYaw);
        }

        if (sponsorNames?.Length > 0 && !string.IsNullOrEmpty(sponsorNames[0]))
        {
            var anchor = System.Array.Find(modelNodes, node => node.name == "HoodSponsor");
            var position = anchor == null ? new Vector3(0f, 0.686f, 0.975f) : car.InverseTransformPoint(anchor.position);
            AddSponsor(sponsorNames[0], position, hoodAndTrunkPaint);
        }

        void AddSideNumber(CarModelPosition placement, float yawOffset)
        {
            var position = placement.Resolve(car, modelNodes, sideClearance: 0.008f);
            float yaw = position.x > 0f ? -90f : 90f;
            AddNumber(position, Quaternion.Euler(0f, yaw + yawOffset, 0f), carType.sideNumberScale,
                carType.sideNumberMaxSize);
        }

        void AddSponsor(string sponsor, Vector3 position, Material paint)
        {
            var decal = CreateObject("Sponsor decal", car);
            decal.transform.localPosition = position;
            decal.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var text = decal.AddComponent<TextMesh>();
            text.font = font;
            text.text = sponsor;
            text.anchor = TextAnchor.MiddleCenter;
            text.fontStyle = FontStyle.Bold;
            text.fontSize = 48;
            text.characterSize = Mathf.Min(0.035f, 0.15f / sponsor.Length);
            Color background = wrapped ? (Color)paintStyle.Value.ColorAt(position, paint == hoodAndTrunkPaint) : paint.color;
            text.color = 0.213f * background.r + 0.715f * background.g + 0.072f * background.b > 0.52f
                ? Color.black : Color.white;
            decal.GetComponent<MeshRenderer>().sharedMaterial = decalMaterial;
        }

        void AddNumber(Vector3 position, Quaternion rotation, float scale, Vector2 maxSize)
        {
            var label = CreateObject("Race number", car);
            label.transform.localPosition = position;
            label.transform.localRotation = rotation;
            var text = label.AddComponent<TextMesh>();
            text.font = font;
            CarNumberFonts.Apply(text, paintStyle);
            text.text = numberText ?? number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            text.anchor = TextAnchor.MiddleCenter;
            text.fontSize = 64;
            text.characterSize = 0.06f * scale;
            Color body = wrapped ? (Color)paintStyle.Value.ColorAt(position, false) : bodyPaint.color;
            text.color = paintStyle?.NumberColor is Color32 numberColor ? (Color)numberColor : CarNumberFonts.ContrastColor(body);
            var renderer = label.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = GetCarDecalMaterial(text.font);
            if (maxSize.x > 0f && maxSize.y > 0f)
            {
                Bounds ink = CarNumberInkBounds(text, renderer.localBounds);
                float fit = Mathf.Min(1f, Mathf.Min(maxSize.x / Mathf.Max(.001f, ink.size.x),
                    maxSize.y / Mathf.Max(.001f, ink.size.y)));
                label.transform.localScale = Vector3.one * fit;
                label.transform.localPosition = position - rotation * (ink.center * fit);
            }
        }
    }

    private static readonly Dictionary<(Font Font, int Size), float> carNumberAscents = new();

    private static Bounds CarNumberInkBounds(TextMesh text, Bounds lineBounds)
    {
        Font font = text.font;
        var key = (font, text.fontSize);
        if (!carNumberAscents.TryGetValue(key, out float ascent))
        {
            if (FontEngine.InitializeFontEngine() != FontEngineError.Success ||
                FontEngine.LoadFontFace(font, text.fontSize) != FontEngineError.Success)
                return lineBounds;
            ascent = FontEngine.GetFaceInfo().ascentLine;
            if (!(ascent > 0f))
                return lineBounds;
            carNumberAscents.Add(key, ascent);
        }

        font.RequestCharactersInTexture(text.text, text.fontSize, text.fontStyle);
        Vector2 min = Vector2.one * float.PositiveInfinity;
        Vector2 max = Vector2.one * float.NegativeInfinity;
        int advance = 0;
        foreach (char digit in text.text)
        {
            if (!font.GetCharacterInfo(digit, out var glyph, text.fontSize, text.fontStyle))
                return lineBounds;
            min = Vector2.Min(min, new Vector2(advance + glyph.minX, glyph.minY));
            max = Vector2.Max(max, new Vector2(advance + glyph.maxX, glyph.maxY));
            advance += glyph.advance;
        }

        if (advance <= 0 || lineBounds.size.x <= 0f)
            return lineBounds;

        // TextMesh bounds describe the full line. Place the glyphs relative to its baseline.
        float unitsPerPixel = lineBounds.size.x / advance;
        var baseline = new Vector2(lineBounds.min.x, lineBounds.max.y - ascent * unitsPerPixel);
        Vector2 center = baseline + (min + max) * (.5f * unitsPerPixel);
        Vector2 size = (max - min + Vector2.one) * unitsPerPixel; // Half a pixel at each edge for font hinting.
        return new Bounds(center, size);
    }

    private static readonly System.Collections.Generic.Dictionary<Font, Material> carDecalMaterials = new();

    private static Material GetCarDecalMaterial(Font font)
    {
        if (!carDecalMaterials.TryGetValue(font, out var material) || material == null)
        {
            var shader = Resources.Load<Shader>("Startup/CarDecal");
            if (shader == null) return font.material;
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            carDecalMaterials[font] = material;
            Font.textureRebuilt -= RefreshCarDecalAtlas;
            Font.textureRebuilt += RefreshCarDecalAtlas;
        }
        RefreshCarDecalAtlas(font);
        return material;
    }

    private static void RefreshCarDecalAtlas(Font font)
    {
        if (font != null && carDecalMaterials.TryGetValue(font, out var material) && material != null)
            material.mainTexture = font.material.mainTexture;
    }

    private static GameObject CreateRaceCarModel(Transform car, Material accent, Material body, Material glass,
        Material rubber, string modelResource, out float halfWidth, CarPaintTemplate fordTemplate = null,
        CarPaintTemplateRenderAssets templateAssets = null, string referenceModel = null,
        string interiorResource = null, string referenceInterior = null)
    {
        halfWidth = .9f; // Built-in fallback tire span.
        var asset = string.IsNullOrEmpty(modelResource) ? null : Resources.Load<GameObject>("Startup/" + modelResource);
        if (asset == null)
            asset = Resources.Load<GameObject>("Startup/Impala");
        if (asset == null)
            return null;

        var model = Instantiate(asset, car, false);
        if (!string.IsNullOrEmpty(interiorResource))
        {
            var interior = Resources.Load<GameObject>("Startup/" + interiorResource);
            if (interior != null)
                Instantiate(interior, model.transform, false);
        }
        var children = model.GetComponentsInChildren<Transform>();
        model.transform.localRotation = RaceCarAlignment(children, car.worldToLocalMatrix, model.transform.localRotation);

        foreach (var child in children)
            child.gameObject.layer = PreviewLayer;

        var renderers = model.GetComponentsInChildren<MeshRenderer>();
        if (renderers.Length == 0)
        {
            Destroy(model);
            return null;
        }

        Bounds bounds = referenceModel != null && referenceModel != modelResource &&
            TryRaceCarReferenceBounds(referenceModel, referenceInterior, out Bounds referenceBounds)
            ? referenceBounds : RaceCarBounds(renderers, car.worldToLocalMatrix);
        foreach (var renderer in renderers)
        {
            if (fordTemplate != null)
            {
                if (renderer.name == "Windshield")
                    MustangTemplatePreview.AddWindshieldBanner(renderer, car, body, fordTemplate.FordBanner, templateAssets.Retain);
                if (MustangTemplatePreview.Apply(renderer, car, body, templateAssets.Retain))
                    continue;
            }

            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = materials[i]?.name switch
                {
                    "HoodTrunkPaint" => accent,
                    "windows" => glass,
                    "window net" or "wheel" or "brake" => rubber,
                    "ModifiedMetal" => Resources.Load<Material>("Startup/ModifiedMetal") ?? rubber,
                    string materialName when materialName.StartsWith("RaceTruck_", System.StringComparison.Ordinal) => materials[i],
                    string materialName when materialName.StartsWith("LateModel_", System.StringComparison.Ordinal) => materials[i],
                    string materialName when materialName.StartsWith("FordMustang_", System.StringComparison.Ordinal) =>
                        CupCarPanelPaint.Apply(renderer, i, materials[i], body),
                    string materialName when materialName.StartsWith("ChevroletCamaro_", System.StringComparison.Ordinal) =>
                        CupCarPanelPaint.Apply(renderer, i, materials[i], body),
                    string materialName when materialName.StartsWith("ToyotaCamry_", System.StringComparison.Ordinal) =>
                        CupCarPanelPaint.Apply(renderer, i, materials[i], body),
                    _ => body
                };
            }
            renderer.sharedMaterials = materials;
        }

        float scale = 2.9f / Mathf.Max(0.001f, bounds.size.z);
        halfWidth = bounds.extents.x * scale;
        model.transform.localScale *= scale;
        model.transform.localPosition -= scale * new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        return model;
    }

    private static readonly Dictionary<GameObject, Bounds> RaceCarReferenceBounds = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRaceCarReferenceBounds() => RaceCarReferenceBounds.Clear();

    private static bool TryRaceCarReferenceBounds(string resource, string interiorResource, out Bounds bounds)
    {
        var asset = Resources.Load<GameObject>("Startup/" + resource);
        bounds = default;
        if (asset == null)
            return false;
        if (RaceCarReferenceBounds.TryGetValue(asset, out bounds))
            return true;

        var renderers = asset.GetComponentsInChildren<MeshRenderer>();
        if (renderers.Length == 0)
            return false;
        var root = asset.transform;
        var aligned = RaceCarAlignment(asset.GetComponentsInChildren<Transform>(), Matrix4x4.identity, root.localRotation);
        var rootToCar = Matrix4x4.TRS(root.localPosition, aligned, root.localScale);
        bounds = RaceCarBounds(renderers, rootToCar * root.worldToLocalMatrix);
        if (!string.IsNullOrEmpty(interiorResource))
        {
            var interior = Resources.Load<GameObject>("Startup/" + interiorResource);
            var parts = interior?.GetComponentsInChildren<MeshRenderer>();
            if (parts?.Length > 0)
                bounds.Encapsulate(RaceCarBounds(parts, rootToCar));
        }
        RaceCarReferenceBounds.Add(asset, bounds);
        return true;
    }

    private static Quaternion RaceCarAlignment(Transform[] nodes, Matrix4x4 toCar, Quaternion fallback)
    {
        var front = System.Array.Find(nodes, node => node.name == "CarFront");
        var rear = System.Array.Find(nodes, node => node.name == "CarRear");
        if (front == null || rear == null)
            return fallback;
        Vector3 forward = toCar.MultiplyVector(front.position - rear.position);
        forward.y = 0;
        return forward.sqrMagnitude > 0.001f ? Quaternion.Inverse(Quaternion.LookRotation(forward)) : fallback;
    }

    private static Bounds RaceCarBounds(MeshRenderer[] renderers, Matrix4x4 worldToCar)
    {
        Bounds bounds = default;
        bool first = true;
        foreach (var renderer in renderers)
        {
            Bounds mesh = renderer.localBounds;
            var toCar = worldToCar * renderer.transform.localToWorldMatrix;
            for (int corner = 0; corner < 8; corner++)
            {
                var point = new Vector3((corner & 1) == 0 ? mesh.min.x : mesh.max.x,
                    (corner & 2) == 0 ? mesh.min.y : mesh.max.y, (corner & 4) == 0 ? mesh.min.z : mesh.max.z);
                point = toCar.MultiplyPoint3x4(point);
                if (first)
                    bounds = new Bounds(point, Vector3.zero);
                else
                    bounds.Encapsulate(point);
                first = false;
            }
        }
        return bounds;
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

    private static void AssignBristolLighting(Transform root)
    {
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            renderer.renderingLayerMask = BristolLightingLayer;
    }

    private void OnEnable() => EnableBristolLighting();

    private void EnableBristolLighting()
    {
        if (bristolSun == null || RenderSettings.sun == bristolSun)
            return;

        previousSun = RenderSettings.sun;
        RenderSettings.sun = bristolSun;
        bristolPipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (bristolPipeline != null)
        {
            previousShadowDistance = bristolPipeline.shadowDistance;
            bristolPipeline.shadowDistance = Mathf.Max(previousShadowDistance, raceCamera.farClipPlane);
        }
    }

    private void OnDisable()
    {
        engineAudio?.Stop();
        DisableBristolLighting();
    }

    private void DisableBristolLighting()
    {
        if (bristolSun == null || RenderSettings.sun != bristolSun)
            return;

        RenderSettings.sun = previousSun;
        if (bristolPipeline != null)
            bristolPipeline.shadowDistance = previousShadowDistance;
    }

    private void OnDestroy()
    {
        if (raceCamera != null)
            raceCamera.targetTexture = null;
        if (rearViewCamera != null)
            rearViewCamera.targetTexture = null;
        if (RearViewImage != null)
        {
            RearViewImage.Release();
            Destroy(RearViewImage);
        }
        if (Image != null)
        {
            Image.Release();
            Destroy(Image);
        }

        if (bristolConcrete != null)
            Destroy(bristolConcrete);
        if (bristolAsphalt != null)
            Destroy(bristolAsphalt);
        foreach (var mesh in roadMeshes)
            Destroy(mesh);
        foreach (var material in materials)
            Destroy(material);
        foreach (var texture in carPaintTextures)
            Destroy(texture);
        if (iowaFenceTexture != null)
            Destroy(iowaFenceTexture);
    }
}


// Visual bicycle kinematics: the rear axle has no lateral velocity.
// Position remains the existing body center, not a new simulation reference point.
internal struct RearAxleHeading
{
    // The imported Impala's displayed wheelbase is 1.5517 m (the same scale
    // reference used for the pace truck). Approximate the center midway between axles.
    internal const float CenterToRearAxle = 1.5517f * 0.5f;
    private Vector3 previousPosition;
    private float headingDegrees;
    private bool initialized;

    internal void Apply(Transform car, bool spinning)
    {
        Vector3 forward = car.localRotation * Vector3.forward;
        float reference = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        Vector3 movement = car.localPosition - previousPosition;
        previousPosition = car.localPosition;
        movement.y = 0f;
        // Teleports, resets and crash recovery must not produce a steering swing.
        if (!initialized || spinning || movement.sqrMagnitude > 100f)
        {
            headingDegrees = reference;
            initialized = !spinning;
            return;
        }
        if (movement.sqrMagnitude > 0.00000001f)
            headingDegrees = Advance(headingDegrees, movement, CenterToRearAxle);
        Vector3 up = car.localRotation * Vector3.up;
        float radians = headingDegrees * Mathf.Deg2Rad;
        Vector3 heading = Vector3.ProjectOnPlane(new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)), up);
        if (heading.sqrMagnitude > 0.0001f)
            car.localRotation = Quaternion.LookRotation(heading, up);
    }

    internal static float Advance(float headingDegrees, Vector3 movement, float rearOffset)
    {
        // Exact solution for a straight center-displacement segment:
        // d(yaw)/ds = sin(travelHeading - yaw) / centerToRearAxle.
        // Unlike a frame-time smoothing factor, this remains stable at any speed.
        float distance = new Vector2(movement.x, movement.z).magnitude;
        if (distance < 0.00001f) return headingDegrees;
        float travel = Mathf.Atan2(movement.x, movement.z) * Mathf.Rad2Deg;
        float difference = Mathf.DeltaAngle(headingDegrees, travel) * Mathf.Deg2Rad;
        float remaining = 2f * Mathf.Atan(Mathf.Tan(difference * 0.5f) *
            Mathf.Exp(-distance / Mathf.Max(0.01f, rearOffset)));
        return travel - remaining * Mathf.Rad2Deg;
    }
}


internal static class PitMergeWindow
{
    internal static float TimeToDistance(float distance, float speed, float acceleration) =>
        (Mathf.Sqrt(speed * speed + 2f * acceleration * distance) - speed) / acceleration;

    internal static float ApproachTravel(float time, float speed, float limit, float acceleration)
    {
        speed = Mathf.Min(speed, limit);
        float accelerating = Mathf.Min(time, Mathf.Max(0f, (limit - speed) / acceleration));
        return speed * accelerating + .5f * acceleration * accelerating * accelerating +
            limit * (time - accelerating);
    }

    internal static float ApproachTime(float distance, float speed, float limit, float acceleration)
    {
        speed = Mathf.Min(speed, limit);
        float accelerating = Mathf.Max(0f, (limit - speed) / acceleration);
        float acceleratedDistance = ApproachTravel(accelerating, speed, limit, acceleration);
        return distance <= acceleratedDistance ? TimeToDistance(distance, speed, acceleration) :
            accelerating + (distance - acceleratedDistance) / limit;
    }

    internal static bool IsClear(float duration, int fieldCount,
        System.Func<float, float, Vector3> candidate, System.Func<int, float, Vector3?> obstacle,
        System.Func<float, float, bool> active = null, System.Action<int, float> blocked = null)
    {
        int samples = Mathf.CeilToInt(duration / .1f);
        for (int sample = 0; sample <= samples; sample++)
        {
            float time = sample * .1f;
            // Check actual curved paths, not a chord joining unrelated future positions.
            for (int pace = 0; pace < 3; pace++)
            {
                float acceleration = Mathf.Lerp(4f, 7.89f, pace * .5f);
                if (active != null && !active(time, acceleration)) continue;
                Vector3 point = candidate(time, acceleration);
                for (int index = 0; index < fieldCount; index++)
                    for (int offset = -1; offset <= 1; offset++)
                    {
                        var position = obstacle(index, Mathf.Max(0f, time + offset * .35f));
                        if (!position.HasValue) continue;
                        Vector3 separation = position.Value - point;
                        if (Mathf.Abs(separation.y) < 1.5f && separation.x * separation.x + separation.z * separation.z < 16f)
                        {
                            blocked?.Invoke(index, time);
                            return false;
                        }
                    }
            }
        }
        return true;
    }
}


internal static class ApronExitPlan
{
    internal static bool GapConflict(float gap, float relativeSpeed, float seconds)
    {
        float future = gap + relativeSpeed * seconds;
        return Mathf.Min(Mathf.Abs(gap), Mathf.Abs(future)) < 6f || gap * future <= 0f;
    }

    internal static float NextStraight(float start, float lapLength, System.Func<float, bool> straight, out float end)
    {
        for (float ahead = 5f; ahead < lapLength; ahead += 5f)
        {
            float d = start + ahead;
            if (!straight(d) || !straight(d + 20f) || !straight(d + 40f)) continue;
            float length = 45f;
            while (length < lapLength * .5f && straight(d + length)) length += 5f;
            end = d + length;
            return d;
        }
        end = start + lapLength;
        return end;
    }
}
