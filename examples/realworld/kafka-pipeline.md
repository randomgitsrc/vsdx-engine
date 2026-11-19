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

## 这个案例暴露的问题

**分数 100，但有一处不该出现的绕行。**

`Kafka Connect` 和 `Kafka Broker` 在**同一容器、同一行**，连线却绕到容器外再折回：

```
产物 waypoints: (2.62, 2.62) → (5.00, 2.62)
流平台容器:     y[2.74, 4.26]
=> y=2.62 在容器【下边界之外】
```

同层同容器内、无遮挡的两点，正确画法是水平直线。

**这是检测器看不见的问题**：`check-layout` 报 100 分，无 `edge.*` 问题。

### 未查清的成因

三条路径给出三个不同结果：

| 路径 | e3 的 waypoints |
| --- | --- |
| `execute` 产物 | `y=2.62`（容器外）|
| 直接调引擎（无 FitPage）| `y=3.85`（容器内）|
| 手动加 FitPage | `y=1.78, 4.05`（第三个值）|

**说明 `FitPage`/`Recenter` 与路由之间存在交互，我没查清。** 记录而非猜测。

---

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
