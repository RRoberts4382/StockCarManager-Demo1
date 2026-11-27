using UnityEngine;

public sealed partial class TrackPreview
{
    private const string DoverVenueName = "Dover";
    private const float DoverPitCorridorWidth = 47f * 0.3048f;
    private const float DoverPitRearWallDepth = 2.4f * 0.335f;

    private static readonly (string Name, Vector2 Position)[] DoverSafetySites =
    {
        ("Frontstretch exit", new Vector2(-163.98f, 151.75f)),
        ("Turn 1 midpoint", new Vector2(-271.28f, 107.30f)),
        ("Turn 2 midpoint", new Vector2(-271.28f, -107.30f)),
        ("Backstretch entrance", new Vector2(-163.98f, -151.75f)),
        ("Backstretch exit", new Vector2(163.98f, -151.75f)),
        ("Turn 3 midpoint", new Vector2(271.28f, -107.30f)),
        ("Turn 4 midpoint", new Vector2(271.28f, 107.30f)),
        ("Frontstretch entrance", new Vector2(163.98f, 151.75f))
    };

    private bool UsesDoverVenue => definition.id == DoverTrackId && importedTrackGeometry;

    private float PitWorkWallBack => UsesDoverVenue
        ? DoverPitCorridorWidth - definition.pitRoad.widthMeters * .5f + DoverPitRearWallDepth
        : definition.pitRoad.widthMeters * .5f + PitWallThickness;

    private void AdvanceDoverApronLanes()
    {
        if (!UsesDoverVenue || trafficCars == null)
            return;

        float inside = customLayout.InsideSign;
        for (int i = 0; i < trafficCars.Length; i++)
        {
            var car = trafficCars[i];
            if (!onTrack[i] || car.InPit || crashes[i].Active || car.FlatTireLaneBlend > 0f ||
                (!car.ApronEntryActive && !car.ApronExitActive))
                continue;

            float before = drivingDistances[i];
            float depth = inside * drivingLanes[i] - RoadWidths(before).x * .5f;
            // Carry the apron lane with the road width change. The steering limit
            // still governs a driver's move across the apron or onto the track.
            float follow = Mathf.SmoothStep(0f, 1f, depth);
            drivingLanes[i] += (ApronLane(car.TrackDistance) - ApronLane(before)) * follow;
        }
    }

    private void AddDoverVenue()
    {
        foreach (var marker in GetComponentsInChildren<Transform>(true))
        {
            if (marker.name == "Dover Spotter camera")
            {
                HideAuthoredVenueMarker(marker);
                var roof = transform.InverseTransformPoint(marker.position);
                float station = TrackUtilityGeometry.ClosestMainDistance(customLayout, lapLength, roof);
                var trackPosition = TrackPoint(station, 0f, out _, out _);
                var towardTrack = Vector3.ProjectOnPlane(trackPosition - roof, Vector3.up).normalized;
                iowaSpotterPosition = roof + towardTrack * 8f + Vector3.up;
            }
            else if (marker.name == "Dover scoring pylon")
            {
                HideAuthoredVenueMarker(marker);
                var position = transform.InverseTransformPoint(marker.position);
                var finish = TrackPoint(definition.StartFinishFraction * lapLength, 0f, out _, out _);
                var towardTrack = Vector3.ProjectOnPlane(finish - position, Vector3.up).normalized;
                BuildScoringPylon(position, towardTrack);
            }
        }

        var sites = new (string Name, float Station)[DoverSafetySites.Length];
        for (int i = 0; i < sites.Length; i++)
        {
            var site = DoverSafetySites[i];
            var point = customLayout.ToTrackSpace(new Vector3(site.Position.x, 0f, site.Position.y));
            float station = TrackUtilityGeometry.ClosestMainDistance(customLayout, lapLength, point);
            sites[i] = (site.Name, station / lapLength);
        }

        AddVenueHaulers(DoverVenueName);
        AddAuthoredOvalVenue(DoverVenueName, sites, 36071);
    }

    private float DoverPacePitStation(Vector3 position)
    {
        var source = customLayout.ToSourceSpace(position);
        float bestError = float.PositiveInfinity;
        float bestDistance = 0f;
        float start = 0f;
        for (int i = 0; i < pitRoute.Length; i++)
        {
            var spline = pitRoute[i];
            float fraction = spline.ClosestFraction(source);
            float error = (source - spline.Sample(fraction)).sqrMagnitude;
            if (error < bestError)
            {
                bestError = error;
                bestDistance = start + fraction * pitLengths[i];
            }
            start += pitLengths[i];
        }
        return bestDistance;
    }
}
