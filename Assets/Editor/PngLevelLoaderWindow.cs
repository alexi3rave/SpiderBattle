using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WormCrawlerPrototype;

public sealed class PngLevelLoaderWindow : EditorWindow
{
    private const string TerrainPngPathPrefKey = "WormCrawler_SelectedTerrain";

    private readonly List<string> _terrainResourcePaths = new List<string>();
    private readonly List<string> _terrainDisplayNames = new List<string>();
    private int _selectedTerrainIndex;

    [MenuItem("Tools/WormCrawler/PNG Level Loader")]
    [MenuItem("Tools/PNG Level Loader")]
    public static void Open()
    {
        GetWindow<PngLevelLoaderWindow>(title: "PNG Level Loader");
    }

    private void OnEnable()
    {
        RefreshTerrainList();
        SyncSelectionFromPrefs();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Pick terrain PNG and generate", EditorStyles.boldLabel);

        if (_terrainDisplayNames.Count == 0)
        {
            EditorGUILayout.HelpBox("No terrain PNG found in Assets/Resources/Levels/. Put your map PNG there.", MessageType.Warning);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(_terrainDisplayNames.Count == 0))
            {
                _selectedTerrainIndex = EditorGUILayout.Popup("Terrain", _selectedTerrainIndex, _terrainDisplayNames.ToArray());
            }

            if (GUILayout.Button("Refresh", GUILayout.Width(70)))
            {
                RefreshTerrainList();
                SyncSelectionFromPrefs();
            }
        }

        EditorGUILayout.Space(6);

        using (new EditorGUI.DisabledScope(_terrainResourcePaths.Count == 0))
        {
            if (GUILayout.Button("Save As Default (PlayMode)"))
            {
                SaveSelectionToPrefs();
            }

            if (GUILayout.Button("Apply + Generate Now"))
            {
                SaveSelectionToPrefs();
                ApplyAndGenerateNow();
            }
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.HelpBox(
            "How it works:\n" +
            "- Put map PNGs into Assets/Resources/Levels/.\n" +
            "- This tool saves chosen map path (PlayerPrefs). Bootstrap will auto-use it on start.\n" +
            "- 'Apply + Generate Now' also finds/creates World + SimpleWorldGenerator and regenerates immediately.",
            MessageType.Info);
    }

    private void RefreshTerrainList()
    {
        _terrainResourcePaths.Clear();
        _terrainDisplayNames.Clear();

        var levelsDir = "Assets/Resources/Levels";
        if (!Directory.Exists(levelsDir))
        {
            _selectedTerrainIndex = 0;
            return;
        }

        var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { levelsDir });
        for (var i = 0; i < guids.Length; i++)
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fileName = Path.GetFileNameWithoutExtension(assetPath);
            if (string.Equals(fileName, "entities", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var rel = assetPath.Replace("\\", "/");
            var idx = rel.IndexOf("Resources/", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                continue;
            }

            var resourcesRel = rel.Substring(idx + "Resources/".Length);
            resourcesRel = resourcesRel.Substring(0, resourcesRel.Length - ".png".Length);

            _terrainResourcePaths.Add(resourcesRel);
            _terrainDisplayNames.Add(resourcesRel);
        }

        _selectedTerrainIndex = Mathf.Clamp(_selectedTerrainIndex, 0, Mathf.Max(0, _terrainResourcePaths.Count - 1));
    }

    private void SyncSelectionFromPrefs()
    {
        var current = PlayerPrefs.GetString(TerrainPngPathPrefKey, string.Empty);
        if (string.IsNullOrEmpty(current))
        {
            _selectedTerrainIndex = 0;
            return;
        }

        for (var i = 0; i < _terrainResourcePaths.Count; i++)
        {
            if (string.Equals(_terrainResourcePaths[i], current, StringComparison.OrdinalIgnoreCase))
            {
                _selectedTerrainIndex = i;
                return;
            }
        }
    }

    private void SaveSelectionToPrefs()
    {
        if (_terrainResourcePaths.Count == 0) return;

        var path = _terrainResourcePaths[Mathf.Clamp(_selectedTerrainIndex, 0, _terrainResourcePaths.Count - 1)];
        PlayerPrefs.SetString(TerrainPngPathPrefKey, path);
        PlayerPrefs.Save();
        Debug.Log($"[PNG Loader] Default terrain saved: '{path}'");
    }

    private void ApplyAndGenerateNow()
    {
        if (_terrainResourcePaths.Count == 0) return;

        var path = _terrainResourcePaths[Mathf.Clamp(_selectedTerrainIndex, 0, _terrainResourcePaths.Count - 1)];
        TryEnsureReadableImport(path);
        var tex = Resources.Load<Texture2D>(path);
        if (tex == null)
        {
            Debug.LogError($"[PNG Loader] Cannot load terrain from Resources '{path}'.");
            return;
        }

        var gen = FindOrCreateGenerator();
        if (gen == null)
        {
            Debug.LogError("[PNG Loader] Failed to find or create SimpleWorldGenerator.");
            return;
        }

        Undo.RecordObject(gen, "Apply PNG Terrain");
        gen.ConfigurePngTerrain(path);

        if (Application.isPlaying)
        {
            gen.RegenerateNewSeed();
        }
        else
        {
            gen.RegenerateNewSeed();
            EditorSceneManager.MarkSceneDirty(gen.gameObject.scene);
        }

        Selection.activeObject = gen.gameObject;
    }

    private static SimpleWorldGenerator FindOrCreateGenerator()
    {
#if UNITY_2023_1_OR_NEWER || UNITY_6000_0_OR_NEWER
        var gen = UnityEngine.Object.FindAnyObjectByType<SimpleWorldGenerator>();
#else
        var gen = UnityEngine.Object.FindObjectOfType<SimpleWorldGenerator>();
#endif
        if (gen != null)
        {
            return gen;
        }

        var worldGo = GameObject.Find("World");
        if (worldGo == null)
        {
            worldGo = new GameObject("World");
        }

        gen = worldGo.GetComponent<SimpleWorldGenerator>();
        if (gen == null)
        {
            gen = worldGo.AddComponent<SimpleWorldGenerator>();
        }

        return gen;
    }

    private static void TryEnsureReadableImport(string resourcesPathNoExt)
    {
        if (string.IsNullOrEmpty(resourcesPathNoExt)) return;

        var assetPath = "Assets/Resources/" + resourcesPathNoExt.Replace("\\", "/") + ".png";
        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
        {
            return;
        }

        var changed = false;
        if (!importer.isReadable)
        {
            importer.isReadable = true;
            changed = true;
        }

        if (importer.textureCompression != TextureImporterCompression.Uncompressed)
        {
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            changed = true;
        }

        if (importer.mipmapEnabled)
        {
            importer.mipmapEnabled = false;
            changed = true;
        }

        if (importer.filterMode != FilterMode.Point)
        {
            importer.filterMode = FilterMode.Point;
            changed = true;
        }

        if (changed)
        {
            importer.SaveAndReimport();
        }
    }
}
