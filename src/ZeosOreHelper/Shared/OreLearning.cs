using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using ZeosOreOverlay;
namespace ZeoOreShared {
 internal sealed class OreSearchConfig {
  internal readonly OreOverlaySettings Values;
  internal OreSearchConfig(OreOverlaySettings values){Values=values;}
  internal bool Legacy {get{return Values.Get("SearchSort","Most estimated ore")=="Legacy quality";}}
  internal bool AutoLearn {get{return Values.Get("LearningMode","Auto-learn")=="Auto-learn";}}
  internal bool HideLow {get{return Values.B("HideBelowThreshold",true);}}
  internal double Threshold(string ore){double v=Values.D("OreBenchmarkPercent:"+ore);return Math.Max(1,Math.Min(100,v>0?v:Values.D("BenchmarkPercent",80)))/100;}
 }
 public sealed class OreObservation {
  public string Asteroid{get;set;} public string Ore{get;set;} public double Volume{get;set;} public double Percent{get;set;}
  public int Lod{get;set;} public int Samples{get;set;} public long TotalSamples{get;set;} public long SolidSamples{get;set;}
  public bool Verified{get;set;} public long UtcTicks{get;set;}
 }
 public sealed class OreLearningFile {public int Schema{get;set;}=1;public string World{get;set;} public Dictionary<string,OreSavedBenchmark> Benchmarks{get;set;}=new Dictionary<string,OreSavedBenchmark>();public List<OreObservation> Records{get;set;}=new List<OreObservation>();}
 public sealed class OreSavedBenchmark {public double Volume{get;set;}public double Percent{get;set;}public int Count{get;set;}}
 internal sealed class OreBenchmark {internal double Volume,Percent;internal int Count;}
 internal sealed class OreLearningStore {
  private readonly string path,world;private OreLearningFile data;private readonly Dictionary<string,OreBenchmark> active=new Dictionary<string,OreBenchmark>(StringComparer.OrdinalIgnoreCase);
  private bool dirty;private DateTime nextSave;internal string LastError="";
  internal static string WorldKey(string identity){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-","").ToLowerInvariant();}
  internal OreLearningStore(string directory,string worldKey,bool frozen=false){world=worldKey;path=Path.Combine(directory,"learning-"+worldKey+".json");data=new OreLearningFile{World=world};Load();if(frozen && data.Benchmarks!=null){foreach(var b in data.Benchmarks)if(b.Value!=null&&Finite(b.Value.Volume)&&b.Value.Volume>=0&&Finite(b.Value.Percent)&&b.Value.Percent>=0&&b.Value.Percent<=100&&b.Value.Count>=0)active[b.Key]=new OreBenchmark{Volume=b.Value.Volume,Percent=b.Value.Percent,Count=b.Value.Count};}else UseLatest();}
  internal int Count {get{return data.Records.Select(r=>r.Asteroid).Distinct().Count();}}
  internal int VerifiedCount {get{return data.Records.Where(r=>r.Verified).Select(r=>r.Asteroid).Distinct().Count();}}
  internal OreBenchmark Best(string ore){OreBenchmark value;return active.TryGetValue(ore,out value)?value:new OreBenchmark();}
  internal void UseLatest(){dirty=true;active.Clear();foreach(var group in data.Records.Where(r=>r.Verified).GroupBy(r=>r.Ore,StringComparer.OrdinalIgnoreCase)){var b=new OreBenchmark{Volume=group.Max(r=>r.Volume),Percent=group.Max(r=>r.Percent),Count=group.Select(r=>r.Asteroid).Distinct().Count()};active[group.Key]=b;}}
  internal bool Observe(OreObservation observation,bool autoLearn){
   if(!autoLearn||!Valid(observation))return false;
   var old=data.Records.FirstOrDefault(r=>r.Asteroid==observation.Asteroid&&r.Ore.Equals(observation.Ore,StringComparison.OrdinalIgnoreCase));
   // An ordinary coarse revisit must not downgrade a finer observation.
   if(old!=null&&old.Verified&&!observation.Verified)return false;
   if(old!=null)data.Records.Remove(old);data.Records.Add(observation);dirty=true;
   Prune();
   // Fill an empty learning baseline once enough distinct fine scans exist, then hold it stable.
   var current=Best(observation.Ore);if(current.Count<3){var verified=data.Records.Where(r=>r.Verified&&r.Ore.Equals(observation.Ore,StringComparison.OrdinalIgnoreCase)).ToArray();if(verified.Length>=3)active[observation.Ore]=new OreBenchmark{Volume=verified.Max(r=>r.Volume),Percent=verified.Max(r=>r.Percent),Count=verified.Length};}
   return !observation.Verified&&(current.Volume<=0||observation.Volume>current.Volume*1.1||observation.Percent>current.Percent*1.1);
  }
  internal void Reset(){data.Records.Clear();active.Clear();dirty=true;Save(true);}
  internal void Save(bool force=false){if(!dirty||(!force&&DateTime.UtcNow<nextSave))return;nextSave=DateTime.UtcNow.AddSeconds(5);try{data.Benchmarks=active.ToDictionary(b=>b.Key,b=>new OreSavedBenchmark{Volume=b.Value.Volume,Percent=b.Value.Percent,Count=b.Value.Count});Directory.CreateDirectory(Path.GetDirectoryName(path));string tmp=path+".tmp";File.WriteAllText(tmp,new JavaScriptSerializer{MaxJsonLength=8*1024*1024}.Serialize(data));if(File.Exists(path))File.Replace(tmp,path,path+".previous",true);else File.Move(tmp,path);dirty=false;LastError="";}catch(Exception ex){LastError=ex.Message;}}
  private void Load(){if(!File.Exists(path))return;try{if(new FileInfo(path).Length>8*1024*1024)throw new IOException("Learning file exceeds limit");var loaded=new JavaScriptSerializer{MaxJsonLength=8*1024*1024}.Deserialize<OreLearningFile>(File.ReadAllText(path));if(loaded==null||loaded.Schema!=1||loaded.World!=world)throw new IOException("Learning file world/schema mismatch");loaded.Records=(loaded.Records??new List<OreObservation>()).Where(Valid).GroupBy(r=>r.Asteroid+"|"+r.Ore.ToUpperInvariant()).Select(g=>g.OrderByDescending(r=>r.Verified).ThenByDescending(r=>r.UtcTicks).First()).ToList();data=loaded;Prune();}catch(Exception ex){LastError=ex.Message;}}
  private void Prune(){data.Records=data.Records.GroupBy(r=>r.Ore,StringComparer.OrdinalIgnoreCase).SelectMany(g=>g.Where(r=>r.Verified).OrderByDescending(r=>r.Volume).Take(64).Concat(g.Where(r=>r.Verified).OrderByDescending(r=>r.Percent).Take(64)).Concat(g.OrderByDescending(r=>r.UtcTicks).Take(64)).Distinct()).ToList();}
  internal static bool Valid(OreObservation r){return r!=null&&!string.IsNullOrWhiteSpace(r.Asteroid)&&r.Asteroid.Length<=256&&!string.IsNullOrWhiteSpace(r.Ore)&&r.Ore.Length<=64&&Finite(r.Volume)&&r.Volume>=0&&Finite(r.Percent)&&r.Percent>=0&&r.Percent<=100&&r.Lod>=0&&r.Lod<=9&&r.Samples>=0&&r.SolidSamples>=0&&r.TotalSamples>=r.SolidSamples;}
  internal static bool Finite(double value){return !double.IsNaN(value)&&!double.IsInfinity(value);}
  internal static double Volume(double contentUnits,int lod,double voxelMetres){return contentUnits*Math.Pow((1<<lod)*voxelMetres,3);}
 }
 internal static class OreSelectionProfiles {
  internal static void Save(string path,string slot,OreOverlaySettings settings){if(slot!="1"&&slot!="2"&&slot!="3")throw new ArgumentException("Choose a slot");var selected=string.Join(",",OreOverlaySettings.KnownOres.Where(o=>settings.B("OreEnabled:"+o,true)));OreIni.Merge(path,new Dictionary<string,string>{{"Slot"+slot,selected},{"Name"+slot,settings.Get("SelectionName","Selection "+slot)}});}
  internal static void Load(string path,string slot,OreOverlaySettings settings){var values=OreIni.Read(path);string text;if(!values.TryGetValue("Slot"+slot,out text))throw new ArgumentException("This selection slot is empty");var ores=new HashSet<string>(text.Split(','),StringComparer.OrdinalIgnoreCase);foreach(var ore in OreOverlaySettings.KnownOres)settings.Set("OreEnabled:"+ore,ores.Contains(ore));string name;values.TryGetValue("Name"+slot,out name);settings.Set("SelectionName",string.IsNullOrWhiteSpace(name)?"Selection "+slot:name);settings.Set("ActivePreset",settings.Get("SelectionName"));settings.Save();}
 }
}
