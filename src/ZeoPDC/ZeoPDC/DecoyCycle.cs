using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace ZeoPDC
{
    internal sealed class DecoyPort
    {
        public long Id;
        public bool Active=true;
        public Func<string> Read;
        public Action<string> Write;
    }
    [DataContract] internal sealed class DecoyLease
    {
        [DataMember] public long Id;
        [DataMember] public string Original, Owned;
    }
    // No combat or targeting mutations: only friendly decoy category advertisements.
    internal sealed class DecoyCycle
    {
        readonly Action<List<DecoyLease>> save;
        readonly Dictionary<long,DecoyLease> leases;
        readonly Dictionary<long,DecoyPort> ports=new Dictionary<long,DecoyPort>();
        readonly HashSet<long> yielded=new HashSet<long>();
        bool recovering=true;
        int nextFrame, phase;
        public int Managed, Eligible;
        public string Status="OFF";
        public DecoyCycle(IEnumerable<DecoyLease> prior,Action<List<DecoyLease>> persist)
        { leases=(prior??new DecoyLease[0]).GroupBy(x=>x.Id).ToDictionary(x=>x.Key,x=>x.Last()); save=persist; }
        void Persist() { save(leases.Values.ToList()); }
        internal static bool CategoryText(string text)
        { int v; return string.IsNullOrWhiteSpace(text) || (int.TryParse(text,out v)&&v>=1&&v<=7); }
        bool Restore(long id)
        {
            try {
            DecoyPort p; if(!ports.TryGetValue(id,out p)) return false;
            var l=leases[id]; var current=p.Read();
            if(current==l.Owned) { p.Write(l.Original); if(p.Read()!=l.Original) return false; }
            // User/other mod edits win over our rollback.
            leases.Remove(id); Persist(); return true;
            } catch { return false; }
        }
        public void Step(IEnumerable<DecoyPort> visible,bool enabled,int frame,double seconds)
        {
            try {
                var all=visible.ToArray();
                var list=all.Where(x=>x.Active).OrderBy(x=>x.Id).Take(64).ToArray(); Eligible=list.Length; Managed=0;
                foreach(var p in all) ports[p.Id]=p;
                var ids=new HashSet<long>(list.Select(x=>x.Id));
                foreach(var id in leases.Keys.ToArray())
                    if(!enabled || recovering || !ids.Contains(id)) Restore(id);
                if(recovering && leases.Count>0) { Status="RESTORE PENDING"; return; }
                recovering=false;
                if(!enabled) { nextFrame=0;phase=0;yielded.Clear();Status=leases.Count>0?"RESTORE PENDING":"OFF / RESTORED"; return; }
                if(frame<nextFrame) { Managed=leases.Keys.Count(ids.Contains);Status=Managed+" CYCLING / "+seconds.ToString("0.0")+"s";return; }
                nextFrame=frame+(int)Math.Ceiling(Math.Max(.5,seconds)*60);
                for(int i=0;i<list.Length;i++) {
                    var p=list[i]; if(yielded.Contains(p.Id)) continue;
                    var current=p.Read(); DecoyLease l;
                    if(leases.TryGetValue(p.Id,out l)) {
                        if(current!=l.Owned) { leases.Remove(p.Id);yielded.Add(p.Id);Persist();continue; }
                    } else {
                        if(!CategoryText(current)) { yielded.Add(p.Id);continue; }
                        l=new DecoyLease { Id=p.Id,Original=current,Owned=current }; leases[p.Id]=l;
                    }
                    l.Owned=(1+(phase+i)%7).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    Persist(); // durable intent precedes mutation, including partial-success writes
                    p.Write(l.Owned);
                    if(p.Read()!=l.Owned) throw new InvalidOperationException("Decoy category readback failed");
                    Managed++;
                }
                phase=(phase+1)%7;
                Status=Managed+" CYCLING / "+seconds.ToString("0.0")+"s"+(yielded.Count>0?" / "+yielded.Count+" YIELDED":"");
            } catch { recovering=true; Status="FAULT / RESTORE PENDING"; }
        }
    }
    internal sealed class CriticalHeatState
    {
        public bool Active;
        public int Entered;
        public void Update(bool valid,double heat,int frame,PdcConfig c)
        {
            if(!valid||!c.HeatWarningEnabled||double.IsNaN(heat)||double.IsInfinity(heat)) { Active=false;return; }
            if(!Active&&heat>=c.HeatWarningPercent) { Active=true;Entered=frame; }
            else if(Active&&heat<=c.HeatWarningResetPercent) Active=false;
        }
    }
}
