#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WormCrawlerPrototype
{
    public static class MissingScriptFinder
    {
        [MenuItem("Tools/WormCrawler/Find Missing Scripts")]
        public static void FindMissingScripts()
        {
            var missingCount = 0;

            for (var si = 0; si < SceneManager.sceneCount; si++)
            {
                var scene = SceneManager.GetSceneAt(si);
                if (!scene.isLoaded)
                {
                    continue;
                }

                var roots = scene.GetRootGameObjects();
                for (var i = 0; i < roots.Length; i++)
                {
                    missingCount += ScanGameObjectTreeForMissing(roots[i], scene.path);
                }
            }

            var prefabGuids = AssetDatabase.FindAssets("t:Prefab");
            for (var i = 0; i < prefabGuids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                GameObject root = null;
                try
                {
                    root = PrefabUtility.LoadPrefabContents(path);
                    missingCount += ScanGameObjectTreeForMissing(root, path);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[MissingScriptFinder] Failed to scan prefab '{path}': {e.GetType().Name} {e.Message}");
                }
                finally
                {
                    if (root != null)
                    {
                        PrefabUtility.UnloadPrefabContents(root);
                    }
                }
            }

            if (missingCount == 0)
            {
                Debug.Log("[MissingScriptFinder] No missing scripts found in open scenes or prefabs.");
            }
            else
            {
                Debug.LogWarning($"[MissingScriptFinder] Missing script references found: {missingCount}. See previous log entries.");
            }
        }

        private static int ScanGameObjectTreeForMissing(GameObject root, string contextPath)
        {
            if (root == null)
            {
                return 0;
            }

            var missing = 0;
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                var t = transforms[i];
                if (t == null)
                {
                    continue;
                }

                var go = t.gameObject;
                var comps = go.GetComponents<Component>();
                for (var ci = 0; ci < comps.Length; ci++)
                {
                    if (comps[ci] == null)
                    {
                        missing++;
                        Debug.LogError($"[MissingScriptFinder] Missing script on '{GetPath(t)}' (context: {contextPath})", go);
                    }
                }
            }

            return missing;
        }

        private static string GetPath(Transform t)
        {
            if (t == null)
            {
                return "<null>";
            }

            var path = t.name;
            var p = t.parent;
            while (p != null)
            {
                path = p.name + "/" + path;
                p = p.parent;
            }

            return path;
        }
    }
}
#endif
