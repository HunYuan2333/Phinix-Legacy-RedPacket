# 红包 1.2.10 本地候选

分支：`codex/redpacket-sqlite-compat-20261110`，从 `a4a88ad` 建立，保留并纳入已有 RP-REL 紧凑编码/领取退避工作。修改前未提交文件已备份到 `/tmp/phinix-redpacket-before-sqlite-compat-20261110`。不推送 main、不执行正式 release/publish。

本次定位：日志的 `CodecUnavailable` 发生在库存入库校验、SQLite 提交之前；仅凭旧日志不能确定具体物品或校验分支。独立插件需要单独更新，主模组 ZIP 没有它。新增日志细分缺物品/材料 Def、状态堆叠边界和解码失败，不伪造缺失 Mod 的物品，也不自动丢掉失败领取。

实际联测发现并修复了当前候选中的另一处真实拒绝：紧凑带状态记录 Quantity 可大于一，但 GetAggregationKey 仍返回 null，触发库存的 indivisible 检查。现在仅显式 RepeatedStacks 的完整相同合法堆可按 payload key 保存；旧单堆仍保持不可分割，物化每次只处理一完整堆，原状态字节保留。

没有把红包所有业务文件迁到 SQLite。本轮接收/发送仍通过中立库存合同使用 SQLite 库存，原发送托管和接收退避文件保持原路径。不会宣称旧审计的红包全部存储问题已关闭。

验证：

- net472/Mono 可靠性与真实 codec 校验：131 项通过。
- net10 + 真实 InventoryLedger/InventoryDatabaseStore + SQLite：136 项通过，覆盖 999999 WoodLog 入库、完整状态字节、数量保持、重复 depositId 去重、同 ID 内容冲突、重开库/跨 Game 余额及回执保持。只构造 Def fixture，未物化真实世界实体。
- 发送/状态/通知修复：49 项算法测试通过；物品选择：14 项通过。
- 双版本构建与 schema2 variants 包 static binding/CRC 检查通过；最终包的摘要另附制品 manifest。
- Mono 有缺 Unity.Burst 属性依赖的警告，所测用例完成通过；这些不是游戏启动或 Unity/native 物化验收。

最终版本为本地候选 1.2.10。线上查询到的最新版本为 1.2.9；预留正式自动发行最低 1.2.11 以免与本地制品同号。`ci/config.json.hostCommit` 没有伪造或替换为未验收 Host；本地 pack 明确使用当前 SQLite 主包工作区。仍需原问题物品、双端领取、暂停后重试、重启和提取黑盒验证。
