<script setup lang="ts">
import { ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import { nestedToRows, parseSource, prettySource, rowsToNested } from '../behavior-tree'
import type { BehaviorNodeRow, BootstrapPayload } from '../types'
import BlocklyCanvas from './BlocklyCanvas.vue'

const props = defineProps<{
  ownerId: string
  nodes: BehaviorNodeRow[]
  options?: BootstrapPayload['options']
  issues: string[]
}>()

const emit = defineEmits<{
  'update:nodes': [nodes: BehaviorNodeRow[]]
}>()

const tab = ref('blocks')
const selectedNodeId = ref('')
const sourceKind = ref<'behavior' | 'record'>('behavior')
const sourceText = ref('')
const sourceError = ref('')

function refreshSource(): void {
  sourceError.value = ''
  sourceText.value = sourceKind.value === 'behavior'
    ? prettySource(props.nodes)
    : JSON.stringify({ ownerId: props.ownerId, behaviors: rowsToNested(props.nodes) }, null, 2)
}

function applySource(): void {
  try {
    if (sourceKind.value === 'behavior') emit('update:nodes', parseSource(sourceText.value))
    else {
      const parsed = JSON.parse(sourceText.value) as { behaviors?: unknown; nodes?: BehaviorNodeRow[] }
      if (Array.isArray(parsed.nodes) && parsed.nodes.length) emit('update:nodes', parsed.nodes)
      else emit('update:nodes', nestedToRows((parsed.behaviors ?? []) as never))
    }
    sourceError.value = ''
    ElMessage.success('源码已应用到积木。')
  } catch (error) {
    sourceError.value = error instanceof Error ? error.message : String(error)
  }
}

watch(tab, (name) => { if (name === 'source') refreshSource() })
watch(sourceKind, () => { if (tab.value === 'source') refreshSource() })
</script>

<template>
  <el-tabs v-model="tab" class="behavior-tabs">
    <el-tab-pane label="积木" name="blocks">
      <BlocklyCanvas
        :nodes="nodes"
        :owner-id="ownerId"
        :selected-id="selectedNodeId"
        :options="options"
        @update:nodes="emit('update:nodes', $event)"
        @update:selected-id="selectedNodeId = $event"
      />
    </el-tab-pane>
    <el-tab-pane label="源码" name="source">
      <div class="source-toolbar">
        <el-radio-group v-model="sourceKind" size="small">
          <el-radio-button value="behavior">行为脚本</el-radio-button>
          <el-radio-button value="record">嵌套 JSON</el-radio-button>
        </el-radio-group>
        <el-button type="primary" size="small" @click="applySource">应用到积木</el-button>
        <el-button size="small" @click="refreshSource">从积木重新生成</el-button>
        <span class="source-error" v-if="sourceError">{{ sourceError }}</span>
      </div>
      <el-input v-model="sourceText" type="textarea" class="source-editor" :autosize="{ minRows: 22, maxRows: 42 }" spellcheck="false" />
    </el-tab-pane>
    <el-tab-pane :label="`校验问题（${issues.length}）`" name="issues">
      <el-empty v-if="issues.length===0" description="尚无校验问题" />
      <el-alert v-for="(issue,index) in issues" :key="index" :title="issue" :type="issue.includes('[ERROR]')?'error':'warning'" :closable="false" class="issue" />
    </el-tab-pane>
  </el-tabs>
</template>
