# Phase 8.2 待裁决事项

**日期：** 2026-11-17
**状态：** Stage 3 复审 FAIL；以下两项需负责人裁决后才能完成剩余修订。
**其余 10 项 M 已修订完毕。**

---

## D-4：TextBlock 字体是否需要可回读

### 事实（已实测）

`VisioTextBlockBridge.cs:141` 写入的是 **shape 级裸单元格**：

```csharp
AddCell(shape, "Font", style.Font);   // → <Cell N="Font" V="Verdana"/>
```

实测结果：

```
TextBlock Font cell : ['Verdana']      ← 写进去了
Verdana in document.xml : False        ← 但没注册进 FaceNames
FaceNames count : 0
```

Visio 的字体必须**同时**在 `document.xml` 的 `<FaceNames>` 里注册，
并按索引引用；只写一个字符串，Visio 与 Reader 都无法解析。

Reader 侧因此取不到值——`ReadStyle` 读的是 `shape.TextStyle.FontFamily`，
而该属性来自注册表，不是裸 cell。

### 两个选项

| 选项 | 做法 | 后果 |
| --- | --- | --- |
| **A. 修 Bridge，让字体可回读** | `VisioTextBlockBridge` 把字体注册进 `FaceNames` 并用索引引用 | TextBlock 字体保真；**须改 `Visio*Bridge`**，与"不触碰 Bridge"的原约束冲突，T2 工时增加 |
| **B. 明确声明"TextBlock 字体不回读"** | 验收改为 XML 解包断言（`Cell N="Font"` 存在且值正确），不改 Bridge | 范围不变；但 `export-model` 读回的 TextBlock 字体为 null，调用方须知道这个限制 |

### 我的建议

**选 A。** 理由：

1. 任务 §3.5 明确要求"Reader 能正确读回已支持的样式"。留一个写了读不回的字段，
   等于在协议里埋一个"看起来支持"的假象——这正是本轮 16 项缺陷的共同模式。
2. 选项 B 把"读不回"变成永久限制，但**同一份图里 Node 字体能读回、TextBlock 不能**，
   这种不一致会在后续阶段反复制造困惑。
3. 工时增加有限：`FaceNames` 是 `document.xml` 的一个 section，与现有
   `VisioMetadataBridge` 同属 docProps 类操作，不涉及 page 部件重写。

**若选 B**，我会在能力矩阵里把"TextBlock 字体回读"明确标为 ❌ 并写明原因，
而不是让它悄悄留在 `⚠️` 里。

---

## D-8：`layout=manual` 时 `direction` 是否生效

### 事实

两层冲突：

**语义层**——我的文档给出两个对立答案：

| 方案 | 行为 | 理由 |
| --- | --- | --- |
| **A** | `direction` 不生效，进 `unmetConstraints` | `manual` 的契约是"作者坐标即最终结果" |
| **B** | `direction` 生效，整体转置，waypoints 逐点跟随 | `direction` 是独立维度，与"是否重排节点"无关 |

两者都站得住。我倾向 **B**：`manual` 说的是"不要重新计算布局"，
而 `direction` 说的是"把整张图转过来"，后者不改变任何相对关系，
不违反 manual 的承诺。

**实现层**——方案 B 有两个额外障碍（审查实测）：

1. `IrEdge` **没有 `Waypoints` 字段**，协议层无法携带折点
2. `DesignSpec.cs:163` 的 manual 分支**不做任何转置**

因此：

| 方案 | 额外工作 |
| --- | --- |
| A | 零——只需检查 `layout==manual` 时报告一条 `unmet` |
| B | `DiagramIr` 加 `Waypoints`；manual 分支加转置；waypoints 转置逻辑（§11 已列） |

### 我的建议

**选 A（本轮）。** 理由：

1. 方案 B 需要给 IR 加新字段，属于协议变更，任务 §4.9 要求"不改变现有 IR 兼容性，
   除非有必要并经过版本评估"。为一个尚无真实用例的组合（`manual` + `direction`）
   做协议扩展，不划算。
2. 方案 A 诚实：`manual` 下不转置，且**明确报告**"direction 未应用"，
   调用方立刻知道发生了什么。这符合任务 §4.10"不允许静默丢弃"。
3. 若将来出现真实需求，再按方案 B 扩展，届时 `Waypoints` 字段可与其他 IR 扩展一并评估。

**注意：** 无论选哪个，§11.6 的断言 4 与 7 都必须按裁决结果二选一重写——
当前两条互斥。

---

## 需要负责人的回复

```
D-4: A / B
D-8: A / B
```

两项都可以只回一个字母。收到后我会：
1. 按裁决更新计划中对应的验收断言
2. 同步三份文档
3. 提交 Stage 4 最终材料包，请求进入 Stage 5 的批准
