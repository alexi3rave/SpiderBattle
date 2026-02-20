#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace WormCrawlerPrototype
{
    public static class ExplosionPrefabGenerator
    {
        private const string SpriteSheetPath = "Assets/Sprites/Explosions/CyberpunkExplosion.png";
        private const string AnimFolder = "Assets/Animations/Explosions";
        private const string PrefabFolder = "Assets/Prefabs/Effects";
        private const string ResourcesPrefabFolder = "Assets/Resources/Prefabs/Effects";
        private const string EffectsSortingLayerName = "Effects";

        [MenuItem("Tools/WormCrawler/Generate Explosion Prefab")]
        public static void Generate()
        {
            try
            {
                EnsureFolder(AnimFolder);
                EnsureFolder(PrefabFolder);
                EnsureFolder(ResourcesPrefabFolder);

                var sheetPath = ResolveSpriteSheetPath();
                if (string.IsNullOrEmpty(sheetPath))
                {
                    Debug.LogError("[ExplosionGen] Sprite sheet not found. Select the CyberpunkExplosion texture in Project and run generator again.");
                    return;
                }

                var sprites = EnsureSlicedAndLoadSprites(sheetPath);
                if (sprites == null || sprites.Length == 0)
                {
                    Debug.LogError($"[ExplosionGen] No sprites found for '{sheetPath}'. Ensure Texture is Sprite (Multiple) and sliced into 8 sprites (128x128). You can also select the texture in Project and re-run generator.");
                    return;
                }

                if (sprites.Length < 2)
                {
                    Debug.LogError($"[ExplosionGen] Expected multiple sprites in sheet, got {sprites.Length}. Set Texture importer Sprite Mode = Multiple and slice 8x 128x128.");
                    return;
                }

                var clip = CreateOrUpdateClip(sprites, $"{AnimFolder}/ExplosionAnim.anim", fps: 12f);
                var controller = CreateOrUpdateController(clip, $"{AnimFolder}/Explosion.controller");

                var prefabGo = BuildExplosionGameObject(controller);
                var prefabPath = $"{PrefabFolder}/Explosion.prefab";
                var resourcesPrefabPath = $"{ResourcesPrefabFolder}/Explosion.prefab";

                SavePrefab(prefabGo, prefabPath);
                SavePrefab(prefabGo, resourcesPrefabPath);

                GameObject.DestroyImmediate(prefabGo);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log($"[ExplosionGen] Generated Explosion prefab at '{prefabPath}' and Resources copy at '{resourcesPrefabPath}'.");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private static string ResolveSpriteSheetPath()
        {
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(SpriteSheetPath) != null)
            {
                return SpriteSheetPath;
            }

            if (Selection.activeObject is Texture2D)
            {
                var p = AssetDatabase.GetAssetPath(Selection.activeObject);
                if (!string.IsNullOrEmpty(p))
                {
                    return p;
                }
            }

            var guids = AssetDatabase.FindAssets("CyberpunkExplosion t:Texture2D");
            for (var i = 0; i < guids.Length; i++)
            {
                var p = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!string.IsNullOrEmpty(p))
                {
                    return p;
                }
            }

            return null;
        }

        private static Sprite[] EnsureSlicedAndLoadSprites(string sheetPath)
        {
            var sprites = LoadExplosionSprites(sheetPath);
            if (sprites.Length >= 2)
            {
                return sprites;
            }

            if (TryAutoConfigureAndSlice(sheetPath))
            {
                AssetDatabase.Refresh();
            }

            return LoadExplosionSprites(sheetPath);
        }

        private static bool TryAutoConfigureAndSlice(string sheetPath)
        {
            var importer = AssetImporter.GetAtPath(sheetPath) as TextureImporter;
            if (importer == null)
            {
                return false;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = 64f;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(sheetPath);
            if (tex == null)
            {
                return true;
            }

            var w = tex.width;
            var h = tex.height;
            if (w <= 0 || h <= 0)
            {
                return true;
            }

            if (w == 1024 && h == 128)
            {
                if (!TrySliceWithSpriteEditorDataProvider(importer))
                {
                    importer.SaveAndReimport();
                }
                return true;
            }

            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
            return true;
        }

        private static bool TrySliceWithSpriteEditorDataProvider(TextureImporter importer)
        {
            try
            {
                var factories = new SpriteDataProviderFactories();
                factories.Init();
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(importer.assetPath);
                if (tex == null)
                {
                    return false;
                }

                var dataProvider = factories.GetSpriteEditorDataProviderFromObject(tex);
                if (dataProvider == null)
                {
                    return TrySliceViaSpritesheet(importer);
                }

                dataProvider.InitSpriteEditorDataProvider();
                if (TrySliceViaProviderReflection(dataProvider))
                {
                    dataProvider.Apply();
                    importer.SaveAndReimport();
                    return true;
                }

                return TrySliceViaSpritesheet(importer);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ExplosionGen] Auto-slice via ISpriteEditorDataProvider failed: {e.Message}");
                return false;
            }
        }

        private static bool TrySliceViaProviderReflection(ISpriteEditorDataProvider dataProvider)
        {
            if (dataProvider == null)
            {
                return false;
            }

            var spriteRectProviderType =
                Type.GetType("UnityEditor.U2D.Sprites.ISpriteRectDataProvider, Unity.2D.Sprite.Editor")
                ?? Type.GetType("UnityEditor.U2D.Sprites.ISpriteRectDataProvider, UnityEditor");
            var spriteRectType =
                Type.GetType("UnityEditor.U2D.Sprites.SpriteRect, Unity.2D.Sprite.Editor")
                ?? Type.GetType("UnityEditor.U2D.Sprites.SpriteRect, UnityEditor");

            if (spriteRectProviderType == null || spriteRectType == null)
            {
                return false;
            }

            var getDataProvider = dataProvider.GetType().GetMethod("GetDataProvider", Type.EmptyTypes);
            if (getDataProvider == null || !getDataProvider.IsGenericMethodDefinition)
            {
                return false;
            }

            object rectProvider;
            try
            {
                rectProvider = getDataProvider.MakeGenericMethod(spriteRectProviderType).Invoke(dataProvider, null);
            }
            catch
            {
                return false;
            }

            if (rectProvider == null)
            {
                return false;
            }

            var rectsArray = Array.CreateInstance(spriteRectType, 8);
            for (var i = 0; i < 8; i++)
            {
                var rectObj = Activator.CreateInstance(spriteRectType);
                if (rectObj == null)
                {
                    return false;
                }

                SetMember(spriteRectType, rectObj, "name", $"CyberpunkExplosion_{i + 1:00}");
                SetMember(spriteRectType, rectObj, "rect", new Rect(i * 128, 0, 128, 128));
                SetMember(spriteRectType, rectObj, "pivot", new Vector2(0.5f, 0.5f));
                SetMember(spriteRectType, rectObj, "alignment", SpriteAlignment.Center);
                rectsArray.SetValue(rectObj, i);
            }

            var setRects = rectProvider.GetType().GetMethod("SetSpriteRects");
            if (setRects == null)
            {
                return false;
            }

            setRects.Invoke(rectProvider, new object[] { rectsArray });
            return true;
        }

        private static void SetMember(Type t, object obj, string name, object value)
        {
            var prop = t.GetProperty(name);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(obj, value);
                return;
            }

            var field = t.GetField(name);
            if (field != null)
            {
                field.SetValue(obj, value);
            }
        }

        private static bool TrySliceViaSpritesheet(TextureImporter importer)
        {
            if (importer == null)
            {
                return false;
            }

#pragma warning disable 0618
            var metas = new SpriteMetaData[8];
            for (var i = 0; i < 8; i++)
            {
                metas[i] = new SpriteMetaData
                {
                    name = $"CyberpunkExplosion_{i + 1:00}",
                    rect = new Rect(i * 128, 0, 128, 128),
                    pivot = new Vector2(0.5f, 0.5f),
                    alignment = (int)SpriteAlignment.Center
                };
            }

            importer.spritesheet = metas;
#pragma warning restore 0618

            importer.SaveAndReimport();
            return true;
        }

        private static Sprite[] LoadExplosionSprites(string sheetPath)
        {
            var sprites = AssetDatabase.LoadAllAssetsAtPath(sheetPath)
                .OfType<Sprite>()
                .OrderBy(s => s.rect.x)
                .ThenBy(s => s.name, StringComparer.Ordinal)
                .ToArray();

            return sprites;
        }

        private static AnimationClip CreateOrUpdateClip(Sprite[] sprites, string assetPath, float fps)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            if (clip == null)
            {
                clip = new AnimationClip();
                AssetDatabase.CreateAsset(clip, assetPath);
            }

            clip.frameRate = Mathf.Max(1f, fps);

            var binding = new EditorCurveBinding
            {
                type = typeof(SpriteRenderer),
                path = string.Empty,
                propertyName = "m_Sprite"
            };

            var keyframes = new ObjectReferenceKeyframe[sprites.Length];
            for (var i = 0; i < sprites.Length; i++)
            {
                keyframes[i] = new ObjectReferenceKeyframe
                {
                    time = i / clip.frameRate,
                    value = sprites[i]
                };
            }

            AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            EditorUtility.SetDirty(clip);
            return clip;
        }

        private static AnimatorController CreateOrUpdateController(AnimationClip clip, string assetPath)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(assetPath);
            }

            var sm = controller.layers[0].stateMachine;
            sm.states = Array.Empty<ChildAnimatorState>();
            var state = sm.AddState("ExplosionAnim");
            state.motion = clip;
            sm.defaultState = state;

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static GameObject BuildExplosionGameObject(RuntimeAnimatorController controller)
        {
            var root = new GameObject("Explosion");
            root.layer = 0;

            var sr = root.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 60;
            TrySetSortingLayer(sr, EffectsSortingLayerName);

            var anim = root.AddComponent<Animator>();
            anim.runtimeAnimatorController = controller;

            var ctrl = root.AddComponent<ExplosionController>();

            var fire = CreateParticleChild(root.transform, "Fire_PS", sortingOrder: 61);
            ConfigureFire(fire);

            var smoke = CreateParticleChild(root.transform, "Smoke_PS", sortingOrder: 60);
            ConfigureSmoke(smoke);

            var sparks = CreateParticleChild(root.transform, "Sparks_PS", sortingOrder: 62);
            ConfigureSparks(sparks);

            return root;
        }

        private static ParticleSystem CreateParticleChild(Transform parent, string name, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var ps = go.AddComponent<ParticleSystem>();
            var r = go.GetComponent<ParticleSystemRenderer>();
            if (r != null)
            {
                r.sortingOrder = sortingOrder;
                TrySetSortingLayer(r, EffectsSortingLayerName);
            }

            return ps;
        }

        private static void TrySetSortingLayer(Renderer r, string sortingLayerName)
        {
            if (r == null || string.IsNullOrEmpty(sortingLayerName))
            {
                return;
            }

            var layers = SortingLayer.layers;
            for (var i = 0; i < layers.Length; i++)
            {
                if (layers[i].name == sortingLayerName)
                {
                    r.sortingLayerName = sortingLayerName;
                    return;
                }
            }
        }

        private static void ConfigureFire(ParticleSystem ps)
        {
            var main = ps.main;
            main.duration = 0.8f;
            main.loop = false;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
            main.maxParticles = 50;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color32(255, 102, 0, 255));

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 50) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 1.0f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(
                new Gradient
                {
                    colorKeys = new[]
                    {
                        new GradientColorKey(new Color32(255, 102, 0, 255), 0f),
                        new GradientColorKey(new Color32(255, 80, 0, 255), 1f)
                    },
                    alphaKeys = new[]
                    {
                        new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(0f, 1f)
                    }
                });

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.y = new ParticleSystem.MinMaxCurve(-2f);
        }

        private static void ConfigureSmoke(ParticleSystem ps)
        {
            var main = ps.main;
            main.duration = 1.5f;
            main.loop = false;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
            main.maxParticles = 20;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color32(102, 102, 102, 200));

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 20) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 1.0f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(
                new Gradient
                {
                    colorKeys = new[]
                    {
                        new GradientColorKey(new Color32(102, 102, 102, 255), 0f),
                        new GradientColorKey(new Color32(80, 80, 80, 255), 1f)
                    },
                    alphaKeys = new[]
                    {
                        new GradientAlphaKey(0.6f, 0f),
                        new GradientAlphaKey(0f, 1f)
                    }
                });

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.4f));

            var force = ps.forceOverLifetime;
            force.enabled = true;
            force.y = new ParticleSystem.MinMaxCurve(-1f);
        }

        private static void ConfigureSparks(ParticleSystem ps)
        {
            var main = ps.main;
            main.duration = 0.6f;
            main.loop = false;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(8f, 15f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
            main.maxParticles = 50;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color32(0, 255, 255, 255));

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 50) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 30f;
            shape.radius = 0.1f;

            var force = ps.forceOverLifetime;
            force.enabled = true;
            force.y = new ParticleSystem.MinMaxCurve(-5f);
        }

        private static void SavePrefab(GameObject go, string path)
        {
            EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                PrefabUtility.SaveAsPrefabAssetAndConnect(go, path, InteractionMode.AutomatedAction);
            }
            else
            {
                PrefabUtility.SaveAsPrefabAsset(go, path);
            }
        }

        private static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            folder = folder.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            var parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            var name = Path.GetFileName(folder);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
            {
                return;
            }

            EnsureFolder(parent);
            if (!AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder(parent, name);
            }
        }
    }
}
#endif
