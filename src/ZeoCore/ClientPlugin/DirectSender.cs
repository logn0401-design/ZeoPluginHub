using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace ZeoCore
{
    internal sealed class DirectSender : IDisposable
    {
        private readonly Uri _endpoint;
        private readonly string _host;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private int _inFlight;
        private volatile int _lastStatus;
        private volatile string _lastError = "never sent";
        private volatile int _lastAccepted;
        private volatile int _lastIgnored;
        private volatile int _lastRejected;
        private long _sent;
        private long _failed;

        public DirectSender(Uri endpoint)
        {
            _endpoint = endpoint ?? throw new ArgumentNullException("endpoint");
            _host = endpoint.Host;
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            ServicePointManager.Expect100Continue = false;
        }

        public string Host { get { return _host; } }
        public int LastStatus { get { return _lastStatus; } }
        public string LastError { get { return _lastError; } }
        public int LastAccepted { get { return _lastAccepted; } }
        public int LastIgnored { get { return _lastIgnored; } }
        public int LastRejected { get { return _lastRejected; } }
        public long Sent { get { return Interlocked.Read(ref _sent); } }
        public long Failed { get { return Interlocked.Read(ref _failed); } }
        public bool Busy { get { return Interlocked.CompareExchange(ref _inFlight, 0, 0) != 0; } }

        public bool TrySend(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;
            if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0) return false;

            ThreadPool.QueueUserWorkItem(_ => SendWorker(json));
            return true;
        }

        private void SendWorker(string json)
        {
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                var req = (HttpWebRequest)WebRequest.Create(_endpoint);
                req.Method = "POST";
                req.ContentType = "application/json; charset=utf-8";
                req.Accept = "application/json";
                req.UserAgent = "ZeoCore/" + Plugin.Version;
                req.ContentLength = bytes.Length;
                req.Timeout = 4500;
                req.ReadWriteTimeout = 4500;
                req.KeepAlive = true;
                req.AllowAutoRedirect = false;

                using (Stream stream = req.GetRequestStream())
                    stream.Write(bytes, 0, bytes.Length);

                string responseText = "";
                using (var response = (HttpWebResponse)req.GetResponse())
                {
                    _lastStatus = (int)response.StatusCode;
                    try
                    {
                        using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                            responseText = reader.ReadToEnd();
                    }
                    catch { }
                }

                if (_lastStatus >= 200 && _lastStatus < 300)
                {
                    ParseReceipt(responseText);
                    Interlocked.Increment(ref _sent);
                    _lastError = "";
                }
                else
                {
                    Interlocked.Increment(ref _failed);
                    _lastError = "HTTP " + _lastStatus;
                }
            }
            catch (WebException ex)
            {
                int status = 0;
                try
                {
                    var resp = ex.Response as HttpWebResponse;
                    if (resp != null)
                    {
                        status = (int)resp.StatusCode;
                        resp.Dispose();
                    }
                }
                catch { }
                _lastStatus = status;
                _lastError = status > 0 ? "HTTP " + status : ex.Status.ToString();
                Interlocked.Increment(ref _failed);
                Plugin.Log("Direct send failed host=" + _host + " error=" + _lastError);
            }
            catch (Exception ex)
            {
                _lastStatus = 0;
                _lastError = ex.GetType().Name + ": " + ex.Message;
                Interlocked.Increment(ref _failed);
                Plugin.Log("Direct send failed host=" + _host + " error=" + _lastError);
            }
            finally
            {
                Interlocked.Exchange(ref _inFlight, 0);
            }
        }

        private void ParseReceipt(string text)
        {
            _lastAccepted = 0;
            _lastIgnored = 0;
            _lastRejected = 0;
            if (string.IsNullOrWhiteSpace(text)) return;
            try
            {
                var row = _json.DeserializeObject(text) as Dictionary<string, object>;
                if (row == null) return;
                _lastAccepted = ReadInt(row, "accepted");
                _lastIgnored = ReadInt(row, "ignored");
                _lastRejected = ReadInt(row, "rejected");
            }
            catch { }
        }

        private static int ReadInt(Dictionary<string, object> row, string key)
        {
            object value;
            if (!row.TryGetValue(key, out value) || value == null) return 0;
            try { return Convert.ToInt32(value); }
            catch { return 0; }
        }

        public void Dispose()
        {
        }
    }
}
