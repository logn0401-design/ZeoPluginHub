using System;
namespace ZeoCore
{
    internal static class FriendlyTrackPolicy
    {
        internal static bool NetworkFriendly(HudTrack t)
        {
            return t!=null&&t.Source==HudTrackSource.FleetFriendly&&t.Friendly&&t.SameSector&&t.HasPosition&&!t.IsDistress;
        }
        internal static bool FreshNetworkFriendly(HudTrack t,double staleSeconds)
        {
            return NetworkFriendly(t)&&!t.Stale&&t.AgeSeconds>=0&&t.AgeSeconds<=staleSeconds;
        }
        internal static bool RenderableSpectrum(double age){return age>=0&&age<=15;}
        internal static bool CanDeclutter(HudTrack t){return t!=null&&!t.Focused&&!NetworkFriendly(t);}
        internal static void MergeFreshness(HudTrack keeper,HudTrack other)
        {
            // A sensor observation must not make the retained network report look newer.
            if(NetworkFriendly(keeper)&&!NetworkFriendly(other))return;
            if(other.AgeSeconds<keeper.AgeSeconds)keeper.AgeSeconds=other.AgeSeconds;
            keeper.Stale=keeper.Stale&&other.Stale;
        }
    }
}
