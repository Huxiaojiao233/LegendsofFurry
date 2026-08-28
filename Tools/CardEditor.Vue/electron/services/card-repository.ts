import { DatabaseSync } from 'node:sqlite'
import type {
  AssetRecord, BehaviorNodeRow, CardEditorRecord, CharacterRecord, ClassRecord,
  DeckRecord, EquipmentRecord, GameSettingsRecord, PoolRecord, RarityRecord, StatusRecord,
} from '../contracts.js'

type Row = Record<string, unknown>

/** 把 SQLite 的整数布尔字段转换为 TypeScript 布尔值。 */
function bool(value: unknown): boolean {
  return Number(value) !== 0
}

/** 将行为节点按父节点优先进行拓扑排序，满足 SQLite 自引用外键。 */
function orderNodesForInsert(nodes: BehaviorNodeRow[]): BehaviorNodeRow[] {
  const pending = [...nodes].sort((a, b) => a.sortOrder - b.sortOrder || a.nodeId.localeCompare(b.nodeId))
  const ordered: BehaviorNodeRow[] = []
  const inserted = new Set<string>()
  while (pending.length > 0) {
    const ready = pending.filter((node) => !node.parentNodeId || inserted.has(node.parentNodeId))
    if (ready.length === 0) throw new Error('行为图包含循环父子关系或引用了不存在的父节点。')
    for (const node of ready) {
      ordered.push(node)
      inserted.add(node.nodeId)
      pending.splice(pending.indexOf(node), 1)
    }
  }
  return ordered
}

/** 直接访问现有内容 SQLite，并提供卡牌聚合级读写事务。 */
export class CardRepository {
  private readonly database: DatabaseSync

  /** 打开数据库并启用外键、WAL 与忙等待，保持与原 .NET 仓储一致。 */
  constructor(private readonly databasePath: string) {
    this.database = new DatabaseSync(databasePath)
    this.database.exec('PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 10000;')
  }

  /** 关闭数据库句柄，供应用退出和测试清理。 */
  close(): void {
    this.database.close()
  }

  /** 读取全部卡牌及其标签、卡池、测试牌库关系和完整行为图。 */
  loadCards(): CardEditorRecord[] {
    const rows = this.database.prepare('SELECT * FROM cards ORDER BY sort_order, card_id').all() as Row[]
    return rows.map((row) => this.mapCard(row))
  }

  /** 将一张数据库主记录映射为维护工具需要的完整卡牌聚合。 */
  private mapCard(row: Row): CardEditorRecord {
    const cardId = String(row.card_id)
    const tags = (this.database.prepare(`
      SELECT m.tag_id FROM card_tag_members m WHERE m.card_id = ? ORDER BY m.tag_id
    `).all(cardId) as Row[]).map((item) => String(item.tag_id))
    const pool = this.database.prepare(`
      SELECT pool_id FROM card_pool_members WHERE card_id = ? ORDER BY sort_order, pool_id LIMIT 1
    `).get(cardId) as Row | undefined
    const testEntry = this.database.prepare(`
      SELECT 1 AS found FROM deck_entries e JOIN decks d ON d.deck_id=e.deck_id
      WHERE e.card_id=? AND d.is_test_deck=1 LIMIT 1
    `).get(cardId) as Row | undefined
    return {
      cardId,
      displayName: String(row.display_name),
      description: String(row.description),
      artworkKey: row.artwork_key == null ? '' : String(row.artwork_key),
      rarityId: String(row.rarity_id),
      familyId: String(row.family_id),
      actionCost: Number(row.action_cost),
      manaCost: Number(row.mana_cost),
      spendAllAction: bool(row.spend_all_action),
      spendAllMana: bool(row.spend_all_mana),
      isAttack: bool(row.is_attack),
      exhaustOnPlay: bool(row.exhaust_on_play),
      temporary: bool(row.temporary),
      curse: bool(row.curse),
      unplayable: bool(row.unplayable),
      selectionMode: String(row.selection_mode),
      targetRange: Number(row.target_range),
      teamFilter: String(row.team_filter),
      lifeStateFilter: String(row.life_state_filter),
      requiresLineOfSight: bool(row.requires_line_of_sight),
      allowSelf: bool(row.allow_self),
      enabled: bool(row.enabled),
      sortOrder: Number(row.sort_order),
      rowVersion: Number(row.row_version),
      tags,
      poolId: pool ? String(pool.pool_id) : '',
      includeInTestDeck: Boolean(testEntry),
      behaviorNodes: this.loadBehaviorNodes('card', cardId),
    }
  }

  /** 读取指定所有者的行为节点，并把行为元数据投影到每一行。 */
  private loadBehaviorNodes(ownerKind: string, ownerId: string): BehaviorNodeRow[] {
    return (this.database.prepare(`
      SELECT b.behavior_id, b.trigger_key, b.priority, b.enabled,
             n.node_id, n.parent_node_id, n.branch_key, n.sort_order,
             n.node_kind, n.operation_key, n.parameters_json
      FROM behaviors b JOIN behavior_nodes n ON n.behavior_id=b.behavior_id
      WHERE b.owner_kind=? AND b.owner_id=?
      ORDER BY b.priority, b.behavior_id, n.sort_order, n.node_id
    `).all(ownerKind, ownerId) as Row[]).map((row) => ({
      behaviorId: String(row.behavior_id), triggerKey: String(row.trigger_key),
      priority: Number(row.priority), enabled: bool(row.enabled), nodeId: String(row.node_id),
      parentNodeId: row.parent_node_id == null ? '' : String(row.parent_node_id),
      branchKey: String(row.branch_key), sortOrder: Number(row.sort_order),
      nodeKind: String(row.node_kind), operationKey: String(row.operation_key),
      parametersJson: String(row.parameters_json),
    }))
  }

  /** 在一个事务中保存卡牌主记录及全部从属关系。 */
  saveCard(card: CardEditorRecord): void {
    this.database.exec('BEGIN IMMEDIATE')
    try {
      this.upsertCard(card)
      this.replaceTags(card)
      this.replacePool(card)
      this.replaceOwnerBehaviors('card', card.cardId, card.behaviorNodes)
      this.synchronizeTestDeck(card)
      this.database.exec('COMMIT')
    } catch (error) {
      this.database.exec('ROLLBACK')
      throw error
    }
  }

  /** 新增或更新卡牌主表字段并递增数据库行版本。 */
  private upsertCard(card: CardEditorRecord): void {
    const now = new Date().toISOString()
    this.database.prepare(`
      INSERT INTO cards(card_id,display_name,description,artwork_key,rarity_id,family_id,
        action_cost,mana_cost,spend_all_action,spend_all_mana,is_attack,exhaust_on_play,
        temporary,curse,unplayable,selection_mode,target_range,team_filter,life_state_filter,
        requires_line_of_sight,allow_self,enabled,sort_order,created_at,updated_at)
      VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)
      ON CONFLICT(card_id) DO UPDATE SET display_name=excluded.display_name,description=excluded.description,
        artwork_key=excluded.artwork_key,rarity_id=excluded.rarity_id,family_id=excluded.family_id,
        action_cost=excluded.action_cost,mana_cost=excluded.mana_cost,spend_all_action=excluded.spend_all_action,
        spend_all_mana=excluded.spend_all_mana,is_attack=excluded.is_attack,exhaust_on_play=excluded.exhaust_on_play,
        temporary=excluded.temporary,curse=excluded.curse,unplayable=excluded.unplayable,
        selection_mode=excluded.selection_mode,target_range=excluded.target_range,team_filter=excluded.team_filter,
        life_state_filter=excluded.life_state_filter,requires_line_of_sight=excluded.requires_line_of_sight,
        allow_self=excluded.allow_self,enabled=excluded.enabled,sort_order=excluded.sort_order,
        row_version=cards.row_version+1,updated_at=excluded.updated_at
    `).run(
      card.cardId, card.displayName, card.description, card.artworkKey || null, card.rarityId, card.familyId,
      card.actionCost, card.manaCost, Number(card.spendAllAction), Number(card.spendAllMana), Number(card.isAttack),
      Number(card.exhaustOnPlay), Number(card.temporary), Number(card.curse), Number(card.unplayable),
      card.selectionMode, card.targetRange, card.teamFilter, card.lifeStateFilter, Number(card.requiresLineOfSight),
      Number(card.allowSelf), Number(card.enabled), card.sortOrder, now, now,
    )
  }

  /** 用界面中的标签集合替换卡牌现有标签关系。 */
  private replaceTags(card: CardEditorRecord): void {
    this.database.prepare('DELETE FROM card_tag_members WHERE card_id=?').run(card.cardId)
    for (const tag of [...new Set(card.tags.map((value) => value.trim()).filter(Boolean))].sort()) {
      this.database.prepare('INSERT OR IGNORE INTO card_tags(tag_id,display_name) VALUES(?,?)').run(tag, tag)
      this.database.prepare('INSERT INTO card_tag_members(card_id,tag_id) VALUES(?,?)').run(card.cardId, tag)
    }
  }

  /** 自动登记卡池并用当前单一卡池选择替换旧成员关系。 */
  private replacePool(card: CardEditorRecord): void {
    this.database.prepare('DELETE FROM card_pool_members WHERE card_id=?').run(card.cardId)
    if (!card.poolId.trim()) return
    const pool = card.poolId.trim()
    this.database.prepare('INSERT OR IGNORE INTO card_pools(pool_id,display_name) VALUES(?,?)').run(pool, pool)
    this.database.prepare('INSERT INTO card_pool_members(card_id,pool_id,weight,sort_order) VALUES(?,?,1,0)').run(card.cardId, pool)
  }

  /** 删除所有者旧行为后按行为分组写入完整行为图。 */
  replaceOwnerBehaviors(ownerKind: string, ownerId: string, nodes: BehaviorNodeRow[]): void {
    this.database.prepare(`DELETE FROM behaviors WHERE owner_kind=? AND owner_id=?`).run(ownerKind, ownerId)
    const behaviorIds = [...new Set(nodes.map((node) => node.behaviorId))].sort()
    for (const behaviorId of behaviorIds) {
      const group = nodes.filter((node) => node.behaviorId === behaviorId)
      const first = group[0]
      this.database.prepare(`INSERT INTO behaviors(behavior_id,owner_kind,owner_id,trigger_key,priority,enabled)
        VALUES(?,?,?,?,?,?)`).run(behaviorId, ownerKind, ownerId, first.triggerKey, first.priority, Number(first.enabled))
      const insert = this.database.prepare(`INSERT INTO behavior_nodes(
        node_id,behavior_id,parent_node_id,branch_key,sort_order,node_kind,operation_key,parameters_json)
        VALUES(?,?,?,?,?,?,?,?)`)
      for (const node of orderNodesForInsert(group)) {
        insert.run(node.nodeId, behaviorId, node.parentNodeId || null, node.branchKey, node.sortOrder,
          node.nodeKind, node.operationKey, node.parametersJson)
      }
    }
  }

  /** 同步卡牌是否属于策划测试牌库。 */
  private synchronizeTestDeck(card: CardEditorRecord): void {
    this.database.prepare(`INSERT OR IGNORE INTO decks(deck_id,display_name,is_test_deck,enabled)
      VALUES('planner_test','策划测试牌库',1,1)`).run()
    this.database.prepare(`DELETE FROM deck_entries WHERE deck_id='planner_test' AND card_id=?`).run(card.cardId)
    if (card.includeInTestDeck) {
      const count = this.database.prepare(`SELECT COUNT(*) AS value FROM deck_entries WHERE deck_id='planner_test'`).get() as Row
      this.database.prepare(`INSERT INTO deck_entries(deck_id,card_id,amount,sort_order)
        VALUES('planner_test',?,1,?)`).run(card.cardId, Number(count.value) + 1)
    }
  }

  /** 删除指定卡牌；牌库或其他引用存在时由 SQLite 外键阻止。 */
  deleteCard(cardId: string): boolean {
    return Number(this.database.prepare('DELETE FROM cards WHERE card_id=?').run(cardId).changes) > 0
  }

  /** 新增或更新一条受管资源记录。 */
  saveAsset(assetKey: string, relativePath: string, sha256: string): void {
    this.database.prepare(`INSERT INTO assets(asset_key,asset_kind,relative_path,sha256)
      VALUES(?,'artwork',?,?) ON CONFLICT(asset_key) DO UPDATE SET
      asset_kind='artwork',relative_path=excluded.relative_path,sha256=excluded.sha256`)
      .run(assetKey, relativePath, sha256)
  }

  /** 返回资源 Key 对应的相对路径，缺失时返回 undefined。 */
  getAssetPath(assetKey: string): string | undefined {
    const row = this.database.prepare('SELECT relative_path FROM assets WHERE asset_key=?').get(assetKey) as Row | undefined
    return row ? String(row.relative_path) : undefined
  }

  /** 读取全部状态及其行为图。 */
  loadStatuses(): StatusRecord[] {
    return (this.database.prepare('SELECT * FROM statuses ORDER BY status_id').all() as Row[]).map((row) => ({
      statusId: String(row.status_id), displayName: String(row.display_name), description: String(row.description ?? ''),
      category: String(row.category ?? 'neutral'), maximumStacks: Number(row.maximum_stacks ?? 0),
      stackingPolicy: String(row.stacking_policy ?? 'add'), durationPolicy: String(row.duration_policy ?? 'none'),
      enabled: bool(row.enabled), behaviorNodes: this.loadBehaviorNodes('status', String(row.status_id)),
    }))
  }

  /** 事务保存状态主记录与行为图。 */
  saveStatus(record: StatusRecord): void {
    this.database.exec('BEGIN IMMEDIATE')
    try {
      this.database.prepare(`INSERT INTO statuses(status_id,display_name,description,category,maximum_stacks,stacking_policy,duration_policy,enabled)
        VALUES(?,?,?,?,?,?,?,?) ON CONFLICT(status_id) DO UPDATE SET display_name=excluded.display_name,
        description=excluded.description, category=excluded.category, maximum_stacks=excluded.maximum_stacks,
        stacking_policy=excluded.stacking_policy, duration_policy=excluded.duration_policy, enabled=excluded.enabled`)
        .run(record.statusId, record.displayName, record.description, record.category, record.maximumStacks,
          record.stackingPolicy, record.durationPolicy, Number(record.enabled))
      this.replaceOwnerBehaviors('status', record.statusId, record.behaviorNodes)
      this.database.exec('COMMIT')
    } catch (error) {
      this.database.exec('ROLLBACK')
      throw error
    }
  }

  /** 删除状态及其行为；仍被卡牌引用时由策划自行保证发布校验。 */
  deleteStatus(statusId: string): boolean {
    this.database.prepare(`DELETE FROM behaviors WHERE owner_kind='status' AND owner_id=?`).run(statusId)
    return Number(this.database.prepare('DELETE FROM statuses WHERE status_id=?').run(statusId).changes) > 0
  }

  /** 读取职业资料、牌库配方、特性与行为。 */
  loadClasses(): ClassRecord[] {
    return (this.database.prepare('SELECT * FROM class_profiles ORDER BY sort_order, class_id').all() as Row[]).map((row) => {
      const payload = parseClassPayload(String(row.deck_recipe_json ?? '{}'))
      return {
        classId: String(row.class_id), displayName: String(row.display_name), description: String(row.description ?? ''),
        initialHealth: Number(row.initial_health), initialMana: Number(row.initial_mana), maximumMana: Number(row.maximum_mana),
        deckRecipe: payload.deckRecipe, traits: payload.traits, enabled: bool(row.enabled), sortOrder: Number(row.sort_order),
        behaviorNodes: this.loadBehaviorNodes('class', String(row.class_id)),
      }
    })
  }

  /** 事务保存职业资料与行为。 */
  saveClass(record: ClassRecord): void {
    this.database.exec('BEGIN IMMEDIATE')
    try {
      this.database.prepare(`INSERT INTO class_profiles(class_id,display_name,description,initial_health,initial_mana,maximum_mana,deck_recipe_json,enabled,sort_order)
        VALUES(?,?,?,?,?,?,?,?,?) ON CONFLICT(class_id) DO UPDATE SET display_name=excluded.display_name,
        description=excluded.description, initial_health=excluded.initial_health, initial_mana=excluded.initial_mana,
        maximum_mana=excluded.maximum_mana, deck_recipe_json=excluded.deck_recipe_json, enabled=excluded.enabled, sort_order=excluded.sort_order`)
        .run(record.classId, record.displayName, record.description, record.initialHealth, record.initialMana, record.maximumMana,
          JSON.stringify({ DeckRecipe: record.deckRecipe, Traits: record.traits }), Number(record.enabled), record.sortOrder)
      this.replaceOwnerBehaviors('class', record.classId, record.behaviorNodes)
      this.database.exec('COMMIT')
    } catch (error) {
      this.database.exec('ROLLBACK')
      throw error
    }
  }

  /** 删除职业及其行为。 */
  deleteClass(classId: string): boolean {
    this.database.prepare(`DELETE FROM behaviors WHERE owner_kind='class' AND owner_id=?`).run(classId)
    return Number(this.database.prepare('DELETE FROM class_profiles WHERE class_id=?').run(classId).changes) > 0
  }

  /** 读取角色及其行为。 */
  loadCharacters(): CharacterRecord[] {
    return (this.database.prepare('SELECT * FROM characters ORDER BY sort_order, character_id').all() as Row[]).map((row) => ({
      characterId: String(row.character_id), displayName: String(row.display_name), description: String(row.description ?? ''),
      initialHealth: Number(row.initial_health), baseDamage: Number(row.base_damage), moveSteps: Number(row.move_steps),
      tags: parseStringArray(row.tags_json), enabled: bool(row.enabled), sortOrder: Number(row.sort_order),
      behaviorNodes: this.loadBehaviorNodes('character', String(row.character_id)),
    }))
  }

  /** 事务保存角色与行为。 */
  saveCharacter(record: CharacterRecord): void {
    this.database.exec('BEGIN IMMEDIATE')
    try {
      this.database.prepare(`INSERT INTO characters(character_id,display_name,description,initial_health,base_damage,move_steps,tags_json,enabled,sort_order)
        VALUES(?,?,?,?,?,?,?,?,?) ON CONFLICT(character_id) DO UPDATE SET display_name=excluded.display_name,
        description=excluded.description, initial_health=excluded.initial_health, base_damage=excluded.base_damage,
        move_steps=excluded.move_steps, tags_json=excluded.tags_json, enabled=excluded.enabled, sort_order=excluded.sort_order`)
        .run(record.characterId, record.displayName, record.description, record.initialHealth, record.baseDamage,
          record.moveSteps, JSON.stringify(record.tags), Number(record.enabled), record.sortOrder)
      this.replaceOwnerBehaviors('character', record.characterId, record.behaviorNodes)
      this.database.exec('COMMIT')
    } catch (error) {
      this.database.exec('ROLLBACK')
      throw error
    }
  }

  /** 删除角色及其行为。 */
  deleteCharacter(characterId: string): boolean {
    this.database.prepare(`DELETE FROM behaviors WHERE owner_kind='character' AND owner_id=?`).run(characterId)
    return Number(this.database.prepare('DELETE FROM characters WHERE character_id=?').run(characterId).changes) > 0
  }

  /** 读取装备及其行为。 */
  loadEquipment(): EquipmentRecord[] {
    return (this.database.prepare('SELECT * FROM equipment ORDER BY sort_order, equipment_id').all() as Row[]).map((row) => ({
      equipmentId: String(row.equipment_id), displayName: String(row.display_name), description: String(row.description ?? ''),
      slotKey: String(row.slot_key), cardPoolId: String(row.card_pool_id), tags: parseStringArray(row.tags_json),
      enabled: bool(row.enabled), sortOrder: Number(row.sort_order),
      behaviorNodes: this.loadBehaviorNodes('equipment', String(row.equipment_id)),
    }))
  }

  /** 事务保存装备与行为。 */
  saveEquipment(record: EquipmentRecord): void {
    this.database.exec('BEGIN IMMEDIATE')
    try {
      this.database.prepare('INSERT OR IGNORE INTO card_pools(pool_id,display_name) VALUES(?,?)').run(record.cardPoolId, record.cardPoolId)
      this.database.prepare(`INSERT INTO equipment(equipment_id,display_name,description,slot_key,card_pool_id,tags_json,enabled,sort_order)
        VALUES(?,?,?,?,?,?,?,?) ON CONFLICT(equipment_id) DO UPDATE SET display_name=excluded.display_name,
        description=excluded.description, slot_key=excluded.slot_key, card_pool_id=excluded.card_pool_id,
        tags_json=excluded.tags_json, enabled=excluded.enabled, sort_order=excluded.sort_order`)
        .run(record.equipmentId, record.displayName, record.description, record.slotKey, record.cardPoolId,
          JSON.stringify(record.tags), Number(record.enabled), record.sortOrder)
      this.replaceOwnerBehaviors('equipment', record.equipmentId, record.behaviorNodes)
      this.database.exec('COMMIT')
    } catch (error) {
      this.database.exec('ROLLBACK')
      throw error
    }
  }

  /** 删除装备及其行为。 */
  deleteEquipment(equipmentId: string): boolean {
    this.database.prepare(`DELETE FROM behaviors WHERE owner_kind='equipment' AND owner_id=?`).run(equipmentId)
    return Number(this.database.prepare('DELETE FROM equipment WHERE equipment_id=?').run(equipmentId).changes) > 0
  }

  /** 读取卡池目录。 */
  loadPools(): PoolRecord[] {
    return (this.database.prepare('SELECT * FROM card_pools ORDER BY sort_order, pool_id').all() as Row[]).map((row) => ({
      poolId: String(row.pool_id), displayName: String(row.display_name), enabled: bool(row.enabled), sortOrder: Number(row.sort_order ?? 0),
    }))
  }

  /** 保存卡池。 */
  savePool(record: PoolRecord): void {
    this.database.prepare(`INSERT INTO card_pools(pool_id,display_name,enabled,sort_order) VALUES(?,?,?,?)
      ON CONFLICT(pool_id) DO UPDATE SET display_name=excluded.display_name, enabled=excluded.enabled, sort_order=excluded.sort_order`)
      .run(record.poolId, record.displayName, Number(record.enabled), record.sortOrder)
  }

  /** 删除卡池；仍有卡牌或装备引用时由外键阻止。 */
  deletePool(poolId: string): boolean {
    return Number(this.database.prepare('DELETE FROM card_pools WHERE pool_id=?').run(poolId).changes) > 0
  }

  /** 读取固定牌库及条目。 */
  loadDecks(): DeckRecord[] {
    return (this.database.prepare('SELECT * FROM decks ORDER BY deck_id').all() as Row[]).map((row) => {
      const deckId = String(row.deck_id)
      const entries = (this.database.prepare('SELECT card_id, amount, sort_order FROM deck_entries WHERE deck_id=? ORDER BY sort_order, card_id').all(deckId) as Row[])
        .map((entry) => ({ cardId: String(entry.card_id), amount: Number(entry.amount), sortOrder: Number(entry.sort_order) }))
      return {
        deckId, displayName: String(row.display_name), isTestDeck: bool(row.is_test_deck), enabled: bool(row.enabled), entries,
      }
    })
  }

  /** 事务替换牌库条目。 */
  saveDeck(record: DeckRecord): void {
    this.database.exec('BEGIN IMMEDIATE')
    try {
      this.database.prepare(`INSERT INTO decks(deck_id,display_name,is_test_deck,enabled) VALUES(?,?,?,?)
        ON CONFLICT(deck_id) DO UPDATE SET display_name=excluded.display_name, is_test_deck=excluded.is_test_deck, enabled=excluded.enabled`)
        .run(record.deckId, record.displayName, Number(record.isTestDeck), Number(record.enabled))
      this.database.prepare('DELETE FROM deck_entries WHERE deck_id=?').run(record.deckId)
      for (const entry of record.entries) {
        this.database.prepare('INSERT INTO deck_entries(deck_id,card_id,amount,sort_order) VALUES(?,?,?,?)')
          .run(record.deckId, entry.cardId, entry.amount, entry.sortOrder)
      }
      this.database.exec('COMMIT')
    } catch (error) {
      this.database.exec('ROLLBACK')
      throw error
    }
  }

  /** 删除牌库（条目级联删除）。 */
  deleteDeck(deckId: string): boolean {
    return Number(this.database.prepare('DELETE FROM decks WHERE deck_id=?').run(deckId).changes) > 0
  }

  /** 读取稀有度表。 */
  loadRarities(): RarityRecord[] {
    return (this.database.prepare('SELECT * FROM rarities ORDER BY sort_order, rarity_id').all() as Row[]).map((row) => ({
      rarityId: String(row.rarity_id), displayName: String(row.display_name), colorHex: String(row.color_hex),
      defaultWeight: Number(row.default_weight), sortOrder: Number(row.sort_order),
    }))
  }

  /** 保存稀有度。 */
  saveRarity(record: RarityRecord): void {
    this.database.prepare(`INSERT INTO rarities(rarity_id,display_name,color_hex,default_weight,sort_order) VALUES(?,?,?,?,?)
      ON CONFLICT(rarity_id) DO UPDATE SET display_name=excluded.display_name, color_hex=excluded.color_hex,
      default_weight=excluded.default_weight, sort_order=excluded.sort_order`)
      .run(record.rarityId, record.displayName, record.colorHex, record.defaultWeight, record.sortOrder)
  }

  /** 删除稀有度；仍有卡牌引用时由外键阻止。 */
  deleteRarity(rarityId: string): boolean {
    return Number(this.database.prepare('DELETE FROM rarities WHERE rarity_id=?').run(rarityId).changes) > 0
  }

  /** 读取受管资源目录。 */
  loadAssets(): AssetRecord[] {
    return (this.database.prepare('SELECT * FROM assets ORDER BY asset_key').all() as Row[]).map((row) => ({
      assetKey: String(row.asset_key), assetKind: String(row.asset_kind), relativePath: String(row.relative_path),
      sha256: row.sha256 == null ? '' : String(row.sha256),
    }))
  }

  /** 保存资源元数据（不复制文件）。 */
  saveAssetMeta(record: AssetRecord): void {
    this.database.prepare(`INSERT INTO assets(asset_key,asset_kind,relative_path,sha256) VALUES(?,?,?,?)
      ON CONFLICT(asset_key) DO UPDATE SET asset_kind=excluded.asset_kind, relative_path=excluded.relative_path, sha256=excluded.sha256`)
      .run(record.assetKey, record.assetKind, record.relativePath, record.sha256 || null)
  }

  /** 删除资源记录；仍被卡牌引用时由外键阻止。 */
  deleteAsset(assetKey: string): boolean {
    return Number(this.database.prepare('DELETE FROM assets WHERE asset_key=?').run(assetKey).changes) > 0
  }

  /** 读取基础战斗参数。 */
  loadGameSettings(): GameSettingsRecord {
    const values = new Map<string, string>()
    for (const row of this.database.prepare('SELECT setting_key, value_json FROM game_settings').all() as Row[]) {
      values.set(String(row.setting_key), String(row.value_json))
    }
    const num = (key: string, fallback: number): number => {
      const raw = values.get(key)
      const parsed = raw ? Number(raw) : NaN
      return Number.isFinite(parsed) ? parsed : fallback
    }
    return {
      handLimit: num('hand_limit', 10), startingHandSize: num('starting_hand_size', 5), drawPerTurn: num('draw_per_turn', 5),
      baseActionPoints: num('base_action_points', 3), baseMoveSteps: num('base_move_steps', 2),
      playerCharacterId: values.get('player_character_id') ?? '', enemyCharacterId: values.get('enemy_character_id') ?? '',
    }
  }

  /** 覆盖保存基础战斗参数。 */
  saveGameSettings(record: GameSettingsRecord): void {
    const put = (key: string, value: string | number): void => {
      this.database.prepare(`INSERT INTO game_settings(setting_key,value_json) VALUES(?,?)
        ON CONFLICT(setting_key) DO UPDATE SET value_json=excluded.value_json`).run(key, String(value))
    }
    put('hand_limit', record.handLimit)
    put('starting_hand_size', record.startingHandSize)
    put('draw_per_turn', record.drawPerTurn)
    put('base_action_points', record.baseActionPoints)
    put('base_move_steps', record.baseMoveSteps)
    put('player_character_id', record.playerCharacterId)
    put('enemy_character_id', record.enemyCharacterId)
  }
}

function parseStringArray(raw: unknown): string[] {
  try {
    const value = typeof raw === 'string' ? JSON.parse(raw) : raw
    return Array.isArray(value) ? value.map((item) => String(item)) : []
  } catch {
    return []
  }
}

function parseClassPayload(json: string): { deckRecipe: ClassRecord['deckRecipe']; traits: ClassRecord['traits'] } {
  try {
    const payload = JSON.parse(json) as Record<string, unknown>
    const recipe = (payload.DeckRecipe ?? payload.deckRecipe) as ClassRecord['deckRecipe'] | undefined
    const traits = (payload.Traits ?? payload.traits) as ClassRecord['traits'] | undefined
    return { deckRecipe: Array.isArray(recipe) ? recipe : [], traits: Array.isArray(traits) ? traits : [] }
  } catch {
    return { deckRecipe: [], traits: [] }
  }
}
