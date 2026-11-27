using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class TrackPreview
{
    private const int DoverGrooveSamples = 1024;
    private Texture2D doverGrooveTexture;

    private void ApplyDoverTrackFinish(GameObject venue)
    {
        var shader = Resources.Load<Shader>("Startup/DoverTrackFinish");
        if (shader == null || !shader.isSupported)
            return;

        var groove = Resources.Load<TextAsset>("Startup/DoverGroove");
        if (groove != null && groove.bytes.Length == DoverGrooveSamples &&
            SystemInfo.SupportsTextureFormat(TextureFormat.R8))
        {
            doverGrooveTexture = new Texture2D(DoverGrooveSamples, 1, TextureFormat.R8, false, true)
            {
                name = "Dover preferred-line wear",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };
            doverGrooveTexture.LoadRawTextureData(groove.bytes);
            doverGrooveTexture.Apply(false, true);
        }

        Vector3 origin = customLayout.ToSourceSpace(Vector3.zero);
        float scale = (customLayout.ToSourceSpace(Vector3.right) - origin).x;
        Matrix4x4 worldToSource = Matrix4x4.TRS(origin, Quaternion.identity, new Vector3(scale, 1f, scale)) *
            transform.worldToLocalMatrix;

        var asphalt = Finish("Dover asphalt", new Color(.224f, .237f, .249f), 0f);
        var harness = Finish("Dover weathered harness asphalt", new Color(.335f, .358f, .374f), 0f);
        var concrete = Finish("Dover racing concrete", new Color(.53f, .52f, .495f), 1f);
        var wall = Finish("Dover retaining concrete", new Color(.68f, .68f, .65f), 2f);
        var outerWall = Finish("Dover outside retaining concrete", new Color(.68f, .68f, .65f), 3f);
        var tonedScenery = new Dictionary<Material, Material>();
        foreach (var renderer in venue.GetComponentsInChildren<MeshRenderer>(true))
        {
            string name = renderer.name;
            if (name == "Racing surface")
                renderer.sharedMaterial = concrete;
            else if (name == "Concrete slab contraction seams")
                renderer.enabled = false; // The racing surface shader draws these joints.
            else if (name == "Harness track")
                renderer.sharedMaterial = harness;
            else if (name.StartsWith("Outside track wall", StringComparison.Ordinal))
                renderer.sharedMaterial = outerWall;
            else if (name.StartsWith("Inside apron wall", StringComparison.Ordinal) ||
                     name.StartsWith("Inside track wall", StringComparison.Ordinal) ||
                     name.StartsWith("Infield perimeter wall", StringComparison.Ordinal) ||
                     name.StartsWith("Pit continuous stall-side wall", StringComparison.Ordinal) ||
                     name.StartsWith("Stall-side pit wall", StringComparison.Ordinal) ||
                     name.StartsWith("Pit track divider", StringComparison.Ordinal) ||
                     name.StartsWith("Trackside pit divider", StringComparison.Ordinal))
                renderer.sharedMaterial = wall;
            else if (name.Contains("asphalt", StringComparison.OrdinalIgnoreCase) ||
                     name is "Infield roads" or "Car parking areas" or "Hauler parking areas" or
                         "Protected pit work areas" or "Walkway behind pit work areas" or
                         "Paddock crossing through the pit wall" or "Emergency access")
                renderer.sharedMaterial = asphalt;
            else if (name.StartsWith("Dover grandstand ", StringComparison.Ordinal) ||
                     name.StartsWith("Exterior building ", StringComparison.Ordinal))
            {
                var slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    var source = slots[i];
                    if (source == null || !source.HasProperty("_BaseColor") ||
                        source.GetColor("_BaseColor").grayscale < .6f)
                        continue;
                    if (!tonedScenery.TryGetValue(source, out var toned))
                    {
                        var color = source.GetColor("_BaseColor");
                        toned = new Material(source) { name = source.name + " (Dover)" };
                        toned.SetColor("_BaseColor", new Color(color.r * .68f, color.g * .72f, color.b * .76f, color.a));
                        tonedScenery.Add(source, toned);
                        materials.Add(toned);
                    }
                    slots[i] = toned;
                }
                renderer.sharedMaterials = slots;
            }
        }

        Material Finish(string name, Color tint, float kind)
        {
            var material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", tint);
            material.SetFloat("_FinishKind", kind);
            material.SetMatrix("_WorldToTrackSource", worldToSource);
            if (kind == 1f && doverGrooveTexture != null)
            {
                material.SetTexture("_Groove", doverGrooveTexture);
                material.SetFloat("_UseGroove", 1f);
            }
            materials.Add(material);
            return material;
        }
    }

    private void ReleaseDoverTrackFinish()
    {
        if (doverGrooveTexture != null)
            Destroy(doverGrooveTexture);
    }
}
