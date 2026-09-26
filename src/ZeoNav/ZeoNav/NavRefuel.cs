using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using Tank=Sandbox.ModAPI.IMyGasTank;
using Connector=Sandbox.ModAPI.IMyShipConnector;
using ConnectorStatus=Sandbox.ModAPI.Ingame.MyShipConnectorStatus;

namespace ZeoNav
{
    // Only tank Stockpile modes are changed. Actual transfer is the game's conveyor system.
    internal sealed class NavRefuel
    {
        [DataContract] internal sealed class Recovery
        { [DataMember] public string Context; [DataMember] public List<long> Tanks=new List<long>(); }
        internal bool Active {get;private set;}
        internal string Status="Connect to a supply port, then REFUEL to fill ship gas tanks.";
        private readonly Func<ShipContext> ship;private readonly Action<string> log;private readonly string journal;
        private readonly List<Tank> tanks=new List<Tank>();
        private readonly Dictionary<long,DateTime> restored=new Dictionary<long,DateTime>();
        private Recovery recovery=new Recovery();private bool loaded;
        private Connector dock,other;private ShipContext owner;private DateTime started,next;
        internal NavRefuel(Func<ShipContext> ship,Action<string> log,string journal){this.ship=ship;this.log=log;this.journal=journal;}
        private string Context(){var s=MyAPIGateway.Session;return s?.Player==null?null:MyAPIGateway.Multiplayer.ServerId+"|"+s.Name+"|"+s.Player.IdentityId;}
        private bool Access(Sandbox.ModAPI.IMyTerminalBlock b){return b!=null&&!b.Closed&&MyAPIGateway.Session?.Player!=null&&b.HasPlayerAccess(MyAPIGateway.Session.Player.IdentityId);}
        internal void Toggle()
        {
            if(Active){Stop("Refuel cancelled; tank restoration requested.");return;}
            try
            {
                Load();Restore();if(recovery.Tanks.Count>0){Status="Previous tank restoration is pending; return to that ship/session first.";return;}
                owner=ship();if(owner==null||owner.Grid.IsStatic){Status="Control your docked ship from its cockpit.";return;}
                var grids=new List<IMyCubeGrid>();MyAPIGateway.GridGroups.GetGroup(owner.Grid,GridLinkTypeEnum.Mechanical,grids);if(grids.Count==0)grids.Add(owner.Grid);
                var ids=new HashSet<long>(grids.Select(g=>g.EntityId));var docks=new List<Connector>();tanks.Clear();
                foreach(var grid in grids){var blocks=new List<IMySlimBlock>();grid.GetBlocks(blocks);foreach(var b in blocks){var c=b.FatBlock as Connector;if(Access(c)&&c.Status==ConnectorStatus.Connected&&c.OtherConnector!=null&&!ids.Contains(c.OtherConnector.CubeGrid.EntityId))docks.Add(c);var t=b.FatBlock as Tank;if(Access(t)&&t.IsWorking)tanks.Add(t);}}
                if(docks.Count!=1){Status="Refuel requires exactly one locked supply connector.";return;}
                dock=docks[0];other=dock.OtherConnector;
                if(!Access(other)||!dock.IsWorking||!other.IsWorking){Status="Both supply connectors must be powered and accessible.";return;}
                if(tanks.Count==0){Status="No working gas tanks found on this ship.";return;}
                recovery=new Recovery{Context=Context(),Tanks=tanks.Where(t=>!t.Stockpile&&t.FilledRatio<.999).Select(t=>t.EntityId).ToList()};
                Save(); // Durable intent before changing any block; preserves original stockpile=true tanks.
                Active=true;started=DateTime.UtcNow;next=DateTime.MinValue;
                foreach(var t in tanks)if(recovery.Tanks.Contains(t.EntityId))t.Stockpile=true;
                Status="REFUEL — filling ship gas tanks; press again to cancel.";
            }
            catch(Exception ex){Stop("Refuel stopped: "+ex.Message);}
        }
        internal void Update()
        {
            var now=DateTime.UtcNow;if(now<next)return;next=now.AddSeconds(.75);
            try
            {
                Load();if(!Active){Restore();return;}
                if(ship()!=owner||Context()!=recovery.Context||!Access(dock)||!Access(other)||dock.Status!=ConnectorStatus.Connected||dock.OtherConnector?.EntityId!=other.EntityId||!dock.IsWorking||!other.IsWorking){Stop("Refuel stopped: control, connection or power changed.");return;}
                var grids=new List<IMyCubeGrid>();MyAPIGateway.GridGroups.GetGroup(owner.Grid,GridLinkTypeEnum.Mechanical,grids);if(grids.Count==0)grids.Add(owner.Grid);
                var ids=new HashSet<long>(grids.Select(g=>g.EntityId));
                if(tanks.Any(t=>!Access(t)||!t.IsWorking||!ids.Contains(t.CubeGrid.EntityId))){Stop("Refuel stopped: tank or construct changed.");return;}
                int full=tanks.Count(t=>t.FilledRatio>=.999);
                Status="REFUEL — "+full+"/"+tanks.Count+" tanks full; average "+(100*tanks.Average(t=>t.FilledRatio)).ToString("0")+"%. Press again to cancel.";
                if(full==tanks.Count)Stop("Refuel complete; tank restoration requested.");
                else if((now-started).TotalMinutes>=3)Stop("Refuel timed out; check station supply and conveyors. Tank restoration requested.");
            }
            catch(Exception ex){Stop("Refuel stopped: "+ex.Message);}
        }
        internal void Stop(string reason)
        {
            bool was=Active;Active=false;if(was)Status=reason;
            try{Load();Restore();}catch(Exception ex){Status="Tank recovery pending: "+ex.Message;}
            if(was)log("REFUEL STOP // "+Status);
        }
        private void Load()
        {
            if(loaded)return;
            if(File.Exists(journal))
            {
                bool found=false,content=false;
                foreach(var line in File.ReadLines(journal))
                {
                    if(string.IsNullOrWhiteSpace(line))continue;
                    content=true;var r=JsonIo.FromBytes<Recovery>(Encoding.UTF8.GetBytes(line));
                    if(r?.Tanks!=null){recovery=r;found=true;}
                }
                if(content&&!found)throw new IOException("Refuel recovery journal is unreadable; preserving it and refusing new tank changes.");
            }
            loaded=true;
        }
        private void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(journal));
            var bytes=Encoding.UTF8.GetBytes("\n"+Encoding.UTF8.GetString(JsonIo.ToBytes(recovery))+"\n");
            using(var f=new FileStream(journal,FileMode.Append,FileAccess.Write,FileShare.Read)){f.Write(bytes,0,bytes.Length);f.Flush(true);}
        }
        private void Restore()
        {
            if(Active||recovery.Context!=Context()||recovery.Tanks.Count==0)return;
            bool changed=false;var now=DateTime.UtcNow;
            foreach(long id in recovery.Tanks.ToArray())
            {
                VRage.ModAPI.IMyEntity entity;if(!MyAPIGateway.Entities.TryGetEntityById(id,out entity))continue;
                var tank=entity as Tank;if(!Access(tank))continue;
                DateTime sent;
                if(restored.TryGetValue(id,out sent)&&!tank.Stockpile&&(now-sent).TotalSeconds>=2){recovery.Tanks.Remove(id);restored.Remove(id);changed=true;}
                else if(!restored.ContainsKey(id)||(now-sent).TotalSeconds>=3){tank.Stockpile=false;restored[id]=now;}
            }
            if(changed)Save();
            if(recovery.Tanks.Count==0)Status=Status.Replace("tank restoration requested","tank modes restored");
        }
    }
}
