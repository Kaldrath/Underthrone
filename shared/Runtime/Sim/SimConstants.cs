namespace Underthrone.Shared.Sim
{
    /// Fixed-step simulation settings shared by client prediction and the server.
    /// All gameplay timings are expressed in ticks so both sides agree exactly.
    public static class SimConstants
    {
        public const int TICK_RATE = 30;
        public const float TICK_DELTA = 1f / TICK_RATE;

        /// Radius of the walkable arena in the Phase 1 slice (metres).
        public const float ARENA_RADIUS = 40f;

        /// Most input ticks the server will rewind/forgive for latency (200 ms at 30 Hz).
        public const int MAX_LAG_COMPENSATION_TICKS = 6;
    }
}
