using System;
using System.IO;
using Fixture;
using ZeoCore;
using Sandbox.Game;
using Sandbox.ModAPI;
internal static class Program
{
 static int checks;static DateTime now;static QuickRefillController refill;static Tank tank;static Dock dock;static Cargo shipCargo,baseCargo;static Block weapon;static LocalHudSnapshot snapshot;
 static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message+" // "+refill?.Status+" requests="+MyInventory.Requests+" have="+shipCargo?.Inventory.Amount);}
 static T Add<T>(Grid grid,T block)where T:Block{block.CubeGrid=grid;grid.Blocks.Add(new Slim{FatBlock=block});MyAPIGateway.Entities.All[block.EntityId]=block;return block;}
 static void Setup(){
  now=DateTime.UtcNow;Plugin.DataDirectory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Plugin.DataDirectory);
  refill=new QuickRefillController();refill.Clock=()=>now;MyInventory.Requests=0;MyInventory.Delayed=false;MyInventory.Pending=null;HudSettings.Current=new HudSettings();MyAPIGateway.Entities.All.Clear();
  MyAPIGateway.Session=new Session();var ship=new Grid{EntityId=10};var station=new Grid{EntityId=20,IsStatic=true};
  var cockpit=Add(ship,new Block{EntityId=11});MyAPIGateway.Session.Player.Controller.ControlledEntity.Entity=cockpit;
  dock=Add(ship,new Dock{EntityId=12});var other=Add(station,new Dock{EntityId=21});dock.OtherConnector=other;other.OtherConnector=dock;
  tank=Add(ship,new Tank{EntityId=13});shipCargo=Add(ship,new Cargo{EntityId=14});baseCargo=Add(station,new Cargo{EntityId=22});
  shipCargo.Inventory=new MyInventory{Owner=shipCargo,Amount=100};baseCargo.Inventory=new MyInventory{Owner=baseCargo,Amount=5000};
  weapon=Add(ship,new Block{EntityId=15});weapon.Inventory=new MyInventory{Owner=weapon,Amount=0,Capacity=200};
  snapshot=new LocalHudSnapshot{OwnGridId=10,CapturedUtc=now};snapshot.Ammo.Add(new HudAmmoStock{Key="PDC40",Relevant=true});
 }
 static void Tick(int sec=1){now=now.AddSeconds(sec);refill.Update();}
 static void Settle(int count=30){for(int i=0;i<count;i++)Tick();}
 static int Main(){try{
  Setup();snapshot.Ammo[0].Relevant=false;shipCargo.Inventory.Amount=0;tank.FilledRatio=1;refill.Toggle(snapshot);Settle();
  Check(weapon.Inventory.Amount==200 && shipCargo.Inventory.Amount==800,"Empty stock loads compatible weapons then reserve despite HUD relevance false");
  Setup();snapshot.Ammo[0].WeaponCompatible=false;tank.FilledRatio=1;refill.Toggle(snapshot);Settle();Check(MyInventory.Requests==0,"Stock alone cannot authorize incompatible ammo");
  Setup();baseCargo.Inventory.Amount=0;shipCargo.Inventory.Amount=1000;tank.FilledRatio=1;refill.Toggle(snapshot);Settle();Check(weapon.Inventory.Amount==200&&shipCargo.Inventory.Amount==800,"Existing onboard reserve fills weapon without base stock");
  Setup();MyInventory.Delayed=true;refill.Toggle(snapshot);Tick();Tick();Check(MyInventory.Requests==1,"No duplicate request during network delay");Tick(13);Check(!refill.Active&&!tank.Stockpile,"Timeout stops and restores gas modes");
  Setup();refill.Toggle(snapshot);dock.Status=Sandbox.ModAPI.Ingame.MyShipConnectorStatus.Unconnected;Tick();Check(!refill.Active&&!tank.Stockpile,"Undock cancels and restores tanks");
  Setup();tank.Stockpile=true;refill.Toggle(snapshot);refill.Toggle(snapshot);Check(tank.Stockpile,"Originally stockpiling tank stays stockpiling");
  Setup();var oxygen=Add((Grid)shipCargo.CubeGrid,new Tank{EntityId=77});refill.Toggle(snapshot);Check(tank.Stockpile&&oxygen.Stockpile,"All gas tanks including oxygen enter fill mode");refill.Toggle(snapshot);Check(!tank.Stockpile&&!oxygen.Stockpile,"Cancel restores both gas tanks");
  Setup();HudSettings.Current.RefillTanks=false;refill.Toggle(snapshot);Settle();Check(!tank.Stockpile,"Tank option off respected");
  Setup();HudSettings.Current.RefillAmmo=false;HudSettings.Current.RefillUnloadOtherCargo=true;shipCargo.Inventory.Type="MyObjectBuilder_Ore";shipCargo.Inventory.Subtype="Iron";shipCargo.Inventory.Amount=25.125;baseCargo.Inventory.Amount=0;tank.FilledRatio=1;refill.Toggle(snapshot);Settle();Check(shipCargo.Inventory.Amount==0&&baseCargo.Inventory.Amount==25.125,"Unload preserves fractional ore amount");
  Setup();HudSettings.Current.RefillAmmo=false;HudSettings.Current.RefillUnloadOtherCargo=true;shipCargo.CustomName="Tools [ZEO KEEP]";shipCargo.Inventory.Type="MyObjectBuilder_Component";shipCargo.Inventory.Subtype="SteelPlate";tank.FilledRatio=1;refill.Toggle(snapshot);Settle();Check(shipCargo.Inventory.Amount==100,"Keep-tagged cargo protected");
  Setup();HudSettings.Current.RefillAmmo=false;HudSettings.Current.RefillUnloadOtherCargo=true;shipCargo.Inventory.Type="MyObjectBuilder_Ingot";shipCargo.Inventory.Subtype="sdx_itemReactorFuel";tank.FilledRatio=1;refill.Toggle(snapshot);Settle();Check(shipCargo.Inventory.Amount==100,"Native fuel protected from unload");
  Setup();var safe=Add((Grid)shipCargo.CubeGrid,new Cargo{EntityId=90});safe.BlockDefinition.SubtypeName="sdx_cargocontainerReinforced1x1";safe.Inventory=new MyInventory{Owner=safe,Amount=0};shipCargo.Inventory.Amount=0;tank.FilledRatio=1;refill.Toggle(snapshot);Settle();Check(weapon.Inventory.Amount==200&&safe.Inventory.Amount==800&&shipCargo.Inventory.Amount==0,"Weapons first, reinforced reserves second");
  Setup();var fake=Add((Grid)shipCargo.CubeGrid,new Cargo{EntityId=91});fake.CustomName="Reinforced Small Cargo Container";fake.Inventory=new MyInventory{Owner=fake,Amount=0};tank.FilledRatio=1;refill.Toggle(snapshot);Settle();Check(fake.Inventory.Amount==0,"Renamed container does not gain reinforced priority");
  Setup();baseCargo.Access=false;tank.FilledRatio=1;refill.Toggle(snapshot);Settle();Check(baseCargo.Inventory.Amount==5000,"No access means no base pull");
  Setup();baseCargo.Inventory.Connected=false;tank.FilledRatio=1;refill.Toggle(snapshot);Settle();Check(baseCargo.Inventory.Amount==5000&&refill.Status.Contains("partial"),"Disconnected stock reports partial");
  Setup();snapshot.CapturedUtc=now.AddSeconds(-10);refill.Toggle(snapshot);Check(!refill.Active&&!tank.Stockpile,"Stale compatibility scan rejected");
  Setup();var reactor=Add((Grid)shipCargo.CubeGrid,new Reactor{EntityId=88});reactor.Inventory=new MyInventory{Owner=reactor,Amount=0,Capacity=100};baseCargo.Inventory.Type="MyObjectBuilder_Ingot";baseCargo.Inventory.Subtype="sdx_itemReactorFuel";shipCargo.Inventory.Amount=0;HudSettings.Current.RefillAmmo=false;tank.FilledRatio=1;refill.Toggle(snapshot);Settle();Check(reactor.Inventory.Amount==100&&shipCargo.Inventory.Amount==900,"Native SDX2 fuel fills reactor then reserve");
  Setup();refill.Toggle(snapshot);File.AppendAllText(Path.Combine(Plugin.DataDirectory,"quick-refill-recovery.json"),"{broken");var recovered=new QuickRefillController();recovered.Clock=()=>now;recovered.Update();Check(!tank.Stockpile,"Crash journal survives truncated final record");
  Console.WriteLine("PASS: "+checks+" production-controller refill scenarios");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
