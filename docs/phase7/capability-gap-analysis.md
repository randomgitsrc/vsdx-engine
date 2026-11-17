# Phase 7 能力差距分析（Gate C 输入）

**依据：** `phase0-6-implementation-audit.md` 与 `baseline-report.md` 的实测证据
**用途：** 确定 P0/P1/P2 改造范围，供 Gate C 决策
**日期：** 2026-11-17

---

## 1. 差距全景

审计共发现 10 项差距。按任务 §6.1 的优先级定义分类：

| # | 差距 | 优先级 | 当前状态 |
| --- | --- | --- | --- |
| G1 | 嵌套容器结构丢失 | **P0** | ✅ **已修复** |
| G2 | create 流水线顺序错误 | **P0** | ✅ **已修复** |
| G2b | 层内排序反转 | **P0** | ✅ **已修复** |
| G11 | 页面过小下限导致窄图留白 | **P0** | ✅ **已修复** |
| G3 | 无混合布局（区域内不同策略） | P1 | 未修 |
| G4 | 无泳道布局 | P1 | 未修 |
| G5 | 无局部重排 | P1 | 未修 |
| G6 | 扇出布线质量差 | P1 | 未修 |
| G7 | 无视觉组件（标题/图例/注释） | P1 | 未修 |
| G8 | 页面利用率仅 info 级 | P1 | 部分（改为断言但未阻断） |
| G9 | `Node.Type` 死字段 | P2 | 未修 |
| G10 | 无 L4 Visio 验证 | 无法验证 | 环境限制 |
| G12 | 无取消/超时机制 | P2 | 未修 |

---

## 2. 已修复项（P0）

### G1 嵌套容器结构丢失

| 项目 | 内容 |
| --- | --- |
| 根因 | OfficeIMO.Visio **无公开的 shape parenting API**（`VisioShape.Parent` 为 internal setter）；写入器因此把子容器当兄弟，读取器也从不恢复 `ParentId` |
| 影响 | 部署图、集群图、多级分区图的层级静默丢失 |
| 修复 | 新增 `VisioNestingBridge`：保存后按容器标题定位 shape，写入 `Parent` cell；读取端直接解析 page XML 恢复 `ParentId` 并把子容器的成员上提给父容器 |
| 证据 | `Phase7RegressionTests.G1_*`（3 项）；实测 `Parent cells: 0 → 1`、`parentId: None → outer` |
| 风险 | 依赖 XML 后处理，OfficeIMO 升级若改变容器 shape 的文本/ID 规则需复核 |
| 回归 | 235 项测试全通过；18/18 基准不受影响 |

### G2 create 流水线顺序错误

| 项目 | 内容 |
| --- | --- |
| 根因 | `FitPage` 在布局**之前**执行，按作者旧坐标裁剪页面，随后布局把节点搬走 |
| 影响 | 实测 microservices/complex 右侧空白 **7.09"**，机器评分仍 90 |
| 修复 | 管线重排：`text → place → containers → recenter → fit page → route` |
| 证据 | 右侧空白 7.09" → **0.86"**；评分 90 → 98 |
| 风险 | 低——纯顺序调整，各步语义不变 |

### G2b 层内排序反转

| 项目 | 内容 |
| --- | --- |
| 根因 | 按"前驱节点平均 X"排序，源点无前驱故得分 `double.MaxValue`，排到**最后** |
| 影响 | 每条简单链 A→B 都渲染成 B 在 A 左侧；排序还依赖作者原始坐标 |
| 修复 | 改为图内拓扑序（入边在本层内的前驱全部放置后才放置），作者 X 仅用于破平 |
| 证据 | `G2b_ChainReadsLeftToRight`、`G2b_OrderingIsIndependentOfAuthorX` |
| 风险 | 低——排序结果更稳定 |

### G11 页面下限导致窄图留白

| 项目 | 内容 |
| --- | --- |
| 根因 | `MinPageWidth = 8.0` 强制窄内容也放进 8" 宽的页面 |
| 影响 | 垂直堆叠的图（如三节点链）右侧空白超过 2" |
| 修复 | 下限降为 4.0 × 3.0，仅保证页面可用，不强加宽高比 |
| 证据 | `G2_PageIsFittedAfterPlacement` |
| 风险 | 极小的图会得到很小的页面——这是期望行为 |

---

## 3. 未修复项与建议

### G3 混合布局（P1）

**问题：** 整页只能使用一种布局策略。任务 §6.3 明确要求"不能将不同区域内的所有节点
强行放入同一种布局算法"。

**实测证据：**

```console
$ grep -n "per.container\|each container separately" src/Vsdx.Engine/*.cs
（无结果）
```

`DiagramLayoutEngine.Apply()` 对整页调用一次 `engine.Layout(page, options)`。

**建议方案（最小改造）：**

1. `LayoutOptions` 增加 `IReadOnlyDictionary<string, LayoutStrategy>? StrategyByGroup`
2. `LayeredLayoutEngine` 的 `LayoutBanded` 分支改为：每个容器用其指定策略算内部布局，
   得到该容器的内容包围盒，再由外层按行堆叠
3. 无指定策略的容器沿用页面默认

**工作量估计：** 中等（改动集中在 `LayeredLayoutEngine.LayoutBanded`）
**风险：** 容器内布局与全局堆叠的坐标系需要仔细转换；须有 6 族基准做回归
**验收标准：** 一个案例中同时出现 tree 区域 + flow 区域 + grid 区域，且硬约束全过

### G4 泳道布局（P1）

**问题：** 无 `Lane` 概念，跨职能泳道图无法表达。

**实测证据：**

```console
$ grep -rniE "swimlane|laneId" src/ --include=*.cs
（无结果）
```

**建议方案：**

泳道不是新的几何算法，而是**带语义的容器**。建议：

1. `Group` 增加 `Kind = "lane"` 与 `LaneIndex`
2. `LayeredLayoutEngine` 对 lane 容器按 `LaneIndex` 纵向堆叠，**每条泳道独立横向排列**
3. 跨泳道连线走正交路由（已有）

**工作量估计：** 小到中（复用容器机制）
**风险：** 低
**验收标准：** 2 条泳道各含 3 个节点、含 2 条跨泳道连线，渲染后泳道标题清晰、连线不穿标题

### G5 局部重排（P1）

**问题：** `ImpactAnalysis` 能算出"该重排哪个容器"，但**没有只重排单容器的能力**，
只能整页重排，波及无关区域。

**建议方案：**

新增 `DiagramLayoutEngine.ApplyToGroup(diagram, groupId, strategy)`：
只对指定容器内的成员重新布局，并保持该容器在原页面中的位置锚点不变。

**工作量估计：** 小（现有 per-container 布局逻辑可复用）
**风险：** 低——限定在一个容器内
**验收标准：** 只改一个容器后，其它容器的节点坐标逐字节不变

### G6 扇出布线质量差（P1）

**问题：** 多条连线从同一节点发出时，路由各自为政，形成长距离平行走廊。

**实测证据：** microservices/complex 中 7 条线从网关扇出，绕行比 1.26–1.41，
虽在 2.0 阈值内，但视觉明显不专业（见 `baseline-report.md` §4.1）。

**根因：** `OrthogonalRouter` 对每条边独立求解，**没有总线/共享干线的概念**。

**建议方案：**

1. 检测"同一源点的多条边"→ 合并为一条干线 + 分支（trunk-and-branch）
2. 或至少让扇出边共享同一通道的偏移，形成等间距平行线

**工作量估计：** 中大（布线器核心改动）
**风险：** 高——布线器是当前最复杂的模块（522 行），改动需完整回归
**验收标准：** 7 条扇出边的绕行比降到 1.2 以下，且 `edge.crossing` 不增加

### G7 视觉组件（P1）

**问题：** 无图表标题、图例、注释框。

**建议方案：** 在 `Node` 之外增加 `Decorations` 集合（title / legend / annotation），
由同一套 `DiagramSizer` 参与排版。不引入新概念，复用现有 Shape 写入路径。

**工作量估计：** 小
**风险：** 低
**验收标准：** 能生成带标题和图例的图，且标题不遮挡容器

### G8 页面利用率仅 info 级（P1，部分改善）

**当前：** `page.underused` 仍是 `info`，不阻断交付。

**建议：** 改为 `warning`，并新增一条硬规则：内容占页面 < 15% 时判为 `error`。

**风险：** 可能误伤"故意留白"的设计稿。建议做成可配置阈值，默认 15%。

### G9 `Node.Type` 死字段（P2）

语义类型字段存在但无人读取。建议在 `MasterCatalog.ResolveMaster` 中优先用
`Node.Type` 选择形状族（如 `database` → `Process`），让语义真正驱动视觉。

### G12 无取消/超时机制（P2）

任务 §11 明确要求。当前 `RenderRequest`、`WriteOptions` 均无 `CancellationToken`，
CLI 无超时参数。建议先给 CLI 加 `--timeout`，再把 token 贯穿 `VsdxEngine` 各方法。

---

## 4. 建议的改造顺序

按"影响交付质量 × 风险"排序：

| 顺序 | 项 | 理由 |
| --- | --- | --- |
| 1 | **已完成** G1/G2/G2b/G11 | P0 正确性，已修复 |
| 2 | G8 页面利用率硬约束 | 让"大片空白"不能再通过；成本极低 |
| 3 | G3 混合布局 | 任务反复强调，且是"专业制图"的核心缺口 |
| 4 | G5 局部重排 | 修复机制的必要补全，风险低 |
| 5 | G4 泳道 | 覆盖第 4 类图形，复用容器机制 |
| 6 | G7 视觉组件 | 提升"成品感"，风险低 |
| 7 | G6 扇出布线 | 收益大但风险最高，需完整回归 |
| 8 | G9/G12 | P2，最后处理 |

---

## 5. 不建议做的事

任务 §13 Gate C 要求"不要为了实现本 Goal 列出的所有建议模块而强行改造"。
以下明确**不建议**在当前阶段做：

| 建议 | 不做的理由 |
| --- | --- |
| 引入 Graphviz / MSAGL 替换自研布局 | 自研四策略已覆盖 18 个基准且硬约束全过；引入进程/JVM 依赖换取边际收益不划算。若 G6 需要更好的布线算法再评估 |
| 重写 `OrthogonalRouter` | 当前布线在避障上正确，问题只在多线汇聚。局部增强优于重写 |
| 把 `Diagram.Type` 扩展成完整设计语言 | 那属于 Figure Agent 的 DesignSpec 职责，Engine 不应承担 |
| 为嵌套容器引入新的层级模型 | 现有 `ParentId` 语义已足够，问题在实现而非模型 |
| 实现 L4 验证的替代方案 | 无法验证就是无法验证，不得用其它测试替代 |

---

## 6. 已知的架构性问题（需说明但不立即重构）

### 6.1 VSDX 后处理补丁的累积

当前有三处依赖"保存后 patch XML"：

| 桥 | 目的 | 原因 |
| --- | --- | --- |
| `VisioMetadataBridge` | 写模型元数据到 docProps | OfficeIMO 无自定义属性 API |
| `VisioNestingBridge` | 写 `Parent` cell | OfficeIMO 无 parenting API |
| （未来）视觉组件若需主题 | — | — |

**这是架构债**：每次 OfficeIMO 升级都要复核 patch 逻辑。
但**不建议现在重构**——三处 patch 都有测试覆盖，且改动范围极小（只增 cell，不删内容）。
若 OfficeIMO 未来暴露相应 API，应逐步替换。

**触发重构的条件：** patch 数量超过 5 处，或某次 OfficeIMO 升级导致 patch 失效。

### 6.2 布线器与布局器的耦合

`LayeredLayoutEngine` 内部调用 `DiagramLayout.Apply`（算容器边界），
`VsdxEngine.Create` 又调一次。重复计算且顺序敏感——G2 的缺陷就出在这里。

**建议：** 明确"谁在什么时候算容器边界"的契约，写进 `engine-api-contract.md`。

---

## 7. 对任务书的一项反馈

任务 §4.1 要求十类图形中含"跨职能泳道图"。但泳道图本质上是
**"带语义的横向分区 + 分区内的流程"**，与现有容器机制高度同构。

建议在 Gate C 决策时确认：泳道是否必须作为一等概念（`Lane` 类型），
还是可以用 `Group.Kind = "lane"` 表达。后者成本更低且不破坏兼容性。
