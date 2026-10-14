# Phinix-Legacy-RedPacket

本仓库是 Phinix Rework 的独立插件仓库。保留工作区修改，不 reset、不用远端文件覆盖源码；只修改任务授权范围。当前 1.6 收口以 UI/UX roadmap 与真实源码为准，编译/白盒通过不等于游戏验收通过。1.7 的引擎迁移与边界整理另行实施。

## 呈现与业务边界

当前 UI 使用 Host 同一套公共 presentation/theme/action/confirmation 能力，不建立插件私有 renderer 或第二套动作注册。插件保留校验、操作、资源与业务权威；回调返回不等于交付成功，关窗不取消业务或自动返还。变更必须同时维护当前消费者与必要示例。不要把开发术语写进玩家提示，提示应说明当前状态和下一步；结果未知、防重复交付等必要提醒不得删掉。

## main 自动发布流程

- 正式流程是 `.github/workflows/release.yml`：本仓库 `main` 的每次 push 会自动构建并发布，不是只有 tag 才发布。普通分支/PR 用于准备与检查。未经本次任务明确授权，不推送 main、不手动创建 Release/tag、不上传或发布制品。
- `ci/config.json` 的 `baseVersion` 是版本基线；`ci/release.py` 扫描对应 major/minor 的全部 GitHub Releases（含 draft），分配未占用的 patch。同一 source commit 的重跑复用原版本和 draft，不分配新版本。当前收口基线 **1.2.0**；后续 main push 会递增 patch。`publication.json`/工程版本和本地 `pack.py --version` 不是线上分配器。
- 本地候选编号与正式版本分开；正式版本必须高于已经提供给用户的候选，避免升级倒退。保留旧 ZIP；同一正式版本的资产不可覆盖为不同字节。`ci/publish.py` 核对 source SHA、固定 Host SHA、摘要和资产 SHA，部分失败只能以相同制品重跑。
- Host 必须先完成验收并形成真实最终 commit，再更新 `ci/config.json.hostCommit`。workflow 的 checkout 从该配置读取，不另写第二个 SHA。固定 gitlinks 与编译引用 digest，不跟随移动 main，也不更新 vendored protobuf SDK pin/source；构建引用只能用于编译，不能打进插件 ZIP。
- `requiredPresentationMembers` 是发布预检，本轮要求 `WithImage`。旧 Host 缺契约或成员时 `ci/build.py` 会拒绝构建；不得删检查来绕过。当前本地 Host pin 已固定为 main 的真实 API3 双版本提交；更新 pin 必须重验，不填写假 SHA。
- 发布前：核对共享文件所有权，完成源码/语言资源摘要和占位符检查、必要白盒/编译/包 static references/CRC 检查；核对游戏验收记录与最终测试组合。把 version baseline、Host pin、源码、资源和摘要作为一批合入，减少中间 main push 产生的不完整正式包。
- 不在本地执行 `ci/release.py` 或 `ci/publish.py` 做测试：它们会写 GitHub。版本算法和发布检查用模拟 API 的单元测试；允许本地 `pack.py` 生成候选。禁止提交凭据、真实服务器/玩家日志、GameDlls、构建输出、ZIP 或运行期数据。

## 当前发行目标

当前用户授权的发行目标是 API >=3.0.0 <4.0.0：一个产品版本、一个 schema2 variants ZIP，同时包含 RimWorld 1.6/net472 与 1.7/net10.0。两版使用 ci/config.json.hostCommit 同一真实最终 Host 源码，不新增旧客户端组合分支；内部历史 adapters 不代表旧客户端兼容承诺。不改业务协议、保存键或确认规则。references17 未登记或旧 Host 缺必要能力时，workflow 必须在分配正式版本前停止。刷新语言资源后更新 Client/localization.json 与 source-manifest.json，并执行 python3 check-source.py。正式 Host pin 与 Index trusted snapshot 必须等真实最终 Host/Common 提交后同步，禁止假 SHA 或游戏 DLL 公开上传。
