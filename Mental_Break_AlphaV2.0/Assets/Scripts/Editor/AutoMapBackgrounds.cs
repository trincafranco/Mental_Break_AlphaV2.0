using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;

/// <summary>
/// Editor tool to automatically map background sprites from Assets/Graphics/Backgrounds/ to BackgroundCommandHandler.
/// Access via: Tools > Yarn Spinner > Auto-Map Backgrounds
/// </summary>
public class AutoMapBackgrounds : EditorWindow
{
    [MenuItem("Tools/Yarn Spinner/Auto-Map Backgrounds")]
    public static void ShowWindow()
    {
        GetWindow<AutoMapBackgrounds>("Auto-Map Backgrounds");
    }

    void OnGUI()
    {
        GUILayout.Label("Background Sprite Auto-Mapping", EditorStyles.boldLabel);
        GUILayout.Space(10);

        GUILayout.Label("This will automatically map background sprites from Assets/Graphics/Backgrounds/");
        GUILayout.Label("to BackgroundCommandHandler based on filenames (bg_*.png).");
        GUILayout.Space(10);

        if (GUILayout.Button("Auto-Map Background Sprites", GUILayout.Height(30)))
        {
            MapBackgroundSprites();
        }

        GUILayout.Space(20);
        GUILayout.Label("Animated Backgrounds", EditorStyles.boldLabel);
        GUILayout.Space(5);
        
        if (GUILayout.Button("Setup Animated Sprite Sheets", GUILayout.Height(30)))
        {
            SetupAnimatedSpriteSheets();
        }
        
        GUILayout.Space(5);
        GUILayout.Label("This configures sprite sheets for animation (bg_supervisoroffice, etc.)");

        GUILayout.Space(10);
        GUILayout.Label("Note: Images must be configured as Sprites in Unity import settings.");
        GUILayout.Label("Review the mappings in the BackgroundCommandHandler Inspector.");
    }
    
    [MenuItem("Tools/Yarn Spinner/Setup Animated Backgrounds")]
    public static void SetupAnimatedSpriteSheets()
    {
        // Configuration for animated sprite sheets
        var animatedSheets = new[]
        {
            new { path = "Assets/Graphics/Backgrounds/bg_supervisoroffice.png", columns = 5, rows = 4, frameCount = 20 }
        };
        
        foreach (var sheet in animatedSheets)
        {
            if (!System.IO.File.Exists(sheet.path))
            {
                Debug.LogWarning($"Animated sprite sheet not found: {sheet.path}");
                continue;
            }
            
            TextureImporter importer = AssetImporter.GetAtPath(sheet.path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError($"Could not get TextureImporter for: {sheet.path}");
                continue;
            }
            
            // Configure as sprite sheet
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.filterMode = FilterMode.Point; // Pixel art friendly
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            
            // Get texture size
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(sheet.path);
            if (texture == null)
            {
                Debug.LogError($"Could not load texture: {sheet.path}");
                continue;
            }
            
            int frameWidth = texture.width / sheet.columns;
            int frameHeight = texture.height / sheet.rows;
            
            // Create sprite rects for each frame
            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var dataProvider = factory.GetSpriteEditorDataProviderFromObject(importer);
            dataProvider.InitSpriteEditorDataProvider();
            
            List<SpriteRect> spriteRects = new List<SpriteRect>();
            string baseName = System.IO.Path.GetFileNameWithoutExtension(sheet.path);
            
            int frameIndex = 0;
            for (int row = 0; row < sheet.rows; row++)
            {
                for (int col = 0; col < sheet.columns; col++)
                {
                    if (frameIndex >= sheet.frameCount) break;
                    
                    var rect = new SpriteRect
                    {
                        name = $"{baseName}_{frameIndex}",
                        spriteID = GUID.Generate(), // Required for proper sprite creation
                        rect = new Rect(
                            col * frameWidth,
                            texture.height - (row + 1) * frameHeight, // Unity uses bottom-left origin
                            frameWidth,
                            frameHeight
                        ),
                        alignment = SpriteAlignment.Center,
                        pivot = new Vector2(0.5f, 0.5f)
                    };
                    spriteRects.Add(rect);
                    frameIndex++;
                }
            }
            
            dataProvider.SetSpriteRects(spriteRects.ToArray());
            dataProvider.Apply();
            
            // Write changes to the importer and reimport
            var assetImporterEditor = Editor.CreateEditor(importer);
            assetImporterEditor.serializedObject.ApplyModifiedProperties();
            DestroyImmediate(assetImporterEditor);
            
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
            
            Debug.Log($"✅ Configured animated sprite sheet: {sheet.path} ({sheet.columns}x{sheet.rows} = {sheet.frameCount} frames)");
        }
        
        // Also configure the BackgroundCommandHandler with animation settings
        ConfigureAnimationHandler();
        
        Debug.Log("Animated sprite sheet setup complete!");
    }
    
    private static void ConfigureAnimationHandler()
    {
        BackgroundCommandHandler handler = FindAnyObjectByType<BackgroundCommandHandler>();
        if (handler == null)
        {
            Debug.LogWarning("BackgroundCommandHandler not found. Open the GameScene to configure animation settings.");
            return;
        }
        
        // Add/update animated background configs
        bool hasSupervisorConfig = false;
        foreach (var config in handler.animatedBackgroundConfigs)
        {
            if (config.key == "bg_supervisoroffice")
            {
                hasSupervisorConfig = true;
                config.columns = 5;
                config.rows = 4;
                config.frameCount = 20;
                config.fps = 10f;
                break;
            }
        }
        
        if (!hasSupervisorConfig)
        {
            handler.animatedBackgroundConfigs.Add(new BackgroundCommandHandler.AnimatedBackgroundConfig
            {
                key = "bg_supervisoroffice",
                columns = 5,
                rows = 4,
                frameCount = 20,
                fps = 10f
            });
        }
        
        // Ensure bg_supervisoroffice is in animated backgrounds list
        if (!handler.animatedBackgrounds.Contains("bg_supervisoroffice"))
        {
            handler.animatedBackgrounds.Add("bg_supervisoroffice");
        }
        
        EditorUtility.SetDirty(handler);
        Debug.Log("✅ Configured BackgroundCommandHandler animation settings");
    }

    private static void MapBackgroundSprites()
    {
        // Find BackgroundCommandHandler
        BackgroundCommandHandler handler = FindAnyObjectByType<BackgroundCommandHandler>();
        if (handler == null)
        {
            Debug.LogError("BackgroundCommandHandler not found! Please ensure GameScene is open and BackgroundCommandHandler exists.");
            return;
        }

        // Find all textures in Assets/Graphics/Backgrounds/
        string backgroundsPath = "Assets/Graphics/Backgrounds";
        string[] textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { backgroundsPath });
        
        if (textureGuids.Length == 0)
        {
            Debug.LogWarning($"No textures found in {backgroundsPath}");
            return;
        }

        // Build dictionary of existing sprites by key
        Dictionary<string, BackgroundCommandHandler.SpriteEntry> existingEntries = 
            handler.backgroundSprites.ToDictionary(e => e?.key ?? "", e => e);

        int mappedCount = 0;
        int existingCount = 0;

        // Load all textures and convert to sprites
        Dictionary<string, Sprite> availableSprites = new Dictionary<string, Sprite>();
        
        foreach (string guid in textureGuids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            
            // Try to load as Sprite first
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sprite != null)
            {
                string spriteName = sprite.name.ToLower();
                availableSprites[spriteName] = sprite;
            }
            else
            {
                // Try loading as Texture2D and check if it's configured as Sprite
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (texture != null)
                {
                    TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                    if (importer != null && importer.textureType == TextureImporterType.Sprite)
                    {
                        // Try to load sprite directly
                        sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                        if (sprite != null)
                        {
                            string spriteName = sprite.name.ToLower();
                            availableSprites[spriteName] = sprite;
                        }
                        else
                        {
                            Debug.LogWarning($"Texture {assetPath} is configured as Sprite but couldn't load as Sprite. You may need to reimport it.");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"Texture {assetPath} is not configured as Sprite. Setting to Sprite (2D) mode...");
                        // Auto-configure as sprite
                        if (importer != null)
                        {
                            importer.textureType = TextureImporterType.Sprite;
                            importer.spriteImportMode = SpriteImportMode.Single;
                            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                            
                            sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                            if (sprite != null)
                            {
                                string spriteName = sprite.name.ToLower();
                                availableSprites[spriteName] = sprite;
                                Debug.Log($"✅ Converted {assetPath} to Sprite");
                            }
                        }
                    }
                }
            }
        }

        // Map background keys from Yarn files (bg_*)
        Dictionary<string, string[]> backgroundMappings = new Dictionary<string, string[]>
        {
            // Key: abstract bg key, Value: array of filename patterns to match
            { "bg_office", new[] { "bg_office", "office" } },
            { "bg_tribunal", new[] { "bg_tribunal", "tribunal" } },
            { "bg_triage", new[] { "bg_triage", "triage" } },
            { "bg_museum", new[] { "bg_museum", "museum" } },
            { "bg_mosaic", new[] { "bg_mosaic", "mosaic" } },
            { "bg_mailroom", new[] { "bg_mailroom", "mailroom" } },
            { "bg_hallway", new[] { "bg_hallway", "hallway" } },
            { "bg_darkroom", new[] { "bg_darkroom", "darkroom" } },
            { "bg_backend", new[] { "bg_backend", "backend" } }
        };

        // Try to map each background key
        foreach (var mapping in backgroundMappings)
        {
            string abstractKey = mapping.Key;
            string[] patterns = mapping.Value;

            // Check if already mapped
            if (existingEntries.ContainsKey(abstractKey) && existingEntries[abstractKey].sprite != null)
            {
                existingCount++;
                continue;
            }

            // Try to find matching sprite
            Sprite matchedSprite = null;
            foreach (string pattern in patterns)
            {
                string patternLower = pattern.ToLower();
                
                // Try exact match
                if (availableSprites.TryGetValue(patternLower, out matchedSprite))
                {
                    break;
                }

                // Try partial match (sprite name contains pattern)
                var partialMatch = availableSprites.FirstOrDefault(kvp =>
                    kvp.Key.Contains(patternLower) || patternLower.Contains(kvp.Key)
                );
                if (partialMatch.Value != null)
                {
                    matchedSprite = partialMatch.Value;
                    Debug.Log($"Partial background match: {abstractKey} -> {partialMatch.Key}");
                    break;
                }

                // Try fuzzy match
                var fuzzyMatch = availableSprites.FirstOrDefault(kvp =>
                    ContainsSimilarWords(kvp.Key, patternLower)
                );
                if (fuzzyMatch.Value != null)
                {
                    matchedSprite = fuzzyMatch.Value;
                    Debug.Log($"Fuzzy background match: {abstractKey} -> {fuzzyMatch.Key}");
                    break;
                }
            }

            if (matchedSprite != null)
            {
                // Add or update entry
                if (existingEntries.ContainsKey(abstractKey))
                {
                    existingEntries[abstractKey].sprite = matchedSprite;
                }
                else
                {
                    handler.backgroundSprites.Add(new BackgroundCommandHandler.SpriteEntry
                    {
                        key = abstractKey,
                        sprite = matchedSprite
                    });
                }
                mappedCount++;
                Debug.Log($"✅ Mapped background {abstractKey} -> {matchedSprite.name}");
            }
            else
            {
                Debug.LogWarning($"⚠️ Could not find suitable sprite for background key '{abstractKey}'. Patterns tried: {string.Join(", ", patterns)}");
            }
        }

        // Also try to auto-map any remaining bg_*.png files
        foreach (var spritePair in availableSprites)
        {
            string spriteKey = spritePair.Key;
            
            // If it starts with bg_, try to use it directly
            if (spriteKey.StartsWith("bg_") && 
                !handler.backgroundSprites.Any(e => e != null && e.key == spriteKey))
            {
                handler.backgroundSprites.Add(new BackgroundCommandHandler.SpriteEntry
                {
                    key = spriteKey,
                    sprite = spritePair.Value
                });
                mappedCount++;
                Debug.Log($"✅ Auto-mapped background: {spriteKey} -> {spritePair.Value.name}");
            }
        }

        // Mark scene as dirty
        EditorUtility.SetDirty(handler);
        if (handler.gameObject.scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(handler.gameObject.scene);
        }

        Debug.Log($"Background Mapping Complete: {mappedCount} new mappings, {existingCount} already mapped.");
    }

    private static bool ContainsSimilarWords(string key1, string key2)
    {
        string[] words1 = key1.Split(new[] { '_', ' ', '-' }).Where(w => w.Length > 2).ToArray();
        string[] words2 = key2.Split(new[] { '_', ' ', '-' }).Where(w => w.Length > 2).ToArray();

        int matches = words1.Count(w1 => words2.Any(w2 =>
            w1.Equals(w2, System.StringComparison.OrdinalIgnoreCase) ||
            w1.Contains(w2, System.StringComparison.OrdinalIgnoreCase) ||
            w2.Contains(w1, System.StringComparison.OrdinalIgnoreCase)
        ));

        return matches > 0 && matches >= Mathf.Min(words1.Length, words2.Length) / 2;
    }
}

