using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ZeoNav;

internal static partial class Tests
{
    private static void CatalogTests()
    {
        var plugin = new Plugin();
        var pathField = typeof(Plugin).GetField("catalogOverlayPath", BindingFlags.NonPublic | BindingFlags.Instance);
        var dataField = typeof(Plugin).GetField("dataDir", BindingFlags.NonPublic | BindingFlags.Instance);
        string originalData = (string)dataField.GetValue(plugin);
        Check("Manual installs retain overlay fallback", pathField.GetValue(plugin) == null);
        Action<IReadOnlyDictionary<string, string>, string> reject = (assets, label) => {
            bool threw = false;
            try { plugin.LoadAssets(assets); } catch (InvalidOperationException) { threw = true; } catch (FileNotFoundException) { threw = true; }
            Check(label, threw && pathField.GetValue(plugin) == null);
        };
        reject(null, "Missing catalog assets fail before Init");
        reject(new Dictionary<string, string>(), "Missing overlay asset name rejected");
        string fixture = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "catalog-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        var valid = new Dictionary<string, string> { { "ZeoNavOverlayPackage", fixture } };
        reject(valid, "Empty overlay package rejected");
        string exe = Path.Combine(fixture, "ZeoNavOverlay.exe");
        File.WriteAllText(exe, "offline fixture; never executed");
        reject(valid, "Overlay without matching config rejected");
        File.WriteAllText(exe + ".config", "offline fixture; never executed");
        plugin.LoadAssets(valid);
        Check("Catalog binds supplied overlay path", (string)pathField.GetValue(plugin) == Path.GetFullPath(exe));
        Check("Catalog preserves persistent settings directory", (string)dataField.GetValue(plugin) == originalData && originalData.EndsWith(Path.Combine("Pulsar", "ZeoNav")));
        Check("Binding assets does not initialize networking", typeof(Plugin).GetField("rx", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(plugin) == null);
    }
}
