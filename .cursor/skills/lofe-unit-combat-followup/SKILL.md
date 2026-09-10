---
name: lofe-unit-combat-followup
description: >-
  Continues Legends of Furry / 福瑞杀 unified Unit, Utility AI, world-editor
  enemy placement, wading/wet, slime, and recruitable Boss follow-up work.
  Use when implementing remaining 太糕 cards/passives, party unlock/recruit,
  wet mechanical effects, slime jump targeting, or after Demo 设计.xlsx changes.
---

# 福瑞杀单位战斗后续

统一 Unit 模型、Utility AI、世界编辑器部署、涉水/潮湿入口、史莱姆数据已经落地。后续只补缺口，不要再拆角色/魔物表，也不要按卡牌中文名写 AI `if`。

## 不要再加兼容层

内容 schema 只认 **3**，扩展包 format 只认 **2**。不要再读 `characters.json`、`character` 定义种类、`enemyCharacterIds`、单文件 `demo.json`、SQLite 工作库或旧文件夹内容包。旧数据直接删，不要写迁移。

## 已落地、不要回退

- 内容身份是 `UnitDefinition`：`unitKind` / `defaultFaction` / `controller` / `isBoss` / `recruitable` / `canJoinParty` / `deckId` / `aiProfileId` / `capabilities`。
- 敌人只从关卡 `UnitPlacements` 加载。`BattleRoster.SpawnEncounter` 不再生成敌人。
- AI 用 `UtilityAiController` + 5 个模板（general/aggressive/cautious/kiting/desperate）。Boss 用阶段叠加模板，没有独立 BossAI。
- 导出包里 `aiOverrides` 必须是 `{ key, value }[]`。Unity JsonUtility 不能把缺省 float 和 `0` 分开。
- 涉水是单位能力 `wading`；进水耗尽剩余行动力；潮湿靠状态 `applyOnTerrainIds: ["base.water"]`，不要在 C# 里写死 `wet`。

## 数据源

桌面表格：`C:\Users\Huxiaojiao\Desktop\《福瑞杀》Demo 设计.xlsx`

已知表格矛盾：**魔物** 牌堆构成逐项相加是 18，备注写「共16张」。保持 18，不要改成 16。

## 待补齐

### 1. 太糕 Boss 内容

`taigao` 已是 `isBoss` + `recruitable` + `canJoinParty`，部署在 `boss-1` / chunk `(2,3)`。

卡组表（合计 18）尚未建齐：

| 卡 | 数量 | 现状 |
|---|---|---|
| 爪击 / 疾走 / 格挡 | 3/3/3 | 已有 `hit_01` / `run_01` / `block_01` |
| 魔药 / 遁影 / 吼叫 / 支配 | 1/1/2/3 | 内容包没有这些卡 |
| 展翅 / 恶议 | 1/1 | 魔王角色牌页只有空行 |

被动未写成单位行为图：龙裔（开局把展翅加入牌库）、自诋（受负面时获得锋利）、勿论（回合开始获得恶议）。

当前 `taigao_boss` 仍是可执行占位（爪击/疾走/格挡/包扎）。补卡后改牌库，不要另建 Boss 实体。

### 2. 收编与编队

字段已预留。还没有：击败 Boss → 解锁单位 → 整编队伍出战。做这条链时继续引用 `unitId`，不要复制一份玩家角色数据。

### 3. 潮湿与史莱姆效果精度

- Excel 状态表没有「潮湿」。现在只有进入 `base.water` 获得 `wet`。伤害/移动修正要配状态行为图，不要新开一套状态系统。
- `slime_harden`（硬化）已建卡，但不在牌堆构成里，不要塞进 `slime_standard`。
- `slime_jump` 文案是「立刻移动至目标格」；若运行时仍是免费位移而不是指定格，按表格改效果。
- `slime_devour` 的【吞没】层数=自身当前生命；核对行为图是否按生命取值。
- 魔物页还有空行：灰狼强盗、骷髅兽、堕落勇者枣。有表再补 Unit，仍走同一张单位表。

### 4. 验证

内容工具：`D:\LegendsOfFurry\Tools\LOFE_ContentKit` 里 `pnpm test`，再 `pnpm export:core` 写出 `Project/Content/Packs/lofe_core.lofepackage`。`planner_pack.lofepackage` 也必须是 schema 3 / format 2；旧 schema 直接拒载。

Unity 6.0.5f1：`D:\Unity\Hub\Editor\6000.5.7f1\Editor\Unity.exe`，EditMode 至少跑 `CardBehaviorBaselineTests`、`WorldMapFoundationTests`、`ContentArchitectureFoundationTests`。大地图进战、涉水、Boss 阶段要 PlayMode 手测。

## 部署对照

- 史莱姆：全部 `battle` 关卡，实例如 `fight-*-slime-1`
- 太糕：`boss-1`，实例 `boss-1-taigao-1`
- 调试战斗场景只出己方，不出敌人
