using System;
using System.Collections.Generic;

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

    public CarSimulationContext(TrackDefinition track, ScheduledRace scheduledEvent, RaceSession session,
        SessionCarResult[] cars, bool isInstant = false)
    {
        Track = track;
        Event = scheduledEvent;
        Session = session;
        Cars = cars;
        IsInstant = isInstant;
    }
}

internal static class CarSimulationModels
{
    public static IReadOnlyList<ICarSimulationModel> Available { get; } =
        Array.AsReadOnly(new ICarSimulationModel[] { new BasicSimulation(), new TrackState001() });

    public static ICarSimulationModel Default => Available[0];

    public static int IndexForId(string id)
    {
        for (int i = 0; i < Available.Count; i++)
        {
            if (Available[i].Id == id)
                return i;
        }

        return 0;
    }
}
