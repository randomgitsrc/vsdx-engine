# 实验 E10：扇出改走总线，结构对了但分数下降

**日期：** 2026-11-20
**状态：** 完成（已实施）
**依据 Goal：** §4.2（专业架构图不是几个矩形加箭头）、第三轮评审 S1/S2

---

## 一、问题

一个节点分出三条以上边到并排的目标时，路由器**逐条独立处理**，结果形态各异。

`slack-cellular` 实测（改前）：

| 边 | waypoints |
| --- | --- |
| `e3 → fe-a` | 2 个，绕到 `(6.15, 4.38) → (2.4, 4.38)` |
| `e4 → fe-b` | 0（直线）|
| `e5 → fe-c` | 0（直线）|

读者看到一条折线和两条斜线，**看不出是同一个分发决策**。

---

## 二、实现

`OrthogonalRouter.RouteFanOuts`：识别「同一源、≥3 条边、目标在同一行」的扇出，
统一按总线走：

```
源 ──┬──────┬──────┬──   母线（水平）
     │      │      │
     A      B      C      垂直下引
```

---

## 三、结果

`deploy-topology` 的 `HAProxy → API 实例 1/2/3` 改后：

```
三条线的 waypoints 完全一致:
  (源x, 4.85) → (目标x, 4.85) → (目标x, 3.68)
```

三个 `9000` 标签整齐排在母线上。

---

## 四、代价：**分数下降**

| 场景 | 改前 | 改后 | 原因 |
| --- | --- | --- | --- |
| `slack-cellular` | 92 | **88** | 3 处竖线穿单元标题带 |
| `deploy-topology` | 92 | **88** | 2 处竖线穿标题带 |
| `org-tree` | 100 | 100 | 无扇出，不受影响 |

**根因是 E7**：容器标题与首列节点同起点，从上方垂直进入容器必然穿过标题文字。
总线让三条线**都**垂直进入，所以都穿。

改前只有 `e3` 绕行避开，另两条从侧面进入也避开——**但那是三条线走法各异换来的**。

---

## 五、为什么仍然采用

**结构对称是更根本的语义。** 读者要判断「是不是均分到三个单元」，靠的是三条线形态一致；
标题穿越是视觉瑕疵，但不改变「这是一个分发」的读法。

而且 E7 有独立解法（让标题避开首列节点），一旦解决，总线就没有代价了。
反过来不成立：如果为了避标题而让三条线走法各异，E7 修好后这个问题依然存在。

---

## 六、边界

- 只在 `deploy-topology` 与 `slack-cellular` 上验证
- 目标不在同一行的扇出不走总线（会穿节点）
- 有 waypoints 的边不参与（作者指定优先）
- 竖线最后一段的斜率由 `VisioSide.Auto` 决定，不是总线产生的

---

## 七、如何复现

```bash
source .envrc.sh
CLI=src/Vsdx.Cli/bin/Release/net8.0/vsdx-tool
echo '{"specVersion":"1.0"}' > /tmp/s.json
$CLI execute --ir examples/realworld/deploy-topology.ir.json --spec /tmp/s.json --output /tmp/d.vsdx
$CLI check-layout --input /tmp/d.vsdx      # 88，2 处 through-container-heading
$CLI export-model --input /tmp/d.vsdx --output /tmp/d.json
# 查 lb 的三条出边，waypoints 结构应完全一致
```
