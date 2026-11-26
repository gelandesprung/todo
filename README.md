# DayTodo 日清单

一个本地优先的待办与随记工具：Windows 桌面版 + 安卓随行版。按天显示任务、随手记下想法、可查历史；未完成的任务第二天自动续过来。

- 桌面技术栈：C# / WPF / .NET 8（`net8.0-windows`），自包含发布，目标机器**不需要安装 .NET**
- 安卓技术栈：Kotlin + Jetpack Compose，minSdk 24 / targetSdk 35
- 目标系统：Windows 10 / 11 64 位（需在你的目标机器上实测）；Android 7.0 及以上
- 权限：桌面版普通用户权限，安装与运行都不弹 UAC
- 主题：固定浅色
- 当前版本：桌面 **1.0.9** / 安卓 **1.3**（[下载安装包 / 便携版 / APK](https://github.com/gelandesprung/todo/releases/tag/v1.0.9)）
- 两端数据**不互通**：桌面版存在 `%APPDATA%\DayTodo`，安卓版存在应用私有目录，各自独立

## 功能

| 功能 | 行为 |
|---|---|
| 按天显示 | 左侧日历点选任意日期，或顶部前后箭头翻天 |
| 查历史 | 历史日显示当天出现过的全部任务，含「当日未完成」标记 |
| 自动顺延 | 启动和跨零点时，未完成任务推进到今天，累计顺延天数 |
| 顺延标记 | 橙色「顺延 N 天」，满 3 天变红；历史日显示「未完成 · 已顺延至今天」 |
| 历史只读 | 选中今天之前的日期时，不能新增、勾选、删除 |
| 删除 | 硬删除，点「删除」立即移除，不进回收站、不可恢复 |
| 撤回 | 任务行「撤回」把这条计划拿回下方编辑框，改完再重新发；编辑框同时只留最后一条撤回 |
| 改期移动 | 任务行「改期」可移到今天或未来任意一天；未来任务行多一个「拉回今天」，一键移回 |
| 设置面板 | 标题栏齿轮或托盘「设置…」打开；开机自启、托盘行为、MCP 端口与令牌、数据目录、关于全部收在这里 |
| 记录位置 | 设置 →「打开记录所在位置」，在资源管理器里选中当天的 `days\yyyy-MM-dd.json`（同目录还有 `manifest.json` 与 `backups`） |
| 标签 / 优先级 | 标签逗号分隔最多 4 个；高 / 中 / 低三级，列表按未完成优先、再按优先级排序 |
| 托盘常驻 | 关闭窗口只隐藏到托盘，双击托盘图标恢复；托盘菜单只剩「显示主窗口 / 设置… / 退出」 |
| 开机自启 | 设置里开关，写 `HKCU\...\Run`，带 `--minimized` 参数，启动后直接进托盘 |
| 单实例 | 第二次启动时激活已有窗口，不重复开进程 |
| 随记 | 主窗口「任务 / 随记」标签共用左侧日历；按天归类，只能记今天，历史随记只读 |
| 随记引用 | 历史随记点「引用」跳回今天并插入编辑框；引用按 ID 回读原文，源被删时用快照兜底 |
| 随记折叠 | 长随记默认只显示约 3 行，可「展开全文 / 收起」；折叠状态只在本机界面上，不写进数据 |
| 当天可改 | 今天的随记可编辑、删除；跨零点后永久只读 |
| @ 待办引用 | 随记正文打 `@` 弹层列出今天 / 昨天的任务，选中后写入引用；删掉正文里的 `@标题` 即解除引用 |
| 全文搜索 | 标签行右侧常驻搜索框，`Ctrl+F` 聚焦；检索全部日期的任务标题 / 标签与随记正文，空格分词取交集，结果按日期倒序分组、命中词高亮；只读，点结果跳回原日期并定位高亮，`Esc` 退出 |

## 安卓版（1.3）

首页是「日期标题 + 日期胶囊条 + 进度摘要 + 分组列表 + 底部三栏 + 右下角 FAB」；任务规则与桌面版一致（今天 / 未来可编辑，历史只读），随记只能记当天。长任务点开居中全文对话框，可复制文本。

搜索从首页右上角的搜索图标进入独立搜索页：输入框自动聚焦并弹出键盘，底部导航与 FAB 暂时隐藏；按日期倒序分组、命中词高亮，筛选「全部 / 任务 / 随记 / 未完成」。结果只读，点一条退出搜索、跳到对应日期与标签、滚动定位并短暂高亮；返回键或左上角箭头退出。跳到日期胶囊条覆盖范围之外时，界面会临时补一个该日胶囊，避免选中态丢失。

安卓数据存在应用私有目录 `filesDir/daytodo-data.json`，**不与桌面版同步或互通**。

构建（工具链内置在 `tools\`，国内镜像直连、不走代理）：

```powershell
& "D:\workbuddy\todo\tools\build-android.ps1" -GradleArgs "assembleDebug","--console=plain","--no-daemon"
```

产物：`android\app\build\outputs\apk\debug\DayTodo-1.3-debug.apk`。本机没有常驻模拟器，界面需在真机上验证。

## 目录结构

```
D:\workbuddy\todo\
├─ src\DayTodo\
│  ├─ DayTodo.csproj          项目配置（WPF + WinForms 托盘，图标 / 清单）
│  ├─ app.manifest            DPI 感知、asInvoker、Win10/11 兼容声明
│  ├─ App.xaml / .cs          启动、单实例、托盘、全局异常处理
│  ├─ MainWindow.xaml / .cs  主窗口、标题栏、改期日期选择、跨零点定时器
│  ├─ Views\SettingsWindow.*  设置面板（通用 / MCP 服务 / 数据 / 关于）
│  ├─ Assets\app.ico          exe / 安装包图标（16/32/48/256 四档）
│  ├─ Assets\tray.png         托盘图标（内嵌资源）
│  ├─ Themes\Styles.xaml      浅色画刷与控件样式（含设置面板用开关）
│  ├─ Models\Entities.cs      TaskItem / NoteItem / NoteReference / AppData / DayFile / 索引模型
│  ├─ Services\Services.cs    顺延引擎、改期、开机自启、数据位置
│  ├─ Services\DataStore.cs   按天分片存储、脏写、manifest、迁移与恢复
│  ├─ Services\CloudStore.cs  云端文件桶抽象（ICloudStore / 空实现 / 本地目录参考实现）
│  ├─ Services\AppSettings.cs 应用偏好（关闭窗口是否最小化到托盘）
│  ├─ ViewModels\ViewModel.cs MVVM 视图模型与命令（任务 + 随记）
│  ├─ ViewModels\SettingsViewModel.cs 设置面板视图模型
│  └─ Mcp\                    内嵌 MCP 服务
│     ├─ McpServer.cs         JSON-RPC 2.0 与 10 个工具（任务 + 随记）
│     ├─ McpHttpHost.cs       Streamable HTTP，TcpListener 手写，零 NuGet 依赖
│     ├─ McpTaskService.cs    任务工具的数据操作
│     ├─ McpNoteService.cs    随记工具的数据操作
│     └─ McpSettings.cs       开关状态持久化
├─ android\
│  ├─ app\src\main\java\com\daytodo\android\
│  │  ├─ MainActivity.kt      入口
│  │  ├─ model\Models.kt      数据模型与日期工具
│  │  ├─ data\Repository.kt   本地 JSON 存储（filesDir）
│  │  ├─ data\ReminderScheduler.kt 每日提醒
│  │  └─ ui\                  Compose 页面与视图模型（MainScreen / SearchScreen / SettingsScreen / Sheets / Theme / DayTodoApp）
│  └─ app\build.gradle.kts    安卓版本 1.3（versionCode 4）
├─ tools\                     本机 JDK 17 / Gradle 8.9 / Android SDK / .NET SDK（不入库）
├─ build\
│  ├─ make-icon.ps1          生成多尺寸 ico + 托盘 png
│  ├─ publish.ps1            一键编译 / 发布 / 打安装包
│  └─ installer.iss          Inno Setup 6 脚本（当前用户安装）
├─ dist\
│  ├─ portable\DayTodo.exe   单文件便携版
│  └─ installer\DayTodo-Setup-1.0.9.exe
└─ README.md
```

## 数据

自 1.0.4 起改为**一天一个文件**，不再是全库存一个 `tasks.json`：

```
%APPDATA%\DayTodo\
├─ days\
│  ├─ 2026-11-11.json     当天的任务（按 DateCurrent 归属，一条任务只存一份）与随记
│  ├─ 2026-11-12.json
│  └─ 2026-11-13.json     今天
├─ manifest.json          索引：每个日期的文件名、任务数、随记数、SHA-256
├─ backups\
│  ├─ 2026-11-13\
│  │  └─ 2026-11-13-071530.json   该日改动前的副本，每个日期最多 7 份
│  └─ corrupt\            损坏文件的隔离目录
├─ app-settings.json      关闭窗口是否最小化到托盘
├─ mcp-settings.json      MCP 启用状态、端口、访问令牌
└─ tasks.json             旧版全库文件，迁移后原样保留作为回滚点
```

- 日文件格式：`{ "SchemaVersion": 3, "Date": "yyyy-MM-dd", "Tasks": [...], "Notes": [...] }`，UTF-8 无 BOM、中文不转义，可直接用记事本看；字段仍是 PascalCase（任务的 `Id` 是 GUID 字符串，`DateCompleted` 可为 null；随记见下节）
- `manifest.json` 的 `Count` 仍是任务数，新增 `NoteCount` 单独统计当天随记数，SHA-256 覆盖整份日文件
- `manifest.json` 由 PC 端独占写，**不含时间戳**：内容不变就不会重写，将来别的设备只读它、再用 `Sha256` 算出需要增量下载哪几天
- **只写脏文件**：保存时按 `DateCurrent` 分组，内容没变的日期文件一个字节都不动。这是特意为了网盘同步设计的 —— 历史文件保持静态，不会每次点一下就产生一堆新版本或冲突副本
- 顺延 / 改期不需要跨文件事务：改写 `DateCurrent` 后，Save 会把它写进新日期、把旧日期文件清空（**保留空文件而不是删除**，同步盘对删除更敏感，历史日也需要知道这天存在过）
- 写入仍是先写 `.tmp` 再原子替换；某一天文件损坏时只隔离这一天（`backups\corrupt\`）并从该日最近备份恢复，**不影响其他日期**
- 一次性迁移：首次启动若发现旧 `tasks.json` 且没有 `manifest.json`，按 `DateCurrent` 拆成日文件并生成索引；旧文件不删，可随时回滚
- `DAYTODO_DATA_DIR` 环境变量仍可重定向整个数据目录（隔离测试用）。设置后该实例会使用独立的单实例互斥与激活事件，**不会**被日常托盘实例挡住、也不会把它的窗口抢到前台；不设置时行为与以前完全一致
- **卸载程序不会删除任务数据**；需要彻底清除请手动删 `%APPDATA%\DayTodo`
- 找记录：设置 →「打开记录所在位置」会选中今天的文件；「打开备份目录」打开 `backups`。想导出/备份直接复制整个数据目录即可，网盘同步也指向这个目录

### 随记

随记和任务住在同一份按天文件里，但规则更严格：**写入只发生在真正的今天**，翻到历史或未来日期时底部不渲染输入框，跨零点会自动清空草稿并锁死。历史随记不可编辑、不可删除，界面按钮和 ViewModel 命令两层都拦住。

一条随记是 `{ "Id", "Date", "CreatedAt", "Text", "References" }`：`Date` 创建后不再变，`CreatedAt` 是本地 `HH:mm:ss`（编辑保留原值），`Text` 是多行纯文本，没有标签和优先级。当天随记按时间正序显示，像日记一样往下读。

`References` 是指向其他随记的链接：`{ "Date", "NoteId", "Text" }`。展示时按 `NoteId` 回读目标当前文本，目标已被删除时用 `Text` 快照兜底；点引用块会跳到被引用那天。引用只接受已存在的随记，不允许悬空链接。

长随记默认折叠到约 3 行（超出显示省略号），点「展开全文」看完整内容，再点「收起」折回去。折叠状态只存在界面行模型里，不写进文件，列表刷新后回到折叠。

> 当前定位是「**PC 写、云端存、别处读**」。程序只在启动时读取磁盘、运行中内存常驻，不会感知网盘从别处同步过来的改动；因此适合单向备份或「一端退出后另一端打开」，不建议两台电脑同时开着实时双写。

### 任务字段与顺延规则

一条任务永远只有一条记录，不按天复制。物理上按 `DateCurrent` 落到对应日期的文件里；启动时 `Load` 会把**所有**日文件合并回同一份内存数据，所以下面这套历史可见规则完全没变（不能只加载当天文件，否则历史日看不到顺延过的任务）：

| 字段 | 含义 |
|---|---|
| `DateOrigin` | 首次排在哪天；改期移动任务时会改写为目标日（等于重新排期） |
| `DateCurrent` | 当前排期，顺延后变成今天 |
| `DateCompleted` | 完成日期，`null` = 未完成 |
| `PostponeDays` | 累计顺延天数 |

某天是否显示这条任务：`DateOrigin ≤ 该天 ≤ DateCompleted`（未完成时右边界为今天）。
扫描到 `DateCurrent < 今天` 且未完成的任务时，`PostponeDays += 相差天数`，`DateCurrent = 今天`。

### 改期 / 移动任务

未完成的任务可以用任务行的「改期」打开日历窗口，选今天或之后的任意一天（不能移到过去）。查看未来某天时，那天的任务行会多出「拉回今天」，点一下就移回今天；也可以在日历窗口里直接选今天。

移动 = 重新排期，不是复制：`DateOrigin` 和 `DateCurrent` 一起改成目标日，`PostponeDays` 清零，任务从原来的日期消失（历史日也不再显示它）。目标日到了还没完成，就照常按上面的顺延规则累计天数。已完成的任务、历史日（只读）不能移动。

移到未来后，今天的列表里就看不到这条任务了；左侧「今日概览」会出现「未来安排 N 项 · 查看」，点它跳到最近的未来日，在那里可以「拉回今天」（点完界面会跟着回到今天）。

### 撤回：把计划拿回编辑框再改

任务行的「撤回」相当于「发错了，重新改一下」：点一下，这条计划**从清单里移除**，标题、标签、优先级一起回到底部编辑框，光标自动落到输入框末尾，编辑框上方出现一条「撤回」提示，写清撤回的是哪一条。改完按回车或点「添加」就重新发出去 —— 新任务排到当前选中的那一天，未完成状态重置。

编辑框同时**只放一条撤回内容**：连续撤回多条时，前一条的内容被后一条覆盖，最后只留下最后撤回的那条（之前撤回的计划已按你的操作从清单里移除，不会攒成一堆草稿）。自己把输入框清空，撤回提示也随之消失。

和「删除」的区别：「删除」是硬删除、什么都不留；「撤回」是「改完重发」的入口，两者都在可编辑的日期生效，历史日（只读）不显示这两个按钮。

## CloudStore 接口（预留，默认关闭）

`Services\CloudStore.cs` 只定义一层抽象：把云端看成「一个能被两端程序读写的**扁平文件桶**」，契约只有三个动作。

| 成员 | 含义 |
|---|---|
| `ListAsync()` | 列出桶内对象，返回 `CloudFile(Name, Length, Sha256, LastModifiedUtc)`；SHA-256 与 `manifest.json` 一致，供增量比对 |
| `GetAsync(name)` | 取回一个文件的原始字节；不存在返回 `null` |
| `PutAsync(name, bytes)` | 整文件原子替换；**内容相同就一个字节都不写**并返回 `false`，避免同步盘产生多余版本 |
| `Provider` / `IsEnabled` | 适配器标识，以及这个实例是否真的会做 I/O |

文件里还有三个配套类型：`NullCloudStore`（默认，纯惰性；`Put` 直接抛异常而不是静默丢数据）、`LocalFolderCloudStore`（把本地目录当桶的参考实现，防目录穿越、过滤写入中的 `.tmp`）、`CloudStoreProvider.Current`（唯一的替换入口，读 `DAYTODO_CLOUD_DIR`）。

**当前状态：接口只是接口。** `DataStore` 不调用它，程序不自动上传、不自动下载、不发任何网络请求，也不会读写数据目录之外的文件：

- 不设环境变量时 `Provider=none`、`IsEnabled=False`；
- 设 `DAYTODO_CLOUD_DIR` 指向某个本地目录（例如微云同步助手正在同步的文件夹），只是让 `LocalFolderCloudStore` 可用，**并不会触发同步**；
- 接口刻意**不提供 Delete**（按天布局保留空文件而不用删除）、**不做锁 / ETag**（本设计里 PC 独占写，其他端只读），并统一用异步 + `CancellationToken`，方便以后接 HTTP 适配器。

以后要接真实云产品，只需实现 `ICloudStore` 并替换 `CloudStoreProvider` 的创建逻辑（微云 MCP / 坚果云 WebDAV / 对象存储各写一个适配器），按天分片的文件布局、`manifest.json` 和将来的移动端读取代码都不用改。自检里的 `CloudStore*` 报告字段见「测试与排错」。

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

自动启动自检（`--verify` 会**真实显示主窗口**，模拟输入一条任务并按回车，再切到随记标签写一条、试引用和历史只读，最后自动回滚整个数据目录，不会改动你的清单）：

```powershell
Remove-Item "$env:TEMP\DayTodo-verify.ok" -Force -ErrorAction SilentlyContinue
Start-Process "dist\portable\DayTodo.exe" -ArgumentList "--verify" -Wait
Get-Content "$env:TEMP\DayTodo-verify.ok"
```

报告里应出现 `CanAdd=True`、`AddButtonEnabled=True`、`EnterHandled=True`，且 `TasksAfterEnter` 比运行前多 1（`TasksAfterEnter` 统计的是今天那个日文件里的任务数）；若启动期抛异常（弹「启动失败」），标记文件不会生成。自检会在开始前给整个数据目录做快照，结束后完整还原 —— 日文件、manifest、按日备份和设置文件都不会被改动。

自检还会**真实显示一次设置面板**并跑一遍设置动作，确认绑定与热更新都没坏：

| 报告字段 | 含义 |
|---|---|
| `SettingsAutoStart` / `SettingsMcpEnabled` / `SettingsPort` / `SettingsEndpoint` / `SettingsStatus` | 设置面板各字段是否正确绑定 |
| `PortApplied` / `PortListenerMoved` / `OldPortClosed` | 改端口后监听器是否真的搬到新端口、旧端口是否释放 |
| `TokenRejectsAnonymous` / `TokenAcceptsBearer` | 加令牌后无令牌 401、带令牌 200 |
| `PortRestored` / `TokenRestored` | 自检结束后端口与令牌是否恢复原值 |
| `SnapshotFiles` / `DayFiles` / `ManifestExists` | 快照文件数、按天文件数、索引是否存在 |
| `SettingsDaysDir` | 设置面板数据分区绑定的 days 目录 |
| `CloudStoreProvider` / `CloudStoreEnabled` | 云端适配器的默认值（应为 `none` / `False`，即完全惰性） |
| `CloudStorePut` / `CloudStoreDirtyWriteSkipped` / `CloudStoreList` / `CloudStoreRoundTrip` / `CloudStoreMissingIsNull` / `CloudStoreRejectsTraversal` / `CloudStoreNullRejectsPut` | 在临时目录里跑一遍 `ICloudStore` 契约：写入、相同内容跳过、列目录 + SHA-256、读回一致、不存在返回 null、拒绝目录穿越、惰性实现的 `Put` 明确报错 |
| `RecallButtonRendered` / `RecallButtonVisible` / `RecallButtonCommand` | 任务行里「撤回」按钮是否真的渲染出来、可见、命令已绑定 |
| `RecallComposerTitle` / `RecallDraftPending` / `RecallHintText` / `RecallHintVisible` | 撤回后内容是否回到编辑框、草稿状态与提示条是否正确 |
| `RecallRemovedFromList` / `TasksAfterRecall` | 撤回后清单里没有这条、当天日文件也已写盘（计数减 1） |
| `RecallKeepsLastOnly` / `RecallFirstGone` | 连续撤回两条时，编辑框只留最后一条，前一条已不在清单里 |
| `RecallResent` / `RecallDraftCleared` / `RecallComposerCleared` | 改完重新发成功，草稿状态与输入框都已清空 |
| `NoteTabVisible` / `NoteAdded` / `NoteEdited` | 切到随记标签、回车保存、当天编辑都真的生效 |
| `QuoteSaved` / `NotesInDayFile` | 引用随保存写入日文件，条数与当天文件一致 |
| `HistoryNoteReadOnly` / `HistoryRowNotEditable` / `HistoryEditButtonHidden` / `HistoryDeleteButtonHidden` | 历史随记只读：不能写入，编辑 / 删除按钮不渲染 |
| `LongNoteCanFold` / `LongNoteInitiallyCollapsed` / `LongNoteExpanded` / `LongNoteCollapsedAgain` | 长随记默认折叠、展开、再次收起 |
| `SearchOpened` / `SearchHits` / `SearchHasTaskHit` / `SearchHasNoteHit` / `SearchGroupHeaders` / `SearchHighlightRuns` | 搜索入口、跨天命中（任务 + 随记）、日期分组与命中词高亮 |
| `SearchFilterTasksOnly` / `SearchDownHandled` / `SearchEnterHandled` / `SearchJumpedToHitDay` / `SearchClickJumped` / `SearchEscKeptDate` | 筛选、↑↓ / Enter / 点击跳转、Esc 恢复日期均通过 |

版本记录：1.0.1 修掉启动期绑定异常（`ProgressBar.Value` 默认双向绑定，绑定到只读计算属性 `Progress` 时 WPF 尝试回写并抛异常，已改为 `Mode=OneWay`）；1.0.2 重做添加栏（带边框的长输入框、框内提示文字、回车与按钮双通道），并把自检扩展为端到端交互测试；1.0.3 抽出设置面板，开机自启 / 记录位置 / MCP 端口令牌等操作全部收进设置，主界面左栏只留 MCP 服务一项；1.0.4 把全库单文件改成按天分片（`days\yyyy-MM-dd.json` + `manifest.json`），加入脏写、按日备份、单日损坏隔离与旧数据迁移，为网盘同步和将来的移动端读取预留文件布局；同时抽出 `CloudStore` 抽象接口（默认惰性、不含任何云端实现），并给每条任务加上「撤回」操作（拿回编辑框改完重发，编辑框只保留最后一条撤回）。1.0.5 加入「随记」：按天归类、只能记今天、历史只读、支持跨天引用，和任务共用同一份日文件；1.0.6 加长随记折叠（约 3 行 / 展开 / 收起）并把版本号改成从 csproj 单一来源驱动打包；1.0.7 把随记接入 MCP，新增 5 个随记工具，让 AI 客户端也能读写、搜索随记。1.0.8 加长任务全文卡片（窗口内 overlay，可复制）与随记里的 `@` 待办引用；安卓 1.1 重做首页界面、1.2 加任务全文对话框。1.0.9 / 安卓 1.3 加入全文搜索：跨全部日期检索任务标题 / 标签与随记正文，空格分词取交集，按日期倒序分组、命中词高亮，结果只读并跳回原日期定位；桌面用标签行搜索框 + `Ctrl+F`，安卓用独立搜索页。

测试清单：

1. 首次启动 → 添加 3 条任务（不同优先级 / 标签）→ 勾一条 → 进度条变化
2. 关窗口 → 托盘图标仍在 → 双击恢复
3. 改系统日期到明天（或等跨零点）→ 未完成任务自动出现在新的一天，带「顺延 N 天」
4. 回到历史日 → 显示「未完成 · 已顺延至今天」，添加栏与删除按钮消失，勾选无效
5. 设置 → 开机自启 → 检查 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
6. 重复双击 exe → 只激活已有窗口，不出现第二个进程
7. 手动改坏某一天的 `days\yyyy-MM-dd.json` → 启动后只隔离这一天并从该日备份恢复，其他日期任务不受影响
8. 在中文用户名 / 带空格路径下安装运行
9. 今天的任务点「改期」选下周某天 → 今天列表里消失 → 翻到那天能看到 → 点「拉回今天」→ 回到今天
10. 设置 →「打开记录所在位置」→ 资源管理器选中今天的 `days\yyyy-MM-dd.json`；「打开备份目录」能看到按日期分目录的备份；数据分区显示 days 目录与 manifest 路径
11. 设置 → 改 MCP 端口为 8766 → 立刻监听新端口、8765 释放；改回 8765 恢复
12. 设置 → 填访问令牌 → 无令牌的客户端请求 401、带 `Bearer <令牌>` 才通
13. 设置 → 关掉「关闭窗口时最小化到托盘」→ 点关闭按钮应用直接退出；打开则缩回托盘
14. 主界面左栏只剩 MCP 服务一项，标题栏齿轮与托盘「设置…」都能打开设置面板
15. 放一份旧版 `tasks.json` 进隔离数据目录后首次启动 → 自动按 `DateCurrent` 拆成日文件、生成 `manifest.json`，旧文件仍在
16. 连续两次不改动数据的保存 → 所有日文件与 `manifest.json` 的修改时间都不变（脏写生效）
17. 任务行点「撤回」→ 该条从清单消失、内容回到编辑框、提示条出现 → 改标题按回车 → 新的一条排回当天；连着撤回两条 → 编辑框只剩最后那条的内容
18. 切到「随记」写一条长文本 → 默认只显示约 3 行 → 点「展开全文」看完整内容 → 点「收起」回到 3 行
19. 翻到昨天 → 底部没有输入框、随记的编辑/删除按钮不显示；点历史随记「引用」→ 跳回今天并把引用插进编辑框，保存后按 ID 回读原文
20. 让 AI 客户端通过 MCP 调 `create_note` / `search_notes` / `update_note` / `delete_note` → 界面立刻刷新；对历史随记的更新与删除应返回只读错误
21. 按 `Ctrl+F` 或点标签行搜索框 → 输入跨天关键词 → 任务 / 随记命中按日期倒序显示并高亮 → 切「任务 / 随记 / 未完成」筛选 → 点一条跳到那天并短暂高亮；按 `Esc` 回到进入搜索前的日期

常见坑：

- **Windows Defender / 杀软误报**：未签名的单文件自包含 exe 首次运行可能触发 SmartScreen 或误报，尤其便携版。处理：点「更多信息 → 仍要运行」；企业环境加白名单；正式发布建议买代码签名证书（EV 可立即获得 SmartScreen 信誉），用 `signtool sign /fd SHA256 /tr http://timestamp.digicert.com ...` 签名。
- **首次启动略慢**：单文件 exe 会把运行时解压到 `%TEMP%\.net\DayTodo`，约 1–3 秒，之后有缓存。
- **托盘图标不显示**：检查是否被 Windows 托盘折叠，或图标资源未嵌入；可重启资源管理器。
- **开机自启路径含空格**：脚本已用双引号包裹 exe 路径。
- **DPI 模糊**：`app.manifest` 已声明 PerMonitorV2；多显示器不同缩放需在你的目标系统实测。
- **数据写不进去**：确认 `%APPDATA%\DayTodo` 可写（企业策略可能重定向 AppData）。
- **32 位系统**：产物是 `win-x64`，安装包也已限制 x64；Win10/11 32 位需改成 `win-x86` 重新发布。

## MCP 服务：让 AI 客户端管理任务与随记

MCP 服务**内嵌在 DayTodo 主程序里**：应用启动后自动在 `http://127.0.0.1:8765/mcp` 监听，WorkBuddy、Claude Desktop、Cline 等 AI 客户端可以直接创建任务、记随记、搜索随记。工具操作的是主程序同一份内存数据，界面立刻刷新；不需要第二个 exe、不需要文件锁、也不需要外部转发。

### 界面开关

左栏底部只剩一张 **MCP 服务**卡片：一个开关加一行状态（运行中 / 已停止 / 启动失败）。这是主界面上唯一保留的功能开关，其余操作都收进了设置面板 —— 标题栏齿轮按钮或托盘「设置…」都能打开。

设置面板分四块：

| 分区 | 内容 |
|---|---|
| 通用 | 开机自启（写 `HKCU\...\Run`）、关闭窗口时最小化到托盘 |
| MCP 服务 | 启用开关、端口、访问令牌、连接地址（一键复制） |
| 数据 | days 目录 / 当天文件 / manifest 路径、打开记录所在位置、打开备份目录 |
| 关于 | 版本号、退出 DayTodo |

端口与令牌**改完即生效**：设置窗口会立刻重启监听器（无令牌时放行，带令牌时校验 `Authorization: Bearer <令牌>`），状态保存在 `%APPDATA%\DayTodo\mcp-settings.json`，下次启动沿用（默认开启）。

```json
{
  "Enabled": true,
  "Port": 8765,
  "Token": ""
}
```

`Token` 非空时请求需带 `Authorization: Bearer <令牌>`；也可用环境变量 `DAYTODO_MCP_TOKEN`（配置文件优先）。服务只绑定 127.0.0.1 并校验 `Origin`。端口占用时服务保持关闭，主程序照常运行。

### 工具

| 工具 | 作用 | 主要参数 |
|---|---|---|
| `create_task` | 新建任务，默认排今天，可指定今天或未来日期 | `title`（必填）、`date`（`yyyy-MM-dd`）、`priority`（`low`/`normal`/`high`，也接受 高/中/低 或 0/1/2）、`tags`（最多 4 个） |
| `list_tasks` | 列出任务，返回 `id` 供后续操作 | `date`、`include_completed`、`all` |
| `complete_task` | 标记完成 / 撤销完成 | `id` 或 `title`、`undo` |
| `delete_task` | 硬删除，不可恢复 | `id` 或 `title` |
| `get_overview` | 今日总数 / 已完成 / 未完成 / 顺延数 | 无 |
| `create_note` | 在今天的随记里记一条想法，可附带引用 | `text`（必填，最多 5000 字）、`references`（已存在随记的 ID 数组，也接受 `{id,date?,text?}`） |
| `list_notes` | 查看某天（默认今天）的随记，返回 ID / 日期 / 时间 / 正文 / 引用 | `date`、`all`、`limit`（默认 100，上限 500） |
| `search_notes` | 全文搜索，大小写不敏感，正文和引用文字都匹配，按日期倒序 | `query`（必填）、`from`、`to`、`limit` |
| `update_note` | 改今天的随记正文和/或引用；至少给一项 | `id`（必填）、`text`、`references`、`clear_references` |
| `delete_note` | 永久删除今天的随记，不可恢复 | `id`（必填） |

历史日期和历史随记都是只读：给过去日期建任务、修改或删除历史随记都会返回错误。随记只能写到真正的今天，引用必须是已存在的随记 ID（可用 `list_notes` / `search_notes` 取得），不允许悬空链接。`delete_task` / `delete_note` 都是立即硬删除，不进回收站。

### 在 WorkBuddy 里配置

服务由应用托管，客户端用 URL 连接（不再需要 `command` 启动独立进程）。

1. 打开用户级配置文件 `C:\Users\<用户名>\.workbuddy\mcp.json`（不存在就新建）。也可以从侧边栏「连接器 / 插件 → 管理连接器 → 右上角自定义连接器」打开内置 JSON 编辑器：

```json
{
  "mcpServers": {
    "daytodo": {
      "type": "http",
      "url": "http://127.0.0.1:8765/mcp",
      "description": "DayTodo 日清单：管理每日任务与随记"
    }
  }
}
```

2. 在连接器管理里找到 `daytodo`，点「信任」并启用，状态变绿即连接成功。
3. 重启 WorkBuddy 客户端（改 `mcp.json` 后需重启才会加载），之后直接用自然语言说「帮我加个任务：……」或「把刚才的想法记到随记里，搜一下之前有没有类似的」即可。

WorkBuddy 的 Streamable HTTP 类型固定写 `"type": "http"`；Cline、Claude Desktop 等客户端可能写 `"streamableHttp"`，也常可省略 `type` 由 `url` 自动推断。启用 token 时加 `"headers": { "Authorization": "Bearer <令牌>" }`。CLI 用户可执行 `codebuddy mcp add-json --scope user daytodo '{"type":"http","url":"http://127.0.0.1:8765/mcp"}'`。

**注意**：DayTodo 没运行（或开关关闭）时客户端连不上，先启动应用；建议同时开启「开机自启」。

手动试一下：

```bash
curl -X POST http://127.0.0.1:8765/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"create_task","arguments":{"title":"用 AI 加的任务"}}}'
```

记一条随记、再搜一下：

```bash
curl -X POST http://127.0.0.1:8765/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"create_note","arguments":{"text":"今天想到一个点子"}}}'

curl -X POST http://127.0.0.1:8765/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"search_notes","arguments":{"query":"点子"}}}'
```

端点行为：`POST` 返回 JSON（只接受 `text/event-stream` 时返回 SSE），通知返回 202，`GET` 返回 405，`DELETE` 结束会话。

### 实现位置

- `src\DayTodo\Mcp\McpServer.cs`：MCP JSON-RPC 2.0（initialize / tools/list / tools/call / ping）
- `src\DayTodo\Mcp\McpHttpHost.cs`：Streamable HTTP，TcpListener 手写，零 NuGet 依赖
- `src\DayTodo\Mcp\McpTaskService.cs`：任务工具的数据操作，切到 UI 线程后走 `DataStore.Save` + `Rebuild`
- `src\DayTodo\Mcp\McpNoteService.cs`：随记工具的同一套数据操作；只写今天、历史只读、引用按 ID 解析
- `src\DayTodo\Mcp\McpSettings.cs`：开关状态持久化

数据仍是同一份内存 `AppData`，落盘时按天分片（见「数据」章节）。因为只有一个进程写，不再需要跨进程锁、乐观重试或 FileSystemWatcher —— 这也意味着程序**不会**感知网盘从别处同步过来的文件改动。当前定位是「PC 写、云端存、别处读」：适合单向备份或一端退出后另一端打开，不适合两台电脑同时开着实时双写。
