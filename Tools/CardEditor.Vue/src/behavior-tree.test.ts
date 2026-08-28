import { describe, expect, it } from 'vitest'
import type { BehaviorNodeRow } from './types'
import { nestedToRows, parseSource, prettySource, rowsToNested } from './behavior-tree'

function row(partial: Partial<BehaviorNodeRow> & Pick<BehaviorNodeRow, 'nodeId' | 'nodeKind' | 'operationKey'>): BehaviorNodeRow {
  return {
    behaviorId: 'card.on_play', triggerKey: 'on_play', priority: 0, enabled: true,
    parentNodeId: '', branchKey: 'children', sortOrder: 0, parametersJson: '{}', ...partial,
  }
}

describe('behavior-tree', () => {
  it('条件分支与重复往返后保持父子和参数', () => {
    const rows: BehaviorNodeRow[] = [
      row({ nodeId: 'root', nodeKind: 'sequence', operationKey: 'sequence', sortOrder: 0 }),
      row({ nodeId: 'hit', parentNodeId: 'root', sortOrder: 1, nodeKind: 'effect', operationKey: 'damage', parametersJson: '{"amount":{"kind":"constant","value":4}}' }),
      row({ nodeId: 'if', parentNodeId: 'root', sortOrder: 2, nodeKind: 'condition', operationKey: 'target_has_armor', parametersJson: '{}' }),
      row({ nodeId: 'bonus', parentNodeId: 'if', branchKey: 'then', sortOrder: 1, nodeKind: 'effect', operationKey: 'damage', parametersJson: '{"amount":{"kind":"constant","value":2}}' }),
      row({ nodeId: 'loop', parentNodeId: 'root', sortOrder: 3, nodeKind: 'repeat', operationKey: 'repeat', parametersJson: '{"count":{"kind":"constant","value":3}}' }),
      row({ nodeId: 'tick', parentNodeId: 'loop', sortOrder: 1, nodeKind: 'effect', operationKey: 'damage', parametersJson: '{}' }),
    ]
    const nested = rowsToNested(rows)
    expect(nested).toHaveLength(1)
    expect(nested[0].root.children?.map((n) => n.kind)).toEqual(['effect', 'condition', 'repeat'])
    expect(nested[0].root.children?.[1].then?.[0].op).toBe('damage')
    const round = nestedToRows(nested)
    expect(round.map((item) => `${item.nodeId}:${item.parentNodeId}:${item.branchKey}`)).toEqual(
      rows.map((item) => `${item.nodeId}:${item.parentNodeId}:${item.branchKey}`),
    )
    const parsed = parseSource(prettySource(rows))
    expect(parsed).toHaveLength(rows.length)
    expect(JSON.parse(parsed.find((item) => item.nodeId === 'loop')!.parametersJson).count.value).toBe(3)
  })
})
