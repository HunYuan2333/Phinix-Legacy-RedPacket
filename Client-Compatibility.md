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
