using UnityEngine;
using UnityEngine.InputSystem;

namespace WormCrawlerPrototype
{
    public sealed class HeroTeleport : MonoBehaviour
    {
        [Header("State")]
        public bool Enabled = false;

        [SerializeField] private bool usedThisMatch = false;

        public bool CanUseNow => !usedThisMatch;

        private Rigidbody2D _rb;
        private Collider2D _heroCol;
        private GrappleController _grapple;
        private HeroAmmoCarousel _ammo;
        private TurnManager _turn;

        private bool _didTeleportThisEnable;
        private bool _confirmOpen;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _heroCol = GetComponent<Collider2D>();
            _grapple = GetComponent<GrappleController>();
            _ammo = GetComponent<HeroAmmoCarousel>();
            _turn = TurnManager.Instance;
        }

        public void ResetMatchUsage()
        {
            usedThisMatch = false;
        }

        public bool TryTeleportNow()
        {
            return TryTeleportNowInternal(bypassConfirmation: false);
        }

        public bool TryTeleportNowBot()
        {
            return TryTeleportNowInternal(bypassConfirmation: true);
        }

        private bool TryTeleportNowInternal(bool bypassConfirmation)
        {
            if (!Enabled)
            {
                return false;
            }

            if (_didTeleportThisEnable)
            {
                return false;
            }

            if (!bypassConfirmation && !_confirmOpen)
            {
                return false;
            }

            if (usedThisMatch)
            {
                Enabled = false;
                _confirmOpen = false;
                if (_ammo != null) _ammo.ForceSelectRope();
                return false;
            }

            if (_turn != null)
            {
                if (_turn.ActivePlayer != transform)
                {
                    Enabled = false;
                    _confirmOpen = false;
                    return false;
                }

                if (!_turn.CanSelectWeapon(TurnManager.TurnWeapon.Teleport))
                {
                    Enabled = false;
                    _confirmOpen = false;
                    if (_ammo != null) _ammo.ForceSelectRope();
                    return false;
                }
            }

            if (!TryFindRandomTeleportTarget(out var target))
            {
                Enabled = false;
                _confirmOpen = false;
                if (_ammo != null) _ammo.ForceSelectRope();
                return false;
            }

            if (_turn != null)
            {
                if (!_turn.TryConsumeShot(TurnManager.TurnWeapon.Teleport))
                {
                    return false;
                }
                _turn.NotifyWeaponSelected(TurnManager.TurnWeapon.Teleport);
            }

            ApplyTeleport(target);

            usedThisMatch = true;
            _didTeleportThisEnable = true;
            _confirmOpen = false;
            Enabled = false;
            if (_ammo != null) _ammo.ForceSelectRope();

            if (_turn != null && _turn.ActivePlayer == transform)
            {
                _turn.EndTurnAfterAttack();
            }

            return true;
        }

        private void Update()
        {
            if (Bootstrap.IsMapMenuOpen)
            {
                return;
            }

            if (!Enabled)
            {
                _didTeleportThisEnable = false;
                _confirmOpen = false;
                return;
            }

            if (_didTeleportThisEnable)
            {
                Enabled = false;
                _confirmOpen = false;
                if (_ammo != null) _ammo.ForceSelectRope();
                return;
            }

            if (!_confirmOpen)
            {
                _confirmOpen = true;
            }

            if (UnityEngine.InputSystem.Keyboard.current != null)
            {
                if (UnityEngine.InputSystem.Keyboard.current.enterKey.wasPressedThisFrame || UnityEngine.InputSystem.Keyboard.current.numpadEnterKey.wasPressedThisFrame || UnityEngine.InputSystem.Keyboard.current.yKey.wasPressedThisFrame)
                {
                    TryTeleportNowInternal(bypassConfirmation: true);
                    return;
                }
                if (UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame || UnityEngine.InputSystem.Keyboard.current.nKey.wasPressedThisFrame)
                {
                    CancelTeleport();
                    return;
                }
            }
        }

        private void OnGUI()
        {
            if (Bootstrap.IsMapMenuOpen)
            {
                return;
            }
            if (!Enabled || !_confirmOpen)
            {
                return;
            }
            if (usedThisMatch)
            {
                return;
            }

            var w = Mathf.Min(520f, Screen.width * 0.9f);
            var h = 170f;
            var x = (Screen.width - w) * 0.5f;
            var y = (Screen.height - h) * 0.35f;
            var rect = new Rect(x, y, w, h);

            GUI.ModalWindow(GetInstanceID(), rect, _ =>
            {
                GUILayout.Space(6f);
                GUILayout.Label("Телепортироваться в случайную точку карты?");
                GUILayout.Space(10f);
                GUILayout.Label("Enter/Y = Да, Esc/N = Отмена");
                GUILayout.FlexibleSpace();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Да", GUILayout.Height(42f)))
                {
                    TryTeleportNowInternal(bypassConfirmation: true);
                }
                if (GUILayout.Button("Отмена", GUILayout.Height(42f)))
                {
                    CancelTeleport();
                }
                GUILayout.EndHorizontal();

            }, "Teleport");
        }

        private void CancelTeleport()
        {
            _confirmOpen = false;
            Enabled = false;
            if (_ammo != null) _ammo.ForceSelectRope();
        }

        private bool TryFindRandomTeleportTarget(out Vector2 target)
        {
            target = default;

            // Build a reasonable map AABB from terrain colliders.
            var world = GameObject.Find("World");
            if (world == null)
            {
                return false;
            }

            var ground = world.transform.Find("GroundPoly");
            var islands = world.transform.Find("Islands");

            bool IsTerrainCollider(Collider2D c)
            {
                if (c == null || c.isTrigger) return false;
                if (ground != null)
                {
                    var t = c.transform;
                    if (t == ground || t.IsChildOf(ground)) return true;
                }
                if (islands != null)
                {
                    var t = c.transform;
                    if (t == islands || t.IsChildOf(islands)) return true;
                }
                // Fallback by name, in case hierarchy differs.
                return c.transform != null && (c.transform.name == "GroundPoly" || c.transform.name == "Islands");
            }

            var polys = world.GetComponentsInChildren<Collider2D>(includeInactive: true);
            if (polys == null || polys.Length == 0)
            {
                return false;
            }

            var foundBounds = false;
            Bounds b = default;
            for (var i = 0; i < polys.Length; i++)
            {
                var c = polys[i];
                if (c == null) continue;
                if (c.isTrigger) continue;

                // Only consider terrain-like roots.
                var rootName = c.transform.root != null ? c.transform.root.name : "";
                var isTerrain = (ground != null && (c.transform == ground || c.transform.IsChildOf(ground)))
                               || (islands != null && (c.transform == islands || c.transform.IsChildOf(islands)))
                               || c.transform.name == "GroundPoly" || c.transform.name == "Islands";
                if (!isTerrain && rootName != "World")
                {
                    // Still allow if collider is under World but not hero/projectile.
                    if (!c.transform.IsChildOf(world.transform))
                    {
                        continue;
                    }
                }

                if (!foundBounds)
                {
                    b = c.bounds;
                    foundBounds = true;
                }
                else
                {
                    b.Encapsulate(c.bounds);
                }
            }

            if (!foundBounds)
            {
                return false;
            }

            // Avoid edge spawns and keep within map.
            var margin = 0.5f;
            var minX = b.min.x + margin;
            var maxX = b.max.x - margin;
            var minY = b.min.y + margin;
            var maxY = b.max.y - margin;
            if (maxX <= minX || maxY <= minY)
            {
                return false;
            }

            var heroSize = _heroCol != null ? (Vector2)_heroCol.bounds.size : new Vector2(1.0f, 1.5f);
            heroSize.x = Mathf.Max(0.25f, heroSize.x);
            heroSize.y = Mathf.Max(0.25f, heroSize.y);

            bool IsFarFromOtherHeroes(Vector2 p)
            {
                var ids = world.GetComponentsInChildren<PlayerIdentity>(includeInactive: true);
                if (ids == null) return true;
                var minDist = Mathf.Max(0.8f, heroSize.x * 1.25f);
                var minDist2 = minDist * minDist;
                for (var i = 0; i < ids.Length; i++)
                {
                    var id = ids[i];
                    if (id == null) continue;
                    var t = id.transform;
                    if (t == transform) continue;
                    var d2 = ((Vector2)t.position - p).sqrMagnitude;
                    if (d2 < minDist2) return false;
                }
                return true;
            }

            Vector2 LiftOutOfGroundLike(Vector2 p)
            {
                // Best-effort: if overlapping, step up until free.
                var box = new Vector2(heroSize.x * 0.9f, heroSize.y * 0.9f);
                var step = Mathf.Max(0.02f, heroSize.y * 0.08f);
                for (var i = 0; i < 60; i++)
                {
                    if (IsValidTeleportTarget(p))
                    {
                        return p;
                    }
                    p.y += step;
                }
                return p;
            }

            const int attempts = 140;
            for (var a = 0; a < attempts; a++)
            {
                var x = Random.Range(minX, maxX);

                // Raycast from above the map down to the first terrain hit.
                var rayOrigin = new Vector2(x, maxY + heroSize.y * 2.0f);
                var hits = Physics2D.RaycastAll(rayOrigin, Vector2.down, (maxY - minY) + heroSize.y * 4.0f, ~0);
                if (hits == null || hits.Length == 0)
                {
                    continue;
                }

                var found = false;
                RaycastHit2D chosen = default;
                for (var i = 0; i < hits.Length; i++)
                {
                    var h = hits[i];
                    if (h.collider == null) continue;
                    if (h.collider.isTrigger) continue;
                    if (!IsTerrainCollider(h.collider)) continue;
                    chosen = h;
                    found = true;
                    break;
                }

                if (!found)
                {
                    continue;
                }

                // Place above the surface.
                var p = chosen.point + Vector2.up * (heroSize.y * 0.6f + 0.08f);
                p = LiftOutOfGroundLike(p);
                if (!IsValidTeleportTarget(p))
                {
                    continue;
                }
                if (!IsFarFromOtherHeroes(p))
                {
                    continue;
                }

                target = p;
                return true;
            }

            return false;
        }

        private void ApplyTeleport(Vector2 target)
        {
            if (_grapple != null && _grapple.IsAttached)
            {
                _grapple.ForceDetach();
            }

            transform.position = new Vector3(target.x, target.y, transform.position.z);

            if (_rb != null)
            {
                _rb.linearVelocity = Vector2.zero;
                _rb.angularVelocity = 0f;
            }
        }

        private bool IsValidTeleportTarget(Vector2 target)
        {
            if (_heroCol == null)
            {
                return true;
            }

            var size = _heroCol.bounds.size;
            var w = Mathf.Max(0.1f, size.x * 0.9f);
            var h = Mathf.Max(0.1f, size.y * 0.9f);

            var hits = Physics2D.OverlapBoxAll(target, new Vector2(w, h), 0f, ~0);
            if (hits == null || hits.Length == 0)
            {
                return true;
            }

            for (var i = 0; i < hits.Length; i++)
            {
                var c = hits[i];
                if (c == null) continue;
                if (c == _heroCol) continue;
                if (c.isTrigger) continue;

                if (c.transform == transform || c.transform.IsChildOf(transform))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private void OnDrawGizmosSelected()
        {
            if (_heroCol == null) return;
            var size = _heroCol.bounds.size;
            var w = Mathf.Max(0.1f, size.x * 0.9f);
            var h = Mathf.Max(0.1f, size.y * 0.9f);
            Gizmos.color = new Color(0.6f, 0.9f, 1f, 0.25f);
            Gizmos.DrawCube(transform.position, new Vector3(w, h, 0.01f));
        }
    }
}
