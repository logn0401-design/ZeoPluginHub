using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Sandbox.ModAPI;
using VRageMath;

namespace ZeoPDC
{
    internal sealed class LabGunState
    {
        internal readonly LabWeaponAdapter Adapter=new LabWeaponAdapter();
        internal readonly Dictionary<string,object> Saved=new Dictionary<string,object>(), Owned=new Dictionary<string,object>();
        internal Sandbox.ModAPI.IMyTerminalBlock Block;
        internal bool NativeConfigured, RequestDisabled, BurstDisabled;
        internal ulong BurstTarget;
        internal BurstDecision Burst=new BurstDecision();
        internal int BurstAllowFrame=-1;
        internal long BurstShotMark;
        internal long Mode=-1;
        internal int RequestFrame=-10000, LeaseFrame=-10000;
        internal long ShotMark;
        internal ulong Target;
        internal string State="NATIVE", ShadowState="UNOBSERVED", ManagerFailure="";
        internal double? ShadowError, ShadowFlight;
    }
    internal sealed partial class PdcEngine
    {
        internal readonly LabJournal LabLog=new LabJournal();
        readonly LabDamageMonitor labDamage=new LabDamageMonitor();
        readonly SharedBurstPolicy labBurst=new SharedBurstPolicy();
        readonly List<LabGunState> labOwned=new List<LabGunState>();
        readonly Dictionary<int,LabCohortReport> labCohorts=new Dictionary<int,LabCohortReport>();
        readonly HashSet<int> labReported=new HashSet<int>();
        internal int LabRevision, LabBaselineComplete;
        internal long LabDamageRecords, LabDamageKeyDrops;
        readonly Dictionary<string,double> labDamageTotals=new Dictionary<string,double>();
        readonly Dictionary<string,long> labDamageCounts=new Dictionary<string,long>();
        bool labRecording;
        internal string LabFault=""; internal string LabShipId { get { return grid==null?"":grid.EntityId.ToString(); } }
        int labClearFrame=-1, labGeneration, labEpoch, labWriteFailures;
        string labBoundShip="";
        internal bool LabCanApply
        {
            get { return HasShip && wc.Ready && stableSensorHealthy && labClearFrame>=0 && frame-labClearFrame>=12 &&
                guns.Count>0 && guns.All(g=>g.Functional && g.TelemetryFrame>=frame-6 && g.NativeControlKnown && !g.NativeManual && g.Profile.Valid && g.ShotHooked &&
                    frame-g.LastObservedShotFrame>(g.Profile.ShotLifeSeconds+g.Profile.StartupSeconds+.1)*60 &&
                    !g.Outer.Active && !g.OuterStopPending && g.NativeRead!="PROJECTILE"); }
        }
        internal void LabBegin()
        {
            LabLog.Open(dataDir); LabRevision=1; LabBaselineComplete=0; LabFault="";
            labRecording=true;
            labCohorts.Clear(); labReported.Clear(); labGeneration=wc.Generation; labEpoch=liveTrackerEpoch; labBoundShip=LabShipId; labWriteFailures=0;
            labDamageTotals.Clear(); labDamageCounts.Clear(); LabDamageRecords=LabDamageKeyDrops=0;
            labDamage.Drain(); labDamage.Received=labDamage.Dropped=labDamage.Errors=0;
        }
        internal void LabChanged(int revision)
        {
            LabRevision=revision; labBurst.Reset();
            foreach(var g in guns) { g.Lab.BurstDisabled=false; g.Lab.BurstTarget=0; g.Lab.Burst=new BurstDecision(); g.Lab.BurstAllowFrame=-1; }
            foreach(var g in guns) { g.Lab.RequestDisabled=false; g.Lab.Target=0; g.Lab.RequestFrame=-10000; }
            RecordConfiguration(config());
        }
        internal void LabObserve()
        {
            // Also maintain a clear-boundary observation before LAB_START is accepted.
            bool clear=stableSensorHealthy && !tracks.Any(t=>!t.Resolved) && guns.Count>0 && guns.All(g=>g.NativeRead!="PROJECTILE");
            labClearFrame=clear?(labClearFrame<0?frame:labClearFrame):-1;
            if(!config().LabEnabled || !labRecording) return;
            if(LabLog.WriteErrors>0) LabFault="JOURNAL_WRITE_FAILURE";
            if(labGeneration!=wc.Generation || labEpoch!=liveTrackerEpoch) LabFault="GENERATION_OR_TRACKER_RESET";
            labDamage.Tick=frame; labDamage.Revision=LabRevision;
            labDamage.SetOwned(guns.Select(g=>g.EntityId)); wc.HookLabDamage(labDamage);
            FlushLabDamage();
            foreach(var group in tracks.Where(t=>t.VolleyIndex>0 && t.CountedPhysical).GroupBy(t=>t.VolleyIndex)) {
                if(labReported.Contains(group.Key)) continue;
                var rows=group.ToArray(); LabCohortReport r;
                if(!labCohorts.TryGetValue(group.Key,out r)) {
                    r=new LabCohortReport { volley=group.Key,revision=LabRevision,first_tick=frame,reason="",comparable=true };
                    labCohorts.Add(group.Key,r);
                }
                if(r.revision!=LabRevision) { r.comparable=false; r.reason="MIXED_CONFIGURATION"; }
                if(!stableSensorHealthy || goalSensorGap || rows.Any(t=>t.InitialCohort || !t.StableId || t.Reacquisitions>0)) { r.comparable=false; r.reason="INITIAL_PARTIAL_OR_ACQUISITION_GAP"; }
                if(guns.Count==0 || guns.Any(g=>!g.HeatKnown || !g.ShotHooked || !g.Functional || !g.Profile.Valid || !g.NativeControlKnown || g.NativeManual))
                { r.comparable=false; r.reason="GUN_COVERAGE_GAP"; }
                if(!string.IsNullOrEmpty(LabFault)) { r.comparable=false; r.reason=LabFault; }
                r.sampled_peak_heat=Math.Max(r.sampled_peak_heat,guns.Where(g=>g.HeatKnown).Select(g=>g.Heat).DefaultIfEmpty(0).Max());
                if(rows.Length!=32 || rows.Any(t=>!t.Resolved)) continue;
                r.count=rows.Length; r.leakers=rows.Count(t=>t.MinHull<=150); r.last_tick=frame;
                r.projectile_ids=rows.Select(t=>t.ProjectileId.ToString()).ToArray();
                if(r.comparable && r.revision==1) LabBaselineComplete++;
                LabLog.Write("cohort_complete",frame,LabRevision,r); labReported.Add(group.Key);
                WriteLabTotals();
            }
            // Bounded session: no silent removal of keyed accounting. Stop before exhaustion.
            if(labReported.Count>1024) LabFault="COHORT_CAPACITY_REACHED";
            if(goalSensorGap) LabFault="ACQUISITION_GAP_REQUIRES_FRESH_BASELINE";
            if(frame%30==0) {
                foreach(var g in guns) {
                    ShadowIntercept(g);
                    LabLog.Write("gun_sample",frame,LabRevision,LabSample(g));
                }
                LabLog.Flush();
            }
        }
        void FlushLabDamage()
        {
            foreach(var hit in labDamage.Drain()) {
                LabLog.Write("damage_event_unvalidated",frame,LabRevision,hit); LabDamageRecords++;
                string key=hit.gun+":"+hit.part+":"+hit.target;
                if(!labDamageTotals.ContainsKey(key) && labDamageTotals.Count>=8192) { LabDamageKeyDrops++; continue; }
                double sum; labDamageTotals.TryGetValue(key,out sum); labDamageTotals[key]=sum+hit.damage;
                long count; labDamageCounts.TryGetValue(key,out count); labDamageCounts[key]=count+1;
            }
        }
        void WriteLabTotals()
        {
            LabLog.Write("damage_cumulative_keys",frame,LabRevision,labDamageTotals.Select(p=>new LabDamageTotal { key=p.Key,damage=p.Value,events=labDamageCounts[p.Key].ToString() }).ToArray());
        }
        internal LabGunSample[] LabSamples() { return guns.Select(LabSample).ToArray(); }
        LabGunSample LabSample(Gun g)
        {
            var hardware=!config().LabEnabled && config().ManagedDefenseEnabled?g.Manager:g.Lab;
            return new LabGunSample { gun=g.EntityId.ToString(),part=g.Part,sample_tick=frame,native_age_ticks=frame-g.TelemetryFrame,
                assigned=g.TrackId.ToString(),acquired=g.NativeRead=="PROJECTILE"?g.NativeProjectile.ToString():null,requested=g.Lab.Target.ToString(),
                state=hardware.State,adapter=hardware.Adapter.Status,spread_deg=hardware.Adapter.SpreadDegrees,tolerance_deg=hardware.Adapter.ToleranceDegrees,
                prediction=hardware.Adapter.Prediction,advanced_solver=hardware.Adapter.AdvancedSolver,
                ready=g.ReadyRead=="OK"?(bool?)g.ReadyValue:null,firing=g.ShootingRead=="OK"?(bool?)g.ShootingValue:null,
                aligned=g.NativeControlKnown?(bool?)g.NativeAligned:null,heat=g.HeatKnown?Finite(g.Heat):null,callbacks=g.Shots.ToString(),
                rof=g.RofReadOk?Finite(g.ActualRof):null,range=g.RangeReadOk?Finite(g.ActualRange):null,
                cadence_reason=g.CadenceReason, effective_rpm=g.Profile==null?(double?)null:Finite(g.Profile.EffectiveRpm),
                burst_target=g.Lab.BurstTarget.ToString(),burst_pause=g.Lab.Burst.Pause,burst_rounds=g.Lab.Burst.ObservedRounds.ToString(),
                burst_spent_units=g.Lab.Burst.SpentUnits,burst_budget_units=g.Lab.Burst.BudgetUnits,burst_pause_until=g.Lab.Burst.PauseUntil,
                predictor=g.Lab.ShadowState,shadow_error_deg=g.Lab.ShadowError,shadow_flight_s=g.Lab.ShadowFlight };
        }
        void AllocateLabNative()
        {
            var c=config(); double bank=guns.Where(g=>g.HeatKnown).Select(g=>g.Heat).DefaultIfEmpty(0).Max();
            foreach(var g in guns) {
                if(g.Shots>g.ObservedShotMark) g.LastObservedShotFrame=frame;
                g.ObservedShotMark=g.Shots;
                var t=ObservedNativeTrack(g); g.TrackId=0; g.DecisionTrackId=t==null?0:t.Id;
                g.DecisionBasis="WC_ACTUAL_TARGET_ROF_ONLY"; g.Allowed=true; g.PkState="WC_AUTONOMOUS_NO_ZEO_GATE";
                // No target is not a reason to slow a gun that WC may acquire between samples.
                g.Rof=t==null?1:CombatRof(g.Heat,bank,t,c,0);
                g.DesiredRange=g.ActualRange;
                if(c.LabRangeControl) { g.DesiredRange=c.EngagementRangeMeters; g.RangeReason="LAB_EXPLICIT_RANGE"; }
                else g.RangeReason="NATIVE_RANGE_UNCHANGED";
            }
        }
        bool OwnLabTerminal<T>(Gun g,string key,T value)
        {
            T current;
            if(!TryReadTerminal(g.Block,key,out current)) return false;
            if(!g.Lab.Saved.ContainsKey(key)) g.Lab.Saved[key]=current;
            if(!Equals(current,value) && !SetTerminal(g.Block,key,value)) return false;
            g.Lab.Owned[key]=value;
            T read; return TryReadTerminal(g.Block,key,out read) && Equals(read,value);
        }
        bool RestoreLabTerminals(LabGunState state)
        {
            bool ok=true;
            foreach(var pair in state.Owned.ToArray()) {
                bool restored=false;
                if(pair.Value is bool) {
                    bool current;
                    restored=TryReadTerminal(state.Block,pair.Key,out current) && (!Equals(current,pair.Value) ||
                        SetTerminal(state.Block,pair.Key,(bool)state.Saved[pair.Key]));
                } else if(pair.Value is long) {
                    long current;
                    restored=TryReadTerminal(state.Block,pair.Key,out current) && (!Equals(current,pair.Value) ||
                        SetTerminal(state.Block,pair.Key,(long)state.Saved[pair.Key]));
                }
                if(restored) { state.Owned.Remove(pair.Key); state.Saved.Remove(pair.Key); } else ok=false;
            }
            return ok;
        }
        internal bool ReleaseLabHardware(string why)
        {
            bool ok=true; labBurst.Reset();
            foreach(var g in guns) ReleaseOuter(g,"LAB_RELEASE");
            foreach(var state in labOwned) { ok &= state.Adapter.Restore(); ok &= RestoreLabTerminals(state); state.NativeConfigured=false; state.Mode=-1; state.Target=0; }
            foreach(var g in guns.Where(g=>g.RangeOwned)) {
                double current;
                if(!TrackingRangePolicy.TryObserved(wc.GetMaxWeaponRange(g.Entity,g.Part),out current)) { ok=false; continue; }
                if(Math.Abs(current-g.OwnedRange)>5) { g.RangeOwned=false; continue; }
                if(!wc.SetTrackingRange(g.Entity,(float)g.SavedRange)) { ok=false; continue; }
                double restored;
                if(TrackingRangePolicy.TryObserved(wc.GetMaxWeaponRange(g.Entity,g.Part),out restored) && Math.Abs(restored-g.SavedRange)<=5) g.RangeOwned=false;
                else ok=false;
            }
            // Returning from experimental firing is a known autonomous state, not trigger ON.
            foreach(var g in guns.Where(g=>g.Lab.NativeConfigured==false && g.NativeControlKnown && !g.NativeManual && g.Entity!=null)) {
                if(!labOwned.Contains(g.Lab)) continue;
                bool restored=SetTerminal(g.Block,"WC_Shoot Mode",0L) && wc.ToggleWeaponFire(g.Entity,g.Part,false);
                g.Lab.NativeConfigured=restored; ok &= restored;
            }
            if(!ok) LabFault="RESTORE_PENDING";
            return ok;
        }
        void ApplyLabControl()
        {
            var c=config(); bool all=true; int applied=0;
            labBurst.Enabled=labRecording && c.LabBurstEnabled;
            if(labBurst.Overflow) LabFault="BURST_CAPACITY_EXHAUSTED";
            if(!wc.Ready || !c.ControlEnabled || LabShipId!=labBoundShip || wc.Generation!=labGeneration)
            { ReleaseLabHardware("CONTROL_OR_CONTEXT_UNAVAILABLE"); return; }
            foreach(var old in labOwned.Where(s=>!guns.Any(g=>ReferenceEquals(g.Lab,s))).ToArray()) {
                if(old.Adapter.Restore() && RestoreLabTerminals(old)) labOwned.Remove(old); else LabFault="REMOVED_GUN_RESTORE_PENDING";
            }
            foreach(var g in guns) {
                if(g.Entity==null || g.Block==null || !g.Functional) continue;
                var state=g.Lab; state.Block=g.Block; if(!labOwned.Contains(state)) labOwned.Add(state);
                if(!g.NativeControlKnown || g.NativeManual) { if(!state.Adapter.Restore()) LabFault="RESTORE_PENDING"; state.State="MANUAL_OR_OBSERVER_YIELD"; continue; }
                bool ok=true;
                state.Burst=DecideLabBurst(g,c);
                long mode=(g.OuterSelected && c.PreemptiveFireEnabled) || !c.LabNativeOnly || state.Burst.Pause?2L:0L;
                if(!state.NativeConfigured || state.Mode!=mode) {
                    // Auto mode + trigger OFF lets WC's AiShooting path run. Trigger ON is a manual request.
                    ok=OwnLabTerminal(g,"WC_Shoot Mode",mode) && wc.ToggleWeaponFire(g.Entity,g.Part,false);
                    state.NativeConfigured=ok; if(ok) state.Mode=mode;
                }
                var component=wc.NativeObserver.Component(g.Entity);
                ok &= state.Adapter.Apply(component,g.Part,c,g.EntityId);
                state.State=c.LabBurstEnabled?state.Burst.Reason:"WC_AUTONOMOUS";
                if(c.LabNativeOnly && !(g.OuterSelected && c.PreemptiveFireEnabled)) g.Allowed=true;
                if(c.LabDistribution>=0) ok &= OwnLabTerminal(g,"WC_EnableFireDistribution",c.LabDistribution==1);
                if(FreshProfile(g)) { ResolveManagedCadence(g); ok &= ApplyGunRof(g,g.Rof); if(c.LabRangeControl) ok &= ApplyGunRange(g,c.LabNativeOnly?c.EngagementRangeMeters:g.DesiredRange); }
                // Optional outer fire retains physical-aim, heat, lifetime and request budgets.
                if(g.OuterSelected && c.PreemptiveFireEnabled) {
                    state.State="OUTER_PHYSICAL_BURST";
                } else if(!c.LabNativeOnly && !g.OuterSelected) {
                    ok &= OwnLabTerminal(g,"WC_Shoot Mode",2L);
                    ok &= ApplyFireCommand(g,tracks.FirstOrDefault(t=>t.Id==g.DecisionTrackId && !t.Resolved),g.Allowed);
                    state.State="ZEO_MANAGED";
                }
                if(!string.IsNullOrWhiteSpace(c.LabRequestGunIds) && LabSettings.ContainsId(c.LabRequestGunIds,g.EntityId)) ServiceLabRequest(g);
                else { state.Target=0; state.RequestDisabled=false; }
                var t=ObservedNativeTrack(g);
                System.Threading.Volatile.Write(ref g.Context,new ShotContext { Track=t==null?0:t.Id,Volley=t==null?0:t.VolleyIndex,
                    Projectile=t==null?0:t.ProjectileId,ObservedNativeProjectile=g.NativeProjectile,NativeRead=g.NativeRead,NativeFrame=g.TelemetryFrame,
                    LastObservedFrame=t==null?frame:t.LastFrame,CommandFrame=frame,Allowed=!state.Burst.Pause && (c.LabNativeOnly||g.Allowed),Gate=state.State,Mode=ExperimentMode });
                g.CommandResult=ok?"LAB_APPLIED_READBACK":"LAB_CAPABILITY_OR_WRITE_FAILED";
                all &= ok; if(ok) applied++;
            }
            directControlActive=all && applied>0; directControlState="LAB "+applied+"/"+guns.Count;
            labWriteFailures=all?0:labWriteFailures+1;
            if(labWriteFailures>=3) LabFault="HARDWARE_READBACK_FAILED";
        }
        BurstDecision DecideLabBurst(Gun g,PdcConfig c)
        {
            var s=g.Lab; var d=new BurstDecision();
            if(!c.LabBurstEnabled || !labRecording) { s.BurstTarget=0; return d; }
            d.Reason="BURST_INELIGIBLE_NATIVE";
            if(s.BurstDisabled) { d.Reason="BURST_NO_SHOT_NATIVE_FALLBACK"; return d; }
            if(!FreshProfile(g) || !g.Profile.SimpleBallistic || g.Profile.PreserveCadence || !g.Profile.Adjustable || !g.ShotHooked || !stableSensorHealthy || g.ShootingRead!="OK" || g.ReadyRead!="OK") return d;
            var t=ObservedNativeTrack(g);
            // Manual OFF may clear native ownership: keep only the bounded pause's
            // previously observed identity, with fresh sensor health/position.
            if(t==null && s.Burst.Pause && frame<s.Burst.PauseUntil)
                t=tracks.FirstOrDefault(x=>x.ProjectileId==s.BurstTarget && !x.Resolved);
            if(t==null || !FreshOuterTrack(t) || t.HealthRead!="OK" || frame-t.HealthFrame>6) {
                s.BurstTarget=0; s.BurstAllowFrame=-1; d.Reason="BURST_STALE_TARGET_NATIVE"; return d;
            }
            s.BurstTarget=t.ProjectileId;
            // Use full ammunition life as a conservative in-flight time allowance.
            // This is not an assertion that counted rounds are still alive or will hit.
            double flight=g.Profile.ShotLifeSeconds;
            bool urgent=t.Hull<=c.LabBurstUrgentRangeMeters || t.Tti<=c.LabBurstUrgentTtiSeconds ||
                t.Tti<=flight+c.LabBurstPauseSeconds+g.Profile.StartupSeconds+.25;
            d=labBurst.Decide(t.ProjectileId,frame,t.Health,g.Profile.HealthDamage,c.LabBurstInitialRounds,c.LabBurstTopUpRounds,c.LabBurstMargin,
                (int)Math.Ceiling(c.LabBurstPauseSeconds*60),urgent);
            if(d.Pause && d.SpentUnits>d.BudgetUnits+8*g.Profile.HealthDamage) {
                s.BurstDisabled=true; d.Pause=false; d.Reason="BURST_OVERSHOOT_NATIVE_FALLBACK";
                return d;
            }
            if(d.Pause || !g.ReadyValue || !g.NativeAligned) s.BurstAllowFrame=-1;
            else if(s.BurstAllowFrame<0 || g.Shots!=s.BurstShotMark) { s.BurstAllowFrame=frame; s.BurstShotMark=g.Shots; }
            else if(frame-s.BurstAllowFrame>Math.Max(60,(g.Profile.StartupSeconds+.5)*60)) {
                s.BurstDisabled=true; d.Pause=false; d.Reason="BURST_NO_SHOT_NATIVE_FALLBACK";
            }
            return d;
        }
        void ServiceLabRequest(Gun g)
        {
            var c=config(); var s=g.Lab;
            if(s.RequestDisabled) { s.State="REQUEST_TIMEOUT_NATIVE_FALLBACK"; return; }
            if(!FreshProfile(g) || !stableSensorHealthy || g.OuterSelected || g.NativeManual) return;
            var current=tracks.FirstOrDefault(t=>t.ProjectileId==s.Target && FreshOuterTrack(t));
            var candidate=tracks.Where(t=>FreshOuterTrack(t) && ManagedRangeAllows(g,t,c))
                .OrderBy(t=>t.Tti + (g.ScopeValid?OuterIntercept.Error(g.ScopeDirection,t.Pos-g.ScopeOrigin)/270:1))
                .ThenBy(t=>guns.Count(other=>other!=g && other.NativeProjectile==t.ProjectileId)).FirstOrDefault();
            if(candidate==null) { s.Target=0; return; }
            if(current!=null && (frame-s.LeaseFrame<c.LabLeaseSeconds*60 || current.Tti<=candidate.Tti+c.LabSwitchAdvantageSeconds)) candidate=current;
            if(s.Target!=candidate.ProjectileId) { s.Target=candidate.ProjectileId; s.LeaseFrame=frame; }
            if(s.RequestFrame>0 && frame-s.RequestFrame>=c.LabNoShotTimeoutSeconds*60 && g.Shots==s.ShotMark) {
                s.RequestDisabled=true; s.State="REQUEST_TIMEOUT_NATIVE_FALLBACK";
                wc.ToggleWeaponFire(g.Entity,g.Part,false); OwnLabTerminal(g,"WC_Shoot Mode",0L); return;
            }
            // Never repeatedly request while WC is still acquiring/firing an accepted cycle.
            if(frame-s.RequestFrame<c.LabRequestIntervalSeconds*60 || (s.RequestFrame>0 && g.Shots==s.ShotMark)) return;
            string result=wc.RequestProjectileShot(g.Entity,g.Part,s.Target); s.State="ID_REQUEST_"+result;
            if(result=="ACCEPTED") { s.RequestFrame=frame; s.ShotMark=g.Shots; }
        }
        void ShadowIntercept(Gun g)
        {
            var t=ObservedNativeTrack(g); g.Lab.ShadowError=g.Lab.ShadowFlight=null;
            if(t==null || !g.ScopeValid || !g.Profile.Valid || !g.Profile.SimpleBallistic) { g.Lab.ShadowState="NO_FRESH_BALLISTIC_TARGET"; return; }
            try {
                double flight; Vector3D aim;
                var velocity=controller.GetShipVelocities().LinearVelocity;
                if(!LabPredictionSolver.Solve(t.Pos-g.ScopeOrigin,t.StateVelocity-velocity,t.Acceleration,g.Profile.ShotSpeed,g.Profile.ShotLifeSeconds,out aim,out flight))
                { g.Lab.ShadowState="NO_INTERCEPT"; return; }
                g.Lab.ShadowError=Finite(OuterIntercept.Error(g.ScopeDirection,aim)); g.Lab.ShadowFlight=flight;
                g.Lab.ShadowState="ACCELERATION_MODEL_OBSERVATION_ONLY_NO_GRAVITY";
            } catch { g.Lab.ShadowState="UNAVAILABLE"; }
        }
        internal void CloseLabDiagnostics() { labRecording=false; labDamage.Unhook(); FlushLabDamage(); WriteLabTotals(); LabLog.Flush(); LabLog.Dispose(); }
        internal void StopLabRecording() { if(testArmed) AbortTest(); CloseLabDiagnostics(); }
        internal string LabDamageStatus { get { return labDamage.Status; } }
        internal long LabDroppedDamage { get { return labDamage.Dropped; } }
    }
    internal static class LabPredictionSolver
    {
        // Bounded numerical intercept for shadow comparisons. Acceleration is a
        // local estimate, not knowledge of the torpedo's future guidance decisions.
        internal static bool Solve(Vector3D r,Vector3D v,Vector3D a,double speed,double life,out Vector3D aim,out double flight)
        {
            aim=Vector3D.Zero; flight=0;
            if(!BankPlanner.Finite(r)||!BankPlanner.Finite(v)||!BankPlanner.Finite(a)||!BankPlanner.Finite(speed)||speed<=0||!BankPlanner.Finite(life)||life<=0) return false;
            double lo=0, hi=0; bool bracket=false;
            for(int i=1;i<=64;i++) { hi=life*i/64; var p=r+v*hi+.5*a*hi*hi; if(p.Length()<=speed*hi) { bracket=true; break; } lo=hi; }
            if(!bracket) return false;
            for(int i=0;i<24;i++) { double mid=(lo+hi)/2; if((r+v*mid+.5*a*mid*mid).Length()>speed*mid) lo=mid; else hi=mid; }
            flight=hi; aim=r+v*hi+.5*a*hi*hi; return BankPlanner.Finite(aim) && aim.LengthSquared()>1e-10;
        }
    }
}


