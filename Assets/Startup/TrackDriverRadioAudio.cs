using System.Collections.Generic;
using UnityEngine;

internal sealed class TrackDriverRadioAudio
{
    private const string ResourcePath = "Startup/DriverRadio/";
    private const float EngineBedGain = .16f;
    private const float VoiceGain = .86f;
    private readonly AudioSource voice;
    private readonly AudioSource staticBed;
    private readonly AudioSource squelch;
    private readonly TrackEngineAudio.EngineVoice engine;
    private readonly Dictionary<string, string> recordings = new(System.StringComparer.Ordinal);
    private readonly Dictionary<string, string> crewRecordings = new(System.StringComparer.Ordinal);
    private readonly Dictionary<string, AudioClip> loadedClips = new(System.StringComparer.Ordinal);
    private readonly Queue<(AudioClip Clip, bool Crew, float Gain)> pending = new();
    private RaceSessionRun activeRun;
    private double speechEnd;
    private bool urgentTransmission;
    private int spotterPriority;
    private bool crewSpeaking;

    private bool IsPlaying => activeRun != null;
    internal bool IsPlayingUrgent => urgentTransmission && (IsPlaying || squelch.isPlaying);

    [System.Serializable]
    private sealed class RecordingCatalog
    {
        public Recording[] lines;
        public Recording[] crewLines;
    }

    [System.Serializable]
    private sealed class Recording
    {
        public string text;
        public string id;
    }

    internal TrackDriverRadioAudio(Transform parent)
    {
        var radio = new GameObject("Driver radio");
        radio.transform.SetParent(parent, false);
        voice = MakeSource(radio, VoiceGain);
        staticBed = MakeSource(radio, .34f);
        staticBed.clip = Resources.Load<AudioClip>(ResourcePath + "Static");
        staticBed.loop = true;
        squelch = MakeSource(radio, .48f);
        squelch.clip = Resources.Load<AudioClip>(ResourcePath + "Squelch");
        LoadCatalog();

        if (TrackEngineAudio.TryLoadLoops(out var onThrottle, out var offThrottle))
        {
            var engineBed = new GameObject("Driver radio engine");
            engineBed.transform.SetParent(radio.transform, false);
            engine = new TrackEngineAudio.EngineVoice(engineBed.transform, onThrottle, offThrottle);

            // Unity audio filters need an AudioSource on the object first.
            var highPass = engineBed.AddComponent<AudioHighPassFilter>();
            if (highPass != null)
                highPass.cutoffFrequency = 100f;
            var lowPass = engineBed.AddComponent<AudioLowPassFilter>();
            if (lowPass != null)
                lowPass.cutoffFrequency = 1450f;
        }
    }

    private void LoadCatalog()
    {
        var asset = Resources.Load<TextAsset>(ResourcePath + "Lines");
        if (asset == null)
            return;

        RecordingCatalog catalog;
        try
        {
            catalog = JsonUtility.FromJson<RecordingCatalog>(asset.text);
        }
        catch (System.ArgumentException)
        {
            return;
        }
        IndexRecordings(catalog?.lines, recordings);
        IndexRecordings(catalog?.crewLines, crewRecordings);
    }

    private static void IndexRecordings(Recording[] lines, Dictionary<string, string> index)
    {
        if (lines == null)
            return;

        foreach (var line in lines)
            if (!string.IsNullOrEmpty(line?.text) && !string.IsNullOrEmpty(line.id))
                index[line.text] = line.id;
    }

    private AudioClip LoadClip(string name)
    {
        if (!loadedClips.TryGetValue(name, out var clip))
        {
            clip = Resources.Load<AudioClip>(ResourcePath + name);
            loadedClips[name] = clip;
        }
        return clip;
    }

    internal bool TryPlay(RaceSessionRun run, IReadOnlyList<string> lines, bool urgent, float driverGain,
        string crewMessage, string crewVoice, float crewGain)
    {
        AudioClip crewClip = null;
        if (crewMessage != null && crewRecordings.TryGetValue(crewMessage, out string crewId))
        {
            string prefix = crewVoice == "Roger" ? "Crew" : "Crew" + crewVoice;
            crewClip = LoadClip(prefix + crewId);
        }
        if (lines.Count == 0 && crewClip == null)
            return false;

        string speaker = string.Equals(run.PlayerDriverGender, "Woman", System.StringComparison.OrdinalIgnoreCase)
            ? "Sarah" : "David";
        var clips = new AudioClip[lines.Count];
        for (int i = 0; i < lines.Count; i++)
        {
            if (!recordings.TryGetValue(lines[i], out string id))
                return false;

            clips[i] = LoadClip(speaker + id);
            if (clips[i] == null)
                return false;
        }

        // Keep consecutive incident calls together. A new routine call replaces
        // an older one, and an incident interrupts routine chatter.
        if (activeRun != run || !urgent || !urgentTransmission)
        {
            Stop();
            activeRun = run;
            urgentTransmission = urgent;
        }

        if (crewClip != null)
            pending.Enqueue((crewClip, true, crewGain));
        foreach (var clip in clips)
            pending.Enqueue((clip, false, driverGain));

        if (voice.clip == null)
            BeginTransmission();
        return true;
    }

    internal bool TryPlaySpotter(RaceSessionRun run, SpotterCall call, string speaker, float gain)
    {
        int priority = PlayerSpotter.Priority(call);
        if ((IsPlaying || squelch.isPlaying) &&
            (priority == 0 || spotterPriority > priority || (priority == 1 && IsPlayingUrgent)))
            return false;

        var clip = LoadClip("Spotter" + speaker + call);
        if (clip == null)
            return false;

        Stop();
        activeRun = run;
        urgentTransmission = priority > 0;
        spotterPriority = priority;
        // Like the crew chief, the spotter transmits away from the player's engine.
        pending.Enqueue((clip, true, gain));
        BeginTransmission();
        return true;
    }

    private void BeginTransmission()
    {
        if (staticBed.clip != null)
            staticBed.Play();
        if (squelch.clip != null)
            squelch.Play();
        PlayNext(AudioSettings.dspTime + .15d);
    }

    internal void Update(RaceSessionRun run)
    {
        if (!IsPlaying)
            return;
        if (run != activeRun)
        {
            Stop();
            return;
        }

        if (AudioSettings.dspTime >= speechEnd)
        {
            if (pending.Count > 0)
            {
                bool speakerChanged = crewSpeaking != pending.Peek().Crew;
                if (speakerChanged && squelch.clip != null)
                    squelch.Play();
                PlayNext(AudioSettings.dspTime + (speakerChanged ? .45d : .045d));
            }
            else
            {
                bool wasUrgent = urgentTransmission;
                Stop();
                if (squelch.clip != null)
                {
                    urgentTransmission = wasUrgent;
                    squelch.Play();
                }
                return;
            }
        }

        double rpm = run.EngineRpm(0);
        if (crewSpeaking || rpm <= 0d)
        {
            engine?.Stop();
        }
        else if (engine != null)
        {
            engine.Update(run.TransmissionGear(0), run.ThrottleInput(0),
                Mathf.Min(Time.unscaledDeltaTime, .05f), TrackEngineAudio.PitchForRpm(rpm), 0f, EngineBedGain);
            engine.Play(1f, 0);
        }
    }

    internal void Stop()
    {
        activeRun = null;
        urgentTransmission = false;
        spotterPriority = 0;
        crewSpeaking = false;
        pending.Clear();
        voice.Stop();
        voice.clip = null;
        staticBed.Stop();
        squelch.Stop();
        engine?.Stop();
    }

    private void PlayNext(double start)
    {
        var next = pending.Dequeue();
        crewSpeaking = next.Crew;
        voice.clip = next.Clip;
        voice.volume = VoiceGain * next.Gain;
        speechEnd = start + (double)voice.clip.samples / voice.clip.frequency;
        voice.PlayScheduled(start);
    }

    private static AudioSource MakeSource(GameObject owner, float gain)
    {
        var source = owner.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.priority = 15;
        source.volume = gain;
        return source;
    }
}
