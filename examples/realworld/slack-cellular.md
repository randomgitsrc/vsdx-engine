# Slack 蜂窝架构：可用区即单元

**来源**：[Slack Engineering — Slack's Migration to a Cellular Architecture](https://slack.engineering/slacks-migration-to-a-cellular-architecture/)（2023-08-22）
**建模日期：** 2026-11-20
**性质：** **外部真实案例**（第 2 个）

---

## 为什么选这个案例

它与 `netflix-active-active` 的**拓扑风格完全不同**：

| | Netflix 双活 | Slack 蜂窝 |
| --- | --- | --- |
| 结构 | 2 个区域**对称** | 1 个边缘 + **3 个平行单元** |
| 关系 | 跨区域复制 | 区域内隔离，**不跨区** |
| 视觉要求 | 左右对照 | **并列单元** |

同一个引擎画这两种图，能检验它对不同架构风格的适应力。

---

## 原文的关键约束

| 约束 | 原文表述 |
| --- | --- |
| 三个 AZ 是单元 | "AZs are cells, and cells may be drained" |
| 单元内隔离 | "each service only communicates with services within its AZ" |
| 边缘跨区 | "Edge load balancers are located in different regions entirely" |
| 权重路由 | "Envoy weighted clusters and dynamic weight assignment via RTDS" |
| 控制面 | "Rotor, our in-house xDS control plane" |
| 数据存储 | "our main datastore Vitess" |

---

## 这个案例暴露的问题

### 1. 引擎无法表达「容器并排」

蜂窝架构的视觉本质是：**边缘层在上，三个 AZ 在下并排**——它们是平行的单元，读者要能一眼看出「任何一个坏了，另外两个照常」。

实际渲染把三个 AZ **竖向叠放**，`Envoy` 到 `Vitess B`/`C` 的连线绕到页面右侧，整体读不出「并列」这个核心概念。

**根因**：`layered` 布局把每个容器当成一个带，按 `order` 上下堆叠。六种策略
（`Manual`/`Layered`/`Grid`/`Flow`/`Tree`/`Swimlane`）**没有一种表达容器并排**。

这是**能力缺口**，不是参数问题。`microlith`、多区域部署、任何「N 个平行单元」的图都受影响。

### 2. E7 的标题穿越再次出现

`e3`（Envoy → 前端 A）穿过 `us-east-1a` 的标题文字。与 `cbo` 的 `SQL` 是同一个问题：
容器标题与首列节点同起点。

---

## 实测

- 分数：**96/100**，硬约束通过
- 问题：1 处标题穿越；**布局无法表达并列**（后者检测器测不出）

---

## 如何复现

```bash
source .envrc.sh
CLI=src/Vsdx.Cli/bin/Release/net8.0/vsdx-tool
echo '{"specVersion":"1.0","layout":"layered","direction":"topToBottom"}' > /tmp/s.json
$CLI execute --ir examples/realworld/slack-cellular.ir.json --spec /tmp/s.json --output /tmp/s.vsdx
$CLI render --input /tmp/s.vsdx --format png --output /tmp/s.png
$CLI check-layout --input /tmp/s.vsdx      # 96/100
```
