using UnityEngine;

namespace WormCrawlerPrototype
{
    public sealed class ExplosionController : MonoBehaviour
    {
        private static bool s_CarveCraterWarned;

        [Header("Timing")]
        [SerializeField] private float peakTimeSeconds = 0.4f;
        [SerializeField] private float lifetimeSeconds = 0.8f;
        [SerializeField] private bool explodeByAnimationEvent = false;

        [Header("Radius")]
        [SerializeField] private float explosionRadiusMultiplier = 3.5f;
        [SerializeField] private float craterDepthHeroHeights = 1.0f;

        [Header("Damage")]
        [SerializeField] private float maxDamage = 50f;
        [SerializeField] private LayerMask enemyMask = ~0;
        [SerializeField] private float knockbackImpulse = 10f;

        [Header("Terrain")]
        [SerializeField] private LayerMask groundMask = ~0;

        private Animator _anim;
        private SpriteRenderer _sr;
        private bool _exploded;

        private readonly Collider2D[] _enemyHits = new Collider2D[32];
        private readonly Collider2D[] _groundHits = new Collider2D[64];

        private float _radiusOverride;
        private float _heroHeightOverride;

        private float _damageMultiplier = 1f;
        private float _knockbackMultiplier = 1f;

        private bool _gameplayDisabled;

        private float _cachedHeroH;
        private float _cachedRadius;

        public void Initialize(float radiusWorld, float heroHeight, LayerMask ground, LayerMask enemies)
        {
            _radiusOverride = Mathf.Max(0f, radiusWorld);
            _heroHeightOverride = Mathf.Max(0f, heroHeight);
            if (ground.value != 0)
            {
                groundMask = ground;
            }
            if (enemies.value != 0)
            {
                enemyMask = enemies;
            }
        }

        public void ForceSetGameplayMasks(LayerMask ground, LayerMask enemies)
        {
            groundMask = ground;
            enemyMask = enemies;
        }

        public void DisableGameplayEffects()
        {
            ForceSetGameplayMasks(ground: 0, enemies: 0);
            _damageMultiplier = 0f;
            _knockbackMultiplier = 0f;
            _gameplayDisabled = true;
        }

        public void SetDamageMultiplier(float multiplier)
        {
            _damageMultiplier = Mathf.Max(0f, multiplier);
        }

        public void SetKnockbackMultiplier(float multiplier)
        {
            _knockbackMultiplier = Mathf.Max(0f, multiplier);
        }

        private void Awake()
        {
            _anim = GetComponent<Animator>();
            _sr = GetComponent<SpriteRenderer>();
        }

        private void Start()
        {
            _cachedHeroH = ResolveHeroHeight();
            _cachedRadius = ResolveRadius(_cachedHeroH);
            ApplyVisualScale(_cachedRadius);

            if (_anim != null)
            {
                _anim.Play(0, 0, 0f);
            }

            if (!explodeByAnimationEvent)
            {
                if (peakTimeSeconds > 0.0001f)
                {
                    Invoke(nameof(Explode), peakTimeSeconds);
                }
                else
                {
                    Explode();
                }
            }

            if (lifetimeSeconds > 0.0001f)
            {
                Destroy(gameObject, lifetimeSeconds);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void Explode()
        {
            if (_exploded)
            {
                return;
            }
            _exploded = true;

            var center = (Vector2)transform.position;
            var heroH = _cachedHeroH > 0.0001f ? _cachedHeroH : ResolveHeroHeight();
            var radius = _cachedRadius > 0.0001f ? _cachedRadius : ResolveRadius(heroH);

            Debug.Log($"[Explosion] radius={radius:F2} heroH={heroH:F2}");

            if (!_gameplayDisabled)
            {
                ApplyDamage(center, radius);
                ApplyCrater(center, heroH, radius);
            }
        }

        // Call this from an AnimationEvent on the explosion clip (e.g. at frame 4).
        public void AnimationEvent_Explode()
        {
            Explode();
        }

        private void ApplyVisualScale(float radius)
        {
            if (_sr == null)
            {
                return;
            }

            var desiredDiameter = Mathf.Max(0.01f, radius) * 2f;
            var spriteDiameter = 1f;
            if (_sr.sprite != null)
            {
                var b = _sr.sprite.bounds;
                spriteDiameter = Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.y));
            }

            var s = desiredDiameter / spriteDiameter;
            transform.localScale = new Vector3(s, s, 1f);
        }

        private float ResolveHeroHeight()
        {
            if (_heroHeightOverride > 0.0001f)
            {
                return _heroHeightOverride;
            }

            var hero = GameObject.Find("Hero");
            if (hero == null)
            {
                return 1f;
            }

            var col = hero.GetComponent<Collider2D>();
            if (col == null)
            {
                col = hero.GetComponentInChildren<Collider2D>();
            }

            return col != null ? Mathf.Max(0.25f, col.bounds.size.y) : 1f;
        }

        private float ResolveRadius(float heroH)
        {
            if (_radiusOverride > 0.0001f)
            {
                return _radiusOverride;
            }

            return Mathf.Max(0.25f, heroH * Mathf.Max(0.01f, explosionRadiusMultiplier));
        }

        private void ApplyDamage(Vector2 center, float radius)
        {
            radius = Mathf.Max(0.01f, radius);

            var filter = new ContactFilter2D();
            filter.useLayerMask = true;
            filter.SetLayerMask(enemyMask);
            filter.useTriggers = false;
            var count = Physics2D.OverlapCircle(center, radius, filter, _enemyHits);
            for (var i = 0; i < count; i++)
            {
                var c = _enemyHits[i];
                _enemyHits[i] = null;
                if (c == null || c.isTrigger)
                {
                    continue;
                }

                var health = c.GetComponentInParent<SimpleHealth>();
                if (health == null)
                {
                    continue;
                }

                var p = (Vector2)c.bounds.center;
                var dist = Vector2.Distance(center, p);
                var t = Mathf.Clamp01(dist / radius);
                var dmg = Mathf.RoundToInt(Mathf.Max(0f, maxDamage) * _damageMultiplier);
                if (dmg > 0)
                {
                    health.TakeDamage(dmg);
                }

                var rb = c.GetComponentInParent<Rigidbody2D>();
                if (rb != null)
                {
                    var away = ((Vector2)c.bounds.center - center);
                    if (away.sqrMagnitude < 0.0001f)
                    {
                        away = Vector2.up;
                    }
                    away.Normalize();
                    var imp = Mathf.Max(0f, knockbackImpulse) * _knockbackMultiplier * (1f - t);
                    if (imp > 0.0001f)
                    {
                        rb.AddForce(away * imp, ForceMode2D.Impulse);
                    }
                }
            }
        }

        private void ApplyCrater(Vector2 center, float heroH, float radius)
        {
            var depth = Mathf.Max(0.05f, heroH * Mathf.Max(0.01f, craterDepthHeroHeights));

            var carveCenter = center;
            {
                var nearFilter = new ContactFilter2D();
                nearFilter.useLayerMask = true;
                nearFilter.SetLayerMask(groundMask);
                nearFilter.useTriggers = false;
                var nearCount = Physics2D.OverlapCircle(center, Mathf.Max(0.01f, radius) * 2f, nearFilter, _groundHits);
                var bestDist2 = float.PositiveInfinity;
                for (var i = 0; i < nearCount; i++)
                {
                    var c = _groundHits[i];
                    _groundHits[i] = null;
                    if (c == null || c.isTrigger) continue;

                    var p = c.ClosestPoint(center);
                    var d2 = ((Vector2)p - center).sqrMagnitude;
                    if (d2 < bestDist2)
                    {
                        bestDist2 = d2;
                        carveCenter = p;
                    }
                }
            }

            if (TryCarveBitmapCrater(carveCenter, radius))
            {
                return;
            }

            var overlapFilter = new ContactFilter2D();
            overlapFilter.useLayerMask = true;
            overlapFilter.SetLayerMask(groundMask);
            overlapFilter.useTriggers = false;
            var overlapCount = Physics2D.OverlapCircle(carveCenter, Mathf.Max(0.01f, radius), overlapFilter, _groundHits);
            for (var i = 0; i < overlapCount; i++)
            {
                var c = _groundHits[i];
                _groundHits[i] = null;
                if (c == null || c.isTrigger)
                {
                    continue;
                }

                if (c is PolygonCollider2D poly)
                {
                    ApplyCraterPolygon(poly, carveCenter, radius, depth);
                    continue;
                }
            }

            Physics2D.SyncTransforms();
        }

        private bool TryCarveBitmapCrater(Vector2 center, float radius)
        {
            SimpleWorldGenerator gen;
#if UNITY_6000_0_OR_NEWER
            gen = Object.FindFirstObjectByType<SimpleWorldGenerator>();
#else
            gen = Object.FindObjectOfType<SimpleWorldGenerator>();
#endif
            if (gen == null)
            {
                return false;
            }

            var ok = gen.CarveCraterWorld(center, Mathf.Max(0.01f, radius));
            if (!ok)
            {
                if (!_gameplayDisabled && !s_CarveCraterWarned)
                {
                    Debug.LogWarning("[Explosion] CarveCraterWorld failed (runtime terrain not initialized or unsupported terrain mode).");
                    s_CarveCraterWarned = true;
                }
            }
            return ok;
        }

        private static void ApplyCraterPolygon(PolygonCollider2D poly, Vector2 center, float radius, float depth)
        {
            if (poly.pathCount <= 0)
            {
                return;
            }

            var localCenter = (Vector2)poly.transform.InverseTransformPoint(center);

            var path = poly.GetPath(0);
            if (path == null || path.Length < 5)
            {
                return;
            }

            var bottomY = Mathf.Min(path[path.Length - 1].y, path[path.Length - 2].y);
            var topCount = path.Length - 2;
            var r = Mathf.Max(0.01f, radius);
            var changed = false;

            for (var i = 0; i < topCount; i++)
            {
                var p = path[i];
                var dx = p.x - localCenter.x;
                var adx = Mathf.Abs(dx);
                if (adx > r)
                {
                    continue;
                }

                var t = dx / r;
                var parab = 1f - (t * t);
                var y = p.y - depth * parab;
                y = Mathf.Max(bottomY + 0.05f, y);
                if (y < p.y - 0.0001f)
                {
                    path[i] = new Vector2(p.x, y);
                    changed = true;
                }
            }

            if (!changed)
            {
                return;
            }

            poly.SetPath(0, path);

            var mf = poly.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                var mesh = mf.mesh;
                var verts = mesh.vertices;
                if (verts != null && verts.Length >= 4)
                {
                    var topVertCount = verts.Length / 2;
                    topVertCount = Mathf.Clamp(topVertCount, 0, verts.Length);
                    for (var vi = 0; vi < topVertCount; vi++)
                    {
                        var v = verts[vi];
                        var dx = v.x - localCenter.x;
                        var adx = Mathf.Abs(dx);
                        if (adx > r)
                        {
                            continue;
                        }

                        var t = dx / r;
                        var parab = 1f - (t * t);
                        v.y = Mathf.Max(bottomY, v.y - depth * parab);
                        verts[vi] = v;
                    }
                    mesh.vertices = verts;
                    mesh.RecalculateBounds();
                }
            }
        }

        private static void ApplyCraterEdge(EdgeCollider2D edge, Vector2 center, float radius, float depth)
        {
            var pts = edge.points;
            if (pts == null || pts.Length < 2)
            {
                return;
            }

            var t = edge.transform;
            var r = Mathf.Max(0.01f, radius);
            var changed = false;

            for (var i = 0; i < pts.Length; i++)
            {
                var world = (Vector2)t.TransformPoint(pts[i]);
                var dx = world.x - center.x;
                var adx = Mathf.Abs(dx);
                if (adx > r)
                {
                    continue;
                }

                var u = dx / r;
                var parab = 1f - (u * u);
                var y = world.y - depth * parab;
                if (y < world.y - 0.0001f)
                {
                    world.y = y;
                    pts[i] = (Vector2)t.InverseTransformPoint(world);
                    changed = true;
                }
            }

            if (changed)
            {
                edge.points = pts;
            }
        }
    }
}
