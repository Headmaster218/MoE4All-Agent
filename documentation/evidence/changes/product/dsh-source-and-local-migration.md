---
kind: change-record
status: historical
scope: dsh-provenance
recorded: 2026-10-11
source_checkpoint: release-0.10.0
---

# DSH 来源与本地改造迁移

这是 2026-09-23 建立 Agent 基础时观察到的生产安装来源与行为清单，不是当前依赖锁；当前 gitlink 见[产品架构](../../../architecture/product/agent-distribution.md)。本文集中原 `harness/inventory.md` 与 `harness/local-changes.md` 中的信息，不携带用户数据、地址和凭据。

## 来源版本

| 组件 | 安装版本 | 上游提交与注意事项 |
| --- | --- | --- |
| DSH | 0.1.1-rc.2 | `b150a551b8d465e31e418e1b2eaf5e79bbb7d28e`；安装 Harness 本体未发现 overlay |
| Desktop | 0.6.3 | `e589bf688624871d953f8c58418f913c407dfc7e`；root package 仍为 0.1.1，artifact/tag 为 0.6.3，Harness 依赖 0.1.1-rc.2 |
| dshmarket | 1.45.1 | `1664caec99219b4902f1686e2e34614815d38346`；无 overlay |
| Brave | 1.0.0 | `10799a8c19ec6d672789257be609750515dbb513`；profile 选择该搜索 provider |
| TTS | 0.3.1 | `ff26f361df77605599896fa2ba76eb875eddc1d2`；以安装 lockfile 而非同名 tag 为准 |
| Remote Web UI | 0.3.17 | `46f1616f26525c7b8a782968f02fa97fde2c5790` |
| Mneme | 0.7.29 | `0b3e99f6ff4948ab4b823914a0a16f4411d25a4c`；nested dsh-mneme 才是发布 package，root 仍为 0.7.16 |
| EasyRewrite | 2.4.1 | `8bfa03c946e81e6159d0c839ffb7a02c6c52c158`；无 overlay |
| Cron | 0.2.7 | `f7a57a64ac26e325ddacc59cd9368e6844657347`；无 overlay |
| 候选 uploader | 0.4.0 | `63d474b2a3aea6b880a6c7f36aa7a1d0680a38a6`；未启用、未纳入当前 submodule |

各维护仓库从准确 source checkpoint 导入，不导入完整上游 history；各自 `UPSTREAM.md` 保存来源、release/commit、license 与 attribution，根 gitlink 固定组合。Desktop shell 和 uploader 没有当前维护子模块，不能列成内置已交付插件。

## 行为迁移与边界

| ID | 行为 | 迁移状态/边界 |
| --- | --- | --- |
| DESKTOP-PORT | Desktop 固定 1633，启动检查占用 | 未迁移，隔离 Web 使用独立端口；不能照搬为产品默认 |
| REMOTE-EVENTS | 配对/授权门控转发 `events.mux`/host，使审批和提问到手机 | 维护 Remote 子模块已迁移，有 contract/rewrite 测试记录 |
| REMOTE-TRANSPORT | 旧 DSH 缺少预期 transport，`createApiClient` fallback | 兼容 adapter；统一 transport 契约后再考虑移除 |
| MNEME-V2 | 单调用逐轮 distill2、cluster 驱动 dream3、engine/reasoning 设置 | 维护 Mneme 已迁移；保存功能而非生产记忆内容 |
| MNEME-EMBED | native Embedding 替代未用的 HF transformers 依赖 | profile 明确 stub override；未来可换 native provider 契约 |
| PROFILE-POLICY | Brave、Mneme v2、title no-reasoning route | portable patch 保留；模型特化仍要发布审查 |
| UPLOAD-PATH | 控制字符、ADS、保留设备名防护；delete 仅 uploads 直接子项 | 旧 v1 候选保留安全设计，未进当前内置组件；另有 same-origin、upload cap、keep-forever 默认 |
| REMOTE-ENDPOINT | 私人公网地址和 Windows port proxy | 只属用户配置/迁移输入，绝不作默认 |
| TITLE-DIAG | 临时 title proxy、探针、stream 诊断 | 测试用途，不进入生产 routing；log 不发布 |
| LOCAL-TOOLS | ASR/media、受控 Bilibili 评论流程及个人 skills | 单独审查提炼为可选插件；不导入 cookie、队列、corpus、secret 或个人 workspace |

历史 `distribution/profile/cordis.patch.yml` 保存行为覆盖，`profile/package.json` 保存插件组合；HF stub 保留当时安装策略。源码快照排除 session、记忆库、附件、log、credential、remote `publicBaseUrl`。默认 profile 不是证明成品已验收；legacy DevHarness 复制设置/凭据行为和干净 Agent 初始化必须区分。发行与迁移剩余工作见[路线图](../../../roadmap/agent-distribution.md)。
