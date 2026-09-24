using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(DreamscapeGrabbable))]
public class DreamscapeShakeRelease : MonoBehaviour
{
    [Header("Shake release")]
    [SerializeField] DreamscapeGrabbable grabbable;
    [SerializeField] bool enableShakeRelease = true;
    [Tooltip("Motion slower than this does not count as a shake stroke (m/s).")]
    [SerializeField] float minReversalSpeed = 0.3f;
    [Tooltip("Hand must travel at least this far between counted reversals (meters).")]
    [SerializeField] float minReversalDistance = 0.04f;
    [Tooltip("Direction changes required inside shakeSampleWindow.")]
    [SerializeField] int reversalsToRelease = 3;
    [Tooltip("Rolling window used to count reversals.")]
    [SerializeField] float shakeSampleWindow = 0.5f;
    [Tooltip("Blocks shake-release briefly after grab.")]
    [SerializeField] float shakeGraceSeconds = 0.5f;

    Vector3 _previousHandPosition;
    Vector3 _lastSignificantVelocity;
    Vector3 _positionAtLastReversal;
    bool _hasPreviousHandPosition;
    bool _hasSignificantVelocity;
    bool _hasReversalAnchor;
    readonly Queue<float> _reversalTimes = new Queue<float>();
    float _shakeGraceTimer;

    void Awake()
    {
        if (grabbable == null)
            grabbable = GetComponent<DreamscapeGrabbable>();
    }

    void OnEnable()
    {
        if (grabbable == null)
            grabbable = GetComponent<DreamscapeGrabbable>();

        if (grabbable != null)
            grabbable.onGrabbed.AddListener(HandleGrabbed);
    }

    void OnDisable()
    {
        if (grabbable != null)
            grabbable.onGrabbed.RemoveListener(HandleGrabbed);
    }

    void Update()
    {
        if (grabbable == null || !grabbable.IsGrabbed)
            return;

        if (_shakeGraceTimer > 0f)
            _shakeGraceTimer -= Time.deltaTime;

        if (ShouldReleaseFromShake())
            grabbable.ForceRelease();
    }

    void HandleGrabbed()
    {
        ResetShakeTracking();
        _shakeGraceTimer = shakeGraceSeconds;
    }

    bool ShouldReleaseFromShake()
    {
        if (!enableShakeRelease)
            return false;

        if (!grabbable.TryGetActiveHandTransform(out Transform hand))
            return false;

        TrackShakeReversals(hand);

        if (_shakeGraceTimer > 0f)
            return false;

        return _reversalTimes.Count >= reversalsToRelease;
    }

    void ResetShakeTracking()
    {
        _hasPreviousHandPosition = false;
        _hasSignificantVelocity = false;
        _hasReversalAnchor = false;
        _reversalTimes.Clear();
    }

    void TrackShakeReversals(Transform hand)
    {
        if (Time.deltaTime <= 0f)
            return;

        Vector3 position = hand.position;
        if (!_hasPreviousHandPosition)
        {
            _previousHandPosition = position;
            _hasPreviousHandPosition = true;
            return;
        }

        Vector3 velocity = (position - _previousHandPosition) / Time.deltaTime;
        _previousHandPosition = position;
        float speed = velocity.magnitude;

        if (speed >= minReversalSpeed)
        {
            bool reversed = _hasSignificantVelocity
                && Vector3.Dot(velocity, _lastSignificantVelocity) < 0f;
            bool traveledFarEnough = !_hasReversalAnchor
                || (position - _positionAtLastReversal).sqrMagnitude
                    >= minReversalDistance * minReversalDistance;

            if (reversed && traveledFarEnough)
            {
                _reversalTimes.Enqueue(Time.time);
                _positionAtLastReversal = position;
                _hasReversalAnchor = true;
            }

            _lastSignificantVelocity = velocity;
            _hasSignificantVelocity = true;

            if (!_hasReversalAnchor)
            {
                _positionAtLastReversal = position;
                _hasReversalAnchor = true;
            }
        }

        float cutoff = Time.time - shakeSampleWindow;
        while (_reversalTimes.Count > 0 && _reversalTimes.Peek() < cutoff)
            _reversalTimes.Dequeue();
    }
}
