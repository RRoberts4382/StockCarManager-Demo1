using System;
using System.Collections.Generic;
using UnityEngine;

internal static class ImportedGrandstandSeating
{
    private const int TrackSamples = 192;

    internal static void Register(int trackId, GameObject model, Transform track, GrandstandAttendance attendance,
        float lapLength, Func<float, (Vector3 Position, Vector3 Outward)> sampleEdge)
    {
        var edge = new (Vector3 Position, Vector3 Outward)[TrackSamples];
        for (int i = 0; i < edge.Length; i++)
            edge[i] = sampleEdge(lapLength * i / edge.Length);

        foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!IsBench(trackId, filter.name) || filter.sharedMesh == null || !filter.sharedMesh.isReadable)
                continue;

            var section = RowCenterline(filter.sharedMesh, track.worldToLocalMatrix * filter.transform.localToWorldMatrix);
            if (section.Count < 2)
                continue;

            Vector3 center = section[section.Count / 2];
            int nearest = 0;
            float best = float.PositiveInfinity;
            for (int i = 0; i < edge.Length; i++)
            {
                Vector3 displacement = center - edge[i].Position;
                float distance = displacement.x * displacement.x + displacement.z * displacement.z;
                if (distance < best)
                {
                    best = distance;
                    nearest = i;
                }
            }

            RegisterRow(section, edge[nearest].Outward, "Section " + (nearest / 8 + 1),
                trackId == 29 ? .82f : .25f, attendance);
        }
    }

    private static bool IsBench(int track, string name) => track switch
    {
        28 => name.StartsWith("Bleacher row", StringComparison.Ordinal),
        29 => name.Contains("grandstand bench", StringComparison.OrdinalIgnoreCase),
        30 => name.StartsWith("Grandstand seating", StringComparison.Ordinal),
        31 => name.StartsWith("Grandstand seating tier", StringComparison.Ordinal),
        _ => false
    };

    private static List<Vector3> RowCenterline(Mesh mesh, Matrix4x4 toTrack)
    {
        // These authored bench surfaces are thin strips made from pairs of top triangles.
        // Their short edges cross the seat depth, even on a curved row. Midpoints follow the full row.
        var vertices = mesh.vertices;
        float highest = float.NegativeInfinity;
        var bounds = new Bounds(toTrack.MultiplyPoint3x4(vertices[0]), Vector3.zero);
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = toTrack.MultiplyPoint3x4(vertices[i]);
            highest = Mathf.Max(highest, vertices[i].y);
            bounds.Encapsulate(vertices[i]);
        }

        var centers = new HashSet<Vector3>();
        int[] indices = mesh.triangles;
        for (int i = 0; i < indices.Length; i += 3)
        {
            Vector3 a = vertices[indices[i]];
            Vector3 b = vertices[indices[i + 1]];
            Vector3 c = vertices[indices[i + 2]];
            if (Mathf.Min(a.y, Mathf.Min(b.y, c.y)) < highest - .025f)
                continue;

            float ab = (b - a).sqrMagnitude;
            float bc = (c - b).sqrMagnitude;
            float ca = (a - c).sqrMagnitude;
            if (ab < .99f * Mathf.Min(bc, ca))
                centers.Add((a + b) * .5f);
            else if (bc < .99f * Mathf.Min(ca, ab))
                centers.Add((b + c) * .5f);
            else if (ca < .99f * Mathf.Min(ab, bc))
                centers.Add((c + a) * .5f);
        }

        var row = new List<Vector3>(centers);
        bool readX = bounds.size.x > bounds.size.z;
        row.Sort((a, b) => (readX ? a.x : a.z).CompareTo(readX ? b.x : b.z));
        return row;
    }

    private static void RegisterRow(List<Vector3> centers, Vector3 outside, string label, float endClearance,
        GrandstandAttendance attendance)
    {
        float total = 0f;
        for (int i = 1; i < centers.Count; i++)
            total += Vector3.Distance(centers[i - 1], centers[i]);

        float spacing = GrandstandMeshBuilder.SeatSpacing;
        int remaining = Mathf.FloorToInt((total - endClearance * 2f) / spacing);
        if (remaining < 1)
            return;

        float next = (total - (remaining - 1) * spacing) * .5f;
        float traveled = 0f;
        for (int i = 1; i < centers.Count && remaining > 0; i++)
        {
            Vector3 a = centers[i - 1];
            Vector3 b = centers[i];
            float length = Vector3.Distance(a, b);
            float end = traveled + length;
            if (next < end && length > .001f)
            {
                int count = Mathf.Min(remaining, Mathf.CeilToInt((end - next) / spacing));
                Vector3 outward = Vector3.Cross(Vector3.up, b - a).normalized;
                if (Vector3.Dot(outward, outside) < 0f)
                    outward = -outward;
                var seats = new GrandstandMeshBuilder.SeatStrip(new Vector3(next - traveled - length * .5f, 0f, 0f), count);
                attendance.Register(label, seats, a, b, outward, outward, length);
                next += count * spacing;
                remaining -= count;
            }
            traveled = end;
        }
    }
}
