using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;

namespace ZeoPDC
{
    [DataContract] internal sealed class LiveLifecycleRow
    {
        [DataMember] public string segment_id, session_id, config_revision, tracker_epoch, track_id, projectile_id;
        [DataMember] public int first_tick, last_tick;
        [DataMember] public int? last_position_tick;
        [DataMember] public double? min_observed_hull_m;
        [DataMember] public string state, observation_count, lost_transitions, reappearances;
        [DataMember] public string last_native_state, native_state_read_status, first_non_alive_native_state;
        [DataMember] public int? last_native_state_tick, native_state_read_tick, first_non_alive_native_state_tick;
        [DataMember] public string evidence = "TRACK_OBSERVATIONS_NOT_CONFIRMED_INTERCEPTS_OR_IMPACTS";
        internal LiveLifecycleRow Copy() { return (LiveLifecycleRow)MemberwiseClone(); }
    }
    [DataContract] internal sealed class LiveLifecycle
    {
        [DataMember] public string schema = "zeo.pdc.lifecycle.v1";
        [DataMember] public string scope = "CUMULATIVE_CURRENT_LIVE_SESSION_CONFIG_SEGMENTS";
        [DataMember] public string timebase = TelemetryProvenance.Timebase;
        [DataMember] public string coverage_status, total_observations, retained_observations, dropped_observations, dropped_segments;
        [DataMember] public int max_segments = 512;
        [DataMember] public LiveLifecycleRow[] segments;
    }
    internal sealed partial class LiveTelemetryFeed
    {
        readonly Dictionary<string, LiveLifecycleRow> lifecycle = new Dictionary<string, LiveLifecycleRow>();
        readonly Queue<string> lifecycleOrder = new Queue<string>();
        long lifecycleSequence, lifecycleTotal, lifecycleDropped, lifecycleEvictions;
        static string Exact(long n) { return n.ToString(CultureInfo.InvariantCulture); }
        static string Increment(string n) { return Exact(long.Parse(n, CultureInfo.InvariantCulture) + 1); }
        void ClearLifecycle()
        {
            lifecycle.Clear(); lifecycleOrder.Clear();
            lifecycleSequence = lifecycleTotal = lifecycleDropped = lifecycleEvictions = 0;
        }
        void DropLifecycleKey(string key)
        {
            LiveLifecycleRow row;
            if (!lifecycle.TryGetValue(key, out row)) return;
            lifecycleDropped += long.Parse(row.observation_count, CultureInfo.InvariantCulture);
            lifecycleEvictions++; lifecycle.Remove(key);
        }
        void DropLifecycleRevision(string revision)
        {
            foreach (string key in lifecycle.Where(p => p.Value.config_revision == revision).Select(p => p.Key).ToArray()) DropLifecycleKey(key);
            var kept = lifecycleOrder.Where(lifecycle.ContainsKey).ToArray();
            lifecycleOrder.Clear(); foreach (string key in kept) lifecycleOrder.Enqueue(key);
        }
        public void ObserveTrackLifecycle(int tick, int epoch, int track, ulong projectile, bool stable, int positionTick, double hull, bool lost)
        {
            lock (gate)
            {
                if (!enabled || shipId == null || !stable || projectile == 0 || configurations.Count == 0) return;
                string revision = configurations[configurations.Count - 1].config_revision;
                string key = revision + "/" + Exact(epoch) + "/" + Exact(track) + "/" + projectile.ToString(CultureInfo.InvariantCulture);
                LiveLifecycleRow row;
                if (!lifecycle.TryGetValue(key, out row))
                {
                    if (lifecycle.Count >= 512) DropLifecycleKey(lifecycleOrder.Dequeue());
                    row = new LiveLifecycleRow { segment_id = Exact(++lifecycleSequence), session_id = sessionId, config_revision = revision,
                        tracker_epoch = Exact(epoch), track_id = Exact(track), projectile_id = projectile.ToString(CultureInfo.InvariantCulture),
                        first_tick = tick, last_tick = tick, observation_count = "0", lost_transitions = "0", reappearances = "0" };
                    lifecycle.Add(key, row); lifecycleOrder.Enqueue(key);
                }
                string state = lost ? "LOST_UNCONFIRMED" : "OBSERVED";
                if (lost && row.state != state) row.lost_transitions = Increment(row.lost_transitions);
                if (!lost && row.state == "LOST_UNCONFIRMED") row.reappearances = Increment(row.reappearances);
                row.state = state; row.last_tick = tick;
                // A loss declaration must not turn an old position into a fresh
                // observation or import a prior configuration's closest approach.
                if (!lost && positionTick == tick && !double.IsNaN(hull) && !double.IsInfinity(hull) && hull >= 0)
                {
                    row.last_position_tick = positionTick;
                    row.min_observed_hull_m = row.min_observed_hull_m.HasValue ? Math.Min(row.min_observed_hull_m.Value, hull) : hull;
                }
                row.observation_count = Increment(row.observation_count); lifecycleTotal++;
            }
        }
        // Diagnostic annotation of an existing current observation. This neither
        // creates segments nor increments position/loss observation counters.
        public void ObserveNativeState(int tick, int epoch, int track, ulong projectile,
            string nativeState, string status, int observedTick)
        {
            lock (gate)
            {
                if (!enabled || shipId == null || projectile == 0 || configurations.Count == 0) return;
                string revision = configurations[configurations.Count - 1].config_revision;
                string key = revision + "/" + Exact(epoch) + "/" + Exact(track) + "/" + projectile.ToString(CultureInfo.InvariantCulture);
                LiveLifecycleRow row;
                if (!lifecycle.TryGetValue(key, out row) || row.last_tick != tick || row.state != "OBSERVED") return;
                row.native_state_read_tick = tick;
                row.native_state_read_status = string.IsNullOrEmpty(status) ? "UNREAD" : status.Length > 64 ? "INVALID_STATUS" : status;
                if (status != "OK" || observedTick != tick || string.IsNullOrEmpty(nativeState) || nativeState.Length > 64)
                {
                    if (status == "OK") row.native_state_read_status = "INVALID_OR_STALE_STATE";
                    return;
                }
                row.last_native_state = nativeState; row.last_native_state_tick = observedTick;
                // Non-Alive includes synchronization and other states; it is NOT
                // a confirmed destruction, impact, or cause-of-death classifier.
                if (nativeState != "Alive" && !row.first_non_alive_native_state_tick.HasValue)
                { row.first_non_alive_native_state = nativeState; row.first_non_alive_native_state_tick = observedTick; }
            }
        }
        LiveLifecycle LifecycleSnapshot()
        {
            return new LiveLifecycle { coverage_status = lifecycleEvictions == 0 ? "RETAINED_RECORDED_OBSERVATIONS" : "TRUNCATED_RECORDED_OBSERVATIONS",
                total_observations = Exact(lifecycleTotal), retained_observations = Exact(lifecycleTotal - lifecycleDropped),
                dropped_observations = Exact(lifecycleDropped), dropped_segments = Exact(lifecycleEvictions),
                segments = lifecycleOrder.Select(k => lifecycle[k].Copy()).ToArray() };
        }
    }
}
