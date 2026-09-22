using System;
using System.Collections.Generic;
using VRageMath;
namespace VRage.Game.ModAPI
{
 interface IMyGps { string Name {get;set;} string Description {get;set;} Vector3D Coords {get;set;} bool ShowOnHud {get;set;} TimeSpan? DiscardAt {get;set;} }
 interface IMyGpsCollection { void GetGpsList(long player,List<IMyGps> list); IMyGps Create(string name,string description,Vector3D pos,bool shown,bool temporary); void AddGps(long player,IMyGps gps); void ModifyGps(long player,IMyGps gps); }
}
namespace Sandbox.ModAPI
{
 static class MyAPIGateway { public static FakeSession Session; }
 class FakeSession { public FakePlayer Player=new FakePlayer(); public VRage.Game.ModAPI.IMyGpsCollection GPS; public string Name="test"; }
 class FakePlayer { public long IdentityId=123; public ulong SteamUserId=7656119; }
}
namespace ZeoCore
{
 class FleetLinkClient { public bool Online=true; public string Host="test.invalid"; public FleetPictureSnapshot Picture; public FleetPictureSnapshot Snapshot() { return Picture; } }
 static class SectorIdentity { public static Sector Capture() { return new Sector { Id="srv:1"}; } }
 class Sector { public string Id; }
 static class Plugin { public static void Log(string message) { } }
}
class GpsApi : VRage.Game.ModAPI.IMyGpsCollection
{
 public class Point : VRage.Game.ModAPI.IMyGps { public string Name {get;set;} public string Description {get;set;} public Vector3D Coords {get;set;} public bool ShowOnHud {get;set;} public TimeSpan? DiscardAt {get;set;} }
 public readonly List<VRage.Game.ModAPI.IMyGps> Points=new List<VRage.Game.ModAPI.IMyGps>();
 public bool CreatedShown,CreatedTemporary;public long AddedFor;public int Adds,Moves;
 public void GetGpsList(long player,List<VRage.Game.ModAPI.IMyGps> list) { if(player!=123)throw new Exception("Wrong player");list.AddRange(Points); }
 public VRage.Game.ModAPI.IMyGps Create(string name,string desc,Vector3D pos,bool shown,bool temporary) {CreatedShown=shown;CreatedTemporary=temporary;return new Point{Name=name,Description=desc,Coords=pos,ShowOnHud=shown,DiscardAt=temporary?(TimeSpan?)TimeSpan.Zero:null};}
 public void AddGps(long player,VRage.Game.ModAPI.IMyGps point) {AddedFor=player;Adds++;Points.Add(point);}
 public void ModifyGps(long player,VRage.Game.ModAPI.IMyGps point) {if(player!=123)throw new Exception("Wrong player");Moves++;}
}
