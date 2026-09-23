using System.Collections.Generic;
using System.Reflection;
using VRage.Plugins;

[assembly: AssemblyVersion("1.0.1.0")]
[assembly: AssemblyFileVersion("1.0.1.0")]

namespace Zeo.PulsarCatalog.Nav
{
    public sealed class EntryPoint : IPlugin
    {
        private readonly ZeoNav.Plugin nav = new ZeoNav.Plugin();
        public void LoadAssets(IReadOnlyDictionary<string, string> assets) { nav.LoadAssets(assets); }
        public void Init(object gameInstance) { nav.Init(gameInstance); }
        public void Update() { nav.Update(); }
        public void Dispose() { nav.Dispose(); }
        public void OpenConfigDialog() { nav.OpenConfigDialog(); }
    }
}
