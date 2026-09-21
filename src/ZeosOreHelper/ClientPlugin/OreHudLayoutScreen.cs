using System;
using ZeoOreShared;
using ZeosOreOverlay;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace ZeosOreHelper
{
    internal sealed class OreHudLayoutScreen : MyGuiScreenBase
    {
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X,Y; }
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        private readonly Plugin host;
        private readonly OreLayoutModel model;
        private readonly MyGuiControlLabel status;
        private bool down=true,dragging,placeSelected;
        private double offsetX,offsetY,startX,startY;
        internal OreHudLayoutScreen(Plugin host)
            : base(new Vector2(.5f,.5f),new Vector4(0,0,0,0),new Vector2(1,1),true)
        {
            this.host=host; model=new OreLayoutModel(new OreOverlaySettings(HudSettings.SettingsPath)); host.Layout=model.Draft; 
            DrawMouseCursor=true; CloseButtonEnabled=false; EnabledBackgroundFade=false;
            CanHideOthers=false; CanBeHidden=false;
            Label(-.354f,-.460f,"ZEO ORE // EDIT LIST POSITION",.75f);
            Label(-.354f,-.429f,"Drag the outlined panel. PLACE PANEL lets you drag it from anywhere below.",.50f);
            Button(-.285f,-.387f,.14f,"PLACE PANEL",delegate { placeSelected=true; dragging=false; });
            Button(-.132f,-.387f,.14f,"UNDO",delegate { model.Undo(); dragging=false; });
            Button(.174f,-.387f,.14f,"SAVE",Save);
            Button(.327f,-.387f,.14f,"CANCEL",delegate { CloseScreen(); });
            Button(.021f,-.387f,.14f,"RESET POS",delegate { model.ResetPosition(); dragging=false; });
            status=Label(-.354f,-.344f,"Drag the panel outline, or use PLACE PANEL.",.50f);
        }
        public override string GetFriendlyName() { return "ZeoOreHudLayoutEditor"; }
        public override bool Update(bool hasFocus)
        {
            bool result=base.Update(hasFocus);
            var top=MyGuiManager.GetScreenCoordinateFromNormalizedCoordinate(new Vector2(.13f,.01f));
            var bottom=MyGuiManager.GetScreenCoordinateFromNormalizedCoordinate(new Vector2(.905f,.178f));
            model.Draft.ToolbarX=top.X; model.Draft.ToolbarY=top.Y;
            model.Draft.ToolbarW=bottom.X-top.X; model.Draft.ToolbarH=bottom.Y-top.Y;
            var snapshot=GameWindowState.Capture(); var frame=host.LastFrame;
            var ack=host.LayoutAck;
            var bounds=ack==null?new OreBounds():new OreBounds{X=ack.X,Y=ack.Y,Width=ack.Width,Height=ack.Height};
            bool pressed=(GetAsyncKeyState(1)&0x8000)!=0;
            bool fresh=snapshot.Valid && host.LayoutFresh && ack.ViewportW==snapshot.Width && ack.ViewportH==snapshot.Height;
            if(!hasFocus || !snapshot.Focused || !fresh)
            {
                dragging=false; down=pressed;
                if(!fresh) status.Text="Waiting for the external HUD preview.";
                return result;
            }
            POINT cursor; if(!GetCursorPos(out cursor)) return result;
            double x=cursor.X-snapshot.Left,y=cursor.Y-snapshot.Top;
            if(pressed && !down && !model.Draft.ToolbarContains(x,y) && x>=0 && y>=0 && x<snapshot.Width && y<snapshot.Height)
            {
                dragging=placeSelected || bounds.Contains(x,y);
                if(dragging)
                {
                    offsetX=placeSelected ? bounds.Width/2 : x-bounds.X;
                    offsetY=placeSelected ? bounds.Height/2 : y-bounds.Y;
                    startX=x; startY=y; placeSelected=false;
                }
            }
            if(pressed && dragging && (Math.Abs(x-startX)>2 || Math.Abs(y-startY)>2))
                model.Move(x-offsetX,y-offsetY,bounds.Width,bounds.Height,snapshot.Width,snapshot.Height);
            if(!pressed) dragging=false;
            down=pressed;
            status.Text=placeSelected ? "Drag below this toolbar to place the ranking panel." : "SAVE commits moved coordinates. UNDO restores the starting position. ESC cancels.";
            return result;
        }
        private void Save()
        {
            try
            {
                var changes=model.Changes();
                if(changes.Count>0)
                {
                    if(!host.LayoutFresh||!GameWindowState.Capture().Valid)throw new InvalidOperationException("Wait for a valid game viewport.");
                    OreIni.Merge(HudSettings.SettingsPath,changes);host.NativeSettingsChanged();
                }
                CloseScreen();
            }
            catch(Exception ex) { status.Text="Save failed: "+ex.Message; }
        }
        protected override void OnClosed() { host.Layout=null;  base.OnClosed(); }
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
