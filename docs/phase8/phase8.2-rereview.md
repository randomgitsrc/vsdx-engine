# Phase 8.2 定向复审（M1–M12 闭环核验）

**复审对象（本轮实际读取的版本，md5 固定）：**

| 文件 | md5 | 行数 |
| --- | --- | --- |
| `phase8.2-audit.md` | `42e5375960b2f4546bea0531065d270e` | 719 |
| `phase8.2-plan.md` | `42c6f7cd8cad750ee6735db6316e210f` | 968 |
| `phase8.2-review-packet.md` | `a0a6071c466a3eb27ecee5805f24e597` | 136 |

**复审基线：** `HEAD=a0672f8`（三份文档已在复审期间由 `6d80ce7`、`a0672f8` 两次提交落库，
内容与上表 md5 一致，**正文未再变动**）。
**复审日期：** 2026-11-17
**复审者立场：** 与 Stage 3 同一审查者。本轮不采信修订说明，逐条复验。

---

## 1. 复审方法与独立性

### 1.1 证据等级（沿用上轮）

| 等级 | 类型 | 本轮用途 |
| --- | --- | --- |
| E1 | 端到端实测 + 回读 + 解包 XML | 判定 M2/M3/M4/M6/M8/M9 的行为主张 |
| E2 | 源码定位（file:line） | 判定 M1/M3/M8/M9/M10/M11 的可行性 |
| E3 | 文档陈述 | **仅用于定位待验命题，不作结论依据** |

实测产物全部在 `.review-scratch-r2/`（gitignored），可复现。

### 1.2 已执行的操作

```console
$ dotnet build src/Vsdx.Cli/Vsdx.Cli.csproj -c Release
  已成功生成。0 个警告 0 个错误
$ dotnet test tests/Vsdx.Tests/Vsdx.Tests.csproj -c Release
  已通过! - 失败: 0，通过: 373，已跳过: 0，总计: 373
```

基线独立复现成功。**产品代码零改动**（`git diff --stat -- src/ tests/` 为空）。

### 1.3 关于"文档在复审期间再次变动"的声明

复审过程中，缺陷总数由 14 → 15 → 16 变动两次（缺陷 15 装饰夹紧、缺陷 16 容器顺序），
三份文档同步改了未提交版本，并在复审末尾由 `6d80ce7` / `a0672f8` 落库。
**本文所有结论针对 §1 表中 md5 固定的版本**（与 `a0672f8` 内容一致）。
若继续变动，本文 file:line 引用可能漂移。

---

## 2. 逐项结论

### 2.1 总表

| # | 要求 | 结论 | 一句话依据 |
| --- | --- | --- | --- |
| **M1** | 补 `DiagramSizer.Apply`、修数据流图、重写 §12 | **ADDRESSED** | 图已含该阶段；§12 不再说"提前"；拆分可实现（2 处需补充说明） |
| **M2** | 重定义 `mode=fixed`、删"功能正确"判语 | **PARTIALLY** | 缺陷 13 与 T4b 已加且可证伪；但审计 §8 的"**功能正确**"**仍在** |
| **M3** | `DiagramIr.cs:282` 保留 `lane` | **ADDRESSED** | 改写仍在（:282）；保留后确实选中 Swimlane，且几何与 layered 不同 |
| **M4** | 补 Reader 字体回读 | **PARTIALLY** | Edge 标签：可行且充分；TextBlock：写侧本身不合规 + 未登记 FaceName + 需改 `Visio*Bridge`（§5 声明不触碰） |
| **M5** | §4 补齐、清混合键 | **PARTIALLY** | 表 16/16 齐全、§4.1 已清；但"16⇄16⇄16"**双向映射为假**，packet 仍写 14 |
| **M6** | 换真 IR 输入 | **ADDRESSED** | 三份 IR 为真 IR，标签完整回读，评分复现 |
| **M7** | 机制性闸门 | **PARTIALLY** | 闸门 1/2 是机制级；闸门 3 是**枚举白名单**，且与 T1b 矛盾 |
| **M8** | 补转置清单、定 waypoint 时序 | **PARTIALLY** | 清单扩了；但**新增 §11.2 与 §11.4 自相矛盾**，验收 4 与 7 互斥 |
| **M9** | `Canvas.Unit` 契约 | **PARTIALLY** | 契约正确、双重换算已证实；但 §7/§3 仍写"标签 cm"，§17.2"唯一换算点"漏掉 `create` |
| **M10** | 背景实现路径与顺序 | **PARTIALLY** | 路径 A 选对；但顺序分析**三条事实全错**（见 §2.10） |
| **M11** | 统一字号表 | **PARTIALLY** | 三表不一致确认；Model 下沉可行；但 `TextScale` 写错、caption 目标值未给 |
| **M12** | 修正材料包 | **PARTIALLY** | 点名的 4 项全修；仍留 packet 2 处"14 项"、"18 节"、破坏性变更清单与 plan §7 冲突、工时表未含新增缺陷 |

**ADDRESSED 3 项 / PARTIALLY 9 项 / NOT ADDRESSED 0 项。**

---

### 2.2 M1 —— 已解决，但需补两条实施细节

**(a) 图是否含该阶段：是。** §1.2 现为（plan:43-60）：

```
ResolvedStyle ──▶ 模型对象（含字号）
                        │
                        ▼
        **DiagramSizer.Apply** ← spec 路径当前缺失
                │  （字号→文字占位→框体尺寸）
                ▼
        DiagramLayoutEngine.Apply（布局）
```

**(b) §12 是否还开"把字号提前"的方子：否。** §12.1 标题即"真正的根因：阶段缺失，不是顺序"，
§12.3 结论句为"**这不是"调整顺序"，是"把缺的环节装回去"**"（plan:578）。

**(c) 拆分是否可实现：可实现，不是手挥。** 我按 `DesignSpec.cs:127-300` 逐段核对：

| 现方法分段 | 行 | 是否纯样式（无几何） | 归属 |
| --- | --- | --- | --- |
| 策略选择 + `DiagramLayoutEngine.Apply` | 127-164 | ❌ 几何 | `ApplyLayoutAndCanvas` |
| `DiagramLayout.Apply` | 166 | ❌ 几何 | 同上 |
| Typography | 169-195 | ✅ 只写 `Style.FontSize/Font` | `ApplyStyleOnly` |
| 容器色硬编码 | 199-226 | ✅ 只写 `group.Style` | 同上 |
| `RoleColors` | 228-240 | ✅ 只写 `node.Style` | 同上 |
| `Background` | 242-247 | ✅ 只写 `Diagram/Page.Background` | 同上 |
| `Canvas` + `FitPage` | 249-287 | ❌ 几何 | `ApplyLayoutAndCanvas` |
| `Evaluate` 约束 | 289-300 | ❌ 依赖最终几何 | 必须最后 |

**四个样式块连续且几何无关**，`ApplyStyleOnly` 是纯提取，`ApplyLayoutAndCanvas` 收 127-166 + 249-300。
拆分在结构上成立。

**两条需要补充说明（非阻断）：**

1. **`applied` 顺序会变。** 现顺序是 `layout=…` → `container.bands` → `typography` → …；
   拆分后 `typography` 会跑到最前。若有测试或调用方断言 `appliedDesignNotes` 顺序，会破。
   计划未提。
2. **`DiagramSizer.Apply` 会用错画布尺寸。** `DiagramSizer.cs:45-48` 以
   `page.Size.Width/Height` 推 `TypeScale.ForCanvas`；而拆分后 canvas 还没设置，
   此时是默认 11×8.5。若 spec 只声明 `canvas.fixed 20×14` 而无 `typography`，
   字号会按 11×8.5 推，再放到 20×14 页面上——**偏小**。

   **这不是推测，我算了也实测了。** `TypeScale.ForCanvas(11, 8.5, 3)`
   → `diag=13.90`、`sizeFactor=0.75`（触底）、`densityFactor=1.30`（触顶）、
   **`NodeTitle = 10.725`**。我用 `create --fit-text` 在 11 pt 下量得
   `[1.30, 2.20, 2.575]`，而不带 spec 的实测是 `[1.30, 2.15, 2.525]`——
   比值 `11/10.725 = 1.0256`，预测 `2.15×1.0256 = 2.205`（实测 2.20）、
   `2.525×1.0256 = 2.590`（实测 2.575）。**吻合。**
   即"11 pt 的框体"与"无 spec 的框体"本来就不相等，因为后者用的是 10.725 pt。

   这使 §12.4 断言 1（"同一字号下带 spec 与不带 spec 的框体必须一致"）**在页面尺寸不同时必然失败**，
   断言 5 的 0.05" 容差同理需要限定条件。计划未限定。

> 结论：M1 的**三项要求全部落实**，拆分可实施。上述两点是实施细节，不构成阻断。

---

### 2.3 M2 —— 任务已加，但"删除功能正确判语"这条明确要求没做

**缺陷 13 与 T4b 确实存在且可证伪：**

- audit §2.9（:449-478）已列 P0，含"缩小了 78%，且报告成功"；
- plan §4 第 13 行 → `T4b`；§9 `T4b | mode=fixed 语义 | 请求 20×14 → 实得 20×14，或 unmet 有明确 entry`。

**实测复现（你说 4.00×3.45，我得到同量级不同值，原因见下）：**

```console
$ execute --ir one.ir.json --spec '{"canvas":{"mode":"fixed","width":20,"height":14}}'
  applied: ['layout=Layered','container.bands','canvas.fixed+shrunk']
  unmet  : []
  page   : 4 x 3.45 in          ← 请求 20×14
```

我用 1 节点 IR 得 **4.00×3.45**；3 节点得 **5.95×5.875**；2 节点得 **4.00×5.875**。
**具体数值随内容变化**，但"请求 20×14、实得远小、`unmet=[]`"这一结论稳定成立。
缺陷成立。

**但 M2 的第二条要求没有执行。** 我的 M2 原文是"**删除审计 §8「功能正确」的判语**"。
审计 §8 现为（audit:638-639）：

```
`Canvas.ShrinkToFit` 已补实测：请求 20×14 时，`true` 收缩到 4.00×3.45，
`false` 保持 20×14。**功能正确。**
```

紧接其上的表格行已加注"但默认 true 时 `mode=fixed` 失去意义，见 §2.9"，
**但结论句"功能正确"原封不动**。这正是 M2 点名要删的那句。

**另一个 M2 未覆盖的失败方向：`fixed` 还会**向上**越界。** 实测：

```console
$ execute --ir arch.ir.json --spec '{"canvas":{"mode":"fixed","width":8,"height":6}}'
  applied: ['canvas.fixed']     ← 注意不是 fixed+shrunk
  unmet  : []
  page   : 11.35 x 9.85 in      ← 请求 8x6
```

`FitPage(shrink:true)` 在内容装不下时走 `mustGrow` 分支（`DiagramSizer.cs:153-161`），
**把页面撑大到 11.35×9.85**，且因为 `Shrank=false`，`applied` 只写 `canvas.fixed`。
所以 `mode=fixed` 既会**小于**请求值、也会**大于**请求值，两个方向都静默。

**修法是否 sound：**

- 审计 §2.9 推荐"方案 1：fixed 时忽略 shrinkToFit"——**方案 1 能同时修好两个方向**（不再调 `FitPage`），成立。
- 但 T4b 的验收只测了**收缩方向**（20×14）。若实现者选方案 2（"收缩后进 unmet"），
  **撑大方向仍不会被报告**——因为撑大不是 `shrink`。方案 2 的表述不完备。

> 结论：**PARTIALLY**。任务与可证伪验收已到位（M2 的主要诉求达成），但
> ①明确要求的"删除功能正确判语"未做；②验收未覆盖撑大方向，方案 2 不完备。

---

### 2.4 M3 —— 已解决

**`DiagramIr.cs:282` 仍在改写：**

```csharp
Kind = container.Kind is "lane" ? "container" : (container.Kind ?? "container"),
```

**保留 `lane` 后能否选中 Swimlane：能。** 我在 **`create` 路径**（不经 IR 转换，
可保留 `Kind="lane"`）实测两条策略：

```console
$ create --input lane.model.json --layout swimlane
  page 10x8  nodes [a(1.5,2.98) b(3.35,2.98) c(1.5,0.85)]
$ create --input lane.model.json --layout layered
  page 10x8  nodes [a(1.5,2.30) b(3.35,2.30) c(1.5,4.42)]
```

**几何确实不同**，所以 T0b 的验收（"`applied` 含 `layout=Swimlane` 且几何与 layered 不同"）
**可达**。`SuggestStrategy` 的判据是 `page.Groups.Any(g => g.Kind == "lane")`
（`LayoutStrategies.cs:109`），保留即命中。

**除 Swimeley 外还有谁依赖 `Kind`：** 我把 `src/` 全量 grep 后确认只有三处：

| 位置 | 判据 | 保留 `lane` 后的影响 |
| --- | --- | --- |
| `LayoutStrategies.cs:109` | `Kind == "lane"` | ✅ 正是要修的 |
| `SwimlaneLayoutEngine.cs:21` | `Kind == "lane"` | ✅ 同上 |
| `OfficeImoIncrementalEditor.cs:630` | `Kind == "group"` | 无影响（lane ≠ group） |

**但有一个必须提醒的副作用：`Kind` 不参与往返。**
`OfficeImoDocumentReader.cs:542` 读回时**无条件**写 `Kind = "container"`。
我实测验证：

```console
$ create lane.model.json (Kind=lane) -> export-model
  groups: [('L2','container'), ('L1','container')]     ← lane 丢失
```

后果：**T0b 的验收不能用"回读 `kind == lane`"作为判据**，只能用几何或 `applied`。
计划 §9 的 T0b 验收写的是"`applied` 含 `layout=Swimlane` 且几何与 layered 不同"，
**恰好避开了这个坑**，这是正确的。但计划未说明"`Kind` 不回读"这一事实，
实施者若顺手加一条回读断言会踩空。

> 结论：**ADDRESSED**。缺陷、修法、验收三者对齐，且验收判据已避开已知盲区。

---

### 2.5 M4 —— Edge 标签可行，TextBlock 一半不成立

**Edge 标签：前提成立、修法可行。**

`OfficeImoDocumentReader.cs` 确实**没有** `LabelFont` 映射（全文 grep 无结果），
而 Writer 确实写了。我解包 `page1.xml`：

```xml
<Section N="Character"><Row IX="0">
  <Cell N="Font" V="Georgia" /><Cell N="Color" V="#AA0000" />
  <Cell N="Size" V="0.180555555555556" U="PT" /><!-- 13pt -->
</Row></Section>
```

且 `document.xml` 里 **Georgia 已登记进 FaceNames**（`grep -c Georgia = 1`）。
回读仍是 `labelFont=None`。所以：**写侧合规、读侧缺失**——补 Reader 映射即可，
"`labelFont=Georgia` → 回读 Georgia"**可达**。

**TextBlock：写侧本身就有问题，"补 Reader"不足以达成验收。**

我用模型指定 `textBlocks[0].style.font = "Verdana"` 走 `create`，解包结果：

| 检查项 | 结果 | 含义 |
| --- | --- | --- |
| `page1.xml` 有 Font 单元格 | `<Cell N="Font" V="Verdana" />` | 写进 page 了 |
| 但位置 | **形状级裸 Cell**，不在 `Section N="Character"` 里 | 非 Visio 合规位置 |
| `document.xml` 是否登记 Verdana | **0 次** | 字体表里没有 |
| 回读 `style.font` | **缺失**（只有 `fontSize: 15.999984`） | 读不回 |

对比 Edge 标签（Writer 走 OfficeIMO `SetFont`，生成合规的 Character section + FaceName），
**TextBlock 的 Font 是 `VisioTextBlockBridge.cs:141` 用 `AddCell(shape, "Font", …)` 直写的裸单元格**。
Reader 的 `ReadStyle`（`:604-620`）读的是 `shape.TextStyle.FontFamily`，
既拿不到这个裸 cell，字体也没进表。

**这带来一个 M4 未处理的范围冲突：** 修 TextBlock 字体需要改
`VisioTextBlockBridge.cs`（改 Character section + 登记 FaceName），
而计划 §5 明确写"**不触碰：** `FidelityGuard` / `Visio*Bridge` / 协议版本号"（plan:249），
§1.2 又说"**唯一例外：`Theme.Background`** 必须改 Writer/Reader"（plan:75-76）。
**于是：M4 的一半要求，撞在 §5 的"不触碰"红线上，计划没有解这个结。**

> 结论：**PARTIALLY**。Edge 标签（M4 的主诉）已正确覆盖且可达；
> TextBlock 字体被低估为"Reader 缺失"，实为"写侧不合规 + 未登记 + 需改 Bridge"，
> 与 §5 的边界声明直接冲突。

---

### 2.6 M5 —— 表齐了，但"一一对应"是假的

**逐项核对：**

| 检查项 | 期望 | 实测 | 结论 |
| --- | --- | --- | --- |
| §4 标题数 | 16 | "十六项缺陷的修法"（plan:185） | ✅ |
| §4 表体行数 | 16 | 程序化统计 = **16** | ✅ |
| audit §10 清单 | 16 | 表体 16 行（audit:631-645） | ✅ |
| audit 结句 | 16 | "16 项缺陷中，初稿漏了 10 项"（audit:718） | ✅ |
| review-packet | 16 | 第 16 行"**16 项缺陷**" | ✅ |
| §4.1 是否还提混合键 | 否 | "原版…**已被负责人否决**"、"**不采用动态混合键**"（plan:216,229） | ✅ |
| 每行映射到一个任务 | 16 个任务 | **❌ 见下** | ❌ |

**"16 项缺陷 ⇄ 16 行修法 ⇄ 16 个任务，一一对应"（plan:212）为假。** 程序化比对 §4 与 §9：

```
§4 引用的任务: T0 T1 T10 T2 T3 T4 T5 T7 T6 T0.5 T2b T4b T0b T8a T1b
§9 实际列出的: T0 T0b T4b T1 T1b T2 T2b T3 T4 T5 T6 T7 T8 T8a T9 T10
```

两个方向都不闭合：

1. **`T0.5` 被缺陷 11 引用，但 §9 里没有这条任务。** `grep T0.5` 在 plan 只命中 2 处
   （:205 §4 表、:537 §12 标题），**§9 的任务表 0 处**。而 T0.5 恰恰是 M1 的落地任务。
2. **`T8`（三套产物）与 `T9`（全量回归）在 §9 里，但没有任何缺陷行指向它们。**

行数 16 = 16 = 16 是**巧合**：一边多出 T0.5、另一边多出 T8/T9，正好抵消。
M5 的核心判据是"文档内 N 项缺陷 ⇄ N 行修法 ⇄ N 个任务，**一一对应**"，
按这个判据，**M5 未达成**。

**另有 §5 文件表的三处硬伤（M5 要求"统一 Writer 范围表述"，此处仍未统一）：**

| 问题 | 证据 |
| --- | --- |
| `OfficeImoDocumentWriter.cs` **仍然出现两行**（上轮 C1.2 已指出，未修） | plan:241 与 plan:243，内容几乎相同 |
| "`DiagramIr.cs` **无需改动**（字段已够）" | plan:242。**但 T0b 改 `DiagramIr.cs:282`、T1b 改同文件内的 `DiagramIrConverter`（`:232`）** — 自相矛盾 |
| 缺 `LayoutStrategies.cs`（T0/T8a 必改）、缺 `VisioTextBlockBridge.cs`（M4 TextBlock 必改）、缺 `Program.cs`（M7 闸门所在） | 全表 grep 无这三项 |

> 结论：**PARTIALLY**。表体补齐这件事**做对了**，混合键也清干净了；
> 但"一一对应"不成立、§5 仍自相矛盾且遗漏 3 个必改文件。

---

### 2.7 M6 —— 已解决（输入格式这条）

**三份文件真实存在且是真 IR：**

| 文件 | 顶层键 | `irVersion` | `modelVersion` | 节点/边/文本块/路径 |
| --- | --- | --- | --- | --- |
| `examples/phase8/arch.ir.json` | irVersion,id,title,kind,containers,nodes,edges,textBlocks | `"1.0"` | **无** | 10 / 9 / 4 / 0 |
| `examples/phase8/flow.ir.json` | 同上 | `"1.0"` | **无** | 11 / 12 / 2 / 0 |
| `examples/phase8/overview.ir.json` | 同上 + `paths` | `"1.0"` | **无** | 10 / 9 / 4 / 1 |

与旧 `benchmarks/*.json`（顶层 `modelVersion`/`pageSize`/`nodes[].text`）**格式不同**，
是真 `DiagramIr`。计划 §6.4 的"10/9/4、11/12/2、10/9/4/1"**逐项对得上**。

**标签确实可读（对照旧输入的失败）：**

```console
$ execute --ir examples/phase8/arch.ir.json -> export-model
  nodes: ('web','Web 门户\nReact SPA') ('mobile','移动端\niOS / Android') ('gw','API 网关\nKong') …
  edges: ('e1','HTTPS') ('e2','gRPC') ('e3','verify') …
  text : ('title','title','企业分析平台') ('subtitle',…,'Enterprise Analytics Platform') …
```

`'Web 门户\nReact SPA'` **完整保留**（旧 `benchmarks` 输入会退化成 `'web'`）。
换行也保住了。

**执行结果复现（计划声称 succeeded / hard=True / 92-100-92）：**

```console
arch     status succeeded hard True score 92  issues ['edge.crossing','text.overflow-horizontal']
flow     status succeeded hard True score 100 issues []
overview status succeeded hard True score 92  issues ['edge.crossing','page.margins']
```

**与你报的数字逐项一致。**

> 结论：**ADDRESSED**。M6 的实质诉求（验收不得喂错格式）已解决。
> 9 组 VSDX+PNG 尚未产出，但那是 T8 的实施内容，不属 M6 的闭环判据。
> 附注：`arch` 当前带 `text.overflow-horizontal`，而 §6.4 断言 4 要求"overflow 计数为零"——
> 这是"修前基线"，修后须归零，验收本身可证伪，无问题。

---

### 2.8 M7 —— 两条机制级 + 一条假装成闸门的枚举

**闸门 1（未知成员）：机制级，但计划的写法自相矛盾。**

我写了一个最小工程实测 `System.Text.Json` 在本项目 SDK（8.0.428 / ref pack 8.0.34）下的行为：

```console
API present: Disallow
Disallow THREW: JsonException msg=The JSON property 'typografy' could not be mapped to any
```

即 `UnmappedMemberHandling.Disallow` **会抛异常中断反序列化**，拿不到"部分解析结果 + 未知成员清单"。
而计划 §15.2 的示例同时要求：

```csharp
public sealed class IrParseResult {
    public DiagramIr? Value { get; init; }                    // 要拿到值
    public List<string> UnknownMembers { get; init; } = new(); // 又要拿到清单
}
```

**这两者不能靠 `Disallow` 同时成立。** 计划用"（或自定义 converter 收集未知键）"留了口子，
**那个口子才是可行路径**，但正文把 `Disallow` 摆在前面，实施者很可能照抄前者。

另有一个**位置问题**：闸门放在 CLI（`Program.cs:715-721` 构造 `JsonSerializerOptions`），
而 `ProtocolRunner.Execute(DiagramIr, DesignSpec, …)` 收的是**已反序列化的对象**
（`ProtocolRunner.cs:69`）。库内调用方（tests、`BatchRunner`、`CandidateComparison`）
**绕过闸门**。这不影响 CLI 验收，但"M7 机制性封闭"只在 CLI 边界成立。

**闸门 2（枚举白名单）：机制级，可接受。** 对 `layout/direction/palette/role/kind/…
` 统一白名单，确实封住了"未知枚举值静默回退"这一类。与 `role` 的 Schema 已有 `enum`
（`diagram-ir.schema.json`）一致，不算新发明。

**闸门 3（未消费字段登记）：这是枚举，不是机制。**

```csharp
private static readonly string[] NotYetExecuted = { "containers[].order", "metadata.*" };
```

一个**手写常量数组**，新字段默认不在里面，只能靠人记得加。
这与"逐项枚举缺陷"是同一模式，只是从"枚举缺陷"换成"枚举死字段"。
按 M7 的原始判据（"引入机制性闸门而非逐项枚举"），**闸门 3 不达标**。

**更严重：闸门 3 与 T1b 直接矛盾。**

- §15.1 残留表（plan:745）把 `` `IrContainer.Order` | 协议接受，从不执行 `` 列为"**修完 16 项后仍存在**"；
- 但缺陷 16 / T1b（plan:209, :401）的修法正是"**转换器消费 `Order`**"，验收是"`flow.ir.json` 中 `intake` 的 Y 最大"。

**同一个字段，一边说"本阶段不执行、登记为未消费"，一边说"本阶段实现它"。**
实施者按 §15.2 会把它写进 `NotYetExecuted`，按 T1b 又会消费它——两条指令互斥。

> 结论：**PARTIALLY**。闸门 1/2 是机制级（闸门 1 需改用自定义 converter 并说明库内绕过问题）；
> 闸门 3 是枚举，且与 T1b 冲突。

---

### 2.9 M8 —— 清单扩了，但引入了新的自相矛盾

**清单是否完整（对 `DiagramModel.cs` 逐字段核）：**

已覆盖：`Node.Bounds`/`Angle`、`Group.Bounds`、`Edge.FromPoint/ToPoint/Waypoints/FromPort/ToPort`、
`Page.TextBlocks[].Bounds`、`Page.Paths[].Bounds`/`Angle`、`PathRow.Cells`、
`Page.Size`、`Diagram.PageSize`、`Diagram.Orientation`、
`ShapeStyle.PaddingX/HeadingHeight/PaddingBottom`。

**仍缺两项空间载荷**（与"遗漏项"同性质，只是藏在容器里）：

| 未覆盖 | 位置 | 为什么算空间数据 |
| --- | --- | --- |
| `Node/TextBlock/FreePath.NativeCells` | `DiagramModel.cs`（三处都有） | 注释为"Native Visio cells preserved verbatim"——可含 `PinX/PinY/Width/Height` |
| `UnmappedObject.RawXml` | `DiagramModel.cs:352-359` | 整段原始形状 XML，含坐标 |

二者都是"模型表达不了的坐标原样留存"，转置不会碰它们，转置后会出现"框转了、这些形状没转"。
计划 §11.1 的标准是"全部空间数据"，按该标准**仍不完整**。

**waypoint 时序：修对了，但修出了矛盾。**

先说做对的部分：§11.2 正确识别 `LayoutStrategies.cs:142` 在布局期无条件清空
（我确认该行存在，且 `CandidateComparison.cs:158` 同样清空），
并把"非 manual 下不转置"写进了验收 4 的后半句。这解决了上轮"验收 4 不可达"的一半问题。

再说新引入的矛盾——**§11.2 与 §11.4 直接对立**：

| 位置 | 主张 |
| --- | --- |
| §11.2 表（plan:474） | `Layout == manual` → "**保留并转置**" |
| §11.2 正文（plan:476-477） | "`Direction + manual` 是**有意义的组合**……请求纵向排列时应整体转置，而折点必须跟着转" |
| §11.2 验收（plan:479-480） | "在 `layout=manual` + 显式 waypoints 下，`direction=topToBottom` 后 waypoints 逐点转置且非空" |
| **§11.4（plan:507-508）** | "`Direction` 在 manual 下**不应生效**，且必须进 `unmetConstraints`" |
| **§11.6 #7（plan:532）** | "**Manual 模式不转置**，且 `unmetConstraints` 有记录" |
| §11.6 #4（plan:528） | "**`layout=manual` 下 Waypoints 逐点转置**（非清空）" |

**§11.6 的断言 4 与断言 7 互斥**，§11.2 与 §11.4 互斥。这不是措辞问题：
按 §11.4 实现，就没有 manual 转置；按 §11.2 实现，就违反 §11.4 的 unmet 要求。

**而且 §11.2 的验收在当前代码上不可达。** 我核了两点：

1. **IR 无法携带 waypoints。** `IrEdge`（`DiagramIr.cs:170-186`）只有
   `Id/From/To/Relation/Label/FromPort/ToPort`，**没有 `Waypoints`**。
2. **`layout=manual` 下根本没有转置阶段。** `DesignSpecApplier` 的 manual 分支
   （`DesignSpec.cs:161-164`）只执行 `applied.Add("layout=manual")`，**不调任何引擎、不调转置**；
   带 `Direction` 的 `LayoutOptions` 只在非 manual 分支构造（`:130-135`）。

所以"`layout=manual` + 显式 waypoints + `direction=topToBottom`"只能靠**手工构造 `Diagram`
直接调引擎**才能凑出来——**这正是上轮把 M8 判为问题的理由：假绿**。
修订把这个验收从"不可达"改成了"另一个不可达"。

**补充：我实测确认 manual 下 waypoints 确实保留（未被清空）：**

```console
$ create --layout manual  (显式 3 个 waypoints) -> export-model
  waypoints: [(3.0,1.5), (3.0,5.0), (4.5,5.0)]     ← 原样保留
$ create --layout layered -> export-model
  waypoints: [(1.6,2.7)]                            ← 被重路由
```

即"manual 不清空"这一前提是对的，缺的是"谁来转置"。

> 结论：**PARTIALLY**。清单扩了、时序障碍识别对了，但
> ①仍缺 `NativeCells`/`RawXml`；②§11.2 与 §11.4 互斥、断言 4 与 7 互斥；
> ③manual+direction 的验收在当前代码上不可达（IR 无 waypoints、manual 无转置阶段）。

---

### 2.10 M9 —— 契约对，但支撑证据被改成了错的

**双重换算风险：真的，我复现了。**

```console
$ execute --ir ... --spec '{"canvas":{"mode":"fixed","width":10,"height":8,"unit":"cm"}}'
  page: 10 x 8 in                        ← execute 完全不换算
$ create  (模型 size=10x8 cm) -> export-model
  page: 10 x 8 in                        ← create 也不换算
$ update  (同一模型) -> export-model
  page: 3.937 x 3.150 in                 ← update 换算
```

`OfficeImoIncrementalEditor.cs:62` 确实调 `MasterCatalog.UnitFactor`，
`MasterCatalog.cs:70-77` 定义 `"cm" => 1/2.54`、**`_ => 1.0`**。
所以契约"上游归一为英寸、`UnitFactor("inches")==1`"**算术上成立**，能避免双重换算。
这条修法是对的。

**但支撑证据被改错了——这是本轮最重要的一处新错误。先说我自己的责任。**

**我上轮报告（`phase8.2-independent-review.md:68`，A.1 第 4 行）本身就是自相矛盾的：**

> ……实测解包 `pages.xml` 得 `<Cell N="PageWidth" V="10" U="IN"/>`，全文无 `U="CM"`。
> 单位标签**根本没写进文件**，不存在"标签与数值不一致"。

同一条目里，前半句**明确引出了 `U="IN"`**，后半句却断言"根本没写进文件"。
**这是我的错误，修订方是沿着它走的。** 责任不在修订方一人。

**修订把这个错误放大成了更强、更明确的错：**

审计 §2.4 现在写（audit:270-273）：

> 实测解包 XML 后**既无 `U="CM"` 也无 `U="IN"`**。**单位标签根本没写进文件。**

并署名"（独立审查 **A9/B8** 纠正）"。**三点都不成立：**

1. **"也无 `U="IN"`"是新增的、可证伪的错误陈述。** 我原文只说"无 `CM`"，
   并未说"无 `IN`"——修订把一个"部分正确"改成了"两个都不对"。
2. **`U="IN"` 确实存在**，只是**不在 `page1.xml`、而在 `visio/pages/pages.xml`**：

```xml
<!-- visio/pages/pages.xml -->
<Page ID="0" …><PageSheet …>
  <Cell N="PageWidth"  V="11" U="IN" />
  <Cell N="PageHeight" V="9.4" U="IN" />
```

   用 `canvas.unit=cm` 请求 10×8 时，`pages.xml` 仍是 `<Cell N="PageWidth" V="10" U="IN"/>`。
3. **章节引用错了。** 我上轮提出单位问题的是 **A.1 第 4 行 / §B7**；
   **A9/B8 是 `mode=fixed`**（`mode=fixed` 名存实亡）。把单位结论署给 A9/B8 是张冠李戴。

**准确的事实（我本轮复测确认）：** Writer 硬传 `VisioMeasurementUnit.Inches`
（`OfficeImoDocumentWriter.cs:50`），所以**标签永远写 `IN`**，而**数值原样照抄调用方**。
于是"请求 10 cm"被写成"10 IN"，**输出比意图大 2.54 倍，但 XML 自洽、不报错**。
危险不在"标签缺失"，也不在"标签与数值不一致"，而在"**标签恒为 IN 而数值未换算**"。

**由此派生的三处仍在传播的旧表述（与 audit §2.4 的更正互相矛盾）：**

| 位置 | 仍写 | 与更正后的 §2.4 冲突 |
| --- | --- | --- |
| audit §2.4 标题（:257） | "`Canvas.Unit` **只写标签**不换算" | 标签非"写错"，是"恒为 IN" |
| audit §10 缺陷 6（:706） | "`Canvas.Unit` **只写标签**不换算" | 同上 |
| plan §3 兼容性（:361） | "以前**写错标签**，现在换算正确" | 同上 |
| plan §7（:370） | "现状是\"**标签写 cm**、数值仍按英寸\"" | **明确错**：标签是 IN，从不写 cm |
| plan §4 第 6 行（:200） | "Writer 硬传 Inches，**标签未写入文件**" | **明确错**：`U="IN"` 在 pages.xml |

即：审计正文改了、标题没改；计划的 §4 跟着改了、§7 没改。
**计划 §7 与审计 §2.4 就同一事实给出相反陈述。**

**另有两处 M9 未处理：**

- **§17.2 说"唯一换算点：`OfficeImoIncrementalEditor`（已有）"（plan:899）——不准确。**
  `create` 路径（`OfficeImoDocumentWriter.cs:44-50`）把 `pageModel.Size.Width` **直接当英寸**传给
  `MasterCatalog.Inches(...)`，**一次换算都不做**（我实测 10×8 cm → 10×8 in）。
  所以现状不是"一个换算点"，而是"`update` 换算、`execute`/`create` 全不换算"。
- **§17.3 断言 3（"读回 `unit` 字段 = `inches`"）是恒真命题。**
  `UnitFactor` 不认识的值回落到 1.0，且 Reader 从不写 `cm`——我实测任何路径回读都是 `inches`。
  这条断言**证明不了归一化发生过**。

> 结论：**PARTIALLY**。契约与算术正确、双重换算风险已证实并被正确规避；
> 但支撑事实被改错（`U="IN"` 存在）、误署我的名字、且该错误在 audit 标题与 plan §3/§7 中继续传播。

---

### 2.11 M10 —— 路径选对了，顺序分析三条全错

**路径 A（PageSheet）是对的。** 理由也成立：背景是页面属性，不该进 `ShapeOrder`。
验收 3（"`ShapeOrder` 长度不变"）是个好的反向断言。

**但 §18.3 的"重写顺序"整段建立在错误事实上。** 计划写（plan:953-963）：

> 当前保存后**依次**执行：1. Nesting 2. FreePath 3. TextBlock 4. **ZOrder** 5. **Master** 6. [新增] Background
>
> **背景桥必须最后**，因为前面每个桥都会 `entry.Delete()` + 重建，若背景先写会被覆盖掉。

我核了五个桥的实际调用点，**三处不实**：

| 计划说法 | 实际 | 证据 |
| --- | --- | --- |
| 六个桥在一条序列上"依次执行" | **这是两条路径的合并**。`create` 只跑 Nesting/FreePath/TextBlock/Metadata（**无 ZOrder、无 Master**）；`update` 跑 FreePath/TextBlock/ZOrder/Master/Metadata（**无 Nesting**） | `OfficeImoDocumentWriter.cs:157-166` vs `OfficeImoIncrementalEditor.cs:297-307` |
| "每个桥都会 `Delete()` + 重建，先写会被覆盖" | **错。每个桥都是 `XDocument.Load` → 改 → `Delete` → 重建 → `doc.Save`**，即**读-改-写**。它只动自己关心的节点，其余原样写回。**先写的不会被覆盖** | `VisioNestingBridge.cs:83,137-140`；`VisioFreePathBridge.cs:55,148-151`；`VisioTextBlockBridge.cs:63,105-108`；`VisioZOrderBridge.cs:53,109-112`；`VisioMasterBridge.cs:54,82-85` |
| 背景桥与"三处 `pageN.xml` 重写"争同一条目 | **错。`PageSheet` 在 `visio/pages/pages.xml`，不在 `pageN.xml`。** 而**没有任何桥碰 `pages.xml`** | 我解包确认 `PageSheet` 只出现在 `pages.xml`；`grep -rn "pages.xml" src/Vsdx.Adapters/` 无命中；五个桥的 `entryName` 全部硬编码 `$"visio/pages/page{pageIndex+1}.xml"` |

**结论：M10 想要解决的"顺序敏感"问题，实际上不存在**——背景桥写的是**另一个 part**
（`pages.xml`），与所有既有桥零交集；真正的约束只是"必须在 `doc.Save()` 之后"，
而这自动成立。计划把不存在的竞争写成"关键"，会让实施者为错误的原因做排序设计。

**另有一处小的口径问题：** §5 写"不触碰 `Visio*Bridge`"，而 §18.3 第 6 步要**新增**
`VisioBackgroundBridge`。新增不等于触碰既有，但同一份文档里两句话读起来是矛盾的，
应改为"不触碰**既有** `Visio*Bridge`"。

> 结论：**PARTIALLY**。实现路径选择正确、z-order 验收正确；
> 但"顺序"分析三点全错（合并了两条路径、误解了读-改-写、认错了 part）。

---

### 2.12 M11 —— 三表不一致确认，下沉可行，但细节有错

**三张表确实不一致（我逐格复核）：**

| 角色 | `TypeScale`（`TextLayout.cs:19-24`） | `SizeForRole`（`VisioTextBlockBridge.cs:25-33`） | 计划 §2.3（plan:124-133） | 一致？ |
| --- | --- | --- | --- | --- |
| title | 20 | 20 | 20（DiagramTitle） | ✅ |
| subtitle | 12 | 12 | 12（RegionTitle） | ✅ |
| caption | — | **9** | **无此行** | ⚠️ 缺 |
| legend | — | 10 | 10（NodeCaption） | ✅ |
| note | **Annotation 9** | **8** | **9**（Annotation） | ❌ **9 vs 8** |

**你指出的 `note` 不一致（8 vs 9）成立。** 我另外找到：
**`caption` 在计划 §2.3 里根本没有对应行**，而 Bridge 给它 9、`NodeCaption` 是 10——
所以"caption 该取几"三份材料都没有答案。计划 §16.4 断言 1 恰恰要测 caption，
**却没写目标值**。

**依赖方向：把常量放 `Vsdx.Model` 可行。** 我核了项目引用：

```
Vsdx.Adapters  -> Vsdx.Model          （无 Vsdx.Engine）
Vsdx.Engine    -> Vsdx.Model, Vsdx.Adapters, Vsdx.Renderer, Vsdx.Validator
```

`Model` 是共同祖先，`Engine → Model ← Adapters` 成立，方案可行，不是空想。

**三处需要修正的细节：**

1. **类名写错。** §16.3 写"`Vsdx.Engine.TextScale` 引用它作为默认值"（plan:868），
   实际类名是 **`TypeScale`**（`TextLayout.cs:9`）。全文 grep `TextScale` 在 `src/` 下 **0 命中**。
2. **断言 2 无法执行。** "`grep -c \"20\" VisioTextBlockBridge.cs` 不再出现裸数字字号"——
   这个 grep 会命中 `0.20`、`120`、行号注释等，不能证明"无裸字号"。
   应改成"该文件不再出现 `=>` 后的数字字面量"或用 Roslyn 断言。
3. **未披露的行为变更。** 统一后 `note` 从 **8 → 9**。8 恰是 `MinReadablePt` 下限
   （`TypeScale.cs:11`、`VisioTextBlockBridge.cs:35`），改后会改变既有产物字号。
   plan §7「兼容性」**没有列这一项**（只列了 `Canvas.Unit` 与 `Direction`）。

> 结论：**PARTIALLY**。M11 的核心（承认三表冲突、指定唯一权威、给出可行的依赖方向）已落实；
> 但类名错误、caption 目标值缺失、note 字号变更未披露。

---

### 2.13 M12 —— 大部分已修，但留有 4 处硬伤

**已修好的（我逐条核）：**

| 上轮问题 | 现状 |
| --- | --- |
| §四"**不触碰** … LayoutStrategies" | 已删；改为"`LayoutStrategies.cs:142` 在布局期清空…"（packet:70），与计划一致 |
| D2/D3 仍作为**待决问题**提问 | 已改为"**已裁决，非待决**"（packet:63），并写"已落实" |
| 材料清单第 3 项是**不存在的文件** | `phase8.2-independent-review.md` 现已存在（838 行），该项成立 |
| T0 工时"小" | 已改"**中**"，理由"需覆盖 10 类空间数据 + 时序"（packet:107） |

**仍有 4 处：**

1. **"14 项"残留 2 处。** packet:27"审计 **14** 项中它独立复验了 11 项成立"、
   packet:136"因此 **14** 项不是\"全部缺陷\""。而同文件 packet:16 已写"**16 项缺陷**"，
   演进表（:57-59）也到 16。**同一份 136 行文件里 16 与 14 并存。**
2. **"18 节"过期。** packet:17 写"Stage 2 实施计划（**18 节**…）"，实际 `## ` 计数为 **19**
   （§0–§18）。
3. **破坏性变更清单与 plan §7 冲突。** packet §五 C2 列的是
   `Direction` + **`mode=fixed`**；plan §7 写的却是
   "**两处破坏性变更：`Canvas.Unit` 与 `Direction`**"（plan:365）。
   **两份文档对"哪两处是破坏性变更"给出不同答案。**
4. **§六 工时表未纳入新增缺陷。** 表里最晚只到"M11 字号归一"，
   缺陷 15/T8a、缺陷 16/T1b 与 §15 闸门均未计入，却已下结论"总计：从中等偏小上调为中等"。

> 结论：**PARTIALLY**。M12 点名的四项全部处理了，但"材料包与审计/计划无矛盾"这个判据未达成。

---

## 3. 修订引入的新错误（本轮重点）

M1–M12 之外，修订本身制造了以下新问题。前两条会直接导致错误实现。

### 3.1 【新，P1】`U="IN"` 事实被改成更强的错误，并误署章节

见 §2.10。`U="IN"` 确实写在 `pages.xml` 的 `PageSheet` 里。
**这个错误的源头是我上轮报告的自相矛盾**（`independent-review.md:68` 引了 `U="IN"` 却说"根本没写进文件"），
**修订把它从"部分正确"放大成"既无 CM 也无 IN"的明确错误**，并署给 A9/B8（实际应为 A.1#4/B7）。

这条错误还外溢到 audit §2.4 标题、audit §10 缺陷 6、plan §3、plan §4 第 6 行、plan §7 共五处，
其中 plan §7"标签写 cm"与 §2.4"标签根本没写"**互相否定**，
且五处**没有一处**是准确表述（准确表述为"标签恒写 `IN`、数值未换算"）。

### 3.2 【新，P1】§11 内部互斥，T0 无法按计划实现

见 §2.9。§11.2（manual 保留并转置）与 §11.4（manual 不生效、进 unmet）对立；
§11.6 断言 4 与断言 7 互斥。实施者无论选哪边都会违反另一半。

### 3.3 【新，P2】§15 的闸门 3 与本轮新缺陷 16 冲突

见 §2.8。`containers[].order` 同时被列为"本阶段不执行、登记上报"与"本阶段实现"。

### 3.4 【新，P2】§5 与 T0b/T1b 冲突：`DiagramIr.cs` 说"无需改动"

实际 T0b 改 `DiagramIr.cs:282`、T1b 改同文件的 `DiagramIrConverter`（`DiagramIr.cs:232`）。

### 3.5 【新，P2】§16.3 类名 `TextScale` 不存在

实际为 `TypeScale`（`TextLayout.cs:9`）。

### 3.6 【新，P2】§17.2 "唯一换算点"漏掉 `create` 路径

见 §2.10。`create` 一次换算都不做。

### 3.7 【新，P2】§18.3 重写顺序的三条事实错误

见 §2.11。

---

## 4. 跨文档矛盾清单

| # | 矛盾 | 位置 A | 位置 B |
| --- | --- | --- | --- |
| 1 | 单位标签是否写入文件 | audit §2.4："既无 `U="CM"` 也无 `U="IN"`" | plan §4 第 6 行："标签未写入文件" ✓ / plan §7："标签写 cm" ✗ / **实测：`U="IN"` 存在** |
| 2 | 哪两处是破坏性变更 | plan §7："`Canvas.Unit` 与 `Direction`" | packet §五 C2："`Direction` 与 `mode=fixed`" |
| 3 | 缺陷总数 | audit/plan/packet 表头：16 | packet:27 与 packet:136："14 项" |
| 4 | 计划节数 | 实际 19（§0–§18） | packet:17："18 节" |
| 5 | `DiagramIr.cs` 是否要改 | plan §5：":240 无需改动" | plan §4 缺陷 14→T0b、缺陷 16→T1b（都改该文件） |
| 6 | `IrContainer.Order` 是否本阶段实现 | plan §15.1/闸门 3：登记为"未消费" | plan §4 缺陷 16 / T1b：本阶段消费 |
| 7 | manual 下是否转置 | plan §11.2：转置 | plan §11.4 / §11.6#7：不转置 |
| 8 | `Visio*Bridge` 是否触碰 | plan §5："不触碰" | plan §18.3：新增 `VisioBackgroundBridge`；M4 TextBlock 需改 `VisioTextBlockBridge` |
| 9 | 缺陷 6 的定性 | audit §2.4 正文：非"标签与数值不一致" | audit §2.4 **标题**、§10 缺陷 6："只写标签不换算" |
| 10 | "16⇄16⇄16 一一对应" | plan:212 声称 | 实测 §4 有 T0.5、§9 无；§9 有 T8/T9、§4 无 |

---

## 5. 缺陷计数一致性（专项）

你要求"若发现仍写 15 或 14 就指出"。结果：

| 位置 | 数字 | 判定 |
| --- | --- | --- |
| audit §10 表体 | 16 行 | ✅ |
| audit 结句（:718） | "16 项缺陷中，初稿漏了 10 项、误判 3 项" | ✅ |
| plan §4 标题（:185） | "十六项" | ✅ |
| plan §4 表体 | 16 行 | ✅ |
| plan:212 | "16 项缺陷 ⇄ 16 行修法 ⇄ 16 个任务" | ❌ **映射为假**（见 §2.6） |
| **plan:20**（修订索引 M5 行） | "§4 表补齐 12 行→**现 14 行**" | ❌ **仍写 14**（§4 实为 16 行） |
| plan §15.1（:742,745） | "（12→14 项）"、"修完 **14** 项后仍存在" | ⚠️ 陈旧的中间态表述 |
| packet:16 | "**16 项缺陷**" | ✅ |
| **packet:27** | "审计 **14** 项中它独立复验了 11 项" | ❌ **仍写 14** |
| **packet:136** | "因此 **14** 项不是\"全部缺陷\"" | ❌ **仍写 14** |
| packet 演进表（:57-59） | 10→12→14→15→16 | ✅ |

**结论：计数在表头与清单层已一致（16），但仍有 5 处陈旧数字：
review-packet 正文 2 处"14"（:27、:136），plan 修订索引 1 处"现 14 行"（:20），
plan §15 2 处"14"（:742、:745）。且"16⇄16⇄16"这句自证是错的。**

---

## 6. 五维度（仅就本轮修订变化作增量判断）

| 维度 | 上轮 | 本轮 | 变化依据 |
| --- | --- | --- | --- |
| 1 架构责任划分 | ⚠️ | ⚠️ 仍 | 字号表权威已定（M11 方向对），但 `Vsdx.Model` 下沉后 `TypeScale` 的"派生"与"常量"关系未说清；§5 文件表仍与任务表冲突 |
| 2 图形表达覆盖 | ⚠️ 偏窄 | ⚠️ **略改善** | swimlane 可达性有修法（T0b，实测可行）；容器顺序（缺陷 16）被识别。但 palette 仍是封闭 4 套，表达维度仍只有"颜色 + 字号" |
| 3 协议工程"接受不执行不报告" | ❌ | ⚠️ **改善但仍不封闭** | 闸门 1/2 是机制级；闸门 3 是枚举且与 T1b 冲突；库内调用方绕过 CLI 闸门 |
| 4 VSDX 保真 | ⚠️ | ⚠️ 仍 | 路径 A 正确，但顺序分析建立在错误事实上；`NativeCells`/`RawXml` 不在转置范围 |
| 5 测试工程 | ❌ | ✅ **显著改善** | M6 是真修：三份真 IR、标签完整、结果可复现。新 IR 一渲染就暴露出 5 个旧测试看不见的问题（容器同色、无角色配色、标题重叠、分隔线穿字、流程倒置）——**这恰好证明"373 项全绿"对视觉能力零保护**，也证明 M6 的修法打对了靶 |

**关于维度 5 的一句话判断：** 本轮最有价值的改动不是任何一条 M 的文字，而是**真的造了
IR 并真的渲染了**。缺陷 15、16 都是渲染后才发现的，读代码一万遍也读不出来。
这条方法论应当写进计划，而不只是体现在结论里。

---

## 7. 裁决

> ### 裁决：**FAIL**

### 7.1 为什么不是 PASS WITH CONDITIONS

上轮我写过"若 M1–M6 补齐，可转 PASS WITH CONDITIONS"。本轮 M1、M3、M6 **确实补齐了**
（且 M6 是全轮质量最高的一项），**但 M2、M4、M5 未补齐，且 M8、M9 在修订中引入了新的
事实错误与自相矛盾**。按"补齐 M1–M6"这个自己立的条件，条件未达成。

判 FAIL 的实质理由只有一条：**仍有会直接导致错误实现或错误验收的缺陷。**
不是"文档不够完美"。

### 7.2 仍为阻断的 M 项（5 项）

| # | 阻断理由 | 必须做的修正 | 成本 |
| --- | --- | --- | --- |
| **M2** | 明确要求的"删除审计 §8「功能正确」判语"**未执行**（audit:639 仍在）；且 T4b 验收只覆盖收缩方向，方案 2 不覆盖撑大方向（实测 8×6 → 11.35×9.85） | 删该句；T4b 验收补一条"请求 8×6 → 8×6，或 unmet 有 entry" | 小 |
| **M4** | TextBlock 字体不是"Reader 缺失"，而是**写侧不合规（形状级裸 `Font` 单元格、未登记 FaceName）+ 需改 `VisioTextBlockBridge`**，与 §5"不触碰 `Visio*Bridge`"冲突。按现计划实施，`textBlock.font=Verdana → 回读 Verdana` **不可能达成** | 二选一：①把 `VisioTextBlockBridge` 加入必改清单（改 Character section + 登记 FaceName）；②明确声明 TextBlock 字体不回读，改用 XML 断言作验收 | 需决策 |
| **M5** | "16⇄16⇄16 一一对应"为**假**：§4 引用 `T0.5` 但 §9 无此任务；§9 有 `T8`/`T9` 但无缺陷指向。且 §5 仍重复列出 Writer、并把 `DiagramIr.cs` 标为"无需改动"（T0b/T1b 都要改它） | §9 补 `T0.5`；`T8`/`T9` 标注为"横切任务、不映射单一缺陷"；删 §5 重复行；改 `DiagramIr.cs` 为"需改（`:282`、转换器）"；补 `LayoutStrategies.cs`/`VisioTextBlockBridge.cs`/`Program.cs` | 小 |
| **M8** | §11.2 与 §11.4 互斥、§11.6 断言 4 与 7 互斥；且 manual+direction 验收**不可达**（IR 无 waypoints、manual 分支无转置阶段）——这与上轮判 M8 的理由同类 | 二者择一并在全文统一：**(A) manual 不转置**（删 §11.2 的 manual 行与断言 4，保留 §11.4 的 unmet 要求）；或 **(B) manual 转置**（给 manual 分支加显式转置调用，并让 `IrEdge` 支持 waypoints）。同时补 `NativeCells`/`RawXml` | 需决策 |
| **M9** | 支撑事实被改成更强的错误（"既无 CM 也无 IN"，实际 `U="IN"` 存在）并误署 A9/B8（应为 A.1#4/B7）；五处表述相互否定（plan §7"标签写 cm" vs audit §2.4"标签根本没写"）；§17.2"唯一换算点"漏 `create`；§17.3 断言 3 恒真 | 把 audit §2.4 正文/标题、audit §10 缺陷 6、plan §3、plan §4 第 6 行、plan §7 五处统一为"Writer 恒写 `U="IN"`、数值未换算，故 10 cm 被写成 10 IN"；§17.2 改为"`update` 已换算，`execute`/`create` 未换算"；断言 3 换成"`page.Size.Unit` 内部值为 `inches` 且数值已除 2.54" | 小 |

### 7.3 条件项（不阻断开工，但应在实施前落定）

| # | 项 | 要求 |
| --- | --- | --- |
| M7 | 闸门 1 的写法自相矛盾（`Disallow` 会抛异常，拿不到"值 + 清单"）；闸门 3 是枚举且与 T1b 冲突；库内调用方绕过 CLI 闸门 | 改自定义 converter；删 `containers[].order` 出 `NotYetExecuted`；说明闸门只在 CLI 边界生效 |
| M10 | §18.3 顺序分析三点全错（合并两条路径、误解读-改-写、认错 part） | 改为"`PageSheet` 在 `pages.xml`，与既有桥零交集；唯一约束是必须在 `doc.Save()` 之后" |
| M11 | `TextScale` 应为 `TypeScale`；`caption` 目标值未给；`note` 8→9 变更未披露 | 三处修正 |
| M12 | packet 残留 2 处"14"、"18 节"、破坏性变更清单与 plan §7 冲突、工时表未含新增缺陷 | 四处修正 |
| M1 | 补充说明：`applied` 顺序会变；`DiagramSizer` 会用 canvas 设置前的页面尺寸推 TypeScale，需限定断言 1/5 的前提 | 两条说明 |

### 7.4 值得明确认可的部分

不因为判 FAIL 就抹掉这些——它们是真的：

1. **M1 的归因重写是诚实且正确的。** 从"顺序错"改为"阶段缺失"，并且真的去
   `grep -rn "DiagramSizer.Apply"` 核了四个调用点。**我实测复现：同一 IR、同一 11 pt，
   带 spec 节点宽 `[2.00,2.00,2.00]`，不带 spec `[1.30,2.15,2.525]`；`baseSize` 9→18 框体纹丝不动。**
   归因成立。
2. **M6 是全轮最实质的修复。** 造了三份真 IR、真跑通、真回读标签。
   由此暴露的缺陷 15、16 **靠读代码永远发现不了**。
3. **M11 的依赖方向判断正确。** `Adapters` 确实只引用 `Model`，下沉方案是唯一可行方向，
   不是拍脑袋。
4. **M9 的契约算术正确。** `UnitFactor("inches") == 1`，归一化后确实不双重换算。
5. **缺陷 13/14/15/16 的实测数据我逐项复现成功**（4.00×3.45 同量级、swimlane 与 layered
   逐点相同、title/subtitle 从 0.6" 塌到 0.1"、flow 容器 `5.55/3.43/1.30`）。
   **新缺陷不是编的。**
6. **基线 373 测试 / 0 warning 独立复现。**

### 7.5 对新增缺陷 15、16 的独立判断

这两条**不在 M1–M12 范围内**（它们是修订之后才发现的），但你要求我判断，
且它们影响"本阶段该不该现在开工"。

**事实层面：两条都成立，我复现了。**

| 缺陷 | 我的复现 | 判定 |
| --- | --- | --- |
| 15 装饰夹紧 | `arch.ir.json` 的 title y=11.4 / subtitle y=10.8（0.6"）→ 输出 8.55 / 8.65（**0.1"**），与审计给的数字一致 | ✅ 成立 |
| 16 容器顺序 | `flow.ir.json` → `fulfil 5.55 / review 3.43 / intake 1.30`；翻转 `order`、删除 `order` 输出**完全不变**；翻转**声明顺序**输出随之改变 | ✅ 成立 |

**归因层面：缺陷 16 的根因链有一处与实测路径不符（见 §8.2）。**
审计说根因在 `LayeredLayoutEngine:117-121` 的 `MeanY` 排序；但 `flow.ir.json` 带
`role=decision` 节点且 `kind=workflow`，`SuggestStrategy` 选的是 **Flow**，
实际走 `FlowAndTreeLayoutEngine.LayoutBanded`（`:93` 按 `Bounds.Y` 升序）。
**结论（order 未被消费）成立，但引用的代码路径不是实际执行的那条。**

**该不该因此阻断开工？我的判断：不阻断，但 T1b 必须先做。**

- 不阻断的理由：这是 `IrContainer.Order` 这个已知"接受不执行"字段的**后果显化**，
  不是新的架构问题；T1b 已把它排进任务。
- 但**必须排在最前**：容器顺序错会让后续所有 PNG 目视验收都基于一张语义颠倒的图，
  修复效果无法判断。**T1b 应作为 T8（三套产物）的前置条件**，当前 §9 未标注这个依赖。

**关于"修前基线"新增第五条（流程倒置）：** 我认可这个做法——把"修之前长什么样"记下来，
修完才有对照。但 §6.4 的基线表把"连线大量交叉"标注为"布局问题，**不在本阶段范围**"（plan §6.4），
而缺陷 16 又说是 P0 必须修。**"倒置是 P0、交叉不是"这个区分需要给理由**，否则读起来像随手划界。

### 7.6 达到什么条件可转 PASS WITH CONDITIONS

只需完成 §7.2 的 5 项，其中：
- **3 项是文档编辑**（M2、M5、M9），**半天内可完成**；
- **2 项需要一次决策**（M4 的 TextBlock 字体范围、M8 的 manual 是否转置）。

这两项决策一旦落定，M1–M12 即全部可闭环，本计划可转 **PASS WITH CONDITIONS**，
条件项为 §7.3 的五条。

---

## 8. 本次复审的局限

0. **我上轮报告自身有错，本轮回溯更正。** `phase8.2-independent-review.md:68`（A.1 第 4 行）
   同一条目内既引出 `<Cell N="PageWidth" V="10" U="IN"/>`、又断言"单位标签根本没写进文件"——
   **自相矛盾**。修订方是沿此错误走的，最终放大成"既无 CM 也无 IN"。
   我在 §2.10 与 §3.1 已如实标注这个源头。**审查者的错误同样应被记录，不能只记被审方的。**
1. **无 Microsoft Visio，L4 仍未验证。** 本文所有"溢出/重叠/倒置"判断基于
   `check-layout` 文本度量与 `export-model` 回读，**不是 Visio 渲染结果**。
   凡"在 Visio 里看起来如何"，本文一律未断言。
2. **缺陷 16 的归因我做了一半。** 我确认了"`IrContainer.Order` 无消费者"（`grep` 全库仅声明）、
   "翻转/删除 order 输出完全不变"、"推翻声明顺序会改变输出"（说明实际由声明序驱动）。
   但**未穷尽** `LayeredLayoutEngine` 与 `FlowLayoutBanded` 两条排序路径在所有输入下的差异——
   `flow.ir.json` 带 `role=decision` 节点（`risk`/`stock`），`SuggestStrategy` 按
   `kind=workflow` + 有决策节点选中 **Flow**，而非 Layered；
   而审计 §2.12 的根因链写的是 `LayeredLayoutEngine:117-121` 的 `MeanY` 排序。
   **两条路径排序逻辑不同**（Layered 按 `MeanY` 降序、Flow 按 `Bounds.Y` 升序），
   审计只描述了前者，实测走的是后者。**归因链的这一步与实测路径不符**，需实施者再核。
3. **未审计 `LayoutAnalyzer` / `RepairController` / 渲染器**，与前轮相同。
4. **文档已落库但仍可能再变**，若再变动，本文引用即失效。
5. **`.review-scratch-r2/` 已被 `a0672f8` 加入 `.gitignore:61`**（我实测 `git check-ignore` 命中）。
   本处仅作记录：该目录内的 IR/VSDX/JSON 均为临时证据，**不应提交**。

---

## 附：本文引用的关键位置（便于复核）

```text
src/Vsdx.Protocol/DiagramIr.cs:199              Order 声明（无消费者）
src/Vsdx.Protocol/DiagramIr.cs:232              DiagramIrConverter 类
src/Vsdx.Protocol/DiagramIr.cs:282              lane → container（M3）
src/Vsdx.Protocol/DiagramIr.cs:297              Bounds(0,0,2.0,0.9) 写死
src/Vsdx.Protocol/DiagramIr.cs:328-329          FromPort/ToPort 映射（IR 有）
src/Vsdx.Protocol/DesignSpec.cs:127-164         layout 分支（manual 只写 applied）
src/Vsdx.Protocol/DesignSpec.cs:169-195         Typography（纯样式，可提取）
src/Vsdx.Protocol/DesignSpec.cs:199-226         容器硬编码色（纯样式）
src/Vsdx.Protocol/DesignSpec.cs:249-287         Canvas + FitPage
src/Vsdx.Protocol/ProtocolRunner.cs:66-88       spec 分支（无 DiagramSizer）
src/Vsdx.Protocol/ProtocolRunner.cs:77          唯一 spec==null 的 DiagramSizer.Apply
src/Vsdx.Engine/DiagramSizer.cs:39-116          Apply：按文字推框体
src/Vsdx.Engine/DiagramSizer.cs:45-48           TypeScale 取自 page.Size（M1 细节 2）
src/Vsdx.Engine/DiagramSizer.cs:153-180         mustGrow / shrink 两分支（M2 撑大方向）
src/Vsdx.Engine/LayoutStrategies.cs:109         Kind=="lane" 判据
src/Vsdx.Engine/LayoutStrategies.cs:142         布局期无条件清空 waypoints
src/Vsdx.Engine/LayoutStrategies.cs:166-183     ClampDecorationsToPage（缺陷 15）
src/Vsdx.Engine/LayeredLayoutEngine.cs:110-121  容器按 MeanY 降序
src/Vsdx.Engine/FlowAndTreeLayoutEngine.cs:91-93 容器按 Bounds.Y 升序（flow 实际路径）
src/Vsdx.Engine/SwimlaneLayoutEngine.cs:20-27   lane 识别与回退
src/Vsdx.Engine/TextLayout.cs:9,19-24           TypeScale（类名，非 TextScale）
src/Vsdx.Adapters/OfficeImoDocumentWriter.cs:50 VisioMeasurementUnit.Inches 硬传
src/Vsdx.Adapters/OfficeImoDocumentWriter.cs:157-166  create 的四个桥
src/Vsdx.Adapters/OfficeImoDocumentWriter.cs:377-378  Edge 标签字体（写侧合规）
src/Vsdx.Adapters/OfficeImoDocumentReader.cs:542  Group.Kind 无条件 "container"
src/Vsdx.Adapters/OfficeImoDocumentReader.cs:604-620  ReadStyle 读 TextStyle
src/Vsdx.Adapters/OfficeImoDocumentReader.cs:627-676  ReadEdge 无 LabelFont
src/Vsdx.Adapters/VisioTextBlockBridge.cs:25-33  SizeForRole（第二张表）
src/Vsdx.Adapters/VisioTextBlockBridge.cs:141    形状级裸 Font 单元格（M4）
src/Vsdx.Adapters/OfficeImoIncrementalEditor.cs:62,297-307  update 换算 + 五个桥
src/Vsdx.Adapters/MasterCatalog.cs:70-77        UnitFactor：_ => 1.0
src/Vsdx.Cli/Program.cs:715-721                 IR/Spec 反序列化（闸门位置）
src/Vsdx.Model/DiagramModel.cs:352-359          UnmappedObject.RawXml（转置遗漏）
schemas/phase7/diagram-ir.schema.json:232-235   order: integer, minimum 0（无语义描述）
examples/phase8/{arch,flow,overview}.ir.json    三份真 IR（M6）
```
