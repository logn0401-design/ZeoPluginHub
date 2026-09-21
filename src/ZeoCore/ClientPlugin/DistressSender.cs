using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace ZeoCore
{
    internal sealed class DistressSender : IDisposable
    {
        private readonly Uri _endpoint;
        private readonly string _deviceId;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
        private int _inFlight;
        private volatile int _lastStatus;
        private volatile string _lastError = "waiting";
        private volatile string _serverVersion = "";

        public DistressSender(Uri endpoint, string deviceId)
        {
            _endpoint = endpoint ?? throw new ArgumentNullException("endpoint");
            _deviceId = deviceId ?? "";
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            ServicePointManager.Expect100Continue = false;
        }

        public int LastStatus { get { return _lastStatus; } }
        public string LastError { get { return _lastError; } }
        public string ServerVersion { get { return _serverVersion; } }
        public bool Busy { get { return Interlocked.CompareExchange(ref _inFlight, 0, 0) != 0; } }

        public bool TrySend(object distressPayload)
        {
            if (distressPayload == null) return false;
            if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0) return false;
            _lastStatus = 0;
            _lastError = "sending";
            ThreadPool.QueueUserWorkItem(_ => SendWorker(distressPayload));
            return true;
        }

        private void SendWorker(object distressPayload)
        {
            try
            {
                var wrapper = new Dictionary<string, object>
                {
                    { "client_id", _deviceId },
                    { "distress", distressPayload }
                };
                byte[] bytes = Encoding.UTF8.GetBytes(_json.Serialize(wrapper));
                var req=(HttpWebRequest)WebRequest.Create(_endpoint);
                req.Method="POST";
                req.ContentType="application/json; charset=utf-8";
                req.Accept="application/json";
                req.UserAgent="ZeoCore/"+Plugin.Version;
                req.Timeout=6000;
                req.ReadWriteTimeout=6000;
                req.KeepAlive=true;
                req.AllowAutoRedirect=false;
                req.ContentLength=bytes.Length;
                using(Stream s=req.GetRequestStream()) s.Write(bytes,0,bytes.Length);
                string responseText = "";
                using(var resp=(HttpWebResponse)req.GetResponse())
                {
                    _lastStatus=(int)resp.StatusCode;
                    using(var reader=new StreamReader(resp.GetResponseStream(),Encoding.UTF8)) responseText = reader.ReadToEnd();
                }
                if (_lastStatus >= 200 && _lastStatus < 300)
                {
                    var root = _json.DeserializeObject(responseText) as Dictionary<string, object>;
                    object versionObj;
                    _serverVersion = root != null && root.TryGetValue("version", out versionObj) && versionObj != null
                        ? (Convert.ToString(versionObj) ?? "") : "";
                    if (!string.Equals(_serverVersion, "7.1", StringComparison.Ordinal))
                        throw new InvalidDataException("server version mismatch: " + (_serverVersion.Length == 0 ? "missing" : _serverVersion));
                    _lastError = "";
                }
                else _lastError = "HTTP "+_lastStatus;
            }
            catch(WebException ex)
            {
                int status=0;
                try { var r=ex.Response as HttpWebResponse; if(r!=null){status=(int)r.StatusCode;r.Dispose();} } catch { }
                _lastStatus=status;
                _lastError=status>0 ? "HTTP "+status : ex.Status.ToString();
                Plugin.Log("Distress send failed: "+_lastError);
            }
            catch(Exception ex)
            {
                _lastStatus=0;
                _lastError=ex.GetType().Name+": "+ex.Message;
                Plugin.Log("Distress send failed: "+_lastError);
            }
            finally { Interlocked.Exchange(ref _inFlight,0); }
        }

        public void Dispose() { }
    }
}
