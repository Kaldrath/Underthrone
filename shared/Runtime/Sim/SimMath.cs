using System;
using System.Numerics;

namespace Underthrone.Shared.Sim
{
    public static class SimMath
    {
        public static Vector3 Forward(float yaw) => new Vector3(MathF.Sin(yaw), 0f, MathF.Cos(yaw));

        public static Vector3 Right(float yaw) => new Vector3(MathF.Cos(yaw), 0f, -MathF.Sin(yaw));

        public static float YawTowards(Vector3 from, Vector3 to)
        {
            return MathF.Atan2(to.X - from.X, to.Z - from.Z);
        }

        public static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.X - b.X;
            float dz = a.Z - b.Z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        /// True when <paramref name="target"/> is within range and inside the cone in front of
        /// <paramref name="origin"/> facing <paramref name="yaw"/>.
        public static bool InArc(Vector3 origin, float yaw, Vector3 target, float range, float halfArcDegrees)
        {
            float dx = target.X - origin.X;
            float dz = target.Z - origin.Z;
            float distSq = dx * dx + dz * dz;
            if (distSq > range * range)
            {
                return false;
            }
            if (distSq < 1e-6f)
            {
                return true;
            }
            float dist = MathF.Sqrt(distSq);
            Vector3 forward = Forward(yaw);
            float cos = (dx * forward.X + dz * forward.Z) / dist;
            return cos >= MathF.Cos(halfArcDegrees * (MathF.PI / 180f));
        }

        public static Vector3 ClampToArena(Vector3 position, float radius)
        {
            float lenSq = position.X * position.X + position.Z * position.Z;
            if (lenSq <= radius * radius)
            {
                return position;
            }
            float scale = radius / MathF.Sqrt(lenSq);
            return new Vector3(position.X * scale, position.Y, position.Z * scale);
        }
    }
}
