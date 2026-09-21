using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ZeosOreHelper
{
    internal struct GameWindowSnapshot
    {
        public bool Valid; public int Left; public int Top; public int Width; public int Height; public bool Focused;
    }

    internal static class GameWindowState
    {
        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left,Top,Right,Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X,Y; }
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd,out RECT r);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd,ref POINT p);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd,int a,out RECT r,int cb);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd,out RECT r);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);

        internal static GameWindowSnapshot Capture()
        {
            var s=new GameWindowSnapshot();
            try
            {
                using(var p=Process.GetCurrentProcess())
                {
                    IntPtr h=p.MainWindowHandle; bool ok=false; RECT r=new RECT();
                    if(h!=IntPtr.Zero)
                    {
                        try
                        {
                            RECT c; POINT o=new POINT();
                            if(GetClientRect(h,out c)&&c.Right>c.Left&&c.Bottom>c.Top&&ClientToScreen(h,ref o))
                            {
                                s.Valid=true;s.Left=o.X;s.Top=o.Y;s.Width=c.Right-c.Left;s.Height=c.Bottom-c.Top;ok=s.Width>=200&&s.Height>=200;
                            }
                        }catch{ok=false;}
                        if(!ok)
                        {
                            try{ok=DwmGetWindowAttribute(h,DWMWA_EXTENDED_FRAME_BOUNDS,out r,Marshal.SizeOf(typeof(RECT)))==0&&r.Right>r.Left&&r.Bottom>r.Top;}catch{ok=false;}
                            if(!ok)try{ok=GetWindowRect(h,out r)&&r.Right>r.Left&&r.Bottom>r.Top;}catch{ok=false;}
                            if(ok){s.Valid=true;s.Left=r.Left;s.Top=r.Top;s.Width=r.Right-r.Left;s.Height=r.Bottom-r.Top;}
                        }
                    }
                    try{IntPtr fg=GetForegroundWindow();uint pid;GetWindowThreadProcessId(fg,out pid);s.Focused=pid==(uint)p.Id;}catch{s.Focused=false;}
                }
            }catch{}
            return s;
        }
    }
}
