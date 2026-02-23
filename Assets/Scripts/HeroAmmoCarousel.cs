using UnityEngine;
using UnityEngine.InputSystem;
using WormCrawlerPrototype.AI;

namespace WormCrawlerPrototype
{
    public sealed class HeroAmmoCarousel : MonoBehaviour
    {
        private enum AmmoSlot
        {
            Rope = 0,
            Grenade = 1,
            ClawGun = 2,
            Teleport = 3,
        }

        [SerializeField] private bool startWithGrenade = false;
        [SerializeField] private string ropeIconResourcesPath = "Icons/Rope";
        [SerializeField] private string grenadeIconResourcesPath = "Icons/Grenade";
        [SerializeField] private string clawGunIconResourcesPath = "Icons/auto_gun";
        [SerializeField] private string teleportIconResourcesPath = "Icons/Teleport";
        [SerializeField] private string clawGunSpritesheetResourcesPath = "Weapons/claw_gun";
        [SerializeField] private int clawGunFrameCount = 9;

        private Texture2D _grenadeIcon;
        private Texture2D _ropeIcon;
        private Texture2D _clawGunIcon;
        private Texture2D _teleportIcon;
        private Texture2D _clawGunSheet;
        private Rect _clawGunFrame0Uv;
        private Texture2D _borderTex;

        private AmmoSlot _selected;

        private GrappleController _grapple;
        private HeroGrenadeThrower _grenade;
        private HeroClawGun _clawGun;
        private HeroTeleport _teleport;
        private WormAimController _aim;
        private TurnManager _turn;

        private void Awake()
        {
            _grapple = GetComponent<GrappleController>();
            _grenade = GetComponent<HeroGrenadeThrower>();
            _clawGun = GetComponent<HeroClawGun>();
            _teleport = GetComponent<HeroTeleport>();
            if (_teleport == null)
            {
                _teleport = gameObject.AddComponent<HeroTeleport>();
            }
            _aim = GetComponent<WormAimController>();

            _turn = TurnManager.Instance;

            _selected = startWithGrenade ? AmmoSlot.Grenade : AmmoSlot.Rope;

            _grenadeIcon = ResolveIconTexture(grenadeIconResourcesPath);
            _ropeIcon = ResolveIconTexture(ropeIconResourcesPath);
            if (_ropeIcon == null)
            {
                _ropeIcon = GenerateRopeIcon();
            }
            _clawGunIcon = ResolveIconTexture(clawGunIconResourcesPath);
            _teleportIcon = ResolveIconTexture(teleportIconResourcesPath);
            if (_teleportIcon == null)
            {
                _teleportIcon = GenerateTeleportIcon();
            }
            if (_clawGunIcon == null)
            {
                _clawGunIcon = GenerateClawGunFallbackIcon();
            }
            ResolveClawGunIcon();

            _borderTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _borderTex.SetPixel(0, 0, Color.white);
            _borderTex.Apply(false, true);

            ApplySelection();
        }

        private void OnDestroy()
        {
        }

        private void Update()
        {
            if (Bootstrap.IsMapMenuOpen)
            {
                return;
            }

            if (Keyboard.current == null)
            {
                return;
            }

            if (Keyboard.current.digit1Key.wasPressedThisFrame)
            {
                _selected = CoerceSelection(AmmoSlot.Rope);
                ApplySelection();
            }
            else if (Keyboard.current.digit2Key.wasPressedThisFrame || Keyboard.current.numpad2Key.wasPressedThisFrame)
            {
                _selected = CoerceSelection(AmmoSlot.Grenade);
                ApplySelection();
            }
            else if (Keyboard.current.digit3Key.wasPressedThisFrame || Keyboard.current.numpad3Key.wasPressedThisFrame)
            {
                _selected = CoerceSelection(AmmoSlot.ClawGun);
                ApplySelection();
            }
            else if (Keyboard.current.digit4Key.wasPressedThisFrame || Keyboard.current.numpad4Key.wasPressedThisFrame)
            {
                _selected = CoerceSelection(AmmoSlot.Teleport);
                ApplySelection();
            }
        }

        private AmmoSlot CoerceSelection(AmmoSlot desired)
        {
            if (desired == AmmoSlot.ClawGun && _grapple != null && _grapple.IsAttached)
            {
                return AmmoSlot.Rope;
            }

            if (_turn != null)
            {
                if (desired == AmmoSlot.Grenade && !_turn.CanSelectWeapon(TurnManager.TurnWeapon.Grenade))
                {
                    return _selected;
                }
                if (desired == AmmoSlot.ClawGun && !_turn.CanSelectWeapon(TurnManager.TurnWeapon.ClawGun))
                {
                    return _selected;
                }
                if (desired == AmmoSlot.Teleport && !_turn.CanSelectWeapon(TurnManager.TurnWeapon.Teleport))
                {
                    return _selected;
                }
            }

            if (desired == AmmoSlot.Teleport)
            {
                if (_teleport == null || !_teleport.CanUseNow)
                {
                    return _selected;
                }
            }
            return desired;
        }

        private static Texture2D ResolveIconTexture(string resourcesPath)
        {
            if (string.IsNullOrEmpty(resourcesPath))
            {
                return null;
            }

            var s = Resources.Load<Sprite>(resourcesPath);
            if (s != null && s.texture != null)
            {
                return s.texture;
            }

            return Resources.Load<Texture2D>(resourcesPath);
        }

        private void ApplySelection()
        {
            _selected = CoerceSelection(_selected);

            var grenadeSelected = _selected == AmmoSlot.Grenade;
            var clawSelected = _selected == AmmoSlot.ClawGun;
            var teleportSelected = _selected == AmmoSlot.Teleport;
            var bot = GetComponent<SpiderBotController>();

            if (_aim != null)
            {
                _aim.SetAimClampEnabled(clawSelected, 60f);
                _aim.ShowReticle = !clawSelected;
            }

            if (_grapple != null)
            {
                _grapple.SetRopeHandVisible(_selected == AmmoSlot.Rope);

                // For AI worms, TurnManager manages grapple input; avoid disabling input here,
                // because GrappleController detaches when InputEnabled becomes false.
                if (bot == null)
                {
                    if (grenadeSelected)
                    {
                        _grapple.DetachWhenInputDisabled = false;
                        _grapple.InputEnabled = false;
                    }
                    else
                    {
                        _grapple.DetachWhenInputDisabled = true;
                        _grapple.InputEnabled = !clawSelected;
                    }
                }
            }

            if (_grenade != null)
            {
                _grenade.Enabled = grenadeSelected;
            }

            if (_clawGun != null)
            {
                _clawGun.Enabled = clawSelected;
            }

            if (_teleport != null)
            {
                _teleport.Enabled = teleportSelected;
            }
        }

        public void ForceSelectRope()
        {
            _selected = AmmoSlot.Rope;
            ApplySelection();
        }

        public void SelectGrenade()
        {
            _selected = CoerceSelection(AmmoSlot.Grenade);
            ApplySelection();
        }

        public void SelectClawGun()
        {
            _selected = CoerceSelection(AmmoSlot.ClawGun);
            ApplySelection();
        }

        public void SelectTeleport()
        {
            _selected = CoerceSelection(AmmoSlot.Teleport);
            ApplySelection();
        }

        private void OnGUI()
        {
            if (Bootstrap.IsMapMenuOpen)
            {
                return;
            }

            // Align with the top HUD row (TurnManager).
            var hudFont = Mathf.Clamp(Mathf.RoundToInt(Screen.height * 0.032f), 18, 44);
            var hudH = Mathf.Max(28f, hudFont * 1.35f);
            var pad = Mathf.Max(10f, hudFont * 0.4f);

            var iconSize = 44f * 3f;
            var gap = 14f * 3f;

            var leftRect = new Rect(pad, pad, Screen.width * 0.33f, hudH);
            var rightRect = new Rect(Screen.width - pad - Screen.width * 0.33f, pad, Screen.width * 0.33f, hudH);

            var availableW = Mathf.Max(0f, rightRect.xMin - leftRect.xMax);
            var baseTotalW = iconSize * 4f + gap * 3f;
            var scale = baseTotalW > 0.01f ? Mathf.Clamp01(availableW / baseTotalW) : 1f;
            if (scale < 1f)
            {
                iconSize *= scale;
                gap *= scale;
            }

            var totalW = iconSize * 4f + gap * 3f;
            var x0 = leftRect.xMax + (availableW - totalW) * 0.5f;
            var y0 = pad + (hudH - iconSize) * 0.5f;

            var ropeRect = new Rect(x0, y0, iconSize, iconSize);
            var grenadeRect = new Rect(x0 + (iconSize + gap) * 1f, y0, iconSize, iconSize);
            var clawRect = new Rect(x0 + (iconSize + gap) * 2f, y0, iconSize, iconSize);
            var tpRect = new Rect(x0 + (iconSize + gap) * 3f, y0, iconSize, iconSize);

            DrawHudIconButton(ropeRect, _ropeIcon, AmmoSlot.Rope);
            DrawGrenadeHudIconButton(grenadeRect, AmmoSlot.Grenade);
            DrawClawGunHudIconButton(clawRect, AmmoSlot.ClawGun);
            DrawHudIconButton(tpRect, _teleportIcon, AmmoSlot.Teleport);
        }

        private void DrawIconBorder(Rect rect, bool selected, bool canSelect)
        {
            if (_borderTex == null) return;
            var borderW = Mathf.Max(2f, rect.width * 0.04f);
            var borderColor = selected
                ? new Color(1f, 1f, 1f, 0.95f)
                : new Color(0.8f, 0.8f, 0.8f, canSelect ? 0.50f : 0.20f);
            var prev = GUI.color;
            GUI.color = borderColor;
            // top
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, borderW), _borderTex);
            // bottom
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - borderW, rect.width, borderW), _borderTex);
            // left
            GUI.DrawTexture(new Rect(rect.x, rect.y, borderW, rect.height), _borderTex);
            // right
            GUI.DrawTexture(new Rect(rect.xMax - borderW, rect.y, borderW, rect.height), _borderTex);
            GUI.color = prev;
        }

        private void DrawHudIconButton(Rect rect, Texture2D tex, AmmoSlot slot)
        {
            var canSelect = CoerceSelection(slot) == slot;
            var selected = _selected == slot;

            var prevEnabled = GUI.enabled;
            GUI.enabled = canSelect;

            var prevColor = GUI.color;
            GUI.color = selected ? new Color(1f, 1f, 1f, 1f) : new Color(1f, 1f, 1f, canSelect ? 0.60f : 0.25f);

            var clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);
            if (tex != null)
            {
                GUI.DrawTexture(rect, tex, ScaleMode.ScaleToFit, alphaBlend: true);
            }
            else
            {
                GUI.Label(rect, "?");
            }

            DrawIconBorder(rect, selected, canSelect);

            GUI.color = prevColor;
            GUI.enabled = prevEnabled;

            if (clicked)
            {
                _selected = CoerceSelection(slot);
                ApplySelection();
            }
        }

        private void DrawGrenadeHudIconButton(Rect rect, AmmoSlot slot)
        {
            var canSelect = CoerceSelection(slot) == slot;
            var selected = _selected == slot;

            var prevEnabled = GUI.enabled;
            GUI.enabled = canSelect;

            var prev = GUI.color;
            GUI.color = selected ? new Color(1f, 1f, 1f, 1f) : new Color(1f, 1f, 1f, canSelect ? 0.60f : 0.25f);

            var clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);
            if (_grenadeIcon != null)
            {
                GUI.DrawTexture(rect, _grenadeIcon, ScaleMode.ScaleToFit, alphaBlend: true);
            }
            else
            {
                GUI.Label(rect, "G");
            }

            if (_grenade != null)
            {
                var n = Mathf.Max(0, _grenade.GrenadesLeft);
                var fontSize = Mathf.Clamp(Mathf.RoundToInt(rect.height * 0.28f), 12, 24);
                var labelH = fontSize * 1.3f;
                var labelW = rect.width * 0.55f;
                var labelRect = new Rect(rect.xMax - labelW - 2f, rect.y + 2f, labelW, labelH);

                var style = new GUIStyle(GUI.skin.label);
                style.alignment = TextAnchor.UpperRight;
                style.fontStyle = FontStyle.Bold;
                style.fontSize = fontSize;

                var text = n.ToString();
                var shadow = new Color(0f, 0f, 0f, 0.90f);
                var main = selected ? new Color(1f, 1f, 0.3f, 1f) : new Color(1f, 1f, 0.3f, canSelect ? 0.90f : 0.45f);

                var prevColor2 = GUI.color;
                GUI.color = shadow;
                GUI.Label(new Rect(labelRect.x + 1f, labelRect.y + 1f, labelRect.width, labelRect.height), text, style);
                GUI.color = main;
                GUI.Label(labelRect, text, style);
                GUI.color = prevColor2;
            }

            DrawIconBorder(rect, selected, canSelect);

            GUI.color = prev;
            GUI.enabled = prevEnabled;

            if (clicked)
            {
                _selected = CoerceSelection(slot);
                ApplySelection();
            }
        }

        private void DrawClawGunHudIconButton(Rect rect, AmmoSlot slot)
        {
            var canSelect = CoerceSelection(slot) == slot;
            var selected = _selected == slot;

            var prevEnabled = GUI.enabled;
            GUI.enabled = canSelect;

            var prev = GUI.color;
            GUI.color = selected ? new Color(1f, 1f, 1f, 1f) : new Color(1f, 1f, 1f, canSelect ? 0.60f : 0.25f);

            var clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);

            if (_clawGunIcon != null)
            {
                GUI.DrawTexture(rect, _clawGunIcon, ScaleMode.ScaleToFit, alphaBlend: true);
            }
            else if (_clawGunSheet != null)
            {
                GUI.DrawTextureWithTexCoords(rect, _clawGunSheet, _clawGunFrame0Uv, alphaBlend: true);
            }
            else
            {
                GUI.Label(rect, "A");
            }

            if (_clawGun != null)
            {
                var shots = Mathf.Max(0, _clawGun.ShotsLeft);
                var fontSize = Mathf.Clamp(Mathf.RoundToInt(rect.height * 0.28f), 12, 24);
                var labelH = fontSize * 1.3f;
                var labelW = rect.width * 0.55f;
                var labelRect = new Rect(rect.xMax - labelW - 2f, rect.y + 2f, labelW, labelH);

                var style = new GUIStyle(GUI.skin.label);
                style.alignment = TextAnchor.UpperRight;
                style.fontStyle = FontStyle.Bold;
                style.fontSize = fontSize;

                var text = shots.ToString();
                var shadow = new Color(0f, 0f, 0f, 0.90f);
                var main = selected ? new Color(1f, 1f, 0.3f, 1f) : new Color(1f, 1f, 0.3f, canSelect ? 0.90f : 0.45f);

                var prevColor2 = GUI.color;
                GUI.color = shadow;
                GUI.Label(new Rect(labelRect.x + 1f, labelRect.y + 1f, labelRect.width, labelRect.height), text, style);
                GUI.color = main;
                GUI.Label(labelRect, text, style);
                GUI.color = prevColor2;
            }

            DrawIconBorder(rect, selected, canSelect);

            GUI.color = prev;
            GUI.enabled = prevEnabled;

            if (clicked)
            {
                _selected = CoerceSelection(slot);
                ApplySelection();
            }
        }

        private void ResolveClawGunIcon()
        {
            _clawGunSheet = null;
            _clawGunFrame0Uv = new Rect(0f, 0f, 1f, 1f);

            if (!string.IsNullOrEmpty(clawGunSpritesheetResourcesPath))
            {
                _clawGunSheet = Resources.Load<Texture2D>(clawGunSpritesheetResourcesPath);
                if (_clawGunSheet == null)
                {
                    var s = Resources.Load<Sprite>(clawGunSpritesheetResourcesPath);
                    if (s != null)
                    {
                        _clawGunSheet = s.texture;
                    }
                }
            }

            if (_clawGunSheet == null)
            {
                return;
            }

            var frames = Mathf.Max(1, clawGunFrameCount);
            var fw = Mathf.Max(1, Mathf.FloorToInt(_clawGunSheet.width / (float)frames));
            var u = Mathf.Clamp01(fw / (float)_clawGunSheet.width);
            _clawGunFrame0Uv = new Rect(0f, 0f, u, 1f);
        }

        private static Texture2D GenerateRopeIcon()
        {
            var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;

            var clear = new Color(0f, 0f, 0f, 0f);
            var col = new Color(1f, 1f, 1f, 1f);
            for (var y = 0; y < 32; y++)
            {
                for (var x = 0; x < 32; x++)
                {
                    tex.SetPixel(x, y, clear);
                }
            }

            for (var y = 4; y < 28; y++)
            {
                var x = 16 + (int)(Mathf.Sin(y * 0.35f) * 4f);
                tex.SetPixel(Mathf.Clamp(x, 0, 31), y, col);
                tex.SetPixel(Mathf.Clamp(x + 1, 0, 31), y, col);
            }

            tex.Apply(false, true);
            return tex;
        }

        private static Texture2D GenerateClawGunFallbackIcon()
        {
            var tex = new Texture2D(64, 32, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;

            var clear = new Color(0f, 0f, 0f, 0f);
            var col = new Color(1f, 1f, 1f, 1f);
            for (var y = 0; y < tex.height; y++)
            {
                for (var x = 0; x < tex.width; x++)
                {
                    tex.SetPixel(x, y, clear);
                }
            }

            // Simple horizontal gun silhouette.
            for (var x = 8; x < 56; x++)
            {
                for (var y = 14; y < 18; y++)
                {
                    tex.SetPixel(x, y, col);
                }
            }
            for (var x = 44; x < 60; x++)
            {
                for (var y = 16; y < 20; y++)
                {
                    tex.SetPixel(x, y, col);
                }
            }
            for (var x = 18; x < 28; x++)
            {
                for (var y = 6; y < 14; y++)
                {
                    tex.SetPixel(x, y, col);
                }
            }

            tex.Apply(false, true);
            return tex;
        }

        private static Texture2D GenerateTeleportIcon()
        {
            var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;

            var clear = new Color(0f, 0f, 0f, 0f);
            var col = new Color(1f, 1f, 1f, 1f);
            for (var y = 0; y < 32; y++)
            {
                for (var x = 0; x < 32; x++)
                {
                    tex.SetPixel(x, y, clear);
                }
            }

            // Simple portal ring.
            var cx = 16;
            var cy = 16;
            for (var a = 0; a < 360; a += 10)
            {
                var r0 = a * Mathf.Deg2Rad;
                var x = cx + Mathf.RoundToInt(Mathf.Cos(r0) * 10f);
                var y = cy + Mathf.RoundToInt(Mathf.Sin(r0) * 10f);
                if (x >= 0 && x < 32 && y >= 0 && y < 32) tex.SetPixel(x, y, col);
            }
            for (var y = 10; y <= 22; y++)
            {
                tex.SetPixel(cx, y, col);
            }

            tex.Apply(false, true);
            return tex;
        }
    }
}
