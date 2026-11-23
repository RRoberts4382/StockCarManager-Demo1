using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public sealed partial class TrackPreview
{
    private const uint IowaRaceLightingLayer = 1u << 2;
    private const uint IowaRoadLightingLayer = 1u << 3;
    private const uint IowaPitLightingLayer = 1u << 4;

    private readonly List<Light> iowaVenueLights = new List<Light>();
    private readonly Dictionary<Transform, (Renderer[] Renderers, uint Zone)> iowaVehicleLighting =
        new Dictionary<Transform, (Renderer[], uint)>();

    internal bool SupportsIowaVenueLighting => iowaVenueLights.Count > 0;

    private void RegisterIowaVenueLight(Light lamp, bool racing)
    {
        lamp.cullingMask = 1 << PreviewLayer;
        lamp.GetUniversalAdditionalLightData().renderingLayers = racing ? IowaRaceLightingLayer : IowaRoadLightingLayer;
        lamp.enabled = TrackSkyIsNight;
        iowaVenueLights.Add(lamp);
    }

    private void ApplyIowaNightLighting(bool night)
    {
        SetDetailedTrackLighting(night);
        SetIowaPitStallLighting(night);
        SetIowaNightFixtureLenses(night);
        SetAuthoredVenueLampLenses(night);
        foreach (var lamp in iowaVenueLights)
            lamp.enabled = night;
    }

    private void AssignTrackLightingLayers()
    {
        if (definition.id is not (IowaTrackId or PhoenixTrackId or RichmondTrackId or NorthWilkesboroTrackId or MichiganTrackId))
        {
            AssignBristolLighting(transform);
            return;
        }

        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            renderer.renderingLayerMask = BristolLightingLayer | (definition.id == IowaTrackId ? IowaLightingZone(renderer) : AuthoredVenueLightingZone(renderer));

        iowaVehicleLighting.Clear();
        foreach (var car in cars)
            if (car != null)
                CacheIowaVehicleLighting(car);
        if (paceCar != null)
            CacheIowaVehicleLighting(paceCar);
    }

    private uint IowaLightingZone(Renderer renderer)
    {
        string surface = renderer.name;
        if (surface is "Iowa racing surface" or "Iowa flat apron" or "Iowa white racing edge" or
            "Iowa continuous outside concrete wall" or "Iowa restart zone band" ||
            surface.StartsWith("Start finish checker", StringComparison.Ordinal) ||
            surface.StartsWith("Iowa restart endpoint", StringComparison.Ordinal))
            return IowaRaceLightingLayer;

        if (teamPitBoxes != null && renderer.transform.IsChildOf(teamPitBoxes.transform) ||
            surface == "Iowa pit road" || surface.StartsWith("Iowa pit stall stripe ", StringComparison.Ordinal) ||
            surface.StartsWith("Iowa pit speed ", StringComparison.Ordinal) ||
            surface.StartsWith("Iowa concrete pit work pad ", StringComparison.Ordinal) ||
            surface.StartsWith("Iowa infield pit wall ", StringComparison.Ordinal))
            return IowaPitLightingLayer;

        if (surface is "Iowa east lot access" or "Iowa east return drive" or "Iowa infield south spur" or
            "Iowa frontstretch infield loop road" or "Iowa infield loop access" or
            "Iowa garage yards and pit service asphalt" or "Iowa west infield parking asphalt" ||
            surface.StartsWith("Iowa emergency access ", StringComparison.Ordinal))
            return IowaRoadLightingLayer;

        return 0;
    }

    private uint IowaTrackVehicleLighting(float distance, float lane)
    {
        Vector2 widths = RoadWidths(distance);
        float inward = lane * customLayout.InsideSign;
        float halfRoad = widths.x * .5f;
        return inward >= -halfRoad && inward <= halfRoad + widths.y ? IowaRaceLightingLayer : 0u;
    }

    private uint IowaConnectorVehicleLighting(Vector3 position, float attachment)
    {
        Vector3 center = TrackPoint(attachment, 0f, out var heading, out _);
        float station = attachment + Vector3.Dot(position - center, heading);
        float inside = customLayout.InsideSign;
        Vector2 widths = RoadWidths(station);
        Vector3 edge = TrackPoint(station, inside * widths.x * .5f, out var forward, out _);
        float apronOffset = Vector3.Dot(position - edge, Vector3.Cross(Vector3.up, forward).normalized * inside);
        return apronOffset >= 0f && apronOffset <= widths.y ? IowaRaceLightingLayer : 0u;
    }

    private void CacheIowaVehicleLighting(Transform car)
    {
        uint zone = iowaVehicleLighting.TryGetValue(car, out var prior) ? prior.Zone : 0;
        Renderer[] renderers = car.GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in renderers)
            renderer.renderingLayerMask = BristolLightingLayer | zone;
        iowaVehicleLighting[car] = (renderers, zone);
    }

    private void ApplyIowaVehicleLighting(Transform car, uint zone)
    {
        if (!iowaVehicleLighting.TryGetValue(car, out var vehicle) || vehicle.Zone == zone)
            return;

        foreach (var renderer in vehicle.Renderers)
            renderer.renderingLayerMask = BristolLightingLayer | zone;
        iowaVehicleLighting[car] = (vehicle.Renderers, zone);
    }
}
