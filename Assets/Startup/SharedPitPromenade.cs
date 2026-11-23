using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class TrackPreview
{
    private const float PitPromenadeWidth = 3.048f;
    private const float PitPromenadeWorkDepth = 11f;
    private const float PitPromenadeOpenEnd = 28f;

    private Vector3 PitPromenadePoint(float fraction, float infieldOffset, out Vector3 forward, out Vector3 infield)
    {
        Vector3 center = PitPosition(pitRoute[1], fraction, pitHeights[1], pitHeights[2]);
        forward = Vector3.ProjectOnPlane(PitTangent(pitRoute[1], fraction, pitHeights[1], pitHeights[2]), Vector3.up).normalized;
        infield = Vector3.Cross(Vector3.up, forward) * pitWallSide;
        return center + infield * infieldOffset;
    }

    private void AddSharedPitPromenade()
    {
        float length = pitLengths[1];
        if (length <= .01f)
            return;

        bool hasWorkPaving = importedTrackGeometry && definition.id == IowaTrackId;
        bool hasWalkway = false;
        bool hasVendors = false;
        bool hasBarrels = false;
        var obstructions = new List<Bounds>();
        foreach (var renderer in GetComponentsInChildren<MeshRenderer>())
        {
            if (!renderer.enabled)
                continue;

            string name = renderer.name.ToLowerInvariant();
            hasWorkPaving |= name.Contains("pit work concrete") || name.Contains("concrete pit work") ||
                name.StartsWith("pit support apron", StringComparison.Ordinal);
            bool pitWalk = name.Contains("pit walkway") || name.Contains("pit walk ") || name.Contains("pit promenade");
            hasWalkway |= name == "shared pit promenade walkway" || name.EndsWith("pit walkway", StringComparison.Ordinal) ||
                name.StartsWith("pit walkway concrete", StringComparison.Ordinal);
            // These two authored venue meshes bundle both carts and bins under a single label.
            bool furnishedWalk = name is "iowa pit walkway amenities" or "michigan pit promenade fixtures and scoring pylon" or
                "shared pit promenade amenities";
            hasVendors |= furnishedWalk || pitWalk && name.Contains("concession");
            hasBarrels |= furnishedWalk || pitWalk && (name.Contains("barrel") || name.Contains("bins"));

            if (name.Contains("garage") && (name.Contains("road") || name.Contains("court") ||
                name.Contains("roof") || name.Contains("wall")) ||
                name.Contains("infield loop") || name.Contains("emergency access") ||
                name is "emergency vehicle access road" or "emergency vehicle paved bays" ||
                name.Contains("service road") || !furnishedWalk && name.Contains("scoring pylon"))
                obstructions.Add(renderer.bounds);
        }

        if (hasWorkPaving && hasWalkway && hasVendors && hasBarrels)
            return;

        float halfPit = definition.pitRoad.widthMeters * .5f;
        float workEdge = halfPit + PitWallThickness;
        // Phoenix already has a ten-foot walkway with its inner edge 14.6 m from the pit datum.
        float walkStart = hasWalkway && definition.id == PhoenixTrackId ? 14.6f : workEdge + PitPromenadeWorkDepth;
        float walkEnd = walkStart + PitPromenadeWidth;
        var connectorLines = new List<Vector3[]>(4)
        {
            SampleConnector(pitRoute[0], pitLengths[0], pitHeights[0], pitHeights[1]),
            SampleConnector(pitRoute[2], pitLengths[2], pitHeights[2], pitHeights[3])
        };
        if (bristolFrontEntry != null)
            connectorLines.Add(SampleConnector(bristolFrontEntry, bristolFrontEntry.Length, bristolFrontEntryHeight, 0f));
        if (bristolBackExit != null)
            connectorLines.Add(SampleConnector(bristolBackExit, bristolBackExitLength, 0f, bristolBackExitHeight));

        if (!hasWorkPaving)
            Pave("Shared pit work concrete", workEdge + .02f, walkStart, -.006f, new Color32(157, 154, 146, 255));
        if (!hasWalkway)
            Pave("Shared pit promenade walkway", walkStart, walkEnd, -.002f, new Color32(175, 169, 156, 255));

        if (!hasVendors || !hasBarrels)
        {
            var furnishings = new IowaPitWorkMesh();
            float firstSite = PitPromenadeOpenEnd + 2f;
            float nextVendor = firstSite;
            float nextBarrel = firstSite + 3f;
            int placed = 0;
            for (float station = firstSite; station <= length - firstSite; station += 2f)
            {
                float fraction = station / length;
                if (!hasVendors && station >= nextVendor)
                {
                    Vector3 floor = PitPromenadePoint(fraction, walkEnd + 1.55f, out _, out var infield);
                    if (ClearSite(floor, 2.35f))
                    {
                        Quaternion facing = Quaternion.LookRotation(-infield);
                        PaveBay(floor, facing, 3.5f, 3.1f);
                        AddIowaConcessionCart(furnishings, floor, facing, placed % 2 == 0);
                        placed++;
                        nextVendor = station + 48f;
                        nextBarrel = Mathf.Max(nextBarrel, station + 3f);
                    }
                }
                if (!hasBarrels && station >= nextBarrel)
                {
                    Vector3 floor = PitPromenadePoint(fraction, walkEnd + 1.05f, out _, out var infield);
                    if (ClearSite(floor, 1.2f))
                    {
                        PaveBay(floor, Quaternion.LookRotation(-infield), 1.1f, 2.1f);
                        AddIowaTrashBarrel(furnishings, floor);
                        placed++;
                        nextBarrel = station + 18f;
                        nextVendor = Mathf.Max(nextVendor, station + 3f);
                    }
                }
            }
            if (placed > 0)
                AddIowaFanFurnishings(furnishings);

            void PaveBay(Vector3 floor, Quaternion facing, float width, float depth) =>
                furnishings.Box(floor - Vector3.up * .045f, new Vector3(width, .09f, depth), facing, IowaPitWorkMesh.Part.Light);
        }

        void Pave(string name, float near, float far, float height, Color color)
        {
            // Leave rooms at both ends for the connectors and use separate spans across access roads.
            float endMargin = Mathf.Min(12f, length * .07f);
            int spans = Mathf.Max(1, Mathf.CeilToInt((length - 2f * endMargin) / 4f));
            var vertices = new Vector3[(spans + 1) * 2];
            var triangles = new List<int>(spans * 6);
            bool previousClear = false;
            for (int i = 0; i <= spans; i++)
            {
                float fraction = Mathf.Lerp(endMargin, length - endMargin, i / (float)spans) / length;
                Vector3 center = PitPromenadePoint(fraction, near, out _, out var infield) + Vector3.up * height;
                Vector3 edge = center + infield * (far - near);
                vertices[2 * i] = center;
                vertices[2 * i + 1] = edge;
                bool clear = ClearSite(center, .15f) && ClearSite(edge, .15f) &&
                    ClearSite(Vector3.Lerp(center, edge, .5f), .15f);
                if (i > 0 && clear && previousClear)
                {
                    int start = 2 * (i - 1);
                    if (pitWallSide > 0f)
                    {
                        triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
                        triangles.Add(start + 1); triangles.Add(start + 2); triangles.Add(start + 3);
                    }
                    else
                    {
                        triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
                        triangles.Add(start + 1); triangles.Add(start + 3); triangles.Add(start + 2);
                    }
                }
                previousClear = clear;
            }
            if (triangles.Count > 0)
                AddRoadMesh(name, MakeMaterial(color), vertices, triangles.ToArray());
        }

        bool ClearSite(Vector3 point, float radius)
        {
            Vector3 world = transform.TransformPoint(point);
            foreach (var bounds in obstructions)
                if (world.x >= bounds.min.x - radius && world.x <= bounds.max.x + radius &&
                    world.z >= bounds.min.z - radius && world.z <= bounds.max.z + radius)
                    return false;

            float connectorClearance = halfPit + radius;
            foreach (var line in connectorLines)
                for (int i = 1; i < line.Length; i++)
                    if (HorizontalSegmentDistanceSquared(point, line[i - 1], line[i]) < connectorClearance * connectorClearance)
                        return false;

            float station = TrackUtilityGeometry.ClosestMainDistance(customLayout, lapLength, point);
            Vector3 track = TrackPoint(station, 0f, out var heading, out _);
            Vector3 inward = Vector3.Cross(Vector3.up, heading).normalized * customLayout.InsideSign;
            Vector2 widths = RoadWidths(station);
            float offset = Vector3.Dot(point - track, inward);
            return offset > widths.x * .5f + widths.y + radius || offset < -widths.x * .5f - radius;
        }

        Vector3[] SampleConnector(TrackPitSpline spline, float meters, float start, float end)
        {
            int spans = Mathf.Max(1, Mathf.CeilToInt(meters / 3f));
            var line = new Vector3[spans + 1];
            for (int i = 0; i <= spans; i++)
                line[i] = PitPosition(spline, i / (float)spans, start, end);
            return line;
        }
    }
}
