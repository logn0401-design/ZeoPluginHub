using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using ZeoOreShared;
using ZeosOreOverlay;
using ZeosOreHelper;
using VRageMath;
internal static partial class Tests {
 static void SimplePings(){
  var settings=new OreOverlaySettings(Path.Combine(dir,"simple.ini"));
  var form=(HudOverlayForm)FormatterServices.GetUninitializedObject(typeof(HudOverlayForm));typeof(HudOverlayForm).GetField("_s",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(form,settings);GC.SuppressFinalize(form);
  var roid=new ZeosOreOverlay.OreOverlayRoid{Number=17,Grade="S",TopOre="Uranium",TopOrePercent=12,DistanceMeters=1250,EstimatedVolume=250000,ScanStatus="VERIFIED SCAN"};
  Func<string> label=()=> (string)Method(typeof(HudOverlayForm),"PingLabel").Invoke(form,new object[]{roid});
  Check(label()=="Uranium  1.3 km"||label()=="Uranium  1.2 km","simple ore and distance only");Check(!label().Contains("VERIFIED")&&!label().Contains("%")&&!label().Contains("250"),"simple strips extra statistics");
  roid.Selected=true;Check(label().Contains("~250k m3")&&!label().Contains("%"),"only aimed target gets amount");
  settings.Set("PingShowAmount",false);Check(!label().Contains("m3"),"amount preference honored");settings.Set("PingShowAmount",true);
  settings.Set("PingLabelStyle","Simple");Check(!label().Contains("m3"),"fully simple mode");
  settings.Set("PingLabelStyle","Custom");Check(label().Contains("17")&&label().Contains("VERIFIED SCAN")&&label().Contains("12.00%"),"all old information remains in custom mode");
  settings.Set("PingLabelStyle","Simple");roid.DistanceMeters=45;Check(label()=="Uranium  45 m","nearby distance stays visible in metres");
  var model=new OreUiModel(null,settings.PathName);model.Apply(OreUiCatalog.Options.SingleForTest("MinimumDistanceMeters"),20d);Check(model.Current.D("MinimumDistanceMeters")==20000,"minimum units convert to kilometres");
  model.Apply(OreUiCatalog.Options.SingleForTest("MinimumDistanceMeters"),0d);Check(model.Current.D("MinimumDistanceMeters")==0,"include nearby action clears only minimum");
  var survey=new VoxelSurveyor();survey.Records.Add(new VoxelSurveyor.RoidRecord{EntityId=999,Position=new Vector3D(0,0,0),Distance=19999,MaxDimensionMeters=100,Ores=new System.Collections.Generic.List<VoxelSurveyor.OreStat>{new VoxelSurveyor.OreStat{Ore="Uranium",EstimatedVolume=100,PercentOfSolid=5}}});
  Plugin.Instance=new Plugin{Search=new OreSearchConfig(settings)};var hud=new HudSettings{Enabled=true,MinimumDistanceMeters=20000,MinimumGrade="X",MaxMarkers=8};var builder=new OreOverlayFrameBuilder();
  var frame=builder.Build(survey,hud);Check(frame.ShownPings==0&&frame.SearchMessage.Contains("20 km hidden"),"20km approach cutoff explained");
  hud.MinimumDistanceMeters=0;frame=builder.Build(survey,hud);Check(frame.ShownPings==1,"approaching asteroid visible after clear");survey.Records[0].Distance=45;frame=builder.Build(survey,hud);Check(frame.ShownPings==1,"close approach does not distance-fade marker");Plugin.Instance=null;
 }
}
internal static class TestCatalogLookup {internal static OreOption SingleForTest(this OreOption[] options,string key){return System.Linq.Enumerable.Single(options,o=>o.Key==key);}}
