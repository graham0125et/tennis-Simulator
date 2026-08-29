using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class SpinDebugVisualizer : MonoBehaviour
{
    public const string SharedPanelName = "SpinStatsPanel";
    private const string SpinTextName = "SpinStatsSpinText";
    private const string RpmTextName = "SpinStatsRpmText";
    private const string RatioTextName = "SpinStatsRatioText";
    private const string AxisTextName = "SpinStatsAxisText";

    [Header("Source")]
    public BallController ballController;

    [Header("Ring")]
    public bool showRing = true;
    public float ringRadius = 0.12f;
    public float ringWidth = 0.012f;
    public int ringSegments = 72;
    public Color topspinColor = new Color(1f, 0.12f, 0.08f, 1f);
    public Color backspinColor = new Color(0.15f, 0.48f, 1f, 1f);
    public Color neutralColor = Color.white;

    [Header("Scale")]
    public float neutralThresholdRpm = 80f;
    public float fullScaleRpm = 2500f;
    public float ballRadiusMetres = 0.033f;

    [Header("Overlay")]
    public bool showOverlay = true;
    public bool overlayOnRight = true;
    public bool overlayOnBottom = true;
    public Vector2 overlayOffset = new Vector2(20f, 160f);
    public KeyCode toggleKey = KeyCode.None;

    [Header("Canvas Overlay")]
    public bool useCanvasOverlay = true;
    public bool showCanvasOverlayInEditMode = true;
    public Canvas overlayCanvas;
    public RectTransform overlayPanel;
    public Vector2 canvasOverlayDefaultOffset = new Vector2(-24f, 120f);
    public Vector2 canvasOverlaySize = new Vector2(230f, 104f);
    public TextMeshProUGUI canvasSpinText;
    public TextMeshProUGUI canvasRpmText;
    public TextMeshProUGUI canvasRatioText;
    public TextMeshProUGUI canvasAxisText;

    private Rigidbody rb;
    private LineRenderer ring;
    private Material ringMaterial;
    private Camera cachedCamera;
    private GUIStyle labelStyle;
    private GUIStyle boxStyle;
    private bool visible = true;

    public void Configure(
        BallController source,
        bool sourceShowOverlay,
        float sourceFullScaleRpm,
        float sourceRingRadius,
        float sourceRingWidth)
    {
        ballController = source;
        showOverlay = sourceShowOverlay;
        fullScaleRpm = Mathf.Max(1f, sourceFullScaleRpm);
        ringRadius = Mathf.Max(0.01f, sourceRingRadius);
        ringWidth = Mathf.Max(0.001f, sourceRingWidth);

        rb = GetComponent<Rigidbody>();
        EnsureRing();
        EnsureCanvasOverlay();
    }

    public void SetVisible(bool value)
    {
        visible = value;
        if (ring != null)
            ring.enabled = value && showRing;
    }

    void Awake()
    {
        if (ballController == null)
            ballController = GetComponent<BallController>();

        rb = GetComponent<Rigidbody>();
        EnsureRing();
        EnsureCanvasOverlay();
    }

    void OnEnable()
    {
        SetVisible(visible);
        EnsureCanvasOverlay();
        UpdateCanvasOverlay();
    }

    void OnDisable()
    {
        if (ring != null)
            ring.enabled = false;
    }

    void OnDestroy()
    {
        if (ringMaterial != null)
            Destroy(ringMaterial);
    }

    void Update()
    {
        if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey))
            SetVisible(!visible);

        EnsureCanvasOverlay();
        UpdateCanvasOverlay();
    }

    void LateUpdate()
    {
        if (!visible || !showRing)
        {
            if (ring != null)
                ring.enabled = false;
            return;
        }

        if (ballController == null)
            ballController = GetComponent<BallController>();

        if (rb == null)
            rb = GetComponent<Rigidbody>();

        EnsureRing();
        UpdateRing();
    }

    void OnGUI()
    {
        if (useCanvasOverlay && overlayPanel != null)
            return;

        if (!visible || !showOverlay || ballController == null)
            return;

        EnsureGuiStyles();

        SpinDebugState state = GetSpinState();
        float x = overlayOnRight ? Screen.width - overlayOffset.x - 230f : overlayOffset.x;
        float y = overlayOnBottom ? Screen.height - overlayOffset.y - 104f : overlayOffset.y;
        Rect rect = new Rect(x, y, 230f, 104f);

        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.Box(rect, GUIContent.none, boxStyle);

        GUI.color = state.color;
        GUI.Label(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, 22f), $"Spin: {state.typeName}", labelStyle);
        GUI.color = Color.white;
        GUI.Label(new Rect(rect.x + 10f, rect.y + 32f, rect.width - 20f, 22f), $"RPM: {state.signedRpm:F0}   rad/s: {state.signedRadPerSecond:F1}", labelStyle);
        GUI.Label(new Rect(rect.x + 10f, rect.y + 56f, rect.width - 20f, 22f), $"ratio: {state.spinRatio:F2}   intensity: {state.intensity:F2}", labelStyle);
        GUI.Label(new Rect(rect.x + 10f, rect.y + 80f, rect.width - 20f, 22f), $"axis: {state.axis.x:F2}, {state.axis.y:F2}, {state.axis.z:F2}", labelStyle);
        GUI.color = Color.white;
    }

    private void EnsureRing()
    {
        if (ring != null)
            return;

        GameObject ringObject = new GameObject("Spin Debug Ring");
        ringObject.transform.SetParent(transform, false);
        ring = ringObject.AddComponent<LineRenderer>();
        ring.useWorldSpace = true;
        ring.loop = true;
        ring.positionCount = Mathf.Max(12, ringSegments);
        ring.widthMultiplier = ringWidth;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.receiveShadows = false;
        ring.enabled = visible && showRing;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        if (shader != null)
        {
            ringMaterial = new Material(shader);
            ring.material = ringMaterial;
        }
    }

    private void EnsureCanvasOverlay()
    {
        if (!useCanvasOverlay)
            return;

        if (overlayCanvas == null)
            overlayCanvas = FindFirstObjectByType<Canvas>();

        if (overlayCanvas == null)
            return;

        if (overlayPanel == null)
            overlayPanel = EnsureSharedCanvasPanel(overlayCanvas, canvasOverlayDefaultOffset, canvasOverlaySize);

        BindCanvasOverlayTexts();
    }

    public static RectTransform EnsureSharedCanvasPanel(Canvas canvas, Vector2 defaultOffset, Vector2 size)
    {
        if (canvas == null)
            return null;

        Transform existing = canvas.transform.Find(SharedPanelName);
        RectTransform rect = existing != null
            ? existing as RectTransform
            : null;

        if (rect == null)
        {
            GameObject panelObject = new GameObject(SharedPanelName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panelObject.transform.SetParent(canvas.transform, false);
            panelObject.layer = canvas.gameObject.layer;

            rect = panelObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = defaultOffset;

            Image image = panelObject.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.50f);
            image.raycastTarget = false;
        }

        rect.sizeDelta = new Vector2(Mathf.Max(180f, size.x), Mathf.Max(84f, size.y));
        EnsureCanvasText(rect, SpinTextName, "Spin: Neutral", new Vector2(10f, -8f), new Vector2(-20f, 22f), new Color(1f, 0.12f, 0.08f, 1f));
        EnsureCanvasText(rect, RpmTextName, "RPM: 0   rad/s: 0.0", new Vector2(10f, -32f), new Vector2(-20f, 22f), Color.white);
        EnsureCanvasText(rect, RatioTextName, "ratio: 0.00   intensity: 0.00", new Vector2(10f, -56f), new Vector2(-20f, 22f), Color.white);
        EnsureCanvasText(rect, AxisTextName, "axis: 0.00, 0.00, 0.00", new Vector2(10f, -80f), new Vector2(-20f, 22f), Color.white);
        return rect;
    }

    private static TextMeshProUGUI EnsureCanvasText(RectTransform parent, string name, string text, Vector2 anchoredPosition, Vector2 sizeDelta, Color color)
    {
        Transform existing = parent.Find(name);
        GameObject textObject = existing != null
            ? existing.gameObject
            : new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));

        textObject.transform.SetParent(parent, false);
        textObject.layer = parent.gameObject.layer;

        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;

        TextMeshProUGUI label = textObject.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.color = color;
        label.fontSize = 13f;
        label.alignment = TextAlignmentOptions.Left;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        return label;
    }

    private void BindCanvasOverlayTexts()
    {
        if (overlayPanel == null)
            return;

        canvasSpinText = GetPanelText(SpinTextName);
        canvasRpmText = GetPanelText(RpmTextName);
        canvasRatioText = GetPanelText(RatioTextName);
        canvasAxisText = GetPanelText(AxisTextName);
    }

    private TextMeshProUGUI GetPanelText(string childName)
    {
        Transform child = overlayPanel.Find(childName);
        return child != null ? child.GetComponent<TextMeshProUGUI>() : null;
    }

    private void UpdateCanvasOverlay()
    {
        if (!useCanvasOverlay || overlayPanel == null)
            return;

        bool shouldShow = visible && showOverlay && (Application.isPlaying || showCanvasOverlayInEditMode);
        if (overlayPanel.gameObject.activeSelf != shouldShow)
            overlayPanel.gameObject.SetActive(shouldShow);

        if (!shouldShow)
            return;

        SpinDebugState state = GetSpinState();
        if (canvasSpinText != null)
        {
            canvasSpinText.text = $"Spin: {state.typeName}";
            canvasSpinText.color = state.color;
        }

        if (canvasRpmText != null)
            canvasRpmText.text = $"RPM: {state.signedRpm:F0}   rad/s: {state.signedRadPerSecond:F1}";

        if (canvasRatioText != null)
            canvasRatioText.text = $"ratio: {state.spinRatio:F2}   intensity: {state.intensity:F2}";

        if (canvasAxisText != null)
            canvasAxisText.text = $"axis: {state.axis.x:F2}, {state.axis.y:F2}, {state.axis.z:F2}";
    }

    private void UpdateRing()
    {
        SpinDebugState state = GetSpinState();
        int segments = Mathf.Max(12, ringSegments);
        if (ring.positionCount != segments)
            ring.positionCount = segments;

        ring.widthMultiplier = Mathf.Lerp(ringWidth * 0.75f, ringWidth * 1.6f, state.intensity);
        ring.startColor = state.color;
        ring.endColor = state.color;

        if (ringMaterial != null)
            ringMaterial.color = state.color;

        Camera cam = GetCamera();
        Vector3 right = cam != null ? cam.transform.right : Vector3.right;
        Vector3 up = cam != null ? cam.transform.up : Vector3.up;
        Vector3 center = transform.position;
        float radius = ringRadius * Mathf.Lerp(0.9f, 1.25f, state.intensity);

        for (int i = 0; i < segments; i++)
        {
            float angle = (Mathf.PI * 2f * i) / segments;
            Vector3 point = center + right * Mathf.Cos(angle) * radius + up * Mathf.Sin(angle) * radius;
            ring.SetPosition(i, point);
        }
    }

    private Camera GetCamera()
    {
        if (cachedCamera == null)
            cachedCamera = Camera.main;

        return cachedCamera;
    }

    private SpinDebugState GetSpinState()
    {
        SpinDebugState state = new SpinDebugState();

        if (ballController == null)
        {
            state.color = neutralColor;
            state.typeName = "No Ball";
            return state;
        }

        float signedRpm = ballController.spinRpm;
        float signedRad = BaseShotLibrary.RpmToRadPerSecond(signedRpm);
        float absRpm = Mathf.Abs(signedRpm);
        float intensity = Mathf.InverseLerp(neutralThresholdRpm, Mathf.Max(neutralThresholdRpm + 1f, fullScaleRpm), absRpm);

        Color targetColor = neutralColor;
        string typeName = "Neutral";

        if (absRpm >= neutralThresholdRpm)
        {
            if (signedRpm > 0f)
            {
                targetColor = topspinColor;
                typeName = "Topspin";
            }
            else
            {
                targetColor = backspinColor;
                typeName = "Backspin/Slice";
            }
        }

        Color color = Color.Lerp(neutralColor, targetColor, intensity);
        color.a = Mathf.Lerp(0.45f, 1f, intensity);

        float speed = rb != null ? rb.linearVelocity.magnitude : 0f;
        float spinRatio = speed > 0.01f
            ? ballRadiusMetres * ballController.spinMagnitudeRadPerSecond / speed
            : 0f;

        state.signedRpm = signedRpm;
        state.signedRadPerSecond = signedRad;
        state.absRadPerSecond = ballController.spinMagnitudeRadPerSecond;
        state.axis = ballController.spinRadPerSecond.sqrMagnitude > 0.0001f
            ? ballController.spinRadPerSecond.normalized
            : Vector3.zero;
        state.spinRatio = spinRatio;
        state.intensity = Mathf.Clamp01(intensity);
        state.color = color;
        state.typeName = typeName;

        return state;
    }

    private void EnsureGuiStyles()
    {
        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 13;
            labelStyle.normal.textColor = Color.white;
        }

        if (boxStyle == null)
            boxStyle = new GUIStyle(GUI.skin.box);
    }

    private struct SpinDebugState
    {
        public float signedRpm;
        public float signedRadPerSecond;
        public float absRadPerSecond;
        public float spinRatio;
        public float intensity;
        public Vector3 axis;
        public Color color;
        public string typeName;
    }
}
