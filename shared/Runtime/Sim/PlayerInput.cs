using System;

namespace Underthrone.Shared.Sim
{
    [Flags]
    public enum InputButtons : byte
    {
        None = 0,
        Attack = 1 << 0,
        Dodge = 1 << 1,
        Block = 1 << 2,
    }

    /// One tick of player intent. Values are stored quantised exactly as they travel on the
    /// wire, so the client predicts with the same numbers the server simulates.
    public readonly struct PlayerInput
    {
        public readonly uint Tick;
        public readonly sbyte MoveX;      // strafe, -127..127
        public readonly sbyte MoveY;      // forward, -127..127
        public readonly ushort YawQ;      // 0..65535 maps to 0..2π
        public readonly InputButtons Buttons;

        public PlayerInput(uint tick, sbyte moveX, sbyte moveY, ushort yawQ, InputButtons buttons)
        {
            Tick = tick;
            MoveX = moveX;
            MoveY = moveY;
            YawQ = yawQ;
            Buttons = buttons;
        }

        /// Builds an input from unquantised values (move axes -1..1, yaw in radians).
        public static PlayerInput Create(uint tick, float moveX, float moveY, float yawRadians, InputButtons buttons)
        {
            return new PlayerInput(tick, QuantizeAxis(moveX), QuantizeAxis(moveY), QuantizeYaw(yawRadians), buttons);
        }

        public float MoveXf => MoveX / 127f;
        public float MoveYf => MoveY / 127f;
        public float Yaw => YawQ * (MathF.PI * 2f / 65536f);

        public bool Held(InputButtons button) => (Buttons & button) != 0;

        public static sbyte QuantizeAxis(float value)
        {
            float clamped = value < -1f ? -1f : value > 1f ? 1f : value;
            return (sbyte)MathF.Round(clamped * 127f);
        }

        public static ushort QuantizeYaw(float radians)
        {
            float turns = radians / (MathF.PI * 2f);
            turns -= MathF.Floor(turns);
            return (ushort)((int)MathF.Round(turns * 65536f) & 0xFFFF);
        }
    }
}
