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
public class DreamscapeGrabbables : MonoBehaviour
{
    public const int HolderNone = -1;

    public const string GrabTrigger = "Grab";
    public const string ReleaseTrigger = "Release";

    public enum HandPreference
    {
        Either,
        LeftOnly,
        RightOnly
    }

    // =========================================================
    // NETWORKING
    // =========================================================

    [Header("Networking")]

    [Tooltip("Single NetworkSyncedInteger for grab + placed state.")]
    [FormerlySerializedAs("holderSync")]
    [SerializeField]
    private NetworkSyncedInteger stateSync;


    // =========================================================
    // GRAB DETECTION
    // =========================================================

    [Header("Grab detection")]

    [SerializeField]
    private HandPreference handPreference = HandPreference.Either;

    [SerializeField]
    private float grabRadius = 0.4f;

    [SerializeField]
    private float grabDwellSeconds = 0.15f;


    // STEP 1
    //
    // ON:
    // Normal hand-distance grabbing works.
    //
    // OFF:
    // Timeline decides exactly when the grab occurs.
    [Tooltip(
        "ON = normal hand proximity grab. " +
        "OFF = Timeline controls the grab."
    )]
    [SerializeField]
    private bool automaticHandProximityGrab = true;


    // =========================================================
    // SHAKE RELEASE
    // =========================================================

    [Header("Shake release")]

    [SerializeField]
    private bool enableShakeRelease = true;

    [SerializeField]
    private float shakeSpeedThreshold = 8f;

    [SerializeField]
    private float shakeSampleWindow = 0.5f;

    [SerializeField]
    private float shakeGraceSeconds = 0.5f;


    // =========================================================
    // ATTACH
    // =========================================================

    [Header("Attach")]

    [SerializeField]
    private Transform leftAttachPoint;

    [SerializeField]
    private Transform rightAttachPoint;

    [SerializeField]
    private Vector3 localAttachOffset = Vector3.zero;

    [SerializeField]
    private Vector3 localAttachEuler;

    [SerializeField]
    private bool matchHandRotation;


    // =========================================================
    // PHYSICS
    // =========================================================

    [Header("Physics")]

    [SerializeField]
    private bool useGravityWhenReleased;


    // =========================================================
    // EVENTS
    // =========================================================

    [Header("Events")]

    public UnityEvent onGrabbed;
    public UnityEvent onReleased;
    public UnityEvent onPlacementLocked;


    // =========================================================
    // PRIVATE VARIABLES
    // =========================================================

    private NetworkSyncedTransform _syncedTransform;
    private NetworkSyncedInteger _stateSync;
    private ClientToServerTrigger _serverTrigger;
    private Rigidbody _rigidbody;

    private float _dwellTimer;

    private bool _isGrabbedLocally;
    private bool _placementLocked;

    private HumanBodyBones _activeHand =
        HumanBodyBones.LastBone;

    private HumanBodyBones _pendingGrabHand =
        HumanBodyBones.LastBone;

    private Vector3 _previousHandPosition;

    private bool _hasPreviousHandPosition;

    private float _shakePeakSpeed;
    private float _shakeSampleTimer;
    private float _shakeGraceTimer;

    private Quaternion _rotationWhileHeld;

    private bool _syncedRotationWhileHeld;

    private Vector3 _gripOffsetInObjectSpace;

    private Quaternion _gripRotationInObjectSpace =
        Quaternion.identity;

    private bool _hasGripAttach;

    private bool _pendingInitialGrabSnap;

    private Vector3 _twistAxisAtGrab;

    private bool _transformRegistered;


    private static readonly HashSet<string>
        RegisteredTransformIds =
        new HashSet<string>();


    // =========================================================
    // PUBLIC STATE
    // =========================================================

    public bool IsGrabbed =>
        _isGrabbedLocally;

    public bool IsPlacementLocked =>
        _placementLocked;

    public NetworkSyncedInteger StateSync =>
        _stateSync;

    public int StateValue =>
        _stateSync != null
            ? _stateSync.Value
            : PlaceableObjectNetworkState.Free;

    public int HolderHash =>
        PlaceableObjectNetworkState.GetHolderHash(
            StateValue
        );

    public bool IsPlaced =>
        PlaceableObjectNetworkState.IsPlaced(
            StateValue
        );

    public bool IsHeldByLocalPlayer =>
        _isGrabbedLocally &&
        IsLocalPlayerHash(HolderHash);

    public bool IsHeldByAnyone =>
        PlaceableObjectNetworkState.IsHeld(
            StateValue
        );


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        _syncedTransform =
            GetComponent<NetworkSyncedTransform>();

        _stateSync =
            stateSync != null
                ? stateSync
                : GetComponent<NetworkSyncedInteger>();

        _serverTrigger =
            GetComponent<ClientToServerTrigger>();

        _rigidbody =
            GetComponent<Rigidbody>();
    }


    private void OnEnable()
    {
        if (_stateSync != null)
        {
            _stateSync.OnValueChanged
                .AddListener(OnStateChanged);
        }

        if (_serverTrigger != null)
        {
            _serverTrigger.OnTriggerWithArg
                .AddListener(
                    HandleServerTriggerWithArg
                );
        }

        if (IsPlaced)
        {
            _placementLocked = true;
        }
    }


    private void OnDisable()
    {
        if (_stateSync != null)
        {
            _stateSync.OnValueChanged
                .RemoveListener(OnStateChanged);
        }

        if (_serverTrigger != null)
        {
            _serverTrigger.OnTriggerWithArg
                .RemoveListener(
                    HandleServerTriggerWithArg
                );
        }

        if (_isGrabbedLocally)
        {
            EndGrabLocal();
        }
    }


    // =========================================================
    // UPDATE
    // =========================================================

    private void Update()
    {
        if (_isGrabbedLocally &&
            _shakeGraceTimer > 0f)
        {
            _shakeGraceTimer -=
                Time.deltaTime;
        }


        if (!CanRunLocalGrabLogic())
        {
            return;
        }


        // -----------------------------------------------------
        // Already holding
        // -----------------------------------------------------

        if (_isGrabbedLocally)
        {
            if (ShouldRelease())
            {
                RequestRelease();
            }

            return;
        }


        // -----------------------------------------------------
        // Someone else is holding the object
        // -----------------------------------------------------

        if (HolderHash != HolderNone)
        {
            _dwellTimer = 0f;

            return;
        }


        // =====================================================
        // STEP 1
        //
        // If Timeline controls grabbing,
        // stop the normal automatic proximity grab here.
        // =====================================================

        if (!automaticHandProximityGrab)
        {
            _dwellTimer = 0f;

            return;
        }


        // -----------------------------------------------------
        // NORMAL HAND PROXIMITY GRAB
        // -----------------------------------------------------

        if (!TryGetClosestAvailableHand(
                out _,
                out float distance))
        {
            _dwellTimer = 0f;

            return;
        }


        if (distance <= grabRadius)
        {
            _dwellTimer +=
                Time.deltaTime;


            if (_dwellTimer >=
                grabDwellSeconds)
            {
                if (TryGetClosestAvailableHand(
                        out HumanBodyBones hand,
                        out _))
                {
                    _pendingGrabHand =
                        hand;

                    RequestGrab();
                }
            }
        }
        else
        {
            _dwellTimer = 0f;
        }
    }


    private void FixedUpdate()
    {
        if (!_isGrabbedLocally ||
            !NetworkGate.IsInitialized ||
            !NetworkGate.IsClient)
        {
            return;
        }

        UpdateHeldPose();
    }


    // =========================================================
    // CAN GRAB?
    // =========================================================

    private bool CanRunLocalGrabLogic()
    {
        if (_placementLocked ||
            IsPlaced)
        {
            return false;
        }


        if (!NetworkGate.IsInitialized ||
            !NetworkGate.IsClient)
        {
            return false;
        }


        if (GameController.Instance == null ||
            GameController.Instance.CurrentPlayer == null)
        {
            return false;
        }


        if (HolderHash != HolderNone &&
            !IsLocalPlayerHash(
                HolderHash
            ))
        {
            return false;
        }


        return true;
    }


    // =========================================================
    // STEP 2
    // TIMELINE ANIMATED GRAB
    // =========================================================

    public bool RequestAnimatedGrab(
        HumanBodyBones hand)
    {
        if (!CanRunLocalGrabLogic())
        {
            Debug.LogWarning(
                "Timeline grab failed: " +
                "local grab logic cannot run.",
                this
            );

            return false;
        }


        if (!PlaceableObjectNetworkState
                .IsFree(StateValue))
        {
            Debug.LogWarning(
                "Timeline grab failed: " +
                "object is not free.",
                this
            );

            return false;
        }


        if (!IsHandAllowed(hand))
        {
            Debug.LogWarning(
                "Timeline grab failed: "
                + hand
                + " is not allowed.",
                this
            );

            return false;
        }


        if (!LocalHandOccupancy
                .IsHandAvailable(hand))
        {
            Debug.LogWarning(
                "Timeline grab failed: "
                + hand
                + " already has an object.",
                this
            );

            return false;
        }


        // Remember which hand Timeline chose.
        _pendingGrabHand =
            hand;


        Debug.Log(
            "Timeline requested grab with "
            + hand,
            this
        );


        // Use the normal RVSDK network grab.
        RequestGrab();


        return true;
    }


    // =========================================================
    // TIMELINE SIGNAL - RIGHT HAND
    // =========================================================

    public void TimelineGrabRightHand()
    {
        Debug.Log(
            "Timeline Signal -> RIGHT HAND GRAB",
            this
        );


        RequestAnimatedGrab(
            HumanBodyBones.RightHand
        );
    }


    // =========================================================
    // TIMELINE SIGNAL - LEFT HAND
    // =========================================================

    public void TimelineGrabLeftHand()
    {
        Debug.Log(
            "Timeline Signal -> LEFT HAND GRAB",
            this
        );


        RequestAnimatedGrab(
            HumanBodyBones.LeftHand
        );
    }


    // =========================================================
    // NETWORK REQUEST
    // =========================================================

    private void RequestGrab()
    {
        if (!PlaceableObjectNetworkState
                .IsFree(StateValue))
        {
            return;
        }


        _dwellTimer =
            0f;


        _serverTrigger.Trigger(
            GrabTrigger
        );
    }


    private void RequestRelease()
    {
        _serverTrigger.Trigger(
            ReleaseTrigger
        );
    }


    // =========================================================
    // SERVER
    // =========================================================

    private void HandleServerTriggerWithArg(
        string triggerName,
        AvatarController avatar,
        string argument)
    {
        if (!NetworkController.IsServer ||
            avatar == null)
        {
            return;
        }


        if (_placementLocked ||
            IsPlaced)
        {
            return;
        }


        if (argument ==
            GrabTrigger)
        {
            TryAssignHolder(
                avatar
            );
        }
        else if (argument ==
                 ReleaseTrigger)
        {
            TryClearHolder(
                avatar
            );
        }
    }


    private void TryAssignHolder(
        AvatarController avatar)
    {
        if (!PlaceableObjectNetworkState
                .IsFree(
                    _stateSync.Value
                ))
        {
            return;
        }


        _stateSync.Value =
            avatar.PlayerId
                .GetHashCode();
    }


    private void TryClearHolder(
        AvatarController avatar)
    {
        if (!PlaceableObjectNetworkState
                .IsHeld(
                    _stateSync.Value
                ))
        {
            return;
        }


        if (_stateSync.Value !=
            avatar.PlayerId
                .GetHashCode())
        {
            return;
        }


        _stateSync.Value =
            HolderNone;
    }


    // =========================================================
    // NETWORK STATE CHANGED
    // =========================================================

    private void OnStateChanged(
        int newValue)
    {
        if (PlaceableObjectNetworkState
                .IsPlaced(newValue))
        {
            if (_isGrabbedLocally)
            {
                EndGrabLocal();
            }


            _placementLocked =
                true;


            return;
        }


        if (_placementLocked)
        {
            return;
        }


        bool isHeld =
            PlaceableObjectNetworkState
                .IsHeld(newValue);


        int holderHash =
            PlaceableObjectNetworkState
                .GetHolderHash(
                    newValue
                );


        bool shouldHoldLocally =
            IsLocalPlayerHash(
                holderHash
            );


        if (isHeld)
        {
            ApplyHeldPhysicsState();
        }
        else
        {
            ApplyReleasedPhysicsState();
        }


        if (shouldHoldLocally &&
            !_isGrabbedLocally)
        {
            BeginGrabLocal();
        }
        else if (!shouldHoldLocally &&
                 _isGrabbedLocally)
        {
            EndGrabLocal();
        }
    }


    // =========================================================
    // PHYSICS
    // =========================================================

    private void ApplyHeldPhysicsState()
    {
        if (_rigidbody == null)
        {
            return;
        }


        ClearRigidbodyVelocities();


        _rigidbody.isKinematic =
            true;

        _rigidbody.useGravity =
            false;
    }


    private void ApplyReleasedPhysicsState()
    {
        if (_rigidbody == null ||
            _placementLocked)
        {
            return;
        }


        if (useGravityWhenReleased)
        {
            _rigidbody.isKinematic =
                false;

            _rigidbody.useGravity =
                true;


            ClearRigidbodyVelocities();


            return;
        }


        _rigidbody.isKinematic =
            true;

        _rigidbody.useGravity =
            false;
    }


    private void ClearRigidbodyVelocities()
    {
        if (_rigidbody == null ||
            _rigidbody.isKinematic)
        {
            return;
        }


        _rigidbody.velocity =
            Vector3.zero;

        _rigidbody.angularVelocity =
            Vector3.zero;
    }


    // =========================================================
    // START LOCAL GRAB
    // =========================================================

    private void BeginGrabLocal()
    {
        HumanBodyBones hand =
            _pendingGrabHand;


        _pendingGrabHand =
            HumanBodyBones.LastBone;


        if (!IsHandAllowed(hand) ||
            !LocalHandOccupancy
                .IsHandAvailable(hand))
        {
            if (!TryGetClosestAvailableHand(
                    out hand,
                    out _))
            {
                RequestRelease();

                return;
            }
        }


        if (!LocalHandOccupancy
                .TryRegister(
                    hand,
                    this
                ))
        {
            RequestRelease();

            return;
        }


        _isGrabbedLocally =
            true;


        _activeHand =
            hand;


        _rotationWhileHeld =
            transform.rotation;


        _twistAxisAtGrab =
            transform.forward;


        _pendingInitialGrabSnap =
            matchHandRotation;


        CacheGripAttach(
            GetGripPointTransform(
                _activeHand
            )
        );


        EnsureTransformRegistered();


        _syncedTransform.IsSource =
            true;


        _syncedRotationWhileHeld =
            _syncedTransform
                .SyncRotation;


        _syncedTransform.SyncRotation =
            matchHandRotation;


        ResetShakeTracking();


        _shakeGraceTimer =
            shakeGraceSeconds;


        onGrabbed.Invoke();


        Debug.Log(
            "RVSDK grab approved. Active hand = "
            + _activeHand,
            this
        );


        UpdateHeldPose();
    }


    // =========================================================
    // END LOCAL GRAB
    // =========================================================

    private void EndGrabLocal()
    {
        if (_activeHand !=
            HumanBodyBones.LastBone)
        {
            LocalHandOccupancy
                .Unregister(
                    _activeHand,
                    this
                );
        }


        _isGrabbedLocally =
            false;


        _activeHand =
            HumanBodyBones.LastBone;


        _pendingGrabHand =
            HumanBodyBones.LastBone;


        _hasGripAttach =
            false;


        _pendingInitialGrabSnap =
            false;


        _syncedTransform.IsSource =
            false;


        _syncedTransform.SyncRotation =
            _syncedRotationWhileHeld;


        ResetShakeTracking();


        onReleased.Invoke();
    }


    // =========================================================
    // FOLLOW HAND
    // =========================================================

    private void UpdateHeldPose()
    {
        Transform hand =
            GetHandTransform(
                _activeHand
            );


        if (hand == null)
        {
            return;
        }


        Quaternion handAttachRotation =
            hand.rotation *
            Quaternion.Euler(
                localAttachEuler
            );


        Quaternion targetRotation;


        if (matchHandRotation)
        {
            Quaternion handAlignedRotation =
                handAttachRotation *
                Quaternion.Inverse(
                    _gripRotationInObjectSpace
                );


            Quaternion swing =
                GetSwing(
                    handAlignedRotation,
                    _twistAxisAtGrab
                );


            Quaternion twist =
                _pendingInitialGrabSnap
                    ? GetTwist(
                        _rotationWhileHeld,
                        _twistAxisAtGrab
                    )
                    : GetTwist(
                        handAlignedRotation,
                        _twistAxisAtGrab
                    );


            targetRotation =
                swing * twist;


            if (_pendingInitialGrabSnap)
            {
                _pendingInitialGrabSnap =
                    false;
            }
        }
        else
        {
            targetRotation =
                _rotationWhileHeld;
        }


        Vector3 targetGripPosition =
            hand.position +
            hand.rotation *
            localAttachOffset;


        Vector3 targetPosition =
            _hasGripAttach
                ? targetGripPosition
                  -
                  targetRotation *
                  _gripOffsetInObjectSpace
                : targetGripPosition;


        ApplyHeldPose(
            targetPosition,
            targetRotation
        );
    }


    // =========================================================
    // ROTATION HELPERS
    // =========================================================

    private static Quaternion GetTwist(
        Quaternion rotation,
        Vector3 twistAxis)
    {
        if (twistAxis.sqrMagnitude <
            1e-8f)
        {
            return Quaternion.identity;
        }


        twistAxis.Normalize();


        Vector3 vectorPart =
            new Vector3(
                rotation.x,
                rotation.y,
                rotation.z
            );


        Vector3 twistPart =
            Vector3.Project(
                vectorPart,
                twistAxis
            );


        if (twistPart.sqrMagnitude <
            1e-8f)
        {
            return Quaternion.identity;
        }


        return Quaternion.Normalize(
            new Quaternion(
                twistPart.x,
                twistPart.y,
                twistPart.z,
                rotation.w
            )
        );
    }


    private static Quaternion GetSwing(
        Quaternion rotation,
        Vector3 twistAxis)
    {
        return rotation *
               Quaternion.Inverse(
                   GetTwist(
                       rotation,
                       twistAxis
                   )
               );
    }


    // =========================================================
    // GRIP POINT
    // =========================================================

    private void CacheGripAttach(
        Transform gripPoint)
    {
        if (gripPoint == null ||
            gripPoint == transform)
        {
            _hasGripAttach =
                false;


            _gripRotationInObjectSpace =
                Quaternion.identity;


            return;
        }


        _gripOffsetInObjectSpace =
            Quaternion.Inverse(
                transform.rotation
            )
            *
            (
                gripPoint.position -
                transform.position
            );


        _gripRotationInObjectSpace =
            Quaternion.Inverse(
                transform.rotation
            )
            *
            gripPoint.rotation;


        _hasGripAttach =
            true;
    }


    private Transform GetGripPointTransform(
        HumanBodyBones hand)
    {
        Transform point =
            hand ==
            HumanBodyBones.LeftHand
                ? leftAttachPoint
                : rightAttachPoint;


        if (point == null ||
            point == transform)
        {
            return null;
        }


        return point;
    }


    private void ApplyHeldPose(
        Vector3 targetPosition,
        Quaternion targetRotation)
    {
        if (_rigidbody != null &&
            _rigidbody.isKinematic)
        {
            _rigidbody.MovePosition(
                targetPosition
            );


            _rigidbody.MoveRotation(
                targetRotation
            );


            return;
        }


        transform.SetPositionAndRotation(
            targetPosition,
            targetRotation
        );
    }


    // =========================================================
    // NETWORK TRANSFORM REGISTER
    // =========================================================

    private void EnsureTransformRegistered()
    {
        if (_transformRegistered ||
            _syncedTransform == null)
        {
            return;
        }


        if (!NetworkGate.IsInitialized ||
            !NetworkGate.IsClient)
        {
            return;
        }


        string transformId =
            _syncedTransform.Id;


        if (string.IsNullOrEmpty(
                transformId))
        {
            return;
        }


        if (RegisteredTransformIds
                .Contains(transformId))
        {
            _transformRegistered =
                true;


            return;
        }


        _syncedTransform.Register(
            true
        );


        RegisteredTransformIds.Add(
            transformId
        );


        _transformRegistered =
            true;
    }


    // =========================================================
    // RELEASE
    // =========================================================

    private bool ShouldRelease()
    {
        if (!TryGetHandTransform(
                _activeHand,
                out _))
        {
            return true;
        }


        return ShouldReleaseFromShake();
    }


    private void ResetShakeTracking()
    {
        _hasPreviousHandPosition =
            false;


        _shakePeakSpeed =
            0f;


        _shakeSampleTimer =
            0f;
    }


    private bool ShouldReleaseFromShake()
    {
        if (!enableShakeRelease ||
            _shakeGraceTimer > 0f)
        {
            return false;
        }


        if (!TryGetHandTransform(
                _activeHand,
                out Transform hand))
        {
            return false;
        }


        if (_hasPreviousHandPosition &&
            Time.deltaTime > 0f)
        {
            float speed =
                (
                    hand.position -
                    _previousHandPosition
                ).magnitude
                /
                Time.deltaTime;


            if (speed >
                _shakePeakSpeed)
            {
                _shakePeakSpeed =
                    speed;
            }


            _shakeSampleTimer +=
                Time.deltaTime;


            if (_shakeSampleTimer >=
                shakeSampleWindow)
            {
                bool release =
                    _shakePeakSpeed >=
                    shakeSpeedThreshold;


                _shakePeakSpeed =
                    0f;


                _shakeSampleTimer =
                    0f;


                _previousHandPosition =
                    hand.position;


                return release;
            }
        }


        _previousHandPosition =
            hand.position;


        _hasPreviousHandPosition =
            true;


        return false;
    }


    // =========================================================
    // HAND DISTANCE
    // =========================================================

    private float GetHandGrabDistance(
        HumanBodyBones handBone)
    {
        if (!TryGetHandTransform(
                handBone,
                out Transform hand))
        {
            return float.MaxValue;
        }


        Transform attachPoint =
            handBone ==
            HumanBodyBones.LeftHand
                ? leftAttachPoint
                : rightAttachPoint;


        Vector3 grabTarget =
            attachPoint != null
                ? attachPoint.position
                : transform.position;


        return Vector3.Distance(
            hand.position,
            grabTarget
        );
    }


    private bool TryGetClosestAvailableHand(
        out HumanBodyBones hand,
        out float distance)
    {
        hand =
            HumanBodyBones.LastBone;


        distance =
            float.MaxValue;


        if (handPreference ==
                HandPreference.LeftOnly ||
            handPreference ==
                HandPreference.Either)
        {
            if (LocalHandOccupancy
                    .IsHandAvailable(
                        HumanBodyBones.LeftHand
                    ))
            {
                float leftDistance =
                    GetHandGrabDistance(
                        HumanBodyBones.LeftHand
                    );


                if (leftDistance <
                    distance)
                {
                    distance =
                        leftDistance;


                    hand =
                        HumanBodyBones.LeftHand;
                }
            }
        }


        if (handPreference ==
                HandPreference.RightOnly ||
            handPreference ==
                HandPreference.Either)
        {
            if (LocalHandOccupancy
                    .IsHandAvailable(
                        HumanBodyBones.RightHand
                    ))
            {
                float rightDistance =
                    GetHandGrabDistance(
                        HumanBodyBones.RightHand
                    );


                if (rightDistance <
                    distance)
                {
                    distance =
                        rightDistance;


                    hand =
                        HumanBodyBones.RightHand;
                }
            }
        }


        return hand !=
               HumanBodyBones.LastBone;
    }


    private bool IsHandAllowed(
        HumanBodyBones hand)
    {
        if (hand ==
            HumanBodyBones.LeftHand)
        {
            return
                handPreference ==
                    HandPreference.Either
                ||
                handPreference ==
                    HandPreference.LeftOnly;
        }


        if (hand ==
            HumanBodyBones.RightHand)
        {
            return
                handPreference ==
                    HandPreference.Either
                ||
                handPreference ==
                    HandPreference.RightOnly;
        }


        return false;
    }


    // =========================================================
    // RUNTIME DSL HAND
    // =========================================================

    private bool TryGetHandTransform(
        HumanBodyBones handBone,
        out Transform hand)
    {
        hand =
            GetHandTransform(
                handBone
            );


        return hand != null;
    }


    private Transform GetHandTransform(
        HumanBodyBones handBone)
    {
        RuntimePlayer localPlayer =
            GameController.Instance
                ?.CurrentPlayer;


        Animator animator =
            localPlayer
                ?.AvatarController
                ?.AvatarAnimator;


        if (animator == null)
        {
            return null;
        }


        return animator.GetBoneTransform(
            handBone
        );
    }


    // =========================================================
    // LOCAL PLAYER
    // =========================================================

    private bool IsLocalPlayerHash(
        int holderHash)
    {
        if (holderHash ==
            HolderNone)
        {
            return false;
        }


        Guid? localPlayerId =
            GameController.Instance
                ?.CurrentPlayer
                ?.AvatarController
                ?.PlayerId;


        return
            localPlayerId.HasValue
            &&
            localPlayerId.Value
                .GetHashCode()
            ==
            holderHash;
    }


    // =========================================================
    // PUBLIC RELEASE / PLACEMENT
    // =========================================================

    public void ForceRelease()
    {
        if (IsPlaced)
        {
            return;
        }


        if (_isGrabbedLocally)
        {
            RequestRelease();
        }
        else if (
            NetworkController.IsServer
            &&
            PlaceableObjectNetworkState
                .IsHeld(
                    _stateSync.Value
                ))
        {
            _stateSync.Value =
                HolderNone;
        }
    }


    public bool IsHeldBy(
        AvatarController avatar)
    {
        if (avatar == null)
        {
            return false;
        }


        return
            PlaceableObjectNetworkState
                .IsHeld(StateValue)
            &&
            StateValue ==
            avatar.PlayerId
                .GetHashCode();
    }


    public void SetPlacedOnServer(
        PlacementZone zone)
    {
        if (!NetworkController.IsServer ||
            zone == null ||
            IsPlaced)
        {
            return;
        }


        _stateSync.Value =
            zone.PlacedStateValue;
    }


    public void SnapToWorldPose(
        Vector3 position,
        Quaternion rotation,
        bool lockPlacement = true)
    {
        ForceRelease();


        transform.SetPositionAndRotation(
            position,
            rotation
        );


        if (_rigidbody != null)
        {
            _rigidbody.isKinematic =
                true;


            _rigidbody.useGravity =
                false;
        }


        if (lockPlacement)
        {
            LockPlacement();
        }
    }


    public void LockPlacement()
    {
        _placementLocked =
            true;


        ForceRelease();


        onPlacementLocked.Invoke();
    }


    public void UnlockPlacement()
    {
        _placementLocked =
            false;
    }


#if UNITY_EDITOR

    public void AutoAssignComponents()
    {
        stateSync =
            GetComponent<
                NetworkSyncedInteger
            >();


        Transform leftChild =
            FindAttachChild(
                "LeftAttachPoint"
            );


        if (leftChild != null &&
            leftChild != transform)
        {
            leftAttachPoint =
                leftChild;
        }


        Transform rightChild =
            FindAttachChild(
                "RightAttachPoint"
            );


        if (rightChild != null &&
            rightChild != transform)
        {
            rightAttachPoint =
                rightChild;
        }
    }


    private Transform FindAttachChild(
        string name)
    {
        Transform child =
            transform.Find(name);


        if (child != null)
        {
            return child;
        }


        for (int i = 0;
             i < transform.childCount;
             i++)
        {
            Transform candidate =
                transform.GetChild(i);


            if (candidate.name.Equals(
                    name,
                    StringComparison
                        .OrdinalIgnoreCase))
            {
                return candidate;
            }
        }


        return null;
    }

#endif


    // =========================================================
    // LOCAL HAND OCCUPANCY
    // =========================================================

    private static class LocalHandOccupancy
    {
        private static DreamscapeGrabbables
            _leftHandOccupant;

        private static DreamscapeGrabbables
            _rightHandOccupant;


        public static bool IsHandAvailable(
            HumanBodyBones hand)
        {
            return GetOccupant(hand)
                   == null;
        }


        public static bool TryRegister(
            HumanBodyBones hand,
            DreamscapeGrabbables grabbable)
        {
            if (grabbable == null ||
                !IsHandAvailable(hand))
            {
                return false;
            }


            if (hand ==
                HumanBodyBones.LeftHand)
            {
                _leftHandOccupant =
                    grabbable;
            }
            else if (hand ==
                     HumanBodyBones.RightHand)
            {
                _rightHandOccupant =
                    grabbable;
            }
            else
            {
                return false;
            }


            return true;
        }


        public static void Unregister(
            HumanBodyBones hand,
            DreamscapeGrabbables grabbable)
        {
            if (
                hand ==
                    HumanBodyBones.LeftHand
                &&
                _leftHandOccupant ==
                    grabbable)
            {
                _leftHandOccupant =
                    null;
            }
            else if (
                hand ==
                    HumanBodyBones.RightHand
                &&
                _rightHandOccupant ==
                    grabbable)
            {
                _rightHandOccupant =
                    null;
            }
        }


        private static DreamscapeGrabbables
            GetOccupant(
                HumanBodyBones hand)
        {
            if (hand ==
                HumanBodyBones.LeftHand)
            {
                return _leftHandOccupant;
            }


            if (hand ==
                HumanBodyBones.RightHand)
            {
                return _rightHandOccupant;
            }


            return null;
        }
    }
}