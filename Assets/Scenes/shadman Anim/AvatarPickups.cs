using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

[RequireComponent(typeof(PlayableDirector))]
public class AvatarPickups : MonoBehaviour
{
    [Header("Pickup Animation")]
    [SerializeField] private TimelineAsset pickupTimelineAsset;

    [Header("Avatar Hand")]
    [SerializeField] private Transform handHoldPoint;

    [Header("Pickup Timing")]
    [SerializeField] private float attachTimeSeconds = 1.0f;

    private PlayableDirector director;
    private Animator animator;

    private Transform objectToPickUp;
    private Transform originalParent;

    private bool isHoldingObject;
    private bool waitingToAttach;

    private void Awake()
    {
        director = GetComponent<PlayableDirector>();
        animator = GetComponent<Animator>();

        if (director == null)
        {
            Debug.LogError("PlayableDirector is missing.");
            return;
        }

        if (animator == null)
        {
            Debug.LogError("Animator is missing.");
            return;
        }

        if (pickupTimelineAsset == null)
        {
            Debug.LogError("Pickup Timeline Asset is not assigned.");
            return;
        }

        director.playableAsset = pickupTimelineAsset;
        director.playOnAwake = false;

        BindTimelineToAvatar();
    }

    private void BindTimelineToAvatar()
    {
        foreach (TrackAsset track in pickupTimelineAsset.GetOutputTracks())
        {
            if (track is AnimationTrack)
            {
                director.SetGenericBinding(track, animator);

                Debug.Log(
                    "Bound Timeline Animation Track to: "
                    + animator.gameObject.name
                );

                break;
            }
        }
    }

    private void Update()
    {
        if (!waitingToAttach)
            return;

        if (director.time >= attachTimeSeconds)
        {
            AttachObjectToHand();
            waitingToAttach = false;
        }
    }

    // The grabbed object is passed into this method.
    public void StartPickup(Transform pickedObject)
    {
        if (pickedObject == null)
        {
            Debug.LogWarning("No object was passed to StartPickup.");
            return;
        }

        objectToPickUp = pickedObject;
        originalParent = objectToPickUp.parent;

        isHoldingObject = false;
        waitingToAttach = true;

        director.time = 0;
        director.Play();
    }

    private void AttachObjectToHand()
    {
        if (objectToPickUp == null)
        {
            Debug.LogWarning("There is no object to pick up.");
            return;
        }

        if (handHoldPoint == null)
        {
            Debug.LogWarning("Hand Hold Point is missing.");
            return;
        }

        objectToPickUp.SetParent(handHoldPoint);

        objectToPickUp.localPosition = Vector3.zero;
        objectToPickUp.localRotation = Quaternion.identity;

        isHoldingObject = true;

        Debug.Log("Object attached to hand.");
    }

    public void ReleaseObject()
    {
        if (!isHoldingObject || objectToPickUp == null)
            return;

        objectToPickUp.SetParent(originalParent, true);

        isHoldingObject = false;
        objectToPickUp = null;
    }
}