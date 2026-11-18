# 已知未解问题

本文件只列**当前仍未解决**的问题。已修的不在此列——它们由对应测试固定。

## 1. L4 原生验证未执行

**最高优先级，也是唯一影响产品定义的项。**

本机无 Microsoft Visio，`/mnt/c` 只读且无 PowerShell，WPS 不可替代。
全部验证止于 L1（包结构）、L2（读回）、L3（渲染）。

`tools/l4/verify-visio.ps1` 已写好并做过静态审查，**从未运行**。

**解除条件**：一台装有 Microsoft Visio 的 Windows 机器，或可写挂载的 `/mnt/c` + PowerShell。

## 2. 页面利用率虚高，导致 `page.underused` 漏报

**实测**：`checkout-sequence` 报告 84%，而节点本身只占 23%——渲染图上约 70% 是空白。

| 场景 | 报告 | 仅节点 | underused |
| --- | --- | --- | --- |
| user-data-flow | 82% | **24%** | ❌ 漏报 |
| checkout-sequence | 77% | **23%** | ❌ 漏报 |
| cbo / deploy / swimlane / tree | 68–78% | 47–68% | ✅ 正确 |

**根因**：利用率按「所有可见对象」的包围盒计算，含独立文本块。文本块远离节点时把内容框撑到整页。

**这不是错误判定，是检查从未走到判定**——大部分空白的页面被当作正常，且无法与真正正常的区分。

**为何未修**：改定义会改变全项目所有分数。

**建议方案**：`LayoutReport` 同时给出 `nodeUtilisation`，不改变现有数字，让调用方自行选择。

**固定测试**：`UnderusedMissTests`（含一条断言"不触发"的测试，修复后会失败，迫使记录更新）。

## 3. 评分权重缺依据

`SoftWeight` 决定所有分数，但没有任何一项说明为何取该值。

最可疑的是：`edge.detour` 扣 **6**，`edge.crossing` 扣 **2**——绕远路比两线交叉罚得重 3 倍；
`label.overlaps-label`（文字读不了）只有 4。

**为何未修**：同 §2，改动影响所有分数。

**固定测试**：`SoftWeightBaselineTests`（含一条断言"交叉轻于绕行"的测试）。

## 4. `repair` 的接受准则允许分数大幅下降

**实测（cbo）**：`86 → 70`，问题数 `9 → 7`，决策 `Accepted`。

准则本身合理——`repair` 的目标是消除指定问题，不是抬高总分。
但目标问题 `container.heading-collision` 修复后**依然存在**（换了几个节点），而分数掉了 16 分。

**为何未修**：收紧意味着选一个阈值并改变 `repair` 行为，属产品取舍。

## 5. 图例可能与连线标签视觉冲突

`ClampToPage` 只保证文本块在页面内，**不避让连线标签**——而标签是渲染时绘制的，
不在任何包围盒里，24 项检测全都看不到。

**当前状态**：已知，未修。需要一个能估算标签绘制位置的机制，成本较高。

## 6. 跨层连线是斜线

`OrthogonalRouter` 的策略是「直边只在穿形状时才绕行」。

| 布局 | 实测 |
| --- | --- |
| `tree` | 9 条全 `straight` |
| `layered` / `swimlane` | 8 `straight` + 1 `curved` |

层级图通常期望正交折线。**这是设计选择而非缺陷**：无遮挡时改线是在替作者做决定。
详见 `capability-boundaries.md` §8。

**若要改**：让布局引擎按图类型选默认边类型（树形/泳道 → `rightAngle`），属能力扩展。

## 7. 时序图与状态机

模型层不支持时序语义（端点由节点决定，无法表达"生命线上的第 N 条消息"），
状态机无状态语义。

详见 `capability-boundaries.md`。

## 8. 校验器不检查 Master 引用有效性

悬空的 `Master="9999"` 仍返回 `valid: true`，而 Visio 会显示为损坏形状。
详见 `limitations.md` §11。

---

## 已完成的不在此列

容器标题间隙（§1 旧）、泳道标题预留（§5 旧）、`edge.diagonal-across-band` 不可达（§6 旧）
均已在 2026-11-19 修复，分别由 `HeadingClearanceTests`、`SwimlaneHeadingTests`、
`DiagonalAcrossBandTests` 固定。
