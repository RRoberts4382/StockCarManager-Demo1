using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class TrackPreview
{
    private const float PitSignalWallSetback = .34f;

    private void CreatePitSignal()
    {
        pitOpenSignal = SignalMaterial(new Color32(45, 230, 70, 255));
        pitClosedSignal = SignalMaterial(new Color32(255, 40, 35, 255));
        var pole = CreateObject("Pit entry signal", transform).transform;
        PlacePitSignal(pole);

        var housing = MakeMaterial(new Color32(24, 25, 30, 255));
        var footing = MakeMaterial(new Color32(156, 152, 145, 255));
        Box("Pit signal footing", pole, new Vector3(0f, .05f, 0f), new Vector3(.42f, .10f, .42f), footing);
        Box("Signal post", pole, new Vector3(0f, 1.06f, 0f), new Vector3(.14f, 2.12f, .14f), housing);
        Box("Pit signal housing", pole, new Vector3(0f, 2.16f, 0f), new Vector3(.66f, .47f, .24f), housing);
        pitSignal = Box("Pit road light", pole, new Vector3(0f, 2.16f, .115f),
            new Vector3(.49f, .31f, .065f), pitOpenSignal).GetComponent<Renderer>();

        Material SignalMaterial(Color color)
        {
            var signal = MakeMaterial(color);
            signal.EnableKeyword("_EMISSION");
            signal.SetColor("_EmissionColor", color * .9f);
            return signal;
        }
    }

    private void PlacePitSignal(Transform pole)
    {
        float roadLength = pitLengths[1];
        float limitStart = Mathf.Clamp01(definition.pitRoad.speedLimitStart) * roadLength;
        float halfWidth = definition.pitRoad.widthMeters * .5f;
        Vector3 limitPoint = LaneCenter(limitStart);
        var walls = new List<PitSignalWall>();
        foreach (var filter in GetComponentsInChildren<MeshFilter>())
        {
            string name = filter.name;
            if (filter.sharedMesh != null && (name.Contains("pit wall", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("pit separator wall", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Pit continuous stall-side wall", StringComparison.Ordinal) ||
                name.StartsWith("Road-course barrier", StringComparison.OrdinalIgnoreCase)))
                walls.Add(new PitSignalWall(filter, transform));
        }

        // Sampling the full driving route keeps the post clear of the curved entry, the
        // through lane, and the exit even when they approach the same wall end.
        var routes = new List<Vector3>[pitRoute.Length];
        for (int segment = 0; segment < routes.Length; segment++)
        {
            int intervals = Mathf.Max(1, Mathf.CeilToInt(pitLengths[segment]));
            var route = routes[segment] = new List<Vector3>(intervals + 1);
            for (int sample = 0; sample <= intervals; sample++)
                route.Add(PitDrivingPosition(pitRoute[segment], sample / (float)intervals,
                    0f, pitHeights[segment], pitHeights[segment + 1]));
        }

        float bestDistance = float.PositiveInfinity;
        float foundStation = limitStart;
        Vector3 foot = default;
        Vector3 fallbackWall = default;
        Quaternion fallbackRotation = Quaternion.identity;
        const float stationStep = .40f;
        float searchRadius = Mathf.Min(100f, roadLength);
        int samples = Mathf.CeilToInt(searchRadius / stationStep);
        SearchExistingWalls(false);
        if (float.IsPositiveInfinity(bestDistance))
            SearchExistingWalls(true);

        if (float.IsPositiveInfinity(bestDistance))
        {
            // A venue such as Kansas can have a paved pit lane without a modeled
            // pit barrier at its mouth. Add a low individual barrier only in a clear verge.
            for (int i = 0; i <= samples && float.IsPositiveInfinity(bestDistance); i++)
            {
                float offset = i * stationStep;
                SearchVerge(limitStart + offset);
                if (i != 0)
                    SearchVerge(limitStart - offset);
            }
            if (float.IsPositiveInfinity(bestDistance))
            {
                Debug.LogWarning($"No protected pit signal location was found at {definition.name}.", this);
                PositionPitCar(pole, 0f, halfWidth + .5f);
                pole.localRotation *= Quaternion.Euler(0f, 180f, 0f);
                return;
            }

            var concrete = MakeMaterial(new Color32(177, 173, 163, 255));
            var wall = Box("Pit entry signal protective wall", transform, fallbackWall + Vector3.up * .46f,
                new Vector3(PitWallThickness, .92f, 1.65f), concrete);
            wall.transform.localRotation = fallbackRotation;
        }

        Vector3 viewFrom = foundStation >= 24f ? LaneCenter(foundStation - 24f) :
            PitDrivingPosition(pitRoute[0], Mathf.Clamp01(1f - (24f - foundStation) / pitLengths[0]),
                0f, pitHeights[0], pitHeights[1]);
        Vector3 facing = Vector3.ProjectOnPlane(viewFrom - foot, Vector3.up);
        pole.SetLocalPositionAndRotation(foot, Quaternion.LookRotation(facing, Vector3.up));

        Vector3 LaneCenter(float station) => PitPosition(pitRoute[1], station / roadLength, pitHeights[1], pitHeights[2]);

        void SearchExistingWalls(bool acceptProtectedPaving)
        {
            for (int i = 0; i <= samples; i++)
            {
                float offset = i * stationStep;
                if (offset * offset > bestDistance + 4f)
                    break;
                SearchWall(limitStart + offset, acceptProtectedPaving);
                if (i != 0)
                    SearchWall(limitStart - offset, acceptProtectedPaving);
            }
        }

        void SearchWall(float station, bool acceptProtectedPaving)
        {
            if (station < .26f || station > roadLength - .26f)
                return;

            Vector3 center = LaneCenter(station);
            Vector3 forward = PitTangent(pitRoute[1], station / roadLength, pitHeights[1], pitHeights[2]);
            Vector3 across = Vector3.Cross(Vector3.up, forward).normalized;
            foreach (var wall in walls)
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 ray = across * side;
                Vector3 midpoint = center + Vector3.up * .38f;
                float endClearance = acceptProtectedPaving ? .9f : .24f;
                if (!wall.TryCross(midpoint, ray, out float near, out float far) || near < halfWidth - .28f ||
                    far > halfWidth + 8f || far - near > 1.4f ||
                    !wall.TryCross(midpoint - forward * endClearance, ray, out _, out _) ||
                    !wall.TryCross(midpoint + forward * endClearance, ray, out _, out _))
                    continue;

                Vector3 point = center + ray * (far + PitSignalWallSetback);
                float score = HorizontalDistanceSquared(point, limitPoint);
                if (score >= bestDistance || !IsClearOfPavement(point, .25f, acceptProtectedPaving ? ray : null))
                    continue;

                foot = point;
                foundStation = station;
                bestDistance = score;
            }
        }

        void SearchVerge(float station)
        {
            if (station < .9f || station > roadLength - .9f)
                return;

            Vector3 center = LaneCenter(station);
            Vector3 forward = Vector3.ProjectOnPlane(PitTangent(pitRoute[1], station / roadLength,
                pitHeights[1], pitHeights[2]), Vector3.up).normalized;
            Vector3 across = Vector3.Cross(Vector3.up, forward).normalized;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 direction = across * (side * pitWallSide);
                Vector3 panel = center + direction * (halfWidth + .65f);
                Vector3 point = panel + direction * (PitWallThickness * .5f + PitSignalWallSetback);
                float score = HorizontalDistanceSquared(point, limitPoint);
                if (score >= bestDistance || !IsClearOfPavement(point, .25f) ||
                    !IsClearOfPavement(panel, .40f) || !IsClearOfPavement(panel + forward * .83f, .40f) ||
                    !IsClearOfPavement(panel - forward * .83f, .40f))
                    continue;

                foot = point;
                foundStation = station;
                fallbackWall = panel;
                fallbackRotation = Quaternion.LookRotation(forward, Vector3.up);
                bestDistance = score;
            }
        }

        bool IsClearOfPavement(Vector3 point, float clearance, Vector3? protectedSide = null)
        {
            float envelope = halfWidth + clearance;
            foreach (var route in routes)
            for (int i = 1; i < route.Count; i++)
                if (HorizontalSegmentDistanceSquared(point, route[i - 1], route[i]) < envelope * envelope)
                    return false;

            float track = TrackUtilityGeometry.ClosestMainDistance(customLayout, lapLength, point);
            Vector3 trackCenter = TrackPoint(track, 0f, out Vector3 heading, out _);
            Vector3 inward = Vector3.Cross(Vector3.up, heading).normalized * customLayout.InsideSign;
            var widths = RoadWidths(track);
            // Wide apron meshes may also pave the working area behind the infield wall.
            // Accept that verge only when a continuous wall separates it from the driving side.
            float apron = protectedSide.HasValue && Vector3.Dot(protectedSide.Value, inward) > .65f ? 0f : widths.y;
            Vector3 inside = TrackPoint(track, customLayout.InsideSign * (widths.x * .5f + apron), out _, out _);
            Vector3 outside = TrackPoint(track, -customLayout.InsideSign * widths.x * .5f, out _, out _);
            float lateral = Vector3.Dot(point - trackCenter, inward);
            return lateral > Vector3.Dot(inside - trackCenter, inward) + clearance ||
                lateral < Vector3.Dot(outside - trackCenter, inward) - clearance;
        }
    }

    private static float HorizontalDistanceSquared(Vector3 a, Vector3 b) =>
        (a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z);

    private static float HorizontalSegmentDistanceSquared(Vector3 point, Vector3 start, Vector3 end)
    {
        Vector3 line = end - start;
        line.y = 0f;
        float fraction = Mathf.Clamp01(Vector3.Dot(point - start, line) / Mathf.Max(.00001f, line.sqrMagnitude));
        return HorizontalDistanceSquared(point, start + line * fraction);
    }

    private sealed class PitSignalWall
    {
        private readonly Matrix4x4 toLocal;
        private readonly Bounds bounds;
        private readonly Vector3[] vertices;
        private readonly int[] triangles;

        public PitSignalWall(MeshFilter filter, Transform track)
        {
            toLocal = filter.transform.worldToLocalMatrix * track.localToWorldMatrix;
            Mesh mesh = filter.sharedMesh;
            bounds = mesh.bounds;
            if (mesh.isReadable)
            {
                vertices = mesh.vertices;
                triangles = mesh.triangles;
            }
        }

        public bool TryCross(Vector3 origin, Vector3 direction, out float near, out float far)
        {
            // Keep the ray parameter in track meters despite local FBX transforms.
            origin = toLocal.MultiplyPoint3x4(origin);
            direction = toLocal.MultiplyVector(direction);
            near = far = 0f;
            if (!bounds.IntersectRay(new Ray(origin, direction), out float boundsNear) ||
                !bounds.IntersectRay(new Ray(origin + direction * 30f, -direction), out float boundsFar))
                return false;

            float scale = direction.magnitude;
            if (vertices == null)
            {
                near = boundsNear / scale;
                far = 30f - boundsFar / scale;
                return far > near;
            }

            near = float.PositiveInfinity;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]];
                Vector3 ab = vertices[triangles[i + 1]] - a;
                Vector3 ac = vertices[triangles[i + 2]] - a;
                Vector3 normal = Vector3.Cross(direction, ac);
                float determinant = Vector3.Dot(ab, normal);
                // The FBX can store small local triangles under a scaled transform. A
                // fixed cutoff would reject valid triangles on otherwise full-size walls.
                float normalScale = Vector3.Cross(ab, ac).magnitude * scale;
                if (Mathf.Abs(determinant) <= .00001f * normalScale)
                    continue;

                Vector3 offset = origin - a;
                float x = Vector3.Dot(offset, normal) / determinant;
                Vector3 crossed = Vector3.Cross(offset, ab);
                float y = Vector3.Dot(direction, crossed) / determinant;
                float distance = Vector3.Dot(ac, crossed) / determinant;
                if (x < -.00001f || y < -.00001f || x + y > 1.00001f || distance < 0f)
                    continue;

                near = Mathf.Min(near, distance);
                far = Mathf.Max(far, distance);
            }
            return far > near;
        }
    }
}
