using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using VRage.Game;
using VRage.Utils;
using VRageMath;
using ZeoOverlay;

namespace ZeoCore
{
    internal static class ZeoHudLayoutSession
    {
        internal static HudLayoutState Current;
        internal static HudLayoutFeedback Feedback;
        internal static DateTime FeedbackUtc;
        internal static void Receive(HudLayoutFeedback data)
        {
            if(Current==null || data==null || data.Token!=Current.Token || data.Width<200 || data.Height<200) return;
            Feedback=data; FeedbackUtc=DateTime.UtcNow;
        }
        internal static void End() { Current=null; Feedback=null; }
    }

    internal sealed class ZeoHudLayoutScreen : MyGuiScreenBase
    {
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X,Y; }
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        private readonly HudLayoutState _layout;
        private readonly HudPanelPosition[] _original;
        private readonly Action _changed;
        private readonly MyGuiControlCombobox _select;
        private readonly MyGuiControlLabel _status;
        private bool _down=true, _saved, _placeSelected;
        private HudPanelBounds _drag;
        private double _offsetX,_offsetY,_startX,_startY;

        internal ZeoHudLayoutScreen(Action changed)
            : base(new Vector2(.5f,.5f),new Vector4(0,0,0,0),new Vector2(1,1),true)
        {
            _changed=changed;
            _layout=HudLayoutState.Capture(OverlaySettings.Load(HudSettings.PathName));
            _original=_layout.Panels.Select(p=>p.Copy()).ToArray();
            ZeoHudLayoutSession.Current=_layout; ZeoHudLayoutSession.Feedback=null;
            DrawMouseCursor=true; CloseButtonEnabled=false; EnabledBackgroundFade=false;
            CanHideOthers=false; CanBeHidden=false;
            Label(-.39f,-.466f,"ZEOCORE // EDIT HUD POSITIONS",.80f);
            Label(-.39f,-.434f,"Drag panels. For a covered panel: select it above, then drag anywhere below this toolbar.",.58f);
            _select=new MyGuiControlCombobox(new Vector2(-.240f,-.386f),new Vector2(.295f,.044f),
                originAlign:MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER,openAreaItemsCount:6);
            for(int i=0;i<HudLayoutState.Ids.Length;i++) _select.AddItem(i,HudLayoutState.Names[i]);
            _select.SelectItemByKey(0);
            _select.ItemSelected+=delegate { int index=(int)_select.GetSelectedKey(); if(index>=0 && index<6) { _layout.Selected=HudLayoutState.Ids[index]; _placeSelected=true; _drag=null; } };
            Controls.Add(_select);
            Button(-.004f,-.386f,.145f,"UNDO ALL",delegate {
                _layout.Panels=_original.Select(p=>p.Copy()).ToList(); _drag=null;
                _status.Text="Original positions restored in preview. SAVE commits; CANCEL discards.";
            });
            Button(.165f,-.386f,.160f,"SAVE LAYOUT",Save);
            Button(.335f,-.386f,.145f,"CANCEL",delegate { CloseScreen(); });
            _status=Label(-.39f,-.344f,"Waiting for HUD preview...",.55f);
        }
        public override string GetFriendlyName() { return "ZeoCoreHudLayoutEditor"; }
        public override bool Update(bool hasFocus)
        {
            bool result=base.Update(hasFocus);
            var top=MyGuiManager.GetScreenCoordinateFromNormalizedCoordinate(new Vector2(.095f,.010f));
            var bottom=MyGuiManager.GetScreenCoordinateFromNormalizedCoordinate(new Vector2(.91f,.177f));
            _layout.Toolbar=new HudPanelBounds { X=top.X,Y=top.Y,Width=bottom.X-top.X,Height=bottom.Y-top.Y };
            bool pressed=(GetAsyncKeyState(1)&0x8000)!=0;
            var window=GameWindowState.Capture();
            var feedback=ZeoHudLayoutSession.Feedback;
            bool fresh=feedback!=null && (DateTime.UtcNow-ZeoHudLayoutSession.FeedbackUtc).TotalSeconds<1 &&
                feedback.Width==window.Width && feedback.Height==window.Height;
            if(!hasFocus || !window.Focused || !fresh)
            {
                _drag=null; _down=pressed;
                if(!fresh) _status.Text="Waiting for HUD preview... The overlay must be running.";
                return result;
            }
            POINT cursor;
            if(!GetCursorPos(out cursor)) return result;
            double x=cursor.X-window.Left,y=cursor.Y-window.Top;
            bool toolbar=_select.IsOpen || _layout.Toolbar.Contains(x,y);
            if(pressed && !_down && !toolbar)
            {
                _drag=_placeSelected ? feedback.Panels.FirstOrDefault(p=>p.Id==_layout.Selected) :
                    feedback.Panels.FirstOrDefault(p=>p.Id==_layout.Selected && p.Contains(x,y)) ??
                    feedback.Panels.LastOrDefault(p=>p.Contains(x,y));
                if(_drag!=null)
                {
                    _layout.Selected=_drag.Id;
                    _select.SelectItemByKey(Array.IndexOf(HudLayoutState.Ids,_drag.Id),false);
                    _offsetX=_placeSelected ? _drag.Width/2 : x-_drag.X; _offsetY=_placeSelected ? _drag.Height/2 : y-_drag.Y; _startX=x; _startY=y;
                    _placeSelected=false;
                }
            }
            if(pressed && _drag!=null && (Math.Abs(x-_startX)>2 || Math.Abs(y-_startY)>2))
                _layout.Move(_drag.Id,x-_offsetX,y-_offsetY,_drag.Width,_drag.Height,window.Width,window.Height);
            if(!pressed) _drag=null;
            _down=pressed;
            int moved=_layout.Panels.Count(p=>p.Moved);
            _status.Text=_placeSelected ? "Drag anywhere below this toolbar to place the selected panel." : (moved==0 ? "Drag panels to place them." : moved+" panel(s) moved.")+"  SAVE LAYOUT commits. ESC / CANCEL discards.";
            return result;
        }
        private void Save()
        {
            try { _layout.Save(HudSettings.PathName); if(_changed!=null) _changed(); _saved=true; CloseScreen(); }
            catch(Exception ex) { _status.Text="Save failed: "+ex.Message; Plugin.Log("Layout save failed: "+ex); }
        }
        protected override void OnClosed()
        {
            ZeoHudLayoutSession.End();
            Plugin.Log(_saved ? "HUD layout saved." : "HUD layout edit cancelled.");
            base.OnClosed();
        }
        private MyGuiControlLabel Label(float x,float y,string text,float scale)
        {
            var c=new MyGuiControlLabel(new Vector2(x,y),null,text,new Vector4(.82f,.91f,.94f,1),scale,null,MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
            Controls.Add(c); return c;
        }
        private void Button(float x,float y,float width,string text,Action click)
        {
            Controls.Add(new MyGuiControlButton(new Vector2(x,y),MyGuiControlButtonStyleEnum.Rectangular,new Vector2(width,.048f),null,
                MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER,null,new StringBuilder(text),.60f,
                MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER,MyGuiControlHighlightType.WHEN_CURSOR_OVER,delegate(MyGuiControlButton b) { click(); }));
        }
    }
}

