using System;
using System.Collections.Generic;
using UnityEngine;

internal static class TrackUtilityGeometry
{
    public static string Validate(TrackDefinition track)
    {
        var markers = track.raceMarkers;
        if (markers != null)
        {
            if (markers.hasStartFinish && !Fraction(markers.startFinish))
                return "Start/finish must be between 0 and 100% of the lap.";
            if (markers.hasRestartZone)
            {
                float start = Mathf.Repeat(markers.restartZoneStart - track.StartFinishFraction, 1f);
                float end = Mathf.Repeat(markers.restartZoneEnd - track.StartFinishFraction, 1f);
                if (!Fraction(markers.restartZoneStart) || !Fraction(markers.restartZoneEnd) || start <= 0f || end <= start)
                    return "The restart zone must run forward and end before the next start/finish crossing.";
            }
        }

        var pit = track.pitRoad;
        if (pit?.enabled != true)
            return null;
        if ((track.layoutPoints?.Length ?? 0) < 3)
            return "Make the main spline editable before adding pit road.";
        if (!(pit.widthMeters >= 3f && pit.widthMeters <= 100f) ||
            !(pit.speedLimitMph > 0f && pit.speedLimitMph <= 200f) ||
            !Fraction(pit.speedLimitStart) || !Fraction(pit.speedLimitEnd) || pit.speedLimitStart >= pit.speedLimitEnd)
            return "Enter a pit width of 3–100 m, a speed above 0 and up to 200 mph, and a speed-limit start before its end.";
        if (!ValidPoints(pit.points, 2, 256) || !ValidPoints(pit.entrance, 0, 256) ||
            !ValidPoints(pit.exit, 0, 256) || !ValidPoints(pit.stalls, 0, 1000))
            return "Pit road needs at least two distinct points. Pit coordinates must be finite.";
        return null;
    }

    private static bool Fraction(float value) => value >= 0f && value <= 1f;

    private static bool ValidPoints(TrackPitPoint[] points, int min, int max)
    {
        int count = points?.Length ?? 0;
        if (count < min || count > max)
            return false;
        for (int i = 0; i < count; i++)
        {
            var point = points[i];
            if (point == null || !(Mathf.Abs(point.x) <= 100000f && Mathf.Abs(point.z) <= 100000f))
                return false;
            if (i > 0 && point.x == points[i - 1].x && point.z == points[i - 1].z)
                return false;
        }
        return true;
    }

    public static TrackLayoutPoint[] EditableOval(TrackDefinition track)
    {
        var result = new List<TrackLayoutPoint>();
        float half = track.OvalStraightLength * 0.5f;
        float radius = track.OvalTurnRadius;
        for (int end = 0; end < 2; end++)
        {
            float sign = end == 0 ? 1f : -1f;
            float entry = end == 0 ? track.banking.frontStraight : track.banking.backStraight;
            float exit = end == 0 ? track.banking.backStraight : track.banking.frontStraight;
            float peak = end == 0 ? track.banking.turns12 : track.banking.turns34;
            if (half > 0f)
            {
                for (int i = 0; i < 8; i++)
                {
                    result.Add(new TrackLayoutPoint
                    {
                        x = sign * Mathf.Lerp(-half, half, i / 8f),
                        z = -sign * radius,
                        banking = entry
                    });
                }
            }
            for (int i = 0; i < 24; i++)
            {
                float progress = i / 24f;
                float angle = (progress + (end == 0 ? -0.5f : 0.5f)) * Mathf.PI;
                result.Add(new TrackLayoutPoint
                {
                    x = sign * half + radius * Mathf.Cos(angle),
                    z = radius * Mathf.Sin(angle),
                    banking = progress < 0.5f
                        ? Mathf.SmoothStep(entry, peak, progress / 0.30f)
                        : Mathf.SmoothStep(exit, peak, (1f - progress) / 0.30f)
                });
            }
        }
        return result.ToArray();
    }

    public static void ApplyAspectRatio(TrackDefinition track)
    {
        if (!(track.aspectRatio >= 1f) || float.IsInfinity(track.aspectRatio))
            throw new ArgumentException("Enter an aspect ratio of at least 1.");

        string error = track.Validate();
        if (error != null)
            throw new ArgumentException(error);

        var spline = new TrackLayout(track.layoutPoints, track.lengthMiles * TrackDefinition.MetersPerMile, track.widthMeters);
        Vector3 size = spline.SourceSize;
        if (size.x <= 0f || size.z <= 0f)
            throw new ArgumentException("The spline needs both horizontal and vertical extent to change its aspect ratio.");

        float scaleX = track.aspectRatio * (size.z / size.x);
        float centerX = spline.SourceCenterX;
        foreach (var point in track.layoutPoints)
            point.x = centerX + (point.x - centerX) * scaleX;

        var pit = track.pitRoad;
        if (pit?.enabled == true)
        {
            foreach (var path in new[] { pit.points, pit.entrance, pit.exit, pit.stalls })
            {
                foreach (var point in path ?? Array.Empty<TrackPitPoint>())
                    point.x = centerX + (point.x - centerX) * scaleX;
            }
        }
    }

    public static void GenerateStalls(TrackDefinition track)
    {
        EnsurePitReady(track);
        if (track.pitStalls < 1 || track.pitStalls > 1000)
            throw new ArgumentException("Set the number of pit stalls to 1–1,000.");
        var pit = track.pitRoad;
        var road = new TrackPitSpline(pit.points);
        var stalls = new TrackPitPoint[track.pitStalls];
        for (int i = 0; i < stalls.Length; i++)
        {
            float fraction = Mathf.Lerp(pit.speedLimitStart, pit.speedLimitEnd, (i + 0.5f) / stalls.Length);
            stalls[i] = TrackPitSpline.Data(road.Sample(fraction));
        }
        pit.stalls = stalls;
    }

    public static void GenerateConnections(TrackDefinition track)
    {
        GenerateConnection(track, true);
        GenerateConnection(track, false);
    }

    public static void GenerateConnection(TrackDefinition track, bool entrance, Vector3? sourceAttachment = null)
    {
        EnsurePitReady(track);
        float lap = track.lengthMiles * TrackDefinition.MetersPerMile;
        var main = new TrackLayout(track.layoutPoints, lap, track.widthMeters);
        var pit = new TrackPitSpline(track.pitRoad.points);
        float endpoint = entrance ? 0f : 1f;
        Vector3 position = main.ToTrackSpace(pit.Sample(endpoint));
        Vector3 tangent = (pit.Sample(entrance ? 0.02f : 1f) - pit.Sample(entrance ? 0f : 0.98f)).normalized;
        float? attachment = null;
        if (sourceAttachment.HasValue)
        {
            Vector3 clicked = main.ToTrackSpace(sourceAttachment.Value);
            float distance = ClosestMainDistance(main, lap, clicked);
            Vector3 center = main.Sample(distance, out _, out _);
            center.y = clicked.y;
            if (Vector3.Distance(center, clicked) > main.Widths(distance).x * 0.5f + 5f)
                throw new ArgumentException("Click on the main track to place the connector.");
            attachment = distance;
        }
        TrackPitPoint[] connector = Connect(main, lap, position, tangent, entrance, attachment);
        if (entrance)
            track.pitRoad.entrance = connector;
        else
            track.pitRoad.exit = connector;
    }

    private static TrackPitPoint[] Connect(TrackLayout main, float lap, Vector3 pitEnd, Vector3 pitTangent, bool entrance, float? attachment)
    {
        float bestDistance = NearestMainDistance(main, pitEnd, pitTangent);
        Vector3 nearest = main.Sample(bestDistance, out _, out _);
        nearest.y = pitEnd.y;
        float run = Mathf.Min(lap * 0.15f, Mathf.Clamp(Vector3.Distance(nearest, pitEnd) * 2f, 35f, 110f));
        float direction = entrance ? 1f : -1f;
        float join = bestDistance - direction * run;
        if (attachment.HasValue)
        {
            join = attachment.Value;
            run = Mathf.Repeat(direction * (bestDistance - join), lap);
            if (run < 1f)
                throw new ArgumentException(entrance
                    ? "Choose a track point before the pit-road start in race direction."
                    : "Choose a track point after the pit-road end in race direction.");
        }
        float mergeLength = Mathf.Min(run * 0.30f, 45f);
        float transitionLength = Mathf.Min(run * 0.35f, 65f);
        var points = new TrackPitPoint[Mathf.Clamp(Mathf.CeilToInt(run / 8f) + 1, 13, 256)];
        for (int i = 0; i < points.Length; i++)
        {
            float t = i / (float)(points.Length - 1);
            float trackDistance = Mathf.Repeat(join + direction * run * t, lap);
            Vector3 trackPoint = main.Sample(trackDistance, out Vector3 forward, out _);
            Vector2 widths = main.Widths(trackDistance);
            Vector3 inward = Vector3.Cross(Vector3.up, forward) * main.InsideSign;
            float merge = Mathf.SmoothStep(0f, 1f, run * t / mergeLength);
            // Match the blue apron guide drawn by TrackUtilityMap, even at zero apron width.
            float apronEdgeOffset = widths.x * 0.5f + widths.y;
            trackPoint += inward * (apronEdgeOffset * merge);
            float remaining = run * (1f - t);
            Vector3 pitApproach = pitEnd - direction * pitTangent * remaining;
            // A zero blend slope at both ends preserves the track and pit-road tangents.
            float transition = Mathf.Clamp01(1f - remaining / transitionLength);
            float blend = transition * transition * transition * (10f + transition * (6f * transition - 15f));
            Vector3 point = Vector3.Lerp(trackPoint, pitApproach, blend);
            int index = entrance ? i : points.Length - 1 - i;
            points[index] = TrackPitSpline.Data(main.ToSourceSpace(point));
        }
        return points;
    }

    internal static float ClosestMainDistance(TrackLayout main, float lap, Vector3 position)
    {
        float bestDistance = 0f;
        float bestError = float.PositiveInfinity;
        Vector3 start = main.Sample(0f, out _, out _);
        start.y = position.y;
        for (int i = 0; i < main.Samples; i++)
        {
            float startDistance = main.SampleDistance(i);
            float endDistance = i + 1 == main.Samples ? lap : main.SampleDistance(i + 1);
            Vector3 end = main.Sample(endDistance, out _, out _);
            end.y = position.y;
            Vector3 segment = end - start;
            float blend = segment.sqrMagnitude > 0f
                ? Mathf.Clamp01(Vector3.Dot(position - start, segment) / segment.sqrMagnitude) : 0f;
            float error = (position - Vector3.Lerp(start, end, blend)).sqrMagnitude;
            if (error < bestError)
            {
                bestError = error;
                bestDistance = Mathf.Lerp(startDistance, endDistance, blend);
            }
            start = end;
        }
        return bestDistance;
    }

    public static void ShapePitRoad(TrackDefinition track)
    {
        EnsurePitReady(track);
        float lap = track.lengthMiles * TrackDefinition.MetersPerMile;
        var main = new TrackLayout(track.layoutPoints, lap, track.widthMeters);
        var pit = track.pitRoad;
        TrackPitPoint first = pit.points[0];
        TrackPitPoint last = pit.points[pit.points.Length - 1];
        Vector3 start = main.ToTrackSpace(TrackPitSpline.Position(first));
        Vector3 end = main.ToTrackSpace(TrackPitSpline.Position(last));
        float startDistance = NearestMainDistance(main, start, Vector3.zero);
        float endDistance = NearestMainDistance(main, end, Vector3.zero);
        float run = Mathf.Repeat(endDistance - startDistance, lap);
        if (run < 1f || run > lap * 0.5f)
            throw new ArgumentException("Place pit start and end beside different points on the main track, in race direction, within half a lap.");

        Vector2 startOffset = OffsetAt(main, startDistance, start);
        Vector2 endOffset = OffsetAt(main, endDistance, end);
        int count = Mathf.Clamp(Mathf.CeilToInt(run / 12f) + 1, 9, 96);
        var points = new TrackPitPoint[count];
        points[0] = first;
        points[count - 1] = last;
        for (int i = 1; i < count - 1; i++)
        {
            float t = i / (float)(count - 1);
            Vector3 center = main.Sample(Mathf.Repeat(startDistance + t * run, lap), out Vector3 tangent, out _);
            Vector3 right = Vector3.Cross(Vector3.up, tangent);
            Vector2 offset = Vector2.Lerp(startOffset, endOffset, t);
            points[i] = TrackPitSpline.Data(main.ToSourceSpace(center + tangent * offset.x + right * offset.y));
        }
        pit.points = points;
    }

    private static Vector2 OffsetAt(TrackLayout main, float distance, Vector3 position)
    {
        Vector3 center = main.Sample(distance, out Vector3 tangent, out _);
        Vector3 offset = position - center;
        return new Vector2(Vector3.Dot(offset, tangent), Vector3.Dot(offset, Vector3.Cross(Vector3.up, tangent)));
    }

    private static float NearestMainDistance(TrackLayout main, Vector3 position, Vector3 direction)
    {
        float bestDistance = 0f;
        float bestScore = float.PositiveInfinity;
        for (int i = 0; i < main.Samples; i++)
        {
            float distance = main.SampleDistance(i);
            Vector3 center = main.Sample(distance, out Vector3 tangent, out _);
            center.y = position.y;
            tangent.y = 0f;
            float score = (center - position).sqrMagnitude + 400f * (1f - Vector3.Dot(tangent, direction));
            if (score < bestScore)
            {
                bestScore = score;
                bestDistance = distance;
            }
        }
        return bestDistance;
    }

    private static void EnsurePitReady(TrackDefinition track)
    {
        if (track.pitRoad?.enabled != true)
            throw new ArgumentException("Add the pit-road start and end points first.");
        string error = track.Validate();
        if (error != null)
            throw new ArgumentException(error);
    }
}

internal sealed class TrackPitSpline
{
    private readonly List<Vector3> samples = new List<Vector3>();
    private readonly List<float> distances = new List<float>();

    public float Length => distances[distances.Count - 1];

    public TrackPitSpline(TrackPitPoint[] points)
    {
        for (int i = 0; i < points.Length - 1; i++)
        {
            Vector3 b = Position(points[i]);
            Vector3 c = Position(points[i + 1]);
            Vector3 a = i == 0 ? 2f * b - c : Position(points[i - 1]);
            Vector3 d = i + 2 == points.Length ? 2f * c - b : Position(points[i + 2]);
            for (int j = 0; j < 16; j++)
            {
                float t = j / 16f;
                Add(0.5f * (2f * b + (c - a) * t + (2f * a - 5f * b + 4f * c - d) * t * t
                    + (-a + 3f * b - 3f * c + d) * t * t * t));
            }
        }
        Add(Position(points[points.Length - 1]));
    }

    public Vector3 Sample(float fraction)
    {
        float distance = Length * Mathf.Clamp01(fraction);
        int index = distances.BinarySearch(distance);
        if (index >= 0)
            return samples[index];
        int next = ~index;
        return Vector3.Lerp(samples[next - 1], samples[next],
            Mathf.InverseLerp(distances[next - 1], distances[next], distance));
    }

    private void Add(Vector3 point)
    {
        int count = samples.Count;
        distances.Add(count == 0 ? 0f : distances[count - 1] + Vector3.Distance(samples[count - 1], point));
        samples.Add(point);
    }

    public float ClosestFraction(Vector3 point)
    {
        float bestDistance = float.PositiveInfinity;
        float along = 0f;
        for (int i = 1; i < samples.Count; i++)
        {
            var segment = samples[i] - samples[i - 1];
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= 0f)
                continue;

            float t = Mathf.Clamp01(Vector3.Dot(point - samples[i - 1], segment) / lengthSquared);
            float distance = (point - (samples[i - 1] + t * segment)).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                along = Mathf.Lerp(distances[i - 1], distances[i], t);
            }
        }
        return Length > 0f ? along / Length : 0f;
    }

    public static Vector3 Position(TrackPitPoint point) => new Vector3(point.x, 0f, point.z);
    public static TrackPitPoint Data(Vector3 point) => new TrackPitPoint { x = point.x, z = point.z };
}
