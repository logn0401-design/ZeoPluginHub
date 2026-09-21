using System;
using System.Collections.Generic;

namespace ZeoNav
{
    // Remember commands sent to the server; local readback can lag behind them.
    internal sealed class ThrustCommandCache
    {
        private sealed class Entry { public float Ratio; public double SentAt; }
        private readonly Dictionary<long, Entry> sent = new Dictionary<long, Entry>();
        public bool ShouldSend(long id, float ratio, float readback, double now)
        {
            Entry prior;
            if (sent.TryGetValue(id, out prior) && prior.Ratio == ratio &&
                (Math.Abs(readback - ratio) < .0001f || now - prior.SentAt < .5)) return false;
            sent[id] = new Entry { Ratio = ratio, SentAt = now };
            return true;
        }
        public float PreviousRatio(long id, float readback)
        {
            Entry prior;
            return sent.TryGetValue(id, out prior) ? prior.Ratio : readback;
        }
        public void Forget(long id) { sent.Remove(id); }
        public void Reset() { sent.Clear(); }
    }

    internal sealed class AlignmentThrustGate
    {
        private bool enabled;
        public bool Allow(double degrees)
        {
            if (!SignalBudget.Finite(degrees) || degrees > .5) enabled = false;
            else if (degrees <= .25) enabled = true;
            return enabled;
        }
        public void Reset() { enabled = false; }
    }
}
