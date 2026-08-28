import * as Blockly from 'blockly'
import type { EditorOptions } from './types'
import { BLOCK } from './blockly-codec'
import { conditionLabels, effectLabels, nodeLabels, targetLabels, triggerLabels, zh } from './labels'

const DAMAGE_TYPES: [string, string][] = [
  ['普通', 'normal'], ['火', 'fire'], ['冰', 'ice'], ['雷', 'lightning'],
  ['光', 'light'], ['暗', 'dark'], ['毒', 'poison'], ['真实', 'true'],
]

let catalogs: EditorOptions = {
  rarities: [], selectionModes: [], teamFilters: [], lifeStates: [],
  triggers: ['on_play'], effects: ['no_op'], effectTargets: ['selected_unit'],
  conditions: ['target_has_armor'], foreachTargets: ['units_in_front_area'],
  equipmentSlots: [], statusCategories: [], stackingPolicies: [], durationPolicies: [],
}

/** Blockly 下拉框读取的当前能力目录。 */
export function setBlockCatalogs(options?: EditorOptions): void {
  if (options) catalogs = options
}

function pairs(keys: string[] | undefined, labels: Record<string, string>, fallback: string): [string, string][] {
  const list = new Set<string>([fallback, ...Object.keys(labels), ...(keys ?? [])])
  return [...list].map((key) => [zh(labels, key), key])
}

function floatNumber(value: number, min?: number): Blockly.FieldNumber {
  return new Blockly.FieldNumber(value, min, undefined, 0.01)
}

interface LofBlock extends Blockly.Block {
  hat?: string
  lofNodeId?: string
  lofBehaviorId?: string
  lofEnabled?: boolean
  lofPriority?: number
  lofParams?: Record<string, unknown>
}

const extra = {
  saveExtraState(this: LofBlock) {
    return {
      nodeId: this.lofNodeId || this.id,
      behaviorId: this.lofBehaviorId,
      enabled: this.lofEnabled,
      priority: this.lofPriority,
      params: this.lofParams ?? {},
    }
  },
  loadExtraState(this: LofBlock, state: Record<string, unknown> | undefined) {
    const extraState = state ?? {}
    this.lofNodeId = String(extraState.nodeId ?? '')
    this.lofBehaviorId = extraState.behaviorId ? String(extraState.behaviorId) : undefined
    this.lofEnabled = extraState.enabled === undefined ? true : Boolean(extraState.enabled)
    this.lofPriority = Number(extraState.priority ?? 0)
    this.lofParams = extraState.params && typeof extraState.params === 'object'
      ? extraState.params as Record<string, unknown>
      : {}
  },
}

function define(type: string, init: (this: LofBlock) => void): void {
  Blockly.Blocks[type] = { ...extra, init }
}

/** 注册卡牌行为积木；每次覆盖以便热更新字段定义。 */
export function registerLofBlocks(): void {
  define(BLOCK.hat, function () {
    this.appendDummyInput()
      .appendField('当')
      .appendField(new Blockly.FieldDropdown(() => pairs(catalogs.triggers, triggerLabels, 'on_play')), 'TRIGGER')
    this.setNextStatement(true)
    this.setColour(45)
    this.hat = 'cap'
    this.setTooltip('时机帽子，下面拼接效果积木')
  })

  define(BLOCK.effect, function () {
    this.appendDummyInput()
      .appendField(new Blockly.FieldDropdown(() => pairs(catalogs.effects, effectLabels, 'no_op')), 'OP')
      .appendField('对')
      .appendField(new Blockly.FieldDropdown(() => pairs(catalogs.effectTargets, targetLabels, 'selected_unit')), 'TARGET')
      .appendField(floatNumber(1, 0), 'AMOUNT')
    this.appendDummyInput()
      .appendField('伤害类型')
      .appendField(new Blockly.FieldDropdown(DAMAGE_TYPES), 'DAMAGE_TYPE')
    this.setPreviousStatement(true)
    this.setNextStatement(true)
    this.setColour(210)
    this.setTooltip('效果积木')
  })

  define(BLOCK.condition, function () {
    this.appendDummyInput()
      .appendField('如果')
      .appendField(new Blockly.FieldDropdown(() => pairs(catalogs.conditions, conditionLabels, 'target_has_armor')), 'OP')
      .appendField('概率')
      .appendField(floatNumber(0.1, 0), 'CHANCE')
    this.appendStatementInput('THEN').appendField('则')
    this.appendStatementInput('ELSE').appendField('否则')
    this.setPreviousStatement(true)
    this.setNextStatement(true)
    this.setColour(30)
    this.setTooltip('条件积木；随机概率用 0～1，例如 0.1 表示一成')
  })

  define(BLOCK.repeat, function () {
    this.appendDummyInput()
      .appendField('重复')
      .appendField(new Blockly.FieldNumber(2, 1, undefined, 1), 'COUNT')
      .appendField('次')
    this.appendStatementInput('DO')
    this.setPreviousStatement(true)
    this.setNextStatement(true)
    this.setColour(0)
    this.setTooltip('重复积木')
  })

  define(BLOCK.foreach, function () {
    this.appendDummyInput()
      .appendField('挨个')
      .appendField(new Blockly.FieldDropdown(() => pairs(catalogs.foreachTargets, targetLabels, 'units_in_front_area')), 'OP')
      .appendField('范围')
      .appendField(floatNumber(1, 0), 'RANGE')
    this.appendStatementInput('DO')
    this.setPreviousStatement(true)
    this.setNextStatement(true)
    this.setColour(260)
    this.setTooltip('挨个积木')
  })

  define(BLOCK.sequence, function () {
    this.appendDummyInput().appendField(zh(nodeLabels, 'sequence'))
    this.appendStatementInput('DO')
    this.setPreviousStatement(true)
    this.setNextStatement(true)
    this.setColour(160)
    this.setTooltip('顺序积木')
  })
}

function flyoutBlocks(type: string, field: string, keys: string[] | undefined, labels: Record<string, string>): Blockly.utils.toolbox.FlyoutItemInfo[] {
  const list = [...new Set([...(keys ?? []), ...Object.keys(labels)])]
  return list.map((key) => ({ kind: 'block', type, fields: { [field]: key } }))
}

/** 按当前目录生成 Blockly 工具箱。 */
export function buildToolbox(): Blockly.utils.toolbox.ToolboxInfo {
  return {
    kind: 'categoryToolbox',
    contents: [
      { kind: 'category', name: '时机', colour: '#ffbf00', contents: flyoutBlocks(BLOCK.hat, 'TRIGGER', catalogs.triggers, triggerLabels) },
      {
        kind: 'category',
        name: '结构',
        colour: '#0fbd8c',
        contents: [
          { kind: 'block', type: BLOCK.condition },
          { kind: 'block', type: BLOCK.repeat },
          { kind: 'block', type: BLOCK.foreach },
          { kind: 'block', type: BLOCK.sequence },
        ],
      },
      { kind: 'category', name: '效果', colour: '#4c97ff', contents: flyoutBlocks(BLOCK.effect, 'OP', catalogs.effects, effectLabels) },
      { kind: 'category', name: '判断', colour: '#ffab19', contents: flyoutBlocks(BLOCK.condition, 'OP', catalogs.conditions, conditionLabels) },
    ],
  }
}
