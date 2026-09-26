using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Sandbox.ModAPI;

namespace ZeoCore
{
    internal sealed class ServerTrustSnapshot
    {
        public string State = "OFFLINE";
        public string Sector = "UNKNOWN";
        public string SessionName = "";
        public string ExpectedEndpoint = "";
        public string ObservedEndpoint = "";
        public ulong ServerId;
        public bool MultiplayerActive;
        public bool IdentityVerified;
        public bool EndpointVerified;
        public bool NetworkAllowed;
        public string Detail = "No multiplayer session";

        public ServerTrustSnapshot Clone()
        {
            return (ServerTrustSnapshot)MemberwiseClone();
        }

        public string Fingerprint()
        {
            return State + "|" + Sector + "|" + SessionName + "|" + ServerId + "|" +
                   ExpectedEndpoint + "|" + ObservedEndpoint + "|" +
                   IdentityVerified + "|" + EndpointVerified + "|" + NetworkAllowed;
        }
    }

    // v0.5.9 SDX2 server identity/context observer. Approved device membership, not this snapshot, is the normal gameplay authorization gate.
    //
    // Important ordering fix:
    //   1) If the real remote endpoint is observable, an exact approved SDX2
    //      38.107.232.37:27335-27346 endpoint is authoritative and determines sector.
    //   2) Only when SE/Steam does not expose that endpoint do we fall back to the
    //      session/world name plus the non-zero Steam server identity.
    //
    // A broad reflection probe can encounter query/peer/auxiliary endpoints, so seeing
    // the approved host on some unrelated port is NOT by itself grounds for rejection.
    // Only an exact allowlisted endpoint is treated as endpoint verification.
    //
    // Account/device pairing is intentionally independent from this tactical trust gate;
    // this class gates tactical network use, not a member's ability to link their account.
    internal static class ServerTrust
    {
        private const string ApprovedHost = "38.107.232.37";
        private const int LobbyPort = 27335;
        private const int LastDxPort = 27346;

        // Explicit Expanse Steam server identities for diagnostics and one-time application presence.
        private const ulong ExpanseLobbySteamServerId = 90291207542525968UL;
        private const ulong ExpanseLiveSteamServerId = 90291827970980880UL;

        private static readonly Regex DxNumber = new Regex(
            @"(?:^|[^A-Z0-9])DX\s*(11|10|[1-9])(?:[^A-Z0-9]|$)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex IpPort = new Regex(
            @"(?<!\d)(\d{1,3}(?:\.\d{1,3}){3}):(\d{2,5})(?!\d)",
            RegexOptions.Compiled);

        private sealed class ProbeNode
        {
            public object Value;
            public int Depth;
        }

        public static ServerTrustSnapshot Capture()
        {
            var snap = new ServerTrustSnapshot();

            try
            {
                var mp = MyAPIGateway.Multiplayer;
                var session = MyAPIGateway.Session;

                if (mp == null || session == null)
                {
                    snap.Detail = "Multiplayer/session unavailable";
                    return snap;
                }

                bool active = false;
                try { active = mp.MultiplayerActive; } catch { }
                snap.MultiplayerActive = active;

                ulong serverId = 0;
                try { serverId = mp.ServerId; } catch { }
                snap.ServerId = serverId;

                string onlineMode = "";
                try { onlineMode = session.OnlineMode.ToString(); } catch { }

                string sessionName = "";
                try { sessionName = session.Name ?? ""; } catch { }
                snap.SessionName = sessionName.Trim();

                if (!active || string.Equals(
                    onlineMode,
                    "OFFLINE",
                    StringComparison.OrdinalIgnoreCase))
                {
                    snap.State = "OFFLINE";
                    snap.Detail = "Not connected to a multiplayer server";
                    return snap;
                }

                // Probe both roots. Different SE/Steam builds expose the useful remote
                // endpoint from different objects.
                var endpoints = new List<IPEndPoint>();
                MergeEndpoints(endpoints, ProbeRemoteEndpoints(mp));
                MergeEndpoints(endpoints, ProbeRemoteEndpoints(session));

                string endpointSector;
                int endpointPort;
                IPEndPoint exactEndpoint;

                if (TryFindApprovedEndpoint(
                    endpoints,
                    out exactEndpoint,
                    out endpointSector,
                    out endpointPort))
                {
                    snap.Sector = endpointSector;
                    snap.ExpectedEndpoint = ApprovedHost + ":" + endpointPort;
                    snap.ObservedEndpoint = ApprovedHost + ":" + endpointPort;
                    snap.EndpointVerified = true;

                    if (serverId == 0)
                    {
                        snap.State = "VERIFYING SERVER";
                        snap.NetworkAllowed = false;
                        snap.Detail = "Exact approved DX endpoint found; waiting for Steam server identity";
                        return snap;
                    }

                    snap.IdentityVerified = true;
                    snap.NetworkAllowed = true;
                    snap.State = "AUTHORIZED";
                    snap.Detail = "Exact DX endpoint + Steam server identity verified";
                    return snap;
                }

                // Preserve one useful endpoint for diagnostics when available, but do
                // not treat broad reflection results as proof of an invalid server.
                snap.ObservedEndpoint = FirstUsefulEndpoint(endpoints);

                // Space Engineers may hide the remote endpoint while still exposing ServerId.
                if (serverId == ExpanseLobbySteamServerId || serverId == ExpanseLiveSteamServerId)
                {
                    snap.Sector = serverId == ExpanseLobbySteamServerId ? "DX_Lobby" : "EXPANSE";
                    snap.IdentityVerified = true;
                    snap.NetworkAllowed = true;
                    snap.State = "AUTHORIZED";
                    snap.Detail = "Approved Expanse Steam server identity verified; endpoint hidden by client API";
                    return snap;
                }

                int namedPort;
                string namedSector;
                if (TryMapDxSector(sessionName, out namedSector, out namedPort))
                {
                    snap.Sector = namedSector;
                    snap.ExpectedEndpoint = ApprovedHost + ":" + namedPort;

                    if (serverId == 0)
                    {
                        snap.State = "VERIFYING SERVER";
                        snap.Detail = "DX session recognized; waiting for Steam server identity";
                        return snap;
                    }

                    snap.IdentityVerified = true;
                    snap.NetworkAllowed = true;
                    snap.State = "AUTHORIZED";
                    snap.Detail = "DX session + Steam server identity verified; endpoint not exposed by client API";
                    return snap;
                }

                // v0.5.9.1 LAB: DX is context only. Do not expose a VERIFYING DX
                // gameplay-looking gate while transport features are being isolated.
                snap.State = "DX CONTEXT UNKNOWN";
                snap.NetworkAllowed = true;
                snap.Detail = string.IsNullOrWhiteSpace(sessionName)
                    ? "LAB MODE: multiplayer connected; DX endpoint/sector not exposed"
                    : "LAB MODE: session does not expose a mapped DX sector";
                return snap;
            }
            catch (Exception ex)
            {
                snap.State = "TRUST ERROR";
                snap.NetworkAllowed = false;
                snap.Detail = "Trust probe error: " + ex.GetType().Name;
                return snap;
            }
        }

        private static bool TryFindApprovedEndpoint(
            List<IPEndPoint> endpoints,
            out IPEndPoint endpoint,
            out string sector,
            out int port)
        {
            endpoint = null;
            sector = null;
            port = 0;

            if (endpoints == null) return false;

            for (int i = 0; i < endpoints.Count; i++)
            {
                IPEndPoint ep = endpoints[i];
                if (ep == null) continue;

                string ip = NormalizeAddress(ep.Address);
                if (!string.Equals(
                    ip,
                    ApprovedHost,
                    StringComparison.OrdinalIgnoreCase))
                    continue;

                string mappedSector;
                if (!TryMapApprovedPort(ep.Port, out mappedSector))
                    continue;

                endpoint = ep;
                sector = mappedSector;
                port = ep.Port;
                return true;
            }

            return false;
        }

        private static bool TryMapApprovedPort(int port, out string sector)
        {
            sector = null;

            if (port == LobbyPort)
            {
                sector = "DX_Lobby";
                return true;
            }

            if (port < LobbyPort + 1 || port > LastDxPort)
                return false;

            int number = port - LobbyPort;
            if (number < 1 || number > 11)
                return false;

            sector = "DX" + number;
            return true;
        }

        private static bool TryMapDxSector(
            string sessionName,
            out string sector,
            out int port)
        {
            sector = null;
            port = 0;

            string s = (sessionName ?? "").Trim();
            string compact = Regex.Replace(
                s.ToUpperInvariant(),
                @"[^A-Z0-9]",
                "");

            if (compact.Contains("DXLOBBY"))
            {
                sector = "DX_Lobby";
                port = LobbyPort;
                return true;
            }

            Match m = DxNumber.Match(s.ToUpperInvariant());
            if (!m.Success) return false;

            int n;
            if (!int.TryParse(m.Groups[1].Value, out n) || n < 1 || n > 11)
                return false;

            sector = "DX" + n;
            port = LobbyPort + n;
            return true;
        }

        private static void MergeEndpoints(
            List<IPEndPoint> target,
            List<IPEndPoint> source)
        {
            if (target == null || source == null) return;

            for (int i = 0; i < source.Count; i++)
                AddEndpoint(target, source[i]);
        }

        private static string FirstUsefulEndpoint(List<IPEndPoint> endpoints)
        {
            if (endpoints == null || endpoints.Count == 0)
                return "";

            // Prefer any observation of the approved host for diagnostics.
            for (int i = 0; i < endpoints.Count; i++)
            {
                IPEndPoint ep = endpoints[i];
                if (ep == null) continue;

                if (string.Equals(
                    NormalizeAddress(ep.Address),
                    ApprovedHost,
                    StringComparison.OrdinalIgnoreCase))
                    return NormalizeAddress(ep.Address) + ":" + ep.Port;
            }

            IPEndPoint first = endpoints[0];
            return first == null
                ? ""
                : NormalizeAddress(first.Address) + ":" + first.Port;
        }

        private static List<IPEndPoint> ProbeRemoteEndpoints(object root)
        {
            var result = new List<IPEndPoint>();
            if (root == null) return result;

            var queue = new Queue<ProbeNode>();
            var visited = new HashSet<int>();
            queue.Enqueue(new ProbeNode { Value = root, Depth = 0 });

            int examined = 0;

            while (queue.Count > 0 && examined < 96)
            {
                ProbeNode node = queue.Dequeue();
                object obj = node.Value;
                if (obj == null) continue;

                int identity = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
                if (!visited.Add(identity)) continue;
                examined++;

                Type type = obj.GetType();
                BindingFlags flags = BindingFlags.Instance |
                                     BindingFlags.Public |
                                     BindingFlags.NonPublic;

                FieldInfo[] fields;
                try { fields = type.GetFields(flags); }
                catch { fields = new FieldInfo[0]; }

                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];

                    // Root members are cheap and sometimes carry the endpoint under an
                    // unexpected field name. Deeper traversal remains name-filtered.
                    if (node.Depth > 0 && !Interesting(field.Name))
                        continue;

                    object value = null;
                    try { value = field.GetValue(obj); } catch { }

                    CaptureEndpoint(value, result);

                    if (node.Depth < 2 && ShouldRecurse(value))
                        queue.Enqueue(new ProbeNode
                        {
                            Value = value,
                            Depth = node.Depth + 1
                        });
                }

                PropertyInfo[] props;
                try { props = type.GetProperties(flags); }
                catch { props = new PropertyInfo[0]; }

                for (int i = 0; i < props.Length; i++)
                {
                    PropertyInfo prop = props[i];

                    if (prop.GetIndexParameters().Length != 0 || !prop.CanRead)
                        continue;

                    if (node.Depth > 0 && !Interesting(prop.Name))
                        continue;

                    object value = null;
                    try { value = prop.GetValue(obj, null); } catch { }

                    CaptureEndpoint(value, result);

                    if (node.Depth < 1 && ShouldRecurse(value))
                        queue.Enqueue(new ProbeNode
                        {
                            Value = value,
                            Depth = node.Depth + 1
                        });
                }
            }

            return result;
        }

        private static bool Interesting(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            string n = name.ToLowerInvariant();
            return n.Contains("server") ||
                   n.Contains("endpoint") ||
                   n.Contains("remote") ||
                   n.Contains("address") ||
                   n.Contains("connection") ||
                   n.Contains("peer") ||
                   n.Contains("host") ||
                   n.Contains("socket") ||
                   n.Contains("network");
        }

        private static bool ShouldRecurse(object value)
        {
            if (value == null ||
                value is string ||
                value is Uri ||
                value is EndPoint ||
                value.GetType().IsPrimitive ||
                value.GetType().IsEnum)
                return false;

            string asm = value.GetType().Assembly.GetName().Name ?? "";
            return asm.StartsWith("Sandbox", StringComparison.OrdinalIgnoreCase) ||
                   asm.StartsWith("VRage", StringComparison.OrdinalIgnoreCase) ||
                   asm.IndexOf("Steam", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void CaptureEndpoint(
            object value,
            List<IPEndPoint> result)
        {
            if (value == null) return;

            IPEndPoint ep = value as IPEndPoint;
            if (ep != null)
            {
                AddEndpoint(result, ep);
                return;
            }

            EndPoint endPoint = value as EndPoint;
            ep = endPoint as IPEndPoint;
            if (ep != null)
            {
                AddEndpoint(result, ep);
                return;
            }

            Uri uri = value as Uri;
            if (uri != null)
            {
                IPAddress ip;
                if (IPAddress.TryParse(uri.Host, out ip) && uri.Port > 0)
                    AddEndpoint(result, new IPEndPoint(ip, uri.Port));
                return;
            }

            string text = value as string;

            // Some Steam/VRage endpoint structs only expose a useful IP:port through
            // ToString(). The probe is bounded, so this is safe to attempt.
            if (text == null)
            {
                try
                {
                    if (!value.GetType().IsPrimitive && !value.GetType().IsEnum)
                        text = value.ToString();
                }
                catch { }
            }

            CaptureEndpointText(text, result);
        }

        private static void CaptureEndpointText(
            string text,
            List<IPEndPoint> result)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            MatchCollection matches = IpPort.Matches(text);

            for (int i = 0; i < matches.Count; i++)
            {
                IPAddress ip;
                int port;

                if (IPAddress.TryParse(matches[i].Groups[1].Value, out ip) &&
                    int.TryParse(matches[i].Groups[2].Value, out port) &&
                    port > 0 &&
                    port <= 65535)
                    AddEndpoint(result, new IPEndPoint(ip, port));
            }
        }

        private static void AddEndpoint(
            List<IPEndPoint> result,
            IPEndPoint endpoint)
        {
            if (result == null || endpoint == null) return;

            string key = NormalizeAddress(endpoint.Address) + ":" + endpoint.Port;

            for (int i = 0; i < result.Count; i++)
            {
                IPEndPoint existing = result[i];
                if (existing == null) continue;

                string existingKey = NormalizeAddress(existing.Address) + ":" + existing.Port;

                if (string.Equals(
                    existingKey,
                    key,
                    StringComparison.OrdinalIgnoreCase))
                    return;
            }

            result.Add(endpoint);
        }

        private static string NormalizeAddress(IPAddress address)
        {
            if (address == null) return "";

            try
            {
                if (address.IsIPv4MappedToIPv6)
                    return address.MapToIPv4().ToString();
            }
            catch { }

            return address.ToString();
        }
    }
}
