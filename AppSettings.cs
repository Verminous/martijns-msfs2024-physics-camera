using System;
using System.IO;
using System.Text.Json;

namespace MsfsPhysicsCamera
{
    public class AppSettings
    {
        public double EffectMultiplier { get; set; } = 1.0;
        public double TransXMultiplier { get; set; } = 1.0;
        public double TransYMultiplier { get; set; } = 1.0;
        public double TransZMultiplier { get; set; } = 1.0;
        public double PitchMultiplier { get; set; } = 1.0;
        public double RollMultiplier { get; set; } = 1.0;

        public double TransXMin { get; set; } = -1.0;
        public double TransXMax { get; set; } = 1.0;
        public double TransYMin { get; set; } = -1.0;
        public double TransYMax { get; set; } = 1.0;
        public double TransZMin { get; set; } = -1.0;
        public double TransZMax { get; set; } = 1.0;
        public double PitchMin { get; set; } = -0.05;
        public double PitchMax { get; set; } = 0.05;
        public double RollMin { get; set; } = -0.05;
        public double RollMax { get; set; } = 0.05;

        public double WashoutStrength { get; set; } = 0.7;

        public bool SimulateGravity { get; set; } = true;
        public bool OnlyInCockpit { get; set; } = true;

        // Defaults from flight training practice: a standard-rate turn needs about airspeed/10 + 7 degrees
        // of bank (about 16 degrees at 90 kt), small heading corrections about half of that, so heads
        // start turning at 10 degrees of held bank; a 20-25 degree look keeps the instruments in view.
        public bool LookIntoTurns { get; set; } = true;
        public double TurnLookStrength { get; set; } = 1.0;
        public double TurnLookMax { get; set; } = 25.0;
        public double TurnLookStart { get; set; } = 10.0;
        public double TurnLookReaction { get; set; } = 1.0;
        public double TurnLookSpeed { get; set; } = 1.0;
        public double TurnLookSnap { get; set; } = 1.0;

        public bool MinimizeOnStartup { get; set; } = false;
        public bool MinimizeToTray { get; set; } = false;
        public bool ExitWhenMsfsExits { get; set; } = false;

        public double? WindowTop { get; set; }
        public double? WindowLeft { get; set; }

        private static string SettingsFilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to load settings: " + ex.Message);
            }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to save settings: " + ex.Message);
            }
        }
    }
}
