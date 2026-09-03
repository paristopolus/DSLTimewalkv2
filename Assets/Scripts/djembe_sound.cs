using System.Collections.Generic;
using RvSdk.Avatar;
using RvSdk.Component;
using RvSdk.Controller;
using RvSdk.Module;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class djembe_sound : MonoBehaviour
{
    [Header("Hit detection")]
    [SerializeField] float handMatchRadius = 0.25f;
    [SerializeField] float minHitSpeed = 0.75f;
    [SerializeField] float hitCooldownSeconds = 0.12f;

    [Header("Impact clips")]
    [SerializeField] float mediumThreshold = 2.5f;
    [SerializeField] float hardThreshold = 5f;

    ClientToServerTrigger _clientTrigger;
    NetworkSound _networkSound;
    Collider _drumCollider;

    float _lastHitTime = float.NegativeInfinity;
    bool _hasPreviousLeftHand;
    bool _hasPreviousRightHand;
    Vector3 _previousLeftHandPosition;
    Vector3 _previousRightHandPosition;

    readonly Dictionary<int, Vector3> _colliderPreviousPositions = new Dictionary<int, Vector3>();

    void Awake()
    {
        _clientTrigger = GetComponent<ClientToServerTrigger>();
        _networkSound = GetComponent<NetworkSound>();
        _drumCollider = GetComponent<Collider>();

        Rigidbody body = GetComponent<Rigidbody>();
        if (body != null)
        {
            body.isKinematic = true;
            body.useGravity = false;
        }
    }

    void OnEnable()
    {
        if (_clientTrigger != null)
            _clientTrigger.OnTriggerWithArg.AddListener(HandleServerHit);
    }

    void OnDisable()
    {
        if (_clientTrigger != null)
            _clientTrigger.OnTriggerWithArg.RemoveListener(HandleServerHit);

        _colliderPreviousPositions.Clear();
        _hasPreviousLeftHand = false;
        _hasPreviousRightHand = false;
    }

    void Update()
    {
        if (!CanDetectHits() || !TryGetLocalHandTransforms(out Transform leftHand, out Transform rightHand))
            return;

        if (leftHand != null)
        {
            _previousLeftHandPosition = leftHand.position;
            _hasPreviousLeftHand = true;
        }

        if (rightHand != null)
        {
            _previousRightHandPosition = rightHand.position;
            _hasPreviousRightHand = true;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (!CanDetectHits() || _clientTrigger == null)
            return;

        if (!TryGetLocalHandHit(other, out float hitSpeed))
            return;

        if (hitSpeed < minHitSpeed || Time.time - _lastHitTime < hitCooldownSeconds)
            return;

        _lastHitTime = Time.time;
        _clientTrigger.Trigger(SelectClipIndex(hitSpeed).ToString());
    }

    public void HandleServerHit(string triggerName, AvatarController avatar, string clipIndex)
    {
        if (!NetworkController.IsServer || _networkSound == null)
            return;

        if (!int.TryParse(clipIndex, out int index))
            index = 0;

        _networkSound.PlayOnce(index);
    }

    bool CanDetectHits()
    {
        return NetworkGate.IsInitialized && NetworkGate.IsClient;
    }

    bool TryGetLocalHandHit(Collider other, out float hitSpeed)
    {
        hitSpeed = 0f;

        AvatarController avatar = other.GetComponentInParent<AvatarController>();
        if (avatar == null)
            return false;

        AvatarController localAvatar = GameController.Instance?.CurrentPlayer?.AvatarController;
        if (localAvatar == null || avatar != localAvatar)
            return false;

        if (_drumCollider == null || !TryGetLocalHandTransforms(out Transform leftHand, out Transform rightHand))
            return false;

        float leftDistance = GetHandDistanceToDrum(leftHand);
        float rightDistance = GetHandDistanceToDrum(rightHand);

        if (Mathf.Min(leftDistance, rightDistance) > handMatchRadius)
            return false;

        if (leftDistance <= rightDistance)
            hitSpeed = GetTrackedHandSpeed(leftHand, _hasPreviousLeftHand, _previousLeftHandPosition);
        else
            hitSpeed = GetTrackedHandSpeed(rightHand, _hasPreviousRightHand, _previousRightHandPosition);

        if (hitSpeed <= 0f)
            hitSpeed = GetColliderSpeed(other);

        return true;
    }

    float GetHandDistanceToDrum(Transform hand)
    {
        if (hand == null)
            return float.MaxValue;

        return Vector3.Distance(_drumCollider.ClosestPoint(hand.position), hand.position);
    }

    float GetTrackedHandSpeed(Transform hand, bool hasPreviousPosition, Vector3 previousPosition)
    {
        if (hand == null || !hasPreviousPosition || Time.deltaTime <= 0f)
            return 0f;

        return (hand.position - previousPosition).magnitude / Time.deltaTime;
    }

    float GetColliderSpeed(Collider other)
    {
        int id = other.GetInstanceID();
        Vector3 position = other.transform.position;

        if (!_colliderPreviousPositions.TryGetValue(id, out Vector3 previousPosition) || Time.deltaTime <= 0f)
        {
            _colliderPreviousPositions[id] = position;
            return 0f;
        }

        float speed = (position - previousPosition).magnitude / Time.deltaTime;
        _colliderPreviousPositions[id] = position;
        return speed;
    }

    bool TryGetLocalHandTransforms(out Transform leftHand, out Transform rightHand)
    {
        leftHand = null;
        rightHand = null;

        Animator animator = GameController.Instance?.CurrentPlayer?.AvatarController?.AvatarAnimator;
        if (animator == null)
            return false;

        leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        return leftHand != null || rightHand != null;
    }

    int SelectClipIndex(float hitSpeed)
    {
        if (hitSpeed >= hardThreshold)
            return 2;

        if (hitSpeed >= mediumThreshold)
            return 1;

        return 0;
    }
}
