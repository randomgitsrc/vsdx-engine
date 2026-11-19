# 管理文件一致性报告

**日期：** 2026-11-19
**基线提交：** `85f6697`（`main`，266 commits，tag `v0.9.0`）
**依据 Goal：** `goal-2.1.md` §12.2 信息权威层级、§12.3 权威文件职责、§15.1 阶段 0
**方法：** 逐份读取 + 与实际代码/产物核对，**不采信任何文件的自我声明**

---

## 一、结论摘要

**Goal 2.1 与现有项目文件不矛盾，但现有文件之间存在三类问题：**

| 类型 | 数量 | 严重度 |
| --- | --- | --- |
| **冲突**：两份文件对同一事实给出不同说法 | 4 处 | 高——读者无法判断该信哪个 |
| **遗漏**：Goal 要求的权威文件不存在 | 6 类 | 高——知识无处沉淀 |
| **重复**：同一职责分散在多处 | 2 处 | 中——更新时容易只改一处 |

**没有发现 Goal 2.1 与 `PROJECT-CHARTER.md` v1.0 的实质性冲突**——后者是前者的真子集。

---

## 二、现有管理文件清单

| 文件 | 大小 | 声称的职责 | 实际状态 |
| --- | --- | --- | --- |
| `goal-2.1.md` | 48 KB | 长期章程 | ✅ 完整，1853 行，18 章 |
| `PROJECT-CHARTER.md` | 3.7 KB | 长期产品目标与边界 | ⚠️ 基于旧 Goal `0905`，非 2.1 |
| `PROJECT-STATUS.md` | 7.8 KB | 当前真实状态 | ⚠️ 含过时数字（见 §三.1） |
| `ROADMAP.md` | 5.8 KB | 阶段路线 | ❌ 用 A/B/C/D，与 Goal 的 0–5 不兼容 |
| `AGENTS.md` | 3.2 KB | Agent 入口 | ✅ 结构合理，缺知识导航 |
| `README.md` | 16.5 KB | 对外说明 | ✅ 已核对（轮 79） |
| `CHANGELOG.md` | 4.7 KB | 版本变更 | ✅ 已含 `[Unreleased]`（轮 88） |
| `ENGINEERING-HEALTH.md` | 7.7 KB | 工程健康 | ⚠️ 与 PROJECT-STATUS 职责重叠 |
| `TASK-REGISTER.md` | 6.2 KB | 任务登记 | ⚠️ 归属不明（是路线还是缺陷？） |

---

## 三、冲突（4 处）

### 3.1 `ROADMAP.md` 说 L4 是"唯一未解的阻塞"，`known-issues.md` 列了 7 项

| 文件 | 说法 | 核实方式 |
| --- | --- | --- |
| `ROADMAP.md` | 「L4……这是**唯一未解的阻塞**」 | `grep -n "唯一未解" ROADMAP.md` |
| `docs/phase8/known-issues.md` | 列出 **7 项**未解 | `grep -cE "^## [0-9]+\." docs/phase8/known-issues.md` |

**影响**：读者先读 ROADMAP，会以为除了 L4 一切就绪。

**注意**：`PROJECT-STATUS.md` 中**既没有"未解"也没有指向 `known-issues.md`**
（`grep -c "未解\|known-issues" PROJECT-STATUS.md` 为 0）——本报告初稿声称它"提到 1 处"，**该行是虚构的，已删除**。

**建议**：ROADMAP 改为"L4 是唯一需要**外部环境**的阻塞"，并指向 `known-issues.md`。

### 3.2 `ROADMAP.md` 内部矛盾

同一份文件里：

- 正文：「**已完成的**：……风格系统（阶段 B）」`grep -n "已完成的" ROADMAP.md`
- 状态图：「`[未开始]`──── 设计能力（风格系统）」`grep -n "未开始" ROADMAP.md`

**影响**：无法判断风格系统到底做没做。

**实测**：调色板、字号层级、容器色带**都已实现**（`Palettes.cs`、`TypeScale`），
但"设计能力"若指自动选择配色/构图，则**确实未做**。**两者都对，但混用了同一个词。**

### 3.3 两套阶段编号并存

| 来源 | 阶段划分 |
| --- | --- |
| Goal 2.1 §15 | 阶段 0–5（事实基线 / 专业制图 / 页面适配 / VSDX 可靠性 / 产品化 / 维护）|
| `ROADMAP.md` | 阶段 A–D（真实制图验证 / 风格系统 / L4 / 工程债）|

**影响**：无法把现有进度映射到 Goal 的阶段。

**映射尝试**：

| ROADMAP 阶段 | 对应 Goal 阶段 | 状态 |
| --- | --- | --- |
| A 真实制图验证 | 阶段 1（部分） | 做过 6 个场景，但**未按 Goal 的 Level 校准** |
| B 风格系统 | 阶段 1（部分） | 参数系统已实现，**设计决策未实现** |
| C L4 | 阶段 3 | Blocked |
| D 工程债 | 无对应 | 应并入阶段 0 或持续维护 |

### 3.4 `PROJECT-CHARTER.md` 基于旧 Goal

- 文件头写：**"依据：项目目标文档 `goal-20261118-0905.md`"**
- 实际长期章程是 `goal-2.1.md`

**影响**：读者可能以为 2.1 只是补充，不知它是替代。

---

### 3.5 【最严重】`ROADMAP.md` 声称"阶段 A 已完成"，实际七步工作流只验证了 4 步

`ROADMAP.md` 写「阶段 A：真实制图验证 ✅ **已完成**」（`grep -n "阶段 A" ROADMAP.md`），并称：

> 「由 Agent 按 Skill 指导独立完成制图（不含我手工调 JSON）」

而同一仓库的 `docs/phase8/phase8.3-realworld-validation.md` 自述（`grep -n "自然语言" docs/phase8/phase8.3-realworld-validation.md`）：

| 步骤 | 该报告的实际状态 |
| --- | --- |
| 1 自然语言 → 需求 | **手工转为 JSON（未测 Agent 语义理解）** |
| 2 判断图形类型 | ✅ |
| 3 自主构图配色 | ✅ |
| 4 Engine 转 VSDX | ✅ |
| 5 检查渲染并修改 | ✅（受路由限制）|
| 6 Visio 打开编辑 | ⬜ **未验证** |
| 7 人工改后 Agent 续改 | ⬜ **未验证** |

另一处更明确：「**未测**自然语言 → IR 的自动转换（本次手工写 JSON）」

**两个问题叠加**：

1. **ROADMAP 的描述与证据不符**——「由 Agent 独立完成」不成立，IR 是手写的
2. **Goal §1.3 第 1 条成功标准从未被测**——「Agent 能否通过明确接口完成真实制图任务」

**这不是小事**：Goal §1.2 明确七步工作流「缺一不可」，其中第 1 步是入口。

**建议更正 ROADMAP 的措辞为**：「阶段 A：Engine 执行能力验证 ✅ 已完成（IR 手工编写）」，并把"Agent 自主制图"列为阶段 1 的待验证项。

### 3.6 示例分布与 Goal 的优先级相反

Goal §3.1 定义三个优先级，实测六个示例的分布：

| Goal 优先级 | 家族 | 示例数 | 示例 |
| --- | --- | --- | --- |
| **第一：A** | 系统与技术架构图 | **1** | `cbo`（kind=architecture）|
| **第一：B** | 网络与基础设施拓扑 | **1** | `deploy-topology`（kind=network）|
| 第二：C | 业务流程与跨职能流程 | **4** | `user-data-flow`、`checkout-sequence`、`order-swimlane`（kind=workflow）|
| — | 组织架构 | 1 | `org-tree`（kind=org，**不在 Goal §3.1 的清单中**）|

**问题**：Goal 把「系统与技术架构图」列为第一优先级，但只有 1 个示例；
第三优先级的流程有 4 个。`org-tree` 甚至不在 Goal 的图形家族清单里。

**原因**（推测）：这 6 个示例是在 Goal 2.1 之前建立的，当时的优先级不同。

**建议**：阶段 1 补足架构类示例——至少覆盖 Goal §3.1 A 的 7 个子类
（分层/模块/微服务/企业应用/技术平台/部署/数据平台）。

### 3.8 【Reviewer 补充，已核实】两处更硬的冲突

| # | 冲突 | 证据 |
| --- | --- | --- |
| a | 「25/25 已修复」vs「24 / 25 已修复」 | `grep -n "25/25" PROJECT-STATUS.md`、`grep -n "24 / 25" TASK-REGISTER.md` |
| b | **证据链断裂**：25/25 的唯一来源文件不存在 | `PROJECT-STATUS.md` 指向 `docs/phase8/phase8.3-defect-register.md`，`ls` 报不存在 |

(a) 是纯数值矛盾，同一事实两个数字。
(b) 更严重——Goal §9.1 要求能力声明可追溯，而「25/25 已修复」**没有可追溯的来源**。

**另**：`ROADMAP.md` 与 `PROJECT-STATUS.md` 多处引用「目标文档 §5」「目标文档 §3」，
但 `goal-2.1.md` 中 `grep -c "25 项"` 为 0——引用的是**已废弃的旧 Goal**。

---

## 四、遗漏（Goal 要求但不存在）

| Goal 条款 | 要求 | 现状 |
| --- | --- | --- |
| §4.1–4.5 | **专业制图知识基线** | ❌ 不存在（本阶段已补 `docs/diagram-knowledge/`）|
| §4.5 | **案例库** | ❌ 不存在（目录已建，无案例）|
| §6.1 | **Output Profile** | ❌ 不存在（本阶段已出研究稿）|
| §8.6 | **研究索引与实验记录** | ❌ 不存在（`docs/research/` 已建，无内容）|
| §9.1 | **真实能力矩阵** | ⚠️ 有 `docs/phase8/capability-matrix.md`，但埋在阶段目录里，非权威位置 |
| §12.3 | **权威文件职责表** | ❌ 不存在——没人定义"哪份文件对哪个问题有最终解释权" |
| §13 | **知识同步协议** | ❌ 不存在 |
| §3.2 | **Figure-Agent 的设计指导** | ⚠️ `skill/SKILL.md` 是**操作手册**（怎么用 CLI），只有一张 4 行的家族选择表；**没有**教"如何决定层数、如何组织信息、如何判断视觉层次"——这些正是 Goal 分配给 Figure-Agent 的职责 |

### 3.7 Goal 2.1 从未提到 benchmark，而项目的验证体系以它为核心

全文检索 `goal-2.1.md`：**"benchmark" 0 次，"基准" 0 次。**

Goal 认可的验证方式是（§1.3 第 9 条、§4.4、§9.1）：

> 「产品能力是否能够通过**可重复的真实任务和证据**证明」
> 「搜集**真实专业架构图案例**」
> 「能力矩阵记录……**真实案例**」

而当前项目的主要回归手段是 `benchmarks/` 下的**合成**用例（`ls benchmarks/*/*.json | wc -l`，6 家族 × 3 复杂度）。

**这不是说 benchmark 无用**——它们能防回归、能测规模。问题是：

| 用途 | benchmark 是否胜任 |
| --- | --- |
| 防代码回归 | ✅ 胜任 |
| 证明"能画专业图" | ❌ **不胜任**——它们是合成的，不是专业架构师画的 |

**风险**：过去 60 轮里，我曾用"18/18 基准通过"作为能力证据。按 Goal 的标准，
这只是**代码没坏**的证据，不是**产品有价值**的证据。

**建议**：benchmark 保留为回归工具，但**不得出现在"能力证明"的陈述中**；
能力证明只能用真实案例（`examples/realworld/` 的 6 个也算，但需补架构类）。

---

## 五、重复（2 处）

| 职责 | 分散在 | 建议 |
| --- | --- | --- |
| 当前状态 | `PROJECT-STATUS.md` + `ENGINEERING-HEALTH.md` + `TASK-REGISTER.md` | 收敛为一份状态 + 一份健康报告，后者只谈工程指标 |
| 缺陷/任务 | `TASK-REGISTER.md` + `docs/phase8/known-issues.md` + `docs/phase8/phase8.3-candidates.md` | 缺陷进 `known-issues`，任务进 `ROADMAP`，候选进研究目录 |

---

## 六、Goal 2.1 自身的可执行性问题（3 处）

**Goal 是方向与约束，不是全知文件。以下是我读完后认为需要澄清的：**

### 6.1 §4.3 的 Level 分级混合了两个维度

详见 `docs/diagram-knowledge/diagram-complexity-levels.md`。**建议拆为 E（表达）与 D（布局）双维度。**

### 6.2 §9.3 独立评审在单 Agent 环境下的可行性

Goal 要求「应尽可能让 Reviewer 不依赖实施者的结论」。
本环境**无独立 Reviewer Agent**，只能用 Subagent 或自查清单。

**建议**：明确记录"本次评审的独立程度"，而不是含糊声称"已独立评审"。

### 6.3 §6.1 要求 Web 输出，但无验收标准

Goal 列出 Web/Screen，但 §6.4 的检查项全部基于纸张（纸张利用率、是否溢出）。
Web 没有对应的可判定标准。

**建议**：要么定义 Web 的验收项，要么明确"Web 输出本期不做"。

---

## 七、建议的目标文件结构

**在现有文件基础上调整，不推倒重来**（Goal §17.3 禁止无依据重构）：

```
vsdx-engine/
├── AGENTS.md                    # 保留，增加"知识导航"章节
├── PROJECT-CHARTER.md           # 更新：声明基于 goal-2.1，补职责边界
├── PROJECT-STATUS.md            # 保留，作为唯一"当前状态"权威
├── ROADMAP.md                   # 重写：改用 Goal 的阶段 0–5
├── goal-2.1.md                  # **纳入版本控制**（当前未跟踪）
│
├── docs/
│   ├── product/
│   │   └── diagram-domain-scope.md      # 新建：§3.1 的范围细化
│   ├── diagram-knowledge/               # **本阶段已建**
│   │   ├── diagram-complexity-levels.md ✅
│   │   ├── output-profiles.md           ✅
│   │   ├── architecture-diagrams.md     ⬜ 待写
│   │   ├── visual-quality.md            ⬜ 待写
│   │   └── case-studies/                ⬜ 空
│   ├── research/
│   │   ├── index.md                     ⬜ 待写
│   │   └── experiments/                 ⬜ 空
│   ├── quality/
│   │   └── capability-matrix.md         ⬜ 应从 phase8/ 迁出
│   ├── plans/active/                    ⬜ 空
│   ├── architecture/                    ⬜ 空（现有 docs/architecture.md 可迁入）
│   └── phase7/ phase8/                  # 保留为历史档案，加 Snapshot 头
│
├── ENGINEERING-HEALTH.md        # 保留，收窄为纯工程指标
└── TASK-REGISTER.md             # 建议合并进 ROADMAP 后归档
```

---

## 八、处置建议（按优先级）

| 优先级 | 动作 | 理由 | 是否需所有者批准 |
| --- | --- | --- | --- |
| **P0** | `goal-2.1.md` 纳入 Git | 章程必须在仓库里，否则后续 Agent 找不到 | 否（事实修正） |
| **P0** | 修 `ROADMAP.md` 的"唯一阻塞"表述 | 直接误导读者 | 否 |
| **P1** | `ROADMAP.md` 改用 Goal 阶段 0–5 | 否则无法映射进度 | 否 |
| **P1** | `PROJECT-CHARTER.md` 声明基于 2.1 | 版本关系不明 | 否 |
| **P2** | 建 `capability-matrix.md` 权威位置 | Goal §9.1 明确要求 | 否 |
| **P2** | 修 ROADMAP 内部矛盾（§3.2） | 同文件自相矛盾 | 否 |
| **P3** | 合并 `TASK-REGISTER.md` | 职责重复 | 是（涉及删除文件）|

**本轮不执行这些改动**——按你的要求"暂时不要立即重构现有项目管理文件"。

---

## 九、本报告的局限

1. **未读 `docs/phase7/` 全部 9 份文件**，只检查了它们与当前状态的关系（历史档案，不改）。
2. **`ENGINEERING-HEALTH.md` 未逐条核实**，只判断了职责重叠。
3. **"是否需要所有者批准"是我的判断**，依据 Goal §11.1–11.3 的分类，可能与所有者理解不同。
