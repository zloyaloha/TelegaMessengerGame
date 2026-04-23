using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class CargoGridVisualizer : MonoBehaviour
{
    private enum CellState
    {
        Default,
        Hover,
        ValidPlacement,
        InvalidPlacement,
        Occupied
    }

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    [Header("References")]
    [SerializeField] private CartInventory cartInventory;
    [SerializeField] private Transform visualsRoot;

    [Header("Cells")]
    [SerializeField, Min(0f)] private float cellPadding = 0.04f;
    [SerializeField] private string gridCellLayerName = "CargoGridCell";
    [SerializeField] private Material defaultMaterial;
    [SerializeField] private Material hoverMaterial;
    [SerializeField] private Material validPlacementMaterial;
    [SerializeField] private Material invalidPlacementMaterial;
    [SerializeField] private Material occupiedMaterial;

    [Header("Ghost")]
    [SerializeField] private Material ghostMaterial;
    [SerializeField] private Color ghostValidColor = new Color(0.25f, 1f, 0.45f, 0.45f);
    [SerializeField] private Color ghostInvalidColor = new Color(1f, 0.3f, 0.3f, 0.45f);

    private readonly Dictionary<Vector3Int, CargoGridCellView> _cellViews = new Dictionary<Vector3Int, CargoGridCellView>();
    private readonly Dictionary<CellState, Material> _fallbackMaterials = new Dictionary<CellState, Material>();
    private MaterialPropertyBlock _ghostPropertyBlock;

    private Vector3Int? _hoveredCell;
    private bool _hasPlacementPreview;
    private Vector3Int _previewOrigin;
    private Vector3Int _previewSize = Vector3Int.one;
    private bool _previewValid;

    private CargoInstance _ghostSourceCargo;
    private GameObject _ghostObject;
    private Renderer[] _ghostRenderers = System.Array.Empty<Renderer>();

    private void Awake()
    {
        if (cartInventory == null)
        {
            cartInventory = GetComponentInParent<CartInventory>();
        }

        _ghostPropertyBlock = new MaterialPropertyBlock();
        EnsureVisualsRoot();
    }

    private void OnEnable()
    {
        if (cartInventory != null)
        {
            cartInventory.InventoryChanged += HandleInventoryChanged;
        }
    }

    private void OnDisable()
    {
        if (cartInventory != null)
        {
            cartInventory.InventoryChanged -= HandleInventoryChanged;
        }
    }

    public void Show()
    {
        EnsureVisualsRoot();
        EnsureGrid();

        if (visualsRoot != null)
        {
            visualsRoot.gameObject.SetActive(true);
        }

        RefreshCellVisuals();
    }

    public void Hide()
    {
        HideGhost();
        _hoveredCell = null;
        _hasPlacementPreview = false;

        if (visualsRoot != null)
        {
            visualsRoot.gameObject.SetActive(false);
        }
    }

    public void SetHoveredCell(Vector3Int? hoveredCell)
    {
        _hoveredCell = hoveredCell;
        RefreshCellVisuals();
    }

    public void SetPlacementPreview(Vector3Int origin, Vector3Int size, bool valid)
    {
        _hasPlacementPreview = true;
        _previewOrigin = origin;
        _previewSize = size;
        _previewValid = valid;
        RefreshCellVisuals();
    }

    public void ClearPlacementPreview()
    {
        _hasPlacementPreview = false;
        RefreshCellVisuals();
    }

    public void UpdateGhost(CargoInstance cargo, Vector3Int origin, bool valid)
    {
        if (cargo == null || cartInventory == null)
        {
            HideGhost();
            return;
        }

        EnsureGhost(cargo);
        if (_ghostObject == null)
        {
            return;
        }

        _ghostObject.SetActive(true);
        _ghostObject.transform.SetParent(cartInventory.CargoParent, false);
        _ghostObject.transform.localRotation = Quaternion.identity;

        Vector3 blockMinLocal = cartInventory.GetLocalMin(origin);
        if (cargo.TryGetGridPlacement(cartInventory.CargoParent, blockMinLocal, out Vector3 localPosition, out Vector3 localScale))
        {
            _ghostObject.transform.localPosition = localPosition;
            _ghostObject.transform.localScale = localScale;
        }
        else
        {
            _ghostObject.transform.localPosition = cartInventory.GetLocalCenter(origin, cargo.GridSize);
        }

        ApplyGhostTint(valid ? ghostValidColor : ghostInvalidColor);
    }

    public void HideGhost()
    {
        _ghostSourceCargo = null;

        if (_ghostObject != null)
        {
            Destroy(_ghostObject);
            _ghostObject = null;
        }

        _ghostRenderers = System.Array.Empty<Renderer>();
    }

    private void HandleInventoryChanged()
    {
        RefreshCellVisuals();
    }

    private void EnsureVisualsRoot()
    {
        if (visualsRoot != null)
        {
            return;
        }

        if (transform != null && transform.parent != null && GetComponent<CartInventory>() == null)
        {
            visualsRoot = transform;
            return;
        }

        GameObject visualsRootObject = new GameObject("CargoGridVisuals");
        visualsRootObject.transform.SetParent(transform, false);
        visualsRoot = visualsRootObject.transform;
    }

    private void EnsureGrid()
    {
        if (cartInventory == null || visualsRoot == null)
        {
            return;
        }

        int expectedCount = cartInventory.GridDimensions.x * cartInventory.GridDimensions.y * cartInventory.GridDimensions.z;
        if (_cellViews.Count != expectedCount)
        {
            ClearCellViews();

            Vector3Int dimensions = cartInventory.GridDimensions;
            int configuredLayer = LayerMask.NameToLayer(gridCellLayerName);

            for (int x = 0; x < dimensions.x; x++)
            {
                for (int y = 0; y < dimensions.y; y++)
                {
                    for (int z = 0; z < dimensions.z; z++)
                    {
                        Vector3Int coords = new Vector3Int(x, y, z);
                        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        cube.name = $"GridCell_{x}_{y}_{z}";
                        cube.transform.SetParent(visualsRoot, false);

                        if (configuredLayer >= 0)
                        {
                            cube.layer = configuredLayer;
                        }

                        Renderer rendererComponent = cube.GetComponent<Renderer>();
                        Collider colliderComponent = cube.GetComponent<Collider>();
                        if (rendererComponent != null)
                        {
                            rendererComponent.shadowCastingMode = ShadowCastingMode.Off;
                            rendererComponent.receiveShadows = false;
                        }

                        if (colliderComponent != null)
                        {
                            colliderComponent.isTrigger = true;
                        }

                        CargoGridCellView cellView = cube.AddComponent<CargoGridCellView>();
                        cellView.Initialize(coords, rendererComponent);
                        _cellViews[coords] = cellView;
                    }
                }
            }
        }

        foreach (KeyValuePair<Vector3Int, CargoGridCellView> pair in _cellViews)
        {
            CargoGridCellView cellView = pair.Value;
            if (cellView == null)
            {
                continue;
            }

            cellView.transform.localPosition = cartInventory.GetLocalCenter(pair.Key, Vector3Int.one);
            cellView.transform.localRotation = Quaternion.identity;
            cellView.transform.localScale = Vector3.one * Mathf.Max(0.01f, cartInventory.CellWorldSize - cellPadding);
        }
    }

    private void ClearCellViews()
    {
        foreach (KeyValuePair<Vector3Int, CargoGridCellView> pair in _cellViews)
        {
            if (pair.Value != null)
            {
                Destroy(pair.Value.gameObject);
            }
        }

        _cellViews.Clear();
    }

    private void RefreshCellVisuals()
    {
        if (cartInventory == null || _cellViews.Count == 0)
        {
            return;
        }

        foreach (KeyValuePair<Vector3Int, CargoGridCellView> pair in _cellViews)
        {
            CargoGridCellView cellView = pair.Value;
            if (cellView == null || cellView.TargetRenderer == null)
            {
                continue;
            }

            Vector3Int coords = pair.Key;
            CellState targetState = ResolveCellState(coords);
            cellView.TargetRenderer.sharedMaterial = ResolveCellMaterial(targetState);
        }
    }

    private CellState ResolveCellState(Vector3Int coords)
    {
        if (_hasPlacementPreview && IsInsidePreview(coords))
        {
            return _previewValid ? CellState.ValidPlacement : CellState.InvalidPlacement;
        }

        if (_hoveredCell.HasValue && _hoveredCell.Value == coords)
        {
            return CellState.Hover;
        }

        return cartInventory.IsOccupied(coords) ? CellState.Occupied : CellState.Default;
    }

    private bool IsInsidePreview(Vector3Int coords)
    {
        return coords.x >= _previewOrigin.x
            && coords.y >= _previewOrigin.y
            && coords.z >= _previewOrigin.z
            && coords.x < _previewOrigin.x + _previewSize.x
            && coords.y < _previewOrigin.y + _previewSize.y
            && coords.z < _previewOrigin.z + _previewSize.z;
    }

    private void EnsureGhost(CargoInstance cargo)
    {
        if (_ghostObject != null && _ghostSourceCargo == cargo)
        {
            return;
        }

        HideGhost();

        GameObject source = cargo.Data != null && cargo.Data.Prefab != null
            ? cargo.Data.Prefab
            : cargo.gameObject;

        if (source == null)
        {
            return;
        }

        _ghostObject = Instantiate(source, visualsRoot);
        _ghostObject.name = $"{source.name}_Ghost";
        _ghostSourceCargo = cargo;
        PrepareGhostObject(_ghostObject);
        _ghostRenderers = _ghostObject.GetComponentsInChildren<Renderer>(true);

        Material resolvedGhostMaterial = ResolveGhostMaterial();
        for (int i = 0; i < _ghostRenderers.Length; i++)
        {
            Renderer rendererComponent = _ghostRenderers[i];
            if (rendererComponent == null)
            {
                continue;
            }

            rendererComponent.shadowCastingMode = ShadowCastingMode.Off;
            rendererComponent.receiveShadows = false;

            Material[] materials = rendererComponent.sharedMaterials;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                materials[materialIndex] = resolvedGhostMaterial;
            }

            rendererComponent.sharedMaterials = materials;
        }
    }

    private void PrepareGhostObject(GameObject ghost)
    {
        Behaviour[] behaviours = ghost.GetComponentsInChildren<Behaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            behaviours[i].enabled = false;
        }

        Collider[] colliders = ghost.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = false;
        }

        Rigidbody[] rigidbodies = ghost.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < rigidbodies.Length; i++)
        {
            rigidbodies[i].isKinematic = true;
            rigidbodies[i].useGravity = false;
            rigidbodies[i].detectCollisions = false;
        }
    }

    private void ApplyGhostTint(Color tint)
    {
        for (int i = 0; i < _ghostRenderers.Length; i++)
        {
            Renderer rendererComponent = _ghostRenderers[i];
            if (rendererComponent == null)
            {
                continue;
            }

            rendererComponent.GetPropertyBlock(_ghostPropertyBlock);
            if (HasProperty(rendererComponent.sharedMaterial, BaseColorId))
            {
                _ghostPropertyBlock.SetColor(BaseColorId, tint);
            }
            else if (HasProperty(rendererComponent.sharedMaterial, ColorId))
            {
                _ghostPropertyBlock.SetColor(ColorId, tint);
            }

            rendererComponent.SetPropertyBlock(_ghostPropertyBlock);
        }
    }

    private Material ResolveCellMaterial(CellState state)
    {
        switch (state)
        {
            case CellState.Hover:
                return hoverMaterial != null ? hoverMaterial : GetFallbackMaterial(state, new Color(0.25f, 0.65f, 1f, 0.3f));
            case CellState.ValidPlacement:
                return validPlacementMaterial != null ? validPlacementMaterial : GetFallbackMaterial(state, new Color(0.25f, 1f, 0.45f, 0.28f));
            case CellState.InvalidPlacement:
                return invalidPlacementMaterial != null ? invalidPlacementMaterial : GetFallbackMaterial(state, new Color(1f, 0.3f, 0.3f, 0.28f));
            case CellState.Occupied:
                return occupiedMaterial != null ? occupiedMaterial : GetFallbackMaterial(state, new Color(0.8f, 0.8f, 0.8f, 0.18f));
            case CellState.Default:
            default:
                return defaultMaterial != null ? defaultMaterial : GetFallbackMaterial(state, new Color(0.85f, 0.85f, 0.85f, 0.16f));
        }
    }

    private Material ResolveGhostMaterial()
    {
        return ghostMaterial != null ? ghostMaterial : GetFallbackMaterial(CellState.ValidPlacement, new Color(0.25f, 1f, 0.45f, 0.35f));
    }

    private Material GetFallbackMaterial(CellState state, Color color)
    {
        if (_fallbackMaterials.TryGetValue(state, out Material existingMaterial) && existingMaterial != null)
        {
            return existingMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader)
        {
            name = $"Runtime_{state}_CellMaterial"
        };

        if (material.HasProperty(BaseColorId))
        {
            material.SetColor(BaseColorId, color);
        }

        if (material.HasProperty(ColorId))
        {
            material.SetColor(ColorId, color);
        }

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
        }

        if (material.HasProperty("_Blend"))
        {
            material.SetFloat("_Blend", 0f);
        }

        if (material.HasProperty("_SrcBlend"))
        {
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        }

        if (material.HasProperty("_DstBlend"))
        {
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetFloat("_ZWrite", 0f);
        }

        material.renderQueue = (int)RenderQueue.Transparent;
        _fallbackMaterials[state] = material;
        return material;
    }

    private static bool HasProperty(Material material, int propertyId)
    {
        return material != null && material.HasProperty(propertyId);
    }
}
