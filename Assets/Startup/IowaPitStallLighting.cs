using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public sealed partial class TrackPreview
{
    private readonly List<Light> iowaPitStallLights = new List<Light>();
    private Mesh iowaPitStallMastMesh, iowaPitStallHousingMesh, iowaPitStallLensMesh;
    private Material iowaPitStallMetal, iowaPitStallLens;

    internal bool SupportsIowaPitStallLighting => iowaPitStallLights.Count > 0;

    private void AddIowaPitStallLight(Transform parent, int number, Vector3 center, Vector3 travel,
        Vector3 infield, Vector3 pitBoxAim)
    {
        // Place the narrow mast in the stall's side margin, behind the open wall-side lane.
        Vector3 along = Vector3.ProjectOnPlane(travel, Vector3.up).normalized;
        Vector3 across = Vector3.ProjectOnPlane(infield, Vector3.up).normalized;
        float boxInset = Vector3.Dot(pitBoxAim - center, across);
        Vector3 basePosition = center + along * 3.28f + across * (boxInset + .70f);
        Quaternion mastHeading = Quaternion.LookRotation(-across, Vector3.up);
        Vector3 fixtureOffset = new Vector3(0f, 4.25f, .21f);
        Vector3 lightPosition = basePosition + mastHeading * fixtureOffset;
        Vector3 heading = pitBoxAim - lightPosition;

        EnsureIowaPitStallLightMeshes();

        var pole = CreateObject($"Iowa pit stall light pole {number}", parent).transform;
        pole.SetLocalPositionAndRotation(basePosition, mastHeading);
        IowaPitStallFixtureMesh(pole, iowaPitStallMastMesh, iowaPitStallMetal);

        var fixture = CreateObject($"Iowa pit stall lamp {number}", pole).transform;
        fixture.SetLocalPositionAndRotation(fixtureOffset, Quaternion.Inverse(mastHeading) *
            Quaternion.LookRotation(heading, Vector3.up));
        IowaPitStallFixtureMesh(fixture, iowaPitStallHousingMesh, iowaPitStallMetal);
        var lens = CreateObject("Iowa pit stall lamp lens", fixture).transform;
        IowaPitStallFixtureMesh(lens, iowaPitStallLensMesh, iowaPitStallLens);

        var spot = fixture.gameObject.AddComponent<Light>();
        spot.type = LightType.Spot;
        spot.color = new Color32(255, 237, 210, 255);
        spot.intensity = 40f;
        spot.range = 9f;
        spot.spotAngle = 63f;
        spot.innerSpotAngle = 40f;
        spot.shadows = LightShadows.None;
        spot.cullingMask = 1 << PreviewLayer;
        spot.GetUniversalAdditionalLightData().renderingLayers = IowaPitLightingLayer;
        spot.enabled = TrackSkyIsNight;
        iowaPitStallLights.Add(spot);
    }

    private void SetIowaPitStallLighting(bool night)
    {
        if (iowaPitStallLens == null)
            return;

        iowaPitStallLens.SetColor("_EmissionColor", night ? new Color(1f, .9f, .74f) * 1.65f : Color.black);
        if (night)
            iowaPitStallLens.EnableKeyword("_EMISSION");
        else
            iowaPitStallLens.DisableKeyword("_EMISSION");

        foreach (var spot in iowaPitStallLights)
            spot.enabled = night;
    }

    private void EnsureIowaPitStallLightMeshes()
    {
        if (iowaPitStallMastMesh != null)
            return;

        iowaPitStallMetal = MakeMaterial(new Color32(110, 120, 127, 255));
        iowaPitStallMetal.SetFloat("_Smoothness", .22f);
        iowaPitStallLens = MakeMaterial(new Color32(219, 218, 207, 255));
        SetIowaPitStallLighting(TrackSkyIsNight);

        const IowaPitWorkMesh.Part steel = IowaPitWorkMesh.Part.Primary;
        var mast = new IowaPitWorkMesh();
        mast.Box(Vector3.up * .021f, new Vector3(.27f, .042f, .27f), Quaternion.identity, steel);
        mast.Cylinder(Vector3.up * .04f, Vector3.up * .29f, .071f, .071f, steel);
        mast.Cylinder(Vector3.up * .285f, Vector3.up * 4.25f, .048f, .032f, steel);
        mast.Cylinder(Vector3.up * 4.25f, new Vector3(0f, 4.25f, .22f), .025f, .025f, steel);
        iowaPitStallMastMesh = mast.Build("Iowa pit stall mast geometry", 0, 1);

        var housing = new IowaPitWorkMesh();
        housing.Box(Vector3.zero, new Vector3(.31f, .15f, .14f), Quaternion.identity, steel);
        iowaPitStallHousingMesh = housing.Build("Iowa pit stall housing geometry", 0, 1);

        var lens = new IowaPitWorkMesh();
        lens.Box(Vector3.forward * .075f, new Vector3(.27f, .115f, .018f), Quaternion.identity, steel);
        iowaPitStallLensMesh = lens.Build("Iowa pit stall lens geometry", 0, 1);

        roadMeshes.Add(iowaPitStallMastMesh);
        roadMeshes.Add(iowaPitStallHousingMesh);
        roadMeshes.Add(iowaPitStallLensMesh);
    }

    private static void IowaPitStallFixtureMesh(Transform item, Mesh mesh, Material paint)
    {
        item.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = item.gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = paint;
        renderer.renderingLayerMask = BristolLightingLayer;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
    }
}
