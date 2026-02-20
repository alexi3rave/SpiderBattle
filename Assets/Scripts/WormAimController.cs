using UnityEngine;
using UnityEngine.InputSystem;

namespace WormCrawlerPrototype
{
    public sealed class WormAimController : MonoBehaviour
    {
        [SerializeField] private Camera aimCamera;

        [SerializeField] private bool useKeyboardAim = true;
        [SerializeField] private float aimRadius = 9f;
        [SerializeField] private float aimAngularSpeedDeg = 420f;
        [SerializeField] private float aimStepDeg = 5f;

        [SerializeField] private bool stopAtVerticalUnlessTurned = true;

        [SerializeField] private bool showReticle = true;
        [SerializeField] private Color reticleColor = new Color(1f, 0.9f, 0.1f, 0.95f);
        [SerializeField] private float reticleWorldSize = 0.9f;

        public Vector2 AimDirection { get; private set; } = Vector2.right;
        public Vector2 AimWorldPoint { get; private set; }
        public Vector2 AimOriginWorld { get; private set; }

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

        public bool ExternalAimOverride { get; private set; }
        public Vector2 ExternalAimDirection { get; private set; } = Vector2.right;

        public void SetExternalAimOverride(bool enabled, Vector2 dir)
        {
            ExternalAimOverride = enabled;
            ExternalAimDirection = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;
        }

        private InputAction _aimV;
        private InputAction _moveH;
        private float _angleDeg;
        private int _facingSign = 1;
        private bool _clampEnabled;
        private float _clampDeg = 60f;
        private Transform _reticle;
        private static Sprite s_ReticleSprite;

        public float AimStepDeg
        {
            get => aimStepDeg;
            set => aimStepDeg = Mathf.Max(0f, value);
        }

        public void SetAimClampEnabled(bool enabled, float clampDeg)
        {
            _clampEnabled = enabled;
            _clampDeg = Mathf.Clamp(clampDeg, 0f, 180f);
        }

        private void Awake()
        {
            useKeyboardAim = true;

            _aimV = new InputAction("AimV", InputActionType.Value);
            _aimV.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/downArrow").With("Positive", "<Keyboard>/upArrow");
            _aimV.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/s").With("Positive", "<Keyboard>/w");
            _aimV.AddBinding("<Gamepad>/rightStick/y");
            _aimV.Enable();

            _moveH = new InputAction("MoveH", InputActionType.Value);
            _moveH.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/a").With("Positive", "<Keyboard>/d");
            _moveH.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/leftArrow").With("Positive", "<Keyboard>/rightArrow");
            _moveH.AddBinding("<Gamepad>/leftStick/x");
            _moveH.Enable();

            _angleDeg = 0f;
            AimOriginWorld = GetAimOriginWorld();
            EnsureReticle();

            ApplyInputEnabledState();
        }

        private void OnEnable()
        {
            ApplyInputEnabledState();
            if (_reticle != null) _reticle.gameObject.SetActive(showReticle && (InputEnabled || ExternalAimOverride));
        }

        private void OnDisable()
        {
            _aimV?.Disable();
            _moveH?.Disable();
            if (_reticle != null) _reticle.gameObject.SetActive(false);
        }

        private void ApplyInputEnabledState()
        {
            if (_inputEnabled)
            {
                _aimV?.Enable();
                _moveH?.Enable();
            }
            else
            {
                _aimV?.Disable();
                _moveH?.Disable();
            }
        }

        private void OnDestroy()
        {
            _aimV?.Disable();
            _aimV?.Dispose();
            _moveH?.Disable();
            _moveH?.Dispose();
        }

        private void LateUpdate()
        {
            AimOriginWorld = GetAimOriginWorld();

            if (ExternalAimOverride)
            {
                var d = ExternalAimDirection;
                if (d.sqrMagnitude < 0.0001f) d = Vector2.right;
                d.Normalize();
                if (Mathf.Abs(d.x) > 0.15f) _facingSign = d.x >= 0f ? 1 : -1;
                AimDirection = ApplyStepAndClamp(d);
                AimWorldPoint = AimOriginWorld + AimDirection * Mathf.Max(0.05f, aimRadius);
                UpdateReticle();
                return;
            }

            if (!InputEnabled)
            {
                UpdateReticle();
                return;
            }

            UpdateFacing();
            if (useKeyboardAim)
            {
                UpdateKeyboardAim();
                UpdateReticle();
                return;
            }

            var cam = aimCamera != null ? aimCamera : Camera.main;
            if (cam == null)
            {
                AimDirection = Vector2.right;
                AimWorldPoint = AimOriginWorld;
                UpdateReticle();
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null)
            {
                AimDirection = _facingSign >= 0 ? Vector2.right : Vector2.left;
                AimWorldPoint = AimOriginWorld;
                return;
            }

            var mp = mouse.position.ReadValue();
            var wp3 = cam.ScreenToWorldPoint(new Vector3(mp.x, mp.y, -cam.transform.position.z));
            AimWorldPoint = new Vector2(wp3.x, wp3.y);

            var from = AimOriginWorld;
            var dir = AimWorldPoint - from;
            if (dir.sqrMagnitude < 0.0001f)
            {
                AimDirection = Vector2.right;
                return;
            }

            var aim = dir.normalized;
            AimDirection = ApplyStepAndClamp(aim);
            if (Mathf.Abs(AimDirection.x) > 0.15f)
            {
                _facingSign = AimDirection.x >= 0f ? 1 : -1;
            }
            AimWorldPoint = AimOriginWorld + AimDirection * Mathf.Max(0.05f, aimRadius);
            UpdateReticle();
        }

        private void UpdateFacing()
        {
            var h = 0f;
            if (_moveH != null)
            {
                h = _moveH.ReadValue<float>();
            }

            if (Mathf.Abs(h) > 0.15f)
            {
                var newSign = h > 0f ? 1 : -1;
                if (newSign != _facingSign)
                {
                    _facingSign = newSign;
                }
            }
        }

        private void UpdateKeyboardAim()
        {
            var input = 0f;
            if (_aimV != null)
            {
                input = _aimV.ReadValue<float>();
            }

            if (Mathf.Abs(input) > 0.001f)
            {
                var dirSign = _facingSign >= 0 ? 1f : -1f;
                if (_clampEnabled || stopAtVerticalUnlessTurned)
                {
                    var max = GetMaxAimDeg();
                    _angleDeg = Mathf.Clamp(_angleDeg + input * dirSign * (aimAngularSpeedDeg * Time.deltaTime), -max, max);
                }
                else
                {
                    _angleDeg += input * (aimAngularSpeedDeg * Time.deltaTime);
                }
            }

            _angleDeg = SnapDeg(_angleDeg);

            float rad;
            if (_clampEnabled || stopAtVerticalUnlessTurned)
            {
                _angleDeg = Mathf.Clamp(_angleDeg, -GetMaxAimDeg(), GetMaxAimDeg());
                var baseAngle = _facingSign >= 0 ? 0f : 180f;
                rad = (baseAngle + _angleDeg) * Mathf.Deg2Rad;
            }
            else
            {
                _angleDeg = (_angleDeg % 360f + 360f) % 360f;
                rad = _angleDeg * Mathf.Deg2Rad;
            }

            var dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            if (dir.sqrMagnitude < 0.0001f)
            {
                dir = _facingSign >= 0 ? Vector2.right : Vector2.left;
            }

            AimDirection = dir.normalized;
            AimDirection = ApplyStepAndClamp(AimDirection);
            AimWorldPoint = AimOriginWorld + AimDirection * Mathf.Max(0.05f, aimRadius);
        }

        private float GetMaxAimDeg()
        {
            return _clampEnabled ? Mathf.Clamp(_clampDeg, 0f, 180f) : 90f;
        }

        private float SnapDeg(float deg)
        {
            var step = aimStepDeg;
            if (step <= 0.01f)
            {
                return deg;
            }
            return Mathf.Round(deg / step) * step;
        }

        private Vector2 ApplyStepAndClamp(Vector2 dir)
        {
            var d = dir;
            if (d.sqrMagnitude < 0.0001f)
            {
                d = _facingSign >= 0 ? Vector2.right : Vector2.left;
            }

            var ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            ang = (ang % 360f + 360f) % 360f;

            if (_clampEnabled || stopAtVerticalUnlessTurned)
            {
                var baseAngle = _facingSign >= 0 ? 0f : 180f;
                var rel = Mathf.DeltaAngle(baseAngle, ang);
                var max = GetMaxAimDeg();
                rel = Mathf.Clamp(rel, -max, max);
                rel = SnapDeg(rel);
                ang = (baseAngle + rel);
            }
            else
            {
                ang = SnapDeg(ang);
            }

            var final = ang * Mathf.Deg2Rad;
            var outDir = new Vector2(Mathf.Cos(final), Mathf.Sin(final));
            if (outDir.sqrMagnitude < 0.0001f)
            {
                outDir = _facingSign >= 0 ? Vector2.right : Vector2.left;
            }
            return outDir.normalized;
        }

        private Vector2 ClampToFacingHemisphere(Vector2 dir)
        {
            var d = dir;
            if (_facingSign >= 0)
            {
                if (d.x < 0f) d.x = 0f;
            }
            else
            {
                if (d.x > 0f) d.x = 0f;
            }

            if (d.sqrMagnitude < 0.0001f)
            {
                return _facingSign >= 0 ? Vector2.right : Vector2.left;
            }

            return d.normalized;
        }

        private Vector2 GetAimOriginWorld()
        {
            var col = GetComponent<Collider2D>();
            if (col != null)
            {
                return ComputeAabbCenter(col.bounds);
            }

            var sr = GetComponentInChildren<SpriteRenderer>();
            if (sr != null)
            {
                return ComputeAabbCenter(sr.bounds);
            }

            return transform.position;
        }

        private static Vector2 ComputeAabbCenter(Bounds b)
        {
            var min = b.min;
            var max = b.max;
            return new Vector2((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f);
        }

        private void EnsureReticle()
        {
            if (!showReticle)
            {
                if (_reticle != null)
                {
                    _reticle.gameObject.SetActive(false);
                }
                return;
            }

            if (_reticle == null)
            {
                var existing = transform.Find("AimReticle");
                if (existing != null)
                {
                    _reticle = existing;
                }
                else
                {
                    var go = new GameObject("AimReticle");
                    go.transform.SetParent(transform, false);
                    _reticle = go.transform;
                }

                var sr = _reticle.GetComponent<SpriteRenderer>();
                if (sr == null)
                {
                    sr = _reticle.gameObject.AddComponent<SpriteRenderer>();
                }
                sr.sprite = GetOrCreateReticleSprite();
                sr.sortingOrder = 100;
                sr.color = reticleColor;
            }

            _reticle.gameObject.SetActive(true);
        }

        private void UpdateReticle()
        {
            if (!showReticle || (!InputEnabled && !ExternalAimOverride))
            {
                if (_reticle != null) _reticle.gameObject.SetActive(false);
                return;
            }

            EnsureReticle();
            if (_reticle == null) return;

            _reticle.position = new Vector3(AimWorldPoint.x, AimWorldPoint.y, 0f);
            var s = Mathf.Max(0.01f, reticleWorldSize);
            _reticle.localScale = new Vector3(s, s, 1f);

            var sr = _reticle.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                sr.color = reticleColor;
            }
        }

        private static Sprite GetOrCreateReticleSprite()
        {
            if (s_ReticleSprite != null)
            {
                return s_ReticleSprite;
            }

            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var clear = new Color(1f, 1f, 1f, 0f);
            var pixels = new Color[size * size];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = clear;

            var mid = size / 2;
            var line = Color.white;
            var gap = 3;
            var len = 12;
            for (var x = mid - len; x <= mid + len; x++)
            {
                if (Mathf.Abs(x - mid) <= gap) continue;
                pixels[mid * size + x] = line;
            }
            for (var y = mid - len; y <= mid + len; y++)
            {
                if (Mathf.Abs(y - mid) <= gap) continue;
                pixels[y * size + mid] = line;
            }
            pixels[mid * size + mid] = line;

            tex.SetPixels(pixels);
            tex.Apply(false, true);
            s_ReticleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            return s_ReticleSprite;
        }
    }
}
