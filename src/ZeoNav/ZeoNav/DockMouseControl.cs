using System;
using System.IO;
using System.Runtime.Serialization;
using Sandbox.ModAPI;
using VRageMath;
namespace ZeoNav
{
    // Disable the cockpit's manual gyro torque, not the gyro overrides used by Nav.
    // Save its original setting and release on dock, abort, lost context or disposal.
    internal sealed class DockMouseControl
    {
        [DataContract] internal sealed class Recovery
        {
            [DataMember] public string Context;
            [DataMember] public long Cockpit;
            [DataMember] public bool Original;
        }
        private readonly string path;
        private Recovery recovery;
        private bool loaded;
        private DateTime nextRecovery;
        private readonly SavedControlSwitch control=new SavedControlSwitch();
        private Sandbox.Game.Entities.MyShipController native;
        internal DockMouseControl(string path){this.path=path;}
        private static string Context(){var s=MyAPIGateway.Session;return s?.Player==null?null:MyAPIGateway.Multiplayer.ServerId+"|"+s.Name+"|"+s.Player.IdentityId;}
        internal void Recover(Sandbox.ModAPI.IMyShipController controller)
        {
            if(DateTime.UtcNow<nextRecovery)return;
            nextRecovery=DateTime.UtcNow.AddSeconds(1);
            if(control.Active)Release();
            if(!loaded)
            {
                recovery=JsonIo.Load<Recovery>(path);
                if(File.Exists(path)&&recovery==null)throw new InvalidOperationException("Mouse-control recovery record cannot be read.");
                loaded=true;
            }
            var cockpit=controller as Sandbox.Game.Entities.MyShipController;
            if(recovery==null||recovery.Cockpit==0||cockpit==null||cockpit.Closed||recovery.Context!=Context()||recovery.Cockpit!=cockpit.EntityId)return;
            if(!controller.HasPlayerAccess(MyAPIGateway.Session.Player.IdentityId))return;
            cockpit.ControlGyros=recovery.Original;
            if(cockpit.ControlGyros==recovery.Original)ClearRecovery();
        }
        private void ClearRecovery(){JsonIo.Save(path,new Recovery());recovery=null;}
        internal static bool Takeover(Vector3 move,float roll){return move.LengthSquared()>.01||Math.Abs(roll)>.05;}
        internal void Acquire(Sandbox.ModAPI.IMyShipController controller)
        {
            if(control.Active)throw new InvalidOperationException("Mouse control already owned.");
            nextRecovery=DateTime.MinValue;Recover(controller);
            if(recovery!=null&&recovery.Cockpit!=0)throw new InvalidOperationException("Return to the previous docking cockpit to restore its mouse control first.");
            native=controller as Sandbox.Game.Entities.MyShipController;
            if(native==null)throw new InvalidOperationException("Cockpit gyro control unavailable.");
            var cockpit=native;
            recovery=new Recovery{Context=Context(),Cockpit=cockpit.EntityId,Original=cockpit.ControlGyros};
            JsonIo.Save(path,recovery); // Persist before changing a synchronized cockpit setting.
            control.Acquire(()=>cockpit.ControlGyros,value=>cockpit.ControlGyros=value);
            Maintain();
        }
        internal void Maintain()
        {
            control.Maintain();
            if(native!=null&&!native.Closed)native.CubeGrid.GridSystems.GyroSystem.ControlTorque=Vector3.Zero;
        }
        internal void Release()
        {
            try
            {
                control.Release();
                if(native!=null&&!native.Closed&&recovery!=null&&native.ControlGyros==recovery.Original)ClearRecovery();
            }
            finally{native=null;}
        }
    }
    internal sealed class SavedControlSwitch
    {
        private Func<bool> read;
        private Action<bool> write;
        private bool original;
        internal bool Active {get{return write!=null;}}
        internal void Acquire(Func<bool> read,Action<bool> write)
        {
            if(Active)throw new InvalidOperationException("Control already owned.");
            original=read();this.read=read;this.write=write;
            Maintain();
        }
        internal void Maintain()
        {
            if(Active&&read()){write(false);if(read())throw new InvalidOperationException("Cockpit did not release manual gyro control.");}
        }
        internal void Release()
        {
            if(!Active)return;
            write(original);
            if(read()!=original)throw new InvalidOperationException("Cockpit gyro control restoration is pending.");
            write=null;read=null;
        }
    }
}
