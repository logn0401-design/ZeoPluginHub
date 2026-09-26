using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
namespace ZeoOreShared {
 internal static class OreIni {
  internal static Dictionary<string,string> Parse(IEnumerable<string> lines) {
   var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
   foreach(var raw in lines) { var line=raw.Trim(); int eq=line.IndexOf('='); if(eq<1||line.StartsWith("#")||line.StartsWith(";"))continue; result[line.Substring(0,eq).Trim()]=line.Substring(eq+1).Trim(); } return result;
  }
  internal static Dictionary<string,string> Read(string path) { return File.Exists(path)?Parse(File.ReadAllLines(path)):new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase); }
  internal static Dictionary<string,string> Save(string path,Dictionary<string,string> desired,Dictionary<string,string> baseline) {
   var changes=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
   foreach(var p in desired) { string old; if(baseline==null||!baseline.TryGetValue(p.Key,out old)||old!=p.Value)changes[p.Key]=p.Value; }
   return Merge(path,changes);
  }
  internal static Dictionary<string,string> Merge(string path,Dictionary<string,string> changes) {
   string name; using(var sha=SHA256.Create())name=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant()))).Replace("-","");
   using(var gate=new Mutex(false,"Local\\ZeoOreSettings_"+name)) {
    bool held=false; string temp=null;
    try {
     try { held=gate.WaitOne(3000); } catch(AbandonedMutexException) { held=true; }
     if(!held)throw new IOException("Settings are busy. Try again.");
     var values=Read(path); foreach(var p in changes)values[p.Key]=p.Value;
     if(changes.Count==0 && File.Exists(path))return values;
     Directory.CreateDirectory(Path.GetDirectoryName(path)); temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
     File.WriteAllLines(temp,new[]{"# Zeos Ore Helper v0.7.2 - settings shared by native UI and overlay"}.Concat(values.OrderBy(p=>p.Key,StringComparer.OrdinalIgnoreCase).Select(p=>p.Key+"="+p.Value)),Encoding.UTF8);
     if(File.Exists(path))File.Replace(temp,path,path+".previous",true);else File.Move(temp,path);
     var saved=Read(path); foreach(var p in changes) { string v; if(!saved.TryGetValue(p.Key,out v)||v!=p.Value)throw new IOException("Could not verify "+p.Key); } return saved;
    } finally { if(temp!=null&&File.Exists(temp))File.Delete(temp); if(held)gate.ReleaseMutex(); }
   }
  }
 }
}
