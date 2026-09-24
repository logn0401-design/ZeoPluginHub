using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Web.Script.Serialization;
using ZeoOverlay;
internal static class Program
{
 static int checks;
 static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Check(bool ok,string label){checks++;if(!ok)throw new Exception(label);}
 static bool Near(double a,double b){return Math.Abs(a-b)<.001;}
 static int Main(){try{Persistence();Render();Console.WriteLine("PASS: "+checks+" geometry, persistence, IPC, viewport and production-render checks.");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
 static void Persistence(){
  var folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"settings-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
  string path=Path.Combine(folder,"hud.json");File.WriteAllText(path,"{}");
  var initial=OverlaySettings.Load(path);initial.Save();var draft=HudLayoutState.Capture(initial);var original=draft.Panels.Select(p=>p.Copy()).ToList();
  foreach(var id in HudLayoutState.Ids){
   Check(Near(HudLayoutState.GetSize(initial,id),1)&&Near(HudLayoutState.GetSize(initial,id,true),1),"Old settings defaults "+id);
   var b=new HudPanelBounds{Id=id,X=500,Y=400,Width=400,Height=200};
   Check(b.ResizeEdges(900,600)==10&&b.ResizeEdges(700,500)==0,"Grips vs move "+id);
   draft.Resize(b,2,200,80,1920,1080);var p=draft.Panels.First(x=>x.Id==id);
   Check(Near(p.WidthScale,1.5)&&Near(p.HeightScale,1),"Right edge changes width only "+id);
   draft.Resize(b,8,200,100,1920,1080);
   Check(Near(p.WidthScale,1)&&Near(p.HeightScale,1.5),"Bottom edge changes height only "+id);
   draft.Resize(b,5,-200,-50,1920,1080);
   Check(Near(p.WidthScale,1.5)&&Near(p.HeightScale,1.25),"Corner changes axes independently "+id);
   Check(Near(p.X,2*300.0/1920-1)&&Near(p.Y,1-2*350.0/1080),"Opposite corner anchored "+id);
   var limit=HudLayoutState.Capture(initial);limit.Resize(b,10,10000,10000,1280,720);var lp=limit.Panels.First(x=>x.Id==id);
   Check(500+400*lp.WidthScale<=1270.001&&400+200*lp.HeightScale<=710.001,"Viewport resize limit "+id);
  }
  Check(HudLayoutState.Ids.All(id=>Near(HudLayoutState.GetSize(OverlaySettings.Load(path),id),1)),"Draft/cancel never writes");
  var latest=OverlaySettings.Load(path);latest.ShowAmmoPanel=false;latest.TextScale=1.37;latest.Save();draft.Save(path);var saved=OverlaySettings.Load(path);
  Check(HudLayoutState.Ids.All(id=>Near(HudLayoutState.GetSize(saved,id),1.5)&&Near(HudLayoutState.GetSize(saved,id,true),1.25)),"Both dimensions saved for all six");
  Check(!saved.ShowAmmoPanel&&Near(saved.TextScale,1.37),"Unrelated concurrent edits preserved");
  File.WriteAllText(path,"{}");saved=OverlaySettings.Load(path);
  Check(HudLayoutState.Ids.All(id=>Near(HudLayoutState.GetSize(saved,id),1.5)&&Near(HudLayoutState.GetSize(saved,id,true),1.25)),"Sidecar survives legacy settings writer");
  var move=HudLayoutState.Capture(saved);latest=OverlaySettings.Load(path);latest.AmmoHudWidth=2;latest.AmmoHudHeight=.75;latest.Save();move.Move("ammo",200,300,400,200,1920,1080);move.Save(path);
  saved=OverlaySettings.Load(path);Check(Near(saved.AmmoHudWidth,2)&&Near(saved.AmmoHudHeight,.75),"Move-only save preserves concurrent dimensions");
  draft.Panels=original;draft.Save(path);Check(Near(OverlaySettings.Load(path).AmmoHudWidth,2),"Undo leaves clean fields untouched");
  var bad=HudLayoutState.Capture(saved);bad.Resize(new HudPanelBounds{Id="ammo",X=10,Y=10,Width=400,Height=200},10,double.NaN,2,1920,1080);Check(!bad.Panels.Any(x=>x.Moved||x.Resized),"NaN input rejected");
  var json=new JavaScriptSerializer();var ipc=json.Deserialize<HudLayoutState>(json.Serialize(move));Check(Near(ipc.Panels.First(x=>x.Id=="ammo").HeightScale,1.25),"IPC preserves axes");
 }
 static void Render(){
  var form=(HudOverlayForm)FormatterServices.GetUninitializedObject(typeof(HudOverlayForm));GC.SuppressFinalize(form);
  Action<string,object> set=(n,v)=>typeof(HudOverlayForm).GetField(n,Hidden).SetValue(form,v);
  var settings=new OverlaySettings{ShipLayout=1,ScopeRows=6,RosterRows=6,TextScale=1,ShowAmmoPanel=true,FollowGameHud=true};
  var rects=new Dictionary<string,RectangleF>();set("_settings",settings);set("_layoutRects",rects);set("_panelFit",new Dictionary<string,float>());set("_frameLock",new object());set("_lastLayoutReplyUtc",DateTime.UtcNow);
  var frame=new OverlayFrame{HasShip=true,HudEnabled=true,OwnGridName="ZEO Battleship // Long Ship Name",SectorName="Outer Belt — Sector 21",Speed=129.5,H2O=.75,O2=.34,FusionPellets=10245,DriveHealth=.85,ReactorHealth=.78,ShipHp=.67,PowerCurrent=1400,PowerMax=2200,RxOn=true,RxLinked=true,AuthAuthorized=true,AuthFactionTag="ZEO",AuthEffectiveScope="FACTION",ServerTrustSector="SECTOR 21",TxOn=true,TotalScopeCount=6};
  for(int i=0;i<6;i++){
   frame.ScopeRows.Add(new OverlayScopeRow{TrackId=i+1,Name=i==0 ? "SDX Long Modded Cruiser Name" : "Contact "+i,Distance=16000-i*2400,Speed=120+i*10,Closing=i*12,Relation=i%2==0 ? "hostile" : "friendly",Stale=i==5});
   frame.RosterRows.Add(new OverlayRosterRow{Name=i==0 ? "Long Friendly Battleship Name" : "ZEO Escort "+i,SameSector=i%2==0,SectorKnown=true,SectorName="Remote Outer Belt Sector",Online=true,Distance=3500,ShipHp=.78});
   frame.AmmoRows.Add(new OverlayAmmoRow{Key="future-ammo-"+i,CleanName=i==0 ? "Improvised 100mm Sabot Ammunition" : "PDC Ammunition "+i,Have=12345+i*100,Want=20000,Relevant=true});
  }
  frame.DistressAlerts.Add(new OverlayDistressAlert{Name="Friendly Long Ship Name",Type="UNDER ATTACK",SectorName="Outer Belt Sector",ShipHp=.34,SecondsRemaining=245});
  string[] methods={"DrawFlightPanel","DrawScopePanel","DrawFleetPanel","DrawAmmoPanel","DrawRosterPanel","DrawDistressBanner"};
  var aspects=new[]{new SizeF(1,1),new SizeF(1.8f,.75f),new SizeF(.65f,1.7f),new SizeF(.5f,.5f),new SizeF(3,3)};
  foreach(var viewport in new[]{new Size(1280,720),new Size(1920,1080),new Size(3440,1440)})
  foreach(var aspect in aspects){
   var layout=HudLayoutState.Capture(settings);
   foreach(var p in layout.Panels){p.WidthScale=aspect.Width;p.HeightScale=aspect.Height;p.X=2*60.0/viewport.Width-1;p.Y=1-2*80.0/viewport.Height;p.Custom=true;}
   layout.ApplyTo(settings);
   using(var sheet=new Bitmap(1800,1000))using(var sg=Graphics.FromImage(sheet)){
    sg.Clear(Color.FromArgb(7,9,12));
    for(int i=0;i<6;i++)using(var bitmap=new Bitmap(viewport.Width,viewport.Height))using(var g=Graphics.FromImage(bitmap)){
     g.Clear(Color.Transparent);g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
     set("_layoutDrawing",true);rects.Clear();var before=g.Transform.Elements;
     typeof(HudOverlayForm).GetMethod(methods[i],Hidden).Invoke(form,new object[]{g,viewport.Width,viewport.Height,frame});
     var r=rects[HudLayoutState.Ids[i]];
     Check(r.Left>=0&&r.Top>=0&&r.Right<=viewport.Width&&r.Bottom<=viewport.Height,"Fits viewport "+methods[i]);
     Check(before.SequenceEqual(g.Transform.Elements),"Graphics transform restored "+methods[i]);
     bool ink=false,outside=false;
     for(int y=0;y<bitmap.Height;y+=4)for(int x=0;x<bitmap.Width;x+=4)if(bitmap.GetPixel(x,y).A>0){ink=true;if(x<r.Left-2||x>r.Right+2||y<r.Top-2||y>r.Bottom+2)outside=true;}
     Check(ink&&!outside,"Panel draws without bleed "+methods[i]);
     float factor=Math.Min(580/r.Width,440/r.Height);var dest=new RectangleF((i%3)*600+10,(i/3)*500+40,r.Width*factor,r.Height*factor);
     sg.DrawImage(bitmap,dest,r,GraphicsUnit.Pixel);
     using(var font=new Font("Consolas",16))sg.DrawString(HudLayoutState.Names[i]+"  "+(int)r.Width+"x"+(int)r.Height,font,Brushes.White,(i%3)*600+10,(i/3)*500+10);
    }
    if(viewport.Width==1920)sheet.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"panels-"+aspect.Width+"x"+aspect.Height+".png"));
   }
  }
  // Font scale must recover each frame and must not depend on width.
  var fitter=typeof(HudOverlayForm).GetMethod("SizedPanelRectangle",Hidden);
  foreach(var id in HudLayoutState.Ids){
   foreach(double w in new[]{.5,2.0,1.0}){
    var d=HudLayoutState.Capture(settings);foreach(var panel in d.Panels){panel.WidthScale=w;panel.HeightScale=1.25;}d.ApplyTo(settings);
    fitter.Invoke(form,new object[]{id,1920,1080,10f,10f,400f,200f});
    var fits=(Dictionary<string,float>)typeof(HudOverlayForm).GetField("_panelFit",Hidden).GetValue(form);
    Check(Near(fits[id],1.25),"Width does not ratchet text scale "+id);
   }
  }
  // Preview applies draft dimensions temporarily and restores all settings.
  using(var b=new Bitmap(1920,1080))using(var g=Graphics.FromImage(b)){
   var saved=HudLayoutState.Capture(settings);frame.Layout=HudLayoutState.Capture(settings);frame.Layout.Panels.First(x=>x.Id=="ammo").WidthScale=.75;frame.Layout.Toolbar=new HudPanelBounds{X=0,Y=0,Width=1920,Height=190};
   typeof(HudOverlayForm).GetMethod("DrawLayoutPreview",Hidden).Invoke(form,new object[]{g,1920,1080,frame});
   Check(Near(settings.AmmoHudWidth,saved.Panels.First(x=>x.Id=="ammo").WidthScale),"Editor doesn't mutate saved sizes");
   Check(b.GetPixel(70,100).A==0,"Native toolbar remains clear");
   frame.Layout=null;frame.HasShip=false;frame.DistressAlerts.Clear();set("_layoutDrawing",true);rects.Clear();
   typeof(HudOverlayForm).GetMethod("DrawAmmoPanel",Hidden).Invoke(form,new object[]{g,1920,1080,frame});Check(rects.ContainsKey("ammo"),"Empty ammo retains frame");
   frame.ShowFlightData=true;frame.GameHudState=1;typeof(HudOverlayForm).GetMethod("DrawHud",Hidden).Invoke(form,new object[]{g,1920,1080,frame});Check(rects.ContainsKey("ship"),"No-ship fallback uses resize path");
  }
 }
}
