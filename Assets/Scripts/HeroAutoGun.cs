using UnityEngine;
using UnityEngine.InputSystem;

namespace WormCrawlerPrototype
{
    public sealed class HeroAutoGun : MonoBehaviour
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

        [Header("Firing")]
        [SerializeField] private float shotsPerSecond = 2f;
        [SerializeField] private float range = 25f;
        [SerializeField] private int bulletDamage = 10;
        [SerializeField] private float bulletKnockbackImpulse = 6f;
        [SerializeField] private float bulletExplosionRadiusHeroHeights = 0.35f;
        [SerializeField] private LayerMask hitMask = ~0;

        private InputAction _shoot;
        private float _nextShotTime;
        private bool _held;
        private bool _consumedThisHold;

        private TurnManager _turn;

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
            _shoot = new InputAction("ShootAutoGun", InputActionType.Button);
            _shoot.AddBinding("<Keyboard>/space");
            _shoot.AddBinding("<Gamepad>/rightTrigger");
            _shoot.started += OnShootStarted;
            _shoot.canceled += OnShootCanceled;
            _shoot.Enable();

            ApplyInputEnabledState();

#if UNITY_6000_0_OR_NEWER
            _turn = Object.FindFirstObjectByType<TurnManager>();
#else
            _turn = Object.FindObjectOfType<TurnManager>();
#endif
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
            _held = false;
            _consumedThisHold = false;
        }

        private void OnShootStarted(InputAction.CallbackContext ctx)
        {
            if (!InputEnabled) return;
            if (!Enabled) return;
            _held = true;
            _consumedThisHold = false;
            _nextShotTime = Mathf.Min(_nextShotTime, Time.time);

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
                if (!_turn.TryConsumeShot(TurnManager.TurnWeapon.AutoGun))
                {
                    _held = false;
                    return;
                }
                _turn.NotifyWeaponSelected(TurnManager.TurnWeapon.AutoGun);
                _consumedThisHold = true;
            }
        }

        private void OnShootCanceled(InputAction.CallbackContext ctx)
        {
            if (!InputEnabled) return;
            _held = false;
            _consumedThisHold = false;

            if (Enabled)
            {
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
                    _turn.NotifyAutoGunReleased();
                }

                var carousel = GetComponent<HeroAmmoCarousel>();
                if (carousel != null)
                {
                    carousel.ForceSelectRope();
                }
            }
        }

        private void LateUpdate()
        {
            if (Bootstrap.IsMapMenuOpen)
            {
                return;
            }

            if (!Enabled)
            {
                _held = false;
                return;
            }

            if (_shoot == null)
            {
                return;
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

            var interval = 1f / Mathf.Max(0.01f, shotsPerSecond);
            if (Time.time < _nextShotTime)
            {
                return;
            }

            _nextShotTime = Time.time + interval;
            FireOnce();
        }

        private void FireOnce()
        {
            if (!_consumedThisHold)
            {
                _held = false;
                return;
            }

            var aim = GetComponent<WormAimController>();
            var origin = (Vector2)transform.position;
            var col = GetComponent<Collider2D>();
            if (col != null)
            {
                origin = col.bounds.center;
            }

            var dir = aim != null ? aim.AimDirection : Vector2.right;
            if (dir.sqrMagnitude < 0.0001f)
            {
                dir = Vector2.right;
            }
            dir.Normalize();

            var hit = Physics2D.Raycast(origin, dir, Mathf.Max(0.1f, range), hitMask);
            if (hit.collider == null || hit.collider.isTrigger)
            {
                return;
            }

            var ht = hit.collider.transform;
            if (ht == transform || ht.IsChildOf(transform))
            {
                return;
            }

            var health = hit.collider.GetComponentInParent<SimpleHealth>();
            if (health != null)
            {
                health.TakeDamage(Mathf.Max(0, bulletDamage), DamageSource.AutoGun);
            }

            var rb = hit.collider.GetComponentInParent<Rigidbody2D>();
            if (rb != null)
            {
                var away = ((Vector2)hit.collider.bounds.center - hit.point);
                if (away.sqrMagnitude < 0.0001f)
                {
                    away = Vector2.up;
                }
                away.Normalize();
                var imp = Mathf.Max(0f, bulletKnockbackImpulse);
                if (imp > 0.0001f)
                {
                    var heroH = Mathf.Max(0.25f, hit.collider.bounds.size.y);
                    var radius = Mathf.Max(0.05f, heroH * Mathf.Max(0.01f, bulletExplosionRadiusHeroHeights));

                    var walker = rb.GetComponent<HeroSurfaceWalker>();
                    if (walker == null)
                    {
                        walker = rb.GetComponentInChildren<HeroSurfaceWalker>();
                    }
                    if (walker != null)
                    {
                        walker.NudgeIdleAnchor(worldDelta: away * radius, unlockSeconds: 0.20f);
                    }

                    rb.AddForce(away * imp, ForceMode2D.Impulse);
                }
            }
        }
    }
}
