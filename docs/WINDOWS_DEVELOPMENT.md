# Windows 开发接手

Windows 仓库：<https://github.com/ichthyoplanktonzyh/lingrove-windows>。Intel／AMD x64 机器直接沿用
当前 `win-x64` 配置；Windows 是实际运行、调试和宿主验收端，Mac 项目提供行为与界面对照。
使用同一个仓库继续开发，不新建仓库，不复制 Mac 的 `bin/`、`obj/`、`.dotnet/` 或数据库。

## 先准备工具

必需：Git for Windows、Windows x64 .NET SDK。标准 SDK 安装器包含 WPF Desktop Runtime；自定义或精简
安装缺少该运行时时再补装。SDK 必须满足仓库
`global.json`：当前要求 `10.0.402`，允许同一功能带后续补丁；例如 `10.0.403` 可以由
`latestPatch` 选择。以仓库内 `dotnet --version` 能成功解析为准，不修改 SDK pin 来绕过环境问题。
下载和安装方法见 [Microsoft 的 .NET Windows 安装说明](https://learn.microsoft.com/dotnet/core/install/windows)
和 [.NET 10 下载页](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)。

如果需要完整 IDE，使用支持 .NET 10 的 Visual Studio 2026，选择“.NET 桌面开发”。只用终端构建
WPF 时可以直接使用 SDK。[Microsoft 的版本对应表](https://learn.microsoft.com/dotnet/core/install/windows#install-with-visual-studio)
说明了 IDE 支持范围。原生 C++ helper 开发另外需要 MSVC x64、Windows SDK、CMake 和 Python 3.11+；
可在 Visual Studio Installer 添加“使用 C++ 的桌面开发”和 C++ CMake 工具。参见
[Microsoft 的 C++ 工具说明](https://learn.microsoft.com/visualstudio/install/workload-component-id-vs-build-tools)。
这部分不妨碍先编译运行 WPF 主程序。原生 helper 的精确 CI 配置仍见
[独立原生边界](SPEECH_NATIVE_HELPER.md)；新的本机工具链需要实际验证，不能直接算作 CI 同配置结果。

## 获取代码与 Mac 参考

下面的命令在普通 PowerShell 终端运行，路径可自行替换。用有仓库权限的 GitHub 账号完成 Git 认证，
不把令牌写进 URL 或提交文件。

```powershell
New-Item -ItemType Directory -Force C:\Dev | Out-Null
Set-Location C:\Dev
git clone https://github.com/ichthyoplanktonzyh/lingrove-windows.git
Set-Location .\lingrove-windows
git switch main
git pull --ff-only
powershell -NoProfile -ExecutionPolicy RemoteSigned -File .\scripts\check-windows-dev.ps1
```

自检脚本只读工具路径、Git 提交和 SDK/运行时版本；不安装依赖、不修改系统设置、不启动产品或模型，
也不读取生词本。`-Json` 输出机器可读结果；`-RequireNative` 将原生工具缺失视为失败。
Windows SDK 检查头文件和 x64 库是否存在，完整可用性仍由实际原生编译确认。调用 .NET CLI 时在当前
进程暂时关闭其遥测，结束后恢复原值，不修改用户或机器环境变量。
Python 只检查 launcher 是否存在，避免新版安装管理器自动获取解释器；原生工作前自行确认已安装的
Python 版本。上面的 `-ExecutionPolicy RemoteSigned` 只作用于该次子进程，允许执行 Git 检出后的本地脚本；
脚本本身不改变执行策略，也不修改用户或机器的持久化策略。组织组策略仍优先；受管理机器应遵循其批准的脚本运行方式。

建议把 Mac 源码取到旁边作为只读 Git 参考，不在 Windows 编译 Mac App：

```powershell
Set-Location C:\Dev
git clone --filter=blob:none --no-checkout https://github.com/ichthyoplanktonzyh/lingrove.git lingrove-mac-reference
git -C .\lingrove-mac-reference fetch --depth 1 origin 35b7fbe5c788c5524936ade1dfb7c89a3a285b04
git -C .\lingrove-mac-reference rev-parse FETCH_HEAD
Set-Location .\lingrove-windows
```

读取 [AGENTS.md](../AGENTS.md)、[固定 Mac 基准](MAC_LATEST_BASELINE.md) 和 [对齐与 UI 验收约定](WINDOWS_ALIGNMENT.md)。
本轮固定使用 `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`，不再每轮追踪 Mac 新提交。
使用 `git show SHA:路径` 阅读该快照的源码和设计依据。保留旧测试的版本证据；在 Windows 不运行需要 Xcode 的 Swift verifier，
这部分继续由 Mac CI 完成。不要把 Windows 的新 UI 建在旧数据语义上，也不要自行设计另一套产品。

## 第一轮编译与启动

```powershell
dotnet --version
dotnet restore Cotabby.sln
dotnet build Cotabby.sln --configuration Release --no-restore
dotnet run --project src/Cotabby.App/Cotabby.App.csproj --configuration Release --no-build
```

程序内部、可执行文件和数据目录仍沿用 `Cotabby`，这是明确的兼容边界。普通启动不代表全部功能已完成；
以 [PARITY.md](PARITY.md)、模块文档和当前 PR 检查为准。首次可先确认托盘、设置和生词本窗口能正常打开。
需要实际生成时，在设置中明确选择已有本地模型或配置该任务的端点；环境自检和普通 CI 不自动下载模型。
凭据在 Windows 上重新配置，不复制 Mac Keychain 或将密钥写入 Git。

有改动时运行相应窄测试，必要时按 CI 顺序验证：

```powershell
$env:COTABBY_CLIPBOARD_TEST = '0'
dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj --configuration Release --no-build --no-restore
dotnet test tests/Cotabby.Inference.Tests/Cotabby.Inference.Tests.csproj --configuration Release --no-build --no-restore --filter 'FullyQualifiedName!~ClipboardTextReaderTests'
dotnet test tests/Cotabby.App.Tests/Cotabby.App.Tests.csproj --configuration Release --no-build --no-restore
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj --configuration Release --no-build --no-restore --blame-hang-timeout 2m
```

这些测试不等于真实宿主验收。维护者现指定按 Word、Chrome、Codex 的顺序优先检查，实际进展记录于
[宿主验收记录](WINDOWS_HOST_ACCEPTANCE.md)。检查中文输入法组合阶段、切换焦点时撤销旧结果、Tab 接受、部分接受、插入后的原文、带后文的光标，
再检查不同缩放下的幽灵文字位置。剪贴板恢复用自己准备的纯文本／富文本样例单独核对。生词本同时核对选择、
编辑、保存重开、图片、导出和备份。记录 Windows 版本、应用版本、屏幕缩放、复现步骤及实际像素，失败就修复，
不要把“编译通过”写成“原生行为已验证”。

普通日志位于 `%LOCALAPPDATA%\Cotabby\logs\cotabby.log`。需要详细调试时，在启动进程的当前终端设置
`$env:COTABBY_DEBUG_LOG = '1'`；详细日志会包含输入和建议文本，只使用为复现准备的材料。
调查结束在该终端删除这个变量再启动。数据默认仍在当前 Windows 用户的本地目录，不能假设与 Mac 存储兼容。

## 接手后的工作方式

PR 管理可以使用官方 GitHub CLI。安装后在自己的浏览器完成它的独立授权：

```powershell
winget install --id GitHub.cli --exact --source winget
gh auth login --hostname github.com --git-protocol https --web
gh pr status --repo ichthyoplanktonzyh/lingrove-windows
```

Git 凭据管理器的登录与 `gh` 登录相互独立；无需把令牌写入 URL、项目文件或聊天记录。

Windows 作为主开发端后，让它继续当前功能分支；同一分支只由一台电脑主动修改。Mac 负责对照与回归，
源码通过 Git 同步，不搬运构建缓存。主智能体统筹，GPT-6.1 Terra high 子智能体按完整模块负责实现、测试和模块文档，
经过独立复核与最终提交检查后直接合并 main。功能、交互、UI 与视觉设计共同纳入验收；
仍不需要每轮生成安装包、可下载程序或发布 release。

当前任务顺序保存在 [最新基准](MAC_LATEST_BASELINE.md)。新的独立资源值／投影与旧运行时数据格式分开，
参见 [资源适配边界](VOCABULARY_RESOURCES_CURRENT.md)和[当前生产组合](VOCABULARY_CANONICAL_APP.md)。本轮使用
新的版本化文件，旧数据转换不在范围内。继续推进分类、复习准备／批量界面、阅读 OCR 和其余当前格式入口；
已有源码不等于这些入口全部完成，旧音频扩展仍暂缓。
