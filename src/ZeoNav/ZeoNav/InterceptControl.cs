using System;
using VRageMath;

namespace ZeoNav
{
    // Entry/exit hysteresis and a short acquisition interval prevent moving
    // headings from repeatedly switching between main drives and RCS.
    internal sealed class InterceptBurnGate
    {
        private double eligibleSince=double.NaN,lastTime=double.NaN,ratio;
        private bool acquired;
        internal void Reset(){eligibleSince=lastTime=double.NaN;ratio=0;acquired=false;}
        internal double Observe(double angle,double headingRate,double now)
        {
            if(!SignalBudget.Finite(angle)||!SignalBudget.Finite(headingRate)||!SignalBudget.Finite(now)||angle<0||headingRate<0)
            {Reset();return 0;}
            double projected=angle+headingRate*.5;
            double dt=double.IsNaN(lastTime)?0:Math.Max(0,Math.Min(.1,now-lastTime));
            if(!double.IsNaN(lastTime)&&now<lastTime)Reset();
            lastTime=now;
            if(projected>=8){acquired=false;eligibleSince=double.NaN;ratio=0;return 0;}
            if(!acquired)
            {
                if(projected>4){eligibleSince=double.NaN;ratio=0;return 0;}
                if(double.IsNaN(eligibleSince))eligibleSince=now;
                if(now-eligibleSince<.12)return 0;
                acquired=true;
            }
            double x=Math.Max(0,Math.Min(1,projected/8));
            double safe=1-x*x*(3-2*x);
            // Cut unsafe thrust immediately; only increases are ramped.
            ratio=Math.Min(safe,ratio+dt*4);
            return ratio;
        }
        internal static double HeadingRate(Vector3D angular,Vector3D forward)
        {
            if(!WorldMotion.Finite(angular)||!WorldMotion.Finite(forward)||forward.LengthSquared()<.5)return double.PositiveInfinity;
            forward.Normalize();
            return (angular-forward*Vector3D.Dot(angular,forward)).Length()*180/Math.PI;
        }
    }

    internal static class InterceptEnvelope
    {
        internal static double Closing(double distance,double standOff,double separation,double main,double rcs,double turn,bool rcsOnly)
        {
            double terminal=RendezvousMath.ClosingLimit(distance-separation,rcs,turn);
            if(rcsOnly)return terminal;
            // Main braking must finish outside the RCS-only terminal region.
            double cruise=RendezvousMath.ClosingLimit(distance-standOff-500,main,turn);
            return Math.Max(terminal,cruise);
        }
        internal static bool ReuseBaseline(SignalBudget fresh,SignalBudget prior)
        {
            if(fresh==null||prior==null||!prior.Ready||prior.TargetKm<=0||
                !SignalBudget.Finite(prior.FeedbackScale)||prior.FeedbackScale<1||
                !SignalBudget.Finite(prior.SphericalBaseSquared)||prior.SphericalBaseSquared<0||
                !SignalBudget.Finite(prior.DirectionalBaseSquared)||prior.DirectionalBaseSquared<0||
                fresh.Buckets.Count!=prior.Buckets.Count)return false;
            foreach(var pair in fresh.Buckets)
            {
                SignalBudget.Bucket old;
                if(!prior.Buckets.TryGetValue(pair.Key,out old)||old.Directional!=pair.Value.Directional)return false;
                for(int i=0;i<6;i++)if(old.Cost[i]!=pair.Value.Cost[i]||old.Reserve[i]!=pair.Value.Reserve[i])return false;
            }
            fresh.SphericalBaseSquared=prior.SphericalBaseSquared;
            fresh.DirectionalBaseSquared=prior.DirectionalBaseSquared;
            fresh.FeedbackScale=prior.FeedbackScale;
            return true;
        }
    }
}
