# 事件驱动订单管道（Kafka）

**来源**：[Confluent 官方文档](https://docs.confluent.io/platform/current/overview.html)
**性质：** **外部真实案例**（第 3 个），也是第一个**以消息总线为中心**的结构

---

## 为什么选这个案例

前两个外部案例都是「区域/单元」结构。这一个不同：

| | Netflix | Slack | Kafka |
| --- | --- | --- | --- |
| 结构 | 2 区对称 | 1 边缘 + 3 单元 | **多源 → 中心 → 多消费方** |
| 关键关系 | 跨区复制 | 区域内隔离 | **扇入 + 扇出** |

组件取自文档中真实存在的产品：Kafka Connect、Kafka Streams、Schema Registry、Tiered Storage、ksqlDB。

---

## 一处曾被误判为缺陷的绕行

`Kafka Connect` 和 `Kafka Broker` 之间的连线绕到容器下方（`y=2.62`，容器底 `2.74`）。

我最初记为「同层无遮挡却绕行」，**这是错的**。查证后：

```
connect x[0.86, 2.59]
sr      x[3.15, 4.55]   <- Schema Registry 挡在中间
broker  x[5.10, 6.40]
```

**`Schema Registry` 正好在两者之间，同高。** 直线必然穿过它，所以路由器绕到节点下方 0.12
（`Clearance`）是**正确的**。

### 为什么重排 IR 无效

我试过把 `Schema Registry` 挪到层末，布局没变。原因在 `LayeredLayoutEngine`：
层内顺序由 `OrderWithinBands` 和 `MinimiseCrossings` 决定，**不采用 IR 的声明顺序**。

而这个优化器的选择是合理的：`e4` 是 `sr → broker`，把 `sr` 放到 `broker` 之后会让 `e4` 反向跨过
`broker`，交叉更多。

### 结论

**这不是引擎缺陷。** 是这张图的拓扑本身让 `Connect → Broker` 成为一条需要绕行的边。

要让它变直，得改图：把 `Connect` 放在 `Broker` 左侧紧邻，把 `Schema Registry` 移到别的位置
（比如 `Broker` 上方或下方作为旁路）。那是**构图决策**，不是引擎问题。

## 实测

- 分数：**100/100**，硬约束通过，页面利用率 79%
- 图例：5 类（数据库/服务/网关/消息队列/存储）

---

## 如何复现

```bash
source .envrc.sh
CLI=src/Vsdx.Cli/bin/Release/net8.0/vsdx-tool
echo '{"specVersion":"1.0","layout":"layered","direction":"topToBottom"}' > /tmp/s.json
$CLI execute --ir examples/realworld/kafka-pipeline.ir.json --spec /tmp/s.json --output /tmp/k.vsdx
$CLI check-layout --input /tmp/k.vsdx      # 100/100
$CLI export-model --input /tmp/k.vsdx --output /tmp/k.json
# 查 e3 的 waypoints 是否在 core 容器 y 范围之外
```
