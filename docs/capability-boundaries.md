# 能力边界（2026-11-18，HEAD `bf9a0ef`）

本文件说明 **vsdx-engine 能画什么、不能画什么**，依据是对五种结构的实测。

## 一、支持的布局策略

| 策略 | 排列方式 | 适用 |
| --- | --- | --- |
| `Layered` | 按图深度分层，层内横向排列 | 架构图、部署拓扑、网络图 |
| `Flow` | 按边拓扑序逐个排列，超宽折行 | 流程图、数据流 |
| `Tree` | 按树形层级 | 组织架构 |
| `Grid` | 均匀网格 | 清单、目录 |
| `Swimlane` | 每条泳道一行 | 跨部门流程 |
| `Manual` | 保留作者坐标 | 精确控制 |

`auto` 按 IR 的 `kind` 与是否含容器选择（`LayoutStrategies.SuggestStrategy`）。

## 二、实测结果

可复现场景（`examples/realworld/*.ir.json`），分数用 `check-layout` 实测：

| 场景 | 结构 | 策略 | 分数 | 可用性 |
| --- | --- | --- | --- | --- |
| 跨境电商架构 `cbo` | 3 层容器 | Layered | 92 | ⚠️ 2 处连线穿容器标题 |
| 部署拓扑 `deploy-topology` | 3 层容器 + 扇出 | Layered | 92 | ⚠️ 少量连线交叉（拓扑性，见 §八）|
| 用户数据流 `user-data-flow` | 无容器长链 | Flow | 100 | ✅ |
| **Slack 蜂窝** `slack-cellular` | 1 边缘 + 3 平行单元 | Layered | 96 | ⚠️ **布局无法表达容器并排**；第 2 个外部案例 |
| **Netflix 双活** `netflix-active-active` | 2 区域 + 跨区复制 | Layered | 90 | ⚠️ 跨区边交叉；**唯一外部来源案例** |
| 下单支付时序 `checkout-sequence` | 6 参与者 9 消息 | Flow | 88 | ⚠️ 结构对但不合时序 |
| 订单履约泳道 `order-swimlane` | 3 泳道 | Swimlane | 96 | ✅ 跨泳道连线为斜线 |
| 组织架构 `org-tree` | 3 层树 | Tree | 100 | ✅ |

> 分数会随修复变化。**以 `check-layout` 实测为准**，本表只是当时的快照。

## 三、不能画好的

### 1. 时序图

**没有参与者列与时间轴的概念。** 引擎只有"分层"和"流式"两种排列。

实测冲突：`s1`↔`s9`、`s2`↔`s8`、`s3`↔`s6` 都是**去程与回程**，
两端节点相同，标签中点必然重合——`SpreadLabels` 无法消除，因为没有位置可避开。

时序图需要：参与者固定为列，消息按序号沿时间轴排列，回程消息与去程分列两侧。

#### 尝试过手工摆放（2026-11-18，C23 修复后重测）

用 `layout=manual` + `anchor:{mode:"fixed"}` 精确摆放 5 个参与者（`x=2,5,8,11,14`，`y=10`）
和 8 条消息。**坐标精确保持**（C23 已修），但重叠依旧：

```console
label.overlaps-label (4)
  '1 提交' 与 '8 展示'   <- user↔web 去程与回程
  '2 创建' 与 '7 返回'   <- web↔order
  '3 支付' 与 '6 完成'   <- order↔pay
  '4 扣款' 与 '5 成功'   <- pay↔gw
```

**交替设置 `labelPosition`（0.3 / 0.7）无效**：两条反向连线**是同一条线段**，
沿比例移动只会在同一直线上滑动，中点仍然重合。

`tests/Vsdx.Tests/SequenceFeasibilityTests.cs`（4 项）把这个限制钉住，
若将来连线端点或标签机制变化，测试会提醒。

### 2. 状态机

**没有状态与转移的专用语义。** 状态机的节点是"状态"、边是"转移"，
通常需要初始态/终止态标记、自转移、复合状态。

实测 96 分，结构正确，但布局与普通流程图无异。有环时可读，但环形不会被特别处理。

### 3. 需要精确对齐的图

引擎保证"不重叠、不出界、不裁切"，**不保证**：
- 节点列对齐（允许 ±0.3" 漂移）
- 连线正交（斜线允许）——见下方说明
- 标签不重叠（只能减少，无法完全消除）


## 三附、检测器实际覆盖的问题类别

`check-layout` 与 `execute` 的 `quality` 字段会报告以下代码。**这是引擎能自动发现的问题全集。**

### 节点与容器

| 代码 | 含义 | 级别 |
| --- | --- | --- |
| `node.overlap` | 两个节点包围盒相交 | error |
| `node.out-of-page` | 节点超出页面 | error |
| `group.overlap` | 两个容器相交 | error |
| `group.member-escapes` | 成员落在容器外 | error |
| `group.gutter` | 容器之间间距过小 | warning |
| `container.heading-collision` | 成员进入容器标题区，标题被遮挡 | warning |

### 文字

| 代码 | 含义 | 级别 |
| --- | --- | --- |
| `text.overflow-horizontal` | 文字横向放不下 | **error** |
| `text.overflow-vertical` | 文字纵向放不下 | **error** |
| `text.overlaps-node` | 文本块压在节点上 | warning |
| `text.overlaps-container` | 文本块横跨容器边框 | warning |
| `text.overlaps-text` | 两个文本块重叠 | warning |
| `label.overlaps-node` | 连线标签落在节点上 | warning |
| `label.overlaps-label` | 两个连线标签重叠 | warning |

### 连线

| 代码 | 含义 | 级别 |
| --- | --- | --- |
| `edge.crossing` | 两条连线交叉 | warning |
| `edge.through-node` | 连线穿过节点 | **error** |
| `edge.through-container-heading` | 连线穿过容器标题文字 | warning |
| `edge.diagonal-across-band` | 斜线跨容器 | warning |
| `edge.detour` | 连线绕行过长 | warning |
| `edge.fan-in` / `edge.fan-out` | 扇入/扇出过多 | warning |

### 排布与页面

| 代码 | 含义 |
| --- | --- |
| `row.misaligned` | 同一行节点垂直偏移过大 |
| `row.size-jitter` | 同一行节点尺寸差异过大 |
| `spacing.too-tight` | 同行节点水平间距过小 |
| `page.margins` | 内容边距偏大 | info |
| `page.underused` | 页面利用率过低 | warning（严重时 error） |

**error 级（令 `hardConstraintsPassed=false`）：**

`node.overlap` · `node.out-of-page` · `group.overlap` · `group.member-escapes` ·
`edge.through-node` · `edge.through-container-heading` · `text.overflow-horizontal` · `text.overflow-vertical`

**warning / info 级（只影响 `score`）：** 其余全部。

实测确认：**error 级 7 项，warning 14 项，info 1 项**（`page.margins`），
另加 `page.underused` 的严重度按利用率动态计算（warning，严重时 error），合计 24 个代码。

## 四、能画好的

- **分层架构图**：3–5 层容器，每层 3–6 节点
- **部署/网络拓扑**：同分层，允许一对多、多对一
- **流程图**：线性或带分支，无容器时走 Flow
- **数据流**：长链或扇出汇聚

共同点：**结构性排列可以用"层"或"序"表达**。

## 五、纸张

默认 `canvas.mode=auto` 贴合内容，产出**非标准尺寸**。

`canvas.mode=paper` 吸附到 A5/A4/A3/A2/Tabloid（按需旋转）。

实测三个架构类场景在 paper 模式下：

| 场景 | 纸张 | 尺寸 |
| --- | --- | --- |
| `cbo` | **A4 纵向** | 11.69 × 16.54 |
| `deploy-topology` | **A4 横向** | 11.69 × 8.27 |
| `user-data-flow` | **A3 横向** | 16.54 × 11.69 |

**按内容量各自吸附**，不是统一一张纸——只有内容放不下 A4 时才升到 A3。

## 六、未验证

**L4（Visio 原生打开）从未执行。** 本机无 Visio，`/mnt/c` 只读。

L1（包结构）、L2（读回）、L3（渲染）都不能替代 L4。
"原生、可编辑的 VSDX" 是项目定义的核心，**这一条目前是假设，不是结论**。

## 七、如何判断一张图能不能画

1. 结构能否用 **层** 或 **序** 表达？能 → 可用
2. 是否需要 **固定列 + 时间轴**（时序图）→ 不可用
3. 是否需要 **状态语义**（入口/出口/复合）→ 部分可用
4. 是否有大量 **双向对**（A↔B 多次）→ 标签会重叠，需人工调 `labelPosition`

第 4 条可用 `labelPosition` 手工规避，是唯一有 workaround 的情况。

## 八、共享后端的连线交叉（不是缺陷）

多个节点连到同一组后端时，**交叉是拓扑性的**，不是布局选择。

**实测（`deploy-topology`，82 分，5 处 `edge.crossing`）**：
三个 API 实例在 x = 1.50 / 3.35 / 5.20，三个数据存储也在 x = 1.50 / 3.35 / 5.20。
`api1→rd` 与 `api2→rd` 都指向最右的存储，**必然越过排在它前面的两个节点**。

**这不是引擎能解决的**，已验证三条：

| 尝试 | 结果 |
| --- | --- |
| 换方向（leftToRight） | 更差：64 分 |
| 调整 IR 声明顺序 | 无变化（引擎按边排序，不按声明） |
| 引擎自动选择 | 已选更好的方向（topToBottom） |

**能消除交叉的是改接线**，例如 `api1→pg1`、`api2→pg2`、`api3→rd`——
但那是改动被描述的系统，不是改动它的图。

`BipartiteCrossingTests` 固定这四条结论。

## 九、为什么斜线是允许的

`OrthogonalRouter` 的策略是：**直线边只在会穿过其他形状时才绕行**，否则保持原样。

```csharp
// 直边除非穿过形状，否则保持直
if (!PathHitsShapes(direct, others)) continue;
```

**理由**：作者画一条直线是有意的。在没有遮挡的情况下把它改成折线，是在替作者做他没要求的决定。

**代价**：树形图、泳道的跨层连线因此是斜的：

| 布局 | 实测边类型 |
| --- | --- |
| `tree` | 9 条全 `straight` |
| `layered` / `swimlane` | 8 `straight` + 1 `curved` |
| `flow` | 7 `curved` + 1 `straight` + 1 `rightangle` |

树形图尤其明显——层级图通常期望正交折线，而这里是斜线。

**要改需要**：让布局引擎按图类型选择默认边类型（树形/泳道 → `rightAngle`），
而不是依赖路由器的穿形状判断。这属能力扩展，不是缺陷修复。
