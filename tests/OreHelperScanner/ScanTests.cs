using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using VRageMath;
using ZeoOreShared;
using ZeosOreHelper;
using ZeosOreOverlay;
using Sandbox.Game.Entities;
using NativeSession=AsteroidScanner.Data.Scripts.AsteroidScanner.Session;
using Asteroid=AsteroidScanner.Data.Scripts.AsteroidScanner.Asteroid;
internal static class ScanTests {
 static int checks;static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
 static int Main(string[] args){try{
  var settings=new OreOverlaySettings(Path.Combine(args[0],"scanner.ini"));var search=new OreSearchConfig(settings);var hud=new HudSettings();
  var voxel=new MyVoxelMap();voxel.Storage.Ore[new Vector3I(31,1,1)]=1;voxel.Storage.Ore[new Vector3I(32,1,1)]=1;voxel.Storage.Ore[new Vector3I(40,1,1)]=2;
  var record=new VoxelSurveyor.RoidRecord{EntityId=7,Voxel=voxel,Position=Vector3D.Zero,Distance=5000};var survey=new VoxelSurveyor();survey.Records.Add(record);var mapper=new OreDepositSurveyor();
  mapper.Update(6,survey,search);Check(mapper.Project(survey,hud,search).Count==0,"partial scan never exposed");
  for(int frame=12;frame<=120;frame+=6)mapper.Update(frame,survey,search);
  var markers=mapper.Project(survey,hud,search);Check(markers.Count==2,"completed scan maps disconnected ores");Check(voxel.Storage.Reads==8&&voxel.Storage.MaxRead==32768,"one bounded brick per update");
  var uranium=markers.Single(m=>m.Ore=="Uranium");Check(uranium.EstimatedVolume==1024&&Math.Abs(uranium.ScreenX)<1e-9,"cluster joins across brick boundary at correct center");Check(Math.Abs(uranium.DiameterMeters-Math.Sqrt(96)*2)<1e-6,"extent derived from cell bounds not ore volume");
  mapper.Update(2004,survey,search);Check(mapper.Project(survey,hud,search).Count==2,"old completed map retained while refreshing");
  record.Distance=5001;mapper.Update(2010,survey,search);Check(mapper.Project(survey,hud,search).Count==0,"leaving 5km drops map and job");record.Distance=100;
  for(int frame=2016;frame<2130;frame+=6)mapper.Update(frame,survey,search);Check(mapper.Project(survey,hud,search).Count==2,"reentry remaps");
  hud.Disabled.Add("Uranium");Check(mapper.Project(survey,hud,search).Count==1,"deposit ore selection");hud.Disabled.Clear();
  voxel.Storage=new FakeStorage();Check(mapper.Project(survey,hud,search).Count==0,"replaced storage invalidates map immediately");mapper.Reset();Check(mapper.Project(survey,hud,search).Count==0,"world reset clears locations");
  voxel.Storage.Fail=true;mapper.Update(2142,survey,search);Check(mapper.Project(survey,hud,search).Count==0&&mapper.Status.Contains("unavailable"),"read failure contained with no partial data");voxel.Storage.Fail=false;
  mapper.Reset();voxel.StorageMin=new Vector3I(32);voxel.StorageMax=new Vector3I(64);voxel.Storage.Ore[new Vector3I(4,4,4)]=1;
  mapper.Update(2202,survey,search);mapper.Update(2208,survey,search);var offset=mapper.Project(survey,hud,search).Single();Check(Math.Abs(offset.ScreenX-(-220d/1000))<1e-9,"nonzero storage minimum used in read and coordinate conversion");
  mapper.Reset();voxel.StorageMax=new Vector3I(4096);mapper.Update(2214,survey,search);Check(mapper.Project(survey,hud,search).Count==0&&mapper.Status.Contains("budget"),"oversized asteroid bounded");
  NativeSession.Instance=new NativeSession();var native=new Asteroid{EntityId=7,Position=record.Position};NativeSession.Instance.Asteroids[1]=new List<Asteroid>{native};var reader=new SdxScanReader();
  var history=new OreLearningStore(Path.Combine(args[0],"SDX2"),OreLearningStore.WorldKey("fake-session"));reader.Update(60,survey,history,search);Check(record.SdxScan!=null&&record.SdxScan.Ore["Uranium"]==1024&&history.Count==1,"reader imports completed matching scan and learns");
  NativeSession.Instance.CurrentlyScanning=native;reader.Update(120,survey,history,search);Check(record.SdxScan==null,"ongoing native rescan excluded");NativeSession.Instance.CurrentlyScanning=null;
  native.Position=new Vector3D(1000);reader.Update(180,survey,history,search);Check(record.SdxScan==null,"mismatched world position rejected");native.Position=record.Position;
  NativeSession.Instance=new NativeSession();reader.Update(240,survey,history,search);Check(record.SdxScan==null,"new native session cannot reuse old snapshot");
  NativeSession.Instance.Asteroids[1]=new List<Asteroid>{native};reader.Update(300,survey,history,search);Check(record.SdxScan!=null,"new session imports only its own entries");
  settings.Set("PreferSdxScans",false);reader.Update(360,survey,history,search);Check(record.SdxScan==null,"native source disabled clears snapshot");settings.Set("PreferSdxScans",true);
  NativeSession.Instance=null;reader.Update(420,survey,history,search);Check(record.SdxScan==null&&reader.Status.Contains("not loaded"),"missing mod falls back safely");
  Console.WriteLine("PASS "+checks+" scanner/adapter assertions (fake game inputs)");return 0;
 }catch(Exception ex){Console.WriteLine(ex);return 1;}}
}
