using System;
using System.Collections.Generic;
using System.Linq;
using VRageMath;

namespace ZeoPDC
{
    internal sealed partial class PdcEngine
    {
        private string bankObserverStatus = "OBSERVER: ARM to capture queues";

        private void ObserveBanks()
        {
            // Observation is subordinate to defense. An adapter/math/geometry
            // error is visible in telemetry and cannot skip the firing cycle.
            try
            {
                if (controller == null || recorder == null) return;
                double time = frame / 60.0;
                var world = controller.WorldMatrix;
                var inverse = MatrixD.Invert(world);
                var motion = controller.GetShipVelocities();
                Vector3D omega = motion.AngularVelocity;
                Vector3D originVelocity = motion.LinearVelocity + Vector3D.Cross(omega, world.Translation - controller.CenterOfMass);
                Vector3D localOmega = Vector3D.TransformNormal(omega, inverse);
                recorder.ObserverRow("bank_ship.csv", time, world.Translation.X, world.Translation.Y, world.Translation.Z,
                    originVelocity.X, originVelocity.Y, originVelocity.Z, omega.X, omega.Y, omega.Z,
                    world.Right.X, world.Right.Y, world.Right.Z, world.Up.X, world.Up.Y, world.Up.Z, world.Backward.X, world.Backward.Y, world.Backward.Z);
                var hulls = new List<BoundingBoxD>();
                foreach (var cg in constructGrids)
                {
                    if (cg == null || cg.Closed) continue;
                    // Actual occupied grid extents (including mechanical subgrids),
                    // boxed in current controller axes. Ten metres is a provisional
                    // safety envelope, separate from the old 150 m danger metric.
                    var localBox = new BoundingBoxD(((Vector3D)cg.Min - new Vector3D(.5)) * cg.GridSize,
                        ((Vector3D)cg.Max + new Vector3D(.5)) * cg.GridSize);
                    Vector3D min = new Vector3D(double.MaxValue), max = new Vector3D(double.MinValue);
                    for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++)
                    {
                        Vector3D corner = new Vector3D((cornerIndex & 1) == 0 ? localBox.Min.X : localBox.Max.X,
                            (cornerIndex & 2) == 0 ? localBox.Min.Y : localBox.Max.Y, (cornerIndex & 4) == 0 ? localBox.Min.Z : localBox.Max.Z);
                        Vector3D p = Vector3D.Transform(Vector3D.Transform(corner, cg.WorldMatrix), inverse);
                        min = Vector3D.Min(min, p); max = Vector3D.Max(max, p);
                    }
                    min -= new Vector3D(10); max += new Vector3D(10);
                    hulls.Add(new BoundingBoxD(min, max));
                    recorder.ObserverRow("bank_hulls.csv", time, cg.EntityId, min.X, min.Y, min.Z, max.X, max.Y, max.Z, 10);
                }
                var inputs = new List<BankPlanner.Threat>();
                foreach (var t in tracks)
                {
                    if (t.Resolved || !t.StableId) continue;
                    double age = (frame - t.HealthFrame) / 60.0;
                    recorder.ObserverRow("projectile_health.csv", time, t.Id, t.ProjectileId, t.HealthRead, age, t.Health, t.AmmoName,
                        t.StateVelocity.X, t.StateVelocity.Y, t.StateVelocity.Z, t.Acceleration.X, t.Acceleration.Y, t.Acceleration.Z, t.HealthSource);
                    // Missing API state is never substituted with a zero-health kill.
                    inputs.Add(new BankPlanner.Threat { Track = t.Id, Id = t.ProjectileId, Health = t.Health,
                        Age = t.HealthRead == "OK" ? age : 999,
                        Position = Vector3D.Transform(t.Pos, inverse),
                        Velocity = Vector3D.TransformNormal(t.StateVelocity - originVelocity, inverse) });
                }
                var bankGuns = new List<BankPlanner.Gun>();
                foreach (var g in guns)
                {
                    var owner = tracks.FirstOrDefault(t => t.Id == g.TrackId);
                    recorder.ObserverRow("native_targets.csv", time, g.EntityId, g.Part, g.NativeRead, g.NativeProjectile,
                        owner == null ? 0UL : owner.ProjectileId,
                        g.ShootingRead == "OK" ? (g.ShootingValue ? "1" : "0") : "",
                        g.ReadyRead == "OK" ? (g.ReadyValue ? "1" : "0") : "", wc.NativeObserver.Status, wc.NativeObserver.AssemblyIdentity);
                    if (g.Block == null) continue;
                    var mount = g.Block.WorldMatrix;
                    Vector3D muzzle = g.ScopeValid ? g.ScopeOrigin : mount.Translation;
                    Vector3D pos = Vector3D.Transform(muzzle, inverse);
                    Vector3D dir = g.ScopeValid ? Vector3D.TransformNormal(g.ScopeDirection, inverse) : Vector3D.Zero;
                    Vector3D vel = Vector3D.TransformNormal(Vector3D.Cross(omega, muzzle - world.Translation), inverse);
                    string subtype = g.Block.BlockDefinition.SubtypeName;
                    string ammo = wc.GetActiveAmmo(g.Entity, g.Part) ?? "";
                    bool known = (subtype == "sdx_pdcImprovised" || subtype == "sdx_pdcImprovisedHalfSlope" || subtype == "sdx_pdcImprovisedSlope") && ammo == "40mm Lead-Steel";
                    var input = new BankPlanner.Gun { Id = g.EntityId, Part = g.Part,
                        Bank = BankPlanner.Side(Vector3D.TransformNormal(mount.Up, inverse)),
                        Profile = known ? "INSTALLED_SDX_DEFAULTS_UNCALIBRATED" : "UNKNOWN_PROFILE",
                        Position = pos, Velocity = vel, Direction = dir.LengthSquared() > 1e-9 ? Vector3D.Normalize(dir) : Vector3D.Zero,
                        Usable = known && g.Functional && g.ScopeValid && g.ReadyRead == "OK" && g.ReadyValue && g.RofReadOk,
                        Speed = known ? 3000 : 0, ShotsPerSecond = known ? 15 * Math.Max(.5, Math.Min(1, g.ActualRof)) : 0,
                        HealthPerHit = known ? 1 : 0, SlewRadiansPerSecond = known ? .0785 * 60 : 0,
                        StartupSeconds = .2, Range = g.DesiredRange };
                    bankGuns.Add(input);
                    recorder.ObserverRow("bank_guns.csv", time, input.Id, input.Part, input.Bank, input.Profile, ammo,
                        pos.X, pos.Y, pos.Z, vel.X, vel.Y, vel.Z, dir.X, dir.Y, dir.Z, input.Usable ? 1 : 0,
                        input.Speed, input.ShotsPerSecond, input.HealthPerHit, input.Range, g.Heat);
                }
                var plans = BankPlanner.Build(inputs, bankGuns, hulls, localOmega);
                foreach (var p in plans)
                    recorder.ObserverRow("bank_queue.csv", time, p.Threat.Track, p.Threat.Id, p.Threat.Path, p.Threat.Side,
                        p.Threat.Deadline, p.Threat.CpaTime, p.Threat.CpaDistance, p.Threat.Health,
                        p.Primary == null ? 0 : p.Primary.Id, p.Primary == null ? -1 : p.Primary.Part, p.Primary == null ? "NONE" : p.Primary.Bank,
                        p.Rank, p.Backup == null ? 0 : p.Backup.Id, p.Backup == null ? -1 : p.Backup.Part,
                        p.Primary == null ? double.NaN : p.Start, p.Primary == null ? double.NaN : p.FirstHit,
                        p.Primary == null ? double.NaN : p.Finish, p.Slack, p.MinimumHits, p.ScenarioShots,
                        BankPlanner.AssumedHitProbability, BankPlanner.PerTargetConfidence,
                        p.Intercept.X, p.Intercept.Y, p.Intercept.Z, p.Status, "OBSERVATION_ONLY");
                int projectileTargets = guns.Count(g => g.TargetFlag2);
                int idReads = guns.Count(g => g.NativeRead == "PROJECTILE");
                bankObserverStatus = "OBSERVER IDs " + idReads + "/" + projectileTargets + " | HP " + inputs.Count(t => t.Age <= .2) +
                    "/" + inputs.Count + " | queued " + plans.Count(p => p.Primary != null) + " | HP " + wc.NativeProjectiles.Status;
            }
            catch (Exception ex)
            {
                bankObserverStatus = "OBSERVER ERROR: " + ex.GetType().Name;
                if (frame % 60 == 0) recorder.Event("OBSERVER_ERROR", ex.GetType().Name + ": " + ex.Message);
            }
        }
    }
}
