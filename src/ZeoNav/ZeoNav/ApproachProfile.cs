using System;
namespace ZeoNav
{
    internal static class ApproachProfile
    {
        internal static double Cruise(NavConfig c)
        { double n=c==null?125:c.MaxDriveSigKm;return SignalBudget.Finite(n)?Math.Max(5,Math.Min(750,n)):5; }
        internal static double Arrival(NavConfig c)
        { return c!=null&&c.ApproachSigEnabled?Math.Min(Cruise(c),SignalBudget.Finite(c.ApproachSigKm)?Math.Max(5,Math.Min(750,c.ApproachSigKm)):5):Cruise(c); }
        internal static double Departure(NavConfig c)
        { return c.DepartureSigEnabled?Math.Min(Cruise(c),SignalBudget.Finite(c.DepartureSigKm)?Math.Max(5,Math.Min(750,c.DepartureSigKm)):5):Cruise(c); }
        internal static double Effective(NavConfig c,bool departure,bool approach)
        { return Math.Min(departure?Departure(c):Cruise(c),approach?Arrival(c):Cruise(c)); }
        internal static bool FinalPhase(NavPhase p)
        { return p==NavPhase.PRE_FLIP||p==NavPhase.FLIP||p==NavPhase.BRAKE||p==NavPhase.TERMINAL_SETTLE; }
        internal static bool Activate(NavConfig c,bool latched,double gpsDistance,NavPhase p)
        { return c.ApproachSigEnabled&&(latched||gpsDistance<=c.ApproachDistanceKm*1000||FinalPhase(p)); }
        internal static double Ratio(SignalBudget budget,double ceiling)
        {
            if(budget==null||!budget.Ready)return 0;
            double previous=budget.TargetKm;
            try{budget.TargetKm=ceiling;return budget.Limit(0,1,new double[6]);}
            finally{budget.TargetKm=previous;}
        }
    }
}
