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

**关键：** Writer 的样式通路已够用（`SetFontSize`、容器 `TextStyle`、TextBlock 字号都在），
缺的只是上游赋值。

**唯一例外：`Theme.Background` 必须改 Writer/Reader** —— 该字段当前设置后从不落盘，
修复需要真正写入 `PageSheet/PageBackground` 并读回。

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

## 4. 十二项缺陷的修法

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
| `src/Vsdx.Adapters/OfficeImoDocumentWriter.cs` | **写页面背景**（仅此一处） | 中（触碰保真层） |
| `src/Vsdx.Adapters/OfficeImoDocumentReader.cs` | **读回页面背景** | 中 |
| `schemas/phase7/design-spec.schema.json` | 枚举 4 个 palette 名 | 低 |
| `skill/SKILL.md` | 新增 palette 与样式优先级说明 | 低 |
| `docs/phase8/capability-matrix.md` | 更新样式相关行 | 低 |

**不触碰：** `FidelityGuard` / `Visio*Bridge` / 协议版本号。

**需触碰（初稿遗漏）：** Writer/Reader 各一处（背景色）、四个布局引擎（Direction）。

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

---

## 11. T0 的实现细节：Transpose 必须覆盖全部空间数据

### 11.1 现有实现只覆盖一半

`CandidateComparison.cs:141` 已有 `Transpose(Diagram)`，处理了：

```csharp
node.Bounds  X/Y 与 W/H 互换        ✅
group.Bounds X/Y 与 W/H 互换        ✅
edge.Waypoints = null（清空）       ✅
page.Size W/H 互换                  ✅
```

**但它遗漏了以下空间数据，直接复用会产生错误几何：**

| 遗漏项 | 位置 | 后果 |
| --- | --- | --- |
| `Page.TextBlocks` | `Page.TextBlocks[].Bounds` | 标题位置不转，跑到页面外 |
| `Page.Paths` | `Page.Paths[].Bounds` | 分隔线位置与宽高不转 |
| **`FreePath.Geometry` 内部坐标** | `PathRow.Cells["X"/"Y"]` | **路径形状本身不转**——框转了、线没转 |
| `Node.Angle` / `FreePath.Angle` | 旋转角 | 角度未镜像，图形朝向错误 |
| `Edge.FromPoint` / `ToPoint` | 自由端点 | 端点留在原坐标 |
| `Edge.Waypoints` | 折点列表 | 当前是**清空**而非转置 |

### 11.2 Waypoints 应当转置而非清空

现有实现选择清空，理由是"旧 waypoints 描述的是转置前的几何"。

**这在 `CandidateComparison` 场景下可接受**（后续会重新路由），
**但在 `Direction` 场景下不可接受**：用户显式给出的 waypoints 是设计意图，
清空等于丢弃输入——违反任务 §4.10"不允许静默丢弃输入"。

**T0 应：** 转置 `labelPosition` 之外的全部坐标点，包括 waypoints 与自由端点。

### 11.3 几何内部坐标必须镜像

`FreePath.Geometry` 的 `PathRow.Cells` 存的是**局部坐标**（相对 box）。
当 box 的 W/H 互换时，局部 X 坐标范围也变了，必须相应交换 X/Y 单元格：

```
前面: MoveTo(0,0) → LineTo(8,0)        // 水平线
转置: MoveTo(0,0) → LineTo(0,8)        // 竖直线
```

**只换 Bounds 不换 Cells，会得到一条"框转竖了、线还是横的"错误图形。**

### 11.4 Manual 模式不得转置

任务要求"Manual 模式必须保留用户坐标"。

`ManualLayoutEngine` 的职责就是"什么都不做"，因此 `Direction` 在 manual 下
**不应生效**，且必须进 `unmetConstraints` 说明"manual 保留作者坐标，direction 未应用"。

### 11.5 提取位置

移到 `DiagramLayout` 作为 `Transpose(Page)`，`CandidateComparison` 改为调用，
避免两份实现漂移。签名：

```csharp
/// <summary>Mirrors every spatial value on the page across the X=Y diagonal.</summary>
public static void Transpose(Page page);
```

### 11.6 验收

| # | 断言 |
| --- | --- |
| 1 | 四策略 × 两方向：`xspread`/`yspread` 互换 |
| 2 | TextBlock 位置跟随转置 |
| 3 | Path Bounds 与**内部 Geometry 坐标**同时转置 |
| 4 | Waypoints 逐点转置（非清空） |
| 5 | 自由端点 `FromPoint`/`ToPoint` 转置 |
| 6 | `Angle` 镜像 |
| 7 | Manual 模式不转置，且 `unmetConstraints` 有记录 |
| 8 | 转置两次回到原状态（幂等性反向验证） |

---

## 12. 字号必须早于布局确定（T0.5）

### 12.1 问题

审计 §2.2c 实测：`baseSize` 24 时框体仍是 2.00"，文字溢出 2.5 倍。

**根因是步骤顺序：**

```csharp
139: DiagramLayoutEngine.Apply(...);   // 按当前字号算位置
166: DiagramLayout.Apply(...);        // 按成员反算容器
168: // Typography 在这里才改字号
178: node.Style.FontSize = clamped;   // 框体已经定了
```

**字号 → 文字占位 → 框体尺寸**。先定尺寸再改字号，尺寸必然失效。

### 12.2 修法

样式解析（含字号）必须在 `DiagramSizer.Apply` **之前**完成：

```
DesignSpec → StyleResolver → 字号写入模型 → DiagramSizer.Apply → 布局 → 写入 VSDX
                              ↑
                     必须在这里，不能更晚
```

这也是 `StyleResolver` 必须**前置**而非"事后接上 palette"的原因。

### 12.3 验收（独立项）

| # | 断言 |
| --- | --- |
| 1 | `baseSize` 9→24，框体尺寸必须随之增大 |
| 2 | 增大后 `text.overflow-*` 为零 |
| 3 | 字号在 `DiagramSizer.Apply` 之前已写入模型（可用探针断言） |

**T0.5 与 T1 有依赖：** T1 的解析器若不前置，T0.5 无法通过。
因此实施顺序为 **T0 → T0.5 → T1**，但 T0.5 的验收独立于 T1。

---

## 13. BaseSize 语义修正（依负责人 D3）

### 13.1 当前错误

源码注释写明 `Base node-title size in points`，但实现是：

```csharp
node.Style.FontSize = Math.Max(baseSize, MinReadableSize);   // 所有节点同值
```

**注释说"node-title 基准"，实现却抹平了所有层级。**

### 13.2 正确语义

`BaseSize` = **`NodeTitle` 的目标绝对字号**，作为整体缩放基准：

```csharp
double scale = baseSize / TypeScale.NodeTitle;      // 例如 14 / 11 ≈ 1.27

// 应用到全部档位，保留层级关系
double For(double tier) => tier * scale;
```

| 档位 | 默认 | baseSize=14 时（scale=1.2727） |
| --- | --- | --- |
| DiagramTitle | 20 | 25.45 |
| RegionTitle | 12 | 15.27 |
| NodeTitle | 11 | **14.00**（基准点，恒等于 baseSize） |
| NodeCaption | 10 | 12.73 |
| EdgeLabel | 9 | 11.45 |
| Annotation | 9 | 11.45 |

数值由 `python3` 逐档核算，非手算。**校验点：`NodeTitle * scale` 必须精确等于 `baseSize`。**

**层级比例保持不变，整体缩放。** 这正是任务 §3.3 要求的"不能只给一个全局 FontSize"。

### 13.3 显式字号与下限

```csharp
最终字号 = ObjectStyle.FontSize                        // 显式优先
        ?? For(TypeScale[textRole])                    // 否则按角色
        → max(result, MinReadableSize)                 // 最后夹紧
        → 若被夹紧，进 unmetConstraints（修死代码）
```

### 13.4 验收

| # | 断言 |
| --- | --- |
| 1 | `baseSize=14` 时 NodeTitle 恰为 14，且 title > region > node > label |
| 2 | 缩放后层级**比例**与原 TypeScale 一致 |
| 3 | 显式 `fontSize` 覆盖缩放结果 |
| 4 | 低于 `MinReadableSize` 时被提升**且**进 unmet |
| 5 | 字号在布局前确定（见 §12） |

---

## 14. ObjectMapping 契约（依负责人 D2）

### 14.1 决策

**保持裸 ID 格式**，前提是 IR 对象 ID 在**整个 IR 范围内全局唯一**（跨页面、跨类型）。

### 14.2 必须做的事

| # | 事项 |
| --- | --- |
| 1 | **核查** `IrValidator` 当前是否只做页内唯一性检查 |
| 2 | 若仅页内唯一 → **补全局唯一性校验**（`ir.id-duplicate-global`） |
| 3 | 覆盖全部页面与 Node / Container / Edge / TextBlock / Path |
| 4 | 加测试断言：两页各有同名 id 时校验拒绝 |

**若全局唯一成为协议保证**，则裸 ID 可安全用作键，无需改格式，
也避免"加页面后既有键格式改变"。

**若无法保证**，则退回结构化方案：`ObjectMappings` 列表含
`pageId / objectId / objectKind / modelId`，并保留现有 `ObjectMapping`
作为兼容投影。**不采用动态混合键。**

### 14.3 实测：跨页面 ID 唯一性**当前不成立**

已实测，结论明确：

```console
$ execute dup.ir.json     # 两页各有 id="same"
  status: partial
  errors: []

$ export-model → 
  page1 nodes: [('same', 'same')]
  page2 nodes: [('same', 'same')]
```

**跨页面重复 id 未被拒绝，且两页都保留了 `same`。**

代码原因：`IrValidator.ValidatePage()` 内的 `nodeIds` 是**局部变量**
（`ProtocolRunner.cs:238`），每页新建一个，页间不共享。

**后果：** 裸 ID 作 `ObjectMapping` 键会互相覆盖——第二页的 `same`
会盖掉第一页的 `same`。

### 14.4 因此 T7 必须分两步

| 步 | 内容 |
| --- | --- |
| T7a | **补全局唯一性校验**：跨页面 + 跨对象类型的 id 重复 → `ir.id-duplicate-global` |
| T7b | 在 T7a 成立的前提下，`ObjectMapping` 用裸 ID 覆盖全部页面与五类对象 |

**T7a 是 T7b 的前置条件。** 若 T7a 因兼容性原因不能加（旧 IR 可能已有跨页重复），
则退回结构化 `ObjectMappings` 方案，**不采用动态混合键**（依负责人 D2）。

**T7a 的兼容性影响需评估：** 现有 IR 若存在跨页重复 id，加上校验后会从
`partial` 变成 `failed`。这属修 bug，但须在报告中披露。
