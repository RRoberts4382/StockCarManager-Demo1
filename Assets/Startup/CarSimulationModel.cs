using System;
using System.Collections.Generic;
using System.Linq;

internal interface ICarSimulationModel
{
    string Id { get; }
    string Name { get; }
}

internal sealed class CarSimulationContext
{
    public TrackDefinition Track { get; }
    public ScheduledRace Event { get; }
    public RaceSession Session { get; }
    public IReadOnlyList<SessionCarResult> Cars { get; }
    public bool IsInstant { get; }
    public bool PreservePlayerCondition { get; }
    public Setup PlayerSetup { get; set; }
    public bool EqualConditionTesting { get; set; }
    internal int TireWearRate { get; set; } = 1;
    private Setup[] rivalSetups;

    internal void ResetLiveTrackConditions()
    {
        PrepareEqualConditions();
        RaceSessionRun.ResetVehiclesForSession(Cars, PreservePlayerCondition);
    }

    internal void PrepareRivalSetups(bool includesPlayer = true)
    {
        if (rivalSetups != null) return;
        bool locking = Session.kind != RaceSessionKind.Practice || Event?.SetupLocked == true;
        if (locking) EqualConditionTesting = false;
        if (EqualConditionTesting) return;
        rivalSetups = new Setup[Cars.Count];
        var ovalBaselines = VehicleSpeed003.UsesNonPlateOvalSetupTuning(Track) ? new List<Setup>() : null;
        for (int i = 0; i < Cars.Count; i++)
        {
            var car = Cars[i].car;
            if (car == null) continue;
            bool player = includesPlayer && i == 0;
            var saved = Event?.EventSetup(car);
            bool baseline = false;
            var setup = player ? PlayerSetup ?? car.SetupsFor(Track.id).LastOrDefault()?.setup ?? car.setup
                : saved ?? RivalSetup001.ForTrack(car, Track, out baseline);
            if (Event?.SetupLocked == true && saved != null) setup = saved;
            if (player)
                PlayerSetup = locking ? RivalSetup001.Copy(setup) : setup;
            else
            {
                rivalSetups[i] = RivalSetup001.Copy(setup);
                if (baseline && ovalBaselines != null)
                    ovalBaselines.Add(rivalSetups[i]);
            }
        }

        if (ovalBaselines != null)
            RivalSetup001.WidenOvalBaselines(ovalBaselines);
        if (locking && Event != null)
        {
            for (int i = 0; i < Cars.Count; i++)
            {
                var car = Cars[i].car;
                if (car != null && (!Event.SetupLocked || Event.EventSetup(car) == null))
                    Event.CaptureEventSetup(car, includesPlayer && i == 0 ? PlayerSetup : rivalSetups[i]);
            }
            Event.setupLocked = true;
        }
    }
    internal Setup RivalSetup(int index) => rivalSetups?[index];

    internal void PrepareEqualConditions()
    {
        if (!EqualConditionTesting || Session.kind != RaceSessionKind.Practice || Cars.Count == 0 || Cars[0].car == null)
        {
            EqualConditionTesting = false;
            return;
        }
        var reference = Cars[0].car;
        PlayerSetup = UnityEngine.JsonUtility.FromJson<Setup>(UnityEngine.JsonUtility.ToJson(PlayerSetup ?? reference.setup ?? new Setup()));
        for (int i = 0; i < Cars.Count; i++)
        {
            var original = Cars[i].car;
            if (i != 0)
            {
                // Clone rivals before applying test values; saved team cars stay intact.
                var copy = UnityEngine.JsonUtility.FromJson<TeamCar>(UnityEngine.JsonUtility.ToJson(original ?? reference));
                foreach (var component in TeamCar.Components)
                {
                    copy.ComponentLevel(component) = reference.ComponentLevel(component);
                    copy.ComponentWear(component) = reference.ComponentWear(component);
                }
                copy.setup = UnityEngine.JsonUtility.FromJson<Setup>(UnityEngine.JsonUtility.ToJson(PlayerSetup));
                Cars[i].car = copy;
            }
            Cars[i].car.ResetTrackCondition();
            Cars[i].Fuel = SessionFuel.TankCapacity;
        }
    }

    internal static SessionCarResult[] LimitField(TrackDefinition track, SessionCarResult[] cars)
    {
        int count = track?.SessionFieldSize(cars.Length) ?? cars.Length;
        return count == cars.Length ? cars : cars.Take(count).ToArray();
    }

    public CarSimulationContext(TrackDefinition track, ScheduledRace scheduledEvent, RaceSession session,
        SessionCarResult[] cars, bool isInstant = false, bool preservePlayerCondition = false)
    {
        Track = track;
        Event = scheduledEvent;
        Session = session;
        Cars = LimitField(track, cars);
        IsInstant = isInstant;
        PreservePlayerCondition = preservePlayerCondition;
    }
}

internal static class CarSimulationModels
{
    public static IReadOnlyList<ICarSimulationModel> Available { get; } =
        Array.AsReadOnly(new ICarSimulationModel[] { new BasicSimulation(), new TrackState001() });

    public static ICarSimulationModel Default => Available[1];

    public static int IndexForId(string id)
    {
        for (int i = 0; i < Available.Count; i++)
        {
            if (Available[i].Id == id)
                return i;
        }

        return 1;
    }
}
