# 配色对比度实测（2026-11-18）

用 **WCAG 2.1 相对亮度公式**测量四套 palette 的每一对「填充色 / 文字色」。
文字色由 `ColorUtil.ReadableTextColor` 决定（引擎自动选黑或白）。

阈值参考：WCAG AA 正文 **4.5:1**，AA 大字（18pt 或 14pt 粗）**3.0:1**。

## 一、结论

| 类别 | 最低比 | 最高比 | 是否达标 |
| --- | --- | --- | --- |
| 容器带（BandColour 自带 text） | **6.56** | 16.13 | ✅ 全部 ≥ AA |
| 默认填充（无角色） | **18.88** | 18.88 | ✅ |
| 角色填充 | **4.36** | 16.13 | ⚠️ 3 个低于 AA |

**40 个角色色中 37 个达到 AA，3 个达到 AA-large 但未达 AA。**

## 二、未达 AA 的三个

| palette | 角色 | 填充 | 文字 | 对比度 |
| --- | --- | --- | --- | --- |
| enterprise | database | `#C55A11` | `#111111` | **4.36** |
| enterprise | queue | `#C55A11` | `#111111` | **4.36** |
| enterprise | storage | `#C55A11` | `#111111` | **4.36** |

三者共用同一颜色 `#C55A11`（深橙），差 **0.14** 到 AA。

`#111111` 已是引擎能选的最深文字色（另一个选项 `#FFFFFF` 在同一背景上只有 4.0:1，更差）。
所以**要提高只能改填充色**。

## 三、四套 palette 的完整数据

### enterprise

| 角色 | 填充 | 文字 | 比 | 等级 |
| --- | --- | --- | --- | --- |
| actor | #2E75B6 | #FFFFFF | 4.84 | AA |
| client | #2E75B6 | #FFFFFF | 4.84 | AA |
| database | #C55A11 | #111111 | 4.36 | AA-large |
| decision | #F2B705 | #111111 | 10.39 | AA |
| external | #9DC3E6 | #111111 | 10.23 | AA |
| gateway | #F2B705 | #111111 | 10.39 | AA |
| monitor | #7F7F7F | #111111 | 4.72 | AA |
| queue | #C55A11 | #111111 | 4.36 | AA-large |
| service | #70AD47 | #111111 | 6.98 | AA |
| storage | #C55A11 | #111111 | 4.36 | AA-large |

### midnight

| 角色 | 填充 | 文字 | 比 |
| --- | --- | --- | --- |
| actor / client | #1F3864 | #FFFFFF | 11.62 |
| database / queue / storage | #843C0C | #FFFFFF | 7.98 |
| decision / gateway | #BF8F00 | #111111 | 6.42 |
| external | #2F5597 | #FFFFFF | 7.32 |
| monitor | #404040 | #FFFFFF | 10.37 |
| service | #375623 | #FFFFFF | 8.34 |

**全部 ≥ AA。**

### muted

| 角色 | 填充 | 文字 | 比 |
| --- | --- | --- | --- |
| actor / client | #8496B0 | #111111 | 6.27 |
| database / queue / storage | #B08968 | #111111 | 5.96 |
| decision / gateway | #C9A96E | #111111 | 8.44 |
| external | #B8C4D4 | #111111 | 10.69 |
| monitor | #9A9A9A | #111111 | 6.71 |
| service | #8FA98B | #111111 | 7.39 |

**全部 ≥ AA。**

### technical

| 角色 | 填充 | 文字 | 比 |
| --- | --- | --- | --- |
| actor / client | #D9D9D9 | #111111 | 13.38 |
| database / queue / storage | #A6A6A6 | #111111 | 7.76 |
| decision / gateway | #FFC000 | #111111 | 11.50 |
| external | #EDEDED | #111111 | 16.13 |
| monitor | #7F7F7F | #111111 | 4.72 |
| service | #BFBFBF | #111111 | 10.27 |

**全部 ≥ AA。**

## 四、如果要把那三个提到 AA

`#C55A11` 需压到约 **#B04F0C** 才有 4.5:1。但这会改变 `enterprise` 中
数据类节点与其他节点的色彩关系，属于**设计决策**，不是纯技术修正。

三个可选做法：

1. **保持现状**：4.36 只差 0.14，标题类文字（大字）仍达标
2. **调深填充**：`#C55A11` → `#B04F0C`，但需重新审视整组配色
3. **改文字色**：`#111111` 已是极点，无空间

## 五、回归保证

`tests/Vsdx.Tests/PaletteContrastTests.cs`（14 项）持续验证：

- 每个 palette 的容器带 ≥ AA
- 每个 palette 的默认填充 ≥ AA
- 每个角色 ≥ AA-large
- **低于 AA 的角色恰好是那三个**（新增配色若越线会立即失败）
- 亮度公式本身用已知值校验（白/黑 = 21:1）
