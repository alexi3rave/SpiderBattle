using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using WormCrawlerPrototype;

public sealed class DecorPrefabGenerator : EditorWindow
{
    private const string DefaultSpritesFolder = "Assets/Sprites/Decorations/Default";
    private const string DefaultOutputPrefabsFolder = "Assets/Resources/Decorations/Default";

    [SerializeField] private string spritesFolder = DefaultSpritesFolder;
    [SerializeField] private string outputPrefabsFolder = DefaultOutputPrefabsFolder;
    [SerializeField] private int spritePixelsPerUnit = 32;
    [SerializeField] private bool deleteExistingPrefabs = true;
    [SerializeField] private List<DecorSpriteEntry> defaultDecorEntries;

    [MenuItem("Tools/Generate Decoration Prefabs")]
    public static void ShowWindow()
    {
        GetWindow<DecorPrefabGenerator>("Decor Gen");
    }

    private void OnEnable()
    {
        defaultDecorEntries ??= new List<DecorSpriteEntry>();
        if (defaultDecorEntries.Count == 0)
        {
            ScanSpritesAndBuildConfig();
        }
    }

    private void OnGUI()
    {
        GUILayout.Label("Decoration Prefab Generator", EditorStyles.boldLabel);

        spritesFolder = EditorGUILayout.TextField("Sprites Folder", spritesFolder);
        outputPrefabsFolder = EditorGUILayout.TextField("Output Prefabs Folder", outputPrefabsFolder);
        spritePixelsPerUnit = EditorGUILayout.IntField("Pixels Per Unit", spritePixelsPerUnit);
        deleteExistingPrefabs = EditorGUILayout.Toggle("Delete Existing Prefabs", deleteExistingPrefabs);

        GUILayout.Space(8);

        if (GUILayout.Button("Scan Sprites & Build Config", GUILayout.Height(28)))
        {
            ScanSpritesAndBuildConfig();
        }

        if (GUILayout.Button("Normalize Decoration Sprites", GUILayout.Height(28)))
        {
            NormalizeDecorationSprites();
        }

        if (GUILayout.Button("Generate Prefabs From Sprites", GUILayout.Height(40)))
        {
            GeneratePrefabsFromSprites();
        }

        GUILayout.Space(10);

        DrawEntriesUI();

        GUILayout.Space(10);
        EditorGUILayout.HelpBox(
            "Workflow:\n" +
            "1) Put PNGs into Assets/Sprites/Decorations/Default/ (e.g. rock_small_01.png, tree_medium_01.png, house_large.png).\n" +
            "2) Scan Sprites & Build Config.\n" +
            "3) Normalize Decoration Sprites (sets Sprite import settings, Point filter, PPU, pivot bottom-center).\n" +
            "4) Generate Prefabs From Sprites (creates prefabs in Assets/Resources/Decorations/Default/).",
            MessageType.Info);
    }

    private void DrawEntriesUI()
    {
        GUILayout.Label($"Entries: {(defaultDecorEntries == null ? 0 : defaultDecorEntries.Count)}", EditorStyles.boldLabel);
        if (defaultDecorEntries == null)
        {
            return;
        }

        var so = new SerializedObject(this);
        var prop = so.FindProperty("defaultDecorEntries");
        EditorGUILayout.PropertyField(prop, includeChildren: true);
        so.ApplyModifiedProperties();
    }

    private void ScanSpritesAndBuildConfig()
    {
        defaultDecorEntries ??= new List<DecorSpriteEntry>();
        defaultDecorEntries.Clear();

        if (string.IsNullOrWhiteSpace(spritesFolder) || !AssetDatabase.IsValidFolder(spritesFolder))
        {
            Debug.LogWarning($"[DecorGen] Sprites folder not found: '{spritesFolder}'");
            return;
        }

        var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { spritesFolder });
        for (var i = 0; i < guids.Length; i++)
        {
            var path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var filename = Path.GetFileNameWithoutExtension(path);
            var size = InferSizeFromName(filename);
            var (minSlope, maxSlope) = DefaultSlopeRange(size);
            defaultDecorEntries.Add(new DecorSpriteEntry
            {
                spritePath = path,
                size = size,
                minSlopeDeg = minSlope,
                maxSlopeDeg = maxSlope,
                verticalOffset = 0f,
            });
        }

        Debug.Log($"[DecorGen] Scan complete. Found {defaultDecorEntries.Count} sprites in '{spritesFolder}'");
    }

    private void NormalizeDecorationSprites()
    {
        if (defaultDecorEntries == null || defaultDecorEntries.Count == 0)
        {
            Debug.LogWarning("[DecorGen] No entries to normalize. Run 'Scan Sprites & Build Config' first.");
            return;
        }

        var ppu = Mathf.Max(1, spritePixelsPerUnit);
        for (var i = 0; i < defaultDecorEntries.Count; i++)
        {
            var p = defaultDecorEntries[i].spritePath;
            if (string.IsNullOrWhiteSpace(p))
            {
                continue;
            }
            ConfigureSpriteSettings(p, pixelsPerUnit: ppu);
        }

        Debug.Log($"[DecorGen] Normalized {defaultDecorEntries.Count} sprites (PPU={ppu}).");
    }

    private void GeneratePrefabsFromSprites()
    {
        if (defaultDecorEntries == null || defaultDecorEntries.Count == 0)
        {
            Debug.LogWarning("[DecorGen] No entries. Put PNGs into the sprites folder and click 'Scan Sprites & Build Config'.");
            return;
        }

        if (string.IsNullOrWhiteSpace(outputPrefabsFolder))
        {
            Debug.LogWarning("[DecorGen] Output prefabs folder is empty.");
            return;
        }

        EnsureDirectories(outputPrefabsFolder);

        if (deleteExistingPrefabs)
        {
            var confirm = EditorUtility.DisplayDialog(
                "Recreate decoration prefabs?",
                $"This will delete existing prefabs in:\n{outputPrefabsFolder}\n\nContinue?",
                "Delete & Recreate",
                "Cancel");
            if (!confirm)
            {
                return;
            }
            DeleteExistingPrefabs(outputPrefabsFolder);
        }

        var created = 0;
        for (var i = 0; i < defaultDecorEntries.Count; i++)
        {
            var entry = defaultDecorEntries[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.spritePath))
            {
                continue;
            }

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(entry.spritePath);
            if (sprite == null)
            {
                Debug.LogWarning($"[DecorGen] Sprite not found or not imported as Sprite: {entry.spritePath}");
                continue;
            }

            if (CreateDecorPrefabFromSprite(outputPrefabsFolder, sprite, entry))
            {
                created++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        ValidateResourcesLoad();
        Debug.Log($"[DecorGen] Created {created} decoration prefabs in {outputPrefabsFolder}");
    }

    private static void EnsureDirectories(string assetPath)
    {
        var fullPath = Path.GetFullPath(assetPath);
        if (!Directory.Exists(fullPath))
        {
            Directory.CreateDirectory(fullPath);
        }
    }

    private static bool CreateDecorPrefabFromSprite(string basePath, Sprite sprite, DecorSpriteEntry entry)
    {
        var name = Path.GetFileNameWithoutExtension(entry.spritePath);
        var prefabGO = new GameObject(name);
        var sr = prefabGO.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = Color.white;
        sr.sortingOrder = 6;

        var decor = prefabGO.AddComponent<WorldDecoration>();
        decor.size = entry.size;
        decor.minSlopeDeg = entry.minSlopeDeg;
        decor.maxSlopeDeg = entry.maxSlopeDeg;
        decor.verticalOffset = entry.verticalOffset;

        AddColliderAndLayer(prefabGO, sr, entry.size);

        var prefabPath = $"{basePath}/{name}.prefab";
        PrefabUtility.SaveAsPrefabAsset(prefabGO, prefabPath);
        DestroyImmediate(prefabGO);

        Debug.Log($"[DecorGen] Created prefab: {prefabPath}");
        return true;
    }

    private static void AddColliderAndLayer(GameObject prefabGO, SpriteRenderer sr, WorldDecoration.SizeCategory size)
    {
        if (prefabGO == null || sr == null || sr.sprite == null)
        {
            return;
        }

        var obstacleLayer = LayerMask.NameToLayer("TerrainObstacle");
        if (obstacleLayer == -1)
        {
            Debug.LogWarning("[DecorGen] Layer 'TerrainObstacle' not found. Please create it (Project Settings -> Tags and Layers) and re-run generation.");
        }
        else
        {
            prefabGO.layer = obstacleLayer;
        }

        var b = sr.sprite.bounds;
        var fullW = Mathf.Max(0.05f, b.size.x);
        var fullH = Mathf.Max(0.05f, b.size.y);

        if (size == WorldDecoration.SizeCategory.Small)
        {
            var box = prefabGO.AddComponent<BoxCollider2D>();
            box.size = new Vector2(fullW * 0.80f, fullH * 0.45f);
            box.offset = new Vector2(0f, box.size.y * 0.5f);
        }
        else
        {
            var capsule = prefabGO.AddComponent<CapsuleCollider2D>();
            capsule.direction = CapsuleDirection2D.Vertical;
            var heightFactor = size == WorldDecoration.SizeCategory.Medium ? 0.70f : 0.60f;
            capsule.size = new Vector2(fullW * 0.55f, fullH * heightFactor);
            capsule.offset = new Vector2(0f, capsule.size.y * 0.5f);
        }
    }

    private static void ConfigureSpriteSettings(string spriteAssetPath, int pixelsPerUnit)
    {
        var importer = AssetImporter.GetAtPath(spriteAssetPath) as TextureImporter;
        if (importer == null)
        {
            return;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.alphaIsTransparency = true;
        importer.spritePixelsPerUnit = pixelsPerUnit;

        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Custom;
        settings.spritePivot = new Vector2(0.5f, 0f);
        importer.SetTextureSettings(settings);

        importer.SaveAndReimport();
    }

    private static void DeleteExistingPrefabs(string outputFolder)
    {
        var guids = AssetDatabase.FindAssets("t:Prefab", new[] { outputFolder });
        for (var i = 0; i < guids.Length; i++)
        {
            var path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }
            AssetDatabase.DeleteAsset(path);
        }
    }

    private static WorldDecoration.SizeCategory InferSizeFromName(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("_small") || n.Contains("small"))
        {
            return WorldDecoration.SizeCategory.Small;
        }
        if (n.Contains("_medium") || n.Contains("med") || n.Contains("medium"))
        {
            return WorldDecoration.SizeCategory.Medium;
        }
        if (n.Contains("_large") || n.Contains("large") || n.Contains("big"))
        {
            return WorldDecoration.SizeCategory.Large;
        }

        return WorldDecoration.SizeCategory.Medium;
    }

    private static (int minSlope, int maxSlope) DefaultSlopeRange(WorldDecoration.SizeCategory size)
    {
        switch (size)
        {
            case WorldDecoration.SizeCategory.Small:
                return (0, 50);
            case WorldDecoration.SizeCategory.Medium:
                return (0, 30);
            case WorldDecoration.SizeCategory.Large:
                return (0, 20);
            default:
                return (0, 30);
        }
    }

    private static void ValidateResourcesLoad()
    {
        var gos = Resources.LoadAll<GameObject>("Decorations/Default");
        var small = 0;
        var med = 0;
        var large = 0;

        for (var i = 0; i < gos.Length; i++)
        {
            var d = gos[i] != null ? gos[i].GetComponent<WorldDecoration>() : null;
            if (d == null)
            {
                continue;
            }

            switch (d.size)
            {
                case WorldDecoration.SizeCategory.Small:
                    small++;
                    break;
                case WorldDecoration.SizeCategory.Medium:
                    med++;
                    break;
                case WorldDecoration.SizeCategory.Large:
                    large++;
                    break;
            }
        }

        Debug.Log($"[DecorGen] Resources validation: total={gos.Length} small={small} med={med} large={large}");
    }

    [Serializable]
    private sealed class DecorSpriteEntry
    {
        public string spritePath;
        public WorldDecoration.SizeCategory size;
        public int minSlopeDeg;
        public int maxSlopeDeg;
        public float verticalOffset;
    }
}
