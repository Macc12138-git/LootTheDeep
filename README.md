# LootTheDeep

## 游戏设计与协作入口

本项目为 **2D 像素风纯单机游戏**：在独立水下场景中进行随机化搜打撤，将成功撤出的物资用于水面基地建设与生存。当前处于概念设计阶段，文档不代表功能已经实现。

- [设计理念与接续指南](Docs/Design/DesignPrinciples.md)：先读此文件，了解明确方向、设计理由及待确认问题。
- [完整游戏概念方案 V0.2](Docs/Design/GameConcept-v0.2.md)：局外基地、局内搜打撤、四种随机机制、撤离和成长的完整方案。
- [Codex 项目协作说明](AGENTS.md)：新任务与新机器接续项目的读取入口和修改边界。

上述文档随 Git 同步，不依赖原始聊天记录或个人 Codex 记忆。后续设计以“独立局内搜打撤＋局外水面建设”为基线，不退回连续开放海域采集玩法。

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
