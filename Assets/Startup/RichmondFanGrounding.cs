using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class TrackPreview
{
    private sealed class RichmondWalkGround : IDisposable
    {
        private readonly Transform track;
        private readonly List<MeshCollider> surfaces = new();

        internal RichmondWalkGround(Transform track)
        {
            this.track = track;
            foreach (var filter in track.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null || filter.name is not (
                    "Richmond protected FanGrounds pedestrian paths" or "Richmond frontstretch concrete concourse" or
                    "Richmond pit walkway" or "Richmond Victory Lane pavilion" or "Richmond concrete perimeter walkway" or
                    "Richmond frontstretch grandstand concourses" or "Richmond pit walkway amenity bays" or
                    "Richmond pit work concrete" or "Pit walkway access west" or "Pit walkway access east"))
                    continue;

                var collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                surfaces.Add(collider);
            }
            Physics.SyncTransforms();
        }

        internal float[] Sample(Vector3 start, Vector3 end)
        {
            const float spacing = .25f;
            const float probeHeight = .08f;
            int segments = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(start, end) / spacing));
            var heights = new float[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                Vector3 point = Vector3.Lerp(start, end, i / (float)segments);
                Vector3 origin = track.TransformPoint(point) + Vector3.up * probeHeight;
                var ray = new Ray(origin, Vector3.down);
                float nearest = probeHeight * 2f;
                heights[i] = point.y;
                foreach (var surface in surfaces)
                {
                    // Probe close to the authored grade. Even where a building
                    // pad overlaps this route, its upper stories are excluded.
                    if (!surface.Raycast(ray, out var hit, nearest) || hit.normal.y < .5f)
                        continue;

                    nearest = hit.distance;
                    heights[i] = track.InverseTransformPoint(hit.point).y;
                }
            }
            return heights;
        }

        public void Dispose()
        {
            foreach (var surface in surfaces)
            {
                surface.enabled = false;
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(surface);
                else
                    UnityEngine.Object.DestroyImmediate(surface);
            }
        }
    }
}