using UnityEngine;
using UnityEngine.InputSystem;

namespace WormCrawlerPrototype
{
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class HeroSurfaceWalker : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float moveSpeed = 4.5f;
        [SerializeField] private float acceleration = 55f;
        [SerializeField] private float airControl = 0.35f;
        [SerializeField] private float jumpSpeed = 7.5f;

        [Header("Ground")]
        [SerializeField] private LayerMask groundMask;
        [SerializeField] private float groundCheckDistance = 0.25f;
        [SerializeField] private float maxSlopeAngle = 65f;
        [SerializeField] private float stickToGroundForce = 35f;

        [Header("Idle")]
        [SerializeField] private float idleDeceleration = 80f;
        [SerializeField] private float idleStopSpeed = 0.10f;
        [SerializeField] private float cancelTangentGravityFactor = 1.0f;

        [Header("Surface Materials")]
        [SerializeField] private PhysicsMaterial2D movingMaterial;
        [SerializeField] private PhysicsMaterial2D idleMaterial;

        private Rigidbody2D _rb;
        private Collider2D _col;
        private Animator _animator;
        private SpriteRenderer _spriteRenderer;
        private WormAimController _aim;
        private GrappleController _grapple;

        private TargetJoint2D _idleAnchor;
        private bool _idleAnchorEngaged;
        private Vector2 _idleAnchorTarget;
        private float _idleAnchorExternalUnlockUntilT;

        private InputAction _moveH;
        private InputAction _jump;

        private bool _externalMoveOverride;
        private float _externalMoveH;
        private bool _additionalMoveInput;
        private float _additionalMoveH;

        public void SetExternalMoveOverride(bool enabled, float moveH)
        {
            _externalMoveOverride = enabled;
            _externalMoveH = Mathf.Clamp(moveH, -1f, 1f);
        }

        public void SetAdditionalMoveInput(bool enabled, float moveH)
        {
            _additionalMoveInput = enabled;
            _additionalMoveH = Mathf.Clamp(moveH, -1f, 1f);
        }

        public void SetIdleAnchorLock(bool enabled)
        {
            if (_rb == null || _idleAnchor == null)
            {
                return;
            }

            if (!enabled)
            {
                if (_idleAnchor.enabled) _idleAnchor.enabled = false;
                _idleAnchorEngaged = false;
                _rb.constraints = _defaultConstraints;
                return;
            }

            // Only lock when we're actually in contact with ground; otherwise we'd freeze midair.
            if (!_hasGroundContact || !_grounded)
            {
                return;
            }

            _idleAnchorExternalUnlockUntilT = 0f;
            _idleAnchorTarget = _rb.position;
            _idleAnchor.target = _idleAnchorTarget;
            _idleAnchor.enabled = true;
            _idleAnchorEngaged = true;
            _rb.constraints = _defaultConstraints | RigidbodyConstraints2D.FreezePositionX | RigidbodyConstraints2D.FreezePositionY;
            _rb.WakeUp();
        }

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

        private Vector2 _groundNormal = Vector2.up;
        private bool _grounded;

        private bool _hasGroundContact;

        public bool IsGroundedWithContact => _grounded && _hasGroundContact;

        private bool _wasGrounded;
        private float _airbornePeakY;
        private bool _wasRopeAttached;
        private bool _needsAirborneFrameAfterRopeDetach;

        private int _lastLookSign = 1;
        private PhysicsMaterial2D _defaultMaterial;
        private RigidbodyConstraints2D _defaultConstraints;

        private static readonly int IsGroundedHash = Animator.StringToHash("IsGrounded");
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int IsRunningHash = Animator.StringToHash("IsRunning");
        private static readonly int AimYHash = Animator.StringToHash("AimY");

        private bool _hasIsGroundedParam;
        private bool _hasSpeedParam;
        private bool _hasIsRunningParam;
        private bool _hasAimYParam;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _col = GetComponent<Collider2D>();
            _animator = GetComponent<Animator>();
            _spriteRenderer = ResolveHeroSpriteRenderer();
            _aim = GetComponent<WormAimController>();
            _grapple = GetComponent<GrappleController>();

            if (groundMask.value == 0)
            {
                groundMask = ~0;
                var obstacleLayer = LayerMask.NameToLayer("TerrainObstacle");
                if (obstacleLayer >= 0)
                {
                    groundMask = ~(1 << obstacleLayer);
                }
            }

            _idleAnchor = GetComponent<TargetJoint2D>();
            if (_idleAnchor == null)
            {
                _idleAnchor = gameObject.AddComponent<TargetJoint2D>();
            }
            _idleAnchor.enabled = false;
            _idleAnchor.autoConfigureTarget = false;
            _idleAnchor.dampingRatio = 1.0f;
            _idleAnchor.frequency = 12f;
            _idleAnchor.maxForce = 6000f;
            _idleAnchorEngaged = false;

            if (_col != null)
            {
                _defaultMaterial = _col.sharedMaterial;
            }

            _rb.freezeRotation = true;
            _rb.sleepMode = RigidbodySleepMode2D.NeverSleep;
            _defaultConstraints = _rb.constraints;

            CacheAnimatorParams();

            _moveH = new InputAction("MoveH", InputActionType.Value);
            _moveH.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/a")
                .With("Positive", "<Keyboard>/d");
            _moveH.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/leftArrow")
                .With("Positive", "<Keyboard>/rightArrow");
            _moveH.AddBinding("<Gamepad>/leftStick/x");

            _jump = new InputAction("Jump", InputActionType.Button);
            _jump.AddBinding("<Keyboard>/space");
            _jump.AddBinding("<Gamepad>/buttonSouth");

            ApplyInputEnabledState();
        }

        private void ApplyInputEnabledState()
        {
            if (_inputEnabled)
            {
                _moveH?.Enable();
                _jump?.Enable();
            }
            else
            {
                _moveH?.Disable();
                _jump?.Disable();
            }
        }

        private void OnDestroy()
        {
            _moveH?.Disable();
            _moveH?.Dispose();
            _jump?.Disable();
            _jump?.Dispose();
        }

        private void OnDisable()
        {
            _moveH?.Disable();
            _jump?.Disable();

            _externalMoveOverride = false;
            _externalMoveH = 0f;
            _additionalMoveInput = false;
            _additionalMoveH = 0f;

            if (_idleAnchor != null) _idleAnchor.enabled = false;
            _idleAnchorEngaged = false;

            if (_rb != null)
            {
                _rb.constraints = _defaultConstraints;
            }
        }

        private void OnEnable()
        {
            ApplyInputEnabledState();
        }

        private void FixedUpdate()
        {
            UpdateGrounded();
            UpdateFallDamageState();

            if (_grapple != null && _grapple.IsAttached)
            {
                if (_idleAnchor != null) _idleAnchor.enabled = false;
                _idleAnchorEngaged = false;
                if (_rb != null) _rb.constraints = _defaultConstraints;
                UpdateAnimator(0f);
                return;
            }

            var input = 0f;
            if (_externalMoveOverride)
            {
                input = _externalMoveH;
            }
            else
            {
                input = ReadHorizontalInput();
            }
            UpdateSurfaceMaterial(input);

            UpdateIdleAnchor(input);

            if (_grounded)
            {
                ApplyGroundMovement(input);
            }
            else
            {
                ApplyAirMovement(input);
            }

            if (InputEnabled)
            {
                TryJump();
            }
            UpdateAnimator(input);
            UpdateFacing(input);
        }

        private float ReadHorizontalInput()
        {
            var h = InputEnabled && _moveH != null ? Mathf.Clamp(_moveH.ReadValue<float>(), -1f, 1f) : 0f;
            if (_additionalMoveInput)
            {
                h = Mathf.Clamp(_additionalMoveH, -1f, 1f);
            }
            return h;
        }

        private void UpdateFallDamageState()
        {
            var center = GetCenterPosition();

            var groundedNow = _hasGroundContact;

            var ropeAttached = _grapple != null && _grapple.IsAttached;
            if (_wasRopeAttached && !ropeAttached)
            {
                _airbornePeakY = center.y;
                _wasGrounded = false;
                _needsAirborneFrameAfterRopeDetach = true;
            }
            if (ropeAttached)
            {
                if (!_wasRopeAttached)
                {
                    _airbornePeakY = center.y;
                }
                else
                {
                    _airbornePeakY = Mathf.Max(_airbornePeakY, center.y);
                }

                _wasRopeAttached = true;
                _wasGrounded = false;
                _needsAirborneFrameAfterRopeDetach = false;
                return;
            }

            _wasRopeAttached = false;

            if (_needsAirborneFrameAfterRopeDetach)
            {
                if (!groundedNow)
                {
                    _needsAirborneFrameAfterRopeDetach = false;
                }
                else
                {
                    // Ignore a "fake" landing caused by ground raycasts while still very close to the surface.
                    // We require at least one frame of being airborne after rope detach.
                    _wasGrounded = false;
                    return;
                }
            }

            if (!groundedNow)
            {
                if (_wasGrounded)
                {
                    _airbornePeakY = center.y;
                }
                else
                {
                    _airbornePeakY = Mathf.Max(_airbornePeakY, center.y);
                }
                _wasGrounded = false;
                return;
            }

            if (!_wasGrounded)
            {
                var fallDist = Mathf.Max(0f, _airbornePeakY - center.y);
                TryApplyFallDamage(fallDist);
            }

            _wasGrounded = true;
        }

        private void OnCollisionStay2D(Collision2D collision)
        {
            if (collision == null || collision.collider == null)
            {
                return;
            }

            var layer = collision.collider.gameObject.layer;
            if (((groundMask.value >> layer) & 1) == 0)
            {
                return;
            }

            if (collision.collider.isTrigger)
            {
                return;
            }

            _hasGroundContact = true;
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            OnCollisionStay2D(collision);
        }

        private void OnCollisionExit2D(Collision2D collision)
        {
            if (collision == null || collision.collider == null)
            {
                return;
            }

            var layer = collision.collider.gameObject.layer;
            if (((groundMask.value >> layer) & 1) == 0)
            {
                return;
            }

            _hasGroundContact = false;
        }

        private void TryApplyFallDamage(float fallDistance)
        {
            if (fallDistance <= 0.0001f)
            {
                return;
            }

            var heroH = 1f;
            if (_col != null)
            {
                heroH = Mathf.Max(0.25f, _col.bounds.size.y);
            }

            var threshold = Mathf.Max(0.01f, heroH * 3f);
            if (fallDistance < threshold)
            {
                return;
            }

            var damage = 10;
            var over = fallDistance - threshold;
            if (over > 0.0001f)
            {
                var stepSize = threshold * 0.2f;
                var steps = Mathf.CeilToInt(over / Mathf.Max(0.0001f, stepSize));
                damage += Mathf.Max(0, steps) * 5;
            }

            var health = GetComponent<SimpleHealth>();
            if (health != null)
            {
                health.TakeDamage(damage, DamageSource.Fall);
            }
        }

        private void UpdateGrounded()
        {
            _grounded = false;
            _groundNormal = Vector2.up;

            var origin = GetFootPosition();
            var hit = Physics2D.Raycast(origin, Vector2.down, groundCheckDistance, groundMask);
            if (hit.collider == null)
            {
                var c = GetCenterPosition();
                hit = Physics2D.Raycast(c, Vector2.down, groundCheckDistance, groundMask);
            }

            if (hit.collider == null || hit.collider.isTrigger)
            {
                return;
            }

            var slope = Vector2.Angle(hit.normal, Vector2.up);
            if (slope > maxSlopeAngle)
            {
                return;
            }

            _grounded = true;
            _groundNormal = hit.normal.sqrMagnitude > 0.0001f ? hit.normal.normalized : Vector2.up;

            var v = GetVelocity();
            var vn = Vector2.Dot(v, _groundNormal);
            if (vn > 0f)
            {
                vn = 0f;
            }

            SetVelocity(v - _groundNormal * Vector2.Dot(v, _groundNormal) + _groundNormal * vn);

            _rb.AddForce(-_groundNormal * stickToGroundForce, ForceMode2D.Force);
        }

        private void ApplyGroundMovement(float input)
        {
            var v = GetVelocity();
            var tangent = new Vector2(_groundNormal.y, -_groundNormal.x);
            if (tangent.sqrMagnitude > 0.0001f)
            {
                tangent.Normalize();
            }

            var vt = Vector2.Dot(v, tangent);
            var vn = Vector2.Dot(v, _groundNormal);

            if (Mathf.Abs(input) < 0.10f)
            {
                CancelGravityAlongTangent(tangent);
                var idleMaxDV = Mathf.Max(0f, idleDeceleration) * Time.fixedDeltaTime;
                var idleVt = Mathf.MoveTowards(vt, 0f, idleMaxDV);
                if (Mathf.Abs(idleVt) < idleStopSpeed)
                {
                    idleVt = 0f;
                }

                // Stick to ground: no relative sliding along the surface when idle.
                // Keep only a small non-positive normal component so we don't "pop" off the ground.
                var stickyVn = Mathf.Min(0f, vn);
                SetVelocity(tangent * 0f + _groundNormal * stickyVn);
                return;
            }

            var desiredVt = input * moveSpeed;
            var moveMaxDV = Mathf.Max(0f, acceleration) * Time.fixedDeltaTime;
            var newVt = Mathf.MoveTowards(vt, desiredVt, moveMaxDV);

            if (vn > 0f)
            {
                vn = 0f;
            }

            SetVelocity(tangent * newVt + _groundNormal * vn);
        }

        private void UpdateIdleAnchor(float input)
        {
            if (_idleAnchor == null)
            {
                return;
            }

            var wantsJump = InputEnabled && _jump != null && _jump.WasPressedThisFrame();

            // Only engage the idle anchor when we have actual collision contact with the ground.
            // A raycast-based grounded check can be true while hovering slightly above the surface,
            // which would freeze the hero in midair.
            if (!_hasGroundContact)
            {
                if (_idleAnchor.enabled) _idleAnchor.enabled = false;
                _idleAnchorEngaged = false;
                _rb.constraints = _defaultConstraints;
                return;
            }

            if (!_grounded)
            {
                if (_idleAnchor.enabled) _idleAnchor.enabled = false;
                _idleAnchorEngaged = false;
                _rb.constraints = _defaultConstraints;
                return;
            }

            if (wantsJump || Mathf.Abs(input) >= 0.10f)
            {
                if (_idleAnchor.enabled) _idleAnchor.enabled = false;
                _idleAnchorEngaged = false;
                _rb.constraints = _defaultConstraints;
                return;
            }

            if (!_idleAnchorEngaged)
            {
                _idleAnchorTarget = _rb.position;
                _idleAnchor.target = _idleAnchorTarget;
                _idleAnchor.enabled = true;
                _idleAnchorEngaged = true;

                // Hard lock: prevents any sliding along slopes when idle.
                if (Time.time < _idleAnchorExternalUnlockUntilT)
                {
                    _rb.constraints = _defaultConstraints;
                }
                else
                {
                    _rb.constraints = _defaultConstraints | RigidbodyConstraints2D.FreezePositionX | RigidbodyConstraints2D.FreezePositionY;
                }
            }
            else
            {
                _idleAnchor.target = _idleAnchorTarget;
                if (!_idleAnchor.enabled) _idleAnchor.enabled = true;

                if (Time.time < _idleAnchorExternalUnlockUntilT)
                {
                    _rb.constraints = _defaultConstraints;
                }
                else
                {
                    _rb.constraints = _defaultConstraints | RigidbodyConstraints2D.FreezePositionX | RigidbodyConstraints2D.FreezePositionY;
                }
            }
        }

        public void NudgeIdleAnchor(Vector2 worldDelta, float unlockSeconds)
        {
            if (_rb == null)
            {
                return;
            }

            _idleAnchorExternalUnlockUntilT = Mathf.Max(_idleAnchorExternalUnlockUntilT, Time.time + Mathf.Max(0.01f, unlockSeconds));

            _idleAnchorTarget += worldDelta;
            if (_idleAnchor != null)
            {
                _idleAnchor.target = _idleAnchorTarget;
                if (_idleAnchorEngaged && !_idleAnchor.enabled) _idleAnchor.enabled = true;
            }

            _rb.constraints = _defaultConstraints;
            _rb.WakeUp();
        }

        private void CancelGravityAlongTangent(Vector2 tangent)
        {
            if (cancelTangentGravityFactor <= 0.0001f)
            {
                return;
            }

            if (tangent.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            var g = Physics2D.gravity * _rb.gravityScale;
            var gAlongTangent = Vector2.Dot(g, tangent) * tangent;
            if (gAlongTangent.sqrMagnitude <= 0.0000001f)
            {
                return;
            }

            _rb.AddForce(-gAlongTangent * (_rb.mass * cancelTangentGravityFactor), ForceMode2D.Force);
        }

        private void TryJump()
        {
            if (!_grounded)
            {
                return;
            }

            if (_jump == null || !_jump.WasPressedThisFrame())
            {
                return;
            }

            var v = GetVelocity();
            v += _groundNormal * Mathf.Max(0.01f, jumpSpeed);
            SetVelocity(v);
            _grounded = false;
        }

        private void ApplyAirMovement(float input)
        {
            if (Mathf.Abs(input) < 0.01f)
            {
                return;
            }

            var targetVelX = input * moveSpeed;
            var currentVelX = GetVelocity().x;
            var deltaV = targetVelX - currentVelX;
            var force = deltaV * acceleration * airControl;
            _rb.AddForce(Vector2.right * force, ForceMode2D.Force);
        }

        private void UpdateSurfaceMaterial(float input)
        {
            if (_col == null)
            {
                return;
            }

            PhysicsMaterial2D target;
            if (_grounded)
            {
                target = Mathf.Abs(input) < 0.10f ? idleMaterial : movingMaterial;
            }
            else
            {
                target = movingMaterial;
            }

            if (target == null)
            {
                target = _defaultMaterial;
            }

            if (_col.sharedMaterial != target)
            {
                _col.sharedMaterial = target;
            }
        }

        private SpriteRenderer ResolveHeroSpriteRenderer()
        {
            var srs = GetComponentsInChildren<SpriteRenderer>(true);
            if (srs == null || srs.Length == 0)
            {
                return null;
            }

            SpriteRenderer best = null;
            var bestOrder = int.MaxValue;
            for (var i = 0; i < srs.Length; i++)
            {
                var sr = srs[i];
                if (sr == null) continue;
                if (sr.sortingOrder < bestOrder)
                {
                    best = sr;
                    bestOrder = sr.sortingOrder;
                }
            }

            return best;
        }

        private void UpdateFacing(float input)
        {
            var lookX = 0f;
            if (_aim != null)
            {
                var d = _aim.AimDirection;
                if (Mathf.Abs(d.x) > 0.01f)
                {
                    lookX = d.x > 0f ? 1f : -1f;
                }
            }
            if (Mathf.Abs(lookX) < 0.01f && Mathf.Abs(input) > 0.01f)
            {
                lookX = input > 0f ? 1f : -1f;
            }

            if (Mathf.Abs(lookX) > 0.01f)
            {
                _lastLookSign = lookX > 0f ? 1 : -1;
            }
            else
            {
                lookX = _lastLookSign;
            }

            if (_spriteRenderer != null)
            {
                _spriteRenderer.flipX = lookX > 0f;
            }
        }

        private void UpdateAnimator(float input)
        {
            if (_animator == null) return;

            if (_hasIsGroundedParam)
            {
                _animator.SetBool(IsGroundedHash, _grounded);
            }
            if (_hasSpeedParam)
            {
                _animator.SetFloat(SpeedHash, Mathf.Abs(input));
            }
            if (_hasIsRunningParam)
            {
                _animator.SetBool(IsRunningHash, Mathf.Abs(input) > 0.1f);
            }
            if (_hasAimYParam && _aim != null)
            {
                var d = _aim.AimDirection;
                _animator.SetFloat(AimYHash, Mathf.Clamp01(Mathf.Max(0f, d.y)));
            }
        }

        private void CacheAnimatorParams()
        {
            _hasIsGroundedParam = false;
            _hasSpeedParam = false;
            _hasIsRunningParam = false;
            _hasAimYParam = false;
            if (_animator == null || _animator.runtimeAnimatorController == null)
            {
                return;
            }

            var ps = _animator.parameters;
            if (ps == null)
            {
                return;
            }

            for (var i = 0; i < ps.Length; i++)
            {
                var p = ps[i];
                if (p.type == AnimatorControllerParameterType.Bool && p.nameHash == IsGroundedHash)
                {
                    _hasIsGroundedParam = true;
                }
                else if (p.type == AnimatorControllerParameterType.Float && p.nameHash == SpeedHash)
                {
                    _hasSpeedParam = true;
                }
                else if (p.type == AnimatorControllerParameterType.Bool && p.nameHash == IsRunningHash)
                {
                    _hasIsRunningParam = true;
                }
                else if (p.type == AnimatorControllerParameterType.Float && p.nameHash == AimYHash)
                {
                    _hasAimYParam = true;
                }
            }
        }

        private Vector2 GetVelocity()
        {
#if UNITY_6000_0_OR_NEWER
            return _rb.linearVelocity;
#else
            return _rb.velocity;
#endif
        }

        private void SetVelocity(Vector2 v)
        {
#if UNITY_6000_0_OR_NEWER
            _rb.linearVelocity = v;
#else
            _rb.velocity = v;
#endif
        }

        private Vector2 GetFootPosition()
        {
            if (_col != null)
            {
                var b = _col.bounds;
                return new Vector2(b.center.x, b.min.y);
            }
            return (Vector2)transform.position + Vector2.down * 0.3f;
        }

        private Vector2 GetCenterPosition()
        {
            if (_col != null)
            {
                return _col.bounds.center;
            }
            return transform.position;
        }
    }
}
