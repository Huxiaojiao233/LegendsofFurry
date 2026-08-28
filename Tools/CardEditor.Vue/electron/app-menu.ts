import { BrowserWindow, Menu } from 'electron'
import type { EditorSection, MenuCommand } from './contracts.js'

const sections: { section: EditorSection; label: string }[] = [
  { section: 'cards', label: '卡牌' },
  { section: 'statuses', label: '状态' },
  { section: 'classes', label: '职业' },
  { section: 'characters', label: '角色' },
  { section: 'equipment', label: '装备' },
  { section: 'pools', label: '卡池' },
  { section: 'decks', label: '牌库' },
  { section: 'rarities', label: '稀有度' },
  { section: 'assets', label: '资源' },
  { section: 'settings', label: '战斗规则' },
]

/** 向当前窗口发送菜单命令。 */
function send(command: MenuCommand): void {
  BrowserWindow.getFocusedWindow()?.webContents.send('app-menu', command)
}

/** 安装 Windows 原生菜单栏：文件、内容、编辑、发布。 */
export function installApplicationMenu(): void {
  Menu.setApplicationMenu(Menu.buildFromTemplate([
    {
      label: '文件',
      submenu: [
        { label: '保存', accelerator: 'CmdOrCtrl+S', click: () => send({ action: 'save' }) },
        { label: '刷新', accelerator: 'CmdOrCtrl+R', click: () => send({ action: 'reload' }) },
        { type: 'separator' },
        { role: 'quit', label: '退出' },
      ],
    },
    {
      label: '内容',
      submenu: sections.map((item) => ({
        label: item.label,
        click: () => send({ action: 'section', section: item.section }),
      })),
    },
    {
      label: '编辑',
      submenu: [
        { label: '新建', accelerator: 'CmdOrCtrl+N', click: () => send({ action: 'new' }) },
        { label: '复制', accelerator: 'CmdOrCtrl+D', click: () => send({ action: 'duplicate' }) },
        { label: '删除', click: () => send({ action: 'delete' }) },
        { type: 'separator' },
        { role: 'undo', label: '撤销' },
        { role: 'redo', label: '重做' },
        { role: 'cut', label: '剪切' },
        { role: 'copy', label: '复制文本' },
        { role: 'paste', label: '粘贴' },
      ],
    },
    {
      label: '发布',
      submenu: [
        { label: '验证整库', click: () => send({ action: 'validate' }) },
        { label: '发布到 Unity', click: () => send({ action: 'publish' }) },
      ],
    },
  ]))
}
