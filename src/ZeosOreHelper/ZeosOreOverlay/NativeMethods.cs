using System;
using System.Runtime.InteropServices;
namespace ZeosOreOverlay
{
    internal static class NativeMethods
    {
        internal const uint BI_RGB=0,DIB_RGB_COLORS=0;
        [StructLayout(LayoutKind.Sequential)]internal struct BITMAPINFOHEADER {public uint biSize;public int biWidth,biHeight;public ushort biPlanes,biBitCount;public uint biCompression,biSizeImage;public int biXPelsPerMeter,biYPelsPerMeter;public uint biClrUsed,biClrImportant;}
        [StructLayout(LayoutKind.Sequential)]internal struct BITMAPINFO {public BITMAPINFOHEADER bmiHeader;public uint bmiColors;}
        [DllImport("gdi32.dll",SetLastError=true)]internal static extern IntPtr CreateDIBSection(IntPtr hdc,ref BITMAPINFO pbmi,uint usage,out IntPtr bits,IntPtr section,uint offset);
        internal delegate bool EnumWindowsProc(IntPtr hWnd,IntPtr lParam);
        internal const int GWL_EXSTYLE=-20,WS_EX_LAYERED=0x00080000,WS_EX_TRANSPARENT=0x20,WS_EX_TOOLWINDOW=0x80,WS_EX_NOACTIVATE=0x08000000,GWLP_HWNDPARENT=-8,SW_HIDE=0,SW_SHOWNA=8,ULW_ALPHA=2;
        internal const byte AC_SRC_OVER=0,AC_SRC_ALPHA=1;internal const uint WDA_NONE=0,WDA_EXCLUDEFROMCAPTURE=0x11,SWP_NOMOVE=2,SWP_NOSIZE=1,SWP_NOACTIVATE=0x10,SWP_SHOWWINDOW=0x40;internal static readonly IntPtr HWND_TOPMOST=new IntPtr(-1);
        [StructLayout(LayoutKind.Sequential)]internal struct RECT{public int Left,Top,Right,Bottom;}
        [StructLayout(LayoutKind.Sequential)]internal struct POINT{public int X,Y;public POINT(int x,int y){X=x;Y=y;}}
        [StructLayout(LayoutKind.Sequential)]internal struct SIZE{public int cx,cy;public SIZE(int x,int y){cx=x;cy=y;}}
        [StructLayout(LayoutKind.Sequential,Pack=1)]internal struct BLENDFUNCTION{public byte BlendOp,BlendFlags,SourceConstantAlpha,AlphaFormat;}
        [DllImport("user32.dll",SetLastError=true)]internal static extern bool SetWindowDisplayAffinity(IntPtr h,uint a);
        [DllImport("user32.dll",SetLastError=true)]internal static extern bool GetWindowDisplayAffinity(IntPtr h,out uint a);
        [DllImport("user32.dll")]internal static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")]internal static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")]internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]internal static extern bool ReleaseCapture();
        [DllImport("user32.dll")]internal static extern IntPtr SendMessage(IntPtr hWnd,uint msg,IntPtr wParam,IntPtr lParam);
        [DllImport("user32.dll",EntryPoint="SetWindowLongPtr",SetLastError=true)]internal static extern IntPtr SetWindowLongPtr(IntPtr h,int n,IntPtr v);
        [DllImport("user32.dll")]internal static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
        [DllImport("user32.dll")]internal static extern bool EnumWindows(EnumWindowsProc p,IntPtr l);
        [DllImport("user32.dll")]internal static extern bool ShowWindow(IntPtr h,int n);
        [DllImport("user32.dll",SetLastError=true)]internal static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int cx,int cy,uint flags);
        [DllImport("user32.dll",ExactSpelling=true,SetLastError=true)]internal static extern bool UpdateLayeredWindow(IntPtr hwnd,IntPtr hdcDst,ref POINT dst,ref SIZE size,IntPtr hdcSrc,ref POINT src,int key,ref BLENDFUNCTION blend,int flags);
        [DllImport("user32.dll")]internal static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")]internal static extern int ReleaseDC(IntPtr h,IntPtr dc);
        [DllImport("gdi32.dll")]internal static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")]internal static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")]internal static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
        [DllImport("gdi32.dll")]internal static extern bool DeleteObject(IntPtr obj);
        [DllImport("user32.dll")]internal static extern bool GetWindowRect(IntPtr h,out RECT r);
        [DllImport("user32.dll")]internal static extern bool GetClientRect(IntPtr h,out RECT r);
        [DllImport("user32.dll")]internal static extern bool ClientToScreen(IntPtr h,ref POINT p);
        [DllImport("user32.dll")]internal static extern bool SetProcessDPIAware();
    }
}
