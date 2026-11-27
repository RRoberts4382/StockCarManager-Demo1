using System.Collections.Generic;
using UnityEngine;

public sealed partial class TrackPreview
{
    // Matches RESPONSE_ROUTES in the Iowa Blender source. Each paved drive ends on the apron.
    private static readonly Vector2[][] IowaEmergencyDrives =
    {
        new Vector2[] { new(245f, 210f), new(228f, 190f), new(220f, 177f) },
        new Vector2[] { new(522f, 211f), new(531f, 196f), new(537f, 188f), new(547f, 183f) },
        new Vector2[] { new(223f, 326f), new(240f, 340f), new(258f, 365f) },
        new Vector2[] { new(490f, 337f), new(492f, 350f), new(498f, 365f) },
    };

    private readonly List<IowaResponseUnit> iowaResponseUnits = new List<IowaResponseUnit>(8);
    private readonly List<IowaWreck> iowaPendingWrecks = new List<IowaWreck>(CarCount);
    private readonly HashSet<int> iowaWreckCars = new HashSet<int>();
    private readonly List<IowaResponseCall> iowaResponseCalls = new List<IowaResponseCall>(4);
    private bool iowaAmbulanceDispatched;
    private int iowaNextGarageStall;

    private bool IowaWreckResponseInProgress => iowaResponseCalls.Count > 0 || iowaPendingWrecks.Count > 0 || IowaWreckStillRolling();

    private bool IowaWreckStillRolling()
    {
        if (iowaResponseUnits.Count == 0 || stages == null || trafficCars == null)
            return false;

        for (int i = 0; i < cars.Length; i++)
            if (crashes[i].Active && !trafficCars[i].InPit && !SessionRun.Retired(i))
                return true;
        return false;
    }

    private void RegisterIowaResponseVehicle(Transform vehicle, int gateIndex, bool ambulance)
    {
        var pixels = IowaEmergencyDrives[gateIndex - 1];
        var control = new Vector3[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
            control[i] = IowaLayoutPoint(pixels[i].x, pixels[i].y, vehicle.localPosition.y);

        Vector3 exit = control[control.Length - 1];
        float station = TrackUtilityGeometry.ClosestMainDistance(customLayout, lapLength, exit);
        iowaResponseUnits.Add(new IowaResponseUnit(vehicle, gateIndex, ambulance, control, station));
    }

    private void ResetIowaWreckResponse()
    {
        ResetIowaTowRecovery();
        foreach (var unit in iowaResponseUnits)
            unit.Park();
        iowaResponseCalls.Clear();
        iowaAmbulanceDispatched = false;
        authoredGateOwner = null;
        iowaNextGarageStall = 0;
        iowaPendingWrecks.Clear();
        iowaWreckCars.Clear();
    }

    private void QueueIowaWreckResponse(int carIndex)
    {
        if (iowaResponseUnits.Count == 0 || trafficCars?[carIndex] is not TrackPitTraffic.Car car || car.InPit ||
            iowaDeliveredWrecks[carIndex] ||
            !iowaWreckCars.Add(carIndex))
            return;

        iowaPendingWrecks.Add(new IowaWreck(carIndex, car.TrackDistance, crashes[carIndex].Lane));
    }

    private bool IsIowaWreckAwaitingRecovery(int carIndex) =>
        iowaWreckCars.Contains(carIndex) || iowaDeliveredWrecks[carIndex];

    private void AdvanceIowaWreckResponse(float seconds)
    {
        // Wait for this impact's rollout before assigning the nearby pickups in track order.
        bool stillRolling = IowaWreckStillRolling();
        while (iowaPendingWrecks.Count > 0 && !stillRolling)
        {
            int next = NextIowaPickup();
            IowaWreck incident = iowaPendingWrecks[next];
            var tow = NearestIowaTow(incident);
            // A blocked entrance need not keep another truck from responding to a reachable wreck.
            for (int i = 0; tow == null && i < iowaPendingWrecks.Count; i++)
            {
                if (i == next || IowaPickupHasWreckAhead(iowaPendingWrecks[i]))
                    continue;
                tow = NearestIowaTow(iowaPendingWrecks[i]);
                if (tow != null)
                {
                    next = i;
                    incident = iowaPendingWrecks[i];
                }
            }
            if (tow == null)
                break;

            iowaPendingWrecks.RemoveAt(next);
            IowaResponseUnit support = iowaAmbulanceDispatched ? null : IowaAmbulanceAtGate(tow.Unit.GateIndex);
            IowaResponseTrip ambulance = support == null ? null : BuildIowaResponseRoute(support, incident);
            iowaAmbulanceDispatched = true;
            iowaResponseCalls.Add(new IowaResponseCall(incident, ambulance, tow, iowaNextGarageStall++));
        }

        for (int i = 0; i < iowaResponseCalls.Count; i++)
        {
            var response = iowaResponseCalls[i];
            if (response.TowPhase == IowaTowPhase.Approach)
            {
                // A truck can pull out of its bay while it waits for the wreck immediately ahead of its own.
                const float stagingDistance = 2f;
                bool waitForLeadWreck = response.Tow.Traveled <= stagingDistance && IowaPickupHasWreckAhead(response.Incident);
                MoveIowaResponder(response.Tow, response, seconds,
                    stopAt: waitForLeadWreck ? stagingDistance : float.PositiveInfinity);
                if (response.Tow.AtWreck)
                    BeginIowaTowHookup(response);
            }
            if (response.TowPhase != IowaTowPhase.Approach)
                AdvanceIowaTowRecovery(response, seconds);

            AdvanceIowaAmbulance(response, seconds);
            if (response.TowParked && response.AmbulanceParked)
                iowaResponseCalls.RemoveAt(i--);
        }

        if (!IowaWreckResponseInProgress)
            iowaAmbulanceDispatched = false;
    }

    private int NextIowaPickup()
    {
        int lead = 0;
        for (int pass = 0; pass < iowaPendingWrecks.Count; pass++)
        {
            int previous = lead;
            for (int i = 0; i < iowaPendingWrecks.Count; i++)
                if (IowaPickupFollows(iowaPendingWrecks[lead], iowaPendingWrecks[i]))
                    lead = i;
            if (previous == lead)
                break;
        }
        return lead;
    }

    private bool IowaPickupHasWreckAhead(IowaWreck incident)
    {
        foreach (int index in iowaWreckCars)
        {
            var ahead = new IowaWreck(index, trafficCars[index].TrackDistance, crashes[index].Lane);
            if (!IowaPickupFollows(incident, ahead))
                continue;

            // An attached car still counts as an obstruction until it has left the pickup area.
            Vector3 stoppedAt = TrackPoint(ahead.Station, ahead.Lane, out _, out _);
            if (iowaRecoveredCars[index] is not Pose pose || (pose.position - stoppedAt).sqrMagnitude < 16f * 16f)
                return true;
        }
        return false;
    }

    private bool IowaPickupFollows(IowaWreck behind, IowaWreck ahead)
    {
        if (behind.CarIndex == ahead.CarIndex || Mathf.Abs(behind.Lane - ahead.Lane) >= 4f)
            return false;
        float offset = IowaPickupOffset(behind.Station, ahead.Station);
        return (offset > .05f && offset <= 16f) || (Mathf.Abs(offset) <= .05f && ahead.CarIndex < behind.CarIndex);
    }

    private float IowaPickupOffset(float from, float to) =>
        Mathf.Repeat(to - from + lapLength * .5f, lapLength) - lapLength * .5f;

    private IowaResponseTrip NearestIowaTow(IowaWreck incident)
    {
        IowaResponseTrip closest = null, bestClearance = null;
        float closestDistance = float.PositiveInfinity;
        float greatestClearance = float.NegativeInfinity;
        var obstructions = new List<(Vector3 Position, Vector3 Forward)>();
        foreach (int index in iowaWreckCars)
        {
            var car = new IowaWreck(index, trafficCars[index].TrackDistance, crashes[index].Lane);
            if (index != incident.CarIndex && !IowaPickupFollows(incident, car) && iowaRecoveredCars[index] == null)
                obstructions.Add((TrackPoint(car.Station, car.Lane, out _, out _), cars[index].localRotation * Vector3.forward));
        }
        foreach (var unit in iowaResponseUnits)
        {
            if (unit.Ambulance || iowaResponseCalls.Exists(call => !call.TowParked && call.Tow.Unit == unit))
                continue;

            float distance = unit.ApproachDistance + IowaResponseCourseDistance(unit, incident);
            if (distance >= closestDistance)
                continue;

            var route = BuildIowaResponseRoute(unit, incident);
            float clearance = float.PositiveInfinity;
            for (int i = 1; i < route.Route.Length; i++)
            {
                Vector3 point = route.Route[i];
                Vector3 forward = route.Route[Mathf.Min(i + 1, route.Route.Length - 1)] - route.Route[i - 1];
                foreach (var car in obstructions)
                    if ((point - car.Position).sqrMagnitude < 36f)
                        clearance = Mathf.Min(clearance, IowaPickupHullGap(car.Position - point, forward, car.Forward));
            }
            if (clearance >= 0f)
            {
                closest = route;
                closestDistance = distance;
            }
            else if (clearance > greatestClearance)
            {
                greatestClearance = clearance;
                bestClearance = route;
            }
        }
        return closest ?? (iowaResponseCalls.Count == 0 ? bestClearance : null);
    }

    private static float IowaPickupHullGap(Vector3 offset, Vector3 truckHeading, Vector3 carHeading)
    {
        Vector3 truck = Vector3.ProjectOnPlane(truckHeading, Vector3.up).normalized;
        Vector3 car = Vector3.ProjectOnPlane(carHeading, Vector3.up).normalized;
        Vector3 truckSide = Vector3.Cross(Vector3.up, truck);
        Vector3 carSide = Vector3.Cross(Vector3.up, car);
        return Mathf.Max(Mathf.Max(Gap(truck), Gap(truckSide)), Mathf.Max(Gap(car), Gap(carSide)));

        float Gap(Vector3 axis) => Mathf.Abs(Vector3.Dot(offset, axis)) -
            (3.3f * Mathf.Abs(Vector3.Dot(truck, axis)) + 1.3f * Mathf.Abs(Vector3.Dot(truckSide, axis)) +
            1.45f * Mathf.Abs(Vector3.Dot(car, axis)) + .8f * Mathf.Abs(Vector3.Dot(carSide, axis)));
    }

    private IowaResponseUnit IowaAmbulanceAtGate(int gate) =>
        iowaResponseUnits.Find(unit => unit.Ambulance && unit.GateIndex == gate);

    private float IowaResponseCourseDistance(IowaResponseUnit unit, IowaWreck incident)
    {
        // Keep the circuit one-way while multiple vehicles share the apron. Leave room to pass the stopped car.
        float distance = Mathf.Repeat(incident.Station - unit.ApronStation, lapLength);
        return distance < 18f ? distance + lapLength : distance;
    }

    private float IowaResponseRoadOffset(IowaResponseUnit unit) =>
        definition.id is NorthWilkesboroTrackId or DoverTrackId ? .016f : unit.Home.y;

    private IowaResponseTrip BuildIowaResponseRoute(IowaResponseUnit unit, IowaWreck incident)
    {
        var route = new List<Vector3> { unit.Home };
        var up = new List<Vector3> { Vector3.up };
        var road = unit.Access;
        var homeOffset = unit.Home - road[0];
        float accessDistance = 0f;
        Vector3 last = road[0];

        // Pull away from the parking bay while the drive is still wide, then join its centerline.
        for (int segment = 0; segment < road.Length - 1; segment++)
        {
            Vector3 b = road[segment], c = road[segment + 1];
            Vector3 a = segment > 0 ? road[segment - 1] : b * 2f - c;
            Vector3 d = segment + 2 < road.Length ? road[segment + 2] : c * 2f - b;
            int sampleCount = Mathf.CeilToInt(Vector3.Distance(b, c) / .6f);
            for (int sample = 1; sample <= sampleCount; sample++)
            {
                float t = sample / (float)sampleCount;
                Vector3 point = .5f * (2f * b + (c - a) * t + (2f * a - 5f * b + 4f * c - d) * (t * t) +
                    (-a + 3f * b - 3f * c + d) * (t * t * t));
                accessDistance += Vector3.Distance(last, point);
                last = point;
                Add(point + homeOffset * Mathf.SmoothStep(1f, 0f, accessDistance / 12f), Vector3.up);
            }
        }

        float station = unit.ApronStation;
        float delta = IowaResponseCourseDistance(unit, incident);
        const float apronJoin = 12f;
        float towStop = delta + IowaTowVehicleSpacing;
        float finish = unit.Ambulance ? Mathf.Max(apronJoin + 1f, towStop - 11.7f) : towStop;
        Vector3 start = road[road.Length - 1];
        float joinStation = station + apronJoin;
        float roadOffset = IowaResponseRoadOffset(unit);
        Vector3 join = TrackPoint(joinStation, ApronLane(joinStation), out var trackForward, out _);
        if (definition.id is NorthWilkesboroTrackId or DoverTrackId)
            join.y += roadOffset;
        Vector3 firstTangent = (start - road[road.Length - 2]).normalized * 6f;
        Vector3 lastTangent = trackForward * 13f;
        int accessEnd = route.Count - 1;
        for (int sample = 1; sample <= 15; sample++)
        {
            float t = sample / 15f;
            float t2 = t * t;
            float t3 = t2 * t;
            Add((2f * t3 - 3f * t2 + 1f) * start + (t3 - 2f * t2 + t) * firstTangent +
                (-2f * t3 + 3f * t2) * join + (t3 - t2) * lastTangent, Vector3.up);
        }
        int apronStart = route.Count - 1;

        int courseSamples = Mathf.Max(1, Mathf.CeilToInt((finish - apronJoin) / 1.25f));
        float laneChange = Mathf.Max(0f, Mathf.Min(28f, finish - apronJoin) - 3f) * .55f;
        float inside = customLayout.InsideSign;
        const float passingClearance = 3.55f;
        float insidePass = incident.Lane * inside + passingClearance;
        float outsidePass = incident.Lane * inside - passingClearance;
        float earliestWreck = 0f;
        foreach (int other in iowaWreckCars)
        {
            float offset = IowaPickupOffset(incident.Station, trafficCars[other].TrackDistance);
            if (other == incident.CarIndex || offset < -48f || offset > 16f || iowaRecoveredCars[other] != null)
                continue;

            float lane = crashes[other].Lane * inside;
            insidePass = Mathf.Max(insidePass, lane + passingClearance);
            outsidePass = Mathf.Min(outsidePass, lane - passingClearance);
            earliestWreck = Mathf.Min(earliestWreck, offset);
        }
        Vector2 pickupWidth = RoadWidths(incident.Station);
        float insidePassingLimit = pickupWidth.x * .5f +
            LimitRichmondApronOffset(incident.Station, pickupWidth.y - 1.6f);
        bool goOutside = insidePass > insidePassingLimit;
        float passingLane = goOutside ? outsidePass : insidePass;
        float passStart = Mathf.Max(apronJoin, delta + earliestWreck - 50f);
        float passReady = Mathf.Max(passStart + 1f, delta + earliestWreck - 9f);
        float bypassLength = Mathf.Max(0f, delta - apronJoin - 9f);
        var bypasses = GetIowaApronBypasses(joinStation, bypassLength, incident.CarIndex);
        float finalLane = 0f;
        for (int sample = 1; sample <= courseSamples; sample++)
        {
            float along = Mathf.Lerp(apronJoin, finish, sample / (float)courseSamples);
            float sampleStation = station + along;
            Vector2 widths = RoadWidths(sampleStation);
            float apronLane = ApronLane(sampleStation);
            float halfWidth = widths.x * .5f;
            float insidePickupLimit = halfWidth + LimitRichmondApronOffset(sampleStation, widths.y - 2.55f);
            float insidePassLimit = halfWidth + LimitRichmondApronOffset(sampleStation, widths.y - 1.6f);
            float safeLane = inside * Mathf.Clamp(incident.Lane * inside, -halfWidth + 2.55f, insidePickupLimit);
            // Pass the whole cluster before putting the rear lift in front of this car.
            float passLane = inside * Mathf.Clamp(passingLane, -halfWidth + 1.6f, insidePassLimit);
            float lane = Mathf.SmoothStep(apronLane, passLane, Mathf.InverseLerp(passStart, passReady, along));
            lane = apronLane + IowaApronBypassOffset(bypasses, along - apronJoin, bypassLength, (lane - apronLane) * inside) * inside;
            if (!unit.Ambulance && along > delta + 3f)
                lane = Mathf.SmoothStep(passLane, Mathf.MoveTowards(apronLane, safeLane, laneChange),
                    Mathf.InverseLerp(delta + 3f, finish, along));
            finalLane = lane;
            Add(TrackPoint(sampleStation, lane, out _, out var surfaceUp) + Vector3.up * roadOffset, surfaceUp);
        }

        return new IowaResponseTrip(unit, route.ToArray(), up.ToArray(), accessEnd, apronStart)
        {
            CourseStation = station + finish,
            CourseLane = finalLane
        };

        void Add(Vector3 point, Vector3 normal)
        {
            route.Add(point);
            up.Add(normal);
        }
    }

    private void MoveIowaResponder(IowaResponseTrip trip, IowaResponseCall response, float seconds,
        float maximumSpeed = 15f, float trailingSpacing = 0f, float stopAt = float.PositiveInfinity, bool returningHome = false)
    {
        stopAt = LimitAuthoredGateTravel(trip, response, returningHome, stopAt);
        float remaining = Mathf.Min(trip.Length, stopAt) - trip.Traveled;
        if (remaining <= 0f)
            return;

        float speedLimit = maximumSpeed;
        if (trip.Traveled < trip.AccessLength + 11f)
            speedLimit = Mathf.Min(speedLimit, 7f);
        float goalSpeed = Mathf.Min(speedLimit, Mathf.Sqrt(2f * 6f * remaining));
        trip.Speed = Mathf.MoveTowards(trip.Speed, goalSpeed, 7f * seconds);
        float travel = Mathf.Min(remaining, trip.Speed * seconds);
        float next = trip.Traveled + travel;
        Vector3 position = trip.Sample(next, out var orient, out var surfaceUp);
        Vector3 current = trip.Unit.Vehicle.localPosition;

        bool blocked = IowaOtherResponderAhead(trip.Unit, response, current, position) ||
            trip.Traveled > trip.AccessLength && IowaResponderMustYield(current, position) ||
            returningHome && IowaStoppedWreckBlocksReturn(current, position, orient);
        if (trailingSpacing > 0f && !blocked)
        {
            Vector3 load = trip.Sample(Mathf.Max(0f, trip.Traveled - trailingSpacing), out _, out _);
            Vector3 nextLoad = trip.Sample(Mathf.Max(0f, next - trailingSpacing), out _, out _);
            blocked = IowaResponderMustYield(load, nextLoad) || IowaOtherResponderAhead(trip.Unit, response, load, nextLoad);
        }
        if (travel > 0f && blocked)
        {
            trip.Speed = 0f;
            return;
        }

        trip.Traveled = next;
        trip.Unit.Vehicle.SetLocalPositionAndRotation(position, Quaternion.LookRotation(orient, surfaceUp));
        ReleaseAuthoredGate(trip, returningHome);
    }

    private bool IowaStoppedWreckBlocksReturn(Vector3 current, Vector3 next, Vector3 orientation)
    {
        Vector3 forward = Vector3.ProjectOnPlane(orientation, Vector3.up).normalized;
        foreach (int index in iowaWreckCars)
        {
            if (iowaRecoveredCars[index] != null)
                continue;

            Vector3 wreck = cars[index].localPosition;
            Vector3 offset = Vector3.ProjectOnPlane(wreck - next, Vector3.up);
            float ahead = Vector3.Dot(offset, forward);
            if (ahead > 0f && ahead < 5.3f && (offset - forward * ahead).sqrMagnitude < 3f * 3f &&
                MovingCloser(current, next, wreck, 6.2f))
                return true;
        }
        return false;
    }

    private bool IowaOtherResponderAhead(IowaResponseUnit unit, IowaResponseCall response, Vector3 current, Vector3 next)
    {
        Vector3 direction = Vector3.ProjectOnPlane(next - current, Vector3.up).normalized;
        if (direction == Vector3.zero)
            return false;

        foreach (var other in iowaResponseCalls)
        {
            if (!other.TowParked && other.Tow.Unit != unit && other.Tow.Traveled > 0f &&
                Ahead(other.Tow.Unit.Vehicle.localPosition, 7.4f, 2.75f))
                return true;
            if (!other.AmbulanceParked && other.Ambulance.Unit != unit && other.Ambulance.Traveled > 0f &&
                Ahead(other.Ambulance.Unit.Vehicle.localPosition, 7.4f, 2.75f))
                return true;
            if (other != response && (other.TowPhase == IowaTowPhase.Hookup || other.TowPhase == IowaTowPhase.Hauling ||
                other.TowPhase == IowaTowPhase.Unhooking) &&
                iowaRecoveredCars[other.Incident.CarIndex] is Pose wreck && Ahead(wreck.position, 6f, 2.5f))
                return true;
        }
        return false;

        bool Ahead(Vector3 obstacle, float length, float width)
        {
            Vector3 offset = Vector3.ProjectOnPlane(obstacle - next, Vector3.up);
            float forward = Vector3.Dot(offset, direction);
            return forward > 0f && forward < length && (offset - direction * forward).sqrMagnitude < width * width;
        }
    }

    private bool IowaResponderMustYield(Vector3 current, Vector3 next)
    {
        for (int index = 0; index < cars.Length; index++)
        {
            if (!cars[index].gameObject.activeSelf || SessionRun.Retired(index) ||
                SessionRun.CurrentSpeed(index) < 1f)
                continue;

            if (MovingCloser(current, next, cars[index].localPosition, 5.8f))
                return true;
        }
        return false;
    }

    private static bool MovingCloser(Vector3 current, Vector3 next, Vector3 obstacle, float radius)
    {
        float distance = (next - obstacle).sqrMagnitude;
        return distance < radius * radius && distance < (current - obstacle).sqrMagnitude;
    }

    private readonly struct IowaWreck
    {
        internal readonly int CarIndex;
        internal readonly float Station, Lane;

        internal IowaWreck(int carIndex, float station, float lane)
        {
            CarIndex = carIndex;
            Station = station;
            Lane = lane;
        }
    }

    private sealed class IowaResponseUnit
    {
        internal readonly Transform Vehicle;
        internal readonly int GateIndex;
        internal readonly bool Ambulance;
        internal readonly Vector3 Home;
        internal readonly Quaternion Facing;
        internal readonly Vector3[] Access;
        internal readonly float ApronStation;
        internal readonly float ApproachDistance;
        internal Transform Coupling;

        internal IowaResponseUnit(Transform vehicle, int gateIndex, bool ambulance, Vector3[] access, float station)
        {
            Vehicle = vehicle;
            GateIndex = gateIndex;
            Ambulance = ambulance;
            Access = access;
            ApronStation = station;
            Home = vehicle.localPosition;
            Facing = vehicle.localRotation;
            for (int i = 1; i < access.Length; i++)
                ApproachDistance += Vector3.Distance(access[i - 1], access[i]);
        }

        internal void Park() => Vehicle.SetLocalPositionAndRotation(Home, Facing);
    }

    private sealed class IowaResponseTrip
    {
        internal readonly IowaResponseUnit Unit;
        internal readonly Vector3[] Route;
        private readonly Vector3[] up;
        private readonly float[] stations;
        private int segment;
        internal readonly float AccessLength, ApronDistance;
        internal readonly int AccessEndIndex;
        internal float Traveled, Speed;
        internal float CourseStation, CourseLane;
        internal float Length => stations[stations.Length - 1];
        internal float FirstSegmentLength => stations[1];
        internal bool AtWreck => Traveled >= Length;

        internal IowaResponseTrip(IowaResponseUnit unit, Vector3[] route, Vector3[] surfaceUp, int accessEnd, int apronStart)
        {
            Unit = unit;
            Route = route;
            up = surfaceUp;
            AccessEndIndex = accessEnd;
            stations = new float[route.Length];
            for (int i = 1; i < route.Length; i++)
                stations[i] = stations[i - 1] + Vector3.Distance(route[i - 1], route[i]);
            AccessLength = stations[accessEnd];
            ApronDistance = stations[apronStart];
        }

        internal Vector3 Sample(float distance, out Vector3 orient, out Vector3 upDirection)
        {
            while (segment < stations.Length - 2 && stations[segment + 1] < distance)
                segment++;
            while (segment > 0 && stations[segment] > distance)
                segment--;

            float fraction = Mathf.InverseLerp(stations[segment], stations[segment + 1], distance);
            Vector3 behind = segment == 0 ? Route[1] - Route[0] : Route[segment + 1] - Route[segment - 1];
            Vector3 ahead = segment == stations.Length - 2 ? Route[segment + 1] - Route[segment] :
                Route[segment + 2] - Route[segment];
            orient = Vector3.Lerp(behind.normalized, ahead.normalized, fraction);
            upDirection = Vector3.Lerp(up[segment], up[segment + 1], fraction);
            return Vector3.Lerp(Route[segment], Route[segment + 1], fraction);
        }
    }

    private sealed class IowaResponseCall
    {
        internal readonly IowaWreck Incident;
        internal readonly IowaResponseTrip Ambulance, Tow;
        internal readonly int GarageStall;
        internal IowaResponseTrip LoadedTow, EmptyTow, AmbulanceReturn;
        internal IowaTowPhase TowPhase;
        internal float LoadedYardAt, EmptyCourseAt, EmptyHomeAt;
        internal float AmbulanceHomeAt;
        internal Transform Coupling;
        internal bool TowParked;
        internal bool AmbulanceParked => Ambulance == null || (AmbulanceReturn != null && AmbulanceReturn.AtWreck);
        internal Vector3 InitialWreckPosition;
        internal Quaternion InitialWreckRotation;
        internal float ServiceTime;

        internal IowaResponseCall(IowaWreck incident, IowaResponseTrip ambulance, IowaResponseTrip tow, int garageStall)
        {
            Incident = incident;
            Ambulance = ambulance;
            Tow = tow;
            GarageStall = garageStall;
        }
    }
}
