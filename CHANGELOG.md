# Changelog

本文件记录对外可见的能力变化。格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)。

## [Unreleased]

`v0.9.0` 之后的工作。**没有新增图类型，全部是让已有能力如实工作。**

### 新增

- `nodeUtilisation`：只统计节点的页面利用率。`pageUtilisation` 含文本块，
  一个远离节点的注释就能把它抬到整页——`checkout-sequence` 报 84%，而节点只占 23%。
  两个数字并存，`pageUtilisation` 的既有语义不变。

### 修复（对外可见的行为）

| 症状 | 影响 |
| --- | --- |
| 容器标题被首行成员压住 | 推导高度与放置位置用的是同一个公式，**必然相接**。四个耦合点同时修正 |
| 内容未在纸张上居中 | 纸张方向选择改为按余量竞争 |
| band 间距随内容变化 | 容器包络被重复计入 |
| `canvas.mode=paper` 不检查溢出 | 形状可以落在纸外而状态仍为 `partial` |
| `canvas.scaled` 声称"已缩放"但没缩放 | 73" 内容放在 17" 纸上，措辞改了 |
| `requiresVisualReview` 恒为 `true` | 改为跟随实际违规 |
| `lossless` 把 info 当损失 | 只看 error/warning |
| `metadata.source` 丢弃、`metadata.created` 被生成时刻覆盖 | 两者现在如实往返 |
| 文本块 `role` 由 id 前缀决定 | `{"id":"tb","role":"note"}` 曾返回 `caption` |
| `constraint.note` 被丢弃 | 现在附加到未满足约束的 reason |
| 布局问题的严重程度被压平为 warning | `error`/`warning`/`info` 三级如实传递 |
| 校验器不检查 Master 引用 | 悬空引用曾返回 `valid: true` |

### 修复（内部，但影响结果）

- `edge.diagonal-across-band` 曾因分析前先路由而**永不触发**——已可达
- `repair` 的重算开关从 `ValidateAfterSave` 独立出来（`--no-recalc`）

### 变更

- `update` 不再因 `validateAfterSave` 而请求 Visio 重算；改用独立选项

---

## [0.9.0] — 2026-11-18

已发布为 **prerelease**：<https://github.com/randomgitsrc/vsdx-engine/releases/tag/v0.9.0>

**版本号定为 `0.9.0` 而非 `1.0.0`。** 理由：项目定义的核心承诺是「原生、可编辑的 VSDX」，
而 L4（Visio 原生打开）从未执行。`1.0.0` 意味着承诺已验证；`0.9.0` 表示能力已建立但验证不完整。

版本号现由程序集元数据驱动（`EngineInfo.Version`），不再有第二处硬编码。

### 未验证（发布前必读）

- **Microsoft Visio 原生打开（L4）从未执行。** 本机无 Visio，`/mnt/c` 只读，WPS 不可替代。
  全部验证止于 L1（包结构）、L2（读回）、L3（渲染）。项目定义的核心承诺
  「原生、可编辑的 VSDX」目前是**假设**，证据见 `docs/limitations.md` §1 与 §10。

### 已建立的能力

**创建**

- 从结构化描述（Diagram 模型或 Diagram IR）生成 VSDX
- 六种布局策略：`layered` · `flow` · `tree` · `grid` · `swimlane` · `manual`
- `auto` 按 IR 的 `kind` 与是否含容器选择
- 读取、增量修改、渲染、校验、对比

**样式**

- 四套 palette（`enterprise` / `muted` / `technical` / `midnight`），按角色着色
- 六档字号层级，可由 `typography.baseSize` 整体缩放
- 页面背景色写入 `pages.xml`
- 字体族注册到 `document.xml` 的 `FaceName`

**纸张**

- `canvas.mode=paper` 吸附标准纸张（A5/A4/A3/A2/Tabloid，按需旋转）
- `canvas.mode=fixed` 采纳指定尺寸；放不下时报 `canvas.exceeded`

**质量检测**

- 检测器覆盖布局、文字与连接问题，其中一部分为 error 级（令 `hardConstraintsPassed=false`）；
  具体类别以 `LayoutAnalyzer.cs` 为准
- 覆盖 node↔node、group↔group、group↔child、label↔node、label↔label、
  text↔group、text↔node、text↔text

**协议诚实性**

- 做不到的请求进入 `unmetConstraints`，不静默丢弃
- `appliedDesignNotes` 说明实际执行了什么（含推断出的方向、选用的纸张）
- `specVersion` / `irVersion` 主版本不匹配时拒绝

### 已知限制

- **时序图**：模型层无法表达「生命线上的第 N 条消息」，需新布局策略
- **状态机**：可绘制，但无初态/终态/复合状态语义
- **对比度**：40 个角色色**全部达 WCAG AA**（最低 4.52:1）
- **A4 容不下**参考场景的内容量，需 A3
- 详细清单见 `docs/capability-boundaries.md`

### 统计（`v0.9.0` 时点的快照）

| 项 | 值 |
| --- | --- |
| 测试 | 755 项通过 |
| 基准 | 18 个计分用例 + 8 个故障注入用例 |
| 真实场景 | 4 种结构 |
| 外部依赖 | 7 个包（含测试） |

> 这些数字对应 tag `v0.9.0`，不再代表当前树。当前值用 `dotnet test` 与
> `ls examples/realworld/*.ir.json | wc -l` 查。
