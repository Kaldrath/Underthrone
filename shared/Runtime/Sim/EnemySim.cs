using System.Numerics;

namespace Underthrone.Shared.Sim
{
    public enum EnemyAction : byte
    {
        Idle,
        Chase,
        Windup,     // the telegraph: readable anticipation frames on the sprite
        Active,
        Recovery,   // the punish window
        Staggered,
        Dead,
    }

    public struct EnemyState
    {
        public Vector3 Position;
        public float Yaw;
        public int Health;
        public EnemyAction Action;
        public int ActionTicks;

        public static EnemyState SpawnGhoul(Vector3 position)
        {
            return new EnemyState
            {
                Position = position,
                Health = CombatTuning.GHOUL_MAX_HEALTH,
                Action = EnemyAction.Idle,
            };
        }

        public bool IsDead => Action == EnemyAction.Dead;
    }

    /// Barrow Ghoul behaviour: chase, telegraph, strike, recover. Server only, but kept in the
    /// shared library so it is unit-tested alongside the player rules.
    public static class EnemySim
    {
        /// Steps one tick. Returns true on the tick the strike lands (first Active tick); the
        /// server resolves the hit against the target exactly once per attack.
        public static bool Step(ref EnemyState e, bool hasTarget, Vector3 targetPosition)
        {
            if (e.IsDead)
            {
                return false;
            }

            switch (e.Action)
            {
                case EnemyAction.Windup:
                    // Tracks the target through the first half of the telegraph, then commits.
                    if (hasTarget && e.ActionTicks > CombatTuning.GHOUL_WINDUP_TICKS / 2)
                    {
                        e.Yaw = SimMath.YawTowards(e.Position, targetPosition);
                    }
                    if (--e.ActionTicks > 0)
                    {
                        return false;
                    }
                    e.Action = EnemyAction.Active;
                    e.ActionTicks = CombatTuning.GHOUL_ACTIVE_TICKS;
                    return true;
                case EnemyAction.Active:
                    if (--e.ActionTicks <= 0)
                    {
                        e.Action = EnemyAction.Recovery;
                        e.ActionTicks = CombatTuning.GHOUL_RECOVERY_TICKS;
                    }
                    return false;
                case EnemyAction.Recovery:
                case EnemyAction.Staggered:
                    if (--e.ActionTicks <= 0)
                    {
                        e.Action = hasTarget ? EnemyAction.Chase : EnemyAction.Idle;
                        e.ActionTicks = 0;
                    }
                    return false;
            }

            // Idle / Chase
            if (!hasTarget)
            {
                e.Action = EnemyAction.Idle;
                return false;
            }
            e.Yaw = SimMath.YawTowards(e.Position, targetPosition);
            if (SimMath.FlatDistance(e.Position, targetPosition) <= CombatTuning.GHOUL_ATTACK_RANGE * 0.85f)
            {
                e.Action = EnemyAction.Windup;
                e.ActionTicks = CombatTuning.GHOUL_WINDUP_TICKS;
                return false;
            }
            e.Action = EnemyAction.Chase;
            e.Position = SimMath.ClampToArena(
                e.Position + SimMath.Forward(e.Yaw) * (CombatTuning.GHOUL_SPEED * SimConstants.TICK_DELTA),
                SimConstants.ARENA_RADIUS);
            return false;
        }

        public static void ApplyDamage(ref EnemyState e, int damage)
        {
            if (e.IsDead)
            {
                return;
            }
            e.Health -= damage;
            if (e.Health <= 0)
            {
                e.Health = 0;
                e.Action = EnemyAction.Dead;
                e.ActionTicks = 0;
                return;
            }
            // Light flinch only interrupts movement/recovery; a committed telegraph keeps going
            // (poise), so players must dodge or parry rather than trade.
            if (e.Action == EnemyAction.Chase || e.Action == EnemyAction.Idle || e.Action == EnemyAction.Recovery)
            {
                e.Action = EnemyAction.Staggered;
                e.ActionTicks = CombatTuning.GHOUL_HIT_STAGGER_TICKS;
            }
        }

        public static void ApplyParried(ref EnemyState e)
        {
            if (e.IsDead)
            {
                return;
            }
            e.Action = EnemyAction.Staggered;
            e.ActionTicks = CombatTuning.GHOUL_PARRIED_STAGGER_TICKS;
        }
    }
}
