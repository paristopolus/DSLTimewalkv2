using System.Collections;
using System.Collections.Generic;
using UnityEngine;
// using Unity.Netcode;

public class GrandfatherClockController : MonoBehaviour
{
    public static GrandfatherClockController Instance;

    [Header("Clock Settings")]
    public List<string> requiredGearIDs; // e.g. ["gear1", "gear2", "gear3"]
    public Transform clockBigHands;
    public Transform clockSmallHands;
    public AudioSource chimeAudio;

    private HashSet<string> placedGears = new HashSet<string>();
    private bool clockRunning = false;


    //public int timeToWaitToChangeScene = 5;


    [SerializeField]
    private FadeLightsCallAcrossScenes fadeOutScript;

    [SerializeField]
    private List<GameObject> GearsList;

    [SerializeField]
    private List<Transform> GearTransforms;


    public AudioClip AudioEncouragementClip;

    private void Awake()
    {
        Instance = this;
    }

    public void RegisterGearPlaced(string gearID)
    {

        if (!placedGears.Contains(gearID))
        {
            placedGears.Add(gearID);
            CheckCompletion();

            if (placedGears.Count == 2)
            {
                PlayEncouragementAudioClientRpc();
            }
        }
    }

    public void CheckCompletion()
    {
        StartClockClientRpc();
    }

    // [ClientRpc]
    private void StartClockClientRpc()
    {
        StartCoroutine(RunClockSequence());
    }

    // [ClientRpc]
    private void PlayEncouragementAudioClientRpc()
    {
        AudioClip clip = AudioEncouragementClip;
        chimeAudio.PlayOneShot(clip);
    }

    private System.Collections.IEnumerator RunClockSequence()
    {
        float spinTime = 20f;
        float elapsed = 0f;
        Vector3 rotationSpeed = new Vector3(0, 0, -180f);

        if (chimeAudio != null)
            chimeAudio.Play();

        while (elapsed < spinTime)
        {
            clockBigHands.Rotate(-rotationSpeed * 2f * Time.deltaTime);
            clockSmallHands.Rotate(-rotationSpeed * Time.deltaTime);
            elapsed += Time.deltaTime;
            yield return null;

        }
    }


    public void MoveGearsToPosition()
    {
        for (int i = 0; i < GearsList.Count; i++)
        {
            GearsList[i].transform.localPosition = GearTransforms[i].localPosition;
        }
    }
}
