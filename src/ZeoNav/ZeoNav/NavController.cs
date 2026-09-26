using Sandbox.ModAPI;
using System;
using System.Linq;
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
        MANUAL_FLIP,
        INITIAL_BRAKE
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
        private bool approachActive, departureCleared, dampenerAssist;
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

        private SignalBudget signalBudget,rcsTurnBudget;
        private int rcsTurnTopology=-1;
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
        private readonly PilotInputGate pilotInput=new PilotInputGate();
        private readonly MotionRevalidation motionRevalidation=new MotionRevalidation();
        private bool momentumEntry;

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
            if(!s.Motion.Ready){warning="WAIT WORLD VELOCITY / RETRY SHORTLY";return;}
            if(s.HasDockConnection()){warning="UNDOCK BEFORE STARTING A ROUTE";return;}
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
            approachActive=false;departureCleared=false;
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
            Vector3D startVelocity=s.Velocity;
            double along=Vector3D.Dot(startVelocity,dir);
            momentumEntry=MomentumCapture.InWindow(startVelocity,dir);
            bool arrest=!momentumEntry&&startVelocity.Length()>5&&(along<0||(startVelocity-dir*along).Length()>5);
            SetPhase(momentumEntry?NavPhase.ALIGN_PROGRADE:arrest?NavPhase.INITIAL_BRAKE:NavPhase.CANCEL_LATERAL,
                momentumEntry?"moving route entry / preserve momentum pending correction budget":arrest?"arrest initial motion outside forward window":"route start");
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
            pilotInput.Reset();
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
            pilotInput.Reset();motionRevalidation.Reset();momentumEntry=false;rcsTurnBudget=null;rcsTurnTopology=-1;
            ShipContext s = getShip();
            if (s != null) s.ReleaseAll(false);
            bool was = active || manualFlip;
            dampenerAssist=false;
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
            s.AllowRcsTurnAssist=false;
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
                if (getConfig().AbortOnManualInput && HasManualInput(s.Controller,frame)) { Abort("MANUAL PILOT INPUT"); return; }
                ConfigureManualFlipRcs(s, getConfig());
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
            if (c.AbortOnManualInput && HasManualInput(s.Controller,frame)) { Abort("MANUAL PILOT INPUT"); return; }
            if(!c.AbortOnManualInput)pilotInput.Reset();
            bool wasWaiting=motionRevalidation.Waiting;
            var motionDecision=motionRevalidation.Observe(s.Motion,routeClock.Elapsed.TotalSeconds);
            if(motionDecision==MotionDecision.Abort){log("MOTION ABORT // "+s.Motion.LastFault);Abort(motionRevalidation.Reason);return;}
            if(motionDecision==MotionDecision.Hold)
            {
                AssistDampeners(s,false);s.SetDampeners(false);s.ClearThrust();s.ApplyDockRotation(Vector3D.Zero);
                warning="VERIFYING SERVER MOTION / THRUST OFF";eta=-1;
                if(!wasWaiting)log("MOTION HOLD // "+s.Motion.LastFault);
                return;
            }
            if(motionDecision==MotionDecision.Recovered)
            {
                warning="";distanceGrowingTicks=0;lastDistance=Vector3D.Distance(navTarget,s.Position);onTargetTicks=0;s.ResetAim();thrustAlignment.Reset();
                if(phase==NavPhase.ACCELERATE||phase==NavPhase.COAST||phase==NavPhase.ALIGN_PROGRADE||phase==NavPhase.CANCEL_LATERAL)
                {
                    var remaining=navTarget-s.Position;var routeDirection=remaining.LengthSquared()>1e-8?Vector3D.Normalize(remaining):Vector3D.Zero;
                    momentumEntry=MomentumCapture.InWindow(s.Velocity,routeDirection);
                    SetPhase(momentumEntry?NavPhase.ALIGN_PROGRADE:NavPhase.INITIAL_BRAKE,"world motion reverified / replan from current velocity");
                }
                log("MOTION RECOVERED // speed="+s.Velocity.Length().ToString("0.0")+"m/s source="+s.Motion.Source);
            }
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
            s.RcsOnly=false; // Each frame chooses the required bank before commands commit.
            double stopAssistSpeed=5;
            double closing = dir.LengthSquared() > 0 ? Vector3D.Dot(vel, dir) : 0;
            Vector3D lateralVec = vel - dir * closing;
            double lateral = lateralVec.Length();

            if(!departureCleared&&Vector3D.Distance(pos,routeStart)>=c.DepartureDistanceKm*1000)
            {departureCleared=true;if(c.DepartureSigEnabled)log("DEPARTURE SIG // cleared departure zone; cruise ceiling available");}
            bool wasApproach=approachActive;
            approachActive=ApproachProfile.Activate(c,approachActive,Vector3D.Distance(pos,gpsTarget),phase);
            if(approachActive&&!wasApproach)log("APPROACH SIG // limit="+ApproachProfile.Arrival(c)+"km distance="+(Vector3D.Distance(pos,gpsTarget)/1000).ToString("0.0")+"km");
            effectiveDriveRatio = CalculateDriveRatio(c);
            if (effectiveDriveRatio <= 0)
            {
                s.ApplyDockRotation(Vector3D.Zero);
                AssistDampeners(s,false);
                s.ClearThrust();
                eta = -1;
                warning = signalGovernorState;
                if (signalWaitStarted < 0) signalWaitStarted = routeClock.Elapsed.TotalSeconds;
                if (routeClock.Elapsed.TotalSeconds - signalWaitStarted > 15)
                    Abort("SIG CONTROL UNAVAILABLE // " + signalGovernorState);
                return;
            }
            signalWaitStarted = -1;
            // The RCS computer can add rotational authority during large
            // prograde and retrograde turns. Orient() releases it below five
            // degrees, so the proven single-gyro precision hold takes over.
            // Main drives remain gated separately by real heading alignment.
            if(c.RcsTurnAssist&&TurnAssistPhase(phase)&&signalBudget!=null&&signalBudget.Ready)
            {
                var feed=getSpectrum();
                if(feed!=null&&feed.DriveKmReady&&feed.DriveKm<ActiveSigKm(c)*.9)
                {
                    if(rcsTurnBudget==null||rcsTurnTopology!=s.TopologyRevision){rcsTurnBudget=feed.BuildBudget(s,true);rcsTurnTopology=s.TopologyRevision;}
                    rcsTurnBudget.SphericalBaseSquared=signalBudget.SphericalBaseSquared;rcsTurnBudget.DirectionalBaseSquared=signalBudget.DirectionalBaseSquared;
                    rcsTurnBudget.FeedbackScale=Math.Max(1,signalBudget.FeedbackScale);
                    double worst=rcsTurnBudget.PredictedSquared(new double[]{1,1,1,1,1,1});
                    s.AllowRcsTurnAssist=SignalBudget.Finite(worst)&&worst<Math.Pow(ActiveSigKm(c)*.85,2);
                }
            }
            stopAssistSpeed=StopAssistSpeed(s);
            if(dampenerAssist&&(speed>stopAssistSpeed||!CanAssistDampeners()||
                (phase!=NavPhase.INITIAL_BRAKE&&(phase!=NavPhase.TERMINAL_SETTLE||dist>c.ArrivalRadiusMeters))))
                AssistDampeners(s,false);
            if (warning.Contains("SIG") || warning.Contains("SIGNATURE")) warning = "";
            double mass = s.Mass;
            double fullThrusterAccel = s.Force(MoveDir.Forward) / mass;
            double thrusterAccel = fullThrusterAccel * effectiveDriveRatio;
            double gravityAlongRoute = dir.LengthSquared() > 0 ? Vector3D.Dot(s.Gravity, dir) : 0;
            // Toward-target gravity helps acceleration but hurts the later retro burn.
            // Reserve braking authority under the arrival ceiling from departure.
            double forwardAccel = Math.Max(.01, thrusterAccel + gravityAlongRoute);
            double plannedBrakeRatio=ApproachProfile.Ratio(signalBudget,Math.Min(ActiveSigKm(c),ApproachProfile.Arrival(c)));
            double availableBrake=fullThrusterAccel*plannedBrakeRatio-gravityAlongRoute;
            if(availableBrake<.01){Abort("APPROACH SIG TOO LOW FOR BRAKING / RAISE LIMIT");return;}
            double brakeAccel=availableBrake;
            double emergencyBrakeAccel = Math.Max(.01, fullThrusterAccel - gravityAlongRoute);
            if (emergencyBrakeAccel < .05) { Abort("BRAKING AUTHORITY TOO LOW"); return; }
            commandSpeed = GetSpeedCap(c, effectiveDriveRatio);
            double closingPositive = Math.Max(0, closing);
            double flipAllowance = FlipAllowance(c);
            // Use complete world speed, including lateral energy, rather than a possibly
            // clipped controller readout or closing-only speed.
            stopDistance=BrakingPlan.Distance(speed,forwardAccel,brakeAccel,gravityAlongRoute,flipAllowance,c.BrakeSafety);
            flipAt = stopDistance;
            flipIn = closingPositive > .1 ? Math.Max(0, (dist - stopDistance) / closingPositive) : double.PositiveInfinity;
            if(momentumEntry)
            {
                double sideAcceleration=Enumerable.Range(2,4).Min(i=>s.Force((MoveDir)i)/mass*signalBudget.Limit(i,1,idleStopCommands));
                bool keep=MomentumCapture.InWindow(vel,dir)&&MomentumCapture.HasRoom(dist,stopDistance,closing,lateral,sideAcceleration,flipAllowance);
                momentumEntry=false;
                log("MOVING ROUTE ENTRY // keep="+keep+" speed="+speed.ToString("0.0")+" lateral="+lateral.ToString("0.0")+" sideAccel="+sideAcceleration.ToString("0.000")+" distance="+dist.ToString("0")+" stop="+stopDistance.ToString("0"));
                if(!keep)SetPhase(NavPhase.INITIAL_BRAKE,"insufficient correction or stopping room for moving entry");
            }
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
            if(distanceGrowingTicks>30&&phase!=NavPhase.TERMINAL_SETTLE)
            {
                warning="OVERSHOOT RECOVERY";
                if(phase!=NavPhase.FLIP&&phase!=NavPhase.PRE_FLIP&&phase!=NavPhase.BRAKE&&phase!=NavPhase.INITIAL_BRAKE)
                    SetPhase(NavPhase.INITIAL_BRAKE,"arrest motion after passing target");
            }

            switch (phase)
            {
                case NavPhase.INITIAL_BRAKE:
                    s.ClearThrust();
                    if(speed<=stopAssistSpeed)FinishLowSpeedStop(s,vel);
                    else
                    {
                        AssistDampeners(s,false);
                        Vector3D retro=-vel/speed;s.Orient(retro,2);
                        if(s.ForwardAngleDegrees(retro)<=2)s.SetMove(MoveDir.Forward,Math.Min(effectiveDriveRatio,speed/(Math.Max(.01,fullThrusterAccel)*.8)));
                    }
                    if(speed<=.3)onTargetTicks++;else onTargetTicks=0;
                    if(onTargetTicks>=15){AssistDampeners(s,false);SetPhase(dist<=250?NavPhase.TERMINAL_SETTLE:NavPhase.ALIGN_PROGRADE,"initial motion stopped");}
                    break;
                case NavPhase.CANCEL_LATERAL:
                    s.ClearThrust();
                    if (lateral <= 1.0 || speed <= 1.0) { SetPhase(NavPhase.ALIGN_PROGRADE, "lateral clean"); break; }
                    s.ApplyWorldAcceleration(-lateralVec / 1.5, 1);
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
                    s.ApplySideDamping(vel, dir, 2.0, 1);
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
                    if(speed<=Math.Max(20,stopAssistSpeed)&&dist<=Math.Max(250,c.ArrivalRadiusMeters*20))
                    {s.ClearThrust();SetPhase(NavPhase.TERMINAL_SETTLE,"low-speed terminal envelope");break;}
                    if(speed<=5&&dist>Math.Max(250,c.ArrivalRadiusMeters*20))
                    {s.ClearThrust();SetPhase(NavPhase.INITIAL_BRAKE,"stopped short; prepare bounded continuation");break;}
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
                    // With the ship retrograde, forward thrust opposes travel.
                    // Compute the thrust ratio needed to make the stop, including gravity.
                    // A disturbance or lower MAX SIG can make the planned stop unattainable.
                    double desiredDecel = closingPositive > 0 && dist > 1 ? closingPositive * closingPositive / (2 * Math.Max(1, dist * .82)) : 0;
                    double requiredThrusterAccel = Math.Max(0, desiredDecel + gravityAlongRoute);
                    double needed = requiredThrusterAccel * mass / Math.Max(1, s.Force(MoveDir.Forward));
                    needed = Math.Max(0.0, Math.Min(1.0, needed * 1.15));
                    double brakeCommand = closing<0?effectiveDriveRatio:Math.Min(needed, effectiveDriveRatio);
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
            double stopAssistSpeed=StopAssistSpeed(s);
            if(speed>Math.Max(20,stopAssistSpeed)){AssistDampeners(s,false);SetPhase(NavPhase.INITIAL_BRAKE,"terminal speed requires main-drive braking");return;}
            if(dist>Math.Max(250,c.ArrivalRadiusMeters*20))
            {AssistDampeners(s,false);SetPhase(NavPhase.INITIAL_BRAKE,"replan long terminal recovery");return;}
            if(dist<=c.ArrivalRadiusMeters&&speed<=stopAssistSpeed)FinishLowSpeedStop(s,vel);
            if (dist <= c.ArrivalRadiusMeters && speed <= c.ArrivalSpeedMps)
            {
                arrivalTicks++;
                if (arrivalTicks >= 15)
                {
                    // Full-stop completion must not re-apply a pre-route manual drive
                    // override. Zero Zeo Nav's owned thrust and release gyros/dampeners.
                    dampenerAssist=false; s.ReleaseAll(false); active = false; SetPhase(NavPhase.ARRIVED, "full stop"); warning = "ROUTE COMPLETE";
                    log("ROUTE COMPLETE -> " + destination + " buffer=" + bufferMeters.ToString("0") + "m");
                }
                return;
            }
            arrivalTicks = 0;
            if(dist<=c.ArrivalRadiusMeters&&speed<=stopAssistSpeed)return;
            AssistDampeners(s,false);
            s.RcsOnly=speed<=stopAssistSpeed&&HasRcsStopAuthority(s);
            if(s.RcsOnly)s.Orient(s.Controller.WorldMatrix.Forward,2);
            Vector3D dir = dist > .001 ? displacement / dist : Vector3D.Zero;
            double desiredSpeed = Math.Min(18.0, Math.Max(0, dist * .22));
            if (dist < 30) desiredSpeed = Math.Min(desiredSpeed, 3.0);
            // Terminal correction also honors gyro-only attitude acquisition. If the
            // ship is still moving appreciably, get the real gyro bank close to live
            // retrograde before allowing translation/braking thrust again.
            if (speed > 1&&!s.RcsOnly)
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
            double maxRatio = 1; // Each axis is independently limited by the shared SIG budget.
            double maxAccel = Math.Max(.2, s.Accel(MoveDir.Forward, maxRatio));
            if (accel.Length() > maxAccel) { accel.Normalize(); accel *= maxAccel; }
            s.ApplyWorldAcceleration(accel, maxRatio);
        }

        private bool CanAssistDampeners()
        { return signalBudget!=null&&signalBudget.Ready&&signalBudget.PredictedSquared(new double[]{1,1,1,1,1,1})<=Math.Pow(signalBudget.TargetKm*SignalBudget.RangeMargin,2); }
        private void AssistDampeners(ShipContext s,bool enabled)
        {
            if(dampenerAssist==enabled)return;
            dampenerAssist=enabled;if(s!=null)s.SetDampeners(enabled);
            log("LOW SPEED DAMPENERS // "+(enabled?"ON / full-bank SIG budget verified":"OFF"));
        }
        private void FinishLowSpeedStop(ShipContext s,Vector3D velocity)
        {
            s.Orient(s.Controller.WorldMatrix.Forward,2);
            bool allowed=CanAssistDampeners();
            AssistDampeners(s,allowed);s.ClearThrust();
            s.RcsOnly=!allowed&&HasRcsStopAuthority(s);
            if(!allowed)s.ApplyWorldAcceleration(-velocity/.8,1);
        }
        private static readonly double[] idleStopCommands=new double[6];
        private static double RcsStopAcceleration(ShipContext s)
        {
            if(s.SignatureBudget==null||!s.SignatureBudget.Ready)return 0;
            return Enumerable.Range(0,6).Min(i=>s.RcsForce((MoveDir)i)/s.Mass*s.SignatureBudget.Limit(i,1,idleStopCommands));
        }
        private static bool HasRcsStopAuthority(ShipContext s)
        {return RcsStopAcceleration(s)>=.625;}
        internal static double StopAssistSpeed(ShipContext s)
        {
            // Budget conservatively includes every bank member even in RCS-only mode.
            // Bound the handoff by roughly eight seconds of verified RCS deceleration.
            return Math.Max(5,Math.Min(50,RcsStopAcceleration(s)*8));
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

        private void ConfigureManualFlipRcs(ShipContext s, NavConfig c)
        {
            if (s == null || c == null || !c.RcsTurnAssist) return;
            SpectrumAdapter feed = getSpectrum();
            double ceiling = TargetSigKm(c);
            if (feed == null || !feed.DriveKmReady || !SignalBudget.Finite(ceiling) || ceiling <= 0 ||
                feed.DriveKm >= ceiling * .9) return;
            try
            {
                if (rcsTurnBudget == null || rcsTurnTopology != s.TopologyRevision)
                {
                    rcsTurnBudget = feed.BuildBudget(s, true);
                    rcsTurnTopology = s.TopologyRevision;
                }
                rcsTurnBudget.SphericalBaseSquared = feed.SphericalWeakKm * feed.SphericalWeakKm;
                rcsTurnBudget.DirectionalBaseSquared = feed.DirectionalWeakKm * feed.DirectionalWeakKm;
                rcsTurnBudget.FeedbackScale = 1;
                double worst = rcsTurnBudget.PredictedSquared(new double[] { 1, 1, 1, 1, 1, 1 });
                s.AllowRcsTurnAssist = SignalBudget.Finite(worst) && worst < Math.Pow(ceiling * .85, 2);
            }
            catch { s.AllowRcsTurnAssist = false; }
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
            return ApproachProfile.Cruise(c);
        }
        private double ActiveSigKm(NavConfig c)
        { return ApproachProfile.Effective(c,active&&!departureCleared,approachActive); }

        internal static bool TurnAssistPhase(NavPhase p)
        {return p==NavPhase.ALIGN_PROGRADE||p==NavPhase.ACCELERATE||p==NavPhase.COAST||p==NavPhase.FLIP||p==NavPhase.INITIAL_BRAKE||p==NavPhase.BRAKE;}

        private double EstimateSignalRatio(NavConfig c, ShipContext s)
        {
            if (signalBudget == null || !signalBudget.Ready || s == null || s.Grid.EntityId != signalGovernorGridId) return 0;
            signalBudget.TargetKm = ActiveSigKm(c);
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
            double previousCeiling=signalBudget.TargetKm;
            signalBudget.TargetKm = ActiveSigKm(c);
            if(previousCeiling>signalBudget.TargetKm&&sp.DriveKm>=signalBudget.TargetKm*SignalBudget.RangeMargin)
                signalTrip=true; // An old cruise packet is expected after lowering the ceiling.
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
            signalGovernorState = (approachActive ? "APPROACH SIG / " : c.DepartureSigEnabled&&!departureCleared ? "DEPARTURE SIG / " : "") + (ratio >= .999 ? "FULL THRUST AVAILABLE" : "SIGNATURE BUDGET");
            if (newSample && routeClock.Elapsed.TotalSeconds - lastSignalLog >= 2)
            {
                log("SIG GOV // max=" + signalBudget.TargetKm.ToString("0.0") + "km actual=" + sp.DriveKm.ToString("0.00") +
                    "km available=" + (ratio * 100).ToString("0.00") + "% cmd=" + (s.ForwardCommandRatio * 100).ToString("0.00") +
                    "% speed=" + s.Velocity.Length().ToString("0.0") + "m/s apiSpeed="+s.Motion.ApiVelocity.Length().ToString("0.0")+"m/s measuredSpeed="+s.Motion.MeasuredVelocity.Length().ToString("0.0")+"m/s motion="+s.Motion.Source+" distance="+Vector3D.Distance(navTarget,s.Position).ToString("0.0")+"m stop="+stopDistance.ToString("0.0")+"m phase=" + phase + " align=" + s.LastAlignmentErrorDeg.ToString("0.000") + "deg rate=" + s.LastAngularRateDeg.ToString("0.000") + "deg/s cap=" + commandSpeed.ToString("0") + " [" + speedCapSource + "] // " + signalGovernorState + " // gen=" + sp.SampleGeneration);
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
            configured=Math.Max(configured,getShip()?.TurnAllowanceSeconds??180);
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
            if (seconds < .25 || seconds > 1800 || double.IsNaN(seconds) || double.IsInfinity(seconds)) return 0;
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
            effectiveDriveRatio=Math.Min(effectiveDriveRatio,ApproachProfile.Ratio(signalBudget,ApproachProfile.Effective(c,true,rawDist<=c.ApproachDistanceKm*1000)));
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
            double previewBrakeRatio=ApproachProfile.Ratio(signalBudget,ApproachProfile.Arrival(c));
            double brake = Math.Max(.01,s.Force(MoveDir.Forward)*previewBrakeRatio/mass-gravityAlong);
            commandSpeed = GetSpeedCap(c, effectiveDriveRatio);
            double flipAllowance = FlipAllowance(c);
            if (SpeedCapIsReliable() && effectiveDriveRatio > 0 && previewBrakeRatio > 0)
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

        private bool HasManualInput(Sandbox.ModAPI.IMyShipController c,int frame)
        {
            try
            {
                bool cancel=pilotInput.Observe(c.MoveIndicator,PilotLook.Rotation(c),c.RollIndicator,frame/60d);
                if(cancel)log("PILOT TAKEOVER // move="+c.MoveIndicator+" mouse="+c.RotationIndicator+" roll="+c.RollIndicator);
                return cancel;
            }
            catch { return false; }
        }

        private void SetPhase(NavPhase p, string reason)
        {
            if (phase == p) return;
            NavPhase old = phase;
            phase = p;
            if(dampenerAssist&&p!=NavPhase.INITIAL_BRAKE&&p!=NavPhase.TERMINAL_SETTLE)AssistDampeners(getShip(),false);
            onTargetTicks = 0;
            thrustAlignment.Reset();
            ShipContext s = getShip();
            if (s != null) s.ResetAim();
            if (p != NavPhase.FLIP) flipTargetSet = false;
            if (p == NavPhase.ARRIVED || p == NavPhase.ABORTED || p == NavPhase.DISARMED) idleStatusTicks = 0;
            log("STATE " + old + " -> " + p + " // " + reason);
            if(s!=null&&(p==NavPhase.PRE_FLIP||p==NavPhase.BRAKE||p==NavPhase.TERMINAL_SETTLE))
                log("BRAKE PLAN // distance="+Vector3D.Distance(navTarget,s.Position).ToString("0.0")+"m stop="+stopDistance.ToString("0.0")+"m speed="+s.Velocity.Length().ToString("0.0")+"m/s api="+s.Motion.ApiVelocity.Length().ToString("0.0")+"m/s measured="+s.Motion.MeasuredVelocity.Length().ToString("0.0")+"m/s source="+s.Motion.Source+" mass="+s.Mass.ToString("0")+"kg flipAllowance="+FlipAllowance(getConfig()).ToString("0.0")+"s");
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
                snap.VelocitySource=s.Motion.Source;snap.ApiSpeedMps=s.Motion.ApiVelocity.Length();snap.MeasuredSpeedMps=s.Motion.MeasuredVelocity.Length();
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
            snap.MaxDriveSigKm = ActiveSigKm(c);
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


