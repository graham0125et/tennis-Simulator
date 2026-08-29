using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DefaultExecutionOrder(120)]
public class AIModeToggleUI : MonoBehaviour
{
    [Header("Layout")]
    public Vector2 anchoredPosition = new Vector2(18f, -54f);
    public Vector2 size = new Vector2(168f, 34f);
    public float fontSize = 11f;

    [Header("Colours")]
    public Color panelColour = new Color(0.02f, 0.025f, 0.03f, 0.64f);
    public Color inactiveColour = new Color(0.12f, 0.14f, 0.16f, 0.72f);
    public Color practiceActiveColour = new Color(0.15f, 0.9f, 0.95f, 0.92f);
    public Color matchplayActiveColour = new Color(0.35f, 1f, 0.38f, 0.92f);
    public Color textColour = new Color(0.92f, 0.97f, 1f, 1f);
    public Color activeTextColour = new Color(0.02f, 0.035f, 0.04f, 1f);

    private Canvas canvas;
    private RectTransform root;
    private Image practiceImage;
    private Image matchplayImage;
    private TextMeshProUGUI practiceText;
    private TextMeshProUGUI matchplayText;
    private TennisAIPlayerController.AIDecisionMode currentMode = TennisAIPlayerController.AIDecisionMode.Practice;
    private float nextSyncTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindFirstObjectByType<AIModeToggleUI>() != null)
            return;

        GameObject uiObject = new GameObject("AI Mode Toggle UI", typeof(AIModeToggleUI));
        DontDestroyOnLoad(uiObject);
    }

    private void Awake()
    {
        EnsureCanvas();
        EnsureEventSystem();
        BuildUI();
        SyncFromControllers();
        RefreshVisuals();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextSyncTime)
            return;

        nextSyncTime = Time.unscaledTime + 0.5f;
        SyncFromControllers();
        RefreshVisuals();
    }

    private void EnsureCanvas()
    {
        canvas = FindFirstObjectByType<Canvas>();
        if (canvas != null)
            return;

        GameObject canvasObject = new GameObject("Runtime UI Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        DontDestroyOnLoad(canvasObject);
    }

    private static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null)
            return;

        GameObject eventObject = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        DontDestroyOnLoad(eventObject);
    }

    private void BuildUI()
    {
        GameObject rootObject = new GameObject("AI Mode Toggle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        rootObject.transform.SetParent(canvas.transform, false);
        root = rootObject.GetComponent<RectTransform>();
        root.anchorMin = new Vector2(0f, 1f);
        root.anchorMax = new Vector2(0f, 1f);
        root.pivot = new Vector2(0f, 1f);
        root.anchoredPosition = anchoredPosition;
        root.sizeDelta = size;

        Image panel = rootObject.GetComponent<Image>();
        panel.color = panelColour;

        Button practiceButton = CreateButton("Practice", new Vector2(3f, -3f), new Vector2(size.x * 0.5f - 4.5f, size.y - 6f), out practiceImage, out practiceText);
        Button matchplayButton = CreateButton("Match", new Vector2(size.x * 0.5f + 1.5f, -3f), new Vector2(size.x * 0.5f - 4.5f, size.y - 6f), out matchplayImage, out matchplayText);
        practiceButton.onClick.AddListener(() => SetMode(TennisAIPlayerController.AIDecisionMode.Practice));
        matchplayButton.onClick.AddListener(() => SetMode(TennisAIPlayerController.AIDecisionMode.Matchplay));
    }

    private Button CreateButton(string label, Vector2 anchoredPosition, Vector2 buttonSize, out Image image, out TextMeshProUGUI text)
    {
        GameObject buttonObject = new GameObject(label + " AI Mode Button", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(root, false);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = buttonSize;

        image = buttonObject.GetComponent<Image>();
        image.color = inactiveColour;

        Button button = buttonObject.GetComponent<Button>();
        ColorBlock colours = button.colors;
        colours.normalColor = Color.white;
        colours.highlightedColor = new Color(1f, 1f, 1f, 0.88f);
        colours.pressedColor = new Color(0.78f, 0.9f, 1f, 0.9f);
        colours.selectedColor = Color.white;
        button.colors = colours;

        GameObject textObject = new GameObject(label + " Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(buttonObject.transform, false);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        text = textObject.GetComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.color = textColour;

        return button;
    }

    private void SetMode(TennisAIPlayerController.AIDecisionMode mode)
    {
        TennisAIPlayerController[] controllers = FindObjectsByType<TennisAIPlayerController>(FindObjectsSortMode.None);
        for (int i = 0; i < controllers.Length; i++)
        {
            if (controllers[i] != null)
                controllers[i].SetDecisionMode(mode);
        }

        currentMode = mode;
        RefreshVisuals();
    }

    private void SyncFromControllers()
    {
        TennisAIPlayerController[] controllers = FindObjectsByType<TennisAIPlayerController>(FindObjectsSortMode.None);
        for (int i = 0; i < controllers.Length; i++)
        {
            if (controllers[i] == null)
                continue;

            currentMode = controllers[i].decisionMode;
            return;
        }
    }

    private void RefreshVisuals()
    {
        bool matchplay = currentMode == TennisAIPlayerController.AIDecisionMode.Matchplay;
        if (practiceImage != null)
            practiceImage.color = matchplay ? inactiveColour : practiceActiveColour;
        if (matchplayImage != null)
            matchplayImage.color = matchplay ? matchplayActiveColour : inactiveColour;
        if (practiceText != null)
            practiceText.color = matchplay ? textColour : activeTextColour;
        if (matchplayText != null)
            matchplayText.color = matchplay ? activeTextColour : textColour;
    }
}
