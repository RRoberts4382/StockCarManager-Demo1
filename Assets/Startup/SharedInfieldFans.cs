using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class TrackPreview
{
    private readonly List<(float Start, float End)> sharedFanPitWalls = new();

    private void RegisterSharedInfieldWall(float startFraction, float endFraction) =>
        sharedFanPitWalls.Add((startFraction, endFraction));

    private void AddSharedInfieldWallFans()
    {
        if (pitRoute == null || customLayout == null || (definition.id == IowaTrackId && importedTrackGeometry))
            return; // Iowa already has authored rows behind its curved infield barriers.

        const float wallEndClearance = 3.25f;
        const float rowSpacing = .87f;
        const float firstRowDepth = .62f;
        const float groupSpacing = 4.6f;
        const int rowCount = 3;
        var random = new System.Random(18379 + definition.id * 839);
        var temporary = new List<MeshCollider>();
        var innerWalls = new List<MeshCollider>();
        var authoredPitWalls = new List<MeshCollider>();
        var ground = new List<MeshCollider>();
        var vehiclePavement = new List<MeshCollider>();
        var occupied = new List<Vector3>();
        foreach (var item in GetComponentsInChildren<Transform>())
        {
            if (item.name.IndexOf(" standing fan ", StringComparison.OrdinalIgnoreCase) >= 0)
                occupied.Add(transform.InverseTransformPoint(item.position));
        }

        try
        {
            foreach (var filter in GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null)
                    continue;
                var name = filter.name;
                if (IsSharedFanInnerWall(name))
                    innerWalls.Add(UseSurface(filter));
                else if (IsSharedFanPitWall(name))
                    authoredPitWalls.Add(UseSurface(filter));
                else if (IsSharedFanVehiclePavement(name) || definition.id == DoverTrackId &&
                    (name.StartsWith("Pit entrance", StringComparison.OrdinalIgnoreCase) ||
                     name.StartsWith("Pit exit", StringComparison.OrdinalIgnoreCase)))
                    vehiclePavement.Add(UseSurface(filter));
                else if (filter.transform == grassGround || IsSharedFanGround(name))
                    ground.Add(UseSurface(filter));
            }
            Physics.SyncTransforms();

            float width = definition.pitRoad.widthMeters;
            float pitLength = pitLengths[1];
            foreach (var (first, last) in sharedFanPitWalls)
            {
                AddRows(first * pitLength + wallEndClearance, last * pitLength - wallEndClearance,
                    (station, depth) =>
                    {
                        if (!ClearOfPitWork(station))
                            return;
                        PitFrame(station, out var center, out var protectedSide);
                        var wallBack = center + protectedSide * (width * .5f + PitWallThickness);
                        AddFan(wallBack, protectedSide, depth);
                    });
            }

            if (innerWalls.Count > 0)
                AddRows(0f, lapLength, (station, depth) => AddAuthoredFan(innerWalls, station, depth, TrackFrame, 28f));

            if (authoredPitWalls.Count > 0)
                AddRows(wallEndClearance, pitLength - wallEndClearance, (station, depth) =>
                {
                    if (ClearOfPitWork(station))
                        AddAuthoredFan(authoredPitWalls, station, depth, PitFrame, width * .5f + 12f);
                });
            BuildIowaFans();
        }
        finally
        {
            foreach (var surface in temporary)
            {
                surface.enabled = false;
                if (Application.isPlaying)
                    Destroy(surface);
                else
                    DestroyImmediate(surface);
            }
        }

        MeshCollider UseSurface(MeshFilter filter)
        {
            if (filter.TryGetComponent<MeshCollider>(out var existing) && existing.enabled &&
                existing.sharedMesh == filter.sharedMesh)
                return existing;
            var collider = filter.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
            temporary.Add(collider);
            return collider;
        }

        void AddRows(float first, float last, Action<float, float> sample)
        {
            for (int row = 0; row < rowCount; row++)
            {
                float depth = firstRowDepth + row * rowSpacing;
                for (float station = first + row * 1.35f; station < last; station += groupSpacing)
                {
                    if (random.NextDouble() < .14 + row * .12)
                        continue;
                    float varied = station + (float)random.NextDouble() * 1.2f;
                    if (varied < last)
                        sample(varied, depth + (float)random.NextDouble() * .22f);
                }
            }
        }

        void PitFrame(float station, out Vector3 point, out Vector3 protectedSide)
        {
            float fraction = Mathf.Clamp01(station / pitLengths[1]);
            point = PitPosition(pitRoute[1], fraction, pitHeights[1], pitHeights[2]);
            var tangent = PitTangent(pitRoute[1], fraction, pitHeights[1], pitHeights[2]);
            protectedSide = Vector3.Cross(Vector3.up, tangent).normalized * pitWallSide;
        }

        bool ClearOfPitWork(float station)
        {
            if (practiceStallDistances != null)
                foreach (float stall in practiceStallDistances)
                    if (Mathf.Abs(stall - pitLengths[0] - station) < 1.85f)
                        return false;
            return true;
        }

        void TrackFrame(float station, out Vector3 point, out Vector3 protectedSide)
        {
            float direction = customLayout.InsideSign;
            point = TrackPoint(station, direction * RoadWidths(station).x * .5f, out var tangent, out _);
            protectedSide = Vector3.Cross(Vector3.up, tangent).normalized * direction;
        }

        void AddAuthoredFan(List<MeshCollider> walls, float station, float depth, SharedFanFrame frame, float reach)
        {
            frame(station, out var center, out var protectedSide);
            if (!TryWallFace(walls, center, protectedSide, reach, out var wall, out var front))
                return;

            // Both neighboring stations must be covered by the same barrier.
            // This keeps the rows away from wall ends and access openings.
            for (int side = -1; side <= 1; side += 2)
            {
                frame(station + side * wallEndClearance, out var neighbor, out var direction);
                var support = WorldRay(neighbor + Vector3.up * .48f, direction);
                if (!wall.Raycast(support, out _, reach))
                    return;
            }

            // Use the protected face of the real wall rather than assuming
            // imported barriers all use the procedural pit-wall thickness.
            var backProbe = WorldRay(front + protectedSide * 2f, -protectedSide);
            if (wall.Raycast(backProbe, out var back, 2f) &&
                back.distance > .4f)
                AddFan(transform.InverseTransformPoint(back.point), protectedSide, depth, center.y);
        }

        bool TryWallFace(List<MeshCollider> walls, Vector3 center, Vector3 protectedSide, float reach,
            out MeshCollider wall, out Vector3 point)
        {
            wall = null;
            point = default;
            var ray = WorldRay(center + Vector3.up * .48f, protectedSide);
            foreach (var surface in walls)
            {
                if (!surface.bounds.IntersectRay(ray) || !surface.Raycast(ray, out var hit, reach) ||
                    Vector3.Dot(transform.InverseTransformDirection(hit.normal), protectedSide) > -.35f)
                    continue;
                reach = hit.distance;
                wall = surface;
                point = transform.InverseTransformPoint(hit.point);
            }
            return wall != null;
        }

        void AddFan(Vector3 wallBack, Vector3 protectedSide, float depth, float? floorHint = null)
        {
            var spot = wallBack + protectedSide * depth;
            spot.y = floorHint ?? wallBack.y;
            float station = TrackUtilityGeometry.ClosestMainDistance(customLayout, lapLength, spot);
            var track = TrackPoint(station, 0f, out _, out _);
            var fromTrack = Vector3.ProjectOnPlane(spot - track, Vector3.up);
            var widths = RoadWidths(station);
            float reserve = widths.x * .5f + widths.y + .7f;
            if (Vector3.Dot(fromTrack, protectedSide) <= 0f || fromTrack.sqrMagnitude < reserve * reserve)
                return;
            var source = customLayout.ToSourceSpace(spot);
            source.y = 0f;
            foreach (var route in pitRoute)
            {
                var nearest = customLayout.ToTrackSpace(route.Sample(route.ClosestFraction(source)));
                if (Vector3.ProjectOnPlane(spot - nearest, Vector3.up).sqrMagnitude <
                    (definition.pitRoad.widthMeters * .5f + .6f) * (definition.pitRoad.widthMeters * .5f + .6f))
                    return;
            }
            foreach (var person in occupied)
                if (Vector3.ProjectOnPlane(spot - person, Vector3.up).sqrMagnitude < .71f * .71f)
                    return;

            var floorRay = WorldRay(spot + Vector3.up * .7f, Vector3.down);
            foreach (var route in vehiclePavement)
                if (route.bounds.IntersectRay(floorRay) && route.Raycast(floorRay, out _, 2f))
                    return;
            float floorDistance = 2f;
            foreach (var surface in ground)
                if (surface.bounds.IntersectRay(floorRay) && surface.Raycast(floorRay, out var floor, floorDistance)
                    && floor.normal.y > .5f)
                    floorDistance = floor.distance;
            if (floorDistance == 2f)
                return;
            spot = transform.InverseTransformPoint(floorRay.GetPoint(floorDistance));
            QueueIowaStandingFan(spot, -protectedSide, random.Next());
            occupied.Add(spot);
        }

        Ray WorldRay(Vector3 origin, Vector3 direction) =>
            new(transform.TransformPoint(origin), transform.TransformDirection(direction));
    }

    private delegate void SharedFanFrame(float station, out Vector3 point, out Vector3 protectedSide);

    private static bool IsSharedFanInnerWall(string name) =>
        name.StartsWith("Inside apron wall", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Inside apron concrete wall", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Infield perimeter wall", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Infield turn wall", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Concrete perimeter wall", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("infield wall", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Road-course barrier", StringComparison.OrdinalIgnoreCase);

    private static bool IsSharedFanPitWall(string name) =>
        name.StartsWith("Pit continuous stall-side wall", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Pit stall side wall", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Pit wall behind stalls", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Bristol frontstretch pit wall", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Bristol backstretch pit wall", StringComparison.OrdinalIgnoreCase);

    private static bool IsSharedFanVehiclePavement(string name) =>
        name.IndexOf("access road", StringComparison.OrdinalIgnoreCase) >= 0 ||
        name.IndexOf("service road", StringComparison.OrdinalIgnoreCase) >= 0 ||
        name.IndexOf("garage entry road", StringComparison.OrdinalIgnoreCase) >= 0 ||
        name.IndexOf("garage exit road", StringComparison.OrdinalIgnoreCase) >= 0 ||
        name.IndexOf("emergency access", StringComparison.OrdinalIgnoreCase) >= 0 ||
        name.Equals("Emergency vehicle paved bays", StringComparison.OrdinalIgnoreCase);

    private static bool IsSharedFanGround(string name) =>
        name.Equals("Michigan grass site", StringComparison.OrdinalIgnoreCase) ||
        name.IndexOf("infield", StringComparison.OrdinalIgnoreCase) >= 0 &&
            (name.IndexOf("asphalt", StringComparison.OrdinalIgnoreCase) >= 0 ||
             name.IndexOf("ground", StringComparison.OrdinalIgnoreCase) >= 0 ||
             name.IndexOf("grass", StringComparison.OrdinalIgnoreCase) >= 0) ||
        name.IndexOf("walkway", StringComparison.OrdinalIgnoreCase) >= 0 ||
        name.IndexOf("embankment", StringComparison.OrdinalIgnoreCase) >= 0 ||
        name.IndexOf("motor court", StringComparison.OrdinalIgnoreCase) >= 0 ||
        name.IndexOf("pit work", StringComparison.OrdinalIgnoreCase) >= 0;
}
