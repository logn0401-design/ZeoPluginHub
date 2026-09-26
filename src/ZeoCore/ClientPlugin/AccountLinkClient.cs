using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace ZeoCore
{
    internal sealed class AccountLinkSnapshot
    {
        public string State { get; set; } = "STARTING";
        public string Detail { get; set; } = "Preparing device identity";
        public bool Linked { get; set; }
        public bool Authorized { get; set; }
        public string VerifiedSteamId { get; set; }
        public string VerifiedIdentityId { get; set; }
        public string PairingCode { get; set; } = "";
        public long PairingExpiresUtcMs { get; set; }
        public string Username { get; set; } = "";
        public string FactionTag { get; set; } = "";
        public string AssignedScope { get; set; } = "self";
        public string EffectiveScope { get; set; } = "self";
        public int LastHttpStatus { get; set; }
        public long LastSuccessUtcMs { get; set; }
        public bool Busy { get; set; }

        public AccountLinkSnapshot Clone()
        {
            return (AccountLinkSnapshot)MemberwiseClone();
        }

        public string Fingerprint()
        {
            return (State ?? "") + "|" + Linked + "|" + Authorized + "|" +
                   (PairingCode ?? "") + "|" + (Username ?? "") + "|" +
                   (FactionTag ?? "") + "|" + (EffectiveScope ?? "") + "|" +
                   LastHttpStatus + "|" + (Detail ?? "");
        }
    }

    internal sealed class AccountLinkClient : IDisposable
    {
        private readonly Uri _baseUri;
        private readonly string _deviceId;
        private readonly string _deviceKey;
        private readonly ZeoConfig _config;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024 };
        private readonly object _sync = new object();
        private AccountLinkSnapshot _snapshot = new AccountLinkSnapshot();
        private int _inFlight;
        private int _lastFrame = -100000;
        private long _nextAttemptUtcMs;
        private int _errorStreak;
        private volatile bool _disposed;
        private HttpWebRequest _activeRequest;

        public AccountLinkClient(ZeoConfig config)
        {
            _config = config ?? throw new ArgumentNullException("config");
            _baseUri = _config.GetAccountBaseUri();
            _deviceId = _config.DeviceId ?? throw new ArgumentNullException("deviceId");
            _deviceKey = _config.GetDeviceKey();
            if (!_baseUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !_baseUri.Host.Equals(ZeoConfig.BattleHost, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("AccountLink host validation failed.");
            if (_deviceKey.Length < 32 || _deviceKey.Length > 256)
                throw new InvalidOperationException("Device key length is invalid.");
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            ServicePointManager.Expect100Continue = false;

            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (!string.IsNullOrWhiteSpace(_config.PendingLinkCode) && _config.PendingLinkExpiresUtcMs > now)
            {
                _snapshot.State = "LINK REQUIRED";
                _snapshot.Detail = "Enter this code on Members > ZeoCore Devices";
                _snapshot.PairingCode = _config.PendingLinkCode.Trim().ToUpperInvariant();
                _snapshot.PairingExpiresUtcMs = _config.PendingLinkExpiresUtcMs;
            }
        }

        public AccountLinkSnapshot Snapshot()
        {
            lock (_sync)
            {
                var copy = _snapshot.Clone();
                copy.Busy = Interlocked.CompareExchange(ref _inFlight, 0, 0) != 0;
                return copy;
            }
        }

        public void Update(int frame, ServerTrustSnapshot trust, ulong steamId, long seIdentityId, string displayName)
        {
            if(_disposed)return;
            // v0.5.9: account/device authorization is intentionally independent from recurring DX verification.
            // The website binds this device to the signed-in Zeo/Discord account.
            // After faction approval, telemetry, shared tactical receive and distress use
            // device/member authorization. DX identity remains onboarding and sector context only.

            if (steamId == 0 || seIdentityId == 0)
            {
                lock (_sync)
                {
                    _snapshot.State = "WAITING FOR IDENTITY";
                    _snapshot.Detail = "Steam / Space Engineers identity is not available yet";
                }
                return;
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (now < Interlocked.Read(ref _nextAttemptUtcMs)) return;

            int intervalFrames;
            lock (_sync)
            {
                intervalFrames = _snapshot.Linked ? 600 : 120;
                if (!string.IsNullOrWhiteSpace(_snapshot.PairingCode)) intervalFrames = 120;
            }
            if (frame >= _lastFrame && frame - _lastFrame < intervalFrames) return;
            _lastFrame = frame;

            if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0) return;
            string safeName = string.IsNullOrWhiteSpace(displayName) ? "ZeoCore Device" : displayName.Trim();
            if (safeName.Length > 80) safeName = safeName.Substring(0, 80);
            ThreadPool.QueueUserWorkItem(_ => Worker(steamId, seIdentityId, safeName, trust));
        }

        private void Worker(ulong steamId, long seIdentityId, string displayName, ServerTrustSnapshot trust)
        {
            try
            {
                if(_disposed)return;
                HttpResult context = Post("/v1/zeo/device/context", new Dictionary<string, object>
                {
                    { "device_id", _deviceId },
                    { "device_key", _deviceKey }
                });
                if(_disposed)return;

                if (context.Status >= 200 && context.Status < 300)
                {
                    Dictionary<string, object> body = ParseObject(context.Body);
                    bool linked = ReadBool(body, "linked");
                    if (linked)
                    {
                        bool authorized = ReadBool(body, "authorized");
                        TryReportPresence(trust, steamId, seIdentityId);
                        SetLinked(body, authorized, context.Status, steamId, seIdentityId);
                        SuccessDelay(authorized ? 10000 : 5000);
                        return;
                    }

                    // A pending row can survive a game restart. If the local client no
                    // longer has the short code, safely request a fresh single-use code.
                    AccountLinkSnapshot current = Snapshot();
                    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    if (!string.IsNullOrWhiteSpace(current.PairingCode) && current.PairingExpiresUtcMs > now + 5000)
                    {
                        SetPending(current.PairingCode, current.PairingExpiresUtcMs, context.Status);
                        SuccessDelay(2000);
                        return;
                    }

                    StartLink(steamId, seIdentityId, displayName);
                    return;
                }

                string detail = ReadDetail(context.Body);
                if (context.Status == 401 && detail.IndexOf("not linked", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    StartLink(steamId, seIdentityId, displayName);
                    return;
                }
                if (context.Status == 401 && detail.IndexOf("disabled", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    SetState("DEVICE DISABLED", "This ZeoCore device was disabled on the Members page", false, false, context.Status, true);
                    SuccessDelay(30000);
                    return;
                }
                if (context.Status == 401 || context.Status == 403)
                {
                    SetState("AUTH ERROR", string.IsNullOrWhiteSpace(detail) ? "Device authentication was rejected" : detail,
                             false, false, context.Status, true);
                    ErrorDelay();
                    return;
                }

                throw new WebException("Account context HTTP " + context.Status +
                                       (string.IsNullOrWhiteSpace(detail) ? "" : ": " + detail));
            }
            catch (WebException ex)
            {
                SetState("NETWORK ERROR", SafeMessage(ex.Message), Snapshot().Linked, Snapshot().Authorized, 0, false);
                ErrorDelay();
            }
            catch (Exception ex)
            {
                SetState("AUTH ERROR", ex.GetType().Name + ": " + SafeMessage(ex.Message),
                         Snapshot().Linked, Snapshot().Authorized, 0, false);
                ErrorDelay();
            }
            finally
            {
                Interlocked.Exchange(ref _inFlight, 0);
                lock(_sync)_activeRequest=null;
            }
        }

        private void TryReportPresence(ServerTrustSnapshot trust, ulong steamId, long seIdentityId)
        {
            if (trust == null || !trust.MultiplayerActive || trust.ServerId == 0) return;
            string endpoint = !string.IsNullOrWhiteSpace(trust.ObservedEndpoint)
                ? trust.ObservedEndpoint.Trim()
                : (trust.ExpectedEndpoint ?? "").Trim();
            try
            {
                HttpResult result = Post("/v1/zeo/device/presence", new Dictionary<string, object>
                {
                    { "device_id", _deviceId },
                    { "device_key", _deviceKey },
                    { "steam_id", steamId.ToString() },
                    { "se_identity_id", seIdentityId.ToString() },
                    { "endpoint", endpoint },
                    { "sector", trust.Sector ?? "" },
                    { "server_id", trust.ServerId.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                    { "session_name", trust.SessionName ?? "" }
                });
                if (result.Status < 200 || result.Status >= 300)
                    Plugin.Log("DX presence report rejected HTTP " + result.Status + " // " + SafeMessage(ReadDetail(result.Body)));
            }
            catch (Exception ex)
            {
                Plugin.Log("DX presence report deferred: " + SafeMessage(ex.Message));
            }
        }

        private void StartLink(ulong steamId, long seIdentityId, string displayName)
        {
            HttpResult start = Post("/v1/zeo/device/link/start", new Dictionary<string, object>
            {
                { "device_id", _deviceId },
                { "steam_id", steamId.ToString() },
                { "se_identity_id", seIdentityId.ToString() },
                { "display_name", displayName },
                { "device_key", _deviceKey }
            });

            if (start.Status < 200 || start.Status >= 300)
            {
                string detail = ReadDetail(start.Body);
                SetState(start.Status == 403 ? "AUTH ERROR" : "NETWORK ERROR",
                         string.IsNullOrWhiteSpace(detail) ? "Link request HTTP " + start.Status : detail,
                         false, false, start.Status, true);
                if (start.Status == 403) SuccessDelay(30000); else ErrorDelay();
                return;
            }

            Dictionary<string, object> body = ParseObject(start.Body);
            string code = ReadString(body, "link_code") ?? "";
            int expires = (int)Math.Max(60, ReadLong(body, "expires_in", 600));
            if (string.IsNullOrWhiteSpace(code))
                throw new InvalidDataException("Link server did not return a pairing code.");

            long expiresMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + expires * 1000L;
            SetPending(code.Trim().ToUpperInvariant(), expiresMs, start.Status);
            SuccessDelay(2000);
        }

        private void SetLinked(Dictionary<string, object> body, bool authorized, int httpStatus, ulong currentSteamId, long currentIdentityId)
        {
            string serverSteam = ReadString(body, "steam_id") ?? "";
            string serverIdentity = ReadString(body, "se_identity_id") ?? "";
            if ((!string.IsNullOrWhiteSpace(serverSteam) && !serverSteam.Equals(currentSteamId.ToString(), StringComparison.Ordinal)) ||
                (!string.IsNullOrWhiteSpace(serverIdentity) && !serverIdentity.Equals(currentIdentityId.ToString(), StringComparison.Ordinal)))
            {
                SetState("IDENTITY MISMATCH", "Linked device belongs to a different Steam / SE identity", true, false, httpStatus, false);
                SuccessDelay(30000);
                return;
            }

            lock (_sync)
            {
                _snapshot.State = authorized ? "LINKED" : "PENDING FACTION";
                _snapshot.Detail = authorized
                    ? "Device and faction authorization confirmed"
                    : "Device linked; waiting for faction approval";
                _snapshot.Linked = true;
                _snapshot.Authorized = authorized;
                _snapshot.VerifiedSteamId=currentSteamId.ToString();
                _snapshot.VerifiedIdentityId=currentIdentityId.ToString();
                _snapshot.PairingCode = "";
                _snapshot.PairingExpiresUtcMs = 0;
                _snapshot.Username = ReadString(body, "username") ?? "";
                _snapshot.FactionTag = ReadString(body, "faction_tag") ?? "";
                _snapshot.AssignedScope = ReadString(body, "assigned_info_scope") ?? "self";
                _snapshot.EffectiveScope = ReadString(body, "effective_info_scope") ?? "self";
                _snapshot.LastHttpStatus = httpStatus;
                _snapshot.LastSuccessUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                _config.PendingLinkCode = "";
                _config.PendingLinkExpiresUtcMs = 0;
            }
            try { ZeoConfig.Save(_config); } catch (Exception ex) { Plugin.Log("Account link state save failed: " + ex.Message); }
            _errorStreak = 0;
        }

        private void SetPending(string code, long expiresMs, int httpStatus)
        {
            lock (_sync)
            {
                _snapshot.State = "LINK REQUIRED";
                _snapshot.Detail = "Enter this code on Members > ZeoCore Devices";
                _snapshot.Linked = false;
                _snapshot.Authorized = false;
                _snapshot.PairingCode = code ?? "";
                _snapshot.PairingExpiresUtcMs = expiresMs;
                _snapshot.LastHttpStatus = httpStatus;
                _snapshot.LastSuccessUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                _config.PendingLinkCode = _snapshot.PairingCode;
                _config.PendingLinkExpiresUtcMs = _snapshot.PairingExpiresUtcMs;
            }
            try { ZeoConfig.Save(_config); } catch (Exception ex) { Plugin.Log("Pairing code state save failed: " + ex.Message); }
            _errorStreak = 0;
        }

        private void SetState(string state, string detail, bool linked, bool authorized, int status, bool clearCode)
        {
            bool save = false;
            lock (_sync)
            {
                _snapshot.State = state ?? "UNKNOWN";
                _snapshot.Detail = detail ?? "";
                _snapshot.Linked = linked;
                _snapshot.Authorized = authorized;
                _snapshot.LastHttpStatus = status;
                if (clearCode)
                {
                    _snapshot.PairingCode = "";
                    _snapshot.PairingExpiresUtcMs = 0;
                    _config.PendingLinkCode = "";
                    _config.PendingLinkExpiresUtcMs = 0;
                    save = true;
                }
            }
            if (save)
            {
                try { ZeoConfig.Save(_config); } catch (Exception ex) { Plugin.Log("Account error state save failed: " + ex.Message); }
            }
        }

        private HttpResult Post(string path, object payload)
        {
            Uri uri = new Uri(_baseUri, path);
            if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !uri.Host.Equals(ZeoConfig.BattleHost, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing account request to an unpinned host.");

            byte[] bytes = Encoding.UTF8.GetBytes(_json.Serialize(payload));
            var req = (HttpWebRequest)WebRequest.Create(uri);
            req.Method = "POST";
            req.ContentType = "application/json; charset=utf-8";
            req.Accept = "application/json";
            req.UserAgent = "ZeoCore/" + Plugin.Version;
            req.ContentLength = bytes.Length;
            req.Timeout = 6000;
            req.ReadWriteTimeout = 6000;
            req.KeepAlive = true;
            req.AllowAutoRedirect = false;
            lock(_sync){if(_disposed)throw new ObjectDisposedException("AccountLink");_activeRequest=req;}
            using (Stream st = req.GetRequestStream()) st.Write(bytes, 0, bytes.Length);

            try
            {
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return new HttpResult { Status = (int)resp.StatusCode, Body = reader.ReadToEnd() };
            }
            catch (WebException ex)
            {
                var resp = ex.Response as HttpWebResponse;
                if (resp == null) throw;
                using (resp)
                using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return new HttpResult { Status = (int)resp.StatusCode, Body = reader.ReadToEnd() };
            }
        }

        private Dictionary<string, object> ParseObject(string text)
        {
            var body = _json.DeserializeObject(text ?? "") as Dictionary<string, object>;
            if (body == null) throw new InvalidDataException("Account server returned invalid JSON.");
            return body;
        }

        private string ReadDetail(string text)
        {
            try
            {
                Dictionary<string, object> body = ParseObject(text);
                return ReadString(body, "detail") ?? "";
            }
            catch { return ""; }
        }

        private static string ReadString(Dictionary<string, object> body, string key)
        {
            object value;
            if (body == null || !body.TryGetValue(key, out value) || value == null) return null;
            return Convert.ToString(value);
        }

        private static bool ReadBool(Dictionary<string, object> body, string key)
        {
            object value;
            if (body == null || !body.TryGetValue(key, out value) || value == null) return false;
            if (value is bool) return (bool)value;
            bool parsed;
            return bool.TryParse(Convert.ToString(value), out parsed) && parsed;
        }

        private static long ReadLong(Dictionary<string, object> body, string key, long fallback)
        {
            object value;
            if (body == null || !body.TryGetValue(key, out value) || value == null) return fallback;
            try { return Convert.ToInt64(value); } catch { return fallback; }
        }

        private void SuccessDelay(int milliseconds)
        {
            _errorStreak = 0;
            Interlocked.Exchange(ref _nextAttemptUtcMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + milliseconds);
        }

        private void ErrorDelay()
        {
            _errorStreak = Math.Min(6, _errorStreak + 1);
            int seconds = Math.Min(60, 2 << _errorStreak);
            Interlocked.Exchange(ref _nextAttemptUtcMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + seconds * 1000L);
        }

        private static string SafeMessage(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Unknown network error";
            value = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return value.Length <= 180 ? value : value.Substring(0, 180);
        }

        public void Dispose() {lock(_sync){_disposed=true;_activeRequest?.Abort();}}

        private sealed class HttpResult
        {
            public int Status;
            public string Body;
        }
    }
}
