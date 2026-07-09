using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class ShakeVolumeController : MonoBehaviour
{
    [SerializeField] DreamscapeGrabbable grabbable;
    [SerializeField] AudioSource audioSource;

    [SerializeField] float sensitivity = 0.1f;
    [SerializeField] float maxVolume = 1f;
    [SerializeField] float shakeThreshold = 2f;

    Vector3 _lastPosition;
    bool _hasLastPosition;

    void Awake()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        if (grabbable == null)
            grabbable = GetComponent<DreamscapeGrabbable>();

        if (audioSource != null)
            audioSource.playOnAwake = false;
    }

    void OnEnable()
    {
        _hasLastPosition = false;
    }

    void Update()
    {
        if (audioSource == null || audioSource.clip == null)
            return;

        if (grabbable != null && (grabbable.IsPlacementLocked || grabbable.IsPlaced))
        {
            StopShakeAudio();
            _hasLastPosition = false;
            return;
        }

        Vector3 position = transform.position;

        if (!_hasLastPosition || Time.deltaTime <= 0f)
        {
            _lastPosition = position;
            _hasLastPosition = true;
            return;
        }

        float shakeIntensity = (position - _lastPosition).magnitude / Time.deltaTime;
        _lastPosition = position;

        if (shakeIntensity < shakeThreshold)
            shakeIntensity = 0f;

        ApplyVolume(Mathf.Clamp(shakeIntensity * sensitivity, 0f, maxVolume));
    }

    void OnDisable()
    {
        StopShakeAudio();
        _hasLastPosition = false;
    }

    void ApplyVolume(float volume)
    {
        audioSource.volume = volume;

        if (volume > 0f)
        {
            if (!audioSource.isPlaying)
                audioSource.Play();
            return;
        }

        StopShakeAudio();
    }

    void StopShakeAudio()
    {
        if (audioSource == null)
            return;

        if (audioSource.isPlaying)
            audioSource.Stop();

        audioSource.volume = 0f;
    }

#if UNITY_EDITOR
    public void AutoAssignComponents()
    {
        grabbable = GetComponent<DreamscapeGrabbable>();
        audioSource = GetComponent<AudioSource>();
    }
#endif
}
