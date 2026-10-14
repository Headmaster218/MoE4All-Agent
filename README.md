# MoE4All Agent

MoE4All Agent 是 MoE4All 的桌面 Agent 发行版，包含桌面壳、DSH、内置插件、
首次启动引导和分层更新能力。推理引擎由独立的
[MoE4All Engine](https://github.com/Headmaster218/MoE4All) 项目提供。

当前 Agent 源码位于 `harness/`，其中 DSH 与内置插件以 Git submodule 固定版本。
开发和构建说明见 [harness/README.md](harness/README.md) 与
[Agent 开发指南](documentation/guide/agent-development.md)。

桌面支持中文和 English，以及森林绿、黑色、灰色、白色四种配色。设置持久化与插件适配说明见[语言与配色](documentation/guide/agent-appearance.md)。

Windows 预览版在[Agent Releases](https://github.com/Headmaster218/MoE4All-Agent/releases)发布。
解压后运行 `MoE4All.exe`，无需构建。Engine 与模型独立导入或安装。
标签触发的 CI 和固定依赖见[构建与发布](documentation/guide/agent-release.md)。

## 获取源码

```powershell
git clone --recurse-submodules https://github.com/Headmaster218/MoE4All-Agent.git
```

## 许可证

项目按 `LICENSE` 与 `LICENSE-MIT` 所列条款发布。各 submodule 适用其各自许可证。
