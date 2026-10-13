# 当前正式包支持范围（2026-10-13）

按用户最新决定，1.2 系列正式包仅面向本次重构后的 Host API >=2.0.0 <3.0.0，不再承诺旧 API 1.9 客户端组合。打包直接使用固定当前 Host，compile-client-api 为 2.0.0。历史 adapter 暂留，旧协议和保存键未因此改动。下文为历史测试与兼容决策记录，其发行范围已由本节取代。

# 单包内部 adapter（当前方案）

每个插件发布同一个 1.1.1 包，支持客户端 API >=1.9.0 <3.0.0。原分成 1.9/2.0 两个包的方案已撤销，pack.py 不再有 --client-api 参数，也不存在独立维护线版本号。

--phinix-package 必须提供 1.9 基线公开 DLL，--game-references 提供 RimWorld 1.6 编译引用；人才贸易另提供 Harmony 引用。默认 version 从 publication.json 读取。打包工具 --compile-client-api 1.9.0 校验引用来源，插件 DLL 不引用仅新 SDK 存在的类型。

当前宿主 API/AssemblyFileVersion 仍为 2.0.0；过渡期 ClientExtensionAbstractions 的 CLR AssemblyVersion 冻结 1.9.0.0，避免在业务 adapter 运行前被加载器拒绝。新核心没有恢复已删除接口。更新前一轮 CLR 2.0 开发宿主后须重启；旧客户端 1.9 无需改动。

红包的发送识别、当前 Chat 反射及旧库存 Deliver 仅位于 Client/Compatibility/ClientAdapters.cs。读取运行时常量使用反射，避免 1.9 编译时常量内联误判。当前 Chat 停用不选择旧接口重发；业务/UI 只消费中性公告端口。人才贸易当前使用的 SDK 接口在两端一致，同一二进制直接复用这些稳定契约，无额外版本分支。

单份 ZIP 对两端的最终静态包、宿主引用与实际类型加载检查通过，尚未游戏内验收或上线。客户更新完成后再删除 adapter 旧分支并结束绑定过渡；不为此保留两份业务或混用 UI。

## T4 本地候选 1.1.2

新增 Compatibility/ThemeAdapter.cs，通过反射绑定当前 IClientTheme/IClientThemeRegistry，使用实际激活模块注册和 Commit，持有并释放 owner lease。编译引用仍为 1.9，新接口不进入二进制外部类型引用。1.9 使用插件默认配色；当前宿主的缺键用默认值补齐。UI 只消费 PluginPalette，不包含版本/schema 判断。当前 API 类型形状损坏会明确失败，不回退旧主题接口；可选主题服务缺失可以使用默认样式继续业务。

配色作用域固定当前 palette，快照版本变化时更新。注册的 redpacket.panel / .highlight / .accent 分别有真实面板、悬停与强调消费点，缺值按宿主角色回退。元数据采用中英双语字面标签，不注入宿主专属翻译入口。UI 重构之后可继续替换具体控件，adapter 边界不变。

1.1.2 候选独立提供，publication.json 默认 1.1.1 及线上 Index 未改。编译与 1.9/当前宿主静态校验通过；尚未游戏验收或执行运行时反射绑定，不能视为实际两端功能已验证。

## B28 本地候选 1.1.4：公共 Host 呈现迁移

当前 Host 的红包主页面、发送草稿、领取、详情及设置使用同一公共 presentation session、模型和 typed action point。`builtin.legacy-redpacket.presentation.actions` 的 context 只有只读投影事实；SendIntentReady/ClaimIntentReady 是当前视图的预检，不是权限、业务成功或资源授权。发送/领取仍由原插件 state machine、库存与 relay 执行；关闭视图不取消业务或自动返还。

单包仍以 SDK 1.9 编译，新的 SDK 类型仅由 Compatibility/PresentationAdapter.cs 显式绑定。1.9 保留原 UI/业务 adapters；当前 Host 需要 B28 公共 ClientPresentationMainTab 及 list role 属性，当前接口损坏或缺失时明确失败，不回退第二套 renderer。UI 投影不判断版本、schema 或协议。主题注册沿原 ThemeAdapter，redpacket.panel/highlight/accent 消费同一 Host 主题快照。原 settings keys、协议、保存和领取算法未改。

离线证据：插件编译 0 errors / 0 warnings；20 项 repair seam；实际公共模型和 fake session/registry 上 22 项当前绑定检查，另有 3 项实际 1.9 类型/provider 绑定检查，两组也在 Mono 4.7.2 运行通过。不是 Unity/游戏/真实联机或物品交付验收。候选 ZIP 1.1.4 的 1.9/当前 Host 静态校验通过；publication.json 默认 1.1.1、线上 Index 与发布未改。

离线打包可用 `--framework-references /usr/lib/mono/4.7.2-api` 提供编译引用身份；这些 DLL 不进入插件 ZIP。当前 UI 必须与同批 B28 Host 合批测试；1.9 实机另待测。

## B30 候选 1.1.5：无变化过期轮询不失效确认框

用户实机反馈页面持续提示已变化、确认框自动关闭。UpdateExpiry 原每秒无条件 MarkBadgeDirty，即使没有实际过期，导致呈现 guard 失效及 page 重发。本批只在实际 packet.Expired 从 false 变 true 时通知；真实过期继续失效旧动作，原过期/返还/特殊物品结算和协议不变。当前/1.9 共享同一通知修复，不引入版本分支或放宽 Host guard。

实际 candidate DLL 检查修复前复现 idle poll 版本递增，修复后累计 26 项当前模型/绑定/实际过期通知检查通过；空轮询、未过期、真实过期、已过期再轮询分别验证。20 repair seams 通过，插件编译 0 errors / 0 warnings。游戏确认框与真实发送仍待复测。候选 1.1.5，publication 默认版本未改；旧候选保留，未发布。

## B31 1.1.6：补齐领取超时轮询的无变化通知修复

B30 遗漏 CheckClaimTimeouts 每秒无条件 MarkBadgeDirty，用户反馈仍持续刷新。本批将通知移到 timedOut.Count > 0 后，两个定时路径均无变化不发版本更新，真实过期/真实等待超时仍通知。实际 DLL 回归增加同时空轮询及未超时 pending，合计 28 checks 通过。中英超时文案改为本机结果待确认，不猜发送者离线；不修改业务协议/授权/20 秒阈值/交付/保存。

发送方记录某 UUID 已领取，是发送方处理相应 Claim/Assign 的本地记录，不是领取方实际物品交付的确认。领取方先 DepositReward 成功再更新本机 ClaimedUuids；网络或本机交付阻塞时状态可分歧。AuthorizedLocalClaims 在视觉超时后保留，延迟结果继续按原校验处理；失效/断线/过期等边界不能冒充交付成功。Player (4).log 缺少 Phinix/RedPacket 运行记录，尚无法定位该玩家的网络或交付具体故障，未据此放宽 guard/去重/交付条件。游戏待复测，默认发布版本不变。

## B32 1.1.8：通用无状态组件合组、左栏固定发送区

用户拒绝按物品/mod 特判。本批未加入 CompDrug 或 defName 特例；对非既有 per-unit 类型使用通用 ThingComp 判定：从当前类型至 ThingComp 不允许任何声明实例字段，保存/PreAbsorbStack/PostSplitOff/AllowStackWith 必须仍由 ThingComp 实现。判定按 Type 缓存。合组及发送共用判定，仍需完整保存状态一致、双向 CanStackWith、完整源数量校验；品质/耐久/配料/腐坏/任务/揭示/tick 与未知状态不擦除。状态不明的仍隔离；不跨库存/地图来源合并或改变预留授权。

Host 公共 WithFieldColumns 与 WithFixedFooters 同批补齐。红包左栏顶部搜索/刷新、中部独立物品列表、底部固定发送草稿，数量/份数字段按宽度并排或回退单列。发送与领取反馈按各自区域显示。小窗底部区独立滚动，列表仍保留空间；所有控件仍来自同一 Host，未私有 Draw。当前 SDK 2 Host 需同批 B32；旧 1.9 UI adapter 保留，不要求其实现这些属性。

219 Host stub checks、22 repair seams、29 actual-model/fake-session/状态通知检查通过；游戏待测。候选红包 1.1.8、Host B32、Example 1.0.33；原版本默认和发布未改。

## B34 1.1.9：刷新局部粗筛索引与完整状态核对

GroupPhysical 先按实际实体 Type、ThingDef/Stuff 引用、HitPoints、品质及组件类型组合做 coarse bucket；singleton 不保存/解压/哈希。多堆桶内按规范化完整保存状态 SHA256 找候选，哈希相同再比较原文并检查双向 CanStackWith；发送前仍 PreparePhysical/Capture 复核完整原堆。删除 UI 刷新前 StackedThings.GroupThings 的两两预分组，避免重复扫描。没有物品名/mod 特例，不把索引视为授权或业务结果。

索引只在一次刷新内持有，不跨刷新缓存可变保存状态。保留最多 1000 堆精确检查和 4 Mi 字符预算，分组检查解压 XML 限 128 KiB；超限/未知状态保持独立可选，发送原单堆仍用原恢复能力。分组节省调用并不意味着分帧：刷新仍同步，5000 粗筛单例测试 0 次序列化、1500 同桶测试精确检查 1000 并隔离保留 500，只是离线操作计数，非实机耗时。复杂同桶、地图收集、预览、主线程序列化仍可能慢，分帧与真实 profiling 后续待办。

30 repair seam（包含 hash 强制碰撞、粗筛不同品质/HP/stuff/components、大状态、预算、发送前变化）；29 当前 actual model/fake session 检查通过。SDK 1.9 编译与候选静态校验通过，游戏性能和实际合并发送待测。Host B33/Example 1.0.34 无变更，publication 默认版本未改，未发布。

## B35 1.1.10：性能白盒样例与失败预算修复

Tests/RedPacketPerformanceTests 链接生产 GroupPhysical/PreparePhysical，使用明确的 Verse/合成 XML+gzip serializer，覆盖 13 场景/24 管线。每条 2 次预热、15 次采样，Release .NET 10 tiered compilation 关闭；计时/分配/序列化计数与守恒检查完整记录。raw before/after JSON 保留。不能当游戏性能或 Mono 等价结果。

压力样例发现 oversized 解压失败绕过总字符预算，200 次 serializer；现在失败也扣保守最大检查成本，最多 32 次，再保留所有剩余独立源。约 344 → 56 ms、208 → 33 MiB，31 repair cases 中新增预算回归修复前失败、修复后通过。29 actual model/fake session 检查通过。现同步粗筛在互异场景收益明显，但同桶复杂状态仍约 78 ms；分帧尚未实现。Host B33/Example 1.0.34/API/协议/保存/发送上限不变。候选红包 1.1.10，游戏待测、默认发布版本不变。

## B36 1.1.11：主线程分帧索引

现代公共 presentation 的 refresh 创建本地 GroupingJob，每次 ViewUpdate Advance 2ms软预算/2048步骤。准备中禁发送、完整结果一次发布；中间 slice 不改变 page revision。刷新/关view/stop/Game或uuid变化释放未完成索引/临时预览，不取消业务；发送仍复核原实体。不增加 Host contract，不更改1.9 adapter、协议、保存或交付。

33 repair seams、29 actual-model/fake-session、13样例35管线白盒重跑和SDK1.9编译通过。单次serializer与GC不可抢占，地图收集/库存preview/最终projection仍同步，发送复核仍同步；游戏待测。候选红包1.1.11，Host同批B37（仅库存业务新增API）或原B33，Example1.0.34保留。
