# Phase 8.2 实施计划

**依据：** `phase8.2-audit.md`（Stage 1 交付物）
**状态：** Stage 2 交付物，**尚未编码**
**日期：** 2026-11-17

---

## 1. 目标架构

### 1.1 一句话

引入**一个**样式解析入口 `StyleResolver`，把四处互不相识的 if 合并成一条优先级明确的管线，
让 Node / Group / Edge / TextBlock / Background 共用同一条路径。

### 1.2 数据流

```
DesignSpec
  ├─ Theme.Palette ─────┐
  ├─ Theme.RoleColors ──┤
  ├─ Theme.Background ──┼──▶ StyleResolver ──▶ ResolvedStyle ──▶ 模型对象
  └─ Typography ────────┘         ▲                                    │
                                  │                                    ▼
                          TypeScale（角色字号）              VSDX Writer（已有能力）
                                                                       │
                                                                       ▼
                                                              Reader → Renderer
```

**关键：** Writer 不改。审计已确认 `SetFontSize`、容器 `TextStyle`、TextBlock 字号通路都在，
缺的只是上游赋值。

### 1.3 为什么不是一个"万能样式系统"

任务 §1.3 禁止"为抽象而抽象"。`StyleResolver` 的立项依据是审计 §3 的四个**实际缺陷**：

1. 新增 palette 只能是第 5 处 if
2. 优先级取决于代码顺序，无法表达
3. Typography 只处理 Node（第一处 if 写的）
4. `typography.min_size` 报告只在 Node 分支里，且判断写反成死代码

**若没有这四条，不应新增此类。**

---

## 2. 样式优先级（本阶段固定契约）

任务 §3.1 建议：`Palette Defaults → Role Overrides → Explicit Object Styles`

### 2.1 精确规则

```
最终样式 = Palette[role]                    ← 第 1 层：调色板按角色给色
          → RoleColors[role]                ← 第 2 层：spec 的角色级覆盖
          → 对象显式 Style 字段（非 null）   ← 第 3 层：调用方显式指定
```

**字号独立于颜色，走 TypeScale：**

```
最终字号 = ObjectStyle.FontSize            ← 显式优先
          ?? TypeScale[textRole]           ← 否则按文本角色
          → max(result, MinReadableSize)   ← 最后夹紧，且夹紧时报告
```

### 2.2 三层各自"是否覆盖"的判据

| 层 | 覆盖条件 |
| --- | --- |
| Palette | 总是提供基础值；角色未定义时用 `default` |
| RoleColors | 仅当 `spec.Theme.RoleColors` 含该 role |
| Object Style | 仅当字段**非 null**（不是空字符串） |

**这一条要写进测试**：显式 `fill=""` 与 `fill=null` 必须区分，
否则"未设置"与"设为空"会被混为一谈。

### 2.3 文本角色到 TypeScale 档位

| 对象 | 文本角色 | TypeScale 档位 |
| --- | --- | --- |
| TextBlock role=title | DiagramTitle | 20 |
| TextBlock role=subtitle | RegionTitle | 12 |
| Group 标题 | RegionTitle | 12 |
| Node 主标题 | NodeTitle | 11 |
| Node 第二行 | NodeCaption | 10 |
| Edge 标签 | EdgeLabel | 9 |
| TextBlock role=legend | NodeCaption | 10 |
| TextBlock role=note | Annotation | 9 |

**不新增 TypeScale 字段**——现有 6 档 + role 映射已覆盖任务 §3.3 的 8 个层级。

---

## 3. Palette 设计

### 3.1 四套命名调色板

每套提供**色彩 token**，不是完整模板：

```csharp
record Palette(
    string Background,
    IReadOnlyDictionary<string, string> RoleFills,   // service/database/gateway/client/queue/...
    string ContainerFill, string ContainerStroke, string ContainerText,
    string EdgeStroke, string EdgeLabelText,
    string TextPrimary, string TextSecondary)
```

| Palette | 定位 |
| --- | --- |
| `enterprise` | 沉稳蓝灰，高对比，适合正式文档 |
| `office` | 明亮饱和，接近 Office 默认色板 |
| `technical` | 深色低饱和，适合工程图 |
| `muted` | 低对比柔和，适合印刷 |

### 3.2 硬约束（来自任务 §3.2）

1. 不同 palette 必须产生**真实可观察差异** —— 验收用 PNG 目视，不用 JSON 比对
2. **不只是背景色** —— 必须影响角色色、容器色、边框、文本色
3. 未知 palette **必须**进 `UnmetConstraints`
4. **不得写成固定模板** —— palette 只给颜色，不决定布局、形状或字号

### 3.3 未知 palette 的行为

```csharp
if (!Palettes.TryGet(spec.Theme.Palette, out var palette))
{
    unmet.Add(new UnmetConstraint {
        Code = "theme.palette-unknown",
        Reason = $"未知调色板 '{spec.Theme.Palette}'，可用：{known}"
    });
    // 回退到默认调色板继续执行，但结果标记为未满足
}
```

**回退而不中止**，因为样式缺失不应阻断图形交付；但**必须报告**。

---

## 4. 七项缺陷的修法

| # | 缺陷 | 修法 |
| --- | --- | --- |
| 0 | **`Direction` 四策略全无效（P0）** | 各引擎读 `options.Direction`，`TopToBottom` 转置轴 |
| 1 | Palette 未执行 | `StyleResolver` 第 1 层；未知值报告 |
| 2 | Typography 仅 Node | 所有对象经同一解析器；容器/Edge/TextBlock 接入 |
| 3 | 容器色硬编码 | 移除 `bandFills` 数组，改由 palette 提供 |
| 4 | `Canvas.Unit` 不换算 | 按单位换算到英寸后再写入；**不再写错标签** |
| 5 | `containerLayouts` manual 下静默跳过 | 进 `unmetConstraints`，`Reason` 说明 manual 模式不重排 |
| 6 | `ObjectMapping` 覆盖不全 | 覆盖 Edge/TextBlock/Path + 多页；键改为 `pageId/objectId` |
| 7 | `typography.min_size` 死代码 | 判断改为 `clamped > size`；容器/Edge/TextBlock 也走此路径 |
| 8 | `Theme.Background` 不落盘 | Writer 写 `PageSheet/PageBackground`；Reader 还原 |

### 4.1 ObjectMapping 的键格式（需负责人确认）

当前 `Dictionary<string,string>`，键为裸 id。

**提议：** 单页时保持裸 id（**向后兼容**），多页时用 `pageId/objectId`。
理由：现有调用方按裸 id 取值，直接改格式会破坏兼容性（违反任务 §4.9）。

**若同一页内 id 重复**（IR 已在校验层拒绝），故无需处理。

---

## 5. 文件级改动计划

| 文件 | 改动 | 风险 |
| --- | --- | --- |
| `src/Vsdx.Protocol/StyleResolver.cs` | **新增** —— 解析器 + Palette 定义 | 低 |
| `src/Vsdx.Protocol/DesignSpec.cs` | `Apply()` 改为调用解析器；移除硬编码容器色；修 unit 换算；修 min_size 判断；containerLayouts 报告 | **中**（核心改动） |
| `src/Vsdx.Protocol/ExecutionResult.cs` | `BuildMapping` 覆盖全对象 + 多页 | 低 |
| `src/Vsdx.Engine/LayeredLayoutEngine.cs` 等四个引擎 | 读 `options.Direction`，实现转置 | **中**（触碰已验证布局） |
| `src/Vsdx.Adapters/OfficeImoDocumentWriter.cs` | 写页面背景（**唯一需改 Adapter 处**） | 中（触碰保真层） |
| `src/Vsdx.Protocol/DiagramIr.cs` | 无需改动（字段已够） | — |
| `src/Vsdx.Adapters/*` | **不改动** | — |
| `schemas/phase7/design-spec.schema.json` | 枚举 4 个 palette 名 | 低 |
| `skill/SKILL.md` | 新增 palette 与样式优先级说明 | 低 |
| `docs/phase8/capability-matrix.md` | 更新样式相关行 | 低 |

**不触碰：** Writer / Reader / FidelityGuard / LayoutStrategies / 协议版本号。

---

## 6. 测试计划

### 6.1 优先级（任务 §5.2 要求"每项独立测试"）

| 测试 | 断言 |
| --- | --- |
| `PaletteProvidesDefaults` | 无 roleColors 时节点取 palette 色 |
| `RoleColorsOverridePalette` | roleColors 覆盖 palette 同角色 |
| `ObjectStyleOverridesEverything` | 显式 fill 覆盖 roleColors |
| `NullVsEmptyStringDistinguished` | `fill=null` 走继承，`fill=""` 视为显式空 |
| `ExplicitFontSizeBeatsTypeScale` | 显式字号优先 |
| `MinReadableClampsAndReports` | 低于下限被提升**且**进 unmet（修死代码） |

### 6.2 覆盖范围

| 测试 | 断言 |
| --- | --- |
| `TypographyReachesAllObjectKinds` | Node/Group/Edge/TextBlock 四种都有 font+size |
| `EdgeLabelUsesTypeScale` | Edge 标签字号来自 EdgeLabel 档 |
| `ContainerTitleUsesRegionTitle` | 容器标题来自 RegionTitle 档 |
| `ContainerColorsComeFromPalette` | 换 palette 后容器色改变 |
| `BackgroundFromPalette` | palette 决定背景 |

### 6.3 协议契约

| 测试 | 断言 |
| --- | --- |
| `UnknownPaletteIsReported` | 进 `unmetConstraints`，`isHard=false` |
| `ManualLayoutReportsContainerLayouts` | manual 下进 unmet |
| `ObjectMappingCoversAllKinds` | Edge/TextBlock/Path 都在 |
| `ObjectMappingCoversAllPages` | 两页都有条目 |
| `UnitConversionIsCorrect` | cm→英寸数值正确，标签与数值一致 |

### 6.4 真实产物

生成**三套** DesignSpec × 同一 IR 的 VSDX + PNG，人工目视（任务 §5.3/§5.6）：

| 产物 | IR |
| --- | --- |
| 分层架构图 | `benchmarks/architecture/complex.json` |
| 流程图 | `benchmarks/workflow/complex.json` |
| 综合图 | `examples/phase8/slice.json`（含标题/容器/连线标签/注释） |

### 6.5 回归

- 373 项原测试全过
- 18 基准硬约束全过
- 0 新增 warning

---

## 7. 兼容性

| 项 | 处理 |
| --- | --- |
| 未声明 `theme` 的旧 spec | 行为**不变**（解析器在无 palette 时返回当前默认值） |
| 已声明 `roleColors` 的旧 spec | 效果不变（新路径给出相同结果），需回归验证 |
| `Canvas.Unit` | **行为改变**：以前写错标签，现在换算正确。属修 bug，需在报告说明 |
| `ObjectMapping` | 单页保持裸 id，兼容 |
| 协议版本 | 不变（1.0）——只加执行，不加字段 |

**两处破坏性变更：`Canvas.Unit` 与 `Direction`。**

`Direction` 之前完全不生效，修好后**旧 spec 若声明了 `topToBottom`，产出会真的转置**——
这是修 bug，但会改变既有产物外观，必须明说并在报告中列出。

**`Canvas.Unit`**： 现状是"标签写 cm、数值仍按英寸"，
修正后数值会变。这是修 bug，但必须明说。

---

## 8. 风险

| # | 风险 | 缓解 |
| --- | --- | --- |
| R1 | 统一解析改变既有产物外观 | 逐项对比改动前后 PNG；若变化属"修复"则记录，若属"回归"则回退 |
| R2 | Palette 被做成固定模板 | palette 结构里**没有**布局、形状、字号字段——只有颜色 |
| R3 | 三层优先级实现成四层 | 测试固定每层，尤其 `null` vs `""` |
| R4 | 多页 ObjectMapping 破坏兼容 | 单页保持裸 id |
| R5 | 眼神验收流于形式 | 必须提交 PNG 且逐张写出观察，不以 `check-layout` 分数替代 |

---

## 9. 子任务与验收标准

| # | 子任务 | 验收 |
| --- | --- | --- |
| T0 | 修 `Direction` | 四策略 × 两方向，`xspread`/`yspread` 必须互换 |
| T1 | `StyleResolver` + 4 套 palette | 单元测试证明三层优先级；palette 只有颜色字段 |
| T2 | 接入 Node / Group / Edge / TextBlock | 四种对象都拿到 font+size+color |
| T3 | 移除容器硬编码色 | 换 palette → 容器色变（PNG 可见） |
| T4 | 修 `Canvas.Unit` | cm/mm 换算正确，标签与数值一致 |
| T5 | 修 `containerLayouts` 报告 | manual 下进 unmet |
| T6 | 修 `min_size` 死代码 | 低于下限时进 unmet |
| T7 | `ObjectMapping` 全对象 + 多页 | 两页 × 四类对象全覆盖 |
| T8 | 三套产物 + PNG + 目视 | 提交 VSDX/PNG/spec，附观察记录 |
| T9 | 全量回归 | 373 测试 + 18 基准 + 0 warning |
| T10 | `Theme.Background` 落盘 + 回读 | 写 `#FAFAFA` → 读回同值 |

**每个 T 独立提交、独立测试，不合并未验证的大改动**（任务 §7 Stage 5 要求）。

---

## 10. 明确不做（任务 §6）

外部 Stencil、MasterCatalog/AssetResolver、图片/SVG 嵌入、复合图形、
多区域协调、通道预留、完整 check-fidelity、六族终验、Word OLE、GUI。

**L4 不作为阻塞条件**（任务 §5.7）；L1/L2/L3 必须持续通过。
