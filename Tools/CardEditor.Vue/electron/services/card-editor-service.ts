import path from 'node:path'
import type {
  AssetRecord, BootstrapPayload, CardEditorRecord, CharacterRecord, ClassRecord, DeckRecord,
  EquipmentRecord, GameSettingsRecord, OperationResult, PoolRecord, RarityRecord, StatusRecord,
} from '../contracts.js'
import { backupDatabase } from './backup-service.js'
import { CardRepository } from './card-repository.js'
import { ArtworkService } from './artwork-service.js'
import { ContentToolService } from './content-tool-service.js'

/** 聚合卡牌仓储、资源管理和内容编译器，向 IPC 暴露与未来 HTTP 无关的统一业务接口。 */
export class CardEditorService {
  readonly repository: CardRepository
  readonly artwork: ArtworkService
  readonly contentTool: ContentToolService
  readonly databasePath: string

  /** 根据 Unity 项目根目录装配全部本地业务服务。 */
  constructor(readonly projectRoot: string) {
    this.databasePath = path.join(projectRoot, 'ContentSource', 'lof-content.db')
    this.repository = new CardRepository(this.databasePath)
    this.artwork = new ArtworkService(projectRoot, this.repository)
    this.contentTool = new ContentToolService(projectRoot, this.databasePath)
  }

  /** 返回首屏卡牌数据、项目定位信息和当前运行时能力选项。 */
  bootstrap(): BootstrapPayload {
    return {
      cards: this.repository.loadCards(),
      statuses: this.repository.loadStatuses(),
      classes: this.repository.loadClasses(),
      characters: this.repository.loadCharacters(),
      equipment: this.repository.loadEquipment(),
      pools: this.repository.loadPools(),
      decks: this.repository.loadDecks(),
      rarityRecords: this.repository.loadRarities(),
      assets: this.repository.loadAssets(),
      gameSettings: this.repository.loadGameSettings(),
      databasePath: this.databasePath, projectRoot: this.projectRoot,
      options: {
        rarities: ['gray', 'blue', 'purple', 'gold', 'red'],
        selectionModes: ['none', 'self', 'unit', 'cell', 'direction'],
        teamFilters: ['any', 'ally', 'enemy'], lifeStates: ['any', 'alive', 'dead'],
        triggers: [
          'on_play', 'on_draw', 'on_added_to_hand', 'on_turn_end_in_hand',
          'on_unit_turn_start', 'on_unit_turn_end', 'on_status_changed', 'on_activated_ability',
          'query_outgoing_attack_damage', 'query_incoming_damage', 'query_after_attack', 'query_attack_hit_count',
          'query_armor_gain', 'query_armor_retention', 'query_maximum_action_points', 'query_move_steps',
          'query_mana_gain', 'query_can_play_card', 'query_can_take_turn', 'query_target_range', 'query_lethal_recovery',
        ],
        effects: ['damage','heal','gain_armor','revive','knock_back','add_status','set_status','reduce_status','remove_status','clear_statuses','consume_owner_status','transform_owner_status','modify_action_points','modify_max_action_points','modify_mana','spend_resource','draw_cards','generate_card','move_cards','remove_cards_by_query','modify_card_runtime_value','reveal_top_cards_and_choose_discard','play_top_cards_for_free','begin_free_move','end_turn','no_op','play_vfx','play_sfx','modify_query_value','cancel_query'],
        effectTargets: ['self', 'selected_unit', 'current_graph_target'],
        conditions: ['all','any','not','target_is_alive','target_is_dead','target_is_self','target_team_is','target_has_armor','target_has_status','card_has_tag','card_in_pool','card_rarity_is','target_killed_by_this_card','damage_was_applied','health_damage_greater_than','resource_compare','health_compare','armor_compare','spent_action_compare','spent_mana_compare','random_chance'],
        foreachTargets: ['self','selected_unit','selected_cell','units_around_selected','units_in_manhattan_range','units_in_direction','units_in_front_area','all_allies','all_enemies','all_units','current_card','cards_in_hand','cards_in_draw_pile','cards_in_discard_pile','cards_in_exhaust_pile'],
        equipmentSlots: ['weapon', 'offhand', 'accessory', 'armor', 'treasure', 'boot'],
        statusCategories: ['neutral', 'positive', 'negative'],
        stackingPolicies: ['add', 'set', 'ignore'],
        durationPolicies: ['none', 'turns', 'combat'],
      },
    }
  }

  /** 备份数据库后以聚合事务保存一张卡牌。 */
  saveCard(card: CardEditorRecord): OperationResult {
    return this.mutate('save', () => this.repository.saveCard(card), `卡牌 ${card.cardId} 已保存。`)
  }

  /** 备份数据库后删除卡牌，并保留 SQLite 外键引用保护。 */
  deleteCard(cardId: string): OperationResult {
    return this.mutate('delete', () => this.repository.deleteCard(cardId), `卡牌 ${cardId} 已删除。`, `卡牌不存在：${cardId}`)
  }

  /** 保存状态定义与行为图。 */
  saveStatus(record: StatusRecord): OperationResult {
    return this.mutate('save', () => this.repository.saveStatus(record), `状态 ${record.statusId} 已保存。`)
  }

  /** 删除状态定义。 */
  deleteStatus(statusId: string): OperationResult {
    return this.mutate('delete', () => this.repository.deleteStatus(statusId), `状态 ${statusId} 已删除。`, `状态不存在：${statusId}`)
  }

  /** 保存职业资料与行为图。 */
  saveClass(record: ClassRecord): OperationResult {
    return this.mutate('save', () => this.repository.saveClass(record), `职业 ${record.classId} 已保存。`)
  }

  /** 删除职业资料。 */
  deleteClass(classId: string): OperationResult {
    return this.mutate('delete', () => this.repository.deleteClass(classId), `职业 ${classId} 已删除。`, `职业不存在：${classId}`)
  }

  /** 保存角色定义与行为图。 */
  saveCharacter(record: CharacterRecord): OperationResult {
    return this.mutate('save', () => this.repository.saveCharacter(record), `角色 ${record.characterId} 已保存。`)
  }

  /** 删除角色定义。 */
  deleteCharacter(characterId: string): OperationResult {
    return this.mutate('delete', () => this.repository.deleteCharacter(characterId), `角色 ${characterId} 已删除。`, `角色不存在：${characterId}`)
  }

  /** 保存装备定义与行为图。 */
  saveEquipment(record: EquipmentRecord): OperationResult {
    return this.mutate('save', () => this.repository.saveEquipment(record), `装备 ${record.equipmentId} 已保存。`)
  }

  /** 删除装备定义。 */
  deleteEquipment(equipmentId: string): OperationResult {
    return this.mutate('delete', () => this.repository.deleteEquipment(equipmentId), `装备 ${equipmentId} 已删除。`, `装备不存在：${equipmentId}`)
  }

  /** 保存卡池。 */
  savePool(record: PoolRecord): OperationResult {
    return this.mutate('save', () => this.repository.savePool(record), `卡池 ${record.poolId} 已保存。`)
  }

  /** 删除卡池。 */
  deletePool(poolId: string): OperationResult {
    return this.mutate('delete', () => this.repository.deletePool(poolId), `卡池 ${poolId} 已删除。`, `卡池不存在：${poolId}`)
  }

  /** 保存固定牌库。 */
  saveDeck(record: DeckRecord): OperationResult {
    return this.mutate('save', () => this.repository.saveDeck(record), `牌库 ${record.deckId} 已保存。`)
  }

  /** 删除固定牌库。 */
  deleteDeck(deckId: string): OperationResult {
    return this.mutate('delete', () => this.repository.deleteDeck(deckId), `牌库 ${deckId} 已删除。`, `牌库不存在：${deckId}`)
  }

  /** 保存稀有度。 */
  saveRarity(record: RarityRecord): OperationResult {
    return this.mutate('save', () => this.repository.saveRarity(record), `稀有度 ${record.rarityId} 已保存。`)
  }

  /** 删除稀有度。 */
  deleteRarity(rarityId: string): OperationResult {
    return this.mutate('delete', () => this.repository.deleteRarity(rarityId), `稀有度 ${rarityId} 已删除。`, `稀有度不存在：${rarityId}`)
  }

  /** 保存资源元数据。 */
  saveAssetMeta(record: AssetRecord): OperationResult {
    return this.mutate('save', () => this.repository.saveAssetMeta(record), `资源 ${record.assetKey} 已保存。`)
  }

  /** 删除资源元数据。 */
  deleteAsset(assetKey: string): OperationResult {
    return this.mutate('delete', () => this.repository.deleteAsset(assetKey), `资源 ${assetKey} 已删除。`, `资源不存在：${assetKey}`)
  }

  /** 保存基础战斗参数。 */
  saveGameSettings(record: GameSettingsRecord): OperationResult {
    return this.mutate('save', () => this.repository.saveGameSettings(record), '战斗规则已保存。')
  }

  /** 备份后执行写入，布尔删除结果转成策划可读消息。 */
  private mutate(kind: string, run: () => void | boolean, ok: string, missing?: string): OperationResult {
    backupDatabase(this.projectRoot, this.databasePath, kind)
    const result = run()
    if (result === false) return { succeeded: false, message: missing ?? '记录不存在。' }
    return { succeeded: true, message: ok }
  }

  /** 调用权威内容编译器验证当前数据库。 */
  validate(version: string): Promise<OperationResult> {
    return this.contentTool.validate(version)
  }

  /** 备份数据库后调用权威内容编译器发布到 Unity。 */
  publish(version: string): Promise<OperationResult> {
    backupDatabase(this.projectRoot, this.databasePath, 'publish')
    return this.contentTool.publish(version)
  }

  /** 将 Unity 当前内容指针回退到指定历史版本。 */
  rollback(version: string): OperationResult {
    return this.contentTool.rollback(version)
  }

  /** 关闭所有持久资源，供 Electron 正常退出。 */
  close(): void {
    this.repository.close()
  }
}
