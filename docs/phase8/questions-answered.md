# Phase 8.0 指定问题逐条回答

任务 §5 列出了 30 个"特别需要回答的问题"。本文件逐条回答，
**每条附证据**（源码位置 / 测试 / 实测输出）。

---

## A. 图形模型

### A1. 当前模型已经能够表达哪些图形？

**答：** 11 类表达能力（任务 §4.1）中可表达 4 类：

| 表达能力 | 是否可表达 | 载体 |
| --- | --- | --- |
| 几何图形 | ✅ | `Node.Shape` → 17 个内置 master |
| 连接关系 | ✅ | `Edge` + `OrthogonalRouter` |
| 组合对象 | ✅ | `Group` + `msvStructureType=Container` |
| 图形数据与属性 | ✅ | `Node.ShapeData` → `Property` 段 |
| 任意路径 | ❌（模型缺，格式可写） | — |
| 独立文本 | ❌ | — |
| 图片 | ❌（模型缺，格式可写） | — |
| 图标/符号 | ❌ | — |
| 复合图形 | ❌ | — |
| 页面区域 | ⚠️（用容器近似） | `Group` |
| 背景与装饰 | ⚠️ 仅整页背景色 | `Page.Background` |

**证据：** `capability-matrix.md` §1；`DiagramModel.cs`。

### A2. 哪些只是通过 Shape 字符串实现？

**答：** 全部 17 个形状都是字符串 → master 名映射，没有独立类型：

```csharp
BuiltIn = { Rectangle, Ellipse, Square, Circle, Dynamic connector, Diamond,
            Triangle, Process, Decision, Data, Preparation, Manual operation,
            Off-page reference, Parallelogram, Hexagon, Trapezoid, Pentagon,
            Text Box, Container }
```

别名（如 `database`→`Process`、`gateway`→`Hexagon`）**全部收敛到这 17 个**，
不存在语义形状。`database` 与 `service` 都渲染为矩形。

**证据：** `MasterCatalog.cs`；`DiagramIrConverter.ShapeForRole`。

### A3. 哪些对象能独立存在？

**答：** `Node`、`Edge`、`Group`。
- `Node` 可以不属于任何 Group（`GroupId == null`）
- `Edge` 必须连到两个 Node
- `Group` 可以没有成员（空容器，`LocalRelayout` 会报"容器内没有节点"）

**证据：** `DiagramModel.cs`；`LocalRelayoutTests.Local_EmptyContainerIsReported`。

### A4. 哪些对象只能附属于 Node / Group？

**答：**
- **连接线标签**：`Edge.Label`，是边的属性，不是独立对象
- **容器标题**：`Group.Name`，写入容器形状的 `<Text>`，不能独立移动
- **Shape Data**：`Node.ShapeData` 字典，附属节点
- **样式**：`ShapeStyle` 嵌在 Node/Group/Edge 内

**没有任何对象能独立于 Node/Group 存在**——这是 A1 中"独立文本/图片缺失"的直接原因。

### A5. 自由路径是否能够创建、读取、编辑和保存？

**答：**

| 环节 | 能力 | 证据 |
| --- | --- | --- |
| 创建（写入 VSDX） | ✅ 可行（需 XML patch） | 实验 1：注入 4 段折线，校验通过、渲染正确 |
| 读取 | ⚠️ 会读但**语义丢失** | 实验 1：读回变成 `Node{shape:"Rectangle", text:null}` |
| 编辑 | ❌ 模型无路径字段，无法编辑端点 | 同上 |
| 保存（往返保留） | ✅ 保留原始几何 | 实验 1：`update` 后 `rows=4` 与锯齿坐标仍在 |

**结论：格式层完全支持，模型层完全缺失。**
Phase 8.1 需新增 `FreePath` 类型并让读取器识别 `Section N="Geometry"`。

**证据：** `technical-experiments.md` 实验 1。

### A6. 是否能够表达复合符号？

**答：** 当前**不能**。

但底层有两个可行路径：
1. **原生 master 组合**：Visio 的 Group + 多个 sub-shape（需 XML patch）
2. **Stencil**：`VisioGraphDiagramBuilder.StencilNode(id, text, stencil)` 已存在，
   外部 stencil 里的复合符号可直接引用

路径 2 成本低得多，且复用 Visio 资产生态（ADR-003）。

### A7. 如何避免新模型与旧模型重复？

**答：** 采用**并列集合**而非继承（ADR-002）：

```csharp
public sealed class Page {
    public List<Node> Nodes { get; }          // 保留
    public List<Edge> Edges { get; }          // 保留
    public List<Group> Groups { get; }        // 保留
    public List<TextBlock> TextBlocks { get; }// 新增
    public List<FreePath> Paths { get; }      // 新增
}
```

**不重复的三个理由：**
1. 新类型只承载旧类型**无法表达**的语义（文本无 `GroupId`/`From`，路径无正文）
2. 布局器签名仍是 `Layout(Page)`，只遍历 `Nodes`，不被新类型污染
3. 序列化是三个并列数组，无继承歧义

**若引入 `abstract Shape`：** `Node` 会被迫带 `TextBlock` 的空洞字段，
`TextBlock` 会被迫带 `Edge.From` 的无效语义——这正是重复的来源。

---

## B. VSDX

### B1. OfficeIMO.Visio 已经支持哪些原生能力？

**答：** 公开 256 个类型，本项目仅用 3 个。已确认可用的能力：

| 能力 | API | 项目是否使用 |
| --- | --- | --- |
| 形状 / 连接线 / 页面 / 容器 | `VisioPage` `VisioShape` `VisioConnector` | ✅ |
| 母版导入 | `ImportMasters` `ImportMastersAndGet` `ImportStencilMasters` `LearnMastersFromVsdx` | ❌ |
| 母版集合 | `Masters` `UseMastersByDefault` | ❌ |
| Stencil 目录 | `VisioStencilCatalog.Load/Save/ToBytes` | ❌ |
| Stencil 元数据 | `VisioStencilShape.{SourceLicense, SourceAttribution, PreviewImage, SourceConnectionPoints, IsSupported}` | ❌ |
| Stencil 节点 | `VisioGraphDiagramBuilder.StencilNode` | ❌ |
| 标注 | `VisioCalloutExtensions.AddCallout` `VisioCalloutOptions` | ❌ |
| 批注 | `VisioCommentExtensions` `VisioCommentOptions` | ❌ |
| 图层 | `VisioLayer` `VisioLayerRenderMode` | 部分（仅读） |
| 数据图形 | `VisioDataGraphic` `VisioDataGraphicInstance` `VisioDataGraphicLegend` | ❌ |
| 连接线高级路由 | `VisioConnectorRoutingOptions` `LineJumpCode` `RerouteBehavior` | ❌ |
| 容器编辑 | `VisioContainerEditingExtensions` `VisioContainerInfo` | ❌ |
| Shape↔Master 绑定 | `VisioShape.Master`（可写）`MasterShapeId` `MasterShape` | ❌ |

**实测：** `ImportMastersAndGet` 对生成文件返回 5 个母版，`doc.Masters` 0→5。

**未提供：** 图片、SVG 的高层 API（但包格式支持，见 B2）。

### B2. 哪些能力需要自行 Patch XML？

**答：** 当前 2 处，实验证明还需 3 处：

| 能力 | 现状 | 位置 |
| --- | --- | --- |
| 模型元数据 → docProps | ✅ 已有 patch | `VisioMetadataBridge` |
| 嵌套容器 `Parent` cell | ✅ 已有 patch（写+读） | `VisioNestingBridge` |
| 自由路径 Geometry | ⬜ 需新增（实验已验证可行） | — |
| 图片 `ForeignData` + media 关系 | ⬜ 需新增（实验已验证可行） | — |
| SVG 嵌入 | ⬜ 需新增 + 补 Content-Type | — |

**结论：若三项全做，patch 总数达 5 处。** 触发 ADR-006 的复核条件
（"patch 数 ≥ 5 时评估直接操作 OPC"）。

### B3. 哪些 XML Patch 具有兼容性风险？

| Patch | 风险 | 理由 |
| --- | --- | --- |
| `VisioMetadataBridge` | 低 | 只往 docProps 增字段，不改既有结构 |
| `VisioNestingBridge`（写） | **中** | 依赖"容器 `<Text>` == 组名"这一约定；OfficeIMO 若改文本写入即静默失效 |
| `VisioNestingBridge`（读） | **中** | 直接解析 page XML，绕过库的加载路径 |
| 自由路径 Geometry | 低 | 只新增 Shape，不改其它 |
| 图片 / SVG | 中 | 需正确维护 media 关系与 Content-Type；写错即 `Format error in package`（实验 4 实际遇到过） |

**最脆的是嵌套容器**：join key 是容器标题文本，属结构性假设。

### B4. 哪些原生对象可以往返保留？

**答：** 实测结论：

| 对象 | 往返保留 | 证据 |
| --- | --- | --- |
| Shape 数 / ID | ✅ | `RoundTrip_ShapeCountsAreStable` |
| 文字 / 样式 | ✅ | `RoundTrip_StylesSurvive` |
| 连接线端点 | ✅ | `RoundTrip_ConnectorEndpointsSurvive` |
| 容器（`msvStructureType`） | ✅ | 实测 3 个 marker |
| 嵌套容器（`Parent`） | ✅ | Phase 7 修复 + 测试 |
| Shape Data（`Property`） | ✅ | 实测 `prop 1→1` |
| **无 Geometry 段的形状** | **❌ 被注入矩形** | **实测 `geom 0→8`** |
| 自定义 Geometry | ✅ 几何保留 | 实验 1 |
| Z-order | ❌ 丢失 | `ShapeOrder` 读了不写 |

### B5. 外部 VSDX 有哪些无法编辑的内容？

| 内容 | 可读 | 可编辑 | 说明 |
| --- | --- | --- | --- |
| 自定义 Geometry | ✅ | ❌ | 读回被标记为 Rectangle 节点 |
| 图层 | ✅ | ❌ | 读了但写回未验证 |
| 主题 / StyleSheet | ✅ | ❌ | 不进模型 |
| 数据图形 | ✅ | ❌ | 不进模型 |
| 图片 | ✅ | ❌ | 模型无字段 |
| 批注 / 标注 | ✅ | ❌ | 库有 API，项目未接 |
| 无 Geometry 的 Master 形状 | ✅ | ⚠️ | **保存时被注入矩形，破坏原状** |

### B6. 如何保护未映射对象？

**答：** 现有机制 + 缺失的机制：

**已有：**
- 兼容性报告（`CompatibilityReport`）记录无法映射项
- 增量编辑保留未修改对象（`RoundTrip_` 系列测试）
- 无操作往返结构完全一致（实测 `cells` 集合相同）

**缺失（P0）：**
- **没有"禁止改动未映射对象"的保护**。§B5 的"无 Geometry 形状被注入矩形"
  正是保护缺失的后果——项目连"这个形状原本没有几何"都不知道。

**建议：** Phase 8.1 新增 `check-fidelity`（ADR-006），
比对修改前后的结构差异，把"未映射对象是否被破坏"变成可复核输出。

---

## C. 视觉系统

### C1. Theme 是否真正执行？

**答：部分执行。**

| 字段 | 执行 | 证据 |
| --- | --- | --- |
| `theme.roleColors` | ✅ | `DesignSpec.cs:224-238` 按 `Node.Type` 上色 |
| `theme.background` | ✅ | `DesignSpec.cs:240-244` |
| `theme.palette` | ❌ **完全不执行** | 全仓库仅 2 处引用，均为声明 |

### C2. Palette 是否真正执行？

**答：否。**

```console
$ grep -rn "Palette" src/ --include=*.cs
src/Vsdx.Protocol/DesignSpec.cs:83:    public string? Palette { get; set; }
src/Vsdx.Model/DiagramModel.cs:205:  public string? Palette { get; set; }
```

**两处都是声明，无任何读取方。** Schema 允许调用方设置，执行时被静默忽略，
**且不进 `unmetConstraints`**——违反项目自己的协议契约。

### C3. 字体层级是否完整？

**答：不完整。**

`TextLayout.TypeScale` 提供六级（title/heading/subheading/body/label/annotation），
节点正文走这套推导。但：

| 文本类型 | 字号来源 |
| --- | --- |
| 节点正文 | ✅ `TypeScale` 推导 |
| **容器标题** | ❌ 固定（`ShapeStyle.FontSize` 或默认） |
| **连线标签** | ❌ 固定 |

任务 §4.2 要求"不能只提供一个全局 FontSize，需要支持不同角色的字体层级"，
容器标题与连线标签恰好是最需要层级的两个角色，当前都没有。

### C4. 样式继承是否存在？

**答：不存在。**

`ShapeStyle` 是每个对象上的独立字段，无"角色默认 → 对象覆盖"链：

```csharp
class ShapeStyle { Fill, Stroke, TextColor, Font, FontSize, Bold, HAlign }
```

`DesignSpecApplier` 里样式逻辑分散在 3 处（typography / roleColors / background），
各自直接改 `node.Style`，没有统一解析。

**后果：** 新增 `palette` 只能在第 4 处再加一段 if，无法支撑"多套风格"。

### C5. 不同视觉风格是否能够产生明显不同的结果？

**答：未验证，且基础设施不足。**

目前可调的只有：`roleColors`（逐角色色）、`background`、`Typography.BaseSize`、
`FontFamily`。也就是说能改**颜色和字号**，但：

- 形状不随角色变化（database 与 service 同为矩形）
- 无字体层级（容器/标签字号固定）
- `palette` 不生效

**判断：** 可以产出"配色不同"的图，但难以产出任务 §8.2 要求的
"字体层级明显、颜色有设计逻辑、不同对象具有清晰视觉角色"的**风格差异**。
需要先补 C3/C4 才能验收 8.2。

### C6. Renderer 与 VSDX 输出是否一致？

**答：结构性一致，但有一处已验证的差异。**

**一致的保证：** 渲染器**总是重新打开磁盘上的 VSDX** 再投影，不读内存模型
（`OfficeImoVsdxRenderer`）。因此不可能出现"另画一张图冒充"。

**已验证的差异：** 实验 2 中，含 `ForeignData` 图片的 VSDX 校验通过、
渲染成功，但**渲染出的是占位框而非真实位图**。

即：图片能存进 VSDX，但 PNG/SVG 预览不反映真实图片内容。
这是必须如实记录的渲染器能力边界。

---

## D. 资产

### D1. 当前 MasterCatalog 有哪些真实 Master？

**答：17 个**（另有 1 个动态连接线）：

```
Rectangle  Ellipse  Square  Circle  Dynamic connector  Diamond  Triangle
Process  Decision  Data  Preparation  Manual operation  Off-page reference
Parallelogram  Hexagon  Trapezoid  Pentagon  Text Box  Container
```

全部分别名映射到这 17 个。**无数据库、服务器、云、队列等语义形状。**

### D2. 是否能够加载外部 Stencil？

**答：库支持，项目未接，且未实测过真实 .vssx。**

`VisioStencilCatalog.Load(...)` 存在多个重载，`LoadAsync` 亦有。

**已实测的替代路径：** `ImportMastersAndGet` 从**已有 VSDX** 导入母版，
返回 5 个母版，`doc.Masters` 0→5。

**未验证：** `Load` 是否接受真实 Visio `.vssx` 文件。这是 Phase 8.3 的第一项待验证内容。

### D3. 是否能直接创建具有语义的原生图形？

**答：能（通过母版），但项目未接。**

`VisioShape.Master` 可写，`MasterShapeId` / `MasterShape` 可写。
配合 `ImportMastersAndGet`，理论上可创建"引用真实 master 的形状"。

**当前实现：** 全部走 `MasterCatalog` 的字符串→内置 master 名映射，
`database` 得到的仍是 `Process`（矩形）。

### D4. SVG 能否嵌入 VSDX？

**答：能。** 实验 3 已实证：

- 写入 `visio/media/image1.svg`
- page1.xml 加 `Type="Foreign"` shape + `<ForeignData>`
- rels 加 image 关系
- **必须**在 `[Content_Types].xml` 补 `<Default Extension="svg" ContentType="image/svg+xml"/>`

结果：包校验 VALID，渲染 34840 bytes。

**成本差异：** PNG 不需要补 Content-Type（库已写 png/jpg/emf），SVG 需要。

### D5. 复合图标能否由多个原生 Shape 组成？

**答：技术上可以，项目未实现。**

Visio 的复合符号本质是 Group + 多个 sub-shape。当前项目能创建 Group 与 Container，
但 `Group` 只承载"节点集合"，不能承载"形状集合"——`Group.Members` 是 node id 列表。

**建议：** 优先走 Stencil（D2），而不是自建复合形状组合。

### D6. 哪些能力需要第三方库补足？

**答：** 经实验后修正——**比原判断少很多**：

| 能力 | 需要补足？ | 修正说明 |
| --- | --- | --- |
| 图片 | ⚠️ 需自写 XML，非第三方库 | 实验 2 证明可行 |
| SVG | ⚠️ 需自写 XML + Content-Type | 实验 3 证明可行 |
| 自由路径 | ⚠️ 需自写 XML | 实验 1 证明可行 |
| 资产/母版/Stencil | ❌ **库已提供** | `ImportMastersAndGet` 实测可用 |
| 布局算法 | ❌ 自研已够 | 5 策略 + 泳道覆盖 18 基准 |
| 图形渲染 | ❌ 库已提供 | `ToSvg` / `SaveAsImages` |

**结论：唯一需要"补"的是 XML 写入层，不是第三方库。**

---

## E. 页面构图

### E1. 现有布局器的职责边界是什么？

**答：**

| 组件 | 职责 | 不负责 |
| --- | --- | --- |
| `ILayoutEngine`（5 实现） | 在页面上摆放 `Node` | 容器尺寸、页面尺寸、连线 |
| `DiagramLayout.Apply` | 由成员反算容器包围盒 | 区域尺寸分配 |
| `DiagramSizer` | 由文字算节点/容器/页面尺寸 | 位置 |
| `OrthogonalRouter` | 算连线路径 | 节点位置 |
| `LocalRelayout` | 重排**一个**容器内成员 | 其它容器 |
| `LayoutAnalyzer` | 检测 | 修复 |

**关键边界：容器尺寸是"反算"的，不是"分配"的。**
这是 E2 的核心限制。

### E2. 多区域布局是否已有基础？

**答：部分基础，缺关键一环。**

**已有：**
- `Group` 作为区域载体
- `DesignSpec.ContainerLayouts` 逐容器指定策略
- `LocalRelayout` 区域级重排

**缺：没有"区域尺寸由上层决定"的能力。**
`DiagramLayout.Apply` 总是按成员反算 `Group.Bounds`，无法锁定区域大小。
因此"先划分页面成几个区，再各自布局"这个流程无法表达。

**这正是架构提案 ADR-005 要补的：`Group.FixedSize`。**

### E3. LocalRelayout 能解决哪些问题？

**答：** 能解决"单个容器内部排布乱"，且**有隔离保证**：

```csharp
// 重排前后快照容器外所有节点，逐字段比对
if (after.X != before.X || ...) return IsolationBroken = true;
```

**已覆盖（9 项测试）：**
- 容器成员沿边排序（拓扑序，非作者坐标）
- 单行对齐
- 保持容器锚点
- 网格排布
- 环形边不死锁
- 空容器 / 不存在容器报错

**不能解决：**
- 区域尺寸不合适（它只在现有框内排）
- 跨区域连线
- 区域之间挤压/重叠

### E4. 哪些问题属于全局协调？

**答：**

| 问题 | 为何是全局 |
| --- | --- |
| 区域之间的空间分配 | 需要知道所有区域的期望尺寸才能分 |
| 区域重叠/挤压 | 单个区域无法感知邻居 |
| 页面尺寸决策 | 依赖全部内容 |
| 跨区域连线路由 | 两端在不同区域，需全局通道 |
| 标题/图例位置 | 与所有区域竞争边缘空间 |
| 局部改动后的整体再平衡 | 改一处可能影响别处留白 |

### E5. 跨区域路由是否有必要单独建模？

**答：暂不必要，但需要"通道预留"。**

**理由：** 跨区域连线当前走通用 `OrthogonalRouter`，能避障，
问题只在于**没有为连线预留空间**，所以线只能从节点间隙钻。

Phase 7 的扇出实验失败正是这个原因：
7 条线从同一网关发出，任何"行内走廊"都会穿过兄弟节点。

**建议顺序（写入 `risks-and-dependencies.md` R6）：**
1. 先做**通道预留**（布局时为连线留条带）
2. 再考虑共享干线
3. **不要**先建独立的跨区域路由子系统——那会与现有布线器职责重叠

---

## 汇总：30 问的答案分布

| 结论类型 | 数量 | 问题编号 |
| --- | --- | --- |
| ✅ 已可满足 | 9 | A3, A7, B1, D1, D4, D6, E1, E3, E4 |
| ⚠️ 部分满足 / 有条件 | 13 | A1, A5, B2, B3, B4, B5, C1, C6, D2, D3, D5, E2, E5 |
| ❌ 不满足 | 8 | A2, A4, A6, B6, C2, C3, C4, C5 |
| **合计** | **30** | 覆盖 A1–E5，每条只归一个主要结论 |

**四条最重要的结论：**

1. **A5 自由路径**：格式层完全可行（实验 1），模型层完全缺失——读回时被标记为
   `Rectangle` 节点且 `text=null`。
2. **C2 Palette**：唯一一条"协议声明但完全未执行"的字段。它能通过 Schema 校验、
   被设计者写进 DesignSpec，执行时被静默忽略，且**不进 `unmetConstraints`**——
   违反项目自己定的契约。
3. **B2 patch 数量**：若实现自由路径 + 图片 + SVG，XML patch 从 2 处增至 5 处，
   触发 ADR-006 复核条件（需评估是否改为直接操作 OPC）。
4. **E5 跨区域路由**：不建议单独建模。Phase 7 的扇出实验已证明，
   没有通道预留时任何"行内走廊"都会穿过节点。顺序必须先预留、后共享干线。
