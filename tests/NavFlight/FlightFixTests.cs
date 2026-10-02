using System;
using System.Collections.Generic;
using System.Reflection;
using ZeoNav;
using VRageMath;
using VRage.Game.ModAPI;

internal static partial class Tests
{
    private static void FlightFixTests()
    {
        foreach(double speed in new[]{1676.4,2930.6,50000d})
        {
            var m=new WorldMotion();var v=new Vector3D(speed,0,0);var gate=new MotionRevalidation();
            for(int i=0;i<=120;i++)m.ObserveMod(v*(i/60d),new Vector3D(Math.Min(speed,1000),0,0),v,i/60d,85);
            for(int i=121;i<=150;i++)m.ObserveMod(v*2,new Vector3D(1000,0,0),v,i/60d,85);
            Check("High-speed half-second position freeze quarantines instead of aborting "+speed,!m.Ready&&m.RecoverableFault&&gate.Observe(m,0)==MotionDecision.Hold);
            Check("Fault records actual prediction window "+speed,m.LastFault.Contains("window=")&&m.LastFault.Contains("expected="));
            bool recovered=false;
            for(int i=151;i<=270;i++)
            {
                m.ObserveMod(v*(i/60d),new Vector3D(1000,0,0),v,i/60d,85);
                var decision=gate.Observe(m,(i-150)/60d);
                recovered|=decision==MotionDecision.Recovered;
                if(decision==MotionDecision.Abort)break;
            }
            Check("Catch-up requires fresh windows and retains true speed "+speed,recovered&&m.Ready&&(m.Velocity-v).Length()<.01);
            m.ObserveMod(new Vector3D(10000000,0,0),Vector3D.Zero,v,4.6,85);
            Check("Huge teleport still aborts immediately "+speed,!m.Ready&&!m.RecoverableFault&&gate.Observe(m,2)==MotionDecision.Abort);
        }
        var replay=new WorldMotion();var revalidation=new MotionRevalidation();var velocity=new Vector3D(1676.4,0,0);
        double offset=0,time=0;
        for(int i=0;i<=120;i++){time=i/60d;replay.ObserveMod(velocity*time,Vector3D.Right*1000,velocity,time,85);}
        foreach(double discrepancy in new[]{925.3,993.4,1113.3})
        {
            offset+=discrepancy;
            for(int i=0;i<30;i++){time+=1d/60;replay.ObserveMod(velocity*time+Vector3D.Right*offset,Vector3D.Right*1000,velocity,time,85);}
            Check("Recorded discrepancy enters hold "+discrepancy,!replay.Ready&&replay.RecoverableFault&&revalidation.Observe(replay,time)==MotionDecision.Hold);
            bool recovered=false;
            for(int i=0;i<60;i++){time+=1d/60;replay.ObserveMod(velocity*time+Vector3D.Right*offset,Vector3D.Right*1000,velocity,time,85);recovered|=revalidation.Observe(replay,time)==MotionDecision.Recovered;}
            Check("Recorded discrepancy recovers without abort "+discrepancy,recovered);
        }
        var frozen=new WorldMotion();var recovery=new MotionRevalidation();
        for(int i=0;i<=150;i++)frozen.ObserveMod(Vector3D.Zero,Vector3D.Right*1000,Vector3D.Right*50000,i/60d,85);
        recovery.Observe(frozen,0);
        Check("Persistent frozen position remains bounded",recovery.Observe(frozen,2.01)==MotionDecision.Abort);
        Check("Invalid recovery time cannot extend a hold",recovery.Observe(frozen,double.NaN)==MotionDecision.Abort);
        frozen.ObserveMod(Vector3D.Zero,Vector3D.Zero,Vector3D.Zero,3,double.PositiveInfinity);
        Check("Invalid acceleration bounds cannot validate moving telemetry",!frozen.Ready&&!frozen.RecoverableFault);

        var burn=new InterceptBurnGate();double ratio=0;
        for(int i=0;i<60;i++)ratio=burn.Observe(1.02,.58,i/60d);
        Check("Recorded near-alignment now allows stable main burn",ratio>.85);
        bool continuous=true;
        for(int i=60;i<120;i++){ratio=burn.Observe(i%2==0?.86:1.05,i%2==0?1.15:.41,i/60d);continuous&=ratio>.8;}
        Check("Recorded angle/rate variation no longer alternates main and RCS",continuous);
        Check("Unsafe heading cuts main thrust immediately",burn.Observe(15,0,2)==0);
        Check("Invalid angular data cannot command main thrust",burn.Observe(0,double.NaN,2.1)==0);
        Check("Roll alone does not block a safe burn",InterceptBurnGate.HeadingRate(Vector3D.Forward*2,Vector3D.Forward)<.001);
        Check("Pitch or yaw remains part of the burn gate",InterceptBurnGate.HeadingRate(Vector3D.Up*.1,Vector3D.Forward)>5);
        double far=InterceptEnvelope.Closing(48000,7000,500,60,2,30,false);
        double old=RendezvousMath.ClosingLimit(47500,2,30);
        Check("Far intercept accounts for main-drive braking",far>old*2);
        Check("Near target retains RCS envelope",Math.Abs(InterceptEnvelope.Closing(7400,7000,500,60,2,30,false)-RendezvousMath.ClosingLimit(6900,2,30))<.001);
        Check("RCS-only intercept never borrows main-drive braking",InterceptEnvelope.Closing(48000,7000,500,60,2,30,true)==old);
        var fresh=new SignalBudget();fresh.Add("a",true,0,100,20);
        var prior=new SignalBudget{Ready=true,TargetKm=400,SphericalBaseSquared=100,DirectionalBaseSquared=200,FeedbackScale=2};prior.Add("a",true,0,100,20);
        Check("Compatible fresh definition model can reuse validated baseline",InterceptEnvelope.ReuseBaseline(fresh,prior)&&fresh.FeedbackScale==2&&fresh.DirectionalBaseSquared==200);
        fresh.Add("a",true,1,50);
        Check("Topology/emission changes reject cached baseline",!InterceptEnvelope.ReuseBaseline(fresh,prior));
        prior.Ready=false;
        Check("Stale model cannot authorize immediate acquisition",!InterceptEnvelope.ReuseBaseline(new SignalBudget(),prior));
        TargetControllerFixTests();
        RecoveredRouteMomentumTests();
        TurnRetryBudgetTests();
    }

    private static void TurnRetryBudgetTests()
    {
        var grid=FixtureProxy.Make<IMyCubeGrid>(c=>c.MethodName=="get_EntityId"?922L:FixtureProxy.Default(c));
        var controller=FixtureProxy.Make<Sandbox.ModAPI.IMyShipController>(c=>{
            if(c.MethodName=="get_CubeGrid")return grid;
            if(c.MethodName=="get_WorldMatrix")return MatrixD.Identity;
            return FixtureProxy.Default(c);
        });
        var ship=new ShipContext(controller,_=>{});
        ship.Gyros.Add(FixtureProxy.Make<Sandbox.ModAPI.IMyGyro>(c=>{
            if(c.MethodName=="get_EntityId")return 923L;
            if(c.MethodName=="get_IsFunctional"||c.MethodName=="get_Enabled")return true;
            if(c.MethodName=="get_WorldMatrix")return MatrixD.Identity;
            if(c.MethodName=="get_GyroPower")return 1f;
            return FixtureProxy.Default(c);
        }));
        var rcs=FixtureProxy.Make<Sandbox.ModAPI.IMyGyro>(c=>{
            if(c.MethodName=="get_EntityId")return 924L;
            if(c.MethodName=="get_CubeGrid")return grid;
            if(c.MethodName=="get_IsFunctional")return true;
            if(c.MethodName=="get_WorldMatrix")return MatrixD.Identity;
            if(c.MethodName=="get_BlockDefinition")
            {var d=FixtureProxy.Default(c);d.GetType().GetField("SubtypeName").SetValue(d,"sdg_rcsGyroComputer");return d;}
            if(c.MethodName=="get_GyroPower")return 1f;
            return FixtureProxy.Default(c);
        });
        ((List<Sandbox.ModAPI.IMyGyro>)typeof(ShipContext).GetField("rcsGyros",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(ship)).Add(rcs);
        ship.SignatureBudget=new SignalBudget{Ready=true,TargetKm=0};
        Check("Unrestricted route may retry an available RCS turn bank",ship.BeginRcsTurnRetry());
        ship.ReleaseGyros();ship.FlipTurnMode="AUTO";
        var capped=new SignalBudget{Ready=true,TargetKm=1};capped.Add("main",true,0,1e12);ship.SignatureBudget=capped;
        Check("Finite SIG ceiling rejects an over-budget RCS retry",!ship.BeginRcsTurnRetry());
        ship.FlipTurnMode="GYRO";ship.SignatureBudget=new SignalBudget{Ready=true,TargetKm=0};
        Check("Explicit gyro-only preference remains respected",!ship.BeginRcsTurnRetry());
    }

    private static void RecoveredRouteMomentumTests()
    {
        var blocks=new List<IMySlimBlock>();
        var grid=FixtureProxy.Make<IMyCubeGrid>(c=>{
            if(c.MethodName=="get_EntityId")return 123L;
            if(c.MethodName=="GetBlocks"){((List<IMySlimBlock>)c.Args[0]).AddRange(blocks);return null;}
            return FixtureProxy.Default(c);
        });
        var controller=FixtureProxy.Make<Sandbox.ModAPI.IMyShipController>(c=>{
            switch(c.MethodName)
            {
                case "get_CubeGrid":return grid;
                case "get_WorldMatrix":return MatrixD.Identity;
                case "CalculateShipMass":var m=FixtureProxy.Default(c);var f=m.GetType().GetField("PhysicalMass");f.SetValue(m,Convert.ChangeType(1000000,f.FieldType));return m;
            }
            return FixtureProxy.Default(c);
        });
        var ship=new ShipContext(controller,_=>{});
        var main=new DriveFixture(1201,"Main drive",100000000,Vector3D.Backward);
        ship.ThrusterBanks[MoveDir.Forward].Add(main.Block);
        ship.Gyros.Add(FixtureProxy.Make<Sandbox.ModAPI.IMyGyro>(c=>{
            switch(c.MethodName){case "get_EntityId":return 1202L;case "get_CubeGrid":return grid;
                case "get_IsFunctional":case "get_Enabled":return true;
                case "get_WorldMatrix":return MatrixD.Identity;case "get_GyroPower":return 1f;}
            return FixtureProxy.Default(c);
        }));
        foreach(var block in new IMyCubeBlock[]{main.Block,ship.Gyros[0]})
        {
            var held=block;
            blocks.Add(FixtureProxy.Make<IMySlimBlock>(c=>c.MethodName=="get_FatBlock"?held:FixtureProxy.Default(c)));
        }
        var velocity=Vector3D.Forward*1025;
        for(int i=0;i<=120;i++)ship.Motion.ObserveMod(velocity*(i/60d),Vector3D.Forward*1000,velocity,i/60d,85);
        for(int i=121;i<=150;i++)ship.Motion.ObserveMod(velocity*2,Vector3D.Forward*1000,velocity,i/60d,85);
        var config=new NavConfig{MaxDriveSigKm=0,AbortOnManualInput=false,SpeedCapOverride=50000};
        var events=new List<string>();
        var nav=new NavController(()=>ship,()=>config,()=>null,events.Add);
        NavSet(nav,"active",true);NavSet(nav,"phase",NavPhase.ACCELERATE);
        NavSet(nav,"routeStart",Vector3D.Zero);NavSet(nav,"navTarget",Vector3D.Forward*543496);
        NavSet(nav,"gpsTarget",Vector3D.Forward*543496);
        NavSet(nav,"routeStartDistance",543496d);NavSet(nav,"lastDistance",543496d);
        nav.Update(1);
        Check("Recorded high-speed correction cuts thrust while velocity is unverified",nav.IsControlling&&main.Override==0);
        for(int i=151;i<=270;i++)ship.Motion.ObserveMod(velocity*(i/60d),Vector3D.Forward*1000,velocity,i/60d,85);
        nav.Update(2);
        Check("Actual controller retains aligned momentum after fresh high-speed recovery / phase="+Phase(nav)+" / "+string.Join(" | ",events),nav.IsControlling&&Phase(nav)==NavPhase.ALIGN_PROGRADE);
        NavSet(nav,"phase",NavPhase.INITIAL_BRAKE);
        NavSet(nav,"recoveryTurnTracking",true);
        var watch=(TurnProgressWatchdog)typeof(NavController).GetField("recoveryTurnProgress",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(nav);
        var now=DateTime.UtcNow;
        watch.Reset(now.AddSeconds(-25),180);
        watch.Observe(now.AddSeconds(-14),180,0,20,1);
        nav.Update(3);
        Check("Actual recovery turn cannot remain armed after sustained zero rotation",!nav.IsControlling&&main.Override==0);
    }

    private static void FlightField(object o,string name,object value)
    {o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(o,value);}
    private static void TargetControllerFixTests()
    {
        var blocks=new List<IMySlimBlock>();
        var grid=FixtureProxy.Make<IMyCubeGrid>(c=>{
            if(c.MethodName=="get_EntityId")return 42L;
            if(c.MethodName=="GetBlocks"){((List<IMySlimBlock>)c.Args[0]).AddRange(blocks);return null;}
            return FixtureProxy.Default(c);
        });
        double now=0;bool dampeners=false;Vector3D angular=Vector3D.Up*(.58*Math.PI/180);
        var controller=FixtureProxy.Make<Sandbox.ModAPI.IMyShipController>(c=>{
            switch(c.MethodName)
            {
                case "get_CubeGrid":return grid;case "get_WorldMatrix":return MatrixD.Identity;
                case "GetShipVelocities":var v=FixtureProxy.Default(c);v.GetType().GetField("AngularVelocity").SetValue(v,angular);return v;
                case "CalculateShipMass":var m=FixtureProxy.Default(c);var f=m.GetType().GetField("PhysicalMass");f.SetValue(m,Convert.ChangeType(1000000,f.FieldType));return m;
                case "get_DampenersOverride":return dampeners;case "set_DampenersOverride":dampeners=(bool)c.Args[0];return null;
            }return FixtureProxy.Default(c);
        });
        var ship=new ShipContext(controller,_=>{});var main=new DriveFixture(9001,"Epstein drive",100000000,Vector3D.Backward);
        ship.ThrusterBanks[MoveDir.Forward].Add(main.Block);
        var rcs=new List<DriveFixture>();
        var exhausts=new[]{Vector3D.Backward,Vector3D.Forward,Vector3D.Left,Vector3D.Right,Vector3D.Down,Vector3D.Up};
        for(int i=0;i<6;i++){var d=new DriveFixture(9010+i,"RCS",10000000,exhausts[i]);rcs.Add(d);ship.ThrusterBanks[(MoveDir)i].Add(d.Block);}
        bool gyroOverride=false;
        ship.Gyros.Add(FixtureProxy.Make<Sandbox.ModAPI.IMyGyro>(c=>{
            switch(c.MethodName){case "get_EntityId":return 9002L;case "get_CubeGrid":return grid;case "get_IsFunctional":case "get_Enabled":return true;case "get_WorldMatrix":return MatrixD.Identity;case "get_GyroPower":return 1f;case "get_GyroOverride":return gyroOverride;case "set_GyroOverride":gyroOverride=(bool)c.Args[0];return null;}
            return FixtureProxy.Default(c);
        }));
        ship.RefreshWorkingState();ship.RefreshSignatureTopology();ship.BeginThrustControl();ship.BeginGyroControl();
        for(int i=0;i<=60;i++)ship.Motion.ObserveMod(Vector3D.Zero,Vector3D.Zero,Vector3D.Zero,i/60d,100);
        var tracker=new TargetTracker{Confirmed=true,LockedId=91};var track=new TargetTrack();
        for(int i=0;i<3;i++)track.Accept(new TargetDetection{EmitterId=91,Position=new Vector3D(890,0,-50000),Velocity=Vector3D.Zero,DetectedAt=i*60},i);
        tracker.Tracks[91]=track;
        var config=new NavConfig{MaxDriveSigKm=0,DepartureSigEnabled=false,ApproachSigEnabled=false,MatchKeep=true};
        var entries=new List<string>();var flight=new TargetFlight(()=>ship,()=>config,()=>null,tracker,entries.Add,()=>now);
        typeof(TargetFlight).GetProperty("Active",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(flight,true);
        FlightField(flight,"lockedId",91L);FlightField(flight,"intercept",true);FlightField(flight,"topology",ship.TopologyRevision);
        FlightField(flight,"budget",new SignalBudget{Ready=true,TargetKm=0});FlightField(flight,"rcsBudget",new SignalBudget{Ready=true,TargetKm=0});
        for(int i=0;i<60;i++){now=i/60d;flight.Update(121+i,2+now);}
        Check("Real intercept controller accelerates at recorded 1-degree alignment",flight.Active&&main.Override>0&&ship.TrackTurnBank);
        Check("Real intercept records main acquisition latency",entries.Exists(e=>e.StartsWith("TARGET FIRST MAIN")));
        angular=Vector3D.Up;now=1.01;flight.Update(182,3.01);
        Check("Actual unsafe angular motion switches to bounded RCS",flight.Active&&main.Override==0&&ship.RcsOnly);
        tracker.LockedId=92;now=1.02;flight.Update(183,3.02);
        Check("Target replacement releases controls with exact identity reason",!flight.Active&&main.Override==0&&!ship.TrackTurnBank&&flight.Status.Contains("old=91 new=92"));
        Check("Abort restores original dampeners",!dampeners);
        // The original premature stop occurred in BRAKE. Exercise that real
        // controller phase, not only the shared recovery state machine.
        var navConfig=new NavConfig{MaxDriveSigKm=0,DepartureSigEnabled=false,ApproachSigEnabled=false,AbortOnManualInput=false};
        var nav=new NavController(()=>ship,()=>navConfig,()=>null,entries.Add);
        ship.BeginThrustControl();ship.SetMove(MoveDir.Forward,1);ship.Motion.Reset();
        Vector3D fast=Vector3D.Right*2930.6;
        for(int i=0;i<=120;i++)ship.Motion.ObserveMod(fast*(i/60d),Vector3D.Right*1000,fast,i/60d,85);
        for(int i=121;i<=150;i++)ship.Motion.ObserveMod(fast*2,Vector3D.Right*1000,fast,i/60d,85);
        NavSet(nav,"active",true);NavSet(nav,"phase",NavPhase.BRAKE);NavSet(nav,"navTarget",Vector3D.Right*500000);
        nav.Update(1);
        Check("Recorded 1.46 km discrepancy keeps actual BRAKE route armed",nav.IsControlling&&Phase(nav)==NavPhase.BRAKE&&main.Override==0);
        for(int i=151;i<=270;i++)ship.Motion.ObserveMod(fast*(i/60d),Vector3D.Right*1000,fast,i/60d,85);
        nav.Update(2);
        Check("Actual braking route resumes after high-speed position catch-up",nav.IsControlling);
        nav.Abort("fixture");ship.Motion.Reset();
        for(int i=0;i<=60;i++)ship.Motion.ObserveMod(Vector3D.Zero,Vector3D.Zero,Vector3D.Zero,i/60d,100);
        tracker.LockedId=91;
        Action<int,double,Vector3D> refresh=(frame,seconds,position)=>{
            track=new TargetTrack();
            for(int i=0;i<3;i++)track.Accept(new TargetDetection{EmitterId=91,Position=position,Velocity=Vector3D.Zero,DetectedAt=frame-120+i*60},seconds-2+i);
            tracker.Tracks[91]=track;tracker.Confirmed=true;
        };
        Action<bool> arm=chase=>{
            ship.BeginThrustControl();ship.BeginGyroControl();
            typeof(TargetFlight).GetProperty("Active",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(flight,true);
            FlightField(flight,"intercept",chase);FlightField(flight,"settled",0);FlightField(flight,"staleSince",-1d);
        };
        config.MatchKeep=false;refresh(300,5,new Vector3D(0,0,-50000));arm(false);
        for(int i=0;i<60;i++){now=2+i/60d;flight.Update(300+i,5+i/60d);}
        Check("Actual Match Once completes without chasing target position",!flight.Active&&flight.Status.Contains("VELOCITY MATCHED")&&main.Override==0);
        config.MatchKeep=true;refresh(420,7,new Vector3D(0,0,-50000));arm(false);
        for(int i=0;i<60;i++){now=4+i/60d;flight.Update(420+i,7+i/60d);}
        Check("Actual Keep Matched remains engaged when relative speed settles",flight.Active&&main.Override==0);
        flight.Abort("fixture");config.InterceptStandOffKm=7;
        refresh(540,9,new Vector3D(0,0,-7400));arm(true);
        now=6;flight.Update(540,9);
        Check("Actual near-target intercept uses RCS only",flight.Active&&ship.RcsOnly&&main.Override==0);
        tracker.Confirmed=false;now=6.1;flight.Update(541,9.1);
        Check("Expired signal releases all flight controls explicitly",!flight.Active&&flight.Status.Contains("TARGET SIGNAL EXPIRED")&&main.Override==0);
        refresh(660,11,new Vector3D(0,0,-50000));arm(true);
        now=7;flight.Update(850,14);
        Check("Stale target holds thrust instead of chasing old prediction",flight.Active&&main.Override==0&&flight.Status.Contains("STALE"));
        now=17.1;flight.Update(851,14.1);
        Check("Unrecovered stale target cannot hold controls endlessly",!flight.Active&&main.Override==0);

        // Exercise real preflight and Start, including the scan, rather than only
        // the arithmetic policies. No game session or live settings are required.
        var fat=new List<IMyCubeBlock>{main.Block,ship.Gyros[0]};foreach(var d in rcs)fat.Add(d.Block);
        foreach(var b in fat){var copy=b;blocks.Add(FixtureProxy.Make<IMySlimBlock>(c=>c.MethodName=="get_FatBlock"?copy:FixtureProxy.Default(c)));}
        refresh(960,16,new Vector3D(0,0,-50000));angular=Vector3D.Zero;
        bool ready=flight.CanStart(true,960,16);
        Check("Actual intercept preflight accepts a feasible fresh target / "+flight.Status,ready);
        now=0;flight.Start(true,960,16);flight.Update(961,16.01);
        Check("Actual Start turns immediately without quiet acquisition delay",flight.Active&&entries.Exists(e=>e.Contains("SIG READY / turn available immediately")));
        flight.Abort("fixture");
        for(int i=0;i<=60;i++)ship.Motion.ObserveMod(new Vector3D(0,0,-10000*i/60d),Vector3D.Forward*1000,Vector3D.Forward*10000,20+i/60d,100);
        Check("Impossible moving acquisition fails preflight before a handoff",!flight.CanStart(true,962,16.03)&&!flight.Active);
    }
}
