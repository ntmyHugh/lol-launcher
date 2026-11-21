using System;
using System.Diagnostics;
using System.IO;
using LolLauncher.Native;

namespace LolLauncher.Services
{
    /// <summary>启动外部程序，并在需要时把已有窗口切到前台。</summary>
    public static class LaunchService
    {
        /// <summary>WeGame 模式的可执行文件：优先用户配置，其次游戏目录自带的启动器。</summary>
        public static string ResolveWeGame(string configuredPath, string lolRoot)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            {
                return configuredPath;
            }

            if (!string.IsNullOrWhiteSpace(lolRoot))
            {
                var stub = Path.Combine(lolRoot, "WeGameLauncher", "launcher.exe");
                if (File.Exists(stub))
                {
                    return stub;
                }
            }

            return null;
        }

        public static Process Start(string exePath, out string error)
        {
            error = null;

            try
            {
                var startInfo = new ProcessStartInfo(exePath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty
                };

                var process = Process.Start(startInfo);
                if (process == null)
                {
                    error = "系统未返回进程句柄，可能被安全软件拦截";
                }

                return process;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        /// <summary>同名进程已存在时激活其窗口；返回是否存在该进程。</summary>
        public static bool TryActivateRunning(string processName)
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        NativeMethods.ShowWindow(process.MainWindowHandle, NativeMethods.SwRestore);
                        NativeMethods.SetForegroundWindow(process.MainWindowHandle);
                    }

                    return true;
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }

            return false;
        }
    }
}
