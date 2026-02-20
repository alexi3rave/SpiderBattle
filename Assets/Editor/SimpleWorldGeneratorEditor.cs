using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

using WormCrawlerPrototype;

[CustomEditor(typeof(WormCrawlerPrototype.SimpleWorldGenerator))]
public sealed class SimpleWorldGeneratorEditor : Editor
{
    private readonly List<string> _terrainResourcePaths = new List<string>();
    private readonly List<string> _terrainDisplayNames = new List<string>();
    private int _selectedTerrainIndex;

    private void OnEnable()
    {
        RefreshTerrainList();
        SyncSelectionFromTarget();
    }

    public override void OnInspectorGUI()
    {
        var gen = (WormCrawlerPrototype.SimpleWorldGenerator)target;

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("PNG Level (Quick)", EditorStyles.boldLabel);

        if (_terrainResourcePaths.Count == 0)
        {
            EditorGUILayout.HelpBox("No terrain PNG found in Assets/Resources/Levels/. Add terrain PNGs there.", MessageType.Warning);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(_terrainDisplayNames.Count == 0))
            {
                var newIndex = EditorGUILayout.Popup("Terrain", _selectedTerrainIndex, _terrainDisplayNames.ToArray());
                if (newIndex != _selectedTerrainIndex)
                {
                    _selectedTerrainIndex = newIndex;
                }
            }

            if (GUILayout.Button("Refresh", GUILayout.Width(70)))
            {
                RefreshTerrainList();
                SyncSelectionFromTarget();
            }
        }

        using (new EditorGUI.DisabledScope(_terrainResourcePaths.Count == 0))
        {
            if (GUILayout.Button("Use Selected Terrain + Generate"))
            {
                ApplySelectedTerrain(gen);
                Generate(gen, newSeed: true);
            }
        }

        EditorGUILayout.Space(8);

        DrawDefaultInspector();
    }

    private void RefreshTerrainList()
    {
        _terrainResourcePaths.Clear();
        _terrainDisplayNames.Clear();

        var levelsDir = "Assets/Resources/Levels";
        if (!Directory.Exists(levelsDir))
        {
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

    private void SyncSelectionFromTarget()
    {
        var gen = (WormCrawlerPrototype.SimpleWorldGenerator)target;
        if (gen == null) return;

        var currentPath = GetPrivateStringField(gen, "pngTerrainResourcesPath");
        if (string.IsNullOrEmpty(currentPath)) return;

        for (var i = 0; i < _terrainResourcePaths.Count; i++)
        {
            if (string.Equals(_terrainResourcePaths[i], currentPath, StringComparison.OrdinalIgnoreCase))
            {
                _selectedTerrainIndex = i;
                return;
            }
        }
    }

    private void ApplySelectedTerrain(WormCrawlerPrototype.SimpleWorldGenerator gen)
    {
        if (gen == null) return;
        if (_terrainResourcePaths.Count == 0) return;

        var path = _terrainResourcePaths[Mathf.Clamp(_selectedTerrainIndex, 0, _terrainResourcePaths.Count - 1)];
        var tex = Resources.Load<Texture2D>(path);
        if (tex == null)
        {
            Debug.LogError($"[PNG Quick] Failed to load terrain from Resources '{path}'.");
            return;
        }

        Undo.RecordObject(gen, "Select PNG Terrain");

        SetPrivateBoolField(gen, "usePngLevel", true);
        SetPrivateObjectField(gen, "pngTerrain", tex);
        SetPrivateStringField(gen, "pngTerrainResourcesPath", path);
        SetPrivateObjectField(gen, "pngEntities", null);

        EditorUtility.SetDirty(gen);
    }

    private static void Generate(WormCrawlerPrototype.SimpleWorldGenerator gen, bool newSeed)
    {
        if (gen == null) return;

        var method = gen.GetType().GetMethod(newSeed ? "RegenerateNewSeed" : "RegenerateSameSeed");
        if (method != null)
        {
            method.Invoke(gen, null);
        }
        else
        {
            Debug.LogError("[PNG Quick] Cannot find RegenerateNewSeed/RegenerateSameSeed method.");
        }

        EditorUtility.SetDirty(gen);
    }

    private static string GetPrivateStringField(WormCrawlerPrototype.SimpleWorldGenerator gen, string fieldName)
    {
        var f = typeof(SimpleWorldGenerator).GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return f != null ? f.GetValue(gen) as string : null;
    }

    private static void SetPrivateStringField(WormCrawlerPrototype.SimpleWorldGenerator gen, string fieldName, string value)
    {
        var f = typeof(SimpleWorldGenerator).GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (f != null) f.SetValue(gen, value);
    }

    private static void SetPrivateBoolField(WormCrawlerPrototype.SimpleWorldGenerator gen, string fieldName, bool value)
    {
        var f = typeof(SimpleWorldGenerator).GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (f != null) f.SetValue(gen, value);
    }

    private static void SetPrivateObjectField(WormCrawlerPrototype.SimpleWorldGenerator gen, string fieldName, UnityEngine.Object value)
    {
        var f = typeof(SimpleWorldGenerator).GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (f != null) f.SetValue(gen, value);
    }
}
