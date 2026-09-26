using System;
using System.Collections.Generic;

namespace ZeoPDC
{
    // Pure policy: times are plugin simulation ticks. Callback counts are NOT rounds/hits.
    internal static class PreemptivePolicy
    {
        internal sealed class State
        {
            public bool Active, HeatHold;
            public int EndFrame, NextFrame;
            public ulong Target;
            public long ShotMark, Bursts, AcceptedCallbacks;
            public readonly Dictionary<ulong, long> TargetCallbacks = new Dictionary<ulong, long>();
            public readonly Dictionary<ulong, int> TargetBursts = new Dictionary<ulong, int>();
        }
        public static int Ticks(double seconds) { return Math.Max(1, (int)Math.Ceiling(seconds * 60)); }
        public static bool HeatAllowed(State s, double heat, PdcConfig c)
        {
            if (!BankPlanner.Finite(heat) || heat >= c.PreemptiveStopHeatPercent) s.HeatHold = true;
            else if (heat <= c.PreemptiveResumeHeatPercent) s.HeatHold = false;
            return !s.HeatHold;
        }
        public static bool Expired(State s, int frame, long callbacks, PdcConfig c)
        {
            return s.Active && (frame >= s.EndFrame || callbacks < s.ShotMark ||
                callbacks - s.ShotMark >= c.PreemptiveMaxCallbacksPerBurst);
        }
        public static void Stop(State s, int frame, long callbacks, PdcConfig c)
        {
            if (!s.Active) return;
            long delta = Math.Max(0, callbacks - s.ShotMark), previous;
            s.TargetCallbacks.TryGetValue(s.Target, out previous);
            s.TargetCallbacks[s.Target] = previous + delta;
            s.AcceptedCallbacks += delta;
            s.Active = false;
            s.NextFrame = frame + Ticks(c.PreemptiveCooldownSeconds);
        }
        public static string Evaluate(State s, int frame, long callbacks, ulong target,
            bool eligible, double heat, PdcConfig c)
        {
            bool cool = HeatAllowed(s, heat, c);
            if (s.Active && (!eligible || !cool || target != s.Target || Expired(s, frame, callbacks, c)))
                Stop(s, frame, callbacks, c);
            if (!eligible) return "INELIGIBLE";
            if (!cool) return "HEAT_HOLD";
            if (s.Active) return "BURST";
            if (frame < s.NextFrame) return "COOLDOWN";
            if (target == 0) return "NO_NATIVE_TARGET";
            long spent;
            if (s.TargetCallbacks.TryGetValue(target, out spent) && spent >= c.PreemptiveMaxCallbacksPerTarget)
                return "TARGET_BUDGET";
            int bursts;
            if (s.TargetBursts.TryGetValue(target, out bursts) && bursts >= 2) return "TARGET_BURST_LIMIT";
            // Bound memory without forgetting budgets and accidentally repeating shots.
            if (!s.TargetCallbacks.ContainsKey(target) && s.TargetCallbacks.Count >= 512) return "BUDGET_CAPACITY";
            s.TargetCallbacks[target] = spent;
            s.TargetBursts[target] = bursts + 1;
            s.Active = true; s.Target = target; s.ShotMark = callbacks;
            s.EndFrame = frame + Ticks(c.PreemptiveBurstSeconds); s.Bursts++;
            return "BURST";
        }
    }
}
