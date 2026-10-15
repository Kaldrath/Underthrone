using System;
using System.Numerics;

namespace Underthrone.Shared.Sim
{
    public enum HitOutcome : byte
    {
        Missed,
        Hit,
        Blocked,
        GuardBroken,
        Parried,
        Dodged,
    }

    /// What a defender was doing on one tick; the server keeps a short history per player.
    public readonly struct DefenseSample
    {
        public readonly bool Invulnerable;
        public readonly bool Parrying;
        public readonly bool Blocking;

        public DefenseSample(bool invulnerable, bool parrying, bool blocking)
        {
            Invulnerable = invulnerable;
            Parrying = parrying;
            Blocking = blocking;
        }

        public static DefenseSample From(in PlayerState s) => new DefenseSample(s.IsInvulnerable, s.IsParrying, s.IsBlocking);
    }

    public static class CombatResolver
    {
        /// "Favour the defender": a strike that lands at server tick T is checked against the
        /// defender's state over the last <paramref name="toleranceTicks"/> ticks as well, so a
        /// dodge or parry the player pressed in time on their screen still counts despite ping.
        /// <paramref name="historyNewestFirst"/>[0] is the defender's state at T.
        public static HitOutcome ResolveStrikeOnPlayer(
            ReadOnlySpan<DefenseSample> historyNewestFirst, int toleranceTicks, bool attackerInFront)
        {
            if (historyNewestFirst.Length == 0)
            {
                return HitOutcome.Hit;
            }
            int window = Math.Min(historyNewestFirst.Length, Math.Clamp(toleranceTicks, 0, SimConstants.MAX_LAG_COMPENSATION_TICKS) + 1);
            for (int sampleIndex = 0; sampleIndex < window; sampleIndex++)
            {
                if (historyNewestFirst[sampleIndex].Invulnerable)
                {
                    return HitOutcome.Dodged;
                }
            }
            if (attackerInFront)
            {
                for (int sampleIndex = 0; sampleIndex < window; sampleIndex++)
                {
                    if (historyNewestFirst[sampleIndex].Parrying)
                    {
                        return HitOutcome.Parried;
                    }
                }
                if (historyNewestFirst[0].Blocking)
                {
                    return HitOutcome.Blocked;
                }
            }
            return HitOutcome.Hit;
        }

        /// The guard only covers the front half-circle.
        public static bool IsInFront(in PlayerState defender, Vector3 attackerPosition)
        {
            return SimMath.InArc(defender.Position, defender.Yaw, attackerPosition, float.MaxValue, 90f);
        }

        /// Ghoul strike geometry against a player position.
        public static bool GhoulStrikeReaches(in EnemyState ghoul, Vector3 playerPosition)
        {
            return SimMath.InArc(ghoul.Position, ghoul.Yaw, playerPosition,
                CombatTuning.GHOUL_ATTACK_RANGE, CombatTuning.GHOUL_HALF_ARC_DEGREES);
        }

        /// Player light-attack geometry against a (possibly rewound) target position.
        public static bool LightAttackReaches(in PlayerState attacker, Vector3 targetPosition)
        {
            return SimMath.InArc(attacker.Position, attacker.Yaw, targetPosition,
                CombatTuning.LIGHT_RANGE, CombatTuning.LIGHT_HALF_ARC_DEGREES);
        }
    }
}
