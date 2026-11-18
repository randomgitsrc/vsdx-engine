# 后续阶段候选缺陷（Stage 5 过程中发现，不属 T0 范围）

**用途：** 记录在实施 Phase 8.2 时发现的、超出当前子任务范围的既有缺陷。
**原则：** 不在此处修复，也不因此扩大当前子任务范围；仅如实登记，供后续评估。

---

## C1 【P1】`DiagramLayout.Apply` 不保证父 Group 包围子 Group

**发现于：** T0 的嵌套 Group 测试补强（提交 `b0c2bd0`）

### 实测

同一夹具，两种布局方向都失败：

```console
LeftToRight: parent 5.25x2.41, child 2.60x1.62  -> encloses = False
TopToBottom: parent 2.41x5.25, child 1.62x2.60  -> encloses = False
```

**与转置无关**——不转置时同样失败。

### 根因

`DiagramLayout.Apply` 的 `OrderGroups` 让父组先于子组处理
（`Visit(parent)` 在 `ordered.Add(group)` 之前）。计算父组 bounds 时，
子组取的是**当时的** `g.Bounds`：

```csharp
var childGroups = page.Groups
    .Where(g => g.ParentId == group.Id && g.Bounds is not null)
    .Select(g => g.Bounds!)     // ← 此刻的值
    .ToList();
```

而子组本身若 `AutoSize`（默认），会在之后被重算成大得多的盒子，
**父组不会回头调整**。结果：父组装不下子组。

### 影响

- `CheckContainment` 只查 Group→Node，查不出这个问题（见下 C2）
- 渲染时子容器可能溢出父容器边框
- 多级嵌套时误差会累积

### 候选修法

两遍处理：先自下而上重算所有组，再自下而上**或**父组重算时使用子组的**新** bounds。
或：处理顺序改为子先父后，父组用已更新的子 bounds。

**需要先有真实用例验证**——当前基准里没有多级嵌套容器，所以此缺陷未在任何基准上暴露。

---

## C2 【P2】`CheckContainment` 不检查父 Group 对子 Group

**发现于：** 同上

`CheckContainment` 的实现只比对 `group.Bounds` 与 `group.Members` 里的
**node**，从不读 `ParentId`：

```csharp
foreach (var memberId in group.Members)
{
    if (!nodes.TryGetValue(memberId, out var node)) continue;   // 只有 node
    ...
}
```

**因此它返回空，不能证明嵌套容器关系正确。** C1 就是在这个盲区里存在的。

### 候选修法

增加一段：对每个 `ParentId` 非空的组，断言其 bounds 被父组 bounds 包围。
与 C1 一并修复才有意义。

---

## 边界

- 两项**均不在 T0 范围**，本次未修复
- 未因此修改任何产品代码
- 建议：进入 8.3 前先补一个「多级嵌套容器」基准，让 C1 在真实产物上可复现

---

## C3 【P1】正交路由为多条边分配同一通道，导致线条重合

**发现于：** 真实制图验证（`.lab/cross-border-orders`，2026-11-18）

### 实测

14 节点 / 14 边 / 4 容器的跨境电商订单系统，全部边设为 `kind: rightAngle`：

```console
$ check-layout v3.vsdx
  edge.through-node (3)
    [error] 连线 'e7' (order → redis) 穿过无关节点 'pg'
    [error] 连线 'e8' (order → es)    穿过无关节点 'redis'
    [error] 连线 'e12' (customs → s3) 穿过无关节点 'fx'
  score=0/100
```

导出 waypoints 后发现更根本的问题：

```
e7: order(2.40,7.55) -> redis(4.95,2.35)
    waypoints: [(0.90, 6.84), (0.90, 3.11)]
e8: order(2.40,7.55) -> es(7.50,2.35)
    waypoints: [(0.90, 6.84), (0.90, 3.11)]     ← 与 e7 完全相同
```

**通道复用统计：**

| 通道 x | 共用边数 |
| --- | --- |
| 0.90 | **3** |
| 3.71 | 2 |
| 7.50 | 2 |
| 8.74 | 2 |

节点 x 范围是 `2.40 ~ 12.60`，**x=0.90 落在所有节点左侧之外**——相当于绕到图外。

### 更严重的情形：通道完全脱离起止节点

同一夹具换成严格垂直对齐（`customs` 与 `es` 都在 x=8.20，正上下关系）：

```
e9: customs(8.20,7.70) -> es(8.20,2.50)      ← 理想应是一条 x=8.20 的垂直线
    waypoints: [(1.10, 6.99), (1.10, 3.26)]  ← 实际通道在 x=1.10
```

**路由把一条本可以走直线的边，送到了图最左侧（x=1.10），
横穿整张图的宽度，并在此过程中穿过 x=5.40 的两个节点。**

这不是"通道复用"或"选错通道"，而是**通道搜索没有以起止节点的 x 为约束**。
起止点同在 x=8.20 时，一个合理的实现应当直接连直线。

### 并且：`kind` 被改写

同一夹具把所有边设为 `kind: "straight"` 后重新生成，读回结果：

```
e9  kind=curved  waypoints=[(1.10,6.99), (1.10,3.26)]
e13 kind=curved  waypoints=[(15.30,6.99),(15.30,3.26)]
```

**指定的 `straight` 被改成了 `curved`，并且仍被送到图的极左（1.10）与极右（15.30）。**

两个后果：

1. 调用方无法通过 `kind` 控制路由形态——指定被静默覆盖
2. 覆盖后的结果比指定更差（直线本可穿过，绕行后横穿全图）

**这一条违反了"不允许静默丢弃输入"。**

### 根因（已定位到函数）

`OrthogonalRouter.ComputeChannels`（`:117-135`）生成**列与列之间**的竖直通道：

```csharp
for (int i = 0; i < columns.Count - 1; i++)
{
    if (columns[i + 1].left - columns[i].right < 0.14) continue;   // 列间隙
    channels.Add((columns[i].right + columns[i + 1].left) / 2);
}
```

**当起止节点 x 完全相同（垂直对齐）时，两点之间没有"列间隙"，
遍历结果只能落在最外侧的通道**——即 `channels[0]` 或 `channels[^1]`。

实测三例相互印证：

| 边 | 起止 x | 通道 x | 页面宽 |
| --- | --- | --- | --- |
| e10 | 5.40 → 5.40 | **1.10** | 16.0 |
| e11 | 8.20 → 8.20 | **1.10** | 16.0 |
| e12 | 11.00 → 11.00 | **15.30** | 16.0 |

`1.10` 与 `15.30` 是首尾两个通道，与起止点无关。

**这是算法在"垂直对齐连线"上的结构性盲区**，而非参数或配置问题。
一个合理的实现应先判断：若起止 x 相同且中间无阻挡，直接连竖直线。

### 影响范围

- 任何"存储正下方对应服务"的标准架构图布局都会触发
- 该布局正是本项目 `skill/SKILL.md` §2 第 4 条推荐的做法
  （"Put each data store directly under the service that owns it"）

**即：Skill 推荐的最佳实践，恰好触发路由的最坏情况。**

### 三个独立问题

1. **通道复用**：路由为不同边选了同一 x 通道，产生完全重合的线段，
   `check-layout` 的 `edge.crossing` 只查相交、查不出重合
2. **越界通道**：通道落在整张图之外（x=0.90 / x=1.10，而节点范围是 2.40~12.60）
3. **无视起止约束**：起止节点同在一条铅垂线上时，仍被送到远处再折回

### 为什么基准未暴露

18 个基准单页节点数 ≤15 但**列间距大**（列距 2.90"）、且每带节点少，
同源多分支的情况罕见。本例中 `order` 同时连 `pg`/`redis`/`es`/`bus` 四条边，
触发了通道复用。

### 候选修法

- 路由时为已占用通道记录，后续边避开（或施加偏移）
- 通道搜索范围限制在两端节点的 x 区间内，避免越界
- `check-layout` 增加 `edge.overlap`（共线重合）检测

**未修复**，不属当前任务范围。

---

## C4 【P1】文字尺寸不区分形状，圆形/菱形节点必裁切

**发现于：** 真实制图复测（阶段 A 第二轮，2026-11-18，HEAD `67821aa`）

### 实测

三节点 + 两容器的执行路径图，`typography.baseSize=14`：

```console
$ check-layout enterprise.vsdx
  score=0/100
  text.overflow-horizontal (2)
    [error] 'web' 最长行约 0.82" 宽，实际 0.70"
    [error] 'app' 最长行约 0.78" 宽，实际 0.70"
```

```
web    shape=Circle     box=0.70x0.70   font=14
app    shape=Circle     box=0.70x0.70   font=14
order  shape=Rectangle  box=1.38x0.70   font=14   ← 同字号，宽度是圆的 2 倍
```

### 根因

`DiagramSizer` 与 `TextLayout` **完全不读 `Node.Shape`**：

```console
$ grep -c "Circle|Ellipse|Diamond" src/Vsdx.Engine/TextLayout.cs
0
```

所有节点按**矩形**计算内接文字区域。但圆内能容纳文字的宽度约为直径的 70%
（内接正方形），菱形更小（约 50%）。因此：

- 圆形节点：文字按矩形量，实际放不下 → 必裁切
- 菱形节点：更严重
- 六边形：介于两者之间

### 为什么现在才暴露

缺陷 20 把裁切从 `warning` 升为 `error` 之前，这类图只是 `score` 偏低，
仍报 `hard=true`。升级后它立即变成硬失败。

**这不是缺陷 20 引入的回归，而是它揭露的既有问题。** 缺陷 20 的做法仍然正确：
裁切不该被当作可交付。

### 候选修法

1. 按形状算内接系数（圆 0.70、菱形 0.50、六边形 0.75），据此放大框体
2. 或：`DiagramSizer` 按形状设定最小边长
3. `TextLayout` 的换行估算也应知道可用宽度是内接区域，而非整个包围盒

**需要先有视觉准则**：圆形节点内的文字应当换行还是应当放大节点？
两者都合理，取决于设计意图，宜与 Agent 的图形语义一起定。

**未修复**，需独立任务。

---

## C5 【P1】节点内边距是绝对值，小字号时把框体撑大三倍

**发现于：** C4 修复后的尺寸复核（2026-11-18）

### 实测

```
文本 'Web 商城'，14pt
  引擎实测宽          1.417"
  合理文字宽估算       0.778"   (CJK 0.194"/字 × 2 + 拉丁 0.097"/字 × 4)
  偏差                +82%
```

拆解后：

```
  文字宽  0.778"
+ padding 0.600"   (NodePaddingX = 0.30，两侧)
= 1.378"           ≈ 实测 1.417"
```

**padding 占总宽的 43%。**

### 根因

`TypeScale.NodePaddingX = 0.30`、`NodePaddingY = 0.22` 是**绝对英寸值**，
不随字号缩放。因此：

| 字号 | 文字宽 | padding | padding 占比 |
| --- | --- | --- | --- |
| 8pt | ~0.44" | 0.60" | **58%** |
| 14pt | 0.78" | 0.60" | 43% |
| 24pt | 1.33" | 0.60" | 31% |

**字号越小，padding 越主导尺寸。** 小字号图的节点会被 padding 撑得远超需要。

### 影响

- C4 修复后，圆形节点需要 `1.417/0.70 = 2.02` 直径，视觉上比矩形节点（1.38×0.70）大
  3 倍，比例失衡
- 若 padding 按字号缩放（例如 `0.30 * fontSize/11`），14pt 时 padding 约 0.38"，
  圆直径降到约 1.65"，与矩形节点比例协调

### 候选修法

padding 改为字号的函数：`padding = basePadding * (fontSize / TypeScale.NodeTitle)`，
并设下限避免小字号时文字贴边。

**未修复**，需独立任务——它会改变所有既有产物的节点尺寸，需要单独的回归验证。

---

## C6 【P1】文本块压住容器边框，且检测器查不出来

**发现于：** 真实制图第三次复测（2026-11-18，HEAD `f3f0f1b`）

### 实测

跨境电商订单图，标题与最上层容器重叠：

```
textBlock title   范围 y [8.20, 8.80]
container edge    范围 y [5.85, 8.76]
                  -> 重叠 0.56"
```

`check-layout` **报告 score=91，无任何相关告警**。

渲染图上可见：标题「跨境电商订单系统」压在「接入层 Edge」容器的上边框上，
与容器标题挤在一起。

### 两个独立问题

**1. 布局没有协调装饰元素与容器**

标题在 `y=8.50`，是根据作者给的位置；容器由成员推导后扩展到 `8.76`，
两者各自合法，合起来冲突。`ClampDecorationsToPage` 只保证装饰元素在**页面**内，
不保证不压住**容器**。

**2. 检测器不检查文本块与任何对象的重叠**

```console
$ grep -c "TextBlocks" src/Vsdx.Engine/LayoutAnalyzer.cs
1
```

`LayoutAnalyzer` 有 `node.overlap`、`group.overlap`、`label.overlaps-node`，
**没有 `text.overlaps-*`**。因此这类问题既不会被阻止，也不会被报告。

### 为什么重要

标题被压住是**可直接观察的交付缺陷**，比分数更能说明问题。
而它是"布局与检测都缺"的双重盲区——分数 91 给人一种没问题的错觉。

### 候选修法

1. 布局阶段：容器扩展后，若与装饰元素冲突，把装饰元素推到容器之外
   （或反之，为标题预留顶部带）
2. 检测阶段：新增 `text.overlaps-group`，至少让它可见

**建议先做 2**：让问题可见比自动修复更安全——自动推挤可能在
用户刻意让标题压在色带上时误判。

**未修复**，需独立任务。

---

## C7 【P1】Writer 默认重算容器，覆盖模型里已定稿的 bounds —— ✅ 已修（提交 93def6a）

**发现于：** C6 修复后复核（2026-11-18）

### 实测

```
模型里  AutoSize=null  容器顶=8.46
写盘后                  容器顶=8.76   (+0.30)
```

### 根因

`OfficeImoDocumentWriter.cs:124`：

```csharp
page.AddToContainer(container.Id, memberIds, resizeToFit: group.AutoSize ?? true);
```

`AutoSize` 为 null 时**默认 true**，库按成员重算容器包围盒。
而同一文件的另一处（`:211`）走显式 bounds 路径时用 `AutoSize = group.AutoSize ?? false`——
**两处默认值相反**。

### 后果

1. 模型层算好的容器几何在写盘时被改写，`DiagramLayout.Apply` 的结果不再是最终结果
2. 依赖容器坐标的任何后续计算（如 C6 的装饰避让）都会基于错误前提
3. 两个默认值相反，说明这两条路径中至少一条不是有意的

### 候选修法

统一 `AutoSize ?? false`，或让 `DesignSpecApplier` 显式设置 `AutoSize=false`。
**需要先确认 `AddToContainer` 的 resize 是否必要**——它可能是为了容纳成员的边框。

**已修。** 对照实验证明 resize 不必要：

| AutoSize | 模型与磁盘偏差 | 成员包含问题 |
| --- | --- | --- |
| null（原默认） | **+0.30** | 0 |
| false | **0.00** | 0 |
| true | +0.30 | 0 |

默认值统一为 `false` 后，C6 的装饰避让随之生效，`text.overlaps-container` 消失。

---

## C8 【P1】连线标签互相重叠，且无检测

**发现于：** 真实制图第四次复测（2026-11-18，HEAD `913d51b`）

### 实测

同一张跨境电商图，两条边的标签几乎落在一起：

```
gRPC  ~(4.17, 5.80)
REST  ~(3.89, 5.65)     间距 0.32"，而标签宽约 0.5"
```

渲染图上可见：「gRPC」与「REST」上下压叠，读不出来。

### 检测盲区

```console
$ grep -c "label.overlaps" src/Vsdx.Engine/LayoutAnalyzer.cs
1
```

只有一个 `label.overlaps-node`（标签压节点），**没有标签与标签之间的检查**。
因此 `check-layout` 报 91 分，零告警。

### 与 C6 的同构之处

C6 是"文本块与容器重叠检测缺失"，C8 是"标签与标签重叠检测缺失"——
两者都属**检测器只覆盖部分对象对**的结构性问题：

| 对象对 | 是否检查 |
| --- | --- |
| node ↔ node | ✅ `node.overlap` |
| group ↔ group | ✅ `group.overlap` |
| label ↔ node | ✅ `label.overlaps-node` |
| **label ↔ label** | ❌ |
| **textBlock ↔ group** | ✅ 已补（C6） |
| **textBlock ↔ node** | ❌ |

### 候选修法

1. 检测：新增 `label.overlaps-label`，按标签估算包围盒比对
2. 布局：标签避让（在已路由的线段上错开）

**建议先做 1**：与 C6 同样的理由——先让问题可见，避让规则需要更多设计判断
（标签该往哪边错？错多少？）。

**已修**（检测 + 自动避让，提交 de5f465 之后）。

---

## C9 【P1】检测器忽略 `LabelPosition`，与实际标签位置不一致

**发现于：** C8 修复尝试（2026-11-18）

### 实测

给重叠的边设置不同的 `labelPosition`（0.3 / 0.7）：

```console
$ execute --ir ir2.json   (含 labelPosition)
$ check-layout
  label.overlaps-label (2)     <- 仍然报告重叠
```

但 Writer **确实**使用了它（`:391`）：

```csharp
var position = Math.Clamp(edge.LabelPosition ?? 0.5, 0.05, 0.95);
connector.LabelPlacement = new VisioConnectorLabelPlacement(position, 0, 0.10)
```

而检测器（`LayoutAnalyzer.cs:153` 与 `:188`）**固定使用**：

```csharp
var labelCenter = PointAlong(path, 0.5);
```

### 后果

1. **用户无法通过 `labelPosition` 消除检测器报告的重叠**——他改了，检测器看不见
2. 检测结论**不反映实际产物**：实际标签在 0.3 处，检测器按 0.5 处判断
3. 这是"检测与实现参数不一致"，比标签重叠本身更根本

### 为什么难发现

`labelPosition` 有默认值 0.5，所以**大多数图的检测是正确的**。
只有显式设置过的图才会出现偏差——而那种图正是用户试图修复重叠的图。

### 候选修法

检测器读取 `edge.LabelPosition ?? 0.5`，与 Writer 使用同一个值。

**这一条必须先修**：否则 C8 的布局侧修复（错开标签）无法验证——
检测器会继续按中点判断，无法确认错开是否有效。

**未修复**。

---

## C10 【P2】IR 不携带 `LabelPosition`，协议入口无法移动标签

**发现于：** C8 布局侧修复尝试（2026-11-18，HEAD `f3a381e`）

### 实测

C9 修复后，检测器已能读取 `edge.LabelPosition`。但通过 IR 设置它**无效**：

```console
$ execute --ir ir2.json          # e1/e2 设 0.25，e3/e4 设 0.75
$ check-layout
  label.overlaps-label (2)       # 与不设置时完全相同
```

### 根因

```console
$ grep -c "LabelPosition" src/Vsdx.Protocol/DiagramIr.cs
0
```

`IrEdge` 没有该字段。模型（`Vsdx.Model.Edge`）有，Writer 会用，检测器现在也会读——
**但协议入口不传递它**，所以经 `execute` 路径设置的值在 `DiagramIrConverter` 处就丢了。

### 三处已通、一处断

| 层 | 支持 `LabelPosition` |
| --- | --- |
| `Vsdx.Model.Edge` | ✅ 字段存在 |
| `OfficeImoDocumentWriter` | ✅ 用于 `LabelPlacement` |
| `LayoutAnalyzer` | ✅ C9 后已读取 |
| **`Vsdx.Protocol.IrEdge`** | ❌ **无字段** |

### 影响

经 `execute` 路径（Agent 的主路径）**无法调整标签位置**，
因此 C8 的"标签重叠"在协议层无解——只能靠自动避让，或走 `create` 路径手工写模型。

### 候选修法

给 `IrEdge` 加 `LabelPosition` 并在 `DiagramIrConverter` 中传递。
属协议扩展，需按任务书 §4.9 评估兼容性：**新增可选字段是向后兼容的**。

**未修复**。

---

## C11 【P1】Reader 不读回 `LabelPosition`，检测磁盘文件时永远用默认值

**发现于：** C10 修复后的真实验证（2026-11-18，HEAD `f3a381e`）

### 实测

C10 修复后，IR 的 `labelPosition` 能到达模型。但经 `execute` 写盘后再读回：

```
e5: labelPosition=None label=SQL
e7: labelPosition=None label=index
```

**写进去的值读不回来。**

### 链路

| 层 | `LabelPosition` |
| --- | --- |
| `IrEdge` | ✅ C10 后已加 |
| `DiagramIrConverter` | ✅ C10 后已传 |
| `Vsdx.Model.Edge` | ✅ 有字段 |
| `Writer` | ✅ 用于 `LabelPlacement` |
| `LayoutAnalyzer` | ✅ C9 后已读 |
| **`OfficeImoDocumentReader`** | ❌ **不读回** |

### 后果

`check-layout` 与 `execute` 之后的质量检查都读磁盘文件，
因此 `LabelPosition` 在那里**永远是 null → 默认 0.5**。

所以：
1. 用户设置 `labelPosition` 移动了标签，检测器看不见
2. C9 的修复对磁盘路径**仍然无效**——它让分析器读了字段，但字段在磁盘模型里是空的
3. C8 的布局侧修复无法通过 `check-layout` 验证

### 候选修法

Reader 从 `connector.LabelPlacement.Position` 读回 `LabelPosition`，
与 Writer 写入的值对应。

**未修复**。这一条与 C9 是同一问题的两段：C9 让分析器读字段，C11 让字段真的在磁盘模型里。

---

## C12 【P1】IR 声明顺序被忽略，节点按 id 字母序排列

**发现于：** 真实制图第五次复测（2026-11-18，HEAD `2a4a022`）

### 实测

跨境电商图数据层三个节点，实际排布与声明顺序无关：

```
IR 声明顺序: pg(idx 6), redis(7), es(8)
实际输出:   es(x=1.68), pg(x=3.77), redis(x=5.70)
```

即 **es 被排到了最左**，尽管它是最后声明的。

### 链路

1. `DiagramIrConverter.cs:311` 给所有节点 `Bounds(0, 0, 2.0, 0.9)` —— **X 全为 0**
2. `LayeredLayoutEngine.cs:233` 排序：`OrderBy(n => n.Bounds.X).ThenBy(n => n.Id)` —— 第一键全相等
3. 结果退化为**按 id 字母序**：`es < pg < redis`

**IR 的节点声明顺序从未被使用。**

### 后果

字母序与语义无关，产生无谓的连线交叉。实测该图有 5 处交叉：

```
e4: gw(6.5) -> pay(3.4)
e5: order(1.5) -> pg(3.8)
e6: order(1.5) -> redis(5.7)
e7: pay(3.4) -> es(1.7)      <- 与 e5 交叉
e8: stock(5.2) -> es(1.7)    <- 与 e5 交叉
```

若数据层按声明顺序 `pg, redis, es` 排列，`pay->es` 的终点在右侧，
与 `order->pg` 不再交叉。

### 为什么这是缺陷而非设计选择

**字母序不是任何一种设计意图**。作者写 IR 时的顺序是有意义的
（先声明主库、再缓存、再检索），而引擎把它替换成了一个与语义无关的顺序。

### 候选修法

三个层次，从保守到激进：

1. **保留声明顺序**：`DiagramIrConverter` 给节点分配递增的初始 X（如 `idx * 0.01`），
   使 `OrderBy(X)` 恢复为声明序。**改动最小，不引入新算法。**
2. **重心排序**：按相邻带连接节点的平均 X 排序（Sugiyama 标准做法），进一步减少交叉。
3. 两者结合。

**已修（方案 1）**：转换器给节点递增初始 X。实测交叉 5 -> 1，score 91 -> 95，
数据层顺序从字母序 es/pg/redis 恢复为声明序 pg/redis/es。

方案 2（重心排序）**已尝试并回退**（2026-11-18）。

### 尝试与结果

给带内排序加重心平局打破：节点按其在**相邻带的邻居平均 X** 排序。

```console
基线（字母序/作者序）   总交叉 50
加单轮重心后            总交叉 53   <- 更差
  microservices/complex  2 -> 5
```

### 为什么失败

`LayeredLayoutEngine` 按 `bands.OrderBy(b => b.Key)` **从下往上单轮**处理。
处理某一层时，只有下方层的位置已定，**上方层仍是初始值**。

重心因此混用了新坐标与旧坐标，可能比原来的稳定顺序更差。

标准 Sugiyama 做法需要**多轮迭代**（上→下、下→上反复若干次直到稳定），
单轮不可靠——这次实验印证了这一点。

### 结论

**已回退**，产品代码无改动。若要做，需要：
1. 多次迭代直到交叉数不再下降
2. 每一轮后重新评估实际交叉数，而不是只看目标函数

属独立任务，且**必须**用 18 个基准的交叉总数作为接受标准——
本次就是因为先写测试再量基准，才发现它在真实图上更差。

---

## C13 【P2】图例文字与 palette 解耦，换配色后图例说谎

**发现于：** 新场景复测（部署拓扑，2026-11-18，HEAD `55efbb7`）

### 现象

IR 里手写的图例：

```json
{"id":"legend","text":"蓝=入口  绿=应用  橙=数据","role":"legend"}
```

配 `technical` palette 后，实际颜色是：

```
gateway  #FFC000  (黄)
external #EDEDED  (浅灰)
service  #BFBFBF  (中灰)
```

**图例说"绿=应用"，实际是灰色。**

### 根因

图例是**纯文本**，与 palette 没有任何关联。换 palette 时文字不会跟着变。

这不是引擎的 bug——引擎没有"理解图例语义"的能力。但它是**真实使用者会踩的陷阱**：
同一个 IR 配不同 palette，图例只有一套文字。

### 为什么值得记录

这是**协议表达能力的边界**：`TextBlock` 只有静态文本，没有"颜色引用"概念。

若要让图例跟随 palette，需要：
1. IR 支持图例项与角色绑定（如 `{"role":"service","label":"应用"}`）
2. 由 spec 渲染时替换为实际颜色名

属协议扩展，**不在当前范围**。

### 立即可做的

文档/Skill 中提示：**换 palette 时需同步改图例文字**。
或建议图例使用中性表述（如"三类节点"）而非颜色名。

**未修复**，属表达能力限制而非缺陷。

---

## C14 【P1】无容器图被排成单列，装饰元素决定页面宽度

**发现于：** 数据流图复测（2026-11-18，HEAD `1512cb5`）

### 现象

12 节点 / 12 边 / 无容器的数据流图：

```console
$ check-layout
  score=98/100
  page utilisation 86%
```

**但渲染图观感很差**：页面 `11.00 x 23.30`（宽高比 1:2.1），节点排成一条竖线，
右侧大片空白，标题浮在空白处。

### 数据

```
节点 x 范围   [0.84, 5.10]      <- 只占 4.26"
标题 x=6.95   范围[3.45,10.45]  <- 撑到 10.45"
页面宽 11.00
```

**页面宽度由标题的 X 决定，而不是由内容宽度决定。**

标题 y=9.50 落在节点列表外（节点 y 范围 2.27~21.82），所以它既不在顶部也不在中间，
而是浮在左侧图形的右边。

### 两个独立问题

**1. 无容器图退化为单列**

没有容器时所有节点归入同一 band，带内拓扑序把它排成一列。
对数据流这种**线性链路**勉强可读，但对"web/app 双源汇入 sdk"这类结构，
两个源被上下排列而非并排。

`LayeredLayoutEngine` 有 `MaxNodesPerRow` 选项，但显然未在此情形生效。

**2. 装饰元素坐标与布局结果不匹配**

用户按"图会横向展开"给了 `x=7`，实际排成竖列，标题就跑到空白处。

`ClampDecorationsToPage` 只保证不出页面，不保证与内容协调。

### 为什么 98 分却难看

`page utilisation` 用**内容包围盒**算——标题把包围盒撑宽，所以 86% 看起来正常。
**分数无法反映"内容排成一条线、其余是空白"这种形态问题。**

这是第 N 次印证：**分数不等于观感**。

### 候选修法

1. 无容器时也按 `MaxNodesPerRow` 折行，而不是无限增长一列
2. 装饰元素在布局后重新居中于**内容包围盒**之上（而非沿用作者 X）

**未修复**。这两条都会改变既有产物，需独立验证。

---

## C15 【P0】`LayoutDirection` 的语义与命名相反

**发现于：** C14 修复过程中的对照实验（2026-11-18，HEAD `5cfcc85`）

### 实测

同一张数据流图，只改 `direction`：

```console
$ --spec '{"layout":"layered","direction":"leftToRight"}'
  页面 11.00 x 23.30   比 2.12   <- 竖向

$ --spec '{"layout":"layered","direction":"topToBottom"}'
  页面 23.30 x 11.00   比 0.47   <- 横向
```

**`leftToRight` 产出竖向，`topToBottom` 产出横向——两者都与名字相反。**

### 根因

`LayeredLayoutEngine` 天然把 band 从下往上堆叠，产出**竖向**布局。
T0 的实现是：`TopToBottom` 时**转置**整页，于是竖向变横向。

```csharp
if (options.Direction == LayoutDirection.TopToBottom && strategy != LayoutStrategy.Manual)
    DiagramLayout.Transpose(diagram);
```

所以：
- `LeftToRight`（不转置）= 引擎的默认竖向
- `TopToBottom`（转置）= 横向

**枚举只有两个名字，没有任何注释说明"该方向指什么"。**

### 为什么 T0 时没发现

T0 的验收标准是"两种方向的产出不同"和"xspread/yspread 互换"——
**这两条都成立**，但从未检查"leftToRight 是否真的从左到右"。

验收断言测的是"变化"，不是"方向正确"。

### 影响

调用方写 `direction: "leftToRight"` 想要横向流程图，得到的却是竖列。
这是**协议字段给出与名字相反的语义**，比不生效更糟——不生效容易被发现，
反着生效会被当成"设计如此"。

### 候选修法

选项 A：**转置逻辑反过来** —— `LeftToRight` 时转置（使横向），`TopToBottom` 不转置。
选项 B：**改枚举名** —— 但破坏协议兼容。
选项 C：**保持行为，改文档** —— 最省事但最不诚实。

**建议 A**：它让名字与行为一致，且不改变协议字段。

### 已尝试（方案 A）并回退

反转转置条件后：

```
build  0 warning
tests  35 项失败 / 561 通过
```

**35 项失败全部是方向相关测试**（`DirectionTests`、`DirectionMultiPageTests`、
`DirectionNestedGroupTests`）。它们验证的是旧实现细节——「`TopToBottom` 会转置」——
而不是「`TopToBottom` 产出竖向」这个应有语义。

### 为什么不擅自改

这是**破坏性语义变更**：所有显式设置 `direction` 的既有 spec 产出会翻转。

按任务书 §4.9，改变既有协议行为需评估；而 35 个测试需要**按新语义重写**，
不是小改。这超出「修一个缺陷」的范围，属于**产品行为决策**。

### 已完成（方案 A）

按负责人 D1 的既有授权实施——原文："接受修复，不增加兼容开关……
修复后旧 spec 的输出可能改变，必须在报告和发布说明中明确披露。"

实施时发现**三处连带问题**，都已修：

**1. 转置后文字溢出。** 转置交换 W/H，但文字是按转置前尺寸测的，
宽标签塞进变窄的框就溢出（`text.overflow-horizontal`，硬失败）。
修法：转置后重新 `DiagramSizer.Apply`。

**2. `create` 路径被静默转置。** `VsdxEngine.Create` 用 `new LayoutOptions()`，
默认 `LeftToRight` 现在会转置——**每个 `create` 产物都被旋转了**。
实测 `network/medium` 从 98 分掉到 0（容器重叠）。
修法：`create` 路径显式指定 `TopToBottom`（引擎自身方向）。

**3. Manual 语义随之变化。** 原先只有 `TopToBottom` 在 manual 下被忽略，
现在两个方向都忽略——因为 manual 保留作者坐标，与方向无关。

### 验证

```console
数据流图：
  leftToRight  23.30x11.00  比 0.47   <- 横向 ✓
  topToBottom  11.00x23.30  比 2.12   <- 竖向 ✓
```

18 个基准全部通过；592 项测试通过。

**这一条优先级曾高于 C14：C14 是"布局不理想"，C15 是"语义与名字相反"。现已修复。**
