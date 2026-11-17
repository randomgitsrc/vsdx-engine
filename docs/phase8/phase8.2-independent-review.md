# Phase 8.2 独立技术审查

**审查对象：**
- `docs/phase8/phase8.2-audit.md`（577 行，含 12 项缺陷 + 3 处自我更正）
- `docs/phase8/phase8.2-plan.md`（535 行，含 T0–T10 + §11–§14）
- `docs/phase8/phase8.2-review-packet.md`（129 行）

**审查基线：** `03af878`（工作树干净，`git status` 无产品代码改动）
**审查日期：** 2026-11-17
**审查者立场：** 未参与前序对话，不采信三份文档的任何结论，逐条独立复验。

---

## 审查方法与独立性声明

### 采用的证据等级

本次审查只接受三类证据，并按强度排序：

| 等级 | 证据类型 | 说明 |
| --- | --- | --- |
| E1 | **端到端实测 + 回读** | 构造 IR/Spec → `execute` → `export-model` → 比对 JSON 差异；辅以解包 VSDX 检查原始 XML |
| E2 | **源码定位（file:line）** | 逐行读取调用链，确认消费方存在/缺失 |
| E3 | 文档陈述 | **仅用于定位待验命题，从不作为结论依据** |

凡结论标注"实测"处，均可复现：全部实验在 `.review-scratch/` 下留有 IR/Spec/VSDX/JSON 产物。

### 已执行的实际操作

```console
$ dotnet build src/Vsdx.Cli/Vsdx.Cli.csproj -c Release   # 成功，0 warning 0 error
$ dotnet test tests/Vsdx.Tests/Vsdx.Tests.csproj -c Release
  已通过! - 失败: 0，通过: 373，已跳过: 0，总计: 373
```

基线（373 项通过、0 warning）**独立复现成功**。

实测覆盖：4 种布局 × 2 方向、3 种画布单位、7 组 typography 组合、manual/fixed/auto 三模式、多页与跨页 id、roleColors/default、palette 未知值、
FreePath 几何回读、Edge 标签字体回读、TextBlock 角色回读、约束 Target 作用域、未知 spec 字段、specVersion 越界。

### 独立性声明

1. **审查期间未修改任何产品代码。** 唯一新增是本文档；实验产物在 `.review-scratch/`（可删）。
2. 审查过程中三份文档被作者**两次在线修改**（审计从 292 行扩到 577 行，计划从 282 行扩到 535 行）。
   本文所有引用均针对 `03af878` 版本，并已用 `md5sum` 固定。
3. 作者在审计 §8 主动记录了三处自我更正。我**独立复验了这三条，全部成立**——
   这一点值得肯定，因为自我更正的三条恰好都属于"代码看起来做了、实际没做"这一类，很难自查。
4. 但审计与计划中**仍有若干结论经不起复验**，见 A 节中被判为 REFUTED / PARTIALLY CORRECT 的行。
   我不会因为文档整体质量高而放过这些。

### 一条重要的方法论限制

本次审查以 `export-model` 回读作为主要判据。但 A8 将证明：**回读本身存在盲区**
（Edge 标签字体、TextBlock 字体写了但读不回）。因此凡涉及"写进去了没有"的结论，
我都额外解包 VSDX 检查 `visio/pages/pageN.xml` 原始 XML，不单靠回读。

---

## A. 审计声明核验

### A.1 逐条结论

| # | 审计声明 | 我的结论 | 证据 |
| --- | --- | --- | --- |
| 1 | `Theme.Palette` 完全无消费者 | **VERIFIED** | `grep -rn "Palette" src/` 仅 2 处**声明**：`DesignSpec.cs:83`、`DiagramModel.cs:317`，无读取方。实测 `palette=office` 与基线模型逐字段比对：除 `id`/时间戳/`sourceHash` 外**零差异**；未知 palette `does-not-exist` → `status=succeeded, unmet=[]` |
| 2 | `Typography` 只作用于 Node | **VERIFIED** | `DesignSpec.cs:169-195` 循环体仅 `page.Nodes`，无 `Groups`/`Edges`/`TextBlocks`。实测 `fontFamily=Courier New, baseSize=14`：Node `a`=`Courier New`/14；Group `grp`=None/12；TextBlock `title`=None/20；Edge `e1`=`null`/`null` |
| 3 | 容器颜色硬编码、绕过 Theme | **VERIFIED** | `DesignSpec.cs:202-204` 三组硬编码数组；`:213-215` 以 `??=` 写入。实测换 palette 后 group fill 恒为 `#EAF3FB` |
| 4 | `Canvas.Unit` 不换算 | **结论 VERIFIED，机制描述 REFUTED** | 不换算为真：`DesignSpec.cs:256` 仅 `page.Size.Unit = spec.Canvas.Unit;`，全文件无 `*2.54`/`/25.4`。但"标签会被写成 cm"**是错的**：Writer 在 `OfficeImoDocumentWriter.cs:50` 硬传 `VisioMeasurementUnit.Inches`，实测解包 `pages.xml` 得 `<Cell N="PageWidth" V="10" U="IN"/>`，全文无 `U="CM"`。单位标签**根本没写进文件**，不存在"标签与数值不一致"。见 §B7 |
| 5 | `containerLayouts` 在 manual 下静默跳过 | **VERIFIED** | `DesignSpec.cs:143-159` 位于 `if (layout != manual)`（`:127-160`）内。实测 `layout=manual` + `containerLayouts` → `applied=['layout=manual','container.bands','canvas.auto']`、`unmet=[]` |
| 6 | `BuildMapping` 漏 Edge/TextBlock/Path，且只用第一页 | **VERIFIED，但审计低估了严重度** | `ExecutionResult.cs:167-188`：`:171` 取 `Pages.FirstOrDefault()`；`:174`/`:181` 遍历的是 **`ir.Nodes`/`ir.Containers`（顶层简写）**，不是 `ir.Pages[].Nodes`。实测多页 IR → `mapping: {}`（**完全为空**）；单页但用 `pages[]` 形式 → 同样 `{}`。不是"第二页缺失"，是"用 pages[] 就全丢"。见 §B4 |
| 7 | `typography.min_size` 是死代码 | **VERIFIED** | `DesignSpec.cs:178-187`：`clamped = Math.Max(size, min)`；`:179` `if (clamped < size)`。恒假。实测 `baseSize=4, minReadableSize=9` → 字号被提到 9，`unmet=[]`。作者给出的修法 `clamped > size` **正确** |
| 8 | **Writer 已支持 Edge 标签字体与 TextBlock 字体，缺口在上游** | **PARTIALLY CORRECT（本审查最重要的反驳）** | 见 §A.2 详述。写侧确实通，但**读侧不通**，且这正是计划验收要用的通路 |

### A.2 A8 的详细反驳：这不是"只缺接线"

审计与评审材料包都断言：

> `OfficeImoDocumentWriter:378` 已支持 `SetFontSize`……缺的是 `DesignSpecApplier` 从不设置这些值。
> **因此 Phase 8.2 主要是接线，不是造能力。**

**写侧确实通。** 我手工构造模型（`labelFont=Courier New, labelFontSize=17.5, labelColor=#AA0000`）走 `create`，
解包 `page1.xml` 得：

```xml
<Section N="Character">
  <Row IX="0">
    <Cell N="Font" V="Courier New" />
    <Cell N="Color" V="#AA0000" />
    <Cell N="Size" V="0.243055555555556" U="PT" />   <!-- 17.5/72 -->
  </Row>
</Section>
```

**但回读全丢。** `export-model` 后：`labelFont=None, labelFontSize=None, labelColor=None`。
根因在 `OfficeImoDocumentReader.cs:627-676` 的 `ReadEdge`——它映射了 `Kind`/`Label`/`LineColor`/`LineWeight`/`Waypoints`，
**从未映射 `LabelFont`/`LabelFontSize`/`LabelColor`**（`grep "LabelFont" src/Vsdx.Adapters/OfficeImoDocumentReader.cs` → 无结果）。

TextBlock 字体同病异因：`VisioTextBlockBridge.cs:141` 写了 `<Cell N="Font" V="Verdana"/>`，
但 ①该 Bridge 直写 XML，不经 `ShapeStyle`，Reader 的 `ReadStyle`（`:604-623`）读的是
`shape.TextStyle.FontFamily`（`:616`），而非形状级 `Font` 单元格；
②该 Font 值未登记进 `document.xml` 的 FaceNames 表（实测 `document.xml` 中无 `Verdana`）。
实测：IR 指定 `font=Verdana` → `page1.xml` 有 `<Cell N="Font" V="Verdana"/>` → 回读 `font=None`。

**为什么这不只是措辞问题：**

计划 §6.2 的核心验收项 `TypographyReachesAllObjectKinds` 断言
"Node/Group/Edge/TextBlock 四种都有 font+size"，而**唯一可自动化执行的判据就是 `export-model` 回读**。
按现状，即便 `DesignSpecApplier` 接线完全正确，该测试仍会因 Reader 盲区而失败。
届时实施者无法区分"我接错了线"还是"Reader 不支持"——**这正是计划 §6.1 想避免的返工，却由 A8 的判断亲手制造**。

**正确表述应为：** Node 与 Group 的字体是**双向**通的（我实测 group `Georgia`/15 可完整回读）；
Edge 标签与 TextBlock 字体是**单向只写**的（write-only），修复它们除了上游接线，**还必须补 Reader**。
这直接把 T2 从"小"变成"中"，并把 `OfficeImoDocumentReader.cs` 加进必改清单——
而计划 §5 明确把它列为"需触碰"，但理由是**背景色**，不是字体。

同理，审计 §2.2 称"容器 `TextStyle.Size` 在写入器里被硬编码为 12"也不准确：
`OfficeImoDocumentWriter.cs:219` 是 `if (group.Style?.FontSize is null) container.TextStyle.Size = 12;`
——这是**默认值**，显式值可覆盖且能回读（我实测 group fontSize=15 回读为 15）。不是硬编码。

### A.3 被反驳的其余审计结论

| # | 审计声明 | 结论 | 证据 |
| --- | --- | --- | --- |
| 9 | "`Canvas.Mode=fixed` ✅ 已实现 / 页面精确为请求值" | **REFUTED** | 实测 `mode=fixed, 20×14, shrinkToFit` 缺省(true) → 页面 **6.55×8.25**；`shrinkToFit=false` → 20×14。即：**默认配置下 `fixed` 不 fixed**。与字段文档"a fixed size is honoured when the caller needs a specific paper format"（`DesignSpec.cs:29-31`）直接矛盾，且 `unmet=[]` 无任何提示。见 §B8 |
| 10 | "`Canvas.ShrinkToFit` ✅ 已实现……**功能正确**"（审计 §8） | **PARTIALLY CORRECT** | 分支确实存在且两向都生效（我复现：20×14+true→收缩；false→保持）。但审计把"收缩"判为正确，未察觉它**使 `mode=fixed` 失去意义**。行为观察对，结论错 |
| 11 | "`HardConstraints` ✅ 已实现，**8 类约束**" | **PARTIALLY CORRECT** | 代码 `case` 标签 **7 个**（`DesignSpec.cs:314-385`），Schema 枚举也是 7 项。数字应为 7，非 8 |
| 12 | "`Evaluate()` 8 类约束实现**完整且有测试**" | **REFUTED（测试部分）** | 按 code 统计测试引用：`max_page_width` 4 处、`min_page_utilisation` 1、`require_title` 1；**`max_edge_detour`/`no_edge_crossing`/`max_nodes_per_band`/`no_hard_violations` 各 0 处**。7 类中 4 类无任何测试。见 §C5 |
| 13 | "`SoftConstraints` ✅ 已实现" | **PARTIALLY CORRECT** | 求值确实走同一 `Evaluate` 且 `isHard=false`，不阻断（实测 status=succeeded）。但软约束未满足**只进 `unmetConstraints`，不进入 `Quality.SoftViolations`**：实测软违规 1 项时 `quality.softViolations=0`。两条"软违规"通道互不对账 |

### A.4 审计自查节（§8）的一处硬伤

审计 §8 称"**至此 §1 表中所有 ✅ 项均有回读或实测证据，无一项仅凭代码阅读**"，
并列出 `Direction` 的回读证据为"传入 `LayoutOptions.Direction`（代码路径完整）"。

**这恰恰是最不该出现的证据类型**——"代码路径完整"正是该审计 §0 明确拒绝的三种伪证据之一。
而 §9 随后用实测推翻了自己 40 行前的这张表。§8 与 §9 未做删改对齐，
读者若只读 §8 会得到"Direction 已核验"的错误印象。

---

## B. 审计遗漏的问题

以下 12 项**在审计 12 项缺陷清单与后续三次补充（Direction/Background/min_size）中均未出现**，
且不在负责人已知的五个文档缺陷内。按严重度排序。

### B1 【P0】带 DesignSpec 时，节点框体从不按文字尺寸推导——比无 spec 更差

**这是本次审查最重要的发现，并且它使计划 §12（T0.5）的修法打不到靶。**

`DiagramSizer.Apply`（按文字测量反推框体）只有 4 个调用点：

```
src/Vsdx.Engine/CandidateComparison.cs:103
src/Vsdx.Engine/VsdxEngine.cs:58          （FitToText 选项，默认 false）
src/Vsdx.Protocol/ProtocolRunner.cs:77    （仅 spec == null 分支）
src/Vsdx.Cli/Program.cs:550
```

`DesignSpecApplier.Apply()` **从不调用 `DiagramSizer.Apply`**——它只调用 `Recenter`/`FitPage`（`DesignSpec.cs:260,273-274`），
这两个只调**页面**，不调**框体**。因此 spec 路径下所有节点保留 IR 转换时写死的
`Bounds = new Bounds(0, 0, 2.0, 0.9)`（`DiagramIr.cs:297`）。

**实测（同一 IR，三条路径）：**

| 路径 | 节点 `short` | 节点 `long`（长标签） | 节点 `cjk`（18 字中文） |
| --- | --- | --- | --- |
| **带 spec**（`{"layout":"layered"}`） | 2.00×0.90 | **2.00×0.90** | **2.00×0.90** |
| 空 spec `{}` | 2.00×0.90 | 2.00×0.90 | 2.00×0.90 |
| **不带 spec** | 1.30×0.625 | 3.75×0.825 | 1.80×0.825 |

**结论：只要传了 DesignSpec（哪怕 `{}`），文字尺寸推导整个阶段被跳过。**

等字号对照（排除"字号不同所以框不同"的干扰）：

```console
$ typography.baseSize=11  → 节点宽度 [2.00, 2.00, 2.00]     ← 无差异，即写死的 2.0
$ 不传 spec（引擎默认）    → 节点宽度 [1.30, 3.75, 1.80]     ← 按文字推导
```

两者字号同为 11pt，宽度却完全不同——**唯一变量是 `DiagramSizer.Apply` 是否在流水线上**。
这直接否证了"溢出是字号应用太晚"的归因。

`check-layout` 直击后果：

```console
$ check-layout long_spec.vsdx
text.overflow-horizontal: 'long' 最长行约 5.07" 宽，实际 2.00"
```

而 `execute` 的返回是：

```
status: succeeded
quality.hardConstraintsPassed: true
unmetConstraints: []
issueCodes: ['page.margins', 'page.underused', 'text.overflow-horizontal']
```

**溢出只以软 issueCode 出现，`status` 仍是 `succeeded`。**

**为什么计划 §12 修不好它：** §12.2 写"样式解析（含字号）必须在 `DiagramSizer.Apply` **之前**完成"。
但在 spec 路径里 `DiagramSizer.Apply` **根本不在流水线上**。"提前"一个不存在的阶段，
字号依然不参与框体推导。实测佐证：`baseSize` 从 11 调到 18，框体**完全不变**、溢出照旧——
不是因为顺序错，而是因为尺寸推导引擎没跑。

**这条也重写了审计 §2.2c 的归因。** 审计说根因是"字号在布局后应用"，
但反例是：不带 spec 时字号同样在布局后才由 `DiagramSizer` 参与，结果却正确。
真正的判别变量是 `DiagramSizer.Apply` 是否在流水线上。修法应是
**在 `DesignSpecApplier` 的样式解析之后、布局之前调用 `DiagramSizer.Apply`**，
而计划 §12 只提到顺序，没提到"补上这个阶段"。

### B2 【P1】`roleColors` 缺失可用的兜底，且缺陷 12 在计划中**没有任何对应任务**

审计缺陷 12 已正确识别（我复验成立：`default` 键被忽略，拼错角色静默失效）。但：

- 计划 **§4「十二项缺陷的修法」表只有 9 行**（编号 0–8），缺陷 **10、11、12 全部缺席**；
- 缺陷 10 由 §13 覆盖、缺陷 11 由 §12 覆盖，**缺陷 12 没有任何章节、没有任何 T 编号**；
- 计划 §9 子任务表 T0–T10，**无一项涉及 roleColors/default**；
- 唯一出现 `default` 的地方是 §2.2 优先级判据表（`:74`）的一句"角色未定义时用 `default`"，
  那是**颜色优先级**描述，不是本缺陷的修法与验收。

即：**一个被自己认定的 P2 缺陷，被写进清单、写进评审材料包，却没有被排进任何任务。**
按计划 §9 的"每个 T 独立提交"，它将被完整遗漏。

结论：§4 标题已改为"十二项"，但表体仍是 9 行——**标题与表体不一致，且缺的正好是 3 项**。

### B3 【P1】`DesignSpec.SpecVersion` 从不校验

`ProtocolRunner` 校验 IR 版本（`:191` `ProtocolVersion.IsCompatible(ir.IrVersion, "1")`），
但对 `spec.SpecVersion` 只做回显（`:42` `DesignSpecVersion = spec?.SpecVersion`），从不校验相容性。
`ProtocolVersion.IsCompatible`（`DiagramIr.cs:222`）明明存在，却未被用于 spec。

实测：

```console
$ --spec '{"specVersion":"99.0","layout":"layered"}'
  status=succeeded   designSpecVersion="99.0"   unmet=[]
```

Schema 规定 `"pattern": "^1\\.[0-9]+$"`，但 CLI 不加载 Schema，所以约束无效。
**这是"协议接受、不执行、不报告"的又一实例**，与 palette 同类，审计未列。

### B4 【P1】未知 / 拼错的 DesignSpec 字段被静默丢弃

CLI 反序列化 spec 时使用默认 `JsonSerializerOptions`（`Program.cs:715-721`），
未设 `UnmappedMemberHandling`，也未用 `JsonExtensionData` 收集多余字段。

实测（字段名故意拼错 + 塞入不存在字段）：

```console
$ --spec '{"layout":"layered","theme":{"palette":"office","brandNewField":123},
           "typografy":{"baseSize":20}}'
  status=succeeded   unmet=[]   warnings=[write.text-block]
```

`typografy`（少一个 h）与 `brandNewField` 都被无声吞掉。
**这意味着：审查者与调用方都可以"写错字段名并收到成功"。**
计划 §3.3 花了整节论证"未知 palette 必须报告"，却没有同样处理未知字段——
而未知字段的爆炸半径更大（任何拼写错误都静默失效）。Schema 的 `additionalProperties: false`
在 CLI 路径上从未被执行。

### B5 【P1】`ExecutionResult.ObjectMapping` 在 `pages[]` 形态下**完全为空**

在 A6 已指出 `BuildMapping` 只遍历顶层简写。补一个更尖锐的实测：

```console
$ execute twopage.ir.json        # 两页，共 4 节点 2 边 1 文本块
  objectMapping: {}              # 完全为空，不是"缺第二页"
$ execute onepage_pages.ir.json  # 单页，但用 pages[] 形式
  objectMapping: {}              # 同样为空
```

即：**只要 IR 用了 `pages[]`（包括单页），映射就是空字典。**
审计 §2.6 描述为"只看第一页"，实际是"只看顶层简写数组，多页/新式单页全丢"。
这改变了 T7b 的验收面：不能只测"两页都有条目"，必须先测"用 pages[] 的单页也有条目"。

### B6 【P1】`lane` 容器在 IR→模型转换时被改写成 `container`，导致 `swimlane` 在协议路径上永不可达

`DiagramIr.cs:282`：

```csharp
Kind = container.Kind is "lane" ? "container" : (container.Kind ?? "container"),
```

`"lane"` 被显式映射成 `"container"`。后果：

1. `DiagramLayoutEngine.SuggestStrategy` 的泳道判据（`LayoutStrategies.cs:109`
   `page.Groups.Any(g => g.Kind == "lane")`）**在协议路径上永远为假**；
2. `SwimlaneLayoutEngine.Layout` 取不到任何 lane（`:21`），走 `:27-31` 退化为 layered。

实测（IR 声明两个 `kind:"lane"` 容器 + `kind:"workflow"`）：

```console
$ --spec '{"layout":"auto"}'      → applied: ['layout=Layered', ...]     ← 期望 Swimlane
$ --spec '{"layout":"swimlane"}'  → applied: ['layout=Swimlane', ...]
  但几何与 layered 逐点相同：a(1.85,3.65) b(4.40,3.65) c(1.85,1.225)，页面同为 6.25×5.10
```

即 **`swimlane` 这个"视觉表达"在协议层等于 `layered`**，而报告声称它已应用。
`SwimlaneTests.cs` 全绿，因为它直接构造 `Kind = "lane"` 的 `Group`（`:24-26`），**绕过了 IR 转换**——
测试测的是引擎，不是协议路径。

对 Phase 8.2 的意义：本阶段主题是"多种视觉表达"。五种布局引擎中**有一种在协议入口处是死的**，
而计划把它列为"Phase 7/8.1 已验证，与样式无关"（审计 §5、计划原 §5），未触碰。
按审计自己 §9 的 P0 标准（协议字段接好、最后一公里断、完全静默），这条应同为 P0/P1。

### B7 【P1】单位换算存在**双重换算**陷阱，计划未做决定

`Canvas.Unit` 的修法（T4）计划写"按单位换算到英寸后再写入"。但另一条路径**已经**在换算：

```csharp
// OfficeImoIncrementalEditor.cs:59-70
double targetW = pageModel.Size.Width, targetH = pageModel.Size.Height;
double factor   = MasterCatalog.UnitFactor(pageModel.Size.Unit);   // cm→1/2.54
targetW *= factor; targetH *= factor;
...
page.Width = MasterCatalog.Inches(targetW);
```

实测（模型 `size = 10×8 cm` 走 `update`）：页面写成 **3.937×3.150 英寸**——正确。

因此 T4 一旦"在 `DesignSpec` 里把 10cm 换算成 3.937 并保留 `Unit="cm"`"，
再走 `update` 时 `UnitFactor("cm")` 会**再除一次 2.54**，得到 1.55"。
反向若把 `Unit` 改成 `"inches"`，则 `update` 正确、但模型丢失了调用方声明的显示单位。

**两条路只能选一条，计划没选。** 这不是细节：T4 的验收 `UnitConversionIsCorrect`
只测 `execute` 单程，测不出这个陷阱。必须在计划里明确
"`page.Size` 的规范化契约是『数值恒为英寸、`Unit` 仅作显示标注』"，并同步修正
`OfficeImoIncrementalEditor.cs:62`（或让 `DesignSpec` 写入前先把 `Unit` 归一）。

### B8 【P2】`mode=fixed` 在默认 `shrinkToFit` 下不成立，且不报告

见 A9。补充成因：`DesignSpec.cs:258-265` 在 fixed 分支里，只要 `shrinkToFit` 为真就调
`DiagramSizer.FitPage(page, shrink: true)`，而 `FitPage` 的收缩分支（`DiagramSizer.cs:170-180`）
**可把页面缩到比请求值小得多**（下限 `MinPageWidth=4.0`/`MinPageHeight=3.0`）。

实测请求 20×14（`mode=fixed`，默认 shrink）→ 实得 **6.55×8.25**，`applied=['canvas.fixed+shrunk']`，
`unmet=[]`。用户要 A3 纸，得到的是 A5 的一半，且被告知"成功"。

这是审计**观察到了但判为正确**的一条（§8 "功能正确"）。正确结论应是：
`shrinkToFit` 对 `mode=fixed` 的语义必须重新定义——要么 fixed 下忽略 shrink，
要么收缩后进 `unmet`。

### B9 【P2】`ClampDecorationsToPage` 静默移动作者指定的文本块/路径位置

`LayoutStrategies.cs:174-190` 在每次布局后把 textBlock/path 的 `Bounds.X/Y`
夹进页面（含 0.55" 边距），**不报告**。

实测：IR 指定标题 `box.y = 7.4`，输出 `y = 6.175`——**静默下移 1.2 英寸**。
这与 IR 对文本块的承诺直接冲突：

> TextBlock.Box … "Unlike a node, text is not laid out: where it goes is a design decision,
> so the protocol asks for it rather than guessing."（`DiagramIr.cs:76-78`）

即协议声明"文本位置由调用方决定、引擎不猜"，实现却在事后挪动且不说。
更麻烦的是它与 `canvas.mode=auto` 的交互：布局先跑（页面还很小时就 clamp），
再 `FitPage` 定页——clamp 用的是**旧页面尺寸**。这是 B1 同一类"阶段顺序"缺陷的第二个实例。

### B10 【P2】`constraint.Target` 从不用于作用域限定，但会被原样回显

`Evaluate`（`DesignSpec.cs:306-400`）从不用 `constraint.Target` 过滤对象，
只在结果里把 `Target` 抄进 `UnmetConstraint.Target`（`:395`）。

实测：`{"code":"max_nodes_per_band","value":1,"target":"grp"}`（IR 中容器 id 为 `G`，不是 `grp`）
→ `unmet=[{code:'max_nodes_per_band', target:'grp', reason:'超出：G=3'}]`。

输出看起来"限定了 grp 并发现越界"，实际是：`Target` 无效（`grp` 不存在）、
真正的判定对象是 `G`。**回显一个未生效的目标，比不回显更误导。**
设计文档说 Target 是"Target object for object-scoped constraints"（`:102`），实现未兑现。

### B11 【P2】IR 侧同类"接受但不执行也不报告"（审计只查了 DesignSpec 侧）

审计的方法论（§0）与任务硬要求（"不允许第四种情况"）同样适用于 IR。以下均实测：

| IR 字段 | 声明 | 实际 | 报告 |
| --- | --- | --- | --- |
| `IrContainer.Order`（`DiagramIr.cs:199`） | "lower = higher on the page" | `grep "\.Order"` 无任何读取方 | 无 |
| `IrAnchor.Mode="relative"`（`:166`） | "relative" 是位置提示 | 仅 `Mode=="fixed"` 被采纳（`:304`），relative 完全忽略 | 无（实测 anchor 9,9 被布局挪到 1.55,1，`status=succeeded`） |
| `IrEdge.FromPort/ToPort`（`:184-185`） | 端口提示 | 实测 `fromPort=right, toPort=left` → 回读均为 `None` | 无 |
| `IrMetadata`（`:202-208`） | 描述/来源/标签 | 转换时整体丢弃（`ToDiagram` 不读 `ir.Metadata`） | 无 |
| `Spacing.*` 在 `layout=manual` 下 | 间距意图 | 与 `containerLayouts` 同样被 `if (layout != manual)` 跳过 | **无**（而 containerLayouts 有报告——同类不同待遇） |

最后一条尤其说明问题：审计把 `containerLayouts` 的静默跳过定为 P1，
但 `Spacing` 在 manual 下是**完全相同的缺陷**，审计漏了，计划也没修。

### B12 【P2】评审材料包已实质性过期，且含与新决策冲突的表述

`phase8.2-review-packet.md` 仍停留在决策前状态：

- §四"**不触碰** Writer / Reader / FidelityGuard / **LayoutStrategies** / 协议版本"
  ——与修订后计划 §5「需触碰：Writer/Reader 各一处、四个布局引擎」直接矛盾；
- §五仍在**提问** D2（提议混合键格式）、D3（BaseSize 改缩放基准），
  而负责人已**裁决**（D2 拒绝混合键、D3 批准缩放语义）；计划 §14.1 已改为裸 ID 方案；
- §六工时估计"T0 修 Direction（含 Transpose 补齐）| **小**"——在计划 §11 自己列出
  6 类遗漏、8 条验收断言（含 FreePath 内部几何、弧线参数镜像、幂等性）之后，
  标"小"已不可信；
- 材料清单第 3 项列出 `phase8.2-independent-review.md` 为"Stage 3 独立审查报告"——
  该文件在本次审查前**不存在**。

作为"提交负责人决策"的历史文档它没错，但若继续作为当前材料包，会误导决策。

---

## C. 五维度评审

### C1. 架构：责任划分 —— ⚠️ 有条件通过

**成立的部分：**
- `StyleResolver` 立项有实测依据（4 处 if 产生的 4 个真实缺陷），不是抽象洁癖；
- 把 `Transpose` 从 `CandidateComparison` 提到 `DiagramLayout` 并让两处共用（§11.5）是**正确的去重**，
  不是多余重构——现有两份空间变换必然会漂移；
- "palette 只含颜色 token、不含布局/形状/字号"（§3.1）确实挡住了"固定模板"，
  结构体里没有尺寸类字段，这条自证成立。

**问题：**
1. **依赖方向未处理，会造出第三份字号表。** `DesignSpec`/未来 `StyleResolver` 在 `Vsdx.Protocol`；
   `TypeScale` 在 `Vsdx.Engine`；`VisioTextBlockBridge` 在 `Vsdx.Adapters` 且**明确不能引用 Engine**
   （`:21-23` 自述"Adapters cannot reference the Engine…so the mapping is duplicated here deliberately"）。
   现在 `VisioTextBlockBridge.SizeForRole`（`:25-33`）与 `TypeScale`（`TextLayout.cs:19-24`）已经**互相矛盾**：

   | 角色 | TypeScale | VisioTextBlockBridge.SizeForRole |
   | --- | --- | --- |
   | title | DiagramTitle 20 | 20 ✅ |
   | subtitle | RegionTitle **12** | 12 ✅ |
   | caption | NodeCaption **10** | **9** ❌ |
   | legend | NodeCaption **10** | 10 ✅ |
   | note | Annotation **9** | **8** ❌ |

   计划 §2.3 又给出**第三张**映射表（TextBlock role=title→DiagramTitle 等 8 行）。
   计划声称"不新增 TypeScale 字段——现有 6 档 + role 映射已覆盖"，
   但**没说明这三张表如何归一**，也没指定哪张是权威。§2.3 的 caption/note 取值将与
   `VisioTextBlockBridge` 冲突，直接导致"同一 role，走 bridge 是 9pt、走 resolver 是 10pt"。
   这是本计划最实质的架构缺口。

2. **文件级计划不完整且自相矛盾。** §5 有两条 `OfficeImoDocumentWriter.cs`（`:177` 与 `:179`）
   内容重复；同时 §5「不触碰」写 `FidelityGuard`/`Visio*Bridge`/协议版本（`:185`），
   紧接着写「需触碰」（`:187`）。而 §11.5 要求改 `DiagramLayout.cs` 与 `CandidateComparison.cs`，
   **两者都不在 §5 的文件清单里**。§12 要求改 `ProtocolRunner.cs`（补 sizing 阶段），
   **也不在清单里**。

3. 不必要的部分：无。相反，§10「明确不做」把所有扩大范围的诱惑都挡住了。

### C2. 图形：多种视觉表达 vs 固定模板 —— ⚠️ 偏窄

**成立：** palette 结构纯色彩（§3.1）、四套命名 palette 定位有区分度（enterprise/office/technical/muted）、
§3.2-4 明确"不决定布局、形状、字号"。颜色层不构成模板，这点自证成立。

**不足（三条，均实测）：**
1. **palette 是封闭集合。** `record Palette(...)` 是内部常量，`Theme.Palette` 只接受 4 个名字，
   **调用方无法定义自己的 token 集合**——只能在 4 套里选。这仍是一种（有限的）模板：
   任务要求"多种视觉表达"，而这里表达数 = 4，且不可扩展。计划未提供
   "调用方给 token map"的逃生口（例如 `theme.tokens: {...}`）。
2. **`swimlane` 视觉表达在协议入口是死的**（B6）。五种布局引擎里有一种等于 `layered`，
   而报告声称已应用。这直接削弱"多种表达"的实际供给。
3. **表达维度只有"颜色 + 字号"。** 边框样式、连线样式按关系语义、形状族按角色
   ——这些在模型里都有字段（`ShapeStyle.StrokePattern`、`Edge.LinePattern`、`MasterCatalog.ResolveMaster`），
   但计划全部不涉及。作为"统一视觉样式执行系统"，覆盖面偏窄。

结论：**方向正确，但供给面比"四套 palette + 字号"宽不了多少**，且计划未承认这一点。

### C3. 协议工程：是否还有"接受但不执行、不报告" —— ❌ 不通过

这是本计划最明确的失败点。计划修完 12 项后，**以下仍将处于"第四种情况"**：

| 残留项 | 状态 | 依据 |
| --- | --- | --- |
| `specVersion` 越界 | 接受、不校验、不报告 | B3 实测 `99.0` → succeeded |
| 未知/拼错 spec 字段 | 静默丢弃 | B4 实测 `typografy` 无提示 |
| `roleColors` 缺失/拼错角色 | 静默白色、不报告 | 缺陷 12，**计划无任务**（B2） |
| `IrContainer.Order` | 无读取方、不报告 | B11 |
| `IrAnchor.Mode="relative"` | 忽略、不报告 | B11 实测 |
| `IrEdge.FromPort/ToPort` | 丢失、不报告 | B11 实测 |
| `IrMetadata` | 整体丢弃 | B11 |
| `Spacing` @ manual | 跳过、不报告 | B11 实测 |
| `constraint.Target` | 不回显生效性（回显却无效） | B10 实测 |
| 未知 layout/direction/canvas.mode 枚举值 | 静默回退 | 实测 `{"layout":"kaboom","direction":"sideways","canvas":{"mode":"grow"}}` → `status=succeeded, unmet=[], applied=['layout=Layered',...]` |

**计划的修法模式是"逐项枚举"，不是"机制性封闭"。** 它修 12 个已知实例，
但没有引入任何"未知即报告"的通用闸门（例如：反序列化时收集未知成员、
枚举值白名单校验、或在 `ExecutionResult` 里加 `UnrecognizedInputs` 列表）。
按当前计划，**下一轮审计必然再发现 5–8 个同类残留**——这正是审计 §0 立下的判据想避免的循环。

这一条足以单独决定裁决。

### C4. VSDX 保真：是否会破坏既有对象/样式 —— ⚠️ 风险未受控

**低风险部分：** T1–T9 全部工作在 `Vsdx.Protocol` 层的模型字段上，不触碰 XML。

**高风险三处：**

1. **T10（背景落盘）要改 Writer/Reader，且顺序敏感。**
   `OfficeImoDocumentWriter.Write` 的顺序是：
   `doc.Save()` → `VisioNestingBridge` → `VisioFreePathBridge` → `VisioTextBlockBridge` → `VisioMetadataBridge`
   （`:154-166`）。后三个各自**重新打开 zip、重写 `pageN.xml`**。
   若背景以"背景页"方式实现（OfficeIMO.Visio 已提供 `BackgroundPage`/`BackgroundColor`/`BackgroundSurface` API），
   它会增加一个 page part，与三处基于 `page{index+1}.xml` 硬编码索引的 Bridge（`VisioFreePathBridge.cs:54`、
   `VisioTextBlockBridge.cs:56`）**索引假设直接冲突**。计划只说"写 PageSheet/PageBackground"，
   没选实现路径、没定插入顺序、没评估对 `ShapeOrder`/z-order 恢复的影响。
2. **T4（单位）有双重换算风险**，见 B7。`update` 路径已在换算，`execute` 路径修复后会与它叠加。
3. **T0（Transpose）触碰已验收的布局引擎与路由。**
   计划 §11 要求"Waypoints 转置而非清空"，但 `LayoutStrategies.Apply` 在 `:142`
   **无条件**把 `edge.Waypoints = null`，且发生在布局阶段、早于任何转置点。
   实测：模型里显式给了 3 个 waypoint，走 `execute` 后 → `waypoints: None`。
   所以"不要清空、要转置"在 `execute` 路径上**没有可作用的输入**。
   计划没说清转置相对于这次清空的时序——若放在清空之后，转置的是空列表，
   验收断言 4「Waypoints 逐点转置（非清空）」将**无法通过**，或只能靠手工构造模型的旁路测试通过（假绿）。

**结论：** 保真风险不是"会不会碰到 Writer"，而是"计划没有说明怎么碰、碰的顺序、以及跟既有
`pageN.xml` 重写机制的关系"。T10 尤其如此。

### C5. 测试工程：验收能否证明真实能力 —— ❌ 不通过

**三个硬问题：**

**（1）旗舰目视验收用的是错误格式的输入文件。**

计划 §6.4：

| 产物 | IR |
| --- | --- |
| 分层架构图 | `benchmarks/architecture/complex.json` |
| 流程图 | `benchmarks/workflow/complex.json` |
| 综合图 | `examples/phase8/slice.json` |

但这三个文件是 **Diagram 模型**（有 `pageSize`/`style`/`nodes[].text`/`nodes[].shape`），
不是 Diagram IR（`schemas/phase7/diagram-ir.schema.json` 的 node 只有 `anchor/attributes/containerId/id/label/role`）。
把模型喂给 `--ir` 会被静默按 IR 反序列化，`text`、`shape`、`style`、`bounds` 全部丢失。

实测：

```console
$ execute --ir benchmarks/architecture/complex.json --spec ... --output bench.vsdx
  status: succeeded
$ export-model → 15 个节点，label 保留率 0/15
  web    : 'Web 门户\nReact SPA'  →  'web'
  mobile : '移动端\niOS / Android' →  'mobile'
  api    : 'API 网关\nKong'        →  'api'
```

**三套"真实产物"目视验收会渲染出 15 个以 id 为标签的方块，然后被判定"通过"。**
这是测试工程中最危险的一类失败：**测试全绿、产物是垃圾**。

**（2）现有测试覆盖远低于审计声称的水准。**

审计 §4 称 `Evaluate()` "8 类约束实现完整且有测试"（且数字应为 7）。实际：

| 约束 code | 测试引用数 |
| --- | --- |
| `max_page_width` | 4 |
| `min_page_utilisation` | 1（仅测 JSON 往返，非求值） |
| `require_title` | 1 |
| `max_edge_detour` | **0** |
| `no_edge_crossing` | **0** |
| `max_nodes_per_band` | **0** |
| `no_hard_violations` | **0** |

`Palette` 在 `tests/` 中出现 **0 次**。所以"373 项全过"对本阶段几乎没有保护力：
它保护的是**旧行为不变**，而本阶段要改的 12 项里，有 10 项**当前无任何测试**。
计划 §6.5 把"373 项全过"当回归门槛是合理的，但不能当作 T1–T7 的验收。

**（3）关键验收不可自动化，且计划未提供替代判据。**

- `T8`「三套产物 + PNG + 目视」的验收是"附观察记录"——主观、不可复现、
  无法在 CI 中回归，且（1）已使它注定看到错误内容；
- `BackgroundFromPalette`（§6.2）断言"palette 决定背景"，但背景当前不落盘（缺陷 3），
  该测试依赖 T10 先完成——计划未标注这个依赖；
- `TypographyReachesAllObjectKinds`（§6.2）断言"四种对象都有 font+size"，
  若以 `export-model` 为判据，**必然因 Reader 盲区失败**（A8），而计划未把 Reader 字体修复列入任务。

**唯一质量较高的是：** §11.6 的 8 条断言（含"转置两次回到原状态"的幂等性反向验证）与
§13.4 的 5 条断言（含"NodeTitle × scale 精确等于 baseSize"的校验点）——这两组是真能证伪的验收。
可惜它们只覆盖 T0 与 §13，不覆盖主线 T1–T7。

---

## D. 实施计划的执行链路评估

**结论：计划在 §11–§14 给出了真实的源码级数据流；但主线 §1.2 的数据流图是错的，且错在最关键的一环。**

### D1. 做得好的部分（真实的源码级链路，不是类名堆砌）

| 位置 | 内容 | 评价 |
| --- | --- | --- |
| §11.1 | 逐项列出 `CandidateComparison.cs:141` 的 Transpose **已覆盖** 与 **遗漏** 项 | 真实：我核对 `:141-165`，确为 nodes/groups/waypoints=null/page.Size，判断准确 |
| §11.2 | 指出"清空 waypoints 在 CandidateComparison 可接受、在 Direction 不可接受" | 判断正确，且给出了理由（丢弃用户输入） |
| §11.3 | 指出 `PathRow.Cells` 是**局部坐标**，只换 Bounds 会得到"框转竖了、线还是横的" | 真实：`VisioFreePathBridge.cs:170-229` 确实按局部坐标发射 `row.Cells`；我实测 `MoveTo(0,0)→LineTo(4,0)` 原样往返 |
| §12.1 | 用带行号的伪代码指出"字号在 `DiagramLayoutEngine.Apply`/`DiagramLayout.Apply` 之后才写" | 真实：`DesignSpec.cs:139/166/178` 行号与内容一一对应 |
| §13.2 | `scale = baseSize / TypeScale.NodeTitle`，并给出 6 档结果表，声明"数值由 python3 逐档核算" | **复算通过**：scale=14/11=1.2727…；20→25.45、12→15.27、11→14.00、10→12.73、9→11.45、9→11.45，与文档表格逐格一致 |
| §14.3 | 指出 `IrValidator.ValidatePage` 的 `nodeIds` 是局部变量（`ProtocolRunner.cs:238`），页间不共享 | 真实：我核对 `:238` 确为 `var nodeIds = new HashSet<string>(...)`，在 `ValidatePage` 内 |

这五处是**依据源码行号、可被第三方复核**的陈述，符合"说明真实数据流"的标准。

### D2. 主线数据流图是错的

计划 §1.2：

```
DesignSpec
  ├─ Theme.Palette ─────┐
  ├─ Theme.RoleColors ──┤
  ├─ Theme.Background ──┼──▶ StyleResolver ──▶ ResolvedStyle ──▶ 模型对象
  └─ Typography ────────┘         ▲                                    │
                                  │                                    ▼
                          TypeScale（角色字号）              VSDX Writer（已有能力）
```

**缺失了 `DiagramSizer.Apply`——即"字号 → 文字占位 → 框体尺寸"这一步。**
而这正是 B1 证明的、spec 路径上完全缺席的那个阶段。

对比 §12.2 自己画的图：

```
DesignSpec → StyleResolver → 字号写入模型 → DiagramSizer.Apply → 布局 → 写入 VSDX
```

§12 有这一步，§1.2 没有。**两张图不一致，且 §1.2 是读者第一眼看到的架构图。**
按 §1.2 实施，就会得到"字号正确、框体不匹配、文字溢出、status=succeeded"的现状（B1 已实测）。

### D3. 断言与自己的证据冲突

- §1.2 与 §4 说 **"Writer 不改"**（`:34`「关键：Writer 不改」）；§5 与 §11.5 却列出
  Writer/Reader/四引擎必改（`:187`「需触碰（初稿遗漏）」）。同一文档两种主张并存。
- §4 标题「十二项缺陷的修法」，表体 9 行，缺 10/11/12（其中 12 完全无任务，B2）。
- §4.1 仍在"提议"混合键格式（`pageId/objectId`），§14.1 已按负责人决定改为"保持裸 ID"。
  **两条规则同时留在文档里**，实施者按 §4.1 会做出被负责人否决的方案。
- §14.2 第 1 项仍写"**核查** `IrValidator` 当前是否只做页内唯一性检查"——
  而 §14.3 已经给出了核查结果。计划在向自己提问，说明这两节不是同一轮写成的，未做收口。
- §6.5 写"18 基准硬约束全过"——**此数字正确**（6 个 family × 3 = 18 delivering，
  另有 `benchmarks/faults/` 8 个用于反向断言）。此处无问题，我一度误算为 26，已更正。

### D4. 「类名 vs 数据流」的判定

按"是否说明了真实数据流"逐阶段打分：

| 阶段 | 计划是否说明真实链路 | 依据 |
| --- | --- | --- |
| spec → 样式值 | ⚠️ 部分：给了优先级规则，但没给三份字号表归一方案 | C1 |
| 样式值 → 字号写入模型 | ✅ 明确（§13.2 公式 + 校验点） | D1 |
| **字号 → 框体尺寸** | ❌ **主线图缺失该阶段；§12 有但结论错误** | B1 / D2 |
| 框体 → 布局 | ✅ 复用既有引擎 | — |
| 布局 → 转置（T0） | ⚠️ 列了 6 类遗漏，但漏 ports/padding/PageSize，且与既有 waypoint 清空冲突 | B 节 / C4 |
| 布局 → VSDX 写入 | ⚠️ 只列"写 PageSheet/PageBackground"，未说明与三处 `pageN.xml` 重写的顺序 | C4 |
| 回读 → 验收 | ❌ 验收用错输入格式；Edge/TextBlock 字体回读盲区未处理 | C5 / A8 |

**7 个阶段中，2 个清晰、3 个不完整、2 个错误。** 这是"部分真实数据流 + 部分类名/断言"的混合体，
不足以支撑"尚未编码、可以开工"的判断。

---

## E. 裁决

> ### 裁决：**FAIL**

### 裁决理由（只列已实证的核心项）

**支持通过的证据（我确实找到的）：**
1. 审计的 12 项缺陷中，我独立复验 **11 项成立**（Direction / Palette / Background / Typography 仅 Node /
   容器色硬编码 / Unit 不换算 / containerLayouts / ObjectMapping / min_size 死代码 / BaseSize 抹平 / roleColors 无 default）。
   检出率与准确率都明显高于一般审计文档。
2. 审计 §8 的三处自我更正**全部成立**，且自查方向（"代码有赋值 ≠ 执行"）方法论正确。
3. 计划 §11.6、§13.4 的验收断言是可证伪的真验收（幂等性、精确校验点）。
4. 计划 §13.2 的比例算术**复算无误**。
5. 基线 373 测试 / 0 warning **独立复现**。
6. 计划 §10「明确不做」边界清晰，未扩张范围。

**构成 FAIL 的证据（每条都有实测支撑）：**

1. **【P0，新发现】计划的核心修法打不到靶。** 带 spec 时 `DiagramSizer.Apply` 根本不在流水线上（B1），
   计划 §12 只说"把字号提前到它之前"，而它不存在。实测：`baseSize` 11→18 框体尺寸完全不变、溢出照旧、
   `status=succeeded`。**同一 IR，带 spec 比不带 spec 明显更差**——这是本阶段最该修的缺陷，计划未识别。

2. **【P0，新发现】`mode=fixed` 在默认配置下不成立**，且审计观察到此现象却判为"功能正确"（A9）。
   请求 20×14 得到 6.55×8.25，`unmet=[]`。这是 `Canvas` 语义的根本缺陷，不在 12 项清单内。

3. **【P1，新发现】`swimlane` 在协议入口不可达**（B6）：`DiagramIr.cs:282` 把 `lane` 改写成 `container`，
   导致 `layout=swimlane` 与 `layered` 几何逐点相同，却报告"已应用"。
   在"多种视觉表达"的阶段主题下，五种表达之一实际是死的，且既有测试因绕过 IR 转换而假绿。

4. **【P1，反驳审计 A8】"Writer 已支持、只缺接线"对 Edge 标签与 TextBlock 字体不成立**：
   写进去了但**读不回来**（Reader 无 `LabelFont` 映射；Bridge 的 `Font` 单元格不进 FaceNames）。
   计划 §6.2 的核心验收 `TypographyReachesAllObjectKinds` 因此注定失败或假绿，
   且 T2 被低估为"小"。

5. **【P1，计划内部不一致】§4 标题 12 项、表体 9 行，缺陷 12 无任何任务**（B2）；
   §4.1 与被否决的混合键方案并存、与 §14.1 冲突；§1.2 与 §5 对"Writer 是否改"自相矛盾；
   §14.2 仍在"核查"§14.3 已答的问题。

6. **【P1，测试工程】旗舰目视验收用错输入格式**（C5）：`benchmarks/*.json` 是 Diagram 模型不是 IR，
   实测 15/15 标签丢失（`'Web 门户\nReact SPA'` → `'web'`）。三套"真实产物"将是 15 个 id 方块，
   而验收会判"通过"。

7. **【P1，协议工程】计划不封闭"接受但不执行不报告"这一类**（C3）：修后仍残留 10 类
   （specVersion、未知字段、roleColors、IrContainer.Order、anchor=relative、ports、IrMetadata、
   Spacing@manual、Target 作用域、未知枚举值）。计划的模式是逐项枚举而非机制性闸门，
   下一轮审计必然再发现同类残留。

8. **【P1，VSDX 保真】T10 的实现路径未定**（C4）：背景页与三处硬编码 `page{index+1}.xml` 索引的 Bridge 冲突；
   T4 与 `OfficeImoIncrementalEditor.cs:62` 的既有换算叠加会造成双重换算（B7）。

### 必须修改项（按阻断性排序）

| # | 必须修改 | 对应证据 | 验收方式 |
| --- | --- | --- | --- |
| **M1** | **在 spec 路径补上 `DiagramSizer.Apply`（样式解析后、布局前）**；修正 §1.2 数据流图加入该阶段；重写缺陷 11 的归因（不是"顺序"而是"阶段缺失"） | B1 | `baseSize` 9→24，同一 IR 框体必须随之增大，且 `text.overflow-*` 为 0；**不带 spec 与带 spec 的框体在相同字号下必须一致** |
| **M2** | **重定义 `mode=fixed` 与 `shrinkToFit` 的关系**：fixed 下要么忽略 shrink，要么收缩后进 `unmet`；删除审计 §8「功能正确」的判语 | A9/B8 | 请求 20×14（默认 shrink）→ 页面必须为 20×14，或 `unmet` 有明确 entry |
| **M3** | **把 `DiagramIr.cs:282` 的 `lane → container` 改为保留 `lane`**，并验证 `layout=auto` 能选中 Swimlane | B6 | 声明 lane 的 IR → `applied` 含 `layout=Swimlane`，且几何与 `layered` **不同** |
| **M4** | **补 Reader 侧 Edge 标签字体与 TextBlock 字体回读**（或明确声明"字体不回读"并改用 XML 断言作为验收）；把 `OfficeImoDocumentReader.cs` 加入任务清单，T2 工时改为"中" | A8 | `labelFont=Georgia` → 回读 `Georgia`；`textBlock.font=Verdana` → 回读 `Verdana` |
| **M5** | **§4 表格补齐缺陷 10/11/12 三行**，并给缺陷 12（roleColors default）分配独立任务 Tn + 验收；清除 §4.1 与被否决方案；修正 §14.2 的"核查"为"结论"；统一 §1.2 与 §5 的 Writer 范围表述 | B2/D3 | 文档内 12 项缺陷 ⇄ 12 行修法 ⇄ 12 个任务，一一对应 |
| **M6** | **§6.4 换用真正的 IR 输入**（新建 `examples/phase8/*.ir.json`），或改为 `create` 路径；验收必须断言"标签文本与源文件一致（>90%）" | C5 | 目视产物中标签为 `Web 门户 / React SPA` 而非 `web` |
| **M7** | **引入机制性闸门而非逐项枚举**：spec/IR 反序列化收集未知成员并报告；枚举值白名单校验；`ExecutionResult` 增 `UnrecognizedInputs` | C3/B3/B4 | 拼错 `typografy`、`specVersion=99.0`、`layout=kaboom` 三者都必须进 `unmet`/`UnrecognizedInputs` |
| **M8** | **§11 的转置清单补齐**：`Edge.FromPort/ToPort`（left↔bottom 等）、`ShapeStyle.PaddingX/HeadingHeight/PaddingBottom`（X/Y 轴不同）、`Diagram.PageSize`、`Diagram.Orientation`；并**明确转置相对于 `LayoutStrategies.cs:142` waypoint 清空的时序**（否则 T0 验收断言 4 不可达） | B6/C4 | 带 ports + padding + 作者 waypoint 的 IR，转置后逐项断言 |
| **M9** | **确定 `Canvas.Unit` 的规范化契约**并同步 `OfficeImoIncrementalEditor.cs:62`，避免双重换算；T4 验收需加"`execute` 后再 `update`"的双程断言 | B7 | 10×8cm → execute → 3.937×3.150 → update → 仍为 3.937×3.150 |
| **M10** | **T10 选定背景实现路径**（PageSheet 单元格 vs 背景页），说明与 `VisioNestingBridge`/`VisioFreePathBridge`/`VisioTextBlockBridge` 的重写顺序，并核对对 z-order 的影响 | C4 | 写 `#FAFAFA` → 回读同值；既有 `mixed-fidelity.vsdx` 往返逐字段不变 |
| **M11** | **统一三份字号表**（`TypeScale` / `VisioTextBlockBridge.SizeForRole` / §2.3 映射），指定唯一权威并在计划里写明依赖方向方案（Adapters 不能引用 Engine 的既有约束） | C1 | `caption`/`note` 在两条路径产出同一字号 |
| **M12** | **修正评审材料包**：删除与新决策冲突的"不触碰 LayoutStrategies"、D2/D3 提问改为"已裁决"、T0 工时由"小"改"中"，并移除不存在的材料第 3 项 | B12 | 材料包与审计/计划无矛盾 |

### 关于裁决等级的说明

我**没有**因为"设计看起来合理"而否决——上面 6 条支持证据是我实际认可的，其中审计的缺陷检出率确实高。
判 FAIL 的原因是：**计划的失败点集中在其最核心的两处主张上**——
（a）"Writer 已支持，只缺接线"（对 2/4 类对象不成立，且破坏验收路径）；
（b）"字号提前即可解决溢出"（真正的缺口是阶段缺失，计划未识别）。
这两处不是措辞问题，而是**按计划实施会得到错误结果**的问题。
再加上 §4 覆盖不全、旗舰验收用错输入，此时放行等于把已知缺陷带进实施阶段。

若 M1–M6 补齐，本计划**可以转为 PASS WITH CONDITIONS**；M7–M12 可作为条件项在实施中闭环。

---

## F. 本审查的局限

必须如实声明以下限制，避免本文被当作超出其证据范围的结论使用。

### F1. 无 Microsoft Visio，L4 完全未验证

环境实际状态（我复述 `docs/phase8/l4-verification-status.md` 并确认其结论合理，未独立重验）：
仅 WPS Office 2023，无 Visio/Office；`/mnt/c` 为只读挂载。

**因此：**
- 我无法确认生成的 VSDX 在 Visio 中**打开是否报错、修复提示、或视觉正确**；
- "文字溢出"是依据 `check-layout` 的文本度量估算，不是 Visio 实际渲染；
- 背景页方案（M10）是否真的不破坏保真，**只能靠 XML 层推断，无法用 Visio 验收**。

**凡涉及"在 Visio 里看起来如何"的结论，本文一律未作断言。**

### F2. 回读本身有盲区，我可能低估或高估某些缺陷

A8 已证明 `export-model` 读不回 Edge 标签字体与 TextBlock 字体。这意味着：

- 凡我判定"未生效"的字段，若其效果只存在于 XML 而不被 Reader 还原，我可能**误判为"未执行"**。
  我对 `Canvas.Unit`、`Theme.Background`、`PathRow` 三项额外解包了 XML 以规避此风险；
  对其余项（如 `IrEdge.FromPort`）我**只做了回读判定，未逐项解包 XML**——
  理论上存在"写进去了但读不回"的可能，我未排除。
- 反向风险：某些项我判定"生效"，实际可能只是模型字段被设置、Writer 未消费（这正是缺陷 3 的形态）。

### F3. 未穷举所有 B 类缺陷

B 节的 12 项来自：① 按提示清单（Spacing / ShrinkToFit / MinReadableSize / 未执行字段 / SoftConstraints / 多页）
定向排查；② 沿 `DesignSpecApplier`、`DiagramIrConverter`、`ProtocolRunner`、`LayoutStrategies`、
`ExecutionResult`、四个 Bridge 的调用链顺查；③ 对 schema 与代码的枚举值对账。

**我没有做的：**
- 未审计 `LayoutAnalyzer`（1143 行）的全部检测逻辑与阈值合理性；
- 未审计 `RepairController` / `VersionStore` / `BatchRunner` / `FidelityGuard` 的行为；
- 未做 fuzz / 随机 IR 生成；
- 未审计渲染器（`OfficeImoVsdxRenderer`）与 `FontResolver` 的字体替换行为。

因此 B 节**不构成完备清单**，只能说明"这些确实存在且未被审计覆盖"。

### F4. 三份文档在审查期间被在线修改

审计由 292 行增至 577 行、计划由 282 行增至 535 行、材料包为新增文件。
我以 `03af878` 为基准并用 `md5sum` 固定了版本，但：

- 若后续继续修改，本文的 file:line 引用可能漂移；
- 我在两个时点读到了审计的不同版本（第一版无 §9/§10，无缺陷 10–12），
  **本文所有结论均基于最终版**；第一版中的若干结论已被作者自行撤回，我不再重复计数。

### F5. 对"负责人决策"的审查边界

D1–D3 属**产品决策**，不是事实命题，我不质疑其取舍。
本文只审查"决策是否被正确、完整地落进文档与实现"：
- D1（Direction 无兼容开关）：决策已落，但**转置覆盖面仍不完整**（M8）；
- D2（裸 ID + 全局唯一）：决策已落，且我**独立确认跨页重复 ID 当前未被拒绝**——
  但我实测到的状态是 `succeeded`（而非材料所称 `partial`），比材料描述的更宽松；
- D3（BaseSize 作缩放基准）：决策已落，算术**复算无误**（D1 表）。

### F6. 关于"P0/P1/P2"分级的性质

本文严重度分级是**我的判断**，依据是三项可观测后果：
① 是否静默（无报告）；② 是否使协议字段失去意义；③ 是否使验收失效。
它不是项目既定标准。若项目对 P0 有更严定义（如"仅限数据丢失/崩溃"），
本文的 P0 项（B1、Direction、`mode=fixed`）需按项目标准重新对齐——
但**无论分级如何，M1–M12 的事实基础不变**。

---

## 附：本文引用的关键源码位置（便于复核）

```
src/Vsdx.Protocol/DesignSpec.cs:83            Palette 声明（无读取方）
src/Vsdx.Protocol/DesignSpec.cs:127-164       layout / manual / containerLayouts 分支
src/Vsdx.Protocol/DesignSpec.cs:169-195       Typography 仅遍历 page.Nodes
src/Vsdx.Protocol/DesignSpec.cs:178-187       min_size 恒假分支（死代码）
src/Vsdx.Protocol/DesignSpec.cs:202-215       容器硬编码色（bandFills 等）
src/Vsdx.Protocol/DesignSpec.cs:250-287       Canvas fixed/auto + shrinkToFit
src/Vsdx.Protocol/DesignSpec.cs:256           仅赋 Unit，无换算
src/Vsdx.Protocol/DesignSpec.cs:306-400       Evaluate()：7 个 case + default
src/Vsdx.Protocol/ExecutionResult.cs:167-188  BuildMapping：首页 + 顶层简写
src/Vsdx.Protocol/ProtocolRunner.cs:74-87     spec==null 才跑 DiagramSizer.Apply
src/Vsdx.Protocol/ProtocolRunner.cs:191       IR 版本校验（spec 版本无对应校验）
src/Vsdx.Protocol/ProtocolRunner.cs:238       nodeIds 为页内局部变量
src/Vsdx.Protocol/DiagramIr.cs:282            lane → container（B6）
src/Vsdx.Protocol/DiagramIr.cs:297            Bounds(0,0,2.0,0.9) 写死
src/Vsdx.Protocol/DiagramIr.cs:304            仅 Mode=="fixed" 被采纳
src/Vsdx.Engine/LayoutStrategies.cs:109       lane 判据（协议路径上恒假）
src/Vsdx.Engine/LayoutStrategies.cs:136-145   layout 内清空 waypoints
src/Vsdx.Engine/LayoutStrategies.cs:174-190   ClampDecorationsToPage 静默挪动
src/Vsdx.Engine/CandidateComparison.cs:141-165 现有 Transpose（清空 waypoints）
src/Vsdx.Engine/DiagramSizer.cs:39-52         按文字推导框体（spec 路径缺失）
src/Vsdx.Engine/DiagramSizer.cs:118-181       FitPage：仅增、可缩
src/Vsdx.Engine/TextLayout.cs:12,19-24        TypeScale 默认档位
src/Vsdx.Adapters/OfficeImoDocumentWriter.cs:48-50   页面尺寸恒按 IN 写
src/Vsdx.Adapters/OfficeImoDocumentWriter.cs:219     容器字号默认 12（可覆盖）
src/Vsdx.Adapters/OfficeImoDocumentWriter.cs:377-380 Edge 标签字体（只写）
src/Vsdx.Adapters/OfficeImoDocumentWriter.cs:437-441 容器 SetFontSize
src/Vsdx.Adapters/OfficeImoDocumentReader.cs:604-623 ReadStyle（读 TextStyle）
src/Vsdx.Adapters/OfficeImoDocumentReader.cs:627-676 ReadEdge（无 LabelFont）
src/Vsdx.Adapters/VisioTextBlockBridge.cs:25-33      第二份字号表（与 TypeScale 冲突）
src/Vsdx.Adapters/VisioTextBlockBridge.cs:141,143-144 Font 单元格 + 8pt 硬夹紧
src/Vsdx.Adapters/VisioFreePathBridge.cs:170-229      局部坐标发射
src/Vsdx.Adapters/OfficeImoIncrementalEditor.cs:59-70 既有单位换算（双重换算风险）
src/Vsdx.Model/DiagramModel.cs:19,21          PageSize / Orientation
src/Vsdx.Model/DiagramModel.cs:224-225        FromPort/ToPort（空间字段，计划遗漏）
src/Vsdx.Model/DiagramModel.cs:317            DiagramStyle.Palette 声明（无读取方）
schemas/phase7/design-spec.schema.json        7 项约束枚举；additionalProperties:false（未被 CLI 执行）
schemas/phase7/diagram-ir.schema.json         node 无 text/shape/style（C5 的根基）
```
