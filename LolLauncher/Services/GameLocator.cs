using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace LolLauncher.Services
{
    /// <summary>负责校验与自动探测《英雄联盟》/ WeGame 的安装位置。</summary>
    public static class GameLocator
    {
        private static readonly string[] RequiredDirs = { "Cross", "Game", "Launcher", "LeagueClient" };

        private static readonly HashSet<string> SkipNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Windows",
            "$Recycle.Bin",
            "System Volume Information",
            "Recovery",
            "PerfLogs",
            "MSOCache",
            "Config.Msi",
            "node_modules",
            ".git",
            "AppData",
            "ProgramData",
            "$WinREAgent",
            "Windows.old",
            "Documents and Settings",
            "OneDriveTemp",
            "System32",
            "Temp",
            "Cache"
        };

        public static bool TryValidateLolRoot(string path, out string hint)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                hint = "尚未设置游戏目录";
                return false;
            }

            try
            {
                var normalized = path.Trim().TrimEnd('\\', '/');

                if (!Directory.Exists(normalized))
                {
                    hint = "目录不存在";
                    return false;
                }

                foreach (var dir in RequiredDirs)
                {
                    if (!Directory.Exists(Path.Combine(normalized, dir)))
                    {
                        hint = "缺少 " + dir + " 子目录，可能不是游戏根目录";
                        return false;
                    }
                }

                if (!File.Exists(Path.Combine(normalized, "Launcher", "Client.exe")))
                {
                    hint = "未找到 Launcher\\Client.exe";
                    return false;
                }

                hint = "路径有效，已找到游戏文件";
                return true;
            }
            catch (Exception ex)
            {
                hint = "校验失败：" + ex.Message;
                return false;
            }
        }

        public static bool TryValidateWeGame(string path, out string hint)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                hint = "可选：留空时优先使用游戏目录内的 WeGame 启动器";
                return false;
            }

            try
            {
                if (!File.Exists(path.Trim()))
                {
                    hint = "文件不存在";
                    return false;
                }

                hint = "路径有效，已找到 WeGame";
                return true;
            }
            catch (Exception ex)
            {
                hint = "校验失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>按“运行中进程 → 常见目录 → 限深扫描”的顺序定位游戏根目录。</summary>
        public static string DetectLolRoot()
        {
            foreach (var processName in new[] { "LeagueClient", "League of Legends" })
            {
                foreach (var process in SafeGetProcesses(processName))
                {
                    try
                    {
                        var exe = process.MainModule.FileName;
                        var root = FindLolRootAbove(exe);
                        if (root != null)
                        {
                            return root;
                        }
                    }
                    catch
                    {
                        // 进程权限不足时忽略。
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }

            foreach (var candidate in CommonLolCandidates())
            {
                if (TryValidateLolRoot(candidate, out _))
                {
                    return candidate.TrimEnd('\\');
                }
            }

            return ScanForLolRoot();
        }

        public static string DetectWeGame(string lolRoot)
        {
            foreach (var process in SafeGetProcesses("WeGame"))
            {
                try
                {
                    var exe = process.MainModule.FileName;
                    if (File.Exists(exe))
                    {
                        return exe;
                    }
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }

            var fromRegistry = DetectWeGameFromRegistry();
            if (fromRegistry != null)
            {
                return fromRegistry;
            }

            foreach (var drive in DriveRoots())
            {
                var relatives = new[]
                {
                    @"Tencent\WeGame\WeGame.exe",
                    @"Program Files (x86)\Tencent\WeGame\WeGame.exe",
                    @"Program Files\Tencent\WeGame\WeGame.exe",
                    @"WeGame\WeGame.exe"
                };

                foreach (var relative in relatives)
                {
                    var candidate = Path.Combine(drive, relative);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(lolRoot))
            {
                var dir = lolRoot.TrimEnd('\\');
                for (var i = 0; i < 3; i++)
                {
                    dir = Path.GetDirectoryName(dir);
                    if (string.IsNullOrEmpty(dir))
                    {
                        break;
                    }

                    var direct = Path.Combine(dir, "WeGame.exe");
                    if (File.Exists(direct))
                    {
                        return direct;
                    }

                    var nested = Path.Combine(dir, "WeGame", "WeGame.exe");
                    if (File.Exists(nested))
                    {
                        return nested;
                    }
                }
            }

            return null;
        }

        private static string FindLolRootAbove(string exePath)
        {
            var dir = Path.GetDirectoryName(exePath);

            for (var i = 0; i < 3 && !string.IsNullOrEmpty(dir); i++)
            {
                if (TryValidateLolRoot(dir, out _))
                {
                    return dir.TrimEnd('\\');
                }

                dir = Path.GetDirectoryName(dir);
            }

            return null;
        }

        private static IEnumerable<string> CommonLolCandidates()
        {
            foreach (var drive in DriveRoots())
            {
                yield return Path.Combine(drive, @"WeGameApps\League of Legends");
                yield return Path.Combine(drive, @"Tencent\WeGameApps\League of Legends");
                yield return Path.Combine(drive, @"Games\League of Legends");
                yield return Path.Combine(drive, @"Tencent\Games\League of Legends");
                yield return Path.Combine(drive, @"Program Files\Tencent\WeGameApps\League of Legends");
                yield return Path.Combine(drive, @"Program Files (x86)\Tencent\WeGameApps\League of Legends");
            }
        }

        /// <summary>限深、限时的广度优先扫描，避免遍历整块磁盘。</summary>
        private static string ScanForLolRoot()
        {
            var watch = Stopwatch.StartNew();
            var drives = DriveRoots()
                .OrderBy(drive => string.Equals(drive, SystemDriveRoot(), StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ToList();

            foreach (var drive in drives)
            {
                var queue = new Queue<KeyValuePair<string, int>>();
                queue.Enqueue(new KeyValuePair<string, int>(drive, 0));
                var visited = 0;

                while (queue.Count > 0)
                {
                    if (watch.Elapsed > TimeSpan.FromSeconds(8) || visited > 30000)
                    {
                        return null;
                    }

                    var current = queue.Dequeue();
                    visited++;

                    string[] children;
                    try
                    {
                        children = Directory.GetDirectories(current.Key);
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (var child in children)
                    {
                        var name = Path.GetFileName(child);
                        if (string.IsNullOrEmpty(name) || SkipNames.Contains(name))
                        {
                            continue;
                        }

                        if (LooksLikeLolFolder(name) && TryValidateLolRoot(child, out _))
                        {
                            return child;
                        }

                        if (current.Value + 1 < 4)
                        {
                            queue.Enqueue(new KeyValuePair<string, int>(child, current.Value + 1));
                        }
                    }
                }
            }

            return null;
        }

        private static bool LooksLikeLolFolder(string name)
        {
            return name.IndexOf("league", StringComparison.OrdinalIgnoreCase) >= 0
                   || name.IndexOf("英雄联盟", StringComparison.Ordinal) >= 0
                   || string.Equals(name, "LOL", StringComparison.OrdinalIgnoreCase);
        }

        private static string DetectWeGameFromRegistry()
        {
            var views = new[] { RegistryView.Registry64, RegistryView.Registry32 };
            var hives = new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser };

            foreach (var view in views)
            {
                foreach (var hive in hives)
                {
                    try
                    {
                        using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                        using (var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                        {
                            if (uninstall == null)
                            {
                                continue;
                            }

                            foreach (var subName in uninstall.GetSubKeyNames())
                            {
                                using (var sub = uninstall.OpenSubKey(subName))
                                {
                                    if (sub == null)
                                    {
                                        continue;
                                    }

                                    var displayName = sub.GetValue("DisplayName") as string;
                                    if (string.IsNullOrEmpty(displayName)
                                        || displayName.IndexOf("WeGame", StringComparison.OrdinalIgnoreCase) < 0)
                                    {
                                        continue;
                                    }

                                    var icon = CleanIconPath(sub.GetValue("DisplayIcon") as string);
                                    if (icon != null && File.Exists(icon))
                                    {
                                        return icon;
                                    }

                                    var location = sub.GetValue("InstallLocation") as string;
                                    if (!string.IsNullOrWhiteSpace(location))
                                    {
                                        var exe = Path.Combine(location, "WeGame.exe");
                                        if (File.Exists(exe))
                                        {
                                            return exe;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return null;
        }

        private static string CleanIconPath(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            var path = raw.Trim().Trim('"');
            var comma = path.LastIndexOf(',');
            if (comma > 2)
            {
                path = path.Substring(0, comma).Trim().Trim('"');
            }

            return path;
        }

        private static IEnumerable<string> DriveRoots()
        {
            DriveInfo[] drives;
            try
            {
                drives = DriveInfo.GetDrives();
            }
            catch
            {
                yield break;
            }

            foreach (var drive in drives)
            {
                var ok = false;
                try
                {
                    ok = drive.IsReady && drive.DriveType == DriveType.Fixed;
                }
                catch
                {
                }

                if (ok)
                {
                    yield return drive.RootDirectory.FullName;
                }
            }
        }

        private static string SystemDriveRoot()
        {
            try
            {
                return Path.GetPathRoot(Environment.SystemDirectory);
            }
            catch
            {
                return @"C:\";
            }
        }

        private static Process[] SafeGetProcesses(string name)
        {
            try
            {
                return Process.GetProcessesByName(name);
            }
            catch
            {
                return new Process[0];
            }
        }
    }
}
