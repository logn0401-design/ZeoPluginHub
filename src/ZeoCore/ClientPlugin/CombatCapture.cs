using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.Entity;
using VRageMath;

namespace ZeoCore
{
    // Observation only: no targeting, firing, terminal actions or weapon settings are written.
    internal sealed class CombatCapture : IDisposable
    {
        private sealed class Hook { public MyEntity Weapon; public int Part; public Action<long,int,ulong,long,Vector3D,bool> Callback; }
        private readonly CoreSystemsApi api;
        private readonly object gate = new object();
        private readonly List<Hook> hooks = new List<Hook>();
        private readonly Queue<Dictionary<string,object>> pending = new Queue<Dictionary<string,object>>();
        private readonly Dictionary<ulong, string> flying = new Dictionary<ulong,string>();
        private readonly HashSet<ulong> seen = new HashSet<ulong>();
        private readonly Queue<ulong> order = new Queue<ulong>();
        private long gridId;
        private int scanFrame = -10000, sampleFrame = -10000, generation;
        private string session = NewSession();
        public int MonitoredParts { get { return hooks.Count; } }
        public long Dropped { get; private set; }
        public CombatCapture(CoreSystemsApi api) { this.api = api; }
        private static double[] Vec(Vector3D v) { return new[]{v.X,v.Y,v.Z}; }
        private static bool Finite(Vector3D v) { return !(double.IsNaN(v.X)||double.IsNaN(v.Y)||double.IsNaN(v.Z)||double.IsInfinity(v.X)||double.IsInfinity(v.Y)||double.IsInfinity(v.Z)); }
        private static string NewSession(){return DateTime.UtcNow.Ticks.ToString()+BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(),0).ToString("D20");}
        private string Id(ulong id) { return session + id.ToString("D20"); }
        private void Enqueue(Dictionary<string,object> row)
        {
            row["observedUtcMs"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if(pending.Count>=1024) { pending.Dequeue(); Dropped++; }
            pending.Enqueue(row);
        }
        public void Update(IMyCubeGrid grid, int frame, bool enabled)
        {
            if(!enabled || grid==null || !api.ShotMonitorReady) { if(hooks.Count>0||gridId!=0)Reset(); return; }
            if(gridId!=grid.EntityId) { Reset(); gridId=grid.EntityId; scanFrame=frame-180; }
            if(frame<scanFrame || frame-scanFrame>=180)
            {
                scanFrame=frame;
                var grids=new List<IMyCubeGrid>();
                try { MyAPIGateway.GridGroups.GetGroup(grid, GridLinkTypeEnum.Mechanical, grids); } catch { }
                if(grids.Count==0)grids.Add(grid);
                var desired=new HashSet<string>();
                foreach(var member in grids)
                {
                    var blocks=new List<VRage.Game.ModAPI.IMySlimBlock>();
                    member.GetBlocks(blocks);
                    foreach(var slim in blocks)
                    {
                        var weapon=slim.FatBlock as MyEntity;
                        if(weapon==null || !api.HasCoreWeapon(weapon))continue;
                        var parts=new Dictionary<string,int>();
                        if(!api.WeaponParts(weapon,parts))continue;
                        foreach(int part in new HashSet<int>(parts.Values))
                        {
                            string key=weapon.EntityId+":"+part; desired.Add(key);
                            if(hooks.Exists(h=>h.Weapon==weapon&&h.Part==part) || hooks.Count>=512)continue;
                            var hook=new Hook{Weapon=weapon,Part=part}; int epoch=generation;
                            hook.Callback=(parent,p,id,target,pos,exists)=>{
                                if(id==0||!Finite(pos))return;
                                lock(gate)
                                {
                                    if(epoch!=generation)return;
                                    if(exists && !seen.Add(id))return;
                                    if(exists) { order.Enqueue(id);while(order.Count>4096)seen.Remove(order.Dequeue()); }
                                    else if(!seen.Contains(id))return;
                                    Vector3D velocity;string ammo;float health;
                                    bool observed=api.ProjectileState(id,out velocity,out ammo,out health);
                                    var row=new Dictionary<string,object>{{"kind",exists?0:1},{"projectileId",Id(id)},
                                        {"targetId",target.ToString()},{"position",Vec(pos)},{"weaponId",weapon.EntityId.ToString()},
                                        {"part",part},{"ammo",ammo??api.GetActiveAmmo(weapon,part)},{"group","observed"}};
                                    if(observed)row["velocity"]=Vec(velocity);
                                    Enqueue(row);
                                    if(exists&&flying.Count<128)flying[id]=Id(id);else if(!exists)flying.Remove(id);
                                }
                            };
                            if(api.AddShotMonitor(weapon,part,hook.Callback))hooks.Add(hook);
                        }
                    }
                }
                for(int i=hooks.Count-1;i>=0;i--)if(!desired.Contains(hooks[i].Weapon.EntityId+":"+hooks[i].Part))
                { var h=hooks[i];api.RemoveShotMonitor(h.Weapon,h.Part,h.Callback);hooks.RemoveAt(i); }
            }
            if(frame<sampleFrame||frame-sampleFrame>=12)
            {
                sampleFrame=frame;
                lock(gate)foreach(ulong id in new List<ulong>(flying.Keys))
                {
                    Vector3D pos,vel;string ammo;float health;
                    if(!api.ProjectileSample(id,out pos,out vel,out ammo,out health)){flying.Remove(id);continue;}
                    if(health<=0)continue; // Guided/health-bearing projectiles; rounds use witnessed launch/despawn.
                    Enqueue(new Dictionary<string,object>{{"kind",2},{"projectileId",Id(id)}, {"position",Vec(pos)},{"velocity",Vec(vel)}});
                }
            }
        }
        public object[] Drain()
        {
            lock(gate)
            {
                long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();var rows=new List<object>();
                while(pending.Count>0 && now-(long)pending.Peek()["observedUtcMs"]>10000)pending.Dequeue();
                foreach(var original in pending)
                {
                    var row=new Dictionary<string,object>(original);long age=now-(long)row["observedUtcMs"];row.Remove("observedUtcMs");
                    if(age<0||age>10000)continue;row["ageMs"]=age;rows.Add(row);
                }
                return rows.ToArray();
            }
        }
        private void Reset()
        {
            lock(gate){generation++;pending.Clear();flying.Clear();seen.Clear();order.Clear();session=NewSession();}
            foreach(var h in hooks)api.RemoveShotMonitor(h.Weapon,h.Part,h.Callback);
            hooks.Clear();gridId=0;
        }
        public void Dispose(){Reset();}
    }
}
