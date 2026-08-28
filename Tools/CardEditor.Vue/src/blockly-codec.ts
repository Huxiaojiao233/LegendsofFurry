import type { NestedBehavior, NestedNode } from './behavior-tree'
import { constantExpr, newNodeId, readConstant } from './behavior-tree'

export const BLOCK = {
  hat: 'lof_hat',
  effect: 'lof_effect',
  condition: 'lof_condition',
  repeat: 'lof_repeat',
  foreach: 'lof_foreach',
  sequence: 'lof_sequence',
} as const

const SKIP_TARGET = new Set(['end_turn', 'no_op', 'cancel_query', 'modify_query_value', 'consume_owner_status', 'transform_owner_status', 'play_vfx', 'play_sfx'])
const SKIP_AMOUNT = new Set(['end_turn', 'no_op', 'play_vfx', 'play_sfx', 'cancel_query'])

export interface LofExtraState {
  nodeId?: string
  behaviorId?: string
  enabled?: boolean
  priority?: number
  params?: Record<string, unknown>
}

export interface BlocklyBlockState {
  type: string
  id?: string
  x?: number
  y?: number
  fields?: Record<string, string | number | boolean>
  extraState?: LofExtraState
  next?: { block?: BlocklyBlockState }
  inputs?: Record<string, { block?: BlocklyBlockState }>
}

export interface BlocklyWorkspaceState {
  blocks?: {
    languageVersion?: number
    blocks?: BlocklyBlockState[]
  }
}

function fieldString(block: BlocklyBlockState, name: string, fallback = ''): string {
  const value = block.fields?.[name]
  return value === undefined || value === null ? fallback : String(value)
}

function fieldNumber(block: BlocklyBlockState, name: string, fallback: number): number {
  const value = Number(block.fields?.[name])
  return Number.isFinite(value) ? value : fallback
}

function restParams(params: Record<string, unknown>, omit: string[]): Record<string, unknown> {
  const next = { ...params }
  for (const key of omit) delete next[key]
  return next
}

function link(nodes: BlocklyBlockState[]): BlocklyBlockState | undefined {
  if (!nodes.length) return undefined
  for (let index = 0; index < nodes.length - 1; index += 1) {
    nodes[index].next = { block: nodes[index + 1] }
  }
  return nodes[0]
}

function walk(start?: BlocklyBlockState): NestedNode[] {
  const nodes: NestedNode[] = []
  let current = start
  while (current) {
    nodes.push(blockToNode(current))
    current = current.next?.block
  }
  return nodes
}

function inputStack(block: BlocklyBlockState, name: string): NestedNode[] | undefined {
  const child = block.inputs?.[name]?.block
  const nodes = walk(child)
  return nodes.length ? nodes : undefined
}

function blockToNode(block: BlocklyBlockState): NestedNode {
  const extra = block.extraState ?? {}
  const id = extra.nodeId || block.id || `n.${Math.random().toString(36).slice(2)}`
  const params = { ...(extra.params ?? {}) }
  if (block.type === BLOCK.effect) {
    const op = fieldString(block, 'OP', 'no_op')
    if (!SKIP_TARGET.has(op)) params.target = fieldString(block, 'TARGET', 'selected_unit')
    if (!SKIP_AMOUNT.has(op)) params.amount = constantExpr(fieldNumber(block, 'AMOUNT', 1))
    const damageType = fieldString(block, 'DAMAGE_TYPE', '')
    if (op === 'damage' && damageType) params.damageType = damageType
    return { id, kind: 'effect', op, params }
  }
  if (block.type === BLOCK.condition) {
    const op = fieldString(block, 'OP', 'target_has_armor')
    if (op === 'random_chance') params.chance = fieldNumber(block, 'CHANCE', Number(params.chance ?? 0.1))
    const node: NestedNode = {
      id, kind: 'condition', op, params,
    }
    const thenNodes = inputStack(block, 'THEN')
    const elseNodes = inputStack(block, 'ELSE')
    if (thenNodes) node.then = thenNodes
    if (elseNodes) node.else = elseNodes
    return node
  }
  if (block.type === BLOCK.repeat) {
    params.count = constantExpr(fieldNumber(block, 'COUNT', 2))
    const node: NestedNode = { id, kind: 'repeat', op: 'repeat', params }
    const children = inputStack(block, 'DO')
    if (children) node.children = children
    return node
  }
  if (block.type === BLOCK.foreach) {
    params.range = fieldNumber(block, 'RANGE', 1)
    const node: NestedNode = {
      id, kind: 'foreach', op: fieldString(block, 'OP', 'units_in_front_area'), params,
    }
    const children = inputStack(block, 'DO')
    if (children) node.children = children
    return node
  }
  const node: NestedNode = { id, kind: 'sequence', op: 'sequence', params: extra.params ?? {} }
  const children = inputStack(block, 'DO')
  if (children) node.children = children
  return node
}

function statementInput(name: string, nodes?: NestedNode[]): Record<string, { block: BlocklyBlockState }> | undefined {
  const head = link((nodes ?? []).map((node) => nodeToBlock(node)))
  return head ? { [name]: { block: head } } : undefined
}

function mergeInputs(
  ...parts: Array<Record<string, { block: BlocklyBlockState }> | undefined>
): Record<string, { block: BlocklyBlockState }> | undefined {
  const merged = Object.assign({}, ...parts.filter(Boolean))
  return Object.keys(merged).length ? merged : undefined
}

function nodeToBlock(node: NestedNode): BlocklyBlockState {
  const extra: LofExtraState = { nodeId: node.id, params: {} }
  if (node.kind === 'effect') {
    extra.params = restParams(node.params, ['target', 'amount', 'stacks', 'damageType'])
    return {
      type: BLOCK.effect,
      id: node.id,
      fields: {
        OP: node.op || 'no_op',
        TARGET: String(node.params.target ?? 'selected_unit'),
        AMOUNT: readConstant(node.params.amount ?? node.params.stacks, 1),
        DAMAGE_TYPE: String(node.params.damageType ?? 'normal'),
      },
      extraState: extra,
    }
  }
  if (node.kind === 'condition') {
    extra.params = restParams(node.params, ['chance'])
    return {
      type: BLOCK.condition,
      id: node.id,
      fields: {
        OP: node.op || 'target_has_armor',
        CHANCE: Number(node.params.chance ?? 0.1),
      },
      extraState: extra,
      inputs: mergeInputs(statementInput('THEN', node.then), statementInput('ELSE', node.else)),
    }
  }
  if (node.kind === 'repeat') {
    extra.params = restParams(node.params, ['count'])
    return {
      type: BLOCK.repeat,
      id: node.id,
      fields: { COUNT: readConstant(node.params.count, 2) },
      extraState: extra,
      inputs: statementInput('DO', node.children),
    }
  }
  if (node.kind === 'foreach') {
    extra.params = restParams(node.params, ['range'])
    return {
      type: BLOCK.foreach,
      id: node.id,
      fields: {
        OP: node.op || 'units_in_front_area',
        RANGE: Number(node.params.range ?? 1),
      },
      extraState: extra,
      inputs: statementInput('DO', node.children),
    }
  }
  extra.params = { ...node.params }
  return {
    type: BLOCK.sequence,
    id: node.id,
    extraState: extra,
    inputs: statementInput('DO', node.children),
  }
}

function rootChildren(root: NestedNode): NestedNode[] {
  if (root.kind === 'sequence') return root.children ?? []
  return [root]
}

/** 把嵌套行为脚本编成 Blockly 工作区 JSON。 */
export function nestedToWorkspaceState(scripts: NestedBehavior[]): BlocklyWorkspaceState {
  const blocks = scripts.map((script, index) => {
    const next = link(rootChildren(script.root).map((node) => nodeToBlock(node)))
    const hat: BlocklyBlockState = {
      type: BLOCK.hat,
      id: `${script.id}.hat`,
      x: 40,
      y: 40 + index * 220,
      fields: { TRIGGER: script.trigger },
      extraState: {
        nodeId: script.root.id,
        behaviorId: script.id,
        enabled: script.enabled,
        priority: script.priority,
      },
    }
    if (next) hat.next = { block: next }
    return hat
  })
  return { blocks: { languageVersion: 0, blocks } }
}

function uniqueBehaviorId(ownerId: string, trigger: string, used: Set<string>): string {
  const base = `${ownerId}.${trigger}`
  if (!used.has(base)) return base
  return newNodeId(base, used)
}

/** 把 Blockly 工作区 JSON 解回嵌套行为脚本。 */
export function workspaceStateToNested(state: BlocklyWorkspaceState, ownerId: string): NestedBehavior[] {
  const top = state.blocks?.blocks ?? []
  const used = new Set<string>()
  const scripts: NestedBehavior[] = []
  for (const block of top) {
    if (block.type !== BLOCK.hat) continue
    const extra = block.extraState ?? {}
    const trigger = fieldString(block, 'TRIGGER', 'on_play')
    const behaviorId = extra.behaviorId && !used.has(extra.behaviorId)
      ? extra.behaviorId
      : uniqueBehaviorId(ownerId, trigger, used)
    used.add(behaviorId)
    const children = walk(block.next?.block)
    const rootId = extra.nodeId || `${behaviorId}.root`
    scripts.push({
      id: behaviorId,
      trigger,
      enabled: extra.enabled !== false,
      priority: Number(extra.priority ?? 0),
      root: {
        id: rootId,
        kind: 'sequence',
        op: 'sequence',
        params: {},
        children: children.length ? children : undefined,
      },
    })
  }
  return scripts
}
