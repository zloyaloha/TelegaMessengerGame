using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class MinimapMarkers : MonoBehaviour
{
    private static Sprite _playerFallbackSprite;
    private static Sprite _circleFallbackSprite;
    private static Sprite _goalFallbackSprite;
    private static Sprite _directionFallbackSprite;

    [SerializeField] private MinimapController minimapController;
    [SerializeField] private MinimapGenerator minimapGenerator;
    [SerializeField] private MinimapUI minimapUI;

    [Header("Tracked Objects")]
    [SerializeField] private Transform playerTarget;
    [SerializeField] private Transform cartTarget;
    [SerializeField] private bool showLostCargoMarkers;

    [Header("Sprites")]
    [SerializeField] private Sprite playerMarkerSprite;
    [SerializeField] private Sprite cartMarkerSprite;
    [SerializeField] private Sprite lostCargoMarkerSprite;
    [SerializeField] private Sprite goalMarkerSprite;
    [SerializeField] private Sprite goalDirectionSprite;

    [Header("Visuals")]
    [SerializeField] private Color playerMarkerColor = Color.white;
    [SerializeField] private Color cartMarkerColor = new Color(1f, 0.84f, 0.2f, 1f);
    [SerializeField] private Color lostCargoMarkerColor = new Color(1f, 0.45f, 0.25f, 1f);
    [SerializeField] private Color goalMarkerColor = new Color(0.15f, 1f, 0.65f, 1f);
    [SerializeField] private Color goalDirectionColor = new Color(0.15f, 1f, 0.65f, 1f);
    [SerializeField] private Vector2 playerMarkerSize = new Vector2(12f, 12f);
    [SerializeField] private Vector2 cartMarkerSize = new Vector2(8f, 8f);
    [SerializeField] private Vector2 lostCargoMarkerSize = new Vector2(7f, 7f);
    [SerializeField] private Vector2 goalMarkerSize = new Vector2(12f, 12f);
    [SerializeField] private Vector2 goalDirectionSize = new Vector2(18f, 18f);
    [SerializeField, Min(0f)] private float edgePadding = 6f;
    [SerializeField, Min(0f)] private float goalDirectionOffset = 18f;
    [SerializeField, Min(0f)] private float goalDirectionEdgePadding = 16f;
    [SerializeField, Min(0f)] private float goalDirectionMinDistance = 6f;

    [Header("Marker References")]
    [SerializeField] private RectTransform playerMarker;
    [SerializeField] private RectTransform cartMarker;
    [SerializeField] private RectTransform goalMarker;
    [SerializeField] private RectTransform goalDirectionMarker;

    private readonly Dictionary<CargoInstance, RectTransform> _lostCargoMarkers = new();
    private CartInventory _trackedCartInventory;
    private Vector3 _currentGoalWorldPosition;
    private bool _hasStaticGoal;
    private Transform currentGoalTarget;

    public Transform PlayerTarget => playerTarget;

    private void Awake()
    {
        ResolveDependencies();
        EnsureCoreMarkers();
    }

    private void OnEnable()
    {
        ResolveDependencies();
        ResolveTargets();
        EnsureCoreMarkers();
        SubscribeToCargoLoss();
    }

    private void Update()
    {
        ResolveDependencies();
        ResolveTargets();
        EnsureCoreMarkers();
        if (showLostCargoMarkers && _trackedCartInventory == null && cartTarget != null)
            SubscribeToCargoLoss();

        UpdateCoreMarker(playerMarker, playerTarget, playerMarkerSize, true);
        UpdateCoreMarker(cartMarker, cartTarget, cartMarkerSize, false);
        UpdateGoalMarkers();
        UpdateLostCargoMarkers();
    }

    private void OnDisable()
    {
        UnsubscribeFromCargoLoss();
    }

    private void Reset()
    {
        minimapController = GetComponent<MinimapController>();
        minimapGenerator = FindFirstObjectByType<MinimapGenerator>();
        minimapUI = GetComponent<MinimapUI>();
        ResolveTargets();
    }

    public Vector2 WorldToMinimapPosition(Vector3 worldPos)
    {
        return TryGetMinimapPosition(worldPos, out Vector2 position, true)
            ? position
            : Vector2.zero;
    }

    public void SetGoal(Vector3 worldPosition)
    {
        _currentGoalWorldPosition = worldPosition;
        _hasStaticGoal = true;
        currentGoalTarget = null;
        EnsureCoreMarkers();
        UpdateGoalMarkers();
    }

    public void SetGoal(Transform target)
    {
        currentGoalTarget = target;
        _hasStaticGoal = false;
        EnsureCoreMarkers();
        UpdateGoalMarkers();
    }

    public void ClearGoal()
    {
        currentGoalTarget = null;
        _hasStaticGoal = false;
        _currentGoalWorldPosition = Vector3.zero;
        SetGoalVisualsActive(false);
    }

    public void SetPlayerTarget(Transform target)
    {
        playerTarget = target;
    }

    public void SetCartTarget(Transform target)
    {
        if (cartTarget == target)
        {
            return;
        }

        UnsubscribeFromCargoLoss();
        cartTarget = target;
        SubscribeToCargoLoss();
    }

    private void ResolveDependencies()
    {
        if (minimapController == null)
            minimapController = GetComponent<MinimapController>();

        if (minimapUI == null)
            minimapUI = GetComponent<MinimapUI>();

        if (minimapGenerator == null)
            minimapGenerator = FindFirstObjectByType<MinimapGenerator>();
    }

    private void ResolveTargets()
    {
        if (playerTarget == null)
        {
            PlayerMovement playerMovement = FindFirstObjectByType<PlayerMovement>();
            playerTarget = playerMovement != null ? playerMovement.transform : null;
        }

        if (cartTarget == null)
        {
            CartController cartController = FindFirstObjectByType<CartController>();
            cartTarget = cartController != null ? cartController.transform : null;
        }
    }

    private void EnsureCoreMarkers()
    {
        if (minimapUI == null || minimapUI.MarkerLayer == null)
            return;

        if (playerMarker == null)
            playerMarker = CreateMarker(
                "PlayerMarker",
                playerMarkerSprite != null ? playerMarkerSprite : GetPlayerMarkerSprite(),
                playerMarkerColor,
                playerMarkerSize);

        if (cartMarker == null)
            cartMarker = CreateMarker(
                "CartMarker",
                cartMarkerSprite != null ? cartMarkerSprite : GetCircleMarkerSprite(),
                cartMarkerColor,
                cartMarkerSize);

        if (goalMarker == null)
            goalMarker = CreateMarker(
                "GoalMarker",
                goalMarkerSprite != null ? goalMarkerSprite : GetGoalMarkerSprite(),
                goalMarkerColor,
                goalMarkerSize);

        if (goalDirectionMarker == null)
            goalDirectionMarker = CreateMarker(
                "GoalDirectionMarker",
                goalDirectionSprite != null ? goalDirectionSprite : GetDirectionArrowSprite(),
                goalDirectionColor,
                goalDirectionSize);

        SetGoalVisualsActive(HasGoal());
    }

    private RectTransform CreateMarker(string markerName, Sprite sprite, Color color, Vector2 size)
    {
        GameObject markerObject = new GameObject(markerName, typeof(RectTransform), typeof(Image));
        markerObject.transform.SetParent(minimapUI.MarkerLayer, false);

        RectTransform rectTransform = markerObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.sizeDelta = size;

        Image image = markerObject.GetComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;

        return rectTransform;
    }

    private void UpdateCoreMarker(RectTransform marker, Transform target, Vector2 size, bool rotateWithTarget)
    {
        if (marker == null)
            return;

        if (target == null)
        {
            marker.gameObject.SetActive(false);
            return;
        }

        marker.gameObject.SetActive(true);
        marker.sizeDelta = size;
        marker.anchoredPosition = WorldToMinimapPosition(target.position);

        if (rotateWithTarget)
            marker.localEulerAngles = new Vector3(0f, 0f, -target.eulerAngles.y);
        else
            marker.localEulerAngles = Vector3.zero;
    }

    private void UpdateGoalMarkers()
    {
        if (goalMarker == null || goalDirectionMarker == null)
            return;

        if (!TryGetGoalWorldPosition(out Vector3 goalWorldPosition))
        {
            SetGoalVisualsActive(false);
            return;
        }

        SetGoalVisualsActive(true);

        goalMarker.sizeDelta = goalMarkerSize;
        goalMarker.localEulerAngles = Vector3.zero;

        bool hasProjectedGoalPosition = TryGetMinimapPosition(goalWorldPosition, out Vector2 projectedGoalPosition, true);
        if (hasProjectedGoalPosition)
        {
            goalMarker.gameObject.SetActive(true);
            goalMarker.anchoredPosition = projectedGoalPosition;
        }
        else if (TryGetWorldDirectionToGoal(goalWorldPosition, out Vector2 fallbackDirection))
        {
            goalMarker.gameObject.SetActive(true);
            goalMarker.anchoredPosition = ClampToViewport(
                fallbackDirection * Mathf.Max(goalDirectionOffset * 1.6f, 28f),
                edgePadding);
        }
        else
        {
            goalMarker.gameObject.SetActive(false);
        }

        Transform focusTarget = playerTarget != null ? playerTarget : cartTarget;
        if (focusTarget == null
            || !TryGetMinimapPosition(focusTarget.position, out Vector2 focusPosition, false)
            || !TryGetMinimapPosition(goalWorldPosition, out Vector2 rawGoalPosition, false))
        {
            UpdateGoalDirectionFallback(goalWorldPosition);
            return;
        }

        Vector2 direction = rawGoalPosition - focusPosition;
        float distance = direction.magnitude;
        if (distance <= goalDirectionMinDistance)
        {
            goalDirectionMarker.gameObject.SetActive(false);
            return;
        }

        Vector2 normalizedDirection = direction / distance;
        Vector2 arrowPosition = focusPosition + (normalizedDirection * goalDirectionOffset);
        arrowPosition = ClampToViewport(arrowPosition, goalDirectionEdgePadding);

        goalDirectionMarker.gameObject.SetActive(true);
        goalDirectionMarker.sizeDelta = goalDirectionSize;
        goalDirectionMarker.anchoredPosition = arrowPosition;
        goalDirectionMarker.localEulerAngles = new Vector3(
            0f,
            0f,
            Mathf.Atan2(normalizedDirection.y, normalizedDirection.x) * Mathf.Rad2Deg - 90f);
    }

    private void UpdateGoalDirectionFallback(Vector3 goalWorldPosition)
    {
        if (!TryGetWorldDirectionToGoal(goalWorldPosition, out Vector2 fallbackDirection))
        {
            goalDirectionMarker.gameObject.SetActive(false);
            return;
        }

        goalDirectionMarker.gameObject.SetActive(true);
        goalDirectionMarker.sizeDelta = goalDirectionSize;
        goalDirectionMarker.anchoredPosition = ClampToViewport(
            fallbackDirection * goalDirectionOffset,
            goalDirectionEdgePadding);
        goalDirectionMarker.localEulerAngles = new Vector3(
            0f,
            0f,
            Mathf.Atan2(fallbackDirection.y, fallbackDirection.x) * Mathf.Rad2Deg - 90f);
    }

    private bool TryGetGoalWorldPosition(out Vector3 goalWorldPosition)
    {
        if (currentGoalTarget != null)
        {
            goalWorldPosition = currentGoalTarget.position;
            return true;
        }

        if (_hasStaticGoal)
        {
            goalWorldPosition = _currentGoalWorldPosition;
            return true;
        }

        goalWorldPosition = Vector3.zero;
        return false;
    }

    private bool TryGetMinimapPosition(Vector3 worldPos, out Vector2 localPosition, bool clampToBounds)
    {
        localPosition = Vector2.zero;

        if (minimapController == null || minimapGenerator == null || minimapUI == null || minimapUI.ViewportRect == null)
            return false;

        Rect uvRect = minimapController.CurrentUvRect;
        if (uvRect.width <= Mathf.Epsilon || uvRect.height <= Mathf.Epsilon)
            return false;

        Vector2 normalized = minimapGenerator.WorldToNormalizedCoordinates(worldPos);
        Vector2 viewportSize = minimapUI.ViewportRect.rect.size;

        float localX = ((normalized.x - uvRect.xMin) / uvRect.width - 0.5f) * viewportSize.x;
        float localY = ((normalized.y - uvRect.yMin) / uvRect.height - 0.5f) * viewportSize.y;
        localPosition = new Vector2(localX, localY);

        if (clampToBounds)
            localPosition = ClampToViewport(localPosition, edgePadding);

        return true;
    }

    private Vector2 ClampToViewport(Vector2 position, float padding)
    {
        if (minimapUI == null || minimapUI.ViewportRect == null)
            return position;

        Vector2 viewportSize = minimapUI.ViewportRect.rect.size;
        float maxX = Mathf.Max(0f, viewportSize.x * 0.5f - padding);
        float maxY = Mathf.Max(0f, viewportSize.y * 0.5f - padding);

        return new Vector2(
            Mathf.Clamp(position.x, -maxX, maxX),
            Mathf.Clamp(position.y, -maxY, maxY));
    }

    private bool HasGoal()
    {
        return currentGoalTarget != null || _hasStaticGoal;
    }

    private bool TryGetWorldDirectionToGoal(Vector3 goalWorldPosition, out Vector2 direction)
    {
        direction = Vector2.zero;

        Transform focusTarget = playerTarget != null ? playerTarget : cartTarget;
        if (focusTarget == null)
        {
            return false;
        }

        Vector3 worldOffset = goalWorldPosition - focusTarget.position;
        worldOffset.y = 0f;
        if (worldOffset.sqrMagnitude <= 0.0001f)
        {
            return false;
        }

        direction = new Vector2(worldOffset.x, worldOffset.z).normalized;
        return true;
    }

    private void SetGoalVisualsActive(bool isActive)
    {
        if (goalMarker != null)
            goalMarker.gameObject.SetActive(isActive);

        if (goalDirectionMarker != null)
            goalDirectionMarker.gameObject.SetActive(isActive);
    }

    private void SubscribeToCargoLoss()
    {
        UnsubscribeFromCargoLoss();

        if (!showLostCargoMarkers || cartTarget == null)
            return;

        _trackedCartInventory = cartTarget.GetComponent<CartInventory>();
        if (_trackedCartInventory != null)
            _trackedCartInventory.OnCargoLost += HandleCargoLost;
    }

    private void UnsubscribeFromCargoLoss()
    {
        if (_trackedCartInventory != null)
            _trackedCartInventory.OnCargoLost -= HandleCargoLost;

        _trackedCartInventory = null;
    }

    private void HandleCargoLost(CargoInstance cargo)
    {
        if (!showLostCargoMarkers || cargo == null || minimapUI == null)
            return;

        if (_lostCargoMarkers.ContainsKey(cargo))
            return;

        RectTransform marker = CreateMarker(
            $"LostCargo_{cargo.name}",
            lostCargoMarkerSprite != null ? lostCargoMarkerSprite : GetCircleMarkerSprite(),
            lostCargoMarkerColor,
            lostCargoMarkerSize);

        _lostCargoMarkers[cargo] = marker;
    }

    private void UpdateLostCargoMarkers()
    {
        if (!showLostCargoMarkers || _lostCargoMarkers.Count == 0)
            return;

        List<CargoInstance> staleCargo = null;
        foreach (KeyValuePair<CargoInstance, RectTransform> entry in _lostCargoMarkers)
        {
            CargoInstance cargo = entry.Key;
            RectTransform marker = entry.Value;

            if (cargo == null || marker == null || cargo.State != CargoState.Free || !cargo.gameObject.activeInHierarchy)
            {
                staleCargo ??= new List<CargoInstance>();
                staleCargo.Add(cargo);
                continue;
            }

            marker.gameObject.SetActive(true);
            marker.sizeDelta = lostCargoMarkerSize;
            marker.anchoredPosition = WorldToMinimapPosition(cargo.transform.position);
        }

        if (staleCargo == null)
            return;

        for (int i = 0; i < staleCargo.Count; i++)
        {
            CargoInstance cargo = staleCargo[i];
            if (cargo != null && _lostCargoMarkers.TryGetValue(cargo, out RectTransform marker) && marker != null)
                Destroy(marker.gameObject);

            _lostCargoMarkers.Remove(cargo);
        }
    }

    private static Sprite GetPlayerMarkerSprite()
    {
        if (_playerFallbackSprite != null)
            return _playerFallbackSprite;

        const int size = 32;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "MinimapPlayerMarker"
        };

        Color clear = new Color(0f, 0f, 0f, 0f);
        Color[] pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = clear;

        Vector2 top = new Vector2(size * 0.5f, size - 3f);
        Vector2 left = new Vector2(5f, 7f);
        Vector2 right = new Vector2(size - 5f, 7f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if (IsPointInTriangle(new Vector2(x + 0.5f, y + 0.5f), top, right, left))
                    pixels[y * size + x] = Color.white;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);

        _playerFallbackSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        _playerFallbackSprite.name = "MinimapPlayerMarker";
        return _playerFallbackSprite;
    }

    private static Sprite GetCircleMarkerSprite()
    {
        if (_circleFallbackSprite != null)
            return _circleFallbackSprite;

        const int size = 24;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "MinimapCircleMarker"
        };

        Color clear = new Color(0f, 0f, 0f, 0f);
        Color[] pixels = new Color[size * size];
        float radius = (size * 0.5f) - 1f;
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(center, new Vector2(x + 0.5f, y + 0.5f));
                pixels[y * size + x] = distance <= radius ? Color.white : clear;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);

        _circleFallbackSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        _circleFallbackSprite.name = "MinimapCircleMarker";
        return _circleFallbackSprite;
    }

    private static Sprite GetGoalMarkerSprite()
    {
        if (_goalFallbackSprite != null)
            return _goalFallbackSprite;

        const int size = 28;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "MinimapGoalMarker"
        };

        Color clear = new Color(0f, 0f, 0f, 0f);
        Color[] pixels = new Color[size * size];
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float radius = size * 0.38f;

        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = clear;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 point = new Vector2(x + 0.5f, y + 0.5f);
                float manhattanDistance = Mathf.Abs(point.x - center.x) + Mathf.Abs(point.y - center.y);
                if (manhattanDistance <= radius)
                    pixels[y * size + x] = Color.white;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);

        _goalFallbackSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        _goalFallbackSprite.name = "MinimapGoalMarker";
        return _goalFallbackSprite;
    }

    private static Sprite GetDirectionArrowSprite()
    {
        if (_directionFallbackSprite != null)
            return _directionFallbackSprite;

        const int size = 32;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "MinimapGoalDirectionMarker"
        };

        Color clear = new Color(0f, 0f, 0f, 0f);
        Color[] pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = clear;

        Vector2 top = new Vector2(size * 0.5f, size - 3f);
        Vector2 left = new Vector2(8f, 9f);
        Vector2 right = new Vector2(size - 8f, 9f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if (IsPointInTriangle(new Vector2(x + 0.5f, y + 0.5f), top, right, left))
                    pixels[y * size + x] = Color.white;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);

        _directionFallbackSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        _directionFallbackSprite.name = "MinimapGoalDirectionMarker";
        return _directionFallbackSprite;
    }

    private static bool IsPointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
    {
        float area = SignedTriangleArea(a, b, c);
        float area1 = SignedTriangleArea(point, b, c);
        float area2 = SignedTriangleArea(a, point, c);
        float area3 = SignedTriangleArea(a, b, point);

        bool hasNegative = area1 < 0f || area2 < 0f || area3 < 0f;
        bool hasPositive = area1 > 0f || area2 > 0f || area3 > 0f;
        return Mathf.Abs(area) > Mathf.Epsilon && !(hasNegative && hasPositive);
    }

    private static float SignedTriangleArea(Vector2 a, Vector2 b, Vector2 c) =>
        (a.x * (b.y - c.y) + b.x * (c.y - a.y) + c.x * (a.y - b.y)) * 0.5f;
}
