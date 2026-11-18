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

**尝试过的修法**：在推导中加 `HeadingClearance = 0.12"`。

**失败原因**：`FlowAndTreeLayoutEngine.LayoutBanded` 的推进距离是
`rowHeight + heading + padBottom`，而 `rowHeight` 已含容器总高，**本来就重复计算**。
加 clearance 后重复量增大，`compare` 的转置候选开始报 `group.overlap`
（`CandidateTests.Compare_TransposedCandidateIsStillDeliverable`）。

**当前状态**：
- 检测器会报告（`container.heading-collision`，warning）
- 布局不修复，标题仍可能被压
- 分数因此从 92 降到 80（检测变严格，图纸未变好）

**若要修复**：需先消除 `LayoutBanded` 的重复计算，再引入 clearance。
两者必须同时改，否则会引入容器重叠。

## 2. 图例可能与连线标签视觉冲突

图例文本块按作者坐标放置，`ClampToPage` 只保证在页面内，不避让**连线标签**——
而连线标签是渲染时绘制的，不在模型几何中，`check-layout` 看不到。

**当前状态**：已知，未修。

## 3. L4 未验证

见 `docs/limitations.md`。这是唯一影响核心承诺的未解项。
