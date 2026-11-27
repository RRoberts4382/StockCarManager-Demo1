using UnityEngine;

public sealed partial class TrackPreview
{
    private void ApplyLasVegasGrass(GameObject venue)
    {
        var turf = System.Array.Find(venue.GetComponentsInChildren<MeshRenderer>(),
            renderer => renderer.name == "Las Vegas grass");
        var shader = Resources.Load<Shader>("Startup/LasVegasGrass");
        var mask = Resources.Load<Texture2D>("Startup/LasVegasGrassMask");
        if (turf == null || shader == null || !shader.isSupported || mask == null)
        {
            Debug.LogWarning("Las Vegas turf is unavailable. Using the imported ground material.", this);
            return;
        }

        var material = new Material(shader) { name = "Las Vegas turf" };
        Vector3 sourceOrigin = customLayout.ToSourceSpace(Vector3.zero);
        float inverseScale = (customLayout.ToSourceSpace(Vector3.right) - sourceOrigin).x;
        var trackToSource = Matrix4x4.TRS(sourceOrigin, Quaternion.identity,
            new Vector3(inverseScale, 1f, inverseScale));
        material.SetMatrix("_WorldToTrackSource", trackToSource * transform.worldToLocalMatrix);
        material.SetTexture("_GroundMask", mask);
        materials.Add(material);

        turf.sharedMaterial = material;
        // The larger camera-following ground makes the horizon continuous beyond the imported mesh.
        grassGround.GetComponent<MeshRenderer>().sharedMaterial = material;
    }
}
