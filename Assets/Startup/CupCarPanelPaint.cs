using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

internal static class CupCarPanelPaint
{
    private static readonly Dictionary<(Mesh Source, int Slot), Mesh> PaintMeshes = new();

    internal static Material Apply(MeshRenderer renderer, int slot, Material source, Material body)
    {
        if (source.name.StartsWith("FordMustang_", System.StringComparison.Ordinal) &&
            renderer.name is not ("Front Bumper" or "Front Bumper Aero Ducts" or "Rear Bumper" or
                "Rear Quarter Panel" or "Rear Quarter Panel Right Pit" or "Inverted"))
            return source;

        string detailResource = source.name switch
        {
            "FordMustang_Body" => "FordMustang/FordBodyDetails",
            "FordMustang_BodyRight" => "FordMustang/FordBodyRightDetails",
            "ChevroletCamaro_Body" => "ChevroletCamaro/CamaroBodyDetails",
            "ChevroletCamaro_BodyRight" => "ChevroletCamaro/CamaroBodyRightDetails",
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
            mesh = CreatePaintMesh(key.Source, slot);
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

    private static Mesh CreatePaintMesh(Mesh source, int slot)
    {
        var indices = source.GetTriangles(slot);
        // A small paint slot on the chassis or cockpit should not duplicate the rest of the vertex buffer.
        bool compact = indices.Length > 0 && indices.Length < source.vertexCount / 2;
        var mesh = compact ? ExtractPaintMesh(source, indices) : Object.Instantiate(source);
        if (!compact)
        {
            mesh.subMeshCount = 1;
            mesh.SetTriangles(indices, 0, false);
        }
        mesh.name = source.name + " Team Paint";
        mesh.hideFlags = HideFlags.HideAndDontSave;
        return mesh;
    }

    private static Mesh ExtractPaintMesh(Mesh source, int[] indices)
    {
        var remap = new Dictionary<int, int>();
        var originalVertices = new List<int>();
        for (int i = 0; i < indices.Length; i++)
        {
            int index = indices[i];
            if (!remap.TryGetValue(index, out int mapped))
            {
                mapped = originalVertices.Count;
                remap.Add(index, mapped);
                originalVertices.Add(index);
            }
            indices[i] = mapped;
        }

        using var sourceData = Mesh.AcquireReadOnlyMeshData(source);
        var outputData = Mesh.AllocateWritableMeshData(1);
        try
        {
            var original = sourceData[0];
            var output = outputData[0];
            output.SetVertexBufferParams(originalVertices.Count, source.GetVertexAttributes());
            for (int stream = 0; stream < source.vertexBufferCount; stream++)
            {
                int stride = source.GetVertexBufferStride(stream);
                var originalBuffer = original.GetVertexData<byte>(stream);
                var outputBuffer = output.GetVertexData<byte>(stream);
                for (int i = 0; i < originalVertices.Count; i++)
                    NativeArray<byte>.Copy(originalBuffer, originalVertices[i] * stride, outputBuffer, i * stride, stride);
            }

            var indexFormat = originalVertices.Count <= ushort.MaxValue ? IndexFormat.UInt16 : IndexFormat.UInt32;
            output.SetIndexBufferParams(indices.Length, indexFormat);
            if (indexFormat == IndexFormat.UInt16)
            {
                var buffer = output.GetIndexData<ushort>();
                for (int i = 0; i < indices.Length; i++)
                    buffer[i] = (ushort)indices[i];
            }
            else
            {
                output.GetIndexData<int>().CopyFrom(indices);
            }

            output.subMeshCount = 1;
            output.SetSubMesh(0, new SubMeshDescriptor(0, indices.Length, MeshTopology.Triangles));
            var mesh = new Mesh();
            Mesh.ApplyAndDisposeWritableMeshData(outputData, mesh);
            outputData = default;
            mesh.RecalculateBounds();
            return mesh;
        }
        finally
        {
            if (outputData.Length > 0)
                outputData.Dispose();
        }
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
