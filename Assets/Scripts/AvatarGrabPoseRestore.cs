using UnityEngine;

// restore the previous finger pose before normal tracking updates
[DefaultExecutionOrder(-10000)]
[DisallowMultipleComponent]
public sealed class AvatarGrabPoseRestore : MonoBehaviour
{
    AvatarGrabPoseOverride _poseOverride;

    void Awake() => _poseOverride = GetComponent<AvatarGrabPoseOverride>();

    void Update()
    {
        if (_poseOverride != null) _poseOverride.RestoreUnderlyingPose();
    }
}
