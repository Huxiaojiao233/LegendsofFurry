import fs from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import { afterEach, describe, expect, it } from 'vitest'
import { DatabaseSync } from 'node:sqlite'
import type { CardEditorRecord, StatusRecord } from '../contracts.js'
import { CardRepository } from './card-repository.js'
import { ArtworkService } from './artwork-service.js'

const temporaryDirectories: string[] = []

/** 创建包含仓储测试所需完整外键关系的临时 SQLite 数据库。 */
function createDatabase(): { root: string; databasePath: string } {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'lof-card-editor-'))
  temporaryDirectories.push(root)
  const content = path.join(root, 'ContentSource')
  fs.mkdirSync(content, { recursive: true })
  const databasePath = path.join(content, 'lof-content.db')
  const database = new DatabaseSync(databasePath)
  database.exec(`
    PRAGMA foreign_keys=ON;
    CREATE TABLE rarities(rarity_id TEXT PRIMARY KEY,display_name TEXT,color_hex TEXT,default_weight REAL,sort_order INTEGER);
    CREATE TABLE assets(asset_key TEXT PRIMARY KEY,asset_kind TEXT NOT NULL,relative_path TEXT NOT NULL,sha256 TEXT);
    CREATE TABLE card_pools(pool_id TEXT PRIMARY KEY,display_name TEXT NOT NULL,enabled INTEGER DEFAULT 1,sort_order INTEGER DEFAULT 0);
    CREATE TABLE cards(card_id TEXT PRIMARY KEY,display_name TEXT NOT NULL,description TEXT NOT NULL,artwork_key TEXT REFERENCES assets(asset_key) ON DELETE RESTRICT,rarity_id TEXT NOT NULL REFERENCES rarities(rarity_id),family_id TEXT NOT NULL,action_cost INTEGER NOT NULL,mana_cost INTEGER NOT NULL,spend_all_action INTEGER NOT NULL,spend_all_mana INTEGER NOT NULL,is_attack INTEGER NOT NULL,exhaust_on_play INTEGER NOT NULL,temporary INTEGER NOT NULL,curse INTEGER NOT NULL,unplayable INTEGER NOT NULL,selection_mode TEXT NOT NULL,target_range INTEGER NOT NULL,team_filter TEXT NOT NULL,life_state_filter TEXT NOT NULL,requires_line_of_sight INTEGER NOT NULL,allow_self INTEGER NOT NULL,enabled INTEGER NOT NULL,sort_order INTEGER NOT NULL,row_version INTEGER NOT NULL DEFAULT 1,created_at TEXT NOT NULL,updated_at TEXT NOT NULL);
    CREATE TABLE card_tags(tag_id TEXT PRIMARY KEY,display_name TEXT NOT NULL);
    CREATE TABLE card_tag_members(card_id TEXT REFERENCES cards(card_id) ON DELETE CASCADE,tag_id TEXT REFERENCES card_tags(tag_id) ON DELETE RESTRICT,PRIMARY KEY(card_id,tag_id));
    CREATE TABLE card_pool_members(card_id TEXT REFERENCES cards(card_id) ON DELETE CASCADE,pool_id TEXT REFERENCES card_pools(pool_id) ON DELETE RESTRICT,weight REAL,sort_order INTEGER,PRIMARY KEY(card_id,pool_id));
    CREATE TABLE behaviors(behavior_id TEXT PRIMARY KEY,owner_kind TEXT,owner_id TEXT,trigger_key TEXT,priority INTEGER,enabled INTEGER);
    CREATE TABLE behavior_nodes(node_id TEXT PRIMARY KEY,behavior_id TEXT REFERENCES behaviors(behavior_id) ON DELETE CASCADE,parent_node_id TEXT REFERENCES behavior_nodes(node_id) ON DELETE CASCADE,branch_key TEXT,sort_order INTEGER,node_kind TEXT,operation_key TEXT,parameters_json TEXT);
    CREATE TABLE decks(deck_id TEXT PRIMARY KEY,display_name TEXT,is_test_deck INTEGER,enabled INTEGER);
    CREATE TABLE deck_entries(deck_id TEXT REFERENCES decks(deck_id) ON DELETE CASCADE,card_id TEXT REFERENCES cards(card_id) ON DELETE RESTRICT,amount INTEGER,sort_order INTEGER,PRIMARY KEY(deck_id,card_id));
    CREATE TABLE statuses(status_id TEXT PRIMARY KEY,display_name TEXT,description TEXT,category TEXT,maximum_stacks INTEGER,stacking_policy TEXT,duration_policy TEXT,enabled INTEGER);
    CREATE TABLE class_profiles(class_id TEXT PRIMARY KEY,display_name TEXT,description TEXT,initial_health INTEGER,initial_mana INTEGER,maximum_mana INTEGER,deck_recipe_json TEXT,enabled INTEGER,sort_order INTEGER);
    CREATE TABLE characters(character_id TEXT PRIMARY KEY,display_name TEXT,description TEXT,initial_health INTEGER,base_damage INTEGER,move_steps INTEGER,tags_json TEXT,enabled INTEGER,sort_order INTEGER);
    CREATE TABLE equipment(equipment_id TEXT PRIMARY KEY,display_name TEXT,description TEXT,slot_key TEXT,card_pool_id TEXT,tags_json TEXT,enabled INTEGER,sort_order INTEGER);
    CREATE TABLE game_settings(setting_key TEXT PRIMARY KEY, value_json TEXT);
    INSERT INTO rarities VALUES('gray','灰','#fff',1,0);
  `)
  database.close()
  return { root, databasePath }
}

/** 创建包含根序列与伤害节点的完整测试卡牌。 */
function createCard(): CardEditorRecord {
  return {
    cardId: 'vue_test_card', displayName: 'Vue 测试卡', description: '测试', artworkKey: '', rarityId: 'gray', familyId: 'test',
    actionCost: 1, manaCost: 0, spendAllAction: false, spendAllMana: false, isAttack: true, exhaustOnPlay: false,
    temporary: false, curse: false, unplayable: false, selectionMode: 'unit', targetRange: 1, teamFilter: 'enemy',
    lifeStateFilter: 'alive', requiresLineOfSight: true, allowSelf: false, enabled: true, sortOrder: 0, rowVersion: 1,
    tags: ['attack', 'test'], poolId: 'vue_pool', includeInTestDeck: true,
    behaviorNodes: [
      { behaviorId: 'vue_test_card.on_play', triggerKey: 'on_play', priority: 0, enabled: true, nodeId: 'vue_test_card.on_play.root', parentNodeId: '', branchKey: 'children', sortOrder: 0, nodeKind: 'sequence', operationKey: 'sequence', parametersJson: '{}' },
      { behaviorId: 'vue_test_card.on_play', triggerKey: 'on_play', priority: 0, enabled: true, nodeId: 'vue_test_card.on_play.damage', parentNodeId: 'vue_test_card.on_play.root', branchKey: 'children', sortOrder: 1, nodeKind: 'effect', operationKey: 'damage', parametersJson: '{"target":"selected_unit","amount":{"kind":"constant","value":4}}' },
    ],
  }
}

afterEach(() => {
  for (const directory of temporaryDirectories.splice(0)) fs.rmSync(directory, { recursive: true, force: true })
})

describe('CardRepository', () => {
  it('完整往返卡牌聚合并保留外键删除保护', () => {
    const temporary = createDatabase()
    const repository = new CardRepository(temporary.databasePath)
    const card = createCard()
    repository.saveCard(card)
    const loaded = repository.loadCards()[0]
    expect(loaded.tags).toEqual(['attack', 'test'])
    expect(loaded.poolId).toBe('vue_pool')
    expect(loaded.behaviorNodes).toHaveLength(2)
    expect(() => repository.deleteCard(card.cardId)).toThrow()
    loaded.includeInTestDeck = false
    repository.saveCard(loaded)
    expect(repository.deleteCard(card.cardId)).toBe(true)
    repository.close()
  })

  it('导入卡面、登记资源并返回可显示预览', () => {
    const temporary = createDatabase()
    const repository = new CardRepository(temporary.databasePath)
    const source = path.join(temporary.root, 'source.png')
    fs.writeFileSync(source, Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=', 'base64'))
    const artwork = new ArtworkService(temporary.root, repository)
    const result = artwork.import(source, 'vue_test_card')
    expect(result.succeeded).toBe(true)
    expect(result.value).toBe('card.vue_test_card.artwork')
    expect(artwork.preview(result.value!).value).toMatch(/^data:image\/png;base64,/)
    repository.close()
  })

  it('完整往返状态主记录与行为图', () => {
    const temporary = createDatabase()
    const repository = new CardRepository(temporary.databasePath)
    repository.saveStatus({
      statusId: 'sharp', displayName: '锋利', description: '攻击加伤', category: 'positive',
      maximumStacks: 99, stackingPolicy: 'add', durationPolicy: 'none', enabled: true,
      behaviorNodes: [
        { behaviorId: 'sharp.query', triggerKey: 'query_outgoing_attack_damage', priority: 0, enabled: true, nodeId: 'sharp.query.root', parentNodeId: '', branchKey: 'children', sortOrder: 0, nodeKind: 'sequence', operationKey: 'sequence', parametersJson: '{}' },
        { behaviorId: 'sharp.query', triggerKey: 'query_outgoing_attack_damage', priority: 0, enabled: true, nodeId: 'sharp.query.fx', parentNodeId: 'sharp.query.root', branchKey: 'children', sortOrder: 1, nodeKind: 'effect', operationKey: 'modify_query_value', parametersJson: '{"amount":{"kind":"constant","value":1}}' },
      ],
    })
    const loaded = repository.loadStatuses()[0]
    expect(loaded.displayName).toBe('锋利')
    expect(loaded.behaviorNodes).toHaveLength(2)
    expect(repository.deleteStatus('sharp')).toBe(true)
    expect(repository.loadStatuses()).toHaveLength(0)
    repository.close()
  })
})
