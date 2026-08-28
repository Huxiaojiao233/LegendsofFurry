import { spawn } from 'node:child_process'
import fs from 'node:fs'
import path from 'node:path'
import type { OperationResult } from '../contracts.js'

/** 启动子进程并完整收集标准输出、错误和退出码。 */
function runProcess(command: string, args: string[], cwd: string): Promise<{ code: number; output: string }> {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, { cwd, windowsHide: true, shell: false })
    const chunks: Buffer[] = []
    child.stdout.on('data', (chunk: Buffer) => { chunks.push(chunk) })
    child.stderr.on('data', (chunk: Buffer) => { chunks.push(chunk) })
    child.on('error', reject)
    child.on('close', (code) => {
      const encoding = process.platform === 'win32' ? 'gb18030' : 'utf-8'
      const output = new TextDecoder(encoding).decode(Buffer.concat(chunks)).trim()
      resolve({ code: code ?? 1, output })
    })
  })
}

/** 把内容编译器输出中的问题行提取为界面列表。 */
function parseIssues(output: string): string[] {
  return output.split(/\r?\n/).filter((line) => /^\[(ERROR|WARNING|INFO)\]/i.test(line.trim()))
}

/** 通过现有 .NET 内容编译器提供与 Unity 完全一致的校验和发布能力。 */
export class ContentToolService {
  /** 保存项目根目录供所有编译器命令定位数据库与输出目录。 */
  constructor(private readonly projectRoot: string, private readonly databasePath: string) {}

  /** 选择已构建 DLL；不存在时回退到 dotnet run，保证开发环境也能启动。 */
  private command(command: string, extra: string[]): { executable: string; args: string[] } {
    const dllCandidates = [
      path.join(this.projectRoot, 'Tools', 'Content.Compiler', 'bin', 'Release', 'net8.0', 'Content.Compiler.dll'),
      path.join(this.projectRoot, 'Tools', 'Content.Compiler', 'bin', 'Debug', 'net8.0', 'Content.Compiler.dll'),
    ]
    const dll = dllCandidates.find((candidate) => fs.existsSync(candidate))
    if (dll) return { executable: 'dotnet', args: [dll, command, this.databasePath, ...extra] }
    return {
      executable: 'dotnet',
      args: ['run', '--project', path.join(this.projectRoot, 'Tools', 'Content.Compiler', 'Content.Compiler.csproj'), '--', command, this.databasePath, ...extra],
    }
  }

  /** 对数据库当前快照执行发布级校验并返回所有可定位问题。 */
  async validate(version = 'development'): Promise<OperationResult> {
    const invocation = this.command('validate', [version])
    const result = await runProcess(invocation.executable, invocation.args, this.projectRoot)
    return {
      succeeded: result.code === 0,
      message: result.code === 0 ? '整库校验通过。' : '整库校验发现阻断问题。',
      output: result.output,
      issues: parseIssues(result.output),
    }
  }

  /** 校验并发布指定版本到 Unity StreamingAssets 内容目录。 */
  async publish(version: string): Promise<OperationResult> {
    const cleanVersion = version.trim() || 'development'
    const outputRoot = path.join(this.projectRoot, 'Assets', 'StreamingAssets', 'Content')
    const invocation = this.command('publish', [outputRoot, cleanVersion])
    const result = await runProcess(invocation.executable, invocation.args, this.projectRoot)
    return {
      succeeded: result.code === 0,
      message: result.code === 0 ? `内容 ${cleanVersion} 已发布到 Unity。` : '发布被内容错误阻止。',
      output: result.output,
      issues: parseIssues(result.output),
    }
  }

  /** 原子切换 current.json 到已存在的历史发布版本。 */
  rollback(version: string): OperationResult {
    const cleanVersion = version.trim()
    if (!/^[A-Za-z0-9._-]+$/.test(cleanVersion)) return { succeeded: false, message: '版本号只能包含字母、数字、点、横线和下划线。' }
    const root = path.join(this.projectRoot, 'Assets', 'StreamingAssets', 'Content')
    const manifest = path.join(root, 'versions', cleanVersion, 'manifest.json')
    if (!fs.existsSync(manifest)) return { succeeded: false, message: `发布版本不存在：${cleanVersion}` }
    const target = path.join(root, 'current.json')
    const temporary = `${target}.tmp`
    fs.writeFileSync(temporary, JSON.stringify({ contentVersion: cleanVersion, manifestPath: `versions/${cleanVersion}/manifest.json` }, null, 2))
    fs.renameSync(temporary, target)
    return { succeeded: true, message: `已回退到内容版本 ${cleanVersion}。` }
  }
}
