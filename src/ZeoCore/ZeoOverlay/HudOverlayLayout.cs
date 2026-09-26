using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Text;

namespace ZeoOverlay
{
    internal sealed partial class HudOverlayForm
    {
        private bool _layoutDrawing,_layoutWasActive;
        private readonly Dictionary<string,RectangleF> _layoutRects=new Dictionary<string,RectangleF>();
        private IPEndPoint _layoutReplyEndpoint;
        private DateTime _lastLayoutReplyUtc=DateTime.MinValue;
        private void RecordLayoutBounds(string id,RectangleF rect)
        {
            if(_layoutDrawing)
            {
                float size=_panelFit.ContainsKey(id) ? _panelFit[id] : 1;
                _layoutRects[id]=new RectangleF(rect.X,rect.Y,rect.Width*size,rect.Height*size);
            }
        }
        private readonly Dictionary<string,float> _panelFit=new Dictionary<string,float>();
        private RectangleF SizedPanelRectangle(string id,int width,int height,float x,float y,float w,float h)
        {
            float sx=(float)HudLayoutState.GetSize(_settings,id), sy=(float)HudLayoutState.GetSize(_settings,id,true);
            // A change of resolution must not push content beyond the viewport.
            float viewportFit=Math.Min(1,Math.Min((width-24f)/(w*sx),(height-24f)/(h*sy)));
            float fit=sy*viewportFit;
            _panelFit[id]=fit;
            var physical=ClampRect(width,height,x,y,w*sx*viewportFit,h*sy*viewportFit);
            return new RectangleF(physical.X,physical.Y,w*sx*viewportFit/fit,h*sy*viewportFit/fit);
        }
        private IDisposable BeginPanelSize(Graphics g,string id,RectangleF rect)
        {
            return new PanelSizeScope(this,g,_panelFit[id],rect,PanelTextScale(id));
        }
        private float _activeTextMultiplier=1;
        private Dictionary<FontKey,Font> _fittedFonts;
        private StringFormat _fitLeft,_fitRight,_fitCenter;
        private float PanelTextScale(string id)
        {
            double v=id=="ship"?_settings.ShipBoxTextScale:id=="scope"?_settings.ScopeBoxTextScale:id=="fleet"?_settings.FleetBoxTextScale:id=="ammo"?_settings.AmmoBoxTextScale:id=="roster"?_settings.RosterBoxTextScale:_settings.DistressBoxTextScale;
            return (float)(double.IsNaN(v)||double.IsInfinity(v)?1:Math.Max(.6,Math.Min(3,v)));
        }
        private struct FontKey : IEquatable<FontKey>
        {
            internal string Name;internal FontStyle Style;internal float Size;
            public bool Equals(FontKey other){return Name==other.Name&&Style==other.Style&&Size==other.Size;}
            public override bool Equals(object other){return other is FontKey&&Equals((FontKey)other);}
            public override int GetHashCode(){return (Name.GetHashCode()*397^(int)Style)*397^Size.GetHashCode();}
        }
        private Font FittedFont(Font original,float size)
        {
            size=(float)(Math.Floor(Math.Max(.5,size)*4)/4);
            if(_fittedFonts==null)_fittedFonts=new Dictionary<FontKey,Font>();
            var key=new FontKey { Name=original.Name,Style=original.Style,Size=size };
            Font font;if(_fittedFonts.TryGetValue(key,out font))return font;
            if(_fittedFonts.Count>=256){foreach(var value in _fittedFonts.Values)value.Dispose();_fittedFonts.Clear();}
            font=new Font(original.FontFamily,size,original.Style,GraphicsUnit.Pixel);_fittedFonts[key]=font;return font;
        }
        private void DisposeTextCache(){if(_fittedFonts!=null){foreach(var f in _fittedFonts.Values)f.Dispose();_fittedFonts.Clear();}_fitLeft?.Dispose();_fitRight?.Dispose();_fitCenter?.Dispose();_fitLeft=null;_fitRight=null;_fitCenter=null;}
        private sealed class PanelSizeScope : IDisposable
        {
            private readonly HudOverlayForm _owner;
            private readonly float _previousText;
            private readonly Graphics _graphics;
            private readonly System.Drawing.Drawing2D.GraphicsState _state;
            internal PanelSizeScope(HudOverlayForm owner,Graphics graphics,float size,RectangleF rect,float text)
            {
                _owner=owner;_previousText=owner._activeTextMultiplier;owner._activeTextMultiplier=text;
                _graphics=graphics; _state=graphics.Save();
                using(var matrix=new System.Drawing.Drawing2D.Matrix(size,0,0,size,rect.X*(1-size),rect.Y*(1-size)))
                    graphics.MultiplyTransform(matrix);
                graphics.SetClip(rect,System.Drawing.Drawing2D.CombineMode.Intersect);
            }
            public void Dispose() { _graphics.Restore(_state);_owner._activeTextMultiplier=_previousText; }
        }
        // Fit each field in its own column. Keep whole values and names instead
        // of allowing long modded labels to overlap the neighboring column.
        private void DrawFitted(Graphics g,string text,Font font,Brush brush,RectangleF box,bool right=false)
        { DrawFittedAligned(g,text,font,brush,box,right?StringAlignment.Far:StringAlignment.Near); }
        private void DrawCenteredFitted(Graphics g,string text,Font font,Brush brush,RectangleF box)
        { DrawFittedAligned(g,text,font,brush,box,StringAlignment.Center); }
        private void DrawFittedAligned(Graphics g,string text,Font font,Brush brush,RectangleF box,StringAlignment alignment)
        {
            if(box.Width<=0 || box.Height<=0 || string.IsNullOrEmpty(text))return;
            if(_fitLeft==null){_fitLeft=new StringFormat(StringFormat.GenericTypographic){FormatFlags=StringFormatFlags.NoWrap,Trimming=StringTrimming.EllipsisCharacter,Alignment=StringAlignment.Near,LineAlignment=StringAlignment.Center};_fitRight=(StringFormat)_fitLeft.Clone();_fitRight.Alignment=StringAlignment.Far;_fitCenter=(StringFormat)_fitLeft.Clone();_fitCenter.Alignment=StringAlignment.Center;}
            var format=alignment==StringAlignment.Center?_fitCenter:alignment==StringAlignment.Far?_fitRight:_fitLeft;
            float preferred=font.Size*(_activeTextMultiplier>0?_activeTextMultiplier:1);
            var desired=FittedFont(font,preferred);
            var measured=g.MeasureString(text,desired,int.MaxValue,format);
            // Never enlarge the frame or mutate a saved font preference. Each cell bounds its text.
            float fit=Math.Min(1,Math.Min(box.Width/Math.Max(1,measured.Width),box.Height/Math.Max(1,measured.Height)));
            var fitted=FittedFont(font,preferred*fit);
            var state=g.Save();
            try {g.SetClip(box,System.Drawing.Drawing2D.CombineMode.Intersect);g.DrawString(text,fitted,brush,box,format);}
            finally {g.Restore(state);}
        }
        private RectangleF DistressRectangle(int width,int height,float w,float h,float scale)
        {
            if(!_settings.DistressPositionCustom)
                return SizedPanelRectangle("distress",width,height,(width-w*(float)HudLayoutState.GetSize(_settings,"distress"))/2f,20f*scale,w,h);
            var origin=NormToPixel(width,height,_settings.DistressX,_settings.DistressY);
            return SizedPanelRectangle("distress",width,height,origin.X,origin.Y,w,h);
        }
        private void DrawLayoutPreview(Graphics g,int width,int height,OverlayFrame frame)
        {
            var edit=frame.Layout;
            if(edit==null || edit.Panels==null) return;
            var previous=HudLayoutState.Capture(_settings);
            var drawingState=g.Save();
            _layoutRects.Clear(); _layoutDrawing=true;
            try
            {
                edit.ApplyTo(_settings);
                // Keep a transparent hole above the native SE toolbar. It remains
                // clickable and readable even when a HUD panel is placed behind it.
                if(edit.Toolbar!=null)
                    g.ExcludeClip(Rectangle.Ceiling(new RectangleF((float)edit.Toolbar.X,(float)edit.Toolbar.Y,(float)edit.Toolbar.Width,(float)edit.Toolbar.Height)));
                DrawFlightPanel(g,width,height,frame);
                DrawScopePanel(g,width,height,frame);
                DrawFleetPanel(g,width,height,frame);
                DrawAmmoPanel(g,width,height,frame);
                DrawRosterPanel(g,width,height,frame);
                DrawDistressBanner(g,width,height,frame);
                if(!_layoutRects.ContainsKey("ammo"))
                {
                    float scale=(float)Math.Max(.60,Math.Min(2.25,_settings.TextScale*_settings.AmmoPanelScale));
                    var origin=NormToPixel(width,height,_settings.AmmoX,_settings.AmmoY);
                    RecordLayoutBounds("ammo",SizedPanelRectangle("ammo",width,height,origin.X,origin.Y,Math.Max(390,430*scale),Math.Max(100,135*scale)));
                }
                if(!_layoutRects.ContainsKey("distress"))
                {
                    float scale=(float)Math.Max(.75,Math.Min(1.7,_settings.TextScale));
                    RecordLayoutBounds("distress",DistressRectangle(width,height,Math.Max(420,520*scale),Math.Max(62,74*scale),scale));
                }
                using(var font=new Font("Consolas",14f,FontStyle.Bold,GraphicsUnit.Pixel))
                using(var fill=new SolidBrush(Color.FromArgb(225,12,24,30)))
                using(var text=new SolidBrush(Color.FromArgb(235,222,244,248)))
                {
                    foreach(var entry in _layoutRects)
                    {
                        bool selected=entry.Key==edit.Selected;
                        using(var pen=new Pen(selected ? Color.FromArgb(255,130,226,247) : Color.FromArgb(190,164,185,194),selected ? 3f : 1.5f))
                            g.DrawRectangle(pen,entry.Value.X,entry.Value.Y,entry.Value.Width,entry.Value.Height);
                        if(selected)
                        {
                            var r=entry.Value;
                            using(var grip=new SolidBrush(Color.FromArgb(255,130,226,247)))
                                foreach(var point in new[] { new PointF(r.Left,r.Top),new PointF(r.Right,r.Top),new PointF(r.Left,r.Bottom),new PointF(r.Right,r.Bottom),new PointF(r.Left+r.Width/2,r.Top),new PointF(r.Left+r.Width/2,r.Bottom),new PointF(r.Left,r.Top+r.Height/2),new PointF(r.Right,r.Top+r.Height/2) })
                                    g.FillRectangle(grip,point.X-4,point.Y-4,8,8);
                        }
                        int index=Array.IndexOf(HudLayoutState.Ids,entry.Key);
                        string label=(selected ? "[SELECTED] " : "")+HudLayoutState.Names[index]+" // MOVE / RESIZE "+(HudLayoutState.GetSize(_settings,entry.Key)*100).ToString("0")+"% W / "+(HudLayoutState.GetSize(_settings,entry.Key,true)*100).ToString("0")+"% H";
                        var size=g.MeasureString(label,font);
                        var tag=new RectangleF(entry.Value.X+4,entry.Value.Y+4,Math.Min(entry.Value.Width-8,size.Width+12),size.Height+8);
                        g.FillRectangle(fill,tag);
                        DrawFitted(g,label,font,text,new RectangleF(tag.X+5,tag.Y+3,tag.Width-10,tag.Height-6));
                    }
                }
                if((DateTime.UtcNow-_lastLayoutReplyUtc).TotalMilliseconds>=100)
                {
                    _lastLayoutReplyUtc=DateTime.UtcNow;
                    IPEndPoint endpoint;
                    lock(_frameLock) endpoint=_layoutReplyEndpoint;
                    if(endpoint!=null)
                    {
                        var feedback=new HudLayoutFeedback { Token=edit.Token,Width=width,Height=height,
                            Panels=_layoutRects.Select(p=>new HudPanelBounds { Id=p.Key,X=p.Value.X,Y=p.Value.Y,Width=p.Value.Width,Height=p.Value.Height,WidthScale=HudLayoutState.GetSize(_settings,p.Key),HeightScale=HudLayoutState.GetSize(_settings,p.Key,true) }).ToList() };
                        byte[] bytes=Encoding.UTF8.GetBytes(_json.Serialize(feedback));
                        try { _udp.Send(bytes,bytes.Length,endpoint); }
                        catch(Exception ex) { LogOverlay("Layout feedback failed: "+ex.Message); }
                    }
                }
            }
            finally { previous.ApplyTo(_settings); _layoutDrawing=false; g.Restore(drawingState); }
        }
    }
}

