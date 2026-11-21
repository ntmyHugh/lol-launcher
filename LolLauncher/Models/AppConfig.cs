namespace LolLauncher.Models
{
    /// <summary>持久化到 config.json 的设置。</summary>
    public sealed class AppConfig
    {
        public string LolRootPath { get; set; }

        public string WeGamePath { get; set; }

        /// <summary>大厅客户端启动后自动退出启动器。</summary>
        public bool ExitAfterLobby { get; set; }
    }
}
