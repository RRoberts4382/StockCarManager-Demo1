using System;

internal sealed class SessionPitStops
{
    private const float RepairSecondsPerPoint = 0.5f;
    private readonly TrackPitTraffic pit;
    private readonly TrackPitTraffic.Car[] cars;
    private readonly float[] stalls;
    private readonly Stop[] stops;
    private bool holdPlayerInGarage;

    private struct Stop
    {
        public bool Requested;
        public bool Refuel;
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
    }

    public bool CanRequest(RaceSessionRun run) =>
        !holdPlayerInGarage && (run.IsRace ? run.RemainingRaceMeters > 0 : run.Remaining > 0) &&
        !run.Finished(0) && !run.Retired(0) && !stops[0].Requested && !stops[0].Servicing && run.Fuel(0) > 0;

    public void SetGarageVisit(bool visiting)
    {
        holdPlayerInGarage = visiting;
        if (!visiting && !stops[0].Requested)
            cars[0].NextEntry = float.PositiveInfinity;
    }

    public string PlayerStatus { get; private set; } = string.Empty;

    public bool NeedsFlatTireService(int index) => stops[index].FlatTireStop;

    public bool IsRequested(int index) => stops[index].Requested;

    public bool IsServicing(int index) => stops[index].Servicing;

    public void RequestPlayer(RaceSessionRun run, float fuelGallons, int tireMask, float repairPoints, float[] pressures = null)
    {
        if (!CanRequest(run))
            return;

        stops[0].Requested = true;
        stops[0].FuelGallons = fuelGallons;
        stops[0].TireMask = tireMask;
        stops[0].Pressures = !run.SetupLocked && pressures?.Length == 4 ? (float[])pressures.Clone() : null;
        stops[0].RepairPoints = repairPoints;
        PlayerStatus = "Pit stop queued";
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
        float fuelSeconds = gallons > 0f ? 7f + 2f * (float)(gallons / SessionFuel.TankCapacity) : 0f;
        return Math.Max(tireSeconds, fuelSeconds) + repairPoints * RepairSecondsPerPoint;
    }

    public void Advance(int index, RaceSessionRun run, float seconds, float speed, bool pitOpen, bool allowEmergencyEntry = true)
    {
        var car = cars[index];
        ref var stop = ref stops[index];
        bool garageVisit = index == 0 && holdPlayerInGarage;
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

        float distanceToEntry = pit.DistanceToEntry(car);
        double remaining = run.RemainingRaceMeters;
        if (run.IsRace && index != 0 && !stop.Requested && !stop.Servicing && !car.InPit &&
            remaining > distanceToEntry && run.Fuel(index) < run.FuelForDistance(remaining) &&
            run.Fuel(index) <= run.FuelForDistance(distanceToEntry + run.LapLength) + 0.5)
        {
            stop.Requested = true;
            stop.Refuel = true;
        }

        if ((stop.Requested || garageVisit) && !car.InPit)
        {
            if (pitOpen || (stop.FlatTireStop && allowEmergencyEntry))
                pit.CallToStall(car, stalls[index]);
            else
                car.NextEntry = float.PositiveInfinity;

            if (index == 0)
                PlayerStatus = stop.FlatTireStop && (pitOpen || allowEmergencyEntry) ? "Flat tire · pitting" :
                    pitOpen ? "Pit stop queued" : "Queued · pits closed";
        }

        double before = car.Odometer;
        if (!car.InGarage)
            pit.Advance(car, seconds, speed, stop.Requested || garageVisit ? stalls[index] : float.PositiveInfinity);
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
                    double fuel = run.Fuel(index);
                    double requestedFuel = stop.Refuel
                        ? run.FuelForDistance(run.RemainingRaceMeters) + 1.0 - fuel
                        : stop.FuelGallons;
                    stop.FuelGallons = (float)Math.Max(0, Math.Min(requestedFuel, SessionFuel.TankCapacity - fuel));
                    stop.RepairPoints = Math.Max(0f, Math.Min(stop.RepairPoints, vehicle?.Damage ?? 0f));

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
                    run.AddFuel(index, stop.FuelGallons);
                    var vehicle = run.Vehicle(index);
                    if (vehicle != null)
                    {
                        for (int tire = 0; tire < 4; tire++)
                        {
                            if ((stop.TireMask & (1 << tire)) != 0 && vehicle.GetTire(tire) is CarTire current)
                            {
                                current.wear = 0f;
                                current.temperature = 0f;
                                current.coldPressurePsi = stop.Pressures?.Length == 4
                                    ? UnityEngine.Mathf.Clamp(stop.Pressures[tire], 10f, 60f)
                                    : VehicleSpeed003.ColdPressure(run.SetupForCar(index), tire);
                            }
                        }
                        vehicle.Damage -= stop.RepairPoints;
                    }
                    stop = default;
                    if (index == 0)
                        PlayerStatus = "Pit stop complete";
                }
                else if (index == 0)
                {
                    PlayerStatus = $"Service · {stop.Duration.Value - stop.Time:F1}s";
                }
            }
        }
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
