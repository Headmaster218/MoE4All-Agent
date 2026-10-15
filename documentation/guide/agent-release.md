# Agent 构建与发布

Agent 与 Engine 分别版本化。Agent Windows 包包含桌面壳、Node、DSH 和内置插件，
不包含 Engine 或模型；Engine 插件负责独立安装、导入和更新。

## 固定依赖

- `harness/distribution/runtime-versions.json` 固定 Node、pnpm、registry 和 Node ZIP SHA256。
- `harness/distribution/build-tools/package-lock.json` 固定 pnpm 安装包及 integrity。
- `harness/distribution/update-tools/package-lock.json` 固定随包提供的能力中心工具；用户更新插件不需要另外安装工具链。
- DSH 使用子模块内的 `pnpm-lock.yaml`；Provider 的源码开发使用子模块内的 `package-lock.json`。
- 内置插件固定为 Git submodule commit；profile 使用 `harness/distribution/profile/pnpm-lock.yaml`。
- Actions 使用完整 commit SHA，Windows runner 使用 `windows-2025`。托管 runner 镜像本身会更新，不是位级固定。

修改插件后先提交插件源码及 runtime，再运行 `harness/scripts/Build-Agent.ps1 -UpdateProfileLock`，审查并提交 profile lock。
lock 维护会从插件的 Git commit 打包，明确关闭 Git archive 的 autocrlf 转换。
所有构建模式均使用内容摘要命名的插件 tarball，代码变更即改变依赖标识，不能依赖同版本、同名 tarball 的 `--force` 安装来刷新 hoisted profile。
安装后逐文件核对提交源码、锁定 archive 与实际 `node_modules`，拒绝指针正确但运行文件仍旧的缓存。复用 prebuilt profile 也必须通过该检查。
发行包使用子模块里已提交、验收过的插件 runtime，不在发行阶段重新生成 sourcemap；
修改插件时需先完成源码构建和测试，再提交 runtime 与源码。
发布构建使用 `-Release` 与 frozen lock，不执行依赖自动升级。
DSH 部署使用 pnpm shared-lockfile deploy（仅在部署阶段启用 workspace injection），
从已检查的 workspace lock 生成冻结的部署 lock；不使用会重新解析依赖的 legacy deploy。

## 插件独立发布

“发布所有维护插件”必须逐个覆盖八个插件，不能只处理新增 CI 的仓库或仅确认已有 Release。每个插件都核对本地 main、远端 main、最新发布 tag 的目标 commit、包内版本和公开资产；没有变更且五者一致的插件不重复升版。

存在未发布提交时，先完成源码、runtime 与测试，再升包版本、更新对应 lock 和变更说明，推送 main 与新的 `release-<package.version>` tag。已发布 tag 不移动；Release 的 `target_commitish: main` 不表示 tag 会随 main 更新。提交 main 本身不触发插件发布，tag 发布工作流成功才会生成新资产。

发布后重新下载公开资产，检查远端大小、SHA256、包名、版本、运行入口及兼容范围；提供 release manifest 的插件还需核对其源码 commit。最终清单逐个记录本地 main、远端 main 与发布 tag 一致，而不是以 Agent 整包中包含某次提交代替独立插件已发布。

Agent gitlink 与 profile lock 固定最新验收组合。为测试能力中心，可在隔离体验 profile 中安装真实旧 Release，验证确认下载、重启切换与回退；这不改变源码仓库的最新指针，不伪造插件版本。Agent 本体是否 push、打 tag 和发布，始终是另一项明确决定。

## 发布流程

1. 先在各子仓 main 收敛功能和修复，完成测试及 runtime 构建。本地验收可以暂不 push；正式发布前必须先 push 子仓 main，并确认其包含 Agent 要引用的提交。
2. 在 Agent main 更新 `harness/distribution/agent-build.json` 和发行说明，提交子模块指针及 lockfile。确认远端能获取所有固定子仓提交后，再 push Agent main 和 `release-<version>` 标签。CI 拒绝版本不符或不属于 main 的提交。
3. CI 构建 ZIP 和 SHA256，运行 bootstrap、updater、EXE 回归检查，并从 ZIP 测试全新启动及移动后的重启。
4. CI 创建草稿 release。下载 CI 的原始资产，验证 SHA256，再用真实桌面 EXE 导入本地 Engine、加载已有模型并完成对话。
5. 验收通过后将草稿公开。预览标签包含 `-preview.N`，发布为 prerelease。

CI 不下载大模型，也不宣称 GPU 推理已验收。最终模型对话属于发布前人工环境验收；
本机证据按工作区磁盘规则进入统一生成物根目录的 `evidence/agent/<task-id>`，发行说明记录结论，不打包个人配置和模型路径。

## 本地构建

```powershell
$version = (Get-Content ./harness/distribution/agent-build.json -Raw | ConvertFrom-Json).version
if (-not $env:MOE4ALL_WORK_ROOT) { throw 'Set the generated-data root according to the workspace disk policy.' }
$task = Get-Date -Format 'yyyyMMdd-HHmmss-agent-local'
$output = Join-Path $env:MOE4ALL_WORK_ROOT "staging/agent/$task/package"
$check = Join-Path $env:MOE4ALL_WORK_ROOT "staging/agent/$task/acceptance"
./harness/scripts/Build-AgentRelease.ps1 -Version "release-$version" -OutputDirectory $output
./harness/scripts/Test-AgentRelease.ps1 -Archive (Join-Path $output "MoE4All-Agent-Windows-x64-v$version.zip") -OutputDirectory $check
```

源码及子模块必须干净且匹配提交指针。不要把开发 profile、生产 DSH 数据或本机绝对路径复制进发行包。

`harness/distribution/agent-release.json` 是生成的构建来源清单，不作为源码提交。
清单中的 `dshVersion` 用于能力中心在整包安装前核对维护插件宿主范围，缺失时拒绝安装；独立插件更新与整包更新的边界见[能力中心](capabilities.md)。
使用 `-PrebuiltHarnessRoot` 时，构建器校验其中的 DSH、插件提交、profile lock SHA256 与运行时版本，不能直接复用旧版本目录。
带 `preview.N.local.M` 的版本用于本地验收；当前标识以 `harness/distribution/agent-build.json` 为准，不代表已经创建发布标签或上传资产。
更新器识别 `preview.N.local.M`，同一 preview 下按 local 编号递增，后续更高 preview 或正式版仍能正常更新。

只更新内置插件时，可使用 `-BaseArchive <已校验 ZIP> -RefreshProfileFromHarness <已构建 harness>`。
此模式要求 DSH gitlink、Node/pnpm 固定版本和 DSH 构建入口不变；重新验证全部插件文件并生成新的 profile payload，保留已验证的 DSH/Node payload。来源清单分别记录 DSH 与 profile 的构建提交，不把旧 profile 当成新产物。
