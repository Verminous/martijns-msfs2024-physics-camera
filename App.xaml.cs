using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using Size = System.Windows.Size;

namespace MsfsPhysicsCamera
{
    public partial class App : System.Windows.Application
    {
        private Mutex? _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            // MsfsPhysicsCamera.exe --snapshot <file.png> [--expand]: render the window with sample values to a
            // picture and exit, without connecting to the sim or showing a window. For checking the layout.
            int snapshot = Array.IndexOf(e.Args, "--snapshot");
            if (snapshot >= 0 && snapshot + 1 < e.Args.Length)
            {
                base.OnStartup(e);
                try
                {
                    RenderSnapshot(e.Args[snapshot + 1], e.Args.Contains("--expand"));
                }
                catch (Exception ex)
                {
                    File.WriteAllText(e.Args[snapshot + 1] + ".error.txt", ex.ToString());
                }
                Shutdown();
                return;
            }

            const string appName = "MsfsPhysicsCameraInstanceMutex";
            _mutex = new Mutex(true, appName, out bool createdNew);

            if (!createdNew)
            {
                // Another instance is already running
                ShowAlreadyRunningPopup();
            }
            else
            {
                base.OnStartup(e);
                MainWindow mainWindow = new MainWindow();
                mainWindow.Show();
            }
        }

        private static void RenderSnapshot(string path, bool expand)
        {
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            var window = new MainWindow(preview: true)
            {
                StatusText = "Connected to MSFS", SimConnected = true, MotionStatus = "on", MotionActive = true,
                AccelX = 3.2, AccelY = -8.4, AccelZ = 1.1, VelX = 2.0, VelY = -12.5, VelZ = 180.3, PlanePitch = 4.2, PlaneBank = -24.0,
                HeadX = -0.12, HeadY = 0.31, HeadZ = 0.05, HeadPitch = -0.012, HeadRoll = 0.021, HeadYawDeg = -14.6
            };
            window.NotifyAll();
            if (expand)
            {
                foreach (var expander in LogicalChildren<Expander>(window)) expander.IsExpanded = true;
            }

            // The scrolling content, laid out at the window's width and its full height.
            var scroll = (ScrollViewer)window.Content;
            var content = (FrameworkElement)scroll.Content;
            double width = window.Width;
            content.Measure(new Size(width, double.PositiveInfinity));
            content.Arrange(new Rect(0, 0, width, content.DesiredSize.Height));
            content.UpdateLayout();

            int w = (int)Math.Ceiling(width), h = (int)Math.Ceiling(content.DesiredSize.Height);
            var background = new DrawingVisual();
            using (var dc = background.RenderOpen())
            {
                dc.DrawRectangle((Brush)Current.FindResource("BgBrush"), null, new Rect(0, 0, w, h));
            }
            var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(background);
            bitmap.Render(content);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(path);
            encoder.Save(file);
        }

        private static System.Collections.Generic.IEnumerable<T> LogicalChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
            {
                if (child is T match) yield return match;
                foreach (var nested in LogicalChildren<T>(child)) yield return nested;
            }
        }

        private void ShowAlreadyRunningPopup()
        {
            Window window = new Window
            {
                Title = "MSFS Physics Camera",
                Width = 350,
                Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                Topmost = true,
                Style = (Style)FindResource("AppWindow")
            };

            StackPanel panel = new StackPanel
            {
                Margin = new Thickness(20),
                VerticalAlignment = VerticalAlignment.Center
            };

            TextBlock textBlock = new TextBlock
            {
                Text = "Another instance of MSFS Physics Camera is already running.",
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 20)
            };

            System.Windows.Controls.Button exitButton = new System.Windows.Controls.Button
            {
                Content = "Exit (10)",
                Width = 100,
                Height = 30,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };

            int countdown = 10;
            DispatcherTimer timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };

            exitButton.Click += (s, ev) =>
            {
                timer.Stop();
                window.Close();
                Current.Shutdown();
            };

            timer.Tick += (s, ev) =>
            {
                countdown--;
                if (countdown <= 0)
                {
                    timer.Stop();
                    window.Close();
                    Current.Shutdown();
                }
                else
                {
                    exitButton.Content = $"Exit ({countdown})";
                }
            };

            panel.Children.Add(textBlock);
            panel.Children.Add(exitButton);
            window.Content = panel;

            window.Closed += (s, ev) => Current.Shutdown();

            timer.Start();
            window.Show();
        }
    }
}
