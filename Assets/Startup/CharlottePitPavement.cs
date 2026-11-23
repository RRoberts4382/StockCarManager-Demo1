using System.Collections.Generic;
using UnityEngine;

public sealed partial class TrackPreview
{
    private Vector3[] charlotteApronVertices;

    private Vector3[] ClipCharlottePitApron(Vector3[] ribbon, List<int> triangles)
    {
        var apron = charlotteApronVertices;
        if (apron == null) return ribbon;

        var faces = new List<(Vector3 A, Vector3 B, Vector3 C, Vector4 Bounds)>();
        Vector3 min = ribbon[0];
        Vector3 max = min;
        foreach (var point in ribbon)
        {
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }
        var ribbonBounds = new Vector4(min.x, min.z, max.x, max.z);
        for (int current = 0; current < apron.Length; current += 2)
        {
            int next = (current + 2) % apron.Length;
            AddFace(apron[current], apron[next], apron[current + 1]);
            AddFace(apron[current + 1], apron[next], apron[next + 1]);
        }

        var vertices = new List<Vector3>(ribbon.Length);
        var result = new List<int>(triangles.Count);
        var pieces = new List<List<Vector3>>();
        var remaining = new List<List<Vector3>>();
        for (int i = 0; i < triangles.Count; i += 3)
        {
            var a = ribbon[triangles[i]];
            var b = ribbon[triangles[i + 1]];
            var c = ribbon[triangles[i + 2]];
            var bounds = Bounds(a, b, c);
            pieces.Clear();
            pieces.Add(new List<Vector3> { a, b, c });
            foreach (var face in faces)
            {
                if (!Intersects(bounds, face.Bounds)) continue;
                remaining.Clear();
                foreach (var piece in pieces)
                {
                    // Split off the disjoint pieces outside each edge. Discard only
                    // the interior of the rendered apron triangle.
                    var inside = Split(piece, face.A, face.B, remaining);
                    inside = Split(inside, face.B, face.C, remaining);
                    Split(inside, face.C, face.A, remaining);
                }
                (pieces, remaining) = (remaining, pieces);
                if (pieces.Count == 0) break;
            }
            foreach (var piece in pieces)
            {
                int index = vertices.Count;
                vertices.AddRange(piece);
                for (int corner = 1; corner < piece.Count - 1; corner++)
                {
                    if (Side(piece[0], piece[corner], piece[corner + 1]) <= 0.000001) continue;
                    result.Add(index);
                    result.Add(index + corner);
                    result.Add(index + corner + 1);
                }
            }
        }
        triangles.Clear();
        triangles.AddRange(result);
        return vertices.ToArray();

        void AddFace(Vector3 a, Vector3 b, Vector3 c)
        {
            var bounds = Bounds(a, b, c);
            if (Intersects(ribbonBounds, bounds)) faces.Add((a, b, c, bounds));
        }

        static List<Vector3> Split(List<Vector3> polygon, Vector3 a, Vector3 b, List<List<Vector3>> remaining)
        {
            if (polygon.Count < 3) return polygon;
            var inside = new List<Vector3>();
            var outside = new List<Vector3>();
            var previous = polygon[polygon.Count - 1];
            double previousDistance = Side(a, b, previous);
            foreach (var point in polygon)
            {
                double distance = Side(a, b, point);
                if ((distance >= 0.0) != (previousDistance >= 0.0))
                {
                    var seam = Vector3.LerpUnclamped(previous, point, (float)(previousDistance / (previousDistance - distance)));
                    inside.Add(seam);
                    outside.Add(seam);
                }
                (distance >= 0.0 ? inside : outside).Add(point);
                previous = point;
                previousDistance = distance;
            }
            if (outside.Count >= 3) remaining.Add(outside);
            return inside;
        }

        static Vector4 Bounds(Vector3 a, Vector3 b, Vector3 c)
        {
            var lower = Vector3.Min(a, Vector3.Min(b, c));
            var upper = Vector3.Max(a, Vector3.Max(b, c));
            return new Vector4(lower.x, lower.z, upper.x, upper.z);
        }

        static bool Intersects(Vector4 a, Vector4 b) =>
            a.x <= b.z && a.z >= b.x && a.y <= b.w && a.w >= b.y;

        static double Side(Vector3 a, Vector3 b, Vector3 point) =>
            ((double)b.z - a.z) * ((double)point.x - a.x) - ((double)b.x - a.x) * ((double)point.z - a.z);
    }
}
