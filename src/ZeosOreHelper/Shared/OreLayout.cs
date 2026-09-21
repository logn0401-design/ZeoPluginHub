using System;
using System.Collections.Generic;
using ZeosOreOverlay;
namespace ZeoOreShared {
 public sealed class OreLayoutDraft {
  public string Token{get;set;}=Guid.NewGuid().ToString("N");
  public double X{get;set;} public double Y{get;set;}
  public float ToolbarX{get;set;} public float ToolbarY{get;set;} public float ToolbarW{get;set;} public float ToolbarH{get;set;}
  public bool ToolbarContains(double x,double y){return x>=ToolbarX&&y>=ToolbarY&&x<=ToolbarX+ToolbarW&&y<=ToolbarY+ToolbarH;}
 }
 public sealed class OreLayoutAck { public string Token{get;set;} public double X{get;set;} public double Y{get;set;} public double Width{get;set;} public double Height{get;set;} public int ViewportW{get;set;} public int ViewportH{get;set;} }
 internal struct OreBounds { public double X,Y,Width,Height; public bool Contains(double x,double y){return x>=X&&y>=Y&&x<=X+Width&&y<=Y+Height;} }
 internal static class OreGeometry {
  internal static double Safe(double n,double lo,double hi,double fallback){return double.IsNaN(n)||double.IsInfinity(n)?fallback:Math.Max(lo,Math.Min(hi,n));}
  internal static OreBounds Bounds(OreOverlaySettings s,int w,int h,int rows,OreLayoutDraft draft=null) {
   string layout=s.Get("HudLayout","Prospector").ToUpperInvariant();double baseW=layout=="WIDE"?860:layout=="TACTICAL"?580:layout=="COMPACT"?470:700;
   double scale=Safe(s.D("PanelScale",1),.5,2,1),text=Safe(s.D("TextScale",1),.5,2,1);
   double width=baseW*scale*Safe(s.D("PanelWidthScale",1),.65,2.5,1);
   double height=(s.B("ShowHeader",true)?50*scale:10)+(s.B("ShowColumnHeader",true)?24*scale:0)+(s.B("ShowStatusBar",true)?25*scale:0)+Math.Max(0,Math.Min(20,rows))*24*Safe(s.D("RowScale",1),.65,1.75,1)*text+14;
   width=Math.Min(Math.Max(1,w-16),width);height=Math.Min(Math.Max(1,h-16),height);
   double cx=(Safe(draft==null?s.D("PanelX",-.72):draft.X,-1,1,-.72)+1)*.5*w,cy=(1-Safe(draft==null?s.D("PanelY",-.64):draft.Y,-1,1,-.64))*.5*h;
   return new OreBounds{X=Math.Max(8,Math.Min(w-width-8,cx-width/2)),Y=Math.Max(8,Math.Min(h-height-8,cy-height/2)),Width=width,Height=height};
  }
 }
 internal sealed class OreLayoutModel {
  internal readonly OreLayoutDraft Draft; private readonly double originalX,originalY;
  internal OreLayoutModel(OreOverlaySettings s){originalX=s.D("PanelX",-.72);originalY=s.D("PanelY",-.64);Draft=new OreLayoutDraft{X=originalX,Y=originalY};}
  internal void Move(double x,double y,double panelW,double panelH,int w,int h) {
   if(w<200||h<200||double.IsNaN(x)||double.IsNaN(y)||double.IsInfinity(x)||double.IsInfinity(y))return;
   x=Math.Max(8,Math.Min(w-panelW-8,x));y=Math.Max(8,Math.Min(h-panelH-8,y));
   Draft.X=Math.Max(-1,Math.Min(1,(x+panelW/2)*2/w-1));Draft.Y=Math.Max(-1,Math.Min(1,1-(y+panelH/2)*2/h));
  }
  internal void ResetPosition(){Draft.X=-.72;Draft.Y=-.64;}
  internal void Undo(){Draft.X=originalX;Draft.Y=originalY;}
  internal Dictionary<string,string> Changes(){var d=new Dictionary<string,string>();if(Math.Abs(Draft.X-originalX)>1e-9)d["PanelX"]=Draft.X.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture);if(Math.Abs(Draft.Y-originalY)>1e-9)d["PanelY"]=Draft.Y.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture);return d;}
 }
}
