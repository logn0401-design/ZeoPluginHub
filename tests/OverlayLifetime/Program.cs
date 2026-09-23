using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Zeo.Shared;

static class Program
{
    static readonly string Exe = Assembly.GetExecutingAssembly().Location;
    static int checks;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    static ProcessStartInfo Info(string args) { return new ProcessStartInfo(Exe, args) { UseShellExecute=false, CreateNoWindow=true }; }
    static string Fresh() { return Path.Combine(Path.GetDirectoryName(Exe), "fixture-"+Guid.NewGuid().ToString("N")); }
    static void WaitFile(string path)
    {
        var clock=Stopwatch.StartNew(); while(!File.Exists(path) && clock.ElapsedMilliseconds<8000) Thread.Sleep(25);
        Check(File.Exists(path), "child ready");
    }
    static Process StartChild(OverlayProcessOwner owner, string path, string mode="normal", long window=0)
    {
        owner.Start(Info("child \""+path+"\" "+mode),window); WaitFile(path);
        var child=Process.GetProcessById(int.Parse(File.ReadAllText(path))); var handle=child.Handle; return child;
    }
    static void Exited(Process child, string reason)
    {
        Check(child.WaitForExit(7000),reason); Check(child.ExitCode==0,reason+" clean exit code"); child.Dispose();
    }
    [STAThread] static void Main(string[] args)
    {
        if(args.Length>0 && args[0]=="child")
        {
            using(var lifetime=OverlayLifetime.Attach(args,()=> { if(args[2]=="normal") Environment.Exit(0); else Thread.Sleep(Timeout.Infinite); }))
            {
                if(lifetime==null) Environment.Exit(9);
                File.WriteAllText(args[1],Process.GetCurrentProcess().Id.ToString());
                Thread.Sleep(Timeout.Infinite);
            }
            return;
        }
        if(args.Length>0 && args[0]=="parent")
        {
            var owner=new OverlayProcessOwner();
            var child=StartChild(owner,args[1],args[2]);
            File.WriteAllText(args[1]+".parent-ready","ready");
            if(args[3]=="exit") Environment.Exit(0); // deliberately skip Dispose, like game teardown
            Thread.Sleep(Timeout.Infinite);
            return;
        }
        Check(OverlayLifetime.Attach(new string[0],()=>{})==null,"missing owner rejected");
        Check(OverlayLifetime.Attach(new[]{"--owner-pid","bad"},()=>{})==null,"invalid pid rejected");
        using(var self=Process.GetCurrentProcess())
        using(var signal=new EventWaitHandle(false,EventResetMode.ManualReset,"Local\\ZeoOverlayStop_01234567890123456789012345678901"))
        {
            string[] wrong={"--owner-pid",self.Id.ToString(),"--owner-start",(self.StartTime.ToUniversalTime().Ticks+1).ToString(),"--owner-stop","01234567890123456789012345678901"};
            Check(OverlayLifetime.Attach(wrong,()=>{})==null,"same PID different creation time rejected");
            wrong[3]=self.StartTime.ToUniversalTime().Ticks.ToString(); signal.Set();
            Check(OverlayLifetime.Attach(wrong,()=>{})==null,"already stopped owner rejected");
        }
        using(var first=new OverlayProcessOwner())
        using(var second=new OverlayProcessOwner())
        {
            var a=StartChild(first,Fresh());var b=StartChild(second,Fresh());
            Thread.Sleep(3200); Check(!a.HasExited && !b.HasExited,"no telemetry does not imply owner exit");
            first.Start(Info("child ignored normal")); Check(first.Running,"repeated Start retains child");
            first.Dispose(); Exited(a,"plugin unload"); Check(!b.HasExited,"unrelated overlay survives other plugin unload");
            second.Dispose(); Exited(b,"second independent unload"); first.Dispose();
        }
        foreach(string stop in new[]{"exit","kill"})
        foreach(string ui in new[]{"normal","hung"})
        {
            string path=Fresh();using(var parent=Process.Start(Info("parent \""+path+"\" "+ui+" "+stop)))
            {
                WaitFile(path);var child=Process.GetProcessById(int.Parse(File.ReadAllText(path)));var handle=child.Handle;WaitFile(path+".parent-ready");
                if(stop=="kill") parent.Kill(); Check(parent.WaitForExit(5000),"fixture owner ended");
                Exited(child,"owner "+stop+" / UI "+ui);
            }
        }
        using(var owner=new OverlayProcessOwner())
        {
            var child=StartChild(owner,Fresh(),"hung");owner.Dispose();
            Check(child.WaitForExit(5000),"plugin unload kills only owned hung child");child.Dispose();
        }
        using(var owner=new OverlayProcessOwner())
        {
            var window=new NativeWindow();
            window.CreateHandle(new CreateParams { Caption="Zeo lifetime hidden test",Style=0,X=-30000,Y=-30000,Width=1,Height=1 });
            var child=StartChild(owner,Fresh(),"normal",window.Handle.ToInt64());
            Thread.Sleep(11000);
            Check(!child.HasExited,"hidden/unfocused existing window survives beyond close grace");
            window.DestroyHandle();
            Check(child.WaitForExit(15000),"destroyed game window closes overlay while owner remains alive");
            Check(!Process.GetCurrentProcess().HasExited,"window closure did not terminate owner");child.Dispose();
        }
        Console.WriteLine("PASS: "+checks+" real-process lifetime checks (normal exit, crash, hung UI, unload, isolation, no-telemetry survival, PID reuse rejection).");
    }
}
