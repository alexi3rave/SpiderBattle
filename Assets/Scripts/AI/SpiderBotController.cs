using System.Collections;
using UnityEngine;
using WormCrawlerPrototype;

namespace WormCrawlerPrototype.AI
{
    public sealed class SpiderBotController : MonoBehaviour
    {
        private sealed class IntHolder
        {
            public int Value;
        }

        private static void EnsureBotStatsCapacity(int team)
        {
            var idx = Mathf.Max(0, team);
            var need = idx + 1;
            if (_botTeamShotsTotal == null || _botTeamShotsTotal.Length < need)
            {
                var newSize = Mathf.Max(2, need);
                System.Array.Resize(ref _botTeamShotsTotal, newSize);
                System.Array.Resize(ref _botTeamShotsClaw, newSize);
                System.Array.Resize(ref _botTeamLastLoggedTotal, newSize);
            }
        }

        private TurnManager.TurnWeapon SelectWeaponForTeamBalance(int team, bool canClaw, bool canGrenade, bool closeClaw)
        {
            if (canClaw && !canGrenade)
            {
                return TurnManager.TurnWeapon.ClawGun;
            }
            if (!canClaw && canGrenade)
            {
                return TurnManager.TurnWeapon.Grenade;
            }
            if (!canClaw && !canGrenade)
            {
                return TurnManager.TurnWeapon.Grenade;
            }

            if (closeClaw)
            {
                return TurnManager.TurnWeapon.ClawGun;
            }

            EnsureBotStatsCapacity(team);
            var total = _botTeamShotsTotal[team];
            var claw = _botTeamShotsClaw[team];
            var frac = total > 0 ? (float)claw / total : 0f;
            var target = Mathf.Clamp01(targetClawShotFractionPerTeam);

            if (frac < target)
            {
                return TurnManager.TurnWeapon.ClawGun;
            }

            return TurnManager.TurnWeapon.Grenade;
        }

        private void RegisterBotShot(int team, bool isClaw)
        {
            EnsureBotStatsCapacity(team);
            _botTeamShotsTotal[team]++;
            if (isClaw) _botTeamShotsClaw[team]++;

            if (!debugLogs)
            {
                return;
            }

            var total = _botTeamShotsTotal[team];
            if (total < 1)
            {
                return;
            }

            if (total - _botTeamLastLoggedTotal[team] < 5)
            {
                return;
            }
            _botTeamLastLoggedTotal[team] = total;

            var claw = _botTeamShotsClaw[team];
            var frac = (float)claw / total;
            Debug.Log($"[BotFireStats] team={team} shots={total} claw={claw} claw%={Mathf.RoundToInt(frac * 100f)}");
        }

        private sealed class FloatHolder
        {
            public float Value;
        }

        private sealed class BoolHolder
        {
            public bool Value;
        }

        private enum ActionPair
        {
            None,
            Walk,
            Rope45,
            RopeVertical,
            RopeHorizontal,
        }

        private enum RopeFirePolicy
        {
            General,
            AllowDown,
        }

        [SerializeField] private bool rememberActionPairsAcrossTurns = true;
        private ActionPair _lastPair = ActionPair.None;
        private bool _horizPairFlip;
        private int _consecutiveWalkSteps;

        [SerializeField] private BotDifficulty difficulty = BotDifficulty.Normal;

        public void SetDifficulty(BotDifficulty d)
        {
            difficulty = d;
            botClawHoldSeconds = GetClawHoldSecondsForDifficulty(difficulty);
        }

        [SerializeField] private float thinkDelaySeconds = 0.55f;
        [SerializeField] private float postActionDelaySeconds = 0.25f;

        [SerializeField] private float clawGunRange = 25f;
        [SerializeField] private float grenadeRange = 18f;
        [SerializeField] private float grenadeAcceptRadius = 2.4f;

        [SerializeField] private float botClawFireDownOffsetDeg = 10f;

        [SerializeField] private float ropeAttachWaitSeconds = 0.35f;
        [SerializeField] private float ropeSwingSeconds = 0.55f;

        [SerializeField] private float approachStepSeconds = 0.5f;
        [SerializeField] private float ropeDetachMaxDescendSeconds = 1.75f;
        [SerializeField] private float ropeDownFullExtendSeconds = 5.0f;

        [SerializeField] private float approachMaxSeconds = 55f;
        [SerializeField] private float retreatAfterAttackSeconds = 7f;
        [SerializeField] private float retreatWalkBurstSeconds = 0.85f;
        [SerializeField] private float safePointSearchRadius = 18f;

        [SerializeField] private float botClawHoldSeconds = 4.5f;

        [SerializeField] private int tunnelEscapeMinFailedRopeDownAttempts = 2;
        [SerializeField] private int tunnelEscapeMaxLegs = 6;

        [SerializeField] private float closeRangeOverrideFactor = 0.5f;
        [SerializeField] private float ropePostManeuverRecheckDelay = 0.05f;
        [SerializeField] private bool debugLogs = false;

        [SerializeField, Range(0f, 1f)] private float targetClawShotFractionPerTeam = 0.5f;

        private static int[] _botTeamShotsTotal;
        private static int[] _botTeamShotsClaw;
        private static int[] _botTeamLastLoggedTotal;

        private Coroutine _routine;
        private TurnManager _turn;

        private Vector2 _turnStartPos;
        private float _turnStartTime;
        private bool _didAutoTeleportThisTurn;

        public void StartTurn(TurnManager turn)
        {
            _turn = turn;
            if (_routine != null)
            {
                StopCoroutine(_routine);
            }

            _turnStartPos = transform.position;
            _turnStartTime = Time.time;
            _didAutoTeleportThisTurn = false;
            _routine = StartCoroutine(TakeTurnRoutine());
        }

        private IEnumerator TakeTurnRoutine()
        {
            yield return new WaitForSeconds(thinkDelaySeconds);

            bool IsMyTurn()
            {
                return _turn != null && _turn.ActivePlayer == transform;
            }

            if (_turn == null || _turn.ActivePlayer == null)
            {
                if (debugLogs) Debug.Log($"[SpiderBot] Abort: no TurnManager/ActivePlayer ({name})");
                yield break;
            }

            if (!IsMyTurn())
            {
                if (debugLogs) Debug.Log($"[SpiderBot] Abort: not active player ({name})");
                yield break;
            }

            if (!rememberActionPairsAcrossTurns)
            {
                _lastPair = ActionPair.None;
            }

            var myId = GetComponent<PlayerIdentity>();
            var myTeam = myId != null ? myId.TeamIndex : 0;

            EnsureBotStatsCapacity(myTeam);

            while (IsMyTurn())
            {
                // Anti-stuck: if the bot doesn't move for a full minute, use Teleport.
                if (!_didAutoTeleportThisTurn)
                {
                    var heroH = GetHeroHeight();
                    var threshold = Mathf.Max(0.25f, heroH) * 10f;
                    var dist = Vector2.Distance(_turnStartPos, (Vector2)transform.position);
                    if (Time.time - _turnStartTime >= 60f && dist < threshold)
                    {
                        var tp = GetComponent<HeroTeleport>();
                        var ammo0 = GetComponent<HeroAmmoCarousel>();
                        if (tp != null && tp.CanUseNow && ammo0 != null)
                        {
                            ammo0.SelectTeleport();
                            tp.Enabled = true;
                            var didTp = tp.TryTeleportNowBot();
                            _didAutoTeleportThisTurn = true;
                            _turnStartPos = transform.position;
                            _turnStartTime = Time.time;

                            if (didTp)
                            {
                                yield return new WaitForSeconds(postActionDelaySeconds);
                                continue;
                            }
                        }
                        else
                        {
                            _didAutoTeleportThisTurn = true;
                        }
                    }
                }

                var target = FindBestEnemyTarget(myTeam);
                if (target == null)
                {
                    if (debugLogs) Debug.Log($"[SpiderBot] No target found ({name})");
                    yield return new WaitForSeconds(0.35f);
                    continue;
                }

                var aim = GetComponent<WormAimController>();
                var ammo = GetComponent<HeroAmmoCarousel>();
                var claw = GetComponent<HeroClawGun>();
                var grenade = GetComponent<HeroGrenadeThrower>();
                var grapple = GetComponent<GrappleController>();
                var rb = GetComponent<Rigidbody2D>();
                var walker = GetComponent<HeroSurfaceWalker>();

                var effectiveGrenadeRange = grenadeRange;
                var effectiveClawRange = clawGunRange;
                if (grenade != null)
                {
                    effectiveGrenadeRange = Mathf.Max(0.1f, grenade.GetMaxRangePublic());
                    effectiveClawRange = Mathf.Max(0.1f, effectiveGrenadeRange * 1.5f);
                }

                if (aim != null)
                {
                    aim.SetExternalAimOverride(false, Vector2.down);
                }

                var origin = aim != null ? aim.AimOriginWorld : (Vector2)transform.position;
                if (target == null)
                {
                    if (debugLogs) Debug.Log($"[SpiderBot] Target destroyed before aim ({name})");
                    yield return null;
                    continue;
                }
                var targetPoint = GetTargetAimPoint(target);
                var dirToTarget = (targetPoint - origin);
                if (dirToTarget.sqrMagnitude < 0.0001f)
                {
                    dirToTarget = Vector2.right;
                }

                yield return ApproachUntilInRange(target, myTeam, grapple, aim, walker);

                if (!IsMyTurn())
                {
                    if (debugLogs) Debug.Log($"[SpiderBot] Abort after approach: not active ({name})");
                    yield break;
                }
                if (target == null)
                {
                    if (debugLogs) Debug.Log($"[SpiderBot] Target destroyed after approach ({name})");
                    yield return null;
                    continue;
                }

                origin = aim != null ? aim.AimOriginWorld : (Vector2)transform.position;
                targetPoint = GetTargetAimPoint(target);
                dirToTarget = (targetPoint - origin);
                if (dirToTarget.sqrMagnitude < 0.0001f) dirToTarget = Vector2.right;
                var distToTarget = dirToTarget.magnitude;
                var heroH0 = GetHeroHeight();
                var grenadeExplosionRadius = Mathf.Max(0.25f, heroH0 * 2.5f);
                GetShootFlags(origin, target, myTeam, grenadeExplosionRadius, effectiveClawRange, effectiveGrenadeRange, out var canClaw, out var canGrenade, out distToTarget);

                var hasGrenadeTrajectory = false;
                var grenadeAimDir = Vector2.right;
                if (canGrenade && grenade != null)
                {
                    hasGrenadeTrajectory = TryFindGrenadeAim(grenade, targetPoint, myTeam, out grenadeAimDir);
                    if (!hasGrenadeTrajectory)
                    {
                        canGrenade = false;
                    }
                }

                if (!canClaw && !canGrenade)
                {
                    if (debugLogs) Debug.Log($"[SpiderBot] Can't shoot yet (dist {distToTarget:0.0}), canClaw={canClaw}, canGrenade={canGrenade} ({name})");
                    yield return new WaitForSeconds(0.12f);
                    continue;
                }

                // Prefer ClawGun when very close, even if grenade is also possible.
                var closeClaw = distToTarget <= Mathf.Max(0.75f, heroH0 * 1.25f);
                var weapon = SelectWeaponForTeamBalance(myTeam, canClaw, canGrenade, closeClaw);

                var desiredAimDir = dirToTarget.normalized;
                if (weapon == TurnManager.TurnWeapon.Grenade)
                {
                    if (!hasGrenadeTrajectory)
                    {
                        yield return new WaitForSeconds(0.12f);
                        continue;
                    }
                    desiredAimDir = grenadeAimDir;
                }

                if (weapon == TurnManager.TurnWeapon.ClawGun)
                {
                    if (TryFindClawAim(target, origin, targetPoint, effectiveClawRange, botClawFireDownOffsetDeg, out var clawAim))
                    {
                        desiredAimDir = clawAim;
                    }
                }

                var noisyAimDir = ComputeAimDirectionWithDifficulty(desiredAimDir);

                var finalAimDir = noisyAimDir;
                if (aim != null)
                {
                    aim.SetExternalAimOverride(true, finalAimDir);
                }

                if (weapon == TurnManager.TurnWeapon.ClawGun)
                {
                    if (ammo != null) ammo.SelectClawGun();
                    yield return new WaitForSeconds(0.15f);
                    if (claw != null)
                    {
                        claw.Enabled = true;
                        var didAttack = claw.TryFireOnceNow();
                        if (!didAttack)
                        {
                            LogAI("ClawGun TryFireOnceNow returned false");
                        }

                        if (didAttack)
                        {
                            RegisterBotShot(myTeam, isClaw: true);
                        }

                        // Continuous fire with aim tracking for the duration set by difficulty.
                        claw.SetExternalHeld(true);
                        var holdT = Time.time;
                        var holdDur = Mathf.Clamp(botClawHoldSeconds, 0.25f, 10.0f);
                        while (Time.time - holdT < holdDur)
                        {
                            if (_turn == null || _turn.ActivePlayer != transform)
                            {
                                break;
                            }
                            // Re-aim at target every tick.
                            if (target != null && aim != null)
                            {
                                var trackOrigin = aim.AimOriginWorld;
                                var trackPoint = GetTargetAimPoint(target);
                                var trackDir = (trackPoint - trackOrigin);
                                if (trackDir.sqrMagnitude > 0.0001f)
                                {
                                    if (TryFindClawAim(target, trackOrigin, trackPoint, effectiveClawRange, botClawFireDownOffsetDeg, out var trackAim))
                                    {
                                        aim.SetExternalAimOverride(true, trackAim);
                                    }
                                    else
                                    {
                                        aim.SetExternalAimOverride(true, trackDir.normalized);
                                    }
                                }
                            }
                            yield return new WaitForSeconds(0.08f);
                        }
                        claw.SetExternalHeld(false);

                        claw.Enabled = false;

                        if (!didAttack)
                        {
                            if (aim != null)
                            {
                                aim.SetExternalAimOverride(false, Vector2.down);
                            }
                            yield return new WaitForSeconds(0.12f);
                            continue;
                        }
                    }
                }
                else
                {
                    if (ammo != null) ammo.SelectGrenade();
                    yield return new WaitForSeconds(0.15f);
                    if (grenade != null)
                    {
                        grenade.Enabled = true;
                        var didAttack = grenade.TryThrowNow();
                        if (didAttack)
                        {
                            RegisterBotShot(myTeam, isClaw: false);
                        }
                        grenade.Enabled = false;

                        if (!didAttack)
                        {
                            if (aim != null)
                            {
                                aim.SetExternalAimOverride(false, Vector2.down);
                            }
                            yield return new WaitForSeconds(0.12f);
                            continue;
                        }
                    }
                }

                yield return new WaitForSeconds(postActionDelaySeconds);

                if (aim != null)
                {
                    aim.SetExternalAimOverride(false, Vector2.down);
                }

                if (_turn != null && _turn.ActivePlayer == transform)
                {
                    yield return RetreatRoutine(myTeam, grapple, aim, walker, retreatAfterAttackSeconds);
                    _turn.EndTurnAfterAttack();
                }
            }
        }

        private IEnumerator TryShootIfPossibleNowRoutine(Transform target, GrappleController grapple, WormAimController aim, HeroSurfaceWalker walker, BoolHolder shot)
        {
            if (shot != null) shot.Value = false;

            if (_turn == null || _turn.ActivePlayer != transform)
            {
                yield break;
            }

            if (target == null)
            {
                yield break;
            }

            var myId = GetComponent<PlayerIdentity>();
            var myTeam = myId != null ? myId.TeamIndex : 0;

            var ammo = GetComponent<HeroAmmoCarousel>();
            var claw = GetComponent<HeroClawGun>();
            var grenade = GetComponent<HeroGrenadeThrower>();

            var origin = aim != null ? aim.AimOriginWorld : (Vector2)transform.position;
            var targetPoint = GetTargetAimPoint(target);
            var dirToTarget = (targetPoint - origin);
            if (dirToTarget.sqrMagnitude < 0.0001f) dirToTarget = Vector2.right;

            var heroH0 = GetHeroHeight();
            var grenadeExplosionRadius = Mathf.Max(0.25f, heroH0 * 2.5f);

            var effectiveGrenadeRange = grenadeRange;
            var effectiveClawRange = clawGunRange;
            if (grenade != null)
            {
                effectiveGrenadeRange = Mathf.Max(0.1f, grenade.GetMaxRangePublic());
                effectiveClawRange = Mathf.Max(0.1f, effectiveGrenadeRange * 1.5f);
            }

            GetShootFlags(origin, target, myTeam, grenadeExplosionRadius, effectiveClawRange, effectiveGrenadeRange, out var canClaw, out var canGrenade, out var distToTarget);
            if (!canClaw && !canGrenade)
            {
                yield break;
            }

            var hasGrenadeTrajectory = false;
            var grenadeAimDir = Vector2.right;
            if (canGrenade && grenade != null)
            {
                hasGrenadeTrajectory = TryFindGrenadeAim(grenade, targetPoint, myTeam, out grenadeAimDir);
                if (!hasGrenadeTrajectory)
                {
                    canGrenade = false;
                }
            }

            if (!canClaw && !canGrenade)
            {
                yield break;
            }

            var closeClaw = distToTarget <= Mathf.Max(0.75f, heroH0 * 1.25f);
            var weapon = SelectWeaponForTeamBalance(myTeam, canClaw, canGrenade, closeClaw);

            var desiredAimDir = dirToTarget.normalized;
            if (weapon == TurnManager.TurnWeapon.Grenade)
            {
                if (grenade != null)
                {
                    if (hasGrenadeTrajectory)
                    {
                        desiredAimDir = grenadeAimDir;

                        if (difficulty == BotDifficulty.Hard && grapple != null && grapple.IsAttached)
                        {
                            if (ammo != null) ammo.SelectGrenade();
                            yield return new WaitForSeconds(0.10f);

                            grenade.Enabled = true;
                            if (aim != null) aim.SetExternalAimOverride(true, desiredAimDir);
                            yield return new WaitForSeconds(0.10f);

                            grenade.TryThrowNow();
                            RegisterBotShot(myTeam, isClaw: false);

                            if (shot != null) shot.Value = true;
                            yield break;
                        }
                    }
                    else
                    {
                        // Can't find a viable grenade throw path.
                        yield break;
                    }
                }
                else
                {
                    yield break;
                }
            }

            var aimDir = ComputeAimDirectionWithDifficulty(desiredAimDir);
            if (aim != null)
            {
                aim.SetExternalAimOverride(true, aimDir);
            }

            if (weapon == TurnManager.TurnWeapon.ClawGun)
            {
                if (ammo != null) ammo.SelectClawGun();
                yield return new WaitForSeconds(0.15f);
                if (claw != null)
                {
                    claw.Enabled = true;
                    var didAttack = claw.TryFireOnceNow();
                    if (!didAttack)
                    {
                        LogAI("(post-rope) ClawGun TryFireOnceNow returned false");
                    }
                    if (didAttack)
                    {
                        RegisterBotShot(myTeam, isClaw: true);
                    }

                    // Continuous fire with aim tracking from rope maneuver.
                    claw.SetExternalHeld(true);
                    var holdT = Time.time;
                    var holdDur = Mathf.Clamp(botClawHoldSeconds, 0.25f, 10.0f);
                    while (Time.time - holdT < holdDur)
                    {
                        if (_turn == null || _turn.ActivePlayer != transform)
                        {
                            break;
                        }
                        if (target != null && aim != null)
                        {
                            var trackOrigin = aim.AimOriginWorld;
                            var trackPoint = GetTargetAimPoint(target);
                            var trackDir = (trackPoint - trackOrigin);
                            if (trackDir.sqrMagnitude > 0.0001f)
                            {
                                if (TryFindClawAim(target, trackOrigin, trackPoint, effectiveClawRange, botClawFireDownOffsetDeg, out var trackAim))
                                {
                                    aim.SetExternalAimOverride(true, trackAim);
                                }
                                else
                                {
                                    aim.SetExternalAimOverride(true, trackDir.normalized);
                                }
                            }
                        }
                        yield return new WaitForSeconds(0.08f);
                    }
                    claw.SetExternalHeld(false);
                    claw.Enabled = false;

                    if (!didAttack)
                    {
                        if (shot != null) shot.Value = false;
                        yield break;
                    }
                }
            }
            else
            {
                if (ammo != null) ammo.SelectGrenade();
                yield return new WaitForSeconds(0.15f);
                if (grenade != null)
                {
                    grenade.Enabled = true;
                    var didAttack = grenade.TryThrowNow();
                    if (didAttack)
                    {
                        RegisterBotShot(myTeam, isClaw: false);
                    }
                    else
                    {
                        if (shot != null) shot.Value = false;
                        yield break;
                    }
                }
            }

            yield return new WaitForSeconds(postActionDelaySeconds);
            if (aim != null)
            {
                aim.SetExternalAimOverride(false, Vector2.down);
            }

            if (_turn != null && _turn.ActivePlayer == transform)
            {
                yield return RetreatRoutine(myTeam, grapple, aim, walker, retreatAfterAttackSeconds);
                _turn.EndTurnAfterAttack();
            }

            if (shot != null) shot.Value = true;
        }

        private IEnumerator TunnelEscapeRoutine(Transform target, int myTeam, GrappleController grapple, WormAimController aim, HeroSurfaceWalker walker)
        {
            if (target == null || walker == null)
            {
                yield break;
            }

            var basePos = (Vector2)transform.position;
            var baseY = basePos.y;
            var heroH = GetHeroHeight();

            var origin = aim != null ? aim.AimOriginWorld : (Vector2)transform.position;
            var targetPoint = GetTargetAimPoint(target);
            var forwardSign = (targetPoint.x - origin.x) >= 0f ? 1f : -1f;

            var legs = Mathf.Max(1, tunnelEscapeMaxLegs);
            var meters = 2f;
            var dirSign = forwardSign;

            for (var leg = 0; leg < legs; leg++)
            {
                if (_turn == null || _turn.ActivePlayer != transform)
                {
                    yield break;
                }

                if (_turn.SecondsLeft <= 8)
                {
                    walker.SetExternalMoveOverride(false, 0f);
                    yield break;
                }

                var targetX = basePos.x + dirSign * meters;
                var moveStartT = Time.time;
                var stallStartT = Time.time;
                var lastX = transform.position.x;

                while (true)
                {
                    if (_turn == null || _turn.ActivePlayer != transform)
                    {
                        walker.SetExternalMoveOverride(false, 0f);
                        yield break;
                    }

                    if (_turn.SecondsLeft <= 8)
                    {
                        walker.SetExternalMoveOverride(false, 0f);
                        yield break;
                    }

                    var x = transform.position.x;
                    var remaining = Mathf.Abs(targetX - x);
                    if (remaining <= 0.15f)
                    {
                        break;
                    }

                    // Timeout: don't spend too long on one leg.
                    if (Time.time - moveStartT >= 2.5f)
                    {
                        break;
                    }

                    // Stall detection: if X isn't changing, stop this leg.
                    if (Mathf.Abs(x - lastX) >= 0.03f)
                    {
                        lastX = x;
                        stallStartT = Time.time;
                    }
                    else if (Time.time - stallStartT >= 0.75f)
                    {
                        break;
                    }

                    walker.SetExternalMoveOverride(true, dirSign);
                    yield return new WaitForSeconds(0.05f);
                }

                walker.SetExternalMoveOverride(false, 0f);
                yield return new WaitForSeconds(0.05f);

                var movedDown = new FloatHolder();
                var downNoProgress5s = new BoolHolder();
                yield return RopeDownPullUpAndFallRoutine(target, grapple, aim, movedDown, downNoProgress5s);
                yield return new WaitForSeconds(0.15f);

                var afterPos = (Vector2)transform.position;
                var escapedDist = (afterPos - basePos).magnitude;
                if (escapedDist >= Mathf.Max(0.60f, heroH * 1.10f))
                {
                    yield break;
                }

                dirSign = -dirSign;
                meters += 2f;
            }

            walker.SetExternalMoveOverride(false, 0f);
        }

        private IEnumerator ApproachUntilInRange(Transform target, int myTeam, GrappleController grapple, WormAimController aim, HeroSurfaceWalker walker)
        {
            var startT = Time.time;
            var prevDist = float.PositiveInfinity;
            var noProgressSteps = 0;
            _consecutiveWalkSteps = 0;
            var lastStandPos = (Vector2)transform.position;
            var wellWindowSteps = 0;
            var wellWindowStartDist = float.PositiveInfinity;
            var progressCheckT = Time.time;
            var progressCheckDist = float.PositiveInfinity;
            var failedRopeDownAttempts = 0;
            while (Time.time - startT < Mathf.Max(0.25f, approachMaxSeconds))
            {
                if (_turn == null || _turn.ActivePlayer != transform)
                {
                    yield break;
                }

                // Keep using the available turn time instead of stopping after a couple of rope tries.
                if (_turn.SecondsLeft <= 8)
                {
                    if (walker != null) walker.SetExternalMoveOverride(false, 0f);
                    yield break;
                }

                if (target == null)
                {
                    yield break;
                }

                var origin = aim != null ? aim.AimOriginWorld : (Vector2)transform.position;
                var targetPoint = GetTargetAimPoint(target);
                var toTarget = targetPoint - origin;
                if (toTarget.sqrMagnitude < 0.0001f) toTarget = Vector2.right;
                var distBeforeWalk = toTarget.magnitude;

                if (wellWindowSteps <= 0)
                {
                    wellWindowStartDist = distBeforeWalk;
                }

                if (progressCheckDist > 999999f)
                {
                    progressCheckDist = distBeforeWalk;
                    progressCheckT = Time.time;
                }

                var canClaw = distBeforeWalk <= clawGunRange && HasLineOfSight(origin, target, myTeam);
                var heroH0 = GetHeroHeight();
                var grenadeExplosionRadius = Mathf.Max(0.25f, heroH0 * 2.5f);

                var grenade = GetComponent<HeroGrenadeThrower>();
                var effectiveGrenadeRange = grenade != null ? Mathf.Max(0.1f, grenade.GetMaxRangePublic()) : grenadeRange;
                var canGrenadeByDist = distBeforeWalk <= effectiveGrenadeRange && distBeforeWalk > grenadeExplosionRadius;
                var canGrenade = false;
                if (canGrenadeByDist && grenade != null)
                {
                    canGrenade = TryFindGrenadeAim(grenade, targetPoint, myTeam, out _);
                    if (!canGrenade)
                    {
                        LogAI($"Grenade in dist-range but no trajectory: dist={distBeforeWalk:0.0} effRange={effectiveGrenadeRange:0.0}");
                    }
                }

                if (canClaw || canGrenade)
                {
                    if (walker != null) walker.SetExternalMoveOverride(false, 0f);
                    yield break;
                }

                // After 5 consecutive "no progress" steps, use a fallback maneuver.
                if (noProgressSteps >= 5)
                {
                    var movedDown = new FloatHolder();
                    var downNoProgress5s = new BoolHolder();
                    yield return RopeDownPullUpAndFallRoutine(target, grapple, aim, movedDown, downNoProgress5s);
                    var heroH2 = GetHeroHeight();
                    if (movedDown.Value < Mathf.Max(0.10f, heroH2 * 0.60f)) failedRopeDownAttempts++;
                    else failedRopeDownAttempts = 0;
                    noProgressSteps = 0;
                    _consecutiveWalkSteps = 0;

                    if (failedRopeDownAttempts >= Mathf.Max(1, tunnelEscapeMinFailedRopeDownAttempts))
                    {
                        yield return TunnelEscapeRoutine(target, myTeam, grapple, aim, walker);
                        failedRopeDownAttempts = 0;
                    }
                }

                // 1) Walk toward enemy for 0.5s.
                var moveSign = toTarget.x >= 0f ? 1f : -1f;
                if (walker != null)
                {
                    walker.SetExternalMoveOverride(true, moveSign);
                }
                yield return new WaitForSeconds(Mathf.Max(0.05f, approachStepSeconds));
                if (walker != null)
                {
                    walker.SetExternalMoveOverride(false, 0f);
                }

                _consecutiveWalkSteps++;

                // 2) Re-check distance after 0.5s.
                origin = aim != null ? aim.AimOriginWorld : (Vector2)transform.position;
                targetPoint = GetTargetAimPoint(target);
                toTarget = targetPoint - origin;
                if (toTarget.sqrMagnitude < 0.0001f) toTarget = Vector2.right;
                var distAfterWalk = toTarget.magnitude;

                // Progress check based on post-walk distance, to avoid jitter before walking.
                // If we are not getting closer step-to-step, count as "no progress".
                var progressEps = 0.12f;
                if (distAfterWalk >= (prevDist - progressEps))
                {
                    noProgressSteps++;
                }
                else
                {
                    noProgressSteps = 0;
                    failedRopeDownAttempts = 0;
                }

                var standPos = (Vector2)transform.position;
                lastStandPos = standPos;

                // If distance did not decrease, try rope toward enemy.
                if (distAfterWalk >= (distBeforeWalk - progressEps))
                {
                    var movedDown = new FloatHolder();
                    var downNoProgress5s = new BoolHolder();
                    yield return RopeDownPullUpAndFallRoutine(target, grapple, aim, movedDown, downNoProgress5s);
                    var heroH2 = GetHeroHeight();
                    if (movedDown.Value < Mathf.Max(0.10f, heroH2 * 0.60f)) failedRopeDownAttempts++;
                    else failedRopeDownAttempts = 0;
                    _consecutiveWalkSteps = 0;

                    if (failedRopeDownAttempts >= Mathf.Max(1, tunnelEscapeMinFailedRopeDownAttempts))
                    {
                        yield return TunnelEscapeRoutine(target, myTeam, grapple, aim, walker);
                        failedRopeDownAttempts = 0;
                    }
                }

                // "Well" detection: evaluate net progress over 5 approach cycles.
                wellWindowSteps++;
                if (wellWindowSteps >= 5)
                {
                    var heroH = GetHeroHeight();
                    var minDelta = Mathf.Max(0.25f, heroH * 3f);
                    var netImprovement = wellWindowStartDist - distAfterWalk;
                    if (netImprovement < minDelta)
                    {
                        var movedDown = new FloatHolder();
                        var downNoProgress5s = new BoolHolder();
                        yield return RopeDownPullUpAndFallRoutine(target, grapple, aim, movedDown, downNoProgress5s);
                        _consecutiveWalkSteps = 0;
                    }

                    wellWindowSteps = 0;
                    wellWindowStartDist = distAfterWalk;
                }

                // 1-second progress monitoring: if distance stops decreasing, trigger under-feet rope escape.
                if (Time.time - progressCheckT >= 1.0f)
                {
                    if (distAfterWalk >= (progressCheckDist - progressEps))
                    {
                        var movedDown = new FloatHolder();
                        var downNoProgress5s = new BoolHolder();
                        yield return RopeDownPullUpAndFallRoutine(target, grapple, aim, movedDown, downNoProgress5s);
                        var heroH2 = GetHeroHeight();
                        if (movedDown.Value < Mathf.Max(0.10f, heroH2 * 0.60f)) failedRopeDownAttempts++;
                        else failedRopeDownAttempts = 0;
                        noProgressSteps = 0;
                        _consecutiveWalkSteps = 0;

                        if (failedRopeDownAttempts >= Mathf.Max(1, tunnelEscapeMinFailedRopeDownAttempts))
                        {
                            yield return TunnelEscapeRoutine(target, myTeam, grapple, aim, walker);
                            failedRopeDownAttempts = 0;
                        }
                    }

                    progressCheckDist = distAfterWalk;
                    progressCheckT = Time.time;
                }

                prevDist = distAfterWalk;
            }
        }

        private IEnumerator RopeDownPullUpAndFallRoutine(Transform target, GrappleController grapple, WormAimController aim, FloatHolder movedOut, BoolHolder noProgress5s)
        {
            if (grapple == null || target == null || movedOut == null || noProgress5s == null)
            {
                yield break;
            }

            var startPos = (Vector2)transform.position;

            var ropeDir = Vector2.down;
            if (aim != null) aim.SetExternalAimOverride(true, ropeDir);

            var walker = GetComponent<HeroSurfaceWalker>();
            if (walker != null) walker.SetIdleAnchorLock(true);

            if (!TryFireRopeValidated(grapple, aim, ropeDir, RopeFirePolicy.AllowDown))
            {
                if (walker != null) walker.SetIdleAnchorLock(false);
                movedOut.Value = ((Vector2)transform.position - startPos).magnitude;
                yield break;
            }
            yield return new WaitForSeconds(ropeAttachWaitSeconds);

            if (walker != null) walker.SetIdleAnchorLock(false);

            if (!grapple.IsAttached)
            {
                if (aim != null) aim.SetExternalAimOverride(false, Vector2.down);
                movedOut.Value = ((Vector2)transform.position - startPos).magnitude;
                yield break;
            }

            // -90 rule: extend rope down to the stop (max rope length) by holding "Down" continuously.
            // Must hold for at least ropeDownFullExtendSeconds AND (ideally) reach MaxRopeLength.
            var maxLen = grapple.MaxRopeLength;
            var extendStartT = Time.time;
            var reachedMax = false;
            noProgress5s.Value = false;

            grapple.SetExternalMoveOverride(true, moveH: 0f, moveV: -1f);
            while (grapple.IsAttached)
            {
                var curLen = grapple.CurrentRopeLength;
                reachedMax = maxLen > 0.0001f && curLen >= maxLen - 0.10f;
                var heldLongEnough = Time.time - extendStartT >= Mathf.Max(0.05f, ropeDownFullExtendSeconds);

                if (reachedMax && heldLongEnough)
                {
                    break;
                }

                // Safety cap: if we can't reach max length, don't hang forever.
                if (heldLongEnough && !reachedMax && Time.time - extendStartT >= Mathf.Max(0.05f, ropeDownFullExtendSeconds + 3.0f))
                {
                    noProgress5s.Value = true;
                    break;
                }

                yield return new WaitForSeconds(0.05f);
            }

            // Fall/swing toward enemy while still on rope.
            var origin = aim != null ? aim.AimOriginWorld : (Vector2)transform.position;
            var targetPoint = GetTargetAimPoint(target);
            var sign = (targetPoint.x - origin.x) >= 0f ? 1f : -1f;

            grapple.SetExternalMoveOverride(true, moveH: sign, moveV: 0f);
            yield return new WaitForSeconds(Mathf.Max(0.05f, ropeSwingSeconds));

            grapple.SetExternalMoveOverride(false, 0f, 0f);
            yield return SafeDetachRopeRoutine(grapple, anchorLikelyBelow: true);

            // If we are still attached, do not detach in midair. Wait until ground contact.
            if (grapple.IsAttached)
            {
                var walkerForDetach = GetComponent<HeroSurfaceWalker>();
                var waitStartT = Time.time;
                while (grapple.IsAttached)
                {
                    if (_turn == null || _turn.ActivePlayer != transform)
                    {
                        yield break;
                    }

                    if ((walkerForDetach != null && walkerForDetach.IsGroundedWithContact) || GetDistanceToGround() <= 0.15f)
                    {
                        grapple.DetachRope();
                        break;
                    }

                    // Keep descending while attached (don't just wait), so we actually reach ground.
                    var heroY = transform.position.y;
                    var anchorY = grapple.AnchorWorld.y;
                    var heroAboveAnchor = heroY > anchorY + 0.05f;
                    var v = heroAboveAnchor ? 1f : -1f;
                    grapple.SetExternalMoveOverride(true, moveH: 0f, moveV: v);

                    // Safety: don't wait forever; keep rope attached if we couldn't land.
                    if (Time.time - waitStartT >= 6.0f)
                    {
                        grapple.SetExternalMoveOverride(false, 0f, 0f);
                        break;
                    }

                    yield return new WaitForSeconds(0.12f);
                }

                grapple.SetExternalMoveOverride(false, 0f, 0f);
            }

            if (aim != null) aim.SetExternalAimOverride(false, Vector2.down);

            // Short ground/air-control burst toward enemy after detaching.
            if (walker != null) walker.SetExternalMoveOverride(true, sign);
            yield return new WaitForSeconds(0.35f);
            if (walker != null) walker.SetExternalMoveOverride(false, 0f);

            // Mandatory post-rope recheck: if we can hit now, shoot immediately.
            yield return new WaitForSeconds(Mathf.Max(0.0f, ropePostManeuverRecheckDelay));
            var shot = new BoolHolder();
            yield return TryShootIfPossibleNowRoutine(target, grapple, aim, walker, shot);
            if (shot.Value)
            {
                yield break;
            }

            movedOut.Value = ((Vector2)transform.position - startPos).magnitude;
        }

        private IEnumerator SafeDetachRopeRoutine(GrappleController grapple, bool anchorLikelyBelow)
        {
            if (grapple == null)
            {
                yield break;
            }

            var walker = GetComponent<HeroSurfaceWalker>();
            var startT = Time.time;
            var groundedForDetach = false;
            while (grapple.IsAttached && Time.time - startT < Mathf.Max(0.05f, ropeDetachMaxDescendSeconds))
            {
                // Detach only when we have actual contact with the ground (avoid midair detaches).
                if (walker != null && walker.IsGroundedWithContact)
                {
                    groundedForDetach = true;
                    break;
                }

                // Try to descend while staying attached.
                // Decide reel/extend based on actual relative position to the anchor.
                // If hero is ABOVE anchor -> reel-in (v=+1) pulls down.
                // If hero is BELOW anchor -> extend (v=-1) allows dropping down.
                var heroY = transform.position.y;
                var anchorY = grapple.AnchorWorld.y;
                var heroAboveAnchor = heroY > anchorY + 0.05f;
                var v = heroAboveAnchor ? 1f : -1f;
                grapple.SetExternalMoveOverride(true, moveH: 0f, moveV: v);
                yield return new WaitForSeconds(0.12f);
            }

            // If we are hanging over a chasm and cannot find ground by descending/adjusting,
            // shorten rope (reel-in) to return toward the anchor and try to "feel" ground.
            // This keeps us attached and avoids midair detaches.
            if (grapple.IsAttached && !groundedForDetach)
            {
                var reelStartT = Time.time;
                while (grapple.IsAttached && Time.time - reelStartT < 6.0f)
                {
                    if (walker != null && walker.IsGroundedWithContact)
                    {
                        groundedForDetach = true;
                        break;
                    }

                    var curLen = grapple.CurrentRopeLength;
                    var minLen = grapple.MinRopeLength;
                    if (minLen > 0.0001f && curLen <= minLen + 0.10f)
                    {
                        break;
                    }

                    grapple.SetExternalMoveOverride(true, moveH: 0f, moveV: 1f);
                    yield return new WaitForSeconds(0.12f);
                }
            }

            grapple.SetExternalMoveOverride(false, 0f, 0f);

            if (grapple.IsAttached && groundedForDetach)
            {
                grapple.DetachRope();
            }
        }

        private float GetDistanceToGround()
        {
            var origin = (Vector2)transform.position;
            var hit = Physics2D.Raycast(origin, Vector2.down, 50f, ~0);
            if (hit.collider == null || hit.collider.isTrigger)
            {
                return float.PositiveInfinity;
            }

            return hit.distance;
        }

        private IEnumerator RetreatRoutine(int myTeam, GrappleController grapple, WormAimController aim, HeroSurfaceWalker walker, float maxSeconds)
        {
            var safe = FindBestSafePoint(myTeam);
            if (!safe.HasValue)
            {
                yield break;
            }

            var startT = Time.time;
            while (Time.time - startT < Mathf.Max(0.05f, maxSeconds))
            {
                if (_turn == null || _turn.ActivePlayer != transform)
                {
                    yield break;
                }

                var origin = (Vector2)transform.position;
                var toSafe = safe.Value - origin;
                if (toSafe.magnitude < 1.25f)
                {
                    if (walker != null) walker.SetExternalMoveOverride(false, 0f);
                    yield break;
                }

                var moveSign = toSafe.x >= 0f ? 1f : -1f;
                if (walker != null)
                {
                    walker.SetExternalMoveOverride(true, moveSign);
                }
                yield return new WaitForSeconds(Mathf.Max(0.05f, retreatWalkBurstSeconds));
                if (walker != null)
                {
                    walker.SetExternalMoveOverride(false, 0f);
                }

                // If still far and rope available, do one rope attempt toward safe point.
                if (toSafe.magnitude > 10f)
                {
                    // Disabled: retreat rope shots frequently look like forward/back shots.
                    // Retreat should be walking-only; horizontal rope shots are reserved for the stuck -90 fallback.
                    yield return null;
                }
            }
        }

        private Vector2? FindBestSafePoint(int myTeam)
        {
            PlayerIdentity[] ids;
#if UNITY_6000_0_OR_NEWER
            ids = Object.FindObjectsByType<PlayerIdentity>(FindObjectsSortMode.None);
#else
            ids = Object.FindObjectsOfType<PlayerIdentity>();
#endif

            var enemies = new System.Collections.Generic.List<Transform>();
            for (var i = 0; i < ids.Length; i++)
            {
                var id = ids[i];
                if (id == null) continue;
                if (id.TeamIndex == myTeam) continue;
                if (id.GetComponent<SimpleHealth>() != null && id.GetComponent<SimpleHealth>().HP <= 0) continue;
                enemies.Add(id.transform);
            }

            if (enemies.Count == 0)
            {
                return null;
            }

            var origin = (Vector2)transform.position;
            var samples = difficulty == BotDifficulty.Easy ? 9 : (difficulty == BotDifficulty.Normal ? 13 : 17);
            var bestScore = float.NegativeInfinity;
            var best = origin;

            for (var i = 0; i < samples; i++)
            {
                var t = samples <= 1 ? 0.5f : (i / (float)(samples - 1));
                var x = Mathf.Lerp(-safePointSearchRadius, safePointSearchRadius, t);
                var p = origin + new Vector2(x, 8f);

                // Drop down to ground.
                var hit = Physics2D.Raycast(p, Vector2.down, 30f, ~0);
                if (hit.collider == null || hit.collider.isTrigger) continue;

                var groundP = hit.point + Vector2.up * 0.2f;
                var minDist = float.PositiveInfinity;
                for (var e = 0; e < enemies.Count; e++)
                {
                    var et = enemies[e];
                    if (et == null) continue;
                    minDist = Mathf.Min(minDist, Vector2.Distance(groundP, (Vector2)et.position));
                }

                // Prefer far from enemies, but don't go too far from current position (so we can reach).
                var reachPenalty = Vector2.Distance(groundP, origin) * 0.15f;
                var score = minDist - reachPenalty;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = groundP;
                }
            }

            return best;
        }

        private Vector2 FindBestRopeDir(Vector2 origin, Vector2 toTarget, bool stuck, int attempt)
        {
            var baseDir = stuck
                ? (Vector2.up * 1.0f + new Vector2(0.10f * Mathf.Sign(toTarget.x == 0f ? 1f : toTarget.x), 0f)).normalized
                : (toTarget.normalized + Vector2.up * 0.75f).normalized;

            var fanDeg = stuck ? 30f : 45f;
            var samples = difficulty == BotDifficulty.Easy ? 5 : (difficulty == BotDifficulty.Normal ? 9 : 13);
            var maxDist = 30f;
            var bestScore = float.NegativeInfinity;
            var best = baseDir;

            for (var i = 0; i < samples; i++)
            {
                var t = samples <= 1 ? 0.5f : (i / (float)(samples - 1));
                var ang = Mathf.Lerp(-fanDeg, fanDeg, t);
                ang += attempt * 12f;
                var dir = Rotate(baseDir, ang);
                // Force strongly upward-biased rope directions to prevent any "forward/back" looking shots.
                if (dir.y < 0.60f) dir.y = 0.60f;
                dir.Normalize();

                // Extra safety: avoid directions that still look too horizontal.
                if (Mathf.Abs(dir.y) < 0.65f)
                {
                    continue;
                }

                var hit = Physics2D.Raycast(origin, dir, maxDist, ~0);
                if (hit.collider == null || hit.collider.isTrigger) continue;

                var score = 0f;
                score += hit.point.y * 1.25f;
                score += Vector2.Dot((hit.point - origin).normalized, toTarget.normalized) * 8f;
                score -= hit.distance * 0.15f;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = dir;
                }
            }

            return best;
        }

        private bool TryFindGrenadeAim(HeroGrenadeThrower grenade, Vector2 targetPoint, int myTeam, out Vector2 aimDir)
        {
            aimDir = Vector2.right;
            if (grenade == null)
            {
                return false;
            }

            var origin = grenade.GetThrowOriginWorldPublic(out var heroH, out _);
            var toTarget = targetPoint - origin;
            var sign = toTarget.x >= 0f ? 1f : -1f;

            var angles = difficulty == BotDifficulty.Easy ? 9 : (difficulty == BotDifficulty.Normal ? 15 : 23);
            var minAng = 15f;
            var maxAng = 78f;

            // If the enemy is below us, allow a downward throw search as well.
            var allowDown = targetPoint.y < origin.y - heroH * 0.35f;
            var minDownAng = 10f;
            var maxDownAng = 75f;

            var best = float.PositiveInfinity;
            var bestDir = Vector2.right;
            void ConsiderAngle(float angDeg)
            {
                var rad = angDeg * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(rad) * sign, Mathf.Sin(rad));
                if (dir.sqrMagnitude < 0.0001f) return;
                dir.Normalize();

                if (grenade.PredictLandingPoint(dir, out var land))
                {
                    var d = Vector2.Distance(land, targetPoint);
                    if (d < best)
                    {
                        best = d;
                        bestDir = dir;
                    }
                }
            }

            for (var i = 0; i < angles; i++)
            {
                var t = angles <= 1 ? 0.5f : (i / (float)(angles - 1));
                var upAng = Mathf.Lerp(minAng, maxAng, t);
                ConsiderAngle(upAng);
                if (allowDown)
                {
                    var downAng = -Mathf.Lerp(minDownAng, maxDownAng, t);
                    ConsiderAngle(downAng);
                }
            }

            if (best <= Mathf.Max(0.5f, grenadeAcceptRadius))
            {
                aimDir = bestDir;
                return true;
            }

            return false;
        }

        private static Vector2 Rotate(Vector2 v, float degrees)
        {
            var r = degrees * Mathf.Deg2Rad;
            var c = Mathf.Cos(r);
            var s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        private Transform FindBestEnemyTarget(int myTeam)
        {
            PlayerIdentity[] ids;
#if UNITY_6000_0_OR_NEWER
            ids = Object.FindObjectsByType<PlayerIdentity>(FindObjectsSortMode.None);
#else
            ids = Object.FindObjectsOfType<PlayerIdentity>();
#endif
            Transform best = null;
            var bestDist = float.PositiveInfinity;

            var selfPos = transform.position;

            for (var i = 0; i < ids.Length; i++)
            {
                var id = ids[i];
                if (id == null) continue;
                if (id.TeamIndex == myTeam) continue;

                var h = id.GetComponent<SimpleHealth>();
                if (h != null && h.HP <= 0) continue;

                var d = (id.transform.position - selfPos).sqrMagnitude;
                if (best == null || d < bestDist)
                {
                    best = id.transform;
                    bestDist = d;
                }
            }

            if (best != null)
            {
                return best;
            }

            return null;
        }

        private static bool HasLineOfSight(Vector2 origin, Transform target, int myTeam)
        {
            if (target == null) return false;
            var dest = GetTargetAimPoint(target);
            var dir = dest - origin;
            if (dir.sqrMagnitude < 0.0001f) return true;
            var hit = Physics2D.Raycast(origin, dir.normalized, dir.magnitude, ~0);
            if (hit.collider == null) return false;
            var id = hit.collider.GetComponentInParent<PlayerIdentity>();
            return id != null && id.TeamIndex != myTeam;
        }

        private void LogAI(string msg)
        {
            if (!debugLogs)
            {
                return;
            }

            Debug.Log($"[SpiderBot] {name}: {msg}");
        }

        private float GetHeroHeight()
        {
            var col = GetComponentInChildren<Collider2D>();
            if (col != null)
            {
                return Mathf.Max(0.25f, col.bounds.size.y);
            }
            return 1f;
        }

        private void GetShootFlags(Vector2 origin, Transform target, int myTeam, float grenadeExplosionRadius, float clawRange, float grenadeRange, out bool canClaw, out bool canGrenade, out float dist)
        {
            var targetPoint = GetTargetAimPoint(target);
            dist = (targetPoint - origin).magnitude;

            var pointBlank = dist <= clawRange * Mathf.Clamp01(closeRangeOverrideFactor);
            canClaw = dist <= clawRange && (pointBlank || HasLineOfSight(origin, target, myTeam));
            canGrenade = dist <= grenadeRange && dist > grenadeExplosionRadius;
        }

        private bool TryFireRopeValidated(GrappleController grapple, WormAimController aim, Vector2 ropeDir, RopeFirePolicy policy)
        {
            if (grapple == null)
            {
                return false;
            }

            // IMPORTANT: GrappleController.FireRope toggles detach when already attached.
            // Bots must never call it while attached, otherwise we detach midair.
            if (grapple.IsAttached)
            {
                return false;
            }

            if (ropeDir.sqrMagnitude < 0.0001f)
            {
                ropeDir = Vector2.up;
            }

            ropeDir.Normalize();

            if (policy == RopeFirePolicy.AllowDown)
            {
                // Only allow a strict down shot for these routines.
                ropeDir = Vector2.down;
            }
            else
            {
                // Rope-only aim restrictions: forbid near-horizontal sectors.
                // Forbidden sectors (degrees, 0..360): 320-40 and 140-220.
                var ang = Mathf.Atan2(ropeDir.y, ropeDir.x) * Mathf.Rad2Deg;
                ang = (ang % 360f + 360f) % 360f;
                var inForbidden0 = ang >= 320f || ang <= 40f;
                var inForbidden180 = ang >= 140f && ang <= 220f;
                if (inForbidden0 || inForbidden180)
                {
                    LogAI($"Blocked rope shot: dir={ropeDir} angleDeg={ang:0.0}");
                    return false;
                }
            }

            if (aim != null)
            {
                aim.SetExternalAimOverride(true, ropeDir);
            }

            grapple.FireRopeExternal(ropeDir);
            return true;
        }

        private Vector2 ComputeAimDirectionWithDifficulty(Vector2 ideal)
        {
            var spreadDeg = difficulty switch
            {
                BotDifficulty.Easy => 16f,
                BotDifficulty.Normal => 8f,
                _ => 2.5f,
            };

            var noise = Random.Range(-spreadDeg, spreadDeg);
            var ang = Mathf.Atan2(ideal.y, ideal.x) * Mathf.Rad2Deg + noise;
            var rad = ang * Mathf.Deg2Rad;
            var d = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            if (d.sqrMagnitude < 0.0001f) d = Vector2.right;
            return d.normalized;
        }

        private static float GetClawHoldSecondsForDifficulty(BotDifficulty d)
        {
            return d switch
            {
                BotDifficulty.Easy => 3f,
                BotDifficulty.Normal => 5f,
                _ => 7f,
            };
        }

        private static bool TryFindClawAim(Transform target, Vector2 origin, Vector2 targetPoint, float range, float fireDownOffsetDeg, out Vector2 aimDir)
        {
            aimDir = Vector2.right;
            if (target == null)
            {
                return false;
            }

            range = Mathf.Max(0.1f, range);
            var toTarget = targetPoint - origin;
            if (toTarget.sqrMagnitude < 0.0001f)
            {
                toTarget = Vector2.right;
            }

            var baseDir = toTarget.normalized;
            var baseAng = Mathf.Atan2(baseDir.y, baseDir.x) * Mathf.Rad2Deg;

            Vector2 ApplyClawFireDownOffset(Vector2 d)
            {
                var v = d;
                if (v.sqrMagnitude < 0.0001f) v = Vector2.right;
                v.Normalize();

                var ang = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
                var signedOffset = v.x >= 0f ? -fireDownOffsetDeg : fireDownOffsetDeg;
                ang += signedOffset;
                var rad = ang * Mathf.Deg2Rad;
                var outDir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
                if (outDir.sqrMagnitude < 0.0001f) outDir = Vector2.right;
                return outDir.normalized;
            }

            bool HitsTarget(Vector2 d)
            {
                var fireDir = ApplyClawFireDownOffset(d);
                var hit = Physics2D.Raycast(origin, fireDir, range, ~0);
                if (hit.collider == null || hit.collider.isTrigger)
                {
                    return false;
                }
                var ht = hit.collider.transform;
                return ht == target || ht.IsChildOf(target);
            }

            if (HitsTarget(baseDir))
            {
                aimDir = baseDir;
                return true;
            }

            // Scan a cone around the target direction to find a ray that hits the target.
            var steps = 28;
            var maxDeg = 70f;
            var stepDeg = maxDeg / Mathf.Max(1, steps);
            for (var i = 1; i <= steps; i++)
            {
                var a0 = baseAng + stepDeg * i;
                var a1 = baseAng - stepDeg * i;
                var r0 = a0 * Mathf.Deg2Rad;
                var r1 = a1 * Mathf.Deg2Rad;

                var d0 = new Vector2(Mathf.Cos(r0), Mathf.Sin(r0));
                if (HitsTarget(d0))
                {
                    aimDir = d0.normalized;
                    return true;
                }

                var d1 = new Vector2(Mathf.Cos(r1), Mathf.Sin(r1));
                if (HitsTarget(d1))
                {
                    aimDir = d1.normalized;
                    return true;
                }
            }

            return false;
        }

        private static Vector2 GetTargetAimPoint(Transform target)
        {
            if (target == null)
            {
                return Vector2.zero;
            }
            var col = target.GetComponentInChildren<Collider2D>();
            if (col != null)
            {
                return col.bounds.center;
            }
            return target.position;
        }
    }
}
