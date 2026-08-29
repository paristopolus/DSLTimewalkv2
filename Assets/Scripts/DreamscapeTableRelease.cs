using UnityEngine;

[RequireComponent(typeof(DreamscapeGrabbable))]
public class DreamscapeTableRelease : MonoBehaviour
{
    [Header("Table release")]
    [SerializeField] DreamscapeGrabbable grabbable;
    [SerializeField] bool enableTableRelease = true;
    [Tooltip("Empty or table transform at this instrument's rest pose.")]
    [SerializeField] Transform tableRestPose;
    [Tooltip("How close the instrument must be to the rest pose to count as on the table.")]
    [SerializeField] float releaseDistance = 0.35f;
    [Tooltip("Instrument must move this far from the rest pose before a table drop can fire.")]
    [SerializeField] float leaveDistance = 0.5f;
    [Tooltip("Must stay in range this long before releasing.")]
    [SerializeField] float releaseDwellSeconds = 0.2f;
    [Tooltip("Blocks table-release briefly after grab so pickup from the table does not drop immediately.")]
    [SerializeField] float grabGraceSeconds = 0.4f;
    [SerializeField] bool snapToTableOnRelease = true;

    float _grabGraceTimer;
    float _dwellTimer;
    bool _armedThisGrab;
    bool _pendingTableSnap;

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
        {
            grabbable.onGrabbed.AddListener(HandleGrabbed);
            grabbable.onReleased.AddListener(HandleReleased);
        }
    }

    void OnDisable()
    {
        if (grabbable == null)
            return;

        grabbable.onGrabbed.RemoveListener(HandleGrabbed);
        grabbable.onReleased.RemoveListener(HandleReleased);
    }

    void Update()
    {
        if (grabbable == null || !enableTableRelease || tableRestPose == null || !grabbable.IsGrabbed)
        {
            _dwellTimer = 0f;
            return;
        }

        if (_grabGraceTimer > 0f)
            _grabGraceTimer -= Time.deltaTime;

        float distance = Vector3.Distance(transform.position, tableRestPose.position);

        if (distance > leaveDistance)
        {
            _armedThisGrab = true;
            _dwellTimer = 0f;
            return;
        }

        if (!_armedThisGrab || _grabGraceTimer > 0f || distance > releaseDistance)
        {
            _dwellTimer = 0f;
            return;
        }

        _dwellTimer += Time.deltaTime;
        if (_dwellTimer < releaseDwellSeconds)
            return;

        _dwellTimer = 0f;
        _pendingTableSnap = snapToTableOnRelease;
        grabbable.ForceRelease();
    }

    void HandleGrabbed()
    {
        _grabGraceTimer = grabGraceSeconds;
        _dwellTimer = 0f;
        _armedThisGrab = false;
        _pendingTableSnap = false;
    }

    void HandleReleased()
    {
        _dwellTimer = 0f;
        _armedThisGrab = false;

        if (!_pendingTableSnap)
            return;

        _pendingTableSnap = false;
        SnapToTable();
    }

    void SnapToTable()
    {
        if (tableRestPose == null)
            return;

        transform.SetPositionAndRotation(tableRestPose.position, tableRestPose.rotation);

        Rigidbody body = GetComponent<Rigidbody>();
        if (body == null)
            return;

        if (!body.isKinematic)
        {
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        body.isKinematic = true;
        body.useGravity = false;
    }

#if UNITY_EDITOR
    public void AutoAssignComponents()
    {
        grabbable = GetComponent<DreamscapeGrabbable>();

        if (tableRestPose == null)
            tableRestPose = FindTableRestPose();
    }

    Transform FindTableRestPose()
    {
        Transform child = FindAttachChild("TableRestPose");
        if (child != null)
            return child;

        child = FindAttachChild("RestPose");
        if (child != null)
            return child;

        string ownerName = gameObject.name.Replace("(Clone)", string.Empty).Trim();
        string expectedTableName = ownerName + "_table";

        Transform[] transforms = FindObjectsOfType<Transform>(true);
        Transform namedMatch = null;
        Transform closestTable = null;
        float closestDistance = float.MaxValue;

        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            if (candidate == null || candidate == transform)
                continue;

            string candidateName = candidate.name;
            if (candidateName.Equals(expectedTableName, System.StringComparison.OrdinalIgnoreCase))
                return candidate;

            if (namedMatch == null
                && candidateName.IndexOf(ownerName, System.StringComparison.OrdinalIgnoreCase) >= 0
                && candidateName.IndexOf("table", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                namedMatch = candidate;
            }

            if (candidateName.IndexOf("table", System.StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            float distance = Vector3.Distance(transform.position, candidate.position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestTable = candidate;
            }
        }

        if (namedMatch != null)
            return namedMatch;

        return closestTable;
    }

    Transform FindAttachChild(string name)
    {
        Transform child = transform.Find(name);
        if (child != null)
            return child;

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform candidate = transform.GetChild(i);
            if (candidate.name.Equals(name, System.StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return null;
    }
#endif
}
