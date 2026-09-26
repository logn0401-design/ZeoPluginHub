using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Windows.Forms;

namespace ZeoPdcOverlay
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            int commandPort=0, ownerPid=0;
            for(int i=0;args!=null&&i<args.Length;i++)
            {
                if(args[i].Equals("--command-port",StringComparison.OrdinalIgnoreCase)&&i+1<args.Length) int.TryParse(args[++i],out commandPort);
                else if(args[i].Equals("--owner-pid",StringComparison.OrdinalIgnoreCase)&&i+1<args.Length) int.TryParse(args[++i],out ownerPid);
            }
            if(commandPort<=1024||commandPort>=65535) return;
            bool created;
            using(var single=new Mutex(true,"Local\\ZeoPdcOverlay_"+ownerPid,out created))
            {
                if(!created)return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new OverlayContext(commandPort,ownerPid));
            }
        }
    }

    internal sealed class OverlayContext:ApplicationContext
    {
        private readonly SnapshotReceiver rx=new SnapshotReceiver();
        private readonly HudForm hud=new HudForm();
        private MenuForm menu;
        private readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
        private readonly int commandPort, ownerPid;
        public OverlayContext(int port,int pid)
        {
            commandPort=port;ownerPid=pid;rx.Start();Send(new PdcCommand{Type="HELLO",Value=rx.LocalPort});
            // Keep the 660x900 settings window completely nonexistent until the user actually opens it.
            // This prevents Windows/capture stacks from exposing an unpainted menu HWND as a white rectangle.
            hud.EnsureHidden();
            timer.Interval=50;timer.Tick+=Tick;timer.Start();
        }
        private void Tick(object sender,EventArgs e)
        {
            if(ownerPid>0){try{var p=Process.GetProcessById(ownerPid);if(p.HasExited){ExitThread();return;}}catch{ExitThread();return;}}
            double receiptAge; PdcSnapshot s=rx.Read(out receiptAge);
            if(!HudVisibility.Fresh(s,ownerPid,receiptAge,DateTime.UtcNow.Ticks)) {
                hud.EnsureHidden();if(menu!=null && menu.Visible)menu.Hide();return;
            }
            bool stream=s.Config==null||s.Config.StreamerMode;
            hud.SetStreamerMode(stream);
            if(menu!=null) menu.SetStreamerMode(stream);

            IntPtr fg=Native.GetForegroundWindow();
            bool menuOwn=menu!=null&&menu.Visible&&menu.IsHandleCreated&&fg==menu.Handle;
            bool hudOwn=hud.IsHandleCreated&&fg==hud.Handle;
            bool game=s.GameHwnd!=0&&fg.ToInt64()==s.GameHwnd;
            bool allowed=game||menuOwn||hudOwn;

            if(s.MenuVisible&&allowed)
            {
                MenuForm m=EnsureMenu();
                m.SetStreamerMode(stream);
                if(!m.Visible)
                {
                    // Fully build and paint the dark ZeoCore shell BEFORE the first Show().
                    m.Prepare(s);
                    m.CenterOnGame(s);
                    m.Show();
                }
                else m.Apply(s);
                m.BringToFront();
            }
            else if(menu!=null&&menu.Visible) menu.Hide();

            if(HudVisibility.Show(s,allowed))hud.Render(s,new Rectangle(s.ClientX,s.ClientY,s.ClientW,s.ClientH));else hud.EnsureHidden();
        }
        private MenuForm EnsureMenu()
        {
            if(menu!=null)return menu;
            menu=new MenuForm();
            menu.Command+=Send;
            menu.FormClosing+=(sender,e)=>{e.Cancel=true;menu.Hide();Send(new PdcCommand{Type="MENU_CLOSE"});};
            return menu;
        }
        private void Send(PdcCommand c){try{byte[] b=JsonIo.ToBytes(c);using(var u=new UdpClient())u.Send(b,b.Length,new IPEndPoint(IPAddress.Loopback,commandPort));}catch{}}
        protected override void ExitThreadCore(){try{rx.Dispose();}catch{}try{timer.Dispose();}catch{}base.ExitThreadCore();}
    }

    internal sealed class SnapshotReceiver:IDisposable
    {
        private UdpClient client;private Thread thread;private volatile bool run;private PdcSnapshot latest;private long latestSeq, receivedStamp;private readonly object gate=new object();
        public PdcSnapshot Read(out double age){lock(gate){age=latest==null?double.PositiveInfinity:(Stopwatch.GetTimestamp()-receivedStamp)/(double)Stopwatch.Frequency;return latest;}}public int LocalPort{get;private set;}
        public void Start(){client=new UdpClient(new IPEndPoint(IPAddress.Loopback,0));LocalPort=((IPEndPoint)client.Client.LocalEndPoint).Port;run=true;thread=new Thread(Loop){IsBackground=true,Name="ZeoPdcOverlayRx"};thread.Start();}
        private void Loop(){var ep=new IPEndPoint(IPAddress.Any,0);while(run){try{byte[] b=client.Receive(ref ep);var s=JsonIo.FromBytes<PdcSnapshot>(b);if(s!=null)lock(gate){if(s.SnapshotSeq>latestSeq){latest=s;latestSeq=s.SnapshotSeq;receivedStamp=Stopwatch.GetTimestamp();}}}catch(SocketException){if(run)Thread.Sleep(25);}catch{Thread.Sleep(50);}}}
        public void Dispose(){run=false;try{client.Close();}catch{}}
    }

    internal static class Native
    {
        public const int WS_EX_LAYERED=0x00080000,WS_EX_TRANSPARENT=0x20,WS_EX_TOOLWINDOW=0x80,WS_EX_NOACTIVATE=0x08000000,ULW_ALPHA=2,SW_HIDE=0,SW_SHOWNA=8;
        public const uint WDA_NONE=0,WDA_EXCLUDEFROMCAPTURE=0x11,BI_RGB=0,DIB_RGB_COLORS=0;public const byte AC_SRC_OVER=0,AC_SRC_ALPHA=1;
        [StructLayout(LayoutKind.Sequential)]public struct POINT{public int X,Y;public POINT(int x,int y){X=x;Y=y;}}
        [StructLayout(LayoutKind.Sequential)]public struct SIZE{public int cx,cy;public SIZE(int x,int y){cx=x;cy=y;}}
        [StructLayout(LayoutKind.Sequential,Pack=1)]public struct BLENDFUNCTION{public byte BlendOp,BlendFlags,SourceConstantAlpha,AlphaFormat;}
        [StructLayout(LayoutKind.Sequential)]public struct BITMAPINFOHEADER{public uint biSize;public int biWidth,biHeight;public ushort biPlanes,biBitCount;public uint biCompression,biSizeImage;public int biXPelsPerMeter,biYPelsPerMeter;public uint biClrUsed,biClrImportant;}
        [StructLayout(LayoutKind.Sequential)]public struct BITMAPINFO{public BITMAPINFOHEADER bmiHeader;public uint bmiColors;}
        [DllImport("user32.dll",SetLastError=true)]public static extern bool SetWindowDisplayAffinity(IntPtr h,uint a);
        [DllImport("user32.dll")]public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll",ExactSpelling=true,SetLastError=true)]public static extern bool UpdateLayeredWindow(IntPtr hwnd,IntPtr hdcDst,ref POINT dst,ref SIZE size,IntPtr hdcSrc,ref POINT src,int key,ref BLENDFUNCTION blend,int flags);
        [DllImport("user32.dll")]public static extern IntPtr GetDC(IntPtr h);[DllImport("user32.dll")]public static extern int ReleaseDC(IntPtr h,IntPtr d);[DllImport("user32.dll")]public static extern bool ShowWindow(IntPtr h,int c);
        [DllImport("gdi32.dll")]public static extern IntPtr CreateCompatibleDC(IntPtr h);[DllImport("gdi32.dll",SetLastError=true)]public static extern IntPtr CreateDIBSection(IntPtr h,ref BITMAPINFO i,uint usage,out IntPtr bits,IntPtr section,uint offset);[DllImport("gdi32.dll")]public static extern bool DeleteDC(IntPtr h);[DllImport("gdi32.dll")]public static extern IntPtr SelectObject(IntPtr h,IntPtr o);[DllImport("gdi32.dll")]public static extern bool DeleteObject(IntPtr o);
    }

    internal sealed class ThemePalette
    {
        public Color Text, Secondary, Back, Border, Accent, Good, Warning, Danger;
        public Color MenuBack, MenuPanel, MenuText, MenuAccent;

        public static ThemePalette From(PdcConfig c)
        {
            string n = (c.Theme ?? "WAR ROOM").Trim().ToUpperInvariant();
            if (n == "GRAPHITE")
                return P("#D7D9DC", "#A9ADB2", "#2B2E32", "#666C72", "#D7A04B", "#8FD3B0", "#E6D28A", "#E07872", "#202327", "#2A2E33", "#D7D9DC", "#AEB5BC");
            if (n == "MONOCHROME")
                return P("#D8D8D8", "#A5A5A5", "#272727", "#686868", "#D8D8D8", "#C6C6C6", "#FFFFFF", "#F0F0F0", "#1F1F1F", "#292929", "#D8D8D8", "#A8A8A8");
            if (n == "AMBER")
                return P("#E3D6B0", "#B5A77E", "#2D2A24", "#746A50", "#D8BE76", "#A8D0A2", "#F1D88C", "#E48168", "#211F1A", "#2B2923", "#E3D6B0", "#C1AE75");
            if (n == "HIGH CONTRAST")
                return P("#E8ECF1", "#B8BEC5", "#101419", "#7B858E", "#FFB84A", "#4DE1FF", "#FFE16B", "#FF5A5F", "#101419", "#1A2026", "#E8ECF1", "#F2C94C");
            if (n == "CUSTOM")
                return P("#E8ECF1", "#B8BEC5", "#101419", "#7B858E", "#FFB84A", "#4DE1FF", "#FFE16B", "#FF5A5F", "#101419", "#1A2026", "#E8ECF1", "#F2C94C");
            // ZeoCore V1.2d WAR ROOM fallback palette; live settings override this when available.
            return P("#F1F3F5", "#9CA3AB", "#090B0E", "#414850", "#C8CDD2", "#B8D7E8", "#FFFFFF", "#F04444", "#07090B", "#111419", "#F1F3F5", "#D52B2B");
        }

        private static ThemePalette P(string text, string secondary, string back, string border, string accent, string good, string warning, string danger, string menuBack, string menuPanel, string menuText, string menuAccent)
        {
            return new ThemePalette
            {
                Text = C(text), Secondary = C(secondary), Back = C(back), Border = C(border), Accent = C(accent), Good = C(good), Warning = C(warning), Danger = C(danger),
                MenuBack = C(menuBack), MenuPanel = C(menuPanel), MenuText = C(menuText), MenuAccent = C(menuAccent)
            };
        }
        private static Color C(string s) { try { return ColorTranslator.FromHtml(s); } catch { return Color.White; } }
    }


    internal sealed class ZeoCoreAppearance
    {
        public int FrameStyle = -1;
        public int FontStyle = -1;
        public int PanelOpacity = -1;
        public double PanelPaddingScale = double.NaN;
        public double BorderWidth = double.NaN;
        public string HudTextColor;
        public string HudSecondaryColor;
        public string HudPanelColor;
        public string HudBorderColor;
        public string SpectrumColor;
        public string FriendlyColor;
        public string FocusColor;
        public string HostileColor;
        public string MenuBackgroundColor;
        public string MenuPanelColor;
        public string MenuTextColor;
        public string MenuAccentColor;

        private static readonly object Gate = new object();
        private static DateTime lastCheckUtc = DateTime.MinValue;
        private static DateTime lastWriteUtc = DateTime.MinValue;
        private static ZeoCoreAppearance cached;

        public static ZeoCoreAppearance Get()
        {
            lock (Gate)
            {
                DateTime now = DateTime.UtcNow;
                if ((now - lastCheckUtc).TotalMilliseconds < 900) return cached;
                lastCheckUtc = now;
                try
                {
                    string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "Pulsar", "ZeoCore", "hud-settings.json");
                    if (!File.Exists(path)) { cached = null; lastWriteUtc = DateTime.MinValue; return null; }
                    DateTime write = File.GetLastWriteTimeUtc(path);
                    if (cached != null && write == lastWriteUtc) return cached;
                    string json = File.ReadAllText(path);
                    var a = new ZeoCoreAppearance();
                    a.FrameStyle = JsonInt(json, "FrameStyle", -1);
                    a.FontStyle = JsonInt(json, "FontStyle", -1);
                    a.PanelOpacity = JsonInt(json, "PanelOpacity", -1);
                    a.PanelPaddingScale = JsonDouble(json, "PanelPaddingScale", double.NaN);
                    a.BorderWidth = JsonDouble(json, "BorderWidth", double.NaN);
                    a.HudTextColor = JsonString(json, "HudTextColor");
                    a.HudSecondaryColor = JsonString(json, "HudSecondaryColor");
                    a.HudPanelColor = JsonString(json, "HudPanelColor");
                    a.HudBorderColor = JsonString(json, "HudBorderColor");
                    a.SpectrumColor = JsonString(json, "SpectrumColor");
                    a.FriendlyColor = JsonString(json, "FriendlyColor");
                    a.FocusColor = JsonString(json, "FocusColor");
                    a.HostileColor = JsonString(json, "HostileColor");
                    a.MenuBackgroundColor = JsonString(json, "MenuBackgroundColor");
                    a.MenuPanelColor = JsonString(json, "MenuPanelColor");
                    a.MenuTextColor = JsonString(json, "MenuTextColor");
                    a.MenuAccentColor = JsonString(json, "MenuAccentColor");
                    cached = a;
                    lastWriteUtc = write;
                }
                catch { cached = null; }
                return cached;
            }
        }

        public ThemePalette ToPalette(ThemePalette fallback)
        {
            return new ThemePalette
            {
                Text = ColorOf(HudTextColor, fallback.Text),
                Secondary = ColorOf(HudSecondaryColor, fallback.Secondary),
                Back = ColorOf(HudPanelColor, fallback.Back),
                Border = ColorOf(HudBorderColor, fallback.Border),
                Accent = ColorOf(SpectrumColor, fallback.Accent),
                Good = ColorOf(FriendlyColor, fallback.Good),
                Warning = ColorOf(FocusColor, fallback.Warning),
                Danger = ColorOf(HostileColor, fallback.Danger),
                MenuBack = ColorOf(MenuBackgroundColor, fallback.MenuBack),
                MenuPanel = ColorOf(MenuPanelColor, fallback.MenuPanel),
                MenuText = ColorOf(MenuTextColor, fallback.MenuText),
                MenuAccent = ColorOf(MenuAccentColor, fallback.MenuAccent)
            };
        }

        private static Color ColorOf(string value, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            try { return ColorTranslator.FromHtml(value); } catch { return fallback; }
        }

        private static string JsonString(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return null;
            string needle = "\"" + key + "\"";
            int p = json.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (p < 0) return null;
            p = json.IndexOf(':', p + needle.Length);
            if (p < 0) return null;
            p++;
            while (p < json.Length && char.IsWhiteSpace(json[p])) p++;
            if (p >= json.Length || json[p] != '"') return null;
            p++;
            int start = p;
            while (p < json.Length)
            {
                if (json[p] == '"' && (p == start || json[p - 1] != '\\')) return json.Substring(start, p - start);
                p++;
            }
            return null;
        }

        private static int JsonInt(string json, string key, int fallback)
        {
            double d = JsonDouble(json, key, double.NaN);
            return double.IsNaN(d) ? fallback : (int)Math.Round(d);
        }

        private static double JsonDouble(string json, string key, double fallback)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return fallback;
            string needle = "\"" + key + "\"";
            int p = json.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (p < 0) return fallback;
            p = json.IndexOf(':', p + needle.Length);
            if (p < 0) return fallback;
            p++;
            while (p < json.Length && char.IsWhiteSpace(json[p])) p++;
            int start = p;
            while (p < json.Length && ("-+0123456789.eE".IndexOf(json[p]) >= 0)) p++;
            double v;
            return double.TryParse(json.Substring(start, p - start), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out v) ? v : fallback;
        }
    }


    internal sealed class HudForm:Form
    {
        private bool streamer, presented;
        public HudForm(){FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;BackColor=Color.Black;SetBounds(-32000,-32000,1,1);Visible=false;}
        protected override bool ShowWithoutActivation{get{return true;}}
        protected override CreateParams CreateParams{get{var cp=base.CreateParams;cp.ExStyle|=Native.WS_EX_LAYERED|Native.WS_EX_TRANSPARENT|Native.WS_EX_TOOLWINDOW|Native.WS_EX_NOACTIVATE;return cp;}}
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }
        public void SetStreamerMode(bool on){streamer=on;if(IsHandleCreated){try{Native.SetWindowDisplayAffinity(Handle,on?Native.WDA_EXCLUDEFROMCAPTURE:Native.WDA_NONE);}catch{}}}
        public void EnsureHidden(){if(IsHandleCreated && presented)Native.ShowWindow(Handle,Native.SW_HIDE);presented=false;}
        protected override void OnHandleDestroyed(EventArgs e){presented=false;base.OnHandleDestroyed(e);}
        public void Render(PdcSnapshot s,Rectangle game)
        {
            PdcConfig c=s.Config??new PdcConfig();ThemePalette p=ThemePalette.From(c);ZeoCoreAppearance z=c.FollowZeoCoreAppearance?ZeoCoreAppearance.Get():null;if(z!=null)p=z.ToPalette(p);
            int frame=z!=null&&z.FrameStyle>=0&&z.FrameStyle<=16?z.FrameStyle:FrameIndex(c.Frame);double bw=z!=null&&!double.IsNaN(z.BorderWidth)?z.BorderWidth:c.BorderWidth;int op=z!=null&&z.PanelOpacity>=0?z.PanelOpacity:c.BackingOpacity;
            var bounds=PdcHudGeometry.Bounds(game.Width,game.Height,c);
            Present(s,new Rectangle(game.Left+bounds.Left,game.Top+bounds.Top,bounds.Width,bounds.Height),c,p,frame,bw,op);
        }
        private void Present(PdcSnapshot s,Rectangle rect,PdcConfig c,ThemePalette p,int fs,double bw,int op)
        {
            using(var bmp=new Bitmap(rect.Width,rect.Height,PixelFormat.Format32bppPArgb))
            {
                using(var g=Graphics.FromImage(bmp))
                {
                    PaintPanel(g,s,rect,c,p,fs,bw,op);
                }
                Layer(bmp,rect);
            }
        }
        internal static void PaintPanel(Graphics g,PdcSnapshot s,Rectangle rect,PdcConfig c,ThemePalette p,int fs,double bw,int op)
        {
            g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            if(s.HudClipTopPixels>0){float top=Math.Max(0,s.ClientY+s.HudClipTopPixels-rect.Top);g.SetClip(new RectangleF(0,top,rect.Width,Math.Max(0,rect.Height-top)));}
            // Core-style independent frame axes with uniform text/content scaling.
            // A wider panel adds column space instead of stretching glyphs horizontally.
            float fit=(float)Math.Min(rect.Width/PdcHudGeometry.BaseWidth,rect.Height/PdcHudGeometry.BaseHeight(c));
            if(fit<=0)return;g.ScaleTransform(fit,fit);
            float w=rect.Width/fit,h=rect.Height/fit;
            DrawFrame(g,new RectangleF(2,2,w-4,h-4),c,p,fs,bw,op);
            var zeo=c.FollowZeoCoreAppearance?ZeoCoreAppearance.Get():null;
            int style=zeo!=null&&zeo.FontStyle>=0?zeo.FontStyle:c.HudFontStyle;
            string font=style==1?"Bahnschrift SemiCondensed":style==2?"Consolas":style==3?"Segoe UI Semibold":fs==3||fs==16?"Consolas":fs>=6?"Bahnschrift SemiCondensed":"Segoe UI";
            double age=s.SnapshotUtcTicks>0?Math.Max(0,(DateTime.UtcNow.Ticks-s.SnapshotUtcTicks)/(double)TimeSpan.TicksPerSecond):999;
            bool valid=age<=1.5&&s.CoreReady&&s.PdcCount>0&&!double.IsNaN(s.HottestHeatPercent)&&!double.IsInfinity(s.HottestHeatPercent);
            double padding=zeo!=null&&!double.IsNaN(zeo.PanelPaddingScale)?Math.Max(.5,Math.Min(2,zeo.PanelPaddingScale)):1;
            float text=(float)PdcHudGeometry.TextSize(c),left=18+(float)((padding-1)*6),right=w-left,inner=right-left;
            HudText(g,"ZEO // DEFENSE",p.Text,new RectangleF(left,12,inner,30),17*text,true,font);
            using(var line=new Pen(Color.FromArgb(85,p.Border)))g.DrawLine(line,left,46,right,46);
            float y=56;
            foreach(string row in PdcHudGeometry.Rows(c)) {
                float rh=(float)PdcHudGeometry.RowHeight(row,c);
                string label="",value="";Color color=p.Text;
                switch(row) {
                    case "Array": label="PDC ARRAY";value=s.PdcOnline+" / "+s.PdcCount+" ONLINE";break;
                    case "Heat":label="HOTTEST HEAT";value=valid?s.HottestHeatPercent.ToString("0.0")+"%":"--";color=valid?HeatGradient(s.HottestHeatPercent):p.Secondary;break;
                    case "AverageHeat":label="AVERAGE HEAT";value=valid?s.AverageHeatPercent.ToString("0.0")+"% (sampled)":"UNAVAILABLE";break;
                    case "Integrity":label="PDC INTEGRITY";value=s.PdcCount>0?s.AverageHpPercent.ToString("0")+"%":"--";break;
                    case "Inbound":label="INCOMING";value=s.ActiveInbound.ToString();color=s.ActiveInbound>0?p.Warning:p.Good;break;
                    case "Ammo":label="AMMUNITION";value=s.TotalAmmo.ToString("N0");break;
                    case "Decoys":label="DECOYS";value=s.DecoyStatus??"WAITING";break;
                    case "Manager":label="PDC MANAGER";value=age>1.5?"STALE":s.PdcCount==0?"NO CONTROLLED SHIP":s.DirectControlActive?"ACTIVE":"NATIVE / INACTIVE";color=s.DirectControlActive?p.Good:p.Warning;break;
                    case "Preaim":label="PREAIM";value=(s.PreaimStatus??"PREAIM WAIT").Replace("PREAIM: ","").Replace("PREAIM ","");color=value.Contains("REQUIRED")?p.Warning:p.Text;break;
                    case "FeedAge":label="TELEMETRY";value=age>1.5?"STALE":"LIVE  "+age.ToString("0.0")+"s";color=age>1.5?p.Warning:p.Secondary;break;
                    case "Warning":
                        if(valid&&s.CriticalHeat) {
                            double pulse=c.HeatWarningFlash&&s.CriticalHeatAgeSeconds<3?.5+.5*Math.Sin(s.CriticalHeatAgeSeconds*Math.PI*2):0;
                            using(var brush=new SolidBrush(Color.FromArgb((int)(60+70*pulse),225,48,40)))g.FillRectangle(brush,left,y,inner,rh-3);
                            HudText(g,"! CRITICAL PDC HEAT  "+s.HottestHeatPercent.ToString("0")+"%",Color.FromArgb(255,180,165),new RectangleF(left+8,y,inner-16,rh-3),14*text,true,font);
                        }
                        y+=rh;continue;
                }
                float lineHeight=26*text;
                HudText(g,label,p.Secondary,new RectangleF(left,y,inner*.42f-8,lineHeight),12*text,true,font);
                HudText(g,value,color,new RectangleF(left+inner*.42f,y,inner*.58f,lineHeight),14*text,true,font,true);
                if(row=="Heat") {
                    var bar=new RectangleF(left,y+29*text,inner,11*text);
                    using(var back=new SolidBrush(Color.FromArgb(145,10,15,19)))g.FillRectangle(back,bar);
                    if(valid){float fraction=(float)Math.Max(0,Math.Min(1,s.HottestHeatPercent/100));if(fraction>0)using(var fill=new SolidBrush(HeatGradient(s.HottestHeatPercent)))g.FillRectangle(fill,bar.X,bar.Y,bar.Width*fraction,bar.Height);}
                    if(valid&&c.HudShowAverageHeat&&!double.IsNaN(s.AverageHeatPercent)&&!double.IsInfinity(s.AverageHeatPercent)){float marker=bar.X+bar.Width*(float)Math.Max(0,Math.Min(1,s.AverageHeatPercent/100));using(var tick=new Pen(p.Text,2))g.DrawLine(tick,marker,bar.Y-2,marker,bar.Bottom+2);}
                }
                y+=rh;
            }
        }
        private static void HudText(Graphics g,string text,Color color,RectangleF box,float size,bool bold,string family,bool right=false)
        {
            if(box.Width<=0||box.Height<=0||string.IsNullOrEmpty(text))return;
            using(var format=new StringFormat(StringFormat.GenericTypographic))using(var brush=new SolidBrush(color))using(var font=new Font(family,size,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel)) {
                format.FormatFlags=StringFormatFlags.NoWrap;format.Alignment=right?StringAlignment.Far:StringAlignment.Near;format.LineAlignment=StringAlignment.Center;
                var measured=g.MeasureString(text,font,int.MaxValue,format);
                float fit=Math.Min(1,Math.Min(box.Width/Math.Max(1,measured.Width),box.Height/Math.Max(1,measured.Height)));
                using(var fitted=new Font(font.FontFamily,Math.Max(.5f,size*fit),font.Style,GraphicsUnit.Pixel))g.DrawString(text,fitted,brush,box,format);
            }
        }
        internal static Color HeatGradient(double heat)
        {
            double h=Math.Max(0,Math.Min(100,heat));
            Color a=h<=60?Color.FromArgb(74,210,131):Color.FromArgb(245,190,64);
            Color b=h<=60?Color.FromArgb(245,190,64):Color.FromArgb(245,65,55);
            double t=h<=60?h/60:(h-60)/40;
            return Color.FromArgb((int)(a.R+(b.R-a.R)*t),(int)(a.G+(b.G-a.G)*t),(int)(a.B+(b.B-a.B)*t));
        }
        private void Layer(Bitmap bitmap,Rectangle rect)
        {
            IntPtr screen=IntPtr.Zero,mem=IntPtr.Zero,hb=IntPtr.Zero,old=IntPtr.Zero;BitmapData bd=null;
            try
            {
                // Keep the first frame hidden until native alpha presentation succeeds.
                // Subsequent frames update in place: hiding every frame causes flicker.
                if(rect.Width<1||rect.Height<1){EnsureHidden();return;}
                if(!IsHandleCreated)SetBounds(rect.X,rect.Y,rect.Width,rect.Height);
                IntPtr hwnd=Handle;
                screen=Native.GetDC(IntPtr.Zero);mem=Native.CreateCompatibleDC(screen);var bi=new Native.BITMAPINFO();bi.bmiHeader.biSize=(uint)Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER));bi.bmiHeader.biWidth=bitmap.Width;bi.bmiHeader.biHeight=-bitmap.Height;bi.bmiHeader.biPlanes=1;bi.bmiHeader.biBitCount=32;bi.bmiHeader.biCompression=Native.BI_RGB;IntPtr bits;hb=Native.CreateDIBSection(screen,ref bi,Native.DIB_RGB_COLORS,out bits,IntPtr.Zero,0);if(hb==IntPtr.Zero){EnsureHidden();return;}old=Native.SelectObject(mem,hb);bd=bitmap.LockBits(new Rectangle(0,0,bitmap.Width,bitmap.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);for(int y=0;y<bitmap.Height;y++)NativeCopy(new IntPtr(bd.Scan0.ToInt64()+y*bd.Stride),new IntPtr(bits.ToInt64()+y*bitmap.Width*4),bitmap.Width*4);bitmap.UnlockBits(bd);bd=null;var dst=new Native.POINT(rect.X,rect.Y);var size=new Native.SIZE(bitmap.Width,bitmap.Height);var src=new Native.POINT(0,0);var blend=new Native.BLENDFUNCTION{BlendOp=Native.AC_SRC_OVER,SourceConstantAlpha=255,AlphaFormat=Native.AC_SRC_ALPHA};bool ok=Native.UpdateLayeredWindow(hwnd,screen,ref dst,ref size,mem,ref src,0,ref blend,Native.ULW_ALPHA);if(!ok){EnsureHidden();return;}if(!presented){Native.ShowWindow(hwnd,Native.SW_SHOWNA);presented=true;}if(streamer)Native.SetWindowDisplayAffinity(hwnd,Native.WDA_EXCLUDEFROMCAPTURE);
            }
            catch{EnsureHidden();}finally{if(bd!=null)try{bitmap.UnlockBits(bd);}catch{}if(old!=IntPtr.Zero&&mem!=IntPtr.Zero)Native.SelectObject(mem,old);if(hb!=IntPtr.Zero)Native.DeleteObject(hb);if(mem!=IntPtr.Zero)Native.DeleteDC(mem);if(screen!=IntPtr.Zero)Native.ReleaseDC(IntPtr.Zero,screen);}
        }
        [DllImport("kernel32.dll",EntryPoint="RtlMoveMemory")]private static extern void Copy(IntPtr d,IntPtr s,int n);private static void NativeCopy(IntPtr s,IntPtr d,int n){Copy(d,s,n);}
        private static void DrawText(Graphics g,string t,Color c,float x,float y,float size,FontStyle st,string fam){using(var f=new Font(fam,size,st,GraphicsUnit.Pixel))using(var b=new SolidBrush(c))g.DrawString(t??"",f,b,x,y);}
        private static void TextRight(Graphics g,string t,Color c,float x,float y,float size,FontStyle st,string fam){using(var f=new Font(fam,size,st,GraphicsUnit.Pixel)){SizeF z=g.MeasureString(t??"",f);DrawText(g,t,c,x-z.Width,y,size,st,fam);}}
        private static Color HeatColor(double h,ThemePalette p){return h>=85?p.Danger:h>=65?p.Warning:p.Text;}private static Color StateColor(PdcSnapshot s,ThemePalette p){if(s.FireLockout)return p.Warning;if(s.ActiveInbound>0)return p.Danger;if((s.TestState??"").Contains("READY"))return p.Good;return p.Secondary;}
        private static void DrawFrame(Graphics g, RectangleF rect, PdcConfig cfg, ThemePalette palette, int frameStyle, double borderWidth, int backingOpacity)
        {
            bool backingFill=backingOpacity>0; Color accent=palette.Accent;
            // ZEOCORE_V13B_PER_PANEL_BACKINGS - backing fill is independent per HUD.
            // Rails remain visible even when a panel's fill is disabled.
            Color panel = palette.Back;
            Color border = palette.Border;
            // War Room-era frames use the menu/command accent consistently so
            // panel content colors do not turn the frame rails cyan/amber/etc.
            if (frameStyle >= 6 && frameStyle != 16)
                accent = palette.MenuAccent;
            float bw = (float)borderWidth;
            int opacity = backingFill ? Math.Max(60, Math.Min(245, backingOpacity)) : 0;
            float x = rect.Left, y = rect.Top, r = rect.Right, b = rect.Bottom;

            if (frameStyle == 14) // LEGACY GLASS // ZEOCORE_V067_LEGACY_GLASS
            {
                float cut = Math.Max(7f, Math.Min(20f, Math.Min(rect.Width, rect.Height) * .065f));
                using (var path = new GraphicsPath())
                {
                    path.AddPolygon(new[] { new PointF(x+cut,y), new PointF(r-cut,y), new PointF(r,y+cut), new PointF(r,b-cut), new PointF(r-cut,b), new PointF(x+cut,b), new PointF(x,b-cut), new PointF(x,y+cut) });
                    using (var fill = new SolidBrush(Color.FromArgb(backingFill ? Math.Max(42, Math.Min(128, Math.Max(60, backingOpacity)/2)) : 0, panel))) g.FillPath(fill, path);
                    using (var rail = new Pen(Color.FromArgb(150, border), Math.Max(.8f, bw))) g.DrawPath(rail, path);
                    using (var ap = new Pen(Color.FromArgb(185, accent), Math.Max(1f, bw)))
                    {
                        float railLen = Math.Min(52f, rect.Width * .14f);
                        g.DrawLine(ap, x+cut+5f, y, x+cut+5f+railLen, y);
                        g.DrawLine(ap, r-cut-5f-railLen, b, r-cut-5f, b);
                    }
                }
                return;
            }

            if (frameStyle == 15) // KEEN SIGNAL // ZEOCORE_V13B_KEEN_SIGNAL_FRAME
            {
                float cut = Math.Max(11f, Math.Min(28f, rect.Height * .18f));
                using (var path = new GraphicsPath())
                {
                    path.AddPolygon(new[] {
                        new PointF(x+cut,y), new PointF(r-cut*.65f,y), new PointF(r,y+cut*.65f),
                        new PointF(r,b-cut*.65f), new PointF(r-cut*.65f,b), new PointF(x+cut,b),
                        new PointF(x,b-cut), new PointF(x,y+cut) });
                    using (var fill = new SolidBrush(Color.FromArgb(backingFill ? Math.Min(176, Math.Max(72, opacity)) : 0, panel))) g.FillPath(fill,path);
                    using (var edge = new Pen(Color.FromArgb(118,border), Math.Max(.8f,bw*.85f))) g.DrawPath(edge,path);
                }
                using (var rail = new Pen(Color.FromArgb(172, accent), Math.Max(1f,bw)))
                using (var ghost = new Pen(Color.FromArgb(72, border), Math.Max(.8f,bw*.75f)))
                {
                    float topLen=Math.Min(76f,rect.Width*.22f);
                    g.DrawLine(rail,x+cut+8f,y+1f,x+cut+8f+topLen,y+1f);
                    g.DrawLine(ghost,r-cut-52f,b-1f,r-cut-8f,b-1f);
                    float cy=(y+b)*.5f;
                    for(int i=0;i<4;i++)
                    {
                        float xx=r-18f-i*10f;
                        g.DrawLine(ghost,xx,cy-7f-(i*1.5f),xx,cy+7f+(i*1.5f));
                    }
                }
                return;
            }

            if (frameStyle == 16) // WEAPON CORE // ZEOCORE_V14A_WEAPON_CORE_FRAME
            {
                // Native WeaponCore language: nearly black tactical plate, very small
                // corner cuts, restrained edge treatment and the thin pale lower data rail.
                // No War Room accent rails are used in this style.
                float cut = Math.Max(4f, Math.Min(9f, rect.Height * .09f));
                Color wcPanel = Color.FromArgb(18, 23, 26);
                Color wcEdge = Color.FromArgb(142, 151, 154);
                Color wcRail = Color.FromArgb(202, 210, 211);

                using (var path = new GraphicsPath())
                {
                    path.AddPolygon(new[] {
                        new PointF(x+cut,y), new PointF(r-cut,y), new PointF(r,y+cut),
                        new PointF(r,b-cut), new PointF(r-cut,b), new PointF(x+cut,b),
                        new PointF(x,b-cut), new PointF(x,y+cut) });

                    using (var fill = new SolidBrush(Color.FromArgb(
                        backingFill ? Math.Min(218, Math.Max(138, opacity)) : 0, wcPanel)))
                        g.FillPath(fill, path);

                    // WeaponCore's outline is intentionally subtle; the lower rail does
                    // most of the visual framing.
                    using (var edge = new Pen(Color.FromArgb(88, wcEdge), Math.Max(.7f, bw * .70f)))
                        g.DrawPath(edge, path);
                }

                using (var rail = new Pen(Color.FromArgb(205, wcRail), Math.Max(1f, bw * .90f)))
                using (var dim = new Pen(Color.FromArgb(90, wcEdge), Math.Max(.7f, bw * .65f)))
                {
                    float yy = b - 4f;
                    float left = x + cut + 7f;
                    float right = r - cut - 8f;
                    g.DrawLine(rail, left, yy, right, yy);

                    // Tiny break/tick details echo WC's data-card separators without
                    // introducing decorative Zeo/War-Room rails.
                    g.DrawLine(dim, left + 22f, yy - 2f, left + 30f, yy - 2f);
                    g.DrawLine(dim, right - 42f, yy + 2f, right - 28f, yy + 2f);
                    g.DrawLine(rail, left, yy - 2f, left, yy + 2f);
                }
                return;
            }

            if (frameStyle == 0) // SE INDUSTRIAL - preserved known-good plate
            {
                float cut = Math.Max(6f, Math.Min(18f, Math.Min(rect.Width, rect.Height) * .07f));
                using (var path = new GraphicsPath())
                {
                    path.AddPolygon(new[] { new PointF(x+cut,y), new PointF(r-cut,y), new PointF(r,y+cut), new PointF(r,b-cut), new PointF(r-cut,b), new PointF(x+cut,b), new PointF(x,b-cut), new PointF(x,y+cut) });
                    using (var fill = new SolidBrush(Color.FromArgb(opacity, panel))) g.FillPath(fill, path);
                    using (var pen = new Pen(Color.FromArgb(Math.Min(210, opacity), border), bw)) g.DrawPath(pen, path);
                }
                using (var ap = new Pen(Color.FromArgb(235, accent), Math.Max(2f, bw*1.8f)))
                { g.DrawLine(ap, x+cut, y+1f, Math.Min(r-cut, x+cut+82f), y+1f); g.DrawLine(ap, x+1f, y+cut, x+1f, Math.Min(b-cut, y+cut+48f)); }
                return;
            }

            if (frameStyle == 1) // FIGHTER HUD - open frame, corner gates + sight rails
            {
                using (var fill = new LinearGradientBrush(rect, Color.FromArgb(Math.Min(opacity, 82), panel), Color.FromArgb(Math.Min(opacity, 24), panel), 90f))
                    g.FillRectangle(fill, rect);
                float c = Math.Max(18f, Math.Min(34f, rect.Height * .22f));
                using (var p = new Pen(Color.FromArgb(205, border), Math.Max(1f, bw)))
                using (var ap = new Pen(Color.FromArgb(245, accent), Math.Max(1.5f, bw*1.45f)))
                {
                    // open corners: deliberately no enclosing rectangle
                    g.DrawLine(ap,x,y,x+c,y); g.DrawLine(ap,x,y,x,y+c);
                    g.DrawLine(p,r-c,y,r,y); g.DrawLine(p,r,y,r,y+c);
                    g.DrawLine(p,x,b-c,x,b); g.DrawLine(p,x,b,x+c,b);
                    g.DrawLine(ap,r-c,b,r,b); g.DrawLine(ap,r,b-c,r,b);
                    // center sight rails + side ticks
                    float mid=(x+r)*.5f;
                    g.DrawLine(p,mid-42f,y+1f,mid-12f,y+1f); g.DrawLine(p,mid+12f,y+1f,mid+42f,y+1f);
                    for(int i=0;i<3;i++) { float yy=y+c+14f+i*16f; g.DrawLine(p,x,yy,x+7f,yy); g.DrawLine(p,r-7f,yy,r,yy); }
                }
                return;
            }

            if (frameStyle == 2) // MARS TACTICAL - asymmetric armored wedge + hard data spine
            {
                float cut = Math.Max(14f, Math.Min(30f, rect.Width*.06f));
                using (var path = new GraphicsPath())
                {
                    path.AddPolygon(new[]{ new PointF(x,y+8f), new PointF(x+18f,y), new PointF(r-cut,y), new PointF(r,y+cut), new PointF(r,b-8f), new PointF(r-12f,b), new PointF(x,b) });
                    using(var fill=new SolidBrush(Color.FromArgb(Math.Min(opacity,195),panel))) g.FillPath(fill,path);
                    using(var p=new Pen(Color.FromArgb(190,border),Math.Max(1f,bw))) g.DrawPath(p,path);
                }
                using(var spine=new SolidBrush(Color.FromArgb(225,accent))) g.FillRectangle(spine,x,y+18f,3.5f,Math.Max(24f,rect.Height-36f));
                using(var ap=new Pen(Color.FromArgb(235,accent),Math.Max(1.8f,bw*1.6f))) { g.DrawLine(ap,x+18f,y,r-cut,y); g.DrawLine(ap,r-cut,y,r,y+cut); }
                using(var tick=new Pen(Color.FromArgb(135,border),1f)) for(int i=0;i<4;i++) g.DrawLine(tick,r-26f-i*10f,b-7f,r-21f-i*10f,b-2f);
                return;
            }

            if (frameStyle == 3) // BELTER UTILITY - broken rails / scaffold, intentionally asymmetric
            {
                using(var fill=new SolidBrush(Color.FromArgb(Math.Min(opacity,105),panel))) g.FillRectangle(fill,x+8f,y+10f,Math.Max(1f,rect.Width-16f),Math.Max(1f,rect.Height-18f));
                using(var p=new Pen(Color.FromArgb(205,border),Math.Max(1f,bw)))
                using(var ap=new Pen(Color.FromArgb(235,accent),Math.Max(1.5f,bw*1.4f)))
                {
                    g.DrawLine(ap,x,y+6f,x+rect.Width*.62f,y+6f);
                    g.DrawLine(p,x+rect.Width*.72f,y+6f,r,y+6f);
                    g.DrawLine(ap,x+7f,y,x+7f,b-28f);
                    g.DrawLine(p,x+7f,b-18f,x+7f,b);
                    g.DrawLine(p,x+24f,b-4f,r-52f,b-4f);
                    g.DrawLine(ap,r-42f,b-4f,r,b-4f);
                    g.DrawLine(ap,r-1f,b-4f,r-1f,b-30f);
                    for(int i=0;i<5;i++) { float xx=x+26f+i*19f; g.DrawLine(p,xx,y+1f,xx+6f,y+11f); }
                }
                return;
            }

            if (frameStyle == 4) // NAVY GLASS - double floating glass panes, no heavy armor border
            {
                using(var fill=new LinearGradientBrush(rect,Color.FromArgb(Math.Min(opacity,72),panel),Color.FromArgb(Math.Min(opacity,26),panel),0f)) g.FillRectangle(fill,rect);
                using(var p=new Pen(Color.FromArgb(125,border),Math.Max(.8f,bw*.75f)))
                using(var ap=new Pen(Color.FromArgb(205,accent),Math.Max(1f,bw)))
                {
                    g.DrawLine(ap,x+16f,y,r-34f,y); g.DrawLine(ap,r-34f,y,r-20f,y+14f);
                    g.DrawLine(p,r-20f,y+14f,r-20f,b-14f); g.DrawLine(p,r-20f,b-14f,r-34f,b);
                    g.DrawLine(p,r-34f,b,x+16f,b); g.DrawLine(p,x+16f,b,x+4f,b-12f); g.DrawLine(p,x+4f,b-12f,x+4f,y+12f); g.DrawLine(p,x+4f,y+12f,x+16f,y);
                    RectangleF inner=new RectangleF(x+10f,y+7f,Math.Max(1f,rect.Width-36f),Math.Max(1f,rect.Height-14f));
                    g.DrawRectangle(p,inner.X,inner.Y,inner.Width,inner.Height);
                }
                return;
            }

            if (frameStyle == 6) // WAR ROOM - website-matched command center plate
            {
                using (var fill = new LinearGradientBrush(rect,
                    Color.FromArgb(Math.Min(opacity, 218), panel),
                    Color.FromArgb(Math.Min(opacity, 172), panel), 90f))
                    g.FillRectangle(fill, rect);

                float cut = Math.Max(8f, Math.Min(18f, Math.Min(rect.Width, rect.Height) * .08f));
                using (var outline = new GraphicsPath())
                {
                    outline.AddPolygon(new[] {
                        new PointF(x+cut,y), new PointF(r-cut,y), new PointF(r,y+cut),
                        new PointF(r,b), new PointF(x,b), new PointF(x,y+cut)
                    });
                    using (var p = new Pen(Color.FromArgb(178, border), Math.Max(.9f, bw)))
                        g.DrawPath(p, outline);
                }

                using (var inner = new Pen(Color.FromArgb(72, 235, 239, 243), 1f))
                    g.DrawRectangle(inner, x+7f, y+7f, Math.Max(1f, rect.Width-14f), Math.Max(1f, rect.Height-14f));

                using (var red = new Pen(Color.FromArgb(245, accent), Math.Max(2f, bw*1.7f)))
                using (var pale = new Pen(Color.FromArgb(160, 235, 239, 243), Math.Max(1f, bw*.85f)))
                {
                    g.DrawLine(red, x+cut, y+1f, Math.Min(r-cut, x+cut+96f), y+1f);
                    g.DrawLine(red, x+1f, y+cut, x+1f, Math.Min(b, y+cut+32f));
                    g.DrawLine(pale, r-58f, b-1f, r-14f, b-1f);
                    g.DrawLine(pale, r-14f, b-1f, r-7f, b-8f);
                    for (int i=0;i<3;i++)
                    {
                        float yy=y+18f+i*11f;
                        g.DrawLine(pale,r-7f,yy,r-2f,yy);
                    }
                }
                return;
            }

            if (frameStyle == 7) // COMMAND GRID - dark tactical grid + locked red corners
            {
                using (var fill = new SolidBrush(Color.FromArgb(Math.Min(opacity, 202), panel)))
                    g.FillRectangle(fill, rect);
                using (var grid = new Pen(Color.FromArgb(26, 220, 225, 230), 1f))
                {
                    float step = Math.Max(18f, Math.Min(28f, rect.Width / 12f));
                    for (float xx=x+step; xx<r; xx+=step) g.DrawLine(grid,xx,y,xx,b);
                    for (float yy=y+step; yy<b; yy+=step) g.DrawLine(grid,x,yy,r,yy);
                }
                using (var p = new Pen(Color.FromArgb(165, border), Math.Max(1f,bw)))
                    g.DrawRectangle(p,x,y,rect.Width,rect.Height);
                using (var red = new Pen(Color.FromArgb(245, accent), Math.Max(1.8f,bw*1.45f)))
                {
                    float c=Math.Max(15f,Math.Min(28f,rect.Height*.18f));
                    g.DrawLine(red,x,y,x+c,y); g.DrawLine(red,x,y,x,y+c);
                    g.DrawLine(red,r-c,b,r,b); g.DrawLine(red,r,b-c,r,b);
                }
                return;
            }

            if (frameStyle == 8) // REDLINE - open premium rails, minimum panel mass
            {
                using (var fill = new SolidBrush(Color.FromArgb(Math.Min(opacity, 92), panel)))
                    g.FillRectangle(fill, x+4f, y+4f, Math.Max(1f,rect.Width-8f), Math.Max(1f,rect.Height-8f));
                using (var pale = new Pen(Color.FromArgb(150, 230, 234, 238), Math.Max(.9f,bw*.8f)))
                using (var red = new Pen(Color.FromArgb(248, accent), Math.Max(2f,bw*1.7f)))
                {
                    float leftEnd=x+Math.Min(92f,rect.Width*.28f);
                    g.DrawLine(red,x,y,leftEnd,y);
                    g.DrawLine(pale,leftEnd+14f,y,r-18f,y);
                    g.DrawLine(pale,r-18f,y,r,y+18f);
                    g.DrawLine(pale,x,b-16f,x+16f,b);
                    g.DrawLine(pale,x+16f,b,r-72f,b);
                    g.DrawLine(red,r-56f,b,r,b);
                    g.DrawLine(red,r,b-26f,r,b);
                }
                return;
            }

            if (frameStyle == 9) // BLACKSITE - matte classified terminal / asymmetric data spine
            {
                float notch=Math.Max(14f,Math.Min(28f,rect.Width*.055f));
                using (var path = new GraphicsPath())
                {
                    path.AddPolygon(new[]{
                        new PointF(x,y), new PointF(r-notch,y), new PointF(r,y+notch),
                        new PointF(r,b-notch*.55f), new PointF(r-notch*.55f,b),
                        new PointF(x+12f,b), new PointF(x,b-12f)
                    });
                    using(var fill=new SolidBrush(Color.FromArgb(Math.Min(opacity,228),panel))) g.FillPath(fill,path);
                    using(var p=new Pen(Color.FromArgb(154,border),Math.Max(1f,bw))) g.DrawPath(p,path);
                }
                using(var red=new SolidBrush(Color.FromArgb(232,accent)))
                    g.FillRectangle(red,x+6f,y+16f,3f,Math.Max(18f,rect.Height-32f));
                using(var pale=new Pen(Color.FromArgb(120,230,234,238),1f))
                {
                    for(int i=0;i<5;i++)
                    {
                        float yy=y+15f+i*10f;
                        g.DrawLine(pale,r-20f,yy,r-8f,yy);
                    }
                    g.DrawLine(pale,x+18f,b-7f,x+72f,b-7f);
                }
                return;
            }

            if (frameStyle == 10) // CHEVRON - clipped command capsule with inward side cues
            {
                float cut=Math.Max(12f,Math.Min(30f,Math.Min(rect.Width*.055f,rect.Height*.22f)));
                using(var path=new GraphicsPath())
                {
                    path.AddPolygon(new[]{
                        new PointF(x+cut,y), new PointF(r-cut,y), new PointF(r,y+cut),
                        new PointF(r-cut*.55f,(y+b)*.5f), new PointF(r,b-cut), new PointF(r-cut,b),
                        new PointF(x+cut,b), new PointF(x,b-cut), new PointF(x+cut*.55f,(y+b)*.5f), new PointF(x,y+cut)
                    });
                    using(var fill=new SolidBrush(Color.FromArgb(Math.Min(opacity,205),panel))) g.FillPath(fill,path);
                    using(var edge=new Pen(Color.FromArgb(165,border),Math.Max(1f,bw))) g.DrawPath(edge,path);
                }
                using(var red=new Pen(Color.FromArgb(245,accent),Math.Max(1.8f,bw*1.55f)))
                using(var pale=new Pen(Color.FromArgb(130,232,236,240),Math.Max(.8f,bw*.75f)))
                {
                    g.DrawLine(red,x+cut,y+1f,x+Math.Min(rect.Width*.34f,128f),y+1f);
                    g.DrawLine(red,r-cut,b-1f,r-Math.Min(rect.Width*.22f,86f),b-1f);
                    float mid=(y+b)*.5f;
                    g.DrawLine(pale,x+3f,mid,x+13f,mid); g.DrawLine(pale,r-13f,mid,r-3f,mid);
                }
                return;
            }

            if (frameStyle == 11) // SPLIT WING - detached left/right command wings with open center rails
            {
                using(var fill=new SolidBrush(Color.FromArgb(Math.Min(opacity,116),panel)))
                    g.FillRectangle(fill,x+9f,y+7f,Math.Max(1f,rect.Width-18f),Math.Max(1f,rect.Height-14f));
                float wing=Math.Max(38f,Math.Min(88f,rect.Width*.22f));
                using(var pale=new Pen(Color.FromArgb(155,border),Math.Max(1f,bw)))
                using(var red=new Pen(Color.FromArgb(245,accent),Math.Max(1.9f,bw*1.6f)))
                {
                    // left wing
                    g.DrawLine(red,x,y+18f,x+16f,y); g.DrawLine(red,x+16f,y,x+wing,y);
                    g.DrawLine(pale,x,y+18f,x,b-18f); g.DrawLine(pale,x,b-18f,x+16f,b); g.DrawLine(pale,x+16f,b,x+wing,b);
                    // right wing
                    g.DrawLine(pale,r-wing,y,r-16f,y); g.DrawLine(pale,r-16f,y,r,y+18f);
                    g.DrawLine(red,r,b-18f,r-16f,b); g.DrawLine(red,r-16f,b,r-wing,b);
                    // deliberately open center rails
                    float mid=(x+r)*.5f;
                    g.DrawLine(pale,mid-42f,y+1f,mid-12f,y+1f); g.DrawLine(pale,mid+12f,y+1f,mid+42f,y+1f);
                    g.DrawLine(pale,mid-32f,b-1f,mid+32f,b-1f);
                }
                return;
            }

            if (frameStyle == 12) // HEX COMMAND - wide six-sided command cell
            {
                float cut=Math.Max(16f,Math.Min(34f,rect.Height*.24f));
                using(var path=new GraphicsPath())
                {
                    path.AddPolygon(new[]{
                        new PointF(x+cut,y), new PointF(r-cut,y), new PointF(r,y+rect.Height*.5f),
                        new PointF(r-cut,b), new PointF(x+cut,b), new PointF(x,y+rect.Height*.5f)
                    });
                    using(var fill=new LinearGradientBrush(rect,Color.FromArgb(Math.Min(opacity,218),panel),Color.FromArgb(Math.Min(opacity,160),panel),0f)) g.FillPath(fill,path);
                    using(var edge=new Pen(Color.FromArgb(168,border),Math.Max(1f,bw))) g.DrawPath(edge,path);
                }
                using(var inner=new Pen(Color.FromArgb(72,235,239,243),1f))
                {
                    float inset=7f;
                    g.DrawLine(inner,x+cut+inset,y+inset,r-cut-inset,y+inset);
                    g.DrawLine(inner,x+cut+inset,b-inset,r-cut-inset,b-inset);
                }
                using(var red=new Pen(Color.FromArgb(245,accent),Math.Max(2f,bw*1.65f)))
                {
                    g.DrawLine(red,x+cut,y+1f,x+cut+Math.Min(104f,rect.Width*.28f),y+1f);
                    g.DrawLine(red,r-cut-Math.Min(68f,rect.Width*.18f),b-1f,r-cut,b-1f);
                }
                return;
            }

            if (frameStyle == 13) // RAZOR - asymmetric blade cuts and offset rails
            {
                float slash=Math.Max(16f,Math.Min(32f,rect.Height*.20f));
                using(var path=new GraphicsPath())
                {
                    path.AddPolygon(new[]{
                        new PointF(x,y+slash), new PointF(x+slash,y), new PointF(r-46f,y),
                        new PointF(r,y+24f), new PointF(r,b), new PointF(x+34f,b), new PointF(x,b-22f)
                    });
                    using(var fill=new SolidBrush(Color.FromArgb(Math.Min(opacity,190),panel))) g.FillPath(fill,path);
                    using(var edge=new Pen(Color.FromArgb(150,border),Math.Max(1f,bw))) g.DrawPath(edge,path);
                }
                using(var red=new Pen(Color.FromArgb(248,accent),Math.Max(2.1f,bw*1.75f)))
                using(var pale=new Pen(Color.FromArgb(135,230,234,238),Math.Max(.9f,bw*.8f)))
                {
                    g.DrawLine(red,x+slash,y+1f,x+Math.Min(rect.Width*.40f,150f),y+1f);
                    g.DrawLine(red,r-46f,y+1f,r,y+25f);
                    g.DrawLine(pale,x+12f,b-8f,x+rect.Width*.60f,b-8f);
                    for(int i=0;i<3;i++)
                    {
                        float xx=r-18f-i*12f;
                        g.DrawLine(pale,xx,b-5f,xx+7f,b-12f);
                    }
                }
                return;
            }

            // STEALTH - only navigation ticks / corners, almost no backing
            using(var fill=new SolidBrush(Color.FromArgb(Math.Min(opacity,35),panel))) g.FillRectangle(fill,rect);
            using(var p=new Pen(Color.FromArgb(165,border),Math.Max(.8f,bw*.75f)))
            using(var ap=new Pen(Color.FromArgb(220,accent),Math.Max(1.2f,bw)))
            {
                float c=18f;
                g.DrawLine(ap,x,y,x+c,y); g.DrawLine(ap,x,y,x,y+c);
                g.DrawLine(p,r-c,y,r,y); g.DrawLine(p,r,y,r,y+c);
                g.DrawLine(p,x,b-c,x,b); g.DrawLine(p,x,b,x+c,b);
                g.DrawLine(ap,r-c,b,r,b); g.DrawLine(ap,r,b-c,r,b);
            }
        }

        private static int FrameIndex(string name)
        {
            string n = (name ?? "WAR ROOM").Trim().ToUpperInvariant();
            switch (n)
            {
                case "SE INDUSTRIAL": return 0;
                case "FIGHTER HUD": return 1;
                case "MARS TACTICAL": return 2;
                case "BELTER UTILITY": return 3;
                case "NAVY GLASS": return 4;
                case "STEALTH": return 5;
                case "WAR ROOM": return 6;
                case "COMMAND GRID": return 7;
                case "REDLINE": return 8;
                case "BLACKSITE": return 9;
                case "CHEVRON": return 10;
                case "SPLIT WING": return 11;
                case "HEX COMMAND": return 12;
                case "RAZOR": return 13;
                case "LEGACY GLASS": return 14;
                case "KEEN SIGNAL": return 15;
                case "WEAPON CORE": return 16;
                // Migrate v0.1.0-v0.1.2 experimental names to the nearest current ZeoCore style.
                case "FLIGHT HUD": return 1;
                case "MINIMAL": return 5;
                case "CUT CORNER": return 0;
                case "SEGMENTED ARMOR": return 0;
                default: return 6;
            }
        }


    }

    internal sealed class MenuForm:Form
    {
        public event Action<PdcCommand> Command;private PdcSnapshot s;private bool streamer;private Panel body;private Label title,status;private int page;private readonly Button[] nav=new Button[5];
        private readonly string[] pages={"STATUS","PDC","HUD"};
        protected override bool ShowWithoutActivation{get{return true;}}
        protected override CreateParams CreateParams{get{var cp=base.CreateParams;cp.ExStyle|=Native.WS_EX_TOOLWINDOW|Native.WS_EX_NOACTIVATE;return cp;}}
        public MenuForm(){Text="Zeo PDC";AutoScaleMode=AutoScaleMode.None;Width=660;Height=900;MinimumSize=new Size(660,760);MaximumSize=new Size(660,980);FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;Font=new Font("Segoe UI",9);BackColor=Color.FromArgb(7,9,11);DoubleBuffered=true;Visible=false;Build();}
        private void Build(){var head=new Panel{Dock=DockStyle.Top,Height=50,Tag="HEAD"};Controls.Add(head);title=new Label{AutoSize=true,Text="ZEO PDC // POINT DEFENSE",Font=new Font("Segoe UI",11,FontStyle.Bold),Location=new Point(16,9)};head.Controls.Add(title);status=new Label{AutoSize=true,Text="V0.3.30 DEFENSE // LEGACY MENU",Location=new Point(18,29),Font=new Font("Segoe UI",7.8f)};head.Controls.Add(status);var x=new Button{Text="X",FlatStyle=FlatStyle.Flat,Size=new Size(38,32),Location=new Point(614,8)};x.FlatAppearance.BorderSize=0;x.Click+=(a,b)=>{Hide();Send("MENU_CLOSE");};head.Controls.Add(x);
            var n=new Panel{Dock=DockStyle.Top,Height=82,Tag="NAV"};Controls.Add(n);for(int i=0;i<nav.Length;i++){int j=i;nav[i]=new Button{Text=pages[i],FlatStyle=FlatStyle.Flat,Location=new Point(14+i*128,32),Size=new Size(118,34)};nav[i].Click+=(a,b)=>{page=j;Rebuild();};n.Controls.Add(nav[i]);}
            body=new Panel{Location=new Point(0,132),Size=new Size(660,768),Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right,AutoScroll=true};Controls.Add(body);ApplyTheme();}
        public void CenterOnGame(PdcSnapshot x){int cx=x.ClientX+x.ClientW/2,cy=x.ClientY+x.ClientH/2;Location=new Point(cx-Width/2,Math.Max(x.ClientY,cy-Height/2));}
        public void SetStreamerMode(bool on){streamer=on;if(Visible&&IsHandleCreated)try{Native.SetWindowDisplayAffinity(Handle,on?Native.WDA_EXCLUDEFROMCAPTURE:Native.WDA_NONE);}catch{}}
        public void Prepare(PdcSnapshot x){s=x;status.Text="V0.3.30 DEFENSE // "+(x.CoreReady?"WC "+x.CoreEndpointCount:"WC WAIT")+" // "+(x.DirectControlActive?"CONTROL ACTIVE":"FALLBACK")+" // "+(x.StableProjectileTracking?"ID TRACK":"POS TRACK");ApplyTheme();Rebuild();}
        public void Apply(PdcSnapshot x){s=x;status.Text="V0.3.30 DEFENSE // "+(x.CoreReady?"WC "+x.CoreEndpointCount:"WC WAIT")+" // "+(x.DirectControlActive?"CONTROL ACTIVE":"FALLBACK")+" // "+(x.StableProjectileTracking?"ID TRACK":"POS TRACK");if(!Visible)return;ApplyTheme();RefreshLive();}
        private ThemePalette Palette(){var p=ThemePalette.From(new PdcConfig{Theme="GRAPHITE"});p.MenuBack=Color.FromArgb(27,37,42);p.MenuPanel=Color.FromArgb(38,52,59);p.MenuText=Color.FromArgb(209,232,240);p.MenuAccent=Color.FromArgb(140,205,222);return p;}
        public void ApplyTheme(){ThemePalette p=Palette();BackColor=p.MenuBack;ForeColor=p.MenuText;foreach(Control c in Controls)Style(c,p);Invalidate(true);}
        private void Style(Control root,ThemePalette p){root.ForeColor=p.MenuText;if(root is Panel)root.BackColor=p.MenuBack;if(root is Button){var b=(Button)root;b.BackColor=p.MenuPanel;b.ForeColor=p.MenuText;b.FlatAppearance.BorderColor=p.MenuAccent;}foreach(Control c in root.Controls)Style(c,p);}
        private void RefreshLive(){if(body==null||s==null)return;foreach(Control c in body.Controls){var l=c as Label;if(l!=null&&l.Tag is string)l.Text=Live((string)l.Tag);}}
        private string Live(string k){if(s==null)return "--";switch(k){case "summary":return "PDC "+s.PdcOnline+"/"+s.PdcCount+"   HEAT "+s.AverageHeatPercent.ToString("0.0")+"%   HOT "+s.HottestHeatPercent.ToString("0.0")+"%   HP "+s.AverageHpPercent.ToString("0.0")+"%   AMMO "+s.TotalAmmo.ToString("N0");case "bridge":return "DIRECT "+(s.DirectControlActive?"ACTIVE":"FALLBACK")+"   "+(s.ControlState??"")+"   SHOT MON "+(s.ShotMonitorReady?"YES":"NO")+"   TRACK "+(s.StableProjectileTracking?"STABLE ID":"POSITION FALLBACK")+"   WATCHDOG "+s.BridgeState;case "wave":return "SESSION "+s.WaveId.ToString("000")+"   VOLLEY "+s.CurrentVolley+"   COMPLETE "+s.CompletedVolleys+"   STATE "+s.TestState+"   TOTAL "+s.WaveResolved+"/"+s.WaveSeen+"   INT "+s.Intercepts+"   DANGER "+s.Hits;case "goal":return s.GoalProgress??"160 GOAL: awaiting test";case "mode":return "SAVED SETUP: "+(s.Config?.TargetedRequests==true?"C - TARGETED / A GATE":s.Config?.BypassAlignmentGate==true?"B - ANGLE GATE BYPASS":"A - ORIGINAL GATE");case "banks":return s.BankObserver??"OBSERVER waiting";case "last":return s.LastEvent??"";default:return "--";}}
        private void Rebuild(){body.SuspendLayout();body.Controls.Clear();ThemePalette p=Palette();int y=16;AddHeader(pages[page],ref y,p);
            if(page==0){AddLive("summary",ref y,p,true);AddText(s?.ControlState??"WAITING",ref y,p,p.MenuText);AddText(s?.RangeBankStatus??"RANGE BANKS WAITING",ref y,p,p.MenuText);AddText("Use PageDown for the native settings menu. Grid targeting is your choice.",ref y,p,p.MenuText,50);}
            else if(page==1){if(s!=null)foreach(var g in s.Pdcs)AddText(Short(g.Name,22)+"  Heat "+g.HeatPercent.ToString("0")+"%  ROF "+(g.Rof*100).ToString("0")+"%  Range "+g.RangeMeters.ToString("0")+"m",ref y,p,p.MenuText);AddToggle("PDC MANAGER",s?.Config?.ControlEnabled??true,"ControlEnabled",ref y,p);}
            else {AddToggle("FOLLOW ZEOCORE APPEARANCE",s?.Config?.FollowZeoCoreAppearance??true,"FollowZeoCoreAppearance",ref y,p);AddToggle("HUD VISIBLE",s?.Config?.HudEnabled??true,"HudEnabled",ref y,p);AddToggle("STREAMER MODE",s?.Config?.StreamerMode??true,"StreamerMode",ref y,p);}
            body.ResumeLayout();ApplyTheme();}
        private void AddHeader(string t,ref int y,ThemePalette p){AddText(t+" // LEGACY SETTINGS",ref y,p,p.MenuAccent,32,true);}
        private void AddSection(string t,ref int y,ThemePalette p){y+=10;AddText(t,ref y,p,p.MenuAccent,27,true);}
        private void AddLive(string tag,ref int y,ThemePalette p,bool strong){var l=new Label{Location=new Point(18,y),Size=new Size(604,32),Tag=tag,Text=Live(tag),Font=new Font("Segoe UI",strong?9.5f:8.7f,strong?FontStyle.Bold:FontStyle.Regular),ForeColor=p.MenuText};body.Controls.Add(l);y+=36;}
        private void AddText(string t,ref int y,ThemePalette p,Color c,int h=28,bool bold=false){var l=new Label{Location=new Point(18,y),Size=new Size(604,h),Text=t,ForeColor=c,Font=new Font("Segoe UI",bold?9.3f:8.7f,bold?FontStyle.Bold:FontStyle.Regular)};body.Controls.Add(l);y+=h+4;}
        private void AddButton(string text,Action a,ref int y,ThemePalette p){var b=new Button{Text=text,Location=new Point(18,y),Size=new Size(280,36),FlatStyle=FlatStyle.Flat,BackColor=p.MenuPanel,ForeColor=p.MenuText};b.FlatAppearance.BorderColor=p.MenuAccent;b.Click+=(x,e)=>a();body.Controls.Add(b);y+=43;}
        private void AddToggle(string text,bool on,string key,ref int y,ThemePalette p){var b=new Button{Text=text+": "+(on?"[X] ON":"[X] OFF"),Location=new Point(18,y),Size=new Size(340,36),FlatStyle=FlatStyle.Flat,BackColor=p.MenuPanel,ForeColor=on?p.Good:p.Secondary};b.FlatAppearance.BorderColor=p.MenuAccent;b.Click+=(x,e)=>{bool current=s?.Config!=null&&(bool)(typeof(PdcConfig).GetField(key).GetValue(s.Config));Set(key,current?0:1);};body.Controls.Add(b);y+=43;}
        private void Send(string type){Command?.Invoke(new PdcCommand{Type=type});}private void Set(string key,double v){Command?.Invoke(new PdcCommand{Type="SET",Key=key,Value=v});}private static string Short(string v,int n){v=v??"PDC";return v.Length<=n?v:v.Substring(0,n-1)+"…";}
        protected override void OnShown(EventArgs e){base.OnShown(e);if(streamer)try{Native.SetWindowDisplayAffinity(Handle,Native.WDA_EXCLUDEFROMCAPTURE);}catch{}Invalidate(true);}
        protected override void OnVisibleChanged(EventArgs e){base.OnVisibleChanged(e);if(!Visible&&IsHandleCreated)try{Native.SetWindowDisplayAffinity(Handle,Native.WDA_NONE);}catch{}}
    }
}

