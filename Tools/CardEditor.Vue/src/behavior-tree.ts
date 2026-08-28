import type { BehaviorNodeRow } from './types'

/** 源码/积木共用的嵌套节点，保存时再展平为数据库行。 */
export interface NestedNode {
  id: string
  kind: string
  op: string
  params: Record<string, unknown>
  children?: NestedNode[]
  then?: NestedNode[]
  else?: NestedNode[]
}

/** 一条时机脚本：帽子积木 + 根顺序里的子积木。 */
export interface NestedBehavior {
  id: string
  trigger: string
  enabled: boolean
  priority: number
  root: NestedNode
}

/** 安全解析节点参数 JSON，损坏时返回空对象以免打断编辑。 */
export function parseParams(json: string): Record<string, unknown> {
  try {
    const value = JSON.parse(json || '{}') as unknown
    return value && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : {}
  } catch {
    return {}
  }
}

/** 从常量表达式或整数读取策划可编辑的数字。 */
export function readConstant(value: unknown, fallback = 0): number {
  if (typeof value === 'number' && Number.isFinite(value)) return value
  if (value && typeof value === 'object' && 'kind' in value) {
    const record = value as { kind?: unknown; value?: unknown }
    if (record.kind === 'constant') {
      const nested = Number(record.value)
      if (Number.isFinite(nested)) return nested
    }
  }
  return fallback
}

/** 写成引擎识别的整数常量表达式。 */
export function constantExpr(value: number): { kind: 'constant'; value: number } {
  return { kind: 'constant', value }
}

/** 把扁平行为行组装成嵌套脚本，孤儿节点挂到各自行为末尾。 */
export function rowsToNested(rows: BehaviorNodeRow[]): NestedBehavior[] {
  const byBehavior = new Map<string, BehaviorNodeRow[]>()
  for (const row of rows) {
    const list = byBehavior.get(row.behaviorId) ?? []
    list.push(row)
    byBehavior.set(row.behaviorId, list)
  }
  const scripts: NestedBehavior[] = []
  for (const [behaviorId, nodes] of [...byBehavior.entries()].sort(([a], [b]) => a.localeCompare(b))) {
    const meta = nodes[0]
    const visited = new Set<string>()
    const toNested = (row: BehaviorNodeRow): NestedNode => {
      visited.add(row.nodeId)
      const node: NestedNode = {
        id: row.nodeId,
        kind: row.nodeKind,
        op: row.operationKey,
        params: parseParams(row.parametersJson),
      }
      const kids = nodes.filter((child) => child.parentNodeId === row.nodeId)
        .sort((a, b) => a.branchKey.localeCompare(b.branchKey) || a.sortOrder - b.sortOrder)
      if (row.nodeKind === 'condition') {
        const thenNodes = kids.filter((child) => child.branchKey === 'then').map(toNested)
        const elseNodes = kids.filter((child) => child.branchKey === 'else').map(toNested)
        if (thenNodes.length) node.then = thenNodes
        if (elseNodes.length) node.else = elseNodes
      } else {
        const children = kids.filter((child) => child.branchKey !== 'else' && child.branchKey !== 'then').map(toNested)
        if (children.length) node.children = children
      }
      return node
    }
    const roots = nodes.filter((node) => !node.parentNodeId).sort((a, b) => a.sortOrder - b.sortOrder)
    const primary = roots[0]
    if (!primary) continue
    const root = toNested(primary)
    for (const leftover of nodes.filter((node) => !visited.has(node.nodeId))) {
      root.children = [...(root.children ?? []), toNested(leftover)]
    }
    scripts.push({
      id: behaviorId,
      trigger: meta.triggerKey,
      enabled: meta.enabled,
      priority: meta.priority,
      root,
    })
  }
  return scripts
}

/** 把嵌套脚本展平为仓储所需的行为行，并重写父子与顺序。 */
export function nestedToRows(scripts: NestedBehavior[]): BehaviorNodeRow[] {
  const rows: BehaviorNodeRow[] = []
  for (const script of scripts) {
    const walk = (node: NestedNode, parentId: string, branch: string, order: number): void => {
      rows.push({
        behaviorId: script.id,
        triggerKey: script.trigger,
        priority: script.priority,
        enabled: script.enabled,
        nodeId: node.id,
        parentNodeId: parentId,
        branchKey: branch,
        sortOrder: order,
        nodeKind: node.kind,
        operationKey: node.op,
        parametersJson: JSON.stringify(node.params ?? {}),
      })
      if (node.kind === 'condition') {
        ;(node.then ?? []).forEach((child, index) => walk(child, node.id, 'then', index + 1))
        ;(node.else ?? []).forEach((child, index) => walk(child, node.id, 'else', index + 1))
      } else {
        ;(node.children ?? []).forEach((child, index) => walk(child, node.id, 'children', index + 1))
      }
    }
    walk(script.root, '', 'children', 0)
  }
  return rows
}

/** 生成策划可读的嵌套 JSON 源码。 */
export function prettySource(rows: BehaviorNodeRow[]): string {
  return JSON.stringify(rowsToNested(rows), null, 2)
}

/** 解析嵌套 JSON 源码；失败时抛出中文错误。 */
export function parseSource(text: string): BehaviorNodeRow[] {
  let parsed: unknown
  try {
    parsed = JSON.parse(text)
  } catch {
    throw new Error('源码不是合法 JSON。')
  }
  if (!Array.isArray(parsed)) throw new Error('源码根节点必须是行为脚本数组。')
  const scripts = parsed as NestedBehavior[]
  for (const script of scripts) {
    if (!script || typeof script !== 'object') throw new Error('存在无效的行为脚本。')
    if (!script.id || !script.trigger || !script.root) throw new Error('每条脚本需要 id、trigger 和 root。')
  }
  return nestedToRows(scripts)
}

/** 生成不会与现有节点冲突的稳定 ID。 */
export function newNodeId(prefix: string, existing: Iterable<string>): string {
  const used = new Set(existing)
  const stamp = Date.now().toString(36)
  let candidate = `${prefix}.${stamp}`
  let serial = 1
  while (used.has(candidate)) {
    serial += 1
    candidate = `${prefix}.${stamp}.${serial}`
  }
  return candidate
}
