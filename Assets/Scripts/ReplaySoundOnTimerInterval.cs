using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class ReplaySoundOnTimerInterval : MonoBehaviour
{
    public AudioClip soundClip;

    [Tooltip("Seconds between the first and second beep.")]
    [Min(0.01f)] public float startingInterval = 2f;

    [Tooltip("Shortest interval between beeps, in seconds.")]
    [Min(0.01f)] public float minimumInterval = 0.25f;

    [Tooltip("Multiply the interval by this after each repeat. Lower values speed up sooner.")]
    [Range(0.01f, 1f)] public float intervalMultiplier = 0.85f;

    private AudioSource audioSource;
    private float currentInterval;
    private float timer;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = false;
    }

    private void OnEnable()
    {
        currentInterval = Mathf.Max(0.01f, minimumInterval, startingInterval);
        timer = currentInterval;
        PlayBeep();
    }

    private void Update()
    {
        timer -= Time.deltaTime;
        if (timer > 0f)
            return;

        PlayBeep();
        currentInterval = Mathf.Max(
            Mathf.Max(0.01f, minimumInterval),
            currentInterval * Mathf.Clamp(intervalMultiplier, 0.01f, 1f));
        timer = currentInterval;
    }

    private void OnDisable()
    {
        if (audioSource != null)
            audioSource.Stop();
    }

    private void PlayBeep()
    {
        if (soundClip == null)
            return;

        audioSource.clip = soundClip;
        audioSource.Play();
    }
}
