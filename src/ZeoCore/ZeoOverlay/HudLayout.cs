using System;
using System.Collections.Generic;
using System.Linq;

namespace ZeoOverlay
{
    internal sealed class HudPanelPosition
    {
        public string Id { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public bool Custom { get; set; }
        public bool Moved { get; set; }
        public double WidthScale { get; set; } = 1;
        public double HeightScale { get; set; } = 1;
        public bool Resized { get; set; }
        internal HudPanelPosition Copy() { return new HudPanelPosition { Id=Id,X=X,Y=Y,Custom=Custom,Moved=Moved,WidthScale=WidthScale,HeightScale=HeightScale,Resized=Resized }; }
    }
    internal sealed class HudPanelBounds
    {
        public string Id { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double WidthScale { get; set; } = 1;
        public double HeightScale { get; set; } = 1;
        internal int ResizeEdges(double x,double y)
        {
            const double grip=10;
            if(x<X-grip || y<Y-grip || x>X+Width+grip || y>Y+Height+grip) return 0;
            int edges=0;
            if(Math.Abs(x-X)<=grip) edges|=1; else if(Math.Abs(x-X-Width)<=grip) edges|=2;
            if(Math.Abs(y-Y)<=grip) edges|=4; else if(Math.Abs(y-Y-Height)<=grip) edges|=8;
            return edges;
        }
        internal bool Contains(double x,double y) { return x>=X && y>=Y && x<=X+Width && y<=Y+Height; }
    }
    internal sealed class HudLayoutFeedback
    {
        public string Kind { get; set; } = "layout-bounds";
        public string Token { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public List<HudPanelBounds> Panels { get; set; } = new List<HudPanelBounds>();
    }
    internal sealed class HudLayoutState
    {
        internal static readonly string[] Ids={"ship","scope","fleet","ammo","roster","distress"};
        internal static readonly string[] Names={"SHIP INFO","TOS / SCOPE","FLEET / NETWORK","AMMUNITION","FLEET ROSTER","DISTRESS BANNER"};
        public string Token { get; set; }
        public string Selected { get; set; } = "ship";
        public HudPanelBounds Toolbar { get; set; }
        public List<HudPanelPosition> Panels { get; set; } = new List<HudPanelPosition>();
        internal static HudLayoutState Capture(OverlaySettings s)
        {
            return new HudLayoutState { Token=Guid.NewGuid().ToString("N"), Panels=new List<HudPanelPosition> {
                new HudPanelPosition { Id="ship",WidthScale=s.ShipHudWidth,HeightScale=s.ShipHudHeight,X=s.FlightX,Y=s.FlightY },
                new HudPanelPosition { Id="scope",WidthScale=s.ScopeHudWidth,HeightScale=s.ScopeHudHeight,X=s.TrackPanelX,Y=s.TrackPanelY },
                new HudPanelPosition { Id="fleet",WidthScale=s.FleetHudWidth,HeightScale=s.FleetHudHeight,X=s.LinkPanelX,Y=s.LinkPanelY },
                new HudPanelPosition { Id="ammo",WidthScale=s.AmmoHudWidth,HeightScale=s.AmmoHudHeight,X=s.AmmoX,Y=s.AmmoY },
                new HudPanelPosition { Id="roster",WidthScale=s.RosterHudWidth,HeightScale=s.RosterHudHeight,X=s.RosterX,Y=s.RosterY },
                new HudPanelPosition { Id="distress",WidthScale=s.DistressHudWidth,HeightScale=s.DistressHudHeight,X=s.DistressX,Y=s.DistressY,Custom=s.DistressPositionCustom }
            }};
        }
        internal void ApplyTo(OverlaySettings s,bool movedOnly=false)
        {
            foreach(var p in Panels)
            {
                if(!movedOnly || p.Resized) SetSize(s,p.Id,p.WidthScale,p.HeightScale);
                if(movedOnly && !p.Moved) continue;
                switch(p.Id)
                {
                    case "ship": s.FlightX=p.X; s.FlightY=p.Y; break;
                    case "scope": s.TrackPanelX=p.X; s.TrackPanelY=p.Y; break;
                    case "fleet": s.LinkPanelX=p.X; s.LinkPanelY=p.Y; break;
                    case "ammo": s.AmmoX=p.X; s.AmmoY=p.Y; break;
                    case "roster": s.RosterX=p.X; s.RosterY=p.Y; break;
                    case "distress": s.DistressX=p.X; s.DistressY=p.Y; s.DistressPositionCustom=p.Custom; break;
                }
            }
        }
        internal void Move(string id,double left,double top,double panelWidth,double panelHeight,int width,int height)
        {
            if(width<200 || height<200 || !Finite(left) || !Finite(top) || !Finite(panelWidth) || !Finite(panelHeight)) return;
            var p=Panels.FirstOrDefault(x=>x.Id==id);
            if(p==null) return;
            double minX=Math.Max(10,width*.01),minY=Math.Max(10,height*.01);
            left=Math.Max(minX,Math.Min(width-panelWidth-10,left));
            top=Math.Max(minY,Math.Min(height-panelHeight-10,top));
            p.X=Math.Max(-.98,Math.Min(.98,2*left/width-1));
            p.Y=Math.Max(-.98,Math.Min(.98,1-2*top/height));
            p.Custom=true; p.Moved=true;
        }
        internal static double SafeSize(double value)
        {
            return Finite(value) && value>0 ? Math.Max(.5,Math.Min(3,value)) : 1;
        }
        internal static double GetSize(OverlaySettings s,string id,bool vertical=false)
        {
            switch(id)
            {
                case "ship": return SafeSize(vertical ? s.ShipHudHeight : s.ShipHudWidth);
                case "scope": return SafeSize(vertical ? s.ScopeHudHeight : s.ScopeHudWidth);
                case "fleet": return SafeSize(vertical ? s.FleetHudHeight : s.FleetHudWidth);
                case "ammo": return SafeSize(vertical ? s.AmmoHudHeight : s.AmmoHudWidth);
                case "roster": return SafeSize(vertical ? s.RosterHudHeight : s.RosterHudWidth);
                case "distress": return SafeSize(vertical ? s.DistressHudHeight : s.DistressHudWidth);
                default: return 1;
            }
        }
        private static void SetSize(OverlaySettings s,string id,double width,double height)
        {
            width=SafeSize(width); height=SafeSize(height);
            switch(id)
            {
                case "ship": s.ShipHudWidth=width; s.ShipHudHeight=height; break;
                case "scope": s.ScopeHudWidth=width; s.ScopeHudHeight=height; break;
                case "fleet": s.FleetHudWidth=width; s.FleetHudHeight=height; break;
                case "ammo": s.AmmoHudWidth=width; s.AmmoHudHeight=height; break;
                case "roster": s.RosterHudWidth=width; s.RosterHudHeight=height; break;
                case "distress": s.DistressHudWidth=width; s.DistressHudHeight=height; break;
            }
        }
        // Bounds and initial size are frozen at mouse-down. Feedback can arrive
        // more slowly than input without accumulating resize error.
        internal void Resize(HudPanelBounds start,int edges,double dx,double dy,int width,int height)
        {
            if(start==null || edges==0 || width<200 || height<200 ||
               !Finite(dx) || !Finite(dy) || !Finite(start.Width) || !Finite(start.Height) ||
               !Finite(start.X) || !Finite(start.Y) || start.Width<=0 || start.Height<=0) return;
            var p=Panels.FirstOrDefault(x=>x.Id==start.Id);
            if(p==null) return;
            double sw=SafeSize(start.WidthScale), sh=SafeSize(start.HeightScale);
            double marginX=Math.Max(10,width*.01),marginY=Math.Max(10,height*.01);
            double availableW=(edges&1)!=0 ? start.X+start.Width-marginX : width-10-start.X;
            double availableH=(edges&4)!=0 ? start.Y+start.Height-marginY : height-10-start.Y;
            double nw=start.Width, nh=start.Height;
            if((edges&3)!=0) nw=Math.Max(start.Width*.5/sw,Math.Min(Math.Min(start.Width*3/sw,availableW),start.Width+((edges&1)!=0 ? -dx : dx)));
            if((edges&12)!=0) nh=Math.Max(start.Height*.5/sh,Math.Min(Math.Min(start.Height*3/sh,availableH),start.Height+((edges&4)!=0 ? -dy : dy)));
            double left=(edges&1)!=0 ? start.X+start.Width-nw : start.X;
            double top=(edges&4)!=0 ? start.Y+start.Height-nh : start.Y;
            Move(start.Id,left,top,nw,nh,width,height);
            p.WidthScale=SafeSize(sw*nw/start.Width); p.HeightScale=SafeSize(sh*nh/start.Height); p.Resized=true;
        }

        private static bool Finite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }
        internal void Save(string path)
        {
            var latest=OverlaySettings.Load(path);
            ApplyTo(latest,true);
            latest.Save();
            var check=Capture(OverlaySettings.Load(path));
            foreach(var p in Panels.Where(x=>x.Moved || x.Resized))
            {
                var saved=check.Panels.First(x=>x.Id==p.Id);
                if((p.Moved && (Math.Abs(saved.X-p.X)>0.000001 || Math.Abs(saved.Y-p.Y)>0.000001 ||
                   (p.Id=="distress" && saved.Custom!=p.Custom))) || (p.Resized && (Math.Abs(saved.WidthScale-p.WidthScale)>0.000001 || Math.Abs(saved.HeightScale-p.HeightScale)>0.000001)))
                    throw new InvalidOperationException("Layout could not be saved.");
            }
        }
    }
}

