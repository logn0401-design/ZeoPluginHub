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
        private readonly Func<double> elapsed;
        private SignalBudget budget,rcsBudget,preparedBudget,preparedRcs;
        private readonly InterceptBurnGate burnGate=new InterceptBurnGate();
        private ShipContext preparedShip;
        private long preparedId;
        private bool preparedChase,preparedRcsOnly;
        private int preparedTopology;
        private double preparedAt=double.NegativeInfinity;
        private double sigWaitSince=-1,lastControlLog=-1;
        private bool firstTurn,firstMain,firstRcs;
        private string burnStatus="";
        private int topology,settled;
        private double staleSince=-1;
        private bool alignmentLogged;
        private long lockedId;
        private bool intercept,arrived,rcsSetting;
        internal bool Active {get;private set;}
        internal bool RequestedIntercept {get;private set;}
        internal string Status="Target flight idle.";
        internal double Distance,RelativeSpeed;
        internal double Ceiling {get{return ApproachProfile.Effective(config(),true,true);}}
        internal string Mode {get{return intercept?"INTERCEPT":config().MatchKeep?"KEEP MATCHED":"MATCH ONCE";}}
        internal TargetFlight(Func<ShipContext> s,Func<NavConfig> c,Func<SpectrumAdapter> sp,TargetTracker t,Action<string> logger,Func<double> timer=null)
        {ship=s;config=c;spectrum=sp;tracker=t;log=logger;elapsed=timer??(()=>clock.Elapsed.TotalSeconds);}
        internal bool CanStart(bool chase,int tick,double now)
        {
            preparedAt=double.NegativeInfinity;
            var s=ship();var target=tracker.Locked;
            if(s==null||!tracker.Confirmed||target==null){Status="Lock a Spectrum signal first.";return false;}
            if(!target.Fresh(tick,now)){Status="Signal locked; waiting for three fresh tracking samples before flight.";return false;}
            if(!s.Motion.Ready||s.HasDockConnection()||s.Gravity.Length()>.05){Status="Requires verified motion, undocked ship and open space.";return false;}
            if(Ceiling>0&&(spectrum()==null||!spectrum().DriveKmReady)){Status="Wait for fresh own Spectrum SIG before target flight.";return false;}
            Distance=Vector3D.Distance(target.Position(tick),s.Position);
            if(Distance<MinimumSeparation(s)){Status="Too close for target-flight preview; use manual RCS.";return false;}
            if(chase&&!InterceptHasRoom(Distance,config().InterceptStandOffKm*1000,MinimumSeparation(s)))
            {
                Status="Already inside intercept stand-off ("+config().InterceptStandOffKm.ToString("0")+" km); lower it or choose Match Velocity.";
                log("TARGET FLIGHT PREFLIGHT // "+Status);
                return false;
            }
            Vector3D relativePosition=target.Position(tick)-s.Position,relativeVelocity=target.Sample.Velocity-s.Velocity;
            double closestTime=relativeVelocity.LengthSquared()>1?Math.Max(0,Math.Min(12,-Vector3D.Dot(relativePosition,relativeVelocity)/relativeVelocity.LengthSquared())):0;
            if((relativePosition+relativeVelocity*closestTime).Length()<MinimumSeparation(s)*2){Status="Closing too fast to acquire safely; match manually first.";return false;}
            s.Scan();if(s.Gyros.Count==0){Status="No available gyro.";return false;}
            s.RefreshSignatureTopology();
            var c=config();
            var feed=spectrum();
            string capSource;
            double cap=SpeedCapResolver.Resolve(s.Grid,c.SpeedCapOverride,s.Velocity.Length(),out capSource);
            if(target.Sample.Velocity.Length()>cap){Status="Target velocity exceeds your speed cap.";return false;}
            if(chase)
            {
                Vector3D direction=Vector3D.Normalize(relativePosition);
                double along=Vector3D.Dot(target.Sample.Velocity,direction);
                double lateral2=Math.Max(0,target.Sample.Velocity.LengthSquared()-along*along);
                if(Math.Sqrt(Math.Max(0,cap*cap-lateral2))-along<.5){Status="Cannot catch target within speed cap.";return false;}
            }
            try
            {
                preparedBudget=Ceiling==0?new SignalBudget{Ready=true,TargetKm=0}:feed.BuildBudget(s,c.MatchRcsOnly);
                preparedRcs=Ceiling==0?new SignalBudget{Ready=true,TargetKm=0}:feed.BuildBudget(s,true);
            }
            catch(Exception e)
            {
                Status="Cannot verify target-flight SIG model; existing flight continues.";
                log("TARGET FLIGHT PREFLIGHT // SIG MODEL FAILED / "+e.GetType().Name+" / "+e.Message);
                return false;
            }
            if(Ceiling>0)
            {
                bool reuse=InterceptEnvelope.ReuseBaseline(preparedBudget,s.SignatureBudget);
                if(!reuse)
                {
                    // The current own reading is an upper bound, including any
                    // ongoing burn. Double-counting is conservative; never invent
                    // an idle baseline or wait motionless just to calibrate it.
                    preparedBudget.SphericalBaseSquared=feed.SphericalWeakKm*feed.SphericalWeakKm;
                    preparedBudget.DirectionalBaseSquared=feed.DirectionalWeakKm*feed.DirectionalWeakKm;
                }
                preparedRcs.SphericalBaseSquared=preparedBudget.SphericalBaseSquared;
                preparedRcs.DirectionalBaseSquared=preparedBudget.DirectionalBaseSquared;
                preparedRcs.FeedbackScale=preparedBudget.FeedbackScale;
                preparedBudget.Ready=preparedRcs.Ready=true;
                preparedBudget.TargetKm=preparedRcs.TargetKm=Ceiling;
            }
            double main,rcs;
            Authority(s,c,preparedBudget,preparedRcs,out main,out rcs);
            if(main<.01||rcs<.01){Status="Insufficient thrust within MAX SIG; allow the current burn's signal to fall or raise MAX SIG.";return false;}
            double closing=Vector3D.Dot(s.Velocity-target.Sample.Velocity,Vector3D.Normalize(relativePosition));
            double standOff=Math.Max(c.InterceptStandOffKm*1000,MinimumSeparation(s)*2);
            double limit=InterceptEnvelope.Closing(Distance,standOff,MinimumSeparation(s),main,rcs,s.TurnAllowanceSeconds,c.MatchRcsOnly);
            if(closing>limit)
            {
                Status="Closing too fast for verified turn and braking room; existing flight continues.";
                log("TARGET FLIGHT PREFLIGHT // closing="+closing.ToString("0.0")+" limit="+limit.ToString("0.0")+" distance="+Distance.ToString("0")+" main="+main.ToString("0.00")+" rcs="+rcs.ToString("0.00"));
                return false;
            }
            preparedShip=s;preparedId=tracker.LockedId;preparedTopology=s.TopologyRevision;preparedAt=now;preparedChase=chase;preparedRcsOnly=c.MatchRcsOnly;
            return true;
        }
        internal void Start(bool chase,int tick,double now)
        {
            if(Active){Abort("Target flight cancelled.");return;}
            bool prepared=preparedShip==ship()&&preparedId==tracker.LockedId&&tracker.Confirmed&&tracker.Locked!=null&&tracker.Locked.Fresh(tick,now)&&
                preparedChase==chase&&preparedRcsOnly==config().MatchRcsOnly&&now>=preparedAt&&now-preparedAt<1&&
                preparedTopology==ship().TopologyRevision&&preparedBudget!=null&&preparedBudget.TargetKm==Ceiling;
            if(!prepared&&!CanStart(chase,tick,now))return;
            var s=ship();
            RequestedIntercept=chase;intercept=chase;arrived=false;rcsSetting=config().MatchRcsOnly;lockedId=tracker.LockedId;budget=preparedBudget;rcsBudget=preparedRcs;settled=0;staleSince=sigWaitSince=-1;alignmentLogged=false;input.Reset();motion.Reset();burnGate.Reset();clock.Restart();
            preparedAt=double.NegativeInfinity;lastControlLog=-1;firstTurn=firstMain=firstRcs=false;burnStatus="ACQUIRING HEADING";
            s.SaveDampenersOnce();s.SetDampeners(false);s.BeginThrustControl();s.BeginGyroControl();s.ClearThrust();
            s.SignatureBudget=budget;topology=s.TopologyRevision;
            Active=true;Status="ACQUIRING HEADING";log("TARGET FLIGHT START // "+Mode+" id="+lockedId+" distance="+Distance+" SIG READY / turn available immediately");
        }
        private double MinimumSeparation(ShipContext s){return Math.Max(500,s.Grid.WorldVolume.Radius*3);}
        internal static bool InterceptHasRoom(double distance,double standOff,double separation)
        {return SignalBudget.Finite(distance)&&SignalBudget.Finite(standOff)&&SignalBudget.Finite(separation)&&distance>Math.Max(standOff,separation*2);}
        internal void Abort(string reason)
        {bool wasActive=Active;Active=false;Status=reason;clock.Stop();if(wasActive){ship()?.ReleaseAll(false);log("TARGET FLIGHT RELEASE // "+reason);}}
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
            if(s==null){Abort("CONTROLLED SHIP LOST");return;}
            if(tracker.LockedId!=lockedId){Abort("TARGET CHANGED / old="+lockedId+" new="+tracker.LockedId+" / re-engage to confirm new course");return;}
            if(!tracker.Confirmed||t==null){Abort("TARGET SIGNAL EXPIRED / id="+lockedId+" / confirm a fresh signal");return;}
            if(!t.Fresh(tick,now))
            {
                s.SetDampeners(false);s.ClearThrust();s.ApplyDockRotation(Vector3D.Zero);
                burnGate.Reset();
                if(staleSince<0){staleSince=elapsed();log("TARGET TRACK STALE // thrust off / waiting for fresh Spectrum samples");}
                Status="TARGET TRACK STALE / THRUST OFF / REACQUIRING";
                if(elapsed()-staleSince>10)Abort("TARGET SIGNAL LOST / controls released after 10s hold");
                return;
            }
            if(staleSince>=0){log("TARGET TRACK REACQUIRED // fresh samples restored");staleSince=-1;}
            var c=config();
            if(c.MatchRcsOnly!=rcsSetting){Abort("Engine setting changed; re-engage target flight.");return;}
            if(c.AbortOnManualInput&&input.Observe(s.Controller.MoveIndicator,PilotLook.Rotation(s.Controller),s.Controller.RollIndicator,tick/60d)){Abort("MANUAL PILOT INPUT");return;}
            if(s.HasDockConnection()||s.Gravity.Length()>.05){Abort("Target flight context changed.");return;}
            bool wasWaiting=motion.Waiting;
            var decision=motion.Observe(s.Motion,elapsed());
            if(decision==MotionDecision.Abort){log("TARGET MOTION ABORT // "+s.Motion.LastFault);Abort(motion.Reason);return;}
            s.SetDampeners(false);s.ClearThrust();
            if(Vector3D.Distance(t.Position(tick),s.Position)<MinimumSeparation(s)){Abort("Separation floor reached / manual control required.");return;}
            if(decision==MotionDecision.Hold){s.ApplyDockRotation(Vector3D.Zero);burnGate.Reset();Status="VERIFYING MOTION / THRUST OFF";if(!wasWaiting)log("TARGET MOTION HOLD // "+s.Motion.LastFault);return;}
            if(decision==MotionDecision.Recovered){burnGate.Reset();s.ResetAim();log("TARGET MOTION RECOVERED / replanning from verified velocity");}
            var feed=spectrum();
            if(Ceiling>0&&(feed==null||!feed.DriveKmReady)){s.ApplyDockRotation(Vector3D.Zero);burnGate.Reset();Status="WAIT OWN SIG / THRUST OFF";if(sigWaitSince<0)sigWaitSince=elapsed();if(elapsed()-sigWaitSince>10)Abort(Status);return;}
            sigWaitSince=-1;
            double ceiling=ApproachProfile.Effective(c,true,true);
            if(budget.TargetKm==0&&ceiling>0){Abort("MAX SIG enabled during unrestricted flight; re-engage for verified budget.");return;}
            budget.TargetKm=rcsBudget.TargetKm=ceiling;
            if(ceiling>0&&feed.DriveKm>ceiling){Abort("Own SIG exceeded target-flight ceiling.");return;}
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
            double acceleration,rcsAuthority;
            Authority(s,c,budget,rcsBudget,out acceleration,out rcsAuthority);
            if(acceleration<.01){Abort("Insufficient thrust within MAX SIG.");return;}
            double standOff=Math.Max(c.InterceptStandOffKm*1000,MinimumSeparation(s)*2);
            if(rcsAuthority<.01){Abort("Insufficient RCS authority within MAX SIG.");return;}
            double closing=Vector3D.Dot(s.Velocity-t.Sample.Velocity,displacement/Distance);
            double closingLimit=InterceptEnvelope.Closing(Distance,standOff,MinimumSeparation(s),acceleration,rcsAuthority,s.TurnAllowanceSeconds,c.MatchRcsOnly);
            if(closing>closingLimit*1.1)
            {log("TARGET ENVELOPE // closing="+closing.ToString("0.0")+" limit="+closingLimit.ToString("0.0")+" main="+acceleration.ToString("0.00")+" rcs="+rcsAuthority.ToString("0.00"));Abort("Closing speed exceeds verified turn/braking envelope; manual control required.");return;}
            Vector3D desired=DesiredVelocity(intercept,displacement,t.Sample.Velocity,standOff+500,c.MatchRcsOnly?rcsAuthority:acceleration,s.TurnAllowanceSeconds,cap);
            Vector3D error=desired-s.Velocity;
            bool close=Distance<standOff+500;
            if(intercept&&close&&RelativeSpeed<1){intercept=false;arrived=true;desired=t.Sample.Velocity;error=desired-s.Velocity;log("INTERCEPT ARRIVED // switching to velocity match");}
            // At short range use only RCS, avoiding an uncontrolled main-engine turn near the target.
            bool rcs=c.MatchRcsOnly||error.Length()<10||close;
            s.RcsOnly=rcs;
            s.SignatureBudget=rcs?rcsBudget:budget;
            double mainRatio=0,headingRate=0;
            try
            {
                if(rcs){s.TrackTurnBank=false;s.ApplyDockRotation(Vector3D.Zero);burnGate.Reset();burnStatus="RCS VELOCITY MATCH";s.ApplyWorldAcceleration(error/2,1);}
                else if(error.Length()>1)
                {
                    Vector3D direction=Vector3D.Normalize(error);s.TrackTurnBank=true;s.Orient(direction,.5);
                    if(!firstTurn){firstTurn=true;log("TARGET FIRST TURN // elapsed="+elapsed().ToString("0.000")+"s");}
                    if(!alignmentLogged){log("TARGET ALIGN // error="+s.LastAlignmentErrorDeg.ToString("0.0")+"deg / tracking="+t.Sample.Label);alignmentLogged=true;}
                    try{headingRate=InterceptBurnGate.HeadingRate(s.Controller.GetShipVelocities().AngularVelocity,s.Controller.WorldMatrix.Forward);}catch{headingRate=double.PositiveInfinity;}
                    mainRatio=burnGate.Observe(s.LastAlignmentErrorDeg,headingRate,elapsed());
                    if(mainRatio>0)
                    {burnStatus="MAIN / TRACKING";s.ApplyWorldAcceleration(direction*Math.Min(acceleration,error.Length()/2)*mainRatio,1);if(!firstMain){firstMain=true;log("TARGET FIRST MAIN // elapsed="+elapsed().ToString("0.000")+"s");}}
                    else
                    {
                        // Retain velocity and correct the course with bounded RCS while
                        // the main-drive nose is still turning toward the target.
                        s.RcsOnly=true;
                        s.SignatureBudget=rcsBudget;burnStatus="ALIGNING / RCS CORRECTION";
                        s.ApplyWorldAcceleration(DockingMath.Limit(error/4,rcsAuthority),1);
                    }
                }
            }
            catch{s.CancelThrustFrame();throw;}
            if(s.RcsOnly&&!firstRcs&&s.AppliedCommands.Any(p=>p>0)){firstRcs=true;log("TARGET FIRST RCS // elapsed="+elapsed().ToString("0.000")+"s");}
            if(lastControlLog<0||elapsed()-lastControlLog>=2)
            {lastControlLog=elapsed();log("TARGET CONTROL // "+burnStatus+" distance="+Distance.ToString("0")+" closing="+closing.ToString("0.0")+" allowed="+closingLimit.ToString("0.0")+" angle="+s.LastAlignmentErrorDeg.ToString("0.00")+" headingRate="+headingRate.ToString("0.00")+" mainRatio="+mainRatio.ToString("0.00")+" commandedGyros="+s.CommandedTurnGyros+" request="+s.TurnRequestDegPerSec.ToString("0.00"));}
            settled=RelativeSpeed<.5?settled+1:0;
            Status=Mode+" / "+burnStatus+" / relative "+RelativeSpeed.ToString("0.0")+" m/s / "+(Distance/1000).ToString("0.0")+" km / SIG "+ceiling.ToString("0");
            if(!intercept&&settled>=60&&!c.MatchKeep){Abort(arrived?"INTERCEPT COMPLETE / VELOCITY MATCHED":"VELOCITY MATCHED / controls released");s.SetDampeners(false);}
        }
        internal static Vector3D DesiredVelocity(bool intercept,Vector3D displacement,Vector3D targetVelocity,double standOff,double acceleration,double turnSeconds,double cap)
        {return intercept?RendezvousMath.GoalVelocity(displacement,targetVelocity,standOff,acceleration,turnSeconds,cap):targetVelocity;}
        private static void Authority(ShipContext s,NavConfig c,SignalBudget mainBudget,SignalBudget rcsModel,out double main,out double rcs)
        {
            main=(c.MatchRcsOnly?s.RcsForce(MoveDir.Forward):s.ThrusterBanks[MoveDir.Forward].Where(t=>t!=null&&!t.Closed&&t.IsWorking).Sum(t=>(double)t.MaxEffectiveThrust))/s.Mass*mainBudget.Limit(0,1,new double[6]);
            rcs=Enumerable.Range(0,6).Min(i=>s.RcsForce((MoveDir)i)/s.Mass*rcsModel.Limit(i,1,new double[6]))*.5;
        }
    }
}
