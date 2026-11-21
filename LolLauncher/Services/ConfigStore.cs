using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using LolLauncher.Models;

namespace LolLauncher.Services
{
    /// <summary>
    /// 便携式配置存储：config.json 与 exe 同目录，可直接拷贝整个文件夹迁移。
    /// 兼容旧版启动器（lol-launcher-v1.x）的 LOL_ROOT_PATH 字段。
    /// 使用内置的极简 JSON 读写，避免引入 System.Web 依赖。
    /// </summary>
    public sealed class ConfigStore
    {
        private const string PatternTemplate = @"""{0}""\s*:\s*""((?:\\.|[^""\\])*)""";

        public ConfigStore()
        {
            ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        }

        public string ConfigPath { get; }

        public AppConfig Load()
        {
            var config = new AppConfig();

            try
            {
                if (!File.Exists(ConfigPath))
                {
                    return config;
                }

                var json = File.ReadAllText(ConfigPath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return config;
                }

                config.LolRootPath = ReadString(json, "lolRootPath") ?? ReadString(json, "LOL_ROOT_PATH");
                config.WeGamePath = ReadString(json, "weGamePath") ?? ReadString(json, "WEGAME_PATH");
                config.ExitAfterLobby = ReadBool(json, "exitAfterLobby", false);
            }
            catch
            {
                // 配置损坏时按“未配置”处理，不阻塞启动。
            }

            return config;
        }

        public void Save(AppConfig config)
        {
            var json = new StringBuilder();
            json.Append("{\n");
            json.Append("    \"lolRootPath\": ").Append(Quote(config.LolRootPath ?? string.Empty)).Append(",\n");
            json.Append("    \"weGamePath\": ").Append(Quote(config.WeGamePath ?? string.Empty)).Append(",\n");
            json.Append("    \"exitAfterLobby\": ").Append(config.ExitAfterLobby ? "true" : "false").Append('\n');
            json.Append("}\n");

            var tempPath = ConfigPath + ".tmp";
            File.WriteAllText(tempPath, json.ToString(), new UTF8Encoding(false));

            if (File.Exists(ConfigPath))
            {
                File.Delete(ConfigPath);
            }

            File.Move(tempPath, ConfigPath);
        }

        private static string ReadString(string json, string key)
        {
            var pattern = string.Format(PatternTemplate, Regex.Escape(key));
            var match = Regex.Match(json, pattern, RegexOptions.IgnoreCase);
            return match.Success ? Unescape(match.Groups[1].Value) : null;
        }

        private static bool ReadBool(string json, string key, bool fallback)
        {
            var pattern = string.Format(@"""{0}""\s*:\s*(true|false)", Regex.Escape(key));
            var match = Regex.Match(json, pattern, RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return fallback;
            }

            return string.Equals(match.Groups[1].Value, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static string Quote(string value)
        {
            var builder = new StringBuilder(value.Length + 8);
            builder.Append('"');

            foreach (var c in value)
            {
                switch (c)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (c < 0x20)
                        {
                            builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }

                        break;
                }
            }

            builder.Append('"');
            return builder.ToString();
        }

        private static string Unescape(string value)
        {
            if (value.IndexOf('\\') < 0)
            {
                return value;
            }

            var builder = new StringBuilder(value.Length);

            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c != '\\' || i == value.Length - 1)
                {
                    builder.Append(c);
                    continue;
                }

                var next = value[++i];
                switch (next)
                {
                    case '"':
                        builder.Append('"');
                        break;
                    case '\\':
                        builder.Append('\\');
                        break;
                    case '/':
                        builder.Append('/');
                        break;
                    case 'b':
                        builder.Append('\b');
                        break;
                    case 'f':
                        builder.Append('\f');
                        break;
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'u':
                        int code;
                        if (i + 4 < value.Length
                            && int.TryParse(value.Substring(i + 1, 4), NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture, out code))
                        {
                            builder.Append((char)code);
                            i += 4;
                        }
                        else
                        {
                            builder.Append("\\u");
                        }

                        break;
                    default:
                        builder.Append('\\').Append(next);
                        break;
                }
            }

            return builder.ToString();
        }
    }
}
