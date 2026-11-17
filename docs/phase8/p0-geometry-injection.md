# P0 缺陷根因分析与修复记录

**缺陷：** 外部 VSDX 编辑会为每个无几何形状注入矩形 Geometry 段，覆盖原始轮廓。
**状态：** ✅ 已定位、已修复、已验证
**日期：** 2026-11-17

---

## 1. 根因定位过程

### 1.1 第一步：排除本项目写入器

构造一个最小 OPC 包：单个 shape，**无 Geometry 段、无 Master 引用**。
直接调 `OfficeIMO.Visio.VisioDocument.Load(...)` 再 `Save(...)`，**不做任何修改**：

```
A. source package geometry sections: 0
B. after OfficeIMO load+save        : 1   ← 库自己合成的
```

**结论：不是我们的写入器，是 OfficeIMO 在 Save 时合成。**

### 1.2 第二步：确定触发条件

对比两种形状：

| 场景 | 载入前 geometry | 载入后 rows | 载入后 sections |
| --- | --- | --- | --- |
| **无几何、无 Master** | 0 | 0 → **5** | 0 → **1** |
| **有 Master（圆形，25 行）** | 25 | 25 → **25** | 1 → **1** |

**触发条件：形状既无 Geometry 段、又无 Master 引用时，库合成一个 5 行矩形。**

有 master 的形状完全不受影响——这解释了为什么该缺陷此前没被发现：
项目自建文件全部带 master，只有**外部文件**才可能撞上。

### 1.3 第三步：确认没有开关

```console
$ grep -c "NoGeometry\|SkipGeometry\|PreserveGeometry" OfficeIMO.Visio.xml
0 0 0
```

库没有提供禁用该行为的选项。**必须在保存后自行修正。**

---

## 2. 修复方案

**核心约束（负责人意见 1）：绝不接受破坏原文件；无法保证保真时安全拒绝，不得静默产出。**

### 2.1 为什么不是"一律删除 Geometry"

若不区分来源地删除所有 Geometry，会把真实几何（圆形的 25 行、自定义路径）
也删掉——那比原缺陷更糟。

### 2.2 采用：加载前快照 + 保存后精准剥离

```
加载前：记录哪些 shape 没有 Geometry 段        (SnapshotUngeometriedShapes)
        ↓
加载并保存（库会合成矩形）
        ↓
保存后：只对"原本无几何"的 shape，
        且该段的签名恰好是合成矩形时，才删除   (RestoreAfterSave)
```

**签名判定：** 合成矩形固定为 1 个 `MoveTo` + 4 个 `LineTo`，共 5 行。
圆形的 25 行、自定义路径的任意行数都不会误匹配。

**跳过条件：** 若该 shape 现在带 `Master` / `MasterShape` 属性，说明它合法地引用了母版，
不删（否则会破坏母版形状的几何）。

### 2.3 为什么不是"直接改原始 XML 不经过库"

那是更大的重构（放弃 OfficeIMO 的读写），本轮明确"不要求重写架构"。
post-save 修正用最小改动达成同等保真效果。

---

## 3. 验证

### 3.1 无操作往返

```
BEFORE: geometry sections = 0
AFTER : geometry sections = 0     ✅
```

### 3.2 加一个节点（关键场景）

```
AFTER add-node: total shapes= 3  geometry sections= 2
   id=1  Rectangle   geom=0   ← 原有形状，保持无几何
   id=2  Rectangle   geom=1   ← 新增节点，合理地带几何
   id=3  Connector   geom=1   ← 新增连线
```

**原有形状不受影响，新对象正常获得几何。** 这正是修复应有的行为。

### 3.3 不破坏真实几何

自建圆形（25 行几何）经 `update` 后仍为 25 行。

### 3.4 幂等性

连续两次 `update` 不累积、不丢失几何段。

---

## 4. 回归测试

`FidelityTests`（7 项）：

| 测试 | 保证 |
| --- | --- |
| `P0_NoOpUpdateDoesNotAddGeometry` | 零改动不加几何 |
| `P0_AddingNodeLeavesExistingShapesUngeometried` | 加节点不污染原有形状 |
| `P0_DoesNotStripRealGeometry` | 圆形 25 行不被误删 |
| `P0_GeometryPreserverIsIdempotent` | 多次往返稳定 |
| `P0_SnapshotFindsShapesWithoutGeometry` | 快照正确识别 |
| `P0_SnapshotIgnoresGeometriedShapes` | 快照不误报 |
| `P0_SynthesisedRectangleIsWhatTheLibraryProduces` | **库行为变化时立刻报警** |

最后一条尤其重要：它直接把"库合成 5 行矩形"这个前提写成断言。
若 OfficeIMO 升级后改成 6 行或别的形状，该测试会失败，提示修复逻辑已失效。

---

## 5. 残留风险

| 风险 | 说明 | 缓解 |
| --- | --- | --- |
| 依赖"5 行矩形"签名 | 库若改默认形状，剥离失效 | 已有测试锁住该前提 |
| 无法识别"作者本意就是矩形但省略几何" | 这类形状会被剥离几何，回退到母版/无几何 | 视觉等价（都是矩形），无实际损害 |
| 图片/自定义路径等复杂几何 | 不受影响（行数不匹配签名） | 实验 1 已验证 |
| L4 Visio 原生行为 | 未验证 | 如实标注 |

---

## 6. 与负责人决策的对应

| 决策 | 落实 |
| --- | --- |
| 先定位根因 | ✅ 已定位：库在 Save 时合成 |
| 尝试 OfficeIMO 上层修复 | ✅ 采用 post-save 剥离，未动库 |
| 若无法保证保真则安全拒绝 | **当前无需拒绝**——修复已能保证保真 |
| 不得静默生成损坏文件 | ✅ 已有 `write.geometry-preserved` 兼容性记录 |

**注：** "安全拒绝"路径尚未实现，因为当前修复已足够。
若未来遇到无法剥离且可能损坏的情况，需补该路径。
