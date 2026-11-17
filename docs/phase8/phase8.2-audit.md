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
| **`Canvas.Mode=fixed`** | ❌ **名存实亡（原判 ✅，已更正）** | 见 §2.9 |
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

### 2.2b 【P1】`Typography.BaseSize` 抹平了字体层级

任务 §3.3 明确要求"不能只提供一个全局 FontSize，需要支持不同角色的字体层级"。

当前 `BaseSize` 的实现恰恰是"一个全局 FontSize"：

```csharp
foreach (var node in page.Nodes)
{
    node.Style.FontSize = Math.Max(size, MinReadableSize);   // 每个节点同一个值
}
```

**实测（两家对照）：**

```
无 spec       : node sizes = [('a', None), ('b', None), ('c', None)]
baseSize=14   : node sizes = [('a', 14),   ('b', 14),   ('c', 14)  ]
```

**注意第一行**：无 spec 时 `fontSize` 是 `None`——`TypeScale` 并不给节点写字号字段，
它只用来**反算节点尺寸**（字号影响文字→影响框体大小）。因此"层级"体现在几何，不在样式字段。

**而 `baseSize` 一旦给值，就把三个角色全部设成同一个 14**，
此时字号字段有了值，但**层级消失了**——所有节点同样大小。

所以准确说法是：`baseSize` 不是"覆盖了现有层级"，而是**引入了本不存在的平坦字号**，
且跳过 TypeScale 的推导链路。两者机制不同，无法共存。

更矛盾的是：`DiagramSizer` 里**已经**有按画布与密度推导的完整 TypeScale
（`TypeScale.ForCanvas(width, height, nodeCount)`，6 个档位），
而 `DesignSpecApplier` 完全绕过它，另起一套。

**因此存在两套字号机制并存，且互相不通信：**

| 路径 | 来源 | 结果 |
| --- | --- | --- |
| 无 spec | `TypeScale.ForCanvas` | 有层级（title 20 / region 12 / node 11 …） |
| 有 `baseSize` | 直接赋值 | **无层级**（全部同值） |

即"声明了 typography 反而比不声明更难读"，与设计意图相反。

**修法：** `BaseSize` 应解读为**基准**（缩放因子），而非绝对值。
建议：`scale = baseSize / TypeScale.NodeTitle`，作用于整个 TypeScale，
保留档位关系。这是 Phase 8.2 §3.1 统一解析的核心任务之一。

### 2.2c 【P1】字号在布局之后应用，框体不会重算 → 文字溢出

**实测：**

```
baseSize=9  -> box 2.00 x 0.90, fontSize 9
baseSize=24 -> box 2.00 x 0.90, fontSize 24     ← 框完全没变
```

检测器确实抓到了：

```
$ check-layout b24.vsdx
text.overflow-horizontal: 'a' 最长行约 5.09" 宽，实际 2.00"
```

**但那是事后检测，生成时并未阻止。** 产出物仍然落盘，溢出 2.5 倍。

**根因是步骤顺序：**

```csharp
// DesignSpec.cs
139:  DiagramLayoutEngine.Apply(...);   // 布局：按当前字号算位置
166:  DiagramLayout.Apply(...);        // 容器：按成员反算包围盒
168:  // --- Typography ---            // 字号在这里才改
178:  node.Style.FontSize = clamped;   // 但框体已经定了
```

字号影响文字占位、文字占位影响框体尺寸。**先定尺寸再改字号，等于尺寸失效。**

**这与 Phase 8.1 修过的"FitPage 在布局之前"是同一类错误**——步骤顺序错了，
每一步单独看都对，组合起来产出错误结果。

**修法：** 字号必须在 `DiagramSizer.Apply` **之前**确定，
或字号改变后重新跑一次尺寸推导。Phase 8.2 §3.1 的统一解析若在布局前完成，
此问题自然消失——**这也是为什么解析器必须前置，而不只是"把 palette 接上"**。

### 2.2d 【P2】`roleColors` 无 `default` 回退，拼错角色静默失效

**实测**（IR 中一个节点角色拼成 `databse`）：

```json
"roleColors": { "service": "#FF0000", "default": "#00FF00" }
```

```
a (service)  -> #FF0000   ✅
b (databse)  -> #FFFFFF   ← 既没用 default，也没报告
unmet: []
```

**两个问题叠加：**

1. **`default` 键被忽略**——Schema 允许任意键名，代码用 `TryGetValue(role)` 精确匹配，
   从不查 `default`。调用方写了回退色，以为是兜底，实际无效。
2. **角色拼写错误无任何提示**——`databse` 不匹配任何角色，节点保持默认白色，
   且不进 `unmetConstraints`。

**后果：** 一个字母的拼写错误会导致整类节点失去配色，而调用方拿到的结果是
`status=succeeded`。

**修法：** 解析器应支持 `default` 回退，并对"IR 中出现但 roleColors 未覆盖的角色"
在 `unmetConstraints` 里报告一次（按角色去重，避免刷屏）。

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

**但"标签写错"的说法不准确**（审查 A.1#4 / B7 纠正——**不是** A9/B8；
A9/B8 指的是 `mode=fixed`，我一度引错了条目号）。

Writer 在 `OfficeImoDocumentWriter.cs:50` **硬传 `VisioMeasurementUnit.Inches`**。
实测解包**两个** part：

```
visio/pages/page1.xml : U="IN"=False  U="CM"=False
visio/pages/pages.xml : U="IN"=True   U="CM"=False     ← 在这里
```

**`U="IN"` 确实写进了文件，只是在 `pages.xml` 而非 `page1.xml`。**

我上一版写成"既无 `U="CM"` 也无 `U="IN"`"，是**只解包一个 part 就下了全包结论**——
把一次范围过窄的观察当成了全文结论。这与我批评过的"读代码判断能力"是同一类毛病，
只是换成了"读一个文件判断整个包"。

**准确表述：** Writer 硬传 Inches，故 `pages.xml` 中是 `U="IN"`；
`spec.Canvas.Unit` 从不影响该值，`10cm` 与 `10in` 产出相同。

因此危险不在"标签与数值不一致"，而在：
1. `spec.Canvas.Unit` 被**完全忽略**，`10cm` 与 `10in` 产出相同
2. `page.Size.Unit` 在模型里是 `"cm"`，进 Writer 后仍按英寸处理——
   **模型状态与执行结果不一致**

**且存在双重换算风险：** `OfficeImoIncrementalEditor.cs:62` 已在按 `UnitFactor` 换算，
若在 `DesignSpecApplier` 再乘一次，`update` 路径会换算两遍（M9）。

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

**量化（`examples/phase8/arch.ir.json`，单页）：**

```
mapping size: 13
  nodes   10 : covered 10    ✅
  groups   3 : covered  3    ✅
  edges    9 : covered  0    ❌ 全部缺失
  texts    4 : covered  0    ❌ 全部缺失
```

**9 条边与 4 个文本块，映射覆盖率为 0%。**

且实现只看 `diagram.Pages.FirstOrDefault()`——**多页时第二页及以后完全不映射**，
实测多页 IR 的 `objectMapping` 是**空对象** `{}`（不是"缺第二页"）。

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

### 2.10 【P1】`swimlane` 经协议入口不可达，却报告"已应用"

**实测**（IR 声明两个 `kind="lane"` 的容器，分别跑两种布局）：

```
swimlane: applied=['layout=Swimlane']   geometry [(1.85,3.65), (1.85,1.23)]
layered : applied=['layout=Layered']    geometry [(1.85,3.65), (1.85,1.23)]
                                          ↑ 逐点相同
```

**`applied` 声称用了 Swimlane，产出却与 Layered 完全一致。**

根因：

```csharp
// DiagramIr.cs:282
Kind = container.Kind is "lane" ? "container" : (container.Kind ?? "container"),
```

IR 的 `lane` 被**改写成 `container`**，而 `SwimlaneLayoutEngine` 靠
`g.Kind == "lane"` 识别泳道，找不到就回退到 `LayeredLayoutEngine`。

**为什么既有 `SwimlaneTests` 是绿的：** 它直接构造 `Group { Kind = "lane" }`，
**绕过 IR 转换**，因此从未发现协议入口是断的。

**这是"测试通过但能力不可达"的典型**——单元测试测了引擎，没测协议路径。

### 2.13 【P1】缺失"无法表达或渲染的样式必须报告"机制

任务书 §3.5 明文要求：

> **无法表达或渲染的样式必须报告。**

**实测：当前完全没有该机制。**

给一个 TextBlock 设置 `italic: true, color: "#FF0000", fontSize: 14`：

```console
$ execute --json
  status: succeeded
  unmet : []
```

**没有任何"该样式是否真的写进 VSDX / 是否能被渲染"的检查。**

具体缺口：

| 环节 | 现状 | 应有行为 |
| --- | --- | --- |
| 样式 → VSDX | 写了就假设成功 | 应回读确认关键单元格存在 |
| VSDX → Renderer | 渲染器能力未知 | 支持的样式集应显式声明，不支持的进 `unmet` |
| 未知样式值 | 静默接受 | 应报告 |

**这与缺陷 4（Typography 仅 Node）不同**：那项是"没接线"，本项是
**缺少"确认接线成功"的验证环节**——即使所有样式都接上，仍无法回答
"这个样式真的进文件了吗、渲染器认得吗"。

**修法（T2c）：**

1. 定义"Renderer 支持的样式属性白名单"（fill / stroke / fontSize / bold / italic / color / align）
2. 写入后抽样回读，断言关键单元格存在且值正确
3. 白名单外的样式进 `unmetConstraints`（`style.renderer-unsupported`）

**验收：** 设置一个白名单外的样式（如 `shadow: true`）→ `unmet` 必须含对应条目。

---

### 2.12 【P0】IR 声明了容器顺序，但布局完全忽略 → 流程倒置

**实测**（`examples/phase8/flow.ir.json`）：

IR 用 `order` 声明容器次序：`intake(2) → review(1) → fulfil(0)`，
语义上订单从受理→审核→履约，**受理应在最上**。

实际产出：

```
fulfil   y=5.55   ← 最上
review   y=3.43
intake   y=1.30   ← 最下
```

**顺序完全颠倒**，流程从底部开始读。

**根因链（已按审查更正引用的代码路径）：**

1. `IrContainer.Order` 在 `DiagramIrConverter` 中**从未被消费**
2. **`flow.ir.json` 中 `role=decision` 的节点使其走 `FlowLayoutEngine`**，
   容器排序是 `OrderBy(g => g.Bounds?.Y ?? 0)`（`FlowAndTreeLayoutEngine.cs:92`）
   ——**不是** `LayeredLayoutEngine` 的 `MeanY` 逻辑
3. IR 节点无 `anchor` → 全部 `Bounds(0,0,2.0,0.9)`（`DiagramIr.cs:297`）
4. 容器 `Bounds` 由成员反算，成员全在同一点 → 各容器 `Bounds.Y` 相同
5. `OrderBy` 对相等键**保持输入顺序**（稳定排序），输入顺序即字典/插入序，
   与 `order` 无关

**更正说明：** 初稿引用了 `LayeredLayoutEngine:117-121` 作为根因位置。
审查指出该 IR 实际选择 Flow 引擎，两处排序逻辑不同。
**结论不变**（`order` 被忽略 → 顺序错），但**引用路径已改对**。

**修复要点（对两个引擎都成立）：**
两个引擎都必须**优先消费显式 `order`**，缺失时再退回几何推断。

**这是缺陷 6（`IrContainer.Order` 接受不执行）的一个具体后果**，
但后果比"字段无效"严重得多：**产出语义颠倒的图**，且 `status=succeeded`、`hard=True`、`score=100`。

**为什么 `score=100`：** 质量检测只看几何合法性（不重叠、不越界、连线不穿节点），
**不检查容器顺序是否符合语义**——后者需要语义知识，不属于检测器职责。

**修法：**

1. `DiagramIrConverter` 消费 `IrContainer.Order`（写入 `Group.Bounds.Y` 或新字段）
2. `LayeredLayoutEngine` 容器排序优先用显式 `order`，缺失时再退回 `MeanY`
3. 两者都缺失时，按 IR 声明顺序（`OrderBy(ID)` 至少稳定，而非字典序）

**验收：** `flow.ir.json` 产出中 `intake` 的 Y 必须最大（最上）。

---

### 2.11 【P1】装饰元素逐个独立夹紧，相对间距被压没

**实测**（新建的 `examples/phase8/arch.ir.json`）：

IR 中 title 在 y=11.4、subtitle 在 y=10.8（间距 0.6"）。
页面因内容收缩到 9.4" 高，两者都在页外。经 `ClampDecorationsToPage` 后：

```
title    y=8.55  h=0.60
subtitle y=8.65  h=0.40     ← 间距从 0.6" 塌到 0.1"，视觉上重叠
```

**根因**（`LayoutStrategies.cs` 的 `ClampDecorationsToPage`）：
对每个装饰元素**独立**执行 `Clamp(value, half, limit)`。
当多个元素同时越界到同一侧时，各自被压到同一个边界值上，
**相对间距归零**。

渲染结果：标题与副标题叠在一起，不可读。

**这是 Phase 8.1 引入的 clamp 逻辑的缺陷**——当时只测了"单个文本块不越界"，
没测"多个装饰元素同时越界时相对位置是否保持"。

**修法：** 先对整组装饰元素施加**统一偏移**（保持相对间距），
仅在整组仍超出页面时才逐个夹紧。

**验收：** IR 中 title/subtitle 间距 0.6"，页面收缩后间距仍须 ≥ 0.5"。

---

### 2.9 【P0】`mode=fixed` 在默认配置下不成立

**实测：**

```
请求 canvas: { mode:"fixed", width:20, height:14 }   （默认 shrinkToFit=true）
实得: 4.00 x 3.45
unmet: []
```

**缩小了 78%，且报告成功。**

根因：`fixed` 分支里仍执行 `FitPage(page, shrink: true)`（`DesignSpec.cs:258-262`），
而 `ShrinkToFit` 默认 `true`。因此 **`mode=fixed` 在默认配置下等于 `auto`**。

只有显式写 `shrinkToFit: false` 才能得到请求尺寸。

**问题在于语义矛盾：**

| 声明 | 调用方预期 | 实际 |
| --- | --- | --- |
| `mode: "fixed"` | 页面就是 20×14 | 可能被缩到任意小 |
| `shrinkToFit: true`（默认） | 不要留白 | 覆盖了 fixed |

**修法（M2）：** 二选一，且必须报告：

1. `mode=fixed` 时忽略 `shrinkToFit`（fixed 优先），或
2. `shrinkToFit` 生效但**进 `unmetConstraints`** 说明"请求尺寸被收缩为 X×Y"

推荐方案 1，因为 `fixed` 的字面语义更强。若采纳方案 2，必须报告。

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
`DesignSpec → 模型` 这一段。

**但有一处例外必须说清楚：`Theme.Background` 的落盘需要改 Adapters。**

§2.8 已证明该字段设置后从不写入 VSDX，因此修复它**必须**：
- `OfficeImoDocumentWriter` 写 `PageSheet/PageBackground`
- `OfficeImoDocumentReader` 读回该值

这与"不改 Writer/Reader"的表述冲突。**准确的表述应为：**

| 层 | 是否改动 | 原因 |
| --- | --- | --- |
| Writer / Reader | **仅背景色一处** | 其余样式通路已具备 |
| `FidelityGuard` / `Visio*Bridge` | 不改 | 保真机制与样式无关 |
| `LayoutStrategies` | **需改**（T0） | `Direction` 需在各引擎实现 |
| 协议版本号 | 不改 | 只加执行，不加字段 |

**初稿把"几乎不用改 Writer"写成了"不改 Writer"，范围描述不准确，已更正。**

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
| `Canvas.ShrinkToFit` | true→4.00×3.45，false→20×14（实测）。**但默认 true 时 `mode=fixed` 失去意义**，见 §2.9 |
| `HardConstraints` | 未满足进 `unmetConstraints`（实测） |
| `SoftConstraints` | `isHard=false` 且不阻断（实测） |
| `RoleColors` | 节点取到 `#FF0000`，多页均生效（实测） |

`Canvas.ShrinkToFit` 实测：请求 20×14 时，`true` 收缩到 4.00×3.45，
`false` 保持 20×14。

**但"功能正确"这个判语已被撤回**（审查 M2 要求删除）。理由：
`shrinkToFit=true` 时 `mode=fixed` 的尺寸承诺完全失效，这不是"功能正确"，
而是两个字段的语义打架（见 §2.9）。

**且失效是双向的**（审查补充实测，我已复现）：

| 请求 | 实际 | `applied` | `unmet` |
| --- | --- | --- | --- |
| `fixed 20×14`（内容少） | 4.00×3.45（**缩小 78%**） | `canvas.fixed+shrunk` | `[]` |
| `fixed 8×6`（内容多，`arch.ir.json`） | 11.35×9.85（**放大 42%**） | `canvas.fixed` | `[]` |

**`fixed` 会缩小也会放大。** 因此"收缩时进 unmet"这一种修法不足以覆盖
放大方向——两个方向都必须报告。

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
| 10 | `BaseSize` 抹平字体层级 | **P1** | ❌ **遗漏** |
| 11 | 字号在布局后应用 → 文字溢出 | **P1** | ❌ **遗漏** |
| 12 | `roleColors` 无 default 回退 | P2 | ❌ **遗漏** |
| 13 | `mode=fixed` 名存实亡 | **P0** | ❌ **遗漏，且初稿判为"✅ 已实现"** |
| 14 | `swimlane` 经协议不可达 | P1 | ❌ **遗漏** |
| 15 | 装饰元素独立夹紧致间距塌陷 | P1 | ❌ **遗漏**（Phase 8.1 遗留） |
| 16 | IR 容器 `order` 被忽略致流程倒置 | **P0** | ❌ **遗漏** |
| 17 | 缺"无法表达/渲染必须报告"机制 | P1 | ❌ **遗漏**（任务书 §3.5 明确要求） |

**17 项缺陷中，初稿漏了 11 项、误判 3 项。** 这印证了 Stage 3 独立审查的必要性——
也说明"读代码判断能力"不可靠，只有实测才作数。
