using System.Collections.Generic;
using Underthrone.Shared.Sim;

namespace Underthrone.Shared.Client
{
    /// Client-side prediction with server reconciliation for the local player.
    /// Each local tick: Predict(input) moves the body immediately and keeps the input.
    /// Each snapshot: Reconcile(ack, serverState) rewinds to the server's truth and replays every
    /// input the server has not processed yet, so corrections are small and usually invisible.
    public sealed class ClientPrediction
    {
        private const int MAX_PENDING = 128;

        private readonly List<PlayerInput> _pending = new List<PlayerInput>(MAX_PENDING);
        private PlayerState _state;

        public ClientPrediction(PlayerState initial)
        {
            _state = initial;
        }

        public PlayerState State => _state;
        public IReadOnlyList<PlayerInput> PendingInputs => _pending;
        /// Distance the body jumped on the last reconcile (0 when prediction was exact).
        public float LastCorrection { get; private set; }

        public void Predict(in PlayerInput input)
        {
            PlayerSim.Step(ref _state, input);
            _pending.Add(input);
            if (_pending.Count > MAX_PENDING)
            {
                _pending.RemoveAt(0);
            }
        }

        public void Reconcile(uint ackInputTick, in PlayerState serverState)
        {
            int acked = 0;
            while (acked < _pending.Count && _pending[acked].Tick <= ackInputTick)
            {
                acked++;
            }
            _pending.RemoveRange(0, acked);

            PlayerState replayed = serverState;
            for (int inputIndex = 0; inputIndex < _pending.Count; inputIndex++)
            {
                PlayerSim.Step(ref replayed, _pending[inputIndex]);
            }
            LastCorrection = SimMath.FlatDistance(replayed.Position, _state.Position);
            _state = replayed;
        }
    }
}
