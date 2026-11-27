using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class TrackPreview
{
    private const string TrackGrassMaskFolder = "Startup/TrackGrassMasks/";
    private readonly List<(Vector3[] Vertices, int[] Triangles, bool Racing)> trackGrassPaving = new();
    private readonly List<(Renderer Renderer, int Slot)> trackGrassSlots = new();
    private Texture2D generatedTrackGrassMask;
    private bool collectTrackGrass;

    [Serializable]
    private sealed class TrackGrassLayouts
    {
        public TrackGrassLayout[] entries;
    }

    [Serializable]
    private sealed class TrackGrassLayout
    {
        public int id;
        public float minX, minZ, sizeX, sizeZ, signX, signZ;
    }

    private void RecordTrackGrassPavement(string surface, Vector3[] vertices, int[] triangles)
    {
        if (!collectTrackGrass)
            return;

        bool racing = surface == "Road surface";
        if (racing || surface is "Flat apron" or "Pit road" or "Pit entrance" or "Pit exit" or
            "Bristol Turn 4 entry" or "Bristol Turn 3 exit" or "Emergency vehicle paved bays" ||
            surface.EndsWith(" garage motor court", StringComparison.Ordinal) ||
            surface.EndsWith(" garage entry road", StringComparison.Ordinal) ||
            surface.EndsWith(" garage exit road", StringComparison.Ordinal) ||
            surface.EndsWith(" garage access road", StringComparison.Ordinal) ||
            surface.EndsWith(" concrete threshold", StringComparison.Ordinal) ||
            surface.Contains("pit work pad", StringComparison.OrdinalIgnoreCase) ||
            surface.Contains("pit walkway", StringComparison.OrdinalIgnoreCase))
            trackGrassPaving.Add((vertices, triangles, racing));
    }

    private void RecordTrackGrassScenery(Renderer renderer, int slot, Material source, bool includePavement)
    {
        if (!collectTrackGrass)
            return;

        if (source.name.EndsWith(" grass", StringComparison.OrdinalIgnoreCase))
            trackGrassSlots.Add((renderer, slot));

        if (!includePavement || (!source.name.EndsWith(" asphalt", StringComparison.OrdinalIgnoreCase) &&
            source.name is not ("Pit and service pavement" or "Racing concrete" or "Concrete pits" or "North Wilkesboro paved infield")))
            return;

        var mesh = renderer is MeshRenderer && renderer.TryGetComponent<MeshFilter>(out var filter) ? filter.sharedMesh : null;
        if (mesh == null || !mesh.isReadable || slot >= mesh.subMeshCount || mesh.GetTopology(slot) != MeshTopology.Triangles)
            return;

        Matrix4x4 sceneryToTrack = transform.worldToLocalMatrix * renderer.localToWorldMatrix;
        var points = mesh.vertices;
        for (int i = 0; i < points.Length; i++)
            points[i] = sceneryToTrack.MultiplyPoint3x4(points[i]);
        trackGrassPaving.Add((points, mesh.GetTriangles(slot), false));
    }

    private void FinishTrackGrass(Material originalGrass)
    {
        if (!collectTrackGrass)
            return;

        collectTrackGrass = false;
        try
        {
            var shader = Resources.Load<Shader>("Startup/LasVegasGrass");
            if (shader == null || !shader.isSupported)
                return;

            var worldToTrack = transform.worldToLocalMatrix;
            var worldToSource = worldToTrack;
            var finish = TrackPoint(definition.StartFinishFraction * lapLength, 0f, out var forward, out _);
            generatedTrackGrassMask = TrackGrassLayoutMask.Build(trackGrassPaving, definition.majorStyle == "Oval",
                new Vector2(finish.x, finish.z), new Vector2(forward.x, forward.z), out Vector4 generatedBounds);

            Texture2D mask = generatedTrackGrassMask;
            Vector4 bounds = generatedBounds;
            var layout = importedTrackGeometry ? FindImportedGrassLayout() : null;
            if (layout != null)
            {
                var imported = Resources.Load<Texture2D>(TrackGrassMaskFolder + definition.id);
                if (imported != null)
                {
                    Vector3 origin = customLayout.ToSourceSpace(Vector3.zero);
                    float scale = (customLayout.ToSourceSpace(Vector3.right) - origin).x;
                    var mirror = new Vector3(layout.signX, 1f, layout.signZ);
                    worldToSource = Matrix4x4.TRS(Vector3.Scale(origin, mirror), Quaternion.identity,
                        new Vector3(scale * mirror.x, 1f, scale * mirror.z)) * worldToTrack;
                    bounds = new Vector4(layout.minX, layout.minZ, 1f / layout.sizeX, 1f / layout.sizeZ);
                    mask = imported;
                }
            }

            if (mask == null)
                return;

            var material = new Material(shader) { name = definition.name + " turf" };
            material.SetFloat("_UseLayoutMask", 1f);
            material.SetMatrix("_WorldToTrackSource", worldToSource);
            material.SetTexture("_GroundMask", mask);
            material.SetVector("_GroundMaskBounds", bounds);
            if (mask != generatedTrackGrassMask && generatedTrackGrassMask != null)
            {
                material.SetFloat("_UseExtraWear", 1f);
                material.SetMatrix("_WorldToTrackGrass", worldToTrack);
                material.SetTexture("_ExtraWearMask", generatedTrackGrassMask);
                material.SetVector("_ExtraWearBounds", generatedBounds);
            }
            materials.Add(material);

            foreach (Transform child in transform)
            {
                if (child.TryGetComponent<MeshRenderer>(out var renderer) && renderer.sharedMaterial == originalGrass)
                    renderer.sharedMaterial = material;
            }
            foreach (var (renderer, slot) in trackGrassSlots)
            {
                var slots = renderer.sharedMaterials;
                slots[slot] = material;
                renderer.sharedMaterials = slots;
            }
        }
        finally
        {
            trackGrassPaving.Clear();
            trackGrassSlots.Clear();
        }
    }

    private TrackGrassLayout FindImportedGrassLayout()
    {
        var data = Resources.Load<TextAsset>(TrackGrassMaskFolder + "Layouts");
        var layouts = data == null ? null : JsonUtility.FromJson<TrackGrassLayouts>(data.text)?.entries;
        if (layouts != null)
        {
            foreach (var entry in layouts)
            {
                if (entry.id == definition.id && entry.sizeX > 0f && entry.sizeZ > 0f)
                    return entry;
            }
        }
        return null;
    }
}
