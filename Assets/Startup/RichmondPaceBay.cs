using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class TrackPreview
{
    private enum RichmondPacePhase { Parked, Departing, Pacing, Returning }

    private RichmondPacePhase richmondPacePhase;
    private PaceBayRoute richmondPaceReturn, richmondInitialDeparture, richmondCautionDeparture;
    private PaceBayRoute richmondPaceDeparture;
    private Vector3 richmondPaceBay;
    private Quaternion richmondPaceBayHeading;
    private float richmondPaceTravel;
    private float richmondPaceJoinAfterExit;
    private float richmondPaceDriveIntoYardAt;

    private bool RichmondPaceIsParked => richmondPaceReturn != null && richmondPacePhase == RichmondPacePhase.Parked;

    private void ConfigureRichmondPaceBay()
    {
        if (definition.id is not (RichmondTrackId or NorthWilkesboroTrackId) || !importedTrackGeometry)
            return;

        var markers = GetComponentsInChildren<Transform>(true);
        string prefix = VenueSceneryName + " pace ";
        var bay = markers.FirstOrDefault(item => item.name == prefix + "bay");
        var inbound = ReadPath(prefix + "return ");
        var outbound = ReadPath(prefix + "departure ");
        if (bay == null || inbound.Length < 2 || outbound.Length < 2)
        {
            Debug.LogWarning($"{VenueSceneryName}'s protected pace bay or its access route is missing.", this);
            return;
        }

        HideAuthoredVenueMarker(bay);
        richmondPaceBay = transform.InverseTransformPoint(bay.position);
        richmondPaceBayHeading = Quaternion.LookRotation(outbound[1] - outbound[0], Vector3.up);
        inbound[^1] = richmondPaceBay;
        outbound[0] = richmondPaceBay;

        // The rendered vehicle uses the authored gate paths. The race and
        // caution controllers continue to use their original pace distances.
        float pitExit = pitTraffic.ExitDistance;
        float gateIn = TrackUtilityGeometry.ClosestMainDistance(customLayout, lapLength, inbound[0]);
        float gateOut = TrackUtilityGeometry.ClosestMainDistance(customLayout, lapLength, outbound[^1]);
        float returnRun = Mathf.Repeat(gateIn - pitExit, lapLength);
        float departureRun = Mathf.Repeat(gateOut - pitExit, lapLength);
        richmondPaceJoinAfterExit = departureRun + 42f;

        var returning = new List<Vector3>();
        var pitCar = new TrackPitTraffic.Car { InPit = true };
        Sample(pitTraffic.ParkingDistance, pitTraffic.RouteLength,
            distance => SampleTrafficPosition(pitCar, distance, 0f), returning);
        float startApron = ApronLane(pitExit);
        float gateLane = LaneAt(inbound[0], gateIn);
        Sample(0f, returnRun, offset =>
            TrackPoint(pitExit + offset, Mathf.SmoothStep(startApron, gateLane, offset / returnRun), out _, out _),
            returning);
        returning[^1] = inbound[0];
        richmondPaceDriveIntoYardAt = new PaceBayRoute(returning).Length;
        returning.AddRange(inbound.Skip(1));
        richmondPaceReturn = new PaceBayRoute(returning);
        richmondInitialDeparture = DepartureRoute(outbound, departureRun, true);
        richmondCautionDeparture = DepartureRoute(outbound, departureRun, false);
        richmondPacePhase = RichmondPacePhase.Parked;
        richmondPaceTravel = 0f;

        PaceBayRoute DepartureRoute(Vector3[] markersOnPath, float gateDistance, bool formation)
        {
            var route = new List<Vector3>(markersOnPath);
            float gateOffset = LaneAt(route[^1], pitExit + gateDistance);
            Sample(gateDistance, richmondPaceJoinAfterExit, offset =>
            {
                float station = pitExit + offset;
                float target = formation ? FormationTrackLane(station, 0f) : 0f;
                float blend = Mathf.InverseLerp(gateDistance, richmondPaceJoinAfterExit, offset);
                return TrackPoint(station, Mathf.SmoothStep(gateOffset, target, blend), out _, out _);
            }, route, includeBeginning: false);
            return new PaceBayRoute(route);
        }

        float LaneAt(Vector3 point, float station)
        {
            Vector3 center = TrackPoint(station, 0f, out var forward, out _);
            Vector3 across = Vector3.Cross(Vector3.up, forward).normalized;
            return Vector3.Dot(point - center, across);
        }

        Vector3[] ReadPath(string prefix) => markers
            .Where(marker => marker.name.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(marker => marker.name, StringComparer.Ordinal)
            .Select(marker =>
            {
                HideAuthoredVenueMarker(marker);
                return transform.InverseTransformPoint(marker.position);
            }).ToArray();

        static void Sample(float start, float end, Func<float, Vector3> at, List<Vector3> output,
            bool includeBeginning = true)
        {
            int samples = Mathf.Max(1, Mathf.CeilToInt((end - start) / .75f));
            for (int i = includeBeginning ? 0 : 1; i <= samples; i++)
                output.Add(at(Mathf.Lerp(start, end, i / (float)samples)));
        }
    }

    private void AdvanceRichmondPaceBay()
    {
        if (richmondPaceReturn == null)
            return;

        bool stagePace = stages?.HasPaceCar == true;
        var virtualPace = stagePace ? stages.PacePosition() : raceStart.PacePosition();
        bool returning = stagePace ? stages.PaceReturning : raceStart.PaceReturning;
        float pitParking = pitTraffic.ParkingDistance;
        switch (richmondPacePhase)
        {
            case RichmondPacePhase.Parked:
                if (returning || raceStart.State == SessionTrackState.TrackClosed ||
                    virtualPace.OnPitRoad && virtualPace.Distance < pitParking)
                    return;
                richmondPaceDeparture = stagePace ? richmondCautionDeparture : richmondInitialDeparture;
                richmondPaceTravel = 0f;
                richmondPacePhase = RichmondPacePhase.Departing;
                break;
            case RichmondPacePhase.Departing:
                float exitTravel = virtualPace.OnPitRoad
                    ? virtualPace.Distance - pitTraffic.RouteLength
                    : Mathf.Repeat(virtualPace.Distance - pitTraffic.ExitDistance, lapLength);
                float virtualLength = pitTraffic.RouteLength - pitParking + richmondPaceJoinAfterExit;
                float target = Mathf.InverseLerp(0f, virtualLength,
                    exitTravel + pitTraffic.RouteLength - pitParking) * richmondPaceDeparture.Length;
                AdvanceRichmondPaceRoute(richmondPaceDeparture,
                    Mathf.Min(38f, Mathf.Max(0f, target - richmondPaceTravel) / SessionStep));
                if (richmondPaceTravel >= richmondPaceDeparture.Length)
                    richmondPacePhase = RichmondPacePhase.Pacing;
                break;
            case RichmondPacePhase.Pacing:
                if (returning && virtualPace.OnPitRoad && virtualPace.Distance >= pitParking)
                {
                    richmondPaceTravel = 0f;
                    richmondPacePhase = RichmondPacePhase.Returning;
                }
                break;
            case RichmondPacePhase.Returning:
                float yardBlend = Mathf.InverseLerp(richmondPaceDriveIntoYardAt - 16f,
                    richmondPaceDriveIntoYardAt, richmondPaceTravel);
                float speed = Mathf.SmoothStep(15f, 7f, yardBlend);
                speed *= Mathf.Lerp(.4f, 1f, Mathf.InverseLerp(0f, 7f, richmondPaceReturn.Length - richmondPaceTravel));
                AdvanceRichmondPaceRoute(richmondPaceReturn, speed);
                if (richmondPaceTravel >= richmondPaceReturn.Length)
                    richmondPacePhase = RichmondPacePhase.Parked;
                break;
        }
    }

    private void AdvanceRichmondPaceRoute(PaceBayRoute route, float speed)
    {
        if (stages?.HasPaceCar == true && speed > 0f)
        {
            float remaining = route.Length - richmondPaceTravel;
            float lookAhead = Mathf.Min(remaining, speed * speed / (2f * TrackPitTraffic.PitApproachDeceleration) + 2f);
            float requested = speed;
            float room = PacePathClearance(richmondPaceTravel, lookAhead, speed, (station, _) =>
                new Pose(route.At(station), Quaternion.LookRotation(route.At(station + .4f) - route.At(station - .4f))),
                out int blocker);
            if (room < lookAhead)
                speed = Mathf.Min(speed, TrackPitTraffic.ApproachLimit(0f, room, SessionStep));
            RecordPaceTrafficSample(requested, speed, 0f, room, lookAhead, blocker,
                blocker >= 0 ? "Pace bay route clearance" : "");
            cautionPaceTrafficSpeed = speed;
        }
        richmondPaceTravel = Mathf.Min(richmondPaceTravel + speed * SessionStep, route.Length);
    }

    private bool PositionRichmondPaceBay((bool OnPitRoad, float Distance, float ParkingBlend) virtualPace)
    {
        if (richmondPaceReturn == null)
            return false;

        switch (richmondPacePhase)
        {
            case RichmondPacePhase.Parked:
                paceCar.SetLocalPositionAndRotation(richmondPaceBay, richmondPaceBayHeading);
                if (SupportsIowaVenueLighting)
                    ApplyIowaVehicleLighting(paceCar, IowaRoadLightingLayer);
                break;
            case RichmondPacePhase.Departing:
                PositionRoute(richmondPaceDeparture);
                break;
            case RichmondPacePhase.Returning:
                PositionRoute(richmondPaceReturn);
                break;
            default:
                if (!virtualPace.OnPitRoad)
                    return PositionRichmondPacePitApproach(virtualPace.Distance);
                PositionPitCar(paceCar, virtualPace.Distance);
                break;
        }
        return true;

        void PositionRoute(PaceBayRoute route)
        {
            paceCar.localPosition = route.At(richmondPaceTravel);
            var ahead = route.At(richmondPaceTravel + .45f);
            var behind = route.At(richmondPaceTravel - .45f);
            paceCar.localRotation = Quaternion.LookRotation(ahead - behind, Vector3.up);
            if (SupportsIowaVenueLighting)
                ApplyIowaVehicleLighting(paceCar, IowaRoadLightingLayer | IowaPitLightingLayer | IowaRaceLightingLayer);
        }
    }

    private bool PositionRichmondPacePitApproach(float distance)
    {
        const float approachLength = 78f;
        float entry = stages?.HasPaceCar == true ? stages.PacePitEntry : raceStart.PacePitEntry;
        if (entry - distance > approachLength)
            return false;

        // Move from the racing lane onto the paved apron before the live
        // pace route switches to the pit connector at its existing station.
        float lane = ApproachLane(distance);
        float laneSlope = ApproachLane(distance + .5f) - ApproachLane(distance - .5f);
        PositionTrackCar(paceCar, distance, lane, laneSlope);
        return true;

        float ApproachLane(float station)
        {
            float original = raceReleased ? cautionPaceAvoidanceLane : FormationTrackLane(station, 0f);
            return Mathf.SmoothStep(original, ApronLane(station),
                Mathf.InverseLerp(entry - approachLength, entry, station));
        }
    }

    private sealed class PaceBayRoute
    {
        private readonly Vector3[] positions;
        private readonly float[] distances;

        internal PaceBayRoute(List<Vector3> points)
        {
            positions = points.ToArray();
            distances = new float[positions.Length];
            for (int i = 1; i < positions.Length; i++)
                distances[i] = distances[i - 1] + Vector3.Distance(positions[i - 1], positions[i]);
        }

        internal float Length => distances[^1];

        internal Vector3 At(float distance)
        {
            if (distance <= 0f)
                return positions[0];
            if (distance >= Length)
                return positions[^1];

            int next = Array.BinarySearch(distances, distance);
            if (next >= 0)
                return positions[next];
            next = ~next;
            return Vector3.Lerp(positions[next - 1], positions[next],
                Mathf.InverseLerp(distances[next - 1], distances[next], distance));
        }
    }
}