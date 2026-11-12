using System.Collections;
using System.Collections.Generic;
using RvSdk.Controller;
using RvSdk.Module.Messages;
using UnityEngine;
// using Unity.Netcode;
using UnityEngine.SceneManagement;
public class MoveToNewScene : MonoBehaviour
{
    [SerializeField]
    private string nextSceneName = "Tutorial";


    [SerializeField]
    private string previousSceneName = null;
    private string charSelectScene = "Scene0_charSelect";

    [Header("Current Scene GameObjects to Destroy on Scene Change")]
    public List<GameObject> gameObjectsToDestroy = new List<GameObject>();

    //This section happens at Start of scene - unloading last scene
    // void Start()
    // {
    //     if (IsServer)
    //     {
    //         RequestDestroyGameManagerServerRpc(); //destroy previous scene's gamemanager
    //     }
    // }

    // Readjusts the lighting in each scene after last scene is unloaded
    void AdjustLightProbes()
    {
        // Debug.Log("Entered AdjustLightprobes");
        LightProbes.Tetrahedralize();
    }

    void OnDisable()
    {
        LightProbes.needsRetetrahedralization -= AdjustLightProbes;
    }

    // [ServerRpc(RequireOwnership = false)]
    private void RequestDestroyGameManagerServerRpc()
    {
        DestroyPreviousGameManagerClientRpc();
    }

    // [ClientRpc]
    private void DestroyPreviousGameManagerClientRpc()
    {
        Scene previousScene = SceneManager.GetSceneByName(previousSceneName);
        if (previousScene.isLoaded)
        {
            GameObject[] rootObjects = previousScene.GetRootGameObjects();

            foreach (GameObject obj in rootObjects)
            {
                if (obj.name == "GameManager")
                {
                    Destroy(obj);
                }
            }
            UnloadPreviousScenes(); // unloads previous scene from memory
        }
        UnloadFirstScene();
        LightProbes.needsRetetrahedralization += AdjustLightProbes;
    }

    private void UnloadPreviousScenes()
    {
        if (previousSceneName != null)
        {
            // SceneManager.UnloadSceneAsync(previousSceneName);
            StartCoroutine(UnloadPreviousSceneCoroutine(previousSceneName));
        }
    }

    public void UnloadFirstScene()
    {
        // unload the charSelect scene if it's loaded
        if (SceneManager.GetSceneByName(charSelectScene).isLoaded)
        {
            Debug.Log("charSelectLoaded");
            StartCoroutine(UnloadPreviousSceneCoroutine(charSelectScene));
        }
    }

    // ADDED BY JIANNA FOR MEMORY TEST
    private IEnumerator UnloadPreviousSceneCoroutine(string SceneName)
    {
        yield return SceneManager.UnloadSceneAsync(SceneName);
        yield return Resources.UnloadUnusedAssets();
        System.GC.Collect();
    }
    // ADDED BY JIANNA FOR MEMORY TEST END
    // End section of code that runs at Start of scene
    //
    //
    public void ChangeScene() //function is triggered when change to new scene is called
    {
        // if (IsServer)
        //     HandleSceneChange();
        // else
        //     RequestSceneChangeServerRpc();
        //Debug.Log("Made it into ChangeScene");
        // HandleSceneChange();

        GameController.Instance.LoadGameScene(nextSceneName, Transition.FadeBlack);
    }

    private void HandleSceneChange()
    {
        DespawnListedObjects();

        // NetworkManager.Singleton.SceneManager.LoadScene(
        //     nextSceneName,
        //     LoadSceneMode.Additive
        // );
    }

    private void DespawnListedObjects()   //only the host despawns the spawned networked GOs
    {
        foreach (GameObject go in gameObjectsToDestroy)
        {
            if (go == null) continue;

            // // NetworkObject netObj = go.GetComponent<NetworkObject>();

            // if (netObj != null && netObj.IsSpawned)
            // {
            //     netObj.Despawn(true);
            //     // Debug.Log($"[SERVER] Despawned network object: {go.name}");
            // }
            // else
            // {
            Destroy(go);
            // Debug.Log($"[SERVER] Destroyed local-only object: {go.name}");
            // }
        }
        // DestroySceneObjectsClientRpc();
    }


    // [ServerRpc(RequireOwnership = false)]
    // private void RequestSceneChangeServerRpc(ServerRpcParams rpcParams = default)
    // {
    //     HandleSceneChange();
    //     //DestroyGOsClientRpc();
    //     //NetworkManager.Singleton.SceneManager.LoadScene(nextSceneName, LoadSceneMode.Additive);
    // }



    // [ClientRpc]
    private void DestroySceneObjectsClientRpc()
    {
        foreach (GameObject go in gameObjectsToDestroy)
        {
            if (go != null)
            {
                Destroy(go);
                //Debug.Log($"[CLIENT] Destroyed scene object: {go.name}");
            }
        }

    }
}
