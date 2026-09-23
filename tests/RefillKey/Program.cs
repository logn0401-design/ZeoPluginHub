using System;
using System.IO;
using System.Linq;
using ZeoCore;
using ZeoOverlay;
namespace ZeoCore {internal static class Plugin{internal static readonly string DataDirectory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"fixture-"+Guid.NewGuid().ToString("N"));internal static void Log(string s){}}}
internal static class Program
{
 static int checks;
 static void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}
 static void Reject(Action action,string label){bool failed=false;try{action();}catch(ArgumentException){failed=true;}Check(failed,label);}
 static int Main(){try{
  Check(new HudSettings().QuickRefillKey==0&&new OverlaySettings().QuickRefillKey==0,"Old settings default to unbound");
  foreach(var key in QuickRefillBinding.Keys){Check(key==0||Enum.IsDefined(typeof(VRage.Input.MyKeys),(byte)key),"Real MyKeys value "+key);Check(QuickRefillBinding.Keys[QuickRefillBinding.Index(key)]==key,"Stable persisted key "+key);}
  for(int m=0;m<5;m++)for(int mask=0;mask<8;mask++)Check(QuickRefillBinding.MatchModifiers(m,(mask&1)!=0,(mask&2)!=0,(mask&4)!=0)==(mask==new[]{0,1,2,4,5}[m]),"Exact modifier matching");
  var latch=new QuickRefillKeyLatch();Func<bool,bool,bool,bool> poll=(down,allowed,mods)=>latch.Poll(119,1,down,mods,allowed,false);
  Check(!poll(false,true,true),"Initialize without firing");Check(poll(true,true,true),"One keydown starts/cancels");
  for(int i=0;i<100;i++)Check(!poll(true,true,true),"Hold does not repeat");
  poll(false,true,true);Check(poll(true,true,true),"Release/repress invokes cancel");
  poll(false,false,true);Check(!poll(true,false,true),"Chat/menu/focus blocks action");Check(!poll(true,true,true),"Closing menu while held does not fire");poll(false,true,true);Check(poll(true,true,true),"Fresh press after closing menu works");
  poll(false,true,true);Check(!poll(true,true,false),"Wrong modifiers blocked");Check(!poll(true,true,true),"Adding modifier while held does not fire");
  Check(!latch.Poll(120,1,true,true,true,false),"Changing binding while held ignored");latch.Poll(120,1,false,true,true,false);Check(latch.Poll(120,1,true,true,true,false),"New binding after release works");
  Check(QuickRefillBinding.Conflict(117,0,true,1)!=null,"Distress F6 collision");Check(QuickRefillBinding.Conflict(36,0,false,1)!=null,"Menu HOME collision");
  Check(QuickRefillBinding.Conflict(119,0,true,1)==null,"Ctrl F8 available");
  Directory.CreateDirectory(Plugin.DataDirectory);var initial=OverlaySettings.Load(HudSettings.PathName);initial.SchemaVersion=20;initial.ShipHudWidth=1.7;initial.AmmoHudHeight=1.3;initial.WantPdc40=4321;initial.Save();
  var model=new ZeoNativeSettingsModel(HudSettings.PathName,null);var keyOption=ZeoNativeCatalog.Options.Single(x=>x.Key=="QuickRefillKey");var modifierOption=ZeoNativeCatalog.Options.Single(x=>x.Key=="QuickRefillModifier");
  model.Apply(keyOption,QuickRefillBinding.Index(119));model.Apply(modifierOption,4);
  var client=HudSettings.Load();Check(client.QuickRefillKey==119&&client.QuickRefillModifier==4,"Native settings reach runtime model");client.WantPdc40=5432;client.Save();
  var overlay=OverlaySettings.Load(HudSettings.PathName);Check(overlay.QuickRefillKey==119&&overlay.QuickRefillModifier==4&&overlay.WantPdc40==5432,"Runtime save preserves key and unrelated edits");
  Check(Math.Abs(overlay.ShipHudWidth-1.7)<.001&&Math.Abs(overlay.AmmoHudHeight-1.3)<.001,"Resize sidecar preserved");
  string before=File.ReadAllText(HudSettings.PathName);Reject(()=>model.Apply(keyOption,QuickRefillBinding.Index(117)),"Reject binding on enabled distress key");Check(File.ReadAllText(HudSettings.PathName)==before,"Conflict never commits");
  Reject(()=>model.Apply(ZeoNativeCatalog.Options.Single(x=>x.Key=="DistressKey"),3),"Changing distress onto refill key also rejected");
  model.Apply(keyOption,0);Check(HudSettings.Load().QuickRefillKey==0,"Unbind persists");
  overlay=OverlaySettings.Load(HudSettings.PathName);overlay.QuickRefillKey=999999;overlay.QuickRefillModifier=-1;overlay.Save();Check(HudSettings.Load().QuickRefillKey==0&&HudSettings.Load().QuickRefillModifier==1,"Invalid saved bind normalizes safely");
  Console.WriteLine("PASS: "+checks+" key, modifier, held-input, collision and real-settings checks.");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
