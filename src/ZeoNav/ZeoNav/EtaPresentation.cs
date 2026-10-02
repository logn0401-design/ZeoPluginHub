using System;

namespace ZeoNav
{
    // Presentation only: never feeds the speed, thrust or stopping controller.
    internal sealed class EtaPresentation
    {
        private double shown=-1;
        private DateTime last;
        internal void Reset() { shown=-1; last=DateTime.MinValue; }
        internal double Observe(DateTime now,double plannedSeconds)
        {
            if(!SignalBudget.Finite(plannedSeconds)||plannedSeconds<0)
            { Reset(); return -1; }
            if(shown<0||last==DateTime.MinValue||now<last)
            { shown=plannedSeconds; last=now; return shown; }
            double elapsed=Math.Min(10,(now-last).TotalSeconds);
            double countdown=Math.Max(0,shown-elapsed);
            double difference=plannedSeconds-countdown;
            // Show a real delay promptly; smooth small noise and optimistic gains.
            bool materialDelay=difference>Math.Max(5,countdown*.03);
            bool materialGain=difference< -Math.Max(10,countdown*.10);
            shown=materialDelay||materialGain?plannedSeconds:Math.Max(0,countdown+difference*.18);
            last=now;
            return shown;
        }
    }
}
