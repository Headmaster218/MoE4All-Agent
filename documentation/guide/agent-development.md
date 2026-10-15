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

### 插件 CI 与 Release

八个插件各有独立 CI/Release。引擎管理插件 `dsh-llm-moe4all` 已有源码构建、多个官方 DSH fixture 和安装/重启冒烟；其余七个插件新增 Windows/Linux 的已提交运行文件检查、离线测试及发布包校验。部分导入仓库缺少完整上游构建输入，后七者采用 `committed-runtime` 模式，不冒充从源码重建或完整 Agent 集成验收。

- 七个插件固定 Node/npm 与 `ci/host` 依赖锁；TTS 另检查 Node 22，并用模拟 Edge WebSocket 和本地 mock RVC，避免下载模型或访问真实语音服务。
- 发布前验证包入口、相对依赖、客户端 loader 外部依赖、bundle patch、许可证、语法；Mneme 另校验 src/lib 同步。包逐字节比对并校验版本、来源 commit、SHA256 和 integrity，反向测试会拒绝错误 tag、被篡改的包或清单。
- `main` push、PR 和手动触发 CI。七个插件的 tag 必须是 `v<package.json version>` 或 `release-<package.json version>`，且来自 main 历史；验证成功后发布 `.tgz`、`plugin-release.json`、`SHA256SUMS.txt`。现有 `-moe4all.1` 版本仍标记 prerelease，不修改版本号冒充稳定版。
- 六个插件的导入 fixture 为官方 DSH 0.2.0-rc.2；Brave 为 0.1.1-rc.2，其使用的 `installSettingsSection` 在 0.2 fixture 中已不可导入，现已显式限制 DSH 兼容范围。升级 Agent 的上游 DSH 前必须迁移此接口并重测，其他插件的导入冒烟通过也不等于完整功能兼容。

以上仅提供可供更新器使用的发布产物，不新增独立 OTA、自动安装、更新中心或用户配置迁移行为。各插件细节见其 `ci/README.md`。本机检查在统一 local-work 的 staging 快照中完成，不安装到运行中的 Agent/DSH。

仅修改插件 Host 时，本地测试安装可单独更新该插件，不需要重新编译 DSH、桌面壳或引擎。更新前确认测试 Agent 已退出，备份原插件，校验新包与插件 commit 一致，再替换安装目录中的对应插件，并记录本地覆盖的 commit 和包校验值。不得替换用户配置、会话、记忆、模型或 KV；原始发行 ZIP 保持不变。此维护流程不等于已启用发行版内置插件的独立在线更新，后者仍遵循 Agent 统一更新策略。
