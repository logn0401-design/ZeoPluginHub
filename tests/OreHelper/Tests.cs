using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using ZeoOreShared;
using ZeosOreHelper;
using ZeosOreOverlay;
using OreOverlayFrame=ZeosOreOverlay.OreOverlayFrame;
using OreOverlayRoid=ZeosOreOverlay.OreOverlayRoid;

internal static partial class Tests {
 static int checks;static string dir;
 static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
 static void Reject(Action a,string message){bool rejected=false;try{a();}catch(ArgumentException){rejected=true;}Check(rejected,message);}
 static MethodInfo Method(Type t,string name){return t.GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic);}
 static HudSettings ReadHud(string p){var h=new HudSettings();Method(typeof(HudSettings),"ReadFrom").Invoke(h,new object[]{p});return h;}
 static void SaveHud(HudSettings h,string p){Method(typeof(HudSettings),"SaveTo").Invoke(h,new object[]{p});}
 static Dictionary<string,string> Snapshot(HudSettings h){return OreIni.Parse((List<string>)Method(typeof(HudSettings),"Serialize").Invoke(h,null));}
 [STAThread] static int Main(string[] args){try{dir=Path.GetFullPath(args[0]);Directory.CreateDirectory(dir);Settings();Catalog();KeyBinding();Layout();ResizeLayout();Render();Startup();Learning();Search();Chunks();FrameBudget();Selections();PacketSize();SimplePings();SdxScans();Deposits();DepositFrames();Console.WriteLine("PASS "+checks+" assertions");return 0;}catch(Exception ex){Console.WriteLine(ex);return 1;}}
 static void Startup(){var s=new OreOverlaySettings(Path.Combine(dir,"startup.ini"));s.Set("StreamerMode",true);s.Save();var form=new HudOverlayForm(s,0);try{Check(form.IsHandleCreated,"hidden HWND exists for commands and capture checks");Check(!form.Visible&&!NativeMethods.IsWindowVisible(form.Handle),"startup remains hidden");Method(typeof(HudOverlayForm),"RenderTick").Invoke(form,null);Check(!NativeMethods.IsWindowVisible(form.Handle),"no telemetry stays hidden");}finally{Method(typeof(HudOverlayForm),"OnFormClosed").Invoke(form,new object[]{new System.Windows.Forms.FormClosedEventArgs(System.Windows.Forms.CloseReason.ApplicationExitCall)});form.Dispose();}}
 static void Settings(){
  string p=Path.Combine(dir,"merge.ini");var seed=new OreOverlaySettings(p);seed.Save();
  OreIni.Merge(p,new Dictionary<string,string>{{"UnknownFutureKey","keep"},{"UiWindowX","312"}});
  var plugin=ReadHud(p);var menuA=new OreOverlaySettings(p);var menuB=new OreOverlaySettings(p);
  menuA.Set("HudAccentColor","#123456");menuA.Save();menuB.Set("PanelX",.321);menuB.Save();plugin.Enabled=true;SaveHud(plugin,p);
  var got=OreIni.Read(p);Check(got["HudAccentColor"]=="#123456","plugin must retain concurrent palette");Check(got["PanelX"]=="0.321","plugin must retain concurrent layout");Check(got["Enabled"]=="True","plugin intended enabled write");Check(got["UiWindowX"]=="312"&&got["UnknownFutureKey"]=="keep","unknown + overlay-only keys preserved");
  var first=Snapshot(ReadHud(p));SaveHud(ReadHud(p),p);var second=Snapshot(ReadHud(p));foreach(var kv in first)Check(second[kv.Key]==kv.Value,"roundtrip "+kv.Key);
  var stales=Enumerable.Range(0,8).Select(i=>new OreOverlaySettings(p)).ToArray();Parallel.For(0,8,i=>{stales[i].Set("Concurrent"+i,i);stales[i].Save();});got=OreIni.Read(p);for(int i=0;i<8;i++)Check(got["Concurrent"+i]==i.ToString(),"concurrent writer "+i);
  File.AppendAllLines(p,new[]{"PanelScale=NaN","MarkerScale=Infinity","GradeC=250","GradeB=250","GradeA=250","GradeS=250"});var bad=ReadHud(p);Check(!float.IsNaN(bad.PanelScale)&&!float.IsInfinity(bad.MarkerScale),"finite plugin settings");Check(bad.GradeS>=bad.GradeA+.5&&bad.GradeA>=bad.GradeB+.5&&bad.GradeB>=bad.GradeC+.5,"strict grade thresholds at upper cap");var badOverlay=new OreOverlaySettings(p);Check(badOverlay.D("PanelScale",1)==1,"finite overlay settings");
  var presetPath=Path.Combine(dir,"profiles.ini");var presets=new OreOverlaySettings(presetPath);presets.Set("OreMinPercent:Uranium",.345);presets.Set("OreColor:Uranium","#123456");presets.Set("OreShowPing:Uranium",false);presets.Save();foreach(var profile in new[]{"Balanced","Reactor Run","Ice Run","Advanced Materials","Mass Build","Rare Metals","Everything"}){presets.ApplyPreset(profile);Check(presets.D("OreMinPercent:Uranium")==.345&&presets.Get("OreColor:Uranium")=="#123456"&&!presets.B("OreShowPing:Uranium"),"profile preserves detail "+profile);}
 }
 static void Catalog(){
  string p=Path.Combine(dir,"catalog.ini");var seed=new OreOverlaySettings(p);seed.Save();var model=new OreUiModel(null,p);
  Check(OreUiCatalog.Options.Select(o=>o.Key).Distinct().Count()==OreUiCatalog.Options.Length,"unique catalog keys");
  foreach(var page in OreUiCatalog.Pages)Check(OreUiCatalog.Options.Any(o=>o.Tab==page),"working page "+page);
  foreach(var option in OreUiCatalog.Options){
   if(option.Kind==OreOptionKind.Action)continue;
   if(option.Kind==OreOptionKind.Number){foreach(double v in new[]{option.Min,option.Max})Check(Convert.ToDouble(option.Parse(v.ToString(CultureInfo.InvariantCulture)))==v,"numeric endpoints "+option.Key);foreach(string bad in new[]{"NaN","Infinity","-Infinity","",(option.Min-1).ToString(CultureInfo.InvariantCulture),(option.Max+1).ToString(CultureInfo.InvariantCulture)})Reject(()=>option.Parse(bad),"reject number "+option.Key);}
   if(option.Kind==OreOptionKind.Color){Check((string)option.Parse("abcdef")=="#ABCDEF","six-digit hex");Reject(()=>option.Parse("#GG0011"),"bad hex rejected");}
   object value=option.Kind==OreOptionKind.Boolean?(object)!model.Current.B(option.ActualKey):option.Kind==OreOptionKind.Number?(object)option.Min:option.Kind==OreOptionKind.Color?(object)"#A1B2C3":option.Kind==OreOptionKind.Choice?(object)option.Choices[option.Choices.Length-1]:(object)"ORE TEST LCD";
   // Grade rows are deliberately ordered and cross-validated, checked separately below.
   if(option.Key=="GradeS"||option.Key=="GradeA"||option.Key=="GradeB"||option.Key=="GradeC")continue;
   model.Apply(option,value);var reloaded=new OreOverlaySettings(p);Check(reloaded.Get(option.ActualKey)==model.Current.Get(option.ActualKey),"apply/persist "+option.Key);
  }
  foreach(string ore in HudSettings.KnownOres){OreUiModel.SelectedOre=ore;foreach(var option in OreUiCatalog.Options.Where(o=>o.Key.EndsWith(":"))){object value=option.Kind==OreOptionKind.Boolean?(object)false:option.Kind==OreOptionKind.Color?(object)"#112233":(object)(option.Key=="OreWeight:"?17:option.Key=="OreBenchmarkPercent:"?70:.123);model.Apply(option,value);Check(model.Current.Get(option.ActualKey)==new OreOverlaySettings(p).Get(option.ActualKey),"per-ore persistence "+option.ActualKey);}}
  model.Reload();var grade=OreUiCatalog.Options.Single(o=>o.Key=="GradeS");Reject(()=>model.Apply(grade,1.5),"reject reversed grades");
  var theme=OreUiCatalog.Options.Single(o=>o.Key=="ThemePreset");foreach(var choice in theme.Choices){model.Apply(theme,choice);Check(model.Current.Get("ThemePreset")==choice,"theme "+choice);}
  var legacyKeys=OreIni.Read(p);var typed=Snapshot(ReadHud(p));var catalogue=new HashSet<string>(OreUiCatalog.Options.SelectMany(o=>o.Key.EndsWith(":")?HudSettings.KnownOres.Select(ore=>o.Key+ore):new[]{o.Key}),StringComparer.OrdinalIgnoreCase);foreach(var key in typed.Keys)Check(catalogue.Contains(key),"every persisted plugin field accessible: "+key);
 }
 static void Layout(){
  var s=new OreOverlaySettings(Path.Combine(dir,"geometry.ini"));s.Save();byte[] original=File.ReadAllBytes(s.PathName);
  foreach(var size in new[]{new[]{1280,720},new[]{1920,1080},new[]{3440,1440}})foreach(int rows in new[]{0,8,20})foreach(string style in new[]{"Prospector","Compact","Wide"})foreach(double scale in new[]{.5,1,2}){
   s.Set("HudLayout",style);s.Set("PanelScale",scale);var model=new OreLayoutModel(s);var bounds=OreGeometry.Bounds(s,size[0],size[1],rows,model.Draft);
   Check(bounds.X>=8&&bounds.Y>=8&&bounds.X+bounds.Width<=size[0]-7.99&&bounds.Y+bounds.Height<=size[1]-7.99,"bounds inside viewport");
   foreach(var position in new[]{new[]{-9999d,-9999d},new[]{99999d,99999d},new[]{400d,180d}}){model.Move(position[0],position[1],bounds.Width,bounds.Height,size[0],size[1]);var moved=OreGeometry.Bounds(s,size[0],size[1],rows,model.Draft);Check(moved.X>=8&&moved.Y>=8&&moved.X+moved.Width<=size[0]-7.99,"move clamped");}
   Check(model.Changes().Keys.All(k=>k=="PanelX"||k=="PanelY"),"dirty positions only");model.Undo();Check(model.Changes().Count==0,"undo no writes");Check(File.ReadAllBytes(s.PathName).SequenceEqual(original),"preview/cancel no writes");
  }
 }
 static void ResizeLayout(){
  var settings=new OreOverlaySettings(Path.Combine(dir,"resize.ini"));var model=new OreLayoutModel(settings);
  var b=new OreBounds{X=100,Y=200,Width=700,Height=350};
  Check(b.Edges(100,200)==5&&b.Edges(400,350)==0,"Ore edge grips separate from move");
  model.Resize(b,10,350,175,1,1,1920,1080);
  Check(model.Draft.WidthScale==1.5&&model.Draft.HeightScale==1.5,"Ore corner grows both axes");
  var change=model.Changes();Check(change.ContainsKey("HudWidth")&&change.ContainsKey("HudHeight")&&!change.ContainsKey("TextScale"),"Ore resize does not mutate font preference");
  model.Resize(b,2,-350,0,1,1,1920,1080);Check(model.Draft.WidthScale==.5&&model.Draft.HeightScale==1,"Ore horizontal resize isolated");
  model.Resize(b,2,0,0,1,1,1920,1080);Check(model.Draft.WidthScale==1,"Ore shrink then grow recovers");
  model.Undo();Check(model.Changes().Count==0,"Ore undo restores geometry");
  settings.Set("HudWidth",3);settings.Set("HudHeight",3);var bounded=OreGeometry.Bounds(settings,1280,720,20);Check(bounded.X+bounded.Width<=1280&&bounded.Y+bounded.Height<=720,"Ore large resize remains within viewport");
 }
 static void Render(){
  string p=Path.Combine(dir,"render.ini");var s=new OreOverlaySettings(p);s.ApplyTheme("SE NATIVE");s.Set("FrameStyle","SE Native");s.Set("PanelX",-.42);s.Set("PanelY",-.38);s.Set("PingTextScale",1.4);s.Save();
  var form=(HudOverlayForm)FormatterServices.GetUninitializedObject(typeof(HudOverlayForm));typeof(HudOverlayForm).GetField("_s",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,s);GC.SuppressFinalize(form);
  var frame=new OreOverlayFrame{HelperEnabled=true,VisibleCount=24,ReadyCount=18,PendingCount=6,CachedCount=12};
  string[] ores={"Uranium","Tungsten","Titanium","Platinum","Gold","Copper","Ice","Iron"};for(int i=0;i<8;i++)frame.Roids.Add(new OreOverlayRoid{EstimatedVolume=1250000+i*30000,ScanStatus=i<3?"VERIFIED SCAN":"ESTIMATED",DetailLabel=i<3,Number=i+1,ScreenX=-.65+i*.18,ScreenY=.35+(i%2)*.22,DistanceMeters=12000+i*1750,DiameterMeters=450+i*40,Grade=i<3?"S":"A",State="READY",GradeColor=i<3?"#4BFF73":"#73EB87",OreColor="#AADDFF",TopOre=ores[i],TopOrePercent=6.35+i,SecondOre="Iron",SecondOrePercent=24.3,ListEligible=true,PingEligible=true,Selected=i==0,Pinned=i==1});
  foreach(var size in new[]{new[]{1280,720},new[]{1920,1080},new[]{3440,1440}}){using(var bmp=new Bitmap(size[0],size[1],PixelFormat.Format32bppPArgb))using(var g=Graphics.FromImage(bmp)){g.Clear(Color.Transparent);Method(typeof(HudOverlayForm),"DrawHud").Invoke(form,new object[]{g,size[0],size[1],frame});Check(bmp.GetPixel(size[0]-2,size[1]-2).A==0,"transparent HUD corners");bmp.Save(Path.Combine(dir,"hud-"+size[0]+"x"+size[1]+".png"),ImageFormat.Png);}}
  foreach(var size in new[]{new[]{.5,1.0},new[]{1.0,2.0},new[]{2.0,.5},new[]{1.0,1.0}}){
   s.Set("HudWidth",size[0]);s.Set("HudHeight",size[1]);using(var bmp=new Bitmap(1920,1080))using(var g=Graphics.FromImage(bmp)){
    var transform=g.Transform.Elements;Method(typeof(HudOverlayForm),"DrawList").Invoke(form,new object[]{g,1920,1080,frame});Check(transform.SequenceEqual(g.Transform.Elements),"Ore resize restores graphics");
    bmp.Save(Path.Combine(dir,"resize-"+size[0]+"x"+size[1]+".png"));
   }
  }
  foreach(double invalid in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity,double.MaxValue,-double.MaxValue}){frame.Roids[0].ScreenX=invalid;frame.Roids[0].ScreenY=invalid;using(var b=new Bitmap(1280,720))using(var g=Graphics.FromImage(b)){Method(typeof(HudOverlayForm),"DrawHud").Invoke(form,new object[]{g,1280,720,frame});Check(true,"invalid projection safe");}}
  frame.Roids[0].ScreenX=0;frame.Roids[0].ScreenY=0;
  foreach(string style in new[]{"Hex Command","Chevron","Split Wing","Razor","War Room","Minimal","SE Native"}){s.Set("FrameStyle",style);using(var b=new Bitmap(1280,720))using(var g=Graphics.FromImage(b)){Method(typeof(HudOverlayForm),"DrawHud").Invoke(form,new object[]{g,1280,720,frame});Check(true,"legacy frame "+style);}}
  s.Set("TextOpacity",999);s.Set("PanelScale",double.NaN);s.Set("MarkerScale",double.PositiveInfinity);using(var b=new Bitmap(1280,720))using(var g=Graphics.FromImage(b)){Method(typeof(HudOverlayForm),"DrawHud").Invoke(form,new object[]{g,1280,720,frame});Check(true,"malformed render settings safe");}
  s.Set("ListEnabled",false);frame.HelperEnabled=false;frame.Layout=new OreLayoutDraft{X=0,Y=0,ToolbarX=0,ToolbarY=0,ToolbarW=1280,ToolbarH=150};using(var b=new Bitmap(1280,720))using(var g=Graphics.FromImage(b)){Method(typeof(HudOverlayForm),"DrawHud").Invoke(form,new object[]{g,1280,720,frame});Check(!s.B("ListEnabled")&&!frame.HelperEnabled,"preview doesn't enable hidden panel");Check(b.GetPixel(640,50).A==0,"toolbar cutout");}
 }
}
