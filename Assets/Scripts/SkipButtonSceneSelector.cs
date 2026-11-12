using RvSdk.Component;
using RvSdk.Controller;
using RvSdk.Module;
using RvSdk.Module.Messages;
using UnityEngine;
public class SkipButtonSceneSelector : MonoBehaviour
{
    [System.Serializable]
    public class Destination
    {
        public string label;
        public string sceneName;
        public AvatarGenericTrigger trigger;
    }

    [SerializeField] private AvatarGenericTrigger toggleTrigger;
    [SerializeField] private GameObject panel;
    [SerializeField] private Destination[] destinations;
    private bool loading;
    private float nextToggleTime;
    private float selectionReadyTime;

    private void Awake()
    {
        panel.SetActive(false);
        toggleTrigger.OnTriggerValidated.AddListener(_ => TogglePanel());
        foreach (var destination in destinations)
        {
            var entry = destination;
            entry.trigger.OnTriggerValidated.AddListener(_ => SelectScene(entry.sceneName));
            AddLabel(panel.transform, entry.label,
                entry.trigger.transform.localPosition + new Vector3(0, 0, -0.3f), 0.25f);
        }
    }

    public void TogglePanel()
    {
        if (loading || Time.unscaledTime < nextToggleTime) return;
        nextToggleTime = Time.unscaledTime + 0.5f;
        panel.SetActive(!panel.activeSelf);
        selectionReadyTime = Time.unscaledTime + 0.5f;
    }

    public void SelectScene(string sceneName)
    {
        if (loading || !panel.activeSelf || Time.unscaledTime < selectionReadyTime) return;
        if (!NetworkGate.IsServer) return;
        if (GameController.Instance == null || !Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError("Scene selector cannot load: " + sceneName, this);
            return;
        }

        loading = true;
        panel.SetActive(false);
        GameController.Instance.LoadGameScene(sceneName, Transition.FadeBlack, forceReload: true);
    }

    private static void AddLabel(Transform parent, string text, Vector3 position, float size)
    {
        var labelObject = new GameObject(text + " Label");
        labelObject.transform.SetParent(parent, false);
        labelObject.transform.localPosition = position;
        var label = labelObject.AddComponent<TextMesh>();
        label.text = text;
        label.anchor = TextAnchor.MiddleCenter;
        label.alignment = TextAlignment.Center;
        label.fontSize = 64;
        label.characterSize = size;
        label.color = Color.white;
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.GetComponent<MeshRenderer>().sharedMaterial = label.font.material;
    }
}
