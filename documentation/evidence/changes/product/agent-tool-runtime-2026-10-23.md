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
| `6692acb` | 仅在 FullLanguage 初始化 PowerShell 编码，避免 read-only 下的无关报错；不改权限 | 定向 38 项通过、1 项 POSIX 用例跳过；真实 Loader 另 1 项通过 |
| `ab3273a` | 增加按需 `shell_environment`，在原策略内检查实际 shell、工具路径及 Python 隔离启动，不自动选择或安装环境 | 84 项定向测试通过，含真实 Python 和真实 Loader；另以 PS7/PS5.1 read-only 原生 runner 验证 |
| `440da57` | workspace-write 未配置的 uv/pip/npm 缓存默认进入已有私有临时目录，不增加授权 | 4 项测试通过，含真实 uv 离线启动、同会话复用及跨会话拒绝 |

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

首轮只读 PowerShell 出现编码 preamble 与 ConstrainedLanguage 不兼容提示，后续窄修已消除这两条初始化报错，但未解决受限 PowerShell 自身的所有编码限制。捕获输出失败、SSPI 失败和缓存授权分别记录，不能统一归为“缺包”“断网”或 API key 错误。

## 后续有界验证：read-only、Node 管道和 HTTPS

### 已修复并回归

编码初始化现在只在 FullLanguage 执行，仍使用无 BOM UTF-8；read-only 下保持 ConstrainedLanguage 和原来的编码默认值，不尝试被禁止的构造器或 Console 属性赋值。用户命令没有额外的脚本块、异常包装或权限重试。

真实 PS7、PS5.1 的 read-only 用例各覆盖 8 条命令：语言模式不变、Python 直接输出中文及 emoji、管道不新增 BOM、显式退出码 7、已捕获异常、不终止错误后继续执行、终止错误，以及工作区写入仍被拒。全部通过。FullLanguage 的中文输出与无 BOM 管道编码回归也通过；真实 Loader 的前后台工具调用通过。

这是初始化噪声修复，不是完整 Unicode 修复。受限模式的 PowerShell 自身控制台输出仍可能使用旧代码页；PS5.1 的原生程序管道输入仍可能将中文替换为 `?`。直接将 `$OutputEncoding` 换成 `Encoding.UTF8` 会在本次实测管道中引入 BOM，因此未采用。

### 进一步缩小范围，但未改沙箱

使用 Node 24.9.0、同一极小子进程与真实 workspace-write 沙箱，结果如下：

| 命令或输出路径 | 受限运行 | 宿主对照与含义 |
| --- | --- | --- |
| Node `spawnSync` + `stdio: pipe` | EPERM | 相同 pipe 调用在宿主通过 |
| Node `spawnSync` + `stdio: ignore` | 通过 | 说明并非所有子进程都无法启动 |
| Node `spawnSync` + 工作区文件句柄 | 通过 | 仅验证专用输出文件，不涉及用户目录授权 |
| Node `spawnSync` + `stdio: inherit` | 通过 | 父进程可捕获直接继承的输出 |
| 默认 `node --test` | spawn EPERM | 默认测试运行器的子进程路径仍不兼容 |
| `node --test --test-isolation=none` | 简单用例通过 | 仅定位线索；会改变测试间进程隔离语义，不作为自动替换方案 |

以上证据将问题缩小到 Node 的管道/子进程运行路径，尚不能证明具体是哪一项 token、named pipe ACL 或 libuv 实现导致。未修改 Agent 外层 stdio，也未宣称这能普遍修复 npm、测试框架或孙进程 IPC。

PowerShell HTTPS 再次在受限运行下失败，显式宿主对照成功。专用探针只输出有界的异常类型、HResult 与 Win32 NativeErrorCode，复核到 `HttpRequestException → AuthenticationException → Win32Exception`，其中 NativeErrorCode 为 `0x8009030E`。通用 HResult 与 Win32 原生错误码不是同一字段。探针不输出完整 ErrorRecord、请求参数、请求头、正文、环境变量或异常堆栈。

自动保留完整异常详情本次暂缓：通用 `try/catch`、全局强制终止错误或详细错误视图，分别可能改变用户命令语义或泄露请求数据。当前只保留隔离探针，不修改产品错误处理。后续若要实现，应先确定独立诊断通道、字段白名单和退出语义回归边界。

## 2026-10-24：环境检查与私有工具缓存

### 环境事实按需获取，不自动换环境

`shell_environment` 与现有 `pwsh` 同属一个工具插件，使用相同执行器、会话工作区、既有沙箱策略、取消信号及可信 `DSH_*` 变量。它报告实际 shell 版本/语言模式、部分工具路径和缓存设置；Python 先发现常见项目 `.venv`/`venv`，再列 PATH 应用，去重后按配置数量上限执行启动检查。显式 `python_executable` 只检查该路径，不回退其他解释器。单次子进程默认超时 3000 ms，候选默认最多 4 个，均可配置。

Python 使用 `-I -S -B -X utf8`，不导入工作区模块，不执行 site/.pth 启动钩子，不写字节码。结果逐个保留启动成功/失败、版本、实际可执行路径和退出/超时事实。无效 JSON、输出截断、取消或策略拒绝不会被认定为检查成功；任意启动诊断正文不进入结果。运行成功只证明隔离启动，不证明 pip、项目依赖、Conda 激活或原生 DLL 健康，也不包含全机环境发现、自动下载/安装或自动绑定解释器。

真实 Loader 验证显式不存在的解释器如实失败；真实 Python 验证启动成功，即使工作区放有同名 `json.py` 也不误导入。额外原生 runner 探针在 PS7 和 PS5.1 的 ConstrainedLanguage 下均成功获取环境及 Python 版本，无初始化 stderr。工具注册测试还验证额外传入的提权字段不会扩大本工具权限，取消不会伪装成“环境不可用”。

### 缓存只使用已有私有写权限

workspace-write Windows runner 对未配置的 `UV_CACHE_DIR`、`PIP_CACHE_DIR`、`npm_config_cache` 注入 `<private-temp>/tool-cache/<tool>`，无需额外 ACL、无需修改用户 profile，也不提前建目录。显式路径、大小写变体或空值保持不变，外部路径仍需原有授权。read-only 不注入缓存默认值。

这是会话执行缓存：同一活跃会话/工作区对可复用，provider 释放后删除，无 agent 调用只在单次执行中存在；不是跨重启共享的全局包缓存。真实测试证明三个默认目录可写、同会话路径稳定、其他会话获得不同私有目录且不能写入前者、uv 以已有解释器离线启动成功、显式工作区外缓存仍失败、read-only 不获得新写权限。未联网下载或安装依赖。

### Node pipe：新增 Win32 调用级观察

相同 Node 24.9.0、FFI 代码及默认安全属性的最小探针中，宿主的 `CreatePipe`、`CreateNamedPipeW`、`CreateFileW` 读写打开客户端均成功；workspace-write 受限运行中，前两个成功，客户端读写打开返回 `ERROR_ACCESS_DENIED (5)`。探针不修改 DACL、不加 SID、不提权，结束时关闭自己创建的句柄。

这与前轮 Node pipe EPERM 相符，将定位进一步缩小到默认安全属性下的 named-pipe 客户端写访问检查。但它不是 libuv 全链路调用追踪，不能据此声称已经找到适用于所有 Node/测试框架的产品修复。给父 shell 换 stdio 不会改变孙进程自行创建管道的安全属性；若需要控制管道创建端、替换运行后端或修改 Node/libuv，应先讨论兼容性与权限边界，不通过扩充文件写权限或静默宿主重试解决。

## 后续讨论项

- Node IPC：已有默认安全属性下 named-pipe 客户端读写打开被拒的 Win32 最小证据；尚无通用产品修复。继续验证实际 libuv 路径，再讨论运行后端或管道创建端方案。未增加全权限回退。
- PowerShell HTTPS：现已定位到 SSPI `AcquireCredentialsHandle`，尚未证明应调整哪个令牌能力；不绕过 TLS 校验，不把添加凭据作为修复建议。
- 缓存：已有会话私有执行缓存默认值；跨重启、跨会话的持久共享包缓存仍需讨论授权、污染与生命周期。测试没有向真实共享缓存或用户目录授予权限。
- Python 环境发现/选择：已有常见候选发现及按需隔离启动检查；完整环境发现、解释器绑定、项目依赖与 Conda/DLL 健康仍待处理，没有引入 PET、自动下载 Python 或全局包修复。
- 持久 shell、浏览器、Mneme 和 Office 保持各自范围，不与本轮混改。

## 验证边界与证据

原始日志、兼容矩阵 JSON、失败的源码启动对照和本轮运行清单位于本机统一生成物根下的 `evidence/agent/20261023-225242-agent-tool-fixes-c3d8`，提炼结果位于同任务的 `results/agent`。目录由本机磁盘规范管理，不进入产品包。

后续窄修与探针的原始证据另存于 `evidence/agent/20261023-233347-pwsh-narrow-d4a2`，包括 `encoding-probe.json`、`readonly-encoding.json`、`boundary-probe.json` 和定向测试日志；对应总结在同任务的 `results/agent`。本次选定源码文件的类型诊断为零，但简化检查还有 70 条选定文件之外的传递依赖诊断，不能表述为全仓 typecheck 通过。

2026-10-24 的环境/缓存测试及原生探针在 `evidence/agent/20261024-000746-env-cache-e6a1`；对应分析在同任务 `results/agent`。最终环境 84 项、缓存 4 项通过，另有 PS7/PS5.1 read-only 原生探针和 named-pipe Win32 对照。6 组中英配对通过，工具/配置目录已按源码重新生成；所选 10 个源码及测试文件无本文件类型诊断，仍有 70 条外部传递依赖诊断。首次注册实现失败的日志也保留，不与最终通过数混算。仍未整包编译、发布、替换运行中的 Agent 或进行真实模型自主任务验收。

定向源码类型检查只检查选定变更文件，其诊断为零；简化检查程序仍有选定文件之外的传递依赖诊断，不能将其表述为全仓 typecheck 通过。未执行完整构建、全量测试或真实模型自主任务验收。
