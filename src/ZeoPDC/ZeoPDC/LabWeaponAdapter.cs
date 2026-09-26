using System;
using System.Collections;
using System.Reflection;

namespace ZeoPDC
{
    // Host-only experiment. Clone the per-weapon system and constants before any
    // edits: mutating a shared WeaponSystem would also change unrelated weapons.
    internal sealed class LabWeaponAdapter
    {
        const BindingFlags F=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        internal object Weapon, Original, Owned;
        double originalTolerance, ownedTolerance;
        public string Status="NATIVE";
        public double? SpreadDegrees, ToleranceDegrees;
        public string Prediction;
        public bool? AdvancedSolver;
        static object Read(object o,string n) { return OuterAimAdapter.Read(o,n); }
        static FieldInfo Field(object o,string n)
        { var f=o.GetType().GetField(n,F); if(f==null || f.IsStatic) throw new MissingFieldException(n); return f; }
        static object Clone(object o) { return typeof(object).GetMethod("MemberwiseClone",F).Invoke(o,null); }
        static void Set(object o,string n,object value)
        {
            var f=Field(o,n);
            object converted=f.FieldType.IsEnum?Enum.ToObject(f.FieldType,value):Convert.ChangeType(value,f.FieldType,System.Globalization.CultureInfo.InvariantCulture);
            f.SetValue(o,converted);
            if(!Equals(f.GetValue(o),converted)) throw new InvalidOperationException("Readback "+n);
        }
        internal static string HostStatus(object component,bool allowHosted=false)
        {
            if(component==null) return "NO_COMPONENT";
            try {
                var type=component.GetType().Assembly.GetType("CoreSystems.Session",true);
                var session=type.GetField("I",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).GetValue(null);
                if(!(bool)Read(session,"IsServer")) return "REMOTE_CLIENT_UNSUPPORTED";
                if((bool)Read(session,"MpActive")) {
                    if(!allowHosted)return "OFFLINE_WORLD_REQUIRED";
                    if((bool)Read(session,"DedicatedServer"))return "DEDICATED_SERVER_UNSUPPORTED";
                    return "LOCAL_HOST_OK";
                }
                return "LOCAL_HOST_OK";
            } catch { return "HOST_SCHEMA_UNAVAILABLE"; }
        }
        internal static bool LocalHost(object component) { return HostStatus(component)=="LOCAL_HOST_OK"; }
        internal static object WeaponPart(object component,int part)
        {
            if(component==null || Read(component,"Platform.State").ToString()!="Ready" || (bool)Read(component,"UserControlled")) return null;
            var list=Read(component,"Platform.Weapons") as IList;
            return list!=null && list.Count==1 && part==0?list[0]:null;
        }
        public bool Apply(object component,int part,PdcConfig c,long gunId=0,bool productionHost=false)
        {
            try {
                // Hosted production gets only the retained distribution change; lab experiments stay offline.
                if(productionHost&&!HostedDistributionOnly(c)){Restore();Status="HOSTED_CONFIG_REJECTED";return false;}
                string host=HostStatus(component,productionHost);
                if(host!="LOCAL_HOST_OK") { Restore(); Status=host; return false; }
                var weapon=WeaponPart(component,part);
                if(weapon==null) { Restore(); Status="MANUAL_OR_UNSUPPORTED"; return false; }
                return ApplyWeapon(weapon,c,gunId);
            } catch { Restore(); Status="UNSUPPORTED_SCHEMA"; return false; }
        }
        internal static bool HostedDistributionOnly(PdcConfig c)
        {return c!=null&&c.LabDistribution==1&&c.LabSpreadDegrees<0&&c.LabToleranceDegrees<0&&c.LabPrediction<0&&!c.LabAdvancedProjectileSolver&&string.IsNullOrWhiteSpace(c.LabClosestGunIds)&&string.IsNullOrWhiteSpace(c.LabRequestGunIds);}
        internal bool ApplyWeapon(object weapon,PdcConfig c,long gunId=0)
        {
            try {
                if(Owned!=null && (!ReferenceEquals(weapon,Weapon) || !ReferenceEquals(Read(weapon,"System"),Owned)))
                { Restore(); Status="FOREIGN_SYSTEM_CHANGE"; return false; }
                bool closest=LabSettings.ContainsId(c.LabClosestGunIds,gunId), exact=LabSettings.ContainsId(c.LabRequestGunIds,gunId);
                bool modify=c.LabSpreadDegrees>=0 || c.LabToleranceDegrees>=0 || c.LabPrediction>=0 || c.LabAdvancedProjectileSolver || closest || exact || c.LabDistribution>=0;
                if(!modify) { if(!Restore()) return false; Observe(weapon); return true; }
                if(Owned==null) {
                    Weapon=weapon; Original=Read(weapon,"System"); originalTolerance=Convert.ToDouble(Read(weapon,"AimingTolerance"));
                    Owned=Clone(Original);
                    var constants=Clone(Read(Original,"WConst"));
                    Field(Owned,"WConst").SetValue(Owned,constants);
                    if(ReferenceEquals(Read(Owned,"WConst"),Read(Original,"WConst"))) throw new InvalidOperationException("Shared constants");
                }
                var wc=Read(Owned,"WConst"); var originalWc=Read(Original,"WConst");
                Set(wc,"DeviateShotAngleRads",c.LabSpreadDegrees>=0?c.LabSpreadDegrees*Math.PI/180:Convert.ToDouble(Read(originalWc,"DeviateShotAngleRads")));
                Set(wc,"AimingToleranceRads",c.LabToleranceDegrees>=0?c.LabToleranceDegrees*Math.PI/180:Convert.ToDouble(Read(originalWc,"AimingToleranceRads")));
                Set(Owned,"Prediction",c.LabPrediction>=0?c.LabPrediction:Convert.ToInt32(Read(Original,"Prediction")));
                Set(Owned,"UseLimitlessPDSolver",c.LabAdvancedProjectileSolver || (bool)Read(Original,"UseLimitlessPDSolver"));
                // These weapons hide the corresponding terminal controls. Use the
                // cloned definition's priority when explicitly selected for a trial.
                Set(Owned,"ClosestFirst",exact?false:closest?true:(bool)Read(Original,"ClosestFirst"));
                Set(Owned,"AllowSwitchTargetPriority",closest||exact?false:(bool)Read(Original,"AllowSwitchTargetPriority"));
                Set(Owned,"AllowFireDistribution",c.LabDistribution>=0?c.LabDistribution==1:(bool)Read(Original,"AllowFireDistribution"));
                ownedTolerance=c.LabToleranceDegrees>=0?Math.Cos(c.LabToleranceDegrees*Math.PI/180):originalTolerance;
                Field(weapon,"System").SetValue(weapon,Owned); Set(weapon,"AimingTolerance",ownedTolerance);
                if(!ReferenceEquals(Read(weapon,"System"),Owned)) throw new InvalidOperationException("System readback");
                Observe(weapon); Status="APPLIED_INSTANCE_CLONE_UNVALIDATED_LIVE"; return true;
            } catch { Restore(); Status="APPLY_FAILED"; return false; }
        }
        void Observe(object weapon)
        {
            var sys=Read(weapon,"System");
            SpreadDegrees=Convert.ToDouble(Read(sys,"WConst.DeviateShotAngleRads"))*180/Math.PI;
            ToleranceDegrees=Convert.ToDouble(Read(sys,"WConst.AimingToleranceRads"))*180/Math.PI;
            Prediction=Read(sys,"Prediction").ToString(); AdvancedSolver=(bool)Read(sys,"UseLimitlessPDSolver");
        }
        public bool Restore()
        {
            if(Owned==null) { Status="NATIVE"; return true; }
            try {
                if(ReferenceEquals(Read(Weapon,"System"),Owned)) {
                    Field(Weapon,"System").SetValue(Weapon,Original);
                    if(ReferenceEquals(Read(Weapon,"System"),Owned)) { Status="RESTORE_PENDING"; return false; }
                }
                // Do not overwrite another controller's cached tolerance.
                if(Math.Abs(Convert.ToDouble(Read(Weapon,"AimingTolerance"))-ownedTolerance)<1e-10)
                    Set(Weapon,"AimingTolerance",originalTolerance);
                Observe(Weapon); Weapon=Original=Owned=null; Status="RESTORED"; return true;
            } catch { Status="RESTORE_PENDING"; return false; }
        }
    }
}

