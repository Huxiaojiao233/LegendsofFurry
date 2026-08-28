import fs from 'node:fs'
import path from 'node:path'
import { createRequire } from 'node:module'
import { defineConfig, type Plugin } from 'vite'
import vue from '@vitejs/plugin-vue'

const require = createRequire(import.meta.url)

function copyBlocklyMedia(): Plugin {
  const copy = () => {
    const pkg = path.dirname(require.resolve('blockly'))
    const dest = path.resolve(__dirname, 'public/blockly-media')
    fs.mkdirSync(dest, { recursive: true })
    fs.cpSync(path.join(pkg, 'media'), dest, { recursive: true })
  }
  return { name: 'copy-blockly-media', buildStart: copy, configureServer: copy }
}

export default defineConfig({
  plugins: [vue(), copyBlocklyMedia()],
  base: './',
  server: { host: '127.0.0.1', port: 5173, strictPort: true },
  build: { outDir: 'dist' },
})
