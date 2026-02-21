using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Profiling;

namespace WormCrawlerPrototype
{
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class SimpleHero : MonoBehaviour
    {
        public static bool EnableHeroDebugLogs = false;

        [SerializeField] private float walkSpeed = 6f;
        [SerializeField] private float accel = 20f;
        [SerializeField] private bool useCurvedSurfaceTangent = true;
        [SerializeField] private float curvedSurfaceProbeDistance = 2.0f;
        [SerializeField] private int curvedSurfaceProbeSteps = 6;
        [SerializeField] private float curvedSurfaceProbeUp = 0.5f;
        [SerializeField] private float groundRayStart = 0.1f;
        [SerializeField] private float groundRayLength = 0.8f;
        [SerializeField] private LayerMask groundMask = ~0;

        [SerializeField] private Vector2 respawnPosition = new Vector2(10f, 30f);
        [SerializeField] private float respawnMinY = -20f;
        [SerializeField] private float respawnRayDown = 200f;
        [SerializeField] private float respawnSurfaceClearance = 0.05f;
        [SerializeField] private float respawnLiftStep = 0.2f;
        [SerializeField] private int respawnMaxLiftSteps = 40;
        [SerializeField] private bool enableRespawn = false;

        [SerializeField] private Animator animator;
        [SerializeField] private SpriteRenderer spriteRenderer;

        private Rigidbody2D _rb;
        private CapsuleCollider2D _col;
        private Collider2D[] _selfColliders;
        private int _terrainObstacleLayer;
        private InputAction _move;
        private int _debugFixedFrames;

        private GrappleController _grapple;
        private ContactFilter2D _overlapFilter;
        private LayerMask _queryMask;

        private readonly Collider2D[] _overlap = new Collider2D[16];

        private static PhysicsMaterial2D s_NoFrictionMaterial;

        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int IsRunningHash = Animator.StringToHash("IsRunning");
        private static readonly int AimYHash = Animator.StringToHash("AimY");

        private WormAimController _aim;
        private int _lastLookSign = -1;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _col = GetComponent<CapsuleCollider2D>();
            _selfColliders = GetComponents<Collider2D>();
            _grapple = GetComponent<GrappleController>();
            if (_rb != null)
            {
                _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            }
            _terrainObstacleLayer = LayerMask.NameToLayer("TerrainObstacle");
            if (groundMask == ~0 && _terrainObstacleLayer >= 0)
            {
                groundMask = ~(1 << _terrainObstacleLayer);
            }

            _queryMask = groundMask & ~(1 << gameObject.layer);

            _overlapFilter = new ContactFilter2D();
            _overlapFilter.useTriggers = false;
            _overlapFilter.useDepth = false;
            _overlapFilter.useLayerMask = true;
            _overlapFilter.SetLayerMask(_queryMask);

            if (animator == null)
            {
                animator = GetComponent<Animator>();
                if (animator == null)
                {
                    animator = GetComponentInChildren<Animator>();
                }
            }
            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            }
            _aim = GetComponent<WormAimController>();

            if (EnableHeroDebugLogs)
            {
                Debug.Log($"[Hero] SimpleHero active on '{name}'. animator={(animator != null)} controller={(animator != null && animator.runtimeAnimatorController != null)} spriteRenderer={(spriteRenderer != null)} rb={(_rb != null)} col={(_col != null)}");
                if (_rb != null)
                {
                    Debug.Log($"[Hero] RB init: bodyType={_rb.bodyType} simulated={_rb.simulated} constraints={_rb.constraints} gravityScale={_rb.gravityScale} freezeRotation={_rb.freezeRotation}");
                }
                if (_col != null)
                {
                    Debug.Log($"[Hero] COL init: size={_col.size} offset={_col.offset}");
                }
            }

            var mat = GetNoFrictionMaterial();
            if (_selfColliders != null)
            {
                for (var i = 0; i < _selfColliders.Length; i++)
                {
                    if (_selfColliders[i] != null)
                    {
                        _selfColliders[i].sharedMaterial = mat;
                    }
                }
            }

            _debugFixedFrames = EnableHeroDebugLogs ? 8 : 0;

            _move = new InputAction("Move", InputActionType.Value);
            _move.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/a")
                .With("Positive", "<Keyboard>/d");
            _move.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/leftArrow")
                .With("Positive", "<Keyboard>/rightArrow");
            _move.AddBinding("<Gamepad>/leftStick/x");
            _move.Enable();
        }

        private static PhysicsMaterial2D GetNoFrictionMaterial()
        {
            if (s_NoFrictionMaterial == null)
            {
                s_NoFrictionMaterial = new PhysicsMaterial2D("HeroNoFriction");
            }

            // IMPORTANT: always re-apply settings (Enter Play Mode can disable domain reload).
            s_NoFrictionMaterial.friction = 0.02f;
            s_NoFrictionMaterial.bounciness = 0f;
            s_NoFrictionMaterial.frictionCombine = PhysicsMaterialCombine2D.Minimum;
            s_NoFrictionMaterial.bounceCombine = PhysicsMaterialCombine2D.Minimum;
            return s_NoFrictionMaterial;
        }

        private void OnDestroy()
        {
            _move?.Disable();
            _move?.Dispose();
        }

        private void FixedUpdate()
        {
            Profiler.BeginSample("SimpleHero.FixedUpdate");
            try
            {
                var input = Mathf.Clamp(ReadMoveInput(), -1f, 1f);

                if (_rb != null && _col != null)
                {
                    var scaledSize = Vector2.Scale(_col.size, transform.lossyScale);
                    var scaledOffset = Vector2.Scale(_col.offset, transform.lossyScale);
                    var heroHeight = Mathf.Max(0.25f, _col.bounds.size.y);
                    TryResolvePenetration(_rb.position, scaledSize, scaledOffset, heroHeight);
                }

                var shiftDown = Keyboard.current != null && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);
                var isRunning = shiftDown && Mathf.Abs(input) > 0.1f;
                if (animator != null)
                {
                    animator.SetFloat(SpeedHash, Mathf.Abs(input));
                    animator.SetBool(IsRunningHash, isRunning);
                }

                var lookX = 0f;
                var aimY = 0f;
                if (_aim != null)
                {
                    var d = _aim.AimDirection;
                    if (Mathf.Abs(d.x) > 0.01f)
                    {
                        lookX = d.x > 0f ? 1f : -1f;
                    }
                    aimY = Mathf.Clamp01(Mathf.Max(0f, d.y));
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

                if (spriteRenderer != null)
                {
                    spriteRenderer.flipX = lookX > 0f;
                }
                if (animator != null)
                {
                    animator.SetFloat(AimYHash, aimY);
                }

                var onRope = _grapple != null && _grapple.IsAttached;

                Vector2 origin;
                if (_col != null)
                {
                    var b = _col.bounds;
                    origin = new Vector2(b.center.x, b.min.y + groundRayStart);
                }
                else
                {
                    origin = (Vector2)transform.position + Vector2.up * groundRayStart;
                }

                var scaledRayLength = groundRayLength * Mathf.Max(1f, transform.lossyScale.y);

                var hit = GetGroundHit(origin, scaledRayLength);
                var isGrounded = hit.collider != null;
                if (EnableHeroDebugLogs && _debugFixedFrames > 0)
                {
                    _debugFixedFrames--;
                    var v = GetVelocity();
                    Debug.Log($"[Hero] dbg frame={Time.frameCount} pos={transform.position} input={input:F2} grounded={isGrounded} vel={v}");
                }

                if (!onRope)
                {
                    if (isGrounded)
                    {
                        var n = hit.normal.sqrMagnitude > 0.001f ? hit.normal.normalized : Vector2.up;
                        var tangent = new Vector2(n.y, -n.x);
                        if (tangent.x < 0f)
                        {
                            tangent = -tangent;
                        }

                        var heroHeight = 1f;
                        var heroWidth = 1f;
                        var scaledSize = new Vector2(1f, 1f);
                        var scaledOffset = Vector2.zero;
                        var halfHeight = 0.5f;
                        if (_col != null)
                        {
                            heroHeight = Mathf.Max(0.25f, _col.bounds.size.y);
                            heroWidth = Mathf.Max(0.25f, _col.bounds.size.x);
                            scaledSize = Vector2.Scale(_col.size, transform.lossyScale);
                            scaledOffset = Vector2.Scale(_col.offset, transform.lossyScale);
                            halfHeight = Mathf.Max(0.01f, scaledSize.y * 0.5f);
                        }

                        var bestProbe = hit.point;
                        var hasBestProbe = false;

                    if (useCurvedSurfaceTangent && Mathf.Abs(input) > 0.01f)
                    {
                        var dirSign = input > 0f ? 1f : -1f;
                        var probeDx = dirSign * Mathf.Clamp(heroWidth * 0.45f, 0.18f, Mathf.Max(0.18f, curvedSurfaceProbeDistance));
                        var probeOrigin = new Vector2(origin.x + probeDx, origin.y + Mathf.Max(0f, curvedSurfaceProbeUp));
                        var h = GetGroundHit(probeOrigin, scaledRayLength + Mathf.Abs(probeDx) + 0.25f);
                        if (h.collider != null)
                        {
                            bestProbe = h.point;
                            hasBestProbe = true;

                            var d = bestProbe - hit.point;
                            if (d.sqrMagnitude > 0.0001f)
                            {
                                // Ensure we don't turn tangent vertical on noisy edges.
                                d.x = dirSign * Mathf.Max(0.08f, Mathf.Abs(d.x));
                                tangent = d.normalized;
                                if (tangent.x < 0f)
                                {
                                    tangent = -tangent;
                                }
                            }
                        }
                    }

                    var moveInput = input;
                    if (hasBestProbe)
                    {
                        var stepUp = bestProbe.y - hit.point.y;
                        if (stepUp > heroHeight)
                        {
                            moveInput = 0f;
                        }
                    }

                    var rbPos = _rb.position;
                    if (Mathf.Abs(moveInput) > 0.01f)
                    {
                        var dx = moveInput * walkSpeed * Time.fixedDeltaTime;
                        var targetX = rbPos.x + dx;
                        var colCenterY = rbPos.y + scaledOffset.y;
                        var probeOrigin = new Vector2(targetX, colCenterY + halfHeight + Mathf.Max(0.2f, curvedSurfaceProbeUp));
                        var h = GetGroundHit(probeOrigin, scaledRayLength + heroHeight + 1f);
                        if (h.collider != null)
                        {
                            var stepUp = h.point.y - hit.point.y;
                            if (stepUp <= heroHeight)
                            {
                                var targetY = h.point.y + respawnSurfaceClearance + halfHeight - scaledOffset.y;
                                var p = new Vector2(targetX, targetY);

                                var liftStep = Mathf.Max(0.01f, heroHeight * 0.1f);
                                var ok = !IsOverlappingSolid(p, scaledSize, scaledOffset);
                                if (!ok)
                                {
                                    for (var li = 0; li < Mathf.Max(1, curvedSurfaceProbeSteps); li++)
                                    {
                                        p.y += liftStep;
                                        if (!IsOverlappingSolid(p, scaledSize, scaledOffset))
                                        {
                                            ok = true;
                                            break;
                                        }
                                    }
                                }

                                if (ok)
                                {
                                    _rb.MovePosition(p);
                                    SetVelocity(Vector2.zero);
                                }
                                else
                                {
                                    var pFlat = new Vector2(targetX, rbPos.y);
                                    if (!IsOverlappingSolid(pFlat, scaledSize, scaledOffset))
                                    {
                                        _rb.MovePosition(pFlat);
                                        SetVelocity(Vector2.zero);
                                    }
                                    else
                                    {
                                        var v = GetVelocity();
                                        var targetVx = moveInput * walkSpeed;
                                        var newVx = Mathf.MoveTowards(v.x, targetVx, accel * Time.fixedDeltaTime);
                                        SetVelocity(new Vector2(newVx, v.y));
                                    }
                                }
                            }
                            else
                            {
                                var v = GetVelocity();
                                SetVelocity(new Vector2(0f, v.y));
                            }
                        }
                        else
                        {
                            var v = GetVelocity();
                            var targetVx = moveInput * walkSpeed;
                            var newVx = Mathf.MoveTowards(v.x, targetVx, accel * Time.fixedDeltaTime);
                            SetVelocity(new Vector2(newVx, v.y));
                        }
                    }
                    else
                    {
                        var v = GetVelocity();
                        var newVx = Mathf.MoveTowards(v.x, 0f, accel * Time.fixedDeltaTime);
                        SetVelocity(new Vector2(newVx, v.y));
                    }
                }
                    else
                    {
                        var v = GetVelocity();
                        var targetVx = input * walkSpeed;
                        var newVx = Mathf.MoveTowards(v.x, targetVx, accel * Time.fixedDeltaTime);
                        SetVelocity(new Vector2(newVx, v.y));
                    }
                }

                if (enableRespawn && _rb.position.y < respawnMinY)
                {
                    RespawnSafe();
                }
            }
            finally
            {
                Profiler.EndSample();
            }
        }

        private void RespawnSafe()
        {
            var desired = respawnPosition;
            var safe = FindSafeSpawnPosition(desired);
            _rb.position = safe;
            SetVelocity(Vector2.zero);
        }

        private Vector2 FindSafeSpawnPosition(Vector2 desired)
        {
            if (_col == null)
            {
                return desired;
            }

            var scaledSize = Vector2.Scale(_col.size, transform.lossyScale);
            var scaledOffset = Vector2.Scale(_col.offset, transform.lossyScale);
            var halfHeight = Mathf.Max(0.01f, scaledSize.y * 0.5f);

            var rayOrigin = new Vector2(desired.x, desired.y + Mathf.Max(0f, curvedSurfaceProbeUp));
            var ground = GetGroundHit(rayOrigin, Mathf.Max(1f, respawnRayDown));
            var y = desired.y;
            if (ground.collider != null)
            {
                y = ground.point.y + respawnSurfaceClearance + halfHeight;
            }

            var p = new Vector2(desired.x, y);
            for (var i = 0; i < Mathf.Max(1, respawnMaxLiftSteps); i++)
            {
                if (!IsOverlappingSolid(p, scaledSize, scaledOffset))
                {
                    return p;
                }
                p.y += Mathf.Max(0.01f, respawnLiftStep);
            }

            return p;
        }

        private void TryResolvePenetration(Vector2 rbPos, Vector2 scaledSize, Vector2 scaledOffset, float heroHeight)
        {
            if (!IsOverlappingSolid(rbPos, scaledSize, scaledOffset))
            {
                return;
            }

            var p = rbPos;
            var liftStep = Mathf.Max(0.01f, heroHeight * 0.1f);
            for (var i = 0; i < 12; i++)
            {
                p.y += liftStep;
                if (!IsOverlappingSolid(p, scaledSize, scaledOffset))
                {
                    _rb.position = p;
                    var v = GetVelocity();
                    if (v.y < 0f)
                    {
                        SetVelocity(new Vector2(v.x, 0f));
                    }
                    return;
                }
            }
        }

        private bool IsOverlappingSolid(Vector2 rbPos, Vector2 scaledSize, Vector2 scaledOffset)
        {
            Profiler.BeginSample("SimpleHero.IsOverlappingSolid");
            try
            {
                var center = rbPos + scaledOffset;
                var count = Physics2D.OverlapCapsule(center, scaledSize, CapsuleDirection2D.Vertical, 0f, _overlapFilter, _overlap);
                if (count <= 0)
                {
                    return false;
                }

                for (var i = 0; i < count; i++)
                {
                    var c = _overlap[i];
                    _overlap[i] = null;
                    if (c != null)
                    {
                        return true;
                    }
                }

                return false;
            }
            finally
            {
                Profiler.EndSample();
            }
        }

        private float ReadMoveInput()
        {
            var v = 0f;
            if (_move != null)
            {
                v = _move.ReadValue<float>();
            }

            if (Mathf.Abs(v) > 0.001f)
            {
                return v;
            }

            if (Keyboard.current != null)
            {
                var left = Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed;
                var right = Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed;
                if (left && !right) return -1f;
                if (right && !left) return 1f;
                return 0f;
            }

            return 0f;
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

        private RaycastHit2D GetGroundHit(Vector2 origin, float len)
        {
            Profiler.BeginSample("SimpleHero.GetGroundHit");
            try
            {
                var h = Physics2D.Raycast(origin, Vector2.down, len, _queryMask);
                if (h.collider != null && h.collider.isTrigger)
                {
                    return default;
                }

                return h;
            }
            finally
            {
                Profiler.EndSample();
            }
        }

        private bool IsSelfCollider(Collider2D c)
        {
            if (_selfColliders == null || c == null)
            {
                return false;
            }

            for (var i = 0; i < _selfColliders.Length; i++)
            {
                if (_selfColliders[i] == c)
                {
                    return true;
                }
            }

            return false;
        }

        private void OnDrawGizmosSelected()
        {
            Vector2 origin;
            var c = GetComponent<CapsuleCollider2D>();
            if (c != null)
            {
                var b = c.bounds;
                origin = new Vector2(b.center.x, b.min.y + groundRayStart);
            }
            else
            {
                origin = (Vector2)transform.position + Vector2.up * groundRayStart;
            }
            Gizmos.color = Color.yellow;
            var scaledRayLength = groundRayLength * Mathf.Max(1f, transform.lossyScale.y);
            Gizmos.DrawLine(origin, origin + Vector2.down * scaledRayLength);
        }
    }
}
