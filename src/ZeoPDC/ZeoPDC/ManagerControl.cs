using System;
using System.Collections.Generic;
using System.Linq;

namespace ZeoPDC
{
    internal sealed partial class PdcEngine
    {
        readonly List<Gun> managerOwned=new List<Gun>();
        int managerGeneration=-1;

        bool OwnManagerTerminal<T>(Gun g,string key,T value)
        {
            var s=g.Manager; T current;
            if(!TryReadTerminal(g.Block,key,out current)) return false;
            object owned;
            if(s.Owned.TryGetValue(key,out owned) && !Equals(current,owned))
            { s.RequestDisabled=true; s.State="EXTERNAL_EDIT_YIELD"; return false; }
            if(!s.Saved.ContainsKey(key)) s.Saved[key]=current;
            // Record intent before write so partial-success failures can be restored.
            s.Owned[key]=value;
            if(!Equals(current,value) && !SetTerminal(g.Block,key,value)) return false;
            T observed; return TryReadTerminal(g.Block,key,out observed) && Equals(observed,value);
        }

        bool ReleaseManagerGun(Gun g,string why)
        {
            var s=g.Manager;
            bool ok=s.Adapter.Restore(); ok &= RestoreLabTerminals(s);
            // Restores owned ROF only if it still equals our last write. This method
            // does not enable a trigger: production never sets DirectConfigured.
            YieldUnknownProfile(g);
            ok &= g.ProfileYielded;
            s.NativeConfigured=false;
            if(ok && !s.RequestDisabled) s.State=why;
            g.CommandResult=ok?s.State:"MANAGER_RESTORE_PENDING";
            return ok;
        }

        internal bool ReleaseManagerHardware(string why)
        {
            bool ok=true;
            foreach(var g in managerOwned.ToArray())
                if(ReleaseManagerGun(g,why)) { managerOwned.Remove(g); g.Manager.RequestDisabled=false; }
                else ok=false;
            directControlActive=false;
            return ok;
        }

        void ApplyManagerControl()
        {
            var c=config();
            if(!c.ControlEnabled || !wc.Ready || !wc.DirectControlReady ||
                wc.NativeObserver.Status!="SCHEMA_READY" || !stableSensorHealthy)
            {
                ReleaseManagerHardware("MANAGER_NATIVE_YIELD");
                foreach(var g in guns) ReleaseOuter(g,"MANAGER_NATIVE_YIELD");
                directControlState="MANAGER // NATIVE YIELD"; return;
            }
            if(managerGeneration!=wc.Generation)
            {
                if(!ReleaseManagerHardware("API_GENERATION_CHANGE")) return;
                managerGeneration=wc.Generation;
            }
            // Scans replace Gun objects but retain the owned hardware state.
            foreach(var old in managerOwned.ToArray()) {
                var replacement=guns.FirstOrDefault(g=>ReferenceEquals(g.Manager,old.Manager));
                if(replacement!=null && !ReferenceEquals(old,replacement)) {
                    managerOwned.Remove(old); managerOwned.Add(replacement);
                } else if(replacement==null && ReleaseManagerGun(old,"REMOVED_GUN")) managerOwned.Remove(old);
            }
            int applied=0,yielded=0;
            foreach(var g in guns) {
                if(g.Entity==null || g.Block==null) continue;
                var s=g.Manager; s.Block=g.Block;
                if(!managerOwned.Contains(g)) managerOwned.Add(g);
                if(!g.Functional || !FreshProfile(g) || !g.NativeControlKnown || g.NativeManual || s.RequestDisabled || frame<s.RequestFrame) {
                    ReleaseManagerGun(g,s.RequestDisabled?"EXTERNAL_EDIT_YIELD":frame<s.RequestFrame?"RETRY_WAIT // "+s.ManagerFailure:"MANUAL_OR_PROFILE_YIELD");
                    yielded++; continue;
                }
                bool ok=s.Adapter.Apply(wc.NativeObserver.Component(g.Entity),g.Part,ManagerSettings.DistributionOnly(),0,true);
                if(ok) ok=OwnManagerTerminal(g,"WC_EnableFireDistribution",true);
                if(ok && !s.NativeConfigured) {
                    ok=OwnManagerTerminal(g,"WC_Shoot Mode",0L) && wc.ToggleWeaponFire(g.Entity,g.Part,false);
                    s.NativeConfigured=ok;
                }
                long mode;
                if(ok && (!TryReadTerminal(g.Block,"WC_Shoot Mode",out mode) || mode!=0)) {
                    s.RequestDisabled=true; s.State="EXTERNAL_EDIT_YIELD"; ok=false;
                }
                if(ok) { ResolveManagedCadence(g); ok=ApplyGunRof(g,g.Rof); }
                if(!ok) {
                    if(s.Adapter.Status=="FOREIGN_SYSTEM_CHANGE") s.RequestDisabled=true;
                    string failure=s.RequestDisabled?s.State:s.Adapter.Status;
                    ReleaseManagerGun(g,"CAPABILITY_NATIVE_YIELD");
                    s.ManagerFailure=failure; s.RequestFrame=frame+300;
                    s.State="MANAGER_NATIVE_YIELD // "+failure;
                    g.CommandResult=s.State; yielded++; continue;
                }
                s.ManagerFailure=""; s.State="MANAGER_DISTRIBUTION_NATIVE_FIRE";
                g.Allowed=true; g.CommandResult="MANAGER_APPLIED_READBACK";
                var t=ObservedNativeTrack(g);
                System.Threading.Volatile.Write(ref g.Context,new ShotContext {
                    Track=t==null?0:t.Id,Volley=t==null?0:t.VolleyIndex,Projectile=t==null?0:t.ProjectileId,
                    ObservedNativeProjectile=g.NativeProjectile,NativeRead=g.NativeRead,NativeFrame=g.TelemetryFrame,
                    LastObservedFrame=t==null?frame:t.LastFrame,CommandFrame=frame,Allowed=true,Gate=s.State,Mode=ExperimentMode });
                applied++;
            }
            directControlActive=applied>0;
            directControlState="MANAGER // DISTRIBUTION "+applied+"/"+guns.Count+" NATIVE YIELD "+yielded;
        }
    }
}
