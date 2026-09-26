using System;
using System.Collections.Generic;
using VRageMath;
namespace ZeoNav
{
    internal sealed class PilotInputGate
    {
        private double steeringSince=double.NaN;
        internal void Reset(){steeringSince=double.NaN;}
        internal bool Observe(Vector3 move,Vector2 rotation,float roll,double time)
        {
            // Translation/roll keys and a decisive mouse movement remain immediate.
            if(move.LengthSquared()>.01f||Math.Abs(roll)>.05f||rotation.LengthSquared()>=9){Reset();return true;}
            if(rotation.LengthSquared()<=.1225f){Reset();return false;}
            if(double.IsNaN(steeringSince)||time<steeringSince)steeringSince=time;
            return time-steeringSince>=.2;
        }
    }
    internal static class MomentumCapture
    {
        internal static bool InWindow(Vector3D velocity,Vector3D direction)
        {
            double speed=velocity.Length();
            return WorldMotion.Finite(velocity)&&WorldMotion.Finite(direction)&&speed>5&&direction.LengthSquared()>.99&&
                Vector3D.Dot(velocity,direction)/speed>=Math.Cos(25*Math.PI/180);
        }
        internal static bool HasRoom(double distance,double stoppingDistance,double closing,double lateral,double sideAcceleration,double alignSeconds)
        {
            if(!SignalBudget.Finite(distance)||!SignalBudget.Finite(stoppingDistance)||closing<=0||distance<=stoppingDistance)return false;
            if(lateral>1&&sideAcceleration<.01)return false;
            double correctionSeconds=lateral<=1?0:lateral/sideAcceleration;
            return distance-stoppingDistance>closing*(Math.Max(0,alignSeconds)+correctionSeconds)*1.5;
        }
    }
    internal enum MotionDecision { Clear, Hold, Recovered, Abort }
    internal sealed class MotionRevalidation
    {
        private double since=double.NaN;
        private readonly Queue<double> episodes=new Queue<double>();
        internal bool Waiting {get{return !double.IsNaN(since);}}
        internal string Reason {get;private set;}="";
        internal void Reset(){since=double.NaN;episodes.Clear();Reason="";}
        internal MotionDecision Observe(WorldMotion motion,double time)
        {
            if(Waiting&&(time<since||time-since>2)){Reason="SERVER MOTION DID NOT RECOVER WITHIN 2 SECONDS";return MotionDecision.Abort;}
            if(motion.Ready){if(Waiting){since=double.NaN;return MotionDecision.Recovered;}return MotionDecision.Clear;}
            if(!motion.RecoverableFault){Reason="VELOCITY UNVERIFIED / "+motion.Source;return MotionDecision.Abort;}
            if(!Waiting)
            {
                while(episodes.Count>0&&time-episodes.Peek()>30)episodes.Dequeue();
                if(episodes.Count>=3){Reason="REPEATED SERVER MOTION CORRECTIONS / MANUAL CONTROL REQUIRED";return MotionDecision.Abort;}
                episodes.Enqueue(time);since=time;
            }
            return MotionDecision.Hold;
        }
    }
}
