using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Zeo.Performance
{
    // UI-thread owned. GDI+ draws directly into the reusable premultiplied DIB.
    // No per-frame managed pixel array, bitmap conversion, or row-copy pass.
    internal sealed class LayeredSurface : IDisposable
    {
        private IntPtr dc, dib, previous;
        private Bitmap bitmap;
        internal int Allocations { get; private set; }
        internal Bitmap GetBitmap(int width, int height)
        {
            if(width<1 || height<1 || (long)width*height>67108864)throw new ArgumentOutOfRangeException("width");
            if(bitmap!=null && bitmap.Width==width && bitmap.Height==height)return bitmap;
            Dispose();
            try
            {
                dc=CreateCompatibleDC(IntPtr.Zero);
                if(dc==IntPtr.Zero)throw new Win32Exception();
                var info=new Info();info.Header.Size=(uint)Marshal.SizeOf(typeof(Header));
                info.Header.Width=width;info.Header.Height=-height;info.Header.Planes=1;info.Header.Bits=32;
                IntPtr pixels;dib=CreateDIBSection(dc,ref info,0,out pixels,IntPtr.Zero,0);
                if(dib==IntPtr.Zero || pixels==IntPtr.Zero)throw new Win32Exception();
                previous=SelectObject(dc,dib);
                if(previous==IntPtr.Zero || previous==new IntPtr(-1))throw new Win32Exception();
                bitmap=new Bitmap(width,height,checked(width*4),PixelFormat.Format32bppPArgb,pixels);
                Allocations++;return bitmap;
            }
            catch { Dispose();throw; }
        }
        internal bool Present(IntPtr window,int left,int top)
        {
            if(bitmap==null)return false;
            var dst=new Point(left,top);var src=new Point();var size=new Size(bitmap.Width,bitmap.Height);
            var blend=new Blend { Alpha=255,Format=1 };
            return UpdateLayeredWindow(window,IntPtr.Zero,ref dst,ref size,dc,ref src,0,ref blend,2);
        }
        public void Dispose()
        {
            if(bitmap!=null){bitmap.Dispose();bitmap=null;}
            if(dc!=IntPtr.Zero && previous!=IntPtr.Zero && previous!=new IntPtr(-1))SelectObject(dc,previous);
            previous=IntPtr.Zero;
            if(dib!=IntPtr.Zero){DeleteObject(dib);dib=IntPtr.Zero;}
            if(dc!=IntPtr.Zero){DeleteDC(dc);dc=IntPtr.Zero;}
        }
        [StructLayout(LayoutKind.Sequential)]private struct Header
        {public uint Size;public int Width,Height;public ushort Planes,Bits;public uint Compression,ImageSize;public int XPels,YPels;public uint Colors,Important;}
        [StructLayout(LayoutKind.Sequential)]private struct Info {public Header Header;public uint Color;}
        [StructLayout(LayoutKind.Sequential,Pack=1)]private struct Blend {public byte Operation,Flags,Alpha,Format;}
        [DllImport("gdi32.dll",SetLastError=true)]private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll",SetLastError=true)]private static extern IntPtr CreateDIBSection(IntPtr dc,ref Info info,uint usage,out IntPtr bits,IntPtr section,uint offset);
        [DllImport("gdi32.dll",SetLastError=true)]private static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
        [DllImport("gdi32.dll")]private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")]private static extern bool DeleteDC(IntPtr dc);
        [DllImport("user32.dll",ExactSpelling=true,SetLastError=true)]private static extern bool UpdateLayeredWindow(IntPtr hwnd,IntPtr target,ref Point dst,ref Size size,IntPtr source,ref Point src,uint key,ref Blend blend,uint flags);
    }
}
