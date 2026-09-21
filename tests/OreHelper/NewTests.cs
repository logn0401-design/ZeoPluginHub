using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using ZeoOreShared;
using ZeosOreOverlay;
using ZeosOreHelper;
using VRageMath;
internal static partial class Tests {
 static OreObservation Observation(string id,double volume,bool verified=true,string ore="Uranium",double percent=5){return new OreObservation{Asteroid=id,Ore=ore,Volume=volume,Percent=percent,Verified=verified,Lod=2,Samples=50,TotalSamples=1000,SolidSamples=1000,UtcTicks=DateTime.UtcNow.Ticks};}
 static void Learning(){
  string location=Path.Combine(dir,"learning"),key=OreLearningStore.WorldKey("world-a");var l=new OreLearningStore(location,key);
  Check(OreLearningStore.WorldKey("world-b")!=key,"isolated world identities");
  Check(l.Observe(Observation("coarse",90000,false),true),"coarse record requests verification");Check(l.Best("Uranium").Count==0,"coarse excluded from baseline");
  l.Observe(Observation("one",100),true);l.Observe(Observation("two",200),true);Check(l.Best("Uranium").Count<3,"sparse fallback");
  l.Observe(Observation("three",300),true);Check(l.Best("Uranium").Volume==300,"three distinct verified seed baseline");
  l.Observe(Observation("four",1000),true);Check(l.Best("Uranium").Volume==300,"active search remains stable");l.Save(true);
  var frozen=new OreLearningStore(location,key,true);Check(frozen.Best("Uranium").Volume==300,"frozen persisted active baseline");
  Check(!frozen.Observe(Observation("five",10000),false)&&frozen.Count==5,"frozen ignores new records");
  var reopen=new OreLearningStore(location,key);Check(reopen.Best("Uranium").Volume==1000,"next auto session uses latest");
  l.UseLatest();Check(l.Best("Uranium").Volume==1000,"explicit refresh");l.Save(true);Check(new OreLearningStore(location,key,true).Best("Uranium").Volume==1000,"explicit refresh persisted");
  Check(!l.Observe(Observation("four",20000,false),true),"coarse revisit cannot replace verified");l.UseLatest();Check(l.Best("Uranium").Volume==1000,"verified not downgraded");
  Check(!l.Observe(Observation("nan",double.NaN),true),"NaN rejected");Check(!l.Observe(Observation("negative",-1),true),"negative rejected");
  Check(new OreLearningStore(location,OreLearningStore.WorldKey("world-b")).Count==0,"world file isolation");
  for(int i=0;i<500;i++)l.Observe(Observation("more"+i,i*10),true);l.Save(true);Check(l.Count<=192,"bounded history per ore");
  l.Reset();Check(l.Count==0&&l.Best("Uranium").Volume==0,"reset clears active and observations");Check(new OreLearningStore(location,key,true).Count==0,"reset persisted");
  Check(OreLearningStore.Volume(1,0,1)==1&&OreLearningStore.Volume(.5,3,1)==256,"content-weighted physical volume");
  File.WriteAllText(Path.Combine(location,"learning-broken.json"),"{broken");var bad=new OreLearningStore(location,"broken");Check(bad.Count==0&&bad.LastError.Length>0,"corrupt history contained and reported");
 }
 static void Search(){
  var settings=new OreOverlaySettings(Path.Combine(dir,"match.ini"));settings.Set("SearchSort","Most estimated ore");settings.Set("BenchmarkPercent",80);
  var config=new OreSearchConfig(settings);var store=new OreLearningStore(Path.Combine(dir,"search-data"),"fixture");
  for(int i=0;i<3;i++){store.Observe(Observation("u"+i,1000,true,"Uranium",10),true);store.Observe(Observation("i"+i,100000,true,"Iron",80),true);}
  var measured=new[]{new OreMeasure{Ore="Uranium",Volume=850,Percent=7},new OreMeasure{Ore="Iron",Volume=90000,Percent=60}};
  Check(OreSearch.Match(measured,new[]{"Uranium"},false,store,config).Qualifies,"volume qualifies");
  settings.Set("SearchSort","Richest ore");Check(!OreSearch.Match(measured,new[]{"Uranium"},false,store,config).Qualifies,"richness distinct from volume");
  settings.Set("SearchSort","Most estimated ore");settings.Set("OreBenchmarkPercent:Iron",95);
  var any=OreSearch.Match(measured,new[]{"Uranium","Iron"},false,store,config);Check(any.Qualifies&&any.Best.Ore=="Uranium","passing ore precedes larger below-threshold ore");
  Check(!OreSearch.Match(measured,new[]{"Uranium","Iron"},true,store,config).Qualifies,"all ores must pass");
  Check(!OreSearch.Match(measured,new[]{"Uranium","Gold"},true,store,config).Qualifies,"missing ore fails all");
  Check(!OreSearch.Match(measured,new string[0],false,store,config).HasWanted,"no selection gives no pings");
  Check(!OreSearch.Match(measured,new[]{"Gold"},false,store,config).HasWanted,"disabled ores ignored");
  var empty=new OreLearningStore(Path.Combine(dir,"empty-data"),"empty");var sparse=OreSearch.Match(measured,new[]{"Uranium"},false,empty,config);Check(sparse.Qualifies&&sparse.Learning&&sparse.Threshold==0,"sparse data never filters all");
 }
 static void Chunks(){
  foreach(var size in new[]{new[]{1,1,1},new[]{17,33,7},new[]{64,32,16},new[]{67,65,35}})foreach(int edge in new[]{16,32,64}){
   var chunks=new OreScanChunks(size[0],size[1],size[2],edge);var seen=new HashSet<int>();long sum=0;
   do{var c=chunks.Current;int cells=(c.MaxX-c.X+1)*(c.MaxY-c.Y+1)*(c.MaxZ-c.Z+1);Check(cells<=edge*edge*edge,"bounded voxel brick");sum+=cells;
    for(int z=c.Z;z<=c.MaxZ;z++)for(int y=c.Y;y<=c.MaxY;y++)for(int x=c.X;x<=c.MaxX;x++)if(!seen.Add(x+size[0]*(y+size[1]*z)))throw new Exception("scan overlaps");
   }while(chunks.Advance());Check(sum==(long)size[0]*size[1]*size[2]&&sum==seen.Count,"complete nonoverlapping voxel coverage");
  }
 }
 static void FrameBudget(){
  var s=new HudSettings{Enabled=true,MaxMarkers=8,SurveyRangeMeters=1000000,MinimumGrade="X"};
  var values=new OreOverlaySettings(Path.Combine(dir,"frame.ini"));var config=new OreSearchConfig(values);Plugin.Instance=new Plugin{Search=config,Learning=new OreLearningStore(Path.Combine(dir,"frame-data"),"world")};
  var survey=new VoxelSurveyor();for(int i=0;i<24;i++)survey.Records.Add(new VoxelSurveyor.RoidRecord{EntityId=i+1,Pinned=i<12,Position=new Vector3D(i==23?0:i<5?2:.3+i*.001,0,0),Distance=1000*(24-i),MaxDimensionMeters=500,Ores=new List<VoxelSurveyor.OreStat>{new VoxelSurveyor.OreStat{Ore="Uranium",EstimatedVolume=(i+1)*100,PercentOfSolid=5}}});
  var builder=new OreOverlayFrameBuilder();var frame=builder.Build(survey,s);Check(frame.Roids.Count(r=>r.PingEligible)==8,"pinned count stays within max");Check(frame.Roids.Count(r=>r.PingEligible&&r.Offscreen)<=2,"offscreen subcap");Check(frame.Roids.Count(r=>r.DetailLabel)<=3,"label subcap");Check(frame.Roids.Single(r=>r.EntityId==24).PingEligible,"aimed asteroid prioritized within cap");
  s.MaxMarkers=0;frame=builder.Build(survey,s);Check(frame.ShownPings==0&&frame.Roids.All(r=>!r.PingEligible),"zero hides pinned and targeted");
  s.MaxMarkers=8;foreach(var r in survey.Records)r.Pinned=false;values.Set("SearchSort","Nearest matching");frame=builder.Build(survey,s);Check(frame.Roids.First().EntityId==24,"nearest matching ordering");
  values.Set("SearchSort","Most estimated ore");survey.Records[0].Ores[0].EstimatedVolume=100000;frame=builder.Build(survey,s);Check(frame.Roids.First().EntityId==1,"largest estimated volume ordering");
  values.Set("SearchSort","Richest ore");survey.Records[2].Ores[0].PercentOfSolid=20;frame=builder.Build(survey,s);Check(frame.Roids.First().EntityId==3,"richest percentage ordering");
  foreach(string ore in HudSettings.KnownOres)s.SetOreEnabled(ore,false);frame=builder.Build(survey,s);Check(frame.Roids.Count==0&&frame.SearchMessage.Contains("Select"),"no selected ore clear state");Plugin.Instance=null;
 }
 static void Selections(){
  var p=Path.Combine(dir,"selection.ini");var model=new OreUiModel(null,p);model.SelectAll(false);model.Current.Set("OreEnabled:Uranium",true);model.Current.Set("SelectionName","Reactor");model.Current.Set("OreColor:Uranium","#112233");model.Current.Save();model.Selection(true);model.SelectAll(true);model.Selection(false);
  Check(model.Current.B("OreEnabled:Uranium")&&!model.Current.B("OreEnabled:Iron"),"selection restores toggles");Check(model.Current.Get("ActivePreset")=="Reactor","saved selection name");Check(model.Current.Get("OreColor:Uranium")=="#112233","selection preserves appearance");
  var draft=new OreLayoutModel(model.Current);draft.ResetPosition();Check(draft.Draft.X==-.72&&draft.Draft.Y==-.64,"reset draft position");draft.Undo();Check(draft.Changes().Count==0,"undo reset position");
 }
}
