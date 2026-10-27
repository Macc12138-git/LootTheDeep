# 区域容器爆率配置：首版使用说明

本次实现对应[开发方案](RegionContainerLootDevPlan-v0.1.md)。已接入容器配置和随机物资生成；容器随机出生、完整行动结算及物资存档不在本次实现范围。当前玩法方向见[设计原则](DesignPrinciples.md)：容器掉落配置不决定战斗遭遇或强制清场规则。

## 打开与操作

1. 打开 `Assets/Scenes/ExploreScene.unity`。
2. 当前地图选择 `MapContainers`（探索物资区）；实际试玩操作见[地图搜索使用说明](WorldSearchUsage.md)。旧水下示例的“机械维修区”和“电力控制区”保留在配置资源与历史验证中，不是当前场景层级。
3. 在 `RegionContainerManager` Inspector 点击“区域容器爆率控制”。
4. 列表显示名称、类型、世界位置、生效来源、掉落表及每次抽取的稀有及以上概率。点击名称查看详情，点击“定位”聚焦 Scene 视图。
5. 按名称或层级路径搜索，按类型、来源、配置状态筛选。勾选多个容器，指定掉落表或恢复继承。筛选不会清除已有选择；顶部显示被筛选隐藏的已选数量，批量操作包含它们。
6. 单容器详情可修改类型、物资模式和掉落表。要只调一个箱子，先“创建独立副本”，再改副本的权重；直接编辑共享 SO 会影响其他引用者。
7. 展开“区域类型默认规则”，设置某类容器的默认表。移除类型映射后，未单独配置的该类容器使用自身默认表。
8. 修改引用后保存场景，修改 SO 后保存项目；引用和批量配置支持 Undo/Redo。

`LootTheDeep > 搜索 > 接入地图容器` 是当前地图接入入口。`LootTheDeep > Install Region Container Loot` 是旧区域配置示例入口：后续发现已安装区域则保留现有配置并打开窗口，不重置数值。

## 配置资源与生效优先级

资源位于 `Assets/Data/Inventory/Loot/`：五张掉落表、两份区域映射配置。Project 的 Create > LootTheDeep 菜单可创建 Loot Table 与 Region Loot Config。

随机模式下：**容器单独配置表 → 所属区域的类型默认表 → 容器默认表**。预制体已配置通用默认表；场景实例可覆盖这一引用。窗口中的“预制体默认”表示最后一级默认来源，实际读取的是该实例的 `defaultTable`。

`FixedPrototype` 明确使用原有固定物资；`RandomTable` 使用随机掉落。批量指定与恢复继承都会切换为随机模式。已有但错误的区域映射会报错，不会偷偷退回通用表；移除不需要的映射即可恢复正常继承。

| 旧区域示例 | 配置 | 用途 |
| --- | --- | --- |
| 机械区维修箱 | MechanicalLoot | 展示同类型在不同区域使用不同表 |
| 机械区医疗柜 | MedicalLoot | 展示不同类型使用不同物品池 |
| 电力区维修箱 | ElectricalLoot | 与机械区维修箱对比 |
| 电力区特殊储物柜 | SpecialLoot 单独配置 | 展示单容器覆盖及核心奖励组 |

这些数值为功能演示，尚未做经济平衡验收，不能当作正式爆率。医疗表当前只有现有的绷带物品。

## 抽取规则与概率含义

每张表包含多个抽取组，各组依次执行：

- 在 `minRolls` 到 `maxRolls`（含端点）之间等概率决定份数。
- 每份先按四档品质权重抽品质，再按该品质内的物品权重抽具体物品。
- 权重自动归一化，无需相加等于 100。品质顺序：普通、少见、稀有、珍稀。
- 有放回抽取，允许重复；每份生成独立格子物品，不做堆叠。
- `coreReward` 表示摆放时优先处理，不会自动增加份数。保底一份应将该组最小和最大份数设为 1。

列表的“每份稀有+”与详情理论概率均为**单次抽取概率**，不是整箱至少出现一次的概率。模拟预览提供整箱出现比例、平均份数、摆放失败次数及首个样本位置，显示样本量和种子；统计包含失败样本，明确标注为抽取结果而非实际入库率。预览使用独立随机数，不改变真实容器。

## 函数结构与运行时接入

新增代码没有静态类、静态状态或单例。只有 Unity 菜单安装入口 `RegionLootInstaller.Install()` 和批处理检查入口 `RegionLootValidation.Run()` 使用静态方法，内部委托给实例。Unity 自带的静态 API 按正常用法调用。

| 组件或服务 | 主要实例方法 | 职责 |
| --- | --- | --- |
| RegionContainerManager | RefreshContainers / RegisterContainer / UnregisterContainer | 汇总所属容器，通知列表变化 |
| SearchableContainer | FindNearestRegion / Initialize / BeginSearch / Tick | 归属、一次性生成、逐件揭示 |
| LootRuleResolver | Resolve / ValidateContainer | 三级优先级与配置校验 |
| LootGenerator | Generate / PickQuality / PickItem | 抽取数量、品质、物品 |
| LootPlacementPlanner | TryPlan / TryFindSpace | 核心优先、大物品优先、允许旋转的格子摆放 |
| LootConfigEditorActions | BatchSetOverride / BatchClearOverrides / SetRegionDefault / CloneAndAssign | 编辑引用、撤销和独立副本 |
| LootPreviewService | CalculatePerRollProbabilities / Simulate | 理论概率与独立抽样 |
| RegionContainerWindow | SetRegion / RefreshRows / FocusContainer | 窗口列表、详情、过滤与场景标记 |

`InventorySession.Start → ConnectRegions → RefreshRegionContainers → SearchableContainer.Initialize → Resolve/ValidateContainer → Generate → TryPlan → GridInventory.SeedGenerated`。

每个容器单独持有运行时库存；SO 只存规则。搜索、关闭、重新打开、禁用再启用都不重抽。初始化失败会记录错误，不会在再次打开时反复重试。要验证修正后的规则，应重新进入 Play Mode。

区域按层级归属，含未激活子物体；嵌套时最近的区域管理器负责。现有区域中运行时实例化的激活容器会自动注册。运行时生成未激活容器时应显式调用 `RegisterContainer`；本版不自动发现行动开始后新增的区域管理器，也不支持多场景动态区域流式接入。未归属区域的旧容器仍可通过 InventorySession 原有数组接入。

## 容量与当前边界

生成结果采用确定性的首次可放置策略，支持旋转，但不是穷举最优装箱。任一物品无法安放时，整箱初始化失败并报错；不静默丢弃核心或附加奖励、不更换小物品、不重新抽取。配置时应使用模拟检查并控制最大份数或增加格子空间。配置校验会检查单个物品尺寸，但通过校验不等于所有组合一定装得下。

本版未实现每局种子持久化、读档重现、权重随进度动态调整、价格经济统计、随机容器生成点或整局撤离结算。容器内容只在当前场景对象生命周期内保留。

## 验证记录

- Unity 6000.3.19f1 隔离工程编译与 Edit/Play 检查通过：431 个断言（其中 400 次为示例容量检查）。
- 覆盖：配置优先级、嵌套与未激活对象归属、批量修改和 Undo/Redo、独立副本、保存后重载、两万次权重抽样、旋转摆放、失败无部分物资、搜索揭示、重复打开不重抽、拾取转移、动态容器注册。
- 验证脚本监听错误与异常日志。初次测试遇到 Unity Search 启动索引时序异常；改为等待编辑器启动回调完成后运行检查，复跑日志无该异常。
- 原工程已通过菜单生成并保存示例场景及资源，管理窗口可打开并正确呈现区域列表。原工程控制台在接入时无错误；窗口内鼠标筛选、定位和编辑的完整交互回归仍待人工操作验收（当前自动化工具坐标点击报 `noWindowsAvailable`）。相关批量编辑服务已通过隔离检查。
- 未进行独立 Player 构建、设备验证或完整搜打撤闭环测试。

批处理检查入口：`-executeMethod LootTheDeep.Inventory.EditorTools.RegionLootValidation.Run`。仅在复制的隔离工程中运行：检查会修改测试场景和临时资源、调整该工程的 Enter Play Mode 设置、进入并退出 Play Mode，最终关闭编辑器；不要在正在工作的原工程运行。检查自行退出，不要附加 `-quit`。
