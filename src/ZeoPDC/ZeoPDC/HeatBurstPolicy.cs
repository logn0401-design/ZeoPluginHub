using System;
using System.Collections.Generic;
using System.Linq;

namespace ZeoPDC
{
    internal static class HeatCurvePolicy
    {
        // Absolute fraction of nominal ROF, recomputed from heat, never compounded per tick.
        internal static double Rate(double heat, double start, double slope, double minimum, bool urgent)
        {
            if (!BankPlanner.Finite(heat) || heat < 0 || heat > 100) return 1;
            if (urgent) return 1;
            return Math.Max(minimum, Math.Min(1, 1 - Math.Max(0, heat-start)*slope/100));
        }
    }

    internal sealed class BurstDecision
    {
        internal bool Pause;
        internal string Reason="NATIVE";
        internal long ObservedRounds;
        internal double SpentUnits, BudgetUnits;
        internal int PauseUntil;
    }
    // A shared target budget of nominal projectile-health damage units. These are
    // associated fired projectiles, not hits or damage credit. All methods are locked
    // because projectile callbacks can arrive separately from the simulation update.
    internal sealed class SharedBurstPolicy
    {
        sealed class Entry
        {
            internal long Rounds;
            internal double Spent, Budget;
            internal int PauseUntil, LastSeen, LastShot=-100000;
        }
        readonly object gate=new object();
        readonly Dictionary<ulong,Entry> targets=new Dictionary<ulong,Entry>();
        internal volatile bool Enabled;
        internal bool Overflow;
        internal void Reset()
        { lock(gate) { Enabled=false; targets.Clear(); Overflow=false; } }
        Entry Get(ulong id,int tick)
        {
            Entry e;
            if(targets.TryGetValue(id,out e)) return e;
            foreach(var key in targets.Where(p=>tick-p.Value.LastSeen>180).Select(p=>p.Key).ToArray()) targets.Remove(key);
            if(targets.Count>=1024) { Overflow=true; return null; }
            targets[id]=e=new Entry {LastSeen=tick}; return e;
        }
        internal void Observe(ulong target,int tick,double nominalDamage)
        {
            if(!Enabled || target==0 || !BankPlanner.Finite(nominalDamage) || nominalDamage<=0) return;
            lock(gate) {
                if(!Enabled) return;
                var e=Get(target,tick); if(e==null) return;
                e.Rounds++; e.Spent+=nominalDamage; e.LastShot=tick; e.LastSeen=tick;
            }
        }
        internal BurstDecision Decide(ulong target,int tick,double health,double damage,
            int initialRounds,int topupRounds,double margin,int pauseTicks,bool urgent)
        {
            var d=new BurstDecision();
            if(!Enabled || target==0) return d;
            if(!BankPlanner.Finite(health) || health<=0 || !BankPlanner.Finite(damage) || damage<=0)
            { d.Reason="BURST_UNKNOWN_HEALTH_NATIVE"; return d; }
            lock(gate) {
                var e=Get(target,tick);
                if(e==null || Overflow) { d.Reason="BURST_CAPACITY_NATIVE"; return d; }
                e.LastSeen=tick;
                if(e.Budget==0) e.Budget=Math.Max(initialRounds*damage,Math.Ceiling(health/damage*margin)*damage);
                if(urgent) { e.PauseUntil=0; e.Budget=Math.Max(e.Budget,e.Spent+topupRounds*damage); d.Reason="BURST_URGENT_NATIVE"; }
                else if(e.PauseUntil>0 && tick>=e.PauseUntil) {
                    // Always release after a bounded pause, even if health did not fall.
                    // Health changes do not establish which gun hit; no kill promotion.
                    e.PauseUntil=0; e.Budget=e.Spent+Math.Max(topupRounds*damage,Math.Ceiling(health/damage*margin)*damage);
                    d.Reason="BURST_TOP_UP";
                }
                else if(e.PauseUntil==0 && e.Spent>=e.Budget) {
                    e.PauseUntil=tick+Math.Max(1,Math.Min(30,pauseTicks)); d.Reason="BURST_OBSERVE_PAUSE";
                } else d.Reason=e.PauseUntil>tick?"BURST_OBSERVE_PAUSE":"BURST_NATIVE_FIRE";
                d.Pause=!urgent && e.PauseUntil>tick;
                d.ObservedRounds=e.Rounds; d.SpentUnits=e.Spent; d.BudgetUnits=e.Budget; d.PauseUntil=e.PauseUntil;
                return d;
            }
        }
    }
}
