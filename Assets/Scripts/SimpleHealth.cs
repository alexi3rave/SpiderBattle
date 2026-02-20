using UnityEngine;

namespace WormCrawlerPrototype
{
    public enum DamageSource
    {
        Generic = 0,
        GrenadeExplosion = 1,
        HeroDeathExplosion = 2,
        Fall = 3,
        AutoGun = 4,
        ClawGun = 5,
    }

    public sealed class SimpleHealth : MonoBehaviour
    {
        [SerializeField] private int maxHp = 100;
        [SerializeField] private int hp = 100;

        [SerializeField] private float deathExplosionRadiusHeroHeights = 1.75f;
        [SerializeField] private float deathExplosionStrengthMultiplier = 0.5f;

        public int HP => hp;
        public int MaxHP => maxHp;

        public static event System.Action<SimpleHealth, int, DamageSource> Damaged;

        private void Awake()
        {
            maxHp = Mathf.Max(1, maxHp);
            hp = Mathf.Clamp(hp, 0, maxHp);
        }

        public void SetMaxHp(int value, bool refill)
        {
            maxHp = Mathf.Max(1, value);
            if (refill)
            {
                hp = maxHp;
            }
            else
            {
                hp = Mathf.Clamp(hp, 0, maxHp);
            }
        }

        public void TakeDamage(int amount)
        {
            TakeDamage(amount, DamageSource.Generic);
        }

        public void TakeDamage(int amount, DamageSource source)
        {
            amount = Mathf.Max(0, amount);
            if (amount <= 0)
            {
                return;
            }

            hp = Mathf.Max(0, hp - amount);
            Damaged?.Invoke(this, amount, source);

            if (hp <= 0)
            {
                SpawnHeroDeathExplosionFxAndDamage();
                Destroy(gameObject);
            }
        }

        private void SpawnHeroDeathExplosionFxAndDamage()
        {
            var col = GetComponent<Collider2D>();
            if (col == null)
            {
                col = GetComponentInChildren<Collider2D>();
            }

            var heroH = col != null ? Mathf.Max(0.25f, col.bounds.size.y) : 1f;
            var baseGrenadeRadius = Mathf.Max(0.25f, heroH * Mathf.Max(0.01f, deathExplosionRadiusHeroHeights));
            var strength = Mathf.Clamp01(Mathf.Max(0.01f, deathExplosionStrengthMultiplier));
            var radius = Mathf.Max(0.05f, baseGrenadeRadius * strength);

            var pos2 = (Vector2)transform.position;
            if (col != null)
            {
                pos2 = col.bounds.center;
            }

            if (!TrySpawnHeroDeathExplosionPrefab(pos2, heroH, radius, strength))
            {
                SpawnHeroDeathExplosionFx(pos2, radius);
                ApplyHeroDeathDamageAndKnockback(pos2, radius, strength);
                ApplyHeroDeathCrater(pos2, radius);
            }
        }

        private static bool TrySpawnHeroDeathExplosionPrefab(Vector2 pos, float heroH, float radius, float strength)
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
                ctrl.Initialize(radiusWorld: radius, heroHeight: heroH, ground: ~0, enemies: ~0);
                ctrl.SetDamageMultiplier(strength);
                ctrl.SetKnockbackMultiplier(strength);
            }

            return true;
        }

        private static void SpawnHeroDeathExplosionFx(Vector2 pos, float explosionRadius)
        {
            var fxGo = new GameObject("HeroDeathFX");
            fxGo.transform.position = new Vector3(pos.x, pos.y, 0f);

            var fx = fxGo.AddComponent<GrenadeExplosionFx>();
            fx.Configure(targetDiameterWorld: explosionRadius * 2f, sortingOrderOverride: 60);
        }

        private static void ApplyHeroDeathDamageAndKnockback(Vector2 center, float radius, float strength)
        {
            radius = Mathf.Max(0.01f, radius);
            strength = Mathf.Clamp01(Mathf.Max(0f, strength));

            var cols = Physics2D.OverlapCircleAll(center, radius);
            if (cols == null || cols.Length == 0)
            {
                return;
            }

            var dmg = Mathf.RoundToInt(50f * strength);
            var baseImpulse = 10f * strength;

            for (var i = 0; i < cols.Length; i++)
            {
                var c = cols[i];
                if (c == null || c.isTrigger)
                {
                    continue;
                }

                var health = c.GetComponentInParent<SimpleHealth>();
                if (health != null && dmg > 0)
                {
                    health.TakeDamage(dmg, DamageSource.HeroDeathExplosion);
                }

                var rb = c.GetComponentInParent<Rigidbody2D>();
                if (rb != null && baseImpulse > 0.0001f)
                {
                    var away = ((Vector2)c.bounds.center - center);
                    if (away.sqrMagnitude < 0.0001f)
                    {
                        away = Vector2.up;
                    }
                    away.Normalize();
                    rb.AddForce(away * baseImpulse, ForceMode2D.Impulse);
                }
            }
        }

        private static void ApplyHeroDeathCrater(Vector2 center, float radius)
        {
            SimpleWorldGenerator gen;
#if UNITY_6000_0_OR_NEWER
            gen = Object.FindFirstObjectByType<SimpleWorldGenerator>();
#else
            gen = Object.FindObjectOfType<SimpleWorldGenerator>();
#endif
            if (gen != null)
            {
                gen.CarveCraterWorld(center, Mathf.Max(0.01f, radius));
            }
        }
    }
}
