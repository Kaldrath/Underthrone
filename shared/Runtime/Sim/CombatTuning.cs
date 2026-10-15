namespace Underthrone.Shared.Sim
{
    /// Souls-style timings, slowed slightly so they survive ~100 ms of latency.
    /// 30 ticks = 1 second.
    public static class CombatTuning
    {
        // --- Player body ---
        public const int PLAYER_MAX_HEALTH = 100;
        public const float PLAYER_MAX_STAMINA = 100f;
        public const float STAMINA_REGEN_PER_SECOND = 30f;
        public const float STAMINA_REGEN_BLOCKING_SCALE = 0.35f;
        public const int STAMINA_REGEN_DELAY_TICKS = 15;
        public const float WALK_SPEED = 4.5f;
        public const float BLOCK_WALK_SPEED = 2f;
        public const float ATTACK_WALK_SPEED = 0.8f;

        // --- Light attack ---
        public const float LIGHT_STAMINA_COST = 18f;
        public const int LIGHT_WINDUP_TICKS = 8;     // 267 ms
        public const int LIGHT_ACTIVE_TICKS = 4;     // 133 ms hit window
        public const int LIGHT_RECOVERY_TICKS = 10;  // 333 ms
        public const int LIGHT_DAMAGE = 20;
        public const float LIGHT_RANGE = 2.4f;
        public const float LIGHT_HALF_ARC_DEGREES = 50f;

        // --- Dodge (short sidestep / backstep suits first person better than a roll) ---
        public const float DODGE_STAMINA_COST = 25f;
        public const int DODGE_TICKS = 12;           // 400 ms
        public const int DODGE_IFRAME_TICKS = 7;     // 233 ms of invulnerability
        public const float DODGE_SPEED = 8f;

        // --- Block / parry ---
        public const int PARRY_WINDOW_TICKS = 6;     // 200 ms after raising the guard
        public const float BLOCK_STAMINA_PER_DAMAGE = 1.2f;
        public const int GUARD_BREAK_STAGGER_TICKS = 30;
        public const int HIT_STAGGER_TICKS = 9;

        // --- Barrow Ghoul (first enemy) ---
        public const int GHOUL_MAX_HEALTH = 120;
        public const float GHOUL_SPEED = 3f;
        public const float GHOUL_AGGRO_RADIUS = 15f;
        public const float GHOUL_ATTACK_RANGE = 2.2f;
        public const float GHOUL_HALF_ARC_DEGREES = 60f;
        public const int GHOUL_WINDUP_TICKS = 15;    // 500 ms telegraph
        public const int GHOUL_ACTIVE_TICKS = 3;
        public const int GHOUL_RECOVERY_TICKS = 20;
        public const int GHOUL_DAMAGE = 25;
        public const int GHOUL_PARRIED_STAGGER_TICKS = 45;
        public const int GHOUL_HIT_STAGGER_TICKS = 6;
    }
}
