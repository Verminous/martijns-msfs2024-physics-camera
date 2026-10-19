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
            double turnLookSnap = 1.0)
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
            bool applyPhysics = !onlyInCockpit || telemetry.CameraState == 2;

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

            double rawX = applyPhysics ? devX * scaleX : 0;
            double rawY = applyPhysics ? devY * scaleY : 0;
            double rawZ = applyPhysics ? devZ * scaleZ : 0;

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
            double rawPitch = applyPhysics ? telemetry.AccelZ * SCALE_ROT_PITCH * effectMultiplier * pitchMult : 0;
            double rawRoll = applyPhysics ? telemetry.AccelX * SCALE_ROT_ROLL * effectMultiplier * rollMult : 0;

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
            //    faster the head moves (a gentle roll-in keeps the normal speed).
            double bankDeg = bank * (180.0 / Math.PI);
            double rawRollRate = Math.Abs(bankDeg - prevBankDeg) / dt;
            prevBankDeg = bankDeg;
            rollRateDegPerSec += (rawRollRate - rollRateDegPerSec) * (dt / (ROLL_RATE_SMOOTH_SEC + dt));
            double urgencyNow = 1.0 + turnLookSnap * Math.Max(0.0, rollRateDegPerSec - ROLL_RATE_GENTLE_DEG) / ROLL_RATE_REF_DEG;
            heldUrgency = 1.0 + (heldUrgency - 1.0) * Math.Exp(-dt / URGENCY_HOLD_SEC);
            heldUrgency = Math.Max(heldUrgency, urgencyNow);
            double urgency = Math.Min(heldUrgency, URGENCY_MAX);

            double reactionSec = turnLookReactionSec / urgency;
            filteredBankDeg = reactionSec > 0.01
                ? filteredBankDeg + (bankDeg - filteredBankDeg) * (dt / (reactionSec + dt))
                : bankDeg;

            double targetYaw = 0;
            if (applyPhysics && lookIntoTurns)
            {
                double excessDeg = Math.Max(0.0, Math.Abs(filteredBankDeg) - turnLookStartDeg);
                double easedDeg = excessDeg < TURN_LOOK_SOFT_ZONE_DEG
                    ? excessDeg * excessDeg / (2.0 * TURN_LOOK_SOFT_ZONE_DEG)
                    : excessDeg - TURN_LOOK_SOFT_ZONE_DEG / 2.0;
                double yawDeg = Math.Min(easedDeg * turnLookStrength, turnLookMaxDeg);
                targetYaw = TURN_LOOK_SIGN * Math.Sign(filteredBankDeg) * yawDeg * (Math.PI / 180.0);
            }
            // A critically damped spring towards the target (equilibrium where -k * pos + k * target = 0),
            // in sub-steps when it is fast so the explicit integration never overshoots.
            double omega = TURN_LOOK_OMEGA * Math.Max(0.05, turnLookSpeed) * urgency;
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
