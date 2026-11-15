# PCL2 2.14.0.0 逆向分析报告

> 目标：`plain craft launcher 2.exe`（Plain Craft Launcher 2.14.0.0 官方发布版，Minecraft 启动器）
> 产出：完整可编译源码工程、全部内嵌资源、本报告。
> 分析环境：Windows 11 / .NET SDK 11.0.100-rc.1 / ilspycmd 11.2.0.10050。

---

## 0. 摘要

该程序是 **未加壳、未混淆、未签名** 的 .NET Framework 4.8 WPF 应用，由 VB.NET 编写（IL 里保留 `Microsoft.VisualBasic.CompilerServices`、`[StandardModule]` 与 `$VB$Local_*` 局部变量）。单文件发布方式是把 13 个托管 DLL、1 个原生库和 4 个数据包（共 18 项）以 base64 塞进 `PCL.Resources.resx`，运行时通过 `AppDomain.AssemblyResolve` 从资源加载。

| 项目 | 结论 |
| --- | --- |
| 壳 / 保护 | 无（无 UPX，无 .NET Reactor / ConfuserEx / Eazfuscator 特征，元数据完整） |
| 名称混淆 | 无（类型名、方法名、字段名、原始资源条目名全部保留） |
| 控制流混淆 | 未发现（只有 VB 编译器生成的 `ProjectData.SetProjectError` 样板） |
| 反调试 / 反篡改 | 未发现（全树无 `Debugger.IsAttached`、`IsDebuggerPresent`、`CheckRemoteDebuggerPresent`） |
| 字符串加密 | 无（中文 UI 文案、URL、OAuth client id 全部明文） |
| 数字签名 | **未签名**（`Get-AuthenticodeSignature` = `NotSigned`） |
| 反编译难度 | 极低（ilspycmd 一条命令 30 秒产出 137 个 .cs / 88,681 行） |
| 可还原度 | 高：完整工程可编译、可运行，界面与网络行为与原版一致 |

**两个「反编译会坏、必须手工修」的根因**（详见第 12 节）：

1. `((BaseLogger)this).Format(...)` —— ILSpy 用父类转换表达 IL 的**非虚调用** `call`，C# 里却是虚派发，导致 `PclLogger` 无限递归，启动即 `0x800703E9`（`STATUS_STACK_OVERFLOW`）。必须改成 `base.Format(...)`。
2. `(Func<object>)(object)InputDelegate` —— ILSpy 为绕开 C# 类型检查插入的「双转换」，在 IL 里其实没有 cast（原样 `stfld` 把 `Func<TIn>` 存进 `Func<object>` 字段）。当 `TIn` 是值类型元组时，运行期 `InvalidCastException` 直接炸掉 `PageLaunchLeft` 的静态构造。必须把字段类型改成 `Delegate`。

---

## 1. 样本基本信息

| 属性 | 值 |
| --- | --- |
| 文件名 | `plain craft launcher 2.exe` |
| 大小 | 5,993,984 字节 |
| SHA-256 | `77fbc26a13abf725dbd26038cba023224c2ce5518f3ee9c4608acb2274d753f0` |
| MD5 | `1b4f791d400f5e637d22887867e84739` |
| 文件版本 / 产品版本 | 2.14.0.0 / 2.14.0.0 |
| 显示名 | Plain Craft Launcher 启动器（FileDescription） |
| 说明 | Minecraft 启动器（Comments） |
| 版权 | Copyright © 成都瓜皮龙科技有限公司（LegalCopyright） |
| CompanyName | 空 |
| 内部名 | Plain Craft Launcher 2.exe |
| 运行库 | .NET Framework 4.8（CLR 元数据版本 v4.0.30319） |
| 技术栈 | WPF（BAML/XAML）+ Windows Forms + VB 运行时 |
| 语言 | Visual Basic .NET（IL 保留 VB 编译器产物） |
| 发布通道 | `BuildTypes.Snapshot`（Debug=100 / Release=50 / Snapshot=0） |
| 版本常量 | `VersionBaseName = "2.14.0.0"`、`VersionCode = 412`、`VersionDisplay = "快照版 2.14.0.0"`（`src/PCL/ModBase.cs:572-582`） |
| 分支标识 | `VersionBranchMain = "Official"`（`src/PCL/ModSecret.cs:29`） |

程序集属性（`src/Properties/AssemblyInfo.cs`）：`RootNamespace = "PCL"`，`AssemblyConfiguration = "PCL2 Config Mark"`，`Guid = fc6bb5fb-fe5c-4b25-84d7-2effb821c4bd`，另有遗留特性 `[AssemblyAssociatedContentFile("maskfiles/plain craft launcher 2.sln")]`（实际无该文件）。

---

## 2. PE 与 CLR 结构

```text
PE32 (optional header magic 0x10b, machine 0x14c)
  .text   VA 0x00002000  VSize 0x005851E8  RawPtr 0x00000200  RawSize 0x00585200
  .rsrc   VA 0x00588000  VSize 0x00032000  RawPtr 0x00585400
  .reloc  VA 0x005BA000
  CLR header RVA = 0x00002008
```

- 元数据流：`#~`（345,972 字节）、`#Strings`、`#US`、`#GUID`、`#Blob`；**没有 `#Pdb`**（无调试符号），也没有任何加密/压缩流。
- Win32 资源区只有 `RT_ICON(3)`、`RT_GROUP_ICON(14)`、`RT_VERSION(16)`、`RT_MANIFEST(24)`，**没有 `RT_RCDATA(10)`** —— 内嵌 DLL 全在托管资源里。
- 托管资源表：`Plain Craft Launcher 2.g.resources` 773,756 字节 / 113 项；`PCL.Resources.resources` 3,476,322 字节 / 18 项。
- 反射统计：**708 个类型**（含编译器生成的闭包、状态机、委托）。

### 2.1 单文件打包机制

1. `PCL.Resources.resx` 内有 18 个 `System.Byte[]` 条目（base64 内嵌）；
2. 启动时（`src/PCL/Application.xaml.cs:548`）挂 `AppDomain.AssemblyResolve`，按 13 个前缀匹配程序集名：`NAudio`、`Newtonsoft.Json`、`Ookii.Dialogs.Wpf`、`Imazen.WebP`、`CacheCow.Common`、`CacheCow.Client.FileStore`、`CacheCow.Client`、`ThrottleDebounce`、`System.Net.Http.Formatting`、`Microsoft.Win32.Registry`、`PCLCS`、`MeloongCore.Wpf`、`MeloongCore`；
3. 命中后 `Assembly.Load((byte[])Resources.ResourceManager.GetObject(p))`，并用 `ConcurrentDictionary<string, Lazy<Assembly>>` 去重；
4. 加载 `Imazen.WebP` 时顺带 `ExtractLibwebp()`（`:563`）：把 `libwebp64` 写成程序目录下的 `libwebp.dll`，再 `SetDllDirectoryA` 让原生库找到附属 DLL。
---

## 3. 反编译方法与工具链

### 3.1 反编译

```bash
dotnet tool install -g ilspycmd            # 得到 11.2.0.10050（ICSharpCode.Decompiler 11.2）
ilspycmd -p --nested-directories -o <outdir> "plain craft launcher 2.exe"
```

- ILSpy 11.x 只输出 C#（无 VB 后端），对 VB 源程序的还原度约 90%，剩余部分需要手工修（第 12 节）。
- 输出：137 个非空 `.cs`、88,681 行、61 个 `.xaml`、26 个目录。最大文件 `PCL/ModDownloadLib.cs` 169 KB。
- **坑**：`[CompilerGenerated]` 的委托类型（`VB$AnonymousDelegate_*`）用 `-t` 查询返回空且不报错，`-p` 模式会生成 **0 字节 .cs 占位文件**（本样本 28 个）。必须 `ilspycmd -il -t` 才能拿到真实签名。

### 3.2 资源提取

托管资源不能靠 PE 资源遍历拿：`g.resources` 在 ManifestResource 表里 `Offset = 0`，`PCL.Resources.resources` 的 `Offset = 0xBCE80` 处 4 字节长度前缀之后也不是 `.resources` 魔数。最终方案是**只反射加载、不执行目标代码**：

```powershell
$asm    = [System.Reflection.Assembly]::LoadFrom($exe)
$stream = $asm.GetManifestResourceStream("<name>")
$reader = New-Object System.Resources.ResourceReader($stream)
# 逐项读取：byte[] 直接落盘，string（BAML）另存
```

`tools/extract-assets.ps1` 就是这个脚本；`tools/resdump/` 是自写的 PE 元数据读取器（用于取证，最终未用于解包）。

### 3.3 源码里泄漏的原始工程路径

每个 `Logger.*` 调用都把 VB 源文件路径作为字符串常量编进去了（VB 的 `CallerFilePath` 特性），共 **80 个唯一 `.vb` 路径**，例如：

```text
E:\Projects\PCL\本体代码\Plain Craft Launcher 2\Modules\Base\ModLoader.vb
E:\Projects\PCL\本体代码\Plain Craft Launcher 2\Modules\Minecraft\ModDownload.vb
E:\Projects\PCL\本体代码\Plain Craft Launcher 2\Pages\PageSetup\Settings.vb
E:\Projects\PCL\本体代码\Plain Craft Launcher 2\Application.xaml.vb
```

据此可还原原始目录结构（ILSpy 输出是平铺的）：`Modules/Base`、`Modules/Minecraft`、`Modules/Resource`、`Pages/PageLaunch`、`Pages/PageSetup`、`Controls/Behaviors` 等。

---

## 4. 保护与安全性评估

### 4.1 无保护

- 节表正常（`.text/.rsrc/.reloc`），无附加壳节、无高熵数据节；
- `#US` 流里的 UI 文案（如「正在检查更新，请稍候！」）明文可读；
- 无 `SuppressIldasm`、无 `ConfusedByAttribute`、无 Eazfuscator 字符串解密桩；
- 无 `Debugger.IsAttached` 之类的反调试分支（全树 0 命中）；
- 无自校验（不读取自身哈希或签名）。

### 4.2 签名与权限

- **Authenticode：未签名**。因此不存在「证书被篡改」的检测点，更新完全信任 HTTPS 通道 + 服务器下发的 JSON。
- 应用清单（`src/app.manifest`，从 PE `RT_MANIFEST` 提取）：`<requestedExecutionLevel level="asInvoker" uiAccess="false" />`，不请求管理员。
- 运行期若检测到目录不可写，会提示「以管理员身份运行」并 `Environment.Exit(4)`。

### 4.3 硬编码的敏感常量

| 常量 | 值 | 位置 |
| --- | --- | --- |
| 微软 OAuth ClientId | `fe72edc2-3a6f-4280-90e8-e2beb64ce7e1` | `src/PCL/ModSecret.cs:33` |
| 遥测上报地址 | `https://ap-shanghai.cls.tencentcs.com/track?topic_id=pcl-remote-1253424809` | `src/PCL/ModSecret.cs:35` |
| 远端配置 | `https://pcl2-server-1253424809.file.myqcloud.com/ServerConfig.json` | `src/PCL/ModSecret.cs` |
| 公告 | `notice.cfg{CDN}`、`notice.json{CDN}`（同域） | `src/PCL/ModSecret.cs` |
| 伪造 Referer | `http://{412}.pcl2.server/`（412 为内部版本号） | `src/PCL/ModSecret.cs:229` |

> OAuth ClientId 对公共客户端（native app）本身不算机密，但配套的 token 刷新与账户缓存（`LoginMsJson`）落在本地注册表/配置文件里，值得注意。
---

## 5. 程序结构

### 5.1 工程布局（还原后）

```text
src/
  PCL/                       138 个 .cs（其中顶层 132 个）+ 61 个 .xaml（反编译后平铺到命名空间根）
    Application.xaml(.cs)    WPF 入口，全局异常/启动流程
    FormMain.xaml(.cs)       主窗口（97 KB 代码 + 98 KB 的 XAML 逻辑）
    Mod*.cs                  16 个功能模块（见下表）
    Page*.xaml               39 个页面
    My*.xaml                 21 个自绘控件
    VBAnonymousDelegates.cs  【新增】补齐 ILSpy 丢弃的 27 个 VB 匿名委托
    OneOfHelper.cs           【新增】绕开 C# 对「涉及接口类型的用户自定义转换」的限制
    My/                      VB 的 My 命名空间（MyProject / MySettingsProperty / MyWpfExtension）
    Properties/AssemblyInfo.cs
    XamlGeneratedNamespace/GeneratedInternalTypeHelper.cs
  images/                    从 g.resources 还原的 48 png + 2 jpg + ico + ttf
  resources/                 字体与其它资源
  libs/                      13 个托管依赖 DLL + libwebp64.dll（原生）+ 4 个 ZIP/XML 数据包
  PCL.Resources.resx         4.76 MB，内嵌全部 DLL 的 base64
  app.manifest / app.ico     从 PE 资源区提取
```

### 5.2 功能模块

| 模块 | 行数 | 职责 |
| --- | ---: | --- |
| `ModDownloadLib.cs` | 3,802 | 各类下载源的具体实现、安装清单 `McInstallManifest`、文件校验 |
| `ModNet.cs` | 3,322 | HTTP 栈（`NetRequest*`、代理、重试、镜像替换）、下载器、`NetFile` 模型 |
| `ModMain.cs` | 1,768 | 消息框、线程调度、遥测 `Telemetry()`、GPU 偏好设置、主题 |
| `ModSecret.cs` | 1,755 | 服务器常量、更新检查/替换、公告、遥测地址、隐藏彩蛋（`InputPotatoCode`） |
| `ModDownload.cs` | 1,698 | 下载页总控（资源 / 模组 / 整合包 / 服务端） |
| `CrashAnalyzer.cs` | 1,667 | 崩溃报告解析与归因 |
| `ModBase.cs` | 1,424 | 全局常量（版本、颜色、路径）、`MyColor` 等值类型、权限与时间工具 |
| `ModAnimation.cs` | 1,361 | 动画与界面过渡 |
| `ModModpack.cs` | 1,298 | 整合包导入/导出与格式转换 |
| `ModLoader.cs` | 1,217 | `LoaderTask<TIn,TOut>` / `LoaderCombo` 任务流水线、进度 UI |
| `LocalResourceLoaders.cs` | 1,063 | 从资源加载图片/字体/声音 |
| `ModJava.cs` | 965 | 本机 Java 扫描与版本识别 |
| `ResourceSearcher.cs` | 828 | 跨平台资源搜索（CurseForge / Modrinth / 百科） |
| `ModWatcher.cs` | 609 | 游戏进程与日志监视（崩溃捕获入口） |
| `ModMusic.cs` | 456 | 背景音乐播放（NAudio） |
| `ModValidate.cs` | 26 | 名称合法性校验规则 |
| `ModDevelop.cs` | 8 | 开发者模式开关 |
### 5.3 页面分组

- **下载**（8）：`PageDownloadLeft`、`PageDownloadInstall`、`PageDownloadMod`、`PageDownloadPack`、`PageDownloadDataPack`、`PageDownloadResourcePack`、`PageDownloadShader`、`PageDownloadResourceDetail`
- **实例**（6）：`PageInstanceLeft`、`PageInstanceOverall`、`PageInstanceSetup`、`PageInstanceMod`、`PageInstanceModDisabled`、`PageInstanceExport`
- **启动 / 登录 / 联机**（11）：`PageLaunchLeft`、`PageLaunchRight`、`PageLoginLegacy`、`PageLoginNide`、`PageLoginAuth`、`PageLoginMs`（各带 `*Skin` 皮肤变体）、`PageLinkMain`
- **设置 / 其它**（9）：`PageSetupLaunch`、`PageSetupSystem`、`PageSetupUI`、`PageSetupLink`、`PageSetupLeft`、`PageOtherLeft`、`PageOtherAbout`、`PageOtherHelp`、`PageOtherHelpDetail`、`PageOtherTest`
- **通用**（4）：`PageResource`、`PageSelectLeft`、`PageSelectRight`、`PageSpeedLeft`、`PageSpeedRight`

### 5.4 依赖库（`libs/`，全部来自内嵌资源）

| DLL | 字节 | 用途 |
| --- | --- | --- |
| `PCLCS.dll` | 1,102,336 | PCL 自有核心库（`McInstallManifest`、`McComponents` 等模型） |
| `Newtonsoft.Json.dll` | 711,952 | JSON |
| `NAudio.dll` | 471,040 | 音频（音乐播放） |
| `libwebp64.dll` | 457,728 | **原生** WebP 解码（启动时释放到程序目录） |
| `System.Net.Http.Formatting.dll` | 179,680 | HTTP 格式化 |
| `MeloongCore.dll` | 147,968 | 作者自研基础库（`BaseLogger`、`Paths`、`Setting`、`OneOf<,>`、`ProgressProvider`） |
| `Ookii.Dialogs.Wpf.dll` | 105,472 | Vista 风格文件对话框 |
| `Help.dll` | 78,235 | 帮助文档（ZIP） |
| `ThrottleDebounce.dll` | 46,592 | 节流/防抖 |
| `CacheCow.Client.dll` | 37,376 | HTTP 缓存 |
| `MeloongCore.Wpf.dll` | 34,304 | WPF 扩展 |
| `Custom.dll` | 28,188 | 自定义控件（XML 片段） |
| `Microsoft.Win32.Registry.dll` | 26,496 | 注册表封装 |
| `Imazen.WebP.dll` | 18,944 | WebP 托管封装 |
| `CacheCow.Common.dll` / `CacheCow.Client.FileStore.dll` | 13,824 / 9,728 | 缓存后端 |
| `ForgeInstaller.dll` | 3,222 | Forge 安装器（ZIP） |
| `Transformer.dll` | 2,243 | 代码/字节码转换（ZIP） |
---

## 6. 启动流程

入口是 `App.xaml`（`StartupUri="FormMain.xaml"`、`ShutdownMode="OnExplicitShutdown"`），逻辑在 `src/PCL/Application.xaml.cs:57 Application_Startup`。按执行顺序：

| # | 步骤 | 代码位置 |
| --- | --- | --- |
| 1 | `MeloongCore.Main.Init("PCL")`：初始化 `Paths.Base` / `BaseThenName` / `AppDataThenName` 与配置库 | `:63` |
| 2 | `MeloongCore.Wpf.Main.Init()`：WPF 侧初始化（DPI、主题、全局样式） | `:65` |
| 3 | 单实例互斥：`WaitingMutex(e)`（命令行带 `--update/--gpu/--memory` 时跳过，允许和主实例并存） | `:69` |
| 4 | 日志：`new PclLogger { logFolder = Paths.BaseThenName }`，`MinLevel = ModeDebug ? Trace : Info`，赋给 `Logger.Instance` | `:71-76` |
| 5 | `Thread.CurrentThread.Priority = ThreadPriority.Highest` | `:79` |
| 6 | .NET Framework 检测：注册表 `HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full\Release` 小于 **528040**（4.8）→ `OldEnvironmentAssert()` 引导安装 | `:83` |
| 7 | 字体探测：构造 `FormattedText`；捕获 `UriFormatException` 说明 `windir` 被改坏，重置为 `SystemRoot` 后重试 | `:97-104` |
| 8 | 命令行分支：`--update <code> <path> <newPath> <restart>` → `ModSecret.UpdateReplace` 后 `Environment.Exit(5)`；`--gpu <exe>` → `SetGPUPreference`；`--memory` → 内存优化，把释放量作为退出码 | `:107-152` |
| 9 | 建立 `PCL\Pictures\`、`PCL\Musics\`，`CheckPermissionWithException(PCL\)`；失败弹「PCL 没有对当前文件夹的权限」并 `Environment.Exit(4)` | `:156-167` |
| 10 | 缓存目录：`ModBase.PathTemp` 不可用时回退 `%TEMP%\PCL\` 并清空设置项，然后建 `Cache\` | `:170-195` |
| 11 | 网络全局调优：`Expect100Continue=false`、`DefaultConnectionLimit=10000`、`UseNagleAlgorithm=false`、`EnableDnsRoundRobin=true`、`ReusePort=true`；仅当 OS < 6.2 才显式启用 TLS 1.2 | `:198-208` |
| 12 | 启动画面：`Settings.Get<bool>("UiLauncherLogo")` 为真则 `new SplashScreen("Images\\icon.ico")` 置顶显示 | `:209-213` |
| 13 | 第一段日志：版本、识别码、程序路径、系统编码、管理员权限；若程序在 Temp 目录提示「请先解压」并退出；32 位系统直接劝退；记录「第一阶段加载用时」 | `:214-231` |
| 14 | `StartupUri` 创建 `FormMain` → 触发加载器流水线（Minecraft Folder List / Instance List / Loader Skin Ms / 登录），完成第二、三阶段加载 | `FormMain.xaml.cs` |

实测日志（重建版，`PCL\Log1.txt`）：`第一阶段加载用时：424 ms`、`第三阶段加载用时：179 ms`，随后请求 `launchermeta.mojang.com/mc/game/version_manifest.json`、上报「匿名数据上报：启动」、读取 `ServerConfig` 缓存与公告。

`App.xaml` 的全局资源：`corewpf:AdditionConverter/MultiplicationConverter/InverseBooleanToVisibilityConverter/InverseBooleanConverter`；主色 `ColorBrush1..8`（`#343d4a`、`#0b5bcb`、`#1370f3`、`#4890f5`、`#96c0f9`、`#d5e6fd`、`#e0eafd`、`#eaf2fe`）、背景 `ColorBrushBg0/Bg1`、灰色系 `ColorBrushGray1..8`（`#404040`…`#f5f5f5`）。

---

## 7. 网络与认证

### 7.1 服务端点

| 类别 | 端点 |
| --- | --- |
| Mojang 官方 | `piston-meta.mojang.com`、`piston-data.mojang.com`、`launchermeta.mojang.com/mc/game/version_manifest.json`、`libraries.minecraft.net`、`resources.download.minecraft.net` |
| BMCLAPI 镜像 | `bmclapi2.bangbang93.com`（`/maven`、`/libraries`、`/assets`、`/optifine/`、`/forge/promos`、`/fabric-meta/`、`/neoforge/`、`/mirrors/authlib-injector`） |
| Forge / NeoForge | `files.minecraftforge.net`、`maven.neoforged.net/releases/...`、`maven.neoforged.net/api/maven/versions/releases/...` |
| Fabric / LiteLoader | `meta.fabricmc.net/v2/versions`、`dl.liteloader.com/versions/versions.json` |
| OptiFine | `optifine.net`（`adloadx` 通道）、`optifine.net/changelog?f=` |
| 模组站 | `api.modrinth.com/v2/{projects,version_files,search,project}`、`api.curseforge.com/v1/{mods,mods/files,fingerprints/432}`、`www.mcmod.cn` |
| 微软登录 | `login.microsoftonline.com/consumers/oauth2/v2.0/{devicecode,token}`、`login.live.com/oauth20_token.srf`、`user.auth.xboxlive.com/user/authenticate`、`xsts.auth.xboxlive.com/xsts/authorize` |
| 第三方验证 | `auth.mc-user.com:233/{server}/authserver`、`login.mc-user.com:233`、`littleskin.cn/api/yggdrasil`、`authlib-injector.yushi.moe/artifact/latest.json` |
| 自有后端 | `ap-shanghai.cls.tencentcs.com/track?topic_id=pcl-remote-1253424809`（遥测）、`pcl2-server-1253424809.file.myqcloud.com/{ServerConfig.json,notice.cfg,notice.json}` |
| 其它 | `weather-p.kaphia.top`、`wallpaper.p.kaphia.top`、`uptime.easytier.cn/api/nodes`、`meloong.com/afd/...`、`github.com/Meloong-Git/PCL/issues/` |

### 7.2 登录方式（`src/PCL/ModLaunch.cs:49`）

```csharp
public enum McLoginType { Legacy = 0, Nide = 2, Auth = 3, Ms = 5 }
```

| 方式 | 流程 |
| --- | --- |
| Legacy（离线） | 无网络，用户名 + 本地 UUID |
| Nide（统一通行证） | `auth.mc-user.com:233/{server}/authserver`；启动游戏时挂 `-javaagent:authlib-injector.jar="<url>"` 并预置 `-Dauthlibinjector.yggdrasil.prefetched=<base64(api metadata)>` |
| Auth（第三方 Yggdrasil） | 支持 `authlib-injector:yggdrasil-server:<urlencoded>` 深链一键填充；未知站点按 `api/yggdrasil` → `auth/register` 推导注册地址 |
| Ms（微软） | **OAuth 2.0 设备码流程**：`devicecode` 取 `user_code` → 轮询 `oauth2/v2.0/token`（`grant_type=urn:ietf:params:oauth:grant-type:device_code`）→ `login.live.com/oauth20_token.srf` 换 Xbox Live token（`scope=XboxLive.signin`，刷新时带 `client_id`）→ `user.auth.xboxlive.com/user/authenticate`（`AuthMethod: RPS`）→ `xsts.auth.xboxlive.com/xsts/authorize` → Minecraft 服务链 |

`src/PCL/MyMsgLogin.xaml.cs:457` 也直接访问 `login.microsoftonline.com/consumers/oauth2/v2.0/token` 做设备码换票；`src/PCL/ModNet.cs:3319` 对 `xboxlive` / `minecraftservices` 域名有单独的开关判断（`&& !Enabled`）。
---

## 8. 遥测与隐私

### 8.1 实现（`src/PCL/ModMain.cs:1613`）

```csharp
public static void Telemetry(string Event, params string[] Datas)
{
    if (!Settings.Get<bool>("SystemSystemTelemetry") || !StartsWithF(ClsBaseUrl, "http", false)) return;
    RunInNewThread(() => {
        string text = string.Format("{0}&Event={1}", ClsBaseUrl, StringUtils.UrlEscape(Event));
        for (int i = 0; i <= Datas.Length - 1; i += 2)
            text += "&" + UrlEscape(Datas[i]) + "=" + UrlEscape(ReplaceLineEndings(Datas[i + 1], "\n", true));
        ModNet.NetRequestByClient(text, null, null, null, "*/*", 25000, null,
            RequireJson: false, null, SimulateBrowserHeaders: false, MakeLog: false);
    }, "Telemetry", ThreadPriority.Lowest);
}
```

- 传输方式：**GET 查询串**，直接拼在地址上（URL 长度受服务端限制，故调用点的 `Datas` 都控制在短文本内）；
- 开关：`Settings.cs:171` `new Setting("SystemSystemTelemetry", true, Sources.Registry)` —— **默认开启**，用户需在设置页手动关闭；
- 线程：`RunInNewThread(..., "Telemetry", ThreadPriority.Lowest)`，fire-and-forget，不阻塞 UI；
- 日志：调用即写 `匿名数据上报：{Event}`，失败只 `Logger.Warn(..., "匿名数据上报失败")`，不打断用户操作；
- `MakeLog: false` 表示该请求不进网络日志（用户在「网络日志」里看不到遥测请求）。

### 8.2 上报事件

| 事件 | 附带字段 | 位置 |
| --- | --- | --- |
| `启动` | — | `FormMain.xaml.cs:1506` |
| `Minecraft 崩溃` | `Version`、`Detail`（崩溃摘要，经 `ModMinecraft.FilterUserName` 脱敏） | `CrashAnalyzer.cs:1376` |
| `Minecraft 启动成功` / `Minecraft 启动失败` | `Version`、`LoginType`、`Exception` | `ModLaunch.cs:409` / `:451` |
| `文件下载失败` | `FileName`、`Exception` | `ModNet.cs:2285` |
| `错误日志` | `Exception`（原始异常文本） | `PclLogger.cs:125` |
| `联机失败` / `联机开始` / `联机成功` / `联机失败` | `Server`、`NAT` | `PageLinkMain.xaml.cs:750/1160/1259/1517` |

### 8.3 隐私评估

- 上报内容**只由调用点显式传入**（事件名 + 键值对）。通读全部 11 个调用点，未发现自动附加机器指纹、账户名或文件列表的代码路径；机器识别码 `ModBase.Identify` 只写进本地日志。
- 但 `错误日志` / `Minecraft 崩溃` 事件会把异常文本（可能包含本地绝对路径、模组名、部分用户名）发到腾讯云 CLS；崩溃详情走 `FilterUserName` 脱敏，错误日志没有。
- 端点域名是 `*.tencentcs.com`（腾讯云 CLS），数据落地方为境内服务器；程序内没有可配置遥测端点的入口。

---

## 9. 更新机制

### 9.1 手动检查（`src/PCL/ModSecret.cs:742 UpdateCheckByButton`）

1. 静态标志 `IsUiCheckingUpdate` 互斥，重复点击只提示「正在检查更新，请稍候！」；
2. 后台线程里 `ServerLoader.WaitForExit(null, null, IsForceRestart: true)` —— 先等远端配置（`ServerConfig.json`）拉完；
3. `PageSetupSystem.IsLauncherNewest()` 三态：
   - `null`（连不上）→ 红字提示「连接 PCL 服务器失败，请确认系统时间是否准确，尝试将 PCL 加入杀毒软件或防火墙白名单…」；
   - `true` → 绿字「已经是最新快照版 2.14.0.0」；
   - `false` → 若 `!HasUpdatePermission` 则先 `InputPotatoCode(IsUpdating: true)`（内部测试用的更新许可码），否则弹「发现启动器更新，是否立即下载？」；确认后 `UpdateStart(Silent: false)`。

### 9.2 下载与安装（`src/PCL/ModSecret.cs:822 UpdateStart`）

```text
IsUpdating && IsUpdateWaitingRestart  →  UpdateRestart(TriggerRestartAndByEnd: true)
否则 LoaderCombo<string>("启动器更新") 三步：
  ① LoaderTask<string, List<NetFile>>("获取更新信息")
       UrlsOverride 优先；否则读 ServerConfig["Update"][BuildTypes.Snapshot]
       含 PatchUrls 且 UpdateMark 以 "Empty" 开头 → 走增量；否则 DownloadUrls
  ② ModNet.LoaderDownload("下载更新文件")
  ③ LoaderTask<string, int>("安装更新")
```

- 日志：`更新开始，静默：{0}，扩展名：{1}，下载目标：{2}，URL：{3}`；
- 下载目标：`{ModMain.RequestTaskTempFolder()}2.14.0.0.0{扩展名}`；`.zip` 走解压分支；
- 安装前会先删掉 `Paths.Base + "PCL\Plain Craft Launcher 2.exe"`（若残留）；
- 替换自己时用 `--update` 子进程模式：`Application_Startup` 里 `ModSecret.UpdateReplace(Code, Path, NewPath, Restart)` 后 `Environment.Exit(5)`，由新进程覆盖旧 exe 并（可选）重启；
- 入口：设置页「检查更新」按钮、关于页、左侧栏菜单（`CustomEvent.cs:363`、`PageOtherAbout.xaml.cs:198`、`PageOtherLeft.xaml.cs:351`），以及 `PageSetupSystem.xaml.cs:58-59` 的 `CheckUpdateRelease` 自动检查开关。

### 9.3 风险点

- 更新包**没有签名校验**：`ServerConfig.json` 下发的 URL 列表即最终执行依据，程序只做 HTTPS 传输与（`ModNet`）基本的大小/哈希校验；
- `{CDN}` 占位符替换（`ModSecret.cs:192-196`）允许服务端在运行时切换镜像域名；
- 因此这条链路的信任基础完全落在 `pcl2-server-1253424809.file.myqcloud.com` 的 TLS 与腾讯云账号安全上。
---

## 10. 资源提取结果

### 10.1 `Plain Craft Launcher 2.g.resources`（773,756 字节 / 113 项）

| 类型 | 数量 | 说明 |
| --- | ---: | --- |
| `.baml` | 61 | 编译后的 XAML（`application.baml` 15,266、`formmain.baml` 14,921，其余为页面/控件） |
| `.png` | 48 | 界面图片、图标、阴影、Logo |
| `.jpg` | 2 | 背景图 |
| `.ico` | 1 | 主图标（194.8 KB，多尺寸） |
| `.ttf` | 1 | 界面字体 |

提取后按原相对路径还原到 `src/images/`、`src/resources/`，WPF 的 `Resource` 引用因此可以直接工作（这也是「界面能原样显示」的前提）。

### 10.2 `PCL.Resources.resources`（3,476,322 字节 / 18 项）

| 分类 | 条目 |
| --- | --- |
| 托管依赖（13） | `PCLCS`、`MeloongCore`、`MeloongCore.Wpf`、`Newtonsoft.Json`、`NAudio`、`System.Net.Http.Formatting`、`Ookii.Dialogs.Wpf`、`ThrottleDebounce`、`CacheCow.Client`、`CacheCow.Client.FileStore`、`CacheCow.Common`、`Microsoft.Win32.Registry`、`Imazen.WebP` |
| 原生（1） | `libwebp64`（457,728 字节，启动时落盘为 `libwebp.dll`） |
| 数据包（4） | `Help`（78,235，实为 ZIP）、`ForgeInstaller`（3,222，ZIP）、`Transformer`（2,243，ZIP）、`Custom`（28,188，实为以 `<!--` 开头的 XML 片段） |

> 注意 `Help` / `ForgeInstaller` / `Transformer` 的扩展名虽是 `.dll`，内容其实是 ZIP；`Custom.dll` 是 XML。判断依据是条目首字节（`PK\x03\x04` / `<?xml` / `<!--`）。

---

## 11. 还原工程与编译

### 11.1 工程改造

ILSpy 的 `-p` 输出是一个 SDK 风格 csproj，但要能真正编译，做了这些改造：

| 改造 | 说明 |
| --- | --- |
| `TargetFramework` | `net48`（本机无 4.8 引用程序集目录，因此加 `PackageReference Microsoft.NETFramework.ReferenceAssemblies 1.0.3`，`PrivateAssets=All`） |
| `UseWPF` + `UseWindowsForms` | 同时开启（主窗口是 WPF，但大量代码调用 WinForms 的 `Interaction.MsgBox`） |
| `LangVersion` | `14.0`（源码用到较新的语法糖） |
| 14 个 `Reference` | 改为带 `HintPath` 指向 `libs\<name>.dll` |
| 资源嵌入 | `PCL.Resources.resx` + `images/` + `resources/` 保留为 `EmbeddedResource` / `Resource` |
| 图标与清单 | `ApplicationIcon = app.ico`、`ApplicationManifest = app.manifest`（均从 PE 提取） |

### 11.2 编译结果

```text
cd PCL2-REV\src
dotnet build -c Release -v q -nologo
    256 个警告
    0 个错误
    已用时间 00:00:12.66
```

- 产物：`src/bin/Release/net48/Plain Craft Launcher 2.exe`，**单文件**（纯反编译复原时 5,974,528 字节、原版 5,993,984；加上本报告第 16 节的服务端模式后为 6,055,936 字节）；程序集名、版本 2.14.0.0、`FileDescription` 等属性与原版一致；
- **不输出任何依赖 DLL**：csproj 中 13 个托管程序集引用都写作 `<Reference><HintPath>libs\*.dll</HintPath><Private>false</Private></Reference>`，`Private=false` 等于旧式项目的 CopyLocal=false，只用于编译期解析类型；运行时改由 `Application..ctor` 注册的 `AppDomain.AssemblyResolve` 用 `Assembly.Load((byte[])PCL.My.Resources.Resources.ResourceManager.GetObject(名称))` 从内嵌的 `PCL.Resources.resources` 取出（前缀表 13 项：NAudio、Newtonsoft.Json、Ookii.Dialogs.Wpf、Imazen.WebP、CacheCow.Common、CacheCow.Client.FileStore、CacheCow.Client、ThrottleDebounce、System.Net.Http.Formatting、Microsoft.Win32.Registry、PCLCS、MeloongCore.Wpf、MeloongCore）。若把 `Private` 写回 `true`，MSBuild 就会把 13 个 DLL 复制到输出目录——这就是「重建版多出一堆 DLL」的唯一原因，程序本身并不需要它们；
- 原生库 `libwebp64` 不参与引用，由 `ExtractLibwebp()`（Application.xaml.cs:563）在 `Imazen.WebP` 被加载时写成程序目录的 `libwebp.dll` 并 `SetDllDirectoryA`，原版行为相同；
- 反射统计产物类型数：**708**，manifest resource 只有 `Plain Craft Launcher 2.g.resources` 与 `PCL.Resources.resources` 两项（与原版相同）——说明单文件打包结构被完整还原。

### 11.3 与原版的差异量

`diff -rq --strip-trailing-cr`（ILSpy 原始输出 vs 最终工程）：**298 个文件有改动**（237 个 `.cs` + 61 个 `.xaml`），合计 **+7,386 / −13,496 行**。行数净减少主要来自删掉的 VB `WithEvents` 属性样板（818 个）。

### 11.4 规模

| 指标 | 数值 |
| --- | ---: |
| `.cs` 文件 | 139（含新增 2 个） |
| `.xaml` 文件 | 61 |
| `.cs` 总行数 | 82,610 |
| 最大单文件 | `ModDownloadLib.cs` 3,802 行 |
---

## 12. 反编译坑与修复清单

错误数量轨迹：`1754 → 258（首轮修完掩盖的错误暴露出来）→ 80 → 35 → 0`。下表按根因归类。

| # | 现象（错误码 / 数量） | 根因 | 处理 |
| --- | --- | --- | --- |
| 1 | `CS0102` 1636 + `CS0111` 98 | VB 的 `Friend WithEvents X As T` 被 ILSpy 展开成私有字段 `_X` + `X` 属性（setter 里挂钩事件），而 C# 标记编译器也会为 `x:Name="X"` 生成 `internal T X` 字段 → 重名 | 源码变换：删掉 818 个属性，改成 `Hook_X(T value)`（体为原 setter）并在构造函数 `InitializeComponent();` 之后调用 `Hook_X(X)`，让 `X` 解析到标记编译器生成的字段 |
| 2 | `MC1000` 10 | ILSpy 给本程序集内的类型写了 `clr-namespace:PCL;assembly=Plain Craft Launcher 2`，WPF 标记编译器不接受带空格/RVA 的程序集名限定 | 删掉 `;assembly=...` 后缀（另有 2 处空的 `;assembly=` 同样清理） |
| 3 | `CS0111` 49 | 标记编译器为每个带事件的 XAML 生成 `_CreateDelegate`，源码里也有一份同名同参方法 | 删除源码副本，并清掉因此悬空的 `[DebuggerNonUserCode]` / `[GeneratedCode]`（否则 `CS1519` 94 条） |
| 4 | `CS0246` 6 | ILSpy **整类丢弃** `[CompilerGenerated]` 委托，只留下 0 字节占位 `.cs`；而源码里 27 个 `VB$AnonymousDelegate_N` 都被引用 | `ilspycmd -il -t` 逐个取签名，生成 `PCL/VBAnonymousDelegates.cs`（27 个 `internal delegate`） |
| 5 | `CS0246` 32 | 缺失类型其实是**内嵌 DLL 里的嵌套类型**：`ManagedFile`=`PCLCS.McInstallManifest+ManagedFile`、`HashMethod`=`MeloongCore.CryptographyUtils+HashMethod`、`ChildrenAction`=`MeloongCore.ProgressProvider+ChildrenAction`、`OneOf<,>`=`MeloongCore.OneOf\`2`、`RefAction<T>` 存在于 `ConfigEntry\`1` 与 `DynamicConfigEntry\`1` | 加 `using X = 外层.嵌套;` 别名或写全名 |
| 6 | `CS0571` 155 + `CS0030` 7 | C# 不允许显式调用用户定义的 `implicit operator`；且转换涉及接口类型（`IEnumerable<(string,string)>`）时一律禁止 | `JToken.op_Implicit(x)` → `(JToken)x`；`OneOf<,>` 的 7 处接口实参改用泛型辅助 `OneOfHelper.From<T1,T2>(x)`（泛型参数位置不算接口，转换合法） |
| 7 | `CS1593` 32 | VB 的「宽松委托转换」允许 0 参 lambda 绑定到带参委托，ILSpy 输出 `[SpecialName] () => {...}` | 按委托签名补参数 `(object a0, EventArgs a1)` 等（29 处脚本 + 3 处手改） |
| 8 | `CS0165` 15 | VB 局部变量隐式默认值；闭包类被反编译成自引用构造 `T v = new T(v);`（IL 实为 `ldloc.0` 后 `newobj`，`null` 合法） | 声明处初始化；自引用改 `new T((T)null)`；递归 lambda 先声明 `= null` 再赋值 |
| 9 | `CS1106` 2 | VB `Module`（`[StandardModule]`）里有扩展方法，C# 要求所在类 `static` | `public sealed class ModBase/ModMain` → `public static class` |
| 10 | `CS0542` 1 | `class Validate { public abstract string Validate(string Str); }` 成员名等于类型名 | 类改名 `ValidateBase`（26 个文件里的 28 处引用同步改） |
| 11 | `CS0103` 2 | `RuntimeHelpers.InitializeArray(arr, __ldtoken(_003CPrivateImplementationDetails_003E.X))` —— ILSpy 把 `ldtoken` 内联特征原样吐出 | 用**反射 + `RuntimeHelpers.InitializeArray`** 从原 exe 解出数组初值，写成显式数组（`ModLoaders[3]{Fabric,Forge,NeoForge}`、`DonationRank[5]{98,54,23,12,6}`） |
| 12 | `CS0119` 2 | `Main.Init(...)` 被解析成本类 WPF 入口方法 `Application.Main()` | 写全 `MeloongCore.Main.Init` / `MeloongCore.Wpf.Main.Init` |
| 13 | `CS0104` 2 | `TaskExtensions` 在 `MeloongCore.Extensions` 与 `System.Threading.Tasks` 间歧义 | 全名限定 |
| 14 | `CS0266` 6 | `switch (ResourceTypes)` 的 `case 1:` 标签需要显式枚举 | `case (PCLCS.ResourceTypes)1:` |
| 15 | **运行时：启动即栈溢出** `0x800703E9` | `PclLogger.cs:22/40` 的 `((BaseLogger)this).Format(...)`：ILSpy 用父类转换表达 IL 的非虚 `call`，C# 编译出来是虚派发 → 调用自身 → 无限递归 | 改成 `base.Format(...)` / `base.HandleBehavior(...)`（已用反射确认 `FileLogger` 未 override 这两个方法，`base` 等价于原 `call`） |
| 16 | **运行时：`XamlParseException` → `InvalidCastException`** | `ModLoader.cs:344/371/596`：IL 里 `Func<TIn>` 直接 `stfld` 进 `Func<object>` 字段（无 cast），ILSpy 为过编译写成 `(Func<object>)(object)x`；`TIn = (string,string)` 时值类型泛型实例化必然转换失败 | 字段类型改 `Delegate`，读写处直接赋值/`(Func<TIn>)` 强转，等价原 unchecked 语义 |
| 17 | 假错误 6000+ 条 | 把反编译备份目录放进 `src\` 后被 `**/*.cs` 通配符一起编译 | 备份必须放在 `src` 之外（本工程放 `PCL2-REV\_backup_PCL`） |

---

## 13. 验证

### 13.1 编译验证（已通过）

```text
rm -rf obj/Release bin/Release && dotnet build -c Release -v q -nologo
  → 0 个错误 / 256 个警告 / 12.66 s
```

256 个警告绝大多数是 VB 遗留：未使用的私有字段（`CS0414`）、无法访问的代码、`CS0169` 等，不影响运行。

### 13.2 冒烟验证（已通过）

做法：把 `bin/Release/net48` 全量复制到一个干净目录，用 `Start-Process -FilePath <绝对路径> -WorkingDirectory <目录>` 启动，等待 20 秒后看进程是否存活并检查日志。

```text
[重建版] ALIVE pid=2320
[日志]   PCL\Log1.txt  65 行，0 条错误/异常记录
         程序版本：快照版 2.14.0.0 (412)
         识别码：XXXX-XXXX-XXXX-XXXX，未解锁反馈主题（已脱敏）
         系统编码：gb2312 (936, GBK=True)   管理员权限：False
         第一阶段加载用时：424 ms          第三阶段加载用时：179 ms
         窗口已置顶，位置：(233, 109), 900 x 550
         Minecraft Folder List / Instance List / Loader Skin Ms / 登录  全部 Finished
         https://launchermeta.mojang.com/mc/game/version_manifest.json  请求成功
         匿名数据上报：启动
```

对照项：原版 exe 在同条件下同样存活，并生成 `.minecraft\launcher_profiles.json`、`.minecraft\PCL.ini`、`PCL\Log1.txt`、`PCL\Setup.ini`；重建版生成相同的一组文件，首屏日志内容一致。

补充说明：最后一轮（干净重建后）冒烟时 `launchermeta.mojang.com` 在本机不可达（`curl` 同样超时），日志中出现 16 条 `W` 级网络警告（`SocketException` / `HttpRequestException`），`E` 级错误仍为 0，主窗体 `900 x 550` 正常显示 —— 属环境限制，非工程缺陷。

### 13.3 未验证 / 已知限制

- **未做完整游戏启动测试**：需要真实 Minecraft 账户与数 GB 下载，本次只验证到「版本清单请求成功、界面加载完成」。
- **未逐页点测**：61 个页面/控件只做了启动路径上的冒烟；`Hook_X` 变换把 818 个 `WithEvents` 属性改成构造函数里手动挂钩，理论上等价，但没有对每个控件逐一手动触发事件。
- **微软登录全流程未跑通**：设备码流程需要真实账户交互，仅验证了代码路径与端点正确。
- **32 位系统 / Windows 7 分支未验证**（代码里保留了相应判断）。
- 反编译不可避免的语义近似：VB 的数值溢出检查上下文、部分 `OneOf<,>` 隐式转换、`ProjectData` 错误处理块，行为与原版极为接近但不保证逐位一致。
---

## 14. 交付物与复现

### 14.1 目录

```text
PCL2-REV/
  REPORT.md                 本报告
  README.md                 索引与快速开始
  src/                      可编译工程（SDK 风格 csproj，net48）
    PCL/                    138 个 .cs + 61 个 .xaml
    images/  resources/     从 g.resources 还原的图片与字体
    libs/                   18 个内嵌依赖（含 libwebp64 原生库）
    PCL.Resources.resx      4.76 MB 内嵌资源
    app.manifest  app.ico   从 PE 资源区提取
    bin/Release/net48/      已构建产物（单文件 exe，当前 6,055,936 字节）
  libs/                     依赖库原样副本（18 个）
  assets/
    manifest/               两个 .resources 原始 blob
    unpacked/               113 + 18 项全部拆出（BAML、图片、DLL）
  tools/                    资源提取脚本 + PE 元数据读取器 + 构建/冒烟脚本
  _backup_PCL/              ILSpy 原始输出（对照用，不参与编译）
```

### 14.2 复现命令

```bash
# 1) 工具
dotnet tool install -g ilspycmd

# 2) 反编译
ilspycmd -p --nested-directories -o PCL2-src "plain craft launcher 2.exe"

# 3) 提取内嵌资源（只反射加载，不执行目标代码）
powershell -ExecutionPolicy Bypass -File tools/extract-assets.ps1

# 4) 编译
cd src && dotnet build -c Release

# 5) 冒烟
powershell -ExecutionPolicy Bypass -File tools/smoke-test.ps1
```

### 14.3 声明

本工程由反编译产物 + 人工修复构成，**仅用于学习、研究与兼容性验证**。程序著作权归 PCL 作者（Meloong-Git）与「成都瓜皮龙科技有限公司」所有，内嵌的第三方库（Newtonsoft.Json、NAudio、Ookii.Dialogs.Wpf、CacheCow、MeloongCore 等）各自遵循其原始许可。请勿再分发或商用。

---

## 15. 附录

### 15.1 枚举值（IL / 反射实证）

| 枚举 | 值 |
| --- | --- |
| `PCL.ModBase.BuildTypes` | `Debug = 100`、`Release = 50`、`Snapshot = 0` |
| `MeloongCore.LogLevel` | `Trace = 0`、`Info = 1`、`Warn = 2`、`Error = 3` |
| `MeloongCore.LogBehavior` | `None = 0`、`ToastIfDebug = 1`、`Toast = 2`、`Alert = 3`、`AlertThenFeedback = 4`、`AlertThenCrash = 5` |
| `PCLCS.ModLoaders` | `None = 0`、`Forge = 1`、`LiteLoader = 2`、`Fabric = 4`、`NeoForge = 16`、`All = 23` |
| `PCLCS.DonationRank` | `None = 0`、`Rank6 = 6`、`Rank12 = 12`、`Rank23 = 23`、`Rank54 = 54`、`Rank98 = 98` |
| `PCL.McLoginType` | `Legacy = 0`、`Nide = 2`、`Auth = 3`、`Ms = 5` |
| `PCLCS.CryptographyUtils.HashMethod` | `MD5 = 0`、`SHA1 = 1`（由调用点推知） |

### 15.2 关键常量

| 名称 | 值 |
| --- | --- |
| `VersionBaseName` | `2.14.0.0` |
| `VersionCode` | `412` |
| `VersionDisplay` | `快照版 2.14.0.0` |
| `VersionBranchMain` | `Official` |
| `RegFolder` | `PCL` |
| `OAuthClientId` | `fe72edc2-3a6f-4280-90e8-e2beb64ce7e1` |
| `ClsBaseUrl` | `https://ap-shanghai.cls.tencentcs.com/track?topic_id=pcl-remote-1253424809` |
| 缓存目录 | `%TEMP%\PCL\`（用户可改，失败回退） |
| 日志目录 | `<程序目录>\PCL\Log1.txt` |

### 15.3 三类「IL → C#」语义陷阱（复现同类逆向时先查这三条）

| IL | ILSpy 输出 | 为什么坏 | 正确写法 |
| --- | --- | --- | --- |
| `call BaseLogger::Format`（非虚） | `((BaseLogger)this).Format(...)` | C# 里这是虚派发，子类 override 会自调用 → 栈溢出 | `base.Format(...)` |
| `ldarg.1; stfld Func<object> InputDelegate`（无 cast） | `(Func<object>)(object)InputDelegate` | 运行期强制转换，值类型泛型实例化必失败 | 字段类型放宽为 `Delegate` |
| `add_X` / `remove_X` 事件访问器（VB WithEvents） | 私有字段 + 属性 | 与 XAML 生成字段重名 | 删属性，改用构造函数里挂钩 |

### 15.4 命令备忘

```bash
# 取 IL（嵌套类型用 + 分隔，用 / 会报 Could not find a type；反引号泛型要加引号）
ilspycmd -il -t "PCL.ModLoader+LoaderTask`2" "plain craft launcher 2.exe"

# 提取托管资源（PowerShell，纯反射，不执行代码）
$asm = [Reflection.Assembly]::LoadFrom($exe)
$r = New-Object System.Resources.ResourceReader($asm.GetManifestResourceStream("<name>"))

# 比较 ILSpy 原始输出与修改后的工程（必须加 --strip-trailing-cr）
diff -rq --strip-trailing-cr _backup_PCL src/PCL
```

---

*报告生成于 2026-11-15，所有结论均基于对 `plain craft launcher 2.exe`（sha256 `77fbc26a…753f0`）的静态分析与重建版的实际运行验证。*

---

## 16 新增：服务端模式（非原版功能）

在还原工程上新增了服务端管理模块，详见 **[SERVER-MODE.md](SERVER-MODE.md)**。与原版差异的要点：

- 服务端页最初是顶部导航第 6 项（`FormMain.xaml` `BtnTitleSelect5`、`PageType.Server`），**现已并入启动页**：导航项隐藏，改由「版本选择」里新增的「服务端 (N)」卡片进入（`PageLaunchLeft.EnterServerMode()` 切换左栏，右侧换成 `PageServer`），也可在下载页的「服务端版」里选中版本后直接建实例。页面为 `PageServer.xaml(.cs)`；后端 `ModServer.cs`、`ModServerResource.cs`、`ModServerPack.cs`。
- 服务端实例独立存放在 `PCL\Server\<实例名>\`，与客户端 `.minecraft` 完全隔离；进程以 `CreateNoWindow=true` 启动，日志经 stdout 重定向进内嵌控制台（不含 cmd 窗口）。
- 核心来源：原版（BMCLAPI 的 `/version/<ver>/server`）、Paper（`fill.papermc.io/v3`）、Fabric（`meta.fabricmc.net` 启动器 jar + 原版核心，启动参数 `-Dfabric.installer.server.gameJar`）；Forge/NeoForge 代码已实现，但官方安装器硬依赖 `launchermeta.mojang.com`，在被 PAC 拦截的网络下无法完成（已在 UI 给出中文原因）。
- 资源仅走 Modrinth：插件/Mod（含 required 依赖递归）与 `.mrpack` 服务端整合包（`env.server` 过滤 + `server-overrides` + 自动建实例）。
- 另有 `server.properties` 图形化、白名单/OP（通过服务端指令）、崩溃报告查看、端口互斥、关闭 PCL 时的服务器处理询问。

同期还做了几项面向使用者的改动：

- **主题全部解锁**：`ModSecret.ThemeCheckOne` 恒返回 `true`，15 个主题（含隐藏/自定义）可直接选用，并删掉主题的解锁覆盖层与悬浮描述。
- **移除更新功能**：删掉检查更新、自动下载、更新提示与 `--update` 自我替换的界面和代码，保留更新日志查看。
- **删除赞助界面**：移除「关于与鸣谢」里的赞助者名单卡片、系统设置里的识别码/土豆码、个性化里的赞助提示；「关于」「特别鸣谢」两张卡片按要求保留。
- **界面缩放**：设置 →「个性化」新增「界面缩放」滑块（50%–200%），整体缩放窗口与内容。实现方式是 `FormMain.ApplyUiScaleChrome` 给 `PanBack` 挂 `ScaleTransform`、按比例同步窗口尺寸与 `MinWidth/MinHeight`，设置项 `UiLauncherScale` 存偏移量（百分比 = 50 + 值），拖动时用 220 ms 防抖 + 按住鼠标期间推迟，避免缩放改变滑块几何后自反馈漂移；窗口宽高按逻辑尺寸换算后存取，切换比例不会累积误差。
- **下载页 客户端/服务端切换**：版本列表顶部新增「下载类型」卡片，服务端模式跳过「最新版本」卡、按关键词与分组过滤版本，点版本即新建服务端实例（`PageServer.OpenNewWizard(version)`）并下载核心。
- **「插件」页**：社区资源新增 Plugin 分类（仅 Modrinth，`ResourceTypes.Plugin = 32`，facets `project_type:plugin` + 加载器/版本过滤）。安装目标固定为当前选中服务端实例的 `plugins\`：详情页默认把版本对齐到实例版本，版本不符先确认，下载不再弹保存对话框、保留原始 `.jar` 文件名（修复了通用保存流程把插件改名成 `.zip` 的问题）。

---

## 17 品牌改造（PCL SERVER 1.0.0-alpha）

还原工程除功能改造外，还做了整体品牌化，使产物与上游 PCL2 区分开：

| 项 | 原版 | 本分支 |
| --- | --- | --- |
| 程序名 / 产物 | `Plain Craft Launcher 2.exe` | `PCL SERVER.exe`（`csproj` 的 `AssemblyName`、`AssemblyTitle`、`AssemblyProduct` 一并修改） |
| 版本 | `VersionDisplay`「快照版 2.14.0.0」、`VersionCode` 412、`BuildTypes.Snapshot` | `VersionDisplay`「1.0.0-alpha」、`VersionBaseName` 1.0.0.0、`VersionCode` 1、`BuildTypes.Release`、`BuildTypeDisplay`「Alpha」 |
| 窗口标题 / 单实例标识 | `Plain Craft Launcher　` | `PCL SERVER　`（`Application.xaml.cs` 的 `WindowName` / `ClassName`） |
| 标题栏左侧标识 | 手绘的 `PCL` 轮廓（`ShapeTitleLogo` 的路径几何，默认模式 `UiLogoType = 1`） | 文字 **PCL SERVER**（同一 Path 位置改用 `LabTitleLogo` 渲染；`UiLogoType = 2`「文本」模式仍读 `UiLogoText` 自定义，`= 3`「图片」模式不变） |
| 图标 | `app.ico` 199 KB、`Images/icon.ico`、`Images/Heads/Logo.png` | **保持原版不变**：曾试做新图标（蓝底服务器），按用户要求改回原版——三个文件与原版字节一致，产物内嵌的 PE 图标 32×32 也与原版逐像素相同 |
| 关于页 | 三行（龙腾猫跃 / HerobrineXia / PCL） | 三行 + 新增第四行「LoneExplorer337 / PCL SERVER 的修改者」（无头像，仅文字列） |

实现要点：

- 运行期还留有若干含程序集名的字符串（`/Plain Craft Launcher 2;component/...` 形式的 pack URI 与 `assembly=...` 限定），改名后必须同步，否则 XAML 解析会在运行时报错；本次统一替换为 `PCL SERVER`（`PageLaunchRight.xaml.cs`、`PageOtherHelpDetail.xaml.cs`、`PageSelectLeft.xaml.cs`、`PageSpeedLeft.xaml.cs` 各一处，另 6 个 XAML 的 `clr-namespace` 声明）。
- 版本号原本硬编码在更新检查与日志里（`412`），已统一改为 `ModBase.VersionCode`；更新功能本身已在阶段 0 删除，因此 `VersionCode` 仅用于日志与联机版本校验（`PageLinkMain.xaml.cs`）。
- 按钮尺寸：`MyButton` 自身高度自适应，服务器页原先把所有按钮写死 `Height="30"` 且无 `Padding`，与其它页面的 35 px 按钮相比偏矮、文字贴边；现已把实例行/操作行/对话框按钮统一为 34 px、控制台与覆盖层按钮 32 px，并补 `Padding="11-13,0"`。操作行改为 `WrapPanel`，窗口变窄时自动换行而不被裁切（5 个按钮文字也做了缩短：`Mod / 插件`、`装整合包`、`服务器设置`、`白名单/OP`、`崩溃报告`）。
- 构建产物 6,053,376 字节（原版 5,993,984；+59 KB 来自服务端模式、下载页切换、插件页与界面缩放等新增代码，图标已恢复原版 199 KB），仍是单文件、无依赖 DLL。
