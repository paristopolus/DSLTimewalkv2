using System.Collections;
//using System.Collections.Generic;
// using Unity.Netcode;
using UnityEngine;
using RvSdk.Component;

public class MarimbaSounds : MonoBehaviour
{
    public GameObject drumstickObject;
    //public AudioClip Sound1; // Assign the sound clip in the Inspector
    private AudioSource audioSource;
    private ClientToServerTrigger clientToServerTrigger;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        clientToServerTrigger = GetComponent<ClientToServerTrigger>();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.transform.name == drumstickObject.name)
        {
            if (clientToServerTrigger != null)
            {
                clientToServerTrigger.Trigger();
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.transform.name == drumstickObject.name)
        {
            if (clientToServerTrigger != null)
            {
                clientToServerTrigger.Trigger();
            }
        }
    }
}
