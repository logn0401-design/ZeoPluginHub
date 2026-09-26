using System;
using System.Collections.Generic;
using System.Linq;
using ProtoBuf;
using Sandbox.ModAPI;
using VRageMath;
namespace ZeoNav
{
    #pragma warning disable 0649 // Fields are filled by the Spectrum protobuf decoder.
    [ProtoContract] internal sealed class TargetDetection
    {
        [ProtoMember(1)] public long EmitterId;
        [ProtoMember(2)] public Vector3D Position;
        [ProtoMember(3)] public Vector3D Velocity;
        [ProtoMember(4)] public float Strength;
        [ProtoMember(5)] public string FactionTag;
        [ProtoMember(6)] public Dictionary<string,float> EmissionTags;
        [ProtoMember(7)] public int DetectedAt;
        [ProtoMember(8)] public bool SelfOwned;
        [ProtoMember(9)] public string DetailText;
        internal string Label {get{return (FactionTag??"UNKNOWN")+" / "+(DetailText??"CONTACT")+" #"+Math.Abs(EmitterId%1000000).ToString("000000");}}
    }
    #pragma warning restore 0649
    internal sealed class TargetTrack
    {
        internal TargetDetection Sample;
        internal int Consistent;
        internal double ArrivalTime;
        internal Vector3D EstimatedAcceleration;
        // Spectrum exposes a signal on its HUD for up to 15 seconds. A pilot may
        // lock that signal even without a streamed grid; autonomous flight still
        // requires the shorter, three-sample Fresh guard below.
        internal bool Selectable(int tick,double now){return Sample!=null&&tick>=Sample.DetectedAt&&tick-Sample.DetectedAt<=900&&now-ArrivalTime<=15;}
        internal bool Fresh(int tick,double now){return Sample!=null&&Consistent>=3&&tick>=Sample.DetectedAt&&tick-Sample.DetectedAt<=120&&now-ArrivalTime<=2;}
        internal void Accept(TargetDetection next,double now)
        {
            if(next==null||!WorldMotion.Finite(next.Position)||!WorldMotion.Finite(next.Velocity)||next.Velocity.Length()>100000){Consistent=0;return;}
            if(Sample!=null&&next.DetectedAt==Sample.DetectedAt)return;
            double dt=Sample==null?0:((long)next.DetectedAt-Sample.DetectedAt)/60d;
            bool continuous=Sample!=null&&dt>0&&dt<=2&&(next.Position-Sample.Position-(next.Velocity+Sample.Velocity)*(.5*dt)).Length()<=Math.Max(100,next.Velocity.Length()*.1);
            EstimatedAcceleration=Sample!=null&&dt>0&&dt<5?(next.Velocity-Sample.Velocity)/dt:Vector3D.Zero;
            Consistent=continuous?Consistent+1:1;Sample=next;ArrivalTime=now;
        }
        internal Vector3D Position(int tick){double dt=Math.Max(0,Math.Min(15,((long)tick-Sample.DetectedAt)/60d));return Sample.Position+Sample.Velocity*dt+EstimatedAcceleration*(dt*dt*.5);}
    }
    internal sealed class TargetTracker
    {
        internal bool Selecting,Confirmed;
        internal long LockedId,CandidateId;
        internal string Status="Hold Left Ctrl and click a Spectrum signal to lock.";
        internal readonly Dictionary<long,TargetTrack> Tracks=new Dictionary<long,TargetTrack>();
        private readonly List<long> candidates=new List<long>();
        private int cycle;
        private Vector2D lastPointer;
        private bool hasPointer;
        internal TargetTrack Locked {get{TargetTrack t;return Tracks.TryGetValue(LockedId,out t)?t:null;}}
        internal void Reset(){Tracks.Clear();LockedId=CandidateId=0;Confirmed=Selecting=false;cycle=0;hasPointer=false;}
        internal void Select()
        {
            if(!Selecting){Selecting=true;CandidateId=0;cycle=0;hasPointer=false;Status="Move cursor over a Spectrum signal and click to lock.";return;}
            if(CandidateId==0){Status="No fresh contact under reticle.";return;}
            LockedId=CandidateId;Confirmed=true;Selecting=false;Status="SIGNAL LOCKED / "+Locked.Sample.Label;
        }
        internal void Cancel(){Selecting=false;CandidateId=0;hasPointer=false;}
        internal void ClearLock(){Cancel();Confirmed=false;LockedId=0;Status="TARGET CLEARED / hold Left Ctrl to select again";}
        internal void Cycle(){cycle++;}
        internal void Aim(int tick,double now,MatrixD camera,Func<Vector3D,Vector3D> project,Vector2D pointer,int width,int height)
        {
            if(!Selecting)return;
            if(!TargetPointer.Valid(pointer,width,height)){CandidateId=0;return;}
            if(!hasPointer||TargetPointer.DistanceSquared(lastPointer,pointer,width,height)>100){cycle=0;lastPointer=pointer;hasPointer=true;}
            candidates.Clear();double radius=TargetPointer.Radius(height);
            candidates.AddRange(Tracks.Where(p=>p.Value.Selectable(tick,now)).Select(p=>new{p.Key,Position=p.Value.Position(tick)})
                .Where(p=>Vector3D.Dot(p.Position-camera.Translation,camera.Forward)>0)
                .Select(p=>new{p.Key,Screen=project(p.Position)})
                .Where(p=>WorldMotion.Finite(p.Screen)&&Math.Abs(p.Screen.X)<=1&&Math.Abs(p.Screen.Y)<=1)
                .Select(p=>new{p.Key,Distance=TargetPointer.DistanceSquared(new Vector2D(p.Screen.X,p.Screen.Y),pointer,width,height)})
                .Where(p=>p.Distance<=radius*radius).OrderBy(p=>p.Distance).ThenBy(p=>p.Key).Take(32).Select(p=>p.Key));
            CandidateId=candidates.Count==0?0:candidates[cycle%candidates.Count];
        }
        internal void Update(List<TargetDetection> detections,ShipContext own,int tick,double now,MatrixD camera,bool centerAim=true)
        {
            foreach(var d in detections.Take(2048))
            {
                if(d.EmitterId==0||own==null||own.ContainsConstructGridId(d.EmitterId)||tick<d.DetectedAt||tick-d.DetectedAt>900)continue;
                TargetTrack track;
                if(!Tracks.TryGetValue(d.EmitterId,out track))Tracks[d.EmitterId]=track=new TargetTrack();
                track.Accept(d,now);
            }
            // Spectrum contacts can disappear from one poll while a ship turns.
            // Keep their bounded 15-second display history; flight still requires
            // the separate two-second Fresh check before any thrust is allowed.
            foreach(long id in Tracks.Keys.ToArray())if(!Tracks[id].Selectable(tick,now))Tracks.Remove(id);
            if(Confirmed&&(Locked==null||!Locked.Selectable(tick,now))){Confirmed=false;Status="SIGNAL LOST / select and confirm again";}
            if(!Selecting||!centerAim)return;
            candidates.Clear();
            candidates.AddRange(Tracks.Where(p=>p.Value.Selectable(tick,now)).Select(p=>new {p.Key,Dot=Vector3D.Dot(Vector3D.Normalize(p.Value.Position(tick)-camera.Translation),camera.Forward)})
                .Where(p=>p.Dot>=Math.Cos(5*Math.PI/180)).OrderByDescending(p=>p.Dot).ThenBy(p=>p.Key).Take(32).Select(p=>p.Key));
            CandidateId=candidates.Count==0?0:candidates[cycle%candidates.Count];
        }
    }
    internal static class TargetPointer
    {
        internal static Vector2D FromPixels(double x,double y,int width,int height){return new Vector2D(x*2/width-1,1-y*2/height);}
        internal static bool Valid(Vector2D pointer,int width,int height){return width>=200&&height>=200&&SignalBudget.Finite(pointer.X)&&SignalBudget.Finite(pointer.Y)&&Math.Abs(pointer.X)<=1&&Math.Abs(pointer.Y)<=1;}
        internal static double DistanceSquared(Vector2D a,Vector2D b,int width,int height){double x=(a.X-b.X)*width*.5,y=(a.Y-b.Y)*height*.5;return x*x+y*y;}
        internal static double Radius(int height){return Math.Max(32,Math.Min(96,height*64d/1080));}
    }
    internal static class RendezvousMath
    {
        internal static double ClosingLimit(double distance,double acceleration,double turnSeconds)
        {
            if(!SignalBudget.Finite(distance)||!SignalBudget.Finite(acceleration)||!SignalBudget.Finite(turnSeconds)||distance<=0||acceleration<=0)return 0;
            // Solve d = v*T + v^2/(2a), with half the acceleration reserved for uncertainty.
            double a=acceleration*.5,t=Math.Max(2,turnSeconds+2);
            return Math.Max(0,Math.Sqrt(a*a*t*t+2*a*distance)-a*t);
        }
        internal static Vector3D GoalVelocity(Vector3D relativePosition,Vector3D targetVelocity,double standOff,double acceleration,double turnSeconds,double cap)
        {
            double length=relativePosition.Length();
            if(length<1)return targetVelocity;
            double closing=ClosingLimit(Math.Max(0,length-standOff),acceleration,turnSeconds);
            return DockingMath.Limit(targetVelocity+relativePosition/length*closing,cap);
        }
    }
}
