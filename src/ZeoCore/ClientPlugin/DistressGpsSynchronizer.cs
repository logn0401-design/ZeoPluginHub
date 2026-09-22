using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using VRageMath;

namespace ZeoCore
{
    internal sealed class DistressGpsEntry
    {
        public string Description;
        public Vector3D Position;
        public object Handle;
    }

    internal interface IDistressGpsStore
    {
        IList<DistressGpsEntry> Read();
        void Add(string name, string description, Vector3D position);
        void Move(DistressGpsEntry entry, string description, Vector3D position);
    }

    // Only receives the FleetLink snapshot already accepted for the current player/context.
    // Saved points are retained when a call ends; no user GPS is removed or auto-selected.
    internal sealed class DistressGpsSynchronizer
    {
        private sealed class Pending { public bool Seen, Deleted; public long Attempt; }
        private readonly Dictionary<string, Pending> _pending = new Dictionary<string, Pending>();
        private string _context;
        internal void Reset() { _context = null; _pending.Clear(); }

        internal int Sync(string context, FleetPictureSnapshot picture,
            IDistressGpsStore store, long nowMs)
        {
            if (string.IsNullOrEmpty(context) || picture == null) return 0;
            if (_context != context) { Reset(); _context = context; }
            var entries = store.Read();
            var active = new HashSet<string>();
            int writes = 0, processed = 0;
            foreach (var track in picture.Distress)
            {
                if (++processed > 256) break;
                if (track == null || !track.IsDistress || !track.HasPosition || track.EntityId == 0 ||
                    track.DistressExpiresMs <= 0 || !(track.DistressSecondsRemaining > 0) ||
                    !Finite(track.Position)) continue;
                string targetSector = string.IsNullOrWhiteSpace(track.SectorId) ? "unknown" : track.SectorId;
                string sectorLabel = Clean(string.IsNullOrWhiteSpace(track.SectorName) ? targetSector : track.SectorName);
                string marker = Marker(context + "\n" + targetSector, track.EntityId);
                if (!active.Add(marker)) continue;
                Pending pending;
                if (!_pending.TryGetValue(marker, out pending)) _pending[marker] = pending = new Pending();
                DistressGpsEntry existing = null;
                foreach (var entry in entries)
                    if (entry.Description != null && entry.Description.StartsWith(marker + "\n", StringComparison.Ordinal)) { existing = entry; break; }
                string description = marker + "\nZeo distress: " + Clean(track.DistressType) +
                    "\nShip: " + Clean(track.Name) + "\nSector: " + sectorLabel + " (" + Clean(targetSector) + ")" +
                    "\nLast received location: " + DateTimeOffset.FromUnixTimeMilliseconds(nowMs).UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture) +
                    "\nSaved navigation waypoint. The distress call may have ended; verify before routing.";
                if (existing != null)
                {
                    pending.Seen = true;
                    // Retain player edits to name/HUD visibility; update only our location/description.
                    if (Vector3D.DistanceSquared(existing.Position, track.Position) >= 625 &&
                        (pending.Attempt == 0 || nowMs - pending.Attempt >= 5000))
                    {
                        store.Move(existing, description, track.Position);
                        pending.Attempt = nowMs;
                        writes++;
                    }
                }
                else
                {
                    // A player-deleted point stays deleted for the remainder of this active call.
                    if (pending.Seen) pending.Deleted = true;
                    if (pending.Deleted || (pending.Attempt != 0 && nowMs - pending.Attempt < 15000)) continue;
                    store.Add("SOS | " + sectorLabel + " | " + Clean(track.Name) + " | " + track.EntityId.ToString(CultureInfo.InvariantCulture), description, track.Position);
                    pending.Attempt = nowMs;
                    writes++;
                }
            }
            foreach (string key in new List<string>(_pending.Keys)) if (!active.Contains(key)) _pending.Remove(key);
            return writes;
        }

        private static bool Finite(Vector3D v)
        {
            return !(double.IsNaN(v.X) || double.IsNaN(v.Y) || double.IsNaN(v.Z) ||
                double.IsInfinity(v.X) || double.IsInfinity(v.Y) || double.IsInfinity(v.Z));
        }
        private static string Clean(string value)
        {
            var text = new StringBuilder();
            foreach (char c in value ?? "Unknown")
            {
                if (text.Length >= 64) break;
                text.Append(char.IsControl(c) || c == ':' || c == '|' ? ' ' : c);
            }
            return text.ToString().Trim();
        }
        private static string Marker(string context, long source)
        {
            using (var hash = SHA256.Create())
                return "[ZEO-SOS-GPS-v1:" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(context + "\n" + source.ToString(CultureInfo.InvariantCulture)))).Replace("-", "") + "]";
        }
    }
}
