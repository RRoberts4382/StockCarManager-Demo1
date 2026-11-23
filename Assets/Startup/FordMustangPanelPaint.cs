using System.Collections.Generic;
using UnityEngine;

internal static class FordMustangPanelPaint
{
    private static readonly Dictionary<(Mesh Source, int Slot), Mesh> PaintMeshes = new();

    internal static Material Apply(MeshRenderer renderer, int slot, Material source, Material body)
    {
        bool paintable = renderer.name is "Front Bumper" or "Front Bumper Aero Ducts" or "Rear Bumper" or
            "Rear Quarter Panel" or "Rear Quarter Panel Right Pit" or "Inverted";
        if (!paintable)
            return source;

        string detailResource = source.name switch
        {
            "FordMustang_Body" => "FordMustang/FordBodyDetails",
            "FordMustang_BodyRight" => "FordMustang/FordBodyRightDetails",
            _ => null
        };
        if (detailResource == null || !renderer.TryGetComponent<MeshFilter>(out var filter) ||
            filter.sharedMesh == null || !filter.sharedMesh.isReadable || slot >= filter.sharedMesh.subMeshCount)
            return source;

        var details = Resources.Load<Material>(detailResource);
        if (details == null)
            return source;

        var key = (Source: filter.sharedMesh, Slot: slot);
        if (!PaintMeshes.TryGetValue(key, out var mesh) || mesh == null)
        {
            var indices = key.Source.GetTriangles(slot);
            mesh = Object.Instantiate(key.Source);
            mesh.name = key.Source.name + " Team Paint";
            mesh.hideFlags = HideFlags.HideAndDontSave;
            mesh.subMeshCount = 1;
            mesh.SetTriangles(indices, 0, false);
            PaintMeshes[key] = mesh;
        }

        // Leave original UVs on the detail layer and project the team's livery only onto the paint layer.
        var panel = new GameObject("Team Paint") { layer = renderer.gameObject.layer };
        panel.transform.SetParent(renderer.transform, false);
        panel.AddComponent<MeshFilter>().sharedMesh = mesh;
        var paintedPanel = panel.AddComponent<MeshRenderer>();
        paintedPanel.sharedMaterial = body;
        paintedPanel.receiveShadows = renderer.receiveShadows;
        paintedPanel.shadowCastingMode = renderer.shadowCastingMode;
        paintedPanel.lightProbeUsage = renderer.lightProbeUsage;
        paintedPanel.reflectionProbeUsage = renderer.reflectionProbeUsage;
        return details;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPaintMeshes()
    {
        foreach (var mesh in PaintMeshes.Values)
            if (mesh != null)
                Object.Destroy(mesh);
        PaintMeshes.Clear();
    }
}
