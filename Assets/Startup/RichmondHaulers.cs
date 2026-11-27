using System;
using System.Linq;
using UnityEngine;

public sealed partial class TrackPreview
{
    private Transform[] venueHaulers;
    private (Color32 HoodAndTrunk, Color32 RoofAndSides)[] venueGuestHaulerPaint;

    private void AddVenueHaulers(string venue, Vector3? authoredForward = null)
    {
        string stallPrefix = venue + " hauler stall ";
        var markers = GetComponentsInChildren<Transform>(true);
        var stalls = markers.Where(item => item.name.StartsWith(stallPrefix, StringComparison.Ordinal))
            .OrderBy(item => item.name, StringComparer.Ordinal).ToArray();
        if (stalls.Length == 0)
            return;

        var prefab = Resources.Load<GameObject>("Startup/RaceHauler");
        if (prefab == null)
        {
            Debug.LogWarning($"{venue} could not load the race hauler model.", this);
            return;
        }

        // The row faces the paddock, away from the adjacent recovery stalls.
        var recovery = markers.FirstOrDefault(item => item.name == "Richmond recovery bay 20");
        var rowCenter = stalls[Mathf.Max(0, stalls.Length / 2 - 1)];
        var forward = authoredForward ?? (recovery != null
            ? Vector3.ProjectOnPlane(rowCenter.position - recovery.position, Vector3.up).normalized
            : rowCenter.forward);
        var garageBays = definition.id is NorthWilkesboroTrackId or LasVegasTrackId
            ? markers.Where(item => item.name.StartsWith(venue + " hauler garage bay ", StringComparison.Ordinal))
                .ToDictionary(item => item.name, StringComparer.Ordinal)
            : null;
        var group = CreateObject(venue + " parked haulers", transform).transform;
        int count = soloTestSession ? 1 : stalls.Length;
        venueHaulers = new Transform[count];
        venueGuestHaulerPaint = new (Color32, Color32)[count];
        var random = new System.Random();
        for (int i = 0; i < count; i++)
        {
            var hauler = Instantiate(prefab, group, false).transform;
            var tractor = hauler.Find("Tractor")?.GetComponent<Renderer>();
            var trailer = hauler.Find("Trailer")?.GetComponent<Renderer>();
            bool noseForward = tractor == null || trailer == null ||
                hauler.InverseTransformDirection(tractor.bounds.center - trailer.bounds.center).z > 0f;
            var stall = stalls[i];
            Vector3 nose = forward;
            if (garageBays != null &&
                garageBays.TryGetValue(venue + " hauler garage bay " + stall.name.Substring(stallPrefix.Length), out var bay))
                nose = Vector3.ProjectOnPlane(stall.position - bay.position, Vector3.up).normalized;
            Vector3 localForward = transform.InverseTransformDirection(nose);
            hauler.SetLocalPositionAndRotation(transform.InverseTransformPoint(stall.position),
                Quaternion.LookRotation(noseForward ? localForward : -localForward));
            foreach (var child in hauler.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = PreviewLayer;
            venueHaulers[i] = hauler;
            venueGuestHaulerPaint[i] = (RandomHaulerPaint(random), RandomHaulerPaint(random));
        }
        foreach (var stall in stalls)
            HideAuthoredVenueMarker(stall);
        if (!soloTestSession)
            PaintVenueHaulers(null);
    }

    private void EnsureSoloTeamHauler()
    {
        if (!soloTestSession || venueHaulers is { Length: > 0 } || parkedHaulers != null)
            return;

        var prefab = Resources.Load<GameObject>("Startup/RaceHauler");
        if (prefab == null)
            return;

        // Find a strip beyond the full driving surface and pit road projections.
        var finish = TrackPoint(definition.StartFinishFraction * lapLength, 0f, out var forward, out _);
        var along = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
        var outside = Vector3.Cross(Vector3.up, along) * -(customLayout?.InsideSign ?? -1f);
        float setback = 0f;
        const int trackSamples = 256;
        for (int i = 0; i < trackSamples; i++)
        {
            float distance = lapLength * i / trackSamples;
            var center = TrackPoint(distance, 0f, out _, out _);
            var widths = RoadWidths(distance);
            setback = Mathf.Max(setback, Vector3.Dot(center - finish, outside) + widths.x * .5f + widths.y);
        }

        if (customLayout != null && definition.pitRoad is { enabled: true } pit)
        {
            foreach (var points in new[] { pit.points, pit.entrance, pit.exit })
            {
                if (points == null || points.Length < 2)
                    continue;
                var spline = new TrackPitSpline(points);
                for (int i = 0; i <= PitRibbonSegments; i++)
                {
                    var point = customLayout.ToTrackSpace(spline.Sample((float)i / PitRibbonSegments));
                    setback = Mathf.Max(setback, Vector3.Dot(point - finish, outside) + pit.widthMeters * .5f);
                }
            }
        }

        var obstacles = new OutsideBillboardObstacles(transform, cars);
        var heading = Quaternion.LookRotation(along);
        float[] shifts = { 0f, 24f, -24f, 48f, -48f };
        for (float offset = 12f; offset <= 96f; offset += 12f)
        {
            foreach (float shift in shifts)
            {
                var position = finish + along * shift + outside * (setback + offset);
                position.y = groundHeight + .02f;
                if (!obstacles.IsClear(new IowaParkingSpace(position, heading, 3.5f, 14f)))
                    continue;

                var group = CreateObject(VenueSceneryName + " test session hauler parking", transform).transform;
                var hauler = Instantiate(prefab, group, false).transform;
                hauler.SetLocalPositionAndRotation(position, heading);
                foreach (var child in hauler.GetComponentsInChildren<Transform>(true))
                    child.gameObject.layer = PreviewLayer;
                venueHaulers = new[] { hauler };
                venueGuestHaulerPaint = new[] { TeamLivery(27) };
                return;
            }
        }
    }

    private void PaintVenueHaulers(CarSimulationContext context)
    {
        if (venueHaulers == null)
            return;
        var teams = context?.Cars.Where(entry => entry != null &&
            (!soloTestSession || entry.playerEntry) && !string.IsNullOrWhiteSpace(entry.teamName))
            .GroupBy(entry => (entry.playerEntry, Name: entry.teamName.Trim().ToUpperInvariant()))
            .Select(group => group.FirstOrDefault(entry => entry.teamPaint.HasValue || entry.car != null) ?? group.First())
            .ToArray() ?? Array.Empty<SessionCarResult>();
        if (definition.id == NorthWilkesboroTrackId)
            teams = teams.OrderByDescending(entry => entry.playerEntry).ToArray();
        var paint = new MaterialPropertyBlock();
        for (int i = 0; i < venueHaulers.Length; i++)
        {
            var hauler = venueHaulers[i];
            var team = i < teams.Length ? teams[i] : null;
            var colors = team == null
                ? venueGuestHaulerPaint[i]
                : team.teamPaint ?? team.car?.Paint ?? TeamLivery(27 + i);
            hauler.name = team == null ? $"{VenueSceneryName} guest hauler {i + 1:00}" : $"{team.teamName} hauler";
            Color roof = Color.Lerp(colors.RoofAndSides, colors.HoodAndTrunk, .35f);
            PaintHauler(hauler, colors, roof, paint);
        }
    }

    private static Color32 RandomHaulerPaint(System.Random random) => Color.HSVToRGB((float)random.NextDouble(),
        .48f + .36f * (float)random.NextDouble(), .56f + .36f * (float)random.NextDouble());
}
