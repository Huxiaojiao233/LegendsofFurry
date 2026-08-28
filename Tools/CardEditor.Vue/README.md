# Electron 内容包编辑器

这是原 WPF 工具的 Vue 3 + Element Plus + Electron 版本。旧的 .NET 工具已废弃。应用直接访问 Unity 项目中的 `ContentSource/lof-content.db`，不启动 HTTP 服务。

## 编辑能力

- 左侧卡牌列表可按**类别 / 卡池 / 稀有度**分组。
- **积木**：Blockly（Zelos / Scratch 外观）行为编辑。时机帽子、效果、如果/则/否则、重复、挨个；保存仍为原来的行为树 JSON。
- **源码**：同一套行为的嵌套 JSON，或整卡 JSON；可写回积木视图。
- 保存、整库校验、发布到 Unity 仍走原来的编译器。

## 技术结构

- `src`：Vue 3 + Element Plus 界面，只依赖统一的 `CardEditorApi`。
- `electron/preload.ts`：安全 IPC 适配层。
- `electron/services`：可复用的 SQLite、卡面、备份、校验和发布服务。
- `electron/main.ts`：Electron 桌面窗口与 IPC 注册。

## 开发与构建

```powershell
pnpm install
pnpm dev
pnpm test
pnpm build
pnpm package:win
```

开发模式从当前目录向上定位 Unity 工程，也可以通过 `LOF_PROJECT_ROOT` 指定工程根目录。

## 打成策划可脱机使用的程序

本机需要 Node.js 和 pnpm（只在打包的人电脑上）。策划电脑不需要。

```powershell
cd Tools\CardEditor.Vue
pnpm install
pnpm package:win
```

产物是文件夹，不是单独一个文件：

`Tools/CardEditor.Vue/release-v2/win-unpacked/LOF.CardEditor.exe`

这个 exe 必须能找到带 `Assets` 和 `ContentSource` 的工程根目录。在本仓库里可直接双击 `Tools/启动卡牌维护工具.cmd`。

若要把工具拷到没有工程的策划电脑：双击 `Tools/打包内容编辑器给策划.cmd`（内部调用 `pack-planner-kit.ps1`）。生成 `Tools/PlannerOfflineKit/`。把整个文件夹拷走，双击 `StartEditor.cmd`。
