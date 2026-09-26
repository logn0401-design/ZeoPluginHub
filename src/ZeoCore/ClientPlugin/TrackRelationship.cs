using System;
namespace ZeoCore
{
    internal static class TrackRelationship
    {
        private static bool Known(string s){return s=="friendly"||s=="hostile"||s=="neutral";}
        internal static void Merge(HudTrack keeper,HudTrack incoming)
        {
            string a=(keeper.Relation??"unknown").ToLowerInvariant(),b=(incoming.Relation??"unknown").ToLowerInvariant();
            double aAge=double.IsNaN(keeper.RelationAgeSeconds)?keeper.AgeSeconds:keeper.RelationAgeSeconds;
            double bAge=double.IsNaN(incoming.RelationAgeSeconds)?incoming.AgeSeconds:incoming.RelationAgeSeconds;
            if(Known(a))keeper.RelationAgeSeconds=aAge;
            // A raw unknown detection cannot erase a fresh confirmed classification.
            // Conversely, expired shared hostility must not override a new neutral/friendly observation.
            if(FriendlyTrackPolicy.NetworkFriendly(keeper))return;
            if(Known(b)&&!incoming.Stale && (!Known(a)||keeper.Stale||bAge<aAge||
                (bAge==aAge&&incoming.Source==HudTrackSource.WeaponCore))){keeper.Relation=b;keeper.Friendly=b=="friendly";keeper.RelationAgeSeconds=bAge;}
        }
    }
}
