using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Web.Script.Serialization;

namespace ZeosOreHelper
{
    internal sealed class OreOverlayBridge : IDisposable
    {
        internal const int Port = 37842;
        private readonly JavaScriptSerializer _json=new JavaScriptSerializer{MaxJsonLength=8*1024*1024};
        private readonly UdpClient _udp=new UdpClient(new IPEndPoint(IPAddress.Loopback,0));
        internal ZeoOreShared.OreLayoutAck LayoutAck;internal DateTime AckUtc;
        private void ReceiveAcks(){try{int budget=8;while(_udp.Available>0&&budget-->0){var ep=new IPEndPoint(IPAddress.Loopback,0);var bytes=_udp.Receive(ref ep);if(ep.Port!=Port||!IPAddress.IsLoopback(ep.Address))continue;var ack=_json.Deserialize<ZeoOreShared.OreLayoutAck>(Encoding.UTF8.GetString(bytes));if(ack==null||string.IsNullOrEmpty(ack.Token))continue;LayoutAck=ack;AckUtc=DateTime.UtcNow;}}catch{}}
        private readonly IPEndPoint _endpoint=new IPEndPoint(IPAddress.Loopback,Port);
        private DateTime _lastLaunchUtc=DateTime.MinValue;
        private string _lastError="";
        private long _sent;
        private readonly Zeo.Shared.OverlayProcessOwner _owner = new Zeo.Shared.OverlayProcessOwner();

        internal string OverlayExePath { get { return Plugin.CatalogOverlayPath??Path.Combine(Plugin.DataDirectory,"ZeosOreOverlay.exe"); } }
        internal bool Installed { get { return File.Exists(OverlayExePath); } }
        internal string LastError { get { return _lastError; } }
        internal long Sent { get { return _sent; } }
        internal bool Running { get { return _owner.Running; } }

        internal void EnsureRunning(bool enabled)
        {
            if(!enabled||Running)return;
            if((DateTime.UtcNow-_lastLaunchUtc).TotalSeconds<4)return;
            _lastLaunchUtc=DateTime.UtcNow;
            try
            {
                if(!Installed){_lastError="overlay executable missing";return;}
                var psi=new ProcessStartInfo{FileName=OverlayExePath,Arguments="--settings \""+HudSettings.SettingsPath+"\" --port "+Port,WorkingDirectory=Plugin.DataDirectory,UseShellExecute=false,CreateNoWindow=true};
                _owner.Start(psi);_lastError="";Plugin.Log("ZeosOreOverlay launch requested.");
            }
            catch(Exception ex){_lastError=ex.GetType().Name+": "+ex.Message;Plugin.Log("Overlay launch failed: "+_lastError);}
        }

        internal void SendFrame(OreOverlayFrame f){ReceiveAcks();if(f==null)return;try{Send(new OreOverlayPacket{Kind="frame",Frame=f});_sent++;_lastError="";}catch(Exception ex){_lastError=ex.GetType().Name+": "+ex.Message;}}
        internal void ToggleMenu(){try{Send(new OreOverlayPacket{Kind="command",Command="toggle-menu"});}catch(Exception ex){_lastError=ex.Message;}}
        internal void OpenMenu(){try{Send(new OreOverlayPacket{Kind="command",Command="open-menu"});}catch(Exception ex){_lastError=ex.Message;}}
        internal void SettingsChanged(){try{Send(new OreOverlayPacket{Kind="command",Command="reload-settings"});}catch{}}
        private void Send(OreOverlayPacket p){byte[] d=Encode(p);_udp.Send(d,d.Length,_endpoint);}
        internal byte[] Encode(OreOverlayPacket p){string t=_json.Serialize(p);byte[] d=Encoding.UTF8.GetBytes(t);while(d.Length>62000&&p.Frame!=null&&(p.Frame.Roids.Count>1||p.Frame.Deposits.Count>0)){
            // Drop the lowest-priority row, not the entire HUD, when metadata fills a datagram.
            if(p.Frame.Deposits.Count>0){p.Frame.Deposits.RemoveAt(p.Frame.Deposits.Count-1);p.Frame.ShownPings=p.Frame.Roids.Count(r=>r.PingEligible)+p.Frame.Deposits.Count;d=Encoding.UTF8.GetBytes(_json.Serialize(p));continue;}
            var remove=p.Frame.Roids.Where(r=>!r.Selected&&!r.PingEligible).LastOrDefault()??p.Frame.Roids.LastOrDefault(r=>!r.Selected)??p.Frame.Roids.Last();
            p.Frame.Roids.Remove(remove);p.Frame.ShownPings=p.Frame.Roids.Count(r=>r.PingEligible);d=Encoding.UTF8.GetBytes(_json.Serialize(p));
        }if(d.Length>62000)throw new InvalidOperationException("overlay packet exceeded safe UDP size");return d;}
        public void Dispose(){_owner.Dispose();try{_udp.Close();}catch{}}
    }
}
