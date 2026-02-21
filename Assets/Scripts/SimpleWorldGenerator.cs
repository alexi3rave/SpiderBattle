using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace WormCrawlerPrototype
{
    public sealed class SimpleWorldGenerator : MonoBehaviour
    {
        private List<Vector2> BuildCaveSpawnPoints(Vector2 capsuleSizeWorld, float[] heights)
        {
            if (_runtimeSolid == null || _runtimeGridW <= 2 || _runtimeGridH <= 2 || _runtimeCellSize <= 0.00001f)
            {
                return _cachedCaveSpawns;
            }

            if (_cachedCaveSpawns.Count > 0
                && _cachedCaveSeed == _runtimeSeed
                && _cachedCaveGridW == _runtimeGridW
                && _cachedCaveGridH == _runtimeGridH
                && Mathf.Abs(_cachedCaveCellSize - _runtimeCellSize) < 0.00001f)
            {
                return _cachedCaveSpawns;
            }

            _cachedCaveSpawns.Clear();
            _cachedCaveSeed = _runtimeSeed;
            _cachedCaveGridW = _runtimeGridW;
            _cachedCaveGridH = _runtimeGridH;
            _cachedCaveCellSize = _runtimeCellSize;

            var size = new Vector2(Mathf.Max(0.2f, capsuleSizeWorld.x * 0.9f), Mathf.Max(0.3f, capsuleSizeWorld.y * 0.9f));
            var radX = Mathf.Max(1, Mathf.CeilToInt((size.x * 0.5f) / _runtimeCellSize));
            var radY = Mathf.Max(2, Mathf.CeilToInt((size.y * 0.5f) / _runtimeCellSize));

            var w = _runtimeGridW;
            var h = _runtimeGridH;
            var solid = _runtimeSolid;

            var outside = new bool[w, h];
            var qx = new int[w * h];
            var qy = new int[w * h];
            var qh = 0;
            var qt = 0;

            void EnqueueIfAir(int x, int y)
            {
                if (x < 0 || y < 0 || x >= w || y >= h) return;
                if (solid[x, y]) return;
                if (outside[x, y]) return;
                outside[x, y] = true;
                qx[qt] = x;
                qy[qt] = y;
                qt++;
            }

            for (var x = 0; x < w; x++)
            {
                EnqueueIfAir(x, 0);
                EnqueueIfAir(x, h - 1);
            }
            for (var y = 0; y < h; y++)
            {
                EnqueueIfAir(0, y);
                EnqueueIfAir(w - 1, y);
            }

            while (qh < qt)
            {
                var x = qx[qh];
                var y = qy[qh];
                qh++;

                EnqueueIfAir(x - 1, y);
                EnqueueIfAir(x + 1, y);
                EnqueueIfAir(x, y - 1);
                EnqueueIfAir(x, y + 1);
            }

            var visited = new bool[w, h];
            var capsuleDir = CapsuleDirection2D.Vertical;
            var surfaceHeights = heights;

            bool IsAirWithClearance(int cx, int cy)
            {
                if (cx - radX < 1 || cx + radX >= w - 1) return false;
                if (cy - radY < 1 || cy + radY >= h - 1) return false;

                for (var yy = cy - radY; yy <= cy + radY; yy++)
                {
                    for (var xx = cx - radX; xx <= cx + radX; xx++)
                    {
                        if (solid[xx, yy]) return false;
                    }
                }
                return true;
            }

            for (var sy = 1; sy < h - 1; sy++)
            {
                for (var sx = 1; sx < w - 1; sx++)
                {
                    if (solid[sx, sy]) continue;
                    if (outside[sx, sy]) continue;
                    if (visited[sx, sy]) continue;

                    qh = 0;
                    qt = 0;
                    visited[sx, sy] = true;
                    qx[qt] = sx;
                    qy[qt] = sy;
                    qt++;

                    var best = new Vector2Int(-1, -1);
                    var bestScore = -1;

                    while (qh < qt)
                    {
                        var x = qx[qh];
                        var y = qy[qh];
                        qh++;

                        if (y > 0 && solid[x, y - 1] && IsAirWithClearance(x, y))
                        {
                            var score = 0;
                            score += y;
                            score += Mathf.Min(x, w - 1 - x);
                            if (score > bestScore)
                            {
                                bestScore = score;
                                best = new Vector2Int(x, y);
                            }
                        }

                        void TryVisit(int nx, int ny)
                        {
                            if (nx <= 0 || ny <= 0 || nx >= w - 1 || ny >= h - 1) return;
                            if (solid[nx, ny]) return;
                            if (outside[nx, ny]) return;
                            if (visited[nx, ny]) return;
                            visited[nx, ny] = true;
                            qx[qt] = nx;
                            qy[qt] = ny;
                            qt++;
                        }

                        TryVisit(x - 1, y);
                        TryVisit(x + 1, y);
                        TryVisit(x, y - 1);
                        TryVisit(x, y + 1);
                    }

                    if (bestScore <= 0) continue;

                    var standWorld = new Vector2(
                        (best.x + 0.5f) * _runtimeCellSize,
                        _runtimeBottomY + best.y * _runtimeCellSize + 0.05f);

                    if (surfaceHeights != null && surfaceHeights.Length > 0)
                    {
                        var xi = Mathf.Clamp(Mathf.RoundToInt(standWorld.x), 0, surfaceHeights.Length - 1);
                        if (standWorld.y >= surfaceHeights[xi] - 0.25f)
                        {
                            continue;
                        }
                    }

                    var overlaps = Physics2D.OverlapCapsuleAll(standWorld, size, capsuleDir, angle: 0f, ~0);
                    var blocked = false;
                    if (overlaps != null)
                    {
                        for (var i = 0; i < overlaps.Length; i++)
                        {
                            var oc = overlaps[i];
                            if (oc == null || oc.isTrigger) continue;
                            if (!IsGroundPoly(oc)) continue;
                            blocked = true;
                            break;
                        }
                    }
                    if (blocked) continue;

                    _cachedCaveSpawns.Add(standWorld);
                    if (_cachedCaveSpawns.Count >= 32)
                    {
                        return _cachedCaveSpawns;
                    }
                }
            }

            return _cachedCaveSpawns;
        }

        [Header("Terrain")]
        [SerializeField] private Texture2D pngTerrain;
        [SerializeField] private string pngTerrainResourcesPath = "Levels/terrain";
        [SerializeField] private Texture2D pngEntities;
        [SerializeField] private string pngEntitiesResourcesPath = "Levels/entities";
        [SerializeField] private int pngPixelsPerUnit = 8;
        [SerializeField, Range(0f, 1f)] private float pngAlphaSolidThreshold = 0.2f;
        [SerializeField] private Color32 pngHeroColor = new Color32(255, 0, 255, 255);

        [Serializable]
        private struct PngEntitySpawn
        {
            public Color32 color;
            public WorldDecoration prefab;
            public float zRotation;
        }

        [SerializeField] private List<PngEntitySpawn> pngEntitySpawns;

        [SerializeField] private float terrainBottomY = -12f;
        [SerializeField] private int terrainSmoothIterations = 3;

        private Sprite _heroSprite;

        private static PhysicsMaterial2D s_NoFrictionMaterial;

        private const int DefaultWidthUnits = 120;
        private const int DefaultHeightUnits = 40;

        private float[] _lastHeights;
        private int _lastSeed;
        private bool _hasHeroSpawnOverride;
        private Vector2 _heroSpawnOverride;

        private bool[,] _runtimeSolid;
        private int _runtimeGridW;
        private int _runtimeGridH;
        private float _runtimeCellSize;
        private float _runtimeBottomY;
        private int _runtimeSeed;
        private Transform _runtimeGroundTransform;
        private SpriteRenderer _runtimeGroundSpriteRenderer;
        private Texture2D _runtimeGroundTexture;
        private Color32[] _runtimeGroundPixels;

        private readonly List<Vector2> _cachedCaveSpawns = new List<Vector2>(64);
        private int _cachedCaveSeed;
        private int _cachedCaveGridW;
        private int _cachedCaveGridH;
        private float _cachedCaveCellSize;

        public void ConfigurePngTerrain(string terrainResourcesPath)
        {
            pngTerrain = null;
            if (!string.IsNullOrEmpty(terrainResourcesPath))
            {
                pngTerrainResourcesPath = terrainResourcesPath;
            }

            pngEntities = null;
            _hasHeroSpawnOverride = false;
        }

        public void Generate(int seed)
        {
            _lastSeed = seed;
            var t0 = Time.realtimeSinceStartup;
            UnityEngine.Random.InitState(seed);

            var widthUnits = DefaultWidthUnits;
            var heightUnits = DefaultHeightUnits;
            _hasHeroSpawnOverride = false;

            var resolvedTerrainPng = pngTerrain != null ? pngTerrain : Resources.Load<Texture2D>(pngTerrainResourcesPath);
            var resolvedEntitiesPng = pngEntities != null ? pngEntities : Resources.Load<Texture2D>(pngEntitiesResourcesPath);

            if (resolvedTerrainPng != null)
            {
                var ppu = Mathf.Max(1, pngPixelsPerUnit);
                widthUnits = Mathf.Max(4, Mathf.RoundToInt(resolvedTerrainPng.width / (float)ppu));
                heightUnits = Mathf.Max(4, Mathf.RoundToInt(resolvedTerrainPng.height / (float)ppu));
            }
            else
            {
                Debug.LogError($"[Ground] pngTerrain not found (field or Resources at '{pngTerrainResourcesPath}'). Cannot generate world.");
            }

            CreateBackground(widthUnits, heightUnits);

            if (resolvedTerrainPng != null)
            {
                var ppu = Mathf.Max(1, pngPixelsPerUnit);
                var cellSize = 1f / ppu;
                var solid = BuildSolidMaskFromAlpha(resolvedTerrainPng, pngAlphaSolidThreshold);
                PostProcessTerrainInPlace(solid, resolvedTerrainPng.width, resolvedTerrainPng.height);
                _lastHeights = ComputeSurfaceHeightsFromBitmap(solid, widthUnits, heightUnits, ppu, cellSize, terrainBottomY);
                CreateGroundFromPng(resolvedTerrainPng, solid, widthUnits, heightUnits, cellSize, terrainBottomY);
                PlaceEntitiesFromPng(resolvedEntitiesPng, cellSize, terrainBottomY);
            }

            Physics2D.SyncTransforms();

            SpawnHero(_lastHeights);

            var heroT = transform.Find("Hero");
            if (heroT == null)
            {
                Debug.LogWarning($"[Stage1] SimpleWorldGenerator: Hero child not found under '{name}' after SpawnHero (scene='{gameObject.scene.name}').");
            }
            else
            {
                Debug.Log($"[Stage1] SimpleWorldGenerator: Hero spawned in scene='{heroT.gameObject.scene.name}' parent='{heroT.parent.name}' pos={heroT.position}");
            }

            var dt = (Time.realtimeSinceStartup - t0) * 1000f;
            Debug.Log($"[Stage1] Generated world seed={seed} in {dt:0.0}ms");
        }

        [ContextMenu("Regenerate (Same Seed)")]
        public void RegenerateSameSeed()
        {
            RegenerateInternal(_lastSeed);
        }

        [ContextMenu("Regenerate (New Seed)")]
        public void RegenerateNewSeed()
        {
            var newSeed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            RegenerateInternal(newSeed);
        }

        private void RegenerateInternal(int seed)
        {
            ClearGeneratedChildren();
            Generate(seed);
        }

        private void ClearGeneratedChildren()
        {
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                var childName = child.gameObject.name;
                var shouldDestroy =
                    childName == "Background" ||
                    childName == "GroundPoly" ||
                    childName == "Hero" ||
                    child.GetComponentInChildren<WorldDecoration>(true) != null;

                if (!shouldDestroy)
                {
                    continue;
                }

#if UNITY_EDITOR
                ClearEditorSelectionIfDestroying(child.gameObject);
#endif

                if (Application.isPlaying)
                {
                    Destroy(child.gameObject);
                }
                else
                {
                    DestroyImmediate(child.gameObject);
                }
            }
        }

#if UNITY_EDITOR
        private static void ClearEditorSelectionIfDestroying(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            var active = Selection.activeObject;
            if (active == null)
            {
                return;
            }

            GameObject activeGo = null;
            if (active is GameObject g)
            {
                activeGo = g;
            }
            else if (active is Component c)
            {
                activeGo = c != null ? c.gameObject : null;
            }

            if (activeGo == null)
            {
                return;
            }

            if (activeGo == go || activeGo.transform.IsChildOf(go.transform))
            {
                Selection.activeObject = null;
            }
        }
#endif

        private static PhysicsMaterial2D GetNoFrictionMaterial()
        {
            if (s_NoFrictionMaterial == null)
            {
                s_NoFrictionMaterial = new PhysicsMaterial2D("NoFriction")
                {
                    friction = 0f,
                    bounciness = 0f,
                    frictionCombine = PhysicsMaterialCombine2D.Minimum,
                    bounceCombine = PhysicsMaterialCombine2D.Minimum
                };
            }

            return s_NoFrictionMaterial;
        }

        private static Vector2 WorldToLocalScale2D(Transform t)
        {
            var s = t.lossyScale;
            return new Vector2(Mathf.Abs(s.x) < 0.0001f ? 1f : s.x, Mathf.Abs(s.y) < 0.0001f ? 1f : s.y);
        }

        private static void FitColliderToSprite(Collider2D c, SpriteRenderer sr)
        {
            if (c == null || sr == null)
            {
                return;
            }

            var b = sr.bounds;
            var t = c.transform;
            var invScale = WorldToLocalScale2D(t);
            var localSize = new Vector2(b.size.x / invScale.x, b.size.y / invScale.y);

            var delta = (Vector2)((Vector3)b.center - t.position);
            var localOffset = new Vector2(delta.x / invScale.x, delta.y / invScale.y);

            if (c is BoxCollider2D box)
            {
                box.size = localSize;
                box.offset = localOffset;
                return;
            }

            if (c is CapsuleCollider2D capsule)
            {
                capsule.size = localSize;
                capsule.offset = localOffset;
                return;
            }
        }

        private static bool TrySetupPolygonColliderFromSprite(SpriteRenderer sr, PhysicsMaterial2D mat)
        {
            if (sr == null || sr.sprite == null)
            {
                return false;
            }

            var sprite = sr.sprite;
            var shapeCount = sprite.GetPhysicsShapeCount();
            if (shapeCount <= 0)
            {
                return false;
            }

            var go = sr.gameObject;
            var poly = go.GetComponent<PolygonCollider2D>();
            if (poly == null)
            {
                poly = go.AddComponent<PolygonCollider2D>();
            }
            poly.enabled = true;
            poly.isTrigger = false;
            poly.sharedMaterial = mat;
            poly.pathCount = shapeCount;

            var pts = new List<Vector2>(64);
            for (var i = 0; i < shapeCount; i++)
            {
                pts.Clear();
                sprite.GetPhysicsShape(i, pts);
                poly.SetPath(i, pts);
            }

            return true;
        }

        private void CreateBackground(int widthUnits, int heightUnits)
        {
            var bgGO = new GameObject("Background");
            bgGO.transform.SetParent(transform, false);
            bgGO.transform.position = new Vector3(widthUnits * 0.5f, heightUnits * 0.5f, 10f);

            var sr = bgGO.AddComponent<SpriteRenderer>();
            sr.sortingOrder = -100;
            sr.sprite = CreateSolidSprite(new Color(0.10f, 0.12f, 0.16f, 1f));
            bgGO.transform.localScale = new Vector3(widthUnits * 4f, heightUnits * 4f, 1f);
        }

        public bool CarveCraterWorld(Vector2 centerWorld, float radiusWorld)
        {
            if (_runtimeSolid == null || _runtimeGroundTransform == null || _runtimeGroundTexture == null || _runtimeGroundPixels == null)
            {
                return false;
            }

            if (_runtimeGridW <= 0 || _runtimeGridH <= 0 || _runtimeCellSize <= 0.00001f)
            {
                return false;
            }

            var expectedLen = _runtimeGridW * _runtimeGridH;
            if (_runtimeGroundPixels.Length != expectedLen)
            {
                var fresh = GetPixels32Safe(_runtimeGroundTexture);
                if (fresh.Length == expectedLen)
                {
                    _runtimeGroundPixels = fresh;
                }
                else
                {
                    Debug.LogWarning($"[Crater] Pixel buffer size mismatch: pixels={_runtimeGroundPixels.Length} expected={expectedLen} tex={_runtimeGroundTexture.width}x{_runtimeGroundTexture.height} grid={_runtimeGridW}x{_runtimeGridH}");
                    return false;
                }
            }

            var local = centerWorld - (Vector2)_runtimeGroundTransform.position;
            var cx = Mathf.RoundToInt(local.x / _runtimeCellSize);
            var cy = Mathf.RoundToInt(local.y / _runtimeCellSize);
            var rCells = Mathf.CeilToInt(Mathf.Max(0.01f, radiusWorld) / _runtimeCellSize);

            var clampedCx = Mathf.Clamp(cx, 1, _runtimeGridW - 2);
            var clampedCy = Mathf.Clamp(cy, 1, _runtimeGridH - 2);
            if (clampedCx != cx || clampedCy != cy)
            {
                cx = clampedCx;
                cy = clampedCy;
            }

            CarveDisk(_runtimeSolid, _runtimeGridW, _runtimeGridH, cx, cy, rCells);

            var air = new Color32(0, 0, 0, 0);
            var xMin = Mathf.Clamp(cx - rCells - 2, 0, _runtimeGridW - 1);
            var xMax = Mathf.Clamp(cx + rCells + 2, 0, _runtimeGridW - 1);
            var yMin = Mathf.Clamp(cy - rCells - 2, 0, _runtimeGridH - 1);
            var yMax = Mathf.Clamp(cy + rCells + 2, 0, _runtimeGridH - 1);

            var changedPx = 0;
            for (var y = yMin; y <= yMax; y++)
            {
                var row = y * _runtimeGridW;
                for (var x = xMin; x <= xMax; x++)
                {
                    if (_runtimeSolid[x, y])
                    {
                        continue;
                    }

                    var idx = row + x;
                    if (_runtimeGroundPixels[idx].a != 0)
                    {
                        _runtimeGroundPixels[idx] = air;
                        changedPx++;
                    }
                }
            }

            if (changedPx == 0)
            {
                return false;
            }

            _runtimeGroundTexture.SetPixels32(_runtimeGroundPixels);
            _runtimeGroundTexture.Apply();

            RebuildRuntimeEdgeColliders(minLoopAreaCells: 4);

            Physics2D.SyncTransforms();
            return true;
        }

        private void RebuildRuntimeEdgeColliders(int minLoopAreaCells)
        {
            if (_runtimeGroundTransform == null || _runtimeSolid == null)
            {
                return;
            }

            var existing = _runtimeGroundTransform.GetComponents<EdgeCollider2D>();
            for (var i = 0; i < existing.Length; i++)
            {
                if (existing[i] == null) continue;
                existing[i].enabled = false;
                if (Application.isPlaying) Destroy(existing[i]);
                else DestroyImmediate(existing[i]);
            }

            var loops = BuildBoundaryLoops(_runtimeSolid, _runtimeGridW, _runtimeGridH);
            for (var li = 0; li < loops.Count; li++)
            {
                var loop = loops[li];
                if (loop == null || loop.Count < 3)
                {
                    continue;
                }

                if (Mathf.Abs(ComputeSignedArea(loop)) < Mathf.Max(1, minLoopAreaCells))
                {
                    continue;
                }

                var points = SimplifyLoop(loop);
                var smooth = ApplyChaikinSmoothing(points, terrainSmoothIterations);
                if (smooth.Count < 3)
                {
                    continue;
                }

                var edge = _runtimeGroundTransform.gameObject.AddComponent<EdgeCollider2D>();
                edge.isTrigger = false;
                edge.sharedMaterial = GetNoFrictionMaterial();
                edge.edgeRadius = 0f;
                edge.points = ToWorldPointsClosed(smooth, _runtimeCellSize);
                edge.useAdjacentStartPoint = false;
                edge.useAdjacentEndPoint = false;
                edge.enabled = true;
                edge.gameObject.layer = 0;
            }
        }

        private void CreateGroundFromPng(Texture2D tex, bool[,] solid, int widthUnits, int heightUnits, float cellSize, float bottomY)
        {
            var groundGO = new GameObject("GroundPoly");
            groundGO.transform.SetParent(transform, false);
            groundGO.layer = 0;
            groundGO.transform.position = new Vector3(0f, bottomY, 0f);

            _runtimeSolid = solid;
            _runtimeGridW = tex != null ? tex.width : 0;
            _runtimeGridH = tex != null ? tex.height : 0;
            _runtimeCellSize = cellSize;
            _runtimeBottomY = bottomY;
            _runtimeSeed = 0;
            _runtimeGroundTransform = groundGO.transform;

            var rb = groundGO.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Static;
            rb.simulated = true;

            var sr = groundGO.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 0;

            var ppu = Mathf.Max(1, pngPixelsPerUnit);
            var runtimeTex = MakeReadableCopy(tex);
            if (runtimeTex == null)
            {
                runtimeTex = tex;
            }
            if (runtimeTex != null)
            {
                runtimeTex.filterMode = FilterMode.Point;
                runtimeTex.wrapMode = TextureWrapMode.Clamp;
                _runtimeGroundTexture = runtimeTex;
                _runtimeGroundPixels = GetPixels32Safe(runtimeTex);
                sr.sprite = Sprite.Create(runtimeTex, new Rect(0, 0, runtimeTex.width, runtimeTex.height), new Vector2(0f, 0f), ppu);
                _runtimeGroundSpriteRenderer = sr;
            }

            var loops = BuildBoundaryLoops(solid, tex.width, tex.height);
            var added = 0;
            foreach (var loop in loops)
            {
                if (loop == null || loop.Count < 3)
                {
                    continue;
                }

                if (Mathf.Abs(ComputeSignedArea(loop)) < Mathf.Max(1, terrainMinLoopAreaCells))
                {
                    continue;
                }

                var points = SimplifyLoop(loop);
                var smooth = ApplyChaikinSmoothing(points, terrainSmoothIterations);
                if (smooth.Count < 3)
                {
                    continue;
                }

                var edge = groundGO.AddComponent<EdgeCollider2D>();
                edge.isTrigger = false;
                edge.sharedMaterial = GetNoFrictionMaterial();
                edge.edgeRadius = 0f;
                edge.points = ToWorldPointsClosed(smooth, cellSize);
                edge.useAdjacentStartPoint = false;
                edge.useAdjacentEndPoint = false;
                edge.enabled = true;
                edge.gameObject.layer = 0;
                added++;
            }

            Debug.Log($"[Ground] PNG terrain created: tex={tex.width}x{tex.height} units={widthUnits}x{heightUnits} cellSize={cellSize} loops={added}");
        }

        private static bool[,] BuildSolidMaskFromAlpha(Texture2D tex, float alphaThreshold)
        {
            if (tex == null)
            {
                return new bool[1, 1];
            }

            alphaThreshold = Mathf.Clamp01(alphaThreshold);
            var w = tex.width;
            var h = tex.height;
            var solid = new bool[w, h];

            var px = GetPixels32Safe(tex);
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var c = px[y * w + x];
                    solid[x, y] = (c.a / 255f) >= alphaThreshold;
                }
            }

            return solid;
        }

        private void PostProcessTerrainInPlace(bool[,] solid, int w, int h)
        {
            if (solid == null)
            {
                return;
            }

            var sw = solid.GetLength(0);
            var sh = solid.GetLength(1);
            w = Mathf.Clamp(w, 1, sw);
            h = Mathf.Clamp(h, 1, sh);

            var smoothIt = Mathf.Max(0, terrainCellularSmoothIterations);
            if (smoothIt > 0)
            {
                var tmp = new bool[w, h];
                for (var it = 0; it < smoothIt; it++)
                {
                    for (var y = 0; y < h; y++)
                    {
                        for (var x = 0; x < w; x++)
                        {
                            var neighbors = 0;
                            for (var oy = -1; oy <= 1; oy++)
                            {
                                var ny = y + oy;
                                if (ny < 0 || ny >= h) continue;
                                for (var ox = -1; ox <= 1; ox++)
                                {
                                    if (ox == 0 && oy == 0) continue;
                                    var nx = x + ox;
                                    if (nx < 0 || nx >= w) continue;
                                    if (solid[nx, ny]) neighbors++;
                                }
                            }

                            if (neighbors > 4) tmp[x, y] = true;
                            else if (neighbors < 4) tmp[x, y] = false;
                            else tmp[x, y] = solid[x, y];
                        }
                    }

                    for (var y = 0; y < h; y++)
                    {
                        for (var x = 0; x < w; x++)
                        {
                            solid[x, y] = tmp[x, y];
                        }
                    }
                }
            }

            var minIsland = Mathf.Max(0, terrainMinIslandCells);
            if (minIsland > 0)
            {
                var visited = new bool[w * h];
                var queue = new int[w * h];
                var component = new List<int>(1024);

                var largestSize = 0;
                var largestStart = -1;

                for (var y = 0; y < h; y++)
                {
                    for (var x = 0; x < w; x++)
                    {
                        if (!solid[x, y]) continue;
                        var start = y * w + x;
                        if (visited[start]) continue;

                        component.Clear();
                        var qh = 0;
                        var qt = 0;
                        queue[qt++] = start;
                        visited[start] = true;

                        while (qh < qt)
                        {
                            var idx = queue[qh++];
                            component.Add(idx);
                            var cx = idx % w;
                            var cy = idx / w;

                            var left = cx - 1;
                            var right = cx + 1;
                            var down = cy - 1;
                            var up = cy + 1;

                            if (left >= 0)
                            {
                                var n = cy * w + left;
                                if (!visited[n] && solid[left, cy]) { visited[n] = true; queue[qt++] = n; }
                            }
                            if (right < w)
                            {
                                var n = cy * w + right;
                                if (!visited[n] && solid[right, cy]) { visited[n] = true; queue[qt++] = n; }
                            }
                            if (down >= 0)
                            {
                                var n = down * w + cx;
                                if (!visited[n] && solid[cx, down]) { visited[n] = true; queue[qt++] = n; }
                            }
                            if (up < h)
                            {
                                var n = up * w + cx;
                                if (!visited[n] && solid[cx, up]) { visited[n] = true; queue[qt++] = n; }
                            }
                        }

                        if (component.Count > largestSize)
                        {
                            largestSize = component.Count;
                            largestStart = start;
                        }
                    }
                }

                if (largestStart >= 0)
                {
                    Array.Clear(visited, 0, visited.Length);
                    for (var y = 0; y < h; y++)
                    {
                        for (var x = 0; x < w; x++)
                        {
                            if (!solid[x, y]) continue;
                            var start = y * w + x;
                            if (visited[start]) continue;

                            component.Clear();
                            var qh = 0;
                            var qt = 0;
                            queue[qt++] = start;
                            visited[start] = true;

                            while (qh < qt)
                            {
                                var idx = queue[qh++];
                                component.Add(idx);
                                var cx = idx % w;
                                var cy = idx / w;

                                var left = cx - 1;
                                var right = cx + 1;
                                var down = cy - 1;
                                var up = cy + 1;

                                if (left >= 0)
                                {
                                    var n = cy * w + left;
                                    if (!visited[n] && solid[left, cy]) { visited[n] = true; queue[qt++] = n; }
                                }
                                if (right < w)
                                {
                                    var n = cy * w + right;
                                    if (!visited[n] && solid[right, cy]) { visited[n] = true; queue[qt++] = n; }
                                }
                                if (down >= 0)
                                {
                                    var n = down * w + cx;
                                    if (!visited[n] && solid[cx, down]) { visited[n] = true; queue[qt++] = n; }
                                }
                                if (up < h)
                                {
                                    var n = up * w + cx;
                                    if (!visited[n] && solid[cx, up]) { visited[n] = true; queue[qt++] = n; }
                                }
                            }

                            var keep = start == largestStart || component.Count >= minIsland;
                            if (!keep)
                            {
                                for (var i = 0; i < component.Count; i++)
                                {
                                    var idx = component[i];
                                    solid[idx % w, idx / w] = false;
                                }
                            }
                        }
                    }
                }
            }

            var minHole = Mathf.Max(0, terrainMinHoleCells);
            if (minHole > 0)
            {
                var visited = new bool[w * h];
                var queue = new int[w * h];
                var component = new List<int>(1024);

                for (var y = 0; y < h; y++)
                {
                    for (var x = 0; x < w; x++)
                    {
                        if (solid[x, y]) continue;
                        var start = y * w + x;
                        if (visited[start]) continue;

                        component.Clear();
                        var qh = 0;
                        var qt = 0;
                        queue[qt++] = start;
                        visited[start] = true;

                        var touchesBorder = false;
                        while (qh < qt)
                        {
                            var idx = queue[qh++];
                            component.Add(idx);
                            var cx = idx % w;
                            var cy = idx / w;

                            if (cx == 0 || cx == w - 1 || cy == 0 || cy == h - 1)
                            {
                                touchesBorder = true;
                            }

                            var left = cx - 1;
                            var right = cx + 1;
                            var down = cy - 1;
                            var up = cy + 1;

                            if (left >= 0)
                            {
                                var n = cy * w + left;
                                if (!visited[n] && !solid[left, cy]) { visited[n] = true; queue[qt++] = n; }
                            }
                            if (right < w)
                            {
                                var n = cy * w + right;
                                if (!visited[n] && !solid[right, cy]) { visited[n] = true; queue[qt++] = n; }
                            }
                            if (down >= 0)
                            {
                                var n = down * w + cx;
                                if (!visited[n] && !solid[cx, down]) { visited[n] = true; queue[qt++] = n; }
                            }
                            if (up < h)
                            {
                                var n = up * w + cx;
                                if (!visited[n] && !solid[cx, up]) { visited[n] = true; queue[qt++] = n; }
                            }
                        }

                        if (!touchesBorder && component.Count < minHole)
                        {
                            for (var i = 0; i < component.Count; i++)
                            {
                                var idx = component[i];
                                solid[idx % w, idx / w] = true;
                            }
                        }
                    }
                }
            }
        }

        private static Color32[] GetPixels32Safe(Texture2D tex)
        {
            if (tex == null)
            {
                return Array.Empty<Color32>();
            }

            try
            {
                return tex.GetPixels32();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LevelPNG] Texture '{tex.name}' is not readable. Creating a readable copy. ({e.GetType().Name})");
                var copy = MakeReadableCopy(tex);
                return copy != null ? copy.GetPixels32() : Array.Empty<Color32>();
            }
        }

        private static Texture2D MakeReadableCopy(Texture2D source)
        {
            if (source == null)
            {
                return null;
            }

            var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                var tex = new Texture2D(source.width, source.height, TextureFormat.RGBA32, mipChain: false);
                tex.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                tex.Apply();
                return tex;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private void PlaceEntitiesFromPng(Texture2D entities, float cellSize, float bottomY)
        {
            if (entities == null)
            {
                return;
            }

            var w = entities.width;
            var h = entities.height;
            var px = GetPixels32Safe(entities);

            var hasHero = false;
            Vector2 heroPos = default;

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var c = px[y * w + x];
                    if (c.a == 0)
                    {
                        continue;
                    }

                    if (!hasHero && ColorsEqualRgb(c, pngHeroColor))
                    {
                        hasHero = true;
                        heroPos = new Vector2((x + 0.5f) * cellSize, bottomY + (y + 0.5f) * cellSize);
                        continue;
                    }

                    if (pngEntitySpawns == null || pngEntitySpawns.Count == 0)
                    {
                        continue;
                    }

                    for (var i = 0; i < pngEntitySpawns.Count; i++)
                    {
                        var m = pngEntitySpawns[i];
                        if (m.prefab == null)
                        {
                            continue;
                        }

                        if (!ColorsEqualRgb(c, m.color))
                        {
                            continue;
                        }

                        var p = new Vector2((x + 0.5f) * cellSize, bottomY + (y + 0.5f) * cellSize);
                        var instance = Instantiate(m.prefab, transform);
                        instance.transform.position = new Vector3(p.x, p.y + m.prefab.verticalOffset, 0f);
                        instance.transform.rotation = Quaternion.Euler(0f, 0f, m.zRotation);
                        break;
                    }
                }
            }

            if (hasHero)
            {
                _hasHeroSpawnOverride = true;
                _heroSpawnOverride = heroPos;
                Debug.Log($"[LevelPNG] Hero marker found at {heroPos}");
            }
        }

        private static bool ColorsEqualRgb(Color32 a, Color32 b)
        {
            return a.r == b.r && a.g == b.g && a.b == b.b;
        }

        private static void CarveDisk(bool[,] solid, int width, int height, int cx, int cy, int radius)
        {
            var r = Mathf.Max(0, radius);
            var r2 = r * r;
            for (var dy = -r; dy <= r; dy++)
            {
                for (var dx = -r; dx <= r; dx++)
                {
                    if (dx * dx + dy * dy > r2)
                    {
                        continue;
                    }

                    var x = cx + dx;
                    var y = cy + dy;
                    if (x < 1 || x >= width - 1 || y < 1 || y >= height - 1)
                    {
                        continue;
                    }

                    solid[x, y] = false;
                }
            }
        }

        private static float[] ComputeSurfaceHeightsFromBitmap(bool[,] solid, int widthUnits, int heightUnits, int resolution, float cellSize, float bottomY)
        {
            var heights = new float[widthUnits];
            var res = Mathf.Max(1, resolution);
            var solidW = solid != null ? solid.GetLength(0) : 0;
            var solidH = solid != null ? solid.GetLength(1) : 0;
            var gridW = Mathf.Min(widthUnits * res, solidW);
            var gridH = Mathf.Min(heightUnits * res, solidH);
            for (var x = 0; x < widthUnits; x++)
            {
                var bestTop = 0;
                var startX = x * res;
                if (startX >= gridW)
                {
                    heights[x] = bottomY + 0.5f * cellSize;
                    continue;
                }

                var endX = Mathf.Min(gridW, startX + res);
                for (var xx = startX; xx < endX; xx++)
                {
                    for (var y = gridH - 1; y >= 0; y--)
                    {
                        if (solid[xx, y])
                        {
                            if (y > bestTop)
                            {
                                bestTop = y;
                            }
                            break;
                        }
                    }
                }

                heights[x] = bottomY + (bestTop + 0.5f) * cellSize;
            }

            return heights;
        }

        private readonly struct EdgeKey : IEquatable<EdgeKey>
        {
            public readonly Vector2Int A;
            public readonly Vector2Int B;

            public EdgeKey(Vector2Int a, Vector2Int b)
            {
                A = a;
                B = b;
            }

            public bool Equals(EdgeKey other) => A == other.A && B == other.B;
            public override bool Equals(object obj) => obj is EdgeKey other && Equals(other);
            public override int GetHashCode()
            {
                unchecked
                {
                    return (A.GetHashCode() * 397) ^ B.GetHashCode();
                }
            }
        }

        private static List<List<Vector2Int>> BuildBoundaryLoops(bool[,] solid, int w, int h)
        {
            var next = new Dictionary<Vector2Int, List<Vector2Int>>(8192);
            var allEdges = new List<EdgeKey>(8192);

            void AddEdge(Vector2Int a, Vector2Int b)
            {
                var key = new EdgeKey(a, b);
                allEdges.Add(key);
                if (!next.TryGetValue(a, out var list))
                {
                    list = new List<Vector2Int>(1);
                    next[a] = list;
                }
                list.Add(b);
            }

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    if (!solid[x, y])
                    {
                        continue;
                    }

                    if (y == h - 1 || !solid[x, y + 1])
                    {
                        AddEdge(new Vector2Int(x, y + 1), new Vector2Int(x + 1, y + 1));
                    }
                    if (x == w - 1 || !solid[x + 1, y])
                    {
                        AddEdge(new Vector2Int(x + 1, y + 1), new Vector2Int(x + 1, y));
                    }
                    if (y == 0 || !solid[x, y - 1])
                    {
                        AddEdge(new Vector2Int(x + 1, y), new Vector2Int(x, y));
                    }
                    if (x == 0 || !solid[x - 1, y])
                    {
                        AddEdge(new Vector2Int(x, y), new Vector2Int(x, y + 1));
                    }
                }
            }

            var used = new HashSet<EdgeKey>();
            var loops = new List<List<Vector2Int>>();
            for (var i = 0; i < allEdges.Count; i++)
            {
                var startEdge = allEdges[i];
                if (used.Contains(startEdge))
                {
                    continue;
                }

                var loop = new List<Vector2Int>(128);
                var start = startEdge.A;
                var cur = startEdge.A;
                var nextPoint = startEdge.B;

                loop.Add(cur);
                used.Add(startEdge);
                cur = nextPoint;

                var safety = 0;
                while (cur != start && safety++ < 500000)
                {
                    loop.Add(cur);
                    if (!next.TryGetValue(cur, out var choices) || choices.Count == 0)
                    {
                        break;
                    }

                    var found = false;
                    for (var ci = 0; ci < choices.Count; ci++)
                    {
                        var cand = new EdgeKey(cur, choices[ci]);
                        if (!used.Contains(cand))
                        {
                            used.Add(cand);
                            cur = choices[ci];
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        break;
                    }
                }

                if (loop.Count >= 3 && cur == start)
                {
                    loops.Add(loop);
                }
            }

            return loops;
        }

        private static List<Vector2Int> SimplifyLoop(List<Vector2Int> loop)
        {
            if (loop == null || loop.Count < 4)
            {
                return loop;
            }

            var simplified = new List<Vector2Int>(loop.Count);
            var n = loop.Count;
            for (var i = 0; i < n; i++)
            {
                var prev = loop[(i - 1 + n) % n];
                var cur = loop[i];
                var next = loop[(i + 1) % n];

                var d1 = cur - prev;
                var d2 = next - cur;
                if ((d1.x == 0 && d2.x == 0) || (d1.y == 0 && d2.y == 0))
                {
                    continue;
                }

                simplified.Add(cur);
            }

            return simplified.Count >= 3 ? simplified : loop;
        }

        private static float ComputeSignedArea(List<Vector2Int> loop)
        {
            if (loop == null || loop.Count < 3)
            {
                return 0f;
            }

            long sum = 0;
            for (var i = 0; i < loop.Count; i++)
            {
                var a = loop[i];
                var b = loop[(i + 1) % loop.Count];
                sum += (long)a.x * b.y - (long)b.x * a.y;
            }

            return 0.5f * sum;
        }

        private static List<Vector2> ApplyChaikinSmoothing(List<Vector2Int> loop, int iterations)
        {
            if (loop == null || loop.Count < 3)
            {
                return new List<Vector2>(0);
            }

            var pts = new List<Vector2>(loop.Count);
            for (var i = 0; i < loop.Count; i++)
            {
                pts.Add(loop[i]);
            }

            iterations = Mathf.Clamp(iterations, 0, 6);
            for (var it = 0; it < iterations; it++)
            {
                var next = new List<Vector2>(pts.Count * 2);
                for (var i = 0; i < pts.Count; i++)
                {
                    var a = pts[i];
                    var b = pts[(i + 1) % pts.Count];
                    var q = a * 0.75f + b * 0.25f;
                    var r = a * 0.25f + b * 0.75f;
                    next.Add(q);
                    next.Add(r);
                }
                pts = next;
            }

            return pts;
        }

        private static Vector2[] ToWorldPointsClosed(List<Vector2> pts, float cellSize)
        {
            if (pts == null || pts.Count < 2)
            {
                return System.Array.Empty<Vector2>();
            }

            var needClose = (pts[0] - pts[pts.Count - 1]).sqrMagnitude > 0.0001f;
            var arr = new Vector2[pts.Count + (needClose ? 1 : 0)];
            for (var i = 0; i < pts.Count; i++)
            {
                arr[i] = pts[i] * cellSize;
            }

            if (needClose)
            {
                arr[arr.Length - 1] = pts[0] * cellSize;
            }

            return arr;
        }

        private static Sprite CreateSolidSprite(Color color)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, mipChain: false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), pixelsPerUnit: 1f);
        }

        private void SpawnHero(float[] heights)
        {
            if (heights == null || heights.Length == 0)
            {
                Debug.LogWarning("[Stage1] SpawnHero: heights[] is null/empty.");
                return;
            }

            _pendingHero2Spawn = default;

            var xIndex = Mathf.Clamp(15, 0, heights.Length - 1);
            if (_hasHeroSpawnOverride)
            {
                xIndex = Mathf.Clamp(Mathf.FloorToInt(_heroSpawnOverride.x), 0, heights.Length - 1);
            }
            else
            {
                xIndex = UnityEngine.Random.Range(0, heights.Length);
            }

            var xWorld = _hasHeroSpawnOverride ? _heroSpawnOverride.x : (xIndex + 0.5f);
            var y = heights[xIndex] + 1.2f;
            if (_hasHeroSpawnOverride)
            {
                y = _heroSpawnOverride.y + 1.2f;
            }

            var snapStart = new Vector2(xWorld, y + 25f);
            var obstacleLayer = LayerMask.NameToLayer("TerrainObstacle");
            var snapMask = obstacleLayer >= 0 ? ~(1 << obstacleLayer) : ~0;
            var bestGroundHit = default(RaycastHit2D);
            var bestGroundDist = float.MaxValue;
            var hasGroundHit = false;
            var attempts = _hasHeroSpawnOverride ? 1 : 20;
            for (var attempt = 0; attempt < attempts && !hasGroundHit; attempt++)
            {
                if (!_hasHeroSpawnOverride)
                {
                    xIndex = UnityEngine.Random.Range(0, heights.Length);
                    xWorld = xIndex + 0.5f;
                    y = heights[xIndex] + 1.2f;
                    snapStart = new Vector2(xWorld, y + 25f);
                }

                var snapHits = Physics2D.RaycastAll(snapStart, Vector2.down, 120f, snapMask);
                for (var i = 0; i < snapHits.Length; i++)
                {
                    var h = snapHits[i];
                    if (h.collider == null || h.collider.isTrigger)
                    {
                        continue;
                    }

                    if (h.collider.gameObject.name != "GroundPoly")
                    {
                        continue;
                    }

                    if (h.distance < bestGroundDist)
                    {
                        bestGroundDist = h.distance;
                        bestGroundHit = h;
                        hasGroundHit = true;
                    }
                }
            }

            if (hasGroundHit)
            {
                y = bestGroundHit.point.y + 1.2f;
            }

            var heroPrefab = Resources.Load<GameObject>("Hero/Hero");
            GameObject hero;
            if (heroPrefab != null)
            {
                hero = Instantiate(heroPrefab, transform);
                hero.name = "Hero";
                hero.transform.localScale = hero.transform.localScale * 1.8f;
                var groundY = _hasHeroSpawnOverride
                    ? _heroSpawnOverride.y
                    : (hasGroundHit ? bestGroundHit.point.y : heights[xIndex]);
                var heroRb = hero.GetComponent<Rigidbody2D>();
                var heroCol = hero.GetComponent<CapsuleCollider2D>();

                var spawnPos = new Vector2(_hasHeroSpawnOverride ? _heroSpawnOverride.x : xWorld, groundY + 0.05f);
                if (!_hasHeroSpawnOverride && heroCol != null)
                {
                    var mapW1v1 = heights.Length;
                    var leftMin = Mathf.Clamp(mapW1v1 * 0.06f, 0.5f, mapW1v1 - 0.5f);
                    var leftMax = Mathf.Clamp(mapW1v1 * 0.30f, 0.5f, mapW1v1 - 0.5f);
                    var rightMin = Mathf.Clamp(mapW1v1 * 0.70f, 0.5f, mapW1v1 - 0.5f);
                    var rightMax = Mathf.Clamp(mapW1v1 * 0.94f, 0.5f, mapW1v1 - 0.5f);

                    var surfaceLeft = GetSurfaceSpawnInRange(heights, leftMin, leftMax);
                    var surfaceRight = GetSurfaceSpawnInRange(heights, rightMin, rightMax);

                    var caveLeftOk = TryFindVoidSpawnInRange(heights, leftMin, leftMax, heroCol.bounds.size, out var caveLeft);
                    var caveRightOk = TryFindVoidSpawnInRange(heights, rightMin, rightMax, heroCol.bounds.size, out var caveRight);

                    // Rule: one in cave, the other outside (surface), on opposite edges.
                    if (caveLeftOk)
                    {
                        spawnPos = caveLeft;
                        _pendingHero2Spawn = surfaceRight;
                    }
                    else if (caveRightOk)
                    {
                        spawnPos = surfaceLeft;
                        _pendingHero2Spawn = caveRight;
                    }
                    else
                    {
                        spawnPos = surfaceLeft;
                        _pendingHero2Spawn = surfaceRight;
                    }
                }

                if (heroCol != null)
                {
                    spawnPos = LiftOutOfGround(spawnPos, heroCol.bounds.size);
                }

                if (heroRb != null)
                {
                    heroRb.gravityScale = Mathf.Max(9.0f, heroRb.gravityScale * 2.0f);
                    heroRb.position = spawnPos;
                    hero.transform.position = new Vector3(spawnPos.x, spawnPos.y, 0f);
#if UNITY_6000_0_OR_NEWER
                    heroRb.linearVelocity = Vector2.zero;
                    heroRb.angularVelocity = 0f;
#else
                    heroRb.velocity = Vector2.zero;
                    heroRb.angularVelocity = 0f;
#endif
                    Physics2D.SyncTransforms();
                }
                else
                {
                    hero.transform.position = new Vector3(spawnPos.x, spawnPos.y, 0f);
                    Physics2D.SyncTransforms();
                }
                Debug.Log($"[Stage1] Hero prefab components: rb={(heroRb != null)} bodyType={(heroRb != null ? heroRb.bodyType.ToString() : "null")} simulated={(heroRb != null && heroRb.simulated)} constraints={(heroRb != null ? heroRb.constraints.ToString() : "null")} col={(heroCol != null)} colSize={(heroCol != null ? heroCol.size.ToString() : "null")} colOffset={(heroCol != null ? heroCol.offset.ToString() : "null")}");
                Debug.Log($"[Stage1] SpawnHero instantiated prefab 'Resources/Hero/Hero' scene='{hero.scene.name}' worldPos={hero.transform.position} under='{transform.name}'");

                if (hero.GetComponent<WormAimController>() == null)
                {
                    hero.AddComponent<WormAimController>();
                }
                if (hero.GetComponent<GrappleController>() == null)
                {
                    hero.AddComponent<GrappleController>();
                }

                if (hero.GetComponent<HeroGrenadeThrower>() == null)
                {
                    hero.AddComponent<HeroGrenadeThrower>();
                }
                if (hero.GetComponent<HeroClawGun>() == null)
                {
                    hero.AddComponent<HeroClawGun>();
                }
                if (hero.GetComponent<HeroAmmoCarousel>() == null)
                {
                    hero.AddComponent<HeroAmmoCarousel>();
                }

                if (hero.GetComponent<HeroSurfaceWalker>() == null)
                {
                    hero.AddComponent<HeroSurfaceWalker>();
                }

                // Disable old SimpleHero if present (new walker handles movement)
                var oldHero = hero.GetComponent<SimpleHero>();
                if (oldHero != null)
                {
                    oldHero.enabled = false;
                }

                var health = hero.GetComponent<SimpleHealth>();
                if (health == null)
                {
                    health = hero.AddComponent<SimpleHealth>();
                }
                health.SetMaxHp(150, refill: true);

                var id = hero.GetComponent<PlayerIdentity>();
                if (id == null)
                {
                    id = hero.AddComponent<PlayerIdentity>();
                }
                // Team battle (3v3)
                var spiderSpritePrefab = Resources.Load<Sprite>("Hero/spider");
                var redSpritePrefab = Resources.Load<Sprite>("Hero/red_enemy");

                var spawnCountPerTeamPrefab = Mathf.Clamp(Bootstrap.SelectedTeamSize, 1, 5);
                var totalPlayersPrefab = 2 * spawnCountPerTeamPrefab;
                var spawnPointsPrefab = new Vector2[totalPlayersPrefab];
                var hasSpawnPrefab = new bool[totalPlayersPrefab];

                var heroSizePrefab = heroCol != null ? (Vector2)heroCol.bounds.size : new Vector2(1.0f, 1.5f);
                var mapW = heights.Length;
                var anyCaveSpawnPrefab = false;

                var caveSpawns = BuildCaveSpawnPoints(heroSizePrefab, heights);
                var usedCaveSpawn = new bool[caveSpawns.Count];

                bool TryPickCaveSpawnForTeam(int team, out Vector2 p)
                {
                    p = default;
                    if (caveSpawns == null || caveSpawns.Count == 0) return false;

                    var splitX = mapW * 0.5f;
                    for (var attempt = 0; attempt < 32; attempt++)
                    {
                        var idx = UnityEngine.Random.Range(0, caveSpawns.Count);
                        if (idx < 0 || idx >= caveSpawns.Count) continue;
                        if (usedCaveSpawn[idx]) continue;

                        var c = caveSpawns[idx];
                        if (team == 0 && c.x > splitX) continue;
                        if (team == 1 && c.x < splitX) continue;

                        usedCaveSpawn[idx] = true;
                        p = c;
                        return true;
                    }

                    // fallback: any unused cave
                    for (var i = 0; i < caveSpawns.Count; i++)
                    {
                        if (usedCaveSpawn[i]) continue;
                        usedCaveSpawn[i] = true;
                        p = caveSpawns[i];
                        return true;
                    }

                    return false;
                }

                bool IsFarFromExisting(Vector2 p)
                {
                    var minDist = Mathf.Max(0.8f, heroSizePrefab.x * 1.25f);
                    var minDist2 = minDist * minDist;
                    for (var si = 0; si < spawnPointsPrefab.Length; si++)
                    {
                        if (!hasSpawnPrefab[si]) continue;
                        if ((spawnPointsPrefab[si] - p).sqrMagnitude < minDist2) return false;
                    }
                    return true;
                }

                Vector2 FindSpawnForTeam(int team, bool forceCave)
                {
                    var minFrac = team == 0 ? 0.08f : 0.60f;
                    var maxFrac = team == 0 ? 0.40f : 0.92f;
                    var xMin = Mathf.Clamp(mapW * minFrac, 0.5f, mapW - 0.5f);
                    var xMax = Mathf.Clamp(mapW * maxFrac, 0.5f, mapW - 0.5f);

                    if (forceCave)
                    {
                        if (TryPickCaveSpawnForTeam(team, out var caveP))
                        {
                            anyCaveSpawnPrefab = true;
                            return LiftOutOfGround(caveP, heroSizePrefab);
                        }
                    }

                    for (var attempt = 0; attempt < 160; attempt++)
                    {
                        var preferCave = forceCave || UnityEngine.Random.value < 0.55f;
                        Vector2 p;
                        var ok = false;

                        if (preferCave)
                        {
                            ok = TryFindVoidSpawnInRange(heights, xMin, xMax, heroSizePrefab, out p);
                            if (!ok)
                            {
                                p = GetSurfaceSpawnInRange(heights, xMin, xMax);
                                ok = true;
                            }
                            else
                            {
                                anyCaveSpawnPrefab = true;
                            }
                        }
                        else
                        {
                            p = GetSurfaceSpawnInRange(heights, xMin, xMax);
                            ok = true;
                        }

                        if (!ok) continue;

                        p = LiftOutOfGround(p, heroSizePrefab);
                        if (!IsFarFromExisting(p)) continue;
                        return p;
                    }

                    // Fallback: surface mid
                    return LiftOutOfGround(GetSurfaceSpawnInRange(heights, team == 0 ? mapW * 0.25f : mapW * 0.75f, team == 0 ? mapW * 0.25f : mapW * 0.75f), heroSizePrefab);
                }

                // Precompute spawn points for all players.
                for (var team = 0; team < 2; team++)
                {
                    for (var pi = 0; pi < spawnCountPerTeamPrefab; pi++)
                    {
                        var flatIndex = team * spawnCountPerTeamPrefab + pi;
                        var lastPick = (team == 1 && pi == spawnCountPerTeamPrefab - 1);
                        var forceCave = (team == 0 && pi == 0) || (lastPick && !anyCaveSpawnPrefab);
                        var p = FindSpawnForTeam(team, forceCave);
                        spawnPointsPrefab[flatIndex] = p;
                        hasSpawnPrefab[flatIndex] = true;
                    }
                }

                // Configure and duplicate base hero into 6 total players.
                Destroy(hero);

                for (var team = 0; team < 2; team++)
                {
                    for (var pi = 0; pi < spawnCountPerTeamPrefab; pi++)
                    {
                        var flatIndex = team * spawnCountPerTeamPrefab + pi;
                        var p = spawnPointsPrefab[flatIndex];

                        var hgo = Instantiate(heroPrefab, transform);
                        hgo.name = $"Hero_T{team}_{pi}";
                        hgo.transform.localScale = hgo.transform.localScale * 1.8f;

                        var rb2 = hgo.GetComponent<Rigidbody2D>();
                        var col2 = hgo.GetComponent<CapsuleCollider2D>();
                        if (col2 != null)
                        {
                            p = LiftOutOfGround(p, col2.bounds.size);
                        }

                        if (rb2 != null)
                        {
                            rb2.gravityScale = Mathf.Max(9.0f, rb2.gravityScale * 2.0f);
                            rb2.position = p;
                            hgo.transform.position = new Vector3(p.x, p.y, 0f);
#if UNITY_6000_0_OR_NEWER
                            rb2.linearVelocity = Vector2.zero;
                            rb2.angularVelocity = 0f;
#else
                            rb2.velocity = Vector2.zero;
                            rb2.angularVelocity = 0f;
#endif
                            Physics2D.SyncTransforms();
                        }
                        else
                        {
                            hgo.transform.position = new Vector3(p.x, p.y, 0f);
                        }

                        if (hgo.GetComponent<WormAimController>() == null)
                        {
                            hgo.AddComponent<WormAimController>();
                        }
                        if (hgo.GetComponent<GrappleController>() == null)
                        {
                            hgo.AddComponent<GrappleController>();
                        }
                        if (hgo.GetComponent<HeroGrenadeThrower>() == null)
                        {
                            hgo.AddComponent<HeroGrenadeThrower>();
                        }
                        if (hgo.GetComponent<HeroClawGun>() == null)
                        {
                            hgo.AddComponent<HeroClawGun>();
                        }
                        if (hgo.GetComponent<HeroAmmoCarousel>() == null)
                        {
                            hgo.AddComponent<HeroAmmoCarousel>();
                        }
                        if (hgo.GetComponent<HeroSurfaceWalker>() == null)
                        {
                            hgo.AddComponent<HeroSurfaceWalker>();
                        }

                        // Disable old SimpleHero if present (new walker handles movement)
                        var old = hgo.GetComponent<SimpleHero>();
                        if (old != null)
                        {
                            old.enabled = false;
                        }

                        var hh = hgo.GetComponent<SimpleHealth>();
                        if (hh == null) hh = hgo.AddComponent<SimpleHealth>();
                        hh.SetMaxHp(150, refill: true);

                        var pid = hgo.GetComponent<PlayerIdentity>();
                        if (pid == null) pid = hgo.AddComponent<PlayerIdentity>();
                        pid.TeamIndex = team;
                        pid.PlayerIndex = pi;
                        pid.PlayerName = team == 0 ? $"Spider {pi + 1}" : $"Red {pi + 1}";

                        if (Bootstrap.VsCpu && team == 1)
                        {
                            var bot = hgo.GetComponent<WormCrawlerPrototype.AI.SpiderBotController>();
                            if (bot == null)
                            {
                                bot = hgo.AddComponent<WormCrawlerPrototype.AI.SpiderBotController>();
                            }

                            bot.SetDifficulty(Bootstrap.CpuDifficulty);
                        }

                        RemoveDuplicateChildByName(hgo.transform, "AimReticle");

                        // Apply team visuals.
                        var srTeam = hgo.GetComponentInChildren<SpriteRenderer>(true);
                        if (srTeam != null)
                        {
                            if (team == 0)
                            {
                                if (spiderSpritePrefab != null) srTeam.sprite = spiderSpritePrefab;
                                srTeam.color = Color.white;
                            }
                            else
                            {
                                if (redSpritePrefab != null)
                                {
                                    srTeam.sprite = redSpritePrefab;
                                    srTeam.color = Color.white;
                                }
                                else
                                {
                                    if (spiderSpritePrefab != null) srTeam.sprite = spiderSpritePrefab;
                                    srTeam.color = new Color(1f, 0.35f, 0.35f, 1f);
                                }
                            }
                        }
                    }
                }

                EnsureTurnManager();
                return;
            }

            hero = new GameObject("Hero");
            hero.transform.SetParent(transform, false);
            hero.transform.position = _hasHeroSpawnOverride
                ? new Vector3(_heroSpawnOverride.x, _heroSpawnOverride.y + 0.05f, 0f)
                : new Vector3(xWorld, y, 0f);
            hero.transform.localScale = new Vector3(0.5f, 0.5f, 1f) * 1.8f;

            Debug.Log($"[Stage1] SpawnHero created Hero scene='{hero.scene.name}' worldPos={hero.transform.position} under='{transform.name}'");

            var sr = hero.AddComponent<SpriteRenderer>();
            if (_heroSprite == null)
            {
                _heroSprite = CreateSolidSprite(new Color(0.85f, 0.20f, 0.30f, 1f));
            }
            sr.sprite = _heroSprite;
            sr.sortingOrder = 10;

            var rb = hero.AddComponent<Rigidbody2D>();
            rb.gravityScale = 9.0f;
            rb.freezeRotation = true;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            var col = hero.AddComponent<CapsuleCollider2D>();
            col.direction = CapsuleDirection2D.Vertical;
            col.size = new Vector2(0.4f, 0.6f);
            col.offset = new Vector2(0f, 0.0f);

            hero.AddComponent<HeroSurfaceWalker>();

            if (hero.GetComponent<WormAimController>() == null)
            {
                hero.AddComponent<WormAimController>();
            }
            if (hero.GetComponent<GrappleController>() == null)
            {
                hero.AddComponent<GrappleController>();
            }
            if (hero.GetComponent<HeroGrenadeThrower>() == null)
            {
                hero.AddComponent<HeroGrenadeThrower>();
            }
            if (hero.GetComponent<HeroClawGun>() == null)
            {
                hero.AddComponent<HeroClawGun>();
            }
            if (hero.GetComponent<HeroAmmoCarousel>() == null)
            {
                hero.AddComponent<HeroAmmoCarousel>();
            }

            var heroHealth = hero.GetComponent<SimpleHealth>();
            if (heroHealth == null)
            {
                heroHealth = hero.AddComponent<SimpleHealth>();
            }
            heroHealth.SetMaxHp(150, refill: true);

            var heroId = hero.GetComponent<PlayerIdentity>();
            if (heroId == null) heroId = hero.AddComponent<PlayerIdentity>();
            // Team battle (3v3) fallback
            var spiderSprite = Resources.Load<Sprite>("Hero/spider");
            var redSprite = Resources.Load<Sprite>("Hero/red_enemy");

            var spawnCountPerTeam = 3;
            var totalPlayers = 2 * spawnCountPerTeam;
            var spawnPoints = new Vector2[totalPlayers];
            var hasSpawn = new bool[totalPlayers];

            var heroSize = col != null ? (Vector2)col.bounds.size : new Vector2(1.0f, 1.5f);
            var w = heights.Length;

            bool IsFarFromExistingFallback(Vector2 p)
            {
                var minDist = Mathf.Max(0.8f, heroSize.x * 1.25f);
                var minDist2 = minDist * minDist;
                for (var si = 0; si < spawnPoints.Length; si++)
                {
                    if (!hasSpawn[si]) continue;
                    if ((spawnPoints[si] - p).sqrMagnitude < minDist2) return false;
                }
                return true;
            }

            Vector2 FindSpawnForTeamFallback(int team)
            {
                var minFrac = team == 0 ? 0.08f : 0.60f;
                var maxFrac = team == 0 ? 0.40f : 0.92f;
                var xMin = Mathf.Clamp(w * minFrac, 0.5f, w - 0.5f);
                var xMax = Mathf.Clamp(w * maxFrac, 0.5f, w - 0.5f);

                for (var attempt = 0; attempt < 160; attempt++)
                {
                    var preferCave = UnityEngine.Random.value < 0.55f;
                    Vector2 p;
                    var ok = false;

                    if (preferCave)
                    {
                        ok = TryFindVoidSpawnInRange(heights, xMin, xMax, heroSize, out p);
                        if (!ok)
                        {
                            p = GetSurfaceSpawnInRange(heights, xMin, xMax);
                            ok = true;
                        }
                    }
                    else
                    {
                        p = GetSurfaceSpawnInRange(heights, xMin, xMax);
                        ok = true;
                    }

                    if (!ok) continue;

                    p = LiftOutOfGround(p, heroSize);
                    if (!IsFarFromExistingFallback(p)) continue;
                    return p;
                }

                return LiftOutOfGround(GetSurfaceSpawnInRange(heights, team == 0 ? w * 0.25f : w * 0.75f, team == 0 ? w * 0.25f : w * 0.75f), heroSize);
            }

            for (var team = 0; team < 2; team++)
            {
                for (var pi = 0; pi < spawnCountPerTeam; pi++)
                {
                    var flatIndex = team * spawnCountPerTeam + pi;
                    var p = FindSpawnForTeamFallback(team);
                    spawnPoints[flatIndex] = p;
                    hasSpawn[flatIndex] = true;
                }
            }

            var heroTemplate = hero;

            for (var team = 0; team < 2; team++)
            {
                for (var pi = 0; pi < spawnCountPerTeam; pi++)
                {
                    var flatIndex = team * spawnCountPerTeam + pi;
                    var p = spawnPoints[flatIndex];

                    var hgo = Instantiate(heroTemplate, transform);
                    hgo.name = $"Hero_T{team}_{pi}";
                    hgo.transform.position = new Vector3(p.x, p.y, 0f);
                    Physics2D.SyncTransforms();

                    RemoveDuplicateChildByName(hgo.transform, "AimReticle");

                    var hh = hgo.GetComponent<SimpleHealth>();
                    if (hh == null) hh = hgo.AddComponent<SimpleHealth>();
                    hh.SetMaxHp(150, refill: true);

                    var pid = hgo.GetComponent<PlayerIdentity>();
                    if (pid == null) pid = hgo.AddComponent<PlayerIdentity>();
                    pid.TeamIndex = team;
                    pid.PlayerIndex = pi;
                    pid.PlayerName = team == 0 ? $"Spider {pi + 1}" : $"Red {pi + 1}";

                    var srTeam = hgo.GetComponentInChildren<SpriteRenderer>(true);
                    if (srTeam != null)
                    {
                        if (team == 0)
                        {
                            if (spiderSprite != null) srTeam.sprite = spiderSprite;
                            srTeam.color = Color.white;
                        }
                        else
                        {
                            if (redSprite != null)
                            {
                                srTeam.sprite = redSprite;
                                srTeam.color = Color.white;
                            }
                            else
                            {
                                if (spiderSprite != null) srTeam.sprite = spiderSprite;
                                srTeam.color = new Color(1f, 0.35f, 0.35f, 1f);
                            }
                        }
                    }
                }
            }

            if (heroTemplate != null)
            {
                Destroy(heroTemplate);
            }

            EnsureTurnManager();
        }

        private Vector2 LiftOutOfGround(Vector2 pos, Vector2 capsuleSizeWorld)
        {
            var size = new Vector2(Mathf.Max(0.2f, capsuleSizeWorld.x * 0.9f), Mathf.Max(0.3f, capsuleSizeWorld.y * 0.9f));
            var p = pos;
            for (var i = 0; i < 40; i++)
            {
                var overlaps = Physics2D.OverlapCapsuleAll(p, size, CapsuleDirection2D.Vertical, angle: 0f, ~0);
                var blocked = false;
                if (overlaps != null)
                {
                    for (var oi = 0; oi < overlaps.Length; oi++)
                    {
                        var c = overlaps[oi];
                        if (c == null || c.isTrigger) continue;
                        if (!IsGroundPoly(c)) continue;
                        blocked = true;
                        break;
                    }
                }
                if (!blocked)
                {
                    return p;
                }

                p += Vector2.up * 0.15f;
            }

            return p;
        }

        private Vector2 _pendingHero2Spawn;

        private static bool IsGroundPoly(Collider2D c)
        {
            if (c == null)
            {
                return false;
            }
            return c.gameObject != null && c.gameObject.name == "GroundPoly";
        }

        private Vector2 GetSurfaceSpawnInRange(float[] heights, float xMin, float xMax)
        {
            if (heights == null || heights.Length == 0)
            {
                return Vector2.zero;
            }

            var w = heights.Length;
            xMin = Mathf.Clamp(xMin, 0.5f, w - 0.5f);
            xMax = Mathf.Clamp(xMax, xMin, w - 0.5f);

            for (var attempt = 0; attempt < 40; attempt++)
            {
                var x = UnityEngine.Random.Range(xMin, xMax);
                var xi = Mathf.Clamp(Mathf.RoundToInt(x), 0, w - 1);
                var y = heights[xi] + 0.10f;

                var from = new Vector2(x, y + 25f);
                var hits = Physics2D.RaycastAll(from, Vector2.down, 120f, ~0);
                if (hits == null) continue;
                for (var i = 0; i < hits.Length; i++)
                {
                    var h = hits[i];
                    if (h.collider == null || h.collider.isTrigger) continue;
                    if (!IsGroundPoly(h.collider)) continue;
                    return new Vector2(x, h.point.y + 0.05f);
                }
            }

            var midX = (xMin + xMax) * 0.5f;
            var midXi = Mathf.Clamp(Mathf.RoundToInt(midX), 0, w - 1);
            return new Vector2(midX, heights[midXi] + 0.10f);
        }

        private bool TryFindVoidSpawnInRange(float[] heights, float xMin, float xMax, Vector2 capsuleSizeWorld, out Vector2 spawn)
        {
            spawn = default;

            if (heights == null || heights.Length == 0)
            {
                return false;
            }

            var w = heights.Length;
            xMin = Mathf.Clamp(xMin, 0.5f, w - 0.5f);
            xMax = Mathf.Clamp(xMax, xMin, w - 0.5f);

            var halfH = Mathf.Max(0.15f, capsuleSizeWorld.y * 0.5f);
            var size = new Vector2(Mathf.Max(0.2f, capsuleSizeWorld.x * 0.9f), Mathf.Max(0.3f, capsuleSizeWorld.y * 0.9f));

            for (var attempt = 0; attempt < 120; attempt++)
            {
                var x = UnityEngine.Random.Range(xMin, xMax);
                var xi = Mathf.Clamp(Mathf.RoundToInt(x), 0, w - 1);
                var surfaceY = heights[xi];
                var yMin = terrainBottomY + 1.5f + halfH;
                var yMax = surfaceY - 1.0f;
                if (yMax <= yMin)
                {
                    continue;
                }

                var y = UnityEngine.Random.Range(yMin, yMax);
                var p = new Vector2(x, y);

                // Must be in air (no GroundPoly overlapping)
                {
                    var overlaps = Physics2D.OverlapCapsuleAll(p, size, CapsuleDirection2D.Vertical, angle: 0f, ~0);
                    var blocked = false;
                    if (overlaps != null)
                    {
                        for (var oi = 0; oi < overlaps.Length; oi++)
                        {
                            var oc = overlaps[oi];
                            if (oc == null || oc.isTrigger) continue;
                            if (!IsGroundPoly(oc)) continue;
                            blocked = true;
                            break;
                        }
                    }
                    if (blocked)
                    {
                        continue;
                    }
                }

                // Must have GroundPoly below
                var hits = Physics2D.RaycastAll(p, Vector2.down, 6.0f, ~0);
                if (hits == null || hits.Length == 0)
                {
                    continue;
                }
                for (var hi = 0; hi < hits.Length; hi++)
                {
                    var h = hits[hi];
                    if (h.collider == null || h.collider.isTrigger) continue;
                    if (!IsGroundPoly(h.collider)) continue;

                    var stand = new Vector2(p.x, h.point.y + 0.05f);
                    var overlapsAtStand = Physics2D.OverlapCapsuleAll(stand, size, CapsuleDirection2D.Vertical, angle: 0f, ~0);
                    var standBlocked = false;
                    if (overlapsAtStand != null)
                    {
                        for (var oi = 0; oi < overlapsAtStand.Length; oi++)
                        {
                            var oc = overlapsAtStand[oi];
                            if (oc == null || oc.isTrigger) continue;
                            if (!IsGroundPoly(oc)) continue;
                            standBlocked = true;
                            break;
                        }
                    }
                    if (standBlocked)
                    {
                        continue;
                    }

                    spawn = stand;
                    return true;
                }
            }

            return false;
        }

        private static void RemoveDuplicateChildByName(Transform root, string childName)
        {
            if (root == null || string.IsNullOrEmpty(childName))
            {
                return;
            }

            var first = true;
            for (var i = root.childCount - 1; i >= 0; i--)
            {
                var c = root.GetChild(i);
                if (c == null) continue;
                if (c.name != childName) continue;
                if (first)
                {
                    first = false;
                    continue;
                }
                Destroy(c.gameObject);
            }
        }

        private void EnsureTurnManager()
        {
            if (GameObject.Find("TurnManager") != null)
            {
                return;
            }

            var go = new GameObject("TurnManager");
            go.transform.SetParent(transform, false);
            go.AddComponent<TurnManager>();
        }

        private void OnDrawGizmosSelected()
        {
            if (_lastHeights == null || _lastHeights.Length < 2)
            {
                return;
            }

            Gizmos.color = Color.green;
            for (var x = 0; x < _lastHeights.Length - 1; x++)
            {
                var a = new Vector3(x, _lastHeights[x], 0f);
                var b = new Vector3(x + 1, _lastHeights[x + 1], 0f);
                Gizmos.DrawLine(a, b);
            }
        }
    }
}
