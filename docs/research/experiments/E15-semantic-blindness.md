# 实验 E15：检测器对语义错误完全失明

**日期：** 2026-11-20
**状态：** 完成（结论为量化证据）
**依据：** 阶段 1 报告反复断言「评分器测不到构图」，本实验把它变成可复现的数据

---

## 一、方法

构造一张**几何合规但语义错误**的图：

| 错误 | 性质 |
| --- | --- |
| 客户端**直接连数据库**，绕过所有服务 | 架构上等于绕过应用层 |
| `订单 API` 标记 `drained`，但没有单元承接 | 状态语义不成立 |

同时保持：三层对齐、节点不重叠、页边距正常。

---

## 二、结果

```
score=90/100
edge.through-container-heading (2)
```

**两个语义错误一个都没被报出。** 检测器报的是 2 处标题穿越——与语义无关的几何问题。

---

## 三、20 个检测项全是几何的

```
container.heading-collision   edge.crossing          edge.detour
edge.diagonal-across-band     edge.fan-in            edge.fan-out
edge.through-container-heading edge.through-node     group.gutter
group.member-escapes          group.overlap          label.overlaps-label
label.overlaps-node           node.out-of-page       node.overlap
page.margins                  page.underused         row.misaligned
row.size-jitter               spacing.too-tight
```

**没有一条检查「这条边在架构上是否合理」。**

---

## 四、这与评审的结论一致

第四轮评审：「不是排版没对齐，而是**约束缺失**。所以它们整齐但不可靠——
而『整齐』正是最容易骗过验收的地方。」

本实验给出了那个判断的可执行证据：**一张绕过应用层的图能拿 90 分。**

---

## 五、这意味着什么

阶段 1 的验收瓶颈不是「分数不够高」，而是**分数测的东西和要证明的东西不是一回事**。

要验证「专业制图能力」，需要检测器能回答：

- 这个连接在架构上存在吗？
- 这个组件的职责与它的位置一致吗？
- 这个状态（drained）有对应的处置吗？

**这三类问题都需要语义信息，而 `LayoutAnalyzer` 只看坐标。**

---

## 六、顺带发现的两个真实缺陷

构造本实验时发现：

| 缺陷 | 现象 |
| --- | --- |
| `IrNode.Status` 的虚线不生效 | 节点设了 `StrokePattern` 却没设 `Stroke` 颜色，Writer 要求两者都有 |
| `Status` 读不回来 | 模型无 `Status` 字段，状态只存在于视觉效果 |

第二条违反了项目硬约束「协议接受、Schema 允许、执行时忽略、结果又没有报告」。

两条都已修。

---

## 七、如何复现

```bash
source .envrc.sh
CLI=src/Vsdx.Cli/bin/Release/net8.0/vsdx-tool
cat > /tmp/bad.json <<'IR'
{"irVersion":"1.0","id":"bad","kind":"architecture",
 "containers":[{"id":"c","label":"接入层","order":3},
               {"id":"s","label":"服务层","order":2},
               {"id":"d","label":"数据层","order":1}],
 "nodes":[{"id":"client","label":"移动端","role":"client","containerId":"c"},
          {"id":"api","label":"订单 API","role":"service","containerId":"s"},
          {"id":"pg","label":"PostgreSQL","role":"database","containerId":"d"}],
 "edges":[{"id":"e1","from":"client","to":"pg","relation":"writes"},
          {"id":"e2","from":"api","to":"pg","relation":"reads"}]}
IR
echo '{"specVersion":"1.0","layout":"layered","direction":"topToBottom"}' > /tmp/s.json
$CLI execute --ir /tmp/bad.json --spec /tmp/s.json --output /tmp/bad.vsdx
$CLI check-layout --input /tmp/bad.vsdx     # 90 分，只报几何问题
```
