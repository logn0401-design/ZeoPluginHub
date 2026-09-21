using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Voxels;
using VRageMath;
using ZeoOreShared;
namespace ZeosOreHelper {
 internal sealed class OreDepositSurveyor {
  private sealed class Map {internal long Id;internal MyVoxelMap Voxel;internal object Storage;internal int Updated;internal List<Deposit> Deposits=new List<Deposit>();}
  private sealed class Deposit {internal string Ore;internal Vector3D Center;internal double Radius,Volume;}
  private sealed class Job {internal Map Map;internal Vector3I Min;internal OreScanChunks Chunks;internal OreDepositClusters Clusters=new OreDepositClusters();internal bool ReadDone;}
  private readonly Dictionary<long,Map> maps=new Dictionary<long,Map>();private Job job;
  private readonly MyStorageData cache=new MyStorageData(MyStorageDataTypeFlags.ContentAndMaterial);
  internal string Status="Deposits: approach within 5 km";
  internal void Reset(){maps.Clear();job=null;Status="Deposits: approach within 5 km";}
  internal void Update(int frame,VoxelSurveyor survey,OreSearchConfig search){
   if(!search.Values.B("DepositMarkers",true)||!search.Values.B("MarkersEnabled",true)||search.Values.I("MaxMarkers",8)<=0||search.Values.I("MaxDepositMarkers",4)<=0){Reset();Status="Deposit view off";return;}
   if(frame%6!=0)return;
   double range=OreGeometry.Safe(search.Values.D("DepositRangeKm",5),.1,5,5)*1000;
   var nearby=survey.Records.Where(r=>!r.Skipped&&r.Distance<=range&&r.Voxel is MyVoxelMap&&!r.Voxel.Closed).OrderBy(r=>r.Distance).Take(4).ToArray();
   var ids=new HashSet<long>(nearby.Select(r=>r.EntityId));foreach(var id in maps.Keys.Where(id=>!ids.Contains(id)).ToArray())maps.Remove(id);
   if(job!=null&&(!ids.Contains(job.Map.Id)||job.Map.Voxel.Closed||!ReferenceEquals(job.Map.Storage,job.Map.Voxel.Storage)))job=null;
   try {
    if(job==null){foreach(var r in nearby){Map existing;if(maps.TryGetValue(r.EntityId,out existing)&&ReferenceEquals(existing.Storage,r.Voxel.Storage)&&frame-existing.Updated<1800)continue;
     var voxel=(MyVoxelMap)r.Voxel;var storage=voxel.Storage;if(storage==null||storage.Closed)continue;
     var min=voxel.StorageMin>>3;var max=(voxel.StorageMax-Vector3I.One)>>3;var dims=max-min+Vector3I.One;
     var map=new Map{Id=r.EntityId,Voxel=voxel,Storage=storage,Updated=frame};if(existing!=null&&!ReferenceEquals(existing.Storage,storage))maps.Remove(r.EntityId);
     if(dims.X<1||dims.Y<1||dims.Z<1||dims.X>512||dims.Y>512||dims.Z>512||(long)dims.X*dims.Y*dims.Z>2097152){maps[r.EntityId]=map;Status="Deposits: asteroid exceeds scan budget";continue;}
     job=new Job{Map=map,Min=min,Chunks=new OreScanChunks(dims.X,dims.Y,dims.Z,32)};break;
    }}
    if(job==null){if(nearby.Length==0)Status="Deposits: approach within "+(range/1000).ToString("0.#")+" km";return;}
    Status="Deposits: mapping loaded ore (8 m samples)";
    var active=job;
    if(!active.ReadDone){var c=active.Chunks.Current;var min=active.Min+new Vector3I(c.X,c.Y,c.Z);var max=active.Min+new Vector3I(c.MaxX,c.MaxY,c.MaxZ);
     active.Map.Voxel.Storage.PinAndExecute(storage=>{cache.Resize(max-min+Vector3I.One);storage.ReadRange(cache,MyStorageDataTypeFlags.ContentAndMaterial,3,min,max);
      int index=0;for(int z=c.Z;z<=c.MaxZ;z++)for(int y=c.Y;y<=c.MaxY;y++)for(int x=c.X;x<=c.MaxX;x++,index++){
       byte content=cache.Content(index);if(content<=127)continue;var def=MyDefinitionManager.Static.GetVoxelMaterialDefinition(cache.Material(index));
       if(def==null||string.IsNullOrWhiteSpace(def.MinedOre)||def.MinedOre=="Stone")continue;
       active.Clusters.Add(x,y,z,def.MinedOre,content/255.0*512);
      }
     });active.ReadDone=!active.Chunks.Advance();return;
    }
    if(!active.Clusters.Step(4096))return;
    foreach(var c in active.Clusters.Results.OrderByDescending(c=>c.Volume).Take(256)){
     // Convert storage coordinates through the game's voxel transform, including storage offset.
     var voxel=active.Map.Voxel;var coord=(active.Min+new Vector3I(c.MinX,c.MinY,c.MinZ))*8;Vector3D world;
     MyVoxelCoordSystems.VoxelCoordToWorldPosition(voxel.PositionComp.WorldMatrixRef,voxel.WorldMatrix.Translation,voxel.SizeInMetresHalf,ref coord,out world);
     var half=new Vector3D((c.MaxX-c.MinX+1)*4,(c.MaxY-c.MinY+1)*4,(c.MaxZ-c.MinZ+1)*4);
     world+=Vector3D.TransformNormal(half,voxel.WorldMatrix);
     active.Map.Deposits.Add(new Deposit{Ore=c.Ore,Center=world,Radius=half.Length(),Volume=c.Volume});
    }
    active.Map.Updated=frame;maps[active.Map.Id]=active.Map;Status="Deposits: "+active.Map.Deposits.Count+" mapped regions (approximate)";job=null;
   }catch(Exception ex){if(job!=null){job.Map.Updated=frame;job.Map.Deposits.Clear();maps[job.Map.Id]=job.Map;}job=null;Status="Deposits: scan unavailable / budget limit";Plugin.Log(Status+": "+ex.Message);}
  }
  internal List<OreOverlayDeposit> Project(VoxelSurveyor survey,HudSettings settings,OreSearchConfig search){
   var result=new List<OreOverlayDeposit>();if(search==null||!search.Values.B("DepositMarkers",true)||!settings.MarkersEnabled)return result;
   var camera=MyAPIGateway.Session?.Camera;if(camera==null)return result;
   double range=OreGeometry.Safe(search.Values.D("DepositRangeKm",5),.1,5,5)*1000;
   foreach(var map in maps.Values){var r=survey.GetRecord(map.Id);if(r==null||r.Skipped||r.Distance>range||map.Voxel.Closed||!ReferenceEquals(map.Storage,map.Voxel.Storage))continue;
    foreach(var d in map.Deposits){if(!settings.IsOreEnabled(d.Ore)||!settings.GetOreShowPing(d.Ore))continue;bool off;var p=ScreenUtils.WorldToScreen(d.Center,out off);if(off||ScreenUtils.IsOutsideHud(p)||!OreLearningStore.Finite(p.X)||!OreLearningStore.Finite(p.Y))continue;
     bool edgeOff;var edge=ScreenUtils.WorldToScreen(d.Center+camera.WorldMatrix.Right*d.Radius,out edgeOff);double rx=Math.Abs(edge.X-p.X);var top=ScreenUtils.WorldToScreen(d.Center+camera.WorldMatrix.Up*d.Radius,out edgeOff);double ry=Math.Abs(top.Y-p.Y);
     if(!OreLearningStore.Finite(rx)||!OreLearningStore.Finite(ry))continue;
     result.Add(new OreOverlayDeposit{EntityId=map.Id,Ore=d.Ore,ScreenX=p.X,ScreenY=p.Y,RadiusX=Math.Min(2,rx),RadiusY=Math.Min(2,ry),DistanceMeters=Vector3D.Distance(camera.Position,d.Center),AsteroidDistanceMeters=r.Distance,DiameterMeters=d.Radius*2,EstimatedVolume=d.Volume,Color=settings.GetOreColor(d.Ore)});
    }
   }return result.OrderBy(d=>d.AsteroidDistanceMeters).ThenByDescending(d=>d.EstimatedVolume).Take(Math.Max(0,Math.Min(20,search.Values.I("MaxDepositMarkers",4)))).ToList();
  }
 }
}
