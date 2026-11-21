using System;
using System.Diagnostics;

namespace LolLauncher.Services
{
    public sealed class EnvironmentInfo
    {
        public int ProcessCount { get; set; }

        public bool ShellVisible { get; set; }

        public bool Restricted { get; set; }
    }

    /// <summary>
    /// 判断当前进程是否运行在受限上下文（例如低完整性级别 / 沙箱）里：
    /// 这类环境枚举不到系统进程，会导致游戏状态全部显示为未启动。
    /// </summary>
    public static class EnvironmentProbe
    {
        private const int NormalProcessCountThreshold = 40;

        public static EnvironmentInfo Probe()
        {
            var info = new EnvironmentInfo();

            try
            {
                var all = Process.GetProcesses();
                info.ProcessCount = all.Length;
                foreach (var process in all)
                {
                    process.Dispose();
                }
            }
            catch
            {
            }

            try
            {
                var shell = Process.GetProcessesByName("explorer");
                info.ShellVisible = shell.Length > 0;
                foreach (var process in shell)
                {
                    process.Dispose();
                }
            }
            catch
            {
            }

            info.Restricted = !info.ShellVisible || info.ProcessCount < NormalProcessCountThreshold;
            return info;
        }
    }

    public static class Diagnostics
    {
        public static void WriteStartup(EnvironmentInfo info, GameProcessSnapshot snapshot)
        {
            try
            {
                var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "diagnostics.log");
                var version = typeof(Diagnostics).Assembly.GetName().Version;
                var line = string.Format(
                    "[{0:yyyy-MM-dd HH:mm:ss}] v{1} processes={2} explorer={3} restricted={4} wegame={5} login={6} lobby={7} game={8}{9}",
                    DateTime.Now,
                    version,
                    info.ProcessCount,
                    info.ShellVisible,
                    info.Restricted,
                    snapshot.WeGame,
                    snapshot.Login,
                    snapshot.Lobby,
                    snapshot.Game,
                    Environment.NewLine);

                System.IO.File.AppendAllText(path, line);
            }
            catch
            {
            }
        }
    }
}
