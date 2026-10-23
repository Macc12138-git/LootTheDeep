# LootTheDeep

## 游戏设计与协作入口

本项目为 **2D 像素风、俯视角、纯单机的世界搜打撤游戏**：地图使用 Unity Tilemap 制作，将成功撤出的物资用于俯视角基地建设。战斗方向借鉴《元气骑士》的直观反馈、可读攻击、武器差异和遭遇节奏，同时保留开放路线、可选交战与自主撤离。当前处于概念设计与局部原型验证阶段；旧主题原型与验证记录保留，文档更新不代表新方向或完整玩法闭环已经实现。

- [设计理念与接续指南](Docs/Design/DesignPrinciples.md)：先读此文件，了解明确方向、设计理由及待确认问题。
- [完整游戏概念方案 V0.3](Docs/Design/GameConcept-v0.3.md)：当前基线，涵盖俯视角世界探索、Tilemap 地图、搜打撤、动作战斗方向、四种随机机制及基地建设。
- [历史游戏概念方案 V0.2](Docs/Design/GameConcept-v0.2.md)：保留水下主题的历史讨论，以 V0.3 为当前方向。
- [物资与掉落概念方案 V0.1](Docs/Design/LootAndDropConcept-v0.1.md)：物资分类、区域与容器、掉落逻辑及成长节奏；数值为待验证的测试草案。
- [区域容器管理与爆率配置开发方案 V0.1](Docs/Design/RegionContainerLootDevPlan-v0.1.md)：区域容器列表、场景定位、筛选与批量配置、单独配置及分阶段验收；首版已接入。
- [区域容器爆率配置使用说明](Docs/Design/RegionContainerLootUsage.md)：窗口入口、SO 配置、运行时调用链、验证结果与当前限制。
- [Codex 项目协作说明](AGENTS.md)：新任务与新机器接续项目的读取入口和修改边界。

上述文档随 Git 同步，不依赖原始聊天记录或个人 Codex 记忆。后续设计以“俯视角像素世界探索＋可读且可绕过的动作遭遇＋自主搜打撤＋俯视角基地建设”为基线，保留有效撤离入库规则。基地与探索暂沿用独立场景结构，世界是否无缝连通及区域连接方式仍待确认。具体操作、武器成长、遭遇类型和数值仍需单独确认。

## Git 与 Git LFS

- 提交 `Assets/`（包含 `.meta`）、`Packages/`、`ProjectSettings/` 和仓库配置文件。
- `.gitignore` 排除 Unity 缓存、临时文件、日志、构建产物及本机 IDE 配置。
- `.gitattributes` 将图片、音频、视频、模型、字体、二进制插件和压缩包交给 Git LFS，按扩展名匹配，不按大小判断。
- 场景、预制体、`.meta` 等文本文件保留普通 Git 管理；`.asset` 不统一加入 LFS，以免把文本配置也转为 LFS 指针。后续遇到二进制 `.asset` 时按具体路径添加规则。

### 新机器初始化

先安装 Git LFS，再执行：

```sh
git clone https://github.com/Macc12138-git/LootTheDeep.git
cd LootTheDeep
git lfs install --local
git lfs pull
```

如果已有本地仓库，只需在仓库目录执行后两条命令。LFS 安装和 hook 是本机设置，每个新克隆均需初始化。

### 检查与新增规则

```sh
git lfs status
git lfs ls-files
git check-attr filter -- Assets/example.png
git lfs track "Assets/path/to/large-binary.asset"
```

新增规则后需一并提交 `.gitattributes` 和对应资源。远端拉取、推送需要有效的 GitHub 凭据；本地 LFS 初始化成功不代表远端认证或 LFS 上传已验证。
