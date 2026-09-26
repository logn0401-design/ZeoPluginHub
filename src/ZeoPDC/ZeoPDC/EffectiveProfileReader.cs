using System;
using System.Collections;
using System.Globalization;

namespace ZeoPDC
{
    // Reads constants AFTER WeaponCore has applied overrides. No setters or game commands.
    internal static class EffectiveProfileReader
    {
        static object R(object o,string p) { return OuterAimAdapter.Read(o,p); }
        static double D(object o,string p) { return Convert.ToDouble(R(o,p),CultureInfo.InvariantCulture); }
        static int I(object o,string p) { return Convert.ToInt32(R(o,p),CultureInfo.InvariantCulture); }
        static bool B(object o,string p) { return (bool)R(o,p); }
        public static WeaponProfile Read(object component,int part)
        {
            var p=new WeaponProfile();
            try
            {
                if(component==null) { p.Status="NO_COMPONENT"; return p; }
                if(R(component,"Platform.State").ToString()!="Ready") { p.Status="NOT_READY"; return p; }
                var parts=R(component,"Platform.Weapons") as IList;
                // ROF/range sliders are component-wide. Independent decisions for
                // multiple parts would compete and cannot safely be applied here.
                if(parts==null || parts.Count!=1 || part!=0) { p.Status="MULTIPART_NATIVE"; return p; }
                object w=parts[part], sys=R(w,"System"), con=R(sys,"WConst"), ammo=R(w,"ActiveAmmoDef.AmmoDef"), ac=R(ammo,"Const");
                p.Subtype=R(component,"SubtypeName").ToString(); p.Ammo=R(ammo,"AmmoRound").ToString();
                p.Identity=component.GetType().Assembly.ManifestModule.ModuleVersionId.ToString("N")+"/"+p.Subtype+"/"+R(sys,"WeaponIdHash")+"/"+R(ac,"AmmoIdxPos");
                p.NativeRpm=D(con,"RateOfFire"); p.EffectiveRpm=D(w,"RateOfFire");
                p.MinimumRof=D(con,"MinRateOfFire"); p.Adjustable=B(sys,"Values.HardPoint.Ui.RateOfFire");
                int barrels=I(sys,"BarrelsPerShot"), trajectories=I(sys,"Values.HardPoint.Loading.TrajectilesPerBarrel");
                p.ProjectilesPerEvent=checked(barrels*trajectories);
                p.HeatPerEvent=D(con,"HeatPerShot")*D(ac,"HeatModifier")*barrels;
                p.CoolingPerSecond=D(con,"HeatSinkRate"); p.MaximumHeat=D(sys,"MaxHeat");
                p.MinimumRange=D(con,"MinTargetDistance"); p.MaximumRange=Math.Min(D(con,"MaxTargetDistance"),D(ac,"MaxTrajectory"));
                p.HealthDamage=D(ac,"HealthHitModifier"); p.ShotLifeSeconds=D(ac,"MaxLifeTime")/60;
                p.ShotSpeed=D(ac,"DesiredProjectileSpeed"); p.StartupSeconds=D(sys,"Values.HardPoint.Loading.DelayUntilFire")/60;
                p.BurstCount=I(sys,"Values.HardPoint.Loading.ShotsInBurst");
                p.SimpleBallistic=R(ammo,"Trajectory.Guidance").ToString()=="None" && !B(ac,"IsBeamWeapon") && I(ac,"FragmentId")<0 && D(ac,"AccelInMetersPerSec")==0;
                p.Status="OK";
                if(!p.Valid) p.Status="INVALID_EFFECTIVE_PROFILE";
                else p.Seal();
            }
            catch { p.Status="UNSUPPORTED_PROFILE_SCHEMA"; }
            return p;
        }
    }
}
