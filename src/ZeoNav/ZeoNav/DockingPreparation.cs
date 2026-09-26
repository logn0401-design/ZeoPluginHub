using Sandbox.ModAPI;
using Status=Sandbox.ModAPI.Ingame.MyShipConnectorStatus;

namespace ZeoNav
{
    internal sealed class DockPreparation
    {
        private long generation;
        private int quietSamples;
        internal bool Failed {get;private set;}
        internal string Status {get;private set;}
        internal void Reset(long initialGeneration=-1){generation=initialGeneration;quietSamples=0;Failed=false;Status="PREPARING — waiting for quiet own SIG.";}
        internal bool Observe(bool fresh,long sample,bool quiet,double speed,double elapsed)
        {
            if(speed>.3){Failed=true;Status="Preparation stopped: ship drift exceeds 0.30 m/s. Stop and retry.";return false;}
            if(!fresh||!quiet)quietSamples=0;
            if(fresh&&sample!=generation){generation=sample;if(quiet&&elapsed>=.25)quietSamples++;}
            Status=!fresh?"PREPARING — waiting for fresh own-ship Spectrum.":!quiet?"PREPARING — waiting for RCS/thruster output to settle.":"PREPARING — quiet own SIG "+quietSamples+"/3.";
            if(elapsed>=12){Failed=true;Status="Dock preparation timed out: "+Status;return false;}
            return quietSamples>=3;
        }
    }
    internal static class DockingCapture
    {
        internal static bool PairReady(IMyShipConnector own,IMyShipConnector target)
        {
            return own!=null&&target!=null&&!own.Closed&&!target.Closed&&own.IsWorking&&target.IsWorking&&
                own.Status==Status.Connectable&&target.Status==Status.Connectable&&
                own.OtherConnector?.EntityId==target.EntityId&&target.OtherConnector?.EntityId==own.EntityId;
        }
    }
}
