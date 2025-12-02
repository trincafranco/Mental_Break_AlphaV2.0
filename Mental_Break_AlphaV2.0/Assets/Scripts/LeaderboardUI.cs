using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Yarn.Unity;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Displays the Employee of the Month leaderboard at the top middle of the screen.
/// Shows top 3 fake entries plus the player's entry with their current rank.
/// Format: "Rank - Name - Engagement - Sanity"
/// </summary>
public class LeaderboardUI : MonoBehaviour
{
    private static class DefaultStyle
    {
        public const int MinFontSize = 32;
        public const int FontSize = 36;
        public const int TitleFontBonus = 8;
        public const float EntrySpacing = 0f;
        public const float EntryHeightPadding = 2f;
        public const float TitlePreferredHeight = 36f;
        public static readonly Vector2 PanelPadding = new Vector2(36f, 4f);
    }

    [Header("References")]
    [Tooltip("The DialogueRunner to read variables from")]
    public DialogueRunner dialogueRunner;

    [Header("Settings")]
    [Tooltip("Update interval in seconds")]
    public float updateInterval = 0.1f;

    [Tooltip("Minimum font size for leaderboard entries")]
    public int minFontSize = 32;

    [Tooltip("Font size for leaderboard text (clamped by Min Font Size)")]
    public int fontSize = 36;

    [Tooltip("Additional font size applied to the title")]
    public int titleFontBonus = 8;

    [Tooltip("Spacing between entries")]
    public float entrySpacing = 0f;

    [Tooltip("Additional padding added beyond the preferred text height (in pixels)")]
    public float entryHeightPadding = 2f;

    [Tooltip("Preferred height for the leaderboard title row")]
    public float titlePreferredHeight = 36f;

    [Header("Layout")]
    [Tooltip("Top margin from the top edge of the safe area")]
    public float topMargin = 12f;

    [Tooltip("Horizontal margin from the safe area's edges")]
    public float horizontalMargin = 32f;

    [Tooltip("Proportion of the safe-area width used by the leaderboard panel")]
    [Range(0.3f, 0.9f)]
    public float widthFraction = 0.65f;

    [Tooltip("Minimum width allowed for the leaderboard panel")]
    public float minPanelWidth = 520f;

    [Tooltip("Maximum width allowed for the leaderboard panel")]
    public float maxPanelWidth = 900f;

    [Tooltip("Padding inside the leaderboard panel (x = left/right, y = top/bottom)")]
    public Vector2 panelPadding = new Vector2(36f, 4f);

    [Header("Tuning")]
    [Tooltip("If enabled, the script will overwrite inspector values with the defaults defined in code, ensuring consistent layout in builds.")]
    public bool enforceRuntimeDefaults = true;

    private VariableStorageBehaviour variableStorage;
    private DialogueRuntimeWatcher runtimeWatcher;
    private float lastUpdateTime = 0f;

    // UI References
    private GameObject leaderboardPanel;
    private Canvas canvas;
    private List<GameObject> entryObjects = new List<GameObject>();
    private VerticalLayoutGroup layoutGroup;
    private ContentSizeFitter contentSizeFitter;
    private Vector2 lastScreenSize = Vector2.zero;
    private Rect lastSafeArea = Rect.zero;

    // Fake employee data
    private class LeaderboardEntry
    {
        public int rank;
        public string name;
        public float engagement;
        public float sanity;

        public LeaderboardEntry(int rank, string name, float engagement, float sanity)
        {
            this.rank = rank;
            this.name = name;
            this.engagement = engagement;
            this.sanity = sanity;
        }
    }

    private List<LeaderboardEntry> fakeEntries = new List<LeaderboardEntry>();

    private void Awake()
    {
        ApplyRuntimeDefaults();
        // Generate fake data
        GenerateFakeEntries();
    }

    private void OnEnable()
    {
        runtimeWatcher = DialogueRuntimeWatcher.Instance;
        runtimeWatcher.Register(OnRuntimeReady, OnRuntimeLost);

        if (!runtimeWatcher.HasRuntime)
        {
            ShowLoadingState();
        }
    }

    private void OnDisable()
    {
        if (runtimeWatcher != null)
        {
            runtimeWatcher.Unregister(OnRuntimeReady, OnRuntimeLost);
        }

        runtimeWatcher = null;
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
        if (leaderboardPanel == null)
        {
            CreateUI();
        }
    }

    private void OnRuntimeReady(DialogueRunner runner, VariableStorageBehaviour storage)
    {
        if (runner != null)
        {
            dialogueRunner = runner;
        }

        variableStorage = storage;
        
        // Ensure UI is fully created before updating (fixes WebGL first-load timing issue)
        EnsureUIReady();
        
        // Force a layout rebuild if needed
        if (leaderboardPanel != null)
        {
            Canvas.ForceUpdateCanvases();
        }
        
        UpdateLeaderboard();
    }

    private void OnRuntimeLost()
    {
        variableStorage = null;
        ShowLoadingState();
    }

    private void ShowLoadingState()
    {
        EnsureUIReady();
        DisplayStatusMessage("Loading leaderboard…");
    }

    private void EnsureUIReady()
    {
        if (leaderboardPanel == null)
        {
            CreateUI();
        }
    }

    private void DisplayStatusMessage(string message)
    {
        EnsureUIReady();
        ClearEntryObjects();

        GameObject statusObj = new GameObject("Status");
        statusObj.transform.SetParent(leaderboardPanel.transform, false);

        RectTransform statusRect = statusObj.AddComponent<RectTransform>();
        statusRect.anchorMin = new Vector2(0f, 0.5f);
        statusRect.anchorMax = new Vector2(1f, 0.5f);
        statusRect.pivot = new Vector2(0.5f, 0.5f);
        statusRect.anchoredPosition = Vector2.zero;

        float entryHeight = GetEntryHeight();
        LayoutElement layoutElement = statusObj.AddComponent<LayoutElement>();
        layoutElement.preferredHeight = entryHeight;
        layoutElement.minHeight = entryHeight;
        layoutElement.flexibleHeight = 0f;
        layoutElement.flexibleWidth = 1f;

        TextMeshProUGUI textComponent = statusObj.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.instance != null && TMP_Settings.defaultFontAsset != null)
        {
            textComponent.font = TMP_Settings.defaultFontAsset;
        }
        textComponent.text = message;
        textComponent.fontSize = GetEntryFontSize();
        textComponent.alignment = TextAlignmentOptions.Center;
        textComponent.color = Color.gray;
        textComponent.raycastTarget = false;

        entryObjects.Add(statusObj);
    }

    private void ClearEntryObjects()
    {
        foreach (GameObject entryObj in entryObjects)
        {
            if (entryObj != null)
            {
                Destroy(entryObj);
            }
        }
        entryObjects.Clear();
    }

    private void Update()
    {
        EnsurePanelLayout();

        // Update leaderboard at intervals
        if (Time.time - lastUpdateTime >= updateInterval)
        {
            UpdateLeaderboard();
            lastUpdateTime = Time.time;
        }
    }

    private int GetEntryFontSize()
    {
        return Mathf.Max(fontSize, minFontSize);
    }

    private int GetTitleFontSize()
    {
        return GetEntryFontSize() + Mathf.Max(titleFontBonus, 0);
    }

    private float GetEntryHeight()
    {
        // Entry height is computed dynamically per entry; this value represents the minimum padding.
        return GetEntryFontSize() + entryHeightPadding;
    }

    private float GetTitleHeight()
    {
        return Mathf.Max(titlePreferredHeight, GetTitleFontSize() + 12f);
    }

    private void ApplyRuntimeDefaults()
    {
        if (!enforceRuntimeDefaults)
        {
            return;
        }

        minFontSize = DefaultStyle.MinFontSize;
        fontSize = DefaultStyle.FontSize;
        titleFontBonus = DefaultStyle.TitleFontBonus;
        entrySpacing = DefaultStyle.EntrySpacing;
        entryHeightPadding = DefaultStyle.EntryHeightPadding;
        titlePreferredHeight = DefaultStyle.TitlePreferredHeight;
        panelPadding = DefaultStyle.PanelPadding;
    }

    /// <summary>
    /// Generate fake employee entries for top 3 positions
    /// </summary>
    private void GenerateFakeEntries()
    {
        fakeEntries.Clear();

        // Generate 3 fake entries with high engagement, low sanity (deterministic values)
        fakeEntries.Add(new LeaderboardEntry(1, "Sarah Chen", 92f, 45f));
        fakeEntries.Add(new LeaderboardEntry(2, "Marcus Smith", 88f, 38f));
        fakeEntries.Add(new LeaderboardEntry(3, "Calan Fields", 87f, 42f));

        // Sort by engagement descending
        fakeEntries = fakeEntries.OrderByDescending(e => e.engagement).ToList();
        
        // Reassign ranks after sorting
        for (int i = 0; i < fakeEntries.Count; i++)
        {
            fakeEntries[i].rank = i + 1;
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
            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();
        }
        else
        {
            // Use existing canvas, ensure it has required components
            if (canvas.GetComponent<CanvasScaler>() == null)
            {
                canvas.gameObject.AddComponent<CanvasScaler>();
            }
            if (canvas.GetComponent<GraphicRaycaster>() == null)
            {
                canvas.gameObject.AddComponent<GraphicRaycaster>();
            }
        }

        // Create leaderboard panel
        leaderboardPanel = new GameObject("LeaderboardPanel");
        leaderboardPanel.transform.SetParent(canvas.transform, false);

        RectTransform panelRect = leaderboardPanel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 1f);
        panelRect.anchorMax = new Vector2(0.5f, 1f);
        panelRect.pivot = new Vector2(0.5f, 1f);
        panelRect.anchoredPosition = Vector2.zero;

        Image bgImage = leaderboardPanel.AddComponent<Image>();
        bgImage.color = new Color(0f, 0f, 0f, 0.7f); // Semi-transparent black
        bgImage.raycastTarget = false;

        layoutGroup = leaderboardPanel.AddComponent<VerticalLayoutGroup>();
        layoutGroup.childControlWidth = true;
        layoutGroup.childControlHeight = true; // Control height to enforce exact sizes
        layoutGroup.childForceExpandWidth = true;
        layoutGroup.childForceExpandHeight = false; // Don't expand height
        layoutGroup.childAlignment = TextAnchor.UpperCenter;
        layoutGroup.spacing = entrySpacing; // Should be 0

        contentSizeFitter = leaderboardPanel.AddComponent<ContentSizeFitter>();
        contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        // Add title as the first child; entries are added during updates
        CreateTitle("Employee of the Month");

        lastScreenSize = Vector2.zero;
        lastSafeArea = new Rect();
        EnsurePanelLayout();
    }

    private void EnsurePanelLayout()
    {
        if (leaderboardPanel == null)
        {
            return;
        }

        if (layoutGroup != null)
        {
            int horizontalPadding = Mathf.RoundToInt(panelPadding.x);
            int verticalPadding = Mathf.RoundToInt(panelPadding.y);

            layoutGroup.padding.left = horizontalPadding;
            layoutGroup.padding.right = horizontalPadding;
            layoutGroup.padding.top = verticalPadding;
            layoutGroup.padding.bottom = verticalPadding;
            layoutGroup.spacing = 0f; // Force to 0 - no spacing between entries
        }

        RectTransform panelRect = leaderboardPanel.GetComponent<RectTransform>();
        if (panelRect == null)
        {
            return;
        }

        Rect safeArea = Screen.safeArea;
        Vector2 screenSize = new Vector2(Screen.width, Screen.height);

        if (screenSize != lastScreenSize || safeArea != lastSafeArea)
        {
            float safeWidth = Mathf.Max(safeArea.width, 200f);
            float availableWidth = Mathf.Max(safeWidth - (horizontalMargin * 2f), 200f);
            availableWidth = Mathf.Min(availableWidth, safeWidth);

            float clampedFraction = Mathf.Clamp01(widthFraction);
            float targetWidth = safeWidth * clampedFraction;

            float minWidth = Mathf.Min(minPanelWidth, availableWidth);
            float maxWidth = Mathf.Min(maxPanelWidth, availableWidth);
            if (maxWidth < minWidth)
            {
                maxWidth = minWidth;
            }

            targetWidth = Mathf.Clamp(targetWidth, minWidth, maxWidth);

            panelRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, targetWidth);

            float safeTopPadding = Screen.height - safeArea.yMax;
            float horizontalOffset = (safeArea.x + (safeArea.width * 0.5f)) - (Screen.width * 0.5f);
            panelRect.anchoredPosition = new Vector2(horizontalOffset, -(safeTopPadding + topMargin));

            lastScreenSize = screenSize;
            lastSafeArea = safeArea;
        }
    }

    /// <summary>
    /// Create the title text
    /// </summary>
    private void CreateTitle(string titleText)
    {
        GameObject titleObj = new GameObject("Title");
        titleObj.transform.SetParent(leaderboardPanel.transform, false);

        RectTransform titleRect = titleObj.AddComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0f, 0.5f);
        titleRect.anchorMax = new Vector2(1f, 0.5f);
        titleRect.pivot = new Vector2(0.5f, 0.5f);
        titleRect.anchoredPosition = Vector2.zero;
        float titleHeight = GetTitleHeight();
        titleRect.sizeDelta = new Vector2(0f, titleHeight);

        LayoutElement layoutElement = titleObj.AddComponent<LayoutElement>();
        layoutElement.preferredHeight = titleHeight;
        layoutElement.minHeight = titleHeight;
        layoutElement.flexibleHeight = 0f;
        layoutElement.flexibleWidth = 1f;

        TextMeshProUGUI titleTextComponent = titleObj.AddComponent<TextMeshProUGUI>();
        // Load default font if available
        if (TMP_Settings.instance != null && TMP_Settings.defaultFontAsset != null)
        {
            titleTextComponent.font = TMP_Settings.defaultFontAsset;
        }
        titleTextComponent.text = titleText;
        titleTextComponent.fontSize = GetTitleFontSize();
        titleTextComponent.alignment = TextAlignmentOptions.Center;
        titleTextComponent.color = Color.white;
        titleTextComponent.textWrappingMode = TextWrappingModes.NoWrap;
        titleTextComponent.raycastTarget = false;
    }

    /// <summary>
    /// Update the leaderboard with current player data
    /// </summary>
    private void UpdateLeaderboard()
    {
        EnsureUIReady();

        if (variableStorage == null) return;

        // Get player's current values
        float playerEngagement = 0f;
        float playerSanity = 0f;
        
        variableStorage.TryGetValue<float>("$engagement", out playerEngagement);
        variableStorage.TryGetValue<float>("$sanity", out playerSanity);

        // Create combined list with player entry
        List<LeaderboardEntry> allEntries = new List<LeaderboardEntry>(fakeEntries);
        allEntries.Add(new LeaderboardEntry(0, "You", playerEngagement, playerSanity));

        // Sort by engagement descending
        allEntries = allEntries.OrderByDescending(e => e.engagement).ToList();

        // Reassign ranks
        for (int i = 0; i < allEntries.Count; i++)
        {
            allEntries[i].rank = i + 1;
        }

        // Get top 3 + player entry
        List<LeaderboardEntry> displayEntries = new List<LeaderboardEntry>();
        
        // Add top 3
        for (int i = 0; i < Mathf.Min(3, allEntries.Count); i++)
        {
            displayEntries.Add(allEntries[i]);
        }

        // Find player entry and add if not in top 3
        LeaderboardEntry playerEntry = allEntries.FirstOrDefault(e => e.name == "You");
        if (playerEntry != null && playerEntry.rank > 3)
        {
            displayEntries.Add(playerEntry);
        }

        // Update UI
        UpdateUIEntries(displayEntries);
        
        // Force layout rebuild
        if (layoutGroup != null)
        {
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(layoutGroup.GetComponent<RectTransform>());
        }
    }

    /// <summary>
    /// Update or create UI entry objects
    /// </summary>
    private void UpdateUIEntries(List<LeaderboardEntry> entries)
    {
        EnsureUIReady();
        ClearEntryObjects();

        // Create entry objects
        foreach (LeaderboardEntry entry in entries)
        {
            GameObject entryObj = CreateEntryObject(entry);
            entryObjects.Add(entryObj);
        }
        
        // Force layout rebuild after creating entries
        if (layoutGroup != null)
        {
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(layoutGroup.GetComponent<RectTransform>());
        }
    }

    /// <summary>
    /// Create a single entry object
    /// </summary>
    private GameObject CreateEntryObject(LeaderboardEntry entry)
    {
        GameObject entryObj = new GameObject($"Entry_{entry.rank}");
        entryObj.transform.SetParent(leaderboardPanel.transform, false);

        RectTransform entryRect = entryObj.AddComponent<RectTransform>();
        entryRect.anchorMin = new Vector2(0f, 0.5f);
        entryRect.anchorMax = new Vector2(1f, 0.5f);
        entryRect.pivot = new Vector2(0.5f, 0.5f);
        entryRect.anchoredPosition = Vector2.zero;

        // Create horizontal layout for columns
        HorizontalLayoutGroup horizontalLayout = entryObj.AddComponent<HorizontalLayoutGroup>();
        horizontalLayout.childControlWidth = false;
        horizontalLayout.childControlHeight = true;
        horizontalLayout.childForceExpandWidth = false;
        horizontalLayout.childForceExpandHeight = true;
        horizontalLayout.spacing = 16f; // Space between columns
        horizontalLayout.childAlignment = TextAnchor.MiddleLeft;
        horizontalLayout.padding.left = 0;
        horizontalLayout.padding.right = 0;
        horizontalLayout.padding.top = 0;
        horizontalLayout.padding.bottom = 0;

        LayoutElement layoutElement = entryObj.AddComponent<LayoutElement>();
        layoutElement.flexibleHeight = 0f;
        layoutElement.flexibleWidth = 1f;

        Color textColor = entry.name == "You" ? Color.yellow : Color.white;

        // Column 1: Rank (left-aligned, minimal width)
        GameObject rankObj = new GameObject("Rank");
        rankObj.transform.SetParent(entryObj.transform, false);
        RectTransform rankRect = rankObj.AddComponent<RectTransform>();
        LayoutElement rankLayout = rankObj.AddComponent<LayoutElement>();
        rankLayout.preferredWidth = 35f;
        rankLayout.flexibleWidth = 0f;
        TextMeshProUGUI rankText = rankObj.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.instance != null && TMP_Settings.defaultFontAsset != null)
        {
            rankText.font = TMP_Settings.defaultFontAsset;
        }
        rankText.text = entry.rank.ToString();
        rankText.fontSize = GetEntryFontSize();
        rankText.alignment = TextAlignmentOptions.MidlineLeft;
        rankText.color = textColor;
        rankText.raycastTarget = false;

        // Column 2: Name (left-aligned, flexible width)
        GameObject nameObj = new GameObject("Name");
        nameObj.transform.SetParent(entryObj.transform, false);
        RectTransform nameRect = nameObj.AddComponent<RectTransform>();
        LayoutElement nameLayout = nameObj.AddComponent<LayoutElement>();
        nameLayout.preferredWidth = 300f;
        nameLayout.minWidth = 250f;
        nameLayout.flexibleWidth = 1f;
        TextMeshProUGUI nameText = nameObj.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.instance != null && TMP_Settings.defaultFontAsset != null)
        {
            nameText.font = TMP_Settings.defaultFontAsset;
        }
        nameText.text = entry.name;
        nameText.fontSize = GetEntryFontSize();
        nameText.alignment = TextAlignmentOptions.MidlineLeft;
        nameText.textWrappingMode = TextWrappingModes.NoWrap;
        nameText.overflowMode = TextOverflowModes.Overflow;
        nameText.color = textColor;
        nameText.raycastTarget = false;

        // Column 3: Engagement (right-aligned, fixed width)
        GameObject engagementObj = new GameObject("Engagement");
        engagementObj.transform.SetParent(entryObj.transform, false);
        RectTransform engagementRect = engagementObj.AddComponent<RectTransform>();
        LayoutElement engagementLayout = engagementObj.AddComponent<LayoutElement>();
        engagementLayout.preferredWidth = 100f;
        engagementLayout.flexibleWidth = 0f;
        TextMeshProUGUI engagementText = engagementObj.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.instance != null && TMP_Settings.defaultFontAsset != null)
        {
            engagementText.font = TMP_Settings.defaultFontAsset;
        }
        engagementText.text = entry.engagement.ToString("F0");
        engagementText.fontSize = GetEntryFontSize();
        engagementText.alignment = TextAlignmentOptions.MidlineRight;
        engagementText.color = textColor;
        engagementText.raycastTarget = false;

        // Column 4: Sanity (right-aligned, fixed width)
        GameObject sanityObj = new GameObject("Sanity");
        sanityObj.transform.SetParent(entryObj.transform, false);
        RectTransform sanityRect = sanityObj.AddComponent<RectTransform>();
        LayoutElement sanityLayout = sanityObj.AddComponent<LayoutElement>();
        sanityLayout.preferredWidth = 100f;
        sanityLayout.flexibleWidth = 0f;
        TextMeshProUGUI sanityText = sanityObj.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.instance != null && TMP_Settings.defaultFontAsset != null)
        {
            sanityText.font = TMP_Settings.defaultFontAsset;
        }
        sanityText.text = entry.sanity.ToString("F0");
        sanityText.fontSize = GetEntryFontSize();
        sanityText.alignment = TextAlignmentOptions.MidlineRight;
        sanityText.color = textColor;
        sanityText.raycastTarget = false;

        // Calculate entry height
        float entryHeight = GetEntryHeight();
        entryRect.sizeDelta = new Vector2(0f, entryHeight);
        layoutElement.preferredHeight = entryHeight;
        layoutElement.minHeight = entryHeight;

        return entryObj;
    }

    private void OnDestroy()
    {
        if (runtimeWatcher != null)
        {
            runtimeWatcher.Unregister(OnRuntimeReady, OnRuntimeLost);
        }

        // Clean up entry objects
        ClearEntryObjects();
    }
}
