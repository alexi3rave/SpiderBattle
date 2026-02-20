using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WormCrawlerPrototype.Editor
{
    public sealed class PrefabLevelPngBakerWindow : EditorWindow
    {
        [Serializable]
        private struct DecorationColorMapping
        {
            public WorldDecoration prefab;
            public Color32 color;
        }

        [SerializeField] private GameObject levelPrefabOrRoot;
        [SerializeField] private int pixelsPerUnit = 8;
        [SerializeField] private int paddingPixels = 16;
        [SerializeField] private bool bakeEntities = false;
        [SerializeField] private Color32 heroColor = new Color32(255, 0, 255, 255);
        [SerializeField] private string heroSpawnObjectName = "HeroSpawn";

        [SerializeField] private string outputTerrainPngPath = "Assets/Resources/Levels/terrain.png";
        [SerializeField] private string outputEntitiesPngPath = "Assets/Resources/Levels/entities.png";

        [SerializeField] private List<DecorationColorMapping> decorationMappings = new List<DecorationColorMapping>();

        [MenuItem("Tools/WormCrawler/Prefab Level PNG Baker")]
        public static void Open()
        {
            GetWindow<PrefabLevelPngBakerWindow>(title: "Level PNG Baker");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
            levelPrefabOrRoot = (GameObject)EditorGUILayout.ObjectField("Level Root (Terrain Sprites)", levelPrefabOrRoot, typeof(GameObject), allowSceneObjects: true);

            var selectionLooksLikeHero = levelPrefabOrRoot != null && IsLikelyHeroSelection(levelPrefabOrRoot.transform);
            var srCount = 0;
            if (levelPrefabOrRoot != null)
            {
                srCount = levelPrefabOrRoot.GetComponentsInChildren<SpriteRenderer>(includeInactive: true).Length;
            }

            if (selectionLooksLikeHero)
            {
                EditorGUILayout.HelpBox("Selected object looks like Hero/Hero child. Select the LEVEL root that contains terrain sprites.", MessageType.Error);
            }
            else if (levelPrefabOrRoot != null && srCount < 2)
            {
                EditorGUILayout.HelpBox($"Only {srCount} SpriteRenderer found under selection. This usually means you selected the wrong object (e.g. Hero/Visual). Select the level root with terrain sprites.", MessageType.Warning);
            }
            else if (levelPrefabOrRoot != null)
            {
                EditorGUILayout.HelpBox($"Terrain sprites found: {srCount}", MessageType.Info);
            }

            pixelsPerUnit = Mathf.Max(1, EditorGUILayout.IntField("Pixels Per Unit", pixelsPerUnit));
            paddingPixels = Mathf.Max(0, EditorGUILayout.IntField("Padding Pixels", paddingPixels));

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                outputTerrainPngPath = EditorGUILayout.TextField("Terrain PNG Path", outputTerrainPngPath);
                if (GUILayout.Button("…", GUILayout.Width(28)))
                {
                    var p = EditorUtility.SaveFilePanelInProject("Save terrain PNG", "terrain", "png", "Choose where to save terrain.png");
                    if (!string.IsNullOrEmpty(p)) outputTerrainPngPath = p;
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                outputEntitiesPngPath = EditorGUILayout.TextField("Entities PNG Path", outputEntitiesPngPath);
                if (GUILayout.Button("…", GUILayout.Width(28)))
                {
                    var p = EditorUtility.SaveFilePanelInProject("Save entities PNG", "entities", "png", "Choose where to save entities.png");
                    if (!string.IsNullOrEmpty(p)) outputEntitiesPngPath = p;
                }
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Entities", EditorStyles.boldLabel);
            bakeEntities = EditorGUILayout.Toggle("Bake Entities PNG", bakeEntities);
            using (new EditorGUI.DisabledScope(!bakeEntities))
            {
                var hc = (Color)heroColor;
                hc = EditorGUILayout.ColorField("Hero Color", hc);
                heroColor = (Color32)hc;
                heroSpawnObjectName = EditorGUILayout.TextField("Hero Spawn Name", heroSpawnObjectName);
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Decoration Mappings (Prefab -> Color)", EditorStyles.boldLabel);
            var so = new SerializedObject(this);
            var mappingsProp = so.FindProperty("decorationMappings");
            using (new EditorGUI.DisabledScope(!bakeEntities))
            {
                EditorGUILayout.PropertyField(mappingsProp, includeChildren: true);
            }
            so.ApplyModifiedProperties();

            EditorGUILayout.Space(12);

            var canBake = levelPrefabOrRoot != null && !selectionLooksLikeHero && srCount >= 2;
            using (new EditorGUI.DisabledScope(!canBake))
            {
                var btn = bakeEntities ? "Bake Terrain PNG + Entities PNG" : "Bake Terrain PNG";
                if (GUILayout.Button(btn))
                {
                    Bake();
                }
            }

            EditorGUILayout.HelpBox(
                "How to use:\n" +
                "1) Create a LEVEL root object/prefab with multiple terrain SpriteRenderers (ground pieces).\n" +
                "   Do NOT select Hero/Visual. Do NOT select textures from Project view.\n" +
                "2) Click 'Bake Terrain PNG'. This overwrites terrain.png (asks confirmation).\n" +
                "3) In SimpleWorldGenerator enable usePngLevel and set pngTerrainResourcesPath to 'Levels/terrain'.\n" +
                "Optional: enable 'Bake Entities PNG' if you want marker-based placements.",
                MessageType.Info);
        }

        private void Bake()
        {
            if (levelPrefabOrRoot == null)
            {
                Debug.LogError("[LevelBake] No level prefab/root selected.");
                return;
            }

            if (IsLikelyHeroSelection(levelPrefabOrRoot.transform))
            {
                Debug.LogError("[LevelBake] It looks like you selected the Hero (or a child of it). Please select the LEVEL root that contains terrain SpriteRenderers.");
                return;
            }

            if (!outputTerrainPngPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                !outputEntitiesPngPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogError("[LevelBake] Output paths must end with .png");
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<Texture2D>(outputTerrainPngPath) != null)
            {
                var ok = EditorUtility.DisplayDialog(
                    "Overwrite terrain.png?",
                    $"File already exists and will be overwritten:\n{outputTerrainPngPath}",
                    "Overwrite",
                    "Cancel");
                if (!ok) return;
            }

            if (bakeEntities && AssetDatabase.LoadAssetAtPath<Texture2D>(outputEntitiesPngPath) != null)
            {
                var ok = EditorUtility.DisplayDialog(
                    "Overwrite entities.png?",
                    $"File already exists and will be overwritten:\n{outputEntitiesPngPath}",
                    "Overwrite",
                    "Cancel");
                if (!ok) return;
            }

            var root = InstantiateSource(levelPrefabOrRoot);
            if (root == null)
            {
                Debug.LogError("[LevelBake] Failed to instantiate source.");
                return;
            }

            try
            {
                var renderers = root.GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
                if (renderers == null || renderers.Length == 0)
                {
                    Debug.LogError("[LevelBake] No SpriteRenderer found under the selected root.");
                    return;
                }

                if (renderers.Length < 2)
                {
                    Debug.LogError($"[LevelBake] Only {renderers.Length} SpriteRenderer found. This usually means you selected the wrong object (e.g. Hero/Visual). Select the level root with terrain sprites.");
                    return;
                }

                var bounds = ComputeBounds(renderers);
                if (bounds.size.x < 0.001f || bounds.size.y < 0.001f)
                {
                    Debug.LogError($"[LevelBake] Invalid bounds size={bounds.size}");
                    return;
                }

                if (bounds.size.x < 2f || bounds.size.y < 2f)
                {
                    Debug.LogError($"[LevelBake] Bounds too small (size={bounds.size}). Looks like a single sprite was selected. Select the level root.");
                    return;
                }

                var paddingWorld = paddingPixels / (float)pixelsPerUnit;
                bounds.Expand(new Vector3(paddingWorld * 2f, paddingWorld * 2f, 0f));

                var pxW = Mathf.CeilToInt(bounds.size.x * pixelsPerUnit) + paddingPixels * 2;
                var pxH = Mathf.CeilToInt(bounds.size.y * pixelsPerUnit) + paddingPixels * 2;
                pxW = Mathf.Max(8, pxW);
                pxH = Mathf.Max(8, pxH);

                var disabled = DisableExternalSpriteRenderers(root.transform);
                try
                {
                    var terrain = RenderToTexture(bounds, pxW, pxH);
                    WritePng(outputTerrainPngPath, terrain);

                    if (bakeEntities)
                    {
                        var entities = BuildEntitiesTexture(root, bounds, pxW, pxH);
                        WritePng(outputEntitiesPngPath, entities);
                    }
                }
                finally
                {
                    RestoreSpriteRenderers(disabled);
                }

                Debug.Log($"[LevelBake] OK. Terrain='{outputTerrainPngPath}' Entities='{outputEntitiesPngPath}' size={pxW}x{pxH} ppu={pixelsPerUnit}");
            }
            finally
            {
                DestroyImmediate(root);
            }

            AssetDatabase.Refresh();
        }

        private static GameObject InstantiateSource(GameObject source)
        {
            if (PrefabUtility.IsPartOfPrefabAsset(source))
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(source);
                inst.hideFlags = HideFlags.HideAndDontSave;
                inst.transform.position = Vector3.zero;
                inst.transform.rotation = Quaternion.identity;
                inst.transform.localScale = Vector3.one;
                return inst;
            }

            var clone = Instantiate(source);
            clone.hideFlags = HideFlags.HideAndDontSave;
            clone.transform.position = Vector3.zero;
            clone.transform.rotation = Quaternion.identity;
            clone.transform.localScale = Vector3.one;
            return clone;
        }

        private static Bounds ComputeBounds(SpriteRenderer[] renderers)
        {
            var b = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                b.Encapsulate(renderers[i].bounds);
            }
            return b;
        }

        private static Texture2D RenderToTexture(Bounds contentBounds, int width, int height)
        {
            var rt = RenderTexture.GetTemporary(width, height, depthBuffer: 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 1;

            var camGO = new GameObject("__BakeCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.orthographic = true;
            cam.orthographicSize = contentBounds.size.y * 0.5f;
            cam.transform.position = new Vector3(contentBounds.center.x, contentBounds.center.y, -10f);
            cam.transform.rotation = Quaternion.identity;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 50f;
            cam.cullingMask = ~0;
            cam.targetTexture = rt;

            var prevActive = RenderTexture.active;
            try
            {
                RenderTexture.active = rt;
                cam.aspect = width / (float)height;
                var halfH = contentBounds.size.y * 0.5f;
                var halfW = contentBounds.size.x * 0.5f;
                cam.orthographicSize = Mathf.Max(halfH, halfW / Mathf.Max(0.0001f, cam.aspect));
                cam.Render();

                var tex = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                return tex;
            }
            finally
            {
                RenderTexture.active = prevActive;
                cam.targetTexture = null;
                DestroyImmediate(camGO);
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static Dictionary<SpriteRenderer, bool> DisableExternalSpriteRenderers(Transform bakedRoot)
        {
            var disabled = new Dictionary<SpriteRenderer, bool>(512);
            if (bakedRoot == null)
            {
                return disabled;
            }

#if UNITY_2023_1_OR_NEWER || UNITY_6000_0_OR_NEWER
            var all = UnityEngine.Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None);
#else
            var all = UnityEngine.Object.FindObjectsOfType<SpriteRenderer>();
#endif
            for (var i = 0; i < all.Length; i++)
            {
                var sr = all[i];
                if (sr == null) continue;
                if (sr.transform == bakedRoot || sr.transform.IsChildOf(bakedRoot))
                {
                    continue;
                }

                disabled[sr] = sr.enabled;
                sr.enabled = false;
            }

            return disabled;
        }

        private static void RestoreSpriteRenderers(Dictionary<SpriteRenderer, bool> disabled)
        {
            if (disabled == null) return;
            foreach (var kv in disabled)
            {
                if (kv.Key != null)
                {
                    kv.Key.enabled = kv.Value;
                }
            }
        }

        private Texture2D BuildEntitiesTexture(GameObject root, Bounds contentBounds, int width, int height)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false);
            var clear = new Color32[width * height];
            for (var i = 0; i < clear.Length; i++) clear[i] = new Color32(0, 0, 0, 0);
            tex.SetPixels32(clear);

            var min = contentBounds.min;

            var hero = FindByName(root.transform, heroSpawnObjectName);
            if (hero != null)
            {
                SetMarker(tex, hero.position, min, heroColor);
            }

            var worldDecos = root.GetComponentsInChildren<WorldDecoration>(includeInactive: true);
            if (worldDecos != null && worldDecos.Length > 0 && decorationMappings != null)
            {
                for (var i = 0; i < worldDecos.Length; i++)
                {
                    var d = worldDecos[i];
                    if (d == null) continue;

                    var mapped = TryGetDecorationColor(d, out var c);
                    if (!mapped) continue;

                    SetMarker(tex, d.transform.position, min, c);
                }
            }

            tex.Apply();
            return tex;
        }

        private bool TryGetDecorationColor(WorldDecoration instance, out Color32 color)
        {
            color = default;
            if (instance == null || decorationMappings == null) return false;

            var src = PrefabUtility.GetCorrespondingObjectFromSource(instance);
            if (src == null) return false;

            for (var i = 0; i < decorationMappings.Count; i++)
            {
                var m = decorationMappings[i];
                if (m.prefab == null) continue;

                if (m.prefab == src)
                {
                    color = m.color;
                    return true;
                }
            }

            return false;
        }

        private void SetMarker(Texture2D tex, Vector3 worldPos, Vector3 worldMin, Color32 color)
        {
            var x = Mathf.RoundToInt((worldPos.x - worldMin.x) * pixelsPerUnit) + paddingPixels;
            var y = Mathf.RoundToInt((worldPos.y - worldMin.y) * pixelsPerUnit) + paddingPixels;

            x = Mathf.Clamp(x, 0, tex.width - 1);
            y = Mathf.Clamp(y, 0, tex.height - 1);
            tex.SetPixel(x, y, color);
        }

        private static Transform FindByName(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name)) return null;
            if (root.name == name) return root;
            for (var i = 0; i < root.childCount; i++)
            {
                var t = FindByName(root.GetChild(i), name);
                if (t != null) return t;
            }
            return null;
        }

        private static void WritePng(string assetPath, Texture2D tex)
        {
            var full = Path.GetFullPath(assetPath);
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var bytes = tex.EncodeToPNG();
            File.WriteAllBytes(full, bytes);

            var rel = assetPath.Replace("\\", "/");
            AssetDatabase.ImportAsset(rel);

            var importer = AssetImporter.GetAtPath(rel) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.isReadable = true;
                importer.filterMode = FilterMode.Point;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
        }

        private static bool IsLikelyHeroSelection(Transform t)
        {
            if (t == null) return false;

            var cur = t;
            while (cur != null)
            {
                if (cur.GetComponent<SimpleHero>() != null)
                {
                    return true;
                }

                if (cur.name == "Hero")
                {
                    return true;
                }

                cur = cur.parent;
            }

            return false;
        }
    }
}
