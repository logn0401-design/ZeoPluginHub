using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;
using ZeoNav;
using Connector=Sandbox.ModAPI.IMyShipConnector;
using Tank=Sandbox.ModAPI.IMyGasTank;
using Status=Sandbox.ModAPI.Ingame.MyShipConnectorStatus;
internal static partial class Tests
{
    private static void RefuelTests()
    {
        var restoreGateway=new List<Action>();
        Action<string,Func<System.Runtime.Remoting.Messaging.IMethodCallMessage,object>> setup=(name,invoke)=>{
            var f=typeof(MyAPIGateway).GetField(name,BindingFlags.Static|BindingFlags.Public);
            if(f!=null){var original=f.GetValue(null);restoreGateway.Add(()=>f.SetValue(null,original));f.SetValue(null,FixtureProxy.Make(f.FieldType,invoke));}
            else{var p=typeof(MyAPIGateway).GetProperty(name,BindingFlags.Static|BindingFlags.Public);var original=p.GetValue(null);restoreGateway.Add(()=>p.SetValue(null,original));p.SetValue(null,FixtureProxy.Make(p.PropertyType,invoke));}
        };
        string journal=Path.Combine(Path.GetTempPath(),"zeonav-refuel-live-fixture-"+Guid.NewGuid().ToString("N")+".jsonl");
        bool stock=false,existingStock=true,connected=true;double fill=.2;var writes=new List<bool>();
        var ownBlocks=new List<IMySlimBlock>();var stationBlocks=new List<IMySlimBlock>();
        var ownGrid=FixtureProxy.Make<IMyCubeGrid>(call=>{
            if(call.MethodName=="get_EntityId")return 42L;
            if(call.MethodName=="GetBlocks"){((List<IMySlimBlock>)call.Args[0]).AddRange(ownBlocks);return null;}
            return FixtureProxy.Default(call);
        });
        var station=FixtureProxy.Make<IMyCubeGrid>(call=>{
            if(call.MethodName=="get_EntityId")return 43L;
            if(call.MethodName=="GetBlocks"){((List<IMySlimBlock>)call.Args[0]).AddRange(stationBlocks);return null;}
            return FixtureProxy.Default(call);
        });
        Connector dock=null,other=null;
        other=FixtureProxy.Make<Connector>(call=>{
            switch(call.MethodName){case "get_EntityId":return 202L;case "get_CubeGrid":return station;case "get_OtherConnector":return dock;case "get_Status":return connected?Status.Connected:Status.Unconnected;case "get_IsWorking":case "HasPlayerAccess":return true;}
            return FixtureProxy.Default(call);
        });
        dock=FixtureProxy.Make<Connector>(call=>{
            switch(call.MethodName){case "get_EntityId":return 201L;case "get_CubeGrid":return ownGrid;case "get_OtherConnector":return connected?other:null;case "get_Status":return connected?Status.Connected:Status.Unconnected;case "get_IsWorking":case "HasPlayerAccess":return true;}
            return FixtureProxy.Default(call);
        });
        Tank tank=FixtureProxy.Make<Tank>(call=>{
            switch(call.MethodName){case "get_EntityId":return 101L;case "get_CubeGrid":return ownGrid;case "get_FilledRatio":return fill;case "get_Stockpile":return stock;case "get_IsWorking":case "HasPlayerAccess":return true;
            case "set_Stockpile":
                if((bool)call.Args[0])Check("Recovery intent is durable before tank stockpile write",File.Exists(journal)&&File.ReadAllText(journal).Contains("101"));
                stock=(bool)call.Args[0];writes.Add(stock);return null;}
            return FixtureProxy.Default(call);
        });
        Tank preserved=FixtureProxy.Make<Tank>(call=>{
            switch(call.MethodName){case "get_EntityId":return 102L;case "get_CubeGrid":return ownGrid;case "get_FilledRatio":return fill;case "get_Stockpile":return existingStock;case "get_IsWorking":case "HasPlayerAccess":return true;case "set_Stockpile":existingStock=(bool)call.Args[0];return null;}
            return FixtureProxy.Default(call);
        });
        foreach(VRage.ModAPI.IMyEntity block in new VRage.ModAPI.IMyEntity[]{dock,tank,preserved})
        {var b=block;ownBlocks.Add(FixtureProxy.Make<IMySlimBlock>(call=>call.MethodName=="get_FatBlock"?b:FixtureProxy.Default(call)));}
        var controller=FixtureProxy.Make<Sandbox.ModAPI.IMyShipController>(call=>call.MethodName=="get_CubeGrid"?ownGrid:FixtureProxy.Default(call));
        var ship=new ShipContext(controller,_=>{});
        try
        {
            var player=FixtureProxy.Make<IMyPlayer>(call=>call.MethodName=="get_IdentityId"?7L:FixtureProxy.Default(call));
            setup("Session",call=>call.MethodName=="get_Player"?player:call.MethodName=="get_Name"?"FixtureWorld":FixtureProxy.Default(call));
            setup("Multiplayer",call=>call.MethodName=="get_ServerId"?9UL:FixtureProxy.Default(call));
            setup("GridGroups",call=>FixtureProxy.Default(call));
            setup("Entities",call=>{
                if(call.MethodName=="TryGetEntityById"){var args=(object[])call.Args.Clone();args[1]=((long)args[0])==101?tank:null;return new FixtureOut{Value=args[1]!=null,Args=args};}
                return FixtureProxy.Default(call);
            });
            var refuel=new NavRefuel(()=>ship,Console.WriteLine,journal);
            refuel.Toggle();Check("Docked refuel starts and preserves tanks already in stockpile",refuel.Active&&stock&&existingStock);
            connected=false;refuel.Update();Console.WriteLine("REFUEL FIXTURE "+refuel.Status+" active="+refuel.Active+" stock="+stock);
            Check("Disconnect cancels refuel and requests original tank mode",!refuel.Active&&!stock&&existingStock);
            var restore=typeof(NavRefuel).GetField("restored",BindingFlags.NonPublic|BindingFlags.Instance);
            ((Dictionary<long,DateTime>)restore.GetValue(refuel))[101]=DateTime.UtcNow.AddSeconds(-3);
            typeof(NavRefuel).GetField("next",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(refuel,DateTime.MinValue);
            refuel.Update();
            var recovered=(NavRefuel.Recovery)typeof(NavRefuel).GetField("recovery",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(refuel);
            Check("Tank mode must be observed restored before clearing durable intent",recovered.Tanks.Count==0);
            int count=writes.Count;refuel.Toggle();
            Check("Undocked refuel cannot mutate tanks",!refuel.Active&&writes.Count==count);
            connected=true;refuel.Toggle();fill=1;
            refuel.Update();Check("Full tanks finish and restore original mode",!refuel.Active&&!stock&&existingStock);
        }
        finally{foreach(var restoreField in restoreGateway)restoreField();if(File.Exists(journal))File.Delete(journal);}
    }
}
