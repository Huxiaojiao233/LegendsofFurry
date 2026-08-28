<script setup lang="ts">
import { computed } from 'vue'
import type { BehaviorNodeRow, BootstrapPayload } from '../types'
import { constantExpr, parseParams, readConstant } from '../behavior-tree'
import { conditionLabels, effectLabels, nodeLabels, targetLabels, triggerLabels, zh } from '../labels'

const props = defineProps<{
  node?: BehaviorNodeRow
  options?: BootstrapPayload['options']
}>()

const emit = defineEmits<{
  patch: [partial: Partial<BehaviorNodeRow>]
  remove: []
}>()

const params = computed(() => parseParams(props.node?.parametersJson ?? '{}'))

function writeParams(next: Record<string, unknown>): void {
  emit('patch', { parametersJson: JSON.stringify(next) })
}

function setParam(name: string, value: unknown): void {
  writeParams({ ...params.value, [name]: value })
}

function setAmount(name: string, value: number | undefined): void {
  if (value === undefined || Number.isNaN(value)) return
  setParam(name, constantExpr(value))
}

const amount = computed({
  get: () => readConstant(params.value.amount ?? params.value.stacks, 1),
  set: (value: number) => setAmount('amount', value),
})

const count = computed({
  get: () => readConstant(params.value.count, 2),
  set: (value: number) => setAmount('count', value),
})
</script>

<template>
  <aside class="node-inspector" @mousedown.stop @pointerdown.stop>
    <div class="designer-title">积木属性</div>
    <el-empty v-if="!node" :image-size="56" description="点选一块积木" />
    <el-form v-else label-position="top" size="small">
      <p class="inspector-kind">{{ zh(nodeLabels, node.nodeKind) }}</p>
      <el-form-item v-if="!node.parentNodeId" label="时机">
        <el-select :model-value="node.triggerKey" @update:model-value="emit('patch', { triggerKey: String($event) })">
          <el-option v-for="v in options?.triggers ?? []" :key="v" :value="v" :label="zh(triggerLabels, v)" />
        </el-select>
      </el-form-item>
      <el-form-item v-if="node.nodeKind === 'effect'" label="做什么">
        <el-select filterable :model-value="node.operationKey" @update:model-value="emit('patch', { operationKey: String($event) })">
          <el-option v-for="v in options?.effects ?? []" :key="v" :value="v" :label="zh(effectLabels, v)" />
        </el-select>
      </el-form-item>
      <el-form-item v-else-if="node.nodeKind === 'condition'" label="判断">
        <el-select filterable :model-value="node.operationKey" @update:model-value="emit('patch', { operationKey: String($event) })">
          <el-option v-for="v in options?.conditions ?? []" :key="v" :value="v" :label="zh(conditionLabels, v)" />
        </el-select>
      </el-form-item>
      <el-form-item v-else-if="node.nodeKind === 'foreach'" label="挨个选谁">
        <el-select filterable :model-value="node.operationKey" @update:model-value="emit('patch', { operationKey: String($event) })">
          <el-option v-for="v in options?.foreachTargets ?? []" :key="v" :value="v" :label="zh(targetLabels, v)" />
        </el-select>
      </el-form-item>
      <el-form-item v-if="node.nodeKind === 'effect' && !['end_turn', 'no_op'].includes(node.operationKey)" label="对谁">
        <el-select :model-value="String(params.target ?? 'selected_unit')" @update:model-value="setParam('target', $event)">
          <el-option v-for="v in options?.effectTargets ?? []" :key="v" :value="v" :label="zh(targetLabels, v)" />
        </el-select>
      </el-form-item>
      <el-form-item v-if="node.nodeKind === 'effect' && !['end_turn', 'no_op', 'play_vfx', 'play_sfx', 'cancel_query'].includes(node.operationKey)" label="数值">
        <el-input-number v-model="amount" :controls="false" :precision="2" :step="0.01" />
      </el-form-item>
      <el-form-item v-if="node.operationKey === 'damage' || params.damageType" label="伤害类型">
        <el-select :model-value="String(params.damageType ?? 'normal')" allow-create filterable @update:model-value="setParam('damageType', $event)">
          <el-option v-for="v in ['normal', 'fire', 'ice', 'lightning', 'light', 'dark', 'poison', 'true']" :key="v" :value="v" />
        </el-select>
      </el-form-item>
      <el-form-item v-if="node.operationKey === 'random_chance'" label="概率（0～1，0.1 为一成）">
        <el-input-number :model-value="Number(params.chance ?? 0.1)" :min="0" :max="1" :precision="2" :step="0.01" :controls="false" @update:model-value="setParam('chance', $event)" />
      </el-form-item>
      <el-form-item v-if="node.operationKey === 'modify_query_value'" label="怎么改">
        <el-select :model-value="String(params.mode ?? 'add')" @update:model-value="setParam('mode', $event)">
          <el-option value="add" label="加上" />
          <el-option value="set" label="设为" />
          <el-option value="min" label="取较小" />
          <el-option value="max" label="取较大" />
          <el-option value="multiply_percent" label="按百分比乘" />
        </el-select>
      </el-form-item>
      <el-form-item v-if="node.operationKey === 'modify_query_value'" label="仅限卡类别（可空）">
        <el-input :model-value="String(params.familyId ?? '')" @update:model-value="setParam('familyId', $event)" />
      </el-form-item>
      <el-form-item v-if="['add_status', 'set_status', 'reduce_status', 'remove_status', 'target_has_status'].includes(node.operationKey)" label="状态 ID">
        <el-input :model-value="String(params.statusId ?? '')" @update:model-value="setParam('statusId', $event)" />
      </el-form-item>
      <el-form-item v-if="node.nodeKind === 'repeat'" label="重复次数">
        <el-input-number v-model="count" :min="1" :controls="false" />
      </el-form-item>
      <el-form-item v-if="node.nodeKind === 'foreach'" label="范围">
        <el-input-number :model-value="Number(params.range ?? 1)" :min="0" :controls="false" @update:model-value="setParam('range', $event)" />
      </el-form-item>
      <el-button type="danger" plain @click="emit('remove')">删除这块积木</el-button>
    </el-form>
  </aside>
</template>
