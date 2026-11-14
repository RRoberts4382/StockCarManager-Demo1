using System;
using System.Collections.Generic;
using System.Linq;

internal enum RaceSessionKind { Practice, Qualifying, Race }

[Serializable]
internal sealed class PracticeWear
{
    public string chassis;
    public float amount;
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
    public RaceSessionKind kind;
    public string name;
    public int minutes;
    public int laps;
    public List<SessionCarResult> results = new List<SessionCarResult>();
    public bool completed;
    public List<PracticeWear> practiceWear;
    public int[] qualifyingOrder;

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
    public double Fuel = SessionFuel.TankCapacity;
    [NonSerialized] public double InstantSpeed;
    public string carName = string.Empty;
    public int carNumber;
    public string driverName;
    public string teamName;
    public List<float> lapTimes = new List<float>();
    public int position;
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
    private readonly double gallonsPerMeter;
    private readonly double tireWearPerMeter;
    private int scoredStages;
    private float leaderProgress;
    private readonly bool finishOnCheckered;
    private int? stageEndOverride;
    private int[] frozenOrder;
    private float checkeredTime = float.PositiveInfinity;
    private float stageCrossingTime = float.PositiveInfinity;
    public int CurrentStage => scoredStages + 1;
    public int StageStartLap { get; private set; }
    public bool IsRace => session.kind == RaceSessionKind.Race;
    public float Elapsed { get; private set; }
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

    public int PlayerLaps => cars[0].Result.lapTimes.Count;
    public bool Complete => session.minutes > 0 ? Remaining <= 0f : cars.All(car => car.Finished);

    private sealed class CarTiming
    {
        public SessionCarResult Result;
        public float NextCrossing;
        public float LastCrossing = -1f;
        public float FinishTime;
        public float CrossingThisStep = float.NegativeInfinity;
        public float CrossingDistance;
        public float SpeedFactor;
        public bool Finished;
        public bool Retired;
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
        TargetLaps = session.laps;
        this.finishOnCheckered = finishOnCheckered;
        this.lapLength = lapLength;
        this.startLine = startLine;
        gallonsPerMeter = SessionFuel.GallonsPerMeter(simulation.Track);
        float roughness = UnityEngine.Mathf.Clamp01(simulation.Track.weights?.surfaceRoughness ?? 0.5f);
        tireWearPerMeter = (0.001 + 0.003 * roughness) / TrackDefinition.MetersPerMile;
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
            if (IsRace)
                timing.History.Add((timing.Progress, 0f));
            timing.StepStartProgress = timing.Progress;
            timing.Result = entries[i];
            if (IsRace)
                timing.Result.stagePoints = new int[RaceSession.TestBonusStages];
            cars[i] = timing;
        }
    }

    public static List<SessionCarResult> Simulate(CarSimulationContext simulation, IVehicleSpeedModel speedModel,
        Random random, int[] qualifyingOrder = null)
    {
        var session = simulation.Session;
        var entries = simulation.Cars;
        float lapLength = simulation.Track.lengthMiles * TrackDefinition.MetersPerMile;
        var distances = new float[entries.Count];
        if (session.kind == RaceSessionKind.Race)
        {
            var slots = RaceSession.GridSlots(entries, qualifyingOrder);
            float spacing = Math.Min(4.5f, lapLength / (2f * entries.Count));
            for (int i = 0; i < distances.Length; i++)
                distances[i] = -spacing * slots[i];
        }
        var baseSpeeds = new float[entries.Count];
        for (int i = 0; i < baseSpeeds.Length; i++)
            baseSpeeds[i] = speedModel.BaseSpeed(simulation, i, random);

        var run = new RaceSessionRun(session, lapLength, 0f, simulation, speedModel, random, initialDistances: distances);
        var profile = speedModel is VehicleSpeed002 ? new LapSpeedProfile(simulation.Track) : null;
        float profileStart = simulation.Track.StartFinishFraction * lapLength;
        while (!run.Complete)
        {
            float seconds = run.StepDuration(profile == null ? 1f : 0.25f);
            for (int i = 0; i < entries.Count; i++)
            {
                if (run.Finished(i))
                    continue;

                float previous = distances[i];
                float baseSpeed = baseSpeeds[i] * run.SpeedFactor(i);
                float speed = profile?.SpeedAt(distances[i] + profileStart, baseSpeed) ?? baseSpeed;
                if (run.IsRace && run.Fuel(i) <= run.FuelForDistance(lapLength) + 0.5)
                {
                    double remaining = Math.Max(0, session.laps - run.LeaderLaps) * (double)lapLength;
                    run.Refuel(i, remaining);
                }
                speed = run.ConditionLimitedSpeed(i, speed, seconds,
                    profile?.BrakingLimitAt(distances[i] + profileStart) ?? 0f);
                distances[i] += speed * seconds;
                run.RecordTravel(i, distances[i] - previous, speed);
                run.Record(i, previous, distances[i], seconds);
            }
            run.AdvanceClock(seconds);
        }
        return run.Results();
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
                car.Result.lapTimes.Add(lapLength / speed);
                RecordTravel(i, lap == 0 ? 2.0 * lapLength : lapLength, speed);
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
            Vehicle(index).Damage += (float)(meters / TrackDefinition.MetersPerMile) * TireCount(mask);
    }

    public TeamCar Vehicle(int index) => cars[index].Result.car;

    public void AddFuel(int index, double gallons)
    {
        var result = cars[index].Result;
        result.Fuel = Math.Min(SessionFuel.TankCapacity, result.Fuel + Math.Max(0, gallons));
    }

    public double Fuel(int index) => cars[index].Result.Fuel;

    public float CurrentSpeed(int index) => (float)cars[index].Result.InstantSpeed;

    public bool Retired(int index) => cars[index].Retired;

    public float ConditionLimitedSpeed(int index, float speed, float seconds, float brakingLimit = 0f)
    {
        var car = cars[index];
        float damage = car.Result.car?.Damage ?? 0f;
        if (car.Result.Fuel > 0 && damage < 100f)
        {
            const float metersPerSecondToMph = 3600f / TrackDefinition.MetersPerMile;
            float currentSpeed = (float)car.Result.InstantSpeed;
            float rollingSpeed = UnityEngine.Mathf.InverseLerp(60f, 180f, currentSpeed * metersPerSecondToMph);
            float acceleration = UnityEngine.Mathf.Lerp(7.89f, 3.5f, rollingSpeed);
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

    public void RecordTravel(int index, double meters, double speedMetersPerSecond, bool reducedBurn = false)
    {
        var result = cars[index].Result;
        result.InstantSpeed = meters > 0 ? speedMetersPerSecond : 0;
        if (meters <= 0)
            return;
        double adjustedMeters = meters * (reducedBurn ? SessionFuel.ReducedBurnFactor : 1.0);
        result.Fuel = Math.Max(0, result.Fuel - adjustedMeters * gallonsPerMeter);

        if (result.car == null)
            return;

        const float rightSideWearMultiplier = 1.5f;
        const double referenceSpeed = 63.0;
        float wear = (float)(adjustedMeters * tireWearPerMeter * (speedMetersPerSecond / referenceSpeed));
        for (int indexOnCar = 0; indexOnCar < 4; indexOnCar++)
        {
            var tire = result.car.GetTire(indexOnCar);
            if (tire != null)
            {
                float tireWear = indexOnCar % 2 == 0 ? wear : wear * rightSideWearMultiplier;
                tire.wear = UnityEngine.Mathf.Clamp01(tire.wear + tireWear);
            }
        }
    }

    public void Record(int index, float before, float after, float seconds)
    {
        var car = cars[index];
        if (car.Finished || after <= before)
            return;

        if (IsRace)
        {
            car.Progress = after;
            car.Speed = (after - before) / seconds;
        }
        while (after >= car.NextCrossing)
        {
            float fraction = Math.Max(0f, Math.Min(1f, (car.NextCrossing - before) / (after - before)));
            float crossing = Elapsed + seconds * fraction;
            if (car.LastCrossing >= 0f)
            {
                car.Result.lapTimes.Add(crossing - car.LastCrossing);
                if (IsRace && scoredStages < RaceSession.TestBonusStages &&
                    car.Result.lapTimes.Count == StageEnd)
                    stageCrossingTime = Math.Min(stageCrossingTime, crossing);
                TimingRevision++;
                if (finishOnCheckered)
                {
                    car.CrossingThisStep = crossing;
                    car.CrossingDistance = car.NextCrossing;
                    if (!FinishSuspended && car.Result.lapTimes.Count >= TargetLaps)
                        checkeredTime = Math.Min(checkeredTime, crossing);
                }
                else if (session.laps > 0 && car.Result.lapTimes.Count >= session.laps)
                {
                    car.Finished = true;
                    car.FinishTime = crossing;
                    if (IsRace)
                    {
                        car.Progress = car.NextCrossing;
                        car.History.Add((car.Progress, crossing));
                    }
                    return;
                }
            }
            car.LastCrossing = crossing;
            car.NextCrossing += lapLength;
        }
        if (IsRace && Elapsed + seconds - car.History[car.History.Count - 1].Time >= 0.25f)
            car.History.Add((after, Elapsed + seconds));
    }

    public void AdvanceClock(float seconds)
    {
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
        Elapsed += seconds;
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
