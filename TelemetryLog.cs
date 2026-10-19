using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace MsfsPhysicsCamera
{
    // Diagnostic log: what the sim reported and what the app did with it, 10 lines a second while connected,
    // one file per sim session in logs/telemetry-<start time>.csv next to the program. Each line is flushed
    // as it is written, so a crash keeps everything up to that moment. Only the newest MAX_FILES are kept.
    // Switched off with "DiagnosticLog": false in settings.json.
    public class TelemetryLog : IDisposable
    {
        private const int MAX_FILES = 10;
        private const double INTERVAL_SEC = 0.1;
        private const string HEADER = "time,camState,onGround,groundKt,accelX,accelY,accelZ,velX,velY,velZ,bankDeg,crashFlag,crashSeq,aircraftObjId,userObjId,userHasPos,userDistM,inCockpit,parked,onFoot,motionGain,headX,headY,headZ,headPitch,headRoll,headYawDeg,turnReversal,lookTargetDeg,urgency,turnLead";

        private StreamWriter? writer;
        private double sinceLast = INTERVAL_SEC;
        private bool failed;

        public void Write(double dt, SimConnectHandler.TelemetryData d, SimConnectHandler.UserSnapshot u, double userDistanceM, PhysicsEngine p)
        {
            if (failed) return;
            sinceLast += dt;
            if (sinceLast < INTERVAL_SEC) return;
            sinceLast = 0;

            try
            {
                writer ??= Open();
                var ci = CultureInfo.InvariantCulture;
                writer.WriteLine(string.Join(",",
                    DateTime.Now.ToString("HH:mm:ss.fff", ci),
                    d.CameraState, d.SimOnGround, d.GroundVelocity.ToString("F2", ci),
                    d.AccelX.ToString("F3", ci), d.AccelY.ToString("F3", ci), d.AccelZ.ToString("F3", ci),
                    d.VelX.ToString("F3", ci), d.VelY.ToString("F3", ci), d.VelZ.ToString("F3", ci),
                    (d.Bank * 180.0 / Math.PI).ToString("F2", ci),
                    d.CrashFlag, d.CrashSequence,
                    u.AircraftObjectId, u.UserObjectId, u.HasPosition ? 1 : 0, userDistanceM.ToString("F2", ci),
                    p.InCockpit ? 1 : 0, p.Parked ? 1 : 0, p.UserOnFoot ? 1 : 0, p.MotionGain.ToString("F3", ci),
                    p.HeadX.ToString("F4", ci), p.HeadY.ToString("F4", ci), p.HeadZ.ToString("F4", ci),
                    p.HeadPitch.ToString("F4", ci), p.HeadRoll.ToString("F4", ci),
                    (p.HeadYaw * 180.0 / Math.PI).ToString("F2", ci),
                    p.TurnReversal.ToString("F2", ci),
                    p.LookTargetDeg.ToString("F2", ci),
                    p.Urgency.ToString("F2", ci),
                    p.TurnLead.ToString("F2", ci)));
            }
            catch (Exception ex)
            {
                // Never let the log get in the way of the head tracking: stop logging for this run.
                Console.WriteLine("Telemetry log disabled: " + ex.Message);
                failed = true;
                Close();
            }
        }

        private static StreamWriter Open()
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            Directory.CreateDirectory(dir);
            // Names sort by start time; remove the oldest so that MAX_FILES remain with the new one.
            foreach (var old in Directory.GetFiles(dir, "telemetry-*.csv").OrderByDescending(f => f).Skip(MAX_FILES - 1))
            {
                File.Delete(old);
            }
            var w = new StreamWriter(Path.Combine(dir, $"telemetry-{DateTime.Now:yyyyMMdd-HHmmss}.csv")) { AutoFlush = true };
            w.WriteLine(HEADER);
            return w;
        }

        // Called when the sim connection ends, so the next sim session starts a new file.
        public void Close()
        {
            writer?.Dispose();
            writer = null;
        }

        public void Dispose() => Close();
    }
}
