# Netflix 双区域双活架构

**来源**：[Netflix Tech Blog — Active-Active for Multi-Regional Resiliency](https://netflixtechblog.com/active-active-for-multi-regional-resiliency-c47719f6685b)
**建模日期**：2026-11-19
**性质**：**外部真实案例**，不是本项目虚构的场景

---

## 为什么有这个案例

此前六个示例（`cbo`、`deploy-topology` 等）全部是自产的。
它们测的是「引擎能否画我设计的图」，不是「引擎能否画真实世界的图」。

这份 IR 从 Netflix 公开的技术博客提取，组件与拓扑都来自原文，不是编的。

---

## 原文中的关键约束

| 约束 | 原文表述 |
| --- | --- |
| 区域自治 | "no synchronous cross-region calls on the user request path" |
| 本地写 | "Writes go local; Cassandra replicates multi-directionally" |
| 热缓存跨区失效 | "EVCache invalidates cross-region via SQS" |
| 双活范围 | `us-east-1` 与 `us-west-2` |

---

## 这个案例暴露的问题（虚构场景测不出来）

### 1. 对称容器内的顺序不一致

两个区域结构完全相同，但引擎给出的内部顺序不同：

```
east: zuul → disc → evc → cass
west: zuul → evc  → disc → cass
```

**原因**：容器内顺序由拓扑排序决定，而两个区域的入边方向不完全对称。
`LayeredLayoutEngine.OrderWithinBands` 按图遍历，没有「相同结构应产生相同顺序」的约束。

**这是真实局限**：读者想对比两个区域，但顺序不同就无法逐行对照。

### 2. 跨区边导致的交叉

`e4`（写本地）与 `e6`（跨区失效）、`e10`（西区入口）交叉 3 处。
虚构场景里我从未设计过「两条对角线互穿」的拓扑——真实系统里它是常态。

---

## 实测

- 分数：**94/100**，硬约束通过
- 交叉：3 处（拓扑性，见 `capability-boundaries.md` §八）

---

## 如何复现

```bash
source .envrc.sh
CLI=src/Vsdx.Cli/bin/Release/net8.0/vsdx-tool
echo '{"specVersion":"1.0","layout":"layered","direction":"topToBottom"}' > /tmp/s.json
$CLI execute --ir examples/realworld/netflix-active-active.ir.json \
             --spec /tmp/s.json --output /tmp/n.vsdx
$CLI check-layout --input /tmp/n.vsdx      # 94/100
```
