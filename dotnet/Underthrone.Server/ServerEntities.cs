using System.Collections.Generic;
using System.Numerics;
using Underthrone.Shared.Sim;

namespace Underthrone.Server
{
    /// Fixed-size ring of the most recent values, newest first on read.
    public sealed class History<T>
    {
        private readonly T[] _items;
        private int _head;
        private int _count;

        public History(int capacity)
        {
            _items = new T[capacity];
        }

        public int Count => _count;

        public void Push(T item)
        {
            _head = (_head + 1) % _items.Length;
            _items[_head] = item;
            if (_count < _items.Length)
            {
                _count++;
            }
        }

        /// ageTicks 0 = newest. Clamped to the oldest stored value.
        public T Get(int ageTicks)
        {
            if (ageTicks >= _count)
            {
                ageTicks = _count - 1;
            }
            if (ageTicks < 0)
            {
                ageTicks = 0;
            }
            int index = (_head - ageTicks + _items.Length) % _items.Length;
            return _items[index];
        }
    }

    public sealed class ServerPlayer
    {
        private const int MAX_QUEUED_INPUTS = 12;

        public ServerPlayer(ushort id, string name, PlayerState state)
        {
            Id = id;
            Name = name;
            State = state;
        }

        public ushort Id { get; }
        public string Name { get; }
        public PlayerState State;
        public uint LastProcessedInputTick { get; private set; }
        public int LatencyTicks { get; set; }
        public int RespawnTicks { get; set; }
        public History<DefenseSample> Defense { get; } = new History<DefenseSample>(SimConstants.MAX_LAG_COMPENSATION_TICKS + 2);

        private readonly SortedList<uint, PlayerInput> _pending = new SortedList<uint, PlayerInput>();
        private PlayerInput _lastInput;

        /// Accepts redundant/out-of-order inputs; anything already processed or queued is ignored.
        public void Enqueue(PlayerInput input)
        {
            if (input.Tick <= LastProcessedInputTick || _pending.ContainsKey(input.Tick))
            {
                return;
            }
            _pending.Add(input.Tick, input);
            while (_pending.Count > MAX_QUEUED_INPUTS)
            {
                _pending.RemoveAt(0);
            }
        }

        /// The input to simulate this tick. With nothing queued (packet loss) the last input is
        /// repeated; held buttons stay held and no new press is triggered.
        public PlayerInput NextInput()
        {
            if (_pending.Count > 0)
            {
                _lastInput = _pending.Values[0];
                _pending.RemoveAt(0);
                LastProcessedInputTick = _lastInput.Tick;
            }
            return _lastInput;
        }
    }

    public sealed class ServerEnemy
    {
        public ServerEnemy(ushort id, Vector3 spawn)
        {
            Id = id;
            Spawn = spawn;
            State = EnemyState.SpawnGhoul(spawn);
            Positions.Push(spawn);
        }

        public ushort Id { get; }
        public Vector3 Spawn { get; }
        public EnemyState State;
        public ushort TargetPlayerId { get; set; }
        public int RespawnTicks { get; set; }
        /// Positions per tick, for rewinding to what an attacker saw (lag compensation).
        public History<Vector3> Positions { get; } = new History<Vector3>(SimConstants.MAX_LAG_COMPENSATION_TICKS + 2);
        /// Last swing of each player that already hit this enemy, so one swing hits once.
        public Dictionary<ushort, ushort> LastSwingHit { get; } = new Dictionary<ushort, ushort>();
    }
}
