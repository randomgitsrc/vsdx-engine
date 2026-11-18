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

**若要继续修**：需要让 `CandidateComparison` 的转置路径也感知 clearance，
否则转置候选会一直差这一小段。

## 2. 图例可能与连线标签视觉冲突

图例文本块按作者坐标放置，`ClampToPage` 只保证在页面内，不避让**连线标签**——
而连线标签是渲染时绘制的，不在模型几何中，`check-layout` 看不到。

**当前状态**：已知，未修。

## 3. L4 未验证

见 `docs/limitations.md`。这是唯一影响核心承诺的未解项。
