using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public sealed partial class TrackPreview
{
    private GameObject michiganVenue;

    private void AddMichiganVenue()
    {
        AddLargeTrackGrandstands(michiganVenue);
        grandstandScenery.TryGetComponent<GrandstandAttendance>(out var attendance);
        const string standPrefix = "Michigan grandstand ";
        const string benchPrefix = standPrefix + "bench ";
        var safetyFaces = new Renderer[4];
        float finish = definition.StartFinishFraction * lapLength;
        Vector3 finishPoint = TrackPoint(finish, 0f, out _, out _);
        foreach (var fixture in michiganVenue.GetComponentsInChildren<Transform>(true))
        {
            string name = fixture.name;
            Vector3 position = transform.InverseTransformPoint(fixture.position);
            if (name == "Michigan Spotter camera")
            {
                iowaSpotterPosition = position;
                HideAuthoredVenueMarker(fixture);
            }
            else if (name == "Michigan scoring pylon anchor")
            {
                HideAuthoredVenueMarker(fixture);
                Vector3 facing = Vector3.ProjectOnPlane(finishPoint - position, Vector3.up);
                if (facing.sqrMagnitude > .01f)
                    BuildScoringPylon(position, facing);
            }
            else if (name == "Michigan flag official anchor")
            {
                HideAuthoredVenueMarker(fixture);
                var official = CreateObject("Michigan flag official rig", transform).transform;
                official.SetLocalPositionAndRotation(position,
                    Quaternion.LookRotation(-SampleOutsideEdge(finish).Outward, Vector3.up));
                BuildIowaFlagArmRig(official);
            }
            else if (attendance != null && name.StartsWith(benchPrefix, StringComparison.Ordinal))
                RegisterAuthoredBench(fixture, attendance, standPrefix, benchPrefix);
            else if (name.StartsWith("Michigan track light ", StringComparison.Ordinal))
                AddMichiganLight(fixture, position, IowaRaceLightingLayer);
            else if (name.StartsWith("Michigan pit light ", StringComparison.Ordinal) && pitRoute != null)
                AddMichiganLight(fixture, position, IowaPitLightingLayer);
            else if (name.StartsWith("Michigan street light ", StringComparison.Ordinal))
                AddMichiganLight(fixture, position, IowaRoadLightingLayer);
            else
            {
                int bank = name switch
                {
                    "Michigan safety left amber lenses" => 0,
                    "Michigan safety right amber lenses" => 1,
                    "Michigan safety green lenses" => 2,
                    "Michigan safety red lenses" => 3,
                    _ => -1
                };
                if (bank >= 0)
                    safetyFaces[bank] = fixture.GetComponent<Renderer>();
            }
        }
        if (Array.TrueForAll(safetyFaces, face => face != null))
            SetIowaSafetyFaces(safetyFaces[0], safetyFaces[1], safetyFaces[2], safetyFaces[3]);
        // The paddock rows face west across the service aisle in the layout.
        AddVenueHaulers("Michigan", transform.TransformDirection(Vector3.left));
        if (venueHaulers?.Length > 0)
            foreach (var renderer in michiganVenue.GetComponentsInChildren<Renderer>(true))
                if (renderer.name.StartsWith("Michigan hauler display ", StringComparison.Ordinal))
                    renderer.enabled = false;
        ApplyIowaNightLighting(TrackSkyIsNight);
    }

    private void AddMichiganLight(Transform marker, Vector3 position, uint zone)
    {
        HideAuthoredVenueMarker(marker);
        var lamp = CreateObject(marker.name + " emitter", transform).AddComponent<Light>();
        lamp.transform.localPosition = position;
        lamp.shadows = LightShadows.None;
        if (zone == IowaRaceLightingLayer)
        {
            // Broad pools overlap between the evenly spaced outside poles.
            lamp.type = LightType.Point;
            lamp.color = new Color32(236, 243, 255, 255);
            lamp.intensity = 900f;
            lamp.range = 100f;
        }
        else
        {
            Vector3 aim = position + Vector3.down;
            bool pit = zone == IowaPitLightingLayer;
            if (pit)
            {
                float fraction = pitRoute[1].ClosestFraction(customLayout.ToSourceSpace(position));
                Vector3 road = PitPosition(pitRoute[1], fraction, pitHeights[1], pitHeights[2]);
                aim = Vector3.Lerp(road, position, .28f);
            }
            lamp.transform.localRotation = Quaternion.LookRotation(aim - position, Vector3.forward);
            lamp.type = LightType.Spot;
            lamp.color = new Color32(255, 231, 193, 255);
            lamp.intensity = pit ? 64f : 85f;
            lamp.range = pit ? 12f : 20f;
            lamp.spotAngle = 100f;
            lamp.innerSpotAngle = 72f;
        }
        RegisterIowaVenueLight(lamp, zone == IowaRaceLightingLayer);
        lamp.GetUniversalAdditionalLightData().renderingLayers = zone;
    }
}
