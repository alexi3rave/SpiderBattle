// Handles animated dashed aiming line and end scope reticle for Worms-like aiming.
// Owned by HeroClawGun; call UpdateLine() each frame with current aim data.
using UnityEngine;

namespace WormCrawlerPrototype
{
    public sealed class AimLineVisual2D : MonoBehaviour
    {
        // ── Line appearance ──
        [Header("Line")]
        [SerializeField] private float lineWidth = 0.055f;
        [SerializeField] private Color lineColorStart = new Color(0.5f, 1f, 1f, 0.85f);
        [SerializeField] private Color lineColorEnd = new Color(1f, 1f, 1f, 0.35f);
        [SerializeField] private float uvScrollSpeed = 3.0f;
        [SerializeField] private float uvTilingX = 10f;

        // ── Scope reticle ──
        [Header("Scope")]
        [SerializeField] private float scopeWorldSize = 0.55f;
        [SerializeField] private float scopePulseAmplitude = 0.08f;
        [SerializeField] private float scopePulseSpeed = 5.5f;
        [SerializeField] private Color scopeRingColor = new Color(0.5f, 0.88f, 1f, 0.85f);
        [SerializeField] private Color scopeCrossColor = new Color(1f, 1f, 1f, 0.9f);

        // ── Sorting ──
        [Header("Sorting")]
        [SerializeField] private int lineSortingOrder = 90;
        [SerializeField] private int scopeSortingOrder = 91;

        // ── Runtime ──
        private LineRenderer _lr;
        private Material _lrMat;
        private Transform _scopeT;
        private SpriteRenderer _scopeSr;

        private static Texture2D s_DashTex;
        private static Sprite s_ScopeSprite;

        // ────────────────────────────────────────────
        // Public API — called by HeroClawGun each frame
        // ────────────────────────────────────────────
        public void UpdateLine(bool visible, Vector2 origin, Vector2 end)
        {
            EnsureLineRenderer();
            EnsureScope();

            if (!visible)
            {
                if (_lr != null) _lr.enabled = false;
                if (_scopeT != null) _scopeT.gameObject.SetActive(false);
                return;
            }

            // ── Line ──
            _lr.enabled = true;
            _lr.SetPosition(0, new Vector3(origin.x, origin.y, 0f));
            _lr.SetPosition(1, new Vector3(end.x, end.y, 0f));

            _lr.startColor = lineColorStart;
            _lr.endColor = lineColorEnd;
            _lr.startWidth = lineWidth;
            _lr.endWidth = lineWidth * 0.7f;

            // Animate UV scroll — dashes run from hero toward target.
            if (_lrMat != null)
            {
                var offset = -Time.time * uvScrollSpeed;
                _lrMat.mainTextureOffset = new Vector2(offset, 0f);
                _lrMat.mainTextureScale = new Vector2(uvTilingX, 1f);
            }

            // ── Scope ──
            if (_scopeT != null)
            {
                _scopeT.gameObject.SetActive(true);
                _scopeT.position = new Vector3(end.x, end.y, 0f);
                // Keep upright — no rotation.
                _scopeT.rotation = Quaternion.identity;
                // Pulse scale.
                var pulse = 1f + Mathf.Sin(Time.time * scopePulseSpeed) * scopePulseAmplitude;
                var s = scopeWorldSize * pulse;
                _scopeT.localScale = new Vector3(s, s, 1f);
            }
        }

        // ────────────────────────────────────────────
        // Lazy initialization
        // ────────────────────────────────────────────
        private void EnsureLineRenderer()
        {
            if (_lr != null) return;

            _lr = gameObject.GetComponent<LineRenderer>();
            if (_lr == null) _lr = gameObject.AddComponent<LineRenderer>();

            _lr.useWorldSpace = true;
            _lr.positionCount = 2;
            _lr.numCapVertices = 4;
            _lr.numCornerVertices = 0;
            _lr.textureMode = LineTextureMode.Tile;
            _lr.alignment = LineAlignment.TransformZ;
            _lr.sortingOrder = lineSortingOrder;

            // Material with dash texture.
            var dashTex = GetOrCreateDashTexture();
            var shader = Shader.Find("Sprites/Default");
            _lrMat = new Material(shader);
            _lrMat.mainTexture = dashTex;
            _lr.material = _lrMat;

            _lr.startColor = lineColorStart;
            _lr.endColor = lineColorEnd;
            _lr.startWidth = lineWidth;
            _lr.endWidth = lineWidth * 0.7f;
            _lr.enabled = false;
        }

        private void EnsureScope()
        {
            if (_scopeT != null) return;

            var existing = transform.Find("AimScope");
            if (existing != null)
            {
                _scopeT = existing;
            }
            else
            {
                var go = new GameObject("AimScope");
                go.transform.SetParent(transform, false);
                _scopeT = go.transform;
            }

            _scopeSr = _scopeT.GetComponent<SpriteRenderer>();
            if (_scopeSr == null) _scopeSr = _scopeT.gameObject.AddComponent<SpriteRenderer>();

            _scopeSr.sprite = GetOrCreateScopeSprite();
            _scopeSr.sortingOrder = scopeSortingOrder;
            _scopeSr.color = Color.white; // tint handled by sprite colors
            _scopeT.gameObject.SetActive(false);
        }

        // ────────────────────────────────────────────
        // Procedural texture: dash pattern for LineRenderer
        // ────────────────────────────────────────────
        private static Texture2D GetOrCreateDashTexture()
        {
            if (s_DashTex != null) return s_DashTex;

            // 64×4 repeating pattern: bright dash + transparent gap.
            const int w = 64;
            const int h = 4;
            s_DashTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            s_DashTex.wrapMode = TextureWrapMode.Repeat;
            s_DashTex.filterMode = FilterMode.Bilinear;

            var pixels = new Color[w * h];
            var clear = new Color(1f, 1f, 1f, 0f);

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    // Dash occupies x=[0..35], gap occupies x=[36..63].
                    // Soft edges at x=0..3 and x=32..35.
                    float alpha;
                    if (x < 4)
                    {
                        alpha = x / 3f; // fade in
                    }
                    else if (x < 32)
                    {
                        alpha = 1f; // solid dash
                    }
                    else if (x < 36)
                    {
                        alpha = 1f - (x - 32) / 3f; // fade out
                    }
                    else
                    {
                        alpha = 0f; // gap
                    }

                    // Vertical softness: slightly dimmer at edges.
                    var vy = (y == 0 || y == h - 1) ? 0.6f : 1f;
                    pixels[y * w + x] = new Color(1f, 1f, 1f, alpha * vy);
                }
            }

            s_DashTex.SetPixels(pixels);
            s_DashTex.Apply(false, true);
            return s_DashTex;
        }

        // ────────────────────────────────────────────
        // Procedural sprite: sniper scope reticle
        // ────────────────────────────────────────────
        private static Sprite GetOrCreateScopeSprite()
        {
            if (s_ScopeSprite != null) return s_ScopeSprite;

            const int size = 48;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color[size * size];
            var clear = new Color(1f, 1f, 1f, 0f);
            for (var i = 0; i < pixels.Length; i++) pixels[i] = clear;

            var mid = size / 2;
            var ringColor = new Color(0.5f, 0.88f, 1f, 0.85f);   // cyan ring
            var crossColor = new Color(1f, 1f, 1f, 0.9f);         // white cross
            var dotColor = new Color(1f, 1f, 1f, 1f);              // bright center

            // ── Ring (circle outline) ──
            var outerR = 18f;
            var innerR = 15.5f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x - mid + 0.5f;
                    var dy = y - mid + 0.5f;
                    var dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist >= innerR && dist <= outerR)
                    {
                        // Anti-alias edges.
                        var aa = 1f - Mathf.Clamp01(Mathf.Abs(dist - (innerR + outerR) * 0.5f) - (outerR - innerR) * 0.35f);
                        var c = ringColor;
                        c.a *= Mathf.Clamp01(aa);
                        var idx = y * size + x;
                        pixels[idx] = BlendOver(pixels[idx], c);
                    }
                }
            }

            // ── Crosshair lines (with gap in center) ──
            var gap = 4;
            var armLen = 11;
            // Horizontal arms.
            for (var x = mid - armLen - gap; x <= mid + armLen + gap; x++)
            {
                if (x < 0 || x >= size) continue;
                if (Mathf.Abs(x - mid) <= gap) continue;
                var idx = mid * size + x;
                pixels[idx] = BlendOver(pixels[idx], crossColor);
                // Slight thickness: one pixel above and below at lower alpha.
                if (mid - 1 >= 0)
                    pixels[(mid - 1) * size + x] = BlendOver(pixels[(mid - 1) * size + x], new Color(1f, 1f, 1f, 0.35f));
                if (mid + 1 < size)
                    pixels[(mid + 1) * size + x] = BlendOver(pixels[(mid + 1) * size + x], new Color(1f, 1f, 1f, 0.35f));
            }
            // Vertical arms.
            for (var y = mid - armLen - gap; y <= mid + armLen + gap; y++)
            {
                if (y < 0 || y >= size) continue;
                if (Mathf.Abs(y - mid) <= gap) continue;
                var idx = y * size + mid;
                pixels[idx] = BlendOver(pixels[idx], crossColor);
                if (mid - 1 >= 0)
                    pixels[y * size + mid - 1] = BlendOver(pixels[y * size + mid - 1], new Color(1f, 1f, 1f, 0.35f));
                if (mid + 1 < size)
                    pixels[y * size + mid + 1] = BlendOver(pixels[y * size + mid + 1], new Color(1f, 1f, 1f, 0.35f));
            }

            // ── Center dot ──
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    var px = mid + dx;
                    var py = mid + dy;
                    if (px >= 0 && px < size && py >= 0 && py < size)
                    {
                        var a = (dx == 0 && dy == 0) ? 1f : 0.6f;
                        pixels[py * size + px] = BlendOver(pixels[py * size + px], new Color(1f, 1f, 1f, a));
                    }
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false, true);
            s_ScopeSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            return s_ScopeSprite;
        }

        private static Color BlendOver(Color dst, Color src)
        {
            var sa = src.a;
            var da = dst.a;
            var outA = sa + da * (1f - sa);
            if (outA < 0.001f) return new Color(0f, 0f, 0f, 0f);
            var r = (src.r * sa + dst.r * da * (1f - sa)) / outA;
            var g = (src.g * sa + dst.g * da * (1f - sa)) / outA;
            var b = (src.b * sa + dst.b * da * (1f - sa)) / outA;
            return new Color(r, g, b, outA);
        }
    }
}
