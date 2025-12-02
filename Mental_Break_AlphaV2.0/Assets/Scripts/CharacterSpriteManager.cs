using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Yarn.Unity;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Manages character sprite display based on Yarn node tags.
/// Displays up to 2 character sprites (bottom left priority, then bottom right).
/// Sprites render above background, below dialogue/choice box.
/// </summary>
public class CharacterSpriteManager : MonoBehaviour
{
    [Header("Character Sprites")]
    [Tooltip("Assign character sprites to their tags here (optional - will auto-load from Graphics/Characters if empty)")]
    public List<CharacterSpriteEntry> characterSprites = new List<CharacterSpriteEntry>();
    
    [Header("Auto-Load Settings")]
    [Tooltip("Path to character sprites folder (relative to Assets/)")]
    public string characterFolderPath = "Graphics/Characters";
    
    [Header("References")]
    [Tooltip("DialogueRunner reference (auto-found if null)")]
    public DialogueRunner dialogueRunner;
    
    [Header("UI Setup")]
    [Tooltip("Canvas to add character sprites to (auto-found if null)")]
    public Canvas targetCanvas;
    
    [Tooltip("Z-depth offset for character sprites (higher = closer to camera)")]
    public int spriteSortOrder = 10;
    
    [Tooltip("Character sprite size (width, height)")]
    public Vector2 spriteSize = new Vector2(200f, 300f);
    
    [Tooltip("Bottom-left position offset (x, y from bottom-left corner)")]
    public Vector2 bottomLeftOffset = new Vector2(100f, 100f);
    
    [Tooltip("Bottom-right position offset (x, y from bottom-right corner)")]
    public Vector2 bottomRightOffset = new Vector2(-100f, 100f);
    
    // Cache dictionary for fast lookup
    private Dictionary<string, Sprite> spriteDictionary;
    
    // Character sprite GameObjects
    private GameObject leftSpriteObject;
    private GameObject rightSpriteObject;
    
    // Track current background for Alice auto-add logic
    private string currentBackground = "";
    
    // Reference to BackgroundCommandHandler to track background changes
    private BackgroundCommandHandler backgroundHandler;
    
    // Character tag to sprite name mapping
    private Dictionary<string, string> characterTagToSpriteName = new Dictionary<string, string>
    {
        { "char_Alice", "alice" },
        { "char_Supervisor", "supervisor" },
        { "char_Timmy", "timmy" },
        { "char_BTC", "btc" },
        { "char_Player", null }, // Player doesn't have a sprite
        { "char_DarkFigure", null }, // Dark Figure doesn't have a sprite
    };
    
    [System.Serializable]
    public class CharacterSpriteEntry
    {
        public string characterTag; // e.g., "char_Alice"
        public Sprite sprite;
    }
    
    void Awake()
    {
        BuildDictionary();
        FindReferences();
        SetupCharacterSprites();
        
        // Subscribe to node start events
        if (dialogueRunner != null)
        {
            dialogueRunner.onNodeStart.AddListener(OnNodeStarted);
        }
    }
    
    void OnDestroy()
    {
        // Unsubscribe from events
        if (dialogueRunner != null)
        {
            dialogueRunner.onNodeStart.RemoveListener(OnNodeStarted);
        }
    }
    
    void FindReferences()
    {
        if (dialogueRunner == null)
        {
            dialogueRunner = FindAnyObjectByType<DialogueRunner>();
        }
        
        if (targetCanvas == null)
        {
            targetCanvas = FindDialogueCanvas();
        }
        
        // Find BackgroundCommandHandler to track background changes
        if (backgroundHandler == null)
        {
            backgroundHandler = FindAnyObjectByType<BackgroundCommandHandler>();
        }
    }
    
    Canvas FindDialogueCanvas()
    {
        // PRIORITY 1: Find Canvas in DontDestroyOnLoad (this is where dialogue system lives)
        GameObject dontDestroy = GameObject.Find("DontDestroyOnLoad");
        if (dontDestroy != null)
        {
            Canvas canvas = dontDestroy.GetComponentInChildren<Canvas>(true); // Include inactive
            if (canvas != null)
            {
                Debug.Log($"CharacterSpriteManager: Found Canvas in DontDestroyOnLoad: '{canvas.name}' on GameObject '{canvas.gameObject.name}' (InstanceID: {canvas.GetInstanceID()})");
                return canvas;
            }
        }
        
        // PRIORITY 2: Find via LinePresenter (but verify it's in DontDestroyOnLoad or dialogue system)
        LinePresenter linePresenter = FindAnyObjectByType<LinePresenter>();
        if (linePresenter != null)
        {
            Canvas canvas = linePresenter.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                // Verify this Canvas is in DontDestroyOnLoad hierarchy
                Transform current = canvas.transform;
                while (current != null && current.parent != null)
                {
                    if (current.parent.name == "DontDestroyOnLoad" || current.parent.name.Contains("Dialogue"))
                    {
                        Debug.Log($"CharacterSpriteManager: Found Canvas via LinePresenter in dialogue system: '{canvas.name}' on GameObject '{canvas.gameObject.name}' (InstanceID: {canvas.GetInstanceID()})");
                        return canvas;
                    }
                    current = current.parent;
                }
                Debug.LogWarning($"CharacterSpriteManager: LinePresenter Canvas '{canvas.name}' is NOT in DontDestroyOnLoad hierarchy, continuing search...");
            }
        }
        
        // PRIORITY 3: Try OptionsPresenter (but verify it's in dialogue system)
        OptionsPresenter optionsPresenter = FindAnyObjectByType<OptionsPresenter>();
        if (optionsPresenter != null)
        {
            Canvas canvas = optionsPresenter.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                // Verify this Canvas is in DontDestroyOnLoad hierarchy
                Transform current = canvas.transform;
                while (current != null && current.parent != null)
                {
                    if (current.parent.name == "DontDestroyOnLoad" || current.parent.name.Contains("Dialogue"))
                    {
                        Debug.Log($"CharacterSpriteManager: Found Canvas via OptionsPresenter in dialogue system: '{canvas.name}' on GameObject '{canvas.gameObject.name}' (InstanceID: {canvas.GetInstanceID()})");
                        return canvas;
                    }
                    current = current.parent;
                }
                Debug.LogWarning($"CharacterSpriteManager: OptionsPresenter Canvas '{canvas.name}' is NOT in DontDestroyOnLoad hierarchy, continuing search...");
            }
        }
        
        // Last resort: find any Canvas (but warn)
        Canvas anyCanvas = FindAnyObjectByType<Canvas>();
        Debug.LogWarning($"CharacterSpriteManager: Using fallback Canvas (may be wrong one!): {(anyCanvas != null ? $"'{anyCanvas.name}' on '{anyCanvas.gameObject.name}' (InstanceID: {anyCanvas.GetInstanceID()})" : "NULL")}");
        return anyCanvas;
    }
    
    void BuildDictionary()
    {
        spriteDictionary = new Dictionary<string, Sprite>();
        
        // First, add manually assigned sprites
        int manualCount = 0;
        foreach (var entry in characterSprites)
        {
            if (entry.sprite != null && !string.IsNullOrEmpty(entry.characterTag))
            {
                spriteDictionary[entry.characterTag] = entry.sprite;
                manualCount++;
            }
        }
        Debug.Log($"CharacterSpriteManager: Loaded {manualCount} manually assigned sprites");
        
        // Then, try to auto-load from Resources or folder
        AutoLoadCharacters();
        
        // Log dictionary contents
        Debug.Log($"CharacterSpriteManager: Sprite dictionary contains {spriteDictionary.Count} entries:");
        foreach (var kvp in spriteDictionary)
        {
            Debug.Log($"  - {kvp.Key}: {(kvp.Value != null ? kvp.Value.name : "NULL")}");
        }
    }
    
    void AutoLoadCharacters()
    {
#if UNITY_EDITOR
        // In editor, load from folder path (works in both edit and play mode)
        LoadFromFolder();
        
        // Also try Resources as fallback
        LoadFromResources();
#else
        // Runtime: Only Resources.Load works
        LoadFromResources();
#endif
    }
    
    void LoadFromResources()
    {
        // Try Resources.Load with the folder path
        string resourcesPath = characterFolderPath.Replace("Assets/", "").Replace("\\", "/");
        
        // Remove "Resources/" prefix if present
        if (resourcesPath.StartsWith("Resources/", System.StringComparison.OrdinalIgnoreCase))
        {
            resourcesPath = resourcesPath.Substring("Resources/".Length);
        }
        
        Sprite[] sprites = Resources.LoadAll<Sprite>(resourcesPath);
        
        // Also try loading from Resources/Characters (standard location for builds)
        if (sprites == null || sprites.Length == 0)
        {
            sprites = Resources.LoadAll<Sprite>("Characters");
            if (sprites != null && sprites.Length > 0)
            {
                Debug.Log($"CharacterSpriteManager: Loading from Resources/Characters");
            }
        }
        
        if (sprites != null && sprites.Length > 0)
        {
            foreach (var sprite in sprites)
            {
                if (sprite != null)
                {
                    // Extract character name from sprite name (e.g., "alice.jpg" -> "alice")
                    string spriteName = sprite.name.ToLower();
                    string charTag = "char_" + CapitalizeFirst(spriteName);
                    
                    // Try to match with known character tags
                    foreach (var kvp in characterTagToSpriteName)
                    {
                        if (kvp.Value != null && spriteName.Contains(kvp.Value.ToLower()))
                        {
                            charTag = kvp.Key;
                            break;
                        }
                    }
                    
                    // Only add if not already in dictionary (manual assignments take precedence)
                    if (!spriteDictionary.ContainsKey(charTag))
                    {
                        spriteDictionary[charTag] = sprite;
                        Debug.Log($"CharacterSpriteManager: Auto-loaded character '{charTag}' from Resources (sprite: {sprite.name})");
                    }
                }
            }
        }
    }
    
#if UNITY_EDITOR
    void LoadFromFolder()
    {
        string fullPath = "Assets/" + characterFolderPath;
        
        if (!AssetDatabase.IsValidFolder(fullPath))
        {
            return;
        }
        
        // Find all texture files in the folder
        string[] textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { fullPath });
        
        foreach (string guid in textureGuids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            
            // Extract character name from file name (e.g., "alice.jpg" -> "alice")
            string fileName = System.IO.Path.GetFileNameWithoutExtension(assetPath).ToLower();
            string charTag = "char_" + CapitalizeFirst(fileName);
            
            // Try to match with known character tags
            foreach (var kvp in characterTagToSpriteName)
            {
                if (kvp.Value != null && fileName.Contains(kvp.Value.ToLower()))
                {
                    charTag = kvp.Key;
                    break;
                }
            }
            
            // Skip if already in dictionary
            if (spriteDictionary.ContainsKey(charTag))
            {
                continue;
            }
            
            // Load the sprite (Unity can load sprites from textures)
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            
            // If not a sprite, try loading as texture and getting sprites from it
            if (sprite == null)
            {
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (texture != null)
                {
                    // Try to get all sprites from the texture (for sprite sheets)
                    Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                    foreach (Object asset in assets)
                    {
                        if (asset is Sprite spriteAsset)
                        {
                            sprite = spriteAsset;
                            break; // Use the first sprite found
                        }
                    }
                    
                    // If still no sprite, create one from the texture
                    if (sprite == null)
                    {
                        sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0f));
                    }
                }
            }
            
            if (sprite != null)
            {
                spriteDictionary[charTag] = sprite;
                Debug.Log($"CharacterSpriteManager: Auto-loaded character '{charTag}' from {assetPath}");
            }
        }
    }
#endif
    
    string CapitalizeFirst(string str)
    {
        if (string.IsNullOrEmpty(str))
            return str;
        return char.ToUpper(str[0]) + str.Substring(1);
    }
    
    int FindBackgroundImageIndex()
    {
        if (targetCanvas == null)
            return -1;
        
        // Look for BackgroundCommandHandler's image component
        BackgroundCommandHandler bgHandler = FindAnyObjectByType<BackgroundCommandHandler>();
        if (bgHandler != null && bgHandler.backgroundImage != null)
        {
            Transform bgTransform = bgHandler.backgroundImage.transform;
            if (bgTransform.parent == targetCanvas.transform)
            {
                return bgTransform.GetSiblingIndex();
            }
        }
        
        // Fallback: look for any Image component with "background" in name
        for (int i = 0; i < targetCanvas.transform.childCount; i++)
        {
            Transform child = targetCanvas.transform.GetChild(i);
            Image img = child.GetComponent<Image>();
            if (img != null && child.name.ToLower().Contains("background"))
            {
                return i;
            }
        }
        
        return -1;
    }
    
    int FindDialogueUIIndex()
    {
        if (targetCanvas == null)
            return -1;
        
        int lowestDialogueIndex = int.MaxValue;
        
        // Search recursively through all children
        SearchForDialogueUI(targetCanvas.transform, 0, ref lowestDialogueIndex);
        
        return lowestDialogueIndex == int.MaxValue ? -1 : lowestDialogueIndex;
    }
    
    void SearchForDialogueUI(Transform parent, int currentIndex, ref int lowestIndex)
    {
        // Check current level for dialogue UI components
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            
            // Check for LinePresenter
            if (child.GetComponent<LinePresenter>() != null)
            {
                // Get sibling index relative to canvas root
                int siblingIndex = GetSiblingIndexRelativeToCanvas(child);
                if (siblingIndex >= 0 && siblingIndex < lowestIndex)
                {
                    lowestIndex = siblingIndex;
                }
            }
            
            // Check for OptionsPresenter
            if (child.GetComponent<OptionsPresenter>() != null)
            {
                // Get sibling index relative to canvas root
                int siblingIndex = GetSiblingIndexRelativeToCanvas(child);
                if (siblingIndex >= 0 && siblingIndex < lowestIndex)
                {
                    lowestIndex = siblingIndex;
                }
            }
            
            // Check for names containing "Line" or "Option" or "Dialogue"
            string name = child.name.ToLower();
            if (name.Contains("line") || name.Contains("option") || name.Contains("dialogue"))
            {
                // Get sibling index relative to canvas root
                int siblingIndex = GetSiblingIndexRelativeToCanvas(child);
                if (siblingIndex >= 0 && siblingIndex < lowestIndex)
                {
                    lowestIndex = siblingIndex;
                }
            }
            
            // Recursively search children
            if (child.childCount > 0)
            {
                SearchForDialogueUI(child, i, ref lowestIndex);
            }
        }
    }
    
    int GetSiblingIndexRelativeToCanvas(Transform transform)
    {
        // If transform is direct child of canvas, return its sibling index
        if (transform.parent == targetCanvas.transform)
        {
            return transform.GetSiblingIndex();
        }
        
        // Otherwise, find the parent that's a direct child of canvas
        Transform current = transform;
        while (current != null && current.parent != targetCanvas.transform)
        {
            current = current.parent;
        }
        
        if (current != null && current.parent == targetCanvas.transform)
        {
            return current.GetSiblingIndex();
        }
        
        return -1;
    }
    
    void LogCanvasHierarchy(string context = "")
    {
        if (targetCanvas == null)
        {
            Debug.LogWarning($"CharacterSpriteManager: Cannot log hierarchy - targetCanvas is null ({context})");
            return;
        }
        
        Debug.Log($"=== Canvas Hierarchy {context} ===");
        Debug.Log($"Canvas: '{targetCanvas.name}' on GameObject '{targetCanvas.gameObject.name}'");
        Debug.Log($"Total children: {targetCanvas.transform.childCount}");
        
        for (int i = 0; i < targetCanvas.transform.childCount; i++)
        {
            Transform child = targetCanvas.transform.GetChild(i);
            bool isActive = child.gameObject.activeSelf;
            string activeStr = isActive ? "ACTIVE" : "INACTIVE";
            
            // Check if this is one of our sprites
            string spriteMarker = "";
            if (child == leftSpriteObject?.transform) spriteMarker = " <-- LEFT SPRITE";
            if (child == rightSpriteObject?.transform) spriteMarker = " <-- RIGHT SPRITE";
            
            Debug.Log($"  [{i}] {child.name} ({activeStr}){spriteMarker}");
        }
        Debug.Log("=== End Hierarchy ===");
    }
    
    void SetupCharacterSprites()
    {
        // Force re-find the Canvas to ensure we have the correct one
        // This is critical because sprites must be in the same Canvas as dialogue UI
        Canvas correctCanvas = FindDialogueCanvas();
        if (correctCanvas == null)
        {
            Debug.LogError("CharacterSpriteManager: Cannot find dialogue Canvas. Cannot create character sprites.");
            return;
        }
        
        // Verify we're using the correct Canvas
        if (targetCanvas != correctCanvas)
        {
            Debug.LogWarning($"CharacterSpriteManager: Canvas mismatch! targetCanvas was '{targetCanvas?.name}' on '{targetCanvas?.gameObject.name}', but correct Canvas is '{correctCanvas.name}' on '{correctCanvas.gameObject.name}'. Updating...");
            targetCanvas = correctCanvas;
        }
        
        Debug.Log($"CharacterSpriteManager: Setting up sprites in Canvas '{targetCanvas.name}' on GameObject '{targetCanvas.gameObject.name}' (InstanceID: {targetCanvas.GetInstanceID()})");
        
        // Log hierarchy BEFORE making changes
        LogCanvasHierarchy("BEFORE sprite creation");
        
        // Find background and dialogue UI indices
        int backgroundIndex = FindBackgroundImageIndex();
        int dialogueUIIndex = FindDialogueUIIndex();
        
        Debug.Log($"CharacterSpriteManager: Background index: {backgroundIndex}, Dialogue UI index: {dialogueUIIndex}");
        
        // Calculate target index: right after background, but BEFORE dialogue UI
        int targetIndex;
        if (backgroundIndex >= 0)
        {
            // Start right after background
            targetIndex = backgroundIndex + 1;
            
            // If dialogue UI is found and would be before or at our target, adjust
            if (dialogueUIIndex >= 0 && targetIndex >= dialogueUIIndex)
            {
                // Sprites would be after dialogue - move before dialogue
                targetIndex = dialogueUIIndex - 1;
                // But ensure still after background
                if (targetIndex <= backgroundIndex)
                {
                    targetIndex = backgroundIndex + 1; // Place right after background
                }
            }
        }
        else
        {
            // No background found, place at index 0 or before dialogue UI
            if (dialogueUIIndex >= 0)
            {
                // Place at least 1 index before dialogue UI
                targetIndex = Mathf.Max(0, dialogueUIIndex - 1);
            }
            else
            {
                targetIndex = 0; // Fallback: place at start
            }
        }
        
        // Safety: Ensure target index is not negative
        targetIndex = Mathf.Max(0, targetIndex);
        
        Debug.Log($"CharacterSpriteManager: Calculated target sprite index: {targetIndex} (background: {backgroundIndex}, dialogue: {dialogueUIIndex})");
        
        // Destroy existing sprite objects if they exist (in case they were created in wrong Canvas)
        if (leftSpriteObject != null)
        {
            Debug.LogWarning($"CharacterSpriteManager: Destroying existing left sprite object (was in wrong Canvas)");
            DestroyImmediate(leftSpriteObject);
            leftSpriteObject = null;
        }
        if (rightSpriteObject != null)
        {
            Debug.LogWarning($"CharacterSpriteManager: Destroying existing right sprite object (was in wrong Canvas)");
            DestroyImmediate(rightSpriteObject);
            rightSpriteObject = null;
        }
        
        // Verify we have the correct Canvas before creating sprites
        if (targetCanvas == null)
        {
            Debug.LogError("CharacterSpriteManager: targetCanvas is null! Cannot create sprites.");
            return;
        }
        
        Debug.Log($"CharacterSpriteManager: Creating sprites as children of Canvas '{targetCanvas.name}' on '{targetCanvas.gameObject.name}'");
        
        // Create left sprite GameObject
        leftSpriteObject = new GameObject("CharacterSprite_Left");
        // CRITICAL: Explicitly set parent to the dialogue Canvas
        leftSpriteObject.transform.SetParent(targetCanvas.transform, false);
        
        // Verify parent is correct
        if (leftSpriteObject.transform.parent != targetCanvas.transform)
        {
            Debug.LogError($"CharacterSpriteManager: Failed to set parent! Expected '{targetCanvas.gameObject.name}', got '{leftSpriteObject.transform.parent?.gameObject.name}'");
        }
        else
        {
            Debug.Log($"CharacterSpriteManager: Left sprite parent confirmed: '{leftSpriteObject.transform.parent.gameObject.name}'");
        }
        
        // Force sprite to render behind dialogue by setting sibling index
        // Use SetAsFirstSibling or SetSiblingIndex to ensure it's at the calculated position
        if (targetIndex == 0)
        {
            leftSpriteObject.transform.SetAsFirstSibling();
        }
        else
        {
            leftSpriteObject.transform.SetSiblingIndex(targetIndex);
        }
        
        // Verify final position
        int finalLeftIndex = leftSpriteObject.transform.GetSiblingIndex();
        Debug.Log($"CharacterSpriteManager: Left sprite placed at sibling index: {finalLeftIndex} in Canvas '{targetCanvas.name}'");
        
        // Re-check dialogue UI index after placing left sprite (indices may have shifted)
        int dialogueUIIndexAfter = FindDialogueUIIndex();
        Debug.Log($"CharacterSpriteManager: Dialogue UI index after left sprite placement: {dialogueUIIndexAfter}");
        
        // Safety check: If dialogue UI exists and is at same or lower index, move sprite lower
        if (dialogueUIIndexAfter >= 0 && finalLeftIndex >= dialogueUIIndexAfter)
        {
            Debug.LogWarning($"CharacterSpriteManager: Sprite index {finalLeftIndex} is not before dialogue UI index {dialogueUIIndexAfter}. Moving sprite to index {dialogueUIIndexAfter - 1}");
            int newIndex = Mathf.Max(backgroundIndex + 1, dialogueUIIndexAfter - 1);
            leftSpriteObject.transform.SetSiblingIndex(newIndex);
            finalLeftIndex = leftSpriteObject.transform.GetSiblingIndex();
            Debug.Log($"CharacterSpriteManager: Left sprite moved to index: {finalLeftIndex}");
        }
        
        RectTransform leftRect = leftSpriteObject.AddComponent<RectTransform>();
        leftRect.anchorMin = new Vector2(0f, 0f);
        leftRect.anchorMax = new Vector2(0f, 0f);
        leftRect.pivot = new Vector2(0f, 0f);
        leftRect.anchoredPosition = bottomLeftOffset;
        leftRect.sizeDelta = spriteSize;
        
        Image leftImage = leftSpriteObject.AddComponent<Image>();
        leftImage.preserveAspect = true;
        leftSpriteObject.SetActive(false);
        
        // Create right sprite GameObject
        rightSpriteObject = new GameObject("CharacterSprite_Right");
        // CRITICAL: Explicitly set parent to the dialogue Canvas
        rightSpriteObject.transform.SetParent(targetCanvas.transform, false);
        
        // Verify parent is correct
        if (rightSpriteObject.transform.parent != targetCanvas.transform)
        {
            Debug.LogError($"CharacterSpriteManager: Failed to set parent! Expected '{targetCanvas.gameObject.name}', got '{rightSpriteObject.transform.parent?.gameObject.name}'");
        }
        else
        {
            Debug.Log($"CharacterSpriteManager: Right sprite parent confirmed: '{rightSpriteObject.transform.parent.gameObject.name}'");
        }
        
        // Set sibling index to match left sprite (right after left)
        int leftIndex = leftSpriteObject.transform.GetSiblingIndex();
        rightSpriteObject.transform.SetSiblingIndex(leftIndex + 1);
        int finalRightIndex = rightSpriteObject.transform.GetSiblingIndex();
        Debug.Log($"CharacterSpriteManager: Right sprite placed at sibling index: {finalRightIndex} in Canvas '{targetCanvas.name}'");
        
        // Log hierarchy AFTER creating sprites to verify final order
        LogCanvasHierarchy("AFTER sprite creation");
        
        // Final verification: Check all indices one more time
        int finalBackgroundIndex = FindBackgroundImageIndex();
        int finalDialogueUIIndex = FindDialogueUIIndex();
        int finalLeftSpriteIndex = leftSpriteObject.transform.GetSiblingIndex();
        int finalRightSpriteIndex = rightSpriteObject.transform.GetSiblingIndex();
        
        Debug.Log($"CharacterSpriteManager: FINAL INDICES - Background: {finalBackgroundIndex}, Left Sprite: {finalLeftSpriteIndex}, Right Sprite: {finalRightSpriteIndex}, Dialogue UI: {finalDialogueUIIndex}");
        
        // Verify order is correct
        if (finalBackgroundIndex >= 0 && finalLeftSpriteIndex <= finalBackgroundIndex)
        {
            Debug.LogError($"CharacterSpriteManager: ERROR - Left sprite index {finalLeftSpriteIndex} is NOT after background index {finalBackgroundIndex}!");
        }
        if (finalDialogueUIIndex >= 0 && finalRightSpriteIndex >= finalDialogueUIIndex)
        {
            Debug.LogError($"CharacterSpriteManager: ERROR - Right sprite index {finalRightSpriteIndex} is NOT before dialogue UI index {finalDialogueUIIndex}!");
        }
        
        RectTransform rightRect = rightSpriteObject.AddComponent<RectTransform>();
        rightRect.anchorMin = new Vector2(1f, 0f);
        rightRect.anchorMax = new Vector2(1f, 0f);
        rightRect.pivot = new Vector2(1f, 0f);
        rightRect.anchoredPosition = bottomRightOffset;
        rightRect.sizeDelta = spriteSize;
        
        Image rightImage = rightSpriteObject.AddComponent<Image>();
        rightImage.preserveAspect = true;
        rightSpriteObject.SetActive(false);
    }
    
    void OnNodeStarted(string nodeName)
    {
        Debug.Log($"CharacterSpriteManager: Node started: {nodeName}");
        
        if (dialogueRunner == null || dialogueRunner.Dialogue == null)
        {
            Debug.LogWarning("CharacterSpriteManager: DialogueRunner or Dialogue is null");
            return;
        }
        
        // Get tags for this node using the new API
        string tagsHeader = dialogueRunner.Dialogue.GetHeaderValue(nodeName, "tags");
        Debug.Log($"CharacterSpriteManager: Tags header: '{tagsHeader}'");
        
        if (string.IsNullOrEmpty(tagsHeader))
        {
            // No tags, hide all character sprites
            Debug.Log("CharacterSpriteManager: No tags found, hiding all characters");
            HideAllCharacters();
            return;
        }
        
        // Update current background from BackgroundCommandHandler
        UpdateCurrentBackground();
        
        // Split tags by spaces
        string[] tags = tagsHeader.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        
        // Extract character tags (starting with "char_")
        List<string> characterTags = new List<string>();
        foreach (string tag in tags)
        {
            // Remove # prefix if present
            string cleanTag = tag.StartsWith("#") ? tag.Substring(1) : tag;
            if (cleanTag.StartsWith("char_"))
            {
                characterTags.Add(cleanTag);
            }
        }
        
        Debug.Log($"CharacterSpriteManager: Extracted character tags (before auto-add): [{string.Join(", ", characterTags)}]");
        
        // Filter out characters without sprites (Player, DarkFigure)
        characterTags = characterTags.Where(tag => 
            characterTagToSpriteName.ContainsKey(tag) && 
            characterTagToSpriteName[tag] != null).ToList();
        
        // Auto-add Alice when appropriate
        bool aliceAdded = TryAutoAddAlice(characterTags);
        if (aliceAdded)
        {
            Debug.Log("CharacterSpriteManager: Auto-added Alice (player is present, no supervisor, not darkroom)");
        }
        else
        {
            Debug.Log("CharacterSpriteManager: Did not auto-add Alice - Supervisor present or darkroom or already in list");
        }
        
        Debug.Log($"CharacterSpriteManager: Final character tags: [{string.Join(", ", characterTags)}]");
        
        // Limit to 2 characters max
        if (characterTags.Count > 2)
        {
            characterTags = characterTags.Take(2).ToList();
            Debug.Log($"CharacterSpriteManager: Limited to 2 characters: [{string.Join(", ", characterTags)}]");
        }
        
        // Display characters (priority: left first, then right)
        DisplayCharacters(characterTags);
    }
    
    void UpdateCurrentBackground()
    {
        // Try to get current background from BackgroundCommandHandler
        if (backgroundHandler != null && backgroundHandler.backgroundImage != null && backgroundHandler.backgroundImage.sprite != null)
        {
            // Extract background name from sprite name (e.g., "bg_office" from sprite name)
            string spriteName = backgroundHandler.backgroundImage.sprite.name.ToLower();
            currentBackground = spriteName;
            Debug.Log($"CharacterSpriteManager: Current background updated to: {currentBackground}");
        }
    }
    
    bool TryAutoAddAlice(List<string> characterTags)
    {
        // Check if Alice is already in the list
        if (characterTags.Contains("char_Alice"))
        {
            return false; // Already present
        }
        
        // Check if Supervisor is present - if so, don't add Alice
        if (characterTags.Contains("char_Supervisor"))
        {
            return false; // Supervisor present, don't add Alice
        }
        
        // Check if we're in darkroom - if so, don't add Alice
        if (currentBackground.Contains("darkroom"))
        {
            return false; // Darkroom, don't add Alice
        }
        
        // Check if we have room (max 2 characters)
        if (characterTags.Count >= 2)
        {
            return false; // Already have 2 characters
        }
        
        // All conditions met - add Alice
        characterTags.Add("char_Alice");
        return true;
    }
    
    void DisplayCharacters(List<string> characterTags)
    {
        // Hide all first
        HideAllCharacters();
        
        if (characterTags == null || characterTags.Count == 0)
        {
            Debug.Log("CharacterSpriteManager: No characters to display");
            return;
        }
        
        // Display first character on left
        if (characterTags.Count >= 1)
        {
            string leftTag = characterTags[0];
            if (spriteDictionary.TryGetValue(leftTag, out Sprite leftSprite))
            {
                if (leftSpriteObject != null)
                {
                    Image leftImage = leftSpriteObject.GetComponent<Image>();
                    if (leftImage != null)
                    {
                        leftImage.sprite = leftSprite;
                        leftSpriteObject.SetActive(true);
                        Debug.Log($"CharacterSpriteManager: Displaying '{leftTag}' on left (sprite: {leftSprite.name})");
                    }
                }
            }
            else
            {
                Debug.LogWarning($"CharacterSpriteManager: No sprite found for character tag '{leftTag}' in dictionary. Available keys: [{string.Join(", ", spriteDictionary.Keys)}]");
            }
        }
        
        // Display second character on right
        if (characterTags.Count >= 2)
        {
            string rightTag = characterTags[1];
            if (spriteDictionary.TryGetValue(rightTag, out Sprite rightSprite))
            {
                if (rightSpriteObject != null)
                {
                    Image rightImage = rightSpriteObject.GetComponent<Image>();
                    if (rightImage != null)
                    {
                        rightImage.sprite = rightSprite;
                        rightSpriteObject.SetActive(true);
                        Debug.Log($"CharacterSpriteManager: Displaying '{rightTag}' on right (sprite: {rightSprite.name})");
                    }
                }
            }
            else
            {
                Debug.LogWarning($"CharacterSpriteManager: No sprite found for character tag '{rightTag}' in dictionary. Available keys: [{string.Join(", ", spriteDictionary.Keys)}]");
            }
        }
    }
    
    void HideAllCharacters()
    {
        if (leftSpriteObject != null)
        {
            leftSpriteObject.SetActive(false);
        }
        if (rightSpriteObject != null)
        {
            rightSpriteObject.SetActive(false);
        }
    }
    
    // Editor helper method
    void OnValidate()
    {
        // Rebuild dictionary when changes are made in the editor
        if (Application.isPlaying)
        {
            BuildDictionary();
        }
    }
}

