<script setup lang="ts">
import * as Blockly from 'blockly'
import * as zhHans from 'blockly/msg/zh-hans'
import { nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { nestedToRows, parseParams, prettySource, rowsToNested } from '../behavior-tree'
import { buildToolbox, registerLofBlocks, setBlockCatalogs } from '../blockly-blocks'
import { BLOCK, nestedToWorkspaceState, workspaceStateToNested } from '../blockly-codec'
import type { BehaviorNodeRow, BootstrapPayload } from '../types'
import BlockInspector from './BlockInspector.vue'

const locale: Record<string, string> = {}
for (const [key, value] of Object.entries(zhHans)) {
  if (typeof value === 'string') locale[key] = value
}
Blockly.setLocale(locale)

const props = defineProps<{
  nodes: BehaviorNodeRow[]
  ownerId: string
  selectedId: string
  options?: BootstrapPayload['options']
}>()

const emit = defineEmits<{
  'update:nodes': [nodes: BehaviorNodeRow[]]
  'update:selectedId': [id: string]
}>()

const host = ref<HTMLDivElement>()
const selected = ref<BehaviorNodeRow>()

let workspace: Blockly.WorkspaceSvg | undefined
let applying = false
let lastSig = ''
let resizeObserver: ResizeObserver | undefined

interface LofBlock extends Blockly.Block {
  lofNodeId?: string
  lofBehaviorId?: string
  lofEnabled?: boolean
  lofPriority?: number
  lofParams?: Record<string, unknown>
}

function signature(nodes: BehaviorNodeRow[]): string {
  return `${props.ownerId}\n${prettySource(nodes)}`
}

function nodeIdOf(block: Blockly.Block): string {
  const extra = block as LofBlock
  if (block.type === BLOCK.hat) return extra.lofNodeId || `${extra.lofBehaviorId ?? props.ownerId}.root`
  return extra.lofNodeId || block.id
}

function blockByNodeId(nodeId: string): LofBlock | undefined {
  if (!workspace) return undefined
  return workspace.getAllBlocks(false).find((block) => nodeIdOf(block) === nodeId) as LofBlock | undefined
}

function emitFromWorkspace(): void {
  if (!workspace || applying) return
  const state = Blockly.serialization.workspaces.save(workspace)
  const rows = nestedToRows(workspaceStateToNested(state, props.ownerId))
  lastSig = signature(rows)
  selected.value = rows.find((node) => node.nodeId === props.selectedId)
  emit('update:nodes', rows)
}

function loadWorkspace(nodes: BehaviorNodeRow[]): void {
  if (!workspace) return
  applying = true
  Blockly.Events.disable()
  try {
    const scripts = rowsToNested(nodes)
    const state = nestedToWorkspaceState(scripts)
    workspace.clear()
    if (state.blocks?.blocks?.length) {
      Blockly.serialization.workspaces.load(state, workspace, { recordUndo: false })
    }
    lastSig = signature(nodes)
  } finally {
    Blockly.Events.enable()
    void nextTick(() => { applying = false })
  }
}

function onWorkspaceEvent(event: Blockly.Events.Abstract): void {
  if (!workspace || applying) return
  if (event.type === 'selected') {
    const blockId = (event as { newElementId?: string }).newElementId
    if (!blockId) return
    const block = workspace.getBlockById(blockId)
    const nodeId = block ? nodeIdOf(block) : ''
    emit('update:selectedId', nodeId)
    selected.value = props.nodes.find((node) => node.nodeId === nodeId) ?? selected.value
    return
  }
  if (event.isUiEvent) return
  if (event.type === 'finished_loading') return
  emitFromWorkspace()
}

function patchSelected(partial: Partial<BehaviorNodeRow>): void {
  const block = blockByNodeId(props.selectedId)
  if (!block || !workspace) return
  applying = true
  try {
    if (partial.triggerKey && block.type === BLOCK.hat) {
      block.setFieldValue(String(partial.triggerKey), 'TRIGGER')
    }
    if (partial.operationKey && block.getField('OP')) block.setFieldValue(String(partial.operationKey), 'OP')
    if (partial.parametersJson) {
      const params = parseParams(partial.parametersJson)
      ;(block as LofBlock).lofParams = params
      if (block.getField('TARGET') && params.target) block.setFieldValue(String(params.target), 'TARGET')
      if (block.getField('AMOUNT') && params.amount !== undefined) {
        const amount = params.amount
        const value = typeof amount === 'object' && amount && 'value' in amount ? Number((amount as { value: number }).value) : Number(amount)
        if (Number.isFinite(value)) block.setFieldValue(value, 'AMOUNT')
      }
      if (block.getField('COUNT') && params.count !== undefined) {
        const count = params.count
        const value = typeof count === 'object' && count && 'value' in count ? Number((count as { value: number }).value) : Number(count)
        if (Number.isFinite(value)) block.setFieldValue(value, 'COUNT')
      }
      if (block.getField('CHANCE') && params.chance !== undefined) {
        const chance = Number(params.chance)
        if (Number.isFinite(chance)) block.setFieldValue(chance, 'CHANCE')
      }
      if (block.getField('DAMAGE_TYPE') && params.damageType) block.setFieldValue(String(params.damageType), 'DAMAGE_TYPE')
      if (block.getField('RANGE') && params.range !== undefined) block.setFieldValue(Number(params.range), 'RANGE')
    }
  } finally {
    applying = false
  }
  emitFromWorkspace()
}

function removeSelected(): void {
  const block = blockByNodeId(props.selectedId)
  block?.dispose(true)
  emit('update:selectedId', '')
}

onMounted(async () => {
  await nextTick()
  if (!host.value) return
  setBlockCatalogs(props.options)
  registerLofBlocks()
  workspace = Blockly.inject(host.value, {
    toolbox: buildToolbox(),
    renderer: 'zelos',
    theme: Blockly.Themes.Zelos,
    sounds: false,
    media: `${import.meta.env.BASE_URL}blockly-media/`,
    trashcan: true,
    zoom: { controls: true, wheel: true, startScale: 0.85 },
    move: { scrollbars: true, drag: true, wheel: true },
    grid: { spacing: 20, length: 3, colour: '#44556c', snap: false },
  })
  workspace.addChangeListener(onWorkspaceEvent)
  resizeObserver = new ResizeObserver(() => {
    if (workspace) Blockly.svgResize(workspace)
  })
  resizeObserver.observe(host.value)
  loadWorkspace(props.nodes)
  selected.value = props.nodes.find((node) => node.nodeId === props.selectedId)
})

onBeforeUnmount(() => {
  resizeObserver?.disconnect()
  workspace?.dispose()
  workspace = undefined
})

watch(() => props.options, (options) => {
  setBlockCatalogs(options)
  if (workspace) workspace.updateToolbox(buildToolbox())
}, { deep: true })

watch(() => [props.ownerId, props.nodes] as const, () => {
  if (signature(props.nodes) === lastSig) {
    selected.value = props.nodes.find((node) => node.nodeId === props.selectedId)
    return
  }
  loadWorkspace(props.nodes)
  selected.value = props.nodes.find((node) => node.nodeId === props.selectedId)
})

watch(() => props.selectedId, (nodeId) => {
  selected.value = props.nodes.find((node) => node.nodeId === nodeId)
})
</script>

<template>
  <div class="blockly-layout">
    <main class="blockly-stage">
      <div ref="host" class="blockly-host" />
    </main>
    <BlockInspector :node="selected" :options="options" @patch="patchSelected" @remove="removeSelected" />
  </div>
</template>
