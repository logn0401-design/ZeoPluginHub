using System;
using ZeoOreShared;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ZeosOreOverlay
{
    internal sealed class HudOverlayForm : Form
    {
        private readonly OreOverlaySettings _s;
        private readonly JavaScriptSerializer _json=new JavaScriptSerializer{MaxJsonLength=8*1024*1024};
        private readonly object _lock=new object();
        private OreOverlayFrame _frame=new OreOverlayFrame();private long _lastFrameMs;private long _lastSeq=-1;
        private IPEndPoint _reply;private bool _presented;
        private UdpClient _udp;private Thread _rx;private volatile bool _running=true;private readonly System.Windows.Forms.Timer _render;private readonly System.Windows.Forms.Timer _settingsTimer;private SettingsForm _menu;
        private IntPtr _gameHwnd=IntPtr.Zero;private int _gamePid;private DateTime _lastGameForeground=DateTime.MinValue;private bool _captureHud,_captureMenu;

        internal bool CaptureHudApplied{get{return _captureHud;}}internal bool CaptureMenuApplied{get{return _captureMenu;}}
        protected override void SetVisibleCore(bool value){base.SetVisibleCore(false);}
        protected override void OnPaintBackground(PaintEventArgs e){}
        protected override void OnPaint(PaintEventArgs e){}
        protected override bool ShowWithoutActivation{get{return true;}}
        protected override CreateParams CreateParams{get{var cp=base.CreateParams;cp.ExStyle|=NativeMethods.WS_EX_LAYERED|NativeMethods.WS_EX_TRANSPARENT|NativeMethods.WS_EX_TOOLWINDOW|NativeMethods.WS_EX_NOACTIVATE;return cp;}}

        internal HudOverlayForm(OreOverlaySettings settings,int port)
        {
            _s=settings;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;Bounds=new Rectangle(0,0,1,1);_menu=new SettingsForm(_s,this);var hiddenHandle=Handle;HideOverlay();try{StartReceiver(port);}catch(Exception ex){OverlayLog.Error(ex);throw;}
            _render=new System.Windows.Forms.Timer{Interval=33};_render.Tick+=(a,b)=>{try{RenderTick();}catch(Exception ex){HideOverlay();OverlayLog.Error(ex);}};_render.Start();
            _settingsTimer=new System.Windows.Forms.Timer{Interval=400};_settingsTimer.Tick+=(a,b)=>{if(_s.ReloadIfChanged()){ApplyCaptureAffinity();if(_menu!=null&&!_menu.IsDisposed)_menu.RefreshPage();_lastSeq=-1;}};_settingsTimer.Start();
        }
        protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);ApplyCaptureAffinity();}

        private void StartReceiver(int port)
        {
            _udp=new UdpClient(new IPEndPoint(IPAddress.Loopback,port));_rx=new Thread(()=>ReceiveLoop()){IsBackground=true,Name="ZeosOreOverlay-RX"};_rx.Start();
        }
        private void ReceiveLoop()
        {
            var ep=new IPEndPoint(IPAddress.Loopback,0);while(_running)
            {
                try
                {
                    byte[] d=_udp.Receive(ref ep);if(ep.Address==null||!IPAddress.IsLoopback(ep.Address))continue;var p=_json.Deserialize<OreOverlayPacket>(Encoding.UTF8.GetString(d));if(p==null)continue;
                    if(string.Equals(p.Kind,"frame",StringComparison.OrdinalIgnoreCase)&&p.Frame!=null){lock(_lock){_frame=p.Frame;_reply=new IPEndPoint(ep.Address,ep.Port);_lastFrameMs=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();}}
                    else if(string.Equals(p.Kind,"command",StringComparison.OrdinalIgnoreCase))
                    {
                        if(string.Equals(p.Command,"toggle-menu",StringComparison.OrdinalIgnoreCase))SafeBegin(ToggleMenu);
                        else if(string.Equals(p.Command,"open-menu",StringComparison.OrdinalIgnoreCase))SafeBegin(()=>{if(_menu==null||!_menu.Visible)ToggleMenu();});
                        else if(string.Equals(p.Command,"reload-settings",StringComparison.OrdinalIgnoreCase))SafeBegin(()=>{_s.Load();ApplyCaptureAffinity();if(_menu!=null&&!_menu.IsDisposed)_menu.RefreshPage();_lastSeq=-1;});
                        else if(string.Equals(p.Command,"shutdown",StringComparison.OrdinalIgnoreCase))SafeBegin(Close);
                    }
                }catch(SocketException ex){if(!_running)break;OverlayLog.Error(ex);Thread.Sleep(100);}catch(ObjectDisposedException){break;}catch{Thread.Sleep(25);}
            }
        }
        private void SafeBegin(Action a){try{if(!IsDisposed&&IsHandleCreated)BeginInvoke(a);}catch{}}

        internal void ToggleMenu()
        {
            if(_menu==null||_menu.IsDisposed)_menu=new SettingsForm(_s,this);if(_menu.Visible){_menu.Hide();return;}
            _menu.RefreshPage();PositionMenu();var handle=_menu.Handle;ApplyCaptureAffinity();
            if(_s.B("StreamerMode")&&_s.B("StreamerFailClosed",true)&&!_captureMenu)return;
            _menu.Show();_menu.BringToFront();ApplyCaptureAffinity();
        }
        private void PositionMenu()
        {
            Rectangle target=Screen.PrimaryScreen.WorkingArea;
            NativeMethods.RECT r;
            if(_gameHwnd!=IntPtr.Zero&&TryGetGameRect(_gameHwnd,out r))target=Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom);
            int x=target.Left+Math.Max(12,(target.Width-_menu.Width)/2);
            int y=target.Top+Math.Max(12,(target.Height-_menu.Height)/2);
            _menu.Location=new Point(x,y);
        }
        internal void SettingsChanged()
        {
            _s.Save();ApplyCaptureAffinity();_lastSeq=-1;
            if(_s.B("StreamerMode")&&_s.B("StreamerFailClosed",true)&&_menu!=null&&_menu.Visible&&!_captureMenu)_menu.Hide();
        }

        internal void ApplyCaptureAffinity()
        {
            bool streamer=_s.B("StreamerMode");
            if(IsHandleCreated){uint want=streamer?NativeMethods.WDA_EXCLUDEFROMCAPTURE:NativeMethods.WDA_NONE;bool ok=NativeMethods.SetWindowDisplayAffinity(Handle,want);uint actual;bool verified=ok&&NativeMethods.GetWindowDisplayAffinity(Handle,out actual)&&actual==want;_captureHud=streamer&&verified;}
            if(_menu!=null&&_menu.IsHandleCreated){uint want=streamer?NativeMethods.WDA_EXCLUDEFROMCAPTURE:NativeMethods.WDA_NONE;bool ok=NativeMethods.SetWindowDisplayAffinity(_menu.Handle,want);uint actual;bool verified=ok&&NativeMethods.GetWindowDisplayAffinity(_menu.Handle,out actual)&&actual==want;_captureMenu=streamer&&verified;}
        }

        private void RenderTick()
        {
            if(!_running)return;OreOverlayFrame f;long got;lock(_lock){f=_frame;got=_lastFrameMs;}long age=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-got;
            if(f==null||got==0||age>2500||(!f.HelperEnabled&&f.Layout==null)){HideOverlay();return;}
            if(_s.B("StreamerMode")&&_s.B("StreamerFailClosed",true)&&!_captureHud){ApplyCaptureAffinity();if(!_captureHud){HideOverlay();return;}}
            NativeMethods.RECT rect;if(!TryFrameRect(f,out rect)){FindGameWindow();if(_gameHwnd==IntPtr.Zero||!TryGetGameRect(_gameHwnd,out rect)){HideOverlay();return;}}
            if(_gameHwnd==IntPtr.Zero)FindGameWindow();AttachOwner();bool gameFg=IsGameForeground(),menuFg=IsMenuForeground();if(gameFg||f.GameFocused||menuFg)_lastGameForeground=DateTime.UtcNow;bool recent=_lastGameForeground!=DateTime.MinValue&&(DateTime.UtcNow-_lastGameForeground).TotalMilliseconds<=180;
            if((!gameFg&&!f.GameFocused&&!menuFg&&!recent)||(_gameHwnd!=IntPtr.Zero&&NativeMethods.IsIconic(_gameHwnd))){try{if(_menu!=null&&_menu.Visible)_menu.Hide();}catch{}HideOverlay();return;}
            if(f.Sequence==_lastSeq)return;_lastSeq=f.Sequence;int w=rect.Right-rect.Left,h=rect.Bottom-rect.Top;if(w<200||h<200||w>16384||h>16384||(long)w*h>40000000){HideOverlay();return;}
            using(var bmp=new Bitmap(w,h,PixelFormat.Format32bppPArgb))using(var g=Graphics.FromImage(bmp)){g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;g.CompositingMode=CompositingMode.SourceOver;g.Clear(Color.Transparent);DrawHud(g,w,h,f);Present(bmp,rect.Left,rect.Top);if(_presented){EnsureTopmost();if(f.Layout!=null&&_reply!=null){var bounds=OreGeometry.Bounds(_s,w,h,Math.Min(Math.Max(1,Math.Min(20,_s.I("ListRows",8))),f.Roids.Count(r=>r!=null&&r.ListEligible)),f.Layout);var ack=new OreLayoutAck{Token=f.Layout.Token,X=bounds.X,Y=bounds.Y,Width=bounds.Width,Height=bounds.Height,ViewportW=w,ViewportH=h};var data=Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(ack));_udp.Send(data,data.Length,_reply);}}}
        }

        private void DrawHud(Graphics g,int w,int h,OreOverlayFrame f)
        {
            if(f.Layout==null && f.HelperEnabled && _s.B("MarkersEnabled",true))DrawPings(g,w,h,f);
            var state=g.Save();try {
            if(f.Layout!=null)g.ExcludeClip(Rectangle.Round(new RectangleF(f.Layout.ToolbarX,f.Layout.ToolbarY,f.Layout.ToolbarW,f.Layout.ToolbarH)));
            if(f.HelperEnabled&&_s.B("ListEnabled",true)&&!string.Equals(_s.Get("HudLayout"),"Minimal",StringComparison.OrdinalIgnoreCase))DrawList(g,w,h,f);
            if(f.Layout!=null){var b=OreGeometry.Bounds(_s,w,h,Math.Min(Math.Max(1,Math.Min(20,_s.I("ListRows",8))),f.Roids.Count(r=>r!=null&&r.ListEligible)),f.Layout);using(var pen=new Pen(Color.FromArgb(220,140,225,245),2)){pen.DashStyle=DashStyle.Dash;g.DrawRectangle(pen,(float)b.X,(float)b.Y,(float)b.Width,(float)b.Height);}}
            }finally{g.Restore(state);}
        }

        private void DrawPings(Graphics g,int w,int h,OreOverlayFrame f)
        {
            var budget=new OrePingBudget(_s.I("MaxMarkers",8),_s.I("MaxOffscreenPings",2),_s.I("PingDetailLimit",3));
            var occupied=new List<RectangleF>();
            DrawDeposits(g,w,h,f,budget,occupied);
            foreach(var r in (f.Roids??new List<OreOverlayRoid>()).Where(r=>r!=null).OrderByDescending(r=>r.Selected).ThenByDescending(r=>r.Pinned).Take(100))
            {
                if(!r.PingEligible||!OreLearningStore.Finite(r.ScreenX)||!OreLearningStore.Finite(r.ScreenY)||!OreLearningStore.Finite(r.DistanceMeters))continue;
                if(r.DistanceMeters>OreGeometry.Safe(_s.D("PingMaxDistanceKm",1000),1,1000,1000)*1000)continue;
                if(r.Offscreen&&!_s.B("PingOffscreenArrows",true))continue;if(!budget.Take(r.Offscreen))continue;
                float rawX=(float)((Math.Max(-10000,Math.Min(10000,r.ScreenX))+1)*.5*w),rawY=(float)((1-Math.Max(-10000,Math.Min(10000,r.ScreenY)))*.5*h);
                float x=Math.Max(28,Math.Min(w-28,rawX)),y=Math.Max(28,Math.Min(h-28,rawY));
                Color c=PingColor(r);float size=12f*(float)OreGeometry.Safe(_s.D("MarkerScale",1),.35,3,1)*PingSizeFactor(r);
                if(r.Selected)size*=(float)OreGeometry.Safe(_s.D("SelectedMarkerScale",1.25),.5,3,1.25);
                if(r.Offscreen)size*=(float)OreGeometry.Safe(_s.D("OffscreenMarkerScale",1),.35,3,1);size=(float)OreGeometry.Safe(size,3,100,12);
                if(r.Offscreen)DrawArrow(g,new PointF(x,y),new PointF(rawX-w/2f,rawY-h/2f),c,size);else DrawMarker(g,new PointF(x,y),c,size,r.Selected,r.Pinned);
                if(!r.DetailLabel)continue;string label=PingLabel(r);if(label.Length==0)continue;
                using(var font=new Font("Bahnschrift SemiCondensed",Math.Max(8f,10.5f*(float)OreGeometry.Safe(_s.D("PingTextScale",1.2),.5,3,1.2)),FontStyle.Bold,GraphicsUnit.Pixel)){
                    var measured=g.MeasureString(label,font);float lx=x+size+(float)OreGeometry.Safe(_s.D("PingLabelOffsetX",4),-200,200,4),ly=y-size*.65f+(float)OreGeometry.Safe(_s.D("PingLabelOffsetY",0),-200,200,0);
                    lx=Math.Max(4,Math.Min(w-measured.Width-4,lx));ly=Math.Max(4,Math.Min(h-measured.Height-4,ly));
                    var rect=new RectangleF(lx-2,ly-1,measured.Width+4,measured.Height+2);float gap=(float)OreGeometry.Safe(_s.D("PingLabelSpacing",4),0,40,4);var collision=rect;collision.Inflate(gap,gap);
                    if(_s.B("PingHideOverlap",true)&&occupied.Any(p=>p.IntersectsWith(collision)))continue;if(!budget.Detail())continue;occupied.Add(collision);
                    int alpha=Math.Max(0,Math.Min(255,_s.I("PingTextOpacity",245)));Color text=_s.B("PingTextUseMarkerColor",true)?c:C(_s.Get("PingTextColor","#D1E8EF"),Color.White);
                    if(_s.B("PingTextBackground",true))using(var bg=new SolidBrush(Color.FromArgb(alpha*150/255,8,14,19)))g.FillRectangle(bg,rect);
                    if(_s.B("PingTextOutline",true))using(var shadow=new SolidBrush(Color.FromArgb(alpha,0,0,0)))foreach(var offset in new[]{new Point(-1,0),new Point(1,0),new Point(0,-1),new Point(0,1)})g.DrawString(label,font,shadow,lx+offset.X,ly+offset.Y);
                    using(var brush=new SolidBrush(Color.FromArgb(alpha,text)))g.DrawString(label,font,brush,lx,ly);
                }
            }
        }
        private void DrawDeposits(Graphics g,int w,int h,OreOverlayFrame f,OrePingBudget budget,List<RectangleF> occupied)
        {
            if(!_s.B("DepositMarkers",true))return;
            double range=OreGeometry.Safe(_s.D("DepositRangeKm",5),.1,5,5)*1000;
            int maximum=Math.Max(0,Math.Min(20,_s.I("MaxDepositMarkers",4))),count=0;
            foreach(var d in f.Deposits??new List<OreOverlayDeposit>()){
                if(d==null||count>=maximum||!_s.B("OreEnabled:"+d.Ore,true)||!_s.B("OreShowPing:"+d.Ore,true))continue;
                if(!OreLearningStore.Finite(d.ScreenX)||!OreLearningStore.Finite(d.ScreenY)||!OreLearningStore.Finite(d.DistanceMeters)||!OreLearningStore.Finite(d.AsteroidDistanceMeters)||!OreLearningStore.Finite(d.RadiusX)||!OreLearningStore.Finite(d.RadiusY)||!OreLearningStore.Finite(d.DiameterMeters)||d.DistanceMeters<0||d.AsteroidDistanceMeters<0||d.AsteroidDistanceMeters>range||Math.Abs(d.ScreenX)>.98||Math.Abs(d.ScreenY)>.98)continue;
                if(!budget.Take(false))break;count++;
                float x=(float)((d.ScreenX+1)*w*.5),y=(float)((1-d.ScreenY)*h*.5);
                Color color=C(d.Color,Color.Cyan);float size=6f*(float)OreGeometry.Safe(_s.D("MarkerScale",1),.35,3,1);
                using(var pen=new Pen(Color.FromArgb(175,color),1.4f)){
                    if(_s.B("DepositOutlines",true)){pen.DashStyle=DashStyle.Dash;float rx=(float)Math.Max(5,Math.Min(w,d.RadiusX*w*.5)),ry=(float)Math.Max(5,Math.Min(h,d.RadiusY*h*.5));g.DrawEllipse(pen,x-rx,y-ry,rx*2,ry*2);pen.DashStyle=DashStyle.Solid;}
                    g.DrawLine(pen,x-size,y,x+size,y);g.DrawLine(pen,x,y-size,x,y+size);
                }
                if(!d.DetailLabel)continue;
                string label=(d.Ore??"Ore")+" deposit  "+Range(d.DistanceMeters);
                if(_s.B("DepositShowSize",true))label+="\n~"+d.DiameterMeters.ToString("0")+" m extent";
                using(var font=new Font("Bahnschrift SemiCondensed",Math.Max(8f,10.5f*(float)OreGeometry.Safe(_s.D("PingTextScale",1.2),.5,3,1.2)),FontStyle.Bold,GraphicsUnit.Pixel)){
                    var measured=g.MeasureString(label,font);float lx=x+size+(float)OreGeometry.Safe(_s.D("PingLabelOffsetX",4),-200,200,4),ly=y-size*.65f+(float)OreGeometry.Safe(_s.D("PingLabelOffsetY",0),-200,200,0);
                    lx=Math.Max(4,Math.Min(w-measured.Width-4,lx));ly=Math.Max(4,Math.Min(h-measured.Height-4,ly));
                    var rect=new RectangleF(lx-2,ly-1,measured.Width+4,measured.Height+2);var collision=rect;collision.Inflate((float)OreGeometry.Safe(_s.D("PingLabelSpacing",4),0,40,4),(float)OreGeometry.Safe(_s.D("PingLabelSpacing",4),0,40,4));
                    if(_s.B("PingHideOverlap",true)&&occupied.Any(p=>p.IntersectsWith(collision)))continue;if(!budget.Detail())continue;occupied.Add(collision);
                    int alpha=Math.Max(0,Math.Min(255,_s.I("PingTextOpacity",245)));Color text=_s.B("PingTextUseMarkerColor",true)?color:C(_s.Get("PingTextColor","#D1E8EF"),Color.White);
                    if(_s.B("PingTextBackground",true))using(var bg=new SolidBrush(Color.FromArgb(alpha*150/255,8,14,19)))g.FillRectangle(bg,rect);
                    if(_s.B("PingTextOutline",true))using(var shadow=new SolidBrush(Color.FromArgb(alpha,0,0,0)))foreach(var offset in new[]{new Point(-1,0),new Point(1,0),new Point(0,-1),new Point(0,1)})g.DrawString(label,font,shadow,lx+offset.X,ly+offset.Y);
                    using(var brush=new SolidBrush(Color.FromArgb(alpha,text)))g.DrawString(label,font,brush,lx,ly);
                }
            }
        }
        private float PingSizeFactor(OreOverlayRoid r)
        {
            string mode=_s.Get("PingSizeMode","Distance + Grade");float d=1,g=1;if(mode.IndexOf("Distance",StringComparison.OrdinalIgnoreCase)>=0){double t=Math.Max(0,Math.Min(1,(r.DistanceMeters-2000)/58000.0));d=(float)(0.82+0.50*t);}if(mode.IndexOf("Grade",StringComparison.OrdinalIgnoreCase)>=0){string x=(r.Grade??"X").ToUpperInvariant();g=x=="S"?1.35f:x=="A"?1.20f:x=="B"?1.05f:x=="C"?0.95f:x=="D"?0.86f:0.80f;}return d*g;
        }
        private Color PingColor(OreOverlayRoid r)
        {
            string mode=_s.Get("PingColorMode","Grade");if(r.Selected)return C(_s.Get("SelectedColor","#FFE16B"),Color.Gold);if(mode.Equals("Top Ore",StringComparison.OrdinalIgnoreCase))return C(r.OreColor,Color.White);if(mode.Equals("Theme",StringComparison.OrdinalIgnoreCase))return C(_s.Get("HudAccentColor","#D83B3B"),Color.Red);if(mode.Equals("Custom",StringComparison.OrdinalIgnoreCase))return C(_s.Get("PingCustomColor","#E7ECF2"),Color.White);return C(r.GradeColor,Color.White);
        }
        private string PingLabel(OreOverlayRoid r)
        {
            string style=_s.Get("PingLabelStyle","Simple + target detail");
            if(style=="Simple"||style=="Simple + target detail"){
                string ore=string.IsNullOrWhiteSpace(r.TopOre)?"Asteroid":r.TopOre;
                string distance=r.DistanceMeters>=1000?(r.DistanceMeters/1000).ToString(r.DistanceMeters>=10000?"0":"0.0")+" km":r.DistanceMeters.ToString("0")+" m";
                string simple=ore+"  "+distance;
                if(style=="Simple + target detail"&&r.Selected&&_s.B("PingShowAmount",true)&&r.EstimatedVolume>0)simple+="\n~"+Amount(r.EstimatedVolume)+" m3"+(r.ScanStatus=="SDX2 EST"?" (SDX2)":"");
                return simple;
            }
            var parts=new List<string>();if(_s.B("PingShowNumber",true))parts.Add(r.Number>0?r.Number.ToString("00"):"--");
            if(_s.B("PingShowGrade",true))parts.Add(r.MustHit?"S!":r.Grade);
            if(_s.B("PingShowDistance",true))parts.Add(Range(r.DistanceMeters));if(_s.B("PingShowDiameter",false))parts.Add(FormatSize(r.DiameterMeters));
            if(_s.B("PingShowTopOre",true)&&!string.IsNullOrWhiteSpace(r.TopOre))parts.Add(Code(r.TopOre));
            if(_s.B("PingShowOrePercent",true)&&!string.IsNullOrWhiteSpace(r.TopOre))parts.Add(r.TopOrePercent.ToString("0.00")+"%");
            string label=string.Join("  ",parts);var detail=new List<string>();if(_s.B("PingShowAmount",true)&&r.EstimatedVolume>0)detail.Add("~"+Amount(r.EstimatedVolume)+" m3");
            if(_s.B("PingShowScanStatus",true)&&!string.IsNullOrEmpty(r.ScanStatus))detail.Add(r.ScanStatus);
            return label+(detail.Count>0?(label.Length>0?"\n":"")+string.Join("  ",detail):"");
        }
        private static void DrawMarker(Graphics g,PointF p,Color c,float s,bool selected,bool pinned)
        {
            float st=Math.Max(1.2f,s*0.12f);using(var shadow=new Pen(Color.FromArgb(180,0,0,0),st+2))using(var pen=new Pen(Color.FromArgb(245,c),st))
            {
                float a=s*0.85f,b=s*0.34f;Action<Pen> draw=pp=>{g.DrawLine(pp,p.X-a,p.Y-b,p.X-b,p.Y-b);g.DrawLine(pp,p.X-b,p.Y-b,p.X-b,p.Y-a);g.DrawLine(pp,p.X+a,p.Y-b,p.X+b,p.Y-b);g.DrawLine(pp,p.X+b,p.Y-b,p.X+b,p.Y-a);g.DrawLine(pp,p.X-a,p.Y+b,p.X-b,p.Y+b);g.DrawLine(pp,p.X-b,p.Y+b,p.X-b,p.Y+a);g.DrawLine(pp,p.X+a,p.Y+b,p.X+b,p.Y+b);g.DrawLine(pp,p.X+b,p.Y+b,p.X+b,p.Y+a);};draw(shadow);draw(pen);
                if(pinned){using(var bsh=new SolidBrush(Color.FromArgb(245,c)))g.FillEllipse(bsh,p.X-2,p.Y-2,4,4);}if(selected)g.DrawEllipse(pen,p.X-s*1.05f,p.Y-s*1.05f,s*2.1f,s*2.1f);
            }
        }
        private static void DrawArrow(Graphics g,PointF p,PointF dir,Color c,float s)
        {
            double ang=Math.Atan2(dir.Y,dir.X);PointF F(float a,float r){return new PointF(p.X+(float)Math.Cos(ang+a)*r,p.Y+(float)Math.Sin(ang+a)*r);}var pts=new[]{F(0,s),F(2.55f,s*0.70f),F(-2.55f,s*0.70f)};using(var b=new SolidBrush(Color.FromArgb(225,c)))using(var pen=new Pen(Color.FromArgb(245,c),Math.Max(1.2f,s*0.10f))){g.FillPolygon(b,pts);g.DrawPolygon(pen,pts);}
        }

        private void DrawList(Graphics g,int w,int h,OreOverlayFrame f)
        {
            var rows=(f.Roids??new List<OreOverlayRoid>()).Where(x=>x!=null&&x.ListEligible).Take(Math.Max(1,Math.Min(20,_s.I("ListRows",8)))).ToList();string layout=_s.Get("HudLayout","Prospector");float baseW=layout.Equals("Wide",StringComparison.OrdinalIgnoreCase)?860:layout.Equals("Tactical",StringComparison.OrdinalIgnoreCase)?580:layout.Equals("Compact",StringComparison.OrdinalIgnoreCase)?470:700;
            float scale=(float)OreGeometry.Safe(_s.D("PanelScale",1),0.5,2,1),width=baseW*scale*(float)OreGeometry.Safe(_s.D("PanelWidthScale",1),0.65,2.5,1);float textScale=(float)OreGeometry.Safe(_s.D("TextScale",1),0.5,2,1),rowH=24f*(float)OreGeometry.Safe(_s.D("RowScale",1),0.65,1.75,1)*textScale;float headerH=_s.B("ShowHeader",true)?50f*scale:10f;float colsH=_s.B("ShowColumnHeader",true)?24f*scale:0;float statusH=_s.B("ShowStatusBar",true)?25f*scale:0;float height=headerH+colsH+rows.Count*rowH+statusH+14f;
            var bounds=OreGeometry.Bounds(_s,w,h,rows.Count,f.Layout);var rect=new RectangleF((float)bounds.X,(float)bounds.Y,(float)bounds.Width,(float)bounds.Height);
            var listClip=g.Save();g.SetClip(rect,CombineMode.Intersect);try {
            Color panel=C(_s.Get("HudPanelColor","#120D0E"),Color.FromArgb(18,13,14)),border=C(_s.Get("HudBorderColor","#5A2024"),Color.DarkRed),accent=C(_s.Get("HudAccentColor","#D83B3B"),Color.Red),text=C(_s.Get("HudTextColor","#E8EAED"),Color.White),dim=C(_s.Get("HudDimColor","#8C9299"),Color.Gray);
            DrawFrame(g,rect,panel,border,accent,_s.Get("FrameStyle","Hex Command"));float y=rect.Top+8;
            if(_s.B("ShowHeader",true))
            {
                using(var f1=new Font("Bahnschrift SemiCondensed",Math.Max(10,16*textScale),FontStyle.Bold,GraphicsUnit.Pixel))using(var f2=new Font("Consolas",Math.Max(8,10*textScale),FontStyle.Regular,GraphicsUnit.Pixel))using(var tb=new SolidBrush(Color.FromArgb(Math.Max(0,Math.Min(255,_s.I("TextOpacity",245))),text)))using(var db=new SolidBrush(Color.FromArgb(220,dim)))
                {g.DrawString("ZEO // PROSPECTOR",f1,tb,rect.Left+18,y+3);g.DrawString((_s.Get("ActivePreset","Balanced")+"  //  "+_s.Get("SearchSort","Most estimated ore").ToUpperInvariant()).ToUpperInvariant(),f2,db,rect.Left+18,y+25);var best=rows.FirstOrDefault();string br=best==null?"BEST --":"BEST "+best.Number.ToString("00")+" / "+(best.MustHit?"S!":best.Grade);var sz=g.MeasureString(br,f1);using(var bestBrush=new SolidBrush(best==null?dim:C(best.GradeColor,text)))g.DrawString(br,f1,bestBrush,rect.Right-sz.Width-18,y+3);}
                y+=headerH;
            }
            var cols=BuildColumns();float inner=rect.Width-32,total=cols.Sum(c=>c.W);float fit=total>inner?inner/total:1f;using(var font=new Font("Consolas",Math.Max(8,11*textScale),FontStyle.Regular,GraphicsUnit.Pixel))using(var headFont=new Font("Bahnschrift SemiCondensed",Math.Max(8,9.5f*textScale),FontStyle.Bold,GraphicsUnit.Pixel))
            {
                if(_s.B("ShowColumnHeader",true)){float x=rect.Left+16;using(var db=new SolidBrush(Color.FromArgb(210,dim)))foreach(var c in cols){DrawCell(g,c.Label,headFont,db,new RectangleF(x,y+3,Math.Max(1,c.W*fit-3),colsH));x+=c.W*fit;}using(var p=new Pen(Color.FromArgb(110,border),1))g.DrawLine(p,rect.Left+14,y+colsH-2,rect.Right-14,y+colsH-2);y+=colsH;}
                for(int i=0;i<rows.Count;i++)
                {
                    var r=rows[i];var rr=new RectangleF(rect.Left+10,y+i*rowH,rect.Width-20,rowH);if(r.Selected||r.Pinned){using(var b=new SolidBrush(Color.FromArgb(r.Selected?45:25,r.Selected?C(_s.Get("SelectedColor","#FFE16B"),Color.Gold):accent)))g.FillRectangle(b,rr);}float x=rect.Left+16;Color rc=C(r.GradeColor,text);using(var rb=new SolidBrush(Color.FromArgb(Math.Max(0,Math.Min(255,_s.I("TextOpacity",245))),rc)))foreach(var c in cols){DrawCell(g,ColumnValue(c.Key,r),font,rb,new RectangleF(x,rr.Top+4,Math.Max(1,c.W*fit-3),rowH-4));x+=c.W*fit;}
                }
            }
            y+=rows.Count*rowH;if(_s.B("ShowStatusBar",true)){using(var p=new Pen(Color.FromArgb(100,border),1))g.DrawLine(p,rect.Left+14,y+2,rect.Right-14,y+2);using(var f3=new Font("Consolas",Math.Max(8,9.5f*textScale),FontStyle.Regular,GraphicsUnit.Pixel))using(var db=new SolidBrush(Color.FromArgb(215,dim))){string s=string.IsNullOrEmpty(f.SearchMessage)?"VISIBLE "+f.VisibleCount+"  READ "+f.ReadyCount+"  PENDING "+f.PendingCount:f.SearchMessage;g.DrawString(s,f3,db,rect.Left+16,y+7);}}
            }finally{g.Restore(listClip);}
        }
        private sealed class Col{public string Key,Label;public float W;public Col(string k,string l,float w){Key=k;Label=l;W=w;}}
        private List<Col> BuildColumns(){var c=new List<Col>();if(_s.B("ShowColNumber",true))c.Add(new Col("n","#",38));if(_s.B("ShowColPin",true))c.Add(new Col("p","P",28));if(_s.B("ShowColGrade",true))c.Add(new Col("g","G",40));if(_s.B("ShowColDistance",true))c.Add(new Col("d","DIST",76));if(_s.B("ShowColDiameter",true))c.Add(new Col("z","DIA",68));if(_s.B("ShowColTopOre",true))c.Add(new Col("o","ORE",82));if(_s.B("ShowColOrePercent",true))c.Add(new Col("q","%",70));if(_s.B("ShowColAmount",true))c.Add(new Col("a","EST. m3",98));if(_s.B("ShowColScanStatus",true))c.Add(new Col("c","SCAN",88));if(_s.B("ShowColSecondOre",true))c.Add(new Col("s","SECOND",120));if(_s.B("ShowColQuality",false))c.Add(new Col("v","QUAL",65));if(_s.B("ShowColStatus",false))c.Add(new Col("t","STATUS",90));return c;}
        private static string ColumnValue(string k,OreOverlayRoid r){if(k=="a")return r.EstimatedVolume>0?"~"+Amount(r.EstimatedVolume):"--";if(k=="c")return r.ScanStatus=="VERIFIED SCAN"?"VERIFIED":r.ScanStatus??"--";if(k=="n")return r.Number>0?r.Number.ToString("00"):"--";if(k=="p")return r.Pinned?"*":"";if(k=="g")return r.MustHit?"S!":r.Grade;if(k=="d")return Range(r.DistanceMeters);if(k=="z")return FormatSize(r.DiameterMeters);if(k=="o")return string.IsNullOrWhiteSpace(r.TopOre)?"--":Code(r.TopOre);if(k=="q")return string.IsNullOrWhiteSpace(r.TopOre)?"--":r.TopOrePercent.ToString("0.00")+"%";if(k=="s")return string.IsNullOrWhiteSpace(r.SecondOre)?"--":Code(r.SecondOre)+" "+r.SecondOrePercent.ToString("0.00")+"%";if(k=="v")return r.Quality.ToString("0.0");if(k=="t")return r.State;return"";}

        private void DrawFrame(Graphics g,RectangleF r,Color panel,Color border,Color accent,string style)
        {
            int pa=Math.Max(0,Math.Min(255,_s.I("PanelOpacity",190))),fa=Math.Max(0,Math.Min(255,_s.I("FrameOpacity",235)));using(var fill=new SolidBrush(Color.FromArgb(pa,panel)))using(var bp=new Pen(Color.FromArgb(fa,border),1.4f))using(var ap=new Pen(Color.FromArgb(fa,accent),2.0f))
            {
                if(style.Equals("Hex Command",StringComparison.OrdinalIgnoreCase)){float cut=18;var pts=new[]{new PointF(r.Left+cut,r.Top),new PointF(r.Right-cut,r.Top),new PointF(r.Right,r.Top+cut),new PointF(r.Right,r.Bottom-cut),new PointF(r.Right-cut,r.Bottom),new PointF(r.Left+cut,r.Bottom),new PointF(r.Left,r.Bottom-cut),new PointF(r.Left,r.Top+cut)};g.FillPolygon(fill,pts);g.DrawPolygon(bp,pts);g.DrawLine(ap,r.Left+34,r.Top,r.Left+118,r.Top);g.DrawLine(ap,r.Right-118,r.Bottom,r.Right-34,r.Bottom);}
                else if(style.Equals("Chevron",StringComparison.OrdinalIgnoreCase)){float c=24;var pts=new[]{new PointF(r.Left+c,r.Top),new PointF(r.Right-c,r.Top),new PointF(r.Right,r.Top+r.Height/2),new PointF(r.Right-c,r.Bottom),new PointF(r.Left+c,r.Bottom),new PointF(r.Left,r.Top+r.Height/2)};g.FillPolygon(fill,pts);g.DrawPolygon(bp,pts);g.DrawLine(ap,r.Left+c,r.Top,r.Left+115,r.Top);}
                else if(style.Equals("Razor",StringComparison.OrdinalIgnoreCase)){float c=26;var pts=new[]{new PointF(r.Left+c,r.Top),new PointF(r.Right,r.Top),new PointF(r.Right-c,r.Bottom),new PointF(r.Left,r.Bottom),new PointF(r.Left+c*0.4f,r.Top+r.Height*0.55f)};g.FillPolygon(fill,pts);g.DrawPolygon(bp,pts);g.DrawLine(ap,r.Right-150,r.Top,r.Right,r.Top);g.DrawLine(ap,r.Left,r.Bottom,r.Left+130,r.Bottom);}
                else{g.FillRectangle(fill,r);if(!style.Equals("Minimal",StringComparison.OrdinalIgnoreCase))g.DrawRectangle(bp,r.X,r.Y,r.Width,r.Height);if(style.Equals("Split Wing",StringComparison.OrdinalIgnoreCase)){g.DrawLine(ap,r.Left,r.Top,r.Left+120,r.Top);g.DrawLine(ap,r.Right-120,r.Top,r.Right,r.Top);g.DrawLine(ap,r.Left,r.Bottom,r.Left+90,r.Bottom);g.DrawLine(ap,r.Right-90,r.Bottom,r.Right,r.Bottom);}else if(style.Equals("War Room",StringComparison.OrdinalIgnoreCase)){g.DrawLine(ap,r.Left+18,r.Top,r.Left+150,r.Top);g.DrawLine(ap,r.Right-130,r.Bottom,r.Right-18,r.Bottom);}}
            }
        }

        private void HideOverlay(){try{if(IsHandleCreated)NativeMethods.ShowWindow(Handle,NativeMethods.SW_HIDE);}catch{}}
        private static bool TryFrameRect(OreOverlayFrame f,out NativeMethods.RECT r){r=new NativeMethods.RECT();if(f==null||!f.GameWindowValid||f.GameWidth<200||f.GameHeight<200)return false;r.Left=f.GameLeft;r.Top=f.GameTop;r.Right=f.GameLeft+f.GameWidth;r.Bottom=f.GameTop+f.GameHeight;return true;}
        private void AttachOwner(){try{if(IsHandleCreated&&_gameHwnd!=IntPtr.Zero)NativeMethods.SetWindowLongPtr(Handle,NativeMethods.GWLP_HWNDPARENT,_gameHwnd);}catch{}}
        private void EnsureTopmost(){try{if(IsHandleCreated)NativeMethods.SetWindowPos(Handle,NativeMethods.HWND_TOPMOST,0,0,0,0,NativeMethods.SWP_NOMOVE|NativeMethods.SWP_NOSIZE|NativeMethods.SWP_NOACTIVATE);}catch{}}
        private bool FindGameWindow(){try{if(_gameHwnd!=IntPtr.Zero&&NativeMethods.IsWindowVisible(_gameHwnd))return true;foreach(var p in Process.GetProcessesByName("SpaceEngineers")){IntPtr h=p.MainWindowHandle;if(h==IntPtr.Zero||!NativeMethods.IsWindowVisible(h))h=FindVisibleWindow(p.Id);if(h!=IntPtr.Zero){_gameHwnd=h;_gamePid=p.Id;return true;}}}catch{}_gameHwnd=IntPtr.Zero;_gamePid=0;return false;}
        private static IntPtr FindVisibleWindow(int pid){IntPtr found=IntPtr.Zero;try{NativeMethods.EnumWindows((h,l)=>{if(!NativeMethods.IsWindowVisible(h))return true;uint p;NativeMethods.GetWindowThreadProcessId(h,out p);if(p==(uint)pid){found=h;return false;}return true;},IntPtr.Zero);}catch{}return found;}
        private bool IsMenuForeground(){try{if(_menu==null||!_menu.Visible||!_menu.IsHandleCreated)return false;IntPtr fg=NativeMethods.GetForegroundWindow();if(fg==_menu.Handle)return true;uint pid;NativeMethods.GetWindowThreadProcessId(fg,out pid);return pid==(uint)Process.GetCurrentProcess().Id;}catch{return false;}}
        private bool IsGameForeground(){try{IntPtr fg=NativeMethods.GetForegroundWindow();if(fg==IntPtr.Zero)return false;if(_gameHwnd!=IntPtr.Zero&&fg==_gameHwnd)return true;uint pid;NativeMethods.GetWindowThreadProcessId(fg,out pid);if(_gamePid!=0&&pid==(uint)_gamePid)return true;if(pid!=0)using(var p=Process.GetProcessById((int)pid))return p.ProcessName.Equals("SpaceEngineers",StringComparison.OrdinalIgnoreCase);}catch{}return false;}
        private static bool TryGetGameRect(IntPtr h,out NativeMethods.RECT r){r=new NativeMethods.RECT();try{NativeMethods.RECT c;var o=new NativeMethods.POINT(0,0);if(NativeMethods.GetClientRect(h,out c)&&c.Right>c.Left&&c.Bottom>c.Top&&NativeMethods.ClientToScreen(h,ref o)){r.Left=o.X;r.Top=o.Y;r.Right=o.X+c.Right-c.Left;r.Bottom=o.Y+c.Bottom-c.Top;return true;}}catch{}try{return NativeMethods.GetWindowRect(h,out r);}catch{return false;}}
        private void Present(Bitmap bitmap, int left, int top)
        {
            _presented=false;
            IntPtr screenDc = IntPtr.Zero;
            IntPtr memDc = IntPtr.Zero;
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr dibBits = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;
            BitmapData locked = null;
            try
            {
                screenDc = NativeMethods.GetDC(IntPtr.Zero);
                if (screenDc == IntPtr.Zero) { HideOverlay(); return; }

                memDc = NativeMethods.CreateCompatibleDC(screenDc);
                if (memDc == IntPtr.Zero) { HideOverlay(); return; }

                var bmi = new NativeMethods.BITMAPINFO();
                bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(NativeMethods.BITMAPINFOHEADER));
                bmi.bmiHeader.biWidth = bitmap.Width;
                // Negative height creates a top-down 32-bit DIB, matching GDI+ row order.
                bmi.bmiHeader.biHeight = -bitmap.Height;
                bmi.bmiHeader.biPlanes = 1;
                bmi.bmiHeader.biBitCount = 32;
                bmi.bmiHeader.biCompression = NativeMethods.BI_RGB;
                bmi.bmiHeader.biSizeImage = (uint)(bitmap.Width * bitmap.Height * 4);

                hBitmap = NativeMethods.CreateDIBSection(screenDc, ref bmi, NativeMethods.DIB_RGB_COLORS, out dibBits, IntPtr.Zero, 0);
                if (hBitmap == IntPtr.Zero || dibBits == IntPtr.Zero) { HideOverlay(); return; }

                // Do not use Bitmap.GetHbitmap here. GetHbitmap can discard/normalize
                // the alpha channel on some Windows/GDI paths. Copy the premultiplied
                // BGRA bytes directly into a 32-bit DIB section instead.
                locked = bitmap.LockBits(
                    new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format32bppPArgb);

                int rowBytes = bitmap.Width * 4;
                int srcStride = locked.Stride;
                byte[] row = new byte[rowBytes];
                for (int y = 0; y < bitmap.Height; y++)
                {
                    int srcOffset = srcStride >= 0 ? y * srcStride : (bitmap.Height - 1 - y) * (-srcStride);
                    IntPtr srcRow = IntPtr.Add(locked.Scan0, srcOffset);
                    IntPtr dstRow = IntPtr.Add(dibBits, y * rowBytes);
                    Marshal.Copy(srcRow, row, 0, rowBytes);
                    Marshal.Copy(row, 0, dstRow, rowBytes);
                }
                bitmap.UnlockBits(locked);
                locked = null;

                oldBitmap = NativeMethods.SelectObject(memDc, hBitmap);
                var dst = new NativeMethods.POINT(left, top);
                var src = new NativeMethods.POINT(0, 0);
                var size = new NativeMethods.SIZE(bitmap.Width, bitmap.Height);
                var blend = new NativeMethods.BLENDFUNCTION
                {
                    BlendOp = NativeMethods.AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = NativeMethods.AC_SRC_ALPHA
                };

                bool presented = NativeMethods.UpdateLayeredWindow(
                    Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, NativeMethods.ULW_ALPHA);

                if (presented) {_presented=true;NativeMethods.ShowWindow(Handle, NativeMethods.SW_SHOWNA);}
                else HideOverlay();
            }
            catch
            {
                HideOverlay();
            }
            finally
            {
                if (locked != null) { try { bitmap.UnlockBits(locked); } catch { } }
                if (oldBitmap != IntPtr.Zero && memDc != IntPtr.Zero) NativeMethods.SelectObject(memDc, oldBitmap);
                if (hBitmap != IntPtr.Zero) NativeMethods.DeleteObject(hBitmap);
                if (memDc != IntPtr.Zero) NativeMethods.DeleteDC(memDc);
                if (screenDc != IntPtr.Zero) NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        private static string Amount(double volume){return volume>=1000000000?(volume/1000000000).ToString("0.#")+"B":volume>=1000000?(volume/1000000).ToString("0.#")+"M":volume>=1000?(volume/1000).ToString("0.#")+"k":volume.ToString("0");}
        private static void DrawCell(Graphics g,string text,Font font,Brush brush,RectangleF rect){using(var format=new StringFormat{FormatFlags=StringFormatFlags.NoWrap,Trimming=StringTrimming.EllipsisCharacter})g.DrawString(text??"",font,brush,rect,format);}
        private static Color C(string hex,Color d){try{if(!string.IsNullOrWhiteSpace(hex)&&hex.Length==7&&hex[0]=='#'){int v=Convert.ToInt32(hex.Substring(1),16);return Color.FromArgb((v>>16)&255,(v>>8)&255,v&255);}}catch{}return d;}
        private static string Range(double m){if(m>=10000)return(m/1000.0).ToString("0")+"k";if(m>=1000)return(m/1000.0).ToString("0.0")+"k";return m.ToString("0")+"m";}
        private static string FormatSize(double m){if(m<=0)return"--";if(m>=1000)return(m/1000.0).ToString("0.0")+"k";return m.ToString("0")+"m";}
        private static string Code(string ore){if(string.IsNullOrWhiteSpace(ore))return"--";if(ore.Equals("Uranium",StringComparison.OrdinalIgnoreCase))return"U";if(ore.Equals("Tungsten",StringComparison.OrdinalIgnoreCase))return"W";if(ore.Equals("Titanium",StringComparison.OrdinalIgnoreCase))return"Ti";if(ore.Equals("Platinum",StringComparison.OrdinalIgnoreCase))return"Pt";if(ore.Equals("Gold",StringComparison.OrdinalIgnoreCase))return"Au";if(ore.Equals("Silver",StringComparison.OrdinalIgnoreCase))return"Ag";if(ore.Equals("Copper",StringComparison.OrdinalIgnoreCase))return"Cu";if(ore.Equals("Lead",StringComparison.OrdinalIgnoreCase))return"Pb";if(ore.Equals("Magnesium",StringComparison.OrdinalIgnoreCase))return"Mg";if(ore.Equals("Nickel",StringComparison.OrdinalIgnoreCase))return"Ni";if(ore.Equals("Cobalt",StringComparison.OrdinalIgnoreCase))return"Co";if(ore.Equals("Silicon",StringComparison.OrdinalIgnoreCase))return"Si";if(ore.Equals("Iron",StringComparison.OrdinalIgnoreCase))return"Fe";return ore.Length>4?ore.Substring(0,4):ore;}

        protected override void OnFormClosed(FormClosedEventArgs e){_running=false;try{_render.Stop();_settingsTimer.Stop();_render.Dispose();_settingsTimer.Dispose();if(_menu!=null)_menu.Dispose();}catch{}try{_udp.Close();}catch{}try{if(_rx!=null&&_rx.IsAlive)_rx.Join(200);}catch{}base.OnFormClosed(e);}
    }
}
