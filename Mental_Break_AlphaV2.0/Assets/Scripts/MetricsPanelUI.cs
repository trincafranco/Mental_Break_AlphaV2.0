using UnityEngine;
using UnityEngine.UI;
using Yarn.Unity;

#if USE_TMP
using TMPro;
#endif

/// <summary>
/// Displays Engagement, Sanity, and Suspicion percentages on the right side of the screen.
/// Engagement is displayed in fuchsia, Sanity in cyan, Suspicion in orange.
/// Updates in real-time from Yarn variables.
/// Suspicion panel only appears when $suspicion_hud_active is true (granted by Noam in Day 2).
/// </summary>
public class MetricsPanelUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The DialogueRunner to read variables from")]
    public DialogueRunner dialogueRunner;

    [Header("Settings")]
    [Tooltip("Update interval in seconds")]
    public float updateInterval = 0.1f;

    [Tooltip("Minimum font size for metric text")]
    public int minFontSize = 26;

    [Tooltip("Font size for metric text (clamped by Min Font Size)")]
    public int fontSize = 32;

    [Tooltip("Spacing between metrics")]
    public float metricSpacing = 48f;

    [Tooltip("Preferred height for each metric panel")]
    public float panelPreferredHeight = 68f;

    [Tooltip("Minimum width for each metric panel")]
    public float panelMinWidth = 280f;

    [Tooltip("Maximum width for each metric panel")]
    public float panelMaxWidth = 420f;

    [Tooltip("Fraction of the safe-area width used by the metric panels")]
    [Range(0.15f, 0.5f)]
    public float widthFraction = 0.28f;

    [Tooltip("Distance from the safe area's right edge")]
    public float rightMargin = 32f;

    [Tooltip("Distance from the safe area's top edge")]
    public float topMargin = 36f;

    [Tooltip("Padding inside each metric panel (x = left/right, y = top/bottom)")]
    public Vector2 panelPadding = new Vector2(28f, 20f);

    [Tooltip("Background color used for metric panels")]
    public Color panelBackgroundColor = new Color(0f, 0f, 0f, 1f);

    // Colors
    private readonly Color engagementColor = new Color(1f, 0f, 1f, 1f); // Fuchsia #FF00FF
    private readonly Color sanityColor = new Color(0f, 1f, 1f, 1f); // Cyan #00FFFF
    private readonly Color suspicionColorLow = new Color(0.3f, 0.9f, 0.4f, 1f); // Green-ish for low suspicion
    private readonly Color suspicionColorMid = new Color(1f, 0.6f, 0f, 1f); // Orange for medium suspicion
    private readonly Color suspicionColorHigh = new Color(1f, 0.2f, 0.2f, 1f); // Red for high suspicion

    private VariableStorageBehaviour variableStorage;
    private DialogueRuntimeWatcher runtimeWatcher;
    private float lastUpdateTime = 0f;

    // Layout references
    private GameObject metricsRoot;
    private VerticalLayoutGroup rootLayoutGroup;
    private ContentSizeFitter rootFitter;
    private Vector2 lastScreenSize = Vector2.zero;
    private Rect lastSafeArea = new Rect();

    // UI References
    private GameObject engagementPanel;
    private GameObject sanityPanel;
    private GameObject suspicionPanel;
    private Canvas canvas;

#if USE_TMP
    private TextMeshProUGUI engagementText;
    private TextMeshProUGUI sanityText;
    private TextMeshProUGUI suspicionText;
#else
    private Text engagementText;
    private Text sanityText;
    private Text suspicionText;
#endif

    private void OnEnable()
    {
        EnsureUIReady();

        runtimeWatcher = DialogueRuntimeWatcher.Instance;
        runtimeWatcher.Register(OnRuntimeReady, OnRuntimeLost);

        if (!runtimeWatcher.HasRuntime)
        {
            ApplyLoadingState();
        }
    }

    private void OnDisable()
    {
        if (runtimeWatcher != null)
        {
            runtimeWatcher.Unregister(OnRuntimeReady, OnRuntimeLost);
            runtimeWatcher = null;
        }
    }

    private void Start()
    {
        // Find DialogueRunner if not assigned
        if (dialogueRunner == null)
        {
            dialogueRunner = FindAnyObjectByType<DialogueRunner>();
        }

        if (dialogueRunner != null)
        {
            variableStorage = dialogueRunner.VariableStorage;
        }

        // Create UI elements if they don't already exist
        if (metricsRoot == null)
        {
            CreateUI();
        }
        else
        {
            EnsureContainerLayout();
        }

        if (variableStorage == null)
        {
            ApplyLoadingState();
        }
        else
        {
            UpdateMetrics();
        }
    }

    private void OnRuntimeReady(DialogueRunner runner, VariableStorageBehaviour storage)
    {
        if (runner != null)
        {
            dialogueRunner = runner;
        }

        variableStorage = storage;
        UpdateMetrics();
    }

    private void OnRuntimeLost()
    {
        variableStorage = null;
        ApplyLoadingState();
    }

    private void Update()
    {
        EnsureContainerLayout();

        // Update metrics at intervals
        if (Time.time - lastUpdateTime >= updateInterval)
        {
            UpdateMetrics();
            lastUpdateTime = Time.time;
        }
    }

    /// <summary>
    /// Create the UI elements programmatically
    /// </summary>
    private void CreateUI()
    {
        // Find or create Canvas (check for existing one first to share with other UI scripts)
        canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasObj = new GameObject("MetricsCanvas");
            canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler canvasScaler = canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();
            ConfigureCanvasScaler(canvasScaler);
        }
        else
        {
            // Use existing canvas, ensure it has required components
            CanvasScaler canvasScaler = canvas.GetComponent<CanvasScaler>();
            if (canvasScaler == null)
            {
                canvasScaler = canvas.gameObject.AddComponent<CanvasScaler>();
            }
            ConfigureCanvasScaler(canvasScaler);
            if (canvas.GetComponent<GraphicRaycaster>() == null)
            {
                canvas.gameObject.AddComponent<GraphicRaycaster>();
            }
        }

        metricsRoot = new GameObject("MetricsPanelRoot");
        metricsRoot.transform.SetParent(canvas.transform, false);

        RectTransform rootRect = metricsRoot.AddComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(1f, 1f);
        rootRect.anchorMax = new Vector2(1f, 1f);
        rootRect.pivot = new Vector2(1f, 1f);
        rootRect.anchoredPosition = Vector2.zero;

        rootLayoutGroup = metricsRoot.AddComponent<VerticalLayoutGroup>();
        rootLayoutGroup.childControlWidth = true;
        rootLayoutGroup.childControlHeight = true;
        rootLayoutGroup.childForceExpandWidth = true;
        rootLayoutGroup.childForceExpandHeight = false;
        rootLayoutGroup.childAlignment = TextAnchor.UpperRight;
        rootLayoutGroup.spacing = metricSpacing;

        rootFitter = metricsRoot.AddComponent<ContentSizeFitter>();
        rootFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        rootFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        // Create Engagement panel
        engagementPanel = CreateMetricPanel("EngagementPanel");
        engagementText = CreateMetricText(engagementPanel, "Engagement", engagementColor);

        // Create Sanity panel
        sanityPanel = CreateMetricPanel("SanityPanel");
        sanityText = CreateMetricText(sanityPanel, "Sanity", sanityColor);

        // Create Suspicion panel (hidden by default until Noam grants the HUD)
        suspicionPanel = CreateMetricPanel("SuspicionPanel");
        suspicionText = CreateMetricText(suspicionPanel, "Suspicion", suspicionColorLow);
        suspicionPanel.SetActive(false); // Hidden until $suspicion_hud_active is true

        EnsureContainerLayout();
    }

    private void EnsureUIReady()
    {
        if (metricsRoot == null)
        {
            CreateUI();
        }
    }

    /// <summary>
    /// Create a metric panel
    /// </summary>
    private GameObject CreateMetricPanel(string name)
    {
        GameObject panel = new GameObject(name);
        panel.transform.SetParent(metricsRoot.transform, false);

        RectTransform rect = panel.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        float panelHeight = GetPanelHeight();
        rect.sizeDelta = new Vector2(0f, panelHeight);

        LayoutElement layoutElement = panel.AddComponent<LayoutElement>();
        layoutElement.preferredHeight = panelHeight;
        layoutElement.minHeight = panelHeight;
        layoutElement.flexibleHeight = 0f;
        layoutElement.flexibleWidth = 1f;

        // Add padding to the panel
        HorizontalLayoutGroup panelLayout = panel.AddComponent<HorizontalLayoutGroup>();
        panelLayout.padding.left = Mathf.RoundToInt(panelPadding.x);
        panelLayout.padding.right = Mathf.RoundToInt(panelPadding.x);
        panelLayout.padding.top = Mathf.RoundToInt(panelPadding.y);
        panelLayout.padding.bottom = Mathf.RoundToInt(panelPadding.y);
        panelLayout.childControlWidth = true;
        panelLayout.childControlHeight = true;
        panelLayout.childForceExpandWidth = true;
        panelLayout.childForceExpandHeight = true;
        panelLayout.childAlignment = TextAnchor.MiddleRight;

        // Add opaque background
        Image bgImage = panel.AddComponent<Image>();
        bgImage.color = panelBackgroundColor;
        bgImage.raycastTarget = false;

        return panel;
    }

    /// <summary>
    /// Create text component for a metric
    /// </summary>
#if USE_TMP
    private TextMeshProUGUI CreateMetricText(GameObject panel, string label, Color color)
#else
    private Text CreateMetricText(GameObject panel, string label, Color color)
#endif
    {
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(panel.transform, false);

        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        textRect.anchoredPosition = Vector2.zero;

#if USE_TMP
        TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
        // Load default font if available
        if (TMPro.TMP_Settings.instance != null && TMPro.TMP_Settings.instance.defaultFontAsset != null)
        {
            text.font = TMPro.TMP_Settings.instance.defaultFontAsset;
        }
        text.text = $"{label}: 0%";
        text.fontSize = GetFontSize();
        text.alignment = TextAlignmentOptions.MidlineRight;
        text.color = color;
#else
        Text text = textObj.AddComponent<Text>();
        text.text = $"{label}: 0%";
        text.fontSize = GetFontSize();
        text.alignment = TextAnchor.MiddleRight;
        text.color = color;
        // Use default Unity font
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#endif

        return text;
    }

    /// <summary>
    /// Update the metric displays with current values
    /// </summary>
    private void UpdateMetrics()
    {
        if (variableStorage == null)
        {
            ApplyLoadingState();
            return;
        }

        // Get Engagement value
        float engagement = 0f;
        if (variableStorage.TryGetValue<float>("$engagement", out var engagementValue))
        {
            engagement = engagementValue;
        }

        // Get Sanity value
        float sanity = 0f;
        if (variableStorage.TryGetValue<float>("$sanity", out var sanityValue))
        {
            sanity = sanityValue;
        }

        // Update text
        if (engagementText != null)
        {
            engagementText.text = $"Engagement: {engagement:F0}%";
        }

        if (sanityText != null)
        {
            sanityText.text = $"Sanity: {sanity:F0}%";
        }

        // Update Suspicion panel visibility and value
        UpdateSuspicionPanel();
    }

    /// <summary>
    /// Update the suspicion panel based on $suspicion_hud_active and $alert_level
    /// </summary>
    private void UpdateSuspicionPanel()
    {
        if (suspicionPanel == null || variableStorage == null)
        {
            return;
        }

        // Check if the suspicion HUD should be visible
        bool showSuspicion = false;
        if (variableStorage.TryGetValue<bool>("$suspicion_hud_active", out var hudActive))
        {
            showSuspicion = hudActive;
        }

        suspicionPanel.SetActive(showSuspicion);

        if (showSuspicion && suspicionText != null)
        {
            // Get the alert level (suspicion score)
            float alertLevel = 0f;
            if (variableStorage.TryGetValue<float>("$alert_level", out var alertValue))
            {
                alertLevel = alertValue;
            }

            // Update the text
            suspicionText.text = $"Suspicion: {alertLevel:F0}";

            // Update color based on threat level
            // 0-25: Clear (green), 26-50: Flagged (yellow-green), 51-75: Watched (orange), 76+: Critical (red)
            Color suspicionColor;
            if (alertLevel >= 76)
            {
                suspicionColor = suspicionColorHigh; // Red - Critical
            }
            else if (alertLevel >= 51)
            {
                suspicionColor = Color.Lerp(suspicionColorMid, suspicionColorHigh, (alertLevel - 51f) / 25f); // Orange to Red
            }
            else if (alertLevel >= 26)
            {
                suspicionColor = Color.Lerp(suspicionColorLow, suspicionColorMid, (alertLevel - 26f) / 25f); // Green to Orange
            }
            else
            {
                suspicionColor = suspicionColorLow; // Green - Clear
            }

            suspicionText.color = suspicionColor;
        }
    }

    private void ApplyLoadingState()
    {
        EnsureUIReady();

        if (engagementPanel != null)
        {
            engagementPanel.SetActive(true);
        }

        if (sanityPanel != null)
        {
            sanityPanel.SetActive(true);
        }

        // Suspicion panel stays hidden in loading state
        if (suspicionPanel != null)
        {
            suspicionPanel.SetActive(false);
        }

#if USE_TMP
        if (engagementText != null)
        {
            engagementText.text = "Engagement: --%";
        }

        if (sanityText != null)
        {
            sanityText.text = "Sanity: --%";
        }
#else
        if (engagementText != null)
        {
            engagementText.text = "Engagement: --%";
        }

        if (sanityText != null)
        {
            sanityText.text = "Sanity: --%";
        }
#endif
    }

    private void ConfigureCanvasScaler(CanvasScaler scaler)
    {
        if (scaler == null) return;
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
    }

    private void EnsureContainerLayout()
    {
        if (metricsRoot == null)
        {
            return;
        }

        RectTransform rootRect = metricsRoot.GetComponent<RectTransform>();
        if (rootRect == null)
        {
            return;
        }

        Rect safeArea = Screen.safeArea;
        Vector2 screenSize = new Vector2(Screen.width, Screen.height);

        if (screenSize != lastScreenSize || safeArea != lastSafeArea)
        {
            float safeWidth = Mathf.Max(safeArea.width, 200f);
            float availableWidth = Mathf.Max(safeWidth - (rightMargin * 2f), 200f);
            availableWidth = Mathf.Min(availableWidth, safeWidth);

            float clampedFraction = Mathf.Clamp01(widthFraction);
            float targetWidth = safeWidth * clampedFraction;

            float minWidth = Mathf.Min(panelMinWidth, availableWidth);
            float maxWidth = Mathf.Min(panelMaxWidth, availableWidth);
            if (maxWidth < minWidth)
            {
                maxWidth = minWidth;
            }

            targetWidth = Mathf.Clamp(targetWidth, minWidth, maxWidth);

            rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, targetWidth);

            if (rootLayoutGroup != null)
            {
                int horizontalPadding = Mathf.RoundToInt(panelPadding.x);
                int verticalPadding = Mathf.RoundToInt(panelPadding.y);

                rootLayoutGroup.padding.left = horizontalPadding;
                rootLayoutGroup.padding.right = horizontalPadding;
                rootLayoutGroup.padding.top = verticalPadding;
                rootLayoutGroup.padding.bottom = verticalPadding;
                rootLayoutGroup.spacing = metricSpacing;
            }

            float safeTopPadding = Screen.height - safeArea.yMax;
            // Since anchor is (1f, 1f) and pivot is (1f, 1f), use simple negative offsets from top-right corner
            rootRect.anchoredPosition = new Vector2(-rightMargin, -(safeTopPadding + topMargin));

            lastScreenSize = screenSize;
            lastSafeArea = safeArea;
        }
    }

    private float GetPanelHeight()
    {
        int effectiveFontSize = Mathf.Max(fontSize, minFontSize);
        return Mathf.Max(panelPreferredHeight, effectiveFontSize + panelPadding.y * 2f + 8f);
    }

    private int GetFontSize()
    {
        return Mathf.Max(fontSize, minFontSize);
    }
}
