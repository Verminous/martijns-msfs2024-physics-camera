using System;

namespace MsfsPhysicsCamera
{
    public class PhysicsEngine
    {
        // Spring-Mass-Damper for Head movement
        // F = m*a
        // F_spring = -k * x
        // F_damper = -c * v
        // m*a_head = F_ext + F_spring + F_damper

        private double headMass = 5.0; // kg
        private double springK = 200.0; // Spring constant
        private double damperC = 30.0; // Damping constant

        // Current state of head offset
        public double HeadX = 0;
        public double HeadY = 0;
        public double HeadZ = 0;
        public double HeadPitch = 0;
        public double HeadRoll = 0;
        public double HeadYaw = 0;   // radians, positive = look right (look into turns)

        public double? TestOverrideX = null;
        public double? TestOverrideY = null;
        public double? TestOverrideZ = null;
        public double? TestOverridePitch = null;
        public double? TestOverrideRoll = null;

        private double velX = 0, velY = 0, velZ = 0;
        private double velPitch = 0, velRoll = 0;
        private double velYaw = 0;
        private double filteredBankDeg = 0;
        private double prevBankDeg = 0;
        private double rollRateDegPerSec = 0;
        private double heldUrgency = 1.0;
        private double rollRateSignedDegPerSec = 0;
        private double airborneSec = 0;
        private double heldLookDeg = 0;
        private bool inTransit = false;
        private double transitSlowSec = 0;
        private double rollSec = 0, rollTravelDeg = 0, rollSlowSec = 0;
        private int rollDir = 0;
        private double prevRollRateSignedDegPerSec = 0, rollAccelDegPerSec2 = 0;
        private double transitAnchorBankDeg = 0, transitAnchorLookDeg = 0;

        // Why the head is (not) being moved, for the window's status line and the diagnostic log.
        public bool InCockpit { get; private set; }
        public bool Parked { get; private set; }
        public bool UserOnFoot { get; private set; }
        public double MotionGain { get; private set; } = 1.0;
        public double TurnReversal { get; private set; }
        public double LookTargetDeg { get; private set; }   // where look-into-turns is steering the head (before the spring)
        public double Urgency { get; private set; } = 1.0;
        public double TurnLead { get; private set; }   // 1 while the head looks ahead in a deliberate roll (point 6)

        private double prevRawX = 0, prevRawY = 0, prevRawZ = 0;
        private double prevRawPitch = 0, prevRawRoll = 0;
        private double filteredX = 0, filteredY = 0, filteredZ = 0;
        private double filteredPitch = 0, filteredRoll = 0;

#if DEBUG
        private double debugTimeAccumulator = 0.0;
#endif

        private const double DT_MAX = 0.05;
        private const double G_FEET_PER_SEC2 = 32.174;
        private const double SCALE_TRANS_X = 3.0;
        private const double SCALE_TRANS_Y = -3.0;
        private const double SCALE_TRANS_Z = 3.0;
        private const double SCALE_ROT_PITCH = -0.05;
        private const double SCALE_ROT_ROLL = 0.05;

        // Look into turns: above the start angle the turn eases in quadratically over this many degrees
        // of bank, then continues linearly (same slope), so crossing the start angle gives no sudden kick.
        private const double TURN_LOOK_SOFT_ZONE_DEG = 10.0;
        // Sign between PLANE BANK DEGREES and a head turn towards the inside of the turn.
        private const double TURN_LOOK_SIGN = 1.0;
        // Head turn speed: a critically damped spring (m = 1) with natural frequency omega. The base 2 rad/s
        // (k = 4, c = 4) settles on its target in about 1.5 s; the speed slider and the urgency scale omega.
        private const double TURN_LOOK_OMEGA = 2.0;
        // Urgency from roll rate: rolling faster than a gentle roll-in makes the head react and move faster.
        private const double ROLL_RATE_GENTLE_DEG = 8.0;    // deg/s, normal roll-in into a turn
        private const double ROLL_RATE_REF_DEG = 10.0;      // deg/s above gentle per +1x urgency (at 1.0 sensitivity)
        private const double ROLL_RATE_SMOOTH_SEC = 0.15;   // light smoothing of the bank-angle derivative
        private const double URGENCY_MAX = 4.0;
        // A sharp roll lasts well under a second; the urgency fades back to 1 over this time constant, so
        // the head finishes its turn quickly instead of slowing down the moment the wings stop rolling.
        private const double URGENCY_HOLD_SEC = 2.0;
        // Explicit integration stays well-behaved while c * dt stays small; faster heads use sub-steps.
        private const double MAX_DAMPING_STEP = 0.5;
        // Roll towards level while the head is turned (a roll-out or the first half of a reversal), at any speed
        // above a gentle drift: the head has been turned more than TRANSIT_HEAD_DEG within about the last second and
        // the bank moves towards level faster than TRANSIT_ROLL_RATE_ON_DEG. The normal target would bring the head
        // to the centre while the bank is still above the start angle and hold it there while the bank sweeps through
        // level (a visible pause in a reversal), and keep it at the maximum while a steep bank first comes down.
        // Instead the head follows the bank in proportion, from where it is to the centre exactly at level, and on
        // through it until the other side's normal target is larger. The sweep ends only when the roll has stayed
        // below TRANSIT_ROLL_RATE_OFF_DEG (or turned back) for TRANSIT_END_SEC; if the roll picks up again in the same
        // direction while the sweep is still fading (weight above TRANSIT_RESUME_WEIGHT), the same sweep continues.
        // Without that, a roll rate hovering at the threshold restarted the sweep from the lagging head again and
        // again, which held the head in place (logged 2026-10-19 22:52:19, a jet rolling at about 45 deg/s).
        // After the sweep the normal target takes over again over TRANSIT_RELEASE_SEC.
        private const double TRANSIT_HEAD_DEG = 2.0;
        private const double TRANSIT_HEAD_MEMORY_SEC = 1.0;
        private const double TRANSIT_ROLL_RATE_ON_DEG = 6.0;
        private const double TRANSIT_ROLL_RATE_OFF_DEG = 3.0;
        private const double TRANSIT_END_SEC = 0.25;
        private const double TRANSIT_RESUME_WEIGHT = 0.3;
        private const double TRANSIT_MIN_BANK_DEG = 3.0;
        private const double TRANSIT_RELEASE_SEC = 1.0;
        // Looking ahead in a deliberate roll (into a turn, out of it, or through a reversal). Pilots look where they
        // are rolling to; tied to the averaged bank and a gentle head, the head in a slowly rolling aircraft trailed
        // the wings by about 2 s on a roll-in (Cessna, logged 2026-10-19 23:16-23:17: 6 to 12 deg/s of roll, 2 to 6
        // deg/s of head). Deliberate = the bank has moved the same way for LEAD_ROLL_SEC and over
        // LEAD_ROLL_TRAVEL_DEG, longer and larger than a turbulence bump or a small correction; a sweep (point 5)
        // that has passed level counts at once. Then the head aims at the actual bank plus LEAD_SEC of roll (at most
        // LEAD_MAX_DEG, and never past where a slowing roll will stop), skipping the averaging, and moves at least
        // LEAD_MIN_URGENCY times its normal speed. Fades in over LEAD_RISE_SEC and out over LEAD_FALL_SEC.
        // Why only half a second: the app cannot know where the pilot will stop rolling. With 1 s the head ran
        // about 6° past its final angle in a Cessna roll-in and came back; with 0.5 s it arrives with the wings and
        // settles about 2°. On a plain roll-out it drifts at most about 1° past the centre.
        private const double LEAD_ROLL_SEC = 0.6;
        private const double LEAD_ROLL_TRAVEL_DEG = 12.0;
        private const double LEAD_SEC = 0.5;
        private const double LEAD_MAX_DEG = 5.0;
        private const double LEAD_RISE_SEC = 0.15;
        private const double LEAD_FALL_SEC = 0.5;
        private const double LEAD_MIN_URGENCY = 3.0;

        // Standing still on the ground: nothing there should throw the pilot's head around, but the sim can
        // keep a wreck (or a parked aircraft) jittering, which the accelerations pass straight on to the head.
        // Parked below PARKED_BELOW_KT ground speed, moving again above PARKED_ABOVE_KT (hysteresis).
        private const double PARKED_BELOW_KT = 1.0;
        private const double PARKED_ABOVE_KT = 2.0;
        // Leaving the ground also ends it, but only after this long in the air: a bouncing wreck flickers
        // SIM ON GROUND off for a few tenths of a second (logged 2026-10-19), a helicopter lifting off does not.
        private const double PARKED_AIRBORNE_SEC = 0.5;
        // Head motion fades in and out over this time constant instead of switching, so pausing or resuming
        // (camera change, getting out, stopping) gives no kick.
        private const double MOTION_FADE_SEC = 0.3;

        public void Update(SimConnectHandler.TelemetryData telemetry, double dt, 
            double effectMultiplier = 1.0,
            double transXMult = 1.0,
            double transYMult = 1.0,
            double transZMult = 1.0,
            double pitchMult = 1.0,
            double rollMult = 1.0,
            double transXMin = -1.0, double transXMax = 1.0,
            double transYMin = -1.0, double transYMax = 1.0,
            double transZMin = -1.0, double transZMax = 1.0,
            double pitchMin = -1.0, double pitchMax = 1.0,
            double rollMin = -1.0, double rollMax = 1.0,
            double washoutStrength = 0.0,
            bool simulateGravity = true,
            bool onlyInCockpit = true,
            bool lookIntoTurns = false,
            double turnLookStrength = 1.0,
            double turnLookMaxDeg = 25.0,
            double turnLookStartDeg = 10.0,
            double turnLookReactionSec = 1.0,
            double turnLookSpeed = 1.0,
            double turnLookSnap = 1.0,
            bool userOnFoot = false)
        {
            if (dt <= 0) return;
            dt = Math.Min(dt, DT_MAX);

#if DEBUG
            debugTimeAccumulator += dt;
            if (debugTimeAccumulator >= 10.0)
            {
                debugTimeAccumulator = 0.0;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [PhysicsEngine] Input: AccelX={telemetry.AccelX:F2}, AccelY={telemetry.AccelY:F2}, AccelZ={telemetry.AccelZ:F2}, VelX={telemetry.VelX:F2}, VelY={telemetry.VelY:F2}, VelZ={telemetry.VelZ:F2}, Pitch={telemetry.Pitch:F2}, Bank={telemetry.Bank:F2}, CamState={telemetry.CameraState} | Output: HeadX={HeadX:F3}, HeadY={HeadY:F3}, HeadZ={HeadZ:F3}, HeadPitch={HeadPitch:F3}, HeadRoll={HeadRoll:F3}");
            }
#endif

            // If we only want physics in the cockpit, and we are not in the cockpit (CameraState == 2),
            // we set all external forces to 0. The spring-mass-damper will naturally return the head to center.
            // The same when the pilot is out walking (MSFS 2024 still reports CameraState 2 then) or the aircraft
            // stands still on the ground. The forces fade out and in over MOTION_FADE_SEC.
            InCockpit = !onlyInCockpit || telemetry.CameraState == 2;
            airborneSec = telemetry.SimOnGround != 0 ? 0.0 : airborneSec + dt;
            if (telemetry.SimOnGround != 0 && telemetry.GroundVelocity < PARKED_BELOW_KT) Parked = true;
            else if (airborneSec > PARKED_AIRBORNE_SEC || telemetry.GroundVelocity > PARKED_ABOVE_KT) Parked = false;
            UserOnFoot = userOnFoot;
            bool applyPhysics = InCockpit && !UserOnFoot && !Parked;
            MotionGain += ((applyPhysics ? 1.0 : 0.0) - MotionGain) * (dt / (MOTION_FADE_SEC + dt));

            // 1. Calculate external forces based on aircraft acceleration
            // It turns out FreeTrack/MSFS expects +Z for backward and -Z for forward.
            // When accelerating (+AccelZ), we want the head to move backward (+Z).
            // When braking (-AccelZ), we want the head to move forward (-Z).
            // Therefore, Z scale should be positive!
            
            double scaleX = SCALE_TRANS_X * effectMultiplier * transXMult; 
            double scaleY = SCALE_TRANS_Y * effectMultiplier * transYMult; 
            double scaleZ = SCALE_TRANS_Z * effectMultiplier * transZMult; 

            // MSFS accelerations are kinematic (coordinate) accelerations and do not include gravity.
            double pitch = telemetry.Pitch;
            double bank = telemetry.Bank;

            double devX = telemetry.AccelX;
            double devY = telemetry.AccelY;
            double devZ = telemetry.AccelZ;

            if (simulateGravity)
            {
                // We calculate the specific force (what the pilot feels) by subtracting gravity.
                // Gravity in Earth frame is 1G downwards. We project it onto the body axes.
                double gX = G_FEET_PER_SEC2 * Math.Cos(pitch) * Math.Sin(bank);
                double gY = -G_FEET_PER_SEC2 * Math.Cos(pitch) * Math.Cos(bank);
                double gZ = G_FEET_PER_SEC2 * Math.Sin(pitch);

                double specificForceX = telemetry.AccelX - gX;
                double specificForceY = telemetry.AccelY - gY;
                double specificForceZ = telemetry.AccelZ - gZ;

                // We subtract 1G from the Y axis so the head rests at 0 in steady level flight.
                devX = specificForceX;
                devY = specificForceY - G_FEET_PER_SEC2;
                devZ = specificForceZ;
            }

            double rawX = MotionGain * devX * scaleX;
            double rawY = MotionGain * devY * scaleY;
            double rawZ = MotionGain * devZ * scaleZ;

            double alpha = 1.0;
            if (washoutStrength > 0.001)
            {
                alpha = 1.0 / (1.0 + washoutStrength * dt);
            }

            filteredX = alpha * (filteredX + rawX - prevRawX);
            filteredY = alpha * (filteredY + rawY - prevRawY);
            filteredZ = alpha * (filteredZ + rawZ - prevRawZ);

            prevRawX = rawX;
            prevRawY = rawY;
            prevRawZ = rawZ;

            // Update Spring-Mass-Damper for each axis
            UpdateAxis(ref HeadX, ref velX, filteredX, dt, minClamp: transXMin, maxClamp: transXMax);
            
            // For Y-axis (vertical bumps), we want a much faster response (stiffer spring, lower mass effect)
            // so high frequency bumps from taxiing aren't completely swallowed by the low-pass filter effect.
            UpdateAxis(ref HeadY, ref velY, filteredY, dt, k: 500, c: 40, m: 2.0, minClamp: transYMin, maxClamp: transYMax);
            
            UpdateAxis(ref HeadZ, ref velZ, filteredZ, dt, minClamp: transZMin, maxClamp: transZMax);
            
            // Pitch and Roll based on X/Z kinematic accelerations (head tilts forward when braking)
            // We use raw Accel here because using specific force (dev) causes the camera to counteract 
            // the aircraft's attitude (e.g. looking down while climbing, or rolling against the turn).
            double rawPitch = MotionGain * telemetry.AccelZ * SCALE_ROT_PITCH * effectMultiplier * pitchMult;
            double rawRoll = MotionGain * telemetry.AccelX * SCALE_ROT_ROLL * effectMultiplier * rollMult;

            filteredPitch = alpha * (filteredPitch + rawPitch - prevRawPitch);
            filteredRoll = alpha * (filteredRoll + rawRoll - prevRawRoll);

            prevRawPitch = rawPitch;
            prevRawRoll = rawRoll;

            UpdateAxis(ref HeadPitch, ref velPitch, filteredPitch, dt, k: 300, c: 40, minClamp: pitchMin, maxClamp: pitchMax);
            UpdateAxis(ref HeadRoll, ref velRoll, filteredRoll, dt, k: 300, c: 40, minClamp: rollMin, maxClamp: rollMax);

            // Look into turns. In a coordinated turn the sideways acceleration is close to zero, so nothing
            // above moves the head towards the turn. Pilots turn their head into a turn they have committed
            // to, roughly in proportion to the bank angle, but not for small corrections. So:
            // 1. the bank is averaged over the reaction time (first-order low-pass), so short wobbles from
            //    gusts or corrections average out while a held bank builds up;
            // 2. bank below the start angle is ignored;
            // 3. above it the turn eases in (soft zone) and is capped at the maximum;
            // 4. a sharp roll is urgent: the faster the bank changes, the shorter the averaging and the
            //    faster the head moves (a gentle roll-in keeps the normal speed);
            // 5. a fast roll towards level while the head is turned moves the head in proportion to the bank,
            //    reaching the centre exactly at level, so a reversal is one continuous sweep (see TRANSIT_*);
            // 6. in a deliberate roll the head looks a little ahead of the actual bank and moves briskly (see LEAD_*).
            double bankDeg = bank * (180.0 / Math.PI);
            double rawRollRateSigned = (bankDeg - prevBankDeg) / dt;
            double rawRollRate = Math.Abs(rawRollRateSigned);
            prevBankDeg = bankDeg;
            rollRateDegPerSec += (rawRollRate - rollRateDegPerSec) * (dt / (ROLL_RATE_SMOOTH_SEC + dt));
            rollRateSignedDegPerSec += (rawRollRateSigned - rollRateSignedDegPerSec) * (dt / (ROLL_RATE_SMOOTH_SEC + dt));
            double urgencyNow = 1.0 + turnLookSnap * Math.Max(0.0, rollRateDegPerSec - ROLL_RATE_GENTLE_DEG) / ROLL_RATE_REF_DEG;
            heldUrgency = 1.0 + (heldUrgency - 1.0) * Math.Exp(-dt / URGENCY_HOLD_SEC);
            heldUrgency = Math.Max(heldUrgency, urgencyNow);
            double urgency = Math.Min(heldUrgency, URGENCY_MAX);

            double reactionSec = turnLookReactionSec / urgency;
            filteredBankDeg = reactionSec > 0.01
                ? filteredBankDeg + (bankDeg - filteredBankDeg) * (dt / (reactionSec + dt))
                : bankDeg;

            // 6. a deliberate roll (either way): look ahead to where the bank is going.
            double rollRateSigned = rollRateSignedDegPerSec;
            bool rollingNow = Math.Abs(rollRateSigned) > TRANSIT_ROLL_RATE_ON_DEG;
            if (rollingNow && Math.Sign(rollRateSigned) != rollDir)
            {
                rollDir = Math.Sign(rollRateSigned);   // a new roll (or the other way): count from zero
                rollSec = 0;
                rollTravelDeg = 0;
            }
            if (rollingNow)
            {
                rollSlowSec = 0;
                rollSec += dt;
                rollTravelDeg += Math.Abs(rollRateSigned) * dt;
            }
            else
            {
                // A short pause does not end the roll; turning the other way does, at once (turbulence).
                rollSlowSec += dt;
                bool turnedBack = Math.Abs(rollRateSigned) > TRANSIT_ROLL_RATE_OFF_DEG && Math.Sign(rollRateSigned) != rollDir;
                if (turnedBack || rollSlowSec > TRANSIT_END_SEC) { rollSec = 0; rollTravelDeg = 0; rollDir = 0; }
            }
            bool sweepPastLevel = TurnReversal > 0.5 && transitAnchorBankDeg != 0 && Math.Sign(bankDeg) == -Math.Sign(transitAnchorBankDeg)
                && Math.Sign(rollRateSigned) == Math.Sign(bankDeg);
            bool deliberate = rollingNow && ((rollSec >= LEAD_ROLL_SEC && rollTravelDeg >= LEAD_ROLL_TRAVEL_DEG) || sweepPastLevel);
            double leadTau = deliberate ? LEAD_RISE_SEC : LEAD_FALL_SEC;
            TurnLead += ((deliberate ? 1.0 : 0.0) - TurnLead) * (dt / (leadTau + dt));
            // How far ahead: LEAD_SEC of roll (at most LEAD_MAX_DEG), but never past where the wings will stop. An
            // aircraft slows its roll before it stops; once the roll is slowing, the stopping point is
            // rate² / (2 x deceleration) ahead, so the head aims there instead of overshooting and coming back.
            double rawRollAccel = (rollRateSigned - prevRollRateSignedDegPerSec) / dt;
            prevRollRateSignedDegPerSec = rollRateSigned;
            rollAccelDegPerSec2 += (rawRollAccel - rollAccelDegPerSec2) * (dt / (ROLL_RATE_SMOOTH_SEC + dt));
            double aheadAbsDeg = Math.Min(Math.Abs(rollRateSigned) * LEAD_SEC, LEAD_MAX_DEG);
            if (Math.Sign(rollAccelDegPerSec2) == -Math.Sign(rollRateSigned) && Math.Abs(rollAccelDegPerSec2) > 1.0)
            {
                aheadAbsDeg = Math.Min(aheadAbsDeg, rollRateSigned * rollRateSigned / (2.0 * Math.Abs(rollAccelDegPerSec2)));
            }
            double aheadDeg = Math.Sign(rollRateSigned) * aheadAbsDeg;
            double lookBankDeg = filteredBankDeg + TurnLead * (bankDeg + aheadDeg - filteredBankDeg);

            // 2.-3. the normal target ("look" = the head turn on the bank's sign side), for the averaged bank, or
            // during a deliberate roll the bank ahead.
            double excessDeg = Math.Max(0.0, Math.Abs(lookBankDeg) - turnLookStartDeg);
            double easedDeg = excessDeg < TURN_LOOK_SOFT_ZONE_DEG
                ? excessDeg * excessDeg / (2.0 * TURN_LOOK_SOFT_ZONE_DEG)
                : excessDeg - TURN_LOOK_SOFT_ZONE_DEG / 2.0;
            double normalLookDeg = Math.Sign(lookBankDeg) * Math.Min(easedDeg * turnLookStrength, turnLookMaxDeg);

            // 5. roll towards level while the head is turned.
            double lookDeg = TURN_LOOK_SIGN * HeadYaw * (180.0 / Math.PI);
            heldLookDeg *= Math.Exp(-dt / TRANSIT_HEAD_MEMORY_SEC);
            if (Math.Abs(lookDeg) > Math.Abs(heldLookDeg)) heldLookDeg = lookDeg;
            // The roll is deliberate, so the sweep follows the actual bank, not the averaged one.
            double rollRate = Math.Abs(rollRateSignedDegPerSec);
            if (!inTransit)
            {
                // Resume the same sweep only while the bank has not gone back beyond where it started; otherwise a
                // sweep anchored in a shallower bank would hold the head back in a steeper one.
                bool resume = TurnReversal > TRANSIT_RESUME_WEIGHT && transitAnchorBankDeg != 0
                    && rollRate > TRANSIT_ROLL_RATE_ON_DEG
                    && Math.Sign(rollRateSignedDegPerSec) == -Math.Sign(transitAnchorBankDeg)
                    && (Math.Sign(bankDeg) != Math.Sign(transitAnchorBankDeg) || Math.Abs(bankDeg) <= Math.Abs(transitAnchorBankDeg));
                bool start = rollRate > TRANSIT_ROLL_RATE_ON_DEG
                    && Math.Abs(heldLookDeg) > TRANSIT_HEAD_DEG
                    && Math.Abs(bankDeg) > TRANSIT_MIN_BANK_DEG
                    && Math.Sign(lookDeg) == Math.Sign(bankDeg)
                    && Math.Sign(rollRateSignedDegPerSec) == -Math.Sign(bankDeg);
                if (resume)
                {
                    inTransit = true;   // same sweep: keep its anchor
                }
                else if (start)
                {
                    // Anchored on where the head should be for this bank (the normal target), not on where the
                    // lagging head happens to be: anchoring on the head ratcheted it towards the centre in
                    // turbulence, every new sweep starting a little lower.
                    inTransit = true;
                    transitAnchorBankDeg = bankDeg;
                    transitAnchorLookDeg = Math.Sign(bankDeg) * Math.Max(0.0, Math.Sign(bankDeg) * normalLookDeg);
                }
                transitSlowSec = 0;
            }
            else
            {
                bool rolling = rollRate > TRANSIT_ROLL_RATE_OFF_DEG && Math.Sign(rollRateSignedDegPerSec) == -Math.Sign(transitAnchorBankDeg);
                transitSlowSec = rolling ? 0.0 : transitSlowSec + dt;
                if (transitSlowSec > TRANSIT_END_SEC) inTransit = false;   // the roll stopped or turned back
            }
            TurnReversal = inTransit ? 1.0 : TurnReversal * Math.Exp(-dt / TRANSIT_RELEASE_SEC);

            double targetYaw = 0;
            if (lookIntoTurns)
            {
                double lookTargetDeg = normalLookDeg;
                if (TurnReversal > 0 && transitAnchorBankDeg != 0)
                {
                    // Straight line from the target when the roll started to the centre at level, a little ahead
                    // of the actual bank (the same look-ahead as point 6), so the head reaches the centre with the
                    // wings, not after them. On a plain roll-out that lets it drift up to about 1° past the centre.
                    double lineBankDeg = bankDeg + aheadDeg * TurnLead;
                    double lineDeg = transitAnchorLookDeg * lineBankDeg / transitAnchorBankDeg;
                    double normalSameSideDeg = Math.Sign(lookBankDeg) == Math.Sign(lineBankDeg) ? Math.Abs(normalLookDeg) : 0.0;
                    // Never further out than at the start, on either side; past level the normal target takes over
                    // where it is larger.
                    double cappedLineDeg = Math.Min(Math.Abs(lineDeg), Math.Abs(transitAnchorLookDeg));
                    double transitDeg = Math.Sign(lineBankDeg) == Math.Sign(transitAnchorBankDeg)
                        ? Math.Sign(lineDeg) * cappedLineDeg
                        : Math.Sign(lineBankDeg) * Math.Max(cappedLineDeg, normalSameSideDeg);
                    transitDeg = Math.Clamp(transitDeg, -turnLookMaxDeg, turnLookMaxDeg);
                    lookTargetDeg = TurnReversal * transitDeg + (1.0 - TurnReversal) * normalLookDeg;
                }
                targetYaw = MotionGain * TURN_LOOK_SIGN * lookTargetDeg * (Math.PI / 180.0);
                LookTargetDeg = lookTargetDeg;
            }
            // Brisk head in any deliberate roll: looking ahead (6) or sweeping towards and through level (5).
            double headUrgency = Math.Max(urgency, 1.0 + (LEAD_MIN_URGENCY - 1.0) * Math.Max(TurnLead, TurnReversal));
            Urgency = headUrgency;
            // A critically damped spring towards the target (equilibrium where -k * pos + k * target = 0),
            // in sub-steps when it is fast so the explicit integration never overshoots.
            double omega = TURN_LOOK_OMEGA * Math.Max(0.05, turnLookSpeed) * headUrgency;
            double yawK = omega * omega;
            double yawC = 2.0 * omega;
            int steps = Math.Max(1, (int)Math.Ceiling(yawC * dt / MAX_DAMPING_STEP));
            for (int i = 0; i < steps; i++)
            {
                UpdateAxis(ref HeadYaw, ref velYaw, yawK * targetYaw, dt / steps, k: yawK, c: yawC, m: 1.0);
            }

            if (TestOverrideX.HasValue) HeadX = TestOverrideX.Value;
            if (TestOverrideY.HasValue) HeadY = TestOverrideY.Value;
            if (TestOverrideZ.HasValue) HeadZ = TestOverrideZ.Value;
            if (TestOverridePitch.HasValue) HeadPitch = TestOverridePitch.Value;
            if (TestOverrideRoll.HasValue) HeadRoll = TestOverrideRoll.Value;
        }

        private void UpdateAxis(ref double pos, ref double vel, double extForce, double dt, double? k = null, double? c = null, double? m = null, double? minClamp = null, double? maxClamp = null)
        {
            double actualK = k ?? springK;
            double actualC = c ?? damperC;
            double actualM = m ?? headMass;

            double forceSpring = -actualK * pos;
            double forceDamper = -actualC * vel;
            double totalForce = extForce + forceSpring + forceDamper;

            double accel = totalForce / actualM;
            
            vel += accel * dt;
            pos += vel * dt;

            if (minClamp.HasValue && pos < minClamp.Value)
            {
                pos = minClamp.Value;
                if (vel < 0) vel = 0;
            }
            if (maxClamp.HasValue && pos > maxClamp.Value)
            {
                pos = maxClamp.Value;
                if (vel > 0) vel = 0;
            }
        }
    }
}
