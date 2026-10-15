---
kind: guide
status: current
scope: agent-development
last_verified: 2026-10-15
baseline_tag: release-0.1.0-preview.3
verified_commit: 1d13586
---

# Agent 源码开发入口

这不是正式 Agent EXE 安装说明。先获取根仓库固定的 DSH 与八个插件 submodule；Node、pnpm 与下载校验值由 [`runtime-versions.json`](../../harness/distribution/runtime-versions.json) 固定，构建脚本会准备所需运行时。

```powershell
git submodule update --init --recursive
powershell.exe -NoProfile -ExecutionPolicy Bypass -File harness/scripts/Build-Agent.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File harness/scripts/Run-Agent.ps1 -StateRoot harness/agent-state
```

默认 Web 地址为 `http://127.0.0.1:17373`。源码构建状态在 `harness/agent-state/`，使用 `Run-Agent.ps1 -StateRoot harness/agent-state`；发行版 EXE 状态在安装目录的 `data/`。默认 Engine 地址为 `http://127.0.0.1:8080/v1`。`preview.3` 首次启动需主动选择安装或导入；之后的本地提交 `1d13586` 为新初始化的 Agent 内置模式开启首次自动下载并选中引擎，但不会因此启动推理进程。已有 profile 不会被初始化脚本覆盖。不运行本地引擎也能构建/检查 UI。首次初始化可单独使用 `Initialize-Agent.ps1`。

保持 MoE4All 与生产 DSH 的 home、agents、port 独立。不要将生产 profile 目录交给 Agent 脚本；外部 workspace 分享须主动授权。不要在发布构建中使用会拷贝生产凭据的旧 DevHarness 初始化路径。

`harness/1-Build-or-Initialize.cmd` 与 `2-Start.cmd` 是早期 DevHarness 两入口，等价于 Build/Run-DevHarness，并非干净公共发行入口。`-RefreshConfig` 会主动刷新生产设置到隔离开发环境，普通增量构建不会覆盖既有配置。

内置插件源码由 gitlink 固定，不能从市场最新版临时拼装后仍宣称同一发布基线。成品验收需确认 loader 依赖到位、并发双应用隔离、默认模型提供商、手机审批、定时任务及升级确认流程。更多边界见[当前产品架构](../architecture/product/agent-distribution.md)。

## 插件维护

八个自有插件统一在各自的 `main` 开发和提交；`.gitmodules` 的跟踪分支也固定为 `main`。先提交插件源码及对应的 `lib` 运行文件，再更新 Agent 的 gitlink 与 profile 依赖锁，最后提交 Agent `main`。普通构建使用已提交的固定 commit，不自动拉取远端分支，也不把版本号相同当作运行文件相同。

仅修改插件 Host 时，本地测试安装可单独更新该插件，不需要重新编译 DSH、桌面壳或引擎。更新前确认测试 Agent 已退出，备份原插件，校验新包与插件 commit 一致，再替换安装目录中的对应插件，并记录本地覆盖的 commit 和包校验值。不得替换用户配置、会话、记忆、模型或 KV；原始发行 ZIP 保持不变。此维护流程不等于已启用发行版内置插件的独立在线更新，后者仍遵循 Agent 统一更新策略。
