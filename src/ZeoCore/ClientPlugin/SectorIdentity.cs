using System;
using Sandbox.ModAPI;

namespace ZeoCore
{
    internal struct SectorSnapshot
    {
        public string Id;
        public string Name;
        public ulong ServerId;
        public bool Known;
    }

    internal static class SectorIdentity
    {
        public static SectorSnapshot Capture()
        {
            var next = new SectorSnapshot { Id = "unknown", Name = "UNKNOWN SECTOR", ServerId = 0, Known = false };
            try
            {
                ulong serverId = 0;
                if (MyAPIGateway.Multiplayer != null) serverId = MyAPIGateway.Multiplayer.ServerId;
                string sessionName = null;
                try { if (MyAPIGateway.Session != null) sessionName = MyAPIGateway.Session.Name; } catch { }
                if (string.IsNullOrWhiteSpace(sessionName)) sessionName = "SECTOR";
                next.ServerId = serverId;
                next.Name = sessionName.Trim();
                if (serverId != 0)
                {
                    next.Id = "srv:" + serverId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    next.Known = true;
                }
                else
                {
                    next.Id = "session:" + Sanitize(sessionName);
                    next.Known = false;
                }
            }
            catch { }
            return next;
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "unknown";
            var sb = new System.Text.StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.') sb.Append(char.ToLowerInvariant(c));
                else if (char.IsWhiteSpace(c)) sb.Append('-');
            }
            return sb.Length == 0 ? "unknown" : sb.ToString();
        }
    }
}
