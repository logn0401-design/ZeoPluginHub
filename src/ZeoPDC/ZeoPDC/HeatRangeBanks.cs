using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using VRageMath;

namespace ZeoPDC
{
    // Range only: this policy never computes cadence, permissions or target IDs.
    internal static class HeatRangePolicy
    {
        internal sealed class Slot
        {
            public long Id;
            public string Group="UNKNOWN",Reason="WAIT";
            public bool Eligible,Ready,Confirmed;
            public double Heat,Actual,Outer,Middle,Inner,Desired,Changed;
            public int Rank=-1;
        }
        internal static void Plan(IList<Slot> slots,double now,double hold,double margin)
        {
            foreach(var group in slots.GroupBy(s=>s.Group)) {
                var all=group.ToArray();
                foreach(var s in all){s.Desired=s.Outer;s.Reason="FULL_COVERAGE";}
                // A missing/unknown/manual gun is not a cooling reserve. Partial
                // groups and all-hot groups keep their available outer coverage.
                if(all.Length<2 || all.Any(s=>!s.Eligible) || all.All(s=>s.Heat>=80))continue;
                var ordered=all.OrderBy(s=>s.Rank<0?int.MaxValue:s.Rank).ThenBy(s=>s.Id).ToArray();
                bool initialize=ordered.Any(s=>s.Rank<0)||ordered.Select(s=>s.Rank).Distinct().Count()!=ordered.Length;
                if(initialize) {
                    ordered=ordered.OrderBy(s=>s.Heat).ThenBy(s=>s.Id).ToArray();
                    for(int i=0;i<ordered.Length;i++){ordered[i].Rank=i;ordered[i].Changed=now;}
                }
                else if(now-ordered.Max(s=>s.Changed)>=hold) {
                    // One swap per hold period; do not churn the entire bank as
                    // nearly equal samples cross. A replacement must be ready.
                    Slot hotter=null,cooler=null;double gap=margin;
                    foreach(var a in ordered)foreach(var b in ordered)
                        if(a.Rank<b.Rank && b.Ready && a.Heat-b.Heat>=gap){hotter=a;cooler=b;gap=a.Heat-b.Heat;}
                    if(hotter!=null){int rank=hotter.Rank;hotter.Rank=cooler.Rank;cooler.Rank=rank;foreach(var s in ordered)s.Changed=now;}
                }
                int outerCount=(all.Length+2)/3, middleEnd=outerCount+(all.Length-outerCount+1)/2;
                foreach(var s in all){s.Desired=s.Rank<outerCount?s.Outer:s.Rank<middleEnd?s.Middle:s.Inner;s.Reason=s.Rank<outerCount?"OUTER":s.Rank<middleEnd?"MIDDLE":"INNER";}
                // Initial native full-range settings may supply coverage too, but
                // optimistic unacknowledged promotions must never count.
                bool ready=all.Where(s=>s.Rank<outerCount).All(s=>s.Ready&&s.Confirmed&&s.Actual+1>=s.Outer);
                bool promotionPending=all.Any(s=>s.Actual+1<s.Desired || !s.Confirmed);
                if(!ready || promotionPending)
                    foreach(var s in all)if(s.Desired+1<s.Actual){s.Desired=s.Actual;s.Reason="WAIT_FOR_REPLACEMENT";}
            }
        }
    }

    internal sealed class HeatRangeState
    {
        internal HeatRangePolicy.Slot Slot=new HeatRangePolicy.Slot();
        internal ClientSettingLease Lease=new ClientSettingLease();
        internal bool Return,Failed;
        internal string Group;
        internal void Observe(ClientCapability cap,double? value,DateTime now)
        {
            // On the authoritative host a matching actual component value is
            // sufficient; a client requires the newer full server revision.
            if(cap.authority=="HOST" && Lease.owned && Lease.pending && ReferenceEquals(cap.Identity,Lease.Identity) &&
                value.HasValue && cap.range.HasValue && ClientSettingLease.Same(value.Value,Lease.expected) && ClientSettingLease.Same(cap.range.Value,Lease.expected)) {
                Lease.pending=false;Lease.matched_utc=now.ToString("o");
                Lease.status=Lease.restoring?"RESTORE_HOST_STATE_MATCHED":"HOST_STATE_MATCHED";
                if(Lease.restoring){Lease.owned=false;Lease.restoring=false;}
            } else Lease.Observe(cap,value,now);
        }
    }
    [DataContract] internal sealed class HeatRangeReport
    {
        [DataMember] public string utc,gun,group,role;
        [DataMember] public double requested,observed;
        [DataMember] public ClientSettingLease lease;
    }
    internal sealed partial class PdcEngine
    {
        readonly List<Gun> heatRangeOwned=new List<Gun>();
        string heatRangeState="OFF";
        int heatRangeDebugFrame=-1;
        bool heatRangeWasEnabled;
        bool HeatRangeEnabled { get {var c=config();return c.HeatRangeBanksEnabled&&c.ControlEnabled&&c.ManagedDefenseEnabled&&!c.LabEnabled&&!fireLockout&&(!JoiningServer()||c.ClientMode=="ADAPTIVE ROF");} }

        void ServiceHeatRangeReturns()
        {
            var now=DateTime.UtcNow;
            foreach(var old in heatRangeOwned.ToArray()) {
                var g=guns.FirstOrDefault(x=>ReferenceEquals(x.HeatRange,old.HeatRange))??old;
                if(!ReferenceEquals(g,old)){heatRangeOwned.Remove(old);heatRangeOwned.Add(g);}
                var s=g.HeatRange;
                if(!guns.Contains(g))s.Return=true;
                if(!s.Return)continue;
                var cap=ClientCapability.Read(wc.NativeObserver.Component(g.Entity),g.Part);
                var value=ClientRead(g,"Weapon Range");s.Observe(cap,value,now);
                s.Lease.Restore(cap,value,now,v=>ClientWrite(g,"Weapon Range",v));
                if(!s.Lease.owned){s.Return=false;heatRangeOwned.Remove(g);}
            }
        }
        void ReleaseHeatRanges(string why)
        {
            foreach(var g in heatRangeOwned)g.HeatRange.Return=true;
            heatRangeState=why;ServiceHeatRangeReturns();
        }
        void ApplyHeatRangeBanks()
        {
            var c=config();var now=DateTime.UtcNow;
            bool enabled=HeatRangeEnabled;
            if(!enabled || !wc.Ready || !stableSensorHealthy) {
                ReleaseHeatRanges(enabled?"RANGE SENSOR YIELD":"OFF");heatRangeWasEnabled=enabled;return;
            }
            if(!heatRangeWasEnabled)foreach(var g in guns)if(!g.HeatRange.Lease.owned)g.HeatRange=new HeatRangeState();
            heatRangeWasEnabled=true;ServiceHeatRangeReturns();
            var caps=new Dictionary<Gun,ClientCapability>();
            foreach(var g in guns) {
                var state=g.HeatRange;var s=state.Slot;
                var cap=ClientCapability.Read(wc.NativeObserver.Component(g.Entity),g.Part);caps[g]=cap;
                var value=ClientRead(g,"Weapon Range");state.Observe(cap,value,now);
                if(state.Lease.blocked){state.Failed=true;state.Return=state.Lease.owned;}
                s.Id=GunKey(g);s.Group="UNKNOWN_"+s.Id;
                try {
                    if(controller!=null&&g.Block!=null){Matrix mount;g.Block.Orientation.GetMatrix(out mount);
                        var up=Vector3D.TransformNormal(mount.Up,g.Block.CubeGrid.WorldMatrix);
                        s.Group=BankPlanner.Side(Vector3D.TransformNormal(up,MatrixD.Invert(controller.WorldMatrix)));}
                }catch { }
                if(state.Group!=s.Group){s.Rank=-1;state.Group=s.Group;}
                s.Heat=g.Heat;s.Actual=value??0;
                s.Outer=FreshProfile(g)?Math.Min(c.HeatRangeOuterMeters,g.Profile.MaximumRange):s.Actual;
                s.Middle=FreshProfile(g)?Math.Max(g.Profile.MinimumRange,Math.Min(s.Outer,c.HeatRangeMiddleMeters)):s.Outer;
                s.Inner=FreshProfile(g)?Math.Max(g.Profile.MinimumRange,Math.Min(s.Middle,c.HeatRangeInnerMeters)):s.Outer;
                s.Eligible=ClientAccess(g)&&g.Functional&&FreshProfile(g)&&g.Profile.MinimumRange<=s.Outer&&g.NativeControlKnown&&!g.NativeManual&&
                    !g.OuterSelected&&g.TelemetryFrame==frame&&g.HeatKnown&&BankPlanner.Finite(s.Heat)&&s.Heat>=0&&s.Heat<=100&&
                    value.HasValue&&cap.range.HasValue&&cap.Identity!=null&&cap.own_overrides==true&&!state.Failed&&!state.Return;
                s.Ready=s.Eligible&&g.ReadyRead=="OK"&&g.ReadyValue&&g.ScopeValid;
                s.Confirmed=s.Eligible&&!state.Lease.pending&&!state.Lease.restoring&&ClientSettingLease.Same(s.Actual,cap.range??double.NaN);
                if(!s.Eligible&&state.Lease.owned)state.Return=true;
            }
            HeatRangePolicy.Plan(guns.Select(g=>g.HeatRange.Slot).ToArray(),frame/60d,c.HeatRangeHoldSeconds,c.HeatRangeSwapMargin);
            foreach(var g in guns) {
                var s=g.HeatRange; if(!s.Slot.Eligible||s.Return)continue;
                s.Lease.Request("Weapon Range",s.Slot.Desired,caps[g],ClientRead(g,"Weapon Range"),now,v=>ClientWrite(g,"Weapon Range",v));
                if(s.Lease.owned&&!heatRangeOwned.Contains(g))heatRangeOwned.Add(g);
                g.DesiredRange=s.Slot.Desired;g.RangeReason=s.Slot.Reason;
            }
            ServiceHeatRangeReturns();
            heatRangeState="RANGE BANKS "+c.HeatRangeOuterMeters.ToString("0")+" / "+c.HeatRangeMiddleMeters.ToString("0")+" / "+c.HeatRangeInnerMeters.ToString("0")+
                " | "+guns.Count(g=>g.HeatRange.Slot.Eligible)+"/"+guns.Count+" eligible | "+heatRangeOwned.Count(g=>g.HeatRange.Lease.pending)+" pending";
            if(frame-heatRangeDebugFrame>=60){heatRangeDebugFrame=frame;
                try {JsonIo.Save(System.IO.Path.Combine(dataDir,"range-banks.json"),guns.Concat(heatRangeOwned).GroupBy(GunKey).Select(x=>x.First()).Select(g=>new HeatRangeReport {
                    utc=now.ToString("o"),gun=GunKey(g).ToString(),group=g.HeatRange.Slot.Group,role=g.HeatRange.Slot.Reason,requested=g.HeatRange.Slot.Desired,observed=g.HeatRange.Slot.Actual,lease=g.HeatRange.Lease}).ToArray());}catch(Exception ex){log("Range status: "+ex.Message);}
            }
        }
    }
}
