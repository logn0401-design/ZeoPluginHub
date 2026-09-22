using System;
using System.Collections.Generic;
using ZeoCore;
using VRageMath;

class Program
{
    class Store : IDistressGpsStore
    {
        public readonly List<DistressGpsEntry> Entries = new List<DistressGpsEntry>();
        public int Adds, Moves; public bool Ack = true; public string LastName;
        public IList<DistressGpsEntry> Read() { return Entries; }
        public void Add(string name, string desc, Vector3D pos) { Adds++; LastName = name; if (Ack) Entries.Add(new DistressGpsEntry {Description=desc, Position=pos}); }
        public void Move(DistressGpsEntry e,string desc,Vector3D pos) { Moves++; e.Description=desc; e.Position=pos; }
    }
    static int checks;
    static void Check(bool ok,string message) { checks++; if (!ok) throw new Exception(message); }
    static HudTrack Track() { return new HudTrack {EntityId=42,Key="D:42",IsDistress=true,HasPosition=true,SectorId="srv:1",SectorName="A",Position=new Vector3D(5,6,7),Name="Ship:\nGPS:evil",DistressExpiresMs=999999,DistressSecondsRemaining=100,DistressType="SOS"}; }
    static void Main()
    {
        var s=new DistressGpsSynchronizer();var store=new Store();var pic=new FleetPictureSnapshot();var t=Track();pic.Distress.Add(t);
        Check(s.Sync("world/player/faction",pic,store,1000)==1,"First distress creates GPS");
        Check(store.LastName.StartsWith("SOS | ")&&!store.LastName.Contains("\n")&&!store.LastName.Contains(":"),"External name sanitized");
        s.Sync("world/player/faction",pic,store,6000);Check(store.Adds==1,"Polling deduplicates");
        var restart=new DistressGpsSynchronizer();restart.Sync("world/player/faction",pic,store,11000);Check(store.Adds==1,"Restart finds saved GPS");
        t.Position=new Vector3D(100,6,7);s.Sync("world/player/faction",pic,store,16000);Check(store.Moves==1&&store.Entries[0].Position.X==100,"Moved beacon updates same GPS");
        t.Position.X=110;s.Sync("world/player/faction",pic,store,17000);Check(store.Moves==1,"Small movement no write spam");
        store.Entries.Clear();s.Sync("world/player/faction",pic,store,22000);s.Sync("world/player/faction",pic,store,40000);Check(store.Adds==1,"User deletion respected for active call");
        pic.Distress.Clear();s.Sync("world/player/faction",pic,store,45000);pic.Distress.Add(t);s.Sync("world/player/faction",pic,store,50000);Check(store.Adds==2,"New call after clear can save again");
        pic.Distress.Clear();s.Sync("world/player/faction",pic,store,55000);Check(store.Entries.Count==1,"Resolved call GPS retained");
        pic.Distress.Add(t);t.SectorId="srv:2";t.SectorName="Remote sector";
        var remoteStore=new Store();var remoteSync=new DistressGpsSynchronizer();
        remoteSync.Sync("world/player/faction",pic,remoteStore,60000);
        Check(remoteStore.Adds==1&&remoteStore.LastName.Contains("Remote sector"),"Remote sector saved and labelled");
        var other=t.Clone();other.SectorId="srv:3";other.SectorName="Other sector";pic.Distress.Add(other);
        remoteSync.Sync("world/player/faction",pic,remoteStore,61000);Check(remoteStore.Adds==2,"Same source ID in different sectors does not collide");
        remoteSync.Reset();remoteSync.Sync("world/player/faction",pic,remoteStore,62000);Check(remoteStore.Adds==2,"Receiver transfer/restart reuses target sector waypoints");
        pic.Distress.Remove(other);
        t.SectorId="srv:1";t.Position.X=double.NaN;s.Sync("other",pic,store,65000);Check(store.Adds==2,"Invalid coordinates rejected");
        t.Position.X=0;t.DistressSecondsRemaining=0;s.Sync("other",pic,store,70000);Check(store.Adds==2,"Expired call rejected");
        t.DistressSecondsRemaining=100;t.HasPosition=false;s.Sync("other",pic,store,75000);Check(store.Adds==2,"Missing position rejected");
        t.HasPosition=true;s.Sync("different player",pic,store,80000);Check(store.Adds==3,"Context scopes ownership");
        var delayed=new Store {Ack=false};var pending=new DistressGpsSynchronizer();pending.Sync("x",pic,delayed,1000);pending.Sync("x",pic,delayed,6000);Check(delayed.Adds==1,"Network acknowledgement delay does not duplicate sends");
        delayed.Ack=true;pending.Sync("x",pic,delayed,17000);Check(delayed.Adds==2&&delayed.Entries.Count==1,"Unacknowledged add retries after backoff");
        var api=new GpsApi();Sandbox.ModAPI.MyAPIGateway.Session=new Sandbox.ModAPI.FakeSession {GPS=api};
        var fleet=new FleetLinkClient {Picture=pic};var bridge=new DistressGpsBridge();
        bridge.Update(fleet,true,"TEST");Check(api.Adds==1&&api.AddedFor==123,"Bridge uses persistent AddGps for own player");
        Check(!api.CreatedShown&&!api.CreatedTemporary&&!api.Points[0].ShowOnHud&&api.Points[0].DiscardAt==null,"Created GPS hidden and permanent");
        api.Points[0].Name="User route name";api.Points[0].ShowOnHud=true;t.Position.X=500;
        bridge.Reset();bridge.Update(fleet,true,"TEST");Check(api.Moves==1&&api.Points[0].Name=="User route name"&&api.Points[0].ShowOnHud,"Move preserves player name and visibility");
        bridge.Reset();fleet.Online=false;pic.Distress.Add(Track());bridge.Update(fleet,true,"TEST");Check(api.Adds==1,"No GPS writes from offline snapshot");
        bridge.Reset();fleet.Online=true;bridge.Update(fleet,false,"TEST");Check(api.Adds==1,"Receive disabled gates GPS");
        bridge.Reset();bridge.Update(fleet,true,"UNAFFILIATED");Check(api.Adds==1,"Factionless gate");
        bridge.Reset();Sandbox.ModAPI.MyAPIGateway.Session.Name="Different receiver sector";bridge.Update(fleet,true,"TEST");Check(api.Adds==1,"Receiver sector name does not duplicate GPS");
        Console.WriteLine("PASS: "+checks+" distress GPS lifecycle, isolation and game API adapter checks");
    }
}
