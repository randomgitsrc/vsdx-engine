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

**已提供 `nodeUtilisation`**（2026-11-19）：协议层与 `LayoutReport` 都新增了这个字段，
只统计节点，不改动 `pageUtilisation`。

```console
$ execute --ir checkout-sequence.ir.json
  pageUtilisation: 0.773
  nodeUtilisation: 0.234     <- 绘制本身只占 23%
```

**`page.underused` 仍读 `pageUtilisation`**，因此漏报依然存在——但那现在是一个**可选**的修正：
调用方可以自行用 `nodeUtilisation` 判断，或等待是否把 `underused` 也切到新指标（属产品决策）。

**固定测试**：`UnderusedMissTests`（含一条断言"不触发"的测试，修复后会失败，迫使记录更新）。

## 3. `repair` 的接受准则未考虑分数

**原始记录已过时**（2026-11-19 复核）。当时记的是 cbo `86 → 70`，那是容器标题未修前的状态；
标题修复后 cbo 是 100 分，`repair` 无改动可做。

**当前实测（四个场景）**：

| 场景 | 前后 | 尝试 |
| --- | --- | --- |
| cbo | 100 → 100 | 0 |
| deploy-topology | 82 → 82 | 3（全部 `RolledBack`）|
| user-data-flow | 100 → 100 | 0 |
| checkout-sequence | 88 → 88 | 3（全部 `RolledBack`）|

**没有任何改动被接受**，因此接受准则当前不影响任何输出。

**准则本身**：`VersionStore.Evaluate` 接受「未引入新硬约束」的候选，不看分数。
理由是 `repair` 的目标是消除指定问题，不是抬高总分。

**为何仍未改**：改动会改变 `repair` 的行为，而当前没有证据表明这个行为有害——
所有实测尝试都被回滚了。`Accepted` 分支有测试覆盖（`RepairTests`），说明它可达，
但真实的六个场景里一次都没触发。

**取舍已构造并证实存在**（`RepairAcceptanceTests`）：

| | 问题数 | 分数 | 决策 |
| --- | --- | --- | --- |
| 当前 | 2 × `edge.crossing`（权重 2） | 96 | — |
| 候选 | 1 × `edge.detour`（权重 6） | **94** | **`Accepted`** |

问题数 2 → 1，分数 96 → 94。**得分下降仍被接受**，理由"改善了既有问题且未引入新问题"。

还有第三条接受路径：`fixedIntended`——目标问题消失即接受，**即使总数不变、每条剩余问题更重**。

**这正是设计意图**：`repair` 的目标是消除指定问题，不是抬高分数。分数在 `Score` 的注释里
写明是 advisory（参考性）。

**因此这不是缺陷，是有意的取舍**，且 `attempts[]` 会逐条报告前后分数，调用方不会被动地
拿到一个变差的结果而不自知。

**若要改**：需先说明「`repair` 应当追求分数」这一前提成立——当前文档与注释都说不成立。

## 4. 图例可能与连线标签视觉冲突

`ClampToPage` 只保证文本块在页面内，**不避让连线标签**——而标签是渲染时绘制的，
不在任何包围盒里，24 项检测全都看不到。

**当前状态**：已知，未修。需要一个能估算标签绘制位置的机制，成本较高。

## 5. 跨层连线是斜线

`OrthogonalRouter` 的策略是「直边只在穿形状时才绕行」。

| 布局 | 实测 |
| --- | --- |
| `tree` | 9 条全 `straight` |
| `layered` / `swimlane` | 8 `straight` + 1 `curved` |

层级图通常期望正交折线。**这是设计选择而非缺陷**：无遮挡时改线是在替作者做决定。
详见 `capability-boundaries.md` §8。

**若要改**：让布局引擎按图类型选默认边类型（树形/泳道 → `rightAngle`），属能力扩展。

## 6. 时序图与状态机

模型层不支持时序语义（端点由节点决定，无法表达"生命线上的第 N 条消息"），
状态机无状态语义。

**为何未修**：需要扩展模型——`IrEdge` 带消息序号、端点可寻址生命线，
以及一套新的布局策略。这不是参数调整，是新增能力，工作量与风险都高于本项目其余修复。

**实测依据**：把请求/回复交替设置 `labelPosition`（0.3 / 0.7）**无效**——
两条反向连线是同一条线段，沿比例移动只会在同一直线上滑动。
`SequenceFeasibilityTests` 固定了这个不可行性。

详见 `capability-boundaries.md`。

## 7. 校验器不检查 Master 引用有效性

悬空的 `Master="9999"` 仍返回 `valid: true`，而 Visio 会显示为损坏形状。

**为何未修**：补齐需要在 `visio.shape-ids-unique` 旁增加一项，
解析 `visio/masters/masters.xml` 的 `<Master ID>` 集合并与每页的 `Master=` 属性比对。

**代价**：`ValidatorCoverageTests` 里有一条断言当前行为是 `IsValid == true` 的测试，
实现后它会失败——这是正确信号，但需要同步修改。

**影响**：`valid: true` 不能推出「文件在 Visio 中完整可用」。
外部编辑过的文件可能带本引擎不会写出的悬空引用，而校验器不会报告。

详见 `limitations.md` §11。

---

## 已量化、不构成待决问题的

**评分权重缺依据**（原 §3）：权重决定分数，但没有任何一项说明为何取该值——
`edge.detour` 扣 6、`edge.crossing` 扣 2，绕远路比交叉罚得重 3 倍。

复核后确认它**不影响功能正确性**：

| 事实 | 依据 |
| --- | --- |
| 分数是参考性的 | `Score` 注释明写 "the soft score is advisory"；可交付性只看 `HardConstraintsPassed` |
| 只有 `compare` 用它排序 | `CandidateComparison.Deliverable`，排在可交付过滤之后 |
| 无测试依赖中间分值 | 7 处断言全是 `== 0` 或 `== 100` |
| 实测排序正确 | `deploy-topology` 候选按 90 / 72 / 64 降序 |

**唯一影响**是 `compare` 在多个可交付候选间的偏好。当前候选分差 18–26 分，
远大于权重差 4 分，不会造成颠倒。`SoftWeightBaselineTests` 会拦住无声改动。

## 已完成的不在此列

容器标题间隙（§1 旧）、泳道标题预留（§5 旧）、`edge.diagonal-across-band` 不可达（§6 旧）
均已在 2026-11-19 修复，分别由 `HeadingClearanceTests`、`SwimlaneHeadingTests`、
`DiagonalAcrossBandTests` 固定。
