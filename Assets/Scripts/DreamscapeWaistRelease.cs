using RvSdk.Avatar;
using RvSdk.Component;
using RvSdk.Controller;
using RvSdk.Module;
using UnityEngine;

[RequireComponent(typeof(DreamscapeGrabbable))]
public class DreamscapeWaistRelease : MonoBehaviour
{
    static readonly string[] WaistBoneNames =
    {
        "Pelvis",
        "Hips",
        "Hip",
        "CC_Base_Pelvis",
        "CC_Base_Hip"
    };

    [Header("Waist release")]
    [SerializeField] DreamscapeGrabbable grabbable;
    [SerializeField] bool enableWaistRelease = true;
    [Tooltip("Tried first on humanoid avatars. Ignored when that bone is unmapped.")]
    [SerializeField] HumanBodyBones waistBone = HumanBodyBones.Hips;
    [Tooltip("Added to waist bone height. Positive raises the drop line.")]
    [SerializeField] float waistHeightOffset;
    [Tooltip("Object must stay below the waist this long before it drops.")]
    [SerializeField] float belowWaistGraceSeconds = 0.25f;
    [Tooltip("After a drop, waist release stays off this long so the player can pick the object back up.")]
    [SerializeField] float pickupGraceSeconds = 2f;

    float _belowWaistTimer;
    float _pickupGraceTimer;
    bool _armedThisGrab;
    Transform _waistTransform;

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
        if (_pickupGraceTimer > 0f)
            _pickupGraceTimer -= Time.deltaTime;

        if (grabbable == null || !enableWaistRelease || !grabbable.IsGrabbed)
        {
            _belowWaistTimer = 0f;
            return;
        }

        if (!TryGetWaistHeight(out float waistHeight))
        {
            _belowWaistTimer = 0f;
            return;
        }

        bool belowWaist = transform.position.y < waistHeight + waistHeightOffset;

        if (!belowWaist)
        {
            _armedThisGrab = true;
            _belowWaistTimer = 0f;
            return;
        }

        if (!_armedThisGrab || _pickupGraceTimer > 0f)
        {
            _belowWaistTimer = 0f;
            return;
        }

        _belowWaistTimer += Time.deltaTime;
        if (_belowWaistTimer < belowWaistGraceSeconds)
            return;

        _belowWaistTimer = 0f;
        _pickupGraceTimer = pickupGraceSeconds;
        grabbable.ForceReleaseAllowingRegrab();
    }

    void HandleGrabbed()
    {
        _belowWaistTimer = 0f;
        _armedThisGrab = false;
    }

    void HandleReleased()
    {
        _belowWaistTimer = 0f;
        _armedThisGrab = false;
    }

    bool TryGetWaistHeight(out float waistHeight)
    {
        Transform waist = GetWaistTransform();
        if (waist == null)
        {
            waistHeight = 0f;
            return false;
        }

        waistHeight = waist.position.y;
        return true;
    }

    Transform GetWaistTransform()
    {
        if (_waistTransform != null)
            return _waistTransform;

        RuntimePlayer localPlayer = GameController.Instance?.CurrentPlayer;
        AvatarController avatar = localPlayer?.AvatarController;
        Animator animator = avatar?.AvatarAnimator;
        if (animator == null)
            return null;

        if (animator.isHuman)
            _waistTransform = animator.GetBoneTransform(waistBone);

        if (_waistTransform == null)
        {
            Transform searchRoot = avatar != null ? avatar.transform : animator.transform;
            _waistTransform = FindWaistBone(searchRoot);
        }

        return _waistTransform;
    }

    static Transform FindWaistBone(Transform root)
    {
        for (int i = 0; i < WaistBoneNames.Length; i++)
        {
            Transform match = FindChildByName(root, WaistBoneNames[i], exact: true);
            if (match != null)
                return match;
        }

        return FindChildByName(root, "Pelvis", exact: false);
    }

    static Transform FindChildByName(Transform root, string boneName, bool exact)
    {
        if (root.name.Equals(boneName, System.StringComparison.OrdinalIgnoreCase)
            || (!exact && root.name.EndsWith(boneName, System.StringComparison.OrdinalIgnoreCase)))
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform match = FindChildByName(root.GetChild(i), boneName, exact);
            if (match != null)
                return match;
        }

        return null;
    }

#if UNITY_EDITOR
    public void AutoAssignComponents()
    {
        grabbable = GetComponent<DreamscapeGrabbable>();
    }
#endif
}
