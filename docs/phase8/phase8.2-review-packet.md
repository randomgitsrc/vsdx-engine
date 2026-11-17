# Phase 8.2 评审材料包（Stage 4 提交）

**日期：** 2026-11-17
**状态：** 等待负责人批准，**尚未开始产品代码实施**

---

## 材料清单

| # | 文件 | 说明 |
| --- | --- | --- |
| 1 | `phase8.2-audit.md` | Stage 1 现状审计（含字段执行矩阵、**12 项缺陷**、3 处自我更正） |
| 2 | `phase8.2-plan.md` | Stage 2 实施计划（目标架构、优先级契约、文件级改动、测试计划） |
| 3 | `phase8.2-independent-review.md` | Stage 3 独立审查报告 |
| 4 | 本文件 | 提交说明与待决事项 |

---

## 一、审计结论摘要

对 `DesignSpec` 17 个字段逐个**实际执行并回读**，结论：**7 项已实现、4 项部分、6 项未执行**。

审计过程中修正了自己初稿的三处误判：

| 项 | 初稿 | 实际 |
| --- | --- | --- |
| `Theme.Background` | ✅ 已实现 | ❌ 赋值但不落盘，回读为 null |
| `typography.min_size` | 报告存在 | ❌ 判断写反，死代码 |
| `RoleColors` 多页 | 只影响首页 | ✅ 两页均生效 |

**方法教训：** "字段存在""代码有赋值"都不是证据。只有当值真正消费时才作数。

---

## 二、缺陷清单（12 项）

| # | 缺陷 | 严重度 |
| --- | --- | --- |
| 1 | `Direction` 四种布局策略全无效 | **P0** |
| 2 | `Theme.Palette` 未执行 + 未知值不报告 + Schema 不约束 | **P0** |
| 3 | `Theme.Background` 设置后丢失 | P1 |
| 4 | Typography 仅覆盖 Node | P1 |
| 5 | 容器色硬编码绕过 Theme | P1 |
| 6 | `Canvas.Unit` 只写标签不换算 | P1 |
| 7 | `containerLayouts` manual 下静默跳过 | P1 |
| 8 | `ObjectMapping` 仅首页 + 仅 Node/Container | P1 |
| 9 | `typography.min_size` 死代码 | P2 |
| 10 | `BaseSize` 抹平字体层级 | P1 |
| 11 | 字号在布局后应用 → 文字溢出 | P1 |
| 12 | `roleColors` 无 default 回退 | P2 |

**其中 6 项是初稿漏掉的**（Direction、Background、min_size、BaseSize、溢出、default），
印证了"读代码判断能力"不可靠。

---

## 三、核心判断

**写作层（Writer）能力已经够用，缺的是解析层。**

审计确认：
- Edge 标签字号：`OfficeImoDocumentWriter:378` 已支持 `SetFontSize`
- TextBlock 字号：已验证 VSDX 中正确写入 20pt / 8pt
- 容器 TextStyle：已支持

缺的是 `DesignSpecApplier` 从不设置这些值。**因此 Phase 8.2 主要是接线，不是造能力。**

---

## 四、计划要点

引入**一个** `StyleResolver`，把四处互不相识的 if 合并为一条优先级管线：

```
Palette Defaults → Role Overrides → Explicit Object Styles
字号：ObjectStyle.FontSize ?? TypeScale[role] → max(_, MinReadable) 并报告
```

**不触碰** Writer / Reader / FidelityGuard / LayoutStrategies / 协议版本。

---

## 五、待负责人决策（3 项）

### D1. `Direction` 修复属破坏性变更

修好后，旧 spec 若声明 `topToBottom` 会**真的转置**（此前完全无效）。
这是修 bug，但会改变既有产物外观。

**请确认：接受此变更，还是需要兼容开关？**

### D2. `ObjectMapping` 键格式

当前单页用裸 id。提议：**单页保持裸 id（兼容），多页用 `pageId/objectId`**。

**请确认此格式。**

### D3. `BaseSize` 语义

当前是"绝对字号"，导致层级被抹平。提议改为**缩放基准**：
`scale = baseSize / TypeScale.NodeTitle`，保留档位关系。

**这会改变 `baseSize` 的既有行为**（同值输入产生不同输出）。**请确认。**

---

## 六、工时估计

| 子任务 | 估计 |
| --- | --- |
| T0 修 Direction（含 Transpose 补齐） | 小 |
| T1 StyleResolver + 4 palette | 中 |
| T2 接入四种对象 | 小 |
| T3 移除容器硬编码 | 小 |
| T4–T6 单位 / containerLayouts / min_size | 小 |
| T7 ObjectMapping | 小 |
| T8 三套产物 + 目视 | 中 |
| T9–T10 回归 + 背景落盘 | 小 |

**总计：中等偏小。** 因为写入能力已存在，主要是解析与接线。

---

## 七、未做的事（明确边界）

- 未修改任何产品代码
- 未开始 Stage 5 实施
- L4 保持未验证（任务 §5.7 明确不作为阻塞）
- 外部 Stencil / 图片 / 多区域协调等按任务 §6 保留至后续阶段
