using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WormCrawlerPrototype
{
    public sealed class HeroClawGun : MonoBehaviour
    {
        [Header("State")]
        public bool Enabled = false;

        [SerializeField] private bool debugLogs = false;

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

        public bool TryFireOnceNow()
        {
            if (!Enabled)
            {
                return false;
            }
            if (_animActive)
            {
                return false;
            }

            if (shotsLeft <= 0)
            {
                if (debugLogs) Debug.Log($"[ClawGun] No ammo: shotsLeft={shotsLeft} ({name})");
                return false;
            }

            if (_turn == null)
            {
#if UNITY_6000_0_OR_NEWER
                _turn = Object.FindFirstObjectByType<TurnManager>();
#else
                _turn = Object.FindObjectOfType<TurnManager>();
#endif
            }

            var needConsumeShot = !_firedSincePress;
            if (_turn != null && needConsumeShot)
            {
                if (!_turn.TryConsumeShot(TurnManager.TurnWeapon.ClawGun))
                {
                    if (debugLogs) Debug.Log($"[ClawGun] Blocked by TurnManager.TryConsumeShot (ropeOnly/shotUsed/locked): ({name})");
                    return false;
                }
            }

            var ammoBefore = shotsLeft;
            var didStart = BeginSingleShot(skipTurnChecks: true);
            if (didStart)
            {
                _firedSincePress = true;
            }
            if (debugLogs) Debug.Log($"[ClawGun] TryFireOnceNow result={didStart} shotsLeft(before)={ammoBefore} shotsLeft(after)={shotsLeft} ({name})");
            return didStart;
        }

        public void SetExternalHeld(bool held)
        {
            if (!Enabled)
            {
                _held = false;
                _externalHoldActive = false;
                return;
            }

            // External (touch/UI/AI) hold should not directly trigger "release" logic in TurnManager,
            // because multiple input sources may be active (mouse/keyboard + UI).
            // We only notify release when *all* hold sources are released (handled in UpdateAutoFire).
            _externalHoldActive = held;
        }

        [Header("Sprite Sheet (Resources)")]
        [SerializeField] private string spritesheetResourcesPath = "Weapons/claw_gun";
        [SerializeField] private int frames = 9;
        [SerializeField] private int sheetColumns = 3;
        [SerializeField] private int sheetRows = 3;
        [SerializeField] private float pixelsPerUnit = 80f;

        [Header("Firing")]
        [SerializeField] private float shotsPerSecond = 2f;
        [SerializeField] private float clawGunEscapeCountdownDelaySeconds = 5f;
        [SerializeField] private float fireAnimationFps = 24f;
        [SerializeField] private int impactOnFrameIndex = 5;
        [SerializeField] private float bulletRange = 25f;
        [SerializeField] private float bulletExplosionRadiusHeroHeights = 0.35f;
        [SerializeField] private float bulletCraterRadiusHeroHeights = 0.575f;

        [SerializeField] private bool scaleRangeFromGrenade = true;
        [SerializeField] private float bulletRangeAsGrenadeRangeMultiplier = 1.5f;

        [Header("Aim Line")]
        [SerializeField] private bool showAimLine = true;
        [SerializeField] private float aimLineWidth = 0.05f;
        [SerializeField] private Color aimLineColor = new Color(0.95f, 0.95f, 0.95f, 0.55f);

        [SerializeField] private float fireDirectionDownOffsetDeg = 10f;

        [Header("Ammo")]
        [SerializeField] private int maxShots = 40;
        [SerializeField] private int shotsLeft = 40;

        public int ShotsLeft => shotsLeft;

        public void ResetAmmo()
        {
            maxShots = Mathf.Max(0, maxShots);
            shotsLeft = Mathf.Clamp(maxShots, 0, maxShots);
        }

        [Header("Weapon Placement")]
        [SerializeField] private float weaponHeightFractionOfHeroHeight = 0.20976f;
        [SerializeField] private Vector2 weaponOffsetFractionOfHeroSize = new Vector2(-0.8f, 0.27f);
        [SerializeField] private Vector2 weaponSpriteLocalOffsetFractionOfHeroSize = new Vector2(-0.12f, 0f);
        [SerializeField] private Vector2 weaponSpritePivotPixels = new Vector2(650f, 80f);
        [SerializeField] private float weaponAimAngleOffsetDeg = 0f;
        [SerializeField] private Vector2 weaponOffsetAltFractionOfHeroSize = new Vector2(-0.8f, 0.27f);
        [SerializeField] private Vector2 weaponSpriteLocalOffsetAltFractionOfHeroSize = new Vector2(-0.12f, 0f);
        [SerializeField] private float aimXDeadzone = 0.15f;
        [SerializeField] private bool baseMirrorSpriteX = true;
        [SerializeField] private bool baseMirrorSpriteY = true;
        [SerializeField] private bool flipYWhenFacingLeft = true;
        [SerializeField] private int sortingOrderOffsetFromHero = 25;

        [Header("Damage")]
        [SerializeField] private int bulletDamage = 10;
        [SerializeField] private float bulletKnockbackImpulse = 6f;
        [SerializeField] private LayerMask hitMask = ~0;

        private Rigidbody2D _rb;
        private Collider2D _heroCol;
        private WormAimController _aim;
        private Animator _animator;

        private HeroGrenadeThrower _grenade;

        private TurnManager _turn;

        private InputAction _shoot;

        private Transform _weaponPivotT;
        private Transform _weaponSpriteT;
        private SpriteRenderer _weaponSr;

        private LineRenderer _aimLine;

        private Texture2D _sheetTex;
        private Sprite[] _frameSprites;
        private Vector2 _lastPivotPixels;

        private bool _held;
        private float _nextShotTime;

        private bool _firedSincePress;
        private bool _disableAfterShot;

        private bool _keyboardHoldActive;
        private bool _mouseHoldActive;
        private bool _externalHoldActive;
        private bool _prevAnyHold;
        private float _holdStartedAt = -1f;
        private Coroutine _pendingReleaseNotifyRoutine;

        private bool _animActive;
        private float _animT;
        private int _animFrame;
        private bool _impactApplied;

        private Vector2 _pendingImpactPoint;
        private bool _pendingHasImpact;
        private Collider2D _pendingImpactCollider;
        private float _pendingHeroHeight;

        private bool _wasEnabled;
        private int _lastAimXSign = 1;

        private bool _aimStepOverridden;
        private float _aimStepPrev;

        private static readonly int ShootHash = Animator.StringToHash("Shoot");

        private void Update()
        {
            UpdateAimStepOverride();
        }

        private void UpdateAimStepOverride()
        {
            if (_aim == null)
            {
                return;
            }

            if (!Enabled)
            {
                RestoreAimStepIfNeeded();
                return;
            }

            var desired = _held ? 10f : 5f;
            desired = Mathf.Max(0f, desired);

            if (!_aimStepOverridden)
            {
                _aimStepPrev = _aim.AimStepDeg;
                _aimStepOverridden = true;
            }

            _aim.AimStepDeg = desired;
        }

        private void RestoreAimStepIfNeeded()
        {
            if (!_aimStepOverridden || _aim == null)
            {
                _aimStepOverridden = false;
                return;
            }

            _aim.AimStepDeg = _aimStepPrev;
            _aimStepOverridden = false;
        }

        private void ApplyInputEnabledState()
        {
            if (_inputEnabled)
            {
                _shoot?.Enable();
            }
            else
            {
                _shoot?.Disable();
            }
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _heroCol = GetComponent<Collider2D>();
            _aim = GetComponent<WormAimController>();
            _animator = GetComponent<Animator>();
            _grenade = GetComponent<HeroGrenadeThrower>();

#if UNITY_6000_0_OR_NEWER
            _turn = Object.FindFirstObjectByType<TurnManager>();
#else
            _turn = Object.FindObjectOfType<TurnManager>();
#endif

            _shoot = new InputAction("ShootClawGun", InputActionType.Button);
            _shoot.AddBinding("<Keyboard>/space");
            _shoot.AddBinding("<Gamepad>/rightTrigger");
            _shoot.started += OnShootStarted;
            _shoot.canceled += OnShootCanceled;
            _shoot.Enable();
            ApplyInputEnabledState();

            maxShots = Mathf.Max(0, maxShots);
            shotsLeft = Mathf.Clamp(shotsLeft, 0, maxShots);

            EnsureWeaponRenderer();
            EnsureSpritesheet();
        }

        private void OnDestroy()
        {
            if (_shoot != null)
            {
                _shoot.started -= OnShootStarted;
                _shoot.canceled -= OnShootCanceled;
            }
            _shoot?.Disable();
            _shoot?.Dispose();
        }

        private void OnDisable()
        {
            ForceStop();
        }

        public void ForceStop()
        {
            CancelPendingClawReleaseNotify();
            _held = false;
            _keyboardHoldActive = false;
            _mouseHoldActive = false;
            _externalHoldActive = false;
            _prevAnyHold = false;
            _holdStartedAt = -1f;
            _firedSincePress = false;
            _disableAfterShot = false;
            Enabled = false;
            _animActive = false;
            _impactApplied = false;
            _pendingHasImpact = false;
            if (_weaponPivotT != null) _weaponPivotT.gameObject.SetActive(false);
            RestoreAimStepIfNeeded();
        }

        private void OnShootStarted(InputAction.CallbackContext ctx)
        {
            if (!InputEnabled) return;
            if (!Enabled) return;
            CancelPendingClawReleaseNotify();
            _held = true;
            _holdStartedAt = Time.time;
            _firedSincePress = false;
            _nextShotTime = Mathf.Min(_nextShotTime, Time.time);
            if (debugLogs) Debug.Log($"[ClawGun] OnShootStarted: Enabled={Enabled} InputEnabled={InputEnabled} held={_held} ({name})");
        }

        private void OnShootCanceled(InputAction.CallbackContext ctx)
        {
            if (!InputEnabled) return;
            _held = false;

            if (Enabled)
            {
                // If a shot already started, let the animation reach the impact frame and apply damage/FX.
                // Otherwise, a quick tap would immediately disable the weapon and cancel the pending impact.
                if (_firedSincePress)
                {
                    _disableAfterShot = true;
                }
                else
                {
                    Enabled = false;
                }

                var carousel = GetComponent<HeroAmmoCarousel>();
                if (carousel != null)
                {
                    if (!_firedSincePress)
                    {
                        carousel.ForceSelectRope();
                    }
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
                    if (_firedSincePress)
                    {
                        NotifyClawGunReleasedWithDelay();
                    }
                }
            }
        }

        private void CancelPendingClawReleaseNotify()
        {
            if (_pendingReleaseNotifyRoutine != null)
            {
                StopCoroutine(_pendingReleaseNotifyRoutine);
                _pendingReleaseNotifyRoutine = null;
            }
        }

        private void NotifyClawGunReleasedWithDelay()
        {
            if (!_firedSincePress)
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

            if (_turn == null)
            {
                return;
            }

            var threshold = Mathf.Max(0f, clawGunEscapeCountdownDelaySeconds);
            var heldFor = _holdStartedAt >= 0f ? Mathf.Max(0f, Time.time - _holdStartedAt) : threshold;
            var delay = Mathf.Max(0f, threshold - heldFor);

            CancelPendingClawReleaseNotify();
            if (delay <= 0.0001f)
            {
                _turn.NotifyClawGunReleased();
                return;
            }

            _pendingReleaseNotifyRoutine = StartCoroutine(NotifyClawGunReleasedDelayed(delay));
        }

        private IEnumerator NotifyClawGunReleasedDelayed(float delay)
        {
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }

            _pendingReleaseNotifyRoutine = null;

            if (_turn == null)
            {
#if UNITY_6000_0_OR_NEWER
                _turn = Object.FindFirstObjectByType<TurnManager>();
#else
                _turn = Object.FindObjectOfType<TurnManager>();
#endif
            }

            if (_turn == null)
            {
                yield break;
            }

            var ap = _turn.ActivePlayer;
            if (ap == null)
            {
                yield break;
            }

            var sameHero = ap == transform || transform.IsChildOf(ap) || ap.IsChildOf(transform);
            if (!sameHero)
            {
                yield break;
            }

            _turn.NotifyClawGunReleased();
        }

        private void LateUpdate()
        {
            if (Bootstrap.IsMapMenuOpen)
            {
                if (_weaponPivotT != null) _weaponPivotT.gameObject.SetActive(false);
                if (_aimLine != null) _aimLine.enabled = false;
                _wasEnabled = Enabled;
                return;
            }

            if (Enabled && !_wasEnabled)
            {
                if (_animator != null)
                {
                    _animator.SetTrigger(ShootHash);
                }
            }
            _wasEnabled = Enabled;

            if (!Enabled)
            {
                if (_weaponPivotT != null) _weaponPivotT.gameObject.SetActive(false);
                if (_aimLine != null) _aimLine.enabled = false;
                _held = false;
                _keyboardHoldActive = false;
                _mouseHoldActive = false;
                _externalHoldActive = false;
                _prevAnyHold = false;
                return;
            }

            EnsureWeaponRenderer();
            EnsureSpritesheet();

            UpdateWeaponTransform();
            UpdateWeaponAnimation();

            EnsureAimLineRenderer();
            UpdateAimLine();

            UpdateAutoFire();
        }

        private float GetEffectiveBulletRange()
        {
            var r = Mathf.Max(0.1f, bulletRange);
            if (scaleRangeFromGrenade && _grenade != null)
            {
                var gr = Mathf.Max(0.1f, _grenade.GetMaxRangePublic());
                var mul = Mathf.Max(0f, bulletRangeAsGrenadeRangeMultiplier);
                r = Mathf.Max(0.1f, gr * mul);
            }
            return r;
        }

        private void EnsureAimLineRenderer()
        {
            if (_aimLine != null)
            {
                return;
            }

            var go = new GameObject("ClawGunAimLine");
            go.transform.SetParent(transform, false);
            _aimLine = go.AddComponent<LineRenderer>();
            _aimLine.useWorldSpace = true;
            _aimLine.positionCount = 2;
            _aimLine.numCapVertices = 4;
            _aimLine.numCornerVertices = 4;
            _aimLine.material = new Material(Shader.Find("Sprites/Default"));
            _aimLine.startColor = aimLineColor;
            _aimLine.endColor = aimLineColor;
            _aimLine.startWidth = aimLineWidth;
            _aimLine.endWidth = aimLineWidth;
            _aimLine.enabled = false;
        }

        private void UpdateAimLine()
        {
            if (_aimLine == null)
            {
                return;
            }

            if (!showAimLine)
            {
                _aimLine.enabled = false;
                return;
            }

            if (!Enabled)
            {
                _aimLine.enabled = false;
                return;
            }

            var origin = (Vector2)transform.position;
            if (_weaponPivotT != null)
            {
                origin = _weaponPivotT.position;
            }

            var dir = Vector2.right;
            if (_aim != null)
            {
                dir = _aim.AimDirection;
            }
            if (dir.sqrMagnitude < 0.0001f)
            {
                dir = Vector2.right;
            }
            dir = GetFireDirection(dir);

            var range = GetEffectiveBulletRange();
            var end = origin + dir * range;
            var hit = Physics2D.Raycast(origin, dir, range, hitMask);
            if (hit.collider != null && !hit.collider.isTrigger)
            {
                var ht = hit.collider.transform;
                if (ht != null && ht != transform && !ht.IsChildOf(transform))
                {
                    end = hit.point;
                }
            }

            _aimLine.enabled = true;
            _aimLine.startColor = aimLineColor;
            _aimLine.endColor = aimLineColor;
            _aimLine.startWidth = aimLineWidth;
            _aimLine.endWidth = aimLineWidth;

            _aimLine.SetPosition(0, new Vector3(origin.x, origin.y, 0f));
            _aimLine.SetPosition(1, new Vector3(end.x, end.y, 0f));
        }

        private Vector2 GetFireDirection(Vector2 aimDir)
        {
            var d = aimDir;
            if (d.sqrMagnitude < 0.0001f)
            {
                d = Vector2.right;
            }
            d.Normalize();

            var ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            // Apply a consistent "down" offset relative to the horizontal baseline.
            // For right-facing (0°) we subtract the offset; for left-facing (180°) we add it,
            // otherwise the offset would appear mirrored.
            var signedOffset = d.x >= 0f ? -fireDirectionDownOffsetDeg : fireDirectionDownOffsetDeg;
            ang += signedOffset;
            var rad = ang * Mathf.Deg2Rad;
            var outDir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            if (outDir.sqrMagnitude < 0.0001f)
            {
                outDir = Vector2.right;
            }
            return outDir.normalized;
        }

        private void UpdateAutoFire()
        {
            // Human continuous fire (keyboard/mouse) requires InputEnabled.
            // External hold (AI bots, touch/UI) works regardless of InputEnabled.
            if (Enabled && InputEnabled)
            {
                if (Keyboard.current != null && Keyboard.current.spaceKey != null)
                {
                    _keyboardHoldActive = Keyboard.current.spaceKey.isPressed;
                }
                else
                {
                    _keyboardHoldActive = false;
                }

                if (Mouse.current != null && Mouse.current.leftButton != null)
                {
                    _mouseHoldActive = Mouse.current.leftButton.isPressed;
                }
                else
                {
                    _mouseHoldActive = false;
                }
            }
            else
            {
                _keyboardHoldActive = false;
                _mouseHoldActive = false;
            }

            if (Enabled)
            {
                var anyHold = _externalHoldActive || _keyboardHoldActive || _mouseHoldActive;

                // Rising edge: treat as a new press so firing starts immediately.
                if (anyHold && !_prevAnyHold)
                {
                    CancelPendingClawReleaseNotify();
                    _firedSincePress = false;
                    _holdStartedAt = Time.time;
                    _nextShotTime = Mathf.Min(_nextShotTime, Time.time);
                }

                // Falling edge: only when ALL sources released.
                if (!anyHold && _prevAnyHold)
                {
                    if (_turn == null)
                    {
#if UNITY_6000_0_OR_NEWER
                        _turn = Object.FindFirstObjectByType<TurnManager>();
#else
                        _turn = Object.FindObjectOfType<TurnManager>();
#endif
                    }

                    if (_turn != null && _firedSincePress)
                    {
                        NotifyClawGunReleasedWithDelay();
                    }
                }

                _prevAnyHold = anyHold;
                _held = anyHold;
            }
            else
            {
                _prevAnyHold = false;
            }

            if (!_held)
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

            if (_turn != null && !_firedSincePress && !_turn.CanSelectWeapon(TurnManager.TurnWeapon.ClawGun))
            {
                if (debugLogs) Debug.Log($"[ClawGun] AutoFire stopped: CanSelectWeapon=false ({name})");
                _held = false;
                return;
            }

            if (shotsLeft <= 0)
            {
                if (debugLogs) Debug.Log($"[ClawGun] AutoFire stopped: no ammo shotsLeft={shotsLeft} ({name})");
                _held = false;
                return;
            }

            if (_animActive)
            {
                return;
            }

            var interval = 1f / Mathf.Max(0.01f, shotsPerSecond);
            if (Time.time < _nextShotTime)
            {
                return;
            }

            _nextShotTime = Time.time + interval;

            // Fire through the same path as AI/manual single-shot so TurnManager shot consumption applies.
            var didFire = TryFireOnceNow();
            if (!didFire)
            {
                // Prevent "silent" infinite holding when fire is blocked.
                _held = false;
                _keyboardHoldActive = false;
                _mouseHoldActive = false;
            }
            else
            {
                // Keep holding to allow automatic repeat fire.
                _held = true;
            }
        }

        private bool BeginSingleShot(bool skipTurnChecks)
        {
            if (shotsLeft <= 0)
            {
                if (debugLogs) Debug.Log($"[ClawGun] BeginSingleShot aborted: no ammo ({name})");
                return false;
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
                if (!skipTurnChecks && !_turn.CanSelectWeapon(TurnManager.TurnWeapon.ClawGun))
                {
                    if (debugLogs) Debug.Log($"[ClawGun] BeginSingleShot aborted: CanSelectWeapon=false ({name})");
                    return false;
                }
                _turn.NotifyWeaponSelected(TurnManager.TurnWeapon.ClawGun);
            }

            CapturePendingImpact();
            // Apply immediately so the shot has visible effect even if the animation does not progress
            // to the impact frame (e.g., quick disable, low FPS, or other state changes).
            _impactApplied = true;
            ApplyPendingImpact();
            StartFireAnimation();

            shotsLeft = Mathf.Max(0, shotsLeft - 1);

            if (_animator != null)
            {
                _animator.SetTrigger(ShootHash);
            }

            return true;
        }

        private void StartFireAnimation()
        {
            _animActive = true;
            _animT = 0f;
            _animFrame = 0;
            _impactApplied = false;
        }

        private void EnsureWeaponRenderer()
        {
            if (_weaponPivotT != null)
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

            var existing = parent.Find("ClawGunPivot");
            if (existing != null)
            {
                _weaponPivotT = existing;
                _weaponSpriteT = existing.Find("ClawGunSprite");
                if (_weaponSpriteT == null)
                {
                    var spriteGo = new GameObject("ClawGunSprite");
                    spriteGo.transform.SetParent(_weaponPivotT, false);
                    _weaponSpriteT = spriteGo.transform;
                }

                _weaponSr = _weaponSpriteT.GetComponent<SpriteRenderer>();
                if (_weaponSr == null)
                {
                    _weaponSr = _weaponSpriteT.gameObject.AddComponent<SpriteRenderer>();
                }
            }
            else
            {
                var pivotGo = new GameObject("ClawGunPivot");
                pivotGo.transform.SetParent(parent, false);
                _weaponPivotT = pivotGo.transform;

                var spriteGo = new GameObject("ClawGunSprite");
                spriteGo.transform.SetParent(_weaponPivotT, false);
                _weaponSpriteT = spriteGo.transform;

                _weaponSr = spriteGo.AddComponent<SpriteRenderer>();
                _weaponSr.sprite = null;
            }

            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                var c = parent.GetChild(i);
                if (c != null && c != _weaponPivotT && c.name != null && c.name.StartsWith("ClawGun"))
                {
                    Destroy(c.gameObject);
                }
            }

            if (_weaponPivotT != null)
            {
                _weaponPivotT.gameObject.SetActive(true);
            }

            if (_weaponSr != null)
            {
                _weaponSr.enabled = true;
            }

            var heroSr = ResolveHeroVisualSpriteRenderer();
            if (heroSr != null)
            {
                _weaponSr.sortingLayerID = heroSr.sortingLayerID;
                _weaponSr.sortingOrder = heroSr.sortingOrder + sortingOrderOffsetFromHero;
            }
            else
            {
                _weaponSr.sortingOrder = 140;
            }
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

        private void EnsureSpritesheet()
        {
            if (_frameSprites != null && _frameSprites.Length == frames && _lastPivotPixels == weaponSpritePivotPixels)
            {
                return;
            }

            _sheetTex = null;
            _frameSprites = null;

            if (!string.IsNullOrEmpty(spritesheetResourcesPath))
            {
                _sheetTex = Resources.Load<Texture2D>(spritesheetResourcesPath);
                if (_sheetTex == null)
                {
                    var sheetSprite = Resources.Load<Sprite>(spritesheetResourcesPath);
                    if (sheetSprite != null)
                    {
                        _sheetTex = sheetSprite.texture;
                    }
                }
            }

            if (_sheetTex == null)
            {
                _sheetTex = new Texture2D(3, 3, TextureFormat.RGBA32, false);
                _sheetTex.filterMode = FilterMode.Point;
                for (var y = 0; y < 3; y++)
                {
                    for (var x = 0; x < 3; x++)
                    {
                        _sheetTex.SetPixel(x, y, new Color(0.7f, 0.95f, 1f, 1f));
                    }
                }
                _sheetTex.Apply(false, true);
            }

            var w = _sheetTex.width;
            var h = _sheetTex.height;
            var cols = Mathf.Max(1, sheetColumns);
            var rows = Mathf.Max(1, sheetRows);
            var fw = Mathf.Max(1, w / cols);
            var fh = Mathf.Max(1, h / rows);

            var pivotN = new Vector2(
                fw > 0 ? Mathf.Clamp01(weaponSpritePivotPixels.x / fw) : 0.5f,
                fh > 0 ? Mathf.Clamp01(weaponSpritePivotPixels.y / fh) : 0.5f);

            _lastPivotPixels = weaponSpritePivotPixels;

            _frameSprites = new Sprite[frames];
            for (var i = 0; i < frames; i++)
            {
                var col = i % cols;
                var row = i / cols;

                var px = Mathf.Clamp(col * fw, 0, Mathf.Max(0, w - fw));
                var py = Mathf.Clamp(h - (row + 1) * fh, 0, Mathf.Max(0, h - fh));
                var rect = new Rect(px, py, fw, fh);
                _frameSprites[i] = Sprite.Create(_sheetTex, rect, pivotN, Mathf.Max(1f, pixelsPerUnit));
            }

            if (_weaponSr != null && _weaponSr.sprite == null && _frameSprites.Length > 0)
            {
                _weaponSr.sprite = _frameSprites[0];
            }
        }

        private void UpdateWeaponTransform()
        {
            if (_weaponPivotT == null || _weaponSpriteT == null)
            {
                return;
            }

            _weaponPivotT.gameObject.SetActive(true);

            var heroH = 1f;
            var heroW = 1f;
            if (_heroCol != null)
            {
                var b = _heroCol.bounds;
                heroH = Mathf.Max(0.25f, b.size.y);
                heroW = Mathf.Max(0.25f, b.size.x);
            }

            var aimDir = Vector2.right;
            var aimY = 0f;
            if (_aim != null)
            {
                aimDir = _aim.AimDirection;
                aimY = Mathf.Clamp01(Mathf.Max(0f, aimDir.y));
            }

            if (Mathf.Abs(aimDir.x) > aimXDeadzone)
            {
                _lastAimXSign = aimDir.x >= 0f ? 1 : -1;
            }

            var facingSign = 1f;
            var heroSr = ResolveHeroVisualSpriteRenderer();
            if (heroSr != null)
            {
                facingSign = heroSr.flipX ? -1f : 1f;
            }
            else if (transform.localScale.x < 0f)
            {
                facingSign = -1f;
            }

            var useAltPaw = false;
            if (Mathf.Abs(aimDir.x) <= aimXDeadzone)
            {
                useAltPaw = false;
            }
            else
            {
                useAltPaw = (aimDir.x * facingSign) < 0f;
            }

            useAltPaw = !useAltPaw;

            var pivotOffset = useAltPaw ? weaponOffsetAltFractionOfHeroSize : weaponOffsetFractionOfHeroSize;
            var spriteLocalOffset = useAltPaw ? weaponSpriteLocalOffsetAltFractionOfHeroSize : weaponSpriteLocalOffsetFractionOfHeroSize;

            var baseOffset = new Vector2(pivotOffset.x * heroW * facingSign, pivotOffset.y * heroH);
            var world = (Vector2)transform.position + baseOffset;
            _weaponPivotT.position = new Vector3(world.x, world.y, 0f);

            var d2 = aimDir.sqrMagnitude > 0.0001f ? aimDir : Vector2.right;
            var ang2 = Mathf.Atan2(d2.y, d2.x) * Mathf.Rad2Deg + weaponAimAngleOffsetDeg;

            _weaponPivotT.rotation = Quaternion.Euler(0f, 0f, ang2);

            if (_weaponSr != null && _weaponSr.sprite != null)
            {
                var desiredH = Mathf.Max(0.01f, heroH * Mathf.Max(0.01f, weaponHeightFractionOfHeroHeight));
                var spriteH = _weaponSr.sprite.bounds.size.y;
                var s = spriteH > 0.0001f ? desiredH / spriteH : 1f;
                var mx = baseMirrorSpriteX ? -1f : 1f;
                var my = baseMirrorSpriteY ? -1f : 1f;

                var dynY = 1f;
                if (flipYWhenFacingLeft && facingSign < 0f)
                {
                    dynY = -1f;
                }

                var localScale = new Vector3(s * mx, s * my * dynY, 1f);
                _weaponSpriteT.localScale = localScale;

                var spriteLocalHero = new Vector2(spriteLocalOffset.x * heroW, spriteLocalOffset.y * heroH);
                _weaponSpriteT.localPosition = new Vector3(spriteLocalHero.x, spriteLocalHero.y, 0f);
            }
            else
            {
                var spriteLocal = new Vector2(spriteLocalOffset.x * heroW, spriteLocalOffset.y * heroH);
                _weaponSpriteT.localPosition = new Vector3(spriteLocal.x, spriteLocal.y, 0f);
            }

            if (_weaponSr != null)
            {
                _weaponSr.flipX = false;
            }
        }

        private void UpdateWeaponAnimation()
        {
            if (_weaponSr == null || _frameSprites == null || _frameSprites.Length == 0)
            {
                return;
            }

            if (!_animActive)
            {
                _weaponSr.sprite = _frameSprites[0];
                return;
            }

            var fps = Mathf.Max(1f, fireAnimationFps);
            _animT += Time.deltaTime;
            var idx = Mathf.FloorToInt(_animT * fps);
            idx = Mathf.Clamp(idx, 0, _frameSprites.Length);

            if (idx != _animFrame)
            {
                _animFrame = idx;
            }

            if (_animFrame >= 0 && _animFrame < _frameSprites.Length)
            {
                _weaponSr.sprite = _frameSprites[_animFrame];
            }

            var impactFrame = Mathf.Clamp(impactOnFrameIndex, 0, _frameSprites.Length - 1);
            if (!_impactApplied && _animFrame >= impactFrame)
            {
                _impactApplied = true;
                ApplyPendingImpact();
            }

            if (_animFrame >= _frameSprites.Length)
            {
                _animActive = false;
                _weaponSr.sprite = _frameSprites[0];

                if (_disableAfterShot)
                {
                    _disableAfterShot = false;
                    Enabled = false;

                    // Reset hold state so the next activation can start a fresh burst.
                    _held = false;
                    _keyboardHoldActive = false;
                    _mouseHoldActive = false;
                    _externalHoldActive = false;
                    _prevAnyHold = false;
                    _firedSincePress = false;

                    var carousel = GetComponent<HeroAmmoCarousel>();
                    if (carousel != null)
                    {
                        carousel.ForceSelectRope();
                    }
                }
            }
        }

        private void CapturePendingImpact()
        {
            _pendingHasImpact = false;
            _pendingImpactPoint = default;
            _pendingImpactCollider = null;

            var heroH = 1f;
            if (_heroCol != null)
            {
                heroH = Mathf.Max(0.25f, _heroCol.bounds.size.y);
            }
            _pendingHeroHeight = heroH;

            var origin = (Vector2)transform.position;
            if (_weaponPivotT != null)
            {
                origin = _weaponPivotT.position;
            }

            var dir = Vector2.right;
            if (_aim != null)
            {
                dir = _aim.AimDirection;
            }

            if (dir.sqrMagnitude < 0.0001f)
            {
                dir = Vector2.right;
            }
            dir = GetFireDirection(dir);

            var rangeFinal = GetEffectiveBulletRange();
            var hits = Physics2D.RaycastAll(origin, dir, rangeFinal, hitMask);
            RaycastHit2D chosen = default;
            var found = false;
            for (var i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.collider == null) continue;
                if (h.collider.isTrigger) continue;

                var ht = h.collider.transform;
                if (ht == transform || (ht != null && ht.IsChildOf(transform)))
                {
                    continue;
                }

                chosen = h;
                found = true;
                break;
            }

            if (!found)
            {
                if (debugLogs)
                {
                    var hm = (int)hitMask;
                    var selfName = name;
                    Debug.Log($"[ClawGun] No impact: all hits invalid/self/trigger origin={origin} dir={dir} range={rangeFinal:0.00} hitMask={hm} hits={hits.Length} ({selfName})");
                }
                return;
            }

            _pendingHasImpact = true;
            _pendingImpactPoint = chosen.point;
            _pendingImpactCollider = chosen.collider;

            if (debugLogs)
            {
                Debug.Log($"[ClawGun] Captured impact: hit={chosen.collider.name} point={chosen.point} ({name})");
            }
        }

        private void ApplyPendingImpact()
        {
            if (!_pendingHasImpact)
            {
                return;
            }

            var pos = _pendingImpactPoint;
            var heroH = Mathf.Max(0.25f, _pendingHeroHeight);
            var damageRadius = Mathf.Max(0.05f, heroH * Mathf.Max(0.01f, bulletExplosionRadiusHeroHeights));
            var craterRadius = Mathf.Max(0.05f, heroH * Mathf.Max(0.01f, bulletCraterRadiusHeroHeights));

            if (debugLogs)
            {
                Debug.Log($"[ClawGun] ApplyPendingImpact: pos={pos} dmgR={damageRadius:0.00} craterR={craterRadius:0.00} hit={(_pendingImpactCollider != null ? _pendingImpactCollider.name : "(null)")} ({name})");
            }

            SpawnSmallExplosionFx(pos, damageRadius);
            ApplyDirectHitDamage(pos, damageRadius);
            TryCarveSmallCrater(pos, craterRadius);

            _pendingHasImpact = false;
        }

        private static void SpawnSmallExplosionFx(Vector2 pos, float radius)
        {
            var fxGo = new GameObject("ClawGunImpactFX");
            fxGo.transform.position = new Vector3(pos.x, pos.y, 0f);

            var fx = fxGo.AddComponent<GrenadeExplosionFx>();
            fx.Configure(targetDiameterWorld: radius * 2f, sortingOrderOverride: 220);
        }

        private void ApplyDirectHitDamage(Vector2 impactPoint, float impactRadius)
        {
            if (_pendingImpactCollider == null || _pendingImpactCollider.isTrigger)
            {
                return;
            }

            var ht = _pendingImpactCollider.transform;
            if (ht == null || ht == transform || ht.IsChildOf(transform))
            {
                return;
            }

            var dmg = Mathf.Max(0, bulletDamage);
            var health = _pendingImpactCollider.GetComponentInParent<SimpleHealth>();
            if (health != null)
            {
                health.TakeDamage(dmg, DamageSource.ClawGun);
            }

            var rb = _pendingImpactCollider.GetComponentInParent<Rigidbody2D>();
            if (rb != null)
            {
                // Only knock back non-active heroes.
                var allowKnockback = true;
                TurnManager tm;
#if UNITY_6000_0_OR_NEWER
                tm = Object.FindFirstObjectByType<TurnManager>();
#else
                tm = Object.FindObjectOfType<TurnManager>();
#endif
                if (tm != null)
                {
                    var ap = tm.ActivePlayer;
                    var hitRoot = rb.transform;
                    if (ap != null && (hitRoot == ap || hitRoot.IsChildOf(ap)))
                    {
                        allowKnockback = false;
                    }
                }

                var away = ((Vector2)_pendingImpactCollider.bounds.center - impactPoint);
                if (away.sqrMagnitude < 0.0001f)
                {
                    away = Vector2.up;
                }
                away.Normalize();
                var imp = Mathf.Max(0f, bulletKnockbackImpulse) * Mathf.Max(0.01f, impactRadius);
                if (allowKnockback && imp > 0.0001f)
                {
                    var walker = rb.GetComponent<HeroSurfaceWalker>();
                    if (walker == null)
                    {
                        walker = rb.GetComponentInChildren<HeroSurfaceWalker>();
                    }
                    if (walker != null)
                    {
                        walker.NudgeIdleAnchor(worldDelta: away * Mathf.Max(0.01f, impactRadius), unlockSeconds: 0.20f);
                    }

                    rb.AddForce(away * imp, ForceMode2D.Impulse);
                }
            }
        }

        private static void TryCarveSmallCrater(Vector2 pos, float radius)
        {
            SimpleWorldGenerator gen;
#if UNITY_6000_0_OR_NEWER
            gen = Object.FindFirstObjectByType<SimpleWorldGenerator>();
#else
            gen = Object.FindObjectOfType<SimpleWorldGenerator>();
#endif
            if (gen == null)
            {
                return;
            }

            gen.CarveCraterWorld(pos, Mathf.Max(0.01f, radius));
        }
    }
}
