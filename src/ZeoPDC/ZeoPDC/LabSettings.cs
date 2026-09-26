using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace ZeoPDC
{
    internal static class LabSettings
    {
        internal static bool ContainsId(string ids,long id) { return (ids??"").Split(',').Any(x=>x.Trim()==id.ToString(CultureInfo.InvariantCulture)); }
        internal static PdcConfig ProvenBest(PdcConfig c)
        {
            var n=Baseline(c);
            n.LabDistribution=1;
            n.FarRof=.50; n.MidFarRof=.50; n.MidRof=.72; n.CloseRof=.88; n.NearRof=1; n.FullEmergencyRof=1;
            n.EngagementRangeMeters=3000; n.ColdBoostHeatPercent=35; n.ColdRofBoost=.05;
            n.HeatThrottleStartPercent=60; n.SurvivalHeatOverrideRangeMeters=1500; n.SurvivalHeatOverrideTtiSeconds=1.6;
            return PdcConfig.Clamp(n);
        }
        internal static PdcConfig Baseline(PdcConfig c)
        {
            var n=JsonIo.FromBytes<PdcConfig>(JsonIo.ToBytes(c));
            n.LabHeatCurveEnabled=n.LabBurstEnabled=false;
            n.ManagedDefenseEnabled=false; n.LabEnabled=true; n.LabNativeOnly=true; n.LabRangeControl=false;
            n.LabSpreadDegrees=n.LabToleranceDegrees=-1; n.LabPrediction=n.LabDistribution=-1; n.LabAdvancedProjectileSolver=false;
            n.LabClosestGunIds=n.LabRequestGunIds="";
            n.ControlEnabled=true; n.NativeLeadEnabled=true; n.NativeThreatDecisions=false;
            n.PreemptiveLookEnabled=true; n.PreemptiveFireEnabled=false;
            n.BankRolesEnabled=n.BankRotationEnabled=false;
            n.ContinuousTelemetryEnabled=true; n.WeaponAwareEnabled=true;
            n.ExpectedInbound=32; n.HitRadiusMeters=150;
            return PdcConfig.Clamp(n);
        }
        internal static PdcConfig Apply(PdcConfig current,LabPatch[] patch,IEnumerable<long> gunIds)
        {
            if(patch==null || patch.Length==0 || patch.Length>64) throw new ArgumentException("Provide 1..64 settings.");
            var n=JsonIo.FromBytes<PdcConfig>(JsonIo.ToBytes(current)); var keys=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var protectedKeys=new[]{"ManagerPresetVersion","ManagedDefenseEnabled","LabEnabled","ControlEnabled","ExpectedInbound","HitRadiusMeters","StableIdAuthoritative","ContinuousTelemetryEnabled","ConfigVersion","TargetedRequests"};
            foreach(var item in patch) {
                var option=item==null?null:PdcSettingsCatalog.Find(item.Key);
                if(option==null || option.Appearance || option.ObservationOnly || protectedKeys.Contains(option.Key) || !keys.Add(option.Key))
                    throw new ArgumentException("Unknown, duplicate or protected setting: "+item?.Key);
                var f=option.Field;
                if(f.FieldType==typeof(bool)) { bool v; if(!bool.TryParse(item.Value,out v)) throw new ArgumentException("Boolean required: "+f.Name); f.SetValue(n,v); }
                else if(f.FieldType==typeof(double)) { double v; if(!double.TryParse(item.Value,NumberStyles.Float,CultureInfo.InvariantCulture,out v)||!BankPlanner.Finite(v)) throw new ArgumentException("Finite number required: "+f.Name); f.SetValue(n,v); }
                else if(f.FieldType==typeof(int)) { int v; if(!int.TryParse(item.Value,NumberStyles.Integer,CultureInfo.InvariantCulture,out v)) throw new ArgumentException("Integer required: "+f.Name); f.SetValue(n,v); }
                else if(f.FieldType==typeof(string) && (f.Name=="LabClosestGunIds" || f.Name=="LabRequestGunIds")) {
                    var selected=(item.Value??"").Split(',').Where(x=>!string.IsNullOrWhiteSpace(x)).Select(x=>long.Parse(x.Trim(),CultureInfo.InvariantCulture)).ToArray();
                    if(selected.Length>12 || selected.Distinct().Count()!=selected.Length || selected.Any(x=>!gunIds.Contains(x))) throw new ArgumentException("Gun IDs must belong to this battery.");
                    f.SetValue(n,string.Join(",",selected.Select(x=>x.ToString(CultureInfo.InvariantCulture))));
                } else throw new ArgumentException("Unsupported setting: "+f.Name);
            }
            PdcConfig.Clamp(n);
            if(n.LabAdvancedProjectileSolver && n.LabPrediction<2) throw new ArgumentException("Advanced projectile solver requires LabPrediction 2 or 3.");
            if(!n.PreemptiveLookEnabled || !n.NativeLeadEnabled) throw new ArgumentException("This lab retains pre-aim and native observation.");
            if(n.LabNativeOnly && (n.BankRolesEnabled || n.BankRotationEnabled)) throw new ArgumentException("Bank controls require LabNativeOnly=false.");
            if(n.BankRolesEnabled && !n.LabRangeControl) throw new ArgumentException("Bank range roles require LabRangeControl=true.");
            if(!string.IsNullOrEmpty(n.LabRequestGunIds) && !n.LabNativeOnly) throw new ArgumentException("Exact-ID trials require native fallback mode.");
            if((n.LabHeatCurveEnabled || n.LabBurstEnabled) && (!n.LabNativeOnly || !n.WeaponAwareEnabled || n.PreemptiveFireEnabled || !string.IsNullOrEmpty(n.LabRequestGunIds)))
                throw new ArgumentException("Heat/burst trials require weapon-aware native firing without outer fire or exact-ID requests.");
            return n;
        }
    }
}

