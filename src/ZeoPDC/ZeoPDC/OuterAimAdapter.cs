using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using VRage.Game.Entity;
using VRageMath;

namespace ZeoPDC
{
    // Narrow compatibility adapter for authoritative local WC hosts. No fabricated target,
    // range-cap modification, direct matrix write or position-directed projectile.
    internal sealed class OuterAimAdapter
    {
        const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        Type componentType;
        FieldInfo sessionInstance, sessionTick, sessionServer, sessionMp, sessionDedicated, platform, platformState, weapons,
            target, hasTarget, changeTick, userControlled, returning, azTick, elTick, aiOnly, turret;
        MethodInfo getComponent, look, aim, pivot, requestCycle, sendHome;
        object caller, once, manualSignal;
        public string Status { get; private set; } = "UNBOUND";
        internal sealed class Lease
        {
            internal object Weapon, Component;
            internal bool OriginalHome;
            internal uint LastTick = uint.MaxValue;
            internal uint OriginalChangeTick, OwnedChangeTick;
            public bool Owned { get { return Weapon != null; } }
            public string State = "UNOWNED";
        }
        static FieldInfo Field(Type t, string n) { var f=t.GetField(n,F); if(f==null) throw new MissingFieldException(t.FullName,n); return f; }
        internal static object Read(object o, string path)
        {
            foreach (var name in path.Split('.'))
            {
                if (o==null) throw new InvalidOperationException(path);
                var t=o.GetType(); var f=t.GetField(name,F);
                o=f!=null ? f.GetValue(o) : t.GetProperty(name,F)?.GetValue(o,null);
            }
            if (o==null) throw new MissingMemberException(path);
            return o;
        }
        public void Bind(Assembly a)
        {
            Status="UNSUPPORTED_SCHEMA"; getComponent=null;
            try
            {
                componentType=a.GetType("CoreSystems.Support.CoreComponent",true);
                var w=a.GetType("CoreSystems.Platform.Weapon",true);
                var math=a.GetType("CoreSystems.Support.MathFuncs",true);
                var session=a.GetType("CoreSystems.Session",true);
                sessionInstance=Field(session,"I"); sessionTick=Field(session,"Tick");
                sessionServer=Field(session,"IsServer"); sessionMp=Field(session,"MpActive");sessionDedicated=Field(session,"DedicatedServer");
                platform=Field(componentType,"Platform"); platformState=Field(platform.FieldType,"State"); weapons=Field(platform.FieldType,"Weapons");
                userControlled=Field(componentType,"UserControlled"); target=Field(w,"Target"); hasTarget=Field(target.FieldType,"HasTarget");
                changeTick=Field(target.FieldType,"ChangeTick");
                returning=Field(w,"ReturingHome"); azTick=Field(w,"AzimuthTick"); elTick=Field(w,"ElevationTick");
                aiOnly=Field(w,"AiOnlyWeapon"); turret=Field(w,"TurretController");
                var debug=math.GetNestedType("DebugCaller",BindingFlags.Public|BindingFlags.NonPublic);
                caller=Enum.Parse(debug,"TrackingTarget");
                look=math.GetMethod("WeaponLookAt",F,null,new[]{w,typeof(Vector3D).MakeByRefType(),typeof(double),typeof(bool),typeof(bool),debug,typeof(bool).MakeByRefType()},null);
                aim=w.GetMethod("AimBarrel",F,null,Type.EmptyTypes,null); pivot=w.GetMethod("UpdatePivotPos",F,null,Type.EmptyTypes,null);
                sendHome=w.GetMethod("SendTurretHome",F,null,new[]{typeof(object)},null);
                if(!typeof(IList).IsAssignableFrom(Field(session,"HomingWeapons").FieldType)) return;
                var manager=w.GetNestedType("ShootManager",BindingFlags.Public|BindingFlags.NonPublic);
                var request=manager.GetNestedType("RequestType",BindingFlags.Public|BindingFlags.NonPublic);
                var signal=manager.GetNestedType("Signals",BindingFlags.Public|BindingFlags.NonPublic);
                once=Enum.Parse(request,"Once"); manualSignal=Enum.Parse(signal,"Manual");
                requestCycle=manager.GetMethod("RequestShootSync",F,null,new[]{typeof(long),request,signal},null);
                if(look==null || look.ReturnType!=typeof(bool) || aim==null || pivot==null || sendHome==null ||
                    requestCycle==null || requestCycle.ReturnType!=typeof(bool) ||
                    returning.FieldType!=typeof(bool) || hasTarget.FieldType!=typeof(bool) || changeTick.FieldType!=typeof(uint) || azTick.FieldType!=typeof(uint) ||
                    elTick.FieldType!=typeof(uint) || sessionTick.FieldType!=typeof(uint) || !typeof(IList).IsAssignableFrom(weapons.FieldType)) return;
                Status="SCHEMA_READY";
            }
            catch { Status="UNSUPPORTED_SCHEMA"; }
        }
        object Component(MyEntity entity)
        {
            if(entity==null || entity.MarkedForClose) return null;
            var c=entity.Components;
            if(getComponent==null) getComponent=c.GetType().GetMethods(F).First(m=>m.Name=="Get" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length==1 && m.GetParameters().Length==0).MakeGenericMethod(componentType);
            return getComponent.Invoke(c,null);
        }
        public string Step(MyEntity entity, int part, Lease lease, Vector3D direction, double distanceSquared)
        {
            if(Status!="SCHEMA_READY") return lease.State=Status;
            try { return StepComponent(Component(entity),part,lease,direction,distanceSquared); }
            catch { Release(lease); return lease.State="AIM_ERROR"; }
        }
        internal string StepComponent(object comp, int part, Lease lease, Vector3D direction, double distanceSquared)
        {
            try
            {
                object session=sessionInstance.GetValue(null);
                if(session==null || !(bool)sessionServer.GetValue(session) || (bool)sessionDedicated.GetValue(session)) return Yield(lease,"LOCAL_SERVER_ONLY");
                if(comp==null || (bool)userControlled.GetValue(comp)) return Yield(lease,"MANUAL_OR_MISSING");
                object p=platform.GetValue(comp);
                if(p==null || platformState.GetValue(p).ToString()!="Ready") return Yield(lease,"NOT_READY");
                var list=weapons.GetValue(p) as IList;
                // FireWeaponOnceBase is component-wide even when given a part index.
                if(list==null || list.Count!=1 || part!=0) return Yield(lease,"SINGLE_PART_REQUIRED");
                object w=list[part];
                if((bool)hasTarget.GetValue(target.GetValue(w))) return Yield(lease,"NATIVE_TARGET_YIELD");
                if(!(bool)aiOnly.GetValue(w) || !(bool)turret.GetValue(w)) return Yield(lease,"CUSTOM_TURRET_REQUIRED");
                if(!BankPlanner.Finite(direction) || direction.LengthSquared()<.5 || !BankPlanner.Finite(distanceSquared) || distanceSquared<=0) return Yield(lease,"INVALID_DIRECTION");
                if(lease.Owned && !ReferenceEquals(lease.Weapon,w)) Release(lease);
                uint tick=(uint)sessionTick.GetValue(session);
                if(lease.LastTick==tick) return lease.State="ALREADY_STEPPED";
                pivot.Invoke(w,null);
                object[] args={w,Vector3D.Normalize(direction),distanceSquared,false,true,caller,false};
                if(!(bool)look.Invoke(null,args)) return Yield(lease,"MECHANICAL_LIMIT");
                if(!lease.Owned) { lease.Weapon=w; lease.Component=comp; lease.OriginalHome=(bool)returning.GetValue(w); lease.OriginalChangeTick=(uint)changeTick.GetValue(target.GetValue(w)); }
                // Defer WC's idle-home timer while we own aim. Without this WC
                // schedules another delayed homing callback every time we cancel it.
                // No target state, identity, position or alignment flag is fabricated.
                lease.OwnedChangeTick=tick; changeTick.SetValue(target.GetValue(w),tick);
                // Do not add a second angular step after native homing/tracking this tick.
                returning.SetValue(w,false);
                if((uint)azTick.GetValue(w)==tick || (uint)elTick.GetValue(w)==tick) return lease.State="NATIVE_STEP_YIELD";
                lease.LastTick=tick;
                args[3]=true; args[4]=false;
                bool moved=(bool)look.Invoke(null,args);
                if(!(bool)args[6]) return Yield(lease,"MECHANICAL_LIMIT");
                if(moved) aim.Invoke(w,null);
                return lease.State=moved ? "STEPPED" : "AT_COMMANDED_ANGLE";
            }
            catch { return Yield(lease,"AIM_ERROR"); }
        }
        string Yield(Lease lease,string why) { Release(lease); return lease.State=why; }
        public string Release(Lease lease)
        {
            if(!lease.Owned) return lease.State="UNOWNED";
            try
            {
                // Compare-and-restore only our idle homing flag; never rewind angles or native targets.
                if(!(bool)userControlled.GetValue(lease.Component) && !(bool)hasTarget.GetValue(target.GetValue(lease.Weapon)) &&
                    !(bool)returning.GetValue(lease.Weapon) && (uint)changeTick.GetValue(target.GetValue(lease.Weapon))==lease.OwnedChangeTick)
                {
                    object idleTarget=target.GetValue(lease.Weapon);
                    if((uint)changeTick.GetValue(idleTarget)==lease.OwnedChangeTick) changeTick.SetValue(idleTarget,lease.OriginalChangeTick);
                    returning.SetValue(lease.Weapon,lease.OriginalHome);
                    // WC removes a cancelled homing entry. Restoring just the flag
                    // would strand the turret in a home-pending state with no worker.
                    if(lease.OriginalHome)
                    {
                        var homes=Read(sessionInstance.GetValue(null),"HomingWeapons") as IList;
                        if(homes==null) throw new InvalidOperationException("Homing registry unavailable");
                        if(!homes.Contains(lease.Weapon)) sendHome.Invoke(lease.Weapon,new object[]{null});
                    }
                }
                lease.State="RELEASED";
            }
            catch { lease.State="RELEASE_UNVERIFIED"; }
            finally { lease.Weapon=null; lease.Component=null; lease.LastTick=uint.MaxValue; }
            return lease.State;
        }
        // Inspect active runtime definitions, not just source-file names. A changed
        // weapon/ammo profile blocks shots while leaving pre-aim available.
        internal string FireProfile(Lease lease)
        {
            if(!lease.Owned) return "NO_AIM_LEASE";
            try
            {
                object w=lease.Weapon;
                if(Read(lease.Component,"SubtypeName").ToString()!="sdx_pdcImprovised") return "UNVALIDATED_WEAPON";
                object ammo=Read(w,"ActiveAmmoDef.AmmoDef");
                if(Read(ammo,"AmmoRound").ToString()!="40mm Lead-Steel") return "UNVALIDATED_AMMO";
                string path="Trajectory.";
                if(Convert.ToDouble(Read(ammo,path+"DesiredSpeed"))!=3000 || Convert.ToDouble(Read(ammo,path+"AccelPerSec"))!=0 ||
                    Convert.ToDouble(Read(ammo,path+"MaxLifeTime"))!=59 || Convert.ToDouble(Read(ammo,path+"MaxTrajectory"))!=3500 ||
                    Convert.ToDouble(Read(ammo,path+"GravityMultiplier"))!=3 || Convert.ToDouble(Read(ammo,path+"SpeedVariance.Start"))!=-150 ||
                    Convert.ToDouble(Read(ammo,path+"SpeedVariance.End"))!=150 || Read(ammo,path+"Guidance").ToString()!="None") return "AMMO_PROFILE_CHANGED";
                object loading=Read(w,"System.Values.HardPoint.Loading");
                if(Convert.ToDouble(Read(loading,"DelayUntilFire"))!=12 || Convert.ToDouble(Read(loading,"BarrelsPerShot"))!=1 ||
                    Convert.ToDouble(Read(loading,"TrajectilesPerBarrel"))!=1 || Convert.ToDouble(Read(loading,"ShotsInBurst"))!=0 ||
                    (bool)Read(loading,"FireFull")) return "FIRING_PROFILE_CHANGED";
                int count=Convert.ToInt32(Read(lease.Component,"Data.Repo.Values.Set.Overrides.BurstCount"));
                if(count<1 || count>2) return "CYCLE_BUDGET_EXCEEDED";
                if((bool)Read(w,"ShootRequest.Dirty") || Read(w,"ShootRequest.Type").ToString()!="None") return "FOREIGN_SHOOT_REQUEST";
                return "AUDITED_40MM";
            }
            catch { return "UNSUPPORTED_AMMO_SCHEMA"; }
        }
        internal bool CanStartCycle(Lease lease)
        {
            try { return lease.Owned && !(bool)Read(lease.Weapon,"IsShooting") && !(bool)Read(lease.Weapon,"Loading") &&
                !(bool)Read(lease.Weapon,"Reload.WaitForClient") && !(bool)Read(lease.Weapon,"PartState.Overheated") &&
                Convert.ToInt32(Read(lease.Weapon,"ProtoWeaponAmmo.CurrentAmmo"))>0 &&
                Read(lease.Component,"Data.Repo.Values.State.Trigger").ToString()=="Off"; }
            catch { return false; }
        }
        internal int CycleSize(Lease lease)
        {
            try { return Math.Max(1,Convert.ToInt32(Read(lease.Component,"Data.Repo.Values.Set.Overrides.BurstCount"))); }
            catch { return int.MaxValue; }
        }
        internal bool FireOneCycle(Lease lease)
        {
            try
            {
                object session=sessionInstance.GetValue(null);
                if(Status!="SCHEMA_READY" || session==null || !(bool)sessionServer.GetValue(session) || (bool)sessionMp.GetValue(session) ||
                    !CanStartCycle(lease) || FireProfile(lease)!="AUDITED_40MM" || (bool)userControlled.GetValue(lease.Component) ||
                    (bool)hasTarget.GetValue(target.GetValue(lease.Weapon))) return false;
                // WC's physical manual trigger signal allows an untargeted barrel shot.
                // It does not install a fake target or set ShootRequest.Position.
                return (bool)requestCycle.Invoke(Read(lease.Component,"ShootManager"),new[]{(object)0L,once,manualSignal});
            }
            catch { return false; }
        }
    }
}
