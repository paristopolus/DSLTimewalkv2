using System.Collections;
using UnityEngine;

public class FadeAudio : MonoBehaviour
{
    [Header("Fade Settings")]
    [SerializeField] private float fadeDuration = 5f;
    [Tooltip("Y axis = how much volume to keep. Keep this high for longer to hold the music before it tapers off. Fade in uses this curve reversed.")]
    [SerializeField] private AnimationCurve fadeCurve = new AnimationCurve(
        new Keyframe(0f, 1f),
        new Keyframe(0.7f, 0.92f),
        new Keyframe(1f, 0f)
    );

    [Header("Cross-Scene Bridge (optional)")]
    [SerializeField] private string persistentAudioObjectName;

    private AudioSource audioSource;
    private float defaultVolume;
    private Coroutine fadeCoroutine;
    private const float MinVolume = 0.0001f;

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource != null)
            defaultVolume = audioSource.volume;
    }

    public void CallFadeIn()
    {
        FadeInInternal();
    }

    public void FadeIn()
    {
        FadeInInternal();
    }

    public void CallFadeOut()
    {
        FadeOutInternal();
    }

    public void FadeOut()
    {
        FadeOutInternal();
    }

    private void FadeInInternal()
    {
        if (!TryGetTargetForFade(out FadeAudio target))
            return;

        if (target != this)
        {
            target.FadeInInternal();
            return;
        }

        if (fadeCoroutine != null)
            StopCoroutine(fadeCoroutine);

        fadeCoroutine = StartCoroutine(FadeInRoutine());
    }

    private void FadeOutInternal()
    {
        if (!TryGetTargetForFade(out FadeAudio target))
            return;

        if (target != this)
        {
            target.FadeOutInternal();
            return;
        }

        if (fadeCoroutine != null)
            StopCoroutine(fadeCoroutine);

        fadeCoroutine = StartCoroutine(FadeOutRoutine());
    }

    private bool TryGetTargetForFade(out FadeAudio target)
    {
        target = this;
        if (audioSource != null)
            return true;

        if (string.IsNullOrEmpty(persistentAudioObjectName))
        {
            Debug.LogWarning("[FadeAudio] No AudioSource found to fade. Add an AudioSource to this object, or set Persistent Audio Object Name on a bridge FadeAudio in this scene.");
            return false;
        }

        GameObject persistentAudio = GameObject.Find(persistentAudioObjectName);
        if (persistentAudio == null)
        {
            Debug.LogWarning($"[FadeAudio] Could not find persistent audio object: {persistentAudioObjectName}");
            return false;
        }

        target = persistentAudio.GetComponent<FadeAudio>();
        if (target == null)
        {
            Debug.LogWarning($"[FadeAudio] {persistentAudioObjectName} is missing a FadeAudio component.");
            return false;
        }

        return true;
    }

    public void FadeOutAndStopAt(float clipTime)
    {
        if (audioSource == null)
        {
            Debug.LogWarning("[FadeAudio] No AudioSource found to fade.");
            return;
        }

        if (fadeCoroutine != null)
            StopCoroutine(fadeCoroutine);

        fadeCoroutine = StartCoroutine(FadeOutAtTimeRoutine(clipTime));
    }

    private IEnumerator FadeInRoutine()
    {
        if (!audioSource.isPlaying)
        {
            audioSource.volume = MinVolume;
            audioSource.Play();
        }

        float startVolume = audioSource.volume;
        float elapsed = 0f;
        float startVolumeLog = Mathf.Log10(Mathf.Max(startVolume, MinVolume));
        float targetVolumeLog = Mathf.Log10(Mathf.Max(defaultVolume, MinVolume));

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);
            float fadeAmount = fadeCurve.Evaluate(1f - t);
            audioSource.volume = Mathf.Pow(10f, Mathf.Lerp(startVolumeLog, targetVolumeLog, fadeAmount));
            yield return null;
        }

        audioSource.volume = defaultVolume;
        fadeCoroutine = null;
    }

    private IEnumerator FadeOutRoutine()
    {
        float startVolume = audioSource.volume;
        float elapsed = 0f;
        float startVolumeLog = Mathf.Log10(Mathf.Max(startVolume, MinVolume));
        float minVolumeLog = Mathf.Log10(MinVolume);

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);
            float fadeAmount = 1f - fadeCurve.Evaluate(t);
            audioSource.volume = Mathf.Pow(10f, Mathf.Lerp(startVolumeLog, minVolumeLog, fadeAmount));
            yield return null;
        }

        audioSource.Stop();
        audioSource.volume = defaultVolume;
        fadeCoroutine = null;
    }

    private IEnumerator FadeOutAtTimeRoutine(float stopTime)
    {
        float fadeStart = stopTime - fadeDuration;

        while (audioSource.isPlaying && audioSource.time < fadeStart)
            yield return null;

        yield return FadeOutRoutine();
    }
}
