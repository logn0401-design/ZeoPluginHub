using System;
using System.Collections.Generic;
using System.Linq;
using VRageMath;

namespace ZeoPDC
{
    internal sealed partial class PdcEngine
    {
        private bool BankSurvival(Track t,PdcConfig cfg)
        { return t!=null && (t.Hull<=cfg.SurvivalHeatOverrideRangeMeters || t.Tti<=cfg.SurvivalHeatOverrideTtiSeconds); }
        private void UpdateBankRoles(PdcConfig cfg,List<Track> active)
        {
            if(!cfg.BankRolesEnabled)
            {
                foreach(var g in guns) g.BankRole.MaxRange=cfg.WeaponAwareEnabled && FreshProfile(g)?g.Profile.MaximumRange:0;
                BankRolePolicy.Update(guns.Select(g=>g.BankRole).ToList(),cfg,frame/60.0);
                foreach(var g in guns) ResolveManagedRange(g,cfg);
                return;
            }
            foreach(var g in guns)
            {
                var s=g.BankRole;
                s.Id=g.EntityId; s.Part=g.Part; s.Functional=g.Functional && g.Hp>5;
                s.Ready=g.TelemetryFrame==frame && g.ReadyRead=="OK" && g.ReadyValue && g.ScopeValid;
                s.HeatKnown=g.HeatKnown && g.TelemetryFrame==frame; s.Heat=g.Heat;
                s.MaxRange=cfg.WeaponAwareEnabled && FreshProfile(g)?g.Profile.MaximumRange:0;
                 s.RangeKnown=g.RangeReadOk && g.TelemetryFrame==frame; s.ActualRange=g.ActualRange;
                // Mount direction is stable when the turret traverses. Unknown
                // mounts stay separate rather than inventing shared coverage.
                s.Bank="UNKNOWN_"+g.EntityId.ToString();
                try
                {
                    if(controller!=null && g.Block!=null)
                    {
                        Matrix mount; g.Block.Orientation.GetMatrix(out mount);
                        var normal=Vector3D.TransformNormal(mount.Up,g.Block.CubeGrid.WorldMatrix);
                        s.Bank=BankPlanner.Side(Vector3D.TransformNormal(normal,MatrixD.Invert(controller.WorldMatrix)));
                    }
                }
                catch { s.Rank=-1; } // Detached block: keep a separate full-range bank.
                var reachable=active.Where(t=>t.LastFrame==frame && t.LostFrames==0 && BuildPairCandidate(g,t,cfg)!=null).ToArray();
                s.Pressure=reachable.Length; s.Emergency=reachable.Any(t=>BankSurvival(t,cfg));
            }
            BankRolePolicy.Update(guns.Select(g=>g.BankRole).ToList(),cfg,frame/60.0);
            foreach(var g in guns) ResolveManagedRange(g,cfg);
        }
        private bool BankAllows(Gun g,Track t,PdcConfig cfg)
        {
            if(!ManagedRangeAllows(g,t,cfg)) return false;
            if(!cfg.BankRolesEnabled) return true;
            if(t==null) return false;
            if(BankSurvival(t,cfg)) return true;
            if(g.BankRole.Resting) return false;
            // Range is muzzle-to-target, not target-to-hull. API readback is
            // reported separately; unknown readback is not successful control.
            return g.ScopeValid && Vector3D.Distance(g.ScopeOrigin,t.Pos)<=g.DesiredRange;
        }
        private void ApplyBankDecision(Gun g,Track threat,PdcConfig cfg)
        {
            if(!cfg.BankRolesEnabled) return;
            bool survival=BankSurvival(threat,cfg);
            g.Rof=BankRolePolicy.Rate(g.BankRole,g.Rof,cfg,survival);
            if(!BankAllows(g,threat,cfg)) { g.Allowed=false; g.PkState="BANK_HOLD"; }
            // A current native target cannot borrow the range/rest permission
            // of a different scheduler target. Unknown remains labelled by the
            // existing decision telemetry; it is not hit attribution.
            var native=ObservedNativeTrack(g);
            if(native!=null && !BankAllows(g,native,cfg)) { g.Allowed=false; g.PkState="BANK_NATIVE_HOLD"; }
        }
    }
}
