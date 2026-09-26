using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Threading;

namespace ZeoPDC
{
    [DataContract] internal sealed class LiveObservation
    {
        [DataMember] public int? tick;
        [DataMember] public double? time_s, age_s;
        [DataMember] public string source, status, freshness;
        [DataMember] public int? source_tick = null;
        [DataMember] public double? source_time_s = null, source_age_s = null;
        [DataMember] public string source_freshness = "UNKNOWN";
        public static LiveObservation Read(int logged, int? observed, string source, string status = "OK")
        {
            bool valid = observed.HasValue && observed >= 0 && observed <= logged && status == "OK";
            return new LiveObservation { tick = valid ? observed : null, time_s = valid ? observed / 60.0 : null,
                age_s = valid ? (logged - observed) / 60.0 : null, source = source, status = status,
                freshness = valid ? (logged == observed ? "API_READ_THIS_TICK" : "RETAINED") : "UNKNOWN" };
        }
    }
    [DataContract] internal sealed class LiveTrack
    {
        [DataMember] public ThreatProfileObservation ammo_profile;
        [DataMember] public LiveObservation ammo_profile_observation;
        [DataMember] public string track_id, projectile_id, state;
        [DataMember] public double[] position, velocity_estimate;
        [DataMember] public double? hull_m, min_hull_m, tti_s, health;
        [DataMember] public LiveObservation observation, health_observation;
        [DataMember] public string native_projectile_state;
        [DataMember] public LiveObservation native_state_observation;
        [DataMember] public string velocity_kind = "FILTERED_ESTIMATE";
        [DataMember] public bool stable_id, counted_physical;
    }
    [DataContract] internal sealed class LiveGun
    {
        [DataMember] public string target_filters;
        [DataMember] public string weapon_profile_id, weapon_profile_revision, weapon_profile_status, weapon_subtype, weapon_ammo;
        [DataMember] public string cadence_reason, range_reason;
        [DataMember] public LiveObservation profile_observation;
        [DataMember] public double? native_rpm, effective_rpm, native_min_rof, native_min_range_m, native_max_range_m;
        [DataMember] public double? nominal_heat_per_s, cooling_per_s, projectile_health_damage;
        [DataMember] public int? projectiles_per_event, nominal_hits_required;
        [DataMember] public bool? rof_adjustable, simple_ballistic;
        [DataMember] public string preemptive_state, native_control_status;
        [DataMember] public ClientCapability client_capability;
        [DataMember] public ClientSettingLease client_setting;
        [DataMember] public string outer_aim_schema, outer_aim_state, outer_projectile_id, outer_ammo_profile, outer_intercept_state;
        [DataMember] public bool outer_aim_owned, outer_path_clear;
        [DataMember] public double? outer_flight_s, outer_travel_m;
        [DataMember] public string outer_cycle_requests, outer_releases, outer_release_status;
        [DataMember] public int? outer_release_tick, outer_native_reacquired_tick;
        [DataMember] public bool preemptive_burst_active, preemptive_heat_hold;
        [DataMember] public bool? native_aligned, native_manual;
        [DataMember] public double? scope_error_deg;
        [DataMember] public string preemptive_bursts, preemptive_callback_delta;
        [DataMember] public string decision_track_id, decision_basis;
        [DataMember] public string bank, bank_role, bank_reason;
        [DataMember] public string gun_id, native_projectile_id, assigned_track_id, command_result;
        [DataMember] public int part;
        [DataMember] public string shots;
        [DataMember] public bool functional, allowed;
        [DataMember] public bool shot_monitor_registered;
        [DataMember] public double? heat_pct, hp_pct, desired_rof, actual_rof, desired_range_m, actual_range_m;
        [DataMember] public int? ammo;
        [DataMember] public LiveObservation observation, heat_observation;
        [DataMember] public string native_read_status, ready_read_status, shooting_read_status;
        [DataMember] public bool? ready, shooting;
        [DataMember] public string association = "NATIVE_AND_SCHEDULER_OBSERVATIONS_NOT_HIT_CONFIRMATION";
    }
    [DataContract] internal sealed class LiveShip
    {
        [DataMember] public double[] position, velocity, right, up, backward, bounds_min, bounds_max;
        [DataMember] public bool? damage_enabled = null;
        [DataMember] public LiveObservation observation;
    }
    [DataContract] internal sealed class LiveEvent
    {
        [DataMember] public string event_seq, session_id, config_revision, type, gun_id, projectile_id, target_id;
        [DataMember] public int part, sim_tick;
        [DataMember] public double sim_time_s;
        [DataMember] public string timebase = TelemetryProvenance.Timebase;
        [DataMember] public double[] position;
        [DataMember] public LiveObservation observation;
        [DataMember] public string evidence = "MONITORED_SHOT_POINT_NOT_TRAJECTORY_OR_HIT";
    }
    [DataContract] internal sealed class LiveConfiguration
    {
        [DataMember] public string session_id, config_revision, effective_settings_sha256;
        [DataMember] public int effective_from_sim_tick;
        [DataMember] public string timebase = TelemetryProvenance.Timebase;
        [DataMember] public string requested_status = "NOT_RECORDED";
        [DataMember] public PdcConfig requested_settings = null, effective_settings;
        [DataMember] public string hardware_application_status = "USE_PER_GUN_COMMAND_AND_READBACK_EVIDENCE";
    }
    [DataContract] internal sealed class LiveLimits
    {
        [DataMember] public double max_publish_hz = 2;
        [DataMember] public int max_tracks = LiveTelemetryFeed.MaxTracks, max_guns = LiveTelemetryFeed.MaxGuns,
            max_events = LiveTelemetryFeed.MaxEvents, max_configurations = LiveTelemetryFeed.MaxConfigurations,
            max_file_bytes = LiveTelemetryFeed.MaxBytes;
    }
    [DataContract] internal sealed class LiveFrame
    {
        [DataMember] public string schema = "zeo.pdc.live.v1";
        [DataMember] public bool enabled;
        [DataMember] public bool synthetic = false;
        [DataMember] public string state, run_id, session_id, world_session_id, ship_id, publication_seq, published_utc, config_revision;
        [DataMember] public int sim_tick;
        [DataMember] public double sim_time_s;
        [DataMember] public string timebase = TelemetryProvenance.Timebase;
        [DataMember] public string source_timebase = null;
        [DataMember] public int? engine_sim_tick = null;
        [DataMember] public LiveConfiguration[] configurations;
        [DataMember] public LiveTrack[] tracks = new LiveTrack[0];
        [DataMember] public LiveGun[] guns = new LiveGun[0];
        [DataMember] public LiveShip ship;
        [DataMember] public LiveEvent[] events;
        [DataMember] public string event_first_seq, event_last_seq, dropped_events, total_monitored_shots;
        [DataMember] public int truncated_tracks, truncated_guns;
        [DataMember] public int omitted_events_for_size;
        [DataMember] public string writer_error;
        [DataMember] public bool? shot_monitor_available;
        [DataMember] public int monitored_gun_parts, unmonitored_gun_parts;
        [DataMember] public LiveLimits limits = new LiveLimits();
        [DataMember] public LiveReview review;
        [DataMember] public LiveLifecycle lifecycle;
    }

    // No game API or commands here. Only detached snapshots cross to a single
    // coalescing worker; serialization and file I/O never run on the game thread.
    internal sealed partial class LiveTelemetryFeed
    {
        internal const int MaxTracks = 512, MaxGuns = 128, MaxEvents = 2048, MaxConfigurations = 32, MaxBytes = 2 * 1024 * 1024;
        readonly object gate = new object(), writeGate = new object();
        readonly Queue<LiveEvent> events = new Queue<LiveEvent>();
        readonly List<LiveConfiguration> configurations = new List<LiveConfiguration>();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly string path, run = Guid.NewGuid().ToString("N");
        object world;
        string worldId, sessionId, shipId, configJson;
        bool enabled, initialized, worker, disabledPublished;
        volatile bool everPublished;
        double nextPublish;
        long eventSequence, publicationSequence, dropped, totalShots;
        int configNumber;
        LiveFrame pending;
        string writerError;
        public LiveTelemetryFeed(string dataDir) { path = Path.Combine(dataDir, "Live", "current.json"); everPublished=File.Exists(path); }
        public string Pathname { get { return path; } }
        public string WorldIdentity { get { lock (gate) return worldId; } }
        public bool IsEnabled { get { lock (gate) return enabled; } }
        public void Context(bool on, object worldIdentity, string controlledShip, int tick, PdcConfig cfg)
        {
            lock (gate)
            {
                bool worldChanged = !ReferenceEquals(world, worldIdentity);
                bool changed = !initialized || worldChanged || shipId != controlledShip || enabled != on;
                if (changed)
                {
                    if (!initialized || worldChanged) worldId = Guid.NewGuid().ToString("N");
                    world = worldIdentity; shipId = controlledShip; enabled = on; initialized = true;
                    sessionId = Guid.NewGuid().ToString("N"); events.Clear(); configurations.Clear();
                    configJson = null; configNumber = 0; eventSequence = dropped = totalShots = 0;
                    ClearReview();
                    ClearLifecycle();
                    disabledPublished = false;
                }
                if (on && changed) ConfigurationLocked(cfg, tick);
            }
        }
        public void ObserveConfiguration(PdcConfig cfg, int tick)
        { lock (gate) { if (enabled) ConfigurationLocked(cfg, tick); } }
        void ConfigurationLocked(PdcConfig cfg, int tick)
        {
            byte[] serialized = JsonIo.ToBytes(cfg);
            string current = Convert.ToBase64String(serialized);
            if (current == configJson) return;
            string hash;
            using (var sha=SHA256.Create()) hash=BitConverter.ToString(sha.ComputeHash(serialized)).Replace("-", "").ToLowerInvariant();
            configurations.Add(new LiveConfiguration { session_id = sessionId, config_revision = "cfg-" + (++configNumber).ToString("D6", CultureInfo.InvariantCulture),
                effective_from_sim_tick = tick, effective_settings_sha256 = hash, effective_settings = JsonIo.FromBytes<PdcConfig>(serialized) });
            configJson = current;
            if (configurations.Count > MaxConfigurations)
            {
                string removed = configurations[0].config_revision; configurations.RemoveAt(0);
                DropReviewRevision(removed);
                DropLifecycleRevision(removed);
                while (events.Count > 0 && events.Peek().config_revision == removed) { events.Dequeue(); dropped++; }
            }
        }
        public bool Due()
        {
            lock (gate)
            {
                if (!enabled && (disabledPublished || !everPublished)) return false;
                if (clock.Elapsed.TotalSeconds < nextPublish) return false;
                nextPublish = clock.Elapsed.TotalSeconds + .5;
                if (!enabled) disabledPublished=true;
                return true;
            }
        }
        public void Shot(int tick, long gun, int part, ulong projectile, long target, double[] position, string expectedWorld = null, string expectedShip = null)
        {
            lock (gate)
            {
                if (!enabled || shipId == null || configurations.Count == 0) return;
                if ((expectedWorld != null && expectedWorld != worldId) || (expectedShip != null && expectedShip != shipId)) return;
                totalShots++;
                var point = new LiveEvent { event_seq = (++eventSequence).ToString(CultureInfo.InvariantCulture), session_id = sessionId,
                    config_revision = configurations[configurations.Count-1].config_revision, type = "SHOT_MONITOR_POINT", gun_id = gun.ToString(CultureInfo.InvariantCulture),
                    part = part, projectile_id = projectile.ToString(CultureInfo.InvariantCulture), target_id = target == 0 ? null : target.ToString(CultureInfo.InvariantCulture),
                    sim_tick = tick, sim_time_s = tick / 60.0, position = position==null?null:(double[])position.Clone(),
                    observation = LiveObservation.Read(tick,tick,"AddMonitorProjectile") };
                ReviewShot(point); events.Enqueue(point);
                if (events.Count > MaxEvents) { events.Dequeue(); dropped++; }
            }
        }
        public void Publish(LiveFrame frame)
        {
            lock (gate)
            {
                if (frame.tracks.Length>MaxTracks) { frame.truncated_tracks+=frame.tracks.Length-MaxTracks; frame.tracks=frame.tracks.Take(MaxTracks).ToArray(); }
                if (frame.guns.Length>MaxGuns) { frame.truncated_guns+=frame.guns.Length-MaxGuns; frame.guns=frame.guns.Take(MaxGuns).ToArray(); }
                frame.enabled=enabled; frame.run_id=run; frame.session_id=sessionId; frame.world_session_id=worldId; frame.ship_id=shipId;
                frame.publication_seq=(++publicationSequence).ToString(CultureInfo.InvariantCulture); frame.published_utc=DateTime.UtcNow.ToString("o");
                frame.sim_time_s=frame.sim_tick/60.0; frame.config_revision=configurations.LastOrDefault()?.config_revision;
                frame.configurations=configurations.ToArray();
                frame.writer_error=writerError;
                ReviewSamples(frame);
                frame.events=events.ToArray(); frame.event_first_seq=frame.events.FirstOrDefault()?.event_seq;
                frame.event_last_seq=frame.events.LastOrDefault()?.event_seq; frame.dropped_events=dropped.ToString(CultureInfo.InvariantCulture);
                frame.total_monitored_shots=totalShots.ToString(CultureInfo.InvariantCulture);
                frame.review=ReviewSnapshot();
                frame.lifecycle=LifecycleSnapshot();
            }
            lock (writeGate)
            {
                everPublished=true;
                pending=frame;
                if (!worker) { worker=true; ThreadPool.QueueUserWorkItem(_=>Drain()); }
            }
        }
        void Drain()
        {
            while(true)
            {
                LiveFrame snapshot;
                lock (writeGate)
                {
                    snapshot=pending; pending=null;
                    if (snapshot==null) { worker=false; Monitor.PulseAll(writeGate); return; }
                }
                try
                {
                    byte[] bytes=SerializeBounded(snapshot);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    string temp=path+".tmp";
                    using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)) { stream.Write(bytes,0,bytes.Length); stream.Flush(true); }
                    if(File.Exists(path)) File.Replace(temp,path,null,true); else File.Move(temp,path);
                    lock(gate) writerError=null;
                }
                catch(Exception ex) { lock(gate) writerError=ex.GetType().Name+": "+ex.Message; }
            }
        }
        static byte[] SerializeBounded(LiveFrame frame)
        {
            while(true)
            {
                byte[] bytes=JsonIo.ToBytes(frame);
                if(bytes.Length<=MaxBytes) return bytes;
                if(frame.events.Length>0)
                {
                    int remove=Math.Max(1,frame.events.Length/4); frame.events=frame.events.Skip(remove).ToArray();
                    frame.omitted_events_for_size+=remove;
                    frame.event_first_seq=frame.events.FirstOrDefault()?.event_seq; frame.event_last_seq=frame.events.LastOrDefault()?.event_seq;
                    continue;
                }
                if(frame.tracks.Length>0) { frame.truncated_tracks+=frame.tracks.Length; frame.tracks=new LiveTrack[0]; continue; }
                if(frame.guns.Length>0) { frame.truncated_guns+=frame.guns.Length; frame.guns=new LiveGun[0]; continue; }
                throw new InvalidDataException("Live telemetry metadata exceeds byte ceiling");
            }
        }
        public bool Flush(int milliseconds)
        {
            var timer=Stopwatch.StartNew();
            lock(writeGate)
            {
                while(worker && timer.ElapsedMilliseconds<milliseconds) Monitor.Wait(writeGate,Math.Max(1,milliseconds-(int)timer.ElapsedMilliseconds));
                return !worker;
            }
        }
    }
}
