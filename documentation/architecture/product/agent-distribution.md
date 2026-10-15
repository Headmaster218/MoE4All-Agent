---
kind: architecture
status: current
scope: agent-product
last_verified: 2026-10-15
verified_commit: 1d13586
baseline_tag: release-0.1.0-preview.3
---

# Agent 产品与兼容边界

## 产品结构

MoE4All Agent 复用 DSH 的 Agent、会话、工具、审批、插件与 profile，只替换可见品牌并提供维护插件；不新建第二套框架。推理仍由独立 `infr serve` 提供。Agent 与 Engine 分仓、分别版本化；Agent Windows 包包含桌面壳、Node、DSH 与内置插件，不包含 Engine 或模型。

```text
MoE4All 品牌 DSH + 独立 DSH_HOME / workspace / port
  -> 默认 profile 与维护插件
  -> dsh-llm-moe4all
       -> connect: 外部 infr API
       -> 插件拥有的本地引擎进程（可选）
```

内部 `@deepseek-ai/dsh-*`、`DSH_HOME` 等兼容名保留以复用插件生态；保留变量名不等于共享变量值。连接已有引擎不获得该进程的生命周期所有权，不能把“连接”变成重启/停止生产后端。

## `release-0.1.0-preview.3` 中的固定组合

下表记录发布 tag 的组合。根 `.gitmodules` 全部指向 Headmaster218 维护仓库；gitlink 是版本事实，不以本机子模块工作树替代。

| 组件 | gitlink |
| --- | --- |
| DSH | `9b27adb07b998da0056ee0a171af17b7e18d97a8` |
| MoE4All provider | `557e455d53bdfa44354b0d69e3e9a23e327e8494` |
| 市场 | `dd2f5d23445523617abc495d7569e8edea1b501e` |
| Mneme | `7c849bc96f7e401559d9e19bfaf948482d029f74` |
| EasyRewrite | `50d7f94f2e479ce9f7000360794cf591bba5dc4f` |
| 定时任务 | `5a6dc7b66f2dfe43df14fa15184a317a22e639e7` |
| TTS | `d698174e5aad3085735ca33883279ed4d6548a77` |
| 手机/Remote Web UI | `52cf83632f4a2153d68517f5cc57608e8ea0ab57` |
| Brave 搜索 | `3b91a69766f6e4efbeeed64ffef709b26e211d3c` |

## 发布后的本地改动

`1d13586` 在上述发布基线上更新了 DSH 至 `f34e950`、MoE4All provider 至 `d2dd2ce`，其他插件 gitlink 未变。这些提交尚未发布：

- 首次启动保留原生进度页，Agent 页面导航成功后才切换到 WebView；新安装默认使用绿色，已有配色设置继续保留。
- Agent 内置模式首次自动下载并选中最新引擎；DSH 市场安装的独立插件默认提示用户选择安装，不自动下载引擎。自动下载失败后提供手动重试或导入入口。
- 引擎启动失败的分类与建议支持当前界面的中英文设置；生成默认值增加思考模式及思考强度控件。
- Bash/PowerShell 的本地执行器与 persistent terminal 默认输出预算为 4000 UTF-8 bytes；截断标记等结果元数据另计，旧 `maxOutputChars` 参数仍作为兼容入口。

随后本地修订移除了插件的日志自动分类与一槽重试入口。当前启动失败界面保留原始错误和日志，统一提供降低并发、降低上下文、切换稳健自动或激进自动策略、选择更小模型四类常见调整建议；调整由用户在设置中完成。后端结构化错误、启动管理通道、临时降级与恢复策略仍处于讨论阶段。

## 本地 main 收敛

2026-10-15 的本地验收标识为 `0.1.0-preview.4.local.3`，不代表已发布版本。Agent 本体、DSH 与全部维护插件均已收敛到各自本地 main；历史功能分支保留用于追溯，不再作为构建时的隐式版本来源。

- DSH main 为 `f34e950`，保留品牌、桌面适配、中英、四套主题、默认绿色和 4000 bytes 终端输出预算。
- Provider main 的合并提交为 `db4b3fa`，随后 `24667a9` 补齐设置异步加载时的更新提示订阅；同时包含独立插件 `0.2.2` 主线及 Agent 定制线。保留 DSH 0.1/0.2 设置和 provider 兼容、地址校验、请求超时、独立插件更新校验与回滚，以及 Agent 文件选择、稳定模型目录、双 slot 默认值、embedding 衔接、思考控件和通用失败建议。
- 内置 Provider 的自更新提示、手动入口和实际更新目标均受整包模式限制；独立安装模式继续保留主线插件自更新与首次引擎准备流程。引擎更新与插件更新仍是两条独立通道，均不等于启动推理。
- 并发默认值为 2，同时保留主线提供的 1 至 4 slot 设置范围。合并不恢复日志推断、自动降级或一槽重试。
- 其他七个插件的实际 gitlink 与上表一致；其中六个将远端 release 分支的完整提交快进纳入本地 main，Brave 本来就与 main 一致，无须制造新提交。
- profile manifest 和 lock 同步到 Provider `0.2.2` 的新 runtime 及依赖，构建使用提交固定的组合，不自动跟踪远端最新分支。
- `4b7be2e` 修正实际整包配色检查发现的输入占位文字对比度问题，使用宿主主题的 secondary 文字色且不降低透明度。

本次不修改、构建或启动独立 Engine 仓库；GPU 推理与生产后端不属于这次分支收敛的验收范围。

## 数据与运行隔离

源码构建默认以 `harness/agent-state/` 为状态根目录；发行版 EXE 和 `Run-Agent.ps1` 的默认状态根目录为安装目录中的 `data/`。`Initialize-Agent.ps1` 在状态根目录下建立 `home/profiles/web`、`workspace` 与 `agents`，默认 provider 为 MoE4All、endpoint 为 `http://127.0.0.1:8080/v1`、mode 为 `prompt`，禁用默认 DeepSeek。下载引擎与启动推理进程是不同动作，准备好模型后仍需用户启动。已有隔离 profile 会保留；初始化拒绝生产 `APPDATA/dsh-desktop/harness`、`~/.dsh` 等既有数据位置与不安全 reparse 路径。

`Run-Agent.ps1` 使用独立 `DSH_HOME`/`DSH_AGENTS_HOME`，默认 loopback Web 端口 17373，结束后恢复父进程环境；共享外部 workspace 需明确 `AllowSharedWorkspace`。这让开发入口与生产 DSH 分开，但还不能代替最终安装包的同机双运行验收。

旧 `Build-DevHarness.ps1` 是本地迁移开发工具：会复制设置、凭据、patch 和 workspace registry，刻意不复制 sessions、attachments、projection cache、node_modules 或远端配对状态；workspace 记录仍指向真实目录。不要用它制作公共发行 profile，干净构建使用 Agent 入口。

## 发布边界

`release-0.1.0-preview.3` 已包含桌面 EXE、首次启动引导、运行时准备、更新检查与 Windows CI 回归。CI 验证 bootstrap、移动后重启与插件装载，不下载大模型；导入本地 Engine、加载模型并完成真实对话仍属于发布前人工验收。默认 profile 中模型特化配置仍需公开发行审查；“无凭据快照”不等于所有模型或机器都适用。

整包更新必须由用户确认，第三方服务必须主动开启；引擎、手机连接与可选迁移的剩余验收见[交付目标](../../roadmap/agent-distribution.md)。源码与改造出处见[迁移记录](../../evidence/changes/product/dsh-source-and-local-migration.md)。不将个人 IP、凭据、会话和记忆库作为发行默认值。
