import { app, BrowserWindow, dialog, ipcMain } from 'electron'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { installApplicationMenu } from './app-menu.js'
import type {
  AssetRecord, CardEditorRecord, CharacterRecord, ClassRecord, DeckRecord,
  EquipmentRecord, GameSettingsRecord, PoolRecord, RarityRecord, StatusRecord,
} from './contracts.js'
import { CardEditorService } from './services/card-editor-service.js'
import { findProjectRoot } from './services/project-locator.js'

const moduleDirectory = path.dirname(fileURLToPath(import.meta.url))
let service: CardEditorService | undefined

/** 创建安全隔离的主窗口，并在开发或发布环境加载对应 Vue 页面。 */
async function createWindow(): Promise<BrowserWindow> {
  const window = new BrowserWindow({
    width: 1600, height: 940, minWidth: 1180, minHeight: 720,
    title: 'Legends of Furry · 内容包编辑器',
    backgroundColor: '#f1f3f6',
    webPreferences: {
      preload: path.join(moduleDirectory, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: false,
    },
  })
  window.webContents.openDevTools()
  if (process.env.VITE_DEV_SERVER_URL) await window.loadURL(process.env.VITE_DEV_SERVER_URL)
  else await window.loadFile(path.join(moduleDirectory, '..', 'dist', 'index.html'))
  return window
}

/** 把仓储异常转成策划可读失败结果。 */
function tryRun(run: () => { succeeded: boolean; message: string }): { succeeded: boolean; message: string } {
  try { return run() } catch (error) { return failure(error) }
}
function failure(error: unknown): { succeeded: false; message: string } {
  return { succeeded: false, message: error instanceof Error ? error.message : String(error) }
}

/** 注册渲染进程所需的全部业务接口，IPC 层不包含数据库规则。 */
function registerHandlers(editor: CardEditorService): void {
  ipcMain.handle('cards:bootstrap', () => editor.bootstrap())
  ipcMain.handle('cards:save', (_event, card: CardEditorRecord) => {
    try { return editor.saveCard(card) } catch (error) { return failure(error) }
  })
  ipcMain.handle('cards:delete', (_event, cardId: string) => {
    try { return editor.deleteCard(cardId) } catch (error) { return failure(error) }
  })
  ipcMain.handle('pack:saveStatus', (_event, record: StatusRecord) => tryRun(() => editor.saveStatus(record)))
  ipcMain.handle('pack:deleteStatus', (_event, id: string) => tryRun(() => editor.deleteStatus(id)))
  ipcMain.handle('pack:saveClass', (_event, record: ClassRecord) => tryRun(() => editor.saveClass(record)))
  ipcMain.handle('pack:deleteClass', (_event, id: string) => tryRun(() => editor.deleteClass(id)))
  ipcMain.handle('pack:saveCharacter', (_event, record: CharacterRecord) => tryRun(() => editor.saveCharacter(record)))
  ipcMain.handle('pack:deleteCharacter', (_event, id: string) => tryRun(() => editor.deleteCharacter(id)))
  ipcMain.handle('pack:saveEquipment', (_event, record: EquipmentRecord) => tryRun(() => editor.saveEquipment(record)))
  ipcMain.handle('pack:deleteEquipment', (_event, id: string) => tryRun(() => editor.deleteEquipment(id)))
  ipcMain.handle('pack:savePool', (_event, record: PoolRecord) => tryRun(() => editor.savePool(record)))
  ipcMain.handle('pack:deletePool', (_event, id: string) => tryRun(() => editor.deletePool(id)))
  ipcMain.handle('pack:saveDeck', (_event, record: DeckRecord) => tryRun(() => editor.saveDeck(record)))
  ipcMain.handle('pack:deleteDeck', (_event, id: string) => tryRun(() => editor.deleteDeck(id)))
  ipcMain.handle('pack:saveRarity', (_event, record: RarityRecord) => tryRun(() => editor.saveRarity(record)))
  ipcMain.handle('pack:deleteRarity', (_event, id: string) => tryRun(() => editor.deleteRarity(id)))
  ipcMain.handle('pack:saveAssetMeta', (_event, record: AssetRecord) => tryRun(() => editor.saveAssetMeta(record)))
  ipcMain.handle('pack:deleteAsset', (_event, id: string) => tryRun(() => editor.deleteAsset(id)))
  ipcMain.handle('pack:saveGameSettings', (_event, record: GameSettingsRecord) => tryRun(() => editor.saveGameSettings(record)))
  ipcMain.handle('artwork:choose', async () => {
    const result = await dialog.showOpenDialog({
      title: '选择卡牌卡面', properties: ['openFile'],
      filters: [{ name: '卡面图片', extensions: ['png', 'jpg', 'jpeg'] }],
    })
    return result.canceled ? '' : result.filePaths[0] ?? ''
  })
  ipcMain.handle('artwork:import', (_event, sourcePath: string, cardId: string) => {
    try { return editor.artwork.import(sourcePath, cardId) } catch (error) { return failure(error) }
  })
  ipcMain.handle('artwork:preview', (_event, assetKey: string) => {
    try { return editor.artwork.preview(assetKey) } catch (error) { return failure(error) }
  })
  ipcMain.handle('content:validate', async (_event, version: string) => {
    try { return await editor.validate(version) } catch (error) { return failure(error) }
  })
  ipcMain.handle('content:publish', async (_event, version: string) => {
    try { return await editor.publish(version) } catch (error) { return failure(error) }
  })
  ipcMain.handle('content:rollback', (_event, version: string) => {
    try { return editor.rollback(version) } catch (error) { return failure(error) }
  })
}

/** 初始化项目服务和窗口，并在 macOS 重新激活时恢复主窗口。 */
async function startApplication(): Promise<void> {
  const projectRoot = findProjectRoot(app.getAppPath())
  service = new CardEditorService(projectRoot)
  registerHandlers(service)
  installApplicationMenu()
  await createWindow()
  app.on('activate', async () => {
    if (BrowserWindow.getAllWindows().length === 0) await createWindow()
  })
}

app.whenReady().then(startApplication).catch((error) => {
  dialog.showErrorBox('卡牌维护工具启动失败', error instanceof Error ? error.message : String(error))
  app.quit()
})

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit()
})

app.on('before-quit', () => {
  service?.close()
  service = undefined
})
