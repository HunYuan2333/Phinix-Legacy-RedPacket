# 红包与共享 SQLite 库存接口核对

版本：1.2.11 本地候选，分支 `codex/redpacket-sqlite-compat-20261110`，基于已推送的 `0eb5f40` 继续修正。此结论限于已检查接口与用例，不宣称完整游戏业务黑盒已验收。

## 核对结论

| 消费接口/语义 | 核对结果 |
| --- | --- |
| IInventoryRegistrationApi | 普通模块激活注册自己的 codec 和来源 presenter，持有 scoped registration，停用释放；不借 Host 特权注册 |
| IInventoryDepositApi | TryDeposit/CheckDeposit 通过唯一 depositId 及请求内容去重，未要求存档保存回执；旧整笔/逐堆格式仍核对原收据 |
| IInventoryReadApi | 只通过 CanWrite、InventoryChanged 读取状态；没有存档文件路径、SaveIdentity 或 JournalIdentity 格式假设，也不要求 SavedCheckpoint |
| IInventoryReservationApi | GetAvailableSnapshot/预留数量是库存单位，物品数可以是单位数乘整堆数量；本次修正此换算，结算保持 owner/operation/reservationId |
| 跨档范围 | 接收与余额使用共享库；Current.Game 检查用于清理原世界 UI、通知与物理动作，不作为数据库余额选择键。切档不把共享余额从旧存档重建 |
| Abandoned | 新库存已放弃的预留是终态，不退款或重发；本次修正恢复筛选，继续保留 Committed 发送的正常后续核对 |
| 存储边界 | 红包借中立库存接口使用共享余额，不直接访问库存表；自身发送托管/接收退避文件没有在本次全面迁移 SQLite |

## 修复两处真实不兼容

1. 原 RedPacketAvailableItem 假设有 AggregationKey 的记录每单位就是一个物品。新紧凑状态记录可每单位代表完整 75 个物品堆，导致发送列表数量偏小、预留单位错误。本次缓存 codec 的单位物品数，显示真实数量，预留向上换算，按物化上限约束单位；没有每帧重新解码 payload。选择 76 个、每单位 75 个时预留 2 单位，原返还路径退回未选的 74 个，稳定返还回执防重复。
2. 原恢复筛选只排除 Restored，未排除新增的 Abandoned。本次只保留该 owner 的 Reserved/Uncertain/Committed，不把 Abandoned 再纳入恢复。

保留旧不可分割单堆格式和其他 codec 原有按单位处理方式；不是对任意第三方自定义 codec 的跨类型语义承诺。未知发送结果不会因切档或本次更新自动退款。

## 实际验证

- 更新版 1.6/net472、1.7/net10 双版本构建及包 static validation、CRC、模块和兼容范围检查通过。
- 以真正交付的 `Phinix-Rework-SQLite-Native-Fix-1.6-1.7.zip` 内 Host/Contracts DLL 提供方，重新验证两个插件变体的静态引用绑定，均为 hostReferencesChecked=true。
- 150 项检查通过。使用实际已编译红包 DLL（通过既有测试友元访问）与生产 InventoryLedger/InventoryDatabaseStore 源码、真实 SQLite，覆盖紧凑入库/完整 payload、旧收据、重复/冲突 depositId、跨 Game 重开、单位换算、预留/结算/未选余量返还及返还去重、旧单堆、普通 foreign codec 选择及 Abandoned 恢复筛选。
- 关键数量：999999 入库后保持；整堆条目显示 999975，另有余堆 24；选择 76 个预留 2×75，返还 74，余额最终为 999923。
- 使用合成 Def/Thing fixture，未执行真实 Scribe 物化、网络发红包或世界投放。因此实际提取、双端收发、断线和游戏重启仍需黑盒。

复现入口：`Tests/RedPacketReliabilityTests/run-sqlite.py`。包与日志保存在 `/home/hunyuan2333/Phinix/Deliverables/Phinix-RedPacket-Inventory-Compat-2026-11-12`。

本地候选使用新编号 1.2.11，不覆盖已交付的 1.2.10 字节。正式自动发行最低编号预留 1.2.12；未修改正式 Host pin、未推送 main 或手动发布 Release。
