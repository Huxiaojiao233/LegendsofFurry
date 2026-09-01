离线内容包投放目录
====================

内容包制作工具（LOFE_ContentKit）与 Unity 工程完全分开。
把工具导出的整个包文件夹拷到这里，例如：

  Content/Packs/lofe_core/pack.json
  Content/Packs/lofe_core/catalog.json
  Content/Packs/lofe_core/assets/...

Unity 编辑器 Play 和正式游戏启动时都会扫描本目录的直接子目录。
玩家安装目录同样使用：<游戏.exe 所在文件夹>/Content/Packs/

StreamingAssets/Content 只保留引擎空桩；卡牌、职业、卡图都来自这里的包。
不要手改 catalog.json；请用内容包制作工具的「导出离线内容包」。
