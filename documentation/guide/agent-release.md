# Agent 构建与发布

Agent 与 Engine 分别版本化。Agent Windows 包包含桌面壳、Node、DSH 和内置插件，
不包含 Engine 或模型；Engine 插件负责独立安装、导入和更新。

## 固定依赖

- `harness/distribution/runtime-versions.json` 固定 Node、pnpm、registry 和 Node ZIP SHA256。
- `harness/distribution/build-tools/package-lock.json` 固定 pnpm 安装包及 integrity。
- DSH 使用子模块内的 `pnpm-lock.yaml`；Provider 的源码开发使用子模块内的 `package-lock.json`。
- 内置插件固定为 Git submodule commit；profile 使用 `harness/distribution/profile/pnpm-lock.yaml`。
- Actions 使用完整 commit SHA，Windows runner 使用 `windows-2025`。托管 runner 镜像本身会更新，不是位级固定。

修改插件后先提交插件源码及 runtime，再运行 `harness/scripts/Build-Agent.ps1 -UpdateProfileLock`，审查并提交 profile lock。
lock 维护会从插件的 Git commit 打包，明确关闭 Git archive 的 autocrlf 转换。
发行包使用子模块里已提交、验收过的插件 runtime，不在发行阶段重新生成 sourcemap；
修改插件时需先完成源码构建和测试，再提交 runtime 与源码。
发布构建使用 `-Release` 与 frozen lock，不执行依赖自动升级。
DSH 部署使用 pnpm shared-lockfile deploy（仅在部署阶段启用 workspace injection），
从已检查的 workspace lock 生成冻结的部署 lock；不使用会重新解析依赖的 legacy deploy。

## 发布流程

1. 在 main 更新 `harness/distribution/agent-build.json` 和发行说明，提交子模块指针及 lockfile。
2. 对 main 中的提交创建 `release-<version>` 标签并 push。CI 拒绝版本不符或不属于 main 的提交。
3. CI 构建 ZIP 和 SHA256，运行 bootstrap、updater、EXE 回归检查，并从 ZIP 测试全新启动及移动后的重启。
4. CI 创建草稿 release。下载 CI 的原始资产，验证 SHA256，再用真实桌面 EXE 导入本地 Engine、加载已有模型并完成对话。
5. 验收通过后将草稿公开。预览标签包含 `-preview.N`，发布为 prerelease。

CI 不下载大模型，也不宣称 GPU 推理已验收。最终模型对话属于发布前人工环境验收；
证据留在 ignored `artifacts`，发行说明记录结论，不打包个人配置和模型路径。

## 本地构建

```powershell
./harness/scripts/Build-AgentRelease.ps1 -Version release-0.1.0-preview.3
./harness/scripts/Test-AgentRelease.ps1 -Archive ./artifacts/agent-release/MoE4All-Agent-Windows-x64-v0.1.0-preview.3.zip
```

源码及子模块必须干净且匹配提交指针。不要把开发 profile、生产 DSH 数据或本机绝对路径复制进发行包。
