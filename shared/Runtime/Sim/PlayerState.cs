using System.Numerics;

namespace Underthrone.Shared.Sim
{
    public enum PlayerAction : byte
    {
        Idle,
        Blocking,
        AttackWindup,
        AttackActive,
        AttackRecovery,
        Dodging,
        Staggered,
        Dead,
    }

    /// Authoritative player body state. Plain data so the client can copy it from a snapshot
    /// and replay its unacknowledged inputs on top (prediction + reconciliation).
    /// Yaw 0 faces +Z; positive yaw turns toward +X (Unity's convention).
    public struct PlayerState
    {
        public Vector3 Position;
        public float Yaw;
        public int Health;
        public float Stamina;
        public PlayerAction Action;
        public int ActionTicks;        // ticks left in a timed action
        public int IFrameTicks;        // > 0 while a dodge makes the player untouchable
        public int ParryTicks;         // > 0 during the parry window after raising the guard
        public int RegenDelayTicks;    // stamina regen pause after spending
        public Vector3 DodgeDirection;
        public ushort SwingId;         // increments per attack; lets the server hit each target once per swing
        public InputButtons PrevButtons;

        public static PlayerState Spawn(Vector3 position, float yaw)
        {
            return new PlayerState
            {
                Position = position,
                Yaw = yaw,
                Health = CombatTuning.PLAYER_MAX_HEALTH,
                Stamina = CombatTuning.PLAYER_MAX_STAMINA,
                Action = PlayerAction.Idle,
            };
        }

        public bool IsDead => Action == PlayerAction.Dead;
        public bool IsInvulnerable => IFrameTicks > 0;
        public bool IsBlocking => Action == PlayerAction.Blocking;
        public bool IsParrying => Action == PlayerAction.Blocking && ParryTicks > 0;
        public bool IsAttackActive => Action == PlayerAction.AttackActive;

        public Vector3 Forward => SimMath.Forward(Yaw);
    }
}
