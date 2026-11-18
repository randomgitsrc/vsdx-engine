# Changelog

本文件记录对外可见的能力变化。格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)。

## [Unreleased]

> **关于版本号：** `Directory.Build.props` 与 `vsdx-tool version` 目前都报 `1.0.0`，
> 但该版本**尚未发布**（无 git tag、无 GitHub Release）。
> 发布动作按章程 §3.2 需项目所有者批准，见 `docs/RELEASE-PROPOSAL.md`。
> 若 L4 仍未验证，建议以 `0.9.0` 发布而非 `1.0.0`。

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

- 23 类问题，其中 7 类为 error 级（令 `hardConstraintsPassed=false`）
- 覆盖 node↔node、group↔group、group↔child、label↔node、label↔label、
  text↔group、text↔node、text↔text

**协议诚实性**

- 做不到的请求进入 `unmetConstraints`，不静默丢弃
- `appliedDesignNotes` 说明实际执行了什么（含推断出的方向、选用的纸张）
- `specVersion` / `irVersion` 主版本不匹配时拒绝

### 已知限制

- **时序图**：模型层无法表达「生命线上的第 N 条消息」，需新布局策略
- **状态机**：可绘制，但无初态/终态/复合状态语义
- **对比度**：40 个角色色中 37 个达 WCAG AA，3 个为 4.36:1（`enterprise` 的
  `database`/`queue`/`storage`）。是否调整属设计决策
- **A4 容不下**参考场景的内容量，需 A3
- 详细清单见 `docs/capability-boundaries.md`

### 统计

| 项 | 值 |
| --- | --- |
| 测试 | 755 项通过 |
| 基准 | 18 个计分用例 + 8 个故障注入用例 |
| 真实场景 | 4 种结构 |
| 外部依赖 | 7 个包（含测试） |
