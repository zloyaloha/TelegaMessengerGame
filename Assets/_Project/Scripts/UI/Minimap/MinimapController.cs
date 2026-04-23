using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class MinimapController : MonoBehaviour
{
    [SerializeField] private MinimapUI minimapUI;
    [SerializeField] private MinimapGenerator minimapGenerator;
    [SerializeField] private MinimapMarkers minimapMarkers;
    [SerializeField] private Transform playerTarget;

    [Header("Modes")]
    [SerializeField, Min(1f)] private float minimapWorldRadius = 50f;
    [SerializeField] private bool startExpanded;

    [Header("Full Map Zoom")]
    [SerializeField, Min(1f)] private float fullMapMinZoom = 1f;
    [SerializeField, Min(1f)] private float fullMapMaxZoom = 4f;
    [SerializeField, Min(0.001f)] private float fullMapZoomScrollFactor = 0.01f;

    private bool _isFullMapOpen;
    private float _fullMapZoom = 1f;
    private Rect _currentUvRect = new Rect(0f, 0f, 1f, 1f);

    public Rect CurrentUvRect => _currentUvRect;
    public Transform PlayerTarget => playerTarget;
    public bool IsFullMapOpen => _isFullMapOpen;

    private void Awake()
    {
        ResolveDependencies();
    }

    private void OnEnable()
    {
        ResolveDependencies();

        if (minimapGenerator != null)
            minimapGenerator.TextureGenerated += HandleTextureGenerated;
    }

    private void Start()
    {
        ResolveDependencies();

        _isFullMapOpen = startExpanded;
        ApplyModeLayout();

        if (minimapGenerator != null && minimapGenerator.MinimapTexture != null)
            minimapUI?.SetTexture(minimapGenerator.MinimapTexture);

        UpdateMapView();
    }

    private void Update()
    {
        HandleToggleInput();
        HandleZoomInput();
        UpdateMapView();
    }

    private void OnDisable()
    {
        if (minimapGenerator != null)
            minimapGenerator.TextureGenerated -= HandleTextureGenerated;
    }

    private void Reset()
    {
        minimapUI = GetComponent<MinimapUI>();
        minimapMarkers = GetComponent<MinimapMarkers>();
        minimapGenerator = FindFirstObjectByType<MinimapGenerator>();

        PlayerMovement playerMovement = FindFirstObjectByType<PlayerMovement>();
        playerTarget = playerMovement != null ? playerMovement.transform : null;
    }

    public void ToggleMap()
    {
        _isFullMapOpen = !_isFullMapOpen;
        if (_isFullMapOpen)
            _fullMapZoom = Mathf.Clamp(_fullMapZoom, fullMapMinZoom, fullMapMaxZoom);
        else
            _fullMapZoom = 1f;

        ApplyModeLayout();
        UpdateMapView();
    }

    private void HandleToggleInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.mKey.wasPressedThisFrame)
            ToggleMap();
    }

    private void HandleZoomInput()
    {
        if (!_isFullMapOpen)
            return;

        Mouse mouse = Mouse.current;
        if (mouse == null)
            return;

        float scrollDelta = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scrollDelta) <= Mathf.Epsilon)
            return;

        _fullMapZoom = Mathf.Clamp(
            _fullMapZoom + scrollDelta * fullMapZoomScrollFactor,
            fullMapMinZoom,
            fullMapMaxZoom);
    }

    private void UpdateMapView()
    {
        if (minimapGenerator == null || minimapUI == null || minimapUI.MapImage == null)
            return;

        _currentUvRect = _isFullMapOpen
            ? BuildFullMapUvRect()
            : BuildMinimapUvRect();

        minimapUI.MapImage.uvRect = _currentUvRect;
    }

    private Rect BuildMinimapUvRect()
    {
        Rect bounds = minimapGenerator.WorldBoundsXZ;
        if (bounds.width <= Mathf.Epsilon || bounds.height <= Mathf.Epsilon)
            return new Rect(0f, 0f, 1f, 1f);

        Transform focusTarget = playerTarget != null ? playerTarget : minimapMarkers?.PlayerTarget;
        Vector2 center = focusTarget != null
            ? minimapGenerator.WorldToNormalizedCoordinates(focusTarget.position)
            : new Vector2(0.5f, 0.5f);

        float width = Mathf.Clamp01(minimapWorldRadius * 2f / bounds.width);
        float height = Mathf.Clamp01(minimapWorldRadius * 2f / bounds.height);

        return new Rect(
            center.x - width * 0.5f,
            center.y - height * 0.5f,
            width,
            height);
    }

    private Rect BuildFullMapUvRect()
    {
        float zoom = Mathf.Clamp(_fullMapZoom, fullMapMinZoom, fullMapMaxZoom);
        float size = 1f / Mathf.Max(zoom, 1f);
        return new Rect(
            0.5f - size * 0.5f,
            0.5f - size * 0.5f,
            size,
            size);
    }

    private void ApplyModeLayout()
    {
        if (minimapUI == null)
            return;

        if (_isFullMapOpen)
            minimapUI.SetFullMapMode();
        else
            minimapUI.SetMinimapMode();
    }

    private void HandleTextureGenerated(Texture2D texture)
    {
        minimapUI?.SetTexture(texture);
        UpdateMapView();
    }

    private void ResolveDependencies()
    {
        if (minimapUI == null)
            minimapUI = GetComponent<MinimapUI>();

        if (minimapMarkers == null)
            minimapMarkers = GetComponent<MinimapMarkers>();

        if (minimapGenerator == null)
            minimapGenerator = FindFirstObjectByType<MinimapGenerator>();

        if (playerTarget == null)
        {
            PlayerMovement playerMovement = FindFirstObjectByType<PlayerMovement>();
            playerTarget = playerMovement != null ? playerMovement.transform : null;
        }
    }
}
