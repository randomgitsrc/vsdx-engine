# 技术验证报告（P0–P3）

> 本文档记录 **OfficeIMO.Visio 3.6.1 的实际能力验证结果**。所有结论均来自本仓库中真实运行的代码，不采信 README 的功能描述。
>
> 验证环境：Ubuntu 24.04 / WSL2，.NET SDK 8.0.428，OfficeIMO.Visio 3.6.1（commit `923a2f612e7f24f6893d97b0798c377165603e18`）。

---

## 结论摘要

| 阶段 | 内容 | 结果 | 说明 |
| --- | --- | --- | --- |
| P0 | 创建 / 保存 / 重载 / SVG / PNG | **通过** | 只需渲染文本时显式提供 CJK 字体文件 |
| P1 | Group / Container / Shape Data / 多页面 / 端点粘附 / 样式 | **通过** | 容器需显式给 bounds，否则按成员自动包裹 |
| P2 | 加载 / 修改 / 增删 Shape 与 Connector | **通过** | 增量编辑可行；重命名需按 `User` 单元格维护稳定 ID |
| P3 | 往返保真（未修改对象、原始 ID、Group/Connector 关系） | **通过** | 见下方"已知限制"中的 3 条约束 |

**总体判断：OfficeIMO.Visio 足以作为第一阶段后端**，但有 3 处必须由适配层补偿，否则会产生错误文件或错误报告。这三处已在 `Vsdx.Adapters` 中修复并有回归测试覆盖。

---

## P0：基本能力

### 验证内容

创建 VSDX、创建 Shape、创建 Connector、保存、重新加载、导出 SVG、导出 PNG。

### 结果：通过

```
page id=0 name=Diagram w=11 h=8.5
builtin masters (17): Rectangle, Ellipse, Square, Circle, Dynamic connector,
Diamond, Triangle, Process, Decision, Data, Preparation, Manual operation,
Off-page reference, Parallelogram, Hexagon, Trapezoid, Pentagon
shapes: n_frontend, n_gateway, n_db, n_dec, n_note
connectors: c1:Straight, c2:Straight, c3:RightAngle
saved bytes=23172
validate errors=0
```

生成包结构（`unzip -l`）：

```
visio/document.xml
visio/pages/pages.xml
visio/pages/page1.xml
visio/windows.xml
_rels/.rels
visio/_rels/document.xml.rels
visio/pages/_rels/pages.xml.rels
[Content_Types].xml
docProps/{core,app,custom}.xml
```

这是一个结构完整的 OPC 包，不是改名后的图片。

### 发现 1（必须补偿）：PNG 渲染缺省字体不含中日韩字形

使用默认渲染参数导出含中文的 PNG，中文全部变成 `□`（缺字框）：

| `FontFilePath` | 结果 |
| --- | --- |
| 未指定 | 中文渲染为缺字框 |
| `NotoSansSC-VF.ttf` | 中文正常 |
| `wqy-zenhei.ttc` | 中文正常 |
| `simsun.ttc` / `msyh.ttc` | 中文正常 |

**原因**：`VisioPngSaveOptions.FontFilePath` 未设置时回退到内置的托管轮廓字体，该字体没有 CJK 覆盖。SVG 不受影响（用 `font-family` 交给浏览器）。

**补偿**：`Vsdx.Renderer.FontResolver` 在检测到文本含 CJK 时自动挑选可用的 CJK 字体文件并注入 `FontFilePath`；找不到时在兼容性报告中给出明确警告，而不是静默输出乱码。

### 发现 2（必须补偿）：`VsdxPackageValidator.ValidateFile` 对本库产物误报

对**未经修改**的合法包调用 `new VsdxPackageValidator().ValidateFile(path)` 返回 `False`，错误为：

```
Missing override for /visio/pages/pages.xml
```

但该 override 在 `[Content_Types].xml` 中确实存在。逐层定位后确认根因在上游：

```csharp
// VsdxPackageValidator.ContentTypes.cs
var pagesDir = Path.Combine(tempPath, "visio", "pages");
foreach (var pagePath in Directory.GetFiles(pagesDir, "page*.xml"))   // ← 通配符也匹配 pages.xml
{
    var partName = $"/visio/pages/{fileName}";
    if (!HasOverride(doc, partName, CT_Page))   // pages.xml 的 CT 是 pages+xml，不是 page+xml
        _errors.Add($"Missing override for {partName}");
}
```

`page*.xml` 同时匹配 `pages.xml`，于是把"pages.xml 没有 page+xml override"当作错误。

同一个类的 `FixFile` 会把这条误判"修复"成**真正损坏的包**——给 `/visio/pages/pages.xml` 追加第二条 content type：

```xml
<Override PartName="/visio/pages/pages.xml" ContentType="application/vnd.ms-visio.pages+xml" />
<Override PartName="/visio/pages/pages.xml" ContentType="application/vnd.ms-visio.page+xml" />  <!-- 错误 -->
```

**补偿**：

1. 引擎**不调用** `VsdxPackageValidator`，改用自研的 `VsdxPackageValidatorService`（逐项检查 ZIP / XML / OPC / Visio 语义，见 `docs/api.md`）。
2. 自研验证器**显式检查同一 part 被赋予多个 content type** 的情况，该问题因此可被检出而不是被掩盖。
3. 回归测试 `ValidationTests.Validate_DetectsDuplicateContentTypeOverride` 复现并锁定此缺陷。

---

## P1：复杂结构

### 验证内容

Group、Container、嵌套、Shape Data、多页面、连接线端点粘附、复杂样式。

### 结果：通过

| 能力 | 结论 | 证据 |
| --- | --- | --- |
| Container（原生容器） | 支持 | 包内含 `msvStructureType=Container` user cell |
| Group 成员 | 支持 | `ContainerMemberIds` / `ContainerOwnerIds` 双向维护 |
| Shape Data | 支持 | `SetShapeData` → 重新加载可读回 |
| 多页面 | 支持 | `AddPage` / `Pages` |
| 端点粘附 | 支持 | `AddConnector(..., VisioSide.Left, VisioSide.Right)` |
| 复杂样式 | 支持 | fill/stroke/pattern/font/bold/align 等均有对应 API |
| 图层 | 支持 | `AddLayer` / `AddToLayer` |

### 发现 3（必须补偿）：容器一律重算 bounds，忽略显式坐标

`AddContainer(id, pinX, pinY, width, height, ...)` 之后立刻调用 `AddToContainer(containerId, memberIds)` 时，后者默认 `resizeToFit: true`，会**按成员重新计算容器包围盒**，覆盖显式传入的坐标：

```
显式请求 grp_access: x=8.00 y=9.35 w=14.20 h=1.75
实际写入           : x=7.01 y=8.80 w=11.62 h=3.05   ← 成员包围盒 + margin
```

后果是分层色带会压住成员、标题文字被节点遮挡。

**补偿**：

1. 写容器时显式传 `VisioContainerOptions { AutoResize = false, Margin, HeadingHeight }`。
2. 新增 `DiagramLayout.Apply()`：在写入前按成员几何**自行推导**容器 bounds（含标题高度与内边距），使 JSON 中声明的 `autoSize: true` 与最终几何一致。
3. 容器标题改用 `VerticalAlignment = Top` + 上内边距，避免标题落在成员上。
4. `DiagramLayout.CheckContainment()` + `LayoutAnalyzer` 的 `group.member-escapes` 规则做回归保护。

---

## P2：增量编辑

### 验证内容

加载已有 VSDX、修改已有 Shape、新增 Shape、删除 Shape、修改 Connector、保存后重新读取。

### 结果：通过

```
updated demo/architecture_v2.vsdx
  nodes: +1 ~3 -0 =12 preserved
  edges: +2 ~1 -0
  page.resized  Platform Architecture  21.50x11.50 -> 24.25x11.50
  node.modified analytics-service  text='查询服务…' -> text='分析服务…' fill=#70AD47 -> #2F7D8C
  node.added    audit-service
  group.renamed grp_data
```

未修改的 12 个节点**完全保留**：几何、颜色、Shape Data 均未重写。

### 发现 4（必须补偿）：重命名会变成"删除+新增"

引擎的稳定 ID（agent 侧 id）与 Visio 的 shape ID 是两套标识。若只改 JSON 里的 `id`，增量编辑器按 id 匹配不到原形状，就会新建一个形状、原形状因"未出现在模型中"而被保留或删除——既丢失原有 ID，也可能产生重复连线。

**补偿**：

1. 写入时把稳定 ID 存进形状的 `User` 单元格 `VsdxModelId`（不占用 Visio 语义，不影响显示）。
2. 读取时优先用该单元格恢复 agent 侧 id。
3. 增量匹配顺序改为：**模型 id → `visioShapeId` → `VsdxModelId` → 文本**。
4. 边按**端点对**匹配，避免重命名节点后重复生成连线。
5. 回归测试 `RoundTripTests.RoundTrip_IdsAreStable`、`EditTests`、`IntegrationTests.Scenario_Round2_*` 覆盖。

### 发现 5（必须补偿）：连接器端点无法直接赋值

`VisioConnector.From` / `.To` 是 `internal set`，直接赋值编译失败；必须用 `VisioPage.ReconnectConnector(...)`，否则端点 ShapeSheet 单元格（`BeginX/EndX`）不会同步，保存时校验失败：

```
Cannot save Visio document because validation failed:
- Connector 'e-x' on page 'P' references a target shape that is not part of the page.
```

**补偿**：适配层统一走 `ReconnectConnector`，并在保存前对齐端点归属。

---

## P3：往返保真

### 验证内容

原始 VSDX → 加载 → 解析 → 修改 → 保存 → 再次加载；检查 Shape ID、数量、Connector、Group、端点、Shape Data、文字、样式、页面尺寸、图层。

### 结果：通过（带约束）

`RoundTripTests` 中的关键断言（全部通过）：

| 检查项 | 测试 |
| --- | --- |
| Shape 数量稳定 | `RoundTrip_ShapeCountsAreStable` |
| 模型 ID 稳定 | `RoundTrip_IdsAreStable` |
| Visio Shape ID 稳定 | `RoundTrip_VisioShapeIdsAreStable` |
| Shape Data 保留 | `RoundTrip_ShapeDataSurvives` |
| 样式保留 | `RoundTrip_StylesSurvive` |
| Connector 端点保留 | `RoundTrip_ConnectorEndpointsSurvive` |
| 页面尺寸保留 | `RoundTrip_PageSizeSurvives` |
| 连续三代稳定 | `RoundTrip_ThreeGenerationsRemainStable` |
| 空操作无损 | `RoundTrip_NoOpUpdateIsLossless` |
| 容器结构保留 | `RoundTrip_ContainerStructureSurvives` |
| 同模型二进制可复现 | `RoundTrip_BinaryIsReproducibleForSameModel` |

`RoundTrip_NoOpUpdateIsLossless` 的结果：

```
nodes: +0 ~0 -0 =3 preserved      ← 无改动时 3 个形状全部原样保留
```

### 发现 6：XML/OPC 关系正确

自研验证器逐项确认（`docs/api.md` 有完整清单）：

- `[Content_Types].xml` 覆盖所有 part，且无重复声明
- `_rels/.rels` 指向 `visio/document.xml`
- `visio/_rels/document.xml.rels` 指向 `pages/pages.xml`
- 每个 `<Rel>` 能在 `pages.xml.rels` 中解析到真实 part
- `<Connect>` 的 `FromSheet` / `ToSheet` 都指向存在的 shape
- Shape `ID` 全为数字且页内唯一

---

## 已知限制（OfficeIMO.Visio 3.6.1）

| 编号 | 限制 | 影响 | 处置 |
| --- | --- | --- | --- |
| L1 | `VsdxPackageValidator` 对 `pages.xml` 误报，`FixFile` 会写坏包 | 不可用于验收 | 不使用；自研验证器替代 |
| L2 | PNG 缺省字体无 CJK 覆盖 | 中文变缺字框 | `FontResolver` 自动注入字体文件 |
| L3 | `AddToContainer` 默认重算容器 bounds | 分层色带错位 | `AutoResize=false` + 自绘 bounds |
| L4 | `VisioConnector.From/To` 只读，需 `ReconnectConnector` | 改连接关系编译失败 | 适配层封装 |
| L5 | 重命名无内建稳定 ID 机制 | 改名变重建 | `User.VsdxModelId` 单元格 |
| L6 | 无自定义文档属性 API | 模型元数据无处存放 | `VisioMetadataBridge` 后置 patch `docProps/custom.xml` |
| L7 | 嵌套组（组套组）按兄弟容器写入 | 层级扁平化 | 保留 `ParentId`，渲染正确但 Visio 内非真正嵌套；已在兼容性报告 `write.nested-group` 中声明 |
| L8 | 连接线路由为直线/折线近似 | 密集区域仍需显式 waypoints | `OrthogonalRouter` 自动布线 + `check-layout` 检查 |

> 以上限制均已写入 `docs/limitations.md`，并各有对应测试或兼容性报告代码路径。

---

## 复现方式

```bash
# 依赖已固定在 Directory.Packages.props
dotnet test tests/Vsdx.Tests/Vsdx.Tests.csproj -c Release

# 三份示例图
for f in architecture workflow network; do
  dotnet run --project src/Vsdx.Cli -c Release -- create \
    --input examples/$f.json --output .scratch/$f.vsdx
  dotnet run --project src/Vsdx.Cli -c Release -- check-layout \
    --input .scratch/$f.vsdx --json
done
```

---

## 未完成的验证

**Microsoft Visio 原生打开验证未执行**。本环境未安装 Visio，因此：

| 验证层级 | 状态 |
| --- | --- |
| 文件结构验证（ZIP/XML/OPC） | ✅ 已完成 |
| OfficeIMO 重新加载验证 | ✅ 已完成 |
| 第三方渲染验证（SVG/PNG） | ✅ 已完成 |
| **Microsoft Visio 实际打开验证** | ⬜ 待外部 Windows + Visio 环境验收 |

前三项不能替代第四项。需要在装有 Visio 的 Windows 环境执行 `docs/limitations.md` 中的验收清单。
