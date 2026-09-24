using System;
using System.Collections.Generic;
using System.Linq;
namespace VRage { public struct MyFixedPoint { public double V;public static explicit operator MyFixedPoint(double v)=>new MyFixedPoint{V=v};public static explicit operator double(MyFixedPoint v)=>v.V; } }
namespace VRage.ModAPI {public interface IMyEntity{long EntityId{get;}bool Closed{get;}}}
namespace VRage.Game.ModAPI.Ingame {
 public struct MyItemType {public string TypeId,SubtypeId;public MyItemType(string t,string s){TypeId=t;SubtypeId=s;}}
 public struct MyInventoryItem {public uint ItemId;public MyItemType Type;public VRage.MyFixedPoint Amount;}
 public interface IMyInventory {
  VRage.ModAPI.IMyEntity Owner{get;}void GetItems(List<MyInventoryItem> items);
  void GetAcceptedItems(List<MyItemType> items);bool CanTransferItemTo(IMyInventory other,MyItemType item);bool CanItemsBeAdded(VRage.MyFixedPoint amount,MyItemType type);
 }
}
namespace VRage.Game.ModAPI {
 public enum GridLinkTypeEnum {Mechanical}
 public interface IMyCubeBlock:VRage.ModAPI.IMyEntity {IMyCubeGrid CubeGrid{get;}}
 public interface IMyCubeGrid:VRage.ModAPI.IMyEntity {bool IsStatic{get;}void GetBlocks(List<IMySlimBlock> blocks);}
 public interface IMySlimBlock {IMyCubeBlock FatBlock{get;}}
}
namespace Sandbox.ModAPI.Ingame {public enum MyShipConnectorStatus {Unconnected,Connected}}
namespace Sandbox.ModAPI {
 public interface IMyTerminalBlock:VRage.Game.ModAPI.IMyCubeBlock {string CustomName{get;}Fixture.Definition BlockDefinition{get;}bool HasPlayerAccess(long id);bool HasInventory{get;}int InventoryCount{get;}VRage.Game.ModAPI.Ingame.IMyInventory GetInventory(int n);}
 public interface IMyReactor:IMyTerminalBlock {}
 public interface IMyCargoContainer:IMyTerminalBlock {}
 public interface IMyAssembler:IMyTerminalBlock {VRage.Game.ModAPI.Ingame.IMyInventory OutputInventory{get;}}
 public interface IMyGasTank:IMyTerminalBlock {bool Stockpile{get;set;}bool IsWorking{get;}double FilledRatio{get;}}
 public interface IMyShipConnector:IMyTerminalBlock {Ingame.MyShipConnectorStatus Status{get;}IMyShipConnector OtherConnector{get;}bool IsWorking{get;}}
 public static class MyAPIGateway {public static Session Session;public static Multiplayer Multiplayer=new Multiplayer();public static Groups GridGroups=new Groups();public static Entities Entities=new Entities();}
 public class Session {public string Name="TEST";public Player Player=new Player();}
 public class Player {public long IdentityId=42;public Controller Controller=new Controller();}
 public class Controller {public Controlled ControlledEntity=new Controlled();}
 public class Controlled {public object Entity;}
 public class Multiplayer {public ulong ServerId=1;}
 public class Groups {public void GetGroup(VRage.Game.ModAPI.IMyCubeGrid g,VRage.Game.ModAPI.GridLinkTypeEnum kind,List<VRage.Game.ModAPI.IMyCubeGrid> result){result.Add(g);}}
 public class Entities {public Dictionary<long,VRage.ModAPI.IMyEntity> All=new Dictionary<long,VRage.ModAPI.IMyEntity>();public bool TryGetEntityById(long id,out VRage.ModAPI.IMyEntity value)=>All.TryGetValue(id,out value);}
}
namespace Sandbox.Game {
 public class MyInventory:VRage.Game.ModAPI.Ingame.IMyInventory {
  public VRage.ModAPI.IMyEntity Owner{get;set;}public double Amount;public string Type="MyObjectBuilder_AmmoMagazine";public string Subtype="sdx_ammomagazinePdc40mm";public int Capacity=100000;public bool Connected=true;
  public static int Requests;public static bool Delayed;public static Action Pending;
  public void GetItems(List<VRage.Game.ModAPI.Ingame.MyInventoryItem> items){if(Amount>0)items.Add(new VRage.Game.ModAPI.Ingame.MyInventoryItem{ItemId=1,Type=new VRage.Game.ModAPI.Ingame.MyItemType{TypeId=Type,SubtypeId=Subtype},Amount=new VRage.MyFixedPoint{V=Amount}});}
  public void GetAcceptedItems(List<VRage.Game.ModAPI.Ingame.MyItemType> items){if(Owner is Sandbox.ModAPI.IMyReactor)items.Add(new VRage.Game.ModAPI.Ingame.MyItemType("MyObjectBuilder_Ingot","sdx_itemReactorFuel"));}
  public bool CanTransferItemTo(VRage.Game.ModAPI.Ingame.IMyInventory other,VRage.Game.ModAPI.Ingame.MyItemType item)=>Connected&&((MyInventory)other).Connected;
  public bool CanItemsBeAdded(VRage.MyFixedPoint amount,VRage.Game.ModAPI.Ingame.MyItemType type)=>Amount+amount.V<=Capacity;
  public static void TransferByUser(MyInventory src,MyInventory dst,uint id,int slot,VRage.MyFixedPoint amount){Requests++;Action apply=()=>{var n=Math.Min(src.Amount,amount.V);src.Amount-=n;dst.Amount+=n;dst.Subtype=src.Subtype;dst.Type=src.Type;};if(Delayed)Pending=apply;else apply();}
 }
}
namespace ZeoCore {
 internal static class Plugin {internal static string DataDirectory;internal static void Log(string text){}internal static void Notify(string text,int time=3500,string font="White"){} }
 internal class HudSettings {
  internal static HudSettings Current=new HudSettings();internal static HudSettings Load()=>Current;
  public bool RefillAmmo=true,RefillFuel=true,RefillTanks=true,RefillUnloadOtherCargo=false;public int FusionReserveTarget=1000;public bool AmmoOnlyRelevant=true;public double WantPdc40=1000,WantPdc40Improvised=0,WantPdc50=0,WantSabot80=0,WantSabot80Improvised=0,WantSabot100=0,WantTorp160=0,WantTorp190=0,WantTorp220=0;
 }
 internal class HudAmmoStock {public string Key;public bool Relevant;public bool WeaponCompatible=true;public long[] WeaponIds=new long[]{15};}
 internal class LocalHudSnapshot {public long OwnGridId;public DateTime CapturedUtc;public List<HudAmmoStock> Ammo=new List<HudAmmoStock>();}
}
namespace Fixture {
 using VRage.Game.ModAPI;using Sandbox.ModAPI;
 public class Grid:IMyCubeGrid {public long EntityId{get;set;}public bool Closed{get;set;}public bool IsStatic{get;set;}public List<IMySlimBlock> Blocks=new List<IMySlimBlock>();public void GetBlocks(List<IMySlimBlock> b)=>b.AddRange(Blocks);}
 public class Slim:IMySlimBlock {public IMyCubeBlock FatBlock{get;set;}}
 public class Definition {public string SubtypeName="";}
 public class Block:IMyTerminalBlock {public string CustomName{get;set;}="";public Definition BlockDefinition{get;set;}=new Definition();
  public long EntityId{get;set;}public bool Closed{get;set;}public IMyCubeGrid CubeGrid{get;set;}public bool Access=true;public bool HasPlayerAccess(long id)=>Access;
  public Sandbox.Game.MyInventory Inventory;public bool HasInventory=>Inventory!=null;public int InventoryCount=>HasInventory?1:0;public VRage.Game.ModAPI.Ingame.IMyInventory GetInventory(int n)=>Inventory;
 }
 public class Reactor:Block,IMyReactor {}
 public class Cargo:Block,IMyCargoContainer {}
 public class Tank:Block,IMyGasTank {public bool Stockpile{get;set;}public bool IsWorking{get;set;}=true;public double FilledRatio{get;set;}=.4;}
 public class Dock:Block,IMyShipConnector {public Sandbox.ModAPI.Ingame.MyShipConnectorStatus Status{get;set;}=Sandbox.ModAPI.Ingame.MyShipConnectorStatus.Connected;public IMyShipConnector OtherConnector{get;set;}public bool IsWorking{get;set;}=true;}
}
