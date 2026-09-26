using System;
using VRageMath;

namespace ZeoPDC
{
    // An eligibility estimate, not a hit probability. All velocities are world m/s.
    internal static class OuterIntercept
    {
        internal const double Delay = .25; // audited 12-tick startup plus three ticks margin
        internal const double Life = 59d / 60d - .10;
        internal sealed class Solution
        {
            public Vector3D Direction;
            public double FlightSeconds = double.NaN, TravelMeters = double.NaN;
            public bool CanFire;
            public string Reason = "INVALID_KINEMATICS";
        }
        internal static Solution Solve(Vector3D relative, Vector3D targetVelocity, Vector3D shipVelocity,
            Vector3D targetAcceleration, Vector3D naturalGravity, double hull, double closing, double handoff, double delay = Delay)
        {
            var s = new Solution();
            if (!BankPlanner.Finite(relative) || !BankPlanner.Finite(targetVelocity) || !BankPlanner.Finite(shipVelocity) ||
                !BankPlanner.Finite(targetAcceleration) || !BankPlanner.Finite(naturalGravity) || relative.LengthSquared() < 1 ||
                !BankPlanner.Finite(hull) || !BankPlanner.Finite(closing) || !BankPlanner.Finite(handoff) || !BankPlanner.Finite(delay) || delay<0 || delay>Delay) return s;
            Vector3D velocity = targetVelocity - shipVelocity;
            // Long-horizon look is useful even when ammunition cannot reach the target.
            double nominal = Root(relative, velocity, targetAcceleration, naturalGravity * 3, 3000, 3, delay);
            if (double.IsNaN(nominal)) { s.Direction = Vector3D.Normalize(relative); s.Reason = "NO_INTERCEPT"; return s; }
            Vector3D lead = Offset(relative, velocity, targetAcceleration, naturalGravity * 3, nominal, delay);
            s.Direction = Vector3D.Normalize(lead); s.FlightSeconds = nominal; s.TravelMeters = 3000 * nominal;
            double slow = Root(relative, velocity, targetAcceleration, naturalGravity * 3, 2850, Life, delay);
            if (double.IsNaN(slow) || s.TravelMeters > 3400) { s.Reason = "AMMO_LIFETIME"; return s; }
            if (naturalGravity.Length() > .01) { s.Reason = "GRAVITY_NOT_VALIDATED"; return s; }
            if (targetAcceleration.Length() > 25) { s.Reason = "MANEUVER_UNCERTAINTY"; return s; }
            if (closing <= 0 || hull - closing * delay <= handoff + 25) { s.Reason = "HANDOFF_BEFORE_SHOT"; return s; }
            s.CanFire = true; s.Reason = "FEASIBLE_ESTIMATE"; return s;
        }
        static Vector3D Offset(Vector3D p, Vector3D v, Vector3D a, Vector3D gravity, double t, double delay)
        {
            double total = t + delay;
            return p + v * total + a * (.5 * total * total) - gravity * (.5 * t * t);
        }
        static double Root(Vector3D p, Vector3D v, Vector3D a, Vector3D gravity, double speed, double limit, double delay)
        {
            // Find the first crossing; do not assume a receding/accelerating target has a root.
            double lo = 0;
            for (int i = 1; i <= 96; i++)
            {
                double hi = limit * i / 96;
                if (Offset(p,v,a,gravity,hi,delay).Length() - speed * hi <= 0)
                {
                    for (int j = 0; j < 36; j++) { double mid=(lo+hi)/2; if (Offset(p,v,a,gravity,mid,delay).Length()>speed*mid) lo=mid; else hi=mid; }
                    return hi;
                }
                lo = hi;
            }
            return double.NaN;
        }
        internal static double Error(Vector3D actual, Vector3D desired)
        {
            if (!BankPlanner.Finite(actual) || !BankPlanner.Finite(desired) || actual.LengthSquared()<.5 || desired.LengthSquared()<.5) return double.NaN;
            return Math.Acos(Math.Max(-1,Math.Min(1,Vector3D.Dot(Vector3D.Normalize(actual),Vector3D.Normalize(desired))))) * 180 / Math.PI;
        }
    }
}
