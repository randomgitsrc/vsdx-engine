# Phase 0–6 独立实现审计

**审计对象：** `vsdx-engine` @ `967f967`
**审计方式：** 阅读实际源码 + 运行真实用例 + 检查产出文件，**不采信 README 与既往阶段报告**
**审计日期：** 2026-11-17

---

## 0. 审计方法与证据标准

本审计拒绝以下依据：README 描述、设计文档声称、测试方法名、接口声明、TODO 标记。

每项结论必须落在三种证据之一：

| 证据类型 | 本审计中的做法 |
| --- | --- |
| 源码定位 | 给出文件 + 方法名，可 grep 复核 |
| 运行结果 | 给出实际执行的命令与输出 |
| 产出物检查 | 解包 VSDX / 读回模型 / 打开 PNG |

**判定口径：**

- **已实现** — 有实现 + 有针对性测试 + 在真实用例上生效
- **部分实现** — 有实现但存在明确边界或缺陷，且该缺陷影响交付
- **未实现** — 无实现
- **无法验证** — 需要本环境不具备的条件（如 Windows + Visio）

---

## 1. 审计结论总览

| 能力 | 结论 | 严重度 | 处置建议 |
| --- | --- | --- | --- |
| Diagram Model（节点/连线/容器/图层） | **已实现** | — | 保留 |
| 稳定对象 ID | **已实现** | — | 保留 |
| **多层嵌套容器** | **部分实现（有 P0 缺陷）** | **P0** | **修复** |
| 语义属性与视觉属性分离 | **部分实现** | P1 | 修复 |
| Layout Engine（4 策略） | **已实现** | — | 保留 |
| **混合布局（区域内不同策略）** | **未实现** | **P1** | **新增** |
| **泳道布局** | **未实现** | P1 | 新增 |
| **局部布局（只重排一个区域）** | **未实现** | P1 | 新增 |
| Geometry Engine（文字度量、自适应） | **已实现** | — | 保留 |
| Routing（正交/避障） | **部分实现（多线汇聚质量差）** | P1 | 修复 |
| Validator（硬/软分离、17 项） | **已实现** | — | 保留 |
| **Validator：执行顺序缺陷** | **P0 缺陷（已修）** | **P0** | **已修复** |
| Repair Workflow（快照/影响/止损） | **已实现** | — | 保留 |
| Candidate Comparison | **已实现** | — | 保留 |
| Renderer（SVG/PNG/CJK） | **已实现** | — | 保留 |
| VSDX Adapter（读写/增量/身份） | **已实现** | — | 保留 |
| **视觉组件（标题/图例/注释）** | **未实现** | P1 | 新增 |
| L4 Visio 原生行为保真 | **无法验证** | — | 如实标注 |

---

## 2. 逐项审计

### 2.1 Diagram Model

**代码位置：** `src/Vsdx.Model/DiagramModel.cs`（248 行）

| 项目 | 内容 |
| --- | --- |
| 实现机制 | `Diagram → Page → {Group, Node, Edge, Layer}` 树；`Bounds` 为中心点+宽高 |
| 测试证据 | `CreateTests`、`ReadTests`、`RoundTripTests`（`RoundTrip_IdsAreStable` 等） |
| 实际限制 | 见 2.2、2.3 |
| 审计结论 | **已实现**（基础结构） |
| 建议 | 保留 |

节点、连线、容器、图层、Shape Data、多页面均有实现并在测试中往返验证。

### 2.2 多层嵌套容器 — **P0 缺陷**

| 项目 | 内容 |
| --- | --- |
| 代码位置 | 写入 `OfficeImoDocumentWriter.cs:76-88`；读取 `OfficeImoDocumentReader.ReadGroup()` |
| 实现机制 | `Group.ParentId` 字段存在；写入时**两个容器都作为兄弟写入**，仅发一条 warning |
| 测试证据 | **无测试覆盖嵌套容器**（grep `ParentId` 在 tests 下无结果） |
| 实际限制 | 见下方实测 |
| 审计结论 | **部分实现（结构丢失）** |
| 建议 | **修复（P0）** |

**实测证据：**

构造 `outer` 容器内含 `inner` 容器（`inner.ParentId = "outer"`）：

```console
$ vsdx-tool create --input nested.json --output nested.vsdx
created nested.vsdx
  pages=1 shapes=3 connectors=1
  WARNING write.nested-group: Nested group 'inner' is written as a sibling container
```

读回后：

```
groups after round-trip:
   inner parentId= None members= ['a', 'b']
   outer parentId= None members= ['c']
```

包内检查：

```console
$ python3 -c "...统计 Parent cell 与 msvStructureType..."
  structure type: Container
  structure type: Container
  Parent cells: 0
```

**三重丢失：**

1. `inner.ParentId` 读回为 `None` —— 读取器**从不恢复父关系**
2. 两个容器在包内是**平级**（`Parent cells: 0`），Visio 不认为存在嵌套
3. `inner` 的成员 `a`、`b` 从 `outer` 的成员列表中消失

**为什么这是 P0 而非"已知限制"：**
任务把 VSDX 作为可长期维护的资产。嵌套结构是部署图、集群图、多级分区图的常见表达。
当前行为是**静默降级为平级**——用户拿到文件后无法得知层级已丢失。警告只出现在
create 的 stderr，既不在 `--json` 的 compatibility 里被强调，也不阻断交付。

### 2.3 语义属性与视觉属性分离 — 部分实现

| 项目 | 内容 |
| --- | --- |
| 代码位置 | `Node.Type`（语义）、`Node.Shape`、`Node.Style`（视觉） |
| 实际限制 | `Node.Type` 是自由字符串，无受控词表，也不参与任何布局或验证决策 |
| 审计结论 | **部分实现** |
| 建议 | 修复（P1）——见 `capability-gap-analysis.md` |

对比：`Diagram.Type` 在 Phase 3 已接入布局策略选择（`SuggestStrategy`），
但**节点级** `Node.Type` 仍是死字段：

```console
$ grep -rn "node.Type\|\.Type ==" src/ --include=*.cs | grep -v "Diagram\|Node.Type ="
（仅赋值，无读取）
```

### 2.4 Layout Engine — 已实现

| 项目 | 内容 |
| --- | --- |
| 代码位置 | `LayoutStrategies.cs`、`LayeredLayoutEngine.cs`、`FlowAndTreeLayoutEngine.cs` |
| 实现机制 | `ILayoutEngine` + 4 实现（Layered / Flow / Tree / Grid）+ Manual |
| 测试证据 | `LayoutTests`（16 项）：放置、确定性、容器保持、层级方向、策略选择 |
| 实际限制 | 见 2.5、2.6 |
| 审计结论 | **已实现** |
| 建议 | 保留 |

**真实生效验证**（不只是单元测试）：3 份示例 × 5 策略 = 15/15 通过硬约束。

### 2.5 混合布局（区域内不同策略）— **未实现**

| 项目 | 内容 |
| --- | --- |
| 代码位置 | `DiagramLayoutEngine.Apply()` 对整页只用一个 strategy |
| 实现机制 | 无 |
| 测试证据 | 无 |
| 实际限制 | 一整张图只能共享一种布局算法 |
| 审计结论 | **未实现** |
| 建议 | **新增（P1）** |

任务 §6.3 明确要求："不能将不同区域内的所有节点强行放入同一种布局算法"。
当前 `LayeredLayoutEngine.LayoutBanded()` 对每个容器做一行，但**所有容器用同一规则**——
树形区域、流程区域、网格区域无法并存。

### 2.6 泳道布局 — 未实现

```console
$ grep -rniE "swimlane|laneId" src/ --include=*.cs
（无结果；OrthogonalRouter 里的 "lanes" 是布线通道，与泳道无关）
```

任务 §4.1 第 4 类"跨职能泳道图"无法表达。**审计结论：未实现。**

### 2.7 局部布局 — 未实现

`ImpactAnalysis` 能算出"该重排哪个区域"（`RepairScope.RegionRelayout`），
但**没有只重排单个容器的能力**——`DiagramLayoutEngine.Apply` 总是处理整页。
修复循环对此的补偿是"重排整页"，代价是波及无关区域（与任务 §6.9"保护无关对象"相悖）。

### 2.8 Geometry Engine — 已实现

| 项目 | 内容 |
| --- | --- |
| 代码位置 | `TextLayout.cs`、`DiagramSizer.cs` |
| 实现机制 | 字符类别宽度估算 → 节点尺寸 + padding → 容器 → 页面；`TypeScale` 六级字号 |
| 测试证据 | `SizingTests`（20 项） |
| 实际限制 | 估算非真实字体度量，但 Phase 0 实测 CJK 误差 0%、拉丁平均 2.6% |
| 审计结论 | **已实现** |
| 建议 | 保留 |

### 2.9 Routing — 部分实现

| 项目 | 内容 |
| --- | --- |
| 代码位置 | `OrthogonalRouter.cs`（522 行） |
| 实现机制 | 通道 + 行位候选路径搜索，代价函数 = 长度 + 2×转折数，避障验证覆盖完整折线 |
| 测试证据 | `IntegrationTests.Scenario_RouterAvoidsNodes`、`DetectorTests` 绕行比 |
| 实际限制 | **多线汇聚质量差**，见下 |
| 审计结论 | **部分实现** |
| 建议 | 修复（P1） |

**实测缺陷**：microservices/complex 中 7 条连线从同一网关扇出到 7 个服务，
布线器把其中多条压到底部空白区，形成长距离平行线：

```
b0: 绕行 1.41×　c4: 1.41×　c5: 1.32×　c6: 1.26×
```

虽然**全部在 2.0 阈值内**、检测器不报错，但视觉上明显不专业（见
`visual-review-report.md`）。这是"指标合格但视觉不合格"的典型案例——
说明绕行比单指标不足以表达布线质量。

### 2.10 Validator — 已实现，但发现执行顺序 P0 缺陷

| 项目 | 内容 |
| --- | --- |
| 代码位置 | `LayoutAnalyzer.cs`（1010 行）、`VsdxPackageValidatorService.cs`（477 行） |
| 实现机制 | 硬/软分离；硬失败分数归零；14 类几何规则 + 17 项 OPC/Visio 检查 |
| 测试证据 | `DetectorTests`（14）、`ValidationTests`（11）、`BenchmarkTests`（8 个故障注入必检出） |
| 审计结论 | **已实现**（规则本身） |
| 建议 | 保留 |

但审计过程中发现**被测系统本身的执行顺序缺陷**，见 2.11。

### 2.11 【P0 已修复】create 流水线顺序错误

| 项目 | 内容 |
| --- | --- |
| 代码位置 | 原 `VsdxEngine.Create()`：`FitPage` → `Layout` → `Route` |
| 缺陷 | **页面在布局之前就按旧坐标裁剪定尺寸**，随后布局把全部节点搬走 |
| 实测影响 | microservices/complex **右侧空白 7.09 英寸**（页宽 20.45"，内容右边界 13.36"） |

**这是本次审计最有价值的发现**：一个机器评分 90 分、硬约束全过、
18/18 基准"通过"的图，视觉上右侧空了三分之一。

**修复**：管线重排为 `text → place → containers → recenter → fit page → route`。

**修复后**：右侧空白 7.09" → **0.86"**。

> 该缺陷说明：**既有测试与基准全部无法发现它**。
> `check-layout` 只把页面利用率记为 `info`（21%），从不阻断；
> 而"18/18 通过"的结论建立在"硬约束全过"之上。
> 这直接印证任务 §5.3"不能把视觉评价全部伪装成客观自动检测"。

### 2.12 Repair Workflow — 已实现

| 项目 | 内容 |
| --- | --- |
| 代码位置 | `VersionStore.cs`、`RepairController.cs` |
| 实现机制 | 深拷贝快照；接受条件 = 减少问题且不新增硬伤；策略去重；预算耗尽升级；全级试尽即停 |
| 测试证据 | `RepairTests`（20 项） |
| 实际限制 | 无"只重排单容器"能力（见 2.7），升级只能整页重排 |
| 审计结论 | **已实现** |
| 建议 | 保留，配合 2.7 增强 |

### 2.13 Candidate Comparison — 已实现

| 项目 | 内容 |
| --- | --- |
| 代码位置 | `CandidateComparison.cs` |
| 实现机制 | 深拷贝隔离生成；硬过滤 + 软排序；`--emit-dir` 落盘全部候选 |
| 测试证据 | `CandidateTests`（13 项，含"源模型不被修改"断言） |
| 审计结论 | **已实现** |
| 建议 | 保留 |

### 2.14 Renderer — 已实现

| 项目 | 内容 |
| --- | --- |
| 代码位置 | `OfficeImoVsdxRenderer.cs`、`FontResolver.cs` |
| 实现机制 | 重开磁盘 VSDX → 逐页 `ToSvg` / `SaveAsImage`；CJK 字体自动注入 |
| 测试证据 | `RenderTests`（10 项） |
| 实际限制 | 与 Visio 原生渲染存在差异（见 `vsdx-compatibility-report.md`） |
| 审计结论 | **已实现** |
| 建议 | 保留 |

### 2.15 VSDX Adapter — 已实现

| 项目 | 内容 |
| --- | --- |
| 代码位置 | `OfficeImoDocumentReader.cs`、`OfficeImoDocumentWriter.cs`、`OfficeImoIncrementalEditor.cs` |
| 实现机制 | 稳定 ID 存于 `User.VsdxModelId`；增量只写差异对象 |
| 测试证据 | `RoundTripTests`（11）、`ExternalVsdxTests`（9，输入由手写 OPC 生成） |
| 实际限制 | 嵌套容器（2.2）；OfficeIMO 3.6.1 四项上游缺陷已补偿 |
| 审计结论 | **已实现** |
| 建议 | 保留 |

### 2.16 视觉组件 — 未实现

```console
$ grep -rniE "DiagramTitle|legend|callout|annotation" src/Vsdx.Engine/*.cs src/Vsdx.Model/*.cs
（仅 TypeScale 里的字号档位，无实际组件）
```

无图表标题框、无图例、无注释框。任务 §6.6 要求的组件体系缺失。
**审计结论：未实现（P1）。**

---

## 3. 测试审计（任务 §3.2）

| 检查项 | 结果 | 证据 |
| --- | --- | --- |
| 是否有真实断言 | **大部分是** | 断言几何数值、代码集合、往返快照，而非"文件存在" |
| 弱测试（只查存在/非空） | **少量** | `Assert.True(File.Exists(...))` 出现在渲染后 |
| 过度依赖内部实现 | **少量** | `DetectorTests` 直接用 `LayoutIssue` 字段 |
| 缺少失败案例 | **已改善** | 8 个故障注入 + `ExternalVsdxTests` 手写 OPC |
| 缺少端到端 | **有** | `IntegrationTests`（14）、`BenchmarkTests`（47） |
| 测试数据过简 | **部分改善** | 基准 6 族 × 3 档，但 Advanced 档仍以"节点更多"为主 |
| 与真实交付脱节 | **存在** | **全部测试都能在 7.09" 空白页面上通过**（见 2.11） |

**最严重的一条**：任务 §3.2 问"是否存在测试与真实交付场景脱节"。
2.11 给出了明确答案是**存在**——一个视觉上垫底的产出，在 226 项测试、
18 个基准、8 个故障注入全绿的情况下被放行。

根因：所有机器检查都在"几何合法性"层面，而**页面留白只有 `info` 级别**，
没有规则对"内容只占页面 21%"作出任何阻断性判断。

---

## 4. 初始差距清单（供 Gate C 决策）

| # | 差距 | 优先级 | 证据 | 影响 |
| --- | --- | --- | --- | --- |
| G1 | 嵌套容器结构丢失 | **P0** | 2.2 实测，Parent cells=0 | 部署/集群/多级分区图不可用 |
| G2 | create 流水线顺序错误 | **P0** | 2.11 实测，7.09" 空白 | 所有图表都可能右下大面积空白 |
| G3 | 无混合布局（区域内不同策略） | P1 | 2.5 grep 无实现 | 混合架构图被迫同构 |
| G4 | 无泳道布局 | P1 | 2.6 grep 无实现 | 跨职能流程图不可表达 |
| G5 | 无局部布局 | P1 | 2.7 整页重排 | 修复波及无关区域 |
| G6 | 多线汇聚布线质量差 | P1 | 2.9 实测 1.41× 且视觉差 | 扇出型拓扑不专业 |
| G7 | 无视觉组件（标题/图例/注释） | P1 | 2.16 grep 无实现 | 图缺"成品感" |
| G8 | 页面利用率只有 info 级 | P1 | `page.underused` 是 info | 空白页无法被检测器拦截 |
| G9 | `Node.Type` 死字段 | P2 | 2.3 grep | 语义与视觉未分离 |
| G10 | 无 L4 Visio 原生验证 | — | 环境限制 | 如实标注为无法验证 |

---

## 5. 审计对既有结论的修正

本次审计**推翻了 Phase 0–6 报告中的一处结论**：

Phase 6 报告写"18/18 基准案例可交付（90–98 分）"，并以此作为能力达标的证据。
审计显示该结论**在语义上不成立**：其中 microservices/complex 视觉上右侧空出 7.09 英寸，
连接线在底部形成长达数英寸的平行走廊。

"可交付"在 Phase 0–6 中被定义为"硬约束全过"。审计认为这个定义过窄——
**一张右侧空三分之一的图不应当被称为可交付。**

修正后的口径（本阶段采用）：

- **可交付** = 硬约束全过 **且** 页面利用率 ≥ 40% **且** 人工视觉审查无"明显不专业"项
- 三者任一不满足即标记为"部分通过"

按此口径，修复前的 microservices/complex 是**部分通过**，不是通过。

---

## 6. 审计未覆盖的部分

| 项 | 原因 |
| --- | --- |
| L4 Visio 原生行为 | 无 Windows + Visio |
| 超大图（>200 节点）性能 | 未构造 |
| 并发调用隔离 | 未测试 |
| 取消/超时机制 | 代码中未见 CancellationToken 贯穿（见 §11 工程可靠性） |

关于取消机制：`RenderRequest` 等未暴露 `CancellationToken`，
CLI 也没有超时参数。这是任务 §11 明确要求的项，**当前为未实现**。
