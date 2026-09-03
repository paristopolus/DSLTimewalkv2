using UnityEngine;

// local-only tracker so one grabbed object per hand on this client
public static class LocalHandOccupancy
{
    static MonoBehaviour _leftHandOccupant;
    static MonoBehaviour _rightHandOccupant;

    public static bool IsHandAvailable(HumanBodyBones hand)
    {
        return GetOccupant(hand) == null;
    }

    public static bool TryRegister(HumanBodyBones hand, MonoBehaviour occupant)
    {
        if (occupant == null || !IsHandAvailable(hand))
            return false;

        if (hand == HumanBodyBones.LeftHand)
            _leftHandOccupant = occupant;
        else if (hand == HumanBodyBones.RightHand)
            _rightHandOccupant = occupant;
        else
            return false;

        return true;
    }

    public static void Unregister(HumanBodyBones hand, MonoBehaviour occupant)
    {
        if (hand == HumanBodyBones.LeftHand && _leftHandOccupant == occupant)
            _leftHandOccupant = null;
        else if (hand == HumanBodyBones.RightHand && _rightHandOccupant == occupant)
            _rightHandOccupant = null;
    }

    static MonoBehaviour GetOccupant(HumanBodyBones hand)
    {
        if (hand == HumanBodyBones.LeftHand)
            return _leftHandOccupant;

        if (hand == HumanBodyBones.RightHand)
            return _rightHandOccupant;

        return null;
    }
}
