# 实验 E5：路由器对交叉的无感知是结构性的

**日期：** 2026-11-19
**状态：** 完成（Verified）
**依据 Goal：** §4.2 F（连接与关系）、§4.3 Level 1 的 D2 第三项

---

## 一、问题

Level 1 唯一未达标项是 D2 的「多对多交叉控制」。上一轮做了带内重排，消除了部分交叉，
但剩余的交叉涉及**带内边被跨带边穿过**——重排无法解决。

**问题**：路由器是否「知道自己会产生交叉」？

---

## 二、考察源码

`OrthogonalRouter.RoutePage` 的结构：

```csharp
foreach (var edge in page.Edges)
{
    if (edge.Waypoints 非空) continue;                    // 作者指定，不改
    if (非正交 kind)
    {
        if (!PathHitsShapes(direct, others)) continue;    // 直线不穿形状 -> 保持直线
    }
    var attempt = RouteBetween(...);
    if (attempt.Blocked)                                  // 只有穿形状才换端口组合
        foreach (var (fs, ts) in SidePairs(...)) { ... }
    edge.Waypoints = attempt.Waypoints;
}
```

**关键事实**：

| 观察 | 证据 |
| --- | --- |
| 逐条独立路由 | `foreach (var edge in page.Edges)`，从不看已路由的边 |
| 源码中无 `crossing` 一词 | `grep -i crossing src/Vsdx.Engine/OrthogonalRouter.cs` 无结果 |
| 存在候选搜索 | `SidePairs` 枚举端口组合，但**判据只有 `Blocked`**（是否穿形状）|
| 不穿形状的直线**根本不改** | 实测：两条不穿形状的边，`Route` 后 kind 仍为 `straight`，无 waypoints |

---

## 三、结论

**结论（Verified）：交叉在任何路由路径上都不参与决策。**

这不是「启发式不够好」，是**结构性的**：

- 直线边：只避形状，且多数情况保持原样
- 正交边：有候选搜索，但只在 Blocked 时启用，且不看交叉

所以「多对多交叉控制」不是调参能解决的，需要：

1. 候选搜索的判据加入**新增交叉数**
2. 路由时能看到**已路由的边**
3. 直线边也要进入候选搜索，而不是直接 `continue`

---

## 四、改造成本评估

| 改动 | 规模 | 风险 |
| --- | --- | --- |
| 候选判据加入交叉数 | 小（改 `attempt.Blocked` 的判断）| 低 |
| 路由时感知已路由的边 | 中（需要维护已放置路径列表）| 中 |
| 直线边纳入搜索 | **大**（改变默认行为，影响每个场景）| **高** |

第三项是关键风险：目前「不穿形状就保持直线」是一个**有意的保守策略**，
文档 `capability-boundaries.md` §9 解释过它（不替作者做没要求的决定）。

**若让所有直线都进入交叉优化，那张图会从「作者画的直线」变成「路由器重新画的折线」**——
这是行为变更，需要产品判断。

---

## 五、对 Level 1 判断的影响

Level 1 的 D2 第三项未达标，**原因是结构性的，且修复涉及默认行为变更**。

### 五附、收益核实（2026-11-19，推翻了本文件初稿的建议）

初稿建议「先做低风险部分：正交边的 SidePairs 搜索加入交叉判据」。**实测这个改动零收益**：

```
deploy-topology 的 14 条边: 全部 kind=straight
两处交叉 (d7×d9, d11×d14): 都发生在 straight 边之间
```

**没有任何一条边走正交路由**，所以改进正交边的搜索解决不了现有交叉。

`cbo` / `user-data-flow` / `checkout-sequence` 同样以 straight 为主。
换句话说：**交叉几乎全部发生在「路由器根本不碰」的边上。**

因此工作只能落在直线上：

| 方案 | 性质 |
| --- | --- |
| 让直线边也进入交叉优化 | **改变默认行为**——作者画的直线会被改写成折线 |
| 保持直线，改布局减少交叉来源 | 已做（带内重排）；剩余的是带内边被穿 |

**这是产品决策，不是算法选择。** `capability-boundaries.md` §9 记录了
「直线只在穿形状时才绕行」是有意的保守策略，理由是「不替作者做没要求的决定」。
要推翻它，需要明确「宁可改线也要减少交叉」这个优先级。

---

## 六、这份结论的边界

- 只读了 `OrthogonalRouter.cs`，未验证 `ComputeLanes` / `ComputeChannels` 是否隐含避让
- 未测量「正交边加入交叉判据」的实际收益——那需要先实现
- 未考虑 Visio 侧的呈现（L4 Blocked）

---

## 七、如何复现

```bash
grep -i crossing src/Vsdx.Engine/OrthogonalRouter.cs    # 无结果
```

以及 `RouterCrossingAwarenessTests` 的三项测试。
