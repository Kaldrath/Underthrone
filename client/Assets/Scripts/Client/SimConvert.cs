using NVector3 = System.Numerics.Vector3;
using UVector3 = UnityEngine.Vector3;

namespace Underthrone.Client
{
    /// The shared simulation uses System.Numerics; Unity uses its own Vector3.
    internal static class SimConvert
    {
        public static UVector3 ToUnity(this NVector3 v) => new UVector3(v.X, v.Y, v.Z);

        public static NVector3 ToSim(this UVector3 v) => new NVector3(v.x, v.y, v.z);

        public static float YawDegrees(float yawRadians) => yawRadians * UnityEngine.Mathf.Rad2Deg;
    }
}
