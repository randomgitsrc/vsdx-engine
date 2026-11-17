# Phase 8.0 能力矩阵

**依据：** `current-state-audit.md` 的实测证据
**图例：** ✅ IV=Implemented&Verified / ⚠️ II=Implemented Incomplete / 📄 PO=Protocol Only / 🧪 EX=Experimental / ❌ MI=Missing / 🚫 UD=Unsupported by Dependency / ⛔ NS=Not in Engine Scope

---

## 1. 图形模型

| 能力 | 状态 | 证据 | 说明 |
| --- | --- | --- | --- |
| Node（语义节点） | ✅ IV | `DiagramModel.cs:92`、`CreateTests` | 稳定 ID 存 `User.VsdxModelId` |
| Edge（连接线） | ✅ IV | `DiagramModel.cs:130`、`ConnectorTests` | 含 waypoints / 端口 |
| Group / Container | ✅ IV | `DiagramLayout.Apply`、`GroupTests` | 含 `msvStructureType` |
| 嵌套容器 | ⚠️ II | `VisioNestingBridge`、`G1_*` 测试 | 靠 XML patch；join key 是容器标题（脆） |
| Layer（图层） | ⚠️ II | `OfficeImoDocumentReader.cs:289` 读取 | **读取后可写回未验证** |
| 自由路径 / 自定义几何 | ❌ MI | `Node.Shape` 是字符串 | 只能选内置 master |
| 独立文本（标题/注释/图例） | ❌ MI | `grep Title/Legend/Annotation` 仅 TypeScale | 无对象 |
| 图片 | 🚫 UD | 反射未发现 image API | 需自行 patch OPC |
| SVG 嵌入 | 🚫 UD | 反射未发现 | 需 `ForeignData` + 关系 |
| 复合符号 | ❌ MI | — | 依赖 Stencil（库已支持，未接入） |
| Z-order | ⚠️ II | `ShapeOrder` 读取但**从不写入** | 每次保存丢失 |
| 对象 ID 稳定 | ✅ IV | `RoundTrip_IdsAreStable` | — |
| 语义/视觉属性分离 | ⚠️ II | `Node.Type` 存在但仅 Protocol 用 | Engine 布局不读 `Node.Type` |
| 图形数据（Shape Data） | ✅ IV | `RoundTrip_ShapeDataSurvives` | — |
| 多页 | ⚠️ II | 模型/读写/渲染均支持 | **协议层不支持** |

**小结：** 11 类表达能力（任务 §4.1）中，当前可表达 4 类
（几何图形、连接关系、组合、图形数据），缺 7 类。

---

## 2. 视觉样式

| 能力 | 状态 | 证据 | 说明 |
| --- | --- | --- | --- |
| Fill / Stroke / LineWeight | ✅ IV | `ShapeStyler`、`RoundTrip_StylesSurvive` | — |
| 字体族 / 字号 | ✅ IV | `ShapeStyle.Font/FontSize` | — |
| Typography 层级 | ⚠️ II | `TextLayout.TypeScale` 六级 | 但容器标题/连线标签字号是**固定**的 |
| `theme.roleColors` | ✅ IV | `DesignSpec.cs:224-238` | 真正执行 |
| `theme.background` | ✅ IV | `DesignSpec.cs:240-244` | 真正执行 |
| **`theme.palette`** | 📄 **PO** | **仅声明，无读取方** | **协议接受但静默忽略** |
| 样式继承 | ❌ MI | 无 | 每个对象独立 `ShapeStyle` |
| 局部覆盖 | ⚠️ II | Node 级 style 可覆盖 | 无"角色默认 → 对象覆盖"链 |
| 透明度 / Opacity | ❌ MI | 无字段 | — |
| 阴影 / Effects | ❌ MI | 无 | — |
| 语义样式角色 | ⚠️ II | `roleColors` 按 `Node.Type` | 但形状不随角色变化（§3） |
| 多套视觉风格 | 🧪 EX | 可改颜色/字号 | 未验证能否产出"明显不同"的风格 |
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
| 增量编辑 | ✅ IV | `EditTests`、`RoundTrip_*` | 保留未改对象 |
| 稳定 ID | ✅ IV | `RoundTrip_IdsAreStable` | — |
| 容器结构 | ✅ IV | 实测 3 个 marker | — |
| Shape Data | ✅ IV | 实测 prop 1→1 | — |
| 无操作往返 | ✅ IV | 实测 cells 完全一致 | — |
| **外部无几何形状** | ⚠️ **II（P0 缺陷）** | **实测：每 shape 注入 Geometry** | **覆盖非矩形形状** |
| 自定义几何保留 | ⚠️ II | 无样本 | 未验证 |
| Z-order | ⚠️ II | 读取不写入 | — |
| 图片保留 | ⚠️ II | 无样本 | 未验证 |
| 未映射对象保留 | ⚠️ II | 兼容性报告机制存在 | 但注入几何违反此原则 |
| 多代往返 | ✅ IV | `ThreeGenerationsRemainStable` | — |

---

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
| 多页 | ❌ MI | IR 无 `pages` | — |
| 能力声明（Engine 支持什么） | ⚠️ II | 只有 `unmetConstraints` 逐次报告 | 无静态能力清单 |
| 降级声明 | ✅ IV | `degraded` 状态 | — |
| 版本协商 | ✅ IV | `IsCompatible` | 主版本不匹配拒绝 |

---

## 9. 平台与依赖

| 能力 | 状态 | 说明 |
| --- | --- | --- |
| L1 包结构验证 | ✅ IV | 17 项检查 |
| L2 后端重载 | ✅ IV | — |
| L3 第三方渲染 | ✅ IV | 人工目视 |
| **L4 Visio 原生** | ⛔ NS | 无 Windows+Visio；**不得用其它层级替代** |
| Microsoft.Msagl | 🧪 EX | Phase 0 评估过，未引入 |
| Graphviz | 🧪 EX | Phase 0 评估：不引入（进程依赖） |
| OfficeIMO.Visio | ✅ IV | MIT，256 公开类型，仅用 3 个 |

---

## 10. 统计汇总

| 状态 | 数量 | 占比 |
| --- | --- | --- |
| ✅ Implemented & Verified | 54 | 52% |
| ⚠️ Implemented but Incomplete | 18 | 17% |
| 📄 Protocol Only | 1 | 1% |
| 🧪 Experimental | 3 | 3% |
| ❌ Missing | 18 | 17% |
| 🚫 Unsupported by Dependency | 8 | 8% |
| ⛔ Not in Engine Scope | 1 | 1% |
| **合计** | **103** | **100%** |

> 计数口径：逐行统计 §1–§9 表格状态列的第一个标记，每条矩阵行计一次。
> 数字由脚本统计得出，非人工估算。

**分布解读：** 核心执行路径（创建/布局/读写/检测/协议）已经很扎实（52% 已验证）。
缺口集中在**表达丰富度**（形状语义、独立对象、资产）与**保真细节**（几何/Z-order），
而不是基础能力缺失。这决定了 Phase 8 应"接入已有能力 + 补表达层"，
而不是重建引擎。
