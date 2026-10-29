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
powershell -NoProfile -File .\scripts\check-windows-dev.ps1
```

自检脚本只读工具路径、Git 提交和 SDK/运行时版本；不安装依赖、不修改系统设置、不启动产品或模型，
也不读取生词本。`-Json` 输出机器可读结果；`-RequireNative` 将原生工具缺失视为失败。
Windows SDK 检查头文件和 x64 库是否存在，完整可用性仍由实际原生编译确认。调用 .NET CLI 时在当前
进程暂时关闭其遥测，结束后恢复原值，不修改用户或机器环境变量。
Python 只检查 launcher 是否存在，避免新版安装管理器自动获取解释器；原生工作前自行确认已安装的
Python 版本。脚本不改变持久化 PowerShell 执行策略。

建议把 Mac 源码取到旁边作为只读 Git 参考，不在 Windows 编译 Mac App：

```powershell
Set-Location C:\Dev
git clone --filter=blob:none --no-checkout https://github.com/ichthyoplanktonzyh/lingrove.git lingrove-mac-reference
git -C .\lingrove-mac-reference fetch origin main
git -C .\lingrove-mac-reference rev-parse origin/main
Set-Location .\lingrove-windows
```

读取 [AGENTS.md](../AGENTS.md) 和 [最新 Mac 基准](MAC_LATEST_BASELINE.md)。主 Mac 开发仓库可以有尚未
推送的提交；只读参考 clone 只能看到已发布提交。每轮先刷新其 `origin/main`，记录明确 SHA，再用
`git show SHA:路径` 阅读源码。保留旧测试的版本证据；在 Windows 不运行需要 Xcode 的 Swift verifier，
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

这些测试不等于真实宿主验收。第一轮真机检查优先使用记事本、Edge／Chrome 编辑区和一个常用 Electron
编辑器，检查中文输入法组合阶段、切换焦点时撤销旧结果、Tab 接受、部分接受、插入后的原文、带后文的光标，
再检查不同缩放下的幽灵文字位置。剪贴板恢复用自己准备的纯文本／富文本样例单独核对。生词本同时核对选择、
编辑、保存重开、图片、导出和备份。记录 Windows 版本、应用版本、屏幕缩放、复现步骤及实际像素，失败就修复，
不要把“编译通过”写成“原生行为已验证”。

普通日志位于 `%LOCALAPPDATA%\Cotabby\logs\cotabby.log`。需要详细调试时，在启动进程的当前终端设置
`$env:COTABBY_DEBUG_LOG = '1'`；详细日志会包含输入和建议文本，只使用为复现准备的材料。
调查结束在该终端删除这个变量再启动。数据默认仍在当前 Windows 用户的本地目录，不能假设与 Mac 存储兼容。

## 接手后的工作方式

Windows 作为主开发端后，让它继续当前功能分支；同一分支只由一台电脑主动修改。Mac 负责对照与回归，
源码通过 Git 同步，不搬运构建缓存。完成的一轮由主智能体写代码，GPT-6 Luna max 子智能体只做独立审查与测试；
最终提交的检查通过后直接合并 main。仍不需要每轮生成安装包、可下载程序或发布 release。

当前任务顺序保存在 [最新基准](MAC_LATEST_BASELINE.md)。新的独立资源值／投影与旧运行时数据格式分开，
参见 [资源适配边界](VOCABULARY_RESOURCES_CURRENT.md)；下一步接持久化与迁移后，继续最新版建词／导入、意义组织、
复习准备／批量界面、阅读 OCR 和表达辅助。已有源码不等于这些入口全部完成，旧音频扩展仍暂缓。
