# DayTodo 日清单

一个 Windows 桌面待办程序：按天显示任务、可查历史、未完成的任务第二天自动续过来。

- 技术栈：C# / WPF / .NET 8（`net8.0-windows`），自包含发布，目标机器**不需要安装 .NET**
- 目标系统：Windows 10 / 11，64 位（Win10/11 x64 需在你的目标机器上实测）
- 权限：普通用户权限，安装与运行都不弹 UAC
- 主题：固定浅色

## 功能

| 功能 | 行为 |
|---|---|
| 按天显示 | 左侧日历点选任意日期，或顶部前后箭头翻天 |
| 查历史 | 历史日显示当天出现过的全部任务，含「当日未完成」标记 |
| 自动顺延 | 启动和跨零点时，未完成任务推进到今天，累计顺延天数 |
| 顺延标记 | 橙色「顺延 N 天」，满 3 天变红；历史日显示「未完成 · 已顺延至今天」 |
| 历史只读 | 选中今天之前的日期时，不能新增、勾选、删除 |
| 删除 | 硬删除，点「删除」立即移除，不进回收站、不可恢复 |
| 标签 / 优先级 | 标签逗号分隔最多 4 个；高 / 中 / 低三级，列表按未完成优先、再按优先级排序 |
| 托盘常驻 | 关闭窗口只隐藏到托盘，双击托盘图标恢复；右键可退出 / 开关开机自启 |
| 开机自启 | 写 `HKCU\...\Run`，带 `--minimized` 参数，启动后直接进托盘 |
| 单实例 | 第二次启动时激活已有窗口，不重复开进程 |

## 目录结构

```
D:\workbuddy\todo\
├─ src\DayTodo\
│  ├─ DayTodo.csproj          项目配置（WPF + WinForms 托盘，图标 / 清单）
│  ├─ app.manifest            DPI 感知、asInvoker、Win10/11 兼容声明
│  ├─ App.xaml / .cs          启动、单实例、托盘、全局异常处理
│  ├─ MainWindow.xaml / .cs  主窗口、标题栏、跨零点定时器
│  ├─ Assets\app.ico          exe / 安装包图标（16/32/48/256 四档）
│  ├─ Assets\tray.png         托盘图标（内嵌资源）
│  ├─ Themes\Styles.xaml      浅色画刷与控件样式
│  ├─ Models\Entities.cs      TaskItem / AppData
│  ├─ Services\Services.cs    JSON 存储、顺延引擎、开机自启
│  └─ ViewModels\ViewModel.cs MVVM 视图模型与命令
├─ build\
│  ├─ make-icon.ps1          生成多尺寸 ico + 托盘 png
│  ├─ publish.ps1            一键编译 / 发布 / 打安装包
│  └─ installer.iss          Inno Setup 6 脚本（当前用户安装）
├─ dist\
│  ├─ portable\DayTodo.exe   单文件便携版
│  └─ installer\DayTodo-Setup-1.0.2.exe
└─ README.md
```

## 数据

- 主数据：`%APPDATA%\DayTodo\tasks.json`（UTF-8，中文不转义，可直接用记事本看）
- 备份：同目录 `backups\tasks-yyyyMMdd-HHmm.json`，自动保留最近 7 份
- 写入方式：先写 `.tmp` 再原子替换，断电不会写坏主文件
- 文件损坏时：自动改名为 `tasks.corrupt-*.json`，并从最新备份恢复
- **卸载程序不会删除任务数据**；需要彻底清除请手动删 `%APPDATA%\DayTodo`

### 任务字段与顺延规则

一条任务永远只有一条记录，不按天复制：

| 字段 | 含义 |
|---|---|
| `DateOrigin` | 首次排在哪天，永不改变 |
| `DateCurrent` | 当前排期，顺延后变成今天 |
| `DateCompleted` | 完成日期，`null` = 未完成 |
| `PostponeDays` | 累计顺延天数 |

某天是否显示这条任务：`DateOrigin ≤ 该天 ≤ DateCompleted`（未完成时右边界为今天）。
扫描到 `DateCurrent < 今天` 且未完成的任务时，`PostponeDays += 相差天数`，`DateCurrent = 今天`。

## 开发运行

需要 .NET 8 SDK（只需装在开发机）。若本机没有，可装到用户目录、不污染系统：

```powershell
Invoke-WebRequest "https://dot.net/v1/dotnet-install.ps1" -OutFile "$env:TEMP\dotnet-install.ps1"
& "$env:TEMP\dotnet-install.ps1" -Channel 8.0 -InstallDir "$env:LOCALAPPDATA\Microsoft\dotnet" -NoPath
```

如果 SDK 下载太慢，可以手动下载免安装压缩包（推荐用浏览器或下载工具）：

- 官方下载页：`https://dotnet.microsoft.com/download/dotnet/8.0` → SDK 8.0.x → Windows x64
- 直链示例：`https://builds.dotnet.microsoft.com/dotnet/Sdk/8.0.419/dotnet-sdk-8.0.419-win-x64.zip`
- 放到 `D:\workbuddy\todo\tools\`：**zip 文件**（`dotnet-sdk-*-win-x64.zip`）或**已解压的文件夹**（`dotnet-sdk-*-win-x64\`）都认，版本号不限

`build\publish.ps1` 按「用户级安装 → tools 下已解压的 SDK → PATH → tools 下的 zip（按需解压）」自动查找，找到哪个用哪个。也可以双击安装官方 exe 安装器（会写入 PATH），脚本会自动使用。本机实测用的是 8.0.428。

调试运行：

```powershell
cd D:\workbuddy\todo
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" run --project src\DayTodo\DayTodo.csproj
```

## 打包便携版

```powershell
cd D:\workbuddy\todo
powershell -ExecutionPolicy Bypass -File build\publish.ps1
```

等价的手工命令：

```powershell
dotnet publish src\DayTodo\DayTodo.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=none -p:DebugSymbols=false -o dist\portable
```

产物：`dist\portable\DayTodo.exe`，单文件，双击即用，拷到 U 盘也能跑。

资源嵌入说明：`app.ico` 与 `tray.png` 在 csproj 里以 `Resource` 嵌入程序集，托盘图标通过 `pack://application:,,,/Assets/tray.png` 读取，不依赖任何外部文件；exe 图标由 `ApplicationIcon` 写入 PE 资源。因此单文件 exe 单独复制走也能正常显示图标。

## 制作安装包

先安装 [Inno Setup 6](https://jrsoftware.org/isdl.php)，然后：

```powershell
powershell -ExecutionPolicy Bypass -File build\publish.ps1 -Installer
```

或直接编译脚本：

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" build\installer.iss
```

`installer.iss` 已保存为 UTF-8 BOM（Inno Setup 6 正确读取中文向导文本），关键点：

- `PrivilegesRequired=lowest` + `DefaultDirName={localappdata}\DayTodo`：全程不弹 UAC
- `[Icons]`：开始菜单两项（主程序 / 卸载）、桌面快捷方式（安装时可勾选）
- `[Registry]`：可勾选开机自启，卸载时自动清掉 Run 键
- `[Run]`：安装完成后可选「立即启动 DayTodo」
- 卸载入口由 Inno Setup 自动生成，并出现在「设置 → 应用」列表
- 卸载**保留** `%APPDATA%\DayTodo` 的任务数据

## 测试与排错

自动启动自检（`--verify` 会**真实显示主窗口**，模拟输入一条任务并按回车，确认任务真的写入，随后自动回滚数据文件，不会改动你的清单）：

```powershell
Remove-Item "$env:TEMP\DayTodo-verify.ok" -Force -ErrorAction SilentlyContinue
Start-Process "dist\portable\DayTodo.exe" -ArgumentList "--verify" -Wait
Get-Content "$env:TEMP\DayTodo-verify.ok"
```

报告里应出现 `CanAdd=True`、`AddButtonEnabled=True`、`EnterHandled=True`，且 `TasksAfterEnter` 比运行前多 1；若启动期抛异常（弹「启动失败」），标记文件不会生成。

版本记录：1.0.1 修掉启动期绑定异常（`ProgressBar.Value` 默认双向绑定，绑定到只读计算属性 `Progress` 时 WPF 尝试回写并抛异常，已改为 `Mode=OneWay`）；1.0.2 重做添加栏（带边框的长输入框、框内提示文字、回车与按钮双通道），并把自检扩展为端到端交互测试。

测试清单：

1. 首次启动 → 添加 3 条任务（不同优先级 / 标签）→ 勾一条 → 进度条变化
2. 关窗口 → 托盘图标仍在 → 双击恢复
3. 改系统日期到明天（或等跨零点）→ 未完成任务自动出现在新的一天，带「顺延 N 天」
4. 回到历史日 → 显示「未完成 · 已顺延至今天」，添加栏与删除按钮消失，勾选无效
5. 勾选开机自启 → 检查 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
6. 重复双击 exe → 只激活已有窗口，不出现第二个进程
7. 手动改坏 `tasks.json` → 启动后自动隔离并从备份恢复
8. 在中文用户名 / 带空格路径下安装运行

常见坑：

- **Windows Defender / 杀软误报**：未签名的单文件自包含 exe 首次运行可能触发 SmartScreen 或误报，尤其便携版。处理：点「更多信息 → 仍要运行」；企业环境加白名单；正式发布建议买代码签名证书（EV 可立即获得 SmartScreen 信誉），用 `signtool sign /fd SHA256 /tr http://timestamp.digicert.com ...` 签名。
- **首次启动略慢**：单文件 exe 会把运行时解压到 `%TEMP%\.net\DayTodo`，约 1–3 秒，之后有缓存。
- **托盘图标不显示**：检查是否被 Windows 托盘折叠，或图标资源未嵌入；可重启资源管理器。
- **开机自启路径含空格**：脚本已用双引号包裹 exe 路径。
- **DPI 模糊**：`app.manifest` 已声明 PerMonitorV2；多显示器不同缩放需在你的目标系统实测。
- **数据写不进去**：确认 `%APPDATA%\DayTodo` 可写（企业策略可能重定向 AppData）。
- **32 位系统**：产物是 `win-x64`，安装包也已限制 x64；Win10/11 32 位需改成 `win-x86` 重新发布。
