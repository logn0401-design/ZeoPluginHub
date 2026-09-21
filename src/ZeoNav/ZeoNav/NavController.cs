using Sandbox.ModAPI;
using System;
using VRageMath;

namespace ZeoNav
{
    internal enum NavPhase
    {
        DISARMED,
        CANCEL_LATERAL,
        ALIGN_PROGRADE,
        ACCELERATE,
        COAST,
        PRE_FLIP,
        FLIP,
        BRAKE,
        TERMINAL_SETTLE,
        ARRIVED,
        ABORTED,
        MANUAL_FLIP
    }

    internal sealed class NavController
    {
        private readonly Func<ShipContext> getShip;
        private readonly Func<NavConfig> getConfig;
        private readonly Func<SpectrumAdapter> getSpectrum;
        private readonly Action<string> log;

        private NavPhase phase = NavPhase.DISARMED;
        private string destination = "";
        private string warning = "";
        private Vector3D gpsTarget;
        private Vector3D navTarget;
        private Vector3D routeStart;
        private double routeStartDistance;
        private double bufferMeters;
        private bool active;
        private bool manualFlip;
        private int onTargetTicks;
        private int arrivalTicks;
        private double lastDistance = double.MaxValue;
        private int distanceGrowingTicks;
        private double effectiveDriveRatio;
        private double eta, accelTime, coastTime, flipBurnTime;
        private double fullDriveEta = -1, timeSavedAtFull;
        private double stopDistance, flipAt, flipIn;
        private double commandSpeed;
        private double rawSpeedCap;
        private string speedCapSource = "UNKNOWN";
        private Vector3D lastGps;
        private string lastName;
        private double lastBuffer;
        private bool hasLast;
        private double manualFlipDeg;
        private int idleStatusTicks;
        private bool hasPreview;
        private string previewName = "";
        private Vector3D previewGps;
        private Vector3D flipTarget;
        private bool flipTargetSet;
        private Vector3D manualFlipTarget;
        private DateTime flipStartedUtc = DateTime.MinValue;
        private double learnedFlipSeconds;

        private SignalBudget signalBudget;
        private int signalGovernorSampleGeneration = -1;
        private long signalGovernorGridId;
        private string signalGovernorState = "IDLE";
        private double forwardOverrideMismatchSince = -1;
        private readonly AlignmentThrustGate thrustAlignment = new AlignmentThrustGate();
        private int budgetTopology = -1;
        private int quietSamples;
        private bool baselineReady;
        private bool signalTrip;
        private readonly System.Diagnostics.Stopwatch routeClock = new System.Diagnostics.Stopwatch();
        private double signalWaitStarted = -1;
        private double lastSignalLog;

        public NavController(Func<ShipContext> ship, Func<NavConfig> cfg, Func<SpectrumAdapter> spectrum, Action<string> logger)
        {
            getShip = ship; getConfig = cfg; getSpectrum = spectrum; log = logger;
        }

        public bool IsControlling { get { return active || manualFlip; } }
        public bool HasLastDestination { get { return hasLast; } }

        public void SetPreviewDestination(string name, Vector3D gps)
        {
            string nextName = string.IsNullOrWhiteSpace(name) ? "GPS" : name;
            bool changed = !hasPreview || !nextName.Equals(previewName, StringComparison.Ordinal) || Vector3D.DistanceSquared(gps, previewGps) > .01;
            previewName = nextName;
            previewGps = gps;
            hasPreview = true;
            if (changed && !active && !manualFlip)
            {
                phase = NavPhase.DISARMED;
                warning = "";
            }
        }

        public void StartRoute(string name, Vector3D gps, double buffer)
        {
            ShipContext s = getShip();
            if (s == null) { warning = "NO CONTROLLED SHIP"; return; }
            // Full construct scan here is deliberate: SDX main drives may be on
            // mechanically linked subgrids and can load after cockpit acquisition.
            s.Scan();
            s.LogDriveAudit();
            if (s.Gyros.Count == 0) { warning = GyroUnavailable(s); return; }
            if (s.Force(MoveDir.Forward) <= 1)
            {
                warning = "NO FORWARD THRUST // " + s.ForceSummary;
                return;
            }

            AbortInternal(false, "NEW ROUTE");
            NavConfig c = getConfig();
            PrepareSignalGovernor(s, c);
            routeStart = s.Position;
            gpsTarget = gps;
            destination = string.IsNullOrWhiteSpace(name) ? "GPS" : name;
            bufferMeters = Math.Max(0, Math.Min(10000, buffer));
            Vector3D path = gps - routeStart;
            double rawDist = path.Length();
            if (rawDist <= bufferMeters + 10) { warning = "GPS IS INSIDE ARRIVAL BUFFER"; return; }
            Vector3D dir = path / rawDist;
            navTarget = gps - dir * bufferMeters;
            routeStartDistance = Vector3D.Distance(routeStart, navTarget);
            lastDistance = routeStartDistance;
            distanceGrowingTicks = 0;
            s.SaveDampenersOnce();
            s.SetDampeners(false);
            s.BeginThrustControl();
            s.BeginGyroControl();
            s.ClearThrust();
            s.ResetAim();
            flipTargetSet = false;
            active = true;
            manualFlip = false;
            warning = "";
            SetPhase(NavPhase.CANCEL_LATERAL, "route start");
            lastGps = gps; lastName = destination; lastBuffer = bufferMeters; hasLast = true;
            log("START ROUTE -> " + destination +
                " raw=" + rawDist.ToString("0") + "m buffer=" + bufferMeters.ToString("0") +
                "m stop=" + routeStartDistance.ToString("0") + "m maxSig=" + TargetSigKm(c).ToString("0.0") + "km" +
                " // FORWARD PREFLIGHT work=" + s.ForwardWorkingThrusterCount +
                " main=" + s.ForwardMainDriveCount +
                " force=" + (s.Force(MoveDir.Forward) / 1000000.0).ToString("0.0") + "MN" +
                " spectrum=" + (getSpectrum() != null && getSpectrum().Ready ? "READY" : "WAIT"));
        }

        public void RestartLastRoute()
        {
            if (hasLast) StartRoute(lastName, lastGps, lastBuffer);
        }

        public void StartManualFlip()
        {
            ShipContext s = getShip();
            if (s == null) { warning = "NO CONTROLLED SHIP"; return; }
            if (active) Abort("MANUAL FLIP OVERRIDES ROUTE");

            // Refresh topology before a standalone flip. This catches mechanically linked
            // gyro sections and lets the RCS-isolation logic choose the correct gyro set.
            s.Scan();
            if (s.Gyros.Count == 0) { warning = GyroUnavailable(s); return; }

            // Manual Flip means exactly 180 degrees from the ship attitude at keypress.
            // Freeze current Backward once; do NOT keep chasing -velocity while turning.
            // This also works from rest and makes the completion angle unambiguous.
            s.ReleaseGyros();
            s.BeginGyroControl();
            Vector3D v = s.Velocity;
            double vmag = v.Length();
            manualFlipTarget = s.Controller.WorldMatrix.Backward;
            if (manualFlipTarget.LengthSquared() < 1e-8) { warning = "FLIP TARGET INVALID"; s.ReleaseGyros(); return; }
            manualFlipTarget.Normalize();
            manualFlip = true;
            active = false;
            flipStartedUtc = DateTime.UtcNow;
            warning = "";
            onTargetTicks = 0;
            manualFlipDeg = s.ForwardAngleDegrees(manualFlipTarget);
            SetPhase(NavPhase.MANUAL_FLIP, "manual exact 180 / frozen backward target");
            log("MANUAL FLIP TARGET FROZEN // initial error=" + manualFlipDeg.ToString("0.0") + "deg speed=" + vmag.ToString("0.0") + "m/s // " + s.ForceSummary);
        }

        public void Abort(string reason) { AbortInternal(true, reason); }

        private void AbortInternal(bool showState, string reason)
        {
            ShipContext s = getShip();
            if (s != null) s.ReleaseAll(false);
            bool was = active || manualFlip;
            active = false; manualFlip = false; onTargetTicks = 0; arrivalTicks = 0; flipTargetSet = false; manualFlipDeg = 0; flipStartedUtc = DateTime.MinValue;
            signalGovernorSampleGeneration = -1;
            signalGovernorState = "IDLE";
            forwardOverrideMismatchSince = -1;
            if (showState && was) { warning = reason; SetPhase(NavPhase.ABORTED, reason); }
            else if (!showState) phase = NavPhase.DISARMED;
            if (was) log("NAV RELEASE -> " + reason);
        }

        public void Update(int frame)
        {
            ShipContext s = getShip();
            if (s == null) return;
            s.BeginThrustFrame();
            try
            {
                UpdateControl(frame, s);
                s.CommitThrustFrame();
                if (active) VerifyForwardOverride(s, s.ForwardCommandRatio);
            }
            catch { s.CancelThrustFrame(); throw; }
        }

        private void UpdateControl(int frame, ShipContext s)
        {
            if (manualFlip)
            {
                if (getConfig().AbortOnManualInput && HasManualInput(s.Controller)) { Abort("MANUAL PILOT INPUT"); return; }
                idleStatusTicks = 0; UpdateManualFlip(s); return;
            }
            if (!active)
            {
                if (!string.IsNullOrEmpty(warning) && phase != NavPhase.DISARMED)
                {
                    idleStatusTicks++;
                    if (idleStatusTicks > 180) { warning = ""; phase = NavPhase.DISARMED; idleStatusTicks = 0; }
                }
                else if (!string.IsNullOrEmpty(warning))
                {
                    idleStatusTicks++; if (idleStatusTicks > 180) { warning = ""; idleStatusTicks = 0; }
                }
                return;
            }
            idleStatusTicks = 0;

            NavConfig c = getConfig();
            if (c.AbortOnManualInput && HasManualInput(s.Controller)) { Abort("MANUAL PILOT INPUT"); return; }
            if (frame % 30 == 0) s.RefreshWorkingState();
            if (s.Gyros.Count == 0)
            {
                // RCS Control Computers are deliberately NOT a fallback gyro bank.
                // Rescan once for real gyros, then fail safe rather than letting the
                // SDX RCS attitude system fight Zeo Nav's orientation controller.
                s.Scan();
                if (s.Gyros.Count == 0) { Abort("GYRO CONTROL LOST // " + GyroUnavailable(s)); return; }
            }
            if (s.Force(MoveDir.Forward) <= 1)
            {
                // One immediate topology rescan before aborting prevents a stale
                // subgrid list from killing an otherwise valid SDX route.
                s.Scan();
                if (s.Force(MoveDir.Forward) <= 1)
                {
                    Abort("FORWARD/BRAKING THRUST LOST // " + s.ForceSummary);
                    return;
                }
            }

            Vector3D pos = s.Position;
            Vector3D displacement = navTarget - pos;
            double dist = displacement.Length();
            Vector3D dir = dist > .001 ? displacement / dist : Vector3D.Zero;
            Vector3D vel = s.Velocity;
            double speed = vel.Length();
            double closing = dir.LengthSquared() > 0 ? Vector3D.Dot(vel, dir) : 0;
            Vector3D lateralVec = vel - dir * closing;
            double lateral = lateralVec.Length();

            effectiveDriveRatio = CalculateDriveRatio(c);
            if (effectiveDriveRatio <= 0)
            {
                s.ClearThrust();
                eta = -1;
                warning = signalGovernorState;
                if (signalWaitStarted < 0) signalWaitStarted = routeClock.Elapsed.TotalSeconds;
                if (routeClock.Elapsed.TotalSeconds - signalWaitStarted > 15)
                    Abort("SIG CONTROL UNAVAILABLE // " + signalGovernorState);
                return;
            }
            signalWaitStarted = -1;
            if (warning.Contains("SIG") || warning.Contains("SIGNATURE")) warning = "";
            double mass = s.Mass;
            double fullThrusterAccel = s.Force(MoveDir.Forward) / mass;
            double thrusterAccel = fullThrusterAccel * effectiveDriveRatio;
            double gravityAlongRoute = dir.LengthSquared() > 0 ? Vector3D.Dot(s.Gravity, dir) : 0;
            // Toward-target gravity helps acceleration but hurts the later retro burn.
            // Plan and execute braking under the same signature ceiling as acceleration.
            double forwardAccel = Math.Max(.01, thrusterAccel + gravityAlongRoute);
            double brakeAccel = Math.Max(.01, thrusterAccel - gravityAlongRoute);
            double emergencyBrakeAccel = Math.Max(.01, fullThrusterAccel - gravityAlongRoute);
            if (emergencyBrakeAccel < .05) { Abort("BRAKING AUTHORITY TOO LOW"); return; }
            commandSpeed = GetSpeedCap(c, effectiveDriveRatio);
            double closingPositive = Math.Max(0, closing);
            double flipAllowance = FlipAllowance(c);
            // Include telemetry/command latency and acceleration during the frozen flip.
            // A two-second lead covers Spectrum's one-second update plus transport margin.
            double reactionSeconds = 2;
            double flipEntrySpeed = closingPositive + Math.Max(0, forwardAccel) * reactionSeconds;
            double burnEntrySpeed = flipEntrySpeed + Math.Max(0, gravityAlongRoute) * flipAllowance;
            stopDistance = closingPositive * reactionSeconds + .5 * Math.Max(0, forwardAccel) * reactionSeconds * reactionSeconds +
                flipEntrySpeed * flipAllowance + .5 * Math.Max(0, gravityAlongRoute) * flipAllowance * flipAllowance +
                burnEntrySpeed * burnEntrySpeed / (2.0 * brakeAccel);
            stopDistance *= c.BrakeSafety;
            flipAt = stopDistance;
            flipIn = closingPositive > .1 ? Math.Max(0, (dist - stopDistance) / closingPositive) : double.PositiveInfinity;
            if (SpeedCapIsReliable())
            {
                CalculateEta(Math.Max(0, dist), Math.Max(0, closing), commandSpeed, forwardAccel, brakeAccel, flipAllowance);
                // v0.1.14 has one operating model: the user's MAX SIG ceiling. Do not
                // advertise a separate 100% mode/comparison.
                fullDriveEta = -1;
                timeSavedAtFull = 0;
            }
            else
            {
                eta = fullDriveEta = -1;
                timeSavedAtFull = 0;
                accelTime = coastTime = flipBurnTime = 0;
            }

            if (closing < -2 && dist > lastDistance + .001) distanceGrowingTicks++; else distanceGrowingTicks = 0;
            lastDistance = dist;
            if (distanceGrowingTicks > 30 && phase != NavPhase.TERMINAL_SETTLE) { warning = "OVERSHOOT RECOVERY"; SetPhase(NavPhase.TERMINAL_SETTLE, "distance growing"); }

            switch (phase)
            {
                case NavPhase.CANCEL_LATERAL:
                    s.ClearThrust();
                    if (lateral <= 1.0 || speed <= 1.0) { SetPhase(NavPhase.ALIGN_PROGRADE, "lateral clean"); break; }
                    s.ApplyWorldAcceleration(-lateralVec / 1.5, Math.Min(1, effectiveDriveRatio));
                    break;

                case NavPhase.ALIGN_PROGRADE:
                    s.ClearThrust();
                    if (s.Orient(dir, 0.25)) onTargetTicks++; else onTargetTicks = 0;
                    if (onTargetTicks >= 6) { onTargetTicks = 0; SetPhase(NavPhase.ACCELERATE, "prograde aligned"); }
                    break;

                case NavPhase.ACCELERATE:
                    if (stopDistance >= dist) { s.ClearThrust(); SetPhase(NavPhase.PRE_FLIP, "brake boundary reached"); break; }
                    // Attitude acquisition is gyro-only. Do not start/continue the main
                    // drive burn while the nose is outside the prograde gate.
                    s.Orient(dir, 0.25);
                    if (!thrustAlignment.Allow(s.ForwardAngleDegrees(dir)))
                    {
                        s.ClearThrust();
                        break;
                    }
                    s.ClearThrust();
                    s.ApplySideDamping(vel, dir, 2.0, Math.Min(.8, effectiveDriveRatio));
                    // Course corrections get their share first; forward thrust uses the
                    // remaining shared budget. Taper the final tick at the actual speed cap.
                    double speedRatio = Math.Max(0, (commandSpeed - speed) * 60 / Math.Max(.01, fullThrusterAccel));
                    s.SetMove(MoveDir.Forward, Math.Min(effectiveDriveRatio, speedRatio));
                    if (speed >= commandSpeed * .999) { s.ClearThrust(); SetPhase(NavPhase.COAST, "cruise cap reached"); }
                    break;

                case NavPhase.COAST:
                    s.ClearThrust();
                    s.Orient(dir, 0.25);
                    if (stopDistance >= dist) SetPhase(NavPhase.PRE_FLIP, "flip boundary reached");
                    else if (speed < commandSpeed * .995 && dist > stopDistance * 1.05) SetPhase(NavPhase.ACCELERATE, "speed below cruise band");
                    break;

                case NavPhase.PRE_FLIP:
                    s.ClearThrust();
                    // Freeze the retrograde vector at flip entry. v0.1.5 recalculated
                    // -velocity every tick, so gravity/lateral motion moved the target while
                    // the ship was turning and a commanded "180" could visibly stop elsewhere.
                    // Freeze an exact geometric 180-degree target from the attitude at
                    // flip entry. BRAKE will transition to live velocity-retrograde after
                    // this exact flip completes.
                    flipTarget = s.Controller.WorldMatrix.Backward;
                    if (flipTarget.LengthSquared() < 1e-8) flipTarget = speed > 1 ? -vel / speed : -dir;
                    flipTarget.Normalize();
                    flipTargetSet = true;
                    flipStartedUtc = DateTime.UtcNow;
                    log("AUTO FLIP TARGET FROZEN // angle=" + s.ForwardAngleDegrees(flipTarget).ToString("0.0") + "deg allowance=" + flipAllowance.ToString("0.0") + "s");
                    SetPhase(NavPhase.FLIP, "auto flip / frozen retro target");
                    break;

                case NavPhase.FLIP:
                    s.ClearThrust();
                    if (!flipTargetSet)
                    {
                        flipTarget = s.Controller.WorldMatrix.Backward;
                        if (flipTarget.LengthSquared() < 1e-8) flipTarget = speed > 1 ? -vel / speed : -dir;
                        flipTarget.Normalize();
                        flipTargetSet = true;
                    }
                    manualFlipDeg = s.ForwardAngleDegrees(flipTarget);
                    if (s.Orient(flipTarget, .08)) onTargetTicks++; else onTargetTicks = 0;
                    if (onTargetTicks >= 6)
                    {
                        onTargetTicks = 0;
                        manualFlipDeg = 0;
                        ObserveFlipTime("AUTO");
                        SetPhase(NavPhase.BRAKE, "exact 180 complete / transition to retrograde braking");
                    }
                    break;

                case NavPhase.BRAKE:
                    Vector3D retroDir = speed > .5 ? -vel / speed : -dir;
                    // Braking attitude acquisition is also gyro-only. No forward-drive
                    // retro burn is allowed until the real gyro bank has the nose inside
                    // the retrograde gate.
                    s.Orient(retroDir, 2.0);
                    if (s.ForwardAngleDegrees(retroDir) > 2.0)
                    {
                        s.ClearThrust();
                        break;
                    }
                    if (dist <= Math.Max(250, c.ArrivalRadiusMeters * 20) || speed <= 20 || closing < -2)
                    {
                        s.ClearThrust(); SetPhase(NavPhase.TERMINAL_SETTLE, "terminal envelope"); break;
                    }
                    // With the ship retrograde, forward thrust opposes travel.
                    // Compute the thrust ratio needed to make the stop, including gravity.
                    // A disturbance or lower MAX SIG can make the planned stop unattainable.
                    double desiredDecel = closingPositive > 0 && dist > 1 ? closingPositive * closingPositive / (2 * Math.Max(1, dist * .82)) : 0;
                    double requiredThrusterAccel = Math.Max(0, desiredDecel + gravityAlongRoute);
                    double needed = requiredThrusterAccel * mass / Math.Max(1, s.Force(MoveDir.Forward));
                    needed = Math.Max(0.0, Math.Min(1.0, needed * 1.15));
                    double brakeCommand = Math.Min(needed, effectiveDriveRatio);
                    if (needed > effectiveDriveRatio + .03)
                    {
                        warning = "STOP AT RISK / MAX SIG HELD";
                    }
                    else if (warning == "STOP AT RISK / MAX SIG HELD") warning = "";
                    s.ClearThrust();
                    s.SetMove(MoveDir.Forward, brakeCommand);
                    break;

                case NavPhase.TERMINAL_SETTLE:
                    UpdateTerminal(s, c, displacement, dist, vel, speed);
                    break;
            }
        }

        private void UpdateTerminal(ShipContext s, NavConfig c, Vector3D displacement, double dist, Vector3D vel, double speed)
        {
            if (dist <= c.ArrivalRadiusMeters && speed <= c.ArrivalSpeedMps)
            {
                arrivalTicks++;
                s.ClearThrust();
                if (arrivalTicks >= 15)
                {
                    // Full-stop completion must not re-apply a pre-route manual drive
                    // override. Zero Zeo Nav's owned thrust and release gyros/dampeners.
                    s.ReleaseAll(false); active = false; SetPhase(NavPhase.ARRIVED, "full stop"); warning = "ROUTE COMPLETE";
                    log("ROUTE COMPLETE -> " + destination + " buffer=" + bufferMeters.ToString("0") + "m");
                }
                return;
            }
            arrivalTicks = 0;
            Vector3D dir = dist > .001 ? displacement / dist : Vector3D.Zero;
            double desiredSpeed = Math.Min(18.0, Math.Max(0, dist * .22));
            if (dist < 30) desiredSpeed = Math.Min(desiredSpeed, 3.0);
            // Terminal correction also honors gyro-only attitude acquisition. If the
            // ship is still moving appreciably, get the real gyro bank close to live
            // retrograde before allowing translation/braking thrust again.
            if (speed > 1)
            {
                Vector3D terminalRetro = -vel / speed;
                s.Orient(terminalRetro, 8.0);
                if (s.ForwardAngleDegrees(terminalRetro) > 10.0)
                {
                    s.ClearThrust();
                    return;
                }
            }
            Vector3D desiredVel = dir * desiredSpeed;
            Vector3D accel = (desiredVel - vel) / 1.2;
            // Respect the SIG governor during terminal corrections. The old 10%
            // minimum could flare a large ship far above a low KM budget.
            double maxRatio = Math.Max(0.0, effectiveDriveRatio);
            double maxAccel = Math.Max(.2, s.Accel(MoveDir.Forward, maxRatio));
            if (accel.Length() > maxAccel) { accel.Normalize(); accel *= maxAccel; }
            s.ApplyWorldAcceleration(accel, maxRatio);
        }

        private void UpdateManualFlip(ShipContext s)
        {
            if (manualFlipTarget.LengthSquared() < 1e-8)
            {
                manualFlipTarget = s.Controller.WorldMatrix.Backward;
                if (manualFlipTarget.LengthSquared() < 1e-8) { warning = "FLIP TARGET INVALID"; s.ReleaseGyros(); manualFlip = false; return; }
                manualFlipTarget.Normalize();
            }
            manualFlipDeg = s.ForwardAngleDegrees(manualFlipTarget);
            if (s.Orient(manualFlipTarget, .08)) onTargetTicks++; else onTargetTicks = 0;
            if (onTargetTicks >= 6)
            {
                double measured = ObserveFlipTime("MANUAL");
                s.ReleaseGyros();
                manualFlip = false;
                manualFlipDeg = 0;
                warning = measured > 0 ? "FLIP COMPLETE / " + measured.ToString("0.0") + "S" : "FLIP COMPLETE / 180 DEG";
                SetPhase(NavPhase.DISARMED, "manual exact 180 complete / frozen target reached");
            }
        }

        private static string GyroUnavailable(ShipContext s)
        {
            if (s == null) return "NO STANDARD GYROS";
            if (s.RcsGyroCount > 0)
                return "NO STANDARD GYROS // RCS CONTROL COMPUTERS EXCLUDED // " + s.ForceSummary;
            return "NO FUNCTIONAL STANDARD GYROS // " + s.ForceSummary;
        }

        private void PrepareSignalGovernor(ShipContext s, NavConfig c)
        {
            signalGovernorGridId = s == null || s.Grid == null ? 0 : s.Grid.EntityId;
            signalGovernorSampleGeneration = getSpectrum() == null ? -1 : getSpectrum().SampleGeneration;
            signalBudget = null; budgetTopology = -1; quietSamples = 0; baselineReady = false; signalTrip = false;
            signalWaitStarted = -1; lastSignalLog = -10;
            routeClock.Restart();
            signalGovernorState = "MEASURING OWN IDLE SIGNATURE";
            log("SIG GOV ARM // max=" + TargetSigKm(c).ToString("0.0") + " KM // model + fresh own summary / no acquisition burn");
        }

        private static double TargetSigKm(NavConfig c)
        {
            double value = c == null ? 125 : c.MaxDriveSigKm;
            return SignalBudget.Finite(value) ? Math.Max(5, Math.Min(750, value)) : 5;
        }

        private double EstimateSignalRatio(NavConfig c, ShipContext s)
        {
            if (signalBudget == null || !signalBudget.Ready || s == null || s.Grid.EntityId != signalGovernorGridId) return 0;
            signalBudget.TargetKm = TargetSigKm(c);
            return signalBudget.Limit(0, 1, new double[6]);
        }

        private bool VerifyForwardOverride(ShipContext s, double commandedRatio)
        {
            if (s == null || commandedRatio <= .005 || s.ForwardReadbackRatio >= Math.Max(.002, commandedRatio * .35))
            {
                forwardOverrideMismatchSince = -1;
                return true;
            }
            if (s.ForwardWorkingThrusterCount <= 0)
            {
                Abort("NO WORKING FORWARD THRUSTERS // " + s.ForceSummary);
                return false;
            }
            double now = routeClock.Elapsed.TotalSeconds;
            if (forwardOverrideMismatchSince < 0)
            {
                forwardOverrideMismatchSince = now;
                log("FORWARD ACK WAIT // cmd=" + (commandedRatio * 100).ToString("0.0") +
                    "% rb=" + (s.ForwardReadbackRatio * 100).ToString("0.0") + "% // server grace 3s");
            }
            if (now - forwardOverrideMismatchSince >= 3)
            {
                s.LogDriveAudit();
                Abort("FORWARD OVERRIDE REJECTED // " + s.ForceSummary);
                return false;
            }
            return true;
        }

        private double CalculateDriveRatio(NavConfig c)
        {
            ShipContext s = getShip();
            SpectrumAdapter sp = getSpectrum();
            if (s == null || c == null) return 0;
            s.RefreshSignatureTopology();
            if (!active) return EstimateSignalRatio(c, s);
            if (s.Grid.EntityId != signalGovernorGridId) PrepareSignalGovernor(s, c);
            if (signalBudget != null) signalBudget.Ready = false;
            if (sp == null || !c.SpectrumFeedback || !sp.DriveKmReady)
            {
                signalGovernorState = "WAIT FRESH OWN SIG / THRUST ZERO";
                return 0;
            }
            bool newSample = sp.SampleGeneration != signalGovernorSampleGeneration;
            if (signalBudget == null || budgetTopology != s.TopologyRevision)
            {
                try
                {
                    signalBudget = sp.BuildBudget(s);
                    s.SignatureBudget = signalBudget;
                    budgetTopology = s.TopologyRevision;
                    quietSamples = 0; baselineReady = false;
                    signalGovernorSampleGeneration = sp.SampleGeneration;
                    newSample = false;
                    log("SIG MODEL // loaded " + signalBudget.Buckets.Count + " emission buckets // grid=" + s.Grid.EntityId);
                }
                catch (Exception ex)
                {
                    signalGovernorState = "UNSUPPORTED SIG MODEL / THRUST ZERO";
                    if (routeClock.Elapsed.TotalSeconds - lastSignalLog > 5) { log(signalGovernorState + " // " + ex.Message); lastSignalLog = routeClock.Elapsed.TotalSeconds; }
                    return 0;
                }
            }
            signalBudget.TargetKm = TargetSigKm(c);
            if (newSample)
            {
                signalGovernorSampleGeneration = sp.SampleGeneration;
                if (s.ThrustersQuiet)
                {
                    quietSamples++;
                    // Three distinct idle packets flush server/transport lag before
                    // treating a reading as an idle baseline. Never learn from commanded ratio.
                    if (quietSamples >= 3)
                    {
                        signalBudget.SphericalBaseSquared = sp.SphericalWeakKm * sp.SphericalWeakKm;
                        signalBudget.DirectionalBaseSquared = sp.DirectionalWeakKm * sp.DirectionalWeakKm;
                        baselineReady = true;
                    }
                }
                else quietSamples = 0;
                if (sp.DriveKm > signalBudget.TargetKm)
                {
                    // Immediate zero on a measured excursion. Re-arm only after fresh
                    // below-limit data; retain the more conservative coefficient scale.
                    if (!signalTrip) signalBudget.FeedbackScale = Math.Min(1000, signalBudget.FeedbackScale * Math.Max(1.1, Math.Pow(sp.DriveKm / signalBudget.TargetKm, 2) * 1.1));
                    signalTrip = true;
                }
                else if (sp.DriveKm < signalBudget.TargetKm * SignalBudget.RangeMargin) signalTrip = false;
            }
            if (!baselineReady)
            {
                signalGovernorState = "MEASURING OWN IDLE SIGNATURE / THRUST ZERO";
                return 0;
            }
            double idle = Math.Sqrt(Math.Max(signalBudget.SphericalBaseSquared, signalBudget.DirectionalBaseSquared));
            if (idle >= signalBudget.TargetKm * SignalBudget.RangeMargin)
            {
                signalGovernorState = "IDLE SIG TOO HIGH / RAISE MAX OR REDUCE EMITTERS";
                return 0;
            }
            if (signalTrip)
            {
                signalGovernorState = "OVER MAX SIG / THRUST ZERO";
                return 0;
            }
            signalBudget.Ready = true;
            double ratio = signalBudget.Limit(0, 1, new double[6]);
            signalGovernorState = ratio >= .999 ? "FULL THRUST AVAILABLE" : "SIGNATURE BUDGET";
            if (newSample && routeClock.Elapsed.TotalSeconds - lastSignalLog >= 2)
            {
                log("SIG GOV // max=" + signalBudget.TargetKm.ToString("0.0") + "km actual=" + sp.DriveKm.ToString("0.00") +
                    "km available=" + (ratio * 100).ToString("0.00") + "% cmd=" + (s.ForwardCommandRatio * 100).ToString("0.00") +
                    "% speed=" + s.Velocity.Length().ToString("0.0") + "m/s phase=" + phase + " align=" + s.LastAlignmentErrorDeg.ToString("0.000") + "deg rate=" + s.LastAngularRateDeg.ToString("0.000") + "deg/s cap=" + commandSpeed.ToString("0") + " [" + speedCapSource + "] // " + signalGovernorState + " // gen=" + sp.SampleGeneration);
                lastSignalLog = routeClock.Elapsed.TotalSeconds;
            }
            return ratio;
        }

        private double GetSpeedCap(NavConfig c, double ratio)
        {
            ShipContext s = getShip();
            double observed = s == null ? 0 : s.Velocity.Length();
            double controlCeiling = SpeedCapResolver.Resolve(s == null ? null : s.Grid, c.SpeedCapOverride, observed, out speedCapSource);

            // SPEED/SIGNAL limits thrust/emission, not the physical speed ceiling.
            // v0.1.5 incorrectly multiplied the ship cap by DriveSlider, which made a
            // 45% signal setting look like a 45 m/s ship and inflated ETAs into days.
            //
            // AUTO uses ShipCore when available, otherwise the reported server ceiling.
            // Neither source may be raised to chase an overspeed ship.
            rawSpeedCap = SpeedCapSourceIsReliable(speedCapSource) ? controlCeiling : 0;
            return Math.Max(1, controlCeiling);
        }

        private static bool SpeedCapSourceIsReliable(string source)
        {
            string src = (source ?? "").ToUpperInvariant();
            return (src.StartsWith("SHIPCORE") && src.IndexOf("WAIT", StringComparison.Ordinal) < 0) ||
                   src.StartsWith("OVERRIDE") || src.StartsWith("SERVER LIMIT");
        }

        private bool SpeedCapIsReliable()
        {
            return SpeedCapSourceIsReliable(speedCapSource);
        }

        private double FlipAllowance(NavConfig c)
        {
            double configured = c == null ? 20.0 : Math.Max(1.0, c.FlipTimeSeconds);
            if (learnedFlipSeconds <= 0) return configured;
            // Keep 25% margin over the slowest measured 180 this session. The value is
            // runtime-only; ADVANCED still lets the pilot set a persistent allowance.
            return Math.Max(configured, learnedFlipSeconds * 1.25);
        }

        private double ObserveFlipTime(string source)
        {
            if (flipStartedUtc == DateTime.MinValue) return 0;
            double seconds = (DateTime.UtcNow - flipStartedUtc).TotalSeconds;
            flipStartedUtc = DateTime.MinValue;
            if (seconds < .25 || seconds > 120 || double.IsNaN(seconds) || double.IsInfinity(seconds)) return 0;
            if (seconds > learnedFlipSeconds) learnedFlipSeconds = seconds;
            log((source ?? "FLIP") + " 180 MEASURED // " + seconds.ToString("0.00") + "s // learned allowance=" + (learnedFlipSeconds * 1.25).ToString("0.00") + "s");
            return seconds;
        }

        private void CalculateEta(double distance, double startSpeed, double cap, double accel, double brake, double flipTime)
        {
            if (distance <= 0 || accel <= .001 || brake <= .001 || cap <= 1) { eta = -1; accelTime = coastTime = flipBurnTime = 0; return; }
            startSpeed = Math.Max(0, Math.Min(cap, startSpeed));
            double tA = Math.Max(0, (cap - startSpeed) / accel);
            double dA = Math.Max(0, (cap * cap - startSpeed * startSpeed) / (2 * accel));
            double dB = cap * flipTime + cap * cap / (2 * brake);
            if (dA + dB < distance)
            {
                double dC = distance - dA - dB;
                accelTime = tA; coastTime = dC / Math.Max(1, cap); flipBurnTime = flipTime + cap / brake; eta = accelTime + coastTime + flipBurnTime; return;
            }
            // Solve A*v^2 + B*v + C = 0 for a triangular flip/burn profile.
            double A = 1.0 / (2 * accel) + 1.0 / (2 * brake);
            double B = flipTime;
            double C = -(distance + startSpeed * startSpeed / (2 * accel));
            double disc = Math.Max(0, B * B - 4 * A * C);
            double peak = Math.Min(cap, Math.Max(startSpeed, (-B + Math.Sqrt(disc)) / (2 * A)));
            accelTime = Math.Max(0, (peak - startSpeed) / accel);
            coastTime = 0;
            flipBurnTime = flipTime + peak / brake;
            eta = accelTime + flipBurnTime;
        }

        private static double EstimateEta(double distance, double startSpeed, double cap, double accel, double brake, double flipTime)
        {
            if (distance <= 0 || accel <= .001 || brake <= .001 || cap <= 1) return -1;
            startSpeed = Math.Max(0, Math.Min(cap, startSpeed));
            double tA = Math.Max(0, (cap - startSpeed) / accel);
            double dA = Math.Max(0, (cap * cap - startSpeed * startSpeed) / (2 * accel));
            double dB = cap * flipTime + cap * cap / (2 * brake);
            if (dA + dB < distance)
            {
                double dC = distance - dA - dB;
                return tA + dC / Math.Max(1, cap) + flipTime + cap / brake;
            }
            double A = 1.0 / (2 * accel) + 1.0 / (2 * brake);
            double B = flipTime;
            double C = -(distance + startSpeed * startSpeed / (2 * accel));
            double disc = Math.Max(0, B * B - 4 * A * C);
            double peak = Math.Min(cap, Math.Max(startSpeed, (-B + Math.Sqrt(disc)) / (2 * A)));
            return Math.Max(0, (peak - startSpeed) / accel) + flipTime + peak / brake;
        }

        private void UpdatePreview(ShipContext s, NavConfig c)
        {
            if (s == null || c == null || !hasPreview || active || manualFlip || phase == NavPhase.ARRIVED) return;
            Vector3D start = s.Position;
            Vector3D raw = previewGps - start;
            double rawDist = raw.Length();
            double buffer = Math.Max(0, Math.Min(10000, c.BufferKm * 1000.0));
            destination = previewName;
            bufferMeters = buffer;
            if (rawDist <= buffer + 10)
            {
                routeStartDistance = 0; stopDistance = flipAt = 0; flipIn = -1; eta = fullDriveEta = -1; timeSavedAtFull = 0; accelTime = coastTime = flipBurnTime = 0;
                commandSpeed = 0; warning = "GPS IS INSIDE ARRIVAL BUFFER";
                return;
            }
            if (warning == "GPS IS INSIDE ARRIVAL BUFFER") warning = "";
            Vector3D dir = raw / rawDist;
            Vector3D previewTarget = previewGps - dir * buffer;
            Vector3D d = previewTarget - start;
            double dist = d.Length();
            routeStartDistance = dist;
            Vector3D routeDir = dist > .001 ? d / dist : Vector3D.Zero;
            Vector3D vel = s.Velocity;
            double closing = routeDir.LengthSquared() > 0 ? Vector3D.Dot(vel, routeDir) : 0;
            effectiveDriveRatio = EstimateSignalRatio(c, s);
            double mass = Math.Max(1, s.Mass);
            if (s.Force(MoveDir.Forward) <= 1)
            {
                // Keep the route preview visible, but do not manufacture a useful ETA
                // when the controlled construct has no discovered forward authority.
                commandSpeed = GetSpeedCap(c, effectiveDriveRatio);
                eta = fullDriveEta = -1; timeSavedAtFull = 0; accelTime = coastTime = flipBurnTime = 0;
                stopDistance = flipAt = 0; flipIn = -1;
                warning = "NO FORWARD THRUST // " + s.ForceSummary;
                return;
            }
            if (warning != null && warning.StartsWith("NO FORWARD THRUST //", StringComparison.Ordinal)) warning = "";
            double thrusterAccel = s.Force(MoveDir.Forward) * effectiveDriveRatio / mass;
            double gravityAlong = routeDir.LengthSquared() > 0 ? Vector3D.Dot(s.Gravity, routeDir) : 0;
            double accel = Math.Max(.01, thrusterAccel + gravityAlong);
            double brake = Math.Max(.01, thrusterAccel - gravityAlong);
            commandSpeed = GetSpeedCap(c, effectiveDriveRatio);
            double flipAllowance = FlipAllowance(c);
            if (SpeedCapIsReliable() && effectiveDriveRatio > 0)
            {
                CalculateEta(dist, Math.Max(0, closing), commandSpeed, accel, brake, flipAllowance);
                double fullThrusterAccel = s.Force(MoveDir.Forward) / mass;
                double fullAccel = Math.Max(.01, fullThrusterAccel + gravityAlong);
                double fullBrake = Math.Max(.01, fullThrusterAccel - gravityAlong);
                fullDriveEta = EstimateEta(dist, Math.Max(0, closing), commandSpeed, fullAccel, fullBrake, flipAllowance);
                timeSavedAtFull = eta >= 0 && fullDriveEta >= 0 ? Math.Max(0, eta - fullDriveEta) : 0;
                double plannedPeak = coastTime > .001 ? commandSpeed : Math.Max(Math.Max(0, closing), brake * Math.Max(0, flipBurnTime - flipAllowance));
                stopDistance = (plannedPeak * flipAllowance + plannedPeak * plannedPeak / (2.0 * brake)) * c.BrakeSafety;
                flipAt = Math.Min(dist, stopDistance);
                flipIn = Math.Max(0, accelTime + coastTime);
            }
            else
            {
                eta = fullDriveEta = -1;
                timeSavedAtFull = 0;
                accelTime = coastTime = flipBurnTime = 0;
                // Current-speed stop data remains meaningful even while max-speed data waits.
                double now = Math.Max(0, closing);
                stopDistance = (now * flipAllowance + now * now / (2.0 * brake)) * c.BrakeSafety;
                flipAt = Math.Min(dist, stopDistance);
                flipIn = now > .1 ? Math.Max(0, (dist - stopDistance) / now) : -1;
            }
        }

        private static bool HasManualInput(Sandbox.ModAPI.IMyShipController c)
        {
            try
            {
                return c.MoveIndicator.LengthSquared() > .01f || c.RotationIndicator.LengthSquared() > .01f || Math.Abs(c.RollIndicator) > .05f;
            }
            catch { return false; }
        }

        private void SetPhase(NavPhase p, string reason)
        {
            if (phase == p) return;
            NavPhase old = phase;
            phase = p;
            onTargetTicks = 0;
            thrustAlignment.Reset();
            ShipContext s = getShip();
            if (s != null) s.ResetAim();
            if (p != NavPhase.FLIP) flipTargetSet = false;
            if (p == NavPhase.ARRIVED || p == NavPhase.ABORTED || p == NavPhase.DISARMED) idleStatusTicks = 0;
            log("STATE " + old + " -> " + p + " // " + reason);
        }

        public NavSnapshot BuildSnapshot()
        {
            ShipContext s = getShip(); NavConfig c = getConfig(); SpectrumAdapter sp = getSpectrum();
            var snap = new NavSnapshot();
            if (s != null && c != null) UpdatePreview(s, c);
            snap.Ship = s == null ? "NO CONTROLLED SHIP" : s.Name;
            snap.State = active ? "ACTIVE" : manualFlip ? "MANUAL" : phase == NavPhase.ARRIVED ? "ARRIVED" : phase == NavPhase.ABORTED ? "ABORTED" : "DISARMED";
            snap.Phase = phase.ToString().Replace('_', ' ');
            snap.Destination = destination;
            snap.WarningText = warning;
            snap.BufferMeters = bufferMeters;
            snap.RouteStartDistanceMeters = routeStartDistance;
            if (s != null)
            {
                Vector3D v = s.Velocity; snap.SpeedMps = v.Length();
                if (active)
                {
                    Vector3D d = navTarget - s.Position; snap.DistanceMeters = d.Length();
                    Vector3D dir = d.LengthSquared() > 1e-6 ? Vector3D.Normalize(d) : Vector3D.Zero;
                    snap.ClosingMps = Vector3D.Dot(v, dir);
                    snap.LateralMps = (v - dir * snap.ClosingMps).Length();
                    snap.Progress01 = routeStartDistance > 1 ? Math.Max(0, Math.Min(1, 1 - snap.DistanceMeters / routeStartDistance)) : 0;
                }
                else if (hasPreview)
                {
                    Vector3D raw = previewGps - s.Position; double rd = raw.Length();
                    double b = Math.Max(0, Math.Min(10000, c.BufferKm * 1000.0));
                    Vector3D pd = rd > b + 10 ? (previewGps - raw / rd * b) - s.Position : Vector3D.Zero;
                    snap.DistanceMeters = pd.Length();
                    Vector3D dir = pd.LengthSquared() > 1e-6 ? Vector3D.Normalize(pd) : Vector3D.Zero;
                    snap.ClosingMps = Vector3D.Dot(v, dir);
                    snap.LateralMps = (v - dir * snap.ClosingMps).Length();
                    snap.Progress01 = 0;
                }
            }
            snap.CommandSpeedMps = SpeedCapIsReliable() ? commandSpeed : 0;
            snap.SpeedCapMps = rawSpeedCap;
            snap.SpeedCapSource = speedCapSource;
            snap.DriveRatio = effectiveDriveRatio;
            snap.SpectrumDrive = sp == null ? 0 : sp.Drive;
            snap.SpectrumStrength = sp == null ? 0 : sp.Strength;
            snap.SpectrumDriveKm = sp == null ? 0 : sp.DriveKm;
            snap.SphericalStrongKm = sp == null ? 0 : sp.SphericalStrongKm;
            snap.SphericalWeakKm = sp == null ? 0 : sp.SphericalWeakKm;
            snap.DirectionalStrongKm = sp == null ? 0 : sp.DirectionalStrongKm;
            snap.DirectionalWeakKm = sp == null ? 0 : sp.DirectionalWeakKm;
            snap.SpectrumKmReady = sp != null && sp.DriveKmReady;
            snap.SpectrumKmSource = sp == null ? "WAIT SPECTRUM" : sp.DriveKmSource;
            snap.MaxDriveSigKm = TargetSigKm(c);
            snap.SignalGovernorState = signalGovernorState;
            snap.SpectrumReady = sp != null && sp.Ready;
            snap.SpectrumSelfEmitterId = sp == null ? 0 : sp.EmitterId;
            snap.SpectrumSelfMatch = sp == null ? "NONE" : sp.SelfMatch;
            snap.SpectrumSelfAgeFrames = sp == null ? 0 : sp.SelfSampleAgeFrames;
            snap.SpectrumTarget = 0;
            snap.EtaSeconds = eta; snap.EtaAtFullSeconds = fullDriveEta; snap.TimeSavedAtFullSeconds = timeSavedAtFull; snap.AccelSeconds = accelTime; snap.CoastSeconds = coastTime; snap.FlipBurnSeconds = flipBurnTime;
            snap.FlipInSeconds = double.IsInfinity(flipIn) ? -1 : flipIn; snap.FlipAtMeters = flipAt; snap.StopDistanceMeters = stopDistance;
            snap.ManualFlipActive = manualFlip; snap.ManualFlipDegreesLeft = manualFlipDeg;
            if (s != null)
            {
                snap.ConstructGridCount = s.ConstructGridCount;
                snap.ThrusterCount = s.ThrusterCount;
                snap.MainDriveCount = s.MainDriveCount;
                snap.ForwardWorkingMainDriveCount = s.ForwardWorkingMainDriveCount;
                snap.ForwardMainThrustMN = s.ForwardMainEffectiveForce / 1000000;
                snap.ForwardMainRatedThrustMN = s.ForwardMainRatedForce / 1000000;
                snap.ForwardWorkingThrusterCount = s.ForwardWorkingThrusterCount;
                snap.ForwardMainDriveCount = s.ForwardMainDriveCount;
                snap.ForwardCommandRatio = s.ForwardCommandRatio;
                snap.ForwardReadbackRatio = s.ForwardReadbackRatio;
                snap.GyroCount = s.Gyros.Count;
                snap.RcsGyroCount = s.RcsGyroCount;
                snap.ForwardThrustMN = s.Force(MoveDir.Forward) / 1000000.0;
                snap.BackwardThrustMN = s.Force(MoveDir.Backward) / 1000000.0;
                snap.DriveScanSummary = s.ForceSummary;
                snap.AlignmentErrorDeg = s.LastAlignmentErrorDeg;
                snap.AngularSpeedDeg = s.LastAngularRateDeg;
            }
            snap.HudVisible = active || manualFlip || phase == NavPhase.ARRIVED || !string.IsNullOrEmpty(warning);
            return snap;
        }
    }
}


