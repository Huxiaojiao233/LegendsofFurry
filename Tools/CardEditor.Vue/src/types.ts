export interface BehaviorNodeRow {
  behaviorId: string; triggerKey: string; priority: number; enabled: boolean
  nodeId: string; parentNodeId: string; branchKey: string; sortOrder: number
  nodeKind: string; operationKey: string; parametersJson: string
}

export interface CardEditorRecord {
  cardId: string; displayName: string; description: string; artworkKey: string
  rarityId: string; familyId: string; actionCost: number; manaCost: number
  spendAllAction: boolean; spendAllMana: boolean; isAttack: boolean
  exhaustOnPlay: boolean; temporary: boolean; curse: boolean; unplayable: boolean
  selectionMode: string; targetRange: number; teamFilter: string; lifeStateFilter: string
  requiresLineOfSight: boolean; allowSelf: boolean; enabled: boolean; sortOrder: number
  rowVersion: number; tags: string[]; poolId: string; includeInTestDeck: boolean
  behaviorNodes: BehaviorNodeRow[]; isExisting?: boolean
}

export interface StatusRecord {
  statusId: string; displayName: string; description: string; category: string
  maximumStacks: number; stackingPolicy: string; durationPolicy: string; enabled: boolean
  behaviorNodes: BehaviorNodeRow[]; isExisting?: boolean
}

export interface DeckRecipeRow { poolId: string; amount: number; fixedCardId: string; sortOrder: number }
export interface TraitRow { key: string; value: string }

export interface ClassRecord {
  classId: string; displayName: string; description: string
  initialHealth: number; initialMana: number; maximumMana: number
  deckRecipe: DeckRecipeRow[]; traits: TraitRow[]; enabled: boolean; sortOrder: number
  behaviorNodes: BehaviorNodeRow[]; isExisting?: boolean
}

export interface CharacterRecord {
  characterId: string; displayName: string; description: string
  initialHealth: number; baseDamage: number; moveSteps: number; tags: string[]
  enabled: boolean; sortOrder: number; behaviorNodes: BehaviorNodeRow[]; isExisting?: boolean
}

export interface EquipmentRecord {
  equipmentId: string; displayName: string; description: string
  slotKey: string; cardPoolId: string; tags: string[]; enabled: boolean; sortOrder: number
  behaviorNodes: BehaviorNodeRow[]; isExisting?: boolean
}

export interface PoolRecord { poolId: string; displayName: string; enabled: boolean; sortOrder: number; isExisting?: boolean }
export interface DeckEntryRow { cardId: string; amount: number; sortOrder: number }
export interface DeckRecord {
  deckId: string; displayName: string; isTestDeck: boolean; enabled: boolean
  entries: DeckEntryRow[]; isExisting?: boolean
}
export interface RarityRecord {
  rarityId: string; displayName: string; colorHex: string; defaultWeight: number; sortOrder: number; isExisting?: boolean
}
export interface AssetRecord { assetKey: string; assetKind: string; relativePath: string; sha256: string; isExisting?: boolean }
export interface GameSettingsRecord {
  handLimit: number; startingHandSize: number; drawPerTurn: number
  baseActionPoints: number; baseMoveSteps: number; playerCharacterId: string; enemyCharacterId: string
}

export type EditorSection =
  | 'cards' | 'statuses' | 'classes' | 'characters' | 'equipment'
  | 'pools' | 'decks' | 'rarities' | 'assets' | 'settings'

export interface EditorOptions {
  rarities: string[]; selectionModes: string[]; teamFilters: string[]; lifeStates: string[]
  triggers: string[]; effects: string[]; effectTargets: string[]; conditions: string[]; foreachTargets: string[]
  equipmentSlots: string[]; statusCategories: string[]; stackingPolicies: string[]; durationPolicies: string[]
}

export interface BootstrapPayload {
  cards: CardEditorRecord[]; statuses: StatusRecord[]; classes: ClassRecord[]
  characters: CharacterRecord[]; equipment: EquipmentRecord[]; pools: PoolRecord[]
  decks: DeckRecord[]; rarityRecords: RarityRecord[]; assets: AssetRecord[]
  gameSettings: GameSettingsRecord; databasePath: string; projectRoot: string; options: EditorOptions
}

export interface OperationResult {
  succeeded: boolean; message: string; output?: string; issues?: string[]; value?: string
}

export type MenuCommand =
  | { action: 'save' | 'reload' | 'validate' | 'publish' | 'new' | 'duplicate' | 'delete' }
  | { action: 'section'; section: EditorSection }

export interface SimpleEffectRow {
  nodeId: string; triggerKey: string; effectKey: string; targetKey: string
  amount: number; damageType: string; statusId: string
}

export interface CardEditorApi {
  bootstrap(): Promise<BootstrapPayload>
  saveCard(card: CardEditorRecord): Promise<OperationResult>
  deleteCard(cardId: string): Promise<OperationResult>
  saveStatus(record: StatusRecord): Promise<OperationResult>
  deleteStatus(statusId: string): Promise<OperationResult>
  saveClass(record: ClassRecord): Promise<OperationResult>
  deleteClass(classId: string): Promise<OperationResult>
  saveCharacter(record: CharacterRecord): Promise<OperationResult>
  deleteCharacter(characterId: string): Promise<OperationResult>
  saveEquipment(record: EquipmentRecord): Promise<OperationResult>
  deleteEquipment(equipmentId: string): Promise<OperationResult>
  savePool(record: PoolRecord): Promise<OperationResult>
  deletePool(poolId: string): Promise<OperationResult>
  saveDeck(record: DeckRecord): Promise<OperationResult>
  deleteDeck(deckId: string): Promise<OperationResult>
  saveRarity(record: RarityRecord): Promise<OperationResult>
  deleteRarity(rarityId: string): Promise<OperationResult>
  saveAssetMeta(record: AssetRecord): Promise<OperationResult>
  deleteAsset(assetKey: string): Promise<OperationResult>
  saveGameSettings(record: GameSettingsRecord): Promise<OperationResult>
  chooseArtwork(): Promise<string>
  importArtwork(sourcePath: string, cardId: string): Promise<OperationResult>
  artworkPreview(assetKey: string): Promise<OperationResult>
  getPathForFile(file: File): string
  validate(version: string): Promise<OperationResult>
  publish(version: string): Promise<OperationResult>
  rollback(version: string): Promise<OperationResult>
  onMenu(handler: (command: MenuCommand) => void): void
}
