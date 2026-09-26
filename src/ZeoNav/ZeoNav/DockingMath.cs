using System;
using System.Reflection;
using System.Collections.Generic;
using VRageMath;

namespace ZeoNav
{
    internal enum DockStage { Idle, Prepare, Clear, Align, Approach, Capture }
    internal static class DockingMath
    {
        internal static bool SameConstruct(IEnumerable<long> expected,IEnumerable<long> current)
        {return new HashSet<long>(expected).SetEquals(current);}
        private static readonly PropertyInfo connectionPosition=typeof(Sandbox.Game.Entities.Cube.MyShipConnector).GetProperty("ConnectionPosition",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
        private static readonly MethodInfo constraintPosition=typeof(Sandbox.Game.Entities.Cube.MyShipConnector).GetMethod("ConstraintPositionWorld",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
        private static readonly FieldInfo connectorDummy=typeof(Sandbox.Game.Entities.Cube.MyShipConnector).GetField("m_connectorDummyLocal",BindingFlags.Instance|BindingFlags.NonPublic);
        internal static bool Finite(Vector3D v) { return SignalBudget.Finite(v.X)&&SignalBudget.Finite(v.Y)&&SignalBudget.Finite(v.Z); }
        internal static Vector3D PortPosition(Sandbox.ModAPI.IMyShipConnector port)
        {
            // AutoDock (OwendB1) uses Keen's actual constraint point for translation.
            // ConnectionPosition is the visual dummy and can differ on modded blocks.
            var native=port as Sandbox.Game.Entities.Cube.MyShipConnector;
            if(native==null||constraintPosition==null)throw new InvalidOperationException("Connector mating geometry is unavailable for this game/block type.");
            var point=(Vector3D)constraintPosition.Invoke(native,null);
            if(!Finite(point))throw new InvalidOperationException("Invalid connector mating geometry.");
            return point;
        }
        // Adapted from OwendB1/AutoDock face geometry; see THIRD_PARTY_NOTICES.
        internal static MatrixD PortFrame(Sandbox.ModAPI.IMyShipConnector port)
        {
            var native=port as Sandbox.Game.Entities.Cube.MyShipConnector;
            if(native==null||connectorDummy==null||connectionPosition==null)throw new InvalidOperationException("Connector face geometry is unavailable.");
            var dummy=(Matrix)connectorDummy.GetValue(native);
            MatrixD dummyWorld=(MatrixD)dummy*port.CubeGrid.WorldMatrix;
            // ConnectionPosition has already been transformed into grid coordinates.
            dummyWorld.Translation=Vector3D.Transform((Vector3)connectionPosition.GetValue(native),port.CubeGrid.WorldMatrix);
            var modelCenter=Vector3D.Transform(native.PositionComp.LocalAABB.Center,port.WorldMatrix);
            return FaceFrame(PortPosition(port),dummyWorld,modelCenter,port.WorldMatrix);
        }
        internal static MatrixD FaceFrame(Vector3D constraint,MatrixD dummy,Vector3D modelCenter,MatrixD block)
        {
            var normal=dummy.Translation-modelCenter;
            if(normal.LengthSquared()<.0001)normal=dummy.Forward;
            if(normal.LengthSquared()<.0001)normal=block.Forward;
            if(!Finite(normal)||normal.LengthSquared()<.0001)throw new InvalidOperationException("Invalid connector face normal.");
            normal.Normalize();var up=dummy.Up-normal*Vector3D.Dot(dummy.Up,normal);
            if(up.LengthSquared()<.0001)up=dummy.Right-normal*Vector3D.Dot(dummy.Right,normal);
            if(up.LengthSquared()<.0001)up=block.Up-normal*Vector3D.Dot(block.Up,normal);
            if(!Finite(up)||up.LengthSquared()<.0001||!Finite(constraint))throw new InvalidOperationException("Invalid connector face orientation.");
            return MatrixD.CreateWorld(constraint,normal,Vector3D.Normalize(up));
        }
        internal static Vector3D ApproachAcceleration(Vector3D error,Vector3D velocity,Vector3D outward,double authority,double speed,bool final)
        {
            // Correct lateral error before consuming the remaining axial gap.
            var lateral=error-outward*Vector3D.Dot(error,outward);
            var desired=Velocity(error,authority,speed);
            if(final&&lateral.Length()>.2)desired=Velocity(lateral,authority,speed);
            var acceleration=Limit((desired-velocity)/(final?.75:1.5),authority);
            if(error.Length()>.01)
            {
                var direction=Vector3D.Normalize(error);double closing=Vector3D.Dot(velocity,direction);
                // The final goal already includes a physical stand-off; a second padding
                // would stall short of capture, as AutoDock's final-approach split avoids.
                if(closing>Math.Sqrt(2*authority*Math.Max(0,error.Length()-(final?0:.1))))acceleration=-direction*authority;
            }
            return acceleration;
        }
        internal static double SpeedLimit(DockStage stage,double gap,double lateral,double authority,double transit,double approach)
        {
            transit=Math.Max(.2,Math.Min(6,transit));approach=Math.Min(transit,Math.Max(.2,Math.Min(2,approach)));
            if(stage==DockStage.Prepare||stage==DockStage.Idle)return 0;
            if(stage==DockStage.Align)return approach;
            if(stage!=DockStage.Capture)return transit;
            // Final starts at the staging point, not at the magnet. Reserve slow
            // capture speed for the last 3 m, with a braking envelope before each zone.
            double capture=Math.Min(.25,approach);
            if(lateral>.2)return capture;
            double a=Math.Max(0,authority)*.5; // reserve half the commanded braking authority
            // Aim to attain each limit 3 m early, allowing tracking error to settle.
            return Math.Min(transit,Math.Min(BrakeEnvelope(approach,gap-13,a),BrakeEnvelope(capture,gap-6,a)));
        }
        private static double BrakeEnvelope(double finalSpeed,double distance,double acceleration)
        {
            if(distance<=0||acceleration<=0)return finalSpeed;
            // d = v*1.5 + (v^2 - finalSpeed^2)/(2a), including controller response time.
            double response=1.5*acceleration;
            return Math.Max(finalSpeed,Math.Sqrt(response*response+finalSpeed*finalSpeed+2*acceleration*distance)-response);
        }
        internal static bool FinalReady(Vector3D ownPoint,Vector3D targetPoint,Vector3D outward,double angle,double angularSpeed,double standOff)
        {
            var delta=ownPoint-targetPoint;double gap=Vector3D.Dot(delta,outward);
            return angle<.5&&angularSpeed<.15*Math.PI/180&&gap>=0&&gap<=standOff&&Lateral(delta,outward)<.2;
        }
        internal static Vector3D CenterGoal(Vector3D center,Vector3D ownFace,Vector3D targetFace,Vector3D targetOutward,double gap)
        {return targetFace+targetOutward*gap-(ownFace-center);}
        internal static bool SamePose(MatrixD a,MatrixD b)
        {return Vector3D.DistanceSquared(a.Translation,b.Translation)<.0004&&Vector3D.Dot(a.Forward,b.Forward)>.999999&&Vector3D.Dot(a.Up,b.Up)>.999999;}
        internal static Vector3D Limit(Vector3D v,double max) { double n=v.Length();return n>max&&n>0?v*(max/n):v; }
        internal static Vector3D RotationRate(MatrixD current, MatrixD desired, Vector3D omega, out double degrees)
        {
            current.Translation=desired.Translation=Vector3D.Zero;
            var q=QuaternionD.CreateFromRotationMatrix(MatrixD.Transpose(current)*desired);
            q.Normalize(); if(q.W<0)q=-q;
            var axis=new Vector3D(q.X,q.Y,q.Z);double n=axis.Length();
            double angle=2*Math.Atan2(n,Math.Max(0,q.W));degrees=angle*180/Math.PI;
            var error=n>1e-10?axis*(angle/n):Vector3D.Zero;
            return Limit(1.2*error-.8*omega,5*Math.PI/180);
        }
        internal static Vector3D Velocity(Vector3D error,double acceleration,double speedLimit)
        {
            double distance=error.Length();
            if(distance<.01)return Vector3D.Zero;
            // Conservative braking allowance; reduce speed continuously near the target.
            double speed=Math.Min(speedLimit,Math.Min(distance*.35,Math.Sqrt(Math.Max(0,acceleration*distance))));
            return error*(speed/distance);
        }
        internal static bool InFront(Vector3D offset,Vector3D outward,double clearance)
        { return Vector3D.Dot(offset,outward)>=clearance; }
        internal static double Lateral(Vector3D offset,Vector3D normal)
        { return (offset-normal*Vector3D.Dot(offset,normal)).Length(); }
    }
}
