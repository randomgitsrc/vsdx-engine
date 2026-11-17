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

**与 Node 的机制对比（实测）：**

| | Node（`fontFamily: Georgia`） | TextBlock（`font: Verdana`） |
| --- | --- | --- |
| `FaceNames` 条目 | **3** | **0** |
| 字体名在 `document.xml` | ✅ `Georgia` 存在 | ❌ `Verdana` 不存在 |
| Shape 上有 `Character` 段 | ✅ | ❌ |
| Shape 上裸 `Font` cell | ✅ | ✅ |
| Reader 能否读回 | ✅ | ❌ |

**结论：Node 走 `TextStyle` → 自动注册 `FaceNames`；TextBlock 只写裸 cell，没有注册。**
两者机制不同，不是同一个 bug。

Reader 的 `ReadStyle` 读的是 `shape.TextStyle.FontFamily`，该属性来自注册表；
裸 `Font` cell 不在其覆盖范围内，所以取不到。

### 两个选项

| 选项 | 做法 | 后果 |
| --- | --- | --- |
| **A. 修 Bridge，让字体可回读** | `VisioTextBlockBridge` 把字体注册进 `FaceNames` 并用索引引用 | TextBlock 字体保真；**须改 `Visio*Bridge`**，与"不触碰 Bridge"的原约束冲突，T2 工时增加 |
| **B. 明确声明"TextBlock 字体不回读"** | 验收改为 XML 解包断言（`Cell N="Font"` 存在且值正确），不改 Bridge | 范围不变；但 `export-model` 读回的 TextBlock 字体为 null，调用方须知道这个限制 |

### 我的建议

**选 A。** 理由：

1. 任务 §3.5 明确要求"Reader 能正确读回已支持的样式"。留一个写了读不回的字段，
   等于在协议里埋一个"看起来支持"的假象——这正是本轮 16 项缺陷的共同模式。
2. **同一份图里 Node 字体能读回、TextBlock 不能**，这种不一致会在后续阶段反复制造困惑。
3. **工时取决于实现路径，两种都可控。** `FaceNames` 的注册由库的 `VisioTextStyle.FontFamily`
   机制自动完成（`ShapeStyler` 已在用），不需要手写 `document.xml`：

   ```csharp
   // 当前（裸 cell，不注册）
   AddCell(shape, "Font", style.Font);

   // 改为走库的 TextStyle 机制（自动注册 FaceName）
   shape.TextStyle ??= new VisioTextStyle();
   shape.TextStyle.FontFamily = style.Font;
   ```

   **但有一个实现障碍：** `VisioTextBlockBridge` 是在**保存之后**往 XML 里补写 shape 的，
   那时库的 `FaceNames` 已经写完。所以要么
   （a）把 TextBlock 改用库 API 在保存前创建（改动较大），
   要么（b）在 `document.xml` 手工补 `<FaceName>` 并让 cell 用索引引用。

   **(b) 可行但需维护索引**，比"小"重。**T2 工时确认为"中"**——
   不是因为它复杂，而是因为它要正确处理索引且不能再犯"写进去读不回"的错误。

**若选 B**，我会在能力矩阵里把"TextBlock 字体回读"明确标为 ❌ 并写明原因，
而不是让它悄悄留在 `⚠️` 里。

---

## D-8：`layout=manual` 时 `direction` 是否生效 —— ✅ 已裁决

**本项已由负责人 D1 裁定，不再是待决问题。**

> 负责人原文：「**Manual 模式必须保留用户坐标，不得擅自转置。**」

**裁决：方案 A** —— `manual` 下 `direction` 不生效，且必须进 `unmetConstraints`。

| 后果 | 说明 |
| --- | --- |
| 不需要给 `IrEdge` 加 `Waypoints` | 方案 B 的需求作废 |
| `DesignSpec.cs:163` 的 manual 分支不转置 | 只增加一条 `unmet` 记录 |
| §11.6 断言 4 与 7 统一 | 已按方案 A 重写 |

**我上一轮把它列为"待裁决"是错的。** D1 里已经写明，是我没有把该句
登记为正式裁决，导致后面写 §11.2 时又并列了两种解释。
已补建 `phase8.2-owner-rulings.md` 作为裁决的唯一登记处。

---

## 需要负责人的回复

**只剩一项：**

```
D-4: A / B
```

收到后我会：
1. 按裁决更新 T2 的范围与验收断言
2. 同步 audit / plan / review-packet 三份文档
3. 提交 Stage 4 最终材料包，请求进入 Stage 5 的批准

**D-8 无需回复**，已由 D1 裁定。
