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
        internal HudPanelPosition Copy() { return new HudPanelPosition { Id=Id,X=X,Y=Y,Custom=Custom,Moved=Moved }; }
    }
    internal sealed class HudPanelBounds
    {
        public string Id { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
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
                new HudPanelPosition { Id="ship",X=s.FlightX,Y=s.FlightY },
                new HudPanelPosition { Id="scope",X=s.TrackPanelX,Y=s.TrackPanelY },
                new HudPanelPosition { Id="fleet",X=s.LinkPanelX,Y=s.LinkPanelY },
                new HudPanelPosition { Id="ammo",X=s.AmmoX,Y=s.AmmoY },
                new HudPanelPosition { Id="roster",X=s.RosterX,Y=s.RosterY },
                new HudPanelPosition { Id="distress",X=s.DistressX,Y=s.DistressY,Custom=s.DistressPositionCustom }
            }};
        }
        internal void ApplyTo(OverlaySettings s,bool movedOnly=false)
        {
            foreach(var p in Panels)
            {
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
        private static bool Finite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }
        internal void Save(string path)
        {
            var latest=OverlaySettings.Load(path);
            ApplyTo(latest,true);
            latest.Save();
            var check=Capture(OverlaySettings.Load(path));
            foreach(var p in Panels.Where(x=>x.Moved))
            {
                var saved=check.Panels.First(x=>x.Id==p.Id);
                if(Math.Abs(saved.X-p.X)>0.000001 || Math.Abs(saved.Y-p.Y)>0.000001 ||
                   (p.Id=="distress" && saved.Custom!=p.Custom))
                    throw new InvalidOperationException("Layout could not be saved.");
            }
        }
    }
}

