using System;
using System.Collections.Generic;
using System.Numerics;
using Underthrone.Shared.Protocol;
using Underthrone.Shared.Sim;

namespace Underthrone.Server
{
    public readonly struct CombatEventData
    {
        public readonly uint Tick;
        public readonly ushort AttackerId;
        public readonly ushort TargetId;
        public readonly HitOutcome Outcome;
        public readonly ushort Damage;

        public CombatEventData(uint tick, ushort attackerId, ushort targetId, HitOutcome outcome, ushort damage)
        {
            Tick = tick;
            AttackerId = attackerId;
            TargetId = targetId;
            Outcome = outcome;
            Damage = damage;
        }
    }

    /// Authoritative game state for one zone. No networking here: the GameServer feeds inputs in
    /// and reads snapshots/events out, which keeps this fully unit-testable.
    public sealed class World
    {
        public const ushort FIRST_ENEMY_ID = 1000;
        private const int PLAYER_RESPAWN_TICKS = SimConstants.TICK_RATE * 5;
        private const int ENEMY_RESPAWN_TICKS = SimConstants.TICK_RATE * 10;

        private readonly List<ServerPlayer> _players = new List<ServerPlayer>();
        private readonly List<ServerEnemy> _enemies = new List<ServerEnemy>();
        private readonly List<CombatEventData> _events = new List<CombatEventData>();
        private ushort _nextPlayerId = 1;

        public uint Tick { get; private set; }
        public IReadOnlyList<ServerPlayer> Players => _players;
        public IReadOnlyList<ServerEnemy> Enemies => _enemies;
        /// Combat events produced by the last Tick(); cleared at the start of each tick.
        public IReadOnlyList<CombatEventData> Events => _events;

        public ServerEnemy SpawnGhoul(Vector3 position)
        {
            var enemy = new ServerEnemy((ushort)(FIRST_ENEMY_ID + _enemies.Count), position);
            _enemies.Add(enemy);
            return enemy;
        }

        public ServerPlayer AddPlayer(string name)
        {
            ushort id = _nextPlayerId++;
            if (_nextPlayerId >= FIRST_ENEMY_ID)
            {
                _nextPlayerId = 1;
            }
            var player = new ServerPlayer(id, name, PlayerState.Spawn(SpawnPoint(id), 0f));
            _players.Add(player);
            return player;
        }

        public void RemovePlayer(ServerPlayer player)
        {
            _players.Remove(player);
            for (int enemyIndex = 0; enemyIndex < _enemies.Count; enemyIndex++)
            {
                _enemies[enemyIndex].LastSwingHit.Remove(player.Id);
                if (_enemies[enemyIndex].TargetPlayerId == player.Id)
                {
                    _enemies[enemyIndex].TargetPlayerId = 0;
                }
            }
        }

        public void Step()
        {
            Tick++;
            _events.Clear();

            for (int playerIndex = 0; playerIndex < _players.Count; playerIndex++)
            {
                StepPlayer(_players[playerIndex]);
            }
            for (int enemyIndex = 0; enemyIndex < _enemies.Count; enemyIndex++)
            {
                StepEnemy(_enemies[enemyIndex]);
            }
            for (int playerIndex = 0; playerIndex < _players.Count; playerIndex++)
            {
                ResolvePlayerAttack(_players[playerIndex]);
            }
        }

        public void BuildEntitySnapshots(List<EntitySnapshot> into)
        {
            into.Clear();
            for (int playerIndex = 0; playerIndex < _players.Count; playerIndex++)
            {
                ServerPlayer p = _players[playerIndex];
                into.Add(new EntitySnapshot
                {
                    Id = p.Id,
                    Kind = EntityKind.Player,
                    X = p.State.Position.X,
                    Z = p.State.Position.Z,
                    YawQ = PlayerInput.QuantizeYaw(p.State.Yaw),
                    Action = (byte)p.State.Action,
                    Health = (ushort)Math.Max(0, p.State.Health),
                });
            }
            for (int enemyIndex = 0; enemyIndex < _enemies.Count; enemyIndex++)
            {
                ServerEnemy e = _enemies[enemyIndex];
                into.Add(new EntitySnapshot
                {
                    Id = e.Id,
                    Kind = EntityKind.Ghoul,
                    X = e.State.Position.X,
                    Z = e.State.Position.Z,
                    YawQ = PlayerInput.QuantizeYaw(e.State.Yaw),
                    Action = (byte)e.State.Action,
                    Health = (ushort)Math.Max(0, e.State.Health),
                });
            }
        }

        private static Vector3 SpawnPoint(ushort playerId)
        {
            float angle = playerId * 2.399963f; // golden angle spreads spawns out
            return new Vector3(MathF.Sin(angle) * 3f, 0f, MathF.Cos(angle) * 3f - 6f);
        }

        private void StepPlayer(ServerPlayer player)
        {
            if (player.State.IsDead)
            {
                player.NextInput();
                if (--player.RespawnTicks <= 0)
                {
                    player.State = PlayerState.Spawn(SpawnPoint(player.Id), 0f);
                }
                player.Defense.Push(DefenseSample.From(player.State));
                return;
            }
            PlayerInput input = player.NextInput();
            PlayerSim.Step(ref player.State, input);
            player.Defense.Push(DefenseSample.From(player.State));
        }

        private void StepEnemy(ServerEnemy enemy)
        {
            if (enemy.State.IsDead)
            {
                if (--enemy.RespawnTicks <= 0)
                {
                    enemy.State = EnemyState.SpawnGhoul(enemy.Spawn);
                    enemy.LastSwingHit.Clear();
                }
                enemy.Positions.Push(enemy.State.Position);
                return;
            }

            ServerPlayer target = PickTarget(enemy);
            enemy.TargetPlayerId = target != null ? target.Id : (ushort)0;
            Vector3 targetPosition = target != null ? target.State.Position : Vector3.Zero;
            bool strikes = EnemySim.Step(ref enemy.State, target != null, targetPosition);
            enemy.Positions.Push(enemy.State.Position);

            if (strikes && target != null && CombatResolver.GhoulStrikeReaches(enemy.State, target.State.Position))
            {
                ResolveEnemyStrike(enemy, target);
            }
        }

        private ServerPlayer PickTarget(ServerEnemy enemy)
        {
            ServerPlayer best = null;
            float bestDistance = float.MaxValue;
            for (int playerIndex = 0; playerIndex < _players.Count; playerIndex++)
            {
                ServerPlayer p = _players[playerIndex];
                if (p.State.IsDead)
                {
                    continue;
                }
                float distance = SimMath.FlatDistance(enemy.State.Position, p.State.Position);
                // Keep the current target a little longer than a new one would be acquired.
                float range = p.Id == enemy.TargetPlayerId
                    ? CombatTuning.GHOUL_AGGRO_RADIUS * 1.5f
                    : CombatTuning.GHOUL_AGGRO_RADIUS;
                if (distance <= range && distance < bestDistance)
                {
                    best = p;
                    bestDistance = distance;
                }
            }
            return best;
        }

        private void ResolveEnemyStrike(ServerEnemy enemy, ServerPlayer target)
        {
            int window = Math.Min(target.Defense.Count, SimConstants.MAX_LAG_COMPENSATION_TICKS + 1);
            Span<DefenseSample> history = stackalloc DefenseSample[SimConstants.MAX_LAG_COMPENSATION_TICKS + 1];
            for (int age = 0; age < window; age++)
            {
                history[age] = target.Defense.Get(age);
            }
            bool inFront = CombatResolver.IsInFront(target.State, enemy.State.Position);
            HitOutcome outcome = CombatResolver.ResolveStrikeOnPlayer(history.Slice(0, window), target.LatencyTicks, inFront);

            int damage = 0;
            switch (outcome)
            {
                case HitOutcome.Hit:
                    damage = CombatTuning.GHOUL_DAMAGE;
                    PlayerSim.ApplyHit(ref target.State, damage);
                    if (target.State.IsDead)
                    {
                        target.RespawnTicks = PLAYER_RESPAWN_TICKS;
                    }
                    break;
                case HitOutcome.Blocked:
                    if (PlayerSim.ApplyBlockedHit(ref target.State, CombatTuning.GHOUL_DAMAGE))
                    {
                        outcome = HitOutcome.GuardBroken;
                    }
                    break;
                case HitOutcome.Parried:
                    EnemySim.ApplyParried(ref enemy.State);
                    break;
            }
            _events.Add(new CombatEventData(Tick, enemy.Id, target.Id, outcome, (ushort)damage));
        }

        private void ResolvePlayerAttack(ServerPlayer player)
        {
            if (!player.State.IsAttackActive)
            {
                return;
            }
            for (int enemyIndex = 0; enemyIndex < _enemies.Count; enemyIndex++)
            {
                ServerEnemy enemy = _enemies[enemyIndex];
                if (enemy.State.IsDead)
                {
                    continue;
                }
                if (enemy.LastSwingHit.TryGetValue(player.Id, out ushort swing) && swing == player.State.SwingId)
                {
                    continue;
                }
                // Lag compensation: test against where the attacker saw the enemy.
                Vector3 seenAt = enemy.Positions.Get(player.LatencyTicks);
                if (!CombatResolver.LightAttackReaches(player.State, seenAt))
                {
                    continue;
                }
                enemy.LastSwingHit[player.Id] = player.State.SwingId;
                EnemySim.ApplyDamage(ref enemy.State, CombatTuning.LIGHT_DAMAGE);
                if (enemy.State.IsDead)
                {
                    enemy.RespawnTicks = ENEMY_RESPAWN_TICKS;
                }
                _events.Add(new CombatEventData(Tick, player.Id, enemy.Id, HitOutcome.Hit, CombatTuning.LIGHT_DAMAGE));
            }
        }
    }
}
