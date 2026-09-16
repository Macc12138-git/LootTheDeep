# Unity 搜索与背包原型

网页样板已迁入 `Assets/Scenes/ExploreScene.unity`。Unity 版本：6000.3.19f1。

## 使用

打开探索场景进入 Play Mode。出生点右侧有一个维修箱，另外两个箱子在附近更深处。

- WASD / 方向键：移动。
- E：在 1.8 单位范围内打开高亮容器，并开始逐件搜索。
- Tab：打开独立背包 / 关闭当前页面。
- Esc：先取消拖拽，再关闭页面。
- 拖拽：在收纳区之间放置已发现物品；绿色有效、红色无效。
- R：旋转拖拽中或选中的物品；右键物品也可原地旋转。空间不足时保持原状。
- Ctrl / Command + 点击，或选中后点击“快捷转移”：容器物品默认移入背包；随身物品移入当前容器。
- 滚轮 / 触控板 / 右侧滚动条：纵向滚动随身收纳区。

打开页面时玩家停止移动，世界不暂停。关闭页面、目标失效或离开范围时恢复移动。

## 预制体与配置

| 资源 | 用途 |
| --- | --- |
| `Assets/Prefabs/Gameplay/Player.prefab` | 当前玩家本体、移动与输入组件、临时黄色外形及朝向标记 |
| `Assets/Prefabs/Inventory/SearchInventoryUI.prefab` | 完整 Canvas、随身收纳、容器面板、详情、提示和拖拽预览 |
| `Assets/Prefabs/Inventory/InventoryCell.prefab` | 格子边线和底色 |
| `Assets/Prefabs/Inventory/InventoryItem.prefab` | 物品背景、图标、名称、品质条、原位搜索图标和进度 |
| `Assets/Prefabs/Inventory/SearchableContainer.prefab` | 场景可搜索容器、显示名称、搜索耗时、初始物资配置 |
| `Assets/Prefabs/Inventory/InventoryEventSystem.prefab` | Unity EventSystem 和 Input System UI 输入模块 |
| `Assets/Data/Inventory/*.asset` | 六种 ScriptableObject 物品定义，配置名称、描述、尺寸、品质和图标 |

页面布局直接保存在预制体中，组件引用已序列化。运行时只实例化格子和物品卡片预制体；不使用 `new GameObject` / `AddComponent` 构建或绑定 UI。游戏交互使用实例方法。安装工具仅有一个 Unity `MenuItem` / `executeMethod` 所要求的静态入口。

UI 层级：`SearchInventoryUI / Window / InventoryPanel / StorageScroll / Viewport / Content`。在 Prefab Mode 编辑固定页面、装备图标位和各独立格子区域。滚动使用 Unity `ScrollRect` + `RectMask2D` + `Scrollbar`。CanvasScaler 基准为 1440 × 900。

布局为胸挂 → 口袋 → 背包 → 保险箱。胸挂为 2×2 主袋和两个独立 1×2 侧袋；口袋为四个独立单格；背包为 5 列×10 行；保险箱为 3×3。总计 71 格。每区左侧装备图标位不计入容量。

所有 `InventoryGridView` 使用 56 单位格子尺寸。连续格无间距，独立袋位间隔 9 单位。背包视口紧贴五列格子的右边缘，仅纵向滚动。修改格子尺寸时应同步修改各网格、格子模板和页面占位尺寸。

`InventorySession.Initial Items` 的 area 对应 `InventoryView.Player Grids`：0 背包，1 胸挂主袋，2/3 胸挂侧袋，4–7 口袋，8 保险箱。容器的初始物资使用自己的局部坐标（area 字段不使用）。创建新物品：Project 右键 → Create → LootTheDeep → Loot Definition。

探索场景已保存预制体实例。安装菜单 `LootTheDeep / Install Search Inventory Prefabs` 用于首次接入；已存在的会话会跳过，不重建或覆盖已有配置。手动新增容器后，在场景中的 `InventorySession.Containers` 数组添加引用。

## 当前边界

- 固定示例物资用于验证交互，未实现随机地图、掉落生成和存档。
- 未发现物品按占格大小显示灰色阴影，按从上到下、从左到右的顺序，每件 1.5 秒揭示。
- 关闭/暂停保留已发现物品；当前未完成物品的进度重新开始。再次打开不会重新生成物资。
- 无效放置不会修改原物品归属；未知物品不能转移；物品不能跨独立袋位。
- 当前物资只属于本次场景会话；退出 Play Mode 后重置。未接入撤离结算和基地仓库。
- 保险箱目前仅有收纳布局，不代表已经确定死亡保护规则。装备图标位与玩家装备概览是展示，未实现换装和装备属性。
- PNG 来自项目网页样板中的本地 SVG。中文字体使用 Noto Sans CJK SC，授权文件位于 `Assets/Fonts/OFL.txt`。

## 验证记录（2026-09-16）

已用 Unity 6000.3.19f1 编译，并在项目隔离副本中执行 Play Mode 自动检查：

- 场景引用、玩家/UI/三个容器的预制体连接与缺失脚本检查。
- 71 格容量、背包 5×10、保险箱 3×3、左右 56 单位格子尺寸。
- Input System 注入 E、Tab、Esc 和移动按键，检查开关页面、停止及恢复移动。
- 逐件揭示、暂停重置未完成进度、重开保留已发现状态、未知物品禁止转移。
- uGUI 射线与拖拽事件、旋转后跨区放置、原地旋转、无效放置保持原位置和物品总数。
- 纵向滚动到底、保险箱可命中、被视口裁剪的格子不能接收放置。
- 离开交互范围、关闭会话组件、销毁当前容器后的关闭和移动恢复。

另外检查了 Unity 实际渲染的页面截图。自动验证覆盖以上原型行为；未做设备构建、长时间游玩或完整搜打撤流程验证。
