using System.Collections.Generic;
using System.Reflection;
using VRage.Plugins;
[assembly: AssemblyVersion("0.7.2.0")]
[assembly: AssemblyFileVersion("0.7.2.0")]
namespace Zeo.Ore.PulsarCatalog {
    public sealed class EntryPoint : IPlugin {
        private readonly ZeosOreHelper.Plugin ore=new ZeosOreHelper.Plugin();
        public void LoadAssets(IReadOnlyDictionary<string,string> assets){ore.LoadAssets(assets);}
        public void Init(object gameInstance){ore.Init(gameInstance);}
        public void Update(){ore.Update();}
        public void Dispose(){ore.Dispose();}
        public void OpenConfigDialog(){ore.OpenConfigDialog();}
    }
}
