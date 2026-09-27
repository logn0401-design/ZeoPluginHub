using System;
using System.Linq;
using VRageMath;
using VRage.Game.ModAPI;
using ZeoNav;
using Connector=Sandbox.ModAPI.IMyShipConnector;
using Status=Sandbox.ModAPI.Ingame.MyShipConnectorStatus;
internal static partial class Tests
{
    private static void AutoDockTests()
    {
        var ship=FixtureProxy.Make<IMyCubeGrid>(m=>FixtureProxy.Default(m));
        var station=FixtureProxy.Make<IMyCubeGrid>(m=>m.MethodName=="get_IsStatic"?true:FixtureProxy.Default(m));
        Func<long,double,bool,Status,DockingController.Port> port=(id,x,own,state)=>new DockingController.Port{
            Name="Port "+id,Block=FixtureProxy.Make<Connector>(m=>{
                switch(m.MethodName){
                    case "get_EntityId":return id;
                    case "GetPosition":return new Vector3D(x,0,0);
                    case "get_IsWorking":return true;
                    case "get_CubeGrid":return own?ship:station;
                    case "get_Status":return state;
                }return FixtureProxy.Default(m);
            })};
        var a=port(1,0,true,Status.Unconnected);var b=port(2,20,true,Status.Unconnected);
        var near=port(3,50,false,Status.Unconnected);var far=port(4,100,false,Status.Unconnected);
        var occupied=port(5,21,false,Status.Connected);var attracting=port(6,22,false,Status.Connectable);
        var ownPorts=new[]{a,b};var targets=new[]{far,occupied,attracting,near};
        var pair=DockingController.PickNearest(ownPorts,targets,0,0,(x,y)=>true);
        Check("Auto dock chooses nearest free pair from unsorted ports",pair.Item1==b&&pair.Item2==near);
        pair=DockingController.PickNearest(ownPorts,targets,1,0,(x,y)=>true);
        Check("Saved ship connector is honored during automatic station selection",pair.Item1==a&&pair.Item2==near);
        pair=DockingController.PickNearest(ownPorts,targets,0,4,(x,y)=>true);
        Check("Explicit destination override remains available",pair.Item2==far);
        pair=DockingController.PickNearest(ownPorts,targets,0,0,(x,y)=>y.EntityId!=3);
        Check("Inaccessible or wrong-side nearest port is skipped",pair.Item2==far);
        Check("No eligible connector produces no docking selection",DockingController.PickNearest(ownPorts,targets,0,0,(x,y)=>false)==null);
        var newer=port(7,25,false,Status.Unconnected);
        pair=DockingController.PickNearest(ownPorts,targets.Concat(new[]{newer}),0,0,(x,y)=>true);
        Check("Fresh scan can replace an older automatic destination",pair.Item2==newer);
        var report=new DockingController.PortScanReport();
        Check("Non-connector blocks do not count as rejected ports",!report.Consider(null,true,0,1000)&&report.Seen==0);
        Check("Port diagnostic distinguishes access rejection",!report.Consider(near.Block,false,50,1000)&&report.NoAccess==1);
        Check("Port diagnostic distinguishes occupied rejection",!report.Consider(occupied.Block,true,50,1000)&&report.Occupied==1);
        Check("Port diagnostic distinguishes range rejection",!report.Consider(near.Block,true,1001,1000)&&report.Outside==1);
        var offline=FixtureProxy.Make<Connector>(m=>FixtureProxy.Default(m));
        Check("Port diagnostic distinguishes offline rejection",!report.Consider(offline,true,0,1000)&&report.Unavailable==1);
        Check("Connector exactly on scan boundary remains available",report.Consider(near.Block,true,1000,1000)&&report.Accepted==1&&report.Seen==5);
    }
}
