using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
namespace ZeoOreShared {
 // Only the completed public client contract is inspected. No network requests.
 internal sealed class SdxScanData {
  internal object Identity; internal double Total;
  internal readonly Dictionary<string,double> Ore=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
  internal static object Member(object value,string name) {
   if(value==null)return null;var t=value.GetType();var f=t.GetField(name,BindingFlags.Instance|BindingFlags.Public);
   if(f!=null)return f.GetValue(value);var p=t.GetProperty(name,BindingFlags.Instance|BindingFlags.Public);return p!=null&&p.GetIndexParameters().Length==0?p.GetValue(value,null):null;
  }
  internal static SdxScanData Read(object asteroid,long expectedId,bool scanning) {
   try {
    if(scanning||Convert.ToInt64(Member(asteroid,"EntityId"))!=expectedId||Convert.ToInt32(Member(asteroid,"ScanType"))!=2)return null;
    var data=Member(asteroid,"ScanData");if(data==null||Convert.ToInt32(Member(data,"ScanType"))!=2)return null;
    double total=Convert.ToDouble(Member(data,"Volume"));if(!OreLearningStore.Finite(total)||total<=0||total>1e15)return null;
    var raw=Member(data,"Ore");var ore=(raw as IDictionary)??(Member(raw,"Dictionary") as IDictionary);
    if(ore==null||ore.Count>64)return null;
    var result=new SdxScanData{Identity=data,Total=total};double sum=0;
    foreach(DictionaryEntry entry in ore){var name=entry.Key as string;double volume=Convert.ToDouble(entry.Value);
     if(string.IsNullOrWhiteSpace(name)||name.Length>64||!OreLearningStore.Finite(volume)||volume<0||volume>total*1.001)return null;
     sum+=volume;result.Ore.Add(name,volume);
    }
    return sum<=total*1.001?result:null;
   }catch{return null;}
  }
 }
}
