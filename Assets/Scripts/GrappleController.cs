using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WormCrawlerPrototype
{
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class GrappleController : MonoBehaviour
    {
        private static Type s_TilemapCollider2DType;
        private static Type s_TilemapType;
        private static Type s_TilemapRendererType;

        [Header("References")]
        [SerializeField] private WormAimController aim;
        [SerializeField] private LineRenderer line;
        [SerializeField] private Animator animator;

        [Header("Shot")]
        [SerializeField] private float maxDistance = 36f;
        [SerializeField] private float shotTravelTime = 0.08f;
        [SerializeField] private float missRetractTime = 0.12f;

        [SerializeField] private float ropeWidth = 0.08f;

        [Header("Rope Visuals")]
        [SerializeField] private bool enableRopeVisuals = true;
        [SerializeField] private float ropeWidthMultiplier = 2.0f;
        [SerializeField] private Color ropeEdgeColor = new Color(0.20f, 0.65f, 1.0f, 0.00f);
        [SerializeField] private Color ropeCoreColor = new Color(0.20f, 0.65f, 1.0f, 0.95f);
        [SerializeField] private float ropeCoreWidthFraction = 0.0f;
        [SerializeField] private Material ropeGradientMaterial;
        [SerializeField] private Material ropeAuxMaterial;

        [Header("Rope Sparks")]
        [SerializeField] private bool enableRopeSparks = true;
        [SerializeField] private float sparkSpacingHeroHeights = 3f;
        [SerializeField] private float sparkSpeed = 16f;
        [SerializeField] private float sparkSegmentLength = 0.9f;
        [SerializeField] private float sparkAmplitude = 0.10f;
        [SerializeField] private int maxSparks = 8;
        [SerializeField] private Color sparkColor = new Color(0.65f, 0.90f, 1.0f, 0.95f);

        [Header("Anchor Drop")]
        [SerializeField] private bool enableAnchorDrop = true;
        [SerializeField] private int anchorDropStrands = 5;
        [SerializeField] private float anchorDropLength = 0.75f;
        [SerializeField] private float anchorDropSpread = 0.22f;
        [SerializeField] private float anchorDropWidthMultiplier = 2.2f;
        [SerializeField] private float anchorHeadHalfFractionOfHeroHeight = 0.25f;
        [SerializeField] private float anchorRenderInsetFractionOfHeroHeight = 0.06f;
        [SerializeField] private float anchorLightningAmplitudeFraction = 0.25f;

        [Header("Attach")]
        [SerializeField] private float anchorSurfaceOffset = 0.05f;
        [SerializeField] private float minDistance = 1.5f;
        [SerializeField] private float maxRopeLength = 36f;

        [Header("Swing")]
        [SerializeField] private float swingForce = 32f;
        [SerializeField] private float noInputRopeDamping = 4f;

        [SerializeField] private float verticalFallBiasFraction = 1.0f;
        [SerializeField] private float verticalFallBiasTangentDamping = 6f;
        [SerializeField] private float verticalFallBiasControlAngleDeg = 25f;
        [SerializeField] private float verticalFallBiasKickSpeed = 0.9f;

        [SerializeField] private float ropeUpwardGravityFactor = 12f;

        [SerializeField] private float ropeGravityAssist = 1.0f;
        [SerializeField] private float ropeGravityAssistMinAngleDeg = 5f;
        [SerializeField] private float ropeGravityAssistMinAngleDegShort = 20f;
        [SerializeField] private float ropeGravityAssistMinAngleShortFraction = 0.2f;
        [SerializeField] private float ropeGravityAssistMaxTangentSpeed = 999f;

        [Header("Reel")]
        [SerializeField] private float reelSpeed = 9f;

        [SerializeField] private float reelInGravityScaleFactor = 0f;

        [Header("Stiffness")]
        [SerializeField] private float stiffLineDamping = 45f;
        [SerializeField] private float stiffLineSpring = 220f;
        [SerializeField] private float stiffLineMaxStep = 0.35f;

        [Header("Wrap")]
        [SerializeField] private float minSegment = 0.05f;

        [Header("Rope In-Hand Sprite")]
        [SerializeField] private string ropeHandSpriteResourcesPath = "Weapons/rope";
        [SerializeField] private float ropeHandHeightFraction = 0.125f;
        [SerializeField] private bool ropeHandBaseMirrorY = true;
        [SerializeField] private Vector2 ropeHandOffsetFraction = new Vector2(-0.5f, 0.15f);
        [SerializeField] private Vector2 ropeHandCenterOffsetPixels = Vector2.zero;
        [SerializeField] private float ropeHandPixelsPerUnit = 100f;
        [SerializeField] private Vector2 ropeHandPivotNormalized = new Vector2(0.5f, 0.5f);
        [SerializeField] private float ropeHandAimAngleOffsetDeg = 0f;
        [SerializeField] private float ropeHandTridentDownAngleDeg = -180f;
        [SerializeField] private bool ropeHandUseSharedSettingsForAllHeroes = true;
        [SerializeField] private bool ropeHandFollowAim = false;
        [SerializeField] private bool ropeHandFlipYWhenFacingLeft = true;
        [SerializeField] private int ropeHandSortingOrderOffset = -2;

        private Transform _ropeHandPivotT;
        private Transform _ropeHandSpriteT;
        private SpriteRenderer _ropeHandSr;
        private Sprite _ropeHandSprite;
        private bool _ropeHandVisible;

        private static bool _sharedRopeHandSettingsInitialized;
        private static Vector2 _sharedRopeHandOffsetFraction;
        private static Vector2 _sharedRopeHandCenterOffsetPixels;
        private static float _sharedRopeHandPixelsPerUnit;
        private static float _sharedRopeHandAimAngleOffsetDeg;
        private static float _sharedRopeHandTridentDownAngleDeg;

        private void SyncSharedRopeHandSettingsFromThis()
        {
            _sharedRopeHandOffsetFraction = ropeHandOffsetFraction;
            _sharedRopeHandCenterOffsetPixels = ropeHandCenterOffsetPixels;
            _sharedRopeHandPixelsPerUnit = Mathf.Max(0.01f, ropeHandPixelsPerUnit);
            _sharedRopeHandAimAngleOffsetDeg = ropeHandAimAngleOffsetDeg;
            _sharedRopeHandTridentDownAngleDeg = ropeHandTridentDownAngleDeg;
            _sharedRopeHandSettingsInitialized = true;
        }

        private void OnValidate()
        {
            if (ropeHandUseSharedSettingsForAllHeroes)
            {
                SyncSharedRopeHandSettingsFromThis();
            }
        }

        public void SetRopeHandVisible(bool visible)
        {
            _ropeHandVisible = visible;
            if (_ropeHandPivotT != null)
            {
                _ropeHandPivotT.gameObject.SetActive(visible && _state == RopeState.Idle);
            }
        }

        private enum RopeState
        {
            Idle,
            Shooting,
            Retracting,
            Attached
        }

        public bool IsAttached => _state == RopeState.Attached;

        public void FireRope(Vector2 dir)
        {
            if (!InputEnabled)
            {
                return;
            }

            if (_state != RopeState.Idle)
            {
                Detach();
                return;
            }

            if (aim != null)
            {
                aim.SetExternalAimOverride(true, dir);
            }
            StartShot();
        }

        public void FireRopeExternal(Vector2 dir)
        {
            if (_state != RopeState.Idle)
            {
                Detach();
                return;
            }

            if (aim != null)
            {
                aim.SetExternalAimOverride(true, dir);
            }

            StartShot();
        }

        public void DetachRope()
        {
            ForceDetach();
        }

        public void ForceDetach()
        {
            if (_state != RopeState.Idle)
            {
                Detach();
            }
        }

        public float MaxRopeLength => maxRopeLength;
        public float CurrentRopeLength => _totalRopeLength;
        public float MinRopeLength => minDistance;

        public Vector2 AnchorWorld => _anchorWorld;

        private bool _inputEnabled = true;
        public bool DetachWhenInputDisabled = true;

        private bool _externalMoveOverride;
        private float _externalMoveH;
        private float _externalMoveV;
        private bool _additionalMoveInput;
        private float _additionalMoveH;
        private float _additionalMoveV;

        public void SetExternalMoveOverride(bool enabled, float moveH, float moveV)
        {
            _externalMoveOverride = enabled;
            _externalMoveH = Mathf.Clamp(moveH, -1f, 1f);
            _externalMoveV = Mathf.Clamp(moveV, -1f, 1f);
        }

        public void SetAdditionalMoveInput(bool enabled, float moveH, float moveV)
        {
            _additionalMoveInput = enabled;
            _additionalMoveH = Mathf.Clamp(moveH, -1f, 1f);
            _additionalMoveV = Mathf.Clamp(moveV, -1f, 1f);
        }
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

        private RopeState _state = RopeState.Idle;
        private Rigidbody2D _rb;
        private DistanceJoint2D _joint;

        

        private Vector2 _anchorWorld;
        private Vector2 _anchorNormalWorld;
        private bool _hasAnchorNormal;
        private Vector2 _shotHitNormal;
        private readonly List<Vector2> _pivots = new List<Vector2>(16);

        private readonly RaycastHit2D[] _rayHits = new RaycastHit2D[96];
        private ContactFilter2D _rayFilter;

        private float _shotT;
        private bool _shotHasHit;
        private Vector2 _shotTarget;

        private Vector2 _stiffLineDir;
        private bool _hasStiffLine;

        private float _totalRopeLength;

        private Material _ropeEffectsMaterial;
        private Material _ropeGradientMaterialInstance;
        private Material _sparkFillMaterialInstance;
        private static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");
        private static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
        private static readonly int CoreWidthFractionId = Shader.PropertyToID("_CoreWidthFraction");

        private readonly List<LineRenderer> _sparkLines = new List<LineRenderer>(8);
        private float _sparkTime;
        private Collider2D _heroCol;

        private Transform _anchorDropRoot;
        private LineRenderer[] _anchorDropLines;
        private float[] _anchorDropPhase;

        private Vector3[] _ropeBuffer;

        private RigidbodySleepMode2D _savedSleepMode;
        private bool _sleepModeSaved;

        private float _savedGravityScale;
        private bool _gravityScaleSaved;

        private RigidbodyInterpolation2D _savedInterpolation;
        private bool _interpolationSaved;

        private bool _stiffPhaseAvailable;
        private bool _stiffPhaseActive;

        private int _lastFacingSign = 1;

        private InputAction _shoot;
        private InputAction _moveH;
        private InputAction _moveV;

        private static readonly int IsClimbingHash = Animator.StringToHash("IsClimbing");
        private static readonly int ClimbDirHash = Animator.StringToHash("ClimbDir");
        private static readonly int ShootHash = Animator.StringToHash("Shoot");

        private void Awake()
        {
            if (ropeHandUseSharedSettingsForAllHeroes)
            {
                SyncSharedRopeHandSettingsFromThis();
            }

            _rb = GetComponent<Rigidbody2D>();
            _heroCol = GetComponent<Collider2D>();

            if (_rb != null)
            {
                if (!_sleepModeSaved)
                {
                    _savedSleepMode = _rb.sleepMode;
                    _sleepModeSaved = true;
                }
                if (!_gravityScaleSaved)
                {
                    _savedGravityScale = _rb.gravityScale;
                    _gravityScaleSaved = true;
                }
                if (!_interpolationSaved)
                {
                    _savedInterpolation = _rb.interpolation;
                    _interpolationSaved = true;
                }
            }

            if (animator == null)
            {
                animator = GetComponent<Animator>();
            }
            if (aim == null)
            {
                aim = GetComponent<WormAimController>();
            }
            if (aim == null)
            {
                aim = gameObject.AddComponent<WormAimController>();
            }

            _rayFilter = new ContactFilter2D();
            _rayFilter.useTriggers = false;
            _rayFilter.useLayerMask = false;
            _rayFilter.useDepth = false;

            _joint = GetComponent<DistanceJoint2D>();
            if (_joint == null)
            {
                _joint = gameObject.AddComponent<DistanceJoint2D>();
            }

            _joint.autoConfigureDistance = false;
            _joint.autoConfigureConnectedAnchor = false;
            _joint.enableCollision = false;
            _joint.enabled = false;

            if (line == null)
            {
                line = GetComponent<LineRenderer>();
            }

            if (line == null)
            {
                var go = new GameObject("RopeLine");
                go.transform.SetParent(transform, false);
                line = go.AddComponent<LineRenderer>();
            }

            line.enabled = false;
            line.useWorldSpace = true;
            if (line.positionCount < 2)
            {
                line.positionCount = 2;
            }

            if (_ropeEffectsMaterial == null)
            {
                _ropeEffectsMaterial = ropeAuxMaterial != null ? ropeAuxMaterial : line.sharedMaterial;
            }
            EnsureVisuals();

            _shoot = new InputAction("Shoot", InputActionType.Button);
            _shoot.AddBinding("<Keyboard>/space");
            _shoot.AddBinding("<Gamepad>/rightTrigger");
            _shoot.Enable();

            _moveH = new InputAction("MoveH", InputActionType.Value);
            _moveH.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/a").With("Positive", "<Keyboard>/d");
            _moveH.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/leftArrow").With("Positive", "<Keyboard>/rightArrow");
            _moveH.AddBinding("<Gamepad>/leftStick/x");
            _moveH.Enable();

            _moveV = new InputAction("MoveV", InputActionType.Value);
            _moveV.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/s").With("Positive", "<Keyboard>/w");
            _moveV.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/downArrow").With("Positive", "<Keyboard>/upArrow");
            _moveV.AddBinding("<Gamepad>/leftStick/y");
            _moveV.Enable();

            ApplyInputEnabledState();
        }

        private void OnEnable()
        {
            ApplyInputEnabledState();
        }

        private void OnDisable()
        {
            _shoot?.Disable();
            _moveH?.Disable();
            _moveV?.Disable();

            _additionalMoveInput = false;
            _additionalMoveH = 0f;
            _additionalMoveV = 0f;

            // Defensive: hide visuals when disabled so inactive player doesn't show rope artifacts.
            if (line != null) line.enabled = false;
        }

        private void ApplyInputEnabledState()
        {
            if (_inputEnabled)
            {
                _shoot?.Enable();
                _moveH?.Enable();
                _moveV?.Enable();
            }
            else
            {
                _shoot?.Disable();
                _moveH?.Disable();
                _moveV?.Disable();

                if (DetachWhenInputDisabled && _state != RopeState.Idle)
                {
                    Detach();
                }
            }
        }

        private void EnsureVisuals()
        {
            if (!enableRopeVisuals)
            {
                return;
            }

            if (_ropeEffectsMaterial == null)
            {
                _ropeEffectsMaterial = line.sharedMaterial;
            }

            var baseW = Mathf.Max(0.0001f, ropeWidth);
            var width = enableRopeVisuals ? baseW * Mathf.Max(0.01f, ropeWidthMultiplier) : baseW;

            line.startWidth = width;
            line.endWidth = width;
            line.startColor = Color.white;
            line.endColor = Color.white;

            var heroSr = ResolveHeroSpriteRenderer();
            if (heroSr != null)
            {
                line.sortingLayerID = heroSr.sortingLayerID;
                line.sortingOrder = heroSr.sortingOrder - 2;
            }
            else
            {
                if (line.sortingOrder < 50)
                {
                    line.sortingOrder = 50;
                }
            }

            if (!enableRopeVisuals)
            {
                if (_ropeEffectsMaterial != null)
                {
                    line.sharedMaterial = _ropeEffectsMaterial;
                }
                line.startColor = ropeEdgeColor;
                line.endColor = ropeEdgeColor;
                return;
            }

            Shader shader = null;
            if (ropeGradientMaterial != null)
            {
                shader = ropeGradientMaterial.shader;
            }
            if (shader == null)
            {
                shader = Shader.Find("WormCrawler/RopeRadialGradient");
            }

            if (shader != null)
            {
                if (_ropeGradientMaterialInstance == null || _ropeGradientMaterialInstance.shader != shader)
                {
                    _ropeGradientMaterialInstance = ropeGradientMaterial != null ? new Material(ropeGradientMaterial) : new Material(shader);
                }

                line.sharedMaterial = _ropeGradientMaterialInstance;
                _ropeGradientMaterialInstance.SetColor(CoreColorId, ropeCoreColor);
                _ropeGradientMaterialInstance.SetColor(EdgeColorId, ropeEdgeColor);
                _ropeGradientMaterialInstance.SetFloat(CoreWidthFractionId, Mathf.Clamp01(ropeCoreWidthFraction));
            }
            else
            {
                if (_ropeEffectsMaterial != null)
                {
                    line.sharedMaterial = _ropeEffectsMaterial;
                }
                line.startColor = ropeEdgeColor;
                line.endColor = ropeEdgeColor;
            }

            EnsureSparkPool(width);
            EnsureAnchorDrop(width);
        }

        private SpriteRenderer ResolveHeroSpriteRenderer()
        {
            var srs = GetComponentsInChildren<SpriteRenderer>(true);
            if (srs == null)
            {
                return null;
            }

            for (var i = 0; i < srs.Length; i++)
            {
                var sr = srs[i];
                if (sr == null)
                {
                    continue;
                }

                if (line != null)
                {
                    var lt = line.transform;
                    if (sr.transform == lt || sr.transform.IsChildOf(lt))
                    {
                        continue;
                    }
                }

                return sr;
            }

            return null;
        }

        private void EnsureRopeHandSprite()
        {
            if (_ropeHandPivotT != null)
            {
                return;
            }

            // Load sprite from Resources.
            if (_ropeHandSprite == null && !string.IsNullOrEmpty(ropeHandSpriteResourcesPath))
            {
                _ropeHandSprite = Resources.Load<Sprite>(ropeHandSpriteResourcesPath);
                if (_ropeHandSprite == null)
                {
                    var tex = Resources.Load<Texture2D>(ropeHandSpriteResourcesPath);
                    if (tex != null)
                    {
                        _ropeHandSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                            ropeHandPivotNormalized, Mathf.Max(1f, tex.height));
                    }
                }
            }

            if (_ropeHandSprite == null)
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

            var pivotGo = new GameObject("RopeHandPivot");
            pivotGo.transform.SetParent(parent, false);
            _ropeHandPivotT = pivotGo.transform;

            var spriteGo = new GameObject("RopeHandSprite");
            spriteGo.transform.SetParent(_ropeHandPivotT, false);
            _ropeHandSpriteT = spriteGo.transform;

            _ropeHandSr = spriteGo.AddComponent<SpriteRenderer>();
            _ropeHandSr.sprite = _ropeHandSprite;

            var heroSr = ResolveHeroSpriteRenderer();
            if (heroSr != null)
            {
                _ropeHandSr.sortingLayerID = heroSr.sortingLayerID;
                _ropeHandSr.sortingOrder = heroSr.sortingOrder + ropeHandSortingOrderOffset;
            }
            else
            {
                _ropeHandSr.sortingOrder = 40;
            }

            pivotGo.SetActive(false);
        }

        private void UpdateRopeHandTransform()
        {
            if (_ropeHandPivotT == null || _ropeHandSpriteT == null || _ropeHandSr == null)
            {
                return;
            }

            var shouldShow = _ropeHandVisible && _state == RopeState.Idle;
            _ropeHandPivotT.gameObject.SetActive(shouldShow);
            if (!shouldShow)
            {
                return;
            }

            var heroCol = GetComponent<Collider2D>();
            var heroH = 1f;
            var heroW = 1f;
            if (heroCol != null)
            {
                var b = heroCol.bounds;
                heroH = Mathf.Max(0.25f, b.size.y);
                heroW = Mathf.Max(0.25f, b.size.x);
            }

            var aimDir = Vector2.right;
            if (aim != null)
            {
                aimDir = aim.AimDirection;
            }

            var facingSign = 1f;
            var heroSr = ResolveHeroSpriteRenderer();
            if (heroSr != null)
            {
                facingSign = heroSr.flipX ? -1f : 1f;
            }

            var effectiveOffsetFraction = ropeHandOffsetFraction;
            var effectiveCenterOffsetPixels = ropeHandCenterOffsetPixels;
            var effectivePixelsPerUnit = Mathf.Max(0.01f, ropeHandPixelsPerUnit);
            var effectiveAimOffsetDeg = ropeHandAimAngleOffsetDeg;
            var effectiveTridentDownAngleDeg = ropeHandTridentDownAngleDeg;
            if (ropeHandUseSharedSettingsForAllHeroes && _sharedRopeHandSettingsInitialized)
            {
                effectiveOffsetFraction = _sharedRopeHandOffsetFraction;
                effectiveCenterOffsetPixels = _sharedRopeHandCenterOffsetPixels;
                effectivePixelsPerUnit = _sharedRopeHandPixelsPerUnit;
                effectiveAimOffsetDeg = _sharedRopeHandAimAngleOffsetDeg;
                effectiveTridentDownAngleDeg = _sharedRopeHandTridentDownAngleDeg;
            }

            // World offset from hero center: relative fraction + pixel tweak from inspector.
            var ppu = Mathf.Max(0.01f, effectivePixelsPerUnit);
            var pixelOffset = effectiveCenterOffsetPixels / ppu;
            var baseOffset = new Vector2(
                effectiveOffsetFraction.x * heroH * facingSign + pixelOffset.x * facingSign,
                effectiveOffsetFraction.y * heroH + pixelOffset.y);
            var world = (Vector2)transform.position + baseOffset;
            _ropeHandPivotT.position = new Vector3(world.x, world.y, 0f);

            // Keep the trident end pointing down regardless of facing direction.
            _ropeHandPivotT.rotation = Quaternion.Euler(0f, 0f, effectiveAimOffsetDeg + effectiveTridentDownAngleDeg);

            if (_ropeHandSr.sprite != null)
            {
                var desiredH = Mathf.Max(0.01f, heroH * Mathf.Max(0.01f, ropeHandHeightFraction));
                var spriteH = _ropeHandSr.sprite.bounds.size.y;
                var s = spriteH > 0.0001f ? desiredH / spriteH : 1f;

                var mirrorX = facingSign < 0f ? -1f : 1f;
                var baseY = ropeHandBaseMirrorY ? -1f : 1f;
                _ropeHandSpriteT.localScale = new Vector3(s * mirrorX, s * baseY, 1f);
            }
        }

        private LineRenderer CreateChildLineRenderer(string name, int sortingOffset, int positionCount, Material materialOverride)
        {
            var go = new GameObject(name);
            go.transform.SetParent(line.transform, false);

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.enabled = false;
            lr.positionCount = Mathf.Max(2, positionCount);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sortingLayerID = line.sortingLayerID;
            lr.sortingOrder = line.sortingOrder + sortingOffset;

            CopyLineRendererRenderingSettings(line, lr, materialOverride);
            return lr;
        }

        private static void CopyLineRendererRenderingSettings(LineRenderer src, LineRenderer dst, Material materialOverride)
        {
            if (src == null || dst == null)
            {
                return;
            }

            dst.sharedMaterial = materialOverride != null ? materialOverride : src.sharedMaterial;
            dst.textureMode = src.textureMode;
            dst.alignment = src.alignment;
            dst.numCapVertices = src.numCapVertices;
            dst.numCornerVertices = src.numCornerVertices;
            dst.sortingLayerID = src.sortingLayerID;
        }

        private void EnsureSparkPool(float ropeRenderWidth)
        {
            if (!enableRopeVisuals || !enableRopeSparks || line == null)
            {
                for (var i = 0; i < _sparkLines.Count; i++)
                {
                    if (_sparkLines[i] != null) _sparkLines[i].enabled = false;
                }
                return;
            }

            // Sparks should fill the full rope thickness and "overwrite" the gradient visually.
            // We use a dedicated material instance on the same shader but with no edge fade.
            if (_sparkFillMaterialInstance == null)
            {
                var shader = ropeGradientMaterial != null ? ropeGradientMaterial.shader : Shader.Find("WormCrawler/RopeRadialGradient");
                if (shader != null)
                {
                    _sparkFillMaterialInstance = ropeGradientMaterial != null ? new Material(ropeGradientMaterial) : new Material(shader);
                    _sparkFillMaterialInstance.SetColor(CoreColorId, Color.white);
                    _sparkFillMaterialInstance.SetColor(EdgeColorId, Color.white);
                    _sparkFillMaterialInstance.SetFloat(CoreWidthFractionId, 1f);
                }
            }

            var desired = Mathf.Clamp(maxSparks, 0, 32);
            while (_sparkLines.Count < desired)
            {
                var lr = CreateChildLineRenderer($"RopeSpark{_sparkLines.Count}", sortingOffset: 3, positionCount: 5, materialOverride: _sparkFillMaterialInstance != null ? _sparkFillMaterialInstance : _ropeEffectsMaterial);
                lr.startColor = sparkColor;
                lr.endColor = sparkColor;
                lr.startWidth = ropeRenderWidth * 1.08f;
                lr.endWidth = ropeRenderWidth * 1.08f;
                _sparkLines.Add(lr);
            }

            while (_sparkLines.Count > desired)
            {
                var last = _sparkLines.Count - 1;
                var lr = _sparkLines[last];
                _sparkLines.RemoveAt(last);
                if (lr != null)
                {
                    Destroy(lr.gameObject);
                }
            }

            // Refresh existing sparks to ensure they keep the "bubble" look.
            var mat = _sparkFillMaterialInstance != null ? _sparkFillMaterialInstance : _ropeEffectsMaterial;
            var w = ropeRenderWidth * 1.08f;
            for (var i = 0; i < _sparkLines.Count; i++)
            {
                var lr = _sparkLines[i];
                if (lr == null) continue;
                if (lr.positionCount != 5) lr.positionCount = 5;
                if (mat != null) lr.sharedMaterial = mat;
                lr.startColor = sparkColor;
                lr.endColor = sparkColor;
                lr.startWidth = w;
                lr.endWidth = w;
            }
        }

        private void EnsureAnchorDrop(float ropeRenderWidth)
        {
            if (!enableRopeVisuals || !enableAnchorDrop || line == null)
            {
                if (_anchorDropLines != null)
                {
                    for (var i = 0; i < _anchorDropLines.Length; i++)
                    {
                        if (_anchorDropLines[i] != null) _anchorDropLines[i].enabled = false;
                    }
                }
                return;
            }

            var strands = Mathf.Clamp(anchorDropStrands, 3, 3);
            if (strands <= 0)
            {
                return;
            }

            if (_anchorDropRoot == null)
            {
                var go = new GameObject("AnchorDrop");
                go.transform.SetParent(line.transform, false);
                _anchorDropRoot = go.transform;
            }

            if (_anchorDropLines == null || _anchorDropLines.Length != strands)
            {
                if (_anchorDropLines != null)
                {
                    for (var i = 0; i < _anchorDropLines.Length; i++)
                    {
                        if (_anchorDropLines[i] != null)
                        {
                            Destroy(_anchorDropLines[i].gameObject);
                        }
                    }
                }

                _anchorDropLines = new LineRenderer[strands];
                _anchorDropPhase = new float[strands];
                for (var i = 0; i < strands; i++)
                {
                    var go = new GameObject($"Strand{i}");
                    go.transform.SetParent(_anchorDropRoot, false);
                    var lr = go.AddComponent<LineRenderer>();
                    lr.useWorldSpace = true;
                    lr.enabled = false;
                    lr.positionCount = 9;
                    lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    lr.receiveShadows = false;
                    lr.sortingOrder = line.sortingOrder + 2;
                    CopyLineRendererRenderingSettings(line, lr, _ropeEffectsMaterial);

                    lr.startColor = sparkColor;
                    lr.endColor = sparkColor;

                    _anchorDropPhase[i] = UnityEngine.Random.value * 10f;
                    _anchorDropLines[i] = lr;
                }
            }

            var heroH = 1.0f;
            if (_heroCol != null)
            {
                heroH = Mathf.Max(0.25f, _heroCol.bounds.size.y);
            }
            var anchorScale = 4f / 3f;
            var headHalf = Mathf.Max(0.05f, heroH * Mathf.Max(0.01f, anchorHeadHalfFractionOfHeroHeight)) * anchorScale;
            var targetEndWidth = Mathf.Max(ropeRenderWidth * Mathf.Max(1.0f, anchorDropWidthMultiplier) * anchorScale, headHalf);

            for (var i = 0; i < _anchorDropLines.Length; i++)
            {
                var lr = _anchorDropLines[i];
                if (lr == null) continue;
                if (lr.positionCount != 9) lr.positionCount = 9;
                lr.startColor = sparkColor;
                lr.endColor = sparkColor;
                lr.startWidth = targetEndWidth * 0.25f;
                lr.endWidth = targetEndWidth;
            }
        }

        private void OnDestroy()
        {
            _shoot?.Disable();
            _shoot?.Dispose();
            _moveH?.Disable();
            _moveH?.Dispose();
            _moveV?.Disable();
            _moveV?.Dispose();

            if (_ropeGradientMaterialInstance != null)
            {
                Destroy(_ropeGradientMaterialInstance);
                _ropeGradientMaterialInstance = null;
            }

            if (_sparkFillMaterialInstance != null)
            {
                Destroy(_sparkFillMaterialInstance);
                _sparkFillMaterialInstance = null;
            }
        }

        private void Update()
        {
            if (InputEnabled && _shoot != null && _shoot.WasPressedThisFrame())
            {
                if (_state != RopeState.Idle)
                {
                    Detach();
                }
                else
                {
                    StartShot();
                }
            }

            if (_state == RopeState.Shooting)
            {
                _shotT += Time.deltaTime / Mathf.Max(0.0001f, shotTravelTime);
                if (_shotT >= 1f)
                {
                    _shotT = 1f;
                    if (_shotHasHit)
                    {
                        FinishAttachFromShot();
                    }
                    else
                    {
                        _state = RopeState.Retracting;
                        _shotT = 0f;
                    }
                }

                UpdateShotLine(_shotT);
            }
            else if (_state == RopeState.Retracting)
            {
                _shotT += Time.deltaTime / Mathf.Max(0.0001f, missRetractTime);
                var t = Mathf.Clamp01(_shotT);
                UpdateShotLine(1f - t);
                if (t >= 1f)
                {
                    line.enabled = false;
                    _state = RopeState.Idle;
                }
            }

            EnsureRopeHandSprite();
            UpdateRopeHandTransform();
        }

        private void FixedUpdate()
        {
            if (_state != RopeState.Attached)
            {
                return;
            }

            UpdateFacingSign();

            var vInput = Mathf.Clamp(ReadVInput(), -1f, 1f);

            if (animator != null)
            {
                animator.SetBool(IsClimbingHash, Mathf.Abs(vInput) > 0.05f);
                animator.SetFloat(ClimbDirHash, vInput);
            }
            if (!_stiffPhaseActive && _stiffPhaseAvailable && _hasStiffLine && Mathf.Abs(vInput) > 0.001f)
            {
                var swingAnchor = GetSwingAnchor();
                var p = GetBodyAnchorWorld();
                var radial = p - swingAnchor;
                if (radial.sqrMagnitude > 0.0001f)
                {
                    var radialN = radial.normalized;
                    var isLowerSemicircle = Vector2.Dot(radialN, Vector2.down) > 0.001f;
                    if (isLowerSemicircle)
                    {
                        _stiffPhaseAvailable = false;
                    }
                    else
                    {
                        _stiffPhaseActive = true;
                    }
                }
                else
                {
                    _stiffPhaseActive = true;
                }
            }
            if (_stiffPhaseActive && Mathf.Abs(vInput) <= 0.001f)
            {
                _stiffPhaseActive = false;
                _stiffPhaseAvailable = false;
            }

            if (_rb != null && _gravityScaleSaved)
            {
                _rb.gravityScale = _stiffPhaseActive ? (_savedGravityScale * reelInGravityScaleFactor) : _savedGravityScale;
            }

            if (_rb != null)
            {
                _rb.WakeUp();
            }
            UpdateJointAnchor();
            if (!_stiffPhaseActive)
            {
                ApplyUpwardGravityPenalty();
            }
            UpdateRopeWrap();
            if (!_stiffPhaseActive)
            {
                ApplyTangentialGravityAssist();
            }
            UpdateRopeLength();
            if (_stiffPhaseActive)
            {
                ApplyStiffLineMotion(vInput);
            }
            else
            {
                ApplySwingForces();
            }
            UpdateLine();
        }

        private void UpdateFacingSign()
        {
            var h = ReadHInput();
            if (Mathf.Abs(h) > 0.15f)
            {
                _lastFacingSign = h > 0f ? 1 : -1;
            }
        }

        private void ApplyStiffLineMotion(float input)
        {
            if (_rb == null) return;
            if (!_hasStiffLine) return;
            if (_stiffLineDir.sqrMagnitude < 0.0001f) return;

            var dir = _stiffLineDir;
            var n = new Vector2(-dir.y, dir.x);

            var anchor = GetSwingAnchor();
            var dist = _joint != null ? _joint.distance : 0f;
            var targetPos = anchor - dir * dist;

            var bodyAnchor = GetBodyAnchorWorld();
            var delta = targetPos - bodyAnchor;

            var k = 1f;
            if (stiffLineSpring > 0f)
            {
                k = 1f - Mathf.Exp(-stiffLineSpring * Time.fixedDeltaTime);
                k = Mathf.Clamp01(k);
            }

            var desiredRbPos = _rb.position + delta;
            var nextRbPos = Vector2.Lerp(_rb.position, desiredRbPos, k);
            if (stiffLineMaxStep > 0f)
            {
                var step = nextRbPos - _rb.position;
                if (step.sqrMagnitude > stiffLineMaxStep * stiffLineMaxStep)
                {
                    nextRbPos = _rb.position + step.normalized * stiffLineMaxStep;
                }
            }
            _rb.MovePosition(nextRbPos);

            if (stiffLineDamping > 0f)
            {
                var v = GetVelocity();
                var vPerp = Vector2.Dot(v, n);
                _rb.AddForce(-n * (vPerp * stiffLineDamping), ForceMode2D.Force);
            }
        }

        private void ApplyTangentialGravityAssist()
        {
            if (_rb == null) return;
            if (ropeGravityAssist <= 0f) return;

            var swingAnchor = GetSwingAnchor();
            var p = GetBodyAnchorWorld();
            var radial = p - swingAnchor;
            if (radial.sqrMagnitude < 0.0001f) return;

            var radialN = radial.normalized;
            var angleFromDown = Mathf.Acos(Mathf.Clamp(Vector2.Dot(radialN, Vector2.down), -1f, 1f)) * Mathf.Rad2Deg;
            var angleFromVertical = Mathf.Acos(Mathf.Clamp(Mathf.Abs(Vector2.Dot(radialN, Vector2.down)), -1f, 1f)) * Mathf.Rad2Deg;
            var isHeroAboveAnchor = Vector2.Dot(radialN, Vector2.down) < 0f;

            var tangentRight = new Vector2(radialN.y, -radialN.x);
            if (tangentRight.x < 0f)
            {
                tangentRight = -tangentRight;
            }
            var pv0 = _rb.GetPointVelocity(p);
            var vt0 = Vector2.Dot(pv0, tangentRight);

            var stableAngleDeg = ropeGravityAssistMinAngleDeg;
            if (!isHeroAboveAnchor && maxRopeLength > 0.0001f)
            {
                var shortFrac = Mathf.Clamp01(ropeGravityAssistMinAngleShortFraction);
                var ropeFrac = Mathf.Clamp01(_totalRopeLength / maxRopeLength);
                if (ropeFrac <= shortFrac)
                {
                    stableAngleDeg = ropeGravityAssistMinAngleDegShort;
                }
                else
                {
                    var t = Mathf.InverseLerp(shortFrac, 1f, ropeFrac);
                    stableAngleDeg = Mathf.Lerp(ropeGravityAssistMinAngleDegShort, ropeGravityAssistMinAngleDeg, t);
                }
            }

            if (angleFromVertical < stableAngleDeg)
            {
                if (verticalFallBiasFraction > 0f)
                {
                    var h = ReadHInput();
                    var desiredH = Mathf.Abs(h) > 0.15f ? Mathf.Sign(h) : 0f;
                    if (Mathf.Abs(desiredH) > 0.001f)
                    {
                        if (verticalFallBiasKickSpeed > 0f)
                        {
                            var v = GetVelocity();
                            var vT = Vector2.Dot(v, tangentRight);
                            var targetVT = desiredH * verticalFallBiasKickSpeed;
                            if (Mathf.Abs(vT) < Mathf.Abs(targetVT))
                            {
#if UNITY_6000_0_OR_NEWER
                                _rb.linearVelocity = v + tangentRight * (targetVT - vT);
#else
                                _rb.velocity = v + tangentRight * (targetVT - vT);
#endif
                            }
                        }

                        if (verticalFallBiasTangentDamping > 0f)
                        {
                            _rb.AddForce(-tangentRight * (vt0 * verticalFallBiasTangentDamping), ForceMode2D.Force);
                        }

                        var g0 = Physics2D.gravity * _rb.gravityScale;
                        var gMag0 = g0.magnitude;
                        if (gMag0 > 0.000001f)
                        {
                            var f = (gMag0 * _rb.mass) * ropeGravityAssist * verticalFallBiasFraction;
                            _rb.AddForce(tangentRight * desiredH * f, ForceMode2D.Force);
                        }
                    }
                }
                return;
            }

            if (ropeGravityAssistMaxTangentSpeed > 0f && Mathf.Abs(vt0) > ropeGravityAssistMaxTangentSpeed) return;

            var g = Physics2D.gravity * _rb.gravityScale;
            var gMag = g.magnitude;
            if (gMag < 0.000001f) return;

            var gt = g - Vector2.Dot(g, radialN) * radialN;
            var gtMag = gt.magnitude;

            var desiredTangentMag = gMag;
            var extraTangentMag = Mathf.Max(0f, desiredTangentMag - gtMag);
            if (extraTangentMag <= 0.000001f) return;

            var hInput = ReadHInput();
            var desiredH2 = Mathf.Abs(hInput) > 0.15f ? Mathf.Sign(hInput) : -_lastFacingSign;
            var sign = Mathf.Sign(Vector2.Dot(g, tangentRight));

            if (isHeroAboveAnchor)
            {
                var sideX = p.x - swingAnchor.x;
                if (Mathf.Abs(sideX) > 0.01f)
                {
                    sign = Mathf.Sign(sideX);
                }
                else if (Mathf.Abs(hInput) > 0.15f)
                {
                    sign = Mathf.Sign(hInput);
                }
            }
            else if (verticalFallBiasControlAngleDeg > 0f && angleFromVertical < verticalFallBiasControlAngleDeg)
            {
                if (Mathf.Abs(desiredH2) > 0.001f) sign = desiredH2;
            }

            if (Mathf.Abs(sign) < 0.001f)
            {
                if (Mathf.Abs(desiredH2) > 0.001f) sign = desiredH2;
                if (Mathf.Abs(sign) < 0.001f) sign = 1f;
            }
            var dirForce = tangentRight * sign;
            _rb.AddForce(dirForce * (extraTangentMag * _rb.mass * ropeGravityAssist), ForceMode2D.Force);
        }

        private void ApplyUpwardGravityPenalty()
        {
            if (_rb == null) return;

            var v = GetVelocity();
            if (v.y <= 0.01f) return;

            _rb.AddForce(Vector2.down * (v.y * ropeUpwardGravityFactor), ForceMode2D.Force);
        }

        private void StartShot()
        {
            if (animator != null)
            {
                animator.SetTrigger(ShootHash);
            }
            if (aim == null)
            {
                aim = GetComponent<WormAimController>();
            }

            var dir = aim != null ? aim.AimDirection : Vector2.right;
            if (dir.sqrMagnitude < 0.0001f)
            {
                dir = Vector2.right;
            }

            var stiffOrigin = GetRopeOriginWorld();
            var stiffDir = dir;
            if (aim != null)
            {
                var d = aim.AimWorldPoint - stiffOrigin;
                if (d.sqrMagnitude > 0.0001f)
                {
                    stiffDir = d.normalized;
                }
            }
            _stiffLineDir = stiffDir.normalized;
            _hasStiffLine = _stiffLineDir.sqrMagnitude > 0.0001f;

            var origin = GetRopeOriginWorld();
            var hit = RaycastFirstValid(origin, dir, maxDistance);

            _shotHasHit = hit.collider != null;
            if (_shotHasHit)
            {
                _shotHitNormal = hit.normal.sqrMagnitude > 0.0001f ? hit.normal.normalized : Vector2.zero;
                _shotTarget = hit.point + (hit.normal.sqrMagnitude > 0.0001f ? hit.normal.normalized * anchorSurfaceOffset : Vector2.zero);
            }
            else
            {
                _shotHitNormal = Vector2.zero;
                _shotTarget = origin + dir.normalized * maxDistance;
            }

            _shotT = 0f;
            _state = RopeState.Shooting;

            line.enabled = true;
            line.positionCount = 2;
            UpdateShotLine(0f);
        }

        private void FinishAttachFromShot()
        {
            var heroPos = GetRopeOriginWorld();

            _anchorWorld = _shotTarget;
            _anchorNormalWorld = _shotHitNormal;
            _hasAnchorNormal = _anchorNormalWorld.sqrMagnitude > 0.0001f;
            _pivots.Clear();

            var initial = Vector2.Distance(_anchorWorld, heroPos);
            _totalRopeLength = Mathf.Clamp(initial, minDistance, maxRopeLength);

            _joint.enabled = true;
            _joint.connectedBody = null;
            _joint.connectedAnchor = _anchorWorld;
            _joint.distance = Mathf.Clamp(_totalRopeLength, minDistance, maxRopeLength);
            UpdateJointAnchor();

            if (_rb != null && !_sleepModeSaved)
            {
                _savedSleepMode = _rb.sleepMode;
                _sleepModeSaved = true;
            }
            if (_rb != null)
            {
                _rb.sleepMode = RigidbodySleepMode2D.NeverSleep;
            }

            if (_rb != null)
            {
                _savedInterpolation = _rb.interpolation;
                _interpolationSaved = true;
                _rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            }

            if (_rb != null && !_gravityScaleSaved)
            {
                _savedGravityScale = _rb.gravityScale;
                _gravityScaleSaved = true;
            }
            _state = RopeState.Attached;
            _hasStiffLine = _stiffLineDir.sqrMagnitude > 0.0001f;
            _stiffPhaseAvailable = _hasStiffLine;
            _stiffPhaseActive = false;
            UpdateJointAnchor();
        }

        private Vector2 GetRenderAnchorWorld()
        {
            if (!_hasAnchorNormal || _heroCol == null)
            {
                return _anchorWorld;
            }

            var heroH = Mathf.Max(0.25f, _heroCol.bounds.size.y);
            var inset = Mathf.Max(0f, anchorRenderInsetFractionOfHeroHeight) * heroH;
            return _anchorWorld - _anchorNormalWorld * inset;
        }

        private void UpdateJointAnchor()
        {
            if (_joint == null || !_joint.enabled)
            {
                return;
            }

            var origin = GetRopeOriginWorld();
            _joint.anchor = transform.InverseTransformPoint(new Vector3(origin.x, origin.y, transform.position.z));
        }

        private void Detach()
        {
            _state = RopeState.Idle;
            _pivots.Clear();
            _joint.enabled = false;
            line.enabled = false;

            if (_rb != null && _interpolationSaved)
            {
                _rb.interpolation = _savedInterpolation;
                _interpolationSaved = false;
            }
            for (var i = 0; i < _sparkLines.Count; i++)
            {
                if (_sparkLines[i] != null)
                {
                    _sparkLines[i].enabled = false;
                }
            }
            if (_anchorDropLines != null)
            {
                for (var i = 0; i < _anchorDropLines.Length; i++)
                {
                    if (_anchorDropLines[i] != null)
                    {
                        _anchorDropLines[i].enabled = false;
                    }
                }
            }

            if (animator != null)
            {
                animator.SetBool(IsClimbingHash, false);
                animator.SetFloat(ClimbDirHash, 0f);
            }

            _hasStiffLine = false;
            _stiffPhaseAvailable = false;
            _stiffPhaseActive = false;

            if (_rb != null && _sleepModeSaved)
            {
                _rb.sleepMode = _savedSleepMode;
            }

            if (_rb != null)
            {
                if (_gravityScaleSaved)
                {
                    _rb.gravityScale = _savedGravityScale;
                }

                if (_rb.gravityScale <= 0.0001f)
                {
                    _rb.gravityScale = 9f;
                }

                _rb.sleepMode = RigidbodySleepMode2D.NeverSleep;

                _rb.WakeUp();
            }
        }

        private Vector2 GetSwingAnchor()
        {
            return _pivots.Count > 0 ? _pivots[_pivots.Count - 1] : _anchorWorld;
        }

        private Vector2 GetPrevAnchorForUnwrap()
        {
            if (_pivots.Count >= 2) return _pivots[_pivots.Count - 2];
            return _anchorWorld;
        }

        private float ComputePathLengthToSwing()
        {
            if (_pivots.Count == 0) return 0f;

            var len = 0f;
            var prev = _anchorWorld;
            for (var i = 0; i < _pivots.Count; i++)
            {
                len += Vector2.Distance(prev, _pivots[i]);
                prev = _pivots[i];
            }

            return len;
        }

        private void UpdateRopeLength()
        {
            var v = Mathf.Clamp(ReadVInput(), -1f, 1f);
            if (Mathf.Abs(v) > 0.001f)
            {
                _totalRopeLength = Mathf.Clamp(_totalRopeLength - v * (reelSpeed * Time.fixedDeltaTime), minDistance, maxRopeLength);
            }

            var pathToSwing = ComputePathLengthToSwing();
            var remaining = _totalRopeLength - pathToSwing;
            var maxSeg = Mathf.Min(maxDistance, maxRopeLength);
            remaining = Mathf.Clamp(remaining, minSegment, maxSeg);

            _joint.connectedAnchor = GetSwingAnchor();
            _joint.distance = remaining;
        }

        private void ApplySwingForces()
        {
            var swingAnchor = GetSwingAnchor();
            var p = GetBodyAnchorWorld();
            var radial = p - swingAnchor;
            if (radial.sqrMagnitude < 0.0001f)
            {
                return;
            }

            radial.Normalize();
            var tangentRight = new Vector2(radial.y, -radial.x);
            if (tangentRight.x < 0f)
            {
                tangentRight = -tangentRight;
            }

            var h = Mathf.Clamp(ReadHInput(), -1f, 1f);
            if (Mathf.Abs(h) > 0.001f)
            {
                _rb.AddForce(tangentRight * (h * swingForce), ForceMode2D.Force);
            }
            else
            {
                var v = _rb != null ? _rb.GetPointVelocity(p) : GetVelocity();
                var vt = Vector2.Dot(v, tangentRight);
                _rb.AddForce(-tangentRight * (vt * noInputRopeDamping), ForceMode2D.Force);
            }
        }

        private Vector2 GetBodyAnchorWorld()
        {
            if (_joint != null && _joint.enabled)
            {
                var wp = transform.TransformPoint(_joint.anchor);
                return new Vector2(wp.x, wp.y);
            }

            return _rb != null ? _rb.position : (Vector2)transform.position;
        }

        private Vector2 GetVelocity()
        {
#if UNITY_6000_0_OR_NEWER
            return _rb.linearVelocity;
#else
            return _rb.velocity;
#endif
        }

        private void UpdateRopeWrap()
        {
            var heroPos = GetRopeOriginWorld();
            var swingAnchor = GetSwingAnchor();
            var toSwing = swingAnchor - heroPos;
            var segLen = toSwing.magnitude;
            if (segLen < 0.0001f)
            {
                return;
            }

            var dir = toSwing / segLen;
            var hit = RaycastFirstValid(heroPos, dir, segLen);
            if (hit.collider != null && hit.distance < segLen - 0.01f)
            {
                var pivot = hit.point + (hit.normal.sqrMagnitude > 0.0001f ? hit.normal.normalized * anchorSurfaceOffset : Vector2.zero);

                var pathToSwing = ComputePathLengthToSwing();
                var candidatePath = pathToSwing + Vector2.Distance(swingAnchor, pivot);
                if (candidatePath < _totalRopeLength - minSegment)
                {
                    if (_pivots.Count > 0 && Vector2.Distance(_pivots[_pivots.Count - 1], pivot) < 0.02f)
                    {
                        _pivots[_pivots.Count - 1] = pivot;
                    }
                    else
                    {
                        _pivots.Add(pivot);
                    }
                }
            }

            if (_pivots.Count > 0)
            {
                var prevAnchor = GetPrevAnchorForUnwrap();
                var toPrev = prevAnchor - heroPos;
                var prevLen = toPrev.magnitude;
                if (prevLen > 0.0001f)
                {
                    var prevDir = toPrev / prevLen;
                    var unwrapHit = RaycastFirstValid(heroPos, prevDir, prevLen);
                    if (unwrapHit.collider == null)
                    {
                        _pivots.RemoveAt(_pivots.Count - 1);
                    }
                }
            }
        }

        private void UpdateLine()
        {
            if (!line.enabled)
            {
                line.enabled = true;
            }

            var hero = GetRopeOriginWorld();
            var count = 2 + _pivots.Count;
            if (line.positionCount != count)
            {
                line.positionCount = count;
            }

            line.SetPosition(0, hero);

            var idx = 1;
            for (var i = _pivots.Count - 1; i >= 0; i--)
            {
                line.SetPosition(idx++, _pivots[i]);
            }

            line.SetPosition(idx, GetRenderAnchorWorld());

            UpdateRopeVfx();
        }

        private void UpdateShotLine(float t)
        {
            var hero = GetRopeOriginWorld();
            var p = Vector2.Lerp(hero, _shotTarget, Mathf.Clamp01(t));
            line.SetPosition(0, hero);
            line.SetPosition(1, p);

            UpdateRopeVfx();
        }

        private void UpdateRopeVfx()
        {
            if (!enableRopeVisuals || line == null || !line.enabled)
            {
                return;
            }

            if (_ropeBuffer == null || _ropeBuffer.Length < line.positionCount)
            {
                _ropeBuffer = new Vector3[Mathf.Max(16, line.positionCount)];
            }

            var count = line.GetPositions(_ropeBuffer);
            if (count < 2)
            {
                return;
            }

            var ropeLen = 0f;
            for (var i = 1; i < count; i++)
            {
                ropeLen += Vector2.Distance(_ropeBuffer[i - 1], _ropeBuffer[i]);
            }

            UpdateAnchorDrop(_ropeBuffer, count);
            UpdateSparks(_ropeBuffer, count, ropeLen);
        }

        private void UpdateSparks(Vector3[] pts, int count, float ropeLen)
        {
            if (!enableRopeSparks || _sparkLines.Count == 0)
            {
                return;
            }

            if (ropeLen < 0.05f)
            {
                for (var i = 0; i < _sparkLines.Count; i++)
                {
                    if (_sparkLines[i] != null) _sparkLines[i].enabled = false;
                }
                return;
            }

            var heroH = 1.0f;
            if (_heroCol != null)
            {
                heroH = Mathf.Max(0.25f, _heroCol.bounds.size.y);
            }

            var spacing = Mathf.Max(0.25f, heroH * Mathf.Max(0.1f, sparkSpacingHeroHeights));
            var desired = Mathf.Clamp(Mathf.FloorToInt(ropeLen / spacing), 0, _sparkLines.Count);
            var speed = Mathf.Max(0.01f, sparkSpeed);
            _sparkTime += Time.deltaTime;

            for (var i = 0; i < _sparkLines.Count; i++)
            {
                var lr = _sparkLines[i];
                if (lr == null)
                {
                    continue;
                }

                if (i >= desired)
                {
                    lr.enabled = false;
                    continue;
                }

                var dist = (_sparkTime * speed + i * spacing) % ropeLen;
                GetPointAndTangentAtDistance(pts, count, dist, out var p, out var t);
                if (t.sqrMagnitude < 0.0001f)
                {
                    lr.enabled = false;
                    continue;
                }

                if (!lr.enabled) lr.enabled = true;

                var tangent = t.normalized;
                var perp = new Vector2(-tangent.y, tangent.x);
                var len = Mathf.Max(0.05f, sparkSegmentLength);
                var amp = Mathf.Max(0.001f, sparkAmplitude);

                var p0 = p - tangent * (len * 0.5f);
                var p1 = p - tangent * (len * 0.25f) + perp * amp;
                var p2 = p;
                var p3 = p + tangent * (len * 0.25f) - perp * amp;
                var p4 = p + tangent * (len * 0.5f);

                lr.SetPosition(0, p0);
                lr.SetPosition(1, p1);
                lr.SetPosition(2, p2);
                lr.SetPosition(3, p3);
                lr.SetPosition(4, p4);
            }
        }

        private void UpdateAnchorDrop(Vector3[] pts, int count)
        {
            if (!enableAnchorDrop || _anchorDropLines == null || _anchorDropLines.Length == 0)
            {
                return;
            }

            var anchor = GetRenderAnchorWorld();
            var prev = (Vector2)pts[count - 2];

            var intoSurface = Vector2.zero;
            if (_hasAnchorNormal)
            {
                intoSurface = -_anchorNormalWorld;
            }
            if (intoSurface.sqrMagnitude < 0.0001f)
            {
                intoSurface = (prev - anchor);
            }
            if (intoSurface.sqrMagnitude < 0.0001f)
            {
                for (var i = 0; i < _anchorDropLines.Length; i++)
                {
                    if (_anchorDropLines[i] != null) _anchorDropLines[i].enabled = false;
                }
                return;
            }

            intoSurface.Normalize();
            var perp = new Vector2(-intoSurface.y, intoSurface.x);

            var heroH = 1.0f;
            if (_heroCol != null)
            {
                heroH = Mathf.Max(0.25f, _heroCol.bounds.size.y);
            }
            var anchorScale = 4f / 3f;
            var headHalf = Mathf.Max(0.05f, heroH * Mathf.Max(0.01f, anchorHeadHalfFractionOfHeroHeight)) * anchorScale;
            var lenBase = Mathf.Max(headHalf, Mathf.Max(0.05f, anchorDropLength)) * anchorScale;
            var spread = Mathf.Max(0f, anchorDropSpread) * anchorScale;
            var amp = Mathf.Max(0.001f, lenBase * Mathf.Max(0.0f, anchorLightningAmplitudeFraction));

            for (var i = 0; i < _anchorDropLines.Length; i++)
            {
                var lr = _anchorDropLines[i];
                if (lr == null)
                {
                    continue;
                }

                if (!lr.enabled) lr.enabled = true;
                if (lr.positionCount != 9) lr.positionCount = 9;

                var phase = _anchorDropPhase != null && i < _anchorDropPhase.Length ? _anchorDropPhase[i] : 0f;
                var prongIndex = i - 1;
                if (_anchorDropLines.Length == 1) prongIndex = 0;
                else if (_anchorDropLines.Length == 2) prongIndex = i == 0 ? 0 : 1;
                else if (_anchorDropLines.Length >= 3) prongIndex = Mathf.Clamp(prongIndex, -1, 1);

                var prongLen = prongIndex == 0 ? lenBase : (lenBase * 0.70f);
                var prongSide = perp * (prongIndex * spread);
                var prongDir = (intoSurface + perp * (prongIndex * 0.22f)).normalized;
                var prongPerp = new Vector2(-prongDir.y, prongDir.x);

                var time = Time.time;
                var freqA = 10.0f;
                var freqB = 17.0f;

                var segCount = lr.positionCount;
                for (var si = 0; si < segCount; si++)
                {
                    var u = 1f - (si / Mathf.Max(1f, segCount - 1));
                    var bendWeight = Mathf.Pow(u, 1.35f);

                    var wA = Mathf.Sin(time * freqA + phase + u * 6.0f);
                    var wB = Mathf.Sin(time * freqB + phase * 1.7f + u * 9.0f);
                    var n = Mathf.PerlinNoise((phase + time * 0.55f) * 0.35f, (u + prongIndex * 0.37f) * 3.1f) * 2f - 1f;
                    var wave = (wA * 0.65f) + (wB * 0.35f) + (n * 0.35f);

                    var bend = prongPerp * (amp * 0.55f * bendWeight * wave);
                    bend += perp * (amp * 0.18f * bendWeight * Mathf.Sin(time * 23.0f + phase + u * 12.0f));

                    var p = anchor + prongDir * (prongLen * u) + prongSide * u + bend;
                    lr.SetPosition(si, p);
                }
            }
        }

        private static void GetPointAndTangentAtDistance(Vector3[] pts, int count, float dist, out Vector2 p, out Vector2 tangent)
        {
            var remaining = Mathf.Max(0f, dist);
            for (var i = 1; i < count; i++)
            {
                var a = (Vector2)pts[i - 1];
                var b = (Vector2)pts[i];
                var seg = b - a;
                var len = seg.magnitude;
                if (len < 0.0001f)
                {
                    continue;
                }

                if (remaining <= len)
                {
                    var t = remaining / len;
                    p = Vector2.Lerp(a, b, t);
                    tangent = seg / len;
                    return;
                }
                remaining -= len;
            }

            p = (Vector2)pts[count - 1];
            tangent = ((Vector2)pts[count - 1] - (Vector2)pts[count - 2]).normalized;
        }

        private Vector2 GetRopeOriginWorld()
        {
            if (aim != null)
            {
                return aim.AimOriginWorld;
            }
            return _rb != null ? _rb.position : (Vector2)transform.position;
        }

        private float ReadHInput()
        {
            if (_externalMoveOverride) return _externalMoveH;
            if (!InputEnabled) return 0f;
            var h = 0f;
            if (_moveH != null)
            {
                h = _moveH.ReadValue<float>();
            }
            if (_additionalMoveInput)
            {
                h = _additionalMoveH;
            }
            return Mathf.Clamp(h, -1f, 1f);
        }

        private float ReadVInput()
        {
            if (_externalMoveOverride) return _externalMoveV;
            if (!InputEnabled) return 0f;
            var v = 0f;
            if (_moveV != null)
            {
                v = _moveV.ReadValue<float>();
            }
            if (_additionalMoveInput)
            {
                v = _additionalMoveV;
            }
            return Mathf.Clamp(v, -1f, 1f);
        }

        private RaycastHit2D RaycastFirstValid(Vector2 origin, Vector2 dir, float distance)
        {
            var best = default(RaycastHit2D);
            var bestDist = float.MaxValue;

            var hitCount = Physics2D.Raycast(origin, dir, _rayFilter, _rayHits, distance);
            for (var i = 0; i < hitCount; i++)
            {
                var h = _rayHits[i];
                if (h.collider == null) continue;
                if (!IsValidRopeCollider(h.collider)) continue;

                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    best = h;
                }
            }

            return best;
        }

        private bool IsValidRopeCollider(Collider2D col)
        {
            if (col == null) return false;
            if (col.isTrigger) return false;
            if (col.transform == transform || col.transform.IsChildOf(transform)) return false;

            if (IsTilemapCollider(col)) return false;
            if (!IsTerrainCollider(col)) return false;

            return true;
        }

        private static bool IsTilemapCollider(Component c)
        {
            if (c == null) return false;

            if (s_TilemapCollider2DType == null)
            {
                s_TilemapCollider2DType = Type.GetType("UnityEngine.Tilemaps.TilemapCollider2D, UnityEngine.TilemapModule");
                s_TilemapType = Type.GetType("UnityEngine.Tilemaps.Tilemap, UnityEngine.TilemapModule");
                s_TilemapRendererType = Type.GetType("UnityEngine.Tilemaps.TilemapRenderer, UnityEngine.TilemapModule");
            }

            if (s_TilemapCollider2DType != null && c.GetComponentInParent(s_TilemapCollider2DType) != null)
            {
                return true;
            }
            if (s_TilemapType != null && c.GetComponentInParent(s_TilemapType) != null)
            {
                return true;
            }
            if (s_TilemapRendererType != null && c.GetComponentInParent(s_TilemapRendererType) != null)
            {
                return true;
            }

            return false;
        }

        private static bool IsTerrainCollider(Collider2D col)
        {
            if (col == null) return false;

            if (col.GetComponentInParent<WorldDecoration>() != null)
            {
                return true;
            }

            var obstacleLayer = LayerMask.NameToLayer("TerrainObstacle");
            if (obstacleLayer >= 0 && col.gameObject.layer == obstacleLayer)
            {
                return true;
            }

            var cur = col.transform;
            while (cur != null)
            {
                if (cur.name == "GroundPoly" || cur.name == "Islands")
                {
                    return true;
                }

                cur = cur.parent;
            }

            return false;
        }
    }
}
