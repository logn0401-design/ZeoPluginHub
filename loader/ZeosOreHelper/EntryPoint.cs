using System.Collections.Generic;
using System.Reflection;
using VRage.Plugins;
[assembly: AssemblyVersion("1.0.5.0")]
[assembly: AssemblyFileVersion("1.0.5.0")]
namespace Zeo.PulsarCatalog {
    public sealed class EntryPoint : IPlugin {
        private readonly ZeosOreHelper.Plugin plugin = new ZeosOreHelper.Plugin();
        public void LoadAssets(IReadOnlyDictionary<string,string> assets) { plugin.LoadAssets(assets); }
        public void Init(object gameInstance) { plugin.Init(gameInstance); }
        public void Update() { plugin.Update(); }
        public void Dispose() { plugin.Dispose(); }
        public void OpenConfigDialog() { plugin.OpenConfigDialog(); }
    }
}
