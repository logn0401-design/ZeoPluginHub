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
        var excluded=new HashSet<string> {"ConfigVersion","DriveSlider","AutoFlip","FullStop","SpectrumFeedback"};
        var fields=typeof(NavConfig).GetFields().Where(f=>f.IsDefined(typeof(DataMemberAttribute),false)).ToArray();
        var catalog=NavUiCatalog.Options;
        Check(catalog.Select(o=>o.Key).Distinct().Count()==catalog.Count,"unique catalog keys");
        foreach(var field in fields) Check(excluded.Contains(field.Name) || catalog.Any(o=>o.Key==field.Name),"field parity "+field.Name);
        foreach(var row in catalog.Where(o=>!o.Key.StartsWith("@")))
        {
            var field=typeof(NavConfig).GetField(row.Key);
            Check(field!=null,"valid target "+row.Key);
            var values=new List<object>();
            if(row.Kind==NavOptionKind.Number) { values.Add(row.Parse(row.Min.ToString(CultureInfo.InvariantCulture))); values.Add(row.Parse(row.Max.ToString(CultureInfo.InvariantCulture))); }
            else if(row.Kind==NavOptionKind.Boolean) { values.Add(false); values.Add(true); }
            else if(row.Kind==NavOptionKind.Color) values.Add(row.Parse("#a1b2c3"));
            else foreach(string value in row.Choices) values.Add(row.Parse(value));
            foreach(object value in values)
            {
                var draft=live.Copy(); field.SetValue(draft,Convert.ChangeType(value,field.FieldType,CultureInfo.InvariantCulture));
                var copy=JsonIo.FromBytes<NavConfig>(JsonIo.ToBytes(ConfigRules.Clamp(draft)));
                Check(Equals(field.GetValue(copy),field.GetValue(draft)),"catalog roundtrip "+row.Key+"="+value);
            }
            object write=values[values.Count-1];
            store.Apply(NavUiCatalog.Changes(row,write));
            Check(Equals(field.GetValue(store.Read()),Convert.ChangeType(write,field.FieldType,CultureInfo.InvariantCulture)),"persisted target "+row.Key);
        }
        Group("All 64 stored fields accounted for; all editable controls, choices and range endpoints round-trip");
        var sigOption=catalog.Single(o=>o.Key=="MaxDriveSigKm");
        Check(sigOption.Min==5 && sigOption.Max==750,"MAX SIG selector exposes 5 to 750 km");
        store.Apply(NavUiCatalog.Changes(sigOption,sigOption.Parse("750")));
        Check(store.Read().MaxDriveSigKm==750 && JsonIo.Load<NavConfig>(path).MaxDriveSigKm==750,"750 km survives saved config reload");
        bool sigRejected=false; try { sigOption.Parse("751"); } catch { sigRejected=true; }
        Check(sigRejected,"native menu rejects values above 750 km");
        Check(ConfigRules.Clamp(new NavConfig {ConfigVersion=7,MaxDriveSigKm=900}).MaxDriveSigKm==750,"oversized stored MAX SIG clamps to 750 km");
        Check(ConfigRules.Clamp(new NavConfig {ConfigVersion=7,MaxDriveSigKm=125}).MaxDriveSigKm==125,"existing selection stays unchanged");
        Check(ConfigRules.Clamp(new NavConfig {ConfigVersion=4,DriveSlider=100}).MaxDriveSigKm==490,"historical slider migration preserves original intent");
        Group("750 km MAX SIG persistence, bounds and migration");
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
        RendererFixtures();
        Console.WriteLine("RESULT: "+checks+" assertions, "+failures+" failures. Offline tests only; native SE UI and coexistence require an in-game check.");
        return failures==0 ? 0 : 1;
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
        var nativeWindows=typeof(System.Windows.Forms.Control).GetFields(BindingFlags.Instance|BindingFlags.NonPublic).Where(f=>typeof(System.Windows.Forms.NativeWindow).IsAssignableFrom(f.FieldType)).ToArray();
        Check(nativeWindows.Length>0 && nativeWindows.All(f=>f.GetValue(hud)==null),"offline actual renderer did not create a native window");
        Group("Actual HUD renderer bitmaps at 720p, 1080p and ultrawide; preview isolation and alpha");
    }
}
