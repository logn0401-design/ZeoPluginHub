using System;
using System.IO;
using Fixture;
using ZeoCore;
using Sandbox.Game;
using Sandbox.ModAPI;
internal static class Program
{
 static int checks;static DateTime now;static QuickRefillController refill;static Tank tank;static Dock dock;static Cargo shipCargo,baseCargo;static LocalHudSnapshot snapshot;
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
  snapshot=new LocalHudSnapshot{OwnGridId=10,CapturedUtc=now};snapshot.Ammo.Add(new HudAmmoStock{Key="PDC40",Relevant=true});
 }
 static void Tick(int sec=1){now=now.AddSeconds(sec);refill.Update();}
 static int Main(){try{
  Check(QuickRefillPolicy.Missing(100,20)==80,"Deficit only");Check(QuickRefillPolicy.Missing(0,10)==0,"Zero target skipped");Check(QuickRefillPolicy.Missing(100,150)==0,"No offload excess");Check(QuickRefillPolicy.Missing(double.NaN,20)==0,"Invalid target rejected");
  Setup();refill.Toggle(snapshot);Check(refill.Active&&tank.Stockpile,"One action starts tanks");Check(MyInventory.Requests==1&&shipCargo.Inventory.Amount==1000&&baseCargo.Inventory.Amount==4100,"Moves deficit, not full target");Tick();Check(MyInventory.Requests==1,"No duplicate when full");tank.FilledRatio=1;Tick();Check(!refill.Active&&!tank.Stockpile,"Completes and restores tank mode");Tick(3);
  Setup();tank.Stockpile=true;refill.Toggle(snapshot);tank.FilledRatio=1;Tick();Check(tank.Stockpile,"Original stockpile ON preserved");
  Setup();refill.Toggle(snapshot);dock.Status=Sandbox.ModAPI.Ingame.MyShipConnectorStatus.Unconnected;Tick();Check(!refill.Active&&!tank.Stockpile,"Undock stops and restores");
  Setup();MyInventory.Delayed=true;refill.Toggle(snapshot);Tick();Tick();Check(MyInventory.Requests==1,"Server latency cannot stack transfer requests");MyInventory.Pending();Tick();Check(MyInventory.Requests==1&&shipCargo.Inventory.Amount==1000,"Acknowledged transfer recounted");
  Setup();MyInventory.Delayed=true;refill.Toggle(snapshot);Tick(13);Check(!refill.Active&&!tank.Stockpile,"Timeout stops and restores");refill.Toggle(snapshot);Check(MyInventory.Requests==1&&!refill.Active,"Timeout not blindly retried");MyInventory.Pending();Tick(3);snapshot.CapturedUtc=now;refill.Toggle(snapshot);Check(refill.Active&&MyInventory.Requests==1,"Late confirmation unlocks new refill");
  Setup();baseCargo.Inventory.Amount=250;tank.FilledRatio=1;refill.Toggle(snapshot);Tick();Check(shipCargo.Inventory.Amount==350&&!refill.Active&&refill.Status.Contains("partial"),"Partial stock reported honestly");
  Setup();shipCargo.Inventory.Capacity=300;tank.FilledRatio=1;refill.Toggle(snapshot);Tick();Check(shipCargo.Inventory.Amount==300&&refill.Status.Contains("partial"),"Capacity-aware partial amount");
  Setup();baseCargo.Inventory.Connected=false;tank.FilledRatio=1;refill.Toggle(snapshot);Check(MyInventory.Requests==0&&refill.Status.Contains("partial"),"Disconnected conveyor blocks transfer");
  Setup();baseCargo.Access=false;tank.FilledRatio=1;refill.Toggle(snapshot);Check(MyInventory.Requests==0,"No source access, no transfer");
  Setup();shipCargo.Access=false;tank.FilledRatio=1;refill.Toggle(snapshot);Check(MyInventory.Requests==0,"No destination access, no transfer");
  Setup();((Block)dock.OtherConnector).Access=false;refill.Toggle(snapshot);Check(!refill.Active&&!tank.Stockpile&&MyInventory.Requests==0,"Unauthorized dock rejected before changes");
  Setup();snapshot.Ammo[0].Relevant=false;tank.FilledRatio=1;refill.Toggle(snapshot);Check(MyInventory.Requests==0,"Relevant-only respected");
  Setup();snapshot.CapturedUtc=now.AddSeconds(-10);refill.Toggle(snapshot);Check(!refill.Active&&!tank.Stockpile,"Stale ship selection rejected");
  Setup();refill.Toggle(snapshot);refill.Toggle(snapshot);Check(!refill.Active&&!tank.Stockpile,"Second press cancels");
  Setup();refill.Toggle(snapshot);var recovered=new QuickRefillController();recovered.Clock=()=>now;recovered.Update();Check(!tank.Stockpile,"Crash journal restores changed tank mode");
  Setup();refill.Toggle(snapshot);MyAPIGateway.Session.Name="OTHER WORLD";Tick();Check(!refill.Active&&tank.Stockpile,"Session change never writes stale world entities");MyAPIGateway.Session.Name="TEST";Tick();Check(!tank.Stockpile,"Restoration resumes in original world");
  Setup();refill.Toggle(snapshot);Tick(181);Check(!refill.Active&&!tank.Stockpile&&refill.Status.Contains("timed out"),"No endless stockpile when base has no gas");
  Setup();baseCargo.Inventory.Amount=0;tank.FilledRatio=1;var gun=Add((Grid)baseCargo.CubeGrid,new Block{EntityId=99});gun.Inventory=new MyInventory{Owner=gun,Amount=10000};refill.Toggle(snapshot);Check(MyInventory.Requests==0,"Base weapon magazines are not stripped");
  Setup();tank.FilledRatio=1;var shipGun=Add((Grid)shipCargo.CubeGrid,new Block{EntityId=98});shipGun.Inventory=new MyInventory{Owner=shipGun,Amount=1200};refill.Toggle(snapshot);Check(MyInventory.Requests==0,"Already-loaded ship magazines count toward WANT");
  Setup();refill.Toggle(snapshot);File.AppendAllText(Path.Combine(Plugin.DataDirectory,"quick-refill-recovery.json"),"{broken");var afterCrash=new QuickRefillController();afterCrash.Clock=()=>now;afterCrash.Update();Check(!tank.Stockpile,"Truncated journal tail preserves restoration intent");
  Console.WriteLine("PASS: "+checks+" real-controller refill checks with simulated game/network adapters.");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
