using System;
using VRageMath;

namespace ZeoNav
{
    internal enum TurnProgress { Continue, Recover, Fail }

    internal sealed class TurnProgressWatchdog
    {
        private DateTime started, lastProgress;
        private double bestAngle;
        private bool recovered;

        internal void Reset(DateTime now, double angle)
        {
            started = lastProgress = now;
            bestAngle = angle;
            recovered = false;
        }

        internal TurnProgress Observe(DateTime now, double angle, double rateDeg,
            double requestDeg, int commandedGyros)
        {
            if (started == DateTime.MinValue) Reset(now, angle);
            if (!SignalBudget.Finite(angle) || !SignalBudget.Finite(rateDeg) ||
                !SignalBudget.Finite(requestDeg)) return TurnProgress.Fail;
            if (angle < bestAngle - .5)
            {
                bestAngle = angle;
                lastProgress = now;
            }
            if (angle <= .25) return TurnProgress.Continue;
            double stalled = (now - lastProgress).TotalSeconds;
            if ((now - started).TotalSeconds > 300 ||
                (recovered && stalled > 12)) return TurnProgress.Fail;
            if (!recovered && stalled > 10)
            {
                recovered = true;
                lastProgress = now;
                return TurnProgress.Recover;
            }
            return TurnProgress.Continue;
        }
    }

    internal static class TurnAuthorityPolicy
    {
        // Keep the standard bank until the requested heading and angular rate
        // have both settled. A fixed five-degree handoff stranded heavy hulls.
        internal static bool FullBank(double angle, double tolerance, double rateRad,
            bool bankAlreadyActive)
        {
            if (!SignalBudget.Finite(angle) || !SignalBudget.Finite(rateRad)) return false;
            if (angle > Math.Max(.08, tolerance) || rateRad > .15 * Math.PI / 180)
                return true;
            return bankAlreadyActive && angle > Math.Min(.08, tolerance);
        }

        internal static double FineRateLimit(double radians, TurnResponse response)
        {
            const double unknown = 1.5 * Math.PI / 180;
            if (response == null || !response.Ready) return unknown;
            double safe = .8 * Math.Sqrt(2 * response.Deceleration * Math.Max(.00035, radians));
            return Math.Max(.05 * Math.PI / 180, Math.Min(3 * Math.PI / 180, safe));
        }
    }

    // Passive observations are accepted only when current pose and reported
    // angular velocity agree. This never commands a calibration turn.
    internal sealed class PassiveTurnLearner
    {
        private double lastTime, lastOmega;
        private Vector3D lastForward, lastAxis;
        private bool hasSample, braking;

        internal void Reset() { hasSample = braking = false; }

        internal bool Observe(double now, Vector3D forward, Vector3D axis,
            Vector3D angular, Vector3D command, TurnResponse response)
        {
            if (response == null || !Valid(forward) || !Valid(axis) ||
                !Valid(angular) || !Valid(command) ||
                forward.LengthSquared() < .5 || axis.LengthSquared() < .5 ||
                !SignalBudget.Finite(now)) { Reset(); return false; }
            forward.Normalize(); axis.Normalize();
            double omega = Vector3D.Dot(angular, axis);
            double requested = Vector3D.Dot(command, axis);
            if (!hasSample)
            {
                Store(now, forward, axis, omega);
                return false;
            }
            double dt = now - lastTime;
            if (dt < .25) return false;
            if (dt > .75 || Vector3D.Dot(axis, lastAxis) < .95)
            {
                Reset(); Store(now, forward, axis, omega); return false;
            }
            double turn = Vector3D.Dot(axis, Vector3D.Cross(lastForward, forward));
            double expected = (lastOmega + omega) * .5 * dt;
            if (Math.Abs(turn - expected) > Math.Max(.5 * Math.PI / 180,
                Math.Abs(expected) * .6))
            {
                Reset(); Store(now, forward, axis, omega); return false;
            }
            double priorSpeed = Math.Abs(lastOmega), speed = Math.Abs(omega);
            bool accelerating = requested * omega > 0 &&
                Math.Abs(requested) > speed + .025 && speed > priorSpeed + .005;
            bool decelerating = priorSpeed > .025 && speed < priorSpeed - .005 &&
                (requested * lastOmega <= 0 || Math.Abs(requested) < priorSpeed - .025);
            if (accelerating)
            {
                response.Accel(speed - priorSpeed, dt);
                braking = false;
            }
            else if (decelerating && !braking)
            {
                response.Stop(priorSpeed - speed, dt);
                braking = true;
            }
            Store(now, forward, axis, omega);
            return accelerating || decelerating;
        }

        private void Store(double now, Vector3D forward, Vector3D axis, double omega)
        { lastTime = now; lastForward = forward; lastAxis = axis; lastOmega = omega; hasSample = true; }
        private static bool Valid(Vector3D v)
        { return SignalBudget.Finite(v.X) && SignalBudget.Finite(v.Y) && SignalBudget.Finite(v.Z); }
    }
}
