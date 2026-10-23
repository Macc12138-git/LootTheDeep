# 基础墙体 Tilemap V1

用于当前地图轮廓搭建。素材由已配置的 API / gpt-image-2 生成三种母版，再裁切、旋转、统一连接截面并去除底色。

- `walls-atlas-64.png`：透明底图集，320×128，5 列×2 行，每格 64×64，无间距、无外边距。
- `tiles/`：10 个独立的 64×64 透明 PNG。
- 墙宽 24 像素，所有连接位于边缘中央第 20–43 像素。
- `atlas-preview.png`：带棋盘底和分格线的查看图，勿作为图集导入。
- `assembly-preview.png`：使用本套图块和此前地面拼接的示例，供检查连接，不是游戏关卡方案。

图集从左到右、从上到下排列：

| 行 | 第 1 格 | 第 2 格 | 第 3 格 | 第 4 格 | 第 5 格 |
| --- | --- | --- | --- | --- | --- |
| 上行 | 横向直墙 | 纵向直墙 | 上右转角 | 右下转角 | 下左转角 |
| 下行 | 左上转角 | 向上连接的收口 | 向右连接的收口 | 向下连接的收口 | 向左连接的收口 |

“向上连接的收口”表示墙段从本格上边进入，在格内结束，其余朝向同理。

## Unity 导入

1. 使用 `walls-atlas-64.png`，Texture Type 设为 `Sprite (2D and UI)`，Sprite Mode 为 `Multiple`。
2. Pixels Per Unit 为 `64`，Filter Mode 为 `Point`，Compression 为 `None`。
3. 在 Sprite Editor 中使用 `Grid By Cell Size`，设为 `64×64`；Offset 和 Padding 均为 `0`，执行 Slice、Apply。
4. 将切分后的 Sprite 拖入 Tile Palette，在单独的墙体 Tilemap 上绘制。

已检查图块的透明通道、连接宽度和对应边缘像素一致性，并生成拼接预览。未修改 Unity 工程，也未验证运行时碰撞；图片本身不附带 Collider 或 Rule Tile 配置。

生成提示词：`Tools/ImageGen/prompts/blockout-walls-v1.txt`。
