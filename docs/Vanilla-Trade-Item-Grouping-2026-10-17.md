# 原版交易列表与红包选物归类（2026-10-17）

## 结论

原版使用“一行交易项，对应多个真实 Thing 来源”的模型。浏览/数量草稿/物品执行分别处理。应借鉴这个模型和就地数量调整方式，继续通过 Phinix Host 的公共 renderer、主题、动作和确认投影，不把 Dialog_Trade 原生 Draw 塞进插件，也不复用 TradeSession 全局交易状态。

本轮用户要求先做同物品单行展示、发送以后讨论。列表合并不代表已能跨不兼容状态发送；当前仅单来源选择保留原发送行为，多来源显示汇总和明确暂不可发送说明。该候选用于浏览验收，不是跨堆发送完成版本。

## 核验范围

只读检查本机编译参考 DLL 的托管方法体，未启动/修改游戏。参考版本不代表用户正在运行的安装版本：

| 版本 | Assembly-CSharp 身份 | MVID |
|---|---|---|
| 1.6 | 1.6.9438.37837 | 46372f5d-adbf-4af8-939e-608076251180 |
| 1.7 | 1.7.9782.37458 | de18e239-08d3-45a3-b8ee-b67b13536b54 |

公开反编译源码仅作为可阅读的辅助来源；版本不固定，以本机两个 DLL 中对应方法的调用/字段证据交叉核验。未将完整反编译游戏源码或 DLL 加入仓库/插件包。

## 原版怎么做

1. `TradeDeal.AddToTradeables` 调用 `TransferableUtility.TradeableMatching` 寻找已有交易项；无匹配才新建，随后 `AddThing` 保留原物品引用。
2. `Tradeable` 内部维护殖民地、商人的 Thing 列表。`CountHeldBy` 遍历 stackCount 求和，因此行总量不受单堆容量限制。没有为列表显示调用物理吸收/销毁。
3. `TransferAsOne` 不是只比较名字/Def。两版方法体均包含 tradeNeverStack、Stuff、品质、影响价格的耐久差异（相关条件下差值至少 10 拆分）、食物/衣物等分支，普通 Item 最后调用 `CanStackWith`。不能把“同名一行”理解为所有业务状态均等价。
4. `TradeUI.DrawTradeableRow` 同行显示信息、数量和调整控件；`TransferableUIUtility.DoCountAdjustInterfaceInternal` 包含数字直接输入、普通步进和快速跳转，并用允许范围约束调整。不能交易的原因在该行显示。
5. `Tradeable.ResolveTrade` 调用 `TransferNoSplit`，遍历保存的原 Thing 列表，把目标总数分摊给各来源。原版商人交易是本机直接转移；它不需要把多来源压成一个网络模板。

公开阅读依据：[TradeDeal](https://github.com/Chillu1/RimWorldDecompiled/blob/master/RimWorld/TradeDeal.cs)、[TransferableUtility](https://github.com/Chillu1/RimWorldDecompiled/blob/master/RimWorld/TransferableUtility.cs)、[Tradeable](https://github.com/Chillu1/RimWorldDecompiled/blob/master/RimWorld/Tradeable.cs)、[TradeUI](https://github.com/Chillu1/RimWorldDecompiled/blob/master/RimWorld/TradeUI.cs)、[数量控件](https://github.com/Chillu1/RimWorldDecompiled/blob/master/RimWorld/TransferableUIUtility.cs)。

## 与当前红包的差别

红包旧列表直接把 `GroupPhysicalSteps` 的发送分组投影成一行。该分组为了保持既有单模板协议，带有序列化检查、字符/数量预算和状态一致性约束；失败或无法判定时回退单堆。于是界面把发送层限制放大成了大量重复行。没有该次游戏异常日志，不能确定截图中的化合燃料具体命中了哪个回退条件。

当前 `PreparePhysical` 仍从来源状态选择一个 wire template。它和原版逐原物品转移不是同一个保证。直接删掉状态校验，或用第一堆的完整状态替代全部来源，会改变物品状态；本轮不这样处理。

## 已实施的展示切片

- 新增插件内部 `RedPacketItemChoice`，维护原选择项引用和 64 位总量，与发送模型分离。
- 用真实 Def、材质、样式和品质区分变体，正常同类资源只出现一行；不同 Def 即使同名也不混在一起。不同来源可以在同一浏览项汇总，并标注“地图与库存”。列表不再显示“仅此物品堆”限制标签。
- 分页按商品行计算，查询在合并后的商品名上进行；查询不重新序列化 Thing，也不因过滤而丢失所选商品草稿。数据刷新重建选择，语言变化保留仍有效来源，旧动作由版本/Game/草稿等原 guard 及 item epoch 拦截。
- 单来源项沿用原数量和发送入口；多来源只保留数量草稿，不绑定某一个来源冒充整组发送，明确显示暂不可发送。
- 这不是物理合堆，不改存档、库存、物品状态、原传输协议或确认规则。没有提交、推送、发布或安装。

## 后续发送设计要点（本轮未实施）

沿用原版“商品行 → 数量草稿 → 来源分配”的结构，先将数量分配成多条真实来源计划，再逐项复核数量/状态/归属与失败恢复。地图物品与库存记录采用各自原有扣取/预留权威；不能仅凭一个汇总数混用两条交付路径。

若同一红包仍只有一个完整状态模板，应分别讨论：只允许精确等价来源跨堆、使用可无损表达多个来源的格式，或明确的业务归一化规则。后两种涉及协议/物品语义，不作为 UI 小改直接实施。游戏的 CanStackWith 可作为匹配条件之一，不足以证明多份不同 Scribe payload 能压成一份。

## 检查与限制

12 项 source-linked 分组检查通过，含 120 堆 × 750 → 一行 90,000、地图/库存汇总、同名异 Def、材质/品质/样式分隔、空来源、大总量不溢出、原始数量不变与单来源保留原绑定。以最后交付的库存计划修正版 Host ZIP 为精确编译引用，两版插件完整源码编译通过；不混入其他任务正在修改的 Framework/Trade 源码。1.7 保留两项既有 WebRequest obsolete 警告。

游戏内化合燃料、长名称、原生输入、来源变化和跨堆发送仍须分开验收，编译/算法检查不等于游戏验收。

两版实际 ZIP 的引用预检均通过（hostReferencesChecked=true），以及两版各红包 38、人才 86、商店 109 项消费者检查通过。候选 SHA256：`90b18dd9a153fcf7cbde06806c032a69f8746e46be481acbb96944e949a5068b`。
