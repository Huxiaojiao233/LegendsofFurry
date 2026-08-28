<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { CopyDocument, Delete, DocumentChecked, FolderOpened, Plus, Refresh, UploadFilled } from '@element-plus/icons-vue'
import { useCardEditorApi } from './api'
import { cloneForIpc } from './clone'
import { nestedToRows, parseSource, prettySource, rowsToNested } from './behavior-tree'
import PackHub from './components/PackHub.vue'
import BlocklyCanvas from './components/BlocklyCanvas.vue'
import { familyLabels, rarityLabels, zh } from './labels'
import type { BootstrapPayload, CardEditorRecord, EditorSection, MenuCommand, OperationResult } from './types'

const api = useCardEditorApi()
const bootstrap = ref<BootstrapPayload>()
const cards = ref<CardEditorRecord[]>([])
const selected = ref<CardEditorRecord>()
const selectedNodeId = ref('')
const search = ref('')
const groupBy = ref<'familyId' | 'poolId' | 'rarityId'>('familyId')
const openGroups = ref<string[]>([])
const publishVersion = ref('development')
const rollbackVersion = ref('')
const status = ref('正在加载数据库…')
const issues = ref<string[]>([])
const artworkPreview = ref('')
const dirty = ref(false)
const busy = ref(false)
const activeTab = ref('blocks')
const sourceKind = ref<'behavior' | 'card'>('behavior')
const sourceText = ref('')
const sourceError = ref('')
const draggingArtwork = ref(false)
const section = ref<EditorSection>('cards')
const packHub = ref<{ saveCurrent: () => Promise<void>; newRecord: () => void; duplicateRecord: () => void; deleteCurrent: () => Promise<void> }>()
const sections: { id: EditorSection; label: string }[] = [
  { id: 'cards', label: '卡牌' }, { id: 'statuses', label: '状态' }, { id: 'classes', label: '职业' },
  { id: 'characters', label: '角色' }, { id: 'equipment', label: '装备' }, { id: 'pools', label: '卡池' },
  { id: 'decks', label: '牌库' }, { id: 'rarities', label: '稀有度' }, { id: 'assets', label: '资源' },
  { id: 'settings', label: '战斗规则' },
]
let suppressDirty = false

const filteredCards = computed(() => {
  const keyword = search.value.trim().toLowerCase()
  return cards.value.filter((card) => !keyword || card.cardId.toLowerCase().includes(keyword) || card.displayName.toLowerCase().includes(keyword))
})

const groupedCards = computed(() => {
  const groups = new Map<string, CardEditorRecord[]>()
  for (const card of filteredCards.value) {
    let key = String(card[groupBy.value] || '')
    if (groupBy.value === 'poolId' && !key) key = '__none__'
    if (groupBy.value === 'familyId' && !key) key = 'none'
    const list = groups.get(key) ?? []
    list.push(card)
    groups.set(key, list)
  }
  return [...groups.entries()].sort(([a], [b]) => a.localeCompare(b)).map(([key, items]) => ({
    key,
    title: groupTitle(key, items.length),
    items,
  }))
})

function groupTitle(key: string, count: number): string {
  if (groupBy.value === 'familyId') return `${zh(familyLabels, key)}（${count}）`
  if (groupBy.value === 'rarityId') return `${zh(rarityLabels, key)}（${count}）`
  if (key === '__none__') return `未入卡池（${count}）`
  return `${key}（${count}）`
}

/** 创建带安全默认值的新卡牌。 */
function createDefaultCard(cardId: string): CardEditorRecord {
  return {
    cardId, displayName: '新卡牌', description: '', artworkKey: '', rarityId: 'gray', familyId: 'none',
    actionCost: 0, manaCost: 0, spendAllAction: false, spendAllMana: false, isAttack: false,
    exhaustOnPlay: false, temporary: false, curse: false, unplayable: false, selectionMode: 'self',
    targetRange: 0, teamFilter: 'any', lifeStateFilter: 'alive', requiresLineOfSight: false,
    allowSelf: true, enabled: true, sortOrder: 0, rowVersion: 1, tags: [], poolId: '',
    includeInTestDeck: true, behaviorNodes: [], isExisting: false,
  }
}

/** 生成当前卡牌列表中尚未使用的稳定候选 ID。 */
function uniqueCardId(base = 'new_card'): string {
  const ids = new Set(cards.value.map((card) => card.cardId))
  if (!ids.has(base)) return base
  let suffix = 2
  while (ids.has(`${base}_${suffix}`)) suffix += 1
  return `${base}_${suffix}`
}

/** 加载数据库全部卡牌并尽量保留当前选择。 */
async function reload(preferredId?: string): Promise<void> {
  busy.value = true
  try {
    const payload = await api.bootstrap()
    bootstrap.value = payload
    cards.value = payload.cards.map((card) => ({ ...card, isExisting: true }))
    selectCard(cards.value.find((card) => card.cardId === preferredId) ?? cards.value[0])
    status.value = `已加载内容包。数据库：${payload.databasePath}`
    dirty.value = false
  } catch (error) { status.value = `加载失败：${error instanceof Error ? error.message : String(error)}` }
  finally { busy.value = false }
}

/** 切换当前卡牌并刷新卡面预览。 */
function selectCard(card?: CardEditorRecord): void {
  suppressDirty = true
  selected.value = card
  selectedNodeId.value = ''
  artworkPreview.value = ''
  if (card?.artworkKey) void loadArtworkPreview(card.artworkKey)
  refreshSource()
  nextTick(() => { suppressDirty = false })
}

function refreshSource(): void {
  sourceError.value = ''
  if (!selected.value) {
    sourceText.value = ''
    return
  }
  if (sourceKind.value === 'behavior') sourceText.value = prettySource(selected.value.behaviorNodes)
  else {
    const { isExisting: _ignored, ...rest } = selected.value
    sourceText.value = JSON.stringify({ ...rest, behaviors: rowsToNested(rest.behaviorNodes), behaviorNodes: undefined }, null, 2)
  }
}

/** 把源码编辑器写回当前卡牌。 */
function applySource(): void {
  if (!selected.value) return
  try {
    if (sourceKind.value === 'behavior') {
      selected.value.behaviorNodes = parseSource(sourceText.value)
    } else {
      const parsed = JSON.parse(sourceText.value) as CardEditorRecord & { behaviors?: ReturnType<typeof rowsToNested> }
      if (!parsed.cardId) throw new Error('整卡 JSON 需要 cardId。')
      const keptExisting = selected.value.isExisting
      const { behaviors, behaviorNodes, isExisting: _ignoredExisting, ...rest } = parsed
      Object.assign(selected.value, rest)
      selected.value.isExisting = keptExisting
      if (Array.isArray(behaviorNodes) && behaviorNodes.length > 0) selected.value.behaviorNodes = behaviorNodes
      else selected.value.behaviorNodes = nestedToRows(behaviors ?? [])
    }
    sourceError.value = ''
    dirty.value = true
    ElMessage.success('源码已应用到可视化编辑器。')
  } catch (error) {
    sourceError.value = error instanceof Error ? error.message : String(error)
  }
}

function setBehaviorNodes(nodes: CardEditorRecord['behaviorNodes']): void {
  if (selected.value) selected.value.behaviorNodes = nodes
}

/** 新建一张未保存卡牌并立即进入编辑。 */
function newCard(): void {
  const card = createDefaultCard(uniqueCardId())
  cards.value.push(card); selectCard(card); dirty.value = true
  status.value = '已创建未保存卡牌，请设置稳定 ID、效果和牌库入口。'
}

/** 深复制当前卡牌并重写全部行为及节点稳定 ID。 */
function duplicateCard(): void {
  if (!selected.value) return
  const oldId = selected.value.cardId
  const newId = uniqueCardId(`${oldId}_copy`)
  const copy = cloneForIpc(selected.value)
  copy.cardId = newId; copy.displayName += '（复制）'; copy.isExisting = false; copy.rowVersion = 1; copy.artworkKey = ''
  copy.behaviorNodes = copy.behaviorNodes.map((node) => ({
    ...node,
    behaviorId: node.behaviorId.replaceAll(oldId, newId),
    nodeId: node.nodeId.replaceAll(oldId, newId),
    parentNodeId: node.parentNodeId.replaceAll(oldId, newId),
  }))
  cards.value.push(copy); selectCard(copy); dirty.value = true; status.value = '已创建卡牌副本。'
}

/** 保存当前卡牌并从数据库重新加载权威快照。 */
async function saveCard(): Promise<void> {
  if (!selected.value) {
    ElMessage.warning('请先选择一张卡牌。')
    return
  }
  if (activeTab.value === 'source') {
    applySource()
    if (sourceError.value) {
      ElMessage.error(`源码有误，无法保存：${sourceError.value}`)
      return
    }
  }
  busy.value = true
  try {
    const result = await api.saveCard(cloneForIpc(selected.value))
    showResult(result)
    if (result.succeeded) await reload(selected.value.cardId)
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error)
    status.value = `保存失败：${message}`
    ElMessage.error(message)
  } finally { busy.value = false }
}

/** 确认后删除当前已保存卡牌。 */
async function deleteCard(): Promise<void> {
  if (!selected.value?.isExisting) return
  await ElMessageBox.confirm(`确定删除卡牌“${selected.value.displayName}”吗？`, '删除卡牌', { type: 'warning' })
  const result = await api.deleteCard(selected.value.cardId)
  showResult(result)
  if (result.succeeded) await reload()
}

/** 打开系统选图窗口并导入返回的文件。 */
async function chooseArtwork(): Promise<void> {
  const filePath = await api.chooseArtwork()
  if (filePath) await importArtwork(filePath)
}

/** 导入指定绝对路径卡面并绑定返回的资源 Key。 */
async function importArtwork(filePath: string): Promise<void> {
  if (!selected.value) return void ElMessage.warning('请先选择或新建一张卡牌。')
  const result = await api.importArtwork(filePath, selected.value.cardId)
  showResult(result)
  if (result.succeeded && result.value) {
    selected.value.artworkKey = result.value; dirty.value = true; await loadArtworkPreview(result.value)
  }
}

/** 接收资源管理器拖入的一张卡面图片。 */
async function dropArtwork(event: DragEvent): Promise<void> {
  draggingArtwork.value = false
  const files = event.dataTransfer?.files
  if (!files || files.length !== 1) return void ElMessage.warning('请一次只拖入一张卡面图片。')
  await importArtwork(api.getPathForFile(files[0]))
}

/** 读取并显示当前资源 Key 的卡面预览。 */
async function loadArtworkPreview(assetKey: string): Promise<void> {
  const result = await api.artworkPreview(assetKey)
  artworkPreview.value = result.succeeded ? result.value ?? '' : ''
}

/** 执行整库校验并切换到问题页。 */
async function validateContent(): Promise<void> {
  busy.value = true
  try { const result = await api.validate(publishVersion.value); issues.value = result.issues ?? []; activeTab.value = 'issues'; showResult(result) }
  finally { busy.value = false }
}

/** 发布当前版本到 Unity 并展示权威编译器结果。 */
async function publishContent(): Promise<void> {
  busy.value = true
  try { const result = await api.publish(publishVersion.value); issues.value = result.issues ?? []; showResult(result); if (result.succeeded) dirty.value = false }
  finally { busy.value = false }
}

/** 回退 Unity 当前内容指针到指定历史版本。 */
async function rollbackContent(): Promise<void> {
  showResult(await api.rollback(rollbackVersion.value))
}

/** 同步状态栏与右上角消息提示。 */
function showResult(result: OperationResult): void {
  status.value = result.message
  if (result.succeeded) ElMessage.success(result.message); else ElMessage.error(result.message)
}

/** 保存当前分区正在编辑的对象。 */
async function saveCurrent(): Promise<void> {
  try {
    if (section.value === 'cards') await saveCard()
    else await packHub.value?.saveCurrent()
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error)
    status.value = `保存失败：${message}`
    ElMessage.error(message)
  }
}

async function persistRecord<T>(run: (record: T) => Promise<OperationResult>, record: T): Promise<void> {
  busy.value = true
  try {
    const result = await run(cloneForIpc(record))
    showResult(result)
    if (result.succeeded) await reload(selected.value?.cardId)
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error)
    status.value = `保存失败：${message}`
    ElMessage.error(message)
  } finally { busy.value = false }
}

function handleMenu(command: MenuCommand): void {
  if (command.action === 'section') section.value = command.section
  else if (command.action === 'save') void saveCurrent()
  else if (command.action === 'reload') void reload(selected.value?.cardId)
  else if (command.action === 'validate') void validateContent()
  else if (command.action === 'publish') void publishContent()
  else if (command.action === 'new') section.value === 'cards' ? newCard() : packHub.value?.newRecord()
  else if (command.action === 'duplicate') section.value === 'cards' ? duplicateCard() : packHub.value?.duplicateRecord()
  else if (command.action === 'delete') section.value === 'cards' ? void deleteCard() : void packHub.value?.deleteCurrent()
}

watch(selected, () => { if (!suppressDirty) dirty.value = true }, { deep: true })
watch(groupBy, () => { openGroups.value = groupedCards.value.map((group) => group.key) }, { immediate: true })
watch(groupedCards, (groups) => {
  const keys = groups.map((group) => group.key)
  const known = new Set(openGroups.value)
  openGroups.value = [...openGroups.value.filter((key) => keys.includes(key)), ...keys.filter((key) => !known.has(key))]
})
watch(activeTab, (name) => { if (name === 'source') refreshSource() })
watch(sourceKind, () => { if (activeTab.value === 'source') refreshSource() })
onMounted(() => {
  api.onMenu(handleMenu)
  void reload()
})

</script>

<template>
  <el-container class="app-shell" v-loading="busy">
    <el-header class="toolbar">
      <div class="brand">内容包编辑器 <span class="tech">Electron · Vue 3 · Element Plus</span></div>
      <el-button-group>
        <el-button :icon="Plus" @click="section === 'cards' ? newCard() : packHub?.newRecord()">新建</el-button>
        <el-button :icon="CopyDocument" @click="section === 'cards' ? duplicateCard() : packHub?.duplicateRecord()">复制</el-button>
        <el-button type="primary" :icon="DocumentChecked" @click="void saveCurrent()">保存</el-button>
        <el-button type="danger" plain :icon="Delete" @click="section === 'cards' ? deleteCard() : packHub?.deleteCurrent()">删除</el-button>
      </el-button-group>
      <el-button @click="validateContent">验证整库</el-button>
      <el-button type="success" @click="publishContent">发布到 Unity</el-button>
      <el-input v-model="publishVersion" class="version-input" placeholder="发布版本" />
      <el-input v-model="rollbackVersion" class="version-input" placeholder="回退版本" />
      <el-button @click="rollbackContent">回退版本</el-button>
      <el-button :icon="Refresh" @click="reload(selected?.cardId)">刷新</el-button>
    </el-header>
    <el-header class="menubar" height="44px">
      <el-menu mode="horizontal" :ellipsis="false" :default-active="section" :key="section" @select="section = $event as EditorSection">
        <el-menu-item v-for="item in sections" :key="item.id" :index="item.id">{{ item.label }}</el-menu-item>
      </el-menu>
    </el-header>

    <el-container v-if="section === 'cards'" class="workspace">
      <el-aside width="300px" class="card-list-panel">
        <div class="panel-title">卡牌（{{ cards.length }}）</div>
        <el-input v-model="search" clearable placeholder="按 ID 或名称搜索" class="search" />
        <el-select v-model="groupBy" class="group-by" size="small">
          <el-option label="按类别分组" value="familyId" />
          <el-option label="按卡池分组" value="poolId" />
          <el-option label="按稀有度分组" value="rarityId" />
        </el-select>
        <el-scrollbar>
          <el-collapse v-model="openGroups">
            <el-collapse-item v-for="group in groupedCards" :key="group.key" :name="group.key" :title="group.title">
              <div v-for="card in group.items" :key="card.cardId" class="card-list-item" :class="{ active: card === selected }" @click="selectCard(card)">
                <strong>{{ card.displayName || card.cardId }}</strong>
                <small>{{ card.cardId }}</small>
              </div>
            </el-collapse-item>
          </el-collapse>
        </el-scrollbar>
      </el-aside>

      <el-main class="editor-main" v-if="selected">
        <section class="basic-panel">
          <div class="panel-title">基础属性</div>
          <el-form label-position="top" size="default">
            <el-form-item label="稳定 ID"><el-input v-model="selected.cardId" :disabled="selected.isExisting" /></el-form-item>
            <el-form-item label="卡牌名称"><el-input v-model="selected.displayName" /></el-form-item>
            <el-form-item label="描述"><el-input v-model="selected.description" type="textarea" :rows="4" /></el-form-item>
            <div class="form-grid">
              <el-form-item label="稀有度">
                <el-select v-model="selected.rarityId">
                  <el-option v-for="v in bootstrap?.options.rarities" :key="v" :value="v" :label="zh(rarityLabels, v)" />
                </el-select>
              </el-form-item>
              <el-form-item label="类别/家族"><el-input v-model="selected.familyId" placeholder="如 sword / none" /></el-form-item>
            </div>
            <el-form-item label="卡池 ID"><el-input v-model="selected.poolId" placeholder="为空表示不加入卡池" /></el-form-item>
            <el-form-item label="标签"><el-select v-model="selected.tags" multiple filterable allow-create default-first-option /></el-form-item>
            <el-form-item label="卡图资源 Key"><el-input v-model="selected.artworkKey" @change="loadArtworkPreview(selected.artworkKey)" /></el-form-item>
            <div class="artwork-drop" :class="{ dragging: draggingArtwork }" @dragenter.prevent="draggingArtwork=true" @dragover.prevent @dragleave.prevent="draggingArtwork=false" @drop.prevent="dropArtwork">
              <img v-if="artworkPreview" :src="artworkPreview" alt="卡面预览" />
              <div v-else><el-icon size="34"><UploadFilled /></el-icon><p>从资源管理器拖入卡面</p><small>PNG / JPG / JPEG</small></div>
            </div>
            <el-button :icon="FolderOpened" @click="chooseArtwork">选择 / 更换卡面…</el-button>
            <el-checkbox v-model="selected.includeInTestDeck">加入策划测试牌库</el-checkbox>
            <el-divider content-position="left">费用</el-divider>
            <div class="form-grid"><el-form-item label="行动力"><el-input-number v-model="selected.actionCost" :min="0" /></el-form-item><el-form-item label="魔力"><el-input-number v-model="selected.manaCost" :min="0" /></el-form-item></div>
            <div class="check-grid"><el-checkbox v-model="selected.spendAllAction">消耗全部行动力</el-checkbox><el-checkbox v-model="selected.spendAllMana">消耗全部魔力</el-checkbox><el-checkbox v-model="selected.isAttack">攻击牌</el-checkbox><el-checkbox v-model="selected.exhaustOnPlay">打出后消耗</el-checkbox><el-checkbox v-model="selected.temporary">临时牌</el-checkbox><el-checkbox v-model="selected.curse">诅咒</el-checkbox><el-checkbox v-model="selected.unplayable">无法打出</el-checkbox><el-checkbox v-model="selected.enabled">启用</el-checkbox></div>
            <el-divider content-position="left">主目标</el-divider>
            <div class="form-grid"><el-form-item label="选择模式"><el-select v-model="selected.selectionMode"><el-option v-for="v in bootstrap?.options.selectionModes" :key="v" :value="v" /></el-select></el-form-item><el-form-item label="距离"><el-input-number v-model="selected.targetRange" :min="0" /></el-form-item><el-form-item label="阵营"><el-select v-model="selected.teamFilter"><el-option v-for="v in bootstrap?.options.teamFilters" :key="v" :value="v" /></el-select></el-form-item><el-form-item label="生死"><el-select v-model="selected.lifeStateFilter"><el-option v-for="v in bootstrap?.options.lifeStates" :key="v" :value="v" /></el-select></el-form-item></div>
            <el-checkbox v-model="selected.requiresLineOfSight">需要视线</el-checkbox><el-checkbox v-model="selected.allowSelf">允许自己</el-checkbox>
          </el-form>
        </section>

        <section class="behavior-panel">
          <el-tabs v-model="activeTab" class="behavior-tabs">
            <el-tab-pane label="积木" name="blocks">
              <BlocklyCanvas
                :nodes="selected.behaviorNodes"
                :owner-id="selected.cardId"
                :selected-id="selectedNodeId"
                :options="bootstrap?.options"
                @update:nodes="setBehaviorNodes"
                @update:selected-id="selectedNodeId = $event"
              />
            </el-tab-pane>
            <el-tab-pane label="源码" name="source">
              <div class="source-toolbar">
                <el-radio-group v-model="sourceKind" size="small">
                  <el-radio-button value="behavior">行为脚本</el-radio-button>
                  <el-radio-button value="card">整卡 JSON</el-radio-button>
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
        </section>
      </el-main>
      <el-main v-else><el-empty description="数据库中没有卡牌，请点击新建" /></el-main>
    </el-container>

    <PackHub
      v-else
      ref="packHub"
      :section="section"
      :payload="bootstrap"
      :issues="issues"
      @save-status="persistRecord(api.saveStatus, $event)"
      @delete-status="persistRecord((id) => api.deleteStatus(id), $event)"
      @save-class="persistRecord(api.saveClass, $event)"
      @delete-class="persistRecord((id) => api.deleteClass(id), $event)"
      @save-character="persistRecord(api.saveCharacter, $event)"
      @delete-character="persistRecord((id) => api.deleteCharacter(id), $event)"
      @save-equipment="persistRecord(api.saveEquipment, $event)"
      @delete-equipment="persistRecord((id) => api.deleteEquipment(id), $event)"
      @save-pool="persistRecord(api.savePool, $event)"
      @delete-pool="persistRecord((id) => api.deletePool(id), $event)"
      @save-deck="persistRecord(api.saveDeck, $event)"
      @delete-deck="persistRecord((id) => api.deleteDeck(id), $event)"
      @save-rarity="persistRecord(api.saveRarity, $event)"
      @delete-rarity="persistRecord((id) => api.deleteRarity(id), $event)"
      @save-asset-meta="persistRecord(api.saveAssetMeta, $event)"
      @delete-asset="persistRecord((id) => api.deleteAsset(id), $event)"
      @save-game-settings="persistRecord(api.saveGameSettings, $event)"
    />

    <el-footer class="statusbar"><span v-if="dirty" class="dirty">● 未保存</span><span>{{ status }}</span></el-footer>
  </el-container>
</template>
