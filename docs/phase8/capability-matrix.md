# Phase 8.0 能力矩阵

**依据：** `current-state-audit.md` 的实测证据
**图例：** ✅ IV=Implemented&Verified / ⚠️ II=Implemented Incomplete / 📄 PO=Protocol Only / 🧪 EX=Experimental / ❌ MI=Missing / 🚫 UD=Unsupported by Dependency / ⛔ NS=Not in Engine Scope / 🔬 LAB=Format Feasible by Hand, No Engine Support

> **分类口径（2026-11-17 修订）：**
> `🔬 LAB` 与 `⚠️ II` 的区别是本矩阵的关键：
> - `🔬 LAB` = 我用手写 XML **证明了包格式能承载**该特性，但 **Engine 产品代码完全不支持**
>   （无模型字段、无写入器、无读取器）。这**不是**产品能力。
> - `⚠️ II` = Engine **有**实现，但在某个环节不完整（如读了不写、写得不全）。
>
> 初稿把 `🔬 LAB` 误归为 `⚠️ II`，会让读者以为"这个能力已经有了，只差收尾"，
> 而实际是"格式允许，产品一行代码都没有"。这是实质性误判，已在各条目改正。

---

## 1. 图形模型

| 能力 | 状态 | 证据 | 说明 |
| --- | --- | --- | --- |
| Node（语义节点） | ✅ IV | `DiagramModel.cs:92`、`CreateTests` | 稳定 ID 存 `User.VsdxModelId` |
| Edge（连接线） | ✅ IV | `DiagramModel.cs:130`、`ConnectorTests` | 含 waypoints / 端口 |
| Group / Container | ✅ IV | `DiagramLayout.Apply`、`GroupTests` | 含 `msvStructureType` |
| 嵌套容器 | ⚠️ II | `VisioNestingBridge`、`G1_*` 测试 | 靠 XML patch；join key 是容器标题（脆） |
| Layer（图层） | ⚠️ II | 读者可读；实测写出产物**无 `<Layers>` 节** | **只读不写**，往返会丢图层 |
| 自由路径 / 自定义几何 | ✅ **IV** | `FreePath` 模型 + `VisioFreePathBridge`；实测写入 1 个 `NameU="FreePath"` | 曾有 `🔬 LAB` 记录，已实现 |
| 独立文本（标题/注释/图例） | ✅ **IV** | `TextBlock` 模型；实测写入 4 个 `NameU="TextBlock"` | 含字号层级、对齐、转置保持横排 |
| 图片 | 🔬 **LAB** | 实验 2 手写 ForeignData 可行 | **Engine 零支持**：无媒体写入、无模型字段；渲染器仅占位 |
| SVG 嵌入 | 🔬 **LAB** | 实验 3 手写可行，需补 Content-Type | **Engine 零支持** |
| 复合符号 | ❌ MI | — | 建议走 Stencil（库已支持，未接入）而非自建 |
| Z-order | ✅ IV | `VisioZOrderBridge` 恢复文档序 | 曾为 P1 缺陷，已修 |
| 对象 ID 稳定 | ✅ IV | `RoundTrip_IdsAreStable` | — |
| 语义/视觉属性分离 | ⚠️ II | `Node.Type` 存在但仅 Protocol 用 | Engine 布局不读 `Node.Type` |
| 图形数据（Shape Data） | ✅ IV | `RoundTrip_ShapeDataSurvives` | — |
| 多页 | ✅ IV | 模型/读写/渲染/协议均支持；实测 2 页 | 详见 §8 |

**小结：** 11 类表达能力（任务 §4.1）中，模型可表达 4 类
（几何图形、连接关系、组合、图形数据）。

**重要更正（二次）：**

初稿标记 `🚫 Unsupported by Dependency`，是错的——实验证明包格式能承载。
但初稿的第一次修正是**过度乐观**的：把这三项改成 `⚠️ II`（Implemented Incomplete），
读起来像"已经实现，只差收尾"。

**判定口径：** 格式层可行 ≠ 产品层支持。

| 项 | 格式层 | Engine 产品层 |
| --- | --- | --- |
| 自由路径 | ✅ 可行 | ✅ **已实现**（`FreePath` + `VisioFreePathBridge`） |
| 图片 | ✅ 可行 | ❌ 无媒体写入、无模型字段 |
| SVG | ✅ 可行 | ❌ 同上，且需补 Content-Type |

**自由路径已从 `🔬 LAB` 变为 `✅ IV`**，其余两项仍是零支持。
判定口径保留：格式可行不等于产品能力，仍需分别标注。

---

## 2. 视觉样式

| 能力 | 状态 | 证据 | 说明 |
| --- | --- | --- | --- |
| Fill / Stroke / LineWeight | ✅ IV | `ShapeStyler`、`RoundTrip_StylesSurvive` | — |
| 字体族 / 字号 | ✅ IV | `ShapeStyle.Font/FontSize` | — |
| Typography 层级 | ⚠️ II | `TextLayout.TypeScale` 六级 | 但容器标题/连线标签字号是**固定**的 |
| `theme.roleColors` | ✅ IV | `DesignSpec.cs:224-238` | 真正执行 |
| `theme.background` | ✅ IV | `DesignSpec.cs:240-244` | 真正执行 |
| **`theme.palette`** | ✅ **IV** | `Palettes` + spec 应用；实测 `applied: theme.palette=enterprise`，节点得 `#70AD47`/`#C55A11` | 四套：enterprise/muted/technical/midnight；未知名报 `theme.palette-unknown` |
| 样式继承 | ❌ MI | 无 | 每个对象独立 `ShapeStyle` |
| 局部覆盖 | ⚠️ II | Node 级 style 可覆盖 | 无"角色默认 → 对象覆盖"链 |
| 透明度 / Opacity | ❌ MI | 无字段 | — |
| 阴影 / Effects | ❌ MI | 无 | — |
| 语义样式角色 | ✅ IV | `roleColors` + `MasterCatalog` 角色→形状映射 | 实测 `database`→`Process`、`decision`→`Diamond`、`process`→`Rectangle` |
| 多套视觉风格 | ✅ IV | `theme.palette` | 实测 `enterprise` 与 `midnight` 渲染为不同 PNG（MD5 不同） |
| Renderer 与 VSDX 一致 | ✅ IV | 渲染总重开磁盘 VSDX | 结构性保证 |

---

## 3. 图形资产

| 能力 | 状态 | 证据 | 说明 |
| --- | --- | --- | --- |
| 内置母版（17 种） | ✅ IV | `MasterCatalog` | 仅基础几何形状 |
| 语义母版（数据库/云/服务器） | ❌ MI | 别名全收敛到 17 个 | — |
| **母版导入** | 🚫 **库有，项目未用** | `ImportMastersAndGet` **实测返回 5 个** | **最大闲置能力** |
| **Stencil 目录** | 🚫 **库有，项目未用** | `VisioStencilCatalog.Load/Save` | — |
| **Stencil 资产元数据** | 🚫 **库有，项目未用** | `VisioStencilShape.{SourceLicense, SourceAttribution, PreviewImage, SourceConnectionPoints, IsSupported}` | 正好覆盖任务 §8.3 要求 |
| Stencil 节点创建 | 🚫 库有，项目未用 | `VisioGraphDiagramBuilder.StencilNode` | — |
| 外部资产包加载 | ❌ MI | — | 依赖 Stencil 接入 |
| 资产缺失处理 | ❌ MI | — | — |
| 许可证 / 来源记录 | 🚫 库有，项目未用 | `SourceLicense` / `SourceAttribution` | — |
| 连接点定义 | ✅ IV | `VisioConnectionPoint` | 项目用端口 hint |

---

## 4. 布局与构图

| 能力 | 状态 | 证据 | 说明 |
| --- | --- | --- | --- |
| Layered | ✅ IV | `LayeredLayoutEngine`、`LayoutTests` | — |
| Flow | ✅ IV | `FlowLayoutEngine` | 含环检测 |
| Tree | ✅ IV | `TreeLayoutEngine` | — |
| Grid | ✅ IV | `GridLayoutEngine` | 有容器时降级 |
| Swimlane | ✅ IV | `SwimlaneLayoutEngine`、`SwimlaneTests` | Phase 7 新增 |
| Manual | ✅ IV | `ManualLayoutEngine` | 无操作 |
| 策略自动选择 | ✅ IV | `SuggestStrategy` | 按 `Diagram.Type` + 决策节点 + 泳道 |
| 逐容器策略覆盖 | ✅ IV | `DesignSpec.ContainerLayouts` → `LocalRelayout` | — |
| 局部重排 | ✅ IV | `LocalRelayout` + 隔离断言 | 容器外节点逐字节不变 |
| **多区域构图协调** | ❌ MI | — | 无"区域间空间分配"概念 |
| 跨区域连线 | ⚠️ II | 走通用正交路由 | 无专用建模 |
| 区域间距 / 对齐 | ⚠️ II | `DiagramLayout` 固定常量 | 全局统一，非按区域 |
| 标题 / 图例布局 | ❌ MI | — | 无这些对象 |
| 预留通道 | ❌ MI | — | Phase 7 扇出实验失败即因缺此 |
| 页面尺寸协调 | ✅ IV | `DiagramSizer.FitPage` | — |

---

## 5. 路由

| 能力 | 状态 | 证据 | 说明 |
| --- | --- | --- | --- |
| 直线 / 折线 | ✅ IV | `OrthogonalRouter` | — |
| 正交避障 | ✅ IV | `Scenario_RouterAvoidsNodes` | 全折线避障验证 |
| 曲线 | ❌ MI | — | — |
| 连接线标签放置 | ⚠️ II | 中点 + 偏移 | 无避让 |
| 交叉避免 | ❌ MI | 仅检测 | `edge.crossing` 是 warning |
| 共享干线 | ❌ MI | Phase 7 实验失败 | 无通道预留时几何不成立 |
| 扇出检测 | ✅ IV | `edge.fan-out` | 检测非修复 |
| 跨容器路由 | ⚠️ II | 通用路由 | 有斜穿色带问题 |
| 高级路由选项 | 🚫 库有，项目未用 | `VisioConnectorRoutingOptions`、`LineJumpCode` | — |

---

## 6. VSDX 生命周期

| 能力 | 状态 | 证据 | 说明 |
| --- | --- | --- | --- |
| 创建 | ✅ IV | 全部基准 | — |
| 读取 | ✅ IV | `ReadTests`、`ExternalVsdxTests` | — |
| 增量编辑 | ✅ IV | `EditTests`、`RoundTrip_*` | 见 §6b 逐项 |
| 对象身份 | ✅ IV | `RoundTrip_IdsAreStable` | 见 §6a |
| 稳定 ID | ✅ IV | `RoundTrip_IdsAreStable` | — |
| 容器结构 | ✅ IV | 实测 3 个 marker | — |
| Shape Data | ✅ IV | 实测 prop 1→1 | — |
| 无操作往返 | ✅ IV | 实测 cells 完全一致 | — |
| **外部无几何形状** | ⚠️ **II（P0 缺陷）** | **实测：每 shape 注入 Geometry** | **覆盖非矩形形状** |
| 自定义几何保留 | ⚠️ II | 无样本 | 未验证 |
| Z-order | ✅ IV | `VisioZOrderBridge`；保存后恢复文档序 | 详见 §1 |
| 图片保留 | ⚠️ II | 无样本 | 未验证 |
| 未映射对象保留 | ⚠️ II | 兼容性报告机制存在 | 但注入几何违反此原则 |
| 多代往返 | ✅ IV | `ThreeGenerationsRemainStable` | — |

---

### 6a. 对象身份

章程要求"维护对象身份、映射和关系"。载体与边界：

| 能力 | 状态 | 证据 | 说明 |
| --- | --- | --- | --- |
| 稳定 ID | ✅ IV | `User.VsdxModelId`；`RoundTrip_IdsAreStable` | 写入 Reader 可读的自定义单元格 |
| 节点 ID 往返 | ✅ IV | 实测 9 节点 ID 全保留 | — |
| 容器 ID 往返 | ✅ IV | `ContainerBoundsPersistenceTests` | 靠标题匹配，脆弱 |
| 连线 ID 往返 | ✅ IV | `ConnectorTests` | — |
| **文本块 ID 稳定** | ⚠️ **II** | 实测：每次写回移除重建，ID 会变 | 按 `visioShapeId` 的引用会失效 |
| 外部文件对象识别 | ⚠️ II | 无 `VsdxModelId` 时按形状名/位置匹配 | 外部创建的对象可能错配 |

**文本块 ID 不稳定**是已知限制：`VisioTextBlockBridge` 每次写回重新创建形状。
保真守卫因此不能用 ID 判断文本块，改为按"该页是否仍有文本块"判断。

### 6b. 增量修改

九种操作，逐项状态：

| 操作 | 状态 | 证据 | 说明 |
| --- | --- | --- | --- |
| 节点新增 | ✅ IV | `node.added` | — |
| 节点修改 | ✅ IV | `node.modified` | 位置/尺寸/文字/样式 |
| 节点删除 | ✅ IV | `node.deleted` | **级联删除其连线** |
| 连线新增 | ✅ IV | `edge.added` | — |
| 连线修改 | ✅ IV | `edge.modified` | 含标签、箭头、颜色 |
| 连线删除 | ✅ IV | `edge.deleted` | 含级联产生 |
| 容器重命名 | ✅ IV | `group.renamed` | 原地改，不重建 |
| 容器几何 | ✅ IV | `group.resized` | 在成员附加**之后**应用 |
| 页面尺寸 | ✅ IV | `page.resized` | — |
| 未改对象保留 | ✅ IV | 实测 cells 逐一相同 | 无操作往返 25 次无漂移 |
| 级联删除连线 | ✅ IV | `DeleteMissingTests` | 曾有悬空引用，导致整包被拒 |
| 保真守卫 | ✅ IV | `FidelityGuard` | 几何减少会拒绝输出；文本块重写除外 |

**两条实现约束**，改这块前必须知道：

1. **容器几何必须在 `AddToContainer` 之后应用**——该调用会把容器重排到成员周围，先写会被覆盖。
2. **`Update` 会先跑 `DiagramLayout.Apply`**，它按成员重新推导所有容器。
   显式设置的 `Bounds` 会被它覆盖，因此带 `Bounds` 的容器在推导期间临时标 `AutoSize=false`。

## 7. 质量检测

| 能力 | 状态 | 证据 | 说明 |
| --- | --- | --- | --- |
| 硬约束（重叠/越界/逃逸/穿线） | ✅ IV | `LayoutAnalyzer` | 失败分数归零 |
| 软指标（绕行/对齐/抖动/交叉） | ✅ IV | 同上 | 带权重 |
| 页面利用率 | ✅ IV | 分档 + <15% 硬失败 | — |
| 扇出密度 | ✅ IV | `edge.fan-out` | — |
| 斜穿色带 | ✅ IV | `edge.diagonal-across-band` | — |
| 文本溢出 | ✅ IV | `text.overflow-*` | — |
| VSDX 包校验（17 项） | ✅ IV | `VsdxPackageValidatorService` | — |
| 渲染一致性 | ✅ IV | 重开磁盘文件 | — |
| **几何保真检测** | ❌ MI | — | 无法发现 §6 的注入问题 |
| 视觉质量检测 | ❌ MI | — | 只有几何规则；无对比度/层次检查 |

---

## 8. 协议与 Agent 集成

| 能力 | 状态 | 证据 | 说明 |
| --- | --- | --- | --- |
| Diagram IR | ✅ IV | `DiagramIr.cs` + schema | 无需坐标 |
| DesignSpec | ✅ IV | `DesignSpec.cs` + schema | — |
| ExecutionResult | ✅ IV | `ExecutionResult.cs` + schema | — |
| 未满足约束报告 | ✅ IV | `UnmetConstraints`、`ProtocolContractTests` | — |
| 未知约束码报告 | ✅ IV | `unknown_constraint` | — |
| 状态分级 | ✅ IV | succeeded/partial/failed/degraded | — |
| IR→模型映射回流 | ✅ IV | `objectMapping` | — |
| 多页 | ✅ IV | `DiagramIr.Pages`；实测 2 页各含节点与连线，`validate` 通过 | 曾误记为「IR 无 `pages`」 |
| 能力声明（Engine 支持什么） | ⚠️ II | 只有 `unmetConstraints` 逐次报告 | 无静态能力清单 |
| 降级声明 | ✅ IV | `degraded` 状态 | — |
| 版本协商 | ✅ IV | `IsCompatible` | 主版本不匹配拒绝 |

---

## 9. 平台与依赖

### 9a. 验证层级（不是能力项）

**这是"验证做到哪一步"，不是"产品具备什么能力"，因此不计入 §10 统计。**

| 层级 | 状态 | 含义 |
| --- | --- | --- |
| L1 包结构 | ✅ 已验证 | OPC/ZIP/XML 结构正确，17 项检查 |
| L2 后端重载 | ✅ 已验证 | 重新读回并比对结构 |
| L3 第三方渲染 | ✅ 已验证 | 渲染成 PNG 并人工目视 |
| **L4 Visio 原生** | **⬜ 未执行** | 无 Windows+Visio；**L1–L3 不能替代** |

**项目定义的核心承诺「原生、可编辑的 VSDX」需要 L4。** L1–L3 全部通过，也不能推出 L4 通过——
前三项验证的是"文件结构正确、能被本引擎和渲染器读回"，不是"Microsoft Visio 能打开并编辑"。

### 9b. 依赖与实验

| 项 | 状态 | 说明 |
| --- | --- | --- |
| Microsoft.Msagl | 🧪 EX | Phase 0 评估过，未引入 |
| Graphviz | 🧪 EX | Phase 0 评估：不引入（进程依赖） |
| OfficeIMO.Visio | ✅ IV | MIT，256 公开类型，实际使用 14 个 |

---

## 10. 统计汇总

| 状态 | 数量 | 占比 |
| --- | --- | --- |
| ✅ Implemented & Verified | 80 | 66% |
| ⚠️ Implemented but Incomplete | 16 | 13% |
| 🔬 Format Feasible, No Engine Support | 2 | 2% |
| 📄 Protocol Only | 0 | 0% |
| 🧪 Experimental | 2 | 2% |
| ❌ Missing | 15 | 12% |
| 🚫 Unsupported by Dependency | 6 | 5% |
| **合计** | **121** | **100%** |

> 验证层级（L1–L4）不计入本表，见 §9a。它们描述"验证做到哪一步"，不是"产品具备什么能力"。

> **2026-11-18 增补**：章程 §5.1 要求矩阵覆盖「对象身份、增量修改」等维度，原表各只有一行。
> 已补 §6a（对象身份，9 行）与 §6b（增量修改，12 行），合计由 106 增至 125。
>
> **2026-11-18 复核**：`theme.palette` 由 `📄 PO`（"仅声明，无读取方"）改为 `✅ IV`——
> 实测早已实现，文档把自己的能力说少了。`📄` 因此归零，`✅` 增至 60。
> 同一轮自由路径与独立文本也由未实现改为已实现。计数由脚本逐行核对。
>
> `🔬 LAB`（现 2 项：图片、SVG）的引入是矩阵的关键区分：
> 它们既不是 `🚫`（格式支持），也不是 `⚠️ II`（产品有实现），
> 而是"**格式允许、产品零支持**"。把它们混进任何一类都会误导工作量估算。

**分布解读：** 核心执行路径（创建 / 布局 / 读写 / 检测 / 协议 / 增量 / 身份）已验证。
剩余缺口集中在**表达丰富度**——图片、SVG 嵌入、复合符号——而不是基础能力或保真细节：
几何保留（`VisioGeometryPreserver`）与 Z-order（`VisioZOrderBridge`）均已实现。

`🔬 LAB`（2 项：图片、SVG）与 `❌ Missing`（16 项）仍是真实工作量，属"从零实现"而非"接线"。

各项统计见 §10，由脚本逐行核对；`CapabilityMatrixTests` 保证摘要与行一致。
