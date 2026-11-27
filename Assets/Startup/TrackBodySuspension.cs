using System;
using UnityEngine;

public sealed partial class TrackPreview
{
    private const float UnloadedBodyRoll = 2f;
    private const float LoadedBodyRoll = -2f;
    // Clearance on the normalized 2.9 m display car, measured from its wheel contact plane.
    private const float UnderbodyClearance = .062f;
    private readonly struct TrackBump
    {
        public readonly float LapFraction;
        public readonly float LengthMeters;
        public readonly float CompressionMeters;

        public TrackBump(float lapFraction, float lengthMeters, float compressionMeters)
        {
            LapFraction = lapFraction;
            LengthMeters = lengthMeters;
            CompressionMeters = compressionMeters;
        }
    }

    private TrackBump[] trackBumps = Array.Empty<TrackBump>();
    private float fullBumpCompressionSpeed;

    private void PrepareTrackBumps(TrackDefinition track)
    {
        // Layout stations follow the track datum rather than the session's start/finish line.
        trackBumps = track.id switch
        {
            LasVegasTrackId => new TrackBump[] { new(.221f, 16f, .022f), new(.257f, 18f, .049f), new(.288f, 14f, .031f) },
            TexasTrackId => new TrackBump[] { new(.690f, 16f, .028f), new(.754f, 17f, .049f), new(.819f, 15f, .034f) },
            PoconoTrackId => new TrackBump[] { new(.480f, 16f, .048f), new(.501f, 14f, .031f) },
            // COTA Turn 1 braking, the esses, and braking at the end of the back straight.
            CotaTrackId => new TrackBump[] { new(.061f, 14f, .032f), new(.204f, 14f, .035f), new(.510f, 19f, .044f) },
            DoverTrackId => new TrackBump[] { new(.123f, 17f, .036f), new(.393f, 15f, .027f),
                new(.623f, 18f, .042f), new(.893f, 16f, .029f) },
            _ => CreateSeededTrackBumps(track.id, lapLength)
        };
        float ovalSpeed = Mathf.Lerp(22f, 55f, Mathf.InverseLerp(.25f, 1.25f, track.lengthMiles));
        fullBumpCompressionSpeed = track.majorStyle == "Road" ? 38f : track.surface == "Dirt" ? Mathf.Min(ovalSpeed, 32f) : ovalSpeed;
    }

    private static TrackBump[] CreateSeededTrackBumps(int trackId, float length)
    {
        // Keep the same bumps each lap and across sessions, without consuming race randomness.
        var random = new System.Random(unchecked(trackId * 16777619));
        var bumps = new TrackBump[random.Next(1, 3)];
        for (int i = 0; i < bumps.Length; i++)
        {
            float station = bumps.Length == 1
                ? Mathf.Lerp(.08f, .86f, (float)random.NextDouble())
                : .08f + i * .44f + .34f * (float)random.NextDouble();
            float width = Mathf.Min(length * .08f, Mathf.Lerp(11f, 19f, (float)random.NextDouble()));
            float compression = Mathf.Lerp(.024f, .045f, (float)random.NextDouble());
            bumps[i] = new TrackBump(station, width, compression);
        }

        return bumps;
    }

    private struct BodySuspension
    {
        public float CorneringLoad;
        public float Heave;
        public bool Scraping;
        public float Roll => Mathf.Lerp(UnloadedBodyRoll, LoadedBodyRoll, CorneringLoad);
    }

    private readonly BodySuspension[] bodySuspensions = new BodySuspension[CarCount];
    private ParticleSystem underbodySparks;

    private void ResetBodySuspension()
    {
        Array.Clear(bodySuspensions, 0, bodySuspensions.Length);
        underbodySparks?.Clear(false);
    }

    private void SampleBodySuspension()
    {
        // Follow the changing load with the same time response at every playback speed.
        float rollResponse = 1f - Mathf.Exp(-SessionStep / .22f);
        float minimumBumpSpeed = fullBumpCompressionSpeed * (30f / 55f);
        for (int i = 0; i < cars.Length; i++)
        {
            if (!cars[i].gameObject.activeSelf)
                continue;

            ref var suspension = ref bodySuspensions[i];
            var traffic = isQualifying ? (i == 0 ? qualifying?.Car : null) : trafficCars?[i];
            float distance = traffic?.TrackDistance ?? sessionDistances[i];
            float speed = SessionRun.CurrentSpeed(i);
            bool onTrack = traffic?.InPit != true && traffic?.ApronEntryActive != true && traffic?.ApronExitActive != true &&
                (traffic?.FlatTireLaneBlend ?? 0f) == 0f && !crashes[i].Active && !SessionRun.Retired(i) &&
                !iowaRecoveredCars[i].HasValue && (i != 0 || !NorthWilkesboroGarageOffPitRoute) &&
                (raceStart == null || raceReleased);
            float load = onTrack ? SessionRun.VisualCorneringLoadAt(distance, speed) : 0f;
            suspension.CorneringLoad = Mathf.Lerp(suspension.CorneringLoad, load, rollResponse);
            suspension.Heave = 0f;

            if (onTrack && speed > minimumBumpSpeed)
            {
                float bumpLoad = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(minimumBumpSpeed, fullBumpCompressionSpeed, speed));
                suspension.Heave = SampleTrackBumpHeave(distance) * bumpLoad;
            }

            bool wasScraping = suspension.Scraping;
            suspension.Scraping = false;
            if (suspension.Heave >= 0f)
                continue;

            var visual = carVisualLods[i].Current;
            if (!visual.SupportsBodyTravel)
                continue;

            var rotation = Quaternion.AngleAxis(suspension.Roll, Vector3.forward);
            var vehicle = SessionRun.Vehicle(i);
            var setup = SessionRun.SetupForCar(i);
            float halfWidth = raceCarHalfWidths[i] * .76f;
            float lowestClearance = float.PositiveInfinity;
            float contactPressureDrop = 0f;
            float compressionWeight = Mathf.Clamp01(-suspension.Heave / .02f);
            float contactPressureLoss = 0f;
            Vector3 contact = Vector3.zero;
            for (int corner = 0; corner < 4; corner++)
            {
                bool right = (corner & 1) != 0;
                float coldPressure = VehicleSpeed003.ColdPressure(setup, corner, vehicle?.GetTire(corner));
                float pressureLoss = Mathf.Clamp01((1f - coldPressure / (right ? 46f : 28f)) / .35f);
                // Probe the corners of the splitter and floor against the wheel contact plane.
                var point = rotation * new Vector3(right ? halfWidth : -halfWidth, UnderbodyClearance, corner < 2 ? 1.10f : -.96f);
                float pressureDrop = .008f * pressureLoss * compressionWeight;
                float clearance = point.y + suspension.Heave - pressureDrop;
                if (clearance >= lowestClearance)
                    continue;

                lowestClearance = clearance;
                contact = point;
                contactPressureLoss = pressureLoss;
                contactPressureDrop = pressureDrop;
            }

            suspension.Heave -= contactPressureDrop;
            suspension.Scraping = lowestClearance <= 0f;
            if (!suspension.Scraping)
                continue;

            suspension.Heave -= lowestClearance;
            if (!wasScraping && TrackSkyIsNight && raceCamera.enabled)
            {
                float yaw = looseSetupYawAngles[i];
                Vector3 axle = visual.Wheels?.FrontAxleCenter ?? Vector3.forward * RearAxleHeading.CenterToRearAxle;
                contact.y = 0f;
                contact = axle + Quaternion.AngleAxis(yaw, Vector3.up) * (contact - axle);
                EmitUnderbodySparks(cars[i], contact, speed, contactPressureLoss);
            }
        }
    }

    private float SampleTrackBumpHeave(float distance)
    {
        float station = Mathf.Repeat(distance, lapLength);
        foreach (var bump in trackBumps)
        {
            float progress = (station - bump.LapFraction * lapLength) / bump.LengthMeters;
            if (progress is < 0f or >= 1f)
                continue;

            // A smooth compression followed by a smaller rebound, both returning to rest.
            const float reboundStart = .60f;
            bool compressing = progress < reboundStart;
            float phase = compressing ? progress / reboundStart : (progress - reboundStart) / (1f - reboundStart);
            float envelope = Mathf.Sin(Mathf.PI * phase);
            return envelope * envelope * bump.CompressionMeters * (compressing ? -1f : .28f);
        }

        return 0f;
    }

    private void PresentBodySuspension(int index, CarVisual visual)
    {
        if (!visual.SupportsBodyTravel)
            return;

        var suspension = bodySuspensions[index];
        visual.Body.SetLocalPositionAndRotation(Vector3.up * suspension.Heave,
            Quaternion.AngleAxis(suspension.Roll, Vector3.forward));
    }

    private void PresentBodySuspension()
    {
        for (int i = 0; i < cars.Length; i++)
            if (cars[i] != null && cars[i].gameObject.activeSelf && carVisualLods[i]?.Current is CarVisual visual)
                PresentBodySuspension(i, visual);

        if (raceCamera != null && raceCamera.enabled && ViewAngle is (CameraAngle.Hood or CameraAngle.InCar))
            ApplyCameraPose();

        if (underbodySparks == null)
            return;

        if (!TrackSkyIsNight)
            underbodySparks.Clear(false);
        else if (sessionSpeed != 0 && raceCamera.enabled)
            underbodySparks.Simulate(Time.deltaTime, false, false, false);
    }

    private void EmitUnderbodySparks(Transform car, Vector3 contact, float speed, float pressureLoss)
    {
        if (underbodySparks == null)
        {
            if (!LoadFireworkMaterial())
                return;
            underbodySparks = CreateTrackSparkEmitter("Underbody scrape sparks", transform, 192, .9f,
                ParticleSystemRenderMode.Stretch);
            var renderer = underbodySparks.GetComponent<ParticleSystemRenderer>();
            renderer.velocityScale = .015f;
            renderer.lengthScale = 2f;
        }

        Vector3 origin = car.TransformPoint(contact + Vector3.up * .008f);
        int count = 5 + Mathf.RoundToInt(3f * pressureLoss);
        for (int i = 0; i < count; i++)
        {
            float spread = i / (float)(count - 1);
            underbodySparks.Emit(new ParticleSystem.EmitParams
            {
                position = origin,
                // Carry some car velocity so the short streaks trail the moving contact point.
                velocity = car.TransformDirection(new Vector3(Mathf.Lerp(-2f, 2f, spread), .7f + .22f * (i % 3),
                    speed * .78f - 4f - 3f * spread)),
                startColor = Color.Lerp(new Color(1f, .82f, .44f), new Color(1f, .43f, .13f), spread),
                startSize = Mathf.Lerp(.035f, .015f, spread),
                startLifetime = Mathf.Lerp(.11f, .19f, spread)
            }, 1);
        }
    }
}
