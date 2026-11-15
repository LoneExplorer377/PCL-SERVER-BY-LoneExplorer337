# PCL SERVER 1.0.0-alpha

基于 **PCL2 2.14.0.0** 反编译还原并改造的 Minecraft 启动器 + 服务端管理器。源码可编译、可运行，界面与原版一致。

## 这个版本有什么

- **服务端模式**：服务端实例管理、内嵌控制台（无命令行窗口，日志实时着色 + 指令输入）、原版 / Paper / Fabric 核心下载安装、Modrinth 插件与 Mod 安装、服务端整合包（`.mrpack`）、`server.properties` 图形化、白名单 / OP、崩溃报告、端口互斥、关闭启动器时的处理询问。
- **服务端页并入启动页**：版本选择里新增「服务端 (N)」卡片，选中实例后左栏是服务器信息、右侧是控制台。
- **下载页 客户端 / 服务端切换**：可只看服务端核心，支持版本号搜索与分组筛选，选中版本直接新建实例。
- **「插件」页**：下载页社区资源新增插件分类（Modrinth），装到当前服务端实例的 `plugins\`。
- **界面缩放**：设置 → 个性化，50%–200% 整体缩放窗口与内容。
- 主题全部解锁；移除启动器更新功能与赞助界面。

## 下载

`PCL SERVER.exe` —— **单文件，无依赖 DLL**，6,053,376 字节，需要 .NET Framework 4.8。

首次运行会在自身目录创建 `PCL\`（配置、日志、服务端实例）与 `.minecraft\`，属正常行为。

## 已知限制

- **Forge / NeoForge 服务端**在本机网络下装不上：官方安装器硬编码访问 `launchermeta.mojang.com` / `piston-meta.mojang.com`，需要能连通 Mojang 的网络环境；程序会给出中文原因提示。Fabric / Paper / 原版不受影响。
- 微软登录全流程（设备码）未做端到端验证。

## 源码与文档

- `src/` 可编译工程（net48 / WPF），`dotnet build -c Release` 净构建 0 错误 / 254 警告
- [README.md](README.md) 快速开始与改动清单
- [REPORT.md](REPORT.md) 逆向分析报告（PE/CLR 结构、启动流程、网络与认证、遥测、反编译坑）
- [SERVER-MODE.md](SERVER-MODE.md) 服务端模式说明

## 制作

逆向还原、服务端模式与全部改造由 **DSH（DeepSeek Harness）+ DeepSeek-V4.1-Flash-1017** 完成；维护者 **LoneExplorer337**。

> 仅供学习研究与兼容性验证。Plain Craft Launcher 2 的著作权归原作者（Meloong-Git）与成都瓜皮龙科技有限公司所有。