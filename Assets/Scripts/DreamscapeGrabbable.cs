using System;
using System.Collections.Generic;
using RvSdk.Avatar;
using RvSdk.Component;
using RvSdk.Controller;
using RvSdk.Module;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

[RequireComponent(typeof(NetworkSyncedTransform))]
[RequireComponent(typeof(NetworkSyncedInteger))]
[RequireComponent(typeof(ClientToServerTrigger))]
public class DreamscapeGrabbable : MonoBehaviour
{
    // synced int value when nobody is holding this object
    public const int HolderNone = -1;

    // sent through ClientToServerTrigger as the trigger argument
    public const string GrabTrigger = "Grab";
    public const string ReleaseTrigger = "Release";

    public enum HandPreference
    {
        Either,
        LeftOnly,
        RightOnly
    }

    [Header("Networking")]
    // one integer encodes free/held/placed — see PlaceableObjectNetworkState
    [Tooltip("Single NetworkSyncedInteger for grab + placed state.")]
    [FormerlySerializedAs("holderSync")]
    [SerializeField] NetworkSyncedInteger stateSync;

    [Header("Grab detection")]
    // which hand(s) can pick this up
    [SerializeField] HandPreference handPreference = HandPreference.Either;
    // hand must stay within this distance for grabDwellSeconds before grab fires
    [SerializeField] float grabRadius = 0.4f;
    [SerializeField] float grabDwellSeconds = 0.15f;
    // after drop, ignore proximity grab for this long unless ForceReleaseAllowingRegrab
    [SerializeField] float regrabBlockSeconds = 0.5f;

    [Header("Attach")]
    [SerializeField] Transform leftAttachPoint;
    [SerializeField] Transform rightAttachPoint;
    [SerializeField] Vector3 localAttachOffset = Vector3.zero;
    [SerializeField] Vector3 localAttachEuler;
    [SerializeField] bool matchHandRotation;

    [Header("Physics")]
    [SerializeField] bool useGravityWhenReleased;

    [Header("Events")]
    public UnityEvent onGrabbed;
    public UnityEvent onReleased;
    public UnityEvent onPlacementLocked;

    NetworkSyncedTransform _syncedTransform;
    NetworkSyncedInteger _stateSync;
    ClientToServerTrigger _serverTrigger;
    Rigidbody _rigidbody;

    float _dwellTimer;
    bool _isGrabbedLocally;
    bool _placementLocked;
    HumanBodyBones _activeHand = HumanBodyBones.LastBone;
    HumanBodyBones _pendingGrabHand = HumanBodyBones.LastBone;
    float _regrabBlockTimer;
    bool _mustLeaveBeforeRegrab;
    bool _allowImmediateRegrabOnRelease;
    Quaternion _rotationWhileHeld;
    bool _syncedRotationWhileHeld;
    Vector3 _gripOffsetInObjectSpace;
    Quaternion _gripRotationInObjectSpace = Quaternion.identity;
    bool _hasGripAttach;
    bool _pendingInitialGrabSnap;
    Vector3 _twistAxisAtGrab;
    bool _transformRegistered;

    static readonly HashSet<string> RegisteredTransformIds = new HashSet<string>();

    public bool IsGrabbed => _isGrabbedLocally;
    public HumanBodyBones ActiveHand => _activeHand;
    public bool IsPlacementLocked => _placementLocked;
    public NetworkSyncedInteger StateSync => _stateSync;
    public int StateValue => _stateSync != null ? _stateSync.Value : PlaceableObjectNetworkState.Free;
    public int HolderHash => PlaceableObjectNetworkState.GetHolderHash(StateValue);
    public bool IsPlaced => PlaceableObjectNetworkState.IsPlaced(StateValue);

    public bool IsHeldByLocalPlayer =>
        _isGrabbedLocally && IsLocalPlayerHash(HolderHash);

    public bool IsHeldByAnyone => PlaceableObjectNetworkState.IsHeld(StateValue);

    void Awake()
    {
        _syncedTransform = GetComponent<NetworkSyncedTransform>();
        _stateSync = stateSync != null ? stateSync : GetComponent<NetworkSyncedInteger>();
        _serverTrigger = GetComponent<ClientToServerTrigger>();
        _rigidbody = GetComponent<Rigidbody>();
    }

    void OnEnable()
    {
        if (_stateSync != null)
            _stateSync.OnValueChanged.AddListener(OnStateChanged);

        if (_serverTrigger != null)
            _serverTrigger.OnTriggerWithArg.AddListener(HandleServerTriggerWithArg);

        if (IsPlaced)
            _placementLocked = true;
    }

    void OnDisable()
    {
        if (_stateSync != null)
            _stateSync.OnValueChanged.RemoveListener(OnStateChanged);

        if (_serverTrigger != null)
            _serverTrigger.OnTriggerWithArg.RemoveListener(HandleServerTriggerWithArg);

        if (_isGrabbedLocally)
            EndGrabLocal();
    }

    void Update()
    {
        if (!_isGrabbedLocally && _regrabBlockTimer > 0f)
            _regrabBlockTimer -= Time.deltaTime;

        // all proximity + dwell logic runs on the local client only
        if (!CanRunLocalGrabLogic())
            return;

        if (_isGrabbedLocally)
        {
            if (ShouldRelease())
                RequestRelease();
            return;
        }

        // someone else is holding it on the network
        if (HolderHash != HolderNone)
        {
            _dwellTimer = 0f;
            return;
        }

        if (!TryGetClosestAvailableHand(out _, out float distance))
        {
            _dwellTimer = 0f;
            _mustLeaveBeforeRegrab = false;
            return;
        }

        if (_mustLeaveBeforeRegrab)
        {
            if (distance > grabRadius)
                _mustLeaveBeforeRegrab = false;
            else
            {
                _dwellTimer = 0f;
                return;
            }
        }

        if (_regrabBlockTimer > 0f)
        {
            _dwellTimer = 0f;
            return;
        }

        if (distance <= grabRadius)
        {
            _dwellTimer += Time.deltaTime;
            // hand stayed close long enough — ask server to assign holder
            if (_dwellTimer >= grabDwellSeconds && TryGetClosestAvailableHand(out HumanBodyBones hand, out _))
            {
                _pendingGrabHand = hand;
                RequestGrab();
            }
        }
        else
        {
            _dwellTimer = 0f;
        }
    }

    void FixedUpdate()
    {
        if (!_isGrabbedLocally || !NetworkGate.IsInitialized || !NetworkGate.IsClient)
            return;

        // move the object in physics step while this client is the transform source
        UpdateHeldPose();
    }

    bool CanRunLocalGrabLogic()
    {
        if (_placementLocked || IsPlaced)
            return false;

        // needs a live dreamscape client with a spawned avatar
        if (!NetworkGate.IsInitialized || !NetworkGate.IsClient)
            return false;

        if (GameController.Instance == null || GameController.Instance.CurrentPlayer == null)
            return false;

        if (HolderHash != HolderNone && !IsLocalPlayerHash(HolderHash))
            return false;

        return true;
    }

    void RequestGrab()
    {
        if (!PlaceableObjectNetworkState.IsFree(StateValue))
            return;

        _dwellTimer = 0f;
        // client asks, server writes stateSync in HandleServerTriggerWithArg
        _serverTrigger.Trigger(GrabTrigger);
    }

    void RequestRelease()
    {
        _serverTrigger.Trigger(ReleaseTrigger);
    }

    void HandleServerTriggerWithArg(string triggerName, AvatarController avatar, string argument)
    {
        if (!NetworkController.IsServer || avatar == null)
            return;

        if (_placementLocked || IsPlaced)
            return;

        // server is authoritative for who holds the object
        if (argument == GrabTrigger)
            TryAssignHolder(avatar);
        else if (argument == ReleaseTrigger)
            TryClearHolder(avatar);
    }

    void TryAssignHolder(AvatarController avatar)
    {
        if (!PlaceableObjectNetworkState.IsFree(_stateSync.Value))
            return;

        // player id hash becomes the held state until release or placement
        _stateSync.Value = avatar.PlayerId.GetHashCode();
    }

    void TryClearHolder(AvatarController avatar)
    {
        if (!PlaceableObjectNetworkState.IsHeld(_stateSync.Value))
            return;

        if (_stateSync.Value != avatar.PlayerId.GetHashCode())
            return;

        _stateSync.Value = HolderNone;
    }

    void OnStateChanged(int newValue)
    {
        if (PlaceableObjectNetworkState.IsPlaced(newValue))
        {
            if (_isGrabbedLocally)
                EndGrabLocal();

            _placementLocked = true;
            return;
        }

        if (_placementLocked)
            return;

        // stateSync changed on all clients — start/stop local follow from holder hash
        bool isHeld = PlaceableObjectNetworkState.IsHeld(newValue);
        int holderHash = PlaceableObjectNetworkState.GetHolderHash(newValue);
        bool shouldHoldLocally = IsLocalPlayerHash(holderHash);

        if (isHeld)
            ApplyHeldPhysicsState();
        else
            ApplyReleasedPhysicsState();

        if (shouldHoldLocally && !_isGrabbedLocally)
            BeginGrabLocal();
        else if (!shouldHoldLocally && _isGrabbedLocally)
            EndGrabLocal();
    }

    void ApplyHeldPhysicsState()
    {
        if (_rigidbody == null)
            return;

        ClearRigidbodyVelocities();
        _rigidbody.isKinematic = true;
        _rigidbody.useGravity = false;
    }

    void ApplyReleasedPhysicsState()
    {
        if (_rigidbody == null || _placementLocked)
            return;

        if (useGravityWhenReleased)
        {
            _rigidbody.isKinematic = false;
            _rigidbody.useGravity = true;
            ClearRigidbodyVelocities();
            return;
        }

        _rigidbody.isKinematic = true;
        _rigidbody.useGravity = false;
    }

    void ClearRigidbodyVelocities()
    {
        // unity warns if you set velocity on a kinematic body
        if (_rigidbody == null || _rigidbody.isKinematic)
            return;

        _rigidbody.velocity = Vector3.zero;
        _rigidbody.angularVelocity = Vector3.zero;
    }

    void BeginGrabLocal()
    {
        HumanBodyBones hand = _pendingGrabHand;
        _pendingGrabHand = HumanBodyBones.LastBone;

        if (!IsHandAllowed(hand) || !LocalHandOccupancy.IsHandAvailable(hand))
        {
            if (!TryGetClosestAvailableHand(out hand, out _))
            {
                RequestRelease();
                return;
            }
        }

        if (!LocalHandOccupancy.TryRegister(hand, this))
        {
            RequestRelease();
            return;
        }

        _isGrabbedLocally = true;
        _activeHand = hand;
        _rotationWhileHeld = transform.rotation;
        _twistAxisAtGrab = transform.forward;
        _pendingInitialGrabSnap = matchHandRotation;
        CacheGripAttach(GetGripPointTransform(_activeHand));
        EnsureTransformRegistered();
        // this client drives NetworkSyncedTransform while holding
        _syncedTransform.IsSource = true;
        _syncedRotationWhileHeld = _syncedTransform.SyncRotation;
        _syncedTransform.SyncRotation = matchHandRotation;
        _regrabBlockTimer = 0f;
        _mustLeaveBeforeRegrab = false;
        _allowImmediateRegrabOnRelease = false;

        onGrabbed.Invoke();
        UpdateHeldPose();
    }

    void EndGrabLocal()
    {
        if (_activeHand != HumanBodyBones.LastBone)
            LocalHandOccupancy.Unregister(_activeHand, this);

        _isGrabbedLocally = false;
        _activeHand = HumanBodyBones.LastBone;
        _pendingGrabHand = HumanBodyBones.LastBone;
        _hasGripAttach = false;
        _pendingInitialGrabSnap = false;
        _syncedTransform.IsSource = false;
        _syncedTransform.SyncRotation = _syncedRotationWhileHeld;

        if (_allowImmediateRegrabOnRelease)
        {
            _regrabBlockTimer = 0f;
            _mustLeaveBeforeRegrab = false;
        }
        else
        {
            _regrabBlockTimer = regrabBlockSeconds;
            _mustLeaveBeforeRegrab = true;
        }

        _allowImmediateRegrabOnRelease = false;
        onReleased.Invoke();
    }

    void UpdateHeldPose()
    {
        Transform hand = GetHandTransform(_activeHand);
        if (hand == null)
            return;

        Quaternion handAttachRotation = hand.rotation * Quaternion.Euler(localAttachEuler);
        Quaternion targetRotation;

        if (matchHandRotation)
        {
            Quaternion handAlignedRotation = handAttachRotation * Quaternion.Inverse(_gripRotationInObjectSpace);
            Quaternion swing = GetSwing(handAlignedRotation, _twistAxisAtGrab);
            Quaternion twist = _pendingInitialGrabSnap
                ? GetTwist(_rotationWhileHeld, _twistAxisAtGrab)
                : GetTwist(handAlignedRotation, _twistAxisAtGrab);
            targetRotation = swing * twist;

            if (_pendingInitialGrabSnap)
                _pendingInitialGrabSnap = false;
        }
        else
        {
            targetRotation = _rotationWhileHeld;
        }

        Vector3 targetGripPosition = hand.position + hand.rotation * localAttachOffset;
        Vector3 targetPosition = _hasGripAttach
            ? targetGripPosition - targetRotation * _gripOffsetInObjectSpace
            : targetGripPosition;

        ApplyHeldPose(targetPosition, targetRotation);
    }

    static Quaternion GetTwist(Quaternion rotation, Vector3 twistAxis)
    {
        if (twistAxis.sqrMagnitude < 1e-8f)
            return Quaternion.identity;

        twistAxis.Normalize();
        Vector3 vectorPart = new Vector3(rotation.x, rotation.y, rotation.z);
        Vector3 twistPart = Vector3.Project(vectorPart, twistAxis);

        if (twistPart.sqrMagnitude < 1e-8f)
            return Quaternion.identity;

        return Quaternion.Normalize(new Quaternion(twistPart.x, twistPart.y, twistPart.z, rotation.w));
    }

    static Quaternion GetSwing(Quaternion rotation, Vector3 twistAxis)
    {
        return rotation * Quaternion.Inverse(GetTwist(rotation, twistAxis));
    }

    void CacheGripAttach(Transform gripPoint)
    {
        if (gripPoint == null || gripPoint == transform)
        {
            _hasGripAttach = false;
            _gripRotationInObjectSpace = Quaternion.identity;
            return;
        }

        _gripOffsetInObjectSpace = Quaternion.Inverse(transform.rotation) * (gripPoint.position - transform.position);
        _gripRotationInObjectSpace = Quaternion.Inverse(transform.rotation) * gripPoint.rotation;
        _hasGripAttach = true;
    }

    Transform GetGripPointTransform(HumanBodyBones hand)
    {
        Transform point = hand == HumanBodyBones.LeftHand ? leftAttachPoint : rightAttachPoint;
        if (point == null || point == transform)
            return null;

        return point;
    }

    void ApplyHeldPose(Vector3 targetPosition, Quaternion targetRotation)
    {
        if (_rigidbody != null && _rigidbody.isKinematic)
        {
            _rigidbody.MovePosition(targetPosition);
            _rigidbody.MoveRotation(targetRotation);
            return;
        }

        transform.SetPositionAndRotation(targetPosition, targetRotation);
    }

    void EnsureTransformRegistered()
    {
        if (_transformRegistered || _syncedTransform == null)
            return;

        if (!NetworkGate.IsInitialized || !NetworkGate.IsClient)
            return;

        string transformId = _syncedTransform.Id;
        if (string.IsNullOrEmpty(transformId))
            return;

        if (RegisteredTransformIds.Contains(transformId))
        {
            _transformRegistered = true;
            return;
        }

        // Register at grab time so local pre-game moves (note raise, puzzle lift) are not overwritten by sync.
        _syncedTransform.Register(true);
        RegisteredTransformIds.Add(transformId);
        _transformRegistered = true;
    }

    bool ShouldRelease()
    {
        return !TryGetHandTransform(_activeHand, out _);
    }

    public bool TryGetActiveHandTransform(out Transform hand)
    {
        return TryGetHandTransform(_activeHand, out hand);
    }

    float GetHandGrabDistance(HumanBodyBones handBone)
    {
        if (!TryGetHandTransform(handBone, out Transform hand))
            return float.MaxValue;

        Transform attachPoint = handBone == HumanBodyBones.LeftHand ? leftAttachPoint : rightAttachPoint;
        Vector3 grabTarget = attachPoint != null ? attachPoint.position : transform.position;
        return Vector3.Distance(hand.position, grabTarget);
    }

    bool TryGetClosestAvailableHand(out HumanBodyBones hand, out float distance)
    {
        hand = HumanBodyBones.LastBone;
        distance = float.MaxValue;

        if (handPreference == HandPreference.LeftOnly || handPreference == HandPreference.Either)
        {
            if (LocalHandOccupancy.IsHandAvailable(HumanBodyBones.LeftHand))
            {
                float leftDistance = GetHandGrabDistance(HumanBodyBones.LeftHand);
                if (leftDistance < distance)
                {
                    distance = leftDistance;
                    hand = HumanBodyBones.LeftHand;
                }
            }
        }

        if (handPreference == HandPreference.RightOnly || handPreference == HandPreference.Either)
        {
            if (LocalHandOccupancy.IsHandAvailable(HumanBodyBones.RightHand))
            {
                float rightDistance = GetHandGrabDistance(HumanBodyBones.RightHand);
                if (rightDistance < distance)
                {
                    distance = rightDistance;
                    hand = HumanBodyBones.RightHand;
                }
            }
        }

        return hand != HumanBodyBones.LastBone;
    }

    bool IsHandAllowed(HumanBodyBones hand)
    {
        if (hand == HumanBodyBones.LeftHand)
            return handPreference == HandPreference.Either || handPreference == HandPreference.LeftOnly;

        if (hand == HumanBodyBones.RightHand)
            return handPreference == HandPreference.Either || handPreference == HandPreference.RightOnly;

        return false;
    }

    bool TryGetClosestLocalHand(out HumanBodyBones hand, out float distance)
    {
        hand = HumanBodyBones.LastBone;
        distance = float.MaxValue;

        if (handPreference == HandPreference.LeftOnly || handPreference == HandPreference.Either)
        {
            float leftDistance = GetHandGrabDistance(HumanBodyBones.LeftHand);
            if (leftDistance < distance)
            {
                distance = leftDistance;
                hand = HumanBodyBones.LeftHand;
            }
        }

        if (handPreference == HandPreference.RightOnly || handPreference == HandPreference.Either)
        {
            float rightDistance = GetHandGrabDistance(HumanBodyBones.RightHand);
            if (rightDistance < distance)
            {
                distance = rightDistance;
                hand = HumanBodyBones.RightHand;
            }
        }

        return hand != HumanBodyBones.LastBone;
    }

    bool TryGetHandTransform(HumanBodyBones handBone, out Transform hand)
    {
        hand = GetHandTransform(handBone);
        return hand != null;
    }

    Transform GetHandTransform(HumanBodyBones handBone)
    {
        // dreamscape avatar hand bones
        RuntimePlayer localPlayer = GameController.Instance?.CurrentPlayer;
        Animator animator = localPlayer?.AvatarController?.AvatarAnimator;
        if (animator == null)
            return null;

        return animator.GetBoneTransform(handBone);
    }

    bool IsLocalPlayerHash(int holderHash)
    {
        if (holderHash == HolderNone)
            return false;

        Guid? localPlayerId = GameController.Instance?.CurrentPlayer?.AvatarController?.PlayerId;
        return localPlayerId.HasValue && localPlayerId.Value.GetHashCode() == holderHash;
    }

    public void ForceRelease()
    {
        ForceRelease(false);
    }

    // waist drop uses this so the player can grab the object again without pulling away first
    public void ForceReleaseAllowingRegrab()
    {
        ForceRelease(true);
    }

    public void ForceRelease(bool allowImmediateRegrab)
    {
        if (IsPlaced)
            return;

        _allowImmediateRegrabOnRelease = allowImmediateRegrab;

        if (_isGrabbedLocally)
            RequestRelease();
        else if (NetworkController.IsServer && PlaceableObjectNetworkState.IsHeld(_stateSync.Value))
            _stateSync.Value = HolderNone;
    }

    public bool IsHeldBy(AvatarController avatar)
    {
        if (avatar == null)
            return false;

        return PlaceableObjectNetworkState.IsHeld(StateValue)
            && StateValue == avatar.PlayerId.GetHashCode();
    }

    public void SetPlacedOnServer(PlacementZone zone)
    {
        if (!NetworkController.IsServer || zone == null || IsPlaced)
            return;

        // encoded placed value — PlaceableObject listens and snaps on all clients
        _stateSync.Value = zone.PlacedStateValue;
    }

    public void SnapToWorldPose(Vector3 position, Quaternion rotation, bool lockPlacement = true)
    {
        ForceRelease();
        transform.SetPositionAndRotation(position, rotation);

        if (_rigidbody != null)
        {
            _rigidbody.isKinematic = true;
            _rigidbody.useGravity = false;
        }

        if (lockPlacement)
            LockPlacement();
    }

    public void LockPlacement()
    {
        // after this, grab and place logic both bail out
        _placementLocked = true;
        ForceRelease();
        onPlacementLocked.Invoke();
    }

    public void UnlockPlacement()
    {
        _placementLocked = false;
    }

#if UNITY_EDITOR
    public void AutoAssignComponents()
    {
        stateSync = GetComponent<NetworkSyncedInteger>();

        Transform leftChild = FindAttachChild("LeftAttachPoint");
        if (leftChild != null && leftChild != transform)
            leftAttachPoint = leftChild;

        Transform rightChild = FindAttachChild("RightAttachPoint");
        if (rightChild != null && rightChild != transform)
            rightAttachPoint = rightChild;
    }

    Transform FindAttachChild(string name)
    {
        Transform child = transform.Find(name);
        if (child != null)
            return child;

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform candidate = transform.GetChild(i);
            if (candidate.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return null;
    }
#endif

    static class LocalHandOccupancy
    {
        // local-only tracker so one object per hand on this client
        static DreamscapeGrabbable _leftHandOccupant;
        static DreamscapeGrabbable _rightHandOccupant;

        public static bool IsHandAvailable(HumanBodyBones hand)
        {
            return GetOccupant(hand) == null;
        }

        public static bool TryRegister(HumanBodyBones hand, DreamscapeGrabbable grabbable)
        {
            if (grabbable == null || !IsHandAvailable(hand))
                return false;

            if (hand == HumanBodyBones.LeftHand)
                _leftHandOccupant = grabbable;
            else if (hand == HumanBodyBones.RightHand)
                _rightHandOccupant = grabbable;
            else
                return false;

            return true;
        }

        public static void Unregister(HumanBodyBones hand, DreamscapeGrabbable grabbable)
        {
            if (hand == HumanBodyBones.LeftHand && _leftHandOccupant == grabbable)
                _leftHandOccupant = null;
            else if (hand == HumanBodyBones.RightHand && _rightHandOccupant == grabbable)
                _rightHandOccupant = null;
        }

        static DreamscapeGrabbable GetOccupant(HumanBodyBones hand)
        {
            if (hand == HumanBodyBones.LeftHand)
                return _leftHandOccupant;

            if (hand == HumanBodyBones.RightHand)
                return _rightHandOccupant;

            return null;
        }
    }
}
