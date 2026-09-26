using System;
using System.Linq;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRageMath;

namespace ZeoPDC
{
    internal sealed partial class PdcEngine
    {
        private string PreaimSummary()
        {
            if(JoiningServer())return "PREAIM: REMOTE PATH UNVERIFIED";
            var c=config();
            if(!c.PreemptiveLookEnabled) return "PREAIM OFF";
            if(!c.ControlEnabled || !c.NativeLeadEnabled || fireLockout) return "PREAIM PAUSED";
            if(guns.Count==0) return "PREAIM WAIT";
            if(guns.Any(g=>g.Manager.ManagerFailure=="OFFLINE_WORLD_REQUIRED")) return "PREAIM: OFFLINE REQUIRED";
            if(guns.Any(g=>g.Manager.ManagerFailure=="REMOTE_CLIENT_UNSUPPORTED")) return "PREAIM: HOST REQUIRED";
            int active=guns.Count(g=>g.OuterSelected && g.OuterLease.Owned);
            if(active>0) return "PREAIM "+active+" ACTIVE";
            if(guns.Any(g=>g.NativeRead=="PROJECTILE")) return "PREAIM: NATIVE TARGET";
            if(guns.Any(g=>!string.IsNullOrEmpty(g.Manager.ManagerFailure))) return "PREAIM UNAVAILABLE";
            if(guns.Any(g=>g.OuterSelected)) return "PREAIM ALIGN / YIELD";
            return "PREAIM WAIT";
        }
        private bool FreshOuterTrack(Track t)
        {
            return t!=null && !t.Resolved && t.StableId && t.LostFrames==0 && t.LastFrame>=frame-1 && t.LastFrame<=frame &&
                t.HealthRead=="OK" && t.HealthFrame>=frame-1 && t.HealthFrame<=frame && t.Health>0 &&
                t.NativeStateRead=="OK" && t.NativeState=="Alive" && t.NativeStateFrame>=frame-1 && t.NativeStateFrame<=frame && t.HullClosing>0;
        }
        private bool OuterInnerPressure(PdcConfig c)
        {
            return tracks.Any(t=>t!=null && !t.Resolved && t.LostFrames==0 && t.LastFrame>=frame-1 &&
                (t.Hull<=Math.Max(3100,c.EngagementRangeMeters+100) || t.Tti<=c.UrgentCoverageTtiSeconds));
        }
        private bool EarlyLook(PdcConfig c) { return !c.LabEnabled && c.ManagedDefenseEnabled && !c.PreemptiveFireEnabled; }
        private double LookHorizon(PdcConfig c) { return EarlyLook(c)?c.PreaimRangeMeters:c.PreemptiveRangeMeters; }
        private double ScopeError(Gun g, Track t) { return !g.ScopeValid || t==null ? double.NaN : OuterIntercept.Error(g.ScopeDirection,t.Pos-g.ScopeOrigin); }
        private void ApplyPreemptiveLayer()
        {
            if(JoiningServer()){foreach(var g in guns)ReleaseOuter(g,"REMOTE_PREAIM_UNVERIFIED");return;}
            var c=config(); double inner=Math.Max(3100,c.EngagementRangeMeters+100);
            bool enabled=c.ControlEnabled && !fireLockout && c.NativeLeadEnabled && c.PreemptiveLookEnabled;
            bool sourceOk=stableSensorHealthy && wc.NativeObserver.Status=="SCHEMA_READY" && wc.OuterAim.Status=="SCHEMA_READY";
            bool heatKnown=guns.Count>0 && guns.All(g=>g.HeatKnown && g.TelemetryFrame==frame);
            bool pressure=!EarlyLook(c) && OuterInnerPressure(c);
            var approach=tracks.Where(t=>FreshOuterTrack(t) && (EarlyLook(c) || t.Hull>inner) && t.Hull<=LookHorizon(c)).OrderBy(t=>t.Hull).FirstOrDefault();
            var selected=new HashSet<Gun>();
            if(enabled && sourceOk && (EarlyLook(c) || heatKnown) && !pressure && approach!=null)
                foreach(var g in guns.Where(g=>(!c.WeaponAwareEnabled || FreshProfile(g)) && g.Functional && g.ShotHooked && g.NativeControlKnown && !g.NativeManual &&
                    !g.OuterStopPending && (!EarlyLook(c) || g.NativeRead!="PROJECTILE") && g.ScopeValid && g.TargetRead=="OK" && !g.TargetFlag1 && !g.TargetFlag2 && !g.TargetFlag3 && g.ShootingRead=="OK" && (!g.ShootingValue || g.Outer.Active))
                    .OrderByDescending(g=>g.OuterLease.Owned).ThenBy(g=>ScopeError(g,approach)).ThenBy(g=>g.Heat).ThenBy(g=>g.EntityId).Take(ManagerSettings.LookCapacity(c))) selected.Add(g);
            foreach(var g in guns)
            {
                if(!selected.Contains(g))
                {
                    string reason=!enabled?"OFF":!sourceOk?"AIM_SOURCE_UNAVAILABLE":(!EarlyLook(c) && !heatKnown)?"HEAT_UNKNOWN":pressure?"INNER_PRIORITY":approach==null?"NO_OUTER_APPROACH":"NOT_SELECTED";
                    ReleaseOuter(g,reason); continue;
                }
                if(g.OuterTrack!=approach.ProjectileId) ReleaseOuter(g,"TARGET_CHANGE");
                g.OuterSelected=true; g.OuterTrack=approach.ProjectileId;
                g.Allowed=g.Outer.Active; g.DecisionTrackId=approach.Id; g.DecisionBasis="ZEO_OUTER_LEAD";
                if((!c.LabEnabled && !c.ManagedDefenseEnabled) || c.PreemptiveFireEnabled) g.Rof=c.PreemptiveRof;
                // Native tracking ranges remain on their normal adaptive values.
                g.OuterState="ZEO_PREAIM";
            }
        }
        private void StopOuterBurst(Gun g,string reason)
        {
            if(g.Outer.Active)
            {
                PreemptivePolicy.Stop(g.Outer,frame,g.Shots,config());
                g.OuterStopPending=!wc.ToggleWeaponFire(g.Entity,g.Part,false);
            }
            g.Allowed=false; g.OuterState=reason;
            var old=System.Threading.Volatile.Read(ref g.Context);
            if(old!=null) System.Threading.Volatile.Write(ref g.Context,new ShotContext { Track=old.Track,Volley=old.Volley,
                Projectile=old.Projectile,LastObservedFrame=old.LastObservedFrame,ObservedNativeProjectile=old.ObservedNativeProjectile,
                NativeRead=old.NativeRead,NativeFrame=old.NativeFrame,CommandFrame=frame,Hull=old.Hull,Tti=old.Tti,
                Allowed=false,Gate="OUTER_STOP_"+reason,Mode=old.Mode });
        }
        private void ReleaseOuter(Gun g,string reason)
        {
            bool owned=g.OuterSelected || g.OuterLease.Owned || g.Outer.Active;
            if(owned)
            {
                StopOuterBurst(g,reason);
                g.OuterReleaseStatus=wc.OuterAim.Release(g.OuterLease); g.OuterReleases++;
                g.OuterReleaseFrame=frame; g.OuterNativeReacquiredFrame=-1;
                if(testArmed && recorder!=null) recorder.Event("ZEO_OUTER_RELEASE","gun="+g.EntityId+" target="+g.OuterTrack+" reason="+reason+" release="+g.OuterReleaseStatus+" stop_pending="+g.OuterStopPending);
            }
            g.OuterSelected=false; g.OuterTrack=0; g.OuterAimStable=0;
            g.OuterState=g.OuterStopPending?"STOP_WRITE_PENDING":reason;
            if(g.OuterStopPending) g.Allowed=false;
        }
        // Runs each plugin update after allocation. No native target is fabricated.
        private void ServicePreemptiveStops()
        {
            if(JoiningServer())return;
            var c=config(); double inner=Math.Max(3100,c.EngagementRangeMeters+100);
            bool enabled=c.ControlEnabled && c.NativeLeadEnabled && c.PreemptiveLookEnabled && !fireLockout;
            double heat=0; bool heatKnown=guns.Count>0;
            if(guns.Any(g=>g.OuterSelected || g.Outer.Active))
                foreach(var g in guns) { double h; if(!wc.TryReadHeatPercent(g.Entity,g.Part,out h)) { heatKnown=false; break; } heat=Math.Max(heat,h); }
            foreach(var g in guns)
            {
                if(g.OuterStopPending)
                {
                    g.OuterStopPending=!wc.ToggleWeaponFire(g.Entity,g.Part,false);
                    g.Allowed=false; g.OuterState="STOP_WRITE_PENDING"; continue;
                }
                if(!g.OuterSelected) continue;
                var t=tracks.FirstOrDefault(x=>x.ProjectileId==g.OuterTrack && x.StableId);
                if(!enabled || !g.Functional || !stableSensorHealthy || !FreshOuterTrack(t) || (!EarlyLook(c) && (t.Hull<=inner || OuterInnerPressure(c))) || t.Hull>LookHorizon(c))
                { ReleaseOuter(g,!enabled?"OFF":"INNER_OR_STALE_YIELD"); continue; }
                ulong native; bool aligned,manual;
                string nativeRead=wc.NativeObserver.Read(g.Entity,g.Part,out native);
                if(nativeRead=="PROJECTILE" || wc.NativeObserver.ReadControl(g.Entity,g.Part,out aligned,out manual)!="OK" || manual)
                { ReleaseOuter(g,"NATIVE_OR_MANUAL_YIELD"); continue; }
                Vector3D origin,direction,shipVelocity,gravity;
                if(!wc.TryGetWeaponScope(g.Entity,g.Part,out origin,out direction)) { ReleaseOuter(g,"SCOPE_UNAVAILABLE"); continue; }
                try
                {
                    var velocity=controller.GetShipVelocities();
                    // WC's ProjectileGen inherits TopEntityVel, not barrel angular velocity.
                    shipVelocity=velocity.LinearVelocity;
                    gravity=controller.GetNaturalGravity();
                }
                catch { ReleaseOuter(g,"SHIP_MOTION_UNAVAILABLE"); continue; }
                double delay=g.Outer.Active ? Math.Max(0,(g.OuterCycleStartFrame+12-frame)/60d)+.05 : OuterIntercept.Delay;
                g.OuterSolution=EarlyLook(c) ? new OuterIntercept.Solution {Direction=Vector3D.Normalize(t.Pos-origin),Reason="EARLY_DIRECTION_ONLY"} : OuterIntercept.Solve(t.Pos-origin,t.StateVelocity,shipVelocity,t.Acceleration,gravity,t.Hull,t.HullClosing,inner,delay);
                g.OuterScopeError=OuterIntercept.Error(direction,g.OuterSolution.Direction);
                g.OuterAimStable=BankPlanner.Finite(g.OuterScopeError) && g.OuterScopeError<=.25 ? g.OuterAimStable+1 : 0;
                bool wasOwned=g.OuterLease.Owned;
                string step=wc.OuterAim.Step(g.Entity,g.Part,g.OuterLease,g.OuterSolution.Direction,(t.Pos-origin).LengthSquared());
                if(!wasOwned && g.OuterLease.Owned && testArmed && recorder!=null)
                    recorder.Event("ZEO_OUTER_AIM_ACQUIRED","gun="+g.EntityId+" target="+t.ProjectileId+" hull_m="+t.Hull.ToString(System.Globalization.CultureInfo.InvariantCulture)+" step="+step);
                if(step=="ALREADY_STEPPED") continue;
                bool aimOk=g.OuterLease.Owned && (step=="STEPPED" || step=="AT_COMMANDED_ANGLE" || step=="ALREADY_STEPPED");
                if(!aimOk) { StopOuterBurst(g,step); continue; }
                bool cool=PreemptivePolicy.HeatAllowed(g.Outer,heatKnown?heat:double.NaN,c);
                if(c.PreemptiveFireEnabled || g.OuterProfile=="UNREAD") g.OuterProfile=wc.OuterAim.FireProfile(g.OuterLease);
                long mode; bool modeOk=TryReadTerminal(g.Block,"WC_Shoot Mode",out mode) && mode==2;
                bool clear=false;
                try { VRage.Game.ModAPI.IHitInfo hit; clear=c.PreemptiveFireEnabled && g.OuterSolution.CanFire && g.OuterAimStable>=3 && cool && MyAPIGateway.Physics!=null && !MyAPIGateway.Physics.CastRay(origin+direction*.5,origin+g.OuterSolution.Direction*Math.Max(1,g.OuterSolution.TravelMeters),out hit); }
                catch { clear=false; }
                g.OuterPathClear=clear;
                bool eligible=c.PreemptiveFireEnabled && cool && aimOk && g.OuterAimStable>=3 && g.OuterSolution.CanFire &&
                    g.OuterProfile=="AUDITED_40MM" && modeOk && clear && ShipTurnDegPerSec()<=3 &&
                    g.RofReadOk && g.ActualRof<=c.PreemptiveRof+.01 && frame-g.TelemetryFrame<=6;
                if(g.Outer.Active)
                {
                    if(!eligible || PreemptivePolicy.Expired(g.Outer,frame,g.Shots,c)) StopOuterBurst(g,"CYCLE_STOP");
                    else { g.Allowed=true; g.OuterState="CYCLE_PENDING"; }
                    continue;
                }
                g.OuterState=!c.PreemptiveFireEnabled?"ZEO_LOOK_ONLY":!cool?"HEAT_HOLD":g.OuterAimStable<3?"ZEO_ALIGNING":
                    !g.OuterSolution.CanFire?g.OuterSolution.Reason:g.OuterProfile!="AUDITED_40MM"?g.OuterProfile:!clear?"OBSTRUCTED_OR_UNKNOWN":!eligible?"NOT_READY":"READY";
                long spent; g.Outer.TargetCallbacks.TryGetValue(t.ProjectileId,out spent);
                if(!eligible || !wc.OuterAim.CanStartCycle(g.OuterLease) || wc.OuterAim.CycleSize(g.OuterLease)>Math.Min(c.PreemptiveMaxCallbacksPerBurst,c.PreemptiveMaxCallbacksPerTarget-spent)) continue;
                string decision=PreemptivePolicy.Evaluate(g.Outer,frame,g.Shots,t.ProjectileId,true,heat,c);
                if(decision!="BURST") { g.OuterState=decision; continue; }
                // One API cycle, with the audited startup allowance. Never repeat the
                // request inside a cycle; actual callback/time stops still apply.
                g.Outer.EndFrame+=12;
                g.OuterCycleStartFrame=frame;
                var context=new ShotContext { Track=t.Id,Volley=t.VolleyIndex,Projectile=t.ProjectileId,LastObservedFrame=t.LastFrame,
                    ObservedNativeProjectile=0,NativeRead="NO_TARGET",NativeFrame=frame,CommandFrame=frame,Hull=t.Hull,Tti=t.Tti,
                    Allowed=true,Gate="ZEO_OUTER_CYCLE",Mode=ExperimentMode };
                System.Threading.Volatile.Write(ref g.Context,context);
                g.OuterCycleRequests++;
                bool sent=wc.OuterAim.FireOneCycle(g.OuterLease);
                g.Allowed=sent; g.OuterState=sent?"CYCLE_REQUEST_SENT":"CYCLE_REQUEST_FAILED";
                g.RequestResult=g.OuterState; g.PkState="OUTER_"+g.OuterState;
                if(testArmed && recorder!=null) recorder.Event("ZEO_OUTER_CYCLE", "gun="+g.EntityId+" target="+t.ProjectileId+" status="+g.OuterState+" flight_s="+g.OuterSolution.FlightSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if(!sent) StopOuterBurst(g,"CYCLE_REQUEST_FAILED");
            }
        }
    }
}

