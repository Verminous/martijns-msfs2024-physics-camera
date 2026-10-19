using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Brush = System.Windows.Media.Brush;

namespace MsfsPhysicsCamera
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private const double MAX_YAW_RAD = 60.0 * Math.PI / 180.0;

        // Out of the aircraft (MSFS 2024 walkaround): the current user object is more than this far from the
        // aircraft for ON_FOOT_CONFIRM_SEC. The two positions arrive as separate SimConnect messages, so a moving
        // aircraft can be up to a few frames apart from itself; the tolerance grows with speed for that.
        private const double ON_FOOT_MIN_DISTANCE_M = 1.0;
        private const double ON_FOOT_FRAME_SKEW_SEC = 0.1;
        private const double ON_FOOT_CONFIRM_SEC = 0.3;
        private const double KNOTS_TO_MS = 0.514444;
        private const double FEET_TO_M = 0.3048;

        private SimConnectHandler? simConnect;
        private FreeTrackInjector? injector;
        private TelemetryLog? telemetryLog;
        private PhysicsEngine physics;
        private double onFootFarTime;
        private Stopwatch stopwatch;
        private double lastTime;
        private bool isRunning = true;
        private Thread physicsThread;

        public event PropertyChangedEventHandler? PropertyChanged;

        // Binding Properties
        public double AccelX { get; set; }
        public double AccelY { get; set; }
        public double AccelZ { get; set; }
        
        public double VelX { get; set; }
        public double VelY { get; set; }
        public double VelZ { get; set; }
        
        public double PlanePitch { get; set; }
        public double PlaneBank { get; set; }

        public string StatusText { get; set; } = "Initializing...";

        public double HeadX { get; set; }
        public double HeadY { get; set; }
        public double HeadZ { get; set; }
        public double HeadPitch { get; set; }
        public double HeadRoll { get; set; }
        public double HeadYawDeg { get; set; }
        public string MotionStatus { get; set; } = "";
        public bool MotionActive { get; set; }
        public bool SimConnected { get; set; }

        private double _effectMultiplier = 1.0;
        public double EffectMultiplier
        {
            get => _effectMultiplier;
            set
            {
                if (_effectMultiplier != value)
                {
                    _effectMultiplier = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectMultiplier)));
                    SaveSettings();
                }
            }
        }

        private double _transXMultiplier = 1.0;
        public double TransXMultiplier
        {
            get => _transXMultiplier;
            set { if (_transXMultiplier != value) { _transXMultiplier = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TransXMultiplier))); SaveSettings(); } }
        }

        private double _transYMultiplier = 1.0;
        public double TransYMultiplier
        {
            get => _transYMultiplier;
            set { if (_transYMultiplier != value) { _transYMultiplier = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TransYMultiplier))); SaveSettings(); } }
        }

        private double _transZMultiplier = 1.0;
        public double TransZMultiplier
        {
            get => _transZMultiplier;
            set { if (_transZMultiplier != value) { _transZMultiplier = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TransZMultiplier))); SaveSettings(); } }
        }

        private double _pitchMultiplier = 1.0;
        public double PitchMultiplier
        {
            get => _pitchMultiplier;
            set { if (_pitchMultiplier != value) { _pitchMultiplier = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PitchMultiplier))); SaveSettings(); } }
        }

        private double _rollMultiplier = 1.0;
        public double RollMultiplier
        {
            get => _rollMultiplier;
            set { if (_rollMultiplier != value) { _rollMultiplier = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RollMultiplier))); SaveSettings(); } }
        }

        private double _transXMin = -1.0;
        public double TransXMin
        {
            get => _transXMin;
            set { if (_transXMin != value) { _transXMin = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TransXMin))); SaveSettings(); } }
        }

        private double _transXMax = 1.0;
        public double TransXMax
        {
            get => _transXMax;
            set { if (_transXMax != value) { _transXMax = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TransXMax))); SaveSettings(); } }
        }

        private double _transYMin = -1.0;
        public double TransYMin
        {
            get => _transYMin;
            set { if (_transYMin != value) { _transYMin = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TransYMin))); SaveSettings(); } }
        }

        private double _transYMax = 1.0;
        public double TransYMax
        {
            get => _transYMax;
            set { if (_transYMax != value) { _transYMax = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TransYMax))); SaveSettings(); } }
        }

        private double _transZMin = -1.0;
        public double TransZMin
        {
            get => _transZMin;
            set { if (_transZMin != value) { _transZMin = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TransZMin))); SaveSettings(); } }
        }

        private double _transZMax = 1.0;
        public double TransZMax
        {
            get => _transZMax;
            set { if (_transZMax != value) { _transZMax = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TransZMax))); SaveSettings(); } }
        }

        private double _pitchMin = -0.05;
        public double PitchMin
        {
            get => _pitchMin;
            set { if (_pitchMin != value) { _pitchMin = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PitchMin))); SaveSettings(); } }
        }

        private double _pitchMax = 0.05;
        public double PitchMax
        {
            get => _pitchMax;
            set { if (_pitchMax != value) { _pitchMax = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PitchMax))); SaveSettings(); } }
        }

        private double _rollMin = -0.05;
        public double RollMin
        {
            get => _rollMin;
            set { if (_rollMin != value) { _rollMin = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RollMin))); SaveSettings(); } }
        }

        private double _rollMax = 0.05;
        public double RollMax
        {
            get => _rollMax;
            set { if (_rollMax != value) { _rollMax = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RollMax))); SaveSettings(); } }
        }

        private double _washoutStrength = 0.7;
        public double WashoutStrength
        {
            get => _washoutStrength;
            set { if (_washoutStrength != value) { _washoutStrength = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WashoutStrength))); SaveSettings(); } }
        }

        private bool _simulateGravity = true;
        public bool SimulateGravity
        {
            get => _simulateGravity;
            set { if (_simulateGravity != value) { _simulateGravity = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SimulateGravity))); SaveSettings(); } }
        }

        private bool _onlyInCockpit = true;
        public bool OnlyInCockpit
        {
            get => _onlyInCockpit;
            set { if (_onlyInCockpit != value) { _onlyInCockpit = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OnlyInCockpit))); SaveSettings(); } }
        }

        private bool _lookIntoTurns = true;
        public bool LookIntoTurns
        {
            get => _lookIntoTurns;
            set { if (_lookIntoTurns != value) { _lookIntoTurns = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LookIntoTurns))); SaveSettings(); } }
        }

        private double _turnLookStrength = 1.0;
        public double TurnLookStrength
        {
            get => _turnLookStrength;
            set { if (_turnLookStrength != value) { _turnLookStrength = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TurnLookStrength))); SaveSettings(); } }
        }

        private double _turnLookMax = 25.0;
        public double TurnLookMax
        {
            get => _turnLookMax;
            set { if (_turnLookMax != value) { _turnLookMax = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TurnLookMax))); SaveSettings(); } }
        }

        private double _turnLookStart = 10.0;
        public double TurnLookStart
        {
            get => _turnLookStart;
            set { if (_turnLookStart != value) { _turnLookStart = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TurnLookStart))); SaveSettings(); } }
        }

        private double _turnLookReaction = 1.0;
        public double TurnLookReaction
        {
            get => _turnLookReaction;
            set { if (_turnLookReaction != value) { _turnLookReaction = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TurnLookReaction))); SaveSettings(); } }
        }

        private double _turnLookSpeed = 1.0;
        public double TurnLookSpeed
        {
            get => _turnLookSpeed;
            set { if (_turnLookSpeed != value) { _turnLookSpeed = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TurnLookSpeed))); SaveSettings(); } }
        }

        private double _turnLookSnap = 1.0;
        public double TurnLookSnap
        {
            get => _turnLookSnap;
            set { if (_turnLookSnap != value) { _turnLookSnap = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TurnLookSnap))); SaveSettings(); } }
        }

        private bool _minimizeOnStartup;
        public bool MinimizeOnStartup
        {
            get => _minimizeOnStartup;
            set { if (_minimizeOnStartup != value) { _minimizeOnStartup = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MinimizeOnStartup))); SaveSettings(); } }
        }

        private bool _minimizeToTray;
        public bool MinimizeToTray
        {
            get => _minimizeToTray;
            set { if (_minimizeToTray != value) { _minimizeToTray = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MinimizeToTray))); SaveSettings(); } }
        }

        private bool _exitWhenMsfsExits;
        public bool ExitWhenMsfsExits
        {
            get => _exitWhenMsfsExits;
            set { if (_exitWhenMsfsExits != value) { _exitWhenMsfsExits = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ExitWhenMsfsExits))); SaveSettings(); } }
        }

        private AppSettings settings;
        private System.Windows.Forms.NotifyIcon? notifyIcon;

        public MainWindow() : this(preview: false) { }

        // preview: build the window only (no sim connection, tray icon or registry check), for App's --snapshot.
        public MainWindow(bool preview)
        {
            InitializeComponent();
            DataContext = this;
            if (!preview) CheckRegistryStatus();

            settings = AppSettings.Load();
            _effectMultiplier = settings.EffectMultiplier;
            _transXMultiplier = settings.TransXMultiplier;
            _transYMultiplier = settings.TransYMultiplier;
            _transZMultiplier = settings.TransZMultiplier;
            _pitchMultiplier = settings.PitchMultiplier;
            _rollMultiplier = settings.RollMultiplier;
            _transXMin = settings.TransXMin;
            _transXMax = settings.TransXMax;
            _transYMin = settings.TransYMin;
            _transYMax = settings.TransYMax;
            _transZMin = settings.TransZMin;
            _transZMax = settings.TransZMax;
            _pitchMin = settings.PitchMin;
            _pitchMax = settings.PitchMax;
            _rollMin = settings.RollMin;
            _rollMax = settings.RollMax;
            _washoutStrength = settings.WashoutStrength;
            _simulateGravity = settings.SimulateGravity;
            _onlyInCockpit = settings.OnlyInCockpit;
            _lookIntoTurns = settings.LookIntoTurns;
            _turnLookStrength = settings.TurnLookStrength;
            _turnLookMax = settings.TurnLookMax;
            _turnLookStart = settings.TurnLookStart;
            _turnLookReaction = settings.TurnLookReaction;
            _turnLookSpeed = settings.TurnLookSpeed;
            _turnLookSnap = settings.TurnLookSnap;
            _minimizeOnStartup = settings.MinimizeOnStartup;
            _minimizeToTray = settings.MinimizeToTray;
            _exitWhenMsfsExits = settings.ExitWhenMsfsExits;

            physics = new PhysicsEngine();
            stopwatch = new Stopwatch();
            physicsThread = new Thread(PhysicsLoop) { IsBackground = true };
            if (preview) return;

            InitializeTrayIcon();

            if (settings.WindowTop.HasValue && settings.WindowLeft.HasValue)
            {
                double left = settings.WindowLeft.Value;
                double top = settings.WindowTop.Value;

                // Ensure the window is placed within the current virtual screen bounds
                if (left >= SystemParameters.VirtualScreenLeft &&
                    top >= SystemParameters.VirtualScreenTop &&
                    left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 50 &&
                    top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 50)
                {
                    this.Left = left;
                    this.Top = top;
                }
            }

            // Start logic in a background thread
            physicsThread.Start();

            // Update UI at 60fps
            CompositionTarget.Rendering += UpdateUI;

            this.StateChanged += MainWindow_StateChanged;
            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (MinimizeOnStartup)
            {
                this.WindowState = WindowState.Minimized;
            }
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized && MinimizeToTray)
            {
                this.Hide();
            }
        }

        private void InitializeTrayIcon()
        {
            notifyIcon = new System.Windows.Forms.NotifyIcon();
            notifyIcon.Text = "MSFS Physics Camera";
            
            try
            {
                string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                if (!string.IsNullOrEmpty(exePath))
                {
                    notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                }
                else
                {
                    notifyIcon.Icon = System.Drawing.SystemIcons.Application;
                }
            }
            catch
            {
                notifyIcon.Icon = System.Drawing.SystemIcons.Application;
            }

            notifyIcon.Visible = true;
            notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left)
                {
                    ShowMainWindow();
                }
            };

            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            var showItem = new System.Windows.Forms.ToolStripMenuItem("Show");
            showItem.Click += (s, e) => ShowMainWindow();
            var quitItem = new System.Windows.Forms.ToolStripMenuItem("Quit");
            quitItem.Click += (s, e) => this.Close();

            contextMenu.Items.Add(showItem);
            contextMenu.Items.Add(quitItem);
            notifyIcon.ContextMenuStrip = contextMenu;
        }

        private void ShowMainWindow()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
        }

        // Re-reads every bound property (the window is refreshed this way every frame while connected).
        public void NotifyAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

        private void SaveSettings()
        {
            if (settings != null)
            {
                settings.EffectMultiplier = _effectMultiplier;
                settings.TransXMultiplier = _transXMultiplier;
                settings.TransYMultiplier = _transYMultiplier;
                settings.TransZMultiplier = _transZMultiplier;
                settings.PitchMultiplier = _pitchMultiplier;
                settings.RollMultiplier = _rollMultiplier;
                settings.TransXMin = _transXMin;
                settings.TransXMax = _transXMax;
                settings.TransYMin = _transYMin;
                settings.TransYMax = _transYMax;
                settings.TransZMin = _transZMin;
                settings.TransZMax = _transZMax;
                settings.PitchMin = _pitchMin;
                settings.PitchMax = _pitchMax;
                settings.RollMin = _rollMin;
                settings.RollMax = _rollMax;
                settings.WashoutStrength = _washoutStrength;
                settings.SimulateGravity = _simulateGravity;
                settings.OnlyInCockpit = _onlyInCockpit;
                settings.LookIntoTurns = _lookIntoTurns;
                settings.TurnLookStrength = _turnLookStrength;
                settings.TurnLookMax = _turnLookMax;
                settings.TurnLookStart = _turnLookStart;
                settings.TurnLookReaction = _turnLookReaction;
                settings.TurnLookSpeed = _turnLookSpeed;
                settings.TurnLookSnap = _turnLookSnap;
                settings.MinimizeOnStartup = _minimizeOnStartup;
                settings.MinimizeToTray = _minimizeToTray;
                settings.ExitWhenMsfsExits = _exitWhenMsfsExits;
                settings.Save();
            }
        }

        private void PhysicsLoop()
        {
            bool msfsWasRunning = false;
            try
            {
                simConnect = new SimConnectHandler();
                injector = new FreeTrackInjector();
                if (settings.DiagnosticLog) telemetryLog = new TelemetryLog();
                simConnect.Connect();
                
                double timeSinceLastReconnect = 0.0;
                
                stopwatch.Start();
                lastTime = stopwatch.Elapsed.TotalSeconds;

                while (isRunning)
                {
                    double currentTime = stopwatch.Elapsed.TotalSeconds;
                    double dt = currentTime - lastTime;
                    lastTime = currentTime;

                    if (!simConnect.IsConnected)
                    {
                        telemetryLog?.Close();

                        if (msfsWasRunning && ExitWhenMsfsExits)
                        {
                            Dispatcher.Invoke(() => this.Close());
                            return;
                        }

                        timeSinceLastReconnect += dt;
                        if (timeSinceLastReconnect > 2.0)
                        {
                            timeSinceLastReconnect = 0.0;
                            Dispatcher.Invoke(() => StatusText = "Connecting to MSFS");
                            simConnect.Connect();
                        }
                    }
                    else
                    {
                        msfsWasRunning = true;
                        var data = simConnect.CurrentData;
                        var user = simConnect.CurrentUser;
                        double userDistanceM = user.HasPosition ? DistanceM(data, user.Position) : 0;
                        double speedMs = Math.Max(data.GroundVelocity * KNOTS_TO_MS,
                            Math.Sqrt(data.VelX * data.VelX + data.VelY * data.VelY + data.VelZ * data.VelZ) * FEET_TO_M);
                        double onFootTolerance = ON_FOOT_MIN_DISTANCE_M + speedMs * ON_FOOT_FRAME_SKEW_SEC;
                        onFootFarTime = user.HasPosition && userDistanceM > onFootTolerance ? onFootFarTime + dt : 0;
                        bool userOnFoot = onFootFarTime >= ON_FOOT_CONFIRM_SEC;

                        physics.Update(data, dt, EffectMultiplier, TransXMultiplier, TransYMultiplier, TransZMultiplier, PitchMultiplier, RollMultiplier, TransXMin, TransXMax, TransYMin, TransYMax, TransZMin, TransZMax, PitchMin, PitchMax, RollMin, RollMax, WashoutStrength, SimulateGravity, OnlyInCockpit, LookIntoTurns, TurnLookStrength, TurnLookMax, TurnLookStart, TurnLookReaction, TurnLookSpeed, TurnLookSnap, userOnFoot);
                        telemetryLog?.Write(dt, data, user, userDistanceM, physics);

                        // FreeTrack expects translations in millimeters and rotations in radians. 
                        // The base output was misjudged by roughly a factor of 16.
                        float ftX = (float)(physics.HeadX * 160.0);
                        float ftY = (float)(physics.HeadY * 160.0);
                        float ftZ = (float)(physics.HeadZ * 160.0);
                        float ftPitch = (float)(physics.HeadPitch * 16.0);
                        float ftRoll = (float)(physics.HeadRoll * 16.0);
                        // Yaw goes out 1:1 in radians. With the x16 used for pitch/roll, a 15-25 degree look
                        // into a turn became a 240-400 degree spin (looking backwards) in MSFS 2024.
                        // Hard limit as a safety net: never more than 60 degrees either way.
                        float ftYaw = (float)Math.Clamp(physics.HeadYaw, -MAX_YAW_RAD, MAX_YAW_RAD);

                        injector.Update(ftX, ftY, ftZ, ftPitch, ftRoll, ftYaw);
                    }

                    Thread.Sleep(16); // ~60Hz
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => StatusText = "Error: " + ex.Message);
            }
        }

        // Distance between the aircraft and the current user object (flat-earth approximation; both are
        // at most a few hundred metres apart when it matters).
        private static double DistanceM(SimConnectHandler.TelemetryData aircraft, SimConnectHandler.UserPosition user)
        {
            const double M_PER_DEG = 111320.0;
            double dLon = (user.Longitude - aircraft.Longitude + 540.0) % 360.0 - 180.0;
            double north = (user.Latitude - aircraft.Latitude) * M_PER_DEG;
            double east = dLon * M_PER_DEG * Math.Cos(aircraft.Latitude * Math.PI / 180.0);
            double up = (user.Altitude - aircraft.Altitude) * FEET_TO_M;
            return Math.Sqrt(north * north + east * east + up * up);
        }

        private void UpdateUI(object? sender, EventArgs e)
        {
            if (simConnect != null && simConnect.IsConnected)
            {
                StatusText = "Connected to MSFS";
                SimConnected = true;

                var data = simConnect.CurrentData;
                AccelX = data.AccelX;
                AccelY = data.AccelY;
                AccelZ = data.AccelZ;
                VelX = data.VelX;
                VelY = data.VelY;
                VelZ = data.VelZ;
                PlanePitch = data.Pitch * (180.0 / Math.PI); // Convert to degrees for easier reading
                PlaneBank = data.Bank * (180.0 / Math.PI); // Convert to degrees

                HeadX = physics.HeadX;
                HeadY = physics.HeadY;
                HeadZ = physics.HeadZ;
                HeadPitch = physics.HeadPitch;
                HeadRoll = physics.HeadRoll;
                HeadYawDeg = physics.HeadYaw * (180.0 / Math.PI);
                MotionStatus = !physics.InCockpit ? "paused: not in the cockpit view"
                    : physics.UserOnFoot ? "paused: you are out of the aircraft"
                    : physics.Parked ? "paused: standing still on the ground"
                    : "on";
                MotionActive = physics.InCockpit && !physics.UserOnFoot && !physics.Parked;

                // Notify UI
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
            }
            else
            {
                StatusText = "Waiting for MSFS";
                SimConnected = false;
                MotionStatus = "off";
                MotionActive = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (this.WindowState == WindowState.Normal)
            {
                settings.WindowTop = this.Top;
                settings.WindowLeft = this.Left;
            }
            else
            {
                settings.WindowTop = this.RestoreBounds.Top;
                settings.WindowLeft = this.RestoreBounds.Left;
            }
            settings.Save();

            isRunning = false;
            physicsThread?.Join(1000);
            simConnect?.Dispose();
            injector?.Dispose();
            telemetryLog?.Dispose();
            
            if (notifyIcon != null)
            {
                notifyIcon.Visible = false;
                notifyIcon.Dispose();
            }
        }

        private void RangeSliderX_TestClampRequested(object sender, double e) { if (physics != null) physics.TestOverrideX = e; }
        private void RangeSliderX_TestClampEnded(object sender, EventArgs e) { if (physics != null) physics.TestOverrideX = null; }
        private void RangeSliderY_TestClampRequested(object sender, double e) { if (physics != null) physics.TestOverrideY = e; }
        private void RangeSliderY_TestClampEnded(object sender, EventArgs e) { if (physics != null) physics.TestOverrideY = null; }
        private void RangeSliderZ_TestClampRequested(object sender, double e) { if (physics != null) physics.TestOverrideZ = e; }
        private void RangeSliderZ_TestClampEnded(object sender, EventArgs e) { if (physics != null) physics.TestOverrideZ = null; }
        private void RangeSliderPitch_TestClampRequested(object sender, double e) { if (physics != null) physics.TestOverridePitch = e; }
        private void RangeSliderPitch_TestClampEnded(object sender, EventArgs e) { if (physics != null) physics.TestOverridePitch = null; }
        private void RangeSliderRoll_TestClampRequested(object sender, double e) { if (physics != null) physics.TestOverrideRoll = e; }
        private void RangeSliderRoll_TestClampEnded(object sender, EventArgs e) { if (physics != null) physics.TestOverrideRoll = null; }

        private void CheckRegistryStatus()
        {
            // Run checks outside the UI thread to prevent blocking when reading process modules
            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool registryWasCorrect = false;
                bool fixFailed = false;

                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Software\NaturalPoint\NATURALPOINT\NPClient Location");
                    if (key != null)
                    {
                        var pathValue = key.GetValue("Path") as string;
                        if (!string.IsNullOrEmpty(pathValue))
                        {
                            string dllPath = Path.Combine(pathValue, "NPClient64.dll");
                            if (File.Exists(dllPath))
                            {
                                registryWasCorrect = true;
                            }
                        }
                    }
                }
                catch { }

                if (!registryWasCorrect)
                {
                    try
                    {
                        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\NaturalPoint\NATURALPOINT\NPClient Location"))
                        {
                            key.SetValue("Path", AppDomain.CurrentDomain.BaseDirectory);
                        }
                    }
                    catch
                    {
                        fixFailed = true;
                    }
                }

                bool isMsfsRunning = false;
                bool isNpClientLoaded = false;
                bool accessDenied = false;

                var processes = Process.GetProcessesByName("FlightSimulator")
                    .Concat(Process.GetProcessesByName("FlightSimulator2024")).ToArray();

                if (processes.Length > 0)
                {
                    isMsfsRunning = true;
                    foreach (var process in processes)
                    {
                        try
                        {
                            foreach (ProcessModule module in process.Modules)
                            {
                                if (module.ModuleName.Equals("NPClient64.dll", StringComparison.OrdinalIgnoreCase))
                                {
                                    isNpClientLoaded = true;
                                    break;
                                }
                            }
                        }
                        catch (Win32Exception)
                        {
                            accessDenied = true;
                        }
                        if (isNpClientLoaded) break;
                    }
                }

                Dispatcher.Invoke(() =>
                {
                    TrackingAccessDeniedMsg.Visibility = accessDenied ? Visibility.Visible : Visibility.Collapsed;

                    if (fixFailed)
                    {
                        TrackingStatusIcon.Fill = (Brush)FindResource("BadBrush");
                        TrackingStatusText.Text = "Head tracking does not appear to be correctly configured and we could not automatically fix it. Please check your registry permissions.";
                        TrackingRestartMsg.Visibility = Visibility.Collapsed;
                        return;
                    }

                    bool needsRestart = false;
                    if (isMsfsRunning)
                    {
                        if (isNpClientLoaded)
                        {
                            needsRestart = false;
                        }
                        else if (accessDenied)
                        {
                            // Fallback if we can't read modules due to permissions
                            needsRestart = !registryWasCorrect;
                        }
                        else
                        {
                            needsRestart = true;
                        }
                    }

                    if (needsRestart)
                    {
                        TrackingStatusIcon.Fill = (Brush)FindResource("WarnBrush");
                        TrackingStatusText.Text = registryWasCorrect
                            ? "Head tracking is configured, but MSFS hasn't loaded it yet."
                            : "Head tracking was unconfigured, but we automatically applied a fix. You should be good to go going forward.";
                        TrackingRestartMsg.Visibility = Visibility.Visible;

                        Thread monitorThread = new Thread(() =>
                        {
                            while (Process.GetProcessesByName("FlightSimulator").Any() || Process.GetProcessesByName("FlightSimulator2024").Any())
                            {
                                Thread.Sleep(2000);
                            }
                            CheckRegistryStatus();
                        }) { IsBackground = true };
                        monitorThread.Start();
                    }
                    else
                    {
                        TrackingStatusIcon.Fill = (Brush)FindResource("GoodBrush");
                        TrackingStatusText.Text = registryWasCorrect
                            ? "Head tracking seems to be configured correctly in Flight Simulator. You should be good to go."
                            : "Head tracking was unconfigured, but we automatically applied a fix. You should be good to go going forward.";
                        TrackingRestartMsg.Visibility = Visibility.Collapsed;
                    }
                });
            });
        }
    }
}
