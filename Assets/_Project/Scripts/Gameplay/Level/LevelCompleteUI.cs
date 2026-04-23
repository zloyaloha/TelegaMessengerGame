using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class LevelCompleteUI : MonoBehaviour
{
    [Header("Visuals")]
    [SerializeField] private Color overlayColor = new Color(0f, 0f, 0f, 0.72f);
    [SerializeField] private Color panelColor = new Color(0.11f, 0.09f, 0.05f, 0.94f);
    [SerializeField] private Color accentColor = new Color(1f, 0.9f, 0.2f, 1f);
    [SerializeField] private Color textColor = new Color(1f, 0.96f, 0.85f, 1f);
    [SerializeField] private Color dimmedStarColor = new Color(0.35f, 0.35f, 0.35f, 1f);

    [Header("References")]
    [SerializeField] private Canvas rootCanvas;
    [SerializeField] private RectTransform overlayRoot;
    [SerializeField] private Image[] starImages;
    [SerializeField] private Text titleText;
    [SerializeField] private Text deliveredText;
    [SerializeField] private Text cargoHpText;
    [SerializeField] private Text cartHpText;
    [SerializeField] private Text scoreText;
    [SerializeField] private Button nextLevelButton;
    [SerializeField] private Button restartButton;
    [SerializeField] private Behaviour[] blockedBehaviours;

    private readonly List<Behaviour> _resolvedBlockedBehaviours = new();
    private readonly Dictionary<Behaviour, bool> _cachedEnabledStates = new();
    private static Sprite _starSprite;

    public event Action NextLevelRequested;
    public event Action RestartRequested;

    private void Awake()
    {
        EnsureUi();
        HideImmediate();
    }

    public void Show(LevelResult result)
    {
        EnsureUi();
        overlayRoot.gameObject.SetActive(true);
        titleText.text = result.stars >= 3 ? "Доставка выполнена блестяще" : "Доставка завершена";
        deliveredText.text = $"Доставлено ящиков: {result.deliveredCargoCount}/{result.totalCargoCount}";
        cargoHpText.text = $"Средняя сохранность груза: {Mathf.RoundToInt(result.averageCargoHpPercent * 100f)}%";
        cartHpText.text = $"Состояние телеги: {Mathf.RoundToInt(result.cartHpPercent * 100f)}%";
        scoreText.text = $"Итоговый балл: {Mathf.RoundToInt(result.finalScore * 100f)}%";

        for (int i = 0; i < starImages.Length; i++)
        {
            if (starImages[i] == null)
            {
                continue;
            }

            starImages[i].color = i < result.stars ? accentColor : dimmedStarColor;
        }

        SetControlsBlocked(true);
        Time.timeScale = 0f;
    }

    public void HideImmediate()
    {
        EnsureUi();
        overlayRoot.gameObject.SetActive(false);
        SetControlsBlocked(false);
        Time.timeScale = 1f;
    }

    public void SetNextLevelInteractable(bool interactable)
    {
        EnsureUi();
        if (nextLevelButton != null)
        {
            nextLevelButton.interactable = interactable;
        }
    }

    private void EnsureUi()
    {
        if (rootCanvas == null)
        {
            rootCanvas = GetComponent<Canvas>();
        }

        if (rootCanvas == null)
        {
            rootCanvas = gameObject.AddComponent<Canvas>();
            rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }

        if (GetComponent<CanvasScaler>() == null)
        {
            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
        }

        if (GetComponent<GraphicRaycaster>() == null)
        {
            gameObject.AddComponent<GraphicRaycaster>();
        }

        if (overlayRoot != null)
        {
            return;
        }

        Font font = LoadBuiltinFont();

        overlayRoot = CreateUiObject("OverlayRoot", transform);
        overlayRoot.anchorMin = Vector2.zero;
        overlayRoot.anchorMax = Vector2.one;
        overlayRoot.offsetMin = Vector2.zero;
        overlayRoot.offsetMax = Vector2.zero;

        Image overlayImage = overlayRoot.gameObject.AddComponent<Image>();
        overlayImage.color = overlayColor;

        RectTransform panel = CreateUiObject("ResultPanel", overlayRoot);
        panel.anchorMin = new Vector2(0.5f, 0.5f);
        panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(520f, 420f);
        Image panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.color = panelColor;
        VerticalLayoutGroup panelLayout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(28, 28, 28, 28);
        panelLayout.spacing = 14f;
        panelLayout.childAlignment = TextAnchor.UpperCenter;
        panelLayout.childControlHeight = true;
        panelLayout.childControlWidth = true;
        panelLayout.childForceExpandHeight = false;
        panelLayout.childForceExpandWidth = true;

        titleText = CreateText("Title", panel, font, 30, FontStyle.Bold);
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.color = textColor;

        RectTransform starsRow = CreateUiObject("StarsRow", panel);
        HorizontalLayoutGroup starLayout = starsRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        starLayout.childAlignment = TextAnchor.MiddleCenter;
        starLayout.spacing = 12f;
        starLayout.childControlWidth = false;
        starLayout.childControlHeight = false;
        starLayout.childForceExpandWidth = false;
        starLayout.childForceExpandHeight = false;
        LayoutElement starRowLayout = starsRow.gameObject.AddComponent<LayoutElement>();
        starRowLayout.preferredHeight = 72f;

        starImages = new Image[3];
        for (int i = 0; i < starImages.Length; i++)
        {
            RectTransform star = CreateUiObject($"Star_{i}", starsRow);
            star.sizeDelta = new Vector2(52f, 52f);
            Image image = star.gameObject.AddComponent<Image>();
            image.sprite = GetStarSprite();
            image.color = dimmedStarColor;
            starImages[i] = image;
        }

        deliveredText = CreateStatText("DeliveredText", panel, font);
        cargoHpText = CreateStatText("CargoHpText", panel, font);
        cartHpText = CreateStatText("CartHpText", panel, font);
        scoreText = CreateStatText("ScoreText", panel, font);

        RectTransform buttonsRow = CreateUiObject("ButtonsRow", panel);
        HorizontalLayoutGroup buttonLayout = buttonsRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        buttonLayout.spacing = 14f;
        buttonLayout.childControlWidth = true;
        buttonLayout.childControlHeight = true;
        buttonLayout.childForceExpandWidth = true;
        buttonLayout.childForceExpandHeight = false;
        LayoutElement buttonsLayout = buttonsRow.gameObject.AddComponent<LayoutElement>();
        buttonsLayout.preferredHeight = 52f;

        nextLevelButton = CreateButton("NextLevelButton", "Следующий уровень", buttonsRow, font, HandleNextLevelClicked);
        restartButton = CreateButton("RestartButton", "Заново", buttonsRow, font, HandleRestartClicked);
    }

    private void HandleNextLevelClicked()
    {
        HideImmediate();
        NextLevelRequested?.Invoke();
    }

    private void HandleRestartClicked()
    {
        HideImmediate();
        RestartRequested?.Invoke();
    }

    private void SetControlsBlocked(bool blocked)
    {
        ResolveBlockedBehaviours();

        if (blocked)
        {
            _cachedEnabledStates.Clear();
            for (int i = 0; i < _resolvedBlockedBehaviours.Count; i++)
            {
                Behaviour behaviour = _resolvedBlockedBehaviours[i];
                if (behaviour == null)
                {
                    continue;
                }

                _cachedEnabledStates[behaviour] = behaviour.enabled;
                behaviour.enabled = false;
            }

            return;
        }

        foreach (KeyValuePair<Behaviour, bool> entry in _cachedEnabledStates)
        {
            if (entry.Key != null)
            {
                entry.Key.enabled = entry.Value;
            }
        }

        _cachedEnabledStates.Clear();
    }

    private void ResolveBlockedBehaviours()
    {
        if (_resolvedBlockedBehaviours.Count > 0)
        {
            return;
        }

        if (blockedBehaviours != null && blockedBehaviours.Length > 0)
        {
            for (int i = 0; i < blockedBehaviours.Length; i++)
            {
                if (blockedBehaviours[i] != null)
                {
                    _resolvedBlockedBehaviours.Add(blockedBehaviours[i]);
                }
            }

            return;
        }

        AddIfFound(FindFirstObjectByType<PlayerMovement>());
        AddIfFound(FindFirstObjectByType<PlayerInteractor>());
        AddIfFound(FindFirstObjectByType<PlayerCarryController>());
        AddIfFound(FindFirstObjectByType<CartPuller>());

        CartController cartController = FindFirstObjectByType<CartController>();
        if (cartController != null && cartController.CargoGridInput != null)
        {
            AddIfFound(cartController.CargoGridInput);
        }
    }

    private void AddIfFound(Behaviour behaviour)
    {
        if (behaviour != null && !_resolvedBlockedBehaviours.Contains(behaviour))
        {
            _resolvedBlockedBehaviours.Add(behaviour);
        }
    }

    private static RectTransform CreateUiObject(string objectName, Transform parent)
    {
        GameObject gameObject = new GameObject(objectName, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        return gameObject.GetComponent<RectTransform>();
    }

    private Text CreateStatText(string objectName, Transform parent, Font font)
    {
        Text text = CreateText(objectName, parent, font, 22, FontStyle.Normal);
        text.alignment = TextAnchor.MiddleLeft;
        text.color = textColor;
        return text;
    }

    private static Text CreateText(string objectName, Transform parent, Font font, int fontSize, FontStyle fontStyle)
    {
        RectTransform rect = CreateUiObject(objectName, parent);
        LayoutElement layout = rect.gameObject.AddComponent<LayoutElement>();
        layout.preferredHeight = fontSize + 12f;

        Text text = rect.gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.color = new Color(1f, 0.96f, 0.85f, 1f);
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.alignment = TextAnchor.MiddleLeft;
        return text;
    }

    private Button CreateButton(string objectName, string label, Transform parent, Font font, UnityEngine.Events.UnityAction onClick)
    {
        RectTransform rect = CreateUiObject(objectName, parent);
        Image background = rect.gameObject.AddComponent<Image>();
        background.color = accentColor;

        Button button = rect.gameObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = accentColor;
        colors.highlightedColor = Color.Lerp(accentColor, Color.white, 0.2f);
        colors.pressedColor = Color.Lerp(accentColor, Color.black, 0.15f);
        colors.disabledColor = Color.Lerp(accentColor, Color.black, 0.55f);
        button.colors = colors;
        button.targetGraphic = background;
        button.onClick.AddListener(onClick);

        Text buttonText = CreateText("Label", rect, font, 20, FontStyle.Bold);
        buttonText.text = label;
        buttonText.alignment = TextAnchor.MiddleCenter;
        buttonText.color = new Color(0.16f, 0.12f, 0.02f, 1f);
        return button;
    }

    private static Sprite GetStarSprite()
    {
        if (_starSprite != null)
        {
            return _starSprite;
        }

        const int size = 4;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "LevelCompleteSquare"
        };

        Color[] pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = Color.white;
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);

        _starSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        _starSprite.name = "LevelCompleteSquare";
        return _starSprite;
    }

    private static Font LoadBuiltinFont()
    {
        Font font = TryLoadBuiltinFont("LegacyRuntime.ttf");
        if (font != null)
        {
            return font;
        }

        font = TryLoadBuiltinFont("Arial.ttf");
        if (font != null)
        {
            return font;
        }

        Font osFont = Font.CreateDynamicFontFromOSFont(new[] { "Arial", "Helvetica", "Verdana" }, 16);
        if (osFont != null)
        {
            return osFont;
        }

        throw new InvalidOperationException("Failed to load a usable UI font for LevelCompleteUI.");
    }

    private static Font TryLoadBuiltinFont(string resourceName)
    {
        try
        {
            return Resources.GetBuiltinResource<Font>(resourceName);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

}
