using System;
using System.Linq;
using Sandbox.ModAPI;
using VRageMath;
namespace ZeoNav
{
    internal sealed class TargetFlight
    {
        private readonly Func<ShipContext> ship;
        private readonly Func<NavConfig> config;
        private readonly Func<SpectrumAdapter> spectrum;
        private readonly TargetTracker tracker;
        private readonly Action<string> log;
        private readonly PilotInputGate input=new PilotInputGate();
        private readonly MotionRevalidation motion=new MotionRevalidation();
        private readonly System.Diagnostics.Stopwatch clock=new System.Diagnostics.Stopwatch();
        private SignalBudget budget;
        private int topology,generation,quiet,settled;
        private double staleSince=-1;
        private bool alignmentLogged;
        private long lockedId;
        private bool intercept,arrived,rcsSetting;
        internal bool Active {get;private set;}
        internal string Status="Target flight idle.";
        internal double Distance,RelativeSpeed;
        internal double Ceiling {get{return Math.Min(config().MaxDriveSigKm,Math.Min(config().ApproachSigEnabled?config().ApproachSigKm:750,config().DepartureSigEnabled?config().DepartureSigKm:750));}}
        internal string Mode {get{return intercept?"INTERCEPT":config().MatchKeep?"KEEP MATCHED":"MATCH ONCE";}}
        internal TargetFlight(Func<ShipContext> s,Func<NavConfig> c,Func<SpectrumAdapter> sp,TargetTracker t,Action<string> logger)
        {ship=s;config=c;spectrum=sp;tracker=t;log=logger;}
        internal void Start(bool chase,int tick,double now)
        {
            if(Active){Abort("Target flight cancelled.");return;}
            var s=ship();var target=tracker.Locked;
            if(s==null||!tracker.Confirmed||target==null){Status="Lock a Spectrum signal first.";return;}
            if(!target.Fresh(tick,now)){Status="Signal locked; waiting for three fresh tracking samples before flight.";return;}
            if(!s.Motion.Ready||s.HasDockConnection()||s.Gravity.Length()>.05){Status="Requires verified motion, undocked ship and open space.";return;}
            Distance=Vector3D.Distance(target.Position(tick),s.Position);
            if(Distance<MinimumSeparation(s)){Status="Too close for target-flight preview; use manual RCS.";return;}
            Vector3D relativePosition=target.Position(tick)-s.Position,relativeVelocity=target.Sample.Velocity-s.Velocity;
            double closestTime=relativeVelocity.LengthSquared()>1?Math.Max(0,Math.Min(12,-Vector3D.Dot(relativePosition,relativeVelocity)/relativeVelocity.LengthSquared())):0;
            if((relativePosition+relativeVelocity*closestTime).Length()<MinimumSeparation(s)*2){Status="Closing too fast to acquire safely; match manually first.";return;}
            s.Scan();if(s.Gyros.Count==0){Status="No available gyro.";return;}
            intercept=chase;arrived=false;rcsSetting=config().MatchRcsOnly;lockedId=tracker.LockedId;budget=null;quiet=settled=0;generation=-1;staleSince=-1;alignmentLogged=false;input.Reset();motion.Reset();clock.Restart();
            s.SaveDampenersOnce();s.SetDampeners(false);s.BeginThrustControl();s.BeginGyroControl();s.ClearThrust();
            Active=true;Status="ACQUIRING QUIET OWN SIGNAL / THRUST OFF";log("TARGET FLIGHT START // "+Mode+" id="+lockedId+" distance="+Distance);
        }
        private double MinimumSeparation(ShipContext s){return Math.Max(500,s.Grid.WorldVolume.Radius*3);}
        internal void Abort(string reason)
        {if(Active){ship()?.ReleaseAll(false);log("TARGET FLIGHT RELEASE // "+reason);}Active=false;Status=reason;clock.Stop();}
        internal void Update(int tick,double now)
        {
            if(!Active)return;
            var controlled=ship();if(controlled==null){Abort("Controlled ship lost.");return;}
            controlled.BeginThrustFrame();
            try {UpdateControl(tick,now);controlled.CommitThrustFrame();}
            catch {controlled.CancelThrustFrame();throw;}
        }
        private void UpdateControl(int tick,double now)
        {
            var s=ship();var t=tracker.Locked;
            if(s==null||!tracker.Confirmed||tracker.LockedId!=lockedId||t==null){Abort("TARGET LOST / controls released; confirm target again");return;}
            if(!t.Fresh(tick,now))
            {
                s.SetDampeners(false);s.ClearThrust();s.ApplyDockRotation(Vector3D.Zero);
                if(staleSince<0){staleSince=clock.Elapsed.TotalSeconds;log("TARGET TRACK STALE // thrust off / waiting for fresh Spectrum samples");}
                Status="TARGET TRACK STALE / THRUST OFF / REACQUIRING";
                if(clock.Elapsed.TotalSeconds-staleSince>10)Abort("TARGET SIGNAL LOST / controls released after 10s hold");
                return;
            }
            if(staleSince>=0){log("TARGET TRACK REACQUIRED // fresh samples restored");staleSince=-1;}
            var c=config();
            if(c.MatchRcsOnly!=rcsSetting){Abort("Engine setting changed; re-engage target flight.");return;}
            if(c.AbortOnManualInput&&input.Observe(s.Controller.MoveIndicator,PilotLook.Rotation(s.Controller),s.Controller.RollIndicator,tick/60d)){Abort("MANUAL PILOT INPUT");return;}
            if(s.HasDockConnection()||s.Gravity.Length()>.05){Abort("Target flight context changed.");return;}
            var decision=motion.Observe(s.Motion,clock.Elapsed.TotalSeconds);
            if(decision==MotionDecision.Abort){Abort(motion.Reason);return;}
            s.SetDampeners(false);s.ClearThrust();
            if(Vector3D.Distance(t.Position(tick),s.Position)<MinimumSeparation(s)){Abort("Separation floor reached / manual control required.");return;}
            if(decision==MotionDecision.Hold){s.ApplyDockRotation(Vector3D.Zero);Status="VERIFYING MOTION / THRUST OFF";return;}
            var feed=spectrum();
            if(feed==null||!feed.DriveKmReady){s.ApplyDockRotation(Vector3D.Zero);Status="WAIT OWN SIG / THRUST OFF";if(clock.Elapsed.TotalSeconds>10)Abort(Status);return;}
            double ceiling=c.ApproachSigEnabled?Math.Min(c.MaxDriveSigKm,c.ApproachSigKm):c.MaxDriveSigKm;
            if(c.DepartureSigEnabled)ceiling=Math.Min(ceiling,c.DepartureSigKm);
            // First preview budgets the entire rendezvous at the stricter arrival ceiling.
            if(budget==null)
            {
                if(feed.SampleGeneration!=generation){generation=feed.SampleGeneration;quiet=s.ThrustersQuiet?quiet+1:0;}
                if(quiet<3){s.ApplyDockRotation(Vector3D.Zero);Status="ACQUIRING QUIET OWN SIGNAL / "+quiet+" OF 3 / THRUST OFF";if(clock.Elapsed.TotalSeconds>12)Abort("Could not acquire quiet own-SIG baseline.");return;}
                s.RefreshSignatureTopology();budget=feed.BuildBudget(s,c.MatchRcsOnly);budget.TargetKm=ceiling;
                budget.SphericalBaseSquared=feed.SphericalWeakKm*feed.SphericalWeakKm;budget.DirectionalBaseSquared=feed.DirectionalWeakKm*feed.DirectionalWeakKm;budget.Ready=true;
                s.SignatureBudget=budget;topology=s.TopologyRevision;
                log("TARGET FLIGHT SIG READY // ceiling="+ceiling.ToString("0.0")+"km / acquiring target heading");
            }
            budget.TargetKm=ceiling;
            if(feed.DriveKm>ceiling){Abort("Own SIG exceeded target-flight ceiling.");return;}
            if(tick%30==0){s.RefreshWorkingState();s.RefreshSignatureTopology();if(topology!=s.TopologyRevision){Abort("Drive topology changed; re-engage target flight.");return;}}
            string capSource;double cap=SpeedCapResolver.Resolve(s.Grid,c.SpeedCapOverride,s.Velocity.Length(),out capSource);
            if(t.Sample.Velocity.Length()>cap){Abort("Target velocity exceeds your speed cap.");return;}
            Vector3D displacement=t.Position(tick)-s.Position;
            Distance=displacement.Length();
            if(intercept&&Distance>1)
            {
                var direction=displacement/Distance;double along=Vector3D.Dot(t.Sample.Velocity,direction);
                double lateral2=Math.Max(0,t.Sample.Velocity.LengthSquared()-along*along);
                if(Math.Sqrt(Math.Max(0,cap*cap-lateral2))-along<.5){Abort("Cannot catch target within speed cap.");return;}
            }
            RelativeSpeed=(s.Velocity-t.Sample.Velocity).Length();
            if(Distance<MinimumSeparation(s)){Abort("Separation floor reached / manual control required.");return;}
            s.RcsOnly=c.MatchRcsOnly;
            double acceleration=s.Force(MoveDir.Forward)/s.Mass*budget.Limit(0,1,new double[6]);
            if(acceleration<.01){Abort("Insufficient thrust within MAX SIG.");return;}
            double standOff=Math.Max(c.InterceptStandOffKm*1000,MinimumSeparation(s)*2);
            double rcsAuthority=Enumerable.Range(0,6).Min(i=>s.RcsForce((MoveDir)i)/s.Mass*budget.Limit(i,1,new double[6]))*.5;
            if(rcsAuthority<.01){Abort("Insufficient RCS authority within MAX SIG.");return;}
            double closing=Vector3D.Dot(s.Velocity-t.Sample.Velocity,displacement/Distance);
            if(closing>RendezvousMath.ClosingLimit(Distance-MinimumSeparation(s),rcsAuthority,s.TurnAllowanceSeconds)*1.1)
            {Abort("Closing speed exceeds verified separation envelope; manual control required.");return;}
            Vector3D desired=intercept?RendezvousMath.GoalVelocity(displacement,t.Sample.Velocity,standOff,Math.Min(acceleration,rcsAuthority),s.TurnAllowanceSeconds,cap):t.Sample.Velocity;
            Vector3D error=desired-s.Velocity;
            bool close=Distance<standOff+500;
            if(intercept&&close&&RelativeSpeed<1){intercept=false;arrived=true;desired=t.Sample.Velocity;error=desired-s.Velocity;log("INTERCEPT ARRIVED // switching to velocity match");}
            // At short range use only RCS, avoiding an uncontrolled main-engine turn near the target.
            bool rcs=c.MatchRcsOnly||error.Length()<10||close;
            s.RcsOnly=rcs;
            try
            {
                if(rcs){s.ApplyDockRotation(Vector3D.Zero);s.ApplyWorldAcceleration(error/2,1);}
                else if(error.Length()>1)
                {
                    Vector3D direction=Vector3D.Normalize(error);s.Orient(direction,.5);
                    if(!alignmentLogged){log("TARGET ALIGN // error="+s.LastAlignmentErrorDeg.ToString("0.0")+"deg / tracking="+t.Sample.Label);alignmentLogged=true;}
                    if(s.LastAlignmentErrorDeg<1&&s.LastAngularRateDeg<.3)s.ApplyWorldAcceleration(direction*Math.Min(acceleration,error.Length()/2),1);
                    else
                    {
                        // Retain velocity and correct the course with bounded RCS while
                        // the main-drive nose is still turning toward the target.
                        s.RcsOnly=true;
                        s.ApplyWorldAcceleration(DockingMath.Limit(error/4,rcsAuthority),1);
                    }
                }
            }
            catch{s.CancelThrustFrame();throw;}
            settled=RelativeSpeed<.5?settled+1:0;
            Status=Mode+" / relative "+RelativeSpeed.ToString("0.0")+" m/s / "+(Distance/1000).ToString("0.0")+" km / SIG "+ceiling.ToString("0");
            if(!intercept&&settled>=60&&!c.MatchKeep){Abort(arrived?"INTERCEPT COMPLETE / VELOCITY MATCHED":"VELOCITY MATCHED / controls released");s.SetDampeners(false);}
        }
    }
}
