# LOL 启动器

英雄联盟桌面启动器，**WPF + .NET Framework 4.8**，编译产物是单个 exe，双击即用，无需安装运行时。

> **关于旧版脚本**：仓库里原有的 `lol-launcher-by-*.bat`、`core/lol-launcher.ps1` 和旧 `config.json` 已删除。
> 当前实现全部在 C# 里直接完成（`ProcessStartInfo` + `Process.Start`），**不调用任何 `.bat` / `.ps1`**，
> 也不依赖旧版的 ACE 抢占逻辑。删除这些文件不影响功能。

---

## 功能

- 两种启动方式：官方登录（`Launcher\Client.exe`）、WeGame 登录
- 路径配置：自动检测 / 浏览 / 拖拽，校验 `Cross`、`Game`、`Launcher`、`LeagueClient`
- 状态面板：未配置 → 就绪 → 等待启动 → 等待登录 → 已进入大厅 → 对局中 → 已退出
- 四个进程指示灯：WeGame、登录器、大厅、对局
- 等待动画：状态灯呼吸 + 金色流光条
- 大厅启动后自动退出（可选，3 秒倒计时）
- 受限运行环境检测（提示条 + 一键以管理员身份重启）
- 便携配置：`config.json` 与 exe 同目录

---

## 快速开始

从 [Releases](https://github.com/ntmyHugh/lol-launcher/releases) 下载最新版 `LolLauncher.exe` 和 `LolLauncher.exe.config`，放进任意可写目录。

1. 双击 `LolLauncher.exe`
2. 首次启动会自动检测游戏目录；也可手动选择或拖入文件夹
3. 选择“官方登录”或“WeGame 登录”

详细说明见 [docs/使用说明.txt](docs/使用说明.txt)。

---

## 目录结构

```text
.
├── .github/workflows/build.yml   # CI：构建 + 打 tag 自动发布
├── docs/
│   └── 使用说明.txt
├── LolLauncher/                  # WPF 源码
│   ├── App.xaml / MainWindow.xaml
│   ├── ViewModels/               # 状态机与命令
│   ├── Services/                 # 路径探测、进程识别、配置、启动
│   ├── Themes/                   # 深色主题
│   ├── docs/                     # 界面截图
│   └── README.md                 # 开发说明
├── README.md
└── LICENSE
```

---

## 构建

需要 .NET SDK（能编译 `net472`）或 Visual Studio 2019：

```powershell
dotnet build LolLauncher/LolLauncher.csproj -c Release
```

产物：`LolLauncher/bin/Release/net472/LolLauncher.exe`。

## 自动构建与发布

由 GitHub Actions（[.github/workflows/build.yml](.github/workflows/build.yml)）完成：

- push 到 `main`：自动用 MSBuild 构建 `net472`，产物上传为 Actions artifact
- push `v*` 标签：自动创建 GitHub Release，附带 `LolLauncher.exe`、`LolLauncher.exe.config` 和使用说明

---

## 实现说明

| 环节 | 实现位置 | 方式 |
| :--- | :--- | :--- |
| 启动游戏 / WeGame | `Services/LaunchService.cs` | `Process.Start(new ProcessStartInfo { UseShellExecute = true, ... })` |
| 进程识别 | `Services/GameProcessWatcher.cs` | `Process.GetProcessesByName` + `MainModule.FileName` 校验路径 |
| 路径探测 | `Services/GameLocator.cs` | 进程 → 常见目录 → 限深扫描（8 秒 / 3 万目录） |
| 配置读写 | `Services/ConfigStore.cs` | 内置极简 JSON，`config.json` 与 exe 同目录 |
| 状态机 | `ViewModels/MainViewModel.cs` | 1 秒 `DispatcherTimer` 轮询 + `LaunchState` 状态迁移 |

整个程序**不依赖任何外部脚本、第三方 NuGet 包或运行时安装**。

---

## 版本

当前版本 **v2.2.0**。

## 许可证

GNU General Public License v3.0，见 [LICENSE](LICENSE)。
