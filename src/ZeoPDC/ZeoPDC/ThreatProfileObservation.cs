using System;
using System.Globalization;
using System.Runtime.Serialization;

namespace ZeoPDC
{
    [DataContract] internal sealed class ThreatProfileObservation
    {
        [DataMember] public string status="UNAVAILABLE", identity, ammo_name;
        [DataMember] public double? initial_health, life_s, base_speed_mps, trajectory_cap_m;
        [DataMember] public string speed_kind="BASE_DEFINITION_NOT_CURRENT_STAGE_SPEED";
        // Resolve the projectile's own ammo definition, never the launcher's
        // currently selected round or a name-only static catalog match.
        internal static ThreatProfileObservation Read(object details)
        {
            var r=new ThreatProfileObservation();
            try
            {
                object ammo=OuterAimAdapter.Read(details,"AmmoDef"), c=OuterAimAdapter.Read(ammo,"Const");
                object w=OuterAimAdapter.Read(details,"Weapon");
                r.identity=details.GetType().Assembly.ManifestModule.ModuleVersionId.ToString("N")+"/"+
                    OuterAimAdapter.Read(w,"Comp.SubtypeName")+"/"+OuterAimAdapter.Read(w,"System.WeaponIdHash")+"/"+
                    OuterAimAdapter.Read(c,"AmmoIdxPos");
                r.ammo_name=OuterAimAdapter.Read(ammo,"AmmoRound").ToString();
                r.initial_health=N(c,"Health"); r.life_s=N(c,"MaxLifeTime")/60;
                r.base_speed_mps=N(c,"DesiredProjectileSpeed"); r.trajectory_cap_m=N(c,"MaxTrajectory");
                r.status="OK";
            }
            catch { r=new ThreatProfileObservation {status="UNSUPPORTED_THREAT_PROFILE"}; }
            return r;
        }
        static double N(object o,string name)
        {
            double n=Convert.ToDouble(OuterAimAdapter.Read(o,name),CultureInfo.InvariantCulture);
            if(!BankPlanner.Finite(n) || n<0) throw new InvalidOperationException(name);
            return n;
        }
    }
}
