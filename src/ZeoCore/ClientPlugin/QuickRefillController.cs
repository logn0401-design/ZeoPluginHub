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
        private const string FuelSubtype="sdx_itemReactorFuel";
        private readonly Dictionary<string,List<Inventory>> _weapons=new Dictionary<string,List<Inventory>>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Inventory> _unloadSources=new List<Inventory>(),_unloadDestinations=new List<Inventory>();
        private Inventory _pendingInventory;
        private string _pendingType;
        private bool _unloadFinished,_weaponCompatibilityKnown;
        private string _unloadState="Unload off";
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
            _cargo.Clear();_supply.Clear();_countInventories.Clear();_targets.Clear();_tanks.Clear();_weapons.Clear();_unloadSources.Clear();_unloadDestinations.Clear();
            var settings=HudSettings.Load();
            foreach(var block in _ship){
                if(block.HasInventory)for(int i=0;i<block.InventoryCount;i++)_countInventories.Add(block.GetInventory(i));
                if(block is IMyCargoContainer && block.HasInventory){
                    _cargo.Add(block.GetInventory(0));
                    if((block.CustomName??"").IndexOf("[ZEO KEEP]",StringComparison.OrdinalIgnoreCase)<0)_unloadSources.Add(block.GetInventory(0));
                }
                var tank=block as Tank;if(settings.RefillTanks&&tank!=null&&tank.IsWorking)_tanks.Add(tank);
            }
            foreach(var block in _base){
                if(block is IMyCargoContainer && block.HasInventory){_supply.Add(block.GetInventory(0));_unloadDestinations.Add(block.GetInventory(0));}
                var production=block as IMyAssembler;if(production!=null)_supply.Add(production.OutputInventory);
            }
            // Compatibility comes from the installed WeaponCore parts, never from stock or HUD visibility.
            bool needsWeaponScan=settings.RefillAmmo || settings.RefillUnloadOtherCargo;
            if(needsWeaponScan && (snapshot==null || snapshot.OwnGridId!=grid.EntityId || (Now-snapshot.CapturedUtc).TotalSeconds>3)){
                Status="Waiting for this ship's weapon scan. Try again in a moment.";return;
            }
            _weaponCompatibilityKnown=snapshot!=null && snapshot.Ammo.Any(a=>a.WeaponCompatible);
            foreach(var entry in AmmoCatalog.Entries){
                int want=QuickRefillPolicy.Missing(QuickRefillPolicy.Want(settings,entry.Key),0);
                var ammo=snapshot?.Ammo.FirstOrDefault(a=>a.Key==entry.Key && a.WeaponCompatible);
                if(ammo==null || want<=0)continue;
                var guns=_ship.Where(b=>ammo.WeaponIds.Contains(b.EntityId)&&b.HasInventory).Select(b=>(Inventory)b.GetInventory(0)).ToList();
                if(guns.Count==0)continue;
                // Keep configured compatible ammo even when ammo loading is temporarily off.
                _weapons[entry.Subtype]=guns;
                if(settings.RefillAmmo)_targets[entry.Subtype]=want;
            }
            // SDX2 native fuel ID: Ingot/sdx_itemReactorFuel. Fuel never comes from station reactors.
            var reactors=_ship.OfType<IMyReactor>().Where(b=>b.HasInventory).Select(b=>(Inventory)b.GetInventory(0)).Where(AcceptsFuel).ToList();
            if(reactors.Count>0){
                _weapons[FuelSubtype]=reactors;
                if(settings.RefillFuel && settings.FusionReserveTarget>0)_targets[FuelSubtype]=settings.FusionReserveTarget;
            }
            // Exact SDX2 reinforced small cargo definition; a renamed ordinary container cannot qualify.
            _cargo.Sort((a,b)=>Reinforced(b).CompareTo(Reinforced(a)));
            _unloadFinished=!settings.RefillUnloadOtherCargo;
            _unloadState=_unloadFinished?"Unload off":"Unloading non-target cargo";
            _ammoFinished=false;_ammoState="Checking ammo";_started=Now;Active=true;
            _recovery=new Recovery{Context=_context,Tanks=_tanks.Where(t=>!t.Stockpile&&t.FilledRatio<.999).Select(t=>t.EntityId).ToList()};
            SaveRecovery(); // Persist restoration intent BEFORE requesting any mode change.
            foreach(var tank in _tanks)if(_recovery.Tanks.Contains(tank.EntityId))tank.Stockpile=true;
            Status="Refilling: checking ammo / filling tanks. Press again to cancel.";
            Plugin.Notify("Quick refill started. HOME > REFILL > QUICK REFILL / CANCEL to stop.",5000);
            _nextTick=DateTime.MinValue;Update();
        }
        internal void Update()
        {
            if(Now<_nextTick)return;_nextTick=Now.AddSeconds(.75);
            try{
                var context=Context();
                if(!Active){
                    if(context!=null){_identity=MyAPIGateway.Session.Player.IdentityId;LoadRecovery();RestoreRecovery();}
                    if(_gate.Pending&&context==_context)_gate.Observe(InventoryCount(_pendingInventory,_pendingType,_pendingSubtype));
                    return;
                }
                var grid=ControlledGrid();
                if(context!=_context||grid==null||grid.EntityId!=_gridId){Stop("Refill stopped: control or session changed.");return;}
                if(!Accessible(_dock)||!Accessible(_other)||!_dock.IsWorking||!_other.IsWorking||_dock.Status!=Sandbox.ModAPI.Ingame.MyShipConnectorStatus.Connected||_dock.OtherConnector?.EntityId!=_other.EntityId){Stop("Undocked: refill stopped; tank modes restored.");return;}
                // Detached subgrids must not become refill destinations.
                var ids=new HashSet<long>(Group(grid).Select(x=>x.EntityId));
                if(_ship.Any(x=>x.Closed||!ids.Contains(x.CubeGrid.EntityId))){Stop("Ship construct changed. Refill stopped.");return;}
                var baseIds=new HashSet<long>(Group(_other.CubeGrid).Select(x=>x.EntityId));
                if(_base.Any(x=>x.Closed||!baseIds.Contains(x.CubeGrid.EntityId))){Stop("Supply construct changed. Refill stopped.");return;}
                if(_gate.Pending){
                    _gate.Observe(InventoryCount(_pendingInventory,_pendingType,_pendingSubtype));
                    if(_gate.TimedOut(Now)){Stop("Ammo transfer unconfirmed; stopped without sending another request.");return;}
                }
                if(!_gate.Pending&&!_unloadFinished)UnloadNext();
                if(!_gate.Pending&&_unloadFinished&&!_ammoFinished)PullNext();
                int full=_tanks.Count(t=>!t.Closed&&t.FilledRatio>=.999);
                Status=_unloadState+" | "+_ammoState+" | Tanks "+full+"/"+_tanks.Count+" full | press to cancel";
                if(_ammoFinished&&_unloadFinished&&full==_tanks.Count){Stop(_unloadState+" | "+_ammoState+(_tanks.Count==0 ? " | No working tanks found." : " | Tanks full; restoring original modes."));return;}
                if((Now-_started).TotalMinutes>=3)Stop(_ammoState+" | Tank refill timed out; check base gas supply/conveyors.");
            }catch(Exception ex){Stop("Refill stopped: "+ex.Message);Plugin.Log("Quick refill update: "+ex);}
        }
        private static bool AcceptsFuel(Inventory inventory){
            var accepted=new List<VRage.Game.ModAPI.Ingame.MyItemType>();inventory.GetAcceptedItems(accepted);
            return accepted.Any(t=>t.TypeId=="MyObjectBuilder_Ingot" && string.Equals(t.SubtypeId,FuelSubtype,StringComparison.OrdinalIgnoreCase));
        }
        private static bool Reinforced(Inventory inventory){return string.Equals((inventory.Owner as Block)?.BlockDefinition.SubtypeName,"sdx_cargocontainerReinforced1x1",StringComparison.OrdinalIgnoreCase);}
        private static string TypeFor(string subtype){return subtype==FuelSubtype?"MyObjectBuilder_Ingot":"MyObjectBuilder_AmmoMagazine";}
        private double InventoryCount(Inventory inventory,string type,string subtype)
        {
            if(inventory==null)return 0;
            _items.Clear();inventory.GetItems(_items);
            return _items.Where(i=>i.Type.TypeId==type && string.Equals(i.Type.SubtypeId,subtype,StringComparison.OrdinalIgnoreCase)).Sum(i=>(double)i.Amount);
        }
        private double Count(string subtype){return _countInventories.Sum(i=>InventoryCount(i,TypeFor(subtype),subtype));}
        private bool Usable(Inventory inventory){return inventory!=null && Accessible(inventory.Owner as Block);}
        private bool Move(Inventory source,Inventory destination,Item item,double limit)
        {
            if(ReferenceEquals(source,destination)||!Usable(source)||!Usable(destination)||!source.CanTransferItemTo(destination,item.Type))return false;
            var src=source as MyInventory;var dst=destination as MyInventory;if(src==null||dst==null)return false;
            // Preserve fractional ore/ingot quantities using the game's fixed-point precision.
            long lo=0,hi=(long)(Math.Min(Math.Min(limit,(double)item.Amount),1000000)*1000000);
            while(lo<hi){long mid=lo+(hi-lo+1)/2;if(destination.CanItemsBeAdded((MyFixedPoint)(mid/1000000d),item.Type))lo=mid;else hi=mid-1;}
            double amount=lo/1000000d;if(item.Type.TypeId!="MyObjectBuilder_Ingot"&&item.Type.TypeId!="MyObjectBuilder_Ore")amount=Math.Floor(amount);
            if(amount<=0)return false;
            _pendingInventory=destination;_pendingSubtype=item.Type.SubtypeId;_pendingType=item.Type.TypeId;
            _gate.Sent(InventoryCount(destination,_pendingType,_pendingSubtype),amount,Now);
            MyInventory.TransferByUser(src,dst,item.ItemId,-1,(MyFixedPoint)amount);
            return true;
        }
        private bool Supply(Inventory destination,IEnumerable<Inventory> sources,string subtype,double need)
        {
            foreach(var source in sources){
                if(!Usable(source))continue;var items=new List<Item>();source.GetItems(items);
                foreach(var item in items)if(item.Type.TypeId==TypeFor(subtype)&&string.Equals(item.Type.SubtypeId,subtype,StringComparison.OrdinalIgnoreCase)&&Move(source,destination,item,need))return true;
            }return false;
        }
        private void UnloadNext()
        {
            int blocked=0;
            foreach(var source in _unloadSources){
                if(!Usable(source))continue;var items=new List<Item>();source.GetItems(items);
                foreach(var item in items){
                    // Keep all native fuel, not just the reserve, and never strip weapons/reactors/cockpit/character inventories.
                    if(item.Type.TypeId=="MyObjectBuilder_Ingot"&&item.Type.SubtypeId==FuelSubtype)continue;
                    if(item.Type.TypeId=="MyObjectBuilder_AmmoMagazine"&&(!_weaponCompatibilityKnown || AmmoCatalog.FindSubtype(item.Type.SubtypeId)==null || _weapons.ContainsKey(item.Type.SubtypeId)))continue;
                    foreach(var destination in _unloadDestinations)if(Move(source,destination,item,(double)item.Amount)){_unloadState="Cargo unload awaiting sync";return;}
                    blocked++;
                }
            }
            _unloadFinished=true;_unloadState=blocked==0?"Cargo unload complete":"Cargo unload partial: "+blocked+" blocked stack(s)";
        }
        private void PullNext()
        {
            int missingTypes=0,emptyWeapons=0;
            foreach(var target in _targets){
                var guns=_weapons[target.Key];double have=Count(target.Key);
                int need=QuickRefillPolicy.Missing(target.Value,have);
                // Balance ready ammunition across compatible inventories before filling cargo reserves.
                double share=Math.Ceiling((double)target.Value/Math.Max(1,guns.Count));
                foreach(var gun in guns){
                    double shortfall=Math.Max(0,share-InventoryCount(gun,TypeFor(target.Key),target.Key));
                    if(shortfall<=0)continue;
                    if(Supply(gun,_cargo,target.Key,shortfall) || (need>0&&Supply(gun,_supply,target.Key,Math.Min(need,shortfall)))){_ammoState="Loading weapons / reactors; waiting for sync";return;}
                    if(InventoryCount(gun,TypeFor(target.Key),target.Key)==0)emptyWeapons++;
                }
                if(target.Key!=FuelSubtype){
                    foreach(var safe in _cargo.Where(Reinforced))
                        if(Supply(safe,_cargo.Where(i=>!Reinforced(i)),target.Key,1000000)){_ammoState="Moving ammo reserves to reinforced cargo";return;}
                }
                if(need==0)continue;missingTypes++;
                foreach(var destination in _cargo)if(Supply(destination,_supply,target.Key,need)){_ammoState="Loading reserves; waiting for sync";return;}
            }
            _ammoFinished=true;
            _ammoState=_targets.Count==0?"No compatible configured load targets (check weapon scan / WANT)":
                missingTypes==0&&emptyWeapons==0?"Load targets met":"Load partial: "+missingTypes+" reserve type(s) short, "+emptyWeapons+" empty weapon/reactor inventory(s); check stock, access and conveyors";
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
