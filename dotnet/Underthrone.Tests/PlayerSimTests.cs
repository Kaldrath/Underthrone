using System;
using System.Numerics;
using Underthrone.Shared.Sim;
using Xunit;

namespace Underthrone.Tests
{
    public class PlayerSimTests
    {
        private static PlayerInput In(uint tick, float moveX = 0, float moveY = 0, InputButtons buttons = InputButtons.None, float yaw = 0)
            => PlayerInput.Create(tick, moveX, moveY, yaw, buttons);

        [Fact]
        public void Quantize_RoundTripsAxesAndYaw()
        {
            PlayerInput input = PlayerInput.Create(1, 1f, -0.5f, MathF.PI / 2f, InputButtons.None);
            Assert.Equal(1f, input.MoveXf, 3);
            Assert.Equal(-0.5f, input.MoveYf, 2);
            Assert.Equal(MathF.PI / 2f, input.Yaw, 3);
            Assert.Equal((ushort)0, PlayerInput.QuantizeYaw(MathF.PI * 2f));
        }

        [Fact]
        public void WalkingForwardForOneSecond_CoversWalkSpeed()
        {
            PlayerState s = PlayerState.Spawn(Vector3.Zero, 0f);
            for (uint tick = 1; tick <= SimConstants.TICK_RATE; tick++)
            {
                PlayerSim.Step(ref s, In(tick, moveY: 1));
            }
            Assert.Equal(CombatTuning.WALK_SPEED, s.Position.Z, 2);
            Assert.Equal(0f, s.Position.X, 3);
        }

        [Fact]
        public void LightAttack_RunsWindupActiveRecovery_AndCostsStamina()
        {
            PlayerState s = PlayerState.Spawn(Vector3.Zero, 0f);
            uint tick = 1;
            PlayerSim.Step(ref s, In(tick++, buttons: InputButtons.Attack));
            Assert.Equal(PlayerAction.AttackWindup, s.Action);
            Assert.Equal(CombatTuning.PLAYER_MAX_STAMINA - CombatTuning.LIGHT_STAMINA_COST, s.Stamina);
            Assert.Equal(1, s.SwingId);

            int activeTicks = 0;
            int total = CombatTuning.LIGHT_WINDUP_TICKS + CombatTuning.LIGHT_ACTIVE_TICKS + CombatTuning.LIGHT_RECOVERY_TICKS;
            for (int step = 1; step < total; step++)
            {
                PlayerSim.Step(ref s, In(tick++, buttons: InputButtons.Attack)); // held, never re-pressed
                if (s.IsAttackActive)
                {
                    activeTicks++;
                }
            }
            Assert.Equal(CombatTuning.LIGHT_ACTIVE_TICKS, activeTicks);
            Assert.Equal(PlayerAction.AttackRecovery, s.Action); // last of the 22 committed ticks
            PlayerSim.Step(ref s, In(tick++, buttons: InputButtons.Attack));
            Assert.Equal(PlayerAction.Idle, s.Action);
            Assert.Equal(1, s.SwingId); // holding the button does not chain swings
        }

        [Fact]
        public void Attack_IsRefusedWithoutStamina()
        {
            PlayerState s = PlayerState.Spawn(Vector3.Zero, 0f);
            s.Stamina = CombatTuning.LIGHT_STAMINA_COST - 1f;
            PlayerSim.Step(ref s, In(1, buttons: InputButtons.Attack));
            Assert.Equal(PlayerAction.Idle, s.Action);
        }

        [Fact]
        public void Dodge_GivesIFramesThenEnds_AndBackstepsWithoutInput()
        {
            PlayerState s = PlayerState.Spawn(Vector3.Zero, 0f);
            uint tick = 1;
            PlayerSim.Step(ref s, In(tick++, buttons: InputButtons.Dodge));
            Assert.Equal(PlayerAction.Dodging, s.Action);
            Assert.True(s.IsInvulnerable);

            int invulnerableTicks = 1;
            for (int step = 1; step < CombatTuning.DODGE_TICKS; step++)
            {
                PlayerSim.Step(ref s, In(tick++));
                if (s.IsInvulnerable)
                {
                    invulnerableTicks++;
                }
            }
            Assert.Equal(CombatTuning.DODGE_IFRAME_TICKS, invulnerableTicks);
            Assert.Equal(PlayerAction.Dodging, s.Action); // last of the 12 dodge ticks
            PlayerSim.Step(ref s, In(tick++));
            Assert.Equal(PlayerAction.Idle, s.Action);
            Assert.True(s.Position.Z < -2f, $"backstep should move backwards, z={s.Position.Z}");
        }

        [Fact]
        public void RaisingGuard_OpensParryWindowThenPlainBlock()
        {
            PlayerState s = PlayerState.Spawn(Vector3.Zero, 0f);
            uint tick = 1;
            PlayerSim.Step(ref s, In(tick++, buttons: InputButtons.Block));
            Assert.True(s.IsParrying);
            for (int step = 0; step < CombatTuning.PARRY_WINDOW_TICKS; step++)
            {
                PlayerSim.Step(ref s, In(tick++, buttons: InputButtons.Block));
            }
            Assert.True(s.IsBlocking);
            Assert.False(s.IsParrying);
            PlayerSim.Step(ref s, In(tick++));
            Assert.Equal(PlayerAction.Idle, s.Action);
        }

        [Fact]
        public void Stamina_WaitsForRegenDelayThenRecovers()
        {
            PlayerState s = PlayerState.Spawn(Vector3.Zero, 0f);
            uint tick = 1;
            PlayerSim.Step(ref s, In(tick++, buttons: InputButtons.Dodge));
            float afterDodge = s.Stamina;
            while (s.Action == PlayerAction.Dodging)
            {
                PlayerSim.Step(ref s, In(tick++));
            }
            Assert.Equal(afterDodge, s.Stamina);
            for (int step = 0; step < SimConstants.TICK_RATE * 2; step++)
            {
                PlayerSim.Step(ref s, In(tick++));
            }
            Assert.Equal(CombatTuning.PLAYER_MAX_STAMINA, s.Stamina);
        }

        [Fact]
        public void BlockedHitsDrainStamina_UntilTheGuardBreaks()
        {
            PlayerState s = PlayerState.Spawn(Vector3.Zero, 0f);
            PlayerSim.Step(ref s, In(1, buttons: InputButtons.Block));
            bool broke = false;
            for (int hit = 0; hit < 10 && !broke; hit++)
            {
                broke = PlayerSim.ApplyBlockedHit(ref s, CombatTuning.GHOUL_DAMAGE);
            }
            Assert.True(broke);
            Assert.Equal(PlayerAction.Staggered, s.Action);
            Assert.Equal(CombatTuning.PLAYER_MAX_HEALTH, s.Health);
        }

        [Fact]
        public void PlayerStaysInsideArena()
        {
            PlayerState s = PlayerState.Spawn(new Vector3(0, 0, SimConstants.ARENA_RADIUS - 0.1f), 0f);
            for (uint tick = 1; tick <= 60; tick++)
            {
                PlayerSim.Step(ref s, In(tick, moveY: 1));
            }
            Assert.True(s.Position.Length() <= SimConstants.ARENA_RADIUS + 1e-3f);
        }
    }
}
