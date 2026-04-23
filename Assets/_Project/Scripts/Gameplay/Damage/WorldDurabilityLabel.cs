using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class WorldDurabilityLabel : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Durability durability;
    [SerializeField] private Renderer[] sourceRenderers;

    [Header("Layout")]
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 0.45f, 0f);
    [SerializeField, Min(0.05f)] private float worldTextScale = 0.24f;
    [SerializeField, Min(8)] private int fontSize = 48;
    [SerializeField] private string labelPrefix = "HP";

    [Header("Colors")]
    [SerializeField] private Color healthyColor = Color.white;
    [SerializeField] private Color damagedColor = new Color(1f, 0.82f, 0.35f, 1f);
    [SerializeField] private Color destroyedColor = new Color(1f, 0.35f, 0.35f, 1f);

    private Transform _labelTransform;
    private TextMesh _textMesh;
    private MeshRenderer _textRenderer;
    private Transform _cameraTransform;
    private Durability _subscribedDurability;
    private float _computedWorldScale = 0.35f;

    private void Awake()
    {
        if (durability == null)
        {
            durability = GetComponent<Durability>();
        }

        if (sourceRenderers == null || sourceRenderers.Length == 0)
        {
            sourceRenderers = CollectSourceRenderers();
        }

        EnsureLabelCreated();
        RefreshPlacement();
        RefreshText();
    }

    private void OnEnable()
    {
        SubscribeToDurability(durability);
        RefreshText();
    }

    private void LateUpdate()
    {
        RefreshPlacement();
        UpdateLabelTransform();
    }

    private void OnDisable()
    {
        SubscribeToDurability(null);
    }

    public void Initialize(Durability sourceDurability, Renderer[] renderers)
    {
        durability = sourceDurability;

        if (renderers != null && renderers.Length > 0)
        {
            sourceRenderers = renderers;
        }
        else if (sourceRenderers == null || sourceRenderers.Length == 0)
        {
            sourceRenderers = CollectSourceRenderers();
        }

        EnsureLabelCreated();
        SubscribeToDurability(durability);
        RefreshPlacement();
        RefreshText();
        UpdateLabelTransform();
    }

    private void HandleDurabilityChanged(Durability currentDurability)
    {
        RefreshText();
    }

    private void EnsureLabelCreated()
    {
        if (_textMesh != null && _labelTransform != null)
        {
            return;
        }

        Transform existingTransform = transform.Find("WorldDurabilityLabel");
        if (existingTransform != null)
        {
            _labelTransform = existingTransform;
            _textMesh = existingTransform.GetComponent<TextMesh>();
            _textRenderer = existingTransform.GetComponent<MeshRenderer>();
        }

        if (_labelTransform == null)
        {
            GameObject labelObject = new GameObject("WorldDurabilityLabel");
            _labelTransform = labelObject.transform;
            _labelTransform.SetParent(transform, false);
            _textMesh = labelObject.AddComponent<TextMesh>();
            _textRenderer = labelObject.GetComponent<MeshRenderer>();
        }

        if (_textMesh == null)
        {
            _textMesh = _labelTransform.gameObject.AddComponent<TextMesh>();
        }

        if (_textRenderer == null)
        {
            _textRenderer = _labelTransform.GetComponent<MeshRenderer>();
        }

        Font builtInFont = LoadBuiltInFont();
        if (builtInFont != null)
        {
            _textMesh.font = builtInFont;
            _textRenderer.sharedMaterial = builtInFont.material;
        }

        _textMesh.anchor = TextAnchor.MiddleCenter;
        _textMesh.alignment = TextAlignment.Center;
        _textMesh.characterSize = 0.1f;
        _textMesh.fontSize = fontSize;
        _textMesh.richText = false;

        _textRenderer.shadowCastingMode = ShadowCastingMode.Off;
        _textRenderer.receiveShadows = false;
        _textRenderer.lightProbeUsage = LightProbeUsage.Off;
        _textRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        _textRenderer.allowOcclusionWhenDynamic = false;
        _textRenderer.sortingOrder = 5000;
    }

    private void RefreshPlacement()
    {
        EnsureLabelCreated();

        if (sourceRenderers == null || sourceRenderers.Length == 0)
        {
            sourceRenderers = CollectSourceRenderers();
        }

        bool hasBounds = false;
        Bounds combinedBounds = default;

        for (int i = 0; i < sourceRenderers.Length; i++)
        {
            Renderer targetRenderer = sourceRenderers[i];
            if (targetRenderer == null || targetRenderer == _textRenderer)
            {
                continue;
            }

            if (!hasBounds)
            {
                combinedBounds = targetRenderer.bounds;
                hasBounds = true;
            }
            else
            {
                combinedBounds.Encapsulate(targetRenderer.bounds);
            }
        }

        Vector3 worldAnchor = hasBounds
            ? combinedBounds.center + (Vector3.up * combinedBounds.extents.y)
            : transform.position + Vector3.up;

        float maxDimension = hasBounds
            ? Mathf.Max(combinedBounds.size.x, combinedBounds.size.y, combinedBounds.size.z)
            : 1f;

        _computedWorldScale = Mathf.Clamp(maxDimension * worldTextScale, 0.35f, 1.25f);
        _labelTransform.position = worldAnchor + worldOffset;
    }

    private void UpdateLabelTransform()
    {
        if (_labelTransform == null)
        {
            return;
        }

        if (_cameraTransform == null)
        {
            Camera mainCamera = Camera.main;
            _cameraTransform = mainCamera != null ? mainCamera.transform : null;
        }

        if (_cameraTransform != null)
        {
            Vector3 toCamera = _cameraTransform.position - _labelTransform.position;
            if (toCamera.sqrMagnitude > 0.001f)
            {
                _labelTransform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
            }
        }

        Vector3 lossyScale = transform.lossyScale;
        _labelTransform.localScale = new Vector3(
            _computedWorldScale * SafeInverse(lossyScale.x),
            _computedWorldScale * SafeInverse(lossyScale.y),
            _computedWorldScale * SafeInverse(lossyScale.z));
    }

    private void RefreshText()
    {
        if (_textMesh == null)
        {
            return;
        }

        if (durability == null)
        {
            _textMesh.text = string.Empty;
            if (_textRenderer != null)
            {
                _textRenderer.enabled = false;
            }

            return;
        }

        int currentValue = Mathf.CeilToInt(durability.CurrentDurability);
        int maxValue = Mathf.CeilToInt(durability.MaxDurability);
        _textMesh.text = string.IsNullOrWhiteSpace(labelPrefix)
            ? $"{currentValue}/{maxValue}"
            : $"{labelPrefix}: {currentValue}/{maxValue}";

        _textMesh.color = durability.IsDestroyed
            ? destroyedColor
            : Color.Lerp(damagedColor, healthyColor, durability.NormalizedDurability);

        if (_textRenderer != null)
        {
            _textRenderer.enabled = true;
        }
    }

    private void SubscribeToDurability(Durability targetDurability)
    {
        if (_subscribedDurability == targetDurability)
        {
            return;
        }

        if (_subscribedDurability != null)
        {
            _subscribedDurability.DurabilityChanged -= HandleDurabilityChanged;
            _subscribedDurability.Destroyed -= HandleDurabilityChanged;
        }

        _subscribedDurability = targetDurability;

        if (_subscribedDurability != null)
        {
            _subscribedDurability.DurabilityChanged += HandleDurabilityChanged;
            _subscribedDurability.Destroyed += HandleDurabilityChanged;
        }
    }

    private Renderer[] CollectSourceRenderers()
    {
        Renderer[] allRenderers = GetComponentsInChildren<Renderer>(true);
        int validRendererCount = 0;

        for (int i = 0; i < allRenderers.Length; i++)
        {
            if (allRenderers[i] != null && allRenderers[i] != _textRenderer)
            {
                validRendererCount++;
            }
        }

        Renderer[] collectedRenderers = new Renderer[validRendererCount];
        int writeIndex = 0;

        for (int i = 0; i < allRenderers.Length; i++)
        {
            Renderer targetRenderer = allRenderers[i];
            if (targetRenderer == null || targetRenderer == _textRenderer)
            {
                continue;
            }

            collectedRenderers[writeIndex++] = targetRenderer;
        }

        return collectedRenderers;
    }

    private static Font LoadBuiltInFont()
    {
        Font font = TryLoadBuiltInFont("LegacyRuntime.ttf");
        return font != null ? font : TryLoadBuiltInFont("Arial.ttf");
    }

    private static Font TryLoadBuiltInFont(string resourceName)
    {
        try
        {
            return Resources.GetBuiltinResource<Font>(resourceName);
        }
        catch (System.ArgumentException)
        {
            return null;
        }
    }

    private static float SafeInverse(float value)
    {
        float absoluteValue = Mathf.Abs(value);
        return absoluteValue > 0.0001f ? 1f / absoluteValue : 1f;
    }
}
