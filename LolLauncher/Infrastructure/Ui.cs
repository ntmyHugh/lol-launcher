using System;
using System.Collections.Generic;
using System.Security.Principal;
using System.Windows.Media;

namespace LolLauncher.Infrastructure
{
    /// <summary>界面配色常量。</summary>
    public static class Palette
    {
        public const string Gold = "#C8AA6E";
        public const string Cyan = "#0AC8B9";
        public const string Green = "#3ED598";
        public const string Amber = "#F2B34B";
        public const string Red = "#FF5F6D";
        public const string Muted = "#6B7896";
        public const string Dim = "#3A4560";
    }

    /// <summary>刷子缓存：把十六进制颜色转换成冻结的 SolidColorBrush，供 VM 直接绑定。</summary>
    public static class Ui
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, SolidColorBrush> Cache =
            new Dictionary<string, SolidColorBrush>(StringComparer.OrdinalIgnoreCase);

        public static Brush GetBrush(string hex)
        {
            if (string.IsNullOrEmpty(hex))
            {
                hex = Palette.Muted;
            }

            lock (Sync)
            {
                if (Cache.TryGetValue(hex, out var cached))
                {
                    return cached;
                }

                var color = (Color)ColorConverter.ConvertFromString(hex);
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                Cache[hex] = brush;
                return brush;
            }
        }

        public static bool IsAdministrator()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    var principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
