using System;
using System.Runtime.InteropServices;
using System.Text;
using Sandbox.Graphics.GUI;
using Sandbox.Graphics;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace ZeoPDC
{
    internal sealed class PdcHudLayoutScreen : MyGuiScreenBase
    {
        [StructLayout(LayoutKind.Sequential)] struct Point { public int X, Y; }
        [DllImport("user32.dll")] static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        readonly PdcSettingsModel model;
        readonly Func<PdcSnapshot> snapshot;
        readonly MyGuiControlPanel[] outline = new MyGuiControlPanel[4];
        readonly MyGuiControlLabel status;
        readonly double originalX, originalY, originalScale, originalWidth, originalHeight;
        public double X { get; private set; }
        public double Y { get; private set; }
        public double PanelScale { get; private set; }
        public double HudWidth {get;private set;}
        public double HudHeight {get;private set;}
        PdcHudBounds startBounds; PdcConfig startConfig; int resizeEdges; double startX,startY;
        bool down = true, dragging, place;
        double offsetX, offsetY;
        public PdcHudLayoutScreen(PdcSettingsModel model, Func<PdcSnapshot> snapshot)
            : base(new Vector2(.5f, .5f), new Vector4(0, 0, 0, 0), new Vector2(1, 1), true)
        {
            this.model = model; this.snapshot = snapshot; model.Reload();
            X = originalX = model.Current.HudX; Y = originalY = model.Current.HudY; PanelScale=originalScale=model.Current.PanelScale; HudWidth=originalWidth=model.Current.HudWidth;HudHeight=originalHeight=model.Current.HudHeight;
            DrawMouseCursor = true; CloseButtonEnabled = false; EnabledBackgroundFade = false; CanHideOthers = false; CanBeHidden = false;
            Label(-.39f, -.466f, "ZEO PDC // EDIT HUD LAYOUT", .80f);
            Label(-.39f, -.434f, "Drag inside to move; edges resize width/height; corners resize both. PLACE moves a covered panel.", .52f);
            Button(-.32f, .17f, "PLACE PANEL", () => { place = true; dragging = false; });
            Button(-.13f, .17f, "RESET SIZE", () => { HudWidth=HudHeight=1; dragging=false; place=false; });
            Button(.05f, .16f, "UNDO ALL", () => { X=originalX; Y=originalY; PanelScale=originalScale; HudWidth=originalWidth; HudHeight=originalHeight; dragging=false; place=false; });
            Button(.23f, .17f, "SAVE LAYOUT", Save);
            Button(.39f, .13f, "CANCEL", () => CloseScreen());
            status = Label(-.39f, -.344f, "Draft only. SAVE commits; ESC / CANCEL discards.", .53f);
            for (int i = 0; i < outline.Length; i++)
            {
                outline[i] = new MyGuiControlPanel { ColorMask = new Vector4(.35f, .9f, 1, .9f), OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP };
                Controls.Add(outline[i]);
            }
        }
        public override string GetFriendlyName() { return "ZeoPdcHudLayoutEditor"; }
        public override bool Update(bool hasFocus)
        {
            bool result = base.Update(hasFocus); var s = snapshot();
            var cfg = JsonIo.FromBytes<PdcConfig>(JsonIo.ToBytes(s.Config)); cfg.HudX = X; cfg.HudY = Y; cfg.PanelScale=PanelScale;cfg.HudWidth=HudWidth;cfg.HudHeight=HudHeight;
            var bounds = PdcHudGeometry.Bounds(s.ClientW, s.ClientH, cfg);
            Vector2 top = Normalize(bounds.Left, bounds.Top), bottom = Normalize(bounds.Left + bounds.Width, bounds.Top + bounds.Height);
            // Border comes from the exact geometry also used by the external renderer.
            SetBorder(0, top.X, top.Y, bottom.X - top.X, .002f);
            SetBorder(1, top.X, bottom.Y - .002f, bottom.X - top.X, .002f);
            SetBorder(2, top.X, top.Y, .002f, bottom.Y - top.Y);
            SetBorder(3, bottom.X - .002f, top.Y, .002f, bottom.Y - top.Y);
            bool pressed = (GetAsyncKeyState(1) & 0x8000) != 0;
            if (!hasFocus || s.GameHwnd == 0 || GetForegroundWindow().ToInt64() != s.GameHwnd || s.ClientW < 200 || s.ClientH < 200)
            { dragging = false; down = pressed; return result; }
            Point p; if (!GetCursorPos(out p)) return result;
            double mouseX = p.X - s.ClientX, mouseY = p.Y - s.ClientY;
            var toolbarBottom = MyGuiManager.GetScreenCoordinateFromNormalizedCoordinate(new Vector2(.5f, .18f));
            if (pressed && !down && mouseY > toolbarBottom.Y && (place || bounds.Contains(mouseX, mouseY) || PdcHudGeometry.ResizeEdges(bounds,mouseX,mouseY)!=0))
            {
                dragging = true; resizeEdges=place?0:PdcHudGeometry.ResizeEdges(bounds,mouseX,mouseY);
                startBounds=bounds; startConfig=cfg; startX=mouseX; startY=mouseY; offsetX = place ? bounds.Width / 2.0 : mouseX - bounds.Left;
                offsetY = place ? bounds.Height / 2.0 : mouseY - bounds.Top; place = false;
            }
            if (pressed && dragging && (Math.Abs(mouseX-startX)>2 || Math.Abs(mouseY-startY)>2))
            {
                double x,y;
                if(resizeEdges!=0) {
                    double sx,sy; PdcHudGeometry.ResizeAxes(startBounds,resizeEdges,mouseX-startX,mouseY-startY,s.ClientW,s.ClientH,startConfig,out x,out y,out sx,out sy); HudWidth=sx;HudHeight=sy;
                } else PdcHudGeometry.Position(mouseX-offsetX,mouseY-offsetY,s.ClientW,s.ClientH,cfg,out x,out y);
                X=x;Y=y;
            }
            if (!pressed) dragging = false; down = pressed;
            status.Text = place ? "Click and drag below this toolbar to place the panel." : "X " + X.ToString("0.00") + "  Y " + Y.ToString("0.00") + "  W "+HudWidth.ToString("0.00")+"  H "+HudHeight.ToString("0.00")+"x   SAVE commits. ESC / CANCEL discards.";
            return result;
        }
        static Vector2 Normalize(double x, double y)
        {
            var a = MyGuiManager.GetScreenCoordinateFromNormalizedCoordinate(Vector2.Zero);
            var b = MyGuiManager.GetScreenCoordinateFromNormalizedCoordinate(Vector2.One);
            return new Vector2((float)((x - a.X) / Math.Max(1, b.X - a.X)), (float)((y - a.Y) / Math.Max(1, b.Y - a.Y)));
        }
        void SetBorder(int index, float x, float y, float width, float height)
        {
            outline[index].Position = new Vector2(x - .5f, y - .5f); outline[index].Size = new Vector2(width, height);
            // Keep the native toolbar usable even if the panel overlaps it.
            outline[index].Visible = y > .18f;
        }
        void Save()
        {
            try { model.SaveLayoutAxes(X,Y,HudWidth,HudHeight,Math.Abs(X-originalX)>1e-9||Math.Abs(Y-originalY)>1e-9,Math.Abs(HudWidth-originalWidth)>1e-9||Math.Abs(HudHeight-originalHeight)>1e-9); CloseScreen(); }
            catch (Exception ex) { status.Text = "Save failed: " + ex.Message; }
        }
        MyGuiControlLabel Label(float x, float y, string text, float scale)
        { var label = new MyGuiControlLabel(new Vector2(x, y), null, text, PdcNativeSettingsScreen.TextTint, scale, null, MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER); Controls.Add(label); return label; }
        void Button(float x, float width, string text, Action action)
        {
            Controls.Add(new MyGuiControlButton(new Vector2(x, -.386f), MyGuiControlButtonStyleEnum.Rectangular, new Vector2(width, .048f), null,
                MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, null, new StringBuilder(text), .57f,
                MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER, MyGuiControlHighlightType.WHEN_CURSOR_OVER, _ => action()));
        }
    }
}

