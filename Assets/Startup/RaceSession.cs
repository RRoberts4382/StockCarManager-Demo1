using System;
using System.Collections.Generic;
using System.Linq;

internal enum RaceSessionKind { Practice, Qualifying, Race }
internal enum RaceFinishBand { Good, Middle, Bad }

[Serializable]
internal sealed class PracticeWear
{
    public string chassis;
    public float amount;
}

[Serializable]
internal sealed class DriverContact
{
    public int carNumber;
    public string carNumberText;
    internal string DisplayCarNumber => carNumberText ?? carNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string driverName;
    public string teamName;
    public int driverAge;
    public string driverGender;
    public DriverEthnicity driverEthnicity;
    public float damage;
}

internal static class SessionWear
{
    public static float RaceAmount(CarComponent component, TrackDefinition track, Random random)
    {
        bool road = track.majorStyle == "Road";
        float shortOval = 1f - UnityEngine.Mathf.InverseLerp(0.25f, 1.5f, track.lengthMiles);
        float lengthFactor = UnityEngine.Mathf.InverseLerp(0.25f, 2.5f, track.lengthMiles);
        float nominal = component switch
        {
            CarComponent.Engine => 0.25f + 0.25f * lengthFactor,
            CarComponent.Brakes => road ? 0.47f : 0.50f * shortOval,
            CarComponent.Transmission => road ? 0.45f : 0.12f,
            CarComponent.Suspension => road ? 0.32f : 0.27f,
            _ => 0f
        };
        if (component == CarComponent.Aerodynamics)
            return 0f;
        if (component == CarComponent.Brakes && !road && track.lengthMiles >= 1.5f)
            return (float)random.NextDouble() * 0.02f;

        float minimum = component == CarComponent.Engine ? 0.25f : 0f;
        return UnityEngine.Mathf.Clamp(nominal + (float)(random.NextDouble() * 0.10 - 0.05), minimum, 0.55f);
    }
}

[Serializable]
internal sealed class RaceSession
{
    public static RaceFinishBand ClassifyFinish(double position, int fieldSize) =>
        position <= Math.Max(1, (fieldSize + 3) / 4) ? RaceFinishBand.Good
        : position > (fieldSize + 1) / 2 ? RaceFinishBand.Bad : RaceFinishBand.Middle;

    public RaceSessionKind kind;
    public string name;
    public int minutes;
    public int laps;
    public List<SessionCarResult> results = new List<SessionCarResult>();
    public bool completed;
    public bool rivalFinalSetupReviewed;
    public List<PracticeWear> practiceWear;
    public List<DriverContact> playerContacts;
    public PlayerSessionReport playerReport;
    public List<RivalPracticeTelemetry> rivalPracticeTelemetry = new List<RivalPracticeTelemetry>();
    public List<RivalSetupChange> rivalSetupChanges = new List<RivalSetupChange>();
    public List<GarageRunReview> garageRuns = new List<GarageRunReview>();
    public int[] qualifyingOrder;
    public SessionWeatherForecast weather;
    public bool lockPracticeWeather;

    public static int[] GridSlots(IReadOnlyList<SessionCarResult> entries, int[] qualifyingOrder)
    {
        var byNumber = entries.Select((entry, index) => (entry.carNumber, index))
            .ToDictionary(entry => entry.carNumber, entry => entry.index);
        var order = (qualifyingOrder ?? Array.Empty<int>()).Where(byNumber.ContainsKey)
            .Select(number => byNumber[number]).Concat(Enumerable.Range(0, entries.Count)).Distinct();
        var slots = new int[entries.Count];
        int slot = 0;
        foreach (int index in order)
            slots[index] = slot++;
        return slots;
    }
    [NonSerialized] public List<SessionCarResult> temporaryResults;
    public List<SessionCarResult> DisplayResults => temporaryResults ?? results;
    public bool Completed => completed || (results?.Count ?? 0) > 0;

    public const int PracticeMinutes = 10;

    public const int TestOpeningStageLaps = 15;
    public const int TestFinalStageLaps = 30;
    public const int TestBonusStages = 2;
    public const int TestRaceLaps = TestOpeningStageLaps * TestBonusStages + TestFinalStageLaps;

    public static List<RaceSession> Create(int practices, int qualifyingLaps)
    {
        var sessions = new List<RaceSession>();
        for (int i = 1; i <= practices; i++)
            sessions.Add(new RaceSession { kind = RaceSessionKind.Practice, name = $"Practice {i}", minutes = PracticeMinutes });
        sessions.Add(new RaceSession { kind = RaceSessionKind.Qualifying, name = "Qualifying", laps = qualifyingLaps });
        sessions.Add(new RaceSession { kind = RaceSessionKind.Race, name = "Race", laps = TestRaceLaps });
        return sessions;
    }
}

[Serializable]
internal sealed class SessionCarResult
{
    [NonSerialized] public TeamCar car;
    [NonSerialized] public Driver driver;
    [NonSerialized] public (UnityEngine.Color32 HoodAndTrunk, UnityEngine.Color32 RoofAndSides)? teamPaint;
    [NonSerialized] public float? enginePowerRating;
    [NonSerialized] public float? engineReliabilityRating;
    public double Fuel = SessionFuel.TankCapacity;
    [NonSerialized] public double InstantSpeed;
    public string carName = string.Empty;
    public int carNumber;
    public string carNumberText;
    internal string DisplayCarNumber => carNumberText ?? carNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string driverName;
    public string teamName;
    public string ownerEntryId;
    public string rivalDriverId;
    public bool playerEntry;
    public bool manufacturerRecorded;
    public CarManufacturer manufacturer;
    public List<float> lapTimes = new List<float>();
    public int position;
    public bool wrecked;
    public string mechanicalFailure;
    [NonSerialized] public List<DriverContact> contacts;
    [NonSerialized] public float damageSustained;
    [NonSerialized] public float damageAtFinish;
    [NonSerialized] public EventCarCondition trackCondition;
    public long winnings = 0;
    public int pointsEarned = 0;
    public int[] stagePoints;
    public int finishingPoints;
    public int fastestLapPoints;
    public float FastestLap => lapTimes.Count == 0 ? float.PositiveInfinity : lapTimes.Min();
}

internal sealed class RaceSessionRun
{
    private readonly RaceSession session;
    private readonly float lapLength;
    private readonly float startLine;
    private readonly CarTiming[] cars;
    private readonly bool recordContacts;
    private readonly double gallonsPerMeter;
    private readonly double tireWearPerMeter;
    private readonly TireTemperatureModel tireTemperatures;
    private readonly CarSimulationContext simulationContext;
    private readonly Random failureRandom;
    private readonly bool usesSpringPerformance;
    private readonly DynamicTireLoads[] dynamicLoads;
    private readonly float[] thermalSpeeds;
    private readonly float[] throttleInputs, brakeInputs;
    private readonly LapSpeedProfile inputSpeedProfile;
    internal const float CoastingDeceleration = 1.0f; // About 2.2 mph per second.
    internal float ThrottleInput(int index) => throttleInputs[index];
    internal float BrakeInput(int index) => brakeInputs[index];
    internal void TrafficBrakeInput(int index, float speed, float seconds, float brakingLimit, bool drafting = false)
    {
        float current = CurrentSpeed(index);
        float deceleration = (current - speed) / Math.Max(.0001f, seconds);
        throttleInputs[index] = 0f;
        if (deceleration > CoastingDeceleration + .01f)
        {
            brakeInputs[index] = UnityEngine.Mathf.Clamp01(deceleration / Math.Max(.01f, brakingLimit));
        }
        else if (drafting && speed > 0f && brakeInputs[index] == 0f)
        {
            // Steady drafting pace needs partial power. Use the resolved acceleration request
            // to modulate around half throttle, reaching zero at the coasting rate.
            float response = deceleration > 0f ? CoastingDeceleration : ReferenceAcceleration(current);
            throttleInputs[index] = UnityEngine.Mathf.Clamp01(.5f * (1f - deceleration / response));
        }
    }

    internal void ApplyDraftBumpSpeedChange(int index, float speedChange)
    {
        var car = cars[index];
        double speed = Math.Max(0d, car.Result.InstantSpeed + speedChange);
        car.Result.InstantSpeed = speed;
        car.Transmission.Update(speed * 3600d / TrackDefinition.MetersPerMile);
        thermalSpeeds[index] = (float)speed;
    }
    private readonly bool[] thermalRacing;
    private readonly float[] previousThermalSpeeds;
    private readonly float[] thermalDistances;
    private readonly bool[] thermalOnTrack;
    private int scoredStages;
    private readonly List<bool> raceLapCautions = new List<bool>();
    internal IReadOnlyList<bool> RaceLapCautions => raceLapCautions;
    private float leaderProgress;
    private readonly bool finishOnCheckered;
    private int? stageEndOverride;
    private int[] frozenOrder;
    private float checkeredTime = float.PositiveInfinity;
    private float stageCrossingTime = float.PositiveInfinity;
    private readonly int scheduledRaceLaps;
    internal float RaceProgressAggression => VehicleBehavior003.RaceAggression(IsRace,
        Math.Max(0f, (leaderProgress - startLine) / lapLength), scheduledRaceLaps,
        CurrentStage, StageStartLap, StageEnd, BonusStageCount);
    public int CurrentStage => scoredStages + 1;
    private readonly int[] stageEndLaps;
    public int BonusStageCount => Math.Max(0, stageEndLaps.Length - 1);
    public int StageStartLap { get; private set; }
    internal bool SetupLocked => simulationContext.Event?.SetupLocked == true;
    public bool IsRace => session.kind == RaceSessionKind.Race;
    public double PreciseElapsed { get; private set; }
    public float Elapsed => (float)PreciseElapsed;
    public SessionWeatherState Weather { get; }
    public int TimingRevision { get; private set; }
    public float Remaining => Math.Max(0f, session.minutes * 60f - Elapsed);
    public float PlayerBestLap => cars[0].Result.FastestLap;
    public int TargetLaps { get; private set; }
    public bool FinishSuspended { get; private set; }
    public int StageEnd => stageEndOverride ?? StageEndAt(scoredStages);

    internal int StageEndAt(int stageIndex) =>
        stageIndex < BonusStageCount ? stageEndLaps[stageIndex] : TargetLaps;

    public void EndStageAt(int lap) => stageEndOverride = lap;

    public void SuspendFinish() => FinishSuspended = true;

    public void StartOvertime()
    {
        // The first crossing after the restart starts the two timed laps.
        TargetLaps = LeaderLaps + 3;
        session.laps = TargetLaps;
        FinishSuspended = false;
    }

    public void FreezeFinish()
    {
        frozenOrder = new int[cars.Length];
        var order = RaceCarIndices();
        for (int position = 0; position < order.Length; position++)
            frozenOrder[order[position]] = position;
        checkeredTime = Elapsed;
    }
    public float Progress(int index) => cars[index].Progress;

    public int LeaderLaps => cars.Max(car => car.Result.lapTimes.Count);

    public int[] RaceCarIndices() => Enumerable.Range(0, cars.Length)
        .OrderBy(index => cars[index].Retired).ThenByDescending(Progress).ThenBy(index => cars[index].Result.carNumber).ToArray();

    internal int CompletedLaps(int index) => cars[index].Result.lapTimes.Count;
    public int PlayerLaps => cars[0].Result.lapTimes.Count;
    public bool Complete => session.minutes > 0 ? Remaining <= 0f : cars.All(car => car.Finished);

    private sealed class CarTiming
    {
        public SessionCarResult Result;
        public FourSpeedTransmission Transmission;
        public double NextCrossing;
        public double LastCrossing = -1d;
        public float FinishTime;
        public float CrossingThisStep = float.NegativeInfinity;
        public float CrossingDistance;
        public float SpeedFactor;
        public bool Finished;
        public bool Retired;
        public float SimulatedContactDamage;
        public float Progress;
        public float StepStartProgress;
        public float Speed;
        public readonly List<(float Distance, float Time)> History = new List<(float, float)>();
    }

    public RaceSessionRun(RaceSession session, float lapLength, float startLine,
        CarSimulationContext simulation, IVehicleSpeedModel speedModel, Random random,
        float[] initialDistances = null, float gridSpacing = 0f, bool finishOnCheckered = false)
    {
        this.session = session;
        recordContacts = simulation.Event != null;
        TargetLaps = session.laps;
        stageEndLaps = IsRace && simulation.Event?.noStages != true
            ? ScheduledRace.AllocateStageLaps(TargetLaps, simulation.Event?.stageLaps) : Array.Empty<int>();
        for (int i = 1; i < stageEndLaps.Length; i++)
            stageEndLaps[i] += stageEndLaps[i - 1];
        this.finishOnCheckered = finishOnCheckered;
        this.lapLength = lapLength;
        this.startLine = startLine;
        gallonsPerMeter = SessionFuel.GallonsPerMeter(simulation.Track);
        float roughness = UnityEngine.Mathf.Clamp01(simulation.Track.weights?.surfaceRoughness ?? 0.5f);
        tireWearPerMeter = (0.001 + 0.003 * roughness) / TrackDefinition.MetersPerMile;
        simulationContext = simulation;
        Weather = new SessionWeatherState(SessionWeatherForecast.Ensure(session, simulation.Track, simulation.Event), simulation.Track.surface, session.kind == RaceSessionKind.Practice);
        Weather.SetLocked(session.lockPracticeWeather);
        usesSpringPerformance = speedModel is VehicleSpeed003;
        if (usesSpringPerformance) simulation.PrepareRivalSetups();
        tireTemperatures = new TireTemperatureModel(simulation.Track);
        dynamicLoads = Enumerable.Range(0, simulation.Cars.Count).Select(_ => new DynamicTireLoads()).ToArray();
        thermalSpeeds = new float[simulation.Cars.Count];
        thermalRacing = new bool[simulation.Cars.Count];
        previousThermalSpeeds = new float[simulation.Cars.Count];
        thermalDistances = new float[simulation.Cars.Count];
        thermalOnTrack = new bool[simulation.Cars.Count];
        var entries = simulation.Cars;
        scheduledRaceLaps = session.laps; // Keep urgency at maximum through overtime.
        cars = new CarTiming[entries.Count];
        throttleInputs = new float[entries.Count]; brakeInputs = new float[entries.Count];
        inputSpeedProfile = new LapSpeedProfile(simulation.Track, usesSpringPerformance);
        double rearGearRatio = ReferenceRearGearRatio(simulation.Track, session, speedModel, inputSpeedProfile);
        for (int i = 0; i < cars.Length; i++)
        {
            var timing = new CarTiming
            {
                Transmission = new FourSpeedTransmission(rearGearRatio),
                NextCrossing = i == 0 ? startLine + lapLength : startLine,
                LastCrossing = i == 0 ? 0f : -1f,
                SpeedFactor = speedModel.LapSpeedFactor(simulation, i, random)
            };
            if (initialDistances != null)
            {
                float initial = initialDistances[i];
                float next = startLine + (float)Math.Ceiling((initial - startLine) / lapLength) * lapLength;
                bool startsOnLine = Math.Abs(next - initial) < 0.001f;
                timing.NextCrossing = startsOnLine ? next + lapLength : next;
                timing.LastCrossing = startsOnLine ? 0f : -1f;
            }
            timing.Progress = initialDistances == null ? startLine - gridSpacing * i : initialDistances[i];
            thermalDistances[i] = TrackPosition(timing.Progress + (simulation.IsInstant ? simulation.Track.StartFinishFraction * lapLength : 0f), lapLength);
            thermalOnTrack[i] = true;
            if (IsRace)
                timing.History.Add((timing.Progress, 0f));
            timing.StepStartProgress = timing.Progress;
            timing.Result = entries[i];
            timing.Result.mechanicalFailure = null;
            if (IsRace)
                timing.Result.stagePoints = new int[BonusStageCount];
            cars[i] = timing;
        }
        failureRandom = new Random(random.Next());
    }

    internal static double ReferenceRearGearRatio(TrackDefinition track, RaceSession session,
        IVehicleSpeedModel speedModel, LapSpeedProfile profile)
    {
        var reference = new CarSimulationContext(track, null, session, Array.Empty<SessionCarResult>());
        float baseSpeed = speedModel.BaseSpeed(reference, 0, new Random(0));
        float forceFactor = speedModel is VehicleSpeed003 ? VehicleSpeed003.WeightForceFactor(SessionFuel.TankCapacity) : 1f;
        float peakSpeed = speedModel.UsesLapSpeedProfile ? profile.EstimatePeakSpeed(baseSpeed, forceFactor) : baseSpeed;
        double peakMph = peakSpeed * 3600.0 / TrackDefinition.MetersPerMile;
        if (track.restrictorPlate)
            peakMph += TrackDraft.MaximumDraftBoostMph;
        return FourSpeedTransmission.RearGearRatioForTopSpeed(peakMph);
    }

    internal static void ResetVehiclesForSession(IReadOnlyList<SessionCarResult> entries, bool preservePlayerCondition)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            entries[i].Fuel = SessionFuel.TankCapacity;
            if (i != 0 || !preservePlayerCondition)
                entries[i].car?.ResetTrackCondition();
        }
    }

    public static List<SessionCarResult> Simulate(CarSimulationContext simulation, IVehicleSpeedModel speedModel,
        Random random, int[] qualifyingOrder, bool includesPlayer)
    {
        List<SessionCarResult> results = null;
        var steps = SimulateIncremental(simulation, speedModel, random, qualifyingOrder, includesPlayer,
            value => results = value);
        while (steps.MoveNext()) { }
        return results;
    }

    public static System.Collections.IEnumerator SimulateIncremental(CarSimulationContext simulation,
        IVehicleSpeedModel speedModel, Random random, int[] qualifyingOrder, bool includesPlayer,
        Action<List<SessionCarResult>> completed, Func<bool> cancelled = null, Action<float> progress = null)
    {
        var session = simulation.Session;
        var entries = simulation.Cars;
        if (speedModel is VehicleSpeed003) simulation.PrepareRivalSetups(includesPlayer);
        ResetVehiclesForSession(entries, simulation.PreservePlayerCondition);
        float lapLength = simulation.Track.lengthMiles * TrackDefinition.MetersPerMile;
        var distances = new float[entries.Count];
        if (session.kind == RaceSessionKind.Race)
        {
            var slots = RaceSession.GridSlots(entries, qualifyingOrder);
            float spacing = Math.Min(4.5f, lapLength / (2f * entries.Count));
            for (int i = 0; i < distances.Length; i++)
                distances[i] = -spacing * slots[i];
        }
        var preciseDistances = distances.Select(value => (double)value).ToArray();
        var baseSpeeds = new float[entries.Count];
        for (int i = 0; i < baseSpeeds.Length; i++)
            baseSpeeds[i] = speedModel.BaseSpeed(simulation, i, random);

        var run = new RaceSessionRun(session, lapLength, 0f, simulation, speedModel, random, initialDistances: distances);
        var profile = speedModel.UsesLapSpeedProfile ? new LapSpeedProfile(simulation.Track, speedModel is VehicleSpeed003) : null;
        float profileStart = simulation.Track.StartFinishFraction * lapLength;
        var batch = System.Diagnostics.Stopwatch.StartNew();
        while (!run.Complete)
        {
            if (cancelled?.Invoke() == true) yield break;
            float seconds = run.StepDuration(profile == null ? 1f : 0.25f);
            if (includesPlayer && run.IsRace && entries.Count > 1)
                run.TrySimulateContact(distances, seconds, random);
            for (int i = 0; i < entries.Count; i++)
            {
                if (run.Finished(i))
                    continue;

                double previous = preciseDistances[i];
                float baseSpeed = baseSpeeds[i] * run.SpeedFactor(i);
                float speed = profile?.SpeedAt(distances[i] + profileStart, baseSpeed) ?? baseSpeed;
                speed = run.SetupAdjustedSpeed(i, distances[i] + profileStart, speed);
                if (run.IsRace && run.Fuel(i) <= run.FuelForDistance(lapLength) + 0.5)
                {
                    double remaining = Math.Max(0, session.laps - run.LeaderLaps) * (double)lapLength;
                    run.Refuel(i, remaining);
                }
                speed = run.ConditionLimitedSpeed(i, speed, seconds,
                    profile?.BrakingLimitAt(distances[i] + profileStart) ?? 0f);
                preciseDistances[i] += (double)speed * seconds;
                distances[i] = (float)preciseDistances[i];
                run.RecordTravel(i, preciseDistances[i] - previous, speed, preciseDistances[i] + profileStart);
                run.Record(i, previous, preciseDistances[i], seconds);
            }
            run.AdvanceClock(seconds);
            if (batch.ElapsedMilliseconds >= 8)
            {
                float fraction = session.minutes > 0 ? run.Elapsed / (session.minutes * 60f) :
                    (float)(preciseDistances.Max() / Math.Max(1d, session.laps * (double)lapLength));
                progress?.Invoke(UnityEngine.Mathf.Clamp01(fraction));
                yield return null;
                batch.Restart();
            }
        }
        if (cancelled?.Invoke() != true)
        {
            progress?.Invoke(1f);
            completed(run.Results());
        }
    }

    private void TrySimulateContact(float[] distances, float seconds, Random random)
    {
        const double contactsPerSecond = 0.0012;
        if (Finished(0) || CurrentSpeed(0) < 10f || random.NextDouble() >= contactsPerSecond * seconds)
            return;

        int rival = -1;
        float closestGap = 55f;
        for (int i = 1; i < cars.Length; i++)
        {
            if (Finished(i) || CurrentSpeed(i) < 10f)
                continue;
            float gap = UnityEngine.Mathf.Repeat(distances[i] - distances[0] + lapLength * 0.5f, lapLength) - lapLength * 0.5f;
            if (Math.Abs(gap) >= closestGap)
                continue;
            closestGap = Math.Abs(gap);
            rival = i;
        }
        if (rival < 0)
            return;

        double roll = random.NextDouble();
        float damage = 0.5f + 44.5f * (float)(roll * roll * roll);
        RecordContact(0, rival, damage);
        ApplyDamage(0, damage, simulated: true);
        ApplyDamage(rival, damage, simulated: true);
        if (damage >= 20f)
        {
            RecordWreck(0);
            RecordWreck(rival);
        }
    }

    public void SimulateQualifyingRivals(Func<int, float> baseSpeed)
    {
        for (int i = 1; i < cars.Length; i++)
        {
            var car = cars[i];
            for (int lap = 0; lap < session.laps; lap++)
            {
                float factor = car.SpeedFactor;
                float speed = baseSpeed(i) * factor;
                if (usesSpringPerformance)
                {
                    const int samples = 128;
                    float lapSeconds = 0f;
                    float segment = lapLength / samples;
                    for (int sample = 0; sample < samples; sample++)
                    {
                        float distance = startLine + sample * segment;
                        float adjusted = SetupAdjustedSpeed(i, distance, speed);
                        float seconds = segment / Math.Max(1f, adjusted);
                        dynamicLoads[i].Advance(SetupForCar(i), Fuel(i), 0f,
                            adjusted * adjusted * tireTemperatures.CurvatureAt(distance) / 9.80665f, seconds);
                        tireTemperatures.Advance(car.Result.car, seconds, distance, adjusted, adjusted,
                            simulationContext.Track, SetupForCar(i), trackFahrenheit: Weather.TrackFahrenheit,
                            fuelGallons: Fuel(i), dynamicLoads: dynamicLoads[i]);
                        lapSeconds += seconds;
                    }
                    speed = lapLength / lapSeconds;
                }
                car.Result.lapTimes.Add(lapLength / speed);
                RecordTravel(i, lap == 0 ? 2.0 * lapLength : lapLength, speed, startLine);
                TimingRevision++;
            }
            car.Result.InstantSpeed = double.NaN;
            car.Finished = true;
        }
    }

    public void BeginPlayerFlyingLaps(float exitDistance)
    {
        var player = cars[0];
        player.NextCrossing = startLine + (float)Math.Ceiling((exitDistance - startLine) / lapLength) * lapLength;
        player.LastCrossing = -1f;
    }

    public float SpeedFactor(int index) => cars[index].SpeedFactor;
    public int FlatTireMask(int index)
    {
        var car = Vehicle(index);
        int mask = 0;
        for (int tire = 0; tire < 4; tire++)
        {
            if (car?.GetTire(tire)?.wear >= 1f)
                mask |= 1 << tire;
        }
        return mask;
    }

    internal static int TireCount(int mask)
    {
        int count = 0;
        for (int tire = 0; tire < 4; tire++)
            count += (mask >> tire) & 1;
        return count;
    }

    public float FlatTireSpeed(float speed, int mask) =>
        mask == 0 ? speed : Math.Min(speed, TireCount(mask) == 1 ? TrackState001Stages.PaceSpeed : 20f);

    public void RecordFlatTireDamage(int index, int mask, double meters)
    {
        if (mask != 0 && meters > 0)
            ApplyDamage(index, (float)(meters / TrackDefinition.MetersPerMile) * TireCount(mask));
    }

    public void ApplyDamage(int index, float amount, bool simulated = false)
    {
        var timing = cars[index];
        var vehicle = timing.Result.car;
        float current = (vehicle?.Damage ?? 0f) + timing.SimulatedContactDamage;
        float applied = Math.Max(0f, Math.Min(amount, 100f - current));
        if (simulated)
            timing.SimulatedContactDamage += applied;
        else if (vehicle != null)
            vehicle.Damage += applied;
        else
            return;
        timing.Result.damageSustained += applied;
    }

    public TeamCar Vehicle(int index) => cars[index].Result.car;

    public void RecordWreck(int index) => cars[index].Result.wrecked = true;

    public void RecordContact(int first, int second, float damage)
    {
        if (!recordContacts || first != 0)
            return;

        var player = cars[0].Result;
        var rival = cars[second].Result;
        player.contacts ??= new List<DriverContact>();
        var contact = player.contacts.Find(item => item.carNumber == rival.carNumber);
        if (contact == null)
            player.contacts.Add(new DriverContact
            {
                carNumber = rival.carNumber,
                carNumberText = rival.carNumberText,
                driverName = rival.driverName,
                teamName = rival.teamName,
                driverAge = rival.driver?.age ?? 0,
                driverGender = rival.driver?.gender,
                driverEthnicity = rival.driver?.ethnicity ?? DriverEthnicity.Unspecified,
                damage = damage
            });
        else
            contact.damage = Math.Max(contact.damage, damage);
    }

    public void AddFuel(int index, double gallons)
    {
        var result = cars[index].Result;
        result.Fuel = Math.Min(SessionFuel.TankCapacity, result.Fuel + Math.Max(0, gallons));
    }

    internal void SetFuel(int index, double gallons)
    {
        if (double.IsNaN(gallons) || double.IsInfinity(gallons))
            return;
        if (simulationContext.EqualConditionTesting) gallons = SessionFuel.TankCapacity;
        cars[index].Result.Fuel = Math.Max(0d, Math.Min(SessionFuel.TankCapacity, gallons));
    }

    public double Fuel(int index) => cars[index].Result.Fuel;

    internal float BrakingPerformanceFactor(int index) => !usesSpringPerformance ? 1f :
        UnityEngine.Mathf.Min(1f, VehicleSpeed003.WeightForceFactor(Fuel(index)) *
            VehicleSpeed003.DistributionBrakingFactor(SetupForCar(index), Fuel(index))) * Weather.GripFactor;

    public float CurrentSpeed(int index) => (float)cars[index].Result.InstantSpeed;
    internal int TransmissionGear(int index) => cars[index].Transmission.Gear;
    internal double EngineRpm(int index) => cars[index].Retired ? 0.0 : cars[index].Transmission.EngineRpm;

    public bool Retired(int index) => cars[index].Retired;

    // Available drive force balances speed-squared drag at the performance ceiling.
    // This is full throttle even when net acceleration is zero or slightly negative.
    internal static float ReferenceAcceleration(float speed)
    {
        const float metersPerSecondToMph = 3600f / TrackDefinition.MetersPerMile;
        float rollingSpeed = UnityEngine.Mathf.InverseLerp(60f, 180f, speed * metersPerSecondToMph);
        return UnityEngine.Mathf.Lerp(7.89f, 3.5f, rollingSpeed);
    }

    internal static float FullThrottleSpeed(float current, float terminal, float acceleration, float seconds)
    {
        if (seconds <= 0f) return current;
        if (terminal <= .001f) return Math.Max(0f,current-CoastingDeceleration*seconds);
        float ratio=current/terminal;
        float net=UnityEngine.Mathf.Clamp(acceleration*(1f-ratio*ratio),-CoastingDeceleration,acceleration);
        float next=current+net*seconds;
        return current<=terminal ? Math.Min(terminal,next) : Math.Max(terminal,next);
    }

    public string MechanicalFailure(int index) => cars[index].Result.mechanicalFailure;
    private const string FlatTireFailure = "Flat Tire";

    public bool HasTerminalFailure(int index) =>
        MechanicalFailure(index) is string failure && !string.IsNullOrEmpty(failure) && failure != FlatTireFailure;

    public bool HasRandomFlatTire(int index) =>
        MechanicalFailure(index) == FlatTireFailure && FlatTireMask(index) != 0;

    public float DisplayDamage(int index) => HasTerminalFailure(index) || HasRandomFlatTire(index)
        ? 100f : Vehicle(index)?.Damage ?? 0f;

    internal static double MechanicalFailureProbability(double seconds, float? reliability)
    {
        // Five failures over 36 weekends with three hours of active driving each.
        const double failuresPerSecond = 5.0 / (36 * 3 * 3600);
        double totalWeight = 3.0 + 2.0 * EngineFailureWeight(reliability);
        return 1.0 - Math.Exp(-failuresPerSecond * totalWeight / 5.0 * Math.Max(0.0, seconds));
    }

    private static double EngineFailureWeight(float? reliability)
    {
        const float referenceReliability = .95f;
        float rating = reliability is float value && !float.IsNaN(value)
            ? UnityEngine.Mathf.Clamp01(value) : referenceReliability;
        return 1.0 + 4.0 * (referenceReliability - rating);
    }

    private string ChooseMechanicalFailure(float? reliability)
    {
        double engineWeight = EngineFailureWeight(reliability);
        double draw = failureRandom.NextDouble() * (3.0 + 2.0 * engineWeight);
        if (draw < engineWeight) return "Blown Engine";
        if (draw < 2.0 * engineWeight) return "Overheating Engine";
        if (draw < 2.0 * engineWeight + 1.0) return FlatTireFailure;
        return draw < 2.0 * engineWeight + 2.0 ? "Transmission Failure" : "Rear Gear Failure";
    }

    public float ConditionLimitedSpeed(int index, float speed, float seconds, float brakingLimit = 0f,
        bool allowMechanicalFailure = false, float? paceSpeedCeiling = null, float preferredLineSpeedFactor = 1f) =>
        ConditionLimitedSpeedAndPace(index, speed, seconds, out _, brakingLimit, allowMechanicalFailure,
            paceSpeedCeiling, preferredLineSpeedFactor);

    internal float ConditionLimitedSpeedAndPace(int index, float speed, float seconds, out float pace, float brakingLimit = 0f,
        bool allowMechanicalFailure = false, float? paceSpeedCeiling = null, float preferredLineSpeedFactor = 1f)
    {
        pace = 0f;
        var car = cars[index];
        throttleInputs[index]=brakeInputs[index]=0f;
        float damage = (car.Result.car?.Damage ?? 0f) + car.SimulatedContactDamage;
        bool canRun = car.Result.Fuel > 0 && damage < 100f;
        if (allowMechanicalFailure && canRun && car.Result.car != null && !car.Finished &&
            !HasTerminalFailure(index) && FlatTireMask(index) == 0 && car.Result.InstantSpeed > 0.45 && seconds > 0f)
        {
            var engine = cars[simulationContext.EqualConditionTesting ? 0 : index].Result;
            if (failureRandom.NextDouble() < MechanicalFailureProbability(seconds, engine.engineReliabilityRating))
            {
                car.Result.mechanicalFailure = ChooseMechanicalFailure(engine.engineReliabilityRating);
                if (HasTerminalFailure(index))
                    ApplyDamage(index, 100f);
                else
                    car.Result.car.GetTire(failureRandom.Next(4)).wear = 1f;
            }
        }
        if (canRun && !HasTerminalFailure(index))
        {
            float currentSpeed = (float)car.Result.InstantSpeed;
            float acceleration = ReferenceAcceleration(currentSpeed);
            float brakingGrip = 1f;
            if (usesSpringPerformance)
            {
                float weightFactor = VehicleSpeed003.WeightForceFactor(car.Result.Fuel);
                var setup = SetupForCar(index);
                acceleration *= UnityEngine.Mathf.Min(1f, weightFactor * VehicleSpeed003.DistributionAccelerationFactor(setup, car.Result.Fuel));
                brakingGrip = UnityEngine.Mathf.Min(1f, weightFactor * VehicleSpeed003.DistributionBrakingFactor(setup, car.Result.Fuel)) * Weather.GripFactor;
                brakingLimit *= brakingGrip;
            }
            float damageFactor = 1f - damage / 100f;
            // A paced car should be able to hold the commanded speed if it is
            // below what the car can still achieve with its current damage.
            float target = paceSpeedCeiling.HasValue
                ? Math.Min(speed, paceSpeedCeiling.Value * damageFactor)
                : speed * damageFactor;
            pace = target;
            float nextSpeed;
            if (thermalOnTrack[index] && brakingLimit <= 0f && inputSpeedProfile.FullThrottleAt(thermalDistances[index]))
            {
                throttleInputs[index]=1f;
                nextSpeed = FullThrottleSpeed(currentSpeed, target, acceleration, seconds);
            }
            else if (target < currentSpeed)
            {
                float deceleration=brakingLimit>0f ? brakingLimit : CoastingDeceleration;
                if(brakingLimit>0f) brakeInputs[index]=UnityEngine.Mathf.Clamp01((currentSpeed-target)/Math.Max(.0001f,seconds*brakingLimit));
                nextSpeed = Math.Max(target, currentSpeed - deceleration * seconds);
            }
            else
            {
                // Racing-speed ceiling represents available performance, not a partial-throttle command.
                // Traffic avoidance may override this request after the speed profile is evaluated.
                throttleInputs[index]=inputSpeedProfile.FullThrottleAt(thermalDistances[index]) ? 1f :
                    UnityEngine.Mathf.Clamp01(.25f+(target-currentSpeed)/Math.Max(.0001f,acceleration*seconds));
                return Math.Min(target, currentSpeed + acceleration * seconds);
            }

            if (preferredLineSpeedFactor < 1f && nextSpeed > target && seconds > 0f && !paceSpeedCeiling.HasValue)
            {
                // Each 10% of corner target-speed penalty adds up to 1 mph/s of deceleration.
                const float decelerationPerFullPenalty = 10f * TrackDefinition.MetersPerMile / 3600f;
                float extraLoss = (1f - preferredLineSpeedFactor) * decelerationPerFullPenalty * seconds;
                float maxLoss = LapSpeedProfile.MaximumBraking(simulationContext.Track) * brakingGrip * seconds;
                float remainingLoss = Math.Max(0f, maxLoss - (currentSpeed - nextSpeed));
                float slowedSpeed = Math.Max(target, nextSpeed - Math.Min(extraLoss, remainingLoss));
                if (slowedSpeed < nextSpeed)
                {
                    throttleInputs[index] = 0f;
                    brakeInputs[index] = Math.Max(brakeInputs[index],
                        UnityEngine.Mathf.Clamp01((currentSpeed - slowedSpeed) / maxLoss));
                    nextSpeed = slowedSpeed;
                }
            }
            return nextSpeed;
        }

        float coastingSpeed = Math.Max(0f, (float)car.Result.InstantSpeed - 6f * seconds);
        if (coastingSpeed <= 0f)
        {
            Retire(index);
        }
        return coastingSpeed;
    }

    public void Retire(int index)
    {
        var car = cars[index];
        car.Result.InstantSpeed = 0;
        car.Transmission.Update(0.0);
        car.Retired = true;
        car.Finished = true;
        car.FinishTime = float.PositiveInfinity;
    }

    public float LapLength => lapLength;
    public double RemainingRaceMeters => FinishSuspended ? double.PositiveInfinity :
        Math.Max(0.0, TargetLaps * (double)lapLength + startLine - leaderProgress);

    public double FuelForDistance(double meters) => Math.Max(0, meters) * gallonsPerMeter;

    public void Refuel(int index, double remainingMeters)
    {
        double target = Math.Min(SessionFuel.TankCapacity, FuelForDistance(remainingMeters) + 1.0);
        var result = cars[index].Result;
        result.Fuel = Math.Max(result.Fuel, target);
    }

    public bool Finished(int index) => cars[index].Finished;
    public float StepDuration(float seconds) => session.minutes > 0 ? Math.Min(seconds, Remaining) : seconds;

    // Position is supplied by the route controller, never inferred from the odometer.
    internal static float TrackPosition(double distance, float length) => (float)((distance % length + length) % length);
    internal float ThermalTrackPosition(int index) => thermalDistances[index];
    internal bool ThermalOnTrack(int index) => thermalOnTrack[index];

    public void RecordTravel(int index, double meters, double speedMetersPerSecond, double trackPosition,
        bool reducedBurn = false, bool onTrack = true)
    {
        var car = cars[index];
        var result = car.Result;
        result.InstantSpeed = meters > 0 ? speedMetersPerSecond : 0;
        car.Transmission.Update(result.InstantSpeed * 3600.0 / TrackDefinition.MetersPerMile);
        if (meters <= 0) throttleInputs[index]=brakeInputs[index]=0f;
        thermalSpeeds[index] = (float)result.InstantSpeed;
        thermalRacing[index] = onTrack && !reducedBurn && meters > 0;
        thermalOnTrack[index] = onTrack;
        thermalDistances[index] = TrackPosition(trackPosition, lapLength);
        if (meters <= 0)
            return;
        double fuelBeforeTravel = result.Fuel;
        float weightWearFactor = usesSpringPerformance
            ? VehicleSpeed003.WeightTireWearFactor(result.Fuel) : 1f;
        double adjustedMeters = meters * (reducedBurn ? SessionFuel.ReducedBurnFactor : 1.0);
        result.Fuel = Math.Max(0, result.Fuel - adjustedMeters * gallonsPerMeter);

        if (result.car == null)
            return;

        const float rightSideWearMultiplier = 1.5f;
        const double referenceSpeed = 63.0;
        float wear = (float)(adjustedMeters * tireWearPerMeter * (speedMetersPerSecond / referenceSpeed)) * weightWearFactor;
        for (int indexOnCar = 0; indexOnCar < 4; indexOnCar++)
        {
            var tire = result.car.GetTire(indexOnCar);
            if (tire != null)
            {
                float tireWear = indexOnCar % 2 == 0 ? wear : wear * rightSideWearMultiplier;
                if (usesSpringPerformance)
                    tireWear *= VehicleSpeed003.TireTemperatureWearFactor(
                        TireTemperatureModel.Fahrenheit(tire.temperature)) *
                        VehicleSpeed003.DistributionTireLoadFactor(SetupForCar(index), indexOnCar, fuelBeforeTravel) *
                        VehicleSpeed003.PressureWear(SetupForCar(index), indexOnCar, tire) *
                        dynamicLoads[index].Ratio(indexOnCar, SetupForCar(index), fuelBeforeTravel);
                tire.wear = UnityEngine.Mathf.Clamp01(tire.wear + tireWear);
            }
        }
    }

    public void Record(int index, double before, double after, float seconds)
    {
        var car = cars[index];
        if (car.Finished || HasTerminalFailure(index) || after <= before)
            return;

        if (IsRace)
        {
            car.Progress = (float)after;
            car.Speed = (float)((after - before) / seconds);
        }
        while (after >= car.NextCrossing)
        {
            double fraction = Math.Max(0d, Math.Min(1d, (car.NextCrossing - before) / (after - before)));
            double preciseCrossing = PreciseElapsed + (double)seconds * fraction;
            float crossing = (float)preciseCrossing;
            if (car.LastCrossing >= 0f)
            {
                car.Result.lapTimes.Add((float)(preciseCrossing - car.LastCrossing));
                if (IsRace && scoredStages < BonusStageCount &&
                    car.Result.lapTimes.Count == StageEnd)
                    stageCrossingTime = Math.Min(stageCrossingTime, crossing);
                TimingRevision++;
                if (finishOnCheckered)
                {
                    car.CrossingThisStep = crossing;
                    car.CrossingDistance = (float)car.NextCrossing;
                    if (!FinishSuspended && car.Result.lapTimes.Count >= TargetLaps)
                        checkeredTime = Math.Min(checkeredTime, crossing);
                }
                else if (session.laps > 0 && car.Result.lapTimes.Count >= session.laps)
                {
                    car.Finished = true;
                    car.FinishTime = crossing;
                    if (IsRace)
                    {
                        car.Progress = (float)car.NextCrossing;
                        car.History.Add((car.Progress, crossing));
                    }
                    return;
                }
            }
            car.LastCrossing = preciseCrossing;
            car.NextCrossing += lapLength;
        }
        if (IsRace && Elapsed + seconds - car.History[car.History.Count - 1].Time >= 0.25f)
            car.History.Add(((float)after, Elapsed + seconds));
    }

    internal bool TryHandlingFeedback(int index, out int phase, out float balance,
        out float tireContribution, out float loadContribution)
    {
        phase = -1;
        balance = tireContribution = loadContribution = 0f;
        if (!usesSpringPerformance || !thermalOnTrack[index] || Retired(index)) return false;
        float speed = CurrentSpeed(index);
        if (speed < 15f || Math.Abs(tireTemperatures.CurvatureAt(thermalDistances[index])) * speed * speed / 9.80665f < 0.35f)
            return false;
        phase = tireTemperatures.HandlingPhaseAt(thermalDistances[index]);
        if (phase < 0) return false;
        balance = VehicleSpeed003.HandlingBalance(SetupForCar(index), Vehicle(index), dynamicLoads[index],
            Fuel(index), out tireContribution, out loadContribution);
        return true;
    }

    internal float DynamicCornerWeight(int index, int corner) =>
        dynamicLoads[index].Load(corner, SetupForCar(index), Fuel(index));

    internal Setup SetupForCar(int index) =>
        (index == 0 || simulationContext.EqualConditionTesting) && simulationContext.PlayerSetup != null
            ? simulationContext.PlayerSetup : simulationContext.RivalSetup(index) ?? cars[index].Result.car?.setup;

    internal float SetupAdjustedSpeed(int index, float distance, float speed)
    {
        if (!usesSpringPerformance)
            return speed;
        if (simulationContext.Track.restrictorPlate)
            return speed * VehicleSpeed003.RestrictorPlatePaceFactor(simulationContext.Track,
                SetupForCar(index), cars[index].Result.car, Fuel(index), Weather.GripFactor);
        if (inputSpeedProfile.FlatOutKinkAt(distance))
            return speed * VehicleSpeed003.PressureStraightSpeed(SetupForCar(index), cars[index].Result.car);
        float cornerLoad = UnityEngine.Mathf.SmoothStep(0f, 1f,
            UnityEngine.Mathf.InverseLerp(0.05f, 1.5f,
                speed * speed * UnityEngine.Mathf.Abs(tireTemperatures.CurvatureAt(distance)) / 9.80665f));
        var setup = SetupForCar(index);
        float factor = VehicleSpeed003.CornerSpeedFactor(simulationContext.Track, setup,
            cars[index].Result.car, Fuel(index));
        factor *= VehicleSpeed003.ShockSpeedFactor(simulationContext.Track, setup,
            tireTemperatures.ShockMotionAt(distance, speed));
        factor *= dynamicLoads[index].CornerSpeedFactor(setup, Fuel(index));
        factor *= VehicleSpeed003.WeightCornerSpeedFactor(Fuel(index)) * UnityEngine.Mathf.Sqrt(Weather.GripFactor);
        // Unwind the cornering demand as the driver applies power in the final third.
        cornerLoad *= 1f-inputSpeedProfile.CornerExitAt(distance);
        return speed * UnityEngine.Mathf.Lerp(VehicleSpeed003.PressureStraightSpeed(setup, cars[index].Result.car), factor, cornerLoad);
    }

    public void AdvanceClock(float seconds, bool? raceLapUnderCaution = null)
    {
        for (int i = 0; i < cars.Length; i++)
        {
            if (usesSpringPerformance && seconds > 0f)
            {
                float longitudinalG = (thermalSpeeds[i] - previousThermalSpeeds[i]) / seconds / 9.80665f;
                float lateralG = thermalOnTrack[i] ? thermalSpeeds[i] * thermalSpeeds[i] * tireTemperatures.CurvatureAt(thermalDistances[i]) / 9.80665f : 0f;
                dynamicLoads[i].Advance(SetupForCar(i), Fuel(i), longitudinalG, lateralG, seconds);
            }
            tireTemperatures.Advance(cars[i].Result.car, seconds, thermalDistances[i],
                thermalSpeeds[i], previousThermalSpeeds[i],
                usesSpringPerformance ? simulationContext.Track : null, SetupForCar(i), thermalRacing[i], Weather.TrackFahrenheit, Fuel(i), usesSpringPerformance ? dynamicLoads[i] : null, onTrack: thermalOnTrack[i]);
            previousThermalSpeeds[i] = thermalSpeeds[i];
            thermalSpeeds[i] = 0f;
            thermalRacing[i] = false;
        }
        if (IsRace)
        {
            if (stageCrossingTime < float.PositiveInfinity)
            {
                float fraction = Math.Max(0f, Math.Min(1f, (stageCrossingTime - Elapsed) / seconds));
                var stageOrder = cars.OrderByDescending(car =>
                    car.StepStartProgress + (car.Progress - car.StepStartProgress) * fraction)
                    .ThenBy(car => car.Result.carNumber).Take(10);
                int points = 10;
                foreach (var car in stageOrder)
                {
                    car.Result.stagePoints[scoredStages] = points;
                    car.Result.pointsEarned += points--;
                }
                StageStartLap = StageEnd;
                scoredStages++;
                stageEndOverride = null;
                stageCrossingTime = float.PositiveInfinity;
            }

            foreach (var car in cars)
            {
                if (finishOnCheckered && !car.Finished && car.CrossingThisStep >= checkeredTime)
                {
                    car.Finished = true;
                    car.FinishTime = car.CrossingThisStep;
                    car.Progress = car.CrossingDistance;
                }

                car.CrossingThisStep = float.NegativeInfinity;
                car.StepStartProgress = car.Progress;
            }
            leaderProgress = cars.Max(car => car.Progress);
            if (raceLapUnderCaution.HasValue)
            {
                int completed = LeaderLaps;
                while (raceLapCautions.Count < completed)
                    raceLapCautions.Add(raceLapUnderCaution.Value);
            }
        }
        PreciseElapsed += seconds;
        Weather.Advance(seconds, PreciseElapsed);
    }

    private (int Status, int Laps, float FinishTime, float Distance, int Number) RaceOrderKey(CarTiming car) =>
        (car.Retired ? 2 : car.Finished ? 0 : 1,
            finishOnCheckered && car.Finished ? -car.Result.lapTimes.Count : 0,
            car.Finished ? car.FinishTime : float.PositiveInfinity,
            -car.Progress, car.Result.carNumber);

    internal int RaceLeaderIndex()
    {
        int leader = 0;
        var best = RaceOrderKey(cars[leader]);
        for (int i = 1; i < cars.Length; i++)
        {
            var key = RaceOrderKey(cars[i]);
            if (key.CompareTo(best) < 0)
            {
                leader = i;
                best = key;
            }
        }
        return leader;
    }

    public IEnumerable<(SessionCarResult Car, int LapsBehind, float SecondsBehind)> RunningOrder(bool compareToLeader = false)
    {
        var ordered = cars.OrderBy(RaceOrderKey);
        CarTiming ahead = null;
        CarTiming leader = null;
        foreach (var car in ordered)
        {
            if (ahead == null)
            {
                leader = car;
                yield return (car.Result, 0, float.NaN);
            }
            else
            {
                var reference = compareToLeader ? leader : ahead;
                float gap = Math.Max(0f, reference.Progress - car.Progress);
                int laps = (int)(Math.Max(0f, leader.Progress - car.Progress) / lapLength);
                float time = car.Finished && !car.Retired ? car.FinishTime : Elapsed;
                float seconds = laps > 0 ? 0f : TimeGap(reference, car.Progress, gap, time);
                yield return (car.Result, laps, seconds);
            }
            ahead = car;
        }
    }

    private static float TimeGap(CarTiming ahead, float position, float distance, float time)
    {
        var history = ahead.History;
        if (history.Count == 0)
            return float.NaN;
        if (position < history[0].Distance)
            return ahead.Speed > 0f ? distance / ahead.Speed : float.NaN;

        int low = 0;
        int high = history.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (history[middle].Distance < position)
                low = middle + 1;
            else
                high = middle;
        }
        if (low == 0)
            return Math.Max(0f, time - history[0].Time);

        var previous = history[low - 1];
        var next = low < history.Count ? history[low] : (ahead.Progress, time);
        float span = next.Item1 - previous.Distance;
        float fraction = span > 0f ? (position - previous.Distance) / span : 0f;
        float crossing = previous.Time + (next.Item2 - previous.Time) * Math.Max(0f, Math.Min(1f, fraction));
        return Math.Max(0f, time - crossing);
    }

    public int IndexOfCar(SessionCarResult result) =>
        Array.FindIndex(cars, car => car.Result == result);

    public IEnumerable<SessionCarResult> FastestLaps() =>
        cars.Select(car => car.Result).OrderBy(result => result.FastestLap).ThenBy(result => result.carNumber);

    public List<SessionCarResult> Results()
    {
        var ordered = session.kind == RaceSessionKind.Race
            ? cars.OrderBy(car => car.Retired).ThenByDescending(car => finishOnCheckered || car.Retired ? car.Result.lapTimes.Count : 0).ThenBy(car => car.FinishTime)
            : cars.OrderBy(car => car.Result.FastestLap);
        var results = frozenOrder == null ? ordered.Select(car => car.Result).ToList() :
            Enumerable.Range(0, cars.Length).OrderBy(index => frozenOrder[index]).Select(index => cars[index].Result).ToList();
        for (int i = 0; i < results.Count; i++)
            results[i].position = i + 1;
        foreach (var car in cars)
            car.Result.damageAtFinish = (car.Result.car?.Damage ?? 0f) + car.SimulatedContactDamage;
        if (IsRace && Complete)
        {
            var fastest = FastestLaps().FirstOrDefault(result => result.FastestLap > 0f &&
                !float.IsInfinity(result.FastestLap));
            foreach (var result in results)
            {
                result.finishingPoints = result.position == 1 ? 55 : Math.Max(1, 37 - result.position);
                result.fastestLapPoints = result == fastest ? 1 : 0;
                result.pointsEarned = result.stagePoints.Sum() + result.finishingPoints + result.fastestLapPoints;
            }
        }
        return results;
    }

}
