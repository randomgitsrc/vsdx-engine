# Phase 8 架构提案

**依据：** `current-state-audit.md`、`capability-matrix.md`
**核心结论：Phase 8 应当是"接入 + 补表达层"，不是"重建引擎"。**

---

## 1. 为什么不做大重构

任务 §5 要求"不允许在尚未确认现状的情况下直接开始大规模重构"。
审计确认了三件事，它们共同否定了大重构的必要性：

**第一，核心执行路径是健康的。**

| 维度 | 状态 |
| --- | --- |
| 创建 / 读取 / 增量编辑 | ✅ 已验证，含三代往返 |
| 五种布局策略 + 泳道 | ✅ 已验证 |
| 硬/软约束分离 + 分数归零 | ✅ 已验证 |
| 三层协议 + 未满足约束报告 | ✅ 28 项契约测试 |

**第二，最大的缺口在依赖库，不在自研代码。**

```
OfficeIMO.Visio 公开类型        : 256
本项目实际使用的类型            : 3  (VisioPage / VisioShape / VisioConnector)
```

母版导入、Stencil 目录、资产元数据（含许可证/归属）、Callout、Comment、
连接线高级路由**都已存在于依赖库**，实测 `ImportMastersAndGet` 返回 5 个母版即可用。

任务 §8.3 设想的"资产系统"，**大部分工作库已完成**。自己重写一遍是纯浪费。

**第三，真正的缺陷都是局部的。**

P0 的几何注入问题出在写入器的一个分支；`Palette` 是漏了一个 `if`；
`ShapeOrder` 是读了没写。这些都不是"架构错了"，是"实现漏了"。

**结论：** 大重构会破坏已验证的 54 项能力，换不来等价收益。**不做。**

---

## 2. 分层演进方案

保留现有四层，只在必要处扩展：

```
┌─────────────────────────────────────────────┐
│ Protocol   DiagramIr / DesignSpec / Result  │  ← 补：多页、能力声明
├─────────────────────────────────────────────┤
│ Engine     Layout / Router / Detector       │  ← 补：区域协调、通道预留
├─────────────────────────────────────────────┤
│ Model      Diagram/Page/Node/Edge/Group     │  ← 补：TextBlock / FreePath
├─────────────────────────────────────────────┤
│ Adapters   Reader / Writer / Bridges        │  ← 修：几何注入；补：Stencil 接入
└─────────────────────────────────────────────┘
```

**不新增项目，不新增程序集。** 新对象类型进 `Vsdx.Model`，
新逻辑进已有的 `Vsdx.Engine` / `Vsdx.Adapters`。

---

## 3. 通用图形模型（对应任务 §4.1）

### 3.1 设计原则：不做万能对象

任务 §4.1 同时要求"不能把语义对象退化成普通 Shape"和"不能设计无限膨胀的万能对象"。
这两个约束的交集是：**新增能力用独立的最小类型，不引入通用 Shape 基类。**

### 3.2 提议的最小对象集合

| 对象 | 是否新增 | 理由 |
| --- | --- | --- |
| `Node` / `Edge` / `Group` | 保留 | 语义接口，不能退化 |
| `TextBlock` | **新增** | 标题/副标题/注释/图例是 4 类真实需求，不能都塞进 Node |
| `FreePath` | **新增** | 自定义几何是唯一无法用 17 个 master 表达的需求 |
| `AssetRef` | **新增** | 指向 Stencil 资产，而非硬编码字符串 |
| 通用 `Shape` 基类 | **不新增** | 会诱导万能对象；用 `Page` 上的并列集合代替 |

`Page` 变为：

```csharp
public sealed class Page
{
    public List<Node> Nodes { get; }         // 语义对象（保留）
    public List<Edge> Edges { get; }         // 语义对象（保留）
    public List<Group> Groups { get; }       // 容器（保留）
    public List<TextBlock> TextBlocks { get; }  // 新增：独立文本
    public List<FreePath> Paths { get; }        // 新增：自由几何
}
```

**为什么不用继承：** 若引入 `abstract Shape` 让 Node/TextBlock/FreePath 继承，
布局器就要处理"任意 Shape"，而布局只对 Node 有意义。
并列集合让每类对象只出现在该出现的算法里。

### 3.3 坐标与 Z-order

- **坐标系**：沿用现有"页面绝对坐标 + 容器相对原点"，不引入变换矩阵

**Z-order 的设计（已按负责人意见修正）：**

初稿提议在 `Page` 上加一个 `int? ZOrderHint`。**这个设计是错的**，已废弃。

`ZOrderHint` 只能表达"整页有一个层次值"，而 Z-order 的本质是**对象之间的全序关系**。
一个标量无法表达"A 在 B 之上、B 在 C 之上、但 D 与它们无关"，
更无法表达跨类型（Node / TextBlock / FreePath）的相对顺序。

**正确设计：显式的顺序列表，由 Reader 保留原始顺序、Writer 消费该顺序。**

```csharp
public sealed class Page
{
    /// <summary>
    /// 页面上所有可见对象的绘制顺序，从底到顶。元素是对象 id
    /// （Node.Id / TextBlock.Id / FreePath.Id / Group.Id 共用同一命名空间）。
    /// Reader 必须按原始 XML 中 Shape 的出现顺序填充；Writer 必须按此列表输出。
    /// 列表可以只覆盖部分对象；未列出的对象排在已列出的之后，保持其集合内顺序。
    /// </summary>
    public List<string>? ShapeOrder { get; set; }
}
```

**为什么用 id 列表而不是容器类型：**

1. 现有 `Page.ShapeOrder` 已经是 `List<string>?`（`DiagramModel.cs:56`），
   直接复用它，不引入新概念
2. 字符串 id 让 Node / TextBlock / FreePath / Group 混合排序成为可能
3. 解析一个列表即可决定写入顺序，无需类型转换

**Reader 的义务：** 必须记录 `visio/pages/pageN.xml` 中 `<Shape>` 的**真实出现顺序**，
而不是按模型集合的插入顺序。当前 `OfficeImoDocumentReader.cs:281` 已经在做这件事，
但顺序来源需要确认是 XML 顺序而非 API 返回顺序。

**Writer 的义务：** 必须按 `ShapeOrder` 输出 `<Shape>` 元素。
当前写入器完全忽略该字段（`grep ShapeOrder OfficeImoDocumentWriter.cs` 为空），
导致**每次保存都丢失 Z-order**。

**Phase 8.1 的验收：** 外部文件含 5 个重叠 shape，明确的前后遮挡关系，
经 `export-model → update` 后，包内 `<Shape>` 顺序必须与原文件一致。

---

## 4. 视觉样式执行（对应任务 §4.2）

### 4.1 要修的

| 项 | 修法 |
| --- | --- |
| `theme.palette` 不执行 | 实现 4 套命名调色板；未知 palette 报 `unmetConstraints` |
| 容器标题/连线标签字号固定 | 纳入 `TypeScale`，与正文一起推导 |
| 样式继承缺失 | 引入三级：角色默认 → 对象类型默认 → 对象覆盖 |

### 4.2 三级样式解析

```
DesignSpec.Theme (角色色 + palette)
        ↓
Engine 默认（TypeScale 推导字号）
        ↓
Node.Style（显式覆盖，最高优先）
```

实现为一个纯函数，而不是在十几个地方各自 if：

```csharp
public static ResolvedStyle Resolve(Node node, DiagramIr ir, DesignSpec spec);
```

**为什么重要：** 当前 `DesignSpecApplier` 里样式逻辑散在 3 处（typography / roleColors / background），
新增 palette 只能再加一处。集中成解析器才能支撑"多套风格"。

### 4.3 不做的

- 不内置"Minimal / Corporate / Technical"三套固定风格包。
  任务 §8.2 明确说"不得把三种示例风格固化为 Engine 唯一支持的设计模板"。
  Engine 提供 **palette + 角色色 + 字号层级**这套参数面，风格由 DesignSpec 组合。

---

## 5. 资产体系（对应任务 §4.3）

### 5.1 关键判断：不新建资产系统，接入 Stencil

审计证明库已有：

```csharp
VisioStencilCatalog.Load(...)          // 加载外部 stencil
VisioStencilShape.SourceLicense        // 许可证
VisioStencilShape.SourceAttribution    // 归属
VisioStencilShape.PreviewImage         // 预览
VisioStencilShape.SourceConnectionPoints // 连接点
VisioGraphDiagramBuilder.StencilNode(id, text, stencil)  // 创建
```

这已覆盖任务 §8.3 列出的 10 项元数据中的 7 项。

### 5.2 提议的接入方式

```
MasterCatalog（现有 17 项）
        +
StencilCatalog（新增，包在 VisioStencilCatalog 之上）
        ↓
AssetResolver.Resolve(semanticId) → 原生 master / stencil shape / 降级到矩形
```

**降级必须显式：** 找不到语义资产时，回退到矩形并**在 ExecutionResult 报告**，
不得静默替换。

### 5.3 图片与 SVG：实验已完成，进入实现

> **本节已修订。** 原文写"需要先做实验，实验前不承诺图片能力"。
> 实验已在 Phase 8.0 完成并通过，该前提不再成立。

**实验结论**（`technical-experiments.md`）：

| 项 | 包格式 | 产品实现 |
| --- | --- | --- |
| 图片（PNG） | ✅ 可行 | ❌ 零支持 |
| SVG | ✅ 可行（需补 Content-Type） | ❌ 零支持 |

**Phase 8.3 的实现任务（不再是实验）：**

1. 媒体写入器：`visio/media/*` + `pageN.xml.rels` + `[Content_Types].xml`
2. 模型字段：`ImageAsset { MediaPath, Format, Bytes }`
3. 读取器：识别 `Type="Foreign"` + `ForeignData`，关联到模型
4. **Renderer 显示真实图片**——负责人明确：占位框只允许是阶段性限制，
   不接受作为 8.3 验收结果
5. 图片替换（增量编辑已有图片）

**新识别的依赖风险：** Renderer 显示真实位图可能需要图像解码库
（SkiaSharp / ImageSharp / System.Drawing）。这需要单独评估：
许可证、包体积、跨平台行为。**不得在未评估前引入。**

---

### 5.4 依赖冲突的解决：8.1 语义符号 vs 8.3 Stencil（负责人意见 5）

**冲突是什么**

任务 §6 要求 Phase 8.1 的纵向切片包含"一个组合图形或语义符号"。
但语义符号的实现路径是 Stencil / 原生 master，而那属于 Phase 8.3。

若 8.1 必须等 8.3，则 8.1 无法完成；若 8.1 自己造符号，则与 8.3 重复。

**解决原则：8.1 验证"引用链路"，8.3 验证"资产体系"。**

| | Phase 8.1 | Phase 8.3 |
| --- | --- | --- |
| 目标 | 证明**模型能承载并往返**一个语义符号引用 | 证明**能从外部加载**资产并完整管理 |
| 手段 | 用**内置 master**（`MasterCatalog` 已有的 17 个之一，如 `Process`） | `VisioStencilCatalog` + `ImportMasters` + 外部 .vssx |
| 不需要 | 外部资产包 | — |
| 验收 | 符号形状写入、读回、ID 稳定、可增量改 | 资产元数据、许可证、缺失降级、替换 |

**具体做法：**

`Node` 增加一个最小字段：

```csharp
public sealed class Node
{
    /// <summary>
    /// 原生 master 引用。为 null 时退回 Node.Shape 的字符串映射。
    /// 8.1 只用内置 master 验证；8.3 接入 stencil 后可使用外部资产。
    /// </summary>
    public string? MasterRef { get; set; }
}
```

8.1 的纵向切片用 `MasterRef = "Process"` 验证一条完整链路：
模型 → 写入 `<Shape Master="...">` → 读回 `MasterRef` → 再次保存不变。

8.3 则把 `MasterRef` 的取值域从"17 个内置名"扩展到"任意 stencil 资产 id"，
并补 `AssetResolver` 与降级路径。

**这样两个阶段不重叠也不互相阻塞：**
8.1 只关心"引用能不能存活"，8.3 才关心"资产从哪来、缺了怎么办"。

**若 8.1 发现内置 master 无法写入 `Master` 属性**，
则该发现本身是有价值的结论，应在 8.1 报告中记录，并让 8.3 改用直接 patch
（`VisioShape.Master` 反射确认可写，但未实测）。

---

## 6. 构图协调（对应任务 §4.4）

### 6.1 现状

有容器机制、有 `LocalRelayout`、有逐容器策略覆盖。
**但没有"区域"概念**——容器是被动算出来的（包住成员），不是主动分配的。

### 6.2 Group.AutoSize / FixedSize / ContainerLayouts 三者关系（负责人意见 6）

**先说一个审计遗漏：`AutoSize` 已经存在，且已在承担"固定尺寸"的职责。**

```csharp
// DiagramModel.cs:74
public bool? AutoSize { get; set; }

// DiagramLayout.cs:33 — AutoSize == false 时跳过反算，保留现有 Bounds
if (group.Bounds is not null && group.AutoSize == false) continue;

// OfficeImoDocumentWriter.cs:114 — 传给 Visio 容器
page.AddToContainer(container.Id, memberIds, resizeToFit: group.AutoSize ?? true);
```

所以初稿提议新增 `FixedSize` 是**重复造概念**。正确做法是复用 `AutoSize`。

#### 三者职责（明确划分）

| 字段 | 归属 | 作用 | 谁消费 |
| --- | --- | --- | --- |
| `Group.AutoSize: bool?` | 模型 | **是否由内容反算尺寸**。`false` = 保留作者给的 Bounds | `DiagramLayout.Apply`、写入器 `resizeToFit` |
| `Group.LayoutStrategy: string?`（新增） | 模型 | **本容器内部的布局策略** | `LocalRelayout` / 布局引擎 |
| `DesignSpec.ContainerLayouts` | **协议** | 由 Spec 批量指定 `容器id → 策略`，**执行时写入** `Group.LayoutStrategy` | `DesignSpecApplier` |

**关系：** `ContainerLayouts` 是**输入通道**，`Group.LayoutStrategy` 是**落地字段**。
Spec 应用后，前者映射到后者；模型独立使用时可直接设后者。

#### 修正后的模型变更

```csharp
public sealed class Group
{
    public Bounds? Bounds { get; set; }         // 已存在
    public bool? AutoSize { get; set; }         // 已存在，复用，不新增 FixedSize
    public string? LayoutStrategy { get; set; } // 新增：本容器策略
}
```

**语义矩阵：**

| `AutoSize` | `Bounds` | 行为 |
| --- | --- | --- |
| `true` / `null` | 任意 | 按成员反算包围盒（现状默认） |
| `false` | 已给 | **保留给定矩形**，成员在框内排布 ← 这就是"区域" |
| `false` | `null` | 无意义组合，`Normalize` 应报错 |

**8.4 的验收改为：** `AutoSize = false` + 显式 `Bounds` + 各自 `LayoutStrategy`
三者组合，验证多区域页面。**不新增 `FixedSize` 字段。**

**这个修正降低的复杂度：** 少一个字段、少一处语义重叠、少一次迁移；
且 `AutoSize=false` 已经有实现与测试路径，8.4 是在其上补"框内排布"与"跨区路由"。

### 6.3 为什么不做独立的 CompositionCoordinator

任务 §9 警告"不能让 Composition Coordinator 变成另一个强制统一构图的系统"。
引入独立协调器会立刻面临"它和布局器谁说了算"的问题。

**用容器 + 固定尺寸 + 区内策略，已经能表达多区域页面**，
且复用已验证的 `LocalRelayout`。等真实用例证明不够，再考虑独立组件。

### 6.4 Fan-out 的边界

任务 §9 明确指出：不能因为一次实验失败就否定共享路径，也不能未经测试就上生产。

Phase 7 的失败原因是"走廊落在节点行内"。若 Phase 8.4 引入区域内的**通道预留**
（布局时给连线留出条带），共享干线才有可能成立。

**因此顺序是：先有通道预留，再谈共享干线。** 顺序颠倒会重演 Phase 7 的失败。

---

## 7. 保真与质量（对应任务 §4.5）

### 7.1 P0 修复：几何注入

**这是 Phase 8.1 的第一件事**，因为它是"保真"的底线。

**问题：** 外部无 Geometry 段的 shape，经 `update` 后被注入硬编码矩形。

**修复方向（需先验证根因）：**

1. 定位是 OfficeIMO `Save` 合成，还是本项目写入器触发
2. 若是库合成 → 在 `Save` 后 patch 掉新增的 Geometry 段（需要记录原始哪些 shape 无几何）
3. 若是项目触发 → 在写入器区分"新建对象"与"保留对象"

**验收：** 外部文件加 1 个节点后，原有 shape 的 Geometry 段数量**不变**。

### 7.2 保真检测（当前完全缺失）

现有 `check-layout` 只看几何合法性，**看不到保真问题**——这就是 P0 能在 283 项测试全绿下存在的原因。

**提议新增 `check-fidelity`：**

```
输入：原 VSDX + 修改后 VSDX
比对：shape 数 / 每 shape 的 cell 集合 / Geometry 段数 / 关系数 / 媒体文件
输出：每条差异，标注 lost / added / modified
```

这比"再加几个单元测试"更能防住同类问题，因为它对**任意**外部文件都成立。

---

## 8. 演进顺序与理由

| 阶段 | 内容 | 为什么这个顺序 |
| --- | --- | --- |
| 8.0 | 本审计 | 门禁 |
| **8.1** | P0 几何修复 + `TextBlock`/`FreePath` + 纵向切片 | 保真是底线；纵向切片能最早证伪架构 |
| 8.2 | 样式解析器 + palette + 字号层级 | 依赖 8.1 的对象类型（TextBlock 需要字号层级） |
| 8.3 | Stencil 接入 + 图片/SVG 实验 | 实验先行；接入成本低于自建 |
| 8.4 | 区域固定尺寸 + 通道预留 | 通道预留必须在共享干线之前 |
| 8.5 | 跨家族验证 + `check-fidelity` | 需要前面各阶段产物 |

**关键依赖：** 8.3 的图片实验若失败，需回退到"图片标记为不支持"并调整 8.5 验收范围。
这是**允许的降级**，不是失败——任务 §7 要求"如果某种图形对象无法映射，
应当提供替代方案和能力声明"。

---

## 9. 对任务书的三点修正建议

任务 §16 允许"发现设计不合理时必须基于技术证据提出修正"。

### 9.1 Phase 8.3 的工作量被高估

任务将其列为"建立真正可扩展的原生图形和资产机制"，听起来要自建。
审计显示 Stencil 目录、资产元数据、许可证字段**库已提供**。

**建议：** 8.3 的重点从"建立系统"改为"接入 + 补元数据 + 实验图片"。
工作量约为原设想的三分之一。

### 9.2 "11 类图形表达能力"应分级

任务 §4.1 列了 11 类（几何图形、任意路径、独立文本、图片、图标符号、复合图形、
组合对象、页面区域、背景装饰、连接关系、图形数据）。

但**图片依赖未验证的库能力**，图标符号依赖外部资产包，
二者不应与"独立文本"（纯 XML 写入即可）同级。

**建议：** 分为"Engine 内生"（文本/路径/区域/组合）与"依赖外部"（图片/SVG/图标包），
后者以实验结论决定是否纳入 Phase 8 验收。

### 9.3 L4 验证应尽早安排，但不阻塞 Phase 8

任务多处要求 L4 Visio 原生验收。当前无环境。

**建议：** 继续如实标注"未验证"，**不得用任何其它层级替代**。
同时建议尽早安排一次真实 Visio 打开测试——Phase 8 要新增图片/SVG/自定义路径，
这些恰恰是 L1–L3 最难保证、L4 最容易暴露问题的特性。
若等 Phase 8 全部做完再验，返工成本会很高。
