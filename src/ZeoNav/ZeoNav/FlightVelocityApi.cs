using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;
namespace ZeoNav
{
    // Read-only endpoint. Never call FlipAndBurn's Intercept (it changes speed state).
    internal sealed class FlightVelocityApi
    {
        private const long Channel=9806650;
        private Func<IMyCubeGrid,Vector3D> read;
        private object utilities;
        private int requested=-10000;
        internal void Update(int frame)
        {
            if(MyAPIGateway.Utilities==null)return;
            if(utilities!=MyAPIGateway.Utilities){Reset();utilities=MyAPIGateway.Utilities;MyAPIGateway.Utilities.RegisterMessageHandler(Channel,Receive);}
            if(read==null&&frame-requested>=120){requested=frame;MyAPIGateway.Utilities.SendModMessage(Channel,"init");}
        }
        internal void Receive(object payload)
        {
            Delegate endpoint=null;
            var ro=payload as IReadOnlyDictionary<string,Delegate>;var rw=payload as IDictionary<string,Delegate>;
            if(ro!=null)ro.TryGetValue("GetVelocity",out endpoint);else if(rw!=null)rw.TryGetValue("GetVelocity",out endpoint);
            if(endpoint!=null&&endpoint.Method.DeclaringType.FullName=="FlipAndBurn.ApiBackend")read=endpoint as Func<IMyCubeGrid,Vector3D>;
        }
        internal bool TryRead(IMyCubeGrid grid,out Vector3D velocity)
        {velocity=Vector3D.Zero;if(read==null||grid==null)return false;try{velocity=read(grid);return true;}catch{return false;}}
        internal void Reset()
        {try{if(utilities!=null)MyAPIGateway.Utilities?.UnregisterMessageHandler(Channel,Receive);}catch{}utilities=null;read=null;requested=-10000;}
    }

    internal static class PilotLook
    {
        internal static Vector2 Rotation(Sandbox.ModAPI.IMyShipController controller)
        {
            if(NavTargetPickerScreen.InputActive)return Vector2.Zero;
            var input=MyAPIGateway.Input;
            bool looking=input!=null&&(input.IsKeyPress(VRage.Input.MyKeys.LeftAlt)||input.IsKeyPress(VRage.Input.MyKeys.RightAlt));
            return looking?Vector2.Zero:controller.RotationIndicator;
        }
    }
}
