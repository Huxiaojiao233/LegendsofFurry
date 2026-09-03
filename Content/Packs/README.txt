离线内容包投放目录
====================

内容包制作工具（LOFE_ContentKit）与 Unity 工程完全分开。
把工具导出的 .lofepackage 文件拷到这里，例如：

  Content/Packs/lofe_core.lofepackage
  Content/Packs/planner_pack.lofepackage

.lofepackage 是 zip：内含 pack.json、按种类拆开的 catalog 切片，以及 assets/。

Unity 编辑器 Play 和正式游戏启动时都会扫描本目录下的 *.lofepackage。
玩家安装目录同样使用：<游戏.exe 所在文件夹>/Content/Packs/

StreamingAssets/Content 只保留引擎空桩；卡牌、职业、卡图都来自这里的包。
不要手改包内 JSON；请用内容包制作工具打开工程后「导出内容包」。
