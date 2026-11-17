# Phase 8.2 第三轮独立审查

**审查对象（本轮实际读取、md5 固定）：**

| 文件 | md5 | 行数 |
| --- | --- | --- |
| `docs/phase8/phase8.2-audit.md` | `aa98f73f6bba1bb2dbdf3dcc02055013` | 812 |
| `docs/phase8/phase8.2-plan.md` | `9d3a1b3dcb42d0b176a643c4d5e306d0` | 1080 |
| `docs/phase8/phase8.2-review-packet.md` | `5ed2b34a4766fd8c16106d33f3c9d400` | 140 |
| `docs/phase8/phase8.2-owner-rulings.md` | `ffd6c56a06b9c6cb1b8598cf1f10b213` | 133 |
| `docs/phase8/phase8.2-open-decisions.md` | `48eb18183c092da5aafd111e18a176a0` | 73 |

**基线：** `HEAD=4334541`。上表五份文档在审查期间**未被改动**（`git diff --stat -- docs/` 为空，md5 与审查开始时一致），`src/` `tests/` 亦无改动（`git diff --stat -- src/ tests/` 为空）。
**审查日期：** 2026-11-18
**审查者立场：** 与前两轮**无关联**的第三方。未采信任何修订说明，全部结论来自本人的源码阅读与实机执行。

### 关于"文档在审查期间发生变动"的声明（必须披露）

我开工时（2026-11-18 约 00:00 之前，`HEAD=4334541` 尚未生成）工作树里有未提交改动。
实测时间线：

```console
$ git log -3 --format="%h %ci %s"
4334541 2026-11-17 23:29:51  docs(phase8.2): quantify defect 8 ...
ea504fd 2026-11-17 23:28:30  docs(phase8.2): the defect-to-task mapping is 17:17:16+2 ...
873071b 2026-11-17 23:26:01  docs(phase8.2): both open decisions were already settled ...
```

我在审查过程中两次观察到工作树变动：
先看到 `M docs/phase8/phase8.2-plan.md`，后看到 `M docs/phase8/phase8.2-audit.md`，
最终这两次改动以 `ea504fd`、`4334541` 落库。

**本文所有结论针对上表 md5 固定的版本**（与 `4334541` 内容一致）——即"缺陷 8 已量化为 0%"
与"映射为 17:17:16+2"这两处修订**之后**的版本。
若文档继续变动，本文的 `audit:xxx` / `plan:xxx` 行号引用会漂移，**结论需按 md5 重新对齐**。

这一点与上一轮复审（r2 §1.3）披露的情况相同：**修订与审查并行，是本项目当前的常态**。
它本身不是错误，但意味着"审查针对哪个版本"必须由 md5 而非日期或提交号确定。

---

## 审查方法与独立性

### 独立性声明

- 我未参与 Stage 1/Stage 2，也未撰写前两份审查（`phase8.2-independent-review.md`、`phase8.2-rereview.md`）。
  本文件的所有判断来自本轮实测，**不沿用前两轮的结论，包括它们对实施者的有利结论**。
- 前两轮审查在若干处**本身就错了**或漏了（见 §E-1、§E-2、E-6b、B.5），我给出了我自己的实测。
- 我**不是**由实施 Agent 扮演的 Reviewer：本会话没有修改任何产品代码，也没有修改任何既有文档
  （审查结束时 `git status --short` 只有本文件为新增未跟踪项，见文末）。

### 证据等级

| 等级 | 类型 | 用途 |
| --- | --- | --- |
| **E1** | 端到端实测（`execute` → `export-model` → 解包 XML → PNG 逐像素比对） | 判定一切行为主张 |
| **E2** | 源码定位（`file:line`）+ 反编译/strings 探测库 API | 判定可行性与根本原因 |
| **E3** | 文档陈述 | **仅用于定位待验命题，不作结论依据** |

所有实测产物在 `.review-scratch-r3/`（已加 `.gitignore`，`git status` 不显示），可复现。

### 已执行的操作

```console
$ source .envrc.sh && dotnet build src/Vsdx.Cli/Vsdx.Cli.csproj -c Release
  已成功生成。0 个警告 0 个错误
$ dotnet test tests/Vsdx.Tests/Vsdx.Tests.csproj -c Release
  已通过! - 失败: 0，通过: 373，已跳过: 0，总计: 373

$ git status --short            # 审查开始与结束
（空）

$ git diff --stat -- src/ tests/
（空）                            # 产品代码零改动，产品代码未被本审查触碰
```

CLI 全部通过 `execute / export-model / create / update / render / check-layout / validate` 实机调用，
另有 `unzip -p <pkg> visio/pages/pages.xml`、`visio/document.xml`、`visio/pages/page1.xml` 直接解包取证。

---

## A. 17 项缺陷抽样核验

按要求至少 8 项含全部 P0。我**全查了 17 项**（P0 5 项 / P1 10 项 / P2 2 项），下表给出结论与一手证据。

### A.1 核验总表

| # | 缺陷（audit §10 口径） | 严重度 | 我的结论 | 一手证据 |
| --- | --- | --- | --- | --- |
| 1 | `Direction` 四策略全无效 | P0 | ✅ **成立** | 见 A.2 |
| 2 | `Palette` 未执行 + 未知值不报告 | P0 | ✅ **成立** | 见 A.3 |
| 3 | `Theme.Background` 设置后丢失 | P1 | ✅ **成立** | 见 A.4 |
| 4 | Typography 仅覆盖 Node | P1 | ✅ **成立**（且比 audit 描述的更窄） | 见 A.5 |
| 5 | 容器色硬编码绕过 Theme | P1 | ✅ **成立** | 见 A.6 |
| 6 | `Canvas.Unit` 不换算 | P1 | ✅ **成立**（但 audit 的定性表述自相矛盾，见 E-1） | 见 A.7 |
| 7 | `containerLayouts` manual 下静默跳过 | P1 | ✅ **成立** | 见 A.8 |
| 8 | `ObjectMapping` 仅首页 + 仅 Node/Container | P1 | ✅ **成立**（多页形态更糟：整个对象为 `{}`） | 见 A.9 |
| 9 | `typography.min_size` 死代码 | P2 | ✅ **成立** | 见 A.10 |
| 10 | `BaseSize` 抹平字体层级 | P1 | ✅ **成立** | 见 A.11 |
| 11 | 字号在布局后应用 → 文字溢出 | P1 | ⚠️ **现象成立，audit 的归因不成立**（plan 已改正为"阶段缺失"，正确） | 见 A.12 |
| 12 | `roleColors` 无 `default` 回退 | P2 | ✅ **成立**（且拼错角色无提示） | 见 A.13 |
| 13 | `mode=fixed` 名存实亡 | P0 | ✅ **成立**（双向失效，均静默） | 见 A.14 |
| 14 | `swimlane` 经协议不可达 | P1 | ✅ **成立** | 见 A.15 |
| 15 | 装饰元素独立夹紧致间距塌陷 | P1 | ✅ **成立**（可稳定复现：0.6" → 0.1"） | 见 A.16 |
| 16 | IR 容器 `order` 被忽略致流程倒置 | P0 | ✅ **成立** | 见 A.17 |
| 17 | 缺"无法表达/渲染必须报告"机制 | P1 | ✅ **成立**（且比 audit 说得更严重，见 E-6） | 见 A.18 |

**17/17 成立。** 无一项被我否证。

### A.2 缺陷 1：`Direction` 零影响（P0）

四策略 × 两方向，用 `arch.ir.json`：

```console
$ for l in layered flow tree grid; do for d in leftToRight topToBottom; do
    $CLI execute --ir arch.ir.json --spec '{"layout":"'$l'","direction":"'$d'"}' \
                 --output dir-$l-$d.vsdx --json; done; done
```

模型节点包围盒跨度（xspread, yspread）：

```
layered  ltr=(7.65, 4.85) ttb=(7.65, 4.85) identical=True
flow     ltr=(7.65, 4.85) ttb=(7.65, 4.85) identical=True
tree     ltr=(7.65, 4.85) ttb=(7.65, 4.85) identical=True
grid     ltr=(7.65, 4.85) ttb=(7.65, 4.85) identical=True
```

根因（E2）：

```console
$ grep -rn "Direction" src/Vsdx.Engine/*.cs src/Vsdx.Protocol/*.cs
src/Vsdx.Engine/CandidateComparison.cs:29:  public LayoutDirection Direction ...
src/Vsdx.Engine/LayoutStrategies.cs:37:   public enum LayoutDirection
src/Vsdx.Engine/LayoutStrategies.cs:46:   public LayoutDirection Direction ...
src/Vsdx.Protocol/DesignSpec.cs:134:      Direction = spec.Direction.Equals(...)
```

**没有任何布局引擎读取 `options.Direction`。** 枚举、字段、赋值三者齐备，消费者为零。
audit 与 plan 的定性完全正确。

### A.3 缺陷 2：Palette 完全未执行，未知值静默（P0）

```console
$ $CLI execute --ir arch.ir.json --spec '{"theme":{"palette":"does-not-exist"}}' --json
  status: succeeded   unmet: []   applied: ['layout=Layered','container.bands','canvas.auto']
```

`enterprise` vs `technical` 两套 palette 的模型 diff（去掉 id/hash/时间戳等元数据后）：

```
diff count: 7
  /id, /metadata/sourceFile, /metadata/sourceHash,
  /metadata/history[0]/target, /metadata/history[0]/at,
  /metadata/modified, /metadata/created
```

**7 处差异全部是元数据，零处视觉属性。**

根因（E2）：

```console
$ grep -rn "Palette" src/*/*.cs
src/Vsdx.Protocol/DesignSpec.cs:83:  public string? Palette { get; set; }
src/Vsdx.Model/DiagramModel.cs:317:  public string? Palette { get; set; }
```

只有声明，无读取方。Schema 也只写 `"palette": {"type": "string"}`（无 `enum`），
`schemas/phase7/design-spec.schema.json`。三方失守（Schema/执行/报告）成立。

**但我另发现一处 audit 未写的事实**：`execute` 根本不加载 Schema，见 §E-4。

### A.4 缺陷 3：`Theme.Background` 设置后丢失（P1）

```console
$ $CLI execute --ir arch.ir.json --spec '{"theme":{"background":"#FAFAFA"}}' --json
  applied: ['layout=Layered','container.bands','theme.background','canvas.auto']   ← 声称已应用
$ $CLI export-model --input bg.vsdx --json
  diagram bg: None   page bg: None                                                   ← 已丢失
$ unzip -p bg.vsdx visio/pages/pages.xml | grep -o 'PageBackground[^/]*'
（无输出）
```

`appliedDesignNotes` 明确告诉调用方"背景已应用"，实际既没落盘也没回读。**成立**，
且比"未实现"更糟——这是**虚假的成功报告**。

### A.5 缺陷 4：Typography 只覆盖 Node（P1）

```console
$ $CLI execute --ir arch.ir.json --spec '{"typography":{"fontFamily":"Courier New","baseSize":14}}' --json
  status succeeded   unmet []
```

回读结果：

```
NODES : ('gw','Courier New',14) ('mobile','Courier New',14) ('web','Courier New',14) ('auth','Courier New',14)
GROUPS: ('data', None, 12) ('svc', None, 12) ('edge', None, 12)   ← 硬编码 12，font=None
EDGES : ('e1', None) ('e2', None) ('e3', None)                    ← 无任何样式
TEXTS : ('title', None, None) ('subtitle', None, None) ...        ← 完全不受 spec 影响
```

**成立。** 且我说"比 audit 更窄"是指：audit §1 表格的表述是"`Typography.FontFamily` ⚠️ 仅覆盖 Node"，
但**实际是只覆盖 Node 的 `Style.Font` 字段**——TextBlock 的字体走的是 `VisioTextBlockBridge`
另一条完全独立的路径（`SizeForRole` 硬编码 + 裸 `Cell N="Font"`），连"被 Node 路径部分覆盖"都谈不上。

### A.6 缺陷 5：容器色硬编码（P1）

```console
$ grep -c "bandFills\|#EAF3FB" src/Vsdx.Protocol/DesignSpec.cs
3
```

`DesignSpec.cs:199-226` 的 `bandFills/bandStrokes/bandText` 三个五色数组仍在，直接写 `group.Style`。
无论 palette 是什么，容器恒为这五组色。实测三个容器回读色 `/FDF2E9` `/EDF7ED` `/EAF3FB`。**成立。**

### A.7 缺陷 6：`Canvas.Unit` 不换算（P1）

```console
$ for u in inches cm mm; do ... '{"canvas":{"mode":"fixed","width":10,"height":8,"unit":"'$u'","shrinkToFit":false}}'; done
```

模型回读：

```
inches {'width': 10, 'height': 8, 'unit': 'inches'}
cm     {'width': 10, 'height': 8, 'unit': 'inches'}
mm     {'width': 10, 'height': 8, 'unit': 'inches'}
```

XML：

```
[u-inches] PageWidth" V="10" U="IN"
[u-cm]     PageWidth" V="10" U="IN"     ← 完全相同
```

`10cm` 与 `10in` 产出逐字节相同的页面几何。**成立。**

### A.8 缺陷 7：`containerLayouts` 在 manual 下静默跳过（P1）

```console
$ $CLI execute --ir arch.ir.json --spec '{"layout":"manual","containerLayouts":{"svc":"grid"}}' --json
  applied: ['layout=manual','container.bands','canvas.auto']   ← 没有 containerLayouts
  unmet  : []                                                  ← 也没有报告
```

对照同一 spec 非 manual：

```console
$ ... '{"layout":"layered","containerLayouts":{"svc":"grid"}}'
  applied: ['layout=Layered','container svc relaid as grid',...]
```

确实只在 `if (layout != manual)` 分支内（`DesignSpec.cs:139-160`）。manual 下**既不执行也不报告**。**成立。**

### A.9 缺陷 8：`ObjectMapping` 覆盖不全（P1）

单页（`arch.ir.json`：10 节点 / 3 容器 / 9 边 / 4 文本块）：

```console
mapping n= 13
[('web','web'),('mobile','mobile'),...,('edge','edge'),('svc','svc'),('data','data')]
```

**13 = 10 节点 + 3 容器。9 条边覆盖 0，4 个文本块覆盖 0。**

多页（两页、各 2 节点）：

```console
$ $CLI execute --ir multi.ir.json --output multi.vsdx --json
  status: succeeded
  mapping: {}          ← 整个空对象
  errors : []
$ $CLI export-model → page-0 [('same','A'),('b','B')]   page-1 [('same','A2'),('d','D')]
```

根因（E2，`ExecutionResult.cs:168-188`）：`BuildMapping` 只遍历 `ir.Nodes` / `ir.Containers`
（**单页简写字段**），完全没走 `ir.Pages`；且只取 `diagram.Pages.FirstOrDefault()`。

**成立**，且多页形态比 audit 第二版写的"第二页及以后不映射"更严重：**是整体为空**。
（audit 最新版已由 `4334541` 修正了这一点，我复现一致。）

### A.10 缺陷 9：`min_size` 死代码（P2）

```console
$ $CLI execute --ir arch.ir.json --spec '{"typography":{"baseSize":4,"minReadableSize":9}}' --json
  status succeeded   unmet []
$ export-model → node sizes: [9]        ← 确实夹紧了
```

`DesignSpec.cs:178-188`：

```csharp
var clamped = Math.Max(size, spec.Typography.MinReadableSize);
if (clamped < size)      // ← Math.Max(a,b) >= a 恒成立，此分支永假
```

**确实是从不执行的死代码**，且夹紧行为（4→9）对调用方完全静默。**成立。**

### A.11 缺陷 10：`BaseSize` 抹平层级（P1）

```console
baseSize=9 : widths=[0.9, 2] fontSize=[9]
baseSize=11: widths=[0.9, 2] fontSize=[11]
baseSize=24: widths=[0.9, 2] fontSize=[24]
```

所有节点字号取同一个值，`TypeScale` 的 6 档被整体旁路。**成立。**

**但我要补充一个 audit 没说的更严重事实**：框体宽度三种 baseSize 下**完全不变**（都是 `[0.9, 2]`）。
这说明"抹平层级"与"框体不重算"是同一个根因的两个症状——见 A.12 与 E-5。

### A.12 缺陷 11：文字溢出——现象成立，audit 归因不成立

现象（`check-layout`）：

```
b9 : hard True score 96 {'edge.crossing': 2}
b11: hard True score 92 {'text.overflow-horizontal': 1, 'edge.crossing': 2}
b24: hard True score 76 {'text.overflow-vertical': 10, 'text.overflow-horizontal': 2, 'edge.crossing': 2}
```

`baseSize=24` 时 12 处溢出，`execute` 仍返回 `succeeded`（硬约束判据不含 overflow，
这是另一件事，见 E-3）。

**audit §2.2c 的归因（"字号在布局之后应用，框体不会重算"）是错的。**
真实原因是 **spec 路径根本没有 `DiagramSizer.Apply` 阶段**：

```csharp
// ProtocolRunner.cs:66-88
if (spec is not null) { DesignSpecApplier.Apply(diagram, spec); }   // 只 Recenter/FitPage
else { DiagramSizer.Apply(diagram); DiagramLayoutEngine.Apply(...); ... }
```

```console
$ $CLI execute --ir arch.ir.json --json | jq .stagesCompleted
带 spec : ["ir.validate","ir.toModel","design.apply","route","vsdx.write","quality.check"]
不带 spec: ["ir.validate","ir.toModel","layout.default","route","vsdx.write","quality.check"]
```

同字号对照（带 spec `baseSize=11` vs 不带 spec）：

```
带 spec : widths [0.9, 2.0]
不带 spec: widths [0.75, 1.3]
```

**带 spec 反而让框体退化到 `DiagramIr.cs:297` 写死的 `Bounds(0,0,2.0,0.9)`。** 这是"阶段缺失"，不是"顺序错"。

**plan §12 已把这一点改正**（我核对了 `plan:583-600`，标题即"真正的根因：阶段缺失，不是顺序"），
且 plan §4 第 11 行也写"根因是阶段缺失，不是顺序（M1）"。

**但 audit 未改。** `audit:179-215` 仍以"字号在布局之后应用，框体不会重算"为标题与正文，
`audit:186` 甚至给出"修法：字号必须在 `DiagramSizer.Apply` 之前确定"——
而 `DiagramSizer.Apply` 在 spec 路径上**一次都没被调用过**，这个修法不成立。
这是 §E-2 记录的**跨文档矛盾**，属非阻断（plan 已对），但 audit 仍是 Stage 1 交付物，
引用它的后续阶段会被误导。

### A.13 缺陷 12：`roleColors` 无 `default` 回退（P2）

IR 三节点：`role=service` / `role=databse`（拼错）/ `role=queue`，spec：

```json
{"theme":{"roleColors":{"service":"#FF0000","default":"#00FF00"}}}
```

```console
[('a','#FF0000'), ('b','#FFFFFF'), ('c','#FFFFFF')]     ← b 既没用 default，也没报告
unmet: []
```

**成立。** `default` 键被完全忽略，`databse` 拼错无任何提示，`queue` 未覆盖也不提示。

### A.14 缺陷 13：`mode=fixed` 名存实亡（P0，双向）

```console
$ ... '{"canvas":{"mode":"fixed","width":20,"height":14}}'    # 内容少，应缩小
  applied: ['layout=Layered','container.bands','canvas.fixed+shrunk']   unmet: []
  page size: 11.35 x 9.85          ← 请求 20×14
$ ... '{"canvas":{"mode":"fixed","width":8,"height":6}}'      # 内容多，应撑大
  applied: ['layout=Layered','container.bands','canvas.fixed']          unmet: []
  page size: 11.35 x 9.85          ← 请求 8×6
```

**两个方向都失效，两个方向都静默。** 且注意：8×6 那次的 `applied` 写的是 `canvas.fixed`
（不带 `+shrunk`），因为它走的是 `FitPage` 的 `mustGrow` 分支——**`Shrank=false`，
所以连"曾经偏离请求"这一条内部线索都没有**。**成立**，且比 audit §2.9 描述的更隐蔽。

（前两轮审查在数值上不一致：Stage 3 报 4.00×3.45，复审报 5.95×5.875。我得到 11.35×9.85，
因为用的是 `arch.ir.json`（10 节点）而非 1 节点。**具体数值随内容变化，结论稳定。**
plan §9 的 T4b 验收已用 arch 的 20×14 / 8×6 两个方向，与我的复现口径一致，正确。）

### A.15 缺陷 14：`swimlane` 协议不可达（P1）

IR 声明两个 `kind="lane"` 的容器，各自带上成员：

```console
$ $CLI execute --ir lane2.ir.json --spec '{"layout":"swimlane"}' --json
  applied: ['layout=Swimlane', ...]
$ ... '{"layout":"layered"}'
  applied: ['layout=Layered', ...]

swimlane {'a': (1.85, 3.65), 'b': (4.4, 3.65), 'c': (1.85, 1.23)}
layered  {'a': (1.85, 3.65), 'b': (4.4, 3.65), 'c': (1.85, 1.23)}   ← 逐点相同
```

根因（E2，`DiagramIr.cs:282`）：

```csharp
Kind = container.Kind is "lane" ? "container" : (container.Kind ?? "container"),
```

`lane` 被改写成 `container`，而 `LayoutStrategies.cs:109` 与 `SwimlaneLayoutEngine.cs:21`
都靠 `Kind == "lane"` 识别。**成立。**

我另做了**对照实验证明"保留 lane 后确实不同"**（走 `create` 路径，绕过 IR 转换）：

```console
$ $CLI create --input lane.model.json --layout swimlane   → a(1.85,3.42) b(4.40,3.42) c(1.85,1.00)
$ $CLI create --input lane.model.json --layout layered    → a(1.85,4.88) b(4.40,4.88) c(1.85,2.45)
```

**几何确实不同**，所以 T0b 的修法方向成立、验收可达。

### A.16 缺陷 15：装饰元素独立夹紧致间距塌陷（P1）

`arch.ir.json` 中 title 在 y=11.4、subtitle 在 y=10.8（间距 0.6"）。产出后：

```console
title    {'x': 6, 'y': 9,   'w': 9, 'h': 0.6}
subtitle {'x': 6, 'y': 9.1, 'w': 9, 'h': 0.4}     ← 间距 0.6" → 0.1"
```

`page size` 缩到 11.35×9.85，两者都被压到同一上界附近。**根因确认为
`LayoutStrategies.cs:166-186` 的 `ClampDecorationsToPage` 对每个元素独立 `Clamp`。成立。**

PNG 目视确认：标题与副标题**在渲染图上直接叠印**（`企业分析平台` 与
`Enterprise Analytics Platform` 交错重合）。这是可交付层面的硬伤，不只是数值问题。

### A.17 缺陷 16：IR 容器 `order` 被忽略致流程倒置（P0）

`flow.ir.json` 声明：`intake(order=2) → review(order=1) → fulfil(order=0)`，语义上受理应在最上。

```console
fulfil  y=5.55   ← 最上
review  y=3.43
intake  y=1.30   ← 最下
```

**完全颠倒。** 根因（E2）：

```console
$ grep -rn "\.Order\b" src/ --include=*.cs
（无结果）                                    ← IrContainer.Order 无任何消费者
```

`FlowAndTreeLayoutEngine.cs:93` 用 `OrderBy(g => g.Bounds?.Y ?? 0)`，而容器 `Bounds` 由成员反算，
成员初始 `Bounds` 全为 `(0,0,2.0,0.9)`，故各容器 `Y` 相同，稳定排序保持插入序。**成立。**
（`flow` 走 Flow 引擎是对的，因为 IR 含 `role=decision` 节点；audit 已更正引用路径。）

### A.18 缺陷 17：缺"无法表达/渲染必须报告"机制（P1）

任务书 §3.5 硬要求"无法表达或渲染的样式必须报告"。实测三种"不可渲染"的样式：

**(a) 不存在的字体名**

```console
$ IR textBlock font="NoSuchFont-XYZ"
  status succeeded   unmet []   warnings[('write.text-block','写入了 1 个独立文本块')]
$ $CLI validate --input nf.vsdx --render-smoke --json
  {"valid": true, "errors": [], "warnings": []}
```

**(b) `shadow` / `opacity` / `radius` / `lineJoin`**

```console
$ create model.json（node style: shadow=true, opacity=0.5, radius=0.2, lineJoin="round"）
  compatibility: {lossless: True, summary: "lossless", issues: []}     ← 声称无损
$ export-model → style: {'fill':'#FF0000','fillPattern':'1','stroke':...,'hAlign':...}
```

四个字段**静默消失**，且 `lossless: True`。

**(c) 全库 grep 佐证**

```console
$ grep -rn "Shadow\|Opacity\|Radius\|LineJoin" src/Vsdx.Adapters/*.cs src/Vsdx.Renderer/*.cs
（无输出）
```

`ShapeStyle` 有这四个字段（`DiagramModel.cs:300-303`），**写侧零消费者**，
Schema（`schemas/diagram.schema.json`）允许它们，回读也无痕迹。

**成立，且比 audit 描述更严重**——audit 说"缺机制"，实际是"缺机制 **且** 有虚假的
`lossless: true` 报告"，后者会主动误导调用方。

**另注**：audit 恰用"TextBlock 的 `color:"#FF0000"`"作例子（`audit:415`），
但我实测该颜色**根本没写进正确的 XML 位置**，因此"缺报告机制"只是表层——
底层是"写错了层级"。详见 **E-6b**。

---

## B. 文档一致性核验（含脚本化计数结果）

### B.1 缺陷计数：17 在三处表头一致，但仍有 3 处陈旧数字

脚本化统计：

| 位置 | 数字 | 判定 |
| --- | --- | --- |
| `audit` §10 表体 | 17 行 | ✅ |
| `audit:811` 结句 | "17 项缺陷中，初稿漏了 11 项、误判 3 项" | ✅ |
| `plan:186` §4 标题 | "十七项缺陷" | ✅ |
| `plan` §4 表体 | 17 行 | ✅ |
| `packet:16` | "**17 项缺陷**" | ✅ |
| `packet:17` | "19 节" | ✅（实际 §0–§18 = 19 节） |
| **`packet:27`** | "审计 **16** 项中它独立复验了 11 项" | ❌ **仍写 16** |
| **`packet:140`** | "因此 **16** 项不是\"全部缺陷\"" | ❌ **仍写 16** |
| **`plan:20`**（修订索引 M5 行） | "§4 表补齐 12 行→**现 14 行**" | ❌ **仍写 14**（§4 实为 17 行） |
| **`plan:788`** | "逐项枚举缺陷（12→**14** 项）" | ⚠️ 陈述"审查当时"的中间态，可接受但不清晰 |
| **`plan:791`** | "修完 **14** 项后仍存在" | ⚠️ 同上 |

**5 处陈旧数字**（`packet` 2 处写 16、`plan:20` 写 14、`plan:788/791` 写 14）。
复审（r2）指出过 `packet:27` 与 `packet:140`，**本轮仍未修**。

### B.2 §4 的 17 行 → §9 任务映射：**不是**如文档所述

脚本核验（严格按表格解析）：

```
§4 data rows: 17
§4 defect#->task: [('1','T0'),('2','T1'),('3','T10'),('4','T2'),('5','T3'),('6','T4'),
                   ('7','T5'),('8','T7'),('9','T6'),('10','T1'),('11','T0.5'),
                   ('12','T2b'),('13','T4b'),('14','T0b'),('15','T8a'),('16','T1b'),('17','T2c')]
§9 task rows: 18
§9 tasks: [T0, T0.5, T0b, T4b, T1, T1b, T2, T2b, T2c, T3, T4, T5, T6, T7, T8, T8a, T9, T10]
§4 引用的 task 集合: 16 个
§9 独有: ['T8','T9']
§4 引用但 §9 缺失: []
```

**逐条对照文档自己的说法：**

| 文档位置 | 声称 | 实测 | 判定 |
| --- | --- | --- | --- |
| `plan:216` | "17 项缺陷 → 17 行修法：行数与编号严格对应" | 17 行、编号 1–17 连续 | ✅ **真** |
| `plan:217-219` | "17 项缺陷 → 18 个任务……T8/T9 是跨任务门禁" | §9 18 个、§4 引用 16 个、§9 独有恰为 `{T8,T9}` | ✅ **真** |
| `plan:220-221` | "脚本核验：§4 task refs = 17 个，§9 tasks = 18 个，差集恰为 {T8,T9}" | **§4 refs 是 16 个，不是 17 个** | ❌ **假** |
| `plan:222-226` | "缺陷 2 与缺陷 10 共用 T1，因此实际映射到 16 个不同任务" | 缺陷 2→T1、缺陷 10→T1；去重后 16 | ✅ **真** |
| `plan:228` | "现在是 16 : 16 : 17，且差异有明确解释" | 16 任务 : 17 行 : 18 §9 项 | ✅ **真** |

**结论：核心映射陈述已从上一版的"16⇄16⇄16 一一对应"（假）修正为可核验的
"17 缺陷 : 17 行 : 16 任务 : 18 §9 项，差额由 T8/T9 与 T1 合并解释"——这是真的。**
但同一段里"§4 task refs = 17 个"这个**自证数字是错的（实为 16）**，
与他自己的正确结论（16 个不同任务）矛盾。这是一处**低危害的内部算术错误**：
结论对、支撑数字错。不阻断，必须改，否则下一轮又以它为据。

### B.3 文件改动表（§5）：无重复行，但**遗漏 4 个必须改的文件**

脚本核验重复：

```
§5 rows: 11
duplicates: []
```

**无重复行**（上一轮的"重复行"问题已消除）。11 行逐个：

```
src/Vsdx.Protocol/StyleResolver.cs（新增）
src/Vsdx.Protocol/DesignSpec.cs
src/Vsdx.Protocol/ExecutionResult.cs
src/Vsdx.Engine/LayeredLayoutEngine.cs 等四个引擎
src/Vsdx.Adapters/OfficeImoDocumentWriter.cs
src/Vsdx.Adapters/VisioTextBlockBridge.cs
src/Vsdx.Protocol/DiagramIr.cs
src/Vsdx.Adapters/OfficeImoDocumentReader.cs
schemas/phase7/design-spec.schema.json
skill/SKILL.md
docs/phase8/capability-matrix.md
```

**但按 §9 的任务清单反推，至少 4 个必改文件不在表内**（见 §E-7）：

| 缺失文件 | 为什么必须改 | 依据 |
| --- | --- | --- |
| `src/Vsdx.Protocol/ProtocolRunner.cs` | T0.5 要在 spec 分支插 `DiagramSizer.Apply`；`stagesCompleted` 要加一项 | §12.3 的代码片段就改这个文件 |
| `src/Vsdx.Engine/LayoutStrategies.cs` | T8a 修 `ClampDecorationsToPage`（就在此文件 `:166`） | §4 第 15 行 / T8a |
| `src/Vsdx.Model/TypeScaleDefaults.cs`（新增） | §16.3 明确要新建 | §16.3 代码块 |
| `src/Vsdx.Adapters/VisioBackgroundBridge.cs`（新增） | §18.3 明确要新建 | §18.3 结尾 |
| `src/Vsdx.Protocol/DiagramIr.cs` 内的 `DiagramIrConverter` | T1b 要消费 `Order`（转换器在同文件，但 §5 只提"消费 `IrContainer.Order`"，未提 T1b 落在 `DiagramIrConverter`） | §4 第 16 行 |

**实测佐证**：这 5 个文件/类在源码中确有对应位置（`ProtocolRunner.cs` 存在、
`LayoutStrategies.cs:166` 存在、`TypeScaleDefaults.cs` 与 `VisioBackgroundBridge.cs` 尚不存在）。
§5 是"文件级改动计划"，任务书 §7 Stage 2 明确要求提供它；**漏 4–5 个文件属于该交付物的实质缺口**，
且直接影响实施者排期与影响面判断。

### B.4 跨文档/跨节矛盾清单（与上轮相比的消长）

| # | 矛盾 | 位置 A | 位置 B | 状态 |
| --- | --- | --- | --- | --- |
| 1 | Writer 是否要改 | `plan:289` 说"不触碰 FidelityGuard/协议版本" | `plan:259-262` 与 §18.3 明确要改 Writer/Reader/Bridge | ✅ **已消解**（§5 已按 M4/M10 更正，措辞一致） |
| 2 | manual 是否转置 | `plan:517` 表格："`Layout == manual` → **保留并转置**" | `plan:521-531` 与 `plan:578` 断言 7："manual 不转置，进 unmet" | ❌ **仍矛盾**（见 E-9） |
| 3 | 单位标签是否写入文件 | `plan:201`："Writer 硬传 Inches，**标签未写入文件**" | 实测 `pages.xml` 有 `U="IN"`；`audit:270-296` 已明确更正为"确实写在 pages.xml" | ❌ **仍矛盾**（见 E-1） |
| 4 | 单位标签定性 | `plan:402`："以前**写错标签**"；`plan:411`："现状是**标签写 cm**" | 实测标签恒为 `IN`，且 `audit:290`："危险不在标签与数值不一致" | ❌ **仍矛盾**（三处互否） |
| 5 | `DiagramIr.cs` 是否要改 | `plan:261` 已列"需改动" | — | ✅ **已消解**（上轮 M5/P2 项已修） |
| 6 | `containers[].order` 是否本期实现 | `plan:798` §15.1 残留表列"IrContainer.Order 协议接受，从不执行" | `plan:838-844` 已明确"不能放进该清单" + T1b 本期实现 | ⚠️ **部分消解**：闸门 3 的清单已排除它，但 §15.1 的"审查实测残留"表仍把它列为残留，读者会误以为本期不做 |
| 7 | 破坏性变更数量 | `plan:405`："**两处**破坏性变更" | `packet:96-108`："**三处**（此前本文件漏了第三项）" | ❌ **仍矛盾**（见 E-8） |
| 8 | `TextScale` 类名 | `plan:924` 同句自我更正"不是 `TextScale`" | 无 | ✅ **已消解**（实测 `grep TextScale src/` 为空，类名 `TypeScale` 正确） |
| 9 | 计划节数 | `packet:17`："19 节" | 实际 §0–§18 = 19 | ✅ **已消解** |
| 10 | 缺陷数 16 vs 17 | `audit/plan` 表头 17 | `packet:27`、`packet:140` 仍写 16 | ❌ **未修**（见 B.1） |

**10 项中：4 项已消解、1 项部分消解、5 项仍在（其中 1 项纯计数、4 项实质性）。**

**必须点明：上一轮点名的 7 条矛盾里，"Writer 是否要改""DiagramIr 是否要改""TextScale 类名"3 条
确实已改好，这是真进步。但"manual 是否转置"与"单位标签"两条从上一轮至今一字未动。**

### B.5 audit 自身的计数前后不一（本轮新发现）

audit §1 字段矩阵的统计与表格内容不一致：

```
脚本统计（按行首标记）:
  ✅ = 8   ⚠️ = 4   ❌ = 6    total = 18
```

- `audit:45` 声称 **"7 项已实现，4 项部分，6 项未执行"**（合计 17）。表格实际是 **8 ✅ / 4 ⚠️ / 6 ❌**（合计 18）。
  合计差 1 是因为 `Canvas.Mode=auto` 与 `Canvas.Mode=fixed` 分列两行（17 个字段、18 行）；
  **但"7 项已实现"确实与表格的 8 项不符**——表格里 `Canvas.ShrinkToFit`、`HardConstraints`、
  `SoftConstraints`、`Spacing.*` 等 8 行标 ✅。
- `audit:671` 声称 **"3 项完全不执行、3 项部分执行、2 项只覆盖一类对象"**（合计 8）。
  表格实际是 **6 项 ❌、4 项 ⚠️**（合计 10）。**这组数字与表格差 2，且分类口径也对不上**
  （⚠️ 的 4 行恰好是"2 项仅覆盖 Node + 2 项部分"，所以正确表述应为"6 / 2 / 2"）。
- `audit:679` 声称 **"解决 §2 的 7 个缺陷"**。`audit` §2 有 **16 个**缺陷小节（2.1–2.13，编号跳号），加 §9 的 `Direction` 共 **17 项缺陷**。

**三个数字（7 / 3+3+2 / 7）在同一份文档内互不相容，且都与自己的表格不符。**
准确值是：**18 行、8 ✅ / 4 ⚠️ / 6 ❌；§7 应写"6 项完全不执行、2 项部分、2 项仅覆盖一类对象"。**

**两轮前审都没发现这一处**（`grep "7 项已实现"` 在 `independent-review.md` / `rereview.md` 中零命中）。

---

## C. 两项决策的归因是否成立

### C.1 D-8（`manual` 下 `direction`）：**归因成立，但文档自相矛盾使其无法执行**

**归因核验。** `phase8.2-owner-rulings.md:14-24` 引用的负责人原文：

> Manual 模式必须保留用户坐标，不得擅自转置。

我逐字核对了三件事：

1. **任务书里没有这句。** `grep -n "Manual\|保留用户坐标\|转置"` 在
   `goal-20261126-2034.md` 中**零命中**。任务书通篇没有 `manual` 的语义规定。
2. **这句只存在于本项目自己的文档里。** `grep -rn "Manual 模式必须保留用户坐标" docs/` 命中 5 处，
   全部在 `owner-rulings.md` / `plan.md` / `open-decisions.md`，即**实施者自己的转述**。
3. **最早出现是 `7636486`**，形态是 `plan:351`："任务要求\"Manual 模式必须保留用户坐标\""——
   当时它被表述为"任务要求"（任务书里没有），后来在 `dd8992a` 被升级为"负责人原文"。

**这是我无法从仓库内证实的唯一一处**：负责人的 D1 原文我拿不到，
只能看到实施者的转述。**我按证据规则标记为"未验证来源，但内容自洽"**：
`layout=manual` 保留作者坐标在语义上完全合理（`ManualLayoutEngine` 的职责就是"什么都不做"），
且**实测确认当前行为就是这样**：

```console
$ $CLI execute --ir mananch.ir.json --spec '{"layout":"manual","canvas":{"mode":"fixed","width":10,"height":8,"shrinkToFit":false}}'
$ export-model → nodes [('a',2,6), ('b',6,3)]；text [('title',4,1)]
```

**坐标逐一原样保留**（IR 里就是 2,6 / 6,3 / 4,1）。所以 D-8 方案 A 与既有行为一致。

**因此：D-8 不是"合理化"，是正确的。** 但**plan 自己没有执行这个裁决**：

- `plan:517`（§11.2 表格）："`Layout == manual` → **保留并转置**"
- `plan:521-523`（同一节紧接）："`layout=manual` 时 `direction` **不生效**"
- `plan:486`（§11.1 表格）："按 D1 裁定，`manual` 下不转置"
- `plan:578`（§11.6 断言 7）："Manual 模式不转置"
- `plan:531`："`DesignSpec.cs:163` 的 manual 分支保持不转置"

**同一节内 2 票"转置"、4 票"不转置"，且 §11.6 断言 4（"坐标不变 + unmet"）与断言 7
（"不转置 + unmet"）虽然现在一致了，但 §11.2 的表格仍与它们对立。**
实施者若照 §11.2 的表格实现 `Waypoints` 转置，就会违反 D-8。

**判定：归因正确；执行指令矛盾（E-9），这是阻断项。**

### C.2 D-4（TextBlock 字体回读）：**归因成立；但"补一处注册"这个技术判断不成立**

**归因核验。** 实施者引用任务书 §3.5 与 §4.4：

```console
$ grep -n "Reader 能正确读回" goal-*.md
281:> - **Reader 能正确读回已支持的样式；**
$ grep -n "不为了视觉系统" goal-*.md
297:> 4. 不为了视觉系统**重写**现有 VSDX Writer / Reader。
```

**两条引用逐字准确**（§3.5 在第 281 行、§4.4 在第 297 行，与文档标注的"281 行"一致）。
任务书同时要求"无法表达或渲染的样式必须报告"（第 283 行）：

```console
$ sed -n '281,284p' goal-*.md
> - **Reader 能正确读回已支持的样式；**
> - 保存后的 VSDX 再次渲染，样式没有丢失；
> - **无法表达或渲染的样式必须报告。**
```

**所以：修 `VisioTextBlockBridge` 让字体可回读，既满足 §3.5 第一条，也避免每次都触发第三条的
"必须报告"负担。归因链（§3.5 要求可读回 + §4.4 只禁"重写"不禁"补一处注册"）成立。**

**但"补一处注册 = 小改动"这个技术判断是错的。** 我独立核实了桥的实现：

```csharp
// VisioTextBlockBridge.cs:141
if (style?.Font is not null) AddCell(shape, "Font", style.Font);
```

它写的是 **shape 级裸 `<Cell N="Font" V="Verdana" />`**，而库对 Node 走的是
`shape.SetFont(font)` → `VisioTextStyle.FontFamily` → 保存时注册 `FaceNames`。两条路径：

```console
$ # Node 路径（fontFamily=Courier New）
$ unzip -p typo.vsdx visio/document.xml | grep -o '<FaceName[^>]*>'
<FaceName NameU="Courier New" .../><FaceName ID="0" Name="Courier New" .../>...
$ # TextBlock 路径（font=Verdana）
$ unzip -p tb.vsdx visio/document.xml | grep -c '<FaceName'
0                                                    ← 零注册
```

**这不是"少写一行注册"，而是"两套字体机制并存"**：Node 走库的 `TextStyle`（自动注册 FaceNames），
TextBlock 走桥的裸 Cell（绕过注册）。**要修，必须把 TextBlock 从"裸 Cell"迁到"库的 TextStyle"，
即重写 `BuildShape` 的字体与 Character 段生成逻辑**——这正是 plan §5 说的"中"风险，
也是 D-4 裁决里"推荐 A1"的理由。

**判定：归因正确；实施规模被 plan 低估的风险已被 `owner-rulings.md` 的"D-4 工时：中"承接，
但 `plan:263` 的风险评级"中"与 §5 的"中"一致，所以这一点是自洽的。** 不阻断。

**但有一条 plan 没意识到（见 E-10）**：即使字形注册修好了，**Renderer 仍然不消费它**。
我用逐像素比对证明：

```console
$ 同一 IR，textBlock font = "Courier New" / "Arial" / "NoSuchFont-XYZ"
  fr-Courier_New.png   01f4839f991799c2
  fr-Arial.png         01f4839f991799c2
  fr-NoSuchFont-XYZ.png 01f4839f991799c2        ← 三张 PNG 逐字节相同
$ 同一 IR，node font = "Courier New" / "Arial" / "Georgia"
  nfr-Courier_New.png 09a7d17a02feca8f
  nfr-Arial.png       09a7d17a02feca8f
  nfr-Georgia.png     09a7d17a02feca8f          ← 三张 PNG 逐字节相同
```

对照（同一测试里字号/粗体/斜体是**有效**的）：

```console
base (font=Arial,14) 5d532ee94385f275
family2 (font=Courier New,14) 5d532ee94385f275   ← 换字体：无变化
size2 (font=Arial,22) 8dfa82665b7cb06a          ← 换字号：有变化
plain   89ea96c076bad58c
bold    4447a2ca74eb9135                          ← 粗体：有变化
italic  188affd1b4928a30                          ← 斜体：有变化
```

**结论：字体族（FontFamily）对渲染输出零影响，字号/粗体/斜体有影响。**
`FontResolver` 只按"是否需要 CJK"二选一挑一个字体文件（`FontResolver.cs:52-70`），
**从不读 VSDX 里的 `FontFamily`**。所以任务书 §3.5 的"Renderer 不得通过另一套独立规则制造
与 VSDX 不一致的预览"这一条，在**字体维度上当前是不满足的**，而 D-4 的整条推理
默认"修好回读 = 用户能看到效果"。

**这不是 D-4 归因错，是 D-4 的收益被高估**：修好回读后，`export-model` 能读到字体，
但**渲染预览仍不变**，用户依然看不出差别。plan 需要补一条"Renderer 字体支持"的任务或
在 T2c 的白名单里把 `font-family` 标为"可写入、可回读、**不可渲染**"。

---

## D. 五维度评审

### D.1 架构：职责是否清晰，有无不必要重构

**结论：⚠️ 有条件通过。这是本计划最强的一维。**

**做得对的（可核验，非类名堆砌）：**

1. **`StyleResolver` 的立项依据是四个可复现缺陷**（`plan:79-88`）：新增 palette 只能是第 5 处 if、
   优先级取决于代码顺序、Typography 只处理 Node、`min_size` 报告只在 Node 分支且是死代码。
   我逐一验证了这四条（A.3/A.5/A.10/§2 的四处 if 结构），**全部成立**。这不是"为抽象而抽象"。
2. **依赖方向处理正确**（§16）：Adapters 不能引用 Engine，因此把字号常量下沉到 `Vsdx.Model`。
   我核对了 `.csproj`：`Vsdx.Adapters` 只引用 `Vsdx.Model`，`Vsdx.Engine` 引用 Model + Adapters。
   **`Engine → Model ← Adapters` 的菱形成立，方案可行。**
3. **`Transpose` 提取到 `DiagramLayout`**（§11.5）避免两份实现漂移——现有
   `CandidateComparison.cs:141` 的实现确实只处理 node/group/page 三类，
   提取后 `CandidateComparison` 改为调用，是正确的去重而非重构。
4. **§18.3 的事实核查是真的**（我实测过）：`grep -rl "pages.xml" src/Vsdx.Adapters/*.cs` 无结果；
   `page1.xml` 无 `PageSheet`，`pages.xml` 有。**"顺序风险不存在"的推论正确。**

**有问题的：**

1. **`StyleResolver` 放在 `Vsdx.Protocol` 而不是 `Vsdx.Engine`。** `DesignSpec` 是协议类型，
   但"把 spec 解析成模型样式"是**执行**职责。当前 `DesignSpecApplier` 就在 Protocol 里
   （`DesignSpec.cs:112-300`），所以"不动位置"是保守选择，**可以接受**；
   但 plan 没说明为什么不借此机会把它移到 Engine。**不阻断，但应在 §1 里写一句"位置沿用现状，理由是 X"。**
2. **`ApplyStyleOnly` / `ApplyLayoutAndCanvas` 拆分未说明对 `appliedDesignNotes` 顺序的影响。**
   复审（r2 §2.2）已指出：拆分后 `typography` 会跑到 `applied` 最前，若有测试断言顺序会破。
   **plan 至今未提。** 属实施细节，非阻断，但会让实施者踩坑。
3. **T1 合并了 4 个正交职责**：palette 定义 + 三层优先级解析 + BaseSize 缩放 + TypeScale 映射。
   §9 的验收只有"单元测试证明三层优先级"，**无法证伪 BaseSize 缩放是否正确**。
   §13.4 有独立验收（5 条），但那是 §13 不是 T1。**建议 T1 拆 T1（resolver）+ T1c（BaseSize 缩放）。**

### D.2 图形：是否真正支持多种视觉表达，还是滑向固定模板

**结论：⚠️ 偏窄但方向正确；有一个真实的结构性风险被低估。**

**做得对的：**

1. **Palette 结构里没有布局/形状/字号字段**（`plan:145-155`），只有 10 个颜色 token。
   这直接满足任务书 §3.2-7"不得把 Palette 写成完整、固定的图形模板"。
2. **`plan:151` 明确写 "palette 只给颜色，不决定布局、形状或字号"**，且 §3.2 列了 4 条硬约束。
3. 4 套 palette（enterprise/office/technical/muted）的定位描述（沉稳/明亮/深色/低对比）
   对应真实用途，不是 4 个随便的颜色集。

**风险（plan 未识别）：**

1. **`ContainerFill` / `ContainerStroke` / `ContainerText` 是单值，不是按序数组。**
   当前 5 组硬编码色的**真实价值**是"相邻容器不同色，便于区分层次"（`DesignSpec.cs:202-204`）。
   换成单值 palette 后，**三个容器会变成同一色**——这正好**复现了缺陷 5 想修的那个症状**
   （"三个容器同一种浅蓝，看不出层次"，见 `plan:365` 表格）。

   **让我把这条说清楚**：缺陷 5 的症状有两个层面——
   (a) 容器色不来自 Theme（palette 换了它不变）；
   (b) **同层三个容器彼此不同色**（这是当前硬编码数组提供的）。

   plan 的 T3 只修 (a)（"移除 `bandFills` 数组，改由 palette 提供"），
   **会把 (b) 一起移除**。而 `plan:365` 把"三个容器同一种浅蓝，看不出层次"列为**要修的缺陷**，
   修完反而更同色。**这是自相矛盾的：修 T3 会让基线观察里的第 1 条恶化。**
   → 需要 palette 提供 `ContainerFillSequence` 或由 resolver 按容器序做明度/色相偏移。见 E-11。

2. **"多种视觉表达"目前只有 4 套调色板 + 字号缩放两个旋钮。**
   任务书 §3.3 要求 8 个文本角色层级、§3.4 要求角色样式覆盖真实图形对象。
   当前 plan 的 §2.3 映射表把 8 个角色映到 6 个 TypeScale 档位（title/subtitle/region/node/caption/
   edge/annotation），**其中 caption 与 note 都映到 EdgeLabel=9，subtitle 与区域标题都映到 12**。
   这不是不行（任务书说"可以合理复用"），但 plan §2.3 自己写了一行
   "**caption 在 Bridge 是 9、在 TypeScale 是 NodeCaption=10 —— 需统一**"，
   **却没给统一的答案**（§16 只做常量下沉，没定 caption 到底是 9 还是 10）。
   一个未定的数值在 §2.3 与 §16 之间悬着。→ E-12。

3. **`EdgeStroke` / `EdgeLabelText` 是 palette 里的单值**，但 IR 有 8 种 `relation`
   （calls/reads/writes/publishes/consumes/depends/contains/flows，`DiagramIr.cs:174-180`），
   当前只用来决定箭头有无（`DiagramIr.RelationUsesArrow`）。plan 没有任何"按 relation 分色/分线型"的
   设计，**也没说这是本阶段不做**。按任务书 §2.1 的硬要求，这属于"协议接受、执行忽略、结果不报告"
   的第四种情况——它不在 17 项缺陷里。→ E-13。

### D.3 协议工程：是否还有"接受但不执行、不报告"

**结论：❌ 不通过。这是本计划最弱的一维，也是我给出 FAIL 的主因之一。**

plan §15 提出了三条"机制性闸门"（未知成员检测 / 枚举白名单 / 未消费字段登记）。方向正确，
但**三条都留着同一个洞**：闸门只覆盖"**它枚举到的**"字段。

**实测：闸门 1、2、3 全部没有覆盖的现网残留（我逐条跑过）：**

```console
# (1) 三条闸门理应覆盖的输入 —— 当前全部静默
$ spec = {"typografy":{"baseSize":14},"specVersion":"99.0","layout":"kaboom"}
  status: succeeded   unmet: []   applied: ['layout=Layered','container.bands','canvas.auto']
  warnings: ['write.text-block']
```

- `typografy` 拼错 → 静默（闸门 1 正是为它设计的，但当前尚未落地）
- `specVersion: "99.0"` → 静默执行（闸门 2 的枚举白名单**不覆盖版本号**，§15.4 单列了它，但没有机制）
- `layout: "kaboom"` → 静默回退 Layered（闸门 2 覆盖了它，但**只是计划，不是现状**）

这三条是 plan §15.4 的三条验收，**当前全红**——这没问题（本期要修）。问题是：

```console
# (2) 闸门 2 明确列了要校验的字段：layout/direction/palette/role/kind/canvas.mode/unit/align/row.type
#     但下面这些同样是"协议接受、不执行、不报告"，且不在任何闸门的白名单里：
$ spec = {"layout":"layered","containerLayouts":{"svc":"kaboom"}}
  applied: ['container svc relaid as kaboom']    ← 报告"已按 kaboom 重排"
  unmet: []
```

**`containerLayouts` 的 value 是枚举，`Parse("kaboom")` 回退 Layered 且不报告**——
`plan:829` 的枚举白名单里**没有 `containerLayouts.*`**。这是同一类缺陷的漏网。

```console
$ ir = node with "fill":"#00FF00" 顶层字段（IR schema 没有 style，但 JSON 能进去）
  → 节点保持 #FFFFFF，unmet []，无警告
```

IR 的 `additionalProperties: false` 只在 **Schema 文件**里，`execute` 根本不加载 Schema（见 E-4）。

**我另实测的三条未登记残留：**

| 残留 | 实测 | 在哪一层被漏掉 |
| --- | --- | --- |
| `IrAnchor.Mode = "relative"` | 接受，**等同 fixed**（`anchor:{x:7,y:5,mode:"relative"}` → 原位 7,5 未被"相对化"） | ① plan §15.1 列了它，但 §15.2 三条闸门**都不管**它（不是未知成员、不是枚举白名单成员、闸门 3 清单只有 `metadata.*`） |
| `Edge.FromPort`/`ToPort` | **接受、路由用了、但回读为 `None`**（`export-model` 后 `fromPort: null`） | 不在任何闸门；且 plan §11.6 断言 6b 要"转置时重映射端口"，**前提是端口能回读，而它不能** |
| `ConstraintSpec.Target` | 接受、**原样回显**进 `unmet.target`，但**从不用于作用域限定**（`max_page_width` 仍检查全页） | plan §15.1 列了它，但三条闸门不管它（不是未知成员/枚举/`metadata.*`） |

**判定：** plan §15 的三条闸门**方向对、覆盖不足**。它把"机制性"理解成
"对已知字段做枚举校验"，而不是"对**所有**协议字段建立执行→报告的双向契约"。
按现在的设计，实施完 17 项 + 三条闸门后，**仍会有至少 4 类"接受但不执行不报告"**：
`containerLayouts.*` 的非法值、`anchor.mode`、`FromPort`/`ToPort` 的回读、`ConstraintSpec.Target`。

**这一维不通过，但**我要明确说：plan 已经意识到"逐项枚举是错的"（§15.1 开头），
只是修法没跟上。**建设性建议见 §F.3。**

### D.4 VSDX 保真：是否可能破坏现有对象或样式

**结论：⚠️ 风险部分受控，但有两处**会**破坏，且 plan 未识别。**

**做得对的：**

1. **§18 的路径选择正确**（背景走 `PageSheet` 而非背景 shape），理由（背景进 `ShapeOrder` 会与
   z-order/nesting 桥竞争）成立，且我实测确认当前**没有任何 Adapters 代码触碰 `pages.xml`**，
   所以新增 `VisioBackgroundBridge` 确实无竞争者。
2. **§18.4 断言 2** 用 `mixed-fidelity.vsdx` 做逐字段往返对照，是**正确的回归闸门**
   （该文件存在，我 `ls examples/phase8/` 确认）。
3. **§18.4 断言 3**"背景写入后 `ShapeOrder` 长度不变"能证伪"是否偷偷加了 shape"。好断言。

**风险（plan 未识别或未定）：**

1. **`create` 与 `update` 两条路径的单位行为相反，而 T4 只改一处会加剧不一致。**
   我实测：

   ```console
   $ # create：模型 unit=cm，page.Size=10×8
   $ unzip -p upd1.vsdx visio/pages/pages.xml | grep PageWidth
   PageWidth" V="10" U="IN"                       ← create 不换算
   $ # 紧接着 update（模型仍是 unit=cm）
   $ $CLI update --input upd1.vsdx --model upd.model.json --output upd2.vsdx
   changes: [{'kind':'page.resized','before':'10.00x8.00','after':'3.94x3.15'}]
   PageWidth" V="3.937007874015748" U="IN"        ← update 换算
   ```

   **`create` 后再 `update` 会主动把页面从 10" 改成 3.94"**，且报告为正常 resize。
   plan §17.2 把契约定为"`DesignSpecApplier` 把 spec 单位规范化为 inches 后写入 `page.Size`"，
   这**只覆盖 `execute` 路径**，不覆盖 `create`（`create` 不经 `DesignSpecApplier`）。
   **T4 的两处改动（Writer + DesignSpecApplier）如果都做了，`create` 会换算、
   `DesignSpecApplier` 也会规范化——对 `execute` 而言是"规范化一次、Writer 再乘 factor(1)"，
   正确；但对 `create` 而言是"模型 unit=cm → Writer 换算"，也正确。**
   所以 T4 的两处改动**在逻辑上是对的**。但 plan §7 的兼容性表只说"行为改变"，**没说要处理
   已存在 VSDX 的 `update`**：老模型的 `unit` 字段是 `"cm"`，新代码 `create` 后会写 `U="IN"`，
   再 `update` 会二次换算。**典型的存量兼容坑，plan 未提。**

2. **`Theme.Background` 修复会引入真正的保真风险，而 plan 的"无竞争者"论证有缺口。**
   §18.3 的论证是"没有桥触碰 `pages.xml`，所以无顺序风险"。我核对了
   `grep -rl "pages.xml" src/Vsdx.Adapters/*.cs` **无结果**——论证成立。
   但 `OfficeImoDocumentWriter.cs:44-52` 自己就在 `doc.AddPage(...)` 里写 `pages.xml` 的
   `<Page>` 元素（含 `ViewScale`/`ViewCenterX/Y`）。新增 `<PageSheet><Cell N="PageBackground">`
   是**在同一个 part 上加子元素**，不是重写整份，所以安全——**但 plan 没说明这是"原 part 上追加"
   还是"新桥整份重写"**，两种做法对 `<Page>` 属性（ViewScale 等）的影响不同。
   若按现有桥的通用模式（读入→改内存→整份写回）实现，**必须保证 `<Page>` 的属性不被序列化丢失**。
   → 建议：`VisioBackgroundBridge` 用 `XDocument` 增量修改，不整份重建。**plan 应写明。**

3. **`Kind` 不回读（复审 r2 已指出，plan 仍未写）。**
   我实测：`create` 一个 `Kind="lane"` 的模型 → `export-model` → `kind: "container"`。
   T0b 修好 `DiagramIr.cs:282` 后，`applied` 与几何能对，但**任何"回读 kind 验证"的测试都会失败**。
   plan §9 的 T0b 验收恰好用几何判据避开了这个坑，**但没写"`Kind` 不回读"这个事实**，
   实施者顺手加断言就会踩空。

### D.5 测试工程：验收能否证明真实能力

**结论：⚠️ 有条件通过（比上一轮明显改善，但仍有两处无法证伪）。**

**做得对的：**

1. **§6.4 用真 IR（`arch/flow/overview.ir.json`），并加了"输入正确性闸门"**
   （断言 1：PNG 标签与 IR 源一致）。我实测确认这三个文件是 `DiagramIr` 格式
   （顶层有 `irVersion`，无 `modelVersion`），且标签完整回读：

   ```console
   $ # arch.ir.json → 节点 'Web 门户\nReact SPA' 完整保留
   "web": "Web 门户\nReact SPA"     ✅
   ```

   这条闸门是真需求——上一轮用 `benchmarks/*.json`（`Diagram` 模型格式）当 IR，
   会静默丢标签。

2. **§6.4 断言 4**（`text.overflow-*` 计数为零）是**可证伪的**，且当前是红的
   （`baseSize=24` 时 12 处）。正确。
3. **§6.1 的 `NullVsEmptyStringDistinguished`**（`fill=null` 走继承、`fill=""` 视为显式空）
   是**真正困难且必要的**断言。当前代码无法区分（`ShapeStyler.Apply` 用 `ColorUtil.Normalize`，
   空串会归一成 null）。**这是好测试。**
4. **§12.4 的对照式断言**（带 spec vs 不带 spec 的框体宽必须一致）是唯一能区分
   "装回阶段"与"把 2.0 改成 5.0"的断言。**方法论正确。**

**有问题的（两处无法证伪 + 一处已过时）：**

1. **§6.3 `UnitConversionIsCorrect` 的断言"标签与数值一致"在当前代码下恒真。**
   我实测：`pages.xml` 的 `U="IN"` 恒真（Writer 硬传 Inches），而 `page.Size.Unit` 字段
   **回读后总是 `"inches"`**（见 A.7）。所以"标签与数值一致"这个断言**无论实现对错都通过**——
   它测的是 Writer 的常量，不是代码是否正确。→ **应改为断言具体数值**：
   `10cm` → `page.Size.Width == 3.937 && page.Model.Unit == "inches"`。

2. **§6.2 `ContainerColorsComeFromPalette`（"换 palette 后容器色改变"）在 D.2 的风险下会"通过但不够"。**
   它只断言"改变"，不断言"三个容器彼此不同"。若 T3 把 5 色数组换成单值，
   测试通过，但**层次感恶化**（D.2 风险 1）。→ 断言需加"同页多容器至少 2 个不同色"。

3. **§12.4 断言 1 与 5 在"页面尺寸不同"时必然失败。** 复审 r2 §2.2 已算出
   `TypeScale.ForCanvas(11, 8.5, 3)` 的 `NodeTitle = 10.725`，而我独立复算了另外两组：

   ```
   default canvas 11×8.5, 10 nodes      NodeTitle = 8.919  (sizeFactor 触底 0.75)
   canvas.fixed 20×14,  10 nodes        NodeTitle = 14.000 (sizeFactor 1.221, density 触顶 1.30)
   after auto-fit 11.35×9.85, 10 nodes  NodeTitle = 9.771
   ```

   即：**带 spec 且声明 `canvas.fixed 20×14` 时，`DiagramSizer` 会按 20×14 推 TypeScale
   （NodeTitle=14），而不带 spec 走默认 11×8.5 得 8.919。两者框体必然不等。**
   plan T0.5 与 §12.4 断言 1/5 都写"同字号下必须一致"——**这句话在页面尺寸不同时是错的**。
   `plan:433` 的 T0.5 验收同样没加限定。→ 必须加"同一画布尺寸"前置条件。见 E-14。

4. **§9 的 T9 验收写"373 测试 + 18 基准 + 0 warning"**，基准目录有 26 个 JSON
   （`find benchmarks -name "*.json" | wc -l` = 26），"18 基准硬约束"这个数字来源未说明。
   可接受（任务书 §5.5 也写 18），但无法从仓库直接核验 → 记为局限。

---

## E. 新发现的问题

编号 E-1…E-15，每条给严重度与一手证据。**P0 会阻断，P1 应在开工前定，P2 可实施中闭环。**

### E-1 【P1】单位"标签"的定性在 plan 内三处互否，且与实测相反

**证据：**

```console
$ grep -n "标签" docs/phase8/phase8.2-plan.md | grep -i "unit\|cm\|IN"
201: ... （**注意：Writer 硬传 Inches，标签未写入文件**，不存在"标签与数值不一致"）
402: | `Canvas.Unit` | **行为改变**：以前写错标签，现在换算正确。
411: **`Canvas.Unit`**： 现状是"标签写 cm、数值仍按英寸"，
327: | `UnitConversionIsCorrect` | cm→英寸数值正确，标签与数值一致 |
```

实测（`pages.xml`）：

```
PageWidth" V="10" U="IN"     ← 标签是 IN，不是"未写入"，也不是"写 cm"
```

`audit:270-296` **已经给出正确表述**（"`U="IN"` 确实写进了文件，只是在 `pages.xml`"），
但 plan 的三处（:201 / :402 / :411）**三种互不相同的错误表述**仍然存在。

**危害：** 实施者按 :411 的"标签写 cm"去修，会去查 `page.Size.Unit` 为何没写进 XML；
按 :201 的"标签未写入文件"去修，会去加 `U=` 属性。**两者都是错的方向**，真问题是
"数值没乘 UnitFactor"。

### E-2 【P1】audit §2.2c 的根因与 plan §12 直接对立，audit 未同步

**证据：**

| 文档 | 说法 |
| --- | --- |
| `audit:179` 标题 | "字号在**布局之后**应用，框体不会重算" |
| `audit:204` 修法 | "字号必须在 `DiagramSizer.Apply` **之前**确定" |
| `plan:585` 标题 | "真正的根因：**阶段缺失，不是顺序**" |
| `plan:600` | "**这不是"调整顺序"，是"把缺的环节装回去"**" |

实测支持 plan：`stagesCompleted` 在 spec 路径下没有尺寸推导阶段。

**危害：** 任务书 §7 Stage 4 要求把 audit + plan + 审查 + 修改后计划一并提交负责人。
两份文档对同一 P1 缺陷给出**互相否定的根因**，负责人无法判断以哪份为准。
**修法：** audit §2.2c 必须加一段"根因更正：见 plan §12"，或直接改写标题与修法段
（stage 1 交付物出错就该改，不必"保留历史"——这是仓库文档不是论文）。

### E-3 【P1】文字裁切不计入硬约束：`hard=True` 与 `score=92` 无法证明任务书 §5.3 的验收

> **本条我在初稿里写错了一次，此处是更正后的版本，并附更正原因。**
> 初稿我写"同一个 VSDX，`execute` 报 hard=True，`check-layout` 报 hard=False"——
> 那是我把 `l1.vsdx`（容器重排后 `group.overlap` 为 error）的结果错记到 `b24.vsdx` 上。
> 复测后两者**一致**（都 hard=True）。以下是我复测后的准确结论。

**实测（`b24.vsdx`，`baseSize=24`，12 处文字溢出）：**

```console
$ $CLI check-layout --input b24.vsdx --json
  hardConstraintsPassed: true   score: 76
  counts: {errors: 0, warnings: 14}
  text issues severity: {('text.overflow-horizontal','warning'), ('text.overflow-vertical','warning')}
$ $CLI execute --ir arch.ir.json --spec '{"typography":{"baseSize":24}}' --json
  status: succeeded
  quality: {hardConstraintsPassed: true, hardViolations: 0, softViolations: 14,
            score: 76, issueCodes: ['edge.crossing','text.overflow-horizontal','text.overflow-vertical']}
```

**两条命令一致，问题不在"不一致"，而在"两者都把文字裁切定为 warning"：**

| 溢出数 | `hard` | `score` | `status` |
| --- | --- | --- | --- |
| 0（`b9`） | True | 96 | succeeded |
| 1（`b11`） | True | 92 | succeeded |
| 12（`b24`） | **True** | 76 | **succeeded** |

**危害：**

1. 任务书 §5.3 的验收清单里有 **"不发生文本裁切"**（`goal-*.md:348`）。
   当前它**不是硬约束**，所以 `hardConstraintsPassed=true` 这个信号**不能证明**该条验收通过。
2. plan §6.4 的"修前基线"表（`plan:365-371`）给三份产物标了 `hard=True, score 92 / 100 / 92`。
   但同一个 `arch.ir.json` 的产出，**标题与副标题在渲染图上直接叠印**（我在 A.16 用 PNG 确认），
   `score` 仍是 92。**`score` 不是视觉正确性的代理指标**，plan §8 的 R5 也承认这一点，
   但 §6.4 仍然把分数摆在表里，读者会误以为 92 分代表"基本没问题"。
3. 因此 plan T0.5 的验收"`text.overflow-*` 为 0"虽然写法正确，但**没有闸门强制它**——
   实施者可以让它变成 1 而 `execute` 依然 `succeeded`。
   §6.4 断言 4（"`text.overflow-*` 计数为零"）必须**显式用 `check-layout` 的 counts 判定**，
   不能依赖 `execute` 的 `status`。

**建议：** 在 T2c（或新增 T2d）里把"`text.overflow-*` 计数 > 0 → `unmetConstraints` 加
`style.text-overflow`（isHard=true）"作为验收的一部分；否则 §5.3 的"不发生文本裁切"
在本阶段结束时仍不可证。

### E-4 【P1】`execute` 从不加载 JSON Schema，`additionalProperties: false` 形同虚设

**证据：**

```console
$ grep -rn "JsonSchema\|ValidateSpec\|schema" src/Vsdx.Protocol/*.cs src/Vsdx.Cli/*.cs
（无输出）
$ sed -n '712,731p' src/Vsdx.Cli/Program.cs
var irOptions = new JsonSerializerOptions { PropertyNamingPolicy = CamelCase,
                                            PropertyNameCaseInsensitive = true, ... };
var ir = JsonSerializer.Deserialize<DiagramIr>(File.ReadAllText(irPath), irOptions)
```

`schemas/phase7/diagram-ir.schema.json` 与 `design-spec.schema.json` **全都有
`additionalProperties: false`**（我逐个确认过），但**运行时没有任何 Schema 加载器**。
`System.Text.Json` 默认 `UnmappedMemberHandling.Skip`，未知字段静默丢弃。

**危害：** plan §15.2 的闸门 1 说"用 `JsonDocument` 预扫未知键"。这**是在运行时重造一遍
Schema 已经表达过的约束**。更好或至少更该讨论的方案是"加载 schema 用 `JsonSchema.Net` 校验"，
但任务书 §4"不新增依赖"要评估。**plan 选了预扫，可以，但必须写明"为什么不用已有 schema"**
——否则读者会以为 schema 生效，而那正是 audit §2.1 批评的"三方失守"里的第一方。

### E-5 【P2】`DiagramSizer.Apply` 内部的 `DiagramLayout.Apply(diagram)` 会让 T0.5 重排两次

**证据：**

```csharp
// DiagramSizer.cs:111-112（Apply 的末尾，每个 page 循环内）
// ---- 2. Containers from members ----------------------------------
DiagramLayout.Apply(diagram);
```

而 T0.5 的新流水线（`plan:610-616`）是：

```csharp
var application = DesignSpecApplier.ApplyStyleOnly(diagram, spec);
DiagramSizer.Apply(diagram);                            // 内部调 DiagramLayout.Apply
DiagramLayoutEngine.Apply(diagram, options, strategy);  // 布局
```

`DiagramLayout.Apply` 会按成员**反算容器包围盒**。在 `ApplyStyleOnly` 之后、
`DiagramLayoutEngine.Apply` 之前，成员位置还是 IR 的 `(0,0,...)`（若 `anchor` 缺失），
所以容器包围盒会先塌成一点，再被布局引擎重算。**幂等性没问题**（`DiagramSizer.Apply` 文档说幂等），
但**白跑一次**，且 `DiagramSizer.Apply` 内部那句 `DiagramLayout.Apply(diagram)` 在
**只处理一个 page 时被调用 N 次**（在 `foreach (var page in diagram.Pages)` 内）。

**危害：** 性能非重点，但 T0.5 的验收断言 1（带/不带 spec 框体一致）会**受容器包围盒影响**吗？
不会（框体宽高只看节点）。所以 P2。**但实施者应知道这个双调用**，否则会以为容器位置在 sizer 里定了。

### E-6 【P0】Renderer 完全不消费 `FontFamily`——任务书 §3.5 的"Renderer 与 VSDX 一致"在字体维度不成立

**这是本轮最重要的新发现。**

**证据（逐像素比对，三组独立实验）：**

```console
# 实验 1：TextBlock 字体
textBlock font = "Courier New"   → fr-Courier_New.png    01f4839f991799c2
textBlock font = "Arial"         → fr-Arial.png          01f4839f991799c2
textBlock font = "NoSuchFont-XYZ"→ fr-NoSuchFont-XYZ.png 01f4839f991799c2   ← 三张逐字节相同

# 实验 2：Node 字体（走 typography.fontFamily）
fontFamily = "Courier New" → nfr-Courier_New.png 09a7d17a02feca8f
fontFamily = "Arial"       → nfr-Arial.png       09a7d17a02feca8f
fontFamily = "Georgia"     → nfr-Georgia.png     09a7d17a02feca8f   ← 三张逐字节相同

# 实验 3：对照组（同一次运行里字号/粗体/斜体）
base   (Arial,14)    5d532ee94385f275
family2(Courier,14)  5d532ee94385f275   ← 换字体无变化
size2  (Arial,22)    8dfa82665b7cb06a   ← 换字号有变化
plain                89ea96c076bad58c
bold                 4447a2ca74eb9135   ← 粗体有变化
italic               188affd1b4928a30   ← 斜体有变化
```

**根因（E2）：**

```csharp
// FontResolver.cs:52-70
public string? Resolve(bool requiresCjk, string? preferredFontFile = null)
//   → 只按"文本是否含 CJK"在 2 个候选数组里挑一个文件
```

`FontResolver` **从不读 VSDX 的 `FontFamily`**。`OfficeImoVsdxRenderer` 把它传给
`options.FontFilePath`（`OfficeImoVsdxRenderer.cs:80`），即**整个页面用一个字体文件**。

**危害（为什么是 P0）：**

1. **任务书 §3.5 明文**："Renderer 不得通过另一套独立规则制造与 VSDX 不一致的预览。"
   当前**就是另一套规则**：VSDX 里写着 Verdana，预览用 DejaVu/Noto 画。
2. **D-4 的全部收益被抵消。** 修好 `VisioTextBlockBridge` 让字体能回读之后，
   `export-model` 能读到 `font: "Verdana"`，**但用户看 PNG 依然看不出任何区别**。
   任务书 §5.3 的验收"三种不同 DesignSpec 产生真实且可辨别的视觉效果"若包含字体维度，**做不出来**。
3. **17 项缺陷里没有这一项**，两轮前审也没发现（`grep -i "font.*render\|FontResolver"` 在两份审查里零命中）。
4. **plan §6.4 断言 2**（"三套 spec 产出的 PNG 在非背景区域有可见差异"）**若差异只来自字体，会假绿**。
   我实测 palette 变更**也不产生任何视觉差异**（A.3 的 diff count=7 全是元数据），
   所以断言 2 目前只能靠字号差异通过——**而字号差异恰好会触发 E-3 的溢出**。

**建议（不要求本阶段实现完整字体渲染，但必须闭环"报告"）：**
在 T2c 的"渲染器支持白名单"里把 `font-family` 明确标为
**"可写入 / 可回读 / 不可渲染"**，并让每次使用非默认字体时进 `unmetConstraints`
（`style.renderer-unsupported` + `target: <object>`）。这满足 §3.5 第三条"必须报告"，
且不扩大本阶段范围。**这条必须写进 plan，否则 T2c 的白名单会写成"fill/stroke/fontSize/bold/
italic/color/align 全支持"，而其中 font-family 是假的。**

### E-6b 【P0】TextBlock 的 `Color` 与 `Font` 都写在错误的层级——颜色也丢失，不只是字体

**这是 E-6 的姊妹项，audit 用它做缺陷 17 的示例却漏掉了根因。**

`audit:415` 恰好举了"给一个 TextBlock 设置 `italic: true, color: "#FF0000", fontSize: 14`"
作为"无法表达或渲染的样式必须报告"的例子。我照这个例子实测，发现**比 audit 说的更严重**：

```console
$ IR textBlock { color: "#FF0000", fontSize: 24 }
$ unzip -p tc-c1.vsdx visio/pages/page1.xml | grep -o '<Cell N="Color"[^/]*'
<Cell N="Color" V="#FF0000"                         ← 写在 Shape 级
$ $CLI export-model | jq '.pages[0].textBlocks[0].style.textColor'
null                                                 ← 回读丢失

$ 三种颜色各测一次（#FF0000 / #0000FF / #00CC00）
c1 #FF0000 readback textColor= None png= 3d69a1ed2743e2f6
c2 #0000FF readback textColor= None png= 3d69a1ed2743e2f6
c3 #00CC00 readback textColor= None png= 3d69a1ed2743e2f6   ← 三张 PNG 逐字节相同
```

**对照 Node（同一库的正确路径）：**

```console
$ unzip -p nc-red.vsdx visio/pages/page1.xml | grep -o '<Section N="Character">.\{0,60\}'
<Section N="Character"><Row IX="0"><Cell N="Color" V="#111111" />   ← 在 Character 行内
```

**根因（E2，`VisioTextBlockBridge.cs:139-141`）：**

```csharp
if (style?.TextColor is not null) AddCell(shape, "Color", style.TextColor);   // ← Shape 级
if (style?.Font is not null)      AddCell(shape, "Font",  style.Font);        // ← Shape 级
...
shape.Add(character);   // Character 段里只放了 Size 与 Style，没有 Color / Font
```

Visio 的文本字色属于 `Character` 段的 `Color` 单元格，**Shape 级的 `Color` 不是文本色**。
所以：

| 属性 | 写了吗 | 回读得到吗 | 渲染体现吗 |
| --- | --- | --- | --- |
| `fontSize` | ✅ Character/Size | ✅ 24.0 | ✅（PNG 会变） |
| `bold` | ✅ Character/Style | ✅ | ✅（PNG 会变，`t-bold` ≠ `t-plain`） |
| `italic` | ✅ Character/Style | ✅ | ✅（`t-italic` ≠ `t-plain`） |
| **`color`** | ⚠️ 写到了 Shape 级 | ❌ `null` | ❌（三色 PNG 全同） |
| **`font`** | ⚠️ 写到了 Shape 级 | ❌ `null` | ❌（三字体 PNG 全同） |

**危害（为什么是 P0）：**

1. **plan §5 把 `VisioTextBlockBridge` 的改动范围定义为"修字体单元格"**（`plan:263`：
   "当前写 shape 级裸 `<Cell N="Font">`，该字体未注册 FaceNames，Reader 读不到"）。
   **这个诊断只对了一半**：FaceNames 未注册确实是一个问题，但**更根本的是写错了层级**——
   即使 FaceNames 注册好了，Shape 级的 `Font` 也不被 `ReadStyle` 读取
   （`ReadStyle` 只读 `shape.TextStyle`，`OfficeImoDocumentReader.cs:602-625`）。
2. **`Color` 完全不在 D-4 的讨论范围内。** `owner-rulings.md` 的 D-4 只谈字体。
   按当前 plan 实施，**TextBlock 的字色会一直是"写了但不生效"**，而任务书 §3.4
   要求"标签、标题、注释"都要有明确视觉角色——字色是其中最基础的一项。
3. **audit 用这个例子论证"缺报告机制"，却没发现"东西根本没写对"。**
   这两件事叠加会让 T2c 的验收"写入后抽样回读断言单元格存在"**在错误的单元格上通过**：
   若断言只查 `doc.Descendants(Cell).Any(N=="Color")`，Shape 级那个也会命中。

**最小修法（应并入 T2）：** `VisioTextBlockBridge.BuildShape` 把 `Color` 与 `Font`
从 Shape 级移到 `Character/Row` 内，与 `Size` / `Style` 并列：

```csharp
character.Add(new XElement(VisioNs + "Row", new XAttribute("IX", "0"),
    new XElement(VisioNs + "Cell", new XAttribute("N", "Size"), ...),
    new XElement(VisioNs + "Cell", new XAttribute("N", "Style"), ...),
    style?.TextColor is not null
        ? new XElement(VisioNs + "Cell", new XAttribute("N", "Color"), new XAttribute("V", style.TextColor))
        : null,
    ... /* Font 同理，且需走库的 FontFamily 以注册 FaceNames（D-4 方案 A1） */));
```

并把 T2c 的"抽样回读"断言改为**按对象断言**（`textBlocks[i].style.textColor == 输入值`），
而不是"文档里存在某个 Color 单元格"。

### E-7 【P1】§5 文件改动表漏 4–5 个必改文件

**证据：**

```console
$ python3 -  # 解析 §5 表格
§5 rows: 11   duplicates: []
```

按 §9 任务反推，以下文件必须改但不在表中：

| 文件 | 依据 | 现状 |
| --- | --- | --- |
| `src/Vsdx.Protocol/ProtocolRunner.cs` | §12.3 的代码块就在这里插 `DiagramSizer.Apply`；T0.5 的 `stagesCompleted` 断言也在此 | 存在，未列入 §5 |
| `src/Vsdx.Engine/LayoutStrategies.cs` | T8a 改 `ClampDecorationsToPage`（`LayoutStrategies.cs:166-186`） | 存在，未列入 §5 |
| `src/Vsdx.Model/TypeScaleDefaults.cs` | §16.3 明写"新增" | **尚不存在**，未列入 §5 |
| `src/Vsdx.Adapters/VisioBackgroundBridge.cs` | §18.3 明写"新增" | **尚不存在**，未列入 §5 |
| `DiagramIrConverter`（`DiagramIr.cs:232` 起） | T1b 要"转换器消费 `Order`" | §5 只写"消费 `IrContainer.Order`"，未定位到 `DiagramIrConverter` |

**危害：** 任务书 §7 Stage 2 要求"文件级改动计划"。漏掉 `ProtocolRunner.cs` 尤其危险——
它是最核心的编排器，T0.5 的唯一落点，而 §5 看起来像"只改 Protocol/Engine 的 4 个文件"。
负责人据此评估影响面会低估。

### E-8 【P1】"破坏性变更两处 vs 三处"：plan 与 packet 互否，且 plan 自己三处

**证据：**

```console
$ sed -n '405,412p' docs/phase8/phase8.2-plan.md
**两处破坏性变更：`Canvas.Unit` 与 `Direction`。**
...
**`Canvas.Unit`**： 现状是"标签写 cm、数值仍按英寸"，

$ sed -n '96,108p' docs/phase8/phase8.2-review-packet.md
| 变更 | 影响 |
| `Direction` 生效 | ... |
| `mode=fixed` 语义修正 | ... |
| `Canvas.Unit` 换算 | ... |
三者都是修 bug，但会改变既有产物。
```

**packet 说三处（Direction / mode=fixed / Canvas.Unit），plan 说两处（Canvas.Unit / Direction）。**
而 plan 自己的 §4 第 13 行（`mode=fixed`）与 §7 的"行为改变"表都构成第三处。
**plan §7 少列了 `mode=fixed`。** 这直接决定发布说明要不要写"fixed 尺寸会变"——
漏了就是用户升级后页面尺寸突然变化而无预告。

### E-9 【P0】`plan` §11.2 表格与 §11.4/§11.6 直接对立（D-8 裁决无法执行）

**证据（同一节内）：**

```
plan:517   | `Layout == manual` | **保留并转置** | 作者坐标即意图，manual 不清空 |
plan:521   **`Direction + manual` 已由负责人 D1 裁定：方案 A。**
plan:525   | `layout=manual` 时 `direction` **不生效** |
plan:531   `DesignSpec.cs:163` 的 manual 分支保持不转置，只增加一条 `unmet` 记录
plan:486   | `Edge.Waypoints` | ...**按 D1 裁定，`manual` 下不转置，故此项目前无需实现** |
plan:578   | 7 | Manual 模式不转置，且 `unmetConstraints` 有记录 |
```

**§11.2 的表格（"保留并转置"）与同节紧接的裁决框（"不生效"）在同一屏内对立。**
`plan:531` 说"不需要给 `IrEdge` 加 `Waypoints` 字段"，但 `plan:261`（§5 文件表）**仍写着**
"`IrEdge` 加 `Waypoints`（若采纳 §11.2 方案 B）"。

**危害：** 这是 D-8 的**唯一执行落点**。实施者按表格实现"manual 下转置 waypoints"，
就违反了负责人 D1 的明文裁决（"不得擅自转置"）。**这是 P0 级的指令冲突，
与上一轮 r2 §3.2 点的是同一处，本轮只把 §11.2 的裁决框补上，表格一字未动。**

### E-10 【P1】D-4 的收益被高估：修好回读后预览仍不变

见 §C.2 与 E-6。此处只登记结论：**D-4 归因正确，但"修好就能让 TextBlock 字体生效"是错的**，
因为 Renderer 不消费字体。plan 与 owner-rulings 都按"修好=可见效果"叙述。

### E-11 【P1】T3 移除 5 色数组会让"同层容器彼此不同色"一起消失

**证据：**

```csharp
// DesignSpec.cs:202-204（现状）
var bandFills   = new[] { "#EAF3FB", "#EDF7ED", "#FDF2E9", "#F2E9F7", "#EAF6F6" };
// 按 group 序号取色：slot = index % 5
```

```console
$ export-model → 三个容器: data=#FDF2E9, svc=#EDF7ED, edge=#EAF3FB   ← 彼此不同
```

plan §3.1 的 `Palette` 记录只有 `string ContainerFill`（单值）：

```csharp
record Palette(..., string ContainerFill, string ContainerStroke, string ContainerText, ...)
```

T3 验收（`plan:434`）："换 palette → 容器色变（PNG 可见）"。**只测"变"，不测"彼此不同"。**

**危害：** `plan:365` 的修前基线表把"三个容器同一种浅蓝，看不出层次"列为**要修的缺陷**，
而 T3 会让三个容器变成"同一种新蓝"。**修完反而更接近要修的症状。**
→ 建议 `Palette` 增加 `ContainerFillSequence`（2–3 个按序色），或 resolver 按容器序做
明度偏移（+6%/-6%），并在 T3 验收加"同页 ≥2 个容器不同色"。

### E-12 【P2】`caption` 字号在 §2.3 与 §16 之间未定

**证据：**

```
plan:132  | TextBlock role=caption | EdgeLabel | 9（当前桥接层写 9，`NodeCaption` 是 10 —— 需统一） |
plan:915  | `...SizeForRole` | 硬编码 20/12/9/10/8 |
plan:931  TypeScaleDefaults.NodeCaption = 10
plan:931  TypeScaleDefaults.EdgeLabel   = 9
```

§2.3 说"需统一"，§16.3 给了常量（NodeCaption=10、EdgeLabel=9），
但**没说 caption 最终取哪个**。§16.4 断言 1 只说"caption 在 Bridge 与 TypeScale 产出同一字号"——
**这是个同义反复**：只要 Bridge 改成引用 `TypeScaleDefaults` 任一常量，两个路径就必然相同，
**无论选 9 还是 10**。测试无法证伪"选错了常量"。

我实测当前值：`caption → 9`（Bridge 硬编码）。若统一到 `NodeCaption=10`，**produced VSDX 的字号会变**，
属行为改变但 plan §7 未列。→ 需在 §2.3 定死一个值并说明兼容性影响。

### E-13 【P2】`IrEdge.Relation` 的 8 个取值只用于"有无箭头"，plan 未处理

**证据：**

```csharp
// DiagramIr.cs:174-180
/// <summary>Relationship semantics: calls | reads | writes | publishes | consumes | depends |
/// contains | flows. Drives line style and arrow defaults.</summary>
public string? Relation { get; set; }
```

```csharp
// DiagramIr.cs:329
EndArrow = RelationUsesArrow(edge.Relation) ? "triangle" : "none",

// RelationUsesArrow: 只有 "contains" 和 null 返回 false，其余 7 个值全部 true
```

**注释说"Drives line style and arrow defaults"，实际只驱动箭头有无（二分），不驱动 line style。**
`Palette` 只有单个 `EdgeStroke`，没有按 relation 分色/分线型的设计，plan 也没说"本阶段不做"。

按任务书 §2.1 的硬要求，这属于"协议接受、执行忽略、结果不报告"的第四种情况。
**它不在 17 项缺陷里，也不在 §15 闸门的枚举白名单里**（白名单列了 `row.type`，没列 `relation`）。

### E-14 【P1】T0.5 与 §12.4 的"同字号"断言在页面尺寸不同时必然失败

**证据（我独立复算 `TypeScale.ForCanvas`）：**

```python
TypeScale.ForCanvas(w,h,n):
    sizeFactor    = clamp(hypot(w,h)/20, 0.75, 1.5)
    densityFactor = clamp(sqrt((w*h/n)/8), 0.85, 1.3)
    NodeTitle     = clamp(11 * sizeFactor * densityFactor, 8, 14)

(11,    8.5,  10) → NodeTitle = 8.919
(20,    14,   10) → NodeTitle = 14.000
(11.35, 9.85, 10) → NodeTitle = 9.771
```

`plan:433`（T0.5）："同字号下带 spec 与不带 spec 框体一致"。
`plan:641-643`（§12.4）："断言 1：同一字号下……框体宽高必须一致；断言 5：修后……节点宽差 < 0.05""。

**但"不带 spec"走 `DiagramSizer.Apply(diagram)` 时用页面当前尺寸（默认 11×8.5），
"带 spec"若声明 `canvas.fixed 20×14` 则用 20×14——两者 TypeScale 差 57%（8.919 vs 14.0），
框体必然不等。**

计划没加"同一画布尺寸"前置条件，所以这两条断言**在"带 canvas 的 spec"上判"实现错了"**，
实施者会被误导去"修正"一个本来正确的实现。

### E-15 【P2】`plan` §15.1 的"残留表"把 `IrContainer.Order` 列为残留，与 T1b 冲突未清

**证据：**

```
plan:798  | `IrContainer.Order` | 协议接受，从不执行 |     ← §15.1 "审查实测的残留"
plan:838  **注意：** `containers[].order` **不能**放进该清单——缺陷 16 / T1b 正是要在本阶段实现它。
plan:844  private static readonly string[] NotYetExecuted = { "metadata.*" };
```

§15.2 已正确排除，但 §15.1 的表仍把它写成"修完 14 项后仍存在"的残留。
读者看到 §15.1 会以为本期不做。属**表述残留**，P2，但一行字就能清。

---

## F. 裁决

### **FAIL**

**理由：4 个阻断项（E-6 字体白名单、E-6b TextBlock 的 Color/Font 层级错误、E-9 D-8 指令冲突、
E-4+D.3 协议闸门覆盖不足），加上 4 个必须在开工前定的 P1（E-1、E-7、E-8、E-14），
构成"计划仍不可安全实施"。**

我明确说明：**这不是因为文档不够漂亮。** 我认可本计划有实质进步，下面先列认可项。

### F.1 值得明确认可的部分（真进步，不是措辞）

| # | 进步 | 我的核验 |
| --- | --- | --- |
| 1 | 缺陷计数从"16⇄16⇄16 一一对应"（假）改为 **17:17:16+2** 且有脚本化解释 | 我重跑解析：17 行、16 refs、18 §9 项、差集 `{T8,T9}`，**结论正确**（只有"17 refs"这个佐证数字错，见 B.2） |
| 2 | **M1 的根因改正**（"阶段缺失"而非"顺序错"） | 我用 `stagesCompleted` 与框体对照**独立复现**，plan §12 完全正确 |
| 3 | **§6.4 换真 IR + 输入正确性闸门** | 三个 IR 文件确为 `DiagramIr` 格式，标签完整（`Web 门户\nReact SPA` 保留） |
| 4 | **§16 依赖方向下沉到 Model** | 我核对三个 `.csproj`，`Engine → Model ← Adapters` 菱形成立 |
| 5 | **§18.3 的事实核查**（`pages.xml` 无现有写者、`PageSheet` 位置） | 我 `grep -l "pages.xml"` 无结果、解包确认 `PageSheet` 在 `pages.xml`，**两条都对** |
| 6 | **§14.3 的跨页 ID 实测** | 我复现：两页各有 `same` → `status: succeeded`，`nodeIds` 是 `ValidatePage` 局部变量（`ProtocolRunner.cs:238`） |
| 7 | **D-4 / D-8 的归因** | 任务书 §3.5（281 行）与 §4.4（297 行）引用**逐字准确**；manual 不转置与实测行为一致 |
| 8 | 闭环了上一轮点名的 3 条矛盾（Writer 范围、`DiagramIr.cs`、"`TextScale` 类名"） | 逐条确认已改对 |
| 9 | **§12.4 的对照式断言方法论** | "带 spec vs 不带 spec 框体一致"确实能区分"装回阶段"与"改常量"，这是正确的测试设计（尽管有 E-14 的限定缺失） |

**这个计划不是敷衍。它的核心修法（StyleResolver 三层优先级、BaseSize 缩放、T0.5 装回阶段、
T1b 消费 Order、T0b 保留 lane）我逐条验过，方向都对，验收大多可证伪。**

### F.2 阻断项（必须修完才能开工）

#### 阻断 1 — E-6：Renderer 不消费 `FontFamily`，T2c 的白名单会写成假的

**为什么阻断：** 任务书 §3.5 是**硬要求**（"Renderer 不得通过另一套独立规则制造不一致预览"、
"无法表达或渲染的样式必须报告"）。当前字体维度**同时违反两条**，而 plan 的 T2c 正要把
`font-family` 放进"支持白名单"。这不是"实施中完善"能解决的：**白名单写错，T2c 的验收
（"白名单外进 unmet"）就变成一个永假的条件**——字体永远是"白名单内"，永远不报告。

**最小修法（不扩范围）：**
1. T2c 的白名单拆成两列：`可写入+可回读+可渲染`（fill/stroke/fontSize/bold/italic/color/align）
   与 `可写入+可回读+**不可渲染**`（font-family）。
2. 第二列每命中一次 → `unmetConstraints` 加 `style.renderer-unsupported`（isHard=false）。
3. 在 §6.4 断言 2 后加一句限定："**三套 spec 的 PNG 差异不得只依赖字号**"，
   避免用字号差异掩盖字体/配色未生效。
4. 若负责人同意扩大范围，另开任务做 Renderer 字体解析；**但不做也必须报告**。

#### 阻断 2 — E-9：`plan` §11.2 表格与 D-8 裁决对立

**为什么阻断：** 这是 D-8 的唯一执行落点。表格说"manual 保留并转置"，裁决框说"不生效"。
实施者按表格做就违反负责人明文裁决。且 `plan:261` 仍写"`IrEdge` 加 `Waypoints`（若采纳方案 B）"，
而 `plan:531` 说"方案 B 需求已作废"——**同一份文档里"方案 B 已作废"与"若采纳方案 B"并存**。

**最小修法：** 删掉 §11.2 表格中 `Layout == manual` 那一行的"**保留并转置**"，
改为"**保留不转置**（D-8）；waypoints 在 manual 下由 `ManualLayoutEngine` 保留，
`Direction` 不生效并进 unmet"；同时删 `plan:261` 的"`IrEdge` 加 `Waypoints`"。
`plan:486` 与 `plan:531`、`plan:578` 三处已正确，保留。

#### 阻断 3 — E-4 + D.3 的协议工程缺口（附 E-3 作为配套闸门）

**为什么阻断：** 任务书 §2.1 的硬要求是"不允许出现第四种情况：协议接受、Schema 允许、
执行时忽略、结果又没有报告"。当前至少 **4 类**残留不在三条闸门的覆盖里
（`containerLayouts.*` 非法值、`anchor.mode`、`FromPort`/`ToPort` 回读、`ConstraintSpec.Target`），
而 `execute` **不加载 Schema**（闸门 1 是在重造 Schema 的约束）。

**具体证据（都是本轮实测）：**

```console
$ {"layout":"layered","containerLayouts":{"svc":"kaboom"}}
  applied: ['container svc relaid as kaboom']   unmet: []        ← 报告"已按 kaboom 重排"
$ {"layout":"manual","direction":"topToBottom"}                   ← manual 下 direction 静默
  applied: ['layout=manual','container.bands','canvas.auto']  unmet: []
$ IR anchor mode="relative"  → 位置 7,5 被当作 fixed 保留      unmet: []
$ IR edge fromPort/toPort    → export-model 后 fromPort: null  unmet: []
$ baseSize=24 → 12 处文字溢出，execute 仍 status=succeeded, hard=True, score=76
```

**最小修法：**
1. 闸门 2 的枚举白名单必须**从 Schema 生成或与之同源**，而不是手抄一份（手抄必漏，
   实测漏了 `containerLayouts.*` 的 value 与 `relation`）。
2. 闸门 3 的 `NotYetExecuted` 必须列出**全部**本期不执行的字段：
   `metadata.*`、`anchor.mode=relative`、`IrEdge.Relation`（除箭头外）、`ConstraintSpec.Target`；
   并把 `containerLayouts` 的非法值纳入闸门 2。
3. **`text.overflow-*` 必须进硬约束或 `unmet`（E-3）**：当前 12 处裁切仍 `status=succeeded`、
   `hard=True`、`score=76`，任务书 §5.3 的"不发生文本裁切"因此不可证。
   T0.5 与 §6.4 断言 4 必须显式用 `check-layout` 的 counts 判定，不能依赖 `execute` 的 `status`。
4. 明确回答"为什么不用现有 Schema 做闸门 1"（E-4），否则 §2.1 的"三方失守"
   会在修完之后变成"Schema 仍不生效、闸门另造一套"。

#### 阻断 4 — E-6b：TextBlock 的 `Color`/`Font` 写错层级，D-4 的诊断只对了一半

**为什么阻断：** plan §5 对 `VisioTextBlockBridge` 的改动范围是"修字体单元格，让 FaceNames 注册"。
我实测证明**层级错了**：`Color` 与 `Font` 都写在 Shape 级 `Cell` 上，而 Reader 只读 `shape.TextStyle`
（`OfficeImoDocumentReader.cs:602-625`），所以：

```console
$ textBlock color=#FF0000  → XML: <Cell N="Color" V="#FF0000"/>（Shape 级）
$ export-model             → textColor: null                        ← 回读丢失
$ 三色入参 → 三张 PNG 逐字节相同                                     ← 渲染无差异
```

**节点用的是正确层级**（`Character/Row/Cell N="Color"`），所以这是**同一能力的第二条错误路径**
——正是 audit §3 反复强调的缺陷模式，但 D-4 只谈字体、遗漏了颜色。

**最小修法：** 把 `Color` 与 `Font` 移入 `Character/Row`（代码示例见 E-6b）；
T2 的范围从"字体回读"扩为"TextBlock 文本属性回读（Color + Font + Bold + Italic + Size）"；
T2c 的抽样回读断言改为**按对象断言**，否则会在错误的单元格上"通过"。

### F.3 条件项（不阻断开工，但应在 T1/T2 动手前定；建议写进 plan 对应节）

| # | 事项 | 落点 |
| --- | --- | --- |
| C1 | `Palette` 要有 `ContainerFillSequence`（或 resolver 按序偏移），否则 T3 会让容器同色（E-11） | §3.1 + T3 验收 |
| C2 | `caption` 字号定死 9 或 10，并披露是否改变既有产物（E-12） | §2.3 / §16.3 |
| C3 | 破坏性变更从"两处"改为**三处**，补 `mode=fixed`（E-8） | §7 + packet §五 |
| C4 | §5 补 5 个缺失文件（E-7） | §5 |
| C5 | T0.5 / §12.4 的"同字号"断言加"同一画布尺寸"前置（E-14） | T0.5 / §12.4 |
| C6 | audit §2.2c 的根因同步为"阶段缺失"（E-2） | audit §2.2c |
| C7 | 单位标签的三种错误表述统一为"标签恒 `IN`、数值未换算"（E-1） | plan:201/402/411 |
| C8 | `UnitConversionIsCorrect` 改为断言具体数值（D.5-1） | §6.3 |
| C9 | `ContainerColorsComeFromPalette` 加"同页 ≥2 容器不同色"（D.5-2） | §6.2 |
| C10 | 明确 `Kind` 不回读，T0b 不得用回读 kind 作判据（D.4-3） | T0b |
| C11 | `VisioBackgroundBridge` 用 `XDocument` 增量修改，不整份重建（D.4-2） | §18.3 |
| C12 | 拆分后 `appliedDesignNotes` 顺序会变，需声明或修测试（D.1-2） | §12.3 |
| C13 | T1 拆出 BaseSize 缩放的独立验收（D.1-3） | §9 |
| C14 | 陈旧数字：`plan:20`（14 行→17 行）、`packet:27` 与 `packet:140`（16→17）、`plan:788/791` | 三文件 |
| C15 | `plan:220` 的"§4 task refs = 17 个"改为 16（B.2） | §4 |
| C16 | audit §1 统计（7/4/6、3/3/2、17、§2 的 7 个）与表格（18 行、8/4/6）对齐（B.5） | audit:45 / :671 / :679 |
| C17 | `IrEdge.Relation` 与 `anchor.mode` 进"本期不做"清单或进闸门（E-13、D.3） | §15.3 |
| C18 | T2 范围扩为 TextBlock 全部文本属性（Color/Font/Bold/Italic/Size），T2c 断言改为按对象（E-6b） | §5 / T2 / T2c |

### F.4 达到什么条件可转 PASS WITH CONDITIONS

**三种可能，任一成立即可：**

1. **修完 F.2 的四个阻断项**（E-6 白名单拆分 + 报告、E-6b TextBlock Color/Font 层级、
   E-9 表格删除、D.3 的三条修法），且 F.3 的 C3/C4/C5 落定
   → **PASS WITH CONDITIONS**（条件=C1/C2/C6–C18）。
2. 若负责人明确接受"字体与字色只报告、不渲染/不回读"，则 E-6 与 E-6b 降为条件项，
   仍需 E-9 + D.3 两项。**但 E-6b 不建议降级**：字色是 §3.4"标签/标题/注释要有明确视觉角色"
   的最基础一项，且修法只有几行（移单元格位置）。
3. 若负责人明确"`anchor.mode` / `relation` / `Target` / `containerLayouts` 非法值
   本阶段不做且不报告"（即接受第四种情况），则 D.3 降为条件项——
   **但这直接违反任务书 §2.1 与 §4.10，我不建议。**

**我不会因为"整体设计看起来合理"而通过**（任务书 §7 Stage 3 明文禁止）。
上述每条阻断项都有本轮实测输出作为证据，且都指向"按此计划实施会产出错误或虚假的结果"。

### F.5 为什么不是 PASS

用一句话概括：**17 项缺陷全部成立、核心修法方向正确、文档一致性已明显改善，
但计划在"字体渲染"这一维上建立了虚假的验收基础（E-6），在 D-8 的执行落点上自相矛盾（E-9），
并且它承诺的"机制性闸门"覆盖不到自己列出的残留（D.3）。**

前三条会让实施者产出"通过验收但用户看不出效果"（E-6、E-6b）、
"违反负责人裁决"（E-9）的代码；第四条会让"下一轮审计必然再发现同类残留"
（plan §15.1 自己的话）再次发生。这四条都不是风格问题。

**其中 E-6b 尤其值得注意：它是同一份 audit 自己举的例子（`audit:415` 的 TextBlock
`color:"#FF0000"`），但 audit 只看到"缺报告机制"，没看到"颜色写在了错误的 XML 层级"。
两轮前审也没发现。这恰好印证了 audit §8 自己的判断——"把代码看起来做了当成确实做了"——
只不过这次犯错的是 audit 与两轮审查，而不是实施者。**

---

## G. 局限

我明确登记本轮审查的边界，避免读者高估其覆盖。

### G.1 无法验证的事项

| # | 局限 | 影响 |
| --- | --- | --- |
| 1 | **无 Microsoft Visio（L4 未验证）**，与任务书 §5.7 一致 | 我无法判断"字体不可渲染"在 Visio 原生打开时是否真的不生效。VSDX 里 `N="Font" V="Verdana"` 在 Visio 中很可能**是**生效的（只是我们的 Renderer 不认）。**所以 E-6 是"Renderer 不一致"，不是"VSDX 错误"**——这个区分很重要，我在 E-6 的表述里已按此写。 |
| 2 | **负责人的 D1 原文我拿不到** | `owner-rulings.md` 引用的"Manual 模式必须保留用户坐标"在任务书里零命中，在本仓库文档里最早以"任务要求"形态出现（`7636486`）。我按"内容自洽 + 与实测行为一致"接受，但**来源标记为未验证**（§C.1）。若该句不是负责人原话，D-8 的归因链需重审。 |
| 3 | **未审计 `LayoutAnalyzer` / `RepairController` / `OrthogonalRouter` 的内部逻辑** | 我只测了它们的**输出**（`check-layout`、`quality`、waypoints）。E-3 里"文字裁切被判 warning 而非 error"我确认了**判定结果**，但**没定位到 `LayoutAnalyzer` 里决定 severity 的那一行**——这需要读全文，超出本轮范围。 |
| 4 | **未验证 18 个基准硬约束** | `benchmarks/` 有 26 个 JSON，我不清楚"18"的准确所指（可能是 18 条约束而非 18 个文件）。`plan:452` 与任务书 §5.5 都写 18，我按文档接受，**未独立核对**。 |
| 5 | **PNG 逐像素比对不能证明"视觉上无差别"** | 我用 sha256 相同推断"字体不生效"。这在像素级是确定的，但**不排除**"两个字体在这个字号下恰好渲染相同"的可能。不过 `NoSuchFont-XYZ` 也产出同一张图，这基本排除了巧合——**不存在的字体名不可能渲染出与 DejaVu 一样的字形，除非字体名根本没被读**。 |
| 6 | **未测 `update` 路径的完整保真矩阵** | 我只测了"edge label font 在 create→update 后仍在"（**在**）与"cm 单位在 create→update 后被二次换算"（**被**）。任务书 §5.4 要求的 `IR → Spec → Model → VSDX → Reader → Model → Render` 全链回归，本轮只做了关键点抽样。 |
| 7 | **缺陷抽样的"代表性"由我判断** | 任务要求"至少 8 项含 P0"，我做了 17 项全覆盖，但**测度方法**（模型 diff / XML 解包 / PNG 哈希）本身有盲区：模型 diff 看不到 Writer 丢弃的字段（如 E-6 的字体，模型里有、渲染不用），XML 解包看不到 Reader 的语义错误。我尽量三种手段交叉，但**不能声称完备**。 |
| 8 | **未审查实施者的历史对话** | 我只能看到仓库快照。若某处修改有仓库外的理由（如负责人口头指示），我看不到。E-1/E-2 指出的矛盾可能"实施者已知但决定暂不改"，我按"文档是唯一交付物"评判。 |

### G.2 本轮审查自身的可能错误

按我批评审计的标准，我也要自查：

1. **B.5 的多数字统计**依赖我对表格行首标记的解析（`❌` / `⚠️` / `✅` 前缀）。
   若某行标记形态特殊（如 `**❌`）我的脚本可能误判。我人工复核了 18 行，结果与脚本一致，
   但**若有人用不同解析口径得到 17 行（把两个 `Canvas.Mode` 行算一行），数字会不同**——
   我在正文写明了"18 行"的依据（`Canvas.Mode=auto` 与 `Canvas.Mode=fixed` 分列）。
2. **E-11（容器同色）是我的推断，不是实测。** 当前代码用 5 色数组，palette 尚不存在，
   我无法实测"换成单值 palette 后会同色"。这条依据是 `plan:145-155` 的 `record Palette` 签名
   （`string ContainerFill` 单值）与 `plan:365` 的基线观察（"三个容器同一种浅蓝"是要修的缺陷）
   之间的**逻辑冲突**。若实施者本打算在 resolver 里做偏移，我的批评就不成立——
   但 plan 没写，所以我按"文档未定"评判。**这条是 P1 而非 P0，正因它是推断。**
3. **E-14 的 TypeScale 复算**用的是我从源码抄的公式（`TextLayout.cs:42-67`），
   若公式有遗漏（如 `ForCanvas` 后续还有调整），数值会偏。我核对过 `TextLayout.cs` 全文的
   这 20 行，但**未运行单元测试验证我的复算**。结论（页面尺寸不同→字号不同→框体不等）
   在定性上稳健，具体数值可能有 ±0.1 误差。
4. **§C.1 我对 D-8 来源的质疑可能过重。** 负责人的原话完全可能在仓库外存在（会话记录、
   issue、邮件），我只证明了"仓库内无第一手来源"。**我不声称实施者伪造引文**，
   只声称"该引文在本仓库内不可核验"。这个措辞差别很重要。

---

## 附：本文引用的关键位置（便于复核）

| 主张 | 位置 |
| --- | --- |
| `Direction` 无消费者 | `DesignSpec.cs:23,134`；`LayoutStrategies.cs:37,46`；`CandidateComparison.cs:29` |
| `Palette` 无消费者 | `DesignSpec.cs:83`；`DiagramModel.cs:317`；`schemas/phase7/design-spec.schema.json` theme.palette |
| 容器色硬编码 | `DesignSpec.cs:202-226` |
| `min_size` 死代码 | `DesignSpec.cs:178-188` |
| `containerLayouts` 在 manual 分支外 | `DesignSpec.cs:139-160` |
| `BuildMapping` 只走单页简写字段 | `ExecutionResult.cs:168-188` |
| spec 路径无 `DiagramSizer.Apply` | `ProtocolRunner.cs:66-88` |
| `Sizer` 内重复 `DiagramLayout.Apply` | `DiagramSizer.cs:111-112` |
| `lane` 被改写 | `DiagramIr.cs:282` |
| Swi 判据 | `LayoutStrategies.cs:109`；`SwimlaneLayoutEngine.cs:21` |
| `ClampDecorationsToPage` 独立夹紧 | `LayoutStrategies.cs:166-186` |
| 布局期清空 waypoints | `LayoutStrategies.cs:138-142` |
| 现有 `Transpose` 只覆盖三类 | `CandidateComparison.cs:141-165` |
| `IrContainer.Order` 无消费者 | `grep "\.Order\b" src/` 无结果 |
| Flow 引擎容器排序 | `FlowAndTreeLayoutEngine.cs:93` |
| TextBlock 裸 `Font` / `Color` cell（层级错误） | `VisioTextBlockBridge.cs:139-141` |
| TextBlock 的 Character 段缺 Color/Font | `VisioTextBlockBridge.cs:146-158` |
| `SizeForRole` 硬编码 | `VisioTextBlockBridge.cs:25-33` |
| Reader 只读 `shape.TextStyle` | `OfficeImoDocumentReader.cs:602-625` |
| Reader 不读 `LabelFont` | `OfficeImoDocumentReader.cs:629-676` |
| Reader 不读 `Node.Type` | `OfficeImoDocumentReader.cs` 全文无 `node.Type =` |
| `ShapeStyle` 有 Shadow/Opacity/Radius/LineJoin | `DiagramModel.cs:300-303`，**无消费者** |
| `FontResolver` 只按 CJK 二选一 | `FontResolver.cs:52-70` |
| Writer 硬传 Inches | `OfficeImoDocumentWriter.cs:48-52` |
| `IncrementalEditor` 按 UnitFactor 换算 | `OfficeImoIncrementalEditor.cs:62-64` |
| `TypeScale.ForCanvas` 公式 | `TextLayout.cs:42-67` |
| `PageSheet` 在 `pages.xml` | 解包 `u-inches.vsdx` 实测 |
| Adapters 无 `pages.xml` 写者 | `grep -l "pages.xml" src/Vsdx.Adapters/*.cs` 无结果 |
| `execute` 不加载 Schema | `Program.cs:721-731`；`grep "JsonSchema" src/` 无结果 |
| 文字裁切不计入硬约束 | `b24.vsdx`：`check-layout` counts.errors=0, score=76, hard=True |
| 任务书 §3.5 / §4.4 | `goal-20261126-2034.md:281`、`:297` |
| 任务书无 manual 语义规定 | `grep "Manual\|转置"` 零命中 |

---

**审查者签名：** 独立第三方（非 Stage 1/2 参与者）
**审查方式：** 源码阅读 + 端到端实测 + 逐像素比对 + 解包 XML
**产品代码改动：** 无。审查结束时 `git status --short` 输出仅为：
`?? docs/phase8/phase8.2-review-r3.md`（本文件）与 gitignored 的 `.review-scratch-r3/`（不计入）。
`docs/` 下既有五份文档的 md5 与审查开始时一致。
**实测产物：** `.review-scratch-r3/`（326 个文件，含 VSDX/JSON/PNG），可复现上文全部命令。

---

## 附录：全部一手命令（可复现）

```bash
cd /home/kity/oclab/vsdx-engine && source .envrc.sh
CLI=$PWD/src/Vsdx.Cli/bin/Release/net8.0/vsdx-tool

# 基线
dotnet build src/Vsdx.Cli/Vsdx.Cli.csproj -c Release        # 0 warning 0 error
dotnet test  tests/Vsdx.Tests/Vsdx.Tests.csproj -c Release  # 373 passed

# A.2 Direction
for l in layered flow tree grid; do for d in leftToRight topToBottom; do
  $CLI execute --ir examples/phase8/arch.ir.json \
    --spec <(echo "{\"layout\":\"$l\",\"direction\":\"$d\"}") \
    --output /tmp/d-$l-$d.vsdx --json >/dev/null
  $CLI export-model --input /tmp/d-$l-$d.vsdx --output /tmp/d-$l-$d.json --json >/dev/null
done; done
# → 四策略两方向 xspread/yspread 完全相同

# A.3 / A.14 / A.7 / A.9 / A.13 —— 见正文各节的 spec 与预期
# E-6 / E-6b —— 逐像素比对
for f in "Courier New" "Arial" "NoSuchFont-XYZ"; do
  $CLI execute --ir fr.ir.json --output /tmp/fr.vsdx --json >/dev/null
  $CLI render  --input /tmp/fr.vsdx --format png --output /tmp/fr.png --json >/dev/null
  sha256sum /tmp/fr.png        # 三者相同
done
```

> 注：上面的 `arch.ir.json` 路径相对仓库根；其余 IR/spec 由正文各节给出，均在 `.review-scratch-r3/` 内。
