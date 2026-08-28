/** 把 Vue 响应式对象变成可走 IPC 的纯 JSON，避免 structuredClone 对 Proxy 抛错。 */
export function cloneForIpc<T>(value: T): T {
  return JSON.parse(JSON.stringify(value)) as T
}
