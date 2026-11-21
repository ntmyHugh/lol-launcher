namespace LolLauncher.Models
{
    /// <summary>启动器所处的阶段。</summary>
    public enum LaunchState
    {
        NotConfigured,
        Ready,
        Launching,
        WaitingLogin,
        InLobby,
        InGame,
        Exited,
        Error
    }
}
