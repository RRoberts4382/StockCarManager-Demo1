using UnityEngine;

public sealed partial class TrackPreview
{
    private const float BurnoutDuration = 8f;
    private const float BurnoutSettleTime = 1.1f;
    private const float BurnoutTurns = 3f;
    private int winnerBurnoutCar = -1;
    private bool winnerBurnoutActive, winnerBurnoutFinished;
    private float winnerBurnoutStation, winnerBurnoutRadius;
    private float winnerBurnoutTime, winnerBurnoutRemainder;
    private int winnerBurnoutStep;
    private readonly Vector3?[] winnerBurnoutSkids = new Vector3?[2];

    private void ResetWinnerBurnout()
    {
        winnerBurnoutCar = -1;
        winnerBurnoutActive = winnerBurnoutFinished = false;
        winnerBurnoutTime = winnerBurnoutRemainder = 0f;
        winnerBurnoutStep = 0;
        System.Array.Clear(winnerBurnoutSkids, 0, winnerBurnoutSkids.Length);
    }

    private void SelectWinnerBurnoutCar()
    {
        if ((winnerBurnoutCar >= 0 && !SessionRun.Retired(winnerBurnoutCar)) || SessionRun?.IsRace != true)
            return;

        winnerBurnoutCar = SessionRun.FinishedWinnerIndex();
        if (winnerBurnoutCar >= 0 && trafficCars != null)
        {
            var winner = trafficCars[winnerBurnoutCar];
            winner.NextEntry = float.PositiveInfinity;
            winner.ApronEntryActive = false;
        }
    }

    private bool UpdateWinnerBurnout(float seconds)
    {
        if (SessionRun?.IsRace != true)
            return false;
        if (winnerBurnoutFinished)
            return true;

        SelectWinnerBurnoutCar();
        if (!winnerBurnoutActive)
        {
            if (!DrivingComplete)
                return false;

            if (winnerBurnoutCar < 0 || !cars[winnerBurnoutCar].gameObject.activeSelf ||
                !TryFindWinnerBurnoutPath())
            {
                winnerBurnoutFinished = true;
                return true;
            }

            winnerBurnoutActive = true;
            BeginWinnerBurnoutFireworks();
            SetWinnerBurnoutPose(0f);
        }

        // Keep the celebration watchable at every race playback speed.
        winnerBurnoutRemainder += seconds;
        while (winnerBurnoutRemainder >= SessionStep)
        {
            winnerBurnoutRemainder -= SessionStep;
            winnerBurnoutTime += SessionStep;
            if (winnerBurnoutTime >= BurnoutDuration + BurnoutSettleTime)
            {
                winnerBurnoutActive = false;
                winnerBurnoutFinished = true;
                return true;
            }

            if (winnerBurnoutTime <= BurnoutDuration)
            {
                float progress = winnerBurnoutTime / BurnoutDuration;
                SetWinnerBurnoutPose(progress);
                SampleWinnerBurnoutTires(progress);
            }

            fireworkClock = winnerBurnoutTime;
            PresentFireworkCues();
        }

        PresentSessionSkids();
        ApplyCameraPose();
        return true;
    }

    private bool TryFindWinnerBurnoutPath()
    {
        const float bodyClearance = 2.1f;
        float finish = definition.StartFinishFraction * lapLength;
        // Start at the stripe. Use neighboring pavement only if the immediate area is occupied.
        foreach (float offset in new[] { 0f, 9f, -9f, 18f, -18f, 30f, -30f })
        {
            winnerBurnoutStation = finish + offset;
            float width = RoadWidths(winnerBurnoutStation).x;
            winnerBurnoutRadius = Mathf.Min(3.6f, width * .5f - bodyClearance);
            if (winnerBurnoutRadius < 1.5f)
                continue;

            bool clear = true;
            for (int i = 0; i <= 180 && clear; i++)
            {
                var point = WinnerBurnoutPoint(i / 180f);
                clear = Mathf.Abs(point.Lane) + bodyClearance <= RoadWidths(point.Station).x * .5f &&
                    ClearOfCar(paceCar, point.Position);
                for (int car = 0; car < cars.Length && clear; car++)
                    if (car != winnerBurnoutCar)
                        clear = ClearOfCar(cars[car], point.Position);
            }
            if (clear)
                return true;
        }
        return false;

        static bool ClearOfCar(Transform car, Vector3 position)
        {
            if (car == null || !car.gameObject.activeInHierarchy)
                return true;
            Vector3 displacement = car.localPosition - position;
            displacement.y = 0f;
            return displacement.sqrMagnitude >= 4.5f * 4.5f;
        }
    }

    private (float Station, float Lane, Vector3 Position, Vector3 Up) WinnerBurnoutPoint(float progress)
    {
        // Expand the circle as it turns so each rear tire lays a separate spiral.
        float rotation = Mathf.SmoothStep(0f, BurnoutTurns * 2f * Mathf.PI, progress);
        float radius = Mathf.Lerp(winnerBurnoutRadius * .48f, winnerBurnoutRadius,
            rotation / (BurnoutTurns * 2f * Mathf.PI));
        float station = winnerBurnoutStation + Mathf.Cos(rotation) * radius;
        float lane = Mathf.Sin(rotation) * radius;
        Vector3 position = TrackPoint(station, lane, out _, out Vector3 up);
        return (station, lane, position, up);
    }

    private void SetWinnerBurnoutPose(float progress)
    {
        var car = cars[winnerBurnoutCar];
        var point = WinnerBurnoutPoint(progress);
        Vector3 tangent = WinnerBurnoutPoint(Mathf.Min(1f, progress + .012f)).Position -
            WinnerBurnoutPoint(Mathf.Max(0f, progress - .012f)).Position;
        if (tangent.sqrMagnitude < .00000001f)
            TrackPoint(point.Station, point.Lane, out tangent, out _);
        Vector3 forward = Quaternion.AngleAxis(38f, point.Up) * tangent.normalized;
        car.SetLocalPositionAndRotation(point.Position, Quaternion.LookRotation(forward, point.Up));
        var wheels = raceWheels[winnerBurnoutCar];
        wheels?.SampleTireYaw(car);
        if (progress > 0f)
            wheels?.SpinBurnoutTires(SessionStep);
    }

    private void SampleWinnerBurnoutTires(float progress)
    {
        var car = cars[winnerBurnoutCar];
        var point = WinnerBurnoutPoint(progress);
        bool emit = ++winnerBurnoutStep % 3 == 0;
        for (int tire = 0; tire < 2; tire++)
        {
            float side = tire == 0 ? -1f : 1f;
            Vector3? contact = SkidTireContact(winnerBurnoutCar, point.Station, front: false, side: side);
            Vector3? previous = winnerBurnoutSkids[tire];
            winnerBurnoutSkids[tire] = contact;
            if (!contact.HasValue)
                continue;
            if (previous.HasValue)
                AddSkidSegment(previous.Value, contact.Value, .73f);
            if (!emit)
                continue;

            Vector3 position = transform.TransformPoint(contact.Value) + Vector3.up * .1f;
            for (int puff = 0; puff < 3; puff++)
            {
                float spread = puff - 1f;
                Vector3 drift = Vector3.up * (1.15f + .35f * puff) - car.forward * (1.3f + .25f * puff)
                    + car.right * (.3f * side + .5f * spread);
                EmitTrackSmoke(ref tireSmoke, "Tire smoke", position + car.right * (.14f * spread), drift,
                    new Color(.88f, .89f, .90f, .72f), false, startSize: .85f + .18f * puff);
            }
        }
    }

    private void PresentWinnerBurnoutCamera(Transform cameraTransform)
    {
        Vector3 center = TrackPoint(winnerBurnoutStation, 0f, out Vector3 heading, out _);
        Vector3 across = Vector3.Cross(Vector3.up, heading).normalized * -(customLayout?.InsideSign ?? -1f);
        cameraTransform.localPosition = center - heading * 14f + across * 12f + Vector3.up * 9f;
        cameraTransform.localRotation = Quaternion.LookRotation(center + Vector3.up * 1.3f - cameraTransform.localPosition);
        raceCamera.fieldOfView = 34f;
    }

    private void BeginWinnerBurnoutFireworks()
    {
        // The post-race salvos do not use the race-start camera or timing state.
        fireworkSites = null;
        fireworkCues.Clear();
        fireworkSparks?.Clear(false);
        fireworkClock = fireworkVisualClock = 0f;
        if (definition.majorStyle != "Oval" || !LoadFireworkMaterial())
            return;

        var (first, last) = LocateFireworksBackstretch();
        var sites = FindFireworkSites(first, last, 0f, new OutsideBillboardObstacles(transform, cars));
        if (sites == null)
            return;

        for (float time = 0f; time < BurnoutDuration; time += 2f)
            for (int site = 0; site < sites.Length; site++)
                QueueFireworkSalvo(sites[site], site, time);
    }
}
