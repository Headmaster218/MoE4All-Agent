# Agent 工具运行时改进与 Windows 兼容性验证

## 范围

本轮修复来源于 API 模拟后端驱动的工具实测，不升级 Session 格式，不替换沙箱后端，不修改 Mneme、Office、推理引擎或生产 DSH。验证使用独立源码快照与临时工作区、已有解释器和依赖，无模型服务、GPU 推理或新增依赖下载。

旧实测成品为 Agent 0.2.3 / DSH `5bfeb6f`。本轮从 DSH `2c4c90c` 实施源码修改，不能把旧成品结果当成新源码完整验收。未重新打包、发布或替换运行中的 Agent。

## 本地提交

| DSH 提交 | 内容 | 定向验证 |
| --- | --- | --- |
| `dd6e592` | PowerShell 子进程默认 Python UTF-8，显式调用方可覆盖，不改宿主环境 | 37 项通过，1 项 POSIX 用例跳过；含真实 Loader |
| `f3ec9fc` | 持久化执行状态元数据；修正 exit 1、EPERM、裁剪日志的误导提示 | 69 项通过；含真实 PowerShell 和 Loader |
| `80b8cf4` | 独立控制 Harness 开发上下文，不必同时关闭 GUI 定位 | 14 项通过；含本地临时 Web 服务验证 |
| `34746ef` | 长单行有界续读、Unicode 边界、文件身份与版本校验，不再误报完整 EOF | 140 项通过；含真实 Loader 的无密钥快照 |
| `b666668` | 源码 runner 的 tsx 加载器解析为绝对地址；增加显式开启的兼容矩阵 | 源码启动回归用例通过；真实兼容矩阵完成 |

各批用例存在重叠，不将通过数相加宣称为独立用例数。矩阵测试完成也不代表所有被测命令兼容。

Agent portable profile 显式关闭 `developmentContext`，保留命令行控制的浏览器与信任地址。两项 profile 单测分别核验普通任务设置和启动参数传递。

## 真实 Windows 兼容矩阵

使用当前源码的 ACL restricted-token 后端与显式宿主对照。受限命令先执行，宿主对照不是产品自动降级，不向被拒命令补授权。

| 项目 | workspace-write | 宿主对照 |
| --- | --- | --- |
| Python 中文 stdin 与 emoji stdout | 通过，验证本轮编码修复 | 通过 |
| Python 捕获子进程输出 | 通过 | 通过 |
| Node 捕获子进程输出 | EPERM | 通过 |
| Node 继承 stdio | 通过 | 通过 |
| 标准 `node --test` | spawn EPERM | 通过 |
| PowerShell HTTPS | SSPI 获取凭据句柄失败，`0x8009030E` | 通过 |
| Node HTTPS | HTTP 200 | HTTP 200 |
| uv 工作区外专用缓存 | 初始化拒绝，os error 5 | 通过 |
| uv 工作区内专用缓存 | 通过 | 通过 |

负向写入用例证明此次专用路径的工作区外写入被拒、read-only 写入被拒；工作区内写入正常。前台取消正确报告 `aborted=true`、`timedOut=false`。这些是具体用例证据，不是所有 ACL、hardlink、网络或后代进程隔离的证明；后端仍报告 partial enforcement。

只读 PowerShell 还出现已知的编码 preamble 与 ConstrainedLanguage 不兼容提示。捕获输出失败、SSPI 失败和缓存授权分别记录，不能统一归为“缺包”“断网”或 API key 错误。

## 后续讨论项

- Node IPC：定位 restricted token 与 Node named pipe 创建/访问的具体冲突，再评估窄修或后端候选。未增加全权限回退，也未宣称外层 stdio 改造能修复孙进程 IPC。
- PowerShell HTTPS：现已定位到 SSPI `AcquireCredentialsHandle`，尚未证明应调整哪个令牌能力；不绕过 TLS 校验，不把添加凭据作为修复建议。
- 缓存：产品独立缓存目录与沙箱可写范围不一致时，需要讨论受控缓存访问方式、最小授权及跨工作区污染。测试没有向真实共享缓存或用户目录授予权限。
- Python 环境发现/选择：本轮只做编码与明确的工具指引，没有引入 PET、自动下载 Python、全局包修复或通用环境管理层。
- 持久 shell、浏览器、Mneme 和 Office 保持各自范围，不与本轮混改。

## 验证边界与证据

原始日志、兼容矩阵 JSON、失败的源码启动对照和本轮运行清单位于本机统一生成物根下的 `evidence/agent/20261023-225242-agent-tool-fixes-c3d8`，提炼结果位于同任务的 `results/agent`。目录由本机磁盘规范管理，不进入产品包。

定向源码类型检查只检查选定变更文件，其诊断为零；简化检查程序仍有选定文件之外的传递依赖诊断，不能将其表述为全仓 typecheck 通过。未执行完整构建、全量测试或真实模型自主任务验收。
