# 生图配置

使用本机已安装的 imagegen 技能 CLI，不另写 API 调用程序。当前配置用于生成首轮地图素材草案，已于 2026-10-20 完成第一张地面图的真实接口调用。

## 当前预设

- 最新画风试稿使用 `references/pixel-style-outdoor.jpg`、`pixel-style-interior.jpg` 与 `pixel-style-combat.jpg`，来自用户于 2026-10-21 提供的新截图，仅用于像素造型、描边、明暗与地面细节参考。旧 `references/style-reference.jpg` 保留供此前任务复现。
- 模型：`gpt-image-2`，沿用已安装 imagegen 技能的默认值；当前兼容服务的模型列表与真实图片编辑接口均已验证。
- 尺寸：1024×1024；质量：low；格式：PNG；每次一张。
- 三类提示词：地面道路、岩壁墙体、容器。首轮先检查画风与比例，再细化拼接和开放状态。
- 输出：仓库根目录下的 `output/imagegen/`，不会自动导入 Assets 或改变现有场景。
- 独立 Python 环境：`.venv/`，已排除 Git。
- 凭据：`.env`，已排除 Git；`.env.example` 只保留无密钥示例。

## 统一表面约束

按本轮用户反馈，地图和素材均采用平整哑光表面、大色块和少量低对比磨损。道路使用平整灰色压实路面，通过底色与轮廓区分道路、泥土和水泥地。

禁止密集小圆颗粒、逐颗带高光和阴影的凸点、碎石铺面、颗粒状路边、孔洞、抖色和噪点纹理。岩壁与墙体保留大块结构，容器保留可辨认的把手与锁扣。此约束优先于参考图中的微小纹理，已加入地面、墙体、容器和完整地图四份主提示词；不通过模糊处理掩盖颗粒。

配置文件中的 `IMAGEGEN_*` 由下方命令显式传给 CLI，CLI 不会自行加载 `.env`。本机 `.env` 已配置与所填密钥对应的兼容服务地址；缺少此地址时会请求 OpenAI 默认接口，原先的 HTTP 401 即由服务与密钥不匹配引起。

## 填写凭据

在本机编辑 `Tools/ImageGen/.env`，为 `OPENAI_API_KEY` 填入自己的图片 API 密钥。不要把密钥发到聊天中。

OpenAI 密钥入口：<https://platform.openai.com/api-keys>。如使用其他兼容服务，先确认其图片编辑接口、鉴权方式及可用模型，再填写 `OPENAI_BASE_URL` 与对应模型；现有聊天服务的凭据没有被自动复用。

## 在其他机器重建依赖

当前机器已安装依赖。其他机器可使用 Python 3.12 或兼容版本重建独立环境：

```zsh
python3 -m venv Tools/ImageGen/.venv
Tools/ImageGen/.venv/bin/python -m pip install -r Tools/ImageGen/requirements.txt
```

同时需要安装 imagegen 技能，并根据 `.env.example` 创建本机 `.env`。

## 运行

从仓库根目录执行以下初始化命令。`.env` 是本地 shell 配置，只加载自己维护的文件。

```zsh
cd /Users/user/Desktop/LootTheDeep
source Tools/ImageGen/.env
IMAGEGEN_PYTHON="$PWD/Tools/ImageGen/.venv/bin/python"
IMAGEGEN_CLI="${CODEX_HOME:-/Users/user/.codex}/skills/.system/imagegen/scripts/image_gen.py"
```

先检查地面任务，不发送 API 请求：

```zsh
"$IMAGEGEN_PYTHON" "$IMAGEGEN_CLI" edit \
  --image Tools/ImageGen/references/style-reference.jpg \
  --prompt-file Tools/ImageGen/prompts/ground.txt \
  --model "$IMAGEGEN_MODEL" --size "$IMAGEGEN_SIZE" \
  --quality "$IMAGEGEN_QUALITY" --output-format "$IMAGEGEN_OUTPUT_FORMAT" \
  --background opaque --no-augment \
  --out output/imagegen/ground-v1.png --dry-run
```

密钥就绪后，移除上面最后的 `--dry-run` 即生成第一张地面草案。真实请求会使用所配置服务的 API 额度。风格参考图片通过 Image API 的编辑接口随请求上传，用于派生新素材。

岩壁墙体与容器分别使用下面两组参数替换地面任务的提示词及输出，其他参数相同：

| 提示词 | 输出 |
| --- | --- |
| `Tools/ImageGen/prompts/structures.txt` | `output/imagegen/structures-v1.png` |
| `Tools/ImageGen/prompts/containers.txt` | `output/imagegen/containers-v1.png` |

首轮只请求一张检查结果。已有输出不要覆盖，后续使用 `-v2` 等版本名。

### 已生成的第一张地面草案

- `output/imagegen/ground-v1.png`：接口原始输出，RGB PNG，实际尺寸 1254×1254。虽然请求了 1024×1024，当前服务未按请求尺寸返回，后续仍需检查每次输出。
- `output/imagegen/ground-v1-1024.png`：使用最近邻缩放得到的 1024×1024 预览，原图保留。
- 本次沿用 `prompts/ground.txt`，通过 `edit` 命令上传参考图，生成调用约 33 秒完成。
- 初步视觉检查：包含泥土、碎石路面、水泥地坪及部分过渡；存在可见地块接缝，边缘匹配和完整转角尚未验收。缩放只统一预览尺寸，不代表图集已经能够无缝拼接。
- 本次没有生成墙体或容器，没有导入 Unity，也没有修改场景。

### 完整地图草案

- 平整表面版：[`repair-site-map-v3.png`](../../output/imagegen/repair-site-map-v3.png)，实际尺寸 1448×1086，RGB PNG。请求尺寸为 1536×1152，服务仍未按请求尺寸返回；保留原始输出，没有缩放。
- 旧稿：`output/imagegen/repair-site-map-v1.png` 与 `repair-site-map-v2.png`，保留用于对照。
- 主提示词：[`prompts/repair-site-map.txt`](prompts/repair-site-map.txt)。原始版本通过 `edit` 命令同时上传画风参考图与 `ground-v1-1024.png`，生成 v1；本轮提示词已按用户反馈加入平整表面限制，旧地面图现在仅作为配色参考。
- 植物清理提示词：[`prompts/repair-site-map-cleanup.txt`](prompts/repair-site-map-cleanup.txt)。以 v1 为输入，移除附带的植物，得到 v2。
- 表面简化提示词：[`prompts/repair-site-map-flat-surfaces.txt`](prompts/repair-site-map-flat-surfaces.txt)。以未带标注的 v2 为唯一输入，去掉地面与路边密集颗粒，保留大块墙岩、道路与九个容器，得到 v3。
- 三次均使用现有 CLI 配置的 `gpt-image-2`、low 质量、不透明 PNG，每次一张，没有覆盖旧图。
- 画面包含两处开放搜索院落、中央主路、下方外围绕行路、岩壁和低墙，以及九个可辨认容器；右上黄色箱盖用于突出特殊容器。已清除植物和地面密集小圆颗粒，没有人物、UI 或文字。
- 这是地图布局与美术参考，尚未构建 Unity Tilemap、碰撞或容器交互。

### 新参考画风地图试稿（2026-10-21）

- 输出：[`repair-site-map-pixel-v1.png`](../../output/imagegen/repair-site-map-pixel-v1.png)，实际尺寸 1448×1086，RGB PNG；请求 1536×1152，保留服务原图，未缩放或后加像素滤镜。
- 提示词：[`prompts/repair-site-map-pixel-v1.txt`](prompts/repair-site-map-pixel-v1.txt)。使用上述三张新截图作为画风参考，经现有 CLI 的 `edit` 接口生成新地图；没有上传旧地图或旧地面图集，避免继承旧版的手绘表面表现。
- 本次单独指定 `gpt-image-2`、medium 质量、不透明 PNG，每次一张；本机 `.env` 的默认尺寸与质量没有修改。
- 目标表现：较粗且清晰的像素块、阶梯轮廓、物件深色描边、有限明暗色块、稀疏扁平地面磨损。保持两处搜索院落、中央主路和下方绕行路，九个容器以颜色和形状区分。
- 已检查整图可读性与文件完整性：两处院落各有两个开口，外围通路连续，未生成植物、角色、文字、UI 或密集圆形地面颗粒。画风是否采用仍待用户评估；这是地图图片，未导入 Unity 或修改场景。

### 四区拼接验证地图（2026-10-21）

- 用户反馈上一张地图面积不足，本版按游戏物件比例生成四张分区，组合为 2×2 地图，面积为上一张的四倍。
- [完整地图](../../output/imagegen/validation-map-v1/complete.png)：2896×2172；[预览](../../output/imagegen/validation-map-v1/overview.png)：1448×1086；[整套 ZIP](../../output/imagegen/validation-map-v1/validation-map-v1.zip)。
- [输出说明](../../output/imagegen/validation-map-v1/README.md)包含分区排列、实际尺寸、比例建议、24 个容器及路线说明。四张最终分区均为 1448×1086；服务返回的 SW 为 1447×1087，拼接时做了最近邻尺寸统一，其余原图尺寸不变。
- 四份最终提示词：[NW](prompts/validation-map-v1/nw.txt)、[NE](prompts/validation-map-v1/ne.txt)、[SW](prompts/validation-map-v1/sw.txt)、[SE](prompts/validation-map-v1/se.txt)。沿用最新像素画风、平整表面和五类地图元素限制。
- 使用现有 imagegen CLI 的 `edit` 接口、`gpt-image-2`、`medium`、请求 1536×1152。每区先固定布局引导图，NW 参考新户外截图，其余三区参考 NW 保持画风和比例。
- 本地辅助脚本 [`validation_map_layout.py`](validation_map_layout.py) 只负责引导图、地面基础色统一及拼接，不调用图片 API。`guides` 生成布局图，`assemble` 从原图重建最终分区、大图、元数据及 ZIP。
- 建议按约 96×72 格理解整体尺度，尚未切分为严格 Tilemap，未导入 Unity 或修改场景。

### 原始参考画风修订 V2（2026-10-21）

- 本版修正四区地图偏离用户原始截图的问题：采用更粗的统一像素块、暗色阶梯轮廓、具有高度的灰紫石墙，以及绿色地面与不规则棕色土路。V1 保留用于对照。
- [完整地图](../../output/imagegen/validation-map-v2/complete.png)、[总览](../../output/imagegen/validation-map-v2/overview.png)、[原尺寸局部](../../output/imagegen/validation-map-v2/style-detail.png)、[ZIP](../../output/imagegen/validation-map-v2/validation-map-v2.zip)、[详细说明](../../output/imagegen/validation-map-v2/README.md)。完整地图仍为 2896×2172，四张分区及服务原图均为 1448×1086。
- 所有分区直接上传用户原始的 outdoor、combat、interior 三张参考；不再把已生成分区当作其他分区的唯一画风依据。布局引导图为第四张输入，按优先级只控制区域位置和道路接口。
- 最终提示词：[NW](prompts/validation-map-v2/nw.txt)、[NE](prompts/validation-map-v2/ne.txt)、[SW](prompts/validation-map-v2/sw.txt)、[SE](prompts/validation-map-v2/se.txt)。仍使用已配置 CLI 的 `edit`、`gpt-image-2`、`medium`、请求 1536×1152、不透明 PNG。
- [`reference_map_v2.py`](reference_map_v2.py) 负责粗布局引导和本地拼接，并统一少量地面色阶以减小分区底色差异。四区共 24 个容器，只保留地面、道路、墙、岩石与容器；没有修改 Unity 场景或玩法代码。
- 画风是否采用仍待用户评估；地图图片不等于已切分和验证的 Unity Tilemap。

## 透明背景与后续处理

根据本机 imagegen 技能说明，CLI `gpt-image-2` 不支持原生透明背景参数。地面使用不透明输出；墙体和容器先生成纯品红背景，再检查能否使用技能附带的 `remove_chroma_key.py` 提取透明背景。本次没有切换模型，也没有生成任何假透明棋盘格图片。

如果需要其他模型的原生透明输出，届时单独确认模型选择。AI 生成的网格、接缝与像素比例仍需验收，不直接当作已经可以无缝拼接的正式图集。

## 本轮验证边界

Python SDK 导入、参考图、三份提示词和 CLI 参数的 dry-run 均已通过。密钥与对应兼容接口已配置，模型列表鉴权返回 HTTP 200，`gpt-image-2` 的真实图片编辑请求已成功，输出 PNG 已通过格式和完整性检查。墙体、容器、透明背景处理、Unity 导入与长期服务稳定性仍未验证。

官方说明页面在配置阶段访问返回 HTTP 403；初始参数依据是当前安装技能。本次实际可用性结论依据兼容服务的真实返回结果。
