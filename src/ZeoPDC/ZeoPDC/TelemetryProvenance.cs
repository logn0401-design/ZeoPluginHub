using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ZeoPDC
{
    // Used under the recorder's ioGate. Metadata never implies hardware acknowledgement.
    internal sealed class TelemetryProvenance
    {
        public const string Timebase = "PLUGIN_UPDATE_COUNTER_60HZ_ASSUMED";
        public const string Columns = "session_id,config_revision,sim_tick,sim_time_s,timebase";
        public readonly string SessionId = Guid.NewGuid().ToString("N");
        public string Revision { get; private set; }
        readonly string folder;
        readonly Func<int?> tick;
        readonly Dictionary<string, string[]> headers = new Dictionary<string, string[]>();
        string effectiveJson;
        int revision;
        public TelemetryProvenance(string folder, Func<int?> tick, PdcConfig effective, PdcConfig requested)
        {
            this.folder = folder; this.tick = tick;
            RecordConfiguration(effective, requested, "SESSION_START");
            Create(Path.Combine(folder, "session.json"), "{\"schema\":\"zeo.pdc.telemetry.v6\",\"session_id\":" + Q(SessionId) +
                ",\"plugin_version\":" + Q(Plugin.Version) + ",\"created_utc\":" + Q(DateTime.UtcNow.ToString("o")) +
                ",\"initial_config_revision\":" + Q(Revision) + ",\"timebase\":" + Q(Timebase) +
                ",\"ticks_per_second_assumed\":60,\"start_sim_tick\":" + N(tick()) +
                ",\"engine_sim_tick\":null,\"source_timebase\":null,\"upstream_capture_timestamps_available\":false," +
                "\"timebase_notes\":\"Plugin.Update counter since plugin startup; not verified engine ticks, UTC or wall duration. Pauses and sim-speed mapping are unverified.\"," +
                "\"configuration_scope\":\"Effective plugin configuration; weapon application requires command results and terminal readback.\"," +
                "\"csv_empty_value\":\"UNKNOWN_OR_UNAVAILABLE\",\"json_id_type\":\"string\",\"flush_every_plugin_ticks\":60," +
                "\"observation_notes\":\"Observation time is local API-read time. Source capture time/age remain unknown. A retained position is not a fresh observation.\"}");
        }
        public void RecordConfiguration(PdcConfig effective, PdcConfig requested, string reason)
        {
            string current = Encoding.UTF8.GetString(JsonIo.ToBytes(effective));
            if (current == effectiveJson) return;
            string next = "cfg-" + (revision + 1).ToString("D6", CultureInfo.InvariantCulture);
            string digest;
            using (var sha = SHA256.Create()) digest = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(current))).Replace("-", "").ToLowerInvariant();
            Directory.CreateDirectory(Path.Combine(folder, "configs"));
            // Publish the durable immutable snapshot before its revision can be emitted in rows.
            Create(Path.Combine(folder, "configs", next + ".json"), "{\"schema\":\"zeo.pdc.config.v1\",\"session_id\":" + Q(SessionId) +
                ",\"config_revision\":" + Q(next) + ",\"effective_from_sim_tick\":" + N(tick()) + ",\"timebase\":" + Q(Timebase) +
                ",\"reason\":" + Q(reason) + ",\"effective_settings_sha256\":" + Q(digest) +
                ",\"requested_status\":" + Q(requested == null ? "NOT_RECORDED" : "CAPTURED_BEFORE_NORMALIZATION") +
                ",\"requested_settings\":" + (requested == null ? "null" : Encoding.UTF8.GetString(JsonIo.ToBytes(requested))) +
                ",\"effective_settings\":" + current + ",\"hardware_applied_settings\":null," +
                "\"hardware_application_status\":\"USE_PER_GUN_COMMAND_AND_READBACK_EVIDENCE\"}");
            revision++; Revision = next; effectiveJson = current;
        }
        public string JsonFields(int? at = null)
        {
            var t = at ?? tick();
            return "\"session_id\":" + Q(SessionId) + ",\"config_revision\":" + Q(Revision) + ",\"sim_tick\":" + N(t) +
                ",\"sim_time_s\":" + (t.HasValue ? Number(t.Value / 60.0) : "null") + ",\"timebase\":" + Q(Timebase);
        }
        public string TextFields()
        { return "\r\nSessionId=" + SessionId + "\r\nConfigRevision=" + Revision + "\r\nSimTick=" + (tick()?.ToString(CultureInfo.InvariantCulture) ?? "UNKNOWN") + "\r\nTimebase=" + Timebase + "\r\n"; }
        public string StampCsv(string file, string text, bool reset)
        {
            if (reset) headers.Remove(file);
            var output = new StringBuilder();
            foreach (var row in Records(text))
            {
                string[] header;
                if (!headers.TryGetValue(file, out header))
                {
                    headers[file] = ParseRow(row);
                    output.Append(row).Append(',').Append(Columns).Append("\r\n");
                    continue;
                }
                int? at = tick();
                int index = Array.IndexOf(header, "time_s");
                if (index < 0) index = Array.IndexOf(header, "plugin_time_s");
                if (index >= 0)
                {
                    var values = ParseRow(row); double time;
                    at = index < values.Length && double.TryParse(values[index], NumberStyles.Float, CultureInfo.InvariantCulture, out time) &&
                        BankPlanner.Finite(time) && time >= 0 && time * 60 <= int.MaxValue ? (int?)Math.Round(time * 60) : null;
                }
                output.Append(row).Append(',').Append(SessionId).Append(',').Append(Revision).Append(',')
                    .Append(at.HasValue ? at.Value.ToString(CultureInfo.InvariantCulture) : "").Append(',')
                    .Append(at.HasValue ? Number(at.Value / 60.0) : "").Append(',').Append(Timebase).Append("\r\n");
            }
            return output.ToString();
        }
        // Keep quoted multiline values intact when stamping batches of CSV records.
        internal static IEnumerable<string> Records(string text)
        {
            bool quoted = false; int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '"') { if (quoted && i + 1 < text.Length && text[i + 1] == '"') i++; else quoted = !quoted; }
                else if (!quoted && (text[i] == '\r' || text[i] == '\n'))
                {
                    if (i > start) yield return text.Substring(start, i - start);
                    if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    start = i + 1;
                }
            }
            if (quoted) throw new InvalidDataException("Incomplete quoted CSV record");
            if (start < text.Length) yield return text.Substring(start);
        }
        internal static string[] ParseRow(string row)
        {
            var cells = new List<string>(); var cell = new StringBuilder(); bool quoted = false;
            for (int i = 0; i < row.Length; i++)
            {
                char c = row[i];
                if (c == '"') { if (quoted && i + 1 < row.Length && row[i + 1] == '"') { cell.Append('"'); i++; } else quoted = !quoted; }
                else if (c == ',' && !quoted) { cells.Add(cell.ToString()); cell.Clear(); }
                else cell.Append(c);
            }
            cells.Add(cell.ToString()); return cells.ToArray();
        }
        internal static string ObservationCsv(int? logged, int? observed, string source)
        {
            bool known = logged.HasValue && observed.HasValue && observed >= 0 && observed <= logged;
            return (known ? observed.Value.ToString(CultureInfo.InvariantCulture) : "") + "," +
                (known ? Number(observed.Value / 60.0) : "") + "," + (known ? Number((logged.Value - observed.Value) / 60.0) : "") + "," +
                source + "," + (known ? (observed == logged ? "AVAILABLE" : "RETAINED") : "UNKNOWN") + "," +
                (known ? (observed == logged ? "API_READ_THIS_TICK" : "RETAINED") : "UNKNOWN") + ",,,,UNKNOWN";
        }
        public const string ObservationColumns = "observation_tick,observation_time_s,observation_age_s,observation_source,observation_availability,observation_freshness,source_tick,source_time_s,source_age_s,source_freshness";
        static string N(int? n) { return n.HasValue ? n.Value.ToString(CultureInfo.InvariantCulture) : "null"; }
        static string Number(double n) { return n.ToString("R", CultureInfo.InvariantCulture); }
        internal static string Q(string value) { return Encoding.UTF8.GetString(JsonIo.ToBytes(value)); }
        static void Create(string path, string text)
        { using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(text); }
    }
}
