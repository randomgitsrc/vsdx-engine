# 评分权重表

`check-layout` 的分数是 `100 - Σ(权重 × min(出现次数, 3))`，info 级再乘 0.5。
硬约束失败时分数直接归零。

**权重决定一切分数，但没有任何一项说明为何取该值。** 本文件把表列出来，让它可以被复核。

## 当前权重

| 权重 | 检测码 | 级别 |
| --- | --- | --- |
| **6** | `edge.detour` · `edge.fan-in` · `edge.fan-out` | warning |
| **5** | `edge.diagonal-across-band` · `group.gutter` | warning |
| **4**（默认） | `label.overlaps-label` · `label.overlaps-node` · `container.heading-collision` · `text.overlaps-*` · `row.misaligned` · `spacing.too-tight` | warning |
| **3** | `row.size-jitter` | warning |
| **2** | `edge.crossing` | warning |

error 级（`node.overlap`、`group.overlap` 等 7 项）不参与加权——它们直接令分数归零。

## 一个值得质疑的排序

**`edge.detour` 扣 6，`edge.crossing` 扣 2**——绕远路比两线交叉罚得重三倍。

同样，`label.overlaps-label` 和 `container.heading-collision`（都让文字更难读）都是 4，
低于绕行。

**没有找到这些取值的依据。** 它们可能是早期凭直觉定的，也可能有未记录的理由。

## 为什么没有直接改

改任何一项都会改变全项目所有分数：

- 分场景基准（cbo 100、deploy 82 等）全部需要重新记录
- `docs/phase8/phase8.3-realworld-validation.md` 里的历史分数失去可比性
- 这是产品取舍，不是缺陷修复

`SoftWeightBaselineTests` 把当前值固定住：任何改动会让测试失败，从而走一次显式决策，
而不是让分数悄悄漂移。

## 若要重新评估

合理的做法是按「读者能否看懂」逐一论证据，而不是凭直觉：
例如 `text.overflow-*` 已经因内容丢失而列为 error，那 `label.overlaps-label` 只是难读，
两者的相对权重应当有量化的可见性依据（字号、对比度、遮挡面积）。
