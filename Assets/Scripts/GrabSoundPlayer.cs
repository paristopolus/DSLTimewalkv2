using RvSdk.Component;
using UnityEngine;

[RequireComponent(typeof(DreamscapeGrabbable))]
[RequireComponent(typeof(AudioSource))]
public class GrabSoundPlayer : MonoBehaviour
{
    public enum AudioPlayEvent
    {
        Pickup,
        Placement,
        Both
    }

    [Header("Playback")]
    [SerializeField] AudioPlayEvent playOn = AudioPlayEvent.Both;

    [Header("References")]
    [SerializeField] DreamscapeGrabbable grabbable;
    [SerializeField] AudioSource audioSource;
    [SerializeField] AudioClip placementClip;

    int _lastState = PlaceableObjectNetworkState.Free;
    bool _subscribed;

    void Awake()
    {
        if (grabbable == null)
            grabbable = GetComponent<DreamscapeGrabbable>();

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        if (audioSource != null)
            audioSource.playOnAwake = false;
    }

    void OnEnable()
    {
        SubscribeToStateSync();
    }

    void Start()
    {
        SubscribeToStateSync();

        if (grabbable != null)
            _lastState = grabbable.StateValue;
    }

    void OnDisable()
    {
        UnsubscribeFromStateSync();
    }

    void SubscribeToStateSync()
    {
        if (_subscribed || grabbable?.StateSync == null)
            return;

        grabbable.StateSync.OnValueChanged.AddListener(OnStateChanged);
        _subscribed = true;
    }

    void UnsubscribeFromStateSync()
    {
        if (!_subscribed || grabbable?.StateSync == null)
            return;

        grabbable.StateSync.OnValueChanged.RemoveListener(OnStateChanged);
        _subscribed = false;
    }

    void OnStateChanged(int newValue)
    {
        int previous = _lastState;
        _lastState = newValue;

        if (PlaceableObjectNetworkState.IsFree(previous) && PlaceableObjectNetworkState.IsHeld(newValue))
        {
            if (playOn == AudioPlayEvent.Pickup || playOn == AudioPlayEvent.Both)
                PlayGrabAudio();
            return;
        }

        if (PlaceableObjectNetworkState.IsHeld(previous) && PlaceableObjectNetworkState.IsFree(newValue))
        {
            if (playOn == AudioPlayEvent.Pickup || playOn == AudioPlayEvent.Both)
                StopAudio();
            return;
        }

        if (!PlaceableObjectNetworkState.IsPlaced(previous) && PlaceableObjectNetworkState.IsPlaced(newValue))
        {
            if (playOn == AudioPlayEvent.Placement || playOn == AudioPlayEvent.Both)
                PlayPlacementAudio();
        }
    }

    void PlayGrabAudio()
    {
        if (audioSource == null || audioSource.clip == null)
            return;

        audioSource.Play();
    }

    void PlayPlacementAudio()
    {
        StopAudio();

        AudioClip clip = placementClip != null ? placementClip : audioSource.clip;
        if (audioSource == null || clip == null)
            return;

        audioSource.PlayOneShot(clip);
    }

    void StopAudio()
    {
        if (audioSource != null && audioSource.isPlaying)
            audioSource.Stop();
    }

#if UNITY_EDITOR
    public void AutoAssignComponents()
    {
        grabbable = GetComponent<DreamscapeGrabbable>();
        audioSource = GetComponent<AudioSource>();

        if (audioSource != null && placementClip == null && audioSource.clip != null)
            placementClip = audioSource.clip;
    }
#endif
}
