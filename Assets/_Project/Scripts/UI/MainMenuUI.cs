using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class MainMenuUI : MonoBehaviour
{
    [Header("Visuals")]
    [SerializeField] private Color overlayColor = new Color(0.03f, 0.03f, 0.04f, 0.9f);
    [SerializeField] private Color panelColor = new Color(0.13f, 0.11f, 0.08f, 0.96f);
    [SerializeField] private Color accentColor = new Color(0.95f, 0.8f, 0.28f, 1f);
    [SerializeField] private Color titleColor = new Color(1f, 0.97f, 0.88f, 1f);
    [SerializeField] private Color subtitleColor = new Color(0.87f, 0.84f, 0.74f, 1f);
    [SerializeField] private Color buttonTextColor = new Color(0.18f, 0.12f, 0.03f, 1f);

    [Header("References")]
    [SerializeField] private Canvas rootCanvas;
    [SerializeField] private RectTransform overlayRoot;
    [SerializeField] private RectTransform buttonContainer;
    [SerializeField] private Text titleText;
    [SerializeField] private Text subtitleText;
    [SerializeField] private Behaviour[] blockedBehaviours;

    private readonly List<Behaviour> _resolvedBlockedBehaviours = new();
    private readonly Dictionary<Behaviour, bool> _cachedEnabledStates = new();
    private readonly List<Button> _levelButtons = new();
    private bool _isVisible;

    public event Action<LevelConfig> LevelSelected;

    private void Awake()
    {
        EnsureUi();
        HideImmediate();
    }

    public void Show(IReadOnlyList<LevelConfig> levels)
    {
        EnsureUi();
        RebuildButtons(levels);
        overlayRoot.gameObject.SetActive(true);
        SetControlsBlocked(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Time.timeScale = 0f;
        _isVisible = true;
    }

    public void HideImmediate()
    {
        EnsureUi();
        overlayRoot.gameObject.SetActive(false);
        SetControlsBlocked(false);

        if (_isVisible)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        Time.timeScale = 1f;
        _isVisible = false;
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
            rootCanvas.sortingOrder = 100;
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

        RectTransform panel = CreateUiObject("MenuPanel", overlayRoot);
        panel.anchorMin = new Vector2(0.5f, 0.5f);
        panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(520f, 420f);

        Image panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.color = panelColor;

        VerticalLayoutGroup panelLayout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(32, 32, 32, 32);
        panelLayout.spacing = 18f;
        panelLayout.childAlignment = TextAnchor.UpperCenter;
        panelLayout.childControlHeight = true;
        panelLayout.childControlWidth = true;
        panelLayout.childForceExpandHeight = false;
        panelLayout.childForceExpandWidth = true;

        titleText = CreateText("Title", panel, font, 34, FontStyle.Bold);
        titleText.text = "Главное меню";
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.color = titleColor;

        subtitleText = CreateText("Subtitle", panel, font, 22, FontStyle.Normal);
        subtitleText.text = "Выберите уровень сложности";
        subtitleText.alignment = TextAnchor.MiddleCenter;
        subtitleText.color = subtitleColor;

        buttonContainer = CreateUiObject("Buttons", panel);
        VerticalLayoutGroup buttonLayout = buttonContainer.gameObject.AddComponent<VerticalLayoutGroup>();
        buttonLayout.spacing = 14f;
        buttonLayout.childAlignment = TextAnchor.UpperCenter;
        buttonLayout.childControlWidth = true;
        buttonLayout.childControlHeight = true;
        buttonLayout.childForceExpandWidth = true;
        buttonLayout.childForceExpandHeight = false;

        LayoutElement buttonsLayout = buttonContainer.gameObject.AddComponent<LayoutElement>();
        buttonsLayout.preferredHeight = 220f;
    }

    private void RebuildButtons(IReadOnlyList<LevelConfig> levels)
    {
        if (buttonContainer == null)
        {
            return;
        }

        for (int i = buttonContainer.childCount - 1; i >= 0; i--)
        {
            Destroy(buttonContainer.GetChild(i).gameObject);
        }

        _levelButtons.Clear();

        if (levels == null || levels.Count == 0)
        {
            return;
        }

        Font font = LoadBuiltinFont();
        for (int i = 0; i < levels.Count; i++)
        {
            LevelConfig levelConfig = levels[i];
            if (levelConfig == null)
            {
                continue;
            }

            string buttonLabel = string.IsNullOrWhiteSpace(levelConfig.levelName)
                ? $"Level {i + 1}"
                : levelConfig.levelName;

            Button button = CreateButton(buttonLabel, buttonContainer, font, () => HandleLevelSelected(levelConfig));
            _levelButtons.Add(button);
        }
    }

    private void HandleLevelSelected(LevelConfig selectedLevel)
    {
        HideImmediate();
        LevelSelected?.Invoke(selectedLevel);
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
        AddIfFound(FindFirstObjectByType<CameraController>());
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

    private Text CreateText(string objectName, Transform parent, Font font, int fontSize, FontStyle fontStyle)
    {
        RectTransform rect = CreateUiObject(objectName, parent);
        LayoutElement layout = rect.gameObject.AddComponent<LayoutElement>();
        layout.preferredHeight = fontSize + 18f;

        Text text = rect.gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.color = titleColor;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    private Button CreateButton(string label, Transform parent, Font font, UnityEngine.Events.UnityAction onClick)
    {
        RectTransform rect = CreateUiObject($"{label}Button", parent);
        LayoutElement layout = rect.gameObject.AddComponent<LayoutElement>();
        layout.preferredHeight = 58f;

        Image background = rect.gameObject.AddComponent<Image>();
        background.color = accentColor;

        Button button = rect.gameObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = accentColor;
        colors.highlightedColor = Color.Lerp(accentColor, Color.white, 0.18f);
        colors.pressedColor = Color.Lerp(accentColor, Color.black, 0.14f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = Color.Lerp(accentColor, Color.black, 0.55f);
        button.colors = colors;
        button.targetGraphic = background;
        button.onClick.AddListener(onClick);

        Text buttonText = CreateText("Label", rect, font, 24, FontStyle.Bold);
        buttonText.text = label;
        buttonText.color = buttonTextColor;
        buttonText.alignment = TextAnchor.MiddleCenter;

        return button;
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

        throw new InvalidOperationException("Failed to load a usable UI font for MainMenuUI.");
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
