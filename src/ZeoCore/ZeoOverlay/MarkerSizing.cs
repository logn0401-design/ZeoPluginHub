using System;
namespace ZeoOverlay
{
    internal static class MarkerSizing
    {
        internal static double Scale(double basis,double distance,double cap,double focus=1,double offscreen=1)
        {
            double t=Math.Max(0,Math.Min(1,(distance-2000)/58000));t=t*t*(3-2*t);
            return Math.Min(cap,basis*(.85+.8*t)*focus*offscreen);
        }
        internal static bool Hostile(string relation){return string.Equals(relation,"hostile",StringComparison.OrdinalIgnoreCase)||string.Equals(relation,"enemy",StringComparison.OrdinalIgnoreCase);}
        // Local sensors retain > <. Shared hostile reports use a triangle.
        // Attack designation is a color/label state, never an icon replacement.
        internal static int Shape(int source,bool friendly,string relation,bool attack)
        {
            if(friendly||source==2)return 3;
            if(source==3||source==4)return Hostile(relation)?1:source==4?0:2;
            return 4;
        }
    }
}
