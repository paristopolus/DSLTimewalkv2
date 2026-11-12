using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Renderer))]
public sealed class TeleportFootprintPulse : MonoBehaviour
{
    [SerializeField] private Color blueColor = new Color(0.0341506f, 0f, 1f, 1f);
    [SerializeField] private Color whiteColor = Color.white;
    [SerializeField, Min(0.01f)] private float pulseDuration = 2f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private Renderer footprintRenderer;
    private MaterialPropertyBlock originalProperties;
    private MaterialPropertyBlock pulseProperties;
    private float elapsed;

    private void Awake()
    {
        footprintRenderer = GetComponent<Renderer>();
        originalProperties = new MaterialPropertyBlock();
        pulseProperties = new MaterialPropertyBlock();
    }

    private void OnEnable()
    {
        footprintRenderer.GetPropertyBlock(originalProperties);
        elapsed = 0f;
        ApplyColor(blueColor);
    }

    private void Update()
    {
        float duration = Mathf.Max(0.01f, pulseDuration);
        elapsed = Mathf.Repeat(elapsed + Time.unscaledDeltaTime, duration);
        float blend = 0.5f - 0.5f * Mathf.Cos(elapsed / duration * Mathf.PI * 2f);
        ApplyColor(Color.Lerp(blueColor, whiteColor, blend));
    }

    private void ApplyColor(Color color)
    {
        footprintRenderer.GetPropertyBlock(pulseProperties);
        pulseProperties.SetColor(BaseColorId, color);
        pulseProperties.SetColor(ColorId, color);
        pulseProperties.SetColor(EmissionColorId, color);
        footprintRenderer.SetPropertyBlock(pulseProperties);
    }

    private void OnDisable()
    {
        if (footprintRenderer != null)
            footprintRenderer.SetPropertyBlock(originalProperties);
    }
}
