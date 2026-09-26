using System;
using VRage.Game.ModAPI;
using VRageMath;
namespace ZeoNav
{
    internal struct DockBox
    {
        internal Vector3D Center,Half,Right,Up,Back;
        internal DockBox(Vector3D center,Vector3D half,MatrixD frame)
        {Center=center;Half=half;Right=frame.Right;Up=frame.Up;Back=frame.Backward;}
        internal static DockBox World(BoundingBoxD box){return new DockBox(box.Center,box.HalfExtents,MatrixD.Identity);}
        internal static DockBox Block(IMySlimBlock block,BoundingBoxD fallback)
        {
            var grid=block.CubeGrid;
            if(grid==null||grid.GridSize<=0)return World(fallback);
            var min=(Vector3D)block.Min;var max=(Vector3D)block.Max;
            return new DockBox(Vector3D.Transform((min+max)*(.5*grid.GridSize),grid.WorldMatrix),(max-min+Vector3D.One)*(.5*grid.GridSize),grid.WorldMatrix);
        }
        internal double Radius(Vector3D axis){return Math.Abs(Vector3D.Dot(axis,Right))*Half.X+Math.Abs(Vector3D.Dot(axis,Up))*Half.Y+Math.Abs(Vector3D.Dot(axis,Back))*Half.Z;}
        private Vector3D Axis(int i){return i==0?Right:i==1?Up:Back;}
        // Exact continuous SAT for two fixed-orientation boxes under linear translation.
        // A diagonal swept AABB is only a broad phase: its empty corners are not blockers.
        internal static bool Sweep(DockBox a,DockBox b,Vector3D travel,double padding=.1)
        {
            double enter=0,exit=1;
            for(int i=0;i<3;i++)if(!Overlap(a,b,travel,a.Axis(i),padding,ref enter,ref exit))return false;
            for(int i=0;i<3;i++)if(!Overlap(a,b,travel,b.Axis(i),padding,ref enter,ref exit))return false;
            for(int i=0;i<3;i++)for(int j=0;j<3;j++)if(!Overlap(a,b,travel,Vector3D.Cross(a.Axis(i),b.Axis(j)),padding,ref enter,ref exit))return false;
            return true;
        }
        private static bool Overlap(DockBox a,DockBox b,Vector3D travel,Vector3D axis,double padding,ref double enter,ref double exit)
        {
            double length=axis.Length();if(length<1e-8)return true;
            double radius=a.Radius(axis)+b.Radius(axis)+padding*length;
            double position=Vector3D.Dot(a.Center-b.Center,axis),speed=Vector3D.Dot(travel,axis);
            if(Math.Abs(speed)<1e-10)return Math.Abs(position)<=radius;
            double x=(-radius-position)/speed,y=(radius-position)/speed;
            if(x>y){double swap=x;x=y;y=swap;}
            enter=Math.Max(enter,x);exit=Math.Min(exit,y);return enter<=exit;
        }
    }
}
