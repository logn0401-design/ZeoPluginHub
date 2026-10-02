using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using ZeoNav;

internal static class Tests
{
    private static string gameBin,overlayPath,scratch;
    private static int checks,failures;
    public static int Main(string[] args)
    {
        gameBin=args[0]; overlayPath=args[1]; scratch=args[2]; Directory.CreateDirectory(scratch);
        AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=> { string p=Path.Combine(gameBin,new AssemblyName(e.Name).Name+".dll"); return File.Exists(p) ? Assembly.LoadFrom(p) : null; };
        return Run();
    }
    private static void Check(bool ok,string message) { checks++; if(!ok) { failures++; Console.WriteLine("FAIL | "+message); } }
    private static void Group(string name) { Console.WriteLine("CHECKED | "+name); }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run()
    {
        string path=Path.Combine(scratch,"config.json");
        var live=new NavConfig(); JsonIo.Save(path,live);
        var store=new NavUiStore(path,()=>live,c=>live=c);
        KeyBindingTests(store);
        var excluded=new HashSet<string> {"ConfigVersion","DriveSlider","AutoFlip","FullStop","SpectrumFeedback","TargetCtrlAim"};
        var fields=typeof(NavConfig).GetFields().Where(f=>f.IsDefined(typeof(DataMemberAttribute),false)).ToArray();
        var catalog=NavUiCatalog.Options;
        Check(catalog.Select(o=>o.Key).Distinct().Count()==catalog.Count,"unique catalog keys");
        foreach(var field in fields) Check(excluded.Contains(field.Name) || catalog.Any(o=>o.Key==field.Name),"field parity "+field.Name);
        foreach(var row in catalog.Where(o=>!o.Key.StartsWith("@")))
        {
            var field=typeof(NavConfig).GetField(row.Key);
            Check(field!=null,"valid target "+row.Key);
            var values=new List<object>();
            if(row.Kind==NavOptionKind.Number) { values.Add(row.Parse(row.Min.ToString(CultureInfo.InvariantCulture))); values.Add(row.Parse((double.IsPositiveInfinity(row.Max)?100000:row.Max).ToString(CultureInfo.InvariantCulture))); }
            else if(row.Kind==NavOptionKind.Boolean) { values.Add(false); values.Add(true); }
            else if(row.Kind==NavOptionKind.Color) values.Add(row.Parse("#a1b2c3"));
            else foreach(string value in row.Choices) values.Add(row.Parse(value));
            foreach(object value in values)
            {
                var draft=live.Copy(); field.SetValue(draft,Convert.ChangeType(value,field.FieldType,CultureInfo.InvariantCulture));
                var copy=JsonIo.FromBytes<NavConfig>(JsonIo.ToBytes(ConfigRules.Clamp(draft)));
                Check(Equals(field.GetValue(copy),field.GetValue(draft)),"catalog roundtrip "+row.Key+"="+value);
            }
            object write=row.Page=="KEYS"?"None":values[values.Count-1];
            store.Apply(NavUiCatalog.Changes(row,write));
            Check(Equals(field.GetValue(store.Read()),Convert.ChangeType(write,field.FieldType,CultureInfo.InvariantCulture)),"persisted target "+row.Key);
        }
        Group("All 64 stored fields accounted for; all editable controls, choices and range endpoints round-trip");
        var sigOption=catalog.Single(o=>o.Key=="MaxDriveSigKm");
        Check(sigOption.Min==0 && double.IsPositiveInfinity(sigOption.Max),"MAX SIG selector permits zero and has no upper ceiling");
        store.Apply(NavUiCatalog.Changes(sigOption,sigOption.Parse("750")));
        Check(store.Read().MaxDriveSigKm==750 && JsonIo.Load<NavConfig>(path).MaxDriveSigKm==750,"750 km survives saved config reload");
        bool sigRejected=false; try { sigOption.Parse("NaN"); } catch { sigRejected=true; }
        Check(sigRejected,"native menu rejects non-finite SIG input");
        Check(ConfigRules.Clamp(new NavConfig {ConfigVersion=7,MaxDriveSigKm=900}).MaxDriveSigKm==900,"stored limits over 750 remain unchanged");
        Check(ConfigRules.Clamp(new NavConfig {ConfigVersion=7,MaxDriveSigKm=125}).MaxDriveSigKm==125,"existing selection stays unchanged");
        Check(ConfigRules.Clamp(new NavConfig {ConfigVersion=4,DriveSlider=100}).MaxDriveSigKm==490,"historical slider migration preserves original intent");
        store.Apply(NavUiCatalog.Changes(sigOption,sigOption.Parse("25000")));
        Check(store.Read().MaxDriveSigKm==25000,"large SIG limit survives saved reload");
        store.Apply(NavUiCatalog.Changes(sigOption,sigOption.Parse("0")));
        Check(store.Read().MaxDriveSigKm==0 && new NavConfig().MaxDriveSigKm==0,"zero is preserved and is the fresh default");
        Group("Uncapped MAX SIG persistence, unlimited default and migration");
        foreach(string key in new[]{"ApproachSigEnabled","ApproachSigKm","ApproachDistanceKm","DepartureSigEnabled","DepartureSigKm","DepartureDistanceKm"})
            Check(catalog.Single(o=>o.Key==key).Page=="ROUTE","zone control belongs on main route screen: "+key);
        Check(NavUiCatalog.Pages.Contains("DOCKING"),"docking has its own native tab");
        store.Apply(new Dictionary<string,object>{{"ApproachSigEnabled",true},{"ApproachSigKm",65d},{"ApproachDistanceKm",110d},{"DepartureSigEnabled",true},{"DepartureSigKm",45d},{"DepartureDistanceKm",80d}});
        var zoneReload=store.Read();
        Check(zoneReload.ApproachSigEnabled&&zoneReload.ApproachSigKm==65&&zoneReload.DepartureSigKm==45&&zoneReload.DepartureDistanceKm==80,"both profiles save independently");
        store.Apply(new Dictionary<string,object>{{"QuickDockKey","NumPad0"}});
        bool duplicate=false;try{store.Apply(new Dictionary<string,object>{{"RefuelKey","NumPad0"}});}catch(ArgumentException){duplicate=true;}
        Check(duplicate&&store.Read().QuickDockKey=="NumPad0"&&store.Read().RefuelKey=="None","duplicate refuel binding fails without changing saved config");
        store.Apply(new Dictionary<string,object>{{"RefuelKey","NumPad1"}});
        Check(store.Read().RefuelKey=="NumPad1","independent refuel hotkey persists");
        var capOption=catalog.Single(o=>o.Key=="SpeedCapOverride");
        store.Apply(NavUiCatalog.Changes(capOption,capOption.Parse("50000")));
        Check(store.Read().SpeedCapOverride==50000,"50k speed cap persists");
        bool badCap=false; try { capOption.Parse("50001"); } catch { badCap=true; }
        Check(badCap,"speed cap above server maximum is rejected by editor");
        var exitLog=new List<string>(); bool committed=false;
        NavUiExit.SaveValid(()=> { committed=true; return true; },exitLog.Add);
        Check(committed && exitLog.Count==0,"valid close attempts save");
        NavUiExit.SaveValid(()=>false,exitLog.Add);
        Check(exitLog.Count==1,"invalid field cannot veto close");
        NavUiExit.SaveValid(()=> { throw new IOException("fixture"); },exitLog.Add);
        Check(exitLog.Count==2,"settings I/O failure cannot veto close");
        Check(ConfigRules.Clamp(new NavConfig {SpeedCapOverride=double.NaN}).SpeedCapOverride==0,"invalid saved speed falls back to AUTO");
        Group("50k speed entry and non-blocking exit policy");
        var numeric=catalog.Single(o=>o.Key=="HudX");
        foreach(string invalid in new[] {"NaN","Infinity","--1","999","-999",""})
        { bool rejected=false; try { numeric.Parse(invalid); } catch { rejected=true; } Check(rejected,"invalid number "+invalid); }
        var color=catalog.Single(o=>o.Key=="HudText");
        foreach(string invalid in new[] {"red","#FFF","#12345678","#GG1122","123456"})
        { bool rejected=false; try { color.Parse(invalid); } catch { rejected=true; } Check(rejected,"invalid RGB "+invalid); }
        store.Apply(new Dictionary<string,object> {{"MaxDriveSigKm",390d},{"StreamerMode",false}});
        var host=new NavUiHost {Store=store}; var model=new NavUiModel(host);
        store.Apply(new Dictionary<string,object> {{"SpeedCapOverride",3210d}});
        model.Apply(catalog.Single(o=>o.Key=="PanelScale"),1.25d);
        Check(store.Read().SpeedCapOverride==3210 && !store.Read().StreamerMode && store.Read().MaxDriveSigKm==390,"reload before targeted write preserves concurrent settings");
        model.Apply(catalog.Single(o=>o.Key=="TripAccent"),"#102030");
        Check(!store.Read().TripUseHudTheme,"trip color disables follow in same write");
        model.Apply(color,"#203040"); Check(store.Read().Theme=="CUSTOM","HUD color selects custom theme");
        var reset=NavUiCatalog.ResetTrip(); store.Apply(reset);
        Check(store.Read().MaxDriveSigKm==390 && store.Read().SpeedCapOverride==3210,"trip reset preserves flight settings");
        Check(!reset.ContainsKey("TripAccent") && !reset.ContainsKey("StreamerMode"),"reset matches legacy reset field scope");
        string json=File.ReadAllText(path); File.WriteAllText(path,"{\"FutureOption\":{\"name\":\"preserve\",\"v\":17},"+json.Substring(1));
        store.Apply(new Dictionary<string,object> {{"HudX",-.43d}});
        Check(File.ReadAllText(path).Contains("FutureOption") && File.ReadAllText(path).Contains("preserve"),"unknown future fields survive targeted persistence");
        string valid=File.ReadAllText(path); File.WriteAllText(path,"{broken");
        bool blocked=false; try { store.Apply(new Dictionary<string,object> {{"HudX",0d}}); } catch { blocked=true; }
        Check(blocked && File.ReadAllText(path)=="{broken","unreadable config is not overwritten"); File.WriteAllText(path,valid);
        Group("Persistence, concurrent edits, compound colors, invalid inputs and future fields");
        for(int i=1;i<=9;i++) Check(NavUiCatalog.Preset(i).Count==2,"position preset "+i);
        Check(NavUiCatalog.Preset(0).Count==0,"keep current preset has no changes");
        var layout=new NavLayoutModel(store.Read());
        Check(layout.Changes().Count==0,"layout starts unchanged");
        foreach(var viewport in new[] {new[]{1280,720},new[]{1920,1080},new[]{3440,1440},new[]{1600,900}})
        {
            layout.Move(viewport[0]*.2,viewport[1]*.25,440,267,viewport[0],viewport[1]);
            Check(Math.Abs(layout.Draft.X+.6)<1e-8 && Math.Abs(layout.Draft.Y-.5)<1e-8,"pixel to normalized mapping "+viewport[0]);
            layout.Move(-999,99999,440,267,viewport[0],viewport[1]);
            Check(Math.Abs(layout.Draft.X)<=.98 && Math.Abs(layout.Draft.Y)<=.98,"viewport clamp "+viewport[0]);
        }
        var start=new NavLayoutBounds{X=100,Y=200,Width=440,Height=267};
        Check(start.Edges(100,200)==5&&start.Edges(300,300)==0,"resize grip hit regions");
        layout.Resize(start,10,220,133.5,1,1,1920,1080);
        Check(Math.Abs(layout.Draft.WidthScale-1.5)<1e-8&&Math.Abs(layout.Draft.HeightScale-1.5)<1e-8,"corner resizes both axes");
        layout.Resize(start,2,-220,0,1,1,1920,1080);
        Check(layout.Draft.WidthScale==.5&&layout.Draft.HeightScale==1,"width only leaves text-height setting intact");
        layout.Resize(start,2,0,0,1,1,1920,1080);
        Check(layout.Draft.WidthScale==1&&layout.Draft.HeightScale==1,"grow from frozen origin recovers starting size");
        Check(ConfigRules.Clamp(new NavConfig{HudWidth=0,HudHeight=0}).HudHeight==1,"old config missing resize fields gets 100 percent");
        layout.Undo(); Check(layout.Changes().Count==0,"undo returns to edit-session start");
        layout.Move(double.NaN,1,440,267,1920,1080); Check(layout.Changes().Count==0,"invalid drag ignored");
        var lockLayout=new NavLayoutModel(store.Read(),true);
        Check(lockLayout.Draft.TargetLock&&lockLayout.Changes().Count==0,"target lock has independent unchanged draft");
        lockLayout.Move(760,40,380,54,1920,1080);
        Check(Math.Abs(lockLayout.Draft.X+.0104166666667)<1e-7&&lockLayout.Changes().ContainsKey("TargetHudX")&&!lockLayout.Changes().ContainsKey("HudX"),"target lock drag saves its center anchor only");
        lockLayout.Resize(new NavLayoutBounds{X=760,Y=40,Width=380,Height=54},10,40,20,1,1,1920,1080);
        Check(!lockLayout.Changes().ContainsKey("HudWidth")&&lockLayout.Changes().ContainsKey("TargetHudWidth")&&lockLayout.Changes().ContainsKey("TargetHudHeight"),"target lock resizes both dimensions independently from trip panel");
        Check(lockLayout.Draft.WidthScale>1&&lockLayout.Draft.HeightScale>1,"target lock corner drag grows both axes");
        var oldDefault=ConfigRules.Clamp(new NavConfig{ConfigVersion=17,TargetHudX=0,TargetHudY=.96});
        Check(oldDefault.TargetHudX>.5&&oldDefault.TargetHudY<-.5,"untouched old top default migrates bottom right");
        var customized=ConfigRules.Clamp(new NavConfig{ConfigVersion=17,TargetHudX=-.4,TargetHudY=.3});
        Check(customized.TargetHudX==-.4&&customized.TargetHudY==.3,"customized target placement survives migration");
        lockLayout.Undo();Check(lockLayout.Changes().Count==0,"target lock undo restores its own position");
        string before=File.ReadAllText(path); layout.Move(500,300,440,267,1920,1080);
        Check(before==File.ReadAllText(path),"preview/cancel have no persistence side effects");
        store.Apply(new Dictionary<string,object>{{"BackingOpacity",111}}); store.Apply(layout.Changes());
        Check(store.Read().BackingOpacity==111,"save moved coordinates preserves concurrent style edits");
        host.Layout=layout.Draft;
        host.Receive(new NavLayoutBounds {Token="wrong",X=0,Y=0,Width=440,Height=267,ViewportW=1920,ViewportH=1080}); Check(host.Bounds==null,"foreign layout token rejected");
        host.Receive(new NavLayoutBounds {Token=layout.Draft.Token,X=0,Y=0,Width=double.NaN,Height=267,ViewportW=1920,ViewportH=1080}); Check(host.Bounds==null,"invalid renderer bounds rejected");
        host.Receive(new NavLayoutBounds {Token=layout.Draft.Token,X=10,Y=20,Width=440,Height=267,ViewportW=1920,ViewportH=1080}); Check(host.Bounds!=null,"matching actual renderer bounds accepted");
        Group("Draft Save / Cancel / Undo, nine presets, viewport mapping, matching feedback");
        var core=NavUiHost.CoreChanges("{\"FrameStyle\":6,\"ThemePreset\":4,\"FontStyle\":2,\"HudTextColor\":\"#AABBCC\",\"PanelOpacity\":123}");
        Check((string)core["Frame"]=="WAR ROOM" && (string)core["HudText"]=="#AABBCC" && Convert.ToInt32(core["BackingOpacity"])==123,"Core appearance mapping");
        Check(!core.ContainsKey("MenuKey") && !core.ContainsKey("MaxDriveSigKm"),"Core import cannot change key bindings or flight ceiling");
        var sent=new List<NavCommand>(); host.Command=c=>sent.Add(c); host.Select(new GpsDto{Name="Fixture",X=1,Y=2,Z=3});
        Check(sent.Count==1 && sent[0].Type=="SELECT_GPS","destination selection does not start flight");
        host.Start(); Check(sent.Last().Type=="START" && sent.Last().Value==store.Read().BufferKm,"explicit start uses current saved buffer");
        host.Select(null); blocked=false; try { host.Start(); } catch {blocked=true;} Check(blocked,"no destination blocks start");
        Group("Read-only Core appearance import and preserved route actions");
        TargetRendererFixtures();
        RendererFixtures();
        Console.WriteLine("RESULT: "+checks+" assertions, "+failures+" failures. Offline tests only; native SE UI and coexistence require an in-game check.");
        return failures==0 ? 0 : 1;
    }
    private static void KeyBindingTests(NavUiStore store)
    {
        Check(store.Read().TargetAimHoldKey=="LeftShift","fresh aim hold defaults to Left Shift");
        Check(ConfigRules.Clamp(new NavConfig{ConfigVersion=18,TargetAimHoldKey="InvalidKey"}).TargetAimHoldKey=="LeftShift","invalid aim hold falls back to Left Shift");
        Check(NavUiCatalog.Options.Any(o=>o.Page=="KEYS"&&o.Key=="TargetAimHoldKey"),"aim hold is changeable on Keys page");
        store.Apply(new Dictionary<string,object>{{"TargetAimHoldKey","K"}});
        Check(store.Read().TargetAimHoldKey=="K","changed aim hold persists");
        bool holdConflict=false;try{store.Apply(new Dictionary<string,object>{{"TargetSelectKey","K"}});}catch(ArgumentException){holdConflict=true;}
        Check(holdConflict,"hold aim and target toggle cannot share a key");
        store.Apply(new Dictionary<string,object>{{"TargetAimHoldKey","LeftControl"}});
        Check(store.Read().TargetAimHoldKey=="LeftControl","existing custom Left Ctrl binding remains available");
        Check(NavKeyBinding.CaptureHoldKey(VRage.Input.MyKeys.LeftControl),"standalone modifier is capturable for aim hold");
        var legacyAim=ConfigRules.Clamp(new NavConfig{ConfigVersion=16,TargetCtrlAim=false});
        Check(legacyAim.ConfigVersion==18&&legacyAim.TargetAimHoldKey=="None","old disabled Left Ctrl migrates to unbound hold key");
        var options=NavUiCatalog.Options;
        foreach(var optionKey in new[]{"FlipAxisMode","FlipTurnMode","RcsFlipAdvantagePct","DampenerEntryMaxMps","TerminalEnvelopeMeters","TerminalCruiseMps","TerminalHandoffMaxMps","TerminalDampeners"})
            Check(options.Any(o=>o.Key==optionKey),"New flight setting visible: "+optionKey);
        var key=NavKeyBinding.Parse("shift+ctrl+k");
        Check(key.Text=="Ctrl+Shift+K"&&key.Matches(true,false,true)&&!key.Matches(true,true,true),"modifier chords canonicalize and match exact modifiers");
        key=NavKeyBinding.Parse("K");
        Check(key.Matches(false,true,false,true)&&!key.Matches(false,true,false),"target picker accepts free-look Alt without broadening other shortcuts");
        foreach(string invalid in new[]{"Ctrl+None","Ctrl+Ctrl+K","Ctrl+","12345","Alt+MadeUpKey"})
        {bool rejected=false;try{NavKeyBinding.Parse(invalid);}catch(ArgumentException){rejected=true;}Check(rejected,"invalid key binding rejected: "+invalid);}
        var draft=new NavKeyDraft("None");draft.Begin();draft.Accept("Ctrl+Shift+K");
        Check(draft.Value=="Ctrl+Shift+K"&&draft.Saved=="None"&&store.Read().InterceptKey=="None","capturing a chord does not save it");
        draft.Cancel();Check(draft.Value=="None"&&!draft.Listening,"Escape discards draft");
        draft.Begin();draft.Accept("Ctrl+Shift+K");store.Apply(new Dictionary<string,object>{{"InterceptKey",draft.Value}});draft.Applied();
        Check(store.Read().InterceptKey=="Ctrl+Shift+K"&&draft.Saved==draft.Value,"explicit Apply persists chord");
        bool conflict=false;try{store.Apply(new Dictionary<string,object>{{"MenuKey","Shift+Ctrl+K"}});}catch(ArgumentException){conflict=true;}
        Check(conflict&&store.Read().MenuKey=="Insert","modifier order cannot bypass menu conflict check");
        store.Apply(new Dictionary<string,object>{{"TargetSelectKey","J"}});
        conflict=false;try{store.Apply(new Dictionary<string,object>{{"MatchVelocityKey","Alt+J"}});}catch(ArgumentException){conflict=true;}
        Check(conflict&&store.Read().MatchVelocityKey=="None","free-look target shortcut conflict is rejected");
        draft.Accept("None");Check(store.Read().InterceptKey=="Ctrl+Shift+K","CLEAR is only a draft until Apply");
        store.Apply(new Dictionary<string,object>{{"InterceptKey","None"},{"TargetSelectKey","None"}});
        Check(!NavKeyBinding.CaptureKey(VRage.Input.MyKeys.Escape)&&!NavKeyBinding.CaptureKey(VRage.Input.MyKeys.LeftAlt)&&NavKeyBinding.CaptureKey(VRage.Input.MyKeys.End),"capture ignores cancel/modifier keys and accepts normal abort key");
        Group("Click-to-bind drafts, modifier matching, persistence and conflicts");
    }
    private static void TargetRendererFixtures()
    {
        var asm=Assembly.LoadFrom(overlayPath);var type=asm.GetType("ZeoNavOverlay.NavSnapshot");
        var snapshot=Activator.CreateInstance(type);
        type.GetField("TargetLabel").SetValue(snapshot,"TEST / HAULER #123456");
        type.GetField("TargetLocked").SetValue(snapshot,true);
        type.GetField("TargetDistance").SetValue(snapshot,5700d);
        type.GetField("TargetRelativeSpeed").SetValue(snapshot,12.5d);
        type.GetField("SpeedMps").SetValue(snapshot,.125d);type.GetField("SpeedCapMps").SetValue(snapshot,.25d);
        var speedText=asm.GetType("ZeoNavOverlay.HudForm").GetMethod("DockSpeedText",BindingFlags.Static|BindingFlags.NonPublic);
        string dockingText=(string)speedText.Invoke(null,new[]{snapshot});
        Check(dockingText.Contains("0.25")&&dockingText.Contains("RCS LIMIT")&&!dockingText.Contains("SHIPCORE"),"Dock HUD shows its actual fractional RCS speed limit");
        type.GetField("SpeedCapMps").SetValue(snapshot,0d);
        Check(((string)speedText.Invoke(null,new[]{snapshot})).Contains("RCS HOLD"),"Dock preparation HUD identifies hold instead of unresolved ShipCore");
        var draw=asm.GetType("ZeoNavOverlay.HudForm").GetMethod("TargetBitmap",BindingFlags.Static|BindingFlags.NonPublic);
        Check(asm.GetType("ZeoNavOverlay.OverlayContext").GetField("targetHud",BindingFlags.NonPublic|BindingFlags.Instance)!=null,"Small dedicated target reticle is available");
        var reticleDraw=asm.GetType("ZeoNavOverlay.HudForm").GetMethod("TargetReticleBitmap",BindingFlags.Static|BindingFlags.NonPublic);
        type.GetField("TargetCursorState").SetValue(snapshot,1);
        using(var bitmap=(Bitmap)reticleDraw.Invoke(null,new[]{snapshot}))
            Check(bitmap.Width==48&&bitmap.Height==48&&bitmap.GetPixel(24,5).R>bitmap.GetPixel(24,5).G,"Empty-space reticle is yellow and compact");
        type.GetField("TargetCursorState").SetValue(snapshot,2);
        using(var bitmap=(Bitmap)reticleDraw.Invoke(null,new[]{snapshot}))
            Check(bitmap.GetPixel(24,5).G>bitmap.GetPixel(24,5).R,"Hovered-contact reticle is green");
        foreach(int state in new[]{2,3})
        {
            type.GetField("TargetCursorState").SetValue(snapshot,state);
            using(var bitmap=(Bitmap)draw.Invoke(null,new[]{snapshot}))
            {
                var pixel=bitmap.GetPixel(14,14);
                Check(bitmap.Width==64&&bitmap.Height==64&&bitmap.GetPixel(0,10).A==0,"Target box has small transparent bounds "+state);
                Check(state==2?pixel.G>pixel.R:pixel.R>pixel.G*2,"Hover box is green and lock box is red "+state);
                Check(bitmap.GetPixel(32,32).A==0,"Target box leaves signal center visible "+state);
                bitmap.Save(Path.Combine(scratch,state==2?"target-hover.png":"target-locked.png"),ImageFormat.Png);
            }
        }
        var lockDraw=asm.GetType("ZeoNavOverlay.HudForm").GetMethod("TargetLockBitmap",BindingFlags.Static|BindingFlags.NonPublic);
        type.GetField("ClientW").SetValue(snapshot,1920);type.GetField("ClientH").SetValue(snapshot,1080);
        using(var bitmap=(Bitmap)lockDraw.Invoke(null,new[]{snapshot}))
        {
            Check(bitmap.Width<=400&&bitmap.Height<=60&&bitmap.GetPixel(0,0).A==0,"Confirmed target uses a small transparent fixed readout");
            Check(bitmap.GetPixel(5,15).R>bitmap.GetPixel(5,15).G,"Confirmed lock has a red status rail");
            bitmap.Save(Path.Combine(scratch,"target-lock-readout.png"),ImageFormat.Png);
        }
        var configType=asm.GetType("ZeoNavOverlay.NavConfig");var lockConfig=Activator.CreateInstance(configType);
        snapshot.GetType().GetField("Config").SetValue(snapshot,lockConfig);
        configType.GetField("TargetHudTextScale").SetValue(lockConfig,1.5d);
        byte[] originalPixels=null;
        foreach(double size in new[]{1d,.5d,2d,1d})
        {
            configType.GetField("TargetHudWidth").SetValue(lockConfig,size);
            configType.GetField("TargetHudHeight").SetValue(lockConfig,size);
            using(var bitmap=(Bitmap)lockDraw.Invoke(null,new[]{snapshot}))using(var buffer=new MemoryStream())
            {
                bitmap.Save(buffer,ImageFormat.Png);var pixels=buffer.ToArray();
                Check(bitmap.Width==(int)(380*size)&&bitmap.Height==(int)(54*size),"lock readout dimensions follow selected scale");
                if(originalPixels==null)originalPixels=pixels;
                else if(size==1)Check(originalPixels.SequenceEqual(pixels),"lock readout recovers identical text after shrink and grow");
                Check((double)configType.GetField("TargetHudTextScale").GetValue(lockConfig)==1.5,"lock rendering never overwrites text preference");
                bitmap.Save(Path.Combine(scratch,"target-lock-size-"+size.ToString(CultureInfo.InvariantCulture)+".png"));
            }
        }
        var lockBounds=asm.GetType("ZeoNavOverlay.HudForm").GetMethod("TargetLockBounds",BindingFlags.Static|BindingFlags.NonPublic);
        RectangleF initial=(RectangleF)lockBounds.Invoke(null,new object[]{1920,1080,380,54,lockConfig});
        Check(initial.Left>1200&&initial.Top>800&&initial.Right<=1920&&initial.Bottom<=1080,"default target banner is bottom right within viewport");
        configType.GetField("TargetHudX").SetValue(lockConfig,-.5d);
        RectangleF moved=(RectangleF)lockBounds.Invoke(null,new object[]{1920,1080,380,54,lockConfig});
        Check(moved.X<initial.X-400&&moved.Right<=1920,"target banner honors independent horizontal placement");
        type.GetField("ClientX").SetValue(snapshot,-1920);type.GetField("ClientY").SetValue(snapshot,100);
        type.GetField("ClientW").SetValue(snapshot,1920);type.GetField("ClientH").SetValue(snapshot,1080);
        type.GetField("TargetPointerX").SetValue(snapshot,.5d);type.GetField("TargetPointerY").SetValue(snapshot,-.5d);
        type.GetField("TargetMarkerX").SetValue(snapshot,-.5d);type.GetField("TargetMarkerY").SetValue(snapshot,.5d);
        var screenPoint=asm.GetType("ZeoNavOverlay.HudForm").GetMethod("TargetScreenPoint",BindingFlags.Static|BindingFlags.NonPublic);
        var markerPoint=(PointF)screenPoint.Invoke(null,new[]{snapshot});
        Check(markerPoint==new PointF(-1440,370),"Signal box honors window offset and positive-up projection");
    }
    private static void RendererFixtures()
    {
        Assembly asm=Assembly.LoadFrom(overlayPath);
        Type snapshotType=asm.GetType("ZeoNavOverlay.NavSnapshot"),configType=asm.GetType("ZeoNavOverlay.NavConfig"),draftType=asm.GetType("ZeoNavOverlay.NavLayoutDraft"),hudType=asm.GetType("ZeoNavOverlay.HudForm");
        object snap=Activator.CreateInstance(snapshotType),cfg=Activator.CreateInstance(configType),draft=Activator.CreateInstance(draftType);
        Action<object,string,object> set=(o,k,v)=>o.GetType().GetField(k).SetValue(o,v);
        set(cfg,"TripUseHudTheme",false); set(cfg,"TripPanelVisibility","HIDDEN");
        set(snap,"Config",cfg); set(snap,"Layout",draft); set(draft,"Token","fixture"); set(draft,"X",-.6); set(draft,"Y",.5);
        Type context=asm.GetType("ZeoNavOverlay.OverlayContext");
        object preview=context.GetMethod("LayoutPreview",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new[]{snap});
        object previewConfig=snapshotType.GetField("Config").GetValue(preview);
        Check(!ReferenceEquals(preview,snap) && !ReferenceEquals(previewConfig,cfg),"draft clones snapshot and config");
        Check((double)configType.GetField("HudX").GetValue(cfg)==-.88,"preview leaves source coordinates unchanged");
        Check((string)configType.GetField("TripPanelVisibility").GetValue(previewConfig)=="HIDDEN","preview does not change visibility");
        object hud=FormatterServices.GetUninitializedObject(hudType);
        var apply=hudType.GetMethod("Apply"); var boundsMethod=hudType.GetMethod("CalculatePanelBounds",BindingFlags.NonPublic|BindingFlags.Instance); var draw=hudType.GetMethod("DrawPanelContent",BindingFlags.NonPublic|BindingFlags.Instance);
        foreach(var size in new[]{new[]{.5,1.0},new[]{1.0,2.0},new[]{2.0,.5},new[]{1.0,1.0}}) {
            set(previewConfig,"HudWidth",size[0]);set(previewConfig,"HudHeight",size[1]);set(previewConfig,"TripPanelVisibility","ALWAYS");
            set(preview,"Destination","A long navigation destination that must never overlap its distance");
            apply.Invoke(hud,new[]{preview});var bound=(RectangleF)boundsMethod.Invoke(hud,new object[]{1920,1080});
            using(var image=new Bitmap(1920,1080))using(var g=Graphics.FromImage(image)) {
                var transform=g.Transform.Elements;draw.Invoke(hud,new object[]{g,bound,bound});
                Check(transform.SequenceEqual(g.Transform.Elements),"Nav restores graphics transform after resizing");
                Check(bound.Right<=1920&&bound.Bottom<=1080,"Nav resized bounds within viewport");
                image.Save(Path.Combine(scratch,"resize-"+size[0]+"x"+size[1]+".png"));
            }
        }
        set(previewConfig,"HudWidth",1d);set(previewConfig,"HudHeight",1d);set(previewConfig,"TripPanelVisibility","HIDDEN");
        foreach(var viewport in new[] {new[]{1280,720},new[]{1920,1080},new[]{3440,1440}})
        {
            apply.Invoke(hud,new[]{preview}); RectangleF bounds=(RectangleF)boundsMethod.Invoke(hud,new object[]{viewport[0],viewport[1]});
            Check(bounds.Left>=0 && bounds.Top>=0 && bounds.Right<=viewport[0] && bounds.Bottom<=viewport[1],"actual renderer bounds within viewport "+viewport[0]);
            using(var image=new Bitmap(viewport[0],viewport[1],PixelFormat.Format32bppPArgb))
            using(var g=Graphics.FromImage(image))
            {
                g.Clear(Color.Transparent); draw.Invoke(hud,new object[]{g,bounds,bounds});
                Check(image.GetPixel((int)bounds.Left+30,(int)bounds.Top+80).A==0,"hidden preview interior stays transparent "+viewport[0]);
                image.Save(Path.Combine(scratch,"layout-outline-"+viewport[0]+".png"),ImageFormat.Png);
            }
        }
        set(previewConfig,"TripPanelVisibility","AUTO"); set(preview,"HudVisible",true); set(preview,"Phase","ACCELERATE"); set(preview,"Destination","RENDER FIXTURE"); set(preview,"SpeedMps",250d); set(preview,"DistanceMeters",125000d); set(preview,"EtaSeconds",123d);
        apply.Invoke(hud,new[]{preview}); var rect=(RectangleF)boundsMethod.Invoke(hud,new object[]{1280,720});
        using(var image=new Bitmap(1280,720,PixelFormat.Format32bppPArgb)) using(var g=Graphics.FromImage(image))
        { g.Clear(Color.Transparent); draw.Invoke(hud,new object[]{g,rect,rect}); image.Save(Path.Combine(scratch,"active-layout-fixture.png"),ImageFormat.Png); }
        set(preview,"Phase","INITIAL BRAKE");set(preview,"EtaSeconds",-1d);
        set(preview,"AlignmentErrorDeg",179.984d);set(preview,"AngularSpeedDeg",0d);
        apply.Invoke(hud,new[]{preview});
        using(var image=new Bitmap(1280,720,PixelFormat.Format32bppPArgb))using(var g=Graphics.FromImage(image))
        {g.Clear(Color.Transparent);draw.Invoke(hud,new object[]{g,rect,rect});image.Save(Path.Combine(scratch,"recovery-turn-fixture.png"),ImageFormat.Png);}
        set(preview,"State","DOCKING");set(preview,"Phase","DOCK CAPTURE");set(preview,"SpeedMps",.23d);set(preview,"SpeedCapMps",.25d);set(preview,"SpeedCapSource","DOCK RCS");set(preview,"DistanceMeters",2d);set(preview,"EtaSeconds",-1d);
        apply.Invoke(hud,new[]{preview});
        using(var image=new Bitmap(1280,720,PixelFormat.Format32bppPArgb))using(var g=Graphics.FromImage(image))
        {g.Clear(Color.Transparent);draw.Invoke(hud,new object[]{g,rect,rect});image.Save(Path.Combine(scratch,"docking-layout-fixture.png"),ImageFormat.Png);}
        var nativeWindows=typeof(System.Windows.Forms.Control).GetFields(BindingFlags.Instance|BindingFlags.NonPublic).Where(f=>typeof(System.Windows.Forms.NativeWindow).IsAssignableFrom(f.FieldType)).ToArray();
        Check(nativeWindows.Length>0 && nativeWindows.All(f=>f.GetValue(hud)==null),"offline actual renderer did not create a native window");
        Group("Actual HUD renderer bitmaps at 720p, 1080p and ultrawide; preview isolation and alpha");
    }
}
