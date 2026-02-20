using UnityEngine;
using UnityEngine.InputSystem;

namespace WormCrawlerPrototype
{
    public sealed class HeroGrenadeThrower : MonoBehaviour
    {
        [Header("State")]
        public bool Enabled = false;

        private bool _inputEnabled = true;
        public bool InputEnabled
        {
            get => _inputEnabled;
            set
            {
                if (_inputEnabled == value) return;
                _inputEnabled = value;
                ApplyInputEnabledState();
            }
        }

        public bool TryThrowNow()
        {
            if (!Enabled)
            {
                return false;
            }
            Throw();
            return true;
        }

        [Header("Icon")]
        [SerializeField] private string grenadeIconResourcesPath = "Icons/Grenade";
        [SerializeField] private Sprite grenadeIconSprite;
        [SerializeField] private float iconHeightFractionOfHeroHeight = 0.076f;
        [SerializeField] private Vector2 iconOffsetFractionOfHeroSize = new Vector2(0.28f, 0.12f);
        [SerializeField] private Vector2 iconOffsetAimUpExtraFractionOfHeroHeight = new Vector2(0.04f, 0.12f);

        [Header("Throw")]
        [SerializeField] private float maxRangeFractionOfRope = 0.75f;
        [SerializeField] private float minUpAimY = 0.00f;
        [SerializeField] private float flightSlowdown = 1.15f;
        [SerializeField] private float maxHeightFractionOfHeroHeight = 2.0f;
        [SerializeField] private float minRangeFractionWhenHigh = 0.20f;
        [SerializeField] private float spawnForwardFractionOfHeroWidth = 0.33f;
        [SerializeField] private float spawnUpFractionOfHeroHeight = 0.10f;
        [SerializeField] private float grenadeLifetime = 2f;

        [Header("Trajectory")]
        [SerializeField] private bool showTrajectory = true;
        [SerializeField] private int trajectorySteps = 28;
        [SerializeField] private float trajectoryTimeStep = 0.06f;
        [SerializeField] private float trajectoryLineWidth = 0.05f;
        [SerializeField] private Color trajectoryColor = new Color(0.9f, 0.95f, 1f, 0.65f);

        [Header("Projectile Sprite")]
        [SerializeField] private string grenadeSpriteResourcesPath = "Projectiles/grenade";
        [SerializeField] private Sprite grenadeSprite;
        [SerializeField] private float grenadeSpinDegPerSecond = 720f;

        [Header("Ammo")]
        [SerializeField] private int maxGrenades = 4;
        [SerializeField] private int grenadesLeft = 4;

        public void ResetAmmo()
        {
            maxGrenades = Mathf.Max(0, maxGrenades);
            grenadesLeft = Mathf.Clamp(maxGrenades, 0, maxGrenades);
        }

        private Rigidbody2D _rb;
        private Collider2D _heroCol;
        private WormAimController _aim;
        private Animator _animator;
        private GrappleController _grapple;
        private TurnManager _turn;

        private InputAction _throw;

        private Transform _iconT;
        private SpriteRenderer _iconSr;

        private LineRenderer _traj;
        private int _terrainObstacleLayer;
        private LayerMask _groundMask = ~0;

        private static readonly int ShootHash = Animator.StringToHash("Shoot");

        private void ApplyInputEnabledState()
        {
            if (_inputEnabled)
            {
                _throw?.Enable();
            }
            else
            {
                _throw?.Disable();
            }
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _heroCol = GetComponent<Collider2D>();
            _aim = GetComponent<WormAimController>();
            _animator = GetComponent<Animator>();
            _grapple = GetComponent<GrappleController>();

#if UNITY_6000_0_OR_NEWER
            _turn = Object.FindFirstObjectByType<TurnManager>();
#else
            _turn = Object.FindObjectOfType<TurnManager>();
#endif

            _terrainObstacleLayer = LayerMask.NameToLayer("TerrainObstacle");
            if (_terrainObstacleLayer >= 0)
            {
                _groundMask = ~(1 << _terrainObstacleLayer);
            }

            _throw = new InputAction("ThrowGrenade", InputActionType.Button);
            _throw.AddBinding("<Keyboard>/space");
            _throw.AddBinding("<Gamepad>/rightTrigger");
            _throw.Enable();

            ApplyInputEnabledState();

            EnsureIcon();
            EnsureTrajectoryRenderer();

            maxGrenades = Mathf.Max(0, maxGrenades);
            grenadesLeft = Mathf.Clamp(grenadesLeft, 0, maxGrenades);

            if (!Enabled)
            {
                if (_iconT != null) _iconT.gameObject.SetActive(false);
                if (_traj != null) _traj.enabled = false;
            }
        }

        private void OnDisable()
        {
            if (_iconT != null) _iconT.gameObject.SetActive(false);
            if (_traj != null) _traj.enabled = false;
        }

        private void OnDestroy()
        {
            _throw?.Disable();
            _throw?.Dispose();
        }

        private void LateUpdate()
        {
            if (Bootstrap.IsMapMenuOpen)
            {
                if (_iconT != null) _iconT.gameObject.SetActive(false);
                if (_traj != null) _traj.enabled = false;
                return;
            }

            if (!Enabled)
            {
                if (_iconT != null) _iconT.gameObject.SetActive(false);
                if (_traj != null) _traj.enabled = false;
                return;
            }

            EnsureIcon();
            UpdateIcon();

            if (showTrajectory)
            {
                EnsureTrajectoryRenderer();
                UpdateTrajectory();
            }
            else if (_traj != null)
            {
                _traj.enabled = false;
            }

            if (_throw != null && _throw.WasPressedThisFrame())
            {
                if (InputEnabled)
                {
                    Throw();
                }
            }
        }

        private void EnsureIcon()
        {
            if (_iconT != null)
            {
                return;
            }

            var parent = transform;
            var visual = transform.Find("Visual");
            if (visual != null)
            {
                var anim = visual.Find("Anim");
                parent = anim != null ? anim : visual;
            }

            var go = new GameObject("GrenadeIcon");
            go.transform.SetParent(parent, false);
            _iconT = go.transform;

            _iconSr = go.AddComponent<SpriteRenderer>();
            _iconSr.sortingOrder = 30;
            _iconSr.sprite = ResolveIconSprite();
        }

        private SpriteRenderer ResolveHeroVisualSpriteRenderer()
        {
            var visual = transform.Find("Visual");
            if (visual != null)
            {
                var anim = visual.Find("Anim");
                if (anim != null)
                {
                    var sr = anim.GetComponent<SpriteRenderer>();
                    if (sr != null)
                    {
                        return sr;
                    }
                }

                {
                    var sr = visual.GetComponent<SpriteRenderer>();
                    if (sr != null)
                    {
                        return sr;
                    }
                }
            }

            return GetComponentInChildren<SpriteRenderer>();
        }

        private Sprite ResolveIconSprite()
        {
            if (grenadeIconSprite != null)
            {
                return grenadeIconSprite;
            }

            if (!string.IsNullOrEmpty(grenadeIconResourcesPath))
            {
                var s = Resources.Load<Sprite>(grenadeIconResourcesPath);
                if (s != null)
                {
                    return s;
                }

                var t = Resources.Load<Texture2D>(grenadeIconResourcesPath);
                if (t != null)
                {
                    return Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f);
                }
            }

            var tex = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            var px = new Color[16 * 16];
            for (var i = 0; i < px.Length; i++) px[i] = new Color(0.15f, 0.7f, 1f, 1f);
            tex.SetPixels(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 16f);
        }

        private void UpdateIcon()
        {
            if (_iconT == null)
            {
                return;
            }

            _iconT.gameObject.SetActive(true);

            var heroH = 1f;
            var heroW = 1f;
            if (_heroCol != null)
            {
                var b = _heroCol.bounds;
                heroH = Mathf.Max(0.25f, b.size.y);
                heroW = Mathf.Max(0.25f, b.size.x);
            }

            var aimY = 0f;
            var aimDir = Vector2.right;
            if (_aim != null)
            {
                aimDir = _aim.AimDirection;
                aimY = Mathf.Clamp01(Mathf.Max(0f, aimDir.y));
            }

            var facingSign = aimDir.x >= 0f ? 1f : -1f;

            var baseOffset = new Vector2(iconOffsetFractionOfHeroSize.x * heroW * facingSign, iconOffsetFractionOfHeroSize.y * heroH);
            var aimExtra = new Vector2(iconOffsetAimUpExtraFractionOfHeroHeight.x * heroH * facingSign, iconOffsetAimUpExtraFractionOfHeroHeight.y * heroH) * aimY;
            var world = (Vector2)transform.position + baseOffset + aimExtra;
            _iconT.position = new Vector3(world.x, world.y, 0f);

            if (_iconSr != null && _iconSr.sprite != null)
            {
                var desiredH = Mathf.Max(0.01f, heroH * Mathf.Max(0.01f, iconHeightFractionOfHeroHeight));
                var spriteH = _iconSr.sprite.bounds.size.y;
                var s = spriteH > 0.0001f ? desiredH / spriteH : 1f;
                var parentScale = _iconT.parent != null ? _iconT.parent.lossyScale : Vector3.one;
                var safeParentY = Mathf.Abs(parentScale.y) > 0.0001f ? parentScale.y : 1f;
                var safeParentX = Mathf.Abs(parentScale.x) > 0.0001f ? parentScale.x : 1f;
                _iconT.localScale = new Vector3(s / safeParentX, s / safeParentY, 1f);
                _iconSr.flipX = facingSign < 0f;
            }
        }

        private void EnsureTrajectoryRenderer()
        {
            if (_traj != null)
            {
                return;
            }

            var go = new GameObject("GrenadeTrajectory");
            go.transform.SetParent(transform, false);
            _traj = go.AddComponent<LineRenderer>();
            _traj.useWorldSpace = true;
            _traj.alignment = LineAlignment.View;
            _traj.textureMode = LineTextureMode.Stretch;
            _traj.numCapVertices = 4;
            _traj.numCornerVertices = 4;
            _traj.material = new Material(Shader.Find("Sprites/Default"));
            _traj.startColor = trajectoryColor;
            _traj.endColor = trajectoryColor;
            _traj.startWidth = trajectoryLineWidth;
            _traj.endWidth = trajectoryLineWidth;
            _traj.enabled = false;
        }

        private bool TryGroundRaycast(Vector2 from, Vector2 to, out Vector2 hitPoint)
        {
            hitPoint = default;

            var d = to - from;
            var dist = d.magnitude;
            if (dist < 0.0001f)
            {
                return false;
            }

            var dir = d / dist;
            var hits = Physics2D.RaycastAll(from, dir, dist, _groundMask);
            if (hits == null || hits.Length == 0)
            {
                return false;
            }

            for (var i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.collider == null || h.collider.isTrigger)
                {
                    continue;
                }

                var ht = h.collider.transform;
                if (ht == transform || ht.IsChildOf(transform))
                {
                    continue;
                }

                hitPoint = h.point;
                return true;
            }

            return false;
        }

        private Vector2 GetThrowOriginWorld(out float heroH, out float heroW)
        {
            heroH = 1f;
            heroW = 1f;
            if (_heroCol != null)
            {
                var b = _heroCol.bounds;
                heroH = Mathf.Max(0.25f, b.size.y);
                heroW = Mathf.Max(0.25f, b.size.x);
            }

            var aimDir = Vector2.right;
            if (_aim != null)
            {
                aimDir = _aim.AimDirection;
            }

            var facingSign = aimDir.x >= 0f ? 1f : -1f;
            var forward = new Vector2(facingSign, 0f);
            var p = (Vector2)transform.position;
            p += forward * (heroW * Mathf.Max(0f, spawnForwardFractionOfHeroWidth));
            p += Vector2.up * (heroH * Mathf.Max(0f, spawnUpFractionOfHeroHeight));
            return NudgeThrowOriginOutOfGround(p, heroH);
        }

        private Vector2 NudgeThrowOriginOutOfGround(Vector2 origin, float heroH)
        {
            var r = Mathf.Max(0.02f, heroH * 0.03f);
            var p = origin;

            for (var i = 0; i < 8; i++)
            {
                var hits = Physics2D.OverlapCircleAll(p, r, _groundMask);
                var blocked = false;
                if (hits != null)
                {
                    for (var hi = 0; hi < hits.Length; hi++)
                    {
                        var c = hits[hi];
                        if (c == null || c.isTrigger)
                        {
                            continue;
                        }

                        var ct = c.transform;
                        if (ct == transform || ct.IsChildOf(transform))
                        {
                            continue;
                        }

                        blocked = true;
                        break;
                    }
                }

                if (!blocked)
                {
                    return p;
                }

                p += Vector2.up * (r * 1.6f);
            }

            return p;
        }

        private Vector2 ComputeLaunchVelocity(Vector2 origin, float heroH)
        {
            var dir = Vector2.right;
            if (_aim != null)
            {
                dir = _aim.AimDirection;
            }

            return ComputeLaunchVelocityFromAim(origin, heroH, dir);
        }

        public Vector2 ComputeLaunchVelocityFromAim(Vector2 origin, float heroH, Vector2 aimDir)
        {
            var dir = aimDir;

            if (dir.y < 0f)
            {
                dir.y = 0f;
            }
            if (dir.sqrMagnitude < 0.0001f)
            {
                dir = Vector2.right;
            }
            dir.Normalize();

            var aimY = Mathf.Clamp01(Mathf.Max(0f, dir.y));
            if (aimY < minUpAimY)
            {
                dir = new Vector2(Mathf.Sign(dir.x) * Mathf.Sqrt(Mathf.Max(0.0f, 1f - minUpAimY * minUpAimY)), minUpAimY);
                aimY = Mathf.Clamp01(Mathf.Max(0f, dir.y));
            }

            var maxRange = GetMaxRange();
            var baseG = Mathf.Abs(Physics2D.gravity.y) * (_rb != null ? Mathf.Max(0.01f, _rb.gravityScale) : 1f);
            var slow = Mathf.Max(1f, flightSlowdown);
            var g = baseG / (slow * slow);
            g = Mathf.Max(0.01f, g);

            var maxH = Mathf.Max(0.2f, heroH * Mathf.Max(0.05f, maxHeightFractionOfHeroHeight));
            var height = Mathf.Lerp(heroH * 0.35f, maxH, aimY);
            height = Mathf.Max(0.05f, height);

            var rangeFactor = 1f - Mathf.Clamp01(aimY);
            var minRangeF = Mathf.Clamp01(minRangeFractionWhenHigh);
            var desiredRange = maxRange * Mathf.Lerp(minRangeF, 1f, rangeFactor);

            var vy = Mathf.Sqrt(2f * g * height);
            var time = 2f * vy / g;
            var vx = desiredRange / Mathf.Max(0.01f, time);

            var signX = dir.x >= 0f ? 1f : -1f;
            return new Vector2(vx * signX, vy);
        }

        public Vector2 GetThrowOriginWorldPublic(out float heroH, out float heroW)
        {
            return GetThrowOriginWorld(out heroH, out heroW);
        }

        public bool PredictLandingPoint(Vector2 aimDir, out Vector2 landingPoint)
        {
            landingPoint = transform.position;

            var origin = GetThrowOriginWorld(out var heroH, out _);
            var v0 = ComputeLaunchVelocityFromAim(origin, heroH, aimDir);

            var heroGScale = _rb != null ? _rb.gravityScale : 1f;
            var slow = Mathf.Max(1f, flightSlowdown);
            var g = Physics2D.gravity * (heroGScale / (slow * slow));
            var steps = Mathf.Clamp(trajectorySteps, 4, 128);
            var dt = Mathf.Max(0.01f, trajectoryTimeStep);

            var p = origin;
            var v = v0;
            for (var i = 1; i < steps; i++)
            {
                var next = p + v * dt;
                v += g * dt;
                if (TryGroundRaycast(p, next, out var hitPoint))
                {
                    landingPoint = hitPoint;
                    return true;
                }
                p = next;
            }

            landingPoint = p;
            return false;
        }

        private float GetMaxRange()
        {
            if (_grapple != null)
            {
                var rope = _grapple.MaxRopeLength;
                if (rope > 0.5f)
                {
                    return Mathf.Max(1f, rope * Mathf.Clamp01(maxRangeFractionOfRope));
                }
            }

            return 8f;
        }

        public float GetMaxRangePublic()
        {
            return GetMaxRange();
        }

        private void UpdateTrajectory()
        {
            if (_traj == null)
            {
                return;
            }

            var origin = GetThrowOriginWorld(out var heroH, out _);
            var v0 = ComputeLaunchVelocity(origin, heroH);

            var heroGScale = _rb != null ? _rb.gravityScale : 1f;
            var slow = Mathf.Max(1f, flightSlowdown);
            var g = Physics2D.gravity * (heroGScale / (slow * slow));
            var steps = Mathf.Clamp(trajectorySteps, 4, 128);
            var dt = Mathf.Max(0.01f, trajectoryTimeStep);

            _traj.enabled = true;
            _traj.startColor = trajectoryColor;
            _traj.endColor = trajectoryColor;
            _traj.startWidth = trajectoryLineWidth;
            _traj.endWidth = trajectoryLineWidth;

            if (_traj.positionCount != steps)
            {
                _traj.positionCount = steps;
            }

            var p = origin;
            var v = v0;
            _traj.SetPosition(0, new Vector3(p.x, p.y, 0f));

            for (var i = 1; i < steps; i++)
            {
                var next = p + v * dt;
                v += g * dt;

                if (TryGroundRaycast(p, next, out var hitPoint))
                {
                    next = hitPoint;
                    _traj.SetPosition(i, new Vector3(next.x, next.y, 0f));
                    for (var j = i + 1; j < steps; j++)
                    {
                        _traj.SetPosition(j, new Vector3(next.x, next.y, 0f));
                    }
                    break;
                }

                p = next;
                _traj.SetPosition(i, new Vector3(p.x, p.y, 0f));
            }
        }

        private Sprite ResolveGrenadeSprite()
        {
            if (grenadeSprite != null)
            {
                return grenadeSprite;
            }

            if (!string.IsNullOrEmpty(grenadeSpriteResourcesPath))
            {
                var s = Resources.Load<Sprite>(grenadeSpriteResourcesPath);
                if (s != null)
                {
                    return s;
                }

                var t = Resources.Load<Texture2D>(grenadeSpriteResourcesPath);
                if (t != null)
                {
                    return Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f);
                }
            }

            {
                var icon = ResolveIconSprite();
                if (icon != null)
                {
                    return icon;
                }
            }

            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, mipChain: false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.SetPixel(0, 0, new Color(0.25f, 0.95f, 0.85f, 1f));
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }

        private void Throw()
        {
            if (grenadesLeft <= 0)
            {
                return;
            }

            if (_turn == null)
            {
#if UNITY_6000_0_OR_NEWER
                _turn = Object.FindFirstObjectByType<TurnManager>();
#else
                _turn = Object.FindObjectOfType<TurnManager>();
#endif
            }

            if (_turn != null)
            {
                if (!_turn.TryConsumeShot(TurnManager.TurnWeapon.Grenade))
                {
                    return;
                }
            }

            var origin = GetThrowOriginWorld(out var heroH, out var heroW);
            var v0 = ComputeLaunchVelocity(origin, heroH);

            if (_animator != null)
            {
                _animator.SetTrigger(ShootHash);
            }

            var go = new GameObject("Grenade");
            go.transform.position = new Vector3(origin.x, origin.y, 0f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = ResolveGrenadeSprite();
            var heroSr = ResolveHeroVisualSpriteRenderer();
            if (heroSr != null)
            {
                sr.sortingLayerID = heroSr.sortingLayerID;
                sr.sortingOrder = heroSr.sortingOrder + 20;
            }
            else
            {
                sr.sortingOrder = 120;
            }
            sr.enabled = true;
            sr.color = Color.white;

            var desiredH = Mathf.Max(0.10f, heroH * 0.12f) * 5f;
            var spriteH = sr.sprite != null ? sr.sprite.bounds.size.y : 0.1f;
            var scale = spriteH > 0.0001f ? desiredH / spriteH : 1f;
            go.transform.localScale = new Vector3(scale, scale, 1f);

            var rb2d = go.AddComponent<Rigidbody2D>();
            var heroGScale = _rb != null ? _rb.gravityScale : 3f;
            var slow = Mathf.Max(1f, flightSlowdown);
            rb2d.gravityScale = heroGScale / (slow * slow);
            rb2d.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

#if UNITY_6000_0_OR_NEWER
            rb2d.linearVelocity = v0;
#else
            rb2d.velocity = v0;
#endif

            var col = go.AddComponent<CircleCollider2D>();
            col.isTrigger = false;
            col.radius = Mathf.Max(0.02f, desiredH * 0.12f);

            if (_heroCol != null)
            {
                var heroCols = GetComponents<Collider2D>();
                if (heroCols != null)
                {
                    for (var i = 0; i < heroCols.Length; i++)
                    {
                        if (heroCols[i] != null)
                        {
                            Physics2D.IgnoreCollision(col, heroCols[i], true);
                        }
                    }
                }
            }

            // If we spawned intersecting the ground (common on slopes/curves), lift the grenade a bit so it can fly.
            {
                var hits = Physics2D.OverlapCircleAll((Vector2)go.transform.position, col.radius, _groundMask);
                var blocked = false;
                if (hits != null)
                {
                    for (var i = 0; i < hits.Length; i++)
                    {
                        var c = hits[i];
                        if (c == null || c.isTrigger) continue;
                        var ct = c.transform;
                        if (ct == transform || ct.IsChildOf(transform)) continue;
                        blocked = true;
                        break;
                    }
                }

                if (blocked)
                {
                    var p = (Vector2)go.transform.position;
                    p += Vector2.up * (col.radius * 2.5f);
                    go.transform.position = new Vector3(p.x, p.y, 0f);
                }
            }

            var proj = go.AddComponent<GrenadeProjectile>();
            proj.Lifetime = grenadeLifetime;
            proj.GroundMask = _groundMask;
            proj.SpinDegPerSecond = Mathf.Max(0f, grenadeSpinDegPerSecond);
            proj.SetNoFrictionMaterial();

            grenadesLeft = Mathf.Max(0, grenadesLeft - 1);
        }
    }
}
