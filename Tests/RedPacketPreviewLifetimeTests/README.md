# 库存预览寿命回归

使用最终 ZIP 中实际插件 DLL 和本机游戏程序集，构造独立 Game/Thing，调用真实状态机 Tick。测试禁止网络发送，冻结中继轮询；不读取玩家存档或调用库存写入 API。

```sh
python3 Tests/RedPacketPreviewLifetimeTests/run.py \
  --package /absolute/plugin.zip --host-package /absolute/phinix-rework \
  --game /absolute/game-assemblies --game-version 1.7 --output /tmp/fresh-probe
```

1.6 使用 `--game-version 1.6`，需要 Mono 和完整游戏托管依赖（包括 Assembly-CSharp-firstpass、Unity.Burst）。1.7 使用 .NET 10。游戏 DLL 只用于本地测试，不提交或打包。`--expect-bug` 对 1.2.12 运行同一退出路径，要求真实 NullReferenceException；修复版正常运行检查 12 项。合成 Def 必须设置 destroyable=true，否则无法覆盖真实 Destroy 末端。

组件回归不能替代游戏内打开列表、退出、重进及换档五轮验收。
