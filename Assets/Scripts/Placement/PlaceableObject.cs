using RvSdk.Avatar;
using RvSdk.Component;
using RvSdk.Controller;
using RvSdk.Module;
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(DreamscapeGrabbable))]
public class PlaceableObject : MonoBehaviour
{
    // ClientToServerTrigger argument prefix for snap requests
    public const string PlaceArgumentPrefix = "Place|";

    const float PlaceRequestTimeoutSeconds = 0.75f;

    [Header("Identity")]
    // must match PlacementZone.acceptedPlaceableId for the target slot
    [SerializeField] string placeableId;

    [Header("Detection")]
    // coarse search — zones farther than this are ignored entirely
    [SerializeField] float zoneSearchRadius = 1f;

    [Header("References")]
    [SerializeField] DreamscapeGrabbable grabbable;
    [SerializeField] ClientToServerTrigger serverTrigger;

    [Header("Events")]
    public UnityEvent onPlaced;

    bool _placeRequestPending;
    float _placeRequestTime;
    bool _stateSyncSubscribed;
    int _appliedPlacedValue = PlaceableObjectNetworkState.Free;
    PlacementZone _appliedZone;

    public string PlaceableId => placeableId;
    // reads placed state from the shared DreamscapeGrabbable.stateSync integer
    public bool IsPlaced => grabbable != null && grabbable.IsPlaced;

    void Awake()
    {
        if (grabbable == null)
            grabbable = GetComponent<DreamscapeGrabbable>();

        if (serverTrigger == null)
            serverTrigger = GetComponent<ClientToServerTrigger>();
    }

    void OnEnable()
    {
        TrySubscribeStateSync();

        // shares ClientToServerTrigger with DreamscapeGrabbable — different arguments
        if (serverTrigger != null)
            serverTrigger.OnTriggerWithArg.AddListener(HandleServerTriggerWithArg);

        if (IsPlaced)
            ApplyPlacedState();
    }

    void Start()
    {
        TrySubscribeStateSync();

        if (IsPlaced)
            ApplyPlacedState();
    }

    void OnDisable()
    {
        UnsubscribeStateSync();

        if (serverTrigger != null)
            serverTrigger.OnTriggerWithArg.RemoveListener(HandleServerTriggerWithArg);
    }

    void TrySubscribeStateSync()
    {
        if (_stateSyncSubscribed || grabbable?.StateSync == null)
            return;

        grabbable.StateSync.OnValueChanged.AddListener(OnStateSyncChanged);
        _stateSyncSubscribed = true;
    }

    void UnsubscribeStateSync()
    {
        if (!_stateSyncSubscribed || grabbable?.StateSync == null)
            return;

        grabbable.StateSync.OnValueChanged.RemoveListener(OnStateSyncChanged);
        _stateSyncSubscribed = false;
    }

    void Update()
    {
        if (_placeRequestPending && Time.time - _placeRequestTime >= PlaceRequestTimeoutSeconds)
            _placeRequestPending = false;

        if (IsPlaced)
        {
            _placeRequestPending = false;
            ApplyPlacedStateIfNeeded();
            return;
        }

        if (!NetworkGate.IsInitialized || !NetworkGate.IsClient)
            return;

        // only the local holder tries to snap — no release required
        if (grabbable == null || !grabbable.IsHeldByLocalPlayer)
            return;

        TrySnapWhileHeld();
    }

    void LateUpdate()
    {
        if (!IsPlaced || _appliedZone == null)
            return;

        transform.SetPositionAndRotation(_appliedZone.SnapPosition, _appliedZone.SnapRotation);
    }

    void TrySnapWhileHeld()
    {
        if (_placeRequestPending || serverTrigger == null)
            return;

        PlacementZone zone = FindBestZone();
        if (zone == null)
            return;

        _placeRequestPending = true;
        _placeRequestTime = Time.time;
        // same ClientToServerTrigger as grab — different argument string
        serverTrigger.Trigger(PlaceArgumentPrefix + zone.ZoneId);
    }

    PlacementZone FindBestZone()
    {
        PlacementZone best = null;
        float bestDistSq = float.MaxValue;
        Vector3 position = transform.position;
        Quaternion rotation = transform.rotation;
        float searchRadiusSq = zoneSearchRadius * zoneSearchRadius;

        for (int i = 0; i < PlacementZone.ActiveZones.Count; i++)
        {
            PlacementZone zone = PlacementZone.ActiveZones[i];
            if (!zone.AcceptsPlaceable(this))
                continue;

            float distSq = (zone.SnapPosition - position).sqrMagnitude;
            if (distSq > searchRadiusSq)
                continue;

            // zoneSearchRadius is loose — snapDistance on the zone is the tight check
            if (!zone.IsWithinSnapRange(position, rotation))
                continue;

            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = zone;
            }
        }

        return best;
    }

    void HandleServerTriggerWithArg(string triggerName, AvatarController avatar, string argument)
    {
        if (!NetworkController.IsServer || avatar == null)
            return;

        if (!argument.StartsWith(PlaceArgumentPrefix))
            return;

        string targetZoneId = argument.Substring(PlaceArgumentPrefix.Length);
        PlacementZone zone = PlacementZone.FindByZoneId(targetZoneId);
        if (zone == null)
            return;

        TryPlaceOnServer(avatar, zone);
    }

    void TryPlaceOnServer(AvatarController avatar, PlacementZone zone)
    {
        if (IsPlaced || grabbable == null)
            return;

        if (!zone.AcceptsPlaceable(this))
            return;

        if (!grabbable.IsHeldBy(avatar))
            return;

        grabbable.SetPlacedOnServer(zone);
        zone.SetOccupiedOnServer();
    }

    void OnStateSyncChanged(int value)
    {
        if (!PlaceableObjectNetworkState.IsPlaced(value))
            return;

        _placeRequestPending = false;
        // runs on every client when stateSync becomes a placed value
        if (ApplyPlacedStateIfNeeded())
            onPlaced.Invoke();
    }

    bool ApplyPlacedStateIfNeeded()
    {
        if (grabbable == null || grabbable.StateSync == null)
            return false;

        int placedValue = grabbable.StateSync.Value;
        if (!PlaceableObjectNetworkState.IsPlaced(placedValue) || placedValue == _appliedPlacedValue)
            return false;

        return ApplyPlacedState(placedValue);
    }

    void ApplyPlacedState()
    {
        if (grabbable == null || grabbable.StateSync == null)
            return;

        ApplyPlacedState(grabbable.StateSync.Value);
    }

    bool ApplyPlacedState(int placedValue)
    {
        if (!PlaceableObjectNetworkState.IsPlaced(placedValue))
            return false;

        PlacementZone zone = PlacementZone.FindByPlacedStateValue(placedValue);
        if (zone == null)
            return false;

        grabbable.SnapToWorldPose(zone.SnapPosition, zone.SnapRotation);
        _appliedZone = zone;
        _appliedPlacedValue = placedValue;
        return true;
    }

#if UNITY_EDITOR
    public void AutoAssignComponents()
    {
        grabbable = GetComponent<DreamscapeGrabbable>();
        serverTrigger = GetComponent<ClientToServerTrigger>();
    }
#endif
}
