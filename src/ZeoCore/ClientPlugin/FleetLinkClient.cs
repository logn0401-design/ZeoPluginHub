using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using VRageMath;

namespace ZeoCore
{
    internal sealed class FleetLinkClient : IDisposable
    {
        private readonly Uri _endpoint;
        private readonly string _host;
        private readonly string _deviceId;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        private readonly object _sync = new object();
        private FleetPictureSnapshot _snapshot = new FleetPictureSnapshot();
        private long _snapshotTicks;
        private long _generation;
        private string _context = "";
        private bool _disposed;
        private int _inFlight;
        private int _lastFrame = -100000;
        private volatile int _lastStatus;
        private volatile string _lastError = "waiting";
        private volatile string _serverVersion = "";
        private long _received;
        private string _localSectorId = "unknown";
        private string _localSectorName = "UNKNOWN SECTOR";

        public FleetLinkClient(Uri endpoint, string deviceId)
        {
            _endpoint = endpoint ?? throw new ArgumentNullException("endpoint");
            _host = endpoint.Host;
            _deviceId = deviceId ?? "";
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            ServicePointManager.Expect100Continue = false;
        }

        public string Host { get { return _host; } }
        public int LastStatus { get { return _lastStatus; } }
        public string LastError { get { return _lastError; } }
        public string ServerVersion { get { return _serverVersion; } }
        public long Received { get { return Interlocked.Read(ref _received); } }
        public bool Busy { get { return Interlocked.CompareExchange(ref _inFlight, 0, 0) != 0; } }
        public bool Online
        {
            get { lock (_sync) return _lastStatus >= 200 && _lastStatus < 300 &&
                string.IsNullOrEmpty(_lastError) && _snapshotTicks > 0 && ElapsedSeconds(_snapshotTicks) < 10; }
        }

        private static double ElapsedSeconds(long ticks)
        {
            return ticks > 0 ? Math.Max(0, (Stopwatch.GetTimestamp() - ticks) / (double)Stopwatch.Frequency) : 0;
        }

        public void Update(int frame, string localSectorId, string localSectorName, string factionTag, string sessionIdentity = "")
        {
            string sector = string.IsNullOrWhiteSpace(localSectorId) ? "unknown" : localSectorId;
            string name = string.IsNullOrWhiteSpace(localSectorName) ? "UNKNOWN SECTOR" : localSectorName;
            string faction = string.IsNullOrWhiteSpace(factionTag) ? "UNAFFILIATED" : factionTag.Trim().ToUpperInvariant();
            if (faction == "UNAFFILIATED") { ClearForTrustGate("LOCAL ONLY - no game faction"); return; }
            long generation;
            lock (_sync)
            {
                if (_disposed) return;
                string context = sector + "\n" + faction + "\n" + (sessionIdentity ?? "");
                if (_context != context)
                {
                    _context = context;
                    _generation++;
                    _snapshot = new FleetPictureSnapshot();
                    _snapshotTicks = 0;
                    _lastStatus = 0;
                    _lastError = "waiting";
                    _lastFrame = -100000;
                }
                _localSectorId = sector;
                _localSectorName = name;
                int retryFrames = _lastStatus == 401 || _lastStatus == 403 ? 600 : 30;
                if (frame >= _lastFrame && frame - _lastFrame < retryFrames) return;
                if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0) return;
                _lastFrame = frame;
                generation = _generation;
            }
            // Capture context here, never read mutable sector/faction fields in the worker.
            ThreadPool.QueueUserWorkItem(_ => RefreshWorker(generation, faction, sector, name));
        }

        private bool Commit(long generation, FleetPictureSnapshot snapshot, int status, string error)
        {
            lock (_sync)
            {
                if (_disposed || generation != _generation) return false;
                _lastStatus = status;
                _lastError = error;
                if (snapshot != null)
                {
                    _snapshot = snapshot;
                    _snapshotTicks = Stopwatch.GetTimestamp();
                    _serverVersion = "7.1";
                    Interlocked.Increment(ref _received);
                }
                else if (status == 401 || status == 403 || error.StartsWith("InvalidDataException", StringComparison.Ordinal))
                {
                    _snapshot = new FleetPictureSnapshot();
                    _snapshotTicks = 0;
                }
                return true;
            }
        }

        private void RefreshWorker(long generation, string faction, string sector, string sectorName)
        {
            try
            {
                var body = new Dictionary<string, object>
                {
                    { "client_id", _deviceId }, { "faction_tag", faction }, { "world", "default" },
                    { "sector_id", sector }, { "sector_name", sectorName }
                };
                byte[] bytes = Encoding.UTF8.GetBytes(_json.Serialize(body));
                var req = (HttpWebRequest)WebRequest.Create(_endpoint);
                req.Method = "POST";
                req.ContentType = "application/json; charset=utf-8";
                req.Accept = "application/json";
                req.UserAgent = "ZeoCore/" + Plugin.Version;
                req.Timeout = 4500;
                req.ReadWriteTimeout = 4500;
                req.KeepAlive = true;
                req.AllowAutoRedirect = false;
                req.ContentLength = bytes.Length;
                using (Stream st = req.GetRequestStream()) st.Write(bytes, 0, bytes.Length);
                string text;
                int status;
                using (var response = (HttpWebResponse)req.GetResponse())
                {
                    status = (int)response.StatusCode;
                    using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                        text = reader.ReadToEnd();
                }
                if (status < 200 || status >= 300) throw new InvalidOperationException("HTTP " + status);
                Commit(generation, Parse(text), status, "");
            }
            catch (WebException ex)
            {
                int status = 0;
                using (var response = ex.Response as HttpWebResponse)
                    if (response != null) status = (int)response.StatusCode;
                Commit(generation, null, status, status > 0 ? "HTTP " + status : ex.Status.ToString());
            }
            catch (Exception ex)
            {
                Commit(generation, null, 0, ex.GetType().Name + ": " + ex.Message);
            }
            finally { Interlocked.Exchange(ref _inFlight, 0); }
        }

        public FleetPictureSnapshot Snapshot(double staleSeconds = 10, double lastKnownSeconds = 20)
        {
            lock (_sync)
            {
                var copy = _snapshot.Clone();
                double elapsed = ElapsedSeconds(_snapshotTicks);
                foreach (var rows in new[] { copy.Friendlies, copy.Contacts, copy.Roster, copy.Distress })
                {
                    foreach (var row in rows)
                    {
                        row.AgeSeconds += elapsed;
                        row.Stale |= row.AgeSeconds > staleSeconds;
                        row.Online &= !row.Stale;
                        if (row.IsDistress) row.DistressSecondsRemaining = Math.Max(0, row.DistressSecondsRemaining - elapsed);
                    }
                    rows.RemoveAll(row => row.IsDistress
                        ? row.DistressExpiresMs <= 0 || row.DistressSecondsRemaining <= 0
                        : row.AgeSeconds > lastKnownSeconds);
                }
                return copy;
            }
        }

        public void ClearForTrustGate(string reason)
        {
            lock (_sync)
            {
                string error = string.IsNullOrWhiteSpace(reason) ? "account authorization gate" : reason;
                if (_context == "" && _lastError == error) return;
                _generation++;
                _context = "";
                _snapshot = new FleetPictureSnapshot();
                _snapshotTicks = 0;
                _lastStatus = 0;
                _serverVersion = "";
                _lastError = error;
                _lastFrame = -100000;
            }
        }

        private FleetPictureSnapshot Parse(string text)
        {
            var root = _json.DeserializeObject(text) as Dictionary<string, object>;
            if (root == null) throw new InvalidDataException("FleetLink returned non-object JSON");

            string serverVersion = ReadString(root, "version") ?? "";
            if (!string.Equals(serverVersion, "7.1", StringComparison.Ordinal))
                throw new InvalidDataException("server version mismatch: " + (serverVersion.Length == 0 ? "missing" : serverVersion));

            var snap = new FleetPictureSnapshot();
            snap.World = ReadString(root, "world") ?? "default";
            snap.ServerTimeMs = ReadLong(root, "serverTimeMs");
            snap.ReceivedUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            snap.SimSpeed = ReadDouble(root, "simSpeed", 1.0);
            snap.SectorAware = ReadBool(root, "sectorAware") || root.ContainsKey("sectors") || root.ContainsKey("roster");

            ParseRows(root, "friendlies", true, HudTrackSource.FleetFriendly, snap.Friendlies, snap.ServerTimeMs);
            ParseRows(root, "contacts", false, HudTrackSource.FleetContact, snap.Contacts, snap.ServerTimeMs);
            ParseRows(root, "roster", true, HudTrackSource.FleetFriendly, snap.Roster, snap.ServerTimeMs, false);
            ParseDistressRows(root, snap.Distress, snap.ServerTimeMs);
            if (snap.Roster.Count == 0)
                for (int i = 0; i < snap.Friendlies.Count; i++) snap.Roster.Add(snap.Friendlies[i].Clone());
            return snap;
        }

        private static void ParseRows(Dictionary<string, object> root, string key, bool friendly,
            HudTrackSource source, List<HudTrack> output, long serverTimeMs, bool requirePosition = true)
        {
            object raw;
            if (!root.TryGetValue(key, out raw) || raw == null) return;
            var enumerable = raw as IEnumerable;
            if (enumerable == null || raw is string) return;

            foreach (object item in enumerable)
            {
                var row = item as Dictionary<string, object>;
                if (row == null) continue;

                Vector3D position;
                bool hasPosition = ReadVector(row, "position", out position);
                if (!hasPosition && requirePosition) continue;

                Vector3D velocity;
                if (!ReadVector(row, "velocity", out velocity)) velocity = Vector3D.Zero;

                long reporterSourceId = ReadLong(row, "sourceId");
                long id = ReadLong(row, "id");
                if (id == 0) id = reporterSourceId;

                // A live fleet member must be the reporting/piloted grid itself.
                // This rejects legacy replicated-friendly rows where pilot A reported
                // parked/unmanned grid B as a friendly (id B, sourceId A).
                if (friendly)
                {
                    if (reporterSourceId != 0 && id != reporterSourceId) continue;
                    // Roster-only rows without reporter attribution are not enough to
                    // prove an actively piloted grid. Fail closed and let the roster
                    // fall back to verified live friendlies instead.
                    if (!requirePosition && reporterSourceId == 0) continue;
                }

                long captureMs = ReadLong(row, "captureMs");
                double age = serverTimeMs > 0 && captureMs > 0 ? Math.Max(0, (serverTimeMs - captureMs) / 1000.0) : 0;

                string contactType = ReadString(row, "type") ?? "";
                string sensor = ReadString(row, "sensor") ?? "";
                bool signal = !friendly &&
                              (contactType.Equals("signal", StringComparison.OrdinalIgnoreCase) ||
                               contactType.Equals("spectrum", StringComparison.OrdinalIgnoreCase) ||
                               sensor.Equals("spectrum", StringComparison.OrdinalIgnoreCase));
                HudTrackSource actualSource = signal ? HudTrackSource.FleetSignal : source;
                string relation = friendly ? "friendly" : (ReadString(row, "relation") ?? "unknown");
                relation = relation.Trim().ToLowerInvariant();
                if (relation == "signal") relation = "unknown";
                if (relation != "friendly" && relation != "hostile" && relation != "unknown" &&
                    relation != "ordnance" && relation != "wreck" && relation != "debris")
                    relation = "unknown";
                bool resolvedFriendly = friendly || relation == "friendly";

                output.Add(new HudTrack
                {
                    Key = (friendly ? "F:" : (signal ? "FS:" : "C:")) + id.ToString(CultureInfo.InvariantCulture),
                    EntityId = id,
                    ReporterSourceId = reporterSourceId,
                    Name = ReadString(row, "name") ?? "",
                    Relation = relation,
                    ContactType = contactType,
                    Source = actualSource,
                    Position = position,
                    Velocity = velocity,
                    Threat = ReadDouble(row, "threat", 0),
                    SignalStrength = ReadNullableNumber(row, "signalStrength"),
                    RawEmitterId = ReadString(row, "rawEmitterId"),
                    Focused = row.ContainsKey("focus") && row["focus"] != null && !string.IsNullOrEmpty(Convert.ToString(row["focus"], CultureInfo.InvariantCulture)),
                    Stale = ReadBool(row, "stale"),
                    AgeSeconds = age,
                    Friendly = resolvedFriendly,
                    SectorId = ReadSectorId(row),
                    SectorName = ReadSectorName(row),
                    SectorKnown = !string.IsNullOrWhiteSpace(ReadSectorId(row)) && !string.Equals(ReadSectorId(row), "unknown", StringComparison.OrdinalIgnoreCase),
                    Online = !row.ContainsKey("online") || ReadBool(row, "online"),
                    HasPosition = hasPosition,
                    ShipHp = ReadDouble(row, "shipHp", -1),
                    DriveHealth = ReadDouble(row, "driveHealth", -1),
                    PowerLoad = ReadDouble(row, "powerLoad", -1)
                });
            }
        }


        private static void ParseDistressRows(Dictionary<string, object> root, List<HudTrack> output, long serverTimeMs)
        {
            object raw;
            if (!root.TryGetValue("distress", out raw) || raw == null) return;
            var enumerable = raw as IEnumerable;
            if (enumerable == null || raw is string) return;
            long now = serverTimeMs > 0 ? serverTimeMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            foreach (object item in enumerable)
            {
                var row = item as Dictionary<string, object>;
                if (row == null) continue;
                long expires = ReadLong(row, "expiresMs");
                if (expires <= 0) expires = ReadLong(row, "expiresAtMs");
                if (expires <= now) continue;
                Vector3D position; bool hasPosition = ReadVector(row, "position", out position);
                Vector3D velocity; if (!ReadVector(row, "velocity", out velocity)) velocity = Vector3D.Zero;
                long id = ReadLong(row, "id"); if (id == 0) id = ReadLong(row, "sourceId"); if (id == 0) id = ReadLong(row, "shipId");
                long captureMs=ReadLong(row,"captureMs");
                double age=serverTimeMs>0 && captureMs>0 ? Math.Max(0,(serverTimeMs-captureMs)/1000.0) : 0;
                output.Add(new HudTrack
                {
                    Key="D:"+id.ToString(CultureInfo.InvariantCulture), EntityId=id, ReporterSourceId=ReadLong(row,"sourceId"), Name=ReadString(row,"name") ?? ReadString(row,"shipName") ?? "DISTRESS",
                    Relation="distress", Source=HudTrackSource.FleetFriendly, Position=position, Velocity=velocity, Friendly=true,
                    AgeSeconds=age, SectorId=ReadSectorId(row), SectorName=ReadSectorName(row),
                    SectorKnown=!string.IsNullOrWhiteSpace(ReadSectorId(row)) && !string.Equals(ReadSectorId(row),"unknown",StringComparison.OrdinalIgnoreCase),
                    Online=true, HasPosition=hasPosition, ShipHp=ReadDouble(row,"shipHp",-1), DriveHealth=ReadDouble(row,"driveHealth",-1), PowerLoad=ReadDouble(row,"powerLoad",-1),
                    IsDistress=true, DistressType=ReadString(row,"type") ?? "GENERAL SOS", DistressExpiresMs=expires, DistressSecondsRemaining=expires>0 ? Math.Max(0,(expires-now)/1000.0) : 0
                });
            }
        }

        private static double? ReadNullableNumber(Dictionary<string, object> row, string key)
        {
            object raw;
            double value;
            if (!row.TryGetValue(key, out raw) || raw == null || raw is bool ||
                !double.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out value) || double.IsNaN(value) || double.IsInfinity(value)) return null;
            return value;
        }

        private static string ReadSectorId(Dictionary<string, object> row)
        {
            string direct = ReadString(row, "sectorId");
            if (!string.IsNullOrWhiteSpace(direct)) return direct;
            object raw;
            if (row.TryGetValue("sector", out raw))
            {
                var obj = raw as Dictionary<string, object>;
                if (obj != null) return ReadString(obj, "id") ?? ReadString(obj, "sectorId");
            }
            return null;
        }

        private static string ReadSectorName(Dictionary<string, object> row)
        {
            string direct = ReadString(row, "sectorName");
            if (!string.IsNullOrWhiteSpace(direct)) return direct;
            object raw;
            if (row.TryGetValue("sector", out raw))
            {
                var obj = raw as Dictionary<string, object>;
                if (obj != null) return ReadString(obj, "name") ?? ReadString(obj, "sectorName");
            }
            return "UNKNOWN SECTOR";
        }

        private static bool ReadVector(Dictionary<string, object> row, string key, out Vector3D value)
        {
            value = Vector3D.Zero;
            object raw;
            if (!row.TryGetValue(key, out raw) || raw == null) return false;
            var list = raw as IEnumerable;
            if (list == null || raw is string) return false;
            var values = new List<double>(3);
            foreach (object item in list)
            {
                values.Add(ToDouble(item));
                if (values.Count == 3) break;
            }
            if (values.Count != 3) return false;
            value = new Vector3D(values[0], values[1], values[2]);
            return true;
        }

        private static string ReadString(Dictionary<string, object> row, string key)
        {
            object value;
            return row.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;
        }

        private static long ReadLong(Dictionary<string, object> row, string key)
        {
            object value;
            return row.TryGetValue(key, out value) ? ToLong(value) : 0;
        }

        private static double ReadDouble(Dictionary<string, object> row, string key, double fallback)
        {
            object value;
            return row.TryGetValue(key, out value) && value != null ? ToDouble(value) : fallback;
        }

        private static bool ReadBool(Dictionary<string, object> row, string key)
        {
            object value;
            if (!row.TryGetValue(key, out value) || value == null) return false;
            if (value is bool) return (bool)value;
            bool parsed;
            return bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out parsed) && parsed;
        }

        private static long ToLong(object value)
        {
            if (value == null) return 0;
            try { return Convert.ToInt64(value, CultureInfo.InvariantCulture); }
            catch
            {
                long parsed;
                return long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any,
                    CultureInfo.InvariantCulture, out parsed) ? parsed : 0;
            }
        }

        private static double ToDouble(object value)
        {
            if (value == null) return 0;
            try { return Convert.ToDouble(value, CultureInfo.InvariantCulture); }
            catch
            {
                double parsed;
                return double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any,
                    CultureInfo.InvariantCulture, out parsed) ? parsed : 0;
            }
        }

        public void Dispose()
        {
            ClearForTrustGate("disposed");
            lock (_sync) { _disposed = true; _generation++; }
        }
    }
}
