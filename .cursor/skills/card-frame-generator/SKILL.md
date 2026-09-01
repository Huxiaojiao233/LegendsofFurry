---
name: card-frame-generator
description: Generates Legends of Furry card frame assets for Tools/LOFE_CardMaker. Use when an artist describes a card border, 边框, ElementFrame overlay, Character ribbon, Type logo, or Photoshop blend mode for the 500x700 card maker.
---

# 卡牌边框生成

为 `Tools/LOFE_CardMaker` 生成素材。底板永远是 **basic.png**。元素边框叠在 basic 上，混合模式写进 `blends.json`。文字由编辑器绘制，PNG 里不要烤卡名/费用/描述。

## 输出目录

```
Tools/LOFE_CardMaker/public/frames/
  basic.png                      # 5:7，插画窗透明
  ElementFrame/
    blends.json
    火.png
    水.png
    ...
  Character/
    普通.png
    诅咒.png
    ...
  Type/
    伤害.png
    诅咒.png
    ...
```

推荐整卡 **1847×2590** 或任意 500×700 倍数。品质飘带切成 **1575×276**。类型 logo 约 **203×210**，透明底。

## blends.json

键是元素文件名（无扩展名），值是 Photoshop 混合模式（中英文均可）：

```json
{
  "default": "hard-light",
  "火": "hard-light",
  "草": "lighten",
  "水": "hard-light",
  "雷": "lighter-color",
  "冰": "screen",
  "岩": "hard-light",
  "风": "screen",
  "光": "lighten",
  "暗": "hard-light"
}
```

编辑器支持：正常、正片叠底、滤色、叠加、变暗、变亮、颜色加深/减淡、线性加深/减淡、强光、柔光、亮光、线性光、点光、实色混合、深色、浅色、差值、排除、减去、划分、色相、饱和度、颜色、明度。

残缺 PSD 实测：火/水/岩/暗 = 强光；草/光 = 变亮；冰/风 = 滤色；雷 = 浅色。

## 槽位（1847×2590）

见 [reference.md](reference.md)。插画窗 `{ x: 70, y: 58, w: 1722, h: 1626 }`。

## 流程

1. 按用户描述出 basic 底板，插画窗挖空。
2. 元素 overlay 与 basic 同尺寸，写 `blends.json`。
3. 品质飘带放到 `Character/`，文件名即选项（普通/稀有/史诗/传说/诅咒/事件）。
4. 类型 logo 放到 `Type/`。
5. 重启 `pnpm dev` 或重新 build，前端会枚举新文件。

## 检查清单

- [ ] basic 插画窗透明，不是黑底
- [ ] 元素 overlay 与 basic 对齐
- [ ] blends.json 有对应条目
- [ ] PNG 内无卡名/费用/描述文字
- [ ] 比例 5:7
