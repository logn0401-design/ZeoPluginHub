using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
class Program {
 static Assembly core,math;static string bin;static int checks;
 const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
 static object New(string name){return Activator.CreateInstance(core.GetType("ZeoCore."+name),true);}
 static object Get(object o,string n){var f=o.GetType().GetField(n,Flags);return f!=null?f.GetValue(o):o.GetType().GetProperty(n,Flags).GetValue(o);}
 static void Set(object o,string n,object v){var f=o.GetType().GetField(n,Flags);if(f!=null)f.SetValue(o,v);else o.GetType().GetProperty(n,Flags).SetValue(o,v);}
 static object Call(object o,string n,params object[] args){return o.GetType().GetMethod(n,Flags).Invoke(o,args);}
 static void Collection(object o,string n){var f=o.GetType().GetField(n,Flags);f.SetValue(o,Activator.CreateInstance(f.FieldType,true));}
 static object Vector(double x,double y=0,double z=0){return Activator.CreateInstance(math.GetType("VRageMath.Vector3D"),new object[]{x,y,z});}
 static void Check(bool ok,string m){checks++;if(!ok)throw new Exception(m);}
 static int Main(string[] args){try{
  bin=args[0];AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{var p=Path.Combine(bin,new AssemblyName(e.Name).Name+".dll");return File.Exists(p)?Assembly.LoadFrom(p):null;};
  core=Assembly.LoadFrom(args[1]);math=Assembly.LoadFrom(Path.Combine(bin,"VRage.Math.dll"));
  var controller=FormatterServices.GetUninitializedObject(core.GetType("ZeoCore.ZeoHudController"));
  var engine=FormatterServices.GetUninitializedObject(core.GetType("ZeoCore.ZeoCoreEngine"));
  Set(engine,"_tacticalShareSync",new object());Collection(engine,"_sharedSpectrum");Set(controller,"_engine",engine);
  var settings=New("HudSettings");Set(controller,"_settings",settings);Set(settings,"ShowLocalSpectrum",true);Set(settings,"ShareSpectrumSignals",false);
  foreach(var field in new[]{"_spectrumCache","_spectrumPersistent","_tracks","_trackByEntity","_fusionBuckets","_ids"})Collection(controller,field);
  var spectrum=New("SpectrumClient");Set(controller,"_spectrum",spectrum);
  Set(spectrum,"_getClientDetections",new Func<byte[]>(()=>new byte[0]));
  var type=core.GetType("ZeoCore.SpectrumClient+DetectionData");var listType=typeof(System.Collections.Generic.List<>).MakeGenericType(type);IList incoming=null;
  Action<long,int,double> sample=(id,tick,x)=>{
   var d=Activator.CreateInstance(type);Set(d,"EmitterId",id);Set(d,"DetectedAt",tick);Set(d,"Position",Vector(x));Set(d,"Velocity",Vector(60));
   incoming=(IList)Activator.CreateInstance(listType);incoming.Add(d);
  };
  sample(1001,120,1000);Call(controller,"ApplySpectrumSnapshot",incoming,120);
  Check(((IList)Get(controller,"_spectrumCache")).Count==1,"first authoritative signal");
  Call(controller,"ApplySpectrumSnapshot",incoming,180);Check((double)Call(controller,"SpectrumAgeSeconds",1001L,180)==1,"polling cannot refresh an old observation");
  sample(1002,180,1060);Call(controller,"ApplySpectrumSnapshot",incoming,180);
  var cached=(IDictionary)Get(controller,"_spectrumPersistent");Check(cached.Count==1&&!cached.Contains(1001L)&&cached.Contains(1002L),"replacement removes retired signal instead of ghost hold");
  sample(1002,240,1120);Call(controller,"ApplySpectrumSnapshot",incoming,240);Check(cached.Count==1,"new observation updates same signal");
  // Protobuf encodes an empty list as zero bytes; Spectrum does the same.
  Call(controller,"RefreshSpectrumCache",250);
  Check(((IList)Get(controller,"_spectrumCache")).Count==0,"authoritative empty snapshot removes final signal");
  Set(settings,"ShowNeutrals",true);Set(settings,"ShowFriendlyMarkers",true);Set(settings,"ShowLocalSpectrum",false);Set(settings,"ShowLocalWeaponCore",true);Set(settings,"ShowFleetFriendlies",true);Set(settings,"TacticalProcessingCap",192);Set(settings,"MaxRangeKm",10000.0);
  var local=New("LocalHudSnapshot");Set(local,"HasShip",true);Set(local,"OwnGridId",99L);var fleet=New("FleetPictureSnapshot");var wc=(IList)Get(local,"WeaponCoreTracks");var friendly=(IList)Get(fleet,"Friendlies");
  Func<long,object> track=id=>{var t=New("HudTrack");Set(t,"EntityId",id);Set(t,"Position",Vector(1000+id*1000));Set(t,"SameSector",true);Set(t,"SectorId","fixture");Set(t,"Relation","unknown");return t;};
  Set(local,"SectorKnown",true);Set(local,"SectorId","fixture");wc.Add(track(2000));Call(controller,"BuildTracks",300,local,fleet);
  var tracks=(IList)Get(controller,"_tracks");Check(tracks.Count==1,"one local track");int number=(int)Get(tracks[0],"TrackId");
  wc.Clear();friendly.Add(track(2000));Call(controller,"BuildTracks",306,local,fleet);Check(tracks.Count==1&&(int)Get(tracks[0],"TrackId")==number,"real fusion preserves number across WC to fleet handoff");
  friendly.Clear();for(int i=0;i<192;i++)wc.Add(track(3000+i));Call(controller,"BuildTracks",312,local,fleet);Check(tracks.Count==192,"heavy-load processing cap");
  var ids=new System.Collections.Generic.HashSet<int>();foreach(var t in tracks)ids.Add((int)Get(t,"TrackId"));Check(ids.Count==192,"actual fusion has no repeated active IDs");
  var sw=Stopwatch.StartNew();for(int i=0;i<1000;i++)Call(controller,"BuildTracks",320+i,local,fleet);sw.Stop();
  Console.WriteLine("192-contact production fusion, 1000 builds: "+sw.Elapsed.TotalMilliseconds.ToString("0.0")+"ms; "+(sw.Elapsed.TotalMilliseconds/1000).ToString("0.000")+"ms/build (offline, reflection included)");
  Console.WriteLine("PASS "+checks+" production cache/fusion assertions");return 0;
 }catch(Exception e){Console.WriteLine(e);return 1;}}
}
