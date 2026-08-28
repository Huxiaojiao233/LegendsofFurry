import { contextBridge, ipcRenderer, webUtils } from 'electron'
import type {
  AssetRecord, CardEditorRecord, CharacterRecord, ClassRecord, DeckRecord,
  EquipmentRecord, GameSettingsRecord, MenuCommand, PoolRecord, RarityRecord, StatusRecord,
} from './contracts.js'

const cardEditorApi = {
  bootstrap: () => ipcRenderer.invoke('cards:bootstrap'),
  saveCard: (card: CardEditorRecord) => ipcRenderer.invoke('cards:save', card),
  deleteCard: (cardId: string) => ipcRenderer.invoke('cards:delete', cardId),
  saveStatus: (record: StatusRecord) => ipcRenderer.invoke('pack:saveStatus', record),
  deleteStatus: (statusId: string) => ipcRenderer.invoke('pack:deleteStatus', statusId),
  saveClass: (record: ClassRecord) => ipcRenderer.invoke('pack:saveClass', record),
  deleteClass: (classId: string) => ipcRenderer.invoke('pack:deleteClass', classId),
  saveCharacter: (record: CharacterRecord) => ipcRenderer.invoke('pack:saveCharacter', record),
  deleteCharacter: (characterId: string) => ipcRenderer.invoke('pack:deleteCharacter', characterId),
  saveEquipment: (record: EquipmentRecord) => ipcRenderer.invoke('pack:saveEquipment', record),
  deleteEquipment: (equipmentId: string) => ipcRenderer.invoke('pack:deleteEquipment', equipmentId),
  savePool: (record: PoolRecord) => ipcRenderer.invoke('pack:savePool', record),
  deletePool: (poolId: string) => ipcRenderer.invoke('pack:deletePool', poolId),
  saveDeck: (record: DeckRecord) => ipcRenderer.invoke('pack:saveDeck', record),
  deleteDeck: (deckId: string) => ipcRenderer.invoke('pack:deleteDeck', deckId),
  saveRarity: (record: RarityRecord) => ipcRenderer.invoke('pack:saveRarity', record),
  deleteRarity: (rarityId: string) => ipcRenderer.invoke('pack:deleteRarity', rarityId),
  saveAssetMeta: (record: AssetRecord) => ipcRenderer.invoke('pack:saveAssetMeta', record),
  deleteAsset: (assetKey: string) => ipcRenderer.invoke('pack:deleteAsset', assetKey),
  saveGameSettings: (record: GameSettingsRecord) => ipcRenderer.invoke('pack:saveGameSettings', record),
  chooseArtwork: () => ipcRenderer.invoke('artwork:choose'),
  importArtwork: (sourcePath: string, cardId: string) => ipcRenderer.invoke('artwork:import', sourcePath, cardId),
  artworkPreview: (assetKey: string) => ipcRenderer.invoke('artwork:preview', assetKey),
  getPathForFile: (file: File) => webUtils.getPathForFile(file),
  validate: (version: string) => ipcRenderer.invoke('content:validate', version),
  publish: (version: string) => ipcRenderer.invoke('content:publish', version),
  rollback: (version: string) => ipcRenderer.invoke('content:rollback', version),
  onMenu: (handler: (command: MenuCommand) => void) => {
    ipcRenderer.on('app-menu', (_event, command: MenuCommand) => handler(command))
  },
}

contextBridge.exposeInMainWorld('cardEditor', cardEditorApi)
