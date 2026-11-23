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
        var rowCenter = stalls[stalls.Length / 2 - 1];
        var forward = authoredForward ?? (recovery != null
            ? Vector3.ProjectOnPlane(rowCenter.position - recovery.position, Vector3.up).normalized
            : rowCenter.forward);
        var garageBays = definition.id == NorthWilkesboroTrackId
            ? markers.Where(item => item.name.StartsWith(venue + " hauler garage bay ", StringComparison.Ordinal))
                .ToDictionary(item => item.name, StringComparer.Ordinal)
            : null;
        var group = CreateObject(venue + " parked haulers", transform).transform;
        venueHaulers = new Transform[stalls.Length];
        venueGuestHaulerPaint = new (Color32, Color32)[stalls.Length];
        var random = new System.Random();
        for (int i = 0; i < stalls.Length; i++)
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
            HideAuthoredVenueMarker(stalls[i]);
        }
        PaintVenueHaulers(null);
    }

    private void PaintVenueHaulers(CarSimulationContext context)
    {
        if (venueHaulers == null)
            return;
        var teams = context?.Cars.Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.teamName))
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
