# 自动化测试报告

```bash
dotnet test tests/Vsdx.Tests/Vsdx.Tests.csproj -c Release
```

```
已通过! - 失败: 0，通过: 96，已跳过: 0，总计: 96，持续时间: 3 s
```

环境：Ubuntu 24.04 (WSL2) / .NET SDK 8.0.428 / OfficeIMO.Visio 3.6.1 / xUnit 2.9.2

---

## 1. 覆盖矩阵

| 类别 | 用例 | 通过 | 覆盖点 |
| --- | --- | --- | --- |
| CreateTests | 7 | ✅ | ZIP 包结构、非伪造格式、形状/连线计数、Shape Data、原生容器、多形状族、嵌套输出目录 |
| ReadTests | 9 | ✅ | 节点/文字/几何、样式、中文、连接线、图层、页面尺寸、缺文件、非法包、来源元数据 |
| EditTests | 10 | ✅ | 原地改名、新增、删除、移动缩放、改色、改 Shape Data、未改对象保留、页面尺寸、组改名、变更历史 |
| ConnectorTests | 8 | ✅ | 端点、箭头、标签、新增、悬空端点报错、路由类型、删除、重新连接 |
| GroupTests | 7 | ✅ | 原生容器标记、成员关系、包裹校验、自动尺寸、增量改成员、多组、逃逸检测 |
| RenderTests | 10 | ✅ | SVG、PNG 文件头尺寸、中文字形、CJK 字体解析、DPI 缩放、透明背景、自定义背景、缺页面、覆盖输出、CJK 探测 |
| RoundTripTests | 11 | ✅ | 数量/模型 id/Visio id/Shape Data/样式/端点/页面尺寸稳定、连续三代、空操作无损、容器结构、二进制可复现 |
| ExternalVsdxTests | 9 | ✅ | 手写 OPC 包读取、导出模型、单对象修改、重读、渲染、校验、复杂包保留原生特性、多页面读取与渲染 |
| ValidationTests | 11 | ✅ | 合法包、缺文件、非 ZIP、缺 content types、坏 XML、重复 content type、坏连线引用、重复 Shape ID、渲染冒烟、浅校验、逐项报告 |
| IntegrationTests | 14 | ✅ | 第一轮 15 节点端到端、布局无错误、第二轮完整场景、未改对象保留、第三轮保真、diff 准确、第四轮外部包生命周期、JSON 制品闭环、重叠/穿线/溢出检测、布线避障 |
| **合计** | **96** | **✅** | |

---

## 2. 关键用例说明

### 2.1 为什么外部包测试不依赖 OfficeIMO

`ExternalFixtures` 用纯 `System.IO.Compression` + 手写 XML 构造 VSDX：

```csharp
using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
Write(zip, "[Content_Types].xml", ContentTypes(new[] { "page1.xml" }));
Write(zip, "_rels/.rels", RootRels());
Write(zip, "visio/document.xml", DocumentXml());
Write(zip, "visio/pages/page1.xml", PageXml(...));
```

三份样本：

| 样本 | 内容 |
| --- | --- |
| `external-basic.vsdx` | 3 shape + 1 connector + 样式表，无容器 |
| `external-complex.vsdx` | 容器（含 `msvStructureType`）、4 shape、Shape Data、连线 |
| `external-multipage.vsdx` | 2 页，各自独立页面 |

**只用自己的写入器产出来测自己的读取器不算验证**，所以这些用例的输入完全不经过
`OfficeImoDocumentWriter`。

### 2.2 空操作无损（`RoundTrip_NoOpUpdateIsLossless`）

```csharp
var model = engine.Read(v1).Diagram;          // 原样读回
var result = engine.Update(v1, model, v2);    // 原样写回

Assert.Equal(0, result.NodesAdded);
Assert.Equal(0, result.NodesModified);        // ← 关键：不得误报修改
Assert.Equal(0, result.NodesDeleted);
Assert.True(result.NodesPreserved >= 3);
```

这条用例在开发中真实抓到一个缺陷：形状 `Name` 为空而模型把它规范化为节点 id，
导致每个节点都被判定为"重命名"。修复后此用例通过。

### 2.3 单对象修改计数（`External_ModifyOneObjectAndSave`）

```csharp
var target = model.Pages[0].Nodes.First(n => n.Text == "Legacy CRM");
target.Text = "Legacy CRM (retired)";
target.Style.Fill = "#C00000";

var result = engine.Update(path, model, output);

Assert.Equal(1, result.NodesModified);       // 只有这一个
Assert.True(result.NodesPreserved >= 2);     // 其余保留
```

实际输出：

```
updated …/external-modified.vsdx
  nodes: +0 ~1 -0 =2 preserved
  node.modified  2  text='Legacy CRM' … fill=#70AD47 -> text='Legacy CRM (retired)' … fill=#C00000
```

### 2.4 重复 content type 检测（`Validate_DetectsDuplicateContentTypeOverride`）

复现 OfficeIMO 3.6.1 `FixFile` 会写出的损坏形态：

```xml
<Override PartName="/visio/pages/pages.xml" ContentType="application/vnd.ms-visio.pages+xml" />
<Override PartName="/visio/pages/pages.xml" ContentType="application/vnd.ms-visio.page+xml" />
```

断言验证器报 `opc.duplicate-override`。上游验证器对这种包返回 `True`（认为合法）。

### 2.5 连续三代往返（`RoundTrip_ThreeGenerationsRemainStable`）

```
g1 ──update──▶ g2 ──update──▶ g3 ──update──▶ g4
```

每代快照（页面尺寸 / 节点文字几何样式分组 / 连线端点标签 / 组名成员 / 图层）必须与
g1 完全一致。防止逐代漂移。

### 2.6 二进制可复现（`RoundTrip_BinaryIsReproducibleForSameModel`）

同一模型写两次，对所有非 `docProps` 的 XML part 求 SHA-256，必须相等。
这保证"同一个模型得到同一个文件"，是可审计性的基础。

### 2.7 布局质量（`Scenario_Round1_LayoutHasNoErrors`）

```csharp
var report = LayoutAnalyzer.Analyze(engine.Read(vsdx).Diagram);
Assert.Empty(report.Errors);
Assert.True(report.Score >= 80);
```

---

## 3. 验收场景结果

| 轮次 | 要求 | 结果 |
| --- | --- | --- |
| 第一轮 | 15 节点 / 3 分层 / 层内+跨层连线 / ≥2 种节点类型 / 分组 / 合理布局 | ✅ `Scenario_Round1_FifteenNodeArchitecture`：15 shape、3 组、14 连线、≥2 形状族、PNG+SVG+JSON 齐全、valid |
| 第一轮 | 布局无错误 | ✅ score **95**，errors **0** |
| 第二轮 | 改名 / 新增 / 连接 / 移动 / 改组名 | ✅ `Scenario_Round2_FullIncrementalScenario`：精确 `+1 ~2 -0`，old id 消失、新 id 出现、边重定向、组名更新 |
| 第二轮 | 保留未修改对象 | ✅ `Scenario_Round2_PreservesUntouchedObjects` |
| 第三轮 | 往返保真 + 差异报告 | ✅ `Scenario_Round3_RoundTripFidelity`（仅 1 项指纹不同）、`Scenario_Round3_DiffReportIsAccurate` |
| 第四轮 | 外部 VSDX 读/导出/改/存/重读/渲染 | ✅ `Scenario_Round4_ExternalPackageLifecycle` |

---

## 4. Demo 产物验证

```
$ vsdx-tool validate --input demo/architecture.vsdx
  [PASS] zip.readable                      20 entries
  [PASS] xml.parseable                     20 XML parts checked, 0 invalid
  [PASS] opc.document-override
  [PASS] opc.pages-override
  [PASS] opc.page-overrides                1 page part(s)
  [PASS] opc.no-duplicate-overrides
  [PASS] opc.document-relationship
  [PASS] opc.pages-relationship
  [PASS] visio.pages-present               1 page(s)
  [PASS] visio.shape-ids-unique            page1.xml=18
  [PASS] visio.connector-references        28 connect row(s), 0 invalid
  [PASS] visio.group-membership            0 parent link(s), 0 broken
  [PASS] backend.reload                    1 page(s), 18 shape(s), 14 connector(s)
VALID
```

```
$ vsdx-tool check-layout --input demo/architecture.vsdx --json
  score 95, errors 0
$ vsdx-tool check-layout --input demo/architecture_v2.vsdx --json
  score 99, errors 0
```

三份示例模型：

| 示例 | 形状 | 连线 | 布局分 | 错误 |
| --- | --- | --- | --- | --- |
| `examples/architecture.json` | 15 | 14 | 95 | 0 |
| `examples/workflow.json` | 9 | 9 | 99 | 0 |
| `examples/network.json` | 11 | 11 | 99 | 0 |

---

## 5. 未覆盖部分

| 项 | 原因 |
| --- | --- |
| Microsoft Visio 原生打开 | 环境无 Visio（见 `docs/limitations.md` 第 7 节验收清单） |
| 大型文件（>1000 形状）性能 | 超出第一阶段范围，未做基准 |
| .vsdm 宏启用包 | 不在范围 |
| 嵌套组在 Visio 中的真实层级 | 已知限制 L7，渲染正确但 Visio 内为兄弟容器 |
| 极端字体/排版差异 | 渲染为近似，需 Visio 对照 |

---

## 6. 复现

```bash
dotnet test tests/Vsdx.Tests/Vsdx.Tests.csproj -c Release

# 按类别
for c in CreateTests ReadTests EditTests ConnectorTests GroupTests \
         RenderTests RoundTripTests ExternalVsdxTests ValidationTests IntegrationTests; do
  dotnet test tests/Vsdx.Tests/Vsdx.Tests.csproj -c Release \
    --filter "FullyQualifiedName~Vsdx.Tests.$c"
done
```

测试产物写入 `tests/Vsdx.Tests/bin/Release/net8.0/testout/`，不污染仓库。
