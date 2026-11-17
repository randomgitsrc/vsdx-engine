# Phase 8.2 第四轮独立审查

**审查对象（本轮实际读取，md5 固定）：**

| 文件 | md5 | 行数 |
| --- | --- | --- |
| `docs/phase8/phase8.2-plan.md` | `e4bd0bc289ef8fda56d4c340c0644401` | 1438 |
| `docs/phase8/phase8.2-audit.md` | `28083db6ac8d90409ca04eb517aff4f7` | 1176 |
| `docs/phase8/phase8.2-review-packet.md` | `c1f3f8dd78f64af93fc987d61fc498e8` | 153 |
| `docs/phase8/phase8.2-owner-rulings.md` | `d8831f6a1ef3fec21326447dc8bb6977` | 198 |
| `docs/phase8/phase8.2-open-decisions.md` | `fadaf35e1316427c8b863a4679aa1476` | 105 |

**基线：** `HEAD=855a0cd`。上表五份文档与 `855a0cd` 的提交内容**逐字节一致**
（`git show HEAD:docs/phase8/<f> | md5sum` 与工作树 md5 相同），`src/` `tests/` `schemas/` 无改动
（`git diff --stat -- src/ tests/ schemas/` 为空）。

**审查日期：** 2026-11-18
**审查者立场：** 与前四份审查（r1/r2/r3 及 Stage 4 提交）**无关联**的第三方。
未采信任何"已修完"的说明，全部结论来自本轮源码阅读与实机执行。

### 关于"文档在审查期间再次变动"的披露（必须写）

与 r3 披露的情况相同，本轮再次发生**修订与审查并行**。我实测到 plan 在审查过程中出现过三个不同的 md5（即两次改动）：

```console
$ md5sum docs/phase8/phase8.2-plan.md ; wc -l docs/phase8/phase8.2-plan.md
01:05  6686c57222c0fb50c1117474644f43fd   1399 行   ← 等于 f958dbb（我已读的开头部分）
01:06  9400d7ada78564b7799fa90cfb2feef2   1420 行   ← 工作树出现 M，编辑进行中
01:07  e4bd0bc289ef8fda56d4c340c0644401   1438 行   ← 编辑完成，我立即复制固化
01:07  855a0cd 提交落库（内容 = e4bd0bc2）
01:55  e4bd0bc289ef8fda56d4c340c0644401   1438 行   ← 此后未再变动
```

因此我在 `01:07` 把五份文档复制到 `.review-scratch-r4/pinned/` 固化，
**本文全部结论针对 `e4bd0bc2` 这一版**（等于 `855a0cd`）。
若文档继续变动，`plan:xxx` 行号会漂移，结论须按 md5 重新对齐。

两次增量（1399 → 1420 → 1438，共 +39 行）都落在 T2d 一节：
新增 `fc-match` 实测与"FaceName → 系统字体文件"的两种方案比较（现 `plan:708-745`）。
**它只增加了可行性证据，没有改动验收标准**，因此不影响本文判定；
但它也说明"文档在审查中被继续编辑"仍是本项目的常态。

---

## 审查范围与独立性

### 本轮范围：只判"r3 的 4 个阻断项 + C3/C4/C5 是否关闭"

我**没有**重审 25 项缺陷、没有重跑五维度评审、也不以新发现作为裁决依据。
新发现的问题（§F 与正文的"附注"）只登记，不参与裁决。

### 独立性

- 我未参与 Stage 1/2，也未撰写此前任何一份审查或提交说明。
- 本会话**未修改任何产品代码，未修改任何既有文档**。
- 唯一写入仓库的是本文件；实测产物在 `.review-scratch-r4/`（自带 `.gitignore`，`git status` 不显示）。
- 我**先固化文档再动手**，避免"边改边审"导致结论无法对应版本。

### 已执行的操作（可复现）

```console
$ source .envrc.sh && dotnet build src/Vsdx.Cli/Vsdx.Cli.csproj -c Release
  已成功生成。0 个警告 0 个错误
$ dotnet test tests/Vsdx.Tests/Vsdx.Tests.csproj -c Release
  已通过! - 失败: 0，通过: 373，已跳过: 0，总计: 373
$ git status --short
（审查开始与结束均为空）
```

CLI 调用覆盖 `execute` / `export-model` / `render` / `create` / `validate`，
并用 `unzip -p <pkg> visio/pages/page1.xml`、`visio/document.xml` 直接解包取证，
PNG 用 md5 逐字节比对（必要时用 Pillow 统计像素直方图，确认不是空白图）。
另用 Python `jsonschema` 直接对 `schemas/phase7/*.schema.json` 做独立校验，
用于判断"闸门 0 加载 Schema 后能挡住哪些输入"——这是**判断计划可行性**，不是判断实现。

---

## A. 四项阻断项逐项核验

| # | 阻断项（r3 口径） | 是否关闭 | 本轮一手证据 |
| --- | --- | --- | --- |
| 1 | **E-6** Renderer 不消费 `FontFamily` | ✅ **关闭（已登记为缺陷并排期）** | 审计 `audit:661` §2.15【P0】+ `audit:1166` §10 行 18；计划 `plan:335` 缺陷 18 → **T2d**，验收在 `plan:684`。我独立复现：5 种字体（含不存在的）产出 PNG md5 全同（§C.1） |
| 2 | **E-6b** TextBlock 的 `Color`/`Font` 写在 shape 级裸 `<Cell>` | ✅ **关闭（已登记为缺陷并排期）**，⚠️ **但计划的修法会造成回归**（§C.3） | 审计 `audit:690` §2.16【P0】+ `audit:1167` §10 行 19；计划 `plan:336` 缺陷 19 → **T2e**，验收在 `plan:746`。我独立复现：三色 PNG 全同、回读 `textColor=null`，XML 层级与 audit 描述逐字一致（§C.2）。**但** `plan:748` 的"两行移动"单独实施会让 `fontSize`/`bold`/`italic` 一起变 null（§C.3 的 v-a），必须同时注册 `FaceNames`（v-f）|
| 3 | **E-9** `plan §11.2` 表格"保留并转置"与 D1 裁决对立 | ✅ **关闭** | 矛盾行已删；现表 `plan:852-855` 只有两种模式且 `manual` 行写"**不生效**（进 `unmet`）/ 不转置"；`plan:859-862` 与 `plan:867`、`plan:914`（§11.6 断言 7）三处一致。`grep 保留并转置` 在现行 plan 仅剩 `plan:847` 一处"修正说明"引用 |
| 4 | **E-4 + D.3** 协议闸门覆盖不足 / `execute` 不加载 Schema | ⚠️ **部分关闭：方向正确，但闸门 3 的清单不完整构成机制缺口**（不阻断，见 §E 条件 4） | 新增闸门 0（`plan:1170-1181`，"先加载 JSON Schema … 校验失败 → status=failed"）；r3 点名的 4 类漏网项已进 `plan:1183-1190` 表并给了 `input.*` 码；缺陷 21（`audit:626` §2.17 + `audit:1169`）记录了当前行为。**但**：闸门 0 只点名 `design-spec.schema.json`，未覆盖 `diagram-ir.schema.json`；闸门 1（JsonDocument 预扫，`plan:1205-1228`）与闸门 0 并存且未说明取舍；§9 的 26 个任务里只有 **T2f 对应闸门 2**（枚举 `TryParse`），**闸门 0（Schema）、闸门 1（未知成员）、闸门 3（未消费登记）没有任何 T 编号**；闸门 3 的 `NotYetExecuted` 代码行只有 `metadata.*`，与正文不符（§E 条件 4） |

### A.1 逐项说明

**E-6。** r3 要求的是"记录 + 排期 + 可证伪验收"，不是修好（Stage 5 未开工）。三项齐备：

- 审计有独立小节与实测证据（`audit:666-668` 给出三张 PNG 的 md5）；
- 缺陷清单编号 18、严重度 P0（`audit:1166`）；
- 任务 T2d 的验收是"换字体 → PNG **必须不同**（当前 md5 相同）"（`plan:684`），
  这是一条**当前必定失败、修好后可判定**的断言。

我另外确认了这条验收的**方向正确**：用 `--font-file` 换整页字体文件确实会改变 PNG
（同一 VSDX，DejaVuSans vs DejaVuSerif → `99a3fd58` vs `b3cb75e2`），
说明渲染器底层能画不同字形，缺的只是"从 VSDX 读族名"这一跳。
所以 T2d 不是无法完成的任务。

**E-6b。** 登记层面齐备（缺陷 19 / T2e / `plan:746` 的"回读非 null + 三色三字体 PNG 互不相同"），
**但计划的修法本身是错的，且错得比"不够"更严重**：`plan:748` 写"修法只有两行移动"，
而按 §C.3 的单变量实测，单独做这两行会让 `fontSize`/`bold`/`italic` **从可读回退化为全 `null`**——
比不改还差（现状至少字号/粗体/斜体是好的）。必须同时注册 `FaceNames` 才成立。

**这不改变"阻断项已关闭"的判定**：缺陷已登记、任务已排期、验收也可证伪。
但它改变了 T2e 的实施风险等级（从"两行·小"变成"两步且必须同提交·中"），
并暴露 `audit:729-730` 那句"注册与否是次要的"是错的。列为 §E 条件 3（必须）。

**E-9。** 关闭，且我确认现在**只有一种解释**。`manual` 相关的 6 处表述全部指向"不转置 + 进 unmet"：
`plan:815`（§11.1 waypoints 行）、`plan:852-855`（模式表）、`plan:859-862`（已裁定行为表）、
`plan:867`（`DesignSpec.cs:163` 分支）、`plan:885-890`（§11.4）、`plan:914`（§11.6 断言 7）。
当前代码行为与之一致（我实测 `manual` + `topToBottom` 坐标逐点不变、`unmet` 为空——
这正是 T0 要修的"静默"部分）。

*残留（不阻断）*：`plan:843` 留了一个**孤立表头** `| 场景 | waypoints 行为 | 理由 |`，
下面既无分隔行也无数据行，紧接裁决文字。这是删表时删漏的表头，渲染出来是一行裸文本。
它不构成矛盾，但说明这次删除是手工截断而非整段替换。

**E-4 / D.3。** 这一项 r3 给了 4 条最小修法，我逐条对照：

| r3 要求 | 现状 | 判定 |
| --- | --- | --- |
| 闸门 2 白名单**从 Schema 生成或与之同源** | 新增闸门 0 直接加载 Schema（`plan:1170-1181`）—— 这是"同源" | ⚠️ 只做了一半：白名单仍是手抄列表（`plan:1158-1161`），且与闸门 0 并存 |
| 闸门 3 的 `NotYetExecuted` 列出**全部**本期不执行字段 | 代码行仍只有 `{ "metadata.*" }`（`plan:1202`），而正文说还要含 `IrContainer.Kind` 的部分取值（`plan:1197-1198`） | ❌ 正文与代码不一致；`anchor.mode` / `FromPort` / `ConstraintSpec.Target` / `IrEdge.Relation` 都不在清单里 |
| `text.overflow-*` 进硬约束或 `unmet` | 缺陷 20 → T6b（`plan:337`、`plan:690`），验收"`baseSize=24` 时 hard=false、score=0" | ✅ 已落定 |
| 明确回答"为什么不用现有 Schema 做闸门 1" | `plan:1172` 说"闸门 1 在重造 Schema 已有的约束"，`plan:1181` 说"这比手写闸门更可靠" | ⚠️ 说了理由，但没据此**删掉**闸门 1，两套机制并存 |

另有一处覆盖缺口（r3 已点过，仍在）：`IrEdge.Relation` 在 plan 全文**零命中**
（`grep -in relation docs/phase8/phase8.2-plan.md` 无输出），8 个取值仍只驱动箭头有无。
它既不在闸门 2 白名单，也不在闸门 3 清单。

我独立验证了"闸门 0 若真的加载两份 Schema，能挡住什么"（用 Python `jsonschema` 直接校验，
不经产品代码）：

```console
$ {"typografy":{"baseSize":14},"specVersion":"99.0"}  → 2 errors
    '99.0' does not match '^1\.[0-9]+$'
    Additional properties are not allowed ('typografy' was unexpected)
$ {"layout":"kaboom"}                                 → 1 error（不在 enum）
$ {"containerLayouts":{"svc":"kaboom"}}               → 1 error（不在 enum）
$ {"theme":{"palette":"does-not-exist"}}              → 0 errors  ← Schema 本身没有 enum
```

即：**加载 Schema 能一次挡住 r3 的三条验收里的前两条与 containerLayouts**，
第三条（未知 palette）要等 T1 给 `theme.palette` 加 `enum` 才挡得住——这一条计划的
§5 已排（`plan:397` "枚举 4 个 palette 名"）。这是我对"闸门 0 方向正确"的正面确认：
它比手写白名单覆盖面大得多。

**同时它证明了"闸门 0 替代不了闸门 3"。** 我用同一份 `jsonschema` 校验 `diagram-ir.schema.json`：

```console
$ IR 节点带顶层 "fill":"#00FF00"   → 1 error（additionalProperties:false 挡住）
$ IR 节点 "role":"databse"（拼错）  → 1 error（不在 enum）
$ IR 锚点 {"x":1,"y":2,"mode":"relative"} → 0 errors   ← 合法取值，但执行忽略
```

`anchor.mode:"relative"` **通过 Schema 但是不被执行**——这正是"协议接受、Schema 允许、
执行忽略"的第四种情况，**只能靠闸门 3 的未消费登记来报告**。
所以闸门 0 与闸门 3 是**互补而非替代**，计划必须写清分工（§E 条件 4 第 2 点）。
这一条让"闸门 3 的清单不完整"从一个措辞问题升级为**机制缺口**：
清单漏项＝该字段永远不报告。

---

## B. C3/C4/C5 核验

| 项 | 要求 | 现状 | 是否关闭 |
| --- | --- | --- | --- |
| **C3** | 破坏性变更在 plan 与 packet 中**一致为三处**（Direction / `mode=fixed` / `Canvas.Unit`） | plan 内部**三处 vs 两处并存**：`plan:178` 与 `plan:193-199` 写"三处"并列出三项；`plan:649` 仍写"**两处**破坏性变更"，且 §7 的兼容性表（`plan:641-647`）**没有 `mode=fixed` 行**（`sed -n '639,656p'`\|`grep -c fixed` = 0）。packet 自相矛盾：`packet:103` 称"统一为三处"，`packet:108` 标题仍写"两处"，`packet:110` 却说"**以 plan §7 为准，三处**"——而被它当作准绳的 §7 写的是两处 | ❌ **未关闭** |
| **C4** | `plan §5` 文件表补全 r3 点名的 7 项 | r3 点名的 7 项**全部在表内**：`ProtocolRunner`(`plan:388`)、`LayoutStrategies`(`389`)、`TypeScaleDefaults`(`391`)、`VisioBackgroundBridge`(`392`)、`DiagramIrConverter`(`393`)、`FontResolver`(`394`)、闸门实现 `JsonGate.cs`(`395`) | ⚠️ **实质补全，但表本身新增 4 处错误**（下表） |
| **C5** | "同字号 → 同框体"断言加"同一画布尺寸"前置 | §12.4 断言 1 **已加**且给了复算依据（`plan:993`）；但 **T0.5 验收未加**（`plan:676` 仍是"同字号下带 spec 与不带 spec 框体一致"）；§12.4 **断言 5 也未加**（`plan:997` "修后带 spec 与不带 spec 的节点宽差 < 0.05""） | ❌ **未关闭（2 个落点漏 1 个半）** |

### B.1 C4 剩余的 4 处错误（实测）

| # | 问题 | 证据 |
| --- | --- | --- |
| 1 | 列了一个**不存在的文件** `src/Vsdx.Protocol/DiagramIrConverter.cs` | 该类实际定义在 `src/Vsdx.Protocol/DiagramIr.cs:232`（`grep -rn "class DiagramIrConverter" src/` 只有这一处）。而 `plan:387` 已经用 `DiagramIr.cs` 覆盖了 T1b，`plan:393` 是重复且路径错误的一行 |
| 2 | `src/Vsdx.Engine/TypeScaleDefaults.cs` "**新增**（放 `Vsdx.Model`）" | 同一格里路径与放置位置互斥。`ls src/Vsdx.Model/` 无此文件（新增，合理），但文件路径写成 Engine。r3 的 E-7 点过同一处，**改了一半**（加了"放 Vsdx.Model"的括注，没改路径） |
| 3 | T2d 的**实际修改点** `src/Vsdx.Renderer/OfficeImoVsdxRenderer.cs` 不在表内 | 计划自己的代码片段 `plan:689` 就指向该文件 `:37`，owner-rulings 的 D-18 三步也全落在这个文件。§5 只列了 `FontResolver.cs`（`plan:394`） |
| 4 | T5b 的**实际修改点** `src/Vsdx.Engine/LocalRelayout.cs` 不在表内 | 缺陷 22（`plan:339`）与 T5b（`plan:768`）都点名 `LocalRelayout`，该文件存在（`src/Vsdx.Engine/LocalRelayout.cs:15`），但 §5 无此行 |
| 附 | T2g（缺陷 25，Reader 恢复 `Node.Type` / `User.VsdxRole`）的落点只写了"读回页面背景 + 读回 Edge 标签字体 / TextBlock 字体" | `plan:396` 的 Reader 行未含角色回读，而 T2g 的验收（`plan:765`）要求 `type` 跨两次保存一致 |

另：`packet:103` 声称"§5 补齐到 **16 个文件**"，实测 §5 有 **19 行文件**（`381-399`）。
这是陈旧计数，不阻断，但说明 packet 的数字仍靠估。

### B.2 C5 的现状为什么是不完整的

r3 的 C5 落点是"**T0.5 / §12.4**"两处，理由是两个都是"同字号"断言的持有者。

- §12.4 断言 1 已改成"同一字号、**同一画布尺寸**下"，并写明 `TypeScale.ForCanvas` 的推导依据
  （11×8.5 → 8.919；20×14 → 14.000）。**这一处改得完整、可核验。**
- T0.5（`plan:676`）逐字未动。它是 §9 的任务表，实施者按任务表报价与自测，
  **这才是最容易照着做错的地方**。
- §12.4 断言 5（`plan:997`）"修后带 spec 与不带 spec 的节点宽差 < 0.05""同样缺前置——
  若两侧画布不同，这条会判一个正确实现为失败。r3 的 E-14 明确说过这一点。

我复核了 r3 引用的数值方向（用 `TextLayout.cs` 的公式手算，未跑单测）：
`(11, 8.5, 10) → 8.919`、`(20, 14, 10) → 14.000`，比值 1.57。
所以"画布不同则断言必然失败"这一定性成立，不是 r3 的臆测。

---

## C. 我复现的缺陷证据（E-6 / E-6b 的实际命令与输出）

全部产物在 `.review-scratch-r4/`（目录自带 `.gitignore` 屏蔽自身，`git status` 不显示）。
以下命令均可直接重放。

### C.1 E-6：字体族对渲染零影响（我这轮的独立复现）

**实验 1 —— TextBlock 字体，5 个取值（含一个不存在的）：**

```bash
cd /home/kity/oclab/vsdx-engine && source .envrc.sh
CLI=$PWD/src/Vsdx.Cli/bin/Release/net8.0/vsdx-tool
cd .review-scratch-r4
for f in e6/fr-*.ir.json; do
  b=$(basename "$f" .ir.json)
  $CLI execute --ir "$f" --output "e6/$b.vsdx" --json > "e6/$b.exec.json"
  $CLI render  --input "e6/$b.vsdx" --format png --output "e6/$b.png" --json > /dev/null
done
md5sum e6/*.png
```

```console
5fd102ac940eec5068b1027de18016e8  e6/fr-Arial.png
5fd102ac940eec5068b1027de18016e8  e6/fr-Courier_New.png
5fd102ac940eec5068b1027de18016e8  e6/fr-Georgia.png
5fd102ac940eec5068b1027de18016e8  e6/fr-NoSuchFont-XYZ.png      ← 不存在的字体
5fd102ac940eec5068b1027de18016e8  e6/fr-Verdana.png
```

**实验 2 —— 节点字体，走 `typography.fontFamily`（真 IR `arch.ir.json`，10 节点）：**

每个 spec 的内容是 `{"typography":{"fontFamily":"<name>"},"layout":"layered","canvas":{"mode":"auto","shrinkToFit":true}}`，
把 `<name>` 换成三个取值后分别 `execute` → `render`：

```bash
for name in "Courier New" "Georgia" "NoSuchFont-XYZ"; do
  tag=$(echo "$name" | tr ' ' '_')
  $CLI execute --ir ../examples/phase8/arch.ir.json --spec "e6node/spec-$tag.json" \
       --output "e6node/$tag.vsdx" --json
  $CLI render --input "e6node/$tag.vsdx" --format png --output "e6node/$tag.png" --json
done
md5sum e6node/*.png
```

```console
36c1d6c4fd6205ca37e708218139cdb2  e6node/Courier_New.png
36c1d6c4fd6205ca37e708218139cdb2  e6node/Georgia.png
36c1d6c4fd6205ca37e708218139cdb2  e6node/NoSuchFont-XYZ.png
```

**这不是"字体恰好长得一样"**，因为三份 VSDX 里的字族确实不同（解包可见）：

```console
$ unzip -p e6node/Georgia.vsdx visio/document.xml | grep -o '<FaceName [^>]*Name="[^"]*"'
<FaceName ID="0" Name="Georgia"
```

**实验 3 —— 逐形状混排（最能排除"整页同字体"的巧合解释）：**

对比 `mm-georgia.vsdx`（两节点都用 Georgia）、`mm-courier.vsdx`（都用 Courier New）、
`mm-mixed.vsdx`（左 Georgia / 右 Courier New，两个 `<Cell N="Font">` 值不同）：

```console
163be883278887de2dd5f73c2b0cf4d6  pershape/mm-courier.png
163be883278887de2dd5f73c2b0cf4d6  pershape/mm-georgia.png
163be883278887de2dd5f73c2b0cf4d6  pershape/mm-mixed.png
```

**对照组**（证明"渲染器没整体失灵"：字号/斜体/字色/填充分别都能单独改变 PNG）：

```console
1d4e061974a257fbb14e5a7207bd72cb  control/t-base.png      （text, Georgia, 28pt）
6e6d015ef0c24d3587ca5de95072eec5  control/t-size14.png    ← 换字号：有变化
710617c8c60494db1b4227167d363605  control/t-italic.png    ← 斜体：有变化
e21437e7ff34955cda88aeca65a64096  control/t-red.png       ← 字色 #FF0000：有变化
b66bbd7ace06014e980faf0ae9617252  control/t-blue.png      ← 字色 #0000FF：有变化
```

**根因（E2）**：`FontResolver.Resolve(bool requiresCjk, string? preferredFontFile)`
只按"是否含 CJK"在两个候选数组里挑文件；`OfficeImoVsdxRenderer.cs:37`
传入的 `request.FontFile` 只来自命令行，**从不读 VSDX**。
我验证了"如果给了字体文件名它是会生效的"（同 VSDX，Sans vs Serif 整页换 → md5 不同），
所以缺的确实只是"从 `FaceNames` 反查文件"这一跳——与计划 T2d 的诊断一致。

**顺带否证一条计划里的隐含假设**：`--font-face` 参数对 PNG **无影响**
（`--font-face "Georgia"` 与 `--font-face "Courier New"` → 都是 `e97fbc9d`），
而同页两字族只能靠一个 `--font-file` 整页替换（`mix-geo` / `mix-cour` 不同）。
即**当前架构下渲染器天然只能"整页一个字体文件"**，T2d 若只做"取第一个 FaceName"，
混排文档仍然只能近似。这不是阻断（计划没承诺混排渲染），但 §E 条件 3 会写进去。

### C.2 E-6b：`Color` / `Font` 写在 shape 级，回读与渲染双失

```bash
for i in 1 2 3; do   # 三个 IR，仅 textBlock.color 不同：#FF0000 / #0000FF / #00CC00
  $CLI execute --ir e6b/tc-$i.ir.json --output e6b/tc-$i.vsdx --json
  $CLI render  --input e6b/tc-$i.vsdx --format png --output e6b/tc-$i.png --json
  $CLI export-model --input e6b/tc-$i.vsdx --output e6b/tc-$i.model.json --json
done
md5sum e6b/tc-*.png
```

```console
627caa9f2488c2181585650eb3484d0f  e6b/tc-1.png     ← #FF0000
627caa9f2488c2181585650eb3484d0f  e6b/tc-2.png     ← #0000FF
627caa9f2488c2181585650eb3484d0f  e6b/tc-3.png     ← #00CC00
```

回读（三项全丢 `textColor`，`fontSize`/`bold`/`italic` 正常）：

```console
$ export-model → textBlocks[0].style
{'fill': '#FFFFFF', 'fillPattern': '0', 'stroke': '#000000', ...,
 'fontSize': 23.999976, 'bold': False, 'italic': False, 'hAlign': 'left', 'vAlign': 'top'}
```

XML 层级（与 `audit:697` 的片段逐字吻合）：

```xml
<Shape ID="2" NameU="TextBlock" ...>
  <Cell N="FillPattern" V="0" />
  <Cell N="LinePattern" V="0" />
  <Cell N="Color" V="#FF0000" />        ← shape 级
  <Cell N="Font"  V="Verdana" />        ← shape 级
  <Section N="Character">
    <Row IX="0"><Cell N="Size" V="0.25" U="PT" /><Cell N="Style" V="3" /></Row>
  </Section>
```

对照 Node（同一库的正确路径，`Character/Row` 内有 `Color`）：

```xml
<Section N="Character"><Row IX="0">
  <Cell N="Font" V="Georgia" /><Cell N="Color" V="#FF0000" />
  <Cell N="Size" V="0.277777777777778" U="PT" /><Cell N="Style" V="1" />
</Row></Section>
```

**我另做了"若移进正确层级会怎样"的对照，用于确认 r3 的归因是对的。**
把同一个 TextBlock 的 `Color` 移进 `Character/Row`（其余字节不动），三种取值：

```console
$ 同一 TextBlock，仅把 Color 移进 Character/Row（FaceNames 已注册）
  r-none.png  dc44b419e152b6dbfa11cbbea9f49cfa    （Character 内无 Color）
  r-red.png   e1d0b51f9a1f7dc1ffa0dfc5e0dc83fe    （#FF0000）
  r-blue.png  3c2f64dcb10b55198e9697ce3c26f406    （#0000FF）
```

**三张互不相同** → 进入正确层级后渲染器**认字色**；而在 shape 级时三色全同（`627caa9f`）。

所以 r3 的判断"层级错了 → 回读与渲染双双失效"**成立，不需要修正**。
这也说明 T2e 是"一处修好、回读与渲染同时恢复"——**方向正确，代价小**。
唯一要改的是"只移动就够"这一步（见 §C.3）：移动解决**颜色**，字体还需注册 `FaceNames`。

### C.3 按计划字面实施"两行移动"会**使结果更差**（对 T2e 修法评估的实测）

这一条是本轮最重要的负面发现。计划 `plan:748-760` 写"**T2e 的修法只有两行移动**……
把前两行改为加入 `character` 的 `Row` 内部即可"。我在包字节层面精确模拟了这次移动
（不动任何别的字节，`validate` 返回 `valid: True`），结果**不是"部分生效"，而是全面退化**：

```console
无修复（现行产出 e6b/tc-combo.vsdx）
  {'font': None, 'fontSize': 18, 'bold': True, 'italic': True, 'textColor': None}
   ← 字体/字色读不回来，但字号/粗体/斜体是好的

按计划"两行移动"（v-a：Color+Font 移进 Character/Row，未注册 FaceNames）
  {'font': None, 'fontSize': None, 'bold': None, 'italic': None, 'textColor': None}
   ← 连原本能读回的字号/粗体/斜体也一起丢了
```

为定位原因，我做了单变量拆解（每组只改一处）：

| 变体 | Character/Row 内 | FaceNames | font | fontSize | bold | italic | textColor |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 无修复 | —（裸 cell 在 shape 级） | 无 | null | 18 | true | true | null |
| v-k | —（只删了 shape 级 Font） | 无 | null | 18 | true | true | null |
| v-l | —（只删了 shape 级 Color） | 无 | null | 18 | true | true | null |
| **v-a** | **Color + Font** | **无** | **null** | **null** | **null** | **null** | **null** |
| v-c | 仅 Font | 无 | null | null | null | null | null |
| v-b | 仅 Color | 无 | null | 18 | true | true | **#FF0000** |
| v-h | 仅 Font | `"Verdana"` | **Verdana** | 18 | true | true | null |
| v-f | Color + Font | `"Verdana"` | **Verdana** | 18 | true | true | **#FF0000** |

**现象与推断（区分开写）**：

- **可观测的现象**：`ReadStyle` 读的是 `shape.TextStyle`（`OfficeImoDocumentReader.cs:613`），
  且它的返回值被整体使用（`:614-622` 一次性取 `FontFamily`/`Size`/`Bold`/`Italic`/`Color`）。
  实测：`Character/Row` 里一旦出现**未注册进 `FaceNames` 的 `Font` 值**，
  `TextStyle` 整体取到空，`fontSize`/`bold`/`italic` 一起变 null（v-a、v-c）；
  而只放 `Color` 时不触发（v-b 正常读到 `#FF0000`）；注册后 Font 与 Color 双双成功（v-f）。
- **推断（未读库源码，故只作推断）**：`OfficeIMO` 在构建 `TextStyle` 时会按 `FaceNames`
  解析字族索引，查不到即导致整段解析落空，而不是回退到默认字体。
  我**没有** `OfficeIMO.Visio` 的源码（只有 DLL），所以这条机制是推断，
  但上表 8 个变体的单变量对照与它完全一致。

**结论（三条，都可证伪）：**

1. **"两行移动"单独实施是负收益**：它把"字号/粗体/斜体可读回"变成"全部不可读回"。
   按计划字面做 T2e，验收会从"部分通过"退到"全不通过"。
2. **`FaceNames` 注册不是"次要的"**。`audit:729-730` 写"原以为只是 `FaceNames` 未注册，
   实际是**层级错了**——**注册与否是次要的**，位置不对则一切都读不到"。
   按实测，这句话**反了一半**：层级确实是最根本的，但对**字体**回读而言注册同样是必要条件；
   两件都做才成功（v-f），只做任一件都是 `null`（v-a / v-c）。
3. **T2e 的最小改动是两步**：移单元格 **且** 注册 `FaceNames`。两步的先后顺序也重要——
   若先合入"移动"再合入"注册"，中间那个提交是**回归**状态。
   实施时应让两者进同一个提交，或先做注册。

三色三字体的渲染侧也已验证：`f-georgia` / `f-mono` / `f-verdana` 三张 PNG **仍然全同**
（`28da9f0b…`），因为渲染器还没接字族（T2d 未做）。
所以 T2e 的验收"三色三字体产出 PNG 互不相同"里，**"三字体"这一半依赖 T2d**，
实施者若单独跑 T2e 会看到"三色不同、三字体相同"。
这不影响验收可证伪，但两条任务的依赖关系应在 §9 写明（§E 条件 3）。

---

## D. 裁决

### **FAIL**

**这不是"4 个阻断项没关"导致的 FAIL——4 项里 3 项已干净关闭、第 4 项基本关闭。
FAIL 的直接原因是 r3 给出的转 PASS 条件里，C3 未落定、C5 只落了三分之二，
且文档内部仍存在与 r3 的 E-9 同类的自相矛盾。**

**另有一条比 FAIL 更值得注意的发现（§C.3）**：E-6b 作为缺陷登记得没问题，
但计划写的修法（`plan:748` "只有两行移动"）**照字面实施会让结果比现状更差**——
`fontSize`/`bold`/`italic` 会从可读回退化为全 `null`。我把它放在条件 3，
因为它是"照计划做会出错"，不是"计划没写"。

r3 §F.4 原文写得很明确：

> 修完 F.2 的四个阻断项…**且 F.3 的 C3/C4/C5 落定** → PASS WITH CONDITIONS

我逐项实测的结果：

| 转 PASS 的前置 | 状态 |
| --- | --- |
| 阻断 1 E-6 已登记 + 可证伪验收 | ✅ |
| 阻断 2 E-6b 已登记 + 可证伪验收 | ✅（**但照字面实施会回归**，见 §C.3 / 条件 3） |
| 阻断 3 E-9 表格删除、表述唯一 | ✅ |
| 阻断 4 E-4/D.3 闸门覆盖 | ⚠️ 部分关闭：闸门 0 方向对，但闸门 3 清单不全（见 A 表第 4 行） |
| **C3 破坏性变更三处一致** | ❌ **plan §7 仍写两处；packet 说"以 §7 为准，三处"——指向一个说两处的节** |
| **C4 §5 文件表补全** | ⚠️ 7 个点名文件都在，但新增 4 处路径/覆盖错误 |
| **C5 同字号断言加画布前置** | ❌ **T0.5 与 §12.4 断言 5 未加** |

**为什么 C3 的残留足以 FAIL，而不是降级为条件。**
`plan:178`（§1 第 9 条："⚠️ **有三处变更**" + 三行表）与 `plan:649`（§7："**两处**破坏性变更：
`Canvas.Unit` 与 `Direction`"，且表里无 `mode=fixed`）是**同一份文档内的直接互否**，
而 packet 把 §7 指定为准绳（`packet:110`"以 plan §7 为准，**三处**"）。
这正是 r3 判 E-9 为阻断的同一模式：实施者/负责人按不同段落得到不同结论。
差别只在于后果：E-9 会让人**违反负责人裁决**，C3 会让人**漏发一条发布说明**
（升级后 `mode=fixed` 的页面尺寸会真的变，用户没有预告）。后者较轻，但性质相同。

同时我必须说清：**这是"改三行字"就能关闭的 FAIL。**
`plan:649` 改成三处、§7 表补 `mode=fixed` 一行、`packet:108` 标题改三处、`plan:676` 与
`plan:997` 各加"同一画布尺寸"、§5 修 4 处路径、`packet:103` 的"16 个文件"改 19。
不存在需要重新设计的东西。

### D.1 我明确认可的部分（真进步，逐条核过）

| # | 进步 | 我的核验 |
| --- | --- | --- |
| 1 | E-9 的矛盾表**真的删了**，不是加注对冲 | 现行 plan 只剩一处"保留并转置"引用，且明确标为"修正说明"；六个落点一致 |
| 2 | E-6 / E-6b 进入缺陷清单（18 / 19，P0），而非停留在审查文件里 | `audit:1166-1167`、`plan:335-336` 逐号对应，任务 T2d/T2e 各有独立可证伪验收 |
| 3 | 新增闸门 0（加载 Schema） | 我用 `jsonschema` 独立验证：它能挡住 `typografy`、`specVersion:"99.0"`、`layout:"kaboom"`、`containerLayouts` 非法值——**覆盖面远大于手抄白名单**，方向正确 |
| 4 | C4 点名的 7 个文件全部进表 | 逐个 `grep` 确认（含 §12.3 的 `ProtocolRunner`、`LayoutStrategies`、两个"新增"、`FontResolver`、`JsonGate`） |
| 5 | §12.4 断言 1 的修正带复算依据 | 不只加了"同一画布"，还写明 8.919 vs 14.000 的来源——这点比 r3 要求的更多 |
| 6 | D-4 / D-8 / D-18 全部登记为裁决而非待决 | `owner-rulings` 有独立条目；`open-decisions` 开头即"无待决项" |
| 7 | 审查期间仍在自我校正（如 T2d 一节补 `fc-match` 实测） | 我复核了其中四个族名解析，`fc-match` 输出与计划一致；且计划自己指出"外部进程不能作运行时依赖"——判断正确 |

### D.2 为什么不是 PASS WITH CONDITIONS

因为 r3 把 C3/C5 写成"落定"而非"实施时注意"。C3 与 C5 都属于**文档层**的事：
不需要写代码、不需要跑实验，只需要把自己已经知道的事写在同一页上。

而且 C3 这一处**已经是第三次被点名**：

```console
$ sed -n '666,669p' docs/phase8/phase8.2-rereview.md     # r2 的 M12
3. **破坏性变更清单与 plan §7 冲突。** packet §五 C2 列的是
   `Direction` + **`mode=fixed`**；plan §7 写的却是
   "**两处破坏性变更：`Canvas.Unit` 与 `Direction`**"（plan:365）。
   **两份文档对"哪两处是破坏性变更"给出不同答案。**
$ sed -n '1586p' docs/phase8/phase8.2-review-r3.md        # r3 的 C3
| C3 | 破坏性变更从"两处"改为**三处**，补 `mode=fixed`（E-8） | §7 + packet §五 |
```

r2 指出时 plan 的行号是 `:365`，r3 时是 `:405`，现在是 `:649`——**行号一直在动，那句话一字未改**。
packet 里"统一为三处"的表述是后来加的（`git log -S"破坏性变更统一为三处"` → `8d3e468`），
加的时候没有回头改 plan §7，于是从"两份文档冲突"变成了"packet 自己一句话里两种说法"。

本轮 C4 新增的 4 处路径错误（含一个指向不存在文件的行）是同一问题的又一次显形：
`529828b` 重写 §5 时新增了一行 `src/Vsdx.Protocol/DiagramIrConverter.cs`，而该类在 `DiagramIr.cs:232`，
从没有过独立文件；后来的 `9f1281c`（"complete the file table"）又把 `FontResolver` 与 `JsonGate`
补进来，行数从 17 到 19，**但同一次补齐没有回头核对已有行的路径是否真实存在**。
若我在此处放行，下一轮大概率还会因同类问题再 FAIL 一次。

### D.3 如果只修 C3/C5 会怎样

**只修条件 1、2**（C3 的 `plan:649` + §7 表 + `packet:108`；C5 的 `plan:676` + `plan:997`）
→ 我会判 **PASS WITH CONDITIONS**，条件=Stage 5 按 T2d/T2e 实现并验证。
理由：r3 的四个阻断项已关、C3/C4/C5 的**内容**已具备，剩余是行号级的措辞。

完整的兑换表见 §E 末尾。这里只强调一句：**条件 3 虽然不影响"阻断项是否关闭"，
但它是唯一一条"照计划做会主动把功能改坏"的条目**，我建议与条件 1、2 一并处理。

---

## E. 条件（按此修完即可转 PASS WITH CONDITIONS）

以下 6 条**全部是文档改动**，不改产品代码、不需新实验。

### 条件 1（关闭 C3，必须）

1. `plan:649`："两处破坏性变更"→"**三处**"，并把 `mode=fixed` 写进 §7 的兼容性表
   （口径可直接复用 `plan:193-199` 的三行表：`Direction` / `mode=fixed` / `Canvas.Unit`）。
2. `packet:108` 标题"两处仍然破坏性的变更"→"三处"（否则 packet 自己的标题与 `packet:110` 互否）。
3. `packet:110` 的"以 plan §7 为准"保留，但必须与修改后的 §7 一致。

**验收方式：** `grep -n "两处破坏性\|两处仍然破坏性" docs/phase8/phase8.2-{plan,review-packet}.md`
零命中；且 §7 表中含 `mode=fixed` 行。

### 条件 2（关闭 C5，必须）

1. `plan:676`（T0.5 验收）加"**同一字号、同一画布尺寸下**"，与 `plan:993` 用同一措辞。
2. `plan:997`（§12.4 断言 5）同样加前置。

**验收方式：** `grep -n "同字号" docs/phase8/phase8.2-plan.md` 的每一处都带"同一画布"限定；
`plan:676`、`plan:993`、`plan:997` 三处措辞一致。

### 条件 3（修正 T2e 的修法范围，必须——按现计划实施会产生回归）

1. `plan:748` 的"**修法只有两行移动**"改为**两步且必须同提交**：
   **(a) 单元格移入 `Character/Row`；(b) 字族注册进 `document.xml` 的 `FaceNames`**
   （`<FaceName NameU="<family>" …/>`）。
   依据 §C.3 的单变量对照：**只做 (a) 会使 `fontSize`/`bold`/`italic` 从"可读回"退化为 `null`**
   （v-a），比现状更差；做 (a)+(b) 才全部成功（v-f）。
   若分两次提交，中间那次是**回归**——因此必须同一提交，或先做 (b)。
2. `plan:729-730`（以及 `audit:729-730` 同段）"**注册与否是次要的**"应改为
   "层级与注册都是必要条件；对 `font` 而言注册不可省"。这句话现在是错的，
   且它是 D-4 方案的立论之一，会直接影响 T2 的工作量评估。
3. `plan:746`（T2e 验收）注明"三字体 PNG 互不相同"依赖 T2d，或在 T2d 之后再验这一半。
4. `plan:759-760` 的括注"（Node 的 `<Row><Cell N="Font" …/></Row>` 已验证可回读）"
   应补"**且该包的 `document.xml` 已含对应 `FaceName`**"，否则会被读成"位置对就够了"。

**验收方式（两条，都要）：**

- `export-model` 对 `{color:"#FF0000", font:"Verdana"}` 的 TextBlock 返回
  `textColor == "#FF0000"` **且** `font == "Verdana"`（同时非 null）；
- **回归断言**：同一个测试里另验一个**不含 `Font`** 的 TextBlock，
  其 `fontSize`/`bold`/`italic` 仍与输入一致（防止 v-a 那种"修好字体、弄丢字号"）。

### 条件 4（落定闸门 0/1 的关系与 IR 侧覆盖，必须）

1. `plan:1170-1181` 的闸门 0 明确写"**design-spec 与 diagram-ir 两份 Schema 都校验**"，
   并给出 `JsonSchemaValidator` 的实现来源（自研最小校验器 / 引入依赖二选一，
   依任务书 §4 评估后写明）。当前 `Directory.Packages.props` 无任何 JSON Schema 包，
   计划未说明这一点。
2. 闸门 0 与闸门 1（`plan:1205-1228` 的 `JsonDocument` 预扫）**二选一为主、另一为补充**，
   并说明分工；不能两条都写成"选定方案"。
3. `plan:1192-1202` 闸门 3：`NotYetExecuted` 的实际数组与正文对齐。正文说须含
   `metadata.*` 与 `IrContainer.Kind` 的部分取值，代码行只有 `{ "metadata.*" }`；
   `IrEdge.Relation`（8 个取值）当前既不在白名单也不在清单，须明确归入其一。
4. `plan §9` 给闸门 0/1/3 实现一个任务号（如 `T2h`），使其有独立验收；
   现状 26 个任务里只有 T2f 覆盖闸门 2，而 §5（`plan:395`）却新增了 `JsonGate.cs`
   统一实现四道闸门——**有实现落点、无验收任务**。

**验收方式：** `plan:1250-1254`（§15.4）的三条之外，补一条
"`containerLayouts:{"svc":"kaboom"}` → `unmet` 含 `input.unknown-enum`，
且 `applied` 不出现 `relaid as kaboom`"——我实测当前行为是
`applied: ['container svc relaid as kaboom']`、`unmet: []`，这条断言可证伪。

### 条件 5（修正 §5 的 4 处路径，必须）

| 改法 |
| --- |
| 删 `plan:393` 的 `src/Vsdx.Protocol/DiagramIrConverter.cs`（类在 `DiagramIr.cs:232`，已在 `plan:387` 覆盖） |
| `plan:391` 路径改 `src/Vsdx.Model/TypeScaleDefaults.cs`（与同格"放 `Vsdx.Model`"一致） |
| 补 `src/Vsdx.Renderer/OfficeImoVsdxRenderer.cs`（T2d 的 `:37`） |
| 补 `src/Vsdx.Engine/LocalRelayout.cs`（T5b） |
| `plan:396` 的 Reader 行补"按 `User.VsdxRole` 回读 `Node.Type`"（T2g，缺陷 25） |
| `packet:103` 的"16 个文件"改为实际行数 |

**验收方式：** §5 每一行的路径经 `ls` 可判定"存在/新增"；`grep -c` 的行数与 packet 所述一致。

### 条件 6（清理表格断裂残留，建议）

`plan` 里有两处表格被"插入的说明文字"截断，渲染结果不再是一张表：

1. `plan:843` 的孤立表头 `| 场景 | waypoints 行为 | 理由 |`——下面的分隔行与数据行已被删除，
   只剩表头孤零零一行（E-9 删表时漏删）。
2. §9 的任务表**断成三段**：`plan:673-684`（T0…T2d）→ 大段 T2d 说明 →
   `plan:746` 单独一行 T2e → 又一段说明 → `plan:764-778`（T6b、T2g、T1d、T1c、T5b、T2f、T3…T10）。
   三段的列头只在第一段，后两段没有表头。

这一条**不构成矛盾、不影响任何验收的可证伪性**（行的内容都在，`plan:676` 的 T0.5 也能正常显示），
所以只列为建议。但它解释了为什么 C5 的修法容易漏：**读者容易只看到第一段表**，
`T2e`（`plan:746`）与 `T6b`（`plan:764`）都在这张"看起来结束了的表"之外。
若把 §9 重排成一张连续表，C5 与后续验收的漏改概率会下降。

**验收方式：** 无需机器判定；重排后 §9 从表头到 T10 连续、无插入段落即可。

### 转为 PASS WITH CONDITIONS 的判定标准

给出一个**可操作**的兑换表，避免"再改一轮还是 FAIL"：

| 你修了哪些 | 我给什么 | 理由 |
| --- | --- | --- |
| 条件 1 + 2（C3、C5） | **PASS WITH CONDITIONS** | 这两条是 r3 §F.4 的明文前置；修完即满足"4 阻断已关 + C3/C4/C5 落定" |
| 条件 1 + 2 + 3 | **PASS WITH CONDITIONS**，条件只剩"Stage 5 按 T2d/T2e 实现 + 上述验收" | 条件 3 消掉了唯一"照计划做会回归"的坑 |
| 条件 1 + 2 + 3 + 4 | 同上，且实施风险最低 | 闸门分工明确后，闸门 3 不再有"永远不报告"的漏项 |
| **只修 3、4，不修 1、2** | 仍 **FAIL** | C3/C5 是 r3 点名的前置，且都是改几行字的事；跳过它们说明一致性检查仍未建立 |
| 只修 1，不修 2 | **FAIL** | 同上 |

条件 5、6 为随附项，可与 Stage 5 并行，**不作为转 PASS 的前置**。

---

## F. 局限

### F.1 无法验证的事项

| # | 局限 | 影响 |
| --- | --- | --- |
| 1 | **无 Microsoft Visio（L4 未验证）**，与任务书 §5.7 一致 | 我无法判断"VSDX 里写了 `Font` 而渲染器不认"在 Visio 打开时是否生效。**所以 E-6 是"Renderer 与 VSDX 不一致"，不是"VSDX 写错了"**——这个区分对 T2d 的范围判断重要，我按此写 |
| 2 | **`FontResolver` 的候选字体文件在本机是否存在，会影响"字体不可渲染"的结论强度** | 我的实验在"DejaVu 可读、Windows 字体可读"的机器上做。若换一台无任何候选字体的机器，`render` 的 `font` 字段会变成 `null` 并可能触发 `render.cjk-no-font` 警告——那是另一条路径，未测<br>另：我**不持有 `OfficeIMO.Visio` 3.6.1 的源码**（仅 DLL，见 `.nuget/officeimo.visio/3.6.1/`），所以 §C.3 的"为什么 `TextStyle` 整段落空"只能从外部行为反推。**"只移动会退化"是实测，"原因是 FaceNames 索引解析落空"是推断** |
| 3 | **T2e 的修法验证是在手工改包的 VSDX 上做的**，不是改代码后重跑 | 我改的是 `page1.xml` 与 `document.xml` 的文本节点（15 个 part 的包，`validate` 返回 `valid: true`）。真实实现走 OfficeIMO 的对象模型，可能还有其他副作用（如样式索引 `TextStyle="0"` 的联动）。**"必须注册 FaceNames、只移动会退化"我有实测支撑；"只此两步就够"是上界而非下界** |
| 4 | **任务书原文（`goal-20261126-2034.md`）不在仓库内** | r3 引用的 §3.5/§4.4 我只能通过 `owner-rulings` 与 `plan` 的转述看到。凡涉及"任务书要求"的判断，我按"转述自洽 + 与实测行为一致"接受，**未核原文** |
| 5 | **未重跑 r3 的 17 项缺陷** | 本轮范围是"4 阻断 + 3 条件"，我没有重新验证 25 项缺陷各自是否成立。若某项在 r3 之后被证伪，我看不到 |
| 6 | **`jsonschema`（Python）与将来 `JsonSchemaValidator`（.NET）的方言可能不同** | 我用 Draft 2020-12 校验 `schemas/phase7/*.schema.json`，该文件未声明 `$schema`。若实现选 Draft 7 或更早，`additionalProperties` 行为一致，但 `pattern`/`enum` 报错文案会不同——只影响报错文本，不影响"能否挡住"的定性 |
| 7 | **`plan:993` 的 8.919 / 14.000 是我按 `TextLayout.cs` 公式手算复核的**，未跑单测 | 定性结论（画布不同 → 字号不同 → 框体不等）稳健；具体数值可能有 ±0.05 误差。r3 也登记过同一局限 |
| 8 | **文档在审查期间改动过两次**（见开头披露，1399→1420→1438 行） | 我的结论绑定 `e4bd0bc2`（= `855a0cd`）。若之后还有提交，行号与结论都需重新对齐 |

### F.2 我这一轮可能的错误

按我批评审计的标准，也自查三条：

1. **把"两处 vs 三处"判为 FAIL 是裁量，不是硬事实。** r3 的 F.4 把 C3 写成转 PASS 的前置，
   我据此判 FAIL；但也可以论证"三处的内容已经在 §1 与 packet 表里，§7 只是一句没同步的旧话"，
   从而降为条件。我选择 FAIL 的理由是：**同一文档内互否、且 packet 明确把互否的那一节指定为准绳**。
   这个理由我认为站得住，但它确实是一个判断而非测量。
2. **C.3 的实验改变了包的字节结构**（手写 XML），与"产品代码修好后"的产物可能不完全等价。
   我用 `validate` 确认包合法，用 `export-model` 确认回读，但**没有反向验证"渲染结果与
   正确实现一致"**——只验证了"回读字段非 null"与"三色 PNG 互异"。
   另外 C.3 里"库为何整段返回 null"是**推断**：我不持有 `OfficeIMO.Visio` 源码
   （只有 `3.6.1` 的 DLL），所以只能从 8 个单变量变体的行为反推。
   **结论"只移动会退化"是实测，"原因是 FaceNames 索引解析落空"是推断。**
3. **我没有核 r3 引用的 `e3d355cd…` 那三张 PNG**（`.review-scratch-r3/` 里不存在该文件名，
   只有 `fr-*` / `nfr-*`）。我用自己重建的样本复现了同一性质，但**不能声称复现了 r3 的同一份证据**。
   `audit:666-668` 的 md5 与我的不同（样本不同，属正常）。

### F.3 本轮**没有**做的事（边界）

- 未审 Stage 5 的任何实现（尚未开工，符合门禁）。
- 未评价 25 项缺陷的修法是否正确（r3 已做；我只核了 T2d/T2e 两条的可行性与范围）。
- 未审 `benchmarks/` 的"18 项硬约束"、未审 `LayoutAnalyzer` 内部、未审 `RepairController`。
- 未修改任何源码或既有文档；`.gitignore` 未改（`.review-scratch-r4/` 用自带 `.gitignore` 屏蔽，
  与 r3 的做法一致）。

---

## 附：本轮引用位置索引（便于复核）

| 主张 | 位置 |
| --- | --- |
| 计划 §1 称"有三处变更"并列出三项 | `plan:178`、`plan:193-199` |
| 计划 §7 仍称"两处"且无 `mode=fixed` 行 | `plan:641-649` |
| packet 称"统一为三处"，却以 §7 为准；标题仍写两处 | `packet:103`、`packet:108`、`packet:110` |
| T0.5 验收缺"同一画布" | `plan:676` |
| §12.4 断言 1 已加、断言 5 未加 | `plan:993`、`plan:997` |
| §5 的 19 行文件表 | `plan:381-399` |
| `DiagramIrConverter` 真实位置 | `src/Vsdx.Protocol/DiagramIr.cs:232` |
| T2d / T2e 验收 | `plan:684`、`plan:746` |
| T2e"两行移动"表述 | `plan:748-760`（`plan:748` 为"修法只有两行移动"） |
| §C.3 的单变量对照材料 | `.review-scratch-r4/fixprobe/`（16 个手工变体包：`v-a`…`v-l` 9 个 + `f-*` 3 个 + `r-*` 3 个 + `tc-moved`，各附 `export-model` 结果） |
| E-9 修正说明与现行两表 | `plan:846-862` |
| E-9 删除残留（孤立表头） | `plan:843` |
| 闸门 0 | `plan:1170-1181` |
| 4 类漏网项表 | `plan:1183-1190` |
| 闸门 3 与 `NotYetExecuted` | `plan:1192-1202` |
| §15.4 三条验收 | `plan:1248-1254` |
| 审计缺陷 18 / 19 / 21 | `audit:1166`、`audit:1167`、`audit:1169` |
| 审计 §2.15（E-6） | `audit:661-688` |
| 审计 §2.16（E-6b） | `audit:690-730`，其中"注册与否是次要的"在 `audit:729-730`（本审查 §C.3 证明此句需改） |
| 审计 §2.17（非法枚举） | `audit:626-657` |
| D-4 / D-18 裁决 | `owner-rulings:114`、`owner-rulings:160` |
| `owner-rulings` 汇总仍写 D-4"待裁决" | `owner-rulings:192-198` |
| `open-decisions` 称无待决项 | `open-decisions:4`、`open-decisions:8-9` |
| 渲染器只在第 37 行解析字体 | `src/Vsdx.Renderer/OfficeImoVsdxRenderer.cs:37`（`resolver` 在 `:36`） |
| `FontResolver` 只按 CJK 二选一 | `src/Vsdx.Renderer/FontResolver.cs:60-84` |
| TextBlock 裸 Cell | `src/Vsdx.Adapters/VisioTextBlockBridge.cs:140-141` |
| Reader 只读 `shape.TextStyle` | `src/Vsdx.Adapters/OfficeImoDocumentReader.cs:602-622`（`TextColor` 在 `:620`） |
| design-spec Schema 的 enum 与 `additionalProperties:false` | `schemas/phase7/design-spec.schema.json`（我逐键解析确认） |
| diagram-ir Schema 的 enum（role/anchor.mode/relation/fromPort/kind/row.type） | `schemas/phase7/diagram-ir.schema.json` |
| `Directory.Packages.props` 无 JSON Schema 依赖 | `Directory.Packages.props:6-15` |

---

**审查者签名：** 独立第三方（非 Stage 1/2 参与者，未撰写此前任何一轮审查）
**审查方式：** 文档 md5 固化 + 源码定位 + 端到端 CLI 实测 + 解包 XML + PNG md5/像素比对 + 独立 Schema 校验
**产品代码改动：** 无。`git status --short` 仅本文件为新增未跟踪项（实测产物在 gitignored 的 `.review-scratch-r4/`）。
**实测产物：** `.review-scratch-r4/`（263 个文件：IR/Spec/VSDX/PNG/JSON，含 `pinned/` 文档快照、`fixprobe/` 的 16 个手工变体包与 `evidence-index.txt`），可复现 §C 全部命令。
