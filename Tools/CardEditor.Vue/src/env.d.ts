/// <reference types="vite/client" />

import type { CardEditorApi } from './types'

declare global {
  interface Window { cardEditor: CardEditorApi }
}

export {}
