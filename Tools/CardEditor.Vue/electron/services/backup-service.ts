import fs from 'node:fs'
import path from 'node:path'

/** 把时间格式化为可安全用于 Windows 文件名的紧凑字符串。 */
function timestamp(): string {
  return new Date().toISOString().replace(/[-:TZ.]/g, '').slice(0, 17)
}

/** 在数据库旁创建带原因与时间戳的可恢复副本。 */
export function backupDatabase(projectRoot: string, databasePath: string, reason: string): string {
  const directory = path.join(projectRoot, 'ContentSource', 'Backups')
  fs.mkdirSync(directory, { recursive: true })
  const destination = path.join(directory, `${reason}-${timestamp()}.sqlite`)
  fs.copyFileSync(databasePath, destination, fs.constants.COPYFILE_EXCL)
  return destination
}

/** 在覆盖受管卡面之前保存旧图片副本。 */
export function backupArtwork(projectRoot: string, artworkPath: string): string | undefined {
  if (!fs.existsSync(artworkPath)) return undefined
  const directory = path.join(projectRoot, 'ContentSource', 'Backups', 'Artwork')
  fs.mkdirSync(directory, { recursive: true })
  const parsed = path.parse(artworkPath)
  const destination = path.join(directory, `${parsed.name}-${timestamp()}${parsed.ext}`)
  fs.copyFileSync(artworkPath, destination, fs.constants.COPYFILE_EXCL)
  return destination
}
