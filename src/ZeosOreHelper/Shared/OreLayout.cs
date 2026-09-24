using System;
using System.Collections.Generic;
using ZeosOreOverlay;
namespace ZeoOreShared {
 public sealed class OreLayoutDraft {
  public string Token{get;set;}=Guid.NewGuid().ToString("N");
  public double X{get;set;} public double Y{get;set;}
  public double WidthScale{get;set;}=1; public double HeightScale{get;set;}=1;
  public float ToolbarX{get;set;} public float ToolbarY{get;set;} public float ToolbarW{get;set;} public float ToolbarH{get;set;}
  public bool ToolbarContains(double x,double y){return x>=ToolbarX&&y>=ToolbarY&&x<=ToolbarX+ToolbarW&&y<=ToolbarY+ToolbarH;}
 }
 public sealed class OreLayoutAck { public string Token{get;set;} public double X{get;set;} public double Y{get;set;}
  public double WidthScale{get;set;}=1; public double HeightScale{get;set;}=1; public double Width{get;set;} public double Height{get;set;} public int ViewportW{get;set;} public int ViewportH{get;set;} }
 internal struct OreBounds {
 public int Edges(double x,double y){if(x<X-10||y<Y-10||x>X+Width+10||y>Y+Height+10)return 0;return(Math.Abs(x-X)<=10?1:Math.Abs(x-X-Width)<=10?2:0)|(Math.Abs(y-Y)<=10?4:Math.Abs(y-Y-Height)<=10?8:0);}
 public double X,Y,Width,Height; public bool Contains(double x,double y){return x>=X&&y>=Y&&x<=X+Width&&y<=Y+Height;} }
 internal static class OreGeometry {
  internal static double Safe(double n,double lo,double hi,double fallback){return double.IsNaN(n)||double.IsInfinity(n)?fallback:Math.Max(lo,Math.Min(hi,n));}
  internal static OreBounds Bounds(OreOverlaySettings s,int w,int h,int rows,OreLayoutDraft draft=null) {
   string layout=s.Get("HudLayout","Prospector").ToUpperInvariant();double baseW=layout=="WIDE"?860:layout=="TACTICAL"?580:layout=="COMPACT"?470:700;
   double scale=Safe(s.D("PanelScale",1),.5,2,1),text=Safe(s.D("TextScale",1),.5,2,1);
   double width=baseW*scale*Safe(s.D("PanelWidthScale",1),.65,2.5,1);
   double height=(s.B("ShowHeader",true)?50*scale:10)+(s.B("ShowColumnHeader",true)?24*scale:0)+(s.B("ShowStatusBar",true)?25*scale:0)+Math.Max(0,Math.Min(20,rows))*24*Safe(s.D("RowScale",1),.65,1.75,1)*text+14;
   double sx=Safe(draft==null?s.D("HudWidth",1):draft.WidthScale,.5,3,1),sy=Safe(draft==null?s.D("HudHeight",1):draft.HeightScale,.5,3,1);
   double fit=Math.Min(1,Math.Min(Math.Max(1,w-16)/(width*sx),Math.Max(1,h-16)/(height*sy)));
   width*=sx*fit;height*=sy*fit;
   double cx=(Safe(draft==null?s.D("PanelX",-.72):draft.X,-1,1,-.72)+1)*.5*w,cy=(1-Safe(draft==null?s.D("PanelY",-.64):draft.Y,-1,1,-.64))*.5*h;
   return new OreBounds{X=Math.Max(8,Math.Min(w-width-8,cx-width/2)),Y=Math.Max(8,Math.Min(h-height-8,cy-height/2)),Width=width,Height=height};
  }
 }
 internal sealed class OreLayoutModel {
  internal readonly OreLayoutDraft Draft; private readonly double originalX,originalY,originalWidth,originalHeight;
  internal OreLayoutModel(OreOverlaySettings s){originalX=s.D("PanelX",-.72);originalY=s.D("PanelY",-.64);originalWidth=OreGeometry.Safe(s.D("HudWidth",1),.5,3,1);originalHeight=OreGeometry.Safe(s.D("HudHeight",1),.5,3,1);Draft=new OreLayoutDraft{X=originalX,Y=originalY,WidthScale=originalWidth,HeightScale=originalHeight};}
  internal void Move(double x,double y,double panelW,double panelH,int w,int h) {
   if(w<200||h<200||double.IsNaN(x)||double.IsNaN(y)||double.IsInfinity(x)||double.IsInfinity(y))return;
   x=Math.Max(8,Math.Min(w-panelW-8,x));y=Math.Max(8,Math.Min(h-panelH-8,y));
   Draft.X=Math.Max(-1,Math.Min(1,(x+panelW/2)*2/w-1));Draft.Y=Math.Max(-1,Math.Min(1,1-(y+panelH/2)*2/h));
  }
  internal void ResetPosition(){Draft.X=-.72;Draft.Y=-.64;}
  internal void Resize(OreBounds b,int edges,double dx,double dy,double sw,double sh,int w,int h){
   if(b.Width<=0||b.Height<=0||w<200||h<200||double.IsNaN(dx)||double.IsInfinity(dx)||double.IsNaN(dy)||double.IsInfinity(dy))return;
   sw=OreGeometry.Safe(sw,.5,3,1);sh=OreGeometry.Safe(sh,.5,3,1);
   double nw=b.Width,nh=b.Height;
   if((edges&3)!=0)nw=Math.Max(b.Width*.5/sw,Math.Min(b.Width*3/sw,Math.Min((edges&1)!=0?b.X+b.Width-8:w-b.X-8,b.Width+((edges&1)!=0?-dx:dx))));
   if((edges&12)!=0)nh=Math.Max(b.Height*.5/sh,Math.Min(b.Height*3/sh,Math.Min((edges&4)!=0?b.Y+b.Height-8:h-b.Y-8,b.Height+((edges&4)!=0?-dy:dy))));
   Move((edges&1)!=0?b.X+b.Width-nw:b.X,(edges&4)!=0?b.Y+b.Height-nh:b.Y,nw,nh,w,h);
   Draft.WidthScale=OreGeometry.Safe(sw*nw/b.Width,.5,3,1);Draft.HeightScale=OreGeometry.Safe(sh*nh/b.Height,.5,3,1);
  }
  internal void Undo(){Draft.X=originalX;Draft.Y=originalY;Draft.WidthScale=originalWidth;Draft.HeightScale=originalHeight;}
  internal Dictionary<string,string> Changes(){var d=new Dictionary<string,string>();if(Math.Abs(Draft.X-originalX)>1e-9)d["PanelX"]=Draft.X.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture);if(Math.Abs(Draft.Y-originalY)>1e-9)d["PanelY"]=Draft.Y.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture);if(Math.Abs(Draft.WidthScale-originalWidth)>1e-9)d["HudWidth"]=Draft.WidthScale.ToString("R",System.Globalization.CultureInfo.InvariantCulture);if(Math.Abs(Draft.HeightScale-originalHeight)>1e-9)d["HudHeight"]=Draft.HeightScale.ToString("R",System.Globalization.CultureInfo.InvariantCulture);return d;}
 }
}
