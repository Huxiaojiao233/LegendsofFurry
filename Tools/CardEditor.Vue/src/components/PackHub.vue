<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import type {
  AssetRecord, BootstrapPayload, CharacterRecord, ClassRecord, DeckRecord, EditorSection,
  EquipmentRecord, GameSettingsRecord, PoolRecord, RarityRecord, StatusRecord,
} from '../types'
import { cloneForIpc } from '../clone'
import BehaviorPane from './BehaviorPane.vue'

const props = defineProps<{
  section: EditorSection
  payload?: BootstrapPayload
  issues: string[]
}>()

const emit = defineEmits<{
  saveStatus: [record: StatusRecord]
  deleteStatus: [id: string]
  saveClass: [record: ClassRecord]
  deleteClass: [id: string]
  saveCharacter: [record: CharacterRecord]
  deleteCharacter: [id: string]
  saveEquipment: [record: EquipmentRecord]
  deleteEquipment: [id: string]
  savePool: [record: PoolRecord]
  deletePool: [id: string]
  saveDeck: [record: DeckRecord]
  deleteDeck: [id: string]
  saveRarity: [record: RarityRecord]
  deleteRarity: [id: string]
  saveAssetMeta: [record: AssetRecord]
  deleteAsset: [id: string]
  saveGameSettings: [record: GameSettingsRecord]
}>()

const search = ref('')
const statuses = ref<StatusRecord[]>([])
const classes = ref<ClassRecord[]>([])
const characters = ref<CharacterRecord[]>([])
const equipment = ref<EquipmentRecord[]>([])
const pools = ref<PoolRecord[]>([])
const decks = ref<DeckRecord[]>([])
const rarities = ref<RarityRecord[]>([])
const assets = ref<AssetRecord[]>([])
const settings = ref<GameSettingsRecord>({
  handLimit: 10, startingHandSize: 5, drawPerTurn: 5, baseActionPoints: 3, baseMoveSteps: 2,
  playerCharacterId: '', enemyCharacterId: '',
})
const selectedStatus = ref<StatusRecord>()
const selectedClass = ref<ClassRecord>()
const selectedCharacter = ref<CharacterRecord>()
const selectedEquipment = ref<EquipmentRecord>()
const selectedPool = ref<PoolRecord>()
const selectedDeck = ref<DeckRecord>()
const selectedRarity = ref<RarityRecord>()
const selectedAsset = ref<AssetRecord>()

watch(() => props.payload, (payload) => {
  if (!payload) return
  statuses.value = payload.statuses.map((item) => ({ ...item, isExisting: true }))
  classes.value = payload.classes.map((item) => ({ ...item, isExisting: true }))
  characters.value = payload.characters.map((item) => ({ ...item, isExisting: true }))
  equipment.value = payload.equipment.map((item) => ({ ...item, isExisting: true }))
  pools.value = payload.pools.map((item) => ({ ...item, isExisting: true }))
  decks.value = payload.decks.map((item) => ({ ...item, isExisting: true }))
  rarities.value = payload.rarityRecords.map((item) => ({ ...item, isExisting: true }))
  assets.value = payload.assets.map((item) => ({ ...item, isExisting: true }))
  settings.value = { ...payload.gameSettings }
  selectedStatus.value = statuses.value[0]
  selectedClass.value = classes.value[0]
  selectedCharacter.value = characters.value[0]
  selectedEquipment.value = equipment.value[0]
  selectedPool.value = pools.value[0]
  selectedDeck.value = decks.value[0]
  selectedRarity.value = rarities.value[0]
  selectedAsset.value = assets.value[0]
}, { immediate: true })

function match(id: string, name: string): boolean {
  const q = search.value.trim().toLowerCase()
  return !q || id.toLowerCase().includes(q) || name.toLowerCase().includes(q)
}

const filteredStatuses = computed(() => statuses.value.filter((item) => match(item.statusId, item.displayName)))
const filteredClasses = computed(() => classes.value.filter((item) => match(item.classId, item.displayName)))
const filteredCharacters = computed(() => characters.value.filter((item) => match(item.characterId, item.displayName)))
const filteredEquipment = computed(() => equipment.value.filter((item) => match(item.equipmentId, item.displayName)))
const filteredPools = computed(() => pools.value.filter((item) => match(item.poolId, item.displayName)))
const filteredDecks = computed(() => decks.value.filter((item) => match(item.deckId, item.displayName)))
const filteredRarities = computed(() => rarities.value.filter((item) => match(item.rarityId, item.displayName)))
const filteredAssets = computed(() => assets.value.filter((item) => match(item.assetKey, item.relativePath)))

function uniqueId(base: string, used: string[]): string {
  const set = new Set(used)
  if (!set.has(base)) return base
  let n = 2
  while (set.has(`${base}_${n}`)) n += 1
  return `${base}_${n}`
}

function rewriteNodes<T extends { behaviorNodes: StatusRecord['behaviorNodes'] }>(record: T, oldId: string, newId: string): T {
  const copy = cloneForIpc(record)
  copy.behaviorNodes = copy.behaviorNodes.map((node) => ({
    ...node,
    behaviorId: node.behaviorId.replaceAll(oldId, newId),
    nodeId: node.nodeId.replaceAll(oldId, newId),
    parentNodeId: node.parentNodeId.replaceAll(oldId, newId),
  }))
  return copy
}

async function saveCurrent(): Promise<void> {
  if (props.section === 'statuses' && selectedStatus.value) emit('saveStatus', cloneForIpc(selectedStatus.value))
  else if (props.section === 'classes' && selectedClass.value) emit('saveClass', cloneForIpc(selectedClass.value))
  else if (props.section === 'characters' && selectedCharacter.value) emit('saveCharacter', cloneForIpc(selectedCharacter.value))
  else if (props.section === 'equipment' && selectedEquipment.value) emit('saveEquipment', cloneForIpc(selectedEquipment.value))
  else if (props.section === 'pools' && selectedPool.value) emit('savePool', cloneForIpc(selectedPool.value))
  else if (props.section === 'decks' && selectedDeck.value) emit('saveDeck', cloneForIpc(selectedDeck.value))
  else if (props.section === 'rarities' && selectedRarity.value) emit('saveRarity', cloneForIpc(selectedRarity.value))
  else if (props.section === 'assets' && selectedAsset.value) emit('saveAssetMeta', cloneForIpc(selectedAsset.value))
  else if (props.section === 'settings') emit('saveGameSettings', cloneForIpc(settings.value))
  else ElMessage.warning('当前没有可保存的内容。')
}

function newRecord(): void {
  if (props.section === 'statuses') {
    const record: StatusRecord = {
      statusId: uniqueId('new_status', statuses.value.map((item) => item.statusId)),
      displayName: '新状态', description: '', category: 'neutral', maximumStacks: 0,
      stackingPolicy: 'add', durationPolicy: 'none', enabled: true, behaviorNodes: [], isExisting: false,
    }
    statuses.value.push(record); selectedStatus.value = record
  } else if (props.section === 'classes') {
    const record: ClassRecord = {
      classId: uniqueId('new_class', classes.value.map((item) => item.classId)),
      displayName: '新职业', description: '', initialHealth: 10, initialMana: 0, maximumMana: 10,
      deckRecipe: [], traits: [], enabled: true, sortOrder: 0, behaviorNodes: [], isExisting: false,
    }
    classes.value.push(record); selectedClass.value = record
  } else if (props.section === 'characters') {
    const record: CharacterRecord = {
      characterId: uniqueId('new_character', characters.value.map((item) => item.characterId)),
      displayName: '新角色', description: '', initialHealth: 10, baseDamage: 3, moveSteps: 2,
      tags: [], enabled: true, sortOrder: 0, behaviorNodes: [], isExisting: false,
    }
    characters.value.push(record); selectedCharacter.value = record
  } else if (props.section === 'equipment') {
    const record: EquipmentRecord = {
      equipmentId: uniqueId('new_equipment', equipment.value.map((item) => item.equipmentId)),
      displayName: '新装备', description: '', slotKey: 'weapon', cardPoolId: pools.value[0]?.poolId ?? 'sword',
      tags: [], enabled: true, sortOrder: 0, behaviorNodes: [], isExisting: false,
    }
    equipment.value.push(record); selectedEquipment.value = record
  } else if (props.section === 'pools') {
    const record: PoolRecord = { poolId: uniqueId('new_pool', pools.value.map((item) => item.poolId)), displayName: '新卡池', enabled: true, sortOrder: 0, isExisting: false }
    pools.value.push(record); selectedPool.value = record
  } else if (props.section === 'decks') {
    const record: DeckRecord = { deckId: uniqueId('new_deck', decks.value.map((item) => item.deckId)), displayName: '新牌库', isTestDeck: false, enabled: true, entries: [], isExisting: false }
    decks.value.push(record); selectedDeck.value = record
  } else if (props.section === 'rarities') {
    ElMessage.warning('稀有度一般不新建；如需新增请先确认编译器支持该 ID。')
  } else if (props.section === 'assets') {
    const record: AssetRecord = { assetKey: uniqueId('new.asset', assets.value.map((item) => item.assetKey)), assetKind: 'artwork', relativePath: 'CardArt/placeholder.png', sha256: '', isExisting: false }
    assets.value.push(record); selectedAsset.value = record
  }
}

function duplicateRecord(): void {
  if (props.section === 'statuses' && selectedStatus.value) {
    const oldId = selectedStatus.value.statusId
    const newId = uniqueId(`${oldId}_copy`, statuses.value.map((item) => item.statusId))
    const copy = rewriteNodes(cloneForIpc(selectedStatus.value), oldId, newId)
    copy.statusId = newId; copy.displayName += '（复制）'; copy.isExisting = false
    statuses.value.push(copy); selectedStatus.value = copy
  } else if (props.section === 'classes' && selectedClass.value) {
    const oldId = selectedClass.value.classId
    const newId = uniqueId(`${oldId}_copy`, classes.value.map((item) => item.classId))
    const copy = rewriteNodes(cloneForIpc(selectedClass.value), oldId, newId)
    copy.classId = newId; copy.displayName += '（复制）'; copy.isExisting = false
    classes.value.push(copy); selectedClass.value = copy
  } else if (props.section === 'characters' && selectedCharacter.value) {
    const oldId = selectedCharacter.value.characterId
    const newId = uniqueId(`${oldId}_copy`, characters.value.map((item) => item.characterId))
    const copy = rewriteNodes(cloneForIpc(selectedCharacter.value), oldId, newId)
    copy.characterId = newId; copy.displayName += '（复制）'; copy.isExisting = false
    characters.value.push(copy); selectedCharacter.value = copy
  } else if (props.section === 'equipment' && selectedEquipment.value) {
    const oldId = selectedEquipment.value.equipmentId
    const newId = uniqueId(`${oldId}_copy`, equipment.value.map((item) => item.equipmentId))
    const copy = rewriteNodes(cloneForIpc(selectedEquipment.value), oldId, newId)
    copy.equipmentId = newId; copy.displayName += '（复制）'; copy.isExisting = false
    equipment.value.push(copy); selectedEquipment.value = copy
  }
}

async function deleteCurrent(): Promise<void> {
  const confirm = async (name: string): Promise<boolean> => {
    await ElMessageBox.confirm(`确定删除“${name}”吗？`, '删除', { type: 'warning' })
    return true
  }
  try {
    if (props.section === 'statuses' && selectedStatus.value?.isExisting) {
      await confirm(selectedStatus.value.displayName); emit('deleteStatus', selectedStatus.value.statusId)
    } else if (props.section === 'classes' && selectedClass.value?.isExisting) {
      await confirm(selectedClass.value.displayName); emit('deleteClass', selectedClass.value.classId)
    } else if (props.section === 'characters' && selectedCharacter.value?.isExisting) {
      await confirm(selectedCharacter.value.displayName); emit('deleteCharacter', selectedCharacter.value.characterId)
    } else if (props.section === 'equipment' && selectedEquipment.value?.isExisting) {
      await confirm(selectedEquipment.value.displayName); emit('deleteEquipment', selectedEquipment.value.equipmentId)
    } else if (props.section === 'pools' && selectedPool.value?.isExisting) {
      await confirm(selectedPool.value.displayName); emit('deletePool', selectedPool.value.poolId)
    } else if (props.section === 'decks' && selectedDeck.value?.isExisting) {
      await confirm(selectedDeck.value.displayName); emit('deleteDeck', selectedDeck.value.deckId)
    } else if (props.section === 'rarities' && selectedRarity.value?.isExisting) {
      await confirm(selectedRarity.value.displayName); emit('deleteRarity', selectedRarity.value.rarityId)
    } else if (props.section === 'assets' && selectedAsset.value?.isExisting) {
      await confirm(selectedAsset.value.assetKey); emit('deleteAsset', selectedAsset.value.assetKey)
    }
  } catch { /* 取消删除 */ }
}

defineExpose({ saveCurrent, newRecord, duplicateRecord, deleteCurrent })
</script>

<template>
  <el-container class="workspace pack-workspace">
    <el-aside v-if="section !== 'settings'" width="280px" class="card-list-panel">
      <el-input v-model="search" clearable placeholder="搜索" class="search" />
      <el-scrollbar>
        <template v-if="section === 'statuses'">
          <div v-for="item in filteredStatuses" :key="item.statusId" class="card-list-item" :class="{ active: item === selectedStatus }" @click="selectedStatus = item">
            <strong>{{ item.displayName }}</strong><small>{{ item.statusId }} · {{ item.category }}</small>
          </div>
        </template>
        <template v-else-if="section === 'classes'">
          <div v-for="item in filteredClasses" :key="item.classId" class="card-list-item" :class="{ active: item === selectedClass }" @click="selectedClass = item">
            <strong>{{ item.displayName }}</strong><small>{{ item.classId }}</small>
          </div>
        </template>
        <template v-else-if="section === 'characters'">
          <div v-for="item in filteredCharacters" :key="item.characterId" class="card-list-item" :class="{ active: item === selectedCharacter }" @click="selectedCharacter = item">
            <strong>{{ item.displayName }}</strong><small>{{ item.characterId }}</small>
          </div>
        </template>
        <template v-else-if="section === 'equipment'">
          <div v-for="item in filteredEquipment" :key="item.equipmentId" class="card-list-item" :class="{ active: item === selectedEquipment }" @click="selectedEquipment = item">
            <strong>{{ item.displayName }}</strong><small>{{ item.equipmentId }} · {{ item.slotKey }}</small>
          </div>
        </template>
        <template v-else-if="section === 'pools'">
          <div v-for="item in filteredPools" :key="item.poolId" class="card-list-item" :class="{ active: item === selectedPool }" @click="selectedPool = item">
            <strong>{{ item.displayName }}</strong><small>{{ item.poolId }}</small>
          </div>
        </template>
        <template v-else-if="section === 'decks'">
          <div v-for="item in filteredDecks" :key="item.deckId" class="card-list-item" :class="{ active: item === selectedDeck }" @click="selectedDeck = item">
            <strong>{{ item.displayName }}</strong><small>{{ item.deckId }}</small>
          </div>
        </template>
        <template v-else-if="section === 'rarities'">
          <div v-for="item in filteredRarities" :key="item.rarityId" class="card-list-item" :class="{ active: item === selectedRarity }" @click="selectedRarity = item">
            <strong>{{ item.displayName }}</strong><small>{{ item.rarityId }}</small>
          </div>
        </template>
        <template v-else-if="section === 'assets'">
          <div v-for="item in filteredAssets" :key="item.assetKey" class="card-list-item" :class="{ active: item === selectedAsset }" @click="selectedAsset = item">
            <strong>{{ item.assetKey }}</strong><small>{{ item.relativePath }}</small>
          </div>
        </template>
      </el-scrollbar>
    </el-aside>

    <el-main class="editor-main pack-main" v-if="section === 'statuses' && selectedStatus">
      <section class="basic-panel">
        <div class="panel-title">状态</div>
        <el-form label-position="top">
          <el-form-item label="稳定 ID"><el-input v-model="selectedStatus.statusId" :disabled="selectedStatus.isExisting" /></el-form-item>
          <el-form-item label="名称"><el-input v-model="selectedStatus.displayName" /></el-form-item>
          <el-form-item label="描述"><el-input v-model="selectedStatus.description" type="textarea" :rows="4" /></el-form-item>
          <el-form-item label="类别"><el-select v-model="selectedStatus.category"><el-option v-for="v in payload?.options.statusCategories" :key="v" :value="v" /></el-select></el-form-item>
          <el-form-item label="最大层数（0 表示不限制）"><el-input-number v-model="selectedStatus.maximumStacks" :min="0" /></el-form-item>
          <el-form-item label="叠层规则"><el-select v-model="selectedStatus.stackingPolicy"><el-option v-for="v in payload?.options.stackingPolicies" :key="v" :value="v" /></el-select></el-form-item>
          <el-form-item label="持续规则"><el-select v-model="selectedStatus.durationPolicy"><el-option v-for="v in payload?.options.durationPolicies" :key="v" :value="v" /></el-select></el-form-item>
          <el-checkbox v-model="selectedStatus.enabled">启用</el-checkbox>
        </el-form>
      </section>
      <section class="behavior-panel">
        <BehaviorPane :owner-id="selectedStatus.statusId" :nodes="selectedStatus.behaviorNodes" :options="payload?.options" :issues="issues" @update:nodes="selectedStatus.behaviorNodes = $event" />
      </section>
    </el-main>

    <el-main class="editor-main pack-main" v-else-if="section === 'classes' && selectedClass">
      <section class="basic-panel">
        <div class="panel-title">职业</div>
        <el-form label-position="top">
          <el-form-item label="稳定 ID"><el-input v-model="selectedClass.classId" :disabled="selectedClass.isExisting" /></el-form-item>
          <el-form-item label="名称"><el-input v-model="selectedClass.displayName" /></el-form-item>
          <el-form-item label="描述"><el-input v-model="selectedClass.description" type="textarea" :rows="3" /></el-form-item>
          <div class="form-grid">
            <el-form-item label="初始生命"><el-input-number v-model="selectedClass.initialHealth" :min="1" /></el-form-item>
            <el-form-item label="初始魔力"><el-input-number v-model="selectedClass.initialMana" :min="0" /></el-form-item>
            <el-form-item label="魔力上限"><el-input-number v-model="selectedClass.maximumMana" :min="0" /></el-form-item>
            <el-form-item label="排序"><el-input-number v-model="selectedClass.sortOrder" /></el-form-item>
          </div>
          <el-checkbox v-model="selectedClass.enabled">启用</el-checkbox>
          <el-divider>起始牌库配方</el-divider>
          <p class="inspector-kind">开局牌库怎么凑：每一行要么从某个卡池抽若干张，要么指定一张固定卡。</p>
          <el-button size="small" @click="selectedClass.deckRecipe.push({ poolId: '', amount: 1, fixedCardId: '', sortOrder: selectedClass.deckRecipe.length })">加一行</el-button>
          <div v-for="(row, index) in selectedClass.deckRecipe" :key="index" class="recipe-row">
            <el-input v-model="row.poolId" placeholder="卡池 ID（随机抽）" />
            <el-input-number v-model="row.amount" :min="0" />
            <el-input v-model="row.fixedCardId" placeholder="固定卡 ID（填了就抽这张，可空）" />
            <el-button text type="danger" @click="selectedClass.deckRecipe.splice(index, 1)">删</el-button>
          </div>
          <p class="palette-group">张数：这一行放进牌库几张。固定卡 ID 有值时，张数张都是这张卡；为空则从卡池随机。</p>
          <el-divider>特性</el-divider>
          <p class="inspector-kind">给程序看的键值，不是给玩家看的描述。键是特性名（如 artwork），值是对应配置。</p>
          <el-button size="small" @click="selectedClass.traits.push({ key: '', value: '' })">加特性</el-button>
          <div v-for="(row, index) in selectedClass.traits" :key="'t'+index" class="recipe-row">
            <el-input v-model="row.key" placeholder="特性键（程序名）" />
            <el-input v-model="row.value" placeholder="特性值" />
            <el-button text type="danger" @click="selectedClass.traits.splice(index, 1)">删</el-button>
          </div>
        </el-form>
      </section>
      <section class="behavior-panel">
        <BehaviorPane :owner-id="selectedClass.classId" :nodes="selectedClass.behaviorNodes" :options="payload?.options" :issues="issues" @update:nodes="selectedClass.behaviorNodes = $event" />
      </section>
    </el-main>

    <el-main class="editor-main pack-main" v-else-if="section === 'characters' && selectedCharacter">
      <section class="basic-panel">
        <div class="panel-title">角色</div>
        <el-form label-position="top">
          <el-form-item label="稳定 ID"><el-input v-model="selectedCharacter.characterId" :disabled="selectedCharacter.isExisting" /></el-form-item>
          <el-form-item label="名称"><el-input v-model="selectedCharacter.displayName" /></el-form-item>
          <el-form-item label="描述"><el-input v-model="selectedCharacter.description" type="textarea" :rows="3" /></el-form-item>
          <div class="form-grid">
            <el-form-item label="生命"><el-input-number v-model="selectedCharacter.initialHealth" :min="1" /></el-form-item>
            <el-form-item label="基础伤害"><el-input-number v-model="selectedCharacter.baseDamage" :min="0" /></el-form-item>
            <el-form-item label="移动步数"><el-input-number v-model="selectedCharacter.moveSteps" :min="1" /></el-form-item>
            <el-form-item label="排序"><el-input-number v-model="selectedCharacter.sortOrder" /></el-form-item>
          </div>
          <el-form-item label="标签"><el-select v-model="selectedCharacter.tags" multiple filterable allow-create default-first-option /></el-form-item>
          <el-checkbox v-model="selectedCharacter.enabled">启用</el-checkbox>
        </el-form>
      </section>
      <section class="behavior-panel">
        <BehaviorPane :owner-id="selectedCharacter.characterId" :nodes="selectedCharacter.behaviorNodes" :options="payload?.options" :issues="issues" @update:nodes="selectedCharacter.behaviorNodes = $event" />
      </section>
    </el-main>

    <el-main class="editor-main pack-main" v-else-if="section === 'equipment' && selectedEquipment">
      <section class="basic-panel">
        <div class="panel-title">装备</div>
        <el-form label-position="top">
          <el-form-item label="稳定 ID"><el-input v-model="selectedEquipment.equipmentId" :disabled="selectedEquipment.isExisting" /></el-form-item>
          <el-form-item label="名称"><el-input v-model="selectedEquipment.displayName" /></el-form-item>
          <el-form-item label="描述"><el-input v-model="selectedEquipment.description" type="textarea" :rows="3" /></el-form-item>
          <el-form-item label="槽位"><el-select v-model="selectedEquipment.slotKey"><el-option v-for="v in payload?.options.equipmentSlots" :key="v" :value="v" /></el-select></el-form-item>
          <el-form-item label="关联卡池"><el-input v-model="selectedEquipment.cardPoolId" /></el-form-item>
          <el-form-item label="标签"><el-select v-model="selectedEquipment.tags" multiple filterable allow-create default-first-option /></el-form-item>
          <el-form-item label="排序"><el-input-number v-model="selectedEquipment.sortOrder" /></el-form-item>
          <el-checkbox v-model="selectedEquipment.enabled">启用</el-checkbox>
        </el-form>
      </section>
      <section class="behavior-panel">
        <BehaviorPane :owner-id="selectedEquipment.equipmentId" :nodes="selectedEquipment.behaviorNodes" :options="payload?.options" :issues="issues" @update:nodes="selectedEquipment.behaviorNodes = $event" />
      </section>
    </el-main>

    <el-main class="basic-panel" v-else-if="section === 'pools' && selectedPool">
      <div class="panel-title">卡池</div>
      <el-form label-position="top" style="max-width: 520px">
        <el-form-item label="稳定 ID"><el-input v-model="selectedPool.poolId" :disabled="selectedPool.isExisting" /></el-form-item>
        <el-form-item label="名称"><el-input v-model="selectedPool.displayName" /></el-form-item>
        <el-form-item label="排序"><el-input-number v-model="selectedPool.sortOrder" /></el-form-item>
        <el-checkbox v-model="selectedPool.enabled">启用</el-checkbox>
      </el-form>
    </el-main>

    <el-main class="basic-panel" v-else-if="section === 'decks' && selectedDeck">
      <div class="panel-title">牌库</div>
      <el-form label-position="top" style="max-width: 720px">
        <el-form-item label="稳定 ID"><el-input v-model="selectedDeck.deckId" :disabled="selectedDeck.isExisting" /></el-form-item>
        <el-form-item label="名称"><el-input v-model="selectedDeck.displayName" /></el-form-item>
        <el-checkbox v-model="selectedDeck.isTestDeck">策划测试牌库</el-checkbox>
        <el-checkbox v-model="selectedDeck.enabled">启用</el-checkbox>
        <el-divider>条目</el-divider>
        <el-button size="small" @click="selectedDeck.entries.push({ cardId: '', amount: 1, sortOrder: selectedDeck.entries.length })">加卡</el-button>
        <div v-for="(row, index) in selectedDeck.entries" :key="index" class="recipe-row">
          <el-input v-model="row.cardId" placeholder="卡牌 ID" />
          <el-input-number v-model="row.amount" :min="1" />
          <el-button text type="danger" @click="selectedDeck.entries.splice(index, 1)">删</el-button>
        </div>
      </el-form>
    </el-main>

    <el-main class="basic-panel" v-else-if="section === 'rarities' && selectedRarity">
      <div class="panel-title">稀有度</div>
      <el-form label-position="top" style="max-width: 520px">
        <el-form-item label="稳定 ID"><el-input v-model="selectedRarity.rarityId" :disabled="selectedRarity.isExisting" /></el-form-item>
        <el-form-item label="名称"><el-input v-model="selectedRarity.displayName" /></el-form-item>
        <el-form-item label="颜色"><el-input v-model="selectedRarity.colorHex" /></el-form-item>
        <el-form-item label="默认权重"><el-input-number v-model="selectedRarity.defaultWeight" :step="0.05" :min="0" :max="1" /></el-form-item>
        <el-form-item label="排序"><el-input-number v-model="selectedRarity.sortOrder" /></el-form-item>
      </el-form>
    </el-main>

    <el-main class="basic-panel" v-else-if="section === 'assets' && selectedAsset">
      <div class="panel-title">资源</div>
      <el-form label-position="top" style="max-width: 640px">
        <el-form-item label="资源 Key"><el-input v-model="selectedAsset.assetKey" :disabled="selectedAsset.isExisting" /></el-form-item>
        <el-form-item label="类型"><el-input v-model="selectedAsset.assetKind" /></el-form-item>
        <el-form-item label="相对路径"><el-input v-model="selectedAsset.relativePath" /></el-form-item>
        <el-form-item label="SHA-256"><el-input v-model="selectedAsset.sha256" /></el-form-item>
      </el-form>
    </el-main>

    <el-main class="basic-panel" v-else-if="section === 'settings'">
      <div class="panel-title">战斗规则</div>
      <el-form label-position="top" class="form-grid" style="max-width: 720px">
        <el-form-item label="手牌上限"><el-input-number v-model="settings.handLimit" :min="1" /></el-form-item>
        <el-form-item label="起始手牌"><el-input-number v-model="settings.startingHandSize" :min="0" /></el-form-item>
        <el-form-item label="每回合抽牌"><el-input-number v-model="settings.drawPerTurn" :min="0" /></el-form-item>
        <el-form-item label="基础行动力"><el-input-number v-model="settings.baseActionPoints" :min="0" /></el-form-item>
        <el-form-item label="基础移动步数"><el-input-number v-model="settings.baseMoveSteps" :min="1" /></el-form-item>
        <el-form-item label="玩家角色 ID"><el-input v-model="settings.playerCharacterId" /></el-form-item>
        <el-form-item label="敌人角色 ID"><el-input v-model="settings.enemyCharacterId" /></el-form-item>
      </el-form>
    </el-main>
    <el-main v-else><el-empty description="没有可编辑的条目，请新建" /></el-main>
  </el-container>
</template>
