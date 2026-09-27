using System;
using System.Collections.Generic;
using System.Reflection;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using Sandbox.ModAPI;
using ZeoNav;
using Connector=Sandbox.ModAPI.IMyShipConnector;
internal static partial class Tests
{
    private static void DockClearanceTests()
    {
        var moving=new DockBox(Vector3D.Zero,Vector3D.One,MatrixD.Identity);
        var emptyCorner=new DockBox(new Vector3D(0,9,0),Vector3D.One,MatrixD.Identity);
        Check("Empty corner of diagonal swept AABB is not a collision",!DockBox.Sweep(moving,emptyCorner,new Vector3D(10,10,0)));
        Check("Thin obstacle across diagonal travel remains a collision",DockBox.Sweep(moving,new DockBox(new Vector3D(5,5,0),new Vector3D(.01),MatrixD.Identity),new Vector3D(10,10,0)));
        var frame=MatrixD.CreateRotationZ(Math.PI/4);
        var longBox=new DockBox(Vector3D.Zero,new Vector3D(5,.2,.2),frame);
        var parallel=new DockBox(frame.Up,new Vector3D(5,.2,.2),frame);
        Check("Separated rotated blocks with overlapping world AABBs are clear",!DockBox.Sweep(longBox,parallel,Vector3D.Zero));
        Check("Rotation-aware sweep still detects crossing parallel boxes",DockBox.Sweep(longBox,parallel,frame.Up*2));
        foreach(double angle in new[]{0d,.01,.7,1.5,3})
        {
            var rotation=MatrixD.CreateFromYawPitchRoll(angle,angle*.37,angle*.21);
            var shift=new Vector3D(35000,-45800,29000);
            var a=new DockBox(shift,Vector3D.One,rotation);
            var b=new DockBox(shift+Vector3D.TransformNormal(new Vector3D(5,5,0),rotation),new Vector3D(.1),rotation);
            var c=new DockBox(shift+Vector3D.TransformNormal(new Vector3D(0,9,0),rotation),Vector3D.One,rotation);
            var travel=Vector3D.TransformNormal(new Vector3D(10,10,0),rotation);
            Check("Swept collision is independent of world heading "+angle,DockBox.Sweep(a,b,travel)&&!DockBox.Sweep(a,c,travel));
        }
        var ownBlocks=new List<IMySlimBlock>();var obstacles=new List<IMySlimBlock>();
        Func<long,List<IMySlimBlock>,IMyCubeGrid> grid=(id,blocks)=>FixtureProxy.Make<IMyCubeGrid>(call=>{
            if(call.MethodName=="get_EntityId")return id;
            if(call.MethodName=="get_WorldAABB")return new BoundingBoxD(new Vector3D(-1),new Vector3D(1));
            if(call.MethodName=="GetBlocks"){((List<IMySlimBlock>)call.Args[0]).AddRange(blocks);return null;}
            if(call.MethodName=="GetBlocksInsideSphere")return blocks;
            return FixtureProxy.Default(call);
        });
        var ownGrid=grid(1,ownBlocks);var targetGrid=grid(2,obstacles);
        var own=FixtureProxy.Make<Connector>(call=>call.MethodName=="get_EntityId"?11L:FixtureProxy.Default(call));
        var target=FixtureProxy.Make<Connector>(call=>call.MethodName=="get_EntityId"?22L:FixtureProxy.Default(call));
        Func<BoundingBoxD,IMyCubeBlock,IMySlimBlock> block=(box,fat)=>FixtureProxy.Make<IMySlimBlock>(call=>{
            if(call.MethodName=="get_FatBlock")return fat;
            if(call.MethodName=="GetWorldBoundingBox"){var args=(object[])call.Args.Clone();args[0]=box;return new FixtureOut{Value=null,Args=args};}
            return FixtureProxy.Default(call);
        });
        ownBlocks.Add(block(new BoundingBoxD(new Vector3D(-1),new Vector3D(1)),own));
        var controller=FixtureProxy.Make<Sandbox.ModAPI.IMyShipController>(call=>call.MethodName=="get_CubeGrid"?ownGrid:FixtureProxy.Default(call));
        var ship=new ShipContext(controller,_=>{});
        var dock=new DockingController(()=>ship,()=>new NavConfig(),()=>null,_=>{},System.IO.Path.Combine(System.IO.Path.GetTempPath(),"absent-dock-fixture.json"));
        Action<string,object> set=(name,value)=>typeof(DockingController).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(dock,value);
        set("controlled",ship);set("radius",2d);set("rotationRadius",2d);set("own",own);set("target",target);
        set("<Active>k__BackingField",true);set("<Stage>k__BackingField",DockStage.Approach);
        var clear=typeof(DockingController).GetMethod("ClearPath",BindingFlags.Instance|BindingFlags.NonPublic);
        Func<bool,bool> check=final=>(bool)clear.Invoke(dock,new object[]{Vector3D.Zero,new Vector3D(10,0,0),final,true});
        var entities=typeof(MyAPIGateway).GetProperty("Entities",BindingFlags.Static|BindingFlags.Public);
        var field=typeof(MyAPIGateway).GetField("Entities",BindingFlags.Static|BindingFlags.Public);
        object previous=field!=null?field.GetValue(null):entities.GetValue(null);
        object fake=FixtureProxy.Make(field!=null?field.FieldType:entities.PropertyType,call=>call.MethodName=="GetTopMostEntitiesInSphere"?new List<IMyEntity>{targetGrid}:FixtureProxy.Default(call));
        Action<object> assign=value=>{if(field!=null)field.SetValue(null,value);else entities.SetValue(null,value);};
        try
        {
            assign(fake);Check("Empty docking corridor is clear",check(false));
            obstacles.Add(block(new BoundingBoxD(new Vector3D(-1,8,-1),new Vector3D(1,10,1)),null));
            Check("Controller accepts empty corner of a diagonal corridor",(bool)clear.Invoke(dock,new object[]{Vector3D.Zero,new Vector3D(10,10,0),false,true}));
            obstacles.Clear();obstacles.Add(block(new BoundingBoxD(new Vector3D(4.9,4.9,-1),new Vector3D(5.1,5.1,1)),null));
            Check("Controller rejects a real obstacle across diagonal travel",!(bool)clear.Invoke(dock,new object[]{Vector3D.Zero,new Vector3D(10,10,0),false,true}));
            obstacles.Clear();
            obstacles.Add(block(new BoundingBoxD(new Vector3D(4,-.2,-.2),new Vector3D(5,.2,.2)),null));
            Check("Thin obstacle inside hull sweep is detected between corner rays",!check(false));
            obstacles.Clear();obstacles.Add(block(new BoundingBoxD(new Vector3D(4,4,4),new Vector3D(5,5,5)),null));
            Check("Off-corridor station block does not block translation",check(false));
            var extraBlocks=new List<IMySlimBlock>{block(new BoundingBoxD(new Vector3D(-1,4,4),new Vector3D(1,5,5)),null)};
            var hulls=(List<IMyCubeGrid>)typeof(DockingController).GetField("hullGrids",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(dock);
            hulls.Add(ownGrid);hulls.Add(grid(3,extraBlocks));
            Check("Subgrid hull is included in collision sweep",!check(false));hulls.Clear();
            obstacles.Clear();obstacles.Add(block(new BoundingBoxD(new Vector3D(9,-1,-1),new Vector3D(11,1,1)),target));
            Check("Only selected connector pair may share final capture volume",check(true)&&!check(false));
            ownBlocks.Add(block(new BoundingBoxD(new Vector3D(-.5),new Vector3D(.5)),null));
            Check("Own hull cannot collide with selected target connector",!check(true));
            obstacles.Clear();obstacles.Add(block(new BoundingBoxD(new Vector3D(0,3,0),new Vector3D(.5,3.5,.5)),null));
            set("<Stage>k__BackingField",DockStage.Align);
            Check("Alignment checks full rotational clearance",!check(false));
            obstacles.Clear();obstacles.Add(block(new BoundingBoxD(new Vector3D(0,4,0),new Vector3D(.5,4.5,.5)),null));
            Check("Alignment does not use the old doubled turning radius",check(false));
        }
        finally{assign(previous);}
    }
}
