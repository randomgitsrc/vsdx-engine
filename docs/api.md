# API 参考

两套等价接口：C# API 与 CLI。CLI 是 `VsdxEngine` 的薄封装，行为完全一致。

---

## 1. C# API

### 1.1 入口

```csharp
using Vsdx.Engine;      // VsdxEngine, DiagramLayout, LayoutAnalyzer, OrthogonalRouter
using Vsdx.Model;       // Diagram, Node, Edge, Group, Layer, 契约, CompatibilityReport
using Vsdx.Adapters;    // 后端实现（需要替换后端时才直接引用）
using Vsdx.Renderer;    // FontResolver
using Vsdx.Validator;   // VsdxPackageValidatorService
```

```csharp
var engine = new VsdxEngine();                       // 默认装配 OfficeIMO 后端
var engine = new VsdxEngine(reader, writer, editor, renderer, validator);  // 自定义后端
```

### 1.2 创建

```csharp
WriteResult Create(string modelPath, string outputPath, WriteOptions? options = null);
WriteResult Create(Diagram diagram, string outputPath, WriteOptions? options = null);
```

```csharp
var result = engine.Create("model.json", "out.vsdx", new WriteOptions
{
    Title = "企业架构图",
    Author = "platform-team",
    AutoLayout = true,          // 推导容器 bounds 并自动布线（默认 true）
    RequestRecalc = true,       // 请求 Visio 打开时重算（默认 true）
    ValidateAfterSave = true    // 保存后自动校验（默认 true）
});

Console.WriteLine($"{result.ShapesWritten} shapes, {result.ConnectorsWritten} connectors");
foreach (var issue in result.Compatibility.Issues)
    Console.WriteLine($"[{issue.Severity}] {issue.Code}: {issue.Message}");
```

`WriteResult`: `OutputPath`、`Compatibility`、`PagesWritten`、`ShapesWritten`、
`ConnectorsWritten`、`FileSize`、`Backend`

### 1.3 读取

```csharp
ReadResult Read(string vsdxPath);
ReadResult ExportModel(string vsdxPath, string modelPath);
InspectResult Inspect(string vsdxPath);
```

```csharp
var read = engine.Read("diagram.vsdx");
var page = read.Diagram.Pages[0];
Console.WriteLine($"{page.Nodes.Count} nodes, {page.Edges.Count} edges");
Console.WriteLine($"source hash: {read.Diagram.Metadata.SourceHash}");
```

`ReadResult`: `Diagram`、`Compatibility`、`SourcePath`、`Backend`、`BackendVersion`、`FileSize`

`InspectResult`: `Read`（`ReadResult`）、`Validation`（`ValidationResult`）

### 1.4 增量更新

```csharp
UpdateResult Update(string sourceVsdxPath, string modelPath, string outputPath, UpdateOptions? options = null);
UpdateResult Update(string sourceVsdxPath, Diagram diagram, string outputPath, UpdateOptions? options = null);
```

```csharp
var model = engine.Read("v1.vsdx").Diagram;
var node  = model.Pages[0].Nodes.Single(n => n.Id == "query-service");

node.Id           = "analytics-service";   // 新的稳定 id
node.Text         = "分析服务\nAnalytics Service";
node.VisioShapeId = "query-service";       // 固定到原形状 → 原地更新

foreach (var e in model.Pages[0].Edges)
{
    if (e.From == "query-service") e.From = "analytics-service";
    if (e.To   == "query-service") e.To   = "analytics-service";
}

var result = engine.Update("v1.vsdx", model, "v2.vsdx", new UpdateOptions
{
    DeleteMissing = false,       // true 才删除模型中不存在的对象
    AutoLayout = true,           // 推导容器 bounds、重算被遮挡连线
    RecordHistory = true,        // 写入变更历史（默认 true）
    ValidateAfterSave = true
});

Console.WriteLine($"+{result.NodesAdded} ~{result.NodesModified} -{result.NodesDeleted} ={result.NodesPreserved}");
foreach (var change in result.Changes)
    Console.WriteLine($"{change.Kind} {change.Target}: {change.Before} -> {change.After}");
```

`UpdateResult`: 上述计数 + `Changes`（`List<ModelChange>`）、`Compatibility`、`FileSize`

### 1.5 渲染

```csharp
RenderResult Render(RenderRequest request);
```

```csharp
var render = engine.Render(new RenderRequest
{
    InputPath    = "diagram.vsdx",
    OutputPath   = "diagram.png",
    Format       = "png",              // "png" | "svg"
    Dpi          = 144,
    PageIndex    = 0,                  // 省略 = 全部页面
    PageName     = null,               // 与 PageIndex 互斥，优先使用
    Background   = "#FFFFFF",          // 省略 = 页面默认
    Transparent  = false,
    FontFile     = null,               // 显式字体；省略则自动探测
    FontFace     = null,
    RenderText   = true
});

Console.WriteLine($"font: {render.FontUsed}");
foreach (var p in render.Pages)
    Console.WriteLine($"{p.OutputPath} {p.Width}x{p.Height} ({p.Bytes} bytes)");
```

`RenderResult`: `Pages`（`RenderedPage[]`）、`Compatibility`、`FontUsed`、`Backend`

`RenderedPage`: `PageIndex`、`PageName`、`OutputPath`、`Format`、`Bytes`、`Width`、`Height`

### 1.6 校验

```csharp
ValidationResult Validate(string vsdxPath, ValidationOptions? options = null);
```

```csharp
var v = engine.Validate("diagram.vsdx", new ValidationOptions { Deep = true, RenderSmoke = true });
foreach (var check in v.Checks)
    Console.WriteLine($"[{(check.Passed ? "PASS" : "FAIL")}] {check.Name}: {check.Detail}");
```

`ValidationResult`: `IsValid`、`Errors`、`Warnings`、`Checks`

### 1.7 对比

```csharp
DiagramDiff Diff(string beforePath, string afterPath);
```

```csharp
var diff = engine.Diff("v1.vsdx", "v2.vsdx");
foreach (var entry in diff.Changes)
    Console.WriteLine(entry);
Console.WriteLine($"added={diff.Added.Count()} removed={diff.Removed.Count()} modified={diff.Modified.Count()}");
```

`DiffEntry.Kind`: `node.*` / `edge.*` / `group.*` / `page.*` +
`.added` / `.removed` / `.modified`

### 1.8 布局工具

```csharp
// 按成员几何推导容器 bounds（写入前自动调用）
DiagramLayout.Apply(diagram);

// 检查容器是否完整包裹成员
List<string> problems = DiagramLayout.CheckContainment(page);

// 为正交连线计算避障路径（写入前自动调用；显式 waypoints 不会被覆盖）
int routed = OrthogonalRouter.Route(diagram);

// 布局质量分析
LayoutReport report = LayoutAnalyzer.Analyze(diagram);
Console.WriteLine($"score={report.Score} errors={report.Errors.Count()}");

// 单独检查某条直线是否穿过第三个节点
string? blockingNodeId = LayoutAnalyzer.CheckEdgeThroughNode(from, to, others, edge);
```

`LayoutReport`: `Issues`、`Errors`、`Warnings`、`Infos`、`IsClean`、`Score`（0–100）、
`PageUtilisation`、`CountByCode()`

`LayoutIssue`: `Code`、`Severity`、`Page`、`Targets`、`Message`

### 1.9 JSON 模型

```csharp
Diagram d = DiagramJson.Load("model.json");
string json = DiagramJson.Serialize(d);
DiagramJson.Save(d, "out.json");
Diagram d2 = DiagramJson.Deserialize(json);
```

`DiagramJson.Normalize(diagram)` 补齐只声明一侧的关系（`groupId` ⇄ `members`、
`layers` ⇄ `layer.members`），加载时自动调用。

### 1.10 自定义后端

```csharp
public sealed class MyReader : IDocumentReader { /* ... */ }
public sealed class MyWriter : IDocumentWriter { /* ... */ }

var engine = new VsdxEngine(
    reader: new MyReader(),
    writer: new MyWriter(),
    renderer: new OfficeImoVsdxRenderer(),
    validator: new VsdxPackageValidatorService());
```

接口定义见 `vsdx-engine/docs/architecture.md` 第 3 节。

---

## 2. CLI

通用规则：

- 所有命令支持 `--json`，输出稳定 JSON
- 退出码：`0` 成功 / `1` 有错误 / `2` 用法错误
- 相对路径按当前工作目录解析

### 2.1 `create`

```bash
vsdx-tool create --input <model.json> --output <file.vsdx>
                 [--title <text>] [--author <text>] [--no-validate] [--json]
                 [--layout layered|grid|flow|tree|manual|auto]
                 [--fit-text] [--fit-page] [--recenter]
```

**布局参数**（最常用，缺省时按模型自带坐标）：

| 参数 | 作用 |
| --- | --- |
| `--layout <strategy>` | 覆盖 `type` 推断的布局策略；`auto` 按图类型选择 |
| `--fit-text` | 把框撑到文字所需的最小尺寸 |
| `--fit-page` | 把页面缩到内容包围盒 |
| `--recenter` | 把内容居中到页面上 |

不加这三个参数、模型又没有合理坐标时，得到的图**会被判 `page.underused`**
（内容只占页面一小块）——这是 `skill/SKILL.md` 里那条示例曾经踩过的坑。

### 2.2 `inspect`

```bash
vsdx-tool inspect --input <file.vsdx> [--json]
```

JSON 输出含：`pages[]`（含 `nodesDetail` / `edgesDetail` / `layers` / `groups`）、
`validation`、`compatibility`。

### 2.3 `export-model`

```bash
vsdx-tool export-model --input <file.vsdx> --output <model.json> [--json]
```

### 2.4 `update`

```bash
vsdx-tool update --input <source.vsdx> --model <model.json> --output <file.vsdx>
                 [--delete-missing] [--no-validate] [--no-recalc] [--json]
```

`--delete-missing`：删除模型中不存在的对象。默认保留并报告。

`--no-recalc`：不写入「请求 Visio 打开时重算」标记。默认写入——那是为了让人在 Visio 里
编辑时布局跟着更新；若你希望产物保持当前几何不变，加这个参数。

### 2.5 `render`

```bash
vsdx-tool render --input <file.vsdx> --format <png|svg> [--output <path>]
                 [--page <n>] [--page-name <name>] [--dpi <n>]
                 [--background <#RRGGBB|transparent>] [--transparent]
                 [--font-file <path>] [--font-face <name>] [--no-text] [--json]
```

省略 `--output` 时，输出到输入文件同目录、同名的 `.png` / `.svg`。
多页面且未指定 `--page` 时，文件名派生为 `<name>-<page>.<ext>`。

### 2.6 `validate`

```bash
vsdx-tool validate --input <file.vsdx> [--shallow] [--render-smoke] [--json]
```

`--shallow` 跳过后端重载；`--render-smoke` 追加 SVG 渲染冒烟测试。

检查项：

| 名称 | 内容 |
| --- | --- |
| `zip.readable` | ZIP 可读、条目数 |
| `zip.has-content-types` | 存在 `[Content_Types].xml` |
| `xml.parseable` | 所有 XML part 良构 |
| `opc.document-override` | `document.xml` → drawing main |
| `opc.pages-override` | `pages.xml` → pages+xml |
| `opc.page-overrides` | 每个 page part → page+xml |
| `opc.no-duplicate-overrides` | 同 part 不得有两个 content type |
| `opc.rels-default` | `.rels` 默认类型 |
| `opc.root-rels-count` | 根关系存在 |
| `opc.document-relationship` | `.rels` 指向 document.xml |
| `opc.pages-relationship` | document 关联 pages.xml |
| `visio.pages-present` | 至少一个 `<Page>` |
| `visio.page[N].shapes` | 每页形状数与唯一 ID 数 |
| `visio.shape-ids-unique` | Shape ID 数字且唯一 |
| `visio.connector-references` | `<Connect>` 两端存在 |
| `visio.group-membership` | 父引用有效 |
| `backend.reload` | OfficeIMO 可重新加载 |
| `render.svg-smoke` | （可选）SVG 产出非空 |

### 2.7 `check-layout`

```bash
vsdx-tool check-layout --input <file.vsdx> [--json]
```

问题代码见 `docs/architecture.md` 第 9 节。

### 2.8 `diff`

```bash
vsdx-tool diff --before <a.vsdx> --after <b.vsdx> [--json]
```

### 2.9 `batch`

```bash
vsdx-tool batch --op <create|render|validate|export-model> --out-dir <dir>
                --input <file|dir> [--input <file|dir> ...]
                [--input-dir <dir>] [--input-glob <pattern>]
                [--format png|svg] [--dpi <n>] [--background <c>] [--transparent]
                [--font-file <path>] [--json]
```

批量处理一批文件。**单项失败不会中断整个批次**，每项结果独立报告。
`validate` 不需要 `--out-dir`；其余操作需要。

```bash
# 一次创建目录下所有 JSON 模型
vsdx-tool batch --op create --input-dir examples --input-glob "*.json" --out-dir out/

# 批量渲染
vsdx-tool batch --op render --input out/ --out-dir out/ --format png --dpi 120

# 批量校验
vsdx-tool batch --op validate --input out/ --json
```

JSON 输出：

```json
{
  "ok": true, "operation": "create", "total": 3, "succeeded": 3, "failed": 0,
  "items": [{ "input": "examples/architecture.json", "output": "out/architecture.vsdx",
              "ok": true, "detail": "shapes=15 connectors=14", "issues": 0 }]
}
```

C# 等价：

```csharp
var runner = new BatchRunner(new VsdxEngine());
var result = runner.CreateMany(items);          // (modelPath, outputPath)[]
var render = runner.RenderMany(pairs, template);
var check  = runner.ValidateMany(paths);
var export = runner.ExportMany(pairs);
```

`BatchResult`: `Operation`、`Total`、`Succeeded`、`Failed`、`Ok`、`Items`
`BatchItem`: `Input`、`Output`、`Ok`、`Detail`、`Error`、`Issues`

### 2.10 `execute`

三层协议的入口：Diagram IR + 可选 DesignSpec → VSDX + ExecutionResult。

```bash
vsdx-tool execute --ir <model.json> [--spec <design.json>] --output <file.vsdx> [--json]
                  [--render] [--format png|svg]
```

`--json` 输出为 ExecutionResult：

| 字段 | 含义 |
| --- | --- |
| `status` | `succeeded` / `partial` / `failed` / `degraded` |
| `appliedDesignNotes` | 实际执行了什么，含推断结果（如 `direction=topToBottom (auto)`、`canvas.paper=A3`） |
| `unmetConstraints` | **每一条未能满足的请求及原因**；引擎不静默丢弃 |
| `quality.hardConstraintsPassed` | 交付前必查的门禁 |
| `stagesCompleted` | 已完成的阶段（`ir.validate` → `quality.check`） |

`status: partial` 表示文件已写出但部分请求未满足——此时必须读 `unmetConstraints`。

**DesignSpec 是内容与外观的分离**：同一份 IR 配不同 spec 得到不同外观，可用 `diff` 证明
是哪个 spec 产出了哪个文件。

### 2.11 `compare`

生成多个候选布局，按硬约束筛选后以软指标排序。

```bash
vsdx-tool compare --input <file.vsdx> [--output <file.vsdx>] [--emit-dir <dir>] [--json]
```

`--emit-dir` 会保留每个候选，便于逐个查看后再选。

### 2.12 `repair`

有界改进循环：规划 → 尝试 → 接受或回滚 → 升级 → 止损。

```bash
vsdx-tool repair --input <file.vsdx> [--output <file.vsdx>] [--max-rounds <n>] [--max-attempts-per-issue <n>] [--json]
```

**有界**是关键：它不会无限重试。每轮记录尝试与结果，无法改进时停止而不是继续消耗时间。

### 2.13 `version`

```bash
vsdx-tool version [--json]
```

报告工具版本、后端版本与模型版本。版本号由程序集元数据驱动，不硬编码。

---

## 3. 错误与诊断

### 3.1 退出码

| 码 | 含义 |
| --- | --- |
| 0 | 成功（可能含 warning / info） |
| 1 | 已完成但存在 error（见输出） |
| 2 | 用法错误（缺参数、非法值、未知命令） |

### 3.2 JSON 错误形态

```json
{ "ok": false, "error": { "code": "file.not-found", "message": "VSDX not found: x.vsdx" } }
```

### 3.3 兼容性报告

每个操作返回：

```json
{
  "compatibility": {
    "lossless": false,
    "summary": "0 error(s), 1 warning(s), 0 info",
    "issues": [
      { "severity": "warning", "code": "render.cjk-no-font",
        "message": "Diagram contains CJK text but no CJK-capable font…",
        "target": null }
    ]
  }
}
```

代码清单见 `docs/limitations.md` 第 8 节。

---

## 4. 完整示踪示例

```bash
set -e
MODEL=examples/architecture.json
V1=/tmp/a.vsdx
V2=/tmp/a_v2.vsdx

vsdx-tool create       --input $MODEL          --output $V1 --json
vsdx-tool validate     --input $V1             --json
vsdx-tool check-layout --input $V1             --json
vsdx-tool render       --input $V1 --format png --output /tmp/a.png --dpi 120 --json
vsdx-tool export-model --input $V1             --output /tmp/a.json --json

# 编辑 /tmp/a.json 后
vsdx-tool update --input $V1 --model /tmp/a.json --output $V2 --json
vsdx-tool diff   --before $V1 --after $V2 --json
vsdx-tool render --input $V2 --format svg --output /tmp/a_v2.svg --json
```
