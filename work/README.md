# 影之刃本地离线修复记录

此目录保存离线修复使用的源码、逆向分析参考和测试工具。`tmp` 用于临时文件，
`dist` 用于生成 APK，二者均不纳入 Git。

## 当前修复

- 商城中可堆叠碎片按实际购买数量发放。
- 商城不再扣减限购次数，允许无限购买。
- 商城购买数量参数会用于扣款、发放和弹窗显示。
- 死斗血石商店的多数量材料按购买数量发放。
- 北斗秘境场景 `51601` 至 `51604` 映射为
  `FinishRuneFarmLevel`，使“通关2次日常·北斗秘境”正常计数。
- 原有 `FinishAnyLevel` 任意副本计数路径保持不变。
- 论剑增加 `1x` 人机倍率，倍率顺序为 `1x/2x/4x/6x/8x`，并适配四选一
  界面之外的上下滑动与选项排序。
- 签到补签不再受每月次数限制。
- 旧活动中心恢复为纯本地目录，按配置保留 12 个选定的历史活动。
- 旧活动 `1-21` 至 `1-24` 支持本地进度、每日重置、领取和防重复领取：
  - `1-21`：普通副本和扫荡消耗的体力。
  - `1-22`：元宝消费。
  - `1-23`：每日离线充值金额。
  - `1-24`：每日离线充值金额，与 `1-23` 一样每日刷新。
- 旧活动 `1-2`、`1-3`、`1-5` 至 `1-9` 已接入实际结算倍率：
  - `1-2`：密令·强敌掉落翻倍。
  - `1-3`：宝石猎场掉落翻倍。
  - `1-5`：密令·血战掉落翻倍。
  - `1-6`：紧急副本天书掉落翻倍。
  - `1-7`：死斗血石奖励翻倍。
  - `1-8`：论剑彩券奖励翻倍。
  - `1-9`：招财金币及招财令关卡金币翻倍。
  - `1-4` 乱入三倍暂未处理。

## 工具

- `tools/DailyBeidouFix.cs`：为北斗秘境增加精确场景分类。
- `tools/StackQuantityFix.cs`：让商城可堆叠物品按购买数量发放。
- `tools/UnlimitedStoreFix.cs`：移除商城限购检查及限购次数扣减。
- `tools/StoreBundleQuantityFix.cs`：修复血石商店捆绑材料的实际发放数量。
- `tools/UnlimitedResignFix.cs`：解除签到补签次数限制。
- `tools/DuelFactorOneFix.cs`：增加 `1x` 论剑人机；调整倍率排序和滚动区域。
- `tools/OfflineActivityPagesFix.cs`：用本地活动表恢复旧活动页面。
- `tools/OfflineOldActivityFilterFix.cs`：筛选保留的 12 个旧活动。
- `tools/OfflineActivityRewardsPatch.cs`：把活动进度和领取入口接入本地结算。
- `tools/YzrLocalActivity.cs`：活动进度、每日重置、奖励发放和防重复领取模块。
- `tools/apply-store-fix-test.ps1`：覆盖 MuMu 模拟器中已安装的 APK，
  保留 `/data/data/m.Sgame.yzr` 存档。
- `tools/inspect_quest_bundle.py`：通过 UnityPy 导出任务表。

## 外部工具

- `D:\Netease\MuMuPlayer\nx_main\adb.exe`：MuMu 模拟器 ADB。
- `E:\AI Games\tools\unitypy`：解析 Unity AssetBundle。
- Android SDK Build Tools `36.0.0`：`zipalign` 与 `apksigner`。
- `D:\Java\jdk1.8.0_351`：更新 APK 内 ZIP 条目。
- `D:\Android Studio\jbr`：运行当前 Android 签名工具。

## 本地构建约定

APK 中的 `Assembly-CSharp.dll` 使用逐字节 `0xFF` 异或保护。修改后的程序集
必须重新异或，替换回 APK 后执行 zipalign，并用本地测试证书重新签名。

活动进度模块作为独立程序集 `YzrLocalActivity.dll` 放入
`assets/bin/Data/Managed/`，主程序集通过程序集引用调用。

