using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LolLauncher.Services;
using LolLauncher.ViewModels;

namespace LolLauncher
{
    public partial class App : Application
    {
        private Mutex _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            AppDomain.CurrentDomain.UnhandledException += (sender, args) => WriteCrashLog(args.ExceptionObject as Exception);
            DispatcherUnhandledException += (sender, args) =>
            {
                WriteCrashLog(args.Exception);
                args.Handled = true;
                Shutdown(1);
            };

            try
            {
                var snapshotPath = GetArgument(e.Args, "--snapshot");
                var selfTestPath = GetArgument(e.Args, "--selftest");
                var restarted = e.Args.Any(a => string.Equals(a, "--restarted", StringComparison.OrdinalIgnoreCase));
                var debugMode = snapshotPath != null || selfTestPath != null;

                bool createdNew;
                _mutex = new Mutex(true, @"Local\LolLauncher.SingleInstance", out createdNew);

                // “以管理员身份重启”会先启动新实例、再关闭旧实例，这里给旧实例一点退出时间。
                if (!createdNew && restarted)
                {
                    for (var i = 0; i < 30 && !createdNew; i++)
                    {
                        Thread.Sleep(100);
                        try
                        {
                            _mutex.Dispose();
                        }
                        catch
                        {
                        }

                        _mutex = new Mutex(true, @"Local\LolLauncher.SingleInstance", out createdNew);
                    }
                }

                if (!createdNew && !debugMode)
                {
                    MessageBox.Show("LOL 启动器已经在运行了。", "LOL 启动器",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    Shutdown();
                    return;
                }

                if (selfTestPath != null)
                {
                    RunSelfTest(selfTestPath);
                    return;
                }

                if (snapshotPath != null)
                {
                    RunSnapshot(snapshotPath,
                        e.Args.Contains("--waiting", StringComparer.OrdinalIgnoreCase),
                        e.Args.Contains("--restricted", StringComparer.OrdinalIgnoreCase));
                    return;
                }

                var config = new ConfigStore().Load();
                Diagnostics.WriteStartup(EnvironmentProbe.Probe(), GameProcessWatcher.Capture(config.LolRootPath));

                var viewModel = new MainViewModel(new ConfigStore());
                var window = new MainWindow(viewModel);
                MainWindow = window;
                window.Show();            }
            catch (Exception ex)
            {
                WriteCrashLog(ex);
                Shutdown(1);
            }
        }

        private static void WriteCrashLog(Exception exception)
        {
            if (exception == null)
            {
                return;
            }

            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
                File.AppendAllText(path,
                    "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "]\r\n" + exception + "\r\n\r\n");
            }
            catch
            {
            }
        }

        private static string GetArgument(string[] args, string name)
        {
            for (var i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return i + 1 < args.Length ? args[i + 1] : string.Empty;
                }
            }

            return null;
        }

        private void RunSelfTest(string path)
        {
            var lines = new List<string>();

            try
            {
                var watch = Stopwatch.StartNew();
                var lol = GameLocator.DetectLolRoot();
                lines.Add("LoLRoot=" + (lol ?? "<null>") + " (" + watch.ElapsedMilliseconds + " ms)");

                watch.Restart();
                var weGame = GameLocator.DetectWeGame(lol);
                lines.Add("WeGame=" + (weGame ?? "<null>") + " (" + watch.ElapsedMilliseconds + " ms)");

                var snapshot = GameProcessWatcher.Capture(lol);
                lines.Add(string.Format("Process: WeGame={0} Login={1} Lobby={2} Game={3}",
                    snapshot.WeGame, snapshot.Login, snapshot.Lobby, snapshot.Game));

                var environment = EnvironmentProbe.Probe();
                lines.Add(string.Format("Processes={0} Explorer={1} Restricted={2}",
                    environment.ProcessCount, environment.ShellVisible, environment.Restricted));

                var config = new ConfigStore().Load();
                lines.Add("ConfigExitAfterLobby=" + config.ExitAfterLobby);

                lines.Add("Admin=" + Infrastructure.Ui.IsAdministrator());
                lines.Add("ConfigPath=" + new ConfigStore().ConfigPath);
            }
            catch (Exception ex)
            {
                lines.Add("ERROR: " + ex);
            }

            File.WriteAllLines(path, lines);
            Shutdown();
        }

        private void RunSnapshot(string path, bool waiting, bool restricted)
        {
            var viewModel = new MainViewModel(new ConfigStore());
            viewModel.LoadDemo(waiting);
            if (restricted)
            {
                viewModel.ShowEnvironmentWarning();
            }

            var window = new MainWindow(viewModel)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -30000,
                Top = -30000,
                ShowInTaskbar = false
            };

            window.Show();

            // 截图时跳过淡入动画，并等布局稳定后再渲染。
            window.BeginAnimation(UIElement.OpacityProperty, null);
            window.Opacity = 1;

            var captureTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle)
            {
                Interval = TimeSpan.FromMilliseconds(600)
            };

            captureTimer.Tick += (sender, args) =>
            {
                captureTimer.Stop();

                try
                {
                    RenderWindow(window, path);
                }
                finally
                {
                    window.Close();
                    Shutdown();
                }
            };

            captureTimer.Start();
        }

        private static void RenderWindow(Window window, string path)
        {
            try
            {
                var dpi = VisualTreeHelper.GetDpi(window);
                var width = (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX);
                var height = (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY);

                var bitmap = new RenderTargetBitmap(
                    width,
                    height,
                    96 * dpi.DpiScaleX,
                    96 * dpi.DpiScaleY,
                    PixelFormats.Pbgra32);

                bitmap.Render(window);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));

                using (var stream = File.Create(path))
                {
                    encoder.Save(stream);
                }
            }
            catch (Exception ex)
            {
                File.WriteAllText(path + ".error.txt", ex.ToString());
            }
        }
    }
}
