# 保险理赔系统架构图 — 生产记录

## 输入格式：IR（`execute`），不是 model（`create`）

理由：SKILL §0b 明说「Start with the IR unless you have a specific reason not to」。本图是
「三层 + 横向分层 + 一条旁路异步链」，形状完全可由 `role` 推出（client/gateway/service/queue/
database），没有任何节点需要手工坐标。IR 也让 `canvas.mode=paper` 自动落 A4 —— 手写 model
则要自己算 8.27×11.69 里的每一根坐标轴。

## 产物

| 文件 | 说明 |
| --- | --- |
| `diagram.json` | IR 内容（11 节点 / 10 边 / 3 容器） |
| `design.json` | DesignSpec（A4 paper、baseSize 11、minReadable 9、enterprise 调色板） |
| `diagram.vsdx` | 真实 OPC/VSDX 包 |
| `diagram.png` | 140 dpi 渲染 |
| `check-layout.json` / `execute-result.json` | 机检与执行回执 |

## check-layout

`hardConstraintsPassed: true`，`score: 92`。剩余 3 条：

- `edge.through-container-heading`（warning，RegionRelayout）— e8 claim→pg 切过「数据层」标题文字
- `edge.crossing`（warning，RegionRelayout）— e3 lb→claim 与 e5 underwrite→payment 交叉
- `page.margins`（info，Local）— 左右各 1.19" 留白

## 做过的两次调整

1. 初版有「对象存储/影像件」节点，`e10 claim→oss` 被判 `edge.through-node`（穿过 Redis），
   hard 失败、score 0。IR 无 waypoints、无 anchor 可用（anchor 只能钉位置不能改路由），
   于是去掉该节点换 Elasticsearch 接到 notify 上，hard 转绿。
2. 按 `RegionRelayout` 的建议把服务层节点在 IR 里重排（claim→underwrite→payment→mq→notify），
   **分数与缺陷完全不变**（92 / 同样 2 条 warning）。说明引擎的 `layered` 横向顺序不由 IR
   `nodes` 数组顺序决定，而是从边的拓扑推导——改声明顺序对布局无效。

## 看 PNG 后自己不满意的地方

1. **负载均衡在最右、理赔申请在最左**，`路由` 这条边要从右上角拐到最左，中间借了一条横贯
   服务层底部的「汇流轨」（4 个 junction 圆点）。这条轨在图上没有任何语义，像是配线槽，
   是观感最差的一处。根因应是 lb 只连了一个目标、而该目标被排到最左端。
2. **服务层里 MQ 与 payment/notify 之间的三条虚线互相打结**：`理赔通过`(claim→mq)、
   `订阅通知`(mq→notify) 都挤在 payment 下沿附近，MQ 位于 payment 与 notify 之间，
   横穿 payment 下方的虚线容易看错走向。
3. **`通知服务 → Elasticsearch` 的 `归档` 线很长**（几乎横跨整幅宽度斜穿下去），标签孤立
   浮在空白里，读起来像两个不相干的块。
4. **`理赔申请` 框内文字被连线压住**：lb 下来的入边从框顶穿过「理赔申请」四个字。
   `label.overlaps-node` / `text-occluded` 这类软检查没报，属于 §5b 所说「几何检查看不见」的缺陷。
5. **空白分配不均**：接入层左侧一大片空、数据层右下空；pageUtilisation 0.67、node 0.46。
   三层是等高带宽的，节点数却不同。
6. 图层是 A4 横版（1637×1158 px @140dpi ≈ 11.69×8.27"），符合 `canvas.mode=paper` 选的
   A4 横向；正文最小字号 9pt 在 A4 打印上可读，标题与容器标题清楚。

## 未验证 / 不确定

- 未做 L4（本机无 Visio）。
- 未确认 `role: "storage"` 在最终版被完全移除后 legend 只剩 5 项是否损失了「影像件」这一语义——
  业务上讲理赔影像件应属于数据层，是这次为过 hard 闸门做的取舍。
- 没找到 IR 中控制 `fromPort/toPort` 或 waypoints 的字段（§5a 字段表里没有），所以对
  `edge.through-node` 只能靠增删节点绕开，不能靠改路由精确修。
