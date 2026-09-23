using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using Sandbox.Game;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.ModAPI;
using Inventory=VRage.Game.ModAPI.Ingame.IMyInventory;
using Item=VRage.Game.ModAPI.Ingame.MyInventoryItem;
using Grid=VRage.Game.ModAPI.IMyCubeGrid;
using Block=Sandbox.ModAPI.IMyTerminalBlock;
using Tank=Sandbox.ModAPI.IMyGasTank;
using Connector=Sandbox.ModAPI.IMyShipConnector;

namespace ZeoCore
{
    internal sealed class QuickRefillController : IDisposable
    {
        internal bool Active { get; private set; }
        internal string Status { get; private set; }="Dock a ship, then QUICK REFILL: ammo targets + tanks.";
        private readonly List<Block> _ship=new List<Block>(),_base=new List<Block>();
        private readonly List<Tank> _tanks=new List<Tank>();
        private readonly List<Inventory> _cargo=new List<Inventory>(),_supply=new List<Inventory>(),_countInventories=new List<Inventory>();
        private readonly Dictionary<string,int> _targets=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        private readonly RefillTransferGate _gate=new RefillTransferGate();
        private readonly List<Item> _items=new List<Item>();
        private Connector _dock,_other;
        private long _identity,_gridId;
        private string _context,_pendingSubtype;
        private DateTime _nextTick,_started;
        private bool _recoveryLoaded;
        private readonly Dictionary<long,DateTime> _restoreSent=new Dictionary<long,DateTime>();
        private Recovery _recovery=new Recovery();
        private readonly string _journal;
        internal Func<DateTime> Clock=()=>DateTime.UtcNow;
        private DateTime Now { get { return Clock(); } }
        internal QuickRefillController(string journal=null) { _journal=journal ?? Path.Combine(Plugin.DataDirectory,"quick-refill-recovery.json"); }
        private string _ammoState;
        private bool _ammoFinished;
        internal sealed class Recovery
        {
            public string Context {get;set;}
            public List<long> Tanks {get;set;}=new List<long>();
        }
        private static string Context()
        {
            var s=MyAPIGateway.Session;
            return s==null || s.Player==null ? null : s.Name+"|"+MyAPIGateway.Multiplayer.ServerId+"|"+s.Player.IdentityId;
        }
        private static Grid ControlledGrid()
        {
            var p=MyAPIGateway.Session?.Player;
            return (p?.Controller?.ControlledEntity?.Entity as IMyCubeBlock)?.CubeGrid;
        }
        private bool Accessible(Block b)
        {
            return b!=null && !b.Closed && b.HasPlayerAccess(_identity);
        }
        private static List<Grid> Group(Grid grid)
        {
            var grids=new List<Grid>();MyAPIGateway.GridGroups.GetGroup(grid,GridLinkTypeEnum.Mechanical,grids);
            if(grids.Count==0)grids.Add(grid);return grids;
        }
        private void ReadBlocks(List<Grid> grids,List<Block> blocks)
        {
            blocks.Clear();var slim=new List<IMySlimBlock>();
            foreach(var grid in grids){slim.Clear();grid.GetBlocks(slim);foreach(var s in slim){var b=s.FatBlock as Block;if(Accessible(b))blocks.Add(b);}}
        }
        internal void Toggle(LocalHudSnapshot snapshot)
        {
            if(Active){Stop("Refill cancelled; tank modes restored.");return;}
            try { Start(snapshot); }
            catch(Exception ex){Stop("Refill stopped: "+ex.Message);Plugin.Log("Quick refill: "+ex);}
        }
        private void Start(LocalHudSnapshot snapshot)
        {
            if(_gate.Pending){Status="Previous ammo request is unconfirmed. Wait for inventory sync before retrying.";return;}
            var grid=ControlledGrid();var session=MyAPIGateway.Session;
            if(grid==null || session?.Player==null){Status="Control your docked ship from its cockpit first.";return;}
            _identity=session.Player.IdentityId;_context=Context();_gridId=grid.EntityId;
            RestoreRecovery();
            if(_recovery.Tanks.Count>0){Status="Waiting for previous tank restoration. Keep that ship connected to the session.";return;}
            var grids=Group(grid);
            if(grids.Any(x=>x.IsStatic)){Status="Control the visiting ship, not the station.";return;}
            ReadBlocks(grids,_ship);
            var ids=new HashSet<long>(grids.Select(x=>x.EntityId));
            var docks=_ship.OfType<Connector>().Where(x=>x.Status==Sandbox.ModAPI.Ingame.MyShipConnectorStatus.Connected && x.OtherConnector!=null && !ids.Contains(x.OtherConnector.CubeGrid.EntityId)).ToList();
            if(docks.Count!=1){Status=docks.Count==0 ? "No locked connector. Dock to a supply base first." : "More than one dock is connected. Use one supply connection.";return;}
            _dock=docks[0];_other=_dock.OtherConnector;
            if(!Accessible(_other)||!_dock.IsWorking||!_other.IsWorking){Status="Both connectors must be powered and accessible.";return;}
            ReadBlocks(Group(_other.CubeGrid),_base);
            _cargo.Clear();_supply.Clear();_countInventories.Clear();_targets.Clear();_tanks.Clear();
            foreach(var block in _ship){
                if(block.HasInventory)for(int i=0;i<block.InventoryCount;i++)_countInventories.Add(block.GetInventory(i));
                if(block is IMyCargoContainer && block.HasInventory)_cargo.Add(block.GetInventory(0));
                var tank=block as Tank;if(tank!=null&&tank.IsWorking)_tanks.Add(tank);
            }
            foreach(var block in _base){
                if(block is IMyCargoContainer && block.HasInventory)_supply.Add(block.GetInventory(0));
                var production=block as IMyAssembler;if(production!=null)_supply.Add(production.OutputInventory);
            }
            var settings=HudSettings.Load();
            if(settings.AmmoOnlyRelevant && (snapshot==null || snapshot.OwnGridId!=grid.EntityId || (Now-snapshot.CapturedUtc).TotalSeconds>3)){
                Status="Waiting for this ship's ammo scan. Try again in a moment.";return;
            }
            foreach(var entry in AmmoCatalog.Entries){
                if(settings.AmmoOnlyRelevant && !snapshot.Ammo.Any(a=>a.Key==entry.Key&&a.Relevant))continue;
                int want=QuickRefillPolicy.Missing(QuickRefillPolicy.Want(settings,entry.Key),0);if(want>0)_targets[entry.Subtype]=want;
            }
            _ammoFinished=false;_ammoState="Checking ammo";_started=Now;Active=true;
            _recovery=new Recovery{Context=_context,Tanks=_tanks.Where(t=>!t.Stockpile&&t.FilledRatio<.999).Select(t=>t.EntityId).ToList()};
            SaveRecovery(); // Persist restoration intent BEFORE requesting any mode change.
            foreach(var tank in _tanks)if(_recovery.Tanks.Contains(tank.EntityId))tank.Stockpile=true;
            Status="Refilling: checking ammo / filling tanks. Press again to cancel.";
            Plugin.Notify("Quick refill started. HOME > AMMO > QUICK REFILL / CANCEL to stop.",5000);
            _nextTick=DateTime.MinValue;Update();
        }
        internal void Update()
        {
            if(Now<_nextTick)return;_nextTick=Now.AddSeconds(.75);
            try{
                var context=Context();
                if(!Active){
                    if(context!=null){_identity=MyAPIGateway.Session.Player.IdentityId;LoadRecovery();RestoreRecovery();}
                    if(_gate.Pending&&context==_context)_gate.Observe(Count(_pendingSubtype));
                    return;
                }
                var grid=ControlledGrid();
                if(context!=_context||grid==null||grid.EntityId!=_gridId){Stop("Refill stopped: control or session changed.");return;}
                if(!Accessible(_dock)||!Accessible(_other)||!_dock.IsWorking||!_other.IsWorking||_dock.Status!=Sandbox.ModAPI.Ingame.MyShipConnectorStatus.Connected||_dock.OtherConnector?.EntityId!=_other.EntityId){Stop("Undocked: refill stopped; tank modes restored.");return;}
                // Detached subgrids must not become refill destinations.
                var ids=new HashSet<long>(Group(grid).Select(x=>x.EntityId));
                if(_ship.Any(x=>x.Closed||!ids.Contains(x.CubeGrid.EntityId))){Stop("Ship construct changed. Refill stopped.");return;}
                if(_gate.Pending){
                    _gate.Observe(Count(_pendingSubtype));
                    if(_gate.TimedOut(Now)){Stop("Ammo transfer unconfirmed; stopped without sending another request.");return;}
                }
                if(!_gate.Pending&&!_ammoFinished)PullNext();
                int full=_tanks.Count(t=>!t.Closed&&t.FilledRatio>=.999);
                Status=_ammoState+" | Tanks "+full+"/"+_tanks.Count+" full | press to cancel";
                if(_ammoFinished&&full==_tanks.Count){Stop(_ammoState+(_tanks.Count==0 ? " | No working tanks found." : " | Tanks full; restoring original modes."));return;}
                if((Now-_started).TotalMinutes>=3)Stop(_ammoState+" | Tank refill timed out; check base gas supply/conveyors.");
            }catch(Exception ex){Stop("Refill stopped: "+ex.Message);Plugin.Log("Quick refill update: "+ex);}
        }
        private double Count(string subtype)
        {
            double total=0;foreach(var inventory in _countInventories){
                _items.Clear();inventory.GetItems(_items);
                foreach(var item in _items)if(item.Type.TypeId=="MyObjectBuilder_AmmoMagazine"&&string.Equals(item.Type.SubtypeId,subtype,StringComparison.OrdinalIgnoreCase))total+=(double)item.Amount;
            }return total;
        }
        private bool Usable(Inventory inventory)
        {
            return inventory!=null && Accessible(inventory.Owner as Block);
        }
        private void PullNext()
        {
            int missingTypes=0;
            foreach(var target in _targets){
                double have=Count(target.Key);int need=QuickRefillPolicy.Missing(target.Value,have);if(need==0)continue;missingTypes++;
                foreach(var source in _supply){
                    if(!Usable(source))continue;var items=new List<Item>();source.GetItems(items);
                    foreach(var item in items){
                        if(item.Type.TypeId!="MyObjectBuilder_AmmoMagazine"||!string.Equals(item.Type.SubtypeId,target.Key,StringComparison.OrdinalIgnoreCase))continue;
                        foreach(var destination in _cargo){
                            if(!Usable(destination)||!source.CanTransferItemTo(destination,item.Type))continue;
                            int amount=(int)Math.Min(need,Math.Floor((double)item.Amount));
                            int lo=0,hi=amount;while(lo<hi){int mid=lo+(hi-lo+1)/2;if(destination.CanItemsBeAdded((MyFixedPoint)mid,item.Type))lo=mid;else hi=mid-1;}
                            amount=lo;if(amount<=0)continue;
                            var src=source as MyInventory;var dst=destination as MyInventory;if(src==null||dst==null)continue;
                            _pendingSubtype=target.Key;_gate.Sent(have,amount,Now);
                            MyInventory.TransferByUser(src,dst,item.ItemId,-1,(MyFixedPoint)amount);
                            _ammoState="Ammo transfer pending ("+amount+")";return;
                        }
                    }
                }
            }
            _ammoFinished=true;
            _ammoState=missingTypes==0 ? (_targets.Count==0 ? "No eligible ammo targets" : "Ammo targets met") : "Ammo partial: "+missingTypes+" type(s) short (stock / cargo / conveyor / access)";
        }
        private void Stop(string message)
        {
            Active=false;
            message=message.Replace("tank modes restored","tank restoration requested");
            try{RestoreRecovery();}catch(Exception ex){Plugin.Log("Refill tank restore pending: "+ex.Message);message+=" Tank restoration pending.";}
            Status=message;Plugin.Notify(message,6000);Plugin.Log("Quick refill: "+message);
        }
        private void LoadRecovery()
        {
            if(_recoveryLoaded)return;
            if(File.Exists(_journal))
                foreach(var line in File.ReadLines(_journal))
                {
                    try { var record=new JavaScriptSerializer().Deserialize<Recovery>(line);if(record?.Tanks!=null)_recovery=record; }
                    catch { } // A truncated final append never erases the previous valid intent.
                }
            _recoveryLoaded=true;
        }
        private void SaveRecovery()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_journal));
            byte[] bytes=Encoding.UTF8.GetBytes("\n"+new JavaScriptSerializer().Serialize(_recovery)+"\n");
            using(var stream=new FileStream(_journal,FileMode.Append,FileAccess.Write,FileShare.Read))
            { stream.Write(bytes,0,bytes.Length);stream.Flush(true); }
        }
        private void RestoreRecovery()
        {
            LoadRecovery();if(_recovery.Context!=Context()||_recovery.Tanks.Count==0)return;
            bool changed=false;
            foreach(var id in _recovery.Tanks.ToArray()){
                VRage.ModAPI.IMyEntity entity;
                if(!MyAPIGateway.Entities.TryGetEntityById(id,out entity))continue;
                var tank=entity as Tank;if(!Accessible(tank))continue;
                // Always send the restore, even if the stockpile-on request is
                // still in flight. Both use the game's ordered entity events.
                DateTime sent;
                if(_restoreSent.TryGetValue(id,out sent) && !tank.Stockpile && (Now-sent).TotalSeconds>=2)
                { _recovery.Tanks.Remove(id);_restoreSent.Remove(id);changed=true; }
                else if(!_restoreSent.ContainsKey(id) || (Now-sent).TotalSeconds>=3)
                {
                    tank.Stockpile=false;_restoreSent[id]=Now;
                }
            }
            if(changed)SaveRecovery();
            if(_recovery.Tanks.Count==0)Status=Status.Replace("restoring original modes","original modes restored").Replace("tank restoration requested","tank modes restored");
        }
        public void Dispose(){if(Active)Stop("Refill stopped on plugin shutdown.");}
    }
}
