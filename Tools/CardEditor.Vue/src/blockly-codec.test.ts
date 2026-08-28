import { describe, expect, it } from 'vitest'
import { nestedToRows, rowsToNested } from './behavior-tree'
import { nestedToWorkspaceState, workspaceStateToNested } from './blockly-codec'
import type { BehaviorNodeRow } from './types'

function row(partial: Partial<BehaviorNodeRow> & Pick<BehaviorNodeRow, 'nodeId' | 'nodeKind' | 'operationKey'>): BehaviorNodeRow {
  return {
    behaviorId: 'sword.on_play', triggerKey: 'on_play', priority: 0, enabled: true,
    parentNodeId: '', branchKey: 'children', sortOrder: 0, parametersJson: '{}', ...partial,
  }
}

describe('blockly-codec', () => {
  it('帽子、效果、如果、重复能与行为行往返', () => {
    const rows: BehaviorNodeRow[] = [
      row({ nodeId: 'root', nodeKind: 'sequence', operationKey: 'sequence' }),
      row({
        nodeId: 'hit', parentNodeId: 'root', sortOrder: 1, nodeKind: 'effect', operationKey: 'damage',
        parametersJson: '{"target":"selected_unit","amount":{"kind":"constant","value":4},"damageType":"fire"}',
      }),
      row({ nodeId: 'if', parentNodeId: 'root', sortOrder: 2, nodeKind: 'condition', operationKey: 'target_has_armor' }),
      row({
        nodeId: 'bonus', parentNodeId: 'if', branchKey: 'then', sortOrder: 1, nodeKind: 'effect', operationKey: 'gain_armor',
        parametersJson: '{"target":"self","amount":{"kind":"constant","value":3}}',
      }),
      row({
        nodeId: 'loop', parentNodeId: 'root', sortOrder: 3, nodeKind: 'repeat', operationKey: 'repeat',
        parametersJson: '{"count":{"kind":"constant","value":3}}',
      }),
      row({ nodeId: 'tick', parentNodeId: 'loop', sortOrder: 1, nodeKind: 'effect', operationKey: 'draw_cards' }),
    ]
    const scripts = rowsToNested(rows)
    const state = nestedToWorkspaceState(scripts)
    const hats = state.blocks?.blocks ?? []
    expect(hats).toHaveLength(1)
    expect(hats[0].fields?.TRIGGER).toBe('on_play')
    expect(hats[0].next?.block?.fields?.OP).toBe('damage')
    const round = nestedToRows(workspaceStateToNested(state, 'sword'))
    expect(round.find((item) => item.nodeId === 'hit')?.operationKey).toBe('damage')
    expect(JSON.parse(round.find((item) => item.nodeId === 'hit')!.parametersJson).damageType).toBe('fire')
    expect(round.find((item) => item.nodeId === 'bonus')?.branchKey).toBe('then')
    expect(JSON.parse(round.find((item) => item.nodeId === 'loop')!.parametersJson).count.value).toBe(3)
  })

  it('随机概率 0.1 不会被收成整数', () => {
    const rows: BehaviorNodeRow[] = [
      row({ nodeId: 'root', nodeKind: 'sequence', operationKey: 'sequence' }),
      row({
        nodeId: 'if', parentNodeId: 'root', sortOrder: 1, nodeKind: 'condition', operationKey: 'random_chance',
        parametersJson: '{"chance":0.1}',
      }),
    ]
    const round = nestedToRows(workspaceStateToNested(nestedToWorkspaceState(rowsToNested(rows)), 'sword'))
    expect(JSON.parse(round.find((item) => item.nodeId === 'if')!.parametersJson).chance).toBe(0.1)
  })
})
