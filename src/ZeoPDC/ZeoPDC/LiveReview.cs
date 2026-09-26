using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;

namespace ZeoPDC
{
    [DataContract] internal sealed class LiveReviewCounter
    {
        [DataMember] public string session_id, config_revision, gun_id;
        [DataMember] public int part, first_sim_tick, last_sim_tick;
        [DataMember] public string first_event_seq, last_event_seq;
        [DataMember] public string shot_count="0", sample_count="0", heat_sample_count="0", heat_unknown_count="0", ready_sample_count="0", ready_unknown_count="0", shooting_sample_count="0", shooting_unknown_count="0";
        [DataMember] public double? min_sampled_heat_pct, max_sampled_heat_pct;
        public LiveReviewCounter Snapshot() { return (LiveReviewCounter)MemberwiseClone(); }
    }
    [DataContract] internal sealed class LiveReview
    {
        [DataMember] public string schema="zeo.pdc.review.v1", scope="CUMULATIVE_CURRENT_LIVE_SESSION";
        [DataMember] public string coverage_status;
        [DataMember] public string total_monitored_shots, retained_counter_shots, dropped_counter_shots, dropped_counter_groups, dropped_counter_samples;
        [DataMember] public string discarded_event_seq_min, discarded_event_seq_max;
        [DataMember] public int max_counter_groups=1024;
        [DataMember] public LiveReviewCounter[] counters;
        [DataMember] public string heat_scope="VALID_SAMPLES_AT_PUBLICATION_CADENCE_NOT_CONTINUOUS_PHYSICS_PEAK";
    }

    internal sealed partial class LiveTelemetryFeed
    {
        readonly List<LiveReviewCounter> counterOrder=new List<LiveReviewCounter>();
        readonly Dictionary<string,LiveReviewCounter> counters=new Dictionary<string,LiveReviewCounter>();
        long droppedCounterShots, droppedCounterGroups, droppedCounterSamples;
        long? discardedEventMin, discardedEventMax;
        static long Count(string number) { return long.Parse(number,CultureInfo.InvariantCulture); }
        static string CounterKey(string revision,string gun,int part) { return revision+"/"+gun+"/"+part.ToString(CultureInfo.InvariantCulture); }
        static void Increment(ref string number) { number=(Count(number)+1).ToString(CultureInfo.InvariantCulture); }
        void ClearReview()
        {
            counterOrder.Clear(); counters.Clear(); droppedCounterShots=droppedCounterGroups=droppedCounterSamples=0;
            discardedEventMin=discardedEventMax=null;
        }
        LiveReviewCounter Counter(string revision,string gun,int part,int tick)
        {
            string key=CounterKey(revision,gun,part); LiveReviewCounter c;
            if(!counters.TryGetValue(key,out c))
            {
                c=new LiveReviewCounter { session_id=sessionId,config_revision=revision,gun_id=gun,part=part,first_sim_tick=tick,last_sim_tick=tick };
                counters.Add(key,c); counterOrder.Add(c);
                if(counterOrder.Count>1024) DropCounter(counterOrder[0]);
            }
            c.last_sim_tick=tick; return c;
        }
        void DropCounter(LiveReviewCounter c)
        {
            droppedCounterGroups++; droppedCounterShots+=Count(c.shot_count); droppedCounterSamples+=Count(c.sample_count);
            if(c.first_event_seq!=null) { long n=Count(c.first_event_seq); discardedEventMin=discardedEventMin.HasValue?Math.Min(discardedEventMin.Value,n):n; }
            if(c.last_event_seq!=null) { long n=Count(c.last_event_seq); discardedEventMax=discardedEventMax.HasValue?Math.Max(discardedEventMax.Value,n):n; }
            counterOrder.Remove(c); counters.Remove(CounterKey(c.config_revision,c.gun_id,c.part));
        }
        void DropReviewRevision(string revision)
        { foreach(var c in counterOrder.Where(c=>c.config_revision==revision).ToArray()) DropCounter(c); }
        void ReviewShot(LiveEvent e)
        {
            var c=Counter(e.config_revision,e.gun_id,e.part,e.sim_tick); Increment(ref c.shot_count);
            if(c.first_event_seq==null) c.first_event_seq=e.event_seq; c.last_event_seq=e.event_seq;
        }
        void ReviewSamples(LiveFrame frame)
        {
            if(!enabled || configurations.Count==0 || (frame.state!="ACTIVE" && frame.state!="SOURCE_UNAVAILABLE")) return;
            string rev=configurations[configurations.Count-1].config_revision;
            foreach(var g in frame.guns)
            {
                var c=Counter(rev,g.gun_id,g.part,frame.sim_tick); Increment(ref c.sample_count);
                if(g.heat_pct.HasValue && BankPlanner.Finite(g.heat_pct.Value))
                {
                    Increment(ref c.heat_sample_count);
                    c.min_sampled_heat_pct=c.min_sampled_heat_pct.HasValue?Math.Min(c.min_sampled_heat_pct.Value,g.heat_pct.Value):g.heat_pct;
                    c.max_sampled_heat_pct=c.max_sampled_heat_pct.HasValue?Math.Max(c.max_sampled_heat_pct.Value,g.heat_pct.Value):g.heat_pct;
                }
                else Increment(ref c.heat_unknown_count);
                if(!g.ready.HasValue) Increment(ref c.ready_unknown_count); else if(g.ready.Value) Increment(ref c.ready_sample_count);
                if(!g.shooting.HasValue) Increment(ref c.shooting_unknown_count); else if(g.shooting.Value) Increment(ref c.shooting_sample_count);
            }
        }
        LiveReview ReviewSnapshot()
        {
            return new LiveReview { coverage_status=droppedCounterGroups==0?"COMPLETE_MONITORED_SHOT_COUNTS":"TRUNCATED_COUNTER_HISTORY",
                total_monitored_shots=totalShots.ToString(CultureInfo.InvariantCulture),retained_counter_shots=counterOrder.Sum(c=>Count(c.shot_count)).ToString(CultureInfo.InvariantCulture),
                dropped_counter_shots=droppedCounterShots.ToString(CultureInfo.InvariantCulture),dropped_counter_groups=droppedCounterGroups.ToString(CultureInfo.InvariantCulture),
                dropped_counter_samples=droppedCounterSamples.ToString(CultureInfo.InvariantCulture),
                discarded_event_seq_min=discardedEventMin?.ToString(CultureInfo.InvariantCulture),discarded_event_seq_max=discardedEventMax?.ToString(CultureInfo.InvariantCulture),
                counters=counterOrder.Select(c=>c.Snapshot()).ToArray() };
        }
    }
}
