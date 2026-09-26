using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace ZeoCore
{
    internal sealed class DeviceTelemetrySender : IDisposable
    {
        private readonly Uri _endpoint;
        private readonly string _host;
        private readonly string _clientId;
        private readonly JavaScriptSerializer _json =
            new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };

        private int _inFlight;
        private volatile int _lastStatus;
        private volatile string _lastError = "waiting";
        private volatile string _serverVersion = "";
        private volatile int _lastAccepted;
        private volatile int _lastIgnored;
        private volatile int _lastRejected;
        private long _sent;
        private long _failed;

        internal DeviceTelemetrySender(ZeoConfig config)
        {
            if (config == null) throw new ArgumentNullException("config");

            Uri baseUri = config.GetAccountBaseUri();
            _endpoint = new Uri(baseUri, "/v1/zeo/open/telemetry");

            if (!_endpoint.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !_endpoint.Host.Equals(ZeoConfig.BattleHost, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Telemetry endpoint host validation failed.");

            _host = _endpoint.Host;
            _clientId = config.DeviceId ?? "";

            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            ServicePointManager.Expect100Continue = false;
        }

        internal string Host { get { return _host; } }
        internal int LastStatus { get { return _lastStatus; } }
        internal string LastError { get { return _lastError; } }
        internal string ServerVersion { get { return _serverVersion; } }
        internal int LastAccepted { get { return _lastAccepted; } }
        internal int LastIgnored { get { return _lastIgnored; } }
        internal int LastRejected { get { return _lastRejected; } }
        internal long Sent { get { return Interlocked.Read(ref _sent); } }
        internal long Failed { get { return Interlocked.Read(ref _failed); } }
        internal bool Busy { get { return Interlocked.CompareExchange(ref _inFlight, 0, 0) != 0; } }

        internal bool TrySend(string telemetryJson, string factionTag)
        {
            if (string.IsNullOrWhiteSpace(telemetryJson)) return false;
            if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0) return false;

            string faction = string.IsNullOrWhiteSpace(factionTag)
                ? "UNAFFILIATED"
                : factionTag.Trim().ToUpperInvariant();

            _lastStatus = 0;
            _lastError = "sending";

            ThreadPool.QueueUserWorkItem(_ => SendWorker(telemetryJson, faction));
            return true;
        }

        private void SendWorker(string telemetryJson, string factionTag)
        {
            try
            {
                object telemetry = _json.DeserializeObject(telemetryJson);
                var telemetryObject = telemetry as Dictionary<string, object>;
                if (telemetryObject == null)
                    throw new InvalidDataException("telemetry envelope is not JSON object");

                var wrapper = new Dictionary<string, object>
                {
                    { "client_id", _clientId },
                    { "faction_tag", factionTag ?? "UNAFFILIATED" },
                    { "telemetry", telemetryObject }
                };

                byte[] bytes = Encoding.UTF8.GetBytes(_json.Serialize(wrapper));

                var req = (HttpWebRequest)WebRequest.Create(_endpoint);
                req.Method = "POST";
                req.ContentType = "application/json; charset=utf-8";
                req.Accept = "application/json";
                req.UserAgent = "ZeoCore/" + Plugin.Version;
                req.Timeout = 6000;
                req.ReadWriteTimeout = 6000;
                req.KeepAlive = true;
                req.AllowAutoRedirect = false;
                req.ContentLength = bytes.Length;

                using (Stream stream = req.GetRequestStream())
                    stream.Write(bytes, 0, bytes.Length);

                string responseText = "";
                using (var response = (HttpWebResponse)req.GetResponse())
                {
                    _lastStatus = (int)response.StatusCode;
                    using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                        responseText = reader.ReadToEnd();
                }

                if (_lastStatus < 200 || _lastStatus >= 300)
                    throw new InvalidOperationException("HTTP " + _lastStatus);

                ParseReceipt(responseText);
                Interlocked.Increment(ref _sent);
                _lastError = "";
            }
            catch (WebException ex)
            {
                int status = 0;
                string detail = "";
                try
                {
                    var resp = ex.Response as HttpWebResponse;
                    if (resp != null)
                    {
                        status = (int)resp.StatusCode;
                        try
                        {
                            using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                                detail = reader.ReadToEnd();
                        }
                        catch { }
                        resp.Dispose();
                    }
                }
                catch { }

                _lastStatus = status;
                _lastError = status > 0
                    ? "HTTP " + status + (string.IsNullOrWhiteSpace(detail) ? "" : " " + Trim(detail, 500))
                    : ex.Status.ToString();

                Interlocked.Increment(ref _failed);
                Plugin.Log("Open-test telemetry failed host=" + _host + " error=" + _lastError);
            }
            catch (Exception ex)
            {
                _lastStatus = 0;
                _lastError = ex.GetType().Name + ": " + ex.Message;
                Interlocked.Increment(ref _failed);
                Plugin.Log("Open-test telemetry failed host=" + _host + " error=" + _lastError);
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
            _serverVersion = "";

            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidDataException("empty V7.1 telemetry receipt");

            var root = _json.DeserializeObject(text) as Dictionary<string, object>;
            if (root == null)
                throw new InvalidDataException("telemetry receipt is not JSON object");

            _serverVersion = ReadString(root, "version") ?? "";
            if (!string.Equals(_serverVersion, "7.1", StringComparison.Ordinal))
                throw new InvalidDataException(
                    "server version mismatch: " +
                    (_serverVersion.Length == 0 ? "missing" : _serverVersion));

            // V7.1 open telemetry returns the internal ingest receipt under "ingest".
            // Accept both nested and direct receipt fields for compatibility.
            Dictionary<string, object> receipt = root;
            object ingestRaw;
            if (root.TryGetValue("ingest", out ingestRaw))
            {
                var nested = ingestRaw as Dictionary<string, object>;
                if (nested != null) receipt = nested;
            }

            _lastAccepted = ReadInt(receipt, "accepted");
            _lastIgnored = ReadInt(receipt, "ignored");
            _lastRejected = ReadInt(receipt, "rejected");
        }

        private static int ReadInt(Dictionary<string, object> row, string key)
        {
            object value;
            if (row == null || !row.TryGetValue(key, out value) || value == null) return 0;
            try { return Convert.ToInt32(value); }
            catch { return 0; }
        }

        private static string ReadString(Dictionary<string, object> row, string key)
        {
            object value;
            if (row == null || !row.TryGetValue(key, out value) || value == null) return null;
            try { return Convert.ToString(value); }
            catch { return null; }
        }

        private static string Trim(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max) return value ?? "";
            return value.Substring(0, max);
        }

        public void Dispose()
        {
        }
    }
}
