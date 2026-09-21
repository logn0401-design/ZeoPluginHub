using System;
using VRageMath;

namespace ZeoNav
{
    // Small-angle hold uses real angular velocity, not error differences between
    // irregular multiplayer pose updates. Large-angle NavOS turns stay separate.
    internal sealed class PrecisionAim
    {
        public bool Active { get; private set; }
        public void Reset() { Active = false; }
        public bool TryRate(Vector3D forward, Vector3D target, Vector3D angularVelocity, out Vector3D worldRate)
        {
            worldRate = Vector3D.Zero;
            if (!Finite(forward) || !Finite(target) || !Finite(angularVelocity) ||
                forward.LengthSquared() < 1e-10 || target.LengthSquared() < 1e-10)
            { Active = false; return false; }
            forward.Normalize(); target.Normalize();
            double angle = Math.Acos(Math.Max(-1, Math.Min(1, Vector3D.Dot(forward,target))));
            double degrees = angle * 180 / Math.PI;
            if (degrees > 5) Active = false;
            else if (degrees <= 3) Active = true;
            if (!Active) return false;
            Vector3D axis = Vector3D.Cross(forward,target);
            Vector3D error = axis.LengthSquared() > 1e-16 ? Vector3D.Normalize(axis)*angle : Vector3D.Zero;
            worldRate = 1.5 * error - .8 * angularVelocity;
            double maxRate = 3 * Math.PI / 180;
            double length = worldRate.Length();
            if (length > maxRate) worldRate *= maxRate / length;
            if (degrees < .02 && angularVelocity.Length() < .02 * Math.PI / 180) worldRate = Vector3D.Zero;
            return true;
        }
        public static bool Settled(double angle, double tolerance, Vector3D angularVelocity)
        {
            return SignalBudget.Finite(angle) && angle <= tolerance && Finite(angularVelocity) &&
                angularVelocity.Length() <= .15 * Math.PI / 180;
        }
        internal static Vector3D GyroCommand(Vector3D worldRate, MatrixD gyroWorld)
        {
            // Current installed IMyGyro setters negate all three local angular axes.
            return Vector3D.TransformNormal(-worldRate, MatrixD.Transpose(gyroWorld));
        }
        private static bool Finite(Vector3D v) { return SignalBudget.Finite(v.X) && SignalBudget.Finite(v.Y) && SignalBudget.Finite(v.Z); }
    }
}
