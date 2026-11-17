# Engine API 契约

**目的：** 明确 Engine 接受什么、拒绝什么、保证什么、不保证什么。这是 `figure-agent` 与
`vsdx-engine` 之间的接口约定。

---

## 1. 三层协议边界

```
figure-agent                          vsdx-engine
─────────────────────────────────────────────────────────
需求理解 / 视角选择                        执行 / 几何 / 文件
        │                                        │
        │  Diagram IR（说什么）                   │
        ├───────────────────────────────────────▶│
        │  DesignSpec（怎么表达）                 │
        ├───────────────────────────────────────▶│
        │                                        │
        │  ExecutionResult（做成了什么）          │
        │◀───────────────────────────────────────┤
        │  VSDX + PNG/SVG                        │
        │◀───────────────────────────────────────┤
```

**职责划分的硬边界：**

| 归 `figure-agent` | 归 `vsdx-engine` |
| --- | --- |
| 该图要表达什么架构思想 | 节点放在哪里 |
| 用哪种视角（分层/拓扑/时序） | 用什么布局算法实现 |
| 分几个区、区叫什么名 | 区在页面上占多大、怎么排 |
| 哪些关系重要、哪些是次要 | 连线怎么路由、标签放哪 |
| 是否需要拆成多图 | 单图的几何与渲染 |
| 视觉风格的选择 | 风格的执行（颜色/字号/间距） |

**Engine 不做：** 猜用户意图、判断架构是否合理、决定"这张图该长什么样"。

---

## 2. 输入契约

### 2.1 Diagram IR

| 字段 | 必填 | 说明 |
| --- | --- | --- |
| `irVersion` | ✅ | 必须 `1.x`。主版本不符直接拒绝（不猜测） |
| `nodes[].id` | ✅ | 全局唯一、稳定。缺 id 或重复 → 拒绝 |
| `nodes[].role` | — | 决定形状族与主题色；未知角色退化为矩形 |
| `edges[].from/to` | ✅ | 必须指向存在的节点，否则拒绝 |
| `containers[].parentId` | — | 必须存在且不成环，否则拒绝 |
| `nodes[].anchor` | — | **可选**。省略则由 Engine 决定位置 |

**Engine 保证：** IR 不需要任何坐标即可执行。IR 不是坐标清单。

### 2.2 DesignSpec

| 字段 | 默认 | 说明 |
| --- | --- | --- |
| `layout` | `auto` | `auto` 按 IR 的 `kind` 选择 |
| `canvas.mode` | `auto` | `auto` 时页面贴合内容 |
| `hardConstraints` | 空 | 未满足会被列进 `unmetConstraints` 且 `isHard=true` |
| `containerLayouts` | 空 | 逐容器覆盖布局策略 |

**Engine 保证：** 任何声明的约束，要么满足，要么**出现在结果的 `unmetConstraints` 里**。
不存在"声明了但被忽略"的情况。

---

## 3. 输出契约

### 3.1 ExecutionResult.status

| 值 | 含义 | 调用方应做什么 |
| --- | --- | --- |
| `succeeded` | 硬约束全过，无降级 | 可交付 |
| `partial` | 有产物，但硬约束或硬设计约束未满足 | **不可直接交付**，需决策 |
| `degraded` | 产物可用，但某项能力走了回退路径 | 可交付，需说明回退点 |
| `failed` | 无可用产物 | 修正输入后重试 |
| `unsupported` | 请求的能力不存在 | 换方案 |

**关键：`partial` 不等于成功。** Phase 0–6 曾把"硬约束通过"等同于"可交付"，
Phase 7 的审计推翻了这一口径（见 `baseline-report.md` §7）。

### 3.2 quality 三段

| 字段 | 含义 |
| --- | --- |
| `hardConstraintsPassed` | 内容是否完整可见（无重叠/裁切/逃逸） |
| `score` | 软质量 0–100，**仅在 hard 通过时有意义** |
| `requiresVisualReview` | 恒为 `true`：Engine 不声称评分等于视觉验收 |

### 3.3 objectMapping

IR id → 模型 id。正常情况是恒等映射（Engine 保留 id），
但重命名或冲突时会有偏移，调用方必须用它回流。

---

## 4. 错误与诊断分级

| 层级 | 载体 | 是否阻断 |
| --- | --- | --- |
| IR 校验错误 | `errors[]` + `status=failed` | ✅ 执行前阻断 |
| 写入/渲染失败 | `errors[]` | ✅ |
| 硬几何违规 | `quality.hardConstraintsPassed=false` | ✅ 标记不可交付 |
| 硬设计约束未满足 | `unmetConstraints[isHard=true]` | ✅ `status=partial` |
| 软约束未满足 | `unmetConstraints[isHard=false]` | ❌ 仅记录 |
| 能力回退 | `warnings[]` | ❌ `status=degraded` |

---

## 5. 幂等性与确定性

**保证：** 同一 IR + 同一 DesignSpec + 同一版本 → **逐字节相同的 VSDX**
（`RoundTrip_BinaryIsReproducibleForSameModel` 覆盖）。

**不保证：** 跨版本可复现。升级后几何可能改变，这是允许的，但必须通过基准回归。

---

## 6. 明确不保证的事项

| 不保证 | 理由 |
| --- | --- |
| VSDX 在 Microsoft Visio 中完全一致 | 无 Visio 环境，无法验证（L4） |
| 渲染结果与 Visio 原生渲染逐像素一致 | 渲染器是独立实现 |
| 自动评分等于专业视觉验收 | 评分只做筛选 |
| 任意 VSDX 都能无损往返 | 见 `vsdx-compatibility-report.md` |
| 超大图（>200 节点）性能 | 未测试 |
| 跨平台字体完全一致 | 依赖系统字体 |

---

## 7. 版本演进规则

1. **主版本变更** = 破坏性变更。`IsCompatible` 会拒绝，调用方必须显式升级。
2. **次版本变更** = 向后兼容的新增字段。旧调用方忽略新字段即可。
3. **未知字段** 一律忽略，不报错。
4. **未知约束码** 必须报告 `unknown_constraint`，**不得静默跳过**。

---

## 8. 调用示例

```bash
vsdx-tool execute \
  --ir architecture.ir.json \
  --spec architecture.design.json \
  --output out/architecture.vsdx \
  --render --dpi 144 --json
```

```csharp
var runner = new ProtocolRunner();
var result = runner.Execute(ir, spec, "out.vsdx", render: true);

if (result.Status != ExecutionStatus.Succeeded)
{
    foreach (var unmet in result.UnmetConstraints.Where(u => u.IsHard))
        Console.Error.WriteLine($"UNMET {unmet.Code}: {unmet.Reason}");
}
```

---

## 9. 契约的测试锚点

| 契约 | 测试 |
| --- | --- |
| IR 无需坐标 | `IrDoesNotRequireCoordinates` |
| 固定锚点被尊重 | `FixedAnchorsAreHonoured` |
| 约束未满足必须报告 | `UnsatisfiableHardConstraintIsReported` |
| 未知约束必须报告 | `UnknownConstraintIsReportedNotDropped` |
| 状态可区分 | `CleanRunReportsSucceeded` / `InvalidIrReportsFailedWithReason` |
| 映射可回流 | `ResultMapsIrIdsBackToModelIds` |
| 阶段顺序 | `StagesAreRecordedInOrder` |
| 版本兼容规则 | `VersionCompatibilityRule` |
| IR 不成环 | `ValidatorCatchesParentCycles` |
