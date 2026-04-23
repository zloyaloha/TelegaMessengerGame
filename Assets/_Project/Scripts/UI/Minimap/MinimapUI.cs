using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class MinimapUI : MonoBehaviour
{
    private static Sprite _fallbackSprite;

    [SerializeField] private RectTransform widgetRoot;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private RectTransform viewportRect;
    [SerializeField] private Image viewportMaskImage;
    [SerializeField] private Mask viewportMask;
    [SerializeField] private RawImage mapImage;
    [SerializeField] private RectTransform markerLayer;
    [SerializeField] private Image borderImage;

    [Header("Layout")]
    [SerializeField, Min(64f)] private float mapSize = 200f;
    [SerializeField, Min(0f)] private float screenMargin = 20f;
    [SerializeField, Min(0f)] private float borderWidth = 3f;
    [SerializeField, Range(0.2f, 1f)] private float fullMapScreenCoverage = 0.72f;

    [Header("Visuals")]
    [SerializeField] private Color backgroundColor = new Color(0.05f, 0.07f, 0.1f, 0.85f);
    [SerializeField] private Color borderColor = new Color(0.08f, 0.1f, 0.12f, 0.95f);
    [SerializeField] private Sprite borderSprite;

    public RawImage MapImage => mapImage;
    public RectTransform ViewportRect => viewportRect;
    public RectTransform MarkerLayer => markerLayer != null ? markerLayer : viewportRect;

    private void Awake()
    {
        ResolveReferences();
        DisableTechnicalGraphics();
    }

    private void Start()
    {
        ResolveReferences();
        DisableTechnicalGraphics();
        ApplyVisuals();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        mapSize = Mathf.Max(64f, mapSize);
        screenMargin = Mathf.Max(0f, screenMargin);
        borderWidth = Mathf.Max(0f, borderWidth);
        fullMapScreenCoverage = Mathf.Clamp(fullMapScreenCoverage, 0.2f, 1f);
        ResolveReferences();
        DisableTechnicalGraphics();
        ApplyVisuals();
    }
#endif

    public void SetTexture(Texture texture)
    {
        if (mapImage != null)
            mapImage.texture = texture;
    }

    public void SetMinimapMode()
    {
        if (widgetRoot == null)
            return;

        widgetRoot.anchorMin = Vector2.one;
        widgetRoot.anchorMax = Vector2.one;
        widgetRoot.pivot = Vector2.one;
        widgetRoot.sizeDelta = Vector2.one * mapSize;
        widgetRoot.anchoredPosition = new Vector2(-screenMargin, -screenMargin);
        ApplyViewportInset();
    }

    public void SetFullMapMode()
    {
        if (widgetRoot == null)
            return;

        widgetRoot.anchorMin = new Vector2(0.5f, 0.5f);
        widgetRoot.anchorMax = new Vector2(0.5f, 0.5f);
        widgetRoot.pivot = new Vector2(0.5f, 0.5f);
        widgetRoot.sizeDelta = Vector2.one * GetFullMapSize();
        widgetRoot.anchoredPosition = Vector2.zero;
        ApplyViewportInset();
    }

    private float GetFullMapSize()
    {
        RectTransform parentRect = widgetRoot != null ? widgetRoot.parent as RectTransform : null;
        if (parentRect == null)
            return mapSize;

        return Mathf.Min(parentRect.rect.width, parentRect.rect.height) * fullMapScreenCoverage;
    }

    private void ResolveReferences()
    {
        if (widgetRoot == null)
            widgetRoot = transform as RectTransform;

        if (widgetRoot == null)
            return;

        if (backgroundImage == null)
            backgroundImage = FindChildComponent<Image>("Background");

        if (viewportRect == null)
        {
            Transform viewport = widgetRoot.Find("Viewport");
            viewportRect = viewport as RectTransform;
        }

        if (viewportRect != null)
        {
            if (viewportMaskImage == null)
                viewportMaskImage = viewportRect.GetComponent<Image>();

            if (viewportMask == null)
                viewportMask = viewportRect.GetComponent<Mask>();

            if (mapImage == null)
                mapImage = viewportRect.GetComponentInChildren<RawImage>(true);

            if (markerLayer == null)
            {
                Transform markerTransform = viewportRect.Find("MarkerLayer");
                markerLayer = markerTransform as RectTransform;
            }
        }

        if (borderImage == null)
            borderImage = FindChildComponent<Image>("Border");
    }

    private void ApplyVisuals()
    {
        Sprite fallbackSprite = GetFallbackSprite();

        if (backgroundImage != null)
        {
            backgroundImage.sprite = fallbackSprite;
            backgroundImage.type = Image.Type.Simple;
            backgroundImage.color = borderColor;
            backgroundImage.raycastTarget = false;
        }

        if (viewportMaskImage != null)
        {
            viewportMaskImage.sprite = fallbackSprite;
            viewportMaskImage.type = Image.Type.Simple;
            viewportMaskImage.color = backgroundColor;
            viewportMaskImage.raycastTarget = false;
        }

        if (viewportMask != null)
            viewportMask.showMaskGraphic = true;

        if (borderImage != null)
        {
            borderImage.sprite = borderSprite != null ? borderSprite : fallbackSprite;
            borderImage.type = borderSprite != null ? Image.Type.Sliced : Image.Type.Simple;
            borderImage.color = borderColor;
            borderImage.raycastTarget = false;
            borderImage.enabled = borderSprite != null;
        }

        if (mapImage != null)
            mapImage.raycastTarget = false;

        ApplyViewportInset();
    }

    private void DisableTechnicalGraphics()
    {
        if (widgetRoot != null && widgetRoot.TryGetComponent(out Image rootImage))
            rootImage.enabled = false;

        if (markerLayer != null && markerLayer.TryGetComponent(out Image markerLayerImage))
            markerLayerImage.enabled = false;
    }

    private void ApplyViewportInset()
    {
        if (backgroundImage != null)
            StretchToFill(backgroundImage.rectTransform, 0f);

        if (viewportRect != null)
            StretchToFill(viewportRect, borderWidth);

        if (mapImage != null)
            StretchToFill(mapImage.rectTransform, 0f);

        if (markerLayer != null)
            StretchToFill(markerLayer, 0f);

        if (borderImage != null)
            StretchToFill(borderImage.rectTransform, 0f);
    }

    private static void StretchToFill(RectTransform target, float inset)
    {
        if (target == null)
            return;

        target.anchorMin = Vector2.zero;
        target.anchorMax = Vector2.one;
        target.pivot = new Vector2(0.5f, 0.5f);
        target.offsetMin = new Vector2(inset, inset);
        target.offsetMax = new Vector2(-inset, -inset);
        target.anchoredPosition = Vector2.zero;
    }

    private T FindChildComponent<T>(string childName) where T : Component
    {
        Transform child = widgetRoot != null ? widgetRoot.Find(childName) : null;
        return child != null ? child.GetComponent<T>() : null;
    }

    private static Sprite GetFallbackSprite()
    {
        if (_fallbackSprite != null)
            return _fallbackSprite;

        Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            name = "MinimapUIFallback"
        };
        texture.SetPixel(0, 0, Color.white);
        texture.Apply(false, true);

        _fallbackSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        _fallbackSprite.name = "MinimapUIFallback";
        return _fallbackSprite;
    }
}
