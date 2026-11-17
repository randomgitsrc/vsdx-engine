# Phase 8.2 现状审计

**审计对象：** `vsdx-engine` @ `f947a3d`
**审计日期：** 2026-11-17
**基线：** 373 项测试通过，构建 0 warning
**性质：** Stage 1 交付物，**本阶段未修改任何产品代码**

---

## 0. 审计方法

任务要求"以实际源码为依据，不能仅依据旧文档推断"。

本审计对每个 `DesignSpec` 字段做**实际执行**：构造 IR → 应用 spec → 生成 VSDX →
`export-model` 读回 → 比对模型 JSON 的差异。差异来自几何而非样式，就判定为"未执行"。

**拒绝的三种证据：** 字段存在于类中、Schema 允许、执行无报错。
前两者只证明协议接受，第三者已被本项目自己的契约否定（"不能静默忽略"）。

---

## 1. DesignSpec 字段执行矩阵

| 字段 | 结论 | 证据 |
| --- | --- | --- |
| `Layout` | ✅ **已实现** | `layered/manual` 产生不同几何；`appliedDesignNotes` 有记录 |
| **`Direction`** | ❌ **完全未执行（原判 ✅，已更正）** | 见 §9 |
| `Spacing.NodeGapX` | ✅ 已实现 | 间距变大 → 图形变宽（Phase 7 已测） |
| `Spacing.BandGapY` | ✅ 已实现 | 同上 |
| `Canvas.Mode=auto` | ✅ 已实现 | `Recenter` + `FitPage(shrink)` |
| `Canvas.Mode=fixed` | ✅ 已实现 | 页面精确为请求值 |
| `Canvas.Width/Height` | ✅ 已实现 | 实测 10×8 生效 |
| **`Canvas.Unit`** | ❌ **未执行** | 见下 §2.4：`cm`/`mm` 与 `inches` 结果完全相同 |
| `Canvas.ShrinkToFit` | ✅ 已实现 | 分支存在且生效 |
| `Typography.FontFamily` | ⚠️ **仅覆盖 Node** | 见 §2.2 |
| `Typography.BaseSize` | ⚠️ **仅覆盖 Node** | 同上 |
| **`Typography.MinReadableSize`** | ⚠️ **部分** | 仅对 Node 生效；且被夹紧时**不报告** |
| **`ContainerLayouts`** | ❌ **manual 下静默跳过** | 见 §2.5 |
| `HardConstraints` | ✅ 已实现 | 8 类约束，未满足进 `unmetConstraints` |
| `SoftConstraints` | ✅ 已实现 | 同上，`isHard=false` |
| **`Theme.Palette`** | ❌ **完全未执行** | 见 §2.1 |
| **`Theme.Background`** | ❌ **未执行（原判 ✅，已更正）** | 见 §2.8 |
| `Theme.RoleColors` | ⚠️ **部分** | 仅作用于 Node；见 §2.2 |

**统计（二次更正）：17 项中 7 项已实现，4 项部分，6 项未执行。**

> **自我更正：** 初稿把 `Theme.Background` 判为"✅ 已实现"，理由是代码里有
> `diagram.Background = spec.Theme.Background`。但**只赋给模型字段不等于执行**——
> Writer 从不读取该字段（`grep "\.Background" src/Vsdx.Adapters/*.cs` 无结果），
> Reader 也不还原。实测写 `#FAFAFA` 后读回 `diagram bg: None`。
> 这正是本审计自己设立的判据所要排除的那类"看着像实现了"的结论。

---

## 2. 已确认的缺陷

### 2.1 【P0】`Theme.Palette` 完全未执行，且未知值不报告

**实测（同一 IR，两个 spec）：**

```console
$ vsdx-tool execute --ir base.ir.json --spec s.json --output palette.vsdx
  status : succeeded    ← 声称成功
  unmet  : 0
```

模型差异：

```
differences: 12
  /nodes[0]/bounds/x: 1.5 -> 1.85
  /nodes[2]/bounds/w: 1.3 -> 2
  ...（全部是几何）
```

**没有一处是视觉属性。** `fill` 在所有节点上都是 `#FFFFFF`，`background` 为 `None`。

更严重的是**未知 palette**：

```console
$ --spec '{"theme":{"palette":"does-not-exist"}}'
  status=succeeded  unmet=0        ← 静默接受
```

这违反了任务 §2.1 的硬要求："不允许出现第四种情况：协议接受、Schema 允许、执行时忽略、
结果又没有报告。" 当前 palette 正是第四种情况。

**根因：** `grep -rn "Palette" src/` 只有两处声明（`DesignSpec.cs:83`、`DiagramModel.cs:205`），
**无任何读取方**。

**加剧因素：Schema 也不设限。**

```json
"theme.palette": { "type": "string" }
```

既没有 `enum` 限定四个合法名，也没有 `pattern`。因此一个拼错的
`"enterpise"` 能通过 Schema、通过执行、拿到 `status=succeeded`，
调用方没有任何途径发现写错了。

**三方全部失守：Schema 不校验、执行不消费、结果不报告。**
修 8.2 时三处都要改——只加执行仍会让拼写错误静默通过。

### 2.2 【P1】Typography 只覆盖 Node

**实测**（`fontFamily=Courier New, baseSize=14`）：

| 对象 | font | size |
| --- | --- | --- |
| Node `a` | `Courier New` | 14 |
| Group `grp` | **None** | **12**（硬编码） |
| TextBlock `title` | **None** | 20（来自 role，非 spec） |
| Edge `e1` 标签 | **null** | **null** |

**关键点：写入器本身是支持的。**

```csharp
// OfficeImoDocumentWriter.cs:378
if (edge.LabelFontSize is double fs) connector.SetFontSize(fs);
```

缺的是 `DesignSpecApplier` 从不设置 `edge.LabelFontSize`：

```console
$ grep -c "LabelFont" src/Vsdx.Protocol/DesignSpec.cs
0
```

容器同理：`container.TextStyle.Size` 在写入器里被硬编码为 12。

**因此这不是"不能做"，是"没接线"。** 工作量小，但必须有统一解析，否则会成为第五处 if。

### 2.3 【P1】容器颜色硬编码，绕过 Theme

```csharp
// DesignSpec.cs:202-215
var bandFills = new[] { "#EAF3FB", "#EDF7ED", "#FDF2E9", "#F2E9F7", "#EAF6F6" };
var bandStrokes = new[] { "#9DC3E6", "#A9D08E", "#F4B183", "#C5A3D9", "#93CCCC" };
var bandText = new[] { "#1F4E79", "#375623", "#833C0B", "#4B1F6F", "#1F5C5C" };
...
group.Style.Fill ??= bandFills[slot];
```

容器色**完全不经过 Theme**。换 palette 后容器仍固定为这五组颜色。
这正是任务 §2.2-C 指出的问题。

### 2.4 【P1】`Canvas.Unit` 只写标签不换算

**实测：**

```
unit=inches -> page 10.00 x 8.00 in
unit=cm     -> page 10.00 x 8.00 in     ← 应为 3.94 x 3.15
unit=mm     -> page 10.00 x 8.00 in     ← 应为 0.39 x 0.31
```

代码只做了 `page.Size.Unit = spec.Canvas.Unit;`（第 256 行），
**没有任何换算**。`grep "UnitFactor\|2.54\|25.4" DesignSpec.cs` 无结果。

危险之处：`page.Size.Unit` 会被写成 `"cm"`，下游若按其解释会得到错误尺寸——
**标签与数值不一致**，比不写更糟。

### 2.5 【P1】`ContainerLayouts` 在 manual 下静默跳过

**实测：**

```console
$ --spec '{"layout":"manual","containerLayouts":{"grp":"grid"}}'
  applied: ['layout=manual', 'container.bands', 'canvas.auto']
  unmet  : []
```

`containerLayouts` 既没出现在 `applied`，也没进 `unmet` —— **第三种情况之外的第五种：被忽略且不留痕迹**。

根因：`DesignSpecApplier` 把 `containerLayouts` 放在 `if (layout != manual)` 分支内（第 139-160 行）。
manual 模式整体跳过该分支。

**注意：** manual 模式下"不重新布局"是合理的语义，但**必须报告**该覆盖未生效。

### 2.6 【P1】`ExecutionResult.ObjectMapping` 覆盖不全

**实测**（IR 含 2 节点 + 1 容器 + 1 连线 + 1 文本块）：

```console
mapping entries: ['a', 'b', 'grp']
```

**缺 `e1`（Edge）与 `title`（TextBlock）。**

```csharp
// ExecutionResult.cs — BuildMapping
foreach (var node in ir.Nodes) ...
foreach (var container in ir.Containers) ...
// 没有 edges，没有 textBlocks，没有 paths
```

且实现只看 `diagram.Pages.FirstOrDefault()`——**多页时第二页及以后完全不映射**。

任务 §2.2-F 明确要求："是否仅映射第一页、Node 和 Container，而未完整覆盖多页、Edge、
TextBlock、Path" —— **答案：是**。

**ID 冲突问题：** 当前 `Dictionary<string,string>` 用裸 id 作键。
若两页各有 `node-1`，后写者覆盖前者，且无任何提示。
任务要求"必须确定对象映射契约"。

### 2.7 【P2】`MinReadableSize` 夹紧时不报告

**实测**（`baseSize=4, minReadableSize=9`）：`unmet=[]`，无任何记录。

代码确实夹紧了（`Math.Max(size, MinReadableSize)`），但**静默修改调用方的输入**。
任务 §3.3 要求"不能偷偷缩小字号以制造通过结果" —— 反向同理：
把 4pt 悄悄提到 9pt 而不说，调用方以为 4pt 被接受了。

同一位置**确实有**报告代码，但它是**死代码**：

```csharp
var clamped = Math.Max(size, spec.Typography.MinReadableSize);
if (clamped < size)          // ← 永远为假
{
    unmet.Add(new UnmetConstraint { Code = "typography.min_size", ... });
}
```

`Math.Max(size, min) >= size` 恒成立，故 `clamped < size` 恒为假，
该分支**从不执行**。判断方向写反了（应为 `clamped > size`）。

**这是一个比"未报告"更隐蔽的缺陷**：代码看起来处理了这种情况，
静态阅读会以为功能存在，只有实际构造"低于下限"的输入才会发现它从不触发。

---

### 2.8 【P1】`Theme.Background` 设置了模型字段，但从不落盘

**实测：**

```console
$ --spec '{"theme":{"background":"#FAFAFA"}}'
  applied: [..., "theme.background"]     ← 声称已应用
$ export-model → diagram bg: None, page bg: None
```

代码确实执行了 `diagram.Background = spec.Theme.Background` 与
`page.Background ??= ...`（`DesignSpec.cs:240-244`），并记录 `theme.background`。

**但 Adapters 层从不读取这个字段：**

```console
$ grep -rn "\.Background" src/Vsdx.Adapters/*.cs
（无结果）
```

Writer 不写 `PageSheet/PageBackground`，Reader 不还原，因此：
**设置了、报告了、然后丢了。**

这比未实现更糟——`appliedDesignNotes` 明确告诉调用方"背景已应用"。

---

## 3. 一个共同的根因：`DesignSpecApplier` 是单点 if 集合

当前 `DesignSpecApplier.Apply()`（`DesignSpec.cs:100-290`）是一个约 190 行的方法，
样式逻辑分散在 4 处：

| 位置 | 处理 |
| --- | --- |
| ~185-230 | `Typography` → 直接改 `node.Style` |
| ~200-215 | 容器色硬编码数组 |
| ~224-238 | `theme.roleColors` → 直接改 `node.Style` |
| ~240-244 | `theme.background` |

**四处各自独立、互不知道对方。** 后果：

1. 新增 palette 只能是第 5 处 if
2. 无法表达优先级（谁覆盖谁取决于代码顺序）
3. Typography 只处理 Node，因为那是第一处 if 写的
4. `typography.min_size` 的 `unmet` 代码只在 Node 分支里，容器/Edge 从不经过

**结论：** 任务 §3.1 要求"建立统一的 Style Resolution 机制"不是抽象洁癖，
而是这四处 if 已经产生了上述四个实际缺陷。

---

## 4. 可复用部分（不要重写）

| 组件 | 可复用理由 |
| --- | --- |
| `TextLayout.TypeScale` | 已有 8 个语义档位（DiagramTitle/RegionTitle/NodeTitle/NodeCaption/EdgeLabel/Annotation…），**恰好覆盖任务 §3.3 要求的层级** |
| `ShapeStyler` | 已有样式写入逻辑，含 `SetFontSize` |
| `OfficeImoDocumentWriter` | Edge 标签字号（`:378`）、TextBlock 字号、容器 TextStyle **均已支持**，缺的只是上游赋值 |
| `DesignSpecApplier` 的约束求值 | `Evaluate()` 8 类约束实现完整且有测试 |
| `ExecutionResult` 结构 | `UnmetConstraints` 机制可用，只需正确填充 |

**关键判断：写入层已经能做，缺的是解析层。** 这意味着 Phase 8.2 的工作量主要在
`DesignSpec → 模型` 这一段，不需要触碰 Writer / Reader / VSDX 保真逻辑。

---

## 5. 不应改动的稳定部分

| 组件 | 理由 |
| --- | --- |
| `OfficeImoDocumentWriter` / `Reader` | Phase 8.1 的保真机制（几何剥离、母版恢复、z-order）建立在此之上 |
| `FidelityGuard` / `Visio*Bridge` | 安全拒绝与保真守卫，改动风险高收益低 |
| `LayoutStrategies` 五种引擎 | Phase 7/8.1 已验证，与样式无关 |
| 三层协议版本号 | 任务 §4.9 要求不破坏兼容性 |
| `DiagramIr` 现有字段 | 同上 |

---

## 6. 风险与依赖

| # | 风险 | 说明 |
| --- | --- | --- |
| R1 | 引入 StyleResolver 可能被视为"为抽象而抽象" | 缓解：以 §3 的四个实际缺陷为立项依据，不新增无消费者的抽象 |
| R2 | 优先级规则改动会改变既有产物 | 缓解：现有测试固定了行为，需逐项确认哪些是"当前正确"哪些是"当前错误" |
| R3 | Palette 容易被做成固定模板 | 任务 §3.2-7 明确禁止；缓解：palette 只提供色彩 token，不决定布局或形状 |
| R4 | 多页 ObjectMapping 的 ID 冲突契约需设计 | 任务 §2.2-F 要求；需决定键格式（如 `pageId/objectId`）并评估兼容性 |
| R5 | 单位换算改动会改变 `page.Size` 语义 | 若现状是"写了错误标签"，修正后旧 spec 的产出会变；需在计划中说明 |

**依赖：** 无新依赖。所用组件（TypeScale / ShapeStyler / Writer）均已在库内。

---

## 7. 结论

**核心问题不是"缺 palette"，而是样式解析缺少统一入口。**

`DesignSpec` 17 个字段里，3 项完全不执行、3 项部分执行、2 项只覆盖一类对象。
其中 palette 与 containerLayouts 属于"协议接受但静默忽略"，直接违反任务 §2.1 的硬要求。

**写作层（Writer）能力是够的**——Edge 标签字号、TextBlock 字号、容器 TextStyle 都有通路。
缺的是 `DesignSpecApplier` 里那段把 spec 翻译成模型样式的代码，它目前是四处互不相识的 if。

Phase 8.2 应当建立**一个**样式解析入口，把四项输入（Palette / RoleColors / Object Style /
TypeScale）按明确优先级合成为最终样式，并让所有对象类型共用同一条路径。
这既解决 §2 的 7 个缺陷，也避免继续在 if 链上追加第 5、第 6 处。

详细方案见 `phase8.2-plan.md`。

---

## 8. 审计自查：初稿的三处错误

Stage 3 独立审查前，我自己复核了一遍，发现初稿有三处判断错误。**记录在此，
因为它们正好印证了本审计设立的判据。**

| # | 初稿结论 | 实际 | 错在哪 |
| --- | --- | --- | --- |
| 1 | `Theme.Background` ✅ 已实现 | ❌ 设置了但从不落盘 | 把"代码给模型字段赋值"当成"执行"；未验证回读 |
| 2 | `typography.min_size` 报告存在 | ❌ 判断方向写反，死代码 | 读到 `unmet.Add(...)` 就以为会触发，未验证条件 |
| 3 | `RoleColors` 仅 Node | ⚠️ 确认，但**多页是正常的** | 假设"只看第一页"，实测两页都生效 |

**三条错误的共同点：把"代码看起来做了"当成"确实做了"。**

第一条最严重——我在文档里写"✅ 已实现"，而依据仅仅是
`diagram.Background = spec.Theme.Background` 这一行存在。
这与本审计 §0 声明的"拒绝以字段存在为证据"直接矛盾。

**修正方式：** 对每个"已实现"结论，必须有一次实际回读作为证据，
而不只是看到赋值语句。已重新核验 §1 表中所有 ✅ 项：

| 项 | 回读证据 |
| --- | --- |
| `Layout` | 不同布局产生不同几何（实测） |
| `Direction` | 传入 `LayoutOptions.Direction`（代码路径完整） |
| `Spacing.*` | gap 0.3→1.2 使节点展开 2.30→3.20（实测） |
| `Canvas.Mode/Width/Height` | 页面精确为 10×8（实测） |
| `Canvas.ShrinkToFit` | true→4.00×3.45，false→20×14（实测） |
| `HardConstraints` | 未满足进 `unmetConstraints`（实测） |
| `SoftConstraints` | `isHard=false` 且不阻断（实测） |
| `RoleColors` | 节点取到 `#FF0000`，多页均生效（实测） |

`Canvas.ShrinkToFit` 已补实测：请求 20×14 时，`true` 收缩到 4.00×3.45，
`false` 保持 20×14。**功能正确。**

**至此 §1 表中所有 ✅ 项均有回读或实测证据，无一项仅凭代码阅读。**

---

## 9. 【P0，审计遗漏】`Direction` 在全部四种布局策略下完全无效

这是本审计最终发现的最严重缺陷，**初稿完全漏掉了**——因为我看到
`LayoutOptions.Direction` 存在、`DesignSpecApplier` 传了值，就判为"✅ 已实现"。

### 实测

同一 IR（3 节点链），四种策略 × 两个方向：

```
layered/leftToRight: xspread=0.00 yspread=4.85
layered/topToBottom: xspread=0.00 yspread=4.85     ← 完全相同
flow/leftToRight:    xspread=5.10 yspread=0.00
flow/topToBottom:    xspread=5.10 yspread=0.00     ← 完全相同
tree/leftToRight:    xspread=0.00 yspread=3.50
tree/topToBottom:    xspread=0.00 yspread=3.50     ← 完全相同
grid/leftToRight:    xspread=5.10 yspread=0.00
grid/topToBottom:    xspread=5.10 yspread=0.00     ← 完全相同
```

**`Direction` 对输出零影响。**

### 根因

```console
$ grep -rn "options.Direction|\.Direction ==" src/Vsdx.Engine/LayeredLayoutEngine.cs \
      src/Vsdx.Engine/FlowAndTreeLayoutEngine.cs src/Vsdx.Engine/SwimlaneLayoutEngine.cs
（无结果）
```

`LayoutDirection` 枚举存在、`LayoutOptions.Direction` 字段存在、
`DesignSpecApplier` 把 `spec.Direction` 传给它——**但没有任何一个布局引擎读取它**。

### 为什么这是 P0

1. 它是**协议字段**：Schema 允许 `"direction":"topToBottom"`，调用方有理由相信它会生效
2. 它是**绘制方向**：对一张图来说纵横比完全不同，不是细微样式差异
3. 它**完全静默**：既不生效，也不进 `unmetConstraints`

这比 palette 更隐蔽——palette 至少是"没实现"，而 `Direction` 是"管线全部接好，
最后一公里断了"。链条上每一环看起来都对。

### 修法

`LayeredLayoutEngine` / `FlowLayoutEngine` / `GridLayoutEngine` / `TreeLayoutEngine`
的 `Layout()` 需读 `options.Direction`，在 `TopToBottom` 时交换 X/Y 轴
（或在最后对整个页面做转置）。`ManualLayoutEngine` 不改（用户坐标即意图）。

**验收：** 同一 IR 两个方向必须产生 `xspread`/`yspread` 互换的结果。

---

## 10. 审计最终的缺陷清单

| # | 缺陷 | 严重度 | 初稿是否发现 |
| --- | --- | --- | --- |
| 1 | `Direction` 四种策略全无效 | **P0** | ❌ **遗漏** |
| 2 | `Theme.Palette` 未执行 + 未知值不报告 | **P0** | ✅ |
| 3 | `Theme.Background` 设置后丢失 | P1 | ❌ 初稿误判为已实现 |
| 4 | Typography 仅覆盖 Node | P1 | ✅ |
| 5 | 容器色硬编码绕过 Theme | P1 | ✅ |
| 6 | `Canvas.Unit` 只写标签不换算 | P1 | ✅ |
| 7 | `containerLayouts` manual 下静默跳过 | P1 | ✅ |
| 8 | `ObjectMapping` 仅首页 + 仅 Node/Container | P1 | ✅ |
| 9 | `typography.min_size` 死代码 | P2 | ❌ 初稿误判为存在 |

**9 项缺陷中，初稿漏了 3 项、误判 2 项。** 这印证了 Stage 3 独立审查的必要性——
也说明"读代码判断能力"不可靠，只有实测才作数。
