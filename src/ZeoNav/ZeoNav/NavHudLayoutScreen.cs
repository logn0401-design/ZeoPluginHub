using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace ZeoNav
{
    internal sealed class NavLayoutModel
    {
        public readonly NavLayoutDraft Draft;
        private readonly double originalX,originalY,originalWidth,originalHeight;
        public NavLayoutModel(NavConfig config)
        {
            originalX=config.HudX; originalY=config.HudY; originalWidth=Size(config.HudWidth); originalHeight=Size(config.HudHeight);
            Draft=new NavLayoutDraft { Token=Guid.NewGuid().ToString("N"),X=originalX,Y=originalY,WidthScale=originalWidth,HeightScale=originalHeight };
        }
        public void Move(double left,double top,double panelW,double panelH,int viewportW,int viewportH)
        {
            if(viewportW<200 || viewportH<200 || panelW<=0 || panelH<=0 ||
                !Finite(left) || !Finite(top) || !Finite(panelW) || !Finite(panelH)) return;
            left=Math.Max(6,Math.Min(Math.Max(6,viewportW-panelW-6),left));
            top=Math.Max(6,Math.Min(Math.Max(6,viewportH-panelH-6),top));
            Draft.X=Math.Max(-.98,Math.Min(.98,left*2/viewportW-1));
            Draft.Y=Math.Max(-.98,Math.Min(.98,1-top*2/viewportH));
        }
        private static bool Finite(double n) { return !double.IsNaN(n) && !double.IsInfinity(n); }
        public static double Size(double v) { return !Finite(v)||v<=0?1:Math.Max(.5,Math.Min(3,v)); }
        public void Resize(NavLayoutBounds start,int edges,double dx,double dy,double sw,double sh,int w,int h) {
            if(start==null||start.Width<=0||start.Height<=0||!Finite(dx)||!Finite(dy)||w<200||h<200)return;
            sw=Size(sw);sh=Size(sh);
            double nw=start.Width,nh=start.Height;
            if((edges&3)!=0)nw=Math.Max(start.Width*.5/sw,Math.Min(start.Width*3/sw,Math.Min((edges&1)!=0?start.X+start.Width-6:w-start.X-6,start.Width+((edges&1)!=0?-dx:dx))));
            if((edges&12)!=0)nh=Math.Max(start.Height*.5/sh,Math.Min(start.Height*3/sh,Math.Min((edges&4)!=0?start.Y+start.Height-6:h-start.Y-6,start.Height+((edges&4)!=0?-dy:dy))));
            Move((edges&1)!=0?start.X+start.Width-nw:start.X,(edges&4)!=0?start.Y+start.Height-nh:start.Y,nw,nh,w,h);
            Draft.WidthScale=Size(sw*nw/start.Width);Draft.HeightScale=Size(sh*nh/start.Height);
        }
        public void Undo() { Draft.X=originalX; Draft.Y=originalY; Draft.WidthScale=originalWidth; Draft.HeightScale=originalHeight; }
        public Dictionary<string,object> Changes()
        {
            var result=new Dictionary<string,object>();
            if(Math.Abs(Draft.X-originalX)>1e-9) result["HudX"]=Draft.X;
            if(Math.Abs(Draft.Y-originalY)>1e-9) result["HudY"]=Draft.Y;
            if(Math.Abs(Draft.WidthScale-originalWidth)>1e-9)result["HudWidth"]=Draft.WidthScale;
            if(Math.Abs(Draft.HeightScale-originalHeight)>1e-9)result["HudHeight"]=Draft.HeightScale;
            return result;
        }
    }

    internal sealed class NavHudLayoutScreen : MyGuiScreenBase
    {
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X,Y; }
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        private readonly NavUiHost host;
        private readonly NavLayoutModel model;
        private readonly MyGuiControlLabel status;
        private bool down=true,dragging,placeSelected;
        private double offsetX,offsetY,startX,startY,startW,startH;
        private NavLayoutBounds resizeStart; private int edges;
        internal NavHudLayoutScreen(NavUiHost host)
            : base(new Vector2(.5f,.5f),new Vector4(0,0,0,0),new Vector2(1,1),true)
        {
            this.host=host; model=new NavLayoutModel(host.Store.Read()); host.Layout=model.Draft; host.Bounds=null;
            DrawMouseCursor=true; CloseButtonEnabled=false; EnabledBackgroundFade=false;
            CanHideOthers=false; CanBeHidden=false;
            Label(-.354f,-.460f,"ZEO NAV // EDIT TRIP HUD",.75f);
            Label(-.354f,-.429f,"Drag inside to move; drag edges or corners to resize. Text follows panel height.",.50f);
            Button(-.27f,-.387f,.18f,"PLACE PANEL",delegate { placeSelected=true; dragging=false; });
            Button(-.072f,-.387f,.18f,"UNDO",delegate { model.Undo(); dragging=false; });
            Button(.126f,-.387f,.18f,"SAVE",Save);
            Button(.313f,-.387f,.16f,"CANCEL",delegate { CloseScreen(); });
            status=Label(-.354f,-.344f,"Waiting for the actual HUD bounds...",.50f);
        }
        public override string GetFriendlyName() { return "ZeoNavHudLayoutEditor"; }
        public override bool Update(bool hasFocus)
        {
            bool result=base.Update(hasFocus);
            var top=MyGuiManager.GetScreenCoordinateFromNormalizedCoordinate(new Vector2(.13f,.01f));
            var bottom=MyGuiManager.GetScreenCoordinateFromNormalizedCoordinate(new Vector2(.905f,.178f));
            model.Draft.ToolbarX=top.X; model.Draft.ToolbarY=top.Y;
            model.Draft.ToolbarW=bottom.X-top.X; model.Draft.ToolbarH=bottom.Y-top.Y;
            var snapshot=host.Snapshot(); var bounds=host.Bounds;
            bool pressed=(GetAsyncKeyState(1)&0x8000)!=0;
            bool fresh=bounds!=null && (DateTime.UtcNow-host.BoundsUtc).TotalSeconds<1 && bounds.ViewportW==snapshot.ClientW && bounds.ViewportH==snapshot.ClientH;
            if(!hasFocus || GetForegroundWindow().ToInt64()!=snapshot.GameHwnd || !fresh)
            {
                dragging=false; down=pressed;
                if(!fresh) status.Text="Waiting for HUD preview. The external overlay must be running.";
                return result;
            }
            POINT cursor; if(!GetCursorPos(out cursor)) return result;
            double x=cursor.X-snapshot.ClientX,y=cursor.Y-snapshot.ClientY;
            if(pressed && !down && !model.Draft.ToolbarContains(x,y) && x>=0 && y>=0 && x<snapshot.ClientW && y<snapshot.ClientH)
            {
                edges=placeSelected?0:bounds.Edges(x,y);
                dragging=placeSelected || bounds.Contains(x,y) || edges!=0;
                if(dragging)
                {
                    offsetX=placeSelected ? bounds.Width/2 : x-bounds.X;
                    offsetY=placeSelected ? bounds.Height/2 : y-bounds.Y;
                    startX=x; startY=y; resizeStart=bounds; startW=model.Draft.WidthScale; startH=model.Draft.HeightScale; placeSelected=false;
                }
            }
            if(pressed && dragging && (Math.Abs(x-startX)>2 || Math.Abs(y-startY)>2))
            {
                if(edges!=0)model.Resize(resizeStart,edges,x-startX,y-startY,startW,startH,snapshot.ClientW,snapshot.ClientH);
                else model.Move(x-offsetX,y-offsetY,resizeStart.Width,resizeStart.Height,snapshot.ClientW,snapshot.ClientH);
            }
            if(!pressed) dragging=false;
            down=pressed;
            status.Text=placeSelected ? "Drag below this toolbar to place the trip panel." : "SAVE commits layout. UNDO restores starting size and position. ESC cancels.";
            return result;
        }
        private void Save()
        {
            try
            {
                var changes=model.Changes();
                if(changes.Count>0)
                {
                    if(host.Bounds==null || (DateTime.UtcNow-host.BoundsUtc).TotalSeconds>=1) throw new InvalidOperationException("Wait for fresh preview before saving.");
                    host.Store.Apply(changes);
                }
                CloseScreen();
            }
            catch(Exception ex) { status.Text="Save failed: "+ex.Message; }
        }
        protected override void OnClosed() { host.Layout=null; host.Bounds=null; base.OnClosed(); }
        private MyGuiControlLabel Label(float x,float y,string text,float scale)
        {
            var label=new MyGuiControlLabel(new Vector2(x,y),null,text,new Vector4(.82f,.91f,.94f,1),scale,null,MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
            Controls.Add(label); return label;
        }
        private void Button(float x,float y,float width,string text,Action action)
        {
            Controls.Add(new MyGuiControlButton(new Vector2(x,y),MyGuiControlButtonStyleEnum.Rectangular,new Vector2(width,.044f),null,
                MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER,null,new StringBuilder(text),.60f,
                MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER,MyGuiControlHighlightType.WHEN_CURSOR_OVER,delegate(MyGuiControlButton b) { action(); }));
        }
    }
}
