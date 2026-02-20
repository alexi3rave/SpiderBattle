using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class HeroSkinGenerator
{
    private const string HeroSpritePath = "Assets/Resources/Hero/spider.png";
    private const string AnimFolder = "Assets/Animations/Hero";
    private const string ResourcesHeroFolder = "Assets/Resources/Hero";

    [MenuItem("Tools/Generate Hero Skin")]
    public static void Generate()
    {
        EnsureFolder("Assets/Animations");
        EnsureFolder(AnimFolder);
        EnsureFolder("Assets/Resources");
        EnsureFolder(ResourcesHeroFolder);

        ConfigureHeroSprite(HeroSpritePath);
        var heroSprite = AssetDatabase.LoadAssetAtPath<Sprite>(HeroSpritePath);
        if (heroSprite == null)
        {
            Debug.LogError($"[HeroGen] Sprite not found or not imported as Sprite: {HeroSpritePath}");
            return;
        }

        var sprites = AssetDatabase.LoadAllAssetsAtPath(HeroSpritePath);
        var spriteMap = new System.Collections.Generic.Dictionary<string, Sprite>(System.StringComparer.Ordinal);
        for (var i = 0; i < sprites.Length; i++)
        {
            if (sprites[i] is Sprite s && s != null)
            {
                spriteMap[s.name] = s;
            }
        }

        var idleSprites = new[]
        {
            GetSprite(spriteMap, "spider_idle_001"),
            GetSprite(spriteMap, "spider_idle_002")
        };
        var walkSprites = new[]
        {
            GetSprite(spriteMap, "spider_walk_001"),
            GetSprite(spriteMap, "spider_walk_002"),
            GetSprite(spriteMap, "spider_walk_003")
        };
        var climbSprites = new[]
        {
            GetSprite(spriteMap, "spider_climb_001"),
            GetSprite(spriteMap, "spider_climb_002")
        };
        var shootSprites = new[]
        {
            GetSprite(spriteMap, "spider_shoot_001"),
            GetSprite(spriteMap, "spider_shoot_002")
        };

        var idleClip = CreateOrUpdateSpriteClip($"{AnimFolder}/HeroIdle.anim", idleSprites, fps: 1f, loop: true);
        var walkClip = CreateOrUpdateSpriteClip($"{AnimFolder}/HeroWalk.anim", walkSprites, fps: 8f, loop: true);
        var runClip = CreateOrUpdateSpriteClip($"{AnimFolder}/HeroRun.anim", walkSprites, fps: 12f, loop: true);
        var climbUpClip = CreateOrUpdateSpriteClip($"{AnimFolder}/HeroClimbUp.anim", climbSprites, fps: 6f, loop: true);
        var climbDownClip = CreateOrUpdateSpriteClip($"{AnimFolder}/HeroClimbDown.anim", climbSprites, fps: 6f, loop: true);
        var shootFlatClip = CreateOrUpdateSpriteClip($"{AnimFolder}/HeroShootFlat.anim", new[] { shootSprites[0] }, fps: 12f, loop: true);
        var shootUpClip = CreateOrUpdateSpriteClip($"{AnimFolder}/HeroShootUp.anim", new[] { shootSprites[1] }, fps: 12f, loop: true);

        var controller = CreateOrUpdateAnimator($"{AnimFolder}/HeroAnimator.controller", idleClip, walkClip, runClip, climbUpClip, climbDownClip, shootFlatClip, shootUpClip);
        if (controller == null)
        {
            Debug.LogError("[HeroGen] Failed to create AnimatorController.");
            return;
        }

        var prefabPath = $"{ResourcesHeroFolder}/Hero.prefab";
        CreateOrUpdateHeroPrefab(prefabPath, idleSprites[0], controller);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[HeroGen] Animation system ready. States: Idle/Walk/Run/ClimbUp/ClimbDown/Shoot");
    }

    private static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }

        var parts = folderPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        var current = parts[0];
        for (var i = 1; i < parts.Length; i++)
        {
            var next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }
            current = next;
        }
    }

    private static void ConfigureHeroSprite(string assetPath)
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        if (tex == null)
        {
            Debug.LogWarning($"[HeroGen] Texture not found at path: {assetPath}");
            return;
        }

        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
        {
            Debug.LogWarning($"[HeroGen] TextureImporter not found for: {assetPath}");
            return;
        }

        var ppu = Mathf.Clamp((tex.height / 10f) * 1.5f, 32f, 2048f);
        var changed = false;

        if (importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            changed = true;
        }

        if (importer.spriteImportMode != SpriteImportMode.Multiple)
        {
            importer.spriteImportMode = SpriteImportMode.Multiple;
            changed = true;
        }

        if (Math.Abs(importer.spritePixelsPerUnit - ppu) > 0.01f)
        {
            importer.spritePixelsPerUnit = ppu;
            changed = true;
        }

        if (importer.filterMode != FilterMode.Point)
        {
            importer.filterMode = FilterMode.Point;
            changed = true;
        }

        if (importer.wrapMode != TextureWrapMode.Clamp)
        {
            importer.wrapMode = TextureWrapMode.Clamp;
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

        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);

        if (settings.spriteAlignment != (int)SpriteAlignment.Custom)
        {
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            changed = true;
        }

        if ((settings.spritePivot - new Vector2(0.5f, 0f)).sqrMagnitude > 0.0001f)
        {
            settings.spritePivot = new Vector2(0.5f, 0f);
            changed = true;
        }

        if (settings.spriteExtrude < 2)
        {
            settings.spriteExtrude = 2;
            changed = true;
        }

        if (settings.spriteMeshType != (int)SpriteMeshType.FullRect)
        {
            settings.spriteMeshType = (int)SpriteMeshType.FullRect;
            changed = true;
        }

        if (tex.width > 0 && tex.height > 0)
        {
            var columns = 3;
            var rows = 4;
            var cellW = tex.width / (float)columns;
            var cellH = tex.height / (float)rows;

            var trimTop = cellH * 0.14f;
            var trimBottom = cellH * 0.22f;
            var padX = cellW * 0.06f;

            var rowNames = new string[][]
            {
                new[] { "spider_idle_001", "spider_idle_002", null },
                new[] { "spider_walk_001", "spider_walk_002", "spider_walk_003" },
                new[] { "spider_climb_001", "spider_climb_002", null },
                new[] { "spider_shoot_001", "spider_shoot_002", null }
            };

            var metasList = new System.Collections.Generic.List<SpriteMetaData>(16);
            for (var rTop = 0; rTop < rows; rTop++)
            {
                var y = tex.height - (rTop + 1) * cellH;
                for (var c = 0; c < columns; c++)
                {
                    var name = rowNames[rTop][c];
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    var x = c * cellW;
                    var rx = x + padX;
                    var ry = y + trimBottom;
                    var rw = cellW - padX * 2f;
                    var rh = cellH - trimBottom - trimTop;
                    if (rw <= 1f || rh <= 1f)
                    {
                        continue;
                    }

                    var meta = new SpriteMetaData();
                    meta.alignment = (int)SpriteAlignment.Custom;
                    meta.pivot = new Vector2(0.5f, 0f);
                    meta.name = name;
                    meta.rect = new Rect(rx, ry, rw, rh);
                    metasList.Add(meta);
                }
            }

            #pragma warning disable CS0618
            importer.spritesheet = metasList.ToArray();
            #pragma warning restore CS0618
            changed = true;
        }

        if (changed)
        {
            importer.SetTextureSettings(settings);
        }

        if (changed)
        {
            importer.SaveAndReimport();
            Debug.Log($"[HeroGen] Normalized hero sprite import (PPU={ppu}, Point, pivot bottom-center)");
        }
    }

    private static Sprite GetSprite(System.Collections.Generic.Dictionary<string, Sprite> map, string name)
    {
        if (map.TryGetValue(name, out var s) && s != null)
        {
            return s;
        }
        throw new Exception($"[HeroGen] Missing sprite '{name}' in sheet: {HeroSpritePath}");
    }

    private static AnimationClip CreateOrUpdateSpriteClip(string assetPath, Sprite[] sprites, float fps, bool loop)
    {
        if (sprites == null || sprites.Length == 0)
        {
            throw new Exception($"[HeroGen] No sprites provided for clip: {assetPath}");
        }

        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
        if (clip == null)
        {
            clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, assetPath);
        }

        clip.frameRate = Mathf.Max(1f, fps);
        SetLoopTime(clip, loop);

        var floatBindings = AnimationUtility.GetCurveBindings(clip);
        for (var i = 0; i < floatBindings.Length; i++)
        {
            AnimationUtility.SetEditorCurve(clip, floatBindings[i], null);
        }

        var objBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
        for (var i = 0; i < objBindings.Length; i++)
        {
            AnimationUtility.SetObjectReferenceCurve(clip, objBindings[i], null);
        }

        var binding = new EditorCurveBinding
        {
            type = typeof(SpriteRenderer),
            path = "Visual/Anim",
            propertyName = "m_Sprite"
        };

        ObjectReferenceKeyframe[] keyframes;
        var dt = 1f / clip.frameRate;
        if (sprites.Length == 1)
        {
            keyframes = new ObjectReferenceKeyframe[2];
            keyframes[0] = new ObjectReferenceKeyframe { time = 0f, value = sprites[0] };
            keyframes[1] = new ObjectReferenceKeyframe { time = dt, value = sprites[0] };
        }
        else
        {
            keyframes = new ObjectReferenceKeyframe[sprites.Length];
            for (var i = 0; i < sprites.Length; i++)
            {
                keyframes[i] = new ObjectReferenceKeyframe
                {
                    time = i * dt,
                    value = sprites[i]
                };
            }
        }

        AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    private static void SetLoopTime(AnimationClip clip, bool loop)
    {
        if (clip == null)
        {
            return;
        }

        var so = new SerializedObject(clip);
        var settings = so.FindProperty("m_AnimationClipSettings");
        if (settings != null)
        {
            var loopTime = settings.FindPropertyRelative("m_LoopTime");
            if (loopTime != null)
            {
                loopTime.boolValue = loop;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }

    private static AnimatorController CreateOrUpdateAnimator(string assetPath, AnimationClip idle, AnimationClip walk, AnimationClip run, AnimationClip climbUp, AnimationClip climbDown, AnimationClip shootFlat, AnimationClip shootUp)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath);
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(assetPath);
        }

        if (controller == null)
        {
            return null;
        }

        EnsureParam(controller, "Speed", AnimatorControllerParameterType.Float);
        EnsureParam(controller, "IsRunning", AnimatorControllerParameterType.Bool);
        EnsureParam(controller, "IsClimbing", AnimatorControllerParameterType.Bool);
        EnsureParam(controller, "ClimbDir", AnimatorControllerParameterType.Float);
        EnsureParam(controller, "Shoot", AnimatorControllerParameterType.Trigger);
        EnsureParam(controller, "AimY", AnimatorControllerParameterType.Float);

        var sm = controller.layers[0].stateMachine;
        var idleState = FindOrCreateState(sm, "Idle", idle);
        var walkState = FindOrCreateState(sm, "Walk", walk);
        var runState = FindOrCreateState(sm, "Run", run);
        var climbUpState = FindOrCreateState(sm, "ClimbUp", climbUp);
        var climbDownState = FindOrCreateState(sm, "ClimbDown", climbDown);
        var shootState = FindOrCreateState(sm, "Shoot", null);

        var shootBlend = FindOrCreateBlendTree(controller, "ShootBlend");
        ConfigureShootBlend(shootBlend, shootFlat, shootUp);
        shootState.motion = shootBlend;

        sm.defaultState = idleState;

        EnsureTransition(idleState, walkState, new[]
        {
            new AnimatorCondition { mode = AnimatorConditionMode.Greater, parameter = "Speed", threshold = 0.1f }
        });

        EnsureTransition(walkState, idleState, new[]
        {
            new AnimatorCondition { mode = AnimatorConditionMode.Less, parameter = "Speed", threshold = 0.1f }
        });

        EnsureTransition(walkState, runState, new[]
        {
            new AnimatorCondition { mode = AnimatorConditionMode.If, parameter = "IsRunning", threshold = 0f }
        });

        EnsureTransition(runState, walkState, new[]
        {
            new AnimatorCondition { mode = AnimatorConditionMode.IfNot, parameter = "IsRunning", threshold = 0f }
        });

        EnsureAnyStateTransition(sm, shootState, new[]
        {
            new AnimatorCondition { mode = AnimatorConditionMode.If, parameter = "Shoot", threshold = 0f }
        });
        EnsureExitTimeTransition(shootState, idleState, exitTime: 0.92f);

        EnsureAnyStateTransition(sm, climbUpState, new[]
        {
            new AnimatorCondition { mode = AnimatorConditionMode.If, parameter = "IsClimbing", threshold = 0f },
            new AnimatorCondition { mode = AnimatorConditionMode.Greater, parameter = "ClimbDir", threshold = 0.1f }
        });
        EnsureAnyStateTransition(sm, climbDownState, new[]
        {
            new AnimatorCondition { mode = AnimatorConditionMode.If, parameter = "IsClimbing", threshold = 0f },
            new AnimatorCondition { mode = AnimatorConditionMode.Less, parameter = "ClimbDir", threshold = -0.1f }
        });
        EnsureTransition(climbUpState, idleState, new[]
        {
            new AnimatorCondition { mode = AnimatorConditionMode.IfNot, parameter = "IsClimbing", threshold = 0f }
        });
        EnsureTransition(climbDownState, idleState, new[]
        {
            new AnimatorCondition { mode = AnimatorConditionMode.IfNot, parameter = "IsClimbing", threshold = 0f }
        });

        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static void EnsureParam(AnimatorController controller, string name, AnimatorControllerParameterType type)
    {
        for (var i = 0; i < controller.parameters.Length; i++)
        {
            if (controller.parameters[i].name == name)
            {
                return;
            }
        }

        controller.AddParameter(name, type);
    }

    private static AnimatorState FindOrCreateState(AnimatorStateMachine sm, string name, Motion motion)
    {
        for (var i = 0; i < sm.states.Length; i++)
        {
            if (sm.states[i].state != null && sm.states[i].state.name == name)
            {
                sm.states[i].state.motion = motion;
                return sm.states[i].state;
            }
        }

        var state = sm.AddState(name);
        state.motion = motion;
        return state;
    }

    private static BlendTree FindOrCreateBlendTree(AnimatorController controller, string name)
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(controller));
        for (var i = 0; i < assets.Length; i++)
        {
            if (assets[i] is BlendTree bt && bt != null && bt.name == name)
            {
                return bt;
            }
        }

        var tree = new BlendTree();
        tree.name = name;
        tree.hideFlags = HideFlags.HideInHierarchy;
        AssetDatabase.AddObjectToAsset(tree, controller);
        EditorUtility.SetDirty(controller);
        return tree;
    }

    private static void ConfigureShootBlend(BlendTree tree, AnimationClip shootFlat, AnimationClip shootUp)
    {
        tree.blendType = BlendTreeType.Simple1D;
        tree.blendParameter = "AimY";
        tree.useAutomaticThresholds = false;
        tree.children = new ChildMotion[0];
        tree.AddChild(shootFlat, 0f);
        tree.AddChild(shootUp, 1f);
        EditorUtility.SetDirty(tree);
    }

    private static void EnsureTransition(AnimatorState from, AnimatorState to, AnimatorCondition[] conditions)
    {
        for (var i = 0; i < from.transitions.Length; i++)
        {
            var t = from.transitions[i];
            if (t != null && t.destinationState == to)
            {
                t.hasExitTime = false;
                t.duration = 0.08f;
                t.conditions = conditions;
                return;
            }
        }

        var tr = from.AddTransition(to);
        tr.hasExitTime = false;
        tr.duration = 0.08f;
        tr.conditions = conditions;
    }

    private static void EnsureAnyStateTransition(AnimatorStateMachine sm, AnimatorState to, AnimatorCondition[] conditions)
    {
        for (var i = 0; i < sm.anyStateTransitions.Length; i++)
        {
            var t = sm.anyStateTransitions[i];
            if (t != null && t.destinationState == to)
            {
                t.hasExitTime = false;
                t.duration = 0.06f;
                t.conditions = conditions;
                return;
            }
        }

        var tr = sm.AddAnyStateTransition(to);
        tr.hasExitTime = false;
        tr.duration = 0.06f;
        tr.conditions = conditions;
    }

    private static void EnsureExitTimeTransition(AnimatorState from, AnimatorState to, float exitTime)
    {
        for (var i = 0; i < from.transitions.Length; i++)
        {
            var t = from.transitions[i];
            if (t != null && t.destinationState == to)
            {
                t.hasExitTime = true;
                t.exitTime = exitTime;
                t.duration = 0.05f;
                t.conditions = Array.Empty<AnimatorCondition>();
                return;
            }
        }

        var tr = from.AddTransition(to);
        tr.hasExitTime = true;
        tr.exitTime = exitTime;
        tr.duration = 0.05f;
        tr.conditions = Array.Empty<AnimatorCondition>();
    }

    private static void CreateOrUpdateHeroPrefab(string prefabPath, Sprite sprite, RuntimeAnimatorController controller)
    {
        var root = new GameObject("Hero");

        var visual = new GameObject("Visual");
        visual.transform.SetParent(root.transform, false);
        visual.transform.localScale = new Vector3(2.5f, 2.5f, 1f);

        var animGo = new GameObject("Anim");
        animGo.transform.SetParent(visual.transform, false);
        animGo.transform.localPosition = new Vector3(0f, -sprite.bounds.min.y, 0f);

        var sr = animGo.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = 10;

        var animator = root.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.applyRootMotion = false;

        var rb = root.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.simulated = true;
        rb.gravityScale = 3f;
        rb.freezeRotation = true;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        var col = root.AddComponent<CapsuleCollider2D>();
        col.direction = CapsuleDirection2D.Vertical;
        var visualScale = Mathf.Max(0.001f, visual.transform.localScale.y);
        var spriteW = Mathf.Max(0.001f, sprite.bounds.size.x * visualScale);
        var spriteH = Mathf.Max(0.001f, sprite.bounds.size.y * visualScale);
        var capsuleW = Mathf.Clamp(spriteW * 0.55f, 0.25f, 20.0f);
        var capsuleH = Mathf.Clamp(spriteH * 0.70f, 0.50f, 40.0f);
        if (capsuleW > capsuleH)
        {
            capsuleW = capsuleH;
        }
        col.size = new Vector2(capsuleW, capsuleH);
        col.offset = new Vector2(0f, capsuleH * 0.5f);

        root.AddComponent<WormCrawlerPrototype.SimpleHero>();

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        UnityEngine.Object.DestroyImmediate(root);

        Debug.Log($"[HeroGen] Created prefab: {prefabPath}");
    }
}
