using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class TrackPreview
{
    private Vector3[] phoenixResponseOutbound;
    private Vector3[] phoenixRecoveryEntry;
    private Vector3[] phoenixRecoveryExit;
    private AuthoredRecoveryBayPose[] phoenixRecoveryBays;
    private float phoenixApronArrival;
    private float phoenixApronDeparture;
    private IowaResponseTrip authoredGateOwner;

    private float LimitAuthoredGateTravel(IowaResponseTrip trip, IowaResponseCall response, bool returningHome, float stopAt)
    {
        if (phoenixResponseOutbound == null || (!returningHome && trip.AccessLength <= 0f))
            return stopAt;

        // Both directions share the outbound road. Queue a returning
        // vehicle on the apron before its turn, while departing units wait in their bays.
        float entry = returningHome
            ? Mathf.Max(0f, (trip == response.EmptyTow ? response.EmptyHomeAt : response.AmbulanceHomeAt) - 12f)
            : 0f;
        float leave = AuthoredGateEnd(trip, returningHome);
        if (trip.Traveled >= leave) return stopAt;
        if (!returningHome && stopAt < leave) return Mathf.Min(stopAt, entry);
        if (trip.Traveled < entry || (authoredGateOwner != null && authoredGateOwner != trip))
            return Mathf.Min(stopAt, entry);

        authoredGateOwner = trip;
        return stopAt;
    }

    private void ReleaseAuthoredGate(IowaResponseTrip trip, bool returningHome)
    {
        if (authoredGateOwner == trip && trip.Traveled >= AuthoredGateEnd(trip, returningHome))
            authoredGateOwner = null;
    }

    private static float AuthoredGateEnd(IowaResponseTrip trip, bool returningHome) =>
        returningHome ? trip.Length : Mathf.Min(trip.Length, trip.ApronDistance + 10f);

    private readonly struct AuthoredRecoveryBayPose
    {
        internal readonly Vector3 Approach, Drop, Depart;
        internal AuthoredRecoveryBayPose(Vector3 approach, Vector3 drop, Vector3 depart)
        {
            Approach = approach;
            Drop = drop;
            Depart = depart;
        }
    }

    private void ReadAuthoredRecoveryRoutes(string venue, Transform[] markers)
    {
        Vector3[] Read(string kind)
        {
            string prefix = venue + kind;
            var sorted = new SortedDictionary<int, Vector3>();
            foreach (var marker in markers)
            {
                if (!marker.name.StartsWith(prefix, StringComparison.Ordinal) ||
                    !int.TryParse(marker.name.Substring(prefix.Length), out int index)) continue;
                sorted[index] = transform.InverseTransformPoint(marker.position);
                HideAuthoredVenueMarker(marker);
            }
            var points = new Vector3[sorted.Count];
            sorted.Values.CopyTo(points, 0);
            return points;
        }

        Vector3[] outbound = Read(" response outbound ");
        Vector3[] entry = Read(" recovery entry ");
        Vector3[] exit = Read(" recovery exit ");
        Vector3[] approach = Read(" recovery approach ");
        Vector3[] bays = Read(" recovery bay ");
        Vector3[] departure = Read(" recovery depart ");
        if (outbound.Length < 3 || entry.Length < 3 || exit.Length < 3 ||
            bays.Length < cars.Length || approach.Length != bays.Length || departure.Length != bays.Length)
            return;

        var recovery = new AuthoredRecoveryBayPose[bays.Length];
        for (int i = 0; i < recovery.Length; i++)
        {
            Vector3 entrance = bays[i] - approach[i];
            Vector3 leave = departure[i] - bays[i];
            if (entrance.sqrMagnitude < 4f || leave.sqrMagnitude < 64f ||
                Vector3.Dot(entrance.normalized, leave.normalized) < .98f)
                return;
            recovery[i] = new AuthoredRecoveryBayPose(approach[i], bays[i], departure[i]);
        }

        phoenixResponseOutbound = outbound;
        phoenixRecoveryEntry = entry;
        phoenixRecoveryExit = exit;
        phoenixRecoveryBays = recovery;
        phoenixApronArrival = TrackUtilityGeometry.ClosestMainDistance(customLayout, lapLength, entry[0]);
        phoenixApronDeparture = TrackUtilityGeometry.ClosestMainDistance(customLayout, lapLength, exit[^1]);
    }

    private void RegisterAuthoredResponseVehicle(Transform vehicle, bool ambulance)
    {
        if (phoenixResponseOutbound == null) return;
        float station = TrackUtilityGeometry.ClosestMainDistance(customLayout, lapLength, phoenixResponseOutbound[^1]);
        iowaResponseUnits.Add(new IowaResponseUnit(vehicle, 1, ambulance, phoenixResponseOutbound, station));
    }

    private void BuildAuthoredTowDeliveryTrips(IowaResponseCall response)
    {
        var pickup = response.Tow;
        var unit = pickup.Unit;
        var bay = phoenixRecoveryBays[response.GarageStall];
        float roadOffset = IowaResponseRoadOffset(unit);
        const float entranceLead = 12f;
        const float departureLead = 12f;
        float entryStation = phoenixApronArrival - entranceLead;
        float exitStation = phoenixApronDeparture + departureLead;
        Vector3 slotForward = (bay.Depart - bay.Drop).normalized;
        Vector3 stop = bay.Drop + slotForward * IowaTowVehicleSpacing;

        var loaded = new IowaTowPolyline();
        Vector3 tail = TrackPoint(pickup.CourseStation - IowaTowVehicleSpacing, pickup.CourseLane,
            out _, out var tailUp) + Vector3.up * roadOffset;
        loaded.Add(tail, tailUp);
        loaded.Add(unit.Vehicle.localPosition, unit.Vehicle.localRotation * Vector3.up);
        bool needsLaneChange = Mathf.Abs(pickup.CourseLane - ApronLane(pickup.CourseStation)) > .3f;
        AppendIowaTowCourse(loaded, pickup.CourseStation, entryStation, pickup.CourseLane, roadOffset,
            minimumRunUp: needsLaneChange ? 25f : 0f, ignoredWreck: response.Incident.CarIndex);
        response.LoadedYardAt = loaded.Length;
        TrackPoint(entryStation, ApronLane(entryStation), out var inbound, out _);
        var entry = phoenixRecoveryEntry;
        loaded.Join(entry[0], inbound * 13f, (entry[1] - entry[0]).normalized * 9f);
        AppendRoad(loaded, entry);
        float yardTurn = Mathf.Clamp(Vector3.Distance(entry[^1], bay.Approach) * .6f, 3f, 15f);
        loaded.Join(bay.Approach, (entry[^1] - entry[^2]).normalized * yardTurn, slotForward * yardTurn);
        loaded.Add(stop, Vector3.up);
        response.LoadedTow = loaded.Trip(unit);
        response.LoadedTow.Traveled = response.LoadedTow.FirstSegmentLength;

        var empty = new IowaTowPolyline();
        empty.Add(stop, Vector3.up);
        empty.Add(bay.Depart, Vector3.up);
        var exit = phoenixRecoveryExit;
        yardTurn = Mathf.Clamp(Vector3.Distance(bay.Depart, exit[0]) * .6f, 3f, 15f);
        empty.Join(exit[0], slotForward * yardTurn, (exit[1] - exit[0]).normalized * yardTurn);
        AppendRoad(empty, exit);
        Vector3 apron = TrackPoint(exitStation, ApronLane(exitStation), out var outbound, out _) + Vector3.up * roadOffset;
        empty.Join(apron, (exit[^1] - exit[^2]).normalized * 9f, outbound * 13f);
        response.EmptyCourseAt = empty.Length;
        response.EmptyHomeAt = AppendIowaHomeApproach(empty, pickup, exitStation, ApronLane(exitStation));
        response.EmptyTow = empty.Trip(unit);

        void AppendRoad(IowaTowPolyline trip, Vector3[] points)
        {
            if (definition.id == NorthWilkesboroTrackId)
            {
                // These markers already follow the sloped access pavement in three dimensions.
                for (int i = 1; i < points.Length; i++)
                    trip.Add(points[i], Vector3.up);
                return;
            }

            var spline = new TrackPitSpline(Array.ConvertAll(points, TrackPitSpline.Data));
            int samples = Mathf.Max(1, Mathf.CeilToInt(spline.Length / .6f));
            for (int i = 1; i <= samples; i++)
                trip.Add(spline.Sample(i / (float)samples) + Vector3.up * points[0].y, Vector3.up);
        }
    }
}

