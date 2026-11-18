# 后续阶段候选缺陷（Stage 5 过程中发现，不属 T0 范围）

**用途：** 记录在实施 Phase 8.2 时发现的、超出当前子任务范围的既有缺陷。
**原则：** 不在此处修复，也不因此扩大当前子任务范围；仅如实登记，供后续评估。

---

## C1 【P1】`DiagramLayout.Apply` 不保证父 Group 包围子 Group

**发现于：** T0 的嵌套 Group 测试补强（提交 `b0c2bd0`）

### 实测

同一夹具，两种布局方向都失败：

```console
LeftToRight: parent 5.25x2.41, child 2.60x1.62  -> encloses = False
TopToBottom: parent 2.41x5.25, child 1.62x2.60  -> encloses = False
```

**与转置无关**——不转置时同样失败。

### 根因

`DiagramLayout.Apply` 的 `OrderGroups` 让父组先于子组处理
（`Visit(parent)` 在 `ordered.Add(group)` 之前）。计算父组 bounds 时，
子组取的是**当时的** `g.Bounds`：

```csharp
var childGroups = page.Groups
    .Where(g => g.ParentId == group.Id && g.Bounds is not null)
    .Select(g => g.Bounds!)     // ← 此刻的值
    .ToList();
```

而子组本身若 `AutoSize`（默认），会在之后被重算成大得多的盒子，
**父组不会回头调整**。结果：父组装不下子组。

### 影响

- `CheckContainment` 只查 Group→Node，查不出这个问题（见下 C2）
- 渲染时子容器可能溢出父容器边框
- 多级嵌套时误差会累积

### 候选修法

两遍处理：先自下而上重算所有组，再自下而上**或**父组重算时使用子组的**新** bounds。
或：处理顺序改为子先父后，父组用已更新的子 bounds。

**需要先有真实用例验证**——当前基准里没有多级嵌套容器，所以此缺陷未在任何基准上暴露。

---

## C2 【P2】`CheckContainment` 不检查父 Group 对子 Group

**发现于：** 同上

`CheckContainment` 的实现只比对 `group.Bounds` 与 `group.Members` 里的
**node**，从不读 `ParentId`：

```csharp
foreach (var memberId in group.Members)
{
    if (!nodes.TryGetValue(memberId, out var node)) continue;   // 只有 node
    ...
}
```

**因此它返回空，不能证明嵌套容器关系正确。** C1 就是在这个盲区里存在的。

### 候选修法

增加一段：对每个 `ParentId` 非空的组，断言其 bounds 被父组 bounds 包围。
与 C1 一并修复才有意义。

---

## 边界

- 两项**均不在 T0 范围**，本次未修复
- 未因此修改任何产品代码
- 建议：进入 8.3 前先补一个「多级嵌套容器」基准，让 C1 在真实产物上可复现
