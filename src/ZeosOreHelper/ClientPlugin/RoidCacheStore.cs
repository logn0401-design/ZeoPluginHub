using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using VRageMath;

namespace ZeosOreHelper
{
    public sealed class RoidCacheStore
    {
        private sealed class CacheEntry
        {
            public string Key = "";
            public string Sector = "";
            public long UtcTicks;
            public string Grade = "X";
            public double Quality;
            public Vector3D Position;
            public double Diameter;
            public bool Pinned;
            public string OreSummary = "";
        }

        private readonly Dictionary<string, CacheEntry> _entries = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly HudSettings _settings;
        private bool _dirty;
        private int _lastFlushFrame = -10000;

        public static string CachePath { get { return Path.Combine(HudSettings.SettingsDirectory, "roid_cache.tsv"); } }
        public int Count { get { return _entries.Count; } }

        public RoidCacheStore(HudSettings settings)
        {
            _settings = settings ?? new HudSettings();
            Load();
        }

        public void ApplyFlags(VoxelSurveyor.RoidRecord r)
        {
            if (r == null) return;
            CacheEntry e;
            if (_entries.TryGetValue(BuildKey(r), out e) && Vector3D.DistanceSquared(e.Position,r.Position)<100){r.HistoricalLead=true;if(e.Pinned)r.Pinned=true;}
        }

        public void Update(VoxelSurveyor.RoidRecord r)
        {
            if (r == null || !_settings.CacheEnabled) return;
            if (r.State != VoxelSurveyor.SurveyState.Ready && r.State != VoxelSurveyor.SurveyState.NoOre) return;

            var shouldKeep = r.Pinned ||
                             HudSettings.GradeRank(r.Grade) >= HudSettings.GradeRank(_settings.CacheMinimumGrade);
            var key = BuildKey(r);

            if (!shouldKeep)
            {
                CacheEntry old;
                if (_entries.TryGetValue(key, out old) && !old.Pinned)
                {
                    _entries.Remove(key);
                    _dirty = true;
                }
                return;
            }

            CacheEntry e;
            if (!_entries.TryGetValue(key, out e))
            {
                e = new CacheEntry { Key = key };
                _entries[key] = e;
            }

            e.Sector = SectorKey(r.Position);
            e.UtcTicks = DateTime.UtcNow.Ticks;
            e.Grade = r.Grade ?? "X";
            e.Quality = r.QualityIndex;
            e.Position = r.Position;
            e.Diameter = r.MaxDimensionMeters;
            e.Pinned = r.Pinned;
            e.OreSummary = BuildOreSummary(r);
            _dirty = true;
        }

        public void RemoveIfUnpinned(VoxelSurveyor.RoidRecord r)
        {
            if (r == null || r.Pinned) return;
            if (HudSettings.GradeRank(r.Grade) >= HudSettings.GradeRank(_settings.CacheMinimumGrade)) return;
            var key = BuildKey(r);
            if (_entries.Remove(key)) _dirty = true;
        }

        public void Tick(int frame)
        {
            if (!_settings.CacheEnabled) return;
            if (!_dirty || frame - _lastFlushFrame < 300) return;
            _lastFlushFrame = frame;
            Prune(false);
            Save();
        }

        public void FlushNow()
        {
            if (!_settings.CacheEnabled) return;
            Prune(false);
            Save();
        }

        public void Reevaluate(IEnumerable<VoxelSurveyor.RoidRecord> records)
        {
            if (records == null) return;
            foreach (var r in records) Update(r);
            Prune(false);
            Save();
        }

        public int ClearCurrentSector(Vector3D position)
        {
            var sector = SectorKey(position);
            var remove = _entries.Values.Where(x => x.Sector == sector && !x.Pinned).Select(x => x.Key).ToList();
            for (var i = 0; i < remove.Count; i++) _entries.Remove(remove[i]);
            if (remove.Count > 0) { _dirty = true; Save(); }
            return remove.Count;
        }

        public int ClearOld()
        {
            var before = _entries.Count;
            Prune(true);
            if (_entries.Count != before) Save();
            return before - _entries.Count;
        }

        public int ClearAll(bool keepPinned)
        {
            var before = _entries.Count;
            if (keepPinned)
            {
                var keep = _entries.Values.Where(x => x.Pinned).ToList();
                _entries.Clear();
                for (var i = 0; i < keep.Count; i++) _entries[keep[i].Key] = keep[i];
            }
            else _entries.Clear();
            _dirty = true;
            Save();
            return before - _entries.Count;
        }

        public void SettingsChanged()
        {
            Prune(false);
            Save();
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(CachePath)) return;
                var lines = File.ReadAllLines(CachePath);
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    var p = line.Split('\t');
                    if (p.Length < 12) continue;
                    long ticks; double quality, x, y, z, diameter; bool pinned;
                    if (!long.TryParse(p[2], out ticks)) continue;
                    if (!double.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out quality)) continue;
                    if (!double.TryParse(p[5], NumberStyles.Float, CultureInfo.InvariantCulture, out x)) continue;
                    if (!double.TryParse(p[6], NumberStyles.Float, CultureInfo.InvariantCulture, out y)) continue;
                    if (!double.TryParse(p[7], NumberStyles.Float, CultureInfo.InvariantCulture, out z)) continue;
                    if (!double.TryParse(p[8], NumberStyles.Float, CultureInfo.InvariantCulture, out diameter)) continue;
                    if (!bool.TryParse(p[9], out pinned)) pinned = false;
                    var e = new CacheEntry
                    {
                        Key = Unescape(p[0]),
                        Sector = Unescape(p[1]),
                        UtcTicks = ticks,
                        Grade = HudSettings.NormalizeGrade(p[3], "X"),
                        Quality = quality,
                        Position = new Vector3D(x, y, z),
                        Diameter = diameter,
                        Pinned = pinned,
                        OreSummary = Unescape(p[10])
                    };
                    // p[11] is reserved for forward-compatible flags.
                    _entries[e.Key] = e;
                }
                Prune(false);
                Plugin.Log("Roid cache loaded: " + _entries.Count + " entries.");
            }
            catch (Exception ex)
            {
                Plugin.Log("Roid cache load failed: " + ex.Message);
                _entries.Clear();
            }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(HudSettings.SettingsDirectory);
                var text = Serialize();
                var maxBytes = Math.Max(1, _settings.CacheMaxMb) * 1024L * 1024L;
                if (Encoding.UTF8.GetByteCount(text) > maxBytes)
                {
                    PruneToByteLimit(maxBytes);
                    text = Serialize();
                }
                File.WriteAllText(CachePath, text, Encoding.UTF8);
                _dirty = false;
            }
            catch (Exception ex) { Plugin.Log("Roid cache save failed: " + ex.Message); }
        }

        private string Serialize()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Zeos Ore Helper v0.5.1 bounded roid cache");
            sb.AppendLine("# key\tsector\tutcTicks\tgrade\tquality\tx\ty\tz\tdiameter\tpinned\tores\tflags");
            foreach (var e in _entries.Values.OrderByDescending(x => x.Pinned).ThenByDescending(x => x.Quality))
            {
                sb.Append(Escape(e.Key)).Append('\t')
                  .Append(Escape(e.Sector)).Append('\t')
                  .Append(e.UtcTicks).Append('\t')
                  .Append(e.Grade).Append('\t')
                  .Append(e.Quality.ToString("0.000", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(e.Position.X.ToString("0.0", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(e.Position.Y.ToString("0.0", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(e.Position.Z.ToString("0.0", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(e.Diameter.ToString("0.0", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(e.Pinned).Append('\t')
                  .Append(Escape(e.OreSummary)).Append('\t')
                  .Append("-")
                  .AppendLine();
            }
            return sb.ToString();
        }

        private void Prune(bool ageOnly)
        {
            var now = DateTime.UtcNow;
            var cutoff = now.AddDays(-Math.Max(1, _settings.CacheRetentionDays)).Ticks;

            var old = _entries.Values
                .Where(x => !x.Pinned && x.UtcTicks > 0 && x.UtcTicks < cutoff)
                .Select(x => x.Key).ToList();
            for (var i = 0; i < old.Count; i++) _entries.Remove(old[i]);

            if (!ageOnly)
            {
                var sectors = _entries.Values.Select(x => x.Sector).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var perSector = Math.Max(5, _settings.CachePerSector);
                for (var s = 0; s < sectors.Count; s++)
                {
                    var sector = sectors[s];
                    var candidates = _entries.Values
                        .Where(x => x.Sector == sector && !x.Pinned)
                        .OrderByDescending(x => HudSettings.GradeRank(x.Grade))
                        .ThenByDescending(x => x.Quality)
                        .ThenByDescending(x => x.UtcTicks)
                        .ToList();
                    for (var i = perSector; i < candidates.Count; i++)
                        _entries.Remove(candidates[i].Key);
                }
            }

            _dirty = true;
        }

        private void PruneToByteLimit(long maxBytes)
        {
            var nonPinned = _entries.Values
                .Where(x => !x.Pinned)
                .OrderBy(x => HudSettings.GradeRank(x.Grade))
                .ThenBy(x => x.Quality)
                .ThenBy(x => x.UtcTicks)
                .ToList();

            for (var i = 0; i < nonPinned.Count; i++)
            {
                if (Encoding.UTF8.GetByteCount(Serialize()) <= maxBytes) break;
                _entries.Remove(nonPinned[i].Key);
            }
        }

        public static string SectorKey(Vector3D position)
        {
            // 100 km spatial cells are intentionally simple and stable across
            // seamless server/sector handoffs.
            var x = (long)Math.Floor(position.X / 100000.0);
            var y = (long)Math.Floor(position.Y / 100000.0);
            var z = (long)Math.Floor(position.Z / 100000.0);
            return x + "," + y + "," + z;
        }

        private static string BuildKey(VoxelSurveyor.RoidRecord r)
        {
            if (r == null) return "";
            if (!string.IsNullOrWhiteSpace(r.StorageName)) return "S:" + r.StorageName;
            return "E:" + r.EntityId;
        }

        private static string BuildOreSummary(VoxelSurveyor.RoidRecord r)
        {
            if (r == null || r.Ores.Count == 0) return "";
            var sb = new StringBuilder();
            for (var i = 0; i < r.Ores.Count; i++)
            {
                if (i > 0) sb.Append(';');
                sb.Append(r.Ores[i].Ore).Append('=')
                  .Append(r.Ores[i].PercentOfSolid.ToString("0.000", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        private static string Escape(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "").Replace("\n", "\\n");
        }

        private static string Unescape(string s)
        {
            if (s == null) return "";
            return s.Replace("\\t", "\t").Replace("\\n", "\n").Replace("\\\\", "\\");
        }
    }
}
