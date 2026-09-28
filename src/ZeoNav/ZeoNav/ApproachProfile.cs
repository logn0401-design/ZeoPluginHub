using System;
namespace ZeoNav
{
    internal static class ApproachProfile
    {
        // Zero means unrestricted; combining limits must not turn it into a zero budget.
        internal static double Normalize(double n) { return SignalBudget.Finite(n)&&n>=0?n:0; }
        internal static double Restrict(double a,double b) { return a==0?b:b==0?a:Math.Min(a,b); }
        internal static double Cruise(NavConfig c) { return c==null?0:Normalize(c.MaxDriveSigKm); }
        internal static double Arrival(NavConfig c)
        { return c!=null&&c.ApproachSigEnabled?Restrict(Cruise(c),Normalize(c.ApproachSigKm)):Cruise(c); }
        internal static double Departure(NavConfig c)
        { return c!=null&&c.DepartureSigEnabled?Restrict(Cruise(c),Normalize(c.DepartureSigKm)):Cruise(c); }
        internal static double Effective(NavConfig c,bool departure,bool approach)
        { return Restrict(departure?Departure(c):Cruise(c),approach?Arrival(c):Cruise(c)); }
        private static double Blend(double quiet,double cruise,double progress,double fullRange)
        {
            if(quiet==cruise)return cruise;
            progress=Math.Max(0,Math.Min(1,progress));
            if(progress==0)return quiet;
            if(progress==1)return cruise;
            // Use modeled full-bank output for an unrestricted endpoint inside a
            // finite zone. Outside the zone the limit is exactly zero/unrestricted.
            double high=cruise==0?Math.Max(quiet,fullRange):cruise;
            double smooth=progress*progress*(3-2*progress);
            return quiet*(1-smooth)+high*smooth;
        }
        internal static double At(NavConfig c,double fromStart,double toGps,double buffer,double fullRange)
        {
            double cruise=Cruise(c),limit=cruise;
            if(c==null)return limit;
            if(c.DepartureSigEnabled&&c.DepartureSigKm>0)
                limit=Restrict(limit,Blend(Departure(c),cruise,fromStart/Math.Max(1,c.DepartureDistanceKm*1000),fullRange));
            if(c.ApproachSigEnabled&&c.ApproachSigKm>0)
                limit=Restrict(limit,Blend(Arrival(c),cruise,(toGps-buffer)/Math.Max(1,c.ApproachDistanceKm*1000-buffer),fullRange));
            return limit;
        }
        internal static bool NeedsModel(NavConfig c)
        { return Cruise(c)>0||(c.ApproachSigEnabled&&c.ApproachSigKm>0)||(c.DepartureSigEnabled&&c.DepartureSigKm>0); }
        internal static double FullRange(SignalBudget budget)
        { return budget==null?0:Math.Sqrt(budget.PredictedSquared(new double[]{1,1,1,1,1,1}))/SignalBudget.RangeMargin; }
        internal static bool FinalPhase(NavPhase p)
        { return p==NavPhase.PRE_FLIP||p==NavPhase.FLIP||p==NavPhase.BRAKE||p==NavPhase.TERMINAL_SETTLE; }
        internal static bool Activate(NavConfig c,bool latched,double gpsDistance,NavPhase p)
        { return c.ApproachSigEnabled&&(latched||gpsDistance<=c.ApproachDistanceKm*1000); }
        internal static double Ratio(SignalBudget budget,double ceiling)
        {
            if(ceiling==0)return 1;
            if(budget==null||!budget.Ready)return 0;
            double previous=budget.TargetKm;
            try{budget.TargetKm=ceiling;return budget.Limit(0,1,new double[6]);}
            finally{budget.TargetKm=previous;}
        }
    }
}
