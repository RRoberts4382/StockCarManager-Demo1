using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class TrackPreview
{
    private readonly List<Material> authoredVenueLampLenses = new();
    private string VenueSceneryName => definition.id switch
    {
        PhoenixTrackId => "Phoenix",
        RichmondTrackId => "Richmond",
        NorthWilkesboroTrackId => "North Wilkesboro",
        _ => "Iowa"
    };

    private void AddPhoenixVenue()
    {
        AddScoringPylonAtPitWalkway("Phoenix", 16.124f);
        AddAuthoredOvalVenue("Phoenix", PhoenixSafetySites(), 11909);
    }

    private void AddAuthoredOvalVenue(string venue, (string Name, float Station)[] safetySites, int fanSeed)
    {
        BuildIowaFlagArmRig(AddIowaFlagStandScenery());
        AddIowaSafetyLightFixtures(safetySites);
        string standPrefix = venue + " grandstand ";
        string benchPrefix = standPrefix + "bench ";
        var attendanceRoot = CreateObject(standPrefix + "attendance", transform);
        var attendance = attendanceRoot.AddComponent<GrandstandAttendance>();
        grandstandScenery = attendanceRoot;
        var paths = new Dictionary<string, (Transform Start, Transform End)>(StringComparer.Ordinal);
        var markers = GetComponentsInChildren<Transform>(true);
        ReadAuthoredRecoveryRoutes(venue, markers);
        foreach (var item in markers)
        {
            if (item.name.StartsWith(benchPrefix, StringComparison.Ordinal))
                RegisterAuthoredBench(item, attendance, standPrefix, benchPrefix);
            else if (item.name.StartsWith(venue + " grandstand", StringComparison.Ordinal) &&
                item.TryGetComponent<Renderer>(out var stand))
                importedGrandstandRenderers.Add(stand);
            else if (item.name.StartsWith(venue + " pedestrian walk ", StringComparison.Ordinal))
            {
                bool isStart = item.name.EndsWith(" start", StringComparison.Ordinal);
                bool isEnd = item.name.EndsWith(" end", StringComparison.Ordinal);
                if (!isStart && !isEnd) continue;
                string name = item.name.Substring(0, item.name.LastIndexOf(' '));
                paths.TryGetValue(name, out var pair);
                paths[name] = isStart ? (item, pair.End) : (pair.Start, item);
                HideAuthoredVenueMarker(item);
            }
            else if (item.name.StartsWith(venue + " standing fan ", StringComparison.Ordinal))
            {
                var point = transform.InverseTransformPoint(item.position);
                float station = TrackUtilityGeometry.ClosestMainDistance(customLayout, lapLength, point);
                var facing = Vector3.ProjectOnPlane(TrackPoint(station, 0f, out _, out _) - point, Vector3.up);
                QueueIowaStandingFan(point, facing, fanSeed++);
                HideAuthoredVenueMarker(item);
            }
            else if (item.name.StartsWith(venue + " floodlight ", StringComparison.Ordinal))
                AddAuthoredVenueLamp(item, racing: true);
            else if (item.name.StartsWith(venue + " street lamp ", StringComparison.Ordinal))
                AddAuthoredVenueLamp(item, racing: false);
            else if (item.name.StartsWith(venue + " ambulance bay ", StringComparison.Ordinal))
                AddAuthoredResponseVehicle(item, ambulance: true);
            else if (item.name.StartsWith(venue + " tow bay ", StringComparison.Ordinal))
                AddAuthoredResponseVehicle(item, ambulance: false);
        }

        using var grounding = definition.id == RichmondTrackId && paths.Count > 0
            ? new RichmondWalkGround(transform) : null;
        foreach (var pair in paths.Values)
        {
            if (pair.Start == null || pair.End == null) continue;
            Vector3 start = transform.InverseTransformPoint(pair.Start.position);
            Vector3 end = transform.InverseTransformPoint(pair.End.position);
            float length = Vector3.Distance(start, end);
            if (length < 2f) continue;
            int people = Mathf.Clamp(Mathf.FloorToInt(length / 8f), 1, 8);
            for (int walker = 0; walker < people; walker++)
            {
                float span = 1f / people;
                Vector3 a = Vector3.Lerp(start, end, (walker + .06f) * span);
                Vector3 b = Vector3.Lerp(start, end, (walker + .94f) * span);
                if (walker % 2 != 0)
                    (a, b) = (b, a);
                QueueIowaWalkingFan(a, b, fanSeed++, groundHeights: grounding?.Sample(a, b));
            }
        }
        BuildIowaFans();
        if (bristolSun != null)
            ApplyIowaNightLighting(TrackSkyIsNight);
    }

    private static void HideAuthoredVenueMarker(Transform item)
    {
        if (item.TryGetComponent<Renderer>(out var renderer))
            renderer.enabled = false;
    }

    private void RegisterAuthoredBench(Transform marker, GrandstandAttendance attendance, string standPrefix, string benchPrefix)
    {
        HideAuthoredVenueMarker(marker);
        if (!marker.TryGetComponent<MeshFilter>(out var filter) || filter.sharedMesh == null) return;

        // FBX can store the unit conversion in the marker mesh or transform.
        // Transform the actual mesh endpoints so seating follows the authored width.
        Bounds bounds = filter.sharedMesh.bounds;
        Vector3 halfRow = Vector3.right * bounds.extents.x;
        Vector3 a = transform.InverseTransformPoint(marker.TransformPoint(bounds.center - halfRow));
        Vector3 b = transform.InverseTransformPoint(marker.TransformPoint(bounds.center + halfRow));
        float width = Vector3.Distance(a, b);
        int count = Mathf.FloorToInt((width - .50f) / GrandstandMeshBuilder.SeatSpacing);
        if (count > 0)
        {
            Vector3 center = (a + b) * .5f;
            float station = TrackUtilityGeometry.ClosestMainDistance(customLayout, lapLength, center);
            Vector3 outward = SampleOutsideEdge(station).Outward;
            var firstSeat = new Vector3(-(count - 1) * GrandstandMeshBuilder.SeatSpacing * .5f, 0f, 0f);
            string name = marker.name.Substring(benchPrefix.Length);
            int dash = name.IndexOf('-');
            string section = dash >= 0 ? name.Substring(0, dash) : name;
            attendance.Register(standPrefix + section, new GrandstandMeshBuilder.SeatStrip(firstSeat, count),
                a, b, outward, outward, width);
        }
    }

    private (string Name, float Station)[] PhoenixSafetySites()
    {
        // Fractions are along the race spline from its authored seam. Explicit
        // positions remain independent of elevation and banking transition limits.
        return new (string, float)[]
        {
            ("Front straight exit", .25406f), ("Turn 3 midpoint", .30599f),
            ("Turn 4 midpoint", .43791f), ("Back straight entrance", .54064f),
            ("Back straight exit", .75616f), ("Turn 1 midpoint", .81002f),
            ("Turn 2 midpoint", .90620f), ("Front straight entrance", .96267f)
        };
    }

    private void AddScoringPylonAtPitWalkway(string venue, float walkwayCenterFromPit)
    {
        const float walkwayDepth = 3.048f;
        const float bayDepth = 1.48f;
        Vector3 finish = TrackPoint(definition.StartFinishFraction * lapLength, 0f, out var raceHeading, out _);
        var spline = pitRoute[1];
        Vector3 Bay(float fraction, out Vector3 inward)
        {
            Vector3 point = PitPosition(spline, fraction, pitHeights[1], pitHeights[2]);
            Vector3 heading = PitTangent(spline, fraction, pitHeights[1], pitHeights[2]);
            inward = Vector3.Cross(Vector3.up, heading).normalized * pitWallSide;
            return point + inward * (walkwayCenterFromPit + walkwayDepth * .5f + bayDepth * .5f);
        }

        float previous = Vector3.Dot(Bay(0f, out _) - finish, raceHeading);
        float bestDistance = float.PositiveInfinity;
        float bestFraction = -1f;
        for (int sample = 1; sample <= 256; sample++)
        {
            float fraction = sample / 256f;
            Vector3 point = Bay(fraction, out _);
            float current = Vector3.Dot(point - finish, raceHeading);
            if ((current < 0f) != (previous < 0f))
            {
                float crossing = (sample - 1 + previous / (previous - current)) / 256f;
                Vector3 crossingPoint = Bay(crossing, out _);
                float distance = (crossingPoint - finish).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestFraction = crossing;
                    bestDistance = distance;
                }
            }
            previous = current;
        }
        if (bestFraction < 0f) return;
        Vector3 position = Bay(bestFraction, out var infield);
        position.y = Mathf.Max(0f, position.y - .016f);
        var bay = Box(venue + " scoring pylon walkway bay", transform, position - infield * .04f - Vector3.up * .045f,
            new Vector3(2.8f, .09f, bayDepth + .08f), MakeMaterial(new Color32(154, 151, 139, 255)));
        bay.transform.localRotation = Quaternion.LookRotation(-infield, Vector3.up);
        BuildScoringPylon(position, -infield);
    }

    private void AddAuthoredVenueLamp(Transform fixture, bool racing)
    {
        Vector3 origin = transform.InverseTransformPoint(fixture.position);
        Vector3 aim = origin + Vector3.down;
        if (racing)
        {
            float station = TrackUtilityGeometry.ClosestMainDistance(customLayout, lapLength, origin);
            aim = Vector3.Lerp(TrackPoint(station, 0f, out _, out _),
                TrackPoint(station, ApronLane(station), out _, out _), .20f);
        }

        var light = CreateObject(fixture.name + " emitter", transform).AddComponent<Light>();
        Vector3 axis = (aim - origin).normalized;
        light.transform.SetLocalPositionAndRotation(origin + axis * .16f,
            Quaternion.LookRotation(axis, racing ? Vector3.up : Vector3.forward));
        light.type = LightType.Spot;
        light.color = racing ? new Color32(236, 243, 255, 255) : new Color32(255, 231, 193, 255);
        light.intensity = racing ? 780f : 85f;
        light.range = racing ? 70f : 18f;
        light.spotAngle = racing ? 108f : 86f;
        light.innerSpotAngle = racing ? 88f : 67f;
        light.shadows = LightShadows.None;
        RegisterIowaVenueLight(light, racing);

        if (!fixture.TryGetComponent<Renderer>(out var face)) return;
        var lens = MakeMaterial(new Color32(211, 221, 223, 255));
        lens.name = VenueSceneryName + " lamp lens";
        face.sharedMaterial = lens;
        authoredVenueLampLenses.Add(lens);
    }

    private void SetAuthoredVenueLampLenses(bool night)
    {
        foreach (var lens in authoredVenueLampLenses)
        {
            if (night) lens.EnableKeyword("_EMISSION");
            else lens.DisableKeyword("_EMISSION");
            lens.SetColor("_EmissionColor", night ? new Color(.83f, .9f, 1f) * 2.55f : Color.black);
        }
    }

    private void AddAuthoredResponseVehicle(Transform marker, bool ambulance)
    {
        Vector3 position = transform.InverseTransformPoint(marker.position);
        Vector3 facing = Vector3.ProjectOnPlane(transform.InverseTransformDirection(marker.forward), Vector3.up);
        if (facing.sqrMagnitude < .001f) facing = Vector3.forward;
        HideAuthoredVenueMarker(marker);
        var vehicle = CreateObject(VenueSceneryName + (ambulance ? " ambulance" : " tow truck"), transform);
        vehicle.transform.SetLocalPositionAndRotation(position, Quaternion.LookRotation(facing, Vector3.up));
        var shape = new IowaPitWorkMesh();
        if (ambulance) BuildIowaAmbulance(shape);
        else BuildIowaTowTruck(shape);
        Mesh mesh = shape.Build(vehicle.name, ambulance ? 0 : 1, 2);
        roadMeshes.Add(mesh);
        vehicle.AddComponent<MeshFilter>().sharedMesh = mesh;
        vehicle.AddComponent<MeshRenderer>().sharedMaterial = iowaResponseVehiclePaint ??= CreateIowaResponseVehiclePaint();
        AddIowaResponseWarningLamps(vehicle.transform, ambulance);
        RegisterAuthoredResponseVehicle(vehicle.transform, ambulance);
    }

    private uint AuthoredVenueLightingZone(Renderer renderer)
    {
        string name = renderer.name;
        if (teamPitBoxes != null && renderer.transform.IsChildOf(teamPitBoxes.transform) ||
            name.Contains("pit road", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("pit wall", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("pit work", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("pit stall", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Pit continuous stall-side wall", StringComparison.Ordinal))
            return IowaPitLightingLayer;
        if (name.Contains("racing surface", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("apron", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("outside wall", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Outside continuous concrete wall", StringComparison.Ordinal) ||
            name.StartsWith("Track inner ", StringComparison.Ordinal) ||
            name.StartsWith("Pit entrance asphalt", StringComparison.Ordinal) ||
            name.StartsWith("Pit exit asphalt", StringComparison.Ordinal) ||
            name.StartsWith("Start finish checker", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("restart", StringComparison.OrdinalIgnoreCase))
            return IowaRaceLightingLayer;
        return IowaRoadLightingLayer;
    }
}

