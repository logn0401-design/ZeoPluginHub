using System;
using System.Linq;
using VRageMath;

namespace ZeoPDC
{
    internal sealed partial class PdcEngine
    {
        private bool FreshProfile(Gun g)
        { return g!=null && g.ProfileFrame==frame && g.Profile!=null && g.Profile.Valid; }
        private bool ManagedRangeAllows(Gun g,Track t,PdcConfig c)
        {
            if(!c.WeaponAwareEnabled) return true;
            return FreshProfile(g) && t!=null && g.ScopeValid &&
                WeaponProfilePolicy.Eligible(g.Profile,Vector3D.Distance(g.ScopeOrigin,t.Pos)) &&
                Vector3D.Distance(g.ScopeOrigin,t.Pos)<=c.EngagementRangeMeters;
        }
        private void ResolveManagedRange(Gun g,PdcConfig c)
        {
            g.DesiredRange=g.BankRole.DesiredRange;
            g.RangeReason="LEGACY_RANGE";
            if(!c.WeaponAwareEnabled) return;
            if(!FreshProfile(g)) { g.RangeReason="PROFILE_UNKNOWN_NATIVE"; return; }
            if(g.DesiredRange<=g.Profile.MinimumRange && c.EngagementRangeMeters>g.Profile.MinimumRange)
            {
                g.DesiredRange=c.EngagementRangeMeters;
                g.BankRole.Role="MIN_RANGE_SUPPORT";
                g.BankRole.Reason="TIER_BELOW_NATIVE_MINIMUM";
            }
            g.DesiredRange=WeaponProfilePolicy.Range(g.Profile,g.DesiredRange);
            g.BankRole.DesiredRange=g.DesiredRange;
            g.RangeReason=g.DesiredRange<=g.Profile.MinimumRange?"CONFIG_BELOW_NATIVE_MINIMUM":"EFFECTIVE_WEAPON_ENVELOPE";
        }
        private void ResolveManagedCadence(Gun g)
        {
            var c=config(); g.ManagedRofWrite=false; g.CadenceReason="LEGACY_RANGE_ROF";
            if(!c.WeaponAwareEnabled) return;
            if(!FreshProfile(g)) { g.ManagedRofWrite=false; g.CadenceReason="PROFILE_UNKNOWN_NATIVE"; return; }
            if(g.OuterSelected && ((!c.LabEnabled && !c.ManagedDefenseEnabled) || c.PreemptiveFireEnabled))
            {
                // Optional burst cadence remains separately bounded and never
                // receives the slow-gun or survival boost.
                g.ManagedRofWrite=g.Profile.Adjustable && g.RofReadOk;
                if(g.ManagedRofWrite) g.Rof=Math.Max(g.Profile.MinimumRof,c.PreemptiveRof);
                g.CadenceReason="OPTIONAL_OUTER_BUDGET"; return;
            }
            Track t=tracks.FirstOrDefault(x=>x.Id==g.DecisionTrackId && !x.Resolved);
            double? shipRate=ShipCadenceRate(g,c);
            if(shipRate.HasValue)
                g.Rof=t==null?shipRate.Value:Math.Max(g.Rof,shipRate.Value);
            double turn=ShipTurnDegPerSec();
            double f=Math.Max(0,Math.Min(1,(turn-c.ManeuverSupportDegPerSec)/6));
            bool survival=t!=null && (t.Hull<=c.SurvivalHeatOverrideRangeMeters+c.ManeuverRangeBoostMeters*f ||
                t.Tti<=c.SurvivalHeatOverrideTtiSeconds+.20*f);
            if(c.LabEnabled && c.LabHeatCurveEnabled && labRecording && g.Profile.Adjustable && !g.Profile.PreserveCadence)
            {
                // The trial replaces the stepped range/heat law; it does not multiply
                // a second penalty onto it. WC's own native degradation still applies.
                bool fresh=g.HeatKnown && g.TelemetryFrame==frame;
                bool urgent=t==null || t.Hull<=c.LabHeatUrgentRangeMeters || t.Tti<=c.LabHeatUrgentTtiSeconds;
                g.Rof=HeatCurvePolicy.Rate(fresh?g.Heat:double.NaN,c.LabHeatStartPercent,c.LabHeatSlopePercent,g.Profile.MinimumRof,urgent);
                g.ManagedRofWrite=g.RofReadOk && fresh;
                g.CadenceReason=!fresh?"HEAT_CURVE_UNKNOWN_NATIVE":urgent?"HEAT_CURVE_URGENT_NATIVE":"HEAT_CURVE";
                return;
            }
            double? rate=WeaponProfilePolicy.Cadence(g.Profile,g.Rof,
                g.HeatKnown && g.TelemetryFrame==frame?g.Heat:double.NaN,survival,c.HeatThrottleStartPercent,out g.CadenceReason);
            g.ManagedRofWrite=rate.HasValue && g.RofReadOk;
            if(g.ManagedRofWrite) g.Rof=rate.Value;
            if(shipRate.HasValue)g.CadenceReason="SHIP_WITHIN_3KM / "+g.CadenceReason;
            if(rate.HasValue && !g.RofReadOk) g.CadenceReason="ROF_READBACK_UNAVAILABLE";
        }
        private void YieldUnknownProfile(Gun g)
        {
            ReleaseOuter(g,"PROFILE_NATIVE_YIELD");
            g.Allowed=false; g.TrackId=g.DecisionTrackId=0;
            g.ManagedRofWrite=false; g.CadenceReason="PROFILE_UNKNOWN_NATIVE";
            g.RangeReason="PROFILE_UNKNOWN_NATIVE";
            System.Threading.Volatile.Write(ref g.Context,null);
            bool ok=true;
            // Restore only settings still equal to our last successful request.
            // A manual/external edit transfers ownership away from us.
            if(g.RofOwned)
            {
                float current;
                if(!TryReadTerminal(g.Block,"Weapon ROF",out current)) ok=false;
                else if(Math.Abs(current-g.OwnedRof)>.006) g.RofOwned=false;
                else if(SetTerminal(g.Block,"Weapon ROF",(float)g.SavedRof))
                {
                    float restored;
                    if(TryReadTerminal(g.Block,"Weapon ROF",out restored) && Math.Abs(restored-g.SavedRof)<=.006) g.RofOwned=false;
                    else ok=false;
                }
                else ok=false;
            }
            if(g.RangeOwned)
            {
                double current;
                if(!TrackingRangePolicy.TryObserved(wc.GetMaxWeaponRange(g.Entity,g.Part),out current)) ok=false;
                else if(Math.Abs(current-g.OwnedRange)>5) g.RangeOwned=false;
                else
                {
                    bool wrote=wc.SetTrackingRange(g.Entity,(float)g.SavedRange);
                    double restored;
                    if(wrote && TrackingRangePolicy.TryObserved(wc.GetMaxWeaponRange(g.Entity,g.Part),out restored) && Math.Abs(restored-g.SavedRange)<=5) g.RangeOwned=false;
                    else ok=false;
                }
            }
            if(g.DirectConfigured)
            {
                bool native=SetTerminal(g.Block,"WC_Shoot Mode",0L);
                native &= SetTerminal(g.Block,"WC_Shoot",true);
                native &= wc.ToggleWeaponFire(g.Entity,g.Part,true);
                if(native) g.DirectConfigured=false;
                ok &= native;
            }
            g.ProfileYielded=ok;
            g.CommandResult=ok?"PROFILE_NATIVE_YIELD":"PROFILE_RESTORE_PENDING";
        }
    }
}

