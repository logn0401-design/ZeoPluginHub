using System;
using VRageMath;

namespace ZeoCore
{
    // Matches the installed Spectrum HUD's simulation clock and motion equation.
    // The API omits acceleration, so derive it only from distinct samples.
    internal static class SpectrumMotion
    {
        internal static double Age(int now, int observed) { return Math.Max(0, ((long)now - observed) / 60.0); }
        internal static Vector3D Acceleration(Vector3D previous, int previousTick, Vector3D current, int currentTick)
        {
            double dt = ((long)currentTick - previousTick) / 60.0;
            return dt > 0 && dt < 5 && Finite(previous) && Finite(current) ? (current - previous) / dt : Vector3D.Zero;
        }
        internal static Vector3D Position(Vector3D position, Vector3D velocity, Vector3D acceleration, int observed, int now)
        {
            double age = Math.Min(15, Age(now, observed));
            if (velocity == Vector3D.Zero || !Finite(velocity)) return position;
            Vector3D result = position + velocity * age;
            if (Finite(acceleration)) result += acceleration * age * age / 2;
            return Finite(result) ? result : position;
        }
        private static bool Finite(Vector3D v)
        {
            return !(double.IsNaN(v.X) || double.IsInfinity(v.X) || double.IsNaN(v.Y) ||
                double.IsInfinity(v.Y) || double.IsNaN(v.Z) || double.IsInfinity(v.Z));
        }
    }
}
