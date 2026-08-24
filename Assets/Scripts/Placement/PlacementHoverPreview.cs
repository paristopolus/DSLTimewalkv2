using UnityEngine;

[RequireComponent(typeof(PlaceableObject))]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class PlacementHoverPreview : MonoBehaviour
{
    [Header("Hover")]
    [SerializeField] float hoverRadius = 0.75f;
    [SerializeField] Material hoverMaterial;

    [Header("References")]
    [SerializeField] PlaceableObject placeable;
    [SerializeField] DreamscapeGrabbable grabbable;

    GameObject _ghost;
    MeshFilter _sourceFilter;

    void Awake()
    {
        if (placeable == null)
            placeable = GetComponent<PlaceableObject>();

        if (grabbable == null)
            grabbable = GetComponent<DreamscapeGrabbable>();

        _sourceFilter = GetComponent<MeshFilter>();
    }

    void OnDisable()
    {
        HideGhost();
    }

    void OnDestroy()
    {
        if (_ghost != null)
            Destroy(_ghost);
    }

    void Update()
    {
        if (placeable == null || placeable.IsPlaced || grabbable == null || !grabbable.IsHeldByLocalPlayer)
        {
            HideGhost();
            return;
        }

        PlacementZone zone = FindHoverZone();
        if (zone == null)
        {
            HideGhost();
            return;
        }

        ShowGhostAt(zone);
    }

    PlacementZone FindHoverZone()
    {
        PlacementZone best = null;
        float bestDistSq = float.MaxValue;
        Vector3 position = transform.position;
        float hoverRadiusSq = hoverRadius * hoverRadius;

        for (int i = 0; i < PlacementZone.ActiveZones.Count; i++)
        {
            PlacementZone zone = PlacementZone.ActiveZones[i];
            if (!zone.AcceptsPlaceable(placeable))
                continue;

            float distSq = (zone.SnapPosition - position).sqrMagnitude;
            if (distSq > hoverRadiusSq)
                continue;

            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = zone;
            }
        }

        return best;
    }

    void ShowGhostAt(PlacementZone zone)
    {
        EnsureGhost();

        Transform ghostTransform = _ghost.transform;
        ghostTransform.SetPositionAndRotation(zone.SnapPosition, zone.SnapRotation);
        ghostTransform.localScale = transform.lossyScale;

        if (!_ghost.activeSelf)
            _ghost.SetActive(true);
    }

    void HideGhost()
    {
        if (_ghost != null && _ghost.activeSelf)
            _ghost.SetActive(false);
    }

    void EnsureGhost()
    {
        if (_ghost != null)
            return;

        _ghost = new GameObject(name + " HoverGhost");
        _ghost.hideFlags = HideFlags.HideAndDontSave;

        MeshFilter ghostFilter = _ghost.AddComponent<MeshFilter>();
        ghostFilter.sharedMesh = _sourceFilter.sharedMesh;

        MeshRenderer ghostRenderer = _ghost.AddComponent<MeshRenderer>();
        ghostRenderer.sharedMaterial = hoverMaterial;
        ghostRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ghostRenderer.receiveShadows = false;
    }

#if UNITY_EDITOR
    public void AutoAssignComponents()
    {
        placeable = GetComponent<PlaceableObject>();
        grabbable = GetComponent<DreamscapeGrabbable>();
    }
#endif
}
