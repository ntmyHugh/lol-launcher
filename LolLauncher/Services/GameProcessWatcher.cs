using System;
using System.Diagnostics;
using System.IO;

namespace LolLauncher.Services
{
    public sealed class GameProcessSnapshot
    {
        public bool WeGame { get; set; }

        public bool Login { get; set; }

        public bool Lobby { get; set; }

        public bool Game { get; set; }
    }

    /// <summary>
    /// 用一次枚举同时判断 WeGame、登录器、大厅、对局四类进程。
    /// 登录器有两条链路：
    ///   · 官方登录：Launcher\Client.exe（进程名 Client）
    ///   · WeGame 登录：Riot Client 的 RiotClientServices.exe（腾讯 SSO 登录窗口）
    /// 大厅 / 对局按独特的进程名判断。
    /// </summary>
    public static class GameProcessWatcher
    {
        private static readonly string[] CandidateNames =
        {
            "WeGame",
            "Client",
            "RiotClientServices",
            "LeagueClient",
            "LeagueClientUx",
            "League of Legends"
        };

        public static GameProcessSnapshot Capture(string lolRoot)
        {
            var snapshot = new GameProcessSnapshot();

            foreach (var name in CandidateNames)
            {
                Process[] processes;
                try
                {
                    processes = Process.GetProcessesByName(name);
                }
                catch
                {
                    continue;
                }

                foreach (var process in processes)
                {
                    try
                    {
                        if (string.Equals(name, "WeGame", StringComparison.Ordinal))
                        {
                            snapshot.WeGame = true;
                        }
                        else if (string.Equals(name, "Client", StringComparison.Ordinal))
                        {
                            // Client.exe 太常见，必须确认在游戏目录下。
                            if (IsUnderRoot(TryGetPath(process), lolRoot))
                            {
                                snapshot.Login = true;
                            }
                        }
                        else if (string.Equals(name, "RiotClientServices", StringComparison.Ordinal))
                        {
                            // WeGame 登录链路：Riot Client 负责账号认证（腾讯 SSO）。
                            // 路径可读时校验是否属于本游戏目录，读不到时按名字判断。
                            var riotPath = TryGetPath(process);
                            if (riotPath == null || IsUnderRoot(riotPath, lolRoot))
                            {
                                snapshot.Login = true;
                            }
                        }
                        else if (name.StartsWith("LeagueClient", StringComparison.Ordinal)
                                 || string.Equals(name, "League of Legends", StringComparison.Ordinal))
                        {
                            // 这两个进程名足够独特，直接按名字判断，
                            // 避免因游戏装在别处（路径未更新）而漏报。
                            if (name.StartsWith("LeagueClient", StringComparison.Ordinal))
                            {
                                snapshot.Lobby = true;
                            }
                            else
                            {
                                snapshot.Game = true;
                            }
                        }
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }

            return snapshot;
        }

        private static string TryGetPath(Process process)
        {
            try
            {
                return process.MainModule != null ? process.MainModule.FileName : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsUnderRoot(string path, string root)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrWhiteSpace(root))
            {
                return false;
            }

            try
            {
                var normalizedRoot = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                return path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
