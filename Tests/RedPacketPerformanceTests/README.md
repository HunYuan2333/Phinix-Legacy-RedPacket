# 红包分组白盒性能样例

链接真实 `Client/RedPacketStackTemplate.cs`，复用明确的 Verse 测试替身；合成 serializer 真正构造 XML、gzip、随后调用生产解压/规范化/哈希/精确分组和发送复核。没有实际 RimWorld Scribe、地图收集、GUI、网络或交付。

```bash
dotnet restore Tests/RedPacketPerformanceTests --ignore-failed-sources
DOTNET_TieredCompilation=0 dotnet run --project Tests/RedPacketPerformanceTests -c Release --no-restore -- /tmp/redpacket-performance.json 15
```

JSON 路径和采样次数可调整，建议至少 15 次。每条 pipeline 两次预热，每次测量前主动 GC；计时包含生产分组与实际合成 XML/gzip serializer，排除 fixture 创建和测量后的原堆守恒断言。记录中位/样本 p95/min/max、当前线程累计分配字节、序列化次数、输出组数、警告次数。分配量是过程分配量而非峰值常驻内存。对时间不写不稳定的 CI 阈值，数量守恒/确定性计数失败会终止。

`eager-reference` 是仅供测量的逐项精查参考，采用相同 1000 次/4 Mi 字符预算，没有 coarse bucket 跳过；不含旧 UI 的两两 CanStack 预分组，不等于完整旧版性能。哈希代码是测量参考，不能用于业务；生产仍完整原文复核。各 pipeline 的预算访问顺序不同，因此混合场景可能输出不同合并组数；实体数量和数量总和必须全部保留。

场景覆盖同类简单物品 500/1000/10000 堆、10000/50000 粗筛单例、混合品类、8/32 KiB XML 状态、160 KiB 超限、stateless/unknown-stateful 组件及发送时 500/1000 原堆复核。超大数据是边界压力样例，不表示日常存档会有相同布局。

`Results/before-budget-fix.json` 和 `Results/after-budget-fix.json` 保存相同受控设置下各 15 次的结果及生产源码摘要。源码由 B34 → B35 修复失败解压不扣预算问题。不能将这些 .NET 10 测量换算成游戏 Mono 的帧耗时；复杂同桶仍需实机 profiling 和后续分帧设计。

B36 新增 `sliced-index`，13 fixtures / 35 pipelines；`Results/sliced-index.json` 为分帧最终重跑。记录各轮最大 slice 的中位/所有采样最差 slice 与代表性 slices 数。2ms 为软预算、最多2048步，单次序列化与GC不可抢占；不包含地图收集、预览、UI更新和每帧等待。分帧改善阻塞峰值，不代表总 CPU/分配减少。实际 Mono 与游戏 profiling 待测。
