using System;
using VRage;
using VRage.Collections;
using VRage.Game.Entity;
using VRageMath;
namespace ZeoPDC
{
    internal sealed partial class CoreSystemsApi
    {
        internal bool HookLabDamage(LabDamageMonitor monitor)
        { return monitor.Hook(Bind<Action<long,int,Action<ListReader<MyTuple<ulong,long,int,MyEntity,MyEntity,ListReader<MyTuple<Vector3D,object,float>>>>>>>("DamageHandler")); }
    }
}


