using System;
using System.Globalization;
using System.Linq;
using Sandbox.ModAPI;
using VRageMath;

namespace ZeoPDC
{
    internal sealed partial class PdcEngine
    {
        private readonly LiveTelemetryFeed liveFeed;
        private int liveTrackerEpoch;
        private void ObserveLiveTrackLifecycle(Track t, bool lost)
        {
            if (t == null || !liveFeed.IsEnabled) return;
            liveFeed.ObserveTrackLifecycle(frame, liveTrackerEpoch, t.Id, t.ProjectileId, t.StableId,
                t.PositionObservationFrame, t.Hull, lost);
        }
        private static string Id(long id) { return id == 0 ? null : id.ToString(CultureInfo.InvariantCulture); }
        private static string Id(ulong id) { return id == 0 ? null : id.ToString(CultureInfo.InvariantCulture); }
        private static double? Finite(double value) { return BankPlanner.Finite(value) ? (double?)value : null; }
        private static double[] Vector(Vector3D value)
        { return BankPlanner.Finite(value.X) && BankPlanner.Finite(value.Y) && BankPlanner.Finite(value.Z) ? new[] { value.X, value.Y, value.Z } : null; }
        private void RefreshLiveContext()
        {
            try { liveFeed.Context(config().ContinuousTelemetryEnabled, MyAPIGateway.Session, grid == null ? null : Id(grid.EntityId), frame, config()); }
            catch (Exception ex) { if (frame % 60 == 0) log("Live context unavailable: " + ex.Message); }
        }
        public void PublishLiveTelemetry()
        {
            try
            {
                if (!liveFeed.Due()) return;
                var sample = new LiveFrame { sim_tick=frame, state=!liveFeed.IsEnabled ? "DISABLED" : !HasShip ? "NO_CONTROLLED_SHIP" : !wc.Ready ? "SOURCE_UNAVAILABLE" : "ACTIVE" };
                if (liveFeed.IsEnabled && HasShip)
                {
                    sample.shot_monitor_available=wc.Ready && wc.ShotMonitorReady;
                    sample.monitored_gun_parts=guns.Count(g=>g.ShotHooked);
                    sample.unmonitored_gun_parts=guns.Count-sample.monitored_gun_parts;
                    liveFeed.ObserveConfiguration(config(),frame);
                    var active = tracks.Where(t=>!t.Resolved).OrderBy(t=>t.Hull).ToArray();
                    sample.truncated_tracks=Math.Max(0,active.Length-LiveTelemetryFeed.MaxTracks);
                    sample.tracks=active.Take(LiveTelemetryFeed.MaxTracks).Select(t=>new LiveTrack {
                        ammo_profile=t.ThreatProfile,
                        ammo_profile_observation=LiveObservation.Read(frame,t.ThreatProfileFrame<0?(int?)null:t.ThreatProfileFrame,"WC_PROJECTILE_EFFECTIVE_AMMO",t.ThreatProfile?.status ?? "UNAVAILABLE"),
                        track_id=t.Id.ToString(CultureInfo.InvariantCulture), projectile_id=Id(t.ProjectileId), state=t.State,
                        position=Vector(t.Pos), velocity_estimate=Vector(t.Vel), stable_id=t.StableId, counted_physical=t.CountedPhysical,
                        hull_m=Finite(t.Hull), min_hull_m=Finite(t.MinHull), tti_s=Finite(t.Tti), health=t.HealthRead=="OK"?Finite(t.Health):null,
                        observation=LiveObservation.Read(frame,t.PositionObservationFrame<0?(int?)null:t.PositionObservationFrame,t.PositionObservationSource,t.PositionObservationFrame<0?"NOT_RECORDED":"OK"),
                        health_observation=LiveObservation.Read(frame,t.HealthFrame<0?(int?)null:t.HealthFrame,t.HealthSource,t.HealthRead),
                        native_projectile_state=t.NativeState,
                        native_state_observation=LiveObservation.Read(frame,t.NativeStateFrame<0?(int?)null:t.NativeStateFrame,"NATIVE_ACTIVE_PROJECTILES_STATE",t.NativeStateRead)
                    }).ToArray();
                    sample.truncated_guns=Math.Max(0,guns.Count-LiveTelemetryFeed.MaxGuns);
                    sample.guns=guns.Take(LiveTelemetryFeed.MaxGuns).Select(g=> {
                        double heat=double.NaN; bool heatOk=wc.Ready && wc.TryReadHeatPercent(g.Entity,g.Part,out heat);
                        var p=g.Profile!=null && g.Profile.Valid?g.Profile:null;
                        var target=tracks.FirstOrDefault(t=>t.Id==g.DecisionTrackId && !t.Resolved && t.HealthRead=="OK" && t.HealthFrame==frame);
                        return new LiveGun { client_capability=g.Client.Capability,client_setting=g.Client.Lease,target_filters="PLAYER_CONTROLLED",weapon_profile_id=p?.Identity,weapon_profile_revision=p?.Revision,weapon_profile_status=g.Profile.Status,
                            weapon_subtype=p?.Subtype,weapon_ammo=p?.Ammo,cadence_reason=g.CadenceReason,range_reason=g.RangeReason,
                            native_rpm=p==null?(double?)null:p.NativeRpm,effective_rpm=p==null?(double?)null:p.EffectiveRpm,
                            native_min_rof=p==null?(double?)null:p.MinimumRof,native_min_range_m=p==null?(double?)null:p.MinimumRange,
                            native_max_range_m=p==null?(double?)null:p.MaximumRange,nominal_heat_per_s=p==null?(double?)null:p.NominalHeatPerSecond,
                            cooling_per_s=p==null?(double?)null:p.CoolingPerSecond,projectile_health_damage=p==null?(double?)null:p.HealthDamage,
                            projectiles_per_event=p==null?(int?)null:p.ProjectilesPerEvent,rof_adjustable=p==null?(bool?)null:p.Adjustable,
                            simple_ballistic=p==null?(bool?)null:p.SimpleBallistic,
                            nominal_hits_required=target==null?null:WeaponProfilePolicy.NominalHits(p,target.Health),
                            profile_observation=LiveObservation.Read(frame,g.ProfileFrame<0?(int?)null:g.ProfileFrame,"WC_EFFECTIVE_CONSTANTS",g.Profile.Status),
                            gun_id=Id(g.EntityId),part=g.Part,shots=g.Shots.ToString(CultureInfo.InvariantCulture),
                            preemptive_state=g.OuterState, native_control_status=g.NativeControlStatus,
                            outer_aim_schema=wc.OuterAim.Status, outer_aim_state=g.OuterLease.State, outer_aim_owned=g.OuterLease.Owned,
                            outer_projectile_id=g.OuterTrack==0?null:g.OuterTrack.ToString(CultureInfo.InvariantCulture),
                            outer_ammo_profile=g.OuterProfile, outer_intercept_state=g.OuterSolution.Reason,
                            outer_path_clear=g.OuterPathClear, outer_flight_s=Finite(g.OuterSolution.FlightSeconds), outer_travel_m=Finite(g.OuterSolution.TravelMeters),
                            outer_cycle_requests=g.OuterCycleRequests.ToString(CultureInfo.InvariantCulture), outer_releases=g.OuterReleases.ToString(CultureInfo.InvariantCulture),
                            outer_release_status=g.OuterReleaseStatus, outer_release_tick=g.OuterReleaseFrame<0?(int?)null:g.OuterReleaseFrame,
                            outer_native_reacquired_tick=g.OuterNativeReacquiredFrame<0?(int?)null:g.OuterNativeReacquiredFrame,
                            preemptive_burst_active=g.Outer.Active, preemptive_heat_hold=g.Outer.HeatHold,
                            native_aligned=g.NativeControlKnown?(bool?)g.NativeAligned:null, native_manual=g.NativeControlKnown?(bool?)g.NativeManual:null,
                            scope_error_deg=Finite(g.OuterScopeError), preemptive_bursts=g.Outer.Bursts.ToString(CultureInfo.InvariantCulture),
                            preemptive_callback_delta=g.Outer.AcceptedCallbacks.ToString(CultureInfo.InvariantCulture),
                            bank=g.BankRole.Bank, bank_role=g.BankRole.Role, bank_reason=g.BankRole.Reason,
                            decision_track_id=g.DecisionTrackId==0?null:g.DecisionTrackId.ToString(CultureInfo.InvariantCulture), decision_basis=g.DecisionBasis,
                            functional=g.Functional, allowed=g.Allowed, shot_monitor_registered=g.ShotHooked, assigned_track_id=g.TrackId==0?null:g.TrackId.ToString(CultureInfo.InvariantCulture),
                            native_projectile_id=g.NativeRead=="PROJECTILE"?Id(g.NativeProjectile):null, command_result=g.CommandResult,
                            hp_pct=g.HpReadOk?Finite(g.Hp):null,heat_pct=heatOk?Finite(heat):null, ammo=g.TelemetryFrame<0?(int?)null:g.Ammo,
                            desired_rof=config().WeaponAwareEnabled && !g.ManagedRofWrite?null:Finite(g.Rof),actual_rof=g.RofReadOk?Finite(g.ActualRof):null,desired_range_m=Finite(g.DesiredRange),actual_range_m=g.RangeReadOk?Finite(g.ActualRange):null,
                            native_read_status=g.NativeRead,ready_read_status=g.ReadyRead,shooting_read_status=g.ShootingRead,
                            ready=g.ReadyRead=="OK"?(bool?)g.ReadyValue:null,shooting=g.ShootingRead=="OK"?(bool?)g.ShootingValue:null,
                            observation=LiveObservation.Read(frame,g.TelemetryFrame<0?(int?)null:g.TelemetryFrame,"GUN_TELEMETRY_POLL",g.TelemetryFrame<0?"NOT_RECORDED":"OK"),
                            heat_observation=LiveObservation.Read(frame,heatOk?(int?)frame:null,"WEAPONCORE_HEAT_AND_MAX_HEAT",heatOk?"OK":"UNAVAILABLE") };
                    }).ToArray();
                    var world=controller.WorldMatrix; var motion=controller.GetShipVelocities(); var bounds=grid.WorldAABB;
                    sample.ship=new LiveShip { position=Vector(world.Translation),velocity=Vector(motion.LinearVelocity),right=Vector(world.Right),up=Vector(world.Up),backward=Vector(world.Backward),
                        bounds_min=Vector(bounds.Min),bounds_max=Vector(bounds.Max), observation=LiveObservation.Read(frame,frame,"CONTROLLER_AND_PRIMARY_GRID") };
                }
                liveFeed.Publish(sample);
            }
            catch(Exception ex) { if(frame%60==0) log("Live telemetry publish failed: "+ex.Message); }
        }
        private void StopLiveTelemetry()
        {
            try { if(liveFeed.IsEnabled) { liveFeed.Publish(new LiveFrame{sim_tick=frame,state="STOPPED"}); liveFeed.Flush(1000); } }
            catch(Exception ex) { log("Live telemetry stop failed: "+ex.Message); }
        }
    }
}
