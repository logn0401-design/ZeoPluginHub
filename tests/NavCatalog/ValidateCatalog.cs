using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Xml.Linq;
using System.Xml.Serialization;

internal static class ValidateCatalog
{
    // Arguments: repository, game Bin64, Pulsar Libraries/Legacy, compiled loader DLL, scratch directory.
    private static int Main(string[] args)
    {
        string repo = Path.GetFullPath(args[0]);
        string runtime = Path.Combine(repo, "assets/zeonav/0.1.23/ZeoNav.Runtime.dll");
        string scratch = Path.GetFullPath(args[4]);
        Directory.CreateDirectory(scratch);
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) => {
            string name = new AssemblyName(e.Name).Name;
            if (name == "ZeoNav.Runtime") return Assembly.LoadFrom(runtime);
            foreach (string directory in new[] { args[1], args[2] }) {
                string file = Path.Combine(directory, name + ".dll");
                if (File.Exists(file)) return Assembly.LoadFrom(file);
            }
            return null;
        };
        string descriptor = Path.Combine(repo, "Plugins/ZeoNav.xml");
        var shared = Assembly.LoadFrom(Path.Combine(args[2], "Pulsar.Shared.dll"));
        var serializer = new XmlSerializer(shared.GetType("Pulsar.Shared.Data.PluginData", true));
        object pluginData;
        using (var reader = File.OpenRead(descriptor)) pluginData = serializer.Deserialize(reader);
        Require(pluginData.GetType().Name == "GitHubPlugin", "Installed Pulsar deserializes Nav descriptor");
        var xml = XDocument.Load(descriptor).Root;
        Require(xml.Element("Id").Value == "logn0401-design/ZeoPluginHub.ZeoNav", "Unique Nav catalog ID");
        Require(xml.Element("SourceDirectories").Element("Directory").Value == "loader/ZeoNav/", "Only the Nav loader is compiled");
        foreach (var asset in xml.Elements("Asset")) {
            string file = Path.Combine(repo, asset.Attribute("Path").Value);
            string hash;
            using (var sha = SHA256.Create()) using (var input = File.OpenRead(file)) hash = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "");
            Require(hash.Equals(asset.Attribute("Sha256").Value, StringComparison.OrdinalIgnoreCase), "Verified " + asset.Attribute("Name").Value);
        }
        string overlay = Path.Combine(scratch, "overlay-" + Guid.NewGuid().ToString("N"));
        string zip = Path.Combine(repo, "assets/zeonav/0.1.23/ZeoNavOverlay.zip");
        using (var archive = ZipFile.OpenRead(zip)) Require(archive.Entries.Select(e => e.FullName).OrderBy(n => n).SequenceEqual(new[] { "ZeoNavOverlay.exe", "ZeoNavOverlay.exe.config" }), "Overlay archive contains exactly the matching executable and config");
        ZipFile.ExtractToDirectory(zip, overlay);
        var loader = Assembly.LoadFrom(args[3]);
        var entryType = loader.GetType("Zeo.PulsarCatalog.Nav.EntryPoint", true);
        var entry = Activator.CreateInstance(entryType);
        var binding = entryType.GetMethod("LoadAssets");
        bool rejected = false;
        try { binding.Invoke(entry, new object[] { new Dictionary<string, string>() }); }
        catch (TargetInvocationException ex) { rejected = ex.InnerException is InvalidOperationException; }
        Require(rejected, "Actual compiled loader rejects a missing overlay package");
        binding.Invoke(entry, new object[] { new Dictionary<string, string> { { "ZeoNavOverlayPackage", overlay } } });
        var nav = entryType.GetField("nav", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(entry);
        var navType = nav.GetType();
        Require(navType.Assembly.GetName().Name == "ZeoNav.Runtime", "Loader binds the catalog runtime assembly");
        Require((string)navType.GetField("catalogOverlayPath", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(nav) == Path.Combine(overlay, "ZeoNavOverlay.exe"), "Actual runtime binds the extracted overlay");
        Require((string)navType.GetField("dataDir", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(nav) == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pulsar", "ZeoNav"), "Persistent settings path preserved");
        Require(navType.GetField("rx", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(nav) == null, "Offline validation does not initialize network or flight");
        Console.WriteLine("PASS: 11 catalog integration checks; no game, overlay or profiles started/modified.");
        return 0;
    }
    private static void Require(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS | " + label); }
}
