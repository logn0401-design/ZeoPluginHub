using System.Collections.Generic;
using System.Reflection;
using VRage.Plugins;

[assembly: AssemblyVersion("1.0.5.0")]
[assembly: AssemblyFileVersion("1.0.5.0")]

namespace Zeo.PulsarCatalog
{
    // Pulsar compiles this entry point and verifies the accompanying runtime
    // and overlay assets. Full runtime source is maintained in src/ZeoCore.
    public sealed class EntryPoint : IPlugin
    {
        private readonly ZeoCore.Plugin core = new ZeoCore.Plugin();
        public void LoadAssets(IReadOnlyDictionary<string, string> assets) { core.LoadAssets(assets); }
        public void Init(object gameInstance) { core.Init(gameInstance); }
        public void Update() { core.Update(); }
        public void Dispose() { core.Dispose(); }
        public void OpenConfigDialog() { core.OpenConfigDialog(); }
    }
}
