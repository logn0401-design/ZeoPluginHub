using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;

namespace ZeoPDC
{
    internal static class PlayerTargetingPolicy
    {
        // Enforced at the shared terminal writer, including legacy experiment paths.
        internal static bool IsFilter(string key)
        { return key=="WC_Grids" || key=="WC_LargeGrid" || key=="WC_SmallGrid" ||
            key=="WC_Projectiles" || key=="WC_Neutrals" || key=="WC_Unowned" ||
            key=="WC_Friendly" || key=="WC_Biologicals" || key=="WC_Meteors" || key=="WC_FocusFire"; }

        internal static double? ShipRate(double meters,bool fresh,bool gridsEnabled,bool nativeProjectile,PdcConfig c)
        {
            if(!fresh || !gridsEnabled || nativeProjectile || !BankPlanner.Finite(meters) || meters<0 || meters>3000) return null;
            return meters<=900?c.NearRof:meters<=1200?c.CloseRof:meters<=1500?c.MidRof:meters<=2200?c.MidFarRof:c.FarRof;
        }
    }

    internal sealed partial class PdcEngine
    {
        readonly List<MyTuple<MyEntity,float>> shipCadenceCandidates=new List<MyTuple<MyEntity,float>>();
        readonly List<BoundingBoxD> shipCadenceBounds=new List<BoundingBoxD>();
        int shipCadenceFrame=-1;

        void RefreshShipCadenceThreats()
        {
            shipCadenceBounds.Clear();shipCadenceCandidates.Clear();shipCadenceFrame=-1;
            var player=MyAPIGateway.Session?.Player;
            if(grid==null || player==null || !wc.TryGetThreats(grid as MyEntity,shipCadenceCandidates))return;
            foreach(var pair in shipCadenceCandidates) {
                var candidate=pair.Item1 as MyCubeGrid;
                if(candidate==null || candidate.Closed || candidate.MarkedForClose || candidate.EntityId==grid.EntityId)continue;
                // WC sorted candidates alone do not prove hostility. Require known
                // owners and reject a friendly, neutral or unowned grid.
                bool enemy=candidate.BigOwners.Count>0;
                foreach(long owner in candidate.BigOwners)
                    if(MyIDModule.GetRelationPlayerBlock(owner,player.IdentityId)!=MyRelationsBetweenPlayerAndBlock.Enemies) {enemy=false;break;}
                if(enemy)shipCadenceBounds.Add(candidate.PositionComp.WorldAABB);
            }
            shipCadenceFrame=frame;
        }

        double? ShipCadenceRate(Gun g,PdcConfig c)
        {
            bool gridsEnabled;
            bool fresh=g.TelemetryFrame==frame && g.ScopeValid;
            if(!fresh || !TryReadTerminal(g.Block,"WC_Grids",out gridsEnabled) || !gridsEnabled)return null;
            // A live projectile identity always takes priority over nearby ships.
            bool projectile=g.NativeRead=="PROJECTILE" || g.TargetFlag2;
            double distance=double.PositiveInfinity;
            if(g.WcTargetValid && g.WcTargetIsGrid && g.TargetRead=="OK")
                distance=Vector3D.Distance(g.ScopeOrigin,g.WcTargetPos);
            else if(g.NativeRead=="NO_TARGET" && shipCadenceFrame==frame)
                foreach(var box in shipCadenceBounds) {
                    var nearest=Vector3D.Clamp(g.ScopeOrigin,box.Min,box.Max);
                    distance=Math.Min(distance,Vector3D.Distance(g.ScopeOrigin,nearest));
                }
            return PlayerTargetingPolicy.ShipRate(distance,fresh,gridsEnabled,projectile,c);
        }
    }
}
