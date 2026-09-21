using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
namespace ZeosOreOverlay
{
    internal static class Program
    {
        [STAThread]private static void Main(string[] args)
        {
            OverlayLog.Write("Ore overlay v0.7.2 starting");
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException+=(s,e)=>OverlayLog.Error(e.Exception);
            AppDomain.CurrentDomain.UnhandledException+=(s,e)=>OverlayLog.Write("Fatal: "+e.ExceptionObject);
            try {
            bool created;using(var mutex=new Mutex(true,"Local\\ZeosOreOverlay_v0_6",out created))
            {
                if(!created)return;try{NativeMethods.SetProcessDPIAware();}catch{}Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                string settings=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Pulsar","ZeosOreHelper","settings.ini");int port=37842;
                for(int i=0;i<args.Length;i++){if(args[i].Equals("--settings",StringComparison.OrdinalIgnoreCase)&&i+1<args.Length)settings=args[++i];else if(args[i].Equals("--port",StringComparison.OrdinalIgnoreCase)&&i+1<args.Length){int p;if(int.TryParse(args[++i],out p)&&p>1024&&p<65535)port=p;}}
                var s=new OreOverlaySettings(settings);Application.Run(new HudOverlayForm(s,port));
            }
            }catch(Exception ex){OverlayLog.Error(ex);}
        }
    }
}
