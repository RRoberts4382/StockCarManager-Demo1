using System;

internal sealed class SessionPitStops
{
    private const float RepairSecondsPerPoint = 0.5f;
    private readonly TrackPitTraffic pit;
    private readonly TrackPitTraffic.Car[] cars;
    private readonly float[] stalls;
    private readonly Stop[] stops;
    private readonly double[] nextRivalReview;
    private bool holdPlayerInGarage;
    private float garageVisitTarget;

    private struct Stop
    {
        public bool Requested;
        public bool PlayerEditable;
        public bool RivalStrategy;
        public bool StrategicEmergency;
        public double RivalFuelTarget;
        public float FuelGallons;
        public int TireMask;
        public float[] Pressures;
        public float RepairPoints;
        public bool Servicing;
        public bool FlatTireStop;
        public float Time;
        public float? Duration;
        public SetupServicePlan SetupWork;
        public Action CompleteSetup;
    }

    public SessionPitStops(TrackPitTraffic pit, TrackPitTraffic.Car[] cars, float[] stalls)
    {
        this.pit = pit;
        this.cars = cars;
        this.stalls = stalls;
        stops = new Stop[cars.Length];
        nextRivalReview = new double[cars.Length];
    }

    public bool CanRequest(RaceSessionRun run) =>
        !holdPlayerInGarage && (run.IsRace ? run.RemainingRaceMeters > 0 : run.Remaining > 0) &&
        !run.Finished(0) && !run.Retired(0) && !stops[0].Requested && !stops[0].Servicing && run.Fuel(0) > 0;

    public void SetGarageVisit(bool visiting, float? alternateTarget = null)
    {
        holdPlayerInGarage = visiting;
        garageVisitTarget = alternateTarget ?? stalls[0];
        if (!visiting && !stops[0].Requested)
            cars[0].NextEntry = float.PositiveInfinity;
    }

    internal float StopTarget(int index)
    {
        if (stops[index].Requested)
            return stalls[index];
        if (index != 0 || !holdPlayerInGarage)
            return float.PositiveInfinity;
        var player = cars[0];
        return garageVisitTarget != stalls[0] && player.InPit && player.PitDistance > garageVisitTarget
            ? float.PositiveInfinity : garageVisitTarget;
    }

    public string PlayerStatus { get; private set; } = string.Empty;
    internal int PlayerCompletedStops { get; private set; }

    public bool NeedsFlatTireService(int index) => stops[index].FlatTireStop;

    public bool IsRequested(int index) => stops[index].Requested;

    public bool IsServicing(int index) => stops[index].Servicing;

    internal void ClearObstructedCar(int index)
    {
        stops[index] = default;
        if (index == 0) PlayerStatus = "Car cleared from pit lane";
    }

    public bool CanEditPlayerRequest(RaceSessionRun run) =>
        !holdPlayerInGarage && !run.Finished(0) && !run.Retired(0) &&
        stops[0].Requested && stops[0].PlayerEditable && !stops[0].Duration.HasValue;

    internal bool CanCancelPlayerRequest(RaceSessionRun run) =>
        CanEditPlayerRequest(run) && !cars[0].InPit && !stops[0].FlatTireStop && run.FlatTireMask(0) == 0;

    internal bool TryCancelPlayerRequest(RaceSessionRun run)
    {
        if (!CanCancelPlayerRequest(run))
            return false;

        stops[0] = default;
        var player = cars[0];
        player.NextEntry = float.PositiveInfinity;
        player.DeferredEntry = float.NegativeInfinity;
        player.ApronEntryActive = false;
        PlayerStatus = string.Empty;
        return true;
    }

    public void RequestPlayer(RaceSessionRun run, float fuelGallons, int tireMask, float repairPoints,
        float[] pressures = null, bool playerEditable = false)
    {
        if (!CanRequest(run))
            return;

        stops[0].Requested = true;
        stops[0].PlayerEditable = playerEditable;
        SetPlayerService(run, fuelGallons, tireMask, repairPoints, pressures);
        PlayerStatus = "Pit stop queued";
    }

    public void EditPlayerRequest(RaceSessionRun run, float fuelGallons, int tireMask, float repairPoints, float[] pressures)
    {
        if (CanEditPlayerRequest(run))
            SetPlayerService(run, fuelGallons, tireMask | run.FlatTireMask(0), repairPoints, pressures);
    }

    private void SetPlayerService(RaceSessionRun run, float fuelGallons, int tireMask, float repairPoints, float[] pressures)
    {
        stops[0].FuelGallons = fuelGallons;
        stops[0].TireMask = tireMask;
        stops[0].Pressures = !run.SetupLocked && pressures?.Length == 4 ? (float[])pressures.Clone() : null;
        stops[0].RepairPoints = repairPoints;
    }

    internal bool RequestRivalSetupStop(RaceSessionRun run, int index, SetupServicePlan work, Action complete)
    {
        if (run.SetupLocked || index <= 0 || index >= stops.Length || stops[index].Requested || stops[index].Servicing || run.Retired(index)) return false;
        stops[index].Requested = true;
        stops[index].SetupWork = work;
        stops[index].CompleteSetup = complete;
        stops[index].TireMask = 15;
        stops[index].FuelGallons = (float)SessionFuel.TankCapacity;
        return true;
    }

    internal static float FuelServiceSeconds(float gallons) =>
        gallons > 0f ? 7f + 2f * (float)(gallons / SessionFuel.TankCapacity) : 0f;

    internal static float ServiceDuration(int tireCount, float gallons, float repairPoints)
    {
        float tireSeconds = tireCount switch
        {
            1 => 5f,
            2 => 8f,
            3 => 10f,
            4 => 12f,
            _ => 0f
        };
        return Math.Max(tireSeconds, FuelServiceSeconds(gallons)) + repairPoints * RepairSecondsPerPoint;
    }

    internal bool ArmQueuedEntry(int index, bool pitOpen, bool allowEmergencyEntry)
    {
        var car = cars[index];
        ref var stop = ref stops[index];
        if (car.InPit || (!stop.Requested && (index != 0 || !holdPlayerInGarage)))
            return false;

        if (pitOpen || stop.StrategicEmergency || (stop.FlatTireStop && allowEmergencyEntry))
            pit.CallToStall(car, StopTarget(index));
        else
            car.NextEntry = float.PositiveInfinity;
        return true;
    }

    internal void PlanRivalRaceStop(RaceSessionRun run, int index, RivalPitWindow window, bool pitOpen)
    {
        var car = cars[index];
        ref var stop = ref stops[index];
        if (car.InPit || run.Finished(index) || run.HasTerminalFailure(index) || stop.Servicing ||
            stop.Requested && !stop.RivalStrategy) return;

        // A new caution can invalidate a queued stop before the driver commits to pit road.
        bool reconsider = stop.Requested && ((!pitOpen && !stop.StrategicEmergency) ||
            window.EndsUnderCaution && !stop.StrategicEmergency && !stop.FlatTireStop);
        float damage = run.PitRepairableDamage(index);
        if (!reconsider && stop.Requested && damage <= Math.Max(stop.RepairPoints, RivalPitStrategy.DamageThreshold))
        {
            ArmQueuedEntry(index, pitOpen, allowEmergencyEntry: false);
            return;
        }
        if (!reconsider && !stop.Requested && damage <= RivalPitStrategy.DamageThreshold &&
            run.PreciseElapsed < nextRivalReview[index]) return;

        nextRivalReview[index] = run.PreciseElapsed + .5d;
        if (stop.FlatTireStop) return;
        float toEntry = pit.DistanceToEntry(car);
        float greenTransitLoss = Math.Max(0f, pit.RouteLength / Math.Max(1f, pit.SpeedLimit) -
            UnityEngine.Mathf.Repeat(pit.ExitDistance - pit.EntryDistance, run.LapLength) / run.PitStrategyGreenSpeed(index));
        var plan = run.PlanRivalPitStop(index, window, toEntry, stalls[index], greenTransitLoss, pitOpen);
        if (!plan.Requested)
        {
            if (reconsider)
            {
                stop = default;
                car.NextEntry = float.PositiveInfinity;
            }
            return;
        }

        stop.Requested = stop.RivalStrategy = true;
        stop.StrategicEmergency = plan.Emergency;
        stop.RivalFuelTarget = plan.FuelTarget;
        stop.TireMask = plan.TireMask;
        stop.RepairPoints = plan.RepairPoints;
        ArmQueuedEntry(index, pitOpen, allowEmergencyEntry: false);
    }

    internal static void CompleteService(RaceSessionRun run, int index, float gallons, int tireMask,
        float repairPoints, float[] pressures = null)
    {
        run.AddFuel(index, gallons);
        var vehicle = run.Vehicle(index);
        if (vehicle != null)
        {
            for (int tire = 0; tire < 4; tire++)
            {
                if ((tireMask & (1 << tire)) == 0 || vehicle.GetTire(tire) is not CarTire current) continue;
                current.wear = 0f;
                current.temperature = 0f;
                current.coldPressurePsi = pressures?.Length == 4
                    ? UnityEngine.Mathf.Clamp(pressures[tire], 10f, 60f)
                    : VehicleSpeed003.ColdPressure(run.SetupForCar(index), tire);
            }
        }
        run.RepairPitDamage(index, repairPoints);
    }

    public void Advance(int index, RaceSessionRun run, float seconds, float speed, bool pitOpen, bool allowEmergencyEntry = true)
    {
        var car = cars[index];
        ref var stop = ref stops[index];
        int flatTires = run.FlatTireMask(index);
        if (flatTires != 0 && !stop.Duration.HasValue)
        {
            stop.Requested = true;
            stop.FlatTireStop = true;
            stop.TireMask |= flatTires;
        }

        speed = run.FlatTireSpeed(speed, flatTires);
        car.FlatTireLaneBlend = UnityEngine.Mathf.MoveTowards(car.FlatTireLaneBlend,
            flatTires != 0 ? 1f : 0f, seconds);

        if (ArmQueuedEntry(index, pitOpen, allowEmergencyEntry) && index == 0)
            PlayerStatus = stop.FlatTireStop && (pitOpen || allowEmergencyEntry) ? "Flat tire · pitting" :
                pitOpen ? "Pit stop queued" : "Queued · pits closed";

        double before = car.Odometer;
        if (!car.InGarage)
            pit.Advance(car, seconds, speed, StopTarget(index));
        run.RecordFlatTireDamage(index, flatTires, car.Odometer - before);
        if (stop.Requested && car.InPit)
        {
            stop.Servicing = true;
            if (index == 0)
                PlayerStatus = "On pit road";
            if (car.PitDistance == stalls[index])
            {
                if (!stop.Duration.HasValue)
                {
                    var vehicle = run.Vehicle(index);
                    if (stop.RivalStrategy && run.PitRepairableDamage(index) > RivalPitStrategy.DamageThreshold)
                    {
                        stop.RepairPoints = run.PitRepairableDamage(index);
                        stop.RivalFuelTarget = SessionFuel.TankCapacity;
                        stop.TireMask = RivalPitStrategy.AllTires;
                    }
                    double fuel = run.Fuel(index);
                    double requestedFuel = stop.RivalStrategy ? stop.RivalFuelTarget - fuel : stop.FuelGallons;
                    stop.FuelGallons = (float)Math.Max(0, Math.Min(requestedFuel, SessionFuel.TankCapacity - fuel));
                    stop.RepairPoints = Math.Max(0f, Math.Min(stop.RepairPoints, run.PitRepairableDamage(index)));

                    int tireCount = 0;
                    for (int tire = 0; tire < 4; tire++)
                    {
                        if ((stop.TireMask & (1 << tire)) != 0 && vehicle?.GetTire(tire) != null)
                            tireCount++;
                    }
                    stop.Duration = ServiceDuration(tireCount, stop.FuelGallons, stop.RepairPoints) + stop.SetupWork.Seconds;
                    car.InGarage = stop.SetupWork.Garage;
                }
                else
                {
                    stop.Time += seconds;
                }

                if (stop.Time >= stop.Duration.Value)
                {
                    // Apply setup before fitting the new tires so their cold pressures match.
                    stop.CompleteSetup?.Invoke();
                    car.InGarage = false;
                    CompleteService(run, index, stop.FuelGallons, stop.TireMask, stop.RepairPoints, stop.Pressures);
                    stop = default;
                    if (index == 0)
                    {
                        PlayerCompletedStops++;
                        PlayerStatus = "Pit stop complete";
                    }
                }
                else if (index == 0)
                {
                    PlayerStatus = $"Service · {stop.Duration.Value - stop.Time:F1}s";
                }
            }
        }
    }
}

internal readonly struct RivalPitWindow
{
    internal readonly double Meters, YellowMeters;
    internal readonly bool FinalStage, EndsUnderCaution, ClosesBeforeTarget;

    internal RivalPitWindow(double meters, double yellowMeters, bool finalStage, bool endsUnderCaution, bool closesBeforeTarget)
    {
        Meters = meters;
        YellowMeters = Math.Clamp(yellowMeters, 0d, meters);
        FinalStage = finalStage;
        EndsUnderCaution = endsUnderCaution;
        ClosesBeforeTarget = closesBeforeTarget;
    }

    internal double FuelDistance(double meters)
    {
        double yellow = Math.Min(meters, YellowMeters);
        return meters - yellow + yellow * SessionFuel.ReducedBurnFactor;
    }
}

internal readonly struct RivalPitPlan
{
    internal readonly bool Requested, Emergency;
    internal readonly double FuelTarget;
    internal readonly int TireMask;
    internal readonly float RepairPoints;

    internal RivalPitPlan(double fuelTarget, int tireMask, float repairPoints, bool emergency)
    {
        Requested = true;
        FuelTarget = fuelTarget;
        TireMask = tireMask;
        RepairPoints = repairPoints;
        Emergency = emergency;
    }
}

internal static class RivalPitStrategy
{
    internal const float DamageThreshold = 25f;
    internal const int AllTires = 15;
    private const int RightTires = 10;

    internal static RivalPitPlan Decide(RivalPitWindow window, double fuel, double fuelPerMeter,
        double lapLength, double toEntry, double toStall, bool pitOpen, float damage, int flats,
        (float Current, float Finish, float NextEntry) left, (float Current, float Finish, float NextEntry) right,
        float lapSeconds, float greenTransitLoss)
    {
        if (window.Meters <= 0d) return default;
        if (damage > DamageThreshold)
            return new RivalPitPlan(SessionFuel.TankCapacity, AllTires, damage, emergency: true);

        double toNextEntry = toEntry + lapLength;
        double serviceBurn = toStall * SessionFuel.ReducedBurnFactor * fuelPerMeter;
        double reserve = Math.Max(.75d, 1.25d * lapLength * fuelPerMeter);
        double finishFuel = window.FuelDistance(window.Meters) * fuelPerMeter;
        // Reaching a closed stage window can mean waiting for the stages' caution laps to open pit road.
        bool lastBeforeClosure = window.ClosesBeforeTarget && toNextEntry >= window.Meters - 2d * lapLength;
        double nextLegalDistance = pitOpen ? toNextEntry :
            Math.Min(window.Meters, Math.Max(toNextEntry, window.YellowMeters));
        double fuelToNextStall = window.FuelDistance(nextLegalDistance) * fuelPerMeter + serviceBurn;
        bool emergencyFuel = !pitOpen && fuel < Math.Min(finishFuel, fuelToNextStall);
        if (window.EndsUnderCaution && fuel >= finishFuel && flats == 0) return default;

        bool underYellowAtEntry = toEntry < window.YellowMeters;
        bool needsFuel = finishFuel + reserve > fuel &&
            (underYellowAtEntry || lastBeforeClosure || fuel <= fuelToNextStall + reserve);
        bool canReachStop = toEntry < window.Meters && (pitOpen || emergencyFuel);
        if (!canReachStop) return default;

        if (window.EndsUnderCaution) needsFuel = fuel < finishFuel;
        double entryFuel = window.FuelDistance(toEntry) * fuelPerMeter;
        double fuelTarget = needsFuel ? Math.Min(SessionFuel.TankCapacity, finishFuel - entryFuel + reserve) : 0d;
        double predictedFuelAtStall = Math.Max(0d, fuel - serviceBurn - entryFuel);
        float gallons = needsFuel ? (float)Math.Max(0d, fuelTarget - predictedFuelAtStall) : 0f;
        float maxWear = window.FinalStage ? .70f : .50f;
        int tires = DecideTires(left.Finish, right.Finish, maxWear, gallons) | flats;

        if (!needsFuel && flats == 0)
        {
            if (tires == 0) return default;
            bool atRiskOfFlat = Math.Max(left.NextEntry, right.NextEntry) >= .98f;
            double greenLaps = Math.Max(0d, window.Meters - Math.Max(toEntry, window.YellowMeters)) / lapLength;
            float gain = tires == AllTires ? (left.Current + right.Current) * .5f : right.Current * .6f;
            float service = SessionPitStops.ServiceDuration(RaceSessionRun.TireCount(tires), 0f, 0f);
            // Worn tires cost pace, but the gain must cover the lost track position.
            double benefitSeconds = greenLaps * lapSeconds * gain * .08d;
            double costSeconds = underYellowAtEntry ? service * .2d : service + greenTransitLoss;
            if (!atRiskOfFlat && (greenLaps < (underYellowAtEntry ? 1d : 3d) || benefitSeconds < costSeconds))
                return default;
        }

        return new RivalPitPlan(fuelTarget, tires, 0f, emergencyFuel);
    }

    private static int DecideTires(float leftWear, float rightWear, float threshold, float gallons)
    {
        if (gallons > 0f && SessionPitStops.FuelServiceSeconds(gallons) >= SessionPitStops.ServiceDuration(4, 0f, 0f))
            return AllTires;
        if (leftWear > threshold) return AllTires;
        return rightWear > threshold ? RightTires : 0;
    }
}

// Initial game service estimates, shared by setup work and the pit-stop timer.
// Pressure adjustments use replacement tires and add no time beyond tire service.
internal struct SetupServicePlan
{
    internal float Seconds;
    internal bool Garage;
    internal static SetupServicePlan Between(Setup before, Setup after)
    {
        var plan = new SetupServicePlan();
        if (before == null || after == null) return plan;
        bool Changed(float a, float b) => Math.Abs(a-b) > .0001f;
        if (Changed(before.rearTrackBar.RightMountHeightAboveGroundInches, after.rearTrackBar.RightMountHeightAboveGroundInches)) plan.Seconds += 5f;
        if (Changed(before.weightDistribution.CrossPercent, after.weightDistribution.CrossPercent)) plan.Seconds += 5f;
        if (Changed(before.weightDistribution.FrontPercent, after.weightDistribution.FrontPercent) ||
            Changed(before.weightDistribution.LeftPercent, after.weightDistribution.LeftPercent))
        { plan.Seconds += 480f; plan.Garage = true; }
        if (Changed(before.frontSwayBar.RatePoundsPerInch, after.frontSwayBar.RatePoundsPerInch))
        { plan.Seconds += 360f; plan.Garage = true; }
        var a = new[]{before.leftFront,before.rightFront,before.leftRear,before.rightRear};
        var b = new[]{after.leftFront,after.rightFront,after.leftRear,after.rightRear};
        for (int i=0;i<4;i++)
        {
            if (Changed(a[i].spring.ratePoundsPerInch,b[i].spring.ratePoundsPerInch))
            { plan.Seconds += 300f; plan.Garage = true; }
            if (Changed(a[i].shock.compressionStiffness,b[i].shock.compressionStiffness) ||
                Changed(a[i].shock.reboundStiffness,b[i].shock.reboundStiffness))
            { plan.Seconds += 120f; plan.Garage = true; }
        }
        // Garage access/return allowance, in addition to the normal pit transit.
        if (plan.Garage) plan.Seconds += 60f;
        return plan;
    }
}
