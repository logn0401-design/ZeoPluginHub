using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace ZeoCore
{
    internal sealed class DistressGpsBridge
    {
        private readonly DistressGpsSynchronizer _sync = new DistressGpsSynchronizer();
        private object _session;
        private long _nextCheck;
        internal void Reset() { _session = null; _nextCheck = 0; _sync.Reset(); }

        // Called from the simulation update thread, never from the HTTP worker.
        internal void Update(FleetLinkClient fleet, bool enabled, string faction)
        {
            var session = MyAPIGateway.Session;
            if (!ReferenceEquals(_session, session)) { Reset(); _session = session; }
            if (session == null || session.Player == null || session.GPS == null || session.Player.IdentityId == 0) return;
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (now < _nextCheck) return;
            _nextCheck = now + 5000;
            if (!enabled || fleet == null || !fleet.Online || string.IsNullOrWhiteSpace(faction) || faction == "UNAFFILIATED") return;
            try
            {
                long player = session.Player.IdentityId;
                var picture = fleet.Snapshot();
                // Receiver sector/session names change during transfers; do not include them in GPS ownership.
                string owner = session.Player.SteamUserId != 0 ? session.Player.SteamUserId.ToString() : player.ToString();
                string context = fleet.Host + "\n" + picture.World + "\n" + owner + "\n" + faction;
                int count = _sync.Sync(context, picture, new GameStore(session.GPS, player), now);
                if (count > 0) Plugin.Log("Distress GPS: saved/updated " + count + " navigation waypoint(s).");
            }
            catch (Exception ex) { Plugin.Log("Distress GPS update: " + ex.GetType().Name); }
        }

        private sealed class GameStore : IDistressGpsStore
        {
            private readonly IMyGpsCollection _gps;
            private readonly long _player;
            internal GameStore(IMyGpsCollection gps, long player) { _gps = gps; _player = player; }
            public IList<DistressGpsEntry> Read()
            {
                var entries = new List<IMyGps>();
                _gps.GetGpsList(_player, entries);
                var result = new List<DistressGpsEntry>();
                foreach (var entry in entries) if (entry != null)
                    result.Add(new DistressGpsEntry { Description = entry.Description, Position = entry.Coords, Handle = entry });
                return result;
            }
            public void Add(string name, string description, Vector3D position)
            {
                // AddGps persists to the player's world save. AddLocalGps does not save.
                var entry = _gps.Create(name, description, position, false, false);
                entry.ShowOnHud = false;
                entry.DiscardAt = null;
                _gps.AddGps(_player, entry);
            }
            public void Move(DistressGpsEntry entry, string description, Vector3D position)
            {
                var gps = (IMyGps)entry.Handle;
                gps.Coords = position;
                gps.Description = description;
                // Keep the original hash: ModifyGps handles the coordinate/hash transition.
                _gps.ModifyGps(_player, gps);
            }
        }
    }
}
