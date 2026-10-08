using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
// using Unity.Netcode;
using UnityEngine;
using UnityEngine.Playables;
using RvSdk.Component;
// using UnityEngine.XR.Interaction.Toolkit.Interactors;
// using UnityEngine.XR.Interaction.Toolkit;


public class MusicNoteGameController : MonoBehaviour
{
    public AudioSource babyAudioSource;

    [SerializeField]
    private Animator babyAnimator;

    [SerializeField]
    private AudioClip correctClip;

    [SerializeField]
    private NetworkSound finishClipAudio;

    [SerializeField]
    private GameObject musicStaffGame;

    [SerializeField]
    private Transform musicStaffGamePlayPosition;

    [SerializeField]
    private PlayableDirector babyDirector;

    private PlayableDirector playableDirectorFinish;

    public static MusicNoteGameController Instance;

    [HideInInspector]
    public bool musicGameStarted = false;

    [HideInInspector]
    public bool endGameStarted = false;

    [Header("Notes")]
    public List<GameObject> musicNoteSockets;

    [HideInInspector]
    public int endGameTriggerCount = 7;//replaced later with count of socket list

    private int currentCount = 0;

    public List<GameObject> gameFinishObjects = new List<GameObject>();


    [SerializeField]
    private MoveToNewScene moveSceneScript;

    private void Awake()
    {
        Instance = this;
    }

    void OnEnable()
    {
        SubscribeToNotePlacedEvents();
    }

    void OnDisable()
    {
        UnsubscribeFromNotePlacedEvents();
    }

    void Start()
    {
        playableDirectorFinish = GetComponent<PlayableDirector>();

        endGameTriggerCount = musicNoteSockets.Count;
    }

    void SubscribeToNotePlacedEvents()
    {
        foreach (GameObject note in musicNoteSockets)
        {
            if (note == null)
                continue;

            PlaceableObject placeable = note.GetComponent<PlaceableObject>();
            if (placeable != null)
                placeable.onPlaced.AddListener(OnNotePlaced);
        }
    }

    void UnsubscribeFromNotePlacedEvents()
    {
        foreach (GameObject note in musicNoteSockets)
        {
            if (note == null)
                continue;

            PlaceableObject placeable = note.GetComponent<PlaceableObject>();
            if (placeable != null)
                placeable.onPlaced.RemoveListener(OnNotePlaced);
        }
    }

    void OnNotePlaced()
    {
        RegisterCorrectNoteServerRpc(true);
    }

    bool AreAllNotesPlaced()
    {
        if (musicNoteSockets == null || musicNoteSockets.Count == 0)
            return false;

        foreach (GameObject note in musicNoteSockets)
        {
            if (note == null)
                return false;

            PlaceableObject placeable = note.GetComponent<PlaceableObject>();
            if (placeable == null || !placeable.IsPlaced)
                return false;
        }

        return true;
    }


    // [ServerRpc(RequireOwnership = false)]
    private void EndGameServerRpc()
    {
        EndGameClientRpc();
    }

    // [ClientRpc]
    private void EndGameClientRpc()
    {
        StartCoroutine(WaitForEndScene());
    }
    IEnumerator WaitForEndScene()
    {
        babyAudioSource.Stop();
        if (finishClipAudio != null)
            finishClipAudio.PlayOnce(0);
        else
            Debug.LogWarning("Assign FinishClipAudio to the music note game controller.", this);
        foreach (var item in gameFinishObjects)
        {
            item.gameObject.SetActive(true);
        }

        yield return new WaitForSeconds(5); // Allow the finish celebration before starting the timeline.


        DestroyMusicNotesServerRpc(); //Remove musical notes and staff
        playableDirectorFinish.Play();
        //MoveToNewScene is triggered by a marker on the GamePlay Manager Timeline
    }

    // [ServerRpc(RequireOwnership = false)]
    public void StartMusicNoteGameServerRpc() //called by marker on Baby timeline
    {
        musicGameStarted = true;
        musicStaffGame.transform.position = musicStaffGamePlayPosition.position;
    }

    private void PlayAudio(AudioClip clip) //Audio player function
    {
        babyAudioSource.clip = clip;
        babyAudioSource.Play();
    }

    // [ServerRpc(RequireOwnership = false)]
    private void DestroyMusicNotesServerRpc() // called with a  signal emitter on Timeline
    {
        DestroyMusicNotesClientRpc();
    }

    // [ClientRpc]
    private void DestroyMusicNotesClientRpc()
    {
        var notesArray = GameObject.FindGameObjectsWithTag("MusicNotes");
        for (int i = 0; i < notesArray.Length; i++)
        {
            Destroy(notesArray[i]);
        }
        musicStaffGame.SetActive(false);
    }

    // [ServerRpc(RequireOwnership =false)]
    public void PlayAnimationServerRpc(string trigger)
    {
        // Tell all clients to play the animation
        PlayAnimationClientRpc();
    }

    // [ServerRpc(RequireOwnership = false)]
    public void RegisterCorrectNoteServerRpc(bool notePlaced)
    {
        if (notePlaced)
        {
            currentCount++;
            PlayAnimationClientRpc();
        }
        else
        {
            currentCount--;
        }
    }

    // [ClientRpc]
    private void PlayAnimationClientRpc()
    {
        if (playableDirectorFinish != null && AreAllNotesPlaced())
        {
            EndGameServerRpc();
        }
        else
        {
            babyAnimator.SetTrigger("Correct Trigger");
            PlayAudio(correctClip);
        }
    }
}


