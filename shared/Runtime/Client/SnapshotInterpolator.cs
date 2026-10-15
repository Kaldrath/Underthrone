using System;
using Underthrone.Shared.Protocol;

namespace Underthrone.Shared.Client
{
    /// Smooths one remote entity by drawing it slightly in the past, between the two snapshots
    /// that bracket the render time. Holds a short ring of timestamped samples.
    public sealed class SnapshotInterpolator
    {
        private const int CAPACITY = 16;

        private readonly double[] _times = new double[CAPACITY];
        private readonly EntitySnapshot[] _samples = new EntitySnapshot[CAPACITY];
        private int _count;
        private int _newest = -1;

        public void Add(double serverTime, in EntitySnapshot sample)
        {
            if (_count > 0 && serverTime <= _times[_newest])
            {
                return;
            }
            _newest = (_newest + 1) % CAPACITY;
            _times[_newest] = serverTime;
            _samples[_newest] = sample;
            if (_count < CAPACITY)
            {
                _count++;
            }
        }

        public bool HasData => _count > 0;

        /// Position/yaw at <paramref name="renderTime"/>. Discrete fields (action, health) come from
        /// the older bracketing sample so state changes are never shown early.
        public EntitySnapshot Sample(double renderTime, out float yawRadians)
        {
            if (_count == 0)
            {
                throw new InvalidOperationException("No samples");
            }
            int newer = _newest;
            for (int age = 0; age < _count; age++)
            {
                int index = (_newest - age + CAPACITY) % CAPACITY;
                if (_times[index] <= renderTime || age == _count - 1)
                {
                    int older = index;
                    EntitySnapshot a = _samples[older];
                    if (older == newer || age == 0)
                    {
                        yawRadians = YawOf(a);
                        return a;
                    }
                    EntitySnapshot b = _samples[newer];
                    double span = _times[newer] - _times[older];
                    float t = span > 0 ? (float)Math.Clamp((renderTime - _times[older]) / span, 0.0, 1.0) : 1f;
                    EntitySnapshot result = a;
                    result.X = a.X + (b.X - a.X) * t;
                    result.Z = a.Z + (b.Z - a.Z) * t;
                    yawRadians = LerpAngle(YawOf(a), YawOf(b), t);
                    return result;
                }
                newer = index;
            }
            yawRadians = YawOf(_samples[_newest]);
            return _samples[_newest];
        }

        private static float YawOf(in EntitySnapshot s) => s.YawQ * (MathF.PI * 2f / 65536f);

        private static float LerpAngle(float a, float b, float t)
        {
            float delta = b - a;
            while (delta > MathF.PI)
            {
                delta -= MathF.PI * 2f;
            }
            while (delta < -MathF.PI)
            {
                delta += MathF.PI * 2f;
            }
            return a + delta * t;
        }
    }
}
