using System.Collections.Generic;
using UnityEngine;

namespace WormCrawlerPrototype
{
    public sealed class TurnManager : MonoBehaviour
    {
        public static TurnManager Instance { get; private set; }

        public enum TurnWeapon
        {
            None = 0,
            Grenade = 1,
            ClawGun = 2,
            AutoGun = 3,
            Teleport = 4,
        }

        [SerializeField] private float turnSeconds = 60f;
        [SerializeField] private float postActionClampSeconds = 5f;
        [SerializeField] private float loseBelowY = -200f;
        [SerializeField] private float loseGraceSeconds = 1.5f;
        [SerializeField] private int spidersTeamIndex = 0;

        [Header("Debug")]
        [SerializeField] private bool logTurnOrder = true;
        [SerializeField] private int turnLogHistorySize = 10;
        [SerializeField] private bool logWeaponBlocks = false;

        private Transform[] _players;
        private int _activeIndex;
        private readonly List<Transform> _turnOrder = new List<Transform>(16);
        private int _turnOrderCursor;
        private float _turnT;
        private bool _gameEnded;
        private string _winnerName;
        private float _playersReadyT;

        private bool _showEndRoundMenu;
        private int _endRoundSelectedIndex;

        private int _worldInstanceId;

        private static int _team0Wins;
        private static int _team1Wins;

        private static int _nextStartTeam = -1;

        private bool _pendingForcedTurnEnd;
        private int _pendingForcedTurnEndFrame;

        private TurnWeapon _turnLockedWeapon = TurnWeapon.None;
        private bool _turnShotUsed;
        private bool _ropeOnlyThisTurn;
        private bool _grenadeAwaitingExplosion;
        private bool _grenadePostExplosionEscapeWindow;
        private bool _pendingTurnEndAfterGrenadeResolution;
        private int _pendingTurnEndAfterGrenadeResolutionMinFrame;

        private bool _damageReactionActive;
        private float _damageReactionEndT;
        [SerializeField] private float damageReactionSeconds = 2.0f;

        private int _lastKnownActiveTeam = -1;

        private int _lastBotTurnPlayerInstanceId;

        private struct TurnLogEntry
        {
            public int Index;
            public string Player;
            public int Team;
            public float Duration;
            public string Reason;
            public string Next;
        }

        private readonly List<TurnLogEntry> _turnLogHistory = new List<TurnLogEntry>(16);
        private int _turnLogIndex;
        private int _lastLoggedActiveInstanceId;

        private int _lastWeaponBlockLogFrame;

        private int _lastAppliedActiveInstanceId;

        private struct HpAnim
        {
            public int From;
            public int To;
            public float StartT;
            public float Duration;
        }

        private readonly Dictionary<Transform, HpAnim> _hpAnims = new Dictionary<Transform, HpAnim>();

        public Transform ActivePlayer
        {
            get
            {
                if (_turnOrder == null || _turnOrder.Count == 0)
                {
                    return null;
                }

                if (_turnOrderCursor < 0 || _turnOrderCursor >= _turnOrder.Count)
                {
                    _turnOrderCursor = Mathf.Clamp(_turnOrderCursor, 0, _turnOrder.Count - 1);
                }

                // Do not auto-resolve dead players here: if the active hero dies during their turn,
                // the game loop must detect it and advance the turn (preserving strict alternation).
                return _turnOrder[_turnOrderCursor];
            }
        }

        private void OnAnyExplosion()
        {
            if (Instance != this || _gameEnded)
            {
                return;
            }

            if (!_grenadeAwaitingExplosion)
            {
                return;
            }

            _grenadeAwaitingExplosion = false;

            // Active hero already took damage this turn (e.g., fall) and must end turn
            // only after grenade explosion + damage processing is fully resolved.
            if (_pendingTurnEndAfterGrenadeResolution)
            {
                _grenadePostExplosionEscapeWindow = false;
                _pendingTurnEndAfterGrenadeResolutionMinFrame = Mathf.Max(_pendingTurnEndAfterGrenadeResolutionMinFrame, Time.frameCount + 1);
                return;
            }

            _grenadePostExplosionEscapeWindow = true;
            EnsureRemainingTimeAfterActionWindow();
        }

        public void EndTurnAfterAttack()
        {
            if (_gameEnded)
            {
                return;
            }
            // Ignore stale async attack-end calls from previous turns/coroutines.
            // A legal attack_end is only possible after a shot was consumed this turn.
            if (!_turnShotUsed)
            {
                return;
            }
            if (_damageReactionActive)
            {
                return;
            }
            // Grenade turns must not end immediately on throw. The thrower gets:
            // 1) fuse/air time until explosion,
            // 2) then the regular post-action clamp window after explosion.
            if (_grenadeAwaitingExplosion || _grenadePostExplosionEscapeWindow)
            {
                return;
            }
            NextTurn("attack_end");
        }
        public int ActivePlayerIndex
        {
            get
            {
                var ap = ActivePlayer;
                if (_players == null || _players.Length == 0 || ap == null) return -1;
                for (var i = 0; i < _players.Length; i++)
                {
                    if (_players[i] == ap) return i;
                }
                return -1;
            }
        }
        public int SecondsLeft => Mathf.CeilToInt(Mathf.Max(0f, turnSeconds - _turnT));

        public TurnWeapon LockedWeaponThisTurn => _turnLockedWeapon;
        public bool ShotUsedThisTurn => _turnShotUsed;
        public bool RopeOnlyThisTurn => _ropeOnlyThisTurn;

        public void NotifyWeaponSelected(TurnWeapon weapon)
        {
            if (weapon == TurnWeapon.None)
            {
                return;
            }

            if (_turnLockedWeapon == TurnWeapon.None)
            {
                _turnLockedWeapon = weapon;
            }
        }

        public bool CanSelectWeapon(TurnWeapon weapon)
        {
            if (weapon == TurnWeapon.None)
            {
                return true;
            }

            if (_ropeOnlyThisTurn)
            {
                LogWeaponBlockedOncePerFrame("CanSelectWeapon", weapon, "rope_only");
                return false;
            }

            if (_turnShotUsed)
            {
                LogWeaponBlockedOncePerFrame("CanSelectWeapon", weapon, "shot_used");
                return false;
            }

            var ok = _turnLockedWeapon == TurnWeapon.None || _turnLockedWeapon == weapon;
            if (!ok)
            {
                LogWeaponBlockedOncePerFrame("CanSelectWeapon", weapon, $"locked={_turnLockedWeapon}");
            }
            return ok;
        }

        public bool TryConsumeShot(TurnWeapon weapon)
        {
            if (_gameEnded || _damageReactionActive)
            {
                LogWeaponBlockedOncePerFrame("TryConsumeShot", weapon, _gameEnded ? "game_ended" : "damage_reaction");
                return false;
            }

            if (_ropeOnlyThisTurn)
            {
                LogWeaponBlockedOncePerFrame("TryConsumeShot", weapon, "rope_only");
                return false;
            }

            if (_turnShotUsed)
            {
                LogWeaponBlockedOncePerFrame("TryConsumeShot", weapon, "shot_used");
                return false;
            }

            if (!CanSelectWeapon(weapon))
            {
                LogWeaponBlockedOncePerFrame("TryConsumeShot", weapon, $"cant_select locked={_turnLockedWeapon}");
                return false;
            }

            if (_turnLockedWeapon == TurnWeapon.None)
            {
                _turnLockedWeapon = weapon;
            }

            _turnShotUsed = true;

            if (weapon == TurnWeapon.Grenade)
            {
                _grenadeAwaitingExplosion = true;
                _grenadePostExplosionEscapeWindow = false;

                var ap = ActivePlayer;
                if (ap != null)
                {
                    var ammo = ap.GetComponent<HeroAmmoCarousel>();
                    if (ammo != null) ammo.ForceSelectRope();
                }
            }

            // Clamp immediately for single-action weapons.
            // Grenade clamps only after explosion; ClawGun clamps on fire-release
            // so the post-action escape window does not burn during active burst.
            if (weapon != TurnWeapon.Grenade && weapon != TurnWeapon.ClawGun)
            {
                ClampRemainingTimeAfterAction();
            }

            ApplyActiveState();
            return true;
        }

        private void LogWeaponBlockedOncePerFrame(string from, TurnWeapon weapon, string reason)
        {
            if (!logWeaponBlocks)
            {
                return;
            }
            if (_lastWeaponBlockLogFrame == Time.frameCount)
            {
                return;
            }
            _lastWeaponBlockLogFrame = Time.frameCount;

            var ap = ActivePlayer;
            var who = GetPlayerLabel(ap);
            Debug.Log($"[Turn] Weapon blocked: from={from} weapon={weapon} reason={reason} locked={_turnLockedWeapon} shotUsed={_turnShotUsed} ropeOnly={_ropeOnlyThisTurn} active={who}");
        }

        public void NotifyClawGunReleased()
        {
            if (_gameEnded)
            {
                return;
            }

            // When the player releases fire, only rope remains available for the last seconds.
            _ropeOnlyThisTurn = true;
            _turnShotUsed = true;

            ClampRemainingTimeAfterAction();
        }

        private void ClampRemainingTimeAfterAction()
        {
            var total = Mathf.Max(0.01f, turnSeconds);
            var clamp = Mathf.Clamp(postActionClampSeconds, 0f, total);
            if (clamp <= 0.0001f)
            {
                return;
            }

            var remaining = Mathf.Max(0f, total - _turnT);
            if (remaining > clamp)
            {
                _turnT = Mathf.Max(0f, total - clamp);
            }
        }

        private void EnsureRemainingTimeAfterActionWindow()
        {
            var total = Mathf.Max(0.01f, turnSeconds);
            var window = Mathf.Clamp(postActionClampSeconds, 0f, total);
            if (window <= 0.0001f)
            {
                return;
            }

            // Guarantee exactly the post-action window from now (used for grenade post-explosion escape).
            _turnT = Mathf.Max(0f, total - window);
        }

        private void Awake()
        {
            // World reloads can create duplicates. The latest instance should take control,
            // while the previous one must stop driving turn state.
            if (Instance != null && Instance != this)
            {
                Instance.enabled = false;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnEnable()
        {
            SimpleHealth.Damaged += OnAnyDamaged;
            ExplosionController.Exploded += OnAnyExplosion;
        }

        private void OnDisable()
        {
            SimpleHealth.Damaged -= OnAnyDamaged;
            ExplosionController.Exploded -= OnAnyExplosion;
        }

        private void LateUpdate()
        {
            if (Instance != this)
            {
                return;
            }

            if (Bootstrap.IsMapMenuOpen)
            {
                return;
            }

            if (_damageReactionActive)
            {
                if (Time.time >= _damageReactionEndT)
                {
                    _damageReactionActive = false;
                    _pendingForcedTurnEnd = false;

                    // During grenade post-explosion escape window, do not end turn here.
                    if (_grenadePostExplosionEscapeWindow)
                    {
                        ApplyActiveState();
                    }
                    else
                    {
                        NextTurn("damage_reaction_end");
                    }
                }
                return;
            }

            if (_pendingForcedTurnEnd)
            {
                // Converted to a delayed reaction phase.
                _pendingForcedTurnEnd = false;
            }

            EnsurePlayers();
            if (_players == null || _players.Length < 2)
            {
                return;
            }

            EnsureTurnStartLogged();

            if (_gameEnded)
            {
                return;
            }

            if (_pendingTurnEndAfterGrenadeResolution
                && !_grenadeAwaitingExplosion
                && Time.frameCount >= _pendingTurnEndAfterGrenadeResolutionMinFrame)
            {
                _pendingTurnEndAfterGrenadeResolution = false;
                _grenadePostExplosionEscapeWindow = false;
                NextTurn("active_damage_after_grenade_resolution");
                return;
            }

            _turnT += Time.deltaTime;
            _playersReadyT += Time.deltaTime;
            if (_turnT >= Mathf.Max(1f, turnSeconds))
            {
                // Grenade fuse/air time must complete on the same active hero.
                // Do not advance turn on timeout while waiting for grenade explosion.
                if (_grenadeAwaitingExplosion)
                {
                    _turnT = Mathf.Max(0f, turnSeconds - 0.01f);
                    CheckLoseConditions();
                    return;
                }

                var ap = ActivePlayer;
                if (ap != null)
                {
                    var grapple = ap.GetComponent<GrappleController>();
                    if (grapple != null && grapple.IsAttached)
                    {
                        grapple.ForceDetach();
                    }
                }
                NextTurn("timeout");
            }

            CheckLoseConditions();
        }

        private void OnAnyDamaged(SimpleHealth health, int amount, DamageSource source)
        {
            if (Instance != this)
            {
                return;
            }

            if (health == null)
            {
                return;
            }

            if (_gameEnded)
            {
                return;
            }

            if (source != DamageSource.GrenadeExplosion
                && source != DamageSource.Fall
                && source != DamageSource.ClawGun
                && source != DamageSource.HeroDeathExplosion)
            {
                return;
            }

            if (_pendingForcedTurnEnd && _pendingForcedTurnEndFrame == Time.frameCount)
            {
                return;
            }

            var ap = ActivePlayer;
            if (ap == null)
            {
                return;
            }

            var ht = health.transform;

            var damagedHeroRoot = health.GetComponentInParent<PlayerIdentity>()?.transform;
            if (damagedHeroRoot == null) damagedHeroRoot = ht;

            var apId = ap.GetComponent<PlayerIdentity>();
            var damagedId = damagedHeroRoot != null ? damagedHeroRoot.GetComponent<PlayerIdentity>() : null;
            var apTeam = apId != null ? apId.TeamIndex : 0;
            var damagedTeam = damagedId != null ? damagedId.TeamIndex : apTeam;

            var damagedIsActive = ht == ap || ht.IsChildOf(ap);
            var damagedIsEnemy = !damagedIsActive && damagedTeam != apTeam;

            // If active hero takes fall damage or gets hit by grenade explosion during their own turn,
            // end turn immediately after damage is applied: no delayed reaction and no escape window.
            if (damagedIsActive && (source == DamageSource.Fall || source == DamageSource.GrenadeExplosion))
            {
                if (damagedHeroRoot != null)
                {
                    StartDamageFeedbackOnly(damagedHeroRoot, health, amount);
                }

                _damageReactionActive = false;
                _pendingForcedTurnEnd = false;

                // If grenade was thrown and has not exploded yet, keep the same active hero
                // until explosion resolves; then end turn immediately with no escape window.
                if (source == DamageSource.Fall && _grenadeAwaitingExplosion)
                {
                    _pendingTurnEndAfterGrenadeResolution = true;
                    _pendingTurnEndAfterGrenadeResolutionMinFrame = Time.frameCount + 1;
                    _grenadePostExplosionEscapeWindow = false;
                    ApplyActiveState();
                    return;
                }

                _pendingTurnEndAfterGrenadeResolution = false;
                _grenadeAwaitingExplosion = false;
                _grenadePostExplosionEscapeWindow = false;
                NextTurn(source == DamageSource.Fall ? "active_fall_damage" : "active_self_grenade_damage");
                return;
            }

            // Strict timing rule: remaining time may change only after shot/damage.
            // Exception for ClawGun burst: defer clamp until fire release
            // (NotifyClawGunReleased), otherwise the escape window is consumed while firing.
            if (!(source == DamageSource.ClawGun && !_ropeOnlyThisTurn))
            {
                ClampRemainingTimeAfterAction();
            }

            if (damagedIsActive)
            {
                StartDamageReaction(ap, health, amount);
                _pendingForcedTurnEnd = true;
                _pendingForcedTurnEndFrame = Time.frameCount;
            }
            else if (damagedIsEnemy && source != DamageSource.Fall)
            {
                if (damagedHeroRoot != null)
                {
                    StartDamageFeedbackOnly(damagedHeroRoot, health, amount);
                }
            }
        }

        private void StartDamageFeedbackOnly(Transform heroRoot, SimpleHealth health, int amount)
        {
            if (heroRoot == null || health == null)
            {
                return;
            }

            var after = Mathf.Clamp(health.HP, 0, health.MaxHP);
            var before = Mathf.Clamp(after + Mathf.Max(0, amount), 0, health.MaxHP);

            if (_hpAnims.TryGetValue(heroRoot, out var existing))
            {
                before = existing.To;
            }

            var anim = new HpAnim
            {
                From = before,
                To = after,
                StartT = Time.time,
                Duration = Mathf.Max(0.01f, damageReactionSeconds)
            };
            _hpAnims[heroRoot] = anim;

            TryPlayHurtAnimation(heroRoot.gameObject);
        }

        private void StartDamageReaction(Transform activePlayer, SimpleHealth health, int amount)
        {
            if (_damageReactionActive)
            {
                return;
            }

            if (activePlayer == null || health == null)
            {
                return;
            }

            var after = Mathf.Clamp(health.HP, 0, health.MaxHP);
            var before = Mathf.Clamp(after + Mathf.Max(0, amount), 0, health.MaxHP);

            if (_hpAnims.TryGetValue(activePlayer, out var existing))
            {
                before = existing.To;
            }

            var anim = new HpAnim
            {
                From = before,
                To = after,
                StartT = Time.time,
                Duration = Mathf.Max(0.01f, damageReactionSeconds)
            };
            _hpAnims[activePlayer] = anim;

            TryPlayHurtAnimation(activePlayer.gameObject);

            _damageReactionActive = true;
            _damageReactionEndT = Time.time + Mathf.Max(0.01f, damageReactionSeconds);
            ApplyInputDisabled();
        }

        private static void TryPlayHurtAnimation(GameObject hero)
        {
            if (hero == null)
            {
                return;
            }

            var animator = hero.GetComponent<Animator>();
            if (animator == null)
            {
                animator = hero.GetComponentInChildren<Animator>();
            }
            if (animator == null)
            {
                return;
            }

            // Best-effort: trigger common hurt names if present.
            if (HasAnimatorTrigger(animator, "Hurt")) animator.SetTrigger("Hurt");
            if (HasAnimatorTrigger(animator, "Hit")) animator.SetTrigger("Hit");
        }

        private static bool HasAnimatorTrigger(Animator animator, string triggerName)
        {
            if (animator == null || string.IsNullOrEmpty(triggerName))
            {
                return false;
            }

            var ps = animator.parameters;
            if (ps == null) return false;
            for (var i = 0; i < ps.Length; i++)
            {
                if (ps[i].type == AnimatorControllerParameterType.Trigger && ps[i].name == triggerName)
                {
                    return true;
                }
            }
            return false;
        }

        private void EnsurePlayers()
        {
            var prevActive = ActivePlayer;
            var prevTurnT = _turnT;
            var prevPlayersReadyT = _playersReadyT;

            if (_players != null && _players.Length >= 2)
            {
                var ok = true;
                for (var i = 0; i < _players.Length; i++)
                {
                    if (_players[i] == null)
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok) return;
            }

            var world = GameObject.Find("World");
            if (world == null)
            {
                return;
            }

            var worldId = world.GetInstanceID();
            var isNewWorld = worldId != _worldInstanceId;
            _worldInstanceId = worldId;

            var ids = world.GetComponentsInChildren<PlayerIdentity>(includeInactive: true);
            if (ids == null || ids.Length < 2)
            {
                return;
            }

            System.Array.Sort(ids, (a, b) =>
            {
                if (a == null && b == null) return 0;
                if (a == null) return 1;
                if (b == null) return -1;
                var pc = a.PlayerIndex.CompareTo(b.PlayerIndex);
                if (pc != 0) return pc;
                return a.TeamIndex.CompareTo(b.TeamIndex);
            });

            var tmp = new System.Collections.Generic.List<Transform>(ids.Length);
            for (var i = 0; i < ids.Length; i++)
            {
                if (ids[i] != null) tmp.Add(ids[i].transform);
            }
            if (tmp.Count < 2) return;
            _players = tmp.ToArray();

            RebuildTurnOrder();

            if (isNewWorld)
            {
                // New round/world generation: restore ammo to initial values for every hero.
                for (var i = 0; i < _players.Length; i++)
                {
                    var t = _players[i];
                    if (t == null) continue;
                    var grenade = t.GetComponent<HeroGrenadeThrower>();
                    if (grenade != null) grenade.ResetAmmo();
                    var claw = t.GetComponent<HeroClawGun>();
                    if (claw != null) claw.ResetAmmo();
                    var tp = t.GetComponent<HeroTeleport>();
                    if (tp != null) tp.ResetMatchUsage();
                }
            }

            if (isNewWorld)
            {
                var startTeam = _nextStartTeam >= 0 ? _nextStartTeam : Mathf.Clamp(spidersTeamIndex, 0, 1);

                _turnOrderCursor = 0;
                for (var i = 0; i < _turnOrder.Count; i++)
                {
                    var t = _turnOrder[i];
                    var id = t != null ? t.GetComponent<PlayerIdentity>() : null;
                    if (id != null && id.TeamIndex == startTeam)
                    {
                        _turnOrderCursor = i;
                        break;
                    }
                }

                ResolveCursorToAlive(preferSameTeam: true);
            }
            else
            {
                // Keep the same active player if still present and alive; otherwise resolve via the list.
                if (prevActive != null)
                {
                    var found = false;
                    for (var i = 0; i < _turnOrder.Count; i++)
                    {
                        if (_turnOrder[i] == prevActive)
                        {
                            _turnOrderCursor = i;
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        _turnOrderCursor = 0;
                    }
                }
                ResolveCursorToAlive(preferSameTeam: true);
            }
            var sameActive = prevActive != null && ActivePlayer == prevActive;
            if (!sameActive)
            {
                _turnT = 0f;
                _playersReadyT = 0f;

                _turnLockedWeapon = TurnWeapon.None;
                _turnShotUsed = false;
                _ropeOnlyThisTurn = false;
            }
            else
            {
                _turnT = prevTurnT;
                _playersReadyT = prevPlayersReadyT;
            }
            ApplyActiveState();
        }

        private void NextTurn(string reason)
        {
            LogTurnEnd(reason);
            AdvanceTurnByOrder();
            _turnT = 0f;
            _turnLockedWeapon = TurnWeapon.None;
            _turnShotUsed = false;
            _ropeOnlyThisTurn = false;
            _grenadeAwaitingExplosion = false;
            _grenadePostExplosionEscapeWindow = false;
            _pendingTurnEndAfterGrenadeResolution = false;
            _pendingTurnEndAfterGrenadeResolutionMinFrame = 0;
            ApplyActiveState();

            EnsureTurnStartLogged();
        }

        private void EnsureTurnStartLogged()
        {
            if (!logTurnOrder)
            {
                return;
            }

            var ap = ActivePlayer;
            if (ap == null)
            {
                return;
            }

            var id = ap.GetInstanceID();
            if (_lastLoggedActiveInstanceId == id)
            {
                return;
            }

            _lastLoggedActiveInstanceId = id;
            var name = GetPlayerLabel(ap);
            var next = GetNextPlayerLabel();
            Debug.Log($"[Turn] START #{_turnLogIndex + 1}: {name} -> next: {next}");
            PrintTurnHistory();
        }

        private void LogTurnEnd(string reason)
        {
            if (!logTurnOrder)
            {
                return;
            }

            var ap = ActivePlayer;
            if (ap == null)
            {
                return;
            }

            var pid = ap.GetComponent<PlayerIdentity>();
            var team = pid != null ? pid.TeamIndex : -1;
            var entry = new TurnLogEntry
            {
                Index = ++_turnLogIndex,
                Player = GetPlayerLabel(ap),
                Team = team,
                Duration = _turnT,
                Reason = string.IsNullOrEmpty(reason) ? "turn_end" : reason,
                Next = GetNextPlayerLabel()
            };

            _turnLogHistory.Add(entry);
            var max = Mathf.Clamp(turnLogHistorySize, 0, 50);
            if (max > 0)
            {
                while (_turnLogHistory.Count > max)
                {
                    _turnLogHistory.RemoveAt(0);
                }
            }

            Debug.Log($"[Turn] END   #{entry.Index}: {entry.Player} (team {entry.Team}) duration={entry.Duration:0.00}s reason={entry.Reason} -> next: {entry.Next}");
        }

        private void PrintTurnHistory()
        {
            if (!logTurnOrder)
            {
                return;
            }
            if (_turnLogHistory == null || _turnLogHistory.Count == 0)
            {
                return;
            }

            var s = "[Turn] History:\n";
            for (var i = 0; i < _turnLogHistory.Count; i++)
            {
                var e = _turnLogHistory[i];
                s += $"  #{e.Index}: {e.Player} (team {e.Team}) {e.Duration:0.00}s reason={e.Reason} -> {e.Next}\n";
            }
            Debug.Log(s);
        }

        private string GetPlayerLabel(Transform t)
        {
            if (t == null) return "(null)";
            var id = t.GetComponent<PlayerIdentity>();
            var name = id != null ? id.PlayerName : t.name;
            if (id == null) return name;
            return $"{name} [T{id.TeamIndex} P{id.PlayerIndex}]";
        }

        private string GetNextPlayerLabel()
        {
            if (_turnOrder == null || _turnOrder.Count == 0) return "(none)";

            // Predict using the same strict alternation logic as AdvanceTurnByOrder(), but without mutating state.
            var count = _turnOrder.Count;
            var curCursor = Mathf.Clamp(_turnOrderCursor, 0, count - 1);

            // Re-sync cursor to the actual active player if possible (cursor may drift after rebuild/resolve).
            var prev = ActivePlayer;
            if (prev != null)
            {
                for (var i = 0; i < count; i++)
                {
                    if (_turnOrder[i] == prev)
                    {
                        curCursor = i;
                        break;
                    }
                }
            }

            prev = _turnOrder[curCursor];
            var prevTeam = GetTeamIndex(prev);
            var desiredTeam = prevTeam == 0 ? 1 : (prevTeam == 1 ? 0 : -1);

            var startIdx = (curCursor + 1) % count;

            if (desiredTeam >= 0)
            {
                var anyDesiredAlive = false;
                for (var i = 0; i < count; i++)
                {
                    var t = _turnOrder[i];
                    if (!IsAlive(t)) continue;
                    if (GetTeamIndex(t) == desiredTeam)
                    {
                        anyDesiredAlive = true;
                        break;
                    }
                }

                if (anyDesiredAlive)
                {
                    for (var step = 0; step < count; step++)
                    {
                        var idx = (startIdx + step) % count;
                        var t = _turnOrder[idx];
                        if (!IsAlive(t)) continue;
                        if (GetTeamIndex(t) == desiredTeam)
                        {
                            return GetPlayerLabel(t);
                        }
                    }
                }
            }

            // Fallback: next alive in order.
            for (var step = 0; step < count; step++)
            {
                var idx = (startIdx + step) % count;
                var t = _turnOrder[idx];
                if (IsAlive(t))
                {
                    return GetPlayerLabel(t);
                }
            }

            return "(none)";
        }

        private void AdvanceTurnByOrder()
        {
            if (_turnOrder == null || _turnOrder.Count == 0)
            {
                return;
            }

            // Strict alternation: base the decision on the team of the PREVIOUS active hero.
            // IMPORTANT: _turnOrderCursor can drift (e.g., after rebuilds/resolve), so re-sync it
            // with the actual ActivePlayer before deciding the desired team.
            var prev = ActivePlayer;
            var prevCursor = Mathf.Clamp(_turnOrderCursor, 0, _turnOrder.Count - 1);
            if (prev != null)
            {
                for (var i = 0; i < _turnOrder.Count; i++)
                {
                    if (_turnOrder[i] == prev)
                    {
                        prevCursor = i;
                        _turnOrderCursor = i;
                        break;
                    }
                }
            }

            prev = _turnOrder[prevCursor];
            var prevTeam = GetTeamIndex(prev);
            if (prevTeam < 0)
            {
                // The active hero can be destroyed during damage/fall reaction and become null by the time
                // we advance the turn. Preserve strict alternation using the last known active team.
                prevTeam = _lastKnownActiveTeam;
            }
            var desiredTeam = prevTeam == 0 ? 1 : (prevTeam == 1 ? 0 : -1);

            _turnOrderCursor = (prevCursor + 1) % _turnOrder.Count;

            var anyDesiredAlive = desiredTeam >= 0;
            if (anyDesiredAlive)
            {
                anyDesiredAlive = false;
                for (var i = 0; i < _turnOrder.Count; i++)
                {
                    var t = _turnOrder[i];
                    if (!IsAlive(t)) continue;
                    if (GetTeamIndex(t) == desiredTeam)
                    {
                        anyDesiredAlive = true;
                        break;
                    }
                }
            }

            if (anyDesiredAlive)
            {
                for (var step = 0; step < _turnOrder.Count; step++)
                {
                    var idx = (_turnOrderCursor + step) % _turnOrder.Count;
                    var t = _turnOrder[idx];
                    if (!IsAlive(t)) continue;
                    if (GetTeamIndex(t) == desiredTeam)
                    {
                        _turnOrderCursor = idx;
                        return;
                    }
                }

                if (logTurnOrder)
                {
                    Debug.Log($"[Turn] WARN alternation: desiredTeam={desiredTeam} has alive players but none found from cursor. prev={GetPlayerLabel(prev)}");
                }
            }

            // Fallback: pick the next alive hero in order.
            ResolveCursorToAlive(preferSameTeam: false);

            if (anyDesiredAlive && desiredTeam >= 0)
            {
                var chosen = ActivePlayer;
                var chosenTeam = GetTeamIndex(chosen);
                if (chosenTeam != desiredTeam && logTurnOrder)
                {
                    Debug.Log($"[Turn] WARN alternation: chose team={chosenTeam} instead of desiredTeam={desiredTeam}. prev={GetPlayerLabel(prev)} chosen={GetPlayerLabel(chosen)}");
                }
            }
        }

        private static int GetTeamIndex(Transform t)
        {
            if (t == null) return -1;
            var id = t.GetComponent<PlayerIdentity>();
            return id != null ? id.TeamIndex : -1;
        }

        private void RebuildTurnOrder()
        {
            _turnOrder.Clear();
            if (_players == null || _players.Length == 0)
            {
                return;
            }

            var maxSlot = 0;
            for (var i = 0; i < _players.Length; i++)
            {
                var t = _players[i];
                if (t == null) continue;
                var id = t.GetComponent<PlayerIdentity>();
                if (id == null) continue;
                maxSlot = Mathf.Max(maxSlot, Mathf.Max(0, id.PlayerIndex));
            }

            var spiders = Mathf.Clamp(spidersTeamIndex, 0, 1);
            var other = 1 - spiders;
            for (var slot = 0; slot <= maxSlot; slot++)
            {
                var s = FindPlayer(spiders, slot);
                if (s != null) _turnOrder.Add(s);
                var o = FindPlayer(other, slot);
                if (o != null) _turnOrder.Add(o);
            }

            if (_turnOrderCursor < 0 || _turnOrderCursor >= _turnOrder.Count)
            {
                _turnOrderCursor = 0;
            }
        }

        private Transform FindPlayer(int team, int slot)
        {
            if (_players == null) return null;
            for (var i = 0; i < _players.Length; i++)
            {
                var t = _players[i];
                if (t == null) continue;
                var id = t.GetComponent<PlayerIdentity>();
                if (id == null) continue;
                if (id.TeamIndex == team && id.PlayerIndex == slot)
                {
                    return t;
                }
            }
            return null;
        }

        private void ResolveCursorToAlive(bool preferSameTeam)
        {
            if (_turnOrder == null || _turnOrder.Count == 0)
            {
                return;
            }

            _turnOrderCursor = Mathf.Clamp(_turnOrderCursor, 0, _turnOrder.Count - 1);

            var cur = _turnOrder[_turnOrderCursor];
            if (IsAlive(cur))
            {
                return;
            }

            var curTeam = -1;
            if (cur != null)
            {
                var id = cur.GetComponent<PlayerIdentity>();
                if (id != null) curTeam = id.TeamIndex;
            }

            if (preferSameTeam && curTeam >= 0)
            {
                for (var step = 1; step <= _turnOrder.Count; step++)
                {
                    var idx = (_turnOrderCursor + step) % _turnOrder.Count;
                    var t = _turnOrder[idx];
                    if (!IsAlive(t)) continue;
                    var id = t != null ? t.GetComponent<PlayerIdentity>() : null;
                    if (id != null && id.TeamIndex == curTeam)
                    {
                        _turnOrderCursor = idx;
                        return;
                    }
                }
            }

            for (var step = 1; step <= _turnOrder.Count; step++)
            {
                var idx = (_turnOrderCursor + step) % _turnOrder.Count;
                if (IsAlive(_turnOrder[idx]))
                {
                    _turnOrderCursor = idx;
                    return;
                }
            }
        }

        private static bool IsAlive(Transform t)
        {
            if (t == null) return false;
            var h = t.GetComponent<SimpleHealth>();
            return h == null || h.HP > 0;
        }

        private void ApplyActiveState()
        {
            if (_damageReactionActive)
            {
                ApplyInputDisabled();
                return;
            }

            var ap = ActivePlayer;
            _activeIndex = -1;
            if (_players != null && ap != null)
            {
                for (var i = 0; i < _players.Length; i++)
                {
                    if (_players[i] == ap)
                    {
                        _activeIndex = i;
                        break;
                    }
                }
            }

            for (var i = 0; i < _players.Length; i++)
            {
                var t = _players[i];
                if (t == null) continue;
                var active = t == ap;
                SetHeroInputEnabled(t.gameObject, active);
            }
            if (ap != null)
            {
                var apId0 = ap.GetComponent<PlayerIdentity>();
                _lastKnownActiveTeam = apId0 != null ? apId0.TeamIndex : _lastKnownActiveTeam;

                var ammo = ap.GetComponent<HeroAmmoCarousel>();
                // Do not reset weapon selection every time ApplyActiveState() runs.
                // This method is called for many reasons (clamp after action, damage reaction, etc.),
                // and forcing Rope mid-turn cancels held fire and can incorrectly trigger rope-only mode.
                var apId = ap.gameObject.GetInstanceID();
                if (apId != _lastAppliedActiveInstanceId)
                {
                    _lastAppliedActiveInstanceId = apId;
                    if (ammo != null) ammo.ForceSelectRope();
                }

                var bot = ap.GetComponent<WormCrawlerPrototype.AI.SpiderBotController>();
                var apPid = ap.GetComponent<PlayerIdentity>();
                var isBotControlled = bot != null && Bootstrap.VsCpu && apPid != null && apPid.TeamIndex != spidersTeamIndex;
                if (isBotControlled)
                {
                    var id = ap.gameObject.GetInstanceID();
                    if (_lastBotTurnPlayerInstanceId != id)
                    {
                        _lastBotTurnPlayerInstanceId = id;
                        bot.StartTurn(this);
                    }
                }
                else
                {
                    _lastBotTurnPlayerInstanceId = 0;
                }
            }
        }

        private static void SetHeroInputEnabled(GameObject hero, bool enabled)
        {
            var bot = hero.GetComponent<WormCrawlerPrototype.AI.SpiderBotController>();
            var pid = hero.GetComponent<PlayerIdentity>();
            var isBotControlled = bot != null && Bootstrap.VsCpu && pid != null && pid.TeamIndex == 1;
            var inputEnabled = enabled && !isBotControlled;

            var walker = hero.GetComponent<HeroSurfaceWalker>();

            var simpleHero = hero.GetComponent<SimpleHero>();
            if (simpleHero != null)
            {
                // Legacy controller is deprecated; keep it disabled to avoid
                // mixed movement pipelines and inconsistent behavior.
                simpleHero.enabled = false;
            }

            var aim = hero.GetComponent<WormAimController>();
            if (aim != null)
            {
                aim.enabled = true;
                aim.InputEnabled = inputEnabled;
            }

            if (walker != null)
            {
                if (!enabled)
                {
                    walker.SetExternalMoveOverride(false, 0f);
                    walker.SetAdditionalMoveInput(false, 0f);
                }
                walker.InputEnabled = inputEnabled;
            }

            var ammo = hero.GetComponent<HeroAmmoCarousel>();
            if (ammo != null) ammo.enabled = inputEnabled;

            var grenade = hero.GetComponent<HeroGrenadeThrower>();
            if (grenade != null)
            {
                grenade.enabled = true;
                grenade.InputEnabled = inputEnabled;
            }

            var claw = hero.GetComponent<HeroClawGun>();
            if (claw != null)
            {
                if (!inputEnabled) claw.ForceStop();
                claw.enabled = true;
                claw.InputEnabled = inputEnabled;
            }

            var grapple = hero.GetComponent<GrappleController>();
            if (grapple != null)
            {
                if (!enabled)
                {
                    grapple.SetAdditionalMoveInput(false, 0f, 0f);
                    grapple.SetExternalMoveOverride(false, 0f, 0f);
                    if (grapple.IsAttached)
                    {
                        grapple.ForceDetach();
                    }
                }

                // Bots use external overrides; disable input without forcing detach.
                grapple.DetachWhenInputDisabled = !isBotControlled;
                grapple.InputEnabled = inputEnabled;
            }
        }

        private void CheckLoseConditions()
        {
            if (_playersReadyT < Mathf.Max(0f, loseGraceSeconds))
            {
                return;
            }

            var team0Alive = 0;
            var team1Alive = 0;

            for (var i = 0; i < _players.Length; i++)
            {
                var t = _players[i];
                if (t == null)
                {
                    continue;
                }

                var id = t.GetComponent<PlayerIdentity>();
                var team = id != null ? id.TeamIndex : 0;

                var dead = false;
                if (t.position.y < loseBelowY)
                {
                    dead = true;

                    // Ensure out-of-bounds heroes are removed instead of being snapped back by any safety/respawn logic.
                    var hKill = t.GetComponent<SimpleHealth>();
                    if (hKill != null && hKill.HP > 0)
                    {
                        hKill.TakeDamage(hKill.HP, DamageSource.Generic);
                    }
                    else
                    {
                        Destroy(t.gameObject);
                    }
                }
                var h = t.GetComponent<SimpleHealth>();
                if (h != null && h.HP <= 0)
                {
                    dead = true;
                }

                if (!dead)
                {
                    if (team == 0) team0Alive++;
                    else team1Alive++;
                }
            }

            if (team0Alive > 0 && team1Alive > 0)
            {
                if (!IsAlive(ActivePlayer))
                {
                    // If the active hero died during their own turn, advance the turn to preserve
                    // strict alternation.
                    NextTurn("active_died");
                }
                return;
            }

            if (team0Alive <= 0 && team1Alive <= 0)
            {
                EndGame(winnerTeam: 0);
                return;
            }

            var winnerTeam = team0Alive > 0 ? 0 : 1;
            EndGame(winnerTeam);
        }

        private void EndGame(int winnerTeam)
        {
            winnerTeam = Mathf.Clamp(winnerTeam, 0, 1);
            var loserTeam = 1 - winnerTeam;

            _nextStartTeam = loserTeam;

            if (winnerTeam == 0) _team0Wins++;
            else _team1Wins++;

            _winnerName = winnerTeam == 0 ? "Team Spider" : "Team Red";
            _gameEnded = true;

            _showEndRoundMenu = true;
            _endRoundSelectedIndex = 0;

            var total = PlayerPrefs.GetInt("WormCrawler_TotalGames", 0);
            PlayerPrefs.SetInt("WormCrawler_TotalGames", total + 1);
            var k = winnerTeam == 0 ? "WormCrawler_WinsTeam0" : "WormCrawler_WinsTeam1";
            PlayerPrefs.SetInt(k, PlayerPrefs.GetInt(k, 0) + 1);
            PlayerPrefs.SetString("WormCrawler_LastDuel", $"{_winnerName} победил { (loserTeam == 0 ? "Team Spider" : "Team Red") }");
            PlayerPrefs.Save();

            ApplyInputDisabled();
        }

        private void ApplyInputDisabled()
        {
            if (_players == null) return;
            for (var i = 0; i < _players.Length; i++)
            {
                var t = _players[i];
                if (t == null) continue;
                SetHeroInputEnabled(t.gameObject, false);
            }
        }

        private void OnGUI()
        {
            if (Instance != this)
            {
                return;
            }

            if (_players == null || _players.Length == 0)
            {
                return;
            }

            {
                var ap = ActivePlayer;
                var apId = ap != null ? ap.GetComponent<PlayerIdentity>() : null;
                var apName = apId != null ? apId.PlayerName : "";

                var hudFont = Mathf.Clamp(Mathf.RoundToInt(Screen.height * 0.032f), 18, 44);
                var hudH = Mathf.Max(28f, hudFont * 1.35f);
                var pad = Mathf.Max(10f, hudFont * 0.4f);
                var midGap = Mathf.Max(16f, hudFont * 0.6f);

                var leftRect = new Rect(pad, pad, Screen.width * 0.33f, hudH);
                var rightRect = new Rect(Screen.width - pad - Screen.width * 0.33f, pad, Screen.width * 0.33f, hudH);
                var centerRect = new Rect(Screen.width * 0.5f + midGap, pad, Screen.width * 0.5f - pad - midGap, hudH);

                var styleL = new GUIStyle(GUI.skin.label);
                styleL.fontSize = hudFont;
                styleL.alignment = TextAnchor.MiddleLeft;

                var styleR = new GUIStyle(styleL);
                styleR.alignment = TextAnchor.MiddleRight;

                var styleC = new GUIStyle(styleL);
                styleC.alignment = TextAnchor.MiddleCenter;

                var prevCol = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.45f);
                var bgPadX = Mathf.Max(8f, hudFont * 0.55f);
                var bgPadY = Mathf.Max(4f, hudFont * 0.25f);
                var bgH = Mathf.Clamp(hudH - bgPadY * 2f, 18f, hudH);
                var bgY = pad + (hudH - bgH) * 0.5f;

                var leftText = $"SPIDER  {_team0Wins}";
                var rightText = $"RED  {_team1Wins}";
                var centerText = $"{apName}   {SecondsLeft}s";

                var leftSize = styleL.CalcSize(new GUIContent(leftText));
                var rightSize = styleR.CalcSize(new GUIContent(rightText));
                var centerSize = styleC.CalcSize(new GUIContent(centerText));

                var leftBgW = Mathf.Min(leftRect.width, leftSize.x + bgPadX * 2f);
                var rightBgW = Mathf.Min(rightRect.width, rightSize.x + bgPadX * 2f);
                var centerBgW = Mathf.Min(centerRect.width, centerSize.x + bgPadX * 2f);

                var leftBg = new Rect(leftRect.x, bgY, leftBgW, bgH);
                var rightBg = new Rect(rightRect.xMax - rightBgW, bgY, rightBgW, bgH);
                var centerBg = new Rect(centerRect.x, bgY, centerBgW, bgH);

                GUI.Box(leftBg, GUIContent.none);
                GUI.Box(rightBg, GUIContent.none);
                GUI.Box(centerBg, GUIContent.none);
                GUI.color = prevCol;

                GUI.Label(leftRect, leftText, styleL);
                GUI.Label(rightRect, rightText, styleR);
                GUI.Label(centerRect, centerText, styleC);
            }

            var cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            for (var i = 0; i < _players.Length; i++)
            {
                var t = _players[i];
                if (t == null) continue;
                var id = t.GetComponent<PlayerIdentity>();
                var health = t.GetComponent<SimpleHealth>();

                var name = id != null ? id.PlayerName : (i == 0 ? "Player 1" : "Player 2");
                var hp = health != null ? health.HP : 0;

                if (t != null && _hpAnims.TryGetValue(t, out var anim))
                {
                    var dt = Time.time - anim.StartT;
                    var k = anim.Duration > 0f ? Mathf.Clamp01(dt / anim.Duration) : 1f;
                    hp = Mathf.RoundToInt(Mathf.Lerp(anim.From, anim.To, k));
                    if (k >= 1f)
                    {
                        _hpAnims.Remove(t);
                    }
                }

                var world = (Vector2)t.position + Vector2.up * 1.6f;
                var sp = cam.WorldToScreenPoint(new Vector3(world.x, world.y, 0f));
                if (sp.z < 0f) continue;

                var w = 180f;
                var h = 36f;
                var r = new Rect(sp.x - w * 0.5f, Screen.height - sp.y - h, w, h);

                var prev = GUI.color;
                GUI.color = i == _activeIndex ? new Color(1f, 1f, 1f, 1f) : new Color(1f, 1f, 1f, 0.6f);
                GUI.Label(r, $"{name}\nHP: {hp}");
                GUI.color = prev;
            }

            if (_gameEnded)
            {
                var r = new Rect(0f, 20f, Screen.width, 40f);
                GUI.Label(r, $"Winner: {_winnerName}");

                if (_showEndRoundMenu)
                {
                    HandleEndRoundMenuKeyboard();
                    DrawEndRoundMenu();
                }
            }
        }

        private void HandleEndRoundMenuKeyboard()
        {
            if (UnityEngine.InputSystem.Keyboard.current == null)
            {
                return;
            }

            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb.downArrowKey.wasPressedThisFrame)
            {
                _endRoundSelectedIndex = Mathf.Clamp(_endRoundSelectedIndex + 1, 0, 1);
            }
            else if (kb.upArrowKey.wasPressedThisFrame)
            {
                _endRoundSelectedIndex = Mathf.Clamp(_endRoundSelectedIndex - 1, 0, 1);
            }
            else if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
            {
                ActivateEndRoundSelection(_endRoundSelectedIndex);
            }
        }

        private void ActivateEndRoundSelection(int index)
        {
            if (index == 0)
            {
                _showEndRoundMenu = false;
                Bootstrap.RestartMatch();
                return;
            }

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void DrawEndRoundMenu()
        {
            var w = Mathf.Min(420f, Screen.width - 40f);
            var h = 170f;
            var x = (Screen.width - w) * 0.5f;
            var y = (Screen.height - h) * 0.5f;
            var rect = new Rect(x, y, w, h);

            GUI.Box(rect, "Round finished");

            var inner = new Rect(rect.x + 12f, rect.y + 34f, rect.width - 24f, rect.height - 46f);
            GUI.Label(new Rect(inner.x, inner.y, inner.width, 24f), $"Winner: {_winnerName}");

            var btnW = Mathf.Min(220f, inner.width);
            var bx = inner.x + (inner.width - btnW) * 0.5f;
            var by = inner.y + 40f;

            DrawSelectableButton(new Rect(bx, by, btnW, 34f), "Continue", selected: _endRoundSelectedIndex == 0, onClick: () => ActivateEndRoundSelection(0));
            by += 44f;
            DrawSelectableButton(new Rect(bx, by, btnW, 34f), "Exit", selected: _endRoundSelectedIndex == 1, onClick: () => ActivateEndRoundSelection(1));
        }

        private static void DrawSelectableButton(Rect rect, string label, bool selected, System.Action onClick)
        {
            var prev = GUI.color;
            GUI.color = selected ? new Color(0.25f, 0.55f, 1f, 1f) : prev;
            if (GUI.Button(rect, label))
            {
                onClick?.Invoke();
            }
            GUI.color = prev;
        }
    }
}
