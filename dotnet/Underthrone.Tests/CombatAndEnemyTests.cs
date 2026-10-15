using System;
using System.Numerics;
using Underthrone.Shared.Sim;
using Xunit;

namespace Underthrone.Tests
{
    public class CombatAndEnemyTests
    {
        private static readonly DefenseSample Open = new DefenseSample(false, false, false);
        private static readonly DefenseSample Dodging = new DefenseSample(true, false, false);
        private static readonly DefenseSample Parrying = new DefenseSample(false, true, true);
        private static readonly DefenseSample Guarding = new DefenseSample(false, false, true);

        [Fact]
        public void DodgeJustBeforeTheStrike_IsForgivenWithinLatencyTolerance()
        {
            // Newest first: the i-frames ended 2 ticks before the strike landed on the server.
            DefenseSample[] history = { Open, Open, Dodging, Dodging };
            Assert.Equal(HitOutcome.Dodged, CombatResolver.ResolveStrikeOnPlayer(history, toleranceTicks: 3, attackerInFront: true));
            Assert.Equal(HitOutcome.Hit, CombatResolver.ResolveStrikeOnPlayer(history, toleranceTicks: 1, attackerInFront: true));
        }

        [Fact]
        public void ToleranceIsCappedSoHighPingCannotRewindForever()
        {
            var history = new DefenseSample[12];
            for (int age = 0; age < history.Length; age++)
            {
                history[age] = Open;
            }
            history[SimConstants.MAX_LAG_COMPENSATION_TICKS + 2] = Dodging;
            Assert.Equal(HitOutcome.Hit, CombatResolver.ResolveStrikeOnPlayer(history, toleranceTicks: 50, attackerInFront: true));
        }

        [Fact]
        public void ParryAndBlockOnlyWorkFromTheFront()
        {
            DefenseSample[] parry = { Parrying };
            DefenseSample[] guard = { Guarding };
            Assert.Equal(HitOutcome.Parried, CombatResolver.ResolveStrikeOnPlayer(parry, 0, attackerInFront: true));
            Assert.Equal(HitOutcome.Hit, CombatResolver.ResolveStrikeOnPlayer(parry, 0, attackerInFront: false));
            Assert.Equal(HitOutcome.Blocked, CombatResolver.ResolveStrikeOnPlayer(guard, 0, attackerInFront: true));
            Assert.Equal(HitOutcome.Hit, CombatResolver.ResolveStrikeOnPlayer(guard, 0, attackerInFront: false));
        }

        [Fact]
        public void InArc_RespectsRangeAndCone()
        {
            Assert.True(SimMath.InArc(Vector3.Zero, 0f, new Vector3(0, 0, 2), 2.4f, 50f));
            Assert.False(SimMath.InArc(Vector3.Zero, 0f, new Vector3(0, 0, 3), 2.4f, 50f));
            Assert.False(SimMath.InArc(Vector3.Zero, 0f, new Vector3(2, 0, 0.2f), 2.4f, 50f));
            Assert.True(SimMath.InArc(Vector3.Zero, MathF.PI / 2f, new Vector3(2, 0, 0.2f), 2.4f, 50f));
        }

        [Fact]
        public void Ghoul_ChasesTelegraphsAndStrikesOnTheLastWindupTick()
        {
            EnemyState ghoul = EnemyState.SpawnGhoul(new Vector3(0, 0, 10));
            Vector3 player = Vector3.Zero;

            int ticks = 0;
            while (ghoul.Action != EnemyAction.Windup && ticks < 300)
            {
                EnemySim.Step(ref ghoul, true, player);
                ticks++;
            }
            Assert.Equal(EnemyAction.Windup, ghoul.Action);
            Assert.True(SimMath.FlatDistance(ghoul.Position, player) <= CombatTuning.GHOUL_ATTACK_RANGE);

            int windupTicks = 0;
            bool struck = false;
            while (!struck)
            {
                struck = EnemySim.Step(ref ghoul, true, player);
                windupTicks++;
            }
            Assert.Equal(CombatTuning.GHOUL_WINDUP_TICKS, windupTicks);
            Assert.Equal(EnemyAction.Active, ghoul.Action);
        }

        [Fact]
        public void Ghoul_KeepsItsTelegraphWhenHit_ButAParryStaggersIt()
        {
            EnemyState ghoul = EnemyState.SpawnGhoul(Vector3.Zero);
            ghoul.Action = EnemyAction.Windup;
            ghoul.ActionTicks = 5;
            EnemySim.ApplyDamage(ref ghoul, 10);
            Assert.Equal(EnemyAction.Windup, ghoul.Action);

            EnemySim.ApplyParried(ref ghoul);
            Assert.Equal(EnemyAction.Staggered, ghoul.Action);
            Assert.Equal(CombatTuning.GHOUL_PARRIED_STAGGER_TICKS, ghoul.ActionTicks);
        }

        [Fact]
        public void Ghoul_DiesAtZeroHealth()
        {
            EnemyState ghoul = EnemyState.SpawnGhoul(Vector3.Zero);
            EnemySim.ApplyDamage(ref ghoul, CombatTuning.GHOUL_MAX_HEALTH);
            Assert.True(ghoul.IsDead);
            Assert.False(EnemySim.Step(ref ghoul, true, Vector3.Zero));
        }
    }
}
