using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace ZeoCore
{
    internal sealed class ZeoConfig
    {
        internal const string BattleHost = "battle.zeobattlemanager-space.com";
        internal const string BattleBaseUrl = "https://battle.zeobattlemanager-space.com";

        // Legacy direct-link settings are retained for existing installations.
        // Device/account linking no longer depends on them.
        public bool Enabled { get; set; }
        public string PinnedHost { get; set; }
        public string ProtectedEndpoint { get; set; }
        public int PublishSimulationFrames { get; set; }
        public int MaxFriendlies { get; set; }
        public int MaxContacts { get; set; }
        public bool WriteLastPayload { get; set; }
        public bool? TransmitTelemetry { get; set; }

        // v0.5.8 account/device identity. DeviceKey is never stored in clear text.
        public string NetworkBaseUrl { get; set; }
        public string DeviceId { get; set; }
        public string ProtectedDeviceKey { get; set; }
        public int DeviceCredentialVersion { get; set; }
        public string PendingLinkCode { get; set; }
        public long PendingLinkExpiresUtcMs { get; set; }

        private static readonly byte[] LegacyEntropy = Encoding.UTF8.GetBytes("ZeoCore.DirectLink.v0.2");
        private static readonly byte[] DeviceEntropy = Encoding.UTF8.GetBytes("ZeoCore.DeviceAuth.v0.5.8");
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
        private static readonly object SaveLock = new object();

        internal static string ConfigPath
        {
            get { return Path.Combine(Plugin.DataDirectory, "config.json"); }
        }

        internal static ZeoConfig LoadOrMigrate()
        {
            Directory.CreateDirectory(Plugin.DataDirectory);

            if (File.Exists(ConfigPath))
            {
                try
                {
                    var cfg = Json.Deserialize<ZeoConfig>(File.ReadAllText(ConfigPath));
                    ApplyDefaults(cfg);
                    EnsureOpenTestClientId(cfg);
                    ValidateOpenTestConfig(cfg);
                    Save(cfg);
                    Plugin.Log("Config loaded. account host=" + BattleHost +
                               " device=" + ShortDevice(cfg.DeviceId) +
                               " legacy direct=" + (HasLegacyEndpoint(cfg) ? "YES" : "NO"));
                    return cfg;
                }
                catch (Exception ex)
                {
                    Plugin.Log("Config load failed; creating safe replacement: " + ex.Message);
                    BackupBrokenConfig();
                    var fresh = Fresh();
                    Save(fresh);
                    return fresh;
                }
            }

            ZeoConfig migrated = TryMigrateLegacy();
            if (migrated != null)
            {
                Save(migrated);
                return migrated;
            }

            var created = Fresh();
            Save(created);
            Plugin.Log("Created ZeoCore account config. No legacy Direct Link endpoint is required for account pairing.");
            return created;
        }

        internal Uri GetAccountBaseUri()
        {
            string value = string.IsNullOrWhiteSpace(NetworkBaseUrl) ? BattleBaseUrl : NetworkBaseUrl.Trim();
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri))
                throw new InvalidOperationException("Zeo network base URL is invalid.");
            if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Zeo account network requires HTTPS.");
            if (!uri.Host.Equals(BattleHost, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Zeo account host is not the pinned Battle Manager host.");
            if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new InvalidOperationException("Zeo account base URL cannot contain query or fragment data.");
            return new Uri(uri.GetLeftPart(UriPartial.Authority));
        }

        // OPEN TEST 0.6.1: gameplay transport does not use a device secret.
        // Kept only as a compatibility method for old source paths; never used for authorization.
        internal string GetDeviceKey()
        {
            return "";
        }

        internal Uri GetDeviceNetworkEndpoint()
        {
            return PinnedDeviceEndpoint("/v1/zeo/open/network");
        }

        internal Uri GetDeviceDistressEndpoint()
        {
            return PinnedDeviceEndpoint("/v1/zeo/open/distress");
        }

        private Uri PinnedDeviceEndpoint(string path)
        {
            Uri baseUri = GetAccountBaseUri();
            Uri uri = new Uri(baseUri, path);
            if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !uri.Host.Equals(BattleHost, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Zeo device endpoint failed HTTPS/host validation.");
            return uri;
        }

        internal Uri GetEndpoint()
        {
            if (!HasLegacyEndpoint(this))
                throw new InvalidOperationException("Legacy Direct Link endpoint is not configured.");

            byte[] protectedBytes = Convert.FromBase64String(ProtectedEndpoint);
            byte[] clear = ProtectedData.Unprotect(protectedBytes, LegacyEntropy, DataProtectionScope.CurrentUser);
            string value = Encoding.UTF8.GetString(clear);

            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri))
                throw new InvalidOperationException("Protected endpoint is not a valid absolute URI.");
            if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("ZeoCore refuses non-HTTPS telemetry endpoints.");
            if (!uri.Host.Equals(BattleHost, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Endpoint host does not match the pinned Battle Manager host.");
            if (!uri.AbsolutePath.StartsWith("/ingest/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Endpoint path is not a Zeo ingest path.");

            string secretSegment = uri.AbsolutePath.Substring("/ingest/".Length).Trim('/');
            if (secretSegment.Length < 32)
                throw new InvalidOperationException("Ingest path does not meet minimum secret length.");

            return uri;
        }

        internal Uri GetFleetEndpoint()
        {
            Uri ingest = GetEndpoint();
            string secret = ingest.AbsolutePath.Substring("/ingest/".Length).Trim('/');
            var builder = new UriBuilder(ingest)
            {
                Path = "/fleet/" + secret,
                Query = "world=default"
            };
            Uri fleet = builder.Uri;
            if (!fleet.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !fleet.Host.Equals(BattleHost, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("FleetLink endpoint failed HTTPS/host validation.");
            return fleet;
        }

        internal Uri GetDistressEndpoint()
        {
            Uri ingest = GetEndpoint();
            string secret = ingest.AbsolutePath.Substring("/ingest/".Length).Trim('/');
            var builder = new UriBuilder(ingest)
            {
                Path = "/distress/" + secret,
                Query = "world=default"
            };
            Uri distress = builder.Uri;
            if (!distress.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !distress.Host.Equals(BattleHost, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Distress endpoint failed HTTPS/host validation.");
            return distress;
        }

        internal static void Save(ZeoConfig cfg)
        {
            Directory.CreateDirectory(Plugin.DataDirectory);
            ApplyDefaults(cfg);
            EnsureOpenTestClientId(cfg);
            ValidateOpenTestConfig(cfg);
            lock (SaveLock)
            {
                string json = Json.Serialize(cfg);
                File.WriteAllText(ConfigPath, json, new UTF8Encoding(false));
            }
        }

        private static ZeoConfig Fresh()
        {
            var cfg = new ZeoConfig
            {
                Enabled = false,
                PinnedHost = BattleHost,
                ProtectedEndpoint = null,
                PublishSimulationFrames = 30,
                MaxFriendlies = 128,
                MaxContacts = 256,
                WriteLastPayload = false,
                TransmitTelemetry = true,
                NetworkBaseUrl = BattleBaseUrl,
                DeviceCredentialVersion = 0
            };
            EnsureOpenTestClientId(cfg);
            return cfg;
        }

        private static ZeoConfig TryMigrateLegacy()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string conduitDir = Path.Combine(appData, "Conduit");
            string[] candidates =
            {
                Path.Combine(conduitDir, "config.ZEO-KNOWN-GOOD.json"),
                Path.Combine(conduitDir, "config.json")
            };

            foreach (string candidate in candidates)
            {
                if (!File.Exists(candidate)) continue;
                try
                {
                    var legacy = Json.Deserialize<LegacyConduitConfig>(File.ReadAllText(candidate));
                    if (legacy == null || string.IsNullOrWhiteSpace(legacy.EndpointUrl)) continue;

                    Uri uri;
                    if (!Uri.TryCreate(legacy.EndpointUrl.Trim(), UriKind.Absolute, out uri)) continue;
                    if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!uri.Host.Equals(BattleHost, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!uri.AbsolutePath.StartsWith("/ingest/", StringComparison.OrdinalIgnoreCase)) continue;

                    var protectedBytes = ProtectedData.Protect(
                        Encoding.UTF8.GetBytes(uri.AbsoluteUri), LegacyEntropy, DataProtectionScope.CurrentUser);

                    var cfg = Fresh();
                    cfg.Enabled = true;
                    cfg.ProtectedEndpoint = Convert.ToBase64String(protectedBytes);
                    cfg.WriteLastPayload = true;
                    Plugin.Log("Migrated legacy Zeo Direct Link from " + Path.GetFileName(candidate) +
                               "; account device credentials created separately.");
                    return cfg;
                }
                catch (Exception ex)
                {
                    Plugin.Log("Legacy migration candidate failed " + Path.GetFileName(candidate) + ": " + ex.Message);
                }
            }
            return null;
        }

        private static void ApplyDefaults(ZeoConfig cfg)
        {
            if (cfg == null) return;
            if (cfg.PublishSimulationFrames < 10) cfg.PublishSimulationFrames = 30;
            if (cfg.MaxFriendlies <= 0) cfg.MaxFriendlies = 128;
            if (cfg.MaxContacts <= 0) cfg.MaxContacts = 256;
            if (string.IsNullOrWhiteSpace(cfg.NetworkBaseUrl)) cfg.NetworkBaseUrl = BattleBaseUrl;
            cfg.PinnedHost = BattleHost;
            // OPEN TEST: device credential version is deliberately zero; no secret is required.
            cfg.DeviceCredentialVersion = 0;
        }

        private static bool HasLegacyEndpoint(ZeoConfig cfg)
        {
            return cfg != null && cfg.Enabled && !string.IsNullOrWhiteSpace(cfg.ProtectedEndpoint);
        }

        private static void EnsureOpenTestClientId(ZeoConfig cfg)
        {
            if (cfg == null) throw new InvalidOperationException("Config is null.");
            if (string.IsNullOrWhiteSpace(cfg.DeviceId) || cfg.DeviceId.Length < 16 || cfg.DeviceId.Length > 128)
                cfg.DeviceId = "ZEO" + Guid.NewGuid().ToString("N").ToUpperInvariant();
            // Old protected keys may remain in config for rollback compatibility, but
            // OPEN TEST never decrypts, validates, transmits, or authorizes with them.
            cfg.DeviceCredentialVersion = 0;
        }

        private static void ValidateOpenTestConfig(ZeoConfig cfg)
        {
            if (cfg == null) throw new InvalidOperationException("Config is null.");
            if (string.IsNullOrWhiteSpace(cfg.DeviceId) || cfg.DeviceId.Length < 16 || cfg.DeviceId.Length > 128)
                throw new InvalidOperationException("Zeo open-test client ID is missing or invalid.");
            Uri ignored = cfg.GetAccountBaseUriNoRecurse();
        }

        private Uri GetAccountBaseUriNoRecurse()
        {
            string value = string.IsNullOrWhiteSpace(NetworkBaseUrl) ? BattleBaseUrl : NetworkBaseUrl.Trim();
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) ||
                !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !uri.Host.Equals(BattleHost, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Zeo account network host validation failed.");
            return uri;
        }

        private static string ShortDevice(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return "NONE";
            return deviceId.Length <= 12 ? deviceId : deviceId.Substring(0, 8) + "...";
        }

        private static void BackupBrokenConfig()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return;
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                string backup = Path.Combine(Plugin.DataDirectory, "config.corrupt." + stamp + ".json");
                File.Copy(ConfigPath, backup, false);
            }
            catch { }
        }

        private sealed class LegacyConduitConfig
        {
            public string EndpointUrl { get; set; }
        }
    }
}
