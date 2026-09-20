using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace MsfsPhysicsCamera
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private SimConnectHandler? simConnect;
        private FreeTrackInjector? injector;
        private PhysicsEngine physics;
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

        private double _washoutStrength = 1.0;
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

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            CheckRegistryStatus();

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
            _washoutStrength = settings.WashoutStrength;
            _simulateGravity = settings.SimulateGravity;
            _onlyInCockpit = settings.OnlyInCockpit;
            _minimizeOnStartup = settings.MinimizeOnStartup;
            _minimizeToTray = settings.MinimizeToTray;
            _exitWhenMsfsExits = settings.ExitWhenMsfsExits;

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

            physics = new PhysicsEngine();
            stopwatch = new Stopwatch();

            // Start logic in a background thread
            physicsThread = new Thread(PhysicsLoop) { IsBackground = true };
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
                settings.WashoutStrength = _washoutStrength;
                settings.SimulateGravity = _simulateGravity;
                settings.OnlyInCockpit = _onlyInCockpit;
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
                        if (msfsWasRunning && ExitWhenMsfsExits)
                        {
                            Dispatcher.Invoke(() => this.Close());
                            return;
                        }

                        timeSinceLastReconnect += dt;
                        if (timeSinceLastReconnect > 2.0)
                        {
                            timeSinceLastReconnect = 0.0;
                            Dispatcher.Invoke(() => StatusText = "Attempting to connect to MSFS...");
                            simConnect.Connect();
                        }
                    }
                    else
                    {
                        msfsWasRunning = true;
                        var data = simConnect.CurrentData;
                        physics.Update(data, dt, EffectMultiplier, TransXMultiplier, TransYMultiplier, TransZMultiplier, PitchMultiplier, RollMultiplier, TransXMin, TransXMax, TransYMin, TransYMax, TransZMin, TransZMax, WashoutStrength, SimulateGravity, OnlyInCockpit);

                        // FreeTrack expects translations in millimeters and rotations in radians. 
                        // The base output was misjudged by roughly a factor of 16.
                        float ftX = (float)(physics.HeadX * 160.0);
                        float ftY = (float)(physics.HeadY * 160.0);
                        float ftZ = (float)(physics.HeadZ * 160.0);
                        float ftPitch = (float)(physics.HeadPitch * 16.0);
                        float ftRoll = (float)(physics.HeadRoll * 16.0);
                        float ftYaw = 0f;

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

        private void UpdateUI(object? sender, EventArgs e)
        {
            if (simConnect != null && simConnect.IsConnected)
            {
                StatusText = "Connected to MSFS SimConnect";
                
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

                // Notify UI
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
            }
            else
            {
                StatusText = "Waiting for MSFS connection...";
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
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
                        TrackingStatusIcon.Fill = new SolidColorBrush(Colors.Red);
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
                        TrackingStatusIcon.Fill = new SolidColorBrush(Colors.Orange);
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
                        TrackingStatusIcon.Fill = new SolidColorBrush(Colors.Green);
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
