using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Yarn.Unity;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Handles background image commands from Yarn scripts.
/// Command: <<bg key>>
/// Supports both static backgrounds and animated sprite sheet backgrounds.
/// </summary>
public class BackgroundCommandHandler : MonoBehaviour
{
    [Header("Background Image")]
    [Tooltip("Image component that displays background images")]
    public Image backgroundImage;
    
    [Header("Background Sprites")]
    [Tooltip("Assign background sprites to their keys here (optional - will auto-load from Graphics/Backgrounds if empty)")]
    public List<SpriteEntry> backgroundSprites = new List<SpriteEntry>();
    
    [Header("Auto-Load Settings")]
    [Tooltip("Path to background sprites folder (relative to Assets/)")]
    public string backgroundFolderPath = "Graphics/Backgrounds";
    
    [Header("Animation Settings")]
    [Tooltip("Frames per second for animated backgrounds")]
    public float animationFPS = 10f;
    
    [Tooltip("Background keys that should be animated (e.g., bg_supervisoroffice)")]
    public List<string> animatedBackgrounds = new List<string> { "bg_supervisoroffice" };
    
    [Tooltip("Sprite sheet configuration for animated backgrounds")]
    public List<AnimatedBackgroundConfig> animatedBackgroundConfigs = new List<AnimatedBackgroundConfig>();
    
    // Cache dictionary for fast lookup
    private Dictionary<string, Sprite> spriteDictionary;
    
    // Animation state
    private Dictionary<string, Sprite[]> animatedSpriteDictionary = new Dictionary<string, Sprite[]>();
    private Coroutine currentAnimation;
    private string currentBackgroundKey;
    
    [System.Serializable]
    public class AnimatedBackgroundConfig
    {
        public string key;
        public int columns = 5;
        public int rows = 4;
        public int frameCount = 20;
        public float fps = 10f;
    }
    
    [System.Serializable]
    public class SpriteEntry
    {
        public string key;
        public Sprite sprite;
    }
    
    void Awake()
    {
        BuildDictionary();
        
        // Find Image component if not assigned
        if (backgroundImage == null)
        {
            backgroundImage = GetComponent<Image>();
            if (backgroundImage == null)
            {
                Debug.LogWarning("BackgroundCommandHandler: No Image component found. Please assign one in the Inspector.");
            }
        }
    }
    
    void BuildDictionary()
    {
        spriteDictionary = new Dictionary<string, Sprite>();
        animatedSpriteDictionary = new Dictionary<string, Sprite[]>();
        
        // First, add manually assigned sprites
        foreach (var entry in backgroundSprites)
        {
            if (entry.sprite != null && !string.IsNullOrEmpty(entry.key))
            {
                spriteDictionary[entry.key] = entry.sprite;
            }
        }
        
        // Then, try to auto-load from Resources or folder
        AutoLoadBackgrounds();
        
        // Build animated sprite arrays from sprite sheets
        BuildAnimatedSprites();
    }
    
    void BuildAnimatedSprites()
    {
#if UNITY_EDITOR
        string fullPath = "Assets/" + backgroundFolderPath;
        
        foreach (var config in animatedBackgroundConfigs)
        {
            string assetPath = $"{fullPath}/{config.key}.png";
            if (!System.IO.File.Exists(assetPath))
            {
                // Try without bg_ prefix
                string altKey = config.key.StartsWith("bg_") ? config.key.Substring(3) : config.key;
                assetPath = $"{fullPath}/{altKey}.png";
            }
            
            if (System.IO.File.Exists(assetPath))
            {
                // Load all sprites from the sprite sheet
                Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                List<Sprite> sprites = new List<Sprite>();
                
                foreach (Object asset in assets)
                {
                    if (asset is Sprite sprite && sprite.name != System.IO.Path.GetFileNameWithoutExtension(assetPath))
                    {
                        sprites.Add(sprite);
                    }
                }
                
                // Sort sprites by name (they should be named with indices)
                sprites.Sort((a, b) => {
                    // Extract numeric suffix from sprite names
                    int GetIndex(string name)
                    {
                        int idx = name.LastIndexOf('_');
                        if (idx >= 0 && int.TryParse(name.Substring(idx + 1), out int result))
                            return result;
                        return 0;
                    }
                    return GetIndex(a.name).CompareTo(GetIndex(b.name));
                });
                
                if (sprites.Count > 0)
                {
                    string key = config.key.ToLower();
                    animatedSpriteDictionary[key] = sprites.ToArray();
                    Debug.Log($"BackgroundCommandHandler: Loaded {sprites.Count} animation frames for '{key}'");
                }
            }
        }
#endif
    }
    
    void AutoLoadBackgrounds()
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
        // Resources.Load only works if the folder is actually named "Resources"
        string resourcesPath = backgroundFolderPath.Replace("Assets/", "").Replace("\\", "/");
        
        // Remove "Resources/" prefix if present, since Resources.LoadAll expects path relative to Resources folder
        if (resourcesPath.StartsWith("Resources/", System.StringComparison.OrdinalIgnoreCase))
        {
            resourcesPath = resourcesPath.Substring("Resources/".Length);
        }
        
        Sprite[] sprites = Resources.LoadAll<Sprite>(resourcesPath);
        
        if (sprites != null && sprites.Length > 0)
        {
            foreach (var sprite in sprites)
            {
                if (sprite != null)
                {
                    // Extract key from sprite name, handling sprite sheet suffixes like "_0"
                    string key = ExtractKeyFromSpriteName(sprite.name);
                    
                    // Only add if not already in dictionary (manual assignments take precedence)
                    if (!spriteDictionary.ContainsKey(key))
                    {
                        spriteDictionary[key] = sprite;
                        Debug.Log($"BackgroundCommandHandler: Auto-loaded background '{key}' from Resources (sprite: {sprite.name})");
                    }
                }
            }
        }
    }
    
#if UNITY_EDITOR
    void LoadFromFolder()
    {
        string fullPath = "Assets/" + backgroundFolderPath;
        
        if (!AssetDatabase.IsValidFolder(fullPath))
        {
            // Don't warn if Resources loading might work instead
            return;
        }
        
        // Find all texture files in the folder (not just sprites, since they might be imported as textures)
        string[] textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { fullPath });
        
        foreach (string guid in textureGuids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            
            // Extract key from file name (not sprite name)
            string fileName = System.IO.Path.GetFileNameWithoutExtension(assetPath).ToLower();
            string baseKey = ExtractKeyFromFileName(fileName);
            
            // Load all sprites from the texture (for sprite sheets)
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            List<Sprite> allSprites = new List<Sprite>();
            Sprite mainSprite = null;
            
            // Collect all sprites from the texture
            foreach (Object asset in assets)
            {
                if (asset is Sprite sprite)
                {
                    allSprites.Add(sprite);
                    // The "main" sprite has the same name as the file
                    if (sprite.name.Equals(fileName, System.StringComparison.OrdinalIgnoreCase))
                    {
                        mainSprite = sprite;
                    }
                }
            }
            
            // If this is a sprite sheet with multiple sprites, add all of them
            if (allSprites.Count > 1)
            {
                // Sort by frame index
                allSprites.Sort((a, b) => {
                    int GetIndex(string name)
                    {
                        int idx = name.LastIndexOf('_');
                        if (idx >= 0 && int.TryParse(name.Substring(idx + 1), out int result))
                            return result;
                        return 0;
                    }
                    return GetIndex(a.name).CompareTo(GetIndex(b.name));
                });
                
                // Add each sprite with its individual key
                foreach (var sprite in allSprites)
                {
                    string spriteKey = sprite.name.ToLower();
                    if (!spriteDictionary.ContainsKey(spriteKey))
                    {
                        spriteDictionary[spriteKey] = sprite;
                    }
                }
                
                // Also populate animatedSpriteDictionary for this sprite sheet
                if (!animatedSpriteDictionary.ContainsKey(baseKey))
                {
                    // Filter out the main texture sprite if present
                    var frameSprites = allSprites.Where(s => 
                        !s.name.Equals(fileName, System.StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (frameSprites.Length > 0)
                    {
                        animatedSpriteDictionary[baseKey] = frameSprites;
                        Debug.Log($"BackgroundCommandHandler: Loaded {frameSprites.Length} animation frames for '{baseKey}' from folder");
                    }
                }
                
                // Use first frame as the static fallback
                if (!spriteDictionary.ContainsKey(baseKey) && allSprites.Count > 0)
                {
                    spriteDictionary[baseKey] = allSprites[0];
                }
            }
            else if (allSprites.Count == 1)
            {
                // Single sprite - add normally
                if (!spriteDictionary.ContainsKey(baseKey))
                {
                    spriteDictionary[baseKey] = allSprites[0];
                    Debug.Log($"BackgroundCommandHandler: Auto-loaded background '{baseKey}' from {assetPath}");
                }
            }
            
        }
    }
#endif
    
    /// <summary>
    /// Extracts a background key from a sprite name, handling sprite sheet suffixes like "_0"
    /// </summary>
    string ExtractKeyFromSpriteName(string spriteName)
    {
        string key = spriteName.ToLower();
        
        // Remove sprite sheet suffixes like "_0", "_1", etc.
        if (System.Text.RegularExpressions.Regex.IsMatch(key, @"_\d+$"))
        {
            int lastUnderscore = key.LastIndexOf('_');
            if (lastUnderscore > 0)
            {
                string suffix = key.Substring(lastUnderscore + 1);
                if (int.TryParse(suffix, out _))
                {
                    key = key.Substring(0, lastUnderscore);
                }
            }
        }
        
        // Normalize key to match yarn format (bg_conferenceroom)
        if (!key.StartsWith("bg_"))
        {
            key = "bg_" + key;
        }
        
        return key;
    }
    
    /// <summary>
    /// Extracts a background key from a file name
    /// </summary>
    string ExtractKeyFromFileName(string fileName)
    {
        string key = fileName.ToLower();
        
        // Normalize key to match yarn format (bg_conferenceroom)
        if (!key.StartsWith("bg_"))
        {
            key = "bg_" + key;
        }
        
        return key;
    }
    
    /// <summary>
    /// Handles <<bg key>> commands from Yarn scripts
    /// Usage in Yarn: <<bg bg_office>>
    /// </summary>
    [YarnCommand("bg")]
    public void ChangeBackground(string key)
    {
        if (spriteDictionary == null)
        {
            BuildDictionary();
        }
        
        // Normalize the key to lowercase for matching
        string normalizedKey = key.ToLower();
        
        // Stop any existing animation
        if (currentAnimation != null)
        {
            StopCoroutine(currentAnimation);
            currentAnimation = null;
        }
        
        currentBackgroundKey = normalizedKey;
        
        // Check if this is an animated background
        if (IsAnimatedBackground(normalizedKey))
        {
            StartAnimatedBackground(normalizedKey);
            return;
        }
        
        // Try exact match first
        if (!spriteDictionary.TryGetValue(normalizedKey, out Sprite newSprite))
        {
            // Try case-insensitive match
            var caseInsensitiveMatch = spriteDictionary.FirstOrDefault(kvp => 
                kvp.Key.Equals(normalizedKey, System.StringComparison.OrdinalIgnoreCase));
            if (caseInsensitiveMatch.Value != null)
            {
                newSprite = caseInsensitiveMatch.Value;
            }
            else
            {
                // Try matching with sprite sheet suffixes (e.g., bg_conferenceroom_0)
                var spriteSheetMatch = spriteDictionary.FirstOrDefault(kvp =>
                {
                    string baseKey = ExtractKeyFromSpriteName(kvp.Key);
                    return baseKey.Equals(normalizedKey, System.StringComparison.OrdinalIgnoreCase);
                });
                if (spriteSheetMatch.Value != null)
                {
                    newSprite = spriteSheetMatch.Value;
                }
                else
                {
                    // Try partial match as last resort
                    var partialMatch = spriteDictionary.FirstOrDefault(kvp =>
                        kvp.Key.Contains(normalizedKey, System.StringComparison.OrdinalIgnoreCase) ||
                        normalizedKey.Contains(kvp.Key, System.StringComparison.OrdinalIgnoreCase));
                    if (partialMatch.Value != null)
                    {
                        newSprite = partialMatch.Value;
                        Debug.LogWarning($"Background Command: Using partial match for key '{key}' -> '{partialMatch.Key}'. Consider updating Yarn file to use exact key.");
                    }
                }
            }
        }
        
        if (newSprite != null)
        {
            if (backgroundImage != null)
            {
                backgroundImage.sprite = newSprite;
                Debug.Log($"Background: Changed to {key} (sprite: {newSprite.name})");
            }
            else
            {
                Debug.LogWarning($"Background Command: No Image component assigned for key '{key}'");
            }
        }
        else
        {
            Debug.LogWarning($"Background Command: No sprite found for key '{key}'. Available keys: {string.Join(", ", spriteDictionary.Keys.Take(10))}...");
        }
    }
    
    bool IsAnimatedBackground(string key)
    {
        // Check if key is in the animated backgrounds list
        foreach (var animKey in animatedBackgrounds)
        {
            if (animKey.Equals(key, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }
        
        // Also check if we have animation frames loaded for this key
        return animatedSpriteDictionary.ContainsKey(key);
    }
    
    void StartAnimatedBackground(string key)
    {
        // Try to get animation frames from cache
        if (!animatedSpriteDictionary.TryGetValue(key, out Sprite[] frames))
        {
            // Try to load frames dynamically from sprite dictionary
            frames = LoadAnimationFramesFromSprites(key);
            if (frames != null && frames.Length > 0)
            {
                animatedSpriteDictionary[key] = frames;
                Debug.Log($"Background: Cached {frames.Length} animation frames for '{key}'");
            }
        }
        
        if (frames != null && frames.Length > 0)
        {
            float fps = animationFPS;
            
            // Check for custom FPS in config
            var config = animatedBackgroundConfigs.FirstOrDefault(c => 
                c.key.Equals(key, System.StringComparison.OrdinalIgnoreCase));
            if (config != null)
            {
                fps = config.fps;
            }
            
            currentAnimation = StartCoroutine(AnimateBackground(frames, fps));
            Debug.Log($"Background: Started animation for {key} ({frames.Length} frames at {fps} FPS)");
        }
        else
        {
            // Fall back to static background if no animation frames found
            Debug.LogWarning($"Background: No animation frames found for {key}. SpriteDictionary has {spriteDictionary.Count} entries.");
            
            // Log available keys that might match
            var matchingKeys = spriteDictionary.Keys.Where(k => k.Contains(key.Replace("bg_", ""))).Take(5);
            Debug.Log($"Background: Similar keys in dictionary: {string.Join(", ", matchingKeys)}");
            
            // Try to display the first sprite that matches
            var firstMatch = spriteDictionary.FirstOrDefault(kvp => 
                kvp.Key.StartsWith(key + "_", System.StringComparison.OrdinalIgnoreCase));
            if (firstMatch.Value != null && backgroundImage != null)
            {
                backgroundImage.sprite = firstMatch.Value;
                Debug.Log($"Background: Using static fallback: {firstMatch.Key}");
            }
        }
    }
    
    Sprite[] LoadAnimationFramesFromSprites(string key)
    {
        // Find all sprites that match the key pattern with numeric suffix (e.g., bg_supervisoroffice_0, bg_supervisoroffice_1, etc.)
        var matchingSprites = new List<(int index, Sprite sprite)>();
        
        foreach (var kvp in spriteDictionary)
        {
            // Check if key starts with the base key + underscore
            if (kvp.Key.StartsWith(key + "_", System.StringComparison.OrdinalIgnoreCase))
            {
                // Extract the numeric suffix
                string suffix = kvp.Key.Substring(key.Length + 1);
                if (int.TryParse(suffix, out int frameIndex))
                {
                    matchingSprites.Add((frameIndex, kvp.Value));
                }
            }
        }
        
        if (matchingSprites.Count > 0)
        {
            // Sort by frame index and return sprites
            return matchingSprites.OrderBy(x => x.index).Select(x => x.sprite).ToArray();
        }
        
        return null;
    }
    
    IEnumerator AnimateBackground(Sprite[] frames, float fps)
    {
        int currentFrame = 0;
        float frameDelay = 1f / fps;
        
        while (true)
        {
            if (backgroundImage != null && frames.Length > 0)
            {
                backgroundImage.sprite = frames[currentFrame];
                currentFrame = (currentFrame + 1) % frames.Length;
            }
            yield return new WaitForSeconds(frameDelay);
        }
    }
    
    void OnDisable()
    {
        if (currentAnimation != null)
        {
            StopCoroutine(currentAnimation);
            currentAnimation = null;
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

