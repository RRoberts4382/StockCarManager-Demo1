using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class TrackPreview
{
    private enum HaulerGaragePhase { OnPitRoute, Arriving, BackingIn, Parked, Leaving }

    private HaulerGaragePhase haulerGaragePhase;
    private PaceBayRoute haulerGarageApproach, haulerGarageReverse, haulerGarageDeparture;
    private Vector3 haulerGarageAccess, haulerGarageBay, haulerAisleForward, haulerBayInward;
    private Vector3[] haulerGarageIngress;
    private float haulerGarageEntry, haulerGarageExit, haulerGarageTravel, haulerGarageYieldAt;

    internal bool UsesNorthWilkesboroHaulerGarage => haulerGarageIngress != null;
    private bool NorthWilkesboroGarageOffPitRoute => haulerGaragePhase != HaulerGaragePhase.OnPitRoute;
    private bool NorthWilkesboroParkedForGarage => haulerGaragePhase == HaulerGaragePhase.Parked;
    private float? NorthWilkesboroGarageTarget => UsesNorthWilkesboroHaulerGarage ? haulerGarageEntry : null;

    private void ConfigureNorthWilkesboroHaulerGarage()
    {
        haulerGarageIngress = null;
        if (definition.id != NorthWilkesboroTrackId || !importedTrackGeometry)
            return;

        const string prefix = "North Wilkesboro hauler garage ";
        var markers = GetComponentsInChildren<Transform>(true);
        var ingress = ReadPath("ingress ");
        var egress = ReadPath("egress ");
        // The player team occupies the first hauler stall, regardless of the player's car number.
        var access = markers.FirstOrDefault(item => item.name == prefix + "access 01");
        var bay = markers.FirstOrDefault(item => item.name == prefix + "bay 01");
        for (int i = 0; i < markers.Length; i++)
            if (markers[i].name.StartsWith(prefix, StringComparison.Ordinal))
                HideAuthoredVenueMarker(markers[i]);

        if (ingress.Length < 2 || egress.Length < 2 || access == null || bay == null)
        {
            Debug.LogWarning("North Wilkesboro's first hauler bay or garage access routes are missing.", this);
            return;
        }

        var pitCar = new TrackPitTraffic.Car { InPit = true };
        haulerGarageEntry = FindLaneStation(ingress[0], out Vector3 entry);
        haulerGarageExit = FindLaneStation(egress[^1], out Vector3 exit);
        if (Vector3.Distance(ingress[0], entry) > 4f || Vector3.Distance(egress[^1], exit) > 4f ||
            haulerGarageExit < haulerGarageEntry - 1f)
        {
            Debug.LogWarning("North Wilkesboro's garage routes must connect to the native pit lane in driving order.", this);
            return;
        }

        BlendLaneHandoff(egress, exit, reverse: true);
        haulerGarageAccess = Local(access);
        haulerGarageBay = Local(bay);
        haulerAisleForward = Vector3.ProjectOnPlane(haulerGarageAccess - ingress[^1], Vector3.up).normalized;
        haulerBayInward = Vector3.ProjectOnPlane(haulerGarageBay - haulerGarageAccess, Vector3.up).normalized;
        const float turnRadius = 3.5f;
        var path = new List<Vector3> { haulerGarageBay };
        var departureTurn = haulerGarageAccess + haulerBayInward * turnRadius;
        AddQuarterTurn(path, departureTurn, -haulerBayInward, -haulerAisleForward);
        path.AddRange(egress.Take(egress.Length - 1));
        path.Add(exit);
        haulerGarageDeparture = new PaceBayRoute(path);
        // Wait outside the moving lane, even when the final authored points are on the merge.
        haulerGarageYieldAt = haulerGarageDeparture.Length;
        for (int i = path.Count - 2; i > 1; i--)
        {
            haulerGarageYieldAt -= Vector3.Distance(path[i], path[i + 1]);
            float laneClearance = float.PositiveInfinity;
            for (float meters = Mathf.Max(pitTraffic.RoadStart, haulerGarageExit - 30f);
                meters < Mathf.Min(pitTraffic.RouteLength, haulerGarageExit + 30f); meters += .5f)
                laneClearance = Mathf.Min(laneClearance, Vector3.Distance(path[i],
                    SampleTrafficPosition(pitCar, meters, pitTraffic.ThroughLaneAt(meters))));
            if (laneClearance >= 4f)
                break;
        }
        var reverse = new List<Vector3>();
        var reverseStart = haulerGarageAccess + (haulerAisleForward - haulerBayInward) * turnRadius;
        AddQuarterTurn(reverse, reverseStart, -haulerAisleForward, haulerBayInward);
        reverse.Add(haulerGarageBay);
        haulerGarageReverse = new PaceBayRoute(reverse);
        haulerGarageIngress = ingress;

        Vector3 Local(Transform marker) => transform.InverseTransformPoint(marker.position);

        void AddQuarterTurn(List<Vector3> output, Vector3 origin, Vector3 first, Vector3 second)
        {
            for (int i = 0; i <= 12; i++)
            {
                float angle = Mathf.PI * .5f * i / 12f;
                output.Add(origin + first * (turnRadius * Mathf.Sin(angle)) +
                    second * (turnRadius * (1f - Mathf.Cos(angle))));
            }
        }

        Vector3[] ReadPath(string label) => markers
            .Where(item => item.name.StartsWith(prefix + label, StringComparison.Ordinal))
            .OrderBy(item => item.name, StringComparer.Ordinal).Select(Local).ToArray();

        float FindLaneStation(Vector3 marker, out Vector3 nearest)
        {
            float best = float.PositiveInfinity;
            float station = 0f;
            nearest = Vector3.zero;
            for (float meters = pitTraffic.RoadStart + 4f; meters < pitTraffic.RouteLength - 4f; meters += .25f)
            {
                Vector3 point = SampleTrafficPosition(pitCar, meters, pitTraffic.ThroughLaneAt(meters));
                float error = (point - marker).sqrMagnitude;
                if (error >= best) continue;
                best = error;
                station = meters;
                nearest = point;
            }
            return station;
        }
    }

    private static void BlendLaneHandoff(Vector3[] points, Vector3 laneCenter, bool reverse)
    {
        if (reverse)
            Array.Reverse(points);

        const float blendLength = 14f;
        Vector3 offset = laneCenter - points[0];
        Vector3 previous = points[0];
        float distance = 0f;
        for (int i = 0; i < points.Length && distance < blendLength; i++)
        {
            Vector3 original = points[i];
            distance += Vector3.Distance(previous, original);
            points[i] += offset * Mathf.SmoothStep(1f, 0f, distance / blendLength);
            previous = original;
        }
        if (reverse)
            Array.Reverse(points);
    }

    private bool BeginNorthWilkesboroGarageDeparture()
    {
        if (haulerGaragePhase == HaulerGaragePhase.Leaving)
            return true;
        if (!NorthWilkesboroParkedForGarage)
            return false;

        haulerGaragePhase = HaulerGaragePhase.Leaving;
        haulerGarageTravel = 0f;
        return true;
    }

    private void AdvanceNorthWilkesboroGarageTravel(float seconds)
    {
        if (seconds <= 0f || !UsesNorthWilkesboroHaulerGarage || !playerGarageVisit ||
            raceStart != null && !raceReleased)
            return;

        var player = qualifying?.Car ?? trafficCars?[0];
        switch (haulerGaragePhase)
        {
            case HaulerGaragePhase.OnPitRoute:
                if (player?.InPit != true || player.InGarage || SessionRun.Retired(0) ||
                    player.PitDistance != haulerGarageEntry)
                    return;
                var ingress = (Vector3[])haulerGarageIngress.Clone();
                BlendLaneHandoff(ingress, SampleTrafficPosition(player, player.PitDistance, player.PitLane), reverse: false);
                var path = new List<Vector3>(ingress);
                Vector3 reverseStart = haulerGarageReverse.At(0f);
                Vector3 blendStart = Vector3.Lerp(path[^1], haulerGarageAccess, .3f);
                float approachLength = Vector3.Dot(reverseStart - blendStart, haulerAisleForward);
                Vector3 laneShift = reverseStart - blendStart - haulerAisleForward * approachLength;
                for (int i = 0; i <= 12; i++)
                {
                    float fraction = i / 12f;
                    path.Add(blendStart + haulerAisleForward * (fraction * approachLength) +
                        laneShift * Mathf.SmoothStep(0f, 1f, fraction));
                }
                haulerGarageApproach = new PaceBayRoute(path);
                player.InGarage = true;
                player.ActualPitSpeed = 0f;
                haulerGaragePhase = HaulerGaragePhase.Arriving;
                haulerGarageTravel = 0f;
                break;
            case HaulerGaragePhase.Arriving:
                if (Travel(haulerGarageApproach, 6f))
                {
                    haulerGaragePhase = HaulerGaragePhase.BackingIn;
                    haulerGarageTravel = 0f;
                }
                break;
            case HaulerGaragePhase.BackingIn:
                if (Travel(haulerGarageReverse, 1.5f))
                    haulerGaragePhase = HaulerGaragePhase.Parked;
                break;
            case HaulerGaragePhase.Leaving:
                if (haulerGarageTravel <= haulerGarageYieldAt && !NorthWilkesboroPitHandoffClear())
                {
                    Travel(haulerGarageDeparture, 5f, haulerGarageYieldAt);
                    return;
                }
                if (Travel(haulerGarageDeparture, 5f))
                {
                    player.PitDistance = haulerGarageExit;
                    player.PitLane = pitTraffic.ThroughLaneAt(haulerGarageExit);
                    player.PitYaw = player.ActualPitSpeed = 0f;
                    player.Maneuver = player.InGarage = false;
                    carHeadings[0] = default;
                    haulerGaragePhase = HaulerGaragePhase.OnPitRoute;
                    SetPlayerGarageVisit(false);
                }
                break;
            case HaulerGaragePhase.Parked:
                SessionRun.RecordTravel(0, 0d, 0d, player.PreciseTrackDistance, reducedBurn: true, onTrack: false);
                break;
        }

        bool Travel(PaceBayRoute route, float maximumSpeed, float? stopAt = null)
        {
            float target = stopAt ?? route.Length;
            float remaining = target - haulerGarageTravel;
            float speed = Mathf.Min(maximumSpeed, Mathf.Sqrt(2f * 2.5f * remaining));
            float traveled = Mathf.Min(remaining, Mathf.Max(.25f, speed) * seconds);
            haulerGarageTravel += traveled;
            player.Odometer += traveled;
            SessionRun.RecordTravel(0, traveled, traveled / seconds, player.PreciseTrackDistance,
                reducedBurn: true, onTrack: false);

            // Score the pit bypass continuously while the car uses a parallel paddock lane.
            // A whole start/finish crossing would otherwise be skipped at the return handoff.
            float distanceAlongYard = haulerGarageTravel;
            if (haulerGaragePhase != HaulerGaragePhase.Arriving)
                distanceAlongYard += haulerGarageApproach.Length;
            if (haulerGaragePhase == HaulerGaragePhase.Leaving)
                distanceAlongYard += haulerGarageReverse.Length;
            float routeLength = haulerGarageApproach.Length + haulerGarageReverse.Length + haulerGarageDeparture.Length;
            double previous = pitTraffic.RaceDistance(player);
            player.PitDistance = Mathf.Lerp(haulerGarageEntry, haulerGarageExit, distanceAlongYard / routeLength);
            if (qualifying == null)
                SessionRun.Record(0, previous, pitTraffic.RaceDistance(player), seconds);
            return haulerGarageTravel >= target;
        }
    }

    private bool NorthWilkesboroPitHandoffClear()
    {
        if (qualifying != null || trafficCars == null)
            return true;

        float mergeTime = (haulerGarageDeparture.Length - haulerGarageYieldAt) / 5f;
        float upstreamGap = Mathf.Max(12f, pitTraffic.SpeedLimit * (mergeTime + 1.2f));
        for (int i = 1; i < trafficCars.Length; i++)
        {
            var other = trafficCars[i];
            if (!other.InPit || other.InGarage)
                continue;
            float gap = other.PitDistance - haulerGarageExit;
            if (gap > -upstreamGap && gap < 8f &&
                Mathf.Abs(other.PitLane - pitTraffic.ThroughLaneAt(other.PitDistance)) < 2.1f)
                return false;
        }
        return true;
    }

    private bool PositionNorthWilkesboroGarageCar(bool previewCollisionPose)
    {
        if (!NorthWilkesboroGarageOffPitRoute)
            return false;

        PaceBayRoute route = haulerGaragePhase switch
        {
            HaulerGaragePhase.Arriving => haulerGarageApproach,
            HaulerGaragePhase.Leaving => haulerGarageDeparture,
            _ => haulerGarageReverse
        };
        var car = cars[0];
        car.gameObject.SetActive(true);
        Vector3 tangent = route.At(haulerGarageTravel + 1.5f) - route.At(haulerGarageTravel - 1.5f);
        bool reversing = haulerGaragePhase is HaulerGaragePhase.BackingIn or HaulerGaragePhase.Parked;
        car.SetLocalPositionAndRotation(route.At(haulerGarageTravel),
            Quaternion.LookRotation(reversing ? -tangent : tangent, Vector3.up));
        if (!previewCollisionPose)
            raceWheels[0]?.Sample(car, false);
        return true;
    }
}
