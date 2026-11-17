# Phase 0 诊断报告

**项目**：VSDX Engine 专业智能制图能力升级
**本轮性质**：诊断与方案设计，**未进行代码重构**（依据 §12 要求）
**诊断基线**：`31baeea`，96 项测试全部通过
**诊断日期**：2026-11-17

---

## 摘要

一句话结论：**当前引擎不具备"制图"能力，只具备"按给定坐标写文件"能力。**

引擎的 VSDX 文件层（OPC 结构、增量编辑、保真度）是扎实的、经得起测试的；
但**布局层几乎为空**——节点坐标完全来自人工手写的 JSON，引擎不参与任何放置决策。
更严重的是，**质量检测器存在系统性盲区**：一张有 0.43 英寸容器重叠的图被判为 95/100，
一张连接线绕行整个页面的图被判为 99/100。

因此本次升级的主要工作量**不在优化现有算法**（现有算法很少），而在**建立缺失的布局与设计层**，
并**先修正质量检测器**——否则后续所有优化都没有可信的验收依据。

---

## 一、当前能力（实测，非推断）

### 1.1 已验证可用的能力

| 能力 | 状态 | 证据 |
| --- | --- | --- |
| VSDX 创建（OPC 包结构正确） | ✅ | `vsdx-tool validate` 逐项全过 |
| 增量编辑（未改对象不重写） | ✅ | `RoundTrip_NoOpUpdateIsLossless` |
| 稳定 ID 跨重命名保持 | ✅ | `RoundTrip_IdsAreStable` |
| 外部 VSDX 读取 | ✅ | `ExternalFixtures` 手写 OPC，不经过本写入器 |
| 中文渲染 | ✅ | 自动注入 CJK 字体 |
| 容器边界推导 | ✅ | `DiagramLayout.Apply` 按成员算 |
| 正交布线（避障） | ⚠️ 部分 | 有实现，但有绕行失控案例（见 §2.3） |
| 布局质量检测 | ⚠️ 有实现但不可信 | 见 §2.1 |

### 1.2 关键事实：`AutoLayout` 名不副实

```csharp
// VsdxEngine.Create — "AutoLayout" 的全部内容
if (options?.AutoLayout ?? true)
{
    DiagramLayout.Apply(diagram);      // 只算容器边界
    OrthogonalRouter.Route(diagram);   // 只算连线路径
}
```

**没有任何节点放置逻辑。** 全仓库搜索 `PlaceNodes` / `ArrangeNodes` / `LayoutNodes` 无结果。
节点坐标 100% 来自输入 JSON 中手写的 `bounds`。

这解释了为什么项目至今的"布局工作"实际上是在**手工调数字**：
`tools/make_architecture.py` 里硬编码了 `COL = {1: 2.60, 2: 5.50, ...}`，
`tools/make_v2.py` 里同样。所谓"布局迭代"，是人眼看完 PNG 后改常量再重跑。

### 1.3 `Diagram.Type` 是死字段

```bash
$ grep -rn "diagram.Type" src/ --include=*.cs
（无结果）
```

模型层声明了 `type: architecture | microservices | workflow | network`，
但**没有任何代码读取它**。任务 §1.2 要求的"图形类型识别与布局策略选择"目前为 0。

### 1.4 无任何布局策略抽象

搜索 `Strategy` / `Layered` / `ForceDirected` / `Tree` 在 `src/` 下无结果。
只有一套隐式策略：**写什么坐标就画什么**。

---

## 二、当前缺陷（按任务 §7 分类，全部可复现）

### 2.1 【最严重】质量检测器系统性漏检，给出虚假高分

**证据 A**：workflow 图的容器存在边框交叠，检测器仍给 99/100。

```
$ vsdx-tool check-layout --input .diag/wf.vsdx
layout check .diag/wf.vsdx  score=99/100
  page.margins (1)  [info]
```

实测容器几何：

```
grp_fulfil   y [2.20, 4.26]  x [0.80, 12.40]
grp_decision y [4.25, 8.96]  x [6.30, 12.40]
→ 垂直间隙 -0.01"（即上下边框交叠），水平重叠 6.10"
```

重叠量不大（0.01"），但**这说明检测器完全没有"容器间关系"这条规则**——
哪怕重叠量放大十倍它也不会报告。真正暴露问题的是下一条：
`e9` 连线绕行比 2.06，以及 `grp_fulfil` 与其它容器构成的版面明显失衡。

**检测器没有"容器与容器关系"这条规则。** 它只测节点重叠、节点与自身容器的包裹关系，
不测容器之间。

**证据 B**：architecture 图的服务层色带压住了接入层 0.43"，仍给 95/100。

```
grp_access   y [7.28, 10.86]  x [1.15, 11.65]
grp_services y [5.65,  7.71]  x [3.95, 20.50]
→ 垂直间隙 -0.43"，水平重叠 7.70"（负值 = 两层色带互相压盖）
```

**证据 C**：打分公式把严重缺陷稀释掉了。

```csharp
score -= Errors.Count() * 12;
score -= Warnings.Count() * 4;
score -= Infos.Count() * 1;
```

一张图即使有 8 个 error 仍有 4 分；更关键的是**未被建模的缺陷贡献 0 分惩罚**。
99/100 与 60/100 目前不代表视觉质量的差异，只代表"已建模的那几条规则有没有触发"。

> **这一条必须先修**。否则 Phase 1–5 的任何优化都无法被可靠验收，
> 而且会重演任务 §4.1 描述的"越修越乱"——因为检测器看不见真正的伤。

### 2.2 【严重】没有节点放置能力，页面尺寸靠人写

- 节点坐标手写在 JSON 里，引擎不参与（§1.2）
- 页面尺寸手写在 JSON 里：`"pageSize": {"width": 21.5, "height": 11.5}`
- 结果：architecture 图**页面下部 24% 完全空白**（内容底边 y=2.75"，页高 11.5"）
- 正确的是：由内容包围盒 + 留白反推页面尺寸（任务 §2.D "画布尺寸应当根据内容自动调整"）

同时节点尺寸也是手写常量：

```
2.2 × 1.0  →  5 个节点
2.2 × 1.1  →  4 个节点
2.3 × 0.95 →  2 个节点
```

尺寸与文字长度**无关**。任务 §3.4 要求"文字测量 → 节点尺寸 → 容器尺寸 → 布局"的链路，
当前是反向的：先有尺寸，再看文字塞不塞得下。

### 2.3 【严重】连线绕行失控，且无度量

实测 workflow 图 `e9`（取消分支）：

```
直线距离 2.24"  →  实际路径 4.60"  →  绕行比 2.06
```

architecture 图 `e-gw-ingest` 与 `e-auth-audit` 也出现过绕行整个页面边缘的情况
（历史上手工加 waypoint 才压下去）。

检测器**没有任何"绕行比"指标**。任务 §2.F 明确要求"连接线必须表达清晰的关系，
不能仅仅追求几何最短"——但当前连"几何是否失控"都没有度量。

### 2.4 【中】排版无层级

```
字号：11pt × 12 个节点，10pt × 3 个节点
```

没有图表标题 / 区域标题 / 节点标题 / 节点说明 / 连线标签的层级区分（任务 §2.C）。
容器标题靠 `fontSize: 12` 与节点 11pt 拉开 1pt，视觉上几乎无差别。

### 2.5 【中】中文字体度量未被布局使用

`TextMetrics` 只在 `LayoutAnalyzer` 里做事后溢出检测，**不参与尺寸与布局计算**。
代码注释自称 "Crude but conservative"。

实测该估算器精度意外地好（CJK 误差 0%，拉丁平均 2.6%）：

| 文本 | 估算 | 真实 | 误差 |
| --- | --- | --- | --- |
| 数据接入服务 | 0.917" | 0.917" | 0.0% |
| write API | 0.640" | 0.584" | +9.5% |
| Ingest Service | 0.975" | 0.924" | +5.5% |

**结论修正**：估算精度不是瓶颈，**"没被用上"才是瓶颈**。
建议保留该估算器作为快速路径，并在 Phase 2 引入真实字体度量作为精确路径。

### 2.6 【中】无快照 / 回滚 / 候选比较

```console
$ grep -rniE "snapshot|rollback" src/ --include=*.cs
src/Vsdx.Adapters/OfficeImoIncrementalEditor.cs:121:  var beforeSnapshot = SnapshotShape(shape);
```

命中的 `SnapshotShape` 返回的是一个**用于 diff 展示的格式化字符串**：

```csharp
private static string SnapshotShape(VisioShape shape) =>
    $"text='{shape.Text}' pin=({shape.PinX.ToInches():F3},{shape.PinY.ToInches():F3}) ...";
```

它没有保存可还原的状态，也不能回滚。`Metadata.History` 同样只是**变更日志文本**，
不是状态快照。**可回滚的版本快照能力当前为 0。**
任务 §4.2–4.8 要求的基线、影响分析、三级修改决策、迭代止损、候选比较——
**一项都没有**。这是任务 §4 整节的内容，当前完成度为 0。

### 2.7 【轻】基准案例不足

`examples/` 只有 3 份（architecture / workflow / network）。
任务 §8 要求 6 类 × 8 种复杂度（含故障注入），当前约 6%。

---

## 三、根因分析

按"为什么会出现这个缺陷"分层，而不是按"哪里代码不对"。

| 缺陷 | 直接原因 | 深层根因 |
| --- | --- | --- |
| 检测器虚假高分 | 规则不全（无容器间重叠、无绕行比、无字号层级） | **检测器与设计意图脱节**：规则是"想到什么加什么"，没有从"专业制图有哪些维度"反推 |
| 无节点放置 | 无布局引擎 | **架构缺口**：项目做的是"文件格式引擎"，布局被当作使用者的责任 |
| 页面尺寸靠人写 | 同上 | 同上 |
| 连线绕行 | 布线器只有局部避障，无全局代价函数 | 缺少"路径质量"概念，只求"不穿障碍" |
| 无回滚 | 只在增量编辑维度思考 | 缺少"制图是一个迭代收敛过程"的认知 |

**根本矛盾**：项目当前定位是 **VSDX 文件引擎**（把 JSON 忠实写成 VSDX），
而任务要求的是 **智能制图引擎**（理解内容 → 设计 → 布局 → 视觉 → 生成 → 检测 → 迭代）。
两者不是同一件事，缺的是中间一整个"设计与布局"层。

**但底层资产是可复用的**：OPC 写入、增量保真、稳定 ID、外部包读取、CJK 渲染——
这些是第二阶段最难做的部分，已经完成且测试充分。升级路线是**在已有地基上加盖楼层**，
不是推倒重来。

---

## 四、技术选型

任务 §3.2 要求评估 ELK / Graphviz / Dagre / OGDF。逐项实测结果：

| 方案 | .NET 集成 | Linux | 离线 | 许可证 | 结论 |
| --- | --- | --- | --- | --- | --- |
| **ELK** | ❌ NuGet 无包 | 需 JVM | 需自带 jar | EPL-2.0 | **不采用**：跨 JVM 边界，集成与分发成本高 |
| **Graphviz `dot`** | ⚠️ 进程调用 | ✅ 已装 2.43.0 | ✅ | EPL-1.0 | **采用（分层/树）**：`dot` 的分层布局与正交 `splines=ortho` 是业界基准 |
| **Dagre** | ❌ JS | 需 Node | 需 npm | MIT | 不采用：引入 Node 运行时 |
| **OGDF** | ❌ C++ | 需编译 | — | GPL | 不采用：GPL 传染 + 构建复杂 |
| **Microsoft.Msagl** | ✅ 纯 .NET 1.1.6 | ✅ | ✅ | **MIT** | **采用（力导向/网络/组织）**：854k 下载，原生托管 |

### 建议组合架构

```
布局策略层（新增）
├── LayeredLayout      → Graphviz dot（分层架构 / 微服务 / 部署）
├── TreeLayout         → Microsoft.Msagl（组织结构）
├── ForceLayout        → Microsoft.Msagl（网络拓扑 / 混合）
├── FlowLayout         → 自研（业务流程：判断分支语义强于图论）
├── GridLayout         → 自研（简单网格 / 仪表盘）
└── ManualLayout       → 保留手写坐标（兼容现有 JSON，不破坏存量）
```

**关键设计约束**：

1. **布局引擎只输出几何，不做视觉决策。** 任务 §3.1 要求"设计规划与几何布局职责分离"。
2. **保留 `ManualLayout` 为默认**，新策略按 `Diagram.Type` + 显式 `layout.strategy` 选择，
   避免破坏现有 3 份示例与 96 项测试。
3. **Graphviz 通过进程调用，不引入 NuGet 依赖**，缺失时自动降级并报告诊断
   （与现有 `CompatibilityReport` 机制一致）。
4. **Msagl 作为可选 NuGet 依赖**，只在需要力导向时加载。

### 为什么不全用 Graphviz

`dot` 强于分层 DAG，但：

- 业务流程图的分支语义（有货/缺货、取消）需要**容器与语义分组**，`dot` 不理解
- 网络拓扑需要**环形/星形**布局，`dot` 会强制成层次
- 组织架构需要**严格同级对齐**，`dot` 会因为边权重打破对齐

所以是**多策略**而非单引擎。

---

## 五、目标架构

```
                          ┌─────────────────────────┐
用户需求 ────────────────▶ │  1. DesignPlanner        │  ← 新增（语义层）
                          │  内容分析 / 类型识别      │
                          │  信息层次 / 分组方案      │
                          └───────────┬─────────────┘
                                      ▼ DesignSpec（可记录/可检查/可复用）
                          ┌─────────────────────────┐
                          │  2. TextStyle & Metrics  │  ← 新增（字体层）
                          │  字体度量 → 字号层级       │
                          └───────────┬─────────────┘
                                      ▼ MeasuredText
                          ┌─────────────────────────┐
                          │  3. Sizer                │  ← 新增（尺寸层）
                          │  文字 → 节点 → 容器 → 画布 │
                          └───────────┬─────────────┘
                                      ▼ 带真实尺寸的模型
                          ┌─────────────────────────┐
                          │  4. LayoutStrategy       │  ← 新增（几何层）
                          │  Layered/Tree/Force/…    │
                          └───────────┬─────────────┘
                                      ▼ 坐标
                          ┌─────────────────────────┐
                          │  5. OrthogonalRouter     │  ← 已有，需加代价函数
                          └───────────┬─────────────┘
                                      ▼
                          ┌─────────────────────────┐
                          │  6. VSDX Writer          │  ← 已有，不动
                          └───────────┬─────────────┘
                                      ▼
                          ┌─────────────────────────┐
                          │  7. Renderer             │  ← 已有，不动
                          └───────────┬─────────────┘
                                      ▼ PNG
                          ┌─────────────────────────┐
                          │  8. QualityGate          │  ← 已有，需大幅扩充
                          │  硬约束 / 软约束分离      │
                          └───────────┬─────────────┘
                                      ▼
                          ┌─────────────────────────┐
                          │  9. IterationController  │  ← 新增（收敛层）
                          │  快照/回滚/三级修复/止损  │
                          └─────────────────────────┘
```

**与现有代码的关系**：第 5、6、7 层已存在且质量良好，不动。
第 8 层存在但需重写规则集。1–4、9 层为新增。

---

## 六、关键数据结构

### 6.1 `DesignSpec`（设计意图，任务 §1.1 的中间描述）

```csharp
public sealed class DesignSpec
{
    public DiagramKind Kind { get; init; }        // Architecture/Flow/Network/Org/Deployment/Mixed
    public string Intent { get; init; }           // "表达分层职责与依赖"
    public string Audience { get; init; }         // "架构评审"
    public List<VisualTier> Hierarchy { get; init; }   // 信息层次
    public LayoutStrategyKind Strategy { get; init; }
    public LayoutDirection Direction { get; init; }    // LR/TB
    public ThemeSpec Theme { get; init; }
    public TypeScale TypeScale { get; init; }     // 标题/区域/节点/说明/标签 字号
    public CanvasSpec Canvas { get; init; }       // 规格或 auto
    public List<string> Constraints { get; init; } // 特殊约束
}
```

### 6.2 `TypeScale`（字号层级，替换当前硬编码 11pt）

```csharp
public sealed record TypeScale(
    double DiagramTitle, double RegionTitle,
    double NodeTitle, double NodeCaption,
    double EdgeLabel, double Annotation)
{
    public static TypeScale ForCanvas(double wIn, double hIn, int nodeCount);  // 按画布与密度推导
}
```

### 6.3 `QualityReport`（硬/软约束分离，任务 §5.1）

```csharp
public sealed class QualityReport
{
    public bool HardConstraintsPassed { get; }   // 任一失败 => 不可交付
    public List<QualityIssue> HardViolations { get; }
    public List<QualityIssue> SoftViolations { get; }
    public double SoftScore { get; }             // 仅当 Hard 通过才有意义
}

public sealed record QualityIssue(
    Severity Severity,        // Blocker/Major/Minor/Info
    string Code,              // group.overlap / edge.detour / type.no-hierarchy
    string[] Targets,
    string RootCauseHint,     // 任务 §5.2 要求"根因推断"
    string RecommendedFix,    // 任务 §5.2 要求"推荐修复策略"
    FixLevel SuggestedLevel); // Local / RegionRelayout / FullRedraw
```

### 6.4 `VersionStore`（快照与回滚，任务 §4.2）

```csharp
public interface IVersionStore
{
    VersionSnapshot Capture(Diagram model, string label);   // 模型+VSDX+PNG+质量报告
    IReadOnlyList<VersionSnapshot> History { get; }
    void Rollback(string snapshotId);
    bool TryAccept(string snapshotId, QualityReport candidate);  // 只有改善才接受
}
```

### 6.5 `FixAttempt`（每次修改的可验证目标，任务 §4.6）

```csharp
public sealed record FixAttempt(
    string Problem, string RootCause, string Rationale,
    string[] ExpectedAffected, string ExpectedMetricGain,
    string? ActualGain, bool IntroducedNewIssues,
    FixDecision Decision);   // Accepted / RolledBack / Escalated
```

---

## 七、修复与重绘机制

### 7.1 三级修改决策（任务 §4.4）

```
发现缺陷
   │
   ├─ 影响面 = 该元素 + 相连边 + 标签
   │    且 不触及容器/画布 → 【一级 局部修复】
   │
   ├─ 影响面 ∈ 单个容器内的多个元素
   │    且 容器外几何不变 → 【二级 区域重布局】
   │
   └─ 影响面跨多个容器 / 多轮局部修复失败 / 布局策略本身不合理
        → 【三级 整体重绘】
```

**判定依据必须是可计算的量**，不能是 Agent 的感觉：

| 级别 | 触发条件（可计算） |
| --- | --- |
| 局部 | `affectedNodes ≤ 2` 且 `affectedContainers = 0` |
| 区域 | `affectedContainers = 1` 且该容器内 `affectedNodes ≥ 3` |
| 整体 | `affectedContainers ≥ 2` 或 `consecutiveFailures ≥ N` 或硬约束仍失败 |

`N` 通过基准测试标定后写入配置，不硬编码在业务逻辑（任务 §4.7 明确要求）。

### 7.2 止损与候选比较

```
对同一 issue code：
  尝试局部修复 → 失败 → 记录
  再次尝试同类修复 → 失败 → 禁止第三次同类尝试（策略去重）
  → 升级为区域/整体重布局
  → 生成 K 个候选（不同方向 / 间距 / 主题）
  → 硬约束过滤 → 软约束排序 → 选最优
  → 候选隔离生成，失败不影响稳定版本
```

### 7.3 为什么能避免"越修越乱"

任务 §4.1 描述的病根是**局部修改引发连锁变化且无回滚**。三条机制阻断它：

1. **影响面分析**：改之前就知道会波及什么（当前完全没有）
2. **强制快照**：每次修改前落盘，失败必回滚（当前完全没有）
3. **策略去重 + 升级**：同一失败手段不允许重复（当前完全没有）

---

## 八、分阶段实施计划

| Phase | 目标 | 关键交付 | 验收标准（可测） |
| --- | --- | --- | --- |
| **0** | 诊断（本轮） | 本报告 | 问题可复现、根因可验证 |
| **1** | **先修检测器** | 容器重叠 / 绕行比 / 字号层级 / 硬软约束分离 | 现有 3 张图在新检测器下**如实报出已知缺陷**；加入 8 条回归测试锁定遗漏规则 |
| **1b** | 基础几何修正 | 修容器重叠、页面自适应、绕行上限 | 新检测器下 3 张图硬约束全过 |
| **2** | 字体与尺寸 | `TypeScale`、真实字体度量、`Sizer` | 节点尺寸由文字反推；`TypeScale` 分层可见 |
| **3** | 布局引擎 | `DesignSpec`、`LayoutStrategy`（Layered/Tree/Force/Flow）、Graphviz + Msagl 接入 | 3 份示例改用自动布局生成，硬约束全过，人工目视通过 |
| **4** | 迭代机制 | `VersionStore`、影响分析、三级修复、候选比较 | 注入缺陷后可自动收敛；失败可回滚；同策略不重复 |
| **5** | 基准与回归 | 6 类 × 8 复杂度基准案例 + 视觉回归 | 基准全过 + PNG 基线比对 |
| **6** | Visio 原生 / Word | （需 Windows 环境，本轮不做） | — |

### 为什么 Phase 1 是"修检测器"而不是"修布局"

任务 §12 要求先诊断。诊断结论是：**当前没有可信的验收工具**。
在检测器看不见容器重叠的情况下优化布局，等于蒙着眼睛改代码——
这正是任务 §4.1 警告的失败模式。所以顺序必须是：先让尺子准，再动刀。

---

## 九、风险

| 风险 | 影响 | 缓解 |
| --- | --- | --- |
| Graphviz 进程调用在不同环境不可用 | 分层布局失效 | 检测降级 → 回退自研网格布局 + `CompatibilityReport` 警告 |
| 自动布局破坏现有 3 份示例 | 回归 | `ManualLayout` 为默认，新策略需显式开启；96 项测试不回归 |
| 真实字体度量引入新依赖 | 体积/许可 | Phase 2 先复用现有估算器（实测精度足够），真实度量作为可选精确路径 |
| 三级修复判定阈值难标定 | 误升级/误局部 | 阈值从基准案例标定，写配置；先保守（易升级）避免"越修越乱" |
| 任务要求过高，一轮做不完 | 交付不完整 | 已按 Phase 切分，每阶段独立可验收 |
| **视觉审查无法自动化** | 质量判断失真 | 保留人工/视觉模型审查环节（任务 §5.3 明确要求），算法评分只做筛选 |

---

## 十、对任务本身的两处技术异议

按任务 §12 "如果发现目标不合理应明确指出"，有两处需要提出：

### 10.1 "不能无限缩小字体"与"自动适配画布"存在张力

任务 §1.4 禁止靠缩字号解决过载，§2.C 又要求字号随密度自适应。
建议明确规则：**字号只允许在 `[MinReadablePt, MaxPt]` 内浮动**，低于下限时
不再缩字号，而是触发"拆图 / 扩画布 / 减分组层级"。

### 10.2 六类 × 八种复杂度 = 48 个基准案例的工作量

任务 §8 的矩阵（6 类 × {简单,中等,复杂,长文字,多连接,多容器,高密度,极端比例}）
实际是 48 组，每组要存 7 类产物。建议 **Phase 5 先做 6 类 × 3 复杂度（18 组）**，
故障注入场景作为独立用例集（约 8 个），避免为凑矩阵而产生同质案例。

---

## 十一、验收标准（Phase 0 自身）

| 项 | 判定 |
| --- | --- |
| 现有能力已实测而非推断 | ✅ 运行了测试、CLI、图像目视 |
| 缺陷可复现 | ✅ 每条附命令或几何数字 |
| 根因可验证 | ✅ `AutoLayout` 源码、`grep` 空结果、容器几何实测 |
| 技术选型有实测依据 | ✅ NuGet API 查询、`dot -V`、许可证核对 |
| 未进行大规模代码改动 | ✅ 本轮仅新增本报告与诊断产物 |
| 目标架构可落地 | 待评审 |

---

## 附：本轮诊断产物

| 文件 | 说明 |
| --- | --- |
| `docs/phase0-report.md` | 本报告 |
| `.diag/`（未提交） | 诊断期间的临时模型与渲染 |

复现诊断的关键命令：

```bash
dotnet test VsdxEngine.sln -c Release
vsdx-tool check-layout --input demo/architecture.vsdx      # 95/100（但容器重叠 0.43"）
vsdx-tool check-layout --input .diag/wf.vsdx               # 99/100（但容器重叠 4.04"）
git grep -n "diagram.Type" -- src/                          # 空 → type 字段是死的
git grep -n "PlaceNodes\|ArrangeNodes" -- src/              # 空 → 无节点放置
```
