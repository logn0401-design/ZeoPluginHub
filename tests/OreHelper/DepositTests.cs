using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.Serialization;
using System.Web.Script.Serialization;
using VRageMath;
using Color=System.Drawing.Color;
using RectangleF=System.Drawing.RectangleF;
using ZeoOreShared;
using ZeosOreHelper;
using ZeosOreOverlay;
internal static partial class Tests {
 public sealed class Wrapper {public Dictionary<string,float> Dictionary{get;set;}=new Dictionary<string,float>();}
 public sealed class Scan {public int ScanType=2;public float Volume=10240;public Wrapper Ore=new Wrapper();}
 public sealed class Asteroid {public long EntityId=7;public int ScanType=2;public Scan ScanData=new Scan();}
 static void SdxScans(){
  var a=new Asteroid();a.ScanData.Ore.Dictionary.Add("Uranium",1024);
  var scan=SdxScanData.Read(a,7,false);Check(scan!=null&&scan.Ore["Uranium"]==1024&&scan.Total==10240,"completed SDX snapshot contract");
  Check(SdxScanData.Read(a,8,false)==null,"wrong asteroid rejected");Check(SdxScanData.Read(a,7,true)==null,"active scan not completed");
  a.ScanType=1;Check(SdxScanData.Read(a,7,false)==null,"basic metadata not quantities");a.ScanType=2;a.ScanData.ScanType=1;Check(SdxScanData.Read(a,7,false)==null,"incomplete payload rejected");a.ScanData.ScanType=2;
  foreach(float value in new[]{float.NaN,float.PositiveInfinity,-1,11000}){a.ScanData.Ore.Dictionary["Uranium"]=value;Check(SdxScanData.Read(a,7,false)==null,"invalid ore volume rejected");}
  a.ScanData.Ore.Dictionary["Uranium"]=6000;a.ScanData.Ore.Dictionary["Iron"]=6000;Check(SdxScanData.Read(a,7,false)==null,"ore sum exceeds solid volume");a.ScanData.Ore.Dictionary.Remove("Iron");
  a.ScanData.Volume=0;Check(SdxScanData.Read(a,7,false)==null,"empty unfinished scan rejected");a.ScanData.Volume=10240;
  a.ScanData.Ore.Dictionary.Clear();for(int i=0;i<65;i++)a.ScanData.Ore.Dictionary.Add("ore"+i,1);Check(SdxScanData.Read(a,7,false)==null,"bounded native dictionary");
  Check(SdxScanData.Read(null,7,false)==null&&SdxScanData.Read(new object(),7,false)==null,"missing mod and changed schema contained");
  string key=OreLearningStore.WorldKey("sdx-test");var local=new OreLearningStore(Path.Combine(dir,"separate"),key);var native=new OreLearningStore(Path.Combine(dir,"separate","SDX2"),key);
  for(int i=0;i<3;i++){local.Observe(Observation("a"+i,100),true);native.Observe(Observation("a"+i,1000),true);}local.Save(true);native.Save(true);
  Check(new OreLearningStore(Path.Combine(dir,"separate"),key).Best("Uranium").Volume==100&&new OreLearningStore(Path.Combine(dir,"separate","SDX2"),key).Best("Uranium").Volume==1000,"quantity methods have separate persisted baselines");
 }
 static void Deposits(){
  var c=new OreDepositClusters();c.Add(0,0,0,"Uranium",512);c.Add(1,0,0,"Uranium",256);c.Add(2,0,0,"Iron",512);c.Add(1,1,1,"Uranium",512);c.Add(511,511,511,"Uranium",512);
  Check(!c.Step(1)&&!c.Complete,"grouping is incremental");int steps=0;while(!c.Step(1)&&steps++<100){}Check(c.Complete&&c.Results.Count==4,"six-neighbour deposits stay distinct by ore and gap");
  var merged=c.Results.Single(x=>x.Cells==2);Check(merged.Volume==768&&merged.MinX==0&&merged.MaxX==1&&merged.MaxZ==0,"cluster retains content-weighted volume and bounds");
  Check(c.Results.Sum(x=>x.Volume)==2304,"components conserve sampled ore volume");Reject(()=>OreDepositClusters.Key(-1,0,0),"negative coordinate rejected");Reject(()=>OreDepositClusters.Key(512,0,0),"coordinate overflow rejected");
  var empty=new OreDepositClusters();Check(empty.Step(1)&&empty.Results.Count==0,"empty map complete");
  var dense=new OreDepositClusters();for(int z=0;z<16;z++)for(int y=0;y<16;y++)for(int x=0;x<16;x++)dense.Add(x,y,z,"Ice",512);
  steps=0;while(!dense.Step(64)&&steps++<150){}Check(dense.Complete&&dense.Results.Count==1&&dense.Results[0].Cells==4096&&steps>1,"bounded dense grouping crosses scan bricks");
  var limit=new OreDepositClusters();for(int i=0;i<200000;i++)limit.Add(i%512,(i/512)%512,i/262144,"Iron",1);bool rejected=false;try{limit.Add(0,0,1,"Iron",1);}catch(InvalidOperationException){rejected=true;}Check(rejected,"deposit memory cap enforced");
 }
 static void DepositFrames(){
  var settings=new OreOverlaySettings(Path.Combine(dir,"deposits.ini"));settings.Set("HideBelowThreshold",false);
  var plugin=new Plugin{Search=new OreSearchConfig(settings)};Plugin.Instance=plugin;
  var survey=new VoxelSurveyor();var r=new VoxelSurveyor.RoidRecord{EntityId=7,Position=Vector3D.Zero,Distance=100,MaxDimensionMeters=128,Ores=new List<VoxelSurveyor.OreStat>{new VoxelSurveyor.OreStat{Ore="Uranium",EstimatedVolume=100,PercentOfSolid=10}}};survey.Records.Add(r);
  var a=new Asteroid();a.ScanData.Ore.Dictionary.Add("Uranium",1024);r.SdxScan=SdxScanData.Read(a,7,false);
  for(int i=0;i<4;i++)plugin.Deposits.Items.Add(new ZeosOreHelper.OreOverlayDeposit{EntityId=7,Ore="Uranium",ScreenX=i*.1,AsteroidDistanceMeters=100,DistanceMeters=100,EstimatedVolume=512,DiameterMeters=32,RadiusX=.05,RadiusY=.08,Color="#65FF73"});
  var hud=new HudSettings{Enabled=true,MarkersEnabled=true,MaxMarkers=8,MinimumGrade="X"};var builder=new OreOverlayFrameBuilder();
  foreach(int max in new[]{0,1,3,8}){hud.MaxMarkers=max;var f=builder.Build(survey,hud);Check(f.ShownPings==Math.Min(5,max)&&f.Deposits.Count+f.Roids.Count(x=>x.PingEligible)==f.ShownPings,"one shared cap "+max);}
  hud.MaxMarkers=8;var frame=builder.Build(survey,hud);Check(frame.Roids.Single().EstimatedVolume==1024&&frame.Roids.Single().ScanStatus=="SDX2 EST","native quantity displayed with source");settings.Set("PreferSdxScans",false);frame=builder.Build(survey,hud);Check(frame.Roids.Single().EstimatedVolume==100,"native source toggle falls back locally");
  hud.MarkersEnabled=false;Check(builder.Build(survey,hud).ShownPings==0,"master markers disables both");hud.MarkersEnabled=true;
  string json=new JavaScriptSerializer().Serialize(frame);var overlayFrame=new JavaScriptSerializer().Deserialize<ZeosOreOverlay.OreOverlayFrame>(json);Check(overlayFrame.Deposits.Count==4&&overlayFrame.Deposits[0].DiameterMeters==32,"deposit protocol roundtrip");
  var form=(HudOverlayForm)FormatterServices.GetUninitializedObject(typeof(HudOverlayForm));typeof(HudOverlayForm).GetField("_s",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(form,settings);GC.SuppressFinalize(form);
  Action<ZeosOreOverlay.OreOverlayFrame,OrePingBudget> draw=(f,b)=>{using(var bitmap=new Bitmap(1280,720))using(var g=Graphics.FromImage(bitmap))Method(typeof(HudOverlayForm),"DrawDeposits").Invoke(form,new object[]{g,1280,720,f,b,new List<RectangleF>()});};
  overlayFrame.Deposits=overlayFrame.Deposits.Take(1).ToList();var d=overlayFrame.Deposits[0];d.DetailLabel=true;
  foreach(double distance in new[]{5000d,45d,0d}){d.AsteroidDistanceMeters=distance;d.DistanceMeters=distance;var budget=new OrePingBudget(8,2,3);draw(overlayFrame,budget);Check(budget.Count==1,"deposit remains visible at "+distance+"m");}
  d.AsteroidDistanceMeters=5001;var rejectedBudget=new OrePingBudget(8,2,3);draw(overlayFrame,rejectedBudget);Check(rejectedBudget.Count==0,"5km range enforced");d.AsteroidDistanceMeters=100;
  settings.Set("OreEnabled:Uranium",false);rejectedBudget=new OrePingBudget(8,2,3);draw(overlayFrame,rejectedBudget);Check(rejectedBudget.Count==0,"live ore selection honored");settings.Set("OreEnabled:Uranium",true);
  d.ScreenX=double.NaN;rejectedBudget=new OrePingBudget(8,2,3);draw(overlayFrame,rejectedBudget);Check(rejectedBudget.Count==0,"invalid projection rejected");d.ScreenX=.18;d.ScreenY=.2;d.DistanceMeters=185;d.DiameterMeters=96;d.RadiusX=.065;d.RadiusY=.11;
  foreach(var size in new[]{new Size(1280,720),new Size(1920,1080),new Size(3440,1440)})using(var bitmap=new Bitmap(size.Width,size.Height))using(var g=Graphics.FromImage(bitmap)){g.Clear(Color.FromArgb(10,16,26));Method(typeof(HudOverlayForm),"DrawPings").Invoke(form,new object[]{g,size.Width,size.Height,overlayFrame});bitmap.Save(Path.Combine(dir,"deposits-"+size.Width+"x"+size.Height+".png"),ImageFormat.Png);}
 }
}
