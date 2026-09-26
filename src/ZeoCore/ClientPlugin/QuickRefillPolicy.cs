using System;
namespace ZeoCore
{
    // Shared by the real controller and offline tests. A transfer is a request,
    // not a confirmed inventory change; never stack requests on stale counts.
    internal sealed class RefillTransferGate
    {
        internal bool Pending { get; private set; }
        internal double Expected { get; private set; }
        internal DateTime SentUtc { get; private set; }
        internal void Sent(double observed,double amount,DateTime now)
        {
            if(Pending || amount<=0 || double.IsNaN(amount) || double.IsInfinity(amount))
                throw new InvalidOperationException("Invalid or overlapping refill request.");
            Expected=observed+amount;SentUtc=now;Pending=true;
        }
        internal bool Observe(double current)
        {
            if(Pending && current+.001>=Expected) Pending=false;
            return !Pending;
        }
        internal bool TimedOut(DateTime now) { return Pending && (now-SentUtc).TotalSeconds>=12; }
    }
    internal static class QuickRefillPolicy
    {
        internal static int Missing(double wanted,double have)
        {
            if(double.IsNaN(wanted)||double.IsNaN(have)||double.IsInfinity(wanted)||double.IsInfinity(have)) return 0;
            return (int)Math.Max(0,Math.Min(1000000,Math.Floor(wanted-have)));
        }
        internal static double Want(HudSettings s,string key)
        {
            switch(key) {
                case "PDC40":return s.WantPdc40;case "PDC40IMP":return s.WantPdc40Improvised;case "PDC50":return s.WantPdc50;
                case "SABOT80":return s.WantSabot80;case "SABOT80IMP":return s.WantSabot80Improvised;case "SABOT100":return s.WantSabot100;
                case "TORP160":return s.WantTorp160;case "TORP190":return s.WantTorp190;case "TORP220":return s.WantTorp220;
                default:return 0;
            }
        }
    }
}
