using System.Numerics;

namespace Underthrone.Shared.Sim
{
    /// Deterministic per-tick player step. The server runs it authoritatively; the client runs
    /// the same code to predict its own body so movement, swings and dodges respond instantly.
    public static class PlayerSim
    {
        public static void Step(ref PlayerState s, in PlayerInput input)
        {
            if (s.IsDead)
            {
                s.PrevButtons = input.Buttons;
                return;
            }

            bool pressedAttack = Pressed(s.PrevButtons, input.Buttons, InputButtons.Attack);
            bool pressedDodge = Pressed(s.PrevButtons, input.Buttons, InputButtons.Dodge);
            bool holdBlock = input.Held(InputButtons.Block);

            s.Yaw = input.Yaw;
            if (s.IFrameTicks > 0)
            {
                s.IFrameTicks--;
            }
            if (s.ParryTicks > 0)
            {
                s.ParryTicks--;
            }

            AdvanceTimedAction(ref s);

            bool free = s.Action == PlayerAction.Idle || s.Action == PlayerAction.Blocking;
            if (free && pressedDodge && s.Stamina >= CombatTuning.DODGE_STAMINA_COST)
            {
                StartDodge(ref s, input);
            }
            else if (free && pressedAttack && s.Stamina >= CombatTuning.LIGHT_STAMINA_COST)
            {
                SpendStamina(ref s, CombatTuning.LIGHT_STAMINA_COST);
                s.Action = PlayerAction.AttackWindup;
                s.ActionTicks = CombatTuning.LIGHT_WINDUP_TICKS;
                s.SwingId++;
            }
            else if (free)
            {
                if (holdBlock && s.Action != PlayerAction.Blocking)
                {
                    s.Action = PlayerAction.Blocking;
                    s.ParryTicks = CombatTuning.PARRY_WINDOW_TICKS;
                }
                else if (!holdBlock && s.Action == PlayerAction.Blocking)
                {
                    s.Action = PlayerAction.Idle;
                    s.ParryTicks = 0;
                }
            }

            Move(ref s, input);
            RegenStamina(ref s);
            s.PrevButtons = input.Buttons;
        }

        /// A hit that got through: damage, stagger, maybe death.
        public static void ApplyHit(ref PlayerState s, int damage)
        {
            if (s.IsDead)
            {
                return;
            }
            s.Health -= damage;
            if (s.Health <= 0)
            {
                s.Health = 0;
                s.Action = PlayerAction.Dead;
                s.ActionTicks = 0;
                return;
            }
            s.Action = PlayerAction.Staggered;
            s.ActionTicks = CombatTuning.HIT_STAGGER_TICKS;
            s.ParryTicks = 0;
        }

        /// A hit absorbed by the guard. Returns true if it broke the guard.
        public static bool ApplyBlockedHit(ref PlayerState s, int damage)
        {
            SpendStamina(ref s, damage * CombatTuning.BLOCK_STAMINA_PER_DAMAGE);
            if (s.Stamina > 0f)
            {
                return false;
            }
            s.Stamina = 0f;
            s.Action = PlayerAction.Staggered;
            s.ActionTicks = CombatTuning.GUARD_BREAK_STAGGER_TICKS;
            s.ParryTicks = 0;
            return true;
        }

        private static bool Pressed(InputButtons prev, InputButtons now, InputButtons button)
        {
            return (now & button) != 0 && (prev & button) == 0;
        }

        private static void AdvanceTimedAction(ref PlayerState s)
        {
            switch (s.Action)
            {
                case PlayerAction.AttackWindup:
                case PlayerAction.AttackActive:
                case PlayerAction.AttackRecovery:
                case PlayerAction.Dodging:
                case PlayerAction.Staggered:
                    s.ActionTicks--;
                    if (s.ActionTicks > 0)
                    {
                        return;
                    }
                    break;
                default:
                    return;
            }

            switch (s.Action)
            {
                case PlayerAction.AttackWindup:
                    s.Action = PlayerAction.AttackActive;
                    s.ActionTicks = CombatTuning.LIGHT_ACTIVE_TICKS;
                    break;
                case PlayerAction.AttackActive:
                    s.Action = PlayerAction.AttackRecovery;
                    s.ActionTicks = CombatTuning.LIGHT_RECOVERY_TICKS;
                    break;
                default:
                    s.Action = PlayerAction.Idle;
                    s.ActionTicks = 0;
                    break;
            }
        }

        private static void StartDodge(ref PlayerState s, in PlayerInput input)
        {
            SpendStamina(ref s, CombatTuning.DODGE_STAMINA_COST);
            Vector3 wish = WishDirection(s.Yaw, input);
            s.DodgeDirection = wish.LengthSquared() > 0.01f ? Vector3.Normalize(wish) : -SimMath.Forward(s.Yaw);
            s.Action = PlayerAction.Dodging;
            s.ActionTicks = CombatTuning.DODGE_TICKS;
            s.IFrameTicks = CombatTuning.DODGE_IFRAME_TICKS;
            s.ParryTicks = 0;
        }

        private static void Move(ref PlayerState s, in PlayerInput input)
        {
            Vector3 velocity;
            switch (s.Action)
            {
                case PlayerAction.Dodging:
                    velocity = s.DodgeDirection * CombatTuning.DODGE_SPEED;
                    break;
                case PlayerAction.Staggered:
                    velocity = Vector3.Zero;
                    break;
                default:
                    float speed = s.Action == PlayerAction.Idle ? CombatTuning.WALK_SPEED
                        : s.Action == PlayerAction.Blocking ? CombatTuning.BLOCK_WALK_SPEED
                        : CombatTuning.ATTACK_WALK_SPEED;
                    Vector3 wish = WishDirection(s.Yaw, input);
                    if (wish.LengthSquared() > 1f)
                    {
                        wish = Vector3.Normalize(wish);
                    }
                    velocity = wish * speed;
                    break;
            }
            s.Position = SimMath.ClampToArena(s.Position + velocity * SimConstants.TICK_DELTA, SimConstants.ARENA_RADIUS);
        }

        private static Vector3 WishDirection(float yaw, in PlayerInput input)
        {
            return SimMath.Forward(yaw) * input.MoveYf + SimMath.Right(yaw) * input.MoveXf;
        }

        private static void SpendStamina(ref PlayerState s, float amount)
        {
            s.Stamina -= amount;
            s.RegenDelayTicks = CombatTuning.STAMINA_REGEN_DELAY_TICKS;
        }

        private static void RegenStamina(ref PlayerState s)
        {
            if (s.RegenDelayTicks > 0)
            {
                s.RegenDelayTicks--;
                return;
            }
            bool busy = s.Action == PlayerAction.Dodging
                || s.Action == PlayerAction.AttackWindup
                || s.Action == PlayerAction.AttackActive
                || s.Action == PlayerAction.AttackRecovery;
            if (busy || s.Stamina >= CombatTuning.PLAYER_MAX_STAMINA)
            {
                return;
            }
            float rate = CombatTuning.STAMINA_REGEN_PER_SECOND
                * (s.Action == PlayerAction.Blocking ? CombatTuning.STAMINA_REGEN_BLOCKING_SCALE : 1f);
            s.Stamina += rate * SimConstants.TICK_DELTA;
            if (s.Stamina > CombatTuning.PLAYER_MAX_STAMINA)
            {
                s.Stamina = CombatTuning.PLAYER_MAX_STAMINA;
            }
        }
    }
}
