using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed partial class TrackPreview
{
    private const int FireworkSiteCount = 6;
    private const float FireworkSparkGravity = .70f;
    private const float FireworkBurstLifetime = 2.25f;
    private static readonly Color[] FireworkPalette =
    {
        new Color(1f, .36f, .11f), new Color(.18f, .65f, 1f), new Color(1f, .78f, .30f),
        new Color(.85f, .22f, 1f), new Color(.28f, 1f, .51f), new Color(1f, .29f, .38f)
    };

    private readonly struct FireworkSite
    {
        public readonly float Distance;
        public readonly Vector3 Foot;
        public readonly Vector3 Along;
        public readonly Vector3 Outward;

        public FireworkSite(float distance, Vector3 foot, Vector3 along, Vector3 outward)
        {
            Distance = distance;
            Foot = foot;
            Along = along;
            Outward = outward;
        }
    }

    private readonly struct FireworkCue
    {
        public readonly float Time;
        public readonly Vector3 Position;
        public readonly Vector3 Velocity;
        public readonly Color Color;
        public readonly float RiseSeconds;

        public FireworkCue(float time, Vector3 position, Vector3 velocity, Color color, float riseSeconds = 0f)
        {
            Time = time;
            Position = position;
            Velocity = velocity;
            Color = color;
            RiseSeconds = riseSeconds;
        }
    }

    private readonly List<FireworkCue> fireworkCues = new(FireworkSiteCount * 6);
    private FireworkSite[] fireworkSites;
    private Material fireworkMaterial;
    private ParticleSystem fireworkSparks;
    private float fireworkClock;
    private float fireworkVisualClock;
    private float lastFireworkCueTime;
    private float? previousFireworkLead;
    private int nextFireworkSite;
    private uint fireworkSeed;

    private void PrepareRaceStartFireworks(RaceSession session)
    {
        fireworkSparks?.Clear(false);
        fireworkSites = null;
        fireworkCues.Clear();
        nextFireworkSite = 0;
        previousFireworkLead = null;
        fireworkClock = fireworkVisualClock = lastFireworkCueTime = 0f;
        fireworkSeed = 0;
        if (raceStart == null || session.kind != RaceSessionKind.Race || definition.majorStyle != "Oval" ||
            !SessionRun.Weather.IsNight || lapLength <= 0f)
            return;

        if (fireworkMaterial == null)
        {
            var shader = Resources.Load<Shader>("Startup/TrackFireworks");
            if (shader == null || !shader.isSupported)
                return;
            fireworkMaterial = new Material(shader);
            materials.Add(fireworkMaterial);
        }

        var (first, last) = LocateFireworksBackstretch();
        // Choose the final complete pass along this backstretch before the initial green.
        float lapOffset = lapLength * Mathf.Floor((raceStart.GreenDistance - last) / lapLength);
        var obstacles = new OutsideBillboardObstacles(transform, cars);
        var sites = new List<FireworkSite>(FireworkSiteCount);
        for (int i = 0; i < FireworkSiteCount; i++)
        {
            float station = Mathf.Lerp(first, last, (i + .5f) / FireworkSiteCount);
            float outside = -(customLayout?.InsideSign ?? -1f);
            Vector3 edge = TrackPoint(station, outside * RoadWidths(station).x * .5f, out var heading, out _);
            Vector3 along = Vector3.ProjectOnPlane(heading, Vector3.up).normalized;
            Vector3 outward = Vector3.Cross(Vector3.up, along) * outside;
            Quaternion rotation = Quaternion.LookRotation(along, Vector3.up);
            float firstSetback = Mathf.Clamp(lapLength * .014f, 22f, 50f);
            for (float setback = firstSetback; setback <= firstSetback + 200f; setback += 5f)
            {
                Vector3 foot = edge + outward * setback;
                foot.y = groundHeight + .07f;
                if (!obstacles.IsClear(new IowaParkingSpace(foot, rotation, 3.5f, 3.5f)))
                    continue;
                sites.Add(new FireworkSite(station + lapOffset, foot, along, outward));
                break;
            }
        }
        if (sites.Count > 0)
            fireworkSites = sites.ToArray();
    }

    private (float First, float Last) LocateFireworksBackstretch()
    {
        // Find the long stretch headed away from the start/finish direction. Use chord
        // headings over several control points so individual spline knots do not break it.
        const int samples = 256;
        const int from = 68;
        const int to = 198;
        const float straightAlignment = .987688f; // cos(9 degrees)
        float finish = definition.StartFinishFraction * lapLength;
        float interval = lapLength / samples;
        Vector3 front = Heading(finish, .035f * lapLength);
        var headings = new Vector3[to - from];
        for (int i = 0; i < headings.Length; i++)
            headings[i] = Heading(finish + (from + i) * interval, .009f * lapLength);

        float best = 0f;
        float first = finish + lapLength * .42f;
        float last = finish + lapLength * .58f;
        for (int i = 0; i < headings.Length; i++)
        {
            Vector3 heading = headings[i];
            float opposite = -Vector3.Dot(heading, front);
            if (opposite < .40f)
                continue;
            int start = i, end = i;
            while (start > 0 && Vector3.Dot(heading, headings[start - 1]) >= straightAlignment)
                start--;
            while (end < headings.Length - 1 && Vector3.Dot(heading, headings[end + 1]) >= straightAlignment)
                end++;
            float score = (end - start) * (.75f + .25f * opposite);
            if (score <= best)
                continue;
            best = score;
            first = finish + (from + start) * interval;
            last = finish + (from + end) * interval;
        }
        return (first, last);

        Vector3 Heading(float station, float radius)
        {
            Vector3 a = TrackPoint(station - radius, 0f, out _, out _);
            Vector3 b = TrackPoint(station + radius, 0f, out _, out _);
            return Vector3.ProjectOnPlane(b - a, Vector3.up).normalized;
        }
    }

    private void AdvanceRaceStartFireworks(float seconds)
    {
        if (fireworkSites == null)
            return;
        fireworkClock += seconds;
        if (nextFireworkSite >= fireworkSites.Length || raceReleased || !raceStart.OnTrackFieldLead.HasValue)
            return;

        float lead = raceStart.OnTrackFieldLead.Value;
        float before = previousFireworkLead ?? (lead - seconds * TrackState001RaceStart.PaceSpeed);
        previousFireworkLead = lead;
        while (nextFireworkSite < fireworkSites.Length && lead >= fireworkSites[nextFireworkSite].Distance)
        {
            int index = nextFireworkSite++;
            var site = fireworkSites[index];
            if (before <= site.Distance)
            {
                float time = fireworkClock - seconds * (1f - Mathf.InverseLerp(before, lead, site.Distance));
                QueueFireworkSalvo(site, index, time);
            }
        }
    }

    private void QueueFireworkSalvo(FireworkSite site, int index, float time)
    {
        float height = Mathf.Clamp(lapLength * .060f, 50f, 180f);
        for (int shell = 0; shell < 3; shell++)
        {
            float spread = shell - 1f;
            float rise = 1.05f + .16f * shell;
            Vector3 arc = site.Along * (spread * 17f) + site.Outward * (5f + shell * 3f) +
                Vector3.up * (height - Mathf.Abs(spread) * 11f);
            Vector3 exhaustVelocity = arc / rise - Physics.gravity * (FireworkSparkGravity * rise * .5f);
            Color color = FireworkPalette[(index + shell * 2) % FireworkPalette.Length];
            float launchAt = time + .19f * shell;
            Vector3 launch = site.Foot + site.Along * (spread * 1.25f);
            fireworkCues.Add(new FireworkCue(launchAt, launch, exhaustVelocity, color, rise));
            fireworkCues.Add(new FireworkCue(launchAt + rise,
                launch + arc, Vector3.zero, color));
        }
        fireworkCues.Sort(static (a, b) => a.Time.CompareTo(b.Time));
        lastFireworkCueTime = fireworkCues[^1].Time;
    }

    private void PresentRaceStartFireworks()
    {
        if (fireworkSites == null || fireworkClock <= fireworkVisualClock)
            return;

        int consumed = 0;
        while (consumed < fireworkCues.Count && fireworkCues[consumed].Time <= fireworkClock)
        {
            var cue = fireworkCues[consumed++];
            if (fireworkSparks == null)
                CreateFireworkSparks();
            if (cue.Time > fireworkVisualClock)
                fireworkSparks.Simulate(cue.Time - fireworkVisualClock, false, false, false);
            fireworkVisualClock = cue.Time;
            EmitFireworkCue(cue);
        }
        if (consumed > 0)
            fireworkCues.RemoveRange(0, consumed);
        if (fireworkSparks != null && (consumed > 0 || fireworkSparks.particleCount > 0))
            fireworkSparks.Simulate(fireworkClock - fireworkVisualClock, false, false, false);
        fireworkVisualClock = fireworkClock;
        if ((nextFireworkSite >= fireworkSites.Length || raceReleased) &&
            fireworkClock - lastFireworkCueTime > FireworkBurstLifetime + .15f)
            fireworkSites = null;
    }

    private void CreateFireworkSparks()
    {
        var emitter = CreateObject("Backstretch fireworks", transform);
        emitter.SetActive(false);
        fireworkSparks = emitter.AddComponent<ParticleSystem>();
        var main = fireworkSparks.main;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        main.maxParticles = 2048;
        main.gravityModifier = FireworkSparkGravity;
        var emission = fireworkSparks.emission;
        emission.enabled = false;
        var shape = fireworkSparks.shape;
        shape.enabled = false;
        var fade = fireworkSparks.colorOverLifetime;
        fade.enabled = true;
        fade.color = new Gradient
        {
            alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(.86f, .42f),
                new GradientAlphaKey(.24f, .81f), new GradientAlphaKey(0f, 1f) },
            colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }
        };
        var renderer = emitter.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = fireworkMaterial;
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = 1.4f;
        renderer.velocityScale = .055f;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        emitter.SetActive(true);
    }

    private void EmitFireworkCue(FireworkCue cue)
    {
        if (cue.RiseSeconds > 0f)
        {
            Emit(cue.Velocity, Color.Lerp(cue.Color, Color.white, .75f), 1.45f, cue.RiseSeconds);
            Emit(Vector3.up * 3.5f, new Color(1f, .66f, .27f), 2.40f, .21f);
            return;
        }

        const int sparkCount = 108;
        const float goldenAngle = 2.399963f;
        float scale = Mathf.Clamp(lapLength / 2000f, .65f, 1.25f);
        float rotation = (fireworkSeed % 61) * .41f;
        Emit(Vector3.zero, Color.white, 8.5f * scale, .18f);
        for (int i = 0; i < sparkCount; i++)
        {
            float y = 1f - 2f * (i + .5f) / sparkCount;
            float radius = Mathf.Sqrt(1f - y * y);
            float angle = i * goldenAngle + rotation;
            Vector3 direction = new(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
            float variation = (i * 37 % sparkCount) / (float)sparkCount;
            Color color = Color.Lerp(cue.Color, Color.white, .12f + variation * .36f);
            Emit(direction * ((21f + 17f * variation) * scale), color, (1.48f + .60f * variation) * scale,
                Mathf.Lerp(1.5f, FireworkBurstLifetime, variation));
        }

        void Emit(Vector3 velocity, Color color, float size, float lifetime)
        {
            fireworkSparks.Emit(new ParticleSystem.EmitParams
            {
                position = transform.TransformPoint(cue.Position),
                velocity = transform.TransformDirection(velocity),
                startColor = color,
                startSize = size,
                startLifetime = lifetime,
                randomSeed = ++fireworkSeed
            }, 1);
        }
    }
}
