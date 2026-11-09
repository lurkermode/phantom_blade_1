# 影之刃本地离线修复记录

此目录保存离线修复使用的源码、逆向分析参考和测试工具。`tmp` 用于临时文件，
`dist` 用于生成 APK，二者均不纳入 Git。

## 当前修复

- 商城中可堆叠碎片按实际购买数量发放。
- 商城不再扣减限购次数，允许无限购买。
- 商城购买数量参数会用于扣款、发放和弹窗显示。
- 北斗秘境场景 `51601` 至 `51604` 映射为
  `FinishRuneFarmLevel`，使“通关2次日常·北斗秘境”正常计数。
- 原有 `FinishAnyLevel` 任意副本计数路径保持不变。

## 工具

- `tools/DailyBeidouFix.cs`：为北斗秘境增加精确场景分类。
- `tools/StackQuantityFix.cs`：让商城可堆叠物品按购买数量发放。
- `tools/UnlimitedStoreFix.cs`：移除商城限购检查及限购次数扣减。
- `tools/apply-store-fix-test.ps1`：覆盖 MuMu 模拟器中已安装的 APK，
  保留 `/data/data/m.Sgame.yzr` 存档。
- `tools/inspect_quest_bundle.py`：通过 UnityPy 导出任务表。

## 本地构建约定

APK 中的 `Assembly-CSharp.dll` 使用逐字节 `0xFF` 异或保护。修改后的程序集
必须重新异或，替换回 APK 后执行 zipalign，并用本地测试证书重新签名。

