using System;
using System.Windows.Media;
using LolLauncher.Infrastructure;

namespace LolLauncher.Models
{
    /// <summary>一条界面日志。</summary>
    public sealed class LogEntry
    {
        public LogEntry(LogLevel level, string message)
        {
            Time = DateTime.Now;
            Level = level;
            Message = message ?? string.Empty;
        }

        public DateTime Time { get; }

        public LogLevel Level { get; }

        public string Message { get; }

        public string TimeText
        {
            get { return Time.ToString("HH:mm:ss"); }
        }

        public Brush LevelBrush
        {
            get
            {
                switch (Level)
                {
                    case LogLevel.Success:
                        return Ui.GetBrush(Palette.Green);
                    case LogLevel.Warn:
                        return Ui.GetBrush(Palette.Amber);
                    case LogLevel.Error:
                        return Ui.GetBrush(Palette.Red);
                    default:
                        return Ui.GetBrush(Palette.Cyan);
                }
            }
        }
    }
}
