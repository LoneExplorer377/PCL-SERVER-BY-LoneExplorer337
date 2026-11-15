# 服务端模式说明（PCL SERVER 1.0.0-alpha）

本文档说明在逆向还原的 PCL2 工程上新增的「服务器模式」。原版 PCL2 2.14 只有客户端启动器，没有服务端管理功能。当前分支已品牌化为 **PCL SERVER 1.0.0-alpha**（产物 `PCL SERVER.exe`），界面与行为除本文件列出的服务端功能外与原版一致。

## 1. 入口与默认行为

- **入口已并入启动页**：顶部导航不再有「服务器」项（`BtnTitleSelect5` 已隐藏）。进入方式有三种：
  1. 启动页 →「版本选择」→ 卡片「服务端 (N)」里点某个实例 → 左栏变成服务器信息、右侧变成服务器控制台（`PageLaunchLeft.EnterServerMode()` + `FormMain.PageType.Server`）；
  2. 启动页左栏的「新建服务器」/「选择服务端」按钮；
  3. 下载页切到「服务端版」选中任意版本 → 自动弹出新建实例向导并带入该版本号。
- 页面类：`src/PCL/PageServer.xaml` + `PageServer.xaml.cs`（`ModMain.FrmServer`，作为启动页右侧内容显示，不再是独立导航页）；后端：`src/PCL/ModServer.cs`、`src/PCL/ModServerResource.cs`、`src/PCL/ModServerPack.cs`。
- 客户端与服务端完全隔离：客户端仍是 `.minecraft`，服务端实例放在 `PCL\Server\<实例名>\`，两者互不影响。
- 服务端进程用 `ProcessStartInfo` + `CreateNoWindow = true` + 三路重定向启动，**不会弹出 cmd/conhost 窗口**；日志实时显示在页面下方的内嵌控制台，底部输入框回车即发送指令（等价于服务端控制台输入）。

## 2. 实例目录结构

```
PCL\Server\<实例名>\
  server.json          # 实例元数据（名称、MC 版本、核心类型、Java、内存、端口、EULA、加载器版本）
  server.properties    # 首次创建时生成默认内容，之后由「服务器设置」图形化编辑
  eula.txt             # 首次启动前必须勾选同意 EULA
  server.jar           # 原版/Paper 核心；Fabric 时是 fabric server launcher（约 180 KB）
  minecraft-server.jar # Fabric 专用：原版服务端核心（由 PCL 下载，避免启动器再去连 Mojang）
  libraries\           # Forge/NeoForge/Paper 安装器生成的库
  mods\  plugins\       # 分别由「Mod / 插件」按钮写入
  world\  logs\  crash-reports\
```

## 3. 支持的服务端核心

| 类型 | 核心来源 | 本机可装 | 备注 |
| --- | --- | --- | --- |
| 原版（Vanilla） | `https://bmclapi2.bangbang93.com/version/<ver>/server`（302 到可用 jar） | 是 | 51 MB 左右，实测 1.21.1 可启动 |
| Paper | `https://fill.papermc.io/v3/projects/paper/versions/<ver>/builds` 取 `channel=STABLE` 最大 build 的 `downloads["server:default"].url` | 是 | 支持插件；CDN 速度约 90 KB/s |
| Fabric | 启动器 jar `https://meta.fabricmc.net/v2/versions/loader/<mc>/<loader>/<installer>/server/jar` + 原版核心 | 是 | 启动参数必须带 `-Dfabric.installer.server.gameJar="minecraft-server.jar"`，否则它会去连 launchermeta |
| Forge | `https://bmclapi2.bangbang93.com/forge/promos` 取 `<mc>-recommended`，安装器 `https://bmclapi2.bangbang93.com/maven/net/minecraftforge/forge/<mc>-<ver>/forge-<mc>-<ver>-installer.jar` | 否（本机网络） | 安装器 `--installServer` 硬依赖 launchermeta.mojang.com，本机被 PAC 拦到 127.0.0.1:443，报 `Failed to download version manifest` |
| NeoForge | `https://bmclapi2.bangbang93.com/neoforge/meta/api/maven/details/releases/net/neoforged/neoforge`，版本前缀 = `mcMajor.mcMinor.`（1.21.1 → 21.1.x） | 否（本机网络） | 同 Forge，安装器同样卡在 launchermeta |

Java 选择：向导会按 MC 版本推算所需大版本（1.16 及以下 8、1.17 16、1.18/1.19 17、1.20.0-1.20.4 17、1.20.5+/非 1.x 21），在 PCL 的 Java 列表里挑满足要求的最低版本。

## 4. 页面功能

### 4.1 服务器实例卡片

- 实例下拉框 + 新建 / 删除 / 打开文件夹。
- 信息区显示：类型、游戏版本、端口、内存、核心状态（已下载/未下载）、实例目录、Java 路径。
- 按钮行（高 32 px，`WrapPanel` 自动换行）：「Mod / 插件」「装整合包」「服务器设置」「白名单/OP」「崩溃报告」。实例行按钮（新建/删除/打开文件夹）与各对话框按钮为 34 px。

### 4.1.1 下载页的「服务端版」与「插件」页

- 下载页版本列表顶部多了「下载类型」卡片：一键在「客户端版 / 服务端版」之间切换。服务端版不下载游戏本体，只提供版本清单，选中任意版本会新建服务端实例并下载对应核心；同时提供版本号搜索框（如 `1.21`、`26.3`）与分组筛选（全部 / 正式版 / 预览版 / 远古版 / 愚人节版）。
- 下载页左栏「社区资源」新增「插件」页（仅 Modrinth）：搜索与版本筛选照常，底部提示行写明「插件将安装到服务端实例「X」的 plugins 文件夹」。
- 打开插件详情页时，版本分组会自动对齐当前选中实例的 MC 版本（实例是 1.21.1 就默认选 1.21 分组）；点某个版本会**直接下载到该实例的 `plugins\` 目录，不再弹保存对话框，也不会把扩展名改成 .zip**。
- 若所选插件文件标注支持的版本不含实例版本，会先弹窗确认（「版本可能不匹配 / 仍然安装 / 取消」）。

### 4.2 服务器控制台卡片

- 状态圆点 + 文字：未运行 / 正在启动… / 运行中 / 正在停止…；出现 `Done (` 时自动切到「运行中」。
- 启动 / 停止 / 强制结束。停止会发 `stop` 并等 30 秒（超时强制结束）。
- 控制台日志按级别着色：`[PCL]` 绿、`> 指令` 蓝、ERROR/SEVERE/Exception/FATAL 红、WARN 黄、其它浅灰；保留最近 1500 行。
- 日志同时落盘到 `<实例>\logs\PCL-yyyyMMdd-HHmmss.log`。
- 指令输入框 + 发送按钮：与直接在服务端控制台输入完全等价。

### 4.3 新建向导

名称、游戏版本（26.3/1.21.x/1.12.2/1.8.9 等预设列表）、服务端类型（原版/Paper/Fabric/Forge/NeoForge）、Java、内存（MB）、端口、同意 EULA。点「创建并下载」后后台下载核心，进度显示在实例信息区与状态文字上。

### 4.4 服务端资源（Modrinth）

- 只走 `https://api.modrinth.com/v2`（用户指定的唯一来源）。
- 插件服（Paper）：`project_type:plugin`，loader facets `paper/spigot/bukkit/purpur`；Mod 服（Fabric/Forge/NeoForge）：`project_type:mod` + 对应 loader。
- 搜索 facets 同时带 `versions:<实例的 MC 版本>`；结果过滤掉 `server_side == "unsupported"`。
- 安装时自动解析 `required` 依赖（深度 ≤ 4，visited 去重），文件先写 `.download` 再改名。

### 4.5 服务端整合包（.mrpack）

- 搜索 `project_type:modpack` + 当前 MC 版本的整合包，选版本时按 `game_versions` **精确匹配**（1.21.1 ≠ 1.21.11）。
- 下载 `.mrpack` → 读 `modrinth.index.json` → 按 `dependencies` 判定加载器（fabric-loader / forge / neoforge；quilt 明确不支持）→ 新建独立实例 → 遍历 `files` 跳过 `env.server == "unsupported"` → 解压 `overrides/` 与 `server-overrides/` → 补下载对应加载器核心。
- 单个文件下载失败只记警告，不中断整个过程（整合包常有仅客户端文件）。

### 4.6 服务器设置（server.properties 图形化）

12 项：MOTD、人数上限、端口、难度、正版验证、游戏模式、PvP、白名单、视距、模拟距离、出生点保护、监听地址。保存时只改写这些键，其余键与注释原样保留（CRLF + UTF-8 无 BOM）。改端口会同步实例元数据。修改下次启动生效。

### 4.7 白名单 / OP

通过服务端指令实现（`whitelist add|remove <name>`、`op <name>`、`deop <name>`），需要服务器正在运行；未运行时对话框明确提示且按钮灰置。名单从 `whitelist.json` / `ops.json` 读取显示。

### 4.8 崩溃报告

优先打开 `crash-reports\` 下最新的 txt；没有则打开 `logs\latest.log`；都没有时提示。

### 4.9 关闭 PCL 时的询问

若有服务器在运行，`EndProgram` 会先弹三按钮对话框：最小化 PCL（服务器继续跑）/ 停止服务器并退出（先 `stop` 并等待保存，超时再强杀）/ 取消。

### 4.10 端口互斥

启动前检查是否有其它运行中实例占用同一端口，冲突时抛出「端口 N 已被正在运行的实例「X」占用」。

## 5. 与客户端模式的关系

- 两者共用同一个 PCL 进程、同一套 Java 列表与下载源设置，但**没有任何共享的游戏文件**：客户端在 `.minecraft`，服务端在 `PCL\Server\<名>\`。
- 客户端下载页（原版游戏/Mod/整合包/资源包/光影）不受影响，仍按原逻辑工作。

## 6. 已知限制

1. **Forge / NeoForge 服务端在本机装不上**：官方安装器必须访问 launchermeta.mojang.com（本机 PAC 指向 127.0.0.1），PCL 已把这类失败翻译成中文提示，并建议改用 Fabric 或 Paper。换到能访问 Mojang 的网络即可正常安装（代码路径已实现并测到安装器启动）。
2. Quilt 服务端不支持。
3. 代理服（BungeeCord / Velocity）与远程（SSH/RCON）管理未实现，不在本次范围。
4. 服务端整合包只按 `modrinth.index.json` 的 `env.server` 过滤，不解析整合包自带的启动脚本。

## 7. 源码清单

| 文件 | 说明 |
| --- | --- |
| `src/PCL/ModServer.cs` | 实例模型（`ServerInstance`）、下载（原版/Paper/Fabric/Forge/NeoForge）、安装器调用、启动/停止、端口互斥、server.properties 读写、崩溃报告定位 |
| `src/PCL/ModServerResource.cs` | Modrinth 插件/Mod 搜索与安装（含依赖） |
| `src/PCL/ModServerPack.cs` | Modrinth 整合包搜索与安装（.mrpack 解析、overrides、自动建实例） |
| `src/PCL/PageServer.xaml(.cs)` | 服务器页 UI 与全部交互 |
| `src/PCL/FormMain.xaml(.cs)` | 顶部第 6 个导航按钮、`PageType.Server`、关闭时的服务器询问 |
| `src/PCL/ModMain.cs` | `FrmServer` 字段 |

## 8. 实测记录（2026-11-15）

| 项目 | 结果 |
| --- | --- |
| 原版 1.21.1 下载 + 启动 | 通过（`Done (64.895s)!`，无命令行窗口） |
| 控制台指令 `list` | 通过（`There are 0 of a max of 20 players online`） |
| 停止（graceful） | 通过（`Saving worlds` → 退出码 0） |
| Paper 1.21.1 + 插件（Chunky） | 通过 |
| Fabric 1.21.1 + Mod（Lithium） | 通过（启动日志含 `lithium 0.15.4+mc1.21.1`） |
| 整合包（FPS Modpack, Fabric 1.21.1） | 通过（4 个 mod 均为 1.21.1 版本落盘、Fabric 核心补齐 51.6 MB、启动 `Done (44.716s)!`） |
| server.properties 图形化 | 通过（`max-players=30`、`motd` 写入且其余键保留） |
| 白名单/OP | 通过（运行中按钮可用；`whitelist add Notch` 已送达服务端并回报 `That player does not exist`，因本机连不上 Mojang 账户服务） |
| 端口互斥 | 通过（「端口 25565 已被正在运行的实例「FPS_Modpack」占用」） |
| 崩溃报告 | 通过（打开 latest.log） |
| 关闭 PCL 询问 | 通过（最小化 / 停止并退出 / 取消） |
| Forge / NeoForge | 本机网络不可行（Mojang 源），已给出中文原因 |
