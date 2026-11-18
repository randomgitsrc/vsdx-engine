# 已知未解问题

## 1. 容器标题与首行成员相接

**现象**：容器顶部为标题预留 `ContainerHeading`（0.46"），成员顶端恰好落在标题底边上，
渲染时标题文字被成员框压住。

**根因**：`DiagramLayout.Apply` 里 `top += heading` 让成员顶与标题底数学上必然相等：

```
成员顶   = top
容器顶   = top + heading
标题底   = 容器顶 - heading = 成员顶
```

**已修复的部分**：`LayoutBanded` 的重复计算。

原来：
```
node.Y = y + rowHeight/2          # rowHeight 是成员高度
rowHeight = max(rowHeight, derived.H)   # 之后才提升为容器高度
y -= rowHeight + heading + padBottom + gap   # 又加了一次 envelope
```

节点用成员高度定位、推进用容器高度**再加** envelope，两者基准不一致，带宽随行序变化。
现在改为：`node.Y = y - nodeHeight/2`，推进 `y -= advance + gap`，`advance` 是容器总高。
`BandSpacingInvariantTests` 保证相邻容器不重叠、间距一致。

**仍未修复的部分**：容器顶部预留 clearance。

试过在推导里加 `HeadingClearance = 0.12"`，并同时做过节点重定位，但
`CandidateTests.Compare_TransposedCandidateIsStillDeliverable` 仍报 `group.overlap`。
转置候选的几何表明容器在转置后相差恰好 `0.03"`。

**当前状态**：
- `HeadingClearance` 只用于**band 内节点重定位**（让成员不压在标题基线上）
- 容器 box 本身不加 clearance，因此标题仍可能被首行成员压住
- 检测器会报告（`container.heading-collision`，warning）
- `deploy-topology` 因此是 80 分而非更高——**检测变严格了，图纸没变好**

**2026-11-18 二次诊断（精确数据）**

在测试里临时打印实际几何，得到转置候选的确切位置：

```
g1  y[0.56, 2.66]  x[0.55, 3.55]   h=2.10
g2  y[2.63, 4.73]  x[0.55, 1.70]   h=2.10
页面 4.10 x 5.15
```

两个容器**各高 2.10**，纵向需要 `4.20 + 间隙`，而页面只有 `5.15`。
它们纵向重叠 `0.03`，横向也重叠（X 区间相交）。

**这不是 clearance 独有的问题**：`LayoutBanded` 用 `y`（下一个 band 的起点）作为页面高度，
而容器以自身为中心，最后一个容器会向下越过 `y` 半个自身高度。加了 clearance 后容器变高，
越过量增大到压过间隙，才暴露出来。

**试过但没解决的**（各一次，均回退）：
1. 页面高度改为 `y - lowest + margin` —— `lowest` 是绝对坐标，公式错
2. 页面高度改为 `topMost - lowest + margin*2` —— 仍报重叠
3. 节点在容器内重定位 —— 无效

**结论**：这需要一次系统性的页面尺寸重算（含所有对象、含转置路径），
不是加一个常量能解决的。**当前保持 clearance 不进入容器 box**，标题可能被压，
检测器会报告。这比引入容器重叠更好。

## 2. 图例可能与连线标签视觉冲突

图例文本块按作者坐标放置，`ClampToPage` 只保证在页面内，不避让**连线标签**——
而连线标签是渲染时绘制的，不在模型几何中，`check-layout` 看不到。

**当前状态**：已知，未修。

## 3. L4 未验证

见 `docs/limitations.md`。这是唯一影响核心承诺的未解项。

## 4. `repair` 的接受准则允许分数大幅下降

**实测（2026-11-18，cbo 场景）**：

```
baseline score 86  ->  final score 70
问题数 9 -> 7
decision: Accepted
```

`VersionStore.Evaluate` 的准则是「**没有引入新的硬约束就接受**」。这个准则本身合理——
`repair` 的目标是消除指定问题，不是抬高总分。

**但实际效果值得质疑**：

- 目标问题 `container.heading-collision` 在修复后**依然存在**，只是换了几个节点
  （`pg`/`redis`/`es` → `gw`/`stock`/`es`）
- 问题数从 9 降到 7，但分数从 86 降到 70

**报告是诚实的**：`actualGain` 明确写了 `问题数 9 → 7，分数 86 → 70`，
没有隐藏退步。问题在于接受准则没有考虑分数。

**若要收紧**：在 `Evaluate` 中拒绝「分数下降超过阈值」的候选。阈值取值需要判断，
且会改变 `repair` 的行为，属产品决策，未擅自实施。

## 5. 泳道标题预留（本轮尝试失败，已回退）

**现象**：泳道图 `order-swimlane` 的**全部 6 个节点**都落在泳道标题区，
占 `100 → 88` 的全部扣分。

**根因（已确认）**：`SwimlaneLayoutEngine` 先放置节点、再加 envelope：
```csharp
var (width, height) = PlaceRow(row, y, options);   // 节点以 y 为中心
double envelope = Heading + PaddingBottom;
y += height + envelope + BandGapY;                 // 标题留给下一条泳道
```
`LayoutBanded` 有同样的错误，已在轮 25 修复；泳道引擎漏了。

**四次尝试，全部回退**：

1. 调整泳道内的放置顺序（先留 envelope）—— 被 `DiagramLayout.Apply` 抹掉
2. 在 `DiagramLayout` 全局加 `HeadingClearance` —— 让 `Compare_TransposedCandidateIsStillDeliverable`
   报 `group.overlap`（两次容器差 0.03"，与 clearance 的量级一致）
3. 修 `LayoutBanded` 的 `totalHeight` 估算 —— 仍失败
4. 只在泳道引擎内预留 + 推导后扩展 lane 的 box —— 单元测试通过，但真实 3 泳道场景仍有冲突

**当前状态**：已回退到 HEAD，905 项测试通过。泳道标题问题**未修**，检测器会报告。

**若要继续**：真实场景与单元夹具的差异是关键线索——2 条泳道的夹具通过了，
3 条泳道、不同 `order` 的真实场景没有。需要先查清这个差异，而不是再改一次放置逻辑。

## 6. `edge.diagonal-across-band` 曾不可达（已修）

**现象**：`LayoutAnalyzer.Analyze` 先调 `OrthogonalRouter.Route`，后者对被路由的边
无条件写 `edge.Kind = "rightAngle"` 并填充 waypoints。而检查的前置条件是
「无 waypoints 且 `Kind` 非 rightAngle」——路由后所有边都不满足，**检查永不触发**
（84 次场景遍历确认零次）。

**修复**：`Analyze` 在路由**之前**记录哪些边是作者画的自由直线，把这个集合传给
`AnalyzePage`；检查据此判断，而不是读路由后的模型状态。签名不变（内部捕获）。

**关键细节**：路由**不会**改写只穿过容器、不穿过节点的直线——`OrthogonalRouter` 只在
`PathHitsShapes` 为真时才绕行。所以这类边在分析后仍是 `straight`，正是检查该管的情形。

**同时修复**：`benchmarks/faults/fault-cross-region.json` 原本四个节点全无容器，
不可能触发。已重建为三个容器 + 一条斜穿直线的真实用例，并登记进期望码表。

## 7. 页面利用率把装饰算进内容，掩盖了稀疏的图形

**实测（`checkout-sequence`）**：

| 度量 | 数值 |
| --- | --- |
| 报告利用率 | **84%** |
| **仅节点**的包围盒 | `11.65 × 1.85` → **23%** |
| 页面 | `13.05 × 7.05` |

渲染图上约 **70% 是空白**，而报告说 84%。

**根因**：`pageUtilisation` 用「所有可见对象」的包围盒计算，包含独立文本块。
该图有一个 `note` 在 `y=6.35`、`title` 在 `y=4.35`，而节点全在 `y≈0.8~1.5`。
两个文本块把内容框撑到整页高度，节点本身只占底部一条。

**这不是错误的分母，是错误的分子**：文本块确实是页面的内容，但它们的位置不代表
图形本身的密度。

**处置**：未改。改利用率的定义会影响全项目所有分数（与权重问题同类），属产品取舍。
若要改，可考虑在 `LayoutReport` 中同时给出 `nodeUtilisation`，让调用方自行选择。
