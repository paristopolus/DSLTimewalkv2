using System.Collections.Generic;
using RvSdk.Avatar;
using RvSdk.Controller;
using RvSdk.Module.HandMapper;
using UnityEngine;
using UnityEngine.XR.Hands;

[DefaultExecutionOrder(21120)]
[DisallowMultipleComponent]
[RequireComponent(typeof(AvatarController), typeof(AvatarGrabPoseRestore))]
public sealed class AvatarGrabPoseOverride : MonoBehaviour
{
    [SerializeField] HandPoseSO leftGripPose;
    [SerializeField] HandPoseSO rightGripPose;
    [SerializeField] AvatarHand leftHand;
    [SerializeField] AvatarHand rightHand;
    [Min(0f)][SerializeField] float blendInSeconds = 0.12f;
    [Min(0f)][SerializeField] float blendOutSeconds = 0.15f;

    sealed class HandState
    {
        public Object owner;
        public float weight;
        public bool warned;
    }

    struct AppliedBone
    {
        public Transform bone;
        public Quaternion original;
        public Quaternion applied;
    }

    readonly HandState _left = new HandState();
    readonly HandState _right = new HandState();
    readonly List<AppliedBone> _applied = new List<AppliedBone>();
    readonly Dictionary<Transform, Transform> _filteredBones = new Dictionary<Transform, Transform>();
    AvatarController _avatar;

    public static AvatarGrabPoseOverride FindForLocalPlayer()
    {
        var avatar = GameController.Instance?.CurrentPlayer?.AvatarController;
        return avatar != null ? avatar.GetComponent<AvatarGrabPoseOverride>() : null;
    }

    void Awake()
    {
        _avatar = GetComponent<AvatarController>();
    }

    // remember which object owns the grip for this hand
    public void BeginGrip(HumanBodyBones hand, Object owner)
    {
        if (!isActiveAndEnabled || owner == null) return;
        var state = StateFor(hand);
        if (state != null)
        {
            state.owner = owner;
            state.warned = false;
        }
    }

    // only the object holding this grip can release it
    public void EndGrip(HumanBodyBones hand, Object owner)
    {
        var state = StateFor(hand);
        if (state != null && state.owner == owner) state.owner = null;
    }

    HandState StateFor(HumanBodyBones hand)
    {
        if (hand == HumanBodyBones.LeftHand) return _left;
        if (hand == HumanBodyBones.RightHand) return _right;
        return null;
    }

    // restore the underlying pose before tracking so the grip does not feed back into filtering
    public void RestoreUnderlyingPose()
    {
        foreach (var entry in _applied)
        {
            // keep newer bone changes made by another system
            if (entry.bone != null &&
                Quaternion.Angle(entry.bone.localRotation, entry.applied) < 0.1f)
                entry.bone.localRotation = entry.original;
        }
        _applied.Clear();
    }

    void LateUpdate()
    {
        // remote avatars receive grip requests from synchronized session state
        if (_avatar == null)
        {
            Clear();
            return;
        }

        ResolveHands();
        ApplyHand(_left, leftHand, leftGripPose, Handedness.Left);
        ApplyHand(_right, rightHand, rightGripPose, Handedness.Right);
    }

    void ResolveHands()
    {
        if (leftHand != null && rightHand != null) return;
        // find existing hand components once the avatar is ready
        foreach (var hand in GetComponentsInChildren<AvatarHand>(true))
        {
            if (leftHand == null && hand.handedness == Handedness.Left) leftHand = hand;
            if (rightHand == null && hand.handedness == Handedness.Right) rightHand = hand;
        }
    }

    void ApplyHand(HandState state, AvatarHand hand, HandPoseSO pose, Handedness side)
    {
        string problem = null;
        if (hand == null) problem = "No AvatarHand was found. Assign the existing hand component.";
        else if (hand.handedness != side) problem = "The assigned AvatarHand has the wrong handedness.";
        else if (hand.avatarHandDefinition == null) problem = "The AvatarHand has no hand definition.";
        else if (pose == null || pose.data == null) problem = "No grip pose asset is assigned.";
        else if (pose.data.handedness != side) problem = "The grip pose has the wrong handedness.";
        else if (pose.data.fingersRotations == null) problem = "The grip pose has no joint rotations.";
        bool valid = problem == null;
        if (!valid && state.owner != null && !state.warned)
        {
            Debug.LogWarning($"[GrabPose] {name}, {side}: {problem}", this);
            state.warned = true;
        }
        // fade into the saved pose while holding and back out after release
        float target = valid && state.owner != null ? 1f : 0f;
        float seconds = target > state.weight ? blendInSeconds : blendOutSeconds;
        state.weight = seconds <= 0f ? target :
            Mathf.MoveTowards(state.weight, target, Time.deltaTime / seconds);
        if (!valid || state.weight <= 0f) return;

        int appliedFingers = 0;
        foreach (var pair in hand.boneMap)
        {
            // change only fingers so tracking keeps control of the wrist and arm
            if (pair.Key == XRHandJointID.Wrist || pair.Key == XRHandJointID.Palm) continue;
            int index = pair.Key.ToIndex();
            if (pose.data.fingersRotations == null ||
                index < 0 || index >= pose.data.fingersRotations.Length) continue;
            var rotation = pose.data.fingersRotations[index];
            if (Quaternion.Dot(rotation, rotation) < 0.000001f) continue;
            if (pair.Value == null) continue;
            ApplyBone(pair.Value, rotation, state.weight);
            appliedFingers++;
            ApplyBone(FindFilteredBone(pair.Value), rotation, state.weight);
        }
        if (appliedFingers == 0 && state.owner != null && !state.warned)
        {
            Debug.LogWarning($"[GrabPose] {name}, {side}: No usable finger bones match the hand definition and pose.", this);
            state.warned = true;
        }
    }

    Transform FindFilteredBone(Transform bone)
    {
        if (bone == null) return null;
        if (_filteredBones.TryGetValue(bone, out var filtered)) return filtered;
        // also find the filtered finger bones used for skeleton streaming
        foreach (var candidate in GetComponentsInChildren<Transform>(true))
        {
            if (candidate.name != bone.name + "_filtered") continue;
            _filteredBones[bone] = candidate;
            return candidate;
        }
        _filteredBones[bone] = null;
        return null;
    }

    void ApplyBone(Transform bone, Quaternion target, float weight)
    {
        if (bone == null) return;
        var original = bone.localRotation;
        var applied = Quaternion.Slerp(original, target, weight);
        // save the underlying rotation for restoration on the next frame
        _applied.Add(new AppliedBone { bone = bone, original = original, applied = applied });
        bone.localRotation = applied;
    }

    void Clear()
    {
        RestoreUnderlyingPose();
        _left.owner = _right.owner = null;
        _left.weight = _right.weight = 0f;
        _filteredBones.Clear();
    }

    void OnDisable() => Clear();
}
