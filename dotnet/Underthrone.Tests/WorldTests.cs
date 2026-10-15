using System;
using System.Linq;
using System.Numerics;
using Underthrone.Server;
using Underthrone.Shared.Sim;
using Xunit;

namespace Underthrone.Tests
{
    public class WorldTests
    {
        private static uint _inputTick;

        private static void Feed(ServerPlayer player, InputButtons buttons = InputButtons.None, float yaw = 0f, float moveY = 0f)
        {
            player.Enqueue(PlayerInput.Create(++_inputTick, 0f, moveY, yaw, buttons));
        }

        private static (World, ServerPlayer, ServerEnemy) Duel(float ghoulDistance)
        {
            _inputTick = 0;
            var world = new World();
            ServerPlayer player = world.AddPlayer("Tester");
            Vector3 p = player.State.Position;
            ServerEnemy ghoul = world.SpawnGhoul(new Vector3(p.X, 0f, p.Z + ghoulDistance)); // straight ahead (yaw 0)
            return (world, player, ghoul);
        }

        [Fact]
        public void IdlePlayer_IsKilledByAGhoul_ThenRespawns()
        {
            var (world, player, _) = Duel(1.5f);
            bool died = false;
            for (int tick = 0; tick < SimConstants.TICK_RATE * 30 && !died; tick++)
            {
                Feed(player);
                world.Step();
                died = player.State.IsDead;
            }
            Assert.True(died);
            int deadTicks = 0;
            while (player.State.IsDead && deadTicks < SimConstants.TICK_RATE * 10)
            {
                Feed(player);
                world.Step();
                deadTicks++;
            }
            Assert.False(player.State.IsDead);
            Assert.Equal(SimConstants.TICK_RATE * 5, deadTicks);
            Assert.Equal(CombatTuning.PLAYER_MAX_HEALTH, player.State.Health);
        }

        [Fact]
        public void RaisingGuardDuringTheTelegraph_ParriesAndStaggersTheGhoul()
        {
            var (world, player, ghoul) = Duel(1.5f);
            for (int tick = 0; tick < 200; tick++)
            {
                bool lateWindup = ghoul.State.Action == EnemyAction.Windup && ghoul.State.ActionTicks <= 3;
                Feed(player, lateWindup ? InputButtons.Block : InputButtons.None);
                world.Step();
                if (world.Events.Any(e => e.TargetId == player.Id))
                {
                    break;
                }
            }
            CombatEventData strike = world.Events.Single(e => e.TargetId == player.Id);
            Assert.Equal(HitOutcome.Parried, strike.Outcome);
            Assert.Equal(EnemyAction.Staggered, ghoul.State.Action);
            Assert.Equal(CombatTuning.PLAYER_MAX_HEALTH, player.State.Health);
        }

        [Fact]
        public void DodgingThroughTheStrike_TakesNoDamage()
        {
            var (world, player, ghoul) = Duel(1.5f);
            for (int tick = 0; tick < 200; tick++)
            {
                bool lateWindup = ghoul.State.Action == EnemyAction.Windup && ghoul.State.ActionTicks == 2;
                Feed(player, lateWindup ? InputButtons.Dodge : InputButtons.None);
                world.Step();
                if (world.Events.Any(e => e.TargetId == player.Id))
                {
                    break;
                }
            }
            CombatEventData strike = world.Events.Single(e => e.TargetId == player.Id);
            Assert.Equal(HitOutcome.Dodged, strike.Outcome);
            Assert.Equal(CombatTuning.PLAYER_MAX_HEALTH, player.State.Health);
        }

        [Fact]
        public void OneSwing_HitsTheGhoulOnce()
        {
            var (world, player, ghoul) = Duel(1.8f);
            Feed(player, InputButtons.Attack);
            int hits = 0;
            for (int tick = 0; tick < 30; tick++)
            {
                if (tick > 0)
                {
                    Feed(player, InputButtons.Attack); // held
                }
                world.Step();
                hits += world.Events.Count(e => e.AttackerId == player.Id && e.TargetId == ghoul.Id);
            }
            Assert.Equal(1, hits);
            Assert.Equal(CombatTuning.GHOUL_MAX_HEALTH - CombatTuning.LIGHT_DAMAGE, ghoul.State.Health);
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(6, true)]
        public void LagCompensation_HitsWhereTheAttackerSawTheTarget(int latencyTicks, bool expectHit)
        {
            var (world, player, ghoul) = Duel(1.8f);
            player.LatencyTicks = latencyTicks;
            Feed(player, InputButtons.Attack);
            world.Step();
            // Let the windup run until 3 ticks before the swing goes active...
            for (int tick = 0; tick < CombatTuning.LIGHT_WINDUP_TICKS - 4; tick++)
            {
                Feed(player, InputButtons.Attack);
                world.Step();
            }
            // ...then the ghoul is suddenly far away on the server (the attacker hasn't seen it yet).
            ghoul.State.Position = new Vector3(ghoul.State.Position.X + 6f, 0f, ghoul.State.Position.Z);
            ghoul.State.Action = EnemyAction.Staggered;
            ghoul.State.ActionTicks = 60;
            bool hit = false;
            for (int tick = 0; tick < 8; tick++)
            {
                Feed(player, InputButtons.Attack);
                world.Step();
                hit |= world.Events.Any(e => e.AttackerId == player.Id && e.TargetId == ghoul.Id);
            }
            Assert.Equal(expectHit, hit);
        }

        [Fact]
        public void RepeatedAndStaleInputs_AreIgnored()
        {
            var world = new World();
            ServerPlayer player = world.AddPlayer("Tester");
            player.Enqueue(PlayerInput.Create(2, 0, 1, 0, InputButtons.None));
            player.Enqueue(PlayerInput.Create(1, 0, 1, 0, InputButtons.None));
            player.Enqueue(PlayerInput.Create(2, 0, 1, 0, InputButtons.None));
            world.Step();
            Assert.Equal(1u, player.LastProcessedInputTick);
            world.Step();
            Assert.Equal(2u, player.LastProcessedInputTick);
            player.Enqueue(PlayerInput.Create(1, 0, 1, 0, InputButtons.None));
            world.Step(); // nothing new: last input repeats, ack stays
            Assert.Equal(2u, player.LastProcessedInputTick);
        }
    }
}
