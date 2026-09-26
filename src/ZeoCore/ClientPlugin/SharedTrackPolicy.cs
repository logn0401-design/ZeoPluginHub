using System;
using System.Collections.Generic;
using System.Linq;
namespace ZeoCore
{
    internal static class SharedTrackPolicy
    {
        internal static void Apply(List<HudTrack> tracks,int maximum,double rangeKm)
        {
            maximum=Math.Max(0,Math.Min(192,maximum));
            if(double.IsNaN(rangeKm)||double.IsInfinity(rangeKm)||rangeKm<0)rangeKm=0;
            var shared=tracks.Where(t=>t!=null&&!t.IsDistress&&
                (t.Source==HudTrackSource.FleetContact||t.Source==HudTrackSource.FleetSignal)).ToList();
            var allowed=new HashSet<HudTrack>(shared.Where(t=>!double.IsNaN(t.Distance)&&!double.IsInfinity(t.Distance)&&t.Distance>=0&&
                (rangeKm==0||t.Distance<=rangeKm*1000)).OrderBy(t=>t.Distance).ThenBy(t=>t.Key,StringComparer.Ordinal).Take(maximum));
            var rejected=new HashSet<HudTrack>(shared.Where(t=>!allowed.Contains(t)));
            tracks.RemoveAll(t=>rejected.Contains(t));
        }
    }
}
