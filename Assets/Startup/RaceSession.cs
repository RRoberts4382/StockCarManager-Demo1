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

    public const int PracticeMinutes = 999; // Temporary development duration.

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
    public double Fuel = SessionFuel.TankCapacity;
    [NonSerialized] public double InstantSpeed;
    public string carName = string.Empty;
    public int carNumber;
    public string driverName;
    public string teamName;
    public List<float> lapTimes = new List<float>();
    public int position;
    public bool wrecked;
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
    private readonly bool usesSpringPerformance;
    private readonly DynamicTireLoads[] dynamicLoads;
    private readonly float[] thermalSpeeds;
    private readonly bool[] thermalRacing;
    private readonly float[] previousThermalSpeeds;
    private readonly float[] thermalDistances;
    private readonly bool[] thermalOnTrack;
    private int scoredStages;
    private float leaderProgress;
    private readonly bool finishOnCheckered;
    private int? stageEndOverride;
    private int[] frozenOrder;
    private float checkeredTime = float.PositiveInfinity;
    private float stageCrossingTime = float.PositiveInfinity;
    public int CurrentStage => scoredStages + 1;
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
    public int StageEnd => stageEndOverride ?? (CurrentStage <= RaceSession.TestBonusStages
        ? CurrentStage * RaceSession.TestOpeningStageLaps : TargetLaps);

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
        cars = new CarTiming[entries.Count];
        for (int i = 0; i < cars.Length; i++)
        {
            var timing = new CarTiming
            {
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
            if (IsRace)
                timing.Result.stagePoints = new int[RaceSession.TestBonusStages];
            cars[i] = timing;
        }
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
        var profile = speedModel.UsesLapSpeedProfile ? new LapSpeedProfile(simulation.Track) : null;
        float profileStart = simulation.Track.StartFinishFraction * lapLength;
        while (!run.Complete)
        {
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
        }
        return run.Results();
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

    public bool Retired(int index) => cars[index].Retired;

    public float ConditionLimitedSpeed(int index, float speed, float seconds, float brakingLimit = 0f)
    {
        var car = cars[index];
        float damage = (car.Result.car?.Damage ?? 0f) + car.SimulatedContactDamage;
        if (car.Result.Fuel > 0 && damage < 100f)
        {
            const float metersPerSecondToMph = 3600f / TrackDefinition.MetersPerMile;
            float currentSpeed = (float)car.Result.InstantSpeed;
            float rollingSpeed = UnityEngine.Mathf.InverseLerp(60f, 180f, currentSpeed * metersPerSecondToMph);
            float acceleration = UnityEngine.Mathf.Lerp(7.89f, 3.5f, rollingSpeed);
            if (usesSpringPerformance)
            {
                float weightFactor = VehicleSpeed003.WeightForceFactor(car.Result.Fuel);
                var setup = SetupForCar(index);
                acceleration *= UnityEngine.Mathf.Min(1f, weightFactor * VehicleSpeed003.DistributionAccelerationFactor(setup, car.Result.Fuel));
                brakingLimit *= UnityEngine.Mathf.Min(1f, weightFactor * VehicleSpeed003.DistributionBrakingFactor(setup, car.Result.Fuel)) * Weather.GripFactor;
            }
            float target = speed * (1f - damage / 100f);
            if (brakingLimit > 0f && target < currentSpeed)
                return Math.Max(target, currentSpeed - brakingLimit * seconds);
            return Math.Min(target, currentSpeed + acceleration * seconds);
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
        var result = cars[index].Result;
        result.InstantSpeed = meters > 0 ? speedMetersPerSecond : 0;
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
        if (car.Finished || after <= before)
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
                if (IsRace && scoredStages < RaceSession.TestBonusStages &&
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
        return speed * UnityEngine.Mathf.Lerp(VehicleSpeed003.PressureStraightSpeed(setup, cars[index].Result.car), factor, cornerLoad);
    }

    public void AdvanceClock(float seconds)
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
        }
        PreciseElapsed += seconds;
        Weather.Advance(seconds, PreciseElapsed);
    }

    public IEnumerable<(SessionCarResult Car, int LapsBehind, float SecondsBehind)> RunningOrder()
    {
        var ordered = cars.OrderBy(car => car.Retired ? 2 : car.Finished ? 0 : 1)
            .ThenByDescending(car => finishOnCheckered && car.Finished ? car.Result.lapTimes.Count : 0)
            .ThenBy(car => car.Finished ? car.FinishTime : float.PositiveInfinity)
            .ThenByDescending(car => car.Progress).ThenBy(car => car.Result.carNumber);
        CarTiming ahead = null;
        float leaderDistance = 0f;
        foreach (var car in ordered)
        {
            if (ahead == null)
            {
                leaderDistance = car.Progress;
                yield return (car.Result, 0, float.NaN);
            }
            else
            {
                float gap = Math.Max(0f, ahead.Progress - car.Progress);
                int laps = (int)(Math.Max(0f, leaderDistance - car.Progress) / lapLength);
                float time = car.Finished && !car.Retired ? car.FinishTime : Elapsed;
                float seconds = laps > 0 ? 0f : TimeGap(ahead, car.Progress, gap, time);
                yield return (car.Result, laps, seconds);
            }
            ahead = car;
        }
    }

    private static float TimeGap(CarTiming ahead, float position, float distance, float time)
    {
        var history = ahead.History;
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
