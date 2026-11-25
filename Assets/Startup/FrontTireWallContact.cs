using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class TrackPreview
{
    // Position is the center of the nose. The result is travel along the fixed plan heading.
    private bool TryFindFrontTireWallContact(Vector3 position, Vector3 direction, float carHalfWidth,
        out float impactDistance)
    {
        impactDistance = 0f;
        direction.y = 0f;
        if (direction.sqrMagnitude < .0001f)
            return false;

        direction.Normalize();
        var across = Vector3.Cross(Vector3.up, direction);
        const float searchDistance = 450f;
        impactDistance = searchDistance;
        if (TryFindAuthoredTireWallContact(position, direction, across, carHalfWidth, ref impactDistance))
            return true;

        // Some ovals have only a sampled outer edge. Road courses can have open runoff.
        if (definition.majorStyle != "Oval")
            return false;

        float outside = -(customLayout?.InsideSign ?? -1f);
        int samples = Mathf.Clamp(Mathf.CeilToInt(lapLength / 4f), 96, 4096);
        Vector3 previous = TrackPoint(0f, outside * RoadWidths(0f).x * .5f, out _, out _);
        bool hit = false;
        for (int i = 1; i <= samples; i++)
        {
            float station = lapLength * i / samples;
            Vector3 next = TrackPoint(station, outside * RoadWidths(station).x * .5f, out _, out _);
            if (TryClipTireWallEdge(previous - position, next - position, direction, across, carHalfWidth,
                out float distance) && distance < impactDistance)
            {
                impactDistance = distance;
                hit = true;
            }
            previous = next;
        }
        return hit;
    }

    private bool TryFindAuthoredTireWallContact(Vector3 position, Vector3 direction, Vector3 across,
        float carHalfWidth, ref float impactDistance)
    {
        // The venue meshes are visual only. Cook the nearby wall candidates for this one sweep.
        var walls = new List<MeshCollider>();
        var temporary = new List<MeshCollider>();
        Vector3 worldOrigin = transform.TransformPoint(position);
        Vector3 worldAcross = transform.TransformVector(across) * carHalfWidth;
        Vector3 worldDirection = transform.TransformVector(direction);
        float scale = worldDirection.magnitude;
        worldDirection /= scale;
        try
        {
            foreach (var filter in GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null || !IsTireImpactBarrier(filter.name) ||
                    !filter.TryGetComponent<MeshRenderer>(out var renderer) || !renderer.enabled)
                    continue;

                Bounds bounds = renderer.bounds;
                Vector3 flatOrigin = new Vector3(worldOrigin.x, bounds.center.y, worldOrigin.z);
                bounds.Expand(new Vector3(2f * Mathf.Abs(worldAcross.x), 0f, 2f * Mathf.Abs(worldAcross.z)));
                if (!bounds.IntersectRay(new Ray(flatOrigin, worldDirection), out float reach) ||
                    reach > impactDistance * scale)
                    continue;

                if (!filter.TryGetComponent<MeshCollider>(out var wall) || !wall.enabled ||
                    wall.sharedMesh != filter.sharedMesh)
                {
                    wall = filter.gameObject.AddComponent<MeshCollider>();
                    temporary.Add(wall);
                    wall.sharedMesh = filter.sharedMesh;
                }
                walls.Add(wall);
            }

            if (walls.Count == 0)
                return false;

            Physics.SyncTransforms();
            float nearest = impactDistance * scale;
            bool hit = false;
            foreach (var wall in walls)
            {
                Bounds bounds = wall.bounds;
                int heights = Mathf.Max(1, Mathf.CeilToInt(bounds.size.y / .4f));
                for (int side = -2; side <= 2; side++)
                {
                    Vector3 origin = worldOrigin + worldAcross * (side * .5f);
                    // Multiple heights make the cast a plan-view test even on banking and grade changes.
                    for (int height = 0; height < heights; height++)
                    {
                        origin.y = Mathf.Lerp(bounds.min.y, bounds.max.y, (height + .5f) / heights);
                        if (wall.Raycast(new Ray(origin, worldDirection), out var contact, nearest))
                        {
                            nearest = contact.distance;
                            hit = true;
                        }
                    }
                }
            }
            impactDistance = nearest / scale;
            return hit;
        }
        finally
        {
            foreach (var wall in temporary)
            {
                wall.enabled = false;
                if (Application.isPlaying)
                    Destroy(wall);
                else
                    DestroyImmediate(wall);
            }
        }
    }

    private static bool IsTireImpactBarrier(string name) =>
        name.StartsWith("Outside ", StringComparison.OrdinalIgnoreCase) &&
            name.Contains("wall", StringComparison.OrdinalIgnoreCase) &&
            !name.Contains("walkway", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Iowa continuous outside concrete wall", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Pit separator wall", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Track-side pit wall", StringComparison.OrdinalIgnoreCase) ||
        IsSharedFanInnerWall(name) || IsSharedFanPitWall(name);

    private static bool TryClipTireWallEdge(Vector3 first, Vector3 last, Vector3 heading, Vector3 across,
        float halfWidth, out float distance)
    {
        distance = 0f;
        float startSide = Vector3.Dot(first, across);
        float endSide = Vector3.Dot(last, across);
        if (Mathf.Min(startSide, endSide) > halfWidth || Mathf.Max(startSide, endSide) < -halfWidth)
            return false;

        float enter = 0f, leave = 1f;
        float span = endSide - startSide;
        if (Mathf.Abs(span) > .0001f)
        {
            float a = (-halfWidth - startSide) / span;
            float b = (halfWidth - startSide) / span;
            enter = Mathf.Max(0f, Mathf.Min(a, b));
            leave = Mathf.Min(1f, Mathf.Max(a, b));
        }

        float startForward = Vector3.Dot(first, heading);
        float endForward = Vector3.Dot(last, heading);
        float firstInPath = Mathf.Lerp(startForward, endForward, enter);
        float lastInPath = Mathf.Lerp(startForward, endForward, leave);
        distance = Mathf.Max(0f, Mathf.Min(firstInPath, lastInPath));
        return Mathf.Max(firstInPath, lastInPath) >= 0f;
    }
}
