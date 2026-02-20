using UnityEngine;

namespace WormCrawlerPrototype
{
    public sealed class GrenadeProjectile : MonoBehaviour
    {
        public float Lifetime = 6f;
        public LayerMask GroundMask = ~0;

        public float SpinDegPerSecond = 0f;
        public float MaxWalkableSlopeDeg = 45f;
        public float TangentialDamping = 14f;

        public float ExplosionRadiusHeroHeights = 10f;
        public float CraterDepthHeroHeights = 1.0f;

        public float FuseAfterLandingSeconds = 4f;

        private Rigidbody2D _rb;
        private float _t;

        private bool _exploded;
        private bool _landed;
        private float _landedT;

        private Vector2 _landedPos;
        private bool _hasLandedPos;

        private static PhysicsMaterial2D s_NoFriction;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
        }

        public void SetNoFrictionMaterial()
        {
            var mat = GetNoFrictionMaterial();
            var cols = GetComponents<Collider2D>();
            if (cols == null)
            {
                return;
            }
            for (var i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null)
                {
                    cols[i].sharedMaterial = mat;
                }
            }
        }

        private static PhysicsMaterial2D GetNoFrictionMaterial()
        {
            if (s_NoFriction == null)
            {
                s_NoFriction = new PhysicsMaterial2D("GrenadeNoFriction")
                {
                    friction = 0.75f,
                    bounciness = 0f,
                    frictionCombine = PhysicsMaterialCombine2D.Maximum,
                    bounceCombine = PhysicsMaterialCombine2D.Minimum
                };
            }
            return s_NoFriction;
        }

        private void Update()
        {
            _t += Time.deltaTime;

            if (!_landed)
            {
                var spin = SpinDegPerSecond;
                if (Mathf.Abs(spin) > 0.01f)
                {
                    transform.Rotate(0f, 0f, -spin * Time.deltaTime);
                }
            }

            if (_landed)
            {
                _landedT += Time.deltaTime;
                if (_landedT >= Mathf.Max(0.01f, FuseAfterLandingSeconds))
                {
                    Explode();
                    return;
                }

                return;
            }
            if (_t >= Mathf.Max(0.01f, Lifetime))
            {
                Destroy(gameObject);
            }
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (_exploded)
            {
                return;
            }

            if (collision == null || collision.collider == null)
            {
                return;
            }

            var otherLayer = collision.collider.gameObject.layer;
            if (((1 << otherLayer) & GroundMask.value) == 0)
            {
                return;
            }

            if (collision.collider.gameObject.name != "GroundPoly")
            {
                return;
            }

            if (!_landed)
            {
                _landed = true;
                _landedT = 0f;

                _landedPos = (Vector2)transform.position;
                _hasLandedPos = true;
                if (collision.contactCount > 0)
                {
                    _landedPos = collision.GetContact(0).point;
                }
            }
        }

        private void FixedUpdate()
        {
            if (_rb == null)
            {
                return;
            }

            if (!_landed)
            {
                return;
            }

#if UNITY_6000_0_OR_NEWER
            var v = _rb.linearVelocity;
#else
            var v = _rb.velocity;
#endif

            if (v.sqrMagnitude < 0.0001f)
            {
                return;
            }

            var hit = Physics2D.Raycast(_rb.position, Vector2.down, 0.25f, GroundMask);
            if (hit.collider == null)
            {
                return;
            }

            var n = hit.normal.sqrMagnitude > 0.0001f ? hit.normal.normalized : Vector2.up;
            var tangent = new Vector2(n.y, -n.x);
            if (tangent.x < 0f)
            {
                tangent = -tangent;
            }

            var slopeToRight = Vector2.Angle(tangent, Vector2.right);
            if (slopeToRight > 90f)
            {
                slopeToRight = 180f - slopeToRight;
            }
            var canRest = slopeToRight <= Mathf.Clamp(MaxWalkableSlopeDeg, 0f, 89.9f);

            if (canRest)
            {
                var g = Physics2D.gravity * Mathf.Max(0f, _rb.gravityScale);
                var gAlongTangent = Vector2.Dot(g, tangent) * tangent;
                _rb.AddForce(-gAlongTangent * _rb.mass, ForceMode2D.Force);

                var damp = Mathf.Clamp01(Mathf.Max(0f, TangentialDamping) * Time.fixedDeltaTime);
                var vAlongTangent = Vector2.Dot(v, tangent) * tangent;
                var newV = v - vAlongTangent * damp;

#if UNITY_6000_0_OR_NEWER
                _rb.linearVelocity = newV;
#else
                _rb.velocity = newV;
#endif
                return;
            }

            var alongT = Vector2.Dot(v, tangent) * tangent;
            var alongN = Vector2.Dot(v, n) * n;

            if (alongN.y < 0f)
            {
                alongN = Vector2.zero;
            }

            var target = alongT + alongN;
            var blend = 0.25f;
#if UNITY_6000_0_OR_NEWER
            _rb.linearVelocity = Vector2.Lerp(v, target, blend);
#else
            _rb.velocity = Vector2.Lerp(v, target, blend);
#endif
        }

        private void Explode()
        {
            if (_exploded)
            {
                return;
            }
            _exploded = true;

            // Explode at the actual grenade position. The grenade can roll after first landing,
            // so using the initial contact point would explode at the aim/landing point instead of
            // the final resting place.
            var pos = (Vector2)transform.position;
            var heroH = ResolveHeroHeight();
            var explosionRadius = Mathf.Max(0.25f, heroH * Mathf.Max(0.01f, ExplosionRadiusHeroHeights));

            var spawnedPrefab = TrySpawnExplosionPrefab(pos, heroH, explosionRadius);
            if (!spawnedPrefab)
            {
                SpawnExplosionFx(pos, explosionRadius);
            }
            ApplyDamage(pos, heroH, explosionRadius);
            ApplyCrater(pos, heroH, explosionRadius);

            Destroy(gameObject);
        }

        private bool TrySpawnExplosionPrefab(Vector2 pos, float heroH, float explosionRadius)
        {
            var prefab = Resources.Load<GameObject>("Prefabs/Effects/Explosion");
            if (prefab == null)
            {
                return false;
            }

            var go = Instantiate(prefab, new Vector3(pos.x, pos.y, 0f), Quaternion.identity);
            if (go == null)
            {
                return false;
            }

            var ctrl = go.GetComponent<ExplosionController>();
            if (ctrl != null)
            {
                ctrl.Initialize(radiusWorld: explosionRadius, heroHeight: heroH, ground: GroundMask, enemies: ~0);
                ctrl.DisableGameplayEffects();
            }

            return true;
        }

        private static float ResolveHeroHeight()
        {
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
            if (col == null)
            {
                return 1f;
            }

            return Mathf.Max(0.25f, col.bounds.size.y);
        }

        private static void SpawnExplosionFx(Vector2 pos, float explosionRadius)
        {
            var fxGo = new GameObject("GrenadeExplosionFX");
            fxGo.transform.position = new Vector3(pos.x, pos.y, 0f);

            var fx = fxGo.AddComponent<GrenadeExplosionFx>();
            fx.Configure(targetDiameterWorld: explosionRadius * 2f, sortingOrderOverride: 60);
        }

        private static void ApplyDamage(Vector2 pos, float heroH, float explosionRadius)
        {
            var cols = Physics2D.OverlapCircleAll(pos, explosionRadius);
            if (cols == null || cols.Length == 0)
            {
                return;
            }

            var dmg = 75;

            for (var i = 0; i < cols.Length; i++)
            {
                var c = cols[i];
                if (c == null || c.isTrigger)
                {
                    continue;
                }

                var health = c.GetComponentInParent<SimpleHealth>();
                if (health == null)
                {
                    continue;
                }

                if (dmg > 0)
                {
                    health.TakeDamage(dmg, DamageSource.GrenadeExplosion);
                }
            }
        }

        private void ApplyCrater(Vector2 pos, float heroH, float explosionRadius)
        {
            var depth = Mathf.Max(0.05f, heroH * Mathf.Max(0.01f, CraterDepthHeroHeights));

            SimpleWorldGenerator gen;
#if UNITY_6000_0_OR_NEWER
            gen = Object.FindFirstObjectByType<SimpleWorldGenerator>();
#else
            gen = Object.FindObjectOfType<SimpleWorldGenerator>();
#endif
            if (gen != null)
            {
                var ok = gen.CarveCraterWorld(pos, Mathf.Max(0.01f, explosionRadius));
                if (ok)
                {
                    return;
                }
            }

            PolygonCollider2D[] polys;
#if UNITY_6000_0_OR_NEWER
            polys = Object.FindObjectsByType<PolygonCollider2D>(FindObjectsSortMode.None);
#else
            polys = Object.FindObjectsOfType<PolygonCollider2D>();
#endif
            PolygonCollider2D poly = null;
            for (var i = 0; i < polys.Length; i++)
            {
                var p = polys[i];
                if (p != null && p.gameObject != null && p.gameObject.name == "GroundPoly")
                {
                    poly = p;
                    break;
                }
            }
            if (poly == null || poly.pathCount <= 0)
            {
                return;
            }

            var path = poly.GetPath(0);
            if (path == null || path.Length < 5)
            {
                return;
            }

            var bottomY = Mathf.Min(path[path.Length - 1].y, path[path.Length - 2].y);
            var topCount = path.Length - 2;
            var r = Mathf.Max(0.01f, explosionRadius);
            var changed = false;

            for (var i = 0; i < topCount; i++)
            {
                var p = path[i];
                var dx = p.x - pos.x;
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
                        var dx = v.x - pos.x;
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

            Physics2D.SyncTransforms();
        }
    }
}
