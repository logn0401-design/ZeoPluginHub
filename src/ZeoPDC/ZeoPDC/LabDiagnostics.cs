using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game.Entity;
using VRageMath;

namespace ZeoPDC
{
    [DataContract] internal sealed class LabDamageTotal
    { [DataMember] public string key,events; [DataMember] public double damage; }
    [DataContract] internal sealed class LabDamage
    {
        [DataMember] public string utc, bullet, gun, target;
        [DataMember] public int part, tick, revision;
        [DataMember] public double damage;
        [DataMember] public double[] position;
    }
    internal sealed class LabDamageMonitor
    {
        readonly long registrationId=BitConverter.ToInt64(Guid.NewGuid().ToByteArray(),0);
        readonly Queue<LabDamage> pending=new Queue<LabDamage>();
        readonly object gate=new object();
        HashSet<long> owned=new HashSet<long>();
        Action<long,int,Action<ListReader<MyTuple<ulong,long,int,MyEntity,MyEntity,ListReader<MyTuple<Vector3D,object,float>>>>>> handler;
        public long Received, Dropped, Errors;
        public int Tick, Revision;
        public string Status="UNREGISTERED";
        public bool Hook(Action<long,int,Action<ListReader<MyTuple<ulong,long,int,MyEntity,MyEntity,ListReader<MyTuple<Vector3D,object,float>>>>>> next)
        {
            if(handler==next && handler!=null) return true;
            Unhook(); if(next==null) { Status="UNSUPPORTED"; return false; }
            try { next(registrationId,1,Receive); handler=next; Status="REGISTERED_UNVALIDATED"; return true; }
            catch { Status="REGISTER_FAILED"; return false; }
        }
        public void SetOwned(IEnumerable<long> ids) { lock(gate) owned=new HashSet<long>(ids); }
        void Receive(ListReader<MyTuple<ulong,long,int,MyEntity,MyEntity,ListReader<MyTuple<Vector3D,object,float>>>> events)
        {
            lock(gate) try {
                foreach(var e in events) {
                    long gun=e.Item4==null?0:e.Item4.EntityId;
                    if(!owned.Contains(gun)) continue;
                    foreach(var hit in e.Item6) {
                        if(!(hit.Item2 is ulong)) continue; // grid/block damage is not a torpedo hit
                        Received++;
                        if(pending.Count>=4096) { Dropped++; continue; }
                        pending.Enqueue(new LabDamage { utc=DateTime.UtcNow.ToString("o"),bullet=e.Item1.ToString(),gun=gun.ToString(),part=e.Item3,
                            target=((ulong)hit.Item2).ToString(),damage=hit.Item3,position=new[]{hit.Item1.X,hit.Item1.Y,hit.Item1.Z},tick=Tick,revision=Revision });
                    }
                }
            } catch { Errors++; }
        }
        public LabDamage[] Drain() { lock(gate) { var a=pending.ToArray(); pending.Clear(); return a; } }
        public void Unhook()
        {
            if(handler==null) return;
            try { handler(registrationId,0,Receive); handler=null; Status="UNREGISTERED"; }
            catch { Status="UNREGISTER_PENDING"; }
        }
    }
    internal sealed class LabJournal : IDisposable
    {
        StreamWriter writer;
        public string Folder;
        public long Events, WriteErrors;
        public void Open(string root)
        {
            Dispose(); Folder=Path.Combine(root,"Lab",DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ")+"-"+Guid.NewGuid().ToString("N").Substring(0,8));
            Directory.CreateDirectory(Folder);
            writer=new StreamWriter(new FileStream(Path.Combine(Folder,"events.jsonl"),FileMode.CreateNew,FileAccess.Write,FileShare.Read),new UTF8Encoding(false));
        }
        public void Write<T>(string kind,int tick,int revision,T value)
        {
            if(writer==null) return;
            try { writer.WriteLine("{\"seq\":\""+(++Events)+"\",\"utc\":\""+DateTime.UtcNow.ToString("o")+"\",\"kind\":\""+kind+"\",\"tick\":"+tick+",\"revision\":"+revision+",\"data\":"+Encoding.UTF8.GetString(JsonIo.ToBytes(value))+"}"); }
            catch { WriteErrors++; }
        }
        public void Flush() { try { writer?.Flush(); } catch { WriteErrors++; } }
        public void Dispose() { try { writer?.Dispose(); } catch { WriteErrors++; } writer=null; }
    }
    [DataContract] internal sealed class LabGunSample
    {
        [DataMember] public string gun, assigned, acquired, requested, state, adapter, predictor, prediction;
        [DataMember] public int part, sample_tick, native_age_ticks;
        [DataMember] public bool? ready, firing, aligned;
        [DataMember] public double? heat, spread_deg, tolerance_deg, shadow_error_deg, shadow_flight_s;
        [DataMember] public bool? advanced_solver;
        [DataMember] public string callbacks;
        [DataMember] public double? rof, range, effective_rpm;
        [DataMember] public string cadence_reason,burst_target,burst_rounds;
        [DataMember] public bool burst_pause;
        [DataMember] public double burst_spent_units,burst_budget_units;
        [DataMember] public int burst_pause_until;
    }
    [DataContract] internal sealed class LabCohortReport
    {
        [DataMember] public int volley, revision, count, leakers, first_tick, last_tick;
        [DataMember] public bool comparable;
        [DataMember] public string reason, grouping="INFERRED_32_ID_GROUP_NOT_LAUNCH_ID";
        [DataMember] public double sampled_peak_heat;
        [DataMember] public string[] projectile_ids;
    }
}


