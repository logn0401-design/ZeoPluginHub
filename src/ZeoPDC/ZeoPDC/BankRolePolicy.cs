using System;
using System.Collections.Generic;
using System.Linq;

namespace ZeoPDC
{
    // Pure, deterministic policy. No target or game-state API writes.
    internal static class BankRolePolicy
    {
        internal sealed class Slot
        {
            public long Id;
            public int Part, Rank = -1, Pressure;
            public string Bank = "UNKNOWN", Role = "OFF", Reason = "DISABLED";
            public bool Functional, Ready, HeatKnown, RangeKnown, Resting, Emergency, PromotionPending;
            public double Heat, MaxRange, ActualRange, DesiredRange, Rof, LastSwap;
        }
        static double Cap(Slot s, double range) { return s.MaxRange > 0 ? Math.Min(s.MaxRange, range) : range; }
        public static void Update(IList<Slot> slots, PdcConfig cfg, double seconds)
        {
            if (!cfg.BankRolesEnabled)
            {
                foreach (var s in slots) { s.Rank = -1; s.Resting = false; s.PromotionPending=false; s.Role = "OFF"; s.Reason = "DISABLED"; s.DesiredRange = Cap(s,cfg.EngagementRangeMeters); }
                return;
            }
            foreach (var group in slots.GroupBy(s => s.Bank))
            {
                var all = group.ToList();
                var live = all.Where(s => s.Functional).OrderBy(s => s.Rank < 0 ? int.MaxValue : s.Rank).ThenBy(s=>s.Id).ThenBy(s=>s.Part).ToList();
                if (live.Any(s=>s.Rank<0) || live.Select(s=>s.Rank).Distinct().Count()!=live.Count || live.Any(s=>s.Rank>=live.Count))
                {
                    live = live.OrderBy(s=>s.Ready && s.HeatKnown ? 0 : 1).ThenBy(s=>s.HeatKnown?s.Heat:100).ThenBy(s=>s.Id).ThenBy(s=>s.Part).ToList();
                    for(int i=0;i<live.Count;i++) { live[i].Rank=i; live[i].LastSwap=seconds; }
                }
                foreach(var s in all) s.Reason="FIXED_TIERS";
                if (cfg.BankRotationEnabled && live.Count>1 && seconds-live.Max(s=>s.LastSwap)>=cfg.BankRoleHoldSeconds)
                {
                    var outer=live.OrderBy(s=>s.Rank).First();
                    var replacement=live.Where(s=>s!=outer && s.Ready && s.HeatKnown && !s.Resting && s.Heat<=cfg.BankResumeHeatPercent)
                        .OrderBy(s=>s.Heat).ThenBy(s=>s.Rank).FirstOrDefault();
                    if(outer.HeatKnown && outer.Heat>=cfg.BankRotateHeatPercent && replacement!=null && outer.Heat-replacement.Heat>=cfg.BankSwapHeatMargin)
                    {
                        int rank=outer.Rank; outer.Rank=replacement.Rank; replacement.Rank=rank;
                        foreach(var s in live) s.LastSwap=seconds;
                    }
                }
                var requested=new Dictionary<Slot,double>();
                foreach(var s in all)
                {
                    bool reserve=live.Any(x=>x!=s && x.Ready && x.HeatKnown && x.Heat<cfg.BankResumeHeatPercent);
                    s.Resting=cfg.BankRotationEnabled && s.HeatKnown && reserve &&
                        (s.Heat>=cfg.BankRestHeatPercent || (s.Resting && s.Heat>cfg.BankResumeHeatPercent));
                    int outerCount=Math.Max(1,(s.Pressure+cfg.BankThreatsPerOuterGun-1)/cfg.BankThreatsPerOuterGun);
                    int tier=s.Rank<outerCount?0:s.Rank==outerCount?1:2;
                    double range=tier==0?cfg.EngagementRangeMeters:tier==1?cfg.BankMiddleRangeMeters:cfg.BankCloseRangeMeters;
                    s.Rof=tier==0?cfg.FarRof:tier==1?cfg.BankMiddleRof:cfg.BankCloseRof;
                    s.Role=!s.Functional?"UNAVAILABLE":s.Resting?"COOLING":tier==0?"OUTER":tier==1?"MIDDLE":"CLOSE";
                    if(outerCount>1) s.Reason="PRESSURE_SUPPORT";
                    if(s.Resting) s.Reason="HEAT_RESERVE";
                    if(s.Emergency) { range=cfg.EngagementRangeMeters; s.Reason="SURVIVAL_OVERRIDE"; }
                    requested[s]=Cap(s,range);
                }
                // Readback-confirmed promotion precedes any reduction of another
                // gun's range. Unknown/failed readback holds coverage open.
                foreach(var s in live)
                {
                    if(requested[s]>s.DesiredRange+1) s.PromotionPending=true;
                    if(s.Ready && s.RangeKnown && s.ActualRange+1>=requested[s]) s.PromotionPending=false;
                }
                bool waiting=live.Any(s=>s.PromotionPending);
                foreach(var s in all)
                {
                    double desired=requested[s];
                    if(waiting && desired<s.DesiredRange && s.Functional) { desired=s.DesiredRange; s.Reason="HANDOVER_WAIT"; }
                    s.DesiredRange=desired;
                }
            }
        }
        public static double Rate(Slot s, double baseline, PdcConfig cfg, bool survival)
        {
            if(!cfg.BankRolesEnabled) return baseline;
            double rate=Math.Max(baseline,s.Rof);
            if(!survival && s.HeatKnown)
            {
                if(s.Heat>=96) rate=Math.Min(rate,.52);
                else if(s.Heat>=90) rate=Math.Min(rate,.55);
                else if(s.Heat>=80) rate=Math.Min(rate,.60);
                else if(s.Heat>=cfg.HeatThrottleStartPercent) rate=Math.Min(rate,.65);
            }
            return Math.Max(.5,Math.Min(1,rate));
        }
    }
}
