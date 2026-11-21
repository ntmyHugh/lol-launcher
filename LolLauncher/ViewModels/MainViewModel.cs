using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LolLauncher.Infrastructure;
using LolLauncher.Models;
using LolLauncher.Services;

namespace LolLauncher.ViewModels
{
    public sealed class MainViewModel : ObservableObject, IDisposable
    {
        private const int MaxLogEntries = 150;

        private readonly ConfigStore _store;
        private readonly DispatcherTimer _timer;

        private Process _launched;
        private bool _disposed;

        private string _lolRootPath = string.Empty;
        private string _weGamePath = string.Empty;

        private bool _isLolPathValid;
        private bool _isWeGamePathValid;
        private string _lolPathHint = "尚未设置游戏目录";
        private string _weGamePathHint = "可选：留空时优先使用游戏目录内的 WeGame 启动器";
        private Brush _lolPathBrush;
        private Brush _weGamePathBrush;
        private Brush _lolHintBrush;
        private Brush _weGameHintBrush;

        private LaunchState _state = LaunchState.NotConfigured;
        private string _statusTitle = "未配置";
        private string _statusDetail = "请先设置《英雄联盟》安装目录";
        private Brush _statusBrush;

        private bool _isBusy;
        private bool _isDetecting;

        private bool _isWeGameRunning;
        private bool _isLoginRunning;
        private bool _isLobbyRunning;
        private bool _isGameRunning;
        private bool _isWaiting;
        private bool _loginLogged;

        private bool _exitAfterLobby;
        private DispatcherTimer _autoExitTimer;
        private int _autoExitSeconds;

        private string _adminText = "标准用户";
        private Brush _adminBrush;
        private bool _isRestrictedEnvironment;
        private string _environmentWarning = string.Empty;

        public MainViewModel(ConfigStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            Logs = new ObservableCollection<LogEntry>();

            _lolPathBrush = Ui.GetBrush(Palette.Dim);
            _weGamePathBrush = Ui.GetBrush(Palette.Dim);
            _lolHintBrush = Ui.GetBrush(Palette.Muted);
            _weGameHintBrush = Ui.GetBrush(Palette.Muted);
            _statusBrush = Ui.GetBrush(Palette.Amber);
            _adminBrush = Ui.GetBrush(Palette.Muted);

            BrowseLolCommand = new RelayCommand(BrowseLol, () => !IsBusy);
            BrowseWeGameCommand = new RelayCommand(BrowseWeGame, () => !IsBusy);
            DetectCommand = new RelayCommand(() => Detect(), () => !IsBusy);
            SaveCommand = new RelayCommand(() => SaveConfig(false), () => !IsBusy);
            LaunchOfficialCommand = new RelayCommand(() => StartClient(false), CanLaunch);
            LaunchWeGameCommand = new RelayCommand(() => StartClient(true), CanLaunch);
            ClearLogsCommand = new RelayCommand(() => Logs.Clear());
            OpenConfigCommand = new RelayCommand(OpenConfigFolder);
            OpenLolFolderCommand = new RelayCommand(OpenLolFolder, () => Directory.Exists(_lolRootPath));
            RestartElevatedCommand = new RelayCommand(RestartElevated, () => IsRestrictedEnvironment);

            var config = _store.Load();
            _lolRootPath = config.LolRootPath ?? string.Empty;
            _weGamePath = config.WeGamePath ?? string.Empty;
            _exitAfterLobby = config.ExitAfterLobby;

            ValidateLolPath();
            ValidateWeGamePath();

            var isAdmin = Ui.IsAdministrator();
            AdminText = isAdmin ? "管理员权限" : "标准用户";
            AdminBrush = Ui.GetBrush(isAdmin ? Palette.Gold : Palette.Muted);

            var environment = EnvironmentProbe.Probe();
            IsRestrictedEnvironment = environment.Restricted;
            EnvironmentWarning = "当前运行在受限环境中，无法枚举系统进程，游戏状态会一直显示为未启动。"
                                 + "请关闭本窗口，从“文件资源管理器”直接双击 LolLauncher.exe 运行。";
            if (environment.Restricted)
            {
                Log(LogLevel.Warn, "进程枚举受限（可见进程 " + environment.ProcessCount + " 个），游戏状态可能无法识别");
            }

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (sender, args) => RefreshRuntimeState();
            _timer.Start();

            Log(LogLevel.Info, "启动器已就绪");
            if (_isLolPathValid)
            {
                Log(LogLevel.Success, "已加载配置：" + _lolRootPath);
            }
            else
            {
                Log(LogLevel.Warn, "尚未配置游戏目录，可点击“自动检测”或直接选择路径");
            }

            SetIdleState(true);
            DetectExistingSession();
        }

        public event EventHandler CloseRequested;

        public ObservableCollection<LogEntry> Logs { get; }

        public ICommand BrowseLolCommand { get; }

        public ICommand BrowseWeGameCommand { get; }

        public ICommand DetectCommand { get; }

        public ICommand SaveCommand { get; }

        public ICommand LaunchOfficialCommand { get; }

        public ICommand LaunchWeGameCommand { get; }

        public ICommand ClearLogsCommand { get; }

        public ICommand OpenConfigCommand { get; }

        public ICommand OpenLolFolderCommand { get; }

        public ICommand RestartElevatedCommand { get; }

        public string VersionText
        {
            get { return "v2.2.1"; }
        }

        public string ConfigPathText
        {
            get { return _store.ConfigPath; }
        }

        public string LolRootPath
        {
            get { return _lolRootPath; }
            set
            {
                if (Set(ref _lolRootPath, value ?? string.Empty))
                {
                    ValidateLolPath();
                    SetIdleState(false);
                }
            }
        }

        public string WeGamePath
        {
            get { return _weGamePath; }
            set
            {
                if (Set(ref _weGamePath, value ?? string.Empty))
                {
                    ValidateWeGamePath();
                }
            }
        }

        /// <summary>大厅客户端启动后自动退出启动器。</summary>
        public bool ExitAfterLobby
        {
            get { return _exitAfterLobby; }
            set
            {
                if (!Set(ref _exitAfterLobby, value))
                {
                    return;
                }

                if (!value)
                {
                    CancelAutoExit();
                }

                SaveConfig(true);
            }
        }

        public bool IsLolPathValid
        {
            get { return _isLolPathValid; }
            private set
            {
                if (Set(ref _isLolPathValid, value))
                {
                    OnPropertyChanged(nameof(LolPathIcon));
                }
            }
        }

        public bool IsWeGamePathValid
        {
            get { return _isWeGamePathValid; }
            private set
            {
                if (Set(ref _isWeGamePathValid, value))
                {
                    OnPropertyChanged(nameof(WeGamePathIcon));
                }
            }
        }

        public string LolPathIcon
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_lolRootPath))
                {
                    return string.Empty;
                }

                return _isLolPathValid ? "✓" : "✗";
            }
        }

        public string WeGamePathIcon
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_weGamePath))
                {
                    return string.Empty;
                }

                return _isWeGamePathValid ? "✓" : "✗";
            }
        }

        public string LolPathHint
        {
            get { return _lolPathHint; }
            private set { Set(ref _lolPathHint, value); }
        }

        public string WeGamePathHint
        {
            get { return _weGamePathHint; }
            private set { Set(ref _weGamePathHint, value); }
        }

        public Brush LolPathBrush
        {
            get { return _lolPathBrush; }
            private set { Set(ref _lolPathBrush, value); }
        }

        public Brush WeGamePathBrush
        {
            get { return _weGamePathBrush; }
            private set { Set(ref _weGamePathBrush, value); }
        }

        public Brush LolHintBrush
        {
            get { return _lolHintBrush; }
            private set { Set(ref _lolHintBrush, value); }
        }

        public Brush WeGameHintBrush
        {
            get { return _weGameHintBrush; }
            private set { Set(ref _weGameHintBrush, value); }
        }

        public LaunchState State
        {
            get { return _state; }
            private set { Set(ref _state, value); }
        }

        public string StatusTitle
        {
            get { return _statusTitle; }
            private set { Set(ref _statusTitle, value); }
        }

        public string StatusDetail
        {
            get { return _statusDetail; }
            private set { Set(ref _statusDetail, value); }
        }

        public Brush StatusBrush
        {
            get { return _statusBrush; }
            private set { Set(ref _statusBrush, value); }
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            private set
            {
                if (Set(ref _isBusy, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool IsWaiting
        {
            get { return _isWaiting; }
            private set { Set(ref _isWaiting, value); }
        }

        public bool IsWeGameRunning
        {
            get { return _isWeGameRunning; }
            private set
            {
                if (Set(ref _isWeGameRunning, value))
                {
                    OnPropertyChanged(nameof(WeGameChipBrush));
                }
            }
        }

        public bool IsLoginRunning
        {
            get { return _isLoginRunning; }
            private set
            {
                if (Set(ref _isLoginRunning, value))
                {
                    OnPropertyChanged(nameof(LoginChipBrush));
                }
            }
        }

        public bool IsLobbyRunning
        {
            get { return _isLobbyRunning; }
            private set
            {
                if (Set(ref _isLobbyRunning, value))
                {
                    OnPropertyChanged(nameof(LobbyChipBrush));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool IsGameRunning
        {
            get { return _isGameRunning; }
            private set
            {
                if (Set(ref _isGameRunning, value))
                {
                    OnPropertyChanged(nameof(GameChipBrush));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public Brush WeGameChipBrush
        {
            get { return Ui.GetBrush(_isWeGameRunning ? Palette.Green : Palette.Dim); }
        }

        public Brush LoginChipBrush
        {
            get { return Ui.GetBrush(_isLoginRunning ? Palette.Green : Palette.Dim); }
        }

        public Brush LobbyChipBrush
        {
            get { return Ui.GetBrush(_isLobbyRunning ? Palette.Green : Palette.Dim); }
        }

        public Brush GameChipBrush
        {
            get { return Ui.GetBrush(_isGameRunning ? Palette.Cyan : Palette.Dim); }
        }

        public string AdminText
        {
            get { return _adminText; }
            private set { Set(ref _adminText, value); }
        }

        public Brush AdminBrush
        {
            get { return _adminBrush; }
            private set { Set(ref _adminBrush, value); }
        }

        public bool IsRestrictedEnvironment
        {
            get { return _isRestrictedEnvironment; }
            private set { Set(ref _isRestrictedEnvironment, value); }
        }

        public string EnvironmentWarning
        {
            get { return _environmentWarning; }
            private set { Set(ref _environmentWarning, value); }
        }

        /// <summary>把拖入窗口的文件夹 / WeGame.exe 应用到配置。</summary>
        public void HandleDroppedPaths(string[] paths)
        {
            if (paths == null || paths.Length == 0)
            {
                return;
            }

            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    LolRootPath = path.TrimEnd('\\', '/');
                }
                else if (File.Exists(path)
                         && string.Equals(Path.GetFileName(path), "WeGame.exe", StringComparison.OrdinalIgnoreCase))
                {
                    WeGamePath = path;
                }
            }
        }

        /// <summary>用于 --snapshot 的演示数据。</summary>
        public void LoadDemo(bool waiting)
        {
            _timer.Stop();
            IsRestrictedEnvironment = false;

            var lol = GameLocator.DetectLolRoot();
            if (!string.IsNullOrEmpty(lol))
            {
                LolRootPath = lol;
            }

            var weGame = GameLocator.DetectWeGame(_lolRootPath);
            if (!string.IsNullOrEmpty(weGame))
            {
                WeGamePath = weGame;
            }

            Logs.Clear();
            Log(LogLevel.Info, "启动器已就绪");

            if (_isLolPathValid)
            {
                Log(LogLevel.Success, "检测到游戏目录：" + _lolRootPath);
            }

            if (_isWeGamePathValid)
            {
                Log(LogLevel.Success, "检测到 WeGame：" + _weGamePath);
            }

            Log(LogLevel.Info, "提示：也可以把游戏文件夹直接拖进窗口");

            if (waiting)
            {
                ExitAfterLobby = true;
                IsLoginRunning = true;
                SetState(LaunchState.WaitingLogin, "等待登录",
                    "登录器已启动，请完成账号登录，进入大厅后会自动识别", Palette.Cyan);
            }
            else
            {
                SetIdleState(true);
            }
        }

        /// <summary>首次启动且未配置时自动探测一次。</summary>
        public void AutoDetectIfNeeded()
        {
            if (!_isLolPathValid && !_isDetecting)
            {
                Detect();
            }
        }

        /// <summary>仅用于截图：强制显示受限环境提示条。</summary>
        public void ShowEnvironmentWarning()
        {
            IsRestrictedEnvironment = true;
        }

        private void RestartElevated()
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule.FileName;
                var startInfo = new ProcessStartInfo(exePath, "--restarted")
                {
                    UseShellExecute = true,
                    Verb = "runas"
                };

                Process.Start(startInfo);

                var handler = CloseRequested;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, "以管理员身份重启失败：" + ex.Message);
            }
        }

        private void DetectExistingSession()
        {
            var snapshot = GameProcessWatcher.Capture(_lolRootPath);

            IsWeGameRunning = snapshot.WeGame;
            IsLoginRunning = snapshot.Login;
            IsLobbyRunning = snapshot.Lobby;
            IsGameRunning = snapshot.Game;

            if (snapshot.Game)
            {
                SetState(LaunchState.InGame, "对局中", "检测到《英雄联盟》对局进程", Palette.Cyan);
            }
            else if (snapshot.Lobby)
            {
                SetState(LaunchState.InLobby, "已进入大厅", "检测到已运行的大厅客户端", Palette.Green);
            }
            else if (snapshot.Login)
            {
                SetState(LaunchState.WaitingLogin, "等待登录", "检测到已运行的英雄联盟登录器，请完成登录", Palette.Cyan);
            }
        }

        private bool CanLaunch()
        {
            return _isLolPathValid && !IsBusy && !_isLobbyRunning && !_isGameRunning;
        }

        private void StartClient(bool weGameMode)
        {
            if (!_isLolPathValid)
            {
                Log(LogLevel.Warn, "游戏目录无效，无法启动");
                return;
            }

            _loginLogged = false;
            RefreshRuntimeState();

            if (_isLobbyRunning || _isGameRunning)
            {
                SetState(LaunchState.Error, "游戏已在运行", "请先关闭已打开的游戏客户端，再重新启动", Palette.Amber);
                Log(LogLevel.Warn, "检测到游戏已在运行，本次启动已取消");
                return;
            }

            string exePath;
            string displayName;

            if (weGameMode)
            {
                if (LaunchService.TryActivateRunning("WeGame"))
                {
                    SetState(LaunchState.WaitingLogin, "WeGame 已在运行",
                        "请在 WeGame 中启动《英雄联盟》，启动后会自动识别", Palette.Cyan);
                    Log(LogLevel.Info, "WeGame 已在运行，已尝试切换到前台");
                    return;
                }

                exePath = LaunchService.ResolveWeGame(_weGamePath, _lolRootPath);
                displayName = "WeGame";

                if (string.IsNullOrEmpty(exePath))
                {
                    SetState(LaunchState.Error, "未找到 WeGame",
                        "请配置 WeGame.exe 路径，或确认游戏目录包含 WeGameLauncher\\launcher.exe", Palette.Red);
                    Log(LogLevel.Error, "未找到 WeGame 可执行文件");
                    return;
                }
            }
            else
            {
                exePath = Path.Combine(_lolRootPath, "Launcher", "Client.exe");
                displayName = "官方登录器";

                if (!File.Exists(exePath))
                {
                    SetState(LaunchState.Error, "未找到登录器", "缺少 Launcher\\Client.exe，请检查游戏完整性", Palette.Red);
                    Log(LogLevel.Error, "未找到 " + exePath);
                    return;
                }
            }

            SetState(LaunchState.Launching, "正在启动", "正在启动" + displayName + "…", Palette.Cyan);
            Log(LogLevel.Info, "启动" + displayName + "：" + exePath);

            string error;
            var process = LaunchService.Start(exePath, out error);

            if (!string.IsNullOrEmpty(error))
            {
                SetState(LaunchState.Error, "启动失败", error, Palette.Red);
                Log(LogLevel.Error, "启动失败：" + error);
                return;
            }

            _launched = process;
            if (_launched != null)
            {
                try
                {
                    _launched.EnableRaisingEvents = true;
                }
                catch
                {
                }
            }

            SaveConfig(true);

            SetState(LaunchState.WaitingLogin,
                weGameMode ? "WeGame 已启动" : "等待登录",
                weGameMode ? "WeGame 已启动，正在等待《英雄联盟》启动…" : "登录器已启动，正在等待登录…",
                Palette.Cyan);

            Log(LogLevel.Success, displayName + " 已启动");
        }

        private void RefreshRuntimeState()
        {
            if (_disposed)
            {
                return;
            }

            var snapshot = GameProcessWatcher.Capture(_lolRootPath);

            IsWeGameRunning = snapshot.WeGame;
            IsLoginRunning = snapshot.Login;
            IsLobbyRunning = snapshot.Lobby;
            IsGameRunning = snapshot.Game;

            switch (_state)
            {
                case LaunchState.Launching:
                case LaunchState.WaitingLogin:
                    if (snapshot.Game)
                    {
                        SetState(LaunchState.InGame, "对局中", "《英雄联盟》对局进程已运行", Palette.Cyan);
                        Log(LogLevel.Success, "检测到对局进程");
                    }
                    else if (snapshot.Lobby)
                    {
                        SetState(LaunchState.InLobby, "已进入大厅", "大厅客户端已运行，启动完成", Palette.Green);
                        Log(LogLevel.Success, "检测到大厅客户端，启动完成");
                        ScheduleAutoExit();
                    }
                    else
                    {
                        UpdateWaitingStatus(snapshot);
                        CheckLauncherExited(snapshot);
                    }

                    break;

                case LaunchState.InLobby:
                    if (snapshot.Game)
                    {
                        CancelAutoExit();
                        SetState(LaunchState.InGame, "对局中", "《英雄联盟》客户端已运行", Palette.Cyan);
                        Log(LogLevel.Success, "检测到对局进程，祝你好运");
                    }
                    else if (!snapshot.Lobby)
                    {
                        CancelAutoExit();
                        SetState(LaunchState.Ready, "游戏已退出", "可以重新启动", Palette.Gold);
                        Log(LogLevel.Info, "大厅客户端已关闭");
                    }

                    break;

                case LaunchState.InGame:
                    if (!snapshot.Game)
                    {
                        if (snapshot.Lobby)
                        {
                            SetState(LaunchState.InLobby, "已回到大厅", "对局已结束", Palette.Green);
                        }
                        else
                        {
                            SetState(LaunchState.Ready, "游戏已退出", "可以重新启动", Palette.Gold);
                        }

                        Log(LogLevel.Info, "对局已结束");
                    }

                    break;
            }
        }

        private void UpdateWaitingStatus(GameProcessSnapshot snapshot)
        {
            if (snapshot.Login)
            {
                if (!_loginLogged)
                {
                    _loginLogged = true;
                    Log(LogLevel.Success, "检测到英雄联盟登录器");
                }

                SetState(LaunchState.WaitingLogin, "等待登录",
                    "登录器已启动，请完成账号登录，进入大厅后会自动识别", Palette.Cyan);
            }
            else if (snapshot.WeGame)
            {
                SetState(LaunchState.WaitingLogin, "等待启动",
                    "WeGame 已启动，正在等待《英雄联盟》启动…", Palette.Cyan);
            }
            else
            {
                SetState(LaunchState.WaitingLogin, "等待启动",
                    "正在等待客户端进程启动…", Palette.Cyan);
            }
        }

        private void CheckLauncherExited(GameProcessSnapshot snapshot)
        {
            if (_launched == null || IsLaunchedAlive())
            {
                return;
            }

            if (snapshot.WeGame || snapshot.Login)
            {
                return;
            }

            DisposeLaunched();
            SetState(LaunchState.Exited, "启动器已退出", "未检测到游戏客户端，可以重新启动", Palette.Muted);
            Log(LogLevel.Info, "登录器进程已退出");
        }

        private void ScheduleAutoExit()
        {
            if (!_exitAfterLobby || _autoExitTimer != null)
            {
                return;
            }

            _autoExitSeconds = 3;
            _autoExitTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _autoExitTimer.Tick += (sender, args) =>
            {
                _autoExitSeconds--;

                if (_autoExitSeconds > 0)
                {
                    StatusDetail = string.Format("大厅已启动，{0} 秒后自动退出…", _autoExitSeconds);
                    return;
                }

                CancelAutoExit();
                Log(LogLevel.Info, "大厅已启动，启动器自动退出");

                var handler = CloseRequested;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            };

            _autoExitTimer.Start();
            StatusDetail = "大厅已启动，3 秒后自动退出…";
            Log(LogLevel.Info, "大厅已启动，3 秒后自动退出（可在设置中取消勾选）");
        }

        private void CancelAutoExit()
        {
            if (_autoExitTimer == null)
            {
                return;
            }

            _autoExitTimer.Stop();
            _autoExitTimer = null;

            if (_state == LaunchState.InLobby)
            {
                StatusDetail = "大厅客户端已运行，启动完成";
            }
        }

        private void SetIdleState(bool force)
        {
            if (!force && IsActiveState(_state))
            {
                return;
            }

            if (_isLolPathValid)
            {
                SetState(LaunchState.Ready, "就绪", "已找到游戏目录，选择一种方式启动", Palette.Gold);
            }
            else
            {
                SetState(LaunchState.NotConfigured, "未配置", "请先设置《英雄联盟》安装目录（可用“自动检测”）", Palette.Amber);
            }
        }

        private static bool IsActiveState(LaunchState state)
        {
            return state == LaunchState.Launching
                   || state == LaunchState.WaitingLogin
                   || state == LaunchState.InLobby
                   || state == LaunchState.InGame;
        }

        private void SetState(LaunchState state, string title, string detail, string color)
        {
            State = state;
            StatusTitle = title;
            StatusDetail = detail;
            StatusBrush = Ui.GetBrush(color);
            IsWaiting = state == LaunchState.Launching || state == LaunchState.WaitingLogin;
            RecalculateBusy();
        }

        private void RecalculateBusy()
        {
            IsBusy = _state == LaunchState.Launching || _isDetecting;
            CommandManager.InvalidateRequerySuggested();
        }

        private async void Detect()
        {
            if (_isDetecting)
            {
                return;
            }

            _isDetecting = true;
            RecalculateBusy();
            SetState(LaunchState.Launching, "正在检测", "正在扫描已安装的英雄联盟与 WeGame…", Palette.Cyan);
            Log(LogLevel.Info, "开始自动检测…");

            var watch = Stopwatch.StartNew();
            string lol = null;
            string weGame = null;

            try
            {
                lol = await Task.Run(() => GameLocator.DetectLolRoot());
                weGame = GameLocator.DetectWeGame(lol);
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, "检测失败：" + ex.Message);
            }

            _isDetecting = false;

            if (_disposed)
            {
                return;
            }

            if (!string.IsNullOrEmpty(lol))
            {
                LolRootPath = lol;
                Log(LogLevel.Success, "检测到游戏目录：" + lol);
            }
            else
            {
                Log(LogLevel.Warn, "未检测到游戏目录，请手动选择");
            }

            if (!string.IsNullOrEmpty(weGame))
            {
                WeGamePath = weGame;
                Log(LogLevel.Success, "检测到 WeGame：" + weGame);
            }
            else
            {
                Log(LogLevel.Info, "未检测到 WeGame（不影响官方登录）");
            }

            Log(LogLevel.Info, string.Format("检测完成，用时 {0:0.0} 秒", watch.Elapsed.TotalSeconds));

            RecalculateBusy();
            SetIdleState(true);
        }

        private void ValidateLolPath()
        {
            if (string.IsNullOrWhiteSpace(_lolRootPath))
            {
                IsLolPathValid = false;
                LolPathHint = "尚未设置游戏目录";
                LolPathBrush = Ui.GetBrush(Palette.Dim);
                LolHintBrush = Ui.GetBrush(Palette.Muted);
                return;
            }

            string hint;
            var valid = GameLocator.TryValidateLolRoot(_lolRootPath, out hint);

            IsLolPathValid = valid;
            LolPathHint = hint;
            LolPathBrush = Ui.GetBrush(valid ? Palette.Green : Palette.Red);
            LolHintBrush = Ui.GetBrush(valid ? Palette.Muted : Palette.Amber);
        }

        private void ValidateWeGamePath()
        {
            if (string.IsNullOrWhiteSpace(_weGamePath))
            {
                IsWeGamePathValid = false;
                WeGamePathHint = "可选：留空时优先使用游戏目录内的 WeGame 启动器";
                WeGamePathBrush = Ui.GetBrush(Palette.Dim);
                WeGameHintBrush = Ui.GetBrush(Palette.Muted);
                return;
            }

            string hint;
            var valid = GameLocator.TryValidateWeGame(_weGamePath, out hint);

            IsWeGamePathValid = valid;
            WeGamePathHint = hint;
            WeGamePathBrush = Ui.GetBrush(valid ? Palette.Green : Palette.Red);
            WeGameHintBrush = Ui.GetBrush(valid ? Palette.Muted : Palette.Amber);
        }

        private void BrowseLol()
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "选择《英雄联盟》安装根目录（应包含 Game、LeagueClient 等子目录）";
                dialog.ShowNewFolderButton = false;

                if (!string.IsNullOrWhiteSpace(_lolRootPath) && Directory.Exists(_lolRootPath))
                {
                    dialog.SelectedPath = _lolRootPath;
                }

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    LolRootPath = dialog.SelectedPath;
                }
            }
        }

        private void BrowseWeGame()
        {
            using (var dialog = new System.Windows.Forms.OpenFileDialog())
            {
                dialog.Title = "选择 WeGame.exe";
                dialog.Filter = "WeGame (WeGame.exe)|WeGame.exe|可执行文件 (*.exe)|*.exe";
                dialog.CheckFileExists = true;

                if (!string.IsNullOrWhiteSpace(_weGamePath) && File.Exists(_weGamePath))
                {
                    dialog.InitialDirectory = Path.GetDirectoryName(_weGamePath);
                }

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    WeGamePath = dialog.FileName;
                }
            }
        }

        private bool SaveConfig(bool silent)
        {
            try
            {
                _store.Save(new AppConfig
                {
                    LolRootPath = _lolRootPath,
                    WeGamePath = _weGamePath,
                    ExitAfterLobby = _exitAfterLobby
                });

                if (!silent)
                {
                    Log(LogLevel.Success, "配置已保存：" + _store.ConfigPath);
                }

                return true;
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, "保存配置失败：" + ex.Message);
                return false;
            }
        }

        private void OpenConfigFolder()
        {
            try
            {
                if (File.Exists(_store.ConfigPath))
                {
                    Process.Start("explorer.exe", "/select,\"" + _store.ConfigPath + "\"");
                }
                else
                {
                    Process.Start("explorer.exe", "\"" + Path.GetDirectoryName(_store.ConfigPath) + "\"");
                }
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, "打开配置目录失败：" + ex.Message);
            }
        }

        private void OpenLolFolder()
        {
            try
            {
                if (Directory.Exists(_lolRootPath))
                {
                    Process.Start("explorer.exe", "\"" + _lolRootPath + "\"");
                }
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, "打开游戏目录失败：" + ex.Message);
            }
        }

        private bool IsLaunchedAlive()
        {
            try
            {
                return _launched != null && !_launched.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private void DisposeLaunched()
        {
            if (_launched == null)
            {
                return;
            }

            try
            {
                _launched.Dispose();
            }
            catch
            {
            }

            _launched = null;
        }

        private void Log(LogLevel level, string message)
        {
            if (Logs.Count >= MaxLogEntries)
            {
                Logs.RemoveAt(0);
            }

            Logs.Add(new LogEntry(level, message));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timer.Stop();
            CancelAutoExit();
            DisposeLaunched();

            // 关闭窗口时静默保存当前路径，避免用户重新填写。
            try
            {
                SaveConfig(true);
            }
            catch
            {
            }
        }
    }
}
