using System.Collections.Generic;
using System.Reflection;
using VRage.Plugins;
[assembly: AssemblyVersion("0.3.31.0")]
[assembly: AssemblyFileVersion("0.3.31.0")]
namespace Zeo.PulsarCatalog {
    public sealed class EntryPoint : IPlugin {
        private readonly ZeoPDC.Plugin plugin = new ZeoPDC.Plugin();
        public void LoadAssets(IReadOnlyDictionary<string,string> assets) { plugin.LoadAssets(assets); }
        public void Init(object gameInstance) { plugin.Init(gameInstance); }
        public void Update() { plugin.Update(); }
        public void Dispose() { plugin.Dispose(); }
        public void OpenConfigDialog() { plugin.OpenConfigDialog(); }
    }
}
