using System;

internal sealed class TrackState001 : ICarSimulationModel
{
    public TrackState001Practice StartPractice(TrackPitTraffic traffic, TrackPitTraffic.Car[] cars, float[] stalls) =>
        new TrackState001Practice(traffic, cars, stalls, new Random());

    public TrackState001Qualifying StartQualifying(TrackPitTraffic traffic, float stall) =>
        new TrackState001Qualifying(traffic, stall);

    public TrackState001RaceStart StartRace(TrackPitTraffic pit, int[] slots, TrackDefinition track, Random random)
    {
        var zone = RestartZone(track);
        return new TrackState001RaceStart(pit, slots, zone.Finish, zone.Length, zone.Start, zone.End, random);
    }

    public TrackState001Stages StartStages(TrackPitTraffic pit, int count, TrackDefinition track, Random random)
    {
        var zone = RestartZone(track);
        return new TrackState001Stages(pit, zone.Length, zone.Finish, zone.Start, zone.End, count, random,
            widenRestartFormationFirst: track.id is TrackPreview.RichmondTrackId or TrackPreview.NorthWilkesboroTrackId);
    }

    private static (float Length, float Finish, float Start, float End) RestartZone(TrackDefinition track)
    {
        float length = track.lengthMiles * TrackDefinition.MetersPerMile;
        float finish = track.StartFinishFraction;
        var markers = track.raceMarkers;
        float zoneStart = markers?.hasRestartZone == true ? UnityEngine.Mathf.Repeat(markers.restartZoneStart - finish, 1f) : 0.90f;
        float zoneEnd = markers?.hasRestartZone == true ? UnityEngine.Mathf.Repeat(markers.restartZoneEnd - finish, 1f) : 0.98f;
        return (length, finish * length, zoneStart * length, zoneEnd * length);
    }

    public string Id => "vehicle-speed-001";
    public string Name => "TrackState001";
}
