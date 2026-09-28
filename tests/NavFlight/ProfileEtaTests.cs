using System;
using ZeoNav;
internal static partial class Tests
{
    private static void ProfileEtaTests()
    {
        var budget=new SignalBudget{TargetKm=0,Ready=false,SphericalBaseSquared=1e10};
        budget.Add("main",true,0,1e12);
        Check("Unlimited SIG permits full thrust without own-SIG feedback",budget.Limit(0,1,new double[6])==1);
        Check("Unlimited SIG clamps commands and rejects NaN",budget.Limit(0,2,new double[6])==1&&budget.Limit(0,-1,new double[6])==0&&budget.Limit(0,double.NaN,new double[6])==0);
        budget.TargetKm=1000;
        Check("Finite SIG still fails closed without a fresh model",budget.Limit(0,1,new double[6])==0);
        budget.TargetKm=double.NaN;
        Check("Invalid SIG cannot release actuators",budget.Limit(0,1,new double[6])==0);
        var c=new NavConfig{MaxDriveSigKm=1000,DepartureSigEnabled=true,DepartureSigKm=100,DepartureDistanceKm=100,
            ApproachSigEnabled=true,ApproachSigKm=200,ApproachDistanceKm=100};
        Check("Departure begins at selected quiet limit",ApproachProfile.At(c,0,1000000,5000,2000)==100);
        Check("Departure midpoint blends rather than holds a step",ApproachProfile.At(c,50000,1000000,5000,2000)==550);
        Check("Departure reaches cruise at the zone edge",ApproachProfile.At(c,100000,1000000,5000,2000)==1000);
        Check("Arrival enters without a sudden ceiling drop",ApproachProfile.At(c,1000000,100000,5000,2000)==1000);
        Check("Arrival midpoint blends toward quiet limit",ApproachProfile.At(c,1000000,52500,5000,2000)==600);
        Check("Arrival reaches selected limit at buffer, not at GPS center",ApproachProfile.At(c,1000000,5000,5000,2000)==200);
        bool smooth=true;double previous=100;
        for(int i=1;i<=1000;i++)
        {
            double n=ApproachProfile.At(c,i*100d,1000000,5000,2000);
            smooth&=n>=previous&&n<=1000&&n-previous<1.36;previous=n;
        }
        Check("Departure curve stays continuous, monotonic and below cruise",smooth);
        c.MaxDriveSigKm=0;
        Check("Unlimited cruise still honors finite arrival/departure limits",ApproachProfile.Arrival(c)==200&&ApproachProfile.Departure(c)==100);
        Check("Finite departure transitions toward modeled full-bank SIG",ApproachProfile.At(c,50000,1000000,5000,2000)==1050);
        Check("Outside finite zones cruise is genuinely unrestricted",ApproachProfile.At(c,1000000,1000000,5000,2000)==0);
        c.ApproachSigKm=0;c.DepartureSigKm=0;
        Check("Zero profile limits impose no restriction",ApproachProfile.At(c,0,1,5000,2000)==0&&!ApproachProfile.NeedsModel(c));
        Check("Zero combines with finite limits without suppressing thrust",ApproachProfile.Restrict(0,180)==180&&ApproachProfile.Restrict(180,0)==180);
        var trip=RouteEta.Plan(1000000,0,50000,x=>20,10,15,18,5,5,.35);
        var weak=RouteEta.Plan(1000000,0,50000,x=>2,10,15,18,5,5,.35);
        var slowFlip=RouteEta.Plan(1000000,0,50000,x=>20,10,90,18,5,5,.35);
        Check("Arrival ETA is available from rest",trip.Seconds>0&&trip.Acceleration>0&&trip.TurnAndBrake>15);
        Check("Reduced acceleration increases arrival ETA",weak.Seconds>trip.Seconds);
        Check("Slower learned flip increases arrival ETA",slowFlip.Seconds>trip.Seconds);
        var stepped=RouteEta.Plan(1000000,0,50000,x=>2,10,15,18,5,5,.35);
        var ramped=RouteEta.Plan(1000000,0,50000,x=>2+18*Math.Min(1,x/100000),10,15,18,5,5,.35);
        Check("ETA samples future departure release instead of freezing its initial thrust",ramped.Seconds<stepped.Seconds&&ramped.Seconds>=trip.Seconds);
        var brake=RouteEta.RemainingBrake(10000,400,10,0,18,5,5,.35);
        var flip=RouteEta.RemainingBrake(10000,400,10,15,18,5,5,.35);
        Check("Braking ETA coasts to its envelope rather than using RCS for the remaining cruise",brake.Coast>0);
        Check("Braking ETA does not include another acceleration or full flip",brake.Acceleration==0&&brake.TurnAndBrake==38.2&&flip.TurnAndBrake==53.2);
        Check("Terminal ETA accounts for stopping residual motion at arrival",RouteEta.Terminal(0,10,18,2,5,.35)>4.8);
        Check("Unknown braking/RCS authority does not manufacture an ETA",RouteEta.Plan(10000,0,50000,x=>20,10,15,18,0,5,.35).Seconds<0);
        // Compare the main flight segment with an independent analytic solution.
        double distance=1000000,terminalSpeed=20,terminalAccel=100,tail=30,accel=20,decel=10,turn=15;
        double A=1/(2*accel)+1/(2*decel),B=turn,C=-(distance-tail+terminalSpeed*terminalSpeed/(2*decel));
        double peak=(-B+Math.Sqrt(B*B-4*A*C))/(2*A);
        double reference=peak/accel+turn+(peak-terminalSpeed)/decel+RouteEta.Terminal(tail,terminalSpeed,terminalSpeed,terminalAccel,5,.35);
        var actual=RouteEta.Plan(distance,0,50000,x=>accel,decel,turn,terminalSpeed,terminalAccel,5,.35);
        Check("Integrated ETA agrees with constant-acceleration arrival solution",Math.Abs(actual.Seconds-reference)<.001);
        var grid=FixtureProxy.Make<VRage.Game.ModAPI.IMyCubeGrid>(call=>call.MethodName=="get_EntityId"?42L:FixtureProxy.Default(call));
        var pilot=FixtureProxy.Make<Sandbox.ModAPI.IMyShipController>(call=>{
            if(call.MethodName=="get_CubeGrid")return grid;
            if(call.MethodName=="get_WorldMatrix")return VRageMath.MatrixD.Identity;
            return FixtureProxy.Default(call);
        });
        var ship=new ShipContext(pilot,_=>{});
        var config=new NavConfig();
        var nav=new NavController(()=>ship,()=>config,()=>null,_=>{});
        NavSet(nav,"active",true);
        var governor=typeof(NavController).GetMethod("CalculateDriveRatio",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        Check("Actual unrestricted route governor works without Spectrum own feed",(double)governor.Invoke(nav,new object[]{config})==1&&ship.SignatureBudget.Ready&&ship.SignatureBudget.TargetKm==0);
        config.MaxDriveSigKm=1000;
        Check("Turning on a finite route limit waits for own telemetry",(double)governor.Invoke(nav,new object[]{config})==0&&!ship.SignatureBudget.Ready);
        config.MaxDriveSigKm=0;config.ApproachSigEnabled=true;config.ApproachSigKm=100;
        Check("Unrestricted cruise with a finite arrival zone requires its braking model",(double)governor.Invoke(nav,new object[]{config})==0);
    }
}
