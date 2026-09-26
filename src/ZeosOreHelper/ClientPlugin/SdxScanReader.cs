using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using VRageMath;
using ZeoOreShared;
namespace ZeosOreHelper {
 internal sealed class SdxScanReader {
  private Type sessionType;private object session;private int nextDiscovery;private bool learningWasEnabled;
  internal string Status="SDX2: waiting for client scans";
  internal void Reset(){session=null;sessionType=null;nextDiscovery=0;Status="SDX2: waiting for client scans";}
  internal void Update(int frame,VoxelSurveyor survey,OreLearningStore learning,OreSearchConfig search){
   if(frame%60!=0)return;
   var records=survey.Records.ToDictionary(r=>r.EntityId);var previous=records.ToDictionary(p=>p.Key,p=>p.Value.SdxScan);
   if(search.AutoLearn&&!learningWasEnabled)previous.Clear();learningWasEnabled=search.AutoLearn;
   foreach(var r in records.Values)r.SdxScan=null;
   if(!search.Values.B("PreferSdxScans",true)){Status="SDX2 estimates off";return;}
   try {
    if(sessionType==null&&frame>=nextDiscovery){nextDiscovery=frame+300;sessionType=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("AsteroidScanner.Data.Scripts.AsteroidScanner.Session",false)).FirstOrDefault(t=>t!=null);}
    var current=sessionType?.GetProperty("Instance")?.GetValue(null,null);
    if(current==null){session=null;Status="SDX2 not loaded - using local estimates";return;}
    if(!ReferenceEquals(session,current)){previous.Clear();session=current;}
    var groups=SdxScanData.Member(current,"Asteroids") as IDictionary;
    if(groups==null){Status="SDX2 contract unavailable - using local estimates";return;}
    var scanning=SdxScanData.Member(current,"CurrentlyScanning");long scanningId=scanning==null?0:Convert.ToInt64(SdxScanData.Member(scanning,"EntityId"));
    int seen=0,accepted=0;
    foreach(DictionaryEntry group in groups){var items=group.Value as IEnumerable;if(items==null)continue;foreach(var asteroid in items){
     if(++seen>8192)break;long id=Convert.ToInt64(SdxScanData.Member(asteroid,"EntityId"));VoxelSurveyor.RoidRecord r;
     if(!records.TryGetValue(id,out r)||r.Voxel==null||r.Voxel.Closed||r.Voxel.Storage==null)continue;
     var rawPosition=SdxScanData.Member(asteroid,"Position");if(!(rawPosition is Vector3D))continue;var pos=(Vector3D)rawPosition;
     if(!OreLearningStore.Finite(pos.X)||!OreLearningStore.Finite(pos.Y)||!OreLearningStore.Finite(pos.Z)||Vector3D.DistanceSquared(pos,r.Position)>Math.Pow(Math.Max(32,r.Radius*.1),2))continue;
     var scan=SdxScanData.Read(asteroid,id,scanningId==id);if(scan==null)continue;r.SdxScan=scan;accepted++;
     SdxScanData old;previous.TryGetValue(id,out old);
     if(learning!=null&&(old==null||!ReferenceEquals(old.Identity,scan.Identity)))foreach(var ore in scan.Ore){long samples=(long)(scan.Total/512);learning.Observe(new OreObservation{Asteroid=id+"@"+r.Position.ToString(),Ore=ore.Key,Volume=ore.Value,Percent=ore.Value/scan.Total*100,Lod=3,Samples=(int)Math.Min(int.MaxValue,ore.Value/512),SolidSamples=samples,TotalSamples=samples,Verified=true,UtcTicks=DateTime.UtcNow.Ticks},search.AutoLearn);}
    }if(seen>8192)break;}
    Status="SDX2: "+accepted+" completed estimates (saved scan age unknown)";
   }catch(Exception ex){foreach(var r in records.Values)r.SdxScan=null;Status="SDX2 unavailable - using local estimates";if(frame%600==0)Plugin.Log(Status+": "+ex.Message);}
  }
 }
}
