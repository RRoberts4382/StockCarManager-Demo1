using System.Collections.Generic;
using UnityEngine;

public sealed partial class TrackPreview
{
    private enum IowaTowPhase { Approach, Hookup, Hauling, Unhooking, Returning }

    private const float IowaTowVehicleSpacing = 6.25f;
    private const float IowaTowHookupSeconds = 1.8f;
    private const float IowaTowUnhookSeconds = 1.15f;
    private readonly Pose?[] iowaRecoveredCars = new Pose?[CarCount];
    private readonly bool[] iowaDeliveredWrecks = new bool[CarCount];
    private IowaResponseCall iowaTowGarageOwner;

    private void ResetIowaTowRecovery()
    {
        System.Array.Clear(iowaRecoveredCars, 0, iowaRecoveredCars.Length);
        System.Array.Clear(iowaDeliveredWrecks, 0, iowaDeliveredWrecks.Length);
        iowaTowGarageOwner = null;
        foreach (var unit in iowaResponseUnits)
            if (unit.Coupling != null)
                unit.Coupling.gameObject.SetActive(false);
    }

    private bool PositionIowaRecoveredCar(int index)
    {
        if (iowaRecoveredCars[index] is not Pose pose)
            return false;

        cars[index].SetLocalPositionAndRotation(pose.position, pose.rotation);
        return true;
    }

    private void BeginIowaTowHookup(IowaResponseCall response)
    {
        BuildIowaTowDeliveryTrips(response);
        response.TowPhase = IowaTowPhase.Hookup;
        response.ServiceTime = 0f;
        int wreckIndex = response.Incident.CarIndex;
        Transform wreck = cars[wreckIndex];
        wreck.gameObject.SetActive(true);
        response.InitialWreckPosition = wreck.localPosition;
        response.InitialWreckRotation = wreck.localRotation;
        iowaRecoveredCars[wreckIndex] = new Pose(wreck.localPosition, wreck.localRotation);
        IowaResponseUnit tow = response.Tow.Unit;
        if (tow.Coupling == null)
        {
            var metal = MakeMaterial(new Color32(169, 153, 92, 255));
            tow.Coupling = Box($"Iowa gate {tow.GateIndex} tow coupling", transform, Vector3.zero, Vector3.one, metal).transform;
        }
        response.Coupling = tow.Coupling;
        response.Coupling.gameObject.SetActive(true);
    }

    private void AdvanceIowaTowRecovery(IowaResponseCall response, float seconds)
    {
        response.ServiceTime += seconds;
        switch (response.TowPhase)
        {
            case IowaTowPhase.Hookup:
                float hookup = Mathf.Clamp01(response.ServiceTime / IowaTowHookupSeconds);
                PoseIowaTowLoad(response, hookup);
                if (hookup < 1f)
                    return;

                response.TowPhase = IowaTowPhase.Hauling;
                break;

            case IowaTowPhase.Hauling:
                bool atYard = response.LoadedTow.Traveled >= response.LoadedYardAt - .001f;
                if (atYard && iowaTowGarageOwner == null)
                    iowaTowGarageOwner = response;
                // A single truck uses the garage aisles at a time. Others stop on the paved apron before the turnoff.
                float stopAt = iowaTowGarageOwner == response ? float.PositiveInfinity : response.LoadedYardAt;
                MoveIowaResponder(response.LoadedTow, response, seconds, maximumSpeed: atYard ? 7f : 12f,
                    trailingSpacing: IowaTowVehicleSpacing, stopAt: stopAt);
                PoseIowaTowLoad(response, 1f);
                if (response.LoadedTow.AtWreck)
                {
                    response.TowPhase = IowaTowPhase.Unhooking;
                    response.ServiceTime = 0f;
                }
                break;

            case IowaTowPhase.Unhooking:
                float unhook = Mathf.Clamp01(response.ServiceTime / IowaTowUnhookSeconds);
                PoseIowaTowLoad(response, 1f, 1f - unhook);
                if (unhook < 1f)
                    break;

                iowaDeliveredWrecks[response.Incident.CarIndex] = true;
                iowaWreckCars.Remove(response.Incident.CarIndex);
                response.Coupling.gameObject.SetActive(false);
                response.TowPhase = IowaTowPhase.Returning;
                break;

            case IowaTowPhase.Returning:
                if (!response.TowParked)
                    DriveIowaEmptyTowHome(response, seconds);
                break;
        }
    }

    private void DriveIowaEmptyTowHome(IowaResponseCall response, float seconds)
    {
        var empty = response.EmptyTow;
        float speed = empty.Traveled >= response.EmptyCourseAt && empty.Traveled < response.EmptyHomeAt ? 15f : 7f;
        MoveIowaResponder(empty, response, seconds, maximumSpeed: speed, returningHome: true);
        FinishIowaResponderParking(empty);
        response.TowParked = empty.AtWreck;
        if (iowaTowGarageOwner == response && (response.TowParked || empty.Traveled >= response.EmptyCourseAt + 10f))
            iowaTowGarageOwner = null;
    }

    private void AdvanceIowaAmbulance(IowaResponseCall response, float seconds)
    {
        if (response.AmbulanceParked || response.Tow.Traveled < response.Tow.ApronDistance)
            return;

        var arrival = response.Ambulance;
        if (!arrival.AtWreck)
        {
            MoveIowaResponder(arrival, response, seconds);
            return;
        }

        if (response.LoadedTow == null || response.LoadedTow.Traveled < response.LoadedTow.FirstSegmentLength + 12f)
            return;

        if (response.AmbulanceReturn == null)
        {
            var route = new IowaTowPolyline();
            route.Add(arrival.Unit.Vehicle.localPosition, Vector3.up);
            response.AmbulanceHomeAt = AppendIowaHomeApproach(route, arrival, arrival.CourseStation, arrival.CourseLane);
            response.AmbulanceReturn = route.Trip(arrival.Unit);
        }
        var trip = response.AmbulanceReturn;
        MoveIowaResponder(trip, response, seconds, maximumSpeed: trip.Traveled < response.AmbulanceHomeAt ? 15f : 7f,
            returningHome: true);
        FinishIowaResponderParking(trip);
    }

    private static void FinishIowaResponderParking(IowaResponseTrip trip)
    {
        if (trip.AtWreck)
        {
            trip.Unit.Park();
            return;
        }

        float backing = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(13f, 1f, trip.Length - trip.Traveled));
        Transform vehicle = trip.Unit.Vehicle;
        vehicle.localRotation = Quaternion.Slerp(vehicle.localRotation, trip.Unit.Facing, backing);
    }

    private void PoseIowaTowLoad(IowaResponseCall response, float hookup, float attached = 1f)
    {
        var path = response.LoadedTow;
        Vector3 load = path.Sample(Mathf.Max(0f, path.Traveled - IowaTowVehicleSpacing), out var heading, out var up);
        Quaternion rotation = Quaternion.LookRotation(heading, up);
        float lifted = .12f * attached;
        float align = Mathf.SmoothStep(0f, 1f, hookup);
        Vector3 wreckPosition = Vector3.Lerp(response.InitialWreckPosition, load + up * lifted, align);
        Quaternion wreckRotation = Quaternion.Slerp(response.InitialWreckRotation,
            rotation * Quaternion.Euler(-3.2f * attached, 0f, 0f), align);
        iowaRecoveredCars[response.Incident.CarIndex] = new Pose(wreckPosition, wreckRotation);

        Transform tow = path.Unit.Vehicle;
        Vector3 truckHitch = tow.localPosition + tow.localRotation * new Vector3(0f, .65f, -3.08f);
        Vector3 wreckHitch = wreckPosition + wreckRotation * new Vector3(0f, .30f, 1.35f);
        Vector3 tip = Vector3.Lerp(truckHitch, wreckHitch, hookup * attached);
        Vector3 span = tip - truckHitch;
        Quaternion line = span.sqrMagnitude > .00001f ? Quaternion.FromToRotation(Vector3.up, span) : Quaternion.identity;
        response.Coupling.SetLocalPositionAndRotation((truckHitch + tip) * .5f, line);
        response.Coupling.localScale = new Vector3(.09f, span.magnitude, .09f);
    }

    private void BuildIowaTowDeliveryTrips(IowaResponseCall response)
    {
        if (phoenixResponseOutbound != null)
        {
            BuildAuthoredTowDeliveryTrips(response);
            return;
        }
        IowaResponseTrip pickup = response.Tow;
        IowaResponseUnit unit = pickup.Unit;
        GetIowaTowGarageYard(unit.Home.y, response.GarageStall,
            out int entryGate, out Vector3[] delivery, out int exitGate, out Vector3[] departure);
        IowaResponseUnit gateIn = IowaTowAtGate(entryGate);
        IowaResponseUnit gateOut = IowaTowAtGate(exitGate);
        var entry = IowaTowGarageTransition(entryGate);
        var exit = IowaTowGarageTransition(exitGate);

        var loaded = new IowaTowPolyline();
        float rearStation = pickup.CourseStation - IowaTowVehicleSpacing;
        Vector3 behind = TrackPoint(rearStation, pickup.CourseLane, out _, out var behindUp) + Vector3.up * unit.Home.y;
        loaded.Add(behind, behindUp);
        loaded.Add(unit.Vehicle.localPosition, unit.Vehicle.localRotation * Vector3.up);
        float entryStation = gateIn.ApronStation - entry.Lead;
        bool needsLaneChange = Mathf.Abs(pickup.CourseLane - ApronLane(pickup.CourseStation)) > .3f;
        AppendIowaTowCourse(loaded, pickup.CourseStation, entryStation, pickup.CourseLane, unit.Home.y,
            minimumRunUp: needsLaneChange ? 25f : 0f, ignoredWreck: response.Incident.CarIndex);
        response.LoadedYardAt = loaded.Length;
        TrackPoint(entryStation, ApronLane(entryStation), out var entryHeading, out _);
        loaded.Join(delivery[0], entryHeading * entry.ApronTangent,
            (delivery[1] - delivery[0]).normalized * entry.RoadTangent);
        for (int i = 1; i < delivery.Length; i++)
            loaded.Add(delivery[i], Vector3.up);

        response.LoadedTow = loaded.Trip(unit);
        response.LoadedTow.Traveled = response.LoadedTow.FirstSegmentLength;

        var empty = new IowaTowPolyline();
        // If the garage exit is the truck's original access road, stop at its bay without rejoining the oval.
        if (exitGate == unit.GateIndex)
        {
            response.EmptyCourseAt = float.PositiveInfinity;
            int closest = 1;
            for (int i = 2; i < departure.Length; i++)
                if ((departure[i] - unit.Access[0]).sqrMagnitude < (departure[closest] - unit.Access[0]).sqrMagnitude)
                    closest = i;
            for (int i = 0; i <= closest; i++)
                empty.Add(departure[i], Vector3.up);
            empty.Add(unit.Home, Vector3.up);
        }
        else
        {
            foreach (Vector3 point in departure)
                empty.Add(point, Vector3.up);

            float start = gateOut.ApronStation + exit.Lead;
            Vector3 apron = TrackPoint(start, ApronLane(start), out var outbound, out _) + Vector3.up * unit.Home.y;
            Vector3 exitTangent = (departure[^1] - departure[^2]).normalized * exit.RoadTangent;
            empty.Join(apron, exitTangent, outbound * exit.ApronTangent);
            response.EmptyCourseAt = empty.Length;
            response.EmptyHomeAt = AppendIowaHomeApproach(empty, pickup, start, ApronLane(start));
        }
        response.EmptyTow = empty.Trip(unit);
    }

    private float AppendIowaHomeApproach(IowaTowPolyline route, IowaResponseTrip pickup, float start, float lane)
    {
        var unit = pickup.Unit;
        var turn = IowaTowHomeTransition(unit.GateIndex, unit.Ambulance);
        float homeStation = unit.ApronStation - turn.Lead;
        AppendIowaTowCourse(route, start, homeStation, lane, IowaResponseRoadOffset(unit));
        float turnAt = route.Length;
        TrackPoint(homeStation, ApronLane(homeStation), out var heading, out _);
        Vector3 mouth = pickup.Route[pickup.AccessEndIndex];
        int join = pickup.AccessEndIndex - 1;
        while (join > 1 && Mathf.Abs(Vector3.Distance(pickup.Route[join - 1], mouth) - turn.Inset)
            < Mathf.Abs(Vector3.Distance(pickup.Route[join], mouth) - turn.Inset))
            join--;
        Vector3 inward = (pickup.Route[join - 1] - pickup.Route[join + 1]).normalized;
        route.Join(pickup.Route[join], heading * turn.ApronTangent, inward * turn.RoadTangent);
        for (int i = join - 1; i >= 0; i--)
            route.Add(pickup.Route[i], Vector3.up);
        return turnAt;
    }

    private IowaResponseUnit IowaTowAtGate(int gate)
    {
        foreach (var unit in iowaResponseUnits)
            if (!unit.Ambulance && unit.GateIndex == gate)
                return unit;
        throw new System.InvalidOperationException($"Iowa recovery access gate {gate} is missing.");
    }

    private void AppendIowaTowCourse(IowaTowPolyline route, float start, float end, float firstLane,
        float height, float minimumRunUp = 0f, int ignoredWreck = -1)
    {
        float distance = Mathf.Repeat(end - start, lapLength);
        if (distance < minimumRunUp)
            distance += lapLength;

        int samples = Mathf.Max(1, Mathf.CeilToInt(distance));
        float laneOffset = firstLane - ApronLane(start);
        float mergeLength = Mathf.Clamp(distance, .01f, 25f);
        float inside = customLayout.InsideSign;
        var bypasses = GetIowaApronBypasses(start, distance, ignoredWreck);
        for (int i = 1; i <= samples; i++)
        {
            float traveled = distance * i / samples;
            float station = start + traveled;
            float nominal = Mathf.SmoothStep(laneOffset, 0f, traveled / mergeLength) * inside;
            float lane = ApronLane(station) + IowaApronBypassOffset(bypasses, traveled, distance, nominal) * inside;
            Vector3 point = TrackPoint(station, lane, out _, out var up) + Vector3.up * height;
            route.Add(point, up);
        }
    }

    private List<(float At, float Offset)> GetIowaApronBypasses(float start, float distance, int ignoredWreck)
    {
        const float clearance = 4.25f;
        float inside = customLayout.InsideSign;
        var bypasses = new List<(float At, float Offset)>();
        foreach (int index in iowaWreckCars)
        {
            if (index == ignoredWreck || iowaRecoveredCars[index] != null)
                continue;

            float station = trafficCars[index].TrackDistance;
            float along = Mathf.Repeat(station - start, lapLength);
            float separation = (ApronLane(station) - crashes[index].Lane) * inside;
            if (along < distance && Mathf.Abs(separation) < clearance)
                bypasses.Add((along, clearance + separation));
        }
        return bypasses;
    }

    private static float IowaApronBypassOffset(List<(float At, float Offset)> bypasses, float traveled, float distance, float nominal)
    {
        float needed = 0f;
        foreach (var obstacle in bypasses)
        {
            float blend = Mathf.InverseLerp(8f, 30f, Mathf.Abs(traveled - obstacle.At));
            needed = Mathf.Max(needed, Mathf.SmoothStep(obstacle.Offset, 0f, blend));
        }
        // Preserve the pickup and gate joins while moving onto the racing surface around a blocked apron.
        float room = Mathf.SmoothStep(0f, 1f, Mathf.Min(traveled, distance - traveled) / 12f);
        return nominal - Mathf.Max(0f, needed * room - Mathf.Max(0f, -nominal));
    }

    private sealed class IowaTowPolyline
    {
        private readonly List<Vector3> points = new();
        private readonly List<Vector3> up = new();
        internal float Length { get; private set; }

        internal void Add(Vector3 point, Vector3 normal)
        {
            if (points.Count > 0 && (point - points[points.Count - 1]).sqrMagnitude < .00001f)
                return;
            if (points.Count > 0)
                Length += Vector3.Distance(points[points.Count - 1], point);
            points.Add(point);
            up.Add(normal);
        }

        internal void Join(Vector3 end, Vector3 fromTangent, Vector3 toTangent)
        {
            Vector3 start = points[points.Count - 1];
            for (int i = 1; i <= 24; i++)
            {
                Add(PitHermite(start, end, fromTangent, toTangent, i / 24f), Vector3.up);
            }
        }

        internal IowaResponseTrip Trip(IowaResponseUnit unit) => new(unit, points.ToArray(), up.ToArray(), 0, 0);
    }
}
