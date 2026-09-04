using RvSdk.Controller;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

[RequireComponent(typeof(PlayableDirector))]
[RequireComponent(typeof(DreamscapeGrabbables))]
public class DreamscapeAnimatedAutoGrab : MonoBehaviour
{
    [Header("Timeline")]
    [Tooltip("Drag your AnimationDemo Timeline Asset here.")]
    [SerializeField]
    private TimelineAsset pickupTimeline;

    [Header("Pickup Settings")]
    [Tooltip("How close the DSL avatar must be before the Timeline starts.")]
    [SerializeField]
    private float triggerDistance = 1.2f;

    [Tooltip("Timeline time when the animated hand reaches the object.")]
    [SerializeField]
    private float grabAtSeconds = 1.0f;

    [Tooltip("Use Right Hand for your current animation.")]
    [SerializeField]
    private HumanBodyBones pickupHand = HumanBodyBones.RightHand;

    [Header("Debug")]
    [SerializeField]
    private bool showDebugLogs = true;


    private PlayableDirector director;
    private DreamscapeGrabbables grabbable;

    private Animator runtimeAvatarAnimator;

    private bool animationStarted;
    private bool grabRequested;


    private void Awake()
    {
        director = GetComponent<PlayableDirector>();
        grabbable = GetComponent<DreamscapeGrabbables>();

        director.playOnAwake = false;

        if (pickupTimeline != null)
        {
            director.playableAsset = pickupTimeline;
        }
    }


    private void Update()
    {
        // -----------------------------------------
        // 1. Make sure Dreamscape player exists
        // -----------------------------------------

        if (!TryGetRuntimeAvatar(out Animator animator))
        {
            return;
        }


        // -----------------------------------------
        // 2. Don't run if object is already held
        // -----------------------------------------

        if (grabbable.IsHeldByAnyone)
        {
            return;
        }

        if (grabbable.IsPlacementLocked ||
            grabbable.IsPlaced)
        {
            return;
        }


        // -----------------------------------------
        // 3. Wait for DSL avatar to come close
        // -----------------------------------------

        if (!animationStarted)
        {
            float distance =
                GetDistanceFromAvatar(animator);

            if (distance <= triggerDistance)
            {
                StartPickupTimeline(animator);
            }

            return;
        }


        // -----------------------------------------
        // 4. Wait for Timeline hand-contact time
        // -----------------------------------------

        if (!grabRequested &&
            director.time >= grabAtSeconds)
        {
            RequestTimelineGrab();
        }
    }


    // =====================================================
    // GET THE REAL RUNTIME DSL AVATAR
    // =====================================================

    private bool TryGetRuntimeAvatar(
        out Animator animator)
    {
        animator = null;

        RuntimePlayer player =
            GameController.Instance?.CurrentPlayer;

        if (player == null)
        {
            return false;
        }

        if (player.AvatarController == null)
        {
            return false;
        }

        animator =
            player.AvatarController.AvatarAnimator;

        return animator != null;
    }


    // =====================================================
    // DISTANCE
    // =====================================================

    private float GetDistanceFromAvatar(
        Animator animator)
    {
        // Use the avatar's hips as the body position.
        Transform hips =
            animator.GetBoneTransform(
                HumanBodyBones.Hips
            );

        if (hips != null)
        {
            return Vector3.Distance(
                hips.position,
                transform.position
            );
        }

        // Fallback.
        return Vector3.Distance(
            animator.transform.position,
            transform.position
        );
    }


    // =====================================================
    // START TIMELINE
    // =====================================================

    private void StartPickupTimeline(
        Animator animator)
    {
        if (pickupTimeline == null)
        {
            Debug.LogWarning(
                "Pickup Timeline is missing.",
                this
            );

            return;
        }

        runtimeAvatarAnimator = animator;

        director.playableAsset =
            pickupTimeline;


        // Bind the Timeline's animation track
        // to the ACTUAL runtime DSL avatar.
        BindTimelineToRuntimeAvatar();


        director.time = 0;

        director.Play();


        animationStarted = true;
        grabRequested = false;


        if (showDebugLogs)
        {
            Debug.Log(
                "DSL avatar is close. " +
                "Starting pickup Timeline.",
                this
            );
        }
    }


    // =====================================================
    // BIND TIMELINE TO DSL AVATAR
    // =====================================================

    private void BindTimelineToRuntimeAvatar()
    {
        foreach (
            TrackAsset track
            in pickupTimeline.GetOutputTracks())
        {
            if (track is AnimationTrack)
            {
                director.SetGenericBinding(
                    track,
                    runtimeAvatarAnimator
                );

                if (showDebugLogs)
                {
                    Debug.Log(
                        "Bound Timeline Animation Track to DSL avatar: "
                        + runtimeAvatarAnimator.name,
                        this
                    );
                }
            }
        }

        director.RebindPlayableGraphOutputs();
    }


    // =====================================================
    // GRAB AT THE CORRECT TIMELINE TIME
    // =====================================================

    private void RequestTimelineGrab()
    {
        grabRequested = true;

        bool success;

        if (pickupHand ==
            HumanBodyBones.RightHand)
        {
            success =
                grabbable.RequestAnimatedGrab(
                    HumanBodyBones.RightHand
                );
        }
        else
        {
            success =
                grabbable.RequestAnimatedGrab(
                    HumanBodyBones.LeftHand
                );
        }


        if (showDebugLogs)
        {
            Debug.Log(
                success
                    ? "Timeline requested the object grab."
                    : "Timeline grab request FAILED.",
                this
            );
        }
    }


    // =====================================================
    // RESET
    // =====================================================

    public void ResetPickup()
    {
        director.Stop();

        animationStarted = false;
        grabRequested = false;

        runtimeAvatarAnimator = null;
    }


#if UNITY_EDITOR

    private void OnDrawGizmosSelected()
    {
        Gizmos.DrawWireSphere(
            transform.position,
            triggerDistance
        );
    }

#endif
}