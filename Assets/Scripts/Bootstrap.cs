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
        private const string MenuGameTitle = "ГРОНvsГАТА: ПАУКАЛИПСИС";

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
        [SerializeField] private float menuSwipeEdgeWidthPx = 84f;
        [SerializeField] private float menuSwipeMinDistancePx = 150f;
        [SerializeField] private float menuSwipeMaxDurationSeconds = 0.45f;
        [SerializeField] private float mobileButtonAlpha = 0.35f;
        [SerializeField] private float mobileButtonAlphaPressed = 0.6f;
        [SerializeField] private float menuUiScale = 3.75f;
        [SerializeField] private float menuUiScaleFactor = 0.75f;
        [SerializeField] private float mobileMenuScaleMultiplier = 1.35f;
        [SerializeField] private float mobileTouchControlsScaleMultiplier = 1.35f;
        [SerializeField] private float cameraToggleButtonSizePx = 72f;
        [SerializeField] private float cameraToggleButtonMarginPx = 16f;
        [SerializeField] private float touchFireXOffset = -1000f;
        [SerializeField] private float touchDpadXOffset = 1000f;
        [SerializeField] private bool logTouchLayout;
        [SerializeField] private string loadingPictureResourcesPath = "loadPicture";
        [SerializeField] private float startupLoadingMinSeconds = 2f;
        [SerializeField] private float loadingMinSeconds = 2f;
        [Header("Startup Intro")]
        [SerializeField] private bool enableStartupIntro = true;
        [SerializeField] private float startupIntroDurationSeconds = 5.6f;
        [SerializeField] private string startupIntroAudioResourcesPath = "intro";

        private GUIStyle _mobileButtonStyle;
        private GUIStyle _mobileButtonPressedStyle;

        private Texture2D _touchCircleTex;
        private Texture2D _touchCirclePressedTex;
        private Texture2D _touchCircleRingTex;
        private Texture2D _touchCircleRingFireTex;
        private Texture2D _cameraIconTex;
        private Texture2D _loadingPictureTex;

        private bool _showLoadingSplash;
        private float _loadingHideAtRealtime;
        private bool _startupIntroActive;
        private float _startupIntroEndRealtime;
        private AudioSource _startupIntroAudioSource;
        private AudioClip _startupIntroAudioClip;

        private bool _countdownActive;
        private float _countdownStartRealtime;
        private const float CountdownDuration = 5f;
        private float _countdownPanoOrthoSize;
        private Vector3 _countdownPanoCenter;
        private Vector3 _countdownHeroTarget;
        private float _countdownHeroOrthoSize;

        private string _selectedTerrain;

        private enum MenuScreen
        {
            Main = 0,
            SetupMap = 1,
            SetupMode = 2,
            SetupDifficulty = 3,
            SetupTeamSize = 4,
            Records = 5,
            Settings = 6,
        }

        private const string IconScalePrefKey = "WormCrawler_IconScale";
        private const string ControlScalePrefKey = "WormCrawler_ControlScale";
        public static float UserIconScale { get; private set; } = 1f;
        public static float UserControlScale { get; private set; } = 1f;
        private bool _matchInProgress;

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

        private Texture2D _menuPanelTex;
        private Texture2D _menuBtnNormalTex;
        private Texture2D _menuBtnSelectedTex;
        private Texture2D _menuBtnNavTex;
        private float _menuAnimTime;
        private Vector2 _menuTouchScrollVel;
        private bool _menuTouchDragging;
        private Vector2 _menuTouchStart;
        private float _menuTouchScrollStart;

        private bool _isDragging;
        private Vector2 _dragStartMouse;
        private Vector3 _dragStartCamPos;

        private bool _touchPanActive;
        private Vector2 _touchPanPrevPos;
        private bool _touchPinchActive;
        private float _touchPinchPrevDist;

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
            UserIconScale = PlayerPrefs.GetFloat(IconScalePrefKey, 1f);
            UserControlScale = PlayerPrefs.GetFloat(ControlScalePrefKey, 1f);
            RefreshMapList();
            BeginStartupIntroSplash();

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
            if (_startupIntroAudioSource != null)
            {
                _startupIntroAudioSource.Stop();
            }
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
            UpdateLoadingSplashState();

            if (_showMainMenu || _showMapMenu)
            {
                if (_showMapMenu) HandleMapMenuKeyboard();
                if (_showMainMenu) HandleMainMenuKeyboard();
                return;
            }

            if (IsMobileUiEnabled())
            {
                UpdateLeftEdgeMenuSwipe();
            }

            if (_countdownActive)
            {
                return;
            }

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                _showPauseMenu = !_showPauseMenu;
                if (_showPauseMenu) _menuAnimTime = 0f;
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

                // Touch pan (1 finger drag) and pinch-zoom (2 fingers).
                if (_cam != null && _cam.orthographic)
                {
                    var ts = Touchscreen.current;
                    if (ts != null)
                    {
                        var touchCount = 0;
                        Vector2 t0Pos = default, t1Pos = default;
                        bool t0Pressed = false, t1Pressed = false;
                        for (var ti = 0; ti < ts.touches.Count && touchCount < 2; ti++)
                        {
                            var tc = ts.touches[ti];
                            if (tc.press.isPressed)
                            {
                                if (touchCount == 0) { t0Pos = tc.position.ReadValue(); t0Pressed = true; }
                                else { t1Pos = tc.position.ReadValue(); t1Pressed = true; }
                                touchCount++;
                            }
                        }

                        if (touchCount == 2 && t0Pressed && t1Pressed)
                        {
                            var curDist = Vector2.Distance(t0Pos, t1Pos);
                            if (_touchPinchActive)
                            {
                                if (_touchPinchPrevDist > 1f && curDist > 1f)
                                {
                                    var ratio = _touchPinchPrevDist / curDist;
                                    var maxOrtho = ComputeMaxCameraOrthoSize();
                                    _cam.orthographicSize = Mathf.Clamp(_cam.orthographicSize * ratio, 2f, maxOrtho);
                                }
                            }
                            _touchPinchActive = true;
                            _touchPinchPrevDist = curDist;
                            _touchPanActive = false;
                        }
                        else if (touchCount == 1 && t0Pressed)
                        {
                            _touchPinchActive = false;
                            if (_touchPanActive)
                            {
                                var deltaPx = t0Pos - _touchPanPrevPos;
                                var worldPerPixel = (2f * _cam.orthographicSize) / Mathf.Max(1f, Screen.height);
                                _cam.transform.position -= new Vector3(deltaPx.x * worldPerPixel, deltaPx.y * worldPerPixel, 0f);
                            }
                            _touchPanActive = true;
                            _touchPanPrevPos = t0Pos;
                        }
                        else
                        {
                            _touchPinchActive = false;
                            _touchPanActive = false;
                        }
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

            var drawBackgroundImage = _showLoadingSplash || _showMapMenu || _showPauseMenu || _showMainMenu;
            if (drawBackgroundImage)
            {
                DrawLoadingBackgroundImage();
            }

            if (_showLoadingSplash)
            {
                if (_startupIntroActive)
                {
                    DrawIntroSkipButton();
                }
                return;
            }

            if (_countdownActive)
            {
                DrawCountdown();
                return;
            }

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
                DrawCameraToggleButton();
            }

            if (IsMobileUiEnabled())
            {
                DrawMobileTouchControls();
            }
        }

        private void UpdateLeftEdgeMenuSwipe()
        {
            if (_showLoadingSplash || _countdownActive || _showPauseMenu || _showMainMenu || _showMapMenu)
            {
                _menuSwipeTracking = false;
                return;
            }

            var ts = Touchscreen.current;
            if (ts == null)
            {
                _menuSwipeTracking = false;
                return;
            }

            var t = ts.primaryTouch;
            if (t == null)
            {
                _menuSwipeTracking = false;
                return;
            }

            var pos = t.position.ReadValue();
            if (t.press.wasPressedThisFrame)
            {
                if (pos.x <= Mathf.Max(8f, menuSwipeEdgeWidthPx))
                {
                    _menuSwipeTracking = true;
                    _menuSwipeStartPos = pos;
                    _menuSwipeStartTime = Time.unscaledTime;
                }
                else
                {
                    _menuSwipeTracking = false;
                }
                return;
            }

            if (!_menuSwipeTracking)
            {
                return;
            }

            if (!t.press.isPressed)
            {
                _menuSwipeTracking = false;
                return;
            }

            var dt = Time.unscaledTime - _menuSwipeStartTime;
            if (dt > Mathf.Max(0.05f, menuSwipeMaxDurationSeconds))
            {
                _menuSwipeTracking = false;
                return;
            }

            var delta = pos - _menuSwipeStartPos;
            var minDist = Mathf.Max(24f, menuSwipeMinDistancePx);
            if (delta.x >= minDist && Mathf.Abs(delta.y) <= Mathf.Max(48f, delta.x * 0.75f))
            {
                _menuSwipeTracking = false;
                OpenMainMenuFromSwipe();
            }
        }

        private void OpenMainMenuFromSwipe()
        {
            _showPauseMenu = false;
            _showMapMenu = false;
            _showMainMenu = true;
            _screen = MenuScreen.Main;
            _mainMenuSelectedIndex = 0;
            _menuAnimTime = 0f;
            IsMapMenuOpen = true;
        }

        private void DrawCameraToggleButton()
        {
            var size = Mathf.Clamp(cameraToggleButtonSizePx, 42f, 128f);
            var y = Mathf.Clamp(cameraToggleButtonMarginPx, 0f, Mathf.Max(0f, Screen.height - size));
            if (HeroAmmoCarousel.TryGetSharedHudIconLayout(out var iconSize, out var iconRowY))
            {
                size = Mathf.Max(24f, iconSize);
                y = Mathf.Clamp(iconRowY, 0f, Mathf.Max(0f, Screen.height - size));
            }

            var margin = Mathf.Clamp(cameraToggleButtonMarginPx, 6f, 64f);
            var rect = new Rect(margin, y, size, size);

            var prev = GUI.color;
            GUI.color = _followHero ? new Color(1f, 1f, 1f, 0.72f) : new Color(0.4f, 0.85f, 1f, 0.92f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = prev;

            var iconPad = size * 0.14f;
            var iconRect = new Rect(rect.x + iconPad, rect.y + iconPad, rect.width - iconPad * 2f, rect.height - iconPad * 2f);
            if (_cameraIconTex != null)
            {
                var iconColorPrev = GUI.color;
                GUI.color = _followHero ? new Color(0f, 0f, 0f, 0.92f) : new Color(0f, 0.20f, 0.35f, 1f);
                GUI.DrawTexture(iconRect, _cameraIconTex, ScaleMode.StretchToFill, alphaBlend: true);
                GUI.color = iconColorPrev;
            }

            if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
            {
                _followHero = !_followHero;
            }
        }

        private bool IsMobileUiEnabled()
        {
            if (forceMobileUi) return true;
            if (Application.isMobilePlatform) return true;
            if (showTouchControlsOnDesktop) return true;
            return Touchscreen.current != null;
        }

        private float GetMenuUiScale()
        {
            var uiScale = Mathf.Max(1f, menuUiScale) * Mathf.Clamp(menuUiScaleFactor, 0.1f, 3f);
            if (Application.isMobilePlatform || forceMobileUi)
            {
                uiScale *= Mathf.Clamp(mobileMenuScaleMultiplier, 0.5f, 3f);
            }
            return uiScale;
        }

        private void BeginLoadingSplash(float minSeconds)
        {
            _showLoadingSplash = true;
            var hideAt = Time.realtimeSinceStartup + Mathf.Max(0f, minSeconds);
            _loadingHideAtRealtime = Mathf.Max(_loadingHideAtRealtime, hideAt);
        }

        private void BeginStartupIntroSplash()
        {
            if (!enableStartupIntro)
            {
                BeginLoadingSplash(startupLoadingMinSeconds);
                return;
            }

            _startupIntroActive = true;
            _showLoadingSplash = true;
            var introDuration = Mathf.Max(0.1f, startupIntroDurationSeconds);
            _startupIntroEndRealtime = Time.realtimeSinceStartup + introDuration;
            _loadingHideAtRealtime = _startupIntroEndRealtime;

            EnsureStartupIntroAudioSource();
            if (_startupIntroAudioSource != null)
            {
                if (_startupIntroAudioClip == null && !string.IsNullOrEmpty(startupIntroAudioResourcesPath))
                {
                    _startupIntroAudioClip = Resources.Load<AudioClip>(startupIntroAudioResourcesPath);
                }

                if (_startupIntroAudioClip != null)
                {
                    _startupIntroAudioSource.clip = _startupIntroAudioClip;
                    _startupIntroAudioSource.loop = false;
                    _startupIntroAudioSource.Play();
                }
            }
        }

        private void EnsureStartupIntroAudioSource()
        {
            if (_startupIntroAudioSource != null)
            {
                return;
            }

            _startupIntroAudioSource = GetComponent<AudioSource>();
            if (_startupIntroAudioSource == null)
            {
                _startupIntroAudioSource = gameObject.AddComponent<AudioSource>();
            }
            _startupIntroAudioSource.playOnAwake = false;
            _startupIntroAudioSource.spatialBlend = 0f;
            _startupIntroAudioSource.volume = 1f;
        }

        private void EndStartupIntroSplash(bool skipped)
        {
            _startupIntroActive = false;
            _showLoadingSplash = false;
            _loadingHideAtRealtime = Time.realtimeSinceStartup;

            if (_startupIntroAudioSource != null)
            {
                if (skipped)
                {
                    _startupIntroAudioSource.Stop();
                }
            }
        }

        private void UpdateLoadingSplashState()
        {
            if (!_showLoadingSplash)
            {
                return;
            }

            if (_startupIntroActive)
            {
                if (Time.realtimeSinceStartup >= _startupIntroEndRealtime)
                {
                    EndStartupIntroSplash(skipped: false);
                }
                return;
            }

            if (Time.realtimeSinceStartup >= _loadingHideAtRealtime)
            {
                _showLoadingSplash = false;
            }
        }

        private void EnsureLoadingBackgroundTexture()
        {
            if (_loadingPictureTex != null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(loadingPictureResourcesPath))
            {
                _loadingPictureTex = Resources.Load<Texture2D>(loadingPictureResourcesPath);
            }

            if (_loadingPictureTex == null)
            {
                _loadingPictureTex = Resources.Load<Texture2D>("Levels/island_sky");
            }
        }

        private void DrawLoadingBackgroundImage()
        {
            EnsureLoadingBackgroundTexture();
            if (_loadingPictureTex == null)
            {
                return;
            }

            var prevColor = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _loadingPictureTex, ScaleMode.ScaleAndCrop);
            GUI.color = prevColor;
        }

        private void DrawIntroSkipButton()
        {
            var elapsed = Mathf.Max(0f, Time.realtimeSinceStartup - (_startupIntroEndRealtime - Mathf.Max(0.1f, startupIntroDurationSeconds)));
            var blink = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(elapsed * 4.5f));

            var btnW = Mathf.Clamp(Screen.width * 0.26f, 180f, 360f);
            var btnH = Mathf.Clamp(Screen.height * 0.08f, 42f, 72f);
            var x = (Screen.width - btnW) * 0.5f;
            var y = Screen.height - btnH - Mathf.Clamp(Screen.height * 0.04f, 18f, 48f);
            var rect = new Rect(x, y, btnW, btnH);

            var prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.25f * blink);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = prev;

            var style = new GUIStyle(GUI.skin.button);
            style.fontStyle = FontStyle.Bold;
            style.fontSize = Mathf.Clamp(Mathf.RoundToInt(btnH * 0.45f), 18, 34);

            var label = "Пропустить";
            var pressed = GUI.Button(rect, label, style);
            if (pressed)
            {
                EndStartupIntroSplash(skipped: true);
            }
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

            if (_cameraIconTex == null)
            {
                _cameraIconTex = GenerateCameraIconTexture(96);
            }
        }

        private static Texture2D GenerateCameraIconTexture(int size)
        {
            size = Mathf.Clamp(size, 32, 256);
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, mipChain: false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            var clear = new Color(0f, 0f, 0f, 0f);
            var white = new Color(1f, 1f, 1f, 1f);
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    tex.SetPixel(x, y, clear);
                }
            }

            var bodyX0 = Mathf.RoundToInt(size * 0.13f);
            var bodyX1 = Mathf.RoundToInt(size * 0.87f);
            var bodyY0 = Mathf.RoundToInt(size * 0.28f);
            var bodyY1 = Mathf.RoundToInt(size * 0.78f);
            for (var y = bodyY0; y <= bodyY1; y++)
            {
                for (var x = bodyX0; x <= bodyX1; x++)
                {
                    tex.SetPixel(x, y, white);
                }
            }

            var topX0 = Mathf.RoundToInt(size * 0.30f);
            var topX1 = Mathf.RoundToInt(size * 0.56f);
            var topY0 = Mathf.RoundToInt(size * 0.16f);
            var topY1 = Mathf.RoundToInt(size * 0.30f);
            for (var y = topY0; y <= topY1; y++)
            {
                for (var x = topX0; x <= topX1; x++)
                {
                    tex.SetPixel(x, y, white);
                }
            }

            var cx = size * 0.50f;
            var cy = size * 0.53f;
            var rOuter = size * 0.20f;
            var rInner = size * 0.10f;
            var rOuter2 = rOuter * rOuter;
            var rInner2 = rInner * rInner;
            for (var y = bodyY0; y <= bodyY1; y++)
            {
                for (var x = bodyX0; x <= bodyX1; x++)
                {
                    var dx = x - cx;
                    var dy = y - cy;
                    var d2 = dx * dx + dy * dy;
                    if (d2 <= rOuter2)
                    {
                        tex.SetPixel(x, y, clear);
                    }
                    if (d2 <= rInner2)
                    {
                        tex.SetPixel(x, y, white);
                    }
                }
            }

            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return tex;
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
            var touchScale = (Application.isMobilePlatform || forceMobileUi)
                ? Mathf.Clamp(mobileTouchControlsScaleMultiplier, 0.5f, 3f)
                : 1f;

            var ctrlScale = Mathf.Clamp(UserControlScale, 0.5f, 1f);
            const float marginPx = 8f;
            var fireSizePx = 510f * s * touchScale * ctrlScale;
            var dPadBtnPx = 285f * s * touchScale * ctrlScale;
            var dPadGapPx = 81f * s * touchScale * ctrlScale;

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

            _mobileFireRect = fireRect;
            _mobileFireTouchHeld = IsTouchInsideRect(fireRect);
            var pressedFire = DrawRoundMobileButton(fireRect, string.Empty, keyFire, repeat: true, ringTex: _touchCircleRingFireTex);
            var pressedUp = DrawRoundMobileButton(upRect, string.Empty, keyUp, repeat: true, arrowDirection: Vector2.up);
            var pressedLeft = DrawRoundMobileButton(leftRect, string.Empty, keyLeft, repeat: true, arrowDirection: Vector2.left);
            var pressedRight = DrawRoundMobileButton(rightRect, string.Empty, keyRight, repeat: true, arrowDirection: Vector2.right);
            var pressedDown = DrawRoundMobileButton(downRect, string.Empty, keyDown, repeat: true, arrowDirection: Vector2.down);

            if (Event.current.type == EventType.Repaint)
            {
                var fireHeld = pressedFire || keyFire || _mobileFireTouchHeld;
                ApplyMobileInputs(ap, pressedLeft || keyLeft, pressedRight || keyRight, pressedUp || keyUp, pressedDown || keyDown, fireHeld);
            }
        }

        private bool DrawRoundMobileButton(Rect r, string label, bool keyboardPressed, bool repeat, Texture2D ringTex = null, Vector2 arrowDirection = default)
        {
            var colPrev = GUI.color;

            // Use touch-based detection for multitouch support instead of GUI.RepeatButton
            // which only tracks a single touch.
            var touchHeld = IsTouchInsideRect(r);
            var guiPressed = repeat ? GUI.RepeatButton(r, GUIContent.none, GUIStyle.none) : GUI.Button(r, GUIContent.none, GUIStyle.none);
            var pressedNow = touchHeld || guiPressed;
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

            if (arrowDirection.sqrMagnitude > 0.0001f)
            {
                DrawTouchArrowGlyph(r, arrowDirection, isPressed);
            }
            else
            {
                var prevAlign = _mobileButtonStyle.alignment;
                _mobileButtonStyle.alignment = TextAnchor.MiddleCenter;
                if (!string.IsNullOrEmpty(label))
                {
                    GUI.Label(r, label, _mobileButtonStyle);
                }
                _mobileButtonStyle.alignment = prevAlign;
            }

            return pressedNow;
        }

        private static void DrawTouchArrowGlyph(Rect r, Vector2 logicalDir, bool pressed)
        {
            if (logicalDir.sqrMagnitude < 0.0001f)
            {
                return;
            }

            var prevColor = GUI.color;
            GUI.color = pressed ? new Color(1f, 1f, 1f, 0.98f) : new Color(1f, 1f, 1f, 0.92f);

            var size = Mathf.Min(r.width, r.height);
            var shaftLen = size * 0.32f;
            var shaftThickness = Mathf.Max(5f, size * 0.11f);
            var headLen = size * 0.16f;
            var headSpread = size * 0.12f;

            var center = new Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.5f);

            // GUI has down-positive Y; convert logical world-like direction to GUI direction.
            var dir = new Vector2(logicalDir.x, -logicalDir.y).normalized;
            var side = new Vector2(-dir.y, dir.x);

            var tail = center - dir * (shaftLen * 0.5f);
            var tip = center + dir * (shaftLen * 0.5f);
            var headBase = tip - dir * headLen;

            DrawGuiLine(tail, tip, shaftThickness);
            DrawGuiLine(tip, headBase + side * headSpread, shaftThickness);
            DrawGuiLine(tip, headBase - side * headSpread, shaftThickness);

            GUI.color = prevColor;
        }

        private static void DrawGuiLine(Vector2 from, Vector2 to, float thickness)
        {
            var delta = to - from;
            var len = delta.magnitude;
            if (len < 0.001f)
            {
                return;
            }

            var prevMatrix = GUI.matrix;
            var angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            GUIUtility.RotateAroundPivot(angle, from);
            GUI.DrawTexture(new Rect(from.x, from.y - thickness * 0.5f, len, thickness), Texture2D.whiteTexture);
            GUI.matrix = prevMatrix;
        }

        private static bool IsTouchInsideRect(Rect guiRect)
        {
            var ts = Touchscreen.current;
            if (ts == null) return false;
            foreach (var touch in ts.touches)
            {
                if (!touch.press.isPressed) continue;
                var tp = touch.position.ReadValue();
                var guiPos = new Vector2(tp.x, Screen.height - tp.y);
                if (guiRect.Contains(guiPos)) return true;
            }
            return false;
        }

        private bool _mobileFireWasPressed;
        private Rect _mobileFireRect;
        private bool _mobileFireTouchHeld;
        private bool _mobileAimOverrideActive;
        private int _mobileAimOverridePlayerInstanceId;
        private int _mobileAimFacingSign = 1;
        private bool _menuSwipeTracking;
        private Vector2 _menuSwipeStartPos;
        private float _menuSwipeStartTime;

        private void ApplyMobileInputs(Transform ap, bool left, bool right, bool up, bool down, bool fire)
        {
            // If the active player changed, reset mobile aim override state.
            var apInstanceId = ap.GetInstanceID();
            if (_mobileAimOverrideActive && apInstanceId != _mobileAimOverridePlayerInstanceId)
            {
                var prevAim = ap.GetComponent<WormAimController>();
                if (prevAim != null) prevAim.SetExternalAimOverride(false, Vector2.right);
                _mobileAimOverrideActive = false;
                _mobileAimFacingSign = 1;
            }
            _mobileAimOverridePlayerInstanceId = apInstanceId;

            var grenade = ap.GetComponent<HeroGrenadeThrower>();
            var grenadeEnabled = grenade != null && grenade.Enabled;

            var walker = ap.GetComponent<HeroSurfaceWalker>();
            if (walker != null)
            {
                var h = 0f;
                if (left && !right) h = -1f;
                else if (right && !left) h = 1f;

                walker.SetAdditionalMoveInput((left || right), h);
            }

            var grapple = ap.GetComponent<GrappleController>();
            var ropeAttached = grapple != null && grapple.IsAttached;
            var ropeGrenadeMode = ropeAttached && grenadeEnabled;
            if (grapple != null)
            {
                var moveH = 0f;
                if (left && !right) moveH = -1f;
                else if (right && !left) moveH = 1f;

                var moveV = 0f;
                if (!ropeGrenadeMode)
                {
                    if (up && !down) moveV = 1f;
                    else if (down && !up) moveV = -1f;
                }

                grapple.SetAdditionalMoveInput((left || right || up || down), moveH, moveV);
            }

            var aim = ap.GetComponent<WormAimController>();
            var aimDir = aim != null ? aim.AimDirection : Vector2.right;
            if (Mathf.Abs(aimDir.x) > 0.15f)
            {
                _mobileAimFacingSign = aimDir.x >= 0f ? 1 : -1;
            }

            if (aim != null)
            {
                var wantAimAdjust = up || down;
                var wantFacing = left ^ right;

                if (ropeAttached && !grenadeEnabled)
                {
                    // Rope movement mode: allow aim rotation with up/down while attached.
                    if (wantAimAdjust)
                    {
                        var d = aimDir.sqrMagnitude > 0.0001f ? aimDir.normalized : Vector2.right;
                        var facingSign = 1f;
                        if (wantFacing)
                        {
                            facingSign = right ? 1f : -1f;
                        }
                        else if (Mathf.Abs(d.x) > 0.15f)
                        {
                            facingSign = d.x >= 0f ? 1f : -1f;
                        }
                        else
                        {
                            facingSign = _mobileAimFacingSign;
                        }

                        var inputV = 0f;
                        if (up && !down) inputV = 1f;
                        else if (down && !up) inputV = -1f;

                        var curDeg = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                        curDeg = (curDeg % 360f + 360f) % 360f;
                        var baseDeg = facingSign >= 0f ? 0f : 180f;
                        var relDeg = Mathf.DeltaAngle(baseDeg, curDeg);
                        relDeg = Mathf.Clamp(relDeg, -90f, 90f);

                        relDeg += inputV * facingSign * 220f * Time.deltaTime;
                        relDeg = Mathf.Clamp(relDeg, -90f, 90f);

                        var outRad = (baseDeg + relDeg) * Mathf.Deg2Rad;
                        var outDir = new Vector2(Mathf.Cos(outRad), Mathf.Sin(outRad));
                        if (outDir.sqrMagnitude < 0.0001f)
                        {
                            outDir = facingSign >= 0f ? Vector2.right : Vector2.left;
                        }

                        aim.SetExternalAimOverride(true, outDir.normalized);
                        _mobileAimFacingSign = facingSign >= 0f ? 1 : -1;
                        _mobileAimOverrideActive = true;
                        aimDir = outDir.normalized;
                    }
                    else if (wantFacing)
                    {
                        // On rope: keep the current relative angle when switching facing side,
                        // instead of resetting to horizon.
                        var d = aimDir.sqrMagnitude > 0.0001f ? aimDir.normalized : Vector2.right;
                        var newFacingSign = right ? 1f : -1f;
                        var curDeg = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                        curDeg = (curDeg % 360f + 360f) % 360f;
                        var oldBaseDeg = _mobileAimFacingSign >= 0 ? 0f : 180f;
                        var relDeg = Mathf.Clamp(Mathf.DeltaAngle(oldBaseDeg, curDeg), -90f, 90f);

                        var newBaseDeg = newFacingSign >= 0f ? 0f : 180f;
                        var outRad = (newBaseDeg + relDeg) * Mathf.Deg2Rad;
                        var outDir = new Vector2(Mathf.Cos(outRad), Mathf.Sin(outRad));
                        if (outDir.sqrMagnitude < 0.0001f) outDir = newFacingSign >= 0f ? Vector2.right : Vector2.left;

                        aim.SetExternalAimOverride(true, outDir.normalized);
                        _mobileAimFacingSign = newFacingSign >= 0f ? 1 : -1;
                        _mobileAimOverrideActive = true;
                        aimDir = outDir.normalized;
                    }
                    else if (_mobileAimOverrideActive)
                    {
                        // Keep last aim direction while on rope.
                    }
                }
                else if (wantAimAdjust)
                {
                    var d = aimDir.sqrMagnitude > 0.0001f ? aimDir.normalized : Vector2.right;
                    var prevFacingSign = _mobileAimFacingSign;
                    var facingSign = 1f;
                    if (wantFacing)
                    {
                        facingSign = right ? 1f : -1f;
                    }
                    else if (Mathf.Abs(d.x) > 0.15f)
                    {
                        facingSign = d.x >= 0f ? 1f : -1f;
                    }
                    else
                    {
                        facingSign = _mobileAimFacingSign;
                    }

                    var inputV = 0f;
                    if (up && !down) inputV = 1f;
                    else if (down && !up) inputV = -1f;

                    var curDeg = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                    curDeg = (curDeg % 360f + 360f) % 360f;
                    var baseDeg = facingSign >= 0f ? 0f : 180f;
                    var relDeg = Mathf.DeltaAngle(baseDeg, curDeg);
                    relDeg = Mathf.Clamp(relDeg, -90f, 90f);

                    // Touch should match keyboard behavior: when changing facing side,
                    // start from horizon of the new side, then continue in that hemisphere.
                    if (wantFacing && Mathf.Sign(facingSign) != Mathf.Sign(prevFacingSign))
                    {
                        relDeg = 0f;
                    }

                    relDeg += inputV * facingSign * 220f * Time.deltaTime;
                    relDeg = Mathf.Clamp(relDeg, -90f, 90f);

                    var outRad = (baseDeg + relDeg) * Mathf.Deg2Rad;
                    var outDir = new Vector2(Mathf.Cos(outRad), Mathf.Sin(outRad));
                    if (outDir.sqrMagnitude < 0.0001f)
                    {
                        outDir = facingSign >= 0f ? Vector2.right : Vector2.left;
                    }

                    aim.SetExternalAimOverride(true, outDir.normalized);
                    _mobileAimFacingSign = facingSign >= 0f ? 1 : -1;
                    _mobileAimOverrideActive = true;
                    aimDir = outDir.normalized;
                }
                else if (wantFacing)
                {
                    var sign = right ? 1f : -1f;
                    var d = aimDir.sqrMagnitude > 0.0001f ? aimDir.normalized : Vector2.right;
                    var prevSign = _mobileAimFacingSign;

                    Vector2 outDir;
                    if (Mathf.Sign(sign) != Mathf.Sign(prevSign))
                    {
                        // On side switch, reset aim exactly to horizon of new facing side.
                        outDir = sign > 0f ? Vector2.right : Vector2.left;
                    }
                    else
                    {
                        // Same side: keep current relative angle inside this hemisphere.
                        var curDeg = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                        curDeg = (curDeg % 360f + 360f) % 360f;
                        var baseDeg = sign >= 0f ? 0f : 180f;
                        var relDeg = Mathf.Clamp(Mathf.DeltaAngle(baseDeg, curDeg), -90f, 90f);
                        var outRad = (baseDeg + relDeg) * Mathf.Deg2Rad;
                        outDir = new Vector2(Mathf.Cos(outRad), Mathf.Sin(outRad));
                        if (outDir.sqrMagnitude < 0.0001f) outDir = sign > 0f ? Vector2.right : Vector2.left;
                    }

                    aim.SetExternalAimOverride(true, outDir.normalized);
                    _mobileAimFacingSign = sign >= 0f ? 1 : -1;
                    _mobileAimOverrideActive = true;
                    aimDir = outDir.normalized;
                }
                else if (_mobileAimOverrideActive)
                {
                    // No touch directional input: keep override active with the last
                    // aim direction so the reticle stays where the player left it
                    // instead of snapping back to horizon.
                }
            }

            if (fire && !_mobileFireWasPressed)
            {
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
            _menuAnimTime = 0f;
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
            EnsureMenuTextures();
            _menuAnimTime += Time.deltaTime;

            var sw = (float)Screen.width;
            var sh = (float)Screen.height;
            var mobile = IsMobileUiEnabled();
            var uiScale = GetMenuUiScale();

            // Animated entrance.
            var t = Mathf.Clamp01(_menuAnimTime * 3.5f);
            var ease = 1f - (1f - t) * (1f - t);
            var alpha = ease;

            // Full-screen dark overlay.
            var prevColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, alpha * 0.5f);
            GUI.DrawTexture(new Rect(0f, 0f, sw, sh), Texture2D.whiteTexture);
            GUI.color = prevColor;

            // Panel.
            var panelW = Mathf.Min(sw * 0.92f, 700f * uiScale);
            var panelH = Mathf.Min(sh * 0.88f, 600f * uiScale);
            var panelX = (sw - panelW) * 0.5f;
            var panelY = Mathf.Lerp((sh - panelH) * 0.5f - 30f, (sh - panelH) * 0.5f, ease);
            var panelRect = new Rect(panelX, panelY, panelW, panelH);

            GUI.color = new Color(1f, 1f, 1f, alpha);
            if (_menuPanelTex != null) GUI.DrawTexture(panelRect, _menuPanelTex);

            var borderW = Mathf.Max(3f, panelW * 0.005f);
            var pulse = 0.85f + 0.15f * Mathf.Sin(_menuAnimTime * 3.5f);
            var borderCol = new Color(0.4f * pulse, 0.7f * pulse, 1f * pulse, alpha * 0.9f);
            DrawMenuRectBorder(panelRect, borderW, borderCol);

            var pad = Mathf.Max(12f, panelW * 0.03f);
            var btnFontSize = Mathf.Clamp(Mathf.RoundToInt(panelH * 0.042f), 16, 36);
            var navBtnFontSize = Mathf.Clamp(Mathf.RoundToInt(panelH * 0.055f), 18, 40);
            var navBtnH = Mathf.Clamp(panelH * 0.12f, 48f, 80f);
            var navBtnW = Mathf.Max(panelW * 0.28f, 100f);
            var itemBtnH = Mathf.Clamp(panelH * 0.10f, 40f, 70f);
            var itemBtnW = Mathf.Clamp(panelW * 0.35f, 100f, 260f);
            var itemGap = Mathf.Max(8f, pad * 0.5f);

            var navY = panelRect.yMax - navBtnH - pad;

            if (_screen == MenuScreen.Main)
            {
                DrawMenuTitle(panelRect, "Main Menu", alpha, pad);

                var gap = Mathf.Max(12f, panelH * 0.04f);
                var mainBtnW = Mathf.Min(panelW - pad * 2f, 320f * uiScale);
                var mainBtnH = Mathf.Clamp(panelH * 0.13f, 50f, 80f);
                var mainBtnX = panelRect.x + (panelW - mainBtnW) * 0.5f;

                var btnCount = _matchInProgress ? 5 : 4;
                var totalH = mainBtnH * btnCount + gap * (btnCount - 1);
                var startY = panelRect.y + (panelH - totalH) * 0.5f + panelH * 0.06f;
                var row = 0;

                if (_matchInProgress)
                {
                    var texBack = _mainMenuSelectedIndex == row ? _menuBtnSelectedTex : _menuBtnNavTex;
                    if (DrawMenuCartoonButton(new Rect(mainBtnX, startY + (mainBtnH + gap) * row, mainBtnW, mainBtnH), "Back", texBack, navBtnFontSize, alpha))
                    {
                        _mainMenuSelectedIndex = row;
                        CloseMainMenu();
                    }
                    row++;
                }

                var tex0 = _mainMenuSelectedIndex == row ? _menuBtnSelectedTex : _menuBtnNormalTex;
                if (DrawMenuCartoonButton(new Rect(mainBtnX, startY + (mainBtnH + gap) * row, mainBtnW, mainBtnH), "New Game", tex0, navBtnFontSize, alpha))
                {
                    _mainMenuSelectedIndex = row;
                    StartSetupWizard();
                }
                row++;

                var texSet = _mainMenuSelectedIndex == row ? _menuBtnSelectedTex : _menuBtnNormalTex;
                if (DrawMenuCartoonButton(new Rect(mainBtnX, startY + (mainBtnH + gap) * row, mainBtnW, mainBtnH), "Settings", texSet, navBtnFontSize, alpha))
                {
                    _mainMenuSelectedIndex = row;
                    _screen = MenuScreen.Settings;
                }
                row++;

                var tex1 = _mainMenuSelectedIndex == row ? _menuBtnSelectedTex : _menuBtnNormalTex;
                if (DrawMenuCartoonButton(new Rect(mainBtnX, startY + (mainBtnH + gap) * row, mainBtnW, mainBtnH), "Records", tex1, navBtnFontSize, alpha))
                {
                    _mainMenuSelectedIndex = row;
                    _screen = MenuScreen.Records;
                }
                row++;

                var tex2 = _mainMenuSelectedIndex == row ? _menuBtnSelectedTex : _menuBtnNormalTex;
                if (DrawMenuCartoonButton(new Rect(mainBtnX, startY + (mainBtnH + gap) * row, mainBtnW, mainBtnH), "Exit", tex2, navBtnFontSize, alpha))
                {
                    _mainMenuSelectedIndex = row;
                    ExitGame();
                }

                GUI.color = prevColor;
                return;
            }

            if (_screen == MenuScreen.Settings)
            {
                DrawMenuTitle(panelRect, "Settings", alpha, pad);

                var sliderLabelW = Mathf.Min(panelW * 0.40f, 200f);
                var sliderW = Mathf.Min(panelW - pad * 2f - sliderLabelW - 12f, 400f);
                var sliderH = Mathf.Clamp(panelH * 0.08f, 36f, 60f);
                var sliderX = panelRect.x + pad + sliderLabelW + 8f;
                var sliderLabelX = panelRect.x + pad;
                var rowH = sliderH * 1.6f;
                var sY = panelRect.y + panelH * 0.28f;

                var lblStyle = new GUIStyle(GUI.skin.label);
                lblStyle.fontSize = Mathf.Clamp(Mathf.RoundToInt(panelH * 0.038f), 14, 28);
                lblStyle.alignment = TextAnchor.MiddleLeft;

                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.Label(new Rect(sliderLabelX, sY, sliderLabelW, sliderH), $"Icons: x{UserIconScale:0.0}", lblStyle);
                var newIconScale = GUI.HorizontalSlider(new Rect(sliderX, sY + sliderH * 0.3f, sliderW, sliderH * 0.4f), UserIconScale, 1f, 2.5f);
                newIconScale = Mathf.Round(newIconScale * 10f) / 10f;
                if (Mathf.Abs(newIconScale - UserIconScale) > 0.01f)
                {
                    UserIconScale = newIconScale;
                    PlayerPrefs.SetFloat(IconScalePrefKey, UserIconScale);
                    PlayerPrefs.Save();
                }
                sY += rowH;

                GUI.Label(new Rect(sliderLabelX, sY, sliderLabelW, sliderH), $"Controls: x{UserControlScale:0.0}", lblStyle);
                var newCtrlScale = GUI.HorizontalSlider(new Rect(sliderX, sY + sliderH * 0.3f, sliderW, sliderH * 0.4f), UserControlScale, 0.5f, 1f);
                newCtrlScale = Mathf.Round(newCtrlScale * 10f) / 10f;
                if (Mathf.Abs(newCtrlScale - UserControlScale) > 0.01f)
                {
                    UserControlScale = newCtrlScale;
                    PlayerPrefs.SetFloat(ControlScalePrefKey, UserControlScale);
                    PlayerPrefs.Save();
                }

                if (DrawMenuCartoonButton(new Rect(panelRect.x + pad, navY, navBtnW, navBtnH), "Back", _menuBtnNormalTex, navBtnFontSize, alpha))
                {
                    _screen = MenuScreen.Main;
                }

                GUI.color = prevColor;
                return;
            }

            if (_screen == MenuScreen.SetupMap)
            {
                DrawMenuTitle(panelRect, "Step 1: Choose Map", alpha, pad);

                // Horizontal scrollable list of map buttons.
                var listTop = panelRect.y + pad * 2f + panelH * 0.10f;
                var listBottom = navY - pad * 0.5f;
                var listH = Mathf.Max(itemBtnH + 8f, listBottom - listTop);
                var listCenterY = listTop + (listH - itemBtnH) * 0.5f;

                var totalContentW = Mathf.Max(1f, _mapDisplayNames.Count * (itemBtnW + itemGap) - itemGap);
                var scrollRect = new Rect(panelRect.x + pad, listTop, panelW - pad * 2f, listH);
                var contentRect = new Rect(0f, 0f, Mathf.Max(scrollRect.width, totalContentW), listH);

                _mapScroll = GUI.BeginScrollView(scrollRect, _mapScroll, contentRect, true, false);
                for (var i = 0; i < _mapDisplayNames.Count; i++)
                {
                    var bx = i * (itemBtnW + itemGap);
                    var by = (listH - itemBtnH) * 0.5f;
                    var br = new Rect(bx, by, itemBtnW, itemBtnH);
                    var tex = (i == _selectedMapIndex) ? _menuBtnSelectedTex : _menuBtnNormalTex;
                    if (DrawMenuCartoonButton(br, _mapDisplayNames[i], tex, btnFontSize, alpha, bold: i == _selectedMapIndex))
                    {
                        _selectedMapIndex = i;
                    }
                }
                GUI.EndScrollView();

                // Nav buttons.
                if (DrawMenuCartoonButton(new Rect(panelRect.x + pad, navY, navBtnW, navBtnH), "Back", _menuBtnNormalTex, navBtnFontSize, alpha))
                {
                    _screen = MenuScreen.Main;
                }

                var canNext = _selectedMapIndex >= 0 && _selectedMapIndex < _mapResourcePaths.Count;
                var nextTex = canNext ? _menuBtnNavTex : _menuBtnNormalTex;
                var nextAlpha = canNext ? alpha : alpha * 0.4f;
                if (DrawMenuCartoonButton(new Rect(panelRect.xMax - pad - navBtnW, navY, navBtnW, navBtnH), "Next", nextTex, navBtnFontSize, nextAlpha) && canNext)
                {
                    SetSelectedTerrain(_mapResourcePaths[_selectedMapIndex]);
                    _screen = MenuScreen.SetupMode;
                }

                GUI.color = prevColor;
                return;
            }

            if (_screen == MenuScreen.SetupMode)
            {
                DrawMenuTitle(panelRect, "Step 2: Mode", alpha, pad);

                // Horizontal row of mode buttons.
                var modeBtnW = Mathf.Min(panelW * 0.38f, 240f);
                var modeBtnH = Mathf.Clamp(panelH * 0.14f, 50f, 80f);
                var modeGap = Mathf.Max(12f, pad * 0.6f);
                var totalModeW = modeBtnW * 2f + modeGap;
                var modeX = panelRect.x + (panelW - totalModeW) * 0.5f;
                var modeY = panelRect.y + panelH * 0.38f;

                var texPlayer = !VsCpu ? _menuBtnSelectedTex : _menuBtnNormalTex;
                var texCpu = VsCpu ? _menuBtnSelectedTex : _menuBtnNormalTex;

                if (DrawMenuCartoonButton(new Rect(modeX, modeY, modeBtnW, modeBtnH), "Vs Player", texPlayer, btnFontSize, alpha))
                {
                    VsCpu = false;
                    PlayerPrefs.SetInt(VsCpuPrefKey, 0);
                    PlayerPrefs.Save();
                }
                if (DrawMenuCartoonButton(new Rect(modeX + modeBtnW + modeGap, modeY, modeBtnW, modeBtnH), "Vs CPU", texCpu, btnFontSize, alpha))
                {
                    VsCpu = true;
                    PlayerPrefs.SetInt(VsCpuPrefKey, 1);
                    PlayerPrefs.Save();
                }

                // Nav buttons.
                if (DrawMenuCartoonButton(new Rect(panelRect.x + pad, navY, navBtnW, navBtnH), "Back", _menuBtnNormalTex, navBtnFontSize, alpha))
                {
                    _screen = MenuScreen.SetupMap;
                }
                if (DrawMenuCartoonButton(new Rect(panelRect.xMax - pad - navBtnW, navY, navBtnW, navBtnH), "Next", _menuBtnNavTex, navBtnFontSize, alpha))
                {
                    _screen = VsCpu ? MenuScreen.SetupDifficulty : MenuScreen.SetupTeamSize;
                }

                GUI.color = prevColor;
                return;
            }

            if (_screen == MenuScreen.SetupDifficulty)
            {
                DrawMenuTitle(panelRect, "Step 3: CPU Difficulty", alpha, pad);

                // Horizontal row of difficulty buttons.
                var diffBtnW = Mathf.Min(panelW * 0.28f, 180f);
                var diffBtnH = Mathf.Clamp(panelH * 0.13f, 46f, 74f);
                var diffGap = Mathf.Max(10f, pad * 0.5f);
                var totalDiffW = diffBtnW * 3f + diffGap * 2f;
                var diffX = panelRect.x + (panelW - totalDiffW) * 0.5f;
                var diffY = panelRect.y + panelH * 0.38f;

                var texEasy = CpuDifficulty == WormCrawlerPrototype.AI.BotDifficulty.Easy ? _menuBtnSelectedTex : _menuBtnNormalTex;
                var texNorm = CpuDifficulty == WormCrawlerPrototype.AI.BotDifficulty.Normal ? _menuBtnSelectedTex : _menuBtnNormalTex;
                var texHard = CpuDifficulty == WormCrawlerPrototype.AI.BotDifficulty.Hard ? _menuBtnSelectedTex : _menuBtnNormalTex;

                if (DrawMenuCartoonButton(new Rect(diffX, diffY, diffBtnW, diffBtnH), "Easy", texEasy, btnFontSize, alpha))
                {
                    CpuDifficulty = WormCrawlerPrototype.AI.BotDifficulty.Easy;
                    PlayerPrefs.SetInt(CpuDifficultyPrefKey, (int)CpuDifficulty);
                    PlayerPrefs.Save();
                }
                if (DrawMenuCartoonButton(new Rect(diffX + diffBtnW + diffGap, diffY, diffBtnW, diffBtnH), "Normal", texNorm, btnFontSize, alpha))
                {
                    CpuDifficulty = WormCrawlerPrototype.AI.BotDifficulty.Normal;
                    PlayerPrefs.SetInt(CpuDifficultyPrefKey, (int)CpuDifficulty);
                    PlayerPrefs.Save();
                }
                if (DrawMenuCartoonButton(new Rect(diffX + (diffBtnW + diffGap) * 2f, diffY, diffBtnW, diffBtnH), "Hard", texHard, btnFontSize, alpha))
                {
                    CpuDifficulty = WormCrawlerPrototype.AI.BotDifficulty.Hard;
                    PlayerPrefs.SetInt(CpuDifficultyPrefKey, (int)CpuDifficulty);
                    PlayerPrefs.Save();
                }

                // Nav buttons.
                if (DrawMenuCartoonButton(new Rect(panelRect.x + pad, navY, navBtnW, navBtnH), "Back", _menuBtnNormalTex, navBtnFontSize, alpha))
                {
                    _screen = MenuScreen.SetupMode;
                }
                if (DrawMenuCartoonButton(new Rect(panelRect.xMax - pad - navBtnW, navY, navBtnW, navBtnH), "Next", _menuBtnNavTex, navBtnFontSize, alpha))
                {
                    _screen = MenuScreen.SetupTeamSize;
                }

                GUI.color = prevColor;
                return;
            }

            if (_screen == MenuScreen.SetupTeamSize)
            {
                var stepLabel = VsCpu ? "Step 4: Team Size" : "Step 3: Team Size";
                DrawMenuTitle(panelRect, stepLabel, alpha, pad);

                // Horizontal row of team size buttons.
                var tsBtnW = Mathf.Clamp(panelW * 0.15f, 56f, 120f);
                var tsBtnH = Mathf.Clamp(panelH * 0.13f, 46f, 74f);
                var tsGap = Mathf.Max(8f, pad * 0.4f);
                var totalTsW = tsBtnW * 5f + tsGap * 4f;
                var tsX = panelRect.x + (panelW - totalTsW) * 0.5f;
                var tsY = panelRect.y + panelH * 0.38f;

                for (var idx = 0; idx < 5; idx++)
                {
                    var sz = idx + 1;
                    var label = $"{sz}x{sz}";
                    var isSel = SelectedTeamSize == sz;
                    var tex = isSel ? _menuBtnSelectedTex : _menuBtnNormalTex;
                    if (DrawMenuCartoonButton(new Rect(tsX + (tsBtnW + tsGap) * idx, tsY, tsBtnW, tsBtnH), label, tex, btnFontSize, alpha, bold: isSel))
                    {
                        SelectedTeamSize = sz;
                        PlayerPrefs.SetInt(TeamSizePrefKey, SelectedTeamSize);
                        PlayerPrefs.Save();
                    }
                }

                // Nav buttons.
                if (DrawMenuCartoonButton(new Rect(panelRect.x + pad, navY, navBtnW, navBtnH), "Back", _menuBtnNormalTex, navBtnFontSize, alpha))
                {
                    _screen = VsCpu ? MenuScreen.SetupDifficulty : MenuScreen.SetupMode;
                }

                var startBtnTex = _menuBtnNavTex;
                if (DrawMenuCartoonButton(new Rect(panelRect.xMax - pad - navBtnW, navY, navBtnW, navBtnH), "Start!", startBtnTex, navBtnFontSize, alpha))
                {
                    CloseMainMenu();
                    if (_hasPendingScene && !string.IsNullOrEmpty(_pendingScene.path))
                    {
                        GenerateWorld(_pendingScene);
                        _hasPendingScene = false;
                    }
                }

                GUI.color = prevColor;
                return;
            }

            if (_screen == MenuScreen.Records)
            {
                DrawMenuTitle(panelRect, "Records", alpha, pad);

                var total = PlayerPrefs.GetInt("WormCrawler_TotalGames", 0);
                var w0 = PlayerPrefs.GetInt("WormCrawler_WinsTeam0", 0);
                var w1 = PlayerPrefs.GetInt("WormCrawler_WinsTeam1", 0);
                var last = PlayerPrefs.GetString("WormCrawler_LastDuel", "");

                var infoFontSize = Mathf.Clamp(Mathf.RoundToInt(panelH * 0.045f), 14, 32);
                var infoStyle = new GUIStyle(GUI.skin.label);
                infoStyle.fontSize = infoFontSize;
                infoStyle.alignment = TextAnchor.MiddleLeft;
                infoStyle.wordWrap = true;

                var lineH = infoFontSize * 2.2f;
                var infoX = panelRect.x + pad * 1.5f;
                var infoW = panelW - pad * 3f;
                var infoY = panelRect.y + panelH * 0.22f;

                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.Label(new Rect(infoX, infoY, infoW, lineH), $"Total games: {total}", infoStyle);
                infoY += lineH;
                GUI.Label(new Rect(infoX, infoY, infoW, lineH), $"Spider wins: {w0}", infoStyle);
                infoY += lineH;
                GUI.Label(new Rect(infoX, infoY, infoW, lineH), $"Red wins: {w1}", infoStyle);
                infoY += lineH * 1.2f;
                GUI.Label(new Rect(infoX, infoY, infoW, lineH * 2f), $"Last duel: {(string.IsNullOrEmpty(last) ? "-" : last)}", infoStyle);

                // Nav button.
                if (DrawMenuCartoonButton(new Rect(panelRect.x + pad, navY, navBtnW, navBtnH), "Back", _menuBtnNormalTex, navBtnFontSize, alpha))
                {
                    _screen = MenuScreen.Main;
                }

                GUI.color = prevColor;
                return;
            }

            GUI.color = prevColor;
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

        private void EnsureMenuTextures()
        {
            if (_menuPanelTex != null) return;

            _menuPanelTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _menuPanelTex.SetPixel(0, 0, new Color(0.08f, 0.06f, 0.18f, 0.5f));
            _menuPanelTex.Apply(false, true);

            _menuBtnNormalTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _menuBtnNormalTex.SetPixel(0, 0, new Color(0.14f, 0.12f, 0.28f, 0.92f));
            _menuBtnNormalTex.Apply(false, true);

            _menuBtnSelectedTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _menuBtnSelectedTex.SetPixel(0, 0, new Color(0.20f, 0.50f, 0.90f, 0.95f));
            _menuBtnSelectedTex.Apply(false, true);

            _menuBtnNavTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _menuBtnNavTex.SetPixel(0, 0, new Color(0.15f, 0.75f, 0.25f, 0.95f));
            _menuBtnNavTex.Apply(false, true);
        }

        private static void DrawMenuRectBorder(Rect rect, float w, Color col)
        {
            var prev = GUI.color;
            GUI.color = col;
            var tex = Texture2D.whiteTexture;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, w), tex);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - w, rect.width, w), tex);
            GUI.DrawTexture(new Rect(rect.x, rect.y, w, rect.height), tex);
            GUI.DrawTexture(new Rect(rect.xMax - w, rect.y, w, rect.height), tex);
            GUI.color = prev;
        }

        private bool DrawMenuCartoonButton(Rect rect, string label, Texture2D bgTex, int fontSize, float alpha, bool bold = true)
        {
            var prevColor = GUI.color;

            GUI.color = new Color(1f, 1f, 1f, alpha);
            if (bgTex != null)
            {
                GUI.DrawTexture(rect, bgTex);
            }

            var bw = Mathf.Max(2f, rect.width * 0.02f);
            DrawMenuRectBorder(rect, bw, new Color(1f, 1f, 1f, alpha * 0.6f));

            var style = new GUIStyle(GUI.skin.label);
            style.alignment = TextAnchor.MiddleCenter;
            style.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            style.fontSize = fontSize;
            style.wordWrap = false;
            style.clipping = TextClipping.Clip;

            GUI.color = new Color(0f, 0f, 0f, alpha * 0.8f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), label, style);

            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.Label(rect, label, style);

            GUI.color = new Color(1f, 1f, 1f, 0f);
            var clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);

            GUI.color = prevColor;
            return clicked;
        }

        private void DrawMenuTitle(Rect panelRect, string text, float alpha, float pad)
        {
            var gameFontSize = Mathf.Clamp(Mathf.RoundToInt(panelRect.height * 0.075f), 24, 58);
            var stepFontSize = Mathf.Clamp(Mathf.RoundToInt(panelRect.height * 0.032f), 14, 24);

            var gameStyle = new GUIStyle(GUI.skin.label);
            gameStyle.alignment = TextAnchor.MiddleCenter;
            gameStyle.fontStyle = FontStyle.Bold;
            gameStyle.fontSize = gameFontSize;
            gameStyle.wordWrap = true;

            var stepStyle = new GUIStyle(GUI.skin.label);
            stepStyle.alignment = TextAnchor.MiddleCenter;
            stepStyle.fontStyle = FontStyle.Bold;
            stepStyle.fontSize = stepFontSize;
            stepStyle.wordWrap = true;

            var gameH = gameFontSize * 1.55f;
            var gameRect = new Rect(panelRect.x + pad, panelRect.y + pad * 0.20f, panelRect.width - pad * 2f, gameH);
            var stepRect = new Rect(panelRect.x + pad, gameRect.yMax - gameFontSize * 0.12f, panelRect.width - pad * 2f, stepFontSize * 1.8f);

            var prevColor = GUI.color;
            var pulse = 0.85f + 0.15f * Mathf.Sin(_menuAnimTime * 3.6f);
            var wobble = 1.2f * Mathf.Sin(_menuAnimTime * 2.1f);
            var glowCol = new Color(0.12f * pulse, 0.75f * pulse, 1f, alpha * 0.78f);

            // Cartoon glow/outline layers.
            GUI.color = glowCol;
            GUI.Label(new Rect(gameRect.x - 2f, gameRect.y + 1f, gameRect.width, gameRect.height), MenuGameTitle, gameStyle);
            GUI.Label(new Rect(gameRect.x + 2f, gameRect.y + 1f, gameRect.width, gameRect.height), MenuGameTitle, gameStyle);
            GUI.Label(new Rect(gameRect.x, gameRect.y - 1f, gameRect.width, gameRect.height), MenuGameTitle, gameStyle);
            GUI.Label(new Rect(gameRect.x, gameRect.y + 3f, gameRect.width, gameRect.height), MenuGameTitle, gameStyle);

            // Main bright title with subtle vertical animation.
            GUI.color = new Color(1f, 0.98f, 0.86f, alpha);
            GUI.Label(new Rect(gameRect.x, gameRect.y + wobble, gameRect.width, gameRect.height), MenuGameTitle, gameStyle);

            // Step title (small subtitle).
            GUI.color = new Color(0f, 0f, 0f, alpha * 0.65f);
            GUI.Label(new Rect(stepRect.x + 1f, stepRect.y + 1f, stepRect.width, stepRect.height), text, stepStyle);
            GUI.color = new Color(0.92f, 0.96f, 1f, alpha * 0.95f);
            GUI.Label(stepRect, text, stepStyle);
            GUI.color = prevColor;
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

                _mapResourcePaths.Add($"{LevelsResourcesRoot}/{name}");
                _mapDisplayNames.Add(name);
            }

            if (_mapDisplayNames.Count > 1)
            {
                for (var i = 0; i < _mapDisplayNames.Count - 1; i++)
                {
                    for (var j = i + 1; j < _mapDisplayNames.Count; j++)
                    {
                        if (string.Compare(_mapDisplayNames[i], _mapDisplayNames[j], StringComparison.CurrentCultureIgnoreCase) > 0)
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
            var uiScale = GetMenuUiScale();

            var prevLabelFont = GUI.skin.label.fontSize;
            var prevButtonFont = GUI.skin.button.fontSize;
            GUI.skin.label.fontSize = Mathf.RoundToInt(prevLabelFont * uiScale);
            GUI.skin.button.fontSize = Mathf.RoundToInt(prevButtonFont * uiScale);

            var windowW = Mathf.Min(520f * uiScale, Screen.width - 40f);
            var windowH = Mathf.Min(520f * uiScale, Screen.height - 40f);
            var windowX = (Screen.width - windowW) * 0.5f;
            var windowY = (Screen.height - windowH) * 0.5f;
            var windowRect = new Rect(windowX, windowY, windowW, windowH);

            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            GUI.Box(windowRect, GUIContent.none);
            GUI.color = Color.white;

            var mapPad = Mathf.Max(10f, windowRect.width * 0.03f);
            DrawMenuTitle(windowRect, "Select map", 1f, mapPad);

            var inner = new Rect(windowRect.x + 10f * uiScale, windowRect.y + 92f * uiScale, windowRect.width - 20f * uiScale, windowRect.height - 102f * uiScale);
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
            EnsureMenuTextures();
            _menuAnimTime += Time.deltaTime;

            var sw = (float)Screen.width;
            var sh = (float)Screen.height;
            var uiScale = GetMenuUiScale();

            var t = Mathf.Clamp01(_menuAnimTime * 4f);
            var ease = 1f - (1f - t) * (1f - t);
            var alpha = ease;

            // Full-screen dark overlay.
            var prevColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, alpha * 0.5f);
            GUI.DrawTexture(new Rect(0f, 0f, sw, sh), Texture2D.whiteTexture);
            GUI.color = prevColor;

            // Panel.
            var panelW = Mathf.Min(sw * 0.75f, 460f * uiScale);
            var panelH = Mathf.Min(sh * 0.55f, 360f * uiScale);
            var panelX = (sw - panelW) * 0.5f;
            var panelY = Mathf.Lerp((sh - panelH) * 0.5f - 30f, (sh - panelH) * 0.5f, ease);
            var panelRect = new Rect(panelX, panelY, panelW, panelH);

            GUI.color = new Color(1f, 1f, 1f, alpha);
            if (_menuPanelTex != null) GUI.DrawTexture(panelRect, _menuPanelTex);

            var borderW = Mathf.Max(3f, panelW * 0.006f);
            var pulse = 0.85f + 0.15f * Mathf.Sin(_menuAnimTime * 3.5f);
            var borderCol = new Color(0.4f * pulse, 0.7f * pulse, 1f * pulse, alpha * 0.9f);
            DrawMenuRectBorder(panelRect, borderW, borderCol);

            var pad = Mathf.Max(12f, panelW * 0.04f);
            DrawMenuTitle(panelRect, "Pause", alpha, pad);

            var btnFontSize = Mathf.Clamp(Mathf.RoundToInt(panelH * 0.055f), 18, 40);
            var btnW = Mathf.Min(panelW - pad * 2f, 300f * uiScale);
            var btnH = Mathf.Clamp(panelH * 0.15f, 48f, 80f);
            var gap = Mathf.Max(10f, panelH * 0.04f);
            var totalBtnsH = btnH * 3f + gap * 2f;
            var btnX = panelRect.x + (panelW - btnW) * 0.5f;
            var btnY = panelRect.y + (panelH - totalBtnsH) * 0.5f + panelH * 0.08f;

            if (DrawMenuCartoonButton(new Rect(btnX, btnY, btnW, btnH), "Resume", _menuBtnNavTex, btnFontSize, alpha))
            {
                _showPauseMenu = false;
                IsMapMenuOpen = false;
            }
            btnY += btnH + gap;

            if (DrawMenuCartoonButton(new Rect(btnX, btnY, btnW, btnH), "Restart", _menuBtnNormalTex, btnFontSize, alpha))
            {
                _showPauseMenu = false;
                IsMapMenuOpen = false;
                RestartMatch();
            }
            btnY += btnH + gap;

            if (DrawMenuCartoonButton(new Rect(btnX, btnY, btnW, btnH), "Main Menu", _menuBtnNormalTex, btnFontSize, alpha))
            {
                _showPauseMenu = false;
                _showMainMenu = true;
                _screen = MenuScreen.Main;
                _mainMenuSelectedIndex = 0;
                _menuAnimTime = 0f;
                IsMapMenuOpen = true;
            }

            GUI.color = prevColor;
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

            if (_countdownActive && _cam != null && _cam.orthographic)
            {
                var elapsed = Time.realtimeSinceStartup - _countdownStartRealtime;
                var t = Mathf.Clamp01(elapsed / CountdownDuration);
                var ease = t * t * t * (t * (6f * t - 15f) + 10f); // smootherstep

                _cam.orthographicSize = Mathf.Lerp(_countdownPanoOrthoSize, _countdownHeroOrthoSize, ease);
                _cam.transform.position = Vector3.Lerp(_countdownPanoCenter, _countdownHeroTarget, ease);

                if (elapsed >= CountdownDuration)
                {
                    _countdownActive = false;
                    _cam.orthographicSize = _countdownHeroOrthoSize;
                    _cam.transform.position = _countdownHeroTarget;
                }
                return;
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

        private void DrawCountdown()
        {
            var elapsed = Time.realtimeSinceStartup - _countdownStartRealtime;
            var remaining = Mathf.Max(0f, CountdownDuration - elapsed);
            var number = Mathf.CeilToInt(remaining);
            if (number <= 0) number = 1;

            var label = number.ToString();
            var frac = remaining - Mathf.Floor(remaining);
            var scale = 1f + 0.3f * frac;

            var sw = (float)Screen.width;
            var sh = (float)Screen.height;

            var fontSize = Mathf.Clamp(Mathf.RoundToInt(sh * 0.18f * scale), 40, 300);
            var style = new GUIStyle(GUI.skin.label);
            style.alignment = TextAnchor.MiddleCenter;
            style.fontStyle = FontStyle.Bold;
            style.fontSize = fontSize;

            var w = sw * 0.5f;
            var h = fontSize * 1.6f;
            var r = new Rect((sw - w) * 0.5f, (sh - h) * 0.5f, w, h);

            var prevColor = GUI.color;

            // Shadow.
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.Label(new Rect(r.x + 3f, r.y + 3f, r.width, r.height), label, style);

            // Main.
            var pulse = 0.85f + 0.15f * Mathf.Sin(elapsed * 8f);
            GUI.color = new Color(1f, 0.95f * pulse, 0.3f * pulse, 1f);
            GUI.Label(r, label, style);

            GUI.color = prevColor;
        }

        private float ComputeMaxCameraOrthoSize()
        {
            var gen = _generator;
            if (gen == null)
            {
#if UNITY_6000_0_OR_NEWER
                gen = FindFirstObjectByType<SimpleWorldGenerator>();
#else
                gen = FindObjectOfType<SimpleWorldGenerator>();
#endif
            }

            if (gen != null)
            {
                var b = ComputeWorldBounds(gen.gameObject);
                var aspect = Mathf.Max(0.01f, (float)Screen.width / Mathf.Max(1f, Screen.height));
                var orthoFromHeight = b.extents.y + 2f;
                var orthoFromWidth = (b.extents.x + 2f) / aspect;
                return Mathf.Max(orthoFromHeight, orthoFromWidth);
            }

            return 30f;
        }

        private static Bounds ComputeWorldBounds(GameObject worldGO)
        {
            var colliders = worldGO.GetComponentsInChildren<Collider2D>(true);
            var hasColliderBounds = false;
            var bounds = new Bounds(worldGO.transform.position, Vector3.one);

            if (colliders != null)
            {
                for (var i = 0; i < colliders.Length; i++)
                {
                    var c = colliders[i];
                    if (c == null || !c.enabled || c.isTrigger)
                    {
                        continue;
                    }

                    if (!hasColliderBounds)
                    {
                        bounds = c.bounds;
                        hasColliderBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(c.bounds);
                    }
                }
            }

            if (hasColliderBounds)
            {
                return bounds;
            }

            var renderers = worldGO.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                return new Bounds(worldGO.transform.position, Vector3.one * 30f);
            }

            var foundRenderer = false;
            var b = new Bounds(worldGO.transform.position, Vector3.one * 30f);
            for (var i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null)
                {
                    continue;
                }

                // Ignore decorative background to frame by real terrain contour.
                if (r.gameObject.name == "Background")
                {
                    continue;
                }

                if (!foundRenderer)
                {
                    b = r.bounds;
                    foundRenderer = true;
                }
                else
                {
                    b.Encapsulate(r.bounds);
                }
            }

            if (!foundRenderer)
            {
                return new Bounds(worldGO.transform.position, Vector3.one * 30f);
            }

            return b;
        }

        private void GenerateWorld(Scene targetScene)
        {
            _matchInProgress = true;
            BeginLoadingSplash(loadingMinSeconds);
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
                    // Start countdown: panoramic overview → zoom to hero.
                    _countdownHeroOrthoSize = 15f;
                    _countdownHeroTarget = new Vector3(p.x, p.y, _cam.transform.position.z);

                    // Compute panoramic center from world bounds.
                    var worldBounds = ComputeWorldBounds(worldGO);
                    _countdownPanoCenter = new Vector3(worldBounds.center.x, worldBounds.center.y, _cam.transform.position.z);
                    var aspect = Mathf.Max(0.0001f, _cam.aspect);
                    var halfHeightByWidth = worldBounds.extents.x / aspect;
                    _countdownPanoOrthoSize = Mathf.Max(_countdownHeroOrthoSize, worldBounds.extents.y, halfHeightByWidth);

                    _cam.orthographicSize = _countdownPanoOrthoSize;
                    _cam.transform.position = _countdownPanoCenter;

                    _countdownActive = true;
                    _countdownStartRealtime = Time.realtimeSinceStartup;
                }
                else
                {
                    _cam.transform.position = new Vector3(p.x, p.y, _cam.transform.position.z);
                }
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
