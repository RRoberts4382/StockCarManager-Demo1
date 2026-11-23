using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

internal static class EmployeeRaceProgress
{
    public static Action Apply(Career career, ScheduledRace race, TrackDefinition track)
    {
        var results = race.sessions?.Find(session => session.kind == RaceSessionKind.Race)?.results;
        if (track == null || results == null || results.Count == 0)
            return null;

        var weights = track.weights ?? new TrackWeights();
        var previous = new Dictionary<Employee, (float Aero, float Suspension, float Transmission, float Engine)>();
        var player = race.playerSkipped ? null : results.Find(result => result.carNumber == race.carNumber);
        if (player != null)
            TrainCrew(career.employees, player.position);

        var rivalFinishes = results.Where(result => result != player && result.position > 0)
            .ToLookup(result => result.teamName, result => result.position);
        foreach (var team in career.rivalEmployees ?? Enumerable.Empty<TeamEmployees>())
        {
            int bestFinish = rivalFinishes[team.teamName].DefaultIfEmpty(0).Min();
            TrainCrew(team.employees, bestFinish);
        }

        return () =>
        {
            foreach (var entry in previous)
            {
                var employee = entry.Key;
                (employee.bodywork, employee.suspensions, employee.transmissions, employee.engines) = entry.Value;
            }
        };

        void TrainCrew(List<Employee> crew, int position)
        {
            float bonus = DriverRaceProgress.FinishBonus(position, results.Count);
            if (crew == null || bonus <= 0f)
                return;

            float aeroGain = bonus * Mathf.Clamp01(weights.aerodynamics);
            float suspensionGain = bonus * Mathf.Clamp01(weights.suspension);
            float transmissionGain = bonus * Mathf.Clamp01(weights.transmission);
            float engineGain = bonus * Mathf.Clamp01(weights.horsepower);
            foreach (var employee in crew)
            {
                if (!previous.TryAdd(employee, (employee.bodywork, employee.suspensions, employee.transmissions, employee.engines)))
                    continue;
                employee.bodywork = Mathf.Clamp01(employee.bodywork + aeroGain);
                employee.suspensions = Mathf.Clamp01(employee.suspensions + suspensionGain);
                employee.transmissions = Mathf.Clamp01(employee.transmissions + transmissionGain);
                employee.engines = Mathf.Clamp01(employee.engines + engineGain);
            }
        }
    }
}
