using RvSdk.Component;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class ShakeVolumeController : MonoBehaviour
{
    [SerializeField] DreamscapeGrabbable grabbable;
    [SerializeField] AudioSource audioSource;
    [SerializeField] float sensitivity = 1f;
    [SerializeField] float maxVolume = 1f;
    [SerializeField] float shakeThreshold = 0.1f;
    [SerializeField] float soundCooldown = 3f;

    ClientToServerTrigger _clientToServerTrigger;
    Vector3 _lastPosition;
    bool _hasLastPosition;
    float _lastSoundTime;
    float _currentVolume;

    void Awake()
    {
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        if (grabbable == null)
            grabbable = GetComponent<DreamscapeGrabbable>();

        _clientToServerTrigger = GetComponent<ClientToServerTrigger>();

        if (audioSource != null)
        {
            audioSource.clip = null;
            audioSource.playOnAwake = false;
            audioSource.loop = false;
        }
    }

    void OnEnable()
    {
        _hasLastPosition = false;
        _currentVolume = 0f;
        _lastSoundTime = -soundCooldown;
    }

    void Update()
    {
        if (audioSource == null)
            return;

        if (grabbable != null && (grabbable.IsPlacementLocked || grabbable.IsPlaced))
        {
            _currentVolume = 0f;
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

        _currentVolume = Mathf.Clamp(shakeIntensity * sensitivity, 0f, maxVolume);

        if (shakeIntensity < shakeThreshold)
            return;

        if (_clientToServerTrigger == null || grabbable == null || !grabbable.IsHeldByLocalPlayer)
            return;

        if (Time.time - _lastSoundTime <= soundCooldown)
            return;

        _lastSoundTime = Time.time;
        _clientToServerTrigger.Trigger();
    }

    void LateUpdate()
    {
        if (audioSource != null)
            audioSource.volume = _currentVolume;
    }

#if UNITY_EDITOR
    public void AutoAssignComponents()
    {
        grabbable = GetComponent<DreamscapeGrabbable>();
        audioSource = GetComponent<AudioSource>();
    }
#endif
}
