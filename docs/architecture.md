# 架构设计

## 1. 设计目标与约束

| 目标 | 对应设计决定 |
| --- | --- |
| 不依赖 Microsoft Visio | 全部文件操作用 OfficeIMO.Visio + 自研 OPC 层 |
| 后端可替换 | `Vsdx.Model` 只定义接口，`Vsdx.Adapters` 提供实现 |
| JSON 是 Agent 的工作模型 | `Diagram` 模型 + JSON Schema，字段稳定 |
| VSDX 是正式交付物 | 写出的包通过自研 OPC/Visio 语义校验 |
| 不静默丢信息 | `CompatibilityReport` 贯穿每个操作；`Page.Unmapped` 保留无法映射的对象 |
| 增量修改 | 读取→改模型→应用到**原包**，未改对象不重写 |
| 预览反映真实文件 | 渲染器总是重新打开磁盘上的 VSDX |

**非目标（第一阶段）**：Word OLE、COM 自动化、GUI。接口为第二阶段预留。

---

## 2. 分层

```
┌───────────────────────────────────────────────┐
│  Vsdx.Cli   (vsdx-tool)                       │  参数解析 / JSON 输出 / 退出码
├───────────────────────────────────────────────┤
│  Vsdx.Protocol                                │  IR / DesignSpec / ExecutionResult
│   ├── DiagramIr           IR → Diagram         │  三层协议的契约
│   ├── DesignSpec          外观应用 + 报告        │
│   ├── ProtocolRunner      阶段编排与状态判定     │
│   └── DesignSpecApplier   应用并记录 unmet      │
├───────────────────────────────────────────────┤
│  Vsdx.Engine                                  │  编排：Create / Read / Update /
│   ├── VsdxEngine          门面                 │        Render / Validate / Diff
│   ├── DiagramLayout       容器几何推导         │
│   ├── OrthogonalRouter    正交避障布线         │
│   └── LayoutAnalyzer      布局质量检查         │
├───────────────────────────────────────────────┤
│  Vsdx.Renderer        Vsdx.Validator          │  渲染 / 结构校验
├───────────────────────────────────────────────┤
│  Vsdx.Adapters                                │  后端实现
│   ├── OfficeImoDocumentReader                 │  VSDX → Diagram
│   ├── OfficeImoDocumentWriter                 │  Diagram → VSDX
│   ├── OfficeImoIncrementalEditor              │  Diagram ⊕ VSDX → VSDX
│   ├── MasterCatalog / ShapeStyler             │  master 映射 / 样式写入
│   └── VisioMetadataBridge                     │  docProps/custom.xml patch
├───────────────────────────────────────────────┤
│  Vsdx.Model          接口 + JSON 模型 + 兼容性报告 │  （无 OfficeIMO 依赖）
└───────────────────────────────────────────────┘
                     │
              OfficeIMO.Visio 3.6.1
```

`Vsdx.Model` 不引用 OfficeIMO，因此替换后端不会波及 CLI、渲染器契约与 Skill。

---

## 3. 适配器接口

`Vsdx.Model/Contracts.cs` 定义：

| 接口 | 职责 | 当前实现 |
| --- | --- | --- |
| `IDocumentReader` | VSDX → `Diagram`，不可映射内容记入 `Page.Unmapped` | `OfficeImoDocumentReader` |
| `IDocumentWriter` | `Diagram` → VSDX | `OfficeImoDocumentWriter` |
| `IIncrementalDocumentEditor` | `Diagram` ⊕ 现有 VSDX → 新 VSDX | `OfficeImoIncrementalEditor` |
| `IDiagramRenderer` | VSDX → PNG/SVG | `OfficeImoVsdxRenderer` |
| `IDocumentValidator` | VSDX 结构与语义校验 | `VsdxPackageValidatorService` |

`VsdxEngine` 构造函数接受全部五个接口的替换实现，默认装配 OfficeIMO 一套。

### 为什么没有直接使用 `VsdxPackageValidator`

见 `docs/technical-validation.md` 的「发现 2」：该验证器对合法包误报，且其 `FixFile` 会
写出损坏的 `[Content_Types].xml`。自研验证器逐项检查并额外检测重复 content type。

---

## 4. 数据模型

```
Diagram
├── id / name / type / modelVersion / orientation / background
├── pageSize{width,height,unit} / style{font,fontSize,palette,gridSize}
├── metadata{created,modified,sourceFile,sourceHash,backend,generator,history[]}
└── pages[]
    ├── id / name / nameU / visioId / size / background
    ├── groups[]  {id,name,kind,members[],parentId,visioShapeId,bounds,style,autoSize}
    ├── nodes[]   {id,name,type,text,shape,visioShapeId,bounds,groupId,style,
    │              shapeData{},shapeDataLabels{},layers[],hyperlink,angle,imported}
    ├── edges[]   {id,from,to,fromPort,toPort,kind,label,labelPosition,lineColor,
    │              lineWeight,linePattern,beginArrow,endArrow,fromPoint,toPoint,
    │              waypoints[],visioShapeId,layers[],shapeData{}}
    ├── layers[]  {id,name,nameU,visible,locked,print,members[]}
    ├── unmapped[]{visioShapeId,masterNameU,reason,rawXml,preserved}
    └── shapeOrder[]   （保留 z 序）
```

### 三套标识

| 标识 | 作用域 | 谁维护 |
| --- | --- | --- |
| `node.Id` | Agent / JSON 模型 | Skill 与 Agent；跨版本保持稳定 |
| `node.VisioShapeId` | VSDX 包内 | 引擎；从包读出后写回 |
| `User.VsdxModelId` 单元格 | VSDX 包内 | 引擎，用于重命名后恢复 `node.Id` |

重命名时改 `node.Id` 并保留 `visioShapeId`，引擎按后者命中同一形状 → 原地更新而非重建。

### 双向关系归一化

`DiagramJson.Normalize()` 补齐只声明了一侧的关系：

- `node.groupId` ⇄ `group.members`
- `node.layers` / `edge.layers` / `group.layers` ⇄ `layer.members`
- 缺失的 `page.size` 从 `diagram.pageSize` 继承

因此手写 JSON 只需声明一侧。

---

## 5. 创建流程

```
model.json
   │ DiagramJson.Load + Normalize
   ▼
Diagram
   │ DiagramLayout.Apply        按成员几何推导容器 bounds（含标题高度/内边距）
   ▼
Diagram
   │ OrthogonalRouter.Route    为被遮挡的连线计算避障 waypoints
   ▼
Diagram
   │ OfficeImoDocumentWriter   写 master / shape / connector / layer / container
   ▼
VSDX
   │ VisioMetadataBridge.Write patch docProps/custom.xml（历史、时间戳）
   ▼
VSDX（+ 自研 Validator 复核）
```

**容器几何**：`AddToContainer` 默认按成员重算包围盒，会覆盖显式坐标。
`DiagramLayout.Apply` 先在模型层算出一致的 bounds，写入时再传
`VisioContainerOptions { AutoResize = false }`，两侧一致。

**容器标题**：`VerticalAlignment = Top` + 上内边距，避免标题压在成员上。

---

## 6. 增量更新流程

```
source.vsdx ──┬── OfficeImoDocumentReader ──▶ 基线模型（用于判断"模型是否见过该对象"）
              │
              └── VisioDocument.Load ──▶ 可编辑的包
                                            ▲
new.json ── Normalize + Layout + Route ────┘
                                            │ 逐对象比对
                                            ▼
                    ┌──────────────────────────────────────┐
                    │ 节点：模型id → visioShapeId →         │
                    │       VsdxModelId → 文本              │
                    │ 边  ：模型id → 端点对                 │
                    │ 组  ：visioShapeId → 同名文本          │
                    └──────────────────────────────────────┘
                                            │
                              只对差异对象调用写 API
                                            ▼
                                   新的 VSDX + UpdateResult
```

三条关键规则：

1. **未匹配到差异就不写。** `ApplyNodeChanges` 逐属性比较，返回 `changed`；
   `RoundTrip_NoOpUpdateIsLossless` 断言 0 个节点被标记修改。
2. **模型从未提及的对象默认保留**，并在兼容性报告中记为 `update.retained-unmodelled`。
   只有 `--delete-missing` 才删除。
3. **边的匹配以端点对为准**，避免重命名节点后新旧连线并存。

`UpdateResult.Changes` 记录每一项改动的 before/after，直接支撑 `diff` 与审计。

---

## 7. 渲染

```
VSDX ── VisioDocument.Load ── 逐页 ──┬── ToSvg()        → .svg
                                     └── SaveAsImage()  → .png
```

- 渲染输入始终是**磁盘上的 VSDX**，不读内存模型，因此预览与交付物必然一致。
- CJK：`FontResolver` 检测文本是否含 CJK，自动挑选字体文件注入 `FontFilePath`；
  缺失时发出 `render.cjk-no-font` 警告而不是输出缺字框。
- 多页面：输出路径按页名派生成 `name-<page>.png`。
- 背景：`--background` / `--transparent` 映射到 `BackgroundColor`。
- 分辨率：`--dpi` → `VisioImageExportOptions.TargetDpi`。

---

## 8. 校验

`VsdxPackageValidatorService` 分六层，每项检查单独报告：

1. **ZIP 完整性**：可读、条目数、`[Content_Types].xml`、`_rels/.rels`
2. **XML 可解析性**：所有 `.xml` / `.rels` 用禁止 DTD 的 Reader 解析
3. **OPC content types**：document / pages / page override、rels default、无重复声明
4. **OPC relationships**：根关系指向 document.xml、document→pages、Rel ID 唯一
5. **Visio 语义**：Page ID、`<Rel>` 解析到真实 part、Shape ID 数字且唯一、
   `<Connect>` 端点存在、容器父子引用有效
6. **后端重载** +（可选）渲染冒烟

`ValidationResult.Checks` 保存每一项的通过状态与细节，因此"验证通过"是可核查的证据，
不是单一布尔值。

---

## 9. 布局质量

`LayoutAnalyzer` 检测：

| Code | 严重度 | 含义 |
| --- | --- | --- |
| `node.overlap` | error | 两节点相交 |
| `edge.through-node` | error | 连线穿过无关节点 |
| `group.member-escapes` | error | 节点未被容器完整包裹 |
| `node.out-of-page` | warning | 节点超出页面 |
| `text.overflow-vertical/horizontal` | warning | 文字超出框（按字符宽度估算） |
| `spacing.too-tight` | warning | 相邻间距小于阈值 |
| `edge.crossing` | warning | 两条连线交叉 |
| `label.overlaps-node` | warning | 连线标签压在节点上 |
| `page.underused` / `page.margins` | info | 页面利用率偏低 / 边距偏大 |

分数 = `100 - 12×errors - 4×warnings - 1×infos`，用于 Agent 迭代收敛。

估算型检查（文字溢出）刻意保守：宁可多报，避免"看起来没超"却实际被裁切。

---

## 10. 第二阶段扩展点（Word OLE）

第一阶段不实现，但已预留：

```
IDocumentReader / IDocumentWriter
        ▲
        │ 第二阶段新增，不影响既有实现
OfficePackageDocumentAdapter
        ├── DocxOleEmbedder      : VSDX → DOCX（OLE 对象 + 预览 EMF/PNG）
        └── DocxOleExtractor     : DOCX → 原始 VSDX（保持二进制完整）
```

设计约束：

- DOCX 侧必须嵌入**原始 VSDX 二进制**，不能只放图片
- 预览图用第一阶段已有的 `render` 产出
- 提取回来的 VSDX 交给现有 `VsdxEngine.Read/Update` 处理，无需新路径

因为 `Vsdx.Model` 不含 OfficeIMO 类型，Word 适配层可以独立引用模型与引擎，不被迫继承
Visio 实现细节。

---

## 11. 目录结构

```
agent-vsdx/
├── src/
│   ├── Vsdx.Model/        模型、JSON、契约、兼容性报告（无 OfficeIMO）
│   ├── Vsdx.Adapters/     OfficeIMO 读写 + 增量编辑 + 元数据桥
│   ├── Vsdx.Engine/       门面、布局推导、正交布线、质量分析
│   ├── Vsdx.Renderer/     PNG/SVG 渲染 + 字体解析
│   ├── Vsdx.Validator/    OPC/Visio 语义校验
│   └── Vsdx.Cli/          vsdx-tool
├── tests/Vsdx.Tests/      94 项测试（10 类）
├── skill/SKILL.md         Agent Skill
├── schemas/               JSON Schema
├── examples/              3 份示例模型
├── demo/                  两轮验收产物
├── tools/                 示例生成脚本
└── docs/
```
