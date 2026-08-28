import fs from 'node:fs'
import path from 'node:path'

/** 判断候选目录是否同时包含 Unity Assets 与内容源数据库目录。 */
function isProjectRoot(candidate: string): boolean {
  return fs.existsSync(path.join(candidate, 'Assets')) && fs.existsSync(path.join(candidate, 'ContentSource'))
}

/** 从环境变量、当前目录和应用目录向上查找 Unity 项目根目录。 */
export function findProjectRoot(appPath: string): string {
  const seeds = [process.env.LOF_PROJECT_ROOT, process.cwd(), appPath, path.dirname(process.execPath)].filter(Boolean) as string[]
  for (const seed of seeds) {
    let current = path.resolve(seed)
    for (let depth = 0; depth < 8; depth += 1) {
      if (isProjectRoot(current)) return current
      const parent = path.dirname(current)
      if (parent === current) break
      current = parent
    }
  }
  throw new Error('无法定位 Unity 项目根目录，请设置 LOF_PROJECT_ROOT。')
}
