using UnityEngine;

internal sealed class TrackEngineAudio
{
    private const int ReferenceRpm = 4000; // Both recorded loops were pitch-normalized to this speed.
    private const float NearDistanceMeters = 18f;
    private const float EngineGain = 0.34f;
    private const float MixGainLimit = 0.9f;
    private const float ShiftCutSeconds = 0.11f;
    private readonly EngineVoice[] voices;
    private readonly int loopSamples;

    internal TrackEngineAudio(Transform[] cars)
    {
        var onThrottle = Resources.Load<AudioClip>("Startup/StockCarEngineOnThrottle");
        var offThrottle = Resources.Load<AudioClip>("Startup/StockCarEngineOffThrottle");
        if (onThrottle == null || offThrottle == null ||
            onThrottle.samples != offThrottle.samples || onThrottle.frequency != offThrottle.frequency)
        {
            Debug.LogWarning("The stock car engine needs a matched pair of on- and off-throttle audio loops.");
            voices = System.Array.Empty<EngineVoice>();
            return;
        }

        loopSamples = onThrottle.samples;
        voices = new EngineVoice[cars.Length];
        for (int i = 0; i < cars.Length; i++)
            voices[i] = new EngineVoice(cars[i], onThrottle, offThrottle);
    }

    internal void Update(Transform[] cars, RaceSessionRun run, Transform camera)
    {
        float totalGain = 0f;
        float elapsed = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        for (int i = 0; i < voices.Length; i++)
        {
            var voice = voices[i];
            double rpm = run.EngineRpm(i);
            if (!cars[i].gameObject.activeSelf || rpm <= 0.0)
            {
                voice.Stop();
                continue;
            }

            Vector3 direction = cars[i].position - camera.position;
            float distance = direction.magnitude;
            float distanceGain = DistanceGain(distance);
            float pan = 0.75f * Vector3.Dot(direction, camera.right) / Mathf.Max(3f, distance);
            voice.Update(run.TransmissionGear(i), run.ThrottleInput(i), elapsed, PitchForRpm(rpm), pan, distanceGain);
            totalGain += voice.Gain;
        }

        float mixGain = totalGain > MixGainLimit ? MixGainLimit / totalGain : 1f;
        for (int i = 0; i < voices.Length; i++)
        {
            if (voices[i].Gain > 0f)
                voices[i].Play(mixGain, i * 7919 % loopSamples);
        }
    }

    internal void Stop()
    {
        foreach (var voice in voices)
            voice.Stop();
    }

    internal static float PitchForRpm(double rpm) => (float)(rpm / ReferenceRpm);
    internal static float DistanceGain(float meters) => NearDistanceMeters / (NearDistanceMeters + meters);

    private sealed class EngineVoice
    {
        private readonly AudioSource loaded;
        private readonly AudioSource coast;
        private bool playing;
        private int gear;
        private float blend;
        private float shiftCut;
        internal float Gain { get; private set; }

        internal EngineVoice(Transform car, AudioClip onThrottle, AudioClip offThrottle)
        {
            loaded = MakeSource(car, onThrottle);
            coast = MakeSource(car, offThrottle);
        }

        private static AudioSource MakeSource(Transform car, AudioClip clip)
        {
            var source = car.gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            // The track camera supplies distance and pan, even when Unity's listener is elsewhere.
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            return source;
        }

        internal void Update(int nextGear, float throttle, float elapsed, float pitch, float pan, float distanceGain)
        {
            if (playing && nextGear > gear)
                shiftCut = ShiftCutSeconds;
            gear = nextGear;
            float targetBlend = shiftCut > 0f ? 0f : Mathf.Clamp01(throttle);
            blend = playing ? Mathf.MoveTowards(blend, targetBlend, elapsed * (targetBlend < blend ? 18f : 10f)) : targetBlend;
            shiftCut = Mathf.Max(0f, shiftCut - elapsed);
            Gain = EngineGain * distanceGain;
            loaded.pitch = coast.pitch = pitch;
            loaded.panStereo = coast.panStereo = pan;
            loaded.priority = coast.priority = Mathf.RoundToInt(Mathf.Lerp(180f, 48f, distanceGain));
        }

        internal void Play(float mixGain, int sampleOffset)
        {
            loaded.volume = Gain * mixGain * blend;
            coast.volume = Gain * mixGain * (1f - blend) * 0.8f;
            if (playing)
                return;

            // Start at the same DSP time and sample so the blend does not double the pitch.
            double start = AudioSettings.dspTime + 0.03;
            loaded.timeSamples = coast.timeSamples = sampleOffset;
            loaded.PlayScheduled(start);
            coast.PlayScheduled(start);
            playing = true;
        }

        internal void Stop()
        {
            if (playing)
            {
                if (loaded != null)
                    loaded.Stop();
                if (coast != null)
                    coast.Stop();
            }
            playing = false;
            shiftCut = 0f;
            Gain = 0f;
        }
    }
}
