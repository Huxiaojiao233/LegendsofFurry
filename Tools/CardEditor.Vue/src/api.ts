import type { CardEditorApi } from './types'

/** 返回 preload 暴露的类型安全 API，并在普通浏览器误开时给出明确错误。 */
export function useCardEditorApi(): CardEditorApi {
  if (!window.cardEditor) throw new Error('卡牌维护工具必须通过 Electron 启动。')
  return window.cardEditor
}
