import crypto from 'node:crypto'
import fs from 'node:fs'
import path from 'node:path'
import { backupArtwork } from './backup-service.js'
import type { CardRepository } from './card-repository.js'
import type { OperationResult } from '../contracts.js'

/** 验证并清理卡牌 ID，使其可以稳定用于受管卡面文件名。 */
function safeFileStem(cardId: string): string {
  const value = cardId.trim().replace(/[^\p{L}\p{N}_.-]/gu, '_').replace(/^\.+|\.+$/g, '')
  if (!value) throw new Error('请先填写稳定卡牌 ID。')
  return value
}

/** 返回卡面扩展名对应的浏览器 MIME 类型。 */
function mimeType(filePath: string): string {
  return path.extname(filePath).toLowerCase() === '.png' ? 'image/png' : 'image/jpeg'
}

/** 管理卡面复制、备份、哈希、资源登记和预览数据读取。 */
export class ArtworkService {
  /** 保存项目与仓储依赖，确保文件和 SQLite 记录由同一服务协调。 */
  constructor(private readonly projectRoot: string, private readonly repository: CardRepository) {}

  /** 导入一张外部图片到 Unity Resources 并登记稳定资源 Key。 */
  import(sourcePath: string, cardId: string): OperationResult {
    if (!fs.existsSync(sourcePath) || !fs.statSync(sourcePath).isFile()) return { succeeded: false, message: '找不到拖入的卡面文件。' }
    const extension = path.extname(sourcePath).toLowerCase()
    if (!['.png', '.jpg', '.jpeg'].includes(extension)) return { succeeded: false, message: '卡面仅支持 PNG、JPG 或 JPEG。' }
    const stem = safeFileStem(cardId)
    const directory = path.join(this.projectRoot, 'Assets', 'Resources', 'CardArt')
    fs.mkdirSync(directory, { recursive: true })
    const destination = path.join(directory, `${stem}${extension}`)
    backupArtwork(this.projectRoot, destination)
    if (path.resolve(sourcePath).toLowerCase() !== path.resolve(destination).toLowerCase()) fs.copyFileSync(sourcePath, destination)
    const sha256 = crypto.createHash('sha256').update(fs.readFileSync(destination)).digest('hex')
    const assetKey = `card.${cardId.trim()}.artwork`
    this.repository.saveAsset(assetKey, `CardArt/${stem}${extension}`, sha256)
    return { succeeded: true, message: `卡面已导入：${path.basename(destination)}。请保存卡牌。`, value: assetKey }
  }

  /** 读取资源 Key 对应图片并转换为只传输当前预览的 Data URL。 */
  preview(assetKey: string): OperationResult {
    if (!assetKey) return { succeeded: true, message: '', value: '' }
    const relative = this.repository.getAssetPath(assetKey)
    if (!relative) return { succeeded: false, message: `数据库中不存在资源：${assetKey}` }
    const normalized = relative.replace(/\\/g, '/')
    const filePath = normalized.toLowerCase().startsWith('assets/')
      ? path.join(this.projectRoot, ...normalized.split('/'))
      : path.join(this.projectRoot, 'Assets', 'Resources', ...normalized.split('/'))
    if (!fs.existsSync(filePath)) return { succeeded: false, message: `卡面文件不存在：${relative}` }
    const data = fs.readFileSync(filePath).toString('base64')
    return { succeeded: true, message: '', value: `data:${mimeType(filePath)};base64,${data}` }
  }
}
