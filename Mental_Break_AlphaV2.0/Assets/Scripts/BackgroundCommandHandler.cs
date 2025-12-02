using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using Yarn.Unity;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Handles background image commands from Yarn scripts.
/// Supports both static images and animated video backgrounds.
/// Command: <<bg key>>
/// 
/// For WebGL compatibility, video files should be placed in StreamingAssets/Videos/
/// with the same naming convention as backgrounds (e.g., bg_supervisoroffice.mp4)
/// </summary>
public class BackgroundCommandHandler : MonoBehaviour
{
    [Header("Background Image")]
    [Tooltip("Image component that displays static background images")]
    public Image backgroundImage;
    
    [Header("Background Video")]
    [Tooltip("RawImage component that displays video backgrounds (created automatically if not assigned)")]
    public RawImage videoRawImage;
    
    [Tooltip("VideoPlayer component for playing animated backgrounds (created automatically if not assigned)")]
    public VideoPlayer videoPlayer;
    
    [Header("Background Sprites")]
    [Tooltip("Assign background sprites to their keys here (optional - will auto-load from Graphics/Backgrounds if empty)")]
    public List<SpriteEntry> backgroundSprites = new List<SpriteEntry>();
    
    [Header("Auto-Load Settings")]
    [Tooltip("Path to background sprites folder (relative to Assets/)")]
    public string backgroundFolderPath = "Graphics/Backgrounds";
    
    [Tooltip("Path to video files folder (relative to StreamingAssets/)")]
    public string videoFolderPath = "Videos";
    
    // Cache dictionaries for fast lookup
    private Dictionary<string, Sprite> spriteDictionary;
    private HashSet<string> availableVideoKeys; // Keys that have video files available
    
    // RenderTexture for video playback (created lazily)
    private RenderTexture videoRenderTexture;
    
    // Track current background state
    private bool isPlayingVideo = false;
    private string currentVideoKey = "";
    
    // Video preparation state
    private bool isVideoPreparing = false;
    
    // Lazy initialization flag for video system
    private bool videoSystemInitialized = false;
    
    // Known video backgrounds - always try video first for these keys (even in WebGL)
    // Add background keys here when you have video versions available
    private static readonly HashSet<string> knownVideoBackgrounds = new HashSet<string>
    {
        "bg_supervisoroffice"
        // Add more animated backgrounds here as needed
    };
    
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
        
        // Start scanning for available videos (lightweight, no heavy allocations)
        StartCoroutine(ScanForVideos());
        
        // NOTE: Video system is NOT initialized here - it's done lazily when first video is requested
        // This reduces memory pressure on initial load, especially for WebGL
    }
    
    /// <summary>
    /// Lazily initializes the video playback system when first needed.
    /// This avoids creating VideoPlayer and RenderTexture at startup.
    /// </summary>
    void EnsureVideoSystemInitialized()
    {
        if (videoSystemInitialized) return;
        
        Debug.Log("BackgroundCommandHandler: Initializing video system (lazy init)");
        
        // Create or find VideoPlayer if not assigned
        if (videoPlayer == null)
        {
            videoPlayer = GetComponent<VideoPlayer>();
            if (videoPlayer == null)
            {
                videoPlayer = gameObject.AddComponent<VideoPlayer>();
            }
        }
        
        // Configure VideoPlayer for URL-based playback (WebGL compatible)
        videoPlayer.source = VideoSource.Url;
        videoPlayer.isLooping = true;
        videoPlayer.audioOutputMode = VideoAudioOutputMode.None; // Muted
        videoPlayer.playOnAwake = false;
        videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        videoPlayer.skipOnDrop = true; // Better performance for WebGL
        
        // Subscribe to video events
        videoPlayer.prepareCompleted += OnVideoPrepared;
        videoPlayer.errorReceived += OnVideoError;
        
        // NOTE: RenderTexture is NOT created here - it's created in OnVideoPrepared
        // at the actual video dimensions to minimize memory usage
        
        // Setup RawImage for video display if not assigned
        if (videoRawImage == null)
        {
            // Try to find an existing RawImage sibling
            videoRawImage = transform.parent?.GetComponentInChildren<RawImage>();
            
            if (videoRawImage == null && backgroundImage != null)
            {
                // Create a RawImage as a sibling to the Image
                GameObject videoObj = new GameObject("VideoBackground");
                videoObj.transform.SetParent(backgroundImage.transform.parent, false);
                
                // Copy RectTransform settings from background image
                RectTransform imgRect = backgroundImage.GetComponent<RectTransform>();
                RectTransform videoRect = videoObj.AddComponent<RectTransform>();
                videoRect.anchorMin = imgRect.anchorMin;
                videoRect.anchorMax = imgRect.anchorMax;
                videoRect.offsetMin = imgRect.offsetMin;
                videoRect.offsetMax = imgRect.offsetMax;
                videoRect.sizeDelta = imgRect.sizeDelta;
                videoRect.anchoredPosition = imgRect.anchoredPosition;
                
                // Place it behind the image in hierarchy (but same sibling order logic)
                videoObj.transform.SetSiblingIndex(backgroundImage.transform.GetSiblingIndex());
                
                videoRawImage = videoObj.AddComponent<RawImage>();
            }
        }
        
        if (videoRawImage != null)
        {
            videoRawImage.enabled = false; // Start hidden
        }
        
        videoSystemInitialized = true;
    }
    
    /// <summary>
    /// Scans StreamingAssets/Videos for available video files
    /// </summary>
    IEnumerator ScanForVideos()
    {
        availableVideoKeys = new HashSet<string>();
        
        string basePath = System.IO.Path.Combine(Application.streamingAssetsPath, videoFolderPath);
        
#if UNITY_EDITOR
        // In editor, we can directly check the file system
        if (System.IO.Directory.Exists(basePath))
        {
            string[] videoFiles = System.IO.Directory.GetFiles(basePath, "*.mp4");
            foreach (string filePath in videoFiles)
            {
                string fileName = System.IO.Path.GetFileNameWithoutExtension(filePath).ToLower();
                string key = ExtractKeyFromFileName(fileName);
                availableVideoKeys.Add(key);
                Debug.Log($"BackgroundCommandHandler: Found video for '{key}' at {filePath}");
            }
        }
#else
        // For WebGL/builds, we need to check via web request or use a manifest
        // For now, we'll try to load videos on demand and cache results
        Debug.Log("BackgroundCommandHandler: Running in build mode, videos will be checked on demand");
#endif
        
        yield return null;
    }
    
    void BuildDictionary()
    {
        spriteDictionary = new Dictionary<string, Sprite>();
        
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
            string key = ExtractKeyFromFileName(fileName);
            
            // Skip if already in dictionary (manual assignments take precedence)
            if (spriteDictionary.ContainsKey(key))
            {
                continue;
            }
            
            // Load the sprite - try to get all sprites from the texture (for sprite sheets)
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            Sprite targetSprite = null;
            
            // Find the first sprite from the texture
            foreach (Object asset in assets)
            {
                if (asset is Sprite sprite)
                {
                    targetSprite = sprite;
                    break; // Use the first sprite found (usually the main one)
                }
            }
            
            if (targetSprite != null)
            {
                spriteDictionary[key] = targetSprite;
                Debug.Log($"BackgroundCommandHandler: Auto-loaded background '{key}' from {assetPath} (sprite: {targetSprite.name})");
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
    /// Gets the URL for a video file in StreamingAssets
    /// </summary>
    string GetVideoUrl(string key)
    {
        string fileName = key + ".mp4";
        
#if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL needs forward slashes for URLs - Path.Combine uses backslashes on Windows
        return Application.streamingAssetsPath + "/" + videoFolderPath + "/" + fileName;
#else
        // On other platforms, use Path.Combine and add file:// prefix
        string path = System.IO.Path.Combine(Application.streamingAssetsPath, videoFolderPath, fileName);
        return "file://" + path;
#endif
    }
    
    /// <summary>
    /// Checks if a video exists for the given key
    /// </summary>
    bool HasVideo(string key)
    {
        if (availableVideoKeys != null && availableVideoKeys.Contains(key))
        {
            return true;
        }
        
#if UNITY_EDITOR
        // In editor, also check file system directly
        string path = System.IO.Path.Combine(Application.streamingAssetsPath, videoFolderPath, key + ".mp4");
        return System.IO.File.Exists(path);
#else
        // In builds, we'll try to load and see if it works
        return availableVideoKeys != null && availableVideoKeys.Contains(key);
#endif
    }
    
    /// <summary>
    /// Handles <<bg key>> commands from Yarn scripts
    /// Usage in Yarn: <<bg bg_office>>
    /// Automatically uses video if available, otherwise falls back to static image
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
        
        // Check for video first (video takes precedence)
        if (HasVideo(normalizedKey))
        {
            PlayVideoBackground(normalizedKey);
            return;
        }
        
        // For known video backgrounds, always try video first (even in WebGL where we can't pre-scan)
        // This allows video playback to work in builds where HasVideo() returns false
        if (knownVideoBackgrounds.Contains(normalizedKey))
        {
            Debug.Log($"BackgroundCommandHandler: '{normalizedKey}' is a known video background, trying video first");
            TryPlayVideoWithFallback(normalizedKey);
            return;
        }
        
        // Fall back to static image
        Sprite newSprite = FindSprite(normalizedKey);
        if (newSprite != null)
        {
            ShowStaticBackground(newSprite, key);
        }
        else
        {
            // Try video anyway (for WebGL where we can't pre-scan)
            TryPlayVideoWithFallback(normalizedKey);
        }
    }
    
    /// <summary>
    /// Attempts to play video, falling back to static image if video doesn't exist
    /// </summary>
    void TryPlayVideoWithFallback(string key)
    {
        // Lazily initialize video system when first needed
        EnsureVideoSystemInitialized();
        
        string url = GetVideoUrl(key);
        
        // Store the key for fallback handling
        currentVideoKey = key;
        isVideoPreparing = true;
        
        videoPlayer.url = url;
        videoPlayer.Prepare();
        
        // The OnVideoPrepared or OnVideoError callbacks will handle the result
    }
    
    void OnVideoPrepared(VideoPlayer vp)
    {
        if (!isVideoPreparing) return;
        isVideoPreparing = false;
        
        // Video is ready, show it
        if (availableVideoKeys == null)
        {
            availableVideoKeys = new HashSet<string>();
        }
        availableVideoKeys.Add(currentVideoKey);
        
        // Create or update RenderTexture to match actual video dimensions (lazy creation)
        int videoWidth = (int)vp.width;
        int videoHeight = (int)vp.height;
        
        // Ensure minimum dimensions
        if (videoWidth <= 0) videoWidth = 1280;
        if (videoHeight <= 0) videoHeight = 720;
        
        if (videoRenderTexture == null || 
            videoRenderTexture.width != videoWidth || 
            videoRenderTexture.height != videoHeight)
        {
            if (videoRenderTexture != null)
            {
                videoRenderTexture.Release();
                Destroy(videoRenderTexture);
            }
            
            Debug.Log($"BackgroundCommandHandler: Creating RenderTexture at {videoWidth}x{videoHeight} for video");
            videoRenderTexture = new RenderTexture(videoWidth, videoHeight, 0);
            videoRenderTexture.Create();
            videoPlayer.targetTexture = videoRenderTexture;
            
            if (videoRawImage != null)
            {
                videoRawImage.texture = videoRenderTexture;
            }
        }
        
        // Hide static image, show video (disable components, not GameObjects, to keep VideoPlayer active)
        if (backgroundImage != null)
        {
            backgroundImage.enabled = false;
        }
        if (videoRawImage != null)
        {
            videoRawImage.texture = videoRenderTexture;
            videoRawImage.enabled = true;
        }
        
        // Ensure VideoPlayer is enabled before playing
        if (!vp.enabled)
        {
            vp.enabled = true;
        }
        
        // Play the video
        if (vp.gameObject.activeInHierarchy)
        {
            vp.Play();
            isPlayingVideo = true;
            Debug.Log($"Background: Playing video for {currentVideoKey}");
        }
        else
        {
            Debug.LogWarning($"BackgroundCommandHandler: Cannot play video - VideoPlayer GameObject is not active. Key: {currentVideoKey}");
            // Fall back to static image
            Sprite sprite = FindSprite(currentVideoKey);
            if (sprite != null)
            {
                ShowStaticBackground(sprite, currentVideoKey);
            }
        }
    }
    
    void OnVideoError(VideoPlayer vp, string message)
    {
        if (!isVideoPreparing) return;
        isVideoPreparing = false;
        
        Debug.Log($"BackgroundCommandHandler: No video available for '{currentVideoKey}', falling back to static image. ({message})");
        
        // Fall back to static image
        Sprite sprite = FindSprite(currentVideoKey);
        if (sprite != null)
        {
            ShowStaticBackground(sprite, currentVideoKey);
        }
        else
        {
            Debug.LogWarning($"Background Command: No sprite or video found for key '{currentVideoKey}'. Available sprite keys: {string.Join(", ", spriteDictionary.Keys.Take(10))}...");
        }
    }
    
    Sprite FindSprite(string normalizedKey)
    {
        // Try exact match first
        if (spriteDictionary.TryGetValue(normalizedKey, out Sprite sprite))
        {
            return sprite;
        }
        
        // Try case-insensitive match
        var caseInsensitiveMatch = spriteDictionary.FirstOrDefault(kvp => 
            kvp.Key.Equals(normalizedKey, System.StringComparison.OrdinalIgnoreCase));
        if (caseInsensitiveMatch.Value != null)
        {
            return caseInsensitiveMatch.Value;
        }
        
        // Try matching with sprite sheet suffixes (e.g., bg_conferenceroom_0)
        var spriteSheetMatch = spriteDictionary.FirstOrDefault(kvp =>
        {
            string baseKey = ExtractKeyFromSpriteName(kvp.Key);
            return baseKey.Equals(normalizedKey, System.StringComparison.OrdinalIgnoreCase);
        });
        if (spriteSheetMatch.Value != null)
        {
            return spriteSheetMatch.Value;
        }
        
        // Try partial match as last resort
        var partialMatch = spriteDictionary.FirstOrDefault(kvp =>
            kvp.Key.Contains(normalizedKey, System.StringComparison.OrdinalIgnoreCase) ||
            normalizedKey.Contains(kvp.Key, System.StringComparison.OrdinalIgnoreCase));
        if (partialMatch.Value != null)
        {
            Debug.LogWarning($"Background Command: Using partial match for key '{normalizedKey}' -> '{partialMatch.Key}'. Consider updating Yarn file to use exact key.");
            return partialMatch.Value;
        }
        
        return null;
    }
    
    void PlayVideoBackground(string key)
    {
        // Lazily initialize video system when first needed
        EnsureVideoSystemInitialized();
        
        // Stop any current video
        if (videoPlayer != null && videoPlayer.isPlaying)
        {
            videoPlayer.Stop();
        }
        
        currentVideoKey = key;
        string url = GetVideoUrl(key);
        
        Debug.Log($"BackgroundCommandHandler: Loading video from URL: {url}");
        
        isVideoPreparing = true;
        videoPlayer.url = url;
        videoPlayer.Prepare();
        
        // The OnVideoPrepared callback will handle showing the video
    }
    
    void ShowStaticBackground(Sprite sprite, string key)
    {
        // Stop any playing video
        if (videoPlayer != null && videoPlayer.isPlaying)
        {
            videoPlayer.Stop();
        }
        
        // Hide video, show static image (disable components, not GameObjects)
        if (videoRawImage != null)
        {
            videoRawImage.enabled = false;
        }
        if (backgroundImage != null)
        {
            backgroundImage.enabled = true;
            backgroundImage.sprite = sprite;
        }
        
        isPlayingVideo = false;
        
        Debug.Log($"Background: Changed to {key} (sprite: {sprite.name})");
    }
    
    void OnDestroy()
    {
        // Unsubscribe from events
        if (videoPlayer != null)
        {
            videoPlayer.prepareCompleted -= OnVideoPrepared;
            videoPlayer.errorReceived -= OnVideoError;
        }
        
        // Clean up RenderTexture
        if (videoRenderTexture != null)
        {
            videoRenderTexture.Release();
            Destroy(videoRenderTexture);
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
