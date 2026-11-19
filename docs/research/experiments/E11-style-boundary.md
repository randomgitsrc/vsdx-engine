# 实验 E11：四个「不支持」字段不可达的原因在 IR 层

**日期：** 2026-11-20
**状态：** 完成（结论为设计边界，非缺陷）
**依据：** 上一轮修好 `opacity` 后，核查剩余四个字段是否也能修

---

## 一、起点

`OfficeImoDocumentWriter` 报告五个样式字段「模型和 schema 接受，但写不进去」：
`opacity`、`shadow`、`radius`、`lineJoin`、`paddingBottom`。

上一轮证明了 `opacity` 是可以写的（OfficeIMO 无 API，但 Visio 有 `FillForegndTrans` 单元格，可直写 XML）。

**所以「报告里写的不支持」不等于「真的不能支持」。** 剩下四个需要逐个核实。

---

## 二、逐个核查

| 字段 | OfficeIMO 3.6.1 | Visio 单元格 | 结论 |
| --- | --- | --- | --- |
| `shadow` | 无 `Shadow` 属性 | `ShapeShdwShow` + `ShdwForegnd` + 偏移，是一组单元格 | 可写，但需组合 |
| `radius` | 无 | 需改 `Geometry` 段的行，不是单元格 | 代价大 |
| `lineJoin` | 无 | `LineCap` 可写 | 可写 |
| `paddingBottom` | 无 | `BottomMargin` 可写 | 可写 |

`VisioShape` 共 46 个属性，实测列表里没有这四个名字。

---

## 三、真正的阻塞点在上游

**`IrNode` 只有六个字段**：`Id`、`Label`、`Role`、`ContainerId`、`Attributes`、`Anchor`、`Status`。

**没有 `Fill`、没有 `Stroke`、没有 `Bold`。**

样式由 `theme.palette` 按 `role` 推导。这是**有意的约束**，源码写着：

> `IR must not degenerate into a coordinate list, so this stays optional.`（`IrNode.Anchor` 注释）

逐节点样式是同一个失败换了个形式。所以这四个字段**从 IR 根本设置不了**——
Writer 报告它们不支持是准确的，不是遗漏。

---

## 四、与 `opacity` 的区别

| | `opacity` | 其余四个 |
| --- | --- | --- |
| IR 可达？ | ✅ `Status` 推导出 | ❌ 无入口 |
| Writer 可写？ | ❌ 但可绕 | 部分可绕 |
| 性质 | **链路断开** | **设计边界** |

`opacity` 是缺陷；这四个不是。

---

## 五、如何防止边界被误扩展

`StyleBoundaryTests` 固定两件事：

1. 四个字段**仍在**不支持报告里，`opacity` **不在**
2. `IrNode` **没有** `Fill`/`Stroke`/`Shadow`/`Radius` 属性——如果哪天加了，
   这个边界就变了，测试会失败

---

## 六、结论

**不修这四项。** 不是放弃，是判断它们的不可达来自设计约束而非实现缺陷。

如果将来需要逐节点样式，正确的做法是**扩展 IR**（并重新讨论「不退化为坐标和样式列表」这条约束），
而不是在 Writer 里补单元格。
