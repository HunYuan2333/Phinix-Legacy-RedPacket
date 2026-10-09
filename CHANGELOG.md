# 更新日志 / Changelog

## 1.0.2 - 2026-10-09

### 修复

- 物品拆堆前校验完整原始状态并保存发送模板，修复等价多堆物品发送时因拆堆状态变化而被拒绝的问题。
- 物品列表与发送预检采用相同分组规则；单个物品堆足以满足数量时优先使用单堆，未知自定义组件仍保留单堆选择。
- 保留 tick、揭示记录、任务标签、品质和组件状态等有效差异，不将不同状态的物品错误合并；发送模板不再携带发送者地图状态。
- 红包完成与过期通知改为有界、去重、只保存值的队列。通知环境暂不可用时等待，切档、账户变更及插件关闭时丢弃旧通知。
- 通知调用异常不再阻塞已提交的物品事件，也不会重发结果未知的通知或重复奖励。

### 兼容与验证

- 保留包 ID、模块 ID、程序集身份、设置键、中继协议和既有物品交付/恢复规则。
- 17 项算法回归、生产插件编译及本地包静态/宿主引用预检通过。
- 本轮 1.0.2 本地游戏测试已由用户确认通过；不代表覆盖所有第三方物品组件与中继配置。
- 停用或卸载前仍须结清活跃红包并领取返还物资，安装和更新后须重启游戏。

### English

- Validate intact source state and retain the send template before splitting stacks, fixing rejection caused by split-time state changes when sending equivalent items from multiple stacks.
- Use the same equivalence policy for UI grouping and send preflight. Prefer one sufficient stack; unknown custom components remain single-source only.
- Preserve authoritative tick/reveal history, quest tags, quality and component state. Do not merge different item states; detach the wire template from the sender map.
- Queue completion/expiry notices as bounded, deduplicated value copies. Defer when game context is unavailable and discard old notices on save/account changes or shutdown.
- Isolate notification failures from committed item events, without retrying uncertain notification side effects or awarding items twice.
- Preserve package/module/assembly identities, settings keys, relay protocol and existing item delivery/recovery behavior.
- All 17 algorithm regressions, production compilation and local package/host-reference preflight passed. The user confirmed this local 1.0.2 game test passed; this is not blanket certification of every modded item or relay configuration.
- Settle active packets and retrieve returns before disabling/removing the plugin. Restart the game after installation or update.
