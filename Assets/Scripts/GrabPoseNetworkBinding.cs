using RvSdk.Avatar;
using RvSdk.Component;
using RvSdk.Controller;
using RvSdk.Module;
using UnityEngine;

// Uses the existing object's stable network ID and server-owned session values.
public sealed class GrabPoseNetworkBinding
{
    const string Left = "GrabPose:Left";
    const string Right = "GrabPose:Right";
    AvatarGrabPoseOverride _target;
    HumanBodyBones _hand = HumanBodyBones.LastBone;

    static string Key(NetworkSyncedInteger state)
    {
        return state != null && !string.IsNullOrEmpty(state.Id)
            ? state.Id + "/GrabPose/v1" : null;
    }

    public static void Report(ClientToServerTrigger trigger, HumanBodyBones hand)
    {
        if (trigger == null) return;
        if (hand == HumanBodyBones.LeftHand) trigger.Trigger(Left);
        else if (hand == HumanBodyBones.RightHand) trigger.Trigger(Right);
    }

    public static bool HandleReport(string argument, AvatarController sender, NetworkSyncedInteger state)
    {
        int hand = argument == Left ? 1 : argument == Right ? 2 : 0;
        if (hand == 0) return false;
        var key = Key(state);
        if (NetworkGate.IsServer && sender != null && key != null &&
            GameSessionController.HasInstance &&
            PlaceableObjectNetworkState.IsHeld(state.Value) &&
            state.Value == sender.PlayerId.GetHashCode())
        {
            GameSessionController.Instance.SetValue(key,
                GrabPoseNetworkState.Encode(state.Value, hand));
        }
        return true;
    }

    public static void ResetOnServer(NetworkSyncedInteger state)
    {
        var key = Key(state);
        if (NetworkGate.IsInitialized && NetworkGate.IsServer &&
            GameSessionController.HasInstance && key != null)
            GameSessionController.Instance.SetValue(key, 0L);
    }

    public void Update(Object owner, NetworkSyncedInteger state)
    {
        AvatarGrabPoseOverride target = null;
        HumanBodyBones hand = HumanBodyBones.LastBone;
        var key = Key(state);
        if (NetworkGate.IsInitialized && GameSessionController.HasInstance &&
            GameController.HasInstance && key != null &&
            PlaceableObjectNetworkState.IsHeld(state.Value))
        {
            int holder = state.Value;
            int side = GrabPoseNetworkState.HandForHolder(
                GameSessionController.Instance.GetValue(key, 0L), holder);
            if (side != 0)
            {
                foreach (var player in GameController.Instance.RuntimePlayers)
                {
                    var avatar = player.AvatarController;
                    if (avatar == null || avatar.PlayerId.GetHashCode() != holder) continue;
                    // Local pose is driven immediately by BeginGrabLocal/EndGrabLocal.
                    if (!avatar.IsMainPlayer)
                    {
                        target = avatar.GetComponent<AvatarGrabPoseOverride>();
                        hand = side == 1 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
                    }
                    break;
                }
            }
        }

        if (_target != null && !_target.isActiveAndEnabled) Clear(owner);
        if (_target == target && _hand == hand) return;
        Clear(owner);
        if (target == null || !target.isActiveAndEnabled) return;
        _target = target;
        _hand = hand;
        _target.BeginGrip(hand, owner);
    }

    public void Clear(Object owner)
    {
        if (_target != null) _target.EndGrip(_hand, owner);
        _target = null;
        _hand = HumanBodyBones.LastBone;
    }
}
