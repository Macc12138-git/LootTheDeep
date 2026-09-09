# LootTheDeep 项目协作说明

## 开始工作前

- 涉及游戏概念、玩法、美术方向或功能实现时，先阅读 `Docs/Design/DesignPrinciples.md`，再按需阅读完整方案 `Docs/Design/GameConcept-v0.2.md`。
- 设计理念以仓库文档为共享依据，不依赖某台机器上的 Codex 记忆或历史对话。
- 本项目处于概念设计阶段。文档描述的是目标玩法，不代表对应功能已经实现；实际实现状态必须检查项目后再报告。
- 默认使用中文沟通和维护设计文档。

## 必须保留的方向

- 2D 像素风，纯单机；敌对人类由 AI 驱动，不包含联机、真人匹配或多人服务器玩法。
- 水面基地建设与水下搜打撤是两个独立场景，不是连续往返的开放海域采集玩法。
- 每次下水是一局独立行动，包含随机出生地、随机物资、随机高价值容器和随机撤离点。
- 核心循环是局外准备、局内搜索与战斗或避战、有效撤离、物资入库、基地建设，再次出航。
- 搜到物资不等于获得永久资产；成功撤离才将战利品带回基地。潜水器不能默认成为随时可用的安全出口。

## 设计与修改边界

- 区分用户明确方向、当前方案建议、待确认问题；不要把建议写成用户已经确认的要求。
- 横向侧视、地图生成方式、装备损失细则、撤离点类型及潜水器操作方式尚未全部定案，详见设计原则。
- 用户要求分析或方案时保持只读；仅在用户要求保存文档或实施时修改对应内容，不自动扩展到代码实现或图片生成。
- 用户改变核心方向时同步更新设计原则、完整方案及受影响的本文件内容，说明变更理由，避免多个文档互相冲突。
- 保留与当前任务无关的工作区改动；未经明确要求，不自动暂存、提交或推送 Git。

<!-- CODEGRAPH_START -->
## CodeGraph

In repositories indexed by CodeGraph (a `.codegraph/` directory exists at the repo root), reach for it BEFORE grep/find or reading files when you need to understand or locate code:

- **MCP tool** (when available): `codegraph_explore` answers most code questions in one call — the relevant symbols' verbatim source plus the call paths between them, including dynamic-dispatch hops grep can't follow. Name a file or symbol in the query to read its current line-numbered source. If it's listed but deferred, load it by name via tool search.
- **Shell** (always works): `codegraph explore "<symbol names or question>"` prints the same output.

If there is no `.codegraph/` directory, skip CodeGraph entirely — indexing is the user's decision.
<!-- CODEGRAPH_END -->
