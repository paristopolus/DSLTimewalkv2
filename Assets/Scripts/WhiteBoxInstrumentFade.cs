using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Playables;

/// <summary>Fades the white-box instruments only after ActivateMaracas is received.</summary>
public class WhiteBoxInstrumentFade : MonoBehaviour
{
    [SerializeField] private PlayableDirector timeline;
    [SerializeField] private Transform instrumentsRoot;
    [SerializeField] private AudioMixerGroup instrumentGroup;
    [SerializeField] private float silentAtTimelineSeconds = 70f;

    private const string VolumeParameter = "InstrumentVolume";
    private AudioSource[] sources;
    private bool[] originalMutes;
    private AudioMixerGroup[] originalGroups;
    private bool fading;
    private double fadeStart;

    private void Awake()
    {
        if (instrumentsRoot == null || instrumentGroup == null || timeline == null)
        {
            Debug.LogError("WhiteBoxInstrumentFade needs a timeline, instrument root, and mixer group.", this);
            enabled = false;
            return;
        }

        // Include inactive instruments and retain references when grabbed/reparented.
        sources = instrumentsRoot.GetComponentsInChildren<AudioSource>(true);
        originalMutes = new bool[sources.Length];
        originalGroups = new AudioMixerGroup[sources.Length];
        for (int i = 0; i < sources.Length; i++)
        {
            originalMutes[i] = sources[i].mute;
            originalGroups[i] = sources[i].outputAudioMixerGroup;
            sources[i].outputAudioMixerGroup = instrumentGroup;
        }
    }

    private void Start()
    {
        // SetFloat is deferred until Start, after mixer initialization.
        if (!fading)
            ResetFade();
    }

    // Wired alongside the existing maraca activation callbacks in SignalReceiver.
    public void BeginFade()
    {
        if (!enabled || timeline == null || instrumentGroup == null)
            return;

        if (fading && timeline.time >= fadeStart)
            return;

        fadeStart = timeline.time;
        fading = true;
    }

    private void LateUpdate()
    {
        if (!fading)
            return;

        if (timeline.time < fadeStart)
        {
            ResetFade();
            return;
        }

        double duration = silentAtTimelineSeconds - fadeStart;
        float progress = duration <= 0d ? 1f : Mathf.Clamp01((float)((timeline.time - fadeStart) / duration));
        // Mixer attenuation is independent of ShakeVolumeController's per-frame volume.
        instrumentGroup.audioMixer.SetFloat(VolumeParameter, Mathf.Lerp(0f, -80f, progress));
        SetSilent(progress >= 1f);
    }

    private void ResetFade()
    {
        fading = false;
        instrumentGroup.audioMixer.SetFloat(VolumeParameter, 0f);
        SetSilent(false);
    }

    private void SetSilent(bool silent)
    {
        for (int i = 0; i < sources.Length; i++)
            if (sources[i] != null)
                sources[i].mute = silent || originalMutes[i];
    }

    private void OnDestroy()
    {
        if (sources == null)
            return;

        for (int i = 0; i < sources.Length; i++)
        {
            if (sources[i] == null)
                continue;
            sources[i].outputAudioMixerGroup = originalGroups[i];
            sources[i].mute = originalMutes[i];
        }
    }
}
