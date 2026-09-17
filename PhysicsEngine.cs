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

        public double? TestOverrideX = null;
        public double? TestOverrideY = null;
        public double? TestOverrideZ = null;

        private double velX = 0, velY = 0, velZ = 0;
        private double velPitch = 0, velRoll = 0;

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
            bool simulateGravity = true,
            bool onlyInCockpit = true)
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

            double extForceX = applyPhysics ? devX * scaleX : 0;
            double extForceY = applyPhysics ? devY * scaleY : 0;
            double extForceZ = applyPhysics ? devZ * scaleZ : 0;

            // Update Spring-Mass-Damper for each axis
            UpdateAxis(ref HeadX, ref velX, extForceX, dt, minClamp: transXMin, maxClamp: transXMax);
            
            // For Y-axis (vertical bumps), we want a much faster response (stiffer spring, lower mass effect)
            // so high frequency bumps from taxiing aren't completely swallowed by the low-pass filter effect.
            UpdateAxis(ref HeadY, ref velY, extForceY, dt, k: 500, c: 40, m: 2.0, minClamp: transYMin, maxClamp: transYMax);
            
            UpdateAxis(ref HeadZ, ref velZ, extForceZ, dt, minClamp: transZMin, maxClamp: transZMax);
            
            // Pitch and Roll based on X/Z kinematic accelerations (head tilts forward when braking)
            // We use raw Accel here because using specific force (dev) causes the camera to counteract 
            // the aircraft's attitude (e.g. looking down while climbing, or rolling against the turn).
            double extForcePitch = applyPhysics ? telemetry.AccelZ * SCALE_ROT_PITCH * effectMultiplier * pitchMult : 0;
            double extForceRoll = applyPhysics ? telemetry.AccelX * SCALE_ROT_ROLL * effectMultiplier * rollMult : 0;

            UpdateAxis(ref HeadPitch, ref velPitch, extForcePitch, dt, k: 300, c: 40);
            UpdateAxis(ref HeadRoll, ref velRoll, extForceRoll, dt, k: 300, c: 40);

            if (TestOverrideX.HasValue) HeadX = TestOverrideX.Value;
            if (TestOverrideY.HasValue) HeadY = TestOverrideY.Value;
            if (TestOverrideZ.HasValue) HeadZ = TestOverrideZ.Value;
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
