# PCL SERVER 1.0.0-alpha — 基于 PCL2 2.14.0.0 的逆向还原与改造

对 `plain craft launcher 2.exe`（Plain Craft Launcher 2，v2.14.0.0，快照版，sha256 `77fbc26a…753f0`）的完整反编译、资源提取与工程还原。**工程可编译、可运行**，界面与网络行为与原版一致。

> **制作**：本项目的逆向还原、服务端模式与全部改造，由 **DSH**（DeepSeek Harness 编码智能体，模型 **DeepSeek-V4.1-Flash-1017**）完成；项目维护者与需求方为 **LoneExplorer337**。

详细分析见 **[REPORT.md](REPORT.md)**。

本分支已重新品牌化：程序名 **PCL SERVER**（产物 `PCL SERVER.exe`），版本 **1.0.0-alpha**（内部版本号 1，构建类型 Release/Alpha）；程序图标、窗口图标与关于页头像沿用原版 PCL 素材（与原版字节一致）。关于页保留原作者信息，并新增修改者 **LoneExplorer337**。

## 快速开始

```bash
cd src
dotnet build -c Release      # 0 错误 / 254 警告 / 约 50 秒
```

产物：`src/bin/Release/net48/PCL SERVER.exe` —— **单文件，无任何依赖 DLL**（当前构建为 6,053,376 字节，原版 PCL2 2.14.0.0 为 5,993,984）。csproj 里 13 个托管程序集引用是编译期引用（`<Private>false</Private>`，不复制到输出目录），运行时由 `Application` 静态构造注册的 `AppDomain.AssemblyResolve` 从内嵌的 `PCL.Resources.resources` 里 `Assembly.Load(byte[])`，与原版的单文件打包方式一致；原生库 `libwebp64` 则按原逻辑在首次使用时写成目录下的 `libwebp.dll`。

运行需要 .NET Framework 4.8。程序会在自身目录下创建 `PCL\` 与 `.minecraft\`，属正常行为。

## 目录

| 路径 | 内容 |
| --- | --- |
| `src/` | 可编译工程：144 个 `.cs`（85,628 行）+ 63 个 `.xaml` + 内嵌资源 + `libs/` |
| `src/bin/Release/net48/` | 已构建产物（单文件 exe，不含依赖 DLL） |
| `libs/` | 从 exe 内嵌资源中解出的 18 个依赖（13 托管 DLL + `libwebp64.dll` + 4 个 ZIP/XML 数据包） |
| `assets/manifest/` | 两个 `.resources` 原始 blob |
| `assets/unpacked/` | 全部 131 个资源条目（61 BAML + 48 PNG + 2 JPG + ICO + TTF + 18 个 DLL/数据包） |
| `tools/` | 资源提取、构建、冒烟脚本与 PE 元数据读取器 |
| `_backup_PCL/` | ILSpy 反编译原始输出（未做任何修改），已单独发布在 [PCL2-2.14-DECOMPILED-BY-LoneExplorer337](https://github.com/LoneExplorer377/PCL2-2.14-DECOMPILED-BY-LoneExplorer337) |

## 新增功能与改动（相对原版 2.14）

| 改动 | 说明 |
| --- | --- |
| **品牌改造** | 程序名改为 **PCL SERVER**（exe、窗口标题、关于页、运行期 pack URI 全部同步），版本号改为 **1.0.0-alpha**（`VersionBaseName` 1.0.0.0 / `VersionCode` 1 / `VersionDisplay` 1.0.0-alpha），图标沿用原版 PCL 素材，关于页新增修改者 LoneExplorer337 署名行（无头像），服务器页按钮统一为 32/34 px 并加宽内边距（不再被裁切） |
| **服务端模式** | 服务端实例管理、原版/Paper/Fabric 核心下载安装、内嵌控制台（实时日志 + 指令输入，不弹命令行窗口）、Modrinth 插件/Mod 与整合包安装、`server.properties` 图形化、白名单/OP、崩溃报告、端口互斥、关闭 PCL 时的处理询问。详见 **[SERVER-MODE.md](SERVER-MODE.md)** |
| **服务端页并入启动页** | 去掉顶部「服务器」导航；「版本选择」里新增「服务端 (N)」卡片，选中实例后启动页左栏变成服务器信息、右侧变成服务器控制台 |
| **下载页 客户端/服务端 切换** | 版本列表顶部的「下载类型」卡片可一键切换：服务端版只出核心、选中即新建实例并下载；带版本号搜索与分组筛选 |
| **「插件」页** | 下载页社区资源新增插件分类（仅 Modrinth），安装目标为当前选中的服务端实例 `plugins\`，默认对齐实例版本、版本不符先确认、不再弹保存对话框 |
| **界面缩放** | 设置 →「个性化」新增「界面缩放」滑块（50%–200%，整体缩放窗口与内容，松手生效并持久化到 `Setup.ini` 的 `UiLauncherScale`） |
| 主题全部解锁 | `ModSecret.ThemeCheckOne` 恒返回 `true`，15 个主题（含隐藏/赞助/自定义）均可直接选用；同时删除了主题的解锁提示与悬浮描述 |
| 移除更新功能 | 删掉检查更新、自动下载、更新提示与 `--update` 自我替换相关的界面和代码，保留更新日志查看 |
| 移除赞助界面 | 删除「关于与鸣谢」里的赞助者名单卡片、系统设置里的识别码/土豆码、个性化里的赞助提示与主题解锁覆盖层；「关于」「特别鸣谢」两张卡片按要求保留 |
## 环境

- Windows + .NET SDK（本工程用 SDK 11.0.100-rc.1 验证，`net48` 目标）
- 首次构建需要联网拉 `Microsoft.NETFramework.ReferenceAssemblies`（本机没有 4.8 引用程序集目录）
- `dotnet tool install -g ilspycmd`（若要重新反编译）

## 做了什么修改

ILSpy 输出**不能直接编译**，也不一定能正确运行。本工程相对原始反编译输出：298 个文件有改动（+7,386 / −13,496 行）。完整清单见 [REPORT.md 第 12 节](REPORT.md)，最关键的三处：

1. **818 个 VB `WithEvents` 属性**被改写成构造函数里的 `Hook_X(X)` 手工挂钩（否则与 XAML 生成的字段重名，1636 条 `CS0102`）；
2. **27 个 VB 匿名委托**（ILSpy 整类丢弃、只留 0 字节占位文件）按 IL 签名重建为 `src/PCL/VBAnonymousDelegates.cs`；
3. **两处「能编译但运行必崩」的反编译失真**：`((BaseLogger)this).Format(...)` 改为 `base.Format(...)`（否则启动即栈溢出 `0x800703E9`），`LoaderTask.InputDelegate` 字段类型 `Func<object>` 改为 `Delegate`（否则启动即 `InvalidCastException`）。

## 验证状态

| 项 | 结果 |
| --- | --- |
| 干净重建 | 0 错误 / 254 警告 / 49.04 s |
| 冒烟启动 | 进程存活，主窗口 `900 x 550` 正常显示 |
| 单文件运行 | 目录里只放 exe（连 `.exe.config` 都不要）也能启动，日志 0 条 E 级错误、0 条程序集加载失败 |
| 运行日志 | `PCL\Log1.txt` 65 行，0 条错误 |
| 网络 | `launchermeta.mojang.com` 版本清单请求成功 |
| 实际下载游戏 | 1.21.1-Fabric 0.20.1 完整下载安装（assets 790.2 MB）并启动成功 |
| 服务端模式 | 原版 1.21.1 / Paper 1.21.1 / Fabric 1.21.1 下载启动、控制台指令、正常停止、插件/Mod/整合包安装、属性编辑、关闭询问均实测通过；Forge/NeoForge 因本机无法访问 Mojang 源装不上（已给出中文原因） |
| 界面缩放 | 拖到 130% 后窗口 `1170 x 720` 一次到位、仅一条日志；重启后保持（`UiLauncherScale`） |
| 下载页服务端版 | 切到服务端版 → 搜索 `1.21.1` → 分组只剩正式版/预览版 → 点版本自动建实例并带出版本号 |
| 插件页 | 选中 Paper 实例后在插件页安装 VeinMiner，jar 直接落进 `PCL\Server\<实例>\plugins\`（969,367 字节），详情页版本分组自动落在 1.21.1 |
| 品牌改造 | 产物 `PCL SERVER.exe`（6,053,376 字节）；文件属性 Product=`PCL SERVER` / FileDescription=`PCL SERVER 启动器` / FileVersion=1.0.0.0；窗口标题 `PCL SERVER　`、标题栏左侧标识改为文字 **PCL SERVER**；程序/窗口图标与关于页头像保持原版 PCL 素材；关于页显示「PCL SERVER / 当前版本：1.0.0-alpha（1）」与 LoneExplorer337 署名行（无头像） |
| 未验证 | 微软登录全流程（设备码）、其余版本/加载器组合 |

## 免责声明

本项目为逆向工程产物，**仅供学习研究与兼容性验证**，不得用于商业用途。

- **著作权**：Plain Craft Launcher 2 的源码与素材著作权归原作者（Meloong-Git）及「成都瓜皮龙科技有限公司」所有；本仓库不主张任何原创权利，只作为学习材料托管。若权利人提出异议，仓库将立即下架。
- **第三方**：内嵌的 Newtonsoft.Json、NAudio、MeloongCore、CacheCow、Ookii.Dialogs.Wpf、PCLCS 等组件遵循各自许可（多为 MIT / Apache-2.0 / MS-PL），请以各项目原始许可为准。
- **责任**：本分支的修改（品牌化、服务端模式、界面与下载改动、移除更新与赞助界面）由 LoneExplorer337 与 DSH + DeepSeek-V4.1-Flash-1017 完成，**不代表原作者立场**，也不为其质量或行为背书。使用本程序造成的任何后果由使用者自行承担。
- **无担保**：按「现状」提供，不附带任何明示或默示担保。
