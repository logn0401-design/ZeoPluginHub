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
            if(_layoutDrawing) _layoutRects[id]=rect;
        }
        private RectangleF DistressRectangle(int width,int height,float w,float h,float scale)
        {
            if(!_settings.DistressPositionCustom) return new RectangleF((width-w)/2f,20f*scale,w,h);
            var origin=NormToPixel(width,height,_settings.DistressX,_settings.DistressY);
            return ClampRect(width,height,origin.X,origin.Y,w,h);
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
                    _layoutRects["ammo"]=ClampRect(width,height,origin.X,origin.Y,Math.Max(390,430*scale),Math.Max(100,135*scale));
                }
                if(!_layoutRects.ContainsKey("distress"))
                {
                    float scale=(float)Math.Max(.75,Math.Min(1.7,_settings.TextScale));
                    _layoutRects["distress"]=DistressRectangle(width,height,Math.Max(420,520*scale),Math.Max(62,74*scale),scale);
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
                        int index=Array.IndexOf(HudLayoutState.Ids,entry.Key);
                        string label=(selected ? "[SELECTED] " : "")+HudLayoutState.Names[index]+" // DRAG";
                        var size=g.MeasureString(label,font);
                        var tag=new RectangleF(entry.Value.X+4,entry.Value.Y+4,Math.Min(entry.Value.Width-8,size.Width+12),size.Height+8);
                        g.FillRectangle(fill,tag);
                        g.DrawString(label,font,text,tag.X+5,tag.Y+3);
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
                            Panels=_layoutRects.Select(p=>new HudPanelBounds { Id=p.Key,X=p.Value.X,Y=p.Value.Y,Width=p.Value.Width,Height=p.Value.Height }).ToList() };
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

