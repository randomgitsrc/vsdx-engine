# 研究索引

**用途：** 登记所有外部研究、来源与实验，使后续 Agent 能独立复核。
**依据 Goal：** `goal-2.1.md` §8.3（资料来源）、§8.6（研究沉淀）

**状态分类**（Goal §2.4）：`Verified` / `Partially Verified` / `Hypothesis` / `Proposed` / `Superseded`

---

## 一、外部来源登记

| ID | 来源 | 类型 | 获取日期 | 用于 | 可靠度 |
| --- | --- | --- | --- | --- | --- |
| S1 | [C4 model 官方评审清单](https://c4model.com/diagrams/checklist) | 一手，作者 Simon Brown，CC BY 4.0 | 2026-11-19 | 专业图的**可判定评审问题** | 高 |
| S2 | [C4 model 首页](https://c4model.com/) | 一手 | 2026-11-19 | 确认 C4 是分层抽象而非复杂度分级 | 高 |
| S3 | [AWS 官方布局指南（awslabs）](https://raw.githubusercontent.com/awslabs/agent-plugins/main/plugins/deploy-on-aws/skills/aws-architecture-diagram/references/layout-guidelines.md) | 一手，AWS 官方仓库 | 2026-11-19 | 量化间距阈值、13+ 服务规则、边路由规则 | 高 |
| S4 | [Elsevier 制图规范](https://www.elsevier.com/en-gb/about/policies-and-standards/author/artwork-and-media-instructions/artwork-sizing) | 一手，学术出版方 | 2026-11-19 | 物理可读性下限（正文 7 pt，最小 6 pt） | 中高（原文自称 rule-of-thumb）|

### 1.1 来源的关键内容摘录

**S1（C4 评审清单）** —— 提供可判定的检查项：

- 图是否有标题、图类型、图范围、图例
- 每个元素是否有名称、类型、用途、技术选型
- 所有缩写、颜色、形状、图标、边框样式、元素尺寸的含义是否可理解
- **每条箭头是否有描述意图的标签**，方向是否与描述一致
- 所有箭头、线型的含义是否可理解

**S3（AWS 布局指南）** —— 提供量化阈值：

- 120px 服务容器之间：水平 180px / 垂直 120px
- **13+ 服务**：水平 220px / 垂直 160px
- 箭头端点与标签文字 30px 净距
- 一个服务多条出边**必须从不同侧面引出**
- 非相邻连接**必须加 waypoint 绕开中间容器**
- 每张图决策点最多 **1–2 个**
- 辅助服务仅 CloudWatch/CloudTrail/X-Ray/IAM，且无连线、放虚线框

**S4（Elsevier）** —— 印刷可读性：

> "lettering on the artwork should have a finished, printed size of **7 pt** for normal text and no smaller than **6 pt** for subscript and superscript characters. Smaller lettering will yield text that is hardly legible."

---

## 二、研究文档

| 文档 | 状态 | 研究的 Goal 条款 |
| --- | --- | --- |
| [`../diagram-knowledge/diagram-complexity-levels.md`](../diagram-knowledge/diagram-complexity-levels.md) | Proposed | §4.3 分级校准 |
| [`../diagram-knowledge/output-profiles.md`](../diagram-knowledge/output-profiles.md) | Proposed | §6.1–6.4 输出适配与可读性 |
| [`../product/management-consistency-report.md`](../product/management-consistency-report.md) | Confirmed（事实核对）| §12 文件治理、§15.1 阶段 0 |

---

## 三、实验记录

**当前无已完成的实验。** 目录 `experiments/` 为空。

### 3.1 建议的下一批实验（按 Goal §8.5 的要求设计）

| 编号 | 问题 | 输入 | 预期证据 | 优先级 |
| --- | --- | --- | --- | --- |
| E1 | 通条（band）能否用现有模型表达 | 一个跨 3 层的横向条 | 产出的 VSDX 是否保持跨容器关系 | 高 |
| E2 | 多文本区组件（标题/说明/状态） | 单组件含 3 个文本区 | 是否只能拆成 3 个独立 TextBlock | 高 |
| E3 | A4 下多少组件会突破 7 pt | 递增组件数的同一架构 | 成品字号随密度的实测曲线 | 高 |
| E4 | 嵌套容器在 Visio 中的真实层级 | 两层嵌套容器 | 本机无法验证（L4 Blocked）| — |
| E5 | 图标作为组件主体的可行性 | 一个 FreePath 作图标 + 文字 | 渲染与读回是否保持 | 中 |

**E4 标记为 Blocked**：需 Microsoft Visio，本机不具备（Goal §9.2 要求如实标记）。

---

## 四、证据缺口（明确记录，不掩盖）

| 缺口 | 影响 | 状态 |
| --- | --- | --- |
| **无任何外部真实 VSDX 样本** | Level 校准缺少一手案例；只能依赖文字描述的来源 | Blocked（需外部文件或 Visio）|
| **未覆盖企业架构框架**（TOGAF / ArchiMate） | 分级可能遗漏企业 IT 架构的特定表达 | 待研究 |
| **Web 输出无权威来源** | 该部分的密度阈值是推断 | Hypothesis |
| **7pt 来自单一来源** | 阈值可能偏保守或偏松 | Partially Verified |
| **无 Visio，L4 全部 Blocked** | 所有"原生可编辑"断言无证据 | Blocked |

---

## 五、维护规则

1. 新增外部来源 → 登记到 §一，标注获取日期与可靠度
2. 来源失效或被新结论替代 → 状态改 `Superseded`，**不删除**
3. 实验完成 → 在 `experiments/` 下建文件，并在 §三 登记
4. 本索引与 `docs/` 下的研究文档**双向链接**
