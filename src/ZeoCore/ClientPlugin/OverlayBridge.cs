using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Web.Script.Serialization;

namespace ZeoCore
{
    internal sealed class OverlayBridge : IDisposable
    {
        internal const int Port = 37841;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
        private readonly UdpClient _udp = new UdpClient();
        private readonly IPEndPoint _endpoint = new IPEndPoint(IPAddress.Loopback, Port);
        private DateTime _lastLaunchAttemptUtc = DateTime.MinValue;
        private readonly Zeo.Shared.OverlayProcessOwner _owner = new Zeo.Shared.OverlayProcessOwner();
        private long _sent;
        private string _lastError = "";

        internal string OverlayExePath
        {
            get { return Plugin.CatalogOverlayPath ?? Path.Combine(Plugin.DataDirectory, "ZeoOverlay.exe"); }
        }

        internal long Sent { get { return _sent; } }
        internal string LastError { get { return _lastError; } }
        internal bool Installed { get { return File.Exists(OverlayExePath); } }
        internal bool Running { get { return _owner.Running; } }

        internal void EnsureRunning(bool autoLaunch)
        {
            if (!autoLaunch || Running) return;
            if ((DateTime.UtcNow - _lastLaunchAttemptUtc).TotalSeconds < 5) return;
            _lastLaunchAttemptUtc = DateTime.UtcNow;

            try
            {
                if (!File.Exists(OverlayExePath))
                {
                    _lastError = "overlay executable missing";
                    return;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = OverlayExePath,
                    Arguments = "--settings \"" + HudSettings.PathName + "\" --port " + Port,
                    WorkingDirectory = Plugin.DataDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                _owner.Start(psi);
                _lastError = "";
                Plugin.Log("Capture-safe ZeoOverlay launch requested.");
            }
            catch (Exception ex)
            {
                _lastError = ex.GetType().Name + ": " + ex.Message;
                Plugin.Log("Overlay launch failed: " + _lastError);
            }
        }

        internal void SendFrame(OverlayFrame frame)
        {
            if (frame == null) return;
            try
            {
                var packet = new OverlayPacket { Kind = "frame", Frame = frame };
                Send(packet);
                _sent++;
                _lastError = "";
            }
            catch (Exception ex)
            {
                _lastError = ex.GetType().Name + ": " + ex.Message;
            }
        }

        internal void PollLayoutFeedback()
        {
            try
            {
                for(int i=0;i<8 && _udp.Available>0;i++)
                {
                    var sender=new IPEndPoint(IPAddress.Loopback,0);
                    byte[] bytes=_udp.Receive(ref sender);
                    if(!IPAddress.IsLoopback(sender.Address) || sender.Port!=Port || bytes.Length>8192) continue;
                    var reply=_json.Deserialize<ZeoOverlay.HudLayoutFeedback>(Encoding.UTF8.GetString(bytes));
                    if(reply!=null && reply.Kind=="layout-bounds") ZeoHudLayoutSession.Receive(reply);
                }
            }
            catch(Exception ex) { _lastError="layout feedback: "+ex.Message; }
        }

        internal void SendMarkers(OverlayMarkerUpdate update)
        {
            try { Send(new OverlayPacket { Kind="markers", MarkerUpdate=update }); }
            catch (Exception ex) { _lastError=ex.GetType().Name+": "+ex.Message; }
        }

        internal void OpenMenu()
        {
            try { Send(new OverlayPacket { Kind = "command", Command = "open-menu" }); }
            catch (Exception ex) { _lastError = ex.GetType().Name + ": " + ex.Message; }
        }

        internal void ToggleMenu()
        {
            try { Send(new OverlayPacket { Kind = "command", Command = "toggle-menu" }); }
            catch (Exception ex) { _lastError = ex.GetType().Name + ": " + ex.Message; }
        }

        private void Send(OverlayPacket packet)
        {
            string text = _json.Serialize(packet);
            byte[] data = Encoding.UTF8.GetBytes(text);
            if (data.Length > 62000)
                throw new InvalidOperationException("overlay packet exceeded safe UDP size");
            _udp.Send(data, data.Length, _endpoint);
        }

        public void Dispose()
        {
            _owner.Dispose();
            try { _udp.Close(); } catch { }
        }
    }
}
