using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace WormCrawlerPrototype
{
    public sealed class Bootstrap : MonoBehaviour
    {
        public static bool IsMapMenuOpen { get; private set; }
        public static int SelectedTeamSize { get; private set; } = 3;

        public static bool VsCpu { get; private set; }
        public static WormCrawlerPrototype.AI.BotDifficulty CpuDifficulty { get; private set; } = WormCrawlerPrototype.AI.BotDifficulty.Normal;

        private static GUIStyle _pressedButtonStyle;

        private const string TerrainPngPathPrefKey = "WormCrawler_SelectedTerrain";
        private const string TeamSizePrefKey = "WormCrawler_SelectedTeamSize";
        private const string VsCpuPrefKey = "WormCrawler_VsCpu";
        private const string CpuDifficultyPrefKey = "WormCrawler_CpuDifficulty";
        private const string LevelsResourcesRoot = "Levels";

        private SimpleWorldGenerator _generator;
        private Transform _hero;
        private Camera _cam;
        private bool _generatedOnce;
        private bool _followHero = true;
        private float _cameraPanSpeed = 18f;
        private float _cameraZoomSpeed = 6f;
        [SerializeField] private float _cameraFollowSmoothTime = 0.08f;
        private Vector3 _cameraFollowVel;

        [Header("Mobile UI")]
        [SerializeField] private bool forceMobileUi;
        [SerializeField] private bool showTouchControlsOnDesktop = true;
        [SerializeField] private Vector2 mobileVirtualResolution = new Vector2(1080f, 1920f);
        [SerializeField] private float mobileButtonAlpha = 0.35f;
        [SerializeField] private float mobileButtonAlphaPressed = 0.6f;
        [SerializeField] private float menuUiScale = 2.5f;
        [SerializeField] private float menuUiScaleFactor = 0.75f;
        [SerializeField] private float touchFireXOffset = -1000f;
        [SerializeField] private float touchDpadXOffset = 1000f;
        [SerializeField] private bool logTouchLayout;

        private GUIStyle _mobileButtonStyle;
        private GUIStyle _mobileButtonPressedStyle;

        private Texture2D _touchCircleTex;
        private Texture2D _touchCirclePressedTex;
        private Texture2D _touchCircleRingTex;
        private Texture2D _touchCircleRingFireTex;

        private string _selectedTerrain;

        private enum MenuScreen
        {
            Main = 0,
            SetupMap = 1,
            SetupMode = 2,
            SetupDifficulty = 3,
            SetupTeamSize = 4,
            Records = 5,
        }

        private bool _showMainMenu;
        private MenuScreen _screen;

        private bool _showPauseMenu;

        private int _mainMenuSelectedIndex;
        private int _newGameSelectedIndex;
        private int _recordsSelectedIndex;

        private bool _showMapMenu;
        private Vector2 _mapScroll;
        private readonly List<string> _mapResourcePaths = new List<string>(16);
        private readonly List<string> _mapDisplayNames = new List<string>(16);
        private int _selectedMapIndex = -1;
        private float _lastClickTime;
        private int _lastClickIndex = -1;
        private Scene _pendingScene;
        private bool _hasPendingScene;

        private bool _isDragging;
        private Vector2 _dragStartMouse;
        private Vector3 _dragStartCamPos;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            if (FindAnyObjectByType<Bootstrap>() != null)
            {
                return;
            }
            var go = new GameObject("Bootstrap");
            DontDestroyOnLoad(go);
            go.AddComponent<Bootstrap>();
        }

        public static void RestartMatch()
        {
            Bootstrap b;
#if UNITY_6000_0_OR_NEWER
            b = FindFirstObjectByType<Bootstrap>();
#else
            b = FindObjectOfType<Bootstrap>();
#endif
            if (b == null)
            {
                var s = SceneManager.GetActiveScene();
                if (!string.IsNullOrEmpty(s.path))
                {
                    SceneManager.LoadScene(s.name);
                }
                return;
            }

            var scene = SceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(scene.path))
            {
                {
#if UNITY_6000_0_OR_NEWER
                    var tms = FindObjectsByType<TurnManager>(FindObjectsSortMode.None);
#else
                    var tms = FindObjectsOfType<TurnManager>();
#endif
                    if (tms != null)
                    {
                        for (var i = 0; i < tms.Length; i++)
                        {
                            var tm = tms[i];
                            if (tm != null)
                            {
                                tm.gameObject.name = "TurnManager_OLD";
                                Destroy(tm.gameObject);
                            }
                        }
                    }
                }
                b.GenerateWorld(scene);
            }
        }

        private void Awake()
        {
            Application.targetFrameRate = 60;
            SceneManager.sceneLoaded += OnSceneLoaded;

            var active = SceneManager.GetActiveScene();
#if UNITY_2023_1_OR_NEWER || UNITY_6000_0_OR_NEWER
            var bootstrapCount = FindObjectsByType<Bootstrap>(FindObjectsSortMode.None).Length;
#else
            var bootstrapCount = FindObjectsOfType<Bootstrap>().Length;
#endif
            Debug.Log($"[Stage1] Bootstrap.Awake activeScene='{active.name}' path='{active.path}' bootstraps={bootstrapCount}");
        }

        private void Start()
        {
            _selectedTerrain = PlayerPrefs.GetString(TerrainPngPathPrefKey, string.Empty);
            SelectedTeamSize = Mathf.Clamp(PlayerPrefs.GetInt(TeamSizePrefKey, 3), 1, 5);
            VsCpu = PlayerPrefs.GetInt(VsCpuPrefKey, 0) != 0;
            CpuDifficulty = (WormCrawlerPrototype.AI.BotDifficulty)Mathf.Clamp(PlayerPrefs.GetInt(CpuDifficultyPrefKey, 1), 0, 2);
            RefreshMapList();

            var s = SceneManager.GetActiveScene();
            Debug.Log($"[Stage1] Bootstrap.Start activeScene='{s.name}' path='{s.path}'");
            if (!_generatedOnce && !string.IsNullOrEmpty(s.path))
            {
                _generatedOnce = true;
                OpenMainMenu(s);
            }
        }

        private void SetSelectedTerrain(string resourcesPath)
        {
            if (string.IsNullOrEmpty(resourcesPath))
            {
                return;
            }

            _selectedTerrain = resourcesPath;
            PlayerPrefs.SetString(TerrainPngPathPrefKey, resourcesPath);
            PlayerPrefs.Save();
        }

        private void SetSelectedTerrainLocal(string resourcesPath)
        {
            if (string.IsNullOrEmpty(resourcesPath))
            {
                return;
            }

            _selectedTerrain = resourcesPath;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (_generatedOnce)
            {
                return;
            }

            _generatedOnce = true;
            Debug.Log($"[Stage1] Bootstrap.OnSceneLoaded scene='{scene.name}' path='{scene.path}' mode={mode}");
            OpenMainMenu(scene);
        }

        private void Update()
        {
            if (_showMainMenu || _showMapMenu)
            {
                if (_showMapMenu) HandleMapMenuKeyboard();
                if (_showMainMenu) HandleMainMenuKeyboard();
                return;
            }

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                _showPauseMenu = !_showPauseMenu;
                IsMapMenuOpen = _showPauseMenu;
                return;
            }

            if (Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame)
            {
                _followHero = !_followHero;
            }

            if (!_followHero)
            {
                if (_cam == null)
                {
                    _cam = Camera.main;
                    if (_cam == null)
                    {
                        _cam = FindAnyObjectByType<Camera>();
                    }
                }

                if (_cam != null && Keyboard.current != null)
                {
                    var move = Vector3.zero;
                    if (Keyboard.current.leftArrowKey.isPressed || Keyboard.current.aKey.isPressed) move.x -= 1f;
                    if (Keyboard.current.rightArrowKey.isPressed || Keyboard.current.dKey.isPressed) move.x += 1f;
                    if (Keyboard.current.downArrowKey.isPressed || Keyboard.current.sKey.isPressed) move.y -= 1f;
                    if (Keyboard.current.upArrowKey.isPressed || Keyboard.current.wKey.isPressed) move.y += 1f;

                    if (move.sqrMagnitude > 0.001f)
                    {
                        move.Normalize();
                        _cam.transform.position += move * (_cameraPanSpeed * Time.unscaledDeltaTime);
                    }

                    if (_cam.orthographic)
                    {
                        var zoomDelta = 0f;
                        if (Keyboard.current.minusKey.isPressed) zoomDelta += 1f;
                        if (Keyboard.current.equalsKey.isPressed) zoomDelta -= 1f;

                        if (Mathf.Abs(zoomDelta) > 0.001f)
                        {
                            _cam.orthographicSize = Mathf.Clamp(
                                _cam.orthographicSize + zoomDelta * (_cameraZoomSpeed * Time.unscaledDeltaTime),
                                2f,
                                30f);
                        }
                    }
                }

                if (_cam != null && _cam.orthographic && Mouse.current != null)
                {
                    var dragPressed = Mouse.current.middleButton.isPressed || Mouse.current.rightButton.isPressed;
                    var mousePos = Mouse.current.position.ReadValue();
                    if (dragPressed && !_isDragging)
                    {
                        _isDragging = true;
                        _dragStartMouse = mousePos;
                        _dragStartCamPos = _cam.transform.position;
                    }
                    else if (!dragPressed && _isDragging)
                    {
                        _isDragging = false;
                    }

                    if (_isDragging)
                    {
                        var deltaPx = mousePos - _dragStartMouse;
                        var worldPerPixel = (2f * _cam.orthographicSize) / Mathf.Max(1f, Screen.height);
                        var deltaWorld = new Vector3(-deltaPx.x * worldPerPixel, -deltaPx.y * worldPerPixel, 0f);
                        _cam.transform.position = _dragStartCamPos + deltaWorld;
                    }

                    var scroll = Mouse.current.scroll.ReadValue().y;
                    if (Mathf.Abs(scroll) > 0.01f)
                    {
                        var scrollZoom = -scroll * 0.01f;
                        _cam.orthographicSize = Mathf.Clamp(
                            _cam.orthographicSize + scrollZoom * _cameraZoomSpeed,
                            2f,
                            30f);
                    }
                }
            }

            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            {
                RestartMatch();
            }
        }

        private void OnGUI()
        {
            EnsureMobileGuiStyles();

            if (_showMapMenu)
            {
                DrawMapMenu();
                return;
            }

            if (_showPauseMenu)
            {
                DrawPauseMenu();
                return;
            }

            if (_showMainMenu)
            {
                DrawMainMenu();
                return;
            }

            if (IsMobileUiEnabled())
            {
                DrawMobileTouchControls();
            }
        }

        private bool IsMobileUiEnabled()
        {
            if (forceMobileUi) return true;
            if (Application.isMobilePlatform) return true;
            if (showTouchControlsOnDesktop) return true;
            return Touchscreen.current != null;
        }

        private void EnsureMobileGuiStyles()
        {
            if (_mobileButtonStyle != null && _mobileButtonPressedStyle != null)
            {
                return;
            }

            _mobileButtonStyle = new GUIStyle(GUI.skin.label);
            _mobileButtonStyle.fontSize = Mathf.RoundToInt(44f);
            _mobileButtonStyle.normal.textColor = Color.white;
            _mobileButtonStyle.alignment = TextAnchor.MiddleCenter;
            _mobileButtonStyle.wordWrap = true;
            _mobileButtonStyle.clipping = TextClipping.Clip;

            _mobileButtonPressedStyle = new GUIStyle(_mobileButtonStyle);
            _mobileButtonPressedStyle.normal.textColor = Color.white;

            if (_touchCircleTex == null)
            {
                _touchCircleTex = GenerateCircleTexture(256, Color.white);
            }
            if (_touchCirclePressedTex == null)
            {
                _touchCirclePressedTex = GenerateCircleTexture(256, Color.white);
            }

            if (_touchCircleRingTex == null)
            {
                _touchCircleRingTex = GenerateCircleRingTexture(256, Color.white, 10f);
            }
            if (_touchCircleRingFireTex == null)
            {
                _touchCircleRingFireTex = GenerateCircleRingTexture(256, new Color(1f, 0.25f, 0.25f, 1f), 12f);
            }
        }

        private static Texture2D GenerateCircleTexture(int size, Color color)
        {
            size = Mathf.Clamp(size, 32, 512);
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, mipChain: false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            var cx = (size - 1) * 0.5f;
            var cy = (size - 1) * 0.5f;
            var r = (size - 2) * 0.5f;
            var r2 = r * r;

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x - cx;
                    var dy = y - cy;
                    var d2 = dx * dx + dy * dy;
                    var a = d2 <= r2 ? 1f : 0f;
                    tex.SetPixel(x, y, new Color(color.r, color.g, color.b, a));
                }
            }

            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return tex;
        }

        private static Texture2D GenerateCircleRingTexture(int size, Color color, float thickness)
        {
            size = Mathf.Clamp(size, 32, 512);
            thickness = Mathf.Clamp(thickness, 1f, size * 0.25f);

            var tex = new Texture2D(size, size, TextureFormat.ARGB32, mipChain: false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            var cx = (size - 1) * 0.5f;
            var cy = (size - 1) * 0.5f;
            var rOuter = (size - 2) * 0.5f;
            var rInner = Mathf.Max(0f, rOuter - thickness);
            var rOuter2 = rOuter * rOuter;
            var rInner2 = rInner * rInner;

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x - cx;
                    var dy = y - cy;
                    var d2 = dx * dx + dy * dy;
                    var a = (d2 <= rOuter2 && d2 >= rInner2) ? 1f : 0f;
                    tex.SetPixel(x, y, new Color(color.r, color.g, color.b, a));
                }
            }

            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return tex;
        }

        private void DrawMobileTouchControls()
        {
            if (Bootstrap.IsMapMenuOpen)
            {
                return;
            }

            var tm = TurnManager.Instance;
            if (tm == null)
            {
                tm = FindAnyObjectByType<TurnManager>();
            }
            var ap = tm != null ? tm.ActivePlayer : null;
            if (ap == null)
            {
                return;
            }

            if (ap.GetComponent<WormCrawlerPrototype.AI.SpiderBotController>() != null)
            {
                return;
            }

            var kb = Keyboard.current;

            var vw = Mathf.Max(1f, mobileVirtualResolution.x);
            var vh = Mathf.Max(1f, mobileVirtualResolution.y);

            var sx = Screen.width / vw;
            var sy = Screen.height / vh;
            var s = Mathf.Min(sx, sy);

            const float marginPx = 8f;
            var fireSizePx = 340f * s;
            var dPadBtnPx = 190f * s;
            var dPadGapPx = 54f * s;

            var hudFont = Mathf.Clamp(Mathf.RoundToInt(Screen.height * 0.032f), 18, 44);
            var hudPad = Mathf.Max(10f, hudFont * 0.4f);
            var hudThirdW = Screen.width * 0.33f;

            var leftHudCenterX = hudPad + hudThirdW * 0.5f;
            var rightHudRX = Screen.width - hudPad - hudThirdW;

            var fireRect = new Rect(
                Mathf.Clamp((leftHudCenterX + touchFireXOffset) - fireSizePx * 0.5f, marginPx, Screen.width - marginPx - fireSizePx),
                Screen.height - marginPx - fireSizePx,
                fireSizePx,
                fireSizePx);

            var dPadBlockW = dPadBtnPx * 3f + dPadGapPx * 2f;
            var dPadBlockH = dPadBtnPx * 3f + dPadGapPx * 2f;
            var dPadOriginX = Mathf.Clamp((rightHudRX + touchDpadXOffset) - dPadBlockW * 0.5f, marginPx, Screen.width - marginPx - dPadBlockW);
            var dPadOriginY = Screen.height - marginPx - dPadBlockH;

            if (logTouchLayout)
            {
                Debug.Log($"[TouchLayout] screen={Screen.width}x{Screen.height} virtual={vw}x{vh} s={s:0.###} fireRect={fireRect} dpadOrigin=({dPadOriginX:0.###},{dPadOriginY:0.###}) dpadBlock=({dPadBlockW:0.###},{dPadBlockH:0.###}) offsets=(fireX:{touchFireXOffset:0.###},dpadX:{touchDpadXOffset:0.###})");
            }

            var upRect = new Rect(dPadOriginX + dPadBtnPx + dPadGapPx, dPadOriginY, dPadBtnPx, dPadBtnPx);
            var leftRect = new Rect(dPadOriginX, dPadOriginY + dPadBtnPx + dPadGapPx, dPadBtnPx, dPadBtnPx);
            var rightRect = new Rect(dPadOriginX + (dPadBtnPx + dPadGapPx) * 2f, dPadOriginY + dPadBtnPx + dPadGapPx, dPadBtnPx, dPadBtnPx);
            var downRect = new Rect(dPadOriginX + dPadBtnPx + dPadGapPx, dPadOriginY + (dPadBtnPx + dPadGapPx) * 2f, dPadBtnPx, dPadBtnPx);

            var mirrorKeyboard = Application.isMobilePlatform;
            var keyLeft = mirrorKeyboard && kb != null && (kb.leftArrowKey.isPressed || kb.aKey.isPressed);
            var keyRight = mirrorKeyboard && kb != null && (kb.rightArrowKey.isPressed || kb.dKey.isPressed);
            var keyUp = mirrorKeyboard && kb != null && (kb.upArrowKey.isPressed || kb.wKey.isPressed);
            var keyDown = mirrorKeyboard && kb != null && (kb.downArrowKey.isPressed || kb.sKey.isPressed);
            var keyFire = mirrorKeyboard && kb != null && (kb.spaceKey.isPressed || kb.enterKey.isPressed);

            var pressedFire = DrawRoundMobileButton(fireRect, string.Empty, keyFire, repeat: true, ringTex: _touchCircleRingFireTex);
            var pressedUp = DrawRoundMobileButton(upRect, "↑", keyUp, repeat: true);
            var pressedLeft = DrawRoundMobileButton(leftRect, "←", keyLeft, repeat: true);
            var pressedRight = DrawRoundMobileButton(rightRect, "→", keyRight, repeat: true);
            var pressedDown = DrawRoundMobileButton(downRect, "↓", keyDown, repeat: true);

            ApplyMobileInputs(ap, pressedLeft || keyLeft, pressedRight || keyRight, pressedUp || keyUp, pressedDown || keyDown, pressedFire || keyFire);
        }

        private bool DrawRoundMobileButton(Rect r, string label, bool keyboardPressed, bool repeat, Texture2D ringTex = null)
        {
            var colPrev = GUI.color;

            var pressedNow = repeat ? GUI.RepeatButton(r, GUIContent.none, GUIStyle.none) : GUI.Button(r, GUIContent.none, GUIStyle.none);
            var isPressed = keyboardPressed || pressedNow;

            GUI.color = new Color(1f, 1f, 1f, isPressed ? mobileButtonAlphaPressed : mobileButtonAlpha);
            var tex = isPressed ? _touchCirclePressedTex : _touchCircleTex;
            if (tex != null)
            {
                GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, alphaBlend: true);
            }
            GUI.color = colPrev;

            if (ringTex != null)
            {
                GUI.DrawTexture(r, ringTex, ScaleMode.StretchToFill, alphaBlend: true);
            }

            var prevAlign = _mobileButtonStyle.alignment;
            _mobileButtonStyle.alignment = TextAnchor.MiddleCenter;
            if (!string.IsNullOrEmpty(label))
            {
                GUI.Label(r, label, _mobileButtonStyle);
            }
            _mobileButtonStyle.alignment = prevAlign;

            return pressedNow;
        }

        private bool _mobileFireWasPressed;
        private bool _mobileAimOverrideActive;
        private void ApplyMobileInputs(Transform ap, bool left, bool right, bool up, bool down, bool fire)
        {
            var walker = ap.GetComponent<HeroSurfaceWalker>();
            if (walker != null)
            {
                var h = 0f;
                if (left && !right) h = -1f;
                else if (right && !left) h = 1f;

                walker.SetExternalMoveOverride((left || right), h);
            }

            var grapple = ap.GetComponent<GrappleController>();
            if (grapple != null)
            {
                var moveH = 0f;
                if (left && !right) moveH = -1f;
                else if (right && !left) moveH = 1f;

                var moveV = 0f;
                if (up && !down) moveV = 1f;
                else if (down && !up) moveV = -1f;

                grapple.SetExternalMoveOverride((left || right || up || down), moveH, moveV);
            }

            var aim = ap.GetComponent<WormAimController>();
            var aimDir = aim != null ? aim.AimDirection : Vector2.right;

            if (aim != null)
            {
                var wantAimAdjust = up || down;
                var wantFacing = left ^ right;

                if (wantAimAdjust)
                {
                    var d = aimDir.sqrMagnitude > 0.0001f ? aimDir.normalized : Vector2.right;
                    var deltaDeg = (up ? 1f : -1f) * 220f * Time.deltaTime;
                    var rad = deltaDeg * Mathf.Deg2Rad;
                    var c = Mathf.Cos(rad);
                    var s = Mathf.Sin(rad);
                    var rotated = new Vector2(d.x * c - d.y * s, d.x * s + d.y * c);
                    if (rotated.sqrMagnitude < 0.0001f) rotated = Vector2.right;
                    aim.SetExternalAimOverride(true, rotated.normalized);
                    _mobileAimOverrideActive = true;
                    aimDir = rotated.normalized;
                }
                else if (wantFacing)
                {
                    var sign = right ? 1f : -1f;
                    var d = aimDir.sqrMagnitude > 0.0001f ? aimDir.normalized : Vector2.right;
                    d.x = Mathf.Abs(d.x) < 0.15f ? sign : Mathf.Sign(d.x) * sign;
                    if (d.sqrMagnitude < 0.0001f) d = sign > 0f ? Vector2.right : Vector2.left;
                    aim.SetExternalAimOverride(true, d.normalized);
                    _mobileAimOverrideActive = true;
                    aimDir = d.normalized;
                }
                else if (_mobileAimOverrideActive)
                {
                    aim.SetExternalAimOverride(false, Vector2.right);
                    _mobileAimOverrideActive = false;
                }
            }

            if (fire && !_mobileFireWasPressed)
            {
                var grenade = ap.GetComponent<HeroGrenadeThrower>();
                if (grapple != null && grapple.IsAttached)
                {
                    if (grenade != null && grenade.Enabled)
                    {
                        grenade.TryThrowNow();
                    }
                    else
                    {
                        grapple.ForceDetach();
                    }
                    _mobileFireWasPressed = fire;
                    return;
                }
                if (grenade != null && grenade.Enabled)
                {
                    grenade.TryThrowNow();
                }
                else
                {
                    var claw = ap.GetComponent<HeroClawGun>();
                    if (claw != null && claw.Enabled)
                    {
                        claw.TryFireOnceNow();
                    }
                    else if (grapple != null)
                    {
                        var ropeDir = aimDir;
                        if (ropeDir.sqrMagnitude < 0.0001f)
                        {
                            ropeDir = Vector2.up;
                        }
                        else
                        {
                            ropeDir.Normalize();
                        }

                        if (Mathf.Abs(ropeDir.y) < 0.35f)
                        {
                            var sx = Mathf.Abs(ropeDir.x) > 0.05f ? Mathf.Sign(ropeDir.x) : 1f;
                            ropeDir = (Vector2.up + Vector2.right * (0.15f * sx)).normalized;
                        }

                        grapple.FireRopeExternal(ropeDir);
                    }
                }
            }

            {
                var claw = ap.GetComponent<HeroClawGun>();
                if (claw != null && claw.Enabled)
                {
                    claw.SetExternalHeld(fire);
                }
            }

            _mobileFireWasPressed = fire;
        }

        private void OpenMainMenu(Scene scene)
        {
            _pendingScene = scene;
            _hasPendingScene = true;
            RefreshMapList();
            EnsureSelectionValid();

            _showMainMenu = true;
            _screen = MenuScreen.Main;
            _mainMenuSelectedIndex = 0;
            _newGameSelectedIndex = 0;
            _recordsSelectedIndex = 0;
            IsMapMenuOpen = true;
        }

        private void StartSetupWizard()
        {
            RefreshMapList();
            EnsureSelectionValid();
            _screen = MenuScreen.SetupMap;
            _newGameSelectedIndex = 0;
        }

        private void CloseMainMenu()
        {
            _showMainMenu = false;
            IsMapMenuOpen = false;
        }

        private void DrawMainMenu()
        {
            var mobile = IsMobileUiEnabled();
            var uiScale = Mathf.Max(1f, menuUiScale) * Mathf.Clamp(menuUiScaleFactor, 0.1f, 3f);
            var windowRect = new Rect(0f, 0f, Screen.width, Screen.height);

            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);
            GUI.Box(windowRect, "");
            GUI.backgroundColor = prevBg;

            var pad = (mobile ? 34f : 12f) * uiScale;
            var topPad = (mobile ? 64f : 34f) * uiScale;
            var inner = new Rect(windowRect.x + pad, windowRect.y + topPad, windowRect.width - pad * 2f, windowRect.height - (pad + 20f) - topPad);
            var y = inner.y;

            var prevLabelFont = GUI.skin.label.fontSize;
            var prevButtonFont = GUI.skin.button.fontSize;
            var scaledFont = Mathf.RoundToInt(Screen.height * 0.028f * uiScale);
            GUI.skin.label.fontSize = scaledFont;
            GUI.skin.button.fontSize = scaledFont;

            var btnPadX = Mathf.Max(10f * uiScale, scaledFont * 0.9f);
            var btnPadY = Mathf.Max(6f * uiScale, scaledFont * 0.45f);
            var footerBtnH = Mathf.Max(24f * uiScale, scaledFont + btnPadY * 2f);

            float ButtonW(string label)
            {
                var s = GUI.skin.button.CalcSize(new GUIContent(label));
                return Mathf.Clamp(s.x + btnPadX * 2f, 60f * uiScale, inner.width);
            }

            float ButtonH()
            {
                return footerBtnH;
            }

            Rect CenterButtonRect(float y0, string label)
            {
                var w = ButtonW(label);
                var h = ButtonH();
                var x = inner.x + (inner.width - w) * 0.5f;
                return new Rect(x, y0, w, h);
            }

            var footerBtnBackW = ButtonW("Back");
            var footerBtnNextW = ButtonW("Next");
            var footerBtnStartW = ButtonW("Start");
            var footerButtonsY = windowRect.yMax - footerBtnH - Mathf.Max(2f, 4f * uiScale);

            if (_screen == MenuScreen.Main)
            {
                var gap = (mobile ? 26f : 12f) * uiScale;

                DrawSelectableButton(CenterButtonRect(y, "New Game"), "New Game", _mainMenuSelectedIndex == 0, StartSetupWizard);
                y += ButtonH() + gap;
                DrawSelectableButton(CenterButtonRect(y, "Records"), "Records", _mainMenuSelectedIndex == 1, () => _screen = MenuScreen.Records);
                y += ButtonH() + gap;
                DrawSelectableButton(CenterButtonRect(y, "Exit"), "Exit", _mainMenuSelectedIndex == 2, ExitGame);
                GUI.skin.label.fontSize = prevLabelFont;
                GUI.skin.button.fontSize = prevButtonFont;
                return;
            }

            if (_screen == MenuScreen.SetupMap)
            {
                var titleH = (mobile ? 52f : 24f) * uiScale;
                var titleY = windowRect.y + Mathf.Max(2f, pad * 0.15f);
                var afterTitleGap = Mathf.Max(4f * 0.5f, 8f * uiScale * 0.5f);

                var itemH = (mobile ? 46f : 20f) * uiScale * 0.55f;

                GUI.Label(new Rect(inner.x, titleY, inner.width, titleH), "Step 1/4: Choose Map");

                var listTop = titleY + titleH + afterTitleGap;
                var listBottom = footerButtonsY - Mathf.Max(4f, pad * 0.25f);
                var listH = Mathf.Max(1f, listBottom - listTop);
                var scrollRect = new Rect(inner.x, listTop, inner.width, listH);
                var contentRect = new Rect(0f, 0f, inner.width - 20f, Mathf.Max(listH, _mapDisplayNames.Count * itemH));

                _mapScroll = GUI.BeginScrollView(scrollRect, _mapScroll, contentRect);
                for (var i = 0; i < _mapDisplayNames.Count; i++)
                {
                    var r = new Rect(0f, i * itemH, contentRect.width, itemH);
                    if (i == _selectedMapIndex)
                    {
                        var prev = GUI.color;
                        GUI.color = new Color(0.25f, 0.55f, 1f, 0.35f);
                        GUI.Box(r, GUIContent.none);
                        GUI.color = prev;
                    }

                    GUI.Label(new Rect(r.x + 4f * uiScale, r.y + 1f * uiScale, r.width - 8f * uiScale, r.height - 2f * uiScale), _mapDisplayNames[i]);
                    if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                    {
                        _selectedMapIndex = i;
                    }
                }
                GUI.EndScrollView();

                var buttonsY = footerButtonsY;
                DrawSelectableButton(new Rect(inner.x, buttonsY, footerBtnBackW, footerBtnH), "Back", selected: false, () => _screen = MenuScreen.Main);

                GUI.enabled = _selectedMapIndex >= 0 && _selectedMapIndex < _mapResourcePaths.Count;
                DrawSelectableButton(new Rect(inner.xMax - footerBtnNextW, buttonsY, footerBtnNextW, footerBtnH), "Next", selected: false, () =>
                {
                    if (_selectedMapIndex >= 0 && _selectedMapIndex < _mapResourcePaths.Count)
                    {
                        SetSelectedTerrain(_mapResourcePaths[_selectedMapIndex]);
                    }
                    _screen = MenuScreen.SetupMode;
                });
                GUI.enabled = true;

                GUI.skin.label.fontSize = prevLabelFont;
                GUI.skin.button.fontSize = prevButtonFont;
                return;
            }

            if (_screen == MenuScreen.SetupMode)
            {
                var lineH = (mobile ? 60f : 28f) * uiScale;
                var gapY = (mobile ? 28f : 14f) * uiScale;

                var titleH = (mobile ? 52f : 24f) * uiScale;
                var titleY = windowRect.y + Mathf.Max(2f, pad * 0.15f);
                var afterTitleGap = Mathf.Max(4f, 8f * uiScale);

                GUI.Label(new Rect(inner.x, titleY, inner.width, titleH), "Step 2/4: Mode");
                y = titleY + titleH + afterTitleGap;

                y += lineH;

                var buttonsY = footerButtonsY;
                var contentBottom = buttonsY - pad * 0.6f;
                var modeW = ButtonW("Vs Player");
                var modeX = inner.x;
                var btnH = ButtonH();
                var modeY0 = Mathf.Min(y + gapY, contentBottom - (btnH * 2f + gapY));

                DrawSelectableButton(new Rect(modeX, modeY0, modeW, btnH), "Vs Player", !VsCpu, () =>
                {
                    VsCpu = false;
                    PlayerPrefs.SetInt(VsCpuPrefKey, 0);
                    PlayerPrefs.Save();
                });

                DrawSelectableButton(new Rect(modeX, modeY0 + btnH + gapY, modeW, btnH), "Vs CPU", VsCpu, () =>
                {
                    VsCpu = true;
                    PlayerPrefs.SetInt(VsCpuPrefKey, 1);
                    PlayerPrefs.Save();
                });

                DrawSelectableButton(new Rect(inner.x, buttonsY, footerBtnBackW, footerBtnH), "Back", selected: false, () => _screen = MenuScreen.SetupMap);
                DrawSelectableButton(new Rect(inner.xMax - footerBtnNextW, buttonsY, footerBtnNextW, footerBtnH), "Next", selected: false, () => _screen = VsCpu ? MenuScreen.SetupDifficulty : MenuScreen.SetupTeamSize);

                GUI.skin.label.fontSize = prevLabelFont;
                GUI.skin.button.fontSize = prevButtonFont;
                return;
            }

            if (_screen == MenuScreen.SetupDifficulty)
            {
                var lineH = (mobile ? 60f : 28f) * uiScale;
                var gapY = (mobile ? 22f : 12f) * uiScale;

                var titleH = (mobile ? 52f : 24f) * uiScale;
                var titleY = windowRect.y + Mathf.Max(2f, pad * 0.15f);
                var afterTitleGap = Mathf.Max(4f, 8f * uiScale);

                GUI.Label(new Rect(inner.x, titleY, inner.width, titleH), "Step 2/4: CPU Difficulty");
                y = titleY + titleH + afterTitleGap;

                GUI.Label(new Rect(inner.x, y, inner.width, (mobile ? 48f : 24f) * uiScale), "Choose difficulty:");
                y += lineH;

                var w = Mathf.Max(ButtonW("Normal"), Mathf.Max(ButtonW("Easy"), ButtonW("Hard")));
                var x = inner.x + (inner.width - w) * 0.5f;
                var btnH = ButtonH();
                gapY = btnH / 3f;

                var buttonsY = footerButtonsY;
                var contentBottom = buttonsY - pad * 0.6f;
                var totalButtonsH = btnH * 3f + gapY * 2f;
                y = Mathf.Min(y, contentBottom - totalButtonsH);

                DrawSelectableButton(new Rect(x, y, w, btnH), "Easy", CpuDifficulty == WormCrawlerPrototype.AI.BotDifficulty.Easy, () =>
                {
                    CpuDifficulty = WormCrawlerPrototype.AI.BotDifficulty.Easy;
                    PlayerPrefs.SetInt(CpuDifficultyPrefKey, (int)CpuDifficulty);
                    PlayerPrefs.Save();
                });
                y += btnH + gapY;

                DrawSelectableButton(new Rect(x, y, w, btnH), "Normal", CpuDifficulty == WormCrawlerPrototype.AI.BotDifficulty.Normal, () =>
                {
                    CpuDifficulty = WormCrawlerPrototype.AI.BotDifficulty.Normal;
                    PlayerPrefs.SetInt(CpuDifficultyPrefKey, (int)CpuDifficulty);
                    PlayerPrefs.Save();
                });
                y += btnH + gapY;

                DrawSelectableButton(new Rect(x, y, w, btnH), "Hard", CpuDifficulty == WormCrawlerPrototype.AI.BotDifficulty.Hard, () =>
                {
                    CpuDifficulty = WormCrawlerPrototype.AI.BotDifficulty.Hard;
                    PlayerPrefs.SetInt(CpuDifficultyPrefKey, (int)CpuDifficulty);
                    PlayerPrefs.Save();
                });

                DrawSelectableButton(new Rect(inner.x, buttonsY, footerBtnBackW, footerBtnH), "Back", selected: false, () => _screen = MenuScreen.SetupMode);
                DrawSelectableButton(new Rect(inner.xMax - footerBtnNextW, buttonsY, footerBtnNextW, footerBtnH), "Next", selected: false, () => _screen = MenuScreen.SetupTeamSize);

                GUI.skin.label.fontSize = prevLabelFont;
                GUI.skin.button.fontSize = prevButtonFont;
                return;
            }

            if (_screen == MenuScreen.SetupTeamSize)
            {
                var lineH = (mobile ? 60f : 28f) * uiScale;

                var titleH = (mobile ? 52f : 24f) * uiScale;
                var titleY = windowRect.y + Mathf.Max(2f, pad * 0.15f);
                var afterTitleGap = Mathf.Max(4f, 8f * uiScale);

                GUI.Label(new Rect(inner.x, titleY, inner.width, titleH), VsCpu ? "Step 4/4: Team Size" : "Step 3/3: Team Size");
                y = titleY + titleH + afterTitleGap;

                y += lineH;

                var btnW = Mathf.Max(ButtonW("3x3"), Mathf.Max(ButtonW("1x1"), ButtonW("5x5")));
                var btnH = ButtonH();
                var gapX = (mobile ? 18f : 10f) * uiScale;
                var gapY = (mobile ? 22f : 12f) * uiScale;

                var cols = 3;
                var gridW = btnW * cols + gapX * (cols - 1);
                var x0 = inner.x + (inner.width - gridW) * 0.5f;

                for (var idx = 0; idx < 5; idx++)
                {
                    var s = idx + 1;
                    var row = idx / cols;
                    var col = idx % cols;
                    var r = new Rect(x0 + (btnW + gapX) * col, y + (btnH + gapY) * row, btnW, btnH);
                    var label = $"{s}x{s}";
                    var isSelected = SelectedTeamSize == s;
                    DrawSelectableButton(r, label, isSelected, () =>
                    {
                        SelectedTeamSize = s;
                        PlayerPrefs.SetInt(TeamSizePrefKey, SelectedTeamSize);
                        PlayerPrefs.Save();
                    });
                }

                var buttonsY = footerButtonsY;
                DrawSelectableButton(new Rect(inner.x, buttonsY, footerBtnBackW, footerBtnH), "Back", selected: false, () => _screen = VsCpu ? MenuScreen.SetupDifficulty : MenuScreen.SetupMode);
                DrawSelectableButton(new Rect(inner.xMax - footerBtnStartW, buttonsY, footerBtnStartW, footerBtnH), "Start", selected: false, () =>
                {
                    CloseMainMenu();
                    if (_hasPendingScene && !string.IsNullOrEmpty(_pendingScene.path))
                    {
                        GenerateWorld(_pendingScene);
                        _hasPendingScene = false;
                    }
                });

                GUI.skin.label.fontSize = prevLabelFont;
                GUI.skin.button.fontSize = prevButtonFont;
                return;
            }

            if (_screen == MenuScreen.Records)
            {
                var total = PlayerPrefs.GetInt("WormCrawler_TotalGames", 0);
                var w0 = PlayerPrefs.GetInt("WormCrawler_WinsTeam0", 0);
                var w1 = PlayerPrefs.GetInt("WormCrawler_WinsTeam1", 0);
                var last = PlayerPrefs.GetString("WormCrawler_LastDuel", "");

                GUI.Label(new Rect(inner.x, y, inner.width, 24f * uiScale), $"Total games: {total}");
                y += 24f * uiScale;
                GUI.Label(new Rect(inner.x, y, inner.width, 24f * uiScale), $"Spider wins: {w0}");
                y += 24f * uiScale;
                GUI.Label(new Rect(inner.x, y, inner.width, 24f * uiScale), $"Red wins: {w1}");
                y += 34f * uiScale;
                GUI.Label(new Rect(inner.x, y, inner.width, 48f * uiScale), $"Last duel: {(string.IsNullOrEmpty(last) ? "-" : last)}");

                DrawSelectableButton(new Rect(inner.x, footerButtonsY, footerBtnBackW, footerBtnH), "Back", _recordsSelectedIndex == 0, () => _screen = MenuScreen.Main);
                GUI.skin.label.fontSize = prevLabelFont;
                GUI.skin.button.fontSize = prevButtonFont;
                return;
            }

            GUI.skin.label.fontSize = prevLabelFont;
            GUI.skin.button.fontSize = prevButtonFont;
        }

        private void HandleMainMenuKeyboard()
        {
            if (UnityEngine.InputSystem.Keyboard.current == null)
            {
                return;
            }

            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (_screen == MenuScreen.Main)
            {
                if (kb.downArrowKey.wasPressedThisFrame) _mainMenuSelectedIndex = Mathf.Clamp(_mainMenuSelectedIndex + 1, 0, 2);
                else if (kb.upArrowKey.wasPressedThisFrame) _mainMenuSelectedIndex = Mathf.Clamp(_mainMenuSelectedIndex - 1, 0, 2);
                else if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
                {
                    if (_mainMenuSelectedIndex == 0) StartSetupWizard();
                    else if (_mainMenuSelectedIndex == 1) _screen = MenuScreen.Records;
                    else ExitGame();
                }
            }
            else if (_screen == MenuScreen.Records)
            {
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame)
                {
                    _screen = MenuScreen.Main;
                }
            }
            else
            {
                if (kb.escapeKey.wasPressedThisFrame)
                {
                    if (_screen == MenuScreen.SetupMap) _screen = MenuScreen.Main;
                    else if (_screen == MenuScreen.SetupMode) _screen = MenuScreen.SetupMap;
                    else if (_screen == MenuScreen.SetupDifficulty) _screen = MenuScreen.SetupMode;
                    else if (_screen == MenuScreen.SetupTeamSize) _screen = VsCpu ? MenuScreen.SetupDifficulty : MenuScreen.SetupMode;
                }

                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
                {
                    if (_screen == MenuScreen.SetupMap)
                    {
                        if (_selectedMapIndex >= 0 && _selectedMapIndex < _mapResourcePaths.Count)
                        {
                            SetSelectedTerrain(_mapResourcePaths[_selectedMapIndex]);
                        }
                        _screen = MenuScreen.SetupMode;
                    }
                    else if (_screen == MenuScreen.SetupMode)
                    {
                        _screen = VsCpu ? MenuScreen.SetupDifficulty : MenuScreen.SetupTeamSize;
                    }
                    else if (_screen == MenuScreen.SetupDifficulty)
                    {
                        _screen = MenuScreen.SetupTeamSize;
                    }
                    else if (_screen == MenuScreen.SetupTeamSize)
                    {
                        CloseMainMenu();
                        if (_hasPendingScene && !string.IsNullOrEmpty(_pendingScene.path))
                        {
                            GenerateWorld(_pendingScene);
                            _hasPendingScene = false;
                        }
                    }
                }
            }
        }

        private static GUIStyle GetPressedButtonStyle()
        {
            if (_pressedButtonStyle != null)
            {
                return _pressedButtonStyle;
            }

            var s = new GUIStyle(GUI.skin.button);
            s.normal.background = s.active.background;
            s.hover.background = s.active.background;
            s.normal.textColor = s.active.textColor;
            s.hover.textColor = s.active.textColor;
            _pressedButtonStyle = s;
            return _pressedButtonStyle;
        }

        private static void DrawSelectableButton(Rect r, string label, bool selected, Action onClick)
        {
            var style = selected ? GetPressedButtonStyle() : GUI.skin.button;
            if (GUI.Button(r, label, style))
            {
                onClick?.Invoke();
            }
        }

        private static void ExitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void RefreshMapList()
        {
            _mapResourcePaths.Clear();
            _mapDisplayNames.Clear();

            var maps = Resources.LoadAll<Texture2D>(LevelsResourcesRoot);
            if (maps == null)
            {
                return;
            }

            for (var i = 0; i < maps.Length; i++)
            {
                var t = maps[i];
                if (t == null)
                {
                    continue;
                }

                var name = t.name;
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                if (string.Equals(name, "entities", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (name.IndexOf("entities", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                if (!name.StartsWith("terrain", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                _mapResourcePaths.Add($"{LevelsResourcesRoot}/{name}");
                _mapDisplayNames.Add(name);
            }

            if (_mapDisplayNames.Count > 1)
            {
                for (var i = 0; i < _mapDisplayNames.Count - 1; i++)
                {
                    for (var j = i + 1; j < _mapDisplayNames.Count; j++)
                    {
                        if (string.Compare(_mapDisplayNames[i], _mapDisplayNames[j], StringComparison.OrdinalIgnoreCase) > 0)
                        {
                            (_mapDisplayNames[i], _mapDisplayNames[j]) = (_mapDisplayNames[j], _mapDisplayNames[i]);
                            (_mapResourcePaths[i], _mapResourcePaths[j]) = (_mapResourcePaths[j], _mapResourcePaths[i]);
                        }
                    }
                }
            }
        }

        private void EnsureSelectionValid()
        {
            if (_mapResourcePaths.Count == 0)
            {
                _selectedMapIndex = -1;
                return;
            }

            if (!string.IsNullOrEmpty(_selectedTerrain))
            {
                for (var i = 0; i < _mapResourcePaths.Count; i++)
                {
                    if (string.Equals(_mapResourcePaths[i], _selectedTerrain, StringComparison.OrdinalIgnoreCase))
                    {
                        _selectedMapIndex = i;
                        return;
                    }
                }
            }

            if (_selectedMapIndex < 0 || _selectedMapIndex >= _mapResourcePaths.Count)
            {
                _selectedMapIndex = 0;
            }
        }

        private void DrawMapMenu()
        {
            var uiScale = Mathf.Max(1f, menuUiScale);

            var prevLabelFont = GUI.skin.label.fontSize;
            var prevButtonFont = GUI.skin.button.fontSize;
            GUI.skin.label.fontSize = Mathf.RoundToInt(prevLabelFont * uiScale);
            GUI.skin.button.fontSize = Mathf.RoundToInt(prevButtonFont * uiScale);

            var windowW = Mathf.Min(520f * uiScale, Screen.width - 40f);
            var windowH = Mathf.Min(520f * uiScale, Screen.height - 40f);
            var windowX = (Screen.width - windowW) * 0.5f;
            var windowY = (Screen.height - windowH) * 0.5f;
            var windowRect = new Rect(windowX, windowY, windowW, windowH);

            GUI.Box(windowRect, "Select map");

            var inner = new Rect(windowRect.x + 10f * uiScale, windowRect.y + 30f * uiScale, windowRect.width - 20f * uiScale, windowRect.height - 40f * uiScale);
            var listRect = new Rect(inner.x, inner.y, inner.width, inner.height - 50f);

            var itemH = 24f * uiScale;
            var contentH = Mathf.Max(listRect.height, _mapDisplayNames.Count * itemH);
            var viewRect = new Rect(0f, 0f, listRect.width - 20f, contentH);

            var e = Event.current;
            if (e != null && e.type == EventType.ScrollWheel && listRect.Contains(e.mousePosition))
            {
                _mapScroll.y += e.delta.y * 20f;
                _mapScroll.y = Mathf.Clamp(_mapScroll.y, 0f, Mathf.Max(0f, contentH - listRect.height));
                e.Use();
            }

            _mapScroll = GUI.BeginScrollView(listRect, _mapScroll, viewRect);
            for (var i = 0; i < _mapDisplayNames.Count; i++)
            {
                var r = new Rect(0f, i * itemH, viewRect.width, itemH);

                if (i == _selectedMapIndex)
                {
                    var prev = GUI.color;
                    GUI.color = new Color(0.25f, 0.55f, 1f, 0.35f);
                    GUI.Box(r, GUIContent.none);
                    GUI.color = prev;
                }

                GUI.Label(new Rect(r.x + 6f, r.y + 3f, r.width - 12f, r.height - 6f), _mapDisplayNames[i]);

                if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                {
                    _selectedMapIndex = i;
                    var now = Time.realtimeSinceStartup;
                    var isDouble = _lastClickIndex == i && (now - _lastClickTime) <= 0.35f;
                    _lastClickIndex = i;
                    _lastClickTime = now;

                    if (isDouble)
                    {
                        AcceptMapSelection();
                    }
                }
            }
            GUI.EndScrollView();

            var buttonsY = inner.yMax - 40f * uiScale;
            var okRect = new Rect(inner.xMax - 200f * uiScale, buttonsY, 90f * uiScale, 30f * uiScale);
            var cancelRect = new Rect(inner.xMax - 100f * uiScale, buttonsY, 90f * uiScale, 30f * uiScale);

            GUI.enabled = _selectedMapIndex >= 0 && _selectedMapIndex < _mapResourcePaths.Count;
            if (GUI.Button(okRect, "OK"))
            {
                AcceptMapSelection();
            }
            GUI.enabled = true;

            if (GUI.Button(cancelRect, "Cancel"))
            {
                CancelMapSelection();
            }

            GUI.skin.label.fontSize = prevLabelFont;
            GUI.skin.button.fontSize = prevButtonFont;
        }

        private void HandleMapMenuKeyboard()
        {
            if (Keyboard.current == null)
            {
                return;
            }

            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                CancelMapSelection();
                return;
            }

            if (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame)
            {
                AcceptMapSelection();
                return;
            }

            if (_mapResourcePaths.Count == 0)
            {
                return;
            }

            if (_selectedMapIndex < 0)
            {
                _selectedMapIndex = 0;
            }

            if (Keyboard.current.downArrowKey.wasPressedThisFrame)
            {
                _selectedMapIndex = Mathf.Clamp(_selectedMapIndex + 1, 0, _mapResourcePaths.Count - 1);
                ScrollToSelection();
            }
            else if (Keyboard.current.upArrowKey.wasPressedThisFrame)
            {
                _selectedMapIndex = Mathf.Clamp(_selectedMapIndex - 1, 0, _mapResourcePaths.Count - 1);
                ScrollToSelection();
            }
            else if (Keyboard.current.pageDownKey.wasPressedThisFrame)
            {
                _selectedMapIndex = Mathf.Clamp(_selectedMapIndex + 10, 0, _mapResourcePaths.Count - 1);
                ScrollToSelection();
            }
            else if (Keyboard.current.pageUpKey.wasPressedThisFrame)
            {
                _selectedMapIndex = Mathf.Clamp(_selectedMapIndex - 10, 0, _mapResourcePaths.Count - 1);
                ScrollToSelection();
            }
        }

        private void ScrollToSelection()
        {
            var itemH = 24f;
            var windowH = Mathf.Min(520f, Screen.height - 40f);
            var listH = windowH - 90f;
            if (listH <= 1f)
            {
                return;
            }

            var contentH = Mathf.Max(listH, _mapDisplayNames.Count * itemH);
            var y = _selectedMapIndex * itemH;
            var maxScroll = Mathf.Max(0f, contentH - listH);

            if (y < _mapScroll.y)
            {
                _mapScroll.y = Mathf.Clamp(y, 0f, maxScroll);
            }
            else if (y + itemH > _mapScroll.y + listH)
            {
                _mapScroll.y = Mathf.Clamp(y + itemH - listH, 0f, maxScroll);
            }
        }

        private void AcceptMapSelection()
        {
            if (_selectedMapIndex >= 0 && _selectedMapIndex < _mapResourcePaths.Count)
            {
                SetSelectedTerrain(_mapResourcePaths[_selectedMapIndex]);
            }
            else if (!string.IsNullOrEmpty(_selectedTerrain))
            {
                SetSelectedTerrain(_selectedTerrain);
            }

            _showMapMenu = false;
            if (!_showMainMenu) IsMapMenuOpen = false;
            _mapScroll = Vector2.zero;

            if (_showMainMenu)
            {
                _screen = MenuScreen.SetupMap;
                return;
            }

            if (_hasPendingScene && !string.IsNullOrEmpty(_pendingScene.path))
            {
                GenerateWorld(_pendingScene);
                _hasPendingScene = false;
            }
        }

        private void CancelMapSelection()
        {
            _showMapMenu = false;
            if (!_showMainMenu) IsMapMenuOpen = false;
            _mapScroll = Vector2.zero;

            if (_showMainMenu)
            {
                _screen = MenuScreen.SetupMap;
                return;
            }

            if (string.IsNullOrEmpty(_selectedTerrain))
            {
                EnsureSelectionValid();
                if (_selectedMapIndex >= 0 && _selectedMapIndex < _mapResourcePaths.Count)
                {
                    SetSelectedTerrainLocal(_mapResourcePaths[_selectedMapIndex]);
                }
            }

            if (_hasPendingScene && !string.IsNullOrEmpty(_pendingScene.path))
            {
                GenerateWorld(_pendingScene);
                _hasPendingScene = false;
            }
        }

        private void DrawPauseMenu()
        {
            var uiScale = Mathf.Max(1f, menuUiScale);

            var prevLabelFont = GUI.skin.label.fontSize;
            var prevButtonFont = GUI.skin.button.fontSize;
            GUI.skin.label.fontSize = Mathf.RoundToInt(prevLabelFont * uiScale);
            GUI.skin.button.fontSize = Mathf.RoundToInt(prevButtonFont * uiScale);

            var w = Mathf.Min(520f * uiScale, Screen.width - 40f);
            var h = Mathf.Min(360f * uiScale, Screen.height - 40f);
            var x = (Screen.width - w) * 0.5f;
            var y = (Screen.height - h) * 0.5f;
            var r = new Rect(x, y, w, h);

            GUI.Box(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none);
            GUI.Box(r, "Pause");

            var pad = 14f * uiScale;
            var inner = new Rect(r.x + pad, r.y + pad * 2.2f, r.width - pad * 2f, r.height - pad * 3.0f);

            var btnH = 44f * uiScale;
            var gap = 12f * uiScale;
            var yy = inner.y;

            if (GUI.Button(new Rect(inner.x, yy, inner.width, btnH), "Resume"))
            {
                _showPauseMenu = false;
                IsMapMenuOpen = false;
            }
            yy += btnH + gap;

            if (GUI.Button(new Rect(inner.x, yy, inner.width, btnH), "Restart"))
            {
                _showPauseMenu = false;
                IsMapMenuOpen = false;
                RestartMatch();
            }
            yy += btnH + gap;

            if (GUI.Button(new Rect(inner.x, yy, inner.width, btnH), "Main Menu"))
            {
                _showPauseMenu = false;
                _showMainMenu = true;
                _screen = MenuScreen.Main;
                _mainMenuSelectedIndex = 0;
                IsMapMenuOpen = true;
            }

            GUI.skin.label.fontSize = prevLabelFont;
            GUI.skin.button.fontSize = prevButtonFont;
        }

        private void LateUpdate()
        {
            if (_cam == null)
            {
                _cam = Camera.main;
                if (_cam == null)
                {
                    _cam = FindAnyObjectByType<Camera>();
                }
            }

            {
                var tm = TurnManager.Instance;
                if (tm != null && tm.ActivePlayer != null)
                {
                    _hero = tm.ActivePlayer;
                }
            }

            if (_hero == null)
            {
                var world = GameObject.Find("World");
                if (world != null)
                {
                    var heroT = world.transform.Find("Hero");
                    _hero = heroT;
                }
            }

            if (_hero == null || _cam == null) return;
            if (!_followHero) return;

            var p = _hero.position;
            var cp = _cam.transform.position;
            var target = new Vector3(p.x, p.y, cp.z);
            var smooth = Mathf.Max(0f, _cameraFollowSmoothTime);
            if (smooth <= 0.0001f)
            {
                _cam.transform.position = target;
            }
            else
            {
                _cam.transform.position = Vector3.SmoothDamp(cp, target, ref _cameraFollowVel, smooth, Mathf.Infinity, Time.unscaledDeltaTime);
            }
        }

        private void GenerateWorld(Scene targetScene)
        {
            var seed = unchecked((int)DateTime.Now.Ticks);

            Debug.Log($"[Stage1] GenerateWorld targetScene='{targetScene.name}' path='{targetScene.path}' seed={seed}");

            {
#if UNITY_6000_0_OR_NEWER
                var tms = FindObjectsByType<TurnManager>(FindObjectsSortMode.None);
#else
                var tms = FindObjectsOfType<TurnManager>();
#endif
                if (tms != null)
                {
                    for (var i = 0; i < tms.Length; i++)
                    {
                        var tm = tms[i];
                        if (tm != null)
                        {
                            tm.gameObject.name = "TurnManager_OLD";
                            Destroy(tm.gameObject);
                        }
                    }
                }
            }

            var roots = targetScene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                var go = roots[i];
                if (go == null)
                {
                    continue;
                }

                if (go.GetComponentInChildren<Camera>(true) != null)
                {
                    continue;
                }

                if (go.name == "Bootstrap")
                {
                    continue;
                }

#if UNITY_EDITOR
                ClearEditorSelectionIfDestroying(go);
#endif
                Destroy(go);
            }

            var oldGm = GameObject.Find("GameManager");
            if (oldGm != null)
            {
#if UNITY_EDITOR
                ClearEditorSelectionIfDestroying(oldGm);
#endif
                Destroy(oldGm);
            }

            var existing = GameObject.Find("World");
            if (existing != null)
            {
#if UNITY_EDITOR
                ClearEditorSelectionIfDestroying(existing);
#endif
                Destroy(existing);
            }

            var worldGO = new GameObject("World");
            SceneManager.MoveGameObjectToScene(worldGO, targetScene);
            Debug.Log($"[Stage1] World created in scene='{worldGO.scene.name}' (active='{SceneManager.GetActiveScene().name}')");
            _generator = worldGO.AddComponent<SimpleWorldGenerator>();

            _generator.ConfigureDecorations(false);

            var selectedTerrain = string.IsNullOrEmpty(_selectedTerrain)
                ? PlayerPrefs.GetString(TerrainPngPathPrefKey, string.Empty)
                : _selectedTerrain;
            if (!string.IsNullOrEmpty(selectedTerrain))
            {
                _generator.ConfigurePngTerrain(selectedTerrain);
            }

            _generator.Generate(seed);

            var heroT = worldGO.transform.Find("Hero");
            if (heroT == null)
            {
                var hero = GameObject.Find("Hero");
                heroT = hero != null ? hero.transform : null;
            }
            _hero = heroT;
            _cam = Camera.main;
            if (_cam == null)
            {
                _cam = FindAnyObjectByType<Camera>();
            }
            Debug.Log($"[Stage1] After Generate: heroFound={(_hero != null)} camFound={(_cam != null)}");
            if (_hero != null && _cam != null)
            {
                var p = _hero.position;
                if (_cam.orthographic)
                {
                    _cam.orthographicSize = 15f;
                }
                _cam.transform.position = new Vector3(p.x, p.y, _cam.transform.position.z);
            }
            else
            {
                if (_hero == null)
                {
                    Debug.LogWarning("[Stage1] Hero not found after generation. Check Console for errors during SpawnHero.");
                }
                if (_cam == null)
                {
                    Debug.LogWarning("[Stage1] No Camera found (Camera.main is null and no Camera exists). Ensure your scene has an enabled Camera.");
                }
            }
        }

#if UNITY_EDITOR
        private static void ClearEditorSelectionIfDestroying(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            var active = Selection.activeObject;
            if (active == null)
            {
                return;
            }

            GameObject activeGo = null;
            if (active is GameObject g)
            {
                activeGo = g;
            }
            else if (active is Component c)
            {
                activeGo = c != null ? c.gameObject : null;
            }

            if (activeGo == null)
            {
                return;
            }

            if (activeGo == go || activeGo.transform.IsChildOf(go.transform))
            {
                Selection.activeObject = null;
            }
        }
#endif
    }
}
