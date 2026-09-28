using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using VRageMath;
using ZeoNav;
internal static partial class Tests
{
    private static void DockTests()
    {
        RefuelTests();
        DockClearanceTests();
        DockGeometryTests();
        double angle;var identity=MatrixD.Identity;
        Check("Docking settled attitude gives zero rate",DockingMath.RotationRate(identity,identity,Vector3D.Zero,out angle).Length()<1e-10&&angle<1e-6);
        foreach(double lag in new[]{.1,.4,.8})foreach(int delay in new[]{0,15})
        foreach(var start in new[]{MatrixD.CreateRotationY(Math.PI),MatrixD.CreateRotationZ(Math.PI),MatrixD.CreateRotationX(1.2)*MatrixD.CreateRotationY(-1.7)*MatrixD.CreateRotationZ(.8)})
        {
            var current=start;var omega=Vector3D.Zero;
            var desired=MatrixD.CreateRotationX(-.4)*MatrixD.CreateRotationY(.3);
            var queue=new Queue<Vector3D>();for(int i=0;i<delay;i++)queue.Enqueue(Vector3D.Zero);
            for(int i=0;i<7200;i++)
            {
                var command=DockingMath.RotationRate(current,desired,omega,out angle);
                queue.Enqueue(command);var applied=queue.Dequeue();
                omega+=(applied-omega)*Math.Min(1,1d/60/lag);
                if(omega.Length()>1e-12)current=current*MatrixD.CreateFromAxisAngle(Vector3D.Normalize(omega),omega.Length()/60);
            }
            DockingMath.RotationRate(current,desired,omega,out angle);
            Check("Connector facing/roll settles with lag="+lag+" delay="+delay,angle<.05&&omega.Length()<.05*Math.PI/180);
        }
        foreach(double acceleration in new[]{.01,.1,1d})foreach(double speed in new[]{.25,1d,2d})
        {
            double p=50,v=0;var queue=new Queue<double>();for(int i=0;i<15;i++)queue.Enqueue(0);
            bool bounded=true;
            for(int i=0;i<60000;i++)
            {
                double desired=DockingMath.Velocity(new Vector3D(-p,0,0),acceleration,speed).X;
                double command=DockingMath.Limit(new Vector3D((desired-v)/1.5,0,0),acceleration).X;
                queue.Enqueue(command);v+=queue.Dequeue()/60;p+=v/60;
                bounded&=Math.Abs(v)<=speed+.01;
            }
            Check("Delayed translation settles a 50 m approach a="+acceleration+" speed="+speed,Math.Abs(p)<.02&&Math.Abs(v)<.01&&bounded);
        }
        Check("Docking rejects behind-port approach",!DockingMath.InFront(new Vector3D(0,0,10),Vector3D.Forward,5));
        Check("Connector lateral offset is independent of distance",Math.Abs(DockingMath.Lateral(new Vector3D(3,4,100),Vector3D.Forward)-5)<1e-10);
        Check("Named main drive cannot be RCS",!DriveClassifier.IsRcs("MyObjectBuilder_Thrust/sdx_driveIndustrial9x9","RCS renamed",350000000));
        Check("RCS definition remains eligible at high thrust",DriveClassifier.IsRcs("MyObjectBuilder_Thrust/sdx_rcsHeavy","Heavy RCS",3500000));
        Check("Unrecognized thruster fails RCS-only classification",!DriveClassifier.IsRcs("MyObjectBuilder_Thrust/Unknown","Unknown",10000));
        var c=new NavConfig{MaxDriveSigKm=180,ApproachSigEnabled=true,ApproachSigKm=60,ApproachDistanceKm=100,DepartureSigEnabled=true,DepartureSigKm=40,DepartureDistanceKm=100};
        Check("Cruise uses selected normal ceiling",ApproachProfile.Effective(c,false,false)==180);
        Check("Departure uses departure ceiling",ApproachProfile.Effective(c,true,false)==40);
        Check("Approach uses approach ceiling",ApproachProfile.Effective(c,false,true)==60);
        Check("Overlapping zones use lower ceiling",ApproachProfile.Effective(c,true,true)==40);
        Check("Approach activates at 100 km",ApproachProfile.Activate(c,false,100000,NavPhase.ACCELERATE)&&!ApproachProfile.Activate(c,false,100001,NavPhase.ACCELERATE));
        Check("Flip outside arrival radius does not snap to arrival ceiling",!ApproachProfile.Activate(c,false,500000,NavPhase.PRE_FLIP));
        Check("Quiet approach does not flap at zone boundary",ApproachProfile.Activate(c,true,100001,NavPhase.COAST));
        c.ApproachSigKm=500;c.DepartureSigKm=500;
        Check("Profiles never raise cruise maximum",ApproachProfile.Effective(c,true,true)==180);
        c.ApproachSigEnabled=false;c.DepartureSigEnabled=false;
        Check("Disabled profiles preserve existing cruise behavior",ApproachProfile.Effective(c,true,true)==180&&!ApproachProfile.Activate(c,true,1,NavPhase.BRAKE));
        var budget=new SignalBudget{Ready=true,TargetKm=180,SphericalBaseSquared=100,DirectionalBaseSquared=100};
        budget.Add("forward",false,0,100000);
        double cruise=ApproachProfile.Ratio(budget,180),quiet=ApproachProfile.Ratio(budget,60);
        Check("Lower approach ceiling reserves longer braking and leaves live ceiling intact",quiet<cruise&&10000/(2*quiet)>10000/(2*cruise)&&budget.TargetKm==180);
        Check("Unreachable approach budget cannot claim braking authority",ApproachProfile.Ratio(budget,5)==0);
        var migrated=ConfigRules.Clamp(JsonIo.FromBytes<NavConfig>(System.Text.Encoding.UTF8.GetBytes("{\"ConfigVersion\":7,\"MaxDriveSigKm\":180,\"HudWidth\":1.7,\"HudHeight\":1.4}")));
        Check("Existing users retain SIG/HUD with optional profiles off",migrated.ConfigVersion==18&&migrated.TargetAimHoldKey=="LeftControl"&&migrated.TargetHudY==-.76&&migrated.MaxDriveSigKm==180&&migrated.HudWidth==1.7&&migrated.HudHeight==1.4&&!migrated.ApproachSigEnabled&&!migrated.DepartureSigEnabled&&migrated.ApproachDistanceKm==100);
        var keys=new NavConfig{QuickDockKey="Insert"};
        bool rejected=false;try{NavHotkeys.ValidateNewBindings(keys);}catch(ArgumentException){rejected=true;}
        Check("Duplicate Quick Dock/menu binding cannot launch flight",rejected&&!NavHotkeys.Unique(keys,"QuickDockKey"));
        keys.QuickDockKey="NumPad0";NavHotkeys.ValidateNewBindings(keys);Check("Independent docking hotkey accepted",NavHotkeys.Unique(keys,"QuickDockKey"));

        string journal=Path.Combine(Path.GetTempPath(),"zeonav-refuel-test-"+Guid.NewGuid().ToString("N")+".jsonl");
        try
        {
            var record=new NavRefuel.Recovery{Context="test",Tanks=new List<long>{11,22}};
            File.WriteAllBytes(journal,JsonIo.ToBytes(record));File.AppendAllText(journal,"\n{truncated");
            var refill=new NavRefuel(()=>null,_=>{},journal);
            var load=typeof(NavRefuel).GetMethod("Load",BindingFlags.Instance|BindingFlags.NonPublic);
            load.Invoke(refill,null);
            var recovered=(NavRefuel.Recovery)typeof(NavRefuel).GetField("recovery",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(refill);
            Check("Interrupted journal tail retains previous tank recovery IDs",recovered.Tanks.Count==2&&recovered.Tanks[0]==11);
            File.WriteAllText(journal,"{corrupt");
            refill=new NavRefuel(()=>null,_=>{},journal);bool corrupt=false;
            try{load.Invoke(refill,null);}catch(TargetInvocationException ex){corrupt=ex.InnerException is IOException;}
            Check("Entirely corrupt recovery journal prevents new stockpile changes",corrupt);
        }
        finally{if(File.Exists(journal))File.Delete(journal);}
    }
}
