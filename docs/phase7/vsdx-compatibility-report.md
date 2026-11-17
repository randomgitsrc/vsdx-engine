# VSDX 兼容性与可编辑性报告

**审计对象：** `vsdx-engine` 产出的 VSDX 与外部 VSDX 的互操作能力
**重要声明：** 本报告**不包含** Microsoft Visio 原生验证。本环境无 Windows + Visio，
该层级标记为**未验证**，不得由其它层级的证据替代。

---

## 1. 已验证层级

| 层级 | 手段 | 状态 |
| --- | --- | --- |
| L1 文件结构 | ZIP / OPC / XML / 17 项语义检查 | ✅ 已验证 |
| L2 后端重载 | OfficeIMO.Visio 重新加载并比对 | ✅ 已验证 |
| L3 第三方渲染 | 由 VSDX 渲染 SVG/PNG + 人工目视 | ✅ 已验证 |
| **L4 Visio 原生行为** | 真实 Visio 打开、编辑、保存 | ⬜ **未验证** |

---

## 2. L1：文件结构

验证器 `VsdxPackageValidatorService` 逐项检查，结果以列表形式给出，不是单一布尔值：

```
[PASS] zip.readable                     20 entries
[PASS] zip.has-content-types
[PASS] xml.parseable                    20 XML parts checked, 0 invalid
[PASS] opc.document-override
[PASS] opc.pages-override
[PASS] opc.page-overrides               1 page part(s)
[PASS] opc.no-duplicate-overrides
[PASS] opc.rels-default
[PASS] opc.root-rels-count              4 relationship(s)
[PASS] opc.document-relationship
[PASS] opc.pages-relationship
[PASS] visio.pages-present              1 page(s)
[PASS] visio.shape-ids-unique           page1.xml=18
[PASS] visio.connector-references       28 connect row(s), 0 invalid
[PASS] visio.group-membership           0 parent link(s), 0 broken
[PASS] backend.reload                   1 page(s), 18 shape(s), 14 connector(s)
```

**注意一处矛盾**：`visio.group-membership` 显示 `0 parent link(s)`。
这是审计发现的 G1 缺陷的残留痕迹——在嵌套容器的 `Parent` cell 修复之前，
父子关系从不写入。修复后该检查应报告实际链接数，见 §5。

---

## 3. L2：往返保真

### 3.1 已验证保留的内容

| 内容 | 测试 | 结果 |
| --- | --- | --- |
| Shape 数量 | `RoundTrip_ShapeCountsAreStable` | ✅ |
| 模型稳定 ID | `RoundTrip_IdsAreStable` | ✅ |
| Visio Shape ID | `RoundTrip_VisioShapeIdsAreStable` | ✅ |
| Shape Data | `RoundTrip_ShapeDataSurvives` | ✅ |
| 样式（填充/描边/字体） | `RoundTrip_StylesSurvive` | ✅ |
| 连接线端点 | `RoundTrip_ConnectorEndpointsSurvive` | ✅ |
| 页面尺寸 | `RoundTrip_PageSizeSurvives` | ✅ |
| 容器结构 | `RoundTrip_ContainerStructureSurvives` | ✅ |
| 嵌套容器父子关系 | `G1_NestedContainerSurvivesRoundTrip` | ✅（Phase 7 新增） |
| 连续三代稳定 | `RoundTrip_ThreeGenerationsRemainStable` | ✅ |
| 空操作无损 | `RoundTrip_NoOpUpdateIsLossless` | ✅ |

### 3.2 嵌套容器：修复前后

**【修复前】**（Phase 7 审计实测）

```
$ vsdx-tool create --input nested.json --output nested.vsdx
  WARNING write.nested-group: Nested group 'inner' is written as a sibling container

读回：inner parentId = None,  members = ['a','b']
      outer parentId = None,  members = ['c']
包内：Parent cells = 0
```

三重丢失：父关系、包内层级、子容器成员未被父容器包含。

**【修复后】**

```
$ vsdx-tool create --input nested.json --output nested.vsdx
  INFO write.nested-group-applied: 已建立 1 组嵌套容器父子关系

读回：inner parentId = outer,  members = ['a','b']
      outer parentId = None,   members = ['c','a','b']
包内：Parent cells = 1
```

**实现方式（必须记录，因为它决定了脆弱点）：**
OfficeIMO.Visio 无公开的 shape parenting API（`VisioShape.Parent` 为 internal setter），
因此嵌套关系通过**保存后 patch page XML** 写入，并用容器标题作为 join key 读取回来。

**风险：** 该 bridge 依赖"容器 shape 的 `<Text>` 等于组名"这一约定。
若 OfficeIMO 未来改变容器文本的写入方式，bridge 会静默失效（当前会记录
`write.nested-group-unresolved` 警告）。这是已知架构债，已记入 `capability-gap-analysis.md` §6.1。

---

## 4. 外部 VSDX 兼容性

### 4.1 测试样本

`ExternalFixtures` 用 `System.IO.Compression` **手写 OPC 结构**生成，
完全不经过本项目的写入器：

| 样本 | 内容 |
| --- | --- |
| `external-basic.vsdx` | 3 shape + 1 connector + 样式表，无容器 |
| `external-complex.vsdx` | 容器（含 `msvStructureType`）、4 shape、Shape Data、连接线 |
| `external-multipage.vsdx` | 2 页 |

### 4.2 已验证能力

| 能力 | 测试 | 结果 |
| --- | --- | --- |
| 读取外部包 | `External_ReadsHandBuiltPackage` | ✅ |
| 导出模型 | `External_ExportsModel` | ✅ |
| 单对象修改 | `External_ModifyOneObjectAndSave` | ✅ 只改 1 个，保留 2 个 |
| 重新读取 | `External_ReReadAfterModify` | ✅ |
| 渲染 | `External_Renders` | ✅ |
| 校验 | `External_ValidatesCleanly` | ✅ |
| 复杂特性保留 | `External_ComplexPackagePreservesNativeFeatures` | ✅ |
| 多页面 | `External_MultiPagePackageIsReadable` / `...RendersAllPages` | ✅ |

### 4.3 已知的外部包限制

| 特性 | 状态 |
| --- | --- |
| 自定义 Geometry 段（手绘轮廓） | 保留原始几何，不进 JSON 模型，不可经模型编辑 |
| 主题 / StyleSheet 引用 | 包级主题保留；模型只用显式样式 |
| Data Graphics / 注释 / 签名 | 保留在包内，模型不建模 |
| 动态连接线高级路由参数 | 仅建模 `kind` / `waypoints` |
| 宏（.vsdm） | 不在范围 |
| 嵌套容器（外部文件的） | 读取 depends on `Parent` cell 存在；**未用外部样本验证** |

**原则：** 无法映射的内容在包中**保留原样**并在兼容性报告里说明；不会静默丢弃。
但"保留"不等于"可通过 JSON 编辑"。

---

## 5. L3：渲染一致性

### 5.1 渲染来源保证

渲染器**总是重新打开磁盘上的 VSDX** 再投影几何，不读取内存模型。
因此预览与交付物必然一致——不存在"另画一张 SVG 冒充"的可能。

### 5.2 渲染与 Visio 的已知差异

| 差异 | 说明 |
| --- | --- |
| 字体度量 | 使用估算模型（CJK 误差 0%，拉丁平均 2.6%） |
| 换行位置 | 与 Visio 的排版引擎可能不同 |
| 渐变/阴影/主题色 | 渲染器支持有限 |
| 连接线标签避让 | 启发式，非 Visio 算法 |

### 5.3 中文渲染

需 CJK 字体文件；`FontResolver` 自动探测并在失败时给出 `render.cjk-no-font` 警告。
实测使用 `NotoSansSC-VF.ttf` 渲染正常。

---

## 6. 可编辑性评估

"可编辑"指**在 Visio 中打开后能正常操作**，而不是"文件能被解析"。

| 维度 | 评估 | 依据 |
| --- | --- | --- |
| 形状可选中/拖动 | 预期可 | 标准 master + ShapeSheet 单元格已写入 |
| 文字可编辑 | 预期可 | 文本写入 `<Text>` 元素 |
| 连接线粘附 | 预期可 | `<Connect>` 端点已写入且校验通过 |
| 容器可整体移动 | 预期可 | `msvStructureType=Container` 已写入 |
| 嵌套容器层级 | **修复后预期可** | `Parent` cell 已写入，但**未经 Visio 验证** |
| Shape Data 窗口 | 预期可 | `Property` 段已写入 |

**全部"预期可"项均未在真实 Visio 中验证。** 这是本报告最重要的限制声明。

---

## 7. 未完成事项（明确列出）

| # | 事项 | 原因 |
| --- | --- | --- |
| 1 | L4 Visio 原生打开/编辑/保存 | 无 Windows + Visio 环境 |
| 2 | 外部含嵌套容器的 VSDX 读取 | 缺样本 |
| 3 | >50 节点的复杂包 | 未构造 |
| 4 | Visio 保存后的二次往返 | 依赖 #1 |
| 5 | .vsdm 宏包 | 不在范围 |

**不得**用 L1–L3 的结果推断 L4 通过。

---

## 8. 可复核的验证命令

```bash
# L1
vsdx-tool validate --input out.vsdx

# L2
vsdx-tool export-model --input out.vsdx --output rt.json
vsdx-tool update --input out.vsdx --model rt.json --output rt2.vsdx
vsdx-tool diff --before out.vsdx --after rt2.vsdx    # 应为 identical

# L3
vsdx-tool render --input out.vsdx --format svg --output out.svg
vsdx-tool check-layout --input out.vsdx --json
```
