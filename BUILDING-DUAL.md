# API3 双版本构建 / Dual-runtime build

当前本地迁移目标为一个版本、一个 ZIP，包含 RimWorld 1.6/net472 与 1.7/net10.0；要求 API >=3.0.0 <4.0.0，不兼容历史 API2 客户端。需要 .NET10 SDK、Python3 和已核对的两版私有游戏/Harmony 编译引用。Host 同一源码构建两版，Common 从该 Host 固定的 submodule 获取。游戏/Host DLL 不进入插件包。

本地候选 / Local candidate:

```sh
python3 pack.py \
  --phinix-root /path/to/Phinix-Rework \
  --game-references /path/to/rw16-references \
  --game17-references /path/to/rw17-references \
  --harmony17 /path/to/rw17-references/0Harmony.dll \
  --version 0.0.1 \
  --output /tmp/phinix-redpacket-dual-local.zip
```

输出必须不存在；0.0.1 是本地验证用版本，占位路径需替换为真实路径。不要以本地版本覆盖正式 Release。入口隔离构建、两版绑定检查、单包合成，不安装或发布。旧 pack.py 的 --packager/--host-assemblies 参数不再适用。

main push 自动发行保留原版本分配与不可覆盖资产检查，增加两版引用、API3 能力和包校验。尚未推送或线上运行。本地 ci/config.json 已固定真实 Host 提交 3fd389e3049e653b1896a9abaf75b1232894c60c（Common 9d0710aaa9b41c07f28d9bfa25599247e7bb1f93），私有 1.7 引用 refs-rw17-v1，SHA256 41ddc7bc1dbf5bfe39059e549dd77310823621cbd46171214b43652ad10226bf。Host/Common 已推送、引用已上传；插件配置与实现暂不推送，不触发正式 Release。缺项在正式版本预留前阻断，禁止假 SHA、删预检或公开分发游戏 DLL。维护者 token 只读私有编译引用库。

Index catalog schema3 不变，但 trusted reader 与版本/语言投影需升级。第一次双版/API3 范围扩展需要新审核，之后同范围 patch 继续自动更新。编译/白盒通过不代表两版游戏业务验收通过。
