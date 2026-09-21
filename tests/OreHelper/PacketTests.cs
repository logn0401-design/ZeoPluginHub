using System;
using System.Reflection;
using System.Runtime.Serialization;
using System.Web.Script.Serialization;
using System.Text;
using System.Linq;
using ZeosOreHelper;
internal static partial class Tests {
 static void PacketSize(){
  // Exercise the real serializer without binding/sending to the user's overlay.
  var bridge=(OreOverlayBridge)FormatterServices.GetUninitializedObject(typeof(OreOverlayBridge));
  typeof(OreOverlayBridge).GetField("_json",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(bridge,new JavaScriptSerializer{MaxJsonLength=8*1024*1024});
  var frame=new OreOverlayFrame();for(int i=0;i<100;i++)frame.Roids.Add(new OreOverlayRoid{EntityId=i,Selected=i==99,PingEligible=i<8||i==99,ListEligible=true,TopOre=new string('X',800),ScanStatus="VERIFIED SCAN"});
  var data=bridge.Encode(new OreOverlayPacket{Kind="frame",Frame=frame});
  Check(data.Length<=62000,"packet bounded below UDP ceiling");var parsed=new JavaScriptSerializer().Deserialize<OreOverlayPacket>(Encoding.UTF8.GetString(data));
  Check(parsed.Frame.Roids.Any(r=>r.Selected&&r.EntityId==99),"oversized packet retains target");Check(parsed.Frame.Roids.Count(r=>r.PingEligible)==9,"oversized packet retains priority markers");Check(parsed.Frame.ShownPings==9,"packet marker count consistent");
 }
}
