# 夜雀食堂模组管理器（MystiaModManager）

面向《东方夜雀食堂》的通用 BepInEx 模组管理器。第一版支持多配置、本地模组安装/启停、从管理器启动游戏，以及从 [BepInEx Bleeding Edge](https://builds.bepinex.dev/projects/bepinex_be) 安装/更新 IL2CPP win-x64 6.x 框架。

## 镜像

| 角色 | 地址 |
| --- | --- |
| 主仓库 | GitHub：https://github.com/KaWaayi/MystiaModManager |
| 备用 | Gitee：https://gitee.com/wjjnb666/MystiaModManager |

安装器下载管理器本体时：**先试 GitHub**（约 15 秒连不上或迟迟无数据则放弃），**自动切到 Gitee**。可用环境变量覆盖：

```powershell
$env:MYSTIA_GITHUB_REPO = "owner/MystiaModManager"
$env:MYSTIA_GITEE_REPO   = "owner/MystiaModManager"
```

发版时请在 **GitHub Release 与 Gitee Release 都挂上同一份** `MystiaModManager.zip`（可用 `scripts/build.ps1` 打出）。

## 玩家侧依赖

无需额外安装：

- 界面：.NET Framework 4.8 + WPF-UI（Windows 10 1903+ / Windows 11 自带 4.8）
- 核心：Rust DLL（`+crt-static`，不要求 VC++ 运行库）

## 目录结构

```
MystiaModManager/          # 管理器程序（可由安装器覆盖）
MystiaModManagerConfig/    # 配置父目录（安装器不覆盖）
  manager.json
  profiles/
    Default/
      BepInEx/
      dotnet/
      winhttp.dll
```

默认路径：游戏不在 `C:` 时放在游戏盘符根目录；在 `C:` 时放在 `%AppData%`。

## 开发构建

需要：Rust stable、.NET SDK（能编译 net48）。

```powershell
.\scripts\build.ps1
```

产物：

- `ui\bin\Release\MystiaModManager.exe`
- `dist\MystiaModManager.Setup.exe`（单文件安装器，给玩家）
- `dist\MystiaModManager.zip`（上传到 GitHub / Gitee Release）

安装器会优先使用旁边的本地 UI 构建；否则按上面的 GitHub → Gitee 顺序下载。

## 启动行为

- **从管理器启动**：覆盖游戏目录 `winhttp.dll`，并把 `doorstop_config.ini` 写成当前配置的绝对路径，游戏退出后也保留。Steam 若在第一次进程退出后立刻再启动游戏，第二次仍会加载这套模组。不附加命令行参数，避免每次弹出「用自定义参数启动游戏」。
- **从 Steam 直接启动**：游戏目录里的 `doorstop_config.ini` 使用相对路径；若游戏目录下没有 `BepInEx`，则保持原版。若你本机游戏目录里已有一份 BepInEx，Steam 仍会加载那一份（管理器不会移动它）。

## 第一版范围

已实现：路径探测、多配置、本地 zip/dll 安装、`.dll.off` 启停、BepInEx 构建站安装/更新、Doorstop 钩子、游戏占用保护、GitHub/Gitee 双源下载、管理器内检查更新、当前配置的 cfg 编辑（保留注释）。

未实现：在线模组 JSON、依赖关系。
